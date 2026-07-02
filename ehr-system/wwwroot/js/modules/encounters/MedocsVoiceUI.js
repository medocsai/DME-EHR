/**
 * MedocsVoiceUI.js
 *
 * UI coordinator for MEDOCS AI voice entry in Encounter Workspace.
 * Renders recording controls, sends audio chunks to backend, and populates form fields.
 */
(function () {
    'use strict';

    // Legacy map constants removed — simplified schema uses primary field + notes only

    class MedocsVoiceUI {
        constructor(options = {}) {
            this.tabKey = options.tabKey;
            this.patientId = options.patientId;
            this.encounterId = options.encounterId;
            this.onFieldsPopulated = options.onFieldsPopulated || null;

            this._voiceService = null;
            this._containerEl = null;
            this._state = 'idle'; // idle, recording, paused, processing
            this._transcriptContext = [];
            this._chunkCount = 0;
            this._processingCount = 0;
            this._timerInterval = null;
            this._destroyed = false;

            // Track user-edited fields so AI doesn't overwrite them (Vitals & History)
            this._userEditedFields = new Set();
        }

        render(containerEl) {
            this._containerEl = containerEl;

            if (!MedocsVoiceService.isSupported()) {
                containerEl.innerHTML = `
                    <div class="medocs-voice-bar medocs-voice-unsupported">
                        <div class="medocs-voice-brand">
                            <i class="bi bi-mic-fill"></i>
                            <span>MEDOCS AI</span>
                        </div>
                        <small class="text-muted">Voice entry is not supported in this browser. Please use Chrome, Firefox, or Edge.</small>
                    </div>`;
                return;
            }

            containerEl.innerHTML = this._renderBarHtml();
            this._bindEvents();
            this._trackUserEdits();
        }

        destroy() {
            this._destroyed = true;
            if (this._voiceService) {
                this._voiceService.stopRecording();
                this._voiceService.destroy();
                this._voiceService = null;
            }
            this._stopTimer();
            this._transcriptContext = [];
            this._containerEl = null;
        }

        // =========================================
        // HTML Rendering
        // =========================================

        _renderBarHtml() {
            const tabLabel = { 'vitals': 'Vitals', 'history': 'History', 'cc-hpi': 'CC & HPI' }[this.tabKey] || '';

            return `
                <div class="medocs-voice-bar" id="medocsBar_${this.tabKey}">
                    <div class="medocs-voice-brand">
                        <i class="bi bi-mic-fill"></i>
                        <span>MEDOCS AI</span>
                    </div>

                    <div class="medocs-voice-controls">
                        <!-- Idle state -->
                        <button class="btn btn-medocs-record" id="medocsStartBtn_${this.tabKey}" title="Start voice entry for ${tabLabel}">
                            <i class="bi bi-mic-fill me-1"></i>Start Voice Entry
                        </button>

                        <!-- Recording state (hidden initially) -->
                        <button class="btn btn-medocs-pause d-none" id="medocsPauseBtn_${this.tabKey}" title="Pause recording">
                            <i class="bi bi-pause-fill me-1"></i>Pause
                        </button>
                        <button class="btn btn-medocs-resume d-none" id="medocsResumeBtn_${this.tabKey}" title="Resume recording">
                            <i class="bi bi-play-fill me-1"></i>Resume
                        </button>
                        <button class="btn btn-medocs-stop d-none" id="medocsStopBtn_${this.tabKey}" title="Stop recording">
                            <i class="bi bi-stop-fill me-1"></i>Stop
                        </button>
                    </div>

                    <div class="medocs-voice-status">
                        <span class="medocs-voice-timer d-none" id="medocsTimer_${this.tabKey}">00:00</span>
                        <span class="medocs-voice-level d-none" id="medocsLevel_${this.tabKey}">
                            <span class="medocs-level-bar"></span>
                            <span class="medocs-level-bar"></span>
                            <span class="medocs-level-bar"></span>
                            <span class="medocs-level-bar"></span>
                            <span class="medocs-level-bar"></span>
                        </span>
                        <span class="medocs-voice-processing d-none" id="medocsProcessing_${this.tabKey}">
                            <span class="spinner-border spinner-border-sm" style="width:12px;height:12px;"></span>
                            <small>Processing...</small>
                        </span>
                        <span class="medocs-voice-chunks d-none" id="medocsChunks_${this.tabKey}">0 segments</span>
                    </div>

                    <div class="medocs-voice-helper">
                        <small class="text-muted" id="medocsHelper_${this.tabKey}">
                            <i class="bi bi-lightbulb me-1"></i>Have a natural conversation &mdash; AI will document it for you
                        </small>
                    </div>

                    <div class="medocs-voice-error d-none" id="medocsError_${this.tabKey}">
                        <small class="text-danger"></small>
                    </div>
                </div>
                <div class="medocs-voice-disclaimer">
                    <small class="text-muted">
                        <i class="bi bi-info-circle me-1"></i>AI-generated content may contain errors. Please review and verify all entries before saving.${this.tabKey === 'vitals' || this.tabKey === 'history' ? ' Manually edited values will not be overwritten by AI.' : ''}
                    </small>
                </div>`;
        }

        // =========================================
        // Event Binding
        // =========================================

        _bindEvents() {
            const tk = this.tabKey;

            document.getElementById(`medocsStartBtn_${tk}`)?.addEventListener('click', () => this._startRecording());
            document.getElementById(`medocsPauseBtn_${tk}`)?.addEventListener('click', () => this._pauseRecording());
            document.getElementById(`medocsResumeBtn_${tk}`)?.addEventListener('click', () => this._resumeRecording());
            document.getElementById(`medocsStopBtn_${tk}`)?.addEventListener('click', () => this._stopRecording());
        }

        /**
         * Track manual user edits on Vitals fields.
         * Once a user manually changes a field, AI will not overwrite it.
         */
        _trackUserEdits() {
            if (this.tabKey !== 'vitals') return;

            const vitalFieldIds = [
                'ewVitalSysBp', 'ewVitalDiaBp', 'ewVitalHr', 'ewVitalTemp',
                'ewVitalSpO2', 'ewVitalRr', 'ewVitalWeight', 'ewVitalHeight', 'ewVitalNotes'
            ];

            // Use 'keydown' to detect genuine keyboard input (not programmatic changes)
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
                this._voiceService = new MedocsVoiceService({
                    silenceThreshold: 0.01,
                    silenceDuration: 1200,
                    minChunkDuration: 3000
                });

                this._voiceService.onChunkReady((blob, seq, dur) => this._handleChunkReady(blob, seq, dur));
                this._voiceService.onAudioLevel((rms) => this._updateAudioLevel(rms));
                this._voiceService.onError((err) => this._showError(MedocsVoiceService.getErrorMessage(err)));
                this._voiceService.onProlongedSilence(() => this._handleProlongedSilence());

                await this._voiceService.startRecording();

                this._chunkCount = 0;
                this._transcriptContext = [];
                this._updateUI('recording');
                this._startTimer();
                this._updateHelper('Speak naturally — fields will fill as you talk');
            } catch (err) {
                this._showError(MedocsVoiceService.getErrorMessage(err));
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
                this._updateHelper('Speak naturally — fields will fill as you talk');
            }
        }

        _stopRecording() {
            if (this._voiceService) {
                this._voiceService.stopRecording();
                this._voiceService.destroy();
                this._voiceService = null;
            }
            this._stopTimer();
            this._updateUI('idle');
            this._updateHelper('Have a natural conversation — AI will document it for you');
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
        // Chunk Processing
        // =========================================

        async _handleChunkReady(blob, seq, dur) {
            if (this._destroyed) return;

            this._chunkCount++;
            this._updateChunkCount();
            this._processingCount++;
            this._showProcessing(true);

            try {
                const formData = new FormData();
                formData.append('audio', blob, `chunk_${seq}.webm`);
                formData.append('tabKey', this.tabKey);
                formData.append('sequenceNumber', seq.toString());
                formData.append('durationSeconds', dur.toString());
                formData.append('patientId', this.patientId.toString());
                formData.append('encounterId', this.encounterId.toString());

                // Build progressive context (cap at last 5 segments)
                const contextSegments = this._transcriptContext.slice(-5);
                if (contextSegments.length > 0) {
                    formData.append('previousContext', contextSegments.join('\n\n'));
                }

                const token = localStorage.getItem('authToken');
                const response = await fetch('/api/medocs-voice/process-chunk', {
                    method: 'POST',
                    headers: token ? { 'Authorization': `Bearer ${token}` } : {},
                    body: formData
                });

                if (this._destroyed) return;

                if (!response.ok) {
                    const errorText = await response.text().catch(() => '');
                    console.warn(`[MedocsVoiceUI] Server error ${response.status}:`, errorText);
                    return;
                }

                const data = await response.json();

                if (data.Success && data.Transcription) {
                    this._transcriptContext.push(data.Transcription);

                    if (data.ExtractedData) {
                        try {
                            const extracted = JSON.parse(data.ExtractedData);
                            this._applyExtractedData(extracted);
                        } catch (parseErr) {
                            console.warn('[MedocsVoiceUI] Failed to parse extracted data:', parseErr);
                        }
                    }
                } else if (!data.Success) {
                    console.warn('[MedocsVoiceUI] Chunk processing failed:', data.Message);
                }
            } catch (err) {
                console.error('[MedocsVoiceUI] Chunk upload error:', err);
            } finally {
                this._processingCount--;
                if (this._processingCount <= 0) {
                    this._processingCount = 0;
                    this._showProcessing(false);
                }
            }
        }

        // =========================================
        // Field Population
        // =========================================

        _applyExtractedData(data) {
            switch (this.tabKey) {
                case 'vitals':
                    this._populateVitalsFields(data);
                    break;
                case 'history':
                    this._populateHistoryFields(data);
                    break;
                case 'cc-hpi':
                    this._populateCcHpiFields(data);
                    break;
            }

            if (this.onFieldsPopulated) {
                this.onFieldsPopulated(this.tabKey, data);
            }
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

            for (const [key, elementId] of Object.entries(fieldMap)) {
                if (data[key] != null) {
                    // Skip fields the user has manually edited
                    if (this._userEditedFields.has(elementId)) continue;

                    const el = document.getElementById(elementId);
                    if (el) {
                        el.value = data[key];
                        el.dispatchEvent(new Event('input', { bubbles: true }));
                        this._flashField(el);
                    }
                }
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
                    // Client-side dedup: skip items already in the grid
                    if (this._isDuplicateHistoryItem(sectionKey, item)) {
                        console.log(`[MedocsVoiceUI] Skipping duplicate ${sectionKey} item:`, item);
                        continue;
                    }
                    await this._addHistoryItem(workspace, sectionKey, item);
                }
            }
        }

        async _addHistoryItem(workspace, sectionKey, item) {
            // Open an add row for this section (creates one if not already open)
            workspace._openAddRow(sectionKey);

            // Small delay to ensure the add row is in the DOM
            await new Promise(r => setTimeout(r, 150));

            // Find the add row
            const newRow = document.querySelector(`tr[data-section="${sectionKey}"][data-new="true"]`);
            if (!newRow) return;

            // Map AI response fields to grid data-field attributes
            const fieldValues = this._mapHistoryItemToFields(sectionKey, item);

            // Fill in the new row's fields
            for (const [field, value] of Object.entries(fieldValues)) {
                if (value == null) continue;
                const el = newRow.querySelector(`[data-field="${field}"]`);
                if (!el) continue;

                if (el.type === 'checkbox') {
                    el.checked = !!value;
                } else if (el.tagName === 'SELECT') {
                    // Find matching option
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

            // Trigger save via the workspace's _saveNewRow
            await workspace._saveNewRow(sectionKey, newRow);

            // Small delay before adding the next item
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
         * Check if a history item already exists in the grid (client-side dedup).
         * Matches on key fields per section, case-insensitive.
         */
        _isDuplicateHistoryItem(sectionKey, item) {
            const tbody = document.getElementById(`ewGridBody_${sectionKey}`);
            if (!tbody) return false;

            const existingRows = tbody.querySelectorAll('tr[data-id]');
            if (existingRows.length === 0) return false;

            const normalize = (val) => (val || '').toString().trim().toLowerCase();

            for (const row of existingRows) {
                const getField = (fieldName) => {
                    const el = row.querySelector(`[data-field="${fieldName}"]`);
                    if (!el) return '';
                    if (el.type === 'checkbox') return el.checked;
                    if (el.tagName === 'SELECT') {
                        // Compare by selected option text for readability
                        const selected = el.options[el.selectedIndex];
                        return selected ? selected.text.trim().toLowerCase() : '';
                    }
                    return normalize(el.value);
                };

                let isDup = false;
                switch (sectionKey) {
                    case 'allergies':
                        isDup = normalize(item.allergenName) === getField('allergenName');
                        break;
                    case 'medications':
                        isDup = normalize(item.drugName) === getField('drugName');
                        break;
                    case 'problems':
                        isDup = normalize(item.description) === getField('description')
                            || (item.icdCode && normalize(item.icdCode) === getField('icdCode'));
                        break;
                    case 'familyHx':
                        isDup = normalize(item.relation) === getField('relation')
                            && normalize(item.condition) === getField('condition');
                        break;
                    case 'socialHx':
                        isDup = normalize(item.category) === getField('category')
                            && normalize(item.description) === getField('description');
                        break;
                    case 'immunizations':
                        isDup = normalize(item.vaccineName) === getField('vaccineName');
                        break;
                }

                if (isDup) return true;
            }

            return false;
        }

        _populateCcHpiFields(data) {
            if (data.chiefComplaint) {
                const ccEl = document.getElementById('ewCcInput');
                if (ccEl) {
                    ccEl.value = data.chiefComplaint;
                    ccEl.dispatchEvent(new Event('input', { bubbles: true }));
                    this._flashField(ccEl);
                }
            }

            if (data.hpiNarrative) {
                const hpiEl = document.getElementById('ewHpiInput');
                if (hpiEl) {
                    hpiEl.value = data.hpiNarrative;
                    hpiEl.dispatchEvent(new Event('input', { bubbles: true }));
                    this._flashField(hpiEl);
                }
            }
        }

        // =========================================
        // UI Updates
        // =========================================

        _updateUI(state) {
            this._state = state;
            const tk = this.tabKey;

            const startBtn = document.getElementById(`medocsStartBtn_${tk}`);
            const pauseBtn = document.getElementById(`medocsPauseBtn_${tk}`);
            const resumeBtn = document.getElementById(`medocsResumeBtn_${tk}`);
            const stopBtn = document.getElementById(`medocsStopBtn_${tk}`);
            const timer = document.getElementById(`medocsTimer_${tk}`);
            const level = document.getElementById(`medocsLevel_${tk}`);
            const chunks = document.getElementById(`medocsChunks_${tk}`);
            const bar = document.getElementById(`medocsBar_${tk}`);

            // Reset all
            startBtn?.classList.add('d-none');
            pauseBtn?.classList.add('d-none');
            resumeBtn?.classList.add('d-none');
            stopBtn?.classList.add('d-none');
            timer?.classList.add('d-none');
            level?.classList.add('d-none');
            chunks?.classList.add('d-none');

            bar?.classList.remove('medocs-recording', 'medocs-paused');

            switch (state) {
                case 'idle':
                    startBtn?.classList.remove('d-none');
                    break;
                case 'recording':
                    pauseBtn?.classList.remove('d-none');
                    stopBtn?.classList.remove('d-none');
                    timer?.classList.remove('d-none');
                    level?.classList.remove('d-none');
                    chunks?.classList.remove('d-none');
                    bar?.classList.add('medocs-recording');
                    break;
                case 'paused':
                    resumeBtn?.classList.remove('d-none');
                    stopBtn?.classList.remove('d-none');
                    timer?.classList.remove('d-none');
                    chunks?.classList.remove('d-none');
                    bar?.classList.add('medocs-paused');
                    break;
            }
        }

        _updateHelper(text) {
            const el = document.getElementById(`medocsHelper_${this.tabKey}`);
            if (el) el.innerHTML = `<i class="bi bi-lightbulb me-1"></i>${text}`;
        }

        _updateChunkCount() {
            const el = document.getElementById(`medocsChunks_${this.tabKey}`);
            if (el) el.textContent = `${this._chunkCount} segment${this._chunkCount !== 1 ? 's' : ''}`;
        }

        _showProcessing(show) {
            const el = document.getElementById(`medocsProcessing_${this.tabKey}`);
            if (el) el.classList.toggle('d-none', !show);
        }

        _showError(message) {
            const el = document.getElementById(`medocsError_${this.tabKey}`);
            if (el) {
                el.classList.remove('d-none');
                el.querySelector('small').textContent = message;
            }
        }

        _hideError() {
            const el = document.getElementById(`medocsError_${this.tabKey}`);
            if (el) el.classList.add('d-none');
        }

        _updateAudioLevel(rms) {
            const el = document.getElementById(`medocsLevel_${this.tabKey}`);
            if (!el) return;

            const bars = el.querySelectorAll('.medocs-level-bar');
            const normalized = Math.min(1, rms * 10); // Amplify for visibility
            const activeBars = Math.round(normalized * bars.length);

            bars.forEach((bar, i) => {
                bar.classList.toggle('active', i < activeBars);
            });
        }

        // =========================================
        // Timer
        // =========================================

        _startTimer() {
            this._stopTimer();
            this._timerInterval = setInterval(() => {
                if (this._voiceService && this._state === 'recording') {
                    const secs = this._voiceService.getElapsedSeconds();
                    const m = Math.floor(secs / 60).toString().padStart(2, '0');
                    const s = (secs % 60).toString().padStart(2, '0');
                    const el = document.getElementById(`medocsTimer_${this.tabKey}`);
                    if (el) el.textContent = `${m}:${s}`;
                }
            }, 1000);
        }

        _stopTimer() {
            if (this._timerInterval) {
                clearInterval(this._timerInterval);
                this._timerInterval = null;
            }
        }

        // =========================================
        // Visual Feedback
        // =========================================

        _flashField(el) {
            el.classList.remove('medocs-field-updated');
            // Force reflow
            void el.offsetWidth;
            el.classList.add('medocs-field-updated');
        }
    }

    window.MedocsVoiceUI = MedocsVoiceUI;
})();
