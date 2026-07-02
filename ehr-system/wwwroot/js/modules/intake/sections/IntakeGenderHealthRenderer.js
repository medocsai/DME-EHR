/**
 * IntakeGenderHealthRenderer (Employee of IntakeFormRenderer)
 *
 * Why: render Section 7 of PDF — Gender-Specific Health. Shows a Women's block,
 *      a Men's block, or asks Biological Sex first if Demographics.Gender is
 *      ambiguous (Other / unset).
 * What: Women's Health (periods, pregnancies, menopause, conditions, PAP/Mammogram/DEXA dates)
 *       + Men's Health (prostate/ED/TRT, testosterone/prostate/PSA dates)
 *       + Sexual Health (shown to all). Structured JSON payload.
 * Who calls: IntakeFormRenderer.
 * Returns: DOM + getPayload() for section "gender-health".
 *
 * Pre-fills via GET /api/intake/gender-health/prefill on mount.
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

    // PDF verbatim — Women's Health.
    const WOMEN_PERIOD_SYMPTOMS = ['Cramping', 'Bloating', 'Mood changes', 'Migraines', 'Spotting', 'None'];
    const WOMEN_MENOPAUSE_SYMPTOMS = ['Hot flashes', 'Night sweats', 'Vaginal dryness', 'Mood changes', 'Brain fog', 'Weight gain', 'None'];
    const WOMEN_CONDITIONS = [
        'Currently on Birth Control', 'Currently on HRT', 'History of PCOS',
        'History of Endometriosis', 'Hysterectomy', 'Fibrocystic Breasts',
        'Uterine Fibroids', 'History of Breast Cancer', 'Recent Breast Tenderness / Lumps'
    ];

    // PDF verbatim — Men's Health.
    const MEN_CONDITIONS = [
        'Prostate Enlargement (BPH)', 'Prostate Infection', 'Prostate Cancer',
        'Erectile Dysfunction', 'Decreased Libido', 'Decreased Morning Erections',
        'Urinary Hesitancy / Weak Flow', 'Nocturia (waking to urinate)', 'Testicular Pain / Swelling',
        'Infertility / Low Sperm Count', 'Currently on TRT', 'Using ED Medications'
    ];

    class IntakeGenderHealthRenderer {
        constructor() {
            this.hint = 'Skip any question that does not apply. Sexual Health questions are optional.';
        }

        async render(container, data, mode) {
            this.container = container;
            container.innerHTML = '<div class="text-center text-muted py-4"><div class="spinner-border spinner-border-sm me-2"></div> Loading...</div>';

            const pf = await fetchPrefillFor(this);
            this._prefill = pf;

            // Determine which form(s) to show: based on existing BiologicalSex (if set)
            // OR Demographics.Gender (if clearly Male/Female) OR ask the question.
            const existingSex = pf?.biologicalSex;
            const demoGender = pf?.demographicsGender;
            let sexChoice = existingSex
                || (demoGender === 'Male' ? 'Male' : demoGender === 'Female' ? 'Female' : '');

            container.innerHTML = `
                <div class="mb-4">
                    <label class="form-label"><strong>Biological sex at birth</strong></label>
                    <div class="form-text mb-2">Needed for clinical questions below. May differ from your gender identity.</div>
                    <div class="ilf-pill-group" id="ghBiologicalSex">
                        ${['Female', 'Male', 'Intersex', 'Prefer not to say'].map(s => `
                            <button type="button" class="btn btn-sm ${sexChoice === s ? 'btn-primary active' : 'btn-outline-primary'} me-1 mb-1" data-pill-value="${attr(s)}">${escapeHtml(s)}</button>
                        `).join('')}
                        <input type="hidden" id="ghBiologicalSexValue" value="${attr(sexChoice)}" />
                    </div>
                </div>

                <div id="ghWomenBlock" style="display:${sexChoice === 'Female' ? '' : 'none'};">
                    ${renderWomenBlock(pf?.structuredData?.women)}
                </div>

                <div id="ghMenBlock" style="display:${sexChoice === 'Male' ? '' : 'none'};">
                    ${renderMenBlock(pf?.structuredData?.men)}
                </div>

                <hr class="my-4">

                <h6 class="text-primary"><i class="bi bi-heart me-1"></i> Sexual Health (all genders)</h6>
                <div class="row g-3 mb-4">
                    <div class="col-md-6">
                        <label class="form-label small">Is your sex life satisfactory?</label>
                        ${pillRadio('ghSatisfactory', ['Yes', 'No', 'Prefer not to say'], pf?.structuredData?.sexual?.satisfactory)}
                    </div>
                    <div class="col-md-6">
                        <label class="form-label small">Any history of sexually transmitted infection (STI)?</label>
                        ${pillRadio('ghStiHistory', ['Yes', 'No', 'Prefer not to say'], pf?.structuredData?.sexual?.stiHistory)}
                    </div>
                </div>
            `;

            // Pill-radio click handling (single-select).
            container.querySelectorAll('.ilf-pill-group').forEach(group => {
                group.addEventListener('click', (e) => {
                    const pill = e.target.closest('[data-pill-value]');
                    if (!pill) return;
                    group.querySelectorAll('[data-pill-value]').forEach(p => {
                        p.classList.remove('active', 'btn-primary');
                        p.classList.add('btn-outline-primary');
                    });
                    pill.classList.add('active', 'btn-primary');
                    pill.classList.remove('btn-outline-primary');
                    const hidden = group.querySelector('input[type=hidden]');
                    if (hidden) hidden.value = pill.dataset.pillValue;

                    // Biological sex picker: show/hide the correct block and rebuild it.
                    if (group.id === 'ghBiologicalSex') {
                        const val = pill.dataset.pillValue;
                        const womenBlock = document.getElementById('ghWomenBlock');
                        const menBlock = document.getElementById('ghMenBlock');
                        if (womenBlock) {
                            if (val === 'Female' && !womenBlock.innerHTML.trim()) {
                                womenBlock.innerHTML = renderWomenBlock();
                            }
                            womenBlock.style.display = val === 'Female' ? '' : 'none';
                        }
                        if (menBlock) {
                            if (val === 'Male' && !menBlock.innerHTML.trim()) {
                                menBlock.innerHTML = renderMenBlock();
                            }
                            menBlock.style.display = val === 'Male' ? '' : 'none';
                        }
                    }
                });
            });
        }

        getPayload() {
            const pick = name => (document.querySelector(`input[type=hidden][name="${name}"]`)?.value || '').trim();
            const pickById = id => (document.getElementById(id)?.value || '').trim();
            const v = id => (document.getElementById(id)?.value || '').trim();
            const checked = name => Array.from(
                document.querySelectorAll(`input[type=checkbox][data-group="${name}"]:checked`)
            ).map(cb => cb.value);

            const sex = pickById('ghBiologicalSexValue');

            return {
                biologicalSex: sex,
                women: sex === 'Female' ? {
                    ageAtFirstPeriod: v('ghAgeAtFirstPeriod'),
                    lastPeriodDate: v('ghLastPeriodDate'),
                    cycleLength: v('ghCycleLength'),
                    periodDuration: v('ghPeriodDuration'),
                    flow: pick('ghFlow'),
                    periodSymptoms: checked('ghPeriodSymptoms'),
                    pregnancies: v('ghPregnancies'),
                    liveBirths: v('ghLiveBirths'),
                    miscarriages: v('ghMiscarriages'),
                    currentlyPregnantOrBreastfeeding: pick('ghPregnantOrBreastfeeding'),
                    menopauseStatus: pick('ghMenopauseStatus'),
                    ageAtMenopause: v('ghAgeAtMenopause'),
                    menopauseSymptoms: checked('ghMenopauseSymptoms'),
                    conditions: checked('ghWomenConditions'),
                    lastPapDate: v('ghLastPap'),
                    lastMammogramDate: v('ghLastMammogram'),
                    lastDexaDate: v('ghLastDexa')
                } : null,
                men: sex === 'Male' ? {
                    conditions: checked('ghMenConditions'),
                    lastTestosteroneDate: v('ghLastTestosterone'),
                    lastProstateExamDate: v('ghLastProstateExam'),
                    lastPsaDate: v('ghLastPsa'),
                    lastPsaLevel: v('ghLastPsaLevel')
                } : null,
                sexual: {
                    satisfactory: pick('ghSatisfactory'),
                    stiHistory: pick('ghStiHistory')
                }
            };
        }
    }

    function renderWomenBlock(data) {
        data = data || {};
        return `
            <h6 class="text-primary mt-2"><i class="bi bi-gender-female me-1"></i> Women's Health</h6>
            <div class="row g-3 mb-4">
                <div class="col-md-3"><label class="form-label small">Age at First Period</label>
                    <input class="form-control" id="ghAgeAtFirstPeriod" value="${attr(data.ageAtFirstPeriod)}" /></div>
                <div class="col-md-3"><label class="form-label small">Date of Last Period</label>
                    <input type="date" class="form-control" id="ghLastPeriodDate" value="${attr(data.lastPeriodDate)}" /></div>
                <div class="col-md-3"><label class="form-label small">Cycle Length (days)</label>
                    <input class="form-control" id="ghCycleLength" value="${attr(data.cycleLength)}" /></div>
                <div class="col-md-3"><label class="form-label small">Period Duration (days)</label>
                    <input class="form-control" id="ghPeriodDuration" value="${attr(data.periodDuration)}" /></div>
                <div class="col-md-3"><label class="form-label small">Flow</label>
                    ${pillRadio('ghFlow', ['Light', 'Moderate', 'Heavy'], data.flow)}</div>
                <div class="col-md-9">
                    <label class="form-label small">Period Symptoms (check all)</label>
                    ${checkboxRow('ghPeriodSymptoms', WOMEN_PERIOD_SYMPTOMS, data.periodSymptoms || [])}
                </div>
                <div class="col-md-3"><label class="form-label small"># Pregnancies</label>
                    <input class="form-control" id="ghPregnancies" value="${attr(data.pregnancies)}" /></div>
                <div class="col-md-3"><label class="form-label small"># Live Births</label>
                    <input class="form-control" id="ghLiveBirths" value="${attr(data.liveBirths)}" /></div>
                <div class="col-md-3"><label class="form-label small"># Miscarriages</label>
                    <input class="form-control" id="ghMiscarriages" value="${attr(data.miscarriages)}" /></div>
                <div class="col-md-3"><label class="form-label small">Currently Pregnant / Breastfeeding?</label>
                    ${pillRadio('ghPregnantOrBreastfeeding', ['Yes', 'No'], data.currentlyPregnantOrBreastfeeding)}</div>
                <div class="col-md-6"><label class="form-label small">Menopause Status</label>
                    ${pillRadio('ghMenopauseStatus', ['N/A', 'Perimenopause', 'Currently in menopause', 'Post-menopause'], data.menopauseStatus)}</div>
                <div class="col-md-3"><label class="form-label small">Age at Menopause</label>
                    <input class="form-control" id="ghAgeAtMenopause" value="${attr(data.ageAtMenopause)}" /></div>
                <div class="col-12">
                    <label class="form-label small">Menopause Symptoms (check all)</label>
                    ${checkboxRow('ghMenopauseSymptoms', WOMEN_MENOPAUSE_SYMPTOMS, data.menopauseSymptoms || [])}
                </div>
                <div class="col-12">
                    <label class="form-label small">Conditions (check all that apply)</label>
                    ${checkboxRow('ghWomenConditions', WOMEN_CONDITIONS, data.conditions || [])}
                </div>
                <div class="col-md-4"><label class="form-label small">Date of Last PAP Smear</label>
                    <input type="date" class="form-control" id="ghLastPap" value="${attr(data.lastPapDate)}" /></div>
                <div class="col-md-4"><label class="form-label small">Date of Last Mammogram</label>
                    <input type="date" class="form-control" id="ghLastMammogram" value="${attr(data.lastMammogramDate)}" /></div>
                <div class="col-md-4"><label class="form-label small">Date of Last Bone Density (DEXA)</label>
                    <input type="date" class="form-control" id="ghLastDexa" value="${attr(data.lastDexaDate)}" /></div>
            </div>
        `;
    }

    function renderMenBlock(data) {
        data = data || {};
        return `
            <h6 class="text-primary mt-2"><i class="bi bi-gender-male me-1"></i> Men's Health</h6>
            <div class="row g-3 mb-4">
                <div class="col-12">
                    <label class="form-label small">Conditions (check all that apply)</label>
                    ${checkboxRow('ghMenConditions', MEN_CONDITIONS, data.conditions || [])}
                </div>
                <div class="col-md-4"><label class="form-label small">Date of Last Testosterone (Total / Free T)</label>
                    <input type="date" class="form-control" id="ghLastTestosterone" value="${attr(data.lastTestosteroneDate)}" /></div>
                <div class="col-md-4"><label class="form-label small">Date of Last Prostate Exam</label>
                    <input type="date" class="form-control" id="ghLastProstateExam" value="${attr(data.lastProstateExamDate)}" /></div>
                <div class="col-md-4"><label class="form-label small">Date of Last PSA</label>
                    <input type="date" class="form-control" id="ghLastPsa" value="${attr(data.lastPsaDate)}" /></div>
                <div class="col-md-6"><label class="form-label small">Last PSA Level (optional)</label>
                    <input class="form-control" id="ghLastPsaLevel" value="${attr(data.lastPsaLevel)}" /></div>
            </div>
        `;
    }

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
    function escapeHtml(s) {
        return String(s || '').replace(/[&<>"']/g, c => ({
            '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
        }[c]));
    }

    window.IntakeGenderHealthRenderer = IntakeGenderHealthRenderer;
})();
