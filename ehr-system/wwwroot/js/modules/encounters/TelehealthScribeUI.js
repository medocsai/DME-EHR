/**
 * TelehealthScribeUI.js
 *
 * UI coordinator for Telehealth AI Scribe in Encounter Workspace.
 * Renders a persistent scribe bar (not per-tab), captures telehealth call audio,
 * sends chunks to backend for combined extraction, and populates ALL sections
 * (Vitals, History, CC/HPI) simultaneously.
 */
(function () {
    'use strict';

    // Legacy map constants removed — simplified schema uses primary field + notes only

    class TelehealthScribeUI {
        constructor(options = {}) {
            this.patientId = options.patientId;
            this.encounterId = options.encounterId;
            this.onFieldsPopulated = options.onFieldsPopulated || null;
            this.onTranscriptionComplete = options.onTranscriptionComplete || null;
            this.onProcessingChange = options.onProcessingChange || null;

            this._scribeService = null;
            this._containerEl = null;
            this._state = 'idle';
            this._transcriptContext = [];          // Last 5 segments for Gemini context
            this._accumulatedTranscription = [];   // ALL segments for note generation
            this._chunkCount = 0;
            this._processingCount = 0;
            this._timerInterval = null;
            this._destroyed = false;

            // Track user-edited fields so AI doesn't overwrite them
            this._userEditedFields = new Set();

            // Track already-added history items by normalized key (prevents duplicates across chunks)
            this._addedHistoryKeys = new Set();

            // Pending data buffers (for when target DOM elements aren't visible)
            this._pendingVitals = null;
            this._pendingHistory = [];
            this._pendingCcHpi = null;
        }

        render(containerEl) {
            this._containerEl = containerEl;

            // Pre-populate dedup keys from existing history data in workspace cache
            this._loadExistingHistoryKeys();

            if (!TelehealthScribeService.isSupported()) {
                containerEl.innerHTML = `
                    <div class="telehealth-scribe-bar telehealth-scribe-unsupported">
                        <div class="telehealth-scribe-brand">
                            <i class="bi bi-broadcast"></i>
                            <span>Telehealth AI Scribe</span>
                        </div>
                        <small class="text-muted">AI Scribe for telehealth requires Chrome or Edge browser.</small>
                    </div>`;
                return;
            }

            containerEl.innerHTML = this._renderBarHtml();
            this._bindEvents();
        }

        destroy() {
            this._destroyed = true;
            if (this._scribeService) {
                this._scribeService.stopRecording();
                this._scribeService.destroy();
                this._scribeService = null;
            }
            this._stopTimer();
            this._transcriptContext = [];
            this._containerEl = null;
        }

        stopAndFinalize() {
            if (this._scribeService && this._state === 'recording') {
                this._scribeService.stopRecording();
            }
            if (this._scribeService) {
                this._scribeService.destroy();
                this._scribeService = null;
            }
            this._stopTimer();
            this._updateUI('idle');

            if (this.onTranscriptionComplete) {
                this.onTranscriptionComplete(this._accumulatedTranscription);
            }
        }

        getAccumulatedTranscription() {
            return this._accumulatedTranscription;
        }

        /** Number of chunks still being uploaded/transcribed on the server. */
        getProcessingCount() {
            return this._processingCount;
        }

        /** Public method — allows EncounterWorkspace to trigger recording start (e.g. on Admit) */
        startRecording() {
            if (this._state !== 'idle') return;
            this._startRecording();
        }

        /** Flush pending vitals data into the DOM (called when vitals step renders) */
        flushPendingVitals() {
            if (this._pendingVitals) {
                this._populateVitalsFields(this._pendingVitals);
                this._pendingVitals = null;
            }
        }

        /** Flush pending history items into the DOM (called when history step renders) */
        async flushPendingHistory() {
            if (this._pendingHistory.length > 0) {
                const items = [...this._pendingHistory];
                this._pendingHistory = [];
                const workspace = window._encounterWorkspace;
                if (!workspace) return;
                for (const { sectionKey, item } of items) {
                    if (!this._isDuplicateHistoryItem(sectionKey, item)) {
                        await this._addHistoryItem(workspace, sectionKey, item);
                    }
                }
            }
        }

        /** Flush pending CC/HPI data into the DOM (called when CC/HPI step renders) */
        flushPendingCcHpi() {
            if (this._pendingCcHpi) {
                this._populateCcHpiFields(this._pendingCcHpi);
                this._pendingCcHpi = null;
            }
        }

        // =========================================
        // HTML Rendering
        // =========================================

        _renderBarHtml() {
            return `
                <div class="telehealth-scribe-bar" id="telehealthScribeBar">
                    <div class="telehealth-scribe-brand">
                        <i class="bi bi-broadcast"></i>
                        <span>Telehealth AI Scribe</span>
                    </div>

                    <div class="telehealth-scribe-controls">
                        <button class="btn btn-telehealth-start d-none" id="telehealthScribeStartBtn" title="Start AI Scribe">
                            <i class="bi bi-play-fill me-1"></i>Start AI Scribe
                        </button>
                        <button class="btn btn-medocs-pause d-none" id="telehealthScribePauseBtn" title="Pause AI Scribe">
                            <i class="bi bi-pause-fill me-1"></i>Pause
                        </button>
                        <button class="btn btn-medocs-resume d-none" id="telehealthScribeResumeBtn" title="Resume AI Scribe">
                            <i class="bi bi-play-fill me-1"></i>Resume
                        </button>
                        <button class="btn btn-medocs-stop d-none" id="telehealthScribeStopBtn" title="Stop AI Scribe">
                            <i class="bi bi-stop-fill me-1"></i>Stop
                        </button>
                    </div>

                    <div class="telehealth-scribe-status">
                        <span class="telehealth-scribe-sources d-none" id="telehealthScribeSources">
                            <span class="badge bg-success" style="font-size:0.65rem;padding:2px 6px"><i class="bi bi-mic-fill"></i> Doctor</span>
                            <span class="badge bg-info" style="font-size:0.65rem;padding:2px 6px" id="telehealthScribePatientBadge"><i class="bi bi-headphones"></i> Patient</span>
                        </span>
                        <span class="medocs-voice-timer d-none" id="telehealthScribeTimer">00:00</span>
                        <span class="medocs-voice-level d-none" id="telehealthScribeLevel">
                            <span class="medocs-level-bar"></span>
                            <span class="medocs-level-bar"></span>
                            <span class="medocs-level-bar"></span>
                            <span class="medocs-level-bar"></span>
                            <span class="medocs-level-bar"></span>
                        </span>
                        <span class="medocs-voice-processing d-none" id="telehealthScribeProcessing">
                            <span class="spinner-border spinner-border-sm" style="width:12px;height:12px;"></span>
                            <small>Processing...</small>
                        </span>
                        <span class="medocs-voice-chunks d-none" id="telehealthScribeChunks">0 segments</span>
                    </div>

                    <div class="telehealth-scribe-helper">
                        <small class="text-muted" id="telehealthScribeHelper">
                            <i class="bi bi-clock me-1"></i>Waiting — AI Scribe will start when patient is admitted
                        </small>
                    </div>

                    <div class="telehealth-scribe-error d-none" id="telehealthScribeError">
                        <small class="text-danger"></small>
                    </div>

                    <div class="telehealth-scribe-disclaimer">
                        <small>
                            <i class="bi bi-info-circle me-1"></i>AI-generated content may contain errors. Please review and verify all entries before saving. Manually edited values will not be overwritten.
                        </small>
                    </div>
                </div>`;
        }

        // =========================================
        // Event Binding
        // =========================================

        _bindEvents() {
            document.getElementById('telehealthScribeStartBtn')?.addEventListener('click', () => this._startRecording());
            document.getElementById('telehealthScribePauseBtn')?.addEventListener('click', () => this._pauseRecording());
            document.getElementById('telehealthScribeResumeBtn')?.addEventListener('click', () => this._resumeRecording());
            document.getElementById('telehealthScribeStopBtn')?.addEventListener('click', () => this._stopRecording());
        }

        _trackUserEdits() {
            const vitalFieldIds = [
                'ewVitalSysBp', 'ewVitalDiaBp', 'ewVitalHr', 'ewVitalTemp',
                'ewVitalSpO2', 'ewVitalRr', 'ewVitalWeight', 'ewVitalHeight', 'ewVitalNotes'
            ];
            for (const id of vitalFieldIds) {
                const el = document.getElementById(id);
                if (!el) continue;
                el.addEventListener('keydown', () => {
                    this._userEditedFields.add(id);
                    el.dataset.userEdited = 'true';
                });
            }
        }

        // =========================================
        // Recording Control
        // =========================================

        async _startRecording() {
            this._hideError();

            try {
                this._scribeService = new TelehealthScribeService({
                    silenceThreshold: 0.01,
                    silenceDuration: 1200,
                    minChunkDuration: 5000
                });

                this._scribeService.onChunkReady((blob, seq, dur) => this._handleChunkReady(blob, seq, dur));
                this._scribeService.onAudioLevel((rms) => this._updateAudioLevel(rms));
                this._scribeService.onError((err) => this._showError(TelehealthScribeService.getErrorMessage(err)));
                this._scribeService.onMicOnlyFallback(() => {
                    // Update UI to show mic-only mode
                    const patientBadge = document.getElementById('telehealthScribePatientBadge');
                    if (patientBadge) {
                        patientBadge.className = 'badge bg-secondary';
                        patientBadge.style.cssText = 'font-size:0.65rem;padding:2px 6px;text-decoration:line-through';
                    }
                    this._updateHelper('Mic-only mode — patient audio not captured. Using provider audio.');
                });

                await this._scribeService.startRecording();

                this._chunkCount = 0;
                this._transcriptContext = [];
                this._accumulatedTranscription = [];
                this._updateUI('recording');
                this._startTimer();
                this._trackUserEdits();
                this._updateHelper('Transcribing telehealth call — clinical data will be extracted after the call ends');

            } catch (err) {
                console.error('[TelehealthScribeUI] Start error:', err);
                this._showError(TelehealthScribeService.getErrorMessage(err));
                // Show Start button as fallback so doctor can retry manually
                this._hasRecordedBefore = true;
                const startBtn = document.getElementById('telehealthScribeStartBtn');
                startBtn?.classList.remove('d-none');
                this._updateHelper('Permissions denied or error occurred. Click Start AI Scribe to try again.');
            }
        }

        _pauseRecording() {
            if (this._scribeService) {
                this._scribeService.pauseRecording();
                this._updateUI('paused');
                this._stopTimer();
                this._updateHelper('AI Scribe paused — conversation not being recorded');
            }
        }

        _resumeRecording() {
            if (this._scribeService) {
                this._scribeService.resumeRecording();
                this._updateUI('recording');
                this._startTimer();
                this._updateHelper('Transcribing telehealth call — clinical data will be extracted after the call ends');
            }
        }

        _stopRecording() {
            if (this._scribeService) {
                this._scribeService.stopRecording();
                this._scribeService.destroy();
                this._scribeService = null;
            }
            this._stopTimer();
            this._updateUI('idle');
            this._updateHelper('AI Scribe stopped. Processing session transcription...');

            if (this.onTranscriptionComplete) {
                this.onTranscriptionComplete(this._accumulatedTranscription);
            }
        }

        // =========================================
        // Chunk Processing
        // =========================================

        async _handleChunkReady(blob, seq, dur) {
            if (this._destroyed) return;

            this._chunkCount++;
            this._updateChunkCount();
            this._processingCount++;
            this._showProcessing(true);
            this._emitProcessingChange();

            console.log(`[TelehealthPersist] Chunk ready: seq=${seq}, dur=${dur}s, size=${blob.size} bytes, encounterId=${this.encounterId}`);

            // 30s safety timeout per chunk — a hung fetch must never leave the
            // processing counter stuck above zero (which would freeze the
            // "Generate Note from Telehealth" button).
            const abortController = new AbortController();
            const timeoutId = setTimeout(() => abortController.abort(), 30000);

            try {
                const formData = new FormData();
                formData.append('audio', blob, `chunk_${seq}.webm`);
                formData.append('tabKey', 'telehealth');
                formData.append('sequenceNumber', seq.toString());
                formData.append('durationSeconds', dur.toString());
                formData.append('patientId', this.patientId.toString());
                formData.append('encounterId', this.encounterId.toString());

                const token = localStorage.getItem('authToken');
                const t0 = performance.now();
                const response = await fetch('/api/medocs-voice/process-chunk', {
                    method: 'POST',
                    headers: token ? { 'Authorization': `Bearer ${token}` } : {},
                    body: formData,
                    signal: abortController.signal
                });
                const elapsed = Math.round(performance.now() - t0);

                if (this._destroyed) return;

                console.log(`[TelehealthPersist] POST /process-chunk seq=${seq} → HTTP ${response.status} in ${elapsed}ms`);

                if (!response.ok) {
                    const errorText = await response.text().catch(() => '');
                    console.error(`[TelehealthPersist] Server error ${response.status} for seq=${seq}:`, errorText);
                    return;
                }

                const data = await response.json();

                if (data.Success && data.Transcription) {
                    // Telehealth: transcribe-only during the call — accumulate text
                    // Full extraction of all sections happens post-call
                    this._accumulatedTranscription.push(data.Transcription);
                    console.log(`[TelehealthPersist] Chunk ${seq} transcribed OK: ${data.Transcription.length} chars. In-memory segments: ${this._accumulatedTranscription.length}. (Server should have persisted this to TelehealthTranscriptionChunks for encounterId=${this.encounterId})`);
                } else if (!data.Success) {
                    console.warn('[TelehealthPersist] Chunk processing failed:', data.Message);
                } else {
                    console.warn(`[TelehealthPersist] Chunk ${seq} returned Success=true but empty Transcription — nothing persisted`);
                }
            } catch (err) {
                if (err && err.name === 'AbortError') {
                    console.warn(`[TelehealthPersist] Chunk seq=${seq} aborted after 30s timeout — dropping`);
                } else {
                    console.error('[TelehealthPersist] Chunk upload error:', err);
                }
            } finally {
                clearTimeout(timeoutId);
                this._processingCount--;
                if (this._processingCount <= 0) {
                    this._processingCount = 0;
                    this._showProcessing(false);
                }
                this._emitProcessingChange();
            }
        }

        _emitProcessingChange() {
            if (typeof this.onProcessingChange === 'function') {
                try { this.onProcessingChange(this._processingCount); }
                catch (e) { console.warn('[TelehealthScribeUI] onProcessingChange handler threw:', e); }
            }
        }

        /**
         * Post-call extraction: sends the FULL accumulated transcription to the backend
         * for combined extraction of all clinical sections (vitals, history, CC/HPI).
         * Called after the telehealth call ends.
         * Returns the extracted data object or null on failure.
         */
        async extractFullSession() {
            const transcription = this._accumulatedTranscription.join('\n').trim();
            if (!transcription) {
                console.warn('[TelehealthScribeUI] No transcription accumulated for post-call extraction');
                return null;
            }

            console.log(`[TelehealthScribeUI] Starting post-call extraction. Transcription length: ${transcription.length} chars`);
            this._showProcessing(true);
            this._updateHelper('Processing full telehealth session — extracting clinical data...');

            try {
                const token = localStorage.getItem('authToken');
                const response = await fetch('/api/medocs-voice/extract-telehealth-session', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        ...(token ? { 'Authorization': `Bearer ${token}` } : {})
                    },
                    body: JSON.stringify({ Transcription: transcription })
                });

                if (!response.ok) {
                    const errorText = await response.text().catch(() => '');
                    console.error(`[TelehealthScribeUI] Post-call extraction error ${response.status}:`, errorText);
                    this._updateHelper('Post-call extraction failed. You can still generate a clinical note.');
                    return null;
                }

                const data = await response.json();

                if (data.Success && data.ExtractedData) {
                    try {
                        const extracted = JSON.parse(data.ExtractedData);
                        console.log('[TelehealthScribeUI] Post-call extraction succeeded:', Object.keys(extracted));

                        // Apply all extracted sections to the UI
                        this._applyAllSections(extracted);

                        this._updateHelper('Telehealth session processed. Clinical data extracted and applied to all sections.');
                        return extracted;
                    } catch (parseErr) {
                        console.error('[TelehealthScribeUI] Failed to parse post-call extracted data:', parseErr);
                        this._updateHelper('Post-call extraction completed but data parsing failed.');
                        return null;
                    }
                } else {
                    console.warn('[TelehealthScribeUI] Post-call extraction returned no data:', data.Message);
                    this._updateHelper('Post-call extraction completed but no clinical data found.');
                    return null;
                }
            } catch (err) {
                console.error('[TelehealthScribeUI] Post-call extraction error:', err);
                this._updateHelper('Post-call extraction failed. You can still generate a clinical note.');
                return null;
            } finally {
                this._showProcessing(false);
            }
        }

        // =========================================
        // Field Population (All Sections)
        // =========================================

        _applyAllSections(data) {
            if (data.vitals) {
                this._populateVitalsFields(data.vitals);
            }
            if (data.history) {
                this._populateHistoryFields(data.history);
            }
            if (data.ccHpi) {
                this._populateCcHpiFields(data.ccHpi);
            }
            if (Array.isArray(data.orders) && data.orders.length > 0) {
                this._createOrders(data.orders);
            }
            if (Array.isArray(data.prescriptions) && data.prescriptions.length > 0) {
                this._createPrescriptions(data.prescriptions);
            }
            if (this.onFieldsPopulated) {
                this.onFieldsPopulated(data);
            }
        }

        /** Pre-load dedup keys from workspace cache so scribe doesn't re-add existing history items */
        _loadExistingHistoryKeys() {
            const ws = window._encounterWorkspace;
            if (!ws) return;
            const n = (val) => (val || '').toString().trim().toLowerCase();

            if (Array.isArray(ws._allergies)) {
                for (const a of ws._allergies) this._addedHistoryKeys.add(`allergy:${n(a.AllergenName || a.allergenName)}`);
            }
            if (Array.isArray(ws._medications)) {
                for (const m of ws._medications) this._addedHistoryKeys.add(`med:${n(m.DrugName || m.drugName)}`);
            }
            if (Array.isArray(ws._problems)) {
                for (const p of ws._problems) this._addedHistoryKeys.add(`prob:${n(p.Description || p.description)}`);
            }
            if (Array.isArray(ws._familyHistory)) {
                for (const f of ws._familyHistory) this._addedHistoryKeys.add(`famhx:${n(f.Condition || f.condition)}`);
            }
            if (Array.isArray(ws._socialHistory)) {
                for (const s of ws._socialHistory) this._addedHistoryKeys.add(`sochx:${n(s.Category || s.category)}`);
            }
            if (Array.isArray(ws._immunizations)) {
                for (const i of ws._immunizations) this._addedHistoryKeys.add(`imm:${n(i.VaccineName || i.vaccineName)}`);
            }
            console.log(`[TelehealthScribe] Pre-loaded ${this._addedHistoryKeys.size} existing history keys for dedup`);
        }

        _populateVitalsFields(data) {
            const fieldMap = {
                systolicBp: 'ewVitalSysBp',
                diastolicBp: 'ewVitalDiaBp',
                heartRate: 'ewVitalHr',
                temperature: 'ewVitalTemp',
                spO2: 'ewVitalSpO2',
                respiratoryRate: 'ewVitalRr',
                weight: 'ewVitalWeight',
                height: 'ewVitalHeight',
                notes: 'ewVitalNotes'
            };

            let anyFieldFound = false;
            for (const [key, elementId] of Object.entries(fieldMap)) {
                if (data[key] == null) continue;
                if (this._userEditedFields.has(elementId)) continue;

                const el = document.getElementById(elementId);
                if (el) {
                    el.value = data[key];
                    el.dispatchEvent(new Event('input', { bubbles: true }));
                    this._flashField(el);
                    anyFieldFound = true;
                }
            }

            // If no fields were in DOM, store as pending
            if (!anyFieldFound && !document.getElementById('ewVitalSysBp')) {
                this._pendingVitals = data;
            }
        }

        async _populateHistoryFields(data) {
            const workspace = window._encounterWorkspace;
            if (!workspace) return;

            const sectionMap = {
                allergies: 'allergies',
                medications: 'medications',
                problems: 'problems',
                familyHx: 'familyHx',
                socialHx: 'socialHx',
                immunizations: 'immunizations'
            };

            for (const [aiKey, sectionKey] of Object.entries(sectionMap)) {
                const items = data[aiKey];
                if (!Array.isArray(items) || items.length === 0) continue;

                for (const item of items) {
                    // Check if history grid is in the DOM
                    const tbody = document.getElementById(`ewGridBody_${sectionKey}`);
                    if (!tbody) {
                        // Buffer for later
                        if (!this._isDuplicateHistoryItem(sectionKey, item)) {
                            this._pendingHistory.push({ sectionKey, item });
                        }
                        continue;
                    }

                    if (this._isDuplicateHistoryItem(sectionKey, item)) {
                        continue;
                    }
                    await this._addHistoryItem(workspace, sectionKey, item);
                }
            }
        }

        async _addHistoryItem(workspace, sectionKey, item) {
            workspace._openAddRow(sectionKey);
            await new Promise(r => setTimeout(r, 150));

            const newRow = document.querySelector(`tr[data-section="${sectionKey}"][data-new="true"]`);
            if (!newRow) return;

            const fieldValues = this._mapHistoryItemToFields(sectionKey, item);
            for (const [field, value] of Object.entries(fieldValues)) {
                if (value == null) continue;
                const el = newRow.querySelector(`[data-field="${field}"]`);
                if (!el) continue;

                if (el.type === 'checkbox') {
                    el.checked = !!value;
                } else if (el.tagName === 'SELECT') {
                    const opt = Array.from(el.options).find(o =>
                        o.value == value || o.text.toLowerCase() === String(value).toLowerCase()
                    );
                    if (opt) el.value = opt.value;
                    else el.value = value;
                } else {
                    el.value = value;
                }
                this._flashField(el);
            }

            await workspace._saveNewRow(sectionKey, newRow);
            this._markHistoryItemAdded(sectionKey, item);
            await new Promise(r => setTimeout(r, 300));
        }

        _mapHistoryItemToFields(sectionKey, item) {
            // Simplified schema: primary field + notes for all sections
            switch (sectionKey) {
                case 'allergies':
                    return { allergenName: item.allergenName, notes: item.notes || '' };
                case 'medications':
                    return { drugName: item.drugName, notes: item.notes || '' };
                case 'problems':
                    return { description: item.description, notes: item.notes || '' };
                case 'familyHx':
                    return { condition: item.condition, notes: item.notes || '' };
                case 'socialHx':
                    return { category: item.category || '', notes: item.notes || '' };
                case 'immunizations':
                    return { vaccineName: item.vaccineName, notes: item.notes || '' };
                default:
                    return {};
            }
        }

        /**
         * Client-side dedup using a tracking Set — works regardless of DOM state.
         * Generates a normalized key per item and checks/adds to _addedHistoryKeys.
         */
        _isDuplicateHistoryItem(sectionKey, item) {
            const n = (val) => (val || '').toString().trim().toLowerCase();
            let key = '';

            switch (sectionKey) {
                case 'allergies':
                    key = `allergy:${n(item.allergenName)}`;
                    break;
                case 'medications':
                    key = `med:${n(item.drugName)}`;
                    break;
                case 'problems':
                    key = `prob:${n(item.description)}`;
                    break;
                case 'familyHx':
                    key = `famhx:${n(item.condition)}`;
                    break;
                case 'socialHx':
                    key = `sochx:${n(item.category)}`;
                    break;
                case 'immunizations':
                    key = `imm:${n(item.vaccineName)}`;
                    break;
            }

            if (!key) return false;

            if (this._addedHistoryKeys.has(key)) return true;

            // Also check existing DOM rows (pre-existing data loaded before scribe started)
            const tbody = document.getElementById(`ewGridBody_${sectionKey}`);
            if (tbody) {
                const existingTexts = Array.from(tbody.querySelectorAll('tr[data-id] td:first-child'))
                    .map(td => n(td.textContent));
                const primaryValue = sectionKey === 'allergies' ? n(item.allergenName)
                    : sectionKey === 'medications' ? n(item.drugName)
                    : sectionKey === 'problems' ? n(item.description)
                    : sectionKey === 'familyHx' ? n(item.condition)
                    : sectionKey === 'socialHx' ? n(item.category)
                    : sectionKey === 'immunizations' ? n(item.vaccineName) : '';

                if (primaryValue && existingTexts.some(t => t.includes(primaryValue) || primaryValue.includes(t))) {
                    this._addedHistoryKeys.add(key);
                    return true;
                }
            }

            return false;
        }

        /** Mark item as added after successful save */
        _markHistoryItemAdded(sectionKey, item) {
            const n = (val) => (val || '').toString().trim().toLowerCase();
            let key = '';
            switch (sectionKey) {
                case 'allergies': key = `allergy:${n(item.allergenName)}`; break;
                case 'medications': key = `med:${n(item.drugName)}`; break;
                case 'problems': key = `prob:${n(item.description)}`; break;
                case 'familyHx': key = `famhx:${n(item.condition)}`; break;
                case 'socialHx': key = `sochx:${n(item.category)}`; break;
                case 'immunizations': key = `imm:${n(item.vaccineName)}`; break;
            }
            if (key) this._addedHistoryKeys.add(key);
        }

        async _createOrders(orders) {
            const workspace = window._encounterWorkspace;
            if (!workspace) return;
            const patientId = workspace.patientId;
            const providerId = workspace.providerId || App.state.get('providerId');
            if (!patientId || !providerId) {
                console.warn('[TelehealthScribe] Cannot create orders — missing patientId or providerId');
                return;
            }

            for (const order of orders) {
                try {
                    const dto = {
                        PatientId: patientId,
                        ProviderId: providerId,
                        OrderType: order.orderType ?? 0,
                        Status: 0,
                        Priority: order.priority ?? 0,
                        DiagnosisCode: order.diagnosisCode || null,
                        ClinicalIndication: order.clinicalIndication || '',
                        Notes: order.notes || null,
                        LabPanelName: order.labPanelName || null,
                        FastingRequired: order.fastingRequired || false,
                        SpecimenType: order.specimenType || null,
                        Modality: order.modality || null,
                        BodyPart: order.bodyPart || null,
                        ContrastRequired: order.contrastRequired || false,
                        ReferralSpecialty: order.referralSpecialty || null,
                        ReferralReason: order.referralReason || null,
                        ReferralUrgency: order.referralUrgency || null
                    };
                    await window.apiRequest('/orders', { method: 'POST', body: dto, showLoader: false });
                    console.log(`[TelehealthScribe] Created order: ${order.labPanelName || order.modality || order.referralSpecialty || 'order'}`);
                } catch (err) {
                    console.error('[TelehealthScribe] Failed to create order:', err);
                }
            }

            // Refresh orders list in workspace
            if (typeof workspace._loadOrders === 'function') {
                await workspace._loadOrders();
            }
        }

        async _createPrescriptions(prescriptions) {
            const workspace = window._encounterWorkspace;
            if (!workspace) return;
            const patientId = workspace.patientId;
            const providerId = workspace.providerId || App.state.get('providerId');
            if (!patientId || !providerId) {
                console.warn('[TelehealthScribe] Cannot create prescriptions — missing patientId or providerId');
                return;
            }

            for (const rx of prescriptions) {
                try {
                    const dto = {
                        PatientId: patientId,
                        ProviderId: providerId,
                        DrugName: rx.drugName || '',
                        Strength: rx.strength || null,
                        DosageForm: rx.dosageForm || null,
                        Quantity: rx.quantity || null,
                        DaysSupply: rx.daysSupply || null,
                        DoseAmount: rx.doseAmount || null,
                        DoseUnit: rx.doseUnit || null,
                        Route: rx.route || 'Oral',
                        Frequency: rx.frequency || null,
                        DirectionsFreeText: rx.directionsFreeText || null,
                        Refills: rx.refills ?? 0,
                        DiagnosisCode: rx.diagnosisCode || null,
                        Notes: rx.notes || null,
                        Status: 0
                    };
                    await window.apiRequest('/prescriptions', { method: 'POST', body: dto, showLoader: false });
                    console.log(`[TelehealthScribe] Created prescription: ${rx.drugName}`);
                } catch (err) {
                    console.error('[TelehealthScribe] Failed to create prescription:', err);
                }
            }

            // Refresh prescriptions list in workspace
            if (typeof workspace._loadPrescriptions === 'function') {
                await workspace._loadPrescriptions();
            }
        }

        _populateCcHpiFields(data) {
            const ccEl = document.getElementById('ewCcInput');
            const hpiEl = document.getElementById('ewHpiInput');

            let anyFieldFound = false;

            if (data.chiefComplaint && ccEl) {
                ccEl.value = data.chiefComplaint;
                ccEl.dispatchEvent(new Event('input', { bubbles: true }));
                this._flashField(ccEl);
                anyFieldFound = true;
            }

            if (data.hpiNarrative && hpiEl) {
                hpiEl.value = data.hpiNarrative;
                hpiEl.dispatchEvent(new Event('input', { bubbles: true }));
                this._flashField(hpiEl);
                anyFieldFound = true;
            }

            // If no fields were in DOM, store as pending
            if (!anyFieldFound && !ccEl) {
                this._pendingCcHpi = data;
            }
        }

        // =========================================
        // UI Updates
        // =========================================

        _updateUI(state) {
            const prevState = this._state;
            this._state = state;

            const startBtn = document.getElementById('telehealthScribeStartBtn');
            const pauseBtn = document.getElementById('telehealthScribePauseBtn');
            const resumeBtn = document.getElementById('telehealthScribeResumeBtn');
            const stopBtn = document.getElementById('telehealthScribeStopBtn');
            const timer = document.getElementById('telehealthScribeTimer');
            const level = document.getElementById('telehealthScribeLevel');
            const chunks = document.getElementById('telehealthScribeChunks');
            const sources = document.getElementById('telehealthScribeSources');
            const bar = document.getElementById('telehealthScribeBar');

            // Reset all
            startBtn?.classList.add('d-none');
            pauseBtn?.classList.add('d-none');
            resumeBtn?.classList.add('d-none');
            stopBtn?.classList.add('d-none');
            timer?.classList.add('d-none');
            level?.classList.add('d-none');
            chunks?.classList.add('d-none');
            sources?.classList.add('d-none');

            bar?.classList.remove('scribe-recording', 'scribe-paused');

            switch (state) {
                case 'idle':
                    // Only show Start button as fallback if doctor previously stopped/had error
                    // On initial load, Start stays hidden (scribe auto-starts on Admit)
                    if (prevState === 'recording' || prevState === 'paused' || this._hasRecordedBefore) {
                        startBtn?.classList.remove('d-none');
                        this._updateHelper('AI Scribe stopped. Click Start to resume.');
                    }
                    break;
                case 'recording':
                    this._hasRecordedBefore = true;
                    pauseBtn?.classList.remove('d-none');
                    stopBtn?.classList.remove('d-none');
                    timer?.classList.remove('d-none');
                    level?.classList.remove('d-none');
                    chunks?.classList.remove('d-none');
                    sources?.classList.remove('d-none');
                    bar?.classList.add('scribe-recording');
                    break;
                case 'paused':
                    resumeBtn?.classList.remove('d-none');
                    stopBtn?.classList.remove('d-none');
                    timer?.classList.remove('d-none');
                    chunks?.classList.remove('d-none');
                    sources?.classList.remove('d-none');
                    bar?.classList.add('scribe-paused');
                    break;
            }
        }

        _updateHelper(text) {
            const el = document.getElementById('telehealthScribeHelper');
            if (el) el.innerHTML = `<i class="bi bi-lightbulb me-1"></i>${text}`;
        }

        _updateChunkCount() {
            const el = document.getElementById('telehealthScribeChunks');
            if (el) el.textContent = `${this._chunkCount} segment${this._chunkCount !== 1 ? 's' : ''}`;
        }

        _showProcessing(show) {
            const el = document.getElementById('telehealthScribeProcessing');
            if (el) el.classList.toggle('d-none', !show);
        }

        _showError(message) {
            const container = document.getElementById('telehealthScribeError');
            if (container) {
                container.classList.remove('d-none');
                container.querySelector('small').textContent = message;
            }
        }

        _hideError() {
            const container = document.getElementById('telehealthScribeError');
            if (container) container.classList.add('d-none');
        }

        _updateAudioLevel(rms) {
            const container = document.getElementById('telehealthScribeLevel');
            if (!container) return;

            const bars = container.querySelectorAll('.medocs-level-bar');
            const level = Math.min(5, Math.floor(rms * 50));
            bars.forEach((bar, i) => {
                bar.classList.toggle('active', i < level);
            });
        }

        _startTimer() {
            this._stopTimer();
            this._timerInterval = setInterval(() => {
                if (!this._scribeService) return;
                const secs = this._scribeService.getElapsedSeconds();
                const mm = String(Math.floor(secs / 60)).padStart(2, '0');
                const ss = String(secs % 60).padStart(2, '0');
                const el = document.getElementById('telehealthScribeTimer');
                if (el) el.textContent = `${mm}:${ss}`;
            }, 1000);
        }

        _stopTimer() {
            if (this._timerInterval) {
                clearInterval(this._timerInterval);
                this._timerInterval = null;
            }
        }

        _flashField(el) {
            if (!el) return;
            el.classList.add('medocs-field-updated');
            setTimeout(() => el.classList.remove('medocs-field-updated'), 1500);
        }
    }

    window.TelehealthScribeUI = TelehealthScribeUI;

})();
