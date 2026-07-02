/**
 * PortalBookingWizard (Contractor)
 *
 * Why: orchestrate a 4-step booking flow on the patient portal:
 *      1) Visit type    2) Provider    3) Date + Time    4) Reason -> Book
 *      The contractor knows the order of steps, manages navigation, validates each
 *      step before moving forward, and submits the final booking. It does NOT know
 *      how each step renders or fetches data — that's each step's job.
 *
 * What: mount(rootEl) paints the wizard shell (page header, step indicator, body
 *      slot, footer with Back/Continue, sidebar slot for desktop, mobile bottom
 *      strip), then renders step 1.
 *
 * Who calls: PatientPortalModule.loadBookingPage() — when /Portal/Booking loads.
 *
 * Single-provider tenants: if /api/portal/providers returns exactly one provider,
 *      step 2 is auto-completed (that provider is selected for them) and skipped
 *      in both directions of navigation. The sidebar still shows their name.
 */
(function () {
    'use strict';

    function escapeHtml(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, c => ({
            '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
        }[c]));
    }

    function showOverlayToast(title, message, type) {
        // Reuse the portal toast plumbing if present.
        if (typeof window.showPortalToast === 'function') {
            window.showPortalToast(title, message, type);
            return;
        }
        // Fallback: bootstrap toast
        const wrap = document.querySelector('.toast-container') || document.body;
        const id = 'pbw-toast-' + Date.now();
        const html = `
            <div id="${id}" class="toast" role="alert" data-bs-delay="3500">
                <div class="toast-header">
                    <strong class="me-auto">${escapeHtml(title || 'Notice')}</strong>
                    <button type="button" class="btn-close" data-bs-dismiss="toast"></button>
                </div>
                <div class="toast-body">${escapeHtml(message || '')}</div>
            </div>`;
        wrap.insertAdjacentHTML('beforeend', html);
        const el = document.getElementById(id);
        if (window.bootstrap && el) {
            new window.bootstrap.Toast(el).show();
            el.addEventListener('hidden.bs.toast', () => el.remove());
        }
    }

    class PortalBookingWizard {
        constructor() {
            this.root = null;
            this.state = null;
            this.api = null;
            this.summary = null;
            this.steps = [];                  // [{ key, instance, skip:bool }]
            this._currentStepIndex = 0;
            this._submitting = false;
        }

        async mount(root) {
            this.root = root;
            this.state = new window.PortalBookingState();
            this.api = new window.PortalBookingApiClient();
            this.summary = new window.PortalBookingSummaryRenderer();

            // Build step list. Step 2 may auto-skip after providers load.
            this.steps = [
                { key: 'type',     instance: new window.PortalBookingVisitTypeStep(this.state),                            skip: false },
                { key: 'provider', instance: new window.PortalBookingProviderStep(this.state, this.api, {
                    onLoaded: (providers) => this._onProvidersLoaded(providers)
                }),                                                                                                       skip: false },
                { key: 'datetime', instance: new window.PortalBookingDateTimeStep(this.state, this.api),                  skip: false },
                { key: 'reason',   instance: new window.PortalBookingReasonStep(this.state),                              skip: false }
            ];

            this._renderShell();
            this.summary.mount(
                this.root.querySelector('[data-pbw-sidebar]'),
                this.root.querySelector('[data-pbw-mobile]'),
                this.state,
                { totalSteps: 4, stepKeys: this.steps.map(s => s.key) }
            );

            // Initial paint: step 1
            await this._renderCurrentStep();
        }

        _renderShell() {
            this.root.innerHTML = `
                <div class="pbw-page">
                    <a href="/Portal/Dashboard" class="pbw-back-link"><i class="bi bi-chevron-left"></i> Back to Dashboard</a>
                    <h2 class="pbw-page-title"><i class="bi bi-calendar-plus pbw-page-icon"></i> Book an Appointment</h2>
                    <div class="pbw-page-sub">A few quick questions, then you're done</div>

                    <div class="pbw-grid">
                        <div class="pbw-main">
                            <div class="pbw-steps-bar" id="pbwStepsBar">
                                ${this._renderStepBar()}
                            </div>

                            <div class="pbw-card">
                                <div class="pbw-body" id="pbwBody"></div>
                                <div class="pbw-footer">
                                    <button type="button" class="pbw-btn pbw-btn-back" data-action="back" disabled>
                                        <i class="bi bi-arrow-left"></i> Back
                                    </button>
                                    <button type="button" class="pbw-btn pbw-btn-primary" data-action="next">
                                        Continue <i class="bi bi-arrow-right"></i>
                                    </button>
                                </div>
                            </div>
                        </div>
                        <aside class="pbw-sidebar" data-pbw-sidebar></aside>
                    </div>
                </div>
                <div class="pbw-mobile-summary" data-pbw-mobile></div>`;

            // Bind footer buttons
            this.root.querySelector('[data-action="back"]').addEventListener('click', () => this._prev());
            this.root.querySelector('[data-action="next"]').addEventListener('click', () => this._next());

            // Bind step indicator clicks (only allowed for completed steps)
            this.root.querySelectorAll('.pbw-step-item').forEach(el => {
                el.addEventListener('click', () => {
                    const idx = parseInt(el.dataset.stepIndex, 10);
                    if (isNaN(idx) || idx >= this._currentStepIndex) return;
                    this._goTo(idx);
                });
            });
        }

        _renderStepBar() {
            const labels = ['Visit Type', 'Provider', 'Date & Time', 'Reason'];
            const dots = labels.map((label, idx) => {
                const cls = ['pbw-step-item'];
                if (idx === this._currentStepIndex) cls.push('active');
                if (idx < this._currentStepIndex) cls.push('completed');
                if (idx > this._currentStepIndex) cls.push('locked');
                if (this.steps[idx]?.skip) cls.push('skipped');
                return `
                    <div class="${cls.join(' ')}" data-step-index="${idx}">
                        <div class="pbw-step-num"><span>${idx + 1}</span></div>
                        <div class="pbw-step-label">${label}</div>
                    </div>${idx < labels.length - 1 ? `<div class="pbw-step-divider${idx < this._currentStepIndex ? ' done' : ''}"></div>` : ''}`;
            }).join('');
            return dots;
        }

        async _renderCurrentStep() {
            // Refresh step bar
            const bar = this.root.querySelector('#pbwStepsBar');
            if (bar) bar.innerHTML = this._renderStepBar();
            // Re-bind clicks on the rebuilt bar
            this.root.querySelectorAll('.pbw-step-item').forEach(el => {
                el.addEventListener('click', () => {
                    const idx = parseInt(el.dataset.stepIndex, 10);
                    if (isNaN(idx) || idx >= this._currentStepIndex) return;
                    this._goTo(idx);
                });
            });

            // Update state.step (1-based for the summary)
            this.state.patch({ step: this._currentStepIndex + 1 });

            // Render the active step
            const body = this.root.querySelector('#pbwBody');
            const step = this.steps[this._currentStepIndex];
            try {
                const r = step.instance.render(body);
                if (r && typeof r.then === 'function') await r;
            } catch (e) {
                body.innerHTML = `<div class="alert alert-danger">Failed to load this step. Please refresh the page.</div>`;
                console.error(e);
            }

            this._updateFooter();
            this._mirrorFooterToMobile();

            // Subscribe to state changes once to keep the Continue button enabled state fresh
            if (!this._validationSubBound) {
                this.state.subscribe(() => this._updateFooter());
                this._validationSubBound = true;
            }

            window.scrollTo({ top: 0, behavior: 'smooth' });
        }

        _updateFooter() {
            const step = this.steps[this._currentStepIndex];
            const ok = step ? step.instance.validate() : false;
            const isLast = this._currentStepIndex === this.steps.length - 1;
            const nextBtn = this.root.querySelector('[data-action="next"]');
            const backBtn = this.root.querySelector('[data-action="back"]');
            if (nextBtn) {
                nextBtn.disabled = !ok || this._submitting;
                nextBtn.innerHTML = isLast
                    ? (this._submitting ? '<span class="spinner-border spinner-border-sm me-1"></span> Booking...' : '<i class="bi bi-check-circle"></i> Book Appointment')
                    : 'Continue <i class="bi bi-arrow-right"></i>';
                nextBtn.classList.toggle('pbw-btn-success', isLast);
                nextBtn.classList.toggle('pbw-btn-primary', !isLast);
            }
            if (backBtn) {
                backBtn.disabled = this._currentStepIndex === 0 || this._submitting;
            }
            this._mirrorFooterToMobile();
        }

        // Clones the current Back/Continue buttons into the mobile bottom strip.
        _mirrorFooterToMobile() {
            const target = this.summary?.getMobileActionsContainer();
            if (!target) return;
            const isLast = this._currentStepIndex === this.steps.length - 1;
            const step = this.steps[this._currentStepIndex];
            const ok = step ? step.instance.validate() : false;
            target.innerHTML = `
                <button type="button" class="pbw-btn pbw-btn-back" data-mob-back ${this._currentStepIndex === 0 || this._submitting ? 'disabled' : ''}><i class="bi bi-arrow-left"></i></button>
                <button type="button" class="pbw-btn ${isLast ? 'pbw-btn-success' : 'pbw-btn-primary'}" data-mob-next ${(!ok || this._submitting) ? 'disabled' : ''}>
                    ${isLast ? (this._submitting ? '<span class="spinner-border spinner-border-sm me-1"></span> Booking...' : '<i class="bi bi-check-circle"></i> Book Appointment') : 'Continue <i class="bi bi-arrow-right"></i>'}
                </button>`;
            target.querySelector('[data-mob-back]')?.addEventListener('click', () => this._prev());
            target.querySelector('[data-mob-next]')?.addEventListener('click', () => this._next());
        }

        async _next() {
            if (this._submitting) return;
            const step = this.steps[this._currentStepIndex];
            if (!step.instance.validate()) return;

            // Last step → submit booking
            if (this._currentStepIndex === this.steps.length - 1) {
                await this._submit();
                return;
            }

            step.instance.cleanup?.();
            // Skip any skipped steps
            let nextIdx = this._currentStepIndex + 1;
            while (this.steps[nextIdx]?.skip) nextIdx++;
            this._currentStepIndex = nextIdx;
            await this._renderCurrentStep();
        }

        async _prev() {
            if (this._submitting) return;
            if (this._currentStepIndex === 0) return;
            const step = this.steps[this._currentStepIndex];
            step.instance.cleanup?.();
            let prevIdx = this._currentStepIndex - 1;
            while (prevIdx > 0 && this.steps[prevIdx]?.skip) prevIdx--;
            this._currentStepIndex = prevIdx;
            await this._renderCurrentStep();
        }

        async _goTo(targetIdx) {
            if (this._submitting) return;
            if (targetIdx === this._currentStepIndex) return;
            // Only allow jumping back to a previous step
            if (targetIdx > this._currentStepIndex) return;
            this.steps[this._currentStepIndex].instance.cleanup?.();
            this._currentStepIndex = targetIdx;
            await this._renderCurrentStep();
        }

        // Provider step calls this when /api/portal/providers returns. If only one
        // provider is bookable, auto-select them and mark step 2 as skipped.
        _onProvidersLoaded(providers) {
            if (Array.isArray(providers) && providers.length === 1) {
                const p = providers[0];
                this.state.patch({ providerId: p.ProviderId, providerName: p.DisplayName });
                this.steps[1].skip = true;
            }
        }

        async _submit() {
            const s = this.state.get();
            if (!s.selectedSlot) return;
            this._submitting = true;
            this._updateFooter();

            try {
                await this.api.book({
                    StartTime: s.selectedSlot.StartTime,
                    EndTime:   s.selectedSlot.EndTime,
                    Reason:    s.reason || null,
                    ProviderId: s.providerId || null,
                    Type:       s.visitTypeId
                });
                this._renderSuccess(s);
            } catch (e) {
                this._submitting = false;
                this._updateFooter();
                showOverlayToast('Could not book', e?.message || 'Something went wrong. Please try another time.', 'error');
            }
        }

        _renderSuccess(state) {
            const dateStr = state.selectedDate?.toLocaleDateString('en-US', { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric' });
            const timeStr = state.selectedSlot?.StartTimeFormatted || '';
            const provider = state.providerId ? state.providerName : 'Your assigned provider';

            this.root.innerHTML = `
                <div class="pbw-page">
                    <div class="pbw-success-card">
                        <div class="pbw-success-check"><i class="bi bi-check-lg"></i></div>
                        <h3 class="pbw-success-title">Appointment booked</h3>
                        <p class="pbw-success-sub">A confirmation email is on the way.</p>
                        <div class="pbw-success-detail"><i class="bi bi-bookmark-star"></i> ${escapeHtml(state.visitTypeLabel)}</div>
                        <div class="pbw-success-detail"><i class="bi bi-person-badge"></i> ${escapeHtml(provider)}</div>
                        <div class="pbw-success-detail"><i class="bi bi-calendar-event"></i> ${escapeHtml(dateStr)}</div>
                        <div class="pbw-success-detail"><i class="bi bi-clock"></i> ${escapeHtml(timeStr)} (${state.duration} min)</div>
                        <div class="pbw-success-actions">
                            <a class="pbw-btn pbw-btn-primary" href="/Portal/Dashboard"><i class="bi bi-house me-1"></i> Back to Dashboard</a>
                            <a class="pbw-btn pbw-btn-back" href="/Portal/Appointments"><i class="bi bi-calendar-check me-1"></i> View Appointments</a>
                        </div>
                    </div>
                </div>`;
            // Drop summary subscriptions to avoid stale renders
            this.summary?.unmount();
        }
    }

    window.PortalBookingWizard = PortalBookingWizard;
})();
