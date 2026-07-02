/**
 * CareNotesVoice.js
 * -----------------
 * Live mic dictation for the Care Notes "New Care Note" form.
 *
 * Why a separate file:
 *   The textarea + mic UI is its own concern -- recording, chunked upload,
 *   live append. Keeping it out of CareNotesModule.js means the patient-
 *   profile tab module stays focused on list / save / edit / delete.
 *
 * Reuses (Independent Contractor):
 *   - window.MedocsVoiceService     (silence + 10s chunking + EBML init segments,
 *                                    used by the encounter "Start AI Voice" flow.
 *                                    Loaded globally in _Layout.cshtml.)
 *
 * Backend it talks to:
 *   POST /api/care-notes/transcribe-chunk  (audio + previousText -> raw text)
 *
 * UX flow (faithful transcription, no polish, no AI rewriting):
 *   1. User clicks Mic. We start MedocsVoiceService and reveal the recording bar.
 *   2. While recording, every silence / 10s -> chunk emitted -> uploaded ->
 *      raw text appended live to the textarea. User sees their words appearing
 *      exactly as they spoke them.
 *   3. User clicks "Stop & Transcribe". We:
 *        a) call voiceService.stopRecording() -- finalizes any pending audio
 *           into ONE more chunk (last words are NOT lost).
 *        b) wait for the in-flight chunk uploads to settle.
 *        c) done. The textarea already has the final text.
 *   4. User clicks Cancel: stop, clear what was transcribed (keep what user typed by hand).
 *
 * Public API:
 *   const voice = new CareNotesVoice({
 *       textareaId, micBtnId, micLabelId,
 *       recordingBarId, timerId,
 *       stopBtnId, cancelBtnId,
 *       patientId
 *   });
 *   voice.destroy();   // call when the form is unmounted
 */
(function () {
    'use strict';

    class CareNotesVoice {
        constructor(opts) {
            this.opts = opts || {};
            this.patientId = opts.patientId || null;

            // Resolve elements once
            this.$textarea     = document.getElementById(opts.textareaId);
            this.$micBtn       = document.getElementById(opts.micBtnId);
            this.$micLabel     = document.getElementById(opts.micLabelId);
            this.$recordingBar = document.getElementById(opts.recordingBarId);
            this.$timer        = document.getElementById(opts.timerId);
            this.$stopBtn      = document.getElementById(opts.stopBtnId);
            this.$cancelBtn    = document.getElementById(opts.cancelBtnId);

            // State
            this._voiceService = null;
            this._isRecording = false;
            this._destroyed = false;
            this._timerHandle = null;

            // Running raw transcript (everything we've appended live).
            // This is what we send to /polish on Stop, and what we send as
            // previousText with each chunk upload for context.
            this._rawTranscript = '';

            // Snapshot of textarea content BEFORE recording started, so we don't
            // overwrite anything the user typed by hand.
            this._userTypedPrefix = '';

            // Track in-flight chunk uploads so Stop waits for them
            this._pendingUploads = 0;
            this._allUploadsResolved = null;

            // If user clicked Stop, we polish at the end. If user clicked Cancel,
            // we skip polish and don't touch the textarea further.
            this._stopMode = null; // 'stop' | 'cancel' | null

            this._wireButtons();
        }

        // =========================================
        // Lifecycle
        // =========================================
        destroy() {
            this._destroyed = true;
            this._stopTimer();
            if (this._voiceService) {
                try { this._voiceService.destroy(); } catch (e) { /* ignore */ }
                this._voiceService = null;
            }
        }

        // =========================================
        // Buttons
        // =========================================
        _wireButtons() {
            if (this.$micBtn)    this.$micBtn.addEventListener('click', () => this._startRecording());
            if (this.$stopBtn)   this.$stopBtn.addEventListener('click', () => this._stop('stop'));
            if (this.$cancelBtn) this.$cancelBtn.addEventListener('click', () => this._stop('cancel'));
        }

        // =========================================
        // Recording
        // =========================================
        async _startRecording() {
            if (this._isRecording) return;
            if (typeof window.MedocsVoiceService === 'undefined') {
                showToast('Not Supported', 'Voice service is not available on this page.', 'error');
                return;
            }
            if (!window.MedocsVoiceService.isSupported()) {
                showToast('Not Supported', 'Your browser does not support audio recording.', 'error');
                return;
            }

            // Snapshot whatever the user already typed so we append after it
            this._userTypedPrefix = (this.$textarea?.value || '').trim();
            this._rawTranscript = '';
            this._stopMode = null;
            this._pendingUploads = 0;

            this._voiceService = new window.MedocsVoiceService({
                silenceThreshold: 0.01,
                silenceDuration: 1500,   // 1.5s of quiet => sentence boundary
                minChunkDuration: 3000,  // don't make tiny < 3s chunks
                maxChunkDuration: 10000  // hard cut at 10s during continuous speech
            });

            this._voiceService.onChunkReady((blob, seq, dur) => this._handleChunk(blob, seq, dur));
            this._voiceService.onError((err) => {
                console.warn('[CareNotesVoice] error:', err);
                showToast('Mic Error', window.MedocsVoiceService.getErrorMessage(err), 'error');
                this._cleanupAfterStop();
            });

            try {
                await this._voiceService.startRecording();
            } catch (e) {
                showToast('Mic Error', window.MedocsVoiceService.getErrorMessage(e), 'error');
                this._cleanupAfterStop();
                return;
            }

            this._isRecording = true;
            this._setRecordingUi(true);
            this._startTimer();
        }

        async _stop(mode) {
            if (!this._isRecording) return;
            this._stopMode = mode;
            this._isRecording = false;
            this._stopTimer();
            this._setRecordingUi(false);

            // Tell MedocsVoiceService to finalize. This emits ONE final
            // onChunkReady for any pending audio (the user's last words are
            // packaged into one more chunk and uploaded -- nothing is lost).
            if (this._voiceService) {
                try { this._voiceService.stopRecording(); } catch (e) { /* ignore */ }
            }

            if (mode === 'cancel') {
                // Wait for any in-flight uploads so we don't append text after
                // the user cancelled, then clear what we appended live.
                await this._waitForUploads();
                this._restoreUserTypedOnly();
                this._cleanupAfterStop();
                return;
            }

            // mode === 'stop': wait for any pending uploads (especially the
            // final chunk we just kicked off via stopRecording) so the textarea
            // contains EVERYTHING the nurse said before we hand control back.
            this._setMicLabel('Finishing...');
            await this._waitForUploads();
            this._setMicLabel('Mic');

            if (!this._rawTranscript.trim()) {
                showToast('Empty', 'No speech detected.', 'warning');
            }
            this._cleanupAfterStop();
        }

        _cleanupAfterStop() {
            this._setMicLabel('Mic');
            if (this._voiceService) {
                try { this._voiceService.destroy(); } catch (e) { /* ignore */ }
                this._voiceService = null;
            }
            this._isRecording = false;
        }

        // =========================================
        // Chunk upload (called by MedocsVoiceService)
        // =========================================
        async _handleChunk(blob, seq, dur) {
            if (this._destroyed) return;
            if (this._stopMode === 'cancel') return;

            this._pendingUploads++;
            try {
                const fd = new FormData();
                fd.append('audio', blob, `chunk_${seq}.webm`);
                if (this.patientId) fd.append('patientId', String(this.patientId));
                // Send running raw transcript as context. Helps Gemini keep
                // medical terminology / sentence flow consistent.
                if (this._rawTranscript) fd.append('previousText', this._rawTranscript);

                const token = localStorage.getItem('authToken');
                const resp = await fetch('/api/care-notes/transcribe-chunk', {
                    method: 'POST',
                    headers: token ? { 'Authorization': `Bearer ${token}` } : {},
                    body: fd
                });

                if (this._destroyed) return;
                if (!resp.ok) {
                    console.warn('[CareNotesVoice] chunk failed:', resp.status);
                    return;
                }

                const data = await resp.json().catch(() => ({}));
                const chunkText = (data && data.text) ? data.text.trim() : '';
                if (!chunkText) return;

                // Append to running raw transcript + live update textarea
                if (this._rawTranscript) this._rawTranscript += ' ';
                this._rawTranscript += chunkText;
                this._renderLiveTextarea();
            } catch (e) {
                console.warn('[CareNotesVoice] chunk error:', e);
            } finally {
                this._pendingUploads--;
                if (this._pendingUploads <= 0) {
                    this._pendingUploads = 0;
                    if (this._allUploadsResolved) {
                        const r = this._allUploadsResolved;
                        this._allUploadsResolved = null;
                        r();
                    }
                }
            }
        }

        _renderLiveTextarea() {
            if (!this.$textarea) return;
            const prefix = this._userTypedPrefix
                ? this._userTypedPrefix + '\n\n'
                : '';
            this.$textarea.value = prefix + this._rawTranscript;
            // Keep cursor at end so user sees latest text
            try {
                this.$textarea.scrollTop = this.$textarea.scrollHeight;
            } catch (e) { /* ignore */ }
        }

        _restoreUserTypedOnly() {
            if (!this.$textarea) return;
            this.$textarea.value = this._userTypedPrefix;
            this._rawTranscript = '';
        }

        _waitForUploads() {
            if (this._pendingUploads <= 0) return Promise.resolve();
            return new Promise(resolve => { this._allUploadsResolved = resolve; });
        }

        // =========================================
        // UI helpers
        // =========================================
        _setRecordingUi(isRecording) {
            if (this.$micBtn) {
                if (isRecording) this.$micBtn.classList.add('recording');
                else this.$micBtn.classList.remove('recording');
            }
            if (this.$recordingBar) {
                if (isRecording) {
                    this.$recordingBar.classList.remove('d-none');
                    this.$recordingBar.classList.add('d-flex');
                } else {
                    this.$recordingBar.classList.add('d-none');
                    this.$recordingBar.classList.remove('d-flex');
                }
            }
        }

        _setMicLabel(text) {
            if (this.$micLabel) this.$micLabel.textContent = text;
        }

        _startTimer() {
            this._tickTimer();
            this._timerHandle = setInterval(() => this._tickTimer(), 1000);
        }

        _stopTimer() {
            if (this._timerHandle) {
                clearInterval(this._timerHandle);
                this._timerHandle = null;
            }
        }

        _tickTimer() {
            if (!this.$timer || !this._voiceService) return;
            const sec = this._voiceService.getElapsedSeconds();
            const mm = Math.floor(sec / 60);
            const ss = String(sec % 60).padStart(2, '0');
            this.$timer.textContent = `${mm}:${ss}`;
        }
    }

    window.CareNotesVoice = CareNotesVoice;
})();
