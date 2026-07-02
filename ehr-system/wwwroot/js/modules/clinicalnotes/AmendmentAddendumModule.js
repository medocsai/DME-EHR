/* ============================================================
 * Amendment & Addendum — client module
 *
 * Exposes:
 *   - AmendmentAddendum.renderActionButtons(noteId, container) — draws the
 *     Amendment / Addendum buttons + countdown into a container, based on
 *     the server-side amendment-status for the signed note.
 *   - AmendmentAddendum.openAddendumModal(noteId)
 *   - AmendmentAddendum.goToAmendment(noteId, encounterId)
 *   - AmendmentAddendum.formatCountdown(windowEndsAt) — "23h 04m" / "47m"
 *   - AmendmentAddendum.renderHistoryPanel(container, history)
 *
 * All UI work routes through the server for authorization — the client never
 * decides whether a user can amend; it only renders what the server tells it.
 * ============================================================ */
(function () {
    'use strict';

    // apiRequest() prepends "/api" automatically, so we pass paths without it.
    const API = '/clinical-notes';

    async function fetchStatus(noteId) {
        try {
            return await window.apiRequest(`${API}/${noteId}/amendment-status`);
        } catch (e) {
            console.warn('[AmendmentAddendum] status fetch failed', e);
            return null;
        }
    }

    async function fetchHistory(noteId) {
        try {
            return await window.apiRequest(`${API}/${noteId}/history`);
        } catch (e) {
            console.warn('[AmendmentAddendum] history fetch failed', e);
            return null;
        }
    }

    async function fetchJson(url) {
        try { return await window.apiRequest(url); }
        catch (e) { console.warn('[AmendmentAddendum]', url, 'failed', e); return null; }
    }

    function parseJsonArray(s) {
        if (!s) return [];
        try { const x = JSON.parse(s); return Array.isArray(x) ? x : []; }
        catch { return []; }
    }

    function formatCountdown(endIso) {
        if (!endIso) return '';
        const end = new Date(endIso).getTime();
        const now = Date.now();
        const msLeft = end - now;
        if (msLeft <= 0) return 'Window closed';
        const mins = Math.floor(msLeft / 60000);
        const h = Math.floor(mins / 60);
        const m = mins % 60;
        if (h > 0) return `${h}h ${String(m).padStart(2, '0')}m`;
        return `${m}m`;
    }

    function isEnding(endIso) {
        if (!endIso) return false;
        const msLeft = new Date(endIso).getTime() - Date.now();
        return msLeft > 0 && msLeft < 60 * 60 * 1000;
    }

    // --------------------------------------------------
    // Render the Amendment / Addendum action buttons
    // into a container (e.g. the clinical note view
    // modal footer). Returns a promise; caller can await.
    // --------------------------------------------------
    async function renderActionButtons(noteId, container) {
        if (!container) return;
        const status = await fetchStatus(noteId);
        if (!status) return;

        // Do nothing if note is not signed — controller will have returned empty status
        if (!status.OriginalSignedAt && !status.originalSignedAt) return;

        // Property names can be PascalCase (ASP.NET default) — normalize.
        const s = normalize(status);

        // --------------------------------------------------
        // BACKDOOR BLOCK (added 2026-04-20)
        //
        // Scenario this guards against: a provider with an in-progress encounter
        // signs the note, then instead of closing the encounter walks out to the
        // Patient Profile (or Today's Appointments, etc.) and tries to amend the
        // note from there. The server would happily accept that amendment (they
        // are the original signer, encounter is open = Mode 1/2, valid data),
        // but clinically it's wrong — the provider should finish the encounter
        // in the workspace, not sidestep it.
        //
        // Rule: if encounter is In Progress (no CheckOutTime) AND the viewer is
        // outside the encounter workspace, hide the action buttons and show a
        // yellow "Encounter in progress" label instead. Inside the workspace,
        // behavior is unchanged (the Amendment / Addendum buttons still work).
        //
        // Detection: EncounterWorkspaceModule is instantiated on DOMContentLoaded
        // for every page in the app (it's a global script), so `window._encounterWorkspace`
        // is ALWAYS truthy — it cannot be used as an "am I in the workspace?" flag.
        // Instead we check for the encounter page's root element, which only exists
        // when the encounter view is actually rendered. This is the same test the
        // encounter module itself uses in its init() early-return.
        //
        // Security note: this is a UI convenience, not a server-side block. A
        // determined caller could still POST directly. We intentionally do NOT
        // block on the server — that would break legitimate inside-workspace
        // amendments. See rules/technical/amendment-addendum.md.
        // --------------------------------------------------
        const insideEncounterWorkspace = !!document.getElementById('encounterWorkspacePage');
        const encounterInProgress = !s.isEncounterClosed;  // In Progress = not checked out
        if (encounterInProgress && !insideEncounterWorkspace && (s.canAmend || s.canAddendum)) {
            const label = document.createElement('span');
            label.className = 'amendment-inprogress-label';
            label.innerHTML = `
                <i class="bi bi-hourglass-split me-1"></i>
                Encounter in progress. Finish it in the encounter workspace.
            `;
            container.appendChild(label);
            return;   // skip all button/countdown rendering below
        }

        // Countdown (only when encounter is closed and window is open)
        if (s.isAmendmentWindowOpen && s.amendmentWindowEndsAt) {
            const badge = document.createElement('span');
            badge.className = 'amendment-countdown' + (isEnding(s.amendmentWindowEndsAt) ? ' ending' : '');
            badge.dataset.windowEnd = s.amendmentWindowEndsAt;
            badge.innerHTML = `<i class="bi bi-clock-history"></i> <span class="countdown-text">${formatCountdown(s.amendmentWindowEndsAt)}</span>`;
            container.appendChild(badge);
            // Self-tick every 30s
            const tick = () => {
                const txt = formatCountdown(s.amendmentWindowEndsAt);
                const el = badge.querySelector('.countdown-text');
                if (el) el.textContent = txt;
                if (isEnding(s.amendmentWindowEndsAt)) badge.classList.add('ending');
                if (txt === 'Window closed') {
                    clearInterval(handle);
                    refreshButtons();
                }
            };
            const handle = setInterval(tick, 30000);
        }

        // Amendment button
        if (s.canAmend) {
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'btn btn-outline-warning';
            btn.innerHTML = '<i class="bi bi-pencil-square me-1"></i>Amendment';
            btn.addEventListener('click', () => goToAmendment(noteId));
            container.appendChild(btn);
        } else if (s.canAddendum && s.isEncounterClosed && !s.isAmendmentWindowOpen) {
            // Window expired — show disabled Amendment button with tooltip
            const wrap = document.createElement('span');
            wrap.className = 'amendment-disabled-wrap';
            wrap.innerHTML = `
                <span class="amendment-disabled-tip">Amendment window (${s.amendmentWindowHours} h) has passed. Use Addendum to add information.</span>
                <button type="button" class="btn btn-outline-warning" disabled style="opacity:0.55; cursor:not-allowed;">
                    <i class="bi bi-pencil-square me-1"></i>Amendment
                </button>
            `;
            container.appendChild(wrap);
        }

        // Addendum button
        if (s.canAddendum) {
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'btn btn-outline-primary';
            btn.innerHTML = '<i class="bi bi-plus-circle me-1"></i>Addendum';
            btn.addEventListener('click', () => openAddendumModal(noteId));
            container.appendChild(btn);
        }

        async function refreshButtons() {
            // Remove children we added and re-render
            container.querySelectorAll('.amendment-countdown, .amendment-disabled-wrap').forEach(n => n.remove());
            // Also remove our Amendment/Addendum buttons
            [...container.querySelectorAll('button')].forEach(b => {
                const t = b.textContent.trim();
                if (t === 'Amendment' || t === 'Addendum') b.remove();
            });
            await renderActionButtons(noteId, container);
        }
    }

    // --------------------------------------------------
    // Addendum modal (self-contained, creates its own element)
    // --------------------------------------------------
    function openAddendumModal(noteId) {
        // Remove any existing
        document.getElementById('addendumModalDynamic')?.remove();

        const modalHtml = `
            <div class="modal fade" id="addendumModalDynamic" tabindex="-1" aria-hidden="true">
                <div class="modal-dialog modal-lg">
                    <div class="modal-content">
                        <div class="modal-header">
                            <h5 class="modal-title"><i class="bi bi-plus-circle text-primary me-2"></i>Add Addendum</h5>
                            <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
                        </div>
                        <div class="modal-body">
                            <div class="alert alert-light border py-2" style="font-size: 12px;">
                                <i class="bi bi-info-circle me-1"></i>
                                Addendum is supplementary information added to the signed note. The original note is not modified. No impact on codes or claim.
                            </div>
                            <div class="mb-3">
                                <label class="form-label">Reason for Addendum <span class="text-danger">*</span></label>
                                <input type="text" class="form-control" id="addendumReason" maxlength="500" placeholder="e.g., Lab results received after note was signed">
                            </div>
                            <div class="mb-2">
                                <label class="form-label">Addendum Content <span class="text-danger">*</span></label>
                                <textarea class="form-control" id="addendumContent" rows="6" style="min-height: 160px;"></textarea>
                            </div>
                            <div id="addendumError" class="text-danger small mt-2" style="display:none;"></div>
                        </div>
                        <div class="modal-footer">
                            <button type="button" class="btn btn-secondary" data-bs-dismiss="modal">Cancel</button>
                            <button type="button" class="btn btn-primary" id="addendumSubmitBtn"><i class="bi bi-check-lg me-1"></i>Sign &amp; Save Addendum</button>
                        </div>
                    </div>
                </div>
            </div>`;
        document.body.insertAdjacentHTML('beforeend', modalHtml);
        const modalEl = document.getElementById('addendumModalDynamic');
        // Stack above any modal already open (Patient Details, Tabbed Note, etc.)
        modalEl.addEventListener('show.bs.modal', () => {
            if (typeof window.bumpModalZIndexIfStacked === 'function') window.bumpModalZIndexIfStacked(modalEl);
        });
        const modal = new bootstrap.Modal(modalEl);
        modal.show();

        document.getElementById('addendumSubmitBtn').addEventListener('click', async () => {
            const reason = document.getElementById('addendumReason').value.trim();
            const content = document.getElementById('addendumContent').value.trim();
            const errEl = document.getElementById('addendumError');
            errEl.style.display = 'none';

            if (!reason) { errEl.textContent = 'Reason is required.'; errEl.style.display = 'block'; return; }
            if (!content) { errEl.textContent = 'Content is required.'; errEl.style.display = 'block'; return; }

            // Reuse the existing signature capture modal (if available) or fall back
            const signature = await captureSignature();
            if (!signature) { errEl.textContent = 'Signature is required.'; errEl.style.display = 'block'; return; }

            try {
                await window.apiRequest(`${API}/${noteId}/addendum`, {
                    method: 'POST',
                    body: {
                        Content: content,
                        Reason: reason,
                        SignatureData: signature
                    }
                });
                modal.hide();
                if (window.showToast) window.showToast('Success', 'Addendum saved.', 'success');
                // Refresh the note view if it's open
                if (typeof window.viewClinicalNote === 'function') {
                    window.viewClinicalNote(noteId);
                }
            } catch (e) {
                const msg = e?.responseJSON?.error || e?.message || 'Failed to save addendum.';
                errEl.textContent = msg;
                errEl.style.display = 'block';
            }
        });

        modalEl.addEventListener('hidden.bs.modal', () => {
            modalEl.remove();
            // Stacked-modal cleanup: since addendum was opened above a parent
            // modal (Patient Details / Tabbed Note), Bootstrap can leave our
            // bumped-z backdrop behind as an orphan. Trim any excess backdrops.
            if (typeof window.cleanupModalBackdrops === 'function') window.cleanupModalBackdrops();
        });
    }

    // --------------------------------------------------
    // Open the Amendment modal.
    //
    // Replaces the old /clinical-notes/amendment/{id} page (deleted 2026-04-20).
    // The modal stays within whatever page the provider is on — encounter
    // workspace, patient profile, today's appointments — so context is not lost.
    //
    // UX by mode (determined server-side via /amendment-status):
    //   Mode 1 (encounter open, no codes)  → Reason + Trumbowyg editor + Sign
    //   Mode 2 (encounter open, codes exist) → same as Mode 1, then after Sign
    //                                          a follow-up "Review codes?" modal
    //                                          (soft prompt, skippable)
    //   Mode 3 (encounter closed, window open) → Reason + Editor + Codes section
    //                                            (DxCptCodesComponent mounted
    //                                             with autoSave=false) + Sign.
    //                                            Sign commits note + codes in
    //                                            one atomic server transaction.
    //
    // If any tabbed note modal is open when we're called, we close it first —
    // avoids Bootstrap modal stacking / backdrop tangles. After the amendment
    // saves or the user cancels, we do NOT auto-reopen it.
    // --------------------------------------------------
    async function goToAmendment(noteId) {
        // Close the tabbed view modal if open
        const tabbedModal = document.getElementById('tabbedClinicalNoteModal');
        if (tabbedModal && tabbedModal.classList.contains('show')) {
            bootstrap.Modal.getInstance(tabbedModal)?.hide();
            await new Promise(r => setTimeout(r, 250));
            if (typeof window.cleanupModalBackdrops === 'function') window.cleanupModalBackdrops();
        }
        await openAmendmentModal(noteId);
    }

    async function openAmendmentModal(noteId) {
        // Remove any stale instance
        document.getElementById('amendmentModalDynamic')?.remove();

        // Fetch status + history in parallel
        const [status, history] = await Promise.all([
            fetchStatus(noteId),
            fetchHistory(noteId)
        ]);
        if (!status) {
            if (window.showToast) window.showToast('Failed to load amendment status.', 'danger');
            return;
        }
        const s = normalize(status);
        if (!s.canAmend) {
            const msg = (s.isEncounterClosed && !s.isAmendmentWindowOpen)
                ? `The amendment window (${s.amendmentWindowHours}h) has passed. Use Addendum to add information.`
                : 'Only the original signing clinician can amend this note.';
            if (window.showToast) window.showToast(msg, 'warning');
            return;
        }

        // Current content (latest version) for pre-filling the editor
        const currentContent = history?.CurrentHtmlContent || history?.currentHtmlContent || '';

        // For Mode 3, fetch the encounter's current code state so the picker starts
        // with whatever's already there (provider may confirm or adjust).
        let initialIcds = [];
        let initialCpts = [];
        let appointmentId = null;
        let encounterId = null;
        let patientId = null;
        if (s.amendmentMode === 3) {
            const note = await fetchJson(`/clinical-notes/${noteId}`);
            appointmentId = note?.AppointmentId ?? note?.appointmentId ?? null;
            encounterId = note?.EncounterId ?? note?.encounterId ?? null;
            patientId = note?.PatientId ?? note?.patientId ?? null;

            // Resolve encounter. Preferred path: direct lookup by encounterId.
            // Fallback: some historical notes were created without an EncounterId
            // link even though an encounter exists for the same appointment.
            // In that case, look it up via /patients/{patientId}/encounters/by-appointment/{appointmentId}.
            let enc = null;
            if (encounterId) {
                enc = await fetchJson(`/encounters/${encounterId}`);
            }
            if (!enc && appointmentId && patientId) {
                enc = await fetchJson(`/patients/${patientId}/encounters/by-appointment/${appointmentId}`);
                if (enc) encounterId = enc.EncounterId || enc.encounterId || null;
            }

            if (enc) {
                initialIcds = parseJsonArray(enc.IcdSelections || enc.icdSelections);
                initialCpts = parseJsonArray(enc.CptSelections || enc.cptSelections);
            }
        }

        // Build modal markup.
        //
        // Mode 1/2 (encounter open):   single step — Reason + Editor + Sign. No codes here.
        // Mode 3  (encounter closed):  TWO-STEP WIZARD —
        //     Step 1: Reason + Editor  → [Next: Review Codes →]
        //     Step 2: Dx & CPT Codes   → [← Back]  [Sign Amendment]
        //   Both sections live in the same modal body; switching steps is pure DOM
        //   show/hide so editor + code-picker state is preserved across Back/Next.
        const isMode3 = s.amendmentMode === 3;
        const nextVersion = (s.currentVersionNumber || 1) + 1;
        const stepIndicatorHtml = isMode3 ? `
            <div class="d-flex align-items-center gap-2 mb-3" id="amendmentSteps" style="font-size: 12px; color: #6b7280; font-weight: 600;">
                <span class="step-pill" data-step="1">
                    <span class="step-num">1</span> Edit Note
                </span>
                <i class="bi bi-chevron-right" style="font-size: 11px;"></i>
                <span class="step-pill" data-step="2">
                    <span class="step-num">2</span> Review Codes
                </span>
            </div>
        ` : '';

        const modalHtml = `
            <div class="modal fade" id="amendmentModalDynamic" tabindex="-1" aria-hidden="true" data-bs-backdrop="static">
                <div class="modal-dialog modal-xl modal-dialog-scrollable">
                    <div class="modal-content">
                        <div class="modal-header">
                            <div>
                                <h5 class="modal-title mb-0">
                                    <i class="bi bi-pencil-square text-warning me-2"></i>Amend Clinical Note
                                </h5>
                                <div class="text-muted" style="font-size: 12px; margin-top: 2px;">
                                    Creating version ${nextVersion} from v${s.currentVersionNumber || 1}
                                </div>
                            </div>
                            <button type="button" class="btn-close" id="amendmentCancelX"></button>
                        </div>
                        <div class="modal-body">
                            ${stepIndicatorHtml}

                            <div class="alert alert-light border py-2 mb-3" style="font-size: 12px; line-height: 1.55;">
                                <div>
                                    <i class="bi bi-info-circle me-1 text-primary"></i>
                                    Editing the note creates a new signed version. The original stays preserved, read-only.
                                </div>
                                ${isMode3 ? `
                                    <div class="mt-1">
                                        <span class="badge bg-warning text-dark me-1">Post-close</span>
                                        Reviewing codes is required. Signing saves the note and the codes together in a single atomic step.
                                    </div>
                                ` : `
                                    <div class="mt-1">
                                        <span class="badge bg-secondary me-1">Pre-close</span>
                                        Codes step is skipped &mdash; you'll handle codes in the encounter flow.
                                    </div>
                                `}
                            </div>

                            <!-- STEP 1 — Reason + Editor (always the first step for every mode) -->
                            <div id="amendStep1">
                                <div class="mb-3">
                                    <label class="form-label">Reason for Amendment <span class="text-danger">*</span></label>
                                    <input type="text" class="form-control" id="amendReason" maxlength="500"
                                        placeholder="e.g., Corrected Metformin dose from 500mg to 1000mg BID">
                                </div>

                                <label class="form-label">Amended Note Content <span class="text-danger">*</span></label>
                                <div class="mb-3"><textarea id="amendEditor">${currentContent}</textarea></div>
                            </div>

                            ${isMode3 ? `
                                <!-- STEP 2 — Dx & CPT Codes (Mode 3 only, hidden until Next is clicked) -->
                                <div id="amendStep2" style="display:none;">
                                    <h6 class="mb-2"><i class="bi bi-clipboard2-pulse me-1 text-primary"></i>Dx &amp; CPT Codes <span class="text-danger">*</span></h6>
                                    <p class="text-muted small mb-3">
                                        Review or adjust the codes for this encounter. At least one CPT is required.
                                        When you Sign Amendment, the note and the codes are saved atomically and the claim's
                                        charges are updated in the same transaction.
                                    </p>
                                    <div id="amendmentCodesContainer"></div>
                                </div>
                            ` : ''}

                            <div id="amendmentError" class="text-danger small mt-3" style="display:none;"></div>
                        </div>
                        <div class="modal-footer" id="amendmentFooter">
                            <!-- Footer buttons vary by mode + current step — rendered by updateFooter() below -->
                        </div>
                    </div>
                </div>
            </div>`;
        document.body.insertAdjacentHTML('beforeend', modalHtml);
        const modalEl = document.getElementById('amendmentModalDynamic');
        // Stack above any modal already open (Patient Details, Tabbed Note, etc.)
        modalEl.addEventListener('show.bs.modal', () => {
            if (typeof window.bumpModalZIndexIfStacked === 'function') window.bumpModalZIndexIfStacked(modalEl);
        });
        const modal = new bootstrap.Modal(modalEl);
        modal.show();

        // Init Trumbowyg inside the modal
        let editorInitialized = false;
        if (window.$ && typeof $.fn.trumbowyg === 'function') {
            try {
                $('#amendEditor').trumbowyg({
                    btns: [
                        ['viewHTML'], ['undo', 'redo'], ['formatting'],
                        ['strong', 'em'], ['unorderedList', 'orderedList'],
                        ['link'], ['fullscreen']
                    ],
                    autogrow: true,
                    semantic: true,
                    removeformatPasted: true
                });
                editorInitialized = true;
            } catch (e) {
                console.warn('[AmendmentAddendum] Trumbowyg init failed; falling back to plain textarea', e);
            }
        }

        // DxCptCodesComponent is mounted for Mode 3 only, in the BACKGROUND — we
        // deliberately don't await its mount() because loading favorites + AI
        // suggestions can take 1–2 seconds and that blocks the footer (and the
        // Next button) from rendering. Mounting it fire-and-forget lets the user
        // see the modal + footer immediately and start typing Step 1 while the
        // codes screen is loading behind the scenes. By the time they click Next,
        // the mount is almost always complete. Sign-time validation still runs
        // even if it isn't — the component's getCurrentSelections() is safe.
        let codesComponent = null;
        if (isMode3) {
            const container = document.getElementById('amendmentCodesContainer');
            if (container && window.DxCptCodesComponent) {
                codesComponent = new window.DxCptCodesComponent({
                    container,
                    appointmentId,
                    encounterId,
                    patientId,
                    initialIcds,
                    initialCpts,
                    autoSave: false,   // atomic save at amendment sign — see note above
                    maxIcdCodes: 12,
                    // Route AI requests through the from-text endpoints so the
                    // AI sees the PROVIDER'S EDITS, not the old saved note. The
                    // callback reads the current Trumbowyg content on demand.
                    aiSource: 'text',
                    getNoteTextForAi: () => readEditorHtml()
                });
                // Fire and forget — errors are logged by the component itself
                codesComponent.mount().catch(e => console.warn('[AmendmentAddendum] codes mount failed', e));
            }
        }

        // --- Cleanup on close ---
        const cleanup = () => {
            if (codesComponent) {
                try { codesComponent.destroy(); } catch (e) { /* noop */ }
                codesComponent = null;
            }
            if (editorInitialized && window.$) {
                try { $('#amendEditor').trumbowyg('destroy'); } catch (e) { /* noop */ }
            }
            modalEl.remove();
            // Stacked-modal cleanup: this modal was opened above a parent
            // modal, Bootstrap can leave our bumped-z backdrop as an orphan.
            if (typeof window.cleanupModalBackdrops === 'function') window.cleanupModalBackdrops();
        };
        modalEl.addEventListener('hidden.bs.modal', cleanup);

        // --- Wizard state + step switching (Mode 3 only) ---
        // currentStep: 1 = Reason+Editor, 2 = Codes (Mode 3 only). For Mode 1/2 we
        // stay on step 1 forever; the footer renders a direct Sign button.
        let currentStep = 1;

        const step1El = document.getElementById('amendStep1');
        const step2El = document.getElementById('amendStep2');
        const stepsIndicatorEl = document.getElementById('amendmentSteps');
        const footerEl = document.getElementById('amendmentFooter');

        function setStep(n) {
            currentStep = n;
            if (step1El) step1El.style.display = (n === 1) ? '' : 'none';
            if (step2El) step2El.style.display = (n === 2) ? '' : 'none';
            // Update step indicator highlight (Mode 3 only)
            if (stepsIndicatorEl) {
                stepsIndicatorEl.querySelectorAll('.step-pill').forEach(p => {
                    const num = parseInt(p.getAttribute('data-step'), 10);
                    if (num === n) {
                        p.style.color = '#4338ca';
                        const nbox = p.querySelector('.step-num');
                        if (nbox) { nbox.style.background = '#6366F1'; nbox.style.color = '#fff'; }
                    } else {
                        p.style.color = '#6b7280';
                        const nbox = p.querySelector('.step-num');
                        if (nbox) { nbox.style.background = '#e5e7eb'; nbox.style.color = '#6b7280'; }
                    }
                });
            }
            renderFooter();
        }

        function renderFooter() {
            if (!footerEl) return;
            // Build left-side Cancel + right-side primary action(s) based on mode + step.
            if (!isMode3) {
                // Mode 1/2 — single step, direct Sign.
                footerEl.innerHTML = `
                    <button type="button" class="btn btn-secondary" id="amendmentCancelBtn">Cancel</button>
                    <button type="button" class="btn btn-warning ms-auto" id="amendmentSignBtn">
                        <i class="bi bi-check-lg me-1"></i>Sign Amendment
                    </button>`;
            } else if (currentStep === 1) {
                // Mode 3 — Step 1: Cancel + Next
                footerEl.innerHTML = `
                    <button type="button" class="btn btn-secondary" id="amendmentCancelBtn">Cancel</button>
                    <button type="button" class="btn btn-primary ms-auto" id="amendmentNextBtn">
                        Next: Review Codes <i class="bi bi-arrow-right ms-1"></i>
                    </button>`;
            } else {
                // Mode 3 — Step 2: Back + Cancel + Sign
                footerEl.innerHTML = `
                    <button type="button" class="btn btn-outline-secondary" id="amendmentBackBtn">
                        <i class="bi bi-arrow-left me-1"></i>Back
                    </button>
                    <button type="button" class="btn btn-secondary" id="amendmentCancelBtn">Cancel</button>
                    <button type="button" class="btn btn-warning ms-auto" id="amendmentSignBtn">
                        <i class="bi bi-check-lg me-1"></i>Sign Amendment
                    </button>`;
            }
            // Wire handlers on the freshly-rendered buttons
            const cancelBtn = document.getElementById('amendmentCancelBtn');
            if (cancelBtn) cancelBtn.addEventListener('click', confirmCancel);
            const nextBtn = document.getElementById('amendmentNextBtn');
            if (nextBtn) nextBtn.addEventListener('click', handleNext);
            const backBtn = document.getElementById('amendmentBackBtn');
            if (backBtn) backBtn.addEventListener('click', () => setStep(1));
            const signBtn = document.getElementById('amendmentSignBtn');
            if (signBtn) signBtn.addEventListener('click', handleSign);
        }

        // --- Cancel handler (X + footer Cancel). Uses app's Bootstrap ConfirmDialog. ---
        const confirmCancel = async () => {
            if (window.ConfirmDialog && typeof window.ConfirmDialog.show === 'function') {
                const ok = await window.ConfirmDialog.show({
                    title: 'Discard amendment?',
                    message: 'Your changes will be lost. This cannot be undone.',
                    confirmText: 'Discard',
                    cancelText: 'Keep editing',
                    confirmClass: 'btn-danger'
                });
                if (ok) modal.hide();
            } else {
                // Last-resort fallback
                if (confirm('Discard amendment? Your changes will be lost.')) modal.hide();
            }
        };
        document.getElementById('amendmentCancelX').addEventListener('click', confirmCancel);

        // --- Step 1 → Step 2 (Mode 3 only). Validates Reason + Editor before moving on. ---
        //
        // Also triggers a fresh AI suggestion fetch using the AMENDED note text.
        // The codes component was first mounted when the modal opened, so its
        // current AI suggestions reflect the note content at modal-open time.
        // Once the provider has edited the note in Step 1, those suggestions are
        // stale. Calling refreshAiSuggestions() re-hits the from-text endpoints
        // with the latest editor content (via the getNoteTextForAi callback we
        // passed in at construction).
        async function handleNext() {
            const errEl = document.getElementById('amendmentError');
            errEl.style.display = 'none';
            const reason = document.getElementById('amendReason').value.trim();
            const htmlContent = readEditorHtml();
            if (!reason) { errEl.textContent = 'Reason is required.'; errEl.style.display = 'block'; return; }
            if (!htmlContent || !htmlContent.replace(/<[^>]+>/g, '').trim()) {
                errEl.textContent = 'Amended note content is required.';
                errEl.style.display = 'block';
                return;
            }
            // Show Step 2 first so the user sees the codes screen immediately,
            // then refresh suggestions in the background. The component shows
            // its own spinner while the fetch is in flight.
            setStep(2);
            if (codesComponent && typeof codesComponent.refreshAiSuggestions === 'function') {
                codesComponent.refreshAiSuggestions().catch(e =>
                    console.warn('[AmendmentAddendum] AI refresh failed', e));
            }
        }

        function readEditorHtml() {
            if (editorInitialized && window.$) {
                try { return $('#amendEditor').trumbowyg('html'); }
                catch { return document.getElementById('amendEditor')?.value || ''; }
            }
            return document.getElementById('amendEditor')?.value || '';
        }

        // --- Sign handler (final step of every mode) ---
        // Wrapped in a try/catch so no failure paths are silent.
        // Diagnostic logs tag "[AmendmentAddendum]" so they're easy to filter.
        // On validation or server error, the error element is made visible AND
        // scrolled into view (it lives at the bottom of the modal body and can
        // be below the fold on small viewports).
        async function handleSign() {
            console.log('[AmendmentAddendum] Sign click — starting', { noteId, mode: s.amendmentMode, isMode3 });
            const errEl = document.getElementById('amendmentError');
            const signBtn = document.getElementById('amendmentSignBtn');
            const showErr = (msg) => {
                errEl.textContent = msg;
                errEl.style.display = 'block';
                try { errEl.scrollIntoView({ behavior: 'smooth', block: 'center' }); } catch {}
            };
            errEl.style.display = 'none';

            try {
                const reason = (document.getElementById('amendReason')?.value || '').trim();
                const htmlContent = readEditorHtml();

                if (!reason) { showErr('Reason is required.'); return; }
                if (!htmlContent || !htmlContent.replace(/<[^>]+>/g, '').trim()) {
                    showErr('Amended note content is required.');
                    return;
                }

                const signature = await captureSignature();
                if (!signature) { showErr('Signature is required.'); return; }

                const body = { HtmlContent: htmlContent, Reason: reason, SignatureData: signature };
                if (isMode3 && codesComponent) {
                    const sel = codesComponent.getCurrentSelections();
                    if (!sel.cpts || sel.cpts.length === 0) {
                        showErr('At least one CPT code is required.');
                        return;
                    }
                    body.CptSelections = sel.cpts;
                    // Send full ICD objects — the server matches this to IcdSelectionItem
                    // and stores them in Encounter.IcdSelections in the same shape the
                    // encounter's Dx & CPT step writes. Round-trip survives, so the
                    // selected-codes cart pre-fills correctly on the next amendment.
                    body.IcdSelections = (sel.icds || []).map(i => ({
                        Code: i.code || (typeof i === 'string' ? i : ''),
                        Description: i.description || '',
                        AiSuggested: !!i.aiSuggested
                    }));
                }

                // Disable the button while submitting so the user can't double-click
                const originalBtnHtml = signBtn ? signBtn.innerHTML : '';
                if (signBtn) { signBtn.disabled = true; signBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Signing…'; }

                try {
                    console.log('[AmendmentAddendum] POST amendment', { noteId, mode: s.amendmentMode, bodyPreview: { Reason: body.Reason, contentLen: (body.HtmlContent||'').length, cpts: body.CptSelections?.length, icds: body.IcdSelections?.length } });
                    const resp = await window.apiRequest(`${API}/${noteId}/amendment`, { method: 'POST', body });
                    console.log('[AmendmentAddendum] Amendment saved', resp);

                    modal.hide();

                    setTimeout(() => {
                        if (typeof window.cleanupModalBackdrops === 'function') window.cleanupModalBackdrops();
                    }, 350);

                    // Success feedback — toast if helper available, otherwise a plain inline banner
                    // attached to the page so the provider sees confirmation.
                    if (window.showToast) {
                        window.showToast('Success', 'Amendment saved.', 'success');
                    } else {
                        showInlineSuccessBanner(`Amendment saved (v${resp?.versionNumber ?? '?'}).`);
                    }

                    if (s.amendmentMode === 2) {
                        setTimeout(() => showPostAmendmentCodesPrompt(noteId), 400);
                    }
                } catch (apiErr) {
                    console.error('[AmendmentAddendum] POST amendment failed', apiErr);
                    const msg = apiErr?.responseJSON?.error || apiErr?.responseJSON?.message || apiErr?.message || 'Failed to save amendment.';
                    showErr(msg);
                    if (signBtn) { signBtn.disabled = false; signBtn.innerHTML = originalBtnHtml; }
                }
            } catch (outerErr) {
                // Catch-all: if anything threw before the try/catch above (e.g. a DOM
                // lookup returning null), surface it instead of silently failing.
                console.error('[AmendmentAddendum] Sign handler threw', outerErr);
                showErr('Something went wrong. ' + (outerErr?.message || ''));
            }
        }

        // Lightweight fallback if the app's showToast helper isn't loaded on the
        // current page. Creates a self-dismissing toast-like banner at top-right.
        function showInlineSuccessBanner(message) {
            const div = document.createElement('div');
            div.style.cssText = 'position:fixed; top:20px; right:20px; z-index:1090; background:#10b981; color:#fff; padding:12px 18px; border-radius:8px; box-shadow:0 6px 20px rgba(0,0,0,0.15); font-size:13px; font-weight:600; display:flex; align-items:center; gap:8px;';
            div.innerHTML = `<i class="bi bi-check-circle-fill"></i>${message}`;
            document.body.appendChild(div);
            setTimeout(() => { div.style.transition = 'opacity 0.3s'; div.style.opacity = '0'; }, 2500);
            setTimeout(() => div.remove(), 3000);
        }

        // Initial render — step 1 visible, footer buttons for the current mode
        setStep(1);
    }

    // --------------------------------------------------
    // Mode 2 "Review codes?" soft modal — fires after a successful
    // pre-close amendment when codes already exist on the encounter.
    // Non-blocking: Skip or jump to the encounter's Dx & CPT step.
    // --------------------------------------------------
    function showPostAmendmentCodesPrompt(noteId) {
        document.getElementById('amendmentCodesPromptDynamic')?.remove();
        const html = `
            <div class="modal fade" id="amendmentCodesPromptDynamic" tabindex="-1" aria-hidden="true">
                <div class="modal-dialog">
                    <div class="modal-content">
                        <div class="modal-header">
                            <h5 class="modal-title"><i class="bi bi-clipboard2-pulse text-primary me-2"></i>Review codes?</h5>
                            <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
                        </div>
                        <div class="modal-body">
                            <p class="mb-0" style="font-size: 13px;">
                                Your amendment was saved. This encounter already has codes selected.
                                You may want to review them based on the updated note content.
                            </p>
                        </div>
                        <div class="modal-footer">
                            <button type="button" class="btn btn-outline-secondary" data-bs-dismiss="modal">
                                Skip, keep current codes
                            </button>
                            <button type="button" class="btn btn-primary" id="amendmentCodesPromptGoBtn">
                                <i class="bi bi-arrow-right-circle me-1"></i>Go to Codes step
                            </button>
                        </div>
                    </div>
                </div>
            </div>`;
        document.body.insertAdjacentHTML('beforeend', html);
        const el = document.getElementById('amendmentCodesPromptDynamic');
        // Stack above any modal already open (Patient Details, Tabbed Note, etc.)
        el.addEventListener('show.bs.modal', () => {
            if (typeof window.bumpModalZIndexIfStacked === 'function') window.bumpModalZIndexIfStacked(el);
        });
        const m = new bootstrap.Modal(el);
        m.show();
        el.addEventListener('hidden.bs.modal', () => {
            el.remove();
            // Stacked-modal cleanup: trim any orphan backdrops left behind.
            if (typeof window.cleanupModalBackdrops === 'function') window.cleanupModalBackdrops();
        });

        document.getElementById('amendmentCodesPromptGoBtn').addEventListener('click', async () => {
            console.log('[AmendmentAddendum] "Go to Codes step" clicked');

            // Detect context BEFORE hiding the modal (safer)
            const onEncounterPage = !!document.getElementById('encounterWorkspacePage');
            const ws = window._encounterWorkspace;
            console.log('[AmendmentAddendum] context', { onEncounterPage, hasWorkspace: !!ws, hasShowStep: typeof ws?._showStep });

            // FAST PATH — already on the encounter workspace.
            // Don't fetch anything; just hide the modal, wait for Bootstrap to
            // finish the hide animation (hidden.bs.modal event), then jump to
            // Dx & CPT Codes (step index 6). This replaces the earlier
            // `window.location.href = same-page` approach which is a no-op.
            if (onEncounterPage && ws && typeof ws._showStep === 'function') {
                el.addEventListener('hidden.bs.modal', () => {
                    try {
                        console.log('[AmendmentAddendum] jumping to step 6 (Dx & CPT)');
                        ws._showStep(6);
                    } catch (e) {
                        console.warn('[AmendmentAddendum] _showStep failed', e);
                    }
                }, { once: true });
                m.hide();
                return;
            }

            // SLOW PATH — not on encounter page, need to navigate.
            m.hide();
            const note = await fetchJson(`/clinical-notes/${noteId}`);
            const encId = note?.EncounterId ?? note?.encounterId;
            if (encId) {
                window.location.href = `/Encounter/${encId}`;
            } else {
                console.warn('[AmendmentAddendum] could not resolve encounterId for note', noteId);
            }
        });
    }

    // --------------------------------------------------
    // Signature capture helper
    // Reuses localStorage signature if present; otherwise prompts.
    // --------------------------------------------------
    async function captureSignature() {
        // Simplest reliable approach: reuse an existing signature from the user profile
        // stored during login. If absent, synthesize a typed-name signature.
        try {
            const cu = JSON.parse(localStorage.getItem('currentUser') || '{}');
            if (cu && cu.SignatureData) return cu.SignatureData;
            const typedName = `${cu.FirstName || ''} ${cu.LastName || ''}`.trim() || 'Clinician';
            // Generate a simple SVG signature as a data URI fallback
            const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="300" height="60"><text x="10" y="40" font-family="Brush Script MT, cursive" font-size="28" fill="#1f2937">${typedName}</text></svg>`;
            return 'data:image/svg+xml;base64,' + btoa(svg);
        } catch {
            return 'data:image/svg+xml;base64,' + btoa('<svg xmlns="http://www.w3.org/2000/svg"/>');
        }
    }

    // --------------------------------------------------
    // Timeline panel rendering (right sidebar of the note view)
    //
    // Replaces the older linear "Version History" list (removed 2026-04-20).
    // Renders newest-first, using the click-to-swap (versions) or
    // click-to-scroll (addendums) interactions. The consumer is responsible
    // for wiring click behaviours by passing a controller object with:
    //
    //   controller.onVersionClick(versionNumber, isOriginal, isCurrent) => void
    //   controller.onAddendumClick(addendumId) => void
    //   controller.currentVersionNumber       // used for "Current" pill marker
    //   controller.viewingVersionNumber       // used for "Viewing" pill marker
    //                                         // (null = viewing an addendum jump, treat as current)
    // --------------------------------------------------
    function renderTimelinePanel(container, history, controller) {
        if (!container || !history) return;
        const versions = history.Versions || history.versions || [];
        const addendums = history.Addendums || history.addendums || [];
        const ctrl = controller || {};

        const esc = (t) => (t ?? '').toString().replace(/[&<>]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;'}[c]));
        const fmtDate = (iso) => {
            if (!iso) return '';
            try { return new Date(iso).toLocaleString([], { month: '2-digit', day: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' }); }
            catch { return ''; }
        };

        // Build a combined, descending-chronological list of events
        // (addendum timestamps and amendment sign times mix into one timeline).
        const events = [];
        versions.forEach(v => {
            events.push({
                type: 'version',
                versionNumber: v.VersionNumber || v.versionNumber,
                signedAt: v.SignedAt || v.signedAt,
                signedByName: v.SignedByName || v.signedByName || '',
                reason: v.Reason || v.reason || null,
                isOriginal: !!(v.IsOriginal || v.isOriginal),
                isCurrent: !!(v.IsCurrent || v.isCurrent),
            });
        });
        addendums.forEach(a => {
            events.push({
                type: 'addendum',
                addendumId: a.AddendumId || a.addendumId,
                signedAt: a.SignedAt || a.signedAt || a.CreatedAt || a.createdAt,
                signedByName: a.SignedByName || a.signedByName || '',
                reason: a.Reason || a.reason || '',
            });
        });
        // Sort descending by timestamp
        events.sort((a, b) => new Date(b.signedAt).getTime() - new Date(a.signedAt).getTime());

        const currentVN = ctrl.currentVersionNumber;
        const viewingVN = (typeof ctrl.viewingVersionNumber === 'number') ? ctrl.viewingVersionNumber : currentVN;

        let cardsHtml = '';
        events.forEach(ev => {
            if (ev.type === 'version') {
                const vn = ev.versionNumber;
                const cardClass = ev.isOriginal ? 'original-card' : 'amendment-card';
                const pillCls = ev.isOriginal ? 'original' : 'amendment';
                const pillTxt = ev.isOriginal ? `v${vn} · Original` : `v${vn} · Amendment`;
                const isCur = ev.isCurrent || (vn === currentVN);
                const isViewing = (vn === viewingVN);
                const activeCls = isViewing ? ' active' : '';
                cardsHtml += `
                    <div class="tl-card ${cardClass}${activeCls}" data-version="${vn}">
                        <div class="tl-card-head">
                            <div class="row1">
                                <span class="pill ${pillCls}">${esc(pillTxt)}</span>
                                ${isCur ? '<span class="pill current-marker">Current</span>' : ''}
                                ${isViewing ? '<span class="pill viewing-marker"><i class="bi bi-eye-fill me-1"></i>Viewing</span>' : ''}
                            </div>
                            <div class="event-date">${esc(fmtDate(ev.signedAt))}</div>
                            <div class="signer"><i class="bi bi-person-circle"></i>${esc(ev.signedByName)}</div>
                            ${ev.reason ? `<div class="reason-line"><strong>Reason:</strong> ${esc(ev.reason)}</div>` : ''}
                        </div>
                    </div>`;
            } else {
                // addendum card
                cardsHtml += `
                    <div class="tl-card addendum-card" data-addendum="${ev.addendumId}" title="Click to jump to this addendum">
                        <div class="tl-card-head">
                            <div class="row1">
                                <span class="pill addendum"><i class="bi bi-plus-circle me-1"></i>Addendum</span>
                            </div>
                            <div class="event-date">${esc(fmtDate(ev.signedAt))}</div>
                            <div class="signer"><i class="bi bi-person-circle"></i>${esc(ev.signedByName)}</div>
                            ${ev.reason ? `<div class="reason-line"><strong>Reason:</strong> ${esc(ev.reason)}</div>` : ''}
                            <div class="jump-hint"><i class="bi bi-arrow-down-circle"></i>Click to jump to this addendum</div>
                        </div>
                    </div>`;
            }
        });

        const emptyNoticeHtml = (addendums.length === 0 && versions.length <= 1)
            ? `<div class="timeline-empty">
                    <i class="bi bi-info-circle me-1"></i>No amendments or addendums yet.<br>
                    <small>Use the buttons below to add one.</small>
               </div>`
            : '';

        container.innerHTML = `
            <h6 class="tl-heading"><i class="bi bi-clock-history"></i>Note Activity
                <span class="badge-count">${events.length}</span>
            </h6>
            ${cardsHtml}
            ${emptyNoticeHtml}`;

        // Wire version cards
        if (typeof ctrl.onVersionClick === 'function') {
            container.querySelectorAll('.tl-card[data-version]').forEach(card => {
                card.addEventListener('click', () => {
                    const vn = parseInt(card.getAttribute('data-version'), 10);
                    const ev = events.find(e => e.type === 'version' && e.versionNumber === vn);
                    if (!ev) return;
                    ctrl.onVersionClick(vn, ev.isOriginal, ev.isCurrent || (vn === currentVN));
                });
            });
        }
        // Wire addendum cards
        if (typeof ctrl.onAddendumClick === 'function') {
            container.querySelectorAll('.tl-card[data-addendum]').forEach(card => {
                card.addEventListener('click', () => {
                    const id = parseInt(card.getAttribute('data-addendum'), 10);
                    ctrl.onAddendumClick(id);
                });
            });
        }
    }

    // --------------------------------------------------
    // Scroll the given scrollable pane to a target element and flash it.
    // Works regardless of positioned-ancestor context (uses getBoundingClientRect).
    // --------------------------------------------------
    function scrollAndFlash(pane, target, flashClass = 'flash') {
        if (!pane || !target) return;
        const targetRect = target.getBoundingClientRect();
        const paneRect = pane.getBoundingClientRect();
        const scrollTo = pane.scrollTop + (targetRect.top - paneRect.top) - 10;
        pane.scrollTo({ top: Math.max(0, scrollTo), behavior: 'smooth' });
        // Re-trigger the flash animation
        target.classList.remove(flashClass);
        void target.offsetWidth;   // force reflow
        target.classList.add(flashClass);
    }

    // --------------------------------------------------
    // Fetch a specific version's content from the server
    // (amendments and the original). Returns the raw response or null.
    // --------------------------------------------------
    async function fetchVersionContent(noteId, versionNumber) {
        try {
            return await window.apiRequest(`${API}/${noteId}/version/${versionNumber}`);
        } catch (e) {
            console.warn('[AmendmentAddendum] version fetch failed', e);
            return null;
        }
    }

    // --------------------------------------------------
    // Case-insensitive property reader so we tolerate
    // both PascalCase (server default) and camelCase.
    // --------------------------------------------------
    function normalize(obj) {
        if (!obj) return {};
        const result = {};
        for (const k in obj) {
            const lower = k.charAt(0).toLowerCase() + k.slice(1);
            result[lower] = obj[k];
        }
        return result;
    }

    // Export
    window.AmendmentAddendum = {
        renderActionButtons,
        renderTimelinePanel,
        openAddendumModal,
        openAmendmentModal,
        goToAmendment,
        formatCountdown,
        fetchStatus,
        fetchHistory,
        fetchVersionContent,
        scrollAndFlash
    };
})();
