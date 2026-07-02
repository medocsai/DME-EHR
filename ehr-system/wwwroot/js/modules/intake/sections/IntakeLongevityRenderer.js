/**
 * IntakeLongevityRenderer (Employee of IntakeFormRenderer)
 *
 * Why: render Section 4 of PDF — Longevity & Functional Medicine Assessment.
 *      Gated per Location.EnableLongevity; wizard hides this step when off.
 * What: 10 symptom ratings (0-10), 12 goals / 11 prior tests / 18 interventions
 *       / 6 toxin exposures (all PDF verbatim, multi-select checkboxes),
 *       biomarker goals, optimal health vision, last blood panel + physical dates.
 * Who calls: IntakeFormRenderer.
 * Returns: DOM + getPayload() for section "longevity".
 *
 * Pre-fills via GET /api/intake/longevity/prefill on mount.
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
            return (await res.json()) || {};
        } catch { return {}; }
    }

    // PDF verbatim — 10 symptoms rated 0 to 10.
    const SYMPTOMS = [
        { key: 'brainFog',              label: 'Brain Fog / Mental Clarity' },
        { key: 'fatigue',               label: 'Fatigue / Energy Level' },
        { key: 'sleepQuality',          label: 'Sleep Quality' },
        { key: 'anxiety',               label: 'Anxiety' },
        { key: 'depression',            label: 'Depression / Low Mood' },
        { key: 'jointPain',             label: 'Joint / Muscle Pain' },
        { key: 'headaches',             label: 'Headaches / Migraines' },
        { key: 'digestive',             label: 'Digestive Issues' },
        { key: 'cardiovascular',        label: 'Cardiovascular Symptoms' },
        { key: 'skinHairNails',         label: 'Skin / Hair / Nail Changes' },
        { key: 'hormonalSexual',        label: 'Hormonal / Sexual Symptoms' },
        { key: 'immune',                label: 'Immune Function (frequency of illness)' }
    ];

    const GOALS = [
        'Extend healthspan & lifespan', 'Optimize hormone levels', 'Reverse biological age',
        'Improve cognitive performance', 'Enhance athletic performance', 'Weight / body composition',
        'Reduce inflammation', 'Gut & microbiome health', 'Cardiovascular optimization',
        'Sexual health & vitality', 'Cancer prevention', 'Mental / emotional wellness'
    ];

    const PRIOR_TESTING = [
        'Biological Age Test (TruAge, etc.)', 'Genetic / DNA Testing (23andMe, etc.)', 'Comprehensive Hormone Panel',
        'Microbiome / Gut Analysis', 'Telomere Length Testing', 'Advanced Cardiovascular (ApoB, LP(a))',
        'DEXA Scan (body comp / bone density)', 'VO2 Max Testing', 'Continuous Glucose Monitor (CGM)',
        'Heavy Metal / Toxin Testing', 'Coronary Calcium Score (CAC)', 'None of the above'
    ];

    const INTERVENTIONS = [
        'Metformin', 'Rapamycin / mTOR inhibitor', 'NMN / NR (NAD+ precursors)',
        'Berberine', 'Peptide Therapy', 'IV Vitamin / NAD+ Infusions',
        'Hormone Replacement (HRT / TRT)', 'GLP-1 Agonist (Semaglutide, etc.)', 'Hyperbaric Oxygen (HBOT)',
        'Red Light Therapy', 'Cryotherapy', 'Stem Cell / Exosome Therapy',
        'Intermittent / Prolonged Fasting', 'Sauna (regular use)', 'Cold Plunge / Ice Bath',
        'None currently'
    ];

    const TOXINS = [
        'Mold / Water-damaged building exposure', 'Heavy metal exposure (mercury, lead, arsenic)',
        'Pesticide / Herbicide exposure', 'Chemical / Industrial toxin exposure',
        'EMF / 5G proximity concerns', 'Air or water quality concerns'
    ];

    class IntakeLongevityRenderer {
        constructor() {
            this.hint = 'Tap a number to rate how you feel. 0 means no issue, 10 means severe. Optional for any row.';
            this._ratings = {};
        }

        async render(container, data, mode) {
            this.container = container;
            container.innerHTML = '<div class="text-center text-muted py-4"><div class="spinner-border spinner-border-sm me-2"></div> Loading...</div>';

            const pf = await fetchPrefillFor(this);
            this._ratings = pf?.symptomRatings || {};

            const ratingRows = SYMPTOMS.map(s => `
                <div class="iw-rating-row">
                    <div class="fw-semibold">${s.label}</div>
                    <div class="iw-rating-cards" data-rating-key="${s.key}">
                        ${Array.from({length: 11}, (_, i) => {
                            const cls = i === 0 ? 'zero' : (i === 10 ? 'ten' : '');
                            const active = this._ratings[s.key] === i ? ' active' : '';
                            return `<button type="button" class="iw-rating-card ${cls}${active}" data-rating="${i}">${i}</button>`;
                        }).join('')}
                    </div>
                    <div class="d-flex justify-content-between small mt-1"><span>0 · none</span><span>10 · severe</span></div>
                </div>
            `).join('');

            const checkGrid = (name, arr, preset) => `
                <div class="iw-checkbox-grid" data-group="${name}">
                    ${arr.map(g => {
                        const isChecked = Array.isArray(preset) && preset.includes(g);
                        return `<label><input type="checkbox" value="${attr(g)}" ${isChecked ? 'checked' : ''}> <span>${escapeHtml(g)}</span></label>`;
                    }).join('')}
                </div>
            `;

            container.innerHTML = `
                <h6 class="mt-1 text-primary">How are you feeling lately?</h6>
                <div class="small mb-3">Rate each symptom: 0 = none, 10 = severe. Leave blank if not applicable.</div>
                ${ratingRows}

                <h6 class="mt-4 text-primary">Your longevity goals <small>(check all that apply)</small></h6>
                ${checkGrid('goals', GOALS, pf?.goals)}

                <h6 class="mt-4 text-primary">Prior longevity &amp; advanced testing you have done</h6>
                ${checkGrid('priorTesting', PRIOR_TESTING, pf?.priorTesting)}

                <h6 class="mt-4 text-primary">Longevity interventions currently using</h6>
                ${checkGrid('currentInterventions', INTERVENTIONS, pf?.currentInterventions)}

                <h6 class="mt-4 text-primary">Environmental &amp; toxin exposure</h6>
                ${checkGrid('toxinExposure', TOXINS, pf?.toxinExposure)}

                <div class="row g-3 mt-2">
                    <div class="col-md-6">
                        <label class="form-label">Date of Last Comprehensive Blood Panel</label>
                        <input type="date" class="form-control" id="ilLastBloodPanel" value="${attr(pf?.lastBloodPanel)}" />
                    </div>
                    <div class="col-md-6">
                        <label class="form-label">Date of Last Full Physical Exam</label>
                        <input type="date" class="form-control" id="ilLastPhysical" value="${attr(pf?.lastPhysical)}" />
                    </div>
                </div>

                <div class="mt-4">
                    <label class="form-label">Biomarker &amp; Lab Goals</label>
                    <div class="form-text mb-1">What specific biomarkers or health metrics would you like to optimize?</div>
                    <textarea class="form-control" id="ilBiomarkerGoals" rows="2">${txt(pf?.biomarkerGoals)}</textarea>
                </div>
                <div class="mt-3">
                    <label class="form-label">What does "optimal health" look like to you in 5 to 10 years?</label>
                    <textarea class="form-control" id="ilOptimalHealth" rows="2">${txt(pf?.optimalHealthVision)}</textarea>
                </div>
            `;

            container.addEventListener('click', (e) => {
                const card = e.target.closest('.iw-rating-card');
                if (card) {
                    const row = card.closest('.iw-rating-cards');
                    const key = row.getAttribute('data-rating-key');
                    const val = parseInt(card.getAttribute('data-rating'), 10);
                    row.querySelectorAll('.iw-rating-card').forEach(c => c.classList.remove('active'));
                    card.classList.add('active');
                    this._ratings[key] = val;
                }
            });
        }

        _readChecks(group) {
            return Array.from(document.querySelectorAll(`[data-group="${group}"] input[type="checkbox"]:checked`))
                .map(el => el.value);
        }

        getPayload(sectionName) {
            return {
                symptomRatings: this._ratings,
                goals: this._readChecks('goals'),
                priorTesting: this._readChecks('priorTesting'),
                currentInterventions: this._readChecks('currentInterventions'),
                toxinExposure: this._readChecks('toxinExposure'),
                lastBloodPanel: (document.getElementById('ilLastBloodPanel')?.value || '').trim(),
                lastPhysical: (document.getElementById('ilLastPhysical')?.value || '').trim(),
                biomarkerGoals: (document.getElementById('ilBiomarkerGoals')?.value || '').trim(),
                optimalHealthVision: (document.getElementById('ilOptimalHealth')?.value || '').trim()
            };
        }
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

    window.IntakeLongevityRenderer = IntakeLongevityRenderer;
})();
