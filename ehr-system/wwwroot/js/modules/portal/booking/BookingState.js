/**
 * BookingState (Hired Guy)
 *
 * Why: hold the wizard's selections in one place and notify subscribers when
 *      anything changes, so the contractor + summary renderer + steps stay in sync.
 * What: tiny event-emitter holding { step, visitTypeId, visitTypeLabel, duration,
 *       providerId, providerName, selectedDate, selectedSlot, reason }.
 *       Notifies on any patch via subscribe(fn) -> unsubscribe.
 * Who calls: PortalBookingWizard, BookingSummaryRenderer, individual step modules.
 * Returns: instance with get(), patch(updates), subscribe(fn).
 */
(function () {
    'use strict';

    class BookingState {
        constructor(initial) {
            this._state = Object.assign({
                step: 1,
                visitTypeId: 1,                  // FollowUpVisit (legacy default)
                visitTypeLabel: 'Standard Visit',
                duration: 30,
                providerId: null,                // null = "Any Available"
                providerName: 'Any Available',
                selectedDate: null,              // Date object
                selectedSlot: null,              // PortalAvailableSlotDto
                reason: ''
            }, initial || {});
            this._subs = [];
        }

        get() { return Object.assign({}, this._state); }

        patch(updates) {
            Object.assign(this._state, updates);
            for (const fn of this._subs) {
                try { fn(this.get()); } catch (e) { console.error('BookingState subscriber failed', e); }
            }
        }

        subscribe(fn) {
            this._subs.push(fn);
            return () => {
                const i = this._subs.indexOf(fn);
                if (i >= 0) this._subs.splice(i, 1);
            };
        }
    }

    window.PortalBookingState = BookingState;
})();
