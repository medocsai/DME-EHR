/**
 * LocationModule - Handles location switching and management
 *
 * Features:
 * - Location switcher for users
 * - Location management for admins (CRUD)
 * - Set primary location
 * - Timezone handling
 */
class LocationModule {
    constructor(options = {}) {
        this.api = options.api || null;
        this.eventBus = options.eventBus || null;

        // State
        this.availableLocations = [];
        this.currentLocation = null;
        this.timezones = [];

        // Bound handlers
        this._boundHandlers = {};
    }

    /**
     * Initialize the module
     */
    async init() {
        this._loadFromStorage();
        this._bindEvents();
        this._subscribeToAuthEvents();
        this._initLocationDisplay();

        // Fetch fresh location data from API to ensure we have current data
        await this._syncWithApi();
    }

    /**
     * Subscribe to auth events to refresh location data when user logs in
     */
    _subscribeToAuthEvents() {
        // Listen for login/auth events to refresh location data
        if (this.eventBus) {
            this.eventBus.on('auth:login-success', (data) => this._handleAuthSuccess(data));
            this.eventBus.on('auth:restored', () => this._handleAuthRestored());
        }

        // Also listen for App events if available
        if (window.App && window.App.events) {
            window.App.events.on('auth:login-success', (data) => this._handleAuthSuccess(data));
            window.App.events.on('auth:restored', () => this._handleAuthRestored());
            window.App.events.on('app:authenticated', () => this._handleAuthRestored());
        }
    }

    /**
     * Handle successful login - update location from login response
     */
    _handleAuthSuccess(data) {
        console.log('[LocationModule] Auth success - updating location data');

        if (data.LocationId && data.LocationName) {
            this.currentLocation = {
                LocationId: data.LocationId,
                Name: data.LocationName,
                TimeZoneId: data.TimeZoneId || 'America/Chicago',
                TimeZoneAbbreviation: data.TimeZoneAbbreviation || 'CT'
            };
            window.currentLocation = this.currentLocation;
            this._saveToStorage();
        }

        if (data.AvailableLocations) {
            this.availableLocations = data.AvailableLocations;
            this._saveToStorage();
        }

        this._updateLocationDisplay();
    }

    /**
     * Handle auth restored - reload from storage and sync with API
     */
    async _handleAuthRestored() {
        console.log('[LocationModule] Auth restored - syncing location data');
        this._loadFromStorage();
        await this._syncWithApi();
        this._updateLocationDisplay();
    }

    /**
     * Sync location data with API to ensure we have current data
     */
    async _syncWithApi() {
        try {
            const user = this._getCurrentUser();
            if (!user || (user.Role === 0 && !user.TenantId)) {
                // Skip for unauthenticated users or super admins without tenant
                return;
            }

            // Fetch fresh location list from API
            const locations = await this._apiGet('/locations/dropdown');

            if (locations && locations.length > 0) {
                this.availableLocations = locations;

                // Update current location if it exists in the new list
                if (this.currentLocation?.LocationId) {
                    const updatedLocation = locations.find(l => l.LocationId === this.currentLocation.LocationId);
                    if (updatedLocation) {
                        // Update the name and other properties from fresh data
                        this.currentLocation.Name = updatedLocation.Name;
                        this.currentLocation.TimeZoneId = updatedLocation.TimeZoneId || this.currentLocation.TimeZoneId;
                        this.currentLocation.TimeZoneAbbreviation = updatedLocation.TimeZoneAbbreviation || this.currentLocation.TimeZoneAbbreviation;
                    } else {
                        // Current location not found - use primary or first location
                        const primary = locations.find(l => l.IsPrimary) || locations[0];
                        this.currentLocation = {
                            LocationId: primary.LocationId,
                            Name: primary.Name,
                            TimeZoneId: primary.TimeZoneId || 'America/Chicago',
                            TimeZoneAbbreviation: primary.TimeZoneAbbreviation || 'CT'
                        };
                    }
                } else {
                    // No current location - set to primary or first
                    const primary = locations.find(l => l.IsPrimary) || locations[0];
                    this.currentLocation = {
                        LocationId: primary.LocationId,
                        Name: primary.Name,
                        TimeZoneId: primary.TimeZoneId || 'America/Chicago',
                        TimeZoneAbbreviation: primary.TimeZoneAbbreviation || 'CT'
                    };
                }

                window.currentLocation = this.currentLocation;
                this._saveToStorage();
                this._updateLocationDisplay();
            }
        } catch (error) {
            console.error('[LocationModule] Error syncing with API:', error);
            // Continue with existing data if API call fails
        }
    }

    /**
     * Get current location
     */
    getCurrentLocation() {
        return this.currentLocation;
    }

    /**
     * Get current location timezone
     */
    getCurrentTimezone() {
        if (this.currentLocation?.TimeZoneId) {
            return {
                timeZoneId: this.currentLocation.TimeZoneId,
                abbreviation: this.currentLocation.TimeZoneAbbreviation || ''
            };
        }
        return { timeZoneId: 'America/Chicago', abbreviation: 'CT' };
    }

    /**
     * Open the location switcher modal
     */
    openSwitcher() {
        const modal = document.getElementById('locationSwitcherModal');
        if (modal) {
            new bootstrap.Modal(modal).show();
            this.loadSwitcherList();
        }
    }

    /**
     * Load the list of available locations in switcher
     */
    async loadSwitcherList() {
        const listContainer = document.getElementById('locationSwitcherList');
        if (!listContainer) return;

        try {
            if (!this.availableLocations || this.availableLocations.length === 0) {
                this.availableLocations = await this._apiGet('/locations/dropdown');
                this._saveToStorage();
            }

            if (!this.availableLocations || this.availableLocations.length === 0) {
                listContainer.innerHTML = `
                    <div class="text-center text-muted py-3">
                        <i class="bi bi-geo-alt fs-1 d-block mb-2"></i>
                        <p>No locations available</p>
                    </div>
                `;
                return;
            }

            listContainer.innerHTML = this.availableLocations.map(loc => `
                <div class="location-item ${this.currentLocation?.LocationId === loc.LocationId ? 'active' : ''}"
                     data-action="switch-location" data-location-id="${loc.LocationId}" data-location-name="${this._escape(loc.Name)}">
                    <div class="location-item-icon">
                        <i class="bi bi-geo-alt-fill"></i>
                    </div>
                    <div class="location-item-info">
                        <div class="location-item-name">${this._escape(loc.Name)}</div>
                        ${loc.IsPrimary ? '<span class="badge badge-primary-location">Primary</span>' : ''}
                    </div>
                    <div class="location-item-badge">
                        <i class="bi bi-check-circle-fill check-icon"></i>
                    </div>
                </div>
            `).join('');

        } catch (error) {
            console.error('Error loading locations:', error);
            listContainer.innerHTML = `
                <div class="text-center text-danger py-3">
                    <i class="bi bi-exclamation-circle fs-1 d-block mb-2"></i>
                    <p>Error loading locations</p>
                </div>
            `;
        }
    }

    /**
     * Switch to a different location
     */
    async switchTo(locationId, locationName) {
        try {
            this._showLoader();

            const result = await this._apiPost('/locations/switch', { LocationId: locationId });

            // Update local state
            this.currentLocation = {
                LocationId: result.LocationId,
                Name: result.LocationName,
                TimeZoneId: result.TimeZoneId || 'America/Chicago',
                TimeZoneAbbreviation: result.TimeZoneAbbreviation || 'CT'
            };

            this._saveToStorage();

            // Update global state for other modules
            window.currentLocation = this.currentLocation;

            // Update token if provided - CRITICAL: Update ALL token references
            if (result.Token) {
                // Update global variable (legacy support)
                if (window.authToken !== undefined) {
                    window.authToken = result.Token;
                }

                // Update localStorage
                localStorage.setItem('authToken', result.Token);

                // Update App.auth.authToken (AuthModule instance property)
                // This is critical for API requests to use the new token with updated LocationId
                if (window.App && window.App.auth) {
                    window.App.auth.authToken = result.Token;
                }

                // Update ApiService token if available
                if (window.App && window.App.api && typeof window.App.api.setAuthToken === 'function') {
                    window.App.api.setAuthToken(result.Token);
                }
            }

            // Update UI
            this._updateLocationDisplay();

            // Close modal
            const modal = bootstrap.Modal.getInstance(document.getElementById('locationSwitcherModal'));
            if (modal) modal.hide();

            // Reload the entire page to refresh all data for the new location
            window.location.reload();

        } catch (error) {
            console.error('Error switching location:', error);
            this._showToast('Error', error.message || 'Failed to switch location', 'error');
        } finally {
            this._hideLoader();
        }
    }

    /**
     * Open location management modal (admin only)
     */
    openManagement() {
        console.log('[LocationModule] openManagement() method called');

        const user = this._getCurrentUser();
        console.log('[LocationModule] Current user:', user);
        console.log('[LocationModule] User role:', user?.Role);

        if (user?.Role > 1) {
            console.log('[LocationModule] Access denied - role > 1');
            this._showToast('Access Denied', 'Only administrators can manage locations', 'error');
            return;
        }

        const modal = document.getElementById('locationManagementModal');
        console.log('[LocationModule] Modal element found:', !!modal);

        if (modal) {
            console.log('[LocationModule] Opening modal...');
            new bootstrap.Modal(modal).show();
            this.loadManagementList();
        } else {
            console.error('[LocationModule] locationManagementModal not found in DOM');
        }
    }

    /**
     * Open management from switcher
     */
    openManagementFromSwitcher() {
        const switcherModal = bootstrap.Modal.getInstance(document.getElementById('locationSwitcherModal'));
        if (switcherModal) switcherModal.hide();

        setTimeout(() => this.openManagement(), 300);
    }

    /**
     * Load locations for management
     */
    async loadManagementList() {
        const listContainer = document.getElementById('locationManagementList');
        if (!listContainer) return;

        try {
            const locations = await this._apiGet('/locations');

            if (!locations || locations.length === 0) {
                listContainer.innerHTML = `
                    <div class="text-center text-muted py-4">
                        <i class="bi bi-geo-alt fs-1 d-block mb-2"></i>
                        <p>No locations found</p>
                    </div>
                `;
                return;
            }

            listContainer.innerHTML = locations.map(loc => this._renderManagementItem(loc)).join('');

        } catch (error) {
            console.error('Error loading locations:', error);
            listContainer.innerHTML = `
                <div class="text-center text-danger py-3">
                    <i class="bi bi-exclamation-circle fs-1 d-block mb-2"></i>
                    <p>Error loading locations</p>
                </div>
            `;
        }
    }

    /**
     * Open form to add a new location
     */
    async openAddForm() {
        this._resetForm();
        document.getElementById('locationFormTitle').textContent = 'Add Location';
        await this._loadTimezones('America/Chicago');

        new bootstrap.Modal(document.getElementById('locationFormModal')).show();
    }

    /**
     * Edit an existing location
     */
    async edit(locationId) {
        try {
            const location = await this._apiGet(`/locations/${locationId}`);

            document.getElementById('locationFormTitle').textContent = 'Edit Location';
            document.getElementById('locationFormId').value = location.LocationId;
            document.getElementById('locationFormName').value = location.Name || '';
            document.getElementById('locationFormAddress').value = location.Address || '';
            document.getElementById('locationFormCity').value = location.City || '';
            document.getElementById('locationFormState').value = location.State || '';
            document.getElementById('locationFormZip').value = location.ZipCode || '';
            document.getElementById('locationFormPhone').value = location.Phone || '';
            document.getElementById('locationFormFacilityNpi').value = location.FacilityNpi || '';
            document.getElementById('locationFormPlaceOfService').value = location.PlaceOfServiceCode || '11';
            document.getElementById('locationFormPrimary').checked = location.IsPrimary || false;
            const elLongEdit = document.getElementById('locationFormEnableLongevity');
            if (elLongEdit) elLongEdit.checked = !!location.EnableLongevity;

            await this._loadTimezones(location.TimeZoneId || 'America/Chicago');

            new bootstrap.Modal(document.getElementById('locationFormModal')).show();

        } catch (error) {
            console.error('Error loading location:', error);
            this._showToast('Error', 'Failed to load location details', 'error');
        }
    }

    /**
     * Save location (create or update)
     */
    async save(event) {
        if (event) event.preventDefault();

        const locationId = document.getElementById('locationFormId').value;
        const isEdit = !!locationId;

        const data = {
            Name: document.getElementById('locationFormName').value,
            Address: document.getElementById('locationFormAddress').value,
            City: document.getElementById('locationFormCity').value,
            State: document.getElementById('locationFormState').value,
            ZipCode: document.getElementById('locationFormZip').value,
            Phone: document.getElementById('locationFormPhone').value,
            IsPrimary: document.getElementById('locationFormPrimary').checked,
            TimeZoneId: document.getElementById('locationFormTimezone').value,
            FacilityNpi: document.getElementById('locationFormFacilityNpi').value,
            PlaceOfServiceCode: document.getElementById('locationFormPlaceOfService').value,
            EnableLongevity: !!(document.getElementById('locationFormEnableLongevity') || {}).checked
        };

        if (!data.TimeZoneId) {
            this._showToast('Validation Error', 'Please select a timezone for the location', 'error');
            return;
        }

        try {
            this._showLoader();

            if (isEdit) {
                await this._apiPut(`/locations/${locationId}`, data);
            } else {
                await this._apiPost('/locations', data);
            }

            // Close form modal
            const formModal = bootstrap.Modal.getInstance(document.getElementById('locationFormModal'));
            if (formModal) formModal.hide();

            this._showToast('Success', isEdit ? 'Location updated successfully' : 'Location created successfully');

            this.loadManagementList();
            await this.refreshAvailable();

        } catch (error) {
            console.error('Error saving location:', error);
            this._showToast('Error', error.message || 'Failed to save location', 'error');
        } finally {
            this._hideLoader();
        }
    }

    /**
     * Set a location as primary
     */
    async setPrimary(locationId) {
        try {
            this._showLoader();

            await this._apiPost(`/locations/${locationId}/set-primary`);

            this._showToast('Success', 'Primary location updated');
            this.loadManagementList();
            await this.refreshAvailable();

        } catch (error) {
            console.error('Error setting primary location:', error);
            this._showToast('Error', error.message || 'Failed to set primary location', 'error');
        } finally {
            this._hideLoader();
        }
    }

    /**
     * Refresh available locations from API
     */
    async refreshAvailable() {
        try {
            this.availableLocations = await this._apiGet('/locations/dropdown');
            this._saveToStorage();

            // Update global state
            if (window.availableLocations !== undefined) {
                window.availableLocations = this.availableLocations;
            }
        } catch (error) {
            console.error('Error refreshing locations:', error);
        }
    }

    /**
     * Clean up module resources
     */
    destroy() {
        this._unbindEvents();
    }

    // ========================================
    // Private Methods - Rendering
    // ========================================

    _renderManagementItem(loc) {
        return `
            <div class="location-item">
                <div class="location-item-icon">
                    <i class="bi bi-geo-alt-fill"></i>
                </div>
                <div class="location-item-info">
                    <div class="location-item-name">
                        ${this._escape(loc.Name)}
                        ${loc.IsPrimary ? '<span class="badge badge-primary-location ms-2">Primary</span>' : ''}
                    </div>
                    <div class="location-item-address">
                        ${loc.Address ? this._escape(loc.Address) : ''}
                        ${loc.City ? ', ' + this._escape(loc.City) : ''}
                        ${loc.State ? ', ' + this._escape(loc.State) : ''}
                        ${loc.Phone ? ' • ' + this._escape(loc.Phone) : ''}
                    </div>
                    <div class="location-item-timezone">
                        <i class="bi bi-clock text-muted me-1"></i>
                        <small class="text-muted">
                            ${loc.TimeZoneId ? this._escape(loc.TimeZoneId) : 'No timezone set'}
                            ${loc.TimeZoneAbbreviation ? `(${this._escape(loc.TimeZoneAbbreviation)})` : ''}
                        </small>
                    </div>
                    <small class="text-muted">${loc.PatientCount || 0} patients</small>
                </div>
                <div class="location-management-actions">
                    <button class="btn btn-outline-primary btn-sm" data-action="edit-location" data-location-id="${loc.LocationId}" title="Edit">
                        <i class="bi bi-pencil"></i>
                    </button>
                    ${!loc.IsPrimary ? `
                        <button class="btn btn-outline-success btn-sm" data-action="set-primary-location" data-location-id="${loc.LocationId}" title="Set as Primary">
                            <i class="bi bi-star"></i>
                        </button>
                    ` : ''}
                </div>
            </div>
        `;
    }

    _resetForm() {
        document.getElementById('locationFormId').value = '';
        document.getElementById('locationFormName').value = '';
        document.getElementById('locationFormAddress').value = '';
        document.getElementById('locationFormCity').value = '';
        document.getElementById('locationFormState').value = '';
        document.getElementById('locationFormZip').value = '';
        document.getElementById('locationFormPhone').value = '';
        document.getElementById('locationFormFacilityNpi').value = '';
        document.getElementById('locationFormPlaceOfService').value = '11';
        document.getElementById('locationFormPrimary').checked = false;
        const elLongNew = document.getElementById('locationFormEnableLongevity');
        if (elLongNew) elLongNew.checked = false;
    }

    async _loadTimezones(selectedValue) {
        const select = document.getElementById('locationFormTimezone');
        if (!select) return;

        // Common US timezones
        const timezones = [
            { id: 'America/New_York', name: 'Eastern Time (ET)', abbr: 'ET' },
            { id: 'America/Chicago', name: 'Central Time (CT)', abbr: 'CT' },
            { id: 'America/Denver', name: 'Mountain Time (MT)', abbr: 'MT' },
            { id: 'America/Phoenix', name: 'Arizona Time (AZ)', abbr: 'AZ' },
            { id: 'America/Los_Angeles', name: 'Pacific Time (PT)', abbr: 'PT' },
            { id: 'America/Anchorage', name: 'Alaska Time (AKT)', abbr: 'AKT' },
            { id: 'Pacific/Honolulu', name: 'Hawaii Time (HT)', abbr: 'HT' }
        ];

        select.innerHTML = '<option value="">Select timezone...</option>' +
            timezones.map(tz =>
                `<option value="${tz.id}" ${tz.id === selectedValue ? 'selected' : ''}>${tz.name}</option>`
            ).join('');
    }

    _initLocationDisplay() {
        const locationSection = document.getElementById('sidebarLocation');
        const locationName = document.getElementById('currentLocationName');

        if (!locationSection || !locationName) return;

        const user = this._getCurrentUser();

        // Hide for Super Admin without tenant
        if (user?.Role === 0 && !user?.TenantId) {
            locationSection.style.display = 'none';
            return;
        }

        locationSection.style.display = 'block';
        this._updateLocationDisplay();
    }

    _updateLocationDisplay() {
        const locationName = document.getElementById('currentLocationName');
        if (!locationName) return;

        if (this.currentLocation?.Name) {
            locationName.textContent = this.currentLocation.Name;
        } else if (this.availableLocations?.length > 0) {
            const primary = this.availableLocations.find(l => l.IsPrimary) || this.availableLocations[0];
            locationName.textContent = primary.Name;
            this.currentLocation = { LocationId: primary.LocationId, Name: primary.Name };
            window.currentLocation = this.currentLocation;
            this._saveToStorage();
        } else {
            locationName.textContent = 'No Location';
        }
    }

    // ========================================
    // Private Methods - Storage
    // ========================================

    _loadFromStorage() {
        try {
            const stored = localStorage.getItem('currentLocation');
            if (stored) {
                this.currentLocation = JSON.parse(stored);
                // Expose to window for other modules
                window.currentLocation = this.currentLocation;
            }

            const storedAvailable = localStorage.getItem('availableLocations');
            if (storedAvailable) {
                this.availableLocations = JSON.parse(storedAvailable);
            }
        } catch (e) {
            console.error('Error loading location from storage:', e);
        }
    }

    _saveToStorage() {
        try {
            if (this.currentLocation) {
                localStorage.setItem('currentLocation', JSON.stringify(this.currentLocation));
            }
            if (this.availableLocations) {
                localStorage.setItem('availableLocations', JSON.stringify(this.availableLocations));
            }
        } catch (e) {
            console.error('Error saving location to storage:', e);
        }
    }

    // ========================================
    // Private Methods - Event Handling
    // ========================================

    _bindEvents() {
        this._boundHandlers.docClick = (e) => this._handleDocumentClick(e);
        document.addEventListener('click', this._boundHandlers.docClick);

        // Form submit
        const form = document.getElementById('locationForm');
        if (form) {
            this._boundHandlers.formSubmit = (e) => this.save(e);
            form.addEventListener('submit', this._boundHandlers.formSubmit);
        }
    }

    _unbindEvents() {
        if (this._boundHandlers.docClick) {
            document.removeEventListener('click', this._boundHandlers.docClick);
        }

        const form = document.getElementById('locationForm');
        if (form && this._boundHandlers.formSubmit) {
            form.removeEventListener('submit', this._boundHandlers.formSubmit);
        }
    }

    _handleDocumentClick(e) {
        const target = e.target.closest('[data-action]');
        if (!target) return;

        const action = target.getAttribute('data-action');
        const locationId = target.getAttribute('data-location-id');
        const locationName = target.getAttribute('data-location-name');

        switch (action) {
            case 'switch-location':
                this.switchTo(parseInt(locationId), locationName);
                break;
            case 'edit-location':
                this.edit(parseInt(locationId));
                break;
            case 'set-primary-location':
                this.setPrimary(parseInt(locationId));
                break;
        }
    }

    _refreshCurrentPageData() {
        const activePage = document.querySelector('.page.active');
        if (!activePage) return;

        const pageId = activePage.id.replace('Page', '');

        // Emit event for page-specific refresh
        this._emit('location:refreshPage', { pageId });

        // Fallback to global functions
        switch (pageId) {
            case 'dashboard':
                if (typeof window.loadDashboard === 'function') window.loadDashboard();
                break;
            case 'patients':
                if (typeof window.loadPatients === 'function') window.loadPatients();
                break;
            case 'schedule':
                // Use CalendarModule's refresh method
                if (window.calendarModule && typeof window.calendarModule.refresh === 'function') {
                    window.calendarModule.refresh();
                } else if (window.App?.modules?.get('calendar')?.refresh) {
                    window.App.modules.get('calendar').refresh();
                }
                break;
            case 'notes':
                if (typeof window.loadClinicalNotes === 'function') window.loadClinicalNotes();
                break;
            case 'billing':
                if (typeof window.loadBillingData === 'function') window.loadBillingData();
                break;
            case 'consent-forms':
                if (typeof window.loadConsentFormsPage === 'function') window.loadConsentFormsPage();
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

    // ========================================
    // Private Methods - Utilities
    // ========================================

    _escape(str) {
        if (str === null || str === undefined) return '';
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }

    _getCurrentUser() {
        // Try App.auth first, then fallback to window.currentUser
        if (window.App && window.App.auth && typeof window.App.auth.getCurrentUser === 'function') {
            return window.App.auth.getCurrentUser();
        }
        return window.currentUser || null;
    }

    _showToast(title, message, type = 'success') {
        if (window.Toast) {
            window.Toast.show(title, message, type);
        } else if (window.showToast) {
            window.showToast(title, message, type);
        }
    }

    _showLoader() {
        if (typeof window.showGlobalLoader === 'function') {
            window.showGlobalLoader();
        }
    }

    _hideLoader() {
        if (typeof window.hideGlobalLoader === 'function') {
            window.hideGlobalLoader();
        }
    }

    _emit(event, data) {
        if (this.eventBus) {
            this.eventBus.emit(event, data);
        }
    }
}

// Export for module usage
if (typeof module !== 'undefined' && module.exports) {
    module.exports = LocationModule;
}

// Global functions for legacy onclick handlers
function openLocationSwitcher() {
    console.log('[LocationModule] openLocationSwitcher called');
    console.log('[LocationModule] window.locationModule exists:', !!window.locationModule);

    // Ensure LocationModule is initialized
    if (!window.locationModule) {
        console.log('[LocationModule] Attempting sync initialization...');
        _initLocationModuleSync();
    }

    if (window.locationModule) {
        console.log('[LocationModule] Calling openSwitcher()');
        window.locationModule.openSwitcher();
    } else {
        console.error('[LocationModule] Failed to initialize locationModule');
    }
}

function openLocationManagement() {
    console.log('[LocationModule] openLocationManagement called');
    console.log('[LocationModule] window.locationModule exists:', !!window.locationModule);

    // Ensure LocationModule is initialized
    if (!window.locationModule) {
        console.log('[LocationModule] Attempting sync initialization...');
        _initLocationModuleSync();
    }

    if (window.locationModule) {
        console.log('[LocationModule] Calling openManagement()');
        window.locationModule.openManagement();
    } else {
        console.error('[LocationModule] Failed to initialize locationModule');
    }
}

// Helper to synchronously initialize LocationModule if needed
function _initLocationModuleSync() {
    console.log('[LocationModule] _initLocationModuleSync called');

    if (window.locationModule) {
        console.log('[LocationModule] Already initialized, skipping');
        return;
    }

    const hasCurrentUser = typeof currentUser !== 'undefined' && currentUser;
    const hasAppAuth = window.App && window.App.isAuthenticated && window.App.isAuthenticated();
    const isAuthenticated = hasCurrentUser || hasAppAuth;

    console.log('[LocationModule] Auth check:', { hasCurrentUser, hasAppAuth, isAuthenticated });

    if (!isAuthenticated) {
        console.warn('[LocationModule] Not authenticated, cannot initialize');
        return;
    }

    console.log('[LocationModule] Creating new LocationModule instance');
    window.locationModule = new LocationModule({
        api: window.apiService || (window.App && window.App.api),
        eventBus: window.eventBus || (window.App && window.App.events)
    });
    window.locationModule.init();
    console.log('[LocationModule] Initialization complete');
}

function openLocationManagementFromSwitcher() {
    if (window.locationModule) {
        window.locationModule.openManagementFromSwitcher();
    }
}

function switchToLocation(locationId, locationName) {
    if (window.locationModule) {
        window.locationModule.switchTo(locationId, locationName);
    }
}

function openAddLocationForm() {
    if (window.locationModule) {
        window.locationModule.openAddForm();
    }
}

function editLocation(locationId) {
    if (window.locationModule) {
        window.locationModule.edit(locationId);
    }
}

function saveLocation(event) {
    if (window.locationModule) {
        window.locationModule.save(event);
    }
}

function setPrimaryLocation(locationId) {
    if (window.locationModule) {
        window.locationModule.setPrimary(locationId);
    }
}

function getCurrentLocationTimezone() {
    if (window.locationModule) {
        return window.locationModule.getCurrentTimezone();
    }
    return { timeZoneId: 'America/Chicago', abbreviation: 'CT' };
}

// Export for module usage
window.LocationModule = LocationModule;

// Explicitly export global functions to window
window.openLocationSwitcher = openLocationSwitcher;
window.openLocationManagement = openLocationManagement;
window.openLocationManagementFromSwitcher = openLocationManagementFromSwitcher;
window.switchToLocation = switchToLocation;
window.openAddLocationForm = openAddLocationForm;
window.editLocation = editLocation;
window.saveLocation = saveLocation;
window.setPrimaryLocation = setPrimaryLocation;
window.getCurrentLocationTimezone = getCurrentLocationTimezone;

// Auto-initialize when DOM is ready
document.addEventListener('DOMContentLoaded', function() {
    // LocationModule is global - needed for sidebar location switcher on all pages
    const initWhenReady = () => {
        const isAuthenticated = (typeof currentUser !== 'undefined' && currentUser) ||
                               (window.App && window.App.isAuthenticated && window.App.isAuthenticated());

        // Only initialize if authenticated (sidebar is visible)
        if (!isAuthenticated) {
            setTimeout(initWhenReady, 200);
            return;
        }

        // Don't reinitialize if already exists
        if (window.locationModule) {
            return;
        }

        window.locationModule = new LocationModule({
            api: window.apiService || (window.App && window.App.api),
            eventBus: window.eventBus || (window.App && window.App.events)
        });

        window.locationModule.init();
    };

    initWhenReady();
});
