/**
 * TabletIntakeWizardModule (Employee of Tablet)
 *
 * Why: host the intake wizard on the tablet after DOB/SSN verify.
 *      Reuses the shared IntakeFormRenderer Contractor with tablet-scoped paths.
 * What:
 *   1. Reads token from #intakeWizardRoot data-token.
 *   2. Calls GET /api/intake/p/{token}/progress to confirm verify cookie is still valid.
 *      401 -> redirect back to /intake/p/{token} (verify page).
 *   3. Mounts IntakeFormRenderer with:
 *        savePath:    (name) => /api/intake/p/{token}/section/{name}
 *        submitPath:  /api/intake/p/{token}/submit
 *        progressPath:/api/intake/p/{token}/progress
 *      Exits / Thank You redirect to /Portal/Login (prevents the next patient
 *      from going back via browser history).
 *   4. Shows "Hi, {FirstName}" top-right once progress response (or verify) gives us a name.
 *      Post-verify only — pre-auth page never showed the name.
 * Who calls: TabletWizard.cshtml (auto-init on DOMContentLoaded).
 */
(function () {
    'use strict';

    const ROOT_ID = 'intakeWizardRoot';

    /**
     * Decide where the wizard exits to. When the patient came in via the
     * consent → intake handoff (URL has ?return=kiosk), we send them back to
     * the kiosk. If the handoff also carried &kt=<locationKioskToken> we
     * return directly to /Kiosk?token=<kt>&app=1 — the same stable kiosk URL
     * the patient came in on — so staff don't have to re-sign-in / re-pick
     * location. If kt is missing (older link, location has kiosk disabled,
     * etc.) we fall back to /KioskSetup?app=1 which still works.
     *
     * Front-desk QR flow (no ?return=kiosk) keeps the original /Portal/Login.
     * See rules/technical/consent-to-intake-handoff.md.
     */
    function getReturnUrl() {
        try {
            const params = new URLSearchParams(window.location.search);
            if (params.get('return') === 'kiosk') {
                const kt = params.get('kt');
                if (kt) {
                    return '/Kiosk?token=' + encodeURIComponent(kt) + '&app=1';
                }
                return '/KioskSetup?app=1';
            }
        } catch (e) {
            // URLSearchParams not available — fall through to default.
        }
        return '/Portal/Login';
    }

    function showThankYou(root, returnUrl) {
        const isKioskReturn = returnUrl.indexOf('/Kiosk') === 0;
        const destLabel = isKioskReturn ? 'kiosk' : 'Portal login';
        root.innerHTML = `
            <div class="tablet-thankyou">
                <div class="check"><i class="bi bi-check-lg"></i></div>
                <h3>Thank you!</h3>
                <p class="text-muted mt-2">
                    Your intake is saved. Please hand the tablet back to the front desk.
                </p>
                <div class="text-muted mt-4" style="font-size:12px">
                    Redirecting to ${destLabel} in <span id="tabletRedirectCount">3</span> seconds...
                </div>
            </div>
        `;
        let n = 3;
        const span = document.getElementById('tabletRedirectCount');
        const timer = setInterval(() => {
            n -= 1;
            if (span) span.textContent = String(n);
            if (n <= 0) {
                clearInterval(timer);
                window.location.href = returnUrl;
            }
        }, 1000);
    }

    function setHello(firstName) {
        const el = document.getElementById('tabletHello');
        if (!el) return;
        if (firstName && String(firstName).trim()) {
            el.textContent = 'Hi, ' + String(firstName).trim();
        }
    }

    async function init() {
        const root = document.getElementById(ROOT_ID);
        if (!root) return;

        const token = root.getAttribute('data-token');
        if (!token) {
            window.location.href = '/';
            return;
        }

        const progressPath = `/api/intake/p/${encodeURIComponent(token)}/progress`;

        // Guard: verify cookie still valid?
        try {
            const res = await fetch(progressPath);
            if (res.status === 401) {
                window.location.href = `/intake/p/${encodeURIComponent(token)}`;
                return;
            }
            if (!res.ok) {
                root.innerHTML = `<div class="alert alert-danger m-4">Intake form failed to load. Please see the front desk.</div>`;
                return;
            }
            const progress = await res.json();
            if (progress && progress.firstName) setHello(progress.firstName);
        } catch (e) {
            console.error('progress check failed', e);
            root.innerHTML = `<div class="alert alert-danger m-4">Intake form failed to load. Please see the front desk.</div>`;
            return;
        }

        if (typeof window.IntakeFormRenderer !== 'function') {
            root.innerHTML = `<div class="alert alert-danger m-4">Intake form failed to load. Please refresh.</div>`;
            return;
        }

        // Read ?return=kiosk once at init. The wizard is a client-side SPA
        // (IntakeFormRenderer._attemptNav doesn't change window.location), so
        // the original query string is preserved automatically until exit.
        const returnUrl = getReturnUrl();

        const wizard = new window.IntakeFormRenderer({
            savePath: (name) => `/api/intake/p/${encodeURIComponent(token)}/section/${encodeURIComponent(name)}`,
            submitPath: `/api/intake/p/${encodeURIComponent(token)}/submit`,
            progressPath,
            // Tablet prefill: same dispatch pattern as portal, but token-scoped.
            // Server resolves patientId from the verify cookie, not JWT.
            prefillPath: (sectionKey) => `/api/intake/p/${encodeURIComponent(token)}/${encodeURIComponent(sectionKey)}/prefill`,
            // Tablet has no JWT; cookie-based verification handled server-side.
            authHeaders: () => ({}),
            onExit: () => { window.location.href = returnUrl; },
            onComplete: () => showThankYou(root, returnUrl)
        });

        await wizard.mount(root);

        // If progress exposed firstName, already shown. Otherwise leave blank (don't fabricate).
        window._tabletIntakeWizard = wizard;
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
