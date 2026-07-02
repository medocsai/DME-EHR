/**
 * RecordingService.js
 *
 * A dedicated service for handling audio recording functionality.
 * This service manages microphone access, audio capture, silence detection,
 * and automatic audio chunking based on speech pauses.
 *
 * Usage:
 *   const recorder = new RecordingService();
 *   recorder.onChunkCreated((chunk) => console.log('New chunk:', chunk));
 *   await recorder.startRecording();
 *   recorder.pauseRecording();
 *   recorder.resumeRecording();
 *   const audioData = recorder.stopRecording();
 */

// Recording status constants
const RecordingStatus = {
    IDLE: 'idle',
    RECORDING: 'recording',
    PAUSED: 'paused',
    STOPPED: 'stopped'
};

// Audio error codes
const AudioErrorCode = {
    PERMISSION_DENIED: 'PERMISSION_DENIED',
    NO_MICROPHONE: 'NO_MICROPHONE',
    MICROPHONE_IN_USE: 'MICROPHONE_IN_USE',
    CONSTRAINTS_ERROR: 'CONSTRAINTS_ERROR',
    NOT_SUPPORTED: 'NOT_SUPPORTED',
    RECORDER_ERROR: 'RECORDER_ERROR',
    UNKNOWN_ERROR: 'UNKNOWN_ERROR'
};

// Chunk status constants
const ChunkStatus = {
    RECORDING: 'Recording',
    READY: 'Ready',
    SENDING: 'Sending',
    SENT: 'Sent',
    TRANSCRIBED: 'Transcribed',
    ERROR: 'Error'
};

/**
 * RecordingService class
 * Handles all audio recording operations including microphone access,
 * audio capture, pause/resume, silence detection, and audio chunking.
 */
class RecordingService {
    constructor(options = {}) {
        // =========================================
        // Silence Detection Configuration
        // =========================================

        // RMS threshold below which audio is considered silence (0-1 scale)
        // Lower = more sensitive to quiet sounds, Higher = needs louder sound
        this._silenceThreshold = options.silenceThreshold || 0.01;

        // Duration in milliseconds that audio must be silent to trigger chunk creation
        this._silenceDuration = options.silenceDuration || 1500; // 1.5 seconds

        // Minimum chunk duration in milliseconds (don't create tiny chunks)
        this._minChunkDuration = options.minChunkDuration || 2000; // 2 seconds

        // How often to check audio levels (in milliseconds)
        this._analyzerInterval = options.analyzerInterval || 100; // 100ms

        // =========================================
        // Recording State
        // =========================================

        // MediaRecorder instance
        this._mediaRecorder = null;

        // MediaStream from microphone
        this._mediaStream = null;

        // Array of raw audio data from MediaRecorder
        this._audioChunks = [];

        // WebM initialization segment (first chunk with EBML headers) - required for valid files
        this._initSegment = null;

        // Final audio blob after recording stops
        this._audioBlob = null;

        // Current recording status
        this._status = RecordingStatus.IDLE;

        // MIME type being used for recording
        this._mimeType = '';

        // Recording start time (for duration tracking)
        this._recordingStartTime = null;

        // Total paused duration in milliseconds
        this._pausedDuration = 0;

        // Time when pause started
        this._pauseStartTime = null;

        // =========================================
        // Web Audio API for Silence Detection
        // =========================================

        // AudioContext for analyzing audio levels
        this._audioContext = null;

        // AnalyserNode for getting audio data
        this._analyser = null;

        // Source node connecting stream to analyzer
        this._sourceNode = null;

        // Interval ID for audio level checking
        this._analyzerIntervalId = null;

        // Buffer for analyzer data
        this._analyzerBuffer = null;

        // =========================================
        // Silence Detection State
        // =========================================

        // Timestamp when silence was first detected
        this._silenceStartTime = null;

        // Whether we're currently in a silence period
        this._inSilence = false;

        // Whether we've detected speech since recording started
        this._hasDetectedSpeech = false;

        // Timestamp when current chunk started
        this._currentChunkStartTime = null;

        // Index of first audio chunk for current speech segment
        this._currentChunkStartIndex = 0;

        // =========================================
        // Chunk Management
        // =========================================

        // Array of completed speech chunks with metadata
        this._speechChunks = [];

        // Current chunk sequence number
        this._chunkSequenceNumber = 0;

        // =========================================
        // Callbacks
        // =========================================

        // Callback for recording errors
        this._onError = null;

        // Callback for when raw audio data is available
        this._onDataAvailable = null;

        // Callback for when a new speech chunk is created
        this._onChunkCreated = null;

        // Callback for audio level updates (for visualizations)
        this._onAudioLevel = null;
    }

    // =========================================
    // Public API
    // =========================================

    /**
     * Check if audio recording is supported in this browser
     * @returns {boolean} True if recording is supported
     */
    static isSupported() {
        if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
            console.warn('RecordingService: getUserMedia is not supported');
            return false;
        }
        if (typeof MediaRecorder === 'undefined') {
            console.warn('RecordingService: MediaRecorder is not supported');
            return false;
        }
        return true;
    }

    /**
     * Get user-friendly error message for an audio error code
     * @param {string} errorCode - The error code
     * @returns {string} User-friendly error message
     */
    static getErrorMessage(errorCode) {
        switch (errorCode) {
            case AudioErrorCode.PERMISSION_DENIED:
                return 'Microphone access is required to record a session. Please allow microphone access and try again.';
            case AudioErrorCode.NO_MICROPHONE:
                return 'No microphone found. Please connect a microphone and try again.';
            case AudioErrorCode.MICROPHONE_IN_USE:
                return 'Microphone is being used by another application. Please close other apps using the microphone and try again.';
            case AudioErrorCode.CONSTRAINTS_ERROR:
                return 'Your microphone does not support the required audio settings. Please try a different microphone.';
            case AudioErrorCode.NOT_SUPPORTED:
                return 'Audio recording is not supported in this browser. Please use a modern browser like Chrome, Firefox, or Edge.';
            case AudioErrorCode.RECORDER_ERROR:
                return 'An error occurred while recording. Please try again.';
            default:
                return 'An unexpected error occurred with the microphone. Please try again.';
        }
    }

    /**
     * Start recording audio from the microphone
     * @returns {Promise<void>} Resolves when recording has started
     * @throws {Error} If microphone access is denied or unavailable
     */
    async startRecording() {
        // Check browser support
        if (!RecordingService.isSupported()) {
            throw new Error(AudioErrorCode.NOT_SUPPORTED);
        }

        // Don't start if already recording
        if (this._status === RecordingStatus.RECORDING) {
            console.warn('RecordingService: Already recording');
            return;
        }

        try {
            // Request microphone access
            this._mediaStream = await this._requestMicrophoneAccess();

            // Set up Web Audio API for silence detection
            this._setupAudioAnalyzer();

            // Initialize the MediaRecorder
            this._mediaRecorder = this._createMediaRecorder(this._mediaStream);

            // Clear previous recording data
            this._audioChunks = [];
            this._initSegment = null;
            this._audioBlob = null;
            this._pausedDuration = 0;
            this._pauseStartTime = null;

            // Reset silence detection state
            this._silenceStartTime = null;
            this._inSilence = false;
            this._hasDetectedSpeech = false;
            this._currentChunkStartTime = Date.now();
            this._currentChunkStartIndex = 0;

            // Reset chunk management
            this._speechChunks = [];
            this._chunkSequenceNumber = 0;

            // Start recording with 1-second chunks for reliable capture
            this._mediaRecorder.start(1000);
            this._recordingStartTime = Date.now();
            this._status = RecordingStatus.RECORDING;

            // Start silence detection
            this._startAudioAnalysis();
        } catch (error) {
            console.error('RecordingService: Failed to start recording', error);
            this._cleanup();
            throw error;
        }
    }

    /**
     * Pause the current recording
     */
    pauseRecording() {
        if (this._mediaRecorder && this._status === RecordingStatus.RECORDING) {
            this._mediaRecorder.pause();
            this._pauseStartTime = Date.now();
            this._status = RecordingStatus.PAUSED;

            // Pause silence detection
            this._stopAudioAnalysis();
        }
    }

    /**
     * Resume a paused recording
     */
    resumeRecording() {
        if (this._mediaRecorder && this._status === RecordingStatus.PAUSED) {
            // Track paused duration
            if (this._pauseStartTime) {
                this._pausedDuration += Date.now() - this._pauseStartTime;
                this._pauseStartTime = null;
            }

            this._mediaRecorder.resume();
            this._status = RecordingStatus.RECORDING;

            // Resume silence detection
            this._startAudioAnalysis();
        }
    }

    /**
     * Stop recording and get the audio data
     * @returns {Object} Object containing audioBlob, mimeType, duration, size, and chunks
     */
    stopRecording() {
        // Stop silence detection first
        this._stopAudioAnalysis();

        // Finalize any remaining audio as a chunk
        this._finalizeCurrentChunk();

        const result = {
            audioBlob: null,
            mimeType: this._mimeType,
            duration: this.getAudioDuration(),
            size: this.getAudioSize(),
            chunks: this._speechChunks,
            chunkCount: this._speechChunks.length
        };

        if (this._mediaRecorder) {
            // If paused, account for final pause duration
            if (this._status === RecordingStatus.PAUSED && this._pauseStartTime) {
                this._pausedDuration += Date.now() - this._pauseStartTime;
            }

            if (this._mediaRecorder.state !== 'inactive') {
                this._mediaRecorder.stop();
            }

            // Create the final blob synchronously from current chunks
            if (this._audioChunks.length > 0) {
                this._audioBlob = new Blob(this._audioChunks, {
                    type: this._mimeType || 'audio/webm'
                });
                result.audioBlob = this._audioBlob;
                result.size = this._audioBlob.size;
            }
        }

        // Clean up audio context
        this._cleanupAudioAnalyzer();

        // Stop all media tracks
        this._stopMediaTracks();

        this._status = RecordingStatus.STOPPED;

        return result;
    }

    /**
     * Get the current recording status
     * @returns {string} Current status (idle, recording, paused, stopped)
     */
    getStatus() {
        return this._status;
    }

    /**
     * Check if currently recording (includes paused state)
     * @returns {boolean} True if recording is active or paused
     */
    isRecording() {
        return this._status === RecordingStatus.RECORDING ||
               this._status === RecordingStatus.PAUSED;
    }

    /**
     * Get the audio duration in seconds (excluding paused time)
     * @returns {number} Duration in seconds
     */
    getAudioDuration() {
        if (!this._recordingStartTime) return 0;

        let elapsed = Date.now() - this._recordingStartTime - this._pausedDuration;

        // If currently paused, subtract the current pause duration
        if (this._status === RecordingStatus.PAUSED && this._pauseStartTime) {
            elapsed -= (Date.now() - this._pauseStartTime);
        }

        return Math.max(0, Math.floor(elapsed / 1000));
    }

    /**
     * Get the current audio data size in bytes
     * @returns {number} Size in bytes
     */
    getAudioSize() {
        if (this._audioChunks.length === 0) return 0;
        return this._audioChunks.reduce((total, chunk) => total + chunk.size, 0);
    }

    /**
     * Get the recorded audio blob (available after stopRecording)
     * @returns {Blob|null} The audio blob or null if not available
     */
    getAudioBlob() {
        return this._audioBlob;
    }

    /**
     * Get the MIME type of the recording
     * @returns {string} The MIME type
     */
    getMimeType() {
        return this._mimeType;
    }

    /**
     * Get all speech chunks
     * @returns {Array} Array of chunk objects
     */
    getChunks() {
        return this._speechChunks;
    }

    /**
     * Get the number of chunks created
     * @returns {number} Number of chunks
     */
    getChunkCount() {
        return this._speechChunks.length;
    }

    /**
     * Get a specific chunk by sequence number
     * @param {number} sequenceNumber - The chunk sequence number
     * @returns {Object|null} The chunk or null if not found
     */
    getChunk(sequenceNumber) {
        return this._speechChunks.find(c => c.sequenceNumber === sequenceNumber) || null;
    }

    /**
     * Update chunk status
     * @param {number} sequenceNumber - The chunk sequence number
     * @param {string} status - New status from ChunkStatus
     */
    updateChunkStatus(sequenceNumber, status) {
        const chunk = this.getChunk(sequenceNumber);
        if (chunk) {
            chunk.status = status;
        }
    }

    /**
     * Set the starting sequence number for chunks.
     * Used when resuming a recording session to continue numbering from where it left off.
     * @param {number} startingSequence - The sequence number to start from (next chunk will be this number)
     */
    setStartingSequenceNumber(startingSequence) {
        if (startingSequence >= 0) {
            // Set to startingSequence - 1 because it will be incremented when first chunk is created
            this._chunkSequenceNumber = startingSequence - 1;
        }
    }

    /**
     * Get the current chunk sequence number (the next chunk will be this + 1)
     * @returns {number} Current sequence number
     */
    getCurrentSequenceNumber() {
        return this._chunkSequenceNumber;
    }

    /**
     * Get silence detection settings
     * @returns {Object} Current settings
     */
    getSilenceSettings() {
        return {
            silenceThreshold: this._silenceThreshold,
            silenceDuration: this._silenceDuration,
            minChunkDuration: this._minChunkDuration,
            analyzerInterval: this._analyzerInterval
        };
    }

    /**
     * Update silence detection settings (only when not recording)
     * @param {Object} settings - New settings
     */
    updateSilenceSettings(settings) {
        if (this._status !== RecordingStatus.IDLE) {
            console.warn('RecordingService: Cannot update settings while recording');
            return;
        }

        if (settings.silenceThreshold !== undefined) {
            this._silenceThreshold = settings.silenceThreshold;
        }
        if (settings.silenceDuration !== undefined) {
            this._silenceDuration = settings.silenceDuration;
        }
        if (settings.minChunkDuration !== undefined) {
            this._minChunkDuration = settings.minChunkDuration;
        }
        if (settings.analyzerInterval !== undefined) {
            this._analyzerInterval = settings.analyzerInterval;
        }
    }

    /**
     * Set callback for recording errors
     * @param {Function} callback - Error callback function
     */
    onError(callback) {
        this._onError = callback;
    }

    /**
     * Set callback for when raw audio data is available
     * @param {Function} callback - Data callback function
     */
    onDataAvailable(callback) {
        this._onDataAvailable = callback;
    }

    /**
     * Set callback for when a new speech chunk is created
     * @param {Function} callback - Chunk callback function (receives chunk object)
     */
    onChunkCreated(callback) {
        this._onChunkCreated = callback;
    }

    /**
     * Set callback for audio level updates
     * @param {Function} callback - Level callback function (receives RMS value 0-1)
     */
    onAudioLevel(callback) {
        this._onAudioLevel = callback;
    }

    /**
     * Clean up all resources and reset state
     */
    cleanup() {
        this._stopAudioAnalysis();
        this._cleanupAudioAnalyzer();
        this._cleanup();
        this._audioChunks = [];
        this._initSegment = null;
        this._audioBlob = null;
        this._recordingStartTime = null;
        this._pausedDuration = 0;
        this._pauseStartTime = null;
        this._speechChunks = [];
        this._chunkSequenceNumber = 0;
        this._silenceStartTime = null;
        this._inSilence = false;
        this._hasDetectedSpeech = false;
        this._status = RecordingStatus.IDLE;
    }

    // =========================================
    // Silence Detection Methods
    // =========================================

    /**
     * Set up the Web Audio API analyzer for silence detection
     * @private
     */
    _setupAudioAnalyzer() {
        try {
            // Create audio context
            this._audioContext = new (window.AudioContext || window.webkitAudioContext)();

            // Create analyzer node
            this._analyser = this._audioContext.createAnalyser();
            this._analyser.fftSize = 2048;
            this._analyser.smoothingTimeConstant = 0.3;

            // Create buffer for time domain data
            this._analyzerBuffer = new Uint8Array(this._analyser.fftSize);

            // Connect media stream to analyzer
            this._sourceNode = this._audioContext.createMediaStreamSource(this._mediaStream);
            this._sourceNode.connect(this._analyser);
        } catch (error) {
            console.error('RecordingService: Error setting up audio analyzer', error);
            // Continue without silence detection if it fails
        }
    }

    /**
     * Clean up the audio analyzer
     * @private
     */
    _cleanupAudioAnalyzer() {
        if (this._sourceNode) {
            try {
                this._sourceNode.disconnect();
            } catch (e) {}
            this._sourceNode = null;
        }

        if (this._audioContext) {
            try {
                this._audioContext.close();
            } catch (e) {}
            this._audioContext = null;
        }

        this._analyser = null;
        this._analyzerBuffer = null;
    }

    /**
     * Start the audio level analysis loop
     * @private
     */
    _startAudioAnalysis() {
        if (!this._analyser) return;

        this._analyzerIntervalId = setInterval(() => {
            this._analyzeAudioLevel();
        }, this._analyzerInterval);
    }

    /**
     * Stop the audio level analysis loop
     * @private
     */
    _stopAudioAnalysis() {
        if (this._analyzerIntervalId) {
            clearInterval(this._analyzerIntervalId);
            this._analyzerIntervalId = null;
        }
    }

    /**
     * Analyze current audio level and detect silence
     * @private
     */
    _analyzeAudioLevel() {
        if (!this._analyser || !this._analyzerBuffer) return;
        if (this._status !== RecordingStatus.RECORDING) return;

        // Get time domain data
        this._analyser.getByteTimeDomainData(this._analyzerBuffer);

        // Calculate RMS (Root Mean Square) for audio level
        const rms = this._calculateRMS(this._analyzerBuffer);

        // Notify listeners of audio level
        if (this._onAudioLevel) {
            this._onAudioLevel(rms);
        }

        // Check for silence
        const isSilent = rms < this._silenceThreshold;
        const now = Date.now();

        if (isSilent) {
            if (!this._inSilence) {
                // Just entered silence
                this._inSilence = true;
                this._silenceStartTime = now;
            } else if (this._hasDetectedSpeech && this._silenceStartTime) {
                // Check if silence duration threshold reached
                const silenceDuration = now - this._silenceStartTime;
                if (silenceDuration >= this._silenceDuration) {
                    // Create chunk from audio before silence
                    this._createChunkAtSilence();
                }
            }
        } else {
            // Sound detected
            if (this._inSilence) {
                this._inSilence = false;
                this._silenceStartTime = null;
            }

            // Mark that we've detected speech
            if (!this._hasDetectedSpeech) {
                this._hasDetectedSpeech = true;
                this._currentChunkStartTime = now;
                this._currentChunkStartIndex = this._audioChunks.length;
            }
        }
    }

    /**
     * Calculate RMS (Root Mean Square) of audio data
     * @private
     * @param {Uint8Array} buffer - Audio data buffer
     * @returns {number} RMS value between 0 and 1
     */
    _calculateRMS(buffer) {
        let sum = 0;
        for (let i = 0; i < buffer.length; i++) {
            // Convert from 0-255 to -1 to 1
            const sample = (buffer[i] - 128) / 128;
            sum += sample * sample;
        }
        return Math.sqrt(sum / buffer.length);
    }

    /**
     * Create a chunk from audio recorded since last chunk
     * @private
     */
    _createChunkAtSilence() {
        const now = Date.now();
        const chunkDuration = now - this._currentChunkStartTime;

        // Don't create chunk if it's too short
        if (chunkDuration < this._minChunkDuration) {
            return;
        }

        // Get audio chunks for this speech segment
        const audioData = this._audioChunks.slice(this._currentChunkStartIndex);

        if (audioData.length === 0) {
            return;
        }

        // Create the audio blob for this chunk
        // IMPORTANT: WebM requires the initialization segment (EBML headers) at the start
        // Without it, the file is invalid and cannot be played/processed
        let blobParts;
        if (this._currentChunkStartIndex > 0 && this._initSegment) {
            // For subsequent chunks, prepend the init segment
            blobParts = [this._initSegment, ...audioData];
        } else {
            // First chunk already includes the init segment
            blobParts = audioData;
        }
        const audioBlob = new Blob(blobParts, { type: this._mimeType || 'audio/webm' });

        // Create chunk metadata
        this._chunkSequenceNumber++;
        const chunk = {
            sequenceNumber: this._chunkSequenceNumber,
            audioBlob: audioBlob,
            audioData: audioData, // Keep raw chunks for potential re-processing
            duration: Math.round(chunkDuration / 1000), // Duration in seconds
            durationMs: chunkDuration,
            timestamp: new Date().toISOString(),
            recordedAt: now,
            size: audioBlob.size,
            status: ChunkStatus.READY,
            mimeType: this._mimeType || 'audio/webm'
        };

        // Add to chunks array
        this._speechChunks.push(chunk);

        // Notify listeners
        if (this._onChunkCreated) {
            this._onChunkCreated(chunk);
        }

        // Reset for next chunk
        this._currentChunkStartTime = now;
        this._currentChunkStartIndex = this._audioChunks.length;
        this._silenceStartTime = null;
        this._hasDetectedSpeech = false;
    }

    /**
     * Finalize any remaining audio as a chunk when recording stops
     * @private
     */
    _finalizeCurrentChunk() {
        // Only create final chunk if we have recorded speech
        if (!this._hasDetectedSpeech) {
            return;
        }

        const now = Date.now();
        const chunkDuration = now - this._currentChunkStartTime;

        // Get remaining audio chunks
        const audioData = this._audioChunks.slice(this._currentChunkStartIndex);

        if (audioData.length === 0) {
            return;
        }

        // Create the audio blob for this chunk
        // IMPORTANT: WebM requires the initialization segment (EBML headers) at the start
        let blobParts;
        if (this._currentChunkStartIndex > 0 && this._initSegment) {
            // For subsequent chunks, prepend the init segment
            blobParts = [this._initSegment, ...audioData];
        } else {
            // First chunk already includes the init segment
            blobParts = audioData;
        }
        const audioBlob = new Blob(blobParts, { type: this._mimeType || 'audio/webm' });

        // Create chunk metadata
        this._chunkSequenceNumber++;
        const chunk = {
            sequenceNumber: this._chunkSequenceNumber,
            audioBlob: audioBlob,
            audioData: audioData,
            duration: Math.round(chunkDuration / 1000),
            durationMs: chunkDuration,
            timestamp: new Date().toISOString(),
            recordedAt: now,
            size: audioBlob.size,
            status: ChunkStatus.READY,
            mimeType: this._mimeType || 'audio/webm',
            isFinal: true // Mark as the final chunk
        };

        // Add to chunks array
        this._speechChunks.push(chunk);

        // Notify listeners
        if (this._onChunkCreated) {
            this._onChunkCreated(chunk);
        }
    }

    // =========================================
    // Private Methods
    // =========================================

    /**
     * Request microphone access from the user
     * @private
     * @returns {Promise<MediaStream>} The audio stream
     */
    async _requestMicrophoneAccess() {
        try {
            const stream = await navigator.mediaDevices.getUserMedia({
                audio: {
                    echoCancellation: true,
                    noiseSuppression: true,
                    autoGainControl: true,
                    sampleRate: 44100
                }
            });
            return stream;
        } catch (error) {
            throw this._mapMediaError(error);
        }
    }

    /**
     * Map browser media errors to our error codes
     * @private
     * @param {Error} error - The browser error
     * @returns {Error} Error with our error code
     */
    _mapMediaError(error) {
        if (error.name === 'NotAllowedError' || error.name === 'PermissionDeniedError') {
            return new Error(AudioErrorCode.PERMISSION_DENIED);
        } else if (error.name === 'NotFoundError' || error.name === 'DevicesNotFoundError') {
            return new Error(AudioErrorCode.NO_MICROPHONE);
        } else if (error.name === 'NotReadableError' || error.name === 'TrackStartError') {
            return new Error(AudioErrorCode.MICROPHONE_IN_USE);
        } else if (error.name === 'OverconstrainedError') {
            return new Error(AudioErrorCode.CONSTRAINTS_ERROR);
        }
        return new Error(AudioErrorCode.UNKNOWN_ERROR);
    }

    /**
     * Get the best supported audio MIME type
     * @private
     * @returns {string} Supported MIME type
     */
    _getSupportedMimeType() {
        const mimeTypes = [
            'audio/webm;codecs=opus',
            'audio/webm',
            'audio/ogg;codecs=opus',
            'audio/ogg',
            'audio/mp4',
            'audio/mpeg'
        ];

        for (const mimeType of mimeTypes) {
            if (MediaRecorder.isTypeSupported(mimeType)) {
                return mimeType;
            }
        }

        return '';
    }

    /**
     * Create and configure the MediaRecorder
     * @private
     * @param {MediaStream} stream - The audio stream
     * @returns {MediaRecorder} Configured MediaRecorder
     */
    _createMediaRecorder(stream) {
        this._mimeType = this._getSupportedMimeType();

        const options = {
            audioBitsPerSecond: 128000
        };

        if (this._mimeType) {
            options.mimeType = this._mimeType;
        }

        try {
            const recorder = new MediaRecorder(stream, options);

            // Handle data available
            recorder.ondataavailable = (event) => {
                if (event.data && event.data.size > 0) {
                    // Store first chunk as initialization segment (contains EBML headers)
                    if (this._initSegment === null) {
                        this._initSegment = event.data;
                    }

                    this._audioChunks.push(event.data);

                    if (this._onDataAvailable) {
                        this._onDataAvailable(event.data, this.getAudioSize());
                    }
                }
            };

            // Handle errors
            recorder.onerror = (event) => {
                console.error('RecordingService: MediaRecorder error', event.error);
                if (this._onError) {
                    this._onError(new Error(AudioErrorCode.RECORDER_ERROR));
                }
            };

            // Handle stop
            recorder.onstop = () => {
                if (this._audioChunks.length > 0) {
                    this._audioBlob = new Blob(this._audioChunks, {
                        type: this._mimeType || 'audio/webm'
                    });
                }
            };

            return recorder;
        } catch (error) {
            console.error('RecordingService: Error creating MediaRecorder', error);
            throw new Error(AudioErrorCode.RECORDER_ERROR);
        }
    }

    /**
     * Stop all media tracks
     * @private
     */
    _stopMediaTracks() {
        if (this._mediaStream) {
            this._mediaStream.getTracks().forEach(track => {
                track.stop();
            });
            this._mediaStream = null;
        }
    }

    /**
     * Internal cleanup method
     * @private
     */
    _cleanup() {
        if (this._mediaRecorder) {
            if (this._mediaRecorder.state !== 'inactive') {
                try {
                    this._mediaRecorder.stop();
                } catch (e) {
                    // Ignore errors during cleanup
                }
            }
            this._mediaRecorder = null;
        }
        this._stopMediaTracks();
    }
}

// Export for use in other modules (also available globally)
if (typeof module !== 'undefined' && module.exports) {
    module.exports = { RecordingService, RecordingStatus, AudioErrorCode, ChunkStatus };
}

// ============================================
// Recording Progress SignalR Service
// ============================================

/**
 * RecordingProgressService - Handles SignalR connection for recording progress updates
 * Provides real-time progress updates during Save & Finish processing
 */
class RecordingProgressService {
    constructor() {
        this._hubConnection = null;
        this._modalInstance = null;
        this._onProgressUpdate = null;
        this._onComplete = null;
        this._onError = null;
        this._currentSessionId = null;
    }

    /**
     * Initialize the SignalR connection for recording progress
     * @param {string} authToken - JWT auth token for SignalR connection
     * @returns {Promise<void>}
     */
    async initialize(authToken) {
        if (!authToken) {
            return;
        }

        // Only connect if SignalR is available
        if (typeof signalR === 'undefined') {
            return;
        }

        try {
            this._hubConnection = new signalR.HubConnectionBuilder()
                .withUrl('/hubs/recording-progress', {
                    accessTokenFactory: () => authToken
                })
                .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
                .configureLogging(signalR.LogLevel.Warning)
                .build();

            // Handle connection state changes
            this._hubConnection.onreconnecting(() => {
                // Reconnecting to SignalR
            });

            this._hubConnection.onreconnected(() => {
                // Reconnected to SignalR
            });

            this._hubConnection.onclose(() => {
                // SignalR connection closed
            });

            // Handle recording progress updates
            this._hubConnection.on('RecordingProgress', (update) => {
                this._handleProgressUpdate(update);
            });

            // Start connection
            await this._hubConnection.start();

        } catch (err) {
            // Failed to initialize SignalR, continue without real-time progress
        }
    }

    /**
     * Check if connected to SignalR hub
     * @returns {boolean}
     */
    isConnected() {
        return this._hubConnection &&
               this._hubConnection.state === signalR.HubConnectionState.Connected;
    }

    /**
     * Join a session group for targeted updates
     * @param {number} sessionId - The recording session ID
     */
    async joinSessionGroup(sessionId) {
        if (this.isConnected() && sessionId) {
            try {
                this._currentSessionId = sessionId;
                await this._hubConnection.invoke('JoinSessionGroup', sessionId);
            } catch (err) {
                console.warn('RecordingProgressService: Failed to join session group:', err);
            }
        }
    }

    /**
     * Leave current session group (stops receiving updates for that session)
     */
    async leaveCurrentSessionGroup() {
        if (this.isConnected() && this._currentSessionId) {
            try {
                await this._hubConnection.invoke('LeaveSessionGroup', this._currentSessionId);
                this._currentSessionId = null;
            } catch (err) {
                console.warn('RecordingProgressService: Failed to leave session group:', err);
            }
        }
    }

    /**
     * Set callback for progress updates
     * @param {Function} callback - Called with progress update object
     */
    onProgressUpdate(callback) {
        this._onProgressUpdate = callback;
    }

    /**
     * Set callback for completion
     * @param {Function} callback - Called with clinical note IDs array
     */
    onComplete(callback) {
        this._onComplete = callback;
    }

    /**
     * Set callback for errors
     * @param {Function} callback - Called with error message
     */
    onError(callback) {
        this._onError = callback;
    }

    /**
     * Handle incoming progress updates from SignalR
     * @private
     */
    _handleProgressUpdate(update) {
        // Call external progress update callback
        if (this._onProgressUpdate) {
            this._onProgressUpdate(update);
        }

        // Update progress modal UI
        this._updateModalUI(update);

        // SignalR uses camelCase serialization
        const status = update.status ?? update.Status ?? 'in-progress';
        const clinicalNoteIds = update.clinicalNoteIds ?? update.ClinicalNoteIds ?? [];
        const error = update.error ?? update.Error;
        const message = update.message ?? update.Message;

        // Handle completion
        if (status === 'completed') {
            if (this._onComplete) {
                this._onComplete(clinicalNoteIds);
            }
        }

        // Handle errors
        if (status === 'failed') {
            if (this._onError) {
                this._onError(error || message);
            }
        }
    }

    /**
     * Update the progress modal UI with current progress
     * @private
     */
    _updateModalUI(update) {
        const progressBar = document.getElementById('progressBar');
        const progressPercentage = document.getElementById('progressPercentage');
        const progressMessage = document.getElementById('progressMessage');
        const progressDetails = document.getElementById('progressDetails');
        const progressStep = document.getElementById('progressStep');
        const notesCounterContainer = document.getElementById('notesCounterContainer');
        const notesCounter = document.getElementById('notesCounter');
        const modalLabel = document.getElementById('recordingProgressModalLabel');
        const progressSubtitle = document.getElementById('progressSubtitle');

        // SignalR uses camelCase serialization, so handle both cases
        const progress = update.progress ?? update.Progress ?? 0;
        const message = update.message ?? update.Message ?? 'Processing...';
        const details = update.details ?? update.Details ?? '';
        const step = update.step ?? update.Step ?? '';
        const status = update.status ?? update.Status ?? 'in-progress';
        const totalNotes = update.totalNotes ?? update.TotalNotes;
        const currentNoteNumber = update.currentNoteNumber ?? update.CurrentNoteNumber;

        // Update progress bar with smooth animation
        if (progressBar) {
            progressBar.style.width = `${progress}%`;
            progressBar.setAttribute('aria-valuenow', progress);
        }
        if (progressPercentage) {
            progressPercentage.textContent = `${progress}%`;
        }

        // Update message
        if (progressMessage) {
            progressMessage.textContent = message;
        }

        // Update details
        if (progressDetails) {
            progressDetails.textContent = details;
        }

        // Progress is shown as a percentage via #progressPercentage and the progress bar.
        // Hide the step indicator entirely (users prefer percentage over "Step 1 of 5").
        if (progressStep) {
            progressStep.textContent = '';
            progressStep.classList.add('d-none');
        }

        // Handle multi-note counter
        if (totalNotes && totalNotes > 1 && notesCounterContainer && notesCounter) {
            notesCounterContainer.classList.remove('d-none');
            if (currentNoteNumber) {
                notesCounter.textContent = `Clinical Note ${currentNoteNumber} of ${totalNotes}`;
            } else {
                notesCounter.textContent = `${totalNotes} notes to generate`;
            }
        } else if (notesCounterContainer) {
            notesCounterContainer.classList.add('d-none');
        }

        // Handle different statuses
        if (status === 'completed') {
            if (modalLabel) {
                modalLabel.innerHTML = '<i class="bi bi-check-circle-fill me-2 text-success"></i>Complete!';
            }
            if (progressSubtitle) {
                progressSubtitle.textContent = 'Your clinical note is ready';
            }
            if (progressBar) {
                progressBar.classList.remove('progress-bar-animated', 'progress-bar-striped');
                progressBar.classList.add('bg-success');
            }
        } else if (status === 'failed') {
            if (modalLabel) {
                modalLabel.innerHTML = '<i class="bi bi-exclamation-triangle-fill me-2 text-danger"></i>Error';
            }
            if (progressSubtitle) {
                progressSubtitle.textContent = 'Something went wrong';
            }
            if (progressBar) {
                progressBar.classList.remove('progress-bar-animated', 'progress-bar-striped');
                progressBar.classList.add('bg-danger');
            }
        }
    }

    /**
     * Show the recording progress modal
     * @param {number} sessionId - The recording session ID
     */
    showProgressModal(sessionId) {
        // Reset modal to initial state
        const progressBar = document.getElementById('progressBar');
        const progressPercentage = document.getElementById('progressPercentage');
        const progressMessage = document.getElementById('progressMessage');
        const progressDetails = document.getElementById('progressDetails');
        const progressStep = document.getElementById('progressStep');
        const notesCounterContainer = document.getElementById('notesCounterContainer');
        const modalLabel = document.getElementById('recordingProgressModalLabel');
        const progressSubtitle = document.getElementById('progressSubtitle');

        if (progressBar) {
            progressBar.style.width = '0%';
            progressBar.setAttribute('aria-valuenow', 0);
            progressBar.classList.remove('bg-success', 'bg-danger');
            progressBar.classList.add('progress-bar-animated', 'progress-bar-striped', 'bg-primary');
        }
        if (progressPercentage) progressPercentage.textContent = '0%';
        if (progressMessage) progressMessage.textContent = 'Getting started...';
        if (progressDetails) progressDetails.textContent = '';
        if (progressStep) {
            progressStep.textContent = '';
            progressStep.classList.add('d-none');
        }
        if (notesCounterContainer) notesCounterContainer.classList.add('d-none');
        if (modalLabel) modalLabel.innerHTML = '<i class="bi bi-hourglass-split me-2 text-primary"></i>Processing...';
        if (progressSubtitle) progressSubtitle.textContent = "We're creating your clinical note";

        // Set up close button handler
        const closeBtn = document.getElementById('closeProgressModalBtn');
        if (closeBtn) {
            const self = this;
            closeBtn.onclick = function() {
                self.hideProgressModal();
                if (typeof showToast === 'function') {
                    showToast('Info', 'Processing continues in the background. You\'ll receive a notification when complete.', 'info');
                }
            };
        }

        // Join session group for targeted updates
        this.joinSessionGroup(sessionId);

        // Show the modal
        const modalElement = document.getElementById('recordingProgressModal');
        if (modalElement && typeof bootstrap !== 'undefined') {
            this._modalInstance = new bootstrap.Modal(modalElement, {
                backdrop: 'static',
                keyboard: false
            });
            this._modalInstance.show();
        }
    }

    /**
     * Hide the recording progress modal
     */
    hideProgressModal() {
        if (this._modalInstance) {
            this._modalInstance.hide();
            this._modalInstance = null;
        }
    }

    /**
     * Get the modal instance for external control
     * @returns {bootstrap.Modal|null}
     */
    getModalInstance() {
        return this._modalInstance;
    }

    /**
     * Clean up SignalR connection
     */
    async cleanup() {
        if (this._hubConnection) {
            try {
                await this._hubConnection.stop();
            } catch (e) {
                // Ignore errors during cleanup
            }
            this._hubConnection = null;
        }
        this._modalInstance = null;
    }
}

// Create global singleton instance
const recordingProgressService = new RecordingProgressService();
