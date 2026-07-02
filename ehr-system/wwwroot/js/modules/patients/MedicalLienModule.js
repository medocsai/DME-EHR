/**
 * MedicalLienModule - Medical Lien Form functionality
 *
 * Handles provider selection, form preview, and PDF generation for Medical Lien Forms.
 * This module supports both the edit patient modal and the patient detail view modal.
 */

class MedicalLienModule {
    constructor(options = {}) {
        this.api = options.api || null;
        this.currentPatientId = null;
        this.providers = [];
        this.selectedProviderId = null;
        this.formData = null;
        this.isInitialized = false;
        // Track which modal context we're in: 'edit' or 'view'
        this.currentContext = null;
    }

    /**
     * Initialize the module for the edit modal
     */
    init() {
        if (this.isInitialized) return;

        this._bindEditModalEvents();
        this.isInitialized = true;
    }

    /**
     * Bind event handlers for the edit modal
     * @private
     */
    _bindEditModalEvents() {
        // Provider selection change in edit modal
        const providerSelect = document.getElementById('medicalLienProviderSelect');
        if (providerSelect) {
            providerSelect.addEventListener('change', (e) => this._handleProviderChange(e, 'edit'));
        }

        // Tab shown event - load data when Medical Lien tab is shown in edit modal
        const medicalLienTab = document.getElementById('medical-lien-tab');
        if (medicalLienTab) {
            medicalLienTab.addEventListener('shown.bs.tab', () => this._onEditTabShown());
        }
    }

    /**
     * Initialize for the patient detail view modal
     * Called when the Medical Lien tab is shown in the view modal
     * @param {number} patientId - Patient ID
     */
    async initializeForViewModal(patientId) {
        this.currentPatientId = patientId;
        this.currentContext = 'view';
        this.selectedProviderId = null;

        // Bind provider select event for view modal
        const viewProviderSelect = document.getElementById('viewMedicalLienProviderSelect');
        if (viewProviderSelect) {
            // Remove existing listener if any
            viewProviderSelect.removeEventListener('change', this._viewProviderChangeHandler);
            // Add new listener
            this._viewProviderChangeHandler = (e) => this._handleProviderChange(e, 'view');
            viewProviderSelect.addEventListener('change', this._viewProviderChangeHandler);
        }

        // Load providers if not already loaded
        if (this.providers.length === 0) {
            await this.loadProviders();
        }

        // Populate the view modal dropdown
        this._populateViewProviderDropdown();

        // Load preview data (clinic info from server)
        await this._loadPreviewDataForView();
    }

    /**
     * Set the current patient ID and load data if tab is visible (edit modal)
     * @param {number} patientId - Patient ID
     */
    setPatientId(patientId) {
        this.currentPatientId = patientId;
        this.currentContext = 'edit';

        // Update visibility based on whether patient exists
        const newPatientWarning = document.getElementById('medicalLienNewPatient');
        const content = document.getElementById('medicalLienContent');

        if (!patientId) {
            if (newPatientWarning) newPatientWarning.style.display = 'block';
            if (content) content.style.display = 'none';
        } else {
            if (newPatientWarning) newPatientWarning.style.display = 'none';
            if (content) content.style.display = 'block';
        }
    }

    /**
     * Handle tab shown event in edit modal
     * @private
     */
    async _onEditTabShown() {
        const patientIdInput = document.getElementById('patientId');
        const patientId = patientIdInput?.value;

        if (!patientId) {
            this.setPatientId(null);
            return;
        }

        this.currentPatientId = parseInt(patientId);
        this.currentContext = 'edit';
        this.setPatientId(this.currentPatientId);

        // Load providers if not already loaded
        if (this.providers.length === 0) {
            await this.loadProviders();
        }

        // Populate dropdown for edit modal
        this._populateProviderDropdown();

        // Load form preview data
        await this._loadPreviewData();
    }

    /**
     * Load providers for dropdown
     */
    async loadProviders() {
        try {
            const response = await this._apiGet('/api/medicallien/providers');
            this.providers = response || [];
        } catch (error) {
            console.error('[MedicalLienModule] Failed to load providers:', error);
            this._showError('Failed to load providers. Please try again.', this.currentContext);
        }
    }

    /**
     * Populate provider dropdown (edit modal)
     * @private
     */
    _populateProviderDropdown() {
        const select = document.getElementById('medicalLienProviderSelect');
        if (!select) return;

        // Clear existing options except first
        select.innerHTML = '<option value="">Select a provider...</option>';

        this.providers.forEach(provider => {
            const option = document.createElement('option');
            option.value = provider.ProviderId;
            option.textContent = provider.Credentials
                ? `${provider.Name}, ${provider.Credentials}`
                : provider.Name;
            option.dataset.hasSignature = provider.HasSignature;
            select.appendChild(option);
        });
    }

    /**
     * Populate provider dropdown (view modal)
     * @private
     */
    _populateViewProviderDropdown() {
        const select = document.getElementById('viewMedicalLienProviderSelect');
        if (!select) return;

        // Clear existing options except first
        select.innerHTML = '<option value="">Select a provider...</option>';

        this.providers.forEach(provider => {
            const option = document.createElement('option');
            option.value = provider.ProviderId;
            option.textContent = provider.Credentials
                ? `${provider.Name}, ${provider.Credentials}`
                : provider.Name;
            option.dataset.hasSignature = provider.HasSignature;
            select.appendChild(option);
        });
    }

    /**
     * Handle provider selection change
     * @param {Event} e - Change event
     * @param {string} context - 'edit' or 'view'
     * @private
     */
    async _handleProviderChange(e, context) {
        const providerId = e.target.value;
        this.selectedProviderId = providerId ? parseInt(providerId) : null;
        this.currentContext = context;

        // Update signature status indicator
        this._updateSignatureStatus(context);

        // Load preview data if provider is selected
        if (this.selectedProviderId && this.currentPatientId) {
            if (context === 'view') {
                await this._loadPreviewDataForView();
            } else {
                await this._loadPreviewData();
            }
        }
    }

    /**
     * Update signature status indicator
     * @param {string} context - 'edit' or 'view'
     * @private
     */
    _updateSignatureStatus(context) {
        const statusElId = context === 'view' ? 'viewMedicalLienProviderSignatureStatus' : 'medicalLienProviderSignatureStatus';
        const statusEl = document.getElementById(statusElId);
        if (!statusEl) return;

        if (!this.selectedProviderId) {
            statusEl.innerHTML = '';
            return;
        }

        const provider = this.providers.find(p => p.ProviderId === this.selectedProviderId);
        if (provider?.HasSignature) {
            statusEl.innerHTML = '<span class="text-success"><i class="bi bi-check-circle me-1"></i>Signature available</span>';
        } else {
            statusEl.innerHTML = '<span class="text-warning"><i class="bi bi-exclamation-triangle me-1"></i>No signature on file</span>';
        }
    }

    /**
     * Load preview data for the form (edit modal)
     * @private
     */
    async _loadPreviewData() {
        if (!this.currentPatientId) return;

        try {
            // Use selected provider or default to first provider
            const providerId = this.selectedProviderId || (this.providers.length > 0 ? this.providers[0].ProviderId : null);

            if (!providerId) {
                this._updatePreviewFromPatientForm();
                return;
            }

            const response = await this._apiGet(`/api/medicallien/preview/${this.currentPatientId}?providerId=${providerId}`);
            this.formData = response;
            this._updatePreviewDisplay('edit');
        } catch (error) {
            console.error('[MedicalLienModule] Failed to load preview:', error);
            // Fall back to getting data from the patient form
            this._updatePreviewFromPatientForm();
        }
    }

    /**
     * Load preview data for view modal
     * @private
     */
    async _loadPreviewDataForView() {
        if (!this.currentPatientId) return;

        try {
            // Use selected provider or default to first provider
            const providerId = this.selectedProviderId || (this.providers.length > 0 ? this.providers[0].ProviderId : null);

            if (!providerId) {
                // Just update clinic info from server if possible
                return;
            }

            const response = await this._apiGet(`/api/medicallien/preview/${this.currentPatientId}?providerId=${providerId}`);
            this.formData = response;
            this._updatePreviewDisplay('view');
        } catch (error) {
            console.error('[MedicalLienModule] Failed to load preview for view:', error);
        }
    }

    /**
     * Update preview from patient form fields (fallback for edit modal)
     * @private
     */
    _updatePreviewFromPatientForm() {
        const form = document.getElementById('patientForm');
        if (!form) return;

        const firstName = form.querySelector('[name="FirstName"]')?.value || '';
        const lastName = form.querySelector('[name="LastName"]')?.value || '';
        const address = form.querySelector('[name="Address"]')?.value || '';
        const city = form.querySelector('[name="City"]')?.value || '';
        const state = form.querySelector('[name="State"]')?.value || '';
        const zipCode = form.querySelector('[name="ZipCode"]')?.value || '';
        const dateOfInjury = form.querySelector('[name="DateOfInjury"]')?.value || '';

        // Update preview fields
        this._setPreviewField('lienPreviewPatientName', `${firstName} ${lastName}`.trim() || '-');
        this._setPreviewField('lienPreviewPatientAddress', this._formatAddress(address, city, state, zipCode));
        this._setPreviewField('lienPreviewDateOfInjury', dateOfInjury ? this._formatDate(dateOfInjury) : 'Not specified');

        // Attorney info from insurance (try to get from form)
        const attorneyName = form.querySelector('[name="PrimaryInsurance.AttorneyName"]')?.value || '';
        const attorneyPhone = form.querySelector('[name="PrimaryInsurance.AttorneyPhone"]')?.value || '';
        const attorneyEmail = form.querySelector('[name="PrimaryInsurance.AttorneyEmail"]')?.value || '';

        this._setPreviewField('lienPreviewAttorneyName', attorneyName || 'Not specified');
        this._setPreviewField('lienPreviewAttorneyPhone', attorneyPhone || 'Not specified');
        this._setPreviewField('lienPreviewAttorneyEmail', attorneyEmail || 'Not specified');

        // Clinic info will come from server
        this._setPreviewField('lienPreviewClinicName', '-');
        this._setPreviewField('lienPreviewClinicAddress', '-');
        this._setPreviewField('lienPreviewClinicContact', '-');
    }

    /**
     * Update preview display with form data
     * @param {string} context - 'edit' or 'view'
     * @private
     */
    _updatePreviewDisplay(context) {
        if (!this.formData) return;

        const prefix = context === 'view' ? 'viewLienPreview' : 'lienPreview';

        // Patient Information
        this._setPreviewField(`${prefix}PatientName`, this.formData.PatientName || '-');
        this._setPreviewField(`${prefix}PatientAddress`, this.formData.PatientFullAddress || '-');
        this._setPreviewField(`${prefix}DateOfInjury`,
            this.formData.DateOfInjury ? this._formatDate(this.formData.DateOfInjury) : 'Not specified');

        // Attorney Information
        this._setPreviewField(`${prefix}AttorneyName`, this.formData.AttorneyName || 'Not specified');
        this._setPreviewField(`${prefix}AttorneyPhone`, this.formData.AttorneyPhone || 'Not specified');
        this._setPreviewField(`${prefix}AttorneyEmail`, this.formData.AttorneyEmail || 'Not specified');

        // Clinic Information
        this._setPreviewField(`${prefix}ClinicName`, this.formData.ClinicName || '-');
        this._setPreviewField(`${prefix}ClinicAddress`, this.formData.ClinicFullAddress || '-');

        const contactParts = [];
        if (this.formData.ClinicPhone) contactParts.push(`P: ${this.formData.ClinicPhone}`);
        if (this.formData.ClinicEmail) contactParts.push(`E: ${this.formData.ClinicEmail}`);
        this._setPreviewField(`${prefix}ClinicContact`, contactParts.join(' | ') || '-');
    }

    /**
     * Set preview field value
     * @param {string} elementId - Element ID
     * @param {string} value - Value to set
     * @private
     */
    _setPreviewField(elementId, value) {
        const el = document.getElementById(elementId);
        if (el) el.textContent = value;
    }

    /**
     * Format address string
     * @param {string} address - Street address
     * @param {string} city - City
     * @param {string} state - State
     * @param {string} zipCode - ZIP code
     * @returns {string} Formatted address
     * @private
     */
    _formatAddress(address, city, state, zipCode) {
        const parts = [];
        if (address) parts.push(address);

        const cityStateZip = [];
        if (city) cityStateZip.push(city);
        if (state) cityStateZip.push(state);

        let cityState = cityStateZip.join(', ');
        if (zipCode) cityState = cityState ? `${cityState} ${zipCode}` : zipCode;

        if (cityState) parts.push(cityState);

        return parts.join(', ') || '-';
    }

    /**
     * Format date string
     * @param {string} dateStr - Date string
     * @returns {string} Formatted date
     * @private
     */
    _formatDate(dateStr) {
        if (!dateStr) return '-';

        try {
            const date = new Date(dateStr);
            return date.toLocaleDateString('en-US', {
                year: 'numeric',
                month: '2-digit',
                day: '2-digit'
            });
        } catch {
            return dateStr;
        }
    }

    /**
     * Generate Medical Lien Form PDF (for edit modal)
     */
    async generatePdf() {
        await this._generatePdfInternal('edit');
    }

    /**
     * Generate Medical Lien Form PDF from view modal
     * @param {number} patientId - Patient ID
     */
    async generatePdfFromView(patientId) {
        this.currentPatientId = patientId;
        this.currentContext = 'view';

        // Get selected provider from view modal dropdown
        const viewProviderSelect = document.getElementById('viewMedicalLienProviderSelect');
        if (viewProviderSelect) {
            this.selectedProviderId = viewProviderSelect.value ? parseInt(viewProviderSelect.value) : null;
        }

        await this._generatePdfInternal('view');
    }

    /**
     * Internal PDF generation logic
     * @param {string} context - 'edit' or 'view'
     * @private
     */
    async _generatePdfInternal(context) {
        if (!this.currentPatientId) {
            this._showError('Patient must be saved before generating the Medical Lien Form.', context);
            return;
        }

        if (!this.selectedProviderId) {
            this._showError('Please select a provider before generating the form.', context);
            return;
        }

        // Get current location ID from location context
        // Priority: window.currentLocation > App.state > localStorage
        let locationId = null;

        // Try window.currentLocation first (set by LocationModule)
        if (window.currentLocation?.LocationId) {
            locationId = window.currentLocation.LocationId;
        }
        // Try App.state.get('currentLocation')
        else if (window.App?.state?.get?.('currentLocation')?.LocationId) {
            locationId = window.App.state.get('currentLocation').LocationId;
        }
        // Try localStorage
        else {
            const storedLocation = localStorage.getItem('currentLocation');
            if (storedLocation) {
                try {
                    const parsed = JSON.parse(storedLocation);
                    locationId = parsed?.LocationId;
                } catch (e) {
                    console.error('[MedicalLienModule] Failed to parse stored location:', e);
                }
            }
        }

        if (!locationId) {
            this._showError('Location is required. Please select a location from the header.', context);
            return;
        }

        this._showGenerating(true, context);
        this._hideError(context);

        try {
            // Make API call to generate PDF
            const url = `/api/medicallien/generate/${this.currentPatientId}?providerId=${this.selectedProviderId}&locationId=${locationId}`;
            const response = await fetch(url, {
                method: 'GET',
                headers: {
                    'Authorization': `Bearer ${this._getAuthToken()}`
                }
            });

            if (!response.ok) {
                const errorData = await response.json().catch(() => ({}));
                throw new Error(errorData.message || 'Failed to generate Medical Lien Form');
            }

            // Get the filename from response headers or use default
            const contentDisposition = response.headers.get('Content-Disposition');
            let filename = 'MedicalLien.pdf';
            if (contentDisposition) {
                const filenameMatch = contentDisposition.match(/filename[^;=\n]*=((['"]).*?\2|[^;\n]*)/);
                if (filenameMatch && filenameMatch[1]) {
                    filename = filenameMatch[1].replace(/['"]/g, '');
                }
            }

            // Download the PDF
            const blob = await response.blob();
            const downloadUrl = window.URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = downloadUrl;
            a.download = filename;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            window.URL.revokeObjectURL(downloadUrl);

            // Show success toast
            if (typeof showToast === 'function') {
                showToast('Medical Lien Form generated successfully', 'success');
            }

        } catch (error) {
            console.error('[MedicalLienModule] PDF generation failed:', error);
            this._showError(error.message || 'Failed to generate Medical Lien Form. Please try again.', context);
        } finally {
            this._showGenerating(false, context);
        }
    }

    /**
     * Show/hide generating indicator
     * @param {boolean} show - Whether to show
     * @param {string} context - 'edit' or 'view'
     * @private
     */
    _showGenerating(show, context) {
        const generatingElId = context === 'view' ? 'viewMedicalLienGenerating' : 'medicalLienGenerating';
        const generateBtnId = context === 'view' ? 'viewGenerateMedicalLienBtn' : 'generateMedicalLienBtn';

        const generatingEl = document.getElementById(generatingElId);
        const generateBtn = document.getElementById(generateBtnId);

        if (generatingEl) {
            generatingEl.classList.toggle('d-none', !show);
        }
        if (generateBtn) {
            generateBtn.disabled = show;
        }
    }

    /**
     * Show error message
     * @param {string} message - Error message
     * @param {string} context - 'edit' or 'view'
     * @private
     */
    _showError(message, context) {
        const errorElId = context === 'view' ? 'viewMedicalLienError' : 'medicalLienError';
        const errorEl = document.getElementById(errorElId);
        if (errorEl) {
            errorEl.textContent = message;
            errorEl.classList.remove('d-none');
        }
    }

    /**
     * Hide error message
     * @param {string} context - 'edit' or 'view'
     * @private
     */
    _hideError(context) {
        const errorElId = context === 'view' ? 'viewMedicalLienError' : 'medicalLienError';
        const errorEl = document.getElementById(errorElId);
        if (errorEl) {
            errorEl.classList.add('d-none');
        }
    }

    /**
     * Get auth token from storage
     * @returns {string} Auth token
     * @private
     */
    _getAuthToken() {
        return localStorage.getItem('authToken') || localStorage.getItem('token') || sessionStorage.getItem('token') || '';
    }

    /**
     * Make API GET request
     * @param {string} url - URL
     * @returns {Promise<any>} Response data
     * @private
     */
    async _apiGet(url) {
        const response = await fetch(url, {
            method: 'GET',
            headers: {
                'Authorization': `Bearer ${this._getAuthToken()}`,
                'Content-Type': 'application/json'
            }
        });

        if (!response.ok) {
            const errorData = await response.json().catch(() => ({}));
            throw new Error(errorData.message || `Request failed: ${response.status}`);
        }

        return response.json();
    }
}

// Create global instance
window.medicalLienModule = new MedicalLienModule();

// Global function for generate button (edit modal)
function generateMedicalLienForm() {
    if (window.medicalLienModule) {
        window.medicalLienModule.generatePdf();
    }
}

// Global function for generate button (view modal)
function generateMedicalLienFormFromView(patientId) {
    if (window.medicalLienModule) {
        window.medicalLienModule.generatePdfFromView(patientId);
    }
}

// Initialize on document ready
document.addEventListener('DOMContentLoaded', function() {
    if (window.medicalLienModule) {
        window.medicalLienModule.init();
    }
});
