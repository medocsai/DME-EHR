/**
 * PortalIntakeDashboardCard (Employee of Portal Dashboard)
 *
 * Why: show the "Complete Your Intake Form" card on the portal dashboard.
 * What: fetches /api/intake/progress, renders a compact inline card matching
 *       the Book Appointment / Upload Documents banners. Patient clicks the
 *       card to open the wizard. No dismiss — intake reminder stays until
 *       the form is actually submitted (per Hammas 2026-04-24).
 * Who calls: Dashboard.cshtml mounts via #portalIntakeCardRoot.
 * Returns: DOM rendered into that root.
 */
(function () {
    'use strict';

    const ROOT_ID = 'portalIntakeCardRoot';
    const AUTH_TOKEN_KEY = 'portalAuthToken';

    async function fetchProgress() {
        const token = localStorage.getItem(AUTH_TOKEN_KEY);
        if (!token) return null;
        try {
            const res = await fetch('/api/intake/progress', {
                headers: { 'Authorization': `Bearer ${token}` }
            });
            if (!res.ok) return null;
            const raw = await res.json();
            // API serializes with PropertyNamingPolicy=null (PascalCase).
            // Normalize so the rest of this module can read camelCase.
            return {
                completed: raw.Completed ?? raw.completed ?? 0,
                total: raw.Total ?? raw.total ?? 0,
                submittedAt: raw.SubmittedAt ?? raw.submittedAt ?? null
            };
        } catch {
            return null;
        }
    }

    function buttonLabel(progress) {
        if (progress.submittedAt) return { label: 'Review / Update', icon: 'bi-pencil' };
        if (!progress.completed) return { label: 'Start Intake', icon: 'bi-pencil-square' };
        return { label: 'Resume Intake', icon: 'bi-arrow-right-circle' };
    }

    function stateLine(progress) {
        if (progress.submittedAt) return 'Submitted';
        if (!progress.completed) return 'Not started';
        if (progress.completed >= (progress.total || 0)) return 'Ready to submit';
        return 'In progress';
    }

    function hideColumn() {
        const col = document.getElementById('portalIntakeCardCol');
        if (col) col.classList.add('d-none');
        const root = document.getElementById(ROOT_ID);
        if (root) root.innerHTML = '';
    }

    function render(progress) {
        const root = document.getElementById(ROOT_ID);
        if (!root) return;

        // Hide if the patient has already submitted — no need to pester them.
        if (!progress || progress.submittedAt) {
            hideColumn();
            return;
        }

        const total = progress.total || 8;
        const completed = progress.completed || 0;
        const pct = total > 0 ? Math.round((completed / total) * 100) : 0;
        const state = stateLine(progress);
        const firstTime = !progress.completed;

        // Compact inline-banner style matching Book Appointment + Upload Documents.
        // Whole card is clickable, navigates to /Portal/Intake. No dismiss button.
        root.innerHTML = `
            <div class="card portal-card h-100" style="cursor: pointer; border-color: var(--portal-primary, #1B72BE); border-width: 1.5px;" id="intakeCardCta">
                <div class="card-body py-3">
                    <div class="d-flex align-items-center gap-3 mb-2">
                        <div style="width:44px;height:44px;border-radius:10px;background:#DBEAFE;color:#1B72BE;display:flex;align-items:center;justify-content:center;font-size:20px;flex-shrink:0;">
                            <i class="bi bi-clipboard-plus"></i>
                        </div>
                        <div class="flex-grow-1">
                            <div class="fw-semibold">Complete Your Intake Form</div>
                            <div class="text-muted small">${state}${firstTime ? '' : ` · ${completed} of ${total}`}</div>
                        </div>
                        <i class="bi bi-chevron-right text-muted"></i>
                    </div>
                    ${firstTime ? '' : `
                    <div class="progress" style="height:4px;">
                        <div class="progress-bar" role="progressbar" style="width:${pct}%;background:#1B72BE;" aria-valuenow="${pct}" aria-valuemin="0" aria-valuemax="100"></div>
                    </div>`}
                </div>
            </div>
        `;

        document.getElementById('intakeCardCta').addEventListener('click', () => {
            window.location.href = '/Portal/Intake';
        });
    }

    async function init() {
        const root = document.getElementById(ROOT_ID);
        if (!root) return;
        const progress = await fetchProgress();
        render(progress);
    }

    function boot() {
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', () => setTimeout(init, 200));
        } else {
            setTimeout(init, 200);
        }
    }

    boot();

    window.PortalIntakeDashboardCard = { refresh: init };
})();
