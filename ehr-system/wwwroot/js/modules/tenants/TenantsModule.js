/**
 * TenantsModule - Clinic/Tenant management functionality
 * Handles loading, displaying, creating, editing, and managing clinics
 *
 * Usage:
 *   const tenantsModule = new TenantsModule();
 *   tenantsModule.init();
 *
 * Or via App:
 *   App.modules.register('tenants', new TenantsModule());
 */
class TenantsModule {
    constructor() {
        // State
        this.tenants = [];
        this.cachedTimezones = [];

        // DOM references
        this.container = null;
        this.tableBody = null;
        this.tenantModal = null;
        this.tenantForm = null;

        // Status and plan names
        this.planNames = ['Trial', 'Basic', 'Professional', 'Enterprise'];
        this.statusNames = ['Pending', 'Active', 'Suspended', 'Cancelled'];

        // Bound methods
        this.handleFormSubmit = this.handleFormSubmit.bind(this);
        this.handleModalShow = this.handleModalShow.bind(this);
        this.handleModalHidden = this.handleModalHidden.bind(this);
        this.handleTimezoneChange = this.handleTimezoneChange.bind(this);
        this.handleLogoFileChange = this.handleLogoFileChange.bind(this);
        this.uploadLogo = this.uploadLogo.bind(this);
        this.deleteLogo = this.deleteLogo.bind(this);
    }

    /**
     * Initialize the module
     */
    init() {
        this.container = document.getElementById('tenantsPage');
        this.tableBody = this.container?.querySelector('tbody');
        this.tenantModal = document.getElementById('tenantModal');
        this.tenantForm = document.getElementById('tenantForm');

        if (!this.container) {
            console.warn('[TenantsModule] Container not found');
            return;
        }

        this.bindEvents();
        this.load();

        // Register with App if available
        if (window.App && window.App.modules) {
            App.modules.register('tenants', this);
        }

        console.log('[TenantsModule] Initialized');
    }

    /**
     * Bind event handlers
     */
    bindEvents() {
        // Form submission
        if (this.tenantForm) {
            this.tenantForm.addEventListener('submit', this.handleFormSubmit);
        }

        // Modal events
        if (this.tenantModal) {
            this.tenantModal.addEventListener('show.bs.modal', this.handleModalShow);
            this.tenantModal.addEventListener('hidden.bs.modal', this.handleModalHidden);
        }

        // Timezone change handler
        const timezoneSelect = document.getElementById('tenantFormTimezone');
        if (timezoneSelect) {
            timezoneSelect.addEventListener('change', this.handleTimezoneChange);
        }

        // Logo upload handlers
        const logoFileInput = document.getElementById('tenantLogoFile');
        if (logoFileInput) {
            logoFileInput.addEventListener('change', this.handleLogoFileChange);
        }

        const btnUpload = document.getElementById('btnUploadTenantLogo');
        if (btnUpload) {
            btnUpload.addEventListener('click', this.uploadLogo);
        }

        const btnDelete = document.getElementById('btnDeleteTenantLogo');
        if (btnDelete) {
            btnDelete.addEventListener('click', this.deleteLogo);
        }
    }

    /**
     * Load tenants from API
     */
    async load() {
        try {
            const apiGet = window.App?.api
                ? (url) => App.api.get(url)
                : (url) => apiRequest(url);

            this.tenants = await apiGet('/tenants') || [];
            this.render();

        } catch (error) {
            console.error('[TenantsModule] Failed to load tenants:', error);
            Toast.error('Error', 'Failed to load clinics: ' + error.message);
        }
    }

    /**
     * Render tenants table
     */
    render() {
        if (!this.tableBody) return;

        if (!this.tenants.length) {
            this.tableBody.innerHTML = '<tr><td colspan="8" class="text-center text-muted">No clinics found</td></tr>';
            return;
        }

        this.tableBody.innerHTML = this.tenants.map(t => `
            <tr>
                <td><strong>${StringUtils.escape(t.Name)}</strong></td>
                <td>${StringUtils.escape(t.Email || '-')}</td>
                <td>${StringUtils.escape(t.Phone || '-')}</td>
                <td><span class="badge bg-info">${this.planNames[t.Plan] || 'Trial'}</span></td>
                <td>${t.UserCount || 0}</td>
                <td>${t.PatientCount || 0}</td>
                <td>
                    <span class="badge ${t.Status === 1 ? 'bg-success' : t.Status === 2 ? 'bg-warning' : 'bg-secondary'}">
                        ${this.statusNames[t.Status] || 'Unknown'}
                    </span>
                </td>
                <td>
                    <div class="btn-group btn-group-sm" role="group">
                        <button class="btn btn-outline-primary" onclick="tenantsModule.view(${t.TenantId})" title="View Details">
                            <i class="bi bi-eye"></i>
                        </button>
                        <button class="btn btn-outline-secondary" onclick="tenantsModule.edit(${t.TenantId})" title="Edit">
                            <i class="bi bi-pencil"></i>
                        </button>
                        <!--
                            Super Admin belongs to no tenant, so the DME screens have no tenant
                            context of their own to work from and ?tenantId= is how one is chosen.
                            This link is what makes that discoverable: without it the only route
                            in is typing the query string by hand.
                        -->
                        <a class="btn btn-outline-info" href="/Dme/Settings?tenantId=${t.TenantId}" title="DME billing settings">
                            <i class="bi bi-gear"></i>
                        </a>
                        ${t.Status === 1 ? `
                            <button class="btn btn-outline-warning" onclick="tenantsModule.toggleStatus(${t.TenantId}, '${StringUtils.escape(t.Name)}', 2)" title="Suspend Clinic">
                                <i class="bi bi-pause-circle"></i>
                            </button>
                        ` : `
                            <button class="btn btn-outline-success" onclick="tenantsModule.toggleStatus(${t.TenantId}, '${StringUtils.escape(t.Name)}', 1)" title="Activate Clinic">
                                <i class="bi bi-play-circle"></i>
                            </button>
                        `}
                        <button class="btn btn-outline-danger" onclick="tenantsModule.delete(${t.TenantId}, '${StringUtils.escape(t.Name)}')" title="Delete Clinic">
                            <i class="bi bi-trash"></i>
                        </button>
                    </div>
                </td>
            </tr>
        `).join('');
    }

    /**
     * View tenant details
     * @param {number} tenantId - Tenant ID
     */
    async view(tenantId) {
        try {
            const apiGet = window.App?.api
                ? (url) => App.api.get(url)
                : (url) => apiRequest(url);

            // The /stats endpoint was removed with the clinical EHR: it counted
            // appointments, clinical notes and patients, none of which exist in
            // a DME product. The tenant record itself carries what this summary
            // needs. A DME equivalent (customers, open orders, active rentals)
            // would have to read another tenant's data, which row level security
            // deliberately blocks, so it is not a drop-in replacement.
            const tenant = await apiGet(`/tenants/${tenantId}`);

            Toast.info('Clinic Details',
                `${tenant.Name} — ${tenant.UserCount || 0} user(s), plan ${tenant.Plan ?? 'n/a'}`);

        } catch (error) {
            console.error('[TenantsModule] Failed to view tenant:', error);
            Toast.error('Error', 'Failed to load clinic details');
        }
    }

    /**
     * Edit a tenant
     * @param {number} tenantId - Tenant ID
     */
    async edit(tenantId) {
        try {
            const apiGet = window.App?.api
                ? (url) => App.api.get(url)
                : (url) => apiRequest(url);

            const tenant = await apiGet(`/tenants/${tenantId}`);

            if (!this.tenantForm) return;

            // Hide admin fields when editing
            document.querySelectorAll('.new-clinic-admin').forEach(el => el.classList.add('d-none'));
            document.querySelectorAll('.new-clinic-admin input, .new-clinic-admin select').forEach(el => el.removeAttribute('required'));

            // Show logo section for edit mode
            document.querySelectorAll('.edit-clinic-only').forEach(el => el.classList.remove('d-none'));

            // Populate form
            this.tenantForm.querySelector('[name="TenantId"]').value = tenant.TenantId;
            this.tenantForm.querySelector('[name="Name"]').value = tenant.Name;
            this.tenantForm.querySelector('[name="Phone"]').value = tenant.Phone || '';
            this.tenantForm.querySelector('[name="Email"]').value = tenant.Email || '';
            this.tenantForm.querySelector('[name="Address"]').value = tenant.Address || '';
            this.tenantForm.querySelector('[name="City"]').value = tenant.City || '';
            this.tenantForm.querySelector('[name="State"]').value = tenant.State || '';
            this.tenantForm.querySelector('[name="ZipCode"]').value = tenant.ZipCode || '';
            this.tenantForm.querySelector('[name="TaxId"]').value = tenant.TaxId || '';
            this.tenantForm.querySelector('[name="NPI"]').value = tenant.Npi || tenant.NPI || '';
            this.tenantForm.querySelector('[name="Plan"]').value = tenant.Plan || 0;

            document.querySelector('#tenantModal .modal-title').textContent = 'Edit Clinic';
            document.querySelector('#tenantForm button[type="submit"]').textContent = 'Save Changes';

            // Load current logo
            this.loadLogo(tenant.TenantId, tenant.LogoUrl);

            const modal = new bootstrap.Modal(this.tenantModal);
            modal.show();

        } catch (error) {
            console.error('[TenantsModule] Failed to edit tenant:', error);
            Toast.error('Error', 'Failed to load clinic for editing');
        }
    }

    /**
     * Toggle tenant status (activate/suspend)
     * @param {number} tenantId - Tenant ID
     * @param {string} tenantName - Tenant name
     * @param {number} newStatus - New status (1 = Active, 2 = Suspended)
     */
    async toggleStatus(tenantId, tenantName, newStatus) {
        const actionName = newStatus === 1 ? 'activate' : 'suspend';
        const message = newStatus !== 1
            ? `Are you sure you want to ${actionName} "${tenantName}"?\n\nUsers of this clinic will no longer be able to log in.`
            : `Are you sure you want to ${actionName} "${tenantName}"?\n\nUsers will be able to log in again.`;

        const confirmed = await ConfirmDialog.show({
            title: `${actionName.charAt(0).toUpperCase() + actionName.slice(1)} Clinic`,
            message: message,
            confirmText: actionName.charAt(0).toUpperCase() + actionName.slice(1),
            confirmClass: newStatus === 1 ? 'btn-success' : 'btn-warning',
            headerClass: newStatus === 1 ? 'bg-success text-white' : 'bg-warning'
        });
        if (!confirmed) return;

        try {
            if (window.App?.api) {
                await App.api.post(`/tenants/${tenantId}/status`, { status: newStatus });
            } else {
                await apiRequest(`/tenants/${tenantId}/status`, {
                    method: 'POST',
                    body: JSON.stringify({ status: newStatus })
                });
            }

            Toast.success('Success', `Clinic "${tenantName}" has been ${this.statusNames[newStatus].toLowerCase()}`);
            this.load();

        } catch (error) {
            console.error('[TenantsModule] Failed to toggle status:', error);
            Toast.error('Error', `Failed to update clinic status: ${error.message}`);
        }
    }

    /**
     * Delete a tenant
     * @param {number} tenantId - Tenant ID
     * @param {string} tenantName - Tenant name
     */
    async delete(tenantId, tenantName) {
        console.log('[TenantsModule] Delete initiated for:', tenantName);

        // First confirmation
        const confirmed1 = await ConfirmDialog.show({
            title: 'Delete Clinic',
            message: `Are you sure you want to DELETE "${tenantName}"? This action cannot be undone. All users of this clinic will lose access immediately.`,
            confirmText: 'Continue',
            confirmClass: 'btn-danger',
            headerClass: 'bg-danger text-white'
        });
        console.log('[TenantsModule] First confirmation:', confirmed1);
        if (!confirmed1) return;

        // Second confirmation
        const confirmed2 = await ConfirmDialog.show({
            title: 'FINAL WARNING',
            message: `You are about to permanently delete "${tenantName}" and all associated data. Type 'DELETE' in the prompt to confirm.`,
            confirmText: 'I Understand',
            confirmClass: 'btn-danger',
            headerClass: 'bg-danger text-white'
        });
        console.log('[TenantsModule] Second confirmation:', confirmed2);
        if (!confirmed2) return;

        // Small delay to ensure modal is fully closed before showing prompt
        await new Promise(resolve => setTimeout(resolve, 300));

        // Third confirmation - prompt for DELETE
        const confirmation = prompt(`Type "DELETE" to confirm deletion of "${tenantName}":`);
        console.log('[TenantsModule] Prompt result:', confirmation);

        if (confirmation !== 'DELETE') {
            Toast.info('Cancelled', 'Deletion cancelled - confirmation text did not match');
            return;
        }

        try {
            console.log('[TenantsModule] Sending delete request for tenant:', tenantId);

            const token = localStorage.getItem('authToken');
            const response = await fetch(`/api/tenants/${tenantId}`, {
                method: 'DELETE',
                headers: {
                    'Authorization': `Bearer ${token}`,
                    'Content-Type': 'application/json'
                }
            });

            if (!response.ok) {
                const errorData = await response.json().catch(() => ({}));
                throw new Error(errorData.message || `HTTP ${response.status}`);
            }

            Toast.success('Success', `Clinic "${tenantName}" has been deleted`);
            this.load();

        } catch (error) {
            console.error('[TenantsModule] Failed to delete tenant:', error);
            Toast.error('Error', `Failed to delete clinic: ${error.message}`);
        }
    }

    /**
     * Handle form submission
     * @param {Event} e - Submit event
     */
    async handleFormSubmit(e) {
        e.preventDefault();

        const formData = new FormData(e.target);
        const tenantId = formData.get('TenantId');

        const data = {
            Name: formData.get('Name'),
            Phone: formData.get('Phone'),
            Email: formData.get('Email'),
            Address: formData.get('Address'),
            City: formData.get('City'),
            State: formData.get('State'),
            ZipCode: formData.get('ZipCode'),
            TaxId: formData.get('TaxId'),
            NPI: formData.get('NPI'),
            Plan: parseInt(formData.get('Plan')) || 0
        };

        // Add admin fields and initial location for new clinic
        if (!tenantId) {
            data.AdminFirstName = formData.get('AdminFirstName');
            data.AdminLastName = formData.get('AdminLastName');
            data.AdminEmail = formData.get('AdminEmail');
            data.AdminPassword = formData.get('AdminPassword');
            data.InitialLocationName = formData.get('InitialLocationName');
            data.InitialLocationTimeZoneId = formData.get('InitialLocationTimeZoneId');

            // Validate timezone is selected
            if (!data.InitialLocationTimeZoneId) {
                Toast.error('Validation Error', 'Please select a timezone for the first location');
                return;
            }
        }

        try {
            const isEdit = tenantId && tenantId !== '';

            if (window.App?.api) {
                if (isEdit) {
                    await App.api.put(`/tenants/${tenantId}`, data);
                } else {
                    await App.api.post('/tenants', data);
                }
            } else {
                if (isEdit) {
                    await apiRequest(`/tenants/${tenantId}`, {
                        method: 'PUT',
                        body: JSON.stringify(data)
                    });
                } else {
                    await apiRequest('/tenants', {
                        method: 'POST',
                        body: JSON.stringify(data)
                    });
                }
            }

            Toast.success('Success', isEdit ? 'Clinic updated successfully' : 'Clinic created successfully');

            bootstrap.Modal.getInstance(this.tenantModal)?.hide();
            e.target.reset();
            this.load();

        } catch (error) {
            console.error('[TenantsModule] Failed to save tenant:', error);
            Toast.error('Error', error.message || 'Failed to save clinic');
        }
    }

    /**
     * Handle modal show event
     */
    async handleModalShow() {
        // Only reset if not triggered by editTenant
        if (!document.getElementById('tenantId')?.value) {
            this.tenantForm?.reset();
            document.querySelectorAll('.new-clinic-admin').forEach(el => el.classList.remove('d-none'));
            document.querySelectorAll('.new-clinic-admin input, .new-clinic-admin select').forEach(el => el.setAttribute('required', 'required'));
            document.querySelector('#tenantModal .modal-title').textContent = 'Add New Clinic';
            document.querySelector('#tenantForm button[type="submit"]').textContent = 'Create Clinic';

            // Hide logo section for create mode
            document.querySelectorAll('.edit-clinic-only').forEach(el => el.classList.add('d-none'));

            // Load timezone dropdown for new clinic
            await this.loadTimezoneDropdown('America/Chicago');

            // Hide timezone info initially
            const tzInfo = document.getElementById('tenantTimezoneInfo');
            if (tzInfo) tzInfo.style.display = 'none';
        }
    }

    /**
     * Handle modal hidden event
     */
    handleModalHidden() {
        const tenantIdInput = document.getElementById('tenantId');
        if (tenantIdInput) {
            tenantIdInput.value = '';
        }

        // Reset logo section
        this.resetLogoUI();
    }

    /**
     * Handle timezone selection change
     */
    handleTimezoneChange(e) {
        this.updateTimezoneDisplay(e.target.value);
    }

    /**
     * Load timezone dropdown
     * @param {string} selectedTimeZoneId - Default selected timezone
     */
    async loadTimezoneDropdown(selectedTimeZoneId = 'America/Chicago') {
        const select = document.getElementById('tenantFormTimezone');
        if (!select) return;

        try {
            const authTokenValue = window.App?.getAuthToken() || window.authToken;
            const response = await fetch(`/api/locations/timezones`, {
                headers: { 'Authorization': `Bearer ${authTokenValue}` }
            });

            if (!response.ok) {
                throw new Error('Failed to load timezones');
            }

            const timezones = await response.json();
            this.cachedTimezones = timezones;

            // Group by region
            const grouped = {};
            timezones.forEach(tz => {
                if (!grouped[tz.Region]) {
                    grouped[tz.Region] = [];
                }
                grouped[tz.Region].push(tz);
            });

            // Build options HTML with optgroups - US timezones first
            let html = '<option value="">Select timezone...</option>';
            const regionOrder = ['US', 'Europe', 'Asia', 'Pacific'];
            regionOrder.forEach(region => {
                if (grouped[region]) {
                    html += `<optgroup label="${region}">`;
                    grouped[region].forEach(tz => {
                        const selected = tz.TimeZoneId === selectedTimeZoneId ? 'selected' : '';
                        html += `<option value="${StringUtils.escape(tz.TimeZoneId)}" ${selected}>
                            ${StringUtils.escape(tz.DisplayName)} (${StringUtils.escape(tz.UtcOffset)})
                        </option>`;
                    });
                    html += '</optgroup>';
                }
            });

            select.innerHTML = html;

            // Show timezone info if one is selected
            if (selectedTimeZoneId) {
                this.updateTimezoneDisplay(selectedTimeZoneId);
            }

        } catch (error) {
            console.error('[TenantsModule] Error loading timezones:', error);
            // Fallback to common US timezones
            select.innerHTML = `
                <option value="">Select timezone...</option>
                <optgroup label="US">
                    <option value="America/New_York" ${selectedTimeZoneId === 'America/New_York' ? 'selected' : ''}>Eastern Time (ET)</option>
                    <option value="America/Chicago" ${selectedTimeZoneId === 'America/Chicago' ? 'selected' : ''}>Central Time (CT)</option>
                    <option value="America/Denver" ${selectedTimeZoneId === 'America/Denver' ? 'selected' : ''}>Mountain Time (MT)</option>
                    <option value="America/Los_Angeles" ${selectedTimeZoneId === 'America/Los_Angeles' ? 'selected' : ''}>Pacific Time (PT)</option>
                    <option value="America/Anchorage" ${selectedTimeZoneId === 'America/Anchorage' ? 'selected' : ''}>Alaska Time (AKT)</option>
                    <option value="Pacific/Honolulu" ${selectedTimeZoneId === 'Pacific/Honolulu' ? 'selected' : ''}>Hawaii Time (HST)</option>
                </optgroup>
            `;

            // Set fallback cached data
            this.cachedTimezones = [
                { TimeZoneId: 'America/New_York', DisplayName: 'Eastern Time', UtcOffset: 'UTC-5', Abbreviation: 'ET' },
                { TimeZoneId: 'America/Chicago', DisplayName: 'Central Time', UtcOffset: 'UTC-6', Abbreviation: 'CT' },
                { TimeZoneId: 'America/Denver', DisplayName: 'Mountain Time', UtcOffset: 'UTC-7', Abbreviation: 'MT' },
                { TimeZoneId: 'America/Los_Angeles', DisplayName: 'Pacific Time', UtcOffset: 'UTC-8', Abbreviation: 'PT' },
                { TimeZoneId: 'America/Anchorage', DisplayName: 'Alaska Time', UtcOffset: 'UTC-9', Abbreviation: 'AKT' },
                { TimeZoneId: 'Pacific/Honolulu', DisplayName: 'Hawaii Time', UtcOffset: 'UTC-10', Abbreviation: 'HST' }
            ];

            if (selectedTimeZoneId) {
                this.updateTimezoneDisplay(selectedTimeZoneId);
            }
        }
    }

    /**
     * Update timezone display info panel
     * @param {string} timeZoneId - Timezone ID
     */
    updateTimezoneDisplay(timeZoneId) {
        const infoDiv = document.getElementById('tenantTimezoneInfo');
        const displaySpan = document.getElementById('tenantTimezoneDisplay');

        if (!infoDiv || !displaySpan) return;

        if (!timeZoneId) {
            infoDiv.style.display = 'none';
            return;
        }

        // Find timezone info from cached data
        const tzInfo = this.cachedTimezones.find(tz => tz.TimeZoneId === timeZoneId);

        if (tzInfo) {
            displaySpan.textContent = `${tzInfo.DisplayName} (${tzInfo.UtcOffset})`;
            infoDiv.style.display = 'block';
        } else {
            infoDiv.style.display = 'none';
        }
    }

    // ── Logo Management ──────────────────────────────────────────────

    /**
     * Handle logo file input change
     */
    handleLogoFileChange(e) {
        const file = e.target.files[0];
        const btnUpload = document.getElementById('btnUploadTenantLogo');
        if (!btnUpload) return;

        if (file) {
            // Validate file size (2MB max)
            if (file.size > 2 * 1024 * 1024) {
                Toast.error('File Too Large', 'Logo must be under 2MB');
                e.target.value = '';
                btnUpload.disabled = true;
                return;
            }
            btnUpload.disabled = false;
        } else {
            btnUpload.disabled = true;
        }
    }

    /**
     * Load and display the current logo for a tenant
     * @param {number} tenantId - Tenant ID
     * @param {string} logoUrl - Logo URL from tenant data
     */
    loadLogo(tenantId, logoUrl) {
        const img = document.getElementById('tenantLogoImg');
        const placeholder = document.getElementById('tenantLogoPlaceholder');
        const btnDelete = document.getElementById('btnDeleteTenantLogo');

        if (!img || !placeholder) return;

        if (logoUrl) {
            // Add cache-busting param to avoid stale cached logos
            img.src = `/api/tenants/${tenantId}/logo?t=${Date.now()}`;
            img.classList.remove('d-none');
            placeholder.classList.add('d-none');
            if (btnDelete) btnDelete.classList.remove('d-none');

            img.onerror = () => {
                // If logo fails to load, show placeholder
                img.classList.add('d-none');
                placeholder.classList.remove('d-none');
                if (btnDelete) btnDelete.classList.add('d-none');
            };
        } else {
            img.classList.add('d-none');
            img.src = '';
            placeholder.classList.remove('d-none');
            if (btnDelete) btnDelete.classList.add('d-none');
        }
    }

    /**
     * Upload logo for the current tenant being edited
     */
    async uploadLogo() {
        const tenantId = document.getElementById('tenantId')?.value;
        if (!tenantId) return;

        const fileInput = document.getElementById('tenantLogoFile');
        const file = fileInput?.files[0];
        if (!file) {
            Toast.error('No File', 'Please select a logo file first');
            return;
        }

        const btnUpload = document.getElementById('btnUploadTenantLogo');
        const originalText = btnUpload.innerHTML;
        btnUpload.disabled = true;
        btnUpload.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Uploading...';

        try {
            const formData = new FormData();
            formData.append('file', file);

            const token = localStorage.getItem('authToken') || window.App?.getAuthToken();
            const response = await fetch(`/api/tenants/${tenantId}/logo`, {
                method: 'POST',
                headers: { 'Authorization': `Bearer ${token}` },
                body: formData
            });

            if (!response.ok) {
                const errorData = await response.json().catch(() => ({}));
                throw new Error(errorData.message || `Upload failed (HTTP ${response.status})`);
            }

            Toast.success('Success', 'Logo uploaded successfully');

            // Refresh the preview
            this.loadLogo(tenantId, 'uploaded');

            // Clear the file input
            fileInput.value = '';
            btnUpload.disabled = true;

        } catch (error) {
            console.error('[TenantsModule] Failed to upload logo:', error);
            Toast.error('Upload Failed', error.message);
        } finally {
            btnUpload.innerHTML = originalText;
            // Keep disabled if no file selected
            if (!fileInput?.files[0]) {
                btnUpload.disabled = true;
            }
        }
    }

    /**
     * Delete logo for the current tenant being edited
     */
    async deleteLogo() {
        const tenantId = document.getElementById('tenantId')?.value;
        if (!tenantId) return;

        const confirmed = await ConfirmDialog.show({
            title: 'Remove Logo',
            message: 'Are you sure you want to remove the clinic logo?',
            confirmText: 'Remove',
            confirmClass: 'btn-danger'
        });
        if (!confirmed) return;

        try {
            const token = localStorage.getItem('authToken') || window.App?.getAuthToken();
            const response = await fetch(`/api/tenants/${tenantId}/logo`, {
                method: 'DELETE',
                headers: { 'Authorization': `Bearer ${token}` }
            });

            if (!response.ok) {
                const errorData = await response.json().catch(() => ({}));
                throw new Error(errorData.message || `Delete failed (HTTP ${response.status})`);
            }

            Toast.success('Success', 'Logo removed successfully');
            this.loadLogo(tenantId, null);

        } catch (error) {
            console.error('[TenantsModule] Failed to delete logo:', error);
            Toast.error('Error', 'Failed to remove logo: ' + error.message);
        }
    }

    /**
     * Reset logo UI to default state
     */
    resetLogoUI() {
        const img = document.getElementById('tenantLogoImg');
        const placeholder = document.getElementById('tenantLogoPlaceholder');
        const fileInput = document.getElementById('tenantLogoFile');
        const btnUpload = document.getElementById('btnUploadTenantLogo');
        const btnDelete = document.getElementById('btnDeleteTenantLogo');

        if (img) { img.classList.add('d-none'); img.src = ''; }
        if (placeholder) placeholder.classList.remove('d-none');
        if (fileInput) fileInput.value = '';
        if (btnUpload) btnUpload.disabled = true;
        if (btnDelete) btnDelete.classList.add('d-none');
    }

    /**
     * Refresh tenants from server
     */
    async refresh() {
        await this.load();
    }

    /**
     * Destroy the module
     */
    destroy() {
        if (this.tenantForm) {
            this.tenantForm.removeEventListener('submit', this.handleFormSubmit);
        }

        if (this.tenantModal) {
            this.tenantModal.removeEventListener('show.bs.modal', this.handleModalShow);
            this.tenantModal.removeEventListener('hidden.bs.modal', this.handleModalHidden);
        }

        const timezoneSelect = document.getElementById('tenantFormTimezone');
        if (timezoneSelect) {
            timezoneSelect.removeEventListener('change', this.handleTimezoneChange);
        }

        const logoFileInput = document.getElementById('tenantLogoFile');
        if (logoFileInput) {
            logoFileInput.removeEventListener('change', this.handleLogoFileChange);
        }

        const btnUpload = document.getElementById('btnUploadTenantLogo');
        if (btnUpload) {
            btnUpload.removeEventListener('click', this.uploadLogo);
        }

        const btnDelete = document.getElementById('btnDeleteTenantLogo');
        if (btnDelete) {
            btnDelete.removeEventListener('click', this.deleteLogo);
        }

        this.tenants = [];
        this.cachedTimezones = [];
        this.container = null;
        this.tableBody = null;
        this.tenantModal = null;
        this.tenantForm = null;
    }
}

// Export for module usage
window.TenantsModule = TenantsModule;

// Auto-initialize when DOM is ready
document.addEventListener('DOMContentLoaded', function() {
    const container = document.getElementById('tenantsPage');
    if (!container) return;

    const initWhenReady = () => {
        const isAuthenticated = (typeof currentUser !== 'undefined' && currentUser) ||
                               (window.App && window.App.isAuthenticated && window.App.isAuthenticated());

        if (!isAuthenticated) {
            setTimeout(initWhenReady, 200);
            return;
        }

        if (window.tenantsModule) {
            window.tenantsModule.load();
            return;
        }

        window.tenantsModule = new TenantsModule({
            api: window.apiService || (window.App && window.App.api),
            eventBus: window.eventBus || (window.App && window.App.events)
        });

        window.tenantsModule.init();
        window.tenantsModule.load();
    };

    initWhenReady();
});
