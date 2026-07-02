/* ============================================================================
 * DxCptCodesComponent — ICD-10 + CPT selection component
 *
 * OOP analogy: CodeManager — the one person in the company who owns everything
 * about ICD-10 / CPT selection. Code search (ICD-10 + CPT databases), AI
 * suggestions (Gemini), provider favorites (per-user starred codes), ICD-10
 * family selection (siblings within a parent code, e.g. J18.x), manual entry,
 * and the selected-codes cart.
 *
 * CodeManager is hired by:
 *   1. EncounterWorkspaceModule — the "Dx & CPT Codes" step in the encounter
 *      workflow. Auto-saves selections to Encounter.CptSelections /
 *      IcdSelections as the provider picks, so encounter close has fresh codes.
 *
 *   2. AmendmentAddendumModule — the amendment modal, when an amendment changes
 *      the note and the provider wants to review codes. Does NOT auto-save;
 *      selections are handed back to the modal, which commits them atomically
 *      with the note amendment (single DB transaction on the server).
 *
 * Before this extraction (2026-04-20):
 *   All of this lived inside EncounterWorkspaceModule.js, roughly lines
 *   3149–4227 (11 methods + ~20 instance fields prefixed `_dxCpt`). It could
 *   not be reused outside the workspace, which forced the amendment flow to
 *   either duplicate the picker or navigate away to the full encounter page.
 *   Both were wrong — one responsibility, two implementations. See the
 *   `rules/technical/amendment-addendum.md` discussion for the full rationale.
 *
 * ---------------------------------------------------------------------------
 * Public API
 * ---------------------------------------------------------------------------
 *   const picker = new DxCptCodesComponent(options);
 *   await picker.mount();
 *   const { cpts, icds } = picker.getCurrentSelections();
 *   picker.destroy();
 *   await picker.refreshAiSuggestions();    // re-fire AI (amendment use case)
 *
 * Constructor options:
 *   container          HTMLElement  required  where to render
 *   appointmentId      number       required  used by AI + save endpoints
 *   encounterId        number       required when autoSave is true
 *   patientId          number       required when autoSave is true
 *   initialCpts        array        default []   [{ cptCode, description, units, ... }]
 *   initialIcds        array        default []   [{ code, description, ... }]
 *   autoSave           boolean      default true  if true, PUTs to encounter
 *                                                 whenever selections change
 *   maxIcdCodes        number       default 12    CMS-1500 Box 21 limit
 *   nextButton         object|null  default null  { label, onClick } — renders
 *                                                 a button in the cart footer;
 *                                                 encounter step uses this to
 *                                                 navigate to the Checkout step
 *   onChange           fn           default noop  (cpts, icds) => void
 *   onSaveStatus       fn           default noop  ('saving'|'saved'|'error', meta) => void
 *                                                 — only called when autoSave=true
 *   onStepComplete     fn           default noop  (hasCodes) => void — encounter
 *                                                 step uses this to tick its
 *                                                 sidebar checkmark
 *
 * Instance state:
 *   selectedIcds, selectedCpts, activeTab, icdAiSuggestions, cptAiSuggestions,
 *   icdSuggestionsLoaded, cptSuggestionsLoaded, favoritesIcd, favoritesCpt,
 *   favoritesLoaded, familyOverlayOpenForCode, familyPanelCache
 *
 * Static state (shared across all instances):
 *   DxCptCodesComponent._familyOverlayInjected — the floating family overlay
 *     is appended to <body> once per page; every instance reuses the same
 *     DOM node. Only one instance can have it "open" at a time, tracked via
 *     the opening instance's familyOverlayOpenForCode field.
 *
 * Notes for future maintainers:
 *   - Favorites are per-logged-in-user (server-scoped by JWT NameIdentifier).
 *     Same API for both consumers.
 *   - AI endpoints currently key by appointmentId — they pull the latest
 *     signed note text server-side. Amendment reuse today sees the same
 *     suggestions as the encounter flow. When we later add a "fresh AI based
 *     on the amended (not-yet-saved) note text" feature, we will add a new
 *     endpoint that accepts raw text and wire refreshAiSuggestions() to it.
 *   - The family overlay is appended to <body> once and reused. destroy()
 *     closes it if this instance opened it, but does NOT remove the DOM node.
 * ============================================================================ */
(function () {
    'use strict';

    class DxCptCodesComponent {
        constructor(options = {}) {
            // Required
            this.container = options.container;
            if (!this.container) throw new Error('DxCptCodesComponent: container is required');
            this.appointmentId = options.appointmentId;

            // Required-when-autoSave
            this.encounterId = options.encounterId;
            this.patientId = options.patientId;

            // Optional with defaults
            this.autoSave = options.autoSave !== false;
            this.maxIcdCodes = options.maxIcdCodes || 12;
            this.nextButton = options.nextButton || null;

            // AI source — controls which endpoint the component hits for AI suggestions:
            //   'appointment' (default) — /appointments/{id}/suggest-icd|cpt
            //     Reads the SAVED note text server-side. Right for the encounter flow
            //     where the note has been persisted by the time the provider gets to
            //     the Dx & CPT step.
            //   'text' — /billing/ai/suggest-icd-from-text|cpt-from-text
            //     Takes note text from `getNoteTextForAi()` callback. Right for the
            //     amendment modal where the provider has edited the note in-memory
            //     and the AI needs to see the AMENDED text, not the old saved one.
            this.aiSource = options.aiSource === 'text' ? 'text' : 'appointment';
            this.getNoteTextForAi = typeof options.getNoteTextForAi === 'function' ? options.getNoteTextForAi : null;

            // Callbacks
            this.onChange = typeof options.onChange === 'function' ? options.onChange : () => {};
            this.onSaveStatus = typeof options.onSaveStatus === 'function' ? options.onSaveStatus : () => {};
            this.onStepComplete = typeof options.onStepComplete === 'function' ? options.onStepComplete : () => {};

            // Initial state — cloned so external callers can't mutate our internals.
            //
            // We tolerate TWO on-wire formats because historically the encounter
            // Dx & CPT step stored full objects while the amendment flow stored
            // flat code strings. Either way, normalize to the object format the
            // cart/search/favourites code expects:
            //   ICD → { code, description, aiSuggested }
            //   CPT → { cptCode, description, units, rationale, aiSuggested }
            this.selectedIcds = (Array.isArray(options.initialIcds) ? options.initialIcds : []).map(i => {
                if (i == null) return null;
                if (typeof i === 'string') return { code: i, description: '', aiSuggested: false };
                return { code: i.code || i.Code || '', description: i.description || i.Description || '', aiSuggested: !!(i.aiSuggested || i.AiSuggested) };
            }).filter(x => x && x.code);

            this.selectedCpts = (Array.isArray(options.initialCpts) ? options.initialCpts : []).map(c => {
                if (c == null) return null;
                if (typeof c === 'string') return { cptCode: c, description: '', units: 1, rationale: '', aiSuggested: false };
                return {
                    cptCode: c.cptCode || c.CptCode || '',
                    description: c.description || c.Description || '',
                    units: c.units || c.Units || 1,
                    rationale: c.rationale || c.Rationale || '',
                    aiSuggested: !!(c.aiSuggested || c.AiSuggested)
                };
            }).filter(x => x && x.cptCode);

            // Tab state
            this.activeTab = 'icd10';   // 'icd10' | 'cpt'

            // AI suggestion state
            this.icdAiSuggestions = [];
            this.cptAiSuggestions = [];
            this.icdSuggestionsLoaded = false;
            this.cptSuggestionsLoaded = false;

            // Favorites state
            this.favoritesIcd = [];
            this.favoritesCpt = [];
            this.favoritesLoaded = false;

            // Family overlay state (per-instance)
            this.familyOverlayOpenForCode = null;
            this.familyPanelCache = {};

            // Internal
            this.saveTimer = null;
            this._outsideClickHandler = null;   // for search results dropdown
            this._destroyed = false;
        }

        // ====================================================================
        // LIFECYCLE
        // ====================================================================

        async mount() {
            if (this._destroyed) return;

            // Load provider favorites (per-user starred ICD-10/CPT codes) — once per instance.
            // Cheap call (typically <50 rows per type), cached to avoid re-loading on tab switches.
            if (!this.favoritesLoaded) {
                await this._loadFavorites();
            }

            // Render two-column layout
            this._renderTwoColumnLayout();

            // Auto-fire ICD-10 AI suggestions on mount (until loaded once this instance).
            // Even if codes are already picked, we still show suggestions in case the provider
            // wants to add more (e.g. comorbidities, secondary diagnoses).
            if (!this.icdSuggestionsLoaded) {
                await this._loadIcdAiSuggestions();
                this._renderLeftColumn();
            }

            // CPT AI: only fire if we already have ICD selections (CPT prompt depends on diagnoses).
            if (!this.cptSuggestionsLoaded && this.selectedIcds.length > 0) {
                await this._loadCptAiSuggestions();
                this._renderLeftColumn();
            }
        }

        /**
         * Returns the current selections. Consumers call this when they need to
         * capture the final state (e.g. Amendment modal at Sign time).
         */
        getCurrentSelections() {
            return {
                icds: [...this.selectedIcds],
                cpts: [...this.selectedCpts]
            };
        }

        /**
         * Re-fires AI suggestions. Amendment modal may call this after the provider
         * has edited the note content and wants fresh suggestions. Renders the
         * column BEFORE each fetch so the "Analyzing..." spinner actually displays
         * while the network call is in flight.
         */
        async refreshAiSuggestions() {
            this.icdSuggestionsLoaded = false;
            this.cptSuggestionsLoaded = false;
            this._renderLeftColumn();   // spinner visible
            await this._loadIcdAiSuggestions();
            this._renderLeftColumn();   // ICD results rendered; CPT still loading if applicable
            if (this.selectedIcds.length > 0) {
                await this._loadCptAiSuggestions();
                this._renderLeftColumn();
            }
        }

        destroy() {
            this._destroyed = true;
            // Clear any pending save
            if (this.saveTimer) { clearTimeout(this.saveTimer); this.saveTimer = null; }
            // Close family overlay if this instance owned it
            if (this.familyOverlayOpenForCode) this._closeFamilyOverlay();
            // Remove outside-click handler for the search dropdown
            if (this._outsideClickHandler) {
                document.removeEventListener('click', this._outsideClickHandler);
                this._outsideClickHandler = null;
            }
            // Clear container content (detaches all bound handlers inside)
            if (this.container) this.container.innerHTML = '';
        }

        // ====================================================================
        // AI SUGGESTIONS (ICD + CPT)
        // ====================================================================

        async _loadIcdAiSuggestions() {
            try {
                const response = this.aiSource === 'text'
                    ? await window.apiRequest('/billing/ai/suggest-icd-from-text', {
                          method: 'POST',
                          body: { NoteContent: this._getTextForAi() },
                          showLoader: false
                      })
                    : await window.apiRequest(`/appointments/${this.appointmentId}/suggest-icd`, {
                          method: 'POST',
                          showLoader: false
                      });
                this.icdAiSuggestions = [];
                if (response.Success && response.Suggestions && response.Suggestions.length > 0) {
                    this.icdAiSuggestions = response.Suggestions.map(s => ({
                        code: s.Code || s.code,
                        description: s.Description || s.description,
                        confidence: s.Confidence ?? s.confidence ?? 50,
                        rationale: s.Rationale || s.rationale || ''
                    }));
                }
                this.icdSuggestionsLoaded = true;
            } catch (e) {
                console.error('[DxCptCodes] ICD-10 suggestion error:', e);
                this.icdAiSuggestions = [];
                this.icdSuggestionsLoaded = true;
                if (window.showToast) window.showToast('AI ICD-10 suggestions unavailable. You can search or add codes manually.', 'warning');
            }
        }

        async _loadCptAiSuggestions() {
            try {
                // CPT AI depends on the ICD codes the user has picked + the note text.
                // For the 'text' source we pass both in the request body.
                const icdCsv = (this.selectedIcds || []).map(c => c.code).filter(Boolean).join(',');
                const response = this.aiSource === 'text'
                    ? await window.apiRequest('/billing/ai/suggest-cpt-from-text', {
                          method: 'POST',
                          body: { NoteContent: this._getTextForAi(), DiagnosisCodes: icdCsv },
                          showLoader: false
                      })
                    : await window.apiRequest(`/appointments/${this.appointmentId}/suggest-cpt`, {
                          method: 'POST',
                          showLoader: false
                      });
                this.cptAiSuggestions = [];
                if (response.Success && response.Suggestions && response.Suggestions.length > 0) {
                    this.cptAiSuggestions = response.Suggestions.map(s => ({
                        cptCode: s.CptCode || s.cptCode,
                        description: s.Description || s.description,
                        units: s.Units || s.units || 1,
                        rationale: s.Rationale || s.rationale || ''
                    }));
                }
                this.cptSuggestionsLoaded = true;
            } catch (e) {
                console.error('[DxCptCodes] CPT suggestion error:', e);
                this.cptAiSuggestions = [];
                this.cptSuggestionsLoaded = true;
                if (window.showToast) window.showToast('AI CPT suggestions unavailable. You can add codes manually.', 'warning');
            }
        }

        // Strip HTML tags and collapse whitespace for the AI prompt. We do this
        // client-side only for the 'text' source; the server does its own
        // sanitization on the saved-note path.
        _getTextForAi() {
            if (!this.getNoteTextForAi) return '';
            const raw = this.getNoteTextForAi() || '';
            const div = document.createElement('div');
            div.innerHTML = raw;
            const text = div.textContent || div.innerText || '';
            return text.replace(/\s+/g, ' ').trim().substring(0, 8000);
        }

        // ====================================================================
        // RENDERING — top-level layout
        // ====================================================================

        _renderTwoColumnLayout() {
            this.container.innerHTML = `
                <div class="row" id="dxCptStepContent">
                    <div class="col-md-7" id="dxCptLeftColumn"></div>
                    <div class="col-md-5 ps-md-3" id="dxCptRightColumn"></div>
                </div>
            `;
            this._renderLeftColumn();
            this._renderCart();
        }

        _renderLeftColumn() {
            const col = this.container.querySelector('#dxCptLeftColumn');
            if (!col) return;

            const isIcdTab = this.activeTab === 'icd10';
            const cap = this.maxIcdCodes;
            const icdCount = this.selectedIcds.length;
            const icdAtCap = icdCount >= cap;

            col.innerHTML = `
                <!-- Search Card with tabs -->
                <div class="card mb-3" id="dxCptSearchCard">
                    <div class="card-header p-0">
                        <ul class="nav nav-tabs" role="tablist" style="border-bottom:none;">
                            <li class="nav-item">
                                <button class="nav-link ${isIcdTab ? 'active' : ''}" data-tab="icd10" type="button" id="dxCptTabIcd"
                                    style="font-weight:600; padding:10px 22px; ${isIcdTab ? 'color:var(--primary, #6366F1); border-bottom:3px solid var(--primary, #6366F1);' : 'color:#6b7280;'}">
                                    <i class="bi bi-heart-pulse me-1 text-primary"></i>ICD-10
                                </button>
                            </li>
                            <li class="nav-item">
                                <button class="nav-link ${!isIcdTab ? 'active' : ''}" data-tab="cpt" type="button" id="dxCptTabCpt"
                                    style="font-weight:600; padding:10px 22px; ${!isIcdTab ? 'color:#10b981; border-bottom:3px solid #10b981;' : 'color:#6b7280;'}">
                                    <i class="bi bi-tags me-1 text-success"></i>CPT
                                </button>
                            </li>
                        </ul>
                    </div>
                    <div class="card-body">
                        <div class="row g-2 align-items-end position-relative">
                            <div class="col-4">
                                <label class="form-label small mb-1">Code</label>
                                <input type="text" class="form-control" id="dxCptCodeInput" placeholder="Search code..." autocomplete="off">
                            </div>
                            <div class="col-6">
                                <label class="form-label small mb-1">Description</label>
                                <input type="text" class="form-control" id="dxCptDescInput" placeholder="Search description..." autocomplete="off">
                            </div>
                            <div class="col-2">
                                <button class="btn ${isIcdTab ? 'btn-primary' : 'btn-success'} w-100" id="dxCptManualAddBtn">
                                    <i class="bi bi-plus-lg me-1"></i>Add
                                </button>
                            </div>
                            <div id="dxCptSearchResults" class="position-absolute w-100 bg-white border rounded shadow-sm d-none"
                                style="z-index:1060; max-height:280px; overflow-y:auto; top:100%; margin-top:4px;"></div>
                        </div>
                        ${isIcdTab && icdAtCap ? `
                            <small class="text-warning mt-2 d-block"><i class="bi bi-exclamation-triangle me-1"></i>Maximum ${cap} ICD-10 codes per CMS-1500 claim. Remove a code to add another.</small>
                        ` : ''}
                    </div>
                </div>

                <!-- ICD-10 AI Suggestions -->
                <div class="card mb-3" id="dxIcdAiCard">
                    <div class="card-header d-flex align-items-center gap-2">
                        <i class="bi bi-stars text-primary"></i><strong>ICD-10 AI Suggestions</strong>
                        <span class="badge bg-primary ms-auto">${this.icdAiSuggestions.length}</span>
                        <button class="btn btn-outline-primary" id="dxIcdRefreshBtn"><i class="bi bi-arrow-clockwise me-1"></i>Refresh</button>
                    </div>
                    <div class="card-body p-0" id="dxIcdAiBody">
                        ${this._renderIcdAiSuggestionsHtml()}
                    </div>
                </div>

                <!-- CPT AI Suggestions -->
                <div class="card" id="dxCptAiCard">
                    <div class="card-header d-flex align-items-center gap-2">
                        <i class="bi bi-stars text-success"></i><strong>CPT AI Suggestions</strong>
                        <span class="badge bg-success ms-auto">${this.cptAiSuggestions.length}</span>
                        <button class="btn btn-outline-success" id="dxCptRefreshBtn" ${this.selectedIcds.length === 0 ? 'disabled' : ''}>
                            <i class="bi bi-arrow-clockwise me-1"></i>Refresh
                        </button>
                    </div>
                    <div class="card-body p-0" id="dxCptAiBody">
                        ${this._renderCptAiSuggestionsHtml()}
                    </div>
                </div>
            `;

            this._bindTabsEvents();
            this._bindSearchEvents();
            this._bindManualAddEvents();
            this._bindAiEvents();
            this._bindFamilyEvents();
            this._bindRefreshEvents();
        }

        _renderIcdAiSuggestionsHtml() {
            if (!this.icdSuggestionsLoaded) {
                return `<div class="text-center py-3 text-muted"><div class="spinner-border spinner-border-sm me-2"></div>Analyzing clinical note...</div>`;
            }
            if (this.icdAiSuggestions.length === 0) {
                return `<div class="text-center py-3 text-muted small">No AI suggestions available. Use search above or add manually.</div>`;
            }
            const existing = this.selectedIcds.map(c => c.code);
            return `<div class="list-group list-group-flush">
                ${this.icdAiSuggestions.map(s => {
                    const added = existing.includes(s.code);
                    const conf = s.confidence ?? 50;
                    const confColor = conf >= 80 ? 'success' : (conf >= 50 ? 'warning' : 'secondary');
                    const confLabel = conf >= 80 ? 'High' : (conf >= 50 ? 'Medium' : 'Low');
                    const prefix = (s.code || '').substring(0, 3);
                    // NOTE: family panel is rendered as a single FLOATING OVERLAY appended to <body>,
                    // NOT inline inside this row. This decouples it from AI list re-renders so it
                    // never gets wiped by background events (AI fetch, code add/remove, etc.).
                    const starred = this._isIcdFavorite(s.code);
                    return `<div class="list-group-item">
                        <div class="d-flex align-items-center gap-2 flex-wrap">
                            <span class="badge bg-primary">${this._esc(s.code)}</span>
                            ${prefix ? `<button class="btn btn-outline-primary dx-icd-family-btn" data-source-code="${this._esc(s.code)}" data-prefix="${this._esc(prefix)}" style="padding:1px 7px; font-size:11px; border-radius:4px;" title="Show similar ${this._esc(prefix)}.x codes">
                                ${this._esc(prefix)}.x <i class="bi bi-chevron-down" style="font-size:9px;"></i>
                            </button>` : ''}
                            <span class="flex-grow-1">${this._esc(s.description)}</span>
                            <span class="badge bg-${confColor}">${confLabel} ${conf}%</span>
                            <button class="dx-icd-star ${starred ? 'starred' : ''}" data-code="${this._esc(s.code)}" data-desc="${this._esc(s.description)}"
                                title="${starred ? 'Remove from favorites' : 'Add to favorites'}"
                                style="background:none; border:none; padding:2px 4px; cursor:pointer; font-size:15px; line-height:1; color:${starred ? '#f59e0b' : '#d1d5db'};">
                                <i class="bi bi-star${starred ? '-fill' : ''}"></i>
                            </button>
                            ${added
                                ? '<span class="badge bg-success"><i class="bi bi-check-lg me-1"></i>Added</span>'
                                : `<button class="btn btn-outline-primary dx-icd-ai-add" data-code="${this._esc(s.code)}" data-desc="${this._esc(s.description)}"><i class="bi bi-plus-lg me-1"></i>Add</button>`}
                        </div>
                        ${s.rationale ? `<small class="text-muted d-block mt-1"><i class="bi bi-lightbulb me-1"></i>${this._esc(s.rationale)}</small>` : ''}
                    </div>`;
                }).join('')}
            </div>`;
        }

        _renderCptAiSuggestionsHtml() {
            if (this.selectedIcds.length === 0) {
                return `<div class="text-center py-4 text-muted">
                    <i class="bi bi-arrow-up-circle d-block mb-2" style="font-size:24px; opacity:0.5;"></i>
                    <strong>Select ICD-10 codes first</strong><br>
                    <small>CPT suggestions are generated from your diagnoses + clinical note.</small>
                </div>`;
            }
            if (!this.cptSuggestionsLoaded) {
                return `<div class="text-center py-3 text-muted"><div class="spinner-border spinner-border-sm me-2"></div>Analyzing clinical note + diagnoses...</div>`;
            }
            if (this.cptAiSuggestions.length === 0) {
                return `<div class="text-center py-3 text-muted small">No AI suggestions available. Use search above or add manually.</div>`;
            }
            const existing = this.selectedCpts.map(c => c.cptCode);
            return `<div class="list-group list-group-flush">
                ${this.cptAiSuggestions.map(s => {
                    const added = existing.includes(s.cptCode);
                    const starred = this._isCptFavorite(s.cptCode);
                    return `<div class="list-group-item">
                        <div class="d-flex align-items-center gap-2 flex-wrap">
                            <span class="badge bg-success">${this._esc(s.cptCode)}</span>
                            <span class="flex-grow-1">${this._esc(s.description)}</span>
                            <button class="dx-cpt-star ${starred ? 'starred' : ''}" data-code="${this._esc(s.cptCode)}" data-desc="${this._esc(s.description)}"
                                title="${starred ? 'Remove from favorites' : 'Add to favorites'}"
                                style="background:none; border:none; padding:2px 4px; cursor:pointer; font-size:15px; line-height:1; color:${starred ? '#f59e0b' : '#d1d5db'};">
                                <i class="bi bi-star${starred ? '-fill' : ''}"></i>
                            </button>
                            ${added
                                ? '<span class="badge bg-success"><i class="bi bi-check-lg me-1"></i>Added</span>'
                                : `<button class="btn btn-outline-success dx-cpt-ai-add" data-code="${this._esc(s.cptCode)}" data-desc="${this._esc(s.description)}" data-units="${s.units || 1}"><i class="bi bi-plus-lg me-1"></i>Add</button>`}
                        </div>
                        ${s.rationale ? `<small class="text-muted d-block mt-1"><i class="bi bi-lightbulb me-1"></i>${this._esc(s.rationale)}</small>` : ''}
                    </div>`;
                }).join('')}
            </div>`;
        }

        _renderCart() {
            const col = this.container.querySelector('#dxCptRightColumn');
            if (!col) return;

            const icd = this.selectedIcds;
            const cpt = this.selectedCpts;
            const total = icd.length + cpt.length;

            // Note: the "Next" button at the bottom is optional and comes from the consumer.
            // Encounter-step passes { label: 'Next', onClick: () => this._showStep(7) }.
            // Amendment modal passes null (no Next — modal has its own Sign Amendment footer).
            const nextBtnHtml = this.nextButton
                ? `<div class="text-end mt-3">
                       <button class="btn btn-primary" id="dxCptNextBtn">
                           ${this._esc(this.nextButton.label || 'Next')} <i class="bi bi-arrow-right ms-1"></i>
                       </button>
                   </div>`
                : '';

            // NOT sticky — per design discussion, allows scrolling beyond cart for favorites box.
            col.innerHTML = `
                <div class="card border-primary shadow-sm" style="overflow:hidden;">
                    <div class="card-header d-flex align-items-center gap-2">
                        <h6 class="mb-0 text-primary"><i class="bi bi-tags me-2 text-primary"></i>Selected Codes</h6>
                        <span class="badge bg-primary ms-auto">${total}</span>
                    </div>
                    <div class="card-body p-0" id="dxCptCartBody">
                        ${total === 0 ? `
                            <div class="text-center py-4 text-muted">
                                <i class="bi bi-tags d-block mb-2" style="font-size:28px; opacity:0.4;"></i>
                                No codes selected yet.<br>
                                <small>Use AI suggestions, search, or add manually.</small>
                            </div>
                        ` : `
                            <!-- Diagnoses -->
                            <div style="font-size:11px; font-weight:700; text-transform:uppercase; letter-spacing:0.5px; padding:8px 12px 4px; color:#6b7280; background:#f8f9fc; border-bottom:1px solid #e5e7eb;">
                                <i class="bi bi-heart-pulse me-1 text-primary"></i>Diagnoses (ICD-10)
                                <span class="badge bg-primary ms-1">${icd.length}</span>
                            </div>
                            ${icd.length === 0 ? '<div class="text-center text-muted small py-2">No diagnoses added</div>' : icd.map((c, idx) => `
                                <div style="padding:6px 10px; border-bottom:1px solid #f3f4f6;">
                                    <div class="d-flex align-items-center gap-2">
                                        <span class="text-muted small">${idx + 1}.</span>
                                        <span class="badge bg-primary">${this._esc(c.code)}</span>
                                        <span class="flex-grow-1 small">${this._esc(c.description)}</span>
                                        <button class="dx-icd-remove" data-code="${this._esc(c.code)}" title="Remove"
                                            style="background:none; border:1px solid #fca5a5; color:#ef4444; padding:1px 5px; border-radius:4px; font-size:11px; line-height:1.5;">
                                            <i class="bi bi-x"></i>
                                        </button>
                                    </div>
                                </div>
                            `).join('')}

                            <!-- Procedures -->
                            <div style="font-size:11px; font-weight:700; text-transform:uppercase; letter-spacing:0.5px; padding:8px 12px 4px; color:#6b7280; background:#f8f9fc; border-bottom:1px solid #e5e7eb;">
                                <i class="bi bi-tags me-1 text-success"></i>Procedures (CPT)
                                <span class="badge bg-success ms-1">${cpt.length}</span>
                            </div>
                            ${cpt.length === 0 ? '<div class="text-center text-muted small py-2">No procedures added</div>' : cpt.map(c => `
                                <div style="padding:6px 10px; border-bottom:1px solid #f3f4f6;">
                                    <div class="d-flex align-items-center gap-2">
                                        <span class="badge bg-success">${this._esc(c.cptCode)}</span>
                                        <span class="flex-grow-1 small">${this._esc(c.description)}</span>
                                        <label class="text-muted small mb-0 ms-1" style="white-space:nowrap;">Units:</label>
                                        <input type="number" class="form-control form-control-sm dx-cpt-units" style="width:52px;" min="1" max="99" value="${c.units}" data-cpt-code="${this._esc(c.cptCode)}">
                                        <button class="dx-cpt-remove" data-cpt-code="${this._esc(c.cptCode)}" title="Remove"
                                            style="background:none; border:1px solid #fca5a5; color:#ef4444; padding:1px 5px; border-radius:4px; font-size:11px; line-height:1.5;">
                                            <i class="bi bi-x"></i>
                                        </button>
                                    </div>
                                </div>
                            `).join('')}
                        `}
                    </div>
                    <div class="card-footer text-end small" id="dxCptSaveStatus">
                        ${this.autoSave
                            ? '<i class="bi bi-check-circle text-success me-1"></i>Auto-saved'
                            : '<i class="bi bi-info-circle text-muted me-1"></i>Save on confirm'}
                    </div>
                </div>

                ${this._renderFavoritesCardHtml()}

                ${nextBtnHtml}
            `;

            this._bindCartEvents();
            this._bindFavoritesEvents();
            if (this.nextButton) {
                const btn = this.container.querySelector('#dxCptNextBtn');
                if (btn) btn.addEventListener('click', this.nextButton.onClick);
            }

            // Step is "complete" when at least one CPT exists (CPT is required for charges).
            this.onStepComplete(cpt.length > 0);
        }

        // ====================================================================
        // EVENT BINDING
        // ====================================================================

        _bindTabsEvents() {
            const tabIcd = this.container.querySelector('#dxCptTabIcd');
            const tabCpt = this.container.querySelector('#dxCptTabCpt');
            if (tabIcd) tabIcd.addEventListener('click', () => this._switchTab('icd10'));
            if (tabCpt) tabCpt.addEventListener('click', () => this._switchTab('cpt'));
        }

        _switchTab(tab) {
            this.activeTab = tab;
            this._renderLeftColumn();
            // Auto-scroll to the matching AI section
            const target = this.container.querySelector(tab === 'icd10' ? '#dxIcdAiCard' : '#dxCptAiCard');
            if (target) {
                const top = target.getBoundingClientRect().top + window.scrollY - 80;
                window.scrollTo({ top, behavior: 'smooth' });
            }
        }

        _bindSearchEvents() {
            const codeInput = this.container.querySelector('#dxCptCodeInput');
            const descInput = this.container.querySelector('#dxCptDescInput');
            const results   = this.container.querySelector('#dxCptSearchResults');
            if (!codeInput || !descInput || !results) return;

            let debounceTimer = null;
            const doSearch = () => {
                clearTimeout(debounceTimer);
                const codeQuery = codeInput.value.trim();
                const descQuery = descInput.value.trim();
                if (codeQuery.length < 2 && descQuery.length < 2) {
                    results.classList.add('d-none');
                    results.innerHTML = '';
                    return;
                }
                debounceTimer = setTimeout(async () => {
                    if (this.activeTab === 'icd10') {
                        await this._performIcdSearch(codeQuery, descQuery, results);
                    } else {
                        await this._performCptSearch(codeQuery, descQuery, results);
                    }
                }, 300);
            };
            codeInput.addEventListener('input', doSearch);
            descInput.addEventListener('input', doSearch);
            [codeInput, descInput].forEach(el => el.addEventListener('keydown', e => { if (e.key === 'Escape') results.classList.add('d-none'); }));

            // Close on outside click — use capture to avoid stale handler conflicts.
            // Saved on the instance so destroy() can remove it.
            if (this._outsideClickHandler) {
                document.removeEventListener('click', this._outsideClickHandler);
            }
            this._outsideClickHandler = (e) => {
                if (!codeInput.contains(e.target) && !descInput.contains(e.target) && !results.contains(e.target)) {
                    results.classList.add('d-none');
                }
            };
            document.addEventListener('click', this._outsideClickHandler);
        }

        async _performIcdSearch(codeQuery, descQuery, results) {
            try {
                const query = codeQuery.length >= 2 ? codeQuery : descQuery;
                const codes = await window.apiRequest(`/icd-codes/search?query=${encodeURIComponent(query)}&take=20`, { showLoader: false });
                if (!codes || codes.length === 0) {
                    results.innerHTML = `<div class="p-3 text-muted">No ICD-10 codes found</div>`;
                    results.classList.remove('d-none');
                    return;
                }
                const existing = this.selectedIcds.map(c => c.code);
                let filtered = codes.filter(c => !existing.includes(c.Code || c.code));
                if (codeQuery.length >= 2 && descQuery.length >= 2) {
                    const dq = descQuery.toLowerCase();
                    filtered = filtered.filter(c => (c.Description || c.description || '').toLowerCase().includes(dq));
                }
                if (filtered.length === 0) {
                    results.innerHTML = `<div class="p-3 text-muted">No matching codes (or already added)</div>`;
                    results.classList.remove('d-none');
                    return;
                }
                results.innerHTML = filtered.map(c => `
                    <a href="#" class="list-group-item list-group-item-action d-flex align-items-center gap-2 dx-icd-search-item"
                        data-code="${this._esc(c.Code || c.code)}" data-desc="${this._esc(c.Description || c.description)}">
                        <span class="badge bg-primary">${this._esc(c.Code || c.code)}</span>
                        <span>${this._esc(c.Description || c.description)}</span>
                    </a>
                `).join('');
                results.classList.remove('d-none');
                results.querySelectorAll('.dx-icd-search-item').forEach(item => {
                    item.addEventListener('click', e => {
                        e.preventDefault();
                        this._addIcdToCart(item.dataset.code, item.dataset.desc, false);
                        this.container.querySelector('#dxCptCodeInput').value = '';
                        this.container.querySelector('#dxCptDescInput').value = '';
                        results.classList.add('d-none');
                        results.innerHTML = '';
                    });
                });
            } catch (e) {
                console.error('[DxCptCodes] ICD search error:', e);
                results.innerHTML = `<div class="p-3 text-danger">Search failed</div>`;
                results.classList.remove('d-none');
            }
        }

        async _performCptSearch(codeQuery, descQuery, results) {
            try {
                const query = codeQuery.length >= 2 ? codeQuery : descQuery;
                const codes = await window.apiRequest(`/lookups/cpt-codes?search=${encodeURIComponent(query)}`, { showLoader: false });
                if (!codes || codes.length === 0) {
                    results.innerHTML = `<div class="p-3 text-muted">No CPT codes found</div>`;
                    results.classList.remove('d-none');
                    return;
                }
                const existing = this.selectedCpts.map(c => c.cptCode);
                let filtered = codes.filter(c => !existing.includes(c.Code || c.code));
                if (codeQuery.length >= 2 && descQuery.length >= 2) {
                    const dq = descQuery.toLowerCase();
                    filtered = filtered.filter(c => (c.Description || c.description || '').toLowerCase().includes(dq));
                }
                if (filtered.length === 0) {
                    results.innerHTML = `<div class="p-3 text-muted">No matching codes (or already added)</div>`;
                    results.classList.remove('d-none');
                    return;
                }
                results.innerHTML = filtered.map(c => `
                    <a href="#" class="list-group-item list-group-item-action d-flex align-items-center gap-2 dx-cpt-search-item"
                        data-code="${this._esc(c.Code || c.code)}" data-desc="${this._esc(c.Description || c.description)}">
                        <span class="badge bg-success">${this._esc(c.Code || c.code)}</span>
                        <span>${this._esc(c.Description || c.description)}</span>
                    </a>
                `).join('');
                results.classList.remove('d-none');
                results.querySelectorAll('.dx-cpt-search-item').forEach(item => {
                    item.addEventListener('click', e => {
                        e.preventDefault();
                        this._addCptToCart(item.dataset.code, item.dataset.desc, 1, false);
                        this.container.querySelector('#dxCptCodeInput').value = '';
                        this.container.querySelector('#dxCptDescInput').value = '';
                        results.classList.add('d-none');
                        results.innerHTML = '';
                    });
                });
            } catch (e) {
                console.error('[DxCptCodes] CPT search error:', e);
                results.innerHTML = `<div class="p-3 text-danger">Search failed</div>`;
                results.classList.remove('d-none');
            }
        }

        _bindManualAddEvents() {
            const btn = this.container.querySelector('#dxCptManualAddBtn');
            if (!btn) return;
            btn.addEventListener('click', () => {
                const code = this.container.querySelector('#dxCptCodeInput')?.value?.trim();
                const desc = this.container.querySelector('#dxCptDescInput')?.value?.trim();
                if (!code || !desc) {
                    if (window.showToast) window.showToast('Please enter both code and description.', 'warning');
                    return;
                }
                if (this.activeTab === 'icd10') {
                    if (this.selectedIcds.some(c => c.code === code)) {
                        if (window.showToast) window.showToast('This ICD-10 code is already added.', 'warning');
                        return;
                    }
                    this._addIcdToCart(code, desc, false);
                } else {
                    if (this.selectedCpts.some(c => c.cptCode === code)) {
                        if (window.showToast) window.showToast('This CPT code is already added.', 'warning');
                        return;
                    }
                    this._addCptToCart(code, desc, 1, false);
                }
                this.container.querySelector('#dxCptCodeInput').value = '';
                this.container.querySelector('#dxCptDescInput').value = '';
            });
        }

        _bindAiEvents() {
            this.container.querySelectorAll('.dx-icd-ai-add').forEach(btn => {
                btn.addEventListener('click', () => this._addIcdToCart(btn.dataset.code, btn.dataset.desc, true));
            });
            this.container.querySelectorAll('.dx-cpt-ai-add').forEach(btn => {
                btn.addEventListener('click', () => this._addCptToCart(btn.dataset.code, btn.dataset.desc, parseInt(btn.dataset.units) || 1, true));
            });
            // Star toggles on AI suggestion rows — fire-and-forget, optimistic UI
            this.container.querySelectorAll('.dx-icd-star').forEach(btn => {
                btn.addEventListener('click', async () => {
                    await this._toggleIcdFavorite(btn.dataset.code, btn.dataset.desc);
                    this._renderLeftColumn();
                    this._renderFavoritesCardOnly();
                });
            });
            this.container.querySelectorAll('.dx-cpt-star').forEach(btn => {
                btn.addEventListener('click', async () => {
                    await this._toggleCptFavorite(btn.dataset.code, btn.dataset.desc);
                    this._renderLeftColumn();
                    this._renderFavoritesCardOnly();
                });
            });
        }

        _bindFamilyEvents() {
            // Pills are rebuilt every time the AI list re-renders, so wire click handlers each time.
            this.container.querySelectorAll('.dx-icd-family-btn').forEach(btn => {
                btn.addEventListener('click', () => this._toggleFamilyPanel(btn.dataset.sourceCode, btn.dataset.prefix, btn));
            });
        }

        _bindRefreshEvents() {
            const icdBtn = this.container.querySelector('#dxIcdRefreshBtn');
            const cptBtn = this.container.querySelector('#dxCptRefreshBtn');
            if (icdBtn) {
                icdBtn.addEventListener('click', async () => {
                    this.icdSuggestionsLoaded = false;
                    this._renderLeftColumn();    // show spinner in the suggestions card
                    await this._loadIcdAiSuggestions();
                    this._renderLeftColumn();
                });
            }
            if (cptBtn) {
                cptBtn.addEventListener('click', async () => {
                    if (this.selectedIcds.length === 0) return;
                    this.cptSuggestionsLoaded = false;
                    this._renderLeftColumn();    // show spinner in the suggestions card
                    await this._loadCptAiSuggestions();
                    this._renderLeftColumn();
                });
            }
        }

        _bindCartEvents() {
            this.container.querySelectorAll('.dx-icd-remove').forEach(btn => {
                btn.addEventListener('click', () => this._removeIcdFromCart(btn.dataset.code));
            });
            this.container.querySelectorAll('.dx-cpt-remove').forEach(btn => {
                btn.addEventListener('click', () => this._removeCptFromCart(btn.dataset.cptCode));
            });
            this.container.querySelectorAll('.dx-cpt-units').forEach(input => {
                input.addEventListener('change', () => {
                    const item = this.selectedCpts.find(c => c.cptCode === input.dataset.cptCode);
                    if (item) item.units = Math.max(1, parseInt(input.value) || 1);
                    this._emitChange();
                    this._saveSelections();
                });
            });
        }

        // ====================================================================
        // CART — ICD add / remove
        // ====================================================================

        async _addIcdToCart(code, description, aiSuggested) {
            if (this.selectedIcds.some(c => c.code === code)) return;
            if (this.selectedIcds.length >= this.maxIcdCodes) {
                if (window.showToast) window.showToast(`Maximum ${this.maxIcdCodes} ICD-10 codes per CMS-1500 claim. Remove one to add another.`, 'warning');
                return;
            }
            this.selectedIcds.push({ code, description, aiSuggested: !!aiSuggested });
            // Close any open family overlay — picking a code ends the browse session.
            this._closeFamilyOverlay();
            this._renderLeftColumn();
            this._renderCart();
            this._emitChange();
            this._saveSelections();
            // Auto-fire CPT suggestions on first ICD added
            if (this.selectedIcds.length === 1 && !this.cptSuggestionsLoaded && this.selectedCpts.length === 0) {
                await this._loadCptAiSuggestions();
                this._renderLeftColumn();
            }
        }

        _removeIcdFromCart(code) {
            this.selectedIcds = this.selectedIcds.filter(c => c.code !== code);
            this._renderLeftColumn();
            this._renderCart();
            this._emitChange();
            this._saveSelections();
        }

        // ====================================================================
        // CART — CPT add / remove
        // ====================================================================

        _addCptToCart(code, description, units, aiSuggested) {
            if (this.selectedCpts.some(c => c.cptCode === code)) return;
            this.selectedCpts.push({ cptCode: code, description, units, rationale: '', aiSuggested: !!aiSuggested });
            this._renderLeftColumn();
            this._renderCart();
            this._emitChange();
            this._saveSelections();
        }

        _removeCptFromCart(code) {
            this.selectedCpts = this.selectedCpts.filter(c => c.cptCode !== code);
            this._renderLeftColumn();
            this._renderCart();
            this._emitChange();
            this._saveSelections();
        }

        _emitChange() {
            try { this.onChange([...this.selectedCpts], [...this.selectedIcds]); }
            catch (e) { console.error('[DxCptCodes] onChange handler threw:', e); }
        }

        // ====================================================================
        // PERSISTENCE (only when autoSave === true)
        //   Debounced PUT to the encounter that writes BOTH IcdSelections and
        //   CptSelections in one call. Amendment flow sets autoSave=false, which
        //   makes this a no-op; the amendment modal handles saving atomically
        //   at Sign time together with the note version.
        // ====================================================================

        async _saveSelections() {
            if (!this.autoSave) return;

            clearTimeout(this.saveTimer);
            const statusEl = this.container.querySelector('#dxCptSaveStatus');
            if (statusEl) statusEl.innerHTML = `<i class="bi bi-arrow-repeat me-1"></i>Saving...`;
            this.onSaveStatus('saving');

            this.saveTimer = setTimeout(async () => {
                try {
                    const icdJson = JSON.stringify(this.selectedIcds);
                    const cptJson = JSON.stringify(this.selectedCpts);
                    await window.apiRequest(`/patients/${this.patientId}/encounters/${this.encounterId}`, {
                        method: 'PUT',
                        body: { IcdSelections: icdJson, CptSelections: cptJson },
                        showLoader: false
                    });
                    const now = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
                    if (statusEl) statusEl.innerHTML = `<i class="bi bi-check-circle text-success me-1"></i>Saved at ${now}`;
                    this.onSaveStatus('saved', { icdJson, cptJson, savedAt: now });
                } catch (e) {
                    console.error('[DxCptCodes] Save error:', e);
                    if (statusEl) statusEl.innerHTML = `<i class="bi bi-exclamation-circle text-danger me-1"></i>Save failed`;
                    this.onSaveStatus('error', { error: e });
                }
            }, 800);
        }

        // ====================================================================
        // PROVIDER FAVORITES (per-user starred codes, strict server-side isolation)
        // ====================================================================

        async _loadFavorites() {
            try {
                const [icd, cpt] = await Promise.all([
                    window.apiRequest('/favorites?type=ICD10', { showLoader: false }).catch(() => []),
                    window.apiRequest('/favorites?type=CPT',   { showLoader: false }).catch(() => [])
                ]);
                this.favoritesIcd = (icd || []).map(f => ({ code: f.Code || f.code, description: f.Description || f.description }));
                this.favoritesCpt = (cpt || []).map(f => ({ code: f.Code || f.code, description: f.Description || f.description }));
                this.favoritesLoaded = true;
            } catch (e) {
                console.error('[DxCptCodes] Favorites load error:', e);
                this.favoritesIcd = [];
                this.favoritesCpt = [];
                this.favoritesLoaded = true;
            }
        }

        _isIcdFavorite(code) { return this.favoritesIcd.some(f => f.code === code); }
        _isCptFavorite(code) { return this.favoritesCpt.some(f => f.code === code); }

        async _toggleIcdFavorite(code, description) {
            const idx = this.favoritesIcd.findIndex(f => f.code === code);
            if (idx >= 0) {
                this.favoritesIcd.splice(idx, 1);
                try {
                    await window.apiRequest(`/favorites?type=ICD10&code=${encodeURIComponent(code)}`, { method: 'DELETE', showLoader: false });
                } catch (e) {
                    this.favoritesIcd.push({ code, description });
                    if (window.showToast) window.showToast('Failed to remove favorite. Please try again.', 'danger');
                }
            } else {
                this.favoritesIcd.push({ code, description });
                try {
                    await window.apiRequest('/favorites', {
                        method: 'POST',
                        body: { CodeType: 'ICD10', Code: code, Description: description },
                        showLoader: false
                    });
                } catch (e) {
                    this.favoritesIcd = this.favoritesIcd.filter(f => f.code !== code);
                    if (window.showToast) window.showToast('Failed to add favorite. Please try again.', 'danger');
                }
            }
        }

        async _toggleCptFavorite(code, description) {
            const idx = this.favoritesCpt.findIndex(f => f.code === code);
            if (idx >= 0) {
                this.favoritesCpt.splice(idx, 1);
                try {
                    await window.apiRequest(`/favorites?type=CPT&code=${encodeURIComponent(code)}`, { method: 'DELETE', showLoader: false });
                } catch (e) {
                    this.favoritesCpt.push({ code, description });
                    if (window.showToast) window.showToast('Failed to remove favorite. Please try again.', 'danger');
                }
            } else {
                this.favoritesCpt.push({ code, description });
                try {
                    await window.apiRequest('/favorites', {
                        method: 'POST',
                        body: { CodeType: 'CPT', Code: code, Description: description },
                        showLoader: false
                    });
                } catch (e) {
                    this.favoritesCpt = this.favoritesCpt.filter(f => f.code !== code);
                    if (window.showToast) window.showToast('Failed to add favorite. Please try again.', 'danger');
                }
            }
        }

        _renderFavoritesCardHtml() {
            const existingIcd = this.selectedIcds.map(c => c.code);
            const existingCpt = this.selectedCpts.map(c => c.cptCode);

            const icdRowsHtml = this.favoritesIcd.length === 0
                ? `<div class="text-center py-3 text-muted small"><i class="bi bi-star me-1"></i>No ICD-10 favorites yet — star a suggestion to add.</div>`
                : this.favoritesIcd.map(f => {
                    const alreadyAdded = existingIcd.includes(f.code);
                    return `<div style="display:flex; align-items:center; gap:6px; padding:6px 10px; border-bottom:1px solid #f3f4f6; font-size:12px;">
                        <span class="badge bg-primary">${this._esc(f.code)}</span>
                        <span class="flex-grow-1">${this._esc(f.description)}</span>
                        ${alreadyAdded
                            ? '<span class="badge bg-success"><i class="bi bi-check-lg me-1"></i>Added</span>'
                            : `<button class="btn btn-outline-primary fav-icd-add" data-code="${this._esc(f.code)}" data-desc="${this._esc(f.description)}" style="padding:2px 10px; font-size:11px;"><i class="bi bi-plus-lg me-1"></i>Add</button>`}
                        <button class="fav-icd-unstar" data-code="${this._esc(f.code)}" data-desc="${this._esc(f.description)}" title="Remove from favorites"
                            style="background:none; border:none; padding:2px 4px; cursor:pointer; font-size:14px; line-height:1; color:#f59e0b;">
                            <i class="bi bi-star-fill"></i>
                        </button>
                    </div>`;
                }).join('');

            const cptRowsHtml = this.favoritesCpt.length === 0
                ? `<div class="text-center py-3 text-muted small"><i class="bi bi-star me-1"></i>No CPT favorites yet — star a suggestion to add.</div>`
                : this.favoritesCpt.map(f => {
                    const alreadyAdded = existingCpt.includes(f.code);
                    return `<div style="display:flex; align-items:center; gap:6px; padding:6px 10px; border-bottom:1px solid #f3f4f6; font-size:12px;">
                        <span class="badge bg-success">${this._esc(f.code)}</span>
                        <span class="flex-grow-1">${this._esc(f.description)}</span>
                        ${alreadyAdded
                            ? '<span class="badge bg-success"><i class="bi bi-check-lg me-1"></i>Added</span>'
                            : `<button class="btn btn-outline-success fav-cpt-add" data-code="${this._esc(f.code)}" data-desc="${this._esc(f.description)}" style="padding:2px 10px; font-size:11px;"><i class="bi bi-plus-lg me-1"></i>Add</button>`}
                        <button class="fav-cpt-unstar" data-code="${this._esc(f.code)}" data-desc="${this._esc(f.description)}" title="Remove from favorites"
                            style="background:none; border:none; padding:2px 4px; cursor:pointer; font-size:14px; line-height:1; color:#f59e0b;">
                            <i class="bi bi-star-fill"></i>
                        </button>
                    </div>`;
                }).join('');

            return `<div class="card shadow-sm mt-3" id="dxCptFavoritesCard" style="border-color:#fde68a;">
                <div class="card-header d-flex align-items-center gap-2" style="background:#fffbeb; border-bottom-color:#fde68a;">
                    <h6 class="mb-0" style="color:#92400e;"><i class="bi bi-star-fill me-2 text-warning"></i>Favorites</h6>
                    <small class="text-muted ms-auto" style="font-size:10px;">Your personal list</small>
                </div>
                <div class="card-body p-0" id="dxCptFavoritesBody">
                    <div style="font-size:11px; font-weight:700; text-transform:uppercase; letter-spacing:0.5px; padding:7px 12px 4px; color:#6b7280; background:#fffbeb; border-bottom:1px solid #fde68a;">
                        <i class="bi bi-heart-pulse me-1 text-primary"></i>ICD-10 Favorites
                    </div>
                    ${icdRowsHtml}
                    <div style="font-size:11px; font-weight:700; text-transform:uppercase; letter-spacing:0.5px; padding:7px 12px 4px; color:#6b7280; background:#fffbeb; border-top:1px solid #e5e7eb; border-bottom:1px solid #fde68a;">
                        <i class="bi bi-tags me-1 text-success"></i>CPT Favorites
                    </div>
                    ${cptRowsHtml}
                </div>
            </div>`;
        }

        _bindFavoritesEvents() {
            this.container.querySelectorAll('.fav-icd-add').forEach(btn => {
                btn.addEventListener('click', () => this._addIcdToCart(btn.dataset.code, btn.dataset.desc, false));
            });
            this.container.querySelectorAll('.fav-cpt-add').forEach(btn => {
                btn.addEventListener('click', () => this._addCptToCart(btn.dataset.code, btn.dataset.desc, 1, false));
            });
            this.container.querySelectorAll('.fav-icd-unstar').forEach(btn => {
                btn.addEventListener('click', async () => {
                    await this._toggleIcdFavorite(btn.dataset.code, btn.dataset.desc);
                    this._renderLeftColumn();
                    this._renderFavoritesCardOnly();
                });
            });
            this.container.querySelectorAll('.fav-cpt-unstar').forEach(btn => {
                btn.addEventListener('click', async () => {
                    await this._toggleCptFavorite(btn.dataset.code, btn.dataset.desc);
                    this._renderLeftColumn();
                    this._renderFavoritesCardOnly();
                });
            });
        }

        // Re-render JUST the Favorites card without rebuilding the entire right
        // column. Used by star toggles so the Selected Codes card and other
        // cart state stay untouched.
        _renderFavoritesCardOnly() {
            const card = this.container.querySelector('#dxCptFavoritesCard');
            if (!card) return;
            const tmp = document.createElement('div');
            tmp.innerHTML = this._renderFavoritesCardHtml();
            card.replaceWith(tmp.firstElementChild);
            this._bindFavoritesEvents();
        }

        // ====================================================================
        // FAMILY OVERLAY — single floating panel appended to <body>.
        //
        // One overlay per page, shared across all component instances.
        // Decoupled from the AI list: AI re-renders never touch it.
        //
        // Locked UX rules:
        //   - Outside-click does NOT close (prevents accidental dismissal)
        //   - Closes on: × button, Esc key, click same pill again, pick a code,
        //     window resize, component destroy
        //   - On AI re-render: overlay stays put at its original position
        // ====================================================================

        _ensureFamilyOverlayExists() {
            if (DxCptCodesComponent._familyOverlayInjected) return;
            DxCptCodesComponent._familyOverlayInjected = true;

            // Inject overlay CSS once
            if (!document.getElementById('dxIcdFamilyOverlayStyles')) {
                const style = document.createElement('style');
                style.id = 'dxIcdFamilyOverlayStyles';
                style.textContent = `
                    .dx-icd-family-overlay { position:absolute; background:#fff; border:1px solid #c7d2fe; border-radius:8px;
                        box-shadow:0 12px 28px rgba(99,102,241,0.18), 0 2px 6px rgba(0,0,0,0.05);
                        width:480px; max-width:calc(100vw - 40px); max-height:340px; overflow:hidden; z-index:1080;
                        animation:dxFamilyFadeSlide 0.12s ease-out; }
                    @keyframes dxFamilyFadeSlide {
                        from { opacity:0; transform:translateY(-4px); }
                        to   { opacity:1; transform:translateY(0); }
                    }
                    .dx-icd-family-overlay::before { content:''; position:absolute; top:-7px; left:24px;
                        width:12px; height:12px; background:#fff; border-left:1px solid #c7d2fe; border-top:1px solid #c7d2fe;
                        transform:rotate(45deg); }
                    .dx-icd-family-overlay-header { padding:8px 12px;
                        background:linear-gradient(180deg,#eef2ff 0%,#ffffff 100%);
                        border-bottom:1px solid #e0e7ff; font-size:11px; font-weight:700;
                        text-transform:uppercase; letter-spacing:0.4px; color:#4338ca;
                        display:flex; align-items:center; gap:6px; }
                    .dx-icd-family-overlay-header .dxfo-count { background:#e0e7ff; color:#4338ca;
                        padding:1px 7px; border-radius:10px; font-size:10px; margin-left:auto; }
                    .dx-icd-family-overlay-close { background:none; border:none; color:#6b7280;
                        cursor:pointer; padding:0 4px; font-size:16px; line-height:1; margin-left:6px; }
                    .dx-icd-family-overlay-close:hover { color:#ef4444; }
                    .dx-icd-family-overlay-body { max-height:280px; overflow-y:auto; }
                    .dx-icd-family-overlay-item { display:flex; align-items:center; gap:8px;
                        padding:7px 12px; border-bottom:1px solid #f3f4f6; font-size:12px; }
                    .dx-icd-family-overlay-item:last-child { border-bottom:none; }
                    .dx-icd-family-overlay-item:hover { background:#f8f9fc; }
                `;
                document.head.appendChild(style);
            }

            // Inject overlay element into <body>
            const overlay = document.createElement('div');
            overlay.id = 'dxIcdFamilyOverlay';
            overlay.className = 'dx-icd-family-overlay';
            overlay.style.display = 'none';
            overlay.innerHTML = `
                <div class="dx-icd-family-overlay-header">
                    <i class="bi bi-diagram-2"></i>
                    <span id="dxIcdFamilyOverlayTitle">Family</span>
                    <span class="dxfo-count" id="dxIcdFamilyOverlayCount">0</span>
                    <button class="dx-icd-family-overlay-close" id="dxIcdFamilyOverlayCloseBtn" title="Close (Esc)">&times;</button>
                </div>
                <div class="dx-icd-family-overlay-body" id="dxIcdFamilyOverlayBody"></div>
            `;
            document.body.appendChild(overlay);

            // Wire × button — delegates to the currently-owning instance
            document.getElementById('dxIcdFamilyOverlayCloseBtn').addEventListener('click', () => {
                const owner = DxCptCodesComponent._familyOverlayOwner;
                if (owner) owner._closeFamilyOverlay();
            });

            // Esc key (page-level, once)
            document.addEventListener('keydown', (e) => {
                if (e.key === 'Escape') {
                    const owner = DxCptCodesComponent._familyOverlayOwner;
                    if (owner && owner.familyOverlayOpenForCode) owner._closeFamilyOverlay();
                }
            });

            // Window resize — close overlay (avoids stranding off-screen)
            window.addEventListener('resize', () => {
                const owner = DxCptCodesComponent._familyOverlayOwner;
                if (owner && owner.familyOverlayOpenForCode) owner._closeFamilyOverlay();
            });
        }

        _positionFamilyOverlay(pillEl) {
            const overlay = document.getElementById('dxIcdFamilyOverlay');
            if (!overlay || !pillEl) return;
            const rect = pillEl.getBoundingClientRect();
            overlay.style.top  = (rect.bottom + window.scrollY + 8) + 'px';
            overlay.style.left = (rect.left + window.scrollX - 12) + 'px';
        }

        async _toggleFamilyPanel(sourceCode, prefix, pillEl) {
            this._ensureFamilyOverlayExists();

            // Clicking the same pill twice closes the overlay
            if (this.familyOverlayOpenForCode === sourceCode) {
                this._closeFamilyOverlay();
                return;
            }

            // If a different instance had it open, that instance closes first
            const currentOwner = DxCptCodesComponent._familyOverlayOwner;
            if (currentOwner && currentOwner !== this) {
                currentOwner._closeFamilyOverlay();
            }

            // Claim ownership
            this.familyOverlayOpenForCode = sourceCode;
            DxCptCodesComponent._familyOverlayOwner = this;

            this._positionFamilyOverlay(pillEl);
            const overlay = document.getElementById('dxIcdFamilyOverlay');
            document.getElementById('dxIcdFamilyOverlayTitle').textContent = `${prefix}.x family (similar codes)`;
            document.getElementById('dxIcdFamilyOverlayCount').textContent = '...';
            document.getElementById('dxIcdFamilyOverlayBody').innerHTML =
                '<div class="text-center text-muted py-3"><div class="spinner-border spinner-border-sm me-1"></div>Loading...</div>';
            overlay.style.display = 'block';

            const cached = this.familyPanelCache[prefix];
            if (cached) {
                this._renderFamilyOverlayContent(sourceCode, prefix, cached);
                return;
            }

            try {
                const codes = await window.apiRequest(`/icd-codes/search?query=${encodeURIComponent(prefix)}&take=20`, { showLoader: false });
                this.familyPanelCache[prefix] = codes || [];
                if (this.familyOverlayOpenForCode === sourceCode) {
                    this._renderFamilyOverlayContent(sourceCode, prefix, this.familyPanelCache[prefix]);
                }
            } catch (e) {
                console.error('[DxCptCodes] family search error:', e);
                if (this.familyOverlayOpenForCode === sourceCode) {
                    this._renderFamilyOverlayContent(sourceCode, prefix, []);
                }
            }
        }

        _renderFamilyOverlayContent(sourceCode, prefix, codes) {
            const body = document.getElementById('dxIcdFamilyOverlayBody');
            const countEl = document.getElementById('dxIcdFamilyOverlayCount');
            if (!body) return;

            const existing = this.selectedIcds.map(c => c.code);
            const filtered = (codes || []).filter(c => {
                const code = c.Code || c.code;
                return code && code.toUpperCase().startsWith(prefix.toUpperCase()) && code !== sourceCode;
            });
            if (countEl) countEl.textContent = filtered.length;

            if (filtered.length === 0) {
                body.innerHTML = `<div class="text-muted small text-center py-3">No additional ${this._esc(prefix)}.x codes found in the database.</div>`;
                return;
            }
            body.innerHTML = filtered.map(c => {
                const code = c.Code || c.code;
                const desc = c.Description || c.description;
                const added = existing.includes(code);
                const starred = this._isIcdFavorite(code);
                return `<div class="dx-icd-family-overlay-item">
                    <span class="badge bg-primary">${this._esc(code)}</span>
                    <span class="flex-grow-1">${this._esc(desc)}</span>
                    <button class="dx-icd-family-star ${starred ? 'starred' : ''}" data-code="${this._esc(code)}" data-desc="${this._esc(desc)}"
                        title="${starred ? 'Remove from favorites' : 'Add to favorites'}"
                        style="background:none; border:none; padding:2px 4px; cursor:pointer; font-size:14px; line-height:1; color:${starred ? '#f59e0b' : '#d1d5db'};">
                        <i class="bi bi-star${starred ? '-fill' : ''}"></i>
                    </button>
                    ${added
                        ? '<span class="badge bg-success"><i class="bi bi-check-lg"></i></span>'
                        : `<button class="btn btn-outline-primary dx-icd-family-add" data-code="${this._esc(code)}" data-desc="${this._esc(desc)}" style="padding:2px 10px; font-size:11px;"><i class="bi bi-plus-lg me-1"></i>Add</button>`}
                </div>`;
            }).join('');
            body.querySelectorAll('.dx-icd-family-add').forEach(btn => {
                btn.addEventListener('click', () => this._addIcdToCart(btn.dataset.code, btn.dataset.desc, false));
            });
            // Star toggle inside the family overlay — does NOT close overlay, does NOT add code to visit.
            body.querySelectorAll('.dx-icd-family-star').forEach(btn => {
                btn.addEventListener('click', async (e) => {
                    e.stopPropagation();
                    await this._toggleIcdFavorite(btn.dataset.code, btn.dataset.desc);
                    const cached = this.familyPanelCache[prefix] || [];
                    this._renderFamilyOverlayContent(sourceCode, prefix, cached);
                    this._renderFavoritesCardOnly();
                });
            });
        }

        _closeFamilyOverlay() {
            const overlay = document.getElementById('dxIcdFamilyOverlay');
            if (overlay) overlay.style.display = 'none';
            this.familyOverlayOpenForCode = null;
            if (DxCptCodesComponent._familyOverlayOwner === this) {
                DxCptCodesComponent._familyOverlayOwner = null;
            }
        }

        // ====================================================================
        // UTILITIES
        // ====================================================================

        _esc(str) {
            if (str == null) return '';
            const div = document.createElement('div');
            div.textContent = str;
            return div.innerHTML;
        }
    }

    // Static state — one overlay per page, shared across all instances.
    DxCptCodesComponent._familyOverlayInjected = false;
    DxCptCodesComponent._familyOverlayOwner = null;

    // Export
    window.DxCptCodesComponent = DxCptCodesComponent;
})();
