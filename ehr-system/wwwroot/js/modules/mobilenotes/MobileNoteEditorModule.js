/**
 * MobileNoteEditorModule - Mobile Clinical Note Editor
 *
 * Provides touch-optimized clinical note editing with:
 * - Rich text editing (Trumbowyg)
 * - Template selection
 * - SignalR real-time updates
 * - Care Episode creation/update workflow
 * - Initial Evaluation validation
 */
class MobileNoteEditorModule {
    constructor(options = {}) {
        this.api = options.api || null;
        this.eventBus = options.eventBus || null;

        // State
        this.appointmentId = null;
        this.patientId = null;
        this.providerId = null;
        this.noteId = null;
        this.currentUserId = null;
        this.authToken = null;
        this.apiBaseUrl = '';
        this.signalRConnection = null;
        this.selectedTemplateId = null;
        this.noteType = 3; // Default to Progress Note
        this.isEditMode = false;
        this.hasChanges = false;

        // Care episode state
        this.careEpisodeExtraction = null;
        this.isReevaluation = false;

        // Element references
        this.elements = {};

        this.init();
    }

    /**
     * Initialize the module
     */
    async init() {
        this.parseUrlParams();
        this.cacheElements();
        this.bindEvents();

        // Set page title
        this.elements.pageTitle.textContent = this.isEditMode ? 'Edit Note' : 'Write Report';

        // Initialize Trumbowyg
        this.initEditor();

        // Initialize SignalR
        await this.initSignalR();

        // Load data
        await this.loadData();
    }

    /**
     * Parse URL parameters
     */
    parseUrlParams() {
        const params = new URLSearchParams(window.location.search);
        this.appointmentId = params.get('appointmentId');
        this.patientId = params.get('patientId');
        this.providerId = params.get('providerId');
        this.noteId = params.get('noteId');
        this.authToken = params.get('token');
        this.apiBaseUrl = params.get('apiUrl') || '';
        this.currentUserId = params.get('userId');
        this.isEditMode = !!this.noteId;
    }

    /**
     * Cache DOM element references
     */
    cacheElements() {
        this.elements = {
            // Loading
            loadingOverlay: document.getElementById('loadingOverlay'),
            loadingText: document.getElementById('loadingText'),

            // Header
            pageTitle: document.getElementById('pageTitle'),
            closeBtn: document.getElementById('closeBtn'),

            // Error
            errorMessage: document.getElementById('errorMessage'),

            // Info
            patientName: document.getElementById('patientName'),
            providerName: document.getElementById('providerName'),

            // Form
            templateSelect: document.getElementById('templateSelect'),
            serviceDate: document.getElementById('serviceDate'),
            noteContent: document.getElementById('noteContent'),

            // Actions
            saveDraftBtn: document.getElementById('saveDraftBtn'),
            saveSignBtn: document.getElementById('saveSignBtn'),

            // Toast
            toastMessage: document.getElementById('toastMessage'),

            // Missing fields modal
            missingFieldsModal: document.getElementById('missingFieldsModal'),
            missingFieldsTitle: document.getElementById('missingFieldsTitle'),
            missingFieldsInfo: document.getElementById('missingFieldsInfo'),
            missingFieldsList: document.getElementById('missingFieldsList'),

            // Care episode modal
            careEpisodeModal: document.getElementById('careEpisodeModal'),
            careEpisodeTitle: document.getElementById('careEpisodeTitle'),
            careEpisodeInfo: document.getElementById('careEpisodeInfo'),
            diagnosisGroup: document.getElementById('diagnosisGroup'),
            ceDiagnosis: document.getElementById('ceDiagnosis'),
            ceExpectedVisits: document.getElementById('ceExpectedVisits'),
            ceDuration: document.getElementById('ceDuration'),
            ceVisitFrequency: document.getElementById('ceVisitFrequency'),
            cePhysicianName: document.getElementById('cePhysicianName'),
            ceConfirmBtnText: document.getElementById('ceConfirmBtnText')
        };
    }

    /**
     * Bind event handlers
     */
    bindEvents() {
        // Close button
        this.elements.closeBtn.addEventListener('click', () => this.handleClose());

        // Template change
        this.elements.templateSelect.addEventListener('change', () => this.handleTemplateChange());

        // Save buttons
        this.elements.saveDraftBtn.addEventListener('click', () => this.saveDraft());
        this.elements.saveSignBtn.addEventListener('click', () => this.saveAndSign());

        // Missing fields modal close
        document.getElementById('closeMissingFieldsBtn')?.addEventListener('click', () => this.closeMissingFieldsModal());

        // Care episode modal
        document.getElementById('closeCareEpisodeBtn')?.addEventListener('click', () => this.closeCareEpisodeModal());
        document.getElementById('confirmCareEpisodeBtn')?.addEventListener('click', () => this.confirmCareEpisode());
    }

    /**
     * Initialize Trumbowyg editor
     */
    initEditor() {
        $('#noteContent').trumbowyg({
            btns: [
                ['undo', 'redo'],
                ['formatting'],
                ['strong', 'em', 'underline'],
                ['unorderedList', 'orderedList'],
                ['removeformat'],
                ['fullscreen']
            ],
            autogrow: true,
            removeformatPasted: true
        });

        // Track changes
        $('#noteContent').on('tbwchange', () => {
            this.hasChanges = true;
        });
    }

    /**
     * Initialize SignalR connection
     */
    async initSignalR() {
        try {
            this.signalRConnection = new signalR.HubConnectionBuilder()
                .withUrl(this.apiBaseUrl + '/hubs/mobile', {
                    accessTokenFactory: () => this.authToken
                })
                .withAutomaticReconnect()
                .build();

            await this.signalRConnection.start();
            console.log('SignalR connected');
        } catch (err) {
            console.error('SignalR connection failed:', err);
        }
    }

    /**
     * Make API request
     */
    async apiRequest(endpoint, options = {}) {
        const url = this.apiBaseUrl + '/api' + endpoint;
        const response = await fetch(url, {
            ...options,
            headers: {
                'Content-Type': 'application/json',
                'Authorization': 'Bearer ' + this.authToken,
                ...options.headers
            }
        });

        if (!response.ok) {
            throw new Error(`API error: ${response.status}`);
        }

        const text = await response.text();
        return text ? JSON.parse(text) : null;
    }

    /**
     * Load initial data
     */
    async loadData() {
        this.showLoading('Loading...');

        try {
            if (this.isEditMode) {
                await this.loadExistingNote();
            } else if (this.appointmentId) {
                await this.loadFromAppointment();
            } else {
                this.showError('Missing appointment or note context');
            }

            this.hideLoading();
        } catch (err) {
            console.error('Error loading data:', err);
            this.showError('Failed to load data: ' + err.message);
            this.hideLoading();
        }
    }

    /**
     * Load existing note for editing
     */
    async loadExistingNote() {
        const note = await this.apiRequest(`/clinical-notes/${this.noteId}`);

        this.elements.patientName.textContent = note.PatientName;
        this.elements.providerName.textContent = note.ProviderName;
        this.elements.serviceDate.value = note.ServiceDate;
        $('#noteContent').trumbowyg('html', note.HtmlContent || '');

        this.patientId = note.PatientId;
        this.providerId = note.ProviderId;
        this.appointmentId = note.AppointmentId;
        this.noteType = note.Type;

        // Load templates
        await this.loadTemplates(note.Type);
        if (note.TemplateId) {
            this.elements.templateSelect.value = note.TemplateId;
            this.selectedTemplateId = note.TemplateId;
        }
    }

    /**
     * Load from appointment context
     */
    async loadFromAppointment() {
        const appointment = await this.apiRequest(`/appointments/${this.appointmentId}`);

        this.elements.patientName.textContent = appointment.PatientName;
        this.elements.providerName.textContent = appointment.ProviderName;

        // Set service date from appointment
        const apptDate = new Date(appointment.StartTime);
        this.elements.serviceDate.value = apptDate.toISOString().split('T')[0];

        this.patientId = appointment.PatientId;
        this.providerId = appointment.ProviderId;

        // Map appointment type to note type
        this.noteType = this.mapAppointmentTypeToNoteType(appointment.Type);

        // Load templates filtered by appointment type
        await this.loadTemplatesForAppointment(appointment.Type, appointment.LocationId);
    }

    /**
     * Map appointment type to clinical note type
     */
    mapAppointmentTypeToNoteType(appointmentType) {
        const mapping = {
            0: 0, // InitialEvaluation
            1: 2, // FollowUp -> DailyVisit
            2: 6, // Reevaluation
            3: 4, // Discharge -> DischargeSummary
            4: 3  // Consultation -> ProgressNote
        };
        return mapping[appointmentType] ?? 3;
    }

    /**
     * Load templates
     */
    async loadTemplates(type) {
        try {
            const templates = await this.apiRequest(`/clinical-note-templates?activeOnly=true`);
            this.populateTemplateSelect(templates);
        } catch (err) {
            console.error('Error loading templates:', err);
        }
    }

    /**
     * Load templates for appointment
     */
    async loadTemplatesForAppointment(appointmentType, locationId) {
        try {
            let url = `/clinical-note-templates/for-appointment?appointmentType=${appointmentType}`;
            if (locationId) {
                url += `&locationId=${locationId}`;
            }
            const templates = await this.apiRequest(url);
            this.populateTemplateSelect(templates);
        } catch (err) {
            console.error('Error loading templates:', err);
            // Fallback to all templates
            await this.loadTemplates();
        }
    }

    /**
     * Populate template dropdown
     */
    populateTemplateSelect(templates) {
        const select = this.elements.templateSelect;
        select.innerHTML = '<option value="">Select a template</option>';

        templates.forEach(t => {
            const option = document.createElement('option');
            option.value = t.TemplateId;
            option.textContent = t.Name;
            option.dataset.type = t.Type;
            select.appendChild(option);
        });
    }

    /**
     * Handle template change
     */
    async handleTemplateChange() {
        const select = this.elements.templateSelect;
        const templateId = select.value;

        if (!templateId) return;

        // Check if there's existing content
        const currentContent = $('#noteContent').trumbowyg('html');
        if (currentContent && currentContent.trim() !== '') {
            if (!confirm('Loading this template will replace your current content. Continue?')) {
                select.value = this.selectedTemplateId || '';
                return;
            }
        }

        this.showLoading('Loading template...');

        try {
            const template = await this.apiRequest(`/clinical-note-templates/${templateId}`);

            this.selectedTemplateId = template.TemplateId;
            this.noteType = template.Type;

            $('#noteContent').trumbowyg('html', template.HtmlContent || '');
            this.hasChanges = true;

            this.hideLoading();
        } catch (err) {
            console.error('Error loading template:', err);
            this.showToast('Failed to load template');
            this.hideLoading();
        }
    }

    /**
     * Save draft
     */
    async saveDraft() {
        if (!this.validateForm()) return;

        this.elements.saveDraftBtn.disabled = true;
        this.showLoading('Saving draft...');

        try {
            const htmlContent = $('#noteContent').trumbowyg('html');
            const serviceDate = this.elements.serviceDate.value;

            if (this.isEditMode && this.noteId) {
                // Update existing note
                await this.apiRequest(`/clinical-notes/${this.noteId}`, {
                    method: 'PUT',
                    body: JSON.stringify({ HtmlContent: htmlContent })
                });
            } else {
                // Create new note
                const result = await this.apiRequest('/clinical-notes', {
                    method: 'POST',
                    body: JSON.stringify({
                        PatientId: parseInt(this.patientId),
                        ProviderId: parseInt(this.providerId),
                        AppointmentId: this.appointmentId ? parseInt(this.appointmentId) : null,
                        TemplateId: this.selectedTemplateId ? parseInt(this.selectedTemplateId) : null,
                        Type: this.noteType,
                        ServiceDate: serviceDate,
                        HtmlContent: htmlContent
                    })
                });

                this.noteId = result.ClinicalNoteId;
                this.isEditMode = true;
            }

            this.hasChanges = false;
            this.hideLoading();
            this.elements.saveDraftBtn.disabled = false;
        } catch (err) {
            console.error('Error saving draft:', err);
            this.showToast('Failed to save draft');
            this.hideLoading();
            this.elements.saveDraftBtn.disabled = false;
        }
    }

    /**
     * Save and sign - handles IE/Re-eval validation flow
     */
    async saveAndSign() {
        if (!this.validateForm()) return;

        this.elements.saveSignBtn.disabled = true;
        this.showLoading('Saving...');

        try {
            const htmlContent = $('#noteContent').trumbowyg('html');
            const serviceDate = this.elements.serviceDate.value;

            // First save/create the note
            if (this.isEditMode && this.noteId) {
                await this.apiRequest(`/clinical-notes/${this.noteId}`, {
                    method: 'PUT',
                    body: JSON.stringify({ HtmlContent: htmlContent })
                });
            } else {
                const result = await this.apiRequest('/clinical-notes', {
                    method: 'POST',
                    body: JSON.stringify({
                        PatientId: parseInt(this.patientId),
                        ProviderId: parseInt(this.providerId),
                        AppointmentId: this.appointmentId ? parseInt(this.appointmentId) : null,
                        TemplateId: this.selectedTemplateId ? parseInt(this.selectedTemplateId) : null,
                        Type: this.noteType,
                        ServiceDate: serviceDate,
                        HtmlContent: htmlContent
                    })
                });
                this.noteId = result.ClinicalNoteId;
                this.isEditMode = true;
            }

            // Check if this is an Initial Evaluation (0) or Re-evaluation (6)
            const isIEOrReeval = this.noteType === 0 || this.noteType === 6;

            if (isIEOrReeval) {
                // Validate and extract care episode data
                this.showLoading('Validating...');
                const validation = await this.validateInitialEvaluation(this.noteId);

                if (!validation.proceed) {
                    // Validation failed - modal was shown, user needs to edit
                    this.hideLoading();
                    this.elements.saveSignBtn.disabled = false;
                    return;
                }

                // Show care episode confirmation modal
                this.hideLoading();
                this.showCareEpisodeModal(validation.extraction, validation.isReevaluation);
                this.elements.saveSignBtn.disabled = false;
            } else {
                // Regular note - simple confirmation and sign
                if (!confirm('Signing this note will finalize it and it cannot be edited. Continue?')) {
                    this.hideLoading();
                    this.elements.saveSignBtn.disabled = false;
                    return;
                }

                this.showLoading('Signing...');
                await this.apiRequest(`/clinical-notes/${this.noteId}/sign`, {
                    method: 'POST',
                    body: JSON.stringify({})
                });

                this.hasChanges = false;
                await this.notifyMobileAndClose('signed', this.noteId);
            }
        } catch (err) {
            console.error('Error in saveAndSign:', err);
            this.showToast('Failed to save/sign note');
            this.hideLoading();
            this.elements.saveSignBtn.disabled = false;
        }
    }

    /**
     * Validate Initial Evaluation or Re-evaluation note
     */
    async validateInitialEvaluation(noteId) {
        try {
            const result = await this.apiRequest(`/clinical-notes/${noteId}/validate-initial-evaluation`, {
                method: 'POST'
            });

            // Not an IE or Re-eval - proceed directly
            if (!result.IsInitialEvaluation && !result.IsReevaluation) {
                return { proceed: true, isInitialEval: false, isReevaluation: false };
            }

            // Re-evaluation specific error (no care episode linked)
            if (result.IsReevaluation && result.ErrorMessage) {
                this.showToast(result.ErrorMessage);
                return { proceed: false, isInitialEval: false, isReevaluation: true };
            }

            const extraction = result.ExtractionResult;

            // Check for missing fields
            if (!extraction.IsComplete) {
                this.showMissingFieldsModal(extraction.MissingFields, result.IsReevaluation);
                return { proceed: false, isInitialEval: result.IsInitialEvaluation, isReevaluation: result.IsReevaluation };
            }

            // Validate Expected Visits
            if (extraction.ExpectedVisits === null || extraction.ExpectedVisits === undefined) {
                this.showMissingFieldsModal(['Expected Visits is required'], result.IsReevaluation);
                return { proceed: false, isInitialEval: result.IsInitialEvaluation, isReevaluation: result.IsReevaluation };
            }

            if (extraction.ExpectedVisits === 0) {
                this.showMissingFieldsModal(['Expected Visits must be at least 1'], result.IsReevaluation);
                return { proceed: false, isInitialEval: result.IsInitialEvaluation, isReevaluation: result.IsReevaluation };
            }

            // Add re-evaluation context
            if (result.IsReevaluation) {
                extraction.CompletedVisits = result.CompletedVisits;
                extraction.CurrentExpectedVisits = result.CurrentExpectedVisits;
                extraction.CareEpisodeId = result.CareEpisodeId;
            }

            return {
                proceed: true,
                isInitialEval: result.IsInitialEvaluation,
                isReevaluation: result.IsReevaluation,
                extraction
            };

        } catch (error) {
            console.error('Validation error:', error);
            this.showToast('Failed to validate note');
            return { proceed: false, isInitialEval: false, isReevaluation: false };
        }
    }

    /**
     * Show missing fields modal
     */
    showMissingFieldsModal(missingFields, isReeval) {
        const title = isReeval ? 'Missing Fields for Re-evaluation' : 'Missing Fields for Initial Evaluation';
        const info = isReeval
            ? 'The following fields are required to update the Care Episode from the Re-evaluation note:'
            : 'The following fields are required to create a Care Episode from the Initial Evaluation:';

        this.elements.missingFieldsTitle.textContent = title;
        this.elements.missingFieldsInfo.textContent = info;

        this.elements.missingFieldsList.innerHTML = missingFields.map(field =>
            `<li><i class="bi bi-x-circle"></i>${field}</li>`
        ).join('');

        this.elements.missingFieldsModal.classList.remove('hidden');
    }

    /**
     * Close missing fields modal
     */
    closeMissingFieldsModal() {
        this.elements.missingFieldsModal.classList.add('hidden');
    }

    /**
     * Show care episode confirmation modal
     */
    showCareEpisodeModal(extraction, isReeval) {
        this.careEpisodeExtraction = extraction;
        this.isReevaluation = isReeval;

        // Update title and button text
        if (isReeval) {
            this.elements.careEpisodeTitle.textContent = 'Update Care Episode';
            this.elements.careEpisodeInfo.querySelector('span').textContent =
                'Review and confirm the updated Care Episode plan from your Re-evaluation note.';
            this.elements.ceConfirmBtnText.textContent = 'Update & Sign';
            this.elements.diagnosisGroup.style.display = 'none';
        } else {
            this.elements.careEpisodeTitle.textContent = 'Confirm Care Episode';
            this.elements.careEpisodeInfo.querySelector('span').textContent =
                'Review and confirm the Care Episode details before signing.';
            this.elements.ceConfirmBtnText.textContent = 'Confirm & Sign';
            this.elements.diagnosisGroup.style.display = 'block';

            // Show diagnosis info
            const diagnosisText = extraction.DiagnosisDescription || 'Not extracted';
            const diagnosisCode = extraction.DiagnosisCode ? ` (ICD-10: ${extraction.DiagnosisCode})` : '';
            this.elements.ceDiagnosis.textContent = diagnosisText + diagnosisCode;
        }

        // Populate form fields
        this.elements.ceExpectedVisits.value = extraction.ExpectedVisits || '';
        this.elements.ceDuration.value = extraction.DurationWeeks || '';
        this.elements.ceVisitFrequency.value = extraction.VisitFrequency || '';
        this.elements.cePhysicianName.value = extraction.PhysicianName || '';

        this.elements.careEpisodeModal.classList.remove('hidden');
    }

    /**
     * Close care episode modal
     */
    closeCareEpisodeModal() {
        this.elements.careEpisodeModal.classList.add('hidden');
        this.careEpisodeExtraction = null;
    }

    /**
     * Confirm care episode and sign
     */
    async confirmCareEpisode() {
        const expectedVisits = this.elements.ceExpectedVisits.value;

        if (!expectedVisits || parseInt(expectedVisits) < 1) {
            this.showToast('Expected Visits is required and must be at least 1');
            return;
        }

        this.showLoading('Signing...');
        this.elements.careEpisodeModal.classList.add('hidden');

        try {
            // Call sign-with-care-episode endpoint
            const result = await this.apiRequest(`/clinical-notes/${this.noteId}/sign-with-care-episode`, {
                method: 'POST',
                body: JSON.stringify({
                    DiagnosisCode: this.careEpisodeExtraction?.DiagnosisCode || null,
                    DiagnosisDescription: this.careEpisodeExtraction?.DiagnosisDescription || null,
                    Goals: this.careEpisodeExtraction?.Goals || null,
                    TreatmentPlan: this.careEpisodeExtraction?.TreatmentPlan || null,
                    PhysicianName: this.elements.cePhysicianName.value || null,
                    ExpectedVisits: parseInt(expectedVisits),
                    VisitFrequency: this.elements.ceVisitFrequency.value || null,
                    DurationWeeks: this.elements.ceDuration.value
                        ? parseInt(this.elements.ceDuration.value)
                        : null
                })
            });

            this.hasChanges = false;

            await this.notifyMobileAndClose('signed', this.noteId);
        } catch (err) {
            console.error('Error signing with care episode:', err);
            this.showToast('Failed to sign note');
            this.hideLoading();
        }
    }

    /**
     * Validate form
     */
    validateForm() {
        const serviceDate = this.elements.serviceDate.value;
        const content = $('#noteContent').trumbowyg('html');

        if (!serviceDate) {
            this.showToast('Service date is required');
            return false;
        }

        if (!content || content.trim() === '') {
            this.showToast('Note content is required');
            return false;
        }

        return true;
    }

    /**
     * Handle close button
     */
    async handleClose() {
        if (this.hasChanges) {
            if (!confirm('You have unsaved changes. Are you sure you want to leave?')) {
                return;
            }
        }

        await this.notifyMobileAndClose('closed', this.noteId);
    }

    /**
     * Notify mobile app via SignalR and close
     */
    async notifyMobileAndClose(action, savedNoteId) {
        try {
            if (this.signalRConnection && this.signalRConnection.state === 'Connected') {
                await this.signalRConnection.invoke('MobileWriteReportClosed', {
                    action: action,
                    noteId: savedNoteId,
                    userId: this.currentUserId
                });
            }
        } catch (err) {
            console.error('Error sending SignalR message:', err);
        }

        // Give SignalR time to send, then the mobile app will close the browser
        this.showLoading('Closing...');
    }

    // UI Helpers

    showLoading(text) {
        this.elements.loadingText.textContent = text || 'Loading...';
        this.elements.loadingOverlay.classList.remove('hidden');
    }

    hideLoading() {
        this.elements.loadingOverlay.classList.add('hidden');
    }

    showError(message) {
        this.elements.errorMessage.textContent = message;
        this.elements.errorMessage.style.display = 'block';
    }

    showToast(message) {
        const toast = this.elements.toastMessage;
        toast.textContent = message;
        toast.classList.add('show');

        setTimeout(() => {
            toast.classList.remove('show');
        }, 3000);
    }
}

// Self-initialization when DOM is ready
document.addEventListener('DOMContentLoaded', function() {
    const container = document.getElementById('mobileNoteEditorPage');
    if (container) {
        window.mobileNoteEditor = new MobileNoteEditorModule();
    }
});

// Global wrapper functions for onclick handlers
window.handleClose = function() {
    window.mobileNoteEditor?.handleClose();
};

window.saveDraft = function() {
    window.mobileNoteEditor?.saveDraft();
};

window.saveAndSign = function() {
    window.mobileNoteEditor?.saveAndSign();
};

window.handleTemplateChange = function() {
    window.mobileNoteEditor?.handleTemplateChange();
};

window.closeMissingFieldsModal = function() {
    window.mobileNoteEditor?.closeMissingFieldsModal();
};

window.closeCareEpisodeModal = function() {
    window.mobileNoteEditor?.closeCareEpisodeModal();
};

window.confirmCareEpisode = function() {
    window.mobileNoteEditor?.confirmCareEpisode();
};
