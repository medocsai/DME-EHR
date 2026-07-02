/**
 * PatientModule - Patient management functionality
 *
 * Handles patient CRUD operations, filtering, viewing details,
 * insurance management, and document handling.
 *
 * @example
 *   const patients = App.modules.get('patients');
 *   await patients.init();
 *   patients.load();
 */
class PatientModule {
    /**
     * @param {Object} options - Module options
     * @param {Object} options.api - API service instance
     * @param {Object} options.eventBus - Event bus for cross-module communication
     */
    constructor(options = {}) {
        this.api = options.api || null;
        this.eventBus = options.eventBus || null;

        // State
        this.patients = [];
        this.currentPatient = null;
        this.currentFilters = {
            status: 'all',
            validation: 'all',
            episodes: 'all',
            showArchived: false,
            search: ''
        };
        this.isInitialized = false;
        this.currentPage = 1;
        this.pageSize = 25;
        this.totalPages = 0;
        this.totalCount = 0;
        this.pendingFiles = []; // Files queued for upload when creating new patient
        this.pendingAuthorizations = {}; // Authorizations queued for new patient (primary/secondary)

        // DOM references
        this.container = null;
        this.tableBody = null;
        this.searchInput = null;

        // Bind methods
        this._handleContainerClick = this._handleContainerClick.bind(this);
        this._handleSearch = this._handleSearch.bind(this);

        // Initialize helper modules
        this.utilities = window.PatientUtilities;
        this.renderer = new PatientRenderer({ utilities: this.utilities });
        this.documentManager = new PatientDocumentManager({ utilities: this.utilities });
        this.insuranceManager = new PatientInsuranceManager({ utilities: this.utilities });
        this.formHandler = new PatientFormHandler({ utilities: this.utilities });
    }

    /**
     * Initialize the module
     * @returns {Promise<void>}
     */
    async init() {
        this.container = document.getElementById('patientsGrid') ||
                        document.querySelector('.patients-container');
        this.tableBody = document.getElementById('patientsTableBody');
        this.searchInput = document.getElementById('patientSearch');

        // Always bind form events (modal can be opened from anywhere)
        this._bindFormSubmit();

        // Also bind when patient modal is shown (in case form wasn't ready initially)
        const patientModal = document.getElementById('patientModal');
        if (patientModal) {
            // Reset form and tabs when modal is about to be shown
            patientModal.addEventListener('show.bs.modal', (event) => {
                // Initialize insurance company dropdowns FIRST (before form data loads)
                this._initializeInsuranceDropdowns();

                // Check if this is opened by a button (new patient) vs programmatically (edit)
                // When opened by button directly, reset the form
                const trigger = event.relatedTarget;
                if (trigger && trigger.hasAttribute('data-bs-toggle')) {
                    // This is "Add Patient" button - reset everything
                    this.formHandler.resetForm();
                }
            });

            patientModal.addEventListener('shown.bs.modal', () => {
                this._bindFormSubmit();
                this.documentManager.initAttachmentUpload((files) => this._handleFileUpload(files));
                // Initialize input masks (SSN, phone, etc.)
                if (window.InputMaskUtils) {
                    InputMaskUtils.initialize();
                }
                // Reset modal title for new patient
                const titleEl = patientModal.querySelector('.modal-title');
                const patientIdInput = document.getElementById('patientId');
                if (titleEl && patientIdInput && !patientIdInput.value) {
                    titleEl.textContent = 'New Patient';
                }

                // Bind consent tab event listener to load consent history
                this._bindConsentTabListener();
            });

            // Clear pending files and state when modal is closed
            patientModal.addEventListener('hidden.bs.modal', () => {
                this.pendingFiles = [];
                this._pendingAuthorization = null;
                this.currentPatient = null;
            });
        }

        if (!this.container && !this.tableBody) {
            this.isInitialized = true;
            this._emit('patients:initialized');
            return;
        }

        this._bindEvents();
        this._initFilterButtons();

        this.isInitialized = true;
        this._emit('patients:initialized');
    }

    /**
     * Bind event handlers
     * @private
     */
    _bindEvents() {
        // Container click delegation
        if (this.container) {
            this.container.addEventListener('click', this._handleContainerClick);
        }
        if (this.tableBody) {
            this.tableBody.addEventListener('click', this._handleContainerClick);
        }

        // Patient Detail Modal click delegation (for care episode buttons)
        const patientDetailModal = document.getElementById('patientDetailModal');
        if (patientDetailModal) {
            patientDetailModal.addEventListener('click', this._handleContainerClick);
        }

        // Search input
        if (this.searchInput) {
            this.searchInput.addEventListener('input', this.utilities.debounce(this._handleSearch.bind(this), 300));
        }

        // Status filter buttons
        document.querySelectorAll('#patientStatusFilter .filter-btn').forEach(btn => {
            btn.addEventListener('click', (e) => this._handleStatusFilterClick(e));
        });

        // Profile filter buttons
        document.querySelectorAll('#patientValidationFilter .filter-btn').forEach(btn => {
            btn.addEventListener('click', (e) => this._handleProfileFilterClick(e));
        });

        // Archived checkbox
        const archivedCheckbox = document.getElementById('includeArchivedCheckbox');
        if (archivedCheckbox) {
            archivedCheckbox.addEventListener('change', (e) => {
                this.currentFilters.showArchived = e.target.checked;
                this.applyFilters();
            });
        }

        // Subscriber relationship auto-fill: when "Self" is selected, copy patient info
        const patientForm = document.getElementById('patientForm');
        if (patientForm && !patientForm._relationshipChangeBound) {
            patientForm.addEventListener('change', (e) => {
                const select = e.target.closest('.subscriber-relationship-select');
                if (!select) return;

                const prefix = select.getAttribute('data-prefix');
                if (!prefix) return;

                if (select.value === 'Self') {
                    const firstName = patientForm.querySelector('[name="FirstName"]')?.value || '';
                    const lastName = patientForm.querySelector('[name="LastName"]')?.value || '';
                    const dob = patientForm.querySelector('[name="DateOfBirth"]')?.value || '';

                    const setVal = (fieldName, val) => {
                        const input = patientForm.querySelector(`[name="${prefix}.${fieldName}"]`);
                        if (input) input.value = val;
                    };

                    setVal('SubscriberFirstName', firstName);
                    setVal('SubscriberLastName', lastName);
                    setVal('SubscriberDob', dob);
                }
            });
            patientForm._relationshipChangeBound = true;
        }

        // Pagination buttons
        document.getElementById('patientPaginationButtons')?.addEventListener('click', (e) => {
            e.preventDefault();
            const btn = e.target.closest('[data-page]');
            if (!btn || btn.parentElement.classList.contains('disabled')) return;
            this.currentPage = parseInt(btn.dataset.page);
            this.load();
        });

        // Note: Form submission and modal events are now bound in init()
        // to ensure they work even when container is not found
    }

    /**
     * Bind form submit handler if not already bound
     * @private
     */
    _bindFormSubmit() {
        const form = document.getElementById('patientForm');
        if (!form || form._patientModuleHandlerBound) {
            return;
        }

        form.addEventListener('submit', (e) => this._handleFormSubmit(e));
        form._patientModuleHandlerBound = true;

        // Clean up pending photo when patient modal is closed
        const patientModal = document.getElementById('patientModal');
        if (patientModal && !patientModal._pendingPhotoCleanupBound) {
            patientModal.addEventListener('hidden.bs.modal', () => {
                AvatarUtils.clearPendingPhoto('patient');
            });
            patientModal._pendingPhotoCleanupBound = true;
        }
    }

    /**
     * Bind consent tab listener to load consent history when tab is clicked
     * @private
     */
    _bindConsentTabListener() {
        const consentTab = document.getElementById('consent-tab');
        if (!consentTab || consentTab._consentTabHandlerBound) {
            return;
        }

        const loadConsentHistory = () => {
            const patientIdInput = document.getElementById('patientId');
            const patientId = patientIdInput?.value;

            if (!patientId) {
                // New patient - show message instead of loading
                const newPatientEl = document.getElementById('patientConsentNewPatient');
                const loadingEl = document.getElementById('patientConsentLoading');
                const noRecordsEl = document.getElementById('patientConsentNoRecords');
                const contentEl = document.getElementById('patientConsentContent');

                if (loadingEl) loadingEl.classList.add('d-none');
                if (noRecordsEl) noRecordsEl.classList.add('d-none');
                if (contentEl) contentEl.classList.add('d-none');
                if (newPatientEl) newPatientEl.classList.remove('d-none');
                return;
            }

            // Load consent history for existing patient
            if (window.consentModule) {
                console.log('[PatientModule] Loading consent history for patient:', patientId);
                window.consentModule.loadPatientHistory(parseInt(patientId));
            }
        };

        // Listen for tab shown event
        consentTab.addEventListener('shown.bs.tab', loadConsentHistory);

        // Also trigger on click with delay for browsers that don't fire shown.bs.tab
        consentTab.addEventListener('click', () => {
            setTimeout(loadConsentHistory, 100);
        });

        consentTab._consentTabHandlerBound = true;
    }

    /**
     * Initialize payer autocomplete inputs for insurance forms
     * @private
     */
    _initializeInsuranceDropdowns() {
        this._initializePayerAutocomplete('primaryPayerName', 'primaryPayerResults', 'PrimaryInsurance');
        this._initializePayerAutocomplete('secondaryPayerName', 'secondaryPayerResults', 'SecondaryInsurance');
    }

    /**
     * Initialize payer autocomplete for a specific input
     * @private
     * @param {string} inputId - The text input element ID
     * @param {string} resultsId - The results dropdown container ID
     * @param {string} prefix - Insurance prefix (PrimaryInsurance or SecondaryInsurance)
     */
    _initializePayerAutocomplete(inputId, resultsId, prefix) {
        const input = document.getElementById(inputId);
        const results = document.getElementById(resultsId);
        if (!input || !results) return;
        if (input._payerAutocompleteBound) return;

        let debounceTimer = null;
        let selectedIndex = -1;
        let currentResults = [];

        const showResults = () => { results.classList.remove('d-none'); results.classList.add('show'); };
        const hideResults = () => { results.classList.add('d-none'); results.classList.remove('show'); selectedIndex = -1; };

        const escape = (s) => s ? s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;') : '';

        const renderResults = (items) => {
            currentResults = items;
            selectedIndex = -1;
            if (!items.length) {
                results.innerHTML = '<div class="autocomplete-empty text-muted p-2">No payers found</div>';
                showResults();
                return;
            }
            results.innerHTML = items.map((p, i) =>
                `<div class="autocomplete-item" data-index="${i}"><span class="payer-name">${escape(p.Name)}</span><span class="payer-id">${escape(p.PayerIdCode)}</span></div>`
            ).join('');
            showResults();
        };

        const selectItem = (idx) => {
            if (idx < 0 || idx >= currentResults.length) return;
            const payer = currentResults[idx];
            input.value = payer.Name;
            const payerIdInput = document.querySelector(`input[name="${prefix}.PayerId"]`);
            if (payerIdInput) payerIdInput.value = payer.PayerIdCode || '';
            hideResults();
        };

        input.addEventListener('input', () => {
            if (debounceTimer) clearTimeout(debounceTimer);
            const q = input.value.trim();
            if (q.length < 2) { hideResults(); return; }
            debounceTimer = setTimeout(async () => {
                try {
                    const resp = await fetch(`/api/lookups/payers/search?q=${encodeURIComponent(q)}&take=15`, {
                        headers: { 'Authorization': `Bearer ${App.auth.getToken()}` }
                    });
                    if (resp.ok) renderResults(await resp.json());
                } catch (e) { console.error('[PayerSearch]', e); }
            }, 250);
        });

        input.addEventListener('keydown', (e) => {
            if (!results.classList.contains('show')) return;
            const items = results.querySelectorAll('.autocomplete-item');
            if (e.key === 'ArrowDown') {
                e.preventDefault();
                if (selectedIndex >= 0 && items[selectedIndex]) items[selectedIndex].classList.remove('active');
                selectedIndex = (selectedIndex + 1) % items.length;
                items[selectedIndex].classList.add('active');
                items[selectedIndex].scrollIntoView({ block: 'nearest' });
            } else if (e.key === 'ArrowUp') {
                e.preventDefault();
                if (selectedIndex >= 0 && items[selectedIndex]) items[selectedIndex].classList.remove('active');
                selectedIndex = selectedIndex <= 0 ? items.length - 1 : selectedIndex - 1;
                items[selectedIndex].classList.add('active');
                items[selectedIndex].scrollIntoView({ block: 'nearest' });
            } else if (e.key === 'Enter') {
                e.preventDefault();
                if (selectedIndex >= 0) selectItem(selectedIndex);
            } else if (e.key === 'Escape') {
                hideResults();
            }
        });

        results.addEventListener('click', (e) => {
            const item = e.target.closest('.autocomplete-item');
            if (item) selectItem(parseInt(item.dataset.index));
        });

        // Hide on blur (with delay for click to register)
        input.addEventListener('blur', () => setTimeout(hideResults, 200));

        input._payerAutocompleteBound = true;
    }

    /**
     * Initialize filter buttons state
     * @private
     */
    _initFilterButtons() {
        // Set initial active states - the HTML already has 'selected' class on defaults
        // This method ensures consistency if needed
        const statusAllBtn = document.querySelector('#patientStatusFilter .filter-btn[data-value="all"]');
        if (statusAllBtn && !statusAllBtn.classList.contains('selected')) {
            statusAllBtn.classList.add('selected');
            statusAllBtn.dataset.selected = 'true';
        }

        const profileAllBtn = document.querySelector('#patientValidationFilter .filter-btn[data-value=""]');
        if (profileAllBtn && !profileAllBtn.classList.contains('selected')) {
            profileAllBtn.classList.add('selected');
            profileAllBtn.dataset.selected = 'true';
        }
    }

    /**
     * Handle container click events via delegation
     * @private
     * @param {Event} e - Click event
     */
    _handleContainerClick(e) {
        const target = e.target.closest('[data-action]');
        if (!target) return;

        // Prevent default for anchor tags
        if (target.tagName === 'A') {
            e.preventDefault();
        }

        const action = target.dataset.action;
        const patientId = target.dataset.patientId;

        switch (action) {
            case 'view':
                this.view(parseInt(patientId));
                break;
            case 'edit':
                this.edit(parseInt(patientId));
                break;
            case 'delete':
                this.delete(parseInt(patientId), target.dataset.patientName);
                break;
            case 'archive':
                this.archive(parseInt(patientId), target.dataset.patientName);
                break;
            case 'unarchive':
                this.unarchive(parseInt(patientId), target.dataset.patientName);
                break;
            case 'create-care-episode':
                this._openCareEpisodeModal(parseInt(patientId));
                break;
            case 'edit-care-episode':
                this._openCareEpisodeModal(parseInt(patientId), parseInt(target.dataset.episodeId));
                break;
            case 'view-care-episode':
                this._viewCareEpisodeDetails(parseInt(target.dataset.episodeId));
                break;
            case 'complete-care-episode':
                this._completeCareEpisode(parseInt(target.dataset.episodeId));
                break;
            case 'restore-care-episode':
                this._restoreCareEpisode(parseInt(target.dataset.episodeId));
                break;
            case 'zoom-avatar':
                this._showAvatarZoom(parseInt(patientId), target.dataset.patientName, target.dataset.hasPhoto === 'true');
                break;
        }
    }

    /**
     * Show avatar zoom modal with enlarged photo and View Profile link
     * @param {number} patientId - Patient ID
     * @param {string} patientName - Patient name
     * @param {boolean} hasPhoto - Whether patient has a profile picture
     * @private
     */
    _showAvatarZoom(patientId, patientName, hasPhoto) {
        // Remove any existing zoom modal
        const existing = document.getElementById('avatarZoomModal');
        if (existing) existing.remove();

        const safeName = StringUtils.escape(patientName || '');
        const initials = AvatarUtils.getInitials(patientName);

        const avatarContent = hasPhoto
            ? `<img src="/api/patients/${patientId}/profile-picture?v=${Date.now()}"
                   alt="${safeName}"
                   class="avatar-zoom-img"
                   onerror="this.style.display='none'; this.nextElementSibling.style.display='flex';">
               <div class="avatar-zoom-initials" style="display:none;">${StringUtils.escape(initials)}</div>`
            : `<div class="avatar-zoom-initials">${StringUtils.escape(initials)}</div>`;

        const modalHtml = `
            <div class="modal fade" id="avatarZoomModal" tabindex="-1" data-bs-backdrop="true">
                <div class="modal-dialog modal-dialog-centered modal-sm">
                    <div class="modal-content avatar-zoom-content">
                        <div class="avatar-zoom-body text-center p-4">
                            <div class="avatar-zoom-circle avatar-initials-patient mx-auto mb-3">
                                ${avatarContent}
                            </div>
                            <h5 class="mb-3">${safeName}</h5>
                            <button type="button" class="btn btn-primary btn-sm" id="avatarZoomViewProfileBtn">
                                <i class="bi bi-person-lines-fill me-1"></i>View Profile
                            </button>
                        </div>
                        <button type="button" class="btn-close avatar-zoom-close" data-bs-dismiss="modal" aria-label="Close"></button>
                    </div>
                </div>
            </div>
        `;

        document.body.insertAdjacentHTML('beforeend', modalHtml);

        const modalEl = document.getElementById('avatarZoomModal');
        const bsModal = new bootstrap.Modal(modalEl);

        // View Profile button — close zoom and open patient detail
        document.getElementById('avatarZoomViewProfileBtn').addEventListener('click', () => {
            bsModal.hide();
            setTimeout(() => this.view(patientId), 300);
        });

        // Clean up modal from DOM after it's hidden
        modalEl.addEventListener('hidden.bs.modal', () => {
            modalEl.remove();
        });

        bsModal.show();
    }

    /**
     * Handle status filter button clicks
     * @private
     * @param {Event} e - Click event
     */
    _handleStatusFilterClick(e) {
        const btn = e.target.closest('.filter-btn');
        if (!btn) return;

        const filterValue = btn.dataset.value;

        // Update selected state within filter group
        const group = document.getElementById('patientStatusFilter');
        if (group) {
            group.querySelectorAll('.filter-btn').forEach(b => {
                b.classList.remove('selected');
                b.dataset.selected = 'false';
            });
            btn.classList.add('selected');
            btn.dataset.selected = 'true';
        }

        // Update filter state (archived is now a separate checkbox)
        this.currentFilters.status = filterValue;

        this.applyFilters();
    }

    /**
     * Handle profile filter button clicks
     * @private
     * @param {Event} e - Click event
     */
    _handleProfileFilterClick(e) {
        const btn = e.target.closest('.filter-btn');
        if (!btn) return;

        const filterValue = btn.dataset.value;

        // Update selected state within filter group
        const group = document.getElementById('patientValidationFilter');
        if (group) {
            group.querySelectorAll('.filter-btn').forEach(b => {
                b.classList.remove('selected');
                b.dataset.selected = 'false';
            });
            btn.classList.add('selected');
            btn.dataset.selected = 'true';
        }

        // Update filter state - profile maps to validation filter
        this.currentFilters.validation = filterValue || 'all';

        this.applyFilters();
    }

    /**
     * Handle search input
     * @private
     */
    _handleSearch() {
        const searchValue = this.searchInput?.value?.trim() || '';

        // Only search if at least 3 characters typed (avoids returning too many results)
        // Also search if empty (to show all/clear search)
        if (searchValue.length >= 3 || searchValue.length === 0) {
            this.currentFilters.search = searchValue;
            this.applyFilters();
        }
    }

    /**
     * Load patients from API
     * @param {Object} filters - Optional filter overrides
     * @returns {Promise<void>}
     */
    async load(filters = {}) {
        try {
            const mergedFilters = { ...this.currentFilters, ...filters };
            const queryParams = this._buildQueryParams(mergedFilters);
            const pageParams = `page=${this.currentPage}&pageSize=${this.pageSize}`;
            const sep = queryParams ? '&' : '';
            const url = `/patients/paged?${pageParams}${sep}${queryParams}`;

            const response = await this._apiGet(url);
            this.patients = response?.Items || response?.items || [];
            this.totalCount = response?.TotalCount || response?.totalCount || 0;
            this.totalPages = response?.TotalPages || response?.totalPages || 0;
            this.currentPage = response?.Page || response?.page || 1;
            this._render();
            this._updatePagination();
            this._emit('patients:loaded', { patients: this.patients, filters: mergedFilters });
        } catch (error) {
            console.error('[PatientModule] Failed to load patients:', error);
            this.renderer.renderError('Failed to load patients', this.tableBody || this.container);
            throw error;
        }
    }

    /**
     * Apply current filters and reload
     * @returns {Promise<void>}
     */
    async applyFilters() {
        try {
            this.currentPage = 1;
            await this.load();
        } catch (error) {
            console.error('[PatientModule] Error applying filters:', error);
        }
    }

    /**
     * Clear all filters and reload
     * @returns {Promise<void>}
     */
    async clearFilters() {
        try {
            // Reset filters
            this.currentFilters = {
                status: 'all',
                validation: 'all',
                episodes: 'all',
                showArchived: false,
                search: ''
            };

            // Reset UI elements
            const statusAllBtn = document.querySelector('#patientStatusFilter .filter-btn[data-value="all"]');
            if (statusAllBtn) {
                statusAllBtn.click();
            }

            const profileAllBtn = document.querySelector('#patientValidationFilter .filter-btn[data-value=""]');
            if (profileAllBtn) {
                profileAllBtn.click();
            }

            const searchInput = document.getElementById('patientSearch');
            if (searchInput) {
                searchInput.value = '';
            }

            const archivedCheckbox = document.getElementById('includeArchivedCheckbox');
            if (archivedCheckbox) {
                archivedCheckbox.checked = false;
            }

            await this.load();
        } catch (error) {
            console.error('[PatientModule] Error clearing filters:', error);
        }
    }

    /**
     * Load and display patient detail view
     * @param {number} patientId - Patient ID
     * @returns {Promise<void>}
     */
    async view(patientId) {
        try {
            const patient = await this._apiGet(`/patients/${patientId}`);
            this.currentPatient = patient;
            this.renderer.renderDetailModal(
                patient,
                () => this._showModal('patientDetailModal'),
                (id) => this.documentManager.loadViewPatientDocuments(id)
            );
        } catch (error) {
            console.error('[PatientModule] Failed to load patient detail:', error);
            this._showError('Failed to load patient details');
        }
    }

    /**
     * Load patient for editing
     * @param {number} patientId - Patient ID
     * @returns {Promise<void>}
     */
    /**
     * Edit a patient
     * @param {number} patientId - Patient ID
     * @param {string} activeTab - Tab to activate after modal opens (optional)
     * @returns {Promise<void>}
     */
    async edit(patientId, activeTab = null) {
        try {
            const patient = await this._apiGet(`/patients/${patientId}`);
            this.currentPatient = patient;

            // Clear authorization history from previous patient
            const primaryAuthBody = document.getElementById('primaryAuthHistoryBody');
            if (primaryAuthBody) {
                primaryAuthBody.innerHTML = `
                    <tr>
                        <td colspan="6" class="text-center text-muted py-3">
                            <i class="bi bi-info-circle me-1"></i>Loading...
                        </td>
                    </tr>
                `;
            }

            // Show modal first to ensure form elements are in the DOM
            this._showModal('patientModal');

            // Populate form with callbacks for attachments and authorization history
            setTimeout(() => {
                this.formHandler.populateForm(patient, {
                    onLoadAttachments: (pId) => this.documentManager.loadPatientAttachments(pId, (url) => this._apiGet(url)),
                    onLoadHistory: (type) => this.refreshAuthHistory(type)
                });

                // Show saved verification status for insurance
                const primaryIns = patient.Insurances?.find(i => i.Type === 0);
                const secondaryIns = patient.Insurances?.find(i => i.Type === 1);
                if (primaryIns) this._insuranceManager.showSavedVerificationStatus('primary', primaryIns);
                if (secondaryIns) this._insuranceManager.showSavedVerificationStatus('secondary', secondaryIns);

                // If activeTab is specified, switch to that tab
                if (activeTab) {
                    this._activateEditModalTab(activeTab);
                }
            }, 100);
        } catch (error) {
            console.error('[PatientModule] Failed to load patient for edit:', error);
            this._showError('Failed to load patient for editing');
        }
    }

    /**
     * Activate a specific tab in the Edit Patient modal
     * @param {string} tabName - Tab name (basic-info, emergency-contact, insurance, attachments, consent)
     * @private
     */
    _activateEditModalTab(tabName) {
        // The edit modal uses button tabs with data-bs-target attribute
        const tabTrigger = document.querySelector(`#patientModal button[data-bs-target="#${tabName}"]`);
        if (tabTrigger) {
            const tab = new bootstrap.Tab(tabTrigger);
            tab.show();
        }
    }

    /**
     * Delete a patient
     * @param {number} patientId - Patient ID
     * @param {string} patientName - Patient name (for confirmation)
     * @returns {Promise<void>}
     */
    async delete(patientId, patientName) {
        try {
            const confirmed = await this._confirm({
                title: 'Delete Patient',
                message: `Are you sure you want to delete ${patientName}?`
            });

            if (!confirmed) return;

            await this._apiDelete(`/patients/${patientId}`);
            this._showSuccess(`${patientName} has been deleted`);
            this._emit('patients:deleted', { patientId, patientName });
            await this.load();
        } catch (error) {
            console.error('[PatientModule] Failed to delete patient:', error);
            this._showError(error.message || 'Failed to delete patient');
        }
    }

    /**
     * Archive a patient
     * @param {number} patientId - Patient ID
     * @param {string} patientName - Patient name (for confirmation)
     * @returns {Promise<void>}
     */
    async archive(patientId, patientName) {
        try {
            const confirmed = await this._confirm({
                title: 'Archive Patient',
                message: `Are you sure you want to archive ${patientName}?`
            });

            if (!confirmed) return;

            await this._apiPost(`/patients/${patientId}/archive`, {});
            this._showSuccess(`${patientName} has been archived`);
            this._emit('patients:archived', { patientId, patientName });
            await this.load();
        } catch (error) {
            console.error('[PatientModule] Failed to archive patient:', error);
            this._showError(error.message || 'Failed to archive patient');
        }
    }

    /**
     * Unarchive a patient
     * @param {number} patientId - Patient ID
     * @param {string} patientName - Patient name (for confirmation)
     * @returns {Promise<void>}
     */
    async unarchive(patientId, patientName) {
        try {
            const confirmed = await this._confirm({
                title: 'Unarchive Patient',
                message: `Are you sure you want to unarchive ${patientName}?`
            });

            if (!confirmed) return;

            await this._apiPost(`/patients/${patientId}/unarchive`, {});
            this._showSuccess(`${patientName} has been restored`);
            this._emit('patients:unarchived', { patientId, patientName });
            await this.load();
        } catch (error) {
            console.error('[PatientModule] Failed to unarchive patient:', error);
            this._showError(error.message || 'Failed to unarchive patient');
        }
    }

    /**
     * Validate insurance for a patient
     * @param {string} type - Insurance type ('primary' or 'secondary')
     * @returns {Promise<void>}
     */
    async validateInsurance(type = 'primary') {
        const statusEl = document.getElementById(`${type}InsuranceValidationStatus`);
        const resultsEl = document.getElementById(`${type}EligibilityResults`);

        try {
            const prefix = type === 'primary' ? 'PrimaryInsurance' : 'SecondaryInsurance';
            const payerName = document.querySelector(`[name="${prefix}.PayerName"]`)?.value;

            if (!payerName) {
                this._showError(`Please select a payer name first`);
                return;
            }

            if (statusEl) {
                statusEl.innerHTML = '<span class="text-info"><span class="spinner-border spinner-border-sm me-1"></span>Checking eligibility with Office Ally...</span>';
            }

            // For existing patients with saved insurance, use the verify-by-id endpoint
            let insuranceId = null;
            if (this.currentPatient?.Insurances) {
                insuranceId = this.insuranceManager.getInsuranceId(this.currentPatient, type);
            }

            let result;
            if (insuranceId) {
                result = await this._apiPost(`/insurance/${insuranceId}/verify`, {});
            } else {
                // For unsaved patients, use direct verification with form fields
                const payerId = document.querySelector(`[name="${prefix}.PayerId"]`)?.value;
                const policyNumber = document.querySelector(`[name="${prefix}.PolicyNumber"]`)?.value;
                const groupNumber = document.querySelector(`[name="${prefix}.GroupNumber"]`)?.value;
                const subscriberFirstName = document.querySelector(`[name="${prefix}.SubscriberFirstName"]`)?.value;
                const subscriberLastName = document.querySelector(`[name="${prefix}.SubscriberLastName"]`)?.value;
                const subscriberDob = document.querySelector(`[name="${prefix}.SubscriberDob"]`)?.value;

                result = await this._apiPost(`/insurance/verify`, {
                    Type: type === 'primary' ? 0 : 1,
                    PayerName: payerName,
                    PayerId: payerId,
                    PolicyNumber: policyNumber,
                    GroupNumber: groupNumber,
                    SubscriberName: `${subscriberFirstName || ''} ${subscriberLastName || ''}`.trim(),
                    SubscriberDob: subscriberDob || null
                });
            }

            if (result?.Success) {
                if (statusEl) {
                    statusEl.innerHTML = result.IsEligible
                        ? '<span class="text-success"><i class="bi bi-check-circle-fill me-1"></i>Eligible</span>'
                        : '<span class="text-danger"><i class="bi bi-x-circle-fill me-1"></i>Not Eligible</span>';
                }
                this._showEligibilityResults(type, result);
                this._showSuccess('Eligibility verification completed');
            } else {
                if (statusEl) {
                    statusEl.innerHTML = `<span class="text-danger"><i class="bi bi-exclamation-triangle me-1"></i>${result?.ErrorMessage || 'Verification failed'}</span>`;
                }
                if (resultsEl) resultsEl.style.display = 'none';
                this._showError(result?.ErrorMessage || 'Eligibility verification failed');
            }
        } catch (error) {
            console.error('[PatientModule] Insurance validation error:', error);
            if (statusEl) {
                statusEl.innerHTML = `<span class="text-danger"><i class="bi bi-x-circle me-1"></i>${error.message || 'Failed'}</span>`;
            }
            if (resultsEl) resultsEl.style.display = 'none';
            this._showError(error.message || 'Failed to validate insurance');
        }
    }

    /**
     * Display eligibility verification results
     * @param {string} type - 'primary' or 'secondary'
     * @param {Object} result - InsuranceVerificationResult
     */
    _showEligibilityResults(type, result) {
        const el = document.getElementById(`${type}EligibilityResults`);
        if (!el) return;

        const fmt = (v, prefix = '$') => v != null ? `${prefix}${parseFloat(v).toFixed(2)}` : 'N/A';
        const fmtPct = (v) => v != null ? `${parseFloat(v).toFixed(0)}%` : 'N/A';
        const fmtInt = (v) => v != null ? v : 'N/A';

        el.innerHTML = `
            <div class="card border-${result.IsEligible ? 'success' : 'danger'} mt-2">
                <div class="card-header bg-${result.IsEligible ? 'success' : 'danger'} text-white py-2">
                    <strong><i class="bi bi-${result.IsEligible ? 'shield-check' : 'shield-x'} me-1"></i>
                    ${result.IsEligible ? 'ELIGIBLE' : 'NOT ELIGIBLE'}
                    ${result.PlanName ? ` - ${result.PlanName}` : ''}
                    ${result.PlanType ? ` (${result.PlanType})` : ''}</strong>
                </div>
                <div class="card-body py-2">
                    <div class="row g-2" style="font-size: 0.85rem;">
                        <div class="col-md-4">
                            <strong>Copay:</strong> ${fmt(result.CopayInNetwork || result.Copay)}
                            ${result.CopayOutOfNetwork ? `<br><small class="text-muted">Out-of-network: ${fmt(result.CopayOutOfNetwork)}</small>` : ''}
                        </div>
                        <div class="col-md-4">
                            <strong>Coinsurance:</strong> ${fmtPct(result.CoinsuranceInNetwork || result.Coinsurance)}
                            ${result.CoinsuranceOutOfNetwork ? `<br><small class="text-muted">Out-of-network: ${fmtPct(result.CoinsuranceOutOfNetwork)}</small>` : ''}
                        </div>
                        <div class="col-md-4">
                            <strong>Deductible:</strong> ${fmt(result.IndividualDeductible || result.DeductibleTotal)}
                            ${result.IndividualDeductibleMet != null ? `<br><small class="text-muted">Met: ${fmt(result.IndividualDeductibleMet)} / Remaining: ${fmt(result.IndividualDeductibleRemaining)}</small>` : ''}
                        </div>
                        <div class="col-md-4">
                            <strong>OOP Max:</strong> ${fmt(result.IndividualOopMax || result.OutOfPocketMax)}
                            ${result.IndividualOopMet != null ? `<br><small class="text-muted">Met: ${fmt(result.IndividualOopMet)}</small>` : ''}
                        </div>
                        <div class="col-md-4">
                            <strong>Visits:</strong> ${fmtInt(result.AllowedVisits)} allowed
                            ${result.VisitsUsed != null ? ` / ${result.VisitsUsed} used` : ''}
                            ${result.VisitsRemaining != null ? ` / ${result.VisitsRemaining} remaining` : ''}
                        </div>
                        <div class="col-md-4">
                            ${result.RequiresPriorAuthorization ? '<span class="badge bg-warning text-dark"><i class="bi bi-exclamation-triangle me-1"></i>Prior Auth Required</span>' : '<span class="badge bg-info">No Prior Auth Required</span>'}
                        </div>
                        ${result.CoverageEffectiveDate || result.CoverageTerminationDate ? `
                        <div class="col-12">
                            <small class="text-muted">
                                <strong>Coverage:</strong> ${result.CoverageEffectiveDate || '?'} to ${result.CoverageTerminationDate || 'ongoing'}
                                ${result.BenefitPeriod ? ` (${result.BenefitPeriod})` : ''}
                            </small>
                        </div>` : ''}
                        ${result.CoverageNotes ? `
                        <div class="col-12">
                            <small class="text-muted"><strong>Notes:</strong> ${result.CoverageNotes}</small>
                        </div>` : ''}
                    </div>
                </div>
            </div>
        `;
        el.style.display = 'block';
    }

    /**
     * Refresh authorization history for a patient
     * @param {string} type - Insurance type ('primary' or 'secondary')
     * @returns {Promise<void>}
     */
    async refreshAuthHistory(type = 'primary') {
        try {
            if (!this.currentPatient) return;

            const insuranceId = this.insuranceManager.getInsuranceId(this.currentPatient, type);
            if (!insuranceId) {
                console.warn(`[PatientModule] No ${type} insurance found for authorization history`);
                return;
            }

            const authHistory = await this._apiGet(`/authorizations/insurance/${insuranceId}`);
            const historyBody = document.getElementById(`${type}AuthHistoryBody`);

            if (!historyBody) return;

            if (!authHistory || authHistory.length === 0) {
                historyBody.innerHTML = `
                    <tr>
                        <td colspan="6" class="text-center text-muted py-3">
                            <i class="bi bi-info-circle me-1"></i>No authorization history available
                        </td>
                    </tr>
                `;
                return;
            }

            historyBody.innerHTML = authHistory.map(auth =>
                this.insuranceManager.renderAuthorizationRow(auth, insuranceId)
            ).join('');

            // Add event listeners to edit buttons
            historyBody.querySelectorAll('[data-action="edit-auth"]').forEach(btn => {
                btn.addEventListener('click', (e) => {
                    e.preventDefault();
                    this.editAuthorization(btn.dataset.authId, insuranceId);
                });
            });
        } catch (error) {
            console.error('[PatientModule] Failed to refresh authorization history:', error);
            const historyBody = document.getElementById(`${type}AuthHistoryBody`);
            if (historyBody) {
                historyBody.innerHTML = `
                    <tr>
                        <td colspan="6" class="text-center text-danger py-3">
                            <i class="bi bi-exclamation-triangle me-1"></i>Failed to load authorization history
                        </td>
                    </tr>
                `;
            }
        }
    }

    /**
     * Fetch mock authorization data
     * @param {string} type - Insurance type ('primary' or 'secondary')
     * @returns {Promise<void>}
     */
    async fetchMockAuthorization(type = 'primary') {
        try {
            const prefix = type === 'primary' ? 'PrimaryInsurance' : 'SecondaryInsurance';
            const payerName = document.querySelector(`[name="${prefix}.PayerName"]`)?.value;

            if (!payerName) {
                this._showError(`Please enter a ${type} payer name first`);
                return;
            }

            // Check if patient is saved and has insurance ID
            let insuranceId = null;
            if (this.currentPatient?.Insurances) {
                insuranceId = this.insuranceManager.getInsuranceId(this.currentPatient, type);
            }

            // Show loading state
            const authModal = document.getElementById('authorizationModal');
            if (authModal) {
                const title = authModal.querySelector('.modal-title');
                const body = authModal.querySelector('.modal-body');
                if (title) title.textContent = 'Loading...';
                if (body) body.innerHTML = '<div class="text-center"><div class="spinner-border" role="status"><span class="visually-hidden">Loading...</span></div></div>';
                this._showModal('authorizationModal');
            }

            // Fetch or generate mock authorization
            let auth;
            if (insuranceId) {
                // Patient is saved - fetch from API
                auth = await this._apiPost(`/authorizations/insurance/${insuranceId}/fetch`, {});
            } else {
                // New patient - generate mock data
                auth = {
                    AuthorizationNumber: `AUTH-${new Date().toISOString().slice(0,10).replace(/-/g,'')}-${Math.floor(Math.random() * 9000) + 1000}`,
                    AuthorizedVisits: Math.floor(Math.random() * 21) + 10,
                    EffectiveDate: new Date().toISOString().split('T')[0],
                    ExpiryDate: new Date(new Date().setMonth(new Date().getMonth() + Math.floor(Math.random() * 10) + 3)).toISOString().split('T')[0],
                    Notes: `Mock authorization for ${payerName}`
                };
            }

            if (!auth) {
                throw new Error('Failed to fetch authorization data');
            }

            // Store for save
            this._pendingAuthorization = { ...auth, InsuranceId: insuranceId || 'new', Type: type };

            // Render in modal
            const modal = document.getElementById('authorizationModal');
            if (modal) {
                const title = modal.querySelector('.modal-title');
                if (title) {
                    title.textContent = `Authorization - ${auth.AuthorizationNumber || 'New'}`;
                }

                const body = modal.querySelector('.modal-body');
                if (body) {
                    body.innerHTML = `
                        <form id="authorizationForm">
                            <div class="row">
                                <div class="col-md-6">
                                    <div class="mb-3">
                                        <label for="authNumber" class="form-label">Authorization Number</label>
                                        <input type="text" class="form-control" id="authNumber" value="${this.utilities.escape(auth.AuthorizationNumber || '')}">
                                    </div>
                                    <div class="mb-3">
                                        <label for="authEffectiveDate" class="form-label">Effective Date</label>
                                        <input type="date" class="form-control" id="authEffectiveDate" value="${(auth.EffectiveDate || '').split('T')[0]}">
                                    </div>
                                    <div class="mb-3">
                                        <label for="authExpiryDate" class="form-label">Expiry Date</label>
                                        <input type="date" class="form-control" id="authExpiryDate" value="${(auth.ExpiryDate || '').split('T')[0]}">
                                    </div>
                                </div>
                                <div class="col-md-6">
                                    <div class="mb-3">
                                        <label for="authAuthorizedVisits" class="form-label">Authorized Visits</label>
                                        <input type="number" class="form-control" id="authAuthorizedVisits" value="${auth.AuthorizedVisits || ''}">
                                    </div>
                                    <div class="mb-3">
                                        <label for="authUsedVisits" class="form-label">Used Visits</label>
                                        <input type="number" class="form-control" id="authUsedVisits" value="${auth.UsedVisits || ''}">
                                    </div>
                                    <div class="mb-3">
                                        <label for="authRemainingVisits" class="form-label">Remaining Visits</label>
                                        <input type="number" class="form-control" id="authRemainingVisits" value="${auth.RemainingVisits || ''}" readonly>
                                    </div>
                                </div>
                            </div>
                            <div class="mb-3">
                                <label for="authNotes" class="form-label">Notes</label>
                                <textarea class="form-control" id="authNotes" rows="3">${this.utilities.escape(auth.Notes || '')}</textarea>
                            </div>
                        </form>
                    `;
                }

                // Update footer buttons
                const footer = modal.querySelector('.modal-footer');
                if (footer) {
                    footer.innerHTML = `
                        <button type="button" class="btn btn-secondary" onclick="window.patientModule._hideModal('authorizationModal')">Cancel</button>
                        <button type="button" class="btn btn-primary" onclick="window.patientModule.saveAuthorization(event)">Save Authorization</button>
                    `;
                }
            }

            this._showSuccess('Authorization data loaded');
        } catch (error) {
            console.error('[PatientModule] Failed to fetch authorization:', error);
            this._showError(error.message || 'Failed to fetch authorization data');
        }
    }

    /**
     * Edit an existing authorization
     * @param {number} authId - Authorization ID
     * @param {number} insuranceId - Insurance ID
     * @returns {Promise<void>}
     */
    async editAuthorization(authId, insuranceId) {
        try {
            const auth = await this._apiGet(`/authorizations/${authId}`);

            // Store for save
            this._pendingAuthorization = { ...auth, InsuranceId: insuranceId };

            // Render in modal
            const modal = document.getElementById('authorizationModal');
            if (modal) {
                const title = modal.querySelector('.modal-title');
                if (title) {
                    title.textContent = `Edit Authorization - ${auth.AuthorizationNumber || 'Unknown'}`;
                }

                const body = modal.querySelector('.modal-body');
                if (body) {
                    body.innerHTML = `
                        <form id="authorizationForm">
                            <div class="row">
                                <div class="col-md-6">
                                    <div class="mb-3">
                                        <label for="authNumber" class="form-label">Authorization Number</label>
                                        <input type="text" class="form-control" id="authNumber" value="${this.utilities.escape(auth.AuthorizationNumber || '')}">
                                    </div>
                                    <div class="mb-3">
                                        <label for="authEffectiveDate" class="form-label">Effective Date</label>
                                        <input type="date" class="form-control" id="authEffectiveDate" value="${(auth.EffectiveDate || '').split('T')[0]}">
                                    </div>
                                    <div class="mb-3">
                                        <label for="authExpiryDate" class="form-label">Expiry Date</label>
                                        <input type="date" class="form-control" id="authExpiryDate" value="${(auth.ExpiryDate || '').split('T')[0]}">
                                    </div>
                                </div>
                                <div class="col-md-6">
                                    <div class="mb-3">
                                        <label for="authAuthorizedVisits" class="form-label">Authorized Visits</label>
                                        <input type="number" class="form-control" id="authAuthorizedVisits" value="${auth.AuthorizedVisits || ''}">
                                    </div>
                                    <div class="mb-3">
                                        <label for="authUsedVisits" class="form-label">Used Visits</label>
                                        <input type="number" class="form-control" id="authUsedVisits" value="${auth.UsedVisits || ''}">
                                    </div>
                                    <div class="mb-3">
                                        <label for="authRemainingVisits" class="form-label">Remaining Visits</label>
                                        <input type="number" class="form-control" id="authRemainingVisits" value="${auth.RemainingVisits || ''}" readonly>
                                    </div>
                                </div>
                            </div>
                            <div class="mb-3">
                                <label for="authNotes" class="form-label">Notes</label>
                                <textarea class="form-control" id="authNotes" rows="3">${this.utilities.escape(auth.Notes || '')}</textarea>
                            </div>
                        </form>
                    `;
                }

                // Update footer buttons
                const footer = modal.querySelector('.modal-footer');
                if (footer) {
                    footer.innerHTML = `
                        <button type="button" class="btn btn-secondary" onclick="window.patientModule._hideModal('authorizationModal')">Cancel</button>
                        <button type="button" class="btn btn-danger" onclick="window.patientModule.confirmDeleteAuthorization()">Delete</button>
                        <button type="button" class="btn btn-primary" onclick="window.patientModule.saveAuthorization(event)">Update Authorization</button>
                    `;
                }
            }

            this._showModal('authorizationModal');
        } catch (error) {
            console.error('[PatientModule] Failed to edit authorization:', error);
            this._showError(error.message || 'Failed to load authorization');
        }
    }

    /**
     * Save authorization
     * @param {Event} e - Event object
     * @returns {Promise<void>}
     */
    async saveAuthorization(e) {
        if (e) {
            e.preventDefault();
        }

        try {
            if (!this._pendingAuthorization) {
                this._showError('No authorization data to save');
                return;
            }

            const authData = {
                AuthorizationNumber: document.getElementById('authNumber')?.value,
                EffectiveDate: document.getElementById('authEffectiveDate')?.value,
                ExpiryDate: document.getElementById('authExpiryDate')?.value,
                AuthorizedVisits: parseInt(document.getElementById('authAuthorizedVisits')?.value) || null,
                UsedVisits: parseInt(document.getElementById('authUsedVisits')?.value) || 0,
                Notes: document.getElementById('authNotes')?.value
            };

            // For new patients, authorization can only be added after insurance is saved
            if (this._pendingAuthorization.InsuranceId === 'new') {
                this._showError('Please save the patient with insurance first, then you can add authorizations');
                this._hideModal('authorizationModal');
                return;
            }

            // For saved patients, save directly to API
            const url = this._pendingAuthorization.AuthorizationId
                ? `/authorizations/${this._pendingAuthorization.AuthorizationId}`
                : `/authorizations`;

            const method = this._pendingAuthorization.AuthorizationId ? 'PUT' : 'POST';

            // Add InsuranceId for POST (create) requests
            if (method === 'POST') {
                authData.InsuranceId = this._pendingAuthorization.InsuranceId;
            }

            if (method === 'PUT') {
                await this._apiPut(url, authData);
            } else {
                await this._apiPost(url, authData);
            }

            this._hideModal('authorizationModal');
            this._showSuccess('Authorization saved successfully');

            // Refresh the authorization history for the insurance that was just saved
            const insuranceType = this._pendingAuthorization.Type || 'primary';
            await this.refreshAuthHistory(insuranceType);

            this._pendingAuthorization = null;
        } catch (error) {
            console.error('[PatientModule] Failed to save authorization:', error);
            this._showError(error.message || 'Failed to save authorization');
        }
    }

    /**
     * Confirm delete authorization
     * @returns {Promise<void>}
     */
    async confirmDeleteAuthorization() {
        try {
            if (!this._pendingAuthorization?.AuthorizationId) {
                this._showError('Cannot delete unsaved authorization');
                return;
            }

            const confirmed = await this._confirm({
                title: 'Delete Authorization',
                message: 'Are you sure you want to delete this authorization?'
            });

            if (!confirmed) return;

            // Store insurance type before clearing pending authorization
            const insuranceType = this._pendingAuthorization.Type || 'primary';

            await this._apiDelete(`/authorizations/${this._pendingAuthorization.AuthorizationId}`);
            this._hideModal('authorizationModal');
            this._showSuccess('Authorization deleted successfully');
            this._pendingAuthorization = null;

            // Refresh the authorization history
            await this.refreshAuthHistory(insuranceType);
        } catch (error) {
            console.error('[PatientModule] Failed to delete authorization:', error);
            this._showError(error.message || 'Failed to delete authorization');
        }
    }

    /**
     * Open authorization modal for new authorization
     * @returns {Promise<void>}
     */
    async openAuthorizationModal() {
        await this.fetchMockAuthorization('primary');
    }

    /**
     * Handle form submission
     * @private
     * @param {Event} e - Form submit event
     * @returns {Promise<void>}
     */
    async _handleFormSubmit(e) {
        e.preventDefault();
        e.stopPropagation();

        const formData = new FormData(e.target);
        const patientId = formData.get('PatientId');
        const isEdit = patientId && patientId !== '';

        const data = this.formHandler.extractFormData(formData);

        // Email duplicate — HARD BLOCK (no override).
        // Re-check at submit time in case the user edited the email after
        // the blur-validation ran. If a duplicate is detected, paint a red
        // inline error under the email field and abort the save. The user
        // must change the email before they can save.
        if (data && data.Email && data.Email.trim()) {
            try {
                const qs = new URLSearchParams({ email: data.Email.trim() });
                if (isEdit) qs.set('excludePatientId', patientId);
                const dupResult = await this._apiGet(`/patients/check-email?${qs.toString()}`);
                if (dupResult && dupResult.isUnique === false) {
                    const name = dupResult.existingPatientName || 'another patient';
                    const emailInput = document.querySelector('#patientForm [name="Email"]');
                    if (emailInput && this.formHandler && typeof this.formHandler._setEmailDuplicateError === 'function') {
                        this.formHandler._setEmailDuplicateError(emailInput, name);
                        emailInput.focus();
                    }
                    return; // block save
                }
            } catch (err) {
                console.debug('[PatientModule] pre-save email dup check skipped', err);
            }
        }

        try {
            let response;
            if (isEdit) {
                response = await this._apiPut(`/patients/${patientId}`, data);
                this._showSuccess('Patient updated successfully');
                this._emit('patients:updated', { patientId, data });
            } else {
                response = await this._apiPost('/patients', data);
                if (!response) {
                    throw new Error('Server returned empty response. Please check if you are logged in.');
                }
                this._emit('patients:created', { patient: response });

                // Upload any pending files for the new patient
                if (this.pendingFiles.length > 0 && response.PatientId) {
                    await this._uploadPendingFiles(response.PatientId);
                }

                // Upload pending profile photo
                if (AvatarUtils.hasPendingPhoto('patient') && response.PatientId) {
                    try {
                        const photoOk = await AvatarUtils.uploadPendingPhoto('patient', response.PatientId);
                        if (photoOk) {
                            this._showSuccess('Patient created with photo');
                        } else {
                            this._showSuccess('Patient created successfully');
                        }
                    } catch (photoError) {
                        console.error('[PatientModule] Photo upload failed:', photoError);
                        this._showWarning('Patient created but photo upload failed');
                    }
                } else {
                    this._showSuccess('Patient created successfully');
                }

                // Show post-save modal for new patients (like legacy code)
                this._showPostSaveModal(response);
            }

            this._hideModal('patientModal');
            e.target.reset();

            // Clear pending files when modal closes
            this.pendingFiles = [];

            // Only reload if we have a table to populate
            if (this.tableBody) {
                await this.load();
            }
        } catch (error) {
            console.error('[PatientModule] Save patient error:', error);
            // Server-side duplicate-email block (409 Conflict) — render the
            // same inline error as the client-side check, so the message
            // looks identical no matter which guard catches it.
            const msg = (error && error.message) ? String(error.message) : '';
            if (msg.toLowerCase().includes('already in use')) {
                const m = msg.match(/by patient (.+?)(?:\.|$)/i);
                const name = m ? m[1].trim() : '';
                const emailInput = document.querySelector('#patientForm [name="Email"]');
                if (emailInput && this.formHandler && typeof this.formHandler._setEmailDuplicateError === 'function') {
                    this.formHandler._setEmailDuplicateError(emailInput, name);
                    emailInput.focus();
                    return;
                }
            }
            this._showError(msg || 'Failed to save patient');
        }
    }

    /**
     * Build query parameters from filters
     * @private
     * @param {Object} filters - Filter object
     * @returns {string} Query string
     */
    _buildQueryParams(filters) {
        const params = new URLSearchParams();

        // Handle status filter - backend expects 'statuses' (plural) with numeric values
        // or 'noCareEpisode=true' for no care episode filter
        if (filters.status && filters.status !== 'all') {
            if (filters.status === 'no-episode') {
                // Special case: filter for patients without care episodes
                params.append('noCareEpisode', 'true');
            } else {
                // Regular status filter - pass numeric value
                // 0 = Active, 2 = Discharged, etc.
                params.append('statuses', filters.status);
            }
        }

        // Handle archived filter - always send the archived status to backend
        // When unchecked (false), explicitly request non-archived patients only
        // When checked (true), request archived patients
        params.append('isArchived', filters.showArchived ? 'true' : 'false');

        // Handle validation/profile completeness filter
        // validation values: 'all' (no filter), '1' (incomplete), '2' (complete)
        if (filters.validation && filters.validation !== 'all') {
            if (filters.validation === '1') {
                // Incomplete profiles
                params.append('isProfileComplete', 'false');
            } else if (filters.validation === '2') {
                // Complete profiles
                params.append('isProfileComplete', 'true');
            }
        }

        if (filters.search) {
            params.append('search', filters.search);
        }

        return params.toString();
    }

    /**
     * Render patients list
     * @private
     */
    _render() {
        const target = this.tableBody || this.container;
        if (!target) return;

        if (!this.patients?.length) {
            target.innerHTML = this.renderer.renderEmptyState();
            return;
        }

        const currentUser = this.utilities.getCurrentUser();
        const role = parseInt(currentUser?.Role ?? -1);
        const isRestricted = role === 2 || UserRoles.isMaNurse(role);

        target.innerHTML = this.patients.map(patient => this.renderer.renderPatientRow(patient, isRestricted)).join('');
    }

    /**
     * Update pagination controls
     * @private
     */
    _updatePagination() {
        const controls = document.getElementById('patientPaginationControls');
        const buttons = document.getElementById('patientPaginationButtons');
        const info = document.getElementById('patientPaginationInfo');
        if (!controls || !buttons) return;

        if (this.totalCount <= this.pageSize) {
            controls.style.display = 'none';
            if (info) info.textContent = `Showing ${this.totalCount} of ${this.totalCount}`;
            return;
        }
        controls.style.display = '';
        controls.style.removeProperty('display');

        const start = (this.currentPage - 1) * this.pageSize + 1;
        const end = Math.min(this.currentPage * this.pageSize, this.totalCount);
        if (info) info.textContent = `Showing ${start}–${end} of ${this.totalCount}`;

        let html = '';
        html += `<li class="page-item ${this.currentPage <= 1 ? 'disabled' : ''}"><a class="page-link" href="#" data-page="${this.currentPage - 1}">&laquo;</a></li>`;

        let startPage = Math.max(1, this.currentPage - 2);
        let endPage = Math.min(this.totalPages, startPage + 4);
        if (endPage - startPage < 4) startPage = Math.max(1, endPage - 4);

        for (let p = startPage; p <= endPage; p++) {
            html += `<li class="page-item ${p === this.currentPage ? 'active' : ''}"><a class="page-link" href="#" data-page="${p}">${p}</a></li>`;
        }

        html += `<li class="page-item ${this.currentPage >= this.totalPages ? 'disabled' : ''}"><a class="page-link" href="#" data-page="${this.currentPage + 1}">&raquo;</a></li>`;
        buttons.innerHTML = html;
    }

    /**
     * Handle file upload
     * @private
     * @param {FileList} files - Files to upload
     * @returns {Promise<void>}
     */
    async _handleFileUpload(files) {
        const patientId = this.currentPatient?.PatientId;
        const patientIdInput = document.getElementById('patientId');
        const currentPatientId = patientIdInput?.value ? parseInt(patientIdInput.value) : null;

        // Get selected category from dropdown
        const categorySelect = document.getElementById('documentCategory');
        const category = categorySelect ? parseInt(categorySelect.value) : 5; // Default to 5 (Other)

        // Get description from form
        const descriptionInput = document.getElementById('documentDescription');
        const description = descriptionInput?.value || '';

        await this.documentManager.handleFileUpload(files, {
            patientId: patientId || currentPatientId,
            category: category,
            description: description,
            onSuccess: () => {
                this._showSuccess('File uploaded successfully');
                if (patientId || currentPatientId) {
                    this.documentManager.loadPatientAttachments(patientId || currentPatientId, (url) => this._apiGet(url));
                }
            },
            onError: (msg) => {
                this._showError(msg);
            },
            onQueueFile: (files, category, description) => {
                // files is an array, add all to pending with unique IDs
                for (const file of files) {
                    this.pendingFiles.push({
                        id: Date.now() + Math.random(), // Unique ID for removal
                        file,
                        category,
                        description
                    });
                }
                this._showInfo(`${files.length} file(s) queued for upload (will be uploaded when patient is saved)`);
                // Render pending files in the UI
                this.documentManager.renderPendingFiles(this.pendingFiles);
            }
        });
    }

    /**
     * Upload pending files after patient creation
     * @private
     * @param {number} patientId - Patient ID
     * @returns {Promise<void>}
     */
    async _uploadPendingFiles(patientId) {
        if (this.pendingFiles.length === 0) return;

        try {
            for (const item of this.pendingFiles) {
                const fileList = new DataTransfer();
                fileList.items.add(item.file);

                await this.documentManager.handleFileUpload(fileList.files, {
                    patientId,
                    category: item.category || 5, // Default to 5 (Other) to match form
                    description: item.description || '',
                    onSuccess: () => {
                        this._showSuccess(`Uploaded: ${item.file.name}`);
                    },
                    onError: (msg) => {
                        this._showWarning(msg);
                    }
                });
            }

            this.pendingFiles = [];
        } catch (error) {
            console.error('[PatientModule] Error uploading pending files:', error);
            this._showWarning('Some files could not be uploaded');
        }
    }

    /**
     * Remove a pending file from the queue before patient is saved
     * @param {number} fileId - Unique ID of the pending file
     */
    removePendingFile(fileId) {
        const index = this.pendingFiles.findIndex(f => f.id === fileId);
        if (index !== -1) {
            const removed = this.pendingFiles.splice(index, 1);
            this._showInfo(`Removed: ${removed[0].file.name}`);
            // Re-render pending files list
            this.documentManager.renderPendingFiles(this.pendingFiles);
        }
    }

    /**
     * Download a document
     * @param {number} documentId - Document ID
     * @param {number} patientId - Patient ID
     * @returns {Promise<void>}
     */
    async downloadDocument(documentId, patientId) {
        try {
            if (!patientId) {
                // Use current patient if patientId not provided
                patientId = this.currentPatient?.PatientId;
            }

            if (!patientId) {
                throw new Error('Patient ID is required to download document');
            }

            const response = await fetch(`/api/patients/${patientId}/documents/${documentId}`, {
                headers: this.utilities.getHeaders()
            });

            if (!response.ok) {
                throw new Error('Failed to download document');
            }

            const blob = await response.blob();
            const filename = response.headers.get('content-disposition')?.split('filename=')[1]?.trim() || `document_${documentId}`;
            const url = window.URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = filename;
            document.body.appendChild(a);
            a.click();
            a.remove();
            window.URL.revokeObjectURL(url);
        } catch (error) {
            console.error('[PatientModule] Failed to download document:', error);
            this._showError('Failed to download document');
        }
    }

    /**
     * Delete a document
     * @param {number} documentId - Document ID
     * @returns {Promise<void>}
     */
    async deleteDocument(documentId) {
        try {
            const confirmed = await this._confirm({
                title: 'Delete Document',
                message: 'Are you sure you want to delete this document?'
            });

            if (!confirmed) return;

            if (!this.currentPatient?.PatientId) {
                this._showError('Patient ID is required to delete document');
                return;
            }

            await this._apiDelete(`/patients/${this.currentPatient.PatientId}/documents/${documentId}`);
            this._showSuccess('Document deleted successfully');

            // Reload attachments in edit/view modals
            if (this.currentPatient?.PatientId) {
                // Reload in edit modal if open
                const patientModal = document.getElementById('patientModal');
                if (patientModal?.classList.contains('show')) {
                    await this.documentManager.loadPatientAttachments(this.currentPatient.PatientId, (url) => this._apiGet(url));
                }
                // Reload in view modal if open
                else {
                    await this.documentManager.loadViewPatientDocuments(this.currentPatient.PatientId);
                }
            }
        } catch (error) {
            console.error('[PatientModule] Failed to delete document:', error);
            this._showError('Failed to delete document');
        }
    }

    // === API Methods ===

    /**
     * API GET request
     * @private
     */
    async _apiGet(url) {
        if (this.api) {
            return this.api.get(url);
        }
        const response = await fetch(`/api${url}`, {
            method: 'GET',
            headers: this.utilities.getHeaders()
        });
        if (!response.ok) {
            const error = await response.json().catch(() => ({}));
            throw new Error(error.message || 'API request failed');
        }
        return response.json();
    }

    /**
     * API POST request
     * @private
     */
    async _apiPost(url, data) {
        if (this.api) {
            return this.api.post(url, data);
        }
        const response = await fetch(`/api${url}`, {
            method: 'POST',
            headers: this.utilities.getHeaders(),
            body: JSON.stringify(data)
        });
        if (!response.ok) {
            const error = await response.json().catch(() => ({}));
            throw new Error(error.message || 'API request failed');
        }
        return response.json();
    }

    /**
     * API PUT request
     * @private
     */
    async _apiPut(url, data) {
        if (this.api) {
            return this.api.put(url, data);
        }
        const response = await fetch(`/api${url}`, {
            method: 'PUT',
            headers: this.utilities.getHeaders(),
            body: JSON.stringify(data)
        });
        if (!response.ok) {
            const error = await response.json().catch(() => ({}));
            throw new Error(error.message || 'API request failed');
        }
        return response.json();
    }

    /**
     * API DELETE request
     * @private
     */
    async _apiDelete(url) {
        if (this.api) {
            return this.api.delete(url);
        }
        const response = await fetch(`/api${url}`, {
            method: 'DELETE',
            headers: this.utilities.getHeaders()
        });
        if (!response.ok) {
            const error = await response.json().catch(() => ({}));
            throw new Error(error.message || 'API request failed');
        }
        return response.json().catch(() => ({}));
    }

    // === Modal Methods ===

    /**
     * Show modal
     * @private
     */
    _showModal(id) {
        const el = document.getElementById(id);
        if (el && window.bootstrap) {
            new bootstrap.Modal(el).show();
        }
    }

    /**
     * Hide modal
     * @private
     */
    _hideModal(id) {
        const el = document.getElementById(id);
        if (el && window.bootstrap) {
            bootstrap.Modal.getInstance(el)?.hide();
        }
    }

    /**
     * Show post-save modal after creating a new patient
     * Offers option to create Initial Evaluation appointment
     * @private
     * @param {Object} patient - The newly created patient
     */
    _showPostSaveModal(patient) {
        if (!patient?.PatientId) return;

        // Store patient data for the post-save modal
        const patientIdField = document.getElementById('postSavePatientId');
        const patientDataField = document.getElementById('postSavePatientData');

        if (patientIdField) {
            patientIdField.value = patient.PatientId;
        }
        if (patientDataField) {
            patientDataField.value = JSON.stringify(patient);
        }

        // Show the modal with a slight delay to allow patient modal to close
        setTimeout(() => {
            const modalEl = document.getElementById('postSaveModal');
            if (modalEl && window.bootstrap) {
                const modal = new bootstrap.Modal(modalEl);
                modal.show();
            }
        }, 300);
    }

    /**
     * Show confirmation dialog
     * @private
     */
    async _confirm(options) {
        if (window.ConfirmDialog) {
            return ConfirmDialog.show(options);
        }
        return confirm(options.message);
    }

    // === Notification Methods ===

    /**
     * Show success notification
     * @private
     */
    _showSuccess(message) {
        if (window.Toast) {
            Toast.success('Success', message);
        }
    }

    /**
     * Show error notification
     * @private
     */
    _showError(message) {
        if (window.Toast) {
            Toast.error('Error', message);
        }
    }

    /**
     * Show warning notification
     * @private
     */
    _showWarning(message) {
        if (window.Toast) {
            Toast.warning('Warning', message);
        }
    }

    /**
     * Show info notification
     * @private
     */
    _showInfo(message) {
        if (window.Toast) {
            Toast.info('Info', message);
        }
    }

    /**
     * Emit event via EventBus
     * @private
     */
    _emit(event, data = {}) {
        if (this.eventBus) {
            this.eventBus.emit(event, data);
        }
    }

    // === Care Episode Methods ===

    /**
     * Open care episode modal for a patient
     * @private
     * @param {number} patientId - Patient ID
     * @param {number|null} episodeId - Care Episode ID for edit mode (optional)
     */
    async _openCareEpisodeModal(patientId, episodeId = null) {
        console.log('[PatientModule] Opening care episode modal for patient:', patientId, 'episode:', episodeId);

        // Use global function which will get or create CareEpisodeModule
        if (window.openCareEpisodeModal) {
            await window.openCareEpisodeModal(patientId, episodeId);
        } else {
            // Fallback to direct module access
            const careEpisodeModule = window.App?.modules?.get('careEpisodes') || window.careEpisodeModule;
            if (careEpisodeModule && careEpisodeModule.openModal) {
                await careEpisodeModule.openModal(patientId, episodeId);
            } else {
                this._showError('Care Episode module not available');
            }
        }
    }

    /**
     * View care episode details in sidebar
     * @private
     * @param {number} episodeId - Care Episode ID
     */
    async _viewCareEpisodeDetails(episodeId) {
        console.log('[PatientModule] Viewing care episode details:', episodeId);

        const careEpisodeModule = window.App?.modules?.get('careEpisodes') || window.careEpisodeModule;
        if (careEpisodeModule && careEpisodeModule.viewDetails) {
            await careEpisodeModule.viewDetails(episodeId);
        } else {
            // Try to initialize the module if not available
            if (window.getCareEpisodeModule) {
                const module = await window.getCareEpisodeModule();
                if (module && module.viewDetails) {
                    await module.viewDetails(episodeId);
                    return;
                }
            }
            this._showError('Care Episode module not available');
        }
    }

    /**
     * Complete a care episode
     * @private
     * @param {number} episodeId - Care Episode ID
     */
    async _completeCareEpisode(episodeId) {
        console.log('[PatientModule] Completing care episode:', episodeId);

        const careEpisodeModule = window.App?.modules?.get('careEpisodes') || window.careEpisodeModule;
        if (careEpisodeModule && careEpisodeModule.markComplete) {
            await careEpisodeModule.markComplete(episodeId);
            // Refresh the patient details to show updated status
            if (this.currentPatient) {
                await this.view(this.currentPatient.PatientId);
            }
        } else {
            // Fallback: Try to initialize the module
            if (window.getCareEpisodeModule) {
                const module = await window.getCareEpisodeModule();
                if (module && module.markComplete) {
                    await module.markComplete(episodeId);
                    if (this.currentPatient) {
                        await this.view(this.currentPatient.PatientId);
                    }
                    return;
                }
            }
            this._showError('Care Episode module not available');
        }
    }

    /**
     * Restore a completed care episode
     * @private
     * @param {number} episodeId - Care Episode ID
     */
    async _restoreCareEpisode(episodeId) {
        console.log('[PatientModule] Restoring care episode:', episodeId);

        const careEpisodeModule = window.App?.modules?.get('careEpisodes') || window.careEpisodeModule;
        if (careEpisodeModule && careEpisodeModule.restore) {
            await careEpisodeModule.restore(episodeId);
            // Refresh the patient details to show updated status
            if (this.currentPatient) {
                await this.view(this.currentPatient.PatientId);
            }
        } else {
            // Fallback: Try to initialize the module
            if (window.getCareEpisodeModule) {
                const module = await window.getCareEpisodeModule();
                if (module && module.restore) {
                    await module.restore(episodeId);
                    if (this.currentPatient) {
                        await this.view(this.currentPatient.PatientId);
                    }
                    return;
                }
            }
            this._showError('Care Episode module not available');
        }
    }

    /**
     * Destroy the module and clean up
     */
    destroy() {
        if (this.container) {
            this.container.removeEventListener('click', this._handleContainerClick);
        }
        if (this.tableBody) {
            this.tableBody.removeEventListener('click', this._handleContainerClick);
        }
        if (this.searchInput) {
            this.searchInput.removeEventListener('input', this._handleSearch);
        }

        this.patients = [];
        this.currentPatient = null;
        this.container = null;
        this.tableBody = null;
        this.searchInput = null;
        this.isInitialized = false;
    }
}

// Export for module usage
window.PatientModule = PatientModule;

// Auto-initialize when DOM is ready
document.addEventListener('DOMContentLoaded', function() {
    const patientsPage = document.getElementById('patientsPage');
    const patientModal = document.getElementById('patientModal');

    // Initialize if we're on the patients page OR if the patient modal exists (for other pages)
    if (!patientsPage && !patientModal) {
        return;
    }

    const initWhenReady = () => {
        const isAuthenticated = (typeof currentUser !== 'undefined' && currentUser) ||
                               (window.App && window.App.isAuthenticated && window.App.isAuthenticated());

        if (!isAuthenticated) {
            setTimeout(initWhenReady, 200);
            return;
        }

        if (window.patientModule) {
            if (patientsPage) {
                window.patientModule.load();
            }
            return;
        }

        window.patientModule = new PatientModule({
            api: window.apiService || (window.App && window.App.api),
            eventBus: window.eventBus || (window.App && window.App.events)
        });

        window.patientModule.init();

        // Only load patient list if we're on the patients page
        if (patientsPage) {
            window.patientModule.load();
        }
    };

    initWhenReady();
});
