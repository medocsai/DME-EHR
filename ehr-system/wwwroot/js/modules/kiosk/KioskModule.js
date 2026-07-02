/**
 * KioskModule - Patient Check-In Kiosk
 *
 * Handles patient verification, consent form display, signature capture,
 * and check-in submission for tablet kiosk displays.
 *
 * @example
 *   const kiosk = new KioskModule({ token: 'abc123' });
 *   kiosk.init();
 */
class KioskModule {
    /**
     * @param {Object} options - Module options
     * @param {string} options.token - Kiosk token from URL
     */
    constructor(options = {}) {
        this.token = options.token || null;

        // State
        this.sessionToken = null;
        this.sessionExpiresAt = null;
        this.clinicInfo = null;
        this.patientInfo = null;
        this.appointmentInfo = null;
        this.consentForms = [];
        this.currentFormIndex = 0;
        this.formProgress = {};
        this.signaturePads = {};

        // Timers
        this.heartbeatInterval = null;
        this.timeoutWarningInterval = null;
        this.autoResetTimeout = null;
        this.countdownInterval = null;

        // API base
        this.apiBase = '/api/kiosk';

        // App-mode (Android kiosk tablet app via ?app=1)
        this.isAppMode = new URLSearchParams(window.location.search).get('app') === '1';
        this.appAdminEmail = null; // Populated on demand from /api/kiosksetup/me

        // Bind methods
        this._handleVerificationSubmit = this._handleVerificationSubmit.bind(this);
    }

    /**
     * Initialize the kiosk module
     */
    async init() {
        // Extract token from URL if not provided
        if (!this.token) {
            this.token = this._extractTokenFromUrl();
        }

        if (!this.token) {
            this._showError('Invalid Kiosk Link', 'This link is not valid. Please contact clinic staff for the correct link.');
            return;
        }

        // Setup input handlers
        this._setupZipInput();
        this._setupDobInputs();
        this._bindEvents();

        // Enable admin sign-out button/flow when running inside the Android kiosk app
        if (this.isAppMode) {
            this._setupAppMode();
        }

        // Initialize kiosk
        await this._initializeKiosk();
    }

    /**
     * Extract token from URL
     * @private
     * @returns {string|null} Token
     */
    _extractTokenFromUrl() {
        // Try query parameter first: /Kiosk?token=xxx
        const urlParams = new URLSearchParams(window.location.search);
        const queryToken = urlParams.get('token');
        if (queryToken) return queryToken;

        // Try URL path: /Kiosk/xxx
        const pathParts = window.location.pathname.split('/');
        const kioskIndex = pathParts.findIndex(p => p.toLowerCase() === 'kiosk');
        if (kioskIndex !== -1 && pathParts[kioskIndex + 1]) {
            return pathParts[kioskIndex + 1];
        }

        return null;
    }

    /**
     * Initialize kiosk with server validation
     * @private
     */
    async _initializeKiosk() {
        this._showLoading('Validating kiosk...');

        try {
            const response = await fetch(`${this.apiBase}/validate/${this.token}`);
            const data = await response.json();

            if (!data.IsValid) {
                this._showError('Kiosk Unavailable', data.Message || 'This kiosk is not available.');
                return;
            }

            this.clinicInfo = data;

            // Header removed from UI to save space - clinic/location info not needed
            // The template HTML contains its own heading

            this._hideLoading();
            this._showScreen('verificationScreen');
        } catch (error) {
            console.error('[KioskModule] Initialize error:', error);
            this._showError('Connection Error', 'Unable to connect to the server. Please try again or contact clinic staff.');
        }
    }

    /**
     * Bind event handlers
     * @private
     */
    _bindEvents() {
        const verifyForm = document.getElementById('verificationForm');
        if (verifyForm) {
            verifyForm.addEventListener('submit', this._handleVerificationSubmit);
        }
    }

    // ============================================
    // Screen Management
    // ============================================

    /**
     * Show a specific screen
     * @private
     * @param {string} screenId - Screen element ID
     */
    _showScreen(screenId) {
        document.querySelectorAll('.screen').forEach(screen => {
            screen.classList.remove('active');
        });
        const screen = document.getElementById(screenId);
        if (screen) {
            screen.classList.add('active');
            // Scroll to top when showing any screen
            window.scrollTo(0, 0);
        }
    }

    /**
     * Show loading overlay
     * @private
     * @param {string} message - Loading message
     */
    _showLoading(message = 'Loading...') {
        this._setElementText('loadingMessage', message);
        const overlay = document.getElementById('loadingOverlay');
        if (overlay) overlay.classList.remove('hidden');
    }

    /**
     * Hide loading overlay
     * @private
     */
    _hideLoading() {
        const overlay = document.getElementById('loadingOverlay');
        if (overlay) overlay.classList.add('hidden');
    }

    /**
     * Show error screen
     * @private
     * @param {string} title - Error title
     * @param {string} message - Error message
     */
    _showError(title, message) {
        this._hideLoading();
        this._setElementText('errorTitle', title);
        this._setElementText('errorMessage', message);
        this._showScreen('errorScreen');
    }

    // ============================================
    // Verification
    // ============================================

    /**
     * Setup ZIP input (digits only, max 5).
     * @private
     */
    _setupZipInput() {
        const zip = document.getElementById('zipCode');
        if (!zip) return;
        zip.addEventListener('input', function() {
            this.value = this.value.replace(/[^0-9]/g, '').substring(0, 5);
        });
    }

    /**
     * Setup DOB input auto-advance
     * @private
     */
    _setupDobInputs() {
        const dobMonth = document.getElementById('dobMonth');
        const dobDay = document.getElementById('dobDay');
        const dobYear = document.getElementById('dobYear');

        if (dobMonth) {
            dobMonth.addEventListener('input', function() {
                this.value = this.value.replace(/[^0-9]/g, '');
                if (this.value.length === 2 && dobDay) dobDay.focus();
            });
        }

        if (dobDay) {
            dobDay.addEventListener('input', function() {
                this.value = this.value.replace(/[^0-9]/g, '');
                if (this.value.length === 2 && dobYear) dobYear.focus();
            });
        }

        if (dobYear) {
            dobYear.addEventListener('input', function() {
                this.value = this.value.replace(/[^0-9]/g, '');
            });
        }
    }

    /**
     * Clear verification form
     */
    clearVerificationForm() {
        ['lastName', 'dobMonth', 'dobDay', 'dobYear', 'zipCode'].forEach(id => {
            const el = document.getElementById(id);
            if (el) el.value = '';
        });
        const errorDiv = document.getElementById('verificationError');
        if (errorDiv) errorDiv.style.display = 'none';
        const firstField = document.getElementById('lastName');
        if (firstField) firstField.focus();
    }

    /**
     * Handle verification form submit
     * @private
     * @param {Event} event - Submit event
     */
    async _handleVerificationSubmit(event) {
        event.preventDefault();

        const lastName = (document.getElementById('lastName')?.value || '').trim();
        const month = (document.getElementById('dobMonth')?.value || '').padStart(2, '0');
        const day = (document.getElementById('dobDay')?.value || '').padStart(2, '0');
        const year = document.getElementById('dobYear')?.value || '';
        const zipCode = (document.getElementById('zipCode')?.value || '').trim();

        // Validate
        if (!lastName) {
            this._showVerificationError('Please enter your last name');
            return;
        }

        if (!month || !day || !year || year.length !== 4) {
            this._showVerificationError('Please enter a valid date of birth');
            return;
        }

        if (zipCode.length < 5) {
            this._showVerificationError('Please enter your 5 digit ZIP code');
            return;
        }

        const dateOfBirth = `${year}-${month}-${day}`;
        const verifyBtn = document.getElementById('verifyBtn');
        if (verifyBtn) verifyBtn.disabled = true;

        this._showLoading('Verifying your information...');

        try {
            const response = await fetch(`${this.apiBase}/verify/${this.token}`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    LastName: lastName,
                    DateOfBirth: dateOfBirth,
                    ZipCode: zipCode
                })
            });

            const data = await response.json();
            this._hideLoading();
            if (verifyBtn) verifyBtn.disabled = false;

            if (!data.Success) {
                if (data.HasExistingConsent) {
                    this._showScreen('alreadyCompletedScreen');
                } else {
                    this._showVerificationError(data.Message || 'Verification failed. Please try again.');
                }
                return;
            }

            // Store session info
            this.sessionToken = data.SessionToken;
            this.sessionExpiresAt = new Date(data.SessionExpiresAt);
            this.patientInfo = data.PatientInfo;
            this.appointmentInfo = data.AppointmentInfo;

            // Start session management
            this._startSessionManagement();

            // Show welcome screen
            // 2026-05: if the patient already signed consent (typically from
            // the portal at home), skip the consent-forms flow and show a
            // "Yes, I am Here" confirmation screen instead.
            if (data.AlreadyConsented) {
                this._setElementText('alreadyConsentedFirstName', this.patientInfo.FirstName);
                const apptDetailsEl = document.getElementById('alreadyConsentedApptDetails');
                if (apptDetailsEl) {
                    apptDetailsEl.innerHTML =
                        `${this.appointmentInfo.AppointmentType} at ${this._formatTime(this.appointmentInfo.StartTime)}<br>` +
                        `with ${this.appointmentInfo.ProviderName}`;
                }
                this._showScreen('alreadyConsentedScreen');
                this._checkWelcomeBalance('alreadyConsentedBalance', 'alreadyConsentedBalanceAmount');
                return;
            }

            this._setElementText('patientFirstName', this.patientInfo.FirstName);
            const detailsEl = document.getElementById('appointmentDetails');
            if (detailsEl) {
                detailsEl.innerHTML =
                    `${this.appointmentInfo.AppointmentType} at ${this._formatTime(this.appointmentInfo.StartTime)}<br>` +
                    `with ${this.appointmentInfo.ProviderName}`;
            }

            this._showScreen('welcomeScreen');

            // Check and display outstanding balance on welcome screen
            this._checkWelcomeBalance();

        } catch (error) {
            console.error('[KioskModule] Verification error:', error);
            this._hideLoading();
            if (verifyBtn) verifyBtn.disabled = false;
            this._showVerificationError('An error occurred. Please try again.');
        }
    }

    /**
     * Show verification error
     * @private
     * @param {string} message - Error message
     */
    _showVerificationError(message) {
        const errorDiv = document.getElementById('verificationError');
        this._setElementText('verificationErrorText', message);
        if (errorDiv) errorDiv.style.display = 'flex';
    }

    // ============================================
    // Session Management
    // ============================================

    /**
     * Start session management (heartbeat, timeout)
     * @private
     */
    _startSessionManagement() {
        this.heartbeatInterval = setInterval(() => this._sendHeartbeat(), 30000);
        this.timeoutWarningInterval = setInterval(() => this._checkSessionTimeout(), 1000);
    }

    /**
     * Send heartbeat to keep session alive
     * @private
     */
    async _sendHeartbeat() {
        if (!this.sessionToken) return;

        try {
            const response = await fetch(`${this.apiBase}/heartbeat`, {
                method: 'POST',
                headers: { 'X-Kiosk-Session': this.sessionToken }
            });

            if (!response.ok) {
                this._handleSessionExpired();
            } else {
                const data = await response.json();
                if (data.expiresAt) {
                    this.sessionExpiresAt = new Date(data.expiresAt);
                }
            }
        } catch (error) {
            console.error('[KioskModule] Heartbeat error:', error);
        }
    }

    /**
     * Check for session timeout
     * @private
     */
    _checkSessionTimeout() {
        if (!this.sessionExpiresAt) return;

        const now = new Date();
        const secondsRemaining = Math.floor((this.sessionExpiresAt - now) / 1000);

        if (secondsRemaining <= 0) {
            this._handleSessionExpired();
        } else if (secondsRemaining <= 60) {
            this._showTimeoutWarning(secondsRemaining);
        } else {
            this._hideTimeoutWarning();
        }
    }

    /**
     * Show timeout warning
     * @private
     * @param {number} seconds - Seconds remaining
     */
    _showTimeoutWarning(seconds) {
        this._setElementText('timeoutCountdown', seconds);
        const warning = document.getElementById('timeoutWarning');
        if (warning) warning.classList.add('show');
    }

    /**
     * Hide timeout warning
     * @private
     */
    _hideTimeoutWarning() {
        const warning = document.getElementById('timeoutWarning');
        if (warning) warning.classList.remove('show');
    }

    /**
     * Extend session
     */
    extendSession() {
        this._sendHeartbeat();
        this._hideTimeoutWarning();
    }

    /**
     * Handle session expired
     * @private
     */
    _handleSessionExpired() {
        this._stopSessionManagement();
        this._showError('Session Expired', 'Your session has timed out. Please start over.');
    }

    /**
     * Stop session management
     * @private
     */
    _stopSessionManagement() {
        if (this.heartbeatInterval) {
            clearInterval(this.heartbeatInterval);
            this.heartbeatInterval = null;
        }
        if (this.timeoutWarningInterval) {
            clearInterval(this.timeoutWarningInterval);
            this.timeoutWarningInterval = null;
        }
        if (this.autoResetTimeout) {
            clearTimeout(this.autoResetTimeout);
            this.autoResetTimeout = null;
        }
        if (this.countdownInterval) {
            clearInterval(this.countdownInterval);
            this.countdownInterval = null;
        }
        this._hideTimeoutWarning();
    }

    /**
     * Cancel session
     */
    async cancelSession() {
        if (this.sessionToken) {
            try {
                await fetch(`${this.apiBase}/cancel`, {
                    method: 'POST',
                    headers: { 'X-Kiosk-Session': this.sessionToken }
                });
            } catch (error) {
                console.error('[KioskModule] Cancel session error:', error);
            }
        }
        this.reset();
    }

    // ============================================
    // Consent Forms
    // ============================================

    /**
     * Start consent forms flow
     */
    async startConsentForms() {
        this._showLoading('Loading consent forms...');

        try {
            const response = await fetch(`${this.apiBase}/forms`, {
                headers: { 'X-Kiosk-Session': this.sessionToken }
            });

            const data = await response.json();
            this._hideLoading();

            if (!data.Success) {
                this._showError('Error', data.Message || 'Unable to load consent forms.');
                return;
            }

            this.consentForms = data.Forms;
            this.currentFormIndex = 0;
            this.formProgress = {};

            this.consentForms.forEach(form => {
                this.formProgress[form.TemplateId] = {
                    viewedAt: null,
                    viewDurationSeconds: 0,
                    signatures: {}
                };
            });

            this._renderFormProgress();
            this._showCurrentForm();
            this._showScreen('consentFormScreen');

            // Ensure scroll reset after screen is visible (fixes scroll position persisting between patients)
            requestAnimationFrame(() => {
                const contentEl = document.getElementById('consentFormContent');
                if (contentEl) contentEl.scrollTop = 0;
                window.scrollTo(0, 0);
            });

        } catch (error) {
            console.error('[KioskModule] Load forms error:', error);
            this._hideLoading();
            this._showError('Error', 'Unable to load consent forms. Please try again.');
        }
    }

    /**
     * Render form progress dots
     * @private
     */
    _renderFormProgress() {
        const progressContainer = document.getElementById('formProgress');
        if (!progressContainer) return;

        progressContainer.innerHTML = this.consentForms.map((form, index) => {
            let className = 'form-progress-dot';
            if (index < this.currentFormIndex) className += ' completed';
            else if (index === this.currentFormIndex) className += ' active';
            return `<div class="${className}"></div>`;
        }).join('');
    }

    /**
     * Show current form
     * @private
     */
    _showCurrentForm() {
        const form = this.consentForms[this.currentFormIndex];
        if (!form) return;

        if (!this.formProgress[form.TemplateId].viewedAt) {
            this.formProgress[form.TemplateId].viewedAt = new Date().toISOString();
        }

        // Form name heading removed from UI - template HTML contains its own heading
        this._setElementText('currentFormNumber', this.currentFormIndex + 1);
        this._setElementText('totalForms', this.consentForms.length);

        const contentEl = document.getElementById('consentFormContent');
        if (contentEl) {
            contentEl.innerHTML = form.RenderedHtml;
            contentEl.scrollTop = 0;
        }

        this._initializeSignaturePads(form);

        const prevBtn = document.getElementById('prevFormBtn');
        if (prevBtn) prevBtn.style.display = this.currentFormIndex > 0 ? 'block' : 'none';

        const isLastForm = this.currentFormIndex === this.consentForms.length - 1;
        const nextBtn = document.getElementById('nextFormBtn');
        if (nextBtn) {
            nextBtn.innerHTML = isLastForm ?
                'Review & Submit <i class="bi bi-arrow-right"></i>' :
                'Next <i class="bi bi-arrow-right"></i>';
        }

        this._renderFormProgress();
    }

    /**
     * Initialize signature pads for a form
     * @private
     * @param {Object} form - Form object
     */
    _initializeSignaturePads(form) {
        this.signaturePads = {};

        setTimeout(() => {
            form.SignatureFields.forEach(field => {
                const canvas = document.querySelector(`canvas[data-field-id="${field.FieldId}"]`);
                if (!canvas) return;

                const rect = canvas.getBoundingClientRect();
                if (rect.width > 0 && rect.height > 0) {
                    canvas.width = rect.width;
                    canvas.height = rect.height;
                }

                const pad = new SignaturePad(canvas, {
                    backgroundColor: 'rgb(255, 255, 255)',
                    penColor: 'rgb(0, 0, 139)'
                });

                this.signaturePads[field.FieldId] = pad;

                const existingSignature = this.formProgress[form.TemplateId]?.signatures?.[field.FieldId];
                if (existingSignature?.ImageData) {
                    pad.fromDataURL(existingSignature.ImageData);
                    canvas.closest('.signature-canvas-container')?.classList.add('has-signature');
                }

                pad.addEventListener('endStroke', () => {
                    canvas.closest('.signature-canvas-container')?.classList.add('has-signature');
                    this._saveSignature(form.TemplateId, field.FieldId, pad);
                });
            });

            document.querySelectorAll('.clear-signature').forEach(btn => {
                btn.addEventListener('click', () => {
                    const fieldId = btn.dataset.fieldId;
                    const pad = this.signaturePads[fieldId];
                    if (pad) {
                        pad.clear();
                        const canvas = document.querySelector(`canvas[data-field-id="${fieldId}"]`);
                        canvas?.closest('.signature-canvas-container')?.classList.remove('has-signature');
                        if (this.formProgress[form.TemplateId]?.signatures) {
                            delete this.formProgress[form.TemplateId].signatures[fieldId];
                        }
                    }
                });
            });
        }, 100);
    }

    /**
     * Save signature data
     * @private
     */
    _saveSignature(templateId, fieldId, pad) {
        if (!this.formProgress[templateId].signatures) {
            this.formProgress[templateId].signatures = {};
        }

        this.formProgress[templateId].signatures[fieldId] = {
            FieldId: fieldId,
            ImageData: pad.toDataURL(),
            SignedAt: new Date().toISOString()
        };
    }

    /**
     * Validate current form
     * @private
     * @returns {boolean} Is valid
     */
    _validateCurrentForm() {
        const form = this.consentForms[this.currentFormIndex];
        const progress = this.formProgress[form.TemplateId];

        for (const field of form.SignatureFields) {
            if (field.IsRequired) {
                const signature = progress.signatures?.[field.FieldId];
                if (!signature || !signature.ImageData) {
                    this._showValidationAlert(field.Label);
                    return false;
                }

                const pad = this.signaturePads[field.FieldId];
                if (pad && pad.isEmpty()) {
                    this._showValidationAlert(field.Label);
                    return false;
                }
            }
        }

        return true;
    }

    /**
     * Show validation alert modal for missing signature
     * @private
     * @param {string} fieldLabel - The label of the signature field
     */
    _showValidationAlert(fieldLabel) {
        this._showGeneralAlert('Signature Required', `Please provide your signature before continuing.`);
    }

    /**
     * Show general alert modal
     * @private
     * @param {string} title - Alert title
     * @param {string} message - Alert message
     */
    _showGeneralAlert(title, message) {
        const titleEl = document.getElementById('kioskAlertTitle');
        const messageEl = document.getElementById('kioskAlertMessage');

        if (titleEl) titleEl.textContent = title;
        if (messageEl) messageEl.textContent = message;

        const modalEl = document.getElementById('kioskAlertModal');
        if (modalEl) {
            const modal = new bootstrap.Modal(modalEl);
            modal.show();
        }
    }

    /**
     * Go to previous form
     */
    previousForm() {
        if (this.currentFormIndex > 0) {
            this._updateViewDuration();
            this.currentFormIndex--;
            this._showCurrentForm();
        }
    }

    /**
     * Go to next form
     */
    nextForm() {
        if (!this._validateCurrentForm()) return;

        this._updateViewDuration();

        if (this.currentFormIndex < this.consentForms.length - 1) {
            this.currentFormIndex++;
            this._showCurrentForm();
        } else {
            this._showReviewScreen();
        }
    }

    /**
     * Update view duration for current form
     * @private
     */
    _updateViewDuration() {
        const form = this.consentForms[this.currentFormIndex];
        const progress = this.formProgress[form.TemplateId];
        if (progress.viewedAt) {
            const startTime = new Date(progress.viewedAt);
            progress.viewDurationSeconds = Math.floor((new Date() - startTime) / 1000);
        }
    }

    /**
     * Go back to forms from review
     */
    goBackToForms() {
        this.currentFormIndex = this.consentForms.length - 1;
        this._showCurrentForm();
        this._showScreen('consentFormScreen');
    }

    // ============================================
    // Review & Submit
    // ============================================

    /**
     * Show review screen
     * @private
     */
    _showReviewScreen() {
        const reviewList = document.getElementById('reviewFormsList');
        if (reviewList) {
            reviewList.innerHTML = this.consentForms.map(form => {
                const progress = this.formProgress[form.TemplateId];
                const sigCount = Object.keys(progress.signatures || {}).length;

                return `
                    <div class="review-form-item">
                        <span class="review-form-name">${this._escape(form.FormName)}</span>
                        <span class="review-form-signatures">
                            <i class="bi bi-check-circle-fill"></i>
                            ${sigCount} signature${sigCount !== 1 ? 's' : ''}
                        </span>
                    </div>
                `;
            }).join('');
        }

        this._setElementText('confirmPatientName', `${this.patientInfo.FirstName} ${this.patientInfo.LastName}`);
        const checkbox = document.getElementById('confirmationCheckbox');
        if (checkbox) checkbox.checked = false;
        this.updateSubmitButton();

        this._showScreen('reviewScreen');
    }

    /**
     * Update submit button state
     */
    updateSubmitButton() {
        const checkbox = document.getElementById('confirmationCheckbox');
        const submitBtn = document.getElementById('submitBtn');
        if (submitBtn && checkbox) {
            submitBtn.disabled = !checkbox.checked;
        }
    }

    /**
     * Submit consent forms
     */
    async submitConsent() {
        const checkbox = document.getElementById('confirmationCheckbox');
        if (!checkbox?.checked) {
            this._showGeneralAlert('Confirmation Required', 'Please confirm that you have read and understood the consent forms.');
            return;
        }

        this._showLoading('Submitting your consent forms...');

        const formData = this.consentForms.map(form => {
            const progress = this.formProgress[form.TemplateId];
            return {
                TemplateId: form.TemplateId,
                ViewedAt: progress.viewedAt,
                ViewDurationSeconds: progress.viewDurationSeconds,
                Signatures: Object.values(progress.signatures || {})
            };
        });

        try {
            const response = await fetch(`${this.apiBase}/submit`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'X-Kiosk-Session': this.sessionToken
                },
                body: JSON.stringify({
                    Forms: formData,
                    ConfirmationChecked: true
                })
            });

            const data = await response.json();
            this._hideLoading();

            if (!data.Success) {
                this._showError('Submission Error', data.Message || 'Unable to submit consent. Please try again.');
                return;
            }

            this._renderConsentSuccess(data);

        } catch (error) {
            console.error('[KioskModule] Submit error:', error);
            this._hideLoading();
            this._showError('Submission Error', 'An error occurred while submitting. Please try again.');
        }
    }

    /**
     * "Yes, I am Here" — kiosk confirmation when the patient already signed
     * consent (via the portal). Skips the consent forms screen, just transitions
     * the appointment to CheckedIn server-side. Reuses the same success screen
     * + intake handoff as the normal consent submit flow. 2026-05.
     */
    async confirmPresence() {
        const btn = document.getElementById('btnYesIAmHere');
        if (btn) btn.disabled = true;

        this._showLoading('Checking you in...');

        try {
            const response = await fetch(`${this.apiBase}/confirm-presence`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'X-Kiosk-Session': this.sessionToken
                }
            });

            const data = await response.json();
            this._hideLoading();

            if (!data.Success) {
                if (btn) btn.disabled = false;
                this._showError('Check-In Error', data.Message || 'Unable to confirm. Please see the front desk.');
                return;
            }

            this._renderConsentSuccess(data);

        } catch (error) {
            console.error('[KioskModule] Confirm-presence error:', error);
            this._hideLoading();
            if (btn) btn.disabled = false;
            this._showError('Check-In Error', 'An error occurred. Please try again or see the front desk.');
        }
    }

    /**
     * Shared post-success handler used by submitConsent + confirmPresence —
     * both produce the same response shape (KioskSubmitConsentResponseDto)
     * and share the same success screen + intake handoff UX.
     * @private
     */
    _renderConsentSuccess(data) {
        this._setElementText('successPatientName', `${this.patientInfo.FirstName} ${this.patientInfo.LastName}`);
        this._setElementText('successAppointmentTime', this._formatTime(this.appointmentInfo.StartTime));
        this._setElementText('successProviderName', this.appointmentInfo.ProviderName);

        this._stopSessionManagement();
        this._showScreen('successScreen');

        // Check for outstanding balance (soft notification)
        this._checkOutstandingBalance();

        // Consent → intake handoff: when the server says the patient still
        // has intake to do, swap the auto-advance countdown for a button
        // that takes them into the wizard. The intake-verify cookie was
        // already set server-side as a Set-Cookie header on this response,
        // so the wizard route will skip its own identity gate.
        // See rules/technical/consent-to-intake-handoff.md.
        const handoff = data.Intake;
        const handoffBlock = document.getElementById('successIntakeBlock');
        const heading = document.getElementById('successIntakeHeading');
        const btnStart = document.getElementById('btnStartIntake');
        const btnLabel = document.getElementById('btnStartIntakeLabel');
        const countdownEl = document.getElementById('autoAdvanceCountdown');

        if (handoff && handoff.Url && (handoff.State === 'not_started' || handoff.State === 'partial')) {
            if (heading) {
                heading.textContent = handoff.State === 'partial'
                    ? 'Would you like to continue your intake form?'
                    : 'Would you like to start your intake form?';
            }
            if (btnLabel) {
                btnLabel.textContent = handoff.State === 'partial' ? 'Continue Intake Form' : 'Start Intake Form';
            }
            if (btnStart) {
                btnStart.onclick = () => { window.location.href = handoff.Url; };
            }
            if (handoffBlock) handoffBlock.style.display = 'block';
            if (countdownEl) countdownEl.style.display = 'none';
            // Do NOT call _startAutoAdvanceCountdown — wait for the patient.
        } else {
            if (handoffBlock) handoffBlock.style.display = 'none';
            if (countdownEl) countdownEl.style.display = '';
            this._startAutoAdvanceCountdown(5);
        }
    }

    // ============================================
    // Outstanding Balance Check (soft notification)
    // ============================================

    async _checkOutstandingBalance() {
        try {
            const response = await fetch(`${this.apiBase}/balance`, {
                headers: { 'X-Kiosk-Session': this.sessionToken }
            });
            if (!response.ok) return;
            const data = await response.json();
            if (data.HasBalance && data.CurrentBalance > 0) {
                const notice = document.getElementById('kioskBalanceNotice');
                const amountEl = document.getElementById('kioskBalanceAmount');
                if (notice && amountEl) {
                    amountEl.textContent = '$' + data.CurrentBalance.toFixed(2);
                    notice.style.display = 'block';
                }
            }
        } catch (e) {
            // Silently fail — balance display is non-critical
        }
    }

    /**
     * Check and display outstanding balance on the welcome screen.
     * Reusable across welcome screen and the AlreadyConsented confirmation
     * screen by overriding the target element IDs.
     * @private
     */
    async _checkWelcomeBalance(noticeId = 'kioskWelcomeBalance', amountId = 'kioskWelcomeBalanceAmount') {
        try {
            const response = await fetch(`${this.apiBase}/balance`, {
                headers: { 'X-Kiosk-Session': this.sessionToken }
            });
            if (!response.ok) return;
            const data = await response.json();
            if (data.HasBalance && data.CurrentBalance > 0) {
                const notice = document.getElementById(noticeId);
                const amountEl = document.getElementById(amountId);
                if (notice && amountEl) {
                    amountEl.textContent = '$' + data.CurrentBalance.toFixed(2);
                    notice.style.display = 'flex';
                }
            }
        } catch (e) {
            // Silently fail — balance display is non-critical
        }
    }

    // ============================================
    // Auto-Advance Countdown
    // ============================================

    _startAutoAdvanceCountdown(seconds) {
        let remaining = seconds;
        const countdownEl = document.getElementById('countdownSeconds');
        if (countdownEl) countdownEl.textContent = remaining;

        this.countdownInterval = setInterval(() => {
            remaining--;
            if (countdownEl) countdownEl.textContent = remaining;
            if (remaining <= 0) {
                clearInterval(this.countdownInterval);
                this.countdownInterval = null;
                this.reset();
            }
        }, 1000);
    }

    // ============================================
    // Reset
    // ============================================

    /**
     * Reset kiosk to initial state
     */
    reset() {
        this._stopSessionManagement();

        this.sessionToken = null;
        this.sessionExpiresAt = null;
        this.patientInfo = null;
        this.appointmentInfo = null;
        this.consentForms = [];
        this.currentFormIndex = 0;
        this.formProgress = {};
        this.signaturePads = {};

        // Reset all scroll positions for next patient
        window.scrollTo(0, 0);
        document.body.scrollTop = 0;
        document.documentElement.scrollTop = 0;

        // Reset consent form content scroll if it exists
        const contentEl = document.getElementById('consentFormContent');
        if (contentEl) contentEl.scrollTop = 0;

        this.clearVerificationForm();
        this._showScreen('verificationScreen');
    }

    // ============================================
    // App Mode (Android kiosk tablet ? app=1)
    // ============================================

    /**
     * Show the admin logout button and wire the password-confirm modal.
     * Only called when the kiosk is opened inside the Android app (?app=1).
     * @private
     */
    _setupAppMode() {
        const btn = document.getElementById('kioskAppLogoutBtn');
        if (btn) {
            btn.style.display = 'flex';
            btn.addEventListener('click', () => this._openAppLogoutModal());
        }

        const confirmBtn = document.getElementById('kioskAppLogoutConfirmBtn');
        confirmBtn?.addEventListener('click', () => this._submitAppLogout());

        const form = document.getElementById('kioskAppLogoutForm');
        form?.addEventListener('submit', (e) => {
            e.preventDefault();
            this._submitAppLogout();
        });

        // Reset modal state whenever it is hidden
        const modalEl = document.getElementById('kioskAppLogoutModal');
        modalEl?.addEventListener('hidden.bs.modal', () => {
            const pwd = document.getElementById('kioskAppLogoutPassword');
            const err = document.getElementById('kioskAppLogoutError');
            if (pwd) pwd.value = '';
            err?.classList.add('d-none');
        });
    }

    /**
     * Fetch the signed-in admin email from the server (cookie-authenticated).
     * Returns null if the session cookie is missing or invalid.
     * @private
     */
    async _fetchAdminEmail() {
        if (this.appAdminEmail) return this.appAdminEmail;
        try {
            const r = await fetch('/api/kiosksetup/me', { credentials: 'same-origin' });
            if (r.status !== 200) return null;
            const data = await r.json();
            this.appAdminEmail = data?.Email || null;
            return this.appAdminEmail;
        } catch (_) {
            return null;
        }
    }

    /**
     * Mask local-part of email for display, e.g. "admin@md.com" -> "a***n@md.com"
     * @private
     */
    _maskEmail(email) {
        if (!email || typeof email !== 'string') return '—';
        const at = email.indexOf('@');
        if (at < 2) return email;
        const local = email.slice(0, at);
        const domain = email.slice(at);
        const stars = '*'.repeat(Math.max(3, local.length - 2));
        return `${local[0]}${stars}${local[local.length - 1]}${domain}`;
    }

    /**
     * Open the admin sign-out confirmation modal.
     * @private
     */
    async _openAppLogoutModal() {
        const modalEl = document.getElementById('kioskAppLogoutModal');
        if (!modalEl) return;

        // Show modal first so admin sees immediate feedback
        const modal = bootstrap.Modal.getOrCreateInstance(modalEl);
        modal.show();
        setTimeout(() => document.getElementById('kioskAppLogoutPassword')?.focus(), 150);

        // Populate masked email from server (cookie-authenticated)
        this._setElementText('kioskAppLogoutEmail', '…');
        const email = await this._fetchAdminEmail();
        if (email) {
            this._setElementText('kioskAppLogoutEmail', this._maskEmail(email));
        } else {
            // No valid session — send back to setup
            modal.hide();
            window.location.href = '/KioskSetup';
        }
    }

    /**
     * Verify the admin password and, on success, return to /KioskSetup.
     * @private
     */
    async _submitAppLogout() {
        const pwdInput = document.getElementById('kioskAppLogoutPassword');
        const errBox = document.getElementById('kioskAppLogoutError');
        const confirmBtn = document.getElementById('kioskAppLogoutConfirmBtn');
        const password = pwdInput?.value || '';

        errBox?.classList.add('d-none');

        if (!password) {
            if (errBox) {
                errBox.textContent = 'Please enter your password.';
                errBox.classList.remove('d-none');
            }
            pwdInput?.focus();
            return;
        }

        if (confirmBtn) confirmBtn.disabled = true;
        const originalHtml = confirmBtn?.innerHTML;
        if (confirmBtn) {
            confirmBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-2" role="status" aria-hidden="true"></span>Verifying...';
        }

        try {
            const response = await fetch('/api/kiosksetup/confirm-password', {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ Password: password })
            });

            if (response.status === 401) {
                // Cookie expired — force a fresh sign-in
                window.location.href = '/KioskSetup';
                return;
            }

            const data = await response.json();
            if (!data.success) {
                if (errBox) {
                    errBox.textContent = data.message || 'Incorrect password. Please try again.';
                    errBox.classList.remove('d-none');
                }
                if (pwdInput) { pwdInput.value = ''; pwdInput.focus(); }
                return;
            }

            // Cancel any active kiosk session so no patient state leaks forward
            if (this.sessionToken) {
                try {
                    await fetch(`${this.apiBase}/cancel`, {
                        method: 'POST',
                        headers: { 'X-Kiosk-Session': this.sessionToken }
                    });
                } catch (_) { /* best-effort */ }
            }

            // Server has already cleared the auth cookie — just navigate.
            window.location.href = '/KioskSetup';
        } catch (err) {
            console.error('[KioskModule] App logout error:', err);
            if (errBox) {
                errBox.textContent = 'Unable to verify right now. Please try again.';
                errBox.classList.remove('d-none');
            }
        } finally {
            if (confirmBtn) {
                confirmBtn.disabled = false;
                if (originalHtml) confirmBtn.innerHTML = originalHtml;
            }
        }
    }

    // ============================================
    // Utilities
    // ============================================

    /**
     * Parse server datetime ensuring UTC
     * @private
     * @param {string} dateString - Server datetime
     * @returns {Date} Date object
     */
    _parseServerDateTime(dateString) {
        if (!dateString) return null;
        if (!dateString.endsWith('Z') && !dateString.includes('+') && !dateString.includes('-', 10)) {
            dateString = dateString + 'Z';
        }
        return new Date(dateString);
    }

    /**
     * Format time in location timezone
     * @private
     * @param {string} dateString - UTC datetime
     * @returns {string} Formatted time
     */
    _formatTime(dateString) {
        if (!dateString) return '';

        const locationTzId = this.clinicInfo?.TimeZoneId || 'America/Chicago';
        const locationTzAbbr = this.clinicInfo?.TimeZoneAbbreviation || '';

        const date = this._parseServerDateTime(dateString);
        if (!date || isNaN(date.getTime())) return '';

        const formatter = new Intl.DateTimeFormat('en-US', {
            timeZone: locationTzId,
            hour: 'numeric',
            minute: '2-digit',
            hour12: true
        });

        const formattedTime = formatter.format(date);
        return locationTzAbbr ? `${formattedTime} ${locationTzAbbr}` : formattedTime;
    }

    /**
     * Set element text content
     * @private
     */
    _setElementText(id, text) {
        const el = document.getElementById(id);
        if (el) el.textContent = text;
    }

    /**
     * Escape HTML
     * @private
     */
    _escape(unsafe) {
        if (unsafe == null) return '';
        return String(unsafe)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    }
}

// Export for module usage
window.KioskModule = KioskModule;

// Global instance
window.kioskModule = null;

// Global functions for onclick handlers
function clearVerificationForm() {
    if (window.kioskModule) window.kioskModule.clearVerificationForm();
}

function extendSession() {
    if (window.kioskModule) window.kioskModule.extendSession();
}

function cancelSession() {
    if (window.kioskModule) window.kioskModule.cancelSession();
}

function startConsentForms() {
    if (window.kioskModule) window.kioskModule.startConsentForms();
}

function previousForm() {
    if (window.kioskModule) window.kioskModule.previousForm();
}

function nextForm() {
    if (window.kioskModule) window.kioskModule.nextForm();
}

function goBackToForms() {
    if (window.kioskModule) window.kioskModule.goBackToForms();
}

function updateSubmitButton() {
    if (window.kioskModule) window.kioskModule.updateSubmitButton();
}

function submitConsent() {
    if (window.kioskModule) window.kioskModule.submitConsent();
}

function resetKiosk() {
    if (window.kioskModule) window.kioskModule.reset();
}

function confirmPresence() {
    if (window.kioskModule) window.kioskModule.confirmPresence();
}

// Auto-initialize when DOM is ready
document.addEventListener('DOMContentLoaded', function() {
    const container = document.getElementById('kioskPage');
    if (!container) return;

    window.kioskModule = new KioskModule();
    window.kioskModule.init();
});
