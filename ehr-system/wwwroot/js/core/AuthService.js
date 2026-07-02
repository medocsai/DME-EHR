/**
 * AuthService - Authentication and authorization management
 * Handles login, logout, token management, and role-based access
 *
 * Usage:
 *   await App.auth.login(email, password);
 *   App.auth.logout();
 *   const isAuth = App.auth.isAuthenticated();
 *   const canAccess = App.auth.hasRole('Admin');
 */
class AuthService {
    constructor(apiService, stateManager, eventBus) {
        this.api = apiService;
        this.state = stateManager;
        this.events = eventBus;
        this.pendingCredentials = null;
        this.pendingOtp = null; // { email, rememberDevice } — waiting for OTP entry
    }

    /**
     * Attempt to restore session from localStorage
     * @returns {boolean} True if session was restored
     */
    restoreSession() {
        const savedToken = localStorage.getItem('authToken');
        const savedUser = localStorage.getItem('currentUser');

        if (savedToken && savedUser) {
            try {
                const user = JSON.parse(savedUser);
                this.api.setAuthToken(savedToken);
                this.state.set('authToken', savedToken);
                this.state.set('currentUser', user);

                // Restore location state
                const savedLocation = localStorage.getItem('currentLocation');
                const savedLocations = localStorage.getItem('availableLocations');

                if (savedLocation) {
                    this.state.set('currentLocation', JSON.parse(savedLocation));
                }
                if (savedLocations) {
                    this.state.set('availableLocations', JSON.parse(savedLocations));
                }

                this.events.emit('auth:restored', user);
                return true;
            } catch (error) {
                console.error('Failed to restore session:', error);
                this.clearSession();
            }
        }
        return false;
    }

    /**
     * Login with email and password.
     * Step 1 of the OTP flow: if credentials are valid, backend sends OTP to email and returns
     * RequiresOtpVerification=true. Caller must then call verifyOtp(code).
     * If a trusted device cookie is present, OTP is skipped and login completes directly.
     *
     * @param {string} email - User email
     * @param {string} password - User password
     * @param {boolean} [rememberDevice=false] - Create a 15-day trusted device on successful OTP
     * @param {number} [tenantId] - Optional tenant ID for multi-tenant users (trusted-device path)
     * @returns {Promise<Object>} Login result
     */
    async login(email, password, rememberDevice = false, tenantId = null) {
        try {
            const response = await fetch('/api/auth/login', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'include',
                body: JSON.stringify({ Email: email, Password: password, TenantId: tenantId })
            });

            if (!response.ok) {
                const error = await response.json();
                throw new Error(error.message || error.Message || 'Login failed');
            }

            const data = await response.json();

            // OTP required
            if (data.RequiresOtpVerification) {
                this.pendingOtp = { email, rememberDevice };
                this.events.emit('auth:otp-required', { maskedEmail: data.MaskedEmail });
                return { requiresOtpVerification: true, maskedEmail: data.MaskedEmail };
            }

            // Tenant selection required (trusted-device path — OTP was skipped)
            if (data.RequiresTenantSelection && data.AvailableTenants?.length > 1) {
                this.pendingCredentials = { email, password, rememberDevice, fromOtp: false };
                this.events.emit('auth:tenant-selection-required', {
                    tenants: data.AvailableTenants,
                    userName: data.FullName
                });
                return { requiresTenantSelection: true, tenants: data.AvailableTenants };
            }

            return this.completeLogin(data);

        } catch (error) {
            this.events.emit('auth:login-failed', error.message);
            throw error;
        }
    }

    /**
     * Step 2: verify the 6-digit OTP code.
     * Returns tenant selection if user belongs to multiple clinics.
     */
    async verifyOtp(otpCode, tenantId = null) {
        if (!this.pendingOtp && !this.pendingCredentials) {
            throw new Error('No pending OTP to verify');
        }

        const email = (this.pendingOtp || this.pendingCredentials).email;
        const rememberDevice = (this.pendingOtp || this.pendingCredentials).rememberDevice || false;

        try {
            const response = await fetch('/api/auth/verify-otp', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'include',
                body: JSON.stringify({ Email: email, OtpCode: otpCode, TenantId: tenantId, RememberDevice: rememberDevice })
            });

            if (!response.ok) {
                const error = await response.json();
                throw new Error(error.message || error.Message || 'Verification failed');
            }

            const data = await response.json();

            if (data.RequiresTenantSelection) {
                this.pendingCredentials = { email, rememberDevice, fromOtp: true };
                this.pendingOtp = null;
                this.events.emit('auth:tenant-selection-required', {
                    tenants: data.AvailableTenants,
                    userName: data.FullName
                });
                return { requiresTenantSelection: true, tenants: data.AvailableTenants };
            }

            return this.completeLogin(data);
        } catch (error) {
            this.events.emit('auth:otp-failed', error.message);
            throw error;
        }
    }

    /**
     * Request a fresh OTP code (60s server-side cooldown).
     */
    async resendOtp() {
        if (!this.pendingOtp) return false;
        try {
            await fetch('/api/auth/resend-otp', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ Email: this.pendingOtp.email })
            });
            return true;
        } catch (e) {
            console.error('[AuthService] Resend failed:', e);
            return false;
        }
    }

    /**
     * Complete login with user data
     * @param {Object} data - Login response data
     * @returns {Object} User data
     */
    completeLogin(data) {
        this.pendingCredentials = null;

        const token = data.Token ?? data.token ?? data.accessToken;
        if (!token) {
            throw new Error('No token received');
        }

        // Set token
        this.api.setAuthToken(token);
        this.state.set('authToken', token);
        this.state.set('currentUser', data);

        // Store in localStorage
        localStorage.setItem('authToken', token);
        localStorage.setItem('currentUser', JSON.stringify(data));

        // Handle location data
        if (data.LocationId) {
            const location = {
                LocationId: data.LocationId,
                Name: data.LocationName,
                TimeZoneId: data.TimeZoneId || 'America/Chicago',
                TimeZoneAbbreviation: data.TimeZoneAbbreviation || 'CT'
            };
            this.state.set('currentLocation', location);
            localStorage.setItem('currentLocation', JSON.stringify(location));
        }

        if (data.AvailableLocations) {
            this.state.set('availableLocations', data.AvailableLocations);
            localStorage.setItem('availableLocations', JSON.stringify(data.AvailableLocations));
        }

        this.events.emit('auth:login-success', data);
        return data;
    }

    /**
     * Select a tenant (for multi-tenant users).
     * Post-OTP tenant selection uses verify-otp; trusted-device path re-submits login.
     */
    async selectTenant(tenantId) {
        if (!this.pendingCredentials) {
            throw new Error('No pending credentials for tenant selection');
        }

        if (this.pendingCredentials.fromOtp) {
            // Uses "VERIFIED" state window — server accepts empty OTP if state is valid
            return this.verifyOtp('', tenantId);
        }

        return this.login(
            this.pendingCredentials.email,
            this.pendingCredentials.password,
            this.pendingCredentials.rememberDevice || false,
            tenantId
        );
    }

    /**
     * Cancel tenant selection and return to login
     */
    cancelTenantSelection() {
        this.pendingCredentials = null;
        this.pendingOtp = null;
        this.events.emit('auth:tenant-selection-cancelled');
    }

    /**
     * Logout the current user
     */
    logout() {
        this.clearSession();
        this.events.emit('auth:logout');
    }

    /**
     * Clear session data
     */
    clearSession() {
        this.api.clearAuthToken();
        this.state.delete('authToken');
        this.state.delete('currentUser');
        this.state.delete('currentLocation');
        this.state.delete('availableLocations');
        this.pendingCredentials = null;
        this.pendingOtp = null;

        localStorage.removeItem('authToken');
        localStorage.removeItem('currentUser');
        localStorage.removeItem('currentLocation');
        localStorage.removeItem('availableLocations');
        localStorage.removeItem('currentLocationTimezone');
    }

    /**
     * Check if user is authenticated
     * @returns {boolean}
     */
    isAuthenticated() {
        return !!this.state.get('authToken') && !!this.state.get('currentUser');
    }

    /**
     * Get the current user
     * @returns {Object|null}
     */
    getCurrentUser() {
        return this.state.get('currentUser');
    }

    /**
     * Get the auth token
     * @returns {string|null}
     */
    getToken() {
        return this.state.get('authToken');
    }

    /**
     * Get the user's role
     * @returns {number|null} Role enum value
     */
    getRole() {
        return this.getCurrentUser()?.Role ?? null;
    }

    /**
     * Get the user's role name
     * @returns {string}
     */
    getRoleName() {
        const role = this.getRole();
        if (role === null) return 'Unknown';
        return UserRoles.getName(role);
    }

    /**
     * Check if user has a specific role
     * @param {string|number} role - Role name or enum value
     * @returns {boolean}
     */
    hasRole(role) {
        const userRole = this.getRole();
        if (userRole === null) return false;

        // Convert role name to enum value if string
        let targetRole = role;
        if (typeof role === 'string') {
            targetRole = UserRoles[role.toUpperCase()];
        }

        return userRole === targetRole;
    }

    /**
     * Check if user is Super Admin
     * @returns {boolean}
     */
    isSuperAdmin() {
        const user = this.getCurrentUser();
        return user?.Role === 0 && !user?.TenantId;
    }

    /**
     * Check if user is Admin (Clinic Admin or Super Admin with tenant)
     * @returns {boolean}
     */
    isAdmin() {
        const user = this.getCurrentUser();
        return user?.Role === 0 || user?.Role === 1;
    }

    /**
     * Check if user is Clinician
     * @returns {boolean}
     */
    isClinician() {
        return this.getRole() === 2;
    }

    /**
     * Check if user is Front Desk
     * @returns {boolean}
     */
    isFrontDesk() {
        return this.getRole() === 3;
    }

    /**
     * Check if user is Biller
     * @returns {boolean}
     */
    isBiller() {
        return this.getRole() === 4;
    }

    /**
     * Check if user has billing access
     * @returns {boolean}
     */
    hasBillingAccess() {
        const role = this.getRole();
        return role === 0 || role === 1 || role === 4; // Admin or Biller
    }

    /**
     * Check if user can schedule appointments
     * @returns {boolean}
     */
    canSchedule() {
        const role = this.getRole();
        return role !== 4 && role !== 5; // Not Biller or Read-Only
    }

    /**
     * Check if user can edit clinical notes
     * @returns {boolean}
     */
    canEditNotes() {
        const role = this.getRole();
        return role === 0 || role === 1 || role === 2; // Admin or Clinician
    }

    /**
     * Get the tenant ID
     * @returns {number|null}
     */
    getTenantId() {
        return this.getCurrentUser()?.TenantId ?? null;
    }

    /**
     * Get the current location
     * @returns {Object|null}
     */
    getCurrentLocation() {
        return this.state.get('currentLocation');
    }

    /**
     * Get available locations
     * @returns {Array}
     */
    getAvailableLocations() {
        return this.state.get('availableLocations') || [];
    }

    /**
     * Change password
     * @param {string} currentPassword - Current password
     * @param {string} newPassword - New password
     * @returns {Promise<boolean>}
     */
    async changePassword(currentPassword, newPassword) {
        try {
            await this.api.post('/auth/change-password', {
                CurrentPassword: currentPassword,
                NewPassword: newPassword
            });
            return true;
        } catch (error) {
            throw error;
        }
    }

    /**
     * Request password reset
     * @param {string} email - User email
     * @returns {Promise<boolean>}
     */
    async requestPasswordReset(email) {
        try {
            await this.api.post('/auth/forgot-password', { Email: email });
            return true;
        } catch (error) {
            throw error;
        }
    }
}

// Export class
window.AuthService = AuthService;
