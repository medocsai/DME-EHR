/**
 * BookingVisitTypeStep (Hired Guy)
 *
 * Why: collect the patient's visit type (Standard / New Longevity / Follow-Up Longevity)
 *      so the wizard can request slots of the right duration on step 3.
 * What: renders 3 large radio cards. Selection updates BookingState. validate()
 *       returns true iff a type is chosen (always true — Standard is pre-selected).
 * Who calls: PortalBookingWizard.
 */
(function () {
    'use strict';

    const TYPES = [
        { id: 1,  label: 'Standard Visit',              duration: 30, icon: 'bi-stethoscope', desc: 'Routine check-up, follow-up, or general concern.' },
        { id: 10, label: 'New Longevity Patient',       duration: 60, icon: 'bi-stars',       desc: 'First longevity consultation. Comprehensive baseline review.' },
        { id: 11, label: 'Follow-Up Longevity Patient', duration: 45, icon: 'bi-arrow-repeat', desc: 'Continuing care for existing longevity patients.' }
    ];

    class BookingVisitTypeStep {
        constructor(state) { this.state = state; this.container = null; }

        render(container) {
            this.container = container;
            const s = this.state.get();
            const cards = TYPES.map(t => `
                <button type="button" class="pbw-choice-card${s.visitTypeId === t.id ? ' selected' : ''}"
                        data-type-id="${t.id}" data-type-label="${t.label}" data-type-duration="${t.duration}">
                    <div class="pbw-choice-icon" data-tid="${t.id}"><i class="bi ${t.icon}"></i></div>
                    <div class="pbw-choice-title">${t.label}</div>
                    <div class="pbw-choice-meta"><i class="bi bi-clock"></i> ${t.duration} minutes</div>
                    <div class="pbw-choice-desc">${t.desc}</div>
                </button>`).join('');

            container.innerHTML = `
                <h3 class="pbw-step-title">What kind of visit?</h3>
                <div class="pbw-step-lede">Pick the option that best describes your visit. You can change it later.</div>
                <div class="pbw-choice-grid">${cards}</div>`;

            container.querySelectorAll('.pbw-choice-card').forEach(card => {
                card.addEventListener('click', () => {
                    container.querySelectorAll('.pbw-choice-card').forEach(c => c.classList.remove('selected'));
                    card.classList.add('selected');
                    this.state.patch({
                        visitTypeId: parseInt(card.dataset.typeId, 10),
                        visitTypeLabel: card.dataset.typeLabel,
                        duration: parseInt(card.dataset.typeDuration, 10),
                        // Changing duration invalidates any previously selected slot
                        selectedSlot: null
                    });
                });
            });
        }

        validate() {
            const s = this.state.get();
            return s.visitTypeId != null;
        }

        cleanup() { /* No external listeners to remove */ }
    }

    window.PortalBookingVisitTypeStep = BookingVisitTypeStep;
})();
