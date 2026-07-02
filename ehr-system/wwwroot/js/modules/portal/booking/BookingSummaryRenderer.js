/**
 * BookingSummaryRenderer (Hired Guy)
 *
 * Why: render the "Your Selection" panel on desktop/tablet (sidebar) AND on
 *      mobile (sticky bottom strip with expand-on-tap details). One renderer,
 *      both surfaces, fed by the same BookingState.
 * What: mount(sidebarEl, mobileEl, state) — paints both, subscribes to state
 *       changes. unmount() — drops subscription.
 * Who calls: PortalBookingWizard.
 * Returns: instance with mount/unmount.
 */
(function () {
    'use strict';

    function escapeHtml(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, c => ({
            '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
        }[c]));
    }

    function formatDateShort(d) {
        if (!d) return null;
        return d.toLocaleDateString('en-US', { weekday: 'short', month: 'short', day: 'numeric' });
    }

    const STEP_LABELS = ['', 'Visit Type', 'Provider', 'Date & Time', 'Reason'];

    class BookingSummaryRenderer {
        constructor() {
            this.sidebarEl = null;
            this.mobileEl = null;
            this.state = null;
            this._unsub = null;
            this._totalSteps = 4;
        }

        mount(sidebarEl, mobileEl, state, opts) {
            this.sidebarEl = sidebarEl;
            this.mobileEl = mobileEl;
            this.state = state;
            this._totalSteps = (opts && opts.totalSteps) ? opts.totalSteps : 4;
            this._stepKeys = (opts && opts.stepKeys) ? opts.stepKeys.slice() : ['type', 'provider', 'datetime', 'reason'];
            this._renderSidebar();
            this._renderMobile();
            this._unsub = state.subscribe(() => { this._renderSidebar(); this._renderMobile(); });
        }

        unmount() {
            if (this._unsub) { this._unsub(); this._unsub = null; }
            if (this.sidebarEl) this.sidebarEl.innerHTML = '';
            if (this.mobileEl) this.mobileEl.innerHTML = '';
        }

        _renderSidebar() {
            if (!this.sidebarEl || !this.state) return;
            const s = this.state.get();
            const dateText = s.selectedDate ? formatDateShort(s.selectedDate) : 'Not selected';
            const timeText = s.selectedSlot ? (s.selectedSlot.StartTimeFormatted || '--') : 'Not selected';
            const provName = s.providerName || 'Any available';
            const reason = s.reason ? escapeHtml(s.reason) : 'None added';
            const isProvSet = !!s.providerId || s.providerName === 'Any available';

            const row = (icon, label, val, filled) => `
                <div class="pbw-sb-row${filled ? ' filled' : ''}">
                    <i class="bi ${icon} pbw-sb-icon"></i>
                    <div class="pbw-sb-text">
                        <div class="pbw-sb-label">${escapeHtml(label)}</div>
                        <div class="pbw-sb-val${filled ? '' : ' empty'}">${val}</div>
                    </div>
                </div>`;

            this.sidebarEl.innerHTML = `
                <div class="pbw-sb-card">
                    <div class="pbw-sb-icon-circle"><i class="bi bi-calendar2-check"></i></div>
                    <div class="pbw-sb-title">YOUR SELECTION</div>
                    ${row('bi-bookmark-star', 'Visit Type', escapeHtml(s.visitTypeLabel), true)}
                    ${row('bi-person-badge', 'Provider', escapeHtml(provName), isProvSet)}
                    ${row('bi-calendar3', 'Date', escapeHtml(dateText), !!s.selectedDate)}
                    ${row('bi-clock', 'Time', escapeHtml(timeText), !!s.selectedSlot)}
                    ${row('bi-hourglass-split', 'Duration', escapeHtml(s.duration + ' minutes'), true)}
                    ${row('bi-chat-text', 'Reason', reason, !!s.reason)}
                </div>
                <div class="pbw-sb-foot">
                    You can cancel or reschedule from your dashboard up to 24 hours before your appointment.
                </div>`;
        }

        _renderMobile() {
            if (!this.mobileEl || !this.state) return;
            const s = this.state.get();
            const stepLabel = STEP_LABELS[s.step] || '';
            const stepLine = `STEP ${s.step} OF ${this._totalSteps} - ${stepLabel.toUpperCase()}`;
            const summary = this._summaryFor(s);

            const dateText = s.selectedDate ? formatDateShort(s.selectedDate) : 'Not selected';
            const timeText = s.selectedSlot ? (s.selectedSlot.StartTimeFormatted || '--') : 'Not selected';

            const wasExpanded = this.mobileEl.querySelector('.pbw-mob')?.classList.contains('expanded');

            this.mobileEl.innerHTML = `
                <div class="pbw-mob${wasExpanded ? ' expanded' : ''}">
                    <div class="pbw-mob-strip" data-action="toggle-mob">
                        <div class="pbw-mob-current">
                            <div class="pbw-mob-step">${escapeHtml(stepLine)}</div>
                            <div class="pbw-mob-summary">${escapeHtml(summary)}</div>
                        </div>
                        <i class="bi bi-chevron-up pbw-mob-chev"></i>
                    </div>
                    <div class="pbw-mob-details">
                        ${this._mobRow('Visit Type', s.visitTypeLabel)}
                        ${this._mobRow('Provider', s.providerName || 'Any available', !s.providerId && s.providerName !== 'Any available')}
                        ${this._mobRow('Date', dateText, !s.selectedDate)}
                        ${this._mobRow('Time', timeText, !s.selectedSlot)}
                        ${this._mobRow('Duration', s.duration + ' minutes')}
                        ${this._mobRow('Reason', s.reason || 'None added', !s.reason)}
                    </div>
                    <div class="pbw-mob-actions" data-mob-actions></div>
                </div>`;

            this.mobileEl.querySelector('[data-action="toggle-mob"]')?.addEventListener('click', () => {
                this.mobileEl.querySelector('.pbw-mob')?.classList.toggle('expanded');
            });
        }

        _summaryFor(s) {
            switch (s.step) {
                case 1: return s.visitTypeLabel + ', ' + s.duration + ' min';
                case 2: return s.providerName || 'Pick a provider';
                case 3: {
                    const d = s.selectedDate ? formatDateShort(s.selectedDate) : 'Pick a date';
                    return s.selectedSlot
                        ? d + ', ' + (s.selectedSlot.StartTimeFormatted || '')
                        : d + ' — pick a time';
                }
                case 4: return s.reason ? (s.reason.slice(0, 60) + (s.reason.length > 60 ? '…' : '')) : 'No reason (optional)';
                default: return '';
            }
        }

        _mobRow(label, value, isEmpty) {
            return `<div class="pbw-mob-row">
                <span class="pbw-mob-row-label">${escapeHtml(label)}</span>
                <span class="pbw-mob-row-val${isEmpty ? ' empty' : ''}">${escapeHtml(value || '')}</span>
            </div>`;
        }

        // Returns the DOM element where the wizard should mount its prev/next buttons on mobile.
        getMobileActionsContainer() {
            return this.mobileEl ? this.mobileEl.querySelector('[data-mob-actions]') : null;
        }
    }

    window.PortalBookingSummaryRenderer = BookingSummaryRenderer;
})();
