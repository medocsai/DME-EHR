/**
 * TelehealthScribeService.js
 *
 * Audio capture service for Telehealth AI Scribe.
 * Captures BOTH the provider's microphone AND patient's voice (via getDisplayMedia tab audio),
 * mixes them using Web Audio API, and provides silence-detected chunking.
 *
 * Follows the same patterns as MedocsVoiceService.js but with dual-stream capture.
 */
(function () {
    'use strict';

    const ScribeStatus = {
        IDLE: 'idle',
        RECORDING: 'recording',
        PAUSED: 'paused',
        STOPPED: 'stopped'
    };

    class TelehealthScribeService {
        constructor(options = {}) {
            // Silence detection config
            this._silenceThreshold = options.silenceThreshold || 0.01;
            this._silenceDuration = options.silenceDuration || 1200;
            this._minChunkDuration = options.minChunkDuration || 5000; // 5s for telehealth (longer natural conversation)
            this._analyzerInterval = options.analyzerInterval || 100;

            // Recording state
            this._mediaRecorder = null;
            this._micStream = null;
            this._tabAudioStream = null;
            this._mixedStream = null;
            this._audioChunks = [];
            this._initSegment = null;
            this._status = ScribeStatus.IDLE;
            this._mimeType = '';
            this._recordingStartTime = null;
            this._pausedDuration = 0;
            this._pauseStartTime = null;
            this._micOnly = false; // true if getDisplayMedia failed

            // Web Audio API
            this._audioContext = null;
            this._analyser = null;
            this._micSource = null;
            this._tabSource = null;
            this._micGain = null;
            this._tabGain = null;
            this._destination = null;
            this._analyzerIntervalId = null;
            this._analyzerBuffer = null;

            // Silence detection state
            this._silenceStartTime = null;
            this._inSilence = false;
            this._hasDetectedSpeech = false;
            this._currentChunkStartTime = null;
            this._currentChunkStartIndex = 0;

            // Chunk management
            this._chunkSequenceNumber = 0;

            // Callbacks
            this._onChunkReady = null;
            this._onAudioLevel = null;
            this._onError = null;
            this._onStatusChange = null;
            this._onMicOnlyFallback = null;
        }

        // =========================================
        // Static
        // =========================================

        static isSupported() {
            return !!(navigator.mediaDevices &&
                navigator.mediaDevices.getUserMedia &&
                navigator.mediaDevices.getDisplayMedia &&
                typeof MediaRecorder !== 'undefined');
        }

        static getErrorMessage(error) {
            const msg = error?.message || error || '';
            switch (msg) {
                case 'PERMISSION_DENIED':
                    return 'Microphone access is required. Please allow microphone access in your browser settings.';
                case 'DISPLAY_PERMISSION_DENIED':
                    return 'Tab audio sharing was denied. AI Scribe will capture provider audio only.';
                case 'NO_MICROPHONE':
                    return 'No microphone found. Please connect a microphone and try again.';
                case 'MICROPHONE_IN_USE':
                    return 'Microphone is being used by another application.';
                case 'NOT_SUPPORTED':
                    return 'Telehealth AI Scribe requires Chrome or Edge browser.';
                case 'TAB_SHARING_STOPPED':
                    return 'Tab audio sharing was stopped. Patient audio is no longer captured.';
                default:
                    return 'An error occurred with audio capture. Please try again.';
            }
        }

        // =========================================
        // Public API
        // =========================================

        async startRecording() {
            if (!TelehealthScribeService.isSupported()) {
                throw new Error('NOT_SUPPORTED');
            }

            if (this._status === ScribeStatus.RECORDING) {
                return;
            }

            // Stop any other active instance
            if (TelehealthScribeService._activeInstance && TelehealthScribeService._activeInstance !== this) {
                TelehealthScribeService._activeInstance.stopRecording();
            }
            // Also stop any active MedocsVoiceService instance
            if (typeof MedocsVoiceService !== 'undefined' && MedocsVoiceService._activeInstance) {
                MedocsVoiceService._activeInstance.stopRecording();
            }

            try {
                // Step 1: Get microphone stream (required)
                this._micStream = await this._requestMicrophoneAccess();

                // Step 2: Get tab audio stream (optional — falls back to mic-only)
                try {
                    this._tabAudioStream = await this._requestTabAudio();
                } catch (tabErr) {
                    console.warn('[TelehealthScribe] Tab audio capture failed, falling back to mic-only:', tabErr.message);
                    this._micOnly = true;
                    if (this._onMicOnlyFallback) {
                        this._onMicOnlyFallback();
                    }
                }

                // Step 3: Mix audio streams via Web Audio API
                this._setupAudioMixer();

                // Step 4: Create MediaRecorder on mixed/mic stream
                const recordStream = this._mixedStream || this._micStream;
                this._mediaRecorder = this._createMediaRecorder(recordStream);

                // Reset state
                this._audioChunks = [];
                this._initSegment = null;
                this._pausedDuration = 0;
                this._pauseStartTime = null;
                this._silenceStartTime = null;
                this._inSilence = false;
                this._hasDetectedSpeech = false;
                this._currentChunkStartTime = Date.now();
                this._currentChunkStartIndex = 0;
                this._chunkSequenceNumber = 0;

                this._mediaRecorder.start(1000);
                this._recordingStartTime = Date.now();
                this._setStatus(ScribeStatus.RECORDING);
                this._startAudioAnalysis();

                TelehealthScribeService._activeInstance = this;
            } catch (error) {
                this._cleanup();
                throw error;
            }
        }

        pauseRecording() {
            if (this._mediaRecorder && this._status === ScribeStatus.RECORDING) {
                this._mediaRecorder.pause();
                this._pauseStartTime = Date.now();
                this._setStatus(ScribeStatus.PAUSED);
                this._stopAudioAnalysis();
            }
        }

        resumeRecording() {
            if (this._mediaRecorder && this._status === ScribeStatus.PAUSED) {
                if (this._pauseStartTime) {
                    this._pausedDuration += Date.now() - this._pauseStartTime;
                    this._pauseStartTime = null;
                }
                this._mediaRecorder.resume();
                this._setStatus(ScribeStatus.RECORDING);
                this._startAudioAnalysis();
            }
        }

        stopRecording() {
            this._stopAudioAnalysis();
            this._finalizeCurrentChunk();

            if (this._mediaRecorder) {
                if (this._status === ScribeStatus.PAUSED && this._pauseStartTime) {
                    this._pausedDuration += Date.now() - this._pauseStartTime;
                }
                if (this._mediaRecorder.state !== 'inactive') {
                    this._mediaRecorder.stop();
                }
            }

            this._cleanup();
            this._setStatus(ScribeStatus.STOPPED);

            if (TelehealthScribeService._activeInstance === this) {
                TelehealthScribeService._activeInstance = null;
            }
        }

        getStatus() { return this._status; }
        isMicOnly() { return this._micOnly; }

        getElapsedSeconds() {
            if (!this._recordingStartTime) return 0;
            let elapsed = Date.now() - this._recordingStartTime - this._pausedDuration;
            if (this._status === ScribeStatus.PAUSED && this._pauseStartTime) {
                elapsed -= (Date.now() - this._pauseStartTime);
            }
            return Math.max(0, Math.floor(elapsed / 1000));
        }

        onChunkReady(callback) { this._onChunkReady = callback; }
        onAudioLevel(callback) { this._onAudioLevel = callback; }
        onError(callback) { this._onError = callback; }
        onStatusChange(callback) { this._onStatusChange = callback; }
        onMicOnlyFallback(callback) { this._onMicOnlyFallback = callback; }

        destroy() {
            this._stopAudioAnalysis();
            if (this._mediaRecorder && this._mediaRecorder.state !== 'inactive') {
                try { this._mediaRecorder.stop(); } catch (e) { /* ignore */ }
            }
            this._mediaRecorder = null;
            this._cleanup();
            this._audioChunks = [];
            this._initSegment = null;
            this._chunkSequenceNumber = 0;
            this._setStatus(ScribeStatus.IDLE);

            if (TelehealthScribeService._activeInstance === this) {
                TelehealthScribeService._activeInstance = null;
            }

            this._onChunkReady = null;
            this._onAudioLevel = null;
            this._onError = null;
            this._onStatusChange = null;
            this._onMicOnlyFallback = null;
        }

        // =========================================
        // Audio Access
        // =========================================

        async _requestMicrophoneAccess() {
            try {
                return await navigator.mediaDevices.getUserMedia({
                    audio: {
                        echoCancellation: true,
                        noiseSuppression: true,
                        autoGainControl: true,
                        sampleRate: 44100
                    }
                });
            } catch (err) {
                if (err.name === 'NotAllowedError') throw new Error('PERMISSION_DENIED');
                if (err.name === 'NotFoundError') throw new Error('NO_MICROPHONE');
                if (err.name === 'NotReadableError') throw new Error('MICROPHONE_IN_USE');
                throw err;
            }
        }

        async _requestTabAudio() {
            try {
                const displayStream = await navigator.mediaDevices.getDisplayMedia({
                    video: true,  // Required by spec
                    audio: true,
                    preferCurrentTab: true
                });

                // Stop video track immediately — we only need audio
                displayStream.getVideoTracks().forEach(t => t.stop());

                const audioTracks = displayStream.getAudioTracks();
                if (audioTracks.length === 0) {
                    throw new Error('DISPLAY_PERMISSION_DENIED');
                }

                // Handle tab audio track ending (user clicks "Stop sharing" in Chrome)
                audioTracks[0].addEventListener('ended', () => {
                    console.log('[TelehealthScribe] Tab audio track ended');
                    if (this._status === ScribeStatus.RECORDING) {
                        // Detach tab source from mixer, continue mic-only
                        this._micOnly = true;
                        if (this._tabSource) {
                            try { this._tabSource.disconnect(); } catch (e) {}
                            this._tabSource = null;
                        }
                        if (this._onError) {
                            this._onError({ message: 'TAB_SHARING_STOPPED' });
                        }
                    }
                });

                return displayStream;
            } catch (err) {
                if (err.name === 'NotAllowedError' || err.name === 'AbortError') {
                    throw new Error('DISPLAY_PERMISSION_DENIED');
                }
                throw err;
            }
        }

        // =========================================
        // Audio Mixer (Web Audio API)
        // =========================================

        _setupAudioMixer() {
            this._audioContext = new (window.AudioContext || window.webkitAudioContext)();

            // Create mic source
            this._micSource = this._audioContext.createMediaStreamSource(this._micStream);
            this._micGain = this._audioContext.createGain();
            this._micGain.gain.value = 1.0;

            // Create destination for mixed output
            this._destination = this._audioContext.createMediaStreamDestination();

            // Connect mic → gain → destination
            this._micSource.connect(this._micGain);
            this._micGain.connect(this._destination);

            if (this._tabAudioStream && !this._micOnly) {
                // Create tab audio source
                this._tabSource = this._audioContext.createMediaStreamSource(this._tabAudioStream);
                this._tabGain = this._audioContext.createGain();
                this._tabGain.gain.value = 1.0;

                // Connect tab → gain → destination
                this._tabSource.connect(this._tabGain);
                this._tabGain.connect(this._destination);
            }

            this._mixedStream = this._destination.stream;

            // Set up analyzer on the mixed stream
            this._analyser = this._audioContext.createAnalyser();
            this._analyser.fftSize = 2048;
            this._analyser.smoothingTimeConstant = 0.3;
            this._analyserBuffer = new Uint8Array(this._analyser.frequencyBinCount);

            // Connect destination to analyzer for level monitoring
            this._micGain.connect(this._analyser);
            if (this._tabGain) {
                this._tabGain.connect(this._analyser);
            }
        }

        // =========================================
        // Audio Analysis & Silence Detection
        // =========================================

        _startAudioAnalysis() {
            this._analyzerIntervalId = setInterval(() => {
                this._analyzeAudioLevel();
            }, this._analyzerInterval);
        }

        _stopAudioAnalysis() {
            if (this._analyzerIntervalId) {
                clearInterval(this._analyzerIntervalId);
                this._analyzerIntervalId = null;
            }
        }

        _analyzeAudioLevel() {
            if (!this._analyser || !this._analyserBuffer) return;

            this._analyser.getByteTimeDomainData(this._analyserBuffer);
            const rms = this._calculateRMS(this._analyserBuffer);

            if (this._onAudioLevel) {
                this._onAudioLevel(rms);
            }

            // Silence detection logic (mirrors MedocsVoiceService)
            const now = Date.now();

            if (rms < this._silenceThreshold) {
                if (!this._inSilence) {
                    this._inSilence = true;
                    this._silenceStartTime = now;
                }

                if (this._hasDetectedSpeech && this._silenceStartTime) {
                    const silenceDur = now - this._silenceStartTime;
                    if (silenceDur >= this._silenceDuration) {
                        const chunkDur = now - (this._currentChunkStartTime || now);
                        if (chunkDur >= this._minChunkDuration) {
                            this._createChunkAtSilence();
                        }
                    }
                }
            } else {
                this._inSilence = false;
                this._silenceStartTime = null;

                if (!this._hasDetectedSpeech) {
                    this._hasDetectedSpeech = true;
                    this._currentChunkStartTime = now;
                }
            }

            // Safety net: force a chunk if no silence detected for 30 seconds
            if (this._hasDetectedSpeech && this._currentChunkStartTime) {
                const elapsed = now - this._currentChunkStartTime;
                if (elapsed >= 30000) {
                    this._createChunkAtSilence();
                }
            }
        }

        _calculateRMS(buffer) {
            let sum = 0;
            for (let i = 0; i < buffer.length; i++) {
                const sample = (buffer[i] - 128) / 128;
                sum += sample * sample;
            }
            return Math.sqrt(sum / buffer.length);
        }

        // =========================================
        // Chunk Creation
        // =========================================

        _createChunkAtSilence() {
            const now = Date.now();
            const chunkDuration = now - (this._currentChunkStartTime || now);
            if (chunkDuration < this._minChunkDuration) return;

            const audioData = this._audioChunks.slice(this._currentChunkStartIndex);
            if (audioData.length === 0) return;

            const blobParts = this._currentChunkStartIndex > 0 && this._initSegment
                ? [this._initSegment, ...audioData]
                : audioData;

            const audioBlob = new Blob(blobParts, { type: this._mimeType });

            this._currentChunkStartIndex = this._audioChunks.length;
            this._currentChunkStartTime = now;
            this._hasDetectedSpeech = false;
            this._inSilence = false;
            this._silenceStartTime = null;

            this._chunkSequenceNumber++;
            const durationSec = Math.round(chunkDuration / 1000);

            if (this._onChunkReady) {
                this._onChunkReady(audioBlob, this._chunkSequenceNumber, durationSec);
            }
        }

        _finalizeCurrentChunk() {
            if (!this._audioChunks || this._audioChunks.length === 0) return;
            if (this._currentChunkStartIndex >= this._audioChunks.length) return;

            const now = Date.now();
            const chunkDuration = now - (this._currentChunkStartTime || now);
            if (chunkDuration < 1000) return; // Skip very short final chunks

            const audioData = this._audioChunks.slice(this._currentChunkStartIndex);
            if (audioData.length === 0) return;

            const blobParts = this._currentChunkStartIndex > 0 && this._initSegment
                ? [this._initSegment, ...audioData]
                : audioData;

            const audioBlob = new Blob(blobParts, { type: this._mimeType });

            this._chunkSequenceNumber++;
            const durationSec = Math.round(chunkDuration / 1000);

            if (this._onChunkReady) {
                this._onChunkReady(audioBlob, this._chunkSequenceNumber, durationSec);
            }
        }

        // =========================================
        // MediaRecorder Setup
        // =========================================

        _createMediaRecorder(stream) {
            const mimeTypes = [
                'audio/webm;codecs=opus',
                'audio/webm',
                'audio/ogg;codecs=opus',
                'audio/mp4'
            ];

            let selectedMime = '';
            for (const mime of mimeTypes) {
                if (MediaRecorder.isTypeSupported(mime)) {
                    selectedMime = mime;
                    break;
                }
            }

            this._mimeType = selectedMime || 'audio/webm';
            const recorder = new MediaRecorder(stream, {
                mimeType: this._mimeType,
                audioBitsPerSecond: 128000
            });

            recorder.ondataavailable = (e) => {
                if (e.data && e.data.size > 0) {
                    this._audioChunks.push(e.data);
                    if (!this._initSegment && this._audioChunks.length === 1) {
                        this._initSegment = e.data;
                    }
                }
            };

            recorder.onerror = (e) => {
                console.error('[TelehealthScribe] MediaRecorder error:', e);
                if (this._onError) {
                    this._onError({ message: 'RECORDER_ERROR' });
                }
            };

            return recorder;
        }

        // =========================================
        // Cleanup
        // =========================================

        _cleanup() {
            // Stop mic tracks
            if (this._micStream) {
                this._micStream.getTracks().forEach(t => t.stop());
                this._micStream = null;
            }

            // Stop tab audio tracks
            if (this._tabAudioStream) {
                this._tabAudioStream.getTracks().forEach(t => t.stop());
                this._tabAudioStream = null;
            }

            // Disconnect audio nodes
            if (this._micSource) { try { this._micSource.disconnect(); } catch (e) {} this._micSource = null; }
            if (this._tabSource) { try { this._tabSource.disconnect(); } catch (e) {} this._tabSource = null; }
            if (this._micGain) { try { this._micGain.disconnect(); } catch (e) {} this._micGain = null; }
            if (this._tabGain) { try { this._tabGain.disconnect(); } catch (e) {} this._tabGain = null; }
            if (this._analyser) { try { this._analyser.disconnect(); } catch (e) {} this._analyser = null; }
            if (this._destination) { this._destination = null; }

            // Close audio context
            if (this._audioContext && this._audioContext.state !== 'closed') {
                try { this._audioContext.close(); } catch (e) {}
            }
            this._audioContext = null;
            this._mixedStream = null;
            this._analyserBuffer = null;
        }

        // =========================================
        // Internal Helpers
        // =========================================

        _setStatus(newStatus) {
            const prev = this._status;
            this._status = newStatus;
            if (this._onStatusChange && prev !== newStatus) {
                this._onStatusChange(newStatus, prev);
            }
        }
    }

    TelehealthScribeService._activeInstance = null;
    window.TelehealthScribeService = TelehealthScribeService;

})();
