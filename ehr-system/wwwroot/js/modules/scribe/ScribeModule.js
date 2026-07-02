/**
 * ScribeModule.js
 * @version 2.0.1
 *
 * Handles AI scribe recording session functionality.
 * Manages the recording modal, audio capture, chunk upload,
 * session management, and clinical note generation.
 */

// Recording session state constants
const RecordingState = {
    NOT_STARTED: 'not_started',
    RECORDING: 'recording',
    PAUSED: 'paused',
    STOPPED: 'stopped'
};

class ScribeModule {
    constructor(options = {}) {
        this.api = options.api || window.apiRequest;
        this.eventBus = options.eventBus || null;

        // Recording state
        this.recordingSessionState = RecordingState.NOT_STARTED;
        this.recordingTimerInterval = null;
        this.recordingElapsedSeconds = 0;
        this.recordingSessionModalInstance = null;
        this.currentRecordingSessionId = null;
        this.currentRecordingAppointment = null;
        this.unfinishedSessionData = null;
        this.recordingService = null;
        this.startOverConfirmModal = null;

        // Saved appointment ID for completion callback (survives modal close)
        this._completionAppointmentId = null;

        // Floating bar state — when true, modal hide should NOT reset recording
        this._isCollapsedToBar = false;

        // Selected template IDs for note generation
        this._selectedTemplateIds = null;

        // Chunk upload queue state
        this._pendingChunkUploads = [];
        this._isUploadingChunk = false;
        this._activeUploadPromise = null;

        // Recording health monitoring state
        this._lastSpeechTime = null;          // Timestamp of last detected speech
        this._silenceWarningShown = false;     // Whether silence warning is visible
        this._silenceWarningLevel = 0;         // 0=none, 1=warning(30s), 2=critical(60s)
        this._isOffline = !navigator.onLine;   // Current network status
        this._chunkUploadFailCount = 0;        // Consecutive upload failures

        // Event listeners bound to this
        this._boundHandleStartRecording = this._handleStartRecording.bind(this);
        this._boundHandlePauseRecording = this._handlePauseRecording.bind(this);
        this._boundHandleResumeRecording = this._handleResumeRecording.bind(this);
        this._boundHandleSaveProgress = this._handleSaveProgress.bind(this);
        this._boundHandleSaveAndFinish = this._handleSaveAndFinish.bind(this);
        this._boundHandleResumeSession = this._handleResumeSession.bind(this);
        this._boundHandleStartOverClick = this._handleStartOverClick.bind(this);
        this._boundHandleConfirmStartOver = this._handleConfirmStartOver.bind(this);
        this._boundHandleRetryNoteGeneration = this._handleRetryNoteGeneration.bind(this);
        this._boundHandleRetryProcessing = this._handleRetryProcessing.bind(this);

        this.isInitialized = false;
    }

    /**
     * Initialize the scribe module
     */
    async init() {
        if (this.isInitialized) return;

        // Initialize SignalR for progress updates
        const authToken = localStorage.getItem('authToken');
        if (authToken && typeof recordingProgressService !== 'undefined') {
            await recordingProgressService.initialize(authToken);

            // Set up callbacks
            recordingProgressService.onComplete((clinicalNoteIds) => {
                this._handleRecordingComplete(clinicalNoteIds);
            });

            recordingProgressService.onError((error) => {
                this._handleRecordingError(error);
            });
        }

        this.isInitialized = true;
    }

    /**
     * Get or create the RecordingService instance
     */
    _getRecordingService() {
        if (!this.recordingService) {
            if (typeof RecordingService === 'undefined') {
                console.error('[ScribeModule] RecordingService not loaded');
                return null;
            }
            this.recordingService = new RecordingService();

            // Set up chunk upload handler
            this.recordingService.onChunkCreated((chunk) => {
                this._uploadRecordingChunk(chunk);
            });

            // Monitor audio levels for silence/no-mic detection
            this.recordingService.onAudioLevel((rms) => {
                this._handleAudioLevelUpdate(rms);
            });

            // Handle MediaRecorder errors
            this.recordingService.onError((error) => {
                console.error('[ScribeModule] Recording error:', error);
                this._showRecordingWarning('noAudio', 'Recording error: microphone may have disconnected. Please check your device.');
            });
        }
        return this.recordingService;
    }

    /**
     * Open the record session modal for an appointment
     * @param {Object} appointment - The appointment object
     */
    async openModal(appointment) {
        if (!appointment) {
            if (typeof showToast === 'function') {
                showToast('Error', 'No appointment selected', 'error');
            }
            return;
        }

        this.currentRecordingAppointment = appointment;

        // Close appointment detail modal if open
        const detailModal = document.getElementById('appointmentDetailModal');
        if (detailModal) {
            const bsInstance = bootstrap.Modal.getInstance(detailModal);
            if (bsInstance) bsInstance.hide();
        }

        // Reset modal to initial state
        this._resetModal();

        // Populate modal with appointment data
        this._populateModal(appointment);

        // Load template checkboxes for note selection
        this._loadTemplateCheckboxes();

        // Initialize event listeners if not already done
        const modalElement = document.getElementById('recordSessionModal');
        if (modalElement && !modalElement.hasAttribute('data-events-initialized')) {
            this._initModalEvents();
            modalElement.setAttribute('data-events-initialized', 'true');
        }

        // Show the record session modal
        this.recordingSessionModalInstance = new bootstrap.Modal(modalElement, {
            backdrop: 'static',
            keyboard: false
        });
        this.recordingSessionModalInstance.show();

        // Check for unfinished session after modal is shown
        await this._checkAndShowUnfinishedSession(appointment.AppointmentId);
    }

    /**
     * Start recording instantly without showing the pre-recording modal.
     * If there's an unfinished session, falls back to the modal for resume/start-over options.
     * @param {Object} appointment - The appointment object
     */
    async startInstantly(appointment) {
        if (!appointment) {
            if (typeof showToast === 'function') showToast('Error', 'No appointment selected', 'error');
            return;
        }

        this.currentRecordingAppointment = appointment;

        // Close appointment detail modal if open
        const detailModal = document.getElementById('appointmentDetailModal');
        if (detailModal) {
            const bsInstance = bootstrap.Modal.getInstance(detailModal);
            if (bsInstance) bsInstance.hide();
        }

        // Initialize event listeners for modal (needed for floating bar events and stop flow)
        const modalElement = document.getElementById('recordSessionModal');
        if (modalElement && !modalElement.hasAttribute('data-events-initialized')) {
            this._initModalEvents();
            modalElement.setAttribute('data-events-initialized', 'true');
        }

        // Check for unfinished session
        try {
            const result = await this._checkUnfinishedSession(appointment.AppointmentId);
            if (result.Success && result.HasUnfinishedSession) {
                const status = result.RecordingStatus;

                if (status === 'Paused' || status === 'Recording') {
                    // Paused/recording session — show simplified resume modal (no template selection, no appointment details)
                    await this._showSimpleResumeModal(appointment, result);
                    return;
                } else {
                    // Failed/FailedNoteGeneration — show full modal for retry options
                    await this.openModal(appointment);
                    return;
                }
            }
        } catch (e) {
            console.warn('[ScribeModule] Error checking unfinished session, proceeding with new:', e);
        }

        // No unfinished session — start recording immediately
        const service = this._getRecordingService();
        if (!service) {
            if (typeof showToast === 'function') showToast('Error', 'Recording service not available. Please refresh the page.', 'error');
            return;
        }

        try {
            // Create a new recording session
            const sessionResult = await this._createRecordingSession(appointment.AppointmentId);
            if (!sessionResult.Success) {
                throw new Error(sessionResult.Message || 'Failed to create recording session');
            }
            this.currentRecordingSessionId = sessionResult.SessionId;

            // Start audio recording
            await service.startRecording();

            // Update session status to Recording in backend
            await this._updateRecordingSessionStatus(this.currentRecordingSessionId, 'Recording');

            // Update UI state
            this.recordingSessionState = RecordingState.RECORDING;
            this.recordingElapsedSeconds = 0;

            // Start the timer
            this._startTimer();

            // Initialize recording health monitoring
            this._initRecordingHealthMonitor();

            // Go straight to floating bar — no modal at all
            this._collapseToFloatingBar();

        } catch (error) {
            console.error('[ScribeModule] Error starting instant recording:', error);
            const errorMessage = RecordingService.getErrorMessage(error.message);
            if (typeof showToast === 'function') showToast('Error', errorMessage, 'error');
        }
    }

    /**
     * Show a simplified resume modal for paused sessions.
     * Only shows: paused alert with duration + Resume / Start Over buttons.
     * Resume click → immediate recording start → floating bar.
     */
    async _showSimpleResumeModal(appointment, sessionData) {
        this.currentRecordingAppointment = appointment;
        this.unfinishedSessionData = sessionData;

        // Initialize event listeners for floating bar (needed after resume)
        const modalElement = document.getElementById('recordSessionModal');
        if (modalElement && !modalElement.hasAttribute('data-events-initialized')) {
            this._initModalEvents();
            modalElement.setAttribute('data-events-initialized', 'true');
        }

        const elapsedFormatted = this._formatRecordingTime(sessionData.ElapsedSeconds || 0);

        // Build minimal modal body — just the paused info + two buttons
        const modalBody = document.querySelector('#recordSessionModal .modal-body');
        if (modalBody) {
            modalBody.innerHTML = `
                <div class="alert alert-info mb-4" style="display:flex;gap:12px;align-items:flex-start;">
                    <div style="width:36px;height:36px;border-radius:50%;background:#0ea5e9;color:white;display:flex;align-items:center;justify-content:center;flex-shrink:0;">
                        <i class="bi bi-pause-circle-fill"></i>
                    </div>
                    <div>
                        <h6 class="mb-1 fw-bold">Recording Paused</h6>
                        <span>You have a paused recording session.<br>Recorded time: <strong>${elapsedFormatted}</strong></span>
                    </div>
                </div>
                <div class="d-flex gap-3">
                    <button class="btn btn-primary flex-fill" id="simpleResumeBtn">
                        <i class="bi bi-play-fill me-1"></i>Resume
                    </button>
                    <button class="btn btn-outline-secondary flex-fill" id="simpleStartOverBtn">
                        <i class="bi bi-arrow-counterclockwise me-1"></i>Start Over
                    </button>
                </div>
            `;
        }

        // Show the modal (stripped down)
        if (!this.recordingSessionModalInstance) {
            this.recordingSessionModalInstance = new bootstrap.Modal(document.getElementById('recordSessionModal'));
        }
        this._isCollapsedToBar = false;
        this.recordingSessionModalInstance.show();

        // Bind Resume button — instant start
        document.getElementById('simpleResumeBtn')?.addEventListener('click', async () => {
            const btn = document.getElementById('simpleResumeBtn');
            if (btn) { btn.disabled = true; btn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Resuming...'; }

            try {
                const result = await this._resumeRecordingSession(sessionData.SessionId);
                if (!result.success) throw new Error(result.message || 'Failed to resume');

                this.currentRecordingSessionId = result.sessionId;
                this.recordingElapsedSeconds = sessionData.ElapsedSeconds || 0;

                const service = this._getRecordingService();
                if (!service) throw new Error('Recording service not available');

                await service.startRecording();
                await this._updateRecordingSessionStatus(this.currentRecordingSessionId, 'Recording');

                this.recordingSessionState = RecordingState.RECORDING;
                this._startTimer();
                this._initRecordingHealthMonitor();
                this._collapseToFloatingBar();
            } catch (error) {
                console.error('[ScribeModule] Error resuming:', error);
                const errorMessage = RecordingService.getErrorMessage(error.message);
                if (typeof showToast === 'function') showToast('Error', errorMessage, 'error');
                if (btn) { btn.disabled = false; btn.innerHTML = '<i class="bi bi-play-fill me-1"></i>Resume'; }
            }
        });

        // Bind Start Over button
        document.getElementById('simpleStartOverBtn')?.addEventListener('click', async () => {
            const btn = document.getElementById('simpleStartOverBtn');
            if (btn) { btn.disabled = true; btn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Starting over...'; }

            try {
                const result = await this._discardAndRestartSession(sessionData.SessionId, appointment.AppointmentId);
                if (!result.success) throw new Error(result.message || 'Failed to start over');

                this.currentRecordingSessionId = result.sessionId;
                this.recordingElapsedSeconds = 0;

                const service = this._getRecordingService();
                if (!service) throw new Error('Recording service not available');

                await service.startRecording();
                await this._updateRecordingSessionStatus(this.currentRecordingSessionId, 'Recording');

                this.recordingSessionState = RecordingState.RECORDING;
                this._startTimer();
                this._initRecordingHealthMonitor();
                this._collapseToFloatingBar();
            } catch (error) {
                console.error('[ScribeModule] Error starting over:', error);
                const errorMessage = RecordingService.getErrorMessage(error.message);
                if (typeof showToast === 'function') showToast('Error', errorMessage, 'error');
                if (btn) { btn.disabled = false; btn.innerHTML = '<i class="bi bi-arrow-counterclockwise me-1"></i>Start Over'; }
            }
        });
    }

    /**
     * Close the modal before recording starts
     */
    closeModalBeforeStart() {
        if (this.recordingSessionModalInstance) {
            this.recordingSessionModalInstance.hide();
            this.recordingSessionModalInstance = null;
        }
        this.currentRecordingAppointment = null;
    }

    /**
     * Discard the current recording
     */
    async discardRecording() {
        if (!this.currentRecordingSessionId || !this.currentRecordingAppointment) {
            if (typeof showToast === 'function') {
                showToast('Error', 'No recording session to discard', 'error');
            }
            return;
        }

        const service = this._getRecordingService();
        const wasRecording = this.recordingSessionState === RecordingState.RECORDING;

        // Pause the recording while showing confirmation dialog
        if (wasRecording && service) {
            service.pauseRecording();
            this._stopTimer();
            this.recordingSessionState = RecordingState.PAUSED;
            this._updateUI();
        }

        // Show confirmation dialog before discarding
        if (window.ConfirmDialog) {
            const confirmed = await ConfirmDialog.show({
                title: 'Discard Recording?',
                message: 'Are you sure you want to discard this recording? All recorded audio will be permanently deleted and cannot be recovered.',
                confirmText: 'Discard Recording',
                cancelText: 'Keep Recording',
                confirmClass: 'btn-danger',
                headerClass: 'bg-danger text-white'
            });

            if (!confirmed) {
                // User cancelled - resume recording if it was active before
                if (wasRecording && service) {
                    service.resumeRecording();
                    this.recordingSessionState = RecordingState.RECORDING;
                    this._updateUI();
                    this._startTimer();
                }
                return;
            }
        }

        // Stop the recording service completely before discarding
        if (service && (service.isRecording() || service.getStatus() === RecordingStatus.PAUSED)) {
            service.stopRecording();
        }

        // Stop the timer (in case it wasn't already stopped)
        this._stopTimer();

        try {
            // Call discard and restart API
            const result = await this._discardAndRestartSession(
                this.currentRecordingSessionId,
                this.currentRecordingAppointment.AppointmentId
            );

            if (result.success) {
                // Close the modal and clean up
                if (this.recordingSessionModalInstance) {
                    this.recordingSessionModalInstance.hide();
                    this.recordingSessionModalInstance = null;
                }

                // Hide floating bar if visible
                const floatingBar = document.getElementById('floatingRecordingBar');
                if (floatingBar) floatingBar.classList.add('d-none');
                this._isCollapsedToBar = false;

                // Reset all state
                this.unfinishedSessionData = null;
                this.currentRecordingSessionId = null;
                this.currentRecordingAppointment = null;
                this.recordingElapsedSeconds = 0;
                this.recordingSessionState = RecordingState.NOT_STARTED;
                this._updateTimerDisplay();

                // Reset UI state and status badge
                this._resetUIState();
                this._updateStatusBadge(RecordingState.NOT_STARTED);

                if (typeof showToast === 'function') {
                    showToast('Success', 'Recording discarded.', 'success');
                }
            } else {
                this._showError(result.message || 'Failed to discard recording');
            }
        } catch (error) {
            console.error('[ScribeModule] Error discarding recording:', error);
            this._showError('Failed to discard recording: ' + error.message);
        }
    }

    // =========================================
    // Private Methods
    // =========================================

    /**
     * Reset the modal to initial state
     */
    _resetModal() {
        this.recordingSessionState = RecordingState.NOT_STARTED;
        this.recordingElapsedSeconds = 0;
        this.currentRecordingSessionId = null;
        this.unfinishedSessionData = null;
        this._selectedTemplateIds = null;
        this._isCollapsedToBar = false;

        // Hide floating bar if visible
        const floatingBar = document.getElementById('floatingRecordingBar');
        if (floatingBar) floatingBar.classList.add('d-none');

        // Reset chunk upload queue
        this._pendingChunkUploads = [];
        this._isUploadingChunk = false;
        this._activeUploadPromise = null;

        // Reset recording health monitoring
        this._teardownRecordingHealthMonitor();
        this._lastSpeechTime = null;
        this._silenceWarningShown = false;
        this._silenceWarningLevel = 0;
        this._chunkUploadFailCount = 0;
        this._hideAllRecordingWarnings();

        // Show template selection section again
        const templateSection = document.getElementById('recordSessionTemplateSelection');
        if (templateSection) templateSection.classList.remove('d-none');

        // Stop any existing timer
        this._stopTimer();

        // Reset timer display
        this._updateTimerDisplay();

        // Reset status badge
        this._updateStatusBadge(RecordingState.NOT_STARTED);

        // Reset UI state
        this._resetUIState();

        // Hide error
        this._hideError();

        // Clean up recording service
        const service = this._getRecordingService();
        if (service) {
            service.cleanup();
        }
    }

    /**
     * Reset UI state to initial
     */
    _resetUIState() {
        // Hide unfinished session alert and reset its styling
        const alertEl = document.getElementById('unfinishedSessionAlert');
        if (alertEl) {
            alertEl.classList.add('d-none');
            alertEl.className = 'alert alert-info d-none mb-4';
        }

        // Show start recording button, hide others
        document.getElementById('startRecordingContainer')?.classList.remove('d-none');
        document.getElementById('resumeSessionContainer')?.classList.add('d-none');
        document.getElementById('pauseResumeContainer')?.classList.add('d-none');
        document.getElementById('recordingActionButtons')?.classList.add('d-none');
        document.getElementById('recordingAnimation')?.classList.add('d-none');

        // Hide all action buttons in resume container
        document.getElementById('resumeSessionBtn')?.classList.add('d-none');
        document.getElementById('retryNoteGenerationBtn')?.classList.add('d-none');
        document.getElementById('retryProcessingBtn')?.classList.add('d-none');

        // Reset timer container styling
        const timerContainer = document.querySelector('.recording-timer-container');
        if (timerContainer) {
            timerContainer.classList.remove('recording-active', 'recording-paused');
        }

        // Reset start recording button
        const startBtn = document.getElementById('startRecordingBtn');
        if (startBtn) {
            startBtn.disabled = false;
            startBtn.innerHTML = '<i class="bi bi-mic-fill me-2"></i>Start Recording';
        }

        // Show close button (since recording is not active)
        const closeBtn = document.getElementById('recordSessionCloseBtn');
        if (closeBtn) {
            closeBtn.style.display = '';
        }
    }

    /**
     * Populate modal with appointment data
     */
    _populateModal(appt) {
        try {
            // Format date and time
            const startTime = appt.StartTime ? new Date(appt.StartTime) : null;
            const dateStr = startTime ? startTime.toLocaleDateString('en-US', {
                weekday: 'short',
                month: 'short',
                day: 'numeric',
                year: 'numeric'
            }) : '-';
            const timeStr = startTime ? startTime.toLocaleTimeString('en-US', {
                hour: 'numeric',
                minute: '2-digit'
            }) : '-';

            // Appointment type mapping
            const typeNames = ['New Patient', 'Follow-Up', 'Annual Physical', 'Wellness', 'Consultation', 'Telehealth', 'Procedure', 'Urgent', 'Lab Review', 'Med Review'];
            const typeName = typeNames[appt.Type] || 'Appointment';

            // Update modal elements
            document.getElementById('recordSessionApptDate').textContent = dateStr;
            document.getElementById('recordSessionApptTime').textContent = timeStr;
            document.getElementById('recordSessionApptType').textContent = typeName;
            document.getElementById('recordSessionPatientName').textContent = appt.PatientName || '-';
            document.getElementById('recordSessionProviderName').textContent = appt.ProviderName || '-';

            // Update header date/time
            document.getElementById('recordSessionDateTime').textContent = `${dateStr} at ${timeStr}`;
        } catch (error) {
            console.error('[ScribeModule] Error populating modal:', error);
            this._showError('Error loading appointment information.');
        }
    }

    /**
     * Initialize modal event listeners
     */
    _initModalEvents() {
        // Start Recording button
        const startBtn = document.getElementById('startRecordingBtn');
        if (startBtn) {
            startBtn.addEventListener('click', this._boundHandleStartRecording);
        }

        // Pause button
        const pauseBtn = document.getElementById('pauseRecordingBtn');
        if (pauseBtn) {
            pauseBtn.addEventListener('click', this._boundHandlePauseRecording);
        }

        // Resume button (for unpausing during recording)
        const resumeBtn = document.getElementById('resumeRecordingBtn');
        if (resumeBtn) {
            resumeBtn.addEventListener('click', this._boundHandleResumeRecording);
        }

        // Resume Session button (for resuming unfinished session)
        const resumeSessionBtn = document.getElementById('resumeSessionBtn');
        if (resumeSessionBtn) {
            resumeSessionBtn.addEventListener('click', this._boundHandleResumeSession);
        }

        // Start Over button
        const startOverBtn = document.getElementById('startOverBtn');
        if (startOverBtn) {
            startOverBtn.addEventListener('click', this._boundHandleStartOverClick);
        }

        // Confirm Start Over button
        const confirmStartOverBtn = document.getElementById('confirmStartOverBtn');
        if (confirmStartOverBtn) {
            confirmStartOverBtn.addEventListener('click', this._boundHandleConfirmStartOver);
        }

        // Retry Note Generation button
        const retryNoteGenBtn = document.getElementById('retryNoteGenerationBtn');
        if (retryNoteGenBtn) {
            retryNoteGenBtn.addEventListener('click', this._boundHandleRetryNoteGeneration);
        }

        // Retry Processing button
        const retryProcessingBtn = document.getElementById('retryProcessingBtn');
        if (retryProcessingBtn) {
            retryProcessingBtn.addEventListener('click', this._boundHandleRetryProcessing);
        }

        // Save Progress button
        const saveProgressBtn = document.getElementById('saveProgressBtn');
        if (saveProgressBtn) {
            saveProgressBtn.addEventListener('click', this._boundHandleSaveProgress);
        }

        // Save & Finish button
        const saveFinishBtn = document.getElementById('saveFinishBtn');
        if (saveFinishBtn) {
            saveFinishBtn.addEventListener('click', this._boundHandleSaveAndFinish);
        }

        // Modal close button behavior
        const closeBtn = document.getElementById('recordSessionCloseBtn');
        if (closeBtn) {
            closeBtn.onclick = () => {
                if (this.recordingSessionState === RecordingState.NOT_STARTED) {
                    this.closeModalBeforeStart();
                } else {
                    // Recording is active — collapse to floating bar
                    this._collapseToFloatingBar();
                }
            };
        }

        // Handle modal hidden event
        const modalElement = document.getElementById('recordSessionModal');
        if (modalElement) {
            modalElement.addEventListener('hidden.bs.modal', () => {
                // If collapsed to floating bar, don't reset — recording is still active
                if (this._isCollapsedToBar) return;
                this._resetModal();
                this.currentRecordingAppointment = null;
            });
        }
    }

    /**
     * Handle start recording button click
     */
    async _handleStartRecording() {
        const startBtn = document.getElementById('startRecordingBtn');
        const service = this._getRecordingService();

        if (!service) {
            this._showError('Recording service not available. Please refresh the page and try again.');
            return;
        }

        // Validate at least one template is selected
        const selectedIds = this._getSelectedTemplateIds();
        if (selectedIds.length === 0) {
            this._showError('Please select at least one note template before recording.');
            return;
        }

        // Store selected template IDs for save & finish
        this._selectedTemplateIds = selectedIds;

        try {
            // Disable button while starting
            if (startBtn) {
                startBtn.disabled = true;
                startBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-2" role="status" aria-hidden="true"></span>Starting...';
            }

            // Hide template selection section once recording starts
            const templateSection = document.getElementById('recordSessionTemplateSelection');
            if (templateSection) templateSection.classList.add('d-none');

            // Hide any previous errors
            this._hideError();

            // Check if we're resuming an existing session
            const isResuming = this.currentRecordingSessionId != null && this.currentRecordingSessionId > 0;
            let nextSequenceNumber = 1;

            if (isResuming) {
                // Get the next sequence number for the resumed session
                nextSequenceNumber = await this._getNextSequenceNumber(this.currentRecordingSessionId);
            } else {
                // Create a new session
                const sessionResult = await this._createRecordingSession(this.currentRecordingAppointment.AppointmentId);
                if (!sessionResult.Success) {
                    throw new Error(sessionResult.Message || 'Failed to create recording session');
                }

                // Store the session ID for later use
                this.currentRecordingSessionId = sessionResult.SessionId;
            }

            // Start audio recording (this may prompt for permission)
            await service.startRecording();

            // Set the starting sequence number for chunks
            if (nextSequenceNumber > 1) {
                service.setStartingSequenceNumber(nextSequenceNumber);
            }

            // Update session status to Recording in backend
            await this._updateRecordingSessionStatus(this.currentRecordingSessionId, 'Recording');

            // Update UI state
            this.recordingSessionState = RecordingState.RECORDING;
            this._updateUI();

            // Start the timer
            this._startTimer();

            // Initialize recording health monitoring
            this._initRecordingHealthMonitor();

            // Collapse modal to floating bar so doctor can navigate workspace
            this._collapseToFloatingBar();

        } catch (error) {
            console.error('[ScribeModule] Error starting recording:', error);

            // Re-enable button
            if (startBtn) {
                startBtn.disabled = false;
                startBtn.innerHTML = '<i class="bi bi-mic-fill me-2"></i>Start Recording';
            }

            // Show user-friendly error
            const errorMessage = RecordingService.getErrorMessage(error.message);
            this._showError(errorMessage);
        }
    }

    /**
     * Handle pause recording button click
     */
    async _handlePauseRecording() {
        const service = this._getRecordingService();
        if (service && service.getStatus() === RecordingStatus.RECORDING) {
            // Pause audio recording
            service.pauseRecording();

            // Update session status in backend
            if (this.currentRecordingSessionId) {
                await this._updateRecordingSessionStatus(this.currentRecordingSessionId, 'Paused');
            }

            // Update UI state
            this.recordingSessionState = RecordingState.PAUSED;
            this._updateUI();
            this._stopTimer();
        }
    }

    /**
     * Handle resume recording button click
     */
    async _handleResumeRecording() {
        const service = this._getRecordingService();
        if (service && service.getStatus() === RecordingStatus.PAUSED) {
            // Resume audio recording
            service.resumeRecording();

            // Update session status in backend
            if (this.currentRecordingSessionId) {
                await this._updateRecordingSessionStatus(this.currentRecordingSessionId, 'Recording');
            }

            // Reset silence tracking to avoid false warning right after resume
            this._lastSpeechTime = Date.now();
            this._silenceWarningShown = false;
            this._silenceWarningLevel = 0;
            this._hideRecordingWarning('noAudio');

            // Update UI state
            this.recordingSessionState = RecordingState.RECORDING;
            this._updateUI();
            this._startTimer();
        }
    }

    /**
     * Handle save progress button click
     */
    async _handleSaveProgress() {
        const saveProgressBtn = document.getElementById('saveProgressBtn');

        // Validate session ID exists
        if (!this.currentRecordingSessionId) {
            this._showError('No active recording session to save');
            return;
        }

        try {
            if (saveProgressBtn) {
                saveProgressBtn.disabled = true;
                saveProgressBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Saving...';
            }

            // Show save overlay
            this._showSaveOverlay('Saving Progress...', 'Please wait while we save your recording');

            // Stop recording but keep the service ready
            const service = this._getRecordingService();
            if (service && service.isRecording()) {
                service.stopRecording();
            }

            // Stop the timer
            this._stopTimer();

            // Wait for any pending chunk uploads to complete
            if (this._pendingChunkUploads.length > 0 || this._isUploadingChunk || this._activeUploadPromise) {
                await this._waitForAllUploads();
            }

            // Save progress to server
            await this._saveRecordingProgress(this.currentRecordingSessionId, this.recordingElapsedSeconds);

            // Hide save overlay
            this._hideSaveOverlay();

            // Close modal
            if (this.recordingSessionModalInstance) {
                this.recordingSessionModalInstance.hide();
            }

            if (typeof showToast === 'function') {
                showToast('Success', 'Recording progress saved. You can resume later from the appointment.', 'success');
            }

        } catch (error) {
            console.error('[ScribeModule] Error saving progress:', error);
            this._hideSaveOverlay();
            this._showError('Failed to save progress: ' + error.message);
        } finally {
            if (saveProgressBtn) {
                saveProgressBtn.disabled = false;
                saveProgressBtn.innerHTML = '<i class="bi bi-save me-2"></i>Save Progress';
            }
        }
    }

    /**
     * Handle save and finish button click
     */
    async _handleSaveAndFinish() {
        const saveFinishBtn = document.getElementById('saveFinishBtn');
        const sessionId = this.currentRecordingSessionId;

        // Validate session ID exists
        if (!sessionId) {
            this._showError('No active recording session to save');
            return;
        }

        try {
            if (saveFinishBtn) {
                saveFinishBtn.disabled = true;
                saveFinishBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Processing...';
            }

            // Stop recording - this may create a final chunk
            const service = this._getRecordingService();
            if (service) {
                const status = service.getStatus();
                if (status === RecordingStatus.RECORDING || status === RecordingStatus.PAUSED) {
                    service.stopRecording();
                }
            }

            // Stop the timer
            this._stopTimer();

            // Hide floating bar if visible
            const floatingBar = document.getElementById('floatingRecordingBar');
            if (floatingBar) floatingBar.classList.add('d-none');
            this._isCollapsedToBar = false;

            // Save appointment ID for the completion callback (modal hide clears currentRecordingAppointment)
            this._completionAppointmentId = this.currentRecordingAppointment?.AppointmentId || null;

            // Close recording modal if open
            if (this.recordingSessionModalInstance) {
                this.recordingSessionModalInstance.hide();
            }

            // Show non-blocking top progress bar instead of blocking modal
            this._showTopProgressBar(sessionId);

            // Join SignalR session group for progress updates (without showing modal)
            if (typeof recordingProgressService !== 'undefined') {
                recordingProgressService.joinSessionGroup(sessionId);

                // Listen for progress updates to update the top bar
                recordingProgressService._onProgressUpdate = (update) => {
                    const progress = update.progress ?? update.Progress ?? 0;
                    const message = update.message ?? update.Message ?? 'Processing...';
                    const step = update.step ?? update.Step ?? '';
                    this._updateTopProgressBar(progress, message, step);
                };

                recordingProgressService._onComplete = (noteIds) => {
                    this._updateTopProgressBar(100, 'Clinical note generated successfully!', '', true);
                    if (typeof showToast === 'function') showToast('Success', 'Clinical note generated!', 'success');
                    if (window._encounterWorkspace) {
                        // Refresh note step and auto-open the latest generated note
                        setTimeout(async () => {
                            await window._encounterWorkspace._renderNoteStep();
                            // Auto-open the first generated note for viewing
                            if (noteIds && noteIds.length > 0 && typeof editClinicalNote === 'function') {
                                setTimeout(() => editClinicalNote(noteIds[0]), 500);
                            }
                        }, 500);
                    }
                    setTimeout(() => this._hideTopProgressBar(), 5000);
                };

                recordingProgressService._onError = (error) => {
                    this._updateTopProgressBar(0, 'Note generation failed: ' + (error || 'Unknown error'), '', false, true);
                    if (typeof showToast === 'function') showToast('Error', 'Note generation failed', 'error');
                    setTimeout(() => this._hideTopProgressBar(), 8000);
                };
            }

            // Wait for all pending chunk uploads to complete before calling save-and-finish
            if (this._pendingChunkUploads.length > 0 || this._isUploadingChunk || this._activeUploadPromise) {
                if (typeof recordingProgressService !== 'undefined') {
                    recordingProgressService._handleProgressUpdate({
                        sessionId: sessionId,
                        event: 'UploadingChunks',
                        message: 'Uploading audio...',
                        details: 'Please wait while we upload remaining audio data',
                        progress: 5,
                        step: '1/5',
                        status: 'in-progress'
                    });
                }
                await this._waitForAllUploads();
            }

            // Call save and finish API
            const result = await this._saveAndFinishRecordingSession(sessionId);

            // Handle response
            if (result && !result.Success) {
                if (result.Status === 'FailedNoteGeneration') {
                    console.warn('[ScribeModule] Note generation failed, recording saved:', result.Message);
                    if (typeof showToast === 'function') showToast('Warning', 'Recording saved, but note generation failed. You can retry from Draft Recordings.', 'warning');
                } else {
                    console.error('[ScribeModule] Save and finish failed:', result);
                    if (typeof showToast === 'function') showToast('Error', result.Message || 'Something went wrong. Please try again.', 'error');
                }
            }

        } catch (error) {
            console.error('[ScribeModule] Error saving and finishing:', error);
            if (typeof showToast === 'function') showToast('Error', 'Something went wrong. Please try again or create the note manually.', 'error');
        } finally {
            if (saveFinishBtn) {
                saveFinishBtn.disabled = false;
                saveFinishBtn.innerHTML = '<i class="bi bi-check-circle me-2"></i>Save & Finish';
            }
        }
    }

    /**
     * Handle resume session button click (for unfinished sessions)
     */
    async _handleResumeSession() {
        if (!this.unfinishedSessionData || !this.unfinishedSessionData.SessionId) {
            this._showError('No session to resume');
            return;
        }

        const resumeBtn = document.getElementById('resumeSessionBtn');
        if (resumeBtn) {
            resumeBtn.disabled = true;
            resumeBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Resuming...';
        }

        try {
            // Call resume API
            const result = await this._resumeRecordingSession(this.unfinishedSessionData.SessionId);

            if (result.success) {
                // Set the session ID
                this.currentRecordingSessionId = result.sessionId;

                // Hide resume UI, show start recording UI
                document.getElementById('unfinishedSessionAlert')?.classList.add('d-none');
                document.getElementById('resumeSessionContainer')?.classList.add('d-none');
                document.getElementById('startRecordingContainer')?.classList.remove('d-none');

                // The timer already has the elapsed time set, now user can click Start Recording
                if (typeof showToast === 'function') {
                    showToast('Success', 'Session resumed. Click Start Recording to continue.', 'success');
                }
            } else {
                this._showError(result.message || 'Failed to resume session');
            }
        } catch (error) {
            console.error('[ScribeModule] Error resuming session:', error);
            this._showError('Failed to resume session: ' + error.message);
        } finally {
            if (resumeBtn) {
                resumeBtn.disabled = false;
                resumeBtn.innerHTML = '<i class="bi bi-play-circle-fill me-2"></i>Resume Recording';
            }
        }
    }

    /**
     * Handle start over button click - show confirmation modal
     */
    _handleStartOverClick() {
        if (!this.startOverConfirmModal) {
            this.startOverConfirmModal = new bootstrap.Modal(document.getElementById('startOverConfirmModal'));
        }
        this.startOverConfirmModal.show();
    }

    /**
     * Handle confirmed start over
     */
    async _handleConfirmStartOver() {
        if (!this.unfinishedSessionData || !this.unfinishedSessionData.SessionId || !this.currentRecordingAppointment) {
            this._showError('No session to discard');
            return;
        }

        const confirmBtn = document.getElementById('confirmStartOverBtn');
        if (confirmBtn) {
            confirmBtn.disabled = true;
            confirmBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Starting Over...';
        }

        try {
            // Call discard and restart API
            const result = await this._discardAndRestartSession(
                this.unfinishedSessionData.SessionId,
                this.currentRecordingAppointment.AppointmentId
            );

            if (result.success) {
                // Close confirmation modal
                if (this.startOverConfirmModal) {
                    this.startOverConfirmModal.hide();
                }

                // Reset all state
                this.unfinishedSessionData = null;
                this.currentRecordingSessionId = null;
                this.recordingElapsedSeconds = 0;
                this.recordingSessionState = RecordingState.NOT_STARTED;
                this._updateTimerDisplay();

                // Reset UI state and status badge
                this._resetUIState();
                this._updateStatusBadge(RecordingState.NOT_STARTED);

                if (typeof showToast === 'function') {
                    showToast('Success', 'Previous session ended. Ready to start a new recording.', 'success');
                }
            } else {
                this._showError(result.message || 'Failed to start over');
            }
        } catch (error) {
            this._showError('Failed to start over: ' + error.message);
        } finally {
            if (confirmBtn) {
                confirmBtn.disabled = false;
                confirmBtn.innerHTML = '<i class="bi bi-arrow-counterclockwise me-2"></i>Yes, Start Over';
            }
        }
    }

    /**
     * Handle retry note generation button click
     */
    async _handleRetryNoteGeneration() {
        if (!this.unfinishedSessionData || !this.unfinishedSessionData.SessionId) {
            this._showError('No session to retry');
            return;
        }

        const sessionId = this.unfinishedSessionData.SessionId;
        const retryBtn = document.getElementById('retryNoteGenerationBtn');

        if (retryBtn) {
            retryBtn.disabled = true;
            retryBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Retrying...';
        }

        try {
            // Save appointment ID for completion callback
            this._completionAppointmentId = this.currentRecordingAppointment?.AppointmentId || null;

            // Close recording modal
            if (this.recordingSessionModalInstance) {
                this.recordingSessionModalInstance.hide();
            }

            // Show progress modal
            if (typeof recordingProgressService !== 'undefined') {
                recordingProgressService.showProgressModal(sessionId);
                recordingProgressService._handleProgressUpdate({
                    sessionId: sessionId,
                    event: 'RetryingNoteGeneration',
                    message: 'Retrying note generation...',
                    details: 'Please wait while we generate your clinical note',
                    progress: 50,
                    step: '4/5',
                    status: 'in-progress'
                });
            }

            // Call retry API
            const result = await this._retryNoteGeneration(sessionId);

            if (!result.Success) {
                console.error('[ScribeModule] Note generation retry failed:', result.Message);
                if (typeof recordingProgressService !== 'undefined') {
                    recordingProgressService._handleProgressUpdate({
                        sessionId: sessionId,
                        event: 'Error',
                        message: 'Note generation failed',
                        details: result.Message || 'Please try again later',
                        progress: 75,
                        step: '4/5',
                        status: 'failed',
                        error: result.Message
                    });
                }
            }
        } catch (error) {
            console.error('[ScribeModule] Error retrying note generation:', error);
            if (typeof recordingProgressService !== 'undefined') {
                recordingProgressService._handleProgressUpdate({
                    sessionId: sessionId,
                    event: 'Error',
                    message: 'Something went wrong',
                    details: error.message || 'Please try again later',
                    progress: 0,
                    status: 'failed',
                    error: error.message
                });
            }
        } finally {
            if (retryBtn) {
                retryBtn.disabled = false;
                retryBtn.innerHTML = '<i class="bi bi-arrow-repeat me-2"></i>Retry Note Generation';
            }
        }
    }

    /**
     * Handle retry processing button click (for Failed status)
     */
    async _handleRetryProcessing() {
        if (!this.unfinishedSessionData || !this.unfinishedSessionData.SessionId) {
            this._showError('No session to retry');
            return;
        }

        const sessionId = this.unfinishedSessionData.SessionId;
        const retryBtn = document.getElementById('retryProcessingBtn');

        if (retryBtn) {
            retryBtn.disabled = true;
            retryBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Retrying...';
        }

        try {
            // Save appointment ID for completion callback
            this._completionAppointmentId = this.currentRecordingAppointment?.AppointmentId || null;

            // Close recording modal
            if (this.recordingSessionModalInstance) {
                this.recordingSessionModalInstance.hide();
            }

            // Show progress modal
            if (typeof recordingProgressService !== 'undefined') {
                recordingProgressService.showProgressModal(sessionId);
            }

            // Call save-and-finish API (which will retry the full processing)
            const result = await this._saveAndFinishRecordingSession(sessionId);

            if (result && !result.Success) {
                if (result.Status === 'FailedNoteGeneration') {
                    console.warn('[ScribeModule] Processing succeeded but note generation failed');
                    if (typeof recordingProgressService !== 'undefined') {
                        recordingProgressService._handleProgressUpdate({
                            sessionId: sessionId,
                            event: 'Error',
                            message: 'Recording saved, but note generation failed',
                            details: 'You can retry note generation',
                            progress: 75,
                            step: '4/5',
                            status: 'failed',
                            error: result.Message
                        });
                    }
                } else {
                    console.error('[ScribeModule] Retry processing failed:', result);
                    if (typeof recordingProgressService !== 'undefined') {
                        recordingProgressService._handleProgressUpdate({
                            sessionId: sessionId,
                            event: 'Error',
                            message: 'Processing failed',
                            details: result.Message || 'Please try again later',
                            progress: 0,
                            status: 'failed',
                            error: result.Message
                        });
                    }
                }
            }
        } catch (error) {
            console.error('[ScribeModule] Error retrying processing:', error);
            if (typeof recordingProgressService !== 'undefined') {
                recordingProgressService._handleProgressUpdate({
                    sessionId: sessionId,
                    event: 'Error',
                    message: 'Something went wrong',
                    details: error.message || 'Please try again later',
                    progress: 0,
                    status: 'failed',
                    error: error.message
                });
            }
        } finally {
            if (retryBtn) {
                retryBtn.disabled = false;
                retryBtn.innerHTML = '<i class="bi bi-arrow-repeat me-2"></i>Retry Processing';
            }
        }
    }

    /**
     * Check for unfinished session and update UI accordingly
     */
    async _checkAndShowUnfinishedSession(appointmentId) {
        try {
            const result = await this._checkUnfinishedSession(appointmentId);

            if (result.Success && result.HasUnfinishedSession) {
                // Store unfinished session data for later use
                this.unfinishedSessionData = result;

                // Update the timer to show elapsed time
                this.recordingElapsedSeconds = result.ElapsedSeconds || 0;
                this._updateTimerDisplay();

                // Get status-specific UI text and buttons
                const statusConfig = this._getUnfinishedSessionConfig(result);

                // Update alert title and message
                const titleEl = document.getElementById('unfinishedSessionTitle');
                const messageEl = document.getElementById('unfinishedSessionMessage');
                if (titleEl) titleEl.textContent = statusConfig.title;
                if (messageEl) {
                    const elapsedFormatted = this._formatRecordingTime(result.ElapsedSeconds || 0);
                    messageEl.innerHTML = `${statusConfig.message}<br>Recorded time: <strong>${elapsedFormatted}</strong>`;
                }

                // Update alert styling based on status
                const alertEl = document.getElementById('unfinishedSessionAlert');
                if (alertEl) {
                    alertEl.className = `alert ${statusConfig.alertClass} mb-4`;
                    alertEl.classList.remove('d-none');
                }

                // Show/hide appropriate action buttons based on status
                document.getElementById('resumeSessionBtn')?.classList.toggle('d-none', !result.CanResume);
                document.getElementById('retryNoteGenerationBtn')?.classList.toggle('d-none', !result.CanRetryNoteGeneration);
                document.getElementById('retryProcessingBtn')?.classList.toggle('d-none', !result.CanRetrySaveAndFinish);

                // Hide "Start Recording" button, show action buttons container
                document.getElementById('startRecordingContainer')?.classList.add('d-none');
                document.getElementById('resumeSessionContainer')?.classList.remove('d-none');
            } else {
                // No unfinished session - show normal start recording UI
                this.unfinishedSessionData = null;
                document.getElementById('unfinishedSessionAlert')?.classList.add('d-none');
                document.getElementById('startRecordingContainer')?.classList.remove('d-none');
                document.getElementById('resumeSessionContainer')?.classList.add('d-none');
            }
        } catch (error) {
            console.error('[ScribeModule] Error checking for unfinished session:', error);
            this.unfinishedSessionData = null;
        }
    }

    /**
     * Get UI configuration based on unfinished session status
     */
    _getUnfinishedSessionConfig(sessionData) {
        const status = sessionData.RecordingStatus;

        switch (status) {
            case 'FailedNoteGeneration':
                return {
                    title: 'Note Generation Failed',
                    message: 'Your recording was saved but note generation failed. You can retry.',
                    alertClass: 'alert-warning'
                };
            case 'Failed':
                return {
                    title: 'Processing Failed',
                    message: 'Something went wrong during processing. You can retry.',
                    alertClass: 'alert-danger'
                };
            case 'Paused':
                return {
                    title: 'Recording Paused',
                    message: 'You have a paused recording session for this appointment.',
                    alertClass: 'alert-info'
                };
            case 'Recording':
            default:
                return {
                    title: 'Unfinished Recording Found',
                    message: 'You have an unfinished recording session for this appointment.',
                    alertClass: 'alert-info'
                };
        }
    }

    /**
     * Queue a chunk for upload. Chunks are uploaded sequentially to maintain order.
     */
    _uploadRecordingChunk(chunk) {
        if (!this.currentRecordingSessionId) {
            console.warn('[ScribeModule] No session ID, chunk will not be uploaded:', chunk.sequenceNumber);
            return;
        }

        this._pendingChunkUploads.push(chunk);

        // Start processing the queue if not already running
        if (!this._activeUploadPromise) {
            this._activeUploadPromise = this._processChunkUploadQueue();
        }
    }

    /**
     * Process the chunk upload queue sequentially
     */
    async _processChunkUploadQueue() {
        if (this._isUploadingChunk || this._pendingChunkUploads.length === 0) {
            return;
        }

        this._isUploadingChunk = true;

        while (this._pendingChunkUploads.length > 0) {
            const chunk = this._pendingChunkUploads.shift();

            // Update status to sending
            const service = this._getRecordingService();
            if (service) {
                service.updateChunkStatus(chunk.sequenceNumber, ChunkStatus.SENDING);
            }

            // Upload the chunk
            await this._uploadChunkToServer(chunk);

            // Small delay between uploads to avoid overwhelming the server
            if (this._pendingChunkUploads.length > 0) {
                await new Promise(resolve => setTimeout(resolve, 500));
            }
        }

        this._isUploadingChunk = false;
        this._activeUploadPromise = null;
    }

    /**
     * Upload a single chunk to the server
     */
    async _uploadChunkToServer(chunk) {
        try {
            const formData = new FormData();
            formData.append('audio', chunk.audioBlob, `chunk_${chunk.sequenceNumber}.webm`);
            formData.append('sequenceNumber', chunk.sequenceNumber.toString());
            formData.append('durationSeconds', chunk.duration.toString());

            const token = localStorage.getItem('authToken');
            const response = await fetch(`/api/recording/session/${this.currentRecordingSessionId}/chunk`, {
                method: 'POST',
                headers: {
                    'Authorization': `Bearer ${token}`
                },
                body: formData
            });

            if (!response.ok) {
                const errorText = await response.text();
                throw new Error(`Failed to upload chunk: ${errorText}`);
            }

            await response.json();

            // Update chunk status
            const service = this._getRecordingService();
            if (service) {
                service.updateChunkStatus(chunk.sequenceNumber, ChunkStatus.SENT);
            }

            // Reset failure count on success and hide upload warning
            this._chunkUploadFailCount = 0;
            this._hideRecordingWarning('uploadFailed');

        } catch (error) {
            console.error('[ScribeModule] Error uploading chunk:', error);
            const service = this._getRecordingService();
            if (service) {
                service.updateChunkStatus(chunk.sequenceNumber, ChunkStatus.ERROR);
            }

            // Track consecutive failures and warn user
            this._chunkUploadFailCount++;
            if (this._chunkUploadFailCount >= 2) {
                this._showRecordingWarning('uploadFailed',
                    `Audio upload failed (${this._chunkUploadFailCount} chunks). Check your internet connection.`);
            }
        }
    }

    /**
     * Wait for all pending chunk uploads to complete
     */
    async _waitForAllUploads() {
        if (this._activeUploadPromise) {
            await this._activeUploadPromise;
        }
        // Double-check: if more chunks were queued during wait, process them too
        while (this._pendingChunkUploads.length > 0 || this._isUploadingChunk) {
            if (this._activeUploadPromise) {
                await this._activeUploadPromise;
            } else if (this._pendingChunkUploads.length > 0) {
                this._activeUploadPromise = this._processChunkUploadQueue();
                await this._activeUploadPromise;
            } else {
                await new Promise(resolve => setTimeout(resolve, 100));
            }
        }
    }

    // =========================================
    // API Methods
    // =========================================

    /**
     * Create a new recording session
     */
    async _createRecordingSession(appointmentId) {
        const token = localStorage.getItem('authToken');
        const response = await fetch('/api/recording/session', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Authorization': `Bearer ${token}`
            },
            body: JSON.stringify({ AppointmentId: appointmentId })
        });

        if (!response.ok) {
            const errorText = await response.text();
            throw new Error(`Failed to create session: ${errorText}`);
        }

        return await response.json();
    }

    /**
     * Check for unfinished recording session
     */
    async _checkUnfinishedSession(appointmentId) {
        const token = localStorage.getItem('authToken');
        const response = await fetch(`/api/recording/session/check-unfinished/${appointmentId}`, {
            headers: {
                'Authorization': `Bearer ${token}`
            }
        });

        if (!response.ok) {
            return { Success: false, HasUnfinishedSession: false };
        }

        return await response.json();
    }

    /**
     * Resume a recording session
     */
    async _resumeRecordingSession(sessionId) {
        const token = localStorage.getItem('authToken');
        const response = await fetch(`/api/recording/session/${sessionId}/resume`, {
            method: 'POST',
            headers: {
                'Authorization': `Bearer ${token}`
            }
        });

        if (!response.ok) {
            return { success: false, message: 'Failed to resume session' };
        }

        const result = await response.json();
        return { success: true, sessionId: result.SessionId || sessionId };
    }

    /**
     * Discard and restart a recording session
     */
    async _discardAndRestartSession(sessionId, appointmentId) {
        const token = localStorage.getItem('authToken');
        const response = await fetch('/api/recording/session/discard-and-restart', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Authorization': `Bearer ${token}`
            },
            body: JSON.stringify({
                OldSessionId: sessionId,
                AppointmentId: appointmentId
            })
        });

        if (!response.ok) {
            const errorData = await response.json().catch(() => ({}));
            return { success: false, message: errorData.Message || 'Failed to discard session' };
        }

        const result = await response.json();
        return { success: result.Success, sessionId: result.SessionId, message: result.Message };
    }

    /**
     * Get the next sequence number for chunks
     */
    async _getNextSequenceNumber(sessionId) {
        try {
            const token = localStorage.getItem('authToken');
            const response = await fetch(`/api/recording/session/${sessionId}/next-sequence`, {
                headers: {
                    'Authorization': `Bearer ${token}`
                }
            });

            if (response.ok) {
                const result = await response.json();
                return result.NextSequenceNumber || 1;
            }
        } catch (error) {
            console.error('[ScribeModule] Error getting next sequence:', error);
        }
        return 1;
    }

    /**
     * Save recording progress
     */
    async _saveRecordingProgress(sessionId, elapsedSeconds) {
        const token = localStorage.getItem('authToken');
        const response = await fetch(`/api/recording/session/${sessionId}/save-progress`, {
            method: 'PUT',
            headers: {
                'Content-Type': 'application/json',
                'Authorization': `Bearer ${token}`
            },
            body: JSON.stringify({ ElapsedSeconds: elapsedSeconds })
        });

        if (!response.ok) {
            const errorData = await response.json().catch(() => ({}));
            throw new Error(errorData.Message || 'Failed to save progress');
        }

        return await response.json();
    }

    /**
     * Save and finish the recording session
     */
    async _saveAndFinishRecordingSession(sessionId) {
        const token = localStorage.getItem('authToken');
        const headers = {
            'Authorization': `Bearer ${token}`,
            'Content-Type': 'application/json'
        };

        // Send selected template IDs so backend generates notes only for chosen templates
        const body = this._selectedTemplateIds && this._selectedTemplateIds.length > 0
            ? JSON.stringify({ SelectedTemplateIds: this._selectedTemplateIds })
            : JSON.stringify({});

        const response = await fetch(`/api/recording/session/${sessionId}/save-and-finish`, {
            method: 'POST',
            headers,
            body
        });

        const result = await response.json();

        if (!response.ok) {
            // Check if it's a partial success (recording saved but note generation failed)
            if (result.Status === 'FailedNoteGeneration') {
                return result; // Let the caller handle this gracefully
            }
            throw new Error(result.Message || 'Failed to finish recording');
        }

        return result;
    }

    /**
     * Retry note generation for a session that failed note generation
     */
    async _retryNoteGeneration(sessionId) {
        const token = localStorage.getItem('authToken');
        const response = await fetch(`/api/recording/session/${sessionId}/retry-note-generation`, {
            method: 'POST',
            headers: {
                'Authorization': `Bearer ${token}`
            }
        });

        const result = await response.json();

        if (!response.ok) {
            return { Success: false, Message: result.Message || 'Failed to retry note generation' };
        }

        return result;
    }

    /**
     * Update recording session status in the backend database
     * @param {number} sessionId - The recording session ID
     * @param {string} status - The new status ('Recording', 'Paused', etc.)
     */
    async _updateRecordingSessionStatus(sessionId, status) {
        if (!sessionId) {
            console.warn('[ScribeModule] Cannot update status: no session ID');
            return false;
        }

        try {
            const token = localStorage.getItem('authToken');
            const response = await fetch(`/api/recording/session/${sessionId}/status`, {
                method: 'PUT',
                headers: {
                    'Content-Type': 'application/json',
                    'Authorization': `Bearer ${token}`
                },
                body: JSON.stringify({ Status: status })
            });

            if (!response.ok) {
                return false;
            }

            return true;
        } catch (error) {
            console.error('[ScribeModule] Error updating session status:', error);
            return false;
        }
    }

    // =========================================
    // Floating Bar Methods
    // =========================================

    /**
     * Collapse the modal into a floating bar at the bottom of the screen.
     * Recording continues; doctor can navigate the encounter workspace.
     */
    _collapseToFloatingBar() {
        this._isCollapsedToBar = true;

        // Populate bar with patient info
        const patientName = this.currentRecordingAppointment?.PatientName || '';
        const floatingPatient = document.getElementById('floatingRecPatient');
        if (floatingPatient) floatingPatient.textContent = patientName;

        // Sync timer display
        this._updateFloatingTimerDisplay();

        // Show the floating bar
        const bar = document.getElementById('floatingRecordingBar');
        if (bar) {
            bar.classList.remove('d-none', 'floating-bar-paused');
        }

        // Hide the modal (without triggering reset, thanks to _isCollapsedToBar flag)
        if (this.recordingSessionModalInstance) {
            this.recordingSessionModalInstance.hide();
        }

        // Bind floating bar button events (only once)
        this._initFloatingBarEvents();

        // Update floating bar button state
        this._updateFloatingBarUI();
    }

    /**
     * Restore the full modal from the floating bar.
     * Used when Stop or Expand is clicked.
     * @param {boolean} stopRecording - If true, also stops the recording before showing modal
     */
    _restoreFromFloatingBar(stopRecording = false) {
        // Hide the floating bar
        const bar = document.getElementById('floatingRecordingBar');
        if (bar) bar.classList.add('d-none');

        this._isCollapsedToBar = false;

        if (stopRecording) {
            // Stop recording via existing handler flow
            const service = this._getRecordingService();
            if (service && service.getStatus() === RecordingStatus.RECORDING) {
                service.pauseRecording();
            }
            this.recordingSessionState = RecordingState.PAUSED;
            this._stopTimer();
        }

        // Re-sync modal UI to current state
        this._updateUI();

        // Re-show the modal (reuse existing instance if present)
        const modalElement = document.getElementById('recordSessionModal');
        if (modalElement) {
            this.recordingSessionModalInstance = bootstrap.Modal.getInstance(modalElement)
                || new bootstrap.Modal(modalElement, { backdrop: 'static', keyboard: false });
            this.recordingSessionModalInstance.show();
        }
    }

    /**
     * Initialize floating bar button events (idempotent)
     */
    _initFloatingBarEvents() {
        const bar = document.getElementById('floatingRecordingBar');
        if (!bar || bar.hasAttribute('data-events-initialized')) return;
        bar.setAttribute('data-events-initialized', 'true');

        // Pause button
        const floatingPauseBtn = document.getElementById('floatingPauseBtn');
        floatingPauseBtn?.addEventListener('click', async () => {
            floatingPauseBtn.disabled = true;
            await this._handlePauseRecording();
            this._updateFloatingBarUI();
            floatingPauseBtn.disabled = false;
        });

        // Resume button
        const floatingResumeBtn = document.getElementById('floatingResumeBtn');
        floatingResumeBtn?.addEventListener('click', async () => {
            floatingResumeBtn.disabled = true;
            await this._handleResumeRecording();
            this._updateFloatingBarUI();
            floatingResumeBtn.disabled = false;
        });

        // Stop button — pauses recording and shows template selection
        document.getElementById('floatingStopBtn')?.addEventListener('click', () => {
            this._showTemplateSelectionOnStop();
        });

        // Expand button — opens full modal without stopping
        document.getElementById('floatingExpandBtn')?.addEventListener('click', () => {
            this._restoreFromFloatingBar(false);
        });
    }

    /**
     * Update floating bar UI based on current recording state
     */
    _updateFloatingBarUI() {
        const bar = document.getElementById('floatingRecordingBar');
        const pauseBtn = document.getElementById('floatingPauseBtn');
        const resumeBtn = document.getElementById('floatingResumeBtn');
        const label = document.getElementById('floatingRecLabel');

        if (!bar) return;

        const isRecording = this.recordingSessionState === RecordingState.RECORDING;
        const isPaused = this.recordingSessionState === RecordingState.PAUSED;

        bar.classList.toggle('floating-bar-paused', isPaused);

        if (pauseBtn) pauseBtn.classList.toggle('d-none', !isRecording);
        if (resumeBtn) resumeBtn.classList.toggle('d-none', !isPaused);
        if (label) label.textContent = isPaused ? 'PAUSED' : 'REC';
    }

    /**
     * Show template selection popup when Stop is clicked.
     * Pauses recording first, then lets clinician choose templates before processing.
     */
    async _showTemplateSelectionOnStop() {
        // Step 1: Pause recording (don't stop — preserve audio)
        const service = this._getRecordingService();
        if (service && service.getStatus() === RecordingStatus.RECORDING) {
            service.pauseRecording();
        }
        this.recordingSessionState = RecordingState.PAUSED;
        this._stopTimer();
        this._updateFloatingBarUI();

        // Update backend status
        if (this.currentRecordingSessionId) {
            this._updateRecordingSessionStatus(this.currentRecordingSessionId, 'Paused').catch(() => {});
        }

        // Step 2: Load templates and show overlay
        const overlay = document.getElementById('templateSelectionOverlay');
        const listContainer = document.getElementById('templateSelectionList');
        const errorEl = document.getElementById('templateSelectionError');
        if (!overlay || !listContainer) return;

        errorEl?.classList.add('d-none');
        overlay.classList.remove('d-none');

        // Load templates
        try {
            const token = localStorage.getItem('authToken');
            const response = await fetch('/api/clinical-note-templates?activeOnly=true', {
                headers: { 'Authorization': `Bearer ${token}` }
            });
            if (!response.ok) throw new Error('Failed to load templates');
            const templates = await response.json();

            if (!templates || templates.length === 0) {
                listContainer.innerHTML = '<div class="text-muted py-2"><i class="bi bi-info-circle me-1"></i>No active templates available.</div>';
                return;
            }

            listContainer.innerHTML = templates.map(t => `
                <div class="form-check mb-2">
                    <input class="form-check-input template-stop-checkbox" type="checkbox" value="${t.TemplateId}" id="templateStop_${t.TemplateId}">
                    <label class="form-check-label" for="templateStop_${t.TemplateId}">
                        ${this._escapeHtml(t.Name)}
                    </label>
                </div>
            `).join('');
        } catch (e) {
            listContainer.innerHTML = '<div class="text-danger py-2"><i class="bi bi-exclamation-triangle me-1"></i>Failed to load templates.</div>';
        }

        // Step 3: Bind button events (remove old listeners by cloning)
        const cancelBtn = document.getElementById('templateSelectionCancelBtn');
        const confirmBtn = document.getElementById('templateSelectionConfirmBtn');

        const newCancelBtn = cancelBtn.cloneNode(true);
        cancelBtn.parentNode.replaceChild(newCancelBtn, cancelBtn);
        newCancelBtn.addEventListener('click', () => {
            // Resume recording
            overlay.classList.add('d-none');
            if (service) service.resumeRecording();
            this.recordingSessionState = RecordingState.RECORDING;
            this._startTimer();
            this._updateFloatingBarUI();
            if (this.currentRecordingSessionId) {
                this._updateRecordingSessionStatus(this.currentRecordingSessionId, 'Recording').catch(() => {});
            }
        });

        const newConfirmBtn = confirmBtn.cloneNode(true);
        confirmBtn.parentNode.replaceChild(newConfirmBtn, confirmBtn);
        newConfirmBtn.addEventListener('click', async () => {
            // Validate at least one template selected
            const selectedIds = Array.from(document.querySelectorAll('.template-stop-checkbox:checked'))
                .map(cb => parseInt(cb.value));

            if (selectedIds.length === 0) {
                if (errorEl) {
                    errorEl.textContent = 'Please select at least one note template.';
                    errorEl.classList.remove('d-none');
                }
                return;
            }

            // Store selected templates and proceed with save & finish
            this._selectedTemplateIds = selectedIds;
            overlay.classList.add('d-none');

            // Now run the actual save & finish flow
            await this._handleSaveAndFinish();
        });
    }

    /**
     * Update the floating bar timer display
     */
    _updateFloatingTimerDisplay() {
        const el = document.getElementById('floatingRecTimer');
        if (el) {
            el.textContent = this._formatRecordingTime(this.recordingElapsedSeconds);
        }
    }

    // =========================================
    // Top Progress Bar (non-blocking)
    // =========================================

    _showTopProgressBar(sessionId) {
        // Remove existing if any
        this._hideTopProgressBar();

        const bar = document.createElement('div');
        bar.id = 'scribeTopProgressBar';
        bar.className = 'scribe-top-progress';
        bar.innerHTML = `
            <div class="scribe-top-progress-inner">
                <div class="d-flex align-items-center gap-3">
                    <div class="spinner-border spinner-border-sm text-primary" id="scribeProgressSpinner"></div>
                    <span class="fw-semibold" id="scribeProgressMessage">Processing recording...</span>
                    <small class="text-muted" id="scribeProgressStep"></small>
                </div>
                <button class="btn btn-sm btn-outline-secondary" onclick="document.getElementById('scribeTopProgressBar')?.remove()">
                    <i class="bi bi-x"></i>
                </button>
            </div>
            <div class="progress" style="height: 4px; border-radius: 0;">
                <div class="progress-bar progress-bar-striped progress-bar-animated" id="scribeProgressBarFill" role="progressbar" style="width: 5%"></div>
            </div>
        `;

        // Insert at the top of the encounter content area
        const header = document.getElementById('encounterHeader');
        if (header) {
            header.after(bar);
        } else {
            document.body.prepend(bar);
        }
    }

    _updateTopProgressBar(progress, message, step, isComplete = false, isError = false) {
        const fill = document.getElementById('scribeProgressBarFill');
        const msg = document.getElementById('scribeProgressMessage');
        const stepEl = document.getElementById('scribeProgressStep');
        const spinner = document.getElementById('scribeProgressSpinner');

        if (fill) {
            fill.style.width = `${progress}%`;
            fill.classList.remove('bg-success', 'bg-danger');
            if (isComplete) { fill.classList.add('bg-success'); fill.classList.remove('progress-bar-animated'); }
            if (isError) { fill.classList.add('bg-danger'); fill.classList.remove('progress-bar-animated'); }
        }
        if (msg) msg.textContent = message;
        // Show percentage instead of step text (e.g. "1/5")
        if (stepEl) stepEl.textContent = progress > 0 ? `${progress}%` : '';
        if (spinner && (isComplete || isError)) spinner.classList.add('d-none');
    }

    _hideTopProgressBar() {
        document.getElementById('scribeTopProgressBar')?.remove();
    }

    // =========================================
    // Recording Health Monitoring
    // =========================================

    /**
     * Initialize recording health monitors (network, audio).
     * Called once when recording starts.
     */
    _initRecordingHealthMonitor() {
        this._lastSpeechTime = Date.now();
        this._silenceWarningShown = false;
        this._silenceWarningLevel = 0;
        this._chunkUploadFailCount = 0;
        this._isOffline = !navigator.onLine;

        // Network status listeners
        this._boundOnOffline = () => {
            this._isOffline = true;
            this._showRecordingWarning('offline');
        };
        this._boundOnOnline = () => {
            this._isOffline = false;
            this._hideRecordingWarning('offline');
        };
        window.addEventListener('offline', this._boundOnOffline);
        window.addEventListener('online', this._boundOnOnline);

        // Show immediately if already offline
        if (this._isOffline) {
            this._showRecordingWarning('offline');
        }
    }

    /**
     * Tear down recording health monitors.
     * Called when recording stops / modal resets.
     */
    _teardownRecordingHealthMonitor() {
        if (this._boundOnOffline) {
            window.removeEventListener('offline', this._boundOnOffline);
            this._boundOnOffline = null;
        }
        if (this._boundOnOnline) {
            window.removeEventListener('online', this._boundOnOnline);
            this._boundOnOnline = null;
        }
    }

    /**
     * Handle audio level update from RecordingService.
     * Tracks silence duration and shows warnings.
     * @param {number} rms - Root mean square audio level (0-1)
     */
    _handleAudioLevelUpdate(rms) {
        // Only monitor while actively recording
        if (this.recordingSessionState !== RecordingState.RECORDING) return;

        const SILENCE_THRESHOLD = 0.01;
        const WARNING_SECONDS = 30;
        const CRITICAL_SECONDS = 60;

        if (rms >= SILENCE_THRESHOLD) {
            // Speech detected — reset silence tracking
            this._lastSpeechTime = Date.now();
            if (this._silenceWarningShown) {
                this._silenceWarningShown = false;
                this._silenceWarningLevel = 0;
                this._hideRecordingWarning('noAudio');
            }
            return;
        }

        // Still silent — check duration
        const silentSeconds = (Date.now() - (this._lastSpeechTime || Date.now())) / 1000;

        if (silentSeconds >= CRITICAL_SECONDS && this._silenceWarningLevel < 2) {
            this._silenceWarningLevel = 2;
            this._silenceWarningShown = true;
            this._showRecordingWarning('noAudio',
                'No audio detected for over 1 minute. Your microphone may not be working. Please check the device and speak to test.');
        } else if (silentSeconds >= WARNING_SECONDS && this._silenceWarningLevel < 1) {
            this._silenceWarningLevel = 1;
            this._silenceWarningShown = true;
            this._showRecordingWarning('noAudio',
                'No audio detected for 30 seconds. Please check that your microphone is working.');
        }
    }

    /**
     * Show a recording warning in both modal and floating bar.
     * @param {'noAudio'|'offline'|'uploadFailed'} type
     * @param {string} [message] - Optional custom message for modal alert
     */
    _showRecordingWarning(type, message) {
        // Modal warning
        const warningsContainer = document.getElementById('recordingWarnings');
        if (warningsContainer) warningsContainer.classList.remove('d-none');

        if (type === 'noAudio') {
            const el = document.getElementById('recordingWarningNoAudio');
            const textEl = document.getElementById('recordingWarningNoAudioText');
            if (el) el.classList.remove('d-none');
            if (textEl && message) textEl.textContent = message;
        } else if (type === 'offline') {
            const el = document.getElementById('recordingWarningOffline');
            if (el) el.classList.remove('d-none');
        } else if (type === 'uploadFailed') {
            const el = document.getElementById('recordingWarningUploadFailed');
            const textEl = document.getElementById('recordingWarningUploadText');
            if (el) el.classList.remove('d-none');
            if (textEl && message) textEl.textContent = message;
        }

        // Floating bar warning badges
        const floatingWarnings = document.getElementById('floatingRecWarnings');
        if (floatingWarnings) floatingWarnings.classList.remove('d-none');

        const badgeMap = {
            noAudio: 'floatingWarningNoAudio',
            offline: 'floatingWarningOffline',
            uploadFailed: 'floatingWarningUpload'
        };
        const badge = document.getElementById(badgeMap[type]);
        if (badge) badge.classList.remove('d-none');
    }

    /**
     * Hide a specific recording warning from both modal and floating bar.
     * @param {'noAudio'|'offline'|'uploadFailed'} type
     */
    _hideRecordingWarning(type) {
        // Modal
        if (type === 'noAudio') {
            document.getElementById('recordingWarningNoAudio')?.classList.add('d-none');
        } else if (type === 'offline') {
            document.getElementById('recordingWarningOffline')?.classList.add('d-none');
        } else if (type === 'uploadFailed') {
            document.getElementById('recordingWarningUploadFailed')?.classList.add('d-none');
        }

        // Floating bar badge
        const badgeMap = {
            noAudio: 'floatingWarningNoAudio',
            offline: 'floatingWarningOffline',
            uploadFailed: 'floatingWarningUpload'
        };
        document.getElementById(badgeMap[type])?.classList.add('d-none');

        // Hide containers if no warnings visible
        this._updateWarningContainerVisibility();
    }

    /**
     * Hide all recording warnings.
     */
    _hideAllRecordingWarnings() {
        document.getElementById('recordingWarnings')?.classList.add('d-none');
        document.getElementById('recordingWarningNoAudio')?.classList.add('d-none');
        document.getElementById('recordingWarningOffline')?.classList.add('d-none');
        document.getElementById('recordingWarningUploadFailed')?.classList.add('d-none');

        document.getElementById('floatingRecWarnings')?.classList.add('d-none');
        document.getElementById('floatingWarningNoAudio')?.classList.add('d-none');
        document.getElementById('floatingWarningOffline')?.classList.add('d-none');
        document.getElementById('floatingWarningUpload')?.classList.add('d-none');
    }

    /**
     * Hide warning containers if no individual warnings are visible.
     */
    _updateWarningContainerVisibility() {
        // Modal container
        const modalContainer = document.getElementById('recordingWarnings');
        if (modalContainer) {
            const anyVisible = modalContainer.querySelector('.alert:not(.d-none)');
            if (!anyVisible) modalContainer.classList.add('d-none');
        }

        // Floating bar container
        const floatingContainer = document.getElementById('floatingRecWarnings');
        if (floatingContainer) {
            const anyBadge = floatingContainer.querySelector('.badge:not(.d-none)');
            if (!anyBadge) floatingContainer.classList.add('d-none');
        }
    }

    // =========================================
    // UI Helper Methods
    // =========================================

    /**
     * Update UI based on recording state
     */
    _updateUI() {
        const isRecording = this.recordingSessionState === RecordingState.RECORDING;
        const isPaused = this.recordingSessionState === RecordingState.PAUSED;
        const hasStarted = isRecording || isPaused;

        // Update status badge
        this._updateStatusBadge(this.recordingSessionState);

        // Show/hide containers
        document.getElementById('startRecordingContainer')?.classList.toggle('d-none', hasStarted);
        document.getElementById('pauseResumeContainer')?.classList.toggle('d-none', !hasStarted);
        document.getElementById('recordingActionButtons')?.classList.toggle('d-none', !hasStarted);
        document.getElementById('recordingAnimation')?.classList.toggle('d-none', !isRecording);

        // Toggle pause/resume buttons
        document.getElementById('pauseRecordingBtn')?.classList.toggle('d-none', isPaused);
        document.getElementById('resumeRecordingBtn')?.classList.toggle('d-none', isRecording);

        // Update timer container styling
        const timerContainer = document.querySelector('.recording-timer-container');
        if (timerContainer) {
            timerContainer.classList.toggle('recording-active', isRecording);
            timerContainer.classList.toggle('recording-paused', isPaused);
        }

        // Close button: before start shows × (close), during recording shows minimize icon (collapse)
        const closeBtn = document.getElementById('recordSessionCloseBtn');
        const closeBtnIcon = document.getElementById('recordSessionCloseBtnIcon');
        if (closeBtn && closeBtnIcon) {
            closeBtn.style.display = '';
            if (hasStarted) {
                closeBtnIcon.className = 'bi bi-dash-lg';
                closeBtn.title = 'Minimize to floating bar';
                closeBtn.setAttribute('aria-label', 'Minimize');
            } else {
                closeBtnIcon.className = 'bi bi-x-lg';
                closeBtn.title = 'Close';
                closeBtn.setAttribute('aria-label', 'Close');
            }
        }
    }

    /**
     * Update the status badge
     */
    _updateStatusBadge(state) {
        const badge = document.getElementById('recordingStatusBadge');
        const icon = document.getElementById('recordingStatusIcon');
        const text = document.getElementById('recordingStatusText');

        if (!badge || !icon || !text) return;

        badge.className = 'badge fs-6 px-3 py-2';

        switch (state) {
            case RecordingState.RECORDING:
                badge.classList.add('bg-danger', 'status-recording');
                icon.className = 'bi bi-record-circle me-1';
                text.textContent = 'Recording';
                break;
            case RecordingState.PAUSED:
                badge.classList.add('bg-warning', 'text-dark', 'status-paused');
                icon.className = 'bi bi-pause-circle me-1';
                text.textContent = 'Paused';
                break;
            default:
                badge.classList.add('bg-secondary', 'status-not-started');
                icon.className = 'bi bi-circle me-1';
                text.textContent = 'Not Started';
        }
    }

    /**
     * Start the recording timer
     */
    _startTimer() {
        this._stopTimer(); // Clear any existing timer

        const self = this;
        self.recordingTimerInterval = setInterval(() => {
            self.recordingElapsedSeconds++;
            self._updateTimerDisplay();
        }, 1000);

        // Immediately update display to show timer is running
        this._updateTimerDisplay();
    }

    /**
     * Stop the recording timer
     */
    _stopTimer() {
        if (this.recordingTimerInterval) {
            clearInterval(this.recordingTimerInterval);
            this.recordingTimerInterval = null;
        }
    }

    /**
     * Update the timer display (modal + floating bar)
     */
    _updateTimerDisplay() {
        const formatted = this._formatRecordingTime(this.recordingElapsedSeconds);
        const timerEl = document.getElementById('recordingTimer');
        if (timerEl) timerEl.textContent = formatted;
        const floatingTimer = document.getElementById('floatingRecTimer');
        if (floatingTimer) floatingTimer.textContent = formatted;
    }

    /**
     * Format seconds to MM:SS display
     */
    _formatRecordingTime(totalSeconds) {
        const minutes = Math.floor(totalSeconds / 60);
        const seconds = totalSeconds % 60;
        return `${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`;
    }

    /**
     * Load template checkboxes for note selection before recording
     */
    async _loadTemplateCheckboxes() {
        const container = document.getElementById('recordSessionTemplateList');
        if (!container) return;

        container.innerHTML = '<div class="text-center text-muted py-2"><div class="spinner-border spinner-border-sm me-1" role="status"></div> Loading templates...</div>';

        try {
            const token = localStorage.getItem('authToken');
            const response = await fetch('/api/clinical-note-templates?activeOnly=true', {
                headers: { 'Authorization': `Bearer ${token}` }
            });

            if (!response.ok) throw new Error('Failed to load templates');

            const templates = await response.json();

            if (!templates || templates.length === 0) {
                container.innerHTML = '<div class="text-muted py-2"><i class="bi bi-info-circle me-1"></i>No active templates available. Please contact your administrator.</div>';
                return;
            }

            // Render checkboxes (all pre-checked by default)
            container.innerHTML = templates.map(t => `
                <div class="form-check mb-1">
                    <input class="form-check-input record-template-checkbox" type="checkbox" value="${t.TemplateId}" id="recordTemplate_${t.TemplateId}" checked>
                    <label class="form-check-label" for="recordTemplate_${t.TemplateId}">
                        ${this._escapeHtml(t.Name)}
                        ${t.LocationName ? `<small class="text-muted">(${this._escapeHtml(t.LocationName)})</small>` : ''}
                    </label>
                </div>
            `).join('');

        } catch (error) {
            console.error('[ScribeModule] Error loading templates:', error);
            container.innerHTML = '<div class="text-danger py-2"><i class="bi bi-exclamation-triangle me-1"></i>Failed to load templates. Please close and try again.</div>';
        }
    }

    /**
     * Get selected template IDs from checkboxes
     */
    _getSelectedTemplateIds() {
        const checkboxes = document.querySelectorAll('.record-template-checkbox:checked');
        return Array.from(checkboxes).map(cb => parseInt(cb.value));
    }

    /**
     * Escape HTML for safe rendering
     */
    _escapeHtml(str) {
        if (!str) return '';
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }

    /**
     * Show error message
     */
    _showError(message) {
        const errorContainer = document.getElementById('recordSessionError');
        const errorMessage = document.getElementById('recordSessionErrorMessage');
        if (errorContainer && errorMessage) {
            errorMessage.textContent = message;
            errorContainer.classList.remove('d-none');
        }
    }

    /**
     * Hide error message
     */
    _hideError() {
        const errorContainer = document.getElementById('recordSessionError');
        if (errorContainer) {
            errorContainer.classList.add('d-none');
        }
    }

    /**
     * Show save overlay
     */
    _showSaveOverlay(title, message) {
        const overlay = document.getElementById('recordingSaveOverlay');
        const overlayTitle = document.getElementById('saveOverlayTitle');
        const overlayMessage = document.getElementById('saveOverlayMessage');

        if (overlayTitle) overlayTitle.textContent = title;
        if (overlayMessage) overlayMessage.textContent = message;
        if (overlay) overlay.classList.remove('d-none');
    }

    /**
     * Hide save overlay
     */
    _hideSaveOverlay() {
        const overlay = document.getElementById('recordingSaveOverlay');
        if (overlay) overlay.classList.add('d-none');
    }

    /**
     * Handle recording completion
     */
    _handleRecordingComplete(clinicalNoteIds) {
        // Use saved appointment ID since currentRecordingAppointment is cleared when recording modal hides
        const appointmentId = this._completionAppointmentId;

        // Close progress modal after a short delay to show completion
        setTimeout(() => {
            if (typeof recordingProgressService !== 'undefined') {
                recordingProgressService.hideProgressModal();
                // Leave session group to stop receiving updates
                recordingProgressService.leaveCurrentSessionGroup();
            }

            // Open the clinical notes in tabbed view (preferred UX)
            if (clinicalNoteIds && clinicalNoteIds.length > 0) {
                const firstNoteId = clinicalNoteIds[0];
                if (typeof openTabbedClinicalNotes === 'function' && appointmentId) {
                    openTabbedClinicalNotes(appointmentId, firstNoteId);
                } else if (typeof editClinicalNote === 'function') {
                    editClinicalNote(firstNoteId);
                }
            }

            // Clear saved appointment ID
            this._completionAppointmentId = null;
        }, 1500);
    }

    /**
     * Handle recording error from SignalR
     */
    _handleRecordingError(error) {
        console.error('[ScribeModule] Recording error:', error);

        // Don't immediately close the progress modal - it shows the error state.
        // Let the user close it manually via the X button.
        // Just show a toast with a clean message.
        if (typeof showToast === 'function') {
            showToast('Error', 'Clinical note generation failed. You can retry from Draft Recordings.', 'error');
        }
    }
}

// Create global instance
window.scribeModule = new ScribeModule();

// Initialize when DOM is ready
document.addEventListener('DOMContentLoaded', () => {
    window.scribeModule.init();
});
