/**
 * ClinicIntakeFrameModule (Employee of Encounter / Clinic-side intake)
 *
 * Why: host the existing intake wizard inside the encounter for clinic staff.
 *      Mirror of TabletIntakeWizardModule, configured with staff endpoints.
 * What:
 *   1. Reads patientId from #intakeWizardRoot data-patient-id (set by ClinicFrame.cshtml).
 *   2. Mounts IntakeFormRenderer with:
 *        savePath:    (name) => /api/clinic/patients/{id}/intake/section/{name}
 *        progressPath:         /api/clinic/patients/{id}/intake/progress
 *        prefillPath: (name) => /api/clinic/patients/{id}/intake/{name}/prefill
 *      No submitPath (staff editing is not a "submission" event — server has no /submit endpoint
 *      for the clinic group; the renderer's onComplete should be a no-op here).
 *   3. Auth is the staff JWT/cookie carried by the parent session — no token, no verify cookie.
 * Who calls: ClinicFrame.cshtml (auto-init on DOMContentLoaded).
 *
 * Spec: rules/technical/intake-on-clinical-note.md §3, §4.3.
 */
(function () {
    'use strict';

    const ROOT_ID = 'intakeWizardRoot';

    /**
     * Auth: this iframe shares localStorage with the parent encounter page
     * (same origin = localhost:5002 / production host). The parent already
     * stored the JWT under the 'authToken' key via AuthService.js. We read it
     * here and attach it as a Bearer header to every API call — both the
     * initial progress sanity check and every fetch the wizard renderer makes
     * (via the authHeaders callback).
     *
     * If the parent's session expired the API will return 401; we surface a
     * "session expired" message rather than silently failing.
     */
    function getAuthHeaders() {
        const token = localStorage.getItem('authToken');
        return token ? { 'Authorization': `Bearer ${token}` } : {};
    }

    async function init() {
        const root = document.getElementById(ROOT_ID);
        if (!root) return;

        const patientId = root.getAttribute('data-patient-id');
        if (!patientId || patientId === '0') {
            root.innerHTML = `<div class="alert alert-danger m-4">Missing patient context. Please reload.</div>`;
            return;
        }

        const idEnc = encodeURIComponent(patientId);
        const progressPath = `/api/clinic/patients/${idEnc}/intake/progress`;

        // Sanity check: confirm staff JWT is valid for this patient before mounting.
        try {
            const res = await fetch(progressPath, {
                credentials: 'same-origin',
                headers: getAuthHeaders()
            });
            if (res.status === 401) {
                root.innerHTML = `<div class="alert alert-warning m-4">Your session has expired. Please refresh the encounter.</div>`;
                return;
            }
            if (res.status === 403) {
                root.innerHTML = `<div class="alert alert-warning m-4">You do not have access to this patient's intake.</div>`;
                return;
            }
            if (!res.ok) {
                root.innerHTML = `<div class="alert alert-danger m-4">Intake form failed to load. Please refresh.</div>`;
                return;
            }
        } catch (e) {
            console.error('[ClinicIntakeFrame] progress check failed', e);
            root.innerHTML = `<div class="alert alert-danger m-4">Intake form failed to load. Please refresh.</div>`;
            return;
        }

        if (typeof window.IntakeFormRenderer !== 'function') {
            root.innerHTML = `<div class="alert alert-danger m-4">Intake form failed to load. Please refresh.</div>`;
            return;
        }

        const wizard = new window.IntakeFormRenderer({
            savePath: (name) => `/api/clinic/patients/${idEnc}/intake/section/${encodeURIComponent(name)}`,
            // Staff editing is not a submission event. No /submit endpoint exists in the
            // clinic group; if the renderer ever calls submitPath we just bounce to progress.
            submitPath: progressPath,
            progressPath,
            prefillPath: (sectionKey) => `/api/clinic/patients/${idEnc}/intake/${encodeURIComponent(sectionKey)}/prefill`,
            // JWT bearer auth — token read from same-origin localStorage on every call
            // so it stays current even if the parent refreshes the token mid-session.
            authHeaders: getAuthHeaders,
            // No "Save & Exit" destination from inside an iframe; renderer should hide
            // the exit affordance. If it still calls onExit we no-op.
            onExit: () => { /* no-op inside iframe */ },
            // Staff doesn't "complete" intake; if renderer calls onComplete just stay put.
            onComplete: () => { /* no-op inside iframe */ }
        });

        await wizard.mount(root);
        window._clinicIntakeWizard = wizard;
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
