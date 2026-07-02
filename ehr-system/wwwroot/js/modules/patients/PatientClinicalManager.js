/**
 * PatientClinicalManager - Manages clinical data tabs in the Patient Profile modal.
 * Handles Problems, Allergies, Medications, Vitals, Immunizations, and History tabs.
 */
class PatientClinicalManager {
    constructor() {
        this.patientId = null;
        this.loadedTabs = new Set();
        this.data = {
            problems: [], allergies: [], medications: [],
            vitals: [], immunizations: [], familyHistory: [], socialHistory: [],
            encounters: [], treatmentPlans: []
        };
    }

    _getHeaders() {
        return PatientUtilities.getHeaders();
    }

    async _fetch(url) {
        const resp = await fetch(url, { headers: this._getHeaders() });
        if (!resp.ok) throw new Error(`API error ${resp.status}`);
        return resp.json();
    }

    async _post(url, body) {
        const resp = await fetch(url, {
            method: 'POST', headers: this._getHeaders(), body: JSON.stringify(body)
        });
        if (!resp.ok) { const err = await resp.text(); throw new Error(err); }
        return resp.json();
    }

    async _put(url, body) {
        const resp = await fetch(url, {
            method: 'PUT', headers: this._getHeaders(), body: JSON.stringify(body)
        });
        if (!resp.ok) throw new Error(`API error ${resp.status}`);
        return resp.json();
    }

    async _delete(url) {
        const resp = await fetch(url, { method: 'DELETE', headers: this._getHeaders() });
        if (!resp.ok) throw new Error(`API error ${resp.status}`);
        return resp.json();
    }

    /** POST to /api/history-review/{section}/{actionUrl} with reason (Phase 4 endpoint).
     *  Spec: rules/technical/history-review-soft-delete.md (sections 7, 9). */
    async _postHistoryAction(sectionApi, actionUrl, recordId, reason) {
        const resp = await fetch(`/api/history-review/${sectionApi}/${actionUrl}`, {
            method: 'POST',
            headers: this._getHeaders(),
            body: JSON.stringify({ recordId, reason })
        });
        if (!resp.ok) {
            const err = await resp.text().catch(() => `API error ${resp.status}`);
            throw new Error(err || `API error ${resp.status}`);
        }
        return resp.json().catch(() => ({}));
    }

    /** Open the shared HistoryActionModal for a given action and reload the relevant tab on success. */
    _openHistoryActionModal({ section, sectionApi, action, actionUrl, recordId, recordLabel, reload }) {
        if (!window.HistoryActionModal) {
            console.error('[PatientClinicalManager] HistoryActionModal not loaded');
            return;
        }
        window.HistoryActionModal.open({
            section: sectionApi,
            action: action,
            recordId: recordId,
            recordLabel: recordLabel,
            onConfirm: async (reason) => {
                await this._postHistoryAction(sectionApi, actionUrl, recordId, reason);
                if (typeof reload === 'function') await reload();
            }
        });
    }

    initialize(patientId) {
        this.patientId = patientId;
        this.loadedTabs.clear();
    }

    // ─── TAB LOADERS ─────────────────────────────────────────

    async loadProblems() {
        if (!this.patientId) return;
        const container = document.getElementById('clinicalProblemsContent');
        if (!container) return;
        container.innerHTML = '<div class="text-center py-3"><div class="spinner-border spinner-border-sm"></div> Loading...</div>';
        try {
            this.data.problems = await this._fetch(`/api/patients/${this.patientId}/problems`);
            this._renderProblems(container);
        } catch (e) { container.innerHTML = '<div class="text-danger">Failed to load problems</div>'; }
    }

    async loadAllergies() {
        if (!this.patientId) return;
        const container = document.getElementById('clinicalAllergiesContent');
        if (!container) return;
        container.innerHTML = '<div class="text-center py-3"><div class="spinner-border spinner-border-sm"></div> Loading...</div>';
        try {
            this.data.allergies = await this._fetch(`/api/patients/${this.patientId}/allergies`);
            this._renderAllergies(container);
        } catch (e) { container.innerHTML = '<div class="text-danger">Failed to load allergies</div>'; }
    }

    async loadMedications() {
        if (!this.patientId) return;
        const container = document.getElementById('clinicalMedicationsContent');
        if (!container) return;
        container.innerHTML = '<div class="text-center py-3"><div class="spinner-border spinner-border-sm"></div> Loading...</div>';
        try {
            this.data.medications = await this._fetch(`/api/patients/${this.patientId}/medications`);
            this._renderMedications(container);
        } catch (e) { container.innerHTML = '<div class="text-danger">Failed to load medications</div>'; }
    }

    async loadVitals() {
        if (!this.patientId) return;
        const container = document.getElementById('clinicalVitalsContent');
        if (!container) return;
        container.innerHTML = '<div class="text-center py-3"><div class="spinner-border spinner-border-sm"></div> Loading...</div>';
        try {
            this.data.vitals = await this._fetch(`/api/patients/${this.patientId}/vitals`);
            this._renderVitals(container);
        } catch (e) { container.innerHTML = '<div class="text-danger">Failed to load vitals</div>'; }
    }

    async loadImmunizations() {
        if (!this.patientId) return;
        const container = document.getElementById('clinicalImmunizationsContent');
        if (!container) return;
        container.innerHTML = '<div class="text-center py-3"><div class="spinner-border spinner-border-sm"></div> Loading...</div>';
        try {
            this.data.immunizations = await this._fetch(`/api/patients/${this.patientId}/immunizations`);
            this._renderImmunizations(container);
        } catch (e) { container.innerHTML = '<div class="text-danger">Failed to load immunizations</div>'; }
    }

    async loadHistory() {
        if (!this.patientId) return;
        const container = document.getElementById('clinicalHistoryContent');
        if (!container) return;
        container.innerHTML = '<div class="text-center py-3"><div class="spinner-border spinner-border-sm"></div> Loading...</div>';
        try {
            const [fam, soc] = await Promise.all([
                this._fetch(`/api/patients/${this.patientId}/family-history`),
                this._fetch(`/api/patients/${this.patientId}/social-history`)
            ]);
            this.data.familyHistory = fam;
            this.data.socialHistory = soc;
            this._renderHistory(container);
        } catch (e) { container.innerHTML = '<div class="text-danger">Failed to load history</div>'; }
    }

    // ─── RENDERERS ────────────────────────────────────────────

    _renderProblems(container) {
        const items = this.data.problems;
        const active = items.filter(p => p.Status === 0);
        const resolved = items.filter(p => p.Status !== 0);
        container.innerHTML = `
            <div class="d-flex justify-content-between align-items-center mb-3">
                <h6 class="mb-0"><i class="bi bi-list-check me-1"></i>Problem List (${items.length})</h6>
                <button class="btn btn-primary" onclick="window.patientClinicalManager._showAddProblem()">
                    <i class="bi bi-plus-lg me-1"></i>Add Problem
                </button>
            </div>
            ${items.length === 0 ? '<div class="text-muted text-center py-3">No problems recorded</div>' : ''}
            ${active.length > 0 ? `
                <div class="mb-3">
                    <h6 class="text-success small fw-bold mb-2">Active Problems</h6>
                    <div class="list-group list-group-flush">
                        ${active.map(p => this._problemRow(p)).join('')}
                    </div>
                </div>
            ` : ''}
            ${resolved.length > 0 ? `
                <div>
                    <h6 class="text-secondary small fw-bold mb-2">Resolved / Inactive</h6>
                    <div class="list-group list-group-flush">
                        ${resolved.map(p => this._problemRow(p)).join('')}
                    </div>
                </div>
            ` : ''}
        `;
    }

    _problemRow(p) {
        const statusBadge = p.Status === 0 ? '<span class="badge bg-success">Active</span>'
            : p.Status === 1 ? '<span class="badge bg-secondary">Resolved</span>'
            : '<span class="badge bg-warning text-dark">Inactive</span>';
        const isActive = p.Status === 0;
        return `
            <div class="list-group-item d-flex justify-content-between align-items-start px-2 py-2">
                <div>
                    <div class="fw-semibold ${isActive ? '' : 'text-decoration-line-through text-muted'}">${this._esc(p.Description)}</div>
                    <small class="text-muted">
                        ICD-10: <strong>${this._esc(p.IcdCode || 'N/A')}</strong>
                        ${p.OnsetDate ? ` | Onset: ${p.OnsetDate}` : ''}
                        ${p.Notes ? ` | ${this._esc(p.Notes)}` : ''}
                    </small>
                </div>
                <div class="d-flex align-items-center gap-2">
                    ${statusBadge}
                    <div class="dropdown">
                        <button class="btn btn-link text-muted p-0" data-bs-toggle="dropdown"><i class="bi bi-three-dots-vertical"></i></button>
                        <ul class="dropdown-menu dropdown-menu-end">
                            ${isActive ? `
                                <li><a class="dropdown-item" href="#" onclick="window.patientClinicalManager._resolveProblem(${p.PatientProblemId}); return false;"><i class="bi bi-check-circle me-1"></i>Mark Resolved</a></li>
                                <li><a class="dropdown-item" href="#" onclick="window.patientClinicalManager._inactivateProblem(${p.PatientProblemId}); return false;"><i class="bi bi-pause-circle me-1"></i>Mark Inactive</a></li>
                            ` : `
                                <li><a class="dropdown-item" href="#" onclick="window.patientClinicalManager._reactivateProblem(${p.PatientProblemId}); return false;"><i class="bi bi-arrow-counterclockwise me-1"></i>Reactivate</a></li>
                            `}
                            <li><hr class="dropdown-divider"></li>
                            <li><a class="dropdown-item text-danger" href="#" onclick="window.patientClinicalManager._deleteProblem(${p.PatientProblemId}); return false;"><i class="bi bi-trash me-1"></i>Delete</a></li>
                        </ul>
                    </div>
                </div>
            </div>
        `;
    }

    _renderAllergies(container) {
        const items = this.data.allergies;
        container.innerHTML = `
            <div class="d-flex justify-content-between align-items-center mb-3">
                <h6 class="mb-0"><i class="bi bi-exclamation-triangle me-1"></i>Allergies (${items.length})</h6>
                <button class="btn btn-primary" onclick="window.patientClinicalManager._showAddAllergy()">
                    <i class="bi bi-plus-lg me-1"></i>Add Allergy
                </button>
            </div>
            ${items.length === 0 ? '<div class="text-muted text-center py-3">No known allergies (NKA)</div>' : `
                <div class="table-responsive">
                    <table class="table table-sm table-hover mb-0">
                        <thead><tr>
                            <th style="width:30%">Allergen</th><th>Notes</th><th style="width:10%">Status</th><th style="width:5%"></th>
                        </tr></thead>
                        <tbody>
                            ${items.map(a => `
                                <tr>
                                    <td class="fw-semibold ${a.IsActive ? '' : 'text-decoration-line-through text-muted'}">${this._esc(a.AllergenName)}</td>
                                    <td class="text-muted">${this._esc(a.Notes || '')}</td>
                                    <td>${a.IsActive ? '<span class="badge bg-success">Active</span>' : '<span class="badge bg-secondary">Inactive</span>'}</td>
                                    <td>
                                        <div class="dropdown">
                                            <button class="btn btn-link text-muted p-0" data-bs-toggle="dropdown"><i class="bi bi-three-dots-vertical"></i></button>
                                            <ul class="dropdown-menu dropdown-menu-end">
                                                ${a.IsActive
                                                    ? `<li><a class="dropdown-item" href="#" onclick="window.patientClinicalManager._inactivateAllergy(${a.PatientAllergyId}); return false;"><i class="bi bi-pause-circle me-1"></i>Mark Inactive</a></li>`
                                                    : `<li><a class="dropdown-item" href="#" onclick="window.patientClinicalManager._reactivateAllergy(${a.PatientAllergyId}); return false;"><i class="bi bi-arrow-counterclockwise me-1"></i>Reactivate</a></li>`
                                                }
                                                <li><hr class="dropdown-divider"></li>
                                                <li><a class="dropdown-item text-danger" href="#" onclick="window.patientClinicalManager._deleteAllergy(${a.PatientAllergyId}); return false;"><i class="bi bi-trash me-1"></i>Delete</a></li>
                                            </ul>
                                        </div>
                                    </td>
                                </tr>
                            `).join('')}
                        </tbody>
                    </table>
                </div>
            `}
        `;
    }

    _severityBadge(severity, name) {
        const classes = { 0: 'bg-info', 1: 'bg-warning text-dark', 2: 'bg-danger', 3: 'bg-dark' };
        return `<span class="badge ${classes[severity] || 'bg-secondary'}">${this._esc(name || 'Unknown')}</span>`;
    }

    _renderMedications(container) {
        const items = this.data.medications;
        const active = items.filter(m => m.Status === 0);
        const other = items.filter(m => m.Status !== 0);

        const renderCard = (m) => {
            const isActive = m.Status === 0;
            const badgeClass = isActive ? 'bg-success' : (m.Status === 1 ? 'bg-danger' : 'bg-warning text-dark');
            const cardCls = isActive ? 'rx-card med-layout active-unlinked' : 'rx-card med-layout discontinued';

            // NOTE: "Hold", "Resume", and "Complete" buttons are intentionally hidden
            // from the UI for now (per stakeholder decision 2026-04-25). Backend
            // remains implemented (see HistoryReviewActionService and the _holdMed,
            // _resumeMed, _completeMed methods below). To re-enable, restore the
            // commented-out buttons below. DB Status enum already supports OnHold (2)
            // and Completed (3); legacy data with those statuses will still display
            // a "Discontinued"/"Completed" badge.
            let buttons = '';
            if (isActive) {
                // Active medication: Discontinue + Delete (Hold/Complete hidden)
                buttons += `<button class="rx-btn rx-btn-warn" onclick="window.patientClinicalManager._discontinueMed(${m.PatientMedicationId})"><i class="bi bi-x-circle"></i> Discontinue</button>`;
                // Hidden — uncomment to re-enable:
                // buttons += `<button class="rx-btn rx-btn-warn" onclick="window.patientClinicalManager._holdMed(${m.PatientMedicationId})"><i class="bi bi-pause-circle"></i> Hold</button>`;
                // buttons += `<button class="rx-btn rx-btn-success" onclick="window.patientClinicalManager._completeMed(${m.PatientMedicationId})"><i class="bi bi-check-circle"></i> Complete</button>`;
            } else if (m.Status === 1) {
                // Discontinued: Revert
                buttons += `<button class="rx-btn rx-btn-success" onclick="window.patientClinicalManager._revertMed(${m.PatientMedicationId})"><i class="bi bi-arrow-counterclockwise"></i> Revert Discontinue</button>`;
            }
            // Hidden — Resume button for OnHold (Status === 2). Uncomment to re-enable:
            // else if (m.Status === 2) {
            //     buttons += `<button class="rx-btn rx-btn-success" onclick="window.patientClinicalManager._resumeMed(${m.PatientMedicationId})"><i class="bi bi-play-circle"></i> Resume</button>`;
            // }
            buttons += `<button class="rx-btn rx-btn-danger" onclick="window.patientClinicalManager._deleteMed(${m.PatientMedicationId})"><i class="bi bi-trash"></i> Delete</button>`;

            return `
                <div class="${cardCls}">
                    <div class="rx-card-body">
                        <div class="rx-card-head">
                            <div class="drug-icon"><i class="bi bi-capsule"></i></div>
                            <div class="drug-name">${this._esc(m.DrugName)}</div>
                            <span class="badge ${badgeClass}">${this._esc(m.StatusName)}</span>
                        </div>
                        ${m.Notes ? `<div class="rx-card-notes">${this._esc(m.Notes)}</div>` : ''}
                        ${m.StartDate ? `<div class="rx-card-meta">
                            <span><i class="bi bi-calendar3"></i><strong>Started:</strong> ${m.StartDate}</span>
                            ${m.EndDate ? `<span><i class="bi bi-calendar-x"></i><strong>Ended:</strong> ${m.EndDate}</span>` : ''}
                        </div>` : ''}
                    </div>
                    <div class="rx-card-actions">${buttons}</div>
                </div>
            `;
        };

        container.innerHTML = `
            <div class="d-flex justify-content-between align-items-center mb-3">
                <h6 class="mb-0"><i class="bi bi-capsule me-1"></i>Medications (${items.length})</h6>
                <button class="btn btn-primary" onclick="window.patientClinicalManager._showAddMedication()">
                    <i class="bi bi-plus-lg me-1"></i>Add Medication
                </button>
            </div>
            ${items.length === 0
                ? '<div class="text-muted text-center py-3">No medications recorded</div>'
                : `
                    ${active.length > 0 ? `
                        <div class="rx-group-label">Active (${active.length})</div>
                        <div class="rx-card-list">${active.map(renderCard).join('')}</div>
                    ` : ''}
                    ${other.length > 0 ? `
                        <div class="rx-group-label rx-group-label-muted">Discontinued (${other.length})</div>
                        <div class="rx-card-list">${other.map(renderCard).join('')}</div>
                    ` : ''}
                `
            }
        `;
    }

    _renderVitals(container) {
        const items = this.data.vitals;
        const latest = items[0]; // already sorted by RecordedAt desc
        container.innerHTML = `
            <div class="d-flex justify-content-between align-items-center mb-3">
                <h6 class="mb-0"><i class="bi bi-activity me-1"></i>Vitals (${items.length} records)</h6>
                <button class="btn btn-primary" onclick="window.patientClinicalManager._showAddVitals()">
                    <i class="bi bi-plus-lg me-1"></i>Record Vitals
                </button>
            </div>
            ${latest ? `
                <div class="card mb-3">
                    <div class="card-header py-2"><small class="fw-bold">Latest Vitals - ${new Date(latest.RecordedAt).toLocaleDateString()}</small></div>
                    <div class="card-body py-2">
                        <div class="row g-3 text-center">
                            <div class="col-4 col-md-2">
                                <div class="small text-muted">BP</div>
                                <div class="fw-bold ${this._bpClass(latest.SystolicBp)}">${latest.BloodPressure || 'N/A'}</div>
                            </div>
                            <div class="col-4 col-md-2">
                                <div class="small text-muted">HR</div>
                                <div class="fw-bold">${latest.HeartRate || 'N/A'} <small>bpm</small></div>
                            </div>
                            <div class="col-4 col-md-2">
                                <div class="small text-muted">Temp</div>
                                <div class="fw-bold">${latest.Temperature ? latest.Temperature + '°F' : 'N/A'}</div>
                            </div>
                            <div class="col-4 col-md-2">
                                <div class="small text-muted">SpO2</div>
                                <div class="fw-bold">${latest.SpO2 ? latest.SpO2 + '%' : 'N/A'}</div>
                            </div>
                            <div class="col-4 col-md-2">
                                <div class="small text-muted">Weight</div>
                                <div class="fw-bold">${latest.Weight ? latest.Weight + ' lbs' : 'N/A'}</div>
                            </div>
                            <div class="col-4 col-md-2">
                                <div class="small text-muted">BMI</div>
                                <div class="fw-bold ${this._bmiClass(latest.Bmi)}">${latest.Bmi || 'N/A'}</div>
                            </div>
                        </div>
                    </div>
                </div>
            ` : ''}
            ${items.length > 1 ? `
                <h6 class="small fw-bold mb-2">Vitals History</h6>
                <div class="table-responsive">
                    <table class="table table-sm table-hover mb-0">
                        <thead><tr><th>Date</th><th>BP</th><th>HR</th><th>Temp</th><th>SpO2</th><th>Wt</th><th>BMI</th></tr></thead>
                        <tbody>
                            ${items.map(v => `
                                <tr>
                                    <td>${new Date(v.RecordedAt).toLocaleDateString()}</td>
                                    <td class="${this._bpClass(v.SystolicBp)}">${v.BloodPressure || '-'}</td>
                                    <td>${v.HeartRate || '-'}</td>
                                    <td>${v.Temperature || '-'}</td>
                                    <td>${v.SpO2 || '-'}</td>
                                    <td>${v.Weight || '-'}</td>
                                    <td class="${this._bmiClass(v.Bmi)}">${v.Bmi || '-'}</td>
                                </tr>
                            `).join('')}
                        </tbody>
                    </table>
                </div>
            ` : (items.length === 0 ? '<div class="text-muted text-center py-3">No vitals recorded</div>' : '')}
        `;
    }

    _bpClass(systolic) {
        if (!systolic) return '';
        if (systolic >= 140) return 'text-danger';
        if (systolic >= 130) return 'text-warning';
        return '';
    }

    _bmiClass(bmi) {
        if (!bmi) return '';
        if (bmi >= 30) return 'text-danger';
        if (bmi >= 25) return 'text-warning';
        return '';
    }

    _renderImmunizations(container) {
        const items = this.data.immunizations;
        container.innerHTML = `
            <div class="d-flex justify-content-between align-items-center mb-3">
                <h6 class="mb-0"><i class="bi bi-shield-check me-1"></i>Immunizations (${items.length})</h6>
                <button class="btn btn-primary" onclick="window.patientClinicalManager._showAddImmunization()">
                    <i class="bi bi-plus-lg me-1"></i>Add Immunization
                </button>
            </div>
            ${items.length === 0 ? '<div class="text-muted text-center py-3">No immunizations recorded</div>' : `
                <div class="table-responsive">
                    <table class="table table-sm table-hover mb-0">
                        <thead><tr><th style="width:30%">Vaccine</th><th style="width:12%">Date</th><th>Notes</th><th style="width:5%"></th></tr></thead>
                        <tbody>
                            ${items.map(i => `
                                <tr>
                                    <td class="fw-semibold">${this._esc(i.VaccineName)}</td>
                                    <td>${i.AdministeredDate}</td>
                                    <td class="text-muted">${this._esc(i.Notes || '')}</td>
                                    <td>
                                        <button class="btn btn-sm btn-link text-danger p-0" onclick="window.patientClinicalManager._deleteImmunization(${i.PatientImmunizationId})">
                                            <i class="bi bi-trash"></i>
                                        </button>
                                    </td>
                                </tr>
                            `).join('')}
                        </tbody>
                    </table>
                </div>
            `}
        `;
    }

    _renderHistory(container) {
        const fam = this.data.familyHistory;
        const soc = this.data.socialHistory;
        container.innerHTML = `
            <div class="row">
                <div class="col-md-6">
                    <div class="d-flex justify-content-between align-items-center mb-2">
                        <h6 class="mb-0"><i class="bi bi-people me-1"></i>Family History (${fam.length})</h6>
                        <button class="btn btn-outline-primary" onclick="window.patientClinicalManager._showAddFamilyHistory()">
                            <i class="bi bi-plus-lg"></i>
                        </button>
                    </div>
                    ${fam.length === 0 ? '<div class="text-muted small py-2">No family history recorded</div>' : `
                        <div class="list-group list-group-flush">
                            ${fam.map(f => `
                                <div class="list-group-item px-2 py-2 d-flex justify-content-between">
                                    <div>
                                        <span class="fw-semibold">${this._esc(f.Condition)}</span>
                                        ${f.Notes ? `<br><small class="text-muted">${this._esc(f.Notes)}</small>` : ''}
                                    </div>
                                    <button class="btn btn-sm btn-link text-danger p-0" onclick="window.patientClinicalManager._deleteFamilyHistory(${f.PatientFamilyHistoryId})">
                                        <i class="bi bi-trash"></i>
                                    </button>
                                </div>
                            `).join('')}
                        </div>
                    `}
                </div>
                <div class="col-md-6">
                    <div class="d-flex justify-content-between align-items-center mb-2">
                        <h6 class="mb-0"><i class="bi bi-person-lines-fill me-1"></i>Social History (${soc.length})</h6>
                        <button class="btn btn-outline-primary" onclick="window.patientClinicalManager._showAddSocialHistory()">
                            <i class="bi bi-plus-lg"></i>
                        </button>
                    </div>
                    ${soc.length === 0 ? '<div class="text-muted small py-2">No social history recorded</div>' : `
                        <div class="list-group list-group-flush">
                            ${soc.map(s => `
                                <div class="list-group-item px-2 py-2 d-flex justify-content-between">
                                    <div>
                                        <span class="fw-semibold">${this._esc(s.Category)}</span>
                                        ${s.Notes ? `<br><small class="text-muted">${this._esc(s.Notes)}</small>` : ''}
                                    </div>
                                    <button class="btn btn-sm btn-link text-danger p-0" onclick="window.patientClinicalManager._deleteSocialHistory(${s.PatientSocialHistoryId})">
                                        <i class="bi bi-trash"></i>
                                    </button>
                                </div>
                            `).join('')}
                        </div>
                    `}
                </div>
            </div>
        `;
    }

    // ─── ADD FORMS ──────────────────────────────────────────

    _showAddProblem() {
        this._showInlineForm('clinicalProblemsContent', `
            <div class="card pcm-inline-form mb-3">
                <div class="card-header d-flex justify-content-between align-items-center py-2">
                    <span class="text-primary"><i class="bi bi-plus-circle text-primary me-2"></i>Add Problem</span>
                    <button type="button" class="btn-close" aria-label="Close" onclick="window.patientClinicalManager._cancelInlineForm('clinicalProblemsContent')"></button>
                </div>
                <div class="card-body">
                    <div class="row g-2">
                        <div class="col-md-7"><input type="text" class="form-control" id="addProblemDesc" placeholder="Description *" required></div>
                        <div class="col-md-3"><input type="text" class="form-control" id="addProblemNotes" placeholder="Notes (optional)"></div>
                        <div class="col-md-2">
                            <button class="btn btn-primary w-100" onclick="window.patientClinicalManager._saveProblem()">Save</button>
                        </div>
                    </div>
                </div>
            </div>
        `);
    }

    async _saveProblem() {
        const desc = document.getElementById('addProblemDesc')?.value?.trim();
        if (!desc) { this._toast('Description is required', 'warning'); return; }
        try {
            await this._post(`/api/patients/${this.patientId}/problems`, {
                description: desc,
                notes: document.getElementById('addProblemNotes')?.value?.trim() || null
            });
            this._toast('Problem added');
            this.loadProblems();
        } catch (e) { this._toast('Failed to add problem', 'danger'); }
    }

    _resolveProblem(id) {
        const item = (this.data.problems || []).find(p => p.PatientProblemId === id);
        this._openHistoryActionModal({
            sectionApi: 'problem', action: 'markResolved', actionUrl: 'mark-resolved',
            recordId: id, recordLabel: (item && item.Description) || '',
            reload: () => this.loadProblems()
        });
    }

    _inactivateProblem(id) {
        const item = (this.data.problems || []).find(p => p.PatientProblemId === id);
        this._openHistoryActionModal({
            sectionApi: 'problem', action: 'inactivate', actionUrl: 'mark-inactive',
            recordId: id, recordLabel: (item && item.Description) || '',
            reload: () => this.loadProblems()
        });
    }

    _reactivateProblem(id) {
        const item = (this.data.problems || []).find(p => p.PatientProblemId === id);
        this._openHistoryActionModal({
            sectionApi: 'problem', action: 'reactivate', actionUrl: 'reactivate',
            recordId: id, recordLabel: (item && item.Description) || '',
            reload: () => this.loadProblems()
        });
    }

    _deleteProblem(id) {
        const item = (this.data.problems || []).find(p => p.PatientProblemId === id);
        this._openHistoryActionModal({
            sectionApi: 'problem', action: 'delete', actionUrl: 'delete',
            recordId: id, recordLabel: (item && item.Description) || '',
            reload: () => this.loadProblems()
        });
    }

    _showAddAllergy() {
        this._showInlineForm('clinicalAllergiesContent', `
            <div class="card pcm-inline-form mb-3">
                <div class="card-header d-flex justify-content-between align-items-center py-2">
                    <span class="text-primary"><i class="bi bi-plus-circle text-primary me-2"></i>Add Allergy</span>
                    <button type="button" class="btn-close" aria-label="Close" onclick="window.patientClinicalManager._cancelInlineForm('clinicalAllergiesContent')"></button>
                </div>
                <div class="card-body">
                    <div class="row g-2">
                        <div class="col-md-7"><input type="text" class="form-control" id="addAllergyName" placeholder="Allergen Name *" required></div>
                        <div class="col-md-3"><input type="text" class="form-control" id="addAllergyNotes" placeholder="Notes (optional)"></div>
                        <div class="col-md-2">
                            <button class="btn btn-primary w-100" onclick="window.patientClinicalManager._saveAllergy()">Save</button>
                        </div>
                    </div>
                </div>
            </div>
        `);
    }

    async _saveAllergy() {
        const name = document.getElementById('addAllergyName')?.value?.trim();
        if (!name) { this._toast('Allergen name is required', 'warning'); return; }
        try {
            await this._post(`/api/patients/${this.patientId}/allergies`, {
                allergenName: name,
                notes: document.getElementById('addAllergyNotes')?.value?.trim() || null
            });
            this._toast('Allergy added');
            this.loadAllergies();
        } catch (e) { this._toast('Failed to add allergy', 'danger'); }
    }

    _inactivateAllergy(id) {
        const item = (this.data.allergies || []).find(a => a.PatientAllergyId === id);
        this._openHistoryActionModal({
            sectionApi: 'allergy', action: 'inactivate', actionUrl: 'inactivate',
            recordId: id, recordLabel: (item && item.AllergenName) || '',
            reload: () => this.loadAllergies()
        });
    }

    _reactivateAllergy(id) {
        const item = (this.data.allergies || []).find(a => a.PatientAllergyId === id);
        this._openHistoryActionModal({
            sectionApi: 'allergy', action: 'reactivate', actionUrl: 'reactivate',
            recordId: id, recordLabel: (item && item.AllergenName) || '',
            reload: () => this.loadAllergies()
        });
    }

    _deleteAllergy(id) {
        const item = (this.data.allergies || []).find(a => a.PatientAllergyId === id);
        this._openHistoryActionModal({
            sectionApi: 'allergy', action: 'delete', actionUrl: 'delete',
            recordId: id, recordLabel: (item && item.AllergenName) || '',
            reload: () => this.loadAllergies()
        });
    }

    _showAddMedication() {
        this._showInlineForm('clinicalMedicationsContent', `
            <div class="card pcm-inline-form mb-3">
                <div class="card-header d-flex justify-content-between align-items-center py-2">
                    <span class="text-primary"><i class="bi bi-plus-circle text-primary me-2"></i>Add Medication</span>
                    <button type="button" class="btn-close" aria-label="Close" onclick="window.patientClinicalManager._cancelInlineForm('clinicalMedicationsContent')"></button>
                </div>
                <div class="card-body">
                    <div class="row g-2">
                        <div class="col-md-7"><input type="text" class="form-control" id="addMedName" placeholder="Drug Name *" required></div>
                        <div class="col-md-3"><input type="text" class="form-control" id="addMedNotes" placeholder="Notes (optional)"></div>
                        <div class="col-md-2">
                            <button class="btn btn-primary w-100" onclick="window.patientClinicalManager._saveMedication()">Save</button>
                        </div>
                    </div>
                </div>
            </div>
        `);
    }

    async _saveMedication() {
        const name = document.getElementById('addMedName')?.value?.trim();
        if (!name) { this._toast('Drug name is required', 'warning'); return; }
        try {
            await this._post(`/api/patients/${this.patientId}/medications`, {
                drugName: name,
                notes: document.getElementById('addMedNotes')?.value?.trim() || null,
                startDate: new Date().toISOString().split('T')[0]
            });
            this._toast('Medication added');
            this.loadMedications();
        } catch (e) { this._toast('Failed to add medication', 'danger'); }
    }

    _discontinueMed(id) {
        const item = (this.data.medications || []).find(m => m.PatientMedicationId === id);
        this._openHistoryActionModal({
            sectionApi: 'medication', action: 'discontinue', actionUrl: 'discontinue',
            recordId: id, recordLabel: (item && item.DrugName) || '',
            reload: () => this.loadMedications()
        });
    }

    _revertMed(id) {
        const item = (this.data.medications || []).find(m => m.PatientMedicationId === id);
        this._openHistoryActionModal({
            sectionApi: 'medication', action: 'revertDiscontinue', actionUrl: 'revert-discontinue',
            recordId: id, recordLabel: (item && item.DrugName) || '',
            reload: () => this.loadMedications()
        });
    }

    _holdMed(id) {
        const item = (this.data.medications || []).find(m => m.PatientMedicationId === id);
        this._openHistoryActionModal({
            sectionApi: 'medication', action: 'markOnHold', actionUrl: 'mark-on-hold',
            recordId: id, recordLabel: (item && item.DrugName) || '',
            reload: () => this.loadMedications()
        });
    }

    _resumeMed(id) {
        const item = (this.data.medications || []).find(m => m.PatientMedicationId === id);
        this._openHistoryActionModal({
            sectionApi: 'medication', action: 'resumeFromHold', actionUrl: 'resume-from-hold',
            recordId: id, recordLabel: (item && item.DrugName) || '',
            reload: () => this.loadMedications()
        });
    }

    _completeMed(id) {
        const item = (this.data.medications || []).find(m => m.PatientMedicationId === id);
        this._openHistoryActionModal({
            sectionApi: 'medication', action: 'complete', actionUrl: 'complete',
            recordId: id, recordLabel: (item && item.DrugName) || '',
            reload: () => this.loadMedications()
        });
    }

    _deleteMed(id) {
        const item = (this.data.medications || []).find(m => m.PatientMedicationId === id);
        this._openHistoryActionModal({
            sectionApi: 'medication', action: 'delete', actionUrl: 'delete',
            recordId: id, recordLabel: (item && item.DrugName) || '',
            reload: () => this.loadMedications()
        });
    }

    _showAddVitals() {
        this._showInlineForm('clinicalVitalsContent', `
            <div class="card pcm-inline-form mb-3">
                <div class="card-header d-flex justify-content-between align-items-center py-2">
                    <span class="text-primary"><i class="bi bi-heart-pulse text-primary me-2"></i>Record Vitals</span>
                    <button type="button" class="btn-close" aria-label="Close" onclick="window.patientClinicalManager._cancelInlineForm('clinicalVitalsContent')"></button>
                </div>
                <div class="card-body">
                    <div class="row g-2">
                        <div class="col-md-2"><label class="form-label small mb-0">Systolic BP</label><input type="number" class="form-control" id="addVitalSys" placeholder="mmHg"></div>
                        <div class="col-md-2"><label class="form-label small mb-0">Diastolic BP</label><input type="number" class="form-control" id="addVitalDia" placeholder="mmHg"></div>
                        <div class="col-md-2"><label class="form-label small mb-0">Heart Rate</label><input type="number" class="form-control" id="addVitalHR" placeholder="bpm"></div>
                        <div class="col-md-2"><label class="form-label small mb-0">Temp (°F)</label><input type="number" step="0.1" class="form-control" id="addVitalTemp"></div>
                        <div class="col-md-2"><label class="form-label small mb-0">SpO2 (%)</label><input type="number" step="0.1" class="form-control" id="addVitalSpO2"></div>
                        <div class="col-md-2"><label class="form-label small mb-0">Resp Rate</label><input type="number" class="form-control" id="addVitalRR" placeholder="/min"></div>
                    </div>
                    <div class="row g-2 mt-2">
                        <div class="col-md-2"><label class="form-label small mb-0">Weight (lbs)</label><input type="number" step="0.1" class="form-control" id="addVitalWt"></div>
                        <div class="col-md-2"><label class="form-label small mb-0">Height (in)</label><input type="number" step="0.1" class="form-control" id="addVitalHt"></div>
                        <div class="col-md-2 ms-auto d-flex align-items-end">
                            <button class="btn btn-primary w-100" onclick="window.patientClinicalManager._saveVitals()">Save</button>
                        </div>
                    </div>
                </div>
            </div>
        `);
    }

    async _saveVitals() {
        const intVal = (id) => { const v = document.getElementById(id)?.value; return v ? parseInt(v) : null; };
        const decVal = (id) => { const v = document.getElementById(id)?.value; return v ? parseFloat(v) : null; };
        try {
            await this._post(`/api/patients/${this.patientId}/vitals`, {
                systolicBp: intVal('addVitalSys'), diastolicBp: intVal('addVitalDia'),
                heartRate: intVal('addVitalHR'), respiratoryRate: intVal('addVitalRR'),
                temperature: decVal('addVitalTemp'), spO2: decVal('addVitalSpO2'),
                weight: decVal('addVitalWt'), height: decVal('addVitalHt')
            });
            this._toast('Vitals recorded');
            this.loadVitals();
        } catch (e) { this._toast('Failed to save vitals', 'danger'); }
    }

    _showAddImmunization() {
        this._showInlineForm('clinicalImmunizationsContent', `
            <div class="card pcm-inline-form mb-3">
                <div class="card-header d-flex justify-content-between align-items-center py-2">
                    <span class="text-primary"><i class="bi bi-plus-circle text-primary me-2"></i>Add Immunization</span>
                    <button type="button" class="btn-close" aria-label="Close" onclick="window.patientClinicalManager._cancelInlineForm('clinicalImmunizationsContent')"></button>
                </div>
                <div class="card-body">
                    <div class="row g-2">
                        <div class="col-md-7"><input type="text" class="form-control" id="addImmVaccine" placeholder="Vaccine Name *" required></div>
                        <div class="col-md-3"><input type="text" class="form-control" id="addImmNotes" placeholder="Notes (optional)"></div>
                        <div class="col-md-2"><button class="btn btn-primary w-100" onclick="window.patientClinicalManager._saveImmunization()">Save</button></div>
                    </div>
                </div>
            </div>
        `);
    }

    async _saveImmunization() {
        const name = document.getElementById('addImmVaccine')?.value?.trim();
        if (!name) { this._toast('Vaccine name is required', 'warning'); return; }
        try {
            await this._post(`/api/patients/${this.patientId}/immunizations`, {
                vaccineName: name,
                administeredDate: new Date().toISOString().split('T')[0],
                notes: document.getElementById('addImmNotes')?.value?.trim() || null
            });
            this._toast('Immunization added');
            this.loadImmunizations();
        } catch (e) { this._toast('Failed to add immunization', 'danger'); }
    }

    _deleteImmunization(id) {
        const item = (this.data.immunizations || []).find(i => i.PatientImmunizationId === id);
        this._openHistoryActionModal({
            sectionApi: 'immunization', action: 'delete', actionUrl: 'delete',
            recordId: id, recordLabel: (item && item.VaccineName) || '',
            reload: () => this.loadImmunizations()
        });
    }

    _showAddFamilyHistory() {
        this._showInlineForm('clinicalHistoryContent', `
            <div class="card pcm-inline-form mb-3">
                <div class="card-header d-flex justify-content-between align-items-center py-2">
                    <span class="text-primary"><i class="bi bi-plus-circle text-primary me-2"></i>Add Family History</span>
                    <button type="button" class="btn-close" aria-label="Close" onclick="window.patientClinicalManager._cancelInlineForm('clinicalHistoryContent')"></button>
                </div>
                <div class="card-body">
                    <div class="row g-2">
                        <div class="col-md-7"><input type="text" class="form-control" id="addFamCondition" placeholder="Condition *" required></div>
                        <div class="col-md-3"><input type="text" class="form-control" id="addFamNotes" placeholder="Notes (optional)"></div>
                        <div class="col-md-2"><button class="btn btn-primary w-100" onclick="window.patientClinicalManager._saveFamilyHistory()">Save</button></div>
                    </div>
                </div>
            </div>
        `, true);
    }

    async _saveFamilyHistory() {
        const condition = document.getElementById('addFamCondition')?.value?.trim();
        if (!condition) { this._toast('Condition is required', 'warning'); return; }
        try {
            await this._post(`/api/patients/${this.patientId}/family-history`, {
                condition: condition,
                notes: document.getElementById('addFamNotes')?.value?.trim() || null
            });
            this._toast('Family history added');
            this.loadHistory();
        } catch (e) { this._toast('Failed', 'danger'); }
    }

    _deleteFamilyHistory(id) {
        const item = (this.data.history || this.data.familyHistory || []).find(f => f.PatientFamilyHistoryId === id);
        this._openHistoryActionModal({
            sectionApi: 'family-history', action: 'delete', actionUrl: 'delete',
            recordId: id, recordLabel: (item && item.Condition) || '',
            reload: () => this.loadHistory()
        });
    }

    _showAddSocialHistory() {
        this._showInlineForm('clinicalHistoryContent', `
            <div class="card pcm-inline-form mb-3">
                <div class="card-header d-flex justify-content-between align-items-center py-2">
                    <span class="text-primary"><i class="bi bi-plus-circle text-primary me-2"></i>Add Social History</span>
                    <button type="button" class="btn-close" aria-label="Close" onclick="window.patientClinicalManager._cancelInlineForm('clinicalHistoryContent')"></button>
                </div>
                <div class="card-body">
                    <div class="row g-2">
                        <div class="col-md-3"><input type="text" class="form-control" id="addSocCategory" placeholder="Category *"></div>
                        <div class="col-md-7"><input type="text" class="form-control" id="addSocNotes" placeholder="Notes (optional)"></div>
                        <div class="col-md-2"><button class="btn btn-primary w-100" onclick="window.patientClinicalManager._saveSocialHistory()">Save</button></div>
                    </div>
                    <div class="mt-2"><small class="text-muted">Ask about: Tobacco · Alcohol · Drug Use · Exercise · Diet · Occupation · Sexual Activity</small></div>
                </div>
            </div>
        `, true);
    }

    async _saveSocialHistory() {
        const category = document.getElementById('addSocCategory')?.value?.trim();
        if (!category) { this._toast('Category is required', 'warning'); return; }
        try {
            await this._post(`/api/patients/${this.patientId}/social-history`, {
                category: category,
                notes: document.getElementById('addSocNotes')?.value?.trim() || null
            });
            this._toast('Social history added');
            this.loadHistory();
        } catch (e) { this._toast('Failed', 'danger'); }
    }

    _deleteSocialHistory(id) {
        const item = (this.data.socialHistory || this.data.history || []).find(s => s.PatientSocialHistoryId === id);
        this._openHistoryActionModal({
            sectionApi: 'social-history', action: 'delete', actionUrl: 'delete',
            recordId: id, recordLabel: (item && item.Category) || '',
            reload: () => this.loadHistory()
        });
    }

    // ─── ENCOUNTERS ──────────────────────────────────────────

    async loadEncounters() {
        const pid = this.patientId;
        if (!pid) return;
        const container = document.getElementById('patientEncountersContent');
        if (!container) return;

        try {
            this.data.encounters = await this._fetch(`/api/patients/${pid}/encounters`);
            this._renderEncounters(container);
        } catch (e) {
            container.innerHTML = '<div class="text-center text-danger p-3">Failed to load encounters</div>';
        }
    }

    _renderEncounters(container) {
        const encounters = this.data.encounters;
        if (!encounters.length) {
            container.innerHTML = '<div class="text-center text-muted p-4"><i class="bi bi-journal-medical fs-1 d-block mb-2"></i>No encounters recorded</div>';
            return;
        }

        const statusBadge = (status) => {
            const map = { 0: ['Open', 'primary'], 1: ['Signed', 'success'], 2: ['Locked', 'secondary'], 3: ['Amended', 'warning'] };
            const [text, cls] = map[status] || ['Unknown', 'secondary'];
            return `<span class="badge bg-${cls}">${text}</span>`;
        };

        const noteBadge = (noteStatus) => {
            const map = { 'Signed': 'success', 'Draft': 'warning', 'No Note': 'secondary' };
            return `<span class="badge bg-${map[noteStatus] || 'secondary'}">${this._esc(noteStatus)}</span>`;
        };

        const formatDate = (d) => {
            if (!d) return '-';
            try { return new Date(d + 'T00:00:00').toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }); }
            catch { return d; }
        };

        const parseCpt = (raw) => {
            if (!raw) return [];
            try { return JSON.parse(raw) || []; } catch { return []; }
        };

        const cards = encounters.map((enc, idx) => {
            const isFirst = idx === 0;
            const cpts = parseCpt(enc.CptSelections);

            let detailSections = '';

            if (enc.ChiefComplaint) {
                detailSections += `
                    <div class="mb-2">
                        <span class="text-muted small fw-semibold">Chief Complaint</span>
                        <div class="mt-1">${this._esc(enc.ChiefComplaint)}</div>
                    </div>`;
            }

            if (cpts.length > 0) {
                const cptRows = cpts.map(c =>
                    `<div class="small"><span class="badge bg-light text-dark border me-1">${this._esc(c.cptCode)}</span>${this._esc(c.description || '')}</div>`
                ).join('');
                detailSections += `
                    <div class="mb-2">
                        <span class="text-muted small fw-semibold">Procedures</span>
                        <div class="mt-1 d-flex flex-column gap-1">${cptRows}</div>
                    </div>`;
            }

            if (enc.VitalsCount > 0) {
                detailSections += `
                    <div class="mb-2">
                        <span class="text-muted small fw-semibold">Vitals</span>
                        <div class="mt-1"><span class="badge bg-info text-dark"><i class="bi bi-activity me-1"></i>${enc.VitalsCount} vitals recorded</span></div>
                    </div>`;
            }

            // Visit Summary — patient-friendly AI-generated summary, decrypted server-side.
            // Only render when SummaryText is present (encounter closed and Gemini call done).
            // Spec: rules/technical/encounter-summary.md
            if (enc.SummaryText && enc.SummaryText.trim().length > 0) {
                detailSections += `
                    <div class="encounter-summary-block mb-2">
                        <div class="text-muted small fw-semibold mb-1">
                            <i class="bi bi-file-text me-1"></i>Visit Summary
                        </div>
                        <div class="encounter-summary-text">${this._esc(enc.SummaryText)}</div>
                    </div>`;
            }

            const hasNote = enc.ClinicalNoteStatus !== 'No Note';
            const isOpen = enc.Status === 0;
            const viewNoteBtn = hasNote
                ? `<button class="btn btn-outline-secondary" onclick="window.patientClinicalManager._viewEncounterNote(${enc.AppointmentId})">
                       <i class="bi bi-file-text me-1"></i>View Note
                   </button>`
                : '';
            const openEncBtn = isOpen
                ? `<a href="/Encounters/Index?encounterId=${enc.EncounterId}" class="btn btn-outline-primary" target="_blank">
                       <i class="bi bi-box-arrow-up-right me-1"></i>Open Encounter
                   </a>`
                : '';
            detailSections += `
                <div class="d-flex align-items-center gap-2 pt-1 border-top mt-2">
                    <span class="text-muted small fw-semibold">Note:</span>
                    ${noteBadge(enc.ClinicalNoteStatus)}
                    <div class="ms-auto d-flex gap-2">
                        ${viewNoteBtn}
                        ${openEncBtn}
                    </div>
                </div>`;

            return `
                <div class="accordion-item">
                    <h2 class="accordion-header">
                        <button class="accordion-button ${isFirst ? '' : 'collapsed'} py-2" type="button"
                                data-bs-toggle="collapse" data-bs-target="#enc${enc.EncounterId}">
                            <div class="d-flex align-items-center w-100 me-2">
                                <span class="fw-bold me-2">${formatDate(enc.EncounterDate)}</span>
                                ${statusBadge(enc.Status)}
                                <span class="badge bg-light text-dark border ms-2">${this._esc((enc.AppointmentType || 'Visit').replace(/([A-Z])/g, ' $1').trim())}</span>
                                <span class="text-muted ms-2 small">${this._esc(enc.ProviderName || '')}</span>
                                <span class="ms-auto me-2">${noteBadge(enc.ClinicalNoteStatus)}</span>
                            </div>
                        </button>
                    </h2>
                    <div id="enc${enc.EncounterId}" class="accordion-collapse collapse ${isFirst ? 'show' : ''}"
                         data-bs-parent="#encountersAccordion">
                        <div class="accordion-body py-2">
                            ${detailSections}
                        </div>
                    </div>
                </div>`;
        }).join('');

        container.innerHTML = `
            <div class="d-flex justify-content-between align-items-center mb-2">
                <h6 class="mb-0"><i class="bi bi-journal-medical me-1"></i>Visit History (${encounters.length})</h6>
            </div>
            <div class="accordion" id="encountersAccordion">
                ${cards}
            </div>`;
    }

    async _viewEncounterNote(appointmentId) {
        if (!appointmentId) return;
        try {
            const notes = await this._fetch(`/api/clinical-notes/by-appointment/${appointmentId}`);
            if (!notes || !notes.length) { this._toast('No note found for this encounter', 'warning'); return; }
            const noteId = notes[0].ClinicalNoteId;
            // Route through viewClinicalNote so we get the tabbed view modal with
            // version history, amendment / addendum buttons, and proper signed/draft
            // rendering. Earlier this called editClinicalNote which jumped straight
            // into the legacy edit modal and bypassed all of that.
            if (typeof window.viewClinicalNote === 'function') {
                await window.viewClinicalNote(noteId);
            } else if (typeof editClinicalNote === 'function') {
                editClinicalNote(noteId);   // fallback, shouldn't be reached
            }
        } catch (e) { this._toast('Failed to load note', 'danger'); }
    }

    // ─── HELPERS ──────────────────────────────────────────────

    _showInlineForm(containerId, formHtml, prepend = false) {
        const container = document.getElementById(containerId);
        if (!container) return;
        // Remove existing inline form
        const existing = container.querySelector('.card.pcm-inline-form');
        if (existing) existing.remove();
        if (prepend) {
            container.insertAdjacentHTML('afterbegin', formHtml);
        } else {
            const header = container.querySelector('.d-flex.justify-content-between');
            if (header) {
                header.insertAdjacentHTML('afterend', formHtml);
            } else {
                container.insertAdjacentHTML('afterbegin', formHtml);
            }
        }
    }

    _cancelInlineForm(containerId) {
        const container = document.getElementById(containerId);
        if (!container) return;
        const existing = container.querySelector('.card.pcm-inline-form');
        if (existing) existing.remove();
    }

    // ---- Prescriptions Tab ----

    async loadPrescriptions(patientId) {
        if (patientId) this.patientId = patientId;
        const pid = this.patientId;
        if (!pid) return;
        const container = document.getElementById('patientPrescriptionsContent');
        if (!container) return;

        try {
            const prescriptions = await this._fetch(`/api/patients/${pid}/prescriptions`);
            this._renderPrescriptions(container, prescriptions);
        } catch (e) {
            container.innerHTML = '<div class="text-center text-danger p-3">Failed to load prescriptions</div>';
        }
    }

    _renderPrescriptions(container, prescriptions) {
        if (!prescriptions || !prescriptions.length) {
            container.innerHTML = '<div class="text-center text-muted p-4"><i class="bi bi-capsule fs-1 d-block mb-2"></i>No prescriptions recorded</div>';
            return;
        }

        // Status grouping: 0=Draft, 1=Active, 2=Sent, 3=Filled, 4=Cancelled, 5=Expired
        const activeStatuses = [0, 1, 2, 3];
        const activeRx = prescriptions.filter(rx => activeStatuses.includes(rx.Status));
        const archivedRx = prescriptions.filter(rx => !activeStatuses.includes(rx.Status));

        const renderCard = (rx) => {
            const statusBadge = window.PrescriptionStatuses
                ? window.PrescriptionStatuses.getBadgeHtml(rx.Status)
                : `<span class="badge bg-secondary">${rx.StatusName || 'Unknown'}</span>`;

            let cardCls = 'rx-card';
            if (rx.Status === 0) cardCls += ' draft';
            else if (rx.Status === 1 || rx.Status === 2 || rx.Status === 3) cardCls += ' active-link';
            else cardCls += ' discontinued';

            return `
                <div class="${cardCls}">
                    <div class="rx-card-body" style="grid-column: 1 / -1;">
                        <div class="rx-card-head">
                            <div class="drug-icon"><i class="bi bi-prescription2"></i></div>
                            <div class="drug-name">
                                ${this._esc(rx.DrugName)}
                                ${rx.Strength ? `<small>${this._esc(rx.Strength)}</small>` : ''}
                            </div>
                            ${statusBadge}
                        </div>
                        ${rx.DirectionsFreeText ? `<div class="rx-card-notes"><strong>SIG:</strong> ${this._esc(rx.DirectionsFreeText)}</div>` : ''}
                        <div class="rx-card-meta">
                            ${rx.PrescribedDate ? `<span><i class="bi bi-calendar3"></i><strong>Prescribed:</strong> ${rx.PrescribedDate}</span>` : ''}
                            ${rx.Quantity != null ? `<span><i class="bi bi-box"></i><strong>Qty:</strong> ${rx.Quantity}</span>` : ''}
                            ${rx.Refills != null ? `<span><i class="bi bi-arrow-repeat"></i><strong>Refills:</strong> ${rx.Refills}</span>` : ''}
                            ${rx.PharmacyName ? `<span><i class="bi bi-shop"></i><strong>Pharmacy:</strong> ${this._esc(rx.PharmacyName)}</span>` : ''}
                            ${rx.GenericName ? `<span><i class="bi bi-tag"></i><strong>Generic:</strong> ${this._esc(rx.GenericName)}</span>` : ''}
                        </div>
                    </div>
                </div>
            `;
        };

        container.innerHTML = `
            ${activeRx.length > 0 ? `
                <div class="rx-group-label">Active (${activeRx.length})</div>
                <div class="rx-card-list">${activeRx.map(renderCard).join('')}</div>
            ` : ''}
            ${archivedRx.length > 0 ? `
                <div class="rx-group-label rx-group-label-muted">Cancelled / Expired (${archivedRx.length})</div>
                <div class="rx-card-list">${archivedRx.map(renderCard).join('')}</div>
            ` : ''}
        `;
    }

    // ---- Orders Tab ----

    async loadOrders(patientId) {
        if (patientId) this.patientId = patientId;
        const pid = this.patientId;
        if (!pid) return;
        const container = document.getElementById('patientOrdersContent');
        if (!container) return;

        try {
            const orders = await this._fetch(`/api/patients/${pid}/orders`);
            this._renderOrders(container, orders);
        } catch (e) {
            container.innerHTML = '<div class="text-center text-danger p-3">Failed to load orders</div>';
        }
    }

    _renderOrders(container, orders) {
        if (!orders || !orders.length) {
            container.innerHTML = '<div class="text-center text-muted p-4"><i class="bi bi-clipboard2-pulse fs-1 d-block mb-2"></i>No orders recorded</div>';
            return;
        }

        const OC = window.OrderConstants || {};
        const rows = orders.map(o => {
            const typeBadge = OC.TypeBadge ? OC.TypeBadge[o.OrderType] || '' : o.OrderTypeName || '';
            const statusBadge = OC.StatusBadge ? OC.StatusBadge[o.Status] || '' : o.StatusName || '';
            const priorityBadge = OC.PriorityBadge ? OC.PriorityBadge[o.Priority] || '' : o.PriorityName || '';
            const desc = OC.getOrderDescription ? OC.getOrderDescription(o) : (o.LabPanelName || o.ReferralSpecialty || o.BodyPart || 'Order');
            const hasResults = o.Results && o.Results.length > 0;
            const resultsIcon = hasResults ? ' <i class="bi bi-clipboard-check text-success" title="Has results"></i>' : '';

            return `<tr>
                <td>${o.OrderDate || ''}</td>
                <td>${typeBadge}</td>
                <td>${this._esc(desc)}${resultsIcon}</td>
                <td>${priorityBadge}</td>
                <td>${this._esc(o.ProviderName)}</td>
                <td>${statusBadge}</td>
                <td>
                    <button class="btn btn-sm btn-outline-info" onclick="window._ordersModule?._viewOrder(${o.OrderId})" title="View"><i class="bi bi-eye"></i></button>
                </td>
            </tr>`;
        }).join('');

        container.innerHTML = `
            <div class="table-responsive">
                <table class="table table-sm table-hover mb-0">
                    <thead>
                        <tr>
                            <th>Date</th>
                            <th>Type</th>
                            <th>Description</th>
                            <th>Priority</th>
                            <th>Provider</th>
                            <th>Status</th>
                            <th>Actions</th>
                        </tr>
                    </thead>
                    <tbody>${rows}</tbody>
                </table>
            </div>`;
    }

    _esc(str) {
        if (!str) return '';
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }

    _toast(message, type = 'success') {
        if (window.Toast) {
            window.Toast.show(message, type);
        }
    }
}

window.patientClinicalManager = new PatientClinicalManager();
