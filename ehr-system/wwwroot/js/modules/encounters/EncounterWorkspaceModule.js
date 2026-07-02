/**
 * EncounterWorkspaceModule.js
 *
 * Encounter Workspace — the guided visit workflow for Internal Medicine.
 * Single-page experience: Vitals → History → CC/HPI → Note → Orders → Rx → Checkout
 *
 * @version 1
 */
(function () {
    'use strict';

    class EncounterWorkspaceModule {
        constructor() {
            this.encounterId = null;
            this.patientId = null;
            this.encounter = null;
            this.patient = null;
            this.currentStep = 0;
            this._saveTimeout = null;
            this._lastSaveTime = null;
            this._dirty = false;

            // Step definitions
            this.steps = [
                { key: 'vitals',        label: 'Vitals',              icon: 'bi-heart-pulse' },
                { key: 'history',       label: 'History Review',      icon: 'bi-clock-history' },
                { key: 'cc-hpi',        label: 'CC & HPI',           icon: 'bi-chat-text' },
                { key: 'note',          label: 'Clinical Note',       icon: 'bi-file-earmark-medical' },
                { key: 'orders',        label: 'Orders & Referrals',  icon: 'bi-clipboard2-pulse' },
                // COMING SOON — set comingSoon: false (or remove the flag) to re-enable Prescriptions
                { key: 'prescriptions', label: 'Prescriptions',       icon: 'bi-capsule', comingSoon: true },
                { key: 'dx-cpt-codes',  label: 'Dx & CPT Codes',      icon: 'bi-clipboard2-pulse' },
                { key: 'checkout',      label: 'Checkout',            icon: 'bi-check2-circle' }
            ];

            // Data caches for each step
            this._vitals = null;
            this._allergies = null;
            this._medications = null;
            this._problems = null;
            this._familyHistory = null;
            this._socialHistory = null;
            this._immunizations = null;
            this._orders = null;
            this._prescriptions = null;
            this._clinicalNotes = [];

            // Dx & CPT — CACHE fields only.
            // As of 2026-04-20, the actual picker logic lives in DxCptCodesComponent.js.
            // These arrays are kept as a mirror of what the component last emitted via
            // onChange, because the Checkout step and _closeEncounter still read them
            // directly to render summary cards and build the CheckOutWithCpt request.
            this._selectedIcdCodes = [];   // [{ code, description, aiSuggested }]
            this._selectedCptCodes = [];   // [{ cptCode, description, units, ... }]
            this._dxCptComponent = null;   // instance; destroyed when the step re-renders
            this.MAX_ICD_CODES = 12;       // CMS-1500 Box 21 limit (kept for any external reference)

            this._reviewedSections = {};

            // MEDOCS AI Voice instances per step
            this._medocsVoiceInstances = {};
            this._previousStepIndex = null;

            // Telehealth AI Scribe state
            this._telehealthScribeUI = null;
            this._telehealthScribeActive = false;
            this._telehealthTranscription = null;

            // Vitals auto-save state
            this._currentVitalId = null;
            this._vitalsAutoSaveTimer = null;

            // Role-based access
            const _user = JSON.parse(localStorage.getItem('currentUser') || '{}');
            this._userRole = parseInt(_user.Role ?? _user.role ?? -1);
            this._isMaNurse = UserRoles.isMaNurse(this._userRole);

            // Store reference for onclick handlers
            window._encounterWorkspace = this;
        }

        // =====================================================
        // INITIALIZATION
        // =====================================================

        async init() {
            // Only run on the encounter workspace page
            if (!document.getElementById('encounterWorkspacePage')) return;

            // Parse encounterId from URL: /Encounter/{id}
            const pathParts = window.location.pathname.split('/');
            const idx = pathParts.findIndex(p => p.toLowerCase() === 'encounter');
            this.encounterId = idx >= 0 && pathParts[idx + 1] ? parseInt(pathParts[idx + 1]) : null;

            if (!this.encounterId) {
                this._showError('No encounter ID found in URL.');
                return;
            }

            // Enter focused mode — hide main sidebar & top nav
            document.body.classList.add('encounter-focus-mode');
            // Mark the body so global floating widgets (Help, Chat) can flip from
            // right to left via CSS only on encounter pages — they otherwise
            // overlap the intake iframe's Save & Continue button at bottom-right.
            // Spec: rules/technical/intake-on-clinical-note.md §11.6.
            document.body.classList.add('on-encounter-page');

            try {
                await this._loadEncounterData();
                await this._loadPatientData();
                this._renderHeader();
                this._checkPatientDocs(); // Check for patient-uploaded documents
                this._initUnifiedVoiceBar();
                this._renderSidebar();

                // Providers land on Clinical Note (step 3); MA/Nurse land on Vitals (step 0)
                const defaultStep = this._isMaNurse ? 0 : 3;
                this._showStep(defaultStep);

                // Set initial checkmarks for all steps based on loaded data
                this._initAllStepCheckmarks();

                // Show content, hide loading
                document.getElementById('encounterLoading')?.classList.add('d-none');
                document.getElementById('encounterContent')?.classList.remove('d-none');

                // Initialize sticky notes panel
                this._initStickyNotes();

                // Listen for clinical note changes (sign, amend, etc.) via SignalR
                document.addEventListener('clinicalNoteChanged', (e) => {
                    this._onClinicalNoteChanged(e.detail);
                });

                // Telehealth: load saved transcription and decide whether to show video panel
                if (this._isTelehealthEncounter) {
                    await this._loadTelehealthTranscription();
                    if (this._telehealthTranscription) {
                        // Transcription exists — call happened before. Show "Start Video Call" button instead of auto-opening panel.
                        this._telehealthCallHappened = true;
                        // Re-render header and current step to show the video call buttons
                        this._renderHeader();
                        this._showStep(this.currentStep);
                    } else {
                        // No transcription — first visit or no call yet. Auto-open video panel.
                        this._telehealthCallHappened = false;
                        this._initTelehealthPanel();
                    }
                }
            } catch (error) {
                console.error('[EncounterWorkspace] Init error:', error);
                this._showError('Failed to load encounter data.');
            }
        }

        // =====================================================
        // DATA LOADING
        // =====================================================

        async _loadEncounterData() {
            // Use the convenience endpoint that doesn't require patientId
            const response = await window.apiRequest(`/encounters/${this.encounterId}`, { showLoader: false });
            if (!response) throw new Error('Encounter not found');
            this.encounter = response;
            this.patientId = response.PatientId;

            // Cache telehealth flag — survives encounter object being overwritten by PUT responses
            this._isTelehealthEncounter = !!(response.IsTelehealth || response.AppointmentType === 'Telehealth');
        }

        async _loadPatientData() {
            const response = await window.apiRequest(`/patients/${this.patientId}`, { showLoader: false });
            if (!response) throw new Error('Patient not found');
            this.patient = response;
        }

        /**
         * Load saved telehealth transcription from DB (if any).
         * Allows "Generate Note" to work after page revisit.
         */
        async _loadTelehealthTranscription() {
            console.log(`[TelehealthPersist] Loading saved telehealth transcription from DB for encounterId=${this.encounterId}...`);
            try {
                const result = await window.apiRequest(`/medocs-voice/telehealth-transcription/${this.encounterId}`, { showLoader: false });
                console.log('[TelehealthPersist] Load response:', result);
                if (result && result.HasTranscription && result.Transcription) {
                    this._telehealthTranscription = result.Transcription;
                    console.log(`[TelehealthPersist] Loaded saved telehealth transcription: ${result.ChunkCount} chunks, ${result.Transcription.length} chars`);
                } else {
                    console.log(`[TelehealthPersist] No saved transcription found for encounterId=${this.encounterId} (HasTranscription=${result?.HasTranscription})`);
                }
            } catch (err) {
                console.error('[TelehealthPersist] Could not load telehealth transcription:', err);
            }
        }

        // =====================================================
        // HEADER BAR
        // =====================================================

        _renderHeader() {
            const enc = this.encounter;
            const pt = this.patient;

            // Calculate age
            const dob = pt.DateOfBirth ? new Date(pt.DateOfBirth) : null;
            let age = '';
            if (dob) {
                const today = new Date();
                age = Math.floor((today - dob) / (365.25 * 24 * 60 * 60 * 1000));
            }

            const statusColors = { 'Open': 'success', 'Signed': 'primary', 'Locked': 'secondary', 'Amended': 'warning' };
            const statusColor = statusColors[enc.StatusName] || 'secondary';

            const headerEl = document.getElementById('encounterHeader');
            headerEl.innerHTML = `
                <div class="ew-header">
                    <div class="d-flex align-items-center justify-content-between flex-wrap gap-2">
                        <div class="d-flex align-items-center gap-3">
                            <a href="/Home/Dashboard" class="ew-back-btn" title="Back to Dashboard">
                                <i class="bi bi-arrow-left"></i>
                                <span class="d-none d-sm-inline">Dashboard</span>
                            </a>
                            <!-- Patient avatar — photo if uploaded, colored initials
                                 otherwise. Replaces the previous generic
                                 bi-person-circle icon. -->
                            ${window.AvatarUtils ? AvatarUtils.renderPatientAvatar({
                                patientId: pt.PatientId,
                                name: `${pt.FirstName || ''} ${pt.LastName || ''}`.trim(),
                                hasProfilePicture: !!(pt.HasProfilePicture || pt.ProfilePicturePath),
                                size: 'md'
                            }) : ''}
                            <div class="ew-patient-info">
                                <h5 class="mb-0 fw-bold">
                                    ${this._escape(pt.FirstName)} ${this._escape(pt.LastName)}
                                </h5>
                                <small class="text-muted">
                                    MRN: <strong>${this._escape(pt.MRN || pt.Mrn || '')}</strong>
                                    &nbsp;|&nbsp; DOB: ${pt.DateOfBirth ? new Date(pt.DateOfBirth).toLocaleDateString() : '-'}${age ? ` (${age}y)` : ''}
                                    &nbsp;|&nbsp; ${this._escape(enc.ProviderName || '')}
                                </small>
                            </div>
                        </div>
                        <div class="d-flex align-items-center gap-3">
                            ${this._isTelehealthEncounter && this._telehealthCallHappened ? `
                                <button class="btn btn-sm btn-success" id="ewHeaderStartVideoCall" title="Start Video Call"
                                    onclick="window._encounterWorkspace._startVideoCallFromButton()"
                                    style="animation: pulse-glow 2s ease-in-out 3;">
                                    <i class="bi bi-camera-video-fill me-1"></i>Start Video Call
                                </button>
                            ` : ''}
                            <button class="btn btn-sm btn-outline-info position-relative" id="ewPatientDocsToggle" title="Patient Uploaded Documents" onclick="window._encounterWorkspace.togglePatientDocs()">
                                <i class="bi bi-folder2-open me-1"></i>Patient Docs
                                <span class="badge bg-info text-white rounded-pill ms-1 d-none" id="ewPatientDocsCount">0</span>
                            </button>
                            <button class="btn btn-sm btn-outline-warning position-relative" id="ewStickyNotesToggle" title="Patient Sticky Notes" onclick="window._encounterWorkspace.toggleStickyNotes()">
                                <i class="bi bi-sticky me-1"></i>Notes
                                <span class="badge bg-warning text-dark rounded-pill ms-1 d-none" id="ewStickyNotesCount">0</span>
                            </button>
                            <span id="ewAutoSaveIndicator" class="text-muted small">
                                <i class="bi bi-cloud-check"></i> Ready
                            </span>
                            <span class="badge bg-${statusColor} fs-6">
                                <i class="bi bi-circle-fill me-1" style="font-size: 0.5rem;"></i>${enc.StatusName || 'Open'}
                            </span>
                            <span class="badge bg-light text-dark">
                                <i class="bi bi-calendar3 me-1"></i>${enc.EncounterDate || ''}
                            </span>
                        </div>
                    </div>
                    <!-- Unified AI Voice Entry Bar -->
                    <div id="ewUnifiedVoiceContainer"></div>
                </div>
            `;
        }

        // =====================================================
        // SIDEBAR & STEP NAVIGATION
        // =====================================================

        _renderSidebar() {
            const sidebarEl = document.getElementById('encounterSidebar');
            sidebarEl.innerHTML = `
                <nav class="ew-sidebar">
                    ${this.steps.map((step, i) => step.comingSoon ? `
                        <a href="#" class="ew-step-item ew-step-coming-soon ${i === this.currentStep ? 'active' : ''}"
                           data-step="${i}" data-coming-soon="true" title="Coming soon...">
                            <span class="ew-step-icon">
                                <i class="bi ${step.icon}"></i>
                            </span>
                            <span class="ew-step-label">${step.label} <i class="bi bi-clock ms-1" style="font-size:0.7em;opacity:0.7;"></i></span>
                            <span class="ew-step-check d-none" id="ewStepCheck_${i}">
                                <i class="bi bi-check-circle-fill text-success"></i>
                            </span>
                        </a>
                    ` : `
                        <a href="#" class="ew-step-item ${i === this.currentStep ? 'active' : ''}"
                           data-step="${i}" title="${step.label}">
                            <span class="ew-step-icon">
                                <i class="bi ${step.icon}"></i>
                            </span>
                            <span class="ew-step-label">${step.label}</span>
                            <span class="ew-step-check d-none" id="ewStepCheck_${i}">
                                <i class="bi bi-check-circle-fill text-success"></i>
                            </span>
                        </a>
                    `).join('')}

                    <div class="ew-forms-group">
                        <div class="ew-forms-label">Forms</div>
                        <a href="#" class="ew-step-item ew-form-item" id="ewPatientIntakeTool" data-form="patient-intake" title="Open patient intake form">
                            <span class="ew-step-icon">
                                <i class="bi bi-clipboard2-pulse"></i>
                            </span>
                            <span class="ew-step-label">Patient Intake Form</span>
                            <span class="ew-step-check" id="ewPatientIntakeBadge" style="display:none"></span>
                        </a>
                    </div>
                </nav>
            `;

            // Wire the Patient Intake form link.
            // Click swaps the encounter center pane to a fresh iframe (reload-fresh
            // strategy — see rules/technical/intake-on-clinical-note.md §3.3).
            const intakeTool = sidebarEl.querySelector('#ewPatientIntakeTool');
            if (intakeTool) {
                intakeTool.addEventListener('click', (e) => {
                    e.preventDefault();
                    if (!this.patientId) return;
                    this._openIntakeIframeInCenterPane(this.patientId);
                    // Highlight this entry as active, de-highlight any active step
                    sidebarEl.querySelectorAll('.ew-step-item.active').forEach(el => el.classList.remove('active'));
                    intakeTool.classList.add('active');
                });

                // Populate progress badge asynchronously (best-effort)
                if (this.patientId) {
                    this._loadIntakeProgressBadge(this.patientId).catch(() => {});
                }
            }

            // Bind click events. The selector matches both step links AND the
            // Forms group items (Patient Intake Form), which share the
            // .ew-step-item class but have no data-step attribute. Guard so a
            // click on a form item does not call _showStep with NaN.
            sidebarEl.querySelectorAll('.ew-step-item').forEach(item => {
                item.addEventListener('click', (e) => {
                    e.preventDefault();

                    // Form items (Patient Intake Form, etc.) have their own
                    // dedicated click handlers bound elsewhere; skip step nav.
                    if (item.classList.contains('ew-form-item') || !item.dataset.step) {
                        return;
                    }

                    // COMING SOON — intercept clicks on disabled steps; remove block when feature is enabled
                    if (item.dataset.comingSoon) {
                        if (!item._ewComingSoonTooltip) {
                            item._ewComingSoonTooltip = new bootstrap.Tooltip(item, { trigger: 'manual', placement: 'right' });
                        }
                        item._ewComingSoonTooltip.show();
                        setTimeout(() => item._ewComingSoonTooltip?.hide(), 2000);
                        return;
                    }

                    const stepIndex = parseInt(item.dataset.step);
                    if (Number.isNaN(stepIndex) || !this.steps[stepIndex]) return;
                    this._showStep(stepIndex);
                });
            });
        }

        async _loadIntakeProgressBadge(patientId) {
            const badge = document.getElementById('ewPatientIntakeBadge');
            if (!badge) return;
            try {
                // Step 1 endpoint: lighter than the legacy intake-view endpoint —
                // returns progress numbers without the full aggregated section data.
                const resp = await window.apiRequest(`/clinic/patients/${patientId}/intake/progress`, { showLoader: false });
                if (!resp) return;
                const completed = resp.Completed != null ? resp.Completed : resp.completed;
                const total = resp.Total != null ? resp.Total : resp.total;
                if (completed == null || total == null) return;
                badge.textContent = `${completed}/${total}`;
                badge.className = 'badge ' + (completed === total && total > 0 ? 'bg-success' : 'bg-primary') + ' ms-1';
                badge.style.display = '';
            } catch (e) { /* best-effort */ }
        }

        /**
         * Swap the encounter center pane to a fresh iframe loading the staff
         * intake host. Reload-fresh strategy: each click discards any prior iframe
         * and inserts a new one, guaranteeing the form pulls current chart data.
         *
         * Important: we overwrite #encounterMain (not just #ewStepContent) so the
         * step header `<h4>` from the previously active step (e.g. "Vitals") is
         * replaced with the intake form's own "Patient Intake Form" header. If we
         * only replaced #ewStepContent, the old step's title would remain above
         * the iframe.
         *
         * Spec: rules/technical/intake-on-clinical-note.md §3.3.
         */
        _openIntakeIframeInCenterPane(patientId) {
            const mainEl = document.getElementById('encounterMain');
            if (!mainEl) return;
            mainEl.innerHTML = `
                <div class="ew-main-content">
                    <div class="ew-step-header">
                        <h4 class="mb-0">
                            <i class="bi bi-clipboard2-pulse me-2 text-primary"></i>Patient Intake Form
                        </h4>
                    </div>
                    <div class="ew-step-body" id="ewStepContent" style="display:flex; flex-direction:column; padding:0;">
                        <iframe class="cn-intake-frame"
                                src="/clinic/patients/${encodeURIComponent(patientId)}/intake-frame"
                                loading="lazy"
                                style="flex:1; width:100%; min-height:600px; border:0;"></iframe>
                    </div>
                </div>
            `;
            // Track that the center pane is showing the intake iframe (not a step).
            this._previousStepIndex = null;
            this._activeFormKey = 'patient-intake';
        }

        _showStep(index) {
            // Defensive: callers might hand in a bad index (NaN, out-of-range).
            // Bail silently rather than throwing.
            if (typeof index !== 'number' || Number.isNaN(index) || !this.steps[index]) {
                console.warn('[EncounterWorkspace] _showStep called with invalid index:', index);
                return;
            }

            // Destroy per-section MEDOCS voice instance from previous step (legacy, only if no unified bar)
            if (!this._unifiedVoiceBar && this._previousStepIndex != null) {
                const prevKey = this.steps[this._previousStepIndex]?.key;
                if (prevKey && this._medocsVoiceInstances[prevKey]) {
                    this._medocsVoiceInstances[prevKey].destroy();
                    delete this._medocsVoiceInstances[prevKey];
                }
            }
            this._previousStepIndex = index;

            this.currentStep = index;
            const step = this.steps[index];

            // Show unified voice bar only on Vitals, History Review, CC/HPI
            // Hide on Clinical Note (clinicians have Record Session), Orders, Prescriptions, Dx & CPT Codes, Checkout
            if (this._unifiedVoiceBar) {
                const voiceBarSteps = ['vitals', 'history', 'cc-hpi'];
                if (voiceBarSteps.includes(step.key)) {
                    this._unifiedVoiceBar.show();
                } else {
                    this._unifiedVoiceBar.hide();
                }
            }

            // Update sidebar active state
            document.querySelectorAll('.ew-step-item').forEach((item, i) => {
                item.classList.toggle('active', i === index);
            });

            const mainEl = document.getElementById('encounterMain');

            // Clinical Note step for providers: consolidated layout with reference panel
            if (step.key === 'note' && !this._isMaNurse) {
                mainEl.innerHTML = `
                    <div class="ew-main-content">
                        <div class="ew-step-header d-flex align-items-center justify-content-between">
                            <h4 class="mb-0">
                                <i class="bi ${step.icon} me-2 text-primary"></i>${step.label}
                            </h4>
                            <button class="btn btn-sm btn-outline-secondary" id="ewToggleRefPanel" title="Toggle reference panel">
                                <i class="bi bi-layout-sidebar-inset me-1"></i>Reference
                            </button>
                        </div>
                        <div class="ew-consolidated-layout">
                            <div class="ew-ref-panel" id="ewRefPanel">
                                <div class="ew-ref-panel-content" id="ewRefPanelContent">
                                    <div class="text-center py-3"><div class="spinner-border spinner-border-sm"></div> Loading...</div>
                                </div>
                            </div>
                            <div class="ew-note-main" id="ewStepContent">
                                <div class="text-center py-5 text-muted">
                                    <div class="spinner-border spinner-border-sm"></div>
                                    <p class="mt-3">Loading notes...</p>
                                </div>
                            </div>
                        </div>
                    </div>
                `;

                // Bind toggle button
                document.getElementById('ewToggleRefPanel')?.addEventListener('click', () => {
                    const panel = document.getElementById('ewRefPanel');
                    if (panel) panel.classList.toggle('collapsed');
                });

                // Load reference panel and note content in parallel
                this._noteStepStale = false;
                this._renderNoteStep();
                this._loadReferencePanel();
                return;
            }

            // Default layout for all other steps
            mainEl.innerHTML = `
                <div class="ew-main-content">
                    <div class="ew-step-header">
                        <h4 class="mb-0">
                            <i class="bi ${step.icon} me-2 text-primary"></i>${step.label}
                        </h4>
                    </div>
                    <div class="ew-step-body" id="ewStepContent">
                        <div class="text-center py-5 text-muted">
                            <i class="bi ${step.icon}" style="font-size: 3rem;"></i>
                            <p class="mt-3">${step.label} — Coming soon</p>
                        </div>
                    </div>
                </div>
            `;

            // Clear staleness flags for the step being rendered
            if (step.key === 'note') this._noteStepStale = false;
            if (step.key === 'orders') this._ordersStepStale = false;
            if (step.key === 'prescriptions') this._prescriptionsStepStale = false;

            // Render specific step content
            switch (step.key) {
                case 'vitals':        this._renderVitalsStep(); break;
                case 'history':       this._renderHistoryStep(); break;
                case 'cc-hpi':        this._renderCcHpiStep(); break;
                case 'orders':        this._renderOrdersStep(); break;
                case 'prescriptions': this._renderPrescriptionsStep(); break;
                case 'note':          this._renderNoteStep(); break;
                case 'dx-cpt-codes':  this._renderDxCptCodesStep(); break;
                case 'checkout':      this._renderCheckoutStep(); break;
            }
        }

        _markStepComplete(stepIndex, hasData) {
            const checkEl = document.getElementById(`ewStepCheck_${stepIndex}`);
            if (checkEl) {
                checkEl.classList.toggle('d-none', !hasData);
            }
        }

        /** Set checkmarks for all steps based on currently loaded data (called on init and refresh) */
        async _initAllStepCheckmarks() {
            try {
                // Vitals (step 0)
                const vitals = await window.apiRequest(`/patients/${this.patientId}/vitals`, { showLoader: false }).catch(() => []) || [];
                const hasVitals = vitals.some(v => v.EncounterId === this.encounterId);
                this._markStepComplete(0, hasVitals);

                // History (step 1) — check if any history data exists
                const [allergies, medications, problems, familyHx, socialHx, immunizations] = await Promise.all([
                    window.apiRequest(`/patients/${this.patientId}/allergies`, { showLoader: false }).catch(() => []),
                    window.apiRequest(`/patients/${this.patientId}/medications`, { showLoader: false }).catch(() => []),
                    window.apiRequest(`/patients/${this.patientId}/problems`, { showLoader: false }).catch(() => []),
                    window.apiRequest(`/patients/${this.patientId}/family-history`, { showLoader: false }).catch(() => []),
                    window.apiRequest(`/patients/${this.patientId}/social-history`, { showLoader: false }).catch(() => []),
                    window.apiRequest(`/patients/${this.patientId}/immunizations`, { showLoader: false }).catch(() => [])
                ]);
                const hasHistory = [allergies, medications, problems, familyHx, socialHx, immunizations].some(a => a && a.length > 0);
                this._markStepComplete(1, hasHistory);

                // CC/HPI (step 2)
                const enc = this.encounter;
                this._markStepComplete(2, !!(enc.ChiefComplaint || enc.HistoryOfPresentIllness));

                // Clinical Note (step 3)
                const notes = await window.apiRequest(`/clinical-notes/by-appointment/${enc.AppointmentId}`, { showLoader: false }).catch(() => []) || [];
                this._markStepComplete(3, notes.length > 0);

                // Orders (step 4)
                const orders = await window.apiRequest(`/orders?patientId=${this.patientId}`, { showLoader: false }).catch(() => []) || [];
                this._markStepComplete(4, orders.length > 0);

                // Prescriptions (step 5)
                const prescriptions = await window.apiRequest(`/prescriptions?patientId=${this.patientId}`, { showLoader: false }).catch(() => []) || [];
                this._markStepComplete(5, prescriptions.length > 0);
            } catch (e) {
                console.warn('[Encounter] Error initializing step checkmarks:', e);
            }
        }

        // =====================================================
        // STEP RENDERERS (Placeholders — implemented in steps 5.4-5.10)
        // =====================================================

        async _renderVitalsStep() {
            const container = document.getElementById('ewStepContent');
            if (!container) return;

            // Load vitals
            try {
                this._vitals = await window.apiRequest(`/patients/${this.patientId}/vitals`, { showLoader: false }) || [];
            } catch (e) {
                this._vitals = [];
            }

            // Find vitals for this encounter
            const encounterVitals = this._vitals.filter(v => v.EncounterId === this.encounterId);

            // Track existing vital ID for auto-save (PUT vs POST)
            this._currentVitalId = encounterVitals.length > 0 ? encounterVitals[0].PatientVitalId : null;

            this._markStepComplete(0, encounterVitals.length > 0);

            // Previous vitals = all vitals NOT from this encounter
            const previousVitals = this._vitals.filter(v => v.EncounterId !== this.encounterId);
            // Current encounter vital (to pre-fill the form)
            const currentVital = encounterVitals.length > 0 ? encounterVitals[0] : null;

            container.innerHTML = `
                <div class="card">
                    <div class="card-header bg-white d-flex justify-content-between align-items-center">
                        <h6 class="mb-0"><i class="bi bi-heart-pulse me-2 text-primary"></i>Record Vitals</h6>
                        <span id="ewVitalSaveStatus" class="text-muted small"></span>
                    </div>
                    <div class="card-body">
                        <div class="row g-3">
                            <div class="col-md-3">
                                <label class="form-label fw-semibold">Systolic BP</label>
                                <div class="input-group">
                                    <input type="number" class="form-control" id="ewVitalSysBp" placeholder="120" min="60" max="300">
                                    <span class="input-group-text">mmHg</span>
                                </div>
                            </div>
                            <div class="col-md-3">
                                <label class="form-label fw-semibold">Diastolic BP</label>
                                <div class="input-group">
                                    <input type="number" class="form-control" id="ewVitalDiaBp" placeholder="80" min="30" max="200">
                                    <span class="input-group-text">mmHg</span>
                                </div>
                            </div>
                            <div class="col-md-3">
                                <label class="form-label fw-semibold">Heart Rate</label>
                                <div class="input-group">
                                    <input type="number" class="form-control" id="ewVitalHr" placeholder="72" min="20" max="300">
                                    <span class="input-group-text">bpm</span>
                                </div>
                            </div>
                            <div class="col-md-3">
                                <label class="form-label fw-semibold">Temperature</label>
                                <div class="input-group">
                                    <input type="number" class="form-control" id="ewVitalTemp" placeholder="98.6" step="0.1" min="90" max="110">
                                    <span class="input-group-text">&deg;F</span>
                                </div>
                            </div>
                            <div class="col-md-3">
                                <label class="form-label fw-semibold">SpO2</label>
                                <div class="input-group">
                                    <input type="number" class="form-control" id="ewVitalSpO2" placeholder="98" min="50" max="100">
                                    <span class="input-group-text">%</span>
                                </div>
                            </div>
                            <div class="col-md-3">
                                <label class="form-label fw-semibold">Resp Rate</label>
                                <div class="input-group">
                                    <input type="number" class="form-control" id="ewVitalRr" placeholder="16" min="4" max="60">
                                    <span class="input-group-text">/min</span>
                                </div>
                            </div>
                            <div class="col-md-3">
                                <label class="form-label fw-semibold">Weight</label>
                                <div class="input-group">
                                    <input type="number" class="form-control" id="ewVitalWeight" placeholder="170" step="0.1" min="1" max="1000">
                                    <span class="input-group-text">lbs</span>
                                </div>
                            </div>
                            <div class="col-md-3">
                                <label class="form-label fw-semibold">Height</label>
                                <div class="input-group">
                                    <input type="number" class="form-control" id="ewVitalHeight" placeholder="70" step="0.1" min="10" max="120">
                                    <span class="input-group-text">in</span>
                                </div>
                            </div>
                            <div class="col-12">
                                <label class="form-label fw-semibold">BMI <small class="text-muted">(auto-calculated)</small></label>
                                <input type="text" class="form-control bg-light" id="ewVitalBmi" readonly placeholder="—">
                            </div>
                        </div>
                    </div>
                </div>

                <!-- Previous Encounters' Vitals -->
                <div class="card mt-3">
                    <div class="card-header bg-white">
                        <h6 class="mb-0"><i class="bi bi-clock-history me-2 text-secondary"></i>Previous Vitals</h6>
                    </div>
                    <div class="card-body p-0">
                        ${this._renderPreviousVitalsHistory(previousVitals)}
                    </div>
                </div>
            `;

            // BMI auto-calc (bind BEFORE prefill so dispatchEvent triggers it)
            const weightEl = document.getElementById('ewVitalWeight');
            const heightEl = document.getElementById('ewVitalHeight');
            const bmiEl = document.getElementById('ewVitalBmi');

            const calcBmi = () => {
                const w = parseFloat(weightEl?.value);
                const h = parseFloat(heightEl?.value);
                if (w > 0 && h > 0) {
                    const bmi = ((w / (h * h)) * 703).toFixed(1);
                    bmiEl.value = bmi;
                } else {
                    bmiEl.value = '—';
                }
            };

            weightEl?.addEventListener('input', calcBmi);
            heightEl?.addEventListener('input', calcBmi);

            // Pre-fill form if this encounter already has vitals
            if (currentVital) {
                this._prefillVitalsForm(currentVital);
            }

            // Auto-save on any vitals field change (debounced)
            const vitalFieldIds = ['ewVitalSysBp', 'ewVitalDiaBp', 'ewVitalHr', 'ewVitalTemp', 'ewVitalSpO2', 'ewVitalRr', 'ewVitalWeight', 'ewVitalHeight'];
            const autoSaveVitals = () => {
                if (this._vitalsAutoSaveTimer) clearTimeout(this._vitalsAutoSaveTimer);
                this._vitalsAutoSaveTimer = setTimeout(() => this._autoSaveVitals(), 1200);
            };
            vitalFieldIds.forEach(id => {
                document.getElementById(id)?.addEventListener('input', autoSaveVitals);
            });

            // MEDOCS AI Voice Entry (MA/Nurse can use voice for vitals)
            this._initMedocsVoice('vitals');

            // Flush any pending voice data for vitals
            if (this._unifiedVoiceBar) this._unifiedVoiceBar.flushPendingVitals();
            if (this._telehealthScribeUI) this._telehealthScribeUI.flushPendingVitals();
        }

        _prefillVitalsForm(vital) {
            const setVal = (id, val) => {
                const el = document.getElementById(id);
                if (el && val != null) el.value = val;
            };
            setVal('ewVitalSysBp', vital.SystolicBp);
            setVal('ewVitalDiaBp', vital.DiastolicBp);
            setVal('ewVitalHr', vital.HeartRate);
            setVal('ewVitalTemp', vital.Temperature);
            setVal('ewVitalSpO2', vital.SpO2);
            setVal('ewVitalRr', vital.RespiratoryRate);
            setVal('ewVitalWeight', vital.Weight);
            setVal('ewVitalHeight', vital.Height);
            // Notes field hidden from UI — do not try to prefill.

            // Trigger BMI calc
            const weightEl = document.getElementById('ewVitalWeight');
            if (weightEl) weightEl.dispatchEvent(new Event('input', { bubbles: true }));

            // Show saved status
            if (vital.RecordedAt) {
                const time = new Date(vital.RecordedAt).toLocaleTimeString();
                this._updateVitalSaveStatus('saved');
            }
        }

        _renderPreviousVitalsHistory(vitals) {
            if (!vitals || vitals.length === 0) {
                return '<p class="text-muted text-center py-3 mb-0"><i class="bi bi-info-circle me-1"></i>No previous vitals on record</p>';
            }

            const formatBp = (v) => {
                if (v.SystolicBp && v.DiastolicBp) return `${v.SystolicBp}/${v.DiastolicBp}`;
                return '—';
            };

            const flagVal = (val, low, high) => {
                if (val == null) return '';
                if (val < low || val > high) return 'text-danger fw-semibold';
                return '';
            };

            const rows = vitals.slice(0, 10).map(v => {
                const date = v.RecordedAt ? new Date(v.RecordedAt).toLocaleDateString() : '—';
                const bp = formatBp(v);
                const bpClass = (v.SystolicBp > 140 || v.DiastolicBp > 90) ? 'text-danger fw-semibold' : '';
                const hrClass = flagVal(v.HeartRate, 60, 100);
                const tempClass = (v.Temperature && v.Temperature > 100.4) ? 'text-danger fw-semibold' : '';
                const spo2Class = (v.SpO2 && v.SpO2 < 95) ? 'text-danger fw-semibold' : '';

                return `<tr>
                    <td class="text-muted small">${date}</td>
                    <td class="${bpClass}">${bp}</td>
                    <td class="${hrClass}">${v.HeartRate || '—'}</td>
                    <td class="${tempClass}">${v.Temperature || '—'}</td>
                    <td class="${spo2Class}">${v.SpO2 ? v.SpO2 + '%' : '—'}</td>
                    <td>${v.RespiratoryRate || '—'}</td>
                    <td>${v.Weight || '—'}</td>
                    <td>${v.Bmi ? Number(v.Bmi).toFixed(1) : '—'}</td>
                </tr>`;
            }).join('');

            return `
                <div class="table-responsive">
                    <table class="table table-sm table-hover mb-0 small">
                        <thead class="table-light">
                            <tr>
                                <th>Date</th>
                                <th>BP</th>
                                <th>HR</th>
                                <th>Temp</th>
                                <th>SpO2</th>
                                <th>RR</th>
                                <th>Wt</th>
                                <th>BMI</th>
                            </tr>
                        </thead>
                        <tbody>${rows}</tbody>
                    </table>
                </div>`;
        }

        _buildVitalsDto() {
            return {
                EncounterId: this.encounterId,
                SystolicBp: parseInt(document.getElementById('ewVitalSysBp')?.value) || null,
                DiastolicBp: parseInt(document.getElementById('ewVitalDiaBp')?.value) || null,
                HeartRate: parseInt(document.getElementById('ewVitalHr')?.value) || null,
                Temperature: parseFloat(document.getElementById('ewVitalTemp')?.value) || null,
                SpO2: parseFloat(document.getElementById('ewVitalSpO2')?.value) || null,
                RespiratoryRate: parseInt(document.getElementById('ewVitalRr')?.value) || null,
                Weight: parseFloat(document.getElementById('ewVitalWeight')?.value) || null,
                Height: parseFloat(document.getElementById('ewVitalHeight')?.value) || null,
                Notes: '' // Notes field hidden from UI
            };
        }

        _hasAnyVitalValue(dto) {
            return !!(dto.SystolicBp || dto.DiastolicBp || dto.HeartRate || dto.Temperature || dto.SpO2 || dto.RespiratoryRate || dto.Weight || dto.Height);
        }

        _updateVitalSaveStatus(state) {
            const el = document.getElementById('ewVitalSaveStatus');
            if (!el) return;
            switch (state) {
                case 'saving':
                    el.innerHTML = '<i class="bi bi-cloud-arrow-up text-warning me-1"></i>Saving...';
                    break;
                case 'saved':
                    const now = new Date().toLocaleTimeString();
                    el.innerHTML = `<i class="bi bi-cloud-check text-success me-1"></i>Saved at ${now}`;
                    break;
                case 'error':
                    el.innerHTML = '<i class="bi bi-cloud-slash text-danger me-1"></i>Save failed';
                    break;
                default:
                    el.innerHTML = '';
            }
        }

        async _autoSaveVitals() {
            const dto = this._buildVitalsDto();
            if (!this._hasAnyVitalValue(dto)) return;

            this._updateVitalSaveStatus('saving');

            try {
                if (this._currentVitalId) {
                    // UPDATE existing vital
                    await window.apiRequest(`/patients/${this.patientId}/vitals/${this._currentVitalId}`, {
                        method: 'PUT',
                        body: dto,
                        showLoader: false
                    });
                } else {
                    // CREATE new vital
                    const result = await window.apiRequest(`/patients/${this.patientId}/vitals`, {
                        method: 'POST',
                        body: dto,
                        showLoader: false
                    });
                    if (result && result.PatientVitalId) {
                        this._currentVitalId = result.PatientVitalId;
                    }
                }
                this._updateVitalSaveStatus('saved');
                this._markStepComplete(0, true);
            } catch (e) {
                console.error('[EncounterWorkspace] Auto-save vitals error:', e);
                this._updateVitalSaveStatus('error');
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // HISTORY STEP — Excel-like Inline Editable Grid
        // ═══════════════════════════════════════════════════════════════

        /** Section configuration for the history grid.
         * activeData = items shown in the main grid (clinically current).
         * inactiveData = items in the collapsible "Inactive / Discontinued" sub-section.
         * Sections without state (familyHx, socialHx, immunizations) leave inactiveData empty.
         */
        _getHistorySections() {
            return [
                { key: 'allergies',    label: 'Allergies',          icon: 'bi-exclamation-triangle', activeData: this._allergiesActive,    inactiveData: this._allergiesInactive,    color: 'danger',    endpoint: 'allergies',      api: 'allergy',        hasState: true  },
                { key: 'medications',  label: 'Active Medications', icon: 'bi-capsule',              activeData: this._medicationsActive,  inactiveData: this._medicationsInactive,  color: 'info',      endpoint: 'medications',    api: 'medication',     hasState: true  },
                { key: 'problems',     label: 'Problem List',       icon: 'bi-list-check',           activeData: this._problemsActive,     inactiveData: this._problemsInactive,     color: 'warning',   endpoint: 'problems',       api: 'problem',        hasState: true  },
                { key: 'familyHx',     label: 'Family History',     icon: 'bi-people',               activeData: this._familyHistory,      inactiveData: [],                          color: 'secondary', endpoint: 'family-history', api: 'family-history', hasState: false },
                { key: 'socialHx',     label: 'Social History',     icon: 'bi-person-badge',         activeData: this._socialHistory,      inactiveData: [],                          color: 'secondary', endpoint: 'social-history', api: 'social-history', hasState: false },
                { key: 'immunizations',label: 'Immunizations',      icon: 'bi-shield-check',         activeData: this._immunizations,      inactiveData: [],                          color: 'success',   endpoint: 'immunizations',  api: 'immunization',   hasState: false }
            ];
        }

        /** Map a section key to the active/inactive split using DB status fields. */
        _classifyHistoryItems(sectionKey, items) {
            if (!Array.isArray(items)) return { active: [], inactive: [] };
            const active = [];
            const inactive = [];
            for (const item of items) {
                if (this._isItemInactive(sectionKey, item)) inactive.push(item);
                else active.push(item);
            }
            // Sort inactive by most recent state change first (UpdatedAt DESC)
            inactive.sort((a, b) => {
                const da = new Date(this._getFieldValue(a, 'updatedAt') || this._getFieldValue(a, 'createdAt') || 0);
                const db = new Date(this._getFieldValue(b, 'updatedAt') || this._getFieldValue(b, 'createdAt') || 0);
                return db - da;
            });
            return { active, inactive };
        }

        /** Returns true if the item should appear in the Inactive sub-section. */
        _isItemInactive(sectionKey, item) {
            switch (sectionKey) {
                case 'allergies': {
                    const isActive = item.IsActive ?? item.isActive;
                    return isActive === false;
                }
                case 'medications': {
                    // Status: 0=Active, 1=Discontinued, 2=OnHold, 3=Completed
                    // Active sub-section shows Active+OnHold; Inactive shows Discontinued+Completed.
                    const s = item.Status ?? item.status ?? 0;
                    return s === 1 || s === 3;
                }
                case 'problems': {
                    // Status: 0=Active, 1=Resolved, 2=Inactive
                    const s = item.Status ?? item.status ?? 0;
                    return s !== 0;
                }
                default:
                    // Stateless sections — never "inactive"
                    return false;
            }
        }

        /** Human label for the inactive sub-section, varies by section. */
        _inactiveSectionLabel(sectionKey) {
            switch (sectionKey) {
                case 'allergies':   return 'Inactive Allergies';
                case 'medications': return 'Discontinued / Completed';
                case 'problems':    return 'Resolved / Inactive Problems';
                default:            return 'Inactive';
            }
        }

        /** Column definitions per section — simplified to primary field + notes */
        _getGridColumns(sectionKey) {
            const cols = {
                allergies: [
                    { field: 'allergenName', label: 'Allergen', width: '30%', type: 'text', required: true, placeholder: 'Type allergen...', autocomplete: 'allergens' },
                    { field: 'notes', label: 'Notes', width: '52%', type: 'text', placeholder: 'e.g. Drug allergy, causes rash, moderate severity' }
                ],
                medications: [
                    { field: 'drugName', label: 'Drug Name', width: '30%', type: 'text', required: true, placeholder: 'Type drug name...', autocomplete: 'drugs' },
                    { field: 'notes', label: 'Notes', width: '52%', type: 'text', placeholder: 'e.g. 10mg, oral, once daily, tablet' }
                ],
                problems: [
                    { field: 'description', label: 'Description', width: '30%', type: 'text', required: true, placeholder: 'Type problem...', autocomplete: 'icd' },
                    { field: 'notes', label: 'Notes', width: '52%', type: 'text', placeholder: 'Optional notes' }
                ],
                familyHx: [
                    { field: 'condition', label: 'Condition', width: '30%', type: 'text', required: true, placeholder: 'Type condition...', autocomplete: 'icd' },
                    { field: 'notes', label: 'Notes', width: '52%', type: 'text', placeholder: 'e.g. Mother, age 55, deceased' }
                ],
                socialHx: [
                    { field: 'category', label: 'Category', width: '22%', type: 'text', required: true, placeholder: 'Category' },
                    { field: 'notes', label: 'Notes', width: '60%', type: 'text', placeholder: 'e.g. Never smoked, denies tobacco use' }
                ],
                immunizations: [
                    { field: 'vaccineName', label: 'Vaccine', width: '30%', type: 'text', required: true, placeholder: 'Type vaccine...', autocomplete: 'vaccines' },
                    { field: 'notes', label: 'Notes', width: '52%', type: 'text', placeholder: 'e.g. Given today, left arm, Lot# ABC123' }
                ]
            };
            return cols[sectionKey] || [];
        }

        /** ID field name per section */
        _getIdField(sectionKey) {
            const map = {
                allergies: ['PatientAllergyId', 'patientAllergyId'],
                medications: ['PatientMedicationId', 'patientMedicationId'],
                problems: ['PatientProblemId', 'patientProblemId'],
                familyHx: ['PatientFamilyHistoryId', 'patientFamilyHistoryId'],
                socialHx: ['PatientSocialHistoryId', 'patientSocialHistoryId'],
                immunizations: ['PatientImmunizationId', 'patientImmunizationId']
            };
            return map[sectionKey] || [];
        }

        /** Get item ID from data object */
        _getItemId(sectionKey, item) {
            const fields = this._getIdField(sectionKey);
            for (const f of fields) {
                if (item[f] != null) return item[f];
            }
            return null;
        }

        /** Get the field value from a data item, handling PascalCase/camelCase */
        _getFieldValue(item, field) {
            // Try camelCase first (API usually returns this), then PascalCase
            if (item[field] != null) return item[field];
            const pascal = field.charAt(0).toUpperCase() + field.slice(1);
            if (item[pascal] != null) return item[pascal];
            return null;
        }

        async _renderHistoryStep() {
            const container = document.getElementById('ewStepContent');
            if (!container) return;

            // Track debounce timers for auto-save
            this._historyDebounceTimers = this._historyDebounceTimers || {};

            container.innerHTML = `<div class="text-center py-3"><div class="spinner-border spinner-border-sm"></div> Loading history...</div>`;

            // Load all history data in parallel.
            // EF global query filter on IsDeleted = false means soft-deleted rows are
            // already excluded server-side. We classify what's left into Active vs
            // Inactive (per rules/technical/history-review-soft-delete.md).
            try {
                const [allergies, medications, problems, familyHx, socialHx, immunizations] = await Promise.all([
                    window.apiRequest(`/patients/${this.patientId}/allergies`, { showLoader: false }).catch(() => []),
                    window.apiRequest(`/patients/${this.patientId}/medications`, { showLoader: false }).catch(() => []),
                    window.apiRequest(`/patients/${this.patientId}/problems`, { showLoader: false }).catch(() => []),
                    window.apiRequest(`/patients/${this.patientId}/family-history`, { showLoader: false }).catch(() => []),
                    window.apiRequest(`/patients/${this.patientId}/social-history`, { showLoader: false }).catch(() => []),
                    window.apiRequest(`/patients/${this.patientId}/immunizations`, { showLoader: false }).catch(() => [])
                ]);

                // Classify sections that have a clinical state field
                const allergySplit = this._classifyHistoryItems('allergies', allergies);
                const medSplit     = this._classifyHistoryItems('medications', medications);
                const probSplit    = this._classifyHistoryItems('problems', problems);

                this._allergiesActive    = allergySplit.active;
                this._allergiesInactive  = allergySplit.inactive;
                this._medicationsActive  = medSplit.active;
                this._medicationsInactive = medSplit.inactive;
                this._problemsActive     = probSplit.active;
                this._problemsInactive   = probSplit.inactive;

                // Stateless sections — single list
                this._familyHistory  = familyHx || [];
                this._socialHistory  = socialHx || [];
                this._immunizations  = immunizations || [];

                // Backwards-compat aliases used by other code paths in this module
                this._allergies      = this._allergiesActive;
                this._medications    = this._medicationsActive;
                this._problems       = this._problemsActive;
            } catch (e) {
                console.error('[EncounterWorkspace] History load error:', e);
            }

            const sections = this._getHistorySections();
            const hasAnyData = sections.some(s => (Array.isArray(s.activeData) && s.activeData.length > 0) || (Array.isArray(s.inactiveData) && s.inactiveData.length > 0));
            this._markStepComplete(1, hasAnyData);

            container.innerHTML = `
                <div class="mb-3">
                    <span class="text-muted">Review and update patient history for this visit</span>
                </div>
                ${sections.map(s => this._renderHistorySection(s)).join('')}
            `;

            // Bind all grid events
            this._bindHistoryGridEvents(container);

            // MEDOCS AI Voice Entry (MA/Nurse can use voice for history)
            this._initMedocsVoice('history');

            // Flush any pending voice data for history
            if (this._unifiedVoiceBar) this._unifiedVoiceBar.flushPendingHistory();
            if (this._telehealthScribeUI) this._telehealthScribeUI.flushPendingHistory();
        }

        /** Render a history section as a single grid: Active rows + collapsible Inactive sub-section.
         *  Spec: rules/technical/history-review-soft-delete.md (sections 6, 11).
         *  Encounter intake context = inactive sub-section collapsed by default.
         */
        _renderHistorySection(section) {
            const count = Array.isArray(section.activeData) ? section.activeData.length : 0;
            const inactiveCount = Array.isArray(section.inactiveData) ? section.inactiveData.length : 0;
            const columns = this._getGridColumns(section.key);
            const chips = HistoryPresets.CHIPS[section.key] || [];

            let chipsHtml = '';
            if (chips.length > 0) {
                chipsHtml = `<div class="ew-chips-row">
                    ${chips.map((c, i) => `<button class="ew-quick-chip ${c.autoSave ? 'auto-save' : ''}" data-section="${section.key}" data-chip-idx="${i}">${c.autoSave ? '<i class="bi bi-lightning-charge-fill"></i>' : ''}${this._escape(c.label)}</button>`).join('')}
                </div>`;
            }

            let rowsHtml = '';
            if (count > 0) {
                rowsHtml = section.activeData.map(item => this._renderGridRow(section.key, columns, item)).join('');
            }

            let socialPromptsHtml = '';
            if (section.key === 'socialHx') {
                socialPromptsHtml = `<div class="ew-social-prompts">Ask about: Tobacco · Alcohol · Drug Use · Exercise · Diet · Occupation · Sexual Activity</div>`;
            }

            const actionsWidth = '8%';
            const statusWidth = '7%';
            const addLabel = this._getAddLabel(section.key);

            // Inactive sub-section (only for sections with state).
            // Default collapsed on encounter History Review for fast intake review.
            let inactiveHtml = '';
            if (section.hasState) {
                inactiveHtml = this._renderInactiveSubSection(section, true /* defaultCollapsed */);
            }

            return `
                <div class="ew-history-card" data-section="${section.key}" id="ewSection_${section.key}">
                    <div class="ew-history-card-header">
                        <h6>
                            <i class="bi ${section.icon} text-${section.color}"></i>${section.label}
                            <span class="badge bg-${section.color} bg-opacity-10 text-${section.color}" id="ewCount_${section.key}">${count}</span>
                        </h6>
                        ${chipsHtml}
                    </div>
                    <div class="ew-history-card-body">
                        <table class="ew-grid" data-section="${section.key}">
                            <thead>
                                <tr>
                                    ${columns.map(col => `<th style="width:${col.width}"${col.required ? ' class="ew-col-required"' : ''}>${col.label}</th>`).join('')}
                                    <th style="width:${actionsWidth}"></th>
                                    <th style="width:${statusWidth}">Status</th>
                                </tr>
                            </thead>
                            <tbody id="ewGridBody_${section.key}">
                                ${rowsHtml}
                            </tbody>
                        </table>
                        ${socialPromptsHtml}
                        <button class="ew-add-btn" data-section="${section.key}" id="ewAddBtn_${section.key}"><i class="bi bi-plus-lg"></i> Add ${addLabel}</button>
                        <div class="ew-kb-hints d-none" id="ewKbHints_${section.key}"><kbd>Tab</kbd> next cell &nbsp; <kbd>Enter</kbd> save &nbsp; <kbd>Shift+Enter</kbd> save & new &nbsp; <kbd>Esc</kbd> cancel</div>
                        ${inactiveHtml}
                    </div>
                </div>
            `;
        }

        /** Render the collapsible "Inactive / Discontinued / Resolved" sub-section. */
        _renderInactiveSubSection(section, defaultCollapsed) {
            const items = section.inactiveData || [];
            const count = items.length;
            const label = this._inactiveSectionLabel(section.key);
            if (count === 0) {
                return `<div class="ew-history-inactive-empty text-muted small fst-italic mt-2">No ${label.toLowerCase()}.</div>`;
            }

            const collapsedClass = defaultCollapsed ? 'collapsed' : '';
            const headerIcon = defaultCollapsed ? 'bi-chevron-right' : 'bi-chevron-down';

            // Cap at 5 visible by default; reveal rest behind "Show all"
            const maxVisible = 5;
            const visibleItems = items.slice(0, maxVisible);
            const hiddenItems = items.slice(maxVisible);

            const visibleHtml = visibleItems.map(item => this._renderInactiveItem(section.key, item)).join('');
            const hiddenHtml = hiddenItems.map(item => this._renderInactiveItem(section.key, item)).join('');

            return `
                <div class="ew-history-inactive ${collapsedClass}" data-section="${section.key}" id="ewInactive_${section.key}">
                    <button type="button" class="ew-history-inactive-toggle" data-target="ewInactive_${section.key}">
                        <i class="bi ${headerIcon} ew-history-inactive-chevron"></i>
                        <span class="ew-history-inactive-label">${this._escape(label)}</span>
                        <span class="badge bg-secondary bg-opacity-10 text-secondary ms-2">${count}</span>
                    </button>
                    <div class="ew-history-inactive-body">
                        <div class="ew-history-inactive-list">
                            ${visibleHtml}
                            ${hiddenItems.length > 0 ? `<div class="ew-history-inactive-hidden d-none" data-section="${section.key}">${hiddenHtml}</div>` : ''}
                        </div>
                        ${hiddenItems.length > 0 ? `<button type="button" class="ew-history-inactive-showall" data-section="${section.key}">Show all (${count})</button>` : ''}
                    </div>
                </div>
            `;
        }

        /** Render a single read-only Inactive/Discontinued/Resolved item row.
         *  Strikethrough name + italic reason + Reactivate/Delete actions.
         */
        _renderInactiveItem(sectionKey, item) {
            const id = this._getItemId(sectionKey, item);
            let primary = '';
            let secondary = '';
            let stateLabel = '';

            switch (sectionKey) {
                case 'allergies':
                    primary = this._getFieldValue(item, 'allergenName') || '';
                    secondary = this._getFieldValue(item, 'notes') || '';
                    stateLabel = 'Inactive';
                    break;
                case 'medications': {
                    primary = this._getFieldValue(item, 'drugName') || '';
                    secondary = this._getFieldValue(item, 'notes') || '';
                    if (!secondary) {
                        const dosage = this._getFieldValue(item, 'dosage') || '';
                        const freq = this._getFieldValue(item, 'frequency') || '';
                        const route = this._getFieldValue(item, 'route') || '';
                        secondary = [dosage, route, freq].filter(Boolean).join(', ');
                    }
                    const status = item.Status ?? item.status ?? 0;
                    stateLabel = status === 1 ? 'Discontinued' : status === 3 ? 'Completed' : 'Inactive';
                    break;
                }
                case 'problems': {
                    primary = this._getFieldValue(item, 'description') || '';
                    secondary = '';
                    const icd = this._getFieldValue(item, 'icdCode') || '';
                    if (icd) secondary = `ICD: ${icd}`;
                    const s = item.Status ?? item.status ?? 0;
                    stateLabel = s === 1 ? 'Resolved' : 'Inactive';
                    break;
                }
                default:
                    primary = '';
            }

            const updatedAt = this._getFieldValue(item, 'updatedAt') || this._getFieldValue(item, 'createdAt');
            const dateStr = updatedAt ? new Date(updatedAt).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }) : '';

            // Action verbs depend on section
            let reactivateAction = '';
            if (sectionKey === 'allergies') reactivateAction = 'reactivate';
            else if (sectionKey === 'medications') reactivateAction = 'revertDiscontinue';
            else if (sectionKey === 'problems') reactivateAction = 'reactivate';

            const reactivateBtn = reactivateAction
                ? `<button class="btn btn-link btn-sm text-success p-0 ewReactivateBtn" data-section="${sectionKey}" data-action="${reactivateAction}" data-id="${id}" title="Reactivate"><i class="bi bi-arrow-counterclockwise"></i></button>`
                : '';

            return `
                <div class="ew-history-inactive-item" data-id="${id}" data-section="${sectionKey}">
                    <div class="ew-history-inactive-main">
                        <span class="ew-history-inactive-primary">${this._escape(primary)}</span>
                        ${secondary ? `<span class="ew-history-inactive-secondary">: ${this._escape(secondary)}</span>` : ''}
                        <span class="badge bg-secondary bg-opacity-10 text-secondary ms-2">${stateLabel}</span>
                    </div>
                    <div class="ew-history-inactive-meta">
                        ${dateStr ? `<span class="ew-history-inactive-date">${dateStr}</span>` : ''}
                        ${reactivateBtn}
                        <button class="btn btn-link btn-sm text-danger p-0 ewDeleteBtn" data-type="${sectionKey}" data-id="${id}" title="Delete"><i class="bi bi-trash"></i></button>
                    </div>
                </div>
            `;
        }

        /** Render a single previous record item (read-only) */
        _renderPreviousRecord(sectionKey, item, showDate = true) {
            let primary = '';
            let secondary = '';

            switch (sectionKey) {
                case 'allergies':
                    primary = this._getFieldValue(item, 'allergenName') || '';
                    secondary = this._getFieldValue(item, 'notes') || this._getFieldValue(item, 'reaction') || '';
                    if (!secondary) {
                        const typeName = this._getFieldValue(item, 'typeName') || '';
                        const sevName = this._getFieldValue(item, 'severityName') || '';
                        secondary = [typeName, sevName].filter(Boolean).join(', ');
                    }
                    break;
                case 'medications': {
                    primary = this._getFieldValue(item, 'drugName') || '';
                    secondary = this._getFieldValue(item, 'notes') || '';
                    if (!secondary) {
                        const dosage = this._getFieldValue(item, 'dosage') || '';
                        const freq = this._getFieldValue(item, 'frequency') || '';
                        const route = this._getFieldValue(item, 'route') || '';
                        secondary = [dosage, route, freq].filter(Boolean).join(', ');
                    }
                    // Medications get a dedicated renderer with status badge + action dropdown (Discontinue/Revert)
                    return this._renderPreviousMedication(item, primary, secondary);
                }
                case 'problems':
                    primary = this._getFieldValue(item, 'description') || '';
                    secondary = this._getFieldValue(item, 'notes') || '';
                    if (!secondary) {
                        const icd = this._getFieldValue(item, 'icdCode') || '';
                        secondary = icd ? `ICD: ${icd}` : '';
                    }
                    break;
                case 'familyHx':
                    primary = this._getFieldValue(item, 'condition') || '';
                    secondary = this._getFieldValue(item, 'notes') || '';
                    if (!secondary) {
                        const rel = this._getFieldValue(item, 'relation') || '';
                        const age = this._getFieldValue(item, 'ageAtOnset');
                        secondary = [rel, age ? `age ${age}` : ''].filter(Boolean).join(', ');
                    }
                    break;
                case 'socialHx':
                    primary = this._getFieldValue(item, 'category') || '';
                    secondary = this._getFieldValue(item, 'notes') || this._getFieldValue(item, 'description') || '';
                    break;
                case 'immunizations':
                    primary = this._getFieldValue(item, 'vaccineName') || '';
                    secondary = this._getFieldValue(item, 'notes') || '';
                    if (!secondary) {
                        const date = this._getFieldValue(item, 'administeredDate');
                        secondary = date ? new Date(date).toLocaleDateString() : '';
                    }
                    break;
            }

            // Show date from CreatedAt
            const createdAt = this._getFieldValue(item, 'createdAt');
            const dateStr = createdAt ? new Date(createdAt).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }) : '';

            return `<div class="ew-prev-record-item">
                <div class="ew-prev-record-main">
                    <span class="ew-prev-primary">${this._escape(primary)}</span>
                    ${secondary ? `<span class="ew-prev-secondary"> — ${this._escape(secondary)}</span>` : ''}
                </div>
                ${dateStr ? `<span class="ew-prev-date">${dateStr}</span>` : ''}
            </div>`;
        }

        /**
         * Render a previous medication record with status badge and Discontinue/Revert action.
         * Status: 0=Active, 1=Discontinued, 2=OnHold, 3=Completed
         */
        _renderPreviousMedication(item, primary, secondary) {
            const id = this._getItemId('medications', item);
            const status = item.Status ?? item.status ?? 0;
            const isDiscontinued = status === 1;

            const createdAt = this._getFieldValue(item, 'createdAt');
            const dateStr = createdAt ? new Date(createdAt).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }) : '';

            // Status badge: only render a visible badge for non-active statuses
            let statusBadge = '';
            if (isDiscontinued) {
                statusBadge = `<span class="badge bg-secondary-subtle text-secondary ms-1">Discontinued</span>`;
            } else if (status === 2) {
                statusBadge = `<span class="badge bg-warning-subtle text-warning ms-1">On Hold</span>`;
            } else if (status === 3) {
                statusBadge = `<span class="badge bg-info-subtle text-info ms-1">Completed</span>`;
            }

            // Action menu: Discontinue (if active) or Revert (if discontinued)
            const actionItem = isDiscontinued
                ? `<li><a class="dropdown-item small ewRevertMedBtn" href="#" data-id="${id}"><i class="bi bi-arrow-counterclockwise me-1"></i>Revert (Reactivate)</a></li>`
                : `<li><a class="dropdown-item small ewDiscontinuePrevMedBtn" href="#" data-id="${id}"><i class="bi bi-x-circle me-1"></i>Discontinue</a></li>`;

            return `<div class="ew-prev-record-item" data-prev-med-id="${id}">
                <div class="ew-prev-record-main">
                    <span class="ew-prev-primary">${this._escape(primary)}</span>
                    ${secondary ? `<span class="ew-prev-secondary"> — ${this._escape(secondary)}</span>` : ''}
                    ${statusBadge}
                </div>
                <div class="d-flex align-items-center gap-1">
                    ${dateStr ? `<span class="ew-prev-date">${dateStr}</span>` : ''}
                    <div class="dropdown">
                        <button class="btn btn-link p-0 text-muted" data-bs-toggle="dropdown" title="Actions"><i class="bi bi-three-dots-vertical"></i></button>
                        <ul class="dropdown-menu dropdown-menu-end">
                            ${actionItem}
                        </ul>
                    </div>
                </div>
            </div>`;
        }

        /** Deduplicate previous records by primary field — keep most recent */
        _deduplicatePreviousRecords(sectionKey, items) {
            if (!Array.isArray(items) || items.length === 0) return [];
            const primaryFields = {
                allergies: 'allergenName', medications: 'drugName', problems: 'description',
                familyHx: 'condition', socialHx: 'category', immunizations: 'vaccineName'
            };
            const pf = primaryFields[sectionKey];
            if (!pf) return items;

            const seen = new Map();
            for (const item of items) {
                const key = (this._getFieldValue(item, pf) || '').toString().trim().toLowerCase();
                if (!key) continue;
                // Keep the first occurrence (items are ordered by most recent from API)
                if (!seen.has(key)) seen.set(key, item);
            }
            return Array.from(seen.values());
        }

        /** Render an existing data row with editable cells */
        _renderGridRow(sectionKey, columns, item) {
            const id = this._getItemId(sectionKey, item);

            let cellsHtml = columns.map(col => {
                const val = this._getFieldValue(item, col.field);

                if (col.type === 'select') {
                    const optionsHtml = (col.options || []).map(o =>
                        `<option value="${this._escapeAttr(String(o.v))}"${o.v == val ? ' selected' : ''}>${this._escape(o.l)}</option>`
                    ).join('');
                    return `<td><select class="ew-cell-select" data-field="${col.field}">${optionsHtml}</select></td>`;
                } else if (col.type === 'checkbox') {
                    return `<td><input type="checkbox" class="ew-cell-checkbox" data-field="${col.field}" ${val ? 'checked' : ''}></td>`;
                } else if (col.type === 'date') {
                    const dateVal = val ? new Date(val).toISOString().split('T')[0] : '';
                    return `<td><input type="date" class="ew-cell-input" data-field="${col.field}" value="${this._escapeAttr(dateVal)}"></td>`;
                } else if (col.type === 'number') {
                    return `<td><input type="number" class="ew-cell-input" data-field="${col.field}" value="${this._escapeAttr(val != null ? String(val) : '')}" placeholder="${col.placeholder || ''}"></td>`;
                } else {
                    // text input — wrap in ac-container if autocomplete
                    const acId = col.autocomplete ? `ewAc_${sectionKey}_${id}_${col.field}` : '';
                    const wrapStart = col.autocomplete ? `<div class="ew-ac-container">` : '';
                    const wrapEnd = col.autocomplete ? `<div class="ew-ac-results d-none" id="${acId}"></div></div>` : '';
                    return `<td>${wrapStart}<input type="text" class="ew-cell-input" data-field="${col.field}" value="${this._escapeAttr(val || '')}" placeholder="${col.placeholder || ''}" ${col.autocomplete ? `data-ac="${col.autocomplete}" data-ac-id="${acId}"` : ''}>${wrapEnd}</td>`;
                }
            }).join('');

            // Actions column. Per rules/technical/history-review-soft-delete.md, every
            // state change AND delete goes through HistoryActionModal so reason + audit
            // are captured. Verbs differ by section.
            let actionsHtml;
            if (sectionKey === 'medications') {
                // NOTE: "Mark On Hold" and "Complete" are intentionally hidden from
                // the UI for now (per stakeholder decision 2026-04-25). The backend
                // service (HistoryReviewActionService) and API endpoints
                // (POST /api/history-review/medication/mark-on-hold, .../complete,
                // .../resume-from-hold) remain implemented. To re-enable, just
                // restore the two <li> entries below. DB Status enum already
                // supports OnHold (2) and Completed (3).
                actionsHtml = `<td class="ew-cell-actions">
                    <div class="dropdown">
                        <button class="btn btn-link text-muted p-0" data-bs-toggle="dropdown" title="Actions"><i class="bi bi-three-dots-vertical"></i></button>
                        <ul class="dropdown-menu dropdown-menu-end">
                            <li><a class="dropdown-item small ewActionBtn" href="#" data-section="${sectionKey}" data-action="discontinue" data-id="${id}"><i class="bi bi-x-circle me-1"></i>Discontinue</a></li>
                            <li><hr class="dropdown-divider"></li>
                            <li><a class="dropdown-item small text-danger ewDeleteBtn" href="#" data-type="${sectionKey}" data-id="${id}"><i class="bi bi-trash me-1"></i>Delete</a></li>
                        </ul>
                    </div>
                </td>`;
            } else if (sectionKey === 'problems') {
                actionsHtml = `<td class="ew-cell-actions">
                    <div class="dropdown">
                        <button class="btn btn-link text-muted p-0" data-bs-toggle="dropdown" title="Actions"><i class="bi bi-three-dots-vertical"></i></button>
                        <ul class="dropdown-menu dropdown-menu-end">
                            <li><a class="dropdown-item small ewActionBtn" href="#" data-section="${sectionKey}" data-action="markResolved" data-id="${id}"><i class="bi bi-check-circle me-1"></i>Mark Resolved</a></li>
                            <li><a class="dropdown-item small ewActionBtn" href="#" data-section="${sectionKey}" data-action="inactivate" data-id="${id}"><i class="bi bi-pause-circle me-1"></i>Mark Inactive</a></li>
                            <li><hr class="dropdown-divider"></li>
                            <li><a class="dropdown-item small text-danger ewDeleteBtn" href="#" data-type="${sectionKey}" data-id="${id}"><i class="bi bi-trash me-1"></i>Delete</a></li>
                        </ul>
                    </div>
                </td>`;
            } else if (sectionKey === 'allergies') {
                actionsHtml = `<td class="ew-cell-actions">
                    <div class="dropdown">
                        <button class="btn btn-link text-muted p-0" data-bs-toggle="dropdown" title="Actions"><i class="bi bi-three-dots-vertical"></i></button>
                        <ul class="dropdown-menu dropdown-menu-end">
                            <li><a class="dropdown-item small ewActionBtn" href="#" data-section="${sectionKey}" data-action="inactivate" data-id="${id}"><i class="bi bi-pause-circle me-1"></i>Mark Inactive</a></li>
                            <li><hr class="dropdown-divider"></li>
                            <li><a class="dropdown-item small text-danger ewDeleteBtn" href="#" data-type="${sectionKey}" data-id="${id}"><i class="bi bi-trash me-1"></i>Delete</a></li>
                        </ul>
                    </div>
                </td>`;
            } else {
                // familyHx, socialHx, immunizations: Delete only
                actionsHtml = `<td class="ew-cell-actions"><button class="btn btn-link text-danger p-0 ewDeleteBtn" data-type="${sectionKey}" data-id="${id}" title="Delete"><i class="bi bi-trash"></i></button></td>`;
            }

            // Status column
            const statusHtml = `<td class="ew-save-status"><span class="ew-status-idle"><i class="bi bi-check-lg"></i></span></td>`;

            return `<tr data-id="${id}" data-section="${sectionKey}">${cellsHtml}${actionsHtml}${statusHtml}</tr>`;
        }

        /** Render an add row (on-demand, not always visible) */
        _renderAddRow(sectionKey, columns) {
            const today = new Date().toISOString().split('T')[0];

            let cellsHtml = columns.map(col => {
                if (col.type === 'select') {
                    const placeholder = `<option value="" selected disabled>${col.label || 'Select'}...</option>`;
                    const optionsHtml = (col.options || []).map(o =>
                        `<option value="${this._escapeAttr(String(o.v))}">${this._escape(o.l)}</option>`
                    ).join('');
                    return `<td><select class="ew-cell-select" data-field="${col.field}">${placeholder}${optionsHtml}</select></td>`;
                } else if (col.type === 'checkbox') {
                    return `<td><input type="checkbox" class="ew-cell-checkbox" data-field="${col.field}"></td>`;
                } else if (col.type === 'date') {
                    const dateVal = col.default === 'today' ? today : '';
                    return `<td><input type="date" class="ew-cell-input" data-field="${col.field}" value="${dateVal}"></td>`;
                } else if (col.type === 'number') {
                    return `<td><input type="number" class="ew-cell-input" data-field="${col.field}" placeholder="${col.placeholder || ''}"></td>`;
                } else {
                    // text input with optional autocomplete
                    const uid = `ewAcNew_${sectionKey}_${col.field}_${Date.now()}`;
                    const acId = col.autocomplete ? uid : '';
                    const wrapStart = col.autocomplete ? `<div class="ew-ac-container">` : '';
                    const wrapEnd = col.autocomplete ? `<div class="ew-ac-results d-none" id="${acId}"></div></div>` : '';
                    return `<td>${wrapStart}<input type="text" class="ew-cell-input" data-field="${col.field}" placeholder="${col.placeholder || ''}" ${col.autocomplete ? `data-ac="${col.autocomplete}" data-ac-id="${acId}"` : ''}>${wrapEnd}</td>`;
                }
            }).join('');

            // Action buttons: Save, Save & New, Cancel
            const actionsHtml = `<td class="ew-cell-actions"><div class="ew-add-row-actions">
                <button class="ew-btn-save-row" title="Save (Enter)"><i class="bi bi-check-lg"></i></button>
                <button class="ew-btn-save-new" title="Save & New (Shift+Enter)"><i class="bi bi-plus-lg"></i></button>
                <button class="ew-btn-cancel-row" title="Cancel (Esc)"><i class="bi bi-x-lg"></i></button>
            </div></td>`;
            const statusHtml = '<td class="ew-save-status"></td>';

            return `<tr class="ew-row-new ew-row-adding" data-section="${sectionKey}" data-new="true">${cellsHtml}${actionsHtml}${statusHtml}</tr>`;
        }

        /** Get contextual label for the + Add button */
        _getAddLabel(sectionKey) {
            const labels = {
                allergies: 'Allergy',
                medications: 'Medication',
                problems: 'Problem',
                familyHx: 'Family History',
                socialHx: 'Social History',
                immunizations: 'Immunization'
            };
            return labels[sectionKey] || 'Item';
        }

        /** Open an add row in a section */
        _openAddRow(sectionKey) {
            const tbody = document.getElementById(`ewGridBody_${sectionKey}`);
            if (!tbody) return;

            // If add row already exists, just focus it
            const existing = tbody.querySelector('tr[data-new="true"]');
            if (existing) {
                const columns = this._getGridColumns(sectionKey);
                const reqCol = columns.find(c => c.required);
                if (reqCol) {
                    const reqInput = existing.querySelector(`[data-field="${reqCol.field}"]`);
                    if (reqInput) reqInput.focus();
                }
                return;
            }

            // Uncollapse the card if collapsed
            const card = document.getElementById(`ewSection_${sectionKey}`);
            if (card && card.classList.contains('collapsed')) card.classList.remove('collapsed');

            // Render and insert the add row
            const columns = this._getGridColumns(sectionKey);
            const rowHtml = this._renderAddRow(sectionKey, columns);
            tbody.insertAdjacentHTML('beforeend', rowHtml);

            // Init autocomplete on the new row
            const addRow = tbody.querySelector('tr[data-new="true"]');
            if (addRow) {
                addRow.querySelectorAll('input[data-ac]').forEach(inp => this._initGridAutocomplete(inp));

                // Focus the first required field
                const reqCol = columns.find(c => c.required);
                if (reqCol) {
                    const reqInput = addRow.querySelector(`[data-field="${reqCol.field}"]`);
                    if (reqInput) reqInput.focus();
                }
            }

            // Hide the + Add button, show keyboard hints
            const addBtn = document.getElementById(`ewAddBtn_${sectionKey}`);
            if (addBtn) addBtn.classList.add('d-none');
            const kbHints = document.getElementById(`ewKbHints_${sectionKey}`);
            if (kbHints) kbHints.classList.remove('d-none');
        }

        /** Close (remove) the add row in a section */
        _closeAddRow(sectionKey) {
            const tbody = document.getElementById(`ewGridBody_${sectionKey}`);
            if (tbody) {
                const addRow = tbody.querySelector('tr[data-new="true"]');
                if (addRow) addRow.remove();
            }

            // Show the + Add button, hide keyboard hints
            const addBtn = document.getElementById(`ewAddBtn_${sectionKey}`);
            if (addBtn) addBtn.classList.remove('d-none');
            const kbHints = document.getElementById(`ewKbHints_${sectionKey}`);
            if (kbHints) kbHints.classList.add('d-none');
        }

        /**
         * Check if a row with the same key data already exists in the grid.
         * Used to prevent duplicate entries (both manual and voice).
         * @param {string} sectionKey - e.g. 'allergies', 'medications'
         * @param {Object} data - the data being saved (from _collectRowData)
         * @returns {boolean} true if a duplicate exists
         */
        _checkDuplicateInGrid(sectionKey, data) {
            const tbody = document.getElementById(`ewGridBody_${sectionKey}`);
            if (!tbody) return false;

            const existingRows = tbody.querySelectorAll('tr[data-id]');
            if (existingRows.length === 0) return false;

            const norm = (val) => (val || '').toString().trim().toLowerCase();

            for (const row of existingRows) {
                const getVal = (fieldName) => {
                    const el = row.querySelector(`[data-field="${fieldName}"]`);
                    if (!el) return '';
                    if (el.type === 'checkbox') return el.checked;
                    return norm(el.value);
                };

                // Check duplicate by primary field only
                let isDup = false;
                const primaryFields = {
                    allergies: 'allergenName',
                    medications: 'drugName',
                    problems: 'description',
                    familyHx: 'condition',
                    socialHx: 'category',
                    immunizations: 'vaccineName'
                };
                const pf = primaryFields[sectionKey];
                if (pf) isDup = norm(data[pf]) === getVal(pf);

                if (isDup) return true;
            }

            return false;
        }

        // ─── GRID EVENT BINDING ──────────────────────────────────────

        /** Bind all events for the history grids.
         *  IMPORTANT: This is called every time _renderHistoryStep runs (initial
         *  load + after every state-change/delete action). The container element
         *  itself persists across re-renders (only its innerHTML is replaced),
         *  so naively re-adding delegated listeners would stack them and each
         *  click would fire 2×, 3×, etc. The flag below ensures the delegated
         *  listeners are bound exactly once per container. Per-element init
         *  (e.g. autocomplete) still re-runs on every render because new DOM
         *  elements need it. */
        _bindHistoryGridEvents(container) {
            if (container._ewHistoryEventsBound) {
                // Re-init autocomplete for the freshly-rendered inputs only;
                // delegated listeners already in place from first call.
                container.querySelectorAll('input[data-ac]').forEach(input => {
                    this._initGridAutocomplete(input);
                });
                return;
            }
            container._ewHistoryEventsBound = true;
            this._bindHistoryGridEventsImpl(container);
        }

        _bindHistoryGridEventsImpl(container) {
            // Delegate: text input changes (debounced auto-save for existing rows)
            container.addEventListener('input', (e) => {
                const input = e.target;
                if (!input.matches('.ew-cell-input') && !input.matches('.ew-cell-checkbox')) return;
                const row = input.closest('tr');
                if (!row) return;

                const sectionKey = row.dataset.section;
                const isNew = row.dataset.new === 'true';
                const field = input.dataset.field;

                if (input.dataset.ac) {
                    this._handleGridAutocomplete(input, sectionKey);
                }

                if (isNew) {
                    // For new rows, check if required field is filled to trigger save on blur/Enter
                    return;
                }

                // Existing row — debounced PUT
                const timerId = `${sectionKey}_${row.dataset.id}_${field}`;
                if (this._historyDebounceTimers[timerId]) clearTimeout(this._historyDebounceTimers[timerId]);
                this._historyDebounceTimers[timerId] = setTimeout(() => {
                    this._autoSaveExistingRow(sectionKey, row);
                }, 800);
            });

            // Delegate: select changes (immediate save for existing rows)
            container.addEventListener('change', (e) => {
                const el = e.target;
                const row = el.closest('tr');
                if (!row) return;
                const sectionKey = row.dataset.section;
                const isNew = row.dataset.new === 'true';

                if (el.matches('.ew-cell-select') && !isNew) {
                    this._autoSaveExistingRow(sectionKey, row);
                }
                if (el.matches('.ew-cell-checkbox') && !isNew) {
                    this._autoSaveExistingRow(sectionKey, row);
                }
            });

            // Delegate: keyboard — Enter to save new row, Shift+Enter to save & new, Escape to cancel
            container.addEventListener('keydown', (e) => {
                const row = e.target.closest('tr');
                if (!row) return;
                const sectionKey = row.dataset.section;
                const isNew = row.dataset.new === 'true';

                if (e.key === 'Enter' && isNew) {
                    // Don't save if an autocomplete dropdown is currently visible
                    const openAc = row.querySelector('.ew-ac-results:not(.d-none)');
                    if (openAc) return;
                    // Don't save if autocomplete just selected an item
                    if (row._acJustSelected) return;
                    e.preventDefault();
                    if (e.shiftKey) {
                        // Shift+Enter = Save & open new row
                        this._saveNewRow(sectionKey, row).then(saved => {
                            if (saved) this._openAddRow(sectionKey);
                        });
                    } else {
                        // Enter = Save & close
                        this._saveNewRow(sectionKey, row).then(saved => {
                            if (saved) this._closeAddRow(sectionKey);
                        });
                    }
                } else if (e.key === 'Escape') {
                    e.preventDefault();
                    if (isNew) {
                        // Cancel — remove the add row
                        this._closeAddRow(sectionKey);
                    } else {
                        // Revert — re-render the whole step (simplest approach)
                        e.target.blur();
                    }
                }
            });

            // Delegate: state-change + delete buttons.
            // All of these route through HistoryActionModal which captures a required
            // reason and posts to the /api/history-review/{section}/{action} endpoint.
            container.addEventListener('click', (e) => {
                const actionBtn = e.target.closest('.ewActionBtn');
                if (actionBtn) {
                    e.preventDefault();
                    this._openHistoryActionModal(actionBtn.dataset.section, actionBtn.dataset.action, parseInt(actionBtn.dataset.id));
                    return;
                }

                const reactivateBtn = e.target.closest('.ewReactivateBtn');
                if (reactivateBtn) {
                    e.preventDefault();
                    this._openHistoryActionModal(reactivateBtn.dataset.section, reactivateBtn.dataset.action, parseInt(reactivateBtn.dataset.id));
                    return;
                }

                const deleteBtn = e.target.closest('.ewDeleteBtn');
                if (deleteBtn) {
                    e.preventDefault();
                    const type = deleteBtn.dataset.type;
                    const id = deleteBtn.dataset.id;
                    if (type && id) this._openHistoryActionModal(type, 'delete', parseInt(id));
                    return;
                }

                // Toggle inactive sub-section
                const toggleBtn = e.target.closest('.ew-history-inactive-toggle');
                if (toggleBtn) {
                    e.preventDefault();
                    const wrap = document.getElementById(toggleBtn.dataset.target);
                    if (wrap) {
                        wrap.classList.toggle('collapsed');
                        const chev = toggleBtn.querySelector('.ew-history-inactive-chevron');
                        if (chev) {
                            chev.classList.toggle('bi-chevron-right', wrap.classList.contains('collapsed'));
                            chev.classList.toggle('bi-chevron-down', !wrap.classList.contains('collapsed'));
                        }
                    }
                    return;
                }

                // "Show all" inside inactive sub-section
                const showAllBtn = e.target.closest('.ew-history-inactive-showall');
                if (showAllBtn) {
                    e.preventDefault();
                    const sectionKey = showAllBtn.dataset.section;
                    const hidden = container.querySelector(`.ew-history-inactive-hidden[data-section="${sectionKey}"]`);
                    if (hidden) hidden.classList.remove('d-none');
                    showAllBtn.remove();
                    return;
                }

                // Quick-add chips
                const chip = e.target.closest('.ew-quick-chip');
                if (chip) {
                    e.preventDefault();
                    this._handleQuickChip(chip.dataset.section, parseInt(chip.dataset.chipIdx));
                    return;
                }

                // + Add button
                const addBtn = e.target.closest('.ew-add-btn');
                if (addBtn) {
                    e.preventDefault();
                    this._openAddRow(addBtn.dataset.section);
                    return;
                }

                // Save button in add row
                const saveBtn = e.target.closest('.ew-btn-save-row');
                if (saveBtn) {
                    e.preventDefault();
                    const row = saveBtn.closest('tr[data-new="true"]');
                    if (row) {
                        const sk = row.dataset.section;
                        this._saveNewRow(sk, row).then(saved => {
                            if (saved) this._closeAddRow(sk);
                        });
                    }
                    return;
                }

                // Save & New button in add row
                const saveNewBtn = e.target.closest('.ew-btn-save-new');
                if (saveNewBtn) {
                    e.preventDefault();
                    const row = saveNewBtn.closest('tr[data-new="true"]');
                    if (row) {
                        const sk = row.dataset.section;
                        this._saveNewRow(sk, row).then(saved => {
                            if (saved) this._openAddRow(sk);
                        });
                    }
                    return;
                }

                // Cancel button in add row
                const cancelBtn = e.target.closest('.ew-btn-cancel-row');
                if (cancelBtn) {
                    e.preventDefault();
                    const row = cancelBtn.closest('tr[data-new="true"]');
                    if (row) this._closeAddRow(row.dataset.section);
                    return;
                }

                // Collapsed card header click — expand
                const card = e.target.closest('.ew-history-card.collapsed');
                if (card) {
                    card.classList.remove('collapsed');
                }
            });

            // Init autocomplete for existing rows
            container.querySelectorAll('input[data-ac]').forEach(input => {
                this._initGridAutocomplete(input);
            });

        }

        // ─── AUTO-SAVE (EXISTING ROW — PUT) ──────────────────────────

        /** Collect all field values from a row */
        _collectRowData(sectionKey, row) {
            const columns = this._getGridColumns(sectionKey);
            const data = {};
            columns.forEach(col => {
                const el = row.querySelector(`[data-field="${col.field}"]`);
                if (!el) return;
                if (col.type === 'checkbox') {
                    data[col.field] = el.checked;
                } else if (col.type === 'number') {
                    data[col.field] = el.value ? parseInt(el.value) : null;
                } else if (col.type === 'select') {
                    const val = el.value;
                    if (val === '') {
                        // Placeholder selected — use default or null
                        data[col.field] = col.default != null ? col.default : null;
                    } else if (col.options && col.options.length > 0 && typeof col.options[0].v === 'number') {
                        // If select has numeric values (type, severity), parse as int
                        data[col.field] = parseInt(val);
                    } else {
                        data[col.field] = val;
                    }
                } else {
                    data[col.field] = el.value?.trim() || null;
                }
            });
            return data;
        }

        /** Auto-save an existing row via PUT */
        async _autoSaveExistingRow(sectionKey, row) {
            const id = row.dataset.id;
            if (!id) return;

            const section = this._getHistorySections().find(s => s.key === sectionKey);
            if (!section) return;

            const data = this._collectRowData(sectionKey, row);
            this._showRowSaveStatus(row, 'saving');

            try {
                await window.apiRequest(`/patients/${this.patientId}/${section.endpoint}/${id}`, {
                    method: 'PUT',
                    body: data,
                    showLoader: false
                });
                this._showRowSaveStatus(row, 'saved');
            } catch (e) {
                console.error(`[EncounterWorkspace] Auto-save ${sectionKey} error:`, e);
                this._showRowSaveStatus(row, 'error');
            }
        }

        // ─── AUTO-SAVE (NEW ROW — POST) ──────────────────────────────

        /** Save a new row via POST, then convert it to a data row.
         *  Returns true on success, false on failure/skip. */
        async _saveNewRow(sectionKey, row) {
            // Prevent double-save
            if (row._saving) return false;

            const columns = this._getGridColumns(sectionKey);
            const reqCol = columns.find(c => c.required);
            if (reqCol) {
                const reqInput = row.querySelector(`[data-field="${reqCol.field}"]`);
                if (!reqInput || !reqInput.value.trim()) return false; // Required field empty
            }

            row._saving = true;
            const section = this._getHistorySections().find(s => s.key === sectionKey);
            if (!section) return false;

            const data = this._collectRowData(sectionKey, row);

            // Duplicate check: prevent saving if same key data already exists
            if (this._checkDuplicateInGrid(sectionKey, data)) {
                row._saving = false;
                if (window.showToast) window.showToast('This item already exists', 'warning');
                return false;
            }

            // Always include encounterId
            data.encounterId = this.encounterId;

            // Add default fields for specific sections
            if (sectionKey === 'medications') {
                data.startDate = data.startDate || new Date().toISOString().split('T')[0];
            }
            if (sectionKey === 'immunizations' && !data.administeredDate) {
                data.administeredDate = new Date().toISOString().split('T')[0];
            }

            this._showRowSaveStatus(row, 'saving');

            try {
                const result = await window.apiRequest(`/patients/${this.patientId}/${section.endpoint}`, {
                    method: 'POST',
                    body: data,
                    showLoader: false
                });

                // Get the new ID from the response
                const newId = this._getItemId(sectionKey, result) || result?.id || result?.Id;

                // Convert this row from new → data row
                row.dataset.id = newId;
                delete row.dataset.new;
                row.classList.remove('ew-row-new');
                row.classList.remove('ew-row-adding');
                row._saving = false;

                // Update select elements: remove placeholder option, set value from saved data
                row.querySelectorAll('.ew-cell-select').forEach(sel => {
                    const placeholder = sel.querySelector('option[disabled]');
                    if (placeholder) placeholder.remove();
                    const field = sel.dataset.field;
                    const savedVal = this._getFieldValue(result, field) ?? data[field];
                    if (savedVal != null) sel.value = String(savedVal);
                });

                // Replace add-row action buttons with standard data-row actions.
                // Per rules/technical/history-review-soft-delete.md, every state change
                // and delete now goes through HistoryActionModal — same buttons as
                // _renderGridRow.
                const actionsCell = row.querySelector('.ew-cell-actions');
                if (actionsCell) {
                    if (sectionKey === 'medications') {
                        // NOTE: "Mark On Hold" and "Complete" are intentionally hidden from
                        // the UI for now (per stakeholder decision 2026-04-25). Backend
                        // remains implemented (see HistoryReviewActionService). To
                        // re-enable, restore the two <li> entries here AND in
                        // _renderGridRow above. DB Status enum supports OnHold (2)
                        // and Completed (3).
                        actionsCell.innerHTML = `<div class="dropdown">
                            <button class="btn btn-link text-muted p-0" data-bs-toggle="dropdown" title="Actions"><i class="bi bi-three-dots-vertical"></i></button>
                            <ul class="dropdown-menu dropdown-menu-end">
                                <li><a class="dropdown-item small ewActionBtn" href="#" data-section="${sectionKey}" data-action="discontinue" data-id="${newId}"><i class="bi bi-x-circle me-1"></i>Discontinue</a></li>
                                <li><hr class="dropdown-divider"></li>
                                <li><a class="dropdown-item small text-danger ewDeleteBtn" href="#" data-type="${sectionKey}" data-id="${newId}"><i class="bi bi-trash me-1"></i>Delete</a></li>
                            </ul>
                        </div>`;
                    } else if (sectionKey === 'problems') {
                        actionsCell.innerHTML = `<div class="dropdown">
                            <button class="btn btn-link text-muted p-0" data-bs-toggle="dropdown" title="Actions"><i class="bi bi-three-dots-vertical"></i></button>
                            <ul class="dropdown-menu dropdown-menu-end">
                                <li><a class="dropdown-item small ewActionBtn" href="#" data-section="${sectionKey}" data-action="markResolved" data-id="${newId}"><i class="bi bi-check-circle me-1"></i>Mark Resolved</a></li>
                                <li><a class="dropdown-item small ewActionBtn" href="#" data-section="${sectionKey}" data-action="inactivate" data-id="${newId}"><i class="bi bi-pause-circle me-1"></i>Mark Inactive</a></li>
                                <li><hr class="dropdown-divider"></li>
                                <li><a class="dropdown-item small text-danger ewDeleteBtn" href="#" data-type="${sectionKey}" data-id="${newId}"><i class="bi bi-trash me-1"></i>Delete</a></li>
                            </ul>
                        </div>`;
                    } else if (sectionKey === 'allergies') {
                        actionsCell.innerHTML = `<div class="dropdown">
                            <button class="btn btn-link text-muted p-0" data-bs-toggle="dropdown" title="Actions"><i class="bi bi-three-dots-vertical"></i></button>
                            <ul class="dropdown-menu dropdown-menu-end">
                                <li><a class="dropdown-item small ewActionBtn" href="#" data-section="${sectionKey}" data-action="inactivate" data-id="${newId}"><i class="bi bi-pause-circle me-1"></i>Mark Inactive</a></li>
                                <li><hr class="dropdown-divider"></li>
                                <li><a class="dropdown-item small text-danger ewDeleteBtn" href="#" data-type="${sectionKey}" data-id="${newId}"><i class="bi bi-trash me-1"></i>Delete</a></li>
                            </ul>
                        </div>`;
                    } else {
                        actionsCell.innerHTML = `<button class="btn btn-link text-danger p-0 ewDeleteBtn" data-type="${sectionKey}" data-id="${newId}" title="Delete"><i class="bi bi-trash"></i></button>`;
                    }
                }

                this._showRowSaveStatus(row, 'saved');
                this._updateSectionCount(sectionKey, 1);

                // Show + Add button again (caller decides whether to open new row)
                const addBtn = document.getElementById(`ewAddBtn_${sectionKey}`);
                if (addBtn) addBtn.classList.remove('d-none');
                const kbHints = document.getElementById(`ewKbHints_${sectionKey}`);
                if (kbHints) kbHints.classList.add('d-none');

                // Update step complete status
                this._markStepComplete(1, true);

                return true;

            } catch (e) {
                console.error(`[EncounterWorkspace] Save new ${sectionKey} error:`, e);
                this._showRowSaveStatus(row, 'error');
                row._saving = false;
                return false;
            }
        }

        // ─── SAVE STATUS INDICATOR ───────────────────────────────────

        /** Update save status indicator in a row's last cell */
        _showRowSaveStatus(row, status) {
            const cell = row.querySelector('.ew-save-status');
            if (!cell) return;

            switch (status) {
                case 'saving':
                    cell.innerHTML = `<span class="ew-status-saving"><span class="spinner-border spinner-border-sm" style="width:10px;height:10px;"></span> Saving</span>`;
                    break;
                case 'saved':
                    cell.innerHTML = `<span class="ew-status-saved"><i class="bi bi-check-lg"></i> Saved</span>`;
                    // After animation completes, show idle state
                    setTimeout(() => {
                        if (cell.querySelector('.ew-status-saved')) {
                            cell.innerHTML = `<span class="ew-status-idle"><i class="bi bi-check-lg"></i></span>`;
                        }
                    }, 2000);
                    break;
                case 'error':
                    cell.innerHTML = `<span class="ew-status-error"><i class="bi bi-x-lg"></i> Error</span>`;
                    break;
                case 'idle':
                    cell.innerHTML = `<span class="ew-status-idle"><i class="bi bi-check-lg"></i></span>`;
                    break;
                default:
                    cell.innerHTML = '';
            }
        }

        // ─── SECTION COUNT UPDATE ────────────────────────────────────

        /** Update badge count for a section */
        _updateSectionCount(sectionKey, delta) {
            const badge = document.getElementById(`ewCount_${sectionKey}`);
            if (badge) {
                const current = parseInt(badge.textContent) || 0;
                badge.textContent = Math.max(0, current + delta);
            }
            // Also uncollapse the card if it was collapsed
            const card = document.getElementById(`ewSection_${sectionKey}`);
            if (card && delta > 0) card.classList.remove('collapsed');
        }

        // ─── QUICK-ADD CHIPS ─────────────────────────────────────────

        /** Handle click on a quick-add chip */
        async _handleQuickChip(sectionKey, chipIdx) {
            const chips = HistoryPresets.CHIPS[sectionKey];
            if (!chips || !chips[chipIdx]) return;
            const chip = chips[chipIdx];

            if (chip.autoSave) {
                // Direct POST — no form interaction
                const section = this._getHistorySections().find(s => s.key === sectionKey);
                if (!section) return;

                const data = { ...chip.data };
                // Always include encounterId
                data.encounterId = this.encounterId;
                // Add defaults
                if (sectionKey === 'medications') data.startDate = new Date().toISOString().split('T')[0];
                if (sectionKey === 'immunizations') data.administeredDate = data.administeredDate || new Date().toISOString().split('T')[0];

                // Duplicate check for quick chips
                if (this._checkDuplicateInGrid(sectionKey, data)) {
                    if (window.showToast) window.showToast('This item already exists', 'warning');
                    return;
                }

                try {
                    const result = await window.apiRequest(`/patients/${this.patientId}/${section.endpoint}`, {
                        method: 'POST',
                        body: data,
                        showLoader: false
                    });

                    // Insert the new data row into the grid
                    const tbody = document.getElementById(`ewGridBody_${sectionKey}`);
                    if (tbody) {
                        const addRow = tbody.querySelector('tr[data-new="true"]');
                        const columns = this._getGridColumns(sectionKey);
                        const newRowHtml = this._renderGridRow(sectionKey, columns, result);
                        if (addRow) {
                            addRow.insertAdjacentHTML('beforebegin', newRowHtml);
                        } else {
                            tbody.insertAdjacentHTML('beforeend', newRowHtml);
                        }
                        // Show saved status on the new row
                        const insertedRow = tbody.querySelector(`tr[data-id="${this._getItemId(sectionKey, result)}"]`);
                        if (insertedRow) {
                            this._showRowSaveStatus(insertedRow, 'saved');
                            // Init autocomplete on new row
                            insertedRow.querySelectorAll('input[data-ac]').forEach(inp => this._initGridAutocomplete(inp));
                        }
                    }

                    this._updateSectionCount(sectionKey, 1);
                    this._markStepComplete(1, true);
                    // Silent success — no toast
                } catch (e) {
                    console.error(`[EncounterWorkspace] Quick-add ${sectionKey} error:`, e);
                    if (window.showToast) window.showToast(`Failed to add ${chip.label}`, 'error');
                }
            } else {
                // Open add row (creates it if not already open) and fill with chip data
                this._openAddRow(sectionKey);

                // Small delay to ensure the add row is in the DOM
                await new Promise(r => setTimeout(r, 100));

                const tbody = document.getElementById(`ewGridBody_${sectionKey}`);
                if (!tbody) return;
                const addRow = tbody.querySelector('tr[data-new="true"]');
                if (!addRow) return;

                // Clear any existing content first
                addRow.querySelectorAll('.ew-cell-input').forEach(inp => { inp.value = ''; });
                addRow.querySelectorAll('.ew-cell-checkbox').forEach(cb => { cb.checked = false; });
                addRow.querySelectorAll('.ew-cell-select').forEach(sel => { sel.selectedIndex = 0; });

                const data = chip.data;
                Object.keys(data).forEach(field => {
                    const el = addRow.querySelector(`[data-field="${field}"]`);
                    if (el) {
                        if (el.type === 'checkbox') el.checked = !!data[field];
                        else el.value = data[field];
                    }
                });

                // Focus the next empty field or the first field
                const columns = this._getGridColumns(sectionKey);
                let focused = false;
                for (const col of columns) {
                    if (data[col.field] == null || data[col.field] === '') {
                        const el = addRow.querySelector(`[data-field="${col.field}"]`);
                        if (el && el.type !== 'checkbox') { el.focus(); focused = true; break; }
                    }
                }
                // If all chip fields were set, focus the first non-chip field for user review
                if (!focused) {
                    const firstInput = addRow.querySelector('.ew-cell-input, .ew-cell-select');
                    if (firstInput) firstInput.focus();
                }
            }
        }

        // ─── GRID AUTOCOMPLETE ───────────────────────────────────────

        /** Initialize autocomplete on a grid input */
        _initGridAutocomplete(input) {
            const acType = input.dataset.ac;
            const acId = input.dataset.acId;
            if (!acType || !acId) return;

            const resultsEl = document.getElementById(acId);
            if (!resultsEl) return;

            let debounceTimer = null;
            let selectedIndex = -1;
            let currentResults = [];

            const showResults = (html) => {
                resultsEl.innerHTML = html;
                resultsEl.classList.remove('d-none');
            };
            const hideResults = () => {
                resultsEl.classList.add('d-none');
                selectedIndex = -1;
                currentResults = [];
            };

            const search = async (query) => {
                if (query.length < 2) { hideResults(); return; }

                let results = [];
                if (acType === 'allergens') {
                    results = HistoryPresets.searchAllergens(query).map(a => ({
                        primary: a.name,
                        secondary: ['Drug', 'Food', 'Environmental', 'Other'][a.type] || '',
                        data: a
                    }));
                } else if (acType === 'vaccines') {
                    results = HistoryPresets.searchVaccines(query).map(v => ({
                        primary: v.name,
                        secondary: v.cvx ? `CVX: ${v.cvx}` : '',
                        data: v
                    }));
                } else if (acType === 'icd') {
                    try {
                        const icdResults = await window.apiRequest(`/lookups/icd-codes?search=${encodeURIComponent(query)}`, { showLoader: false });
                        results = (icdResults || []).slice(0, 10).map(r => ({
                            primary: r.Description || r.description || '',
                            secondary: r.Code || r.code || '',
                            data: r
                        }));
                    } catch (e) { results = []; }
                } else if (acType === 'drugs') {
                    try {
                        // Try local DB first, fall back to AI search
                        let drugResults = await window.apiRequest(`/drugs/search?q=${encodeURIComponent(query)}&limit=8`, { showLoader: false, showErrors: false }).catch(() => []);
                        if (!drugResults || drugResults.length === 0) {
                            drugResults = await window.apiRequest(`/ai/drugs/search?q=${encodeURIComponent(query)}&limit=8`, { showLoader: false, showErrors: false }).catch(() => []);
                        }
                        results = (drugResults || []).slice(0, 8).map(r => ({
                            primary: r.DisplayName || r.BrandName || r.GenericName || '',
                            secondary: [r.Strength, r.DosageForm].filter(Boolean).join(' '),
                            data: r
                        }));
                    } catch (e) { results = []; }
                }

                currentResults = results;
                if (results.length === 0) {
                    hideResults();
                    return;
                }

                const html = results.map((r, i) =>
                    `<div class="autocomplete-item${i === selectedIndex ? ' active' : ''}" data-index="${i}">
                        <span class="ac-primary">${this._escape(r.primary)}</span>
                        ${r.secondary ? `<span class="ac-secondary ms-1">${this._escape(r.secondary)}</span>` : ''}
                    </div>`
                ).join('');
                showResults(html);
            };

            const selectResult = (index) => {
                if (index < 0 || index >= currentResults.length) return;
                const r = currentResults[index];
                const row = input.closest('tr');
                const sectionKey = row?.dataset.section;

                // Fill primary field
                input.value = r.primary;

                // Auto-fill notes field with additional details from autocomplete
                const notesEl = row.querySelector('[data-field="notes"]');
                if (sectionKey === 'allergies' && acType === 'allergens') {
                    const typeName = ['Drug', 'Food', 'Environmental', 'Other'][r.data.type] || '';
                    if (notesEl && !notesEl.value && typeName) notesEl.value = typeName + ' allergy';
                } else if (sectionKey === 'problems' && acType === 'icd') {
                    // Do NOT auto-fill the notes field with ICD codes.
                    // ICD code is stored separately on the problem row via hidden field/data attribute.
                    // Notes field is free-text clinical context only (e.g. "Fever for 3 days").
                } else if (sectionKey === 'medications' && acType === 'drugs') {
                    const d = r.data;
                    input.value = d.BrandName || d.GenericName || r.primary;
                    const parts = [d.Strength || d.strength, d.Route || d.route, d.DosageForm || d.dosageForm].filter(Boolean);
                    if (notesEl && !notesEl.value && parts.length > 0) notesEl.value = parts.join(', ');
                }

                hideResults();

                // Move focus to the next empty field
                if (row) {
                    const columns = this._getGridColumns(sectionKey);
                    let foundCurrent = false;
                    for (const col of columns) {
                        if (col.field === input.dataset.field) { foundCurrent = true; continue; }
                        if (foundCurrent) {
                            const nextEl = row.querySelector(`[data-field="${col.field}"]`);
                            if (nextEl && !nextEl.value && col.type !== 'checkbox' && col.type !== 'select') {
                                nextEl.focus();
                                return;
                            }
                        }
                    }
                }
            };

            // Input handler with debounce
            input.addEventListener('input', () => {
                if (debounceTimer) clearTimeout(debounceTimer);
                const delay = acType === 'drugs' ? 300 : 150;
                debounceTimer = setTimeout(() => search(input.value.trim()), delay);
            });

            // Keyboard navigation
            input.addEventListener('keydown', (e) => {
                if (!currentResults.length) return;
                if (e.key === 'ArrowDown') {
                    e.preventDefault();
                    selectedIndex = Math.min(selectedIndex + 1, currentResults.length - 1);
                    resultsEl.querySelectorAll('.autocomplete-item').forEach((el, i) => el.classList.toggle('active', i === selectedIndex));
                } else if (e.key === 'ArrowUp') {
                    e.preventDefault();
                    selectedIndex = Math.max(selectedIndex - 1, 0);
                    resultsEl.querySelectorAll('.autocomplete-item').forEach((el, i) => el.classList.toggle('active', i === selectedIndex));
                } else if (e.key === 'Enter' && selectedIndex >= 0) {
                    e.preventDefault();
                    e.stopPropagation(); // Prevent new row save from also firing
                    // Set flag on row so container keydown handler knows to skip
                    const parentRow = input.closest('tr');
                    if (parentRow) parentRow._acJustSelected = true;
                    setTimeout(() => { if (parentRow) parentRow._acJustSelected = false; }, 100);
                    selectResult(selectedIndex);
                }
            });

            // Click on result
            resultsEl.addEventListener('click', (e) => {
                const item = e.target.closest('.autocomplete-item');
                if (item) selectResult(parseInt(item.dataset.index));
            });

            // Hide on blur (delayed for click handling)
            input.addEventListener('blur', () => {
                setTimeout(hideResults, 200);
            });

            // Show on focus if there's existing text
            input.addEventListener('focus', () => {
                if (input.value.trim().length >= 2 && currentResults.length > 0) {
                    resultsEl.classList.remove('d-none');
                }
            });
        }

        /** Handle autocomplete for grid inputs (triggered from input event) */
        _handleGridAutocomplete(input, sectionKey) {
            // Autocomplete is handled by _initGridAutocomplete's event listeners
            // This is a hook for additional logic if needed
        }

        // ─── HISTORY ACTIONS — UNIFIED MODAL FLOW ─────────────────────
        // All state changes and deletes go through HistoryActionModal which
        // captures a required reason and posts to /api/history-review/...
        // Spec: rules/technical/history-review-soft-delete.md (sections 7, 9).

        /** Map encounter sectionKey to API segment used in /api/history-review/{seg}/{action}. */
        _historyApiSegment(sectionKey) {
            switch (sectionKey) {
                case 'allergies':     return 'allergy';
                case 'medications':   return 'medication';
                case 'problems':      return 'problem';
                case 'familyHx':      return 'family-history';
                case 'socialHx':      return 'social-history';
                case 'immunizations': return 'immunization';
                case 'supplements':   return 'supplement';
                default:              return null;
            }
        }

        /** Map a JS action key to its kebab-case URL segment. */
        _historyActionUrlSegment(action) {
            const map = {
                inactivate:        'inactivate',
                reactivate:        'reactivate',
                discontinue:       'discontinue',
                revertDiscontinue: 'revert-discontinue',
                markOnHold:        'mark-on-hold',
                resumeFromHold:    'resume-from-hold',
                complete:          'complete',
                markResolved:      'mark-resolved',
                markInactive:      'mark-inactive',
                delete:            'delete'
            };
            return map[action] || null;
        }

        /** Build a human label for the modal header (drug name / allergen / problem desc). */
        _historyRecordLabel(sectionKey, id) {
            const lookups = {
                allergies:     [this._allergiesActive,    this._allergiesInactive,    'allergenName'],
                medications:   [this._medicationsActive,  this._medicationsInactive,  'drugName'],
                problems:      [this._problemsActive,     this._problemsInactive,     'description'],
                familyHx:      [this._familyHistory,      [],                          'condition'],
                socialHx:      [this._socialHistory,      [],                          'category'],
                immunizations: [this._immunizations,      [],                          'vaccineName']
            };
            const tuple = lookups[sectionKey];
            if (!tuple) return '';
            const [a, b, field] = tuple;
            const all = [...(a || []), ...(b || [])];
            const item = all.find(x => this._getItemId(sectionKey, x) === id);
            if (!item) return '';
            return this._getFieldValue(item, field) || '';
        }

        /** Open the shared modal for a section/action, post on confirm, refresh on success. */
        _openHistoryActionModal(sectionKey, action, recordId) {
            if (!window.HistoryActionModal) {
                console.error('[EncounterWorkspace] HistoryActionModal not loaded');
                return;
            }
            const apiSeg = this._historyApiSegment(sectionKey);
            const actSeg = this._historyActionUrlSegment(action);
            if (!apiSeg || !actSeg) {
                console.error('[EncounterWorkspace] Bad section/action:', sectionKey, action);
                return;
            }

            const recordLabel = this._historyRecordLabel(sectionKey, recordId);

            window.HistoryActionModal.open({
                section: apiSeg,
                action: action,
                recordId: recordId,
                recordLabel: recordLabel,
                onConfirm: async (reason) => {
                    await window.apiRequest(`/history-review/${apiSeg}/${actSeg}`, {
                        method: 'POST',
                        body: { recordId: recordId, reason: reason }
                    });
                    // Reload the whole history step — simplest correct approach,
                    // covers all edge cases (active->inactive, inactive->active, delete).
                    await this._renderHistoryStep();
                }
            });
        }

        async _renderCcHpiStep() {
            const container = document.getElementById('ewStepContent');
            if (!container) return;

            const enc = this.encounter;
            const cc = enc.ChiefComplaint || '';
            const hpi = enc.HistoryOfPresentIllness || '';

            this._markStepComplete(2, !!(cc.trim() || hpi.trim()));

            container.innerHTML = `
                <div class="row g-4">
                    <div class="col-lg-8">
                        <!-- Chief Complaint -->
                        <div class="card mb-3">
                            <div class="card-header bg-white">
                                <h6 class="mb-0"><i class="bi bi-chat-square-text me-2 text-primary"></i>Chief Complaint</h6>
                            </div>
                            <div class="card-body">
                                <input type="text" class="form-control form-control-lg" id="ewCcInput"
                                       placeholder="e.g. Chest pain, shortness of breath, follow-up visit..."
                                       value="${this._escapeAttr(cc)}" maxlength="500">
                                <small class="text-muted mt-1 d-block">Brief reason for today's visit</small>
                            </div>
                        </div>

                        <!-- History of Present Illness -->
                        <div class="card">
                            <div class="card-header bg-white">
                                <h6 class="mb-0"><i class="bi bi-journal-text me-2 text-primary"></i>History of Present Illness (HPI)</h6>
                            </div>
                            <div class="card-body">
                                <textarea class="form-control" id="ewHpiInput" rows="10"
                                          placeholder="Describe the patient's present illness in detail...&#10;&#10;Include: onset, location, duration, character, aggravating/alleviating factors, radiation, timing, severity (OLDCARTS)">${this._escape(hpi)}</textarea>
                                <small class="text-muted mt-1 d-block">
                                    <i class="bi bi-lightbulb me-1"></i>OLDCARTS: Onset, Location, Duration, Character, Aggravating, Radiation, Timing, Severity
                                </small>
                            </div>
                        </div>

                        <div class="d-flex justify-content-end align-items-center mt-3">
                            <span id="ewCcHpiSaveStatus" class="text-muted small"></span>
                        </div>
                    </div>

                    <!-- Sidebar hints -->
                    <div class="col-lg-4">
                        <div class="card bg-light border-0">
                            <div class="card-body">
                                <h6 class="card-title"><i class="bi bi-info-circle me-1 text-primary"></i>Auto-Save</h6>
                                <p class="card-text small text-muted mb-2">
                                    Changes are saved automatically after you stop typing for ~1 second.
                                    You can also click "Save" manually.
                                </p>
                                <hr>
                                <h6 class="card-title"><i class="bi bi-lightbulb me-1 text-warning"></i>HPI Tips</h6>
                                <ul class="small text-muted mb-0 ps-3">
                                    <li><strong>Onset:</strong> When did symptoms start?</li>
                                    <li><strong>Location:</strong> Where is the problem?</li>
                                    <li><strong>Duration:</strong> How long has it lasted?</li>
                                    <li><strong>Character:</strong> What does it feel like?</li>
                                    <li><strong>Aggravating:</strong> What makes it worse?</li>
                                    <li><strong>Radiation:</strong> Does it spread?</li>
                                    <li><strong>Timing:</strong> Constant or intermittent?</li>
                                    <li><strong>Severity:</strong> Rate 1-10</li>
                                </ul>
                            </div>
                        </div>
                    </div>
                </div>
            `;

            // Setup auto-save on both fields
            const ccInput = document.getElementById('ewCcInput');
            const hpiInput = document.getElementById('ewHpiInput');

            const autoSave = () => {
                if (this._saveTimeout) clearTimeout(this._saveTimeout);
                this._dirty = true;
                this._saveTimeout = setTimeout(() => this._saveCcHpi(), 800);
            };

            ccInput?.addEventListener('input', autoSave);
            hpiInput?.addEventListener('input', autoSave);

            // Auto-save handles everything — no manual save button needed

            // beforeunload warning for unsaved changes
            window.addEventListener('beforeunload', (e) => {
                if (this._dirty) {
                    e.preventDefault();
                    e.returnValue = '';
                }
            });

            // MEDOCS AI Voice Entry (MA/Nurse can use voice for CC/HPI)
            this._initMedocsVoice('cc-hpi');

            // Flush any pending voice data for CC/HPI
            if (this._unifiedVoiceBar) this._unifiedVoiceBar.flushPendingCcHpi();
            if (this._telehealthScribeUI) this._telehealthScribeUI.flushPendingCcHpi();
        }

        async _saveCcHpi() {
            const ccVal = document.getElementById('ewCcInput')?.value?.trim() || '';
            const hpiVal = document.getElementById('ewHpiInput')?.value?.trim() || '';

            this._showSaveIndicator('saving');
            const statusEl = document.getElementById('ewCcHpiSaveStatus');
            if (statusEl) statusEl.innerHTML = '<i class="bi bi-arrow-repeat spin me-1"></i>Saving...';

            try {
                const dto = {
                    ChiefComplaint: ccVal || null,
                    HistoryOfPresentIllness: hpiVal || null
                };

                const result = await window.apiRequest(`/patients/${this.patientId}/encounters/${this.encounterId}`, {
                    method: 'PUT',
                    body: dto,
                    showLoader: false
                });

                // Update local cache
                if (result) {
                    this.encounter = result;
                }

                this._dirty = false;
                this._showSaveIndicator('saved');
                const now = new Date().toLocaleTimeString();
                if (statusEl) statusEl.innerHTML = `<i class="bi bi-check-circle text-success me-1"></i>Saved at ${now}`;

                // Update step completion
                this._markStepComplete(2, !!(ccVal || hpiVal));

            } catch (e) {
                console.error('[EncounterWorkspace] Save CC/HPI error:', e);
                this._showSaveIndicator('error');
                if (statusEl) statusEl.innerHTML = '<i class="bi bi-exclamation-circle text-danger me-1"></i>Save failed. Click Save to retry.';
            }
        }

        async _renderOrdersStep() {
            const container = document.getElementById('ewStepContent');
            if (!container) return;

            container.innerHTML = `<div class="text-center py-3"><div class="spinner-border spinner-border-sm"></div> Loading orders...</div>`;

            // Load patient orders
            try {
                this._orders = await window.apiRequest(`/orders?patientId=${this.patientId}`, { showLoader: false }) || [];
            } catch (e) {
                this._orders = [];
            }

            this._markStepComplete(4, this._orders.length > 0);

            const typeNames = { 0: 'Lab', 1: 'Imaging', 2: 'Referral' };
            const typeBadges = { 0: 'primary', 1: 'info', 2: 'warning' };
            const statusNames = { 0: 'Pending', 1: 'InProgress', 2: 'Completed', 3: 'Cancelled' };
            const statusBadges = { 0: 'warning', 1: 'info', 2: 'success', 3: 'secondary' };
            const priorityNames = { 0: 'Routine', 1: 'Urgent', 2: 'STAT' };

            const ordersHtml = this._orders.length === 0
                ? '<p class="text-muted text-center py-4"><i class="bi bi-clipboard2 me-1"></i>No orders for this patient yet</p>'
                : `<div class="table-responsive">
                    <table class="table table-sm table-hover mb-0">
                        <thead class="table-light">
                            <tr><th>Date</th><th>Type</th><th>Description</th><th>Priority</th><th>Status</th><th>Actions</th></tr>
                        </thead>
                        <tbody>
                            ${this._orders.map(o => {
                                const desc = this._escape(o.Description || o.LabPanelName || o.LabTestName || (o.BodyPart ? (typeNames[o.OrderType === 1 ? o.Modality : ''] || '') + ' ' + o.BodyPart : '') || o.ReferralSpecialty || o.ImagingDescription || o.ReferralReason || '');
                                let actions = `<button class="btn btn-outline-info" onclick="window._ordersModule?._viewOrder(${o.OrderId})" title="View"><i class="bi bi-eye"></i></button> `;
                                if (!this._isMaNurse) {
                                    if (o.Status === 0) actions += `<button class="btn btn-outline-secondary" onclick="window._ordersModule?._editOrder(${o.OrderId})" title="Edit"><i class="bi bi-pencil"></i></button> <button class="btn btn-outline-primary" onclick="window._ordersModule?._submitOrder(${o.OrderId})" title="Submit"><i class="bi bi-send"></i></button> `;
                                    if (o.Status >= 1 && o.Status <= 3) actions += `<button class="btn btn-outline-success" onclick="window._ordersModule?._openResultsModal(${o.OrderId})" title="Add Results"><i class="bi bi-clipboard-check"></i></button> `;
                                    if (o.Status === 4) actions += `<button class="btn btn-outline-success" onclick="window._ordersModule?._completeOrder(${o.OrderId})" title="Complete"><i class="bi bi-check-circle"></i></button> `;
                                    if (o.Status < 5) actions += `<button class="btn btn-outline-danger" onclick="window._ordersModule?._cancelOrder(${o.OrderId})" title="Cancel"><i class="bi bi-x-circle"></i></button>`;
                                }
                                return `
                                <tr>
                                    <td class="small">${o.OrderDate || ''}</td>
                                    <td><span class="badge bg-${typeBadges[o.OrderType] || 'secondary'}">${typeNames[o.OrderType] || ''}</span></td>
                                    <td>${desc}</td>
                                    <td class="small">${priorityNames[o.Priority] || ''}</td>
                                    <td><span class="badge bg-${statusBadges[o.Status] || 'secondary'}">${statusNames[o.Status] || ''}</span></td>
                                    <td>${actions}</td>
                                </tr>`;
                            }).join('')}
                        </tbody>
                    </table>
                </div>`;

            const orderActionBtns = this._isMaNurse ? '' : `
                <div class="d-flex gap-2 mb-3">
                    <button class="btn btn-primary" id="ewBtnNewLabOrder">
                        <i class="bi bi-droplet me-1"></i>New Lab Order
                    </button>
                    <button class="btn btn-info text-white" id="ewBtnNewImaging">
                        <i class="bi bi-radioactive me-1"></i>New Imaging
                    </button>
                    <button class="btn btn-warning" id="ewBtnNewReferral">
                        <i class="bi bi-person-rolodex me-1"></i>New Referral
                    </button>
                </div>`;

            container.innerHTML = `
                <div class="d-flex justify-content-between align-items-center mb-3">
                    <span class="text-muted">${this._isMaNurse ? 'View orders for this encounter' : 'Manage orders for this encounter'}</span>
                    <span class="badge bg-light text-dark fs-6">${this._orders.length} order(s)</span>
                </div>

                ${orderActionBtns}

                <!-- Orders list -->
                <div class="card">
                    <div class="card-header bg-white">
                        <h6 class="mb-0"><i class="bi bi-clipboard2-pulse me-2 text-primary"></i>Orders & Referrals</h6>
                    </div>
                    <div class="card-body p-0">
                        ${ordersHtml}
                    </div>
                </div>
            `;

            // Bind buttons
            document.getElementById('ewBtnNewLabOrder')?.addEventListener('click', () => this._openOrderModal(0));
            document.getElementById('ewBtnNewImaging')?.addEventListener('click', () => this._openOrderModal(1));
            document.getElementById('ewBtnNewReferral')?.addEventListener('click', () => this._openOrderModal(2));

            // Listen for modal close to refresh
            const orderModalEl = document.getElementById('orderModal');
            if (orderModalEl) {
                orderModalEl.addEventListener('hidden.bs.modal', () => {
                    // Small delay to let API finish
                    setTimeout(() => this._renderOrdersStep(), 500);
                }, { once: true });
            }
        }

        _openOrderModal(orderType) {
            const pt = this.patient;
            const patientName = `${pt.FirstName} ${pt.LastName}`;

            // Pre-fill patient in the Orders modal
            const searchEl = document.getElementById('orderPatientSearch');
            const idEl = document.getElementById('orderPatientId');
            if (searchEl) searchEl.value = patientName;
            if (idEl) idEl.value = this.patientId;

            // Set order type radio
            const typeRadios = { 0: 'orderTypeLab', 1: 'orderTypeImaging', 2: 'orderTypeReferral' };
            const radio = document.getElementById(typeRadios[orderType]);
            if (radio) radio.checked = true;

            // Trigger type change to show correct form section
            if (window._ordersModule && window._ordersModule._onOrderTypeChange) {
                window._ordersModule._onOrderTypeChange();
            }

            // Show modal
            const modal = new bootstrap.Modal(document.getElementById('orderModal'));
            modal.show();
        }

        async _renderPrescriptionsStep() {
            const container = document.getElementById('ewStepContent');
            if (!container) return;

            container.innerHTML = `<div class="text-center py-3"><div class="spinner-border spinner-border-sm"></div> Loading prescriptions...</div>`;

            // Load patient prescriptions
            try {
                this._prescriptions = await window.apiRequest(`/prescriptions?patientId=${this.patientId}`, { showLoader: false }) || [];
            } catch (e) {
                this._prescriptions = [];
            }

            this._markStepComplete(5, this._prescriptions.length > 0);

            const statusNames = { 0: 'Draft', 1: 'Signed', 2: 'Sent', 3: 'Dispensed', 4: 'Cancelled' };
            const statusBadges = { 0: 'secondary', 1: 'primary', 2: 'info', 3: 'success', 4: 'danger' };

            const rxHtml = this._prescriptions.length === 0
                ? '<p class="text-muted text-center py-4"><i class="bi bi-capsule me-1"></i>No prescriptions for this patient yet</p>'
                : `<div class="table-responsive">
                    <table class="table table-sm table-hover mb-0">
                        <thead class="table-light">
                            <tr><th>Date</th><th>Drug</th><th>Strength</th><th>SIG</th><th>Status</th>${this._isMaNurse ? '' : '<th>Actions</th>'}</tr>
                        </thead>
                        <tbody>
                            ${this._prescriptions.map(rx => {
                                const rxDate = rx.PrescribedDate || rx.PrescriptionDate || '';
                                const sig = rx.DirectionsFreeText || rx.SIG || rx.Sig || '';
                                let rxActions = `<button class="btn btn-outline-info" onclick="window._prescriptionsModule?._viewRx(${rx.PrescriptionId})" title="View"><i class="bi bi-eye"></i></button> `;
                                if (!this._isMaNurse) {
                                    if (rx.Status === 0) rxActions += `<button class="btn btn-outline-secondary" onclick="window._prescriptionsModule?._editRx(${rx.PrescriptionId})" title="Edit"><i class="bi bi-pencil"></i></button> <button class="btn btn-outline-primary" onclick="window._prescriptionsModule?._sendRx(${rx.PrescriptionId})" title="Sign & Send"><i class="bi bi-send-check"></i></button> `;
                                    if (rx.Status <= 2) rxActions += `<button class="btn btn-outline-danger" onclick="window._prescriptionsModule?._cancelRx(${rx.PrescriptionId})" title="Cancel"><i class="bi bi-x-circle"></i></button>`;
                                }
                                return `
                                <tr>
                                    <td class="small">${rxDate}</td>
                                    <td><strong>${this._escape(rx.DrugName || '')}</strong></td>
                                    <td class="small">${this._escape(rx.Strength || '')}</td>
                                    <td class="small">${this._escape(sig)}</td>
                                    <td><span class="badge bg-${statusBadges[rx.Status] || 'secondary'}">${statusNames[rx.Status] || ''}</span></td>
                                    <td>${rxActions}</td>
                                </tr>`;
                            }).join('')}
                        </tbody>
                    </table>
                </div>`;

            const rxActionBtn = this._isMaNurse ? '' : `
                <div class="mb-3">
                    <button class="btn btn-primary" id="ewBtnNewRx">
                        <i class="bi bi-capsule me-1"></i>New Prescription
                    </button>
                </div>`;

            container.innerHTML = `
                <div class="d-flex justify-content-between align-items-center mb-3">
                    <span class="text-muted">${this._isMaNurse ? 'View prescriptions for this encounter' : 'Manage prescriptions for this encounter'}</span>
                    <span class="badge bg-light text-dark fs-6">${this._prescriptions.length} prescription(s)</span>
                </div>

                ${rxActionBtn}

                <!-- Prescriptions list -->
                <div class="card">
                    <div class="card-header bg-white">
                        <h6 class="mb-0"><i class="bi bi-capsule me-2 text-primary"></i>Patient Prescriptions</h6>
                    </div>
                    <div class="card-body p-0">
                        ${rxHtml}
                    </div>
                </div>
            `;

            // Bind button
            document.getElementById('ewBtnNewRx')?.addEventListener('click', () => this._openPrescriptionModal());

            // Listen for modal close to refresh
            const rxModalEl = document.getElementById('prescriptionModal');
            if (rxModalEl) {
                rxModalEl.addEventListener('hidden.bs.modal', () => {
                    setTimeout(() => this._renderPrescriptionsStep(), 500);
                }, { once: true });
            }
        }

        _openPrescriptionModal() {
            const pt = this.patient;
            const patientName = `${pt.FirstName} ${pt.LastName}`;

            // Pre-fill patient in the Prescriptions modal
            const searchEl = document.getElementById('rxPatientSearch');
            const idEl = document.getElementById('rxPatientId');
            if (searchEl) searchEl.value = patientName;
            if (idEl) idEl.value = this.patientId;

            // Show modal
            const modal = new bootstrap.Modal(document.getElementById('prescriptionModal'));
            modal.show();
        }

        async _renderNoteStep() {
            const container = document.getElementById('ewStepContent');
            if (!container) return;

            container.innerHTML = `<div class="text-center py-3"><div class="spinner-border spinner-border-sm"></div> Loading notes...</div>`;

            // Load ALL notes for this encounter
            const enc = this.encounter;
            this._clinicalNotes = [];

            try {
                if (enc.AppointmentId) {
                    this._clinicalNotes = await window.apiRequest(`/clinical-notes/by-appointment/${enc.AppointmentId}`, { showLoader: false }) || [];
                }
                if (this._clinicalNotes.length === 0) {
                    // Try by patient + filter for this encounter
                    const allNotes = await window.apiRequest(`/clinical-notes?patientId=${this.patientId}`, { showLoader: false }) || [];
                    this._clinicalNotes = allNotes.filter(n => n.EncounterId === this.encounterId);
                }
            } catch (e) {
                console.error('[EncounterWorkspace] Note lookup error:', e);
            }

            this._markStepComplete(3, this._clinicalNotes.length > 0);
            this._renderNoteStepContent();
        }

        _renderNoteStepContent() {
            const container = document.getElementById('ewStepContent');
            if (!container) return;

            const statusNames = { 0: 'Draft', 1: 'Pending', 2: 'Signed', 3: 'Amended', 4: 'Final' };
            const statusBadges = { 0: 'warning', 1: 'info', 2: 'success', 3: 'primary', 4: 'secondary' };

            // Action buttons (hidden for MA/Nurse)
            const isTelehealthEncounter = this._isTelehealthEncounter;
            let html = '';

            // Prominent "Start Video Call" button when call has happened and no active panel
            if (isTelehealthEncounter && this._telehealthCallHappened && !this._telehealthPanelActive) {
                html += `
                    <div class="text-center mb-3 p-3 border rounded-3" style="background: linear-gradient(135deg, #e8f5e9, #f1f8e9);" id="ewStartVideoCallCenter">
                        <button class="btn btn-success btn-lg" onclick="window._encounterWorkspace._startVideoCallFromButton()"
                            style="animation: pulse-glow 2s ease-in-out 3;">
                            <i class="bi bi-camera-video-fill me-2"></i>Start Video Call with Patient
                        </button>
                        <p class="text-muted small mt-2 mb-0">Previous call transcription is saved. Start a new call if needed.</p>
                    </div>
                `;
            }

            html += this._isMaNurse ? '' : `
                <div class="d-flex gap-2 mb-3 flex-wrap">
                    <button class="btn btn-primary" id="ewBtnWriteNote">
                        <i class="bi bi-file-earmark-plus me-1"></i>Write Note
                    </button>
                    ${isTelehealthEncounter ? `
                        <button class="btn btn-success" id="ewBtnGenerateTelehealthNote">
                            <i class="bi bi-broadcast me-1"></i>Generate Note from Telehealth Session
                        </button>
                    ` : `
                        <button class="btn btn-outline-primary" id="ewBtnRecordSession">
                            <i class="bi bi-mic-fill me-1"></i>Record Session
                        </button>
                    `}
                </div>
            `;

            // Notes list — compact when intake iframe is also visible (provider/admin),
            // full empty state when it isn't (MA/Nurse, who don't get the iframe below).
            if (this._clinicalNotes.length === 0) {
                if (this._isMaNurse) {
                    html += `
                        <div class="text-center py-4 text-muted">
                            <i class="bi bi-file-earmark-text fs-1"></i>
                            <p class="mt-2 mb-0">No clinical notes for this encounter yet.</p>
                        </div>
                    `;
                } else {
                    // Provider has the Patient Intake Form iframe rendered below; the
                    // big centered empty-state block wastes ~80px of vertical space
                    // for redundant info. One muted line is enough.
                    html += `
                        <p class="text-muted small mb-2">${isTelehealthEncounter
                            ? 'No clinical notes yet. Use <strong>Write Note</strong> or <strong>Generate Note from Telehealth Session</strong>.'
                            : 'No clinical notes yet. Use <strong>Write Note</strong> or <strong>Record Session</strong>.'}</p>
                    `;
                }
            } else {
                html += `<div class="list-group">`;
                for (const note of this._clinicalNotes) {
                    const isDraft = note.Status === 0;
                    const canEdit = isDraft && !this._isMaNurse;
                    const statusName = statusNames[note.Status] || 'Unknown';
                    const badgeClass = statusBadges[note.Status] || 'secondary';
                    const noteDate = note.ServiceDate ? (window.parseServerDateTime ? window.parseServerDateTime(note.ServiceDate) : new Date(note.ServiceDate)).toLocaleDateString() : '';

                    html += `
                        <div class="list-group-item list-group-item-action d-flex justify-content-between align-items-center"
                             style="cursor: pointer;"
                             onclick="window._encounterWorkspace._openNoteInModal(${note.ClinicalNoteId}, ${canEdit})">
                            <div>
                                <span class="badge bg-${badgeClass} me-2">${statusName}</span>
                                <strong>${this._escape(note.TemplateName || note.TypeName || 'Clinical Note')}</strong>
                                <small class="text-muted ms-2">${noteDate}</small>
                            </div>
                            <span class="text-${canEdit ? 'primary' : 'secondary'}">
                                <i class="bi ${canEdit ? 'bi-pencil' : 'bi-eye'} me-1"></i>${canEdit ? 'Edit' : 'View'}
                            </span>
                        </div>
                    `;
                }
                html += `</div>`;
            }

            // Patient Intake is now reached ONLY via the sidebar FORMS group
            // ("Patient Intake Form" link), for every role — same as the MA/Nurse
            // flow. The previous Clinical Note step auto-embed was removed on
            // 2026-05 so provider/admin and nurse see the same intake UX.
            // TODO: update rules/technical/intake-on-clinical-note.md §3.1 to
            // match this new behavior (rule doc currently still describes the
            // auto-embed).

            container.innerHTML = html;

            // Bind action buttons (only if not MA/Nurse)
            if (!this._isMaNurse) {
                document.getElementById('ewBtnWriteNote')?.addEventListener('click', () => this._openWriteNoteModal());
                document.getElementById('ewBtnRecordSession')?.addEventListener('click', () => this._openRecordingSession());
                document.getElementById('ewBtnGenerateTelehealthNote')?.addEventListener('click', () => this._generateNoteFromTelehealth());

                // Sync the freshly-rendered Generate button with the current
                // transcription-finalization state. If chunks are still being
                // transcribed (e.g., user navigated steps right after End Call),
                // the button must immediately show the "wrapping up" label.
                if (this._telehealthScribeUI && typeof this._telehealthScribeUI.getProcessingCount === 'function') {
                    this._updateTelehealthGenerateButtonState(this._telehealthScribeUI.getProcessingCount());
                }
            }
        }

        /** Handle clinical note status change (sign, amend, etc.) — update in-place */
        async _onClinicalNoteChanged(detail) {
            // Refresh the cached notes from API
            const enc = this.encounter;
            try {
                let notes = [];
                if (enc.AppointmentId) {
                    notes = await window.apiRequest(`/clinical-notes/by-appointment/${enc.AppointmentId}`, { showLoader: false }) || [];
                }
                if (notes.length === 0) {
                    const allNotes = await window.apiRequest(`/clinical-notes?patientId=${this.patientId}`, { showLoader: false }) || [];
                    notes = allNotes.filter(n => n.EncounterId === this.encounterId);
                }
                this._clinicalNotes = notes;
                this._markStepComplete(3, notes.length > 0);

                // Only re-render content if user is currently on the Clinical Note step
                if (this.currentStep === 3) {
                    this._renderNoteStepContent();
                }
            } catch (e) {
                console.error('[EncounterWorkspace] Note change refresh error:', e);
            }
        }

        // =====================================================
        // REFERENCE PANEL (Consolidated Vitals/History/CC-HPI)
        // =====================================================

        /**
         * Load and render the reference panel with Vitals, History, CC/HPI
         * for the consolidated Clinical Note view.
         */
        async _loadReferencePanel() {
            const panel = document.getElementById('ewRefPanelContent');
            if (!panel) return;

            try {
                // Load all reference data in parallel
                const [vitals, allergies, medications, problems, familyHx, socialHx] = await Promise.all([
                    window.apiRequest(`/patients/${this.patientId}/vitals`, { showLoader: false }).catch(() => []),
                    window.apiRequest(`/patients/${this.patientId}/allergies`, { showLoader: false }).catch(() => []),
                    window.apiRequest(`/patients/${this.patientId}/medications`, { showLoader: false }).catch(() => []),
                    window.apiRequest(`/patients/${this.patientId}/problems`, { showLoader: false }).catch(() => []),
                    window.apiRequest(`/patients/${this.patientId}/family-history`, { showLoader: false }).catch(() => []),
                    window.apiRequest(`/patients/${this.patientId}/social-history`, { showLoader: false }).catch(() => [])
                ]);

                // Cache for other steps. Reference panel on the Note step shows
                // BOTH active and historical context per
                // rules/technical/history-review-soft-delete.md (section 11), so the
                // clinician sees full picture while composing the note.
                this._vitals = vitals || [];
                this._refAllergiesAll   = allergies || [];
                this._refMedicationsAll = medications || [];
                this._refProblemsAll    = problems || [];
                // Backwards-compat aliases (active subset) for any consumer that
                // expects pre-filtered arrays.
                this._allergies      = (allergies || []).filter(a => (a.IsActive ?? a.isActive) !== false);
                this._medications    = (medications || []).filter(m => (m.Status ?? m.status) === 0);
                this._problems       = (problems || []).filter(p => (p.Status ?? p.status) === 0);
                this._familyHistory  = familyHx || [];
                this._socialHistory  = socialHx || [];

                const enc = this.encounter;
                const encounterVitals = this._vitals.filter(v => v.EncounterId === this.encounterId);
                const currentVital = encounterVitals.length > 0 ? encounterVitals[0] : null;
                const cc = enc.ChiefComplaint || '';
                const hpi = enc.HistoryOfPresentIllness || '';

                let html = '';

                // CC/HPI Section
                html += `
                    <div class="ew-ref-section">
                        <div class="ew-ref-section-header" onclick="this.parentElement.classList.toggle('collapsed')">
                            <h6><i class="bi bi-chat-text me-1 text-primary"></i>CC & HPI</h6>
                            <a href="#" class="ew-ref-edit-link" onclick="event.preventDefault(); event.stopPropagation(); window._encounterWorkspace._showStep(2);" title="Edit CC/HPI">
                                <i class="bi bi-pencil-square"></i>
                            </a>
                        </div>
                        <div class="ew-ref-section-body">
                            ${cc ? `<div class="mb-1"><strong class="small text-muted">CC:</strong> <span class="small">${this._escape(cc)}</span></div>` : '<div class="text-muted small fst-italic">No chief complaint</div>'}
                            ${hpi ? `<div><strong class="small text-muted">HPI:</strong> <span class="small ew-ref-hpi-text">${this._escape(hpi)}</span></div>` : ''}
                        </div>
                    </div>
                `;

                // Vitals Section
                html += `
                    <div class="ew-ref-section">
                        <div class="ew-ref-section-header" onclick="this.parentElement.classList.toggle('collapsed')">
                            <h6><i class="bi bi-heart-pulse me-1 text-danger"></i>Vitals</h6>
                            <a href="#" class="ew-ref-edit-link" onclick="event.preventDefault(); event.stopPropagation(); window._encounterWorkspace._showStep(0);" title="Edit Vitals">
                                <i class="bi bi-pencil-square"></i>
                            </a>
                        </div>
                        <div class="ew-ref-section-body">
                            ${currentVital ? this._renderRefVitals(currentVital) : '<div class="text-muted small fst-italic">No vitals recorded for this encounter</div>'}
                        </div>
                    </div>
                `;

                // History Section
                html += `
                    <div class="ew-ref-section">
                        <div class="ew-ref-section-header" onclick="this.parentElement.classList.toggle('collapsed')">
                            <h6><i class="bi bi-clock-history me-1 text-info"></i>History</h6>
                            <a href="#" class="ew-ref-edit-link" onclick="event.preventDefault(); event.stopPropagation(); window._encounterWorkspace._showStep(1);" title="Edit History">
                                <i class="bi bi-pencil-square"></i>
                            </a>
                        </div>
                        <div class="ew-ref-section-body">
                            ${this._renderRefHistory()}
                        </div>
                    </div>
                `;

                panel.innerHTML = html;
            } catch (e) {
                console.error('[EncounterWorkspace] Reference panel load error:', e);
                panel.innerHTML = '<div class="text-muted small p-2">Failed to load reference data</div>';
            }
        }

        /**
         * Render compact vitals for the reference panel
         */
        _renderRefVitals(v) {
            const items = [];
            if (v.SystolicBp && v.DiastolicBp) items.push(`<span class="ew-ref-vital"><strong>BP</strong> ${v.SystolicBp}/${v.DiastolicBp}</span>`);
            if (v.HeartRate) items.push(`<span class="ew-ref-vital"><strong>HR</strong> ${v.HeartRate}</span>`);
            if (v.Temperature) items.push(`<span class="ew-ref-vital"><strong>Temp</strong> ${v.Temperature}°F</span>`);
            if (v.SpO2) items.push(`<span class="ew-ref-vital"><strong>SpO2</strong> ${v.SpO2}%</span>`);
            if (v.RespiratoryRate) items.push(`<span class="ew-ref-vital"><strong>RR</strong> ${v.RespiratoryRate}</span>`);
            if (v.Weight) items.push(`<span class="ew-ref-vital"><strong>Wt</strong> ${v.Weight} lbs</span>`);
            if (v.Height) items.push(`<span class="ew-ref-vital"><strong>Ht</strong> ${v.Height} in</span>`);
            if (items.length === 0) return '<div class="text-muted small fst-italic">No values recorded</div>';
            return `<div class="ew-ref-vitals-grid">${items.join('')}</div>`;
        }

        /**
         * Render compact history for the reference panel on the Clinical Note step.
         * Shows Active items as primary chips, then Inactive/Discontinued/Resolved
         * items as muted+strikethrough chips so the clinician has full context for
         * note composition without losing the distinction.
         * Spec: rules/technical/history-review-soft-delete.md (section 11).
         */
        _renderRefHistory() {
            const all = (arr) => arr || [];

            // Allergies: active vs inactive (IsActive)
            const allergiesAll = all(this._refAllergiesAll || this._allergies);
            const allergiesActive = allergiesAll.filter(a => (a.IsActive ?? a.isActive) !== false);
            const allergiesInactive = allergiesAll.filter(a => (a.IsActive ?? a.isActive) === false);

            // Medications: Status 0=Active or 2=OnHold are "current"; 1=Discontinued, 3=Completed are historical
            const medsAll = all(this._refMedicationsAll || this._medications);
            const medsActive = medsAll.filter(m => { const s = m.Status ?? m.status ?? 0; return s === 0 || s === 2; });
            const medsInactive = medsAll.filter(m => { const s = m.Status ?? m.status ?? 0; return s === 1 || s === 3; });

            // Problems: Status 0=Active is current; 1=Resolved, 2=Inactive are historical
            const probAll = all(this._refProblemsAll || this._problems);
            const probActive = probAll.filter(p => (p.Status ?? p.status) === 0);
            const probInactive = probAll.filter(p => (p.Status ?? p.status) !== 0);

            const familyHx = all(this._familyHistory);
            const socialHx = all(this._socialHistory);

            const renderAllergen = a => this._escape(a.Allergen || a.AllergenName || 'Unknown');
            const renderMed      = m => this._escape(m.DrugName || m.MedicationName || 'Unknown');
            const renderProblem  = p => this._escape(p.Description || p.ProblemName || 'Unknown');
            const renderFamily   = f => `${this._escape(f.Relation || f.Relationship || '')}: ${this._escape(f.Condition || '')}`;
            const renderSocial   = s => `${this._escape(s.Category || '')}${s.Description ? ': ' + this._escape(s.Description) : ''}`;

            const stateLabelFor = (sectionKey, item) => {
                if (sectionKey === 'allergies') return 'Inactive';
                if (sectionKey === 'medications') {
                    const s = item.Status ?? item.status ?? 0;
                    return s === 1 ? 'Discontinued' : s === 3 ? 'Completed' : 'Inactive';
                }
                if (sectionKey === 'problems') {
                    const s = item.Status ?? item.status ?? 0;
                    return s === 1 ? 'Resolved' : 'Inactive';
                }
                return 'Inactive';
            };

            const groupHtml = (label, activeItems, inactiveItems, render, sectionKey) => {
                if (activeItems.length === 0 && inactiveItems.length === 0) return '';
                const activeChips = activeItems.map(i => `<span class="ew-ref-chip">${render(i)}</span>`).join('');
                const inactiveChips = inactiveItems.map(i => `<span class="ew-ref-chip ew-ref-chip-inactive" title="${stateLabelFor(sectionKey, i)}"><span class="ew-ref-chip-strike">${render(i)}</span></span>`).join('');
                return `
                    <div class="ew-ref-history-group">
                        <span class="ew-ref-history-label">${label} (${activeItems.length}${inactiveItems.length > 0 ? ` + ${inactiveItems.length}` : ''})</span>
                        <div class="ew-ref-history-items">
                            ${activeChips}
                            ${inactiveChips}
                        </div>
                    </div>
                `;
            };

            let html = '';
            html += groupHtml('Allergies',   allergiesActive, allergiesInactive, renderAllergen, 'allergies');
            html += groupHtml('Medications', medsActive,      medsInactive,      renderMed,      'medications');
            html += groupHtml('Problems',    probActive,      probInactive,      renderProblem,  'problems');
            html += groupHtml('Family Hx',   familyHx,        [],                renderFamily,   'familyHx');
            html += groupHtml('Social Hx',   socialHx,        [],                renderSocial,   'socialHx');

            if (!html) {
                html = '<div class="text-muted small fst-italic">No history on record</div>';
            }
            return html;
        }

        _openWriteNoteModal() {
            const enc = this.encounter;
            if (!enc.AppointmentId) {
                if (window.showToast) window.showToast('No appointment linked to this encounter', 'warning');
                return;
            }

            // Build appointment-like object for the existing modal
            const appt = {
                AppointmentId: enc.AppointmentId,
                PatientId: this.patientId,
                PatientName: `${this.patient.FirstName} ${this.patient.LastName}`,
                ProviderId: enc.ProviderId,
                ProviderName: enc.ProviderName,
                LocationId: enc.LocationId || null,
                StartTime: enc.EncounterDate || new Date().toISOString()
            };

            // Call existing GlobalBridge function
            if (typeof window.openCreateClinicalNote === 'function') {
                window.openCreateClinicalNote(appt);

                // Listen for modal close to refresh notes list
                const noteModal = document.getElementById('clinicalNoteModal');
                if (noteModal) {
                    noteModal.addEventListener('hidden.bs.modal', () => {
                        setTimeout(() => this._renderNoteStep(), 500);
                    }, { once: true });
                }
            } else {
                if (window.showToast) window.showToast('Write Note module not available', 'error');
            }
        }

        _openNoteInModal(noteId, isDraft = false) {
            const canEdit = isDraft && !this._isMaNurse;
            const fn = canEdit ? window.editClinicalNote : window.viewClinicalNote;
            if (typeof fn === 'function') {
                fn(noteId);

                // Listen for modal close to refresh notes list
                const modalIds = ['clinicalNoteModal', 'tabbedClinicalNoteModal'];
                for (const id of modalIds) {
                    const modal = document.getElementById(id);
                    if (modal) {
                        modal.addEventListener('hidden.bs.modal', () => {
                            setTimeout(() => this._renderNoteStep(), 500);
                        }, { once: true });
                    }
                }
            }
        }

        _openRecordingSession() {
            const enc = this.encounter;
            if (!enc.AppointmentId) {
                if (window.showToast) window.showToast('No appointment linked to this encounter', 'warning');
                return;
            }

            // Set the global appointment for the recording modal
            window.currentAppointmentForNotes = {
                AppointmentId: enc.AppointmentId,
                PatientId: this.patientId,
                PatientName: `${this.patient.FirstName} ${this.patient.LastName}`,
                ProviderId: enc.ProviderId,
                ProviderName: enc.ProviderName,
                StartTime: enc.EncounterDate || new Date().toISOString(),
                Type: enc.AppointmentType ?? null
            };

            // Open the recording session modal (reuses existing)
            if (typeof window.openRecordSessionModal === 'function') {
                window.openRecordSessionModal();

                // Listen for recording completion to refresh note step
                // Skip refresh when modal is just collapsing to floating bar
                const recordModal = document.getElementById('recordSessionModal');
                if (recordModal) {
                    const onRecordModalHidden = () => {
                        if (window.scribeModule && window.scribeModule._isCollapsedToBar) return;
                        recordModal.removeEventListener('hidden.bs.modal', onRecordModalHidden);
                        setTimeout(() => this._renderNoteStep(), 1000);
                    };
                    recordModal.addEventListener('hidden.bs.modal', onRecordModalHidden);
                }

                // Also listen for the progress modal close (after note generation)
                const progressModal = document.getElementById('recordingProgressModal');
                if (progressModal) {
                    progressModal.addEventListener('hidden.bs.modal', () => {
                        setTimeout(() => this._renderNoteStep(), 1000);
                    }, { once: true });
                }
            } else {
                if (window.showToast) window.showToast('Recording module not available', 'error');
            }
        }

        // _buildAutoPopulatedContent removed — no longer used since note creation is done via existing modals
        _buildAutoPopulatedContent() {
            const enc = this.encounter;
            const pt = this.patient;
            const dob = pt.DateOfBirth ? new Date(pt.DateOfBirth).toLocaleDateString() : '';
            const age = pt.DateOfBirth ? Math.floor((new Date() - new Date(pt.DateOfBirth)) / (365.25 * 24 * 60 * 60 * 1000)) : '';

            let html = '';

            // Patient header
            html += `<h5>Clinical Note</h5>`;
            html += `<p><strong>Patient:</strong> ${this._escape(pt.FirstName)} ${this._escape(pt.LastName)} | DOB: ${dob} (${age}y) | MRN: ${this._escape(pt.MRN || pt.Mrn || '')}</p>`;
            html += `<p><strong>Date of Service:</strong> ${enc.EncounterDate || new Date().toLocaleDateString()} | <strong>Provider:</strong> ${this._escape(enc.ProviderName || '')}</p>`;
            html += `<hr>`;

            // Chief Complaint
            html += `<h6>Chief Complaint</h6>`;
            html += `<p>${this._escape(enc.ChiefComplaint || 'Not documented')}</p>`;

            // HPI
            html += `<h6>History of Present Illness</h6>`;
            html += `<p>${this._escape(enc.HistoryOfPresentIllness || 'Not documented')}</p>`;

            // Vitals
            if (this._vitals && this._vitals.length > 0) {
                const v = this._vitals[0];
                html += `<h6>Vital Signs</h6>`;
                html += `<p>BP: ${v.BloodPressure || (v.SystolicBp + '/' + v.DiastolicBp) || '-'} | HR: ${v.HeartRate || '-'} | Temp: ${v.Temperature || '-'}°F | SpO2: ${v.SpO2 || '-'}% | RR: ${v.RespiratoryRate || '-'} | BMI: ${v.Bmi ? v.Bmi.toFixed(1) : '-'}</p>`;
            }

            // Allergies
            if (this._allergies && this._allergies.length > 0) {
                html += `<h6>Allergies</h6>`;
                html += `<p>${this._allergies.map(a => this._escape(a.AllergenName || '')).join(', ')}</p>`;
            } else {
                html += `<h6>Allergies</h6><p>NKDA</p>`;
            }

            // Medications
            if (this._medications && this._medications.length > 0) {
                const active = this._medications.filter(m => m.Status === 0);
                html += `<h6>Current Medications</h6>`;
                if (active.length > 0) {
                    html += `<ul>${active.map(m => `<li>${this._escape(m.DrugName || '')} ${this._escape(m.Dosage || '')} ${this._escape(m.Frequency || '')}</li>`).join('')}</ul>`;
                } else {
                    html += `<p>None</p>`;
                }
            }

            // Problems
            if (this._problems && this._problems.length > 0) {
                const active = this._problems.filter(p => p.Status === 0);
                html += `<h6>Active Problems</h6>`;
                if (active.length > 0) {
                    html += `<ul>${active.map(p => `<li>${this._escape(p.Description || '')} ${p.IcdCode ? '(' + this._escape(p.IcdCode) + ')' : ''}</li>`).join('')}</ul>`;
                } else {
                    html += `<p>None</p>`;
                }
            }

            // Orders
            if (this._orders && this._orders.length > 0) {
                const typeNames = { 0: 'Lab', 1: 'Imaging', 2: 'Referral' };
                html += `<h6>Orders & Referrals</h6>`;
                html += `<ul>${this._orders.map(o => `<li>[${typeNames[o.OrderType] || ''}] ${this._escape(o.Description || o.LabTestName || o.ImagingDescription || o.ReferralReason || '')}</li>`).join('')}</ul>`;
            }

            // Prescriptions
            if (this._prescriptions && this._prescriptions.length > 0) {
                html += `<h6>Prescriptions</h6>`;
                html += `<ul>${this._prescriptions.map(rx => `<li>${this._escape(rx.DrugName || '')} ${this._escape(rx.Strength || '')} — ${this._escape(rx.SIG || rx.Sig || '')}</li>`).join('')}</ul>`;
            }

            // Assessment & Plan placeholders
            html += `<h6>Assessment</h6>`;
            html += `<p>${this._escape(enc.Assessment || '[Enter assessment]')}</p>`;
            html += `<h6>Plan</h6>`;
            html += `<p>${this._escape(enc.Plan || '[Enter plan]')}</p>`;

            return html;
        }

        // _createNote, _saveNote, _signNote removed — now handled by existing modals via _openWriteNoteModal / _openNoteInModal

        // =====================================================
        // UNIFIED AI VOICE ENTRY (header bar)
        // =====================================================

        _initUnifiedVoiceBar() {
            // Don't show unified bar if telehealth scribe is active (it has its own)
            if (this._telehealthScribeActive) return;
            if (typeof UnifiedVoiceBar === 'undefined') return;

            const container = document.getElementById('ewUnifiedVoiceContainer');
            if (!container) return;

            this._unifiedVoiceBar = new UnifiedVoiceBar({
                patientId: this.patientId,
                encounterId: this.encounterId,
                onFieldsPopulated: (data) => this._handleUnifiedVoiceData(data)
            });
            this._unifiedVoiceBar.render(container);
        }

        _handleUnifiedVoiceData(data) {
            // Update sidebar checkmarks and AI badges when AI fills sections
            if (data.vitals) {
                this._markStepComplete(0, true);
                this._updateSidebarBadge(0, 'vitals');
            }
            if (data.history) {
                const historyKeys = ['allergies', 'medications', 'problems', 'familyHx', 'socialHx', 'immunizations'];
                const hasItems = historyKeys.some(k => Array.isArray(data.history[k]) && data.history[k].length > 0);
                if (hasItems) {
                    this._markStepComplete(1, true);
                    this._updateSidebarBadge(1, 'history');
                }
            }
            if (data.ccHpi && (data.ccHpi.chiefComplaint || data.ccHpi.hpiNarrative)) {
                this._markStepComplete(2, true);
                this._updateSidebarBadge(2, 'cc-hpi');
            }
        }

        _updateSidebarBadge(stepIndex, sectionKey) {
            const stepItem = document.querySelector(`.ew-step-item[data-step="${stepIndex}"]`);
            if (!stepItem) return;

            // Add a small AI badge if not already present
            let badge = stepItem.querySelector('.ew-ai-badge');
            if (!badge) {
                badge = document.createElement('span');
                badge.className = 'ew-ai-badge';
                badge.title = 'AI data added';
                badge.innerHTML = '<i class="bi bi-stars"></i>';
                stepItem.appendChild(badge);
            }
        }

        // =====================================================
        // PER-SECTION MEDOCS AI VOICE ENTRY (legacy, suppressed when unified bar is active)
        // =====================================================

        _initMedocsVoice(tabKey) {
            // Suppress per-tab voice bars when unified bar or telehealth scribe is active
            if (this._unifiedVoiceBar) return;
            if (this._telehealthScribeActive) return;
            if (typeof MedocsVoiceUI === 'undefined') return;

            const stepContent = document.getElementById('ewStepContent');
            if (!stepContent) return;

            const voiceContainer = document.createElement('div');
            voiceContainer.id = `ewMedocsVoice_${tabKey}`;
            voiceContainer.className = 'medocs-voice-container';
            stepContent.insertBefore(voiceContainer, stepContent.firstChild);

            const voiceUI = new MedocsVoiceUI({
                tabKey: tabKey,
                patientId: this.patientId,
                encounterId: this.encounterId,
                onFieldsPopulated: (tab, data) => this._handleVoiceDataPopulated(tab, data)
            });
            voiceUI.render(voiceContainer);
            this._medocsVoiceInstances[tabKey] = voiceUI;
        }

        _handleVoiceDataPopulated(tabKey, data) {
            switch (tabKey) {
                case 'vitals':
                    // Trigger BMI recalculation
                    const weightEl = document.getElementById('ewVitalWeight');
                    const heightEl = document.getElementById('ewVitalHeight');
                    if (weightEl && heightEl) {
                        weightEl.dispatchEvent(new Event('input', { bubbles: true }));
                    }
                    break;
                case 'history':
                    // History rows are saved inline by MedocsVoiceUI — update step completion
                    this._markStepComplete(1, true);
                    break;
                case 'cc-hpi':
                    // Auto-save is triggered by input events dispatched in MedocsVoiceUI
                    this._markStepComplete(2, true);
                    break;
            }
        }

        // =====================================================
        // DX & CPT CODES STEP (combined ICD-10 + CPT in single step)
        // =====================================================
        // Backend source of truth:
        //   Encounter.IcdSelections (JSON array) → BillingClaim.DiagnosisCodes (Box 21)
        //   Encounter.CptSelections (JSON array) → Charge rows (Box 24)
        //
        // IMPLEMENTATION (post-refactor, 2026-04-20):
        //   All rendering, search, AI suggestions, favorites, family overlay,
        //   and cart logic moved out to DxCptCodesComponent.js (see
        //   wwwroot/js/modules/dxcpt/DxCptCodesComponent.js). That component is
        //   the single owner of ICD-10 / CPT selection — same class is also used
        //   by the amendment modal (AmendmentAddendumModule.js). See the
        //   component's header comment for the OOP rationale ("CodeManager —
        //   one person, two consumers").
        //
        //   This method is now just: gate-check (notes must be signed) →
        //   mount the component into the step container → wire callbacks back
        //   to the workspace (save status mirror, step-complete checkmark,
        //   Next button navigating to step 7).
        // =====================================================

        async _renderDxCptCodesStep() {
            const container = document.getElementById('ewStepContent');
            if (!container) return;

            container.innerHTML = `<div class="text-center py-3"><div class="spinner-border spinner-border-sm"></div> Loading...</div>`;

            // Gate: clinical notes must exist and all be signed before codes.
            // This check stays in the workspace — it's a workflow concern, not a
            // picker concern. The component doesn't need to know about notes.
            if (this.encounter.AppointmentId) {
                this._clinicalNotes = await window.apiRequest(`/clinical-notes/by-appointment/${this.encounter.AppointmentId}`, { showLoader: false }).catch(() => []) || [];
            }
            const noteCount = this._clinicalNotes.length;
            const unsignedNotes = this._clinicalNotes.filter(n => n.Status !== 2);
            const allSigned = noteCount > 0 && unsignedNotes.length === 0;

            if (!allSigned) {
                container.innerHTML = `
                    <div class="text-center py-5">
                        <i class="bi bi-exclamation-triangle-fill text-warning fs-1"></i>
                        <h5 class="mt-3">Clinical Notes Required</h5>
                        <p class="text-muted">
                            ${noteCount === 0
                                ? 'Please add and sign all clinical notes before adding diagnosis &amp; procedure codes.'
                                : `${unsignedNotes.length} unsigned note(s) remaining. All clinical notes must be signed before adding diagnosis &amp; procedure codes.`}
                        </p>
                        <button class="btn btn-primary" onclick="window._encounterWorkspace._showStep(3)">
                            <i class="bi bi-file-earmark-medical me-1"></i>Go to Clinical Notes
                        </button>
                    </div>
                `;
                this._markStepComplete(6, false);
                return;
            }

            // Parse persisted selections from the encounter (source of truth in DB).
            let initialIcds = [];
            let initialCpts = [];
            if (this.encounter.IcdSelections) {
                try { initialIcds = JSON.parse(this.encounter.IcdSelections) || []; } catch (e) { initialIcds = []; }
            }
            if (this.encounter.CptSelections) {
                try { initialCpts = JSON.parse(this.encounter.CptSelections) || []; } catch (e) { initialCpts = []; }
            }

            // Keep legacy cache fields in sync — the Checkout step and _closeEncounter
            // still read these directly. Component.onChange also keeps them fresh.
            this._selectedIcdCodes = initialIcds;
            this._selectedCptCodes = initialCpts;

            // Destroy any prior instance (e.g., user left and returned to the step).
            if (this._dxCptComponent) {
                try { this._dxCptComponent.destroy(); } catch (e) { /* noop */ }
                this._dxCptComponent = null;
            }

            // Clear the container and mount the component.
            container.innerHTML = '';
            this._dxCptComponent = new window.DxCptCodesComponent({
                container,
                appointmentId: this.encounter.AppointmentId,
                encounterId: this.encounterId,
                patientId: this.patientId,
                initialIcds,
                initialCpts,
                autoSave: true,
                maxIcdCodes: 12,
                nextButton: {
                    label: 'Next',
                    onClick: () => this._showStep(7)
                },
                onChange: (cpts, icds) => {
                    // Mirror for Checkout/_closeEncounter consumers.
                    this._selectedCptCodes = cpts;
                    this._selectedIcdCodes = icds;
                },
                onSaveStatus: (status, meta) => {
                    // When a save completes, refresh the workspace's encounter snapshot
                    // so downstream reads see the latest JSON without another round-trip.
                    if (status === 'saved' && meta) {
                        this.encounter.IcdSelections = meta.icdJson;
                        this.encounter.CptSelections = meta.cptJson;
                    }
                },
                onStepComplete: (hasCodes) => this._markStepComplete(6, hasCodes)
            });
            await this._dxCptComponent.mount();
        }

        async _renderCheckoutStep() {
            const container = document.getElementById('ewStepContent');
            if (!container) return;

            container.innerHTML = `<div class="text-center py-3"><div class="spinner-border spinner-border-sm"></div> Loading summary...</div>`;

            // Reload data to get latest
            try {
                await this._loadEncounterData();
                // Load vitals/orders/rx if not cached
                if (!this._vitals) this._vitals = await window.apiRequest(`/patients/${this.patientId}/vitals`, { showLoader: false }).catch(() => []) || [];
                if (!this._orders) this._orders = await window.apiRequest(`/orders?patientId=${this.patientId}`, { showLoader: false }).catch(() => []) || [];
                if (!this._prescriptions) this._prescriptions = await window.apiRequest(`/prescriptions?patientId=${this.patientId}`, { showLoader: false }).catch(() => []) || [];
                // Load history data if not cached
                if (!this._allergies) {
                    const [allergies, medications, problems, familyHx, socialHx, immunizations] = await Promise.all([
                        window.apiRequest(`/patients/${this.patientId}/allergies`, { showLoader: false }).catch(() => []),
                        window.apiRequest(`/patients/${this.patientId}/medications`, { showLoader: false }).catch(() => []),
                        window.apiRequest(`/patients/${this.patientId}/problems`, { showLoader: false }).catch(() => []),
                        window.apiRequest(`/patients/${this.patientId}/family-history`, { showLoader: false }).catch(() => []),
                        window.apiRequest(`/patients/${this.patientId}/social-history`, { showLoader: false }).catch(() => []),
                        window.apiRequest(`/patients/${this.patientId}/immunizations`, { showLoader: false }).catch(() => [])
                    ]);
                    this._allergies = allergies || [];
                    this._medications = (medications || []).filter(m => (m.Status ?? m.status) === 0);
                    this._problems = (problems || []).filter(p => (p.Status ?? p.status) === 0);
                    this._familyHistory = familyHx || [];
                    this._socialHistory = socialHx || [];
                    this._immunizations = immunizations || [];
                }
                // Load all clinical notes for this encounter
                if (this.encounter.AppointmentId) {
                    this._clinicalNotes = await window.apiRequest(`/clinical-notes/by-appointment/${this.encounter.AppointmentId}`, { showLoader: false }).catch(() => []) || [];
                }
            } catch (e) {}

            const enc = this.encounter;
            const hasVitals = this._vitals && this._vitals.some(v => v.EncounterId === this.encounterId);
            // Count how many history sections have data added
            const historyAddedCount = [
                this._allergies, this._medications, this._problems,
                this._familyHistory, this._socialHistory, this._immunizations
            ].filter(arr => Array.isArray(arr) && arr.length > 0).length;
            const hasCC = !!(enc.ChiefComplaint && enc.ChiefComplaint.trim());
            const ordersCount = this._orders ? this._orders.length : 0;
            const rxCount = this._prescriptions ? this._prescriptions.length : 0;

            // Multi-note status
            const noteCount = this._clinicalNotes.length;
            const draftNotes = this._clinicalNotes.filter(n => n.Status === 0);
            const signedNotes = this._clinicalNotes.filter(n => n.Status === 2);
            const noteStatusText = noteCount === 0 ? 'None' : `${signedNotes.length} signed, ${draftNotes.length} draft`;
            const noteStatusBadge = noteCount === 0 ? 'secondary' : (draftNotes.length > 0 ? 'warning' : 'success');
            const isSigned = enc.StatusName === 'Signed';

            this._markStepComplete(7, isSigned);

            container.innerHTML = `
                <div class="row g-3 mb-4">
                    <div class="col-md-4">
                        <div class="card text-center ${hasVitals ? 'border-success' : 'border-warning'}">
                            <div class="card-body py-3">
                                <i class="bi ${hasVitals ? 'bi-check-circle-fill text-success' : 'bi-exclamation-circle text-warning'} fs-3"></i>
                                <h6 class="mt-2 mb-0">Vitals</h6>
                                <small class="text-muted">${hasVitals ? 'Recorded' : 'Not recorded'}</small>
                            </div>
                        </div>
                    </div>
                    <div class="col-md-4">
                        <div class="card text-center ${historyAddedCount > 0 ? 'border-success' : 'border-secondary'}">
                            <div class="card-body py-3">
                                <i class="bi ${historyAddedCount > 0 ? 'bi-check-circle-fill text-success' : 'bi-dash-circle text-secondary'} fs-3"></i>
                                <h6 class="mt-2 mb-0">History</h6>
                                <small class="text-muted">Added ${historyAddedCount}/6</small>
                            </div>
                        </div>
                    </div>
                    <div class="col-md-4">
                        <div class="card text-center ${hasCC ? 'border-success' : 'border-warning'}">
                            <div class="card-body py-3">
                                <i class="bi ${hasCC ? 'bi-check-circle-fill text-success' : 'bi-exclamation-circle text-warning'} fs-3"></i>
                                <h6 class="mt-2 mb-0">CC & HPI</h6>
                                <small class="text-muted">${hasCC ? 'Documented' : 'Not entered'}</small>
                            </div>
                        </div>
                    </div>
                    <div class="col-md-4">
                        <div class="card text-center ${ordersCount > 0 ? 'border-success' : 'border-secondary'}">
                            <div class="card-body py-3">
                                <i class="bi ${ordersCount > 0 ? 'bi-check-circle-fill text-success' : 'bi-dash-circle text-secondary'} fs-3"></i>
                                <h6 class="mt-2 mb-0">Orders & Referrals</h6>
                                <small class="text-muted">${ordersCount} order(s)</small>
                            </div>
                        </div>
                    </div>
                    <div class="col-md-4">
                        <div class="card text-center ${rxCount > 0 ? 'border-success' : 'border-secondary'}">
                            <div class="card-body py-3">
                                <i class="bi ${rxCount > 0 ? 'bi-check-circle-fill text-success' : 'bi-dash-circle text-secondary'} fs-3"></i>
                                <h6 class="mt-2 mb-0">Prescriptions</h6>
                                <small class="text-muted">${rxCount} prescription(s)</small>
                            </div>
                        </div>
                    </div>
                    <div class="col-md-4">
                        <div class="card text-center border-${noteStatusBadge}">
                            <div class="card-body py-3">
                                <i class="bi ${noteCount === 0 ? 'bi-x-circle text-secondary' : draftNotes.length > 0 ? 'bi-exclamation-circle text-warning' : 'bi-check-circle-fill text-success'} fs-3"></i>
                                <h6 class="mt-2 mb-0">Clinical Notes</h6>
                                <small class="text-muted">${noteStatusText}</small>
                            </div>
                        </div>
                    </div>
                    <div class="col-md-4">
                        <div class="card text-center ${this._selectedCptCodes.length > 0 ? 'border-success' : 'border-danger'}">
                            <div class="card-body py-3">
                                <i class="bi ${this._selectedCptCodes.length > 0 ? 'bi-check-circle-fill text-success' : 'bi-exclamation-triangle-fill text-danger'} fs-3"></i>
                                <h6 class="mt-2 mb-0">CPT Codes</h6>
                                <small class="text-muted">${this._selectedCptCodes.length > 0 ? this._selectedCptCodes.length + ' code(s) selected' : 'Required'}</small>
                            </div>
                        </div>
                    </div>
                </div>

                <hr>

                ${isSigned ? `
                    <div class="text-center mt-4">
                        <i class="bi bi-check-circle-fill text-success fs-1"></i>
                        <h5 class="mt-2">Encounter Complete</h5>
                        <p class="text-muted">This encounter has been signed and closed.</p>
                        <a href="/Home/Dashboard" class="btn btn-primary">
                            <i class="bi bi-house me-1"></i>Back to Dashboard
                        </a>
                    </div>
                ` : `
                    ${noteCount === 0 ? `
                        <div class="text-center mt-3">
                            <div class="alert alert-danger d-inline-flex align-items-center gap-2 px-4">
                                <i class="bi bi-exclamation-triangle-fill fs-5"></i>
                                <span>At least one clinical note is required before closing the encounter.</span>
                            </div>
                        </div>
                    ` : draftNotes.length > 0 ? `
                        <div class="text-center mt-3">
                            <div class="alert alert-warning d-inline-flex align-items-center gap-2 px-4">
                                <i class="bi bi-pen-fill fs-5"></i>
                                <span>All clinical notes must be signed before closing. ${draftNotes.length} unsigned note(s).</span>
                            </div>
                        </div>
                    ` : this._selectedCptCodes.length === 0 ? `
                        <div class="text-center mt-3">
                            <div class="alert alert-danger d-inline-flex align-items-center gap-2 px-4">
                                <i class="bi bi-exclamation-triangle-fill fs-5"></i>
                                <span>CPT codes are required before closing the encounter.</span>
                            </div>
                        </div>
                    ` : ''}

                    <div class="d-flex justify-content-center gap-3 mt-3">
                        ${noteCount === 0 ? `
                            <button class="btn btn-success btn-lg" disabled>
                                <i class="bi bi-lock me-1"></i>Close Encounter
                            </button>
                        ` : draftNotes.length > 0 ? `
                            ${!this._isMaNurse ? `
                                <button class="btn btn-success btn-lg" id="ewBtnSignAndClose">
                                    <i class="bi bi-pen me-1"></i>Sign All Notes & Close Encounter
                                </button>
                            ` : `
                                <button class="btn btn-success btn-lg" disabled>
                                    <i class="bi bi-lock me-1"></i>Close Encounter (Notes Unsigned)
                                </button>
                            `}
                        ` : `
                            <button class="btn btn-success btn-lg" id="ewBtnCloseEncounter">
                                <i class="bi bi-check-circle me-1"></i>Close Encounter
                            </button>
                        `}
                        <a href="/Home/Dashboard" class="btn btn-outline-secondary btn-lg">
                            <i class="bi bi-arrow-left me-1"></i>Save & Continue Later
                        </a>
                    </div>
                `}
            `;

            // Bind buttons
            document.getElementById('ewBtnSignAndClose')?.addEventListener('click', () => this._signAndCloseEncounter());
            document.getElementById('ewBtnCloseEncounter')?.addEventListener('click', () => this._closeEncounter());
        }

        async _signAndCloseEncounter() {
            // Sign ALL draft notes before closing
            const draftNotes = this._clinicalNotes.filter(n => n.Status === 0);
            if (draftNotes.length > 0) {
                for (const note of draftNotes) {
                    try {
                        await window.apiRequest(`/clinical-notes/${note.ClinicalNoteId}/sign`, {
                            method: 'POST',
                            body: { SignatureData: 'electronic-signature' }
                        });
                    } catch (e) {
                        console.error(`[EncounterWorkspace] Failed to sign note ${note.ClinicalNoteId}:`, e);
                        if (window.showToast) window.showToast(`Failed to sign note "${note.TemplateName || 'Unknown'}". Cannot close encounter.`, 'error');
                        return;
                    }
                }
                console.log(`[EncounterWorkspace] ${draftNotes.length} note(s) signed`);
            }

            await this._closeEncounter();
        }

        async _closeEncounter() {
            const enc = this.encounter;

            // Load CPT selections from DB if in-memory is empty
            if ((!this._selectedCptCodes || this._selectedCptCodes.length === 0) && enc.CptSelections) {
                try { this._selectedCptCodes = JSON.parse(enc.CptSelections) || []; } catch (e) {}
            }

            // Validate CPT codes are selected
            if (!this._selectedCptCodes || this._selectedCptCodes.length === 0) {
                if (window.showToast) window.showToast('CPT codes are required before closing the encounter.', 'error');
                this._showStep(6); // Navigate to Dx & CPT Codes step
                return;
            }

            if (enc.AppointmentId) {
                try {
                    await window.apiRequest(`/appointments/${enc.AppointmentId}/checkout-with-cpt`, {
                        method: 'POST',
                        body: {
                            CptCodes: this._selectedCptCodes.map(c => ({
                                CptCode: c.cptCode,
                                Description: c.description,
                                Units: c.units
                            }))
                        }
                    });
                    // Redirect to dashboard
                    setTimeout(() => {
                        window.location.href = '/Home/Dashboard';
                    }, 1000);
                } catch (e) {
                    console.error('[EncounterWorkspace] Checkout error:', e);
                    const msg = e?.message || e?.Message || 'Failed to close encounter';
                    if (window.showToast) window.showToast(msg, 'error');
                    else alert(msg);
                }
            } else {
                // No appointment linked — just navigate back
                window.location.href = '/Home/Dashboard';
            }
        }

        // =====================================================
        // AUTO-SAVE
        // =====================================================

        _showSaveIndicator(state) {
            const el = document.getElementById('ewAutoSaveIndicator');
            if (!el) return;
            switch (state) {
                case 'saving':
                    el.innerHTML = '<i class="bi bi-cloud-arrow-up text-warning"></i> Saving...';
                    break;
                case 'saved':
                    this._lastSaveTime = new Date();
                    el.innerHTML = `<i class="bi bi-cloud-check text-success"></i> Saved`;
                    break;
                case 'error':
                    el.innerHTML = '<i class="bi bi-cloud-slash text-danger"></i> Save failed';
                    break;
                default:
                    el.innerHTML = '<i class="bi bi-cloud-check"></i> Ready';
            }
        }

        // =====================================================
        // UTILITIES
        // =====================================================

        _escape(str) {
            if (!str) return '';
            const div = document.createElement('div');
            div.textContent = str;
            return div.innerHTML;
        }

        _escapeAttr(str) {
            if (!str) return '';
            return str.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/'/g, '&#39;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
        }

        _showError(message) {
            document.getElementById('encounterLoading')?.classList.add('d-none');
            document.getElementById('encounterContent')?.classList.add('d-none');
            const errorEl = document.getElementById('encounterError');
            if (errorEl) {
                errorEl.classList.remove('d-none');
                const msgEl = errorEl.querySelector('p');
                if (msgEl) msgEl.textContent = message || 'The encounter could not be loaded.';
            }
        }

        // =====================================================
        // PATIENT STICKY NOTES
        // =====================================================

        _initStickyNotes() {
            this._stickyNotes = [];
            this._stickyNotesOpen = false;

            // Create the floating panel container
            const panel = document.createElement('div');
            panel.id = 'ewStickyNotesPanel';
            panel.className = 'ew-sticky-notes-panel d-none';
            panel.innerHTML = `
                <div class="ew-sticky-notes-header">
                    <h6 class="mb-0"><i class="bi bi-sticky-fill text-warning me-2"></i>Patient Sticky Notes</h6>
                    <button class="btn btn-sm btn-link text-muted p-0" onclick="window._encounterWorkspace.toggleStickyNotes()" title="Close">
                        <i class="bi bi-x-lg"></i>
                    </button>
                </div>
                <div class="ew-sticky-notes-add">
                    <div class="input-group input-group-sm">
                        <input type="text" class="form-control" id="ewStickyNoteInput" placeholder="Add a quick note..." maxlength="1000">
                        <button class="btn btn-warning" id="ewStickyNoteAddBtn" onclick="window._encounterWorkspace.addStickyNote()">
                            <i class="bi bi-plus-lg"></i>
                        </button>
                    </div>
                </div>
                <div class="ew-sticky-notes-list" id="ewStickyNotesList">
                    <div class="text-center text-muted py-3"><small>Loading notes...</small></div>
                </div>
            `;
            document.getElementById('encounterContent').appendChild(panel);

            // Enter key to add note
            panel.querySelector('#ewStickyNoteInput').addEventListener('keydown', (e) => {
                if (e.key === 'Enter') {
                    e.preventDefault();
                    this.addStickyNote();
                }
            });

            // Load existing notes
            this._loadStickyNotes();
        }

        async _loadStickyNotes() {
            try {
                const notes = await window.apiRequest(`/patients/${this.patientId}/sticky-notes`, { showLoader: false });
                this._stickyNotes = notes || [];
                this._renderStickyNotesList();
                this._updateStickyNotesCount();
            } catch (err) {
                console.error('[StickyNotes] Load error:', err);
            }
        }

        _renderStickyNotesList() {
            const listEl = document.getElementById('ewStickyNotesList');
            if (!listEl) return;

            if (!this._stickyNotes || this._stickyNotes.length === 0) {
                listEl.innerHTML = `
                    <div class="text-center text-muted py-4">
                        <i class="bi bi-sticky" style="font-size: 2rem; opacity: 0.3;"></i>
                        <p class="mt-2 mb-0"><small>No sticky notes yet.<br>Add one above to remember something about this patient.</small></p>
                    </div>`;
                return;
            }

            listEl.innerHTML = this._stickyNotes.map(note => `
                <div class="ew-sticky-note-item" data-id="${note.PatientStickyNoteId}">
                    <div class="ew-sticky-note-content">${this._escape(note.Content)}</div>
                    <div class="ew-sticky-note-meta">
                        <span><i class="bi bi-person me-1"></i>${this._escape(note.CreatedByName)}</span>
                        <span><i class="bi bi-clock me-1"></i>${this._formatStickyNoteDate(note.CreatedAt)}</span>
                        <button class="btn btn-link btn-sm text-danger p-0 ms-auto" title="Delete" onclick="window._encounterWorkspace.deleteStickyNote(${note.PatientStickyNoteId})">
                            <i class="bi bi-trash3"></i>
                        </button>
                    </div>
                </div>
            `).join('');
        }

        _updateStickyNotesCount() {
            const badge = document.getElementById('ewStickyNotesCount');
            if (!badge) return;
            const count = this._stickyNotes?.length || 0;
            badge.textContent = count;
            badge.classList.toggle('d-none', count === 0);
        }

        _formatStickyNoteDate(dateStr) {
            if (!dateStr) return '';
            const d = new Date(dateStr);
            const now = new Date();
            const diffMs = now - d;
            const diffMins = Math.floor(diffMs / 60000);
            if (diffMins < 1) return 'Just now';
            if (diffMins < 60) return `${diffMins}m ago`;
            const diffHrs = Math.floor(diffMins / 60);
            if (diffHrs < 24) return `${diffHrs}h ago`;
            const diffDays = Math.floor(diffHrs / 24);
            if (diffDays < 7) return `${diffDays}d ago`;
            return d.toLocaleDateString();
        }

        toggleStickyNotes() {
            const panel = document.getElementById('ewStickyNotesPanel');
            if (!panel) return;
            this._stickyNotesOpen = !this._stickyNotesOpen;
            panel.classList.toggle('d-none', !this._stickyNotesOpen);
            if (this._stickyNotesOpen) {
                const input = document.getElementById('ewStickyNoteInput');
                if (input) setTimeout(() => input.focus(), 100);
            }
        }

        // =====================================================
        // PATIENT UPLOADED DOCUMENTS (Encounter Header)
        // =====================================================

        togglePatientDocs() {
            let panel = document.getElementById('ewPatientDocsPanel');
            if (!panel) {
                // Create panel on first toggle
                panel = document.createElement('div');
                panel.id = 'ewPatientDocsPanel';
                panel.className = 'card shadow-sm mb-2';
                panel.style.cssText = 'position:absolute; right:16px; top:60px; width:400px; z-index:100; max-height:400px; overflow-y:auto;';
                panel.innerHTML = `
                    <div class="card-header d-flex justify-content-between align-items-center py-2">
                        <h6 class="mb-0"><i class="bi bi-folder2-open text-info me-2"></i>Patient Uploads</h6>
                        <button class="btn btn-sm btn-link text-muted p-0" onclick="window._encounterWorkspace.togglePatientDocs()" title="Close"><i class="bi bi-x-lg"></i></button>
                    </div>
                    <div class="card-body p-0" id="ewPatientDocsList">
                        <div class="text-center text-muted py-3"><div class="spinner-border spinner-border-sm"></div> Loading...</div>
                    </div>`;
                document.getElementById('encounterHeader').style.position = 'relative';
                document.getElementById('encounterHeader').appendChild(panel);
                this._loadPatientDocs();
                return;
            }
            panel.classList.toggle('d-none');
            if (!panel.classList.contains('d-none')) this._loadPatientDocs();
        }

        async _loadPatientDocs() {
            const listEl = document.getElementById('ewPatientDocsList');
            if (!listEl) return;
            try {
                const patientId = this.encounter?.PatientId || this.patient?.PatientId;
                const docs = await apiRequest(`/patients/${patientId}/documents`);
                const patientDocs = (docs || []).filter(d => d.IsPatientUploaded === true);
                const countBadge = document.getElementById('ewPatientDocsCount');

                if (!patientDocs.length) {
                    listEl.innerHTML = '<div class="text-center text-muted py-3"><i class="bi bi-folder2" style="font-size:1.5rem;"></i><p class="small mt-1 mb-0">No patient uploads</p></div>';
                    if (countBadge) countBadge.classList.add('d-none');
                    return;
                }

                if (countBadge) {
                    countBadge.textContent = patientDocs.length;
                    countBadge.classList.remove('d-none');
                }

                listEl.innerHTML = patientDocs.map(d => {
                    const icon = d.ContentType?.includes('pdf') ? 'bi-file-earmark-pdf' : d.ContentType?.startsWith('image/') ? 'bi-file-earmark-image' : 'bi-file-earmark';
                    const iconBg = d.ContentType?.includes('pdf') ? '#FEE2E2' : d.ContentType?.startsWith('image/') ? '#DBEAFE' : '#F3F4F6';
                    const iconColor = d.ContentType?.includes('pdf') ? '#DC2626' : d.ContentType?.startsWith('image/') ? '#2563EB' : '#6B7280';
                    const size = d.FileSize < 1048576 ? (d.FileSize / 1024).toFixed(0) + ' KB' : (d.FileSize / 1048576).toFixed(1) + ' MB';
                    const date = d.CreatedAt ? new Date(d.CreatedAt).toLocaleDateString('en-US', { month: 'short', day: 'numeric' }) : '';
                    return `
                        <div class="d-flex align-items-center gap-2 p-2 border-bottom" style="cursor:pointer;" onclick="FileViewerModal.showFromFetch({fetchUrl:'/api/patients/${patientId}/documents/${d.DocumentId}',fileName:'${(d.FileName || 'Document').replace(/'/g, "\\'")}'})">
                            <div style="width:32px;height:32px;border-radius:6px;background:${iconBg};color:${iconColor};display:flex;align-items:center;justify-content:center;font-size:14px;flex-shrink:0;">
                                <i class="bi ${icon}"></i>
                            </div>
                            <div class="flex-grow-1 min-width-0">
                                <div style="font-size:12px;font-weight:500;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;">${d.FileName || 'Document'}</div>
                                <div style="font-size:11px;color:#6B7280;">${size} &middot; ${date}</div>
                            </div>
                            <i class="bi bi-download text-muted"></i>
                        </div>`;
                }).join('');
            } catch (e) {
                listEl.innerHTML = '<div class="text-center text-danger py-2 small">Failed to load documents</div>';
            }
        }

        async _checkPatientDocs() {
            try {
                const patientId = this.encounter?.PatientId || this.patient?.PatientId;
                const docs = await apiRequest(`/patients/${patientId}/documents`);
                const patientDocs = (docs || []).filter(d => d.IsPatientUploaded === true);
                const countBadge = document.getElementById('ewPatientDocsCount');
                if (countBadge && patientDocs.length > 0) {
                    countBadge.textContent = patientDocs.length;
                    countBadge.classList.remove('d-none');
                }
            } catch { /* ignore */ }
        }

        async addStickyNote() {
            const input = document.getElementById('ewStickyNoteInput');
            const content = input?.value?.trim();
            if (!content) return;

            const btn = document.getElementById('ewStickyNoteAddBtn');
            btn.disabled = true;
            try {
                const result = await window.apiRequest(`/patients/${this.patientId}/sticky-notes`, {
                    method: 'POST',
                    body: { Content: content },
                    showLoader: false
                });
                if (result) {
                    this._stickyNotes.unshift(result);
                    this._renderStickyNotesList();
                    this._updateStickyNotesCount();
                    input.value = '';
                }
            } catch (err) {
                console.error('[StickyNotes] Add error:', err);
                if (typeof Toast !== 'undefined') Toast.error('Failed to add sticky note');
            } finally {
                btn.disabled = false;
            }
        }

        async deleteStickyNote(noteId) {
            const confirmed = await ConfirmDialog.confirmDelete('this sticky note');
            if (!confirmed) return;
            try {
                await window.apiRequest(`/patients/${this.patientId}/sticky-notes/${noteId}`, {
                    method: 'DELETE',
                    showLoader: false
                });
                this._stickyNotes = this._stickyNotes.filter(n => n.PatientStickyNoteId !== noteId);
                this._renderStickyNotesList();
                this._updateStickyNotesCount();
            } catch (err) {
                console.error('[StickyNotes] Delete error:', err);
                if (typeof Toast !== 'undefined') Toast.error('Failed to delete sticky note');
            }
        }

        // =====================================================
        // TELEHEALTH VIDEO PANEL
        // =====================================================

        /**
         * Initialize the floating telehealth video panel.
         * - Fetches Jitsi config from server
         * - Injects floating panel HTML
         * - Connects to TelehealthHub via SignalR
         * - Loads Jitsi External API and starts video
         * - Makes panel draggable, collapsible, expandable
         */
        async _initTelehealthPanel() {
            console.log('[Telehealth] Initializing telehealth video panel...');

            // Telehealth state
            this._telehealthConnection = null;
            this._jitsiApi = null;
            this._telehealthMinimized = false;
            this._telehealthExpanded = true;
            this._patientWaiting = false;
            this._patientAdmitted = false;

            // Get the appointment ID from the encounter
            const appointmentId = this.encounter?.AppointmentId;
            if (!appointmentId) {
                console.warn('[Telehealth] No AppointmentId on encounter, skipping telehealth panel');
                return;
            }

            try {
                // Step 1: Fetch Jitsi config from server
                const config = await window.apiRequest(`/telehealth/config/${appointmentId}`, { showLoader: false });
                if (!config || !config.RoomName) {
                    console.warn('[Telehealth] No config returned for appointment', appointmentId);
                    return;
                }
                this._telehealthConfig = config;
                console.log('[Telehealth] Config loaded:', config.JitsiDomain, config.RoomName);

                // Step 2: Inject floating panel HTML
                this._injectTelehealthPanelHTML();

                // Step 3: Connect to SignalR for patient waiting notifications
                await this._connectTelehealthSignalR(appointmentId);

                // Step 4: Load Jitsi External API script (JaaS-aware)
                await this._loadJitsiApi(config);

                // Step 5: Start Jitsi video
                this._startJitsi(config);

                // Step 6: Patient presence is now detected by SignalR hub automatically
                // When JoinSession is called (Step 3), the hub checks for active patient connections

                // Step 7: Initialize Telehealth AI Scribe (audio capture + field population)
                this._initTelehealthScribe();

            } catch (err) {
                console.error('[Telehealth] Failed to initialize telehealth panel:', err);
                if (typeof Toast !== 'undefined') Toast.error('Failed to start telehealth video. Please refresh the page.');
            }
        }

        /**
         * Inject the floating video panel HTML into the encounter workspace.
         */
        _injectTelehealthPanelHTML() {
            const panel = document.createElement('div');
            panel.id = 'telehealthVideoPanel';
            // Start in shrunk (base) size, NOT expanded. Provider/nurse can expand if they want.
            panel.className = 'telehealth-video-panel';
            // Keep internal flag in sync with initial state.
            this._telehealthExpanded = false;
            panel.innerHTML = `
                <div class="telehealth-panel-header" id="telehealthPanelHeader">
                    <div class="telehealth-panel-title">
                        <i class="bi bi-camera-video-fill me-2"></i>
                        <span>Telehealth Video</span>
                        <span class="telehealth-panel-badge d-none" id="telehealthWaitingBadge">
                            <i class="bi bi-person-fill"></i> Patient waiting
                        </span>
                    </div>
                    <div class="telehealth-panel-controls">
                        <button class="btn btn-sm btn-link text-white p-0 me-2" title="Minimize"
                            onclick="window._encounterWorkspace._toggleTelehealthMinimize()">
                            <i class="bi bi-dash-lg"></i>
                        </button>
                        <button class="btn btn-sm btn-link text-white p-0 me-2" title="Expand"
                            onclick="window._encounterWorkspace._toggleTelehealthExpand()">
                            <i class="bi bi-arrows-fullscreen" id="telehealthExpandIcon"></i>
                        </button>
                        <button class="btn btn-sm btn-link text-white p-0" title="End Call"
                            onclick="window._encounterWorkspace._endTelehealthCall()">
                            <i class="bi bi-telephone-x-fill"></i>
                        </button>
                    </div>
                </div>
                <div class="telehealth-panel-body" id="telehealthPanelBody">
                    <div class="telehealth-waiting-banner d-none" id="telehealthWaitingBanner">
                        <div class="telehealth-waiting-info">
                            <i class="bi bi-person-video3 me-2"></i>
                            <span id="telehealthWaitingName">Patient is in the waiting room</span>
                        </div>
                        <button class="btn btn-sm btn-success" id="telehealthAdmitBtn"
                            onclick="window._encounterWorkspace._admitPatient()">
                            <i class="bi bi-door-open me-1"></i>Admit
                        </button>
                    </div>
                    <div class="telehealth-video-container" id="telehealthVideoContainer">
                        <div class="telehealth-video-loading">
                            <div class="spinner-border text-light" role="status"></div>
                            <p class="text-light mt-2 mb-0">Starting video...</p>
                        </div>
                    </div>
                </div>
            `;
            document.getElementById('encounterContent').appendChild(panel);

            // Make the panel draggable by its header
            this._makeDraggable(panel, document.getElementById('telehealthPanelHeader'));
        }

        /**
         * Check if the patient is already in the waiting room when the clinician opens the workspace.
         * This handles the race condition where the patient joins before the clinician.
         */
        // Note: Patient waiting detection is now fully handled by SignalR presence.
        // When provider calls JoinSession, the hub checks if patient is already connected
        // and sends PatientWaiting directly. No DB polling needed.

        /**
         * Connect to TelehealthHub via SignalR (authenticated).
         * Listens for PatientWaiting and PatientLeft events.
         */
        async _connectTelehealthSignalR(appointmentId) {
            if (typeof signalR === 'undefined') {
                console.warn('[Telehealth] SignalR not available');
                return;
            }

            const token = localStorage.getItem('authToken') || localStorage.getItem('token');
            if (!token) {
                console.warn('[Telehealth] No auth token for SignalR');
                return;
            }

            try {
                this._telehealthConnection = new signalR.HubConnectionBuilder()
                    .withUrl('/hubs/telehealth', {
                        accessTokenFactory: () => token
                    })
                    .withAutomaticReconnect([2000, 4000, 8000, 16000])
                    .configureLogging(signalR.LogLevel.Warning)
                    .build();

                // Listen for patient joining the waiting room
                this._telehealthConnection.on('PatientWaiting', (data) => {
                    console.log('[Telehealth] Patient waiting:', data);
                    this._onPatientWaiting(data);
                });

                // Listen for patient leaving
                this._telehealthConnection.on('PatientLeft', (data) => {
                    console.log('[Telehealth] Patient left:', data);
                    this._onPatientLeft(data);
                });

                // Reconnect handler — rejoin the session group
                this._telehealthConnection.onreconnected(() => {
                    console.log('[Telehealth] SignalR reconnected, rejoining session');
                    this._telehealthConnection.invoke('JoinSession', appointmentId).catch(err =>
                        console.error('[Telehealth] Failed to rejoin session:', err));
                });

                await this._telehealthConnection.start();
                console.log('[Telehealth] SignalR connected');

                // Join the telehealth session group for this appointment
                await this._telehealthConnection.invoke('JoinSession', appointmentId);
                console.log('[Telehealth] Joined session group for appointment', appointmentId);

            } catch (err) {
                console.error('[Telehealth] SignalR connection failed:', err);
                // Non-fatal — video still works, just no real-time patient waiting notifications
            }
        }

        /**
         * Handle patient waiting notification — show admit banner.
         */
        _onPatientWaiting(data) {
            this._patientWaiting = true;
            const banner = document.getElementById('telehealthWaitingBanner');
            const badge = document.getElementById('telehealthWaitingBadge');
            const nameEl = document.getElementById('telehealthWaitingName');

            if (banner) banner.classList.remove('d-none');
            if (badge) badge.classList.remove('d-none');
            if (nameEl) {
                const patientName = data?.PatientName || data?.patientName || 'Patient';
                nameEl.textContent = `${patientName} is in the waiting room`;
            }

        }

        /**
         * Handle patient left notification — hide admit banner.
         */
        _onPatientLeft(data) {
            this._patientWaiting = false;
            const banner = document.getElementById('telehealthWaitingBanner');
            const badge = document.getElementById('telehealthWaitingBadge');

            if (banner) banner.classList.add('d-none');
            if (badge) badge.classList.add('d-none');
        }

        /**
         * Admit the patient into the video call.
         * POST to /api/telehealth/admit → sends PatientAdmitted event to patient.
         */
        async _admitPatient() {
            const appointmentId = this.encounter?.AppointmentId;
            if (!appointmentId) return;

            const btn = document.getElementById('telehealthAdmitBtn');
            if (btn) {
                btn.disabled = true;
                btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Admitting...';
            }

            try {
                await window.apiRequest('/telehealth/admit', {
                    method: 'POST',
                    body: { AppointmentId: appointmentId },
                    showLoader: false
                });

                this._patientAdmitted = true;
                this._patientWaiting = false;

                // Update UI — hide waiting banner, show admitted confirmation
                const banner = document.getElementById('telehealthWaitingBanner');
                const badge = document.getElementById('telehealthWaitingBadge');
                if (banner) banner.classList.add('d-none');
                if (badge) badge.classList.add('d-none');

                console.log('[Telehealth] Patient admitted successfully');

                // Show connecting overlay on video panel
                this._showConnectingOverlay();

                // Auto-start AI Scribe after admitting patient
                this._autoStartTelehealthScribe();

            } catch (err) {
                console.error('[Telehealth] Failed to admit patient:', err);
                if (typeof Toast !== 'undefined') {
                    Toast.error('Failed to admit patient. Please try again.');
                }
                // Re-enable button
                if (btn) {
                    btn.disabled = false;
                    btn.innerHTML = '<i class="bi bi-door-open me-1"></i>Admit';
                }
            }
        }

        /**
         * Dynamically load the Jitsi Meet External API script.
         */
        _loadJitsiApi(config) {
            return new Promise((resolve, reject) => {
                // Check if already loaded
                if (typeof JitsiMeetExternalAPI !== 'undefined') {
                    resolve();
                    return;
                }

                // JaaS: load from 8x8.vc/{AppId}/external_api.js
                // Fallback: load from domain/external_api.js (self-hosted)
                const domain = config.JitsiDomain || '8x8.vc';
                const appId = config.AppId || '';
                const scriptUrl = appId
                    ? `https://${domain}/${appId}/external_api.js`
                    : `https://${domain}/external_api.js`;

                const script = document.createElement('script');
                script.src = scriptUrl;
                script.onload = () => {
                    console.log('[Telehealth] Jitsi External API loaded from:', scriptUrl);
                    resolve();
                };
                script.onerror = () => {
                    console.error('[Telehealth] Failed to load Jitsi External API from:', scriptUrl);
                    reject(new Error('Failed to load Jitsi video library'));
                };
                document.head.appendChild(script);
            });
        }

        /**
         * Initialize Jitsi Meet in the video container.
         */
        _startJitsi(config) {
            const container = document.getElementById('telehealthVideoContainer');
            if (!container) return;

            // Clear loading indicator
            container.innerHTML = '';

            // JaaS: room name must be prefixed with AppID
            const appId = config.AppId || '';
            const fullRoomName = appId ? `${appId}/${config.RoomName}` : config.RoomName;

            // Build Jitsi options
            const jitsiOptions = {
                roomName: fullRoomName,
                width: '100%',
                height: '100%',
                parentNode: container,
                userInfo: {
                    displayName: config.DisplayName || 'Provider'
                },
                configOverwrite: {
                    startWithAudioMuted: false,
                    startWithVideoMuted: false,
                    disableDeepLinking: true,
                    prejoinConfig: {
                        enabled: false
                    },
                    // 'select-background' exposes Jitsi's native background
                    // picker (None / Blur / Slight blur / pre-built scenes /
                    // Upload custom image). Processing is fully client-side
                    // via TensorFlow Lite + WebAssembly — no PHI leaves the
                    // user's browser. Auto-degrades on low-end devices.
                    toolbarButtons: ['microphone', 'camera', 'select-background', 'hangup', 'tileview'],
                    disableScreensharing: true,
                    enableClosePage: false,
                    disableInviteFunctions: true,
                    enableNoisyMicDetection: false,
                    notifications: [],
                    disableThirdPartyRequests: true,
                    enableWelcomePage: false,
                    enableLobby: false
                },
                interfaceConfigOverwrite: {
                    SHOW_JITSI_WATERMARK: false,
                    SHOW_WATERMARK_FOR_GUESTS: false,
                    SHOW_POWERED_BY: false,
                    SHOW_PROMOTIONAL_CLOSE_PAGE: false,
                    TOOLBAR_BUTTONS: ['microphone', 'camera', 'select-background', 'hangup', 'tileview'],
                    // DISABLE_VIDEO_BACKGROUND removed so the picker is usable.
                    // (Previously hard-disabled the whole feature.)
                    HIDE_INVITE_MORE_HEADER: true,
                    SHOW_CHROME_EXTENSION_BANNER: false,
                    MOBILE_APP_PROMO: false,
                    DISABLE_JOIN_LEAVE_NOTIFICATIONS: true,
                    TOOLBAR_ALWAYS_VISIBLE: true,
                    filmStripOnly: false
                }
            };

            // Add JWT if available (JaaS authenticated mode)
            if (config.Jwt) {
                jitsiOptions.jwt = config.Jwt;
            }

            try {
                this._jitsiApi = new JitsiMeetExternalAPI(config.JitsiDomain, jitsiOptions);

                // Handle video conference events
                this._jitsiApi.addEventListener('videoConferenceJoined', () => {
                    console.log('[Telehealth] Joined video conference');
                });

                this._jitsiApi.addEventListener('videoConferenceLeft', () => {
                    console.log('[Telehealth] Left video conference');
                });

                this._jitsiApi.addEventListener('readyToClose', () => {
                    console.log('[Telehealth] Call ended by user');
                    this._cleanupTelehealth();
                });

                this._jitsiApi.addEventListener('participantJoined', (participant) => {
                    console.log('[Telehealth] Participant joined:', participant.displayName);
                    // Hide waiting banner when someone joins the call
                    const banner = document.getElementById('telehealthWaitingBanner');
                    const badge = document.getElementById('telehealthWaitingBadge');
                    if (banner) banner.classList.add('d-none');
                    if (badge) badge.classList.add('d-none');
                    // Hide connecting overlay
                    this._hideConnectingOverlay();
                });

                console.log('[Telehealth] Jitsi initialized in room:', config.RoomName);

            } catch (err) {
                console.error('[Telehealth] Failed to start Jitsi:', err);
                container.innerHTML = `
                    <div class="text-center text-light p-4">
                        <i class="bi bi-exclamation-triangle fs-1"></i>
                        <p class="mt-2">Unable to start video call.</p>
                        <button class="btn btn-sm btn-outline-light" onclick="window._encounterWorkspace._initTelehealthPanel()">
                            <i class="bi bi-arrow-clockwise me-1"></i>Retry
                        </button>
                    </div>`;
            }
        }

        /**
         * End the telehealth call and clean up.
         */
        _endTelehealthCall() {
            if (this._jitsiApi) {
                try {
                    this._jitsiApi.executeCommand('hangup');
                } catch (e) {
                    console.warn('[Telehealth] Error hanging up:', e);
                }
            }
            this._cleanupTelehealth();
        }

        /**
         * Initialize the Telehealth AI Scribe — captures both doctor mic + patient tab audio.
         * Renders a persistent scribe bar above the step content area.
         */
        _initTelehealthScribe() {
            if (typeof TelehealthScribeUI === 'undefined' || typeof TelehealthScribeService === 'undefined') {
                console.warn('[Telehealth] TelehealthScribe modules not loaded, skipping scribe init');
                return;
            }

            if (!TelehealthScribeService.isSupported()) {
                console.warn('[Telehealth] Browser does not support getDisplayMedia — telehealth scribe unavailable');
                return;
            }

            // Create scribe container and insert between header and body in encounterContent.
            // This placement persists across step switches (encounterMain innerHTML is replaced per step).
            const encounterContent = document.getElementById('encounterContent');
            const encounterBody = document.getElementById('encounterBody');
            if (!encounterContent) return;

            const scribeContainer = document.createElement('div');
            scribeContainer.id = 'telehealthScribeContainer';
            scribeContainer.style.padding = '0 16px';

            // Insert before encounterBody so it sits between header and the step area
            if (encounterBody) {
                encounterContent.insertBefore(scribeContainer, encounterBody);
            } else {
                encounterContent.insertBefore(scribeContainer, encounterContent.firstChild);
            }

            // Instantiate the scribe UI
            this._telehealthScribeUI = new TelehealthScribeUI({
                patientId: this.patientId,
                encounterId: this.encounterId,
                onFieldsPopulated: (data) => {
                    // Trigger BMI recalc if vitals were populated and fields exist
                    if (data.vitals) {
                        const weightEl = document.getElementById('ewVitalWeight');
                        const heightEl = document.getElementById('ewVitalHeight');
                        if (weightEl && heightEl) {
                            weightEl.dispatchEvent(new Event('input', { bubbles: true }));
                        }
                        this._markStepComplete(0, true);
                    }
                    // Mark steps as having data
                    if (data.history) this._markStepComplete(1, true);
                    if (data.ccHpi) this._markStepComplete(2, true);
                },
                onTranscriptionComplete: (transcriptionArr) => {
                    // Called when user manually stops the scribe via Stop button
                    if (Array.isArray(transcriptionArr) && transcriptionArr.length > 0) {
                        this._telehealthTranscription = transcriptionArr.join('\n').trim();
                        console.log('[Telehealth] Manual stop — transcription saved:',
                            `${this._telehealthTranscription.length} chars, ${transcriptionArr.length} segments`);

                        // Trigger post-call extraction of all clinical sections
                        this._extractTelehealthSessionData();
                    }
                },
                onError: (error) => {
                    console.error('[Telehealth Scribe] Error:', error);
                },
                onProcessingChange: (count) => {
                    this._updateTelehealthGenerateButtonState(count);
                }
            });
            this._telehealthScribeUI.render(scribeContainer);
            this._telehealthScribeActive = true;

            console.log('[Telehealth] AI Scribe initialized — will auto-start on patient admit');
        }

        /**
         * Show a "Connecting to patient..." overlay on the video container.
         * Displayed after Admit while waiting for the patient's video to appear.
         */
        _showConnectingOverlay() {
            const container = document.getElementById('telehealthVideoContainer');
            if (!container) return;

            // Remove existing overlay if any
            container.querySelector('.telehealth-connecting-overlay')?.remove();

            const overlay = document.createElement('div');
            overlay.className = 'telehealth-connecting-overlay';
            overlay.id = 'telehealthConnectingOverlay';
            overlay.innerHTML = `
                <div class="spinner-border" role="status"></div>
                <p>Connecting to patient...</p>
                <p style="font-size:0.75rem;color:#78909c;">Video will appear shortly</p>
            `;
            container.appendChild(overlay);

            // Safety timeout — remove after 30s even if no event fires
            this._connectingOverlayTimeout = setTimeout(() => this._hideConnectingOverlay(), 30000);
        }

        _hideConnectingOverlay() {
            const overlay = document.getElementById('telehealthConnectingOverlay');
            if (overlay) {
                overlay.style.transition = 'opacity 0.5s ease';
                overlay.style.opacity = '0';
                setTimeout(() => overlay.remove(), 500);
            }
            if (this._connectingOverlayTimeout) {
                clearTimeout(this._connectingOverlayTimeout);
                this._connectingOverlayTimeout = null;
            }
        }

        /**
         * Auto-start the AI Scribe after patient is admitted.
         * Chrome will show its native mic/screen permission dialogs.
         * If denied, the Start button appears as fallback.
         */
        _autoStartTelehealthScribe() {
            if (!this._telehealthScribeUI) return;
            if (this._telehealthScribeUI._state !== 'idle') return; // Already running

            console.log('[Telehealth] Auto-starting AI Scribe after patient admitted');
            // Short delay to let Jitsi audio settle after admit
            setTimeout(() => {
                if (this._telehealthScribeUI && this._telehealthScribeUI._state === 'idle') {
                    this._telehealthScribeUI.startRecording();
                }
            }, 1000);
        }

        /**
         * Reflect transcription-finalization state on the "Generate Note from
         * Telehealth Session" button. Called whenever chunk processing count
         * changes. Prevents the user from clicking before all in-flight chunks
         * have returned (which would otherwise send a partial transcript).
         */
        _updateTelehealthGenerateButtonState(count) {
            const btn = document.getElementById('ewBtnGenerateTelehealthNote');
            if (!btn) return;

            // Reset any prior 45s override timer when state changes
            if (this._telehealthFinalizeOverrideTimer) {
                clearTimeout(this._telehealthFinalizeOverrideTimer);
                this._telehealthFinalizeOverrideTimer = null;
            }

            if (count > 0) {
                btn.disabled = true;
                btn.dataset.finalizing = '1';
                delete btn.dataset.overrideArmed;
                btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Wrapping up the conversation. One moment, please.';

                this._telehealthFinalizeOverrideTimer = setTimeout(() => {
                    if (btn.dataset.finalizing === '1') {
                        btn.disabled = false;
                        btn.dataset.overrideArmed = '1';
                        btn.innerHTML = '<i class="bi bi-broadcast me-1"></i>Generate Note Anyway';
                        btn.title = 'Some of the conversation may still be finalizing. You can wait a little more or generate now.';
                    }
                }, 45000);
            } else if (btn.dataset.finalizing === '1') {
                delete btn.dataset.finalizing;
                delete btn.dataset.overrideArmed;
                btn.disabled = false;
                btn.title = '';
                btn.innerHTML = '<i class="bi bi-broadcast me-1"></i>Generate Note from Telehealth Session';
            }
        }

        /**
         * Generate a clinical note from the accumulated telehealth transcription.
         * Shows template selection modal, then fires background generation.
         */
        async _generateNoteFromTelehealth() {
            // Guard: if chunks are still being transcribed on the server, do
            // not let the user generate yet — the transcript would be partial.
            // (The button is already disabled by _updateTelehealthGenerateButtonState
            // in this state; this is a belt-and-braces check in case the click
            // arrives via a stale handler.)
            const inFlight = this._telehealthScribeUI && typeof this._telehealthScribeUI.getProcessingCount === 'function'
                ? this._telehealthScribeUI.getProcessingCount() : 0;
            const btnEl = document.getElementById('ewBtnGenerateTelehealthNote');
            const overrideArmed = btnEl && btnEl.dataset && btnEl.dataset.overrideArmed === '1';
            if (inFlight > 0 && !overrideArmed) {
                if (typeof Toast !== 'undefined') Toast.info('Still finalizing the conversation. Please wait a moment.');
                return;
            }

            // Prefer the live array whenever the scribe is still active, so the
            // most recently arrived chunks are included (avoids reusing a stale
            // _telehealthTranscription snapshot taken before all chunks landed).
            let transcription = null;
            if (this._telehealthScribeUI && typeof this._telehealthScribeUI.getAccumulatedTranscription === 'function') {
                const liveArr = this._telehealthScribeUI.getAccumulatedTranscription();
                if (Array.isArray(liveArr) && liveArr.length > 0) {
                    transcription = liveArr.join('\n').trim();
                }
            }
            if (!transcription) transcription = this._telehealthTranscription || null;

            if (Array.isArray(transcription)) {
                transcription = transcription.join('\n').trim();
            }

            if (!transcription) {
                if (typeof Toast !== 'undefined') Toast.warning('No telehealth transcription available. Start the AI Scribe during your telehealth call first.');
                return;
            }

            // Fetch matching templates
            const btn = document.getElementById('ewBtnGenerateTelehealthNote');
            if (btn) {
                btn.disabled = true;
                btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Loading templates...';
            }

            let templates;
            try {
                templates = await window.apiRequest(`/recording/matching-templates?encounterId=${this.encounterId}`);
            } catch (err) {
                if (typeof Toast !== 'undefined') Toast.error('Failed to load note templates');
                if (btn) { btn.disabled = false; btn.innerHTML = '<i class="bi bi-broadcast me-1"></i>Generate Note from Telehealth Session'; }
                return;
            }

            if (btn) { btn.disabled = false; btn.innerHTML = '<i class="bi bi-broadcast me-1"></i>Generate Note from Telehealth Session'; }

            if (!templates || templates.length === 0) {
                if (typeof Toast !== 'undefined') Toast.warning('No clinical note templates available for this encounter.');
                return;
            }

            // Show template selection modal
            this._showTemplateSelectionModal(templates, transcription);
        }

        /**
         * Show modal for provider to select which note types to generate.
         */
        _showTemplateSelectionModal(templates, transcription) {
            // Remove existing modal if any
            document.getElementById('noteTemplateSelectModal')?.remove();

            const checkboxes = templates.map((t, i) => `
                <div class="form-check py-1">
                    <input class="form-check-input" type="checkbox" id="tmplChk_${t.TemplateId}" value="${t.TemplateId}" checked>
                    <label class="form-check-label" for="tmplChk_${t.TemplateId}">${this._escapeHtml(t.TemplateName)}</label>
                </div>
            `).join('');

            const modalHtml = `
                <div class="modal fade" id="noteTemplateSelectModal" tabindex="-1">
                    <div class="modal-dialog modal-dialog-centered">
                        <div class="modal-content">
                            <div class="modal-header">
                                <h5 class="modal-title"><i class="bi bi-file-earmark-medical me-2"></i>Select Note Types to Generate</h5>
                                <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
                            </div>
                            <div class="modal-body">
                                <p class="text-muted mb-3">Select the clinical note types you'd like to generate from the telehealth session:</p>
                                <div id="templateCheckboxes">${checkboxes}</div>
                            </div>
                            <div class="modal-footer d-flex justify-content-between">
                                <button type="button" class="btn btn-outline-secondary" data-bs-dismiss="modal">Cancel</button>
                                <button type="button" class="btn btn-success" id="btnStartNoteGeneration">
                                    <i class="bi bi-play-fill me-1"></i>Generate Selected Notes
                                </button>
                            </div>
                        </div>
                    </div>
                </div>`;

            document.body.insertAdjacentHTML('beforeend', modalHtml);
            const modalEl = document.getElementById('noteTemplateSelectModal');
            const modal = new bootstrap.Modal(modalEl);

            document.getElementById('btnStartNoteGeneration').addEventListener('click', () => {
                const selected = [...document.querySelectorAll('#templateCheckboxes input:checked')]
                    .map(cb => parseInt(cb.value));

                if (selected.length === 0) {
                    if (typeof Toast !== 'undefined') Toast.warning('Please select at least one note type.');
                    return;
                }

                this._startBackgroundNoteGeneration(transcription, selected, modal, modalEl);
            });

            // Cleanup on close
            modalEl.addEventListener('hidden.bs.modal', () => modalEl.remove());
            modal.show();
        }

        /**
         * Fire background note generation and show confirmation in modal.
         */
        async _startBackgroundNoteGeneration(transcription, selectedTemplateIds, modal, modalEl) {
            const generateBtn = document.getElementById('btnStartNoteGeneration');
            if (generateBtn) {
                generateBtn.disabled = true;
                generateBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Starting...';
            }

            const patientName = this.patient ? `${this.patient.FirstName} ${this.patient.LastName}` : 'Patient';

            try {
                const result = await window.apiRequest('/recording/start-note-generation', {
                    method: 'POST',
                    body: {
                        EncounterId: this.encounterId,
                        Transcription: transcription,
                        SelectedTemplateIds: selectedTemplateIds,
                        PatientName: patientName
                    }
                });

                if (result && result.Success && result.JobId) {
                    // Show confirmation message in modal
                    const modalBody = modalEl.querySelector('.modal-body');
                    const modalFooter = modalEl.querySelector('.modal-footer');

                    modalBody.innerHTML = `
                        <div class="text-center py-3">
                            <div class="mb-3">
                                <i class="bi bi-check-circle text-success" style="font-size: 3rem;"></i>
                            </div>
                            <h6 class="mb-2">Notes Are Being Generated</h6>
                            <p class="text-muted mb-0">
                                You can resume with your next patient.<br>
                                You'll be notified when the notes are ready.
                            </p>
                        </div>`;

                    modalFooter.innerHTML = `
                        <button type="button" class="btn btn-primary" data-bs-dismiss="modal">
                            <i class="bi bi-arrow-right me-1"></i>Continue
                        </button>`;

                    // Start tracking in the global tracker
                    if (window._noteGenerationTracker) {
                        window._noteGenerationTracker.trackJob(result.JobId, this.encounterId, patientName, selectedTemplateIds.length);
                    }

                    // Update button to show generation in progress
                    const btn = document.getElementById('ewBtnGenerateTelehealthNote');
                    if (btn) {
                        btn.disabled = true;
                        btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Notes Generating...';
                        // Re-enable when job completes (tracked via NoteGenerationTracker)
                        this._noteGenJobId = result.JobId;
                    }

                    // Auto-close modal after 4 seconds
                    setTimeout(() => { try { modal.hide(); } catch(e) {} }, 4000);
                } else {
                    throw new Error(result?.Message || 'Failed to start note generation');
                }
            } catch (error) {
                console.error('[Telehealth] Start generation error:', error);
                if (typeof Toast !== 'undefined') Toast.error(error.message || 'Failed to start note generation');
                if (generateBtn) {
                    generateBtn.disabled = false;
                    generateBtn.innerHTML = '<i class="bi bi-play-fill me-1"></i>Generate Selected Notes';
                }
            }
        }

        /**
         * Start video call from the "Start Video Call" button.
         * Hides the buttons, opens the telehealth panel.
         */
        async _startVideoCallFromButton() {
            // Hide both video call buttons
            this._telehealthPanelActive = true;
            document.getElementById('ewHeaderStartVideoCall')?.classList.add('d-none');
            document.getElementById('ewStartVideoCallCenter')?.classList.add('d-none');

            // Open the telehealth panel
            await this._initTelehealthPanel();
        }

        /**
         * Called when telehealth call ends (hangup). Shows buttons again.
         */
        _onTelehealthCallEnded() {
            this._telehealthPanelActive = false;
            this._telehealthCallHappened = true;

            // Re-render header and note step to show buttons
            this._renderHeader();
            if (this.currentStep === 3) { // Clinical Note step
                this._renderNoteStep();
            }
        }

        _escapeHtml(text) {
            const div = document.createElement('div');
            div.textContent = text || '';
            return div.innerHTML;
        }

        /**
         * Post-call extraction: sends the full accumulated transcription to the backend
         * for combined extraction of all clinical sections (vitals, history, CC/HPI).
         * Then applies the extracted data to all sections of the encounter workspace.
         */
        async _extractTelehealthSessionData() {
            if (!this._telehealthScribeUI) return;

            try {
                if (typeof Toast !== 'undefined') Toast.info('Processing telehealth session — extracting clinical data...');

                const extracted = await this._telehealthScribeUI.extractFullSession();

                if (extracted) {
                    // Mark steps as having data if sections were populated
                    if (extracted.vitals) this._markStepComplete(0, true);
                    if (extracted.history) this._markStepComplete(1, true);
                    if (extracted.ccHpi) this._markStepComplete(2, true);

                    if (typeof Toast !== 'undefined') Toast.success('Telehealth session data extracted and applied to all sections.');
                    console.log('[Telehealth] Post-call extraction complete. Sections populated:', Object.keys(extracted));
                } else {
                    if (typeof Toast !== 'undefined') Toast.warning('Could not extract clinical data from session. You can still generate a clinical note.');
                }
            } catch (err) {
                console.error('[Telehealth] Post-call extraction error:', err);
                if (typeof Toast !== 'undefined') Toast.error('Failed to process telehealth session data.');
            }
        }

        /**
         * Clean up telehealth resources — Jitsi + SignalR.
         */
        _cleanupTelehealth() {
            // Stop telehealth scribe, save transcription, and trigger post-call extraction
            if (this._telehealthScribeUI) {
                try {
                    // Check if scribe was already stopped manually (via Stop button) —
                    // in that case, onTranscriptionComplete already saved transcription & triggered extraction
                    const alreadyStopped = this._telehealthScribeUI._state === 'idle' && this._telehealthTranscription;

                    this._telehealthScribeUI.stopAndFinalize();

                    if (!alreadyStopped) {
                        // Get accumulated transcription array and join into a single string
                        const transcriptionArr = this._telehealthScribeUI.getAccumulatedTranscription();
                        if (Array.isArray(transcriptionArr) && transcriptionArr.length > 0) {
                            this._telehealthTranscription = transcriptionArr.join('\n').trim();
                            console.log('[Telehealth] Scribe stopped. Transcription saved:',
                                `${this._telehealthTranscription.length} chars, ${transcriptionArr.length} segments`);

                            // Trigger post-call extraction of all clinical sections
                            this._extractTelehealthSessionData();
                        } else if (!this._telehealthTranscription) {
                            console.log('[Telehealth] Scribe stopped. No transcription accumulated.');
                        }
                    } else {
                        console.log('[Telehealth] Scribe was already stopped manually. Transcription preserved.');
                    }
                } catch (e) {
                    console.warn('[Telehealth] Error stopping scribe:', e);
                }
            }

            // Dispose Jitsi
            if (this._jitsiApi) {
                try { this._jitsiApi.dispose(); } catch (e) {}
                this._jitsiApi = null;
            }

            // Disconnect SignalR
            if (this._telehealthConnection) {
                try { this._telehealthConnection.stop(); } catch (e) {}
                this._telehealthConnection = null;
            }

            // Update panel to show ended state
            const panel = document.getElementById('telehealthVideoPanel');
            if (panel) {
                const body = document.getElementById('telehealthPanelBody');
                if (body) {
                    body.innerHTML = `
                        <div class="text-center text-light p-4">
                            <i class="bi bi-camera-video-off fs-1 mb-2"></i>
                            <p class="mb-2">Video call ended</p>
                            <button class="btn btn-sm btn-outline-light" onclick="window._encounterWorkspace._rejoinTelehealthCall()">
                                <i class="bi bi-arrow-clockwise me-1"></i>Rejoin Call
                            </button>
                            <button class="btn btn-sm btn-outline-danger ms-2" onclick="document.getElementById('telehealthVideoPanel')?.remove()">
                                <i class="bi bi-x-lg me-1"></i>Close Panel
                            </button>
                        </div>`;
                }
            }
        }

        /**
         * Rejoin the telehealth call after ending.
         */
        async _rejoinTelehealthCall() {
            // Remove old panel and reinitialize
            document.getElementById('telehealthVideoPanel')?.remove();
            await this._initTelehealthPanel();
        }

        /**
         * Toggle minimize/restore the video panel.
         */
        _toggleTelehealthMinimize() {
            const panel = document.getElementById('telehealthVideoPanel');
            const body = document.getElementById('telehealthPanelBody');
            if (!panel || !body) return;

            this._telehealthMinimized = !this._telehealthMinimized;

            if (this._telehealthMinimized) {
                body.style.display = 'none';
                panel.classList.add('telehealth-panel-minimized');
            } else {
                body.style.display = '';
                panel.classList.remove('telehealth-panel-minimized');
            }
        }

        /**
         * Toggle expand/collapse the video panel to full size.
         */
        _toggleTelehealthExpand() {
            const panel = document.getElementById('telehealthVideoPanel');
            const icon = document.getElementById('telehealthExpandIcon');
            if (!panel) return;

            this._telehealthExpanded = !this._telehealthExpanded;

            if (this._telehealthExpanded) {
                panel.classList.add('telehealth-panel-expanded');
                if (icon) icon.className = 'bi bi-fullscreen-exit';
            } else {
                panel.classList.remove('telehealth-panel-expanded');
                if (icon) icon.className = 'bi bi-arrows-fullscreen';
            }

            // If expanded, also restore from minimized
            if (this._telehealthExpanded && this._telehealthMinimized) {
                this._telehealthMinimized = false;
                const body = document.getElementById('telehealthPanelBody');
                if (body) body.style.display = '';
                panel.classList.remove('telehealth-panel-minimized');
            }
        }

        /**
         * Make the video panel draggable by its header.
         */
        _makeDraggable(panel, handle) {
            if (!panel || !handle) return;

            let isDragging = false;
            let startX, startY, startLeft, startTop;

            handle.addEventListener('mousedown', (e) => {
                // Don't drag if clicking a button
                if (e.target.closest('button')) return;

                isDragging = true;
                startX = e.clientX;
                startY = e.clientY;

                const rect = panel.getBoundingClientRect();
                startLeft = rect.left;
                startTop = rect.top;

                panel.style.transition = 'none';
                handle.style.cursor = 'grabbing';
                e.preventDefault();
            });

            document.addEventListener('mousemove', (e) => {
                if (!isDragging) return;

                const dx = e.clientX - startX;
                const dy = e.clientY - startY;

                let newLeft = startLeft + dx;
                let newTop = startTop + dy;

                // Keep within viewport bounds
                const maxLeft = window.innerWidth - panel.offsetWidth;
                const maxTop = window.innerHeight - panel.offsetHeight;
                newLeft = Math.max(0, Math.min(newLeft, maxLeft));
                newTop = Math.max(0, Math.min(newTop, maxTop));

                panel.style.left = newLeft + 'px';
                panel.style.top = newTop + 'px';
                panel.style.right = 'auto';
                panel.style.bottom = 'auto';
            });

            document.addEventListener('mouseup', () => {
                if (isDragging) {
                    isDragging = false;
                    panel.style.transition = '';
                    handle.style.cursor = 'grab';
                }
            });
        }
    }

    // Auto-initialize on the encounter workspace page
    document.addEventListener('DOMContentLoaded', () => {
        const mod = new EncounterWorkspaceModule();
        window._encounterWorkspace = mod;
        // Delay init to wait for auth (same pattern as OrdersModule)
        setTimeout(() => mod.init(), 250);
    });

})();
