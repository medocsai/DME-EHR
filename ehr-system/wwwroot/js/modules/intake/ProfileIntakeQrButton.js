/**
 * ProfileIntakeQrButton (Employee of Patient Profile)
 *
 * Why: show the QR code + copyable URL modal for the patient's tablet intake link.
 * What:
 *   - Exposes window.ProfileIntakeQrButton.openQrModal(patientId)
 *   - Modal fetches /api/clinic/patients/{id}/intake-token -> { url, token }
 *   - Renders QR (via qrcode-generator lib) + copyable URL input + Copy + Rotate + Close
 *   - Rotate confirms, POSTs /rotate, regenerates QR.
 * Who calls: Patient Profile view (Phase 3 entry point).
 *
 * Security note: the URL contains an opaque GUID. When rotated, old QRs die immediately.
 */
(function () {
    'use strict';

    const MODAL_ID = 'profileIntakeQrModal';
    const state = { patientId: null, currentUrl: '' };

    function esc(s) { return String(s == null ? '' : s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }

    function authHeaders() {
        // Clinic-side calls use the standard localStorage JWT. Match other modules.
        const token =
            localStorage.getItem('authToken') ||
            localStorage.getItem('token') ||
            (window.App && window.App.state && typeof window.App.state.get === 'function' && window.App.state.get('authToken'));
        return token ? { 'Authorization': 'Bearer ' + token } : {};
    }

    function ensureModal() {
        let el = document.getElementById(MODAL_ID);
        if (el) return el;
        el = document.createElement('div');
        el.id = MODAL_ID;
        el.className = 'modal fade';
        el.tabIndex = -1;
        el.setAttribute('aria-hidden', 'true');
        el.innerHTML = `
            <div class="modal-dialog modal-dialog-centered">
                <div class="modal-content">
                    <div class="card-header d-flex justify-content-between align-items-center" style="background:transparent;">
                        <h5 class="mb-0 text-primary">
                            <i class="bi bi-qr-code text-primary me-2"></i>Patient Intake Link
                        </h5>
                        <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close"></button>
                    </div>
                    <div class="modal-body">
                        <p class="text-muted mb-3" style="font-size:14px;">
                            Scan this QR with the clinic tablet, or copy the link and open it in the tablet browser.
                            The patient will verify their last name, date of birth, and ZIP code.
                        </p>
                        <div id="piqrBody">
                            <div class="text-center text-muted py-4">
                                <div class="spinner-border spinner-border-sm me-2"></div> Loading...
                            </div>
                        </div>
                    </div>
                    <div class="modal-footer d-flex justify-content-between">
                        <button type="button" class="btn btn-outline-danger" id="piqrRotateBtn" disabled>
                            <i class="bi bi-arrow-clockwise me-1"></i> Rotate URL
                        </button>
                        <button type="button" class="btn btn-secondary" data-bs-dismiss="modal">Close</button>
                    </div>
                </div>
            </div>
        `;
        document.body.appendChild(el);
        el.querySelector('#piqrRotateBtn').addEventListener('click', handleRotate);
        return el;
    }

    function renderBody(url) {
        state.currentUrl = url || '';
        const body = document.getElementById('piqrBody');
        if (!body) return;
        body.innerHTML = `
            <div class="text-center mb-3">
                <div id="piqrQr" style="display:inline-block; padding:14px; background:#fff; border:1px solid #E2E8F0; border-radius:12px;"></div>
            </div>
            <label class="form-label fw-semibold" style="font-size:13px;">Tablet URL</label>
            <div class="input-group">
                <input type="text" class="form-control font-monospace" id="piqrUrlInput"
                       value="${esc(url)}" readonly style="font-size:12px;">
                <button class="btn btn-outline-primary" id="piqrCopyBtn" type="button">
                    <i class="bi bi-clipboard me-1"></i><span id="piqrCopyLabel">Copy</span>
                </button>
            </div>
            <div id="piqrCopyToast" class="text-success mt-2" style="font-size:12px; display:none;">
                <i class="bi bi-check-circle"></i> Copied to clipboard.
            </div>
        `;

        drawQr(url);

        document.getElementById('piqrCopyBtn').addEventListener('click', handleCopy);
        document.getElementById('piqrRotateBtn').disabled = false;
    }

    function drawQr(url) {
        const target = document.getElementById('piqrQr');
        if (!target) return;
        target.innerHTML = '';
        if (typeof window.qrcode !== 'function') {
            target.innerHTML = '<div class="text-danger small">QR library not loaded.</div>';
            return;
        }
        try {
            // typeNumber=0 -> auto-sized; error-correction "M" is a good default for URLs.
            const qr = window.qrcode(0, 'M');
            qr.addData(url);
            qr.make();
            // cellSize=6px, margin=2 cells. Produces a crisp ~200px code for typical URL lengths.
            target.innerHTML = qr.createSvgTag({ cellSize: 6, margin: 2, scalable: true });
            const svg = target.querySelector('svg');
            if (svg) { svg.style.width = '220px'; svg.style.height = '220px'; }
        } catch (e) {
            console.error('QR render failed', e);
            target.innerHTML = '<div class="text-danger small">Could not render QR code.</div>';
        }
    }

    function handleCopy() {
        const input = document.getElementById('piqrUrlInput');
        if (!input) return;
        const fallback = () => {
            input.select(); input.setSelectionRange(0, 99999);
            try { document.execCommand('copy'); } catch (_) {}
        };
        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(input.value).catch(fallback);
        } else {
            fallback();
        }
        const toast = document.getElementById('piqrCopyToast');
        const label = document.getElementById('piqrCopyLabel');
        if (toast) { toast.style.display = 'block'; setTimeout(() => { toast.style.display = 'none'; }, 2200); }
        if (label) { label.textContent = 'Copied!'; setTimeout(() => { label.textContent = 'Copy'; }, 2200); }
    }

    async function handleRotate() {
        if (!state.patientId) return;
        if (!confirm('This will invalidate the current link. Continue?')) return;

        const btn = document.getElementById('piqrRotateBtn');
        if (btn) { btn.disabled = true; btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span> Rotating...'; }

        try {
            const res = await fetch(`/api/clinic/patients/${state.patientId}/intake-token/rotate`, {
                method: 'POST',
                headers: Object.assign({ 'Content-Type': 'application/json' }, authHeaders())
            });
            if (!res.ok) throw new Error('rotate failed: ' + res.status);
            const data = await res.json();
            renderBody(data.url || '');
        } catch (e) {
            console.error(e);
            alert('Could not rotate the link. Please try again.');
        } finally {
            if (btn) { btn.disabled = false; btn.innerHTML = '<i class="bi bi-arrow-clockwise me-1"></i> Rotate URL'; }
        }
    }

    async function fetchTokenUrl(patientId) {
        const res = await fetch(`/api/clinic/patients/${patientId}/intake-token`, {
            headers: authHeaders()
        });
        if (!res.ok) throw new Error('fetch token failed: ' + res.status);
        return await res.json();
    }

    async function openQrModal(patientId) {
        if (!patientId) { console.warn('openQrModal: no patientId'); return; }
        state.patientId = patientId;
        state.currentUrl = '';

        const el = ensureModal();
        const body = el.querySelector('#piqrBody');
        if (body) {
            body.innerHTML = `<div class="text-center text-muted py-4">
                <div class="spinner-border spinner-border-sm me-2"></div> Loading...
            </div>`;
        }
        const rotateBtn = el.querySelector('#piqrRotateBtn');
        if (rotateBtn) rotateBtn.disabled = true;

        if (!window.bootstrap || !window.bootstrap.Modal) {
            alert('Could not open dialog. Please refresh the page.');
            return;
        }
        const modal = window.bootstrap.Modal.getOrCreateInstance(el);
        modal.show();

        try {
            const data = await fetchTokenUrl(patientId);
            renderBody(data.url || '');
        } catch (e) {
            console.error(e);
            if (body) body.innerHTML = `<div class="alert alert-danger">Could not load the intake link. Please try again.</div>`;
        }
    }

    window.ProfileIntakeQrButton = { openQrModal };
})();
