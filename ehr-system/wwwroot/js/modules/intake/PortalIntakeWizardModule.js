/**
 * PortalIntakeWizardModule (Employee of Portal)
 *
 * Why: host the intake wizard inside the patient portal (authed session).
 * What: mounts IntakeFormRenderer (the shared Contractor) into #intakeWizardRoot
 *       with portal-scoped save / submit / progress paths and JWT auth headers.
 *       Handles Thank You screen on final submit and "Save & Exit" back to dashboard.
 * Who calls: Intake.cshtml (auto-init on DOMContentLoaded).
 */
(function () {
    'use strict';

    const ROOT_ID = 'intakeWizardRoot';
    const AUTH_TOKEN_KEY = 'portalAuthToken';

    function authHeaders() {
        const token = localStorage.getItem(AUTH_TOKEN_KEY);
        return token ? { 'Authorization': `Bearer ${token}` } : {};
    }

    function showThankYou(root) {
        root.innerHTML = `
            <div class="text-center py-5">
                <div style="width:80px;height:80px;border-radius:50%;background:#DCFCE7;color:#16A34A;display:flex;align-items:center;justify-content:center;font-size:40px;margin:0 auto;">
                    <i class="bi bi-check-lg"></i>
                </div>
                <h3 class="mt-4">Thank you!</h3>
                <p class="text-muted mt-2">
                    Your intake has been saved. Your provider will review it before your visit.
                    You can update any section anytime from your dashboard.
                </p>
                <button type="button" class="btn btn-primary mt-3" id="intakeThankBack">
                    <i class="bi bi-house me-1"></i> Return to Dashboard
                </button>
            </div>
        `;
        document.getElementById('intakeThankBack').addEventListener('click', () => {
            window.location.href = '/Portal/Dashboard';
        });
    }

    async function init() {
        const root = document.getElementById(ROOT_ID);
        if (!root) return;

        // Wait briefly for auth token to land.
        await new Promise(r => setTimeout(r, 150));

        if (!localStorage.getItem(AUTH_TOKEN_KEY)) {
            // PatientPortalModule will redirect to login shortly. Show a light hint.
            root.innerHTML = `<div class="alert alert-warning">Please sign in to continue.</div>`;
            return;
        }

        if (typeof window.IntakeFormRenderer !== 'function') {
            root.innerHTML = `<div class="alert alert-danger">Intake form failed to load. Please refresh.</div>`;
            return;
        }

        const wizard = new window.IntakeFormRenderer({
            savePath: (name) => `/api/intake/section/${encodeURIComponent(name)}`,
            submitPath: '/api/intake/submit',
            progressPath: '/api/intake/progress',
            // One URL per wizard step (key from STEPS in IntakeFormRenderer).
            // Server dispatches by section name through IIntakePrefillService.
            prefillPath: (sectionKey) => `/api/intake/${encodeURIComponent(sectionKey)}/prefill`,
            authHeaders,
            onExit: () => { window.location.href = '/Portal/Dashboard'; },
            onComplete: () => showThankYou(root)
        });

        await wizard.mount(root);
        window._portalIntakeWizard = wizard;
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
