/**
 * ClinicalNoteSignModule - Clinical Note Signing Workflow
 *
 * Handles the complete sign workflow for clinical notes including:
 * - Initial Evaluation validation and Care Episode creation
 * - Re-evaluation validation and Care Episode updates
 * - Insurance validation and mismatch warnings
 * - Simple signing for regular notes
 *
 * @example
 *   const signModule = new ClinicalNoteSignModule();
 *   await signModule.init();
 *   const result = await signModule.sign(noteId);
 */
class ClinicalNoteSignModule {
    constructor(options = {}) {
        this.api = options.api || null;
        this.eventBus = options.eventBus || null;

        // State for Care Episode confirmation workflow
        this._confirmCallback = null;
        this._confirmExtraction = null;
        this._isReevaluation = false;

        // Bind methods
        this._handleConfirm = this._handleConfirm.bind(this);
        this._handleCancel = this._handleCancel.bind(this);
    }

    /**
     * Initialize the module
     */
    async init() {
        this._bindModalEvents();
        this._emit('clinicalNoteSign:initialized');
    }

    /**
     * Bind modal button events
     * @private
     */
    _bindModalEvents() {
        // The modal buttons use onclick="confirmCareEpisodeCreation()" etc.
        // We'll expose global functions that delegate to this module
    }

    // ============================================
    // Public API
    // ============================================

    /**
     * Sign a clinical note with full workflow support
     * Handles Initial Evaluation/Re-evaluation validation and Care Episode creation
     * @param {number} noteId - The clinical note ID to sign
     * @param {Object} options - Configuration options
     * @param {boolean} options.skipConfirmation - Skip confirmation for regular notes
     * @param {boolean} options.showSuccessToast - Show success toast (default: false)
     * @returns {Promise<{success: boolean, careEpisodeCreated: boolean, careEpisodeUpdated: boolean}>}
     */
    async sign(noteId, options = {}) {
        const { skipConfirmation = false, showSuccessToast = false } = options;

        // First, validate if this is an Initial Evaluation or Re-evaluation
        const validation = await this._validateNote(noteId);

        if (!validation.proceed) {
            return { success: false, careEpisodeCreated: false, careEpisodeUpdated: false };
        }

        // IM workflow: always use simple sign (no Care Episode flow)
        return this._signSimple(noteId, skipConfirmation, showSuccessToast);
    }

    /**
     * Confirm Care Episode creation (called from modal button)
     */
    confirmCareEpisode() {
        if (!this._confirmCallback) {
            console.error('[ClinicalNoteSignModule] No callback registered');
            return;
        }

        // Get edited values from form fields
        const expectedVisits = parseInt(document.getElementById('ceConfirmExpectedVisits')?.value) || null;
        const visitFrequency = parseInt(document.getElementById('ceConfirmVisitFrequency')?.value) || null;
        const durationWeeks = parseInt(document.getElementById('ceConfirmDuration')?.value) || null;
        const physicianName = document.getElementById('ceConfirmPhysicianName')?.value?.trim() || null;

        // Validate required fields
        if (!expectedVisits || expectedVisits <= 0) {
            this._showToast('Error', 'Please enter a valid number for Expected Visits', 'error');
            return;
        }
        if (!visitFrequency || visitFrequency <= 0) {
            this._showToast('Error', 'Please enter a valid number for Visit Frequency', 'error');
            return;
        }
        if (!durationWeeks || durationWeeks <= 0) {
            this._showToast('Error', 'Please enter a valid number for Duration', 'error');
            return;
        }

        const extraction = this._confirmExtraction || {};
        const callback = this._confirmCallback;

        // Clear state
        this._clearConfirmState();

        // Close modal
        this._hideModal('careEpisodeConfirmModal');

        // Call callback with confirmed data
        callback({
            DiagnosisCode: extraction.DiagnosisCode,
            DiagnosisDescription: extraction.DiagnosisDescription,
            Goals: extraction.Goals,
            TreatmentPlan: extraction.TreatmentPlan,
            PhysicianName: physicianName,
            ExpectedVisits: expectedVisits,
            VisitFrequency: visitFrequency,
            DurationWeeks: durationWeeks
        });
    }

    /**
     * Cancel Care Episode confirmation (called from modal button)
     */
    cancelCareEpisode() {
        const callback = this._confirmCallback;

        // Clear state
        this._clearConfirmState();

        // Close modal
        this._hideModal('careEpisodeConfirmModal');

        // Call callback with null to indicate cancellation
        if (callback) {
            callback(null);
        }
    }

    // ============================================
    // Validation Methods
    // ============================================

    /**
     * Validate Initial Evaluation or Re-evaluation note
     * @private
     * @param {number} noteId - The note ID to validate
     * @returns {Promise<Object>} Validation result
     */
    async _validateNote(noteId) {
        try {
            const result = await this._apiPost(`/clinical-notes/${noteId}/validate-initial-evaluation`);

            // If not an Initial Evaluation or Re-evaluation, proceed directly
            if (!result.IsInitialEvaluation && !result.IsReevaluation) {
                return { proceed: true, isInitialEval: false, isReevaluation: false };
            }

            // Check for Re-evaluation specific error (no care episode linked)
            if (result.IsReevaluation && result.ErrorMessage) {
                this._showToast('Error', result.ErrorMessage, 'error');
                return { proceed: false, isInitialEval: false, isReevaluation: true };
            }

            const extraction = result.ExtractionResult;

            // Check for missing fields
            if (!extraction.IsComplete) {
                this._showMissingFieldsModal(noteId, extraction.MissingFields, result.IsReevaluation);
                return { proceed: false, isInitialEval: result.IsInitialEvaluation, isReevaluation: result.IsReevaluation };
            }

            // Validate Expected Visits
            if (extraction.ExpectedVisits === null || extraction.ExpectedVisits === undefined) {
                this._showMissingFieldsModal(noteId, ['Expected Visits is required'], result.IsReevaluation);
                return { proceed: false, isInitialEval: result.IsInitialEvaluation, isReevaluation: result.IsReevaluation };
            }

            if (extraction.ExpectedVisits === 0) {
                this._showMissingFieldsModal(noteId, ['Expected Visits must be at least 1'], result.IsReevaluation);
                return { proceed: false, isInitialEval: result.IsInitialEvaluation, isReevaluation: result.IsReevaluation };
            }

            // Add re-evaluation context
            if (result.IsReevaluation) {
                extraction.CompletedVisits = result.CompletedVisits;
                extraction.CurrentExpectedVisits = result.CurrentExpectedVisits;
                extraction.CareEpisodeId = result.CareEpisodeId;
            }

            // Check insurance - skip for Self Pay
            if (extraction.IsSelfPay) {
                return { proceed: true, isInitialEval: result.IsInitialEvaluation, isReevaluation: result.IsReevaluation, extraction };
            }

            // No insurance found
            if (!extraction.HasInsurance) {
                return new Promise((resolve) => {
                    this._showNoInsuranceModal(noteId, () => {
                        resolve({ proceed: true, isInitialEval: result.IsInitialEvaluation, isReevaluation: result.IsReevaluation, extraction });
                    });
                });
            }

            // Check for visit mismatch
            if (extraction.HasVisitMismatch) {
                return new Promise((resolve) => {
                    this._showInsuranceMismatchModal(noteId, extraction, () => {
                        resolve({ proceed: true, isInitialEval: result.IsInitialEvaluation, isReevaluation: result.IsReevaluation, extraction });
                    });
                });
            }

            // All validations passed
            return { proceed: true, isInitialEval: result.IsInitialEvaluation, isReevaluation: result.IsReevaluation, extraction };

        } catch (error) {
            console.error('[ClinicalNoteSignModule] Validation error:', error);
            this._showToast('Error', 'Failed to validate evaluation note', 'error');
            return { proceed: false, isInitialEval: false, isReevaluation: false };
        }
    }

    // ============================================
    // Sign Methods
    // ============================================

    /**
     * Sign with Care Episode creation/update
     * @private
     */
    async _signWithCareEpisode(noteId, validation, showSuccessToast) {
        const isReevaluation = validation.isReevaluation;

        return new Promise((resolve) => {
            this._showCareEpisodeConfirmModal(noteId, validation.extraction, isReevaluation, async (confirmedData) => {
                if (!confirmedData) {
                    resolve({ success: false, careEpisodeCreated: false, careEpisodeUpdated: false });
                    return;
                }

                try {
                    const result = await this._apiPost(`/clinical-notes/${noteId}/sign-with-care-episode`, {
                        DiagnosisCode: confirmedData.DiagnosisCode || null,
                        DiagnosisDescription: confirmedData.DiagnosisDescription || null,
                        Goals: confirmedData.Goals || null,
                        TreatmentPlan: confirmedData.TreatmentPlan || null,
                        PhysicianName: confirmedData.PhysicianName || null,
                        ExpectedVisits: confirmedData.ExpectedVisits || null,
                        VisitFrequency: confirmedData.VisitFrequency || null,
                        DurationWeeks: confirmedData.DurationWeeks || null
                    });

                    const careEpisodeCreated = result.CareEpisodeCreated || false;
                    const careEpisodeUpdated = result.CareEpisodeUpdated || false;

                    if (showSuccessToast) {
                        if (careEpisodeCreated) {
                            this._showToast('Success', 'Note signed and Care Episode created successfully');
                        } else if (careEpisodeUpdated) {
                            this._showToast('Success', 'Note signed and Care Episode updated successfully');
                        } else {
                            this._showToast('Success', 'Note signed successfully');
                        }
                    }

                    this._emit('clinicalNoteSign:signed', { noteId, careEpisodeCreated, careEpisodeUpdated });
                    resolve({ success: true, careEpisodeCreated, careEpisodeUpdated });

                } catch (error) {
                    console.error('[ClinicalNoteSignModule] Sign error:', error);
                    this._showToast('Error', error.message || 'Failed to sign note', 'error');
                    resolve({ success: false, careEpisodeCreated: false, careEpisodeUpdated: false });
                }
            });
        });
    }

    /**
     * Simple sign for regular notes
     * @private
     */
    async _signSimple(noteId, skipConfirmation, showSuccessToast) {
        if (!skipConfirmation) {
            const confirmed = await this._showConfirmModal({
                title: 'Sign Clinical Note',
                message: 'Are you sure you want to sign this note? This action cannot be undone.',
                confirmText: 'Sign Note',
                confirmClass: 'btn-success',
                headerClass: 'bg-success text-white'
            });
            if (!confirmed) {
                return { success: false, careEpisodeCreated: false, careEpisodeUpdated: false };
            }
        }

        try {
            await this._apiPost(`/clinical-notes/${noteId}/sign`, {});

            if (showSuccessToast) {
                this._showToast('Success', 'Note signed successfully');
            }

            this._emit('clinicalNoteSign:signed', { noteId, careEpisodeCreated: false, careEpisodeUpdated: false });
            return { success: true, careEpisodeCreated: false, careEpisodeUpdated: false };

        } catch (error) {
            console.error('[ClinicalNoteSignModule] Sign error:', error);
            this._showToast('Error', error.message || 'Failed to sign note', 'error');
            return { success: false, careEpisodeCreated: false, careEpisodeUpdated: false };
        }
    }

    // ============================================
    // Modal Methods
    // ============================================

    /**
     * Show missing fields modal
     * @private
     */
    _showMissingFieldsModal(noteId, missingFields, isReevaluation = false) {
        const listEl = document.getElementById('ieMissingFieldsList');
        if (!listEl) {
            console.error('[ClinicalNoteSignModule] Missing fields modal not found');
            this._showToast('Error', 'Missing required fields: ' + missingFields.join(', '), 'error');
            return;
        }

        listEl.innerHTML = missingFields.map(field =>
            `<li class="list-group-item list-group-item-danger"><i class="bi bi-x-circle me-2"></i>${this._escape(field)}</li>`
        ).join('');

        const infoTextEl = document.getElementById('ieMissingFieldsInfoText');
        if (infoTextEl) {
            infoTextEl.textContent = isReevaluation
                ? 'These fields are required to update the Care Episode from the Re-evaluation note.'
                : 'These fields are required to create a Care Episode from the Initial Evaluation.';
        }

        // Detect open modals for proper navigation
        const editModal = document.getElementById('clinicalNoteModal');
        const tabbedModal = document.getElementById('tabbedClinicalNoteModal');

        const isEditModalOpen = editModal?.classList.contains('show');
        const isTabbedModalOpen = tabbedModal?.classList.contains('show');

        const editBtn = document.getElementById('ieEditNoteBtn');
        if (editBtn) {
            editBtn.onclick = async () => {
                this._hideModal('ieValidationMissingFieldsModal');

                if (isEditModalOpen || isTabbedModalOpen) {
                    setTimeout(() => this._cleanupBackdrops(), 200);
                } else {
                    await this._delay(150);
                    this._cleanupBackdrops();
                    if (typeof editClinicalNote === 'function') {
                        editClinicalNote(noteId);
                    }
                }
            };
        }

        this._showModal('ieValidationMissingFieldsModal');
    }

    /**
     * Show insurance mismatch modal
     * @private
     */
    _showInsuranceMismatchModal(noteId, extraction, onContinue) {
        const contentEl = document.getElementById('ieInsuranceMismatchContent');
        if (!contentEl) {
            console.error('[ClinicalNoteSignModule] Insurance mismatch modal not found');
            if (onContinue) onContinue();
            return;
        }

        const visitsOverLimit = extraction.VisitsOverLimit ||
            ((extraction.TotalVisitsNeeded || 0) - (extraction.InsuranceAllowedVisits || 0));

        contentEl.innerHTML = `
            <div class="alert alert-warning mb-3">
                <h6 class="mb-3"><i class="bi bi-exclamation-triangle me-2"></i>Expected Visits Exceed Insurance Coverage</h6>
                <table class="table table-borderless mb-0">
                    <tbody>
                        <tr>
                            <td class="text-muted" style="width: 200px;">Insurance Coverage:</td>
                            <td><strong>${this._escape(extraction.InsurancePayerName || 'Unknown Payer')}</strong></td>
                        </tr>
                        <tr>
                            <td class="text-muted">Allowed Visits:</td>
                            <td><span class="badge bg-primary fs-6">${extraction.InsuranceAllowedVisits || 0}</span></td>
                        </tr>
                        <tr>
                            <td class="text-muted">Initial Evaluation:</td>
                            <td><span class="badge bg-secondary fs-6">1</span></td>
                        </tr>
                        <tr>
                            <td class="text-muted">Expected Follow-up Visits:</td>
                            <td><span class="badge bg-secondary fs-6">${extraction.ExpectedVisits || 0}</span></td>
                        </tr>
                        <tr class="border-top">
                            <td class="text-muted pt-2"><strong>Total Visits Needed:</strong></td>
                            <td class="pt-2"><span class="badge bg-danger fs-6">${extraction.TotalVisitsNeeded || 'N/A'}</span></td>
                        </tr>
                        <tr>
                            <td class="text-muted"><strong>Over Limit By:</strong></td>
                            <td><span class="badge bg-danger fs-6">+${visitsOverLimit} visits</span></td>
                        </tr>
                    </tbody>
                </table>
            </div>
            <p class="text-muted mb-0">The total visits needed exceeds the patient's insurance coverage. You can edit the note to adjust the expected visits, or continue anyway.</p>
        `;

        // Detect open modals
        const editModal = document.getElementById('clinicalNoteModal');
        const tabbedModal = document.getElementById('tabbedClinicalNoteModal');

        const isEditModalOpen = editModal?.classList.contains('show');
        const isTabbedModalOpen = tabbedModal?.classList.contains('show');

        const editBtn = document.getElementById('ieInsuranceEditBtn');
        if (editBtn) {
            editBtn.onclick = async () => {
                this._hideModal('ieValidationInsuranceModal');

                if (isEditModalOpen || isTabbedModalOpen) {
                    setTimeout(() => this._cleanupBackdrops(), 200);
                } else {
                    await this._delay(150);
                    this._cleanupBackdrops();
                    if (typeof editClinicalNote === 'function') {
                        editClinicalNote(noteId);
                    }
                }
            };
        }

        const continueBtn = document.getElementById('ieInsuranceContinueBtn');
        if (continueBtn) {
            continueBtn.onclick = () => {
                this._hideModal('ieValidationInsuranceModal');
                if (onContinue) onContinue();
            };
        }

        this._showModal('ieValidationInsuranceModal');
    }

    /**
     * Show no insurance modal
     * @private
     */
    _showNoInsuranceModal(noteId, onContinue) {
        // Detect open modals
        const editModal = document.getElementById('clinicalNoteModal');
        const tabbedModal = document.getElementById('tabbedClinicalNoteModal');

        const isEditModalOpen = editModal?.classList.contains('show');
        const isTabbedModalOpen = tabbedModal?.classList.contains('show');

        const editBtn = document.getElementById('ieNoInsuranceEditBtn');
        if (editBtn) {
            editBtn.onclick = async () => {
                this._hideModal('ieValidationNoInsuranceModal');

                if (isEditModalOpen || isTabbedModalOpen) {
                    setTimeout(() => this._cleanupBackdrops(), 200);
                } else {
                    await this._delay(150);
                    this._cleanupBackdrops();
                    if (typeof editClinicalNote === 'function') {
                        editClinicalNote(noteId);
                    }
                }
            };
        }

        const continueBtn = document.getElementById('ieNoInsuranceContinueBtn');
        if (continueBtn) {
            continueBtn.onclick = () => {
                this._hideModal('ieValidationNoInsuranceModal');
                if (onContinue) onContinue();
            };
        }

        this._showModal('ieValidationNoInsuranceModal');
    }

    /**
     * Show Care Episode confirmation modal
     * @private
     */
    _showCareEpisodeConfirmModal(noteId, extraction, isReevaluation, callback) {
        const modal = document.getElementById('careEpisodeConfirmModal');
        if (!modal) {
            console.error('[ClinicalNoteSignModule] Care Episode confirmation modal not found');
            callback(null);
            return;
        }

        // Store state
        this._confirmCallback = callback;
        this._confirmExtraction = extraction || {};
        this._isReevaluation = isReevaluation;

        // Update modal content based on type
        const modalTitle = modal.querySelector('.modal-title');
        const confirmBtn = modal.querySelector('.btn-success');
        const alertInfo = modal.querySelector('.alert-info');

        if (isReevaluation) {
            if (modalTitle) modalTitle.innerHTML = '<i class="bi bi-arrow-repeat me-2"></i>Update Care Episode Plan';
            if (confirmBtn) confirmBtn.innerHTML = '<i class="bi bi-check-lg me-1"></i>Confirm and Update Care Episode';
            if (alertInfo) alertInfo.innerHTML = '<i class="bi bi-info-circle me-2"></i>Review and confirm the updated Care Episode plan from your Re-evaluation note.';
        } else {
            if (modalTitle) modalTitle.innerHTML = '<i class="bi bi-check-circle me-2"></i>Confirm Care Episode';
            if (confirmBtn) confirmBtn.innerHTML = '<i class="bi bi-check-lg me-1"></i>Confirm and Create Care Episode';
            if (alertInfo) alertInfo.innerHTML = '<i class="bi bi-info-circle me-2"></i>Review and confirm the Care Episode details before creation.';
        }

        // Pre-populate fields
        const expectedVisitsInput = document.getElementById('ceConfirmExpectedVisits');
        const visitFrequencyInput = document.getElementById('ceConfirmVisitFrequency');
        const durationInput = document.getElementById('ceConfirmDuration');
        const physicianInput = document.getElementById('ceConfirmPhysicianName');

        if (expectedVisitsInput) expectedVisitsInput.value = extraction?.ExpectedVisits || '';
        if (visitFrequencyInput) visitFrequencyInput.value = extraction?.VisitFrequency || '';
        if (durationInput) durationInput.value = extraction?.DurationWeeks || '';
        if (physicianInput) physicianInput.value = extraction?.PhysicianName || '';

        // Show/hide diagnosis section
        const diagnosisSection = document.getElementById('ceDiagnosisSection');
        const diagnosisSeparator = document.getElementById('ceDiagnosisSeparator');

        if (isReevaluation) {
            if (diagnosisSection) diagnosisSection.style.display = 'none';
            if (diagnosisSeparator) diagnosisSeparator.style.display = 'none';
        } else {
            if (diagnosisSection) diagnosisSection.style.display = 'block';
            if (diagnosisSeparator) diagnosisSeparator.style.display = 'block';
            const diagnosisEl = document.getElementById('ceConfirmDiagnosis');
            const diagnosisCodeEl = document.getElementById('ceConfirmDiagnosisCode');
            if (diagnosisEl) diagnosisEl.textContent = extraction?.DiagnosisDescription || 'Not extracted';
            if (diagnosisCodeEl) diagnosisCodeEl.textContent = extraction?.DiagnosisCode
                ? `ICD-10: ${extraction.DiagnosisCode}`
                : 'ICD-10 code not available';
        }

        // Handle Re-evaluation display
        const reevalInfoSection = document.getElementById('ceReevalInfo');
        if (reevalInfoSection) {
            if (isReevaluation) {
                const completedVisits = extraction?.CompletedVisits || 0;
                const currentExpectedVisits = extraction?.CurrentExpectedVisits || 0;
                const newAdditionalVisits = extraction?.ExpectedVisits || 0;

                reevalInfoSection.innerHTML = `
                    <div class="alert alert-secondary mb-3">
                        <h6 class="mb-2"><i class="bi bi-arrow-repeat me-2"></i>Re-evaluation Update Summary</h6>
                        <table class="table table-sm table-borderless mb-0">
                            <tbody>
                                <tr>
                                    <td>Current Completed Visits:</td>
                                    <td class="fw-semibold">${completedVisits}</td>
                                </tr>
                                <tr>
                                    <td>Current Expected Visits:</td>
                                    <td class="fw-semibold">${currentExpectedVisits}</td>
                                </tr>
                                <tr>
                                    <td>Additional Visits (from this Re-eval):</td>
                                    <td class="fw-semibold text-primary" id="ceReevalAdditionalVisits">${newAdditionalVisits}</td>
                                </tr>
                                <tr class="border-top">
                                    <td><strong>New Total Expected Visits:</strong></td>
                                    <td class="fw-bold text-success" id="ceReevalNewTotal">${currentExpectedVisits + newAdditionalVisits}</td>
                                </tr>
                            </tbody>
                        </table>
                    </div>
                `;
                reevalInfoSection.style.display = 'block';

                // Update labels
                const expectedVisitsLabel = document.querySelector('label[for="ceConfirmExpectedVisits"]');
                if (expectedVisitsLabel) expectedVisitsLabel.textContent = 'Additional Expected Visits';
                const expectedVisitsHelp = document.querySelector('#ceConfirmExpectedVisits + .form-text');
                if (expectedVisitsHelp) expectedVisitsHelp.textContent = 'Number of additional visits from this Re-evaluation';

                // Live update
                if (expectedVisitsInput) {
                    expectedVisitsInput.oninput = function() {
                        const additionalVisits = parseInt(this.value) || 0;
                        const additionalEl = document.getElementById('ceReevalAdditionalVisits');
                        const totalEl = document.getElementById('ceReevalNewTotal');
                        if (additionalEl) additionalEl.textContent = additionalVisits;
                        if (totalEl) totalEl.textContent = currentExpectedVisits + additionalVisits;
                    };
                }
            } else {
                reevalInfoSection.style.display = 'none';
                reevalInfoSection.innerHTML = '';

                // Reset labels
                const expectedVisitsLabel = document.querySelector('label[for="ceConfirmExpectedVisits"]');
                if (expectedVisitsLabel) expectedVisitsLabel.textContent = 'Expected Visits';
                const expectedVisitsHelp = document.querySelector('#ceConfirmExpectedVisits + .form-text');
                if (expectedVisitsHelp) expectedVisitsHelp.textContent = 'Total number of visits for this care episode';

                if (expectedVisitsInput) expectedVisitsInput.oninput = null;
            }
        }

        this._showModal('careEpisodeConfirmModal');
    }

    /**
     * Show generic confirmation modal
     * @private
     */
    _showConfirmModal(options = {}) {
        return new Promise((resolve) => {
            const {
                title = 'Confirm Action',
                message = 'Are you sure you want to proceed?',
                confirmText = 'Confirm',
                confirmClass = 'btn-primary',
                headerClass = ''
            } = options;

            const modalEl = document.getElementById('genericConfirmModal');
            const header = document.getElementById('genericConfirmHeader');
            const titleEl = document.getElementById('genericConfirmTitle');
            const messageEl = document.getElementById('genericConfirmMessage');
            const confirmBtn = document.getElementById('genericConfirmBtn');

            if (!modalEl || !titleEl || !messageEl || !confirmBtn) {
                // Fallback to native confirm
                resolve(window.confirm(message));
                return;
            }

            titleEl.textContent = title;
            messageEl.textContent = message;
            confirmBtn.textContent = confirmText;
            confirmBtn.className = `btn ${confirmClass}`;
            if (header) header.className = `modal-header ${headerClass}`;

            const handleConfirm = () => {
                confirmBtn.removeEventListener('click', handleConfirm);
                this._hideModal('genericConfirmModal');
                resolve(true);
            };

            confirmBtn.addEventListener('click', handleConfirm);

            const handleHidden = () => {
                confirmBtn.removeEventListener('click', handleConfirm);
                modalEl.removeEventListener('hidden.bs.modal', handleHidden);

                // Restore body.modal-open if another modal is still showing behind this one
                const otherOpenModals = document.querySelectorAll('.modal.show');
                if (otherOpenModals.length > 0) {
                    document.body.classList.add('modal-open');
                }

                resolve(false);
            };
            modalEl.addEventListener('hidden.bs.modal', handleHidden);

            this._showModal('genericConfirmModal');
        });
    }

    // ============================================
    // Helper Methods
    // ============================================

    /**
     * Clear confirmation state
     * @private
     */
    _clearConfirmState() {
        this._confirmCallback = null;
        this._confirmExtraction = null;
        this._isReevaluation = false;
    }

    /**
     * Handle confirm button click
     * @private
     */
    _handleConfirm() {
        this.confirmCareEpisode();
    }

    /**
     * Handle cancel button click
     * @private
     */
    _handleCancel() {
        this.cancelCareEpisode();
    }

    /**
     * Show a Bootstrap modal
     * @private
     */
    _showModal(modalId) {
        const modalEl = document.getElementById(modalId);
        if (modalEl) {
            const modal = bootstrap.Modal.getInstance(modalEl) || new bootstrap.Modal(modalEl);
            modal.show();

            // Ensure stacked modals appear above existing modals
            const openModals = document.querySelectorAll('.modal.show');
            if (openModals.length > 0) {
                modalEl.style.zIndex = '1070';
                setTimeout(() => {
                    const backdrops = document.querySelectorAll('.modal-backdrop');
                    if (backdrops.length > 1) {
                        backdrops[backdrops.length - 1].style.zIndex = '1069';
                    }
                }, 10);
            }
        }
    }

    /**
     * Hide a Bootstrap modal
     * @private
     */
    _hideModal(modalId) {
        const modalEl = document.getElementById(modalId);
        if (modalEl) {
            const modal = bootstrap.Modal.getInstance(modalEl);
            if (modal) modal.hide();
        }
    }

    /**
     * Cleanup orphaned modal backdrops
     * @private
     */
    _cleanupBackdrops() {
        const openModals = document.querySelectorAll('.modal.show');
        if (openModals.length === 0) {
            document.querySelectorAll('.modal-backdrop').forEach(backdrop => backdrop.remove());
            document.body.classList.remove('modal-open');
            document.body.style.removeProperty('overflow');
            document.body.style.removeProperty('padding-right');
        }
    }

    /**
     * Delay helper
     * @private
     */
    _delay(ms) {
        return new Promise(resolve => setTimeout(resolve, ms));
    }

    /**
     * Escape HTML
     * @private
     */
    _escape(str) {
        if (str == null) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    }

    /**
     * Show toast notification
     * @private
     */
    _showToast(title, message, type = 'success') {
        if (typeof showToast === 'function') {
            showToast(title, message, type);
        } else if (window.Toast) {
            switch (type) {
                case 'error':
                case 'danger':
                    Toast.error(title, message);
                    break;
                case 'warning':
                    Toast.warning(title, message);
                    break;
                default:
                    Toast.success(title, message);
            }
        } else {
            console.log(`[${type.toUpperCase()}] ${title}: ${message}`);
        }
    }

    /**
     * Emit event
     * @private
     */
    _emit(event, data = {}) {
        if (this.eventBus && typeof this.eventBus.emit === 'function') {
            this.eventBus.emit(event, data);
        }
        // Also dispatch DOM event for legacy handlers
        document.dispatchEvent(new CustomEvent(event, { detail: data }));
    }

    /**
     * API POST request
     * @private
     */
    async _apiPost(endpoint, body = null) {
        if (this.api && typeof this.api.post === 'function') {
            return this.api.post(endpoint, body);
        }

        // Fallback to global apiRequest
        if (typeof apiRequest === 'function') {
            return apiRequest(endpoint, { method: 'POST', body });
        }

        // Direct fetch fallback
        const token = localStorage.getItem('authToken');
        const response = await fetch(`/api${endpoint}`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Authorization': token ? `Bearer ${token}` : ''
            },
            body: body ? JSON.stringify(body) : null
        });

        if (!response.ok) {
            const error = await response.json().catch(() => ({ message: 'Request failed' }));
            throw new Error(error.message || error.Message || 'Request failed');
        }

        if (response.status === 204) return null;
        const text = await response.text();
        return text ? JSON.parse(text) : null;
    }
}

// ============================================
// Module Export & Global Instance
// ============================================

// Create singleton instance
const clinicalNoteSignModule = new ClinicalNoteSignModule();

// Export for module systems
if (typeof module !== 'undefined' && module.exports) {
    module.exports = { ClinicalNoteSignModule, clinicalNoteSignModule };
}

// Expose globally
window.ClinicalNoteSignModule = ClinicalNoteSignModule;
window.clinicalNoteSignModule = clinicalNoteSignModule;

// Global function bridges for modal buttons (onclick handlers)
window.confirmCareEpisodeCreation = function() {
    if (window.clinicalNoteSignModule) {
        window.clinicalNoteSignModule.confirmCareEpisode();
    }
};

window.cancelCareEpisodeConfirmation = function() {
    if (window.clinicalNoteSignModule) {
        window.clinicalNoteSignModule.cancelCareEpisode();
    }
};

// Convenience function for signing notes
window.signClinicalNoteCore = async function(noteId, options = {}) {
    if (window.clinicalNoteSignModule) {
        return window.clinicalNoteSignModule.sign(noteId, options);
    }
    return { success: false, careEpisodeCreated: false, careEpisodeUpdated: false };
};
