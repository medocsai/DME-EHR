/**
 * MedocsVoiceService.js
 *
 * Lightweight audio capture service for MEDOCS AI voice entry in Encounter Workspace.
 * Handles microphone access, audio recording, silence detection, and automatic chunking.
 *
 * Standalone from RecordingService.js — does not share state or dependencies.
 */
(function () {
    'use strict';

    const MedocsVoiceStatus = {
        IDLE: 'idle',
        RECORDING: 'recording',
        PAUSED: 'paused',
        STOPPED: 'stopped'
    };

    class MedocsVoiceService {
        constructor(options = {}) {
            // Silence detection config
            this._silenceThreshold = options.silenceThreshold || 0.01;
            this._silenceDuration = options.silenceDuration || 1200;
            this._minChunkDuration = options.minChunkDuration || 3000;
            this._maxChunkDuration = options.maxChunkDuration || 10000;
            this._analyzerInterval = options.analyzerInterval || 100;

            // Recording state
            this._mediaRecorder = null;
            this._mediaStream = null;
            this._audioChunks = [];
            this._initSegment = null;
            this._status = MedocsVoiceStatus.IDLE;
            this._mimeType = '';
            this._recordingStartTime = null;
            this._pausedDuration = 0;
            this._pauseStartTime = null;

            // Web Audio API
            this._audioContext = null;
            this._analyser = null;
            this._sourceNode = null;
            this._analyzerIntervalId = null;
            this._analyzerBuffer = null;

            // Silence detection state
            this._silenceStartTime = null;
            this._inSilence = false;
            this._hasDetectedSpeech = false;
            this._currentChunkStartTime = null;
            this._currentChunkStartIndex = 0;

            // Prolonged silence auto-pause (60s)
            this._prolongedSilenceThreshold = options.prolongedSilenceThreshold || 60000;
            this._continuousSilenceStart = null;
            this._onProlongedSilence = null;

            // Chunk management
            this._chunkSequenceNumber = 0;

            // Callbacks
            this._onChunkReady = null;
            this._onAudioLevel = null;
            this._onError = null;
            this._onStatusChange = null;
        }

        // =========================================
        // Static
        // =========================================

        static isSupported() {
            return !!(navigator.mediaDevices &&
                navigator.mediaDevices.getUserMedia &&
                typeof MediaRecorder !== 'undefined');
        }

        static getErrorMessage(error) {
            const msg = error?.message || error || '';
            switch (msg) {
                case 'PERMISSION_DENIED':
                    return 'Microphone access is required for voice entry. Please allow microphone access in your browser settings.';
                case 'NO_MICROPHONE':
                    return 'No microphone found. Please connect a microphone and try again.';
                case 'MICROPHONE_IN_USE':
                    return 'Microphone is being used by another application.';
                case 'NOT_SUPPORTED':
                    return 'Audio recording is not supported in this browser. Please use Chrome, Firefox, or Edge.';
                default:
                    return 'An error occurred with the microphone. Please try again.';
            }
        }

        // =========================================
        // Public API
        // =========================================

        async startRecording() {
            if (!MedocsVoiceService.isSupported()) {
                throw new Error('NOT_SUPPORTED');
            }

            if (this._status === MedocsVoiceStatus.RECORDING) {
                return;
            }

            // Stop any other active instance
            if (MedocsVoiceService._activeInstance && MedocsVoiceService._activeInstance !== this) {
                MedocsVoiceService._activeInstance.stopRecording();
            }

            try {
                this._mediaStream = await this._requestMicrophoneAccess();
                this._setupAudioAnalyzer();
                this._mediaRecorder = this._createMediaRecorder(this._mediaStream);

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
                this._continuousSilenceStart = null;

                this._mediaRecorder.start(1000);
                this._recordingStartTime = Date.now();
                this._setStatus(MedocsVoiceStatus.RECORDING);
                this._startAudioAnalysis();

                MedocsVoiceService._activeInstance = this;
            } catch (error) {
                this._cleanup();
                throw error;
            }
        }

        pauseRecording() {
            if (this._mediaRecorder && this._status === MedocsVoiceStatus.RECORDING) {
                this._mediaRecorder.pause();
                this._pauseStartTime = Date.now();
                this._setStatus(MedocsVoiceStatus.PAUSED);
                this._stopAudioAnalysis();
            }
        }

        resumeRecording() {
            if (this._mediaRecorder && this._status === MedocsVoiceStatus.PAUSED) {
                if (this._pauseStartTime) {
                    this._pausedDuration += Date.now() - this._pauseStartTime;
                    this._pauseStartTime = null;
                }
                this._mediaRecorder.resume();
                this._setStatus(MedocsVoiceStatus.RECORDING);
                this._startAudioAnalysis();
            }
        }

        stopRecording() {
            this._stopAudioAnalysis();
            this._finalizeCurrentChunk();

            if (this._mediaRecorder) {
                if (this._status === MedocsVoiceStatus.PAUSED && this._pauseStartTime) {
                    this._pausedDuration += Date.now() - this._pauseStartTime;
                }
                if (this._mediaRecorder.state !== 'inactive') {
                    this._mediaRecorder.stop();
                }
            }

            this._cleanupAudioAnalyzer();
            this._stopMediaTracks();
            this._setStatus(MedocsVoiceStatus.STOPPED);

            if (MedocsVoiceService._activeInstance === this) {
                MedocsVoiceService._activeInstance = null;
            }
        }

        getStatus() { return this._status; }

        getElapsedSeconds() {
            if (!this._recordingStartTime) return 0;
            let elapsed = Date.now() - this._recordingStartTime - this._pausedDuration;
            if (this._status === MedocsVoiceStatus.PAUSED && this._pauseStartTime) {
                elapsed -= (Date.now() - this._pauseStartTime);
            }
            return Math.max(0, Math.floor(elapsed / 1000));
        }

        onChunkReady(callback) { this._onChunkReady = callback; }
        onAudioLevel(callback) { this._onAudioLevel = callback; }
        onError(callback) { this._onError = callback; }
        onStatusChange(callback) { this._onStatusChange = callback; }
        onProlongedSilence(callback) { this._onProlongedSilence = callback; }

        destroy() {
            this._stopAudioAnalysis();
            this._cleanupAudioAnalyzer();
            if (this._mediaRecorder && this._mediaRecorder.state !== 'inactive') {
                try { this._mediaRecorder.stop(); } catch (e) { /* ignore */ }
            }
            this._mediaRecorder = null;
            this._stopMediaTracks();
            this._audioChunks = [];
            this._initSegment = null;
            this._chunkSequenceNumber = 0;
            this._setStatus(MedocsVoiceStatus.IDLE);

            if (MedocsVoiceService._activeInstance === this) {
                MedocsVoiceService._activeInstance = null;
            }

            this._onChunkReady = null;
            this._onAudioLevel = null;
            this._onError = null;
            this._onStatusChange = null;
        }

        // =========================================
        // Audio Analyzer
        // =========================================

        _setupAudioAnalyzer() {
            try {
                this._audioContext = new (window.AudioContext || window.webkitAudioContext)();
                this._analyser = this._audioContext.createAnalyser();
                this._analyser.fftSize = 2048;
                this._analyser.smoothingTimeConstant = 0.3;
                this._analyzerBuffer = new Uint8Array(this._analyser.fftSize);
                this._sourceNode = this._audioContext.createMediaStreamSource(this._mediaStream);
                this._sourceNode.connect(this._analyser);
            } catch (error) {
                console.error('[MedocsVoice] Audio analyzer setup error:', error);
            }
        }

        _cleanupAudioAnalyzer() {
            if (this._sourceNode) {
                try { this._sourceNode.disconnect(); } catch (e) { /* ignore */ }
                this._sourceNode = null;
            }
            if (this._audioContext) {
                try { this._audioContext.close(); } catch (e) { /* ignore */ }
                this._audioContext = null;
            }
            this._analyser = null;
            this._analyzerBuffer = null;
        }

        _startAudioAnalysis() {
            if (!this._analyser) return;
            this._analyzerIntervalId = setInterval(() => this._analyzeAudioLevel(), this._analyzerInterval);
        }

        _stopAudioAnalysis() {
            if (this._analyzerIntervalId) {
                clearInterval(this._analyzerIntervalId);
                this._analyzerIntervalId = null;
            }
        }

        _analyzeAudioLevel() {
            if (!this._analyser || !this._analyzerBuffer) return;
            if (this._status !== MedocsVoiceStatus.RECORDING) return;

            this._analyser.getByteTimeDomainData(this._analyzerBuffer);
            const rms = this._calculateRMS(this._analyzerBuffer);

            if (this._onAudioLevel) {
                this._onAudioLevel(rms);
            }

            const isSilent = rms < this._silenceThreshold;
            const now = Date.now();

            if (isSilent) {
                if (!this._inSilence) {
                    this._inSilence = true;
                    this._silenceStartTime = now;
                } else if (this._hasDetectedSpeech && this._silenceStartTime) {
                    if (now - this._silenceStartTime >= this._silenceDuration) {
                        this._createChunkAtSilence();
                    }
                }
            } else {
                if (this._inSilence) {
                    this._inSilence = false;
                    this._silenceStartTime = null;
                }
                if (!this._hasDetectedSpeech) {
                    this._hasDetectedSpeech = true;
                    this._currentChunkStartTime = now;
                    this._currentChunkStartIndex = this._audioChunks.length;
                }
            }

            // Max chunk duration: cut and send even during continuous speech
            if (this._hasDetectedSpeech && this._currentChunkStartTime) {
                const chunkElapsed = now - this._currentChunkStartTime;
                if (chunkElapsed >= this._maxChunkDuration) {
                    this._createChunkAtSilence();
                }
            }

            // Prolonged silence auto-pause: if no audio for 60s, notify UI to pause
            if (isSilent) {
                if (!this._continuousSilenceStart) {
                    this._continuousSilenceStart = now;
                } else if (now - this._continuousSilenceStart >= this._prolongedSilenceThreshold) {
                    this._continuousSilenceStart = null;
                    if (this._onProlongedSilence) {
                        this._onProlongedSilence();
                    }
                }
            } else {
                this._continuousSilenceStart = null;
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
            const chunkDuration = now - this._currentChunkStartTime;

            if (chunkDuration < this._minChunkDuration) return;

            const audioData = this._audioChunks.slice(this._currentChunkStartIndex);
            if (audioData.length === 0) return;

            let blobParts;
            if (this._currentChunkStartIndex > 0 && this._initSegment) {
                blobParts = [this._initSegment, ...audioData];
            } else {
                blobParts = audioData;
            }
            const audioBlob = new Blob(blobParts, { type: this._mimeType || 'audio/webm' });

            this._chunkSequenceNumber++;
            const durationSec = Math.round(chunkDuration / 1000);

            if (this._onChunkReady) {
                this._onChunkReady(audioBlob, this._chunkSequenceNumber, durationSec);
            }

            // Reset for next chunk
            this._currentChunkStartTime = now;
            this._currentChunkStartIndex = this._audioChunks.length;
            this._silenceStartTime = null;
            this._hasDetectedSpeech = false;
        }

        _finalizeCurrentChunk() {
            if (!this._hasDetectedSpeech) return;

            const now = Date.now();
            const audioData = this._audioChunks.slice(this._currentChunkStartIndex);
            if (audioData.length === 0) return;

            const chunkDuration = now - this._currentChunkStartTime;

            let blobParts;
            if (this._currentChunkStartIndex > 0 && this._initSegment) {
                blobParts = [this._initSegment, ...audioData];
            } else {
                blobParts = audioData;
            }
            const audioBlob = new Blob(blobParts, { type: this._mimeType || 'audio/webm' });

            this._chunkSequenceNumber++;
            const durationSec = Math.max(1, Math.round(chunkDuration / 1000));

            if (this._onChunkReady) {
                this._onChunkReady(audioBlob, this._chunkSequenceNumber, durationSec);
            }
        }

        // =========================================
        // Private Helpers
        // =========================================

        _setStatus(status) {
            const prev = this._status;
            this._status = status;
            if (this._onStatusChange && prev !== status) {
                this._onStatusChange(status, prev);
            }
        }

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
            } catch (error) {
                if (error.name === 'NotAllowedError' || error.name === 'PermissionDeniedError') {
                    throw new Error('PERMISSION_DENIED');
                } else if (error.name === 'NotFoundError' || error.name === 'DevicesNotFoundError') {
                    throw new Error('NO_MICROPHONE');
                } else if (error.name === 'NotReadableError' || error.name === 'TrackStartError') {
                    throw new Error('MICROPHONE_IN_USE');
                }
                throw new Error('UNKNOWN_ERROR');
            }
        }

        _getSupportedMimeType() {
            const types = [
                'audio/webm;codecs=opus',
                'audio/webm',
                'audio/ogg;codecs=opus',
                'audio/ogg',
                'audio/mp4',
                'audio/mpeg'
            ];
            for (const t of types) {
                if (MediaRecorder.isTypeSupported(t)) return t;
            }
            return '';
        }

        _createMediaRecorder(stream) {
            this._mimeType = this._getSupportedMimeType();

            const options = { audioBitsPerSecond: 128000 };
            if (this._mimeType) options.mimeType = this._mimeType;

            const recorder = new MediaRecorder(stream, options);

            recorder.ondataavailable = (event) => {
                if (event.data && event.data.size > 0) {
                    if (this._initSegment === null) {
                        this._initSegment = event.data;
                    }
                    this._audioChunks.push(event.data);
                }
            };

            recorder.onerror = (event) => {
                console.error('[MedocsVoice] MediaRecorder error:', event.error);
                if (this._onError) {
                    this._onError(new Error('RECORDER_ERROR'));
                }
            };

            return recorder;
        }

        _stopMediaTracks() {
            if (this._mediaStream) {
                this._mediaStream.getTracks().forEach(track => track.stop());
                this._mediaStream = null;
            }
        }

        _cleanup() {
            if (this._mediaRecorder) {
                if (this._mediaRecorder.state !== 'inactive') {
                    try { this._mediaRecorder.stop(); } catch (e) { /* ignore */ }
                }
                this._mediaRecorder = null;
            }
            this._stopMediaTracks();
        }
    }

    // Static: only one instance can record at a time
    MedocsVoiceService._activeInstance = null;

    // Expose globally
    window.MedocsVoiceService = MedocsVoiceService;
    window.MedocsVoiceStatus = MedocsVoiceStatus;
})();
