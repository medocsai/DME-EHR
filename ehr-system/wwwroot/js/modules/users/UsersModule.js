/**
 * UsersModule - User management functionality
 *
 * Handles loading, displaying, creating, editing, and managing users.
 * Includes password reset, email change, and provider linking.
 *
 * @example
 *   const users = new UsersModule({ api: App.api, eventBus: App.eventBus });
 *   await users.init();
 */
class UsersModule {
    /**
     * @param {Object} options - Module options
     * @param {Object} options.api - API service instance
     * @param {Object} options.eventBus - Event bus for cross-module communication
     */
    constructor(options = {}) {
        this.api = options.api || null;
        this.eventBus = options.eventBus || null;

        // State
        this.users = [];
        this.providers = [];
        this.currentFilter = 'active';
        this.isInitialized = false;

        // DOM references
        this.tableBody = null;
        this.userModal = null;
        this.userForm = null;
        this.resetPasswordModal = null;
        this.changeEmailModal = null;

        // Role names mapping
        this.roleNames = ['Super Admin', 'Clinic Admin', 'Clinician', 'Front Desk', 'Biller', 'Read Only', 'Medical Assistant', 'Nurse'];

        // Bind methods
        this._handleUserFormSubmit = this._handleUserFormSubmit.bind(this);
        this._handleResetPasswordSubmit = this._handleResetPasswordSubmit.bind(this);
        this._handleChangeEmailSubmit = this._handleChangeEmailSubmit.bind(this);
        this._handleProviderSelectChange = this._handleProviderSelectChange.bind(this);
        this._handleTableClick = this._handleTableClick.bind(this);
        this._handleFilterClick = this._handleFilterClick.bind(this);
    }

    /**
     * Initialize the module
     * @returns {Promise<void>}
     */
    async init() {
        this.tableBody = document.querySelector('#usersTable tbody');
        this.userForm = document.getElementById('userForm');
        this.userModal = document.getElementById('userModal');
        this.resetPasswordModal = document.getElementById('adminResetPasswordModal');
        this.changeEmailModal = document.getElementById('adminChangeEmailModal');

        if (!this.tableBody) {
            console.warn('[UsersModule] Table body #usersTable tbody not found');
            return;
        }

        this._bindEvents();
        await this.load();

        this.isInitialized = true;
        this._emit('users:initialized');
    }

    /**
     * Bind event handlers
     * @private
     */
    _bindEvents() {
        // User form submission
        if (this.userForm) {
            this.userForm.addEventListener('submit', this._handleUserFormSubmit);
        }

        // Reset password form
        const resetPasswordForm = document.getElementById('adminResetPasswordForm');
        if (resetPasswordForm) {
            resetPasswordForm.addEventListener('submit', this._handleResetPasswordSubmit);
        }

        // Change email form
        const changeEmailForm = document.getElementById('adminChangeEmailForm');
        if (changeEmailForm) {
            changeEmailForm.addEventListener('submit', this._handleChangeEmailSubmit);
        }

        // Provider select change
        const providerSelect = document.getElementById('userProviderSelect');
        if (providerSelect) {
            providerSelect.addEventListener('change', this._handleProviderSelectChange);
        }

        // Table action buttons via event delegation
        if (this.tableBody) {
            this.tableBody.addEventListener('click', this._handleTableClick);
        }

        // Filter buttons via event delegation
        const filterContainer = document.querySelector('.user-filter-buttons');
        if (filterContainer) {
            filterContainer.addEventListener('click', this._handleFilterClick);
        }
    }

    /**
     * Handle table action button clicks
     * @private
     * @param {Event} e - Click event
     */
    _handleTableClick(e) {
        const btn = e.target.closest('[data-action]');
        if (!btn) return;

        const action = btn.dataset.action;
        const userId = parseInt(btn.dataset.userId);
        const userName = btn.dataset.userName;
        const userEmail = btn.dataset.userEmail;

        switch (action) {
            case 'edit':
                this.edit(userId);
                break;
            case 'reset-password':
                this.openResetPassword(userId, userName);
                break;
            case 'change-email':
                this.openChangeEmail(userId, userName, userEmail);
                break;
            case 'delete':
                this.delete(userId);
                break;
            case 'reactivate':
                this.reactivate(userId);
                break;
        }
    }

    /**
     * Handle filter button clicks
     * @private
     * @param {Event} e - Click event
     */
    _handleFilterClick(e) {
        const btn = e.target.closest('[data-filter]');
        if (!btn) return;

        this.setFilter(btn.dataset.filter);
    }

    /**
     * Handle provider select change
     * @private
     * @param {Event} e - Change event
     */
    _handleProviderSelectChange(e) {
        const providerId = e.target.value ? parseInt(e.target.value) : null;
        this._updateProviderBadge(providerId);
    }

    /**
     * Load users from API
     * @returns {Promise<void>}
     */
    async load() {
        try {
            // Build query string based on filter
            // API: activeOnly=true (active), activeOnly=false (inactive), activeOnly= (null/all)
            // Note: API defaults to activeOnly=true, so we must explicitly send empty value for 'all'
            let queryParam = '';
            if (this.currentFilter === 'active') {
                queryParam = '?activeOnly=true';
            } else if (this.currentFilter === 'inactive') {
                queryParam = '?activeOnly=false';
            } else if (this.currentFilter === 'all') {
                queryParam = '?activeOnly=';
            }

            // Load users and providers in parallel
            const [users, providers] = await Promise.all([
                this._apiGet(`/users${queryParam}`),
                this._apiGet('/providers?activeOnly=false')
            ]);

            this.users = users || [];
            this.providers = providers || [];

            this._render();
            this._emit('users:loaded', { users: this.users, filter: this.currentFilter });
        } catch (error) {
            console.error('[UsersModule] Failed to load users:', error);
            this._showError('Failed to load users');
            throw error;
        }
    }

    /**
     * Set user filter
     * @param {string} filter - Filter type: 'active', 'inactive', 'all'
     */
    setFilter(filter) {
        this.currentFilter = filter;
        this._updateFilterButtons();
        this.load();
    }

    /**
     * Update filter button states
     * @private
     */
    _updateFilterButtons() {
        document.querySelectorAll('[data-filter]').forEach(btn => {
            const isActive = btn.dataset.filter === this.currentFilter;

            // Remove all outline and solid variants
            btn.classList.remove('btn-primary', 'btn-outline-primary', 'btn-secondary', 'btn-outline-secondary', 'active');

            if (isActive) {
                btn.classList.add('btn-primary');
            } else {
                btn.classList.add('btn-outline-secondary');
            }
        });
    }

    /**
     * Render users to the table
     * @private
     */
    _render() {
        if (!this.tableBody) return;

        // Create provider lookup map
        const providerMap = new Map();
        this.providers.forEach(p => providerMap.set(p.ProviderId, p));

        if (!this.users.length) {
            this.tableBody.innerHTML = `
                <tr>
                    <td colspan="7" class="text-center text-muted py-4">No users found</td>
                </tr>
            `;
            return;
        }

        this.tableBody.innerHTML = this.users.map(u => this._renderUserRow(u, providerMap)).join('');
    }

    /**
     * Render a single user row
     * @private
     * @param {Object} user - User object
     * @param {Map} providerMap - Provider lookup map
     * @returns {string} HTML
     */
    _renderUserRow(user, providerMap) {
        const provider = user.ProviderId ? providerMap.get(user.ProviderId) : null;
        const providerDisplay = provider
            ? `<span class="badge bg-primary"><i class="bi bi-person-badge me-1"></i>${this._escape(provider.FirstName)} ${this._escape(provider.LastName)}</span>`
            : '<span class="text-muted small">-</span>';

        const escapedName = `${this._escape(user.FirstName)} ${this._escape(user.LastName)}`;
        const escapedEmail = this._escape(user.Email);

        const actionButtons = user.IsActive
            ? `
                <button class="btn btn-sm btn-outline-primary" data-action="edit" data-user-id="${user.UserId}" title="Edit">
                    <i class="bi bi-pencil"></i>
                </button>
                <button class="btn btn-sm btn-outline-warning" data-action="reset-password" data-user-id="${user.UserId}" data-user-name="${escapedName}" title="Reset Password">
                    <i class="bi bi-key"></i>
                </button>
                <button class="btn btn-sm btn-outline-info" data-action="change-email" data-user-id="${user.UserId}" data-user-name="${escapedName}" data-user-email="${escapedEmail}" title="Change Email">
                    <i class="bi bi-envelope"></i>
                </button>
                <button class="btn btn-sm btn-outline-danger" data-action="delete" data-user-id="${user.UserId}" title="Deactivate">
                    <i class="bi bi-trash"></i>
                </button>
            `
            : `
                <button class="btn btn-sm btn-success" data-action="reactivate" data-user-id="${user.UserId}" title="Reactivate">
                    <i class="bi bi-person-check me-1"></i>Reactivate
                </button>
            `;

        return `
            <tr class="${user.IsActive ? '' : 'table-secondary'}">
                <td><strong>${escapedName}</strong></td>
                <td>${escapedEmail}</td>
                <td><span class="badge bg-secondary">${this.roleNames[user.Role] || 'User'}</span></td>
                <td>${providerDisplay}</td>
                <td>${this._escape(user.TenantName || '-')}</td>
                <td>
                    <span class="badge ${user.IsActive ? 'bg-success' : 'bg-secondary'}">
                        ${user.IsActive ? 'Active' : 'Inactive'}
                    </span>
                </td>
                <td>${actionButtons}</td>
            </tr>
        `;
    }

    /**
     * Open add user modal
     * @returns {Promise<void>}
     */
    async openAddModal() {
        try {
            // Load fresh providers
            this.providers = await this._apiGet('/providers?activeOnly=true') || [];

            if (!this.userForm) return;

            this.userForm.reset();
            document.getElementById('userId').value = '';

            this._populateProviderSelect(null);
            this._updateProviderBadge(null);
            await this._populateClinicSelect(null);

            // Show password field for new users
            document.querySelectorAll('.new-user-password').forEach(el => el.style.display = 'block');
            document.querySelector('#userForm [name="Password"]')?.setAttribute('required', 'required');

            document.querySelector('#userModal .modal-title').textContent = 'Add New User';

            const modal = new bootstrap.Modal(this.userModal);
            modal.show();
        } catch (error) {
            console.error('[UsersModule] Failed to open add modal:', error);
            this._showError('Failed to load providers');
        }
    }

    /**
     * Edit a user
     * @param {number} userId - User ID
     * @returns {Promise<void>}
     */
    async edit(userId) {
        try {
            const [user, providers] = await Promise.all([
                this._apiGet(`/users/${userId}`),
                this._apiGet('/providers?activeOnly=false')
            ]);

            if (!user) return;

            this.providers = providers || [];

            if (!this.userForm) return;

            this.userForm.reset();
            document.getElementById('userId').value = user.UserId;

            this.userForm.querySelector('[name="FirstName"]').value = user.FirstName || '';
            this.userForm.querySelector('[name="LastName"]').value = user.LastName || '';
            this.userForm.querySelector('[name="Email"]').value = user.Email || '';
            this.userForm.querySelector('[name="Phone"]').value = user.Phone || '';
            this.userForm.querySelector('[name="Role"]').value = user.Role;
            this.userForm.querySelector('[name="IsActive"]').checked = user.IsActive !== false;

            this._populateProviderSelect(user.ProviderId);
            this._updateProviderBadge(user.ProviderId);
            await this._populateClinicSelect(user.TenantId);

            // Hide password field for edit
            document.querySelectorAll('.new-user-password').forEach(el => el.style.display = 'none');
            document.querySelector('#userForm [name="Password"]')?.removeAttribute('required');

            document.querySelector('#userModal .modal-title').textContent = 'Edit User';

            // Close provider modal if open
            bootstrap.Modal.getInstance(document.getElementById('providerModal'))?.hide();

            const modal = new bootstrap.Modal(this.userModal);
            modal.show();

            this._emit('users:editing', { userId, user });
        } catch (error) {
            console.error('[UsersModule] Failed to edit user:', error);
            this._showError('Failed to load user data');
        }
    }

    /**
     * Handle user form submission
     * @private
     * @param {Event} e - Submit event
     */
    async _handleUserFormSubmit(e) {
        e.preventDefault();

        const formData = new FormData(e.target);
        const userId = formData.get('UserId');
        const isEdit = userId && userId !== '';

        const data = {
            FirstName: formData.get('FirstName'),
            LastName: formData.get('LastName'),
            Email: formData.get('Email'),
            Phone: formData.get('Phone') || null,
            Role: parseInt(formData.get('Role')),
            IsActive: formData.get('IsActive') === 'on'
        };

        // Add TenantId for Super Admin
        const tenantId = formData.get('TenantId');
        if (tenantId) {
            data.TenantId = parseInt(tenantId);
        }

        // Add ProviderId
        const providerId = formData.get('ProviderId');
        data.ProviderId = providerId ? parseInt(providerId) : null;

        // Add password for new users
        if (!isEdit) {
            const password = formData.get('Password');
            if (!password || password.length < 8) {
                this._showError('Password must be at least 8 characters');
                return;
            }
            data.Password = password;
        }

        try {
            if (isEdit) {
                await this._apiPut(`/users/${userId}`, data);
                this._showSuccess('User updated successfully');
                this._emit('users:updated', { userId, data });
            } else {
                const result = await this._apiPost('/users', data);
                this._showSuccess('User created successfully');
                this._emit('users:created', { user: result });
            }

            bootstrap.Modal.getInstance(this.userModal)?.hide();
            e.target.reset();
            await this.load();
        } catch (error) {
            console.error('[UsersModule] Failed to save user:', error);
            this._showError(error.message || 'Failed to save user');
        }
    }

    /**
     * Delete (deactivate) a user
     * @param {number} userId - User ID
     * @returns {Promise<void>}
     */
    async delete(userId) {
        const confirmed = await this._confirm({
            title: 'Deactivate User',
            message: 'Are you sure you want to deactivate this user?',
            confirmText: 'Deactivate',
            confirmClass: 'btn-danger',
            headerClass: 'bg-danger text-white'
        });
        if (!confirmed) return;

        try {
            await this._apiDelete(`/users/${userId}`);
            this._showSuccess('User deactivated successfully');
            this._emit('users:deleted', { userId });
            await this.load();
        } catch (error) {
            console.error('[UsersModule] Failed to deactivate user:', error);
            this._showError('Failed to deactivate user');
        }
    }

    /**
     * Reactivate a user
     * @param {number} userId - User ID
     * @returns {Promise<void>}
     */
    async reactivate(userId) {
        const confirmed = await this._confirm({
            title: 'Reactivate User',
            message: 'Are you sure you want to reactivate this user? They will be able to log in again.',
            confirmText: 'Reactivate',
            confirmClass: 'btn-success',
            headerClass: 'bg-success text-white'
        });
        if (!confirmed) return;

        try {
            await this._apiPost(`/users/${userId}/reactivate`);
            this._showSuccess('User reactivated successfully');
            this._emit('users:reactivated', { userId });
            await this.load();
        } catch (error) {
            console.error('[UsersModule] Failed to reactivate user:', error);
            this._showError('Failed to reactivate user');
        }
    }

    /**
     * Open reset password modal
     * @param {number} userId - User ID
     * @param {string} userName - User's display name
     */
    openResetPassword(userId, userName) {
        document.getElementById('resetPasswordUserId').value = userId;
        document.getElementById('resetPasswordUserName').textContent = userName;
        document.getElementById('adminResetPasswordForm')?.reset();
        document.getElementById('resetPasswordUserId').value = userId;

        const modal = new bootstrap.Modal(this.resetPasswordModal);
        modal.show();
    }

    /**
     * Handle reset password form submission
     * @private
     * @param {Event} e - Submit event
     */
    async _handleResetPasswordSubmit(e) {
        e.preventDefault();

        const formData = new FormData(e.target);
        const userId = formData.get('UserId');
        const newPassword = formData.get('NewPassword');
        const confirmPassword = formData.get('ConfirmPassword');

        if (newPassword !== confirmPassword) {
            this._showError('Passwords do not match');
            return;
        }

        if (newPassword.length < 8) {
            this._showError('Password must be at least 8 characters');
            return;
        }

        try {
            await this._apiPost(`/users/${userId}/reset-password`, {
                UserId: parseInt(userId),
                NewPassword: newPassword
            });

            this._showSuccess('Password reset successfully');
            bootstrap.Modal.getInstance(this.resetPasswordModal)?.hide();
            e.target.reset();
            this._emit('users:passwordReset', { userId });
        } catch (error) {
            console.error('[UsersModule] Failed to reset password:', error);
            this._showError(error.message || 'Failed to reset password');
        }
    }

    /**
     * Open change email modal
     * @param {number} userId - User ID
     * @param {string} userName - User's display name
     * @param {string} currentEmail - Current email
     */
    openChangeEmail(userId, userName, currentEmail) {
        document.getElementById('changeEmailUserId').value = userId;
        document.getElementById('changeEmailUserName').textContent = userName;
        document.getElementById('currentUserEmail').value = currentEmail;
        document.getElementById('adminChangeEmailForm')?.reset();
        document.getElementById('changeEmailUserId').value = userId;
        document.getElementById('currentUserEmail').value = currentEmail;

        const modal = new bootstrap.Modal(this.changeEmailModal);
        modal.show();
    }

    /**
     * Handle change email form submission
     * @private
     * @param {Event} e - Submit event
     */
    async _handleChangeEmailSubmit(e) {
        e.preventDefault();

        const formData = new FormData(e.target);
        const userId = formData.get('UserId');
        const newEmail = formData.get('NewEmail');

        if (!newEmail || !newEmail.includes('@')) {
            this._showError('Please enter a valid email address');
            return;
        }

        try {
            await this._apiPost(`/users/${userId}/change-email`, {
                UserId: parseInt(userId),
                NewEmail: newEmail
            });

            this._showSuccess('Email changed successfully');
            bootstrap.Modal.getInstance(this.changeEmailModal)?.hide();
            e.target.reset();
            this._emit('users:emailChanged', { userId, newEmail });
            await this.load();
        } catch (error) {
            console.error('[UsersModule] Failed to change email:', error);
            this._showError(error.message || 'Failed to change email');
        }
    }

    /**
     * Populate clinic (tenant) dropdown — Super Admin only.
     * Non-super-admins won't see the field (hidden via CSS .requires-super-admin),
     * so we skip the API call entirely to avoid a needless 403 for other roles.
     * @private
     * @param {number|null} selectedTenantId - Currently selected tenant ID (for edit)
     */
    async _populateClinicSelect(selectedTenantId) {
        const select = document.getElementById('userTenantSelect');
        if (!select) return;

        if (!document.body.classList.contains('super-admin')) return;

        try {
            const tenants = await this._apiGet('/tenants') || [];
            select.innerHTML = '<option value="">Select a clinic...</option>' +
                tenants.map(t => {
                    const sel = selectedTenantId && t.TenantId === selectedTenantId ? ' selected' : '';
                    return `<option value="${t.TenantId}"${sel}>${this._escape(t.Name)}</option>`;
                }).join('');
        } catch (error) {
            console.error('[UsersModule] Failed to load clinics:', error);
        }
    }

    /**
     * Populate provider dropdown
     * @private
     * @param {number|null} selectedProviderId - Currently selected provider ID
     */
    _populateProviderSelect(selectedProviderId) {
        const select = document.getElementById('userProviderSelect');
        if (!select) return;

        select.innerHTML = '<option value="">-- No Provider (Non-Clinician Account) --</option>';

        this.providers.forEach(p => {
            const option = document.createElement('option');
            option.value = p.ProviderId;
            option.textContent = `${p.FirstName} ${p.LastName}${p.Credentials ? `, ${p.Credentials}` : ''}${p.Specialty ? ` - ${p.Specialty}` : ''}`;
            if (selectedProviderId && p.ProviderId === selectedProviderId) {
                option.selected = true;
            }
            select.appendChild(option);
        });
    }

    /**
     * Update provider badge in modal
     * @private
     * @param {number|null} providerId - Provider ID
     */
    _updateProviderBadge(providerId) {
        const badge = document.getElementById('userProviderBadge');
        if (!badge) return;

        if (providerId) {
            const provider = this.providers.find(p => p.ProviderId === providerId);
            if (provider) {
                badge.className = 'badge bg-primary';
                badge.innerHTML = `<i class="bi bi-person-badge me-1"></i>${this._escape(provider.FirstName)} ${this._escape(provider.LastName)}`;
                return;
            }
        }

        badge.className = 'badge bg-secondary';
        badge.textContent = 'No Provider';
    }

    /**
     * Get a user by ID
     * @param {number} userId - User ID
     * @returns {Object|null}
     */
    getUser(userId) {
        return this.users.find(u => u.UserId === userId) || null;
    }

    /**
     * Refresh users from server
     * @returns {Promise<void>}
     */
    async refresh() {
        await this.load();
    }

    // === API Methods ===

    async _apiGet(url) {
        if (this.api) return this.api.get(url);
        const response = await fetch(`/api${url}`, { headers: this._getHeaders() });
        if (!response.ok) throw new Error((await response.json().catch(() => ({}))).message || 'Request failed');
        return response.json();
    }

    async _apiPost(url, data = {}) {
        if (this.api) return this.api.post(url, data);
        const response = await fetch(`/api${url}`, {
            method: 'POST',
            headers: this._getHeaders(),
            body: JSON.stringify(data)
        });
        if (!response.ok) throw new Error((await response.json().catch(() => ({}))).message || 'Request failed');
        return response.json();
    }

    async _apiPut(url, data) {
        if (this.api) return this.api.put(url, data);
        const response = await fetch(`/api${url}`, {
            method: 'PUT',
            headers: this._getHeaders(),
            body: JSON.stringify(data)
        });
        if (!response.ok) throw new Error((await response.json().catch(() => ({}))).message || 'Request failed');
        return response.json();
    }

    async _apiDelete(url) {
        if (this.api) return this.api.delete(url);
        const response = await fetch(`/api${url}`, {
            method: 'DELETE',
            headers: this._getHeaders()
        });
        if (!response.ok) throw new Error((await response.json().catch(() => ({}))).message || 'Request failed');
        return response.json();
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

    async _confirm(options) {
        if (window.ConfirmDialog) return ConfirmDialog.show(options);
        return confirm(options.message);
    }

    _showSuccess(message) {
        if (window.Toast) Toast.success('Success', message);
    }

    _showError(message) {
        if (window.Toast) Toast.error('Error', message);
    }

    _emit(event, data = {}) {
        if (this.eventBus) this.eventBus.emit(event, data);
    }

    /**
     * Destroy the module and clean up
     */
    destroy() {
        if (this.userForm) {
            this.userForm.removeEventListener('submit', this._handleUserFormSubmit);
        }

        const resetPasswordForm = document.getElementById('adminResetPasswordForm');
        if (resetPasswordForm) {
            resetPasswordForm.removeEventListener('submit', this._handleResetPasswordSubmit);
        }

        const changeEmailForm = document.getElementById('adminChangeEmailForm');
        if (changeEmailForm) {
            changeEmailForm.removeEventListener('submit', this._handleChangeEmailSubmit);
        }

        const providerSelect = document.getElementById('userProviderSelect');
        if (providerSelect) {
            providerSelect.removeEventListener('change', this._handleProviderSelectChange);
        }

        if (this.tableBody) {
            this.tableBody.removeEventListener('click', this._handleTableClick);
        }

        const filterContainer = document.querySelector('.user-filter-buttons');
        if (filterContainer) {
            filterContainer.removeEventListener('click', this._handleFilterClick);
        }

        this.users = [];
        this.providers = [];
        this.tableBody = null;
        this.userModal = null;
        this.userForm = null;
        this.isInitialized = false;
    }
}

// Export for module usage
window.UsersModule = UsersModule;

// Auto-initialize when DOM is ready
document.addEventListener('DOMContentLoaded', function() {
    const container = document.getElementById('usersPage');
    if (!container) return;

    const initWhenReady = () => {
        const isAuthenticated = (typeof currentUser !== 'undefined' && currentUser) ||
                               (window.App && window.App.isAuthenticated && window.App.isAuthenticated());

        if (!isAuthenticated) {
            setTimeout(initWhenReady, 200);
            return;
        }

        if (window.usersModule) {
            window.usersModule.load();
            return;
        }

        window.usersModule = new UsersModule({
            api: window.apiService || (window.App && window.App.api),
            eventBus: window.eventBus || (window.App && window.App.events)
        });

        window.usersModule.init();
        window.usersModule.load();
    };

    initWhenReady();
});
