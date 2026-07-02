/**
 * ConsentModule - Handles consent form templates and patient consents
 *
 * Features:
 * - Consent template CRUD operations
 * - Kiosk link and QR code generation
 * - Patient consent history
 * - Template preview
 */
class ConsentModule {
    constructor(options = {}) {
        this.api = options.api || null;
        this.eventBus = options.eventBus || null;

        // State
        this.templates = [];
        this.editorInitialized = false;

        // Form type names
        this.formTypeNames = ['New Care Episode', 'Returning Patient', 'HIPAA Notice', 'Financial Agreement', 'Custom'];

        // Bound handlers
        this._boundHandlers = {};
    }

    /**
     * Initialize the module
     */
    async init() {
        this._bindEvents();
    }

    /**
     * Load consent forms page
     */
    async load() {
        const location = this._getCurrentLocation();
        if (!location?.LocationId) {
            console.warn('No current location set - cannot load consent forms page');
            return;
        }

        await Promise.all([
            this.loadKioskDisplay(),
            this.loadTemplates()
        ]);
    }

    /**
     * Load and display kiosk link and QR code
     */
    async loadKioskDisplay() {
        const linkInput = document.getElementById('kioskLinkDisplay');
        const qrContainer = document.getElementById('kioskQrCodeDisplay');

        if (!linkInput || !qrContainer) return;

        const location = this._getCurrentLocation();
        if (!location?.LocationId) {
            linkInput.value = 'No location selected';
            qrContainer.innerHTML = '<div class="text-muted small">Select a location first</div>';
            return;
        }

        try {
            let settings = await this._apiGet(`/kiosk-settings/${location.LocationId}`);

            // Create settings if they don't exist
            if (!settings?.KioskToken) {
                settings = await this._apiPut(`/kiosk-settings/${location.LocationId}`, {
                    IsEnabled: true,
                    SessionTimeoutMinutes: 0
                });
            }

            if (settings?.KioskToken) {
                // Use the correct kiosk URL route
                const kioskUrl = `${window.location.origin}/Kiosk?token=${settings.KioskToken}`;
                linkInput.value = kioskUrl;
                this._generateQrCode(kioskUrl);
            } else {
                linkInput.value = 'Error generating kiosk link';
                qrContainer.innerHTML = '<div class="text-danger small">Failed to generate QR code</div>';
            }
        } catch (error) {
            console.error('Error loading kiosk display:', error);
            linkInput.value = 'Error loading kiosk settings';
            qrContainer.innerHTML = '<div class="text-danger small">Error loading QR code</div>';
        }
    }

    /**
     * Copy kiosk link to clipboard
     */
    copyKioskLink() {
        const linkInput = document.getElementById('kioskLinkDisplay');
        if (!linkInput || !linkInput.value || linkInput.value.startsWith('Error') || linkInput.value.startsWith('No location')) {
            this._showToast('Error', 'No valid kiosk link to copy', 'error');
            return;
        }

        navigator.clipboard.writeText(linkInput.value).then(() => {
            this._showToast('Copied', 'Kiosk URL copied to clipboard');
        }).catch(() => {
            linkInput.select();
            document.execCommand('copy');
            this._showToast('Copied', 'Kiosk URL copied to clipboard');
        });
    }

    /**
     * Open kiosk in new tab
     */
    openKioskInNewTab() {
        const linkInput = document.getElementById('kioskLinkDisplay');
        if (!linkInput || !linkInput.value || linkInput.value.startsWith('Error') || linkInput.value.startsWith('No location')) {
            this._showToast('Error', 'No valid kiosk link to open', 'error');
            return;
        }
        window.open(linkInput.value, '_blank');
    }

    /**
     * Load consent templates
     */
    async loadTemplates() {
        const tbody = document.getElementById('consentTemplatesTableBody');
        if (!tbody) return;

        const location = this._getCurrentLocation();
        if (!location?.LocationId) {
            tbody.innerHTML = `
                <tr>
                    <td colspan="6" class="text-center py-4 text-muted">
                        Please select a location to view consent templates.
                    </td>
                </tr>
            `;
            return;
        }

        try {
            this.templates = await this._apiGet(`/consent-templates?locationId=${location.LocationId}`);

            if (this.templates.length === 0) {
                tbody.innerHTML = `
                    <tr>
                        <td colspan="6" class="text-center py-4 text-muted">
                            <i class="bi bi-file-earmark-x display-6"></i>
                            <p class="mt-2">No consent templates found for this location. Click "New Template" to create one.</p>
                        </td>
                    </tr>
                `;
                return;
            }

            tbody.innerHTML = this.templates.map(t => this._renderTemplateRow(t)).join('');

        } catch (error) {
            console.error('Error loading consent templates:', error);
            tbody.innerHTML = `
                <tr>
                    <td colspan="6" class="text-center py-4 text-danger">
                        Error loading templates: ${this._escape(error.message)}
                    </td>
                </tr>
            `;
        }
    }

    /**
     * Open modal for new template
     */
    openNewModal() {
        const location = this._getCurrentLocation();
        if (!location?.LocationId) {
            this._showToast('Error', 'Please select a location first', 'error');
            return;
        }

        document.getElementById('consentTemplateId').value = '';
        document.getElementById('consentTemplateName').value = '';
        document.getElementById('consentTemplateFormType').value = '0';
        document.getElementById('consentTemplateLocation').value = location.LocationId;
        document.getElementById('consentTemplateDescription').value = '';
        document.getElementById('consentTemplateOrder').value = '0';
        document.getElementById('consentTemplateActive').checked = true;

        this._setEditorContent('');

        document.getElementById('consentTemplateModalTitle').innerHTML =
            '<i class="bi bi-file-earmark-text me-2"></i>New Consent Template';

        this._initEditor();

        new bootstrap.Modal(document.getElementById('consentTemplateModal')).show();
    }

    /**
     * Edit existing template
     */
    async edit(templateId) {
        try {
            const template = await this._apiGet(`/consent-templates/${templateId}`);
            const location = this._getCurrentLocation();

            document.getElementById('consentTemplateId').value = template.ConsentFormTemplateId;
            document.getElementById('consentTemplateName').value = template.Name;
            document.getElementById('consentTemplateFormType').value = template.FormType;
            document.getElementById('consentTemplateLocation').value = location?.LocationId || template.LocationId || '';
            document.getElementById('consentTemplateDescription').value = template.Description || '';
            document.getElementById('consentTemplateOrder').value = template.DisplayOrder || 0;
            document.getElementById('consentTemplateActive').checked = template.IsActive;

            this._initEditor();
            this._setEditorContent(template.HtmlContent || '');

            document.getElementById('consentTemplateModalTitle').innerHTML =
                '<i class="bi bi-file-earmark-text me-2"></i>Edit Consent Template';

            new bootstrap.Modal(document.getElementById('consentTemplateModal')).show();

        } catch (error) {
            console.error('Error loading template:', error);
            this._showToast('Error', 'Failed to load template', 'error');
        }
    }

    /**
     * Save consent template
     */
    async save(event) {
        if (event) event.preventDefault();

        const templateId = document.getElementById('consentTemplateId').value;
        const isNew = !templateId;
        const htmlContent = this._getEditorContent();

        if (!htmlContent || htmlContent.trim() === '') {
            this._showToast('Error', 'Please enter form content', 'error');
            return;
        }

        const data = {
            Name: document.getElementById('consentTemplateName').value,
            FormType: parseInt(document.getElementById('consentTemplateFormType').value),
            LocationId: document.getElementById('consentTemplateLocation').value || null,
            Description: document.getElementById('consentTemplateDescription').value,
            HtmlContent: htmlContent,
            DisplayOrder: parseInt(document.getElementById('consentTemplateOrder').value) || 0,
            IsActive: document.getElementById('consentTemplateActive').checked
        };

        try {
            if (isNew) {
                await this._apiPost('/consent-templates', data);
                this._showToast('Success', 'Consent template created');
            } else {
                await this._apiPut(`/consent-templates/${templateId}`, data);
                this._showToast('Success', 'Consent template updated');
            }

            bootstrap.Modal.getInstance(document.getElementById('consentTemplateModal'))?.hide();
            await this.loadTemplates();

        } catch (error) {
            console.error('Error saving template:', error);
            this._showToast('Error', error.message || 'Failed to save template', 'error');
        }
    }

    /**
     * Delete consent template
     */
    async delete(templateId, templateName) {
        const confirmed = await this._confirm(
            'Delete Template',
            `Are you sure you want to delete "${templateName}"? This action cannot be undone.`
        );

        if (!confirmed) return;

        try {
            await this._apiDelete(`/consent-templates/${templateId}`);
            this._showToast('Success', 'Template deleted');
            await this.loadTemplates();
        } catch (error) {
            console.error('Error deleting template:', error);
            this._showToast('Error', error.message || 'Failed to delete template', 'error');
        }
    }

    /**
     * Preview template by ID
     */
    async previewById(templateId) {
        try {
            if (!templateId || isNaN(templateId)) {
                console.error('[ConsentModule] Invalid template ID:', templateId);
                this._showToast('Error', 'Invalid template ID', 'error');
                return;
            }

            console.log('[ConsentModule] Loading template for preview, ID:', templateId);
            const template = await this._apiGet(`/consent-templates/${templateId}`);

            if (!template) {
                console.error('[ConsentModule] Template not found:', templateId);
                this._showToast('Error', 'Template not found', 'error');
                return;
            }

            if (!template.HtmlContent) {
                console.error('[ConsentModule] Template has no content:', template);
                this._showToast('Error', 'Template has no content', 'error');
                return;
            }

            this._showPreview(template.HtmlContent, template.Name);
        } catch (error) {
            console.error('[ConsentModule] Error loading template for preview:', error);
            const errorMessage = error?.message || error?.Message || 'Failed to load template';
            this._showToast('Error', errorMessage, 'error');
        }
    }

    /**
     * Preview current template in editor
     */
    previewCurrent() {
        const htmlContent = this._getEditorContent();
        const name = document.getElementById('consentTemplateName').value || 'Preview';
        this._showPreview(htmlContent, name);
    }

    /**
     * Load patient consent history (for patient detail view or edit patient modal)
     * Uses the proper modal elements from whichever modal is currently open
     */
    async loadPatientHistory(patientId) {
        console.log('[ConsentModule] loadPatientHistory called for patient:', patientId);

        // Store current patient ID for refresh
        this._currentPatientId = patientId;

        // Check which modal is currently visible and scope element selection accordingly
        // Priority: patientModal (Edit Patient) > patientDetailModal (View Patient) > document
        const patientModal = document.getElementById('patientModal');
        const patientDetailContent = document.getElementById('patientDetailContent');

        let modalContainer;
        if (patientModal && patientModal.classList.contains('show')) {
            modalContainer = patientModal;
            console.log('[ConsentModule] Using Edit Patient modal (patientModal)');
        } else if (patientDetailContent) {
            modalContainer = patientDetailContent;
            console.log('[ConsentModule] Using View Patient modal (patientDetailContent)');
        } else {
            modalContainer = document;
            console.log('[ConsentModule] Using document as fallback');
        }

        // Get DOM elements within the detail modal context
        const loadingEl = modalContainer.querySelector('#patientConsentLoading');
        const newPatientEl = modalContainer.querySelector('#patientConsentNewPatient');
        const noRecordsEl = modalContainer.querySelector('#patientConsentNoRecords');
        const contentEl = modalContainer.querySelector('#patientConsentContent');
        const currentConsentCard = modalContainer.querySelector('#currentConsentCard');
        const currentConsentBody = modalContainer.querySelector('#currentConsentBody');
        const accordion = modalContainer.querySelector('#consentHistoryAccordion');

        // If modal elements not found, return silently
        if (!loadingEl && !noRecordsEl && !contentEl) {
            console.warn('[ConsentModule] Consent tab elements not found in DOM');
            console.log('[ConsentModule] loadingEl:', loadingEl, 'noRecordsEl:', noRecordsEl, 'contentEl:', contentEl);
            return;
        }

        console.log('[ConsentModule] Found consent tab elements, calling API...');

        // Show loading
        if (loadingEl) loadingEl.classList.remove('d-none');
        if (newPatientEl) newPatientEl.classList.add('d-none');
        if (noRecordsEl) noRecordsEl.classList.add('d-none');
        if (contentEl) contentEl.classList.add('d-none');

        try {
            // Use correct API endpoint: /consents/patient/{patientId}
            console.log('[ConsentModule] Fetching consent history for patient:', patientId);
            const data = await this._apiGet(`/consents/patient/${patientId}`);
            console.log('[ConsentModule] API response:', data);

            // Hide loading
            if (loadingEl) loadingEl.classList.add('d-none');

            // Check if there are any consents
            const hasConsents = (data.CareEpisodeConsents && data.CareEpisodeConsents.some(ce => ce.Consents?.length > 0)) ||
                               (data.OrphanConsents && data.OrphanConsents.length > 0);

            if (!hasConsents) {
                if (noRecordsEl) noRecordsEl.classList.remove('d-none');
                return;
            }

            // Show content
            if (contentEl) contentEl.classList.remove('d-none');

            // Hide the current consent status card — the flat list is sufficient
            if (currentConsentCard) currentConsentCard.classList.add('d-none');

            // Render flat consent list (no accordion grouping)
            this._renderConsentList(data, accordion);

        } catch (error) {
            console.error('[ConsentModule] Error loading consent history:', error);
            if (loadingEl) loadingEl.classList.add('d-none');
            if (noRecordsEl) {
                noRecordsEl.innerHTML = '<i class="bi bi-exclamation-circle me-2"></i>Error loading consent history. Please try again.';
                noRecordsEl.classList.remove('d-none');
            }
        }
    }

    /**
     * Refresh consent history for current patient
     */
    refreshHistory() {
        if (this._currentPatientId) {
            this.loadPatientHistory(this._currentPatientId);
        } else {
            // Try to get patient ID from the modal
            const patientIdEl = document.getElementById('patientId');
            if (patientIdEl && patientIdEl.value) {
                this.loadPatientHistory(parseInt(patientIdEl.value));
            }
        }
    }

    /**
     * Download consent PDF
     */
    async downloadPdf(consentId) {
        try {
            const response = await fetch(`/api/consents/${consentId}/pdf`, {
                headers: {
                    'Authorization': `Bearer ${window.authToken || localStorage.getItem('authToken')}`
                }
            });

            if (!response.ok) throw new Error('Failed to download PDF');

            const blob = await response.blob();
            const url = window.URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = `consent-${consentId}.pdf`;
            a.click();
            window.URL.revokeObjectURL(url);

        } catch (error) {
            console.error('Error downloading PDF:', error);
            this._showToast('Error', 'Failed to download PDF', 'error');
        }
    }

    // ========================================
    // Manual Consent Upload Methods
    // ========================================

    /**
     * Open the manual consent upload modal
     */
    async openManualUpload(patientId, patientName) {
        // If not provided, try to get from current patient context
        if (!patientId) {
            patientId = this._currentPatientId || document.getElementById('patientId')?.value;
        }

        if (!patientId) {
            this._showToast('Error', 'Please select a patient first', 'error');
            return;
        }

        // Get patient name if not provided
        if (!patientName) {
            try {
                const patient = await this._apiGet(`/patients/${patientId}`);
                patientName = `${patient.FirstName} ${patient.LastName}`;
            } catch (error) {
                patientName = `Patient #${patientId}`;
            }
        }

        // Store patient ID
        document.getElementById('manualConsentPatientId').value = patientId;
        document.getElementById('manualConsentPatientName').textContent = patientName;

        // Reset form fields
        document.getElementById('manualConsentType').value = '0';
        document.getElementById('manualConsentSignedAt').value = '';
        document.getElementById('manualConsentNotes').value = '';
        document.getElementById('manualConsentFile').value = '';

        // Clear file display
        document.getElementById('consentFileSelected').classList.add('d-none');
        document.getElementById('consentUploadDropzone').style.display = '';
        document.getElementById('consentUploadProgress').classList.add('d-none');

        // Auto-detect active care episode for this patient
        await this._loadActiveCareEpisode(patientId);

        // Setup file upload handlers
        this._setupFileUploadHandlers();

        // Show modal
        new bootstrap.Modal(document.getElementById('manualConsentUploadModal')).show();
    }

    /**
     * Load active care episode and auto-link consent to it
     */
    async _loadActiveCareEpisode(patientId) {
        const careEpisodeIdInput = document.getElementById('manualConsentCareEpisodeId');
        const infoDiv = document.getElementById('manualConsentCareEpisodeInfo');
        const infoText = document.getElementById('manualConsentCareEpisodeText');
        const consentTypeSelect = document.getElementById('manualConsentType');

        if (!careEpisodeIdInput || !infoDiv || !infoText) return;

        // Reset
        careEpisodeIdInput.value = '';
        infoDiv.classList.add('d-none');

        try {
            // Try to get active care episode - use direct fetch to avoid showing error toast for 404
            const response = await fetch(`/api/care-episodes/patient/${patientId}/active`, {
                headers: {
                    'Authorization': `Bearer ${window.authToken || localStorage.getItem('authToken')}`
                }
            });

            if (!response.ok) {
                throw new Error('No active care episode');
            }

            const activeCareEpisode = await response.json();

            if (activeCareEpisode && activeCareEpisode.CareEpisodeId) {
                // Active care episode found - auto-link
                careEpisodeIdInput.value = activeCareEpisode.CareEpisodeId;

                const diagnosis = activeCareEpisode.PrimaryDiagnosisDescription || activeCareEpisode.Diagnosis || 'No diagnosis';
                const startDate = activeCareEpisode.StartDate ? this._formatDate(activeCareEpisode.StartDate) : 'Unknown';

                infoText.innerHTML = `<strong>Will be linked to:</strong> ${diagnosis} (Started ${startDate})`;
                infoDiv.classList.remove('d-none', 'alert-warning');
                infoDiv.classList.add('alert-success');

                // Default to Returning Visit type if there's an active care episode
                consentTypeSelect.value = '1';
            }
        } catch (error) {
            // No active care episode found - consent will be unlinked (orphan)
            careEpisodeIdInput.value = '';
            infoText.innerHTML = `<i class="bi bi-info-circle me-1"></i>No active care episode. Consent will be saved as unlinked and can be linked later when a care episode is created.`;
            infoDiv.classList.remove('d-none', 'alert-success');
            infoDiv.classList.add('alert-warning');

            // Default to New Care Episode type
            consentTypeSelect.value = '0';
        }
    }

    /**
     * Setup file upload handlers for drag/drop and file selection
     */
    _setupFileUploadHandlers() {
        const dropzone = document.getElementById('consentUploadDropzone');
        const fileInput = document.getElementById('manualConsentFile');
        const form = document.getElementById('manualConsentUploadForm');

        if (!dropzone || !fileInput) return;

        // Handle drag and drop
        dropzone.addEventListener('dragover', (e) => {
            e.preventDefault();
            dropzone.classList.add('border-primary', 'bg-light');
        });

        dropzone.addEventListener('dragleave', () => {
            dropzone.classList.remove('border-primary', 'bg-light');
        });

        dropzone.addEventListener('drop', (e) => {
            e.preventDefault();
            dropzone.classList.remove('border-primary', 'bg-light');

            const files = e.dataTransfer.files;
            if (files.length > 0) {
                this._handleFileSelection(files[0]);
            }
        });

        // Handle file input change
        fileInput.addEventListener('change', () => {
            if (fileInput.files.length > 0) {
                this._handleFileSelection(fileInput.files[0]);
            }
        });

        // Handle form submission
        if (form) {
            form.onsubmit = (e) => this._submitManualConsent(e);
        }
    }

    /**
     * Handle file selection for preview and validation
     */
    _handleFileSelection(file) {
        const fileInput = document.getElementById('manualConsentFile');

        // Validate file type
        if (!file.type.includes('pdf') && !file.name.toLowerCase().endsWith('.pdf')) {
            this._showToast('Error', 'Only PDF files are allowed', 'error');
            fileInput.value = '';
            return;
        }

        // Validate file size (10MB max)
        const maxSize = 10 * 1024 * 1024;
        if (file.size > maxSize) {
            this._showToast('Error', 'File size exceeds 10MB limit', 'error');
            fileInput.value = '';
            return;
        }

        // Update file input with the dropped file
        const dataTransfer = new DataTransfer();
        dataTransfer.items.add(file);
        fileInput.files = dataTransfer.files;

        // Show file info
        document.getElementById('consentFileName').textContent = file.name;
        document.getElementById('consentFileSize').textContent = this._formatFileSize(file.size);
        document.getElementById('consentFileSelected').classList.remove('d-none');
        document.getElementById('consentUploadDropzone').style.display = 'none';
    }

    /**
     * Clear selected consent file
     */
    clearFile() {
        const fileInput = document.getElementById('manualConsentFile');
        if (fileInput) fileInput.value = '';

        document.getElementById('consentFileSelected').classList.add('d-none');
        document.getElementById('consentUploadDropzone').style.display = '';
    }

    /**
     * Submit manual consent upload
     */
    async _submitManualConsent(event) {
        event.preventDefault();

        const fileInput = document.getElementById('manualConsentFile');
        const submitBtn = document.getElementById('submitManualConsentBtn');
        const progressEl = document.getElementById('consentUploadProgress');

        if (!fileInput.files || fileInput.files.length === 0) {
            this._showToast('Error', 'Please select a PDF file', 'error');
            return;
        }

        const patientId = document.getElementById('manualConsentPatientId').value;
        const consentType = document.getElementById('manualConsentType').value;
        const careEpisodeId = document.getElementById('manualConsentCareEpisodeId').value; // Auto-detected
        const signedAt = document.getElementById('manualConsentSignedAt').value;
        const notes = document.getElementById('manualConsentNotes').value;

        // Create FormData
        const formData = new FormData();
        formData.append('file', fileInput.files[0]);
        formData.append('patientId', patientId);
        formData.append('consentType', consentType);
        // Auto-link to active care episode (if exists)
        if (careEpisodeId) formData.append('careEpisodeId', careEpisodeId);
        if (signedAt) formData.append('signedAt', signedAt);
        if (notes) formData.append('notes', notes);

        // Show progress and disable submit button
        progressEl.classList.remove('d-none');
        submitBtn.disabled = true;
        submitBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Uploading...';

        try {
            console.log('[ConsentModule] Starting upload, patientId:', patientId, 'careEpisodeId:', careEpisodeId);
            const response = await fetch('/api/consents/upload', {
                method: 'POST',
                headers: {
                    'Authorization': `Bearer ${window.authToken || localStorage.getItem('authToken')}`
                },
                body: formData
            });

            console.log('[ConsentModule] Upload response status:', response.status);
            const responseText = await response.text();
            console.log('[ConsentModule] Upload response body:', responseText);

            let result;
            try {
                result = JSON.parse(responseText);
            } catch (e) {
                console.error('[ConsentModule] Failed to parse response as JSON:', e);
                throw new Error('Server returned invalid response');
            }

            if (!response.ok || !result.Success) {
                throw new Error(result.Message || 'Upload failed');
            }

            // Success
            this._showToast('Success', 'Consent form uploaded successfully');

            // Close modal
            bootstrap.Modal.getInstance(document.getElementById('manualConsentUploadModal'))?.hide();

            // Refresh consent history
            if (this._currentPatientId) {
                await this.loadPatientHistory(this._currentPatientId);
            }

        } catch (error) {
            console.error('Error uploading consent:', error);
            this._showToast('Error', error.message || 'Failed to upload consent form', 'error');
        } finally {
            progressEl.classList.add('d-none');
            submitBtn.disabled = false;
            submitBtn.innerHTML = '<i class="bi bi-upload me-1"></i>Upload Consent';
        }
    }

    /**
     * Format file size for display
     */
    _formatFileSize(bytes) {
        if (bytes === 0) return '0 Bytes';
        const k = 1024;
        const sizes = ['Bytes', 'KB', 'MB', 'GB'];
        const i = Math.floor(Math.log(bytes) / Math.log(k));
        return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
    }

    /**
     * Clean up module resources
     */
    destroy() {
        this._unbindEvents();
        this.templates = [];
    }

    // ========================================
    // Private Methods - Rendering
    // ========================================

    _renderTemplateRow(template) {
        return `
            <tr>
                <td>
                    <strong>${this._escape(template.Name)}</strong>
                    ${template.Description ? `<br><small class="text-muted">${this._escape(template.Description)}</small>` : ''}
                </td>
                <td>${this.formTypeNames[template.FormType] || 'Unknown'}</td>
                <td>v${template.Version}</td>
                <td>
                    ${template.IsActive
                        ? '<span class="badge bg-success">Active</span>'
                        : '<span class="badge bg-secondary">Inactive</span>'}
                </td>
                <td>${template.UpdatedAt ? new Date(template.UpdatedAt).toLocaleDateString() : new Date(template.CreatedAt).toLocaleDateString()}</td>
                <td>
                    <button class="btn btn-sm btn-outline-primary" data-action="edit-template" data-template-id="${template.ConsentFormTemplateId}" title="Edit">
                        <i class="bi bi-pencil"></i>
                    </button>
                    <button class="btn btn-sm btn-outline-info" data-action="preview-template" data-template-id="${template.ConsentFormTemplateId}" title="Preview">
                        <i class="bi bi-eye"></i>
                    </button>
                    <button class="btn btn-sm btn-outline-danger" data-action="delete-template" data-template-id="${template.ConsentFormTemplateId}" data-template-name="${this._escape(template.Name)}" title="Delete">
                        <i class="bi bi-trash"></i>
                    </button>
                </td>
            </tr>
        `;
    }

    /**
     * Render current consent status card
     */
    _renderCurrentConsentStatus(data, cardEl, bodyEl) {
        if (!cardEl || !bodyEl) return;

        // Find current active care episode with consents
        const activeCareEpisode = data.CareEpisodeConsents?.find(ce => ce.Status === 0 && ce.Consents?.length > 0);

        if (activeCareEpisode?.Consents?.length > 0) {
            cardEl.classList.remove('d-none');
            const latestConsent = activeCareEpisode.Consents[0];
            const signedDate = new Date(latestConsent.SignedAt);
            const totalForms = activeCareEpisode.Consents.reduce((sum, c) => sum + c.FormCount, 0);

            bodyEl.innerHTML = `
                <div class="d-flex justify-content-between align-items-center">
                    <div>
                        <p class="mb-1"><strong>Latest Consent:</strong> ${signedDate.toLocaleDateString()} at ${signedDate.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</p>
                        <p class="mb-0"><strong>Total:</strong> ${activeCareEpisode.Consents.length} consent session${activeCareEpisode.Consents.length !== 1 ? 's' : ''}, ${totalForms} form${totalForms !== 1 ? 's' : ''}</p>
                    </div>
                    <button class="btn btn-sm btn-outline-light" data-action="download-consent" data-consent-id="${latestConsent.CareEpisodeConsentId}">
                        <i class="bi bi-download me-1"></i>Download PDF
                    </button>
                </div>
            `;
        } else {
            cardEl.classList.add('d-none');
        }
    }

    /**
     * Render flat consent list (simplified — no accordion grouping)
     */
    _renderConsentList(data, containerEl) {
        if (!containerEl) return;

        // Collect all consents into a flat list
        const allConsents = [];

        if (data.CareEpisodeConsents?.length > 0) {
            data.CareEpisodeConsents.forEach(ce => {
                if (ce.Consents?.length > 0) {
                    ce.Consents.forEach(c => allConsents.push(c));
                }
            });
        }

        if (data.OrphanConsents?.length > 0) {
            data.OrphanConsents.forEach(c => allConsents.push(c));
        }

        // Sort by date descending (most recent first)
        allConsents.sort((a, b) => new Date(b.SignedAt) - new Date(a.SignedAt));

        containerEl.innerHTML = allConsents
            .map(c => this._renderConsentDetails(c))
            .join('<hr class="my-2">');
    }

    /**
     * Render individual consent details
     */
    _renderConsentDetails(consent) {
        const signedDate = new Date(consent.SignedAt);
        const appointmentDate = consent.AppointmentDate ? new Date(consent.AppointmentDate) : null;

        return `
            <div class="consent-details p-2 rounded bg-light" data-consent-id="${consent.CareEpisodeConsentId}">
                <div class="d-flex justify-content-between align-items-start">
                    <div>
                        <div class="mb-1">
                            <i class="bi bi-calendar-check me-1"></i>
                            <strong>${signedDate.toLocaleDateString()}</strong> at ${signedDate.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                        </div>
                        <div class="small text-muted">
                            <span class="me-2">${consent.FormCount} form${consent.FormCount !== 1 ? 's' : ''}</span>
                            ${consent.LocationName ? `<span><i class="bi bi-geo-alt me-1"></i>${this._escape(consent.LocationName)}</span>` : ''}
                        </div>
                        ${consent.Forms?.length > 0 ? `
                            <div class="small mt-1">
                                <span class="text-muted">Forms:</span>
                                <ul class="mb-0 ps-3">
                                    ${consent.Forms.map(f => `<li>${this._escape(f.FormName)} <small class="text-muted">(${f.SignatureCount} signature${f.SignatureCount !== 1 ? 's' : ''})</small></li>`).join('')}
                                </ul>
                            </div>
                        ` : ''}
                    </div>
                    <button class="btn btn-sm btn-outline-primary" data-action="download-consent" data-consent-id="${consent.CareEpisodeConsentId}" title="Download PDF">
                        <i class="bi bi-download"></i>
                    </button>
                </div>
            </div>
        `;
    }

    _showPreview(htmlContent, title) {
        // Replace placeholders with sample data
        const previewContent = htmlContent
            .replace(/\{\{PATIENT_FULL_NAME\}\}/g, 'John Doe')
            .replace(/\{\{PATIENT_DOB\}\}/g, '01/15/1980')
            .replace(/\{\{TODAY_DATE\}\}/g, new Date().toLocaleDateString())
            .replace(/\{\{CLINIC_NAME\}\}/g, 'Sample Clinic')
            .replace(/\{\{LOCATION_NAME\}\}/g, 'Main Office')
            .replace(/\{\{PROVIDER_NAME\}\}/g, 'Dr. Smith')
            .replace(/\{\{SIGNATURE:[^}]+\}\}/g, '<div class="border p-3 text-center text-muted">[Signature Field]</div>');

        // Create preview modal
        const previewModal = document.getElementById('consentPreviewModal');
        if (previewModal) {
            document.getElementById('consentPreviewTitle').textContent = title;
            document.getElementById('consentPreviewContent').innerHTML = previewContent;
            new bootstrap.Modal(previewModal).show();
        } else {
            // Fallback: open in new window
            const win = window.open('', '_blank');
            win.document.write(`
                <!DOCTYPE html>
                <html>
                <head>
                    <title>${this._escape(title)} - Preview</title>
                    <link href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.2/dist/css/bootstrap.min.css" rel="stylesheet">
                </head>
                <body class="p-4">
                    <h4 class="mb-4">${this._escape(title)}</h4>
                    ${previewContent}
                </body>
                </html>
            `);
            win.document.close();
        }
    }

    _generateQrCode(url) {
        const container = document.getElementById('kioskQrCodeDisplay');
        if (!container) return;

        container.innerHTML = '';

        if (typeof QRCode !== 'undefined') {
            new QRCode(container, {
                text: url,
                width: 128,
                height: 128,
                colorDark: '#000000',
                colorLight: '#ffffff',
                correctLevel: QRCode.CorrectLevel.M
            });
        } else {
            const qrImg = document.createElement('img');
            qrImg.src = `https://chart.googleapis.com/chart?cht=qr&chs=128x128&chl=${encodeURIComponent(url)}&choe=UTF-8`;
            qrImg.alt = 'Kiosk QR Code';
            container.appendChild(qrImg);
        }
    }

    // ========================================
    // Private Methods - Editor
    // ========================================

    _initEditor() {
        if (this.editorInitialized) return;

        if (typeof $.fn.trumbowyg !== 'undefined') {
            $('#consentTemplateContent').trumbowyg({
                btns: [
                    ['viewHTML'],
                    ['undo', 'redo'],
                    ['formatting'],
                    ['strong', 'em', 'underline', 'del'],
                    ['superscript', 'subscript'],
                    ['justifyLeft', 'justifyCenter', 'justifyRight', 'justifyFull'],
                    ['unorderedList', 'orderedList'],
                    ['horizontalRule'],
                    ['removeformat'],
                    ['fullscreen']
                ],
                autogrow: true,
                minimalLinks: true
            });
            this.editorInitialized = true;
        }
    }

    _getEditorContent() {
        if (typeof $.fn.trumbowyg !== 'undefined') {
            return $('#consentTemplateContent').trumbowyg('html');
        }
        return document.getElementById('consentTemplateContent')?.value || '';
    }

    _setEditorContent(content) {
        if (typeof $.fn.trumbowyg !== 'undefined') {
            $('#consentTemplateContent').trumbowyg('html', content);
        } else {
            const el = document.getElementById('consentTemplateContent');
            if (el) el.value = content;
        }
    }

    // ========================================
    // Private Methods - Event Handling
    // ========================================

    _bindEvents() {
        this._boundHandlers.docClick = (e) => this._handleDocumentClick(e);
        document.addEventListener('click', this._boundHandlers.docClick);

        // Form submit
        const form = document.getElementById('consentTemplateForm');
        if (form) {
            this._boundHandlers.formSubmit = (e) => this.save(e);
            form.addEventListener('submit', this._boundHandlers.formSubmit);
        }
    }

    _unbindEvents() {
        if (this._boundHandlers.docClick) {
            document.removeEventListener('click', this._boundHandlers.docClick);
        }

        const form = document.getElementById('consentTemplateForm');
        if (form && this._boundHandlers.formSubmit) {
            form.removeEventListener('submit', this._boundHandlers.formSubmit);
        }
    }

    _handleDocumentClick(e) {
        const target = e.target.closest('[data-action]');
        if (!target) return;

        const action = target.getAttribute('data-action');
        const templateId = target.getAttribute('data-template-id');
        const templateName = target.getAttribute('data-template-name');
        const consentId = target.getAttribute('data-consent-id');

        switch (action) {
            case 'edit-template':
                this.edit(parseInt(templateId));
                break;
            case 'preview-template':
                this.previewById(parseInt(templateId));
                break;
            case 'delete-template':
                this.delete(parseInt(templateId), templateName);
                break;
            case 'download-consent':
                this.downloadPdf(parseInt(consentId));
                break;
        }
    }

    // ========================================
    // Private Methods - API
    // ========================================

    async _apiGet(endpoint) {
        if (this.api) {
            return await this.api.get(endpoint);
        }
        return await window.apiRequest(endpoint, { showLoader: false });
    }

    async _apiPost(endpoint, data) {
        if (this.api) {
            return await this.api.post(endpoint, data);
        }
        return await window.apiRequest(endpoint, { method: 'POST', body: JSON.stringify(data) });
    }

    async _apiPut(endpoint, data) {
        if (this.api) {
            return await this.api.put(endpoint, data);
        }
        return await window.apiRequest(endpoint, { method: 'PUT', body: JSON.stringify(data) });
    }

    async _apiDelete(endpoint) {
        if (this.api) {
            return await this.api.delete(endpoint);
        }
        return await window.apiRequest(endpoint, { method: 'DELETE' });
    }

    // ========================================
    // Private Methods - Utilities
    // ========================================

    _escape(str) {
        if (str === null || str === undefined) return '';
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }

    _formatDate(dateStr) {
        if (!dateStr) return '-';
        // Handle date-only strings (YYYY-MM-DD) to avoid timezone shifting
        if (typeof dateStr === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(dateStr)) {
            const [year, month, day] = dateStr.split('-').map(Number);
            return new Date(year, month - 1, day).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
        }
        return new Date(dateStr).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
    }

    _getCurrentLocation() {
        return window.currentLocation || null;
    }

    _showToast(title, message, type = 'success') {
        if (window.Toast) {
            window.Toast.show(title, message, type);
        } else if (window.showToast) {
            window.showToast(title, message, type);
        }
    }

    async _confirm(title, message) {
        if (window.ConfirmDialog) {
            return await window.ConfirmDialog.show(title, message);
        }
        return confirm(message);
    }

    _emit(event, data) {
        if (this.eventBus) {
            this.eventBus.emit(event, data);
        }
    }
}

// Export for module usage
if (typeof module !== 'undefined' && module.exports) {
    module.exports = ConsentModule;
}

// Global functions for legacy onclick handlers
function openConsentTemplateModal() {
    if (window.consentModule) {
        window.consentModule.openNewModal();
    }
}

function editConsentTemplate(templateId) {
    if (window.consentModule) {
        window.consentModule.edit(templateId);
    }
}

function previewConsentTemplateById(templateId) {
    if (window.consentModule) {
        window.consentModule.previewById(templateId);
    }
}

function previewConsentTemplate() {
    if (window.consentModule) {
        window.consentModule.previewCurrent();
    }
}

function deleteConsentTemplate(templateId, templateName) {
    if (window.consentModule) {
        window.consentModule.delete(templateId, templateName);
    }
}

function copyKioskLink() {
    if (window.consentModule) {
        window.consentModule.copyKioskLink();
    }
}

function openKioskInNewTab() {
    if (window.consentModule) {
        window.consentModule.openKioskInNewTab();
    }
}

function saveConsentTemplate(event) {
    if (window.consentModule) {
        window.consentModule.save(event);
    }
}

function openManualConsentUpload() {
    if (window.consentModule) {
        window.consentModule.openManualUpload();
    }
}

function clearConsentFile() {
    if (window.consentModule) {
        window.consentModule.clearFile();
    }
}

function refreshPatientConsentHistory() {
    if (window.consentModule) {
        window.consentModule.refreshHistory();
    }
}

function openPatientConsentUpload(patientId) {
    if (window.consentModule) {
        window.consentModule.openManualUpload(patientId);
    }
}

// Auto-initialize when DOM is ready
function initConsentModule() {
    // Support both page ID formats - only needed for the full page load
    const container = document.getElementById('consentFormsPage') || document.getElementById('consent-formsPage');
    const isConsentFormsPage = !!container;

    const initWhenReady = () => {
        const isAuthenticated = (typeof currentUser !== 'undefined' && currentUser) ||
                               (window.App && window.App.isAuthenticated && window.App.isAuthenticated()) ||
                               !!localStorage.getItem('authToken');

        if (!isAuthenticated) {
            setTimeout(initWhenReady, 200);
            return;
        }

        // Wait for location to be set (with timeout) - only for consent forms page
        if (isConsentFormsPage) {
            const hasLocation = window.currentLocation?.LocationId ||
                               window.locationModule?.currentLocation?.LocationId;

            if (!hasLocation) {
                // Retry a few times waiting for location
                if (!initWhenReady.retryCount) initWhenReady.retryCount = 0;
                initWhenReady.retryCount++;

                if (initWhenReady.retryCount < 10) {
                    setTimeout(initWhenReady, 200);
                    return;
                }
                // After 10 retries (2 seconds), proceed anyway - will show "select location" message
            }
        }

        // Always create the module if it doesn't exist (needed for patient modal Consents tab)
        if (!window.consentModule) {
            console.log('[ConsentModule] Initializing ConsentModule...');
            window.consentModule = new ConsentModule({
                api: window.apiService || (window.App && window.App.api),
                eventBus: window.eventBus || (window.App && window.App.events)
            });
            window.consentModule.init();
            console.log('[ConsentModule] ConsentModule initialized successfully');
        }

        // Only call load() on the consent forms page
        if (isConsentFormsPage) {
            window.consentModule.load();
        }
    };

    initWhenReady();
}

// Initialize on DOMContentLoaded or immediately if DOM is already ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initConsentModule);
} else {
    // DOM is already ready, initialize immediately
    initConsentModule();
}
