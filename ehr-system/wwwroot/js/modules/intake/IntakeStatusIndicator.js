/**
 * IntakeStatusIndicator
 *
 * WHY:           Render patient intake form status consistently wherever a patient name appears.
 *                Visual style, text format, and click behavior live in ONE place so they never
 *                drift across screens.
 * WHO CALLS ME:  DashboardModule (Today Appointments)
 *                CareEpisodeModule (appointment rows)
 *                CalendarModule (FullCalendar event card render hook)
 *                PatientRenderer (Patient Intake sidebar label inside patient details modal)
 * WHAT I RETURN: HTML string, ready to insert.
 * HOW TO HIRE:   IntakeStatusIndicator.render({ patientId, intakeStatus, context })
 *
 *   patientId    - int, required for click behavior (open patient profile, intake tab).
 *   intakeStatus - { Status, SubmittedAt, LastEditedAt } as returned by IntakeStatusHelper.cs.
 *                  Status: 0=NotSubmitted, 1=InProgress, 2=Submitted.
 *   context      - 'compact' (Dashboard, CareEpisode list)
 *                  'calendar-event' (FullCalendar event card)
 *                  'sidebar-label' (plain text inside patient profile sidebar nav-link)
 *
 * SPEC:          rules/technical/intake-status-indicator.md
 */
window.IntakeStatusIndicator = (function () {
    'use strict';

    const STATE = {
        NOT_SUBMITTED: 0,
        IN_PROGRESS: 1,
        SUBMITTED: 2
    };

    function _resolve(intakeStatus) {
        const s = (intakeStatus && typeof intakeStatus.Status === 'number') ? intakeStatus.Status : STATE.NOT_SUBMITTED;
        if (s === STATE.SUBMITTED) {
            return { cls: 'done', icon: 'bi-clipboard-check', label: 'Intake submitted', short: 'Submitted', textCls: 'text-success' };
        }
        if (s === STATE.IN_PROGRESS) {
            return { cls: 'partial', icon: 'bi-clipboard-minus', label: 'Intake in progress', short: 'In Progress', textCls: 'intake-text-partial' };
        }
        return { cls: 'empty', icon: 'bi-clipboard-x', label: 'Intake not submitted', short: 'Not Submitted', textCls: 'text-danger' };
    }

    function _tooltip(view, intakeStatus) {
        if (!intakeStatus) return view.label;
        if (intakeStatus.Status === STATE.SUBMITTED && intakeStatus.SubmittedAt) {
            return `${view.label} on ${_formatLocalDate(intakeStatus.SubmittedAt)}`;
        }
        if (intakeStatus.Status === STATE.IN_PROGRESS && intakeStatus.LastEditedAt) {
            return `${view.label}, last edited ${_formatLocalDate(intakeStatus.LastEditedAt)}`;
        }
        return view.label;
    }

    /**
     * Format a UTC datetime as a date in the clinic location's timezone.
     * Falls back to user's primary location IANA tz if available; never uses raw browser
     * default timezone alone (per CLAUDE.md timezone rule).
     */
    function _formatLocalDate(utc) {
        if (!utc) return '';
        try {
            const tz = (window.App && window.App.state && window.App.state.get && window.App.state.get('locationTimeZoneId')) || undefined;
            const d = new Date(utc);
            return new Intl.DateTimeFormat('en-US', {
                year: 'numeric', month: 'short', day: 'numeric',
                hour: 'numeric', minute: '2-digit',
                timeZone: tz
            }).format(d);
        } catch (e) {
            return new Date(utc).toLocaleString();
        }
    }

    function render(opts) {
        opts = opts || {};
        const patientId = opts.patientId;
        const intakeStatus = opts.intakeStatus;
        const context = opts.context || 'compact';
        const view = _resolve(intakeStatus);
        const tooltip = _tooltip(view, intakeStatus);

        // Click handler — opens patient profile modal, then switches to Patient Intake tab.
        // Uses inline onclick so it works inside any list / card / FullCalendar event.
        const onclick = patientId
            ? `event.stopPropagation(); window.IntakeStatusIndicator.openIntakeTab(${patientId}); return false;`
            : '';

        if (context === 'sidebar-label') {
            // Plain text inside the Patient Intake sidebar nav-link. No chip styling, no click handler
            // (the nav-link itself already opens the tab).
            return `<span class="intake-sidebar-label ${view.textCls} fw-semibold ms-2">${view.short}</span>`;
        }

        if (context === 'calendar-event') {
            // Tight strip used inside FullCalendar event card. Inline-flex so it sits as wide as the text.
            return `<div class="ce-intake ${view.cls}" title="${_escAttr(tooltip)}" onclick="${onclick}" role="button">
                        <i class="bi ${view.icon}"></i><span>${view.label}</span>
                    </div>`;
        }

        // 'compact' — Dashboard + CareEpisode list
        return `<span class="intake-info ${view.cls}" title="${_escAttr(tooltip)}" onclick="${onclick}" role="button">
                    <i class="bi ${view.icon}"></i> ${view.label}
                </span>`;
    }

    /**
     * Open the Patient Details modal and auto-switch to the Patient Intake tab.
     * Uses the existing global viewPatient(id) which already loads the modal,
     * then listens for shown.bs.modal once and clicks the intake tab.
     */
    function openIntakeTab(patientId) {
        if (!patientId) return;
        const modalEl = document.getElementById('patientDetailModal');
        if (modalEl) {
            const onShown = function () {
                modalEl.removeEventListener('shown.bs.modal', onShown);
                const link = document.querySelector('#patientDetailModal a[href="#patientIntake"]');
                if (link && typeof link.click === 'function') {
                    link.click();
                }
            };
            modalEl.addEventListener('shown.bs.modal', onShown);
        }
        if (typeof window.viewPatient === 'function') {
            window.viewPatient(patientId);
        }
    }

    function _escAttr(s) {
        return String(s == null ? '' : s).replace(/"/g, '&quot;').replace(/</g, '&lt;');
    }

    return { render: render, openIntakeTab: openIntakeTab, STATE: STATE };
})();
