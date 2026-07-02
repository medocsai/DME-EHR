/**
 * HistoryActionModal — reusable Bootstrap modal for state changes + deletes
 * across the 7 History Review sections (Allergies, Medications, Problems,
 * Family History, Social History, Immunization, Supplements).
 *
 * Spec: rules/technical/history-review-soft-delete.md (section 7).
 *
 * Usage:
 *   HistoryActionModal.open({
 *     section: 'medication',
 *     action: 'discontinue',
 *     recordId: 123,
 *     recordLabel: 'Lisinopril 10mg PO daily',
 *     onConfirm: async (reason) => {
 *       await window.apiRequest('/history-review/medication/discontinue', {
 *         method: 'POST',
 *         body: { recordId: 123, reason }
 *       });
 *     }
 *   });
 *
 * Sections: 'allergy', 'medication', 'problem', 'familyHistory',
 *           'socialHistory', 'immunization', 'supplement'
 *
 * Actions:  'inactivate', 'reactivate', 'discontinue', 'revertDiscontinue',
 *           'markOnHold', 'resumeFromHold', 'complete', 'markResolved', 'delete'
 */
(function () {
    'use strict';

    const REASON_CONFIG = {
        inactivate: {
            title: 'Mark Inactive',
            icon: 'bi-pause-circle',
            iconClass: 'text-warning',
            description: 'Mark this record as no longer clinically active. It stays in the chart for reference.',
            confirmBtnLabel: 'Mark Inactive',
            confirmBtnClass: 'btn-primary',
            options: [
                'Resolved over time',
                'Outgrew (allergies / childhood)',
                'No longer applicable',
                'Other (specify)'
            ]
        },
        reactivate: {
            title: 'Reactivate',
            icon: 'bi-arrow-counterclockwise',
            iconClass: 'text-success',
            description: 'Mark this record as clinically active again.',
            confirmBtnLabel: 'Reactivate',
            confirmBtnClass: 'btn-primary',
            options: [
                'Recurred / new episode',
                'Was deactivated in error',
                'Patient self-resumed',
                'Other (specify)'
            ]
        },
        discontinue: {
            title: 'Discontinue Medication',
            icon: 'bi-x-circle',
            iconClass: 'text-warning',
            description: 'Stop this medication. It stays in the chart with status Discontinued.',
            confirmBtnLabel: 'Discontinue',
            confirmBtnClass: 'btn-primary',
            options: [
                'Side effect / adverse reaction',
                'No longer needed',
                'Replaced with alternative',
                'Patient stopped on own',
                'Course completed',
                'Other (specify)'
            ]
        },
        revertDiscontinue: {
            title: 'Revert Discontinue',
            icon: 'bi-arrow-counterclockwise',
            iconClass: 'text-success',
            description: 'Resume this previously discontinued medication.',
            confirmBtnLabel: 'Resume',
            confirmBtnClass: 'btn-primary',
            options: [
                'Resumed by specialist',
                'Patient self-resumed',
                'Was discontinued in error',
                'Other (specify)'
            ]
        },
        markOnHold: {
            title: 'Mark On Hold',
            icon: 'bi-pause-circle',
            iconClass: 'text-warning',
            description: 'Temporarily pause this medication.',
            confirmBtnLabel: 'Mark On Hold',
            confirmBtnClass: 'btn-primary',
            options: [
                'Procedure / surgery upcoming',
                'Acute illness',
                'Lab abnormality, monitoring',
                'Other (specify)'
            ]
        },
        resumeFromHold: {
            title: 'Resume from Hold',
            icon: 'bi-play-circle',
            iconClass: 'text-success',
            description: 'Return this medication to active status.',
            confirmBtnLabel: 'Resume',
            confirmBtnClass: 'btn-primary',
            options: [
                'Procedure complete',
                'Acute issue resolved',
                'Labs normalized',
                'Other (specify)'
            ]
        },
        complete: {
            title: 'Complete Course',
            icon: 'bi-check-circle',
            iconClass: 'text-success',
            description: 'Mark this medication course as completed.',
            confirmBtnLabel: 'Complete',
            confirmBtnClass: 'btn-primary',
            options: [
                'Full course taken as prescribed',
                'Other (specify)'
            ]
        },
        markResolved: {
            title: 'Mark Resolved',
            icon: 'bi-check-circle',
            iconClass: 'text-success',
            description: 'Mark this problem as resolved. It stays in the chart as past medical history.',
            confirmBtnLabel: 'Mark Resolved',
            confirmBtnClass: 'btn-primary',
            options: [
                'Resolved with treatment',
                'Resolved without treatment',
                'Surgery / procedure addressed',
                'Other (specify)'
            ]
        },
        delete: {
            title: 'Delete Record',
            icon: 'bi-trash',
            iconClass: 'text-danger',
            description: 'Remove this record from the chart. It will be hidden from all views but preserved in the audit log.',
            confirmBtnLabel: 'Delete',
            confirmBtnClass: 'btn-danger',
            options: [
                'Mistakenly entered',
                'Wrong patient',
                'Duplicate entry',
                'Patient denies / never had this',
                'Other (specify)'
            ]
        }
    };

    let _modalEl = null;
    let _modalInstance = null;
    let _currentCtx = null;

    function _el(id) { return document.getElementById(id); }

    function _initModal() {
        if (_modalEl) return _modalEl;
        _modalEl = _el('historyActionModal');
        if (!_modalEl) {
            console.error('[HistoryActionModal] #historyActionModal not found in DOM. Ensure _ModalsHistoryReview partial is rendered.');
            return null;
        }
        _modalInstance = bootstrap.Modal.getOrCreateInstance(_modalEl);

        // Reason select change -> show/hide Other textarea
        const reasonSelect = _el('historyActionReason');
        const otherWrap = _el('historyActionReasonOtherWrap');
        const otherText = _el('historyActionReasonOther');
        reasonSelect.addEventListener('change', () => {
            const isOther = (reasonSelect.value || '').toLowerCase().includes('other');
            otherWrap.style.display = isOther ? '' : 'none';
            if (!isOther) {
                otherText.value = '';
            } else {
                setTimeout(() => otherText.focus(), 50);
            }
            _clearError();
        });

        // Confirm button click
        const confirmBtn = _el('historyActionConfirmBtn');
        confirmBtn.addEventListener('click', _onConfirmClick);

        return _modalEl;
    }

    function _clearError() {
        const errEl = _el('historyActionModalError');
        if (errEl) {
            errEl.classList.add('d-none');
            errEl.textContent = '';
        }
    }

    function _showError(msg) {
        const errEl = _el('historyActionModalError');
        if (errEl) {
            errEl.classList.remove('d-none');
            errEl.textContent = msg;
        }
    }

    function _setBusy(busy) {
        const btn = _el('historyActionConfirmBtn');
        const spinner = _el('historyActionConfirmSpinner');
        if (!btn) return;
        btn.disabled = busy;
        if (spinner) spinner.classList.toggle('d-none', !busy);
    }

    async function _onConfirmClick() {
        if (!_currentCtx) return;
        _clearError();

        const reasonSelect = _el('historyActionReason');
        const otherText = _el('historyActionReasonOther');

        const selected = reasonSelect.value || '';
        if (!selected) {
            _showError('Please select a reason.');
            return;
        }

        let reasonText = selected;
        if (selected.toLowerCase().includes('other')) {
            const otherVal = (otherText.value || '').trim();
            if (!otherVal) {
                _showError('Please specify the reason.');
                return;
            }
            reasonText = otherVal;
        }

        _setBusy(true);
        try {
            await _currentCtx.onConfirm(reasonText);
            _modalInstance.hide();
        } catch (err) {
            console.error('[HistoryActionModal] onConfirm failed', err);
            _showError((err && err.message) || 'Action failed. Please try again.');
        } finally {
            _setBusy(false);
        }
    }

    /**
     * Open the modal.
     *
     * @param {Object} ctx
     * @param {string} ctx.section - one of: allergy, medication, problem, familyHistory, socialHistory, immunization, supplement
     * @param {string} ctx.action - one of REASON_CONFIG keys (inactivate, reactivate, discontinue, etc.)
     * @param {number} ctx.recordId - DB id of the record being acted on
     * @param {string} ctx.recordLabel - human-readable label shown in the modal (e.g. "Lisinopril 10mg")
     * @param {Function} ctx.onConfirm - async callback receiving the reason text. Caller hits the API and resolves/rejects.
     */
    function open(ctx) {
        if (!ctx || !ctx.action || !REASON_CONFIG[ctx.action]) {
            console.error('[HistoryActionModal] Unknown action:', ctx && ctx.action);
            return;
        }
        if (typeof ctx.onConfirm !== 'function') {
            console.error('[HistoryActionModal] ctx.onConfirm must be a function');
            return;
        }
        if (!_initModal()) return;

        const cfg = REASON_CONFIG[ctx.action];
        _currentCtx = ctx;

        // Title + icon
        const iconEl = _el('historyActionModalIcon');
        if (iconEl) {
            iconEl.className = `bi ${cfg.icon} ${cfg.iconClass} me-2`;
        }
        const titleEl = _el('historyActionModalTitle');
        if (titleEl) titleEl.textContent = cfg.title;

        // Description
        const descEl = _el('historyActionModalDescription');
        if (descEl) descEl.textContent = cfg.description;

        // Record label
        const recWrap = _el('historyActionModalRecordWrap');
        const recLabel = _el('historyActionModalRecordLabel');
        if (ctx.recordLabel && recLabel) {
            recLabel.textContent = ctx.recordLabel;
            recWrap.style.display = '';
        } else if (recWrap) {
            recWrap.style.display = 'none';
        }

        // Reason options
        const reasonSelect = _el('historyActionReason');
        reasonSelect.innerHTML = '<option value="">Select a reason...</option>' +
            cfg.options.map(opt => `<option value="${_escape(opt)}">${_escape(opt)}</option>`).join('');
        reasonSelect.value = '';

        // Other textarea
        _el('historyActionReasonOtherWrap').style.display = 'none';
        _el('historyActionReasonOther').value = '';

        // Confirm button label + class
        const confirmBtn = _el('historyActionConfirmBtn');
        const confirmLabel = _el('historyActionConfirmLabel');
        if (confirmLabel) confirmLabel.textContent = cfg.confirmBtnLabel;
        confirmBtn.className = `btn ${cfg.confirmBtnClass}`;

        _clearError();
        _setBusy(false);

        _modalInstance.show();
    }

    function _escape(s) {
        return String(s).replace(/[&<>"']/g, c => ({
            '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
        }[c]));
    }

    // Public API
    window.HistoryActionModal = {
        open,
        REASON_CONFIG  // exposed for any page that wants to peek at action metadata (e.g. button labels)
    };
})();
