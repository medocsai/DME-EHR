/**
 * PatientAppointmentsNotesModule - Handles patient appointments and clinical notes display
 * within the Patient Profile modal's "Appt / Notes" tab.
 *
 * This module provides:
 * - Visits sub-tab: Shows all checked-in appointments
 * - Upcoming sub-tab: Shows future scheduled appointments
 * - Clinical Notes sub-tab: Shows all clinical notes for the patient
 */

class PatientAppointmentsNotesModule {
    constructor(options = {}) {
        this.api = options.api;
        this.patientId = null;
        this.currentSubTab = 'visits';

        // Data storage
        this.visits = [];
        this.upcomingAppointments = [];
        this.clinicalNotes = [];

        // Loading states
        this.isLoadingVisits = false;
        this.isLoadingUpcoming = false;
        this.isLoadingNotes = false;

        // Status mappings
        this.appointmentStatuses = {
            0: { name: 'Scheduled', class: 'bg-secondary' },
            1: { name: 'Confirmed', class: 'bg-info' },
            2: { name: 'Checked In', class: 'bg-primary' },
            3: { name: 'In Progress', class: 'bg-warning text-dark' },
            4: { name: 'Completed', class: 'bg-success' },
            5: { name: 'No Show', class: 'bg-danger' },
            6: { name: 'Cancelled', class: 'bg-danger' },
            8: { name: 'Missed', class: 'bg-dark' }
        };

        this.appointmentTypes = {
            0: { name: 'New Patient', icon: 'bi-person-plus', color: '#4CAF50' },
            1: { name: 'Follow-Up', icon: 'bi-arrow-repeat', color: '#2196F3' },
            2: { name: 'Annual Physical', icon: 'bi-clipboard2-pulse', color: '#9C27B0' },
            3: { name: 'Wellness', icon: 'bi-heart-pulse', color: '#FF9800' },
            4: { name: 'Consultation', icon: 'bi-chat-dots', color: '#00BCD4' },
            5: { name: 'Telehealth', icon: 'bi-camera-video', color: '#607D8B' },
            6: { name: 'Procedure', icon: 'bi-bandaid', color: '#E91E63' },
            7: { name: 'Urgent', icon: 'bi-exclamation-triangle', color: '#F44336' },
            8: { name: 'Lab Review', icon: 'bi-droplet', color: '#795548' },
            9: { name: 'Med Review', icon: 'bi-capsule', color: '#009688' }
        };

        this.noteStatuses = {
            0: { name: 'Draft', class: 'bg-warning text-dark' },
            1: { name: 'Pending Signature', class: 'bg-info' },
            2: { name: 'Signed', class: 'bg-success' },
            3: { name: 'Amended', class: 'bg-primary' },
            4: { name: 'Finalized', class: 'bg-success' }
        };

        this.noteTypes = {
            0: 'History & Physical',
            1: 'SOAP Note',
            2: 'Office Visit Note',
            3: 'Progress Note',
            4: 'Consultation Note',
            5: 'Procedure Note',
            6: 'Annual Wellness Note',
            7: 'Phone Note',
            8: 'Referral Letter',
            9: 'Lab Review Note'
        };

        // Keep these aligned with the server's AppointmentDocumentationStatus enum
        // (Models/Enums/AllEnums.cs). Historically this map had 4 entries (including
        // a stale "Needs Signature" for status 2), which caused completed visits
        // with signed notes to render a misleading "Needs Signature" pill.
        // Now we show a pill ONLY when the status is actionable (InProgress).
        this.documentationStatuses = {
            0: { name: '', class: '', icon: '' },                                               // NotApplicable — hide
            1: { name: 'In Progress', class: 'text-warning', icon: 'bi-hourglass-split' },       // InProgress
            2: { name: '', class: '', icon: '' }                                                 // Complete — the appointment status pill already says "Completed"
        };
    }

    /**
     * Initialize the module for a specific patient
     * @param {number} patientId - The patient ID to load data for
     */
    async initialize(patientId) {
        this.patientId = patientId;
        this.currentSubTab = 'visits';

        // Clear previous data
        this.visits = [];
        this.upcomingAppointments = [];
        this.clinicalNotes = [];
        this._cachedAppointments = null;
        this._cachedNotes = null;

        // Render the tab structure
        this._renderTabStructure();

        // Bind sub-tab events
        this._bindSubTabEvents();

        // Load all counts and visits data - counts first, then use cached data for visits
        await this._loadAllCountsAndVisits();
    }

    /**
     * Load all counts and visits data together efficiently
     * @private
     */
    async _loadAllCountsAndVisits() {
        if (!this.patientId) return;

        this.isLoadingVisits = true;
        const container = document.getElementById('visitsContainer');
        if (container) {
            container.innerHTML = this._renderLoadingState();
        }

        const headers = this._getHeaders();

        try {
            // Fetch appointments and notes in parallel
            const [appointmentsResponse, notesResponse] = await Promise.all([
                fetch(`/api/appointments?patientId=${this.patientId}`, { headers }),
                fetch(`/api/clinical-notes?patientId=${this.patientId}`, { headers })
            ]);

            // Process appointments
            if (appointmentsResponse.ok) {
                const appointments = await appointmentsResponse.json();
                const now = new Date();

                // Calculate and update visits count
                this.visits = appointments.filter(a =>
                    a.Status >= 2 && a.Status !== 6 && a.Status !== 8
                ).sort((a, b) => new Date(b.StartTime) - new Date(a.StartTime));
                this._updateCount('visitsCount', this.visits.length);

                // Calculate and update upcoming count
                const upcomingCount = appointments.filter(a => {
                    const apptDate = new Date(a.StartTime);
                    return apptDate > now && (a.Status === 0 || a.Status === 1);
                }).length;
                this._updateCount('upcomingCount', upcomingCount);

                // Render visits
                this._renderVisits();
            } else {
                throw new Error('Failed to load appointments');
            }

            // Process clinical notes count
            if (notesResponse.ok) {
                const notes = await notesResponse.json();
                this._cachedNotes = notes; // Cache for later use
                this._updateCount('notesCount', notes.length);
            }
        } catch (error) {
            console.error('[PatientAppointmentsNotesModule] Error loading data:', error);
            if (container) {
                container.innerHTML = this._renderErrorState('Failed to load visits');
            }
        } finally {
            this.isLoadingVisits = false;
        }
    }

    /**
     * Render the main tab structure with sub-tabs
     * @private
     */
    _renderTabStructure() {
        const container = document.getElementById('patientApptNotesContainer');
        if (!container) return;

        container.innerHTML = `
            <div class="patient-appt-notes-container">
                <!-- Sub-tab Navigation -->
                <ul class="nav nav-pills nav-fill mb-3" id="apptNotesSubTabs" role="tablist">
                    <li class="nav-item" role="presentation">
                        <button class="nav-link active" id="visits-subtab" data-subtab="visits" type="button">
                            <i class="bi bi-calendar-check me-1"></i>Visits
                            <span class="badge bg-secondary ms-1" id="visitsCount">0</span>
                        </button>
                    </li>
                    <li class="nav-item" role="presentation">
                        <button class="nav-link" id="upcoming-subtab" data-subtab="upcoming" type="button">
                            <i class="bi bi-calendar-event me-1"></i>Upcoming
                            <span class="badge bg-secondary ms-1" id="upcomingCount">0</span>
                        </button>
                    </li>
                    <li class="nav-item" role="presentation">
                        <button class="nav-link" id="notes-subtab" data-subtab="notes" type="button">
                            <i class="bi bi-file-earmark-medical me-1"></i>Clinical Notes
                            <span class="badge bg-secondary ms-1" id="notesCount">0</span>
                        </button>
                    </li>
                </ul>

                <!-- Sub-tab Content -->
                <div class="tab-content" id="apptNotesSubTabContent">
                    <div class="tab-pane fade show active" id="visits-content" role="tabpanel">
                        <div id="visitsContainer">
                            ${this._renderLoadingState()}
                        </div>
                    </div>
                    <div class="tab-pane fade" id="upcoming-content" role="tabpanel">
                        <div id="upcomingContainer">
                            ${this._renderLoadingState()}
                        </div>
                    </div>
                    <div class="tab-pane fade" id="notes-content" role="tabpanel">
                        <div id="notesContainer">
                            ${this._renderLoadingState()}
                        </div>
                    </div>
                </div>
            </div>
        `;
    }

    /**
     * Bind sub-tab click events
     * @private
     */
    _bindSubTabEvents() {
        const subTabs = document.querySelectorAll('#apptNotesSubTabs button[data-subtab]');
        subTabs.forEach(tab => {
            tab.addEventListener('click', async (e) => {
                const subtab = e.currentTarget.dataset.subtab;
                await this._switchSubTab(subtab);
            });
        });
    }

    /**
     * Switch between sub-tabs
     * @param {string} subtab - The subtab to switch to
     * @private
     */
    async _switchSubTab(subtab) {
        // Update active state
        document.querySelectorAll('#apptNotesSubTabs button').forEach(btn => {
            btn.classList.remove('active');
        });
        document.querySelector(`#apptNotesSubTabs button[data-subtab="${subtab}"]`)?.classList.add('active');

        // Update content visibility
        document.querySelectorAll('#apptNotesSubTabContent .tab-pane').forEach(pane => {
            pane.classList.remove('show', 'active');
        });
        document.getElementById(`${subtab}-content`)?.classList.add('show', 'active');

        this.currentSubTab = subtab;

        // Load data for the tab if not already loaded
        switch (subtab) {
            case 'visits':
                if (this.visits.length === 0 && !this.isLoadingVisits) {
                    await this._loadVisits();
                }
                break;
            case 'upcoming':
                if (this.upcomingAppointments.length === 0 && !this.isLoadingUpcoming) {
                    await this._loadUpcoming();
                }
                break;
            case 'notes':
                if (this.clinicalNotes.length === 0 && !this.isLoadingNotes) {
                    await this._loadNotes();
                }
                break;
        }
    }

    /**
     * Load visits (checked-in appointments) - used for refresh/reload
     * @private
     */
    async _loadVisits() {
        if (!this.patientId || this.isLoadingVisits) return;

        // Skip if visits already loaded (initial load handled by _loadAllCountsAndVisits)
        if (this.visits.length > 0) {
            this._renderVisits();
            return;
        }

        this.isLoadingVisits = true;
        const container = document.getElementById('visitsContainer');
        if (container) {
            container.innerHTML = this._renderLoadingState();
        }

        try {
            const headers = this._getHeaders();
            const response = await fetch(`/api/appointments?patientId=${this.patientId}`, { headers });

            if (!response.ok) throw new Error('Failed to load appointments');

            const appointments = await response.json();

            // Filter for checked-in appointments (status >= 2, excluding cancelled/missed)
            this.visits = appointments.filter(a =>
                a.Status >= 2 && a.Status !== 6 && a.Status !== 8
            ).sort((a, b) => new Date(b.StartTime) - new Date(a.StartTime));

            this._renderVisits();
            this._updateCount('visitsCount', this.visits.length);
        } catch (error) {
            console.error('[PatientAppointmentsNotesModule] Error loading visits:', error);
            if (container) {
                container.innerHTML = this._renderErrorState('Failed to load visits');
            }
        } finally {
            this.isLoadingVisits = false;
        }
    }

    /**
     * Load upcoming appointments
     * @private
     */
    async _loadUpcoming() {
        if (!this.patientId || this.isLoadingUpcoming) return;

        this.isLoadingUpcoming = true;
        const container = document.getElementById('upcomingContainer');
        if (container) {
            container.innerHTML = this._renderLoadingState();
        }

        try {
            const headers = this._getHeaders();
            // Get future appointments
            const now = new Date().toISOString();
            const response = await fetch(`/api/appointments?patientId=${this.patientId}&startDate=${now}`, { headers });

            if (!response.ok) throw new Error('Failed to load appointments');

            const appointments = await response.json();

            // Filter for scheduled/confirmed appointments only (status 0 or 1)
            this.upcomingAppointments = appointments.filter(a =>
                a.Status === 0 || a.Status === 1
            ).sort((a, b) => new Date(a.StartTime) - new Date(b.StartTime));

            this._renderUpcoming();
            this._updateCount('upcomingCount', this.upcomingAppointments.length);
        } catch (error) {
            console.error('[PatientAppointmentsNotesModule] Error loading upcoming:', error);
            if (container) {
                container.innerHTML = this._renderErrorState('Failed to load upcoming appointments');
            }
        } finally {
            this.isLoadingUpcoming = false;
        }
    }

    /**
     * Load clinical notes
     * @private
     */
    async _loadNotes() {
        if (!this.patientId || this.isLoadingNotes) return;

        this.isLoadingNotes = true;
        const container = document.getElementById('notesContainer');
        if (container) {
            container.innerHTML = this._renderLoadingState();
        }

        try {
            // Use cached notes if available (from _loadAllCountsAndVisits)
            if (this._cachedNotes) {
                this.clinicalNotes = this._cachedNotes;
                this._cachedNotes = null; // Clear cache after use
            } else {
                const headers = this._getHeaders();
                const response = await fetch(`/api/clinical-notes?patientId=${this.patientId}`, { headers });

                if (!response.ok) throw new Error('Failed to load clinical notes');

                this.clinicalNotes = await response.json();
            }

            // Sort by date (most recent first)
            this.clinicalNotes.sort((a, b) => new Date(b.ServiceDate) - new Date(a.ServiceDate));

            this._renderNotes();
            this._updateCount('notesCount', this.clinicalNotes.length);
        } catch (error) {
            console.error('[PatientAppointmentsNotesModule] Error loading notes:', error);
            if (container) {
                container.innerHTML = this._renderErrorState('Failed to load clinical notes');
            }
        } finally {
            this.isLoadingNotes = false;
        }
    }

    /**
     * Render visits list
     * @private
     */
    _renderVisits() {
        const container = document.getElementById('visitsContainer');
        if (!container) return;

        if (this.visits.length === 0) {
            container.innerHTML = this._renderEmptyState('No visits yet', 'bi-calendar-x', 'This patient has no checked-in appointments.');
            return;
        }

        container.innerHTML = `
            <div class="visits-list">
                ${this.visits.map(visit => this._renderVisitCard(visit)).join('')}
            </div>
        `;

        // Bind card click events
        this._bindCardClickEvents(container, 'visit');
    }

    /**
     * Render upcoming appointments list
     * @private
     */
    _renderUpcoming() {
        const container = document.getElementById('upcomingContainer');
        if (!container) return;

        if (this.upcomingAppointments.length === 0) {
            container.innerHTML = this._renderEmptyState('No upcoming appointments', 'bi-calendar-plus', 'This patient has no scheduled appointments.');
            return;
        }

        container.innerHTML = `
            <div class="upcoming-list">
                ${this.upcomingAppointments.map(appt => this._renderAppointmentCard(appt)).join('')}
            </div>
        `;

        // Bind card click events
        this._bindCardClickEvents(container, 'appointment');
    }

    /**
     * Render clinical notes list
     * @private
     */
    _renderNotes() {
        const container = document.getElementById('notesContainer');
        if (!container) return;

        if (this.clinicalNotes.length === 0) {
            container.innerHTML = this._renderEmptyState('No clinical notes', 'bi-file-earmark-x', 'This patient has no clinical notes on file.');
            return;
        }

        container.innerHTML = `
            <div class="notes-list">
                ${this.clinicalNotes.map(note => this._renderNoteCard(note)).join('')}
            </div>
        `;

        // Bind card click events
        this._bindCardClickEvents(container, 'note');
    }

    /**
     * Render a visit card (checked-in appointment)
     * @param {Object} visit - Visit/appointment data
     * @returns {string} HTML string
     * @private
     */
    _renderVisitCard(visit) {
        const typeInfo = this.appointmentTypes[visit.Type] || { name: 'Unknown', icon: 'bi-question-circle', color: '#6B7280' };
        const statusInfo = this.appointmentStatuses[visit.Status] || { name: 'Unknown', class: 'bg-secondary' };
        const docStatus = this.documentationStatuses[visit.DocumentationStatus] || this.documentationStatuses[0];

        const noteLink = visit.HasNote
            ? `<button class="btn btn-sm btn-outline-primary view-note-btn" data-appointment-id="${visit.AppointmentId}" title="View Clinical Note">
                 <i class="bi bi-file-earmark-text me-1"></i>View Note
               </button>`
            : `<span class="badge bg-light text-muted"><i class="bi bi-file-earmark-x me-1"></i>No Note</span>`;

        return `
            <div class="card mb-2 visit-card hover-shadow" data-appointment-id="${visit.AppointmentId}" style="cursor: pointer; border-left: 4px solid ${typeInfo.color};">
                <div class="card-body p-3">
                    <div class="d-flex justify-content-between align-items-start">
                        <div class="flex-grow-1">
                            <div class="d-flex align-items-center mb-2">
                                <i class="${typeInfo.icon} me-2" style="color: ${typeInfo.color}; font-size: 1.1rem;"></i>
                                <strong>${this._escape(typeInfo.name)}</strong>
                                <span class="badge ${statusInfo.class} ms-2">${statusInfo.name}</span>
                            </div>
                            <div class="small text-muted mb-2">
                                <i class="bi bi-calendar3 me-1"></i>${visit.DateFormatted || this._formatDate(visit.StartTime)}
                                <span class="mx-2">|</span>
                                <i class="bi bi-clock me-1"></i>${visit.StartTimeFormatted || this._formatTime(visit.StartTime)} - ${visit.EndTimeFormatted || this._formatTime(visit.EndTime)}
                            </div>
                            <div class="small">
                                <span class="text-primary"><i class="bi bi-person me-1"></i>${this._escape(visit.ProviderName)}</span>
                                ${visit.LocationName ? `<span class="ms-3 text-muted"><i class="bi bi-geo-alt me-1"></i>${this._escape(visit.LocationName)}</span>` : ''}
                            </div>
                        </div>
                        <div class="text-end">
                            <div class="mb-2">
                                ${docStatus.icon ? `<span class="${docStatus.class}"><i class="${docStatus.icon} me-1"></i>${docStatus.name}</span>` : ''}
                            </div>
                            <div class="btn-group btn-group-sm">
                                <button class="btn btn-sm btn-outline-secondary view-appointment-btn" data-appointment-id="${visit.AppointmentId}" title="View Appointment Details">
                                    <i class="bi bi-calendar-event me-1"></i>View Appt
                                </button>
                                ${noteLink}
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        `;
    }

    /**
     * Render an appointment card (for upcoming appointments)
     * @param {Object} appt - Appointment data
     * @returns {string} HTML string
     * @private
     */
    _renderAppointmentCard(appt) {
        const typeInfo = this.appointmentTypes[appt.Type] || { name: 'Unknown', icon: 'bi-question-circle', color: '#6B7280' };
        const statusInfo = this.appointmentStatuses[appt.Status] || { name: 'Unknown', class: 'bg-secondary' };

        // Calculate days until appointment
        const daysUntil = this._getDaysUntil(appt.StartTime);
        const daysText = daysUntil === 0 ? 'Today' :
                        daysUntil === 1 ? 'Tomorrow' :
                        `In ${daysUntil} days`;

        return `
            <div class="card mb-2 appointment-card hover-shadow" data-appointment-id="${appt.AppointmentId}" style="cursor: pointer; border-left: 4px solid ${typeInfo.color};">
                <div class="card-body p-3">
                    <div class="d-flex justify-content-between align-items-start">
                        <div class="flex-grow-1">
                            <div class="d-flex align-items-center mb-2">
                                <i class="${typeInfo.icon} me-2" style="color: ${typeInfo.color}; font-size: 1.1rem;"></i>
                                <strong>${this._escape(typeInfo.name)}</strong>
                                <span class="badge ${statusInfo.class} ms-2">${statusInfo.name}</span>
                            </div>
                            <div class="small text-muted mb-2">
                                <i class="bi bi-calendar3 me-1"></i>${appt.DateFormatted || this._formatDate(appt.StartTime)}
                                <span class="mx-2">|</span>
                                <i class="bi bi-clock me-1"></i>${appt.StartTimeFormatted || this._formatTime(appt.StartTime)} - ${appt.EndTimeFormatted || this._formatTime(appt.EndTime)}
                            </div>
                            <div class="small">
                                <span class="text-primary"><i class="bi bi-person me-1"></i>${this._escape(appt.ProviderName)}</span>
                                ${appt.LocationName ? `<span class="ms-3 text-muted"><i class="bi bi-geo-alt me-1"></i>${this._escape(appt.LocationName)}</span>` : ''}
                            </div>
                        </div>
                        <div class="text-end">
                            <span class="badge bg-light text-dark fs-6">${daysText}</span>
                            ${appt.IsTelehealth ? '<div class="mt-2"><span class="badge bg-info"><i class="bi bi-camera-video me-1"></i>Telehealth</span></div>' : ''}
                        </div>
                    </div>
                </div>
            </div>
        `;
    }

    /**
     * Render a clinical note card
     * @param {Object} note - Clinical note data
     * @returns {string} HTML string
     * @private
     */
    _renderNoteCard(note) {
        const statusInfo = this.noteStatuses[note.Status] || { name: 'Unknown', class: 'bg-secondary' };
        // The card heading used to show the generic note TYPE ("Office Visit Note"),
        // which is not a useful identifier for the clinician. Prefer the TEMPLATE
        // NAME ("SOAP Note", "Progress Note", etc.) since that's what they picked
        // when creating the note. Fall back to type only if no template is set.
        const typeName = note.TypeName || this.noteTypes[note.Type] || 'Unknown';
        const heading = note.TemplateName && note.TemplateName.trim()
            ? note.TemplateName
            : typeName;

        return `
            <div class="card mb-2 note-card hover-shadow" data-note-id="${note.ClinicalNoteId}" data-appointment-id="${note.AppointmentId || ''}" style="cursor: pointer;">
                <div class="card-body p-3">
                    <div class="d-flex justify-content-between align-items-start">
                        <div class="flex-grow-1">
                            <div class="d-flex align-items-center mb-2">
                                <i class="bi bi-file-earmark-medical me-2 text-primary" style="font-size: 1.1rem;"></i>
                                <strong>${this._escape(heading)}</strong>
                                <span class="badge ${statusInfo.class} ms-2">${statusInfo.name}</span>
                            </div>
                            <div class="small text-muted mb-2">
                                <i class="bi bi-calendar3 me-1"></i>${this._formatDateOnly(note.ServiceDate)}
                            </div>
                            <div class="small">
                                <span class="text-primary"><i class="bi bi-person me-1"></i>${this._escape(note.ProviderName)}</span>
                            </div>
                        </div>
                        <div class="text-end">
                            ${note.SignedAt ? `
                                <div class="small text-success">
                                    <i class="bi bi-check-circle me-1"></i>Signed
                                </div>
                                <div class="small text-muted">${this._formatDateTime(note.SignedAt)}</div>
                            ` : ''}
                            <button class="btn btn-sm btn-outline-info mt-2 view-note-detail-btn" data-note-id="${note.ClinicalNoteId}">
                                <i class="bi bi-eye me-1"></i>View
                            </button>
                        </div>
                    </div>
                </div>
            </div>
        `;
    }

    /**
     * Bind click events to cards
     * @param {HTMLElement} container - Container element
     * @param {string} type - Card type ('visit', 'appointment', 'note')
     * @private
     */
    _bindCardClickEvents(container, type) {
        // Card click - view appointment details
        container.querySelectorAll('.visit-card, .appointment-card').forEach(card => {
            card.addEventListener('click', (e) => {
                // Don't trigger if clicking on a button
                if (e.target.closest('button')) return;

                const appointmentId = card.dataset.appointmentId;
                if (appointmentId) {
                    this._viewAppointmentDetails(parseInt(appointmentId));
                }
            });
        });

        // Note card click - view note
        container.querySelectorAll('.note-card').forEach(card => {
            card.addEventListener('click', (e) => {
                // Don't trigger if clicking on a button
                if (e.target.closest('button')) return;

                const noteId = card.dataset.noteId;
                const appointmentId = card.dataset.appointmentId;
                if (noteId) {
                    this._viewClinicalNote(parseInt(noteId), appointmentId ? parseInt(appointmentId) : null);
                }
            });
        });

        // View appointment buttons in visit cards
        container.querySelectorAll('.view-appointment-btn').forEach(btn => {
            btn.addEventListener('click', (e) => {
                e.stopPropagation();
                const appointmentId = btn.dataset.appointmentId;
                if (appointmentId) {
                    this._viewAppointmentDetails(parseInt(appointmentId));
                }
            });
        });

        // View note buttons in visit cards
        container.querySelectorAll('.view-note-btn').forEach(btn => {
            btn.addEventListener('click', (e) => {
                e.stopPropagation();
                const appointmentId = btn.dataset.appointmentId;
                if (appointmentId) {
                    this._viewNotesByAppointment(parseInt(appointmentId));
                }
            });
        });

        // View note detail buttons
        container.querySelectorAll('.view-note-detail-btn').forEach(btn => {
            btn.addEventListener('click', (e) => {
                e.stopPropagation();
                const noteId = btn.dataset.noteId;
                if (noteId) {
                    this._viewClinicalNote(parseInt(noteId));
                }
            });
        });
    }

    /**
     * View appointment details
     * @param {number} appointmentId - Appointment ID
     * @private
     */
    _viewAppointmentDetails(appointmentId) {
        // Use the global openAppointmentDetails function
        if (typeof window.openAppointmentDetails === 'function') {
            window.openAppointmentDetails(appointmentId);
        } else if (window.appointmentModule && typeof window.appointmentModule.viewAppointment === 'function') {
            window.appointmentModule.viewAppointment(appointmentId);
        } else if (window.App?.modules?.get('appointments')?.viewAppointment) {
            window.App.modules.get('appointments').viewAppointment(appointmentId);
        } else {
            console.warn('[PatientAppointmentsNotesModule] No appointment module available to view appointment');
        }
    }

    /**
     * View clinical note
     * @param {number} noteId - Clinical note ID
     * @param {number|null} appointmentId - Optional appointment ID for multi-note view
     * @private
     */
    _viewClinicalNote(noteId, appointmentId = null) {
        // Use the global clinical notes view function
        if (typeof window.viewClinicalNote === 'function') {
            window.viewClinicalNote(noteId, appointmentId);
        } else if (window.clinicalNotesModule && typeof window.clinicalNotesModule.view === 'function') {
            window.clinicalNotesModule.view(noteId, appointmentId);
        } else {
            console.warn('[PatientAppointmentsNotesModule] No clinical notes module available to view note');
        }
    }

    /**
     * View notes by appointment
     * @param {number} appointmentId - Appointment ID
     * @private
     */
    async _viewNotesByAppointment(appointmentId) {
        try {
            const headers = this._getHeaders();
            const response = await fetch(`/api/clinical-notes/by-appointment/${appointmentId}`, { headers });

            if (!response.ok) throw new Error('Failed to load notes');

            const notes = await response.json();

            if (notes.length === 0) {
                this._showToast('No clinical notes found for this appointment', 'info');
                return;
            }

            // View the first note (or use tabbed view if multiple)
            this._viewClinicalNote(notes[0].ClinicalNoteId, appointmentId);
        } catch (error) {
            console.error('[PatientAppointmentsNotesModule] Error loading notes by appointment:', error);
            this._showToast('Failed to load clinical notes', 'error');
        }
    }

    // ========================
    // Utility Methods
    // ========================

    /**
     * Get API headers with auth token
     * @returns {Object} Headers object
     * @private
     */
    _getHeaders() {
        // Use PatientUtilities if available, otherwise fall back to localStorage
        if (typeof PatientUtilities !== 'undefined' && PatientUtilities.getHeaders) {
            return PatientUtilities.getHeaders();
        }
        if (window.PatientUtilities && window.PatientUtilities.getHeaders) {
            return window.PatientUtilities.getHeaders();
        }
        // Fallback
        const token = localStorage.getItem('token');
        return {
            'Authorization': `Bearer ${token}`,
            'Content-Type': 'application/json'
        };
    }

    /**
     * Update count badge
     * @param {string} elementId - Element ID
     * @param {number} count - Count value
     * @private
     */
    _updateCount(elementId, count) {
        const el = document.getElementById(elementId);
        if (el) {
            el.textContent = count;
            el.classList.toggle('bg-primary', count > 0);
            el.classList.toggle('bg-secondary', count === 0);
        }
    }

    /**
     * Calculate days until a date
     * @param {string} dateStr - Date string
     * @returns {number} Days until date
     * @private
     */
    _getDaysUntil(dateStr) {
        const date = new Date(dateStr);
        const today = new Date();
        today.setHours(0, 0, 0, 0);
        date.setHours(0, 0, 0, 0);
        const diff = date - today;
        return Math.ceil(diff / (1000 * 60 * 60 * 24));
    }

    /**
     * Format date string
     * @param {string} dateStr - Date string
     * @returns {string} Formatted date
     * @private
     */
    _formatDate(dateStr) {
        if (!dateStr) return '-';
        try {
            const date = window.parseServerDateTime ? window.parseServerDateTime(dateStr) : new Date(dateStr);
            if (!date || isNaN(date.getTime())) return '-';
            return date.toLocaleDateString('en-US', {
                weekday: 'short',
                month: 'short',
                day: 'numeric',
                year: 'numeric'
            });
        } catch {
            return dateStr;
        }
    }

    /**
     * Format date only (from DateOnly type)
     * @param {string} dateStr - Date string in YYYY-MM-DD format
     * @returns {string} Formatted date
     * @private
     */
    _formatDateOnly(dateStr) {
        if (!dateStr) return '-';
        try {
            // Handle YYYY-MM-DD format to avoid timezone shifting
            if (typeof dateStr === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(dateStr)) {
                const [year, month, day] = dateStr.split('-').map(Number);
                const date = new Date(year, month - 1, day);
                return date.toLocaleDateString('en-US', {
                    weekday: 'short',
                    month: 'short',
                    day: 'numeric',
                    year: 'numeric'
                });
            }
            const date = new Date(dateStr);
            return date.toLocaleDateString('en-US', {
                weekday: 'short',
                month: 'short',
                day: 'numeric',
                year: 'numeric'
            });
        } catch {
            return dateStr;
        }
    }

    /**
     * Format time string
     * @param {string} dateStr - Date string
     * @returns {string} Formatted time
     * @private
     */
    _formatTime(dateStr) {
        if (!dateStr) return '-';
        try {
            const date = new Date(dateStr);
            return date.toLocaleTimeString('en-US', {
                hour: 'numeric',
                minute: '2-digit',
                hour12: true
            });
        } catch {
            return dateStr;
        }
    }

    /**
     * Format date and time
     * @param {string} dateStr - Date string
     * @returns {string} Formatted date and time
     * @private
     */
    _formatDateTime(dateStr) {
        if (!dateStr) return '-';
        try {
            const date = new Date(dateStr);
            return date.toLocaleDateString('en-US', {
                month: 'short',
                day: 'numeric',
                year: 'numeric',
                hour: 'numeric',
                minute: '2-digit',
                hour12: true
            });
        } catch {
            return dateStr;
        }
    }

    /**
     * Escape HTML to prevent XSS
     * @param {string} str - String to escape
     * @returns {string} Escaped string
     * @private
     */
    _escape(str) {
        if (str === null || str === undefined) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    }

    /**
     * Render loading state
     * @returns {string} HTML string
     * @private
     */
    _renderLoadingState() {
        return `
            <div class="text-center py-5">
                <div class="spinner-border text-primary" role="status">
                    <span class="visually-hidden">Loading...</span>
                </div>
                <p class="text-muted mt-2 mb-0">Loading...</p>
            </div>
        `;
    }

    /**
     * Render empty state
     * @param {string} title - Empty state title
     * @param {string} icon - Bootstrap icon class
     * @param {string} message - Empty state message
     * @returns {string} HTML string
     * @private
     */
    _renderEmptyState(title, icon, message) {
        return `
            <div class="text-center py-5">
                <i class="bi ${icon} fs-1 text-muted d-block mb-3"></i>
                <h5 class="text-muted">${this._escape(title)}</h5>
                <p class="text-muted mb-0">${this._escape(message)}</p>
            </div>
        `;
    }

    /**
     * Render error state
     * @param {string} message - Error message
     * @returns {string} HTML string
     * @private
     */
    _renderErrorState(message) {
        return `
            <div class="alert alert-danger m-3">
                <i class="bi bi-exclamation-triangle me-2"></i>
                ${this._escape(message)}
                <button class="btn btn-sm btn-outline-danger ms-3" onclick="window.patientAppointmentsNotesModule?.refresh()">
                    <i class="bi bi-arrow-clockwise me-1"></i>Retry
                </button>
            </div>
        `;
    }

    /**
     * Show toast notification
     * @param {string} message - Message to show
     * @param {string} type - Toast type ('success', 'error', 'info', 'warning')
     * @private
     */
    _showToast(message, type = 'info') {
        if (typeof window.showToast === 'function') {
            window.showToast(message, type);
        } else {
            console.log(`[Toast ${type}]: ${message}`);
        }
    }

    /**
     * Refresh current tab data
     */
    async refresh() {
        switch (this.currentSubTab) {
            case 'visits':
                this.visits = [];
                await this._loadVisits();
                break;
            case 'upcoming':
                this.upcomingAppointments = [];
                await this._loadUpcoming();
                break;
            case 'notes':
                this.clinicalNotes = [];
                await this._loadNotes();
                break;
        }
    }
}

// Create global instance
window.patientAppointmentsNotesModule = new PatientAppointmentsNotesModule();

// Export for module systems
if (typeof module !== 'undefined' && module.exports) {
    module.exports = PatientAppointmentsNotesModule;
}
