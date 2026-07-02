/**
 * UnifiedVoiceBar.js
 *
 * Persistent voice entry bar in the encounter header.
 * Nurse starts once, talks naturally — AI fills fields across ALL sections
 * (Vitals, History, CC/HPI).
 *
 * HYBRID APPROACH:
 * - Per chunk (every 10s): transcribe only (fast) → show running transcription
 * - Every 30s: send FULL accumulated transcription → extract all sections with full context
 * - On Finalize: one final extraction with complete transcription → corrections applied
 *
 * Uses MedocsVoiceService for audio capture (10s max chunk, silence detection, auto-pause).
 */
(function () {
    'use strict';

    // Legacy map constants removed — simplified schema uses primary field + notes only

    const EXTRACTION_DEBOUNCE_MS = 2000; // Wait 2s after last transcription before extracting

    class UnifiedVoiceBar {
        constructor(options = {}) {
            this.patientId = options.patientId;
            this.encounterId = options.encounterId;
            this.onFieldsPopulated = options.onFieldsPopulated || null;

            this._voiceService = null;
            this._containerEl = null;
            this._state = 'idle'; // idle | recording | paused | finalizing | done
            this._destroyed = false;

            // Transcription accumulation
            this._accumulatedTranscription = [];  // All transcription segments
            this._chunkCount = 0;
            this._processingCount = 0;

            // Periodic extraction
            this._extractionTimer = null;
            this._lastExtractionTime = 0;
            this._isExtracting = false;

            // Timer
            this._timerInterval = null;

            // User-edited fields protection
            this._userEditedFields = new Set();

            // History dedup keys
            this._addedHistoryKeys = new Set();

            // Pending data buffers (for sections not in DOM)
            this._pendingVitals = null;
            this._pendingHistory = [];
            this._pendingCcHpi = null;

            // Silence tracking for "waiting for audio" state
            this._silenceStartTime = null;
            this._waitingForAudioShown = false;
        }

        // =========================================
        // Render
        // =========================================

        render(containerEl) {
            this._containerEl = containerEl;
            containerEl.innerHTML = this._renderBarHtml();
            this._bindEvents();
            this._loadExistingHistoryKeys();
        }

        _renderBarHtml() {
            return `
                <div class="unified-voice-bar" id="unifiedVoiceBar">
                    <div class="unified-voice-inner">
                        <div class="unified-voice-brand">
                            <i class="bi bi-mic-fill"></i>
                            <span>MEDOCS AI</span>
                        </div>

                        <div class="unified-voice-controls">
                            <!-- Idle state: glowing start button -->
                            <button class="btn btn-unified-start" id="unifiedStartBtn" title="Start AI Voice Entry">
                                <i class="bi bi-mic-fill me-1"></i>Start AI Voice Entry
                            </button>

                            <!-- Recording state -->
                            <button class="btn btn-unified-pause d-none" id="unifiedPauseBtn" title="Pause">
                                <i class="bi bi-pause-fill me-1"></i>Pause
                            </button>
                            <button class="btn btn-unified-resume d-none" id="unifiedResumeBtn" title="Resume">
                                <i class="bi bi-play-fill me-1"></i>Resume
                            </button>
                            <button class="btn btn-unified-finalize d-none" id="unifiedFinalizeBtn" title="Finalize — AI will do a final review of all entries">
                                <i class="bi bi-check2-circle me-1"></i>Finalize
                            </button>
                        </div>

                        <div class="unified-voice-status">
                            <span class="unified-voice-timer d-none" id="unifiedTimer">00:00</span>
                            <span class="unified-voice-level d-none" id="unifiedLevel">
                                <span class="unified-level-bar"></span>
                                <span class="unified-level-bar"></span>
                                <span class="unified-level-bar"></span>
                                <span class="unified-level-bar"></span>
                                <span class="unified-level-bar"></span>
                            </span>
                            <span class="unified-voice-processing d-none" id="unifiedProcessing">
                                <span class="spinner-border spinner-border-sm" style="width:12px;height:12px;"></span>
                                <small id="unifiedProcessingText">Processing...</small>
                            </span>
                        </div>

                        <div class="unified-voice-helper">
                            <small id="unifiedHelper">
                                <i class="bi bi-lightbulb me-1"></i>Fills Vitals, History Review &amp; CC/HPI from your conversation
                            </small>
                        </div>
                    </div>

                    <!-- Running transcription display -->
                    <div class="unified-transcription-area d-none" id="unifiedTranscriptionArea">
                        <div class="unified-transcription-scroll" id="unifiedTranscriptionScroll"></div>
                    </div>

                    <div class="unified-voice-disclaimer">
                        <small>
                            <i class="bi bi-info-circle me-1"></i>AI-generated content may contain errors. Please review and verify all entries before finalizing. Manually edited values will not be overwritten by AI.
                        </small>
                    </div>
                </div>`;
        }

        // =========================================
        // Event Binding
        // =========================================

        _bindEvents() {
            document.getElementById('unifiedStartBtn')?.addEventListener('click', () => this._startRecording());
            document.getElementById('unifiedPauseBtn')?.addEventListener('click', () => this._pauseRecording());
            document.getElementById('unifiedResumeBtn')?.addEventListener('click', () => this._resumeRecording());
            document.getElementById('unifiedFinalizeBtn')?.addEventListener('click', () => this._finalize());
        }

        // =========================================
        // Recording Control
        // =========================================

        async _startRecording() {
            if (typeof MedocsVoiceService === 'undefined' || !MedocsVoiceService.isSupported()) {
                this._updateHelper('Voice entry is not supported in this browser.');
                return;
            }

            try {
                this._voiceService = new MedocsVoiceService({
                    silenceThreshold: 0.01,
                    silenceDuration: 1200,
                    minChunkDuration: 3000,
                    maxChunkDuration: 10000
                });

                this._voiceService.onChunkReady((blob, seq, dur) => this._handleChunkReady(blob, seq, dur));
                this._voiceService.onAudioLevel((rms) => this._updateAudioLevel(rms));
                this._voiceService.onError((err) => this._updateHelper(MedocsVoiceService.getErrorMessage(err)));
                this._voiceService.onProlongedSilence(() => this._handleProlongedSilence());

                await this._voiceService.startRecording();

                this._chunkCount = 0;
                this._accumulatedTranscription = [];
                this._silenceStartTime = null;
                this._waitingForAudioShown = false;
                this._lastExtractionTime = Date.now();

                this._updateUI('recording');
                this._startTimer();
                this._startPeriodicExtraction();
                this._updateHelper('Listening...');

                // Show transcription area
                document.getElementById('unifiedTranscriptionArea')?.classList.remove('d-none');
                const scroll = document.getElementById('unifiedTranscriptionScroll');
                if (scroll) scroll.innerHTML = '<span class="text-muted">Transcription will appear here as you speak...</span>';

            } catch (err) {
                this._updateHelper(MedocsVoiceService.getErrorMessage(err));
            }
        }

        _pauseRecording() {
            if (this._voiceService) {
                this._voiceService.pauseRecording();
                this._updateUI('paused');
                this._stopTimer();
                this._updateHelper('Recording paused — click Resume to continue');
            }
        }

        _resumeRecording() {
            if (this._voiceService) {
                this._voiceService.resumeRecording();
                this._updateUI('recording');
                this._startTimer();
                this._silenceStartTime = null;
                this._waitingForAudioShown = false;
                this._updateHelper('Listening...');
            }
        }

        async _finalize() {
            this._updateUI('finalizing');
            this._updateHelper('Finalizing — AI is doing a final review of all entries...');

            // Stop recording
            if (this._voiceService) {
                this._voiceService.stopRecording();
                this._voiceService.destroy();
                this._voiceService = null;
            }
            this._stopTimer();
            this._stopPeriodicExtraction();

            // Final extraction with FULL accumulated transcription
            const fullText = this._accumulatedTranscription.join('\n').trim();
            if (fullText) {
                await this._runExtraction(fullText, true);
            }

            this._updateUI('done');
            this._updateHelper('All entries updated — please review and verify before finalizing');
        }

        _handleProlongedSilence() {
            if (this._voiceService) {
                this._voiceService.pauseRecording();
                this._updateUI('paused');
                this._stopTimer();
                this._updateHelper('Recording paused — no audio detected for a while. Click Resume to continue.');
            }
        }

        // =========================================
        // UI State Management
        // =========================================

        _updateUI(state) {
            this._state = state;
            const bar = document.getElementById('unifiedVoiceBar');
            const startBtn = document.getElementById('unifiedStartBtn');
            const pauseBtn = document.getElementById('unifiedPauseBtn');
            const resumeBtn = document.getElementById('unifiedResumeBtn');
            const finalizeBtn = document.getElementById('unifiedFinalizeBtn');
            const timer = document.getElementById('unifiedTimer');
            const level = document.getElementById('unifiedLevel');

            if (!bar) return;

            bar.classList.remove('unified-recording', 'unified-paused', 'unified-finalizing', 'unified-done');

            switch (state) {
                case 'idle':
                    startBtn?.classList.remove('d-none');
                    pauseBtn?.classList.add('d-none');
                    resumeBtn?.classList.add('d-none');
                    finalizeBtn?.classList.add('d-none');
                    timer?.classList.add('d-none');
                    level?.classList.add('d-none');
                    document.getElementById('unifiedTranscriptionArea')?.classList.add('d-none');
                    break;
                case 'recording':
                    bar.classList.add('unified-recording');
                    startBtn?.classList.add('d-none');
                    pauseBtn?.classList.remove('d-none');
                    resumeBtn?.classList.add('d-none');
                    finalizeBtn?.classList.remove('d-none');
                    timer?.classList.remove('d-none');
                    level?.classList.remove('d-none');
                    break;
                case 'paused':
                    bar.classList.add('unified-paused');
                    startBtn?.classList.add('d-none');
                    pauseBtn?.classList.add('d-none');
                    resumeBtn?.classList.remove('d-none');
                    finalizeBtn?.classList.remove('d-none');
                    timer?.classList.remove('d-none');
                    level?.classList.add('d-none');
                    break;
                case 'finalizing':
                    bar.classList.add('unified-finalizing');
                    startBtn?.classList.add('d-none');
                    pauseBtn?.classList.add('d-none');
                    resumeBtn?.classList.add('d-none');
                    finalizeBtn?.classList.add('d-none');
                    timer?.classList.remove('d-none');
                    level?.classList.add('d-none');
                    this._showProcessing(true, 'Finalizing...');
                    break;
                case 'done':
                    bar.classList.add('unified-done');
                    startBtn?.classList.remove('d-none');
                    startBtn.innerHTML = '<i class="bi bi-mic-fill me-1"></i>Start AI Voice Entry';
                    pauseBtn?.classList.add('d-none');
                    resumeBtn?.classList.add('d-none');
                    finalizeBtn?.classList.add('d-none');
                    timer?.classList.add('d-none');
                    level?.classList.add('d-none');
                    this._showProcessing(false);
                    break;
            }
        }

        _updateHelper(text) {
            const el = document.getElementById('unifiedHelper');
            if (el) el.innerHTML = `<i class="bi bi-lightbulb me-1"></i>${text}`;
        }

        _updateAudioLevel(rms) {
            const bars = document.querySelectorAll('#unifiedLevel .unified-level-bar');
            const thresholds = [0.01, 0.03, 0.06, 0.1, 0.15];
            bars.forEach((bar, i) => {
                bar.classList.toggle('active', rms >= thresholds[i]);
            });

            // Track silence for "waiting for audio" message
            const isSilent = rms < 0.01;
            const now = Date.now();

            if (isSilent) {
                if (!this._silenceStartTime) this._silenceStartTime = now;
                if (!this._waitingForAudioShown && (now - this._silenceStartTime) > 5000) {
                    this._updateHelper('Waiting for audio...');
                    this._waitingForAudioShown = true;
                }
                if (this._waitingForAudioShown && (now - this._silenceStartTime) > 15000) {
                    this._updateHelper('No audio detected');
                }
            } else {
                this._silenceStartTime = null;
                if (this._waitingForAudioShown) {
                    this._updateHelper('Listening...');
                    this._waitingForAudioShown = false;
                }
            }
        }

        _startTimer() {
            this._stopTimer();
            this._timerInterval = setInterval(() => {
                const secs = this._voiceService?.getElapsedSeconds() || 0;
                const m = Math.floor(secs / 60).toString().padStart(2, '0');
                const s = (secs % 60).toString().padStart(2, '0');
                const el = document.getElementById('unifiedTimer');
                if (el) el.textContent = `${m}:${s}`;
            }, 1000);
        }

        _stopTimer() {
            if (this._timerInterval) {
                clearInterval(this._timerInterval);
                this._timerInterval = null;
            }
        }

        // =========================================
        // Chunk Processing (transcribe only — no extraction)
        // =========================================

        async _handleChunkReady(blob, seq, dur) {
            if (this._destroyed) return;

            this._chunkCount++;
            this._showProcessing(true, 'Transcribing...');

            try {
                const token = localStorage.getItem('authToken');
                const formData = new FormData();
                formData.append('audio', blob, `chunk_${seq}.webm`);
                formData.append('tabKey', 'unified');
                formData.append('sequenceNumber', seq.toString());
                formData.append('durationSeconds', dur.toString());
                formData.append('patientId', this.patientId.toString());
                formData.append('encounterId', this.encounterId.toString());

                const response = await fetch('/api/medocs-voice/process-chunk', {
                    method: 'POST',
                    headers: token ? { 'Authorization': `Bearer ${token}` } : {},
                    body: formData
                });

                if (!response.ok) {
                    console.error('[UnifiedVoice] Chunk transcription error:', response.status);
                    return;
                }

                const data = await response.json();

                if (data.Success && data.Transcription && data.Transcription.trim()) {
                    this._accumulatedTranscription.push(data.Transcription);
                    this._appendTranscription(data.Transcription);

                    // Trigger extraction after each transcription (debounced to avoid rapid-fire)
                    this._triggerRealtimeExtraction();
                }
            } catch (err) {
                console.error('[UnifiedVoice] Chunk upload error:', err);
            } finally {
                this._showProcessing(false);
            }
        }

        _appendTranscription(text) {
            const scroll = document.getElementById('unifiedTranscriptionScroll');
            if (!scroll) return;

            // Remove placeholder on first real transcription
            if (this._accumulatedTranscription.length === 1) {
                scroll.innerHTML = '';
            }

            const span = document.createElement('span');
            span.textContent = text + ' ';
            span.className = 'unified-transcript-segment';
            scroll.appendChild(span);

            // Auto-scroll to bottom
            scroll.scrollTop = scroll.scrollHeight;
        }

        _showProcessing(show, text) {
            if (show) this._processingCount++;
            else this._processingCount = Math.max(0, this._processingCount - 1);

            const el = document.getElementById('unifiedProcessing');
            const textEl = document.getElementById('unifiedProcessingText');
            if (el) el.classList.toggle('d-none', this._processingCount === 0);
            if (textEl && text) textEl.textContent = text;
        }

        // =========================================
        // Real-time Extraction (triggered after each transcription chunk)
        // =========================================

        /** Debounced extraction — waits for a brief pause, but forces after 15s max */
        _triggerRealtimeExtraction() {
            // Clear any pending debounce
            if (this._extractionDebounceTimer) {
                clearTimeout(this._extractionDebounceTimer);
            }

            // If extraction is already running, mark that we need another one after it finishes
            if (this._isExtracting) {
                this._extractionPending = true;
                return;
            }

            // Max wait: if 15s+ since last extraction, fire immediately (continuous speech)
            const timeSinceLastExtraction = Date.now() - (this._lastExtractionTime || 0);
            if (timeSinceLastExtraction >= 15000) {
                this._triggerPeriodicExtraction();
                return;
            }

            // Debounce: wait a short time in case more chunks arrive quickly
            this._extractionDebounceTimer = setTimeout(() => {
                this._triggerPeriodicExtraction();
            }, EXTRACTION_DEBOUNCE_MS);
        }

        _startPeriodicExtraction() {
            // No-op — extraction is now triggered by transcription chunks
        }

        _stopPeriodicExtraction() {
            if (this._extractionDebounceTimer) {
                clearTimeout(this._extractionDebounceTimer);
                this._extractionDebounceTimer = null;
            }
        }

        /**
         * Gather all existing data from workspace (vitals, history, CC/HPI)
         * so Gemini knows what's already recorded and doesn't re-extract it.
         */
        _gatherExistingData() {
            const ws = window._encounterWorkspace;
            if (!ws) return '';

            const lines = [];

            // Vitals (current encounter)
            const v = (id) => document.getElementById(id)?.value || '';
            const vitals = [];
            if (v('ewVitalSysBp') && v('ewVitalDiaBp')) vitals.push(`BP: ${v('ewVitalSysBp')}/${v('ewVitalDiaBp')} mmHg`);
            if (v('ewVitalHr')) vitals.push(`HR: ${v('ewVitalHr')} bpm`);
            if (v('ewVitalTemp')) vitals.push(`Temp: ${v('ewVitalTemp')} °F`);
            if (v('ewVitalSpO2')) vitals.push(`SpO2: ${v('ewVitalSpO2')}%`);
            if (v('ewVitalRr')) vitals.push(`RR: ${v('ewVitalRr')}/min`);
            if (v('ewVitalWeight')) vitals.push(`Weight: ${v('ewVitalWeight')} lbs`);
            if (v('ewVitalHeight')) vitals.push(`Height: ${v('ewVitalHeight')} in`);
            const vNotes = v('ewVitalNotes');
            if (vNotes) vitals.push(`Notes: ${vNotes}`);
            if (vitals.length > 0) lines.push(`Vitals: ${vitals.join(', ')}`);

            // History items from workspace data arrays
            const n = (val) => (val || '').toString().trim();

            if (Array.isArray(ws._allergies) && ws._allergies.length > 0) {
                lines.push(`Allergies: ${ws._allergies.map(a => n(a.AllergenName || a.allergenName)).filter(Boolean).join(', ')}`);
            }
            if (Array.isArray(ws._medications) && ws._medications.length > 0) {
                lines.push(`Medications: ${ws._medications.map(m => {
                    const name = n(m.DrugName || m.drugName);
                    const dose = n(m.Dosage || m.dosage);
                    return dose ? `${name} ${dose}` : name;
                }).filter(Boolean).join(', ')}`);
            }
            if (Array.isArray(ws._problems) && ws._problems.length > 0) {
                lines.push(`Problems: ${ws._problems.map(p => n(p.Description || p.description)).filter(Boolean).join(', ')}`);
            }
            if (Array.isArray(ws._familyHistory) && ws._familyHistory.length > 0) {
                lines.push(`Family History: ${ws._familyHistory.map(f => `${n(f.Relation || f.relation)}: ${n(f.Condition || f.condition)}`).filter(s => s !== ': ').join(', ')}`);
            }
            if (Array.isArray(ws._socialHistory) && ws._socialHistory.length > 0) {
                lines.push(`Social History: ${ws._socialHistory.map(s => `${n(s.Category || s.category)}: ${n(s.Description || s.description)}`).filter(s => s !== ': ').join(', ')}`);
            }
            if (Array.isArray(ws._immunizations) && ws._immunizations.length > 0) {
                lines.push(`Immunizations: ${ws._immunizations.map(i => n(i.VaccineName || i.vaccineName)).filter(Boolean).join(', ')}`);
            }

            // CC/HPI
            const cc = v('ewCcInput');
            const hpi = v('ewHpiInput');
            if (cc) lines.push(`Chief Complaint: ${cc}`);
            if (hpi) lines.push(`HPI: ${hpi}`);

            return lines.join('\n');
        }

        async _triggerPeriodicExtraction() {
            if (this._isExtracting) return;
            if (this._accumulatedTranscription.length === 0) return;

            const fullText = this._accumulatedTranscription.join('\n').trim();
            if (!fullText) return;

            await this._runExtraction(fullText, false);
        }

        async _runExtraction(fullTranscription, isFinal) {
            console.log(`[UnifiedVoice] === EXTRACTION ${isFinal ? 'FINAL' : 'PERIODIC'} ===`);
            console.log(`[UnifiedVoice] Transcription length: ${fullTranscription.length} chars`);
            this._isExtracting = true;
            this._showProcessing(true, isFinal ? 'Finalizing...' : 'Updating entries...');

            try {
                const token = localStorage.getItem('authToken');
                const existingData = this._gatherExistingData();

                const response = await fetch('/api/medocs-voice/extract-unified', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        ...(token ? { 'Authorization': `Bearer ${token}` } : {})
                    },
                    body: JSON.stringify({
                        Transcription: fullTranscription,
                        ExistingData: existingData
                    })
                });

                if (!response.ok) {
                    console.error('[UnifiedVoice] Extraction error:', response.status);
                    return;
                }

                const data = await response.json();

                console.log('[UnifiedVoice] Extraction response:', data.Success, data.Message, data.ExtractedData ? data.ExtractedData.substring(0, 200) : 'null');

                if (data.Success && data.ExtractedData) {
                    try {
                        const extracted = JSON.parse(data.ExtractedData);
                        console.log('[UnifiedVoice] Parsed extraction:', JSON.stringify(extracted).substring(0, 300));
                        this._applyAllSections(extracted);
                    } catch (e) {
                        console.error('[UnifiedVoice] Failed to parse extracted data:', e);
                    }
                }
            } catch (err) {
                console.error('[UnifiedVoice] Extraction error:', err);
            } finally {
                this._isExtracting = false;
                this._showProcessing(false);
                this._lastExtractionTime = Date.now();

                // If new transcription arrived while extracting, run again with latest
                if (this._extractionPending) {
                    this._extractionPending = false;
                    this._triggerRealtimeExtraction();
                }
            }
        }

        // =========================================
        // Field Population (All Sections)
        // =========================================

        _applyAllSections(data) {
            console.log('[UnifiedVoice] Applying sections:', {
                hasVitals: !!data.vitals,
                hasHistory: !!data.history,
                hasCcHpi: !!data.ccHpi,
                historyKeys: data.history ? Object.keys(data.history).filter(k => Array.isArray(data.history[k]) && data.history[k].length > 0) : []
            });

            if (data.vitals) this._populateVitalsFields(data.vitals);
            if (data.history) this._populateHistoryFields(data.history);
            if (data.ccHpi) this._populateCcHpiFields(data.ccHpi);

            if (this.onFieldsPopulated) this.onFieldsPopulated(data);
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

            if (!anyFieldFound && !document.getElementById('ewVitalSysBp')) {
                this._pendingVitals = data;
            }
        }

        async _populateHistoryFields(data) {
            const workspace = window._encounterWorkspace;
            if (!workspace) {
                console.warn('[UnifiedVoice] No workspace reference for history population');
                return;
            }

            const sectionMap = {
                allergies: 'allergies',
                medications: 'medications',
                problems: 'problems',
                familyHx: 'familyHx',
                socialHx: 'socialHx',
                immunizations: 'immunizations'
            };

            let anyItemSaved = false;

            console.log('[UnifiedVoice] === HISTORY POPULATION START ===');
            console.log('[UnifiedVoice] Raw history data from Gemini:', JSON.stringify(data));

            for (const [aiKey, sectionKey] of Object.entries(sectionMap)) {
                const items = data[aiKey];
                if (!Array.isArray(items) || items.length === 0) continue;

                console.log(`[UnifiedVoice] Processing ${sectionKey}: ${items.length} items`, JSON.stringify(items));

                for (const item of items) {
                    // Skip items with empty primary fields
                    const primaryFields = {
                        allergies: 'allergenName', medications: 'drugName', problems: 'description',
                        familyHx: 'condition', socialHx: 'category', immunizations: 'vaccineName'
                    };
                    const pf = primaryFields[sectionKey];
                    const primaryVal = (item[pf] || '').toString().trim();
                    if (pf && !primaryVal) {
                        console.log(`[UnifiedVoice] SKIPPED ${sectionKey} — empty primary field "${pf}". Full item:`, JSON.stringify(item));
                        continue;
                    }

                    const tbody = document.getElementById(`ewGridBody_${sectionKey}`);
                    const tbodyExists = !!tbody;
                    const rowCount = tbody ? tbody.querySelectorAll('tr[data-id]').length : 0;

                    // Log what's in the DOM grid right now
                    if (tbody) {
                        const existingValues = Array.from(tbody.querySelectorAll('tr[data-id]')).map(row => {
                            const el = row.querySelector(`[data-field="${pf}"]`);
                            return { id: row.dataset.id, value: el ? el.value : '(no field)' };
                        });
                        console.log(`[UnifiedVoice] DOM grid for ${sectionKey}: ${rowCount} rows`, JSON.stringify(existingValues));
                    }

                    // If duplicate exists and has new notes info, update the existing row's notes
                    const isDup = this._isDuplicateHistoryItem(sectionKey, item);
                    console.log(`[UnifiedVoice] ${sectionKey} item "${primaryVal}" — isDuplicate: ${isDup}, tbodyExists: ${tbodyExists}`);

                    if (isDup) {
                        if (item.notes && tbody) {
                            console.log(`[UnifiedVoice] UPDATING notes for existing ${sectionKey} "${primaryVal}" with notes: "${item.notes}"`);
                            await this._updateExistingItemNotes(workspace, sectionKey, item, tbody);
                        } else {
                            console.log(`[UnifiedVoice] SKIPPED duplicate ${sectionKey} "${primaryVal}" (no notes to update)`);
                        }
                        continue;
                    }

                    if (!tbody) {
                        console.log(`[UnifiedVoice] BUFFERED ${sectionKey} "${primaryVal}" — grid not in DOM`);
                        const pendingKey = this._getHistoryKey(sectionKey, item);
                        const alreadyPending = this._pendingHistory.some(p =>
                            this._getHistoryKey(p.sectionKey, p.item) === pendingKey
                        );
                        if (!alreadyPending) {
                            this._pendingHistory.push({ sectionKey, item });
                        }
                        continue;
                    }

                    // Grid is in DOM — save directly via API
                    console.log(`[UnifiedVoice] ADDING NEW ${sectionKey} "${primaryVal}" with notes: "${item.notes || ''}"`);
                    await this._addHistoryItem(workspace, sectionKey, item);
                    anyItemSaved = true;
                }
            }

            // Items are inserted directly into the grid — no re-render needed
            if (anyItemSaved) {
                workspace._markStepComplete(1, true);
            }
        }

        _populateCcHpiFields(data) {
            let anyFieldFound = false;

            if (data.chiefComplaint) {
                const ccEl = document.getElementById('ewCcInput');
                if (ccEl) {
                    ccEl.value = data.chiefComplaint;
                    ccEl.dispatchEvent(new Event('input', { bubbles: true }));
                    this._flashField(ccEl);
                    anyFieldFound = true;
                }
            }

            if (data.hpiNarrative) {
                const hpiEl = document.getElementById('ewHpiInput');
                if (hpiEl) {
                    hpiEl.value = data.hpiNarrative;
                    hpiEl.dispatchEvent(new Event('input', { bubbles: true }));
                    this._flashField(hpiEl);
                    anyFieldFound = true;
                }
            }

            if (!anyFieldFound && !document.getElementById('ewCcInput')) {
                this._pendingCcHpi = data;
            }
        }

        // =========================================
        // Flush Pending Data
        // =========================================

        flushPendingVitals() {
            if (this._pendingVitals) {
                this._populateVitalsFields(this._pendingVitals);
                this._pendingVitals = null;
            }
        }

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

        flushPendingCcHpi() {
            if (this._pendingCcHpi) {
                this._populateCcHpiFields(this._pendingCcHpi);
                this._pendingCcHpi = null;
            }
        }

        // =========================================
        // History Item Helpers
        // =========================================

        async _addHistoryItem(workspace, sectionKey, item) {
            const endpointMap = {
                allergies: 'allergies',
                medications: 'medications',
                problems: 'problems',
                familyHx: 'family-history',
                socialHx: 'social-history',
                immunizations: 'immunizations'
            };

            try {
                const endpoint = endpointMap[sectionKey];
                if (!endpoint) return;

                const data = this._mapHistoryItemToFields(sectionKey, item);
                console.log(`[UnifiedVoice] _addHistoryItem: ${sectionKey} mapped data:`, JSON.stringify(data));

                // Always include encounterId for This Visit tracking
                data.encounterId = workspace.encounterId;

                // Add defaults for specific sections
                if (sectionKey === 'medications') {
                    data.startDate = data.startDate || new Date().toISOString().split('T')[0];
                }
                if (sectionKey === 'immunizations' && !data.administeredDate) {
                    data.administeredDate = new Date().toISOString().split('T')[0];
                }

                // Save via API
                const result = await window.apiRequest(`/patients/${workspace.patientId}/${endpoint}`, {
                    method: 'POST',
                    body: data,
                    showLoader: false
                });

                // Insert row directly into the grid (no re-render needed)
                const tbody = document.getElementById(`ewGridBody_${sectionKey}`);
                if (tbody && result) {
                    const columns = workspace._getGridColumns(sectionKey);
                    const newRowHtml = workspace._renderGridRow(sectionKey, columns, result);
                    const addRow = tbody.querySelector('tr[data-new="true"]');
                    if (addRow) {
                        addRow.insertAdjacentHTML('beforebegin', newRowHtml);
                    } else {
                        tbody.insertAdjacentHTML('beforeend', newRowHtml);
                    }
                    const insertedRow = tbody.querySelector(`tr[data-id="${workspace._getItemId(sectionKey, result)}"]`);
                    if (insertedRow) {
                        workspace._showRowSaveStatus(insertedRow, 'saved');
                        insertedRow.querySelectorAll('input[data-ac]').forEach(inp => workspace._initGridAutocomplete(inp));
                    }
                    workspace._updateSectionCount(sectionKey, 1);
                }

                // Track dedup key
                const key = this._getHistoryKey(sectionKey, item);
                if (key) this._addedHistoryKeys.add(key);

                // Update workspace cache so _gatherExistingData sends correct context
                const cacheMap = {
                    allergies: '_allergies', medications: '_medications', problems: '_problems',
                    familyHx: '_familyHistory', socialHx: '_socialHistory', immunizations: '_immunizations'
                };
                const cacheProp = cacheMap[sectionKey];
                if (cacheProp && workspace[cacheProp] && result) {
                    workspace[cacheProp].push(result);
                }

                console.log(`[UnifiedVoice] Saved ${sectionKey} item:`, data);
            } catch (err) {
                console.error(`[UnifiedVoice] Error saving ${sectionKey} item:`, err, item);
            }
        }

        /** Update notes on an existing history item that was already added */
        async _updateExistingItemNotes(workspace, sectionKey, item, tbody) {
            const endpointMap = {
                allergies: 'allergies', medications: 'medications', problems: 'problems',
                familyHx: 'family-history', socialHx: 'social-history', immunizations: 'immunizations'
            };
            const primaryFields = {
                allergies: 'allergenName', medications: 'drugName', problems: 'description',
                familyHx: 'condition', socialHx: 'category', immunizations: 'vaccineName'
            };

            const pf = primaryFields[sectionKey];
            const endpoint = endpointMap[sectionKey];
            if (!pf || !endpoint) return;

            const normalize = (val) => (val || '').toString().trim().toLowerCase();
            const itemPrimary = normalize(item[pf]);

            console.log(`[UnifiedVoice] _updateExistingItemNotes: looking for ${sectionKey} "${pf}"="${itemPrimary}"`);

            // Find the existing row in DOM
            for (const row of tbody.querySelectorAll('tr[data-id]')) {
                const el = row.querySelector(`[data-field="${pf}"]`);
                const rowVal = el ? normalize(el.value) : '(no el)';
                console.log(`[UnifiedVoice] _updateExistingItemNotes: row ${row.dataset.id} has ${pf}="${rowVal}" (match: ${rowVal === itemPrimary})`);
                if (!el || rowVal !== itemPrimary) continue;

                // Found matching row — replace notes with latest (Gemini has full context, latest is most complete)
                const notesEl = row.querySelector('[data-field="notes"]');
                if (notesEl && item.notes) {
                    const existingNotes = (notesEl.value || '').trim();
                    const newNotes = item.notes.trim();

                    // Safety: reject garbage notes (Unknown, N/A, placeholders)
                    const garbagePattern = /\bunknown\b|^n\/a$|^not (provided|mentioned|specified|available)/i;
                    if (garbagePattern.test(newNotes)) {
                        console.log(`[UnifiedVoice] REJECTED garbage notes for ${sectionKey} "${itemPrimary}": "${newNotes}"`);
                        break;
                    }

                    // Replace if new notes are different and non-empty
                    if (newNotes && newNotes !== existingNotes) {
                        notesEl.value = newNotes;

                        // Save via PUT
                        const id = row.dataset.id;
                        try {
                            await window.apiRequest(`/patients/${workspace.patientId}/${endpoint}/${id}`, {
                                method: 'PUT',
                                body: { notes: newNotes },
                                showLoader: false
                            });
                            workspace._showRowSaveStatus(row, 'saved');
                            console.log(`[UnifiedVoice] Updated notes for ${sectionKey} item: ${itemPrimary}`);
                        } catch (err) {
                            console.error(`[UnifiedVoice] Error updating notes:`, err);
                        }
                    }
                }
                break;
            }
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
                default: return {};
            }
        }

        _isDuplicateHistoryItem(sectionKey, item) {
            const key = this._getHistoryKey(sectionKey, item);
            if (key && this._addedHistoryKeys.has(key)) return true;

            const tbody = document.getElementById(`ewGridBody_${sectionKey}`);
            if (!tbody) return false;

            const existingRows = tbody.querySelectorAll('tr[data-id]');
            const normalize = (val) => (val || '').toString().trim().toLowerCase();

            for (const row of existingRows) {
                const getField = (fieldName) => {
                    const el = row.querySelector(`[data-field="${fieldName}"]`);
                    if (!el) return '';
                    if (el.type === 'checkbox') return el.checked;
                    if (el.tagName === 'SELECT') {
                        const selected = el.options[el.selectedIndex];
                        return selected ? selected.text.trim().toLowerCase() : '';
                    }
                    return normalize(el.value);
                };

                let isDup = false;
                switch (sectionKey) {
                    case 'allergies': isDup = normalize(item.allergenName) === getField('allergenName'); break;
                    case 'medications': isDup = normalize(item.drugName) === getField('drugName'); break;
                    case 'problems': isDup = normalize(item.description) === getField('description'); break;
                    case 'familyHx': isDup = normalize(item.condition) === getField('condition'); break;
                    case 'socialHx': isDup = normalize(item.category) === getField('category'); break;
                    case 'immunizations': isDup = normalize(item.vaccineName) === getField('vaccineName'); break;
                }
                if (isDup) return true;
            }
            return false;
        }

        _getHistoryKey(sectionKey, item) {
            const n = (val) => (val || '').toString().trim().toLowerCase();
            switch (sectionKey) {
                case 'allergies': return `allergy:${n(item.allergenName)}`;
                case 'medications': return `med:${n(item.drugName)}`;
                case 'problems': return `prob:${n(item.description)}`;
                case 'familyHx': return `famhx:${n(item.condition)}`;
                case 'socialHx': return `sochx:${n(item.category)}`;
                case 'immunizations': return `imm:${n(item.vaccineName)}`;
                default: return null;
            }
        }

        _loadExistingHistoryKeys() {
            const ws = window._encounterWorkspace;
            if (!ws) return;
            const n = (val) => (val || '').toString().trim().toLowerCase();

            if (Array.isArray(ws._allergies))
                for (const a of ws._allergies) this._addedHistoryKeys.add(`allergy:${n(a.AllergenName || a.allergenName)}`);
            if (Array.isArray(ws._medications))
                for (const m of ws._medications) this._addedHistoryKeys.add(`med:${n(m.DrugName || m.drugName)}`);
            if (Array.isArray(ws._problems))
                for (const p of ws._problems) this._addedHistoryKeys.add(`prob:${n(p.Description || p.description)}`);
            if (Array.isArray(ws._familyHistory))
                for (const f of ws._familyHistory) this._addedHistoryKeys.add(`famhx:${n(f.Condition || f.condition)}`);
            if (Array.isArray(ws._socialHistory))
                for (const s of ws._socialHistory) this._addedHistoryKeys.add(`sochx:${n(s.Category || s.category)}`);
            if (Array.isArray(ws._immunizations))
                for (const i of ws._immunizations) this._addedHistoryKeys.add(`imm:${n(i.VaccineName || i.vaccineName)}`);
        }

        // =========================================
        // Utilities
        // =========================================

        _flashField(el) {
            if (!el) return;
            el.classList.remove('medocs-field-updated');
            void el.offsetWidth;
            el.classList.add('medocs-field-updated');
        }

        hide() {
            const bar = document.getElementById('unifiedVoiceBar');
            if (bar) bar.classList.add('d-none');
        }

        show() {
            const bar = document.getElementById('unifiedVoiceBar');
            if (bar) bar.classList.remove('d-none');
        }

        isRecording() {
            return this._state === 'recording' || this._state === 'paused';
        }

        destroy() {
            this._destroyed = true;
            this._stopPeriodicExtraction();
            if (this._voiceService) {
                this._voiceService.stopRecording();
                this._voiceService.destroy();
                this._voiceService = null;
            }
            this._stopTimer();
        }
    }

    window.UnifiedVoiceBar = UnifiedVoiceBar;
})();
