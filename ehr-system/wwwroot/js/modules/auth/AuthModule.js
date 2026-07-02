/**
 * AuthModule - Authentication and session management
 *
 * Handles email+password login, OTP verification, tenant selection, and session state.
 * OTP flow:
 *   1. User submits email + password (+ optional Remember Me) → backend sends 6-digit OTP to email
 *   2. User enters OTP → backend verifies and either logs in, or returns tenant-selection
 *   3. If user belongs to multiple clinics, tenant selection is shown after OTP (2-minute window)
 *   4. Trusted device cookie (__medocs_dt) skips OTP for 15 days when Remember Me was checked.
 *
 * @example
 *   const auth = new AuthModule({ eventBus: App.eventBus });
 *   await auth.init();
 */
class AuthModule {
    constructor(options = {}) {
        this.eventBus = options.eventBus || null;
        this.apiBase = options.apiBase || '/api';

        // State
        this.currentUser = null;
        this.authToken = null;
        this.isAuthenticated = false;
        this.pendingOtp = null;           // { email, rememberDevice } — waiting for OTP entry
        this.pendingTenantSelection = null; // { email, userName, tenants, rememberDevice, fromOtp }
        this.isInitialized = false;

        // DOM references
        this.loginForm = null;
        this.loginPage = null;
        this.appContainer = null;
        this.tenantSelectionContainer = null;
        this.otpContainer = null;
        this.otpForm = null;

        // Bind methods
        this._handleLoginSubmit = this._handleLoginSubmit.bind(this);
        this._handleLogoutClick = this._handleLogoutClick.bind(this);
        this._handleTenantSelect = this._handleTenantSelect.bind(this);
        this._handleOtpSubmit = this._handleOtpSubmit.bind(this);
        this._handleOtpResend = this._handleOtpResend.bind(this);
        this._handleOtpCancel = this._handleOtpCancel.bind(this);
    }

    async init() {
        this._restoreSession();

        this.loginForm = document.getElementById('loginForm');
        this.loginPage = document.getElementById('loginPage');
        this.appContainer = document.getElementById('mainApp');
        this.tenantSelectionContainer = document.getElementById('tenantSelection');
        this.otpContainer = document.getElementById('otpVerification');
        this.otpForm = document.getElementById('otpForm');

        this._bindEvents();

        this.isInitialized = true;

        if (this.isAuthenticated) {
            this._emit('auth:restored', { user: this.currentUser });
        }
    }

    _bindEvents() {
        if (this.loginForm) {
            this.loginForm.addEventListener('submit', this._handleLoginSubmit);
        }

        const logoutBtn = document.getElementById('logoutBtn');
        if (logoutBtn) {
            logoutBtn.addEventListener('click', this._handleLogoutClick);
        }

        if (this.tenantSelectionContainer) {
            this.tenantSelectionContainer.addEventListener('click', this._handleTenantSelect);
        }

        if (this.otpForm) {
            this.otpForm.addEventListener('submit', this._handleOtpSubmit);
        }

        const resendBtn = document.getElementById('otpResendBtn');
        if (resendBtn) {
            resendBtn.addEventListener('click', this._handleOtpResend);
        }

        const cancelBtn = document.getElementById('otpCancelBtn');
        if (cancelBtn) {
            cancelBtn.addEventListener('click', this._handleOtpCancel);
        }
    }

    _restoreSession() {
        const token = localStorage.getItem('authToken');
        const userJson = localStorage.getItem('currentUser');

        if (token && userJson) {
            try {
                this.authToken = token;
                this.currentUser = JSON.parse(userJson);
                this.isAuthenticated = true;
            } catch (e) {
                console.error('[AuthModule] Failed to parse stored user:', e);
                this._clearSession();
            }
        }
    }

    checkAuth() {
        return this.isAuthenticated && !!this.authToken && !!this.currentUser;
    }

    /**
     * Step 1: submit email + password. Returns OTP-required state or tenant selection or full login.
     */
    async login(email, password, rememberDevice = false) {
        try {
            const response = await fetch(`${this.apiBase}/auth/login`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'include', // send __medocs_dt cookie if present
                body: JSON.stringify({ email, password })
            });

            const data = await response.json();

            if (!response.ok) {
                throw new Error(data.message || 'Login failed');
            }

            // OTP required — show OTP entry screen
            if (data.RequiresOtpVerification) {
                this.pendingOtp = { email, rememberDevice };
                this._showOtpVerification(data.MaskedEmail || email);
                return { requiresOtpVerification: true, maskedEmail: data.MaskedEmail };
            }

            // Tenant selection required (trusted-device path — OTP was skipped)
            if (data.RequiresTenantSelection) {
                this.pendingTenantSelection = {
                    email,
                    userName: data.FullName || email,
                    tenants: data.AvailableTenants || [],
                    rememberDevice,
                    fromOtp: false
                };
                this._showTenantSelection(this.pendingTenantSelection.tenants, this.pendingTenantSelection.userName);
                return { requiresTenantSelection: true, tenants: data.AvailableTenants };
            }

            // Trusted-device direct login — session complete
            this._completeSession(data);
            return { success: true, user: this.currentUser };
        } catch (error) {
            console.error('[AuthModule] Login failed:', error);
            this._emit('auth:loginFailed', { error: error.message });
            throw error;
        }
    }

    /**
     * Step 2: submit OTP. Either logs in, or prompts tenant selection.
     */
    async verifyOtp(otpCode, tenantId = null) {
        if (!this.pendingOtp && !this.pendingTenantSelection) {
            throw new Error('No pending OTP to verify');
        }

        const email = (this.pendingOtp || this.pendingTenantSelection).email;
        const rememberDevice = (this.pendingOtp || this.pendingTenantSelection).rememberDevice;

        const response = await fetch(`${this.apiBase}/auth/verify-otp`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            credentials: 'include',
            body: JSON.stringify({ email, otpCode, tenantId, rememberDevice })
        });

        const data = await response.json();

        if (!response.ok) {
            throw new Error(data.message || 'Verification failed');
        }

        // Tenant selection — multi-clinic user
        if (data.RequiresTenantSelection) {
            this.pendingTenantSelection = {
                email,
                userName: data.FullName || email,
                tenants: data.AvailableTenants || [],
                rememberDevice,
                fromOtp: true
            };
            this.pendingOtp = null;
            this._hideOtpVerification();
            this._showTenantSelection(this.pendingTenantSelection.tenants, this.pendingTenantSelection.userName);
            return { requiresTenantSelection: true, tenants: data.AvailableTenants };
        }

        // Full login
        this._completeSession(data);
        return { success: true, user: this.currentUser };
    }

    async resendOtp() {
        if (!this.pendingOtp) return;
        try {
            await fetch(`${this.apiBase}/auth/resend-otp`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ email: this.pendingOtp.email })
            });
            this._showOtpSuccess('A new code has been sent.');
        } catch (e) {
            console.error('[AuthModule] Resend failed', e);
        }
    }

    async _handleLoginSubmit(e) {
        e.preventDefault();

        const email = document.getElementById('loginEmail')?.value;
        const password = document.getElementById('loginPassword')?.value;
        const rememberDevice = document.getElementById('loginRememberDevice')?.checked || false;

        if (!email || !password) {
            this._showError('Please enter email and password');
            return;
        }

        const submitBtn = this.loginForm.querySelector('[type="submit"]');
        const originalText = submitBtn?.innerHTML;

        try {
            if (submitBtn) {
                submitBtn.disabled = true;
                submitBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Signing in...';
            }

            await this.login(email, password, rememberDevice);
        } catch (error) {
            this._showError(error.message || 'Login failed');
        } finally {
            if (submitBtn) {
                submitBtn.disabled = false;
                submitBtn.innerHTML = originalText;
            }
        }
    }

    async _handleOtpSubmit(e) {
        e.preventDefault();

        const otpCode = document.getElementById('otpCode')?.value?.trim();
        if (!otpCode || otpCode.length < 6) {
            this._showOtpError('Please enter the 6-digit code');
            return;
        }

        const submitBtn = this.otpForm.querySelector('[type="submit"]');
        const originalText = submitBtn?.innerHTML;

        try {
            if (submitBtn) {
                submitBtn.disabled = true;
                submitBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Verifying...';
            }
            await this.verifyOtp(otpCode);
        } catch (error) {
            this._showOtpError(error.message || 'Invalid or expired code');
        } finally {
            if (submitBtn) {
                submitBtn.disabled = false;
                submitBtn.innerHTML = originalText;
            }
        }
    }

    async _handleOtpResend(e) {
        e.preventDefault();
        await this.resendOtp();
    }

    _handleOtpCancel(e) {
        e.preventDefault();
        this.pendingOtp = null;
        this.pendingTenantSelection = null;
        this._hideOtpVerification();
        this._showLoginForm();
    }

    _showOtpVerification(maskedEmail) {
        if (this.loginForm) this.loginForm.classList.add('d-none');
        const footerLinks = document.getElementById('loginFooterLinks');
        if (footerLinks) footerLinks.classList.add('d-none');
        if (this.tenantSelectionContainer) this.tenantSelectionContainer.classList.add('d-none');
        if (this.otpContainer) {
            this.otpContainer.classList.remove('d-none');
            const maskedEmailEl = document.getElementById('otpMaskedEmail');
            if (maskedEmailEl) maskedEmailEl.textContent = maskedEmail || '';
            const otpInput = document.getElementById('otpCode');
            if (otpInput) {
                otpInput.value = '';
                setTimeout(() => otpInput.focus(), 50);
            }
            const errEl = document.getElementById('otpError');
            const okEl = document.getElementById('otpSuccess');
            if (errEl) errEl.classList.add('d-none');
            if (okEl) okEl.classList.add('d-none');
        }
    }

    _hideOtpVerification() {
        if (this.otpContainer) this.otpContainer.classList.add('d-none');
    }

    _showLoginForm() {
        if (this.loginForm) this.loginForm.classList.remove('d-none');
        const footerLinks = document.getElementById('loginFooterLinks');
        if (footerLinks) footerLinks.classList.remove('d-none');
        if (this.loginPage) this.loginPage.classList.remove('d-none');
    }

    _showOtpError(message) {
        const errEl = document.getElementById('otpError');
        const okEl = document.getElementById('otpSuccess');
        if (okEl) okEl.classList.add('d-none');
        if (errEl) {
            errEl.textContent = message;
            errEl.classList.remove('d-none');
        }
    }

    _showOtpSuccess(message) {
        const errEl = document.getElementById('otpError');
        const okEl = document.getElementById('otpSuccess');
        if (errEl) errEl.classList.add('d-none');
        if (okEl) {
            okEl.textContent = message;
            okEl.classList.remove('d-none');
        }
    }

    _showTenantSelection(tenants, userName) {
        if (!this.tenantSelectionContainer) {
            console.warn('[AuthModule] Tenant selection container not found');
            return;
        }

        if (this.loginForm) this.loginForm.classList.add('d-none');
        if (this.otpContainer) this.otpContainer.classList.add('d-none');
        const footerLinks = document.getElementById('loginFooterLinks');
        if (footerLinks) footerLinks.classList.add('d-none');
        this.tenantSelectionContainer.classList.remove('d-none');

        const html = `
            <div class="card">
                <div class="card-header">
                    <h5 class="mb-0 text-primary"><i class="bi bi-building me-2"></i>Select Clinic</h5>
                </div>
                <div class="card-body">
                    <p>Welcome back, <strong>${this._escape(userName)}</strong>!</p>
                    <p class="text-muted">You have access to multiple clinics. Please select one to continue:</p>
                    <div class="list-group">
                        ${tenants.map(t => `
                            <button class="list-group-item list-group-item-action tenant-option"
                                    data-tenant-id="${t.TenantId}">
                                <div class="d-flex justify-content-between align-items-center">
                                    <div>
                                        <strong>${this._escape(t.Name)}</strong>
                                    </div>
                                    <i class="bi bi-chevron-right"></i>
                                </div>
                            </button>
                        `).join('')}
                    </div>
                    <button class="btn btn-link mt-3 cancel-tenant-selection">Cancel</button>
                </div>
            </div>
        `;

        this.tenantSelectionContainer.innerHTML = html;
    }

    async _handleTenantSelect(e) {
        const tenantBtn = e.target.closest('.tenant-option');
        const cancelBtn = e.target.closest('.cancel-tenant-selection');

        if (cancelBtn) {
            this.cancelTenantSelection();
            return;
        }

        if (!tenantBtn || !this.pendingTenantSelection) return;

        const tenantId = parseInt(tenantBtn.dataset.tenantId);

        try {
            if (this.pendingTenantSelection.fromOtp) {
                // Tenant selection after OTP — use verify-otp with TenantId (in "VERIFIED" state window)
                await this.verifyOtp('', tenantId);
            } else {
                // Trusted-device path — re-submit login with selected TenantId
                const response = await fetch(`${this.apiBase}/auth/login`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    credentials: 'include',
                    body: JSON.stringify({
                        email: this.pendingTenantSelection.email,
                        password: '',
                        tenantId
                    })
                });

                const data = await response.json();
                if (!response.ok) throw new Error(data.message || 'Tenant selection failed');
                this._completeSession(data);
            }
        } catch (error) {
            this._showError(error.message || 'Failed to select clinic');
        }
    }

    cancelTenantSelection() {
        this.pendingTenantSelection = null;
        this.pendingOtp = null;

        if (this.tenantSelectionContainer) {
            this.tenantSelectionContainer.classList.add('d-none');
            this.tenantSelectionContainer.innerHTML = '';
        }

        this._hideOtpVerification();
        this._showLoginForm();
    }

    /**
     * Complete the session from a successful login/verify response (token present).
     */
    _completeSession(data) {
        const token = data.Token;
        const user = {
            UserId: data.UserId,
            Email: data.Email,
            FullName: data.FullName,
            Role: data.Role,
            TenantId: data.TenantId,
            TenantName: data.TenantName,
            TenantSubdomain: data.TenantSubdomain,
            ProviderId: data.ProviderId,
            LocationId: data.LocationId,
            LocationName: data.LocationName,
            TimeZoneId: data.TimeZoneId,
            TimeZoneAbbreviation: data.TimeZoneAbbreviation
        };

        this._setSession(token, user);
        this._emit('auth:login', { user: this.currentUser });
    }

    _setSession(token, user) {
        this.authToken = token;
        this.currentUser = user;
        this.isAuthenticated = true;
        this.pendingTenantSelection = null;
        this.pendingOtp = null;

        localStorage.setItem('authToken', token);
        localStorage.setItem('currentUser', JSON.stringify(user));

        if (this.tenantSelectionContainer) this.tenantSelectionContainer.classList.add('d-none');
        if (this.otpContainer) this.otpContainer.classList.add('d-none');
        if (this.loginPage) this.loginPage.classList.add('d-none');
        if (this.appContainer) this.appContainer.classList.remove('d-none');
    }

    async logout() {
        try {
            if (this.authToken) {
                await fetch(`${this.apiBase}/auth/logout`, {
                    method: 'POST',
                    headers: {
                        'Authorization': `Bearer ${this.authToken}`,
                        'Content-Type': 'application/json'
                    }
                }).catch(() => {});
            }
        } finally {
            this._clearSession();
            this._emit('auth:logout');

            if (this.appContainer) this.appContainer.classList.add('d-none');
            if (this.loginPage) this.loginPage.classList.remove('d-none');
            this._showLoginForm();
        }
    }

    async _handleLogoutClick(e) {
        e.preventDefault();
        await this.logout();
    }

    _clearSession() {
        this.authToken = null;
        this.currentUser = null;
        this.isAuthenticated = false;
        this.pendingTenantSelection = null;
        this.pendingOtp = null;

        localStorage.removeItem('authToken');
        localStorage.removeItem('currentUser');
        localStorage.removeItem('currentLocation');
        sessionStorage.removeItem('resumePopupShown');
    }

    getCurrentUser() {
        return this.currentUser;
    }

    getToken() {
        return this.authToken;
    }

    hasRole(roles) {
        if (!this.currentUser) return false;
        const roleArray = Array.isArray(roles) ? roles : [roles];
        return roleArray.includes(this.currentUser.Role);
    }

    isSuperAdmin() {
        return this.currentUser?.Role === 0 && !this.currentUser?.TenantId;
    }

    isClinicAdmin() {
        return this.hasRole([0, 1]);
    }

    getRoleName(role) {
        const roleNames = ['Super Admin', 'Clinic Admin', 'Clinician', 'Front Desk', 'Biller', 'Read Only'];
        return roleNames[role] || 'User';
    }

    _escape(str) {
        if (str === null || str === undefined) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    }

    _showError(message) {
        if (window.Toast) {
            Toast.error('Error', message);
        } else {
            alert(message);
        }
    }

    _emit(event, data = {}) {
        if (this.eventBus) {
            this.eventBus.emit(event, data);
        }
    }

    destroy() {
        if (this.loginForm) this.loginForm.removeEventListener('submit', this._handleLoginSubmit);

        const logoutBtn = document.getElementById('logoutBtn');
        if (logoutBtn) logoutBtn.removeEventListener('click', this._handleLogoutClick);

        if (this.tenantSelectionContainer) this.tenantSelectionContainer.removeEventListener('click', this._handleTenantSelect);

        if (this.otpForm) this.otpForm.removeEventListener('submit', this._handleOtpSubmit);

        const resendBtn = document.getElementById('otpResendBtn');
        if (resendBtn) resendBtn.removeEventListener('click', this._handleOtpResend);

        const cancelBtn = document.getElementById('otpCancelBtn');
        if (cancelBtn) cancelBtn.removeEventListener('click', this._handleOtpCancel);

        this.isInitialized = false;
    }
}

// Export for module usage
window.AuthModule = AuthModule;
