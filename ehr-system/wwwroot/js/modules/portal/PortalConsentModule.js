/**
 * PortalConsentModule — patient signs consent forms from the portal (2026-05).
 *
 * Flow:
 *   1. Page loads → fetch /api/portal/consent/awaiting → list cards
 *   2. Patient picks an appointment → fetch /api/portal/consent/templates
 *      → render all templates stacked, each with embedded signature pads
 *   3. Patient signs each required field, checks confirmation box, submits
 *   4. POST /api/portal/consent/submit → success screen
 *
 * UX vs kiosk: kiosk paginates forms one-at-a-time on a tablet; portal stacks
 * them on one scrollable page (better for at-home desktop/laptop with mouse).
 * Backend submit payload is identical.
 *
 * Auth: portal JWT (window.authToken / localStorage 'portalAuthToken').
 * Reuses the apiRequest pattern from PatientPortalModule via direct fetch
 * since this module loads standalone on the Consent page.
 */
(function () {
    'use strict';

    if (!document.getElementById('consentAwaitingContainer')) return; // not on this page

    const AUTH_TOKEN_KEY = 'portalAuthToken';
    let authToken = localStorage.getItem(AUTH_TOKEN_KEY) || '';
    let selectedAppointment = null; // current appointment being signed
    let templates = [];             // templates for the selected appointment
    let signaturePads = {};         // fieldId → SignaturePad
    let patientFullName = '';       // for confirm checkbox label

    function authHeaders(extra) {
        const h = { 'Authorization': `Bearer ${authToken}` };
        if (extra) Object.assign(h, extra);
        return h;
    }

    async function apiGet(url) {
        console.log('[PortalConsent] GET', url, 'token len=', authToken.length);
        const resp = await fetch(url, { headers: authHeaders() });
        console.log('[PortalConsent] GET', url, '→', resp.status);
        if (resp.status === 401) {
            // Show a visible message instead of silently redirecting — makes
            // debug easier and avoids the confusing "page refresh" loop when
            // the token is stale.
            alert('Your session has expired. Please sign in again.');
            window.location.href = '/Portal/Login';
            return null;
        }
        if (!resp.ok) {
            const body = await resp.text().catch(() => '');
            console.error('[PortalConsent] GET failed', url, resp.status, body);
            throw new Error(`GET ${url} → ${resp.status}`);
        }
        return resp.json();
    }

    async function apiPost(url, body) {
        console.log('[PortalConsent] POST', url);
        const resp = await fetch(url, {
            method: 'POST',
            headers: authHeaders({ 'Content-Type': 'application/json' }),
            body: JSON.stringify(body)
        });
        console.log('[PortalConsent] POST', url, '→', resp.status);
        if (resp.status === 401) {
            alert('Your session has expired. Please sign in again.');
            window.location.href = '/Portal/Login';
            return null;
        }
        return resp.json().catch(() => ({}));
    }

    function escape(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, c =>
            ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    }

    // ============================================================
    // Stage 1 — list of appointments awaiting consent
    // ============================================================
    async function loadAwaiting() {
        const container = document.getElementById('consentAwaitingContainer');
        try {
            const items = await apiGet('/api/portal/consent/awaiting');
            if (!items) return;

            if (!items.length) {
                container.innerHTML = `
                    <div class="card portal-card">
                        <div class="card-body text-center py-4">
                            <i class="bi bi-check-circle text-success" style="font-size: 2.5rem;"></i>
                            <h6 class="mt-3 mb-1">All caught up</h6>
                            <p class="text-muted small mb-0">No consent forms are awaiting your signature.</p>
                        </div>
                    </div>
                `;
                return;
            }

            container.innerHTML = items.map(a => `
                <div class="card portal-card mb-2" data-appt-id="${a.AppointmentId}" style="cursor:pointer;">
                    <div class="card-body d-flex justify-content-between align-items-center py-3">
                        <div class="me-3">
                            <h6 class="mb-1">${escape(a.DateFormatted)} at ${escape(a.StartTimeFormatted)}</h6>
                            <p class="mb-0 small text-muted">
                                <i class="bi bi-person me-1"></i>${escape(a.ProviderName)}
                                ${a.LocationName ? ` <i class="bi bi-geo-alt ms-2 me-1"></i>${escape(a.LocationName)}` : ''}
                            </p>
                            <p class="mb-0 small">${escape(a.AppointmentType)}${a.IsTelehealth ? ' <span class="badge bg-info-subtle text-info">Telehealth</span>' : ''}</p>
                        </div>
                        <button type="button" class="btn btn-primary flex-shrink-0">
                            <i class="bi bi-pencil-square me-1"></i>Sign Now
                        </button>
                    </div>
                </div>
            `).join('');

            container.querySelectorAll('[data-appt-id]').forEach(card => {
                card.addEventListener('click', (e) => {
                    console.log('[PortalConsent] Card clicked', card.dataset.apptId);
                    const apptId = parseInt(card.dataset.apptId, 10);
                    const appt = items.find(x => x.AppointmentId === apptId);
                    console.log('[PortalConsent] Matched appt:', appt);
                    if (appt) {
                        openFormScreen(appt);
                    } else {
                        console.warn('[PortalConsent] No appt match for id', apptId);
                    }
                });
            });
        } catch (e) {
            console.error('[PortalConsent] Failed to load awaiting', e);
            container.innerHTML = '<div class="alert alert-danger">Failed to load consent forms. Please refresh.</div>';
        }
    }

    // ============================================================
    // Stage 2 — render templates with signature pads
    // ============================================================
    async function openFormScreen(appt) {
        selectedAppointment = appt;
        signaturePads = {};

        document.getElementById('consentAwaitingContainer').classList.add('d-none');
        document.getElementById('consentSuccessScreen').classList.add('d-none');
        document.getElementById('consentFormScreen').classList.remove('d-none');

        document.getElementById('consentFormApptHeader').textContent =
            `${appt.DateFormatted} at ${appt.StartTimeFormatted}`;
        document.getElementById('consentFormApptMeta').textContent =
            `${appt.ProviderName}${appt.LocationName ? ' · ' + appt.LocationName : ''}`;

        const submitBtn = document.getElementById('consentSubmitBtn');
        const confirmCheck = document.getElementById('consentConfirmCheck');
        submitBtn.disabled = true;
        confirmCheck.checked = false;
        document.getElementById('consentSubmitError').classList.add('d-none');

        const container = document.getElementById('consentTemplatesContainer');
        container.innerHTML = '<div class="text-center py-4"><div class="spinner-border spinner-border-sm"></div> Loading forms...</div>';

        try {
            templates = await apiGet(`/api/portal/consent/templates?appointmentId=${appt.AppointmentId}`);
            if (!templates || templates.length === 0) {
                container.innerHTML = '<div class="alert alert-warning">No consent forms are configured for this appointment. Please contact the clinic.</div>';
                return;
            }

            // Pull patient name from the profile endpoint so the confirm
            // checkbox label can show "I confirm I am {full name}".
            try {
                const profile = await apiGet('/api/portal/profile');
                if (profile) {
                    patientFullName = profile.FullName
                        || `${profile.FirstName || ''} ${profile.LastName || ''}`.trim()
                        || 'me';
                    document.getElementById('consentPatientName').textContent = patientFullName;
                }
            } catch { /* non-blocking */ }

            container.innerHTML = templates.map((t, i) => `
                <div class="card portal-card mb-3">
                    <div class="card-header"><h6 class="mb-0 text-primary">Form ${i + 1} of ${templates.length}: ${escape(t.FormName)}</h6></div>
                    <div class="card-body">
                        <div class="consent-rendered-html mb-3">${t.RenderedHtml}</div>
                    </div>
                </div>
            `).join('');

            // Initialize signature pads for every <canvas data-field-id="...">
            // present in the rendered template HTML (the same markup the kiosk renders).
            setTimeout(() => initializeSignaturePads(), 100);

            // Submit button enables only when the confirmation checkbox is ticked.
            confirmCheck.onchange = () => { submitBtn.disabled = !confirmCheck.checked; };
        } catch (e) {
            console.error('[PortalConsent] Failed to load templates', e);
            container.innerHTML = '<div class="alert alert-danger">Failed to load consent forms. Please try again.</div>';
        }
    }

    function initializeSignaturePads() {
        if (typeof SignaturePad === 'undefined') {
            console.error('[PortalConsent] SignaturePad library not loaded');
            return;
        }
        templates.forEach(template => {
            (template.SignatureFields || []).forEach(field => {
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

                // Key by template+field so collisions across forms are impossible.
                signaturePads[`${template.TemplateId}::${field.FieldId}`] = pad;

                pad.addEventListener('endStroke', () => {
                    canvas.closest('.signature-canvas-container')?.classList.add('has-signature');
                });
            });
        });

        // Wire any "clear signature" buttons rendered by the template HTML.
        document.querySelectorAll('.clear-signature').forEach(btn => {
            btn.addEventListener('click', () => {
                const fieldId = btn.dataset.fieldId;
                // Find the pad — we don't know which template owns this canvas,
                // but template+field combo is unique. Look up by suffix match.
                Object.entries(signaturePads).forEach(([key, pad]) => {
                    if (key.endsWith(`::${fieldId}`)) {
                        pad.clear();
                        const canvas = document.querySelector(`canvas[data-field-id="${fieldId}"]`);
                        canvas?.closest('.signature-canvas-container')?.classList.remove('has-signature');
                    }
                });
            });
        });
    }

    // ============================================================
    // Stage 3 — submit
    // ============================================================
    async function submit() {
        if (!selectedAppointment) return;
        const submitBtn = document.getElementById('consentSubmitBtn');
        const errEl = document.getElementById('consentSubmitError');
        errEl.classList.add('d-none');

        // Collect form payloads — same shape as the kiosk submit body.
        const forms = [];
        for (const template of templates) {
            const signatures = [];
            const requiredMissing = [];
            for (const field of (template.SignatureFields || [])) {
                const pad = signaturePads[`${template.TemplateId}::${field.FieldId}`];
                if (!pad || pad.isEmpty()) {
                    if (field.IsRequired) requiredMissing.push(`${template.FormName} → ${field.Label}`);
                    continue;
                }
                signatures.push({
                    FieldId: field.FieldId,
                    ImageData: pad.toDataURL('image/png'),
                    SignedAt: new Date().toISOString()
                });
            }
            if (requiredMissing.length) {
                errEl.innerHTML = '<strong>Please sign:</strong><br>' + requiredMissing.map(escape).join('<br>');
                errEl.classList.remove('d-none');
                errEl.scrollIntoView({ behavior: 'smooth', block: 'center' });
                return;
            }
            forms.push({
                TemplateId: template.TemplateId,
                ViewedAt: new Date().toISOString(),
                ViewDurationSeconds: 0,
                Signatures: signatures
            });
        }

        submitBtn.disabled = true;
        const origLabel = submitBtn.innerHTML;
        submitBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Submitting...';

        try {
            const result = await apiPost('/api/portal/consent/submit', {
                AppointmentId: selectedAppointment.AppointmentId,
                Forms: forms,
                ConfirmationChecked: true
            });
            if (!result || !result.Success) {
                errEl.textContent = result?.Message || 'Failed to submit. Please try again.';
                errEl.classList.remove('d-none');
                submitBtn.disabled = false;
                submitBtn.innerHTML = origLabel;
                return;
            }

            // Success screen.
            document.getElementById('consentFormScreen').classList.add('d-none');
            const apptDate = selectedAppointment.DateFormatted + ' at ' + selectedAppointment.StartTimeFormatted;
            document.getElementById('consentSuccessApptDate').textContent = apptDate;
            document.getElementById('consentSuccessScreen').classList.remove('d-none');
        } catch (e) {
            console.error('[PortalConsent] Submit error', e);
            errEl.textContent = 'A network error occurred. Please try again.';
            errEl.classList.remove('d-none');
            submitBtn.disabled = false;
            submitBtn.innerHTML = origLabel;
        }
    }

    function cancelToList() {
        selectedAppointment = null;
        signaturePads = {};
        templates = [];
        document.getElementById('consentFormScreen').classList.add('d-none');
        document.getElementById('consentSuccessScreen').classList.add('d-none');
        document.getElementById('consentAwaitingContainer').classList.remove('d-none');
        loadAwaiting();
    }

    document.getElementById('consentBackBtn')?.addEventListener('click', cancelToList);
    document.getElementById('consentCancelBtn')?.addEventListener('click', cancelToList);
    document.getElementById('consentSubmitBtn')?.addEventListener('click', submit);

    // Initial load
    loadAwaiting();
})();
