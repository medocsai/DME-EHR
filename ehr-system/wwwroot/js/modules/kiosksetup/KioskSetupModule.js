/**
 * KioskSetupModule
 *
 * Runs the /KioskSetup page used by the Android kiosk tablet app (WebView):
 *   1. Admin signs in. Server sets a HttpOnly auth cookie.
 *   2. Admin picks a location — this page fetches the tenant's active
 *      locations (+ each location's kiosk token) using that cookie.
 *   3. Browser navigates to /Kiosk?token={kioskToken}&app=1. The kiosk page
 *      reuses the cookie when the admin signs out via password confirm.
 *
 * NOTE: No localStorage / sessionStorage is used. All state that needs to
 * persist across page navigations is held in the HttpOnly cookie issued by
 * the server. This keeps the WebView behavior identical to a normal browser.
 */
class KioskSetupModule {
    constructor() {
        this.api = '/api/kiosksetup';

        // In-memory state only — reset on every page load.
        this.user = null;
        this.locations = [];
        this.selectedLocationId = null;

        // OTP state
        this.pendingOtpEmail = null;
        this.resendCooldownTimer = null;
    }

    // ========================================================
    // Init
    // ========================================================

    async init() {
        this._bindEvents();

        // If the cookie already identifies us (e.g. admin refreshed the page
        // without logging out), skip straight to the location picker.
        const signedInUser = await this._fetchMeOrNull();
        if (signedInUser) {
            this.user = signedInUser;
            await this._enterLocationPicker();
            return;
        }

        this._showScreen('ksSignInScreen');
        setTimeout(() => document.getElementById('ksEmail')?.focus(), 50);
    }

    _bindEvents() {
        document.getElementById('ksSignInForm')
            ?.addEventListener('submit', (e) => this._handleSignIn(e));

        document.getElementById('ksContinueBtn')
            ?.addEventListener('click', () => this._handleContinue());

        document.getElementById('ksSignOutLink')
            ?.addEventListener('click', () => this._handleSignOut());

        document.getElementById('ksErrorRetryBtn')
            ?.addEventListener('click', () => this.init());

        document.getElementById('ksNoFormRetryBtn')
            ?.addEventListener('click', () => this._enterLocationPicker());

        document.getElementById('ksNoFormSignOutBtn')
            ?.addEventListener('click', () => this._handleSignOut());

        // OTP screen
        document.getElementById('ksOtpForm')
            ?.addEventListener('submit', (e) => this._handleOtpVerify(e));
        document.getElementById('ksOtpResendBtn')
            ?.addEventListener('click', () => this._handleOtpResend());
        document.getElementById('ksOtpBackBtn')
            ?.addEventListener('click', () => this._handleOtpBack());
        this._setupOtpInputs();

        document.querySelectorAll('[data-toggle-password]').forEach(btn => {
            btn.addEventListener('click', () => {
                const targetId = btn.getAttribute('data-toggle-password');
                const input = document.getElementById(targetId);
                if (!input) return;
                const icon = btn.querySelector('i');
                if (input.type === 'password') {
                    input.type = 'text';
                    icon?.classList.replace('bi-eye', 'bi-eye-slash');
                } else {
                    input.type = 'password';
                    icon?.classList.replace('bi-eye-slash', 'bi-eye');
                }
            });
        });
    }

    // ========================================================
    // Sign In
    // ========================================================

    async _handleSignIn(event) {
        event.preventDefault();

        const email = (document.getElementById('ksEmail')?.value || '').trim();
        const password = document.getElementById('ksPassword')?.value || '';

        if (!email || !password) {
            this._showSignInError('Enter your email and password to continue.');
            return;
        }

        this._hideSignInError();
        this._setButtonLoading('ksSignInBtn', true);
        this._showLoading('Signing in...');

        try {
            const response = await fetch(`${this.api}/sign-in`, {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ Email: email, Password: password })
            });

            const data = await this._safeJson(response);

            if (response.status === 401 || response.status === 400) {
                this._showSignInError(data?.message || 'Invalid email or password.');
                return;
            }

            if (!response.ok) {
                this._showSignInError(data?.message || 'Sign in failed. Please try again.');
                return;
            }

            if (data?.RequiresTenantSelection) {
                this._showSignInError(
                    data.Message ||
                    'This account belongs to multiple clinics. Please sign in through the web app and retry.'
                );
                return;
            }

            if (data?.RequiresOtpVerification) {
                this.pendingOtpEmail = data.Email || email;
                this._enterOtpScreen(data?.Message);
                return;
            }

            if (!data?.Success) {
                this._showSignInError(data?.Message || 'Sign in failed. Please try again.');
                return;
            }

            this.user = {
                userId: data.UserId,
                email: data.Email,
                fullName: data.FullName,
                role: data.Role,
                tenantId: data.TenantId,
                tenantName: data.TenantName
            };

            await this._enterLocationPicker();
        } catch (err) {
            console.error('[KioskSetup] Sign in error:', err);
            this._showSignInError('Unable to connect. Check your network and try again.');
        } finally {
            this._setButtonLoading('ksSignInBtn', false);
            this._hideLoading();
        }
    }

    // ========================================================
    // OTP Verification
    // ========================================================

    /** Wires auto-advance + paste handling for the 6 OTP boxes. */
    _setupOtpInputs() {
        const boxes = document.querySelectorAll('.ks-otp-box');
        if (!boxes.length) return;

        boxes.forEach((box, i) => {
            box.addEventListener('input', (e) => {
                // Only keep the first digit character
                const digit = (e.target.value || '').replace(/\D/g, '').slice(0, 1);
                e.target.value = digit;
                e.target.classList.toggle('filled', !!digit);
                if (digit && i < boxes.length - 1) {
                    boxes[i + 1].focus();
                }
                if (this._otpValue().length === boxes.length) {
                    // Auto-submit when all 6 digits are present
                    document.getElementById('ksOtpForm')?.requestSubmit();
                }
            });

            box.addEventListener('keydown', (e) => {
                if (e.key === 'Backspace' && !e.target.value && i > 0) {
                    boxes[i - 1].focus();
                    boxes[i - 1].value = '';
                    boxes[i - 1].classList.remove('filled');
                }
            });

            box.addEventListener('paste', (e) => {
                const pasted = (e.clipboardData || window.clipboardData).getData('text') || '';
                const digits = pasted.replace(/\D/g, '').slice(0, boxes.length);
                if (!digits) return;
                e.preventDefault();
                digits.split('').forEach((d, idx) => {
                    if (boxes[idx]) {
                        boxes[idx].value = d;
                        boxes[idx].classList.add('filled');
                    }
                });
                const nextEmpty = Array.from(boxes).find(b => !b.value);
                (nextEmpty || boxes[boxes.length - 1]).focus();
                if (digits.length === boxes.length) {
                    document.getElementById('ksOtpForm')?.requestSubmit();
                }
            });
        });
    }

    _otpValue() {
        return Array.from(document.querySelectorAll('.ks-otp-box'))
            .map(b => b.value || '')
            .join('');
    }

    _clearOtpInputs() {
        document.querySelectorAll('.ks-otp-box').forEach(b => {
            b.value = '';
            b.classList.remove('filled');
        });
    }

    _enterOtpScreen(infoMessage) {
        this._setElementText('ksOtpEmailDisplay', this.pendingOtpEmail || 'your email');
        this._clearOtpInputs();
        document.getElementById('ksOtpError')?.classList.add('hidden');
        this._showOtpInfo(infoMessage || 'A verification code has been sent to your email.');
        this._showScreen('ksOtpScreen');
        setTimeout(() => document.querySelector('.ks-otp-box')?.focus(), 100);
        this._startResendCooldown(60);
    }

    async _handleOtpVerify(event) {
        event.preventDefault();
        const code = this._otpValue();
        if (code.length !== 6) {
            this._showOtpError('Enter the 6-digit code from your email.');
            return;
        }
        if (!this.pendingOtpEmail) {
            // Should not happen — fall back to sign-in
            this._showScreen('ksSignInScreen');
            return;
        }

        this._hideOtpError();
        this._setButtonLoading('ksOtpVerifyBtn', true);
        this._showLoading('Verifying...');

        try {
            const response = await fetch(`${this.api}/verify-otp`, {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    Email: this.pendingOtpEmail,
                    OtpCode: code,
                    RememberDevice: false
                })
            });

            const data = await this._safeJson(response);

            if (response.status === 401 || response.status === 400) {
                this._showOtpError(data?.message || 'Invalid or expired verification code.');
                this._clearOtpInputs();
                document.querySelector('.ks-otp-box')?.focus();
                return;
            }

            if (!response.ok || !data?.Success) {
                this._showOtpError(data?.message || data?.Message || 'Verification failed. Please try again.');
                return;
            }

            this.user = {
                userId: data.UserId,
                email: data.Email,
                fullName: data.FullName,
                role: data.Role,
                tenantId: data.TenantId,
                tenantName: data.TenantName
            };
            this.pendingOtpEmail = null;

            await this._enterLocationPicker();
        } catch (err) {
            console.error('[KioskSetup] OTP verify error:', err);
            this._showOtpError('Unable to verify right now. Please try again.');
        } finally {
            this._setButtonLoading('ksOtpVerifyBtn', false);
            this._hideLoading();
        }
    }

    async _handleOtpResend() {
        if (!this.pendingOtpEmail) return;
        const btn = document.getElementById('ksOtpResendBtn');
        if (btn?.disabled) return;

        this._hideOtpError();
        try {
            const response = await fetch(`${this.api}/resend-otp`, {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ Email: this.pendingOtpEmail })
            });
            const data = await this._safeJson(response);
            this._showOtpInfo(data?.message || 'A new code has been sent to your email.');
        } catch (_) {
            this._showOtpError('Unable to resend right now. Please try again.');
        }
        this._clearOtpInputs();
        document.querySelector('.ks-otp-box')?.focus();
        this._startResendCooldown(60);
    }

    _handleOtpBack() {
        this.pendingOtpEmail = null;
        this._clearOtpInputs();
        if (this.resendCooldownTimer) {
            clearInterval(this.resendCooldownTimer);
            this.resendCooldownTimer = null;
        }
        const password = document.getElementById('ksPassword');
        if (password) password.value = '';
        this._showScreen('ksSignInScreen');
        setTimeout(() => document.getElementById('ksEmail')?.focus(), 50);
    }

    _startResendCooldown(seconds) {
        const btn = document.getElementById('ksOtpResendBtn');
        const label = document.getElementById('ksOtpResendLabel');
        if (!btn || !label) return;

        if (this.resendCooldownTimer) clearInterval(this.resendCooldownTimer);

        btn.disabled = true;
        let remaining = seconds;
        const tick = () => {
            if (remaining <= 0) {
                btn.disabled = false;
                label.textContent = 'Resend code';
                clearInterval(this.resendCooldownTimer);
                this.resendCooldownTimer = null;
                return;
            }
            label.textContent = `Resend code (${remaining}s)`;
            remaining--;
        };
        tick();
        this.resendCooldownTimer = setInterval(tick, 1000);
    }

    _showOtpError(message) {
        const el = document.getElementById('ksOtpError');
        this._setElementText('ksOtpErrorText', message);
        el?.classList.remove('hidden');
        document.getElementById('ksOtpInfo')?.classList.add('hidden');
    }

    _hideOtpError() {
        document.getElementById('ksOtpError')?.classList.add('hidden');
    }

    _showOtpInfo(message) {
        const el = document.getElementById('ksOtpInfo');
        this._setElementText('ksOtpInfoText', message);
        el?.classList.remove('hidden');
    }

    // ========================================================
    // Location Picker
    // ========================================================

    async _enterLocationPicker() {
        this._showLoading('Loading locations...');

        try {
            const response = await fetch(`${this.api}/locations`, {
                credentials: 'same-origin'
            });

            if (response.status === 401) {
                // Cookie expired / missing — send back to sign-in
                this.user = null;
                this._showScreen('ksSignInScreen');
                return;
            }

            if (!response.ok) {
                throw new Error('Failed to load locations');
            }

            this.locations = await response.json();
            this._renderUserChip();

            const usable = this.locations.filter(l => !!l.KioskToken && l.KioskEnabled !== false);
            const readyForKiosk = usable.filter(l => l.HasConsentForm !== false);

            // 0 usable locations with consent forms — show a specific error
            // screen instead of the ambiguous "no locations" empty state.
            if (usable.length === 0) {
                this._renderLocations();
                this._showScreen('ksLocationScreen');
                return;
            }

            if (readyForKiosk.length === 0) {
                this._showNoFormScreen();
                return;
            }

            // Exactly one usable location that's ready for the kiosk — skip
            // the picker and open the kiosk directly. Admin never has to
            // make a meaningless one-item choice.
            if (usable.length === 1 && readyForKiosk.length === 1) {
                this.selectedLocationId = readyForKiosk[0].LocationId;
                this._showLoading('Opening kiosk…');
                const token = readyForKiosk[0].KioskToken;
                window.location.href = `/Kiosk?token=${encodeURIComponent(token)}&app=1`;
                return;
            }

            this._renderLocations();
            this._showScreen('ksLocationScreen');
        } catch (err) {
            console.error('[KioskSetup] Load locations error:', err);
            this._showError(
                'Unable to load locations',
                'Please check your connection and try again.'
            );
        } finally {
            this._hideLoading();
        }
    }

    _renderUserChip() {
        const nameEl = document.getElementById('ksUserName');
        if (nameEl && this.user) {
            nameEl.textContent = this.user.fullName || this.user.email || 'Signed in';
        }
    }

    _renderLocations() {
        const list = document.getElementById('ksLocationList');
        const empty = document.getElementById('ksLocationEmpty');
        const continueBtn = document.getElementById('ksContinueBtn');
        if (!list) return;

        list.innerHTML = '';
        this.selectedLocationId = null;
        if (continueBtn) continueBtn.disabled = true;

        // Only locations that have a kiosk token + are enabled are shown. The
        // consent-form state is surfaced on each card so admin knows which
        // locations are ready; "no form" ones are still clickable so admin
        // gets a precise error instead of a generic "no locations" message.
        const usable = this.locations.filter(l => !!l.KioskToken && l.KioskEnabled !== false);

        if (usable.length === 0) {
            list.classList.add('hidden');
            empty?.classList.remove('hidden');
            return;
        }

        list.classList.remove('hidden');
        empty?.classList.add('hidden');

        usable.forEach(loc => {
            const hasForm = loc.HasConsentForm !== false;
            const item = document.createElement('button');
            item.type = 'button';
            item.className = 'ks-location-item' + (hasForm ? '' : ' ks-location-item--no-form');
            item.setAttribute('role', 'radio');
            item.setAttribute('aria-checked', 'false');
            item.dataset.locationId = loc.LocationId;
            item.dataset.hasForm = hasForm ? '1' : '0';

            const formBadge = hasForm
                ? ''
                : '<span class="ks-badge ks-badge-warn">No form</span>';

            item.innerHTML = `
                <span class="ks-location-icon">
                    <i class="bi bi-geo-alt-fill"></i>
                </span>
                <span class="ks-location-text">
                    <span class="ks-location-name">${this._escape(loc.Name)}${loc.IsPrimary ? ' <span class="ks-badge">Primary</span>' : ''}${formBadge}</span>
                    ${loc.Address ? `<span class="ks-location-address">${this._escape(loc.Address)}</span>` : ''}
                </span>
                <span class="ks-location-check">
                    <i class="bi bi-check-lg"></i>
                </span>
            `;

            item.addEventListener('click', () => this._selectLocation(loc.LocationId));
            list.appendChild(item);
        });
    }

    _selectLocation(locationId) {
        this.selectedLocationId = locationId;
        document.querySelectorAll('.ks-location-item').forEach(el => {
            const match = Number(el.dataset.locationId) === locationId;
            el.classList.toggle('selected', match);
            el.setAttribute('aria-checked', match ? 'true' : 'false');
        });
        const continueBtn = document.getElementById('ksContinueBtn');
        if (continueBtn) continueBtn.disabled = false;
    }

    _handleContinue() {
        if (!this.selectedLocationId) return;
        const loc = this.locations.find(l => l.LocationId === this.selectedLocationId);
        if (!loc?.KioskToken) {
            this._showLocationError('This location does not have a kiosk set up. Please contact your administrator.');
            return;
        }

        if (loc.HasConsentForm === false) {
            this._showLocationError(
                `No consent form is configured for "${loc.Name}". Please create one from the admin panel before using this kiosk.`
            );
            return;
        }

        // The HttpOnly auth cookie persists across this navigation, so the
        // kiosk page can still call /api/kiosksetup/confirm-password.
        const url = `/Kiosk?token=${encodeURIComponent(loc.KioskToken)}&app=1`;
        window.location.href = url;
    }

    async _handleSignOut() {
        try {
            await fetch(`${this.api}/sign-out`, {
                method: 'POST',
                credentials: 'same-origin'
            });
        } catch (_) { /* best-effort */ }

        this.user = null;
        this.locations = [];
        this.selectedLocationId = null;

        const email = document.getElementById('ksEmail');
        const password = document.getElementById('ksPassword');
        if (password) password.value = '';

        this._showScreen('ksSignInScreen');
        setTimeout(() => email?.focus(), 50);
    }

    // ========================================================
    // Small helpers
    // ========================================================

    /** Silently probe /me. Returns the user profile or null if not signed in. */
    async _fetchMeOrNull() {
        try {
            const r = await fetch(`${this.api}/me`, { credentials: 'same-origin' });
            if (r.status !== 200) return null;
            const data = await r.json();
            return {
                userId: data.UserId,
                email: data.Email,
                fullName: data.FullName,
                role: data.Role,
                tenantId: data.TenantId,
                tenantName: data.TenantName
            };
        } catch (_) {
            return null;
        }
    }

    // ========================================================
    // Screen management
    // ========================================================

    _showScreen(id) {
        document.querySelectorAll('.ks-screen').forEach(s => s.classList.remove('active'));
        document.getElementById(id)?.classList.add('active');
        window.scrollTo(0, 0);
    }

    _showLoading(message = 'Loading...') {
        const el = document.getElementById('ksLoading');
        const msg = document.getElementById('ksLoadingMessage');
        if (msg) msg.textContent = message;
        el?.classList.remove('hidden');
    }

    _hideLoading() {
        document.getElementById('ksLoading')?.classList.add('hidden');
    }

    _showError(title, message) {
        document.getElementById('ksErrorTitle').textContent = title;
        document.getElementById('ksErrorMessage').textContent = message;
        this._showScreen('ksErrorScreen');
    }

    /**
     * Dedicated empty-state for when no consent form template exists at all
     * (neither tenant-wide nor for any of the admin's locations).
     * Offers a Refresh button so admin can re-check after setting up a form.
     */
    _showNoFormScreen() {
        this._showScreen('ksNoFormScreen');
    }

    _showSignInError(message) {
        const el = document.getElementById('ksSignInError');
        document.getElementById('ksSignInErrorText').textContent = message;
        el?.classList.remove('hidden');
    }

    _hideSignInError() {
        document.getElementById('ksSignInError')?.classList.add('hidden');
    }

    _showLocationError(message) {
        const el = document.getElementById('ksLocationError');
        document.getElementById('ksLocationErrorText').textContent = message;
        el?.classList.remove('hidden');
        setTimeout(() => el?.classList.add('hidden'), 4000);
    }

    _setButtonLoading(id, loading) {
        const btn = document.getElementById(id);
        if (!btn) return;
        if (loading) {
            btn.disabled = true;
            btn.classList.add('is-loading');
        } else {
            btn.disabled = false;
            btn.classList.remove('is-loading');
        }
    }

    // ========================================================
    // Utilities
    // ========================================================

    async _safeJson(response) {
        try { return await response.json(); } catch (_) { return null; }
    }

    _setElementText(id, text) {
        const el = document.getElementById(id);
        if (el) el.textContent = text;
    }

    _escape(str) {
        if (str == null) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    }
}

document.addEventListener('DOMContentLoaded', () => {
    if (!document.getElementById('kioskSetupPage')) return;
    const app = new KioskSetupModule();
    window.kioskSetupModule = app;
    app.init();
});
