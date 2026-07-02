/**
 * TabletIntakeVerifyModule (Employee of Tablet)
 *
 * Why: pre-auth entry page on the tablet. No patient name shown (HIPAA-safer).
 * What:
 *   1. Extracts token from the root element data-attr (set server-side from URL).
 *   2. Validates token via GET /api/intake/p/{token}. 404 -> invalid-link screen.
 *   3. Renders LastName + DOB + ZipCode verify form (tablet-sized).
 *   4. Submits to POST /api/intake/p/{token}/verify.
 *   5. Success -> server sets verify cookie; redirects to /intake/p/{token}/wizard.
 *   6. 401 -> "Couldn't verify" error. 429 -> "Too many attempts" lockout.
 * Who calls: TabletEntry.cshtml (auto-init on DOMContentLoaded).
 */
(function () {
    'use strict';

    const ROOT_ID = 'tabletVerifyRoot';

    function esc(s) { return String(s == null ? '' : s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }

    function renderInvalid(root) {
        root.innerHTML = `
            <div class="tablet-verify-card">
                <div class="tablet-verify-lock" style="background:#FEE2E2;color:#991B1B;">
                    <i class="bi bi-exclamation-triangle"></i>
                </div>
                <h4>Invalid Link</h4>
                <p class="subtitle">This intake link is not valid or has been updated. Please ask the front desk for a new link.</p>
            </div>
        `;
    }

    function renderForm(root) {
        root.innerHTML = `
            <div class="tablet-verify-card">
                <div class="tablet-verify-lock"><i class="bi bi-shield-lock"></i></div>
                <h4>Patient Identity Verification</h4>
                <p class="subtitle">Please enter your last name, date of birth, and ZIP code to begin your intake.</p>

                <div class="tablet-verify-error" id="tvErr"></div>

                <form id="tvForm" autocomplete="off" novalidate>
                    <label for="tvLastName">Last Name</label>
                    <input type="text" class="form-control form-control-lg" id="tvLastName"
                           maxlength="100" autocapitalize="words" required>

                    <label for="tvDob">Date of Birth</label>
                    <input type="date" class="form-control form-control-lg" id="tvDob" required>

                    <label for="tvZip">ZIP Code</label>
                    <input type="text" class="form-control form-control-lg" id="tvZip"
                           maxlength="10" minlength="5" inputmode="numeric"
                           placeholder="12345" required>

                    <button type="submit" class="btn btn-primary btn-lg w-100" id="tvSubmit">
                        <i class="bi bi-shield-check me-1"></i> Verify &amp; Continue
                    </button>
                </form>

                <div class="tablet-verify-footer">
                    If you don't remember, please ask the front desk.
                </div>
            </div>
        `;
    }

    function showError(msg) {
        const e = document.getElementById('tvErr');
        if (!e) return;
        e.textContent = msg;
        e.style.display = 'block';
    }

    function clearError() {
        const e = document.getElementById('tvErr');
        if (e) { e.textContent = ''; e.style.display = 'none'; }
    }

    async function validateToken(token) {
        try {
            const res = await fetch(`/api/intake/p/${encodeURIComponent(token)}`);
            return res.ok;
        } catch (e) {
            console.error('token validation failed', e);
            return false;
        }
    }

    async function submitVerify(token, lastName, dob, zip) {
        const body = JSON.stringify({ lastName: lastName, dateOfBirth: dob, zipCode: zip });
        const res = await fetch(`/api/intake/p/${encodeURIComponent(token)}/verify`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body
        });
        return res;
    }

    async function init() {
        const root = document.getElementById(ROOT_ID);
        if (!root) return;

        const token = root.getAttribute('data-token');
        if (!token) {
            renderInvalid(root);
            return;
        }

        const ok = await validateToken(token);
        if (!ok) {
            renderInvalid(root);
            return;
        }

        renderForm(root);

        const form = document.getElementById('tvForm');
        form.addEventListener('submit', async (ev) => {
            ev.preventDefault();
            clearError();

            const lastName = (document.getElementById('tvLastName').value || '').trim();
            const dob = document.getElementById('tvDob').value;
            const zip = (document.getElementById('tvZip').value || '').trim();

            if (!lastName || !dob || zip.length < 5) {
                showError('Please enter your last name, date of birth, and ZIP code.');
                return;
            }

            const btn = document.getElementById('tvSubmit');
            btn.disabled = true;
            btn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span> Verifying...';

            try {
                const res = await submitVerify(token, lastName, dob, zip);

                if (res.ok) {
                    window.location.href = `/intake/p/${encodeURIComponent(token)}/wizard`;
                    return;
                }

                if (res.status === 429) {
                    let msg = 'Too many attempts. Please wait 15 minutes or see the front desk.';
                    try {
                        const data = await res.json();
                        if (data && data.message) msg = data.message;
                    } catch (_) {}
                    showError(msg);
                } else if (res.status === 401) {
                    showError("Couldn't verify. Please check your last name, date of birth, and ZIP code.");
                } else if (res.status === 404) {
                    renderInvalid(root);
                    return;
                } else {
                    showError('Something went wrong. Please try again or see the front desk.');
                }
            } catch (e) {
                console.error('verify failed', e);
                showError('Could not connect. Please check the connection and try again.');
            } finally {
                btn.disabled = false;
                btn.innerHTML = '<i class="bi bi-shield-check me-1"></i> Verify & Continue';
            }
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
