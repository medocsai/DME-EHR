/**
 * UnavailabilityModule - Handles therapist time off requests
 *
 * Features:
 * - List unavailabilities
 * - Create/Edit time off requests
 * - Approve/Delete requests
 * - Provider filtering
 */
class UnavailabilityModule {
    constructor(options = {}) {
        this.api = options.api || null;
        this.eventBus = options.eventBus || null;

        // State
        this.unavailabilities = [];
        this.providers = [];

        // Type names
        this.typeNames = ['Time Off', 'Vacation', 'Sick Leave', 'Personal', 'Training', 'Other'];

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
     * Load unavailabilities list
     */
    async load() {
        try {
            const user = this._getCurrentUser();
            let url = '/therapist-unavailability?includeUnapproved=true';

            // For therapists, filter by their provider ID
            if (user?.Role === 2 && user?.ProviderId) {
                url += `&providerId=${user.ProviderId}`;
            }

            this.unavailabilities = await this._apiGet(url) || [];
            this._render();

        } catch (error) {
            console.error('Load unavailabilities error:', error);
        }
    }

    /**
     * Open modal for new time off request
     */
    openNewModal() {
        const form = document.getElementById('unavailabilityForm');
        if (!form) return;

        form.reset();
        document.getElementById('unavailabilityId').value = '';
        document.querySelector('#unavailabilityModal .modal-title').textContent = 'Request Time Off';
        document.querySelector('.partial-day-times')?.classList.add('d-none');

        // Set default dates
        const today = new Date().toISOString().split('T')[0];

        // Initialize Multiple Days mode (unchecked = single day)
        const multipleDaysCheckbox = document.getElementById('unavailMultipleDays');
        if (multipleDaysCheckbox) {
            multipleDaysCheckbox.checked = false;
        }

        // Set single date field
        const singleDateField = document.getElementById('unavailSingleDate');
        if (singleDateField) {
            singleDateField.value = today;
        }

        // Also set hidden start/end dates for form submission
        form.querySelector('[name="StartDate"]').value = today;
        form.querySelector('[name="EndDate"]').value = today;

        // Update UI for single day mode
        this._updateDateFieldsVisibility();

        // Load providers for admin
        this._loadProviders();

        new bootstrap.Modal(document.getElementById('unavailabilityModal')).show();
    }

    /**
     * Edit an existing unavailability
     */
    async edit(id) {
        try {
            const unavailability = await this._apiGet(`/therapist-unavailability/${id}`);
            if (!unavailability) return;

            const form = document.getElementById('unavailabilityForm');
            form.reset();

            // Load providers FIRST and wait for completion
            await this._loadProviders();

            // NOW set the form values after providers are loaded
            document.getElementById('unavailabilityId').value = unavailability.UnavailabilityId;
            document.getElementById('unavailabilityProviderSelect').value = unavailability.ProviderId;
            form.querySelector('[name="Type"]').value = unavailability.Type;

            // Parse dates properly to avoid timezone issues
            const startDate = unavailability.StartDate?.split('T')[0] || unavailability.StartDate;
            const endDate = unavailability.EndDate?.split('T')[0] || unavailability.EndDate;

            form.querySelector('[name="StartDate"]').value = startDate;
            form.querySelector('[name="EndDate"]').value = endDate;
            form.querySelector('[name="IsFullDay"]').checked = unavailability.IsFullDay;

            // Determine if this is a multiple days request
            const isMultipleDays = startDate !== endDate;
            const multipleDaysCheckbox = document.getElementById('unavailMultipleDays');
            if (multipleDaysCheckbox) {
                multipleDaysCheckbox.checked = isMultipleDays;
            }

            // Set single date field
            const singleDateField = document.getElementById('unavailSingleDate');
            if (singleDateField) {
                singleDateField.value = startDate;
            }

            // Update date fields visibility
            this._updateDateFieldsVisibility();

            if (!unavailability.IsFullDay) {
                document.querySelector('.partial-day-times').classList.remove('d-none');
                form.querySelector('[name="UnavailabilityStartTime"]').value = unavailability.StartTime || '';
                form.querySelector('[name="EndTime"]').value = unavailability.EndTime || '';
            } else {
                document.querySelector('.partial-day-times').classList.add('d-none');
            }

            form.querySelector('[name="Reason"]').value = unavailability.Reason || '';

            document.querySelector('#unavailabilityModal .modal-title').textContent = 'Edit Time Off Request';

            new bootstrap.Modal(document.getElementById('unavailabilityModal')).show();

        } catch (error) {
            console.error('Load unavailability error:', error);
            this._showToast('Error', 'Failed to load time off request', 'error');
        }
    }

    /**
     * Save unavailability (create or update)
     */
    async save(event) {
        if (event) event.preventDefault();

        const form = document.getElementById('unavailabilityForm');
        const formData = new FormData(form);
        const unavailabilityId = formData.get('UnavailabilityId');

        // Get provider ID - for clinicians, use their own provider ID
        const user = this._getCurrentUser();
        let providerId = null;

        // For clinicians (Role 2), always use their own provider ID
        if (user?.Role === 2 && user?.ProviderId) {
            providerId = user.ProviderId;
        } else {
            // For admins, get from select (disabled selects don't submit via FormData)
            const providerSelect = document.getElementById('unavailabilityProviderSelect');
            providerId = providerSelect?.value || formData.get('ProviderId');
        }

        if (!providerId) {
            this._showToast('Error', 'Please select a provider', 'error');
            return;
        }

        const isFullDay = form.querySelector('[name="IsFullDay"]').checked;

        // Handle single day vs multiple days
        const multipleDaysCheckbox = document.getElementById('unavailMultipleDays');
        const isMultipleDays = multipleDaysCheckbox?.checked || false;

        let startDate, endDate;
        if (isMultipleDays) {
            startDate = formData.get('StartDate');
            endDate = formData.get('EndDate');
        } else {
            // Single day mode - use single date field for both start and end
            const singleDate = document.getElementById('unavailSingleDate')?.value || formData.get('StartDate');
            startDate = singleDate;
            endDate = singleDate;
        }

        const data = {
            ProviderId: parseInt(providerId),
            Type: parseInt(formData.get('Type')),
            StartDate: startDate,
            EndDate: endDate,
            IsFullDay: isFullDay,
            StartTime: isFullDay ? null : this._formatTime(formData.get('UnavailabilityStartTime')),
            EndTime: isFullDay ? null : this._formatTime(formData.get('EndTime')),
            Reason: formData.get('Reason') || null
        };

        try {
            if (unavailabilityId) {
                await this._apiPut(`/therapist-unavailability/${unavailabilityId}`, data);
                this._showToast('Success', 'Time off request updated');
            } else {
                await this._apiPost('/therapist-unavailability', data);
                this._showToast('Success', 'Time off request submitted');
            }

            // Properly close the modal and remove backdrop
            const modalEl = document.getElementById('unavailabilityModal');
            const modalInstance = bootstrap.Modal.getInstance(modalEl);
            if (modalInstance) {
                modalInstance.hide();
            }
            // Ensure backdrop is removed
            document.querySelectorAll('.modal-backdrop').forEach(el => el.remove());
            document.body.classList.remove('modal-open');
            document.body.style.removeProperty('overflow');
            document.body.style.removeProperty('padding-right');

            await this.load();

            // Refresh calendar if available
            this._emit('unavailability:saved');

        } catch (error) {
            console.error('Save unavailability error:', error);
            this._showToast('Error', error.message || 'Failed to save time off request', 'error');
        }
    }

    /**
     * Approve an unavailability
     */
    async approve(id) {
        try {
            await this._apiPost(`/therapist-unavailability/${id}/approve`);
            this._showToast('Success', 'Unavailability approved');
            await this.load();
            this._emit('unavailability:approved');
        } catch (error) {
            console.error('Approve unavailability error:', error);
            this._showToast('Error', 'Failed to approve request', 'error');
        }
    }

    /**
     * Delete an unavailability
     */
    async delete(id) {
        const confirmed = await this._confirm(
            'Delete Unavailability',
            'Are you sure you want to delete this unavailability record?'
        );

        if (!confirmed) return;

        try {
            await this._apiDelete(`/therapist-unavailability/${id}`);
            this._showToast('Success', 'Unavailability deleted');
            await this.load();
            this._emit('unavailability:deleted');
        } catch (error) {
            console.error('Delete unavailability error:', error);
            this._showToast('Error', 'Failed to delete request', 'error');
        }
    }

    /**
     * Clean up module resources
     */
    destroy() {
        this._unbindEvents();
        this.unavailabilities = [];
        this.providers = [];
    }

    // ========================================
    // Private Methods - Rendering
    // ========================================

    _render() {
        const tbody = document.querySelector('#unavailabilityTable tbody');
        if (!tbody) return;

        const user = this._getCurrentUser();

        tbody.innerHTML = this.unavailabilities.length ? this.unavailabilities.map(u => `
            <tr>
                <td>${this._escape(u.ProviderName)}</td>
                <td>
                    ${this._formatDate(u.StartDate)}${u.StartDate !== u.EndDate ? ` - ${this._formatDate(u.EndDate)}` : ''}
                    <br><small class="text-muted">${u.IsFullDay ? 'Full Day' : `${this._formatTimeDisplay(u.StartTime)} - ${this._formatTimeDisplay(u.EndTime)}`}</small>
                </td>
                <td>${u.TypeName || this.typeNames[u.Type] || 'Unknown'}</td>
                <td>${this._escape(u.Reason || '-')}</td>
                <td>
                    ${u.IsApproved
                        ? '<span class="badge bg-success">Approved</span>'
                        : '<span class="badge bg-warning">Pending</span>'}
                </td>
                <td>
                    ${!u.IsApproved && (user?.Role <= 1) ? `
                        <button class="btn btn-sm btn-success" data-action="approve-unavailability" data-id="${u.UnavailabilityId}">
                            Approve
                        </button>
                    ` : ''}
                    ${(user?.Role <= 1 || !u.IsApproved) ? `
                        <button class="btn btn-sm btn-outline-primary" data-action="edit-unavailability" data-id="${u.UnavailabilityId}">
                            <i class="bi bi-pencil"></i>
                        </button>
                    ` : ''}
                    <button class="btn btn-sm btn-outline-danger" data-action="delete-unavailability" data-id="${u.UnavailabilityId}">
                        <i class="bi bi-trash"></i>
                    </button>
                </td>
            </tr>
        `).join('') : '<tr><td colspan="6" class="text-center text-muted py-4">No unavailability records found</td></tr>';
    }

    async _loadProviders() {
        const user = this._getCurrentUser();
        const select = document.getElementById('unavailabilityProviderSelect');

        if (!select) return;

        // For therapists, just show their own name (disabled)
        if (user?.Role === 2 && user?.ProviderId) {
            select.innerHTML = `<option value="${user.ProviderId}">${this._escape(user.FullName || user.FirstName)}</option>`;
            select.disabled = true;
            return;
        }

        // For admins, load all providers
        select.disabled = false;

        if (select.options.length <= 1) {
            try {
                const providers = await this._apiGet('/providers?activeOnly=true');
                select.innerHTML = '<option value="">Select Provider...</option>' +
                    (providers?.map(p =>
                        `<option value="${p.ProviderId}">${this._escape(p.FirstName)} ${this._escape(p.LastName)}</option>`
                    ).join('') || '');
            } catch (error) {
                console.error('Error loading providers:', error);
            }
        }
    }

    // ========================================
    // Private Methods - Event Handling
    // ========================================

    _bindEvents() {
        this._boundHandlers.docClick = (e) => this._handleDocumentClick(e);
        document.addEventListener('click', this._boundHandlers.docClick);

        // Form submit
        const form = document.getElementById('unavailabilityForm');
        if (form) {
            this._boundHandlers.formSubmit = (e) => this.save(e);
            form.addEventListener('submit', this._boundHandlers.formSubmit);
        }

        // Full day checkbox toggle
        const fullDayCheckbox = document.getElementById('unavailIsFullDay');
        if (fullDayCheckbox) {
            this._boundHandlers.fullDayChange = (e) => this._handleFullDayChange(e);
            fullDayCheckbox.addEventListener('change', this._boundHandlers.fullDayChange);
        }

        // Multiple days checkbox toggle
        const multipleDaysCheckbox = document.getElementById('unavailMultipleDays');
        if (multipleDaysCheckbox) {
            this._boundHandlers.multipleDaysChange = (e) => this._handleMultipleDaysChange(e);
            multipleDaysCheckbox.addEventListener('change', this._boundHandlers.multipleDaysChange);
        }

        // Single date field sync
        const singleDateField = document.getElementById('unavailSingleDate');
        if (singleDateField) {
            this._boundHandlers.singleDateChange = (e) => this._handleSingleDateChange(e);
            singleDateField.addEventListener('change', this._boundHandlers.singleDateChange);
        }

        // Modal show event
        const modal = document.getElementById('unavailabilityModal');
        if (modal) {
            this._boundHandlers.modalShow = (e) => this._handleModalShow(e);
            modal.addEventListener('show.bs.modal', this._boundHandlers.modalShow);
        }
    }

    _unbindEvents() {
        if (this._boundHandlers.docClick) {
            document.removeEventListener('click', this._boundHandlers.docClick);
        }

        const form = document.getElementById('unavailabilityForm');
        if (form && this._boundHandlers.formSubmit) {
            form.removeEventListener('submit', this._boundHandlers.formSubmit);
        }

        const fullDayCheckbox = document.getElementById('unavailIsFullDay');
        if (fullDayCheckbox && this._boundHandlers.fullDayChange) {
            fullDayCheckbox.removeEventListener('change', this._boundHandlers.fullDayChange);
        }

        const multipleDaysCheckbox = document.getElementById('unavailMultipleDays');
        if (multipleDaysCheckbox && this._boundHandlers.multipleDaysChange) {
            multipleDaysCheckbox.removeEventListener('change', this._boundHandlers.multipleDaysChange);
        }

        const singleDateField = document.getElementById('unavailSingleDate');
        if (singleDateField && this._boundHandlers.singleDateChange) {
            singleDateField.removeEventListener('change', this._boundHandlers.singleDateChange);
        }

        const modal = document.getElementById('unavailabilityModal');
        if (modal && this._boundHandlers.modalShow) {
            modal.removeEventListener('show.bs.modal', this._boundHandlers.modalShow);
        }
    }

    _handleDocumentClick(e) {
        const target = e.target.closest('[data-action]');
        if (!target) return;

        const action = target.getAttribute('data-action');
        const id = target.getAttribute('data-id');

        switch (action) {
            case 'approve-unavailability':
                this.approve(parseInt(id));
                break;
            case 'edit-unavailability':
                this.edit(parseInt(id));
                break;
            case 'delete-unavailability':
                this.delete(parseInt(id));
                break;
        }
    }

    _handleFullDayChange(e) {
        const partialTimes = document.querySelector('.partial-day-times');
        if (partialTimes) {
            partialTimes.classList.toggle('d-none', e.target.checked);
        }
    }

    _handleMultipleDaysChange(e) {
        this._updateDateFieldsVisibility();

        // If switching to single day mode, sync the single date from start date
        if (!e.target.checked) {
            const startDate = document.querySelector('[name="StartDate"]')?.value;
            const singleDateField = document.getElementById('unavailSingleDate');
            if (singleDateField && startDate) {
                singleDateField.value = startDate;
            }
        }
    }

    _handleSingleDateChange(e) {
        // Sync single date to both start and end date fields
        const form = document.getElementById('unavailabilityForm');
        if (form) {
            form.querySelector('[name="StartDate"]').value = e.target.value;
            form.querySelector('[name="EndDate"]').value = e.target.value;
        }
    }

    _updateDateFieldsVisibility() {
        const multipleDaysCheckbox = document.getElementById('unavailMultipleDays');
        const isMultipleDays = multipleDaysCheckbox?.checked || false;

        const singleDateContainer = document.querySelector('.single-date-field');
        const dateRangeContainer = document.querySelector('.date-range-fields');

        if (singleDateContainer) {
            singleDateContainer.classList.toggle('d-none', isMultipleDays);
        }
        if (dateRangeContainer) {
            dateRangeContainer.classList.toggle('d-none', !isMultipleDays);
        }
    }

    _handleModalShow(e) {
        // Only reset for new request button
        if (e.relatedTarget && e.relatedTarget.getAttribute('data-bs-target') === '#unavailabilityModal') {
            this.openNewModal();
        }
    }

    // ========================================
    // Private Methods - API
    // ========================================

    async _apiGet(endpoint) {
        if (this.api) {
            return await this.api.get(endpoint);
        }
        return await window.apiRequest(endpoint);
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
        // Parse date parts directly to avoid timezone issues
        // DateOnly strings like "2026-02-03" should not be converted via UTC
        const datePart = dateStr.split('T')[0];
        const [year, month, day] = datePart.split('-').map(Number);
        const date = new Date(year, month - 1, day); // month is 0-indexed
        return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
    }

    _formatTime(timeValue) {
        if (!timeValue) return null;
        return timeValue.length === 5 ? timeValue + ':00' : timeValue;
    }

    _formatTimeDisplay(timeStr) {
        if (!timeStr) return '';
        // timeStr is in format "HH:mm:ss" or "HH:mm" from backend
        const [hours, minutes] = timeStr.split(':').map(Number);
        const period = hours >= 12 ? 'PM' : 'AM';
        const displayHours = hours % 12 || 12;
        return `${displayHours}:${minutes.toString().padStart(2, '0')} ${period}`;
    }

    _getCurrentUser() {
        // Try multiple sources for current user
        if (window.currentUser) return window.currentUser;
        if (window.App?.getCurrentUser) return window.App.getCurrentUser();
        try {
            const stored = localStorage.getItem('currentUser');
            return stored ? JSON.parse(stored) : null;
        } catch {
            return null;
        }
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
        if (typeof window.showConfirmModal === 'function') {
            return await window.showConfirmModal({
                title,
                message,
                confirmText: 'Delete',
                confirmClass: 'btn-danger',
                headerClass: 'bg-danger text-white'
            });
        }
        return confirm(message);
    }

    _emit(event, data) {
        if (this.eventBus) {
            this.eventBus.emit(event, data);
        }

        // Also trigger calendar refresh
        if (window.calendar && (event.includes('saved') || event.includes('approved') || event.includes('deleted'))) {
            window.calendar.refetchEvents();
        }
    }
}

// Export for module usage
if (typeof module !== 'undefined' && module.exports) {
    module.exports = UnavailabilityModule;
}

// Global functions for legacy onclick handlers
function loadUnavailabilities() {
    if (window.unavailabilityModule) {
        window.unavailabilityModule.load();
    }
}

function approveUnavailability(id) {
    if (window.unavailabilityModule) {
        window.unavailabilityModule.approve(id);
    }
}

function deleteUnavailability(id) {
    if (window.unavailabilityModule) {
        window.unavailabilityModule.delete(id);
    }
}

function editUnavailability(id) {
    if (window.unavailabilityModule) {
        window.unavailabilityModule.edit(id);
    }
}

function handleUnavailabilityForm(e) {
    if (window.unavailabilityModule) {
        window.unavailabilityModule.save(e);
    }
}

// Auto-initialize when DOM is ready
document.addEventListener('DOMContentLoaded', function() {
    const container = document.getElementById('unavailabilityPage');
    if (!container) return;

    const initWhenReady = () => {
        const isAuthenticated = (typeof currentUser !== 'undefined' && currentUser) ||
                               (window.App && window.App.isAuthenticated && window.App.isAuthenticated());

        if (!isAuthenticated) {
            setTimeout(initWhenReady, 200);
            return;
        }

        if (window.unavailabilityModule) {
            window.unavailabilityModule.load();
            return;
        }

        window.unavailabilityModule = new UnavailabilityModule({
            api: window.apiService || (window.App && window.App.api),
            eventBus: window.eventBus || (window.App && window.App.events)
        });

        window.unavailabilityModule.init();
        window.unavailabilityModule.load();
    };

    initWhenReady();
});
