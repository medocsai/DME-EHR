/**
 * PatientPortalModule - Handles all patient portal frontend logic.
 * Self-initializing on DOMContentLoaded.
 * Manages: auth flow (DOB+SSN), data loading, rendering for all portal pages.
 */
(() => {
    'use strict';

    const API_BASE = '/api/portal';
    const AUTH_TOKEN_KEY = 'portalAuthToken';
    const PATIENT_KEY = 'portalPatient';

    // Appointment type names (must match server-side AppointmentType enum in Models/Enums/AllEnums.cs)
    const APPT_TYPES = {
        0: 'New Patient Visit',
        1: 'Follow Up Visit',
        2: 'Annual Physical',
        3: 'Wellness Exam',
        4: 'Consultation',
        5: 'Telehealth',
        6: 'Procedure Visit',
        7: 'Urgent Visit',
        8: 'Lab Review',
        9: 'Medication Review'
    };

    const APPT_STATUS = {
        0: 'Scheduled', 1: 'Confirmed', 2: 'Checked In', 3: 'In Progress',
        4: 'Completed', 5: 'No Show', 6: 'Cancelled', 7: 'Rescheduled', 8: 'Missed'
    };

    const APPT_STATUS_CLASS = {
        0: 'primary', 1: 'info', 2: 'warning', 3: 'warning',
        4: 'success', 5: 'danger', 6: 'secondary', 7: 'secondary', 8: 'danger'
    };

    const NOTE_TYPES = {
        0: 'H&P', 1: 'SOAP', 2: 'Office Visit', 3: 'Follow Up',
        4: 'Procedure', 5: 'Consultation', 6: 'Progress Note', 7: 'Other'
    };

    const MED_STATUS = { 0: 'Active', 1: 'Discontinued', 2: 'On Hold', 3: 'Completed' };
    const MED_STATUS_CLASS = { 0: 'success', 1: 'secondary', 2: 'warning', 3: 'info' };

    // Prescription status (match server Prescription model)
    const RX_STATUS = { 0: 'Draft', 1: 'Active', 2: 'Sent', 3: 'Filled', 4: 'Cancelled', 5: 'Expired' };
    const RX_STATUS_CLASS = { 0: 'secondary', 1: 'success', 2: 'info', 3: 'primary', 4: 'danger', 5: 'warning' };

    // Order types and statuses (Lab=0, Imaging=1 only shown in portal)
    const ORDER_TYPE = { 0: 'Lab', 1: 'Imaging' };
    const ORDER_TYPE_ICON = { 0: 'bi-droplet-half', 1: 'bi-camera' };
    const ORDER_STATUS = { 0: 'Draft', 1: 'Pending', 2: 'Sent', 3: 'In Progress', 4: 'Results Received', 5: 'Completed', 6: 'Cancelled' };
    const ORDER_STATUS_CLASS = { 0: 'secondary', 1: 'warning', 2: 'info', 3: 'primary', 4: 'success', 5: 'success', 6: 'danger' };

    const ALLERGY_SEVERITY = { 0: 'Mild', 1: 'Moderate', 2: 'Severe', 3: 'Life-Threatening' };
    const ALLERGY_SEVERITY_CLASS = { 0: 'info', 1: 'warning', 2: 'danger', 3: 'danger' };
    const ALLERGY_TYPES = { 0: 'Drug', 1: 'Food', 2: 'Environmental', 3: 'Other' };

    // State
    let authToken = null;
    let patientInfo = null;
    let currentPage = null;

    // Pending auth data (for multi-step flows)
    let pendingVerifyData = null;
    let pendingOtpAccountId = null;
    let pendingPortalCode = null;

    // ============================================
    // UTILITIES
    // ============================================

    function escape(str) {
        if (!str) return '';
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }

    function formatDate(dateStr, timeZoneId) {
        if (!dateStr) return '-';
        try {
            // DateOnly strings like "2026-04-11" must NOT be parsed with new Date() because
            // JS interprets them as midnight UTC, causing off-by-one in western timezones.
            // Detect date-only format and parse manually.
            const dateOnly = /^\d{4}-\d{2}-\d{2}$/.test(dateStr);
            if (dateOnly) {
                const [y, m, day] = dateStr.split('-').map(Number);
                const d = new Date(y, m - 1, day); // local date, no UTC shift
                return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
            }
            const d = new Date(dateStr);
            const opts = { month: 'short', day: 'numeric', year: 'numeric' };
            if (timeZoneId) opts.timeZone = timeZoneId;
            return d.toLocaleDateString('en-US', opts);
        } catch { return dateStr; }
    }

    function formatDateTime(dateStr, timeZoneId) {
        if (!dateStr) return '-';
        try {
            const d = new Date(dateStr);
            const opts = {
                month: 'short', day: 'numeric', year: 'numeric',
                hour: 'numeric', minute: '2-digit', hour12: true
            };
            if (timeZoneId) opts.timeZone = timeZoneId;
            return d.toLocaleDateString('en-US', opts);
        } catch { return dateStr; }
    }

    function formatTime(dateStr, timeZoneId) {
        if (!dateStr) return '-';
        try {
            const d = new Date(dateStr);
            const opts = { hour: 'numeric', minute: '2-digit', hour12: true };
            if (timeZoneId) opts.timeZone = timeZoneId;
            return d.toLocaleTimeString('en-US', opts);
        } catch { return dateStr; }
    }

    function showToast(title, message, type = 'info') {
        const toast = document.getElementById('portalToast');
        const titleEl = document.getElementById('portalToastTitle');
        const msgEl = document.getElementById('portalToastMessage');
        if (!toast || !titleEl || !msgEl) return;

        titleEl.textContent = title;
        msgEl.textContent = message;
        toast.className = `toast toast-${type === 'error' ? 'danger' : type}`;
        const bsToast = new bootstrap.Toast(toast, { autohide: true, delay: 5000 });
        bsToast.show();
    }

    // ============================================
    // API CALLS
    // ============================================

    async function apiRequest(endpoint, options = {}) {
        const { method = 'GET', body = null } = options;

        const headers = { 'Content-Type': 'application/json' };
        if (authToken) headers['Authorization'] = `Bearer ${authToken}`;

        const fetchOpts = { method, headers };
        if (body && method !== 'GET') fetchOpts.body = JSON.stringify(body);

        const response = await fetch(endpoint, fetchOpts);

        if (response.status === 401) {
            logout();
            return null;
        }

        if (!response.ok) {
            const text = await response.text();
            let msg = 'Request failed';
            try { msg = JSON.parse(text)?.message || text; } catch { msg = text; }
            throw new Error(msg);
        }

        if (response.status === 204) return null;
        const text = await response.text();
        return text ? JSON.parse(text) : null;
    }

    // ============================================
    // AUTH FLOW
    // ============================================

    function getStoredAuth() {
        const token = localStorage.getItem(AUTH_TOKEN_KEY);
        const patient = localStorage.getItem(PATIENT_KEY);
        if (token && patient) {
            try {
                authToken = token;
                patientInfo = JSON.parse(patient);
                return true;
            } catch { clearAuth(); }
        }
        return false;
    }

    function setAuth(token, patient) {
        authToken = token;
        patientInfo = patient;
        localStorage.setItem(AUTH_TOKEN_KEY, token);
        localStorage.setItem(PATIENT_KEY, JSON.stringify(patient));
    }

    function clearAuth() {
        authToken = null;
        patientInfo = null;
        localStorage.removeItem(AUTH_TOKEN_KEY);
        localStorage.removeItem(PATIENT_KEY);
    }

    function logout() {
        const portalCode = patientInfo?.PortalCode;
        clearAuth();
        window.location.href = portalCode ? `/Portal/${portalCode}` : '/Portal';
    }

    async function handleVerify(e) {
        e.preventDefault();
        const errorEl = document.getElementById('portalLoginError');
        const btn = document.getElementById('portalVerifyBtn');
        errorEl?.classList.add('d-none');

        const dob = document.getElementById('portalDob')?.value;
        const ssn4 = document.getElementById('portalSsn4')?.value;

        if (!dob || !ssn4 || ssn4.length !== 4) {
            errorEl.textContent = 'Please enter your date of birth and last 4 digits of SSN.';
            errorEl.classList.remove('d-none');
            return;
        }

        btn.disabled = true;
        btn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Verifying...';

        try {
            const result = await apiRequest(`${API_BASE}/auth/verify`, {
                method: 'POST',
                body: { DateOfBirth: dob, SsnLast4: ssn4 }
            });

            if (!result?.Success) {
                errorEl.textContent = result?.Message || 'Unable to verify your identity.';
                errorEl.classList.remove('d-none');
                return;
            }

            // Store verify data for multi-step flows
            pendingVerifyData = { dob, ssn4 };

            if (result.RequiresClinicSelection) {
                showClinicSelection(result);
            } else if (result.RequiresLocationSelection) {
                showLocationSelection(result);
            } else if (result.Token) {
                completeLogin(result);
            }
        } catch (err) {
            errorEl.textContent = err.message || 'An error occurred. Please try again.';
            errorEl.classList.remove('d-none');
        } finally {
            btn.disabled = false;
            btn.innerHTML = '<i class="bi bi-shield-check me-2"></i>Verify Identity';
        }
    }

    function showClinicSelection(result) {
        document.getElementById('portalVerifyForm')?.classList.add('d-none');
        const section = document.getElementById('portalClinicSelection');
        section?.classList.remove('d-none');

        document.getElementById('portalClinicUserName').textContent = result.PatientName || '';

        const list = document.getElementById('portalClinicList');
        list.innerHTML = (result.AvailableClinics || []).map(c => `
            <button class="btn btn-outline-primary text-start" onclick="window._portalSelectClinic(${c.PatientId}, ${c.TenantId})">
                <i class="bi bi-hospital me-2"></i>${escape(c.ClinicName)}
            </button>
        `).join('');
    }

    window._portalSelectClinic = async function(patientId, tenantId) {
        try {
            const result = await apiRequest(`${API_BASE}/auth/select-clinic`, {
                method: 'POST',
                body: { PatientId: patientId, TenantId: tenantId }
            });

            if (!result?.Success) {
                showToast('Error', result?.Message || 'Verification failed.', 'error');
                return;
            }

            if (result.RequiresLocationSelection) {
                showLocationSelection(result);
            } else if (result.Token) {
                completeLogin(result);
            }
        } catch (err) {
            showToast('Error', err.message, 'error');
        }
    };

    function showLocationSelection(result) {
        document.getElementById('portalVerifyForm')?.classList.add('d-none');
        document.getElementById('portalClinicSelection')?.classList.add('d-none');
        const section = document.getElementById('portalLocationSelection');
        section?.classList.remove('d-none');

        document.getElementById('portalLocationUserName').textContent = result.PatientName || '';

        const list = document.getElementById('portalLocationList');
        list.innerHTML = (result.AvailableLocations || []).map(l => `
            <button class="btn btn-outline-primary text-start"
                    onclick="window._portalSelectLocation(${result.PatientId}, ${result.TenantId}, ${l.LocationId})">
                <div><i class="bi bi-geo-alt me-2"></i><strong>${escape(l.Name)}</strong></div>
                ${l.Address ? `<small class="text-muted ms-4">${escape(l.Address)}</small>` : ''}
            </button>
        `).join('');
    }

    window._portalSelectLocation = async function(patientId, tenantId, locationId) {
        try {
            const result = await apiRequest(`${API_BASE}/auth/select-location`, {
                method: 'POST',
                body: { PatientId: patientId, TenantId: tenantId, LocationId: locationId }
            });

            if (!result?.Success) {
                showToast('Error', result?.Message || 'Verification failed.', 'error');
                return;
            }

            if (result.Token) {
                completeLogin(result);
            }
        } catch (err) {
            showToast('Error', err.message, 'error');
        }
    };

    function completeLogin(result) {
        setAuth(result.Token, {
            PatientId: result.PatientId,
            PatientName: result.PatientName,
            TenantId: result.TenantId,
            PortalCode: result.PortalCode || pendingPortalCode || null
        });
        // Check if redirected from copay email (?action=billing) or messaging email (?openChat=)
        const urlParams = new URLSearchParams(window.location.search);
        const action = urlParams.get('action');
        const openChat = urlParams.get('openChat');
        if (openChat) sessionStorage.setItem('portalOpenChat', openChat);
        window.location.href = action === 'billing' ? '/Portal/Billing' : '/Portal/Dashboard';
    }

    // ============================================
    // NEW AUTH: Email + Password + OTP (branded portal)
    // ============================================

    async function handleEmailLogin(e) {
        e.preventDefault();
        const errorEl = document.getElementById('portalLoginError');
        const successEl = document.getElementById('portalLoginSuccess');
        const btn = document.getElementById('portalLoginBtn');
        errorEl?.classList.add('d-none');
        successEl?.classList.add('d-none');

        const email = document.getElementById('portalEmail')?.value?.trim();
        const password = document.getElementById('portalPassword')?.value;
        const portalCode = window._portalCode || document.getElementById('portalCodeData')?.value;

        if (!email || !password) {
            errorEl.textContent = 'Please enter your email and password.';
            errorEl.classList.remove('d-none');
            return;
        }

        btn.disabled = true;
        btn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Signing in...';

        try {
            const result = await apiRequest(`${API_BASE}/auth/login`, {
                method: 'POST',
                body: { Email: email, Password: password, PortalCode: portalCode }
            });

            if (!result?.Success) {
                errorEl.textContent = result?.Message || 'Invalid email or password.';
                errorEl.classList.remove('d-none');
                return;
            }

            if (result.RequiresOtp) {
                // Show OTP step
                pendingOtpAccountId = result.AccountId;
                pendingPortalCode = portalCode;

                document.getElementById('portalEmailLoginForm')?.classList.add('d-none');
                document.getElementById('portalOtpSection')?.classList.remove('d-none');

                if (result.MaskedEmail) {
                    document.getElementById('otpSentMessage').textContent =
                        `A verification code has been sent to ${result.MaskedEmail}`;
                }

                // Focus OTP input
                setTimeout(() => document.getElementById('portalOtpCode')?.focus(), 100);
            } else if (result.Token) {
                completeLogin(result);
            }
        } catch (err) {
            errorEl.textContent = err.message || 'An error occurred. Please try again.';
            errorEl.classList.remove('d-none');
        } finally {
            btn.disabled = false;
            btn.innerHTML = '<i class="bi bi-box-arrow-in-right me-2"></i>Sign In';
        }
    }

    async function handleOtpVerify(e) {
        e.preventDefault();
        const errorEl = document.getElementById('portalOtpError');
        const btn = document.getElementById('portalVerifyOtpBtn');
        errorEl?.classList.add('d-none');

        const code = document.getElementById('portalOtpCode')?.value?.trim();
        if (!code || code.length !== 6) {
            errorEl.textContent = 'Please enter the 6-digit verification code.';
            errorEl.classList.remove('d-none');
            return;
        }

        btn.disabled = true;
        btn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Verifying...';

        try {
            const result = await apiRequest(`${API_BASE}/auth/verify-otp`, {
                method: 'POST',
                body: {
                    AccountId: pendingOtpAccountId,
                    Code: code,
                    PortalCode: pendingPortalCode
                }
            });

            if (!result?.Success) {
                errorEl.textContent = result?.Message || 'Invalid verification code.';
                errorEl.classList.remove('d-none');
                return;
            }

            if (result.Token) {
                completeLogin(result);
            }
        } catch (err) {
            errorEl.textContent = err.message || 'Verification failed. Please try again.';
            errorEl.classList.remove('d-none');
        } finally {
            btn.disabled = false;
            btn.innerHTML = '<i class="bi bi-shield-check me-2"></i>Verify Code';
        }
    }

    async function handleResendOtp() {
        const btn = document.getElementById('resendOtpBtn');
        btn.disabled = true;
        btn.textContent = 'Sending...';

        try {
            await apiRequest(`${API_BASE}/auth/resend-otp`, {
                method: 'POST',
                body: {
                    AccountId: pendingOtpAccountId,
                    PortalCode: pendingPortalCode
                }
            });
            btn.textContent = 'Sent!';
            setTimeout(() => { btn.textContent = 'Resend'; btn.disabled = false; }, 30000);
        } catch {
            btn.textContent = 'Resend';
            btn.disabled = false;
        }
    }

    async function handleForgotPassword(e) {
        e.preventDefault();
        const errorEl = document.getElementById('forgotPasswordError');
        const successEl = document.getElementById('forgotPasswordSuccess');
        const btn = document.getElementById('forgotPasswordBtn');
        errorEl?.classList.add('d-none');
        successEl?.classList.add('d-none');

        const email = document.getElementById('forgotEmail')?.value?.trim();
        if (!email) {
            errorEl.textContent = 'Please enter your email address.';
            errorEl.classList.remove('d-none');
            return;
        }

        btn.disabled = true;
        btn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Sending...';

        try {
            const portalCode = window._portalCode || document.getElementById('portalCodeData')?.value;
            await apiRequest(`${API_BASE}/auth/forgot-password`, {
                method: 'POST',
                body: { Email: email, PortalCode: portalCode }
            });

            successEl.textContent = 'If an account exists with that email, a password reset link has been sent.';
            successEl.classList.remove('d-none');
        } catch (err) {
            // Still show success for security (prevent email enumeration)
            successEl.textContent = 'If an account exists with that email, a password reset link has been sent.';
            successEl.classList.remove('d-none');
        } finally {
            btn.disabled = false;
            btn.innerHTML = '<i class="bi bi-envelope me-2"></i>Send Reset Link';
        }
    }

    function showBrandedLoginSection(sectionToShow) {
        const sections = ['portalEmailLoginForm', 'portalOtpSection', 'portalForgotPasswordSection'];
        sections.forEach(id => {
            const el = document.getElementById(id);
            if (el) el.classList.toggle('d-none', id !== sectionToShow);
        });
    }

    function initBrandedLoginPage() {
        // Pre-fill email from URL parameter (from copay reminder email link)
        const urlParams = new URLSearchParams(window.location.search);
        const prefillEmail = urlParams.get('email');
        if (prefillEmail) {
            const emailInput = document.getElementById('portalEmail');
            if (emailInput) {
                emailInput.value = prefillEmail;
                // Focus on password field since email is pre-filled
                setTimeout(() => document.getElementById('portalPassword')?.focus(), 300);
            }
        }

        // Email+Password login form
        document.getElementById('portalEmailLoginForm')?.addEventListener('submit', handleEmailLogin);

        // Toggle password visibility
        document.getElementById('togglePassword')?.addEventListener('click', function() {
            const input = document.getElementById('portalPassword');
            const icon = this.querySelector('i');
            if (input.type === 'password') {
                input.type = 'text';
                icon.className = 'bi bi-eye-slash';
            } else {
                input.type = 'password';
                icon.className = 'bi bi-eye';
            }
        });

        // OTP form
        document.getElementById('portalOtpForm')?.addEventListener('submit', handleOtpVerify);
        document.getElementById('resendOtpBtn')?.addEventListener('click', handleResendOtp);
        document.getElementById('backToLoginBtn')?.addEventListener('click', () => {
            showBrandedLoginSection('portalEmailLoginForm');
            document.getElementById('portalOtpCode').value = '';
        });

        // Forgot password
        document.getElementById('forgotPasswordLink')?.addEventListener('click', () => {
            showBrandedLoginSection('portalForgotPasswordSection');
            const loginEmail = document.getElementById('portalEmail')?.value;
            if (loginEmail) document.getElementById('forgotEmail').value = loginEmail;
        });
        document.getElementById('portalForgotPasswordForm')?.addEventListener('submit', handleForgotPassword);
        document.getElementById('backToLoginFromForgot')?.addEventListener('click', () => {
            showBrandedLoginSection('portalEmailLoginForm');
        });
    }

    // ============================================
    // PAGE RENDERERS
    // ============================================

    async function loadDashboard() {
        try {
            const data = await apiRequest(`${API_BASE}/dashboard`);
            if (!data) return;

            const welcomeEl = document.getElementById('dashboardWelcome');
            if (welcomeEl) welcomeEl.textContent = `Welcome, ${patientInfo?.PatientName || 'Patient'}`;

            // Welcome avatar — re-use the same data fetch the top-bar helper does.
            // Cheap because /profile is already cached by the browser by the time
            // the dashboard renders (top-bar fetched it on load).
            _renderDashboardWelcomeAvatar();

            document.getElementById('statUpcomingAppts').textContent = data.UpcomingAppointmentsCount ?? 0;
            document.getElementById('statNotes').textContent = data.RecentNotesCount ?? 0;
            document.getElementById('statMedications').textContent = data.ActiveMedicationsCount ?? 0;
            document.getElementById('statAllergies').textContent = data.ActiveAllergiesCount ?? 0;

            const nextApptEl = document.getElementById('nextAppointmentContent');
            if (data.NextAppointment) {
                const a = data.NextAppointment;
                nextApptEl.innerHTML = `
                    <div class="d-flex align-items-start gap-3">
                        <div class="portal-stat-icon bg-primary-subtle text-primary flex-shrink-0">
                            <i class="bi bi-calendar-event"></i>
                        </div>
                        <div class="flex-grow-1">
                            <h6 class="mb-1">${escape(a.DateFormatted || formatDate(a.StartTime, a.TimeZoneId))} at ${escape(a.StartTimeFormatted || formatTime(a.StartTime, a.TimeZoneId))}${!a.StartTimeFormatted && a.TimeZoneAbbreviation ? ` <span class="small text-muted">(${escape(a.TimeZoneAbbreviation)})</span>` : ''}</h6>
                            <p class="mb-1 text-muted">
                                <i class="bi bi-person me-1"></i>${escape(a.ProviderName || 'Provider')}
                            </p>
                            <p class="mb-1 small">
                                <span class="badge bg-${APPT_STATUS_CLASS[a.Status] || 'secondary'}">${APPT_STATUS[a.Status] || 'Scheduled'}</span>
                                <span class="ms-2">${escape(APPT_TYPES[a.Type] || 'Appointment')}</span>
                            </p>
                            ${a.Reason ? `<p class="mb-0 small text-muted">${escape(a.Reason)}</p>` : ''}
                            ${a.LocationName ? `<p class="mb-0 small text-muted"><i class="bi bi-geo-alt me-1"></i>${escape(a.LocationName)}</p>` : ''}
                        </div>
                    </div>`;
            } else {
                nextApptEl.innerHTML = '<p class="text-muted text-center mb-0 py-2">No upcoming appointments</p>';
            }

            // Consent nudge — surface count of appointments awaiting signature.
            // Best-effort: never blocks the dashboard load if endpoint errors.
            _refreshConsentNudge();
        } catch (err) {
            showToast('Error', 'Failed to load dashboard.', 'error');
        }
    }

    /**
     * Show the "Consent forms are awaiting your signature" nudge on the
     * dashboard and a badge count on the nav. Hidden when count is 0.
     * Safe to call from any page — only mutates elements that exist.
     */
    async function _refreshConsentNudge() {
        try {
            const items = await apiRequest('/api/portal/consent/awaiting');
            const count = Array.isArray(items) ? items.length : 0;

            // Dashboard alert
            const nudge = document.getElementById('dashboardConsentNudge');
            const nudgeText = document.getElementById('dashboardConsentNudgeText');
            if (nudge) {
                if (count > 0) {
                    if (nudgeText) {
                        nudgeText.textContent = count === 1
                            ? '1 consent form is awaiting your signature.'
                            : `${count} consent forms are awaiting your signature.`;
                    }
                    nudge.classList.remove('d-none');
                    nudge.classList.add('d-flex');
                } else {
                    nudge.classList.add('d-none');
                    nudge.classList.remove('d-flex');
                }
            }

            // Nav badge (visible on every portal page)
            const badge = document.getElementById('portalConsentBadge');
            if (badge) {
                if (count > 0) {
                    badge.textContent = String(count);
                    badge.classList.remove('d-none');
                } else {
                    badge.classList.add('d-none');
                }
            }
        } catch { /* non-blocking */ }
    }

    async function loadAppointments() {
        try {
            const data = await apiRequest(`${API_BASE}/appointments`);
            renderAppointments(data || [], 'upcoming');

            // Tab filtering
            document.querySelectorAll('#apptFilterTabs .nav-link').forEach(tab => {
                tab.addEventListener('click', () => {
                    document.querySelectorAll('#apptFilterTabs .nav-link').forEach(t => t.classList.remove('active'));
                    tab.classList.add('active');
                    renderAppointments(data || [], tab.dataset.filter);
                });
            });
        } catch (err) {
            document.getElementById('appointmentsList').innerHTML =
                '<div class="alert alert-danger">Failed to load appointments.</div>';
        }
    }

    function renderAppointments(appointments, filter) {
        const now = new Date();
        let filtered = appointments;

        if (filter === 'upcoming') {
            filtered = appointments.filter(a => new Date(a.StartTime) >= now && a.Status !== 6);
        } else if (filter === 'past') {
            filtered = appointments.filter(a => new Date(a.StartTime) < now || a.Status === 4);
        }

        const container = document.getElementById('appointmentsList');
        if (!filtered.length) {
            container.innerHTML = `<div class="text-center text-muted py-4">
                <i class="bi bi-calendar-x" style="font-size: 2rem;"></i>
                <p class="mt-2">No ${filter === 'all' ? '' : filter} appointments found.</p>
            </div>`;
            return;
        }

        container.innerHTML = filtered.map(a => {
            // Show provider avatar (photo if uploaded, initials otherwise) so
            // the patient sees who they're scheduled with at a glance. Falls
            // back gracefully if AvatarUtils not loaded.
            const providerAvatar = window.AvatarUtils ? AvatarUtils.renderProviderAvatar({
                providerId: a.ProviderId,
                name: a.ProviderName || 'Provider',
                hasProfilePicture: a.ProviderHasProfilePicture,
                size: 'sm',
                cssClass: 'me-2 align-middle d-inline-flex'
            }) : '';
            return `
            <div class="card portal-card mb-2">
                <div class="card-body py-2 px-3">
                    <div class="d-flex justify-content-between align-items-start">
                        <div>
                            <h6 class="mb-1">${escape(a.DateFormatted || formatDate(a.StartTime, a.TimeZoneId))} at ${escape(a.StartTimeFormatted || formatTime(a.StartTime, a.TimeZoneId))}${!a.StartTimeFormatted && a.TimeZoneAbbreviation ? ` <span class="small text-muted">(${escape(a.TimeZoneAbbreviation)})</span>` : ''}</h6>
                            <p class="mb-1 small text-muted d-flex align-items-center">
                                ${providerAvatar}<span>${escape(a.ProviderName || 'Provider')}</span>
                                ${a.LocationName ? ` <span class="ms-2"><i class="bi bi-geo-alt me-1"></i>${escape(a.LocationName)}</span>` : ''}
                            </p>
                            <p class="mb-0 small">${escape(APPT_TYPES[a.Type] || 'Appointment')}${a.IsTelehealth ? ' <span class="badge bg-info-subtle text-info ms-1">Telehealth</span>' : ''}
                                ${a.Reason ? ` - ${escape(a.Reason)}` : ''}</p>
                        </div>
                        <span class="badge bg-${APPT_STATUS_CLASS[a.Status] || 'secondary'} flex-shrink-0">
                            ${APPT_STATUS[a.Status] || 'Unknown'}
                        </span>
                    </div>
                </div>
            </div>
        `;}).join('');
    }

    async function loadVisits() {
        try {
            const data = await apiRequest(`${API_BASE}/visits`);
            const container = document.getElementById('visitsList');

            if (!data?.length) {
                container.innerHTML = `<div class="text-center text-muted py-4">
                    <i class="bi bi-clipboard2-x" style="font-size: 2rem;"></i>
                    <p class="mt-2">No visits to show yet.</p>
                </div>`;
                return;
            }

            container.innerHTML = data.map(v => {
                const providerAvatar = window.AvatarUtils ? AvatarUtils.renderProviderAvatar({
                    providerId: v.ProviderId,
                    name: v.ProviderName || 'Provider',
                    hasProfilePicture: v.ProviderHasProfilePicture,
                    size: 'sm',
                    cssClass: 'me-2 align-middle d-inline-flex'
                }) : '';
                return `
                <div class="card portal-card mb-2 portal-clickable" onclick="window._portalViewVisit(${v.EncounterId})">
                    <div class="card-body py-2 px-3">
                        <div class="d-flex justify-content-between align-items-start">
                            <div>
                                <h6 class="mb-1">${formatDate(v.EncounterDate)}</h6>
                                <p class="mb-1 small text-muted d-flex align-items-center">
                                    ${providerAvatar}<span>${escape(v.ProviderName || 'Provider')}</span>
                                </p>
                                <p class="mb-0 small">${escape(v.AppointmentType || 'Office Visit')}</p>
                            </div>
                            <i class="bi bi-chevron-right text-muted"></i>
                        </div>
                    </div>
                </div>
            `;}).join('');
        } catch (err) {
            document.getElementById('visitsList').innerHTML =
                '<div class="alert alert-danger">Failed to load visits.</div>';
        }
    }

    window._portalViewVisit = async function(encounterId) {
        const modal = new bootstrap.Modal(document.getElementById('visitSummaryModal'));
        const content = document.getElementById('visitSummaryContent');
        content.innerHTML = '<div class="text-center py-3"><div class="spinner-border spinner-border-sm"></div> Loading...</div>';
        modal.show();

        try {
            const visit = await apiRequest(`${API_BASE}/visits/${encounterId}/summary`);
            if (!visit) {
                content.innerHTML = '<div class="alert alert-warning">Visit not found.</div>';
                return;
            }

            const summaryBlock = visit.HasSummary
                ? `<div class="portal-visit-summary">${escape(visit.SummaryText).replace(/\n/g, '<br>')}</div>`
                : `<div class="text-center text-muted py-4">
                       <i class="bi bi-info-circle me-1"></i>
                       A summary is not available for this visit.
                       <div class="small text-muted mt-2">For a recently closed visit, please check back in a few minutes.</div>
                   </div>`;

            content.innerHTML = `
                <div class="portal-visit-meta mb-3">
                    <div class="row small">
                        <div class="col-6"><span class="text-muted">Date:</span> <strong>${formatDate(visit.EncounterDate)}</strong></div>
                        <div class="col-6"><span class="text-muted">Provider:</span> <strong>${escape(visit.ProviderName || '-')}</strong></div>
                        <div class="col-6 mt-1"><span class="text-muted">Visit Type:</span> <strong>${escape(visit.AppointmentType || 'Office Visit')}</strong></div>
                    </div>
                </div>
                <h6 class="text-muted text-uppercase small mb-2"><i class="bi bi-file-text me-1"></i>Summary of this visit</h6>
                ${summaryBlock}
            `;
        } catch (err) {
            content.innerHTML = '<div class="alert alert-danger">Failed to load visit summary.</div>';
        }
    };

    async function loadMedications() {
        try {
            const data = await apiRequest(`${API_BASE}/medications`);
            renderMedications(data || [], 'active');

            // Tab filtering
            document.querySelectorAll('.portal-filter-tabs .nav-link').forEach(tab => {
                tab.addEventListener('click', () => {
                    document.querySelectorAll('.portal-filter-tabs .nav-link').forEach(t => t.classList.remove('active'));
                    tab.classList.add('active');
                    renderMedications(data || [], tab.dataset.filter);
                });
            });
        } catch (err) {
            document.getElementById('medicationsList').innerHTML =
                '<div class="alert alert-danger">Failed to load medications.</div>';
        }
    }

    function renderMedications(medications, filter) {
        let filtered = medications;
        if (filter === 'active') {
            filtered = medications.filter(m => m.Status === 0);
        }

        const container = document.getElementById('medicationsList');
        if (!filtered.length) {
            container.innerHTML = `<div class="text-center text-muted py-4">
                <i class="bi bi-capsule" style="font-size: 2rem;"></i>
                <p class="mt-2">No ${filter === 'active' ? 'active ' : ''}medications found.</p>
            </div>`;
            return;
        }

        container.innerHTML = filtered.map(m => `
            <div class="card portal-card mb-2">
                <div class="card-body py-2 px-3">
                    <div class="d-flex justify-content-between align-items-start">
                        <div>
                            <h6 class="mb-1">${escape(m.DrugName || 'Unknown')}</h6>
                            <p class="mb-1 small">
                                ${m.Dosage ? `<strong>${escape(m.Dosage)}</strong>` : ''}
                                ${m.Frequency ? ` - ${escape(m.Frequency)}` : ''}
                                ${m.Route ? ` (${escape(m.Route)})` : ''}
                            </p>
                            <p class="mb-0 small text-muted">
                                ${m.PrescribedByProviderName ? `<i class="bi bi-person me-1"></i>${escape(m.PrescribedByProviderName)}` : ''}
                                ${m.StartDate ? ` | Started: ${formatDate(m.StartDate)}` : ''}
                            </p>
                        </div>
                        <span class="badge bg-${MED_STATUS_CLASS[m.Status] || 'secondary'} flex-shrink-0">
                            ${MED_STATUS[m.Status] || 'Unknown'}
                        </span>
                    </div>
                </div>
            </div>
        `).join('');
    }

    async function loadPrescriptions() {
        const container = document.getElementById('prescriptionsList');
        if (!container) return;
        try {
            const data = await apiRequest(`${API_BASE}/prescriptions`);
            if (!data?.length) {
                container.innerHTML = `<div class="text-center text-muted py-4">
                    <i class="bi bi-prescription2" style="font-size: 2rem;"></i>
                    <p class="mt-2">No prescriptions on file.</p>
                </div>`;
                return;
            }

            container.innerHTML = data.map(p => {
                const dir = [p.DoseAmount, p.DoseUnit, p.RouteName, p.FrequencyName].filter(Boolean).join(' ');
                const statusCls = RX_STATUS_CLASS[p.Status] ?? 'secondary';
                const statusName = p.StatusName || RX_STATUS[p.Status] || 'Unknown';
                return `
                <div class="card portal-card mb-2">
                    <div class="card-body py-2 px-3">
                        <div class="d-flex justify-content-between align-items-start">
                            <div class="flex-grow-1">
                                <h6 class="mb-1">${escape(p.DrugName || 'Unknown')}${p.Strength ? ` <span class="text-muted small">${escape(p.Strength)}</span>` : ''}</h6>
                                ${p.GenericName && p.GenericName !== p.DrugName ? `<p class="mb-1 small text-muted">Generic: ${escape(p.GenericName)}</p>` : ''}
                                ${dir ? `<p class="mb-1 small"><strong>${escape(dir)}</strong></p>` : ''}
                                ${p.DirectionsFreeText ? `<p class="mb-1 small">${escape(p.DirectionsFreeText)}</p>` : ''}
                                <p class="mb-0 small text-muted">
                                    ${p.ProviderName ? `<i class="bi bi-person me-1"></i>${escape(p.ProviderName)}` : ''}
                                    ${p.PrescribedDate ? ` | ${formatDate(p.PrescribedDate)}` : ''}
                                    ${p.Refills > 0 ? ` | Refills: ${p.Refills}` : ''}
                                </p>
                                ${p.PharmacyName ? `<p class="mb-0 small text-muted"><i class="bi bi-shop me-1"></i>${escape(p.PharmacyName)}</p>` : ''}
                            </div>
                            <span class="badge bg-${statusCls} flex-shrink-0 ms-2">${escape(statusName)}</span>
                        </div>
                    </div>
                </div>`;
            }).join('');
        } catch (err) {
            container.innerHTML = '<div class="alert alert-danger">Failed to load prescriptions.</div>';
        }
    }

    async function loadOrders() {
        const container = document.getElementById('ordersList');
        if (!container) return;
        try {
            const data = await apiRequest(`${API_BASE}/orders`);
            if (!data?.length) {
                container.innerHTML = `<div class="text-center text-muted py-4">
                    <i class="bi bi-clipboard2-pulse" style="font-size: 2rem;"></i>
                    <p class="mt-2">No orders on file.</p>
                </div>`;
                return;
            }

            container.innerHTML = data.map(o => {
                const typeName = o.OrderTypeName || ORDER_TYPE[o.OrderType] || 'Order';
                const icon = ORDER_TYPE_ICON[o.OrderType] || 'bi-clipboard2-pulse';
                const statusCls = ORDER_STATUS_CLASS[o.Status] ?? 'secondary';
                const statusName = o.StatusName || ORDER_STATUS[o.Status] || 'Unknown';

                // Show type-specific details
                let details = '';
                if (o.OrderType === 0) { // Lab
                    details = [o.LabPanelName, o.SpecimenType, o.FastingRequired ? 'Fasting required' : null]
                        .filter(Boolean).join(' • ');
                } else if (o.OrderType === 1) { // Imaging
                    details = [o.ModalityName, o.BodyPart, o.ContrastRequired ? 'With contrast' : null, o.ImagingFacility]
                        .filter(Boolean).join(' • ');
                }

                return `
                <div class="card portal-card mb-2">
                    <div class="card-body py-2 px-3">
                        <div class="d-flex justify-content-between align-items-start">
                            <div class="flex-grow-1">
                                <h6 class="mb-1"><i class="bi ${icon} me-1"></i>${escape(typeName)}${details ? `: ${escape(details)}` : ''}</h6>
                                ${o.ClinicalIndication ? `<p class="mb-1 small">${escape(o.ClinicalIndication)}</p>` : ''}
                                <p class="mb-0 small text-muted">
                                    ${o.ProviderName ? `<i class="bi bi-person me-1"></i>${escape(o.ProviderName)}` : ''}
                                    ${o.OrderDate ? ` | ${formatDate(o.OrderDate)}` : ''}
                                </p>
                                ${o.Notes ? `<p class="mb-0 small text-muted mt-1"><em>${escape(o.Notes)}</em></p>` : ''}
                            </div>
                            <span class="badge bg-${statusCls} flex-shrink-0 ms-2">${escape(statusName)}</span>
                        </div>
                    </div>
                </div>`;
            }).join('');
        } catch (err) {
            container.innerHTML = '<div class="alert alert-danger">Failed to load orders.</div>';
        }
    }

    async function loadAllergies() {
        try {
            const data = await apiRequest(`${API_BASE}/allergies`);
            const container = document.getElementById('allergiesList');

            if (!data?.length) {
                container.innerHTML = `<div class="text-center text-muted py-4">
                    <i class="bi bi-check-circle" style="font-size: 2rem; color: var(--bs-success);"></i>
                    <p class="mt-2">No Known Allergies (NKA)</p>
                </div>`;
                return;
            }

            container.innerHTML = data.map(a => `
                <div class="card portal-card mb-2 ${a.Severity >= 2 ? 'border-danger' : ''}">
                    <div class="card-body py-2 px-3">
                        <div class="d-flex justify-content-between align-items-start">
                            <div>
                                <h6 class="mb-1">
                                    ${a.Severity >= 2 ? '<i class="bi bi-exclamation-triangle-fill text-danger me-1"></i>' : ''}
                                    ${escape(a.AllergenName || 'Unknown')}
                                </h6>
                                <p class="mb-1 small">
                                    <span class="badge bg-light text-dark">${escape(ALLERGY_TYPES[a.Type] || 'Other')}</span>
                                    ${a.Reaction ? ` <span class="ms-1">Reaction: ${escape(a.Reaction)}</span>` : ''}
                                </p>
                                ${a.OnsetDate ? `<p class="mb-0 small text-muted">Onset: ${formatDate(a.OnsetDate)}</p>` : ''}
                            </div>
                            <span class="badge bg-${ALLERGY_SEVERITY_CLASS[a.Severity] || 'secondary'} flex-shrink-0">
                                ${ALLERGY_SEVERITY[a.Severity] || 'Unknown'}
                            </span>
                        </div>
                    </div>
                </div>
            `).join('');
        } catch (err) {
            document.getElementById('allergiesList').innerHTML =
                '<div class="alert alert-danger">Failed to load allergies.</div>';
        }
    }

    async function loadVitals() {
        try {
            const data = await apiRequest(`${API_BASE}/vitals`);
            const container = document.getElementById('vitalsContainer');

            if (!data?.length) {
                container.innerHTML = `<div class="text-center text-muted py-4">
                    <i class="bi bi-activity" style="font-size: 2rem;"></i>
                    <p class="mt-2">No vitals recorded yet.</p>
                </div>`;
                return;
            }

            container.innerHTML = `
                <table class="table table-sm table-hover mb-0 portal-vitals-table">
                    <thead>
                        <tr>
                            <th>Date</th>
                            <th>BP</th>
                            <th>HR</th>
                            <th class="d-none d-md-table-cell">Temp</th>
                            <th class="d-none d-md-table-cell">SpO2</th>
                            <th class="d-none d-lg-table-cell">Weight</th>
                            <th class="d-none d-lg-table-cell">BMI</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${data.map(v => `
                            <tr>
                                <td>${formatDate(v.RecordedAt)}</td>
                                <td>${v.BloodPressure || (v.SystolicBp && v.DiastolicBp ? `${v.SystolicBp}/${v.DiastolicBp}` : '-')}</td>
                                <td>${v.HeartRate ?? '-'}</td>
                                <td class="d-none d-md-table-cell">${v.Temperature ? `${v.Temperature}°` : '-'}</td>
                                <td class="d-none d-md-table-cell">${v.SpO2 ? `${v.SpO2}%` : '-'}</td>
                                <td class="d-none d-lg-table-cell">${v.Weight ? `${v.Weight} lbs` : '-'}</td>
                                <td class="d-none d-lg-table-cell">${v.Bmi ? parseFloat(v.Bmi).toFixed(1) : '-'}</td>
                            </tr>
                        `).join('')}
                    </tbody>
                </table>
            `;
        } catch (err) {
            document.getElementById('vitalsContainer').innerHTML =
                '<div class="alert alert-danger">Failed to load vitals.</div>';
        }
    }

    /**
     * Render the welcome avatar on the portal Dashboard at 52px (lg). Same
     * data source as the top-bar avatar (/profile endpoint); reuses the
     * already-cached response when available.
     */
    async function _renderDashboardWelcomeAvatar() {
        const host = document.getElementById('dashboardWelcomeAvatar');
        if (!host || !window.AvatarUtils) return;
        try {
            const profile = await apiRequest(`${API_BASE}/profile`);
            if (!profile) return;
            const hasPhoto = !!(profile.HasProfilePicture || profile.hasProfilePicture);
            const name = profile.FullName
                || `${profile.FirstName || ''} ${profile.LastName || ''}`.trim()
                || 'Patient';
            const html = AvatarUtils.renderPatientAvatar({
                patientId: profile.PatientId,
                name: name,
                hasProfilePicture: hasPhoto,
                size: 'lg'
            });
            host.innerHTML = hasPhoto
                ? html.replace('/profile-picture"', `/profile-picture?v=${Date.now()}"`)
                : html;
        } catch { /* silent */ }
    }

    /**
     * Populate the patient's avatar in the portal top bar. Fetches the profile
     * once (cheap — already loaded for many other things) and renders an avatar
     * via AvatarUtils. Cache-busted on each render so a freshly-uploaded photo
     * appears immediately when the user navigates away and back.
     */
    async function _populatePortalHeaderAvatar(patientInfo) {
        const host = document.getElementById('portalUserAvatar');
        if (!host || !window.AvatarUtils) return;
        try {
            const profile = await apiRequest(`${API_BASE}/profile`);
            if (!profile) return;
            const hasPhoto = !!(profile.HasProfilePicture || profile.hasProfilePicture);
            const name = profile.FullName
                || `${profile.FirstName || ''} ${profile.LastName || ''}`.trim()
                || patientInfo?.PatientName
                || 'Patient';
            const html = AvatarUtils.renderPatientAvatar({
                patientId: profile.PatientId,
                name: name,
                hasProfilePicture: hasPhoto,
                size: 'sm'
            });
            host.innerHTML = hasPhoto
                ? html.replace('/profile-picture"', `/profile-picture?v=${Date.now()}"`)
                : html;
        } catch { /* silent — header just stays empty */ }
    }

    /**
     * Render / refresh the patient's profile picture avatar + button labels.
     * Called on profile load and after upload/remove so the section stays in sync.
     * Note: this is the ONLY editable field on the portal profile page.
     */
    function _renderPortalProfilePicture(patient) {
        const avatarHost = document.getElementById('portalProfilePictureAvatar');
        const changeLabel = document.getElementById('portalProfilePictureChangeLabel');
        const removeBtn = document.getElementById('portalProfilePictureRemoveBtn');
        if (!avatarHost) return;

        const hasPhoto = !!(patient && (patient.HasProfilePicture || patient.hasProfilePicture));
        const name = patient?.FullName || `${patient?.FirstName || ''} ${patient?.LastName || ''}`.trim() || 'Patient';

        // Reuse AvatarUtils so the look matches everywhere else in the app.
        // Cache-bust on each render so the new picture appears immediately after upload.
        if (window.AvatarUtils && patient?.PatientId) {
            const html = AvatarUtils.renderPatientAvatar({
                patientId: patient.PatientId,
                name: name,
                hasProfilePicture: hasPhoto,
                size: 'xl'
            });
            // Tack on a cache-buster ?v= so the IMG re-fetches after a fresh upload
            avatarHost.innerHTML = hasPhoto
                ? html.replace('/profile-picture"', `/profile-picture?v=${Date.now()}"`)
                : html;
        }

        if (changeLabel) changeLabel.textContent = hasPhoto ? 'Change Photo' : 'Add Photo';
        if (removeBtn) removeBtn.classList.toggle('d-none', !hasPhoto);
    }

    /**
     * One-time wiring for the profile-picture upload/delete buttons.
     * Idempotent — re-running attaches once. Lives at module-scope so the
     * load+reload cycle on the profile page works without leaking listeners.
     */
    let _portalProfilePictureWired = false;
    function _wirePortalProfilePictureHandlers() {
        if (_portalProfilePictureWired) return;
        _portalProfilePictureWired = true;

        const fileInput = document.getElementById('portalProfilePictureInput');
        const changeBtn = document.getElementById('portalProfilePictureChangeBtn');
        const removeBtn = document.getElementById('portalProfilePictureRemoveBtn');

        if (changeBtn && fileInput) {
            changeBtn.addEventListener('click', () => fileInput.click());
        }

        if (fileInput) {
            fileInput.addEventListener('change', async () => {
                const file = fileInput.files?.[0];
                if (!file) return;
                // Client-side validation — match server-side limits in ProfilePictureService.
                if (file.size > 5 * 1024 * 1024) {
                    alert('File size exceeds 5MB limit');
                    fileInput.value = '';
                    return;
                }
                const allowed = ['image/jpeg', 'image/png', 'image/webp'];
                if (!allowed.includes(file.type)) {
                    alert('Only JPG, PNG, and WebP images are allowed');
                    fileInput.value = '';
                    return;
                }
                const fd = new FormData();
                fd.append('file', file);
                try {
                    // IMPORTANT: portal JWT lives under AUTH_TOKEN_KEY ('portalAuthToken'),
                    // NOT the clinic-side 'authToken'. Use the same key apiRequest() uses
                    // so the upload travels authenticated.
                    const token = authToken || localStorage.getItem(AUTH_TOKEN_KEY) || '';
                    const resp = await fetch('/api/portal/me/profile-picture', {
                        method: 'POST',
                        headers: { 'Authorization': `Bearer ${token}` },
                        body: fd
                    });
                    if (!resp.ok) {
                        const err = await resp.json().catch(() => ({}));
                        alert(err.message || 'Upload failed');
                        return;
                    }
                    // Re-fetch profile so HasProfilePicture and the avatar refresh
                    const fresh = await apiRequest(`${API_BASE}/profile`);
                    if (fresh) {
                        _renderPortalProfilePicture(fresh);
                        _populatePortalHeaderAvatar(fresh); // keep top-bar avatar in sync
                        _renderDashboardWelcomeAvatar();    // and the dashboard welcome avatar
                    }
                } catch (e) {
                    console.error('[Portal] Profile picture upload failed:', e);
                    alert('Upload failed. Please try again.');
                } finally {
                    fileInput.value = '';
                }
            });
        }

        if (removeBtn) {
            removeBtn.addEventListener('click', async () => {
                if (!confirm('Remove your profile picture?')) return;
                try {
                    const token = authToken || localStorage.getItem(AUTH_TOKEN_KEY) || '';
                    const resp = await fetch('/api/portal/me/profile-picture', {
                        method: 'DELETE',
                        headers: { 'Authorization': `Bearer ${token}` }
                    });
                    if (!resp.ok) {
                        alert('Failed to remove profile picture');
                        return;
                    }
                    const fresh = await apiRequest(`${API_BASE}/profile`);
                    if (fresh) {
                        _renderPortalProfilePicture(fresh);
                        _populatePortalHeaderAvatar(fresh); // keep top-bar avatar in sync
                        _renderDashboardWelcomeAvatar();    // and the dashboard welcome avatar
                    }
                } catch (e) {
                    console.error('[Portal] Profile picture delete failed:', e);
                    alert('Remove failed. Please try again.');
                }
            });
        }
    }

    async function loadProfile() {
        try {
            const data = await apiRequest(`${API_BASE}/profile`);
            const container = document.getElementById('profileContent');

            if (!data) {
                container.innerHTML = '<div class="alert alert-warning">Profile not found.</div>';
                return;
            }

            // Profile picture section (editable). Wire handlers once, then render.
            _wirePortalProfilePictureHandlers();
            _renderPortalProfilePicture(data);

            container.innerHTML = `
                <div class="row g-3">
                    <!-- Personal Info -->
                    <div class="col-12 col-md-6">
                        <div class="card portal-card h-100">
                            <div class="card-header"><h6 class="mb-0"><i class="bi bi-person me-2"></i>Personal Information</h6></div>
                            <div class="card-body">
                                <div class="portal-profile-field">
                                    <label>Full Name</label>
                                    <span>${escape(data.FullName || data.FirstName + ' ' + data.LastName || '-')}</span>
                                </div>
                                <div class="portal-profile-field">
                                    <label>Date of Birth</label>
                                    <span>${formatDate(data.DateOfBirth)}</span>
                                </div>
                                <div class="portal-profile-field">
                                    <label>Gender</label>
                                    <span>${escape(data.Gender || '-')}</span>
                                </div>
                                <div class="portal-profile-field">
                                    <label>MRN</label>
                                    <span>${escape(data.MRN || data.Mrn || '-')}</span>
                                </div>
                            </div>
                        </div>
                    </div>

                    <!-- Contact Info -->
                    <div class="col-12 col-md-6">
                        <div class="card portal-card h-100">
                            <div class="card-header"><h6 class="mb-0"><i class="bi bi-telephone me-2"></i>Contact Information</h6></div>
                            <div class="card-body">
                                <div class="portal-profile-field">
                                    <label>Phone</label>
                                    <span>${escape(data.Phone || '-')}</span>
                                </div>
                                <div class="portal-profile-field">
                                    <label>Email</label>
                                    <span>${escape(data.Email || '-')}</span>
                                </div>
                                <div class="portal-profile-field">
                                    <label>Address</label>
                                    <span>${escape([data.Address, data.City, data.State, data.ZipCode].filter(Boolean).join(', ') || '-')}</span>
                                </div>
                            </div>
                        </div>
                    </div>

                    <!-- Emergency Contact -->
                    <div class="col-12 col-md-6">
                        <div class="card portal-card h-100">
                            <div class="card-header"><h6 class="mb-0"><i class="bi bi-telephone-plus me-2"></i>Emergency Contact</h6></div>
                            <div class="card-body">
                                <div class="portal-profile-field">
                                    <label>Name</label>
                                    <span>${escape(data.EmergencyContactName || '-')}</span>
                                </div>
                                <div class="portal-profile-field">
                                    <label>Phone</label>
                                    <span>${escape(data.EmergencyContactPhone || '-')}</span>
                                </div>
                                <div class="portal-profile-field">
                                    <label>Relationship</label>
                                    <span>${escape(data.EmergencyContactRelation || '-')}</span>
                                </div>
                            </div>
                        </div>
                    </div>

                    <!-- Insurance -->
                    <div class="col-12 col-md-6">
                        <div class="card portal-card h-100">
                            <div class="card-header"><h6 class="mb-0"><i class="bi bi-shield-check me-2"></i>Insurance</h6></div>
                            <div class="card-body" id="profileInsurance">
                                ${renderInsurance(data.Insurances || data.insurances)}
                            </div>
                        </div>
                    </div>
                </div>
            `;
        } catch (err) {
            document.getElementById('profileContent').innerHTML =
                '<div class="alert alert-danger">Failed to load profile.</div>';
        }
    }

    function renderInsurance(insurances) {
        if (!insurances?.length) return '<p class="text-muted mb-0">No insurance on file.</p>';

        return insurances.map(ins => `
            <div class="portal-profile-field">
                <label>${ins.Type === 0 ? 'Primary' : ins.Type === 1 ? 'Secondary' : 'Other'}</label>
                <span>${escape(ins.PayerName || '-')}</span>
            </div>
            ${ins.PolicyNumber ? `<div class="portal-profile-field"><label>Policy #</label><span>${escape(ins.PolicyNumber)}</span></div>` : ''}
            ${ins.GroupNumber ? `<div class="portal-profile-field"><label>Group #</label><span>${escape(ins.GroupNumber)}</span></div>` : ''}
        `).join('<hr class="my-2">');
    }

    // ============================================
    // INITIALIZATION
    // ============================================

    function detectPage() {
        const path = window.location.pathname.toLowerCase();
        if (path.includes('/portal/dashboard')) return 'dashboard';
        if (path.includes('/portal/booking')) return 'booking';
        if (path.includes('/portal/appointments')) return 'appointments';
        if (path.includes('/portal/visits')) return 'visits';
        if (path.includes('/portal/medications')) return 'medications';
        if (path.includes('/portal/prescriptions')) return 'prescriptions';
        if (path.includes('/portal/orders')) return 'orders';
        if (path.includes('/portal/allergies')) return 'allergies';
        if (path.includes('/portal/vitals')) return 'vitals';
        if (path.includes('/portal/documents')) return 'documents';
        if (path.includes('/portal/billing')) return 'billing';
        if (path.includes('/portal/profile')) return 'profile';
        if (path.includes('/portal/consent')) return 'consent';
        if (path.includes('/portal/intake')) return 'intake';
        if (path.includes('/portal/register')) return 'register';
        if (path.includes('/portal/resetpassword')) return 'resetpassword';
        if (path === '/portal' || path === '/portal/' || path === '/portal/login') return 'login';
        // /portal/{portalCode} - branded login
        if (document.getElementById('portalCodeData')) return 'branded-login';
        return 'login';
    }

    function highlightNav(page) {
        document.querySelectorAll('.portal-nav-link, .portal-mobile-link').forEach(link => {
            link.classList.toggle('active', link.dataset.page === page);
        });
    }

    async function init() {
        currentPage = detectPage();
        const loading = document.getElementById('portalLoading');
        const loginSection = document.getElementById('portalLogin');
        const appSection = document.getElementById('portalApp');

        // Branded login page (/portal/{code}) — email+password+OTP flow
        if (currentPage === 'branded-login') {
            if (getStoredAuth()) {
                window.location.href = '/Portal/Dashboard';
                return;
            }
            loading?.classList.add('d-none');
            loginSection?.classList.remove('d-none');
            initBrandedLoginPage();
            return;
        }

        // Registration and reset pages are self-contained via inline scripts
        if (currentPage === 'register' || currentPage === 'resetpassword') {
            loading?.classList.add('d-none');
            loginSection?.classList.remove('d-none');
            return;
        }

        if (currentPage === 'login') {
            // Legacy login page — check if already authenticated
            if (getStoredAuth()) {
                window.location.href = '/Portal/Dashboard';
                return;
            }
            loading?.classList.add('d-none');
            loginSection?.classList.remove('d-none');

            // Bind login form
            document.getElementById('portalVerifyForm')?.addEventListener('submit', handleVerify);

            // Back buttons
            document.getElementById('portalBackToVerify')?.addEventListener('click', () => {
                document.getElementById('portalClinicSelection')?.classList.add('d-none');
                document.getElementById('portalVerifyForm')?.classList.remove('d-none');
            });
            document.getElementById('portalBackToClinic')?.addEventListener('click', () => {
                document.getElementById('portalLocationSelection')?.classList.add('d-none');
                document.getElementById('portalClinicSelection')?.classList.remove('d-none');
            });

            return;
        }

        // Authenticated pages — check auth
        if (!getStoredAuth()) {
            const portalCode = patientInfo?.PortalCode;
            window.location.href = portalCode ? `/Portal/${portalCode}` : '/Portal';
            return;
        }

        // Show app
        loading?.classList.add('d-none');
        appSection?.classList.remove('d-none');

        // Set user name + avatar in the top bar. The patient's HasProfilePicture
        // and PatientId aren't on the auth payload, so we fetch them once from
        // the profile endpoint after login. Failure falls back to plain initials.
        const nameEl = document.getElementById('portalUserName');
        if (nameEl) nameEl.textContent = patientInfo?.PatientName || 'Patient';
        _populatePortalHeaderAvatar(patientInfo);

        // Highlight active nav
        highlightNav(currentPage);

        // Consent nav-badge refresh — runs on every portal page so the badge
        // stays current regardless of where the patient is.
        _refreshConsentNudge();

        // Bind logout
        document.getElementById('portalLogoutBtn')?.addEventListener('click', logout);

        // Mobile menu toggle
        document.getElementById('portalMenuToggle')?.addEventListener('click', () => {
            document.getElementById('portalMobileNav')?.classList.toggle('d-none');
        });

        // Load page data
        switch (currentPage) {
            case 'dashboard': await loadDashboard(); break;
            case 'booking': await loadBookingPage(); break;
            case 'appointments': await loadAppointments(); break;
            case 'visits': await loadVisits(); break;
            case 'medications': await loadMedications(); break;
            // COMING SOON: replace the redirect with the original line below to restore the page
            // case 'prescriptions': await loadPrescriptions(); break;
            case 'prescriptions': window.location.replace('/Portal/Home'); break;
            case 'orders': await loadOrders(); break;
            case 'allergies': await loadAllergies(); break;
            case 'vitals': await loadVitals(); break;
            case 'documents': break; // Documents page handles its own JS inline
            case 'consent': break;   // PortalConsentModule.js handles this page
            case 'billing': await loadBilling(); break;
            case 'profile': await loadProfile(); break;
        }
    }

    // ============================================
    // BILLING PAGE
    // ============================================

    let stripeInstance = null;
    let stripeElements = null;
    let stripePaymentElement = null;
    let currentPaymentClientSecret = null;
    let selectedInstallmentMonths = null;

    async function loadBilling() {
        try {
            const [balanceResp, transResp, planResp] = await Promise.all([
                apiRequest('/api/portal/billing/balance'),
                apiRequest('/api/portal/billing/transactions'),
                apiRequest('/api/portal/billing/installment-plan')
            ]);

            // Render balance
            if (balanceResp) renderBillingBalance(balanceResp);

            // Render active plan
            if (planResp) renderActivePlan(planResp);

            // Render transactions
            renderTransactions(transResp || []);

            // Wire up buttons
            document.getElementById('portalPayNowBtn')?.addEventListener('click', () => {
                const balance = balanceResp?.CurrentBalance || 0;
                if (balance <= 0) { showToast('Info', 'No balance due.', 'info'); return; }
                window.location.href = `/Portal/Payment?amount=${balance}`;
            });
            document.getElementById('portalPaymentPlanBtn')?.addEventListener('click', () => {
                const balance = balanceResp?.CurrentBalance || 0;
                if (balance <= 0) { showToast('Info', 'No balance due.', 'info'); return; }
                window.location.href = `/Portal/InstallmentSetup?amount=${balance}`;
            });
            document.getElementById('portalCancelPayment')?.addEventListener('click', hideStripePaymentForm);
            document.getElementById('portalCancelInstallment')?.addEventListener('click', hideInstallmentSetup);
            document.getElementById('portalConfirmPayment')?.addEventListener('click', confirmStripePayment);

            // Installment option buttons
            document.querySelectorAll('.installment-option').forEach(btn => {
                btn.addEventListener('click', function () {
                    document.querySelectorAll('.installment-option').forEach(b => b.classList.remove('active', 'btn-primary'));
                    document.querySelectorAll('.installment-option').forEach(b => b.classList.add('btn-outline-primary'));
                    this.classList.remove('btn-outline-primary');
                    this.classList.add('active', 'btn-primary');
                    selectedInstallmentMonths = parseInt(this.dataset.months);
                    const balance = parseFloat(document.getElementById('portalInstallmentTotal').dataset.amount || 0);
                    const monthly = Math.ceil(balance / selectedInstallmentMonths * 100) / 100;
                    document.getElementById('portalInstallmentMonthlyAmount').textContent = '$' + monthly.toFixed(2);
                    document.getElementById('portalInstallmentPreview').style.display = 'block';
                    initInstallmentStripe();
                });
            });

        } catch (e) {
            showToast('Error', 'Failed to load billing information.', 'error');
        }
    }

    function renderBillingBalance(balance) {
        const amountEl = document.getElementById('portalBalanceAmount');
        const actionsEl = document.getElementById('portalPayActions');
        const detailEl = document.getElementById('portalBalanceDetail');

        if (amountEl) amountEl.textContent = '$' + (balance.CurrentBalance || 0).toFixed(2);

        if (balance.CurrentBalance > 0 && actionsEl) {
            actionsEl.style.display = 'flex';
            actionsEl.style.cssText = 'display: flex !important;';
        } else if (actionsEl) {
            actionsEl.style.display = 'none';
            // Show "All Paid" message
            const paidMsg = document.createElement('div');
            paidMsg.className = 'text-center mt-2';
            paidMsg.innerHTML = '<span class="badge bg-success-subtle text-success px-3 py-2" style="font-size:14px;"><i class="bi bi-check-circle me-1"></i>All Paid. No Balance Due.</span>';
            amountEl.parentElement.appendChild(paidMsg);
        }

        if (detailEl && balance.TotalCharges > 0) {
            detailEl.textContent = `Charges: $${balance.TotalCharges.toFixed(2)} | Insurance: $${balance.InsurancePaid.toFixed(2)} | You Paid: $${balance.PatientPaid.toFixed(2)}`;
        }

        if (balance.HasActivePlan) {
            document.getElementById('portalPaymentPlanBtn')?.remove();
        }
    }

    function renderActivePlan(plan) {
        const container = document.getElementById('portalActivePlan');
        if (!container || !plan) return;

        container.style.display = 'block';
        document.getElementById('portalPlanStatus').textContent = plan.StatusName;
        document.getElementById('portalPlanTotal').textContent = '$' + plan.TotalAmount.toFixed(2);
        document.getElementById('portalPlanPaid').textContent = '$' + plan.AmountPaid.toFixed(2);
        document.getElementById('portalPlanRemaining').textContent = '$' + plan.AmountRemaining.toFixed(2);

        const detailsEl = document.getElementById('portalPlanDetails');
        if (detailsEl && plan.Details) {
            detailsEl.innerHTML = plan.Details.map(d => {
                const statusClass = d.Status === 1 ? 'text-success' : d.Status === 3 ? 'text-danger' : 'text-muted';
                const icon = d.Status === 1 ? 'bi-check-circle-fill text-success' : d.Status === 3 ? 'bi-exclamation-circle-fill text-danger' : 'bi-circle text-muted';
                return `<div class="d-flex justify-content-between align-items-center py-1 border-bottom">
                    <div><i class="bi ${icon} me-2"></i><span class="small">#${d.InstallmentNumber} &mdash; ${new Date(d.DueDate).toLocaleDateString()}</span></div>
                    <div><span class="fw-semibold small ${statusClass}">$${d.Amount.toFixed(2)}</span> <span class="badge ${d.Status === 1 ? 'bg-success-subtle text-success' : d.Status === 3 ? 'bg-danger-subtle text-danger' : 'bg-secondary-subtle text-secondary'} ms-1">${d.StatusName}</span></div>
                </div>`;
            }).join('');
        }
    }

    function renderTransactions(transactions) {
        const listEl = document.getElementById('portalTransactionsList');
        const emptyEl = document.getElementById('portalTransactionsEmpty');
        if (!listEl) return;

        if (!transactions.length) {
            listEl.innerHTML = '';
            if (emptyEl) emptyEl.style.display = 'block';
            return;
        }

        if (emptyEl) emptyEl.style.display = 'none';
        listEl.innerHTML = transactions.map(t => `
            <div class="d-flex justify-content-between align-items-center px-3 py-2 border-bottom">
                <div>
                    <div class="small fw-semibold">${t.Description}</div>
                    <div class="text-muted" style="font-size: 11px;">${new Date(t.Date).toLocaleDateString()}</div>
                </div>
                <div class="fw-semibold" style="color: var(--portal-primary);">$${t.Amount.toFixed(2)}</div>
            </div>
        `).join('');
    }

    // --- Stripe Payment ---

    async function showStripePaymentForm(balance) {
        document.getElementById('portalStripeFormCard').style.display = 'block';
        document.getElementById('portalPayAmount').value = balance.toFixed(2);
        document.getElementById('portalBalanceCard').style.display = 'none';

        try {
            const resp = await apiRequest('/api/portal/billing/create-payment-intent', {
                method: 'POST',
                body: JSON.stringify({ Amount: balance, PaymentType: 0 })
            });

            if (!resp) return;
            currentPaymentClientSecret = resp.ClientSecret;

            stripeInstance = Stripe(resp.PublishableKey);
            stripeElements = stripeInstance.elements({ clientSecret: resp.ClientSecret });
            stripePaymentElement = stripeElements.create('payment');
            stripePaymentElement.mount('#portalStripeElement');

            stripePaymentElement.on('ready', () => {
                document.getElementById('portalConfirmPayment').disabled = false;
            });
        } catch (e) {
            showToast('Error', 'Failed to initialize payment form.', 'error');
        }
    }

    function hideStripePaymentForm() {
        document.getElementById('portalStripeFormCard').style.display = 'none';
        document.getElementById('portalBalanceCard').style.display = 'block';
        if (stripePaymentElement) stripePaymentElement.unmount();
        stripePaymentElement = null;
    }

    async function confirmStripePayment() {
        const btn = document.getElementById('portalConfirmPayment');
        btn.disabled = true;
        btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Processing...';

        try {
            const { error } = await stripeInstance.confirmPayment({
                elements: stripeElements,
                confirmParams: {
                    return_url: window.location.origin + '/Portal/Billing'
                }
            });

            if (error) {
                showToast('Payment Failed', error.message, 'error');
                btn.disabled = false;
                btn.innerHTML = '<i class="bi bi-lock me-1"></i>Pay Securely';
            }
            // If no error, Stripe redirects to return_url
        } catch (e) {
            showToast('Error', 'Payment processing failed.', 'error');
            btn.disabled = false;
            btn.innerHTML = '<i class="bi bi-lock me-1"></i>Pay Securely';
        }
    }

    // --- Installment Setup ---

    async function showInstallmentSetup(balance) {
        document.getElementById('portalInstallmentSetupCard').style.display = 'block';
        document.getElementById('portalBalanceCard').style.display = 'none';
        const totalEl = document.getElementById('portalInstallmentTotal');
        totalEl.textContent = '$' + balance.toFixed(2);
        totalEl.dataset.amount = balance;
    }

    function hideInstallmentSetup() {
        document.getElementById('portalInstallmentSetupCard').style.display = 'none';
        document.getElementById('portalBalanceCard').style.display = 'block';
        selectedInstallmentMonths = null;
    }

    async function initInstallmentStripe() {
        const stripeEl = document.getElementById('portalInstallmentStripeElement');
        const startBtn = document.getElementById('portalStartPlan');
        stripeEl.style.display = 'block';
        startBtn.style.display = 'block';

        try {
            const resp = await apiRequest('/api/portal/billing/setup-intent', { method: 'POST' });
            if (!resp) return;

            stripeInstance = Stripe(resp.PublishableKey);
            const elements = stripeInstance.elements({ clientSecret: resp.ClientSecret });
            const paymentElement = elements.create('payment');
            paymentElement.mount('#portalInstallmentStripeElement');

            paymentElement.on('ready', () => { startBtn.disabled = false; });

            startBtn.onclick = async () => {
                startBtn.disabled = true;
                startBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Setting up plan...';

                const { setupIntent, error } = await stripeInstance.confirmSetup({
                    elements,
                    redirect: 'if_required'
                });

                if (error) {
                    showToast('Error', error.message, 'error');
                    startBtn.disabled = false;
                    startBtn.innerHTML = '<i class="bi bi-calendar-check me-1"></i>Start Plan & Pay First Installment';
                    return;
                }

                // Create installment plan via API
                const balance = parseFloat(document.getElementById('portalInstallmentTotal').dataset.amount);
                const planResp = await apiRequest('/api/portal/billing/installment-plan', {
                    method: 'POST',
                    body: JSON.stringify({
                        TotalAmount: balance,
                        NumberOfInstallments: selectedInstallmentMonths,
                        StripePaymentMethodId: setupIntent.payment_method
                    })
                });

                if (planResp) {
                    showToast('Success', 'Payment plan created! First installment has been charged.', 'success');
                    // Reload billing page
                    setTimeout(() => window.location.reload(), 1500);
                } else {
                    startBtn.disabled = false;
                    startBtn.innerHTML = '<i class="bi bi-calendar-check me-1"></i>Start Plan & Pay First Installment';
                }
            };
        } catch (e) {
            showToast('Error', 'Failed to initialize card form.', 'error');
        }
    }

    // ============================================
    // BOOKING PAGE
    // ============================================
    //
    // The booking flow lives in its own modules under wwwroot/js/modules/portal/booking/.
    // PatientPortalModule's only job here is to mount the wizard when the user
    // navigates to /Portal/Booking. Everything else (state, API, steps, sidebar,
    // mobile bottom strip) is owned by those modules.
    async function loadBookingPage() {
        const root = document.getElementById('portalBookingWizardRoot');
        if (!root) return;
        if (typeof window.PortalBookingWizard !== 'function') {
            root.innerHTML = '<div class="alert alert-danger">Booking failed to load. Please refresh.</div>';
            return;
        }
        const wizard = new window.PortalBookingWizard();
        await wizard.mount(root);
        window._portalBookingWizard = wizard;
    }


    // Auto-initialize
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
