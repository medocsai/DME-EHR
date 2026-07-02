/**
 * ProviderModule - Provider management functionality
 *
 * Handles provider CRUD operations, work schedules, signatures,
 * and user account linking.
 *
 * @example
 *   const providers = App.modules.get('providers');
 *   await providers.init();
 *   providers.load();
 */
class ProviderModule {
    /**
     * @param {Object} options - Module options
     * @param {Object} options.api - API service instance
     * @param {Object} options.eventBus - Event bus for cross-module communication
     */
    constructor(options = {}) {
        this.api = options.api || null;
        this.eventBus = options.eventBus || null;

        // State
        this.providers = [];
        this.currentProvider = null;
        this.currentProviderId = null;
        this.pendingSignatureFile = null;
        this.isNewProviderMode = false;
        this.activeFilter = null;
        this.isInitialized = false;

        // DOM references
        this.container = null;
        this.grid = null;

        // Default schedule
        this.defaultSchedule = {
            0: { available: false, start: '08:00', end: '12:00' }, // Sunday
            1: { available: true, start: '08:00', end: '17:00' },  // Monday
            2: { available: true, start: '08:00', end: '17:00' },  // Tuesday
            3: { available: true, start: '08:00', end: '17:00' },  // Wednesday
            4: { available: true, start: '08:00', end: '17:00' },  // Thursday
            5: { available: true, start: '08:00', end: '17:00' },  // Friday
            6: { available: false, start: '08:00', end: '12:00' }  // Saturday
        };

        // Bind methods
        this._handleGridClick = this._handleGridClick.bind(this);
        this._handleFormSubmit = this._handleFormSubmit.bind(this);
        this._handleSignatureSelect = this._handleSignatureSelect.bind(this);
    }

    /**
     * Initialize the module
     * @returns {Promise<void>}
     */
    async init() {
        this.container = document.querySelector('.providers-container');
        this.grid = document.getElementById('providersGrid');

        if (!this.container && !this.grid) {
            console.warn('[ProviderModule] Container not found');
            return;
        }

        this._bindEvents();
        this.isInitialized = true;
        this._emit('providers:initialized');
    }

    /**
     * Bind event handlers
     * @private
     */
    _bindEvents() {
        // Grid click delegation
        if (this.grid) {
            this.grid.addEventListener('click', this._handleGridClick);
        }

        // Form submission
        const form = document.getElementById('providerForm');
        if (form) {
            form.addEventListener('submit', this._handleFormSubmit);
        }

        // Status filter
        const statusFilter = document.getElementById('providerStatusFilter');
        if (statusFilter) {
            statusFilter.addEventListener('change', () => this.load());
        }

        // Clean up pending photo when provider modal is closed
        const providerModal = document.getElementById('providerModal');
        if (providerModal) {
            providerModal.addEventListener('hidden.bs.modal', () => {
                AvatarUtils.clearPendingPhoto('provider');
            });
        }

        // Signature file input
        const signatureInput = document.getElementById('signatureFileInput');
        if (signatureInput) {
            signatureInput.addEventListener('change', (e) => {
                if (e.target.files[0]) {
                    this._handleSignatureSelect(e.target.files[0]);
                }
            });
        }

        // Schedule checkbox toggles
        document.querySelectorAll('.schedule-available').forEach(checkbox => {
            checkbox.addEventListener('change', (e) => {
                const day = e.target.dataset.day;
                const startInput = document.querySelector(`.schedule-start[data-day="${day}"]`);
                const endInput = document.querySelector(`.schedule-end[data-day="${day}"]`);
                if (startInput) startInput.disabled = !e.target.checked;
                if (endInput) endInput.disabled = !e.target.checked;
            });
        });

        // Edit provider button in detail modal
        const editProviderBtn = document.getElementById('editProviderBtn');
        if (editProviderBtn) {
            editProviderBtn.addEventListener('click', () => {
                if (this.currentProviderId) {
                    this.edit(this.currentProviderId);
                }
            });
        }

        // Delete/Deactivate provider button in detail modal
        const deleteProviderBtn = document.getElementById('deleteProviderBtn');
        if (deleteProviderBtn) {
            deleteProviderBtn.addEventListener('click', () => {
                if (this.currentProviderId) {
                    this.delete(this.currentProviderId);
                }
            });
        }
    }

    /**
     * Handle grid click events via delegation
     * @private
     * @param {Event} e - Click event
     */
    _handleGridClick(e) {
        const card = e.target.closest('.provider-card');
        if (card) {
            const providerId = card.dataset.providerId;
            if (providerId) {
                this.view(parseInt(providerId));
            }
        }
    }

    /**
     * Load providers from API
     * @param {boolean|null} activeOnly - Filter by active status
     * @returns {Promise<void>}
     */
    async load(activeOnly = null) {
        try {
            // Get filter value from dropdown if not provided
            if (activeOnly === null) {
                const filterValue = document.getElementById('providerStatusFilter')?.value;
                activeOnly = filterValue === '' ? null : filterValue === 'true';
            }

            let url = '/providers';
            if (activeOnly !== null) {
                url += `?activeOnly=${activeOnly}`;
            }

            this.providers = await this._apiGet(url) || [];
            this._render();
            this._emit('providers:loaded', { providers: this.providers });
        } catch (error) {
            console.error('[ProviderModule] Failed to load providers:', error);
            this._renderError('Failed to load providers');
            throw error;
        }
    }

    /**
     * View provider details
     * @param {number} providerId - Provider ID
     * @returns {Promise<void>}
     */
    async view(providerId) {
        try {
            const provider = await this._apiGet(`/providers/${providerId}`);
            if (!provider) return;

            this.currentProvider = provider;
            this.currentProviderId = providerId;
            this._renderDetailModal(provider);
            this._emit('providers:viewed', { provider });
        } catch (error) {
            console.error('[ProviderModule] Failed to view provider:', error);
            this._showError('Failed to load provider details');
        }
    }

    /**
     * Edit provider
     * @param {number} providerId - Provider ID
     * @returns {Promise<void>}
     */
    async edit(providerId) {
        try {
            const provider = await this._apiGet(`/providers/${providerId}`);
            if (!provider) return;

            this.currentProvider = provider;
            this.currentProviderId = providerId;
            this.isNewProviderMode = false;

            this._populateForm(provider);
            this._loadScheduleToForm(provider.ProviderSchedules || []);
            await this._loadSignature(providerId);
            await this._loadUserInfo(providerId);

            this._hideModal('providerDetailModal');
            this._showModal('providerModal');
            this._emit('providers:editing', { provider });
        } catch (error) {
            console.error('[ProviderModule] Failed to edit provider:', error);
            this._showError('Failed to load provider data');
        }
    }

    /**
     * Open form for new provider
     */
    openNewForm() {
        this.currentProvider = null;
        this.currentProviderId = null;
        this.isNewProviderMode = true;
        this.pendingSignatureFile = null;

        const form = document.getElementById('providerForm');
        if (form) {
            form.reset();
            document.getElementById('providerId').value = '';
        }

        // Reset schedule to defaults
        this._resetScheduleToDefaults();

        // Reset signature UI
        this._resetSignatureUI(true);

        // Profile picture — interactive placeholder for new provider (allows preview before save)
        AvatarUtils.clearPendingPhoto('provider');
        const avatarContainer = document.getElementById('providerEditableAvatarContainer');
        if (avatarContainer) {
            avatarContainer.innerHTML = AvatarUtils.renderEditableAvatar({
                entityType: 'provider',
                entityId: null,
                name: '?',
                hasProfilePicture: false,
                color: '#2196F3',
                size: 'xl'
            });
        }

        // Show new provider user section
        document.querySelectorAll('.new-provider-user').forEach(el => el.style.display = 'block');
        document.querySelectorAll('.edit-provider-user').forEach(el => el.style.display = 'none');

        const titleEl = document.querySelector('#providerModal .modal-title');
        if (titleEl) {
            titleEl.textContent = 'Add New Provider';
        }

        this._showModal('providerModal');
    }

    /**
     * Delete/deactivate provider
     * @param {number} providerId - Provider ID
     * @returns {Promise<void>}
     */
    async delete(providerId) {
        const confirmed = await this._confirm({
            title: 'Deactivate Provider',
            message: 'Are you sure you want to deactivate this provider? They will no longer appear in scheduling.',
            confirmText: 'Deactivate',
            confirmClass: 'btn-danger'
        });

        if (!confirmed) return;

        try {
            await this._apiDelete(`/providers/${providerId}`);
            this._showSuccess('Provider deactivated successfully');
            this._hideModal('providerDetailModal');
            this._emit('providers:deleted', { providerId });
            await this.load();
        } catch (error) {
            console.error('[ProviderModule] Failed to deactivate provider:', error);
            this._showError('Failed to deactivate provider');
        }
    }

    /**
     * Handle form submission
     * @private
     * @param {Event} e - Submit event
     */
    async _handleFormSubmit(e) {
        e.preventDefault();

        const formData = new FormData(e.target);
        const providerId = formData.get('ProviderId');
        const isEdit = providerId && providerId !== '';

        const data = this._extractFormData(formData, isEdit);

        try {
            let newProviderId = null;

            if (isEdit) {
                await this._apiPut(`/providers/${providerId}`, data);
                this._showSuccess('Provider updated successfully');
                this._emit('providers:updated', { providerId, data });
            } else {
                const result = await this._apiPost('/providers', data);
                newProviderId = result?.ProviderId;

                let extras = [];

                // Upload pending signature
                if (this.pendingSignatureFile && newProviderId) {
                    try {
                        await this._uploadSignature(newProviderId, this.pendingSignatureFile);
                        this.pendingSignatureFile = null;
                        extras.push('signature');
                    } catch (sigError) {
                        console.error('[ProviderModule] Signature upload failed:', sigError);
                        this._showWarning('Provider created but signature upload failed');
                    }
                }

                // Upload pending profile photo
                if (AvatarUtils.hasPendingPhoto('provider') && newProviderId) {
                    try {
                        const photoOk = await AvatarUtils.uploadPendingPhoto('provider', newProviderId);
                        if (photoOk) extras.push('photo');
                    } catch (photoError) {
                        console.error('[ProviderModule] Photo upload failed:', photoError);
                        this._showWarning('Provider created but photo upload failed');
                    }
                }

                if (extras.length > 0) {
                    this._showSuccess(`Provider created with ${extras.join(' & ')}`);
                } else {
                    this._showSuccess('Provider created successfully');
                }
                this._emit('providers:created', { provider: result });
            }

            this._hideModal('providerModal');
            e.target.reset();
            this._resetSignatureUI(false);
            await this.load();
        } catch (error) {
            console.error('[ProviderModule] Save provider error:', error);
            this._showError(error.message || 'Failed to save provider');
        }
    }

    /**
     * Extract form data into provider object
     * @private
     * @param {FormData} formData - Form data
     * @param {boolean} isEdit - Whether this is an edit operation
     * @returns {Object} Provider data
     */
    _extractFormData(formData, isEdit) {
        const data = {
            FirstName: formData.get('FirstName'),
            LastName: formData.get('LastName'),
            Npi: formData.get('Npi'),
            Credentials: formData.get('Credentials') || null,
            Specialty: formData.get('Specialty') || null,
            Taxonomy: formData.get('Taxonomy') || null,
            Email: formData.get('Email') || null,
            Phone: formData.get('Phone') || null,
            LicenseNumber: formData.get('LicenseNumber') || null,
            LicenseState: formData.get('LicenseState') || null,
            LicenseExpiry: formData.get('LicenseExpiry') || null,
            DefaultAppointmentDuration: parseInt(formData.get('DefaultAppointmentDuration')) || 30,
            Color: formData.get('Color') || '#2196F3',
            IsActive: formData.get('IsActive') === 'on',
            WorkSchedule: this._collectScheduleFromForm()
        };

        // Add password for new providers
        if (!isEdit) {
            const password = formData.get('Password');
            if (password) {
                data.Password = password;
            }
        }

        return data;
    }

    /**
     * Collect work schedule from form
     * @private
     * @returns {Array} Schedule array
     */
    _collectScheduleFromForm() {
        const schedules = [];
        for (let day = 0; day <= 6; day++) {
            const checkbox = document.querySelector(`.schedule-available[data-day="${day}"]`);
            const startInput = document.querySelector(`.schedule-start[data-day="${day}"]`);
            const endInput = document.querySelector(`.schedule-end[data-day="${day}"]`);

            if (checkbox && startInput && endInput) {
                schedules.push({
                    DayOfWeek: day,
                    StartTime: startInput.value || '08:00',
                    EndTime: endInput.value || '17:00',
                    IsAvailable: checkbox.checked
                });
            }
        }
        return schedules;
    }

    /**
     * Load schedule into form
     * @private
     * @param {Array} schedules - Schedule array from API
     */
    _loadScheduleToForm(schedules) {
        // Reset to defaults first
        this._resetScheduleToDefaults();

        // Apply saved schedules
        if (schedules?.length) {
            schedules.forEach(schedule => {
                const day = schedule.DayOfWeek;
                const checkbox = document.querySelector(`.schedule-available[data-day="${day}"]`);
                const startInput = document.querySelector(`.schedule-start[data-day="${day}"]`);
                const endInput = document.querySelector(`.schedule-end[data-day="${day}"]`);

                if (checkbox && startInput && endInput) {
                    checkbox.checked = schedule.IsAvailable !== false;
                    startInput.value = this._formatTimeForInput(schedule.StartTime);
                    endInput.value = this._formatTimeForInput(schedule.EndTime);
                    startInput.disabled = !checkbox.checked;
                    endInput.disabled = !checkbox.checked;
                }
            });
        }
    }

    /**
     * Reset schedule form to defaults
     * @private
     */
    _resetScheduleToDefaults() {
        for (let day = 0; day <= 6; day++) {
            const defaults = this.defaultSchedule[day];
            const checkbox = document.querySelector(`.schedule-available[data-day="${day}"]`);
            const startInput = document.querySelector(`.schedule-start[data-day="${day}"]`);
            const endInput = document.querySelector(`.schedule-end[data-day="${day}"]`);

            if (checkbox && startInput && endInput) {
                checkbox.checked = defaults.available;
                startInput.value = defaults.start;
                endInput.value = defaults.end;
                startInput.disabled = !defaults.available;
                endInput.disabled = !defaults.available;
            }
        }
    }

    /**
     * Format time for input field
     * @private
     * @param {string} timeValue - Time value from API
     * @returns {string} Formatted time (HH:mm)
     */
    _formatTimeForInput(timeValue) {
        if (!timeValue) return '08:00';
        const parts = timeValue.split(':');
        if (parts.length >= 2) {
            return `${parts[0].padStart(2, '0')}:${parts[1].padStart(2, '0')}`;
        }
        return '08:00';
    }

    // === Signature Methods ===

    /**
     * Handle signature file selection
     * @private
     * @param {File} file - Selected file
     */
    _handleSignatureSelect(file) {
        // Validate file size (max 2MB)
        if (file.size > 2 * 1024 * 1024) {
            this._showError('File size exceeds 2MB limit');
            document.getElementById('signatureFileInput').value = '';
            return;
        }

        // Validate file type
        if (!['image/jpeg', 'image/jpg', 'image/png'].includes(file.type)) {
            this._showError('Only JPG and PNG images are allowed');
            document.getElementById('signatureFileInput').value = '';
            return;
        }

        // Show preview
        this._showSignaturePreview(file);

        if (this.isNewProviderMode) {
            this.pendingSignatureFile = file;
        } else if (this.currentProviderId) {
            this._uploadSignature(this.currentProviderId, file);
        }
    }

    /**
     * Show signature preview
     * @private
     * @param {File} file - Image file
     */
    _showSignaturePreview(file) {
        const preview = document.getElementById('signaturePreview');
        const noSignature = document.getElementById('noSignatureMessage');
        const image = document.getElementById('signatureImage');
        const deleteBtn = document.getElementById('deleteSignatureBtn');
        const pendingMsg = document.getElementById('signaturePendingMessage');

        const imageUrl = URL.createObjectURL(file);

        if (image) {
            if (image.dataset.oldUrl) {
                URL.revokeObjectURL(image.dataset.oldUrl);
            }
            image.src = imageUrl;
            image.dataset.oldUrl = imageUrl;
        }
        if (preview) preview.style.display = 'block';
        if (noSignature) noSignature.style.display = 'none';
        if (deleteBtn) deleteBtn.style.display = 'inline-block';
        if (this.isNewProviderMode && pendingMsg) {
            pendingMsg.classList.remove('d-none');
        }
    }

    /**
     * Load provider signature
     * @private
     * @param {number} providerId - Provider ID
     */
    async _loadSignature(providerId) {
        const preview = document.getElementById('signaturePreview');
        const noSignature = document.getElementById('noSignatureMessage');
        const image = document.getElementById('signatureImage');
        const deleteBtn = document.getElementById('deleteSignatureBtn');
        const fileInput = document.getElementById('signatureFileInput');
        const pendingMsg = document.getElementById('signaturePendingMessage');

        if (fileInput) fileInput.value = '';
        if (pendingMsg) pendingMsg.classList.add('d-none');

        try {
            const token = localStorage.getItem('authToken');
            const response = await fetch(`/api/providers/${providerId}/signature`, {
                headers: { 'Authorization': `Bearer ${token}` }
            });

            if (response.ok) {
                const blob = await response.blob();
                const imageUrl = URL.createObjectURL(blob);

                if (image) {
                    if (image.dataset.oldUrl) {
                        URL.revokeObjectURL(image.dataset.oldUrl);
                    }
                    image.src = imageUrl;
                    image.dataset.oldUrl = imageUrl;
                }
                if (preview) preview.style.display = 'block';
                if (noSignature) noSignature.style.display = 'none';
                if (deleteBtn) deleteBtn.style.display = 'inline-block';
            } else {
                if (preview) preview.style.display = 'none';
                if (noSignature) noSignature.style.display = 'block';
                if (deleteBtn) deleteBtn.style.display = 'none';
            }
        } catch (error) {
            console.error('[ProviderModule] Load signature error:', error);
            if (preview) preview.style.display = 'none';
            if (noSignature) noSignature.style.display = 'block';
            if (deleteBtn) deleteBtn.style.display = 'none';
        }
    }

    /**
     * Upload signature for provider
     * @private
     * @param {number} providerId - Provider ID
     * @param {File} file - Signature file
     * @returns {Promise<void>}
     */
    async _uploadSignature(providerId, file) {
        const progressContainer = document.getElementById('signatureUploadProgress');
        const progressBar = progressContainer?.querySelector('.progress-bar');

        if (progressContainer) progressContainer.style.display = 'block';
        if (progressBar) progressBar.style.width = '0%';

        return new Promise((resolve, reject) => {
            const formData = new FormData();
            formData.append('file', file);

            const token = localStorage.getItem('authToken');
            const xhr = new XMLHttpRequest();
            xhr.open('POST', `/api/providers/${providerId}/signature`, true);
            xhr.setRequestHeader('Authorization', `Bearer ${token}`);

            xhr.upload.onprogress = (e) => {
                if (e.lengthComputable && progressBar) {
                    const percent = (e.loaded / e.total) * 100;
                    progressBar.style.width = percent + '%';
                }
            };

            xhr.onload = () => {
                if (progressContainer) progressContainer.style.display = 'none';

                if (xhr.status >= 200 && xhr.status < 300) {
                    this._showSuccess('Signature uploaded successfully');
                    resolve(true);
                } else {
                    let errorMessage = 'Failed to upload signature';
                    try {
                        const result = JSON.parse(xhr.responseText);
                        errorMessage = result.Message || result.message || errorMessage;
                    } catch (e) {}
                    reject(new Error(errorMessage));
                }
            };

            xhr.onerror = () => {
                if (progressContainer) progressContainer.style.display = 'none';
                reject(new Error('Network error occurred while uploading'));
            };

            xhr.send(formData);
        });
    }

    /**
     * Delete provider signature
     * @returns {Promise<void>}
     */
    async deleteSignature() {
        if (this.isNewProviderMode || !this.currentProviderId) {
            this.pendingSignatureFile = null;
            this._resetSignatureUI(this.isNewProviderMode);
            if (this.isNewProviderMode) {
                this._showSuccess('Signature removed');
            }
            return;
        }

        const confirmed = await this._confirm({
            title: 'Delete Signature',
            message: "Are you sure you want to delete this provider's signature?",
            confirmText: 'Delete',
            confirmClass: 'btn-danger'
        });

        if (!confirmed) return;

        try {
            await this._apiDelete(`/providers/${this.currentProviderId}/signature`);
            this._showSuccess('Signature deleted successfully');
            await this._loadSignature(this.currentProviderId);
        } catch (error) {
            console.error('[ProviderModule] Delete signature error:', error);
            this._showError('Failed to delete signature');
        }
    }

    /**
     * Reset signature UI
     * @private
     * @param {boolean} isNewProvider - Whether in new provider mode
     */
    _resetSignatureUI(isNewProvider = false) {
        this.pendingSignatureFile = null;
        this.isNewProviderMode = isNewProvider;

        const preview = document.getElementById('signaturePreview');
        const noSignature = document.getElementById('noSignatureMessage');
        const image = document.getElementById('signatureImage');
        const fileInput = document.getElementById('signatureFileInput');
        const deleteBtn = document.getElementById('deleteSignatureBtn');
        const pendingMsg = document.getElementById('signaturePendingMessage');

        if (preview) preview.style.display = 'none';
        if (noSignature) noSignature.style.display = 'block';
        if (image) image.src = '';
        if (fileInput) fileInput.value = '';
        if (deleteBtn) deleteBtn.style.display = 'none';
        if (pendingMsg) pendingMsg.classList.add('d-none');
    }

    // === User Account Linking ===

    /**
     * Load provider's user account info
     * @private
     * @param {number} providerId - Provider ID
     */
    async _loadUserInfo(providerId) {
        const container = document.getElementById('providerUserInfo');
        if (!container) return;

        try {
            const userInfo = await this._apiGet(`/providers/${providerId}/user`);

            if (userInfo.HasUser) {
                container.innerHTML = this._renderUserInfo(userInfo);
            } else {
                container.innerHTML = this._renderNoUserMessage();
            }
        } catch (error) {
            console.error('[ProviderModule] Load user info error:', error);
            container.innerHTML = `
                <div class="alert alert-danger mb-0">
                    <i class="bi bi-exclamation-triangle me-2"></i>Failed to load user information.
                </div>
            `;
        }
    }

    /**
     * Render user info display
     * @private
     * @param {Object} userInfo - User information
     * @returns {string} HTML
     */
    _renderUserInfo(userInfo) {
        return `
            <div class="d-flex align-items-start">
                <div class="flex-shrink-0">
                    <div class="rounded-circle bg-success bg-opacity-10 p-3">
                        <i class="bi bi-person-check text-success fs-4"></i>
                    </div>
                </div>
                <div class="flex-grow-1 ms-3">
                    <div class="d-flex justify-content-between align-items-start mb-2">
                        <div>
                            <h6 class="mb-1">${this._escape(userInfo.FullName)}</h6>
                            <span class="badge ${userInfo.IsActive ? 'bg-success' : 'bg-secondary'}">
                                ${userInfo.IsActive ? 'Active' : 'Inactive'}
                            </span>
                            <span class="badge bg-info ms-1">${this._escape(userInfo.RoleName)}</span>
                        </div>
                    </div>
                    <div class="small text-muted">
                        <p class="mb-1"><i class="bi bi-envelope me-2"></i>${this._escape(userInfo.Email)}</p>
                        ${userInfo.LastLoginAt ? `<p class="mb-0"><i class="bi bi-clock-history me-2"></i>Last login: ${this._formatDateTime(userInfo.LastLoginAt)}</p>` : ''}
                    </div>
                </div>
            </div>
        `;
    }

    /**
     * Render no user message
     * @private
     * @returns {string} HTML
     */
    _renderNoUserMessage() {
        return `
            <div class="text-center py-3">
                <div class="rounded-circle bg-warning bg-opacity-10 p-3 d-inline-block mb-3">
                    <i class="bi bi-person-x text-warning fs-3"></i>
                </div>
                <h6 class="text-muted mb-2">No User Account</h6>
                <p class="text-muted small mb-3">This provider does not have an associated user account.</p>
            </div>
        `;
    }

    // === Rendering ===

    /**
     * Render providers grid
     * @private
     */
    _render() {
        if (!this.grid) return;

        if (!this.providers?.length) {
            this.grid.innerHTML = '<div class="col-12 text-center text-muted py-5">No providers found</div>';
            return;
        }

        this.grid.innerHTML = this.providers.map(p => this._renderProviderCard(p)).join('');
    }

    /**
     * Render a single provider card
     * @private
     * @param {Object} provider - Provider data
     * @returns {string} HTML
     */
    _renderProviderCard(provider) {
        const rawCreds = provider.Credentials || '';
        const credBadges = rawCreds.split(/[,/]+/)
            .map(c => c.trim())
            .filter(Boolean)
            .map(c => `<span style="font-size: 12px; padding: 7px 14px;" class="badge bg-danger me-1">${this._escape(c)}</span>`)
            .join('');

        return `
            <div class="col-md-4 col-lg-3">
                <div class="provider-card" style="cursor: pointer;" data-provider-id="${provider.ProviderId}">
                    ${AvatarUtils.renderProviderAvatar({ providerId: provider.ProviderId, name: provider.FullName, color: provider.Color || '#2196F3', hasProfilePicture: provider.HasProfilePicture, size: 'xl', cssClass: provider.IsActive === false ? 'opacity-50 mx-auto mb-3' : 'mx-auto mb-3' })}
                    <h5>${this._escape(provider.FullName)}</h5>
                    <div class="specialty">${this._escape(provider.Specialty || 'Internal Medicine')}</div>
                    <div class="credentials">${credBadges || '<span class="text-muted small">MD</span>'}</div>
                    ${provider.IsActive === false ? '<span class="badge bg-secondary">Inactive</span>' : ''}
                </div>
            </div>
        `;
    }

    /**
     * Render provider detail modal
     * @private
     * @param {Object} provider - Provider data
     */
    _renderDetailModal(provider) {
        const content = document.getElementById('providerDetailContent');
        if (!content) return;

        // Handle both DTO (HasProfilePicture) and raw entity (ProfilePicturePath)
        const hasPic = provider.HasProfilePicture || !!(provider.ProfilePicturePath);

        content.innerHTML = `
            <div class="text-center mb-4">
                ${AvatarUtils.renderProviderAvatar({ providerId: provider.ProviderId, name: `${provider.FirstName} ${provider.LastName}`, hasProfilePicture: hasPic, color: provider.Color || '#2196F3', size: 'xl', cssClass: 'mx-auto mb-3' })}
                <h4 class="mt-2">${this._escape(provider.FirstName)} ${this._escape(provider.LastName)}${provider.Credentials ? ', ' + this._escape(provider.Credentials) : ''}</h4>
                <div class="text-muted">${this._escape(provider.Specialty || 'Internal Medicine')}</div>
                ${provider.IsActive === false ? '<span class="badge bg-secondary mt-2">Inactive</span>' : '<span class="badge bg-success mt-2">Active</span>'}
            </div>

            <div class="row">
                <div class="col-md-6">
                    <h6 class="text-muted mb-3">Contact Information</h6>
                    <p><strong>Email:</strong> ${this._escape(provider.Email || '-')}</p>
                    <p><strong>Phone:</strong> ${this._escape(provider.Phone || '-')}</p>
                </div>
                <div class="col-md-6">
                    <h6 class="text-muted mb-3">Professional Details</h6>
                    <p><strong>NPI:</strong> ${this._escape(provider.Npi)}</p>
                    <p><strong>Taxonomy:</strong> ${this._escape(provider.Taxonomy || '-')}</p>
                </div>
            </div>

            <hr>

            <div class="row">
                <div class="col-md-6">
                    <h6 class="text-muted mb-3">License Information</h6>
                    <p><strong>License Number:</strong> ${this._escape(provider.LicenseNumber || '-')}</p>
                    <p><strong>License State:</strong> ${this._escape(provider.LicenseState || '-')}</p>
                    <p><strong>License Expiry:</strong> ${provider.LicenseExpiry ? this._formatDate(provider.LicenseExpiry) : '-'}</p>
                </div>
                <div class="col-md-6">
                    <h6 class="text-muted mb-3">Settings</h6>
                    <p><strong>Default Appointment Duration:</strong> ${provider.DefaultAppointmentDuration || 30} minutes</p>
                </div>
            </div>
        `;

        this._showModal('providerDetailModal');
    }

    /**
     * Populate provider form for editing
     * @private
     * @param {Object} provider - Provider data
     */
    _populateForm(provider) {
        const form = document.getElementById('providerForm');
        if (!form) return;

        form.reset();
        document.getElementById('providerId').value = provider.ProviderId;

        const fields = ['FirstName', 'LastName', 'Npi', 'Credentials', 'Specialty',
                       'Taxonomy', 'Email', 'Phone', 'LicenseNumber', 'LicenseState',
                       'DefaultAppointmentDuration', 'Color'];

        fields.forEach(field => {
            const input = form.querySelector(`[name="${field}"]`);
            if (input) {
                const value = provider[field] || '';

                // Handle select dropdowns - add custom option if value not in list
                if (input.tagName === 'SELECT' && value) {
                    const optionExists = Array.from(input.options).some(opt => opt.value === value);
                    if (!optionExists) {
                        const customOption = document.createElement('option');
                        customOption.value = value;
                        customOption.textContent = value;
                        input.appendChild(customOption);
                    }
                }

                input.value = value;
            }
        });

        // License expiry date
        const licenseExpiry = form.querySelector('[name="LicenseExpiry"]');
        if (licenseExpiry && provider.LicenseExpiry) {
            licenseExpiry.value = provider.LicenseExpiry.split('T')[0];
        }

        // IsActive checkbox
        const isActiveCheckbox = form.querySelector('[name="IsActive"]');
        if (isActiveCheckbox) {
            isActiveCheckbox.checked = provider.IsActive !== false;
        }

        // Update modal title
        const titleEl = document.querySelector('#providerModal .modal-title');
        if (titleEl) {
            titleEl.textContent = 'Edit Provider';
        }

        // Profile picture — render editable avatar
        const avatarContainer = document.getElementById('providerEditableAvatarContainer');
        if (avatarContainer && provider.ProviderId) {
            const hasPic = provider.HasProfilePicture || !!(provider.ProfilePicturePath);
            avatarContainer.innerHTML = AvatarUtils.renderEditableAvatar({
                entityType: 'provider',
                entityId: provider.ProviderId,
                name: `${provider.FirstName} ${provider.LastName}`,
                hasProfilePicture: hasPic,
                color: provider.Color || '#2196F3',
                size: 'xl'
            });
        }

        // Show edit provider user section
        document.querySelectorAll('.new-provider-user').forEach(el => el.style.display = 'none');
        document.querySelectorAll('.edit-provider-user').forEach(el => el.style.display = 'block');
    }

    /**
     * Render error state
     * @private
     * @param {string} message - Error message
     */
    _renderError(message) {
        if (!this.grid) return;

        this.grid.innerHTML = `
            <div class="col-12 text-center text-danger py-4">
                <i class="bi bi-exclamation-triangle me-2"></i>
                ${this._escape(message)}
                <button class="btn btn-sm btn-outline-danger ms-2" onclick="App.modules.get('providers').load()">
                    Retry
                </button>
            </div>
        `;
    }

    // === API Methods ===

    async _apiGet(url) {
        if (this.api) {
            return this.api.get(url);
        }
        const response = await fetch(`/api${url}`, {
            headers: this._getHeaders()
        });
        if (!response.ok) {
            const error = await response.json().catch(() => ({}));
            throw new Error(error.message || 'API request failed');
        }
        return response.json();
    }

    async _apiPost(url, data = {}) {
        if (this.api) {
            return this.api.post(url, data);
        }
        const response = await fetch(`/api${url}`, {
            method: 'POST',
            headers: this._getHeaders(),
            body: JSON.stringify(data)
        });
        if (!response.ok) {
            const error = await response.json().catch(() => ({}));
            throw new Error(error.message || 'API request failed');
        }
        return response.json();
    }

    async _apiPut(url, data) {
        if (this.api) {
            return this.api.put(url, data);
        }
        const response = await fetch(`/api${url}`, {
            method: 'PUT',
            headers: this._getHeaders(),
            body: JSON.stringify(data)
        });
        if (!response.ok) {
            const error = await response.json().catch(() => ({}));
            throw new Error(error.message || 'API request failed');
        }
        return response.json();
    }

    async _apiDelete(url) {
        if (this.api) {
            return this.api.delete(url);
        }
        const response = await fetch(`/api${url}`, {
            method: 'DELETE',
            headers: this._getHeaders()
        });
        if (!response.ok) {
            const error = await response.json().catch(() => ({}));
            throw new Error(error.message || 'API request failed');
        }
        return response.json().catch(() => ({}));
    }

    _getHeaders() {
        const token = localStorage.getItem('authToken');
        return {
            'Content-Type': 'application/json',
            'Authorization': token ? `Bearer ${token}` : ''
        };
    }

    // === Utility Methods ===

    _escape(str) {
        if (str === null || str === undefined) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    }

    _formatDate(dateStr) {
        if (!dateStr) return '-';
        try {
            // Handle date-only strings (YYYY-MM-DD) to avoid timezone shifting
            if (typeof dateStr === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(dateStr)) {
                const [year, month, day] = dateStr.split('-').map(Number);
                return new Date(year, month - 1, day).toLocaleDateString();
            }
            return new Date(dateStr).toLocaleDateString();
        } catch {
            return dateStr;
        }
    }

    _formatDateTime(dateStr) {
        if (!dateStr) return '-';
        try {
            return new Date(dateStr).toLocaleString();
        } catch {
            return dateStr;
        }
    }

    _showModal(id) {
        const el = document.getElementById(id);
        if (el && window.bootstrap) {
            new bootstrap.Modal(el).show();
        }
    }

    _hideModal(id) {
        const el = document.getElementById(id);
        if (el && window.bootstrap) {
            bootstrap.Modal.getInstance(el)?.hide();
        }
    }

    async _confirm(options) {
        if (window.ConfirmDialog) {
            return ConfirmDialog.show(options);
        }
        return confirm(options.message);
    }

    _showSuccess(message) {
        if (window.Toast) {
            Toast.success('Success', message);
        }
    }

    _showError(message) {
        if (window.Toast) {
            Toast.error('Error', message);
        }
    }

    _showWarning(message) {
        if (window.Toast) {
            Toast.warning('Warning', message);
        }
    }

    _emit(event, data = {}) {
        if (this.eventBus) {
            this.eventBus.emit(event, data);
        }
    }

    /**
     * Destroy the module and clean up
     */
    destroy() {
        if (this.grid) {
            this.grid.removeEventListener('click', this._handleGridClick);
        }

        this.providers = [];
        this.currentProvider = null;
        this.currentProviderId = null;
        this.pendingSignatureFile = null;
        this.container = null;
        this.grid = null;
        this.isInitialized = false;
    }
}

// Export for module usage
window.ProviderModule = ProviderModule;

// Auto-initialize when DOM is ready
document.addEventListener('DOMContentLoaded', function() {
    const container = document.getElementById('providersPage');
    if (!container) return;

    const initWhenReady = () => {
        const isAuthenticated = (typeof currentUser !== 'undefined' && currentUser) ||
                               (window.App && window.App.isAuthenticated && window.App.isAuthenticated());

        if (!isAuthenticated) {
            setTimeout(initWhenReady, 200);
            return;
        }

        if (window.providerModule) {
            window.providerModule.load();
            return;
        }

        window.providerModule = new ProviderModule({
            api: window.apiService || (window.App && window.App.api),
            eventBus: window.eventBus || (window.App && window.App.events)
        });

        window.providerModule.init();
        window.providerModule.load();
    };

    initWhenReady();
});
