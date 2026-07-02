/**
 * AppointmentModule - Appointment management and scheduling
 *
 * Handles appointment CRUD, check-in, cancellation, and the
 * multi-step appointment wizard.
 *
 * @example
 *   const appointments = App.modules.get('appointments');
 *   await appointments.init();
 *   appointments.openNewAppointment();
 */
class AppointmentModule {
    /**
     * @param {Object} options - Module options
     * @param {Object} options.api - API service instance
     * @param {Object} options.eventBus - Event bus for cross-module communication
     */
    constructor(options = {}) {
        this.api = options.api || null;
        this.eventBus = options.eventBus || null;

        // State
        this.currentAppointment = null;
        this.isInitialized = false;

        // Wizard state
        this.wizardState = {
            currentStep: 1,
            maxStep: 4,
            isRecurringPath: false,
            selectedPatient: null,
            selectedType: null,
            selectedDuration: 45,
            scheduleType: 'single',
            selectedSlot: null,
            selectedProvider: null,
            hasActiveCareEpisode: false,
            careEpisodeId: null,
            recurringConfig: null,
            isEditMode: false,
            editingAppointmentId: null,
            isRescheduleMode: false
        };

        // Appointment type info
        this.appointmentTypes = [
            { id: 0, name: 'New Patient', color: '#4CAF50' },
            { id: 1, name: 'Follow-Up', color: '#2196F3' },
            { id: 2, name: 'Annual Physical', color: '#9C27B0' },
            { id: 3, name: 'Wellness', color: '#00BCD4' },
            { id: 4, name: 'Consultation', color: '#FF9800' },
            { id: 5, name: 'Telehealth', color: '#607D8B' },
            { id: 6, name: 'Procedure', color: '#E91E63' },
            { id: 7, name: 'Urgent', color: '#F44336' },
            { id: 8, name: 'Lab Review', color: '#795548' },
            { id: 9, name: 'Med Review', color: '#3F51B5' },
            { id: 10, name: 'New Longevity Patient', color: '#14B8A6' },
            { id: 11, name: 'Follow-Up Longevity Patient', color: '#D97706' },
        ];

        // Per-type default duration (minutes). Used to auto-tick the duration
        // radio when the user picks an appointment type that has a strong
        // expected length. Types not listed fall back to the modal's current
        // default. Longevity durations mirror the patient portal (60 / 45).
        this.defaultDurationByType = {
            10: 60,
            11: 45,
        };

        // Status info
        this.statuses = {
            0: { name: 'Scheduled', class: 'bg-secondary' },
            1: { name: 'Confirmed', class: 'bg-info' },
            2: { name: 'Checked In', class: 'bg-primary' },
            3: { name: 'In Progress', class: 'bg-warning text-dark' },
            4: { name: 'Completed', class: 'bg-success' },
            5: { name: 'Cancelled', class: 'bg-danger' },
            6: { name: 'Cancelled', class: 'bg-danger' },
            7: { name: 'No Show', class: 'bg-dark' },
            8: { name: 'Missed', class: 'bg-dark' }
        };

        // Bind methods
        this._handleWizardButtonClick = this._handleWizardButtonClick.bind(this);
        this._handleTypeCardClick = this._handleTypeCardClick.bind(this);
        this._handleDurationChange = this._handleDurationChange.bind(this);
    }

    /**
     * Initialize the module
     * @returns {Promise<void>}
     */
    async init() {
        this._bindEvents();
        this.isInitialized = true;
        this._emit('appointments:initialized');
    }

    /**
     * Bind event handlers
     * @private
     */
    _bindEvents() {
        // These will be bound when the modal opens, not during init
        // since the modal might not exist yet
    }

    /**
     * Bind modal-specific event handlers
     * @private
     */
    _bindModalEvents() {
        // Wizard navigation buttons
        document.querySelectorAll('[data-wizard-action]').forEach(btn => {
            // Remove existing listener to avoid duplicates
            btn.removeEventListener('click', this._handleWizardButtonClick);
            btn.addEventListener('click', this._handleWizardButtonClick);
        });

        // Type card selection
        document.querySelectorAll('.appt-type-card').forEach(card => {
            card.removeEventListener('click', this._handleTypeCardClick);
            card.addEventListener('click', this._handleTypeCardClick);
        });

        // Duration radio buttons
        document.querySelectorAll('input[name="wizardDuration"]').forEach(radio => {
            radio.removeEventListener('change', this._handleDurationChange);
            radio.addEventListener('change', this._handleDurationChange);
        });

        // Patient search
        const searchInput = document.getElementById('patientSearchInput');
        if (searchInput) {
            const debouncedSearch = this._debounce((e) => {
                this._searchPatients(e.target.value);
            }, 300);

            // Remove existing listener
            searchInput.removeEventListener('input', searchInput._searchHandler);
            // Add new listener and store reference
            searchInput._searchHandler = debouncedSearch;
            searchInput.addEventListener('input', debouncedSearch);
        }

        // Slot filters - date picker and provider select
        const dateInput = document.getElementById('apptDatePicker');
        if (dateInput) {
            dateInput.removeEventListener('change', dateInput._slotChangeHandler);
            dateInput._slotChangeHandler = () => this._loadAvailableSlots();
            dateInput.addEventListener('change', dateInput._slotChangeHandler);
        }

        const providerSelect = document.getElementById('apptProviderSelect');
        if (providerSelect) {
            providerSelect.removeEventListener('change', providerSelect._slotChangeHandler);
            providerSelect._slotChangeHandler = (e) => {
                // Update selected provider in wizard state
                const providerId = e.target.value ? parseInt(e.target.value) : null;
                this.wizardState.selectedProvider = providerId;
                console.log('[AppointmentModule] Provider dropdown changed to:', providerId, e.target.value);

                // FIX: Ensure the dropdown visually reflects the selection
                e.target.value = e.target.value; // Force UI update

                // Clear selected slot since we're changing providers
                this.wizardState.selectedSlot = null;

                // Reload slots with new provider filter
                this._loadAvailableSlots();
            };
            providerSelect.addEventListener('change', providerSelect._slotChangeHandler);
        }

        // FIX: Add listeners for custom date/time to enable Next button
        const customTimeInput = document.getElementById('apptCustomTime');
        if (customTimeInput) {
            customTimeInput.removeEventListener('change', customTimeInput._timeChangeHandler);
            customTimeInput._timeChangeHandler = () => {
                console.log('[AppointmentModule] Custom time changed');
                this._updateWizardButtons();
            };
            customTimeInput.addEventListener('change', customTimeInput._timeChangeHandler);
        }

        const datePickerInput = document.getElementById('apptDatePicker');
        if (datePickerInput) {
            datePickerInput.removeEventListener('input', datePickerInput._dateInputHandler);
            datePickerInput._dateInputHandler = () => {
                console.log('[AppointmentModule] Date picker changed');
                this._updateWizardButtons();
            };
            datePickerInput.addEventListener('input', datePickerInput._dateInputHandler);
        }

        // FIX: Form submission handler
        const form = document.getElementById('appointmentForm');
        if (form) {
            form.removeEventListener('submit', form._submitHandler);
            form._submitHandler = (e) => {
                e.preventDefault();
                console.log('[AppointmentModule] Form submitted');
                this._submitAppointment();
            };
            form.addEventListener('submit', form._submitHandler);
        }
    }

    /**
     * Handle duration change
     * @private
     */
    _handleDurationChange(e) {
        this.wizardState.selectedDuration = parseInt(e.target.value);
        this._updateWizardSummary();
    }

    /**
     * Handle wizard button clicks
     * @private
     * @param {Event} e - Click event
     */
    _handleWizardButtonClick(e) {
        const action = e.target.closest('[data-wizard-action]')?.dataset.wizardAction;
        if (!action) return;

        switch (action) {
            case 'next':
                this._wizardNext();
                break;
            case 'back':
                this._wizardBack();
                break;
            case 'submit':
                this._submitAppointment();
                break;
        }
    }

    /**
     * Handle type card selection
     * @private
     * @param {Event} e - Click event
     */
    _handleTypeCardClick(e) {
        const card = e.target.closest('.appt-type-card');
        if (!card) return;

        const typeId = parseInt(card.dataset.type);

        // Remove selection from other cards
        document.querySelectorAll('.appt-type-card').forEach(c => c.classList.remove('selected'));
        card.classList.add('selected');

        this.wizardState.selectedType = typeId;

        // Set default duration based on appointment type.
        // Explicit overrides win (longevity types 10 = 60 min, 11 = 45 min,
        // matching the patient portal). Otherwise: New Patient (0) and
        // Annual Physical (2) = 60 min; everything else = 30 min.
        const defaultDuration = this.defaultDurationByType[typeId]
            ?? ((typeId === 0 || typeId === 2) ? 60 : 30);
        this.wizardState.selectedDuration = defaultDuration;

        // Update the duration radio button selection
        const durationRadio = document.getElementById(`dur${defaultDuration}`);
        if (durationRadio) {
            durationRadio.checked = true;
        }

        // Show duration selection
        const durationSection = document.getElementById('durationSelectionSection');
        if (durationSection) {
            durationSection.classList.remove('d-none');
        }

        this._updateWizardSummary();
        this._updateWizardButtons();
    }

    // === Public Methods ===

    /**
     * Open new appointment wizard
     * @param {Date} start - Optional start time
     * @param {Date} end - Optional end time
     */
    async openNewAppointment(start, end) {
        // MA/Nurse cannot create appointments
        const _user = JSON.parse(localStorage.getItem('currentUser') || '{}');
        const _role = parseInt(_user.Role ?? _user.role ?? -1);
        if (UserRoles.isMaNurse(_role)) {
            if (window.showToast) window.showToast('Your role does not have permission to schedule appointments', 'warning');
            return;
        }

        this._resetWizardState();

        // Update modal title
        const modalTitle = document.querySelector('#appointmentModal .modal-title');
        if (modalTitle) {
            modalTitle.innerHTML = '<i class="bi bi-calendar-plus me-2"></i>Schedule Appointment';
        }

        // Load providers
        await this._loadProviderDropdown('apptProviderSelect');

        // Load recent patients
        await this._loadRecentPatients();

        // Bind modal event handlers
        this._bindModalEvents();

        // Show wizard at step 1
        this._wizardShowStep(1);
        this._showModal('appointmentModal');
    }

    /**
     * Open appointment details modal
     * @param {number} appointmentId - Appointment ID
     * @param {Object} options - Options (viewOnly, source)
     */
    async openDetails(appointmentId, options = {}) {
        console.log('[AppointmentModule] openDetails called with ID:', appointmentId);
        const { viewOnly = false } = options;

        try {
            console.log('[AppointmentModule] Fetching appointment from API...');
            const appt = await this._apiGet(`/appointments/${appointmentId}`);
            console.log('[AppointmentModule] API response:', appt);

            if (!appt) {
                console.error('[AppointmentModule] No appointment data returned from API');
                return;
            }

            this.currentAppointment = appt;
            console.log('[AppointmentModule] Rendering detail modal...');
            this._renderDetailModal(appt, viewOnly);

            console.log('[AppointmentModule] Showing modal...');
            this._showModal('appointmentDetailModal');

            console.log('[AppointmentModule] Modal should now be visible');
            this._emit('appointments:detailsOpened', { appointment: appt });
        } catch (error) {
            console.error('[AppointmentModule] Failed to load appointment:', error);
            this._showError('Failed to load appointment details');
        }
    }

    /**
     * Edit appointment
     * @param {number} appointmentId - Appointment ID
     */
    async edit(appointmentId) {
        // MA/Nurse cannot edit appointments
        const _user = JSON.parse(localStorage.getItem('currentUser') || '{}');
        const _role = parseInt(_user.Role ?? _user.role ?? -1);
        if (UserRoles.isMaNurse(_role)) {
            if (window.showToast) window.showToast('Your role does not have permission to edit appointments', 'warning');
            return;
        }

        try {
            const appt = await this._apiGet(`/appointments/${appointmentId}`);
            if (!appt) return;

            this._hideModal('appointmentDetailModal');

            // Wait for modal to close
            await new Promise(resolve => setTimeout(resolve, 300));

            this._resetWizardState();
            this.wizardState.isEditMode = true;
            this.wizardState.editingAppointmentId = appointmentId;

            // Pre-populate wizard with appointment data.
            // PatientHasProfilePicture lives on the appointment DTO so the
            // "Selected Patient" card can render the patient's avatar.
            this.wizardState.selectedPatient = {
                PatientId: appt.PatientId,
                FullName: appt.PatientName,
                Mrn: appt.PatientMRN,
                MRN: appt.PatientMRN,
                HasProfilePicture: appt.PatientHasProfilePicture
            };
            this.wizardState.selectedType = appt.Type;
            this.wizardState.selectedProvider = appt.ProviderId;
            this.wizardState.selectedDuration = this._calculateDuration(appt.StartTime, appt.EndTime);
            this.wizardState.scheduleType = 'single';
            this.wizardState.isRecurringPath = false;

            // Store original appointment times for editing
            this.editingAppointment = appt;

            // Set hidden fields
            const appointmentIdField = document.getElementById('appointmentId');
            if (appointmentIdField) appointmentIdField.value = appointmentId;

            const patientIdField = document.getElementById('apptPatientId');
            if (patientIdField) patientIdField.value = appt.PatientId;

            // Update modal title
            const modalTitle = document.querySelector('#appointmentModal .modal-title');
            if (modalTitle) {
                modalTitle.innerHTML = '<i class="bi bi-pencil me-2"></i>Edit Appointment';
            }

            // Hide recurring section when editing (can't convert single to recurring)
            const recurringSection = document.getElementById('recurringConfigSection');
            if (recurringSection) recurringSection.classList.add('d-none');
            const recurringPanel = document.getElementById('recurringConfigPanel');
            if (recurringPanel) recurringPanel.classList.add('d-none');

            // EDIT MODE: Check for active Care Episode (same logic as new appointment)
            try {
                const activeEpisode = await this._apiGetSilent(`/care-episodes/patient/${appt.PatientId}/active`);
                if (activeEpisode && activeEpisode.CareEpisodeId) {
                    this.wizardState.hasActiveCareEpisode = true;
                    this.wizardState.careEpisodeId = activeEpisode.CareEpisodeId;
                    console.log('[AppointmentModule] Edit mode - Patient has active Care Episode:', activeEpisode.CareEpisodeId);
                } else {
                    this.wizardState.hasActiveCareEpisode = false;
                    this.wizardState.careEpisodeId = null;
                    console.log('[AppointmentModule] Edit mode - Patient has no active Care Episode');
                }
            } catch (err) {
                this.wizardState.hasActiveCareEpisode = false;
                this.wizardState.careEpisodeId = null;
                console.log('[AppointmentModule] Edit mode - No active Care Episode found for patient');
            }

            // Update type cards availability based on Care Episode status
            this._updateTypeCardsAvailability();

            // Pre-select the appointment type
            this._selectTypeCard(appt.Type);

            // Pre-select duration
            const durationId = `dur${this.wizardState.selectedDuration}`;
            const durationRadio = document.getElementById(durationId);
            if (durationRadio) durationRadio.checked = true;

            // Show the selected patient display
            this._displaySelectedPatient(this.wizardState.selectedPatient);

            // Load providers for dropdown
            await this._loadProviders('apptProviderSelect', appt.ProviderId);

            // Set notes if available
            const notesField = document.getElementById('apptNotes');
            if (notesField) notesField.value = appt.Reason || '';

            // EDIT MODE: Pre-fill date and time fields with existing appointment data
            const locationTz = window.getCurrentLocationTimezone ? window.getCurrentLocationTimezone() : { timeZoneId: 'America/Chicago' };
            const tzId = appt.TimeZoneId || locationTz.timeZoneId || 'America/Chicago';

            // Format date for date picker (YYYY-MM-DD)
            // Use parseServerDateTime to correctly interpret server UTC times
            // (server returns DateTime without 'Z' suffix; raw new Date() would treat as local).
            const startDate = window.parseServerDateTime
                ? window.parseServerDateTime(appt.StartTime)
                : new Date(appt.StartTime);
            let formattedDate;
            if (window.formatDateForInputInTimezone) {
                formattedDate = window.formatDateForInputInTimezone(appt.StartTime, tzId);
            } else {
                // Fallback
                const year = startDate.toLocaleString('en-US', { year: 'numeric', timeZone: tzId });
                const month = startDate.toLocaleString('en-US', { month: '2-digit', timeZone: tzId });
                const day = startDate.toLocaleString('en-US', { day: '2-digit', timeZone: tzId });
                formattedDate = `${year}-${month}-${day}`;
            }

            // Format time for time picker (HH:MM in 24h format)
            let formattedTime;
            if (window.formatTimeForInputInTimezone) {
                formattedTime = window.formatTimeForInputInTimezone(appt.StartTime, tzId);
            } else {
                // Fallback
                const hours = startDate.toLocaleString('en-US', { hour: '2-digit', hour12: false, timeZone: tzId });
                const minutes = startDate.toLocaleString('en-US', { minute: '2-digit', timeZone: tzId });
                formattedTime = `${hours.padStart(2, '0')}:${minutes.padStart(2, '0')}`;
            }

            // Set date picker
            const datePicker = document.getElementById('apptDatePicker');
            if (datePicker) datePicker.value = formattedDate;

            // Set custom time field
            const customTime = document.getElementById('apptCustomTime');
            if (customTime) customTime.value = formattedTime;

            // Set hidden form fields
            const apptDate = document.getElementById('apptDate');
            if (apptDate) apptDate.value = formattedDate;

            const apptStartTime = document.getElementById('apptStartTime');
            if (apptStartTime) apptStartTime.value = formattedTime;

            // EDIT MODE: Pre-select the original slot so "Next" works immediately
            this.wizardState.selectedSlot = {
                start: appt.StartTime,
                end: appt.EndTime,
                providerId: appt.ProviderId,
                providerName: appt.ProviderName,
                providerColor: appt.ProviderColor,
                locationName: appt.LocationName,
                locationId: appt.LocationId,
                timeZoneAbbreviation: appt.TimeZoneAbbreviation,
                timeZoneId: appt.TimeZoneId,
                IsAvailable: true
            };

            // Bind modal event handlers (important for form submission)
            this._bindModalEvents();

            // Show modal and go to type step (step 2)
            this._showModal('appointmentModal');
            this.wizardShowStep(2);

            this._emit('appointments:editing', { appointment: appt });
        } catch (error) {
            console.error('[AppointmentModule] Failed to load appointment for edit:', error);
            this._showError('Failed to load appointment');
        }
    }

    /**
     * Calculate duration in minutes between two times
     * @private
     * @param {string} startTime - Start time ISO string
     * @param {string} endTime - End time ISO string
     * @returns {number} Duration in minutes
     */
    _calculateDuration(startTime, endTime) {
        try {
            const start = new Date(startTime);
            const end = new Date(endTime);
            const diffMs = end - start;
            return Math.round(diffMs / (1000 * 60));
        } catch {
            return 45; // Default duration
        }
    }

    /**
     * Select a type card visually
     * @private
     * @param {number} type - Appointment type
     */
    _selectTypeCard(type) {
        // Remove selection from all cards
        document.querySelectorAll('.appt-type-card').forEach(card => {
            card.classList.remove('selected');
        });

        // Find and select the matching card
        const typeCard = document.querySelector(`.appt-type-card[data-type="${type}"]`);
        if (typeCard) {
            typeCard.classList.add('selected');
        }

        // Set default duration based on appointment type
        // Type 0 (New Patient Visit) and Type 2 (Annual Physical) = 60 min
        // Other types = 45 min
        const defaultDuration = (type === 0 || type === 2) ? 60 : 45;
        this.wizardState.selectedDuration = defaultDuration;

        // Update the duration radio button selection
        const durationRadio = document.getElementById(`dur${defaultDuration}`);
        if (durationRadio) {
            durationRadio.checked = true;
        }

        // Show duration selection
        const durationSection = document.getElementById('durationSelectionSection');
        if (durationSection) durationSection.classList.remove('d-none');
    }

    /**
     * Display selected patient in the wizard
     * @private
     * @param {Object} patient - Patient data
     */
    _displaySelectedPatient(patient) {
        // Hide search container
        const searchContainer = document.getElementById('apptPatientContainer');
        if (searchContainer) searchContainer.classList.add('d-none');

        // Hide recent patients
        const recentSection = document.getElementById('recentPatientsSection');
        if (recentSection) recentSection.classList.add('d-none');

        // Show selected patient display.
        // Avatar pulled from AvatarUtils so it matches the look used in the
        // patient list / dashboard. `HasProfilePicture` arrives on the patient
        // payload from the search API; fall back to initials if absent.
        const selectedDisplay = document.getElementById('selectedPatientDisplay');
        if (selectedDisplay) {
            selectedDisplay.classList.remove('d-none');

            const nameEl = selectedDisplay.querySelector('#selectedPatientName');
            const infoEl = selectedDisplay.querySelector('#selectedPatientInfo');
            const avatarHost = selectedDisplay.querySelector('#selectedPatientAvatar');

            const fullName = patient.FullName || patient.Name || 'Unknown';
            if (nameEl) nameEl.textContent = fullName;
            if (infoEl) infoEl.textContent = `MRN: ${patient.Mrn || patient.MRN || 'N/A'}`;

            if (avatarHost && window.AvatarUtils) {
                avatarHost.innerHTML = AvatarUtils.renderPatientAvatar({
                    patientId: patient.PatientId,
                    name: fullName,
                    hasProfilePicture: patient.HasProfilePicture,
                    size: 'md'
                });
            }
        }
    }

    /**
     * Check in patient
     * @param {number} appointmentId - Appointment ID
     * @param {number|null} copayCollected - Copay amount collected (null if skipped)
     */
    async checkIn(appointmentId, copayCollected = null) {
        try {
            await this._apiPost(`/appointments/${appointmentId}/checkin`, {
                CopayCollected: copayCollected
            });

            this._showSuccess('Patient checked in');

            // Re-render detail modal immediately so buttons reflect new status
            if (this.currentAppointment && this.currentAppointment.AppointmentId === appointmentId) {
                this.currentAppointment.Status = 2; // CheckedIn
                this._renderDetailModal(this.currentAppointment, false);
            }

            this._emit('appointments:checkedIn', { appointmentId });
        } catch (error) {
            console.error('[AppointmentModule] Check-in error:', error);
            this._showError('Failed to check in patient');
        }
    }

    /**
     * Cancel appointment
     * @param {number} appointmentId - Appointment ID
     * @param {string} reason - Cancellation reason (optional, will be taken from modal if not provided)
     */
    async cancel(appointmentId, reason = '') {
        try {
            await this._apiPost(`/appointments/${appointmentId}/cancel`, {
                Reason: reason
            });

            this._showSuccess('Appointment cancelled');
            this._hideModal('appointmentDetailModal');
            this._hideModal('cancelAppointmentModal');
            this._emit('appointments:cancelled', { appointmentId });
        } catch (error) {
            console.error('[AppointmentModule] Cancel error:', error);
            this._showError('Failed to cancel appointment');
        }
    }

    /**
     * Confirm cancel appointment (called from modal button)
     */
    confirmCancel() {
        const appointmentId = document.getElementById('cancelAppointmentId')?.value;
        const reason = document.getElementById('cancellationReason')?.value?.trim();

        if (!appointmentId) {
            this._showError('No appointment selected');
            return;
        }

        if (!reason) {
            const reasonField = document.getElementById('cancellationReason');
            if (reasonField) {
                reasonField.classList.add('is-invalid');
            }
            this._showError('Please provide a cancellation reason');
            return;
        }

        // Clear validation state
        const reasonField = document.getElementById('cancellationReason');
        if (reasonField) {
            reasonField.classList.remove('is-invalid');
        }

        this.cancel(parseInt(appointmentId), reason);
    }

    /**
     * Reinstate cancelled appointment
     * @param {number} appointmentId - Appointment ID
     */
    async reinstate(appointmentId) {
        try {
            await this._apiPost(`/appointments/${appointmentId}/reinstate`);

            this._showSuccess('Appointment reinstated');
            this._hideModal('appointmentDetailModal');
            this._hideModal('reinstateAppointmentModal');
            this._emit('appointments:reinstated', { appointmentId });
        } catch (error) {
            console.error('[AppointmentModule] Reinstate error:', error);
            this._showError('Failed to reinstate appointment');
        }
    }

    /**
     * Confirm reinstate appointment (called from modal button)
     */
    confirmReinstate() {
        const appointmentId = document.getElementById('reinstateAppointmentId')?.value;

        if (!appointmentId) {
            this._showError('No appointment selected');
            return;
        }

        this.reinstate(parseInt(appointmentId));
    }

    /**
     * Reschedule from a missed appointment
     * Opens the appointment wizard pre-filled with patient, type, and duration from original appointment
     * Jumps directly to the Schedule step (step 3)
     * @param {number} originalAppointmentId - The missed appointment ID
     * @param {number} patientId - Patient ID
     * @param {number} providerId - Provider ID (optional)
     */
    async rescheduleFromMissed(originalAppointmentId, patientId, providerId = null) {
        console.log('[AppointmentModule] Rescheduling from missed appointment:', originalAppointmentId);

        try {
            // Hide detail modal if open
            this._hideModal('appointmentDetailModal');

            // Wait for modal to close
            await new Promise(resolve => setTimeout(resolve, 300));

            // Load original appointment details
            const originalAppt = await this._apiGet(`/appointments/${originalAppointmentId}`);
            if (!originalAppt) {
                this._showError('Failed to load original appointment');
                return;
            }

            // Load patient info
            const patient = await this._apiGet(`/patients/${patientId}`);
            if (!patient) {
                this._showError('Failed to load patient information');
                return;
            }

            // Reset and set up wizard for reschedule
            this._resetWizardState();
            this.wizardState.isEditMode = false;
            this.wizardState.isRescheduleMode = true;

            // Pre-fill from original appointment (HasProfilePicture so the
            // selected-patient avatar renders correctly).
            this.wizardState.selectedPatient = {
                PatientId: originalAppt.PatientId,
                FullName: originalAppt.PatientName,
                Mrn: originalAppt.PatientMRN,
                MRN: originalAppt.PatientMRN,
                HasProfilePicture: originalAppt.PatientHasProfilePicture
            };
            this.wizardState.selectedType = originalAppt.Type;
            this.wizardState.selectedDuration = this._calculateDuration(originalAppt.StartTime, originalAppt.EndTime);
            this.wizardState.scheduleType = 'single';
            this.wizardState.isRecurringPath = false;

            // If provider is specified, pre-select it
            if (providerId) {
                this.wizardState.selectedProvider = providerId;
            }

            // Set the reschedule context
            const rescheduleField = document.getElementById('rescheduleFromAppointmentId');
            if (rescheduleField) {
                rescheduleField.value = originalAppointmentId;
            }

            // Set hidden patient ID field
            const patientIdField = document.getElementById('apptPatientId');
            if (patientIdField) patientIdField.value = patientId;

            // Check for active Care Episode
            try {
                const activeEpisode = await this._apiGetSilent(`/care-episodes/patient/${patientId}/active`);
                if (activeEpisode && activeEpisode.CareEpisodeId) {
                    this.wizardState.hasActiveCareEpisode = true;
                    this.wizardState.careEpisodeId = activeEpisode.CareEpisodeId;
                } else {
                    this.wizardState.hasActiveCareEpisode = false;
                    this.wizardState.careEpisodeId = null;
                }
            } catch (err) {
                this.wizardState.hasActiveCareEpisode = false;
                this.wizardState.careEpisodeId = null;
            }

            // Bind modal event handlers (important for form submission)
            this._bindModalEvents();

            // Open wizard modal
            this._showModal('appointmentModal');

            // Update modal title to indicate reschedule
            const modalTitle = document.querySelector('#appointmentModal .modal-title');
            if (modalTitle) {
                modalTitle.innerHTML = '<i class="bi bi-arrow-repeat me-2"></i>Reschedule Appointment';
            }

            // Hide recurring section for reschedule
            const recurringSection = document.getElementById('recurringConfigSection');
            if (recurringSection) recurringSection.classList.add('d-none');
            const recurringPanel = document.getElementById('recurringConfigPanel');
            if (recurringPanel) recurringPanel.classList.add('d-none');

            // Display selected patient
            this._displaySelectedPatient(this.wizardState.selectedPatient);

            // Update type cards availability and select the original type
            this._updateTypeCardsAvailability();
            this._selectTypeCard(originalAppt.Type);

            // Pre-select duration
            const durationId = `dur${this.wizardState.selectedDuration}`;
            const durationRadio = document.getElementById(durationId);
            if (durationRadio) durationRadio.checked = true;

            // Show duration selection section
            const durationSection = document.getElementById('durationSelectionSection');
            if (durationSection) durationSection.classList.remove('d-none');

            // Load providers for dropdown
            await this._loadProviders('apptProviderSelect', providerId || originalAppt.ProviderId);

            // Set notes if available (reason from original)
            const notesField = document.getElementById('apptNotes');
            if (notesField) notesField.value = originalAppt.Reason || '';

            // Jump directly to step 3 (Schedule) - patient and type are pre-filled
            this.wizardShowStep(3);

            this._showSuccess('Select a new date and time for this appointment.');
        } catch (error) {
            console.error('[AppointmentModule] Reschedule error:', error);
            this._showError('Failed to start reschedule process');
        }
    }

    /**
     * Reschedule any appointment (handles both future and past appointments)
     * - Future appointments: Cancels the original, then opens wizard to create new appointment
     * - Past appointments: Marks as missed, then opens wizard to create new appointment
     * @param {number} appointmentId - The appointment ID to reschedule
     */
    async rescheduleAppointment(appointmentId) {
        console.log('[AppointmentModule] Rescheduling appointment:', appointmentId);

        try {
            // Call the API to process the reschedule (cancel or mark as missed)
            const response = await this._apiPost(`/appointments/${appointmentId}/reschedule`);

            if (!response || !response.Success) {
                this._showError(response?.Message || 'Failed to reschedule appointment');
                return;
            }

            console.log('[AppointmentModule] Reschedule response:', response);

            // Hide detail modal if open
            this._hideModal('appointmentDetailModal');

            // Wait for modal to close
            await new Promise(resolve => setTimeout(resolve, 300));

            // Reset and set up wizard for reschedule
            this._resetWizardState();
            this.wizardState.isEditMode = false;
            this.wizardState.isRescheduleMode = true;

            // Pre-fill from the API response (HasProfilePicture so the
            // selected-patient avatar renders correctly).
            this.wizardState.selectedPatient = {
                PatientId: response.PatientId,
                FullName: response.PatientName,
                Mrn: response.PatientMRN,
                MRN: response.PatientMRN,
                HasProfilePicture: response.PatientHasProfilePicture
            };
            this.wizardState.selectedType = response.Type;

            // Calculate duration from original times
            const startTime = new Date(response.OriginalStartTime);
            const endTime = new Date(response.OriginalEndTime);
            const durationMinutes = Math.round((endTime - startTime) / (1000 * 60));
            this.wizardState.selectedDuration = durationMinutes || 45;

            this.wizardState.scheduleType = 'single';
            this.wizardState.isRecurringPath = false;
            this.wizardState.careEpisodeId = response.CareEpisodeId;

            // Pre-select provider if available
            if (response.ProviderId) {
                this.wizardState.selectedProvider = response.ProviderId;
            }

            // Set the reschedule context - link to original appointment
            const rescheduleField = document.getElementById('rescheduleFromAppointmentId');
            if (rescheduleField) {
                rescheduleField.value = response.OriginalAppointmentId;
            }

            // Set hidden patient ID field
            const patientIdField = document.getElementById('apptPatientId');
            if (patientIdField) patientIdField.value = response.PatientId;

            // Check for active Care Episode
            if (response.CareEpisodeId) {
                this.wizardState.hasActiveCareEpisode = true;
            } else {
                try {
                    const activeEpisode = await this._apiGetSilent(`/care-episodes/patient/${response.PatientId}/active`);
                    if (activeEpisode && activeEpisode.CareEpisodeId) {
                        this.wizardState.hasActiveCareEpisode = true;
                        this.wizardState.careEpisodeId = activeEpisode.CareEpisodeId;
                    } else {
                        this.wizardState.hasActiveCareEpisode = false;
                    }
                } catch (err) {
                    this.wizardState.hasActiveCareEpisode = false;
                }
            }

            // Bind modal event handlers (important for form submission)
            this._bindModalEvents();

            // Open wizard modal
            this._showModal('appointmentModal');

            // Update modal title to indicate reschedule
            const modalTitle = document.querySelector('#appointmentModal .modal-title');
            if (modalTitle) {
                modalTitle.innerHTML = '<i class="bi bi-arrow-repeat me-2"></i>Reschedule Appointment';
            }

            // Hide recurring section for reschedule
            const recurringSection = document.getElementById('recurringConfigSection');
            if (recurringSection) recurringSection.classList.add('d-none');
            const recurringPanel = document.getElementById('recurringConfigPanel');
            if (recurringPanel) recurringPanel.classList.add('d-none');

            // Display selected patient
            this._displaySelectedPatient(this.wizardState.selectedPatient);

            // Update type cards availability and select the original type
            this._updateTypeCardsAvailability();
            this._selectTypeCard(response.Type);

            // Pre-select duration
            const durationId = `dur${this.wizardState.selectedDuration}`;
            const durationRadio = document.getElementById(durationId);
            if (durationRadio) durationRadio.checked = true;

            // Show duration selection section
            const durationSection = document.getElementById('durationSelectionSection');
            if (durationSection) durationSection.classList.remove('d-none');

            // Load providers for dropdown
            await this._loadProviders('apptProviderSelect', response.ProviderId);

            // Set notes if available (reason from original)
            const notesField = document.getElementById('apptNotes');
            if (notesField) notesField.value = response.Reason || '';

            // Jump directly to step 3 (Schedule) - patient and type are pre-filled
            this.wizardShowStep(3);

            // Show appropriate success message
            const action = response.WasCancelled ? 'cancelled' : 'marked as missed';
            this._showSuccess(`Original appointment ${action}. Select a new date and time.`);

        } catch (error) {
            console.error('[AppointmentModule] Reschedule error:', error);
            this._showError(error.message || 'Failed to reschedule appointment');
        }
    }

    // === Wizard Methods ===

    /**
     * Reset wizard state
     * @private
     */
    _resetWizardState() {
        this.wizardState = {
            currentStep: 1,
            maxStep: 4,
            isRecurringPath: false,
            selectedPatient: null,
            selectedType: null,
            selectedDuration: 45,
            scheduleType: 'single',
            selectedSlot: null,
            selectedProvider: null,
            hasActiveCareEpisode: false,
            careEpisodeId: null,
            recurringConfig: null,
            isEditMode: false,
            editingAppointmentId: null,
            isRescheduleMode: false
        };

        // Reset form
        const form = document.getElementById('appointmentForm');
        if (form) form.reset();

        // Reset hidden fields
        ['appointmentId', 'apptPatientId', 'rescheduleFromAppointmentId'].forEach(id => {
            const el = document.getElementById(id);
            if (el) el.value = '';
        });

        // Reset patient search
        const searchInput = document.getElementById('patientSearchInput');
        if (searchInput) searchInput.value = '';

        document.getElementById('patientSearchResults')?.classList.add('d-none');
        document.getElementById('selectedPatientDisplay')?.classList.add('d-none');
        document.getElementById('apptPatientContainer')?.classList.remove('d-none');
        document.getElementById('recentPatientsSection')?.classList.remove('d-none');

        // Reset type cards - remove selection AND reset availability
        document.querySelectorAll('.appt-type-card').forEach(card => {
            card.classList.remove('selected', 'disabled', 'opacity-50');
            card.style.pointerEvents = '';
            card.title = '';
        });
        document.getElementById('durationSelectionSection')?.classList.add('d-none');

        // Reset duration
        const dur45 = document.getElementById('dur45');
        if (dur45) dur45.checked = true;

        this._updateWizardIndicators(1);
        this._updateWizardSummary();
        this._updateWizardButtons();
    }

    /**
     * Show wizard step
     * @private
     * @param {number|string} step - Step number
     */
    _wizardShowStep(step) {
        this.wizardState.currentStep = step;

        // Hide all step content
        document.querySelectorAll('.wizard-step-content').forEach(el => el.classList.add('d-none'));

        // Show current step
        let stepElement = null;
        switch (step) {
            case 1:
                stepElement = document.getElementById('wizardStep1');
                break;
            case 2:
                stepElement = document.getElementById('wizardStep2');
                break;
            case '2b':
                stepElement = document.getElementById('wizardStep2b');
                break;
            case 3:
                stepElement = this.wizardState.isRecurringPath
                    ? document.getElementById('wizardStep3Recurring')
                    : document.getElementById('wizardStep3');
                // Load available slots when Step 3 is shown (for all providers by default)
                if (!this.wizardState.isRecurringPath) {
                    this._loadAvailableSlots();
                } else {
                    // For recurring path, load providers for the recurring provider dropdown
                    this._loadProviderDropdown('recurringProviderSelect');
                    // Load Care Episode guidance if not already loaded
                    if (!this.careEpisodeGuidanceData && this.wizardState.careEpisodeId) {
                        this._loadCareEpisodeGuidance();
                    }
                    // Initialize the recurring preview (will show "Select days" message initially)
                    this._updateRecurringPreview();
                }
                break;
            case 4:
                stepElement = document.getElementById('wizardStep4');
                // FIX: Populate preview panel when showing step 4
                this._updatePreviewPanel();
                break;
        }

        if (stepElement) {
            stepElement.classList.remove('d-none');
        }

        this._updateWizardIndicators(step);
        this._updateWizardSummary();
        this._updateWizardButtons();
    }

    /**
     * Go to next wizard step (public wrapper for onclick handlers)
     */
    wizardNext() {
        this._wizardNext();
    }

    /**
     * Go to previous wizard step (public wrapper for onclick handlers)
     */
    wizardPrev() {
        this._wizardBack();
    }

    /**
     * Go to specific wizard step (public wrapper for onclick handlers)
     */
    wizardGoTo(step) {
        this._wizardShowStep(step);
    }

    /**
     * Show specific wizard step (public method for edit mode)
     */
    wizardShowStep(step) {
        this._wizardShowStep(step);
    }

    /**
     * Select appointment type (public wrapper for onclick handlers)
     * @param {number} type - Appointment type ID (0=New Patient, 1=Follow-Up, 2=Annual Physical, 3=Wellness)
     */
    selectType(type) {
        // Remove selection from other cards
        document.querySelectorAll('.appt-type-card').forEach(c => c.classList.remove('selected'));

        // Find and select the card with matching type
        const card = document.querySelector(`.appt-type-card[data-type="${type}"]`);
        if (card) {
            card.classList.add('selected');
        }

        // Update wizard state
        this.wizardState.selectedType = type;

        // Show duration selection
        const durationSection = document.getElementById('durationSelectionSection');
        if (durationSection) {
            durationSection.classList.remove('d-none');
        }

        this._updateWizardSummary();
        this._updateWizardButtons();
    }

    /**
     * Select schedule type (single or recurring)
     * @param {string} type - Schedule type ('single' or 'recurring')
     */
    selectScheduleType(type) {
        this.wizardState.scheduleType = type;
        this.wizardState.isRecurringPath = (type === 'recurring');

        // Remove selection from other cards
        document.querySelectorAll('.schedule-choice-card').forEach(c => c.classList.remove('selected'));

        // Find and select the card with matching type
        const card = document.querySelector(`.schedule-choice-card[onclick*="${type}"]`);
        if (card) {
            card.classList.add('selected');
        }

        // Show/hide recurring options
        const recurringOptions = document.getElementById('recurringOptionsSection');
        if (recurringOptions) {
            recurringOptions.classList.toggle('d-none', type !== 'recurring');
        }

        // Move to next step
        this._wizardNext();
    }

    /**
     * Go to next wizard step
     * @private
     */
    _wizardNext() {
        const current = this.wizardState.currentStep;

        // Validate current step
        if (!this._validateWizardStep(current)) return;

        // Determine next step
        let nextStep;
        if (current === 1) {
            nextStep = 2;
        } else if (current === 2) {
            // Recurring appointments (step 2b) removed for Internal Medicine - always go to slot selection
            {
                nextStep = 3;
            }
        } else if (current === '2b') {
            nextStep = 3;
        } else if (current === 3) {
            nextStep = 4;
        }

        if (nextStep) {
            this._wizardShowStep(nextStep);
        }
    }

    /**
     * Go to previous wizard step
     * @private
     */
    _wizardBack() {
        const current = this.wizardState.currentStep;

        // In reschedule mode, don't allow going back before step 3
        if (this.wizardState.isRescheduleMode && current === 3) {
            return; // Cannot go back from step 3 in reschedule mode
        }

        let prevStep;
        if (current === 2 || current === '2b') {
            prevStep = 1;
        } else if (current === 3) {
            prevStep = this.wizardState.isRecurringPath ? '2b' : 2;
        } else if (current === 4) {
            prevStep = 3;
        }

        if (prevStep) {
            this._wizardShowStep(prevStep);
        }
    }

    /**
     * Validate wizard step
     * @private
     * @param {number|string} step - Step to validate
     * @returns {boolean} Is valid
     */
    _validateWizardStep(step) {
        switch (step) {
            case 1:
                if (!this.wizardState.selectedPatient) {
                    this._showWarning('Please select a patient');
                    return false;
                }
                break;
            case 2:
                if (this.wizardState.selectedType === null) {
                    this._showWarning('Please select an appointment type');
                    return false;
                }
                // Care episode validation removed for Internal Medicine
                break;
        }
        return true;
    }

    /**
     * Update wizard step indicators
     * @private
     * @param {number|string} currentStep - Current step
     */
    _updateWizardIndicators(currentStep) {
        const numStep = typeof currentStep === 'string' ? parseInt(currentStep) || 2 : currentStep;

        for (let i = 1; i <= 4; i++) {
            const indicator = document.getElementById(`wizardStep${i}Indicator`);
            if (!indicator) continue;

            indicator.classList.remove('active', 'completed');

            if (i < numStep) {
                indicator.classList.add('completed');
            } else if (i === numStep || (currentStep === '2b' && i === 2)) {
                indicator.classList.add('active');
            }
        }
    }

    /**
     * Update wizard summary
     * @private
     */
    _updateWizardSummary() {
        const summaryHeader = document.getElementById('wizardSummaryHeader');
        if (!summaryHeader) return;

        let summaryParts = [];

        if (this.wizardState.selectedPatient) {
            summaryParts.push(`Patient: ${this.wizardState.selectedPatient.FullName}`);
        }
        if (this.wizardState.selectedType !== null) {
            const typeName = this.appointmentTypes[this.wizardState.selectedType]?.name || 'Unknown';
            summaryParts.push(`Type: ${typeName}`);
        }
        if (this.wizardState.selectedDuration) {
            summaryParts.push(`Duration: ${this.wizardState.selectedDuration} min`);
        }

        summaryHeader.textContent = summaryParts.join(' | ') || 'Schedule Appointment';
    }

    /**
     * Update wizard navigation buttons
     * @private
     */
    _updateWizardButtons() {
        const backBtn = document.getElementById('wizardBackBtn');
        const nextBtn = document.getElementById('wizardNextBtn');
        const submitBtn = document.getElementById('apptSaveBtn');
        const step = this.wizardState.currentStep;

        // Show/hide buttons based on step
        // In reschedule mode, hide back button on step 3 since we start there
        if (backBtn) {
            const hideBack = step === 1 || (this.wizardState.isRescheduleMode && step === 3);
            backBtn.style.display = hideBack ? 'none' : 'inline-block';
        }
        if (nextBtn) {
            nextBtn.style.display = step === 4 ? 'none' : 'inline-block';
        }
        if (submitBtn) {
            submitBtn.classList.toggle('d-none', step !== 4);
        }

        // Enable/disable Next button based on step validation
        if (nextBtn) {
            let canProceed = false;

            switch (step) {
                case 1: // Patient selection
                    canProceed = this.wizardState.selectedPatient !== null;
                    break;
                case 2: // Appointment type
                    canProceed = this.wizardState.selectedType !== null;
                    break;
                case 3: // Schedule
                    if (this.wizardState.isRecurringPath) {
                        // For recurring path, check if we have appointments configured
                        canProceed = this.wizardState.recurringConfig &&
                                    this.wizardState.recurringConfig.appointments &&
                                    this.wizardState.recurringConfig.appointments.length > 0;
                    } else {
                        // For single appointment, check for selected slot or custom time
                        canProceed = this.wizardState.selectedSlot !== null ||
                                    document.getElementById('apptCustomTime')?.value;
                    }
                    break;
                default:
                    canProceed = true;
            }

            nextBtn.disabled = !canProceed;
        }
    }

    /**
     * Submit appointment
     * @private
     */
    async _submitAppointment() {
        // Check if this is a recurring appointment submission
        if (this.wizardState.isRecurringPath && this.wizardState.recurringConfig) {
            await this._submitRecurringAppointments();
            return;
        }

        const data = this._buildAppointmentData();

        // DEBUG: Log the appointment data being sent
        console.log('[AppointmentModule] Submitting appointment data:', JSON.stringify(data, null, 2));

        // Validate required fields BEFORE hitting the server, so we can show
        // friendly inline errors instead of raw 400 JSON dumps from the API.
        if (!data.PatientId) {
            this._showError('Please select a patient before saving.');
            return;
        }
        if (!data.ProviderId) {
            this._showError('Please select a provider before saving.');
            return;
        }
        if (!data.StartTime || !data.EndTime) {
            this._showError('Please pick a date and time before saving.');
            return;
        }
        if (data.Type === null || data.Type === undefined) {
            this._showError('Please select an appointment type before saving.');
            return;
        }

        try {
            if (this.wizardState.isEditMode) {
                await this._apiPut(`/appointments/${this.wizardState.editingAppointmentId}`, data);
                this._showSuccess('Appointment updated');
                this._emit('appointments:updated', { appointmentId: this.wizardState.editingAppointmentId });
            } else {
                const result = await this._apiPost('/appointments', data);
                console.log('[AppointmentModule] Appointment created successfully:', result);
                this._showSuccess('Appointment scheduled');
                this._emit('appointments:created', { appointment: result });
            }

            this._hideModal('appointmentModal');
        } catch (error) {
            console.error('[AppointmentModule] Submit error:', error);
            // Translate common server errors into friendly UI messages.
            // The raw error.message often looks like
            //   "HTTP 400 Bad Request - {...validation JSON...}"
            // — never show that directly to the user.
            this._showError(this._friendlyAppointmentError(error));
        }
    }

    /**
     * Map raw API/server errors to human-readable messages for the
     * appointment form. Falls back to a generic "could not save" so the
     * raw RFC 9110 problem-details JSON never reaches the user.
     * @private
     */
    _friendlyAppointmentError(error) {
        const raw = (error && error.message) ? String(error.message) : '';

        // Time-slot conflict (server: InvalidOperationException
        // "Time slot conflicts with existing appointment")
        if (/conflicts? with existing appointment|time slot.*conflict/i.test(raw)) {
            return 'This time slot is already booked for the selected provider. Please choose a different time.';
        }
        // Provider validation (null or missing)
        if (/ProviderId/i.test(raw)) {
            return 'Please select a provider before saving.';
        }
        // Patient validation
        if (/PatientId/i.test(raw)) {
            return 'Please select a patient before saving.';
        }
        // Type / status / time fields
        if (/StartTime|EndTime/i.test(raw)) {
            return 'Please pick a valid date and time before saving.';
        }
        if (/\$\.Type/i.test(raw)) {
            return 'Please select an appointment type before saving.';
        }
        // Generic 400 (validation errors occurred)
        if (/HTTP 400|Bad Request|validation errors occurred/i.test(raw)) {
            return 'Some fields are missing or invalid. Please review the form and try again.';
        }
        // 401 / 403
        if (/HTTP 401|Unauthorized/i.test(raw)) {
            return 'Your session has expired. Please sign in again.';
        }
        if (/HTTP 403|Forbidden/i.test(raw)) {
            return 'You do not have permission to save this appointment.';
        }
        // 409 explicit conflict
        if (/HTTP 409|Conflict/i.test(raw)) {
            return 'This time slot is already booked. Please choose a different time.';
        }
        // 500 / network
        if (/HTTP 5\d\d|Internal Server Error|Failed to fetch|NetworkError/i.test(raw)) {
            return 'Could not reach the server. Please check your connection and try again.';
        }
        // Last-resort fallback — short and friendly
        return 'Could not save the appointment. Please review the form and try again.';
    }

    /**
     * Submit recurring appointments
     * @private
     */
    async _submitRecurringAppointments() {
        const config = this.wizardState.recurringConfig;
        if (!config || !config.appointments || config.appointments.length === 0) {
            this._showError('No appointments available to schedule. Please check provider availability.');
            return;
        }

        const patientId = this.wizardState.selectedPatient?.PatientId;
        if (!patientId) {
            this._showError('Patient is required');
            return;
        }

        // Get duration
        const duration = this.wizardState.selectedDuration || 45;

        // Get notes from recurring notes field
        const notes = document.getElementById('recurringNotes')?.value || '';

        // Get location timezone for proper time conversion
        const locationTz = window.getCurrentLocationTimezone ? window.getCurrentLocationTimezone() : { timeZoneId: 'America/Chicago' };

        console.log('[AppointmentModule] Submitting recurring appointments:', {
            count: config.appointments.length,
            patientId,
            isAutoAssign: config.isAutoAssign,
            duration,
            preferredTime: config.preferredTime
        });

        try {
            let successCount = 0;
            let failCount = 0;
            const createdAppointments = [];

            // Create each appointment - use provider from each appointment (auto-assigned or fixed)
            for (const appt of config.appointments) {
                try {
                    // Get provider ID - either from auto-assign result or from fixed selection
                    const providerId = appt.providerId || config.providerId;

                    if (!providerId) {
                        console.warn('[AppointmentModule] No provider ID for appointment:', appt);
                        failCount++;
                        continue;
                    }

                    // Use the date directly from the appointment (already has time set)
                    const startDate = new Date(appt.date);
                    const endDate = new Date(startDate.getTime() + duration * 60000);

                    // Convert to UTC if helper available
                    let startTime, endTime;
                    if (window.convertTimezoneToUtc) {
                        startTime = window.convertTimezoneToUtc(startDate, locationTz.timeZoneId).toISOString();
                        endTime = window.convertTimezoneToUtc(endDate, locationTz.timeZoneId).toISOString();
                    } else {
                        startTime = startDate.toISOString();
                        endTime = endDate.toISOString();
                    }

                    const appointmentData = {
                        PatientId: patientId,
                        ProviderId: providerId,
                        Type: 1, // Follow-Up
                        StartTime: startTime,
                        EndTime: endTime,
                        Reason: notes
                    };

                    const result = await this._apiPost('/appointments', appointmentData);
                    successCount++;
                    createdAppointments.push({
                        ...result,
                        providerName: appt.providerName
                    });
                } catch (err) {
                    console.error('[AppointmentModule] Failed to create recurring appointment:', err);
                    failCount++;
                }
            }

            // Show result
            if (successCount === 0) {
                this._showError('Failed to create any appointments. Please try again.');
            } else if (failCount === 0) {
                this._showSuccess(`${successCount} appointments scheduled successfully`);
            } else {
                this._showWarning(`${successCount} appointments scheduled, ${failCount} failed`);
            }

            if (successCount > 0) {
                this._emit('appointments:created', { count: successCount, recurring: true, appointments: createdAppointments });
                this._hideModal('appointmentModal');
            }

        } catch (error) {
            console.error('[AppointmentModule] Submit recurring error:', error);
            this._showError(error.message || 'Failed to save recurring appointments');
        }
    }

    /**
     * Build appointment data from wizard state
     * @private
     * @returns {Object} Appointment data
     */
    _buildAppointmentData() {
        let startTime, endTime;

        // FIX: Check if slot is selected, otherwise use custom date/time
        if (this.wizardState.selectedSlot) {
            startTime = this.wizardState.selectedSlot.start;
            endTime = this.wizardState.selectedSlot.end;
        } else {
            // Use custom date and time from form
            const dateInput = document.getElementById('apptDatePicker');
            const timeInput = document.getElementById('apptCustomTime');
            const duration = this.wizardState.selectedDuration || 45;

            if (dateInput?.value && timeInput?.value) {
                // Combine date and time
                const dateTimeStr = `${dateInput.value}T${timeInput.value}:00`;
                const startDate = new Date(dateTimeStr);

                // Get location timezone to convert to UTC properly
                if (window.convertTimezoneToUtc) {
                    const locationTz = window.getCurrentLocationTimezone ? window.getCurrentLocationTimezone() : { timeZoneId: 'America/Chicago' };
                    startTime = window.convertTimezoneToUtc(startDate, locationTz.timeZoneId).toISOString();
                    const endDate = new Date(startDate.getTime() + duration * 60000);
                    endTime = window.convertTimezoneToUtc(endDate, locationTz.timeZoneId).toISOString();
                } else {
                    // Fallback: assume local time
                    startTime = startDate.toISOString();
                    endTime = new Date(startDate.getTime() + duration * 60000).toISOString();
                }
            }
        }

        // Check for reschedule context
        const rescheduleFromId = document.getElementById('rescheduleFromAppointmentId')?.value;
        const rescheduledFromAppointmentId = rescheduleFromId ? parseInt(rescheduleFromId) : null;

        const data = {
            PatientId: this.wizardState.selectedPatient?.PatientId,
            ProviderId: this.wizardState.selectedProvider,
            Type: this.wizardState.selectedType,
            StartTime: startTime,
            EndTime: endTime,
            Reason: document.getElementById('apptNotes')?.value || ''
        };

        // Auto-set IsTelehealth when appointment type is Telehealth (5)
        if (data.Type === 5) {
            data.IsTelehealth = true;
        }

        // Add reschedule reference if this is a rescheduled appointment
        if (rescheduledFromAppointmentId) {
            data.RescheduledFromAppointmentId = rescheduledFromAppointmentId;
            console.log('[AppointmentModule] This appointment is rescheduled from:', rescheduledFromAppointmentId);
        }

        return data;
    }

    // === Patient Search ===

    /**
     * Load recent patients
     * @private
     */
    async _loadRecentPatients() {
        const container = document.getElementById('recentPatientsList');
        if (!container) {
            console.warn('[AppointmentModule] Recent patients container not found');
            return;
        }

        try {
            container.innerHTML = '<div class="text-muted small">Loading recent patients...</div>';

            // Use the regular patients endpoint with filters
            const patients = await this._apiGet('/patients?isArchived=false');

            // Ensure we have an array
            if (!Array.isArray(patients)) {
                console.error('[AppointmentModule] Patients response is not an array:', patients);
                container.innerHTML = '<div class="text-danger small">Error loading patients</div>';
                return;
            }

            if (patients.length === 0) {
                container.innerHTML = '<div class="text-muted small">No patients found</div>';
                return;
            }

            // Filter out archived/deleted and take first 6
            const activePatients = patients.filter(p => {
                // Check if patient is active (not archived and not deleted)
                const isActive = !p.IsArchived && p.Status !== 2;
                return isActive;
            });

            if (activePatients.length === 0) {
                container.innerHTML = '<div class="text-muted small">No active patients</div>';
                return;
            }

            const recentPatients = activePatients.slice(0, 6);

            // Render patient cards
            const html = recentPatients.map(p => {
                const fullName = this._escape(p.FullName || `${p.FirstName} ${p.LastName}`);
                const mrn = this._escape(p.MRN || '');

                return `
                    <div class="col-6 col-md-4">
                        <div class="card h-100 recent-patient-card" style="cursor: pointer;"
                             data-patient-id="${p.PatientId}"
                             data-patient-name="${fullName}"
                             data-patient-mrn="${mrn}"
                             data-has-photo="${p.HasProfilePicture ? '1' : '0'}">
                            <div class="card-body p-2 text-center">
                                ${AvatarUtils.renderPatientAvatar({ patientId: p.PatientId, name: p.FullName || `${p.FirstName} ${p.LastName}`, hasProfilePicture: p.HasProfilePicture, size: 'md', cssClass: 'd-inline-flex mb-2' })}
                                <div class="small fw-medium text-truncate">${fullName}</div>
                                <div class="text-muted" style="font-size: 0.7rem;">${mrn}</div>
                            </div>
                        </div>
                    </div>
                `;
            }).join('');

            container.innerHTML = html;

            // Add click handlers
            container.querySelectorAll('.recent-patient-card').forEach(card => {
                card.addEventListener('click', () => {
                    const patientData = {
                        PatientId: parseInt(card.dataset.patientId),
                        FullName: card.dataset.patientName,
                        MRN: card.dataset.patientMrn,
                        HasProfilePicture: card.dataset.hasPhoto === '1'
                    };
                    this._selectPatient(patientData);
                });
            });

            console.log('[AppointmentModule] Loaded', recentPatients.length, 'recent patients');
        } catch (error) {
            console.error('[AppointmentModule] Failed to load recent patients:', error);
            container.innerHTML = '<div class="text-danger small">Failed to load patients. Please try again.</div>';
        }
    }

    /**
     * Search patients
     * @private
     * @param {string} query - Search query
     */
    async _searchPatients(query) {
        const resultsContainer = document.getElementById('patientSearchResults');
        if (!resultsContainer) {
            console.warn('[AppointmentModule] Search results container not found');
            return;
        }

        // Hide results if query is too short
        if (!query || query.length < 2) {
            resultsContainer.classList.add('d-none');
            return;
        }

        try {
            console.log('[AppointmentModule] Searching for:', query);

            // Search patients - API returns PatientSearchResponse with Results property
            const response = await this._apiGet(`/patients/search?q=${encodeURIComponent(query)}&activeOnly=true`);

            // Extract results array from response
            const patients = response?.Results || [];

            console.log('[AppointmentModule] Found', patients.length, 'patients');

            this._renderPatientSearchResults(patients);
            resultsContainer.classList.remove('d-none');
        } catch (error) {
            console.error('[AppointmentModule] Patient search error:', error);
            this._renderPatientSearchResults([]);
            resultsContainer.classList.remove('d-none');
        }
    }

    /**
     * Render patient search results
     * @private
     * @param {Array} patients - Patient results
     */
    _renderPatientSearchResults(patients) {
        const container = document.getElementById('patientSearchResults');
        if (!container) return;

        if (!patients?.length) {
            container.innerHTML = '<div class="p-3 text-muted text-center">No patients found</div>';
            return;
        }

        container.innerHTML = patients.map(p => `
            <div class="patient-search-item p-2 d-flex align-items-center"
                 data-patient-id="${p.PatientId}"
                 data-patient-name="${this._escape(p.FullName)}"
                 data-patient-mrn="${this._escape(p.MRN)}"
                 data-has-photo="${p.HasProfilePicture ? '1' : '0'}"
                 style="cursor: pointer;">
                ${window.AvatarUtils ? AvatarUtils.renderPatientAvatar({ patientId: p.PatientId, name: p.FullName, hasProfilePicture: p.HasProfilePicture, size: 'sm', cssClass: 'me-2' }) : ''}
                <div>
                    <strong>${this._escape(p.FullName)}</strong>
                    <small class="text-muted d-block">MRN: ${this._escape(p.MRN)}</small>
                </div>
            </div>
        `).join('');

        // Add click handlers
        container.querySelectorAll('.patient-search-item').forEach(item => {
            item.addEventListener('click', () => {
                this._selectPatient({
                    PatientId: parseInt(item.dataset.patientId),
                    FullName: item.dataset.patientName,
                    MRN: item.dataset.patientMrn,
                    HasProfilePicture: item.dataset.hasPhoto === '1'
                });
            });
        });
    }

    /**
     * Select patient
     * @private
     * @param {Object} patient - Patient data
     */
    async _selectPatient(patient) {
        this.wizardState.selectedPatient = patient;

        // Update hidden form field
        document.getElementById('apptPatientId').value = patient.PatientId;

        // LEGACY FIX: Check for active Care Episode (like legacy code lines 3116-3130)
        // Use silent API call that doesn't show error toasts for 404 (expected when no active episode)
        try {
            const activeEpisode = await this._apiGetSilent(`/care-episodes/patient/${patient.PatientId}/active`);
            if (activeEpisode && activeEpisode.CareEpisodeId) {
                this.wizardState.hasActiveCareEpisode = true;
                this.wizardState.careEpisodeId = activeEpisode.CareEpisodeId;
                console.log('[AppointmentModule] Patient has active Care Episode:', activeEpisode.CareEpisodeId);
            } else {
                this.wizardState.hasActiveCareEpisode = false;
                this.wizardState.careEpisodeId = null;
                console.log('[AppointmentModule] Patient has no active Care Episode - only Initial Evaluation allowed');
            }
        } catch (err) {
            // No active care episode found (404 is expected - don't show error)
            this.wizardState.hasActiveCareEpisode = false;
            this.wizardState.careEpisodeId = null;
            console.log('[AppointmentModule] No active Care Episode found for patient');
        }

        // Update the appointment type cards availability based on Care Episode status
        this._updateTypeCardsAvailability();

        // For Internal Medicine, don't auto-select type - let user choose
        // No care episode dependency

        // Hide search results and search container
        document.getElementById('patientSearchResults')?.classList.add('d-none');
        document.getElementById('apptPatientContainer')?.classList.add('d-none');
        document.getElementById('recentPatientsSection')?.classList.add('d-none');

        // Show selected patient display
        const display = document.getElementById('selectedPatientDisplay');
        if (display) {
            display.classList.remove('d-none');
        }

        // Update selected patient details
        const nameEl = document.getElementById('selectedPatientName');
        const infoEl = document.getElementById('selectedPatientInfo');

        if (nameEl) {
            nameEl.textContent = patient.FullName;
        }
        if (infoEl) {
            infoEl.textContent = `MRN: ${patient.MRN}`;
        }

        // Update summary and enable Next button
        this._updateWizardSummary();
        this._updateWizardButtons();

        console.log('[AppointmentModule] Selected patient:', patient.FullName);
    }

    /**
     * Update appointment type cards availability based on Care Episode status
     * Update type card availability - all types available in IM (no Care Episode requirement)
     * @private
     */
    _updateTypeCardsAvailability() {
        const typeCards = document.querySelectorAll('.appt-type-card');

        typeCards.forEach(card => {
            // Remove any existing lock badge
            const existingBadge = card.querySelector('.care-episode-lock-badge');
            if (existingBadge) {
                existingBadge.remove();
            }

            // All appointment types are available in Internal Medicine
            card.classList.remove('disabled', 'opacity-50');
            card.style.pointerEvents = '';
            card.title = '';
        });
    }

    /**
     * Clear patient selection
     */
    _clearPatientSelection() {
        this.wizardState.selectedPatient = null;
        // LEGACY FIX: Also clear Care Episode state
        this.wizardState.hasActiveCareEpisode = false;
        this.wizardState.careEpisodeId = null;

        document.getElementById('apptPatientId').value = '';
        document.getElementById('patientSearchInput').value = '';
        document.getElementById('selectedPatientDisplay')?.classList.add('d-none');
        document.getElementById('apptPatientContainer')?.classList.remove('d-none');
        document.getElementById('recentPatientsSection')?.classList.remove('d-none');

        // Reset type cards availability (all enabled since no patient selected yet)
        const typeCards = document.querySelectorAll('.appt-type-card');
        typeCards.forEach(card => {
            card.classList.remove('disabled', 'opacity-50', 'selected');
            card.style.pointerEvents = '';
            card.title = '';
            // Remove lock badges
            const badge = card.querySelector('.care-episode-lock-badge');
            if (badge) badge.remove();
        });

        this._updateWizardSummary();
        this._updateWizardButtons();
    }

    // === Available Slots ===

    /**
     * Load available slots (public wrapper for refresh button)
     */
    refreshSlots() {
        this._loadAvailableSlots();
    }

    /**
     * Load available time slots
     * @private
     */
    async _loadAvailableSlots() {
        const slotsLoading = document.getElementById('slotsLoading');
        const slotsEmpty = document.getElementById('slotsEmpty');
        const slotsList = document.getElementById('slotsList');
        const dateInput = document.getElementById('apptDatePicker');
        const providerSelect = document.getElementById('apptProviderSelect');

        // Show loading state
        if (slotsLoading) slotsLoading.classList.remove('d-none');
        if (slotsEmpty) slotsEmpty.classList.add('d-none');
        if (slotsList) {
            slotsList.classList.add('d-none');
            slotsList.innerHTML = '';
        }

        try {
            const duration = this.wizardState.selectedDuration || 45;

            // Get timezone info
            const locationTz = window.getCurrentLocationTimezone ? window.getCurrentLocationTimezone() : { timeZoneId: 'America/Chicago' };
            const locationTzId = locationTz.timeZoneId || 'America/Chicago';
            const nowInLocationTz = window.convertUtcToTimezone ? window.convertUtcToTimezone(new Date(), locationTzId) : new Date();
            const todayInLocationTz = `${nowInLocationTz.getFullYear ? nowInLocationTz.getFullYear() : nowInLocationTz.year}-${String((nowInLocationTz.getMonth ? nowInLocationTz.getMonth() + 1 : nowInLocationTz.month + 1)).padStart(2, '0')}-${String((nowInLocationTz.getDate ? nowInLocationTz.getDate() : nowInLocationTz.day)).padStart(2, '0')}`;

            // Determine date - load only the selected date (or today)
            const datePickerValue = dateInput?.value;
            let startDateStr;

            if (datePickerValue) {
                startDateStr = datePickerValue;
            } else {
                // Default to today
                startDateStr = todayInLocationTz;
                if (dateInput) dateInput.value = startDateStr;
            }

            // Load all providers if not already loaded (no global loader - slot box has its own spinner)
            if (!this.cachedProviders || this.cachedProviders.length === 0) {
                this.cachedProviders = await this._apiGet('/providers?activeOnly=true', { showLoader: false }) || [];
            }

            const providerId = providerSelect?.value;
            const providerList = providerId ?
                this.cachedProviders.filter(p => p.ProviderId == providerId) :
                this.cachedProviders.slice(0, 5); // Limit to 5 providers

            // Fetch slots for the selected date only (single day)
            const allSlots = [];
            const startParts = startDateStr.split('-');
            const searchDate = new Date(
                parseInt(startParts[0]),
                parseInt(startParts[1]) - 1,
                parseInt(startParts[2]),
                12, 0, 0
            );
            const dateForApi = new Date(startDateStr + 'T12:00:00Z');

            for (const provider of providerList) {
                try {
                    const slots = await this._apiGet(
                        `/appointments/slots?providerId=${provider.ProviderId}&date=${dateForApi.toISOString()}&durationMinutes=${duration}`,
                        { showLoader: false }
                    );

                    if (slots && slots.length > 0) {
                        const availableSlots = slots.filter(s => s.IsAvailable);
                        availableSlots.forEach(slot => {
                            allSlots.push({
                                ...slot,
                                date: searchDate,
                                dateStr: startDateStr,
                                dateLabel: this._formatDateLabel(startDateStr, todayInLocationTz)
                            });
                        });
                    }
                } catch (e) {
                    console.error(`Error loading slots for provider ${provider.ProviderId}:`, e);
                }
            }

            // Hide loading
            if (slotsLoading) slotsLoading.classList.add('d-none');

            if (allSlots.length === 0) {
                if (slotsEmpty) slotsEmpty.classList.remove('d-none');
                return;
            }

            // Render grouped slots
            if (slotsList) {
                slotsList.classList.remove('d-none');
                this._renderGroupedSlots(allSlots);
            }

        } catch (error) {
            console.error('[AppointmentModule] Failed to load slots:', error);
            if (slotsLoading) slotsLoading.classList.add('d-none');
            if (slotsEmpty) slotsEmpty.classList.remove('d-none');
            if (slotsList) {
                slotsList.innerHTML = '<div class="text-danger small text-center py-3">Failed to load slots. Please try again.</div>';
                slotsList.classList.remove('d-none');
            }
        }
    }

    /**
     * Format date label with timezone awareness
     * @private
     */
    _formatDateLabel(dateStr, todayStr) {
        if (!dateStr || !todayStr) {
            const date = new Date(dateStr + 'T12:00:00');
            const today = new Date();
            today.setHours(0, 0, 0, 0);
            const compareDate = new Date(date);
            compareDate.setHours(0, 0, 0, 0);
            const diffDays = Math.round((compareDate - today) / (1000 * 60 * 60 * 24));
            if (diffDays === 0) return 'Today';
            if (diffDays === 1) return 'Tomorrow';
            return date.toLocaleDateString('en-US', { weekday: 'short', month: 'short', day: 'numeric' });
        }

        if (dateStr === todayStr) return 'Today';

        const todayParts = todayStr.split('-');
        const todayDate = new Date(parseInt(todayParts[0]), parseInt(todayParts[1]) - 1, parseInt(todayParts[2]), 12, 0, 0);
        const tomorrowDate = new Date(todayDate);
        tomorrowDate.setDate(todayDate.getDate() + 1);
        const tomorrowStr = `${tomorrowDate.getFullYear()}-${String(tomorrowDate.getMonth() + 1).padStart(2, '0')}-${String(tomorrowDate.getDate()).padStart(2, '0')}`;

        if (dateStr === tomorrowStr) return 'Tomorrow';

        const dateParts = dateStr.split('-');
        const displayDate = new Date(parseInt(dateParts[0]), parseInt(dateParts[1]) - 1, parseInt(dateParts[2]), 12, 0, 0);
        return displayDate.toLocaleDateString('en-US', { weekday: 'short', month: 'short', day: 'numeric' });
    }

    /**
     * Render grouped slots by date
     * @private
     * @param {Array} slots - Available time slots
     */
    _renderGroupedSlots(slots) {
        const container = document.getElementById('slotsList');
        if (!container) return;

        // Group by date
        const grouped = {};
        slots.forEach(slot => {
            const dateKey = slot.date.toDateString();
            if (!grouped[dateKey]) {
                grouped[dateKey] = {
                    dateLabel: slot.dateLabel,
                    date: slot.date,
                    slots: []
                };
            }
            grouped[dateKey].slots.push(slot);
        });

        // Sort dates
        const sortedDates = Object.values(grouped).sort((a, b) => a.date - b.date);

        // EDIT MODE: Build "Current Appointment" section at top if in edit mode
        let currentAppointmentHtml = '';
        if (this.wizardState.isEditMode && this.editingAppointment) {
            const orig = this.editingAppointment;
            const locationTz = window.getCurrentLocationTimezone ? window.getCurrentLocationTimezone() : { timeZoneId: 'America/Chicago', timeZoneAbbreviation: 'CT' };
            const tzId = orig.TimeZoneId || locationTz.timeZoneId || 'America/Chicago';
            const tzAbbr = orig.TimeZoneAbbreviation || locationTz.timeZoneAbbreviation || '';

            // Format date and time in location timezone
            // Use parseServerDateTime so server UTC times without 'Z' aren't misread as local.
            const startDate = window.parseServerDateTime
                ? window.parseServerDateTime(orig.StartTime)
                : new Date(orig.StartTime);
            const endDate = window.parseServerDateTime
                ? window.parseServerDateTime(orig.EndTime)
                : new Date(orig.EndTime);

            const dateDisplay = startDate.toLocaleDateString('en-US', {
                weekday: 'long',
                month: 'long',
                day: 'numeric',
                year: 'numeric',
                timeZone: tzId
            });
            const startTimeDisplay = startDate.toLocaleTimeString('en-US', {
                hour: 'numeric',
                minute: '2-digit',
                hour12: true,
                timeZone: tzId
            });
            const endTimeDisplay = endDate.toLocaleTimeString('en-US', {
                hour: 'numeric',
                minute: '2-digit',
                hour12: true,
                timeZone: tzId
            });

            currentAppointmentHtml = `
                <div class="current-appointment-section mb-4 p-3 border border-primary rounded bg-primary-subtle">
                    <div class="d-flex align-items-center mb-2">
                        <i class="bi bi-star-fill text-warning me-2"></i>
                        <span class="fw-bold text-primary">CURRENT APPOINTMENT</span>
                    </div>
                    <div class="mb-2">
                        <div class="fw-bold">${this._escape(dateDisplay)}</div>
                        <div class="fs-5">${startTimeDisplay} - ${endTimeDisplay} ${this._escape(tzAbbr)}</div>
                    </div>
                    <div class="mb-2">
                        <small class="text-muted">Provider:</small> ${this._escape(orig.ProviderName || 'Unknown')}
                    </div>
                    ${orig.LocationName ? `<div class="mb-2"><small class="text-muted">Location:</small> ${this._escape(orig.LocationName)}</div>` : ''}
                    <button type="button" class="btn btn-primary btn-sm mt-2" id="keepCurrentTimeBtn">
                        <i class="bi bi-arrow-counterclockwise me-1"></i>Keep This Time
                    </button>
                </div>
            `;
        }

        // Helper to check if slot matches original appointment time
        const isOriginalSlot = (slot) => {
            if (!this.wizardState.isEditMode || !this.editingAppointment) return false;
            const orig = this.editingAppointment;
            return slot.StartTime === orig.StartTime && slot.ProviderId === orig.ProviderId;
        };

        // Render
        container.innerHTML = currentAppointmentHtml + sortedDates.map(group => `
            <div class="slot-date-group mb-3">
                <div class="slot-date-header sticky-top bg-light py-2 border-bottom fw-bold ${group.dateLabel === 'Today' ? 'text-primary' : ''}">
                    ${this._escape(group.dateLabel)}
                </div>
                <div class="slot-cards">
                    ${group.slots.map(slot => {
                        const startTime = this._formatSlotTime(slot);
                        const providerName = this._escape(slot.ProviderName || 'Unknown');
                        const providerColor = slot.ProviderColor || '#2196F3';
                        const locationDisplay = this._getLocationDisplay(slot.LocationName, slot.TimeZoneAbbreviation);
                        const isCurrent = isOriginalSlot(slot);

                        return `
                        <div class="slot-card p-2 mb-2 border rounded cursor-pointer hover-shadow ${isCurrent ? 'border-warning bg-warning-subtle' : 'bg-white'}"
                             data-slot='${JSON.stringify(slot).replace(/'/g, "&#39;")}'>
                            <div class="d-flex align-items-center">
                                ${isCurrent ? '<i class="bi bi-star-fill text-warning me-2" title="Current appointment time"></i>' : ''}
                                <div class="slot-color-bar me-3" style="width: 4px; height: 40px; border-radius: 2px; background-color: ${providerColor}"></div>
                                <div class="slot-time fw-bold me-3" style="min-width: 100px;">
                                    ${startTime}
                                    ${isCurrent ? '<span class="badge bg-warning text-dark ms-1">CURRENT</span>' : ''}
                                </div>
                                <div class="slot-info flex-grow-1">
                                    <div class="slot-provider fw-medium">${providerName}</div>
                                    ${locationDisplay}
                                </div>
                            </div>
                        </div>
                    `}).join('')}
                </div>
            </div>
        `).join('');

        // Add click handlers
        container.querySelectorAll('.slot-card').forEach(card => {
            card.addEventListener('click', () => {
                const slotData = JSON.parse(card.dataset.slot);
                this._selectSlot(slotData);
            });
        });

        // Add click handler for "Keep This Time" button in edit mode
        const keepCurrentTimeBtn = document.getElementById('keepCurrentTimeBtn');
        if (keepCurrentTimeBtn) {
            keepCurrentTimeBtn.addEventListener('click', () => {
                this._restoreOriginalAppointment();
            });
        }
    }

    /**
     * Restore original appointment time when user clicks "Keep This Time" (edit mode)
     * @private
     */
    _restoreOriginalAppointment() {
        if (!this.editingAppointment) {
            this._showError('Original appointment not found');
            return;
        }

        const orig = this.editingAppointment;
        const locationTz = window.getCurrentLocationTimezone ? window.getCurrentLocationTimezone() : { timeZoneId: 'America/Chicago', timeZoneAbbreviation: 'CT' };
        const tzId = orig.TimeZoneId || locationTz.timeZoneId || 'America/Chicago';
        const tzAbbr = orig.TimeZoneAbbreviation || locationTz.timeZoneAbbreviation || '';

        // Format date and time for display
        // Use parseServerDateTime so server UTC times without 'Z' aren't misread as local.
        const startDate = window.parseServerDateTime
            ? window.parseServerDateTime(orig.StartTime)
            : new Date(orig.StartTime);
        const endDate = window.parseServerDateTime
            ? window.parseServerDateTime(orig.EndTime)
            : new Date(orig.EndTime);

        const dateDisplay = startDate.toLocaleDateString('en-US', {
            month: 'short',
            day: 'numeric',
            timeZone: tzId
        });
        const startTimeDisplay = startDate.toLocaleTimeString('en-US', {
            hour: 'numeric',
            minute: '2-digit',
            hour12: true,
            timeZone: tzId
        });

        // Create a slot object from the original appointment
        const originalSlot = {
            start: orig.StartTime,
            end: orig.EndTime,
            providerId: orig.ProviderId,
            providerName: orig.ProviderName,
            providerColor: orig.ProviderColor,
            locationName: orig.LocationName,
            locationId: orig.LocationId,
            timeZoneAbbreviation: tzAbbr,
            timeZoneId: tzId,
            IsAvailable: true
        };

        // Set the selected slot to original
        this.wizardState.selectedSlot = originalSlot;
        this.wizardState.selectedProvider = orig.ProviderId;

        // Update provider dropdown
        const providerSelect = document.getElementById('apptProviderSelect');
        if (providerSelect) {
            providerSelect.value = orig.ProviderId;
        }

        // Update date picker with original date in local timezone
        const datePicker = document.getElementById('apptDatePicker');
        if (datePicker && window.formatDateForInputInTimezone) {
            datePicker.value = window.formatDateForInputInTimezone(orig.StartTime, tzId);
        } else if (datePicker) {
            // Fallback: format date manually
            const year = startDate.toLocaleString('en-US', { year: 'numeric', timeZone: tzId });
            const month = startDate.toLocaleString('en-US', { month: '2-digit', timeZone: tzId });
            const day = startDate.toLocaleString('en-US', { day: '2-digit', timeZone: tzId });
            datePicker.value = `${year}-${month}-${day}`;
        }

        // Format time for input (HH:MM in 24h format)
        let formattedTime;
        if (window.formatTimeForInputInTimezone) {
            formattedTime = window.formatTimeForInputInTimezone(orig.StartTime, tzId);
        } else {
            // Fallback
            const hours = startDate.toLocaleString('en-US', { hour: '2-digit', hour12: false, timeZone: tzId });
            const minutes = startDate.toLocaleString('en-US', { minute: '2-digit', timeZone: tzId });
            formattedTime = `${hours.padStart(2, '0')}:${minutes.padStart(2, '0')}`;
        }

        // Update Custom Time field (visible to user)
        const customTime = document.getElementById('apptCustomTime');
        if (customTime) {
            customTime.value = formattedTime;
        }

        // Update hidden form fields
        const apptDate = document.getElementById('apptDate');
        const apptStartTime = document.getElementById('apptStartTime');
        if (apptDate && window.formatDateForInputInTimezone) {
            apptDate.value = window.formatDateForInputInTimezone(orig.StartTime, tzId);
        }
        if (apptStartTime) {
            apptStartTime.value = formattedTime;
        }

        // Highlight the current appointment section with visual feedback
        const currentSection = document.querySelector('.current-appointment-section');
        if (currentSection) {
            // Add selected styling
            currentSection.classList.remove('bg-primary-subtle');
            currentSection.classList.add('bg-success-subtle', 'border-success');

            // Flash effect
            currentSection.style.transition = 'all 0.3s ease';
            setTimeout(() => {
                currentSection.classList.remove('bg-success-subtle');
                currentSection.classList.add('bg-primary-subtle');
                currentSection.classList.remove('border-success');
            }, 1500);
        }

        // Remove selection from any other slot cards
        document.querySelectorAll('.slot-card').forEach(card => {
            card.classList.remove('border-primary', 'bg-primary-subtle', 'selected');
        });

        // Update wizard buttons
        this._updateWizardButtons();

        // Show success message
        this._showSuccess(`Restored to original time: ${startTimeDisplay} on ${dateDisplay}`);
    }

    /**
     * Format slot time for display
     * @private
     */
    _formatSlotTime(slot) {
        try {
            // Slots have pre-formatted times from the server - use them if available
            if (slot.StartTimeFormatted && slot.EndTimeFormatted) {
                return `${slot.StartTimeFormatted} - ${slot.EndTimeFormatted}`;
            }
            // Fallback: parse and format with timezone
            const startStr = slot.StartTime || slot.Start || slot.start;
            const endStr = slot.EndTime || slot.End || slot.end;
            const start = window.parseServerDateTime ? window.parseServerDateTime(startStr) : new Date(startStr);
            const end = window.parseServerDateTime ? window.parseServerDateTime(endStr) : new Date(endStr);
            const tzId = slot.TimeZoneId || (window.getCurrentLocationTimezone ? window.getCurrentLocationTimezone().timeZoneId : null);
            const options = { hour: 'numeric', minute: '2-digit', hour12: true };
            if (tzId) options.timeZone = tzId;
            const startFormatted = start.toLocaleTimeString('en-US', options);
            const endFormatted = end.toLocaleTimeString('en-US', options);
            return `${startFormatted} - ${endFormatted}`;
        } catch (e) {
            return slot.StartTime || slot.Start || slot.start || '';
        }
    }

    /**
     * Get location display string
     * @private
     */
    _getLocationDisplay(locationName, timeZoneAbbreviation) {
        if (!locationName || locationName === 'Default' || locationName.toLowerCase() === 'default') {
            if (timeZoneAbbreviation) {
                return `<div class="slot-timezone text-muted small">
                            <i class="bi bi-clock me-1"></i>${this._escape(timeZoneAbbreviation)}
                        </div>`;
            }
            return '';
        }
        return `<div class="slot-location text-muted small">
                    <i class="bi bi-geo-alt me-1"></i>${this._escape(locationName)}${timeZoneAbbreviation ? ` (${this._escape(timeZoneAbbreviation)})` : ''}
                </div>`;
    }

    /**
     * Select a time slot
     * @private
     * @param {Object} slot - Selected slot data
     */
    _selectSlot(slot) {
        // Normalize slot data structure (API uses StartTime/EndTime)
        const normalizedSlot = {
            start: slot.StartTime || slot.Start || slot.start,
            end: slot.EndTime || slot.End || slot.end,
            providerId: slot.ProviderId || slot.providerId,
            providerName: slot.ProviderName || slot.providerName,
            providerColor: slot.ProviderColor || slot.providerColor,
            locationName: slot.LocationName || slot.locationName,
            locationId: slot.LocationId || slot.locationId,
            timeZoneAbbreviation: slot.TimeZoneAbbreviation || slot.timeZoneAbbreviation,
            timeZoneId: slot.TimeZoneId || slot.timeZoneId,
            date: slot.date,
            dateStr: slot.dateStr,
            dateLabel: slot.dateLabel
        };

        this.wizardState.selectedSlot = normalizedSlot;

        // FIX: Update selected provider from slot
        this.wizardState.selectedProvider = normalizedSlot.providerId;

        // Remove selection from other slots
        document.querySelectorAll('.slot-card').forEach(c => c.classList.remove('border-primary', 'bg-primary-subtle'));

        // FIX: Highlight selected slot by matching BOTH time AND provider
        const allCards = document.querySelectorAll('.slot-card');
        for (const card of allCards) {
            try {
                const cardSlot = JSON.parse(card.dataset.slot);
                const cardStart = cardSlot.StartTime || cardSlot.Start || cardSlot.start;
                const cardProviderId = cardSlot.ProviderId || cardSlot.providerId;

                // Match both start time AND provider ID to handle overlapping slots
                if (cardStart === normalizedSlot.start && cardProviderId === normalizedSlot.providerId) {
                    card.classList.add('border-primary', 'bg-primary-subtle');
                    break;
                }
            } catch (e) {
                // Skip invalid cards
            }
        }

        // FIX: Get location timezone for proper date/time conversion
        const locationTzId = normalizedSlot.timeZoneId || (window.getCurrentLocationTimezone ? window.getCurrentLocationTimezone().timeZoneId : null) || 'America/Chicago';

        // FIX: Format date and time using timezone-aware functions
        let formattedDate, formattedTime;
        if (window.formatDateForInputInTimezone && window.formatTimeForInputInTimezone) {
            formattedDate = window.formatDateForInputInTimezone(normalizedSlot.start, locationTzId);
            formattedTime = window.formatTimeForInputInTimezone(normalizedSlot.start, locationTzId);
        } else {
            // Fallback to ISO format
            const startDate = new Date(normalizedSlot.start);
            formattedDate = startDate.toISOString().split('T')[0];
            formattedTime = startDate.toTimeString().slice(0, 5);
        }

        // FIX: Update all visible form fields in step 3
        const providerSelect = document.getElementById('apptProviderSelect');
        if (providerSelect && normalizedSlot.providerId) {
            providerSelect.value = normalizedSlot.providerId;
        }

        const datePicker = document.getElementById('apptDatePicker');
        if (datePicker) {
            datePicker.value = formattedDate;
        }

        const customTime = document.getElementById('apptCustomTime');
        if (customTime) {
            customTime.value = formattedTime;
        }

        // Update hidden form fields
        const dateInput = document.getElementById('apptDate');
        if (dateInput) {
            dateInput.value = formattedDate;
        }

        const startTimeInput = document.getElementById('apptStartTime');
        if (startTimeInput) {
            startTimeInput.value = formattedTime;
        }

        // Update summary and enable Next button
        this._updateWizardSummary();
        this._updateWizardButtons();

        console.log('[AppointmentModule] Selected slot:', normalizedSlot.start, normalizedSlot);
    }

    /**
     * Format time string for display
     * @private
     * @param {string} timeString - ISO time string
     * @returns {string} Formatted time
     */
    _formatTime(timeString) {
        try {
            const date = new Date(timeString);
            return date.toLocaleTimeString('en-US', {
                hour: 'numeric',
                minute: '2-digit',
                hour12: true
            });
        } catch (e) {
            return timeString;
        }
    }

    // === Recurring Appointment Methods ===

    /**
     * Store Care Episode data for recurring guidance
     * @private
     */
    careEpisodeGuidanceData = null;

    /**
     * Load and display Care Episode guidance for recurring appointments
     * Matches legacy behavior from lines 16143-16219
     * @private
     */
    async _loadCareEpisodeGuidance() {
        const guidancePanel = document.getElementById('careEpisodeGuidancePanel');
        if (!guidancePanel) return;

        // Check if we have a Care Episode ID
        const careEpisodeId = this.wizardState.careEpisodeId;
        if (!careEpisodeId) {
            guidancePanel.style.display = 'none';
            this.careEpisodeGuidanceData = null;
            return;
        }

        try {
            // Fetch Care Episode details
            const careEpisode = await this._apiGet(`/care-episodes/${careEpisodeId}`);

            if (!careEpisode) {
                guidancePanel.style.display = 'none';
                this.careEpisodeGuidanceData = null;
                return;
            }

            this.careEpisodeGuidanceData = careEpisode;

            // Calculate guidance values
            const expectedVisits = careEpisode.ExpectedVisits || 0;
            const visitFrequency = careEpisode.VisitFrequency || 0;

            // Count only Follow-Up/Visit type (Type=1) appointments that are not cancelled
            // This matches the dashboard "requireScheduleWidget" calculation exactly
            // Status: 0=Scheduled, 2=CheckedIn, 3=InProgress, 4=Completed
            const scheduledFollowUpVisits = (careEpisode.Appointments || []).filter(a =>
                a.Type === 1 && // FollowUp type only
                (a.Status === 0 || a.Status === 2 || a.Status === 3 || a.Status === 4)
            ).length;
            const visitsRemaining = expectedVisits - scheduledFollowUpVisits;

            // Calculate suggested duration in weeks
            let suggestedDuration = '-';
            if (visitFrequency > 0 && visitsRemaining > 0) {
                const weeks = Math.ceil(visitsRemaining / visitFrequency);
                suggestedDuration = `${weeks} week${weeks !== 1 ? 's' : ''}`;
            }

            // Update guidance display
            const visitsNeededEl = document.getElementById('ceGuidanceVisitsNeeded');
            const frequencyEl = document.getElementById('ceGuidanceFrequency');
            const durationEl = document.getElementById('ceGuidanceDuration');
            const scheduledEl = document.getElementById('ceGuidanceScheduled');
            const messageEl = document.getElementById('ceGuidanceMessageText');

            if (visitsNeededEl) visitsNeededEl.textContent = visitsRemaining > 0 ? visitsRemaining : '-';
            if (frequencyEl) frequencyEl.textContent = visitFrequency > 0 ? `${visitFrequency}x` : '-';
            if (durationEl) durationEl.textContent = suggestedDuration;
            if (scheduledEl) scheduledEl.textContent = scheduledFollowUpVisits > 0 ? scheduledFollowUpVisits : '0';

            // Build guidance message
            let messageText = '';
            if (expectedVisits > 0 && visitFrequency > 0 && visitsRemaining > 0) {
                const suggestedWeeks = Math.ceil(visitsRemaining / visitFrequency);
                messageText = `Recommendation: Schedule ${visitsRemaining} visits at ${visitFrequency}x/week = ${suggestedWeeks} week${suggestedWeeks !== 1 ? 's' : ''}`;
            } else if (expectedVisits > 0 && visitsRemaining > 0) {
                messageText = `${visitsRemaining} visits remaining to complete Care Episode`;
            } else if (visitsRemaining <= 0) {
                messageText = 'All expected visits have been scheduled or completed';
            }
            if (messageEl) messageEl.textContent = messageText;

            // Pre-fill recommended values if visit frequency is available
            if (visitFrequency > 0 && visitsRemaining > 0) {
                this._prefillRecurringFromGuidance(visitFrequency, visitsRemaining);
            }

            // Show the guidance panel
            guidancePanel.style.display = 'block';

        } catch (error) {
            console.error('[AppointmentModule] Error loading Care Episode guidance:', error);
            guidancePanel.style.display = 'none';
            this.careEpisodeGuidanceData = null;
        }
    }

    /**
     * Pre-fill recurring form with Care Episode recommended values
     * @private
     * @param {number} visitFrequency - Recommended visits per week
     * @param {number} visitsNeeded - Remaining visits needed
     */
    _prefillRecurringFromGuidance(visitFrequency, visitsNeeded) {
        // Pre-fill duration based on visits needed and frequency
        if (visitFrequency > 0 && visitsNeeded > 0) {
            const suggestedWeeks = Math.ceil(visitsNeeded / visitFrequency);
            const durationInput = document.getElementById('recurrenceDurationWeeks');
            if (durationInput && !durationInput.dataset.userModified) {
                durationInput.value = Math.min(suggestedWeeks, 52); // Max 52 weeks
            }
        }
        // Note: Days are NOT pre-selected - user should manually choose their preferred days
    }

    /**
     * Update recurring appointments preview (public method for onclick handlers)
     */
    updateRecurringPreview() {
        this._updateRecurringPreview();
    }

    /**
     * Update recurring appointments preview
     * @private
     */
    _updateRecurringPreview() {
        console.log('[AppointmentModule] _updateRecurringPreview called');

        // Get selected days
        const selectedDays = [];
        const dayCheckboxes = ['recur_mon', 'recur_tue', 'recur_wed', 'recur_thu', 'recur_fri', 'recur_sat', 'recur_sun'];
        const dayNames = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

        dayCheckboxes.forEach((id, index) => {
            const checkbox = document.getElementById(id);
            console.log(`[AppointmentModule] Checkbox ${id}:`, checkbox ? `found, checked=${checkbox.checked}` : 'NOT FOUND');
            if (checkbox && checkbox.checked) {
                selectedDays.push({
                    dayOfWeek: index === 6 ? 0 : index + 1, // Convert to JS day (Sun=0)
                    name: dayNames[index]
                });
            }
        });

        console.log('[AppointmentModule] Selected days:', selectedDays);

        // Get duration and time
        const durationWeeks = parseInt(document.getElementById('recurrenceDurationWeeks')?.value) || 4;
        const preferredTime = document.getElementById('recurringPreferredTime')?.value || '09:00';
        const providerSelect = document.getElementById('recurringProviderSelect');
        const providerValue = providerSelect?.value || 'auto';
        const isAutoAssign = providerValue === 'auto';

        // Update provider hint text
        const hintEl = document.getElementById('providerSelectHint');
        if (hintEl) {
            hintEl.style.display = isAutoAssign ? 'block' : 'none';
        }

        const dateListEl = document.getElementById('recurringDateList');
        const totalEl = document.getElementById('recurPreviewTotal');
        const availableCountEl = document.getElementById('recurAvailableCount');
        const conflictCountEl = document.getElementById('recurConflictCount');
        const statusEl = document.getElementById('recurringAvailabilityStatus');
        const loadingEl = document.getElementById('recurringCheckLoading');

        if (!dateListEl) return;

        if (selectedDays.length === 0) {
            dateListEl.innerHTML = '<div class="text-muted text-center py-4"><i class="bi bi-info-circle me-1"></i>Select days to see appointments</div>';
            if (totalEl) totalEl.textContent = '0 total';
            if (statusEl) statusEl.style.display = 'none';
            // Clear recurring config when no days selected
            this.wizardState.recurringConfig = null;
            this._updateWizardButtons();
            return;
        }

        // Generate dates for the recurring series
        const startDate = new Date();
        startDate.setHours(0, 0, 0, 0);
        const endDate = new Date(startDate);
        endDate.setDate(endDate.getDate() + (durationWeeks * 7));

        const appointments = [];
        const currentDate = new Date(startDate);
        const [hours, minutes] = preferredTime.split(':').map(Number);

        while (currentDate <= endDate) {
            const dayOfWeek = currentDate.getDay();
            const matchingDay = selectedDays.find(d => d.dayOfWeek === dayOfWeek);

            if (matchingDay) {
                const apptDate = new Date(currentDate);
                apptDate.setHours(hours, minutes, 0, 0);
                appointments.push({
                    date: apptDate,
                    dayName: matchingDay.name,
                    time: preferredTime
                });
            }

            currentDate.setDate(currentDate.getDate() + 1);
        }

        // Update total count
        if (totalEl) totalEl.textContent = `${appointments.length} total`;

        if (appointments.length === 0) {
            dateListEl.innerHTML = '<div class="text-muted text-center py-4">No appointments in selected range</div>';
            if (statusEl) statusEl.style.display = 'none';
            this.wizardState.recurringConfig = null;
            this._updateWizardButtons();
            return;
        }

        // If auto-assign mode, call backend to get provider assignments
        if (isAutoAssign) {
            this._checkAutoAssignAvailability(appointments, selectedDays, durationWeeks, preferredTime);
        } else {
            // Specific provider selected - use existing availability check
            this._renderRecurringPreviewWithProvider(appointments, parseInt(providerValue), selectedDays, durationWeeks, preferredTime);
        }
    }

    /**
     * Check availability with auto-assign providers
     * @private
     */
    async _checkAutoAssignAvailability(appointments, selectedDays, durationWeeks, preferredTime) {
        const dateListEl = document.getElementById('recurringDateList');
        const loadingEl = document.getElementById('recurringCheckLoading');
        const statusEl = document.getElementById('recurringAvailabilityStatus');
        const availableCountEl = document.getElementById('recurAvailableCount');
        const conflictCountEl = document.getElementById('recurConflictCount');

        // Show loading
        if (loadingEl) loadingEl.style.display = 'block';
        if (dateListEl) dateListEl.innerHTML = '';

        try {
            const locationTz = window.getCurrentLocationTimezone ? window.getCurrentLocationTimezone() : { timeZoneId: 'America/Chicago' };
            const duration = this.wizardState.selectedDuration || 45;

            // Convert dates to UTC
            const proposedDates = appointments.map(appt => {
                if (window.convertTimezoneToUtc) {
                    return window.convertTimezoneToUtc(appt.date, locationTz.timeZoneId).toISOString();
                }
                return appt.date.toISOString();
            });

            const response = await this._apiPost('/appointments/auto-assign-providers', {
                DurationMinutes: duration,
                ProposedDates: proposedDates
            });

            if (loadingEl) loadingEl.style.display = 'none';

            // Render results with provider assignments
            const availableSlots = response.Slots.filter(s => s.IsAvailable);
            const unavailableSlots = response.Slots.filter(s => !s.IsAvailable);

            if (availableCountEl) availableCountEl.textContent = availableSlots.length;
            if (conflictCountEl) conflictCountEl.textContent = unavailableSlots.length;
            if (statusEl) statusEl.style.display = 'block';

            dateListEl.innerHTML = response.Slots.map((slot, index) => {
                if (slot.IsAvailable) {
                    return `
                        <div class="d-flex justify-content-between align-items-center p-2 border-bottom bg-light">
                            <div class="d-flex align-items-center gap-2">
                                <i class="bi bi-check-circle-fill text-success"></i>
                                <div>
                                    <div class="fw-medium">${slot.FormattedDate}</div>
                                    <small class="text-muted">${slot.FormattedTime}</small>
                                </div>
                            </div>
                            <div class="text-end">
                                <span class="badge bg-primary">${this._escape(slot.AssignedProviderName)}</span>
                            </div>
                        </div>
                    `;
                } else {
                    return `
                        <div class="d-flex justify-content-between align-items-center p-2 border-bottom bg-danger-subtle">
                            <div class="d-flex align-items-center gap-2">
                                <i class="bi bi-x-circle-fill text-danger"></i>
                                <div>
                                    <div class="fw-medium">${slot.FormattedDate}</div>
                                    <small class="text-muted">${slot.FormattedTime}</small>
                                </div>
                            </div>
                            <div class="text-end">
                                <small class="text-danger">${this._escape(slot.UnavailableReason || 'Unavailable')}</small>
                            </div>
                        </div>
                    `;
                }
            }).join('');

            // Store the recurring config with auto-assigned providers
            this.wizardState.recurringConfig = {
                selectedDays: selectedDays,
                durationWeeks: durationWeeks,
                preferredTime: preferredTime,
                appointments: response.Slots.filter(s => s.IsAvailable).map(slot => ({
                    date: new Date(slot.ProposedDateTime),
                    providerId: slot.AssignedProviderId,
                    providerName: slot.AssignedProviderName
                })),
                isAutoAssign: true,
                autoAssignResults: response.Slots
            };

            this._updateWizardButtons();
            this._checkRecurringGuidanceWarnings();

        } catch (error) {
            console.error('[AppointmentModule] Auto-assign check failed:', error);
            if (loadingEl) loadingEl.style.display = 'none';
            dateListEl.innerHTML = `<div class="text-danger text-center py-4"><i class="bi bi-exclamation-triangle me-1"></i>Failed to check availability</div>`;
        }
    }

    /**
     * Render preview with specific provider selected
     * @private
     */
    async _renderRecurringPreviewWithProvider(appointments, providerId, selectedDays, durationWeeks, preferredTime) {
        const dateListEl = document.getElementById('recurringDateList');
        const loadingEl = document.getElementById('recurringCheckLoading');
        const statusEl = document.getElementById('recurringAvailabilityStatus');
        const availableCountEl = document.getElementById('recurAvailableCount');
        const conflictCountEl = document.getElementById('recurConflictCount');

        // Show loading
        if (loadingEl) loadingEl.style.display = 'block';
        if (dateListEl) dateListEl.innerHTML = '';

        try {
            const locationTz = window.getCurrentLocationTimezone ? window.getCurrentLocationTimezone() : { timeZoneId: 'America/Chicago' };
            const duration = this.wizardState.selectedDuration || 45;

            // Convert dates to UTC
            const proposedDates = appointments.map(appt => {
                if (window.convertTimezoneToUtc) {
                    return window.convertTimezoneToUtc(appt.date, locationTz.timeZoneId).toISOString();
                }
                return appt.date.toISOString();
            });

            const response = await this._apiPost('/appointments/check-recurring-availability', {
                ProviderId: providerId,
                DurationMinutes: duration,
                ProposedDates: proposedDates
            });

            if (loadingEl) loadingEl.style.display = 'none';

            const availableDates = response.Dates.filter(d => d.IsAvailable);
            const conflictDates = response.Dates.filter(d => !d.IsAvailable);

            if (availableCountEl) availableCountEl.textContent = availableDates.length;
            if (conflictCountEl) conflictCountEl.textContent = conflictDates.length;
            if (statusEl) statusEl.style.display = 'block';

            dateListEl.innerHTML = response.Dates.map((dateInfo, index) => {
                if (dateInfo.IsAvailable) {
                    return `
                        <div class="d-flex justify-content-between align-items-center p-2 border-bottom">
                            <div class="d-flex align-items-center gap-2">
                                <i class="bi bi-check-circle-fill text-success"></i>
                                <div>
                                    <div class="fw-medium">${dateInfo.FormattedDate}</div>
                                    <small class="text-muted">${dateInfo.FormattedTime}</small>
                                </div>
                            </div>
                            <div class="text-end">
                                <span class="badge bg-secondary">${this._escape(response.ProviderName)}</span>
                            </div>
                        </div>
                    `;
                } else {
                    return `
                        <div class="d-flex justify-content-between align-items-center p-2 border-bottom bg-danger-subtle">
                            <div class="d-flex align-items-center gap-2">
                                <i class="bi bi-x-circle-fill text-danger"></i>
                                <div>
                                    <div class="fw-medium">${dateInfo.FormattedDate}</div>
                                    <small class="text-muted">${dateInfo.FormattedTime}</small>
                                </div>
                            </div>
                            <div class="text-end">
                                <small class="text-danger">${this._escape(dateInfo.ConflictReason || 'Conflict')}</small>
                            </div>
                        </div>
                    `;
                }
            }).join('');

            // Store the recurring config
            this.wizardState.recurringConfig = {
                selectedDays: selectedDays,
                durationWeeks: durationWeeks,
                preferredTime: preferredTime,
                appointments: response.Dates.filter(d => d.IsAvailable).map(dateInfo => ({
                    date: new Date(dateInfo.ProposedDateTime),
                    providerId: providerId,
                    providerName: response.ProviderName
                })),
                isAutoAssign: false,
                providerId: providerId,
                providerName: response.ProviderName
            };

            this._updateWizardButtons();
            this._checkRecurringGuidanceWarnings();

        } catch (error) {
            console.error('[AppointmentModule] Availability check failed:', error);
            if (loadingEl) loadingEl.style.display = 'none';

            // Fallback: render without availability check
            const locationTz = window.getCurrentLocationTimezone ? window.getCurrentLocationTimezone() : { timeZoneId: 'America/Chicago' };
            dateListEl.innerHTML = appointments.map((appt, index) => {
                const dateStr = appt.date.toLocaleDateString('en-US', {
                    weekday: 'short',
                    month: 'short',
                    day: 'numeric'
                });
                const timeStr = this._formatTimeString(appt.time);

                return `
                    <div class="d-flex justify-content-between align-items-center p-2 border-bottom">
                        <div>
                            <span class="fw-medium">${dateStr}</span>
                            <span class="text-muted ms-2">${timeStr}</span>
                        </div>
                        <span class="badge bg-secondary">#${index + 1}</span>
                    </div>
                `;
            }).join('');

            if (availableCountEl) availableCountEl.textContent = appointments.length;
            if (conflictCountEl) conflictCountEl.textContent = 0;
            if (statusEl) statusEl.style.display = 'block';

            // Store basic config
            this.wizardState.recurringConfig = {
                selectedDays: selectedDays,
                durationWeeks: durationWeeks,
                preferredTime: preferredTime,
                appointments: appointments.map(appt => ({
                    date: appt.date,
                    providerId: providerId
                })),
                isAutoAssign: false,
                providerId: providerId
            };

            this._updateWizardButtons();
        }
    }

    /**
     * Format time string for display (e.g., "09:00" -> "9:00 AM")
     * @private
     * @param {string} timeStr - Time in HH:MM format
     * @returns {string} Formatted time
     */
    _formatTimeString(timeStr) {
        if (!timeStr) return '';
        const [hours, minutes] = timeStr.split(':').map(Number);
        const period = hours >= 12 ? 'PM' : 'AM';
        const displayHours = hours % 12 || 12;
        return `${displayHours}:${String(minutes).padStart(2, '0')} ${period}`;
    }

    /**
     * Check and show validation warnings for recurring appointments
     * @private
     */
    _checkRecurringGuidanceWarnings() {
        const warningEl = document.getElementById('recurringValidationWarning');
        const warningTextEl = document.getElementById('recurringValidationWarningText');

        if (!warningEl || !warningTextEl || !this.careEpisodeGuidanceData) {
            if (warningEl) warningEl.style.display = 'none';
            return;
        }

        const expectedVisits = this.careEpisodeGuidanceData.ExpectedVisits || 0;
        const visitFrequency = this.careEpisodeGuidanceData.VisitFrequency || 0;

        // Count only Follow-Up/Visit type (Type=1) appointments
        const scheduledFollowUpVisits = (this.careEpisodeGuidanceData.Appointments || []).filter(a =>
            a.Type === 1 &&
            (a.Status === 0 || a.Status === 2 || a.Status === 3 || a.Status === 4)
        ).length;
        const visitsRemaining = expectedVisits - scheduledFollowUpVisits;

        // Get currently configured recurring appointments
        const recurringConfig = this.wizardState.recurringConfig;
        if (!recurringConfig || !recurringConfig.appointments) {
            warningEl.style.display = 'none';
            return;
        }

        const plannedAppointments = recurringConfig.appointments.length;

        // Check if planning more than needed
        if (visitsRemaining > 0 && plannedAppointments > visitsRemaining) {
            warningEl.style.display = 'block';
            warningTextEl.textContent = `You're scheduling ${plannedAppointments} appointments, but only ${visitsRemaining} more visits are needed for this Care Episode.`;
        } else {
            warningEl.style.display = 'none';
        }
    }

    /**
     * Update preview panel (Step 4)
     * @private
     */
    _updatePreviewPanel() {
        const slot = this.wizardState.selectedSlot;
        const patient = this.wizardState.selectedPatient;

        if (!patient) {
            console.warn('[AppointmentModule] Cannot update preview: missing patient');
            return;
        }

        // FIX: Use correct element IDs from _ModalsAppointment.cshtml (confirm*, not preview*)
        const confirmPatient = document.getElementById('confirmPatient');
        const confirmDateTime = document.getElementById('confirmDateTime');
        const confirmProvider = document.getElementById('confirmProvider');
        const confirmDuration = document.getElementById('confirmDuration');
        const confirmType = document.getElementById('confirmType');
        const confirmNotes = document.getElementById('confirmNotes');

        // Update patient
        if (confirmPatient) {
            confirmPatient.textContent = `${patient.FullName} (MRN: ${patient.MRN || 'N/A'})`;
        }

        // Update date & time with timezone
        if (confirmDateTime) {
            let dateTimeDisplay;

            // Handle recurring appointments differently - the recurring section will set this later
            if (this.wizardState.isRecurringPath && this.wizardState.recurringConfig) {
                // Will be set in recurring section below
                dateTimeDisplay = null; // Placeholder, will be updated in recurring section
            } else if (slot) {
                // Slot was selected
                if (window.formatDateTimeWithTimezone && window.formatTimeRangeWithTimezone) {
                    // FIX: formatDateTimeWithTimezone expects properties with capital letters
                    const slotForFormatter = {
                        StartTime: slot.start,
                        EndTime: slot.end,
                        TimeZoneId: slot.timeZoneId,
                        TimeZoneAbbreviation: slot.timeZoneAbbreviation
                    };
                    const dateStr = window.formatDateWithTimezone ? window.formatDateWithTimezone(slotForFormatter) : new Date(slot.start).toLocaleDateString();
                    const timeRange = window.formatTimeRangeWithTimezone ? window.formatTimeRangeWithTimezone(slotForFormatter) : '';
                    dateTimeDisplay = `${dateStr}, ${timeRange}`;
                } else {
                    // Fallback formatting
                    const startDate = new Date(slot.start);
                    const endDate = new Date(slot.end);
                    const dateStr = startDate.toLocaleDateString('en-US', {
                        weekday: 'short',
                        month: 'short',
                        day: 'numeric',
                        year: 'numeric'
                    });
                    const startTime = startDate.toLocaleTimeString('en-US', {
                        hour: 'numeric',
                        minute: '2-digit',
                        hour12: true
                    });
                    const endTime = endDate.toLocaleTimeString('en-US', {
                        hour: 'numeric',
                        minute: '2-digit',
                        hour12: true
                    });
                    const tz = slot.timeZoneAbbreviation || '';
                    dateTimeDisplay = `${dateStr}, ${startTime} - ${endTime} ${tz}`;
                }
            } else {
                // Custom date/time was entered
                const dateInput = document.getElementById('apptDatePicker');
                const timeInput = document.getElementById('apptCustomTime');
                const duration = this.wizardState.selectedDuration || 45;

                if (dateInput?.value && timeInput?.value) {
                    const dateTimeStr = `${dateInput.value}T${timeInput.value}:00`;
                    const startDate = new Date(dateTimeStr);
                    const endDate = new Date(startDate.getTime() + duration * 60000);

                    const dateStr = startDate.toLocaleDateString('en-US', {
                        weekday: 'short',
                        month: 'short',
                        day: 'numeric',
                        year: 'numeric'
                    });
                    const startTime = startDate.toLocaleTimeString('en-US', {
                        hour: 'numeric',
                        minute: '2-digit',
                        hour12: true
                    });
                    const endTime = endDate.toLocaleTimeString('en-US', {
                        hour: 'numeric',
                        minute: '2-digit',
                        hour12: true
                    });

                    const locationTz = window.getCurrentLocationTimezone ? window.getCurrentLocationTimezone() : null;
                    const tz = locationTz?.timeZoneAbbreviation || '';

                    dateTimeDisplay = `${dateStr}, ${startTime} - ${endTime} ${tz}`;
                } else {
                    dateTimeDisplay = '-';
                }
            }

            // Only set if we have a value (recurring path sets it later)
            if (dateTimeDisplay !== null) {
                confirmDateTime.textContent = dateTimeDisplay;
            }
        }

        // Update provider
        if (confirmProvider) {
            let providerName;
            if (slot) {
                providerName = slot.providerName || 'Unknown';
            } else {
                // Get from provider dropdown
                const providerSelect = document.getElementById('apptProviderSelect');
                if (providerSelect && providerSelect.selectedIndex > 0) {
                    providerName = providerSelect.options[providerSelect.selectedIndex].text;
                } else {
                    providerName = 'To be assigned';
                }
            }
            confirmProvider.textContent = providerName;
        }

        // Update duration
        if (confirmDuration) {
            const duration = this.wizardState.selectedDuration || 45;
            confirmDuration.textContent = `${duration} minutes`;
        }

        // Update appointment type
        if (confirmType) {
            const typeName = this.appointmentTypes[this.wizardState.selectedType]?.name || 'Unknown';
            confirmType.textContent = typeName;
        }

        // Update notes
        if (confirmNotes) {
            const notesInput = this.wizardState.isRecurringPath
                ? document.getElementById('recurringNotes')
                : document.getElementById('apptNotes');
            const notesText = notesInput?.value || '-';
            confirmNotes.textContent = notesText;
        }

        // Handle recurring appointments summary
        const confirmRecurringSummary = document.getElementById('confirmRecurringSummary');
        const confirmRecurringPattern = document.getElementById('confirmRecurringPattern');
        const confirmRecurringCount = document.getElementById('confirmRecurringCount');
        const summaryRecurringBadge = document.getElementById('summaryRecurringBadge');

        if (this.wizardState.isRecurringPath && this.wizardState.recurringConfig) {
            const config = this.wizardState.recurringConfig;

            // Show recurring summary section
            if (confirmRecurringSummary) {
                confirmRecurringSummary.classList.remove('d-none');
            }

            // Update pattern (days)
            if (confirmRecurringPattern) {
                const dayNames = config.selectedDays.map(d => d.name).join(', ');
                const timeStr = this._formatTimeString(config.preferredTime);
                confirmRecurringPattern.textContent = `${dayNames} at ${timeStr} for ${config.durationWeeks} weeks`;
            }

            // Update count
            if (confirmRecurringCount) {
                confirmRecurringCount.textContent = config.appointments.length;
            }

            // Show recurring badge in summary
            if (summaryRecurringBadge) {
                summaryRecurringBadge.classList.remove('d-none');
            }

            // Update date/time display for recurring
            if (confirmDateTime) {
                const firstAppt = config.appointments[0];
                const lastAppt = config.appointments[config.appointments.length - 1];
                if (firstAppt && lastAppt) {
                    const startStr = firstAppt.date.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
                    const endStr = lastAppt.date.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
                    confirmDateTime.textContent = `${startStr} - ${endStr} (${config.appointments.length} appointments)`;
                }
            }

            // Update provider for recurring
            if (confirmProvider) {
                const recurringProviderSelect = document.getElementById('recurringProviderSelect');
                if (recurringProviderSelect && recurringProviderSelect.selectedIndex > 0) {
                    confirmProvider.textContent = recurringProviderSelect.options[recurringProviderSelect.selectedIndex].text;
                } else {
                    confirmProvider.textContent = 'Auto-assign available provider';
                }
            }

            console.log('[AppointmentModule] Updated preview panel for recurring:', config);
        } else {
            // Hide recurring summary for single appointments
            if (confirmRecurringSummary) {
                confirmRecurringSummary.classList.add('d-none');
            }
            if (summaryRecurringBadge) {
                summaryRecurringBadge.classList.add('d-none');
            }

            console.log('[AppointmentModule] Updated preview panel with:', { patient: patient.FullName, slot, customTime: !slot });
        }
    }

    // === Detail Modal ===

    /**
     * Render appointment detail modal
     * @private
     * @param {Object} appt - Appointment data
     * @param {boolean} viewOnly - View only mode
     */
    _renderDetailModal(appt, viewOnly) {
        const isCancelled = appt.Status === 6;
        const isMissed = appt.Status === 8 || this._isCalculatedMissed(appt);
        const isRescheduled = appt.RescheduledToAppointmentId != null;
        const isFromReschedule = appt.RescheduledFromAppointmentId != null;
        const isCheckedIn = appt.Status >= 2 && appt.Status !== 6 && appt.Status !== 8;

        // Update detail fields
        this._setElementText('apptDetailProvider', appt.ProviderName || 'Provider');
        this._setElementHtml('apptDetailPatient', `
            <a href="#" class="text-primary text-decoration-none fw-semibold" onclick="var m=bootstrap.Modal.getInstance(document.getElementById('appointmentDetailModal'));if(m)m.hide();setTimeout(function(){viewPatient(${appt.PatientId})},300);return false;" title="View Patient Details">
                ${this._escape(appt.PatientName)}
            </a>
            <span class="text-muted">(${this._escape(appt.PatientMRN)})</span>
        `);

        // Fetch and display outstanding balance below patient name
        const apptBalDiv = document.getElementById('apptDetailPatientBalance');
        const apptBalAmt = document.getElementById('apptDetailBalanceAmount');
        if (apptBalDiv) apptBalDiv.style.display = 'none';
        if (appt.PatientId) {
            this._apiGet(`/payments/patient/${appt.PatientId}/outstanding`)
                .then(bal => {
                    if (bal && bal.CurrentBalance > 0 && apptBalDiv && apptBalAmt) {
                        apptBalAmt.textContent = '$' + bal.CurrentBalance.toFixed(2);
                        apptBalDiv.style.display = 'block';
                    }
                }).catch(() => {});
        }

        // Format time with timezone if available
        let timeDisplay;
        if (appt.StartTimeFormatted && appt.EndTimeFormatted) {
            timeDisplay = `${appt.StartTimeFormatted} - ${appt.EndTimeFormatted}`;
        } else {
            const tz = appt.TimeZoneAbbreviation || '';
            timeDisplay = `${this._formatDateTime(appt.StartTime)} - ${this._formatTime(appt.EndTime)}${tz ? ' ' + tz : ''}`;
        }
        this._setElementText('apptDetailTime', timeDisplay);

        // Extend-end-time control: only for active statuses
        // (Scheduled=0, Confirmed=1, CheckedIn=2, InProgress=3). Server enforces
        // the same whitelist so a stale UI can never push through a bad state.
        const extendBtn = document.getElementById('apptDetailExtendBtn');
        const extendPanel = document.getElementById('apptDetailExtendPanel');
        if (extendBtn) {
            const canExtend = [0, 1, 2, 3].includes(appt.Status) && !viewOnly;
            extendBtn.classList.toggle('d-none', !canExtend);
            // Always start with the panel closed when (re)rendering the modal.
            if (extendPanel) extendPanel.classList.add('d-none');
        }
        this._setElementText('apptDetailType', `Type: ${this._getTypeName(appt.Type)}`);
        this._setElementHtml('apptDetailStatus', `Status: ${this._getStatusBadge(appt.Status)}`);
        this._setElementText('apptDetailReason', appt.Reason || '-');

        // Handle cancelled banner with reschedule info
        const cancelledBanner = document.getElementById('cancelledAppointmentBanner');
        if (cancelledBanner) {
            cancelledBanner.classList.toggle('d-none', !isCancelled);
            const cancelledText = cancelledBanner.querySelector('.cancelled-banner-text');
            if (cancelledText && isCancelled) {
                if (isRescheduled) {
                    cancelledText.textContent = `This appointment was cancelled and rescheduled to ${appt.RescheduledToInfo || 'a new date'}`;
                } else {
                    cancelledText.textContent = 'This appointment has been cancelled';
                }
            }
        }

        // Handle missed banner with appropriate text
        const missedBanner = document.getElementById('missedAppointmentBanner');
        if (missedBanner) {
            missedBanner.classList.toggle('d-none', !isMissed);
            const missedText = missedBanner.querySelector('.missed-banner-text');
            if (missedText && isMissed) {
                if (isRescheduled) {
                    missedText.textContent = `This appointment was missed and rescheduled to ${appt.RescheduledToInfo || 'a new date'}`;
                } else {
                    missedText.textContent = 'This appointment was missed (patient did not check in and the date has passed)';
                }
            }
        }

        // Handle rescheduled-from banner
        const rescheduledFromBanner = document.getElementById('rescheduledFromBanner');
        if (rescheduledFromBanner) {
            rescheduledFromBanner.classList.toggle('d-none', !isFromReschedule);
            const rescheduleText = rescheduledFromBanner.querySelector('.reschedule-from-text');
            if (rescheduleText && isFromReschedule) {
                rescheduleText.textContent = `Rescheduled from ${appt.RescheduledFromInfo || 'a previous appointment'}`;
            }
        }

        // Handle created-by info section
        const createdByInfoSection = document.getElementById('createdByInfoSection');
        if (createdByInfoSection) {
            const hasCreatedBy = appt.CreatedByName || appt.CreatedAt;
            createdByInfoSection.classList.toggle('d-none', !hasCreatedBy);
            if (hasCreatedBy) {
                this._setElementText('apptDetailCreatedByName', appt.CreatedByName || 'Unknown');
                this._setElementText('apptDetailCreatedAt', appt.CreatedAt ? this._formatDateTime(appt.CreatedAt) : '');
            }
        }

        // Handle cancellation info section
        const cancellationInfoSection = document.getElementById('cancellationInfoSection');
        if (cancellationInfoSection) {
            cancellationInfoSection.classList.toggle('d-none', !isCancelled);
            if (isCancelled) {
                this._setElementText('apptDetailCancelledBy', appt.CancelledByUserName || 'Unknown');
                this._setElementText('apptDetailCancelledAt', appt.CancelledAt ? this._formatDateTime(appt.CancelledAt) : '-');
                this._setElementText('apptDetailCancellationReason', appt.CancellationReason || 'No reason provided');
            }
        }

        // Handle Telehealth Link section
        const telehealthLinkSection = document.getElementById('telehealthLinkSection');
        if (telehealthLinkSection) {
            const isTelehealth = appt.IsTelehealth || appt.Type === 5;
            telehealthLinkSection.classList.toggle('d-none', !isTelehealth);
            if (isTelehealth) {
                const linkInput = document.getElementById('apptTelehealthLink');
                const telehealthUrl = `${window.location.origin}/Telehealth/Join/${appt.TelehealthToken || ''}`;
                if (linkInput) linkInput.value = appt.TelehealthToken ? telehealthUrl : 'Link not available';

                // Copy button
                const copyBtn = document.getElementById('copyTelehealthLinkBtn');
                if (copyBtn) {
                    copyBtn.onclick = () => {
                        if (linkInput) navigator.clipboard.writeText(linkInput.value).then(() => {
                            copyBtn.innerHTML = '<i class="bi bi-check-lg me-1"></i>Copied';
                            setTimeout(() => { copyBtn.innerHTML = '<i class="bi bi-clipboard me-1"></i>Copy'; }, 2000);
                        });
                    };
                }

                // Send Email button
                const sendBtn = document.getElementById('sendTelehealthLinkBtn');
                if (sendBtn) {
                    sendBtn.onclick = async () => {
                        sendBtn.disabled = true;
                        sendBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Sending...';
                        try {
                            await window.apiRequest(`/telehealth/send-invite/${appt.AppointmentId}`, {
                                method: 'POST',
                                showLoader: false
                            });
                            sendBtn.innerHTML = '<i class="bi bi-check-lg me-1"></i>Sent';
                            if (typeof Toast !== 'undefined') Toast.success('Telehealth link sent to patient');
                        } catch (err) {
                            sendBtn.innerHTML = '<i class="bi bi-envelope me-1"></i>Send Email';
                            if (typeof Toast !== 'undefined') Toast.error('Failed to send email');
                        } finally {
                            sendBtn.disabled = false;
                            setTimeout(() => { sendBtn.innerHTML = '<i class="bi bi-envelope me-1"></i>Send Email'; }, 3000);
                        }
                    };
                }
            }
        }

        // Handle Quick Actions visibility based on appointment status
        const currentUser = this._getCurrentUser();
        const quickActionsSection = document.getElementById('quickActionsSection');
        const checkInBtn = document.getElementById('checkInFromDetailBtn');
        const startVisitBtn = document.getElementById('startVisitFromDetailBtn');
        const inProgressBadge = document.getElementById('inProgressBadge');

        const isTelehealth = appt.IsTelehealth || appt.Type === 5;
        const role = parseInt(currentUser?.Role ?? -1);
        const isAdminFrontDesk = role === UserRoles.CLINIC_ADMIN || role === UserRoles.FRONT_DESK;
        const isClinicalUser = !isAdminFrontDesk;

        if (isCancelled || isMissed) {
            // Hide quick actions for cancelled and missed appointments
            if (quickActionsSection) quickActionsSection.classList.add('d-none');
        } else {
            if (quickActionsSection) quickActionsSection.classList.remove('d-none');
            // Hide Check In button if already checked in
            if (checkInBtn) checkInBtn.classList.toggle('d-none', isCheckedIn);

            // isActiveVisit: checked in but NOT yet completed — drives action buttons
            const isActiveVisit = isCheckedIn && appt.Status !== 4;

            // Admin/Front Desk: show "In Progress" badge only while visit is active
            if (inProgressBadge) inProgressBadge.classList.toggle('d-none', !(isAdminFrontDesk && isActiveVisit));

            // Start Visit button logic (mirrors Dashboard Today's Appointments)
            if (startVisitBtn) {
                let showStartVisit = false;
                if (isTelehealth && isClinicalUser && appt.Status >= 0 && appt.Status <= 3) {
                    // Telehealth: clinical users can start visit from Scheduled through InProgress
                    showStartVisit = true;
                    startVisitBtn.className = 'btn btn-info text-white';
                    startVisitBtn.innerHTML = appt.Status === 3
                        ? '<i class="bi bi-camera-video-fill me-1"></i>Resume'
                        : '<i class="bi bi-camera-video-fill me-1"></i>Start Encounter';
                } else if (!isTelehealth && isClinicalUser && isActiveVisit) {
                    // Non-telehealth: clinical users can start visit when checked in but not completed
                    showStartVisit = true;
                    if (appt.Status === 3) {
                        startVisitBtn.className = 'btn btn-success';
                        startVisitBtn.innerHTML = '<i class="bi bi-arrow-right me-1"></i>Resume';
                    } else {
                        startVisitBtn.className = 'btn btn-warning';
                        startVisitBtn.innerHTML = '<i class="bi bi-play-fill me-1"></i>Start Visit';
                    }
                }
                startVisitBtn.classList.toggle('d-none', !showStartVisit);
            }
        }

        // Handle footer buttons visibility
        const canEdit = !viewOnly && currentUser?.Role !== 2 && !UserRoles.isMaNurse(parseInt(currentUser?.Role ?? -1));
        const canRescheduleRole = currentUser && (currentUser.Role === 0 || currentUser.Role === 1 || currentUser.Role === 3);
        const editBtn = document.getElementById('editAppointmentBtn');
        const deleteBtn = document.getElementById('deleteAppointmentBtn');
        const reinstateBtn = document.getElementById('reinstateAppointmentBtn');
        const rescheduleBtn = document.getElementById('rescheduleAppointmentBtn');

        if (isCancelled) {
            // For cancelled appointments: hide edit and cancel buttons
            if (editBtn) editBtn.classList.add('d-none');
            if (deleteBtn) deleteBtn.classList.add('d-none');
            // Show reinstate button only for Clinic Admin (role 0 or 1) AND only if not already rescheduled
            if (reinstateBtn) {
                const canReinstate = currentUser && (currentUser.Role === 0 || currentUser.Role === 1) && !isRescheduled;
                reinstateBtn.classList.toggle('d-none', !canReinstate);
                if (canReinstate) {
                    reinstateBtn.onclick = () => {
                        if (typeof openReinstateModal === 'function') {
                            openReinstateModal(appt.AppointmentId);
                        } else {
                            this.reinstate(appt.AppointmentId);
                        }
                    };
                }
            }
            // Show reschedule button for cancelled appointments only if not already rescheduled
            if (rescheduleBtn) {
                const canReschedule = !isRescheduled && canRescheduleRole;
                rescheduleBtn.classList.toggle('d-none', !canReschedule);
                rescheduleBtn.textContent = 'Reschedule';
                if (canReschedule) {
                    rescheduleBtn.onclick = () => this.rescheduleAppointment(appt.AppointmentId);
                }
            }
        } else if (isMissed) {
            // For missed appointments: hide edit and cancel buttons
            if (editBtn) editBtn.classList.add('d-none');
            if (deleteBtn) deleteBtn.classList.add('d-none');
            if (reinstateBtn) reinstateBtn.classList.add('d-none');
            // Show reschedule button only if not already rescheduled
            if (rescheduleBtn) {
                const canReschedule = !isRescheduled && canRescheduleRole;
                rescheduleBtn.classList.toggle('d-none', !canReschedule);
                rescheduleBtn.textContent = 'Reschedule';
                if (canReschedule) {
                    rescheduleBtn.onclick = () => this.rescheduleAppointment(appt.AppointmentId);
                }
            }
        } else if (isCheckedIn) {
            // For checked-in appointments: show edit, hide cancel and reschedule
            if (editBtn) editBtn.classList.toggle('d-none', !canEdit);
            if (deleteBtn) deleteBtn.classList.add('d-none'); // Cannot cancel after check-in
            if (reinstateBtn) reinstateBtn.classList.add('d-none');
            if (rescheduleBtn) rescheduleBtn.classList.add('d-none'); // Cannot reschedule after check-in
        } else {
            // For regular scheduled/confirmed appointments: show edit, cancel, AND reschedule
            if (editBtn) editBtn.classList.toggle('d-none', !canEdit);
            if (deleteBtn) deleteBtn.classList.toggle('d-none', !canEdit);
            if (reinstateBtn) reinstateBtn.classList.add('d-none');
            // Show reschedule button for all non-checked-in appointments
            if (rescheduleBtn) {
                const canReschedule = !isRescheduled && canRescheduleRole;
                rescheduleBtn.classList.toggle('d-none', !canReschedule);
                rescheduleBtn.textContent = 'Reschedule';
                if (canReschedule) {
                    rescheduleBtn.onclick = () => this.rescheduleAppointment(appt.AppointmentId);
                }
            }
        }

        // Wire up edit button
        if (editBtn && canEdit) {
            editBtn.onclick = () => this.edit(appt.AppointmentId);
        }

        // Wire up delete button to use the cancellation modal
        if (deleteBtn && canEdit) {
            deleteBtn.onclick = () => {
                if (typeof openCancelAppointmentModal === 'function') {
                    openCancelAppointmentModal(appt.AppointmentId);
                } else {
                    this.cancel(appt.AppointmentId);
                }
            };
        }

        // Load clinical notes for this appointment
        this._loadAppointmentNotes(appt);
    }

    /**
     * Load clinical notes for an appointment
     * @private
     * @param {Object} appt - Appointment data
     */
    async _loadAppointmentNotes(appt) {
        const notesList = document.getElementById('appointmentNotesList');
        const addNoteBtn = document.getElementById('addNoteBtn');
        const recordSessionBtn = document.getElementById('recordSessionBtn');

        if (!notesList) return;

        const currentUser = this._getCurrentUser();
        const isAssignedProvider = currentUser && currentUser.ProviderId &&
                                   currentUser.ProviderId === appt.ProviderId;
        const canEditNotes = isAssignedProvider;

        try {
            const notes = await this._apiGet(`/clinical-notes/by-appointment/${appt.AppointmentId}`);

            if (notes && notes.length > 0) {
                const allSigned = notes.every(n => n.Status === 2 || n.Status === 4);

                // Show/hide add button and record session button based on whether all notes are signed
                if (addNoteBtn) {
                    addNoteBtn.classList.toggle('d-none', allSigned || !canEditNotes);
                }
                if (recordSessionBtn) {
                    recordSessionBtn.classList.toggle('d-none', allSigned || !canEditNotes);
                }

                // Render notes list
                const noteTypeNames = ['H&P', 'SOAP', 'Office Visit', 'Progress Note', 'Consultation', 'Procedure', 'Annual Wellness', 'Phone Note', 'Referral', 'Lab Review'];
                const statusBadges = {
                    0: '<span class="badge bg-secondary">Draft</span>',
                    1: '<span class="badge bg-warning">Pending Signature</span>',
                    2: '<span class="badge bg-success">Signed</span>',
                    3: '<span class="badge bg-info">Pending Co-Sign</span>',
                    4: '<span class="badge bg-primary">Finalized</span>'
                };

                notesList.innerHTML = notes.map(note => {
                    const noteTypeName = noteTypeNames[note.Type] || 'Note';
                    const displayTitle = note.TemplateName || noteTypeName;
                    return `
                    <div class="d-flex justify-content-between align-items-center py-2 border-bottom clinical-note-row"
                         style="cursor: pointer; transition: background-color 0.15s;"
                         onclick="viewClinicalNote(${note.ClinicalNoteId})"
                         onmouseenter="this.style.backgroundColor='#f0f4ff'"
                         onmouseleave="this.style.backgroundColor='transparent'"
                         title="Click to view note">
                        <div>
                            <strong>${this._escape(displayTitle)}</strong>
                            <br>
                            <small class="text-muted">${this._formatDate(note.ServiceDate)}</small>
                            ${statusBadges[note.Status] || ''}
                        </div>
                        <div>
                            <i class="bi bi-chevron-right text-muted"></i>
                        </div>
                    </div>
                `}).join('');
            } else {
                notesList.innerHTML = '<div class="text-muted small py-2">No clinical notes yet</div>';
                if (addNoteBtn) {
                    addNoteBtn.classList.toggle('d-none', !canEditNotes);
                }
                if (recordSessionBtn) {
                    recordSessionBtn.classList.toggle('d-none', !canEditNotes);
                }
            }

            // Wire up add note button
            if (addNoteBtn && canEditNotes) {
                addNoteBtn.onclick = () => {
                    if (typeof openCreateClinicalNote === 'function') {
                        openCreateClinicalNote(appt);
                    }
                };
            }
        } catch (error) {
            console.error('[AppointmentModule] Load appointment notes error:', error);
            notesList.innerHTML = '<div class="text-danger small py-2">Error loading notes</div>';
        }
    }

    // ============================================================================
    // EXTEND APPOINTMENT (run-over support)
    // ----------------------------------------------------------------------------
    // Reveals an inline editor inside the detail modal letting the user push the
    // end time forward when a visit runs long. Quick presets (+5/+10/+15/+30/+60)
    // submit immediately; the custom <input type="time"> + Save button picks an
    // exact end time in the appointment's location TZ.
    //
    // Server validation lives in AppointmentService.ExtendAppointmentAsync:
    //   - status whitelist (Scheduled/Confirmed/CheckedIn/InProgress)
    //   - NewEndTime > StartTime, ≤ StartTime + 4h, ≠ current EndTime
    //   - same-provider overlap unless { Force: true }
    // On 409 (overlap) we ask the user to confirm; on confirm we resend with Force.
    // ============================================================================

    openExtendInline() {
        const panel = document.getElementById('apptDetailExtendPanel');
        if (!panel || !this.currentAppointment) return;
        panel.classList.remove('d-none');

        // Always reset transient panel state on open so stale UI from a prior
        // submit (loader spinning, lingering error text) never bleeds through.
        document.getElementById('apptExtendLoading')?.classList.add('d-none');
        this._showExtendError(null);

        // Pre-fill the custom-time input with the current end-of-day time in the
        // appointment's location TZ. Robust to missing parseServerDateTime helper.
        const customInput = document.getElementById('apptExtendCustom');
        if (customInput) {
            try {
                const endIso = this.currentAppointment.EndTime;
                if (endIso) {
                    const tz = this.currentAppointment.TimeZoneId
                        || (window.getCurrentLocationTimezone ? window.getCurrentLocationTimezone() : null);
                    const endDate = window.parseServerDateTime ? window.parseServerDateTime(endIso) : new Date(endIso);
                    const parts = new Intl.DateTimeFormat('en-US', {
                        timeZone: tz || undefined,
                        hour12: false, hour: '2-digit', minute: '2-digit'
                    }).formatToParts(endDate);
                    const hh = parts.find(p => p.type === 'hour')?.value || '00';
                    const mm = parts.find(p => p.type === 'minute')?.value || '00';
                    customInput.value = `${hh === '24' ? '00' : hh}:${mm}`;
                }
            } catch { /* leave blank — user picks manually */ }
        }

        // Bind preset + custom + cancel handlers once. Idempotent.
        if (!this._extendHandlersBound) {
            this._extendHandlersBound = true;

            const presets = document.getElementById('apptExtendPresets');
            if (presets) {
                presets.querySelectorAll('button[data-extend-min]').forEach(btn => {
                    btn.addEventListener('click', async () => {
                        const minutes = parseInt(btn.dataset.extendMin, 10);
                        if (!minutes || !this.currentAppointment) return;
                        const currentEnd = window.parseServerDateTime
                            ? window.parseServerDateTime(this.currentAppointment.EndTime)
                            : new Date(this.currentAppointment.EndTime);
                        const newEnd = new Date(currentEnd.getTime() + minutes * 60 * 1000);
                        await this._submitExtend(newEnd, false);
                    });
                });
            }

            const customBtn = document.getElementById('apptExtendCustomBtn');
            if (customBtn) {
                customBtn.addEventListener('click', async () => {
                    const value = (document.getElementById('apptExtendCustom')?.value || '').trim();
                    if (!value || !this.currentAppointment) {
                        this._showExtendError('Please pick a time.');
                        return;
                    }
                    // value is "HH:mm" in the appointment's local (location) TZ.
                    // Combine with the appointment's start date in that TZ, then
                    // convert to UTC for the API.
                    const newEnd = this._combineLocationDateWithLocalTime(this.currentAppointment, value);
                    if (!newEnd) {
                        this._showExtendError("Couldn't parse the time. Please try again.");
                        return;
                    }
                    await this._submitExtend(newEnd, false);
                });
            }

            const cancelBtn = document.getElementById('apptExtendCancelBtn');
            if (cancelBtn) {
                cancelBtn.addEventListener('click', () => this.closeExtendInline());
            }
        }
    }

    closeExtendInline() {
        const panel = document.getElementById('apptDetailExtendPanel');
        if (panel) panel.classList.add('d-none');
        this._showExtendError(null);
    }

    /**
     * Combine the appointment's calendar date (in its location TZ) with a
     * user-picked HH:mm to produce a UTC DateTime. Uses Intl/timezone-aware
     * formatting so DST transitions are respected.
     */
    _combineLocationDateWithLocalTime(appt, hhmm) {
        try {
            const tz = appt.TimeZoneId
                || (window.getCurrentLocationTimezone ? window.getCurrentLocationTimezone() : null)
                || 'America/Chicago';
            const startDate = window.parseServerDateTime
                ? window.parseServerDateTime(appt.StartTime)
                : new Date(appt.StartTime);
            const parts = new Intl.DateTimeFormat('en-US', {
                timeZone: tz, year: 'numeric', month: '2-digit', day: '2-digit'
            }).formatToParts(startDate);
            const yyyy = parts.find(p => p.type === 'year')?.value;
            const mm = parts.find(p => p.type === 'month')?.value;
            const dd = parts.find(p => p.type === 'day')?.value;
            if (!yyyy || !mm || !dd) return null;

            const [hStr, mStr] = hhmm.split(':');
            const h = parseInt(hStr, 10);
            const min = parseInt(mStr, 10);
            if (Number.isNaN(h) || Number.isNaN(min)) return null;

            // Build a tz-aware ISO-like string using the wall-clock components,
            // then resolve to UTC by re-formatting and computing the offset.
            // Simpler: construct a UTC date assuming the wall clock, then
            // correct by the TZ offset at that instant.
            const naiveUtc = new Date(Date.UTC(parseInt(yyyy), parseInt(mm) - 1, parseInt(dd), h, min, 0));
            // Compute offset of naiveUtc when interpreted in tz, in ms.
            const localStr = new Intl.DateTimeFormat('en-US', {
                timeZone: tz, hour12: false,
                year: 'numeric', month: '2-digit', day: '2-digit',
                hour: '2-digit', minute: '2-digit', second: '2-digit'
            }).formatToParts(naiveUtc);
            const lp = Object.fromEntries(localStr.map(p => [p.type, p.value]));
            const localAsUtc = Date.UTC(
                parseInt(lp.year),
                parseInt(lp.month) - 1,
                parseInt(lp.day),
                parseInt(lp.hour === '24' ? '0' : lp.hour),
                parseInt(lp.minute),
                parseInt(lp.second)
            );
            const offsetMs = naiveUtc.getTime() - localAsUtc;
            return new Date(naiveUtc.getTime() + offsetMs);
        } catch {
            return null;
        }
    }

    /**
     * POST /appointments/{id}/extend. Handles success (toast + close + refresh)
     * and the soft-conflict path (confirm dialog → resend with Force).
     */
    async _submitExtend(newEndDate, force) {
        if (!this.currentAppointment) return;
        const id = this.currentAppointment.AppointmentId;

        this._showExtendError(null);
        const loading = document.getElementById('apptExtendLoading');
        if (loading) loading.classList.remove('d-none');

        try {
            const resp = await fetch(`/api/appointments/${id}/extend`, {
                method: 'POST',
                headers: this._getHeaders(),
                body: JSON.stringify({
                    NewEndTime: newEndDate.toISOString(),
                    Force: !!force
                })
            });

            // Parse body regardless of status — server returns the same DTO shape
            // for success, 400, 404, and 409.
            const body = await resp.json().catch(() => ({}));

            if (resp.status === 409 && body?.ErrorCode === 'CONFLICT') {
                // Soft confirm — show overlap, let user proceed with Force.
                if (loading) loading.classList.add('d-none');
                const c = body.Conflict || {};
                const slot = c.StartTimeFormatted && c.EndTimeFormatted
                    ? `${c.StartTimeFormatted} - ${c.EndTimeFormatted}`
                    : '';
                const message =
                    `Extending to this time overlaps with ` +
                    `${c.PatientName || 'another patient'}'s appointment` +
                    `${slot ? ` (${slot})` : ''}.\n\nExtend anyway?`;
                if (window.confirm(message)) {
                    await this._submitExtend(newEndDate, true);
                }
                return;
            }

            if (!resp.ok || !body?.Success) {
                if (loading) loading.classList.add('d-none');
                this._showExtendError(body?.Message || 'Could not extend the appointment.');
                return;
            }

            // Success — hide the loader before doing anything else so it never
            // appears stuck if any of the subsequent steps throws.
            if (loading) loading.classList.add('d-none');

            // Update local state, re-render the detail modal so the time display
            // refreshes, emit an event so the calendar updates.
            if (body.NewEndTime) {
                this.currentAppointment.EndTime = body.NewEndTime;
                // Clear formatted strings; _renderDetailModal will rebuild them.
                this.currentAppointment.EndTimeFormatted = null;
            }
            this._renderDetailModal(this.currentAppointment, false);
            this.closeExtendInline();
            this._showSuccess?.('End time updated');
            this._emit('appointments:updated', { appointmentId: id });
        } catch (err) {
            if (loading) loading.classList.add('d-none');
            this._showExtendError(err?.message || 'Network error. Please try again.');
        }
    }

    _showExtendError(msg) {
        const el = document.getElementById('apptExtendError');
        if (!el) return;
        if (!msg) { el.classList.add('d-none'); el.textContent = ''; return; }
        el.textContent = msg;
        el.classList.remove('d-none');
    }

    /**
     * Format date for display
     * @private
     * @param {string} dateStr - Date string
     * @returns {string} Formatted date
     */
    _formatDate(dateStr) {
        if (!dateStr) return '-';
        try {
            // Use parseServerDateTime to correctly interpret server UTC times
            const date = window.parseServerDateTime ? window.parseServerDateTime(dateStr) : new Date(dateStr);
            const locationTz = window.getCurrentLocationTimezone ? window.getCurrentLocationTimezone() : null;
            const options = { month: 'short', day: 'numeric', year: 'numeric' };
            if (locationTz?.timeZoneId) {
                options.timeZone = locationTz.timeZoneId;
            }
            return date.toLocaleDateString('en-US', options);
        } catch {
            return dateStr;
        }
    }

    /**
     * Check if appointment is calculated as missed
     * @private
     * @param {Object} appt - Appointment data
     * @returns {boolean}
     */
    _isCalculatedMissed(appt) {
        if (appt.Status !== 0 && appt.Status !== 1) return false;
        const now = new Date();
        // Use parseServerDateTime to correctly interpret server UTC times
        const aptDate = window.parseServerDateTime ? window.parseServerDateTime(appt.StartTime) : new Date(appt.StartTime);
        return aptDate < now && aptDate.toDateString() !== now.toDateString();
    }

    /**
     * Get appointment type name
     * @private
     * @param {number} type - Type ID
     * @returns {string}
     */
    _getTypeName(type) {
        return this.appointmentTypes[type]?.name || 'Appointment';
    }

    /**
     * Get status badge HTML
     * @private
     * @param {number} status - Status code
     * @returns {string}
     */
    _getStatusBadge(status) {
        const s = this.statuses[status] || { name: 'Unknown', class: 'bg-secondary' };
        return `<span class="badge ${s.class}">${s.name}</span>`;
    }

    // === API Methods ===

    async _apiGet(url, options = {}) {
        if (this.api) {
            return this.api.get(url, options);
        }
        const response = await fetch(`/api${url}`, {
            headers: this._getHeaders()
        });
        if (!response.ok) {
            const error = await response.json().catch(() => ({}));
            throw new Error(error.message || 'API request failed');
        }
        return response.json();
    }

    /**
     * Silent API GET - doesn't show error toasts for expected errors like 404
     * Use this for checking optional resources like Care Episodes
     * @private
     */
    async _apiGetSilent(url) {
        const response = await fetch(`/api${url}`, {
            headers: this._getHeaders()
        });
        if (!response.ok) {
            // Don't throw for 404 - just return null
            if (response.status === 404) {
                return null;
            }
            const error = await response.json().catch(() => ({}));
            throw new Error(error.message || 'API request failed');
        }
        return response.json();
    }

    async _apiPost(url, data = {}) {
        if (this.api) {
            return this.api.post(url, data);
        }
        const response = await fetch(`/api${url}`, {
            method: 'POST',
            headers: this._getHeaders(),
            body: JSON.stringify(data)
        });
        if (!response.ok) {
            const error = await response.json().catch(() => ({}));
            throw new Error(error.message || 'API request failed');
        }
        return response.json();
    }

    async _apiPut(url, data) {
        if (this.api) {
            return this.api.put(url, data);
        }
        const response = await fetch(`/api${url}`, {
            method: 'PUT',
            headers: this._getHeaders(),
            body: JSON.stringify(data)
        });
        if (!response.ok) {
            const error = await response.json().catch(() => ({}));
            throw new Error(error.message || 'API request failed');
        }
        return response.json();
    }

    async _loadProviderDropdown(selectId) {
        try {
            const providers = await this._apiGet('/providers?activeOnly=true');
            const select = document.getElementById(selectId);
            if (!select) return;

            // Use "Any Available Provider" for recurring provider select, "Select Provider" for others
            const defaultOption = selectId === 'recurringProviderSelect'
                ? '<option value="auto">Any Available Provider</option>'
                : '<option value="">Select Provider</option>';

            select.innerHTML = defaultOption +
                (providers || []).map(p =>
                    `<option value="${p.ProviderId}">${this._escape(p.FullName)}</option>`
                ).join('');
        } catch (error) {
            console.error('[AppointmentModule] Load providers error:', error);
        }
    }

    /**
     * Load providers with optional pre-selection
     * @private
     * @param {string} selectId - ID of the select element
     * @param {number} preSelectId - Provider ID to pre-select
     */
    async _loadProviders(selectId, preSelectId = null) {
        try {
            const providers = await this._apiGet('/providers?activeOnly=true');
            const select = document.getElementById(selectId);
            if (!select) return;

            select.innerHTML = '<option value="">Select Provider</option>' +
                (providers || []).map(p =>
                    `<option value="${p.ProviderId}" ${preSelectId && preSelectId == p.ProviderId ? 'selected' : ''}>
                        ${this._escape(p.FullName)} - ${this._escape(p.Specialty || 'Provider')}
                    </option>`
                ).join('');

            // Ensure the value is set
            if (preSelectId) {
                select.value = preSelectId;
            }
        } catch (error) {
            console.error('[AppointmentModule] Load providers error:', error);
            const select = document.getElementById(selectId);
            if (select) {
                select.innerHTML = '<option value="">Error loading providers</option>';
            }
        }
    }

    _getHeaders() {
        const token = localStorage.getItem('authToken');
        return {
            'Content-Type': 'application/json',
            'Authorization': token ? `Bearer ${token}` : ''
        };
    }

    // === Utility Methods ===

    _getCurrentUser() {
        try {
            return JSON.parse(localStorage.getItem('currentUser'));
        } catch {
            return null;
        }
    }

    _escape(str) {
        if (str === null || str === undefined) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    }

    _formatDateTime(dateStr) {
        if (!dateStr) return '-';
        try {
            // Use parseServerDateTime to correctly interpret server UTC times
            const date = window.parseServerDateTime ? window.parseServerDateTime(dateStr) : new Date(dateStr);
            const locationTz = window.getCurrentLocationTimezone ? window.getCurrentLocationTimezone() : null;
            const options = {
                month: 'short', day: 'numeric', year: 'numeric',
                hour: 'numeric', minute: '2-digit', hour12: true
            };
            if (locationTz?.timeZoneId) {
                options.timeZone = locationTz.timeZoneId;
            }
            return date.toLocaleString('en-US', options);
        } catch {
            return dateStr;
        }
    }

    _formatTime(dateStr) {
        if (!dateStr) return '-';
        try {
            // Use parseServerDateTime to correctly interpret server UTC times
            const date = window.parseServerDateTime ? window.parseServerDateTime(dateStr) : new Date(dateStr);
            const locationTz = window.getCurrentLocationTimezone ? window.getCurrentLocationTimezone() : null;
            const options = { hour: 'numeric', minute: '2-digit', hour12: true };
            if (locationTz?.timeZoneId) {
                options.timeZone = locationTz.timeZoneId;
            }
            return date.toLocaleTimeString('en-US', options);
        } catch {
            return dateStr;
        }
    }

    _debounce(fn, delay) {
        let timeoutId;
        return (...args) => {
            clearTimeout(timeoutId);
            timeoutId = setTimeout(() => fn.apply(this, args), delay);
        };
    }

    _setElementText(id, text) {
        const el = document.getElementById(id);
        if (el) el.textContent = text;
    }

    _setElementHtml(id, html) {
        const el = document.getElementById(id);
        if (el) el.innerHTML = html;
    }

    _toggleElement(id, show) {
        const el = document.getElementById(id);
        if (el) el.classList.toggle('d-none', !show);
    }

    _showModal(id) {
        console.log('[AppointmentModule] _showModal called for:', id);
        const el = document.getElementById(id);
        console.log('[AppointmentModule] Modal element found:', !!el);
        console.log('[AppointmentModule] Bootstrap available:', !!window.bootstrap);

        if (el && window.bootstrap) {
            console.log('[AppointmentModule] Getting or creating Bootstrap modal...');
            // Use getOrCreateInstance to avoid creating multiple instances
            const modal = bootstrap.Modal.getOrCreateInstance(el);
            modal.show();
            console.log('[AppointmentModule] Modal.show() called');
        } else {
            console.error('[AppointmentModule] Cannot show modal - element or bootstrap not available');
        }
    }

    _hideModal(id) {
        const el = document.getElementById(id);
        if (el && window.bootstrap) {
            const modal = bootstrap.Modal.getInstance(el);
            if (modal) {
                modal.hide();
                // Remove any lingering backdrops
                setTimeout(() => {
                    const backdrops = document.querySelectorAll('.modal-backdrop');
                    backdrops.forEach(backdrop => {
                        if (!document.querySelector('.modal.show')) {
                            backdrop.remove();
                        }
                    });
                    // Also ensure body classes are cleaned up
                    if (!document.querySelector('.modal.show')) {
                        document.body.classList.remove('modal-open');
                        document.body.style.removeProperty('overflow');
                        document.body.style.removeProperty('padding-right');
                    }
                }, 300);
            }
        }
    }

    async _confirm(options) {
        if (window.ConfirmDialog) {
            return ConfirmDialog.show(options);
        }
        return confirm(options.message);
    }

    _showSuccess(message) {
        if (window.Toast) {
            Toast.success('Success', message);
        }
    }

    _showError(message) {
        if (window.Toast) {
            Toast.error('Error', message);
        }
    }

    _showWarning(message) {
        if (window.Toast) {
            Toast.warning('Warning', message);
        }
    }

    _emit(event, data = {}) {
        if (this.eventBus) {
            this.eventBus.emit(event, data);
        }
    }

    // === Public Preselect Methods ===

    /**
     * Preselect a patient by ID
     * @param {number} patientId - Patient ID to preselect
     */
    async preselectPatient(patientId) {
        try {
            const patient = await this._apiGet(`/patients/${patientId}/autocomplete`);
            if (patient) {
                await this._selectPatient(patient);
            }
        } catch (error) {
            console.error('[AppointmentModule] Failed to preselect patient:', error);
        }
    }

    /**
     * Open appointment wizard for Initial Evaluation
     * Used after creating a new patient to schedule their first appointment
     * @param {number} patientId - Patient ID
     */
    async openForInitialEvaluation(patientId) {
        try {
            // Reset wizard state
            this._resetWizardState();

            // Update modal title
            const modalTitle = document.querySelector('#appointmentModal .modal-title');
            if (modalTitle) {
                modalTitle.innerHTML = '<i class="bi bi-calendar-plus me-2"></i>Schedule New Patient Visit';
            }

            // Load providers
            await this._loadProviders('apptProviderSelect');

            // Load recent patients
            await this._loadRecentPatients();

            // Bind modal event handlers
            this._bindModalEvents();

            // Fetch patient data
            const patient = await this._apiGet(`/patients/${patientId}/autocomplete`);
            if (!patient) {
                this._showError('Failed to load patient data');
                return;
            }

            // Set patient in wizard state
            this.wizardState.selectedPatient = patient;
            document.getElementById('apptPatientId').value = patient.PatientId;

            // For New Patient Visit, no care episode context needed
            this.wizardState.hasActiveCareEpisode = false;
            this.wizardState.careEpisodeId = null;

            // Display selected patient
            this._displaySelectedPatient(patient);

            // Update type cards availability
            this._updateTypeCardsAvailability();

            // Pre-select New Patient Visit (Type 0)
            this.wizardState.selectedType = 0;
            this._selectTypeCard(0);

            // Show duration selection section
            const durationSection = document.getElementById('durationSelectionSection');
            if (durationSection) {
                durationSection.classList.remove('d-none');
            }

            // Update wizard summary
            this._updateWizardSummary();

            // Show wizard at step 1 (patient step is pre-filled, user can confirm and proceed)
            this._wizardShowStep(1);
            this._showModal('appointmentModal');

            console.log('[AppointmentModule] Opened for New Patient Visit, patient:', patient.FullName);

        } catch (error) {
            console.error('[AppointmentModule] Failed to open for New Patient Visit:', error);
            this._showError('Failed to open appointment wizard');
        }
    }

    /**
     * Preselect a patient with a specific care episode context
     * Used when scheduling from Require Schedule widget
     * @param {number} patientId - Patient ID
     * @param {number} careEpisodeId - Care Episode ID
     */
    async preselectPatientWithCareEpisode(patientId, careEpisodeId) {
        try {
            const patient = await this._apiGet(`/patients/${patientId}/autocomplete`);
            if (patient) {
                // Set patient
                this.wizardState.selectedPatient = patient;
                document.getElementById('apptPatientId').value = patient.PatientId;

                // Force set the care episode (bypassing the API call in _selectPatient)
                if (careEpisodeId) {
                    this.wizardState.hasActiveCareEpisode = true;
                    this.wizardState.careEpisodeId = careEpisodeId;
                    console.log('[AppointmentModule] Pre-set Care Episode from Require Schedule:', careEpisodeId);
                } else {
                    // Try to fetch active care episode
                    try {
                        const activeEpisode = await this._apiGetSilent(`/care-episodes/patient/${patientId}/active`);
                        if (activeEpisode && activeEpisode.CareEpisodeId) {
                            this.wizardState.hasActiveCareEpisode = true;
                            this.wizardState.careEpisodeId = activeEpisode.CareEpisodeId;
                        } else {
                            this.wizardState.hasActiveCareEpisode = false;
                            this.wizardState.careEpisodeId = null;
                        }
                    } catch (err) {
                        this.wizardState.hasActiveCareEpisode = false;
                        this.wizardState.careEpisodeId = null;
                    }
                }

                // Update type cards availability
                this._updateTypeCardsAvailability();

                // Auto-select Follow-Up type when coming from Require Schedule (since that's what they need)
                if (this.wizardState.hasActiveCareEpisode) {
                    this.wizardState.selectedType = 1; // Follow Up/Visit
                    this._selectTypeCard(1);
                    console.log('[AppointmentModule] Auto-selected Follow Up/Visit for Require Schedule patient');
                } else {
                    this.wizardState.selectedType = 0; // Initial Evaluation
                    this._selectTypeCard(0);
                }

                // Hide search results and search container
                document.getElementById('patientSearchResults')?.classList.add('d-none');
                document.getElementById('apptPatientContainer')?.classList.add('d-none');
                document.getElementById('recentPatientsSection')?.classList.add('d-none');

                // Show selected patient display
                const display = document.getElementById('selectedPatientDisplay');
                if (display) {
                    display.classList.remove('d-none');
                }

                // Update selected patient details
                const nameEl = document.getElementById('selectedPatientName');
                const infoEl = document.getElementById('selectedPatientInfo');

                if (nameEl) nameEl.textContent = patient.FullName;
                if (infoEl) infoEl.textContent = `MRN: ${patient.MRN}`;

                // Update summary and enable Next button
                this._updateWizardSummary();
                this._updateWizardButtons();

                console.log('[AppointmentModule] Preselected patient with Care Episode:', patient.FullName);
            }
        } catch (error) {
            console.error('[AppointmentModule] Failed to preselect patient with care episode:', error);
        }
    }

    /**
     * Destroy the module and clean up
     */
    destroy() {
        this.currentAppointment = null;
        this._resetWizardState();
        this.isInitialized = false;
    }
}

// Export for module usage
window.AppointmentModule = AppointmentModule;
