/**
 * BookingDateTimeStep (Hired Guy)
 *
 * Why: let the patient pick a date and a time slot — both inside one wizard step
 *      since splitting them feels like padding.
 * What: 7-day quick-pick row, prev/next nav, calendar input, slot grid grouped
 *       by Morning / Afternoon / Evening. Slots come from the API filtered by
 *       state.providerId (when set) and state.duration (visit type drives this).
 * Who calls: PortalBookingWizard.
 */
(function () {
    'use strict';

    const fmt = window.PortalBookingApiClient.formatDateLocal;

    class BookingDateTimeStep {
        constructor(state, api) {
            this.state = state;
            this.api = api;
            this.container = null;
            this._slots = [];
            this._clinicTimeZone = '';
            this._clinicToday = null;          // string YYYY-MM-DD
            this._loading = false;
            this._lastFilterKey = null;        // to detect provider/duration changes
            this._stateUnsub = null;
        }

        async render(container) {
            this.container = container;

            // Initial paint with skeleton
            container.innerHTML = `
                <h3 class="pbw-step-title">When works for you?</h3>
                <div class="pbw-step-lede" id="pbwStep3Lede">Pick a date and time. Showing ${this.state.get().duration}-minute slots.</div>
                <div class="pbw-date-block">
                    <div class="pbw-day-picks" id="pbwDayPicks"></div>
                    <div class="pbw-date-controls">
                        <div class="pbw-date-nav">
                            <button type="button" class="pbw-nav-btn" id="pbwPrevDay"><i class="bi bi-chevron-left"></i></button>
                            <div class="pbw-date-display" id="pbwDateDisplay">Loading...</div>
                            <button type="button" class="pbw-nav-btn" id="pbwNextDay"><i class="bi bi-chevron-right"></i></button>
                        </div>
                        <input type="date" class="pbw-date-input" id="pbwDateInput">
                    </div>
                </div>
                <div id="pbwSlotsArea">
                    <div class="text-center text-muted py-4"><div class="spinner-border spinner-border-sm me-2"></div> Loading available times...</div>
                </div>`;

            await this._initFromTodayProbe();
            this._renderDayPicks();
            this._wireDateControls();
            await this._loadSlots();

            // If duration changes (visit type changed on step 1) while we're on step 3,
            // we need to refetch slots. Subscribe to state changes for this step's lifetime.
            this._stateUnsub = this.state.subscribe(() => {
                const s = this.state.get();
                const key = `${s.providerId || ''}|${s.duration}|${s.selectedDate ? fmt(s.selectedDate) : ''}`;
                if (this._lastFilterKey != null && key !== this._lastFilterKey && !this._loading) {
                    this._loadSlots();
                }
            });
        }

        async _initFromTodayProbe() {
            const s = this.state.get();
            if (s.selectedDate && this._clinicToday) return; // already initialized

            // Probe current clinic timezone via a 0-duration-safe call
            const today = new Date();
            try {
                const probe = await this.api.getSlots({ date: today, duration: s.duration });
                this._clinicToday = probe?.clinicToday || fmt(today);
                this._clinicTimeZone = probe?.clinicTimeZone || '';
            } catch {
                this._clinicToday = fmt(today);
            }
            // Default to clinic's tomorrow
            const todayDate = new Date(this._clinicToday + 'T12:00:00');
            const tomorrow = new Date(todayDate);
            tomorrow.setDate(tomorrow.getDate() + 1);
            this.state.patch({ selectedDate: tomorrow });
        }

        _renderDayPicks() {
            const picks = this.container.querySelector('#pbwDayPicks');
            const display = this.container.querySelector('#pbwDateDisplay');
            const dateInput = this.container.querySelector('#pbwDateInput');
            const todayDate = new Date(this._clinicToday + 'T12:00:00');
            const tomorrowDate = new Date(todayDate); tomorrowDate.setDate(tomorrowDate.getDate() + 1);
            const sel = this.state.get().selectedDate;

            const buttons = [];
            for (let i = 0; i < 7; i++) {
                const d = new Date(todayDate);
                d.setDate(d.getDate() + i);
                const isActive = sel && d.toDateString() === sel.toDateString();
                const label = d.toDateString() === todayDate.toDateString() ? 'Today'
                    : d.toDateString() === tomorrowDate.toDateString() ? 'Tomorrow'
                    : d.toLocaleDateString('en-US', { weekday: 'short', month: 'short', day: 'numeric' });
                buttons.push(`<button type="button" class="pbw-day-pick${isActive ? ' active' : ''}" data-date="${fmt(d)}">${label}</button>`);
            }
            picks.innerHTML = buttons.join('');
            picks.querySelectorAll('.pbw-day-pick').forEach(b => {
                b.addEventListener('click', () => this._setDate(new Date(b.dataset.date + 'T12:00:00')));
            });

            display.textContent = sel ? sel.toLocaleDateString('en-US', { weekday: 'long', month: 'long', day: 'numeric' }) : '';
            dateInput.value = sel ? fmt(sel) : '';
            dateInput.min = this._clinicToday;
        }

        _wireDateControls() {
            this.container.querySelector('#pbwPrevDay')?.addEventListener('click', () => {
                const sel = this.state.get().selectedDate;
                if (!sel) return;
                const prev = new Date(sel); prev.setDate(prev.getDate() - 1);
                if (fmt(prev) < this._clinicToday) return;
                this._setDate(prev);
            });
            this.container.querySelector('#pbwNextDay')?.addEventListener('click', () => {
                const sel = this.state.get().selectedDate;
                if (!sel) return;
                const next = new Date(sel); next.setDate(next.getDate() + 1);
                this._setDate(next);
            });
            this.container.querySelector('#pbwDateInput')?.addEventListener('change', e => {
                if (!e.target.value) return;
                this._setDate(new Date(e.target.value + 'T12:00:00'));
            });
        }

        _setDate(d) {
            // Switching date voids any selected slot
            this.state.patch({ selectedDate: d, selectedSlot: null });
            this._renderDayPicks();
            this._loadSlots();
        }

        async _loadSlots() {
            if (this._loading) return;
            const s = this.state.get();
            if (!s.selectedDate) return;
            this._loading = true;
            this._lastFilterKey = `${s.providerId || ''}|${s.duration}|${fmt(s.selectedDate)}`;

            const slotsArea = this.container.querySelector('#pbwSlotsArea');
            slotsArea.innerHTML = `<div class="text-center text-muted py-4"><div class="spinner-border spinner-border-sm me-2"></div> Loading available times...</div>`;

            try {
                const data = await this.api.getSlots({
                    date: s.selectedDate,
                    providerId: s.providerId,
                    duration: s.duration
                });
                this._slots = (data && data.slots) || [];
                if (data?.clinicTimeZone) this._clinicTimeZone = data.clinicTimeZone;
                if (data?.clinicToday) this._clinicToday = data.clinicToday;
                this._renderSlots();
                const lede = this.container.querySelector('#pbwStep3Lede');
                if (lede) lede.textContent = `Pick a date and time. Showing ${s.duration}-minute slots${s.providerName && s.providerId ? ' with ' + s.providerName : ''}.`;
            } catch (e) {
                slotsArea.innerHTML = `<div class="alert alert-warning">Could not load available times. Please try again.</div>`;
            } finally {
                this._loading = false;
            }
        }

        _renderSlots() {
            const s = this.state.get();
            const slotsArea = this.container.querySelector('#pbwSlotsArea');
            if (!this._slots.length) {
                const provLine = s.providerId
                    ? `No openings with ${s.providerName} on this day.`
                    : 'No appointments available on this day.';
                slotsArea.innerHTML = `
                    <div class="pbw-slot-empty">
                        <i class="bi bi-calendar-x"></i>
                        <div><b>${provLine}</b></div>
                        <div class="text-muted small">Try another day${s.providerId ? ' or go back and choose Any Available' : ''}.</div>
                    </div>`;
                return;
            }

            // Group by AM/PM bucket using StartTime (UTC -> clinic local)
            const morning = [], afternoon = [], evening = [];
            for (const slot of this._slots) {
                const f = slot.StartTimeFormatted || '';
                // Format like "9:30 AM" — use the period to bucket reliably
                const lower = f.toLowerCase();
                const hr = parseInt(f, 10);
                if (lower.includes('am')) morning.push(slot);
                else if (hr < 5) afternoon.push(slot);     // 12:00 PM - 4:30 PM
                else evening.push(slot);                    // 5:00 PM and later
            }

            const grid = (label, icon, slots) => slots.length === 0 ? '' : `
                <div class="pbw-slot-group-label"><i class="bi ${icon}"></i> ${label}</div>
                <div class="pbw-slot-grid">
                    ${slots.map((slot, idx) => this._slotButton(slot)).join('')}
                </div>`;

            slotsArea.innerHTML = grid('Morning', 'bi-sunrise-fill', morning)
                                + grid('Afternoon', 'bi-sun-fill', afternoon)
                                + grid('Evening', 'bi-moon-fill', evening);

            slotsArea.querySelectorAll('.pbw-slot-btn').forEach(btn => {
                btn.addEventListener('click', () => {
                    const idx = parseInt(btn.dataset.idx, 10);
                    if (isNaN(idx)) return;
                    slotsArea.querySelectorAll('.pbw-slot-btn').forEach(b => b.classList.remove('selected'));
                    btn.classList.add('selected');
                    this.state.patch({ selectedSlot: this._slots[idx] });
                });
            });

            // Restore selection if state already has one matching this slot list
            const sel = this.state.get().selectedSlot;
            if (sel) {
                const idx = this._slots.findIndex(x => x.StartTime === sel.StartTime);
                if (idx >= 0) {
                    const btn = slotsArea.querySelectorAll('.pbw-slot-btn')[idx];
                    if (btn) btn.classList.add('selected');
                } else {
                    // Selected slot is no longer in the list — clear it
                    this.state.patch({ selectedSlot: null });
                }
            }
        }

        _slotButton(slot) {
            const idx = this._slots.indexOf(slot);
            const f = (slot.StartTimeFormatted || '').trim();
            const parts = f.split(' ');
            const time = parts[0] || f;
            const period = parts.length > 1 ? parts[parts.length - 1] : '';
            return `<button type="button" class="pbw-slot-btn" data-idx="${idx}">${time}<span class="pbw-slot-ampm">${period}</span></button>`;
        }

        validate() { return !!this.state.get().selectedSlot; }

        cleanup() {
            if (this._stateUnsub) { this._stateUnsub(); this._stateUnsub = null; }
        }
    }

    window.PortalBookingDateTimeStep = BookingDateTimeStep;
})();
