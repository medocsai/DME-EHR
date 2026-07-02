/**
 * IntakeReviewRenderer (Employee of IntakeFormRenderer)
 * Why: placeholder Employee for the Review step. IntakeFormRenderer renders
 *      the review UI inline (since it owns the step list and progress), so
 *      this class exists only to satisfy the 8-renderer roster and ensure
 *      nothing breaks if future code looks it up by key.
 * Who calls: reserved.
 * Returns: no-op.
 */
(function () {
    'use strict';

    class IntakeReviewRenderer {
        constructor() { this.hint = ''; }
        render(container) {
            if (container) container.innerHTML = '';
        }
        getPayload() { return null; }
    }

    window.IntakeReviewRenderer = IntakeReviewRenderer;
})();
