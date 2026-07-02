/**
 * BookingReasonStep (Hired Guy)
 *
 * Why: optional free-text reason. Visible only on the final step. Step always
 *      validates true since the field is optional.
 * What: textarea + privacy hint.
 * Who calls: PortalBookingWizard.
 */
(function () {
    'use strict';

    function escapeAttr(s) {
        return String(s == null ? '' : s).replace(/"/g, '&quot;');
    }

    class BookingReasonStep {
        constructor(state) { this.state = state; this.container = null; }

        render(container) {
            this.container = container;
            const reason = this.state.get().reason || '';
            container.innerHTML = `
                <h3 class="pbw-step-title">Anything we should know?
                    <span class="pbw-step-optional">optional</span>
                </h3>
                <div class="pbw-step-lede">Let your care team know what brings you in. This helps them prepare.</div>
                <textarea class="pbw-reason" id="pbwReasonInput" rows="5"
                          placeholder="For example: annual check-up, follow-up on lab results, new concern about...">${reason ? escapeAttr(reason) : ''}</textarea>
                <div class="pbw-reason-hint"><i class="bi bi-shield-check"></i> Only your care team will see this.</div>`;

            const input = container.querySelector('#pbwReasonInput');
            input?.addEventListener('input', () => {
                this.state.patch({ reason: input.value.trim() });
            });
        }

        validate() { return true; }

        cleanup() { /* No external listeners */ }
    }

    window.PortalBookingReasonStep = BookingReasonStep;
})();
