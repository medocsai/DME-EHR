/**
 * IntakeMedicationsRenderer (Employee of IntakeFormRenderer)
 *
 * Why: render Section 6 (Medications & Supplements) per PDF.
 * What: Rx medications (free-form rows, auto-add first), Supplements (same),
 *       plus 4 patient-level fields: Drug Reaction history, Primary Pharmacy,
 *       Compounding / Specialty Pharmacy, Current Healthcare Team.
 * Who calls: IntakeFormRenderer with multi = ['medications','supplements'].
 * Returns: DOM + getPayload(sectionName).
 *
 * Pre-fills via GET /api/intake/medications/prefill on mount.
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

    class IntakeMedicationsRenderer {
        constructor() {
            this.hint = 'What are you currently taking? It is fine to leave sections empty if they do not apply.';
        }

        async render(container, data, mode) {
            this.container = container;
            container.innerHTML = '<div class="text-center text-muted py-4"><div class="spinner-border spinner-border-sm me-2"></div> Loading...</div>';

            const prefill = await fetchPrefillFor(this);

            container.innerHTML = `
                <h6 class="mt-1 text-primary"><i class="bi bi-capsule me-1"></i> Prescription Medications</h6>
                <div id="imMeds"></div>
                <button type="button" class="btn btn-outline-primary mb-3" data-add="med">
                    <i class="bi bi-plus-lg me-1"></i> Add Medication
                </button>

                <h6 class="mt-3 text-primary"><i class="bi bi-prescription me-1"></i> Supplements / Vitamins</h6>
                <div id="imSupps"></div>
                <button type="button" class="btn btn-outline-primary mb-3" data-add="supp">
                    <i class="bi bi-plus-lg me-1"></i> Add Supplement
                </button>

                <hr class="my-4">

                <div class="row g-3">
                    <div class="col-12">
                        <label class="form-label"><strong>Drug Reaction or Adverse Medication History</strong></label>
                        <textarea class="form-control" id="imDrugReactions" rows="2"
                                  placeholder="Any medications you've had bad reactions to, side effects you can't tolerate, etc.">${txt(prefill?.drugReactionHistory)}</textarea>
                    </div>
                    <div class="col-md-6">
                        <label class="form-label"><strong>Primary Pharmacy &amp; Phone</strong></label>
                        <input class="form-control" id="imPrimaryPharmacy" placeholder="e.g. CVS Main St — (555) 123-4567" value="${attr(prefill?.primaryPharmacyInfo)}" />
                    </div>
                    <div class="col-md-6">
                        <label class="form-label"><strong>Compounding / Specialty Pharmacy &amp; Phone</strong> <span class="text-muted small">(optional)</span></label>
                        <input class="form-control" id="imCompoundingPharmacy" placeholder="e.g. Wellness Compounders — (555) 987-6543" value="${attr(prefill?.compoundingPharmacyInfo)}" />
                    </div>
                    <div class="col-12">
                        <label class="form-label"><strong>Current Healthcare Team</strong></label>
                        <div class="form-text mb-1">Primary Care, Specialists, Naturopath, Chiropractor, Therapist, etc. Include names and cities if helpful.</div>
                        <textarea class="form-control" id="imHealthcareTeam" rows="3">${txt(prefill?.healthcareTeamNotes)}</textarea>
                    </div>
                </div>
            `;

            container.addEventListener('click', (e) => {
                const a = e.target.closest('[data-add]');
                if (a) this._addRow(a.getAttribute('data-add'));
                const r = e.target.closest('[data-remove]');
                if (r) r.closest('[data-row]')?.remove();
            });

            // Pre-fill meds/supps rows from existing data, or auto-add one empty row each.
            const existingMeds = prefill?.medications || [];
            if (existingMeds.length > 0) {
                existingMeds.forEach(m => this._addRow('med', m.drugName, m.notes));
            } else {
                this._addRow('med');
            }

            const existingSupps = prefill?.supplements || [];
            if (existingSupps.length > 0) {
                existingSupps.forEach(s => this._addRow('supp', s.supplementName, s.notes));
            } else {
                this._addRow('supp');
            }
        }

        _addRow(kind, nameValue, notesValue) {
            const id = kind === 'med' ? 'imMeds' : 'imSupps';
            const label = kind === 'med' ? 'Medication Name' : 'Supplement Name';
            const hint = kind === 'med'
                ? 'dose, how often, since when, reason'
                : 'brand, dose, frequency, duration';
            const list = document.getElementById(id);
            if (!list) return;
            const tmpl = document.createElement('div');
            tmpl.className = 'iw-item-card';
            tmpl.setAttribute('data-row', kind);
            tmpl.innerHTML = `
                <div class="row g-2 align-items-end">
                    <div class="col-md-4"><label class="form-label small">${label}</label>
                        <input class="form-control" data-field="name" value="${attr(nameValue)}"></div>
                    <div class="col-md-7"><label class="form-label small">Notes (${hint})</label>
                        <input class="form-control" data-field="notes" value="${attr(notesValue)}"></div>
                    <div class="col-md-1 text-end"><button type="button" class="btn btn-outline-danger" data-remove><i class="bi bi-trash"></i></button></div>
                </div>
            `;
            list.appendChild(tmpl);
        }

        _collect(containerId) {
            return Array.from(document.querySelectorAll(`#${containerId} [data-row]`)).map(row => {
                const n = row.querySelector('[data-field="name"]')?.value.trim() || '';
                const notes = row.querySelector('[data-field="notes"]')?.value.trim() || '';
                return n ? { name: n, notes } : null;
            }).filter(Boolean);
        }

        getPayload(sectionName) {
            if (sectionName === 'medications') {
                const items = this._collect('imMeds').map(r => ({ drugName: r.name, notes: r.notes }));
                // Patient-level fields travel with the medications section.
                return {
                    items,
                    drugReactionHistory: (document.getElementById('imDrugReactions')?.value || '').trim(),
                    primaryPharmacyInfo: (document.getElementById('imPrimaryPharmacy')?.value || '').trim(),
                    compoundingPharmacyInfo: (document.getElementById('imCompoundingPharmacy')?.value || '').trim(),
                    healthcareTeamNotes: (document.getElementById('imHealthcareTeam')?.value || '').trim()
                };
            }
            if (sectionName === 'supplements') {
                const items = this._collect('imSupps').map(r => ({ supplementName: r.name, notes: r.notes }));
                return { items };
            }
            return null;
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

    window.IntakeMedicationsRenderer = IntakeMedicationsRenderer;
})();
