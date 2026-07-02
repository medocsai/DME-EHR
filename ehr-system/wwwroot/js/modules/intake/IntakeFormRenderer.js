/**
 * IntakeFormRenderer (Contractor — shared by Portal and Tablet)
 *
 * Why: render the intake wizard shell. Sidebar, step switching, progress banner,
 *      prev/next, save-and-exit, final submit.
 * What: takes a host config (savePaths, submitPath, progressPath, onExit, onComplete)
 *       and an initial progress DTO from the server. Delegates body rendering to
 *       a section renderer per step.
 * Who calls: PortalIntakeWizardModule (this phase), TabletIntakeWizardModule (later).
 * Returns: a wizard object with .mount(container), .goToStep(idx), .destroy().
 *
 * Host config shape:
 *   {
 *     savePath: (sectionName) => '/api/intake/section/' + sectionName,
 *     submitPath: '/api/intake/submit',
 *     progressPath: '/api/intake/progress',
 *     viewPath: '/api/...intake-view',   // not used v1
 *     authHeaders: () => ({ 'Authorization': 'Bearer ...' }),
 *     onExit: () => { window.location = '/Portal/Dashboard'; },
 *     onComplete: () => { ... show thank-you ... }
 *   }
 */
(function () {
    'use strict';

    // The API serializes with PropertyNamingPolicy=null (PascalCase). Normalize
    // here so the rest of the renderer can read consistent camelCase keys.
    function normalizeProgress(raw) {
        if (!raw) return null;
        const sections = (raw.Sections ?? raw.sections ?? []).map(s => ({
            name: s.Name ?? s.name,
            filled: s.Filled ?? s.filled,
            timestamp: s.Timestamp ?? s.timestamp
        }));
        return {
            completed: raw.Completed ?? raw.completed ?? 0,
            total: raw.Total ?? raw.total ?? 0,
            currentSubmissionId: raw.CurrentSubmissionId ?? raw.currentSubmissionId ?? null,
            submittedAt: raw.SubmittedAt ?? raw.submittedAt ?? null,
            sections
        };
    }

    // Step definitions. `sectionName` is what server expects in /section/{name}.
    // `multi` steps save multiple section names on one "Save & Continue".
    const STEPS = [
        { key: 'demographics',    title: 'Demographics',                sectionName: 'demographics' },
        { key: 'concerns',        title: 'Health Concerns',             sectionName: 'concerns' },
        { key: 'medical-history', title: 'Medical History',             sectionName: 'medical-history',
          multi: ['medical-history', 'allergies', 'immunizations'] },
        { key: 'lifestyle',       title: 'Lifestyle',                   sectionName: 'lifestyle' },
        { key: 'medications',     title: 'Medications & Supplements',   sectionName: 'medications',
          multi: ['medications', 'supplements'] },
        { key: 'gender-health',   title: 'Gender Health',               sectionName: 'gender-health' },
        { key: 'longevity',       title: 'Longevity',                   sectionName: 'longevity', flagged: 'longevity' },
        { key: 'review',          title: 'Review & Finish',             sectionName: null }
    ];

    class IntakeFormRenderer {
        constructor(config) {
            this.config = config || {};
            this.container = null;
            this.progress = null;
            this.currentStep = 0;
            this.renderers = {};  // key -> section renderer instance
            this.steps = [];
        }

        async mount(container) {
            this.container = container;
            await this.refreshProgress();
            this._computeActiveSteps();
            this._renderShell();
            this._renderStep(this.currentStep);
        }

        async refreshProgress() {
            try {
                const res = await fetch(this.config.progressPath, {
                    headers: this.config.authHeaders ? this.config.authHeaders() : {}
                });
                if (res.ok) {
                    const raw = await res.json();
                    this.progress = normalizeProgress(raw);
                }
            } catch (e) {
                console.error('refreshProgress failed', e);
            }
        }

        _computeActiveSteps() {
            const hasLongevity = !!(this.progress && (this.progress.sections || []).find(s => s.name === 'longevity'));
            this.steps = STEPS.filter(s => s.flagged !== 'longevity' || hasLongevity);
        }

        _sectionStatus(key) {
            if (!this.progress || !this.progress.sections) return null;
            return this.progress.sections.find(s => s.name === key) || null;
        }

        _renderShell() {
            const total = this.steps.length;
            const completed = this.progress ? (this.progress.completed || 0) : 0;

            this.container.innerHTML = `
                <div class="intake-wizard-banner d-flex justify-content-between align-items-center p-3 mb-3 rounded"
                     style="background:#F0F7FF;border:1px solid #BFDBFE;">
                    <div><strong>Intake Form</strong> <span class="text-muted small">(saved automatically as you move forward)</span></div>
                    <div class="d-flex align-items-center gap-3">
                        <div><strong id="iwBannerCompleted">${completed}</strong> of <strong id="iwBannerTotal">${total}</strong> sections</div>
                        <button type="button" class="btn btn-outline-secondary" id="iwSaveExit">
                            <i class="bi bi-box-arrow-right me-1"></i> Save &amp; Exit
                        </button>
                    </div>
                </div>
                <div class="intake-wizard-shell row g-3">
                    <div class="col-md-3">
                        <div class="card portal-card">
                            <div class="card-body p-2" id="iwSidebar"></div>
                        </div>
                    </div>
                    <div class="col-md-9">
                        <div class="card portal-card">
                            <div class="card-body" id="iwMain"></div>
                        </div>
                    </div>
                </div>
                <style>
                    .iw-step { display:flex; align-items:center; gap:10px; padding:10px 12px; border-radius:8px; cursor:pointer; color:#334155; }
                    .iw-step:hover { background:#F1F5F9; }
                    .iw-step.active { background:#DBEAFE; color:#1B72BE; font-weight:600; }
                    .iw-step.done { color:#16A34A; }
                    .iw-step .iw-dot { width:26px; height:26px; border-radius:50%; background:#E2E8F0; color:#475569; display:flex; align-items:center; justify-content:center; font-size:13px; font-weight:600; flex-shrink:0; }
                    .iw-step.active .iw-dot { background:#1B72BE; color:#fff; }
                    .iw-step.done .iw-dot { background:#16A34A; color:#fff; }
                    .iw-actions { display:flex; justify-content:space-between; margin-top:24px; padding-top:16px; border-top:1px solid #E2E8F0; }
                    .iw-rating-row { margin:14px 0; }
                    .iw-rating-cards { display:flex; gap:4px; flex-wrap:wrap; margin-top:6px; }
                    .iw-rating-card { min-width:42px; height:42px; border:1px solid #CBD5E1; background:#fff; border-radius:8px; font-weight:600; color:#334155; cursor:pointer; padding:0 10px; }
                    .iw-rating-card.active { background:#1B72BE; color:#fff; border-color:#1B72BE; }
                    .iw-rating-card.zero { color:#16A34A; }
                    .iw-rating-card.ten { color:#DC2626; }
                    .iw-checkbox-grid { display:grid; grid-template-columns:repeat(auto-fit, minmax(220px, 1fr)); gap:8px; }
                    .iw-checkbox-grid label { display:flex; align-items:center; gap:8px; padding:10px 12px; border:1px solid #E2E8F0; border-radius:8px; cursor:pointer; background:#fff; margin:0; }
                    .iw-checkbox-grid label:hover { background:#F8FAFC; }
                    .iw-checkbox-grid label input:checked + span { color:#1B72BE; font-weight:600; }
                    .iw-item-card { border:1px solid #E2E8F0; border-radius:8px; padding:12px; margin-bottom:10px; background:#F8FAFC; }
                </style>
            `;

            this._renderSidebar();
            document.getElementById('iwSaveExit').addEventListener('click', () => this._saveAndExit());
        }

        _renderSidebar() {
            const sb = document.getElementById('iwSidebar');
            if (!sb) return;
            sb.innerHTML = this.steps.map((step, idx) => {
                const status = this._sectionStatus(step.key);
                const done = status && status.filled;
                const active = idx === this.currentStep;
                const cls = ['iw-step'];
                if (active) cls.push('active');
                else if (done) cls.push('done');
                const dot = done ? '<i class="bi bi-check-lg"></i>' : (idx + 1);
                return `<div class="${cls.join(' ')}" data-step="${idx}">
                    <div class="iw-dot">${dot}</div>
                    <div class="iw-label">${step.title}</div>
                </div>`;
            }).join('');
            sb.querySelectorAll('.iw-step').forEach(el => {
                el.addEventListener('click', () => {
                    const idx = parseInt(el.getAttribute('data-step'), 10);
                    this._attemptNav(idx);
                });
            });
        }

        _updateBanner() {
            const completed = this.progress ? (this.progress.completed || 0) : 0;
            const total = this.steps.length;
            const c = document.getElementById('iwBannerCompleted');
            const t = document.getElementById('iwBannerTotal');
            if (c) c.textContent = completed;
            if (t) t.textContent = total;
        }

        _renderStep(idx) {
            this.currentStep = idx;
            const step = this.steps[idx];
            const main = document.getElementById('iwMain');
            if (!main) return;

            // Review step: render inline
            if (step.key === 'review') {
                this._renderReview(main);
                this._renderSidebar();
                return;
            }

            // Build outer: title + body container + actions
            main.innerHTML = `
                <h4 class="mb-1">${step.title}</h4>
                <div class="text-muted small mb-3" id="iwStepHint"></div>
                <div id="iwStepBody"></div>
                <div class="iw-actions">
                    <button type="button" class="btn btn-outline-secondary" id="iwPrevBtn" ${idx === 0 ? 'style="visibility:hidden"' : ''}>
                        <i class="bi bi-arrow-left me-1"></i> Back
                    </button>
                    <button type="button" class="btn btn-primary" id="iwNextBtn">
                        Save &amp; Continue <i class="bi bi-arrow-right ms-1"></i>
                    </button>
                </div>
            `;

            // Instantiate (or reuse) section renderer.
            const renderer = this._getRenderer(step.key);
            const body = document.getElementById('iwStepBody');
            const data = this._dataForStep(step.key);
            if (renderer && typeof renderer.render === 'function') {
                renderer.render(body, data, 'patient-edit');
                const hintEl = document.getElementById('iwStepHint');
                if (hintEl && renderer.hint) hintEl.textContent = renderer.hint;
            } else {
                body.innerHTML = '<div class="alert alert-warning">Section not available.</div>';
            }

            document.getElementById('iwPrevBtn').addEventListener('click', () => this._goPrev());
            document.getElementById('iwNextBtn').addEventListener('click', () => this._goNext());

            this._renderSidebar();
            this._updateBanner();
        }

        _getRenderer(key) {
            if (this.renderers[key]) return this.renderers[key];
            const map = {
                'demographics':    window.IntakeDemographicsRenderer,
                'concerns':        window.IntakeHealthConcernsRenderer,
                'medical-history': window.IntakeMedicalHistoryRenderer,
                'lifestyle':       window.IntakeLifestyleRenderer,
                'medications':     window.IntakeMedicationsRenderer,
                'gender-health':   window.IntakeGenderHealthRenderer,
                'longevity':       window.IntakeLongevityRenderer
            };
            const Cls = map[key];
            if (!Cls) return null;
            const inst = new Cls();

            // Inject host config so the section renderer is portable between
            // portal (JWT) and tablet (verify cookie). The renderer doesn't need
            // to know which context it's in — it just calls the URL it was given
            // with the auth headers it was given.
            inst._prefillUrl = (this.config.prefillPath ? this.config.prefillPath(key) : null);
            inst._authHeaders = (typeof this.config.authHeaders === 'function')
                ? this.config.authHeaders
                : () => ({});

            this.renderers[key] = inst;
            return inst;
        }

        _dataForStep(key) {
            // v1: server doesn't expose full per-section data in progress DTO.
            // Renderers start from empty form; save writes to DB; re-visit shows status but not raw values (acceptable for Path A).
            return this.progress || {};
        }

        async _goPrev() {
            if (this.currentStep > 0) {
                // Save current first (best-effort) then move.
                await this._saveCurrent(/*silent*/ true);
                this._renderStep(this.currentStep - 1);
            }
        }

        async _goNext() {
            const ok = await this._saveCurrent(false);
            if (!ok) return;
            await this.refreshProgress();
            if (this.currentStep + 1 >= this.steps.length) {
                // Already at end
                return;
            }
            this._renderStep(this.currentStep + 1);
        }

        async _saveAndExit() {
            await this._saveCurrent(true);
            if (typeof this.config.onExit === 'function') this.config.onExit();
        }

        async _attemptNav(idx) {
            if (idx === this.currentStep) return;
            await this._saveCurrent(true);
            await this.refreshProgress();
            this._renderStep(idx);
        }

        async _saveCurrent(silent) {
            const step = this.steps[this.currentStep];
            if (!step || step.key === 'review') return true;

            const renderer = this._getRenderer(step.key);
            if (!renderer || typeof renderer.getPayload !== 'function') return true;

            try {
                const sectionNames = step.multi || [step.sectionName];
                for (const sectionName of sectionNames) {
                    const payload = renderer.getPayload(sectionName);
                    // If renderer returns null for a section, skip it.
                    if (payload == null) continue;
                    const res = await fetch(this.config.savePath(sectionName), {
                        method: 'POST',
                        headers: Object.assign(
                            { 'Content-Type': 'application/json' },
                            this.config.authHeaders ? this.config.authHeaders() : {}
                        ),
                        body: JSON.stringify(payload)
                    });
                    if (!res.ok) {
                        if (!silent) alert('Save failed. Please check your entries and try again.');
                        return false;
                    }
                    const result = await res.json();
                    if (result && result.progress) this.progress = result.progress;
                }
                return true;
            } catch (e) {
                console.error('save failed', e);
                if (!silent) alert('Could not save. Check your connection.');
                return false;
            }
        }

        _renderReview(main) {
            const rows = this.steps
                .filter(s => s.key !== 'review')
                .map(s => {
                    const st = this._sectionStatus(s.key);
                    const done = st && st.filled;
                    return `<div class="d-flex justify-content-between align-items-center p-3 border-bottom">
                        <div>
                            <i class="bi ${done ? 'bi-check-circle-fill text-success' : 'bi-circle text-muted'} me-2"></i>
                            <strong>${s.title}</strong>
                        </div>
                        <div>
                            <span class="badge ${done ? 'bg-success-subtle text-success' : 'bg-warning-subtle text-warning'}">
                                ${done ? 'Complete' : 'Not yet'}
                            </span>
                            <button class="btn btn-outline-primary ms-2" data-jump="${s.key}">
                                <i class="bi bi-pencil"></i> Edit
                            </button>
                        </div>
                    </div>`;
                }).join('');

            main.innerHTML = `
                <h4 class="mb-3">Review &amp; Finish</h4>
                <p class="text-muted">Review what you have entered. You can jump back to any section to update it. When you are ready, submit.</p>
                <div class="card border-0">${rows}</div>
                <div class="iw-actions">
                    <button type="button" class="btn btn-outline-secondary" id="iwPrevBtn"><i class="bi bi-arrow-left me-1"></i> Back</button>
                    <button type="button" class="btn btn-success" id="iwSubmitBtn"><i class="bi bi-check-lg me-1"></i> Submit Intake</button>
                </div>
            `;

            main.querySelectorAll('[data-jump]').forEach(btn => {
                btn.addEventListener('click', () => {
                    const key = btn.getAttribute('data-jump');
                    const idx = this.steps.findIndex(s => s.key === key);
                    if (idx >= 0) this._renderStep(idx);
                });
            });
            document.getElementById('iwPrevBtn').addEventListener('click', () => this._goPrev());
            document.getElementById('iwSubmitBtn').addEventListener('click', () => this._finalize());
        }

        async _finalize() {
            try {
                const res = await fetch(this.config.submitPath, {
                    method: 'POST',
                    headers: Object.assign(
                        { 'Content-Type': 'application/json' },
                        this.config.authHeaders ? this.config.authHeaders() : {}
                    )
                });
                if (!res.ok) {
                    alert('Submit failed. Please try again.');
                    return;
                }
                const result = await res.json();
                if (result && result.progress) this.progress = result.progress;
                if (typeof this.config.onComplete === 'function') this.config.onComplete(result);
            } catch (e) {
                console.error(e);
                alert('Submit failed. Check your connection.');
            }
        }
    }

    window.IntakeFormRenderer = IntakeFormRenderer;
})();
