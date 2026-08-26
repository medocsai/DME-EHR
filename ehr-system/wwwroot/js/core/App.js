/**
 * App - Main Application Container
 *
 * Central orchestrator for the modular application architecture.
 * Manages core services, module lifecycle, and application state.
 *
 * @example
 *   // Initialize the application
 *   await App.init();
 *
 *   // Access services
 *   const user = App.auth.getCurrentUser();
 *   const data = await App.api.get('/patients');
 *
 *   // Register and use modules
 *   App.modules.register('patients', new PatientModule({ api: App.api }));
 *   await App.modules.get('patients').init();
 */
const App = (() => {
    // Private state
    let _initialized = false;
    let _modules = new Map();

    // Public API
    const app = {
        // Core services (initialized in init())
        api: null,
        auth: null,
        state: null,
        events: null,

        // Configuration
        config: {
            apiBaseUrl: '/api',
            debugMode: false
        },

        /**
         * Initialize the application
         * @returns {Promise<void>}
         */
        async init() {
            if (_initialized) {
                console.warn('[App] Already initialized');
                return;
            }

            console.log('[App] Initializing...');

            // Initialize core services
            this.events = new EventBus();
            this.state = new StateManager(this.events);
            this.api = new ApiService({ baseUrl: this.config.apiBaseUrl });

            // Initialize auth module
            this.auth = new AuthModule({
                eventBus: this.events,
                apiBase: this.config.apiBaseUrl
            });
            await this.auth.init();

            // Configure API with auth
            this._configureApi();

            // Configure state persistence
            this.state.persist('currentLocation', true);
            this.state.persist('availableLocations', true);

            // Set up global event handlers
            this._setupEventHandlers();

            _initialized = true;
            console.log('[App] Initialization complete');

            // Emit ready event
            this.events.emit('app:ready');

            // Hide loading screen and show appropriate page
            this._showAppropriateView();

            // If authenticated, emit event and load initial data
            if (this.auth.checkAuth()) {
                this.events.emit('app:authenticated', {
                    user: this.auth.getCurrentUser()
                });

                // Initialize core modules that should be available globally
                this._initGlobalModules();
            }
        },

        /**
         * Initialize global modules (available on all pages)
         * @private
         */
        async _initGlobalModules() {
            // Initialize AppointmentModule globally
            if (window.AppointmentModule && !this.modules.has('appointments')) {
                const appointmentModule = new AppointmentModule({
                    api: this.api,
                    eventBus: this.events
                });
                await appointmentModule.init();
                this.modules.register('appointments', appointmentModule);
                window.appointmentModule = appointmentModule;
            }
        },

        /**
         * Hide loading screen and show login or main app
         * @private
         */
        _showAppropriateView() {
            const loadingScreen = document.getElementById('appLoadingScreen');
            const loginPage = document.getElementById('loginPage');
            const mainApp = document.getElementById('mainApp');

            // Hide loading screen
            if (loadingScreen) {
                loadingScreen.classList.add('d-none');
            }

            // Show login or main app based on auth status
            if (this.auth.checkAuth()) {
                if (loginPage) loginPage.classList.add('d-none');
                if (mainApp) mainApp.classList.remove('d-none');
                this._updateUserUI();
            } else {
                if (mainApp) mainApp.classList.add('d-none');
                if (loginPage) loginPage.classList.remove('d-none');
            }
        },

        /**
         * Update user info in sidebar
         * @private
         */
        _updateUserUI() {
            const user = this.auth.getCurrentUser();
            if (!user) return;

            const userNameEl = document.getElementById('userName');
            const userRoleEl = document.getElementById('userRole');
            const userAvatarEl = document.getElementById('userAvatar');

            if (userNameEl) userNameEl.textContent = user.FullName || user.Email || 'User';
            if (userRoleEl) userRoleEl.textContent = this.auth.getRoleName(user.Role);
            if (userAvatarEl) {
                // Try to show profile picture for providers, fallback to initials
                if (user.ProviderId && typeof AvatarUtils !== 'undefined') {
                    userAvatarEl.innerHTML = AvatarUtils.renderUserAvatarContent({
                        providerId: user.ProviderId,
                        name: user.FullName || user.Email || 'U',
                        hasProfilePicture: true // Attempt to load, img onerror will fallback
                    });
                } else {
                    userAvatarEl.textContent = (user.FullName || user.Email || 'U').charAt(0).toUpperCase();
                }
            }

            // Update tenant info and logo
            const tenantEl = document.getElementById('sidebarTenant');
            if (tenantEl && user.TenantName) {
                tenantEl.querySelector('strong').textContent = user.TenantName;
            }

            // Show tenant logo if available (always try loading — logo may have been uploaded after login)
            const tenantLogoContainer = document.getElementById('sidebarTenantLogo');
            const tenantLogoImg = document.getElementById('sidebarTenantLogoImg');
            if (tenantLogoContainer && tenantLogoImg && user.TenantId) {
                tenantLogoImg.src = `/api/tenants/${user.TenantId}/logo?t=${Date.now()}`;
                tenantLogoImg.alt = user.TenantName || 'Clinic Logo';
                tenantLogoImg.onload = () => { tenantLogoContainer.classList.remove('d-none'); };
                tenantLogoImg.onerror = () => { tenantLogoContainer.classList.add('d-none'); };
            }

            // Update role-based visibility
            this._updateRoleBasedVisibility(user.Role);

            // Load global clinic filter for Super Admin
            const roleNum = parseInt(user.Role, 10);
            const isSuperAdmin = roleNum === 0 && !user.TenantId;
            if (isSuperAdmin) {
                this._loadGlobalClinicFilter();
            }
        },

        /**
         * Load and populate the global clinic filter for Super Admin
         * @private
         */
        async _loadGlobalClinicFilter() {
            const filterSelect = document.getElementById('globalClinicFilter');
            if (!filterSelect) return;

            try {
                const response = await fetch('/api/tenants', {
                    headers: {
                        'Authorization': `Bearer ${this.auth.getToken()}`,
                        'Content-Type': 'application/json'
                    }
                });

                if (!response.ok) {
                    console.error('[App] Failed to load tenants');
                    return;
                }

                const tenants = await response.json();

                // Clear and populate
                filterSelect.innerHTML = '<option value="">Select a Clinic</option>';
                tenants.forEach(tenant => {
                    const option = document.createElement('option');
                    option.value = tenant.TenantId;
                    option.textContent = tenant.Name;
                    filterSelect.appendChild(option);
                });

                // Restore previous selection, and re-sync the cookie with it.
                // localStorage never expires and the cookie does, so after the
                // cookie lapses the dropdown would still show a clinic while the
                // server-rendered pages had forgotten which one.
                const savedClinicId = localStorage.getItem('selectedClinicId');
                if (savedClinicId) {
                    filterSelect.value = savedClinicId;
                    this._writeClinicCookie(savedClinicId);
                }

                // Bind change event
                filterSelect.addEventListener('change', (e) => {
                    const clinicId = e.target.value;
                    if (clinicId) {
                        localStorage.setItem('selectedClinicId', clinicId);
                    } else {
                        localStorage.removeItem('selectedClinicId');
                    }
                    // The cookie is what the SERVER reads. localStorage and the
                    // event only reach the SPA modules; the DME screens are
                    // rendered server side and never see either, which is why
                    // switching clinic used to do nothing to them.
                    this._writeClinicCookie(clinicId);
                    // Emit event for modules to reload
                    this.events.emit('clinic:changed', { clinicId });
                    // Reload page to refresh all data
                    window.location.reload();
                });
            } catch (error) {
                console.error('[App] Error loading clinic filter:', error);
            }
        },

        /**
         * Write (or clear) the clinic the server should scope to.
         *
         * WHY A COOKIE AND NOT A HEADER
         * A header can be attached to a fetch call. It cannot be attached to a
         * browser navigating to a Razor page, which is what every DME screen is.
         * The cookie travels with both.
         *
         * WHY IT IS SAFE THAT THIS IS NOT HttpOnly
         * It carries a selection, not a credential, and the server only consults
         * it when the caller's token carries NO TenantId claim. That is Super
         * Admin and nobody else: a clinic admin's claim always wins, so setting
         * this cookie by hand gains them nothing. See
         * Middleware/TenantResolutionMiddleware.cs.
         *
         * @param {string} clinicId Tenant id, or empty to clear the selection
         * @private
         */
        _writeClinicCookie(clinicId) {
            // SameSite=Lax so it survives ordinary navigation but is not sent
            // on cross-site requests. Session cookie on purpose: closing the
            // browser should not leave a Super Admin silently pinned to a
            // clinic they picked days ago.
            const base = 'medocs_clinic=; path=/; SameSite=Lax';
            document.cookie = clinicId
                ? `medocs_clinic=${encodeURIComponent(clinicId)}; path=/; SameSite=Lax`
                : `${base}; expires=Thu, 01 Jan 1970 00:00:00 GMT`;
        },

        /**
         * Update UI elements based on user role
         * CSS uses body classes with !important, so we add classes to body
         * @private
         */
        _updateRoleBasedVisibility(role) {
            // Convert role to number to handle string values from JSON
            const roleNum = parseInt(role, 10);
            const user = this.auth.getCurrentUser();

            // Super Admin = 0, Clinic Admin = 1, Clinician = 2, Front Desk = 3, Biller = 4
            const isSuperAdmin = roleNum === 0 && !user?.TenantId;
            const isAdmin = roleNum === 0 || roleNum === 1;
            const isTherapist = roleNum === 2;
            const isBiller = roleNum === 4;


            // CSS uses body classes with !important to control visibility
            // Add appropriate classes to body element
            const body = document.body;

            // Clear previous role classes
            body.classList.remove('super-admin', 'admin', 'clinician', 'biller', 'front-desk', 'read-only', 'medical-assistant', 'nurse');

            // Add current role classes based on CSS selectors
            if (isSuperAdmin) {
                body.classList.add('super-admin');
                body.classList.add('admin'); // Super admin is also an admin
            } else if (isAdmin) {
                body.classList.add('admin');
            }

            if (isTherapist) {
                body.classList.add('clinician'); // CSS uses 'clinician' class
            }

            if (isBiller) {
                body.classList.add('biller');
            }

            if (roleNum === 3) { // Front Desk
                body.classList.add('front-desk');
            }

            if (roleNum === 5) { // Read Only
                body.classList.add('read-only');
            }

            if (roleNum === 6) { // Medical Assistant
                body.classList.add('medical-assistant');
            }

            if (roleNum === 7) { // Nurse
                body.classList.add('nurse');
            }
        },

        /**
         * Configure API service with auth token
         * @private
         */
        _configureApi() {
            // Add auth token to all API requests
            const originalGet = this.api.get.bind(this.api);
            const originalPost = this.api.post.bind(this.api);
            const originalPut = this.api.put.bind(this.api);
            const originalDelete = this.api.delete.bind(this.api);

            const addAuth = (options = {}) => {
                const token = this.auth.getToken();
                if (token) {
                    options.headers = options.headers || {};
                    options.headers['Authorization'] = `Bearer ${token}`;
                }
                return options;
            };

            this.api.get = (url, options) => originalGet(url, addAuth(options));
            this.api.post = (url, data, options) => originalPost(url, data, addAuth(options));
            this.api.put = (url, data, options) => originalPut(url, data, addAuth(options));
            this.api.delete = (url, options) => originalDelete(url, addAuth(options));
        },

        /**
         * Set up global event handlers
         * @private
         */
        _setupEventHandlers() {
            // Debug logging
            if (this.config.debugMode) {
                this.events.on('*', (event, data) => {
                    console.log(`[App] Event: ${event}`, data);
                });
            }

            // Handle logout - cleanup modules
            this.events.on('auth:logout', () => {
                this.modules.destroyAll();
            });

            // Handle login - update UI and navigate to Dashboard
            this.events.on('auth:login', ({ user }) => {
                console.log('[App] User logged in:', user?.Email || user?.FullName || 'Unknown');
                // Navigate to Dashboard after fresh login to ensure user sees appropriate content
                // This prevents Clinic Admin from seeing Super Admin's leftover pages
                window.location.href = '/Dme/Dashboard';
            });
        },

        /**
         * Module management
         */
        modules: {
            /**
             * Register a module
             * @param {string} name - Module name
             * @param {Object} module - Module instance
             */
            register(name, module) {
                if (_modules.has(name)) {
                    console.warn(`[App] Module '${name}' already registered, replacing...`);
                    const existing = _modules.get(name);
                    if (existing.destroy) existing.destroy();
                }
                _modules.set(name, module);
                console.log(`[App] Module registered: ${name}`);
            },

            /**
             * Get a registered module
             * @param {string} name - Module name
             * @returns {Object|null}
             */
            get(name) {
                return _modules.get(name) || null;
            },

            /**
             * Check if a module is registered
             * @param {string} name - Module name
             * @returns {boolean}
             */
            has(name) {
                return _modules.has(name);
            },

            /**
             * Get all registered modules
             * @returns {Map}
             */
            getAll() {
                return new Map(_modules);
            },

            /**
             * Initialize a registered module
             * @param {string} name - Module name
             * @returns {Promise<void>}
             */
            async init(name) {
                const module = _modules.get(name);
                if (!module) {
                    throw new Error(`Module '${name}' not found`);
                }
                if (module.init && typeof module.init === 'function') {
                    await module.init();
                }
            },

            /**
             * Initialize all registered modules
             * @returns {Promise<void>}
             */
            async initAll() {
                for (const [name, module] of _modules) {
                    try {
                        if (module.init && typeof module.init === 'function') {
                            await module.init();
                        }
                    } catch (error) {
                        console.error(`[App] Failed to init module '${name}':`, error);
                    }
                }
            },

            /**
             * Destroy a module
             * @param {string} name - Module name
             */
            destroy(name) {
                const module = _modules.get(name);
                if (module) {
                    if (module.destroy && typeof module.destroy === 'function') {
                        module.destroy();
                    }
                    _modules.delete(name);
                }
            },

            /**
             * Destroy all modules
             */
            destroyAll() {
                for (const [name, module] of _modules) {
                    if (module.destroy && typeof module.destroy === 'function') {
                        try {
                            module.destroy();
                        } catch (error) {
                            console.error(`[App] Failed to destroy module '${name}':`, error);
                        }
                    }
                }
                _modules.clear();
            }
        },

        // === Convenience methods ===

        /**
         * Get current user
         * @returns {Object|null}
         */
        getCurrentUser() {
            return this.auth?.getCurrentUser() || null;
        },

        /**
         * Get auth token
         * @returns {string|null}
         */
        getAuthToken() {
            return this.auth?.getToken() || null;
        },

        /**
         * Check if user is authenticated
         * @returns {boolean}
         */
        isAuthenticated() {
            return this.auth?.checkAuth() || false;
        },

        /**
         * Check if current user is Super Admin
         * @returns {boolean}
         */
        isSuperAdmin() {
            return this.auth?.isSuperAdmin() || false;
        },

        /**
         * Check if current user is Admin (Clinic or Super)
         * @returns {boolean}
         */
        isAdmin() {
            return this.auth?.isClinicAdmin() || false;
        },

        /**
         * Get current location from state
         * @returns {Object|null}
         */
        getCurrentLocation() {
            return this.state?.get('currentLocation') || null;
        },

        /**
         * Set current location in state
         * @param {Object} location - Location object
         */
        setCurrentLocation(location) {
            this.state?.set('currentLocation', location);
        },

        /**
         * Navigate to a page
         * @param {string} page - Page name
         */
        navigateTo(page) {
            const routes = {
                'dashboard': '/Dme/Dashboard',
                'schedule': '/Home/Schedule',
                'patients': '/Dme/Customers',
                'providers': '/Home/Providers',
                'notes': '/Home/Notes',
                'billing': '/Home/Billing',
                'unavailability': '/Home/Unavailability',
                'reports': '/Home/Reports',
                'templates': '/Home/Templates',
                'users': '/Home/Users',
                'settings': '/Home/Settings',
                'tenants': '/Home/Tenants',
                'consent-forms': '/Home/ConsentForms'
            };

            const url = routes[page.toLowerCase()];
            if (url) {
                window.location.href = url;
            }
        },

        /**
         * Emit a global event
         * @param {string} event - Event name
         * @param {*} data - Event data
         */
        emit(event, data) {
            this.events?.emit(event, data);
        },

        /**
         * Subscribe to a global event
         * @param {string} event - Event name
         * @param {Function} handler - Event handler
         * @returns {Function} Unsubscribe function
         */
        on(event, handler) {
            return this.events?.on(event, handler);
        },

        /**
         * Show a toast notification
         * @param {string} type - 'success', 'error', 'warning', 'info'
         * @param {string} title - Toast title
         * @param {string} message - Toast message
         */
        toast(type, title, message) {
            if (window.Toast) {
                switch (type) {
                    case 'success': Toast.success(title, message); break;
                    case 'error': Toast.error(title, message); break;
                    case 'warning': Toast.warning(title, message); break;
                    case 'info': Toast.info(title, message); break;
                    default: Toast.info(title, message);
                }
            }
        },

        /**
         * Check if app is initialized
         * @returns {boolean}
         */
        isInitialized() {
            return _initialized;
        }
    };

    return app;
})();

// Make App globally available
window.App = App;

// Auto-initialize when DOM is ready
document.addEventListener('DOMContentLoaded', () => {
    App.init().catch(error => {
        console.error('[App] Initialization failed:', error);
    });
});
