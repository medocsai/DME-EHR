/**
 * IntakeMedicalHistoryRenderer (Employee of IntakeFormRenderer)
 *
 * Why: render Section 3 (Personal Medical History). Contains 5 sub-blocks:
 *      Chronic Problems (checkboxes + Add), Allergies (Add only — free-form),
 *      Family History (checkboxes + Add), Surgeries (Add only), Immunizations
 *      (checkboxes + Add). Plus "Cancer — specify" free text.
 * What: Checkbox-driven UX for common items (PDF verbatim); each checked item
 *       creates a row in the corresponding clinical table with an optional notes
 *       field that slides open below. Add button for items not in the fixed list.
 * Who calls: IntakeFormRenderer with multi = ['medical-history','allergies','immunizations'].
 * Returns: DOM + getPayload(sectionName) for each of the 3 sections.
 *
 * Pre-fills via GET /api/intake/medical-history/prefill on mount: existing
 * patient-entered rows pre-check their matching boxes and populate manual rows.
 */
(function () {
    'use strict';

    // Prefill URL + auth headers injected by IntakeFormRenderer (host config).
    // Same renderer works for portal (JWT) and tablet (verify cookie).
    async function fetchPrefillFor(renderer) {
        if (!renderer || !renderer._prefillUrl) return null;
        try {
            const headers = (typeof renderer._authHeaders === 'function') ? renderer._authHeaders() : {};
            const res = await fetch(renderer._prefillUrl, { headers });
            if (!res.ok) return null;
            return await res.json();
        } catch { return null; }
    }

    // PDF verbatim — Chronic Problems / Conditions, grouped by category.
    const PROBLEM_CATEGORIES = [
        {
            key: 'cardiovascular',
            title: 'Cardiovascular & Circulatory',
            items: ['Heart Attack', 'Heart Disease / Other', 'Hypertension (High BP)',
                    'Stroke', 'High Cholesterol', 'Atrial Fibrillation',
                    'Pacemaker / Device', 'Blood Clots / DVT', 'Varicose Veins']
        },
        {
            key: 'metabolic',
            title: 'Metabolic & Endocrine',
            items: ['Type 1 Diabetes', 'Type 2 Diabetes', 'Pre-Diabetes / Insulin Resistance',
                    'Hypoglycemia', 'Metabolic Syndrome', 'Hypothyroidism',
                    'Hyperthyroidism', "Hashimoto's Disease", 'Adrenal Dysfunction',
                    'PCOS', 'Infertility', 'Other Endocrine']
        },
        {
            key: 'gastrointestinal',
            title: 'Gastrointestinal',
            items: ['GERD / Acid Reflux', 'IBS', "Crohn's Disease",
                    'Ulcerative Colitis', 'Celiac Disease', 'SIBO',
                    'Liver Disease / Hepatitis', 'Gallbladder Issues', 'Leaky Gut / Dysbiosis']
        },
        {
            key: 'neurological',
            title: 'Neurological & Mental Health',
            items: ['Depression', 'Anxiety / Panic Attacks', 'Bipolar Disorder',
                    'ADHD', 'Autism Spectrum', 'Seizures / Epilepsy',
                    "Parkinson's Disease", "Alzheimer's / Dementia", 'Multiple Sclerosis',
                    'Migraines', 'Traumatic Brain Injury', 'Other Neurological']
        },
        {
            key: 'musculoskeletal',
            title: 'Musculoskeletal & Immune',
            items: ['Osteoarthritis', 'Rheumatoid Arthritis', 'Fibromyalgia',
                    'Osteoporosis', 'Chronic Pain Syndrome', 'Lupus / Autoimmune',
                    'Asthma', 'COPD / Emphysema', 'Sleep Apnea']
        },
        {
            key: 'infections',
            title: 'Infections & Toxin-Related',
            items: ['Chronic Fatigue Syndrome', 'Long COVID', 'Lyme Disease',
                    'Mold Illness / CIRS', 'Frequent Infections', 'HIV / AIDS',
                    'POTS / Dysautonomia', 'Cancer (specify below)', 'Other']
        }
    ];

    // PDF verbatim — Family Medical History (check all that apply in blood relatives).
    const FAMILY_CONDITIONS = [
        'Heart Disease', 'Hypertension', 'Stroke',
        'Type 2 Diabetes', 'Cancer (any type)', 'Colon Cancer',
        'Breast / Ovarian Cancer', 'Prostate Cancer', "Alzheimer's / Dementia",
        "Parkinson's", 'Autoimmune Disease', 'Osteoporosis',
        'Mental Health Disorders', 'Substance Abuse', 'Kidney Disease',
        'Thyroid Disease', 'Genetic Disorders', 'Other'
    ];

    // PDF verbatim — Immunizations / Vaccinations (check all received).
    const IMMUNIZATIONS = [
        'Childhood Vaccines (full series)', 'Flu (annual)', 'COVID-19',
        'Shingles (Shingrix)', 'Pneumonia', 'Hepatitis A & B',
        'Tetanus (Tdap)', 'HPV', 'Travel Vaccines'
    ];

    class IntakeMedicalHistoryRenderer {
        constructor() {
            this.hint = 'Tell us about your past medical history. Leave anything blank you do not know. Check what applies; add a note if you want to tell us more.';
        }

        async render(container, data, mode) {
            this.container = container;
            container.innerHTML = '<div class="text-center text-muted py-4"><div class="spinner-border spinner-border-sm me-2"></div> Loading your history...</div>';

            const prefill = await fetchPrefillFor(this);
            const preProblems = new Map((prefill?.problems || []).map(p => [p.description, p.notes || '']));
            const preFamily = new Map((prefill?.family || []).map(f => [f.condition, { relation: f.relation || '', notes: f.notes || '' }]));
            const preImm = new Map((prefill?.immunizations || []).map(i => [i.vaccineName, i.notes || '']));
            const preAllergies = prefill?.allergies || [];

            container.innerHTML = `
                <!-- Chronic Problems / Conditions -->
                <h6 class="mt-1 text-primary"><i class="bi bi-clipboard2-pulse me-1"></i> Chronic Problems / Conditions</h6>
                <div class="text-muted small mb-2">Check all that apply (past or current). Add a note if you want to share details.</div>
                ${PROBLEM_CATEGORIES.map(cat => renderCheckboxCategory(cat.title, cat.key, cat.items, preProblems, 'imhProblem')).join('')}
                <div id="imhProblemsManual"></div>
                <button type="button" class="btn btn-outline-primary mb-3" data-add="problem">
                    <i class="bi bi-plus-lg me-1"></i> Add Problem (not listed above)
                </button>

                <div class="mb-4">
                    <label class="form-label"><strong>Cancer</strong> — specify type(s) and year(s)</label>
                    <textarea class="form-control" id="imhCancerSpecify" rows="2" placeholder="e.g. Breast cancer, 2019 (in remission)">${attr(prefill?.cancerSpecify)}</textarea>
                </div>

                <hr class="my-4">

                <!-- Allergies (free-form — no PDF checkbox list, patient-specific) -->
                <h6 class="mt-3 text-primary"><i class="bi bi-exclamation-triangle me-1"></i> Allergies</h6>
                <div class="text-muted small mb-2">Drug, food, or environmental allergies. Include reaction when you can.</div>
                <div id="imhAllergies"></div>
                <button type="button" class="btn btn-outline-primary mb-3" data-add="allergy">
                    <i class="bi bi-plus-lg me-1"></i> Add Allergy
                </button>

                <hr class="my-4">

                <!-- Family History -->
                <h6 class="mt-3 text-primary"><i class="bi bi-people me-1"></i> Family History (Blood Relatives)</h6>
                <div class="text-muted small mb-2">Check conditions that run in your family. Use the Relation field to specify who (e.g. mother, grandfather).</div>
                ${renderCheckboxCategory('', 'family', FAMILY_CONDITIONS, null, 'imhFamily', { family: preFamily })}
                <div id="imhFamilyManual"></div>
                <button type="button" class="btn btn-outline-primary mb-3" data-add="family">
                    <i class="bi bi-plus-lg me-1"></i> Add Family Condition (not listed above)
                </button>

                <hr class="my-4">

                <!-- Surgeries (free-form) -->
                <h6 class="mt-3 text-primary"><i class="bi bi-scissors me-1"></i> Surgeries, Procedures &amp; Hospitalizations (optional)</h6>
                <div id="imhSurgeries"></div>
                <button type="button" class="btn btn-outline-primary mb-3" data-add="surgery">
                    <i class="bi bi-plus-lg me-1"></i> Add Surgery
                </button>

                <hr class="my-4">

                <!-- Immunizations -->
                <h6 class="mt-3 text-primary"><i class="bi bi-shield-check me-1"></i> Immunizations / Vaccinations</h6>
                <div class="text-muted small mb-2">Check what you have received.</div>
                ${renderCheckboxCategory('', 'immunization', IMMUNIZATIONS, null, 'imhImmunization', { simple: preImm })}
                <div id="imhImmunizationsManual"></div>
                <button type="button" class="btn btn-outline-primary mb-3" data-add="immunization">
                    <i class="bi bi-plus-lg me-1"></i> Add Immunization (not listed above)
                </button>
            `;

            container.addEventListener('click', (e) => {
                const addBtn = e.target.closest('[data-add]');
                if (addBtn) this._addRow(addBtn.getAttribute('data-add'));
                const rem = e.target.closest('[data-remove]');
                if (rem) rem.closest('[data-row]')?.remove();
            });

            // Checkbox toggle expands/collapses its notes field
            container.querySelectorAll('.imh-checkbox').forEach(cb => {
                cb.addEventListener('change', () => {
                    const notesBlock = cb.closest('.imh-checkbox-item').querySelector('.imh-check-notes');
                    if (notesBlock) notesBlock.style.display = cb.checked ? '' : 'none';
                });
            });

            // Pre-populate manual rows for anything that was a manual entry (not in the fixed lists).
            const manualProblems = (prefill?.problems || []).filter(p => !isInFixedProblems(p.description));
            manualProblems.forEach(p => this._addRow('problem', p.description, p.notes));

            const manualFamily = (prefill?.family || []).filter(f => !FAMILY_CONDITIONS.includes(f.condition));
            manualFamily.forEach(f => this._addRow('family', null, f.notes, { relation: f.relation, condition: f.condition }));

            const manualImm = (prefill?.immunizations || []).filter(i => !IMMUNIZATIONS.includes(i.vaccineName));
            manualImm.forEach(i => this._addRow('immunization', i.vaccineName, i.notes));

            // Allergies and Surgeries are free-form: render existing rows, then leave
            // one empty row for new entry so patient has a form to fill.
            preAllergies.forEach(a => this._addRow('allergy', a.allergenName, a.notes));
            if (preAllergies.length === 0) this._addRow('allergy');
            this._addRow('surgery');
        }

        _addRow(kind, nameValue, notesValue, familyFields) {
            const listMap = {
                problem: 'imhProblemsManual',
                allergy: 'imhAllergies',
                family: 'imhFamilyManual',
                surgery: 'imhSurgeries',
                immunization: 'imhImmunizationsManual'
            };
            const list = document.getElementById(listMap[kind]);
            if (!list) return;

            const tmpl = document.createElement('div');
            tmpl.className = 'iw-item-card';
            tmpl.setAttribute('data-row', kind);

            if (kind === 'family') {
                tmpl.innerHTML = `
                    <div class="row g-2 align-items-end">
                        <div class="col-md-3"><label class="form-label small">Relation</label>
                            <input class="form-control" data-field="relation" placeholder="e.g. Mother" value="${attr(familyFields?.relation)}"></div>
                        <div class="col-md-4"><label class="form-label small">Condition</label>
                            <input class="form-control" data-field="condition" placeholder="e.g. Diabetes" value="${attr(familyFields?.condition)}"></div>
                        <div class="col-md-4"><label class="form-label small">Notes</label>
                            <input class="form-control" data-field="notes" value="${attr(notesValue)}"></div>
                        <div class="col-md-1 text-end"><button type="button" class="btn btn-outline-danger" data-remove><i class="bi bi-trash"></i></button></div>
                    </div>
                `;
            } else {
                const label = {
                    problem: 'Condition',
                    allergy: 'Allergen',
                    surgery: 'Surgery / Procedure',
                    immunization: 'Vaccine'
                }[kind];
                tmpl.innerHTML = `
                    <div class="row g-2 align-items-end">
                        <div class="col-md-4"><label class="form-label small">${label}</label>
                            <input class="form-control" data-field="name" value="${attr(nameValue)}"></div>
                        <div class="col-md-7"><label class="form-label small">Notes</label>
                            <input class="form-control" data-field="notes" value="${attr(notesValue)}" placeholder="Details you want your provider to see"></div>
                        <div class="col-md-1 text-end"><button type="button" class="btn btn-outline-danger" data-remove><i class="bi bi-trash"></i></button></div>
                    </div>
                `;
            }
            list.appendChild(tmpl);
        }

        _collectChecked(categoryKey) {
            const out = [];
            document.querySelectorAll(`[data-imh-kind="${categoryKey}"] .imh-checkbox:checked`).forEach(cb => {
                const item = cb.closest('.imh-checkbox-item');
                const label = cb.dataset.label;
                const notesEl = item.querySelector('.imh-check-notes input, .imh-check-notes textarea');
                const relationEl = item.querySelector('.imh-check-relation');
                out.push({
                    label,
                    notes: notesEl ? (notesEl.value || '').trim() : '',
                    relation: relationEl ? (relationEl.value || '').trim() : ''
                });
            });
            return out;
        }

        _collectManual(containerId) {
            return Array.from(document.querySelectorAll(`#${containerId} [data-row]`)).map(row => {
                const out = {};
                row.querySelectorAll('[data-field]').forEach(i => {
                    out[i.getAttribute('data-field')] = (i.value || '').trim();
                });
                return out;
            });
        }

        getPayload(sectionName) {
            if (sectionName === 'medical-history') {
                // Combine checked problems + manual problems.
                const checkedProblems = this._collectChecked('imhProblem')
                    .map(c => ({ description: c.label, notes: c.notes }));
                const manualProblems = this._collectManual('imhProblemsManual')
                    .filter(r => r.name)
                    .map(r => ({ description: r.name, notes: r.notes }));

                // Surgeries kept as problems tagged "Surgery." (Path A — no separate table).
                const surgeries = this._collectManual('imhSurgeries')
                    .filter(r => r.name)
                    .map(r => ({ description: r.name, notes: ('Surgery. ' + (r.notes || '')).trim() }));

                // Checked family + manual family.
                const checkedFamily = this._collectChecked('family')
                    .map(c => ({ relation: c.relation, condition: c.label, notes: c.notes }));
                const manualFamily = this._collectManual('imhFamilyManual')
                    .filter(r => r.condition);

                const cancerSpecify = (document.getElementById('imhCancerSpecify')?.value || '').trim();

                return {
                    problems: checkedProblems.concat(manualProblems, surgeries),
                    family: checkedFamily.concat(manualFamily),
                    cancerSpecify
                };
            }
            if (sectionName === 'allergies') {
                const items = this._collectManual('imhAllergies')
                    .filter(r => r.name)
                    .map(r => ({ allergenName: r.name, notes: r.notes }));
                return { items };
            }
            if (sectionName === 'immunizations') {
                const checked = this._collectChecked('imhImmunization')
                    .map(c => ({ vaccineName: c.label, notes: c.notes }));
                const manual = this._collectManual('imhImmunizationsManual')
                    .filter(r => r.name)
                    .map(r => ({ vaccineName: r.name, notes: r.notes }));
                return { items: checked.concat(manual) };
            }
            return null;
        }
    }

    // --- helpers ---

    function renderCheckboxCategory(title, categoryKey, items, preProblems, kindPrefix, extras) {
        const titleHtml = title ? `<div class="fw-semibold text-muted small mb-2">${escapeHtml(title)}</div>` : '';
        const preMap = preProblems || null;
        const familyPre = extras?.family || null;
        const simplePre = extras?.simple || null;

        const cols = items.map(item => {
            const isChecked = preMap ? preMap.has(item)
                : familyPre ? familyPre.has(item)
                : simplePre ? simplePre.has(item)
                : false;
            const notesVal = preMap?.get(item)
                ?? familyPre?.get(item)?.notes
                ?? simplePre?.get(item)
                ?? '';
            const relationVal = familyPre?.get(item)?.relation || '';
            const relationField = familyPre
                ? `<input type="text" class="form-control form-control-sm imh-check-relation" placeholder="Relation (e.g. Mother)" value="${attr(relationVal)}">`
                : '';

            return `
                <div class="col-md-4 imh-checkbox-item mb-2">
                    <div class="form-check">
                        <input class="form-check-input imh-checkbox" type="checkbox"
                               id="imh_${categoryKey}_${slug(item)}" data-label="${attr(item)}"
                               ${isChecked ? 'checked' : ''}>
                        <label class="form-check-label" for="imh_${categoryKey}_${slug(item)}">${escapeHtml(item)}</label>
                    </div>
                    <div class="imh-check-notes mt-1 ps-4" style="display:${isChecked ? '' : 'none'};">
                        ${relationField}
                        <input type="text" class="form-control form-control-sm mt-1" placeholder="Optional notes" value="${attr(notesVal)}">
                    </div>
                </div>
            `;
        }).join('');

        return `
            <div class="imh-checkbox-category mb-3" data-imh-kind="${categoryKey === 'family' ? 'family' : kindPrefix}">
                ${titleHtml}
                <div class="row">${cols}</div>
            </div>
        `;
    }

    function isInFixedProblems(description) {
        return PROBLEM_CATEGORIES.some(cat => cat.items.includes(description));
    }

    function slug(s) {
        return String(s).toLowerCase().replace(/[^a-z0-9]+/g, '_').replace(/^_|_$/g, '');
    }

    function escapeHtml(s) {
        return String(s || '').replace(/[&<>"']/g, c => ({
            '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
        }[c]));
    }

    function attr(v) {
        if (v === null || v === undefined) return '';
        return String(v).replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

    window.IntakeMedicalHistoryRenderer = IntakeMedicalHistoryRenderer;
})();
