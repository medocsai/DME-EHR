/**
 * TelehealthPatientModule — Patient-facing waiting room + video call.
 *
 * Loaded ONLY on /Telehealth/Join/{token} page (standalone, no EHR auth).
 *
 * Flow:
 *   1. Validate token → show appointment info
 *   2. Join waiting room → auto check-in
 *   3. Connect to TelehealthHub (anonymous) → listen for PatientAdmitted
 *   4. On admit → load Jitsi iframe → enter video call
 */
(function () {
    'use strict';

    const page = document.getElementById('telehealthJoinPage');
    if (!page) return;

    const token = page.dataset.token;
    if (!token) {
        showError('No telehealth token provided. Please use the link from your email.');
        return;
    }

    // State elements
    const stateLoading = document.getElementById('stateLoading');
    const stateError = document.getElementById('stateError');
    const stateVerify = document.getElementById('stateVerify');
    const stateWaiting = document.getElementById('stateWaiting');
    const stateInCall = document.getElementById('stateInCall');

    // Data
    let appointmentInfo = null;
    let jitsiApi = null;
    let signalRConnection = null;

    // Start the flow
    init();

    async function init() {
        try {
            // Step 1: Validate token
            appointmentInfo = await validateToken(token);
            if (!appointmentInfo || !appointmentInfo.IsValid) {
                showError(appointmentInfo?.Message || 'This telehealth link is invalid or has expired.');
                return;
            }

            // Step 2: Show verification screen (LastName + DOB + ZipCode)
            showVerificationScreen();

        } catch (err) {
            console.error('[TelehealthPatient] Init error:', err);
            showError('Something went wrong. Please try refreshing the page.');
        }
    }

    function showVerificationScreen() {
        stateLoading.classList.add('d-none');
        stateError.classList.add('d-none');
        stateVerify.classList.remove('d-none');

        // Auto-advance DOB inputs
        const dobMm = document.getElementById('vDobMm');
        const dobDd = document.getElementById('vDobDd');
        const dobYyyy = document.getElementById('vDobYyyy');
        [dobMm, dobDd, dobYyyy].forEach(inp => {
            inp.addEventListener('input', () => { inp.value = inp.value.replace(/\D/g, ''); });
        });
        dobMm.addEventListener('input', () => { if (dobMm.value.length === 2) dobDd.focus(); });
        dobDd.addEventListener('input', () => { if (dobDd.value.length === 2) dobYyyy.focus(); });

        // Digit-strip on the ZIP field (only digits, max 5).
        const zipInp = document.getElementById('vZip');
        if (zipInp) {
            zipInp.addEventListener('input', () => {
                zipInp.value = zipInp.value.replace(/\D/g, '').substring(0, 5);
            });
        }

        // Focus the Last Name field
        document.getElementById('vLastName')?.focus();

        // Verify button
        document.getElementById('verifyBtn').addEventListener('click', handleVerify);

        // Allow Enter key to submit from the ZIP field
        zipInp?.addEventListener('keydown', (e) => { if (e.key === 'Enter') handleVerify(); });
    }

    async function handleVerify() {
        const lastName = (document.getElementById('vLastName')?.value || '').trim();
        const mm = document.getElementById('vDobMm').value.padStart(2, '0');
        const dd = document.getElementById('vDobDd').value.padStart(2, '0');
        const yyyy = document.getElementById('vDobYyyy').value;
        const zip = (document.getElementById('vZip')?.value || '').trim();

        const errEl = document.getElementById('verifyError');
        errEl.classList.add('d-none');

        if (!lastName) {
            errEl.textContent = 'Please enter your last name.';
            errEl.classList.remove('d-none');
            return;
        }
        if (!mm || !dd || yyyy.length !== 4) {
            errEl.textContent = 'Please enter your complete date of birth.';
            errEl.classList.remove('d-none');
            return;
        }
        if (zip.length < 5) {
            errEl.textContent = 'Please enter your 5 digit ZIP code.';
            errEl.classList.remove('d-none');
            return;
        }

        const dob = `${yyyy}-${mm}-${dd}`;
        const btn = document.getElementById('verifyBtn');
        btn.disabled = true;
        btn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Verifying...';

        try {
            const response = await fetch(`/api/telehealth/verify-identity/${encodeURIComponent(token)}`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ LastName: lastName, DateOfBirth: dob, ZipCode: zip })
            });

            const data = await response.json().catch(() => null);

            if (!response.ok || !data?.Success) {
                errEl.textContent = data?.Message || 'Verification failed. Please try again.';
                errEl.classList.remove('d-none');
                btn.disabled = false;
                btn.innerHTML = '<i class="bi bi-check-circle me-1"></i>Verify & Join';
                return;
            }

            // Verification passed — proceed to waiting room
            // Merge verified info into appointmentInfo for the waiting room display
            if (data.ProviderName) appointmentInfo.ProviderName = data.ProviderName;
            if (data.AppointmentTime) appointmentInfo.FormattedAppointmentTime = data.AppointmentTime;
            if (data.PatientFirstName) appointmentInfo.PatientFirstName = data.PatientFirstName;

            stateVerify.classList.add('d-none');
            showWaitingRoom(appointmentInfo);
            await joinWaitingRoom(token);
            await connectSignalR(token);

        } catch (err) {
            console.error('[TelehealthPatient] Verify error:', err);
            errEl.textContent = 'Something went wrong. Please try again.';
            errEl.classList.remove('d-none');
            btn.disabled = false;
            btn.innerHTML = '<i class="bi bi-check-circle me-1"></i>Verify & Join';
        }
    }

    async function validateToken(token) {
        const response = await fetch(`/api/telehealth/validate/${encodeURIComponent(token)}`);
        if (!response.ok) {
            const data = await response.json().catch(() => null);
            return data || { IsValid: false, Message: 'Unable to validate this link.' };
        }
        return await response.json();
    }

    async function joinWaitingRoom(token) {
        try {
            const response = await fetch(`/api/telehealth/join/${encodeURIComponent(token)}`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' }
            });
            const data = await response.json().catch(() => null);
            console.log('[TelehealthPatient] Joined waiting room:', data);
        } catch (err) {
            console.error('[TelehealthPatient] Failed to join waiting room:', err);
        }
    }

    async function connectSignalR(token) {
        if (typeof signalR === 'undefined') {
            console.warn('[TelehealthPatient] SignalR not available');
            // Fallback: poll for admission
            startPolling();
            return;
        }

        try {
            signalRConnection = new signalR.HubConnectionBuilder()
                .withUrl('/hubs/telehealth') // No accessTokenFactory — anonymous
                .withAutomaticReconnect([2000, 4000, 8000, 16000, 32000])
                .configureLogging(signalR.LogLevel.Information)
                .build();

            // Listen for admission
            signalRConnection.on('PatientAdmitted', (data) => {
                console.log('[TelehealthPatient] Admitted to call!', data);
                onAdmitted();
            });

            signalRConnection.onreconnecting(() => {
                console.log('[TelehealthPatient] SignalR reconnecting...');
            });

            signalRConnection.onreconnected(() => {
                console.log('[TelehealthPatient] SignalR reconnected');
                // Rejoin the waiting room group with presence info
                const apptId = appointmentInfo?.AppointmentId || appointmentInfo?.appointmentId || 0;
                const pName = appointmentInfo?.PatientName || appointmentInfo?.patientName || 'Patient';
                signalRConnection.invoke('JoinWaitingRoom', token, apptId, pName).catch(err =>
                    console.error('[TelehealthPatient] Failed to rejoin waiting room:', err));
            });

            await signalRConnection.start();
            console.log('[TelehealthPatient] SignalR connected');

            // Join the waiting room group with appointment info for presence tracking
            const apptId = appointmentInfo?.AppointmentId || appointmentInfo?.appointmentId || 0;
            const patientName = appointmentInfo?.PatientName || appointmentInfo?.patientName || 'Patient';
            await signalRConnection.invoke('JoinWaitingRoom', token, apptId, patientName);
            console.log('[TelehealthPatient] Joined SignalR waiting room group');
        } catch (err) {
            console.error('[TelehealthPatient] SignalR connection failed:', err);
            // Fallback to polling if SignalR fails
            startPolling();
        }
    }

    function startPolling() {
        // Fallback: check every 5 seconds if we should be admitted
        // This is a simple fallback in case SignalR isn't available
        console.log('[TelehealthPatient] Starting polling fallback');
        // For now, we just wait for SignalR — polling would require a server endpoint
    }

    function showWaitingRoom(info) {
        // Normalize property names (handle PascalCase from API)
        const providerName = info.ProviderName || info.providerName || 'Your Provider';
        const clinicName = info.ClinicName || info.clinicName || 'MEDOCS';

        // Use pre-formatted time from verification (already in location timezone)
        // or fall back to raw AppointmentTime from validation
        const formattedTime = info.FormattedAppointmentTime;
        const rawTime = info.AppointmentTime || info.appointmentTime;

        // Update UI
        document.getElementById('clinicName').innerHTML = `<i class="bi bi-camera-video me-2"></i>${escapeHtml(clinicName)}`;
        document.getElementById('providerName').textContent = providerName;

        if (formattedTime) {
            document.getElementById('appointmentTime').textContent = formattedTime;
        } else if (rawTime) {
            const time = new Date(rawTime);
            document.getElementById('appointmentTime').textContent = time.toLocaleString([], {
                weekday: 'short',
                month: 'short',
                day: 'numeric',
                hour: '2-digit',
                minute: '2-digit'
            });
        }

        // Switch state
        stateLoading.classList.add('d-none');
        stateError.classList.add('d-none');
        stateVerify.classList.add('d-none');
        stateWaiting.classList.remove('d-none');
    }

    function showError(message) {
        document.getElementById('errorMessage').textContent = message;
        stateLoading.classList.add('d-none');
        stateWaiting.classList.add('d-none');
        stateError.classList.remove('d-none');
    }

    function onAdmitted() {
        // Load Jitsi and enter the call
        if (!appointmentInfo) return;

        // Fetch Jitsi config from server (we need the domain and room name)
        // Since we're anonymous, we get config from the validation response
        // The room name is derived from the appointment's TelehealthUrl
        loadJitsiAndJoin();
    }

    async function loadJitsiAndJoin() {
        try {
            // Get room name and domain from appointmentInfo (set by validateToken)
            const jitsiDomain = appointmentInfo.JitsiDomain || appointmentInfo.jitsiDomain || '8x8.vc';
            const appId = appointmentInfo.AppId || appointmentInfo.appId || '';
            const roomName = appointmentInfo.RoomName || appointmentInfo.roomName;
            const jwt = appointmentInfo.Jwt || appointmentInfo.jwt || '';

            if (!roomName) {
                console.error('[TelehealthPatient] No room name available from server');
                showError('Unable to join video call. Please try refreshing the page.');
                return;
            }

            // JaaS: room name must be prefixed with AppID
            const fullRoomName = appId ? `${appId}/${roomName}` : roomName;

            console.log('[TelehealthPatient] Loading Jitsi for room:', fullRoomName, 'on domain:', jitsiDomain);

            // JaaS: load from 8x8.vc/{AppId}/external_api.js
            const scriptUrl = appId
                ? `https://${jitsiDomain}/${appId}/external_api.js`
                : `https://${jitsiDomain}/external_api.js`;
            await loadScript(scriptUrl);

            // Get patient name from appointment info
            const patientName = appointmentInfo.PatientName || appointmentInfo.patientName || 'Patient';

            // Show in-call state
            stateWaiting.classList.add('d-none');
            stateInCall.classList.remove('d-none');

            // Build Jitsi options
            const jitsiOptions = {
                roomName: fullRoomName,
                width: '100%',
                height: '100%',
                parentNode: document.getElementById('jitsiPatientContainer'),
                userInfo: {
                    displayName: patientName
                },
                configOverwrite: {
                    startWithAudioMuted: false,
                    startWithVideoMuted: false,
                    disableDeepLinking: true,
                    prejoinConfig: {
                        enabled: false
                    },
                    // 'select-background' exposes Jitsi's native background
                    // picker (Blur / pre-built scenes / Upload custom image).
                    // Fully client-side — image data never leaves the patient's
                    // browser, so no PHI / privacy concerns vs. our server.
                    toolbarButtons: ['microphone', 'camera', 'select-background', 'hangup'],
                    disableScreensharing: true,
                    enableClosePage: false,
                    disableInviteFunctions: true,
                    enableNoisyMicDetection: false
                },
                interfaceConfigOverwrite: {
                    SHOW_JITSI_WATERMARK: false,
                    SHOW_WATERMARK_FOR_GUESTS: false,
                    SHOW_POWERED_BY: false,
                    SHOW_PROMOTIONAL_CLOSE_PAGE: false,
                    TOOLBAR_BUTTONS: ['microphone', 'camera', 'select-background', 'hangup'],
                    // DISABLE_VIDEO_BACKGROUND removed so the picker is usable.
                    HIDE_INVITE_MORE_HEADER: true,
                    SHOW_CHROME_EXTENSION_BANNER: false,
                    MOBILE_APP_PROMO: false,
                    DISABLE_JOIN_LEAVE_NOTIFICATIONS: true
                }
            };

            // Add JWT if available (JaaS authenticated mode — no login screen)
            if (jwt) {
                jitsiOptions.jwt = jwt;
            }

            // Initialize Jitsi
            jitsiApi = new JitsiMeetExternalAPI(jitsiDomain, jitsiOptions);

            // Handle call ended
            jitsiApi.addEventListener('readyToClose', () => {
                console.log('[TelehealthPatient] Call ended');
                stateInCall.classList.add('d-none');

                // Disconnect SignalR so provider gets the PatientLeft event
                if (signalRConnection) {
                    signalRConnection.stop().catch(err =>
                        console.error('[TelehealthPatient] SignalR disconnect error:', err));
                    signalRConnection = null;
                }

                // Show a thank-you message
                showCallEnded();
            });

            console.log('[TelehealthPatient] Jitsi initialized, joined call');

        } catch (err) {
            console.error('[TelehealthPatient] Failed to load Jitsi:', err);
            showError('Unable to start video call. Please check your internet connection and try again.');
        }
    }

    // Ensure SignalR disconnects when patient closes/navigates away from the tab
    window.addEventListener('beforeunload', () => {
        if (signalRConnection) {
            signalRConnection.stop().catch(() => {});
        }
    });

    function showCallEnded() {
        const container = document.querySelector('.telehealth-container') || stateWaiting.parentElement;
        stateInCall.classList.add('d-none');

        // Show a simple thank-you screen
        const thankYou = document.createElement('div');
        thankYou.className = 'telehealth-container';
        thankYou.innerHTML = `
            <div class="telehealth-card">
                <div class="telehealth-header">
                    <h1><i class="bi bi-check-circle me-2"></i>Visit Complete</h1>
                </div>
                <div class="telehealth-body text-center">
                    <div style="font-size: 3rem; color: #4CAF50; margin: 20px 0;">
                        <i class="bi bi-check-circle-fill"></i>
                    </div>
                    <h4>Thank you for your visit!</h4>
                    <p class="text-muted">Your telehealth visit has ended. You may close this window.</p>
                </div>
            </div>
        `;
        document.getElementById('telehealthJoinPage').appendChild(thankYou);
    }

    function loadScript(src) {
        return new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = src;
            script.onload = resolve;
            script.onerror = reject;
            document.head.appendChild(script);
        });
    }

    function escapeHtml(str) {
        if (!str) return '';
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }
})();
