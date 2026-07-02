/**
 * IntakeLifestyleRenderer (Employee of IntakeFormRenderer)
 *
 * Why: render Section 5 (Lifestyle &amp; Habits) per PDF. Structured choices:
 *      Sleep, Exercise, Nutrition, Substance Use, Stress, Dental, Birth History,
 *      Social Support. PDF-verbatim option lists.
 * What: Emits a structured JSON payload. Backend persists the JSON to
 *       PatientIntakeSubmission.LifestyleData for wizard pre-fill, AND writes
 *       human-readable summaries to PatientSocialHistory for Profile / Encounter
 *       views.
 * Who calls: IntakeFormRenderer.
 * Returns: DOM + getPayload() for section "lifestyle".
 */
(function () {
    'use strict';

    // Prefill URL + auth headers injected by IntakeFormRenderer (host config).
    // Same renderer works for portal (JWT) and tablet (verify cookie).
    async function fetchPrefillFor(renderer) {
        if (!renderer || !renderer._prefillUrl) return {};
        try {
            const headers = (typeof renderer._authHeaders === 'function') ? renderer._authHeaders() : {};
            const res = await fetch(renderer._prefillUrl, { headers });
            if (!res.ok) return {};
            const obj = await res.json();
            return obj || {};
        } catch { return {}; }
    }

    class IntakeLifestyleRenderer {
        constructor() {
            this.hint = 'Daily habits tell us a lot about your health. Share what you are comfortable with. Leave anything blank you would rather not answer.';
        }

        async render(container, data, mode) {
            this.container = container;
            container.innerHTML = '<div class="text-center text-muted py-4"><div class="spinner-border spinner-border-sm me-2"></div> Loading...</div>';

            const pf = await fetchPrefillFor(this);
            this._prefill = pf;

            container.innerHTML = `
                <!-- Sleep -->
                <h6 class="text-primary"><i class="bi bi-moon-stars me-1"></i> Sleep</h6>
                <div class="row g-3 mb-4">
                    <div class="col-md-6">
                        <label class="form-label small">Hours of Sleep per Night</label>
                        ${pillRadio('ilfSleepHours', ['<5', '5-6', '6-7', '7-8', '>8'], pf?.sleep?.hours)}
                    </div>
                    <div class="col-md-6">
                        <label class="form-label small">Sleep Quality</label>
                        ${pillRadio('ilfSleepQuality', ['Poor', 'Fair', 'Good', 'Excellent'], pf?.sleep?.quality)}
                    </div>
                    <div class="col-12">
                        <label class="form-label small">Sleep Issues (check all that apply)</label>
                        ${checkboxRow('ilfSleepIssues',
                            ['Trouble falling asleep', 'Waking at night', 'Early waking', 'Snoring / Apnea', 'Night sweats', 'None'],
                            pf?.sleep?.issues || [])}
                    </div>
                </div>

                <!-- Exercise -->
                <h6 class="text-primary"><i class="bi bi-heart-pulse me-1"></i> Exercise &amp; Physical Activity</h6>
                <div class="row g-3 mb-4">
                    <div class="col-md-6">
                        <label class="form-label small">Frequency per week</label>
                        ${pillRadio('ilfExerciseFreq', ['Sedentary', '1-2x', '3-4x', '5-6x', 'Daily'], pf?.exercise?.frequency)}
                    </div>
                    <div class="col-md-6">
                        <label class="form-label small">Types (check all)</label>
                        ${checkboxRow('ilfExerciseTypes',
                            ['Cardio', 'Strength', 'HIIT', 'Yoga/Pilates', 'Walking', 'Swimming', 'Sports', 'Other'],
                            pf?.exercise?.types || [])}
                    </div>
                    <div class="col-12">
                        <label class="form-label small">Physical Limitations or Injuries</label>
                        <input class="form-control" id="ilfExerciseLimits" value="${attr(pf?.exercise?.limitations)}" />
                    </div>
                </div>

                <!-- Nutrition -->
                <h6 class="text-primary"><i class="bi bi-egg-fried me-1"></i> Nutrition</h6>
                <div class="row g-3 mb-4">
                    <div class="col-12">
                        <label class="form-label small">Dietary Pattern (check all)</label>
                        ${checkboxRow('ilfDietPattern',
                            ['Standard', 'Mediterranean', 'Ketogenic', 'Carnivore', 'Paleo', 'Vegetarian', 'Vegan', 'Intermittent Fasting', 'Anti-Inflammatory', 'Other'],
                            pf?.diet?.pattern || [])}
                    </div>
                    <div class="col-md-4">
                        <label class="form-label small">Glasses of Water/Day</label>
                        ${pillRadio('ilfDietWater', ['<4', '4-6', '6-8', '>8'], pf?.diet?.water)}
                    </div>
                    <div class="col-md-4">
                        <label class="form-label small">Meals per Day</label>
                        ${pillRadio('ilfDietMeals', ['1 (OMAD)', '2', '3', '4+'], pf?.diet?.meals)}
                    </div>
                    <div class="col-md-4">
                        <label class="form-label small">Food Cravings</label>
                        <input class="form-control" id="ilfDietCravings" value="${attr(pf?.diet?.cravings)}" />
                    </div>
                    <div class="col-12">
                        <label class="form-label small">Foods You Avoid or Are Sensitive To</label>
                        <input class="form-control" id="ilfDietAvoids" value="${attr(pf?.diet?.avoids)}" />
                    </div>
                </div>

                <!-- Substance Use -->
                <h6 class="text-primary"><i class="bi bi-cup-straw me-1"></i> Substance Use</h6>
                <div class="row g-3 mb-4">
                    <div class="col-md-6">
                        <label class="form-label small">Tobacco / Nicotine</label>
                        ${pillRadio('ilfTobacco', ['Never', 'Former', 'Current'], pf?.substance?.tobacco)}
                        <input class="form-control mt-2" id="ilfTobaccoDetails" placeholder="Type / amount / years used" value="${attr(pf?.substance?.tobaccoDetails)}" />
                    </div>
                    <div class="col-md-6">
                        <label class="form-label small">Alcohol</label>
                        ${pillRadio('ilfAlcohol', ['None', 'Occasional', 'Moderate (1-7/wk)', 'Heavy (7+/wk)'], pf?.substance?.alcohol)}
                    </div>
                    <div class="col-md-6">
                        <label class="form-label small">Cannabis</label>
                        ${pillRadio('ilfCannabis', ['Never', 'Occasionally', 'Regularly'], pf?.substance?.cannabis)}
                    </div>
                    <div class="col-md-6">
                        <label class="form-label small">Recreational Drugs</label>
                        <input class="form-control" id="ilfDrugs" placeholder="None, or describe briefly" value="${attr(pf?.substance?.drugs)}" />
                    </div>
                </div>

                <!-- Stress -->
                <h6 class="text-primary"><i class="bi bi-emoji-dizzy me-1"></i> Stress &amp; Mental Wellness</h6>
                <div class="row g-3 mb-4">
                    <div class="col-12">
                        <label class="form-label small">Current Stress Level (1 Low, 10 High)</label>
                        ${pillRadio('ilfStressLevel', ['1','2','3','4','5','6','7','8','9','10'], pf?.stress?.level)}
                    </div>
                    <div class="col-12">
                        <label class="form-label small">Stress Management (check all)</label>
                        ${checkboxRow('ilfStressMgmt',
                            ['Meditation', 'Yoga', 'Prayer', 'Therapy', 'Exercise', 'Breathwork', 'Journaling', 'Outdoors', 'None'],
                            pf?.stress?.management || [])}
                    </div>
                    <div class="col-md-6">
                        <label class="form-label small">Currently in therapy or counseling?</label>
                        ${pillRadio('ilfInTherapy', ['Yes', 'No'], pf?.stress?.inTherapy)}
                    </div>
                </div>

                <!-- Dental -->
                <h6 class="text-primary"><i class="bi bi-emoji-smile me-1"></i> Dental Health</h6>
                <div class="row g-3 mb-4">
                    <div class="col-12">
                        ${checkboxRow('ilfDental',
                            ['Amalgam (silver) fillings', 'Root canals', 'Crowns / Implants', 'Bleeding gums', 'Recent dental procedures'],
                            pf?.dental || [])}
                    </div>
                </div>

                <!-- Birth History -->
                <h6 class="text-primary"><i class="bi bi-heart me-1"></i> Birth History (relevant to longevity)</h6>
                <div class="row g-3 mb-4">
                    <div class="col-md-4">
                        <label class="form-label small">Birth Type</label>
                        ${pillRadio('ilfBirthType', ['Vaginal', 'C-Section'], pf?.birth?.type)}
                    </div>
                    <div class="col-md-4">
                        <label class="form-label small">Breastfed?</label>
                        ${pillRadio('ilfBreastfed', ['Yes', 'No'], pf?.birth?.breastfed)}
                    </div>
                    <div class="col-md-4">
                        <label class="form-label small">Childhood Antibiotic Use</label>
                        ${pillRadio('ilfAntibiotics', ['Rarely', 'Occasionally', 'Frequently'], pf?.birth?.antibiotics)}
                    </div>
                </div>

                <!-- Social Support -->
                <h6 class="text-primary"><i class="bi bi-people me-1"></i> Social Support &amp; Trauma (optional)</h6>
                <div class="row g-3 mb-4">
                    <div class="col-md-6">
                        <label class="form-label small">Do you have a strong social support system?</label>
                        ${pillRadio('ilfSocialSupport', ['Yes', 'No'], pf?.social?.support)}
                    </div>
                    <div class="col-12">
                        <label class="form-label small">Significant Life Trauma (optional, only if you wish to share)</label>
                        <textarea class="form-control" id="ilfLifeTrauma" rows="2">${txt(pf?.social?.lifeTrauma)}</textarea>
                    </div>
                </div>
            `;

            // Activate any pre-selected pill-radios (CSS highlight) via change event
            container.querySelectorAll('.ilf-pill-group').forEach(group => {
                group.addEventListener('click', (e) => {
                    const pill = e.target.closest('[data-pill-value]');
                    if (!pill) return;
                    group.querySelectorAll('[data-pill-value]').forEach(p => p.classList.remove('active', 'btn-primary'));
                    group.querySelectorAll('[data-pill-value]').forEach(p => p.classList.add('btn-outline-primary'));
                    pill.classList.add('active', 'btn-primary');
                    pill.classList.remove('btn-outline-primary');
                    const hidden = group.querySelector('input[type=hidden]');
                    if (hidden) hidden.value = pill.dataset.pillValue;
                });
            });
        }

        getPayload() {
            const v = id => (document.getElementById(id)?.value || '').trim();
            const pick = name => {
                const el = document.querySelector(`input[type=hidden][name="${name}"]`);
                return el ? el.value : '';
            };
            const checked = name => Array.from(
                document.querySelectorAll(`input[type=checkbox][data-group="${name}"]:checked`)
            ).map(cb => cb.value);

            return {
                sleep: {
                    hours: pick('ilfSleepHours'),
                    quality: pick('ilfSleepQuality'),
                    issues: checked('ilfSleepIssues')
                },
                exercise: {
                    frequency: pick('ilfExerciseFreq'),
                    types: checked('ilfExerciseTypes'),
                    limitations: v('ilfExerciseLimits')
                },
                diet: {
                    pattern: checked('ilfDietPattern'),
                    water: pick('ilfDietWater'),
                    meals: pick('ilfDietMeals'),
                    cravings: v('ilfDietCravings'),
                    avoids: v('ilfDietAvoids')
                },
                substance: {
                    tobacco: pick('ilfTobacco'),
                    tobaccoDetails: v('ilfTobaccoDetails'),
                    alcohol: pick('ilfAlcohol'),
                    cannabis: pick('ilfCannabis'),
                    drugs: v('ilfDrugs')
                },
                stress: {
                    level: pick('ilfStressLevel'),
                    management: checked('ilfStressMgmt'),
                    inTherapy: pick('ilfInTherapy')
                },
                dental: checked('ilfDental'),
                birth: {
                    type: pick('ilfBirthType'),
                    breastfed: pick('ilfBreastfed'),
                    antibiotics: pick('ilfAntibiotics')
                },
                social: {
                    support: pick('ilfSocialSupport'),
                    lifeTrauma: v('ilfLifeTrauma')
                }
            };
        }
    }

    // --- Pill-radio helper: renders a row of buttons acting as a single-select ---
    function pillRadio(name, options, current) {
        const pills = options.map(opt => {
            const isActive = current === opt;
            return `<button type="button"
                class="btn btn-sm ${isActive ? 'btn-primary active' : 'btn-outline-primary'} me-1 mb-1"
                data-pill-value="${attr(opt)}">${escapeHtml(opt)}</button>`;
        }).join('');
        return `
            <div class="ilf-pill-group" data-group="${name}">
                ${pills}
                <input type="hidden" name="${name}" value="${attr(current || '')}" />
            </div>
        `;
    }

    function checkboxRow(name, options, current) {
        const cbs = options.map((opt, i) => {
            const isChecked = Array.isArray(current) && current.includes(opt);
            const id = `${name}_${i}`;
            return `
                <div class="form-check form-check-inline mb-1">
                    <input class="form-check-input" type="checkbox" id="${id}"
                           data-group="${name}" value="${attr(opt)}" ${isChecked ? 'checked' : ''}>
                    <label class="form-check-label" for="${id}">${escapeHtml(opt)}</label>
                </div>
            `;
        }).join('');
        return `<div>${cbs}</div>`;
    }

    function attr(v) {
        if (v === null || v === undefined) return '';
        return String(v).replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }
    function txt(v) {
        if (v === null || v === undefined) return '';
        return String(v).replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }
    function escapeHtml(s) {
        return String(s || '').replace(/[&<>"']/g, c => ({
            '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
        }[c]));
    }

    window.IntakeLifestyleRenderer = IntakeLifestyleRenderer;
})();
