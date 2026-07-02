/**
 * DashboardModule - Dashboard management and widgets
 *
 * Handles dashboard data loading, widgets, and stat displays
 * for today's appointments, missing notes, signatures, and alerts.
 *
 * @example
 *   const dashboard = App.modules.get('dashboard');
 *   await dashboard.init();
 *   dashboard.load();
 */
class DashboardModule {
    /**
     * @param {Object} options - Module options
     * @param {Object} options.api - API service instance
     * @param {Object} options.eventBus - Event bus for cross-module communication
     */
    constructor(options = {}) {
        this.api = options.api || null;
        this.eventBus = options.eventBus || null;

        // State
        this.stats = null;
        this.todayAppointments = [];
        this.missingNotes = [];
        this.missingSignatures = [];
        this.careEpisodeData = null;
        this.requireScheduleData = [];
        this.isInitialized = false;

        // DOM references
        this.container = null;
        this.scheduleTable = null;

        // Bind methods
        this._handleRowClick = this._handleRowClick.bind(this);
        this._handleWidgetAction = this._handleWidgetAction.bind(this);
    }

    /**
     * Initialize the module
     * @returns {Promise<void>}
     */
    async init() {
        this.container = document.getElementById('dashboardPage') ||
                        document.querySelector('.dashboard-container');
        // 2026-05: Today Appointments is now a row-card list, not a table.
        // We keep the variable name `scheduleTable` for minimal diff across the
        // module — it just points at the new <div id="todayScheduleList">.
        this.scheduleTable = document.getElementById('todayScheduleList');

        if (!this.container && !this.scheduleTable) {
            console.warn('[DashboardModule] Container not found');
            return;
        }

        this._bindEvents();
        this.isInitialized = true;
        this._emit('dashboard:initialized');
    }

    /**
     * Bind event handlers
     * @private
     */
    _bindEvents() {
        if (this.scheduleTable) {
            this.scheduleTable.addEventListener('click', this._handleRowClick);
        }

        // Event delegation for all widget action buttons
        if (this.container) {
            this.container.addEventListener('click', this._handleWidgetAction);
        }

        // Refresh copay buttons after a payment is collected
        window.addEventListener('payment:created', () => {
            this._updateCopayButtons();
        });

        // Listen for SignalR appointment change notifications
        this._bindSignalREvents();
    }

    /**
     * Bind SignalR event handlers for real-time updates
     * @private
     */
    _bindSignalREvents() {
        // Listen for appointmentChanged custom event from SignalR service
        document.addEventListener('appointmentChanged', (event) => {
            const notification = event.detail;
            console.log('[DashboardModule] SignalR appointment changed:', notification);

            // Refresh dashboard data when any appointment changes
            // This ensures stats, today's schedule, and widgets are updated
            this._handleAppointmentChangeNotification(notification);
        });

        // Listen for clinical note changes
        document.addEventListener('clinicalNoteChanged', (event) => {
            const notification = event.detail;
            console.log('[DashboardModule] SignalR clinical note changed:', notification);

            // Refresh dashboard data when clinical notes change
            // This updates missing notes and missing signature widgets
            this._handleClinicalNoteChangeNotification(notification);
        });

        // Listen for authorization/require-schedule changes
        document.addEventListener('authorizationChanged', (event) => {
            const notification = event.detail;
            console.log('[DashboardModule] SignalR authorization changed:', notification);

            // Refresh dashboard data when authorization changes
            // This updates the require-schedule widget
            this._handleAuthorizationChangeNotification(notification);
        });

        // Listen for kiosk consent completion notifications
        document.addEventListener('consentCompleted', (event) => {
            const notification = event.detail;
            console.log('[DashboardModule] SignalR consent completed (kiosk check-in):', notification);

            // Refresh dashboard data when patient completes kiosk check-in
            // This updates appointment status to "Checked In"
            this._handleConsentCompletedNotification(notification);
        });

        // Also listen via eventBus if available
        if (this.eventBus) {
            this.eventBus.on('signalr:appointmentChanged', (notification) => {
                this._handleAppointmentChangeNotification(notification);
            });
            this.eventBus.on('signalr:clinicalNoteChanged', (notification) => {
                this._handleClinicalNoteChangeNotification(notification);
            });
            this.eventBus.on('signalr:authorizationChanged', (notification) => {
                this._handleAuthorizationChangeNotification(notification);
            });
            this.eventBus.on('signalr:consentCompleted', (notification) => {
                this._handleConsentCompletedNotification(notification);
            });
        }
    }

    /**
     * Handle appointment change notification from SignalR
     * @private
     * @param {Object} notification - The appointment change notification
     */
    _handleAppointmentChangeNotification(notification) {
        // Only refresh if we're on the dashboard page
        if (!this.container && !this.scheduleTable) {
            return;
        }

        // Debounce rapid updates (e.g., multiple changes in quick succession)
        if (this._refreshTimeout) {
            clearTimeout(this._refreshTimeout);
        }

        this._refreshTimeout = setTimeout(() => {
            console.log(`[DashboardModule] Silently refreshing dashboard due to ${notification.ChangeType} event`);
            this.silentRefresh();
        }, 500); // 500ms debounce
    }

    /**
     * Handle clinical note change notification from SignalR
     * @private
     * @param {Object} notification - The clinical note change notification
     */
    _handleClinicalNoteChangeNotification(notification) {
        // Only refresh if we're on the dashboard page
        if (!this.container && !this.scheduleTable) {
            return;
        }

        // Debounce rapid updates (e.g., multiple changes in quick succession)
        if (this._noteRefreshTimeout) {
            clearTimeout(this._noteRefreshTimeout);
        }

        this._noteRefreshTimeout = setTimeout(() => {
            console.log(`[DashboardModule] Silently refreshing dashboard due to clinical note ${notification.ChangeType} event`);
            this.silentRefresh();
        }, 500); // 500ms debounce
    }

    /**
     * Handle authorization change notification from SignalR
     * @private
     * @param {Object} notification - The authorization change notification
     */
    _handleAuthorizationChangeNotification(notification) {
        // Only refresh if we're on the dashboard page
        if (!this.container && !this.scheduleTable) {
            return;
        }

        // Debounce rapid updates (e.g., multiple changes in quick succession)
        if (this._authRefreshTimeout) {
            clearTimeout(this._authRefreshTimeout);
        }

        this._authRefreshTimeout = setTimeout(() => {
            console.log(`[DashboardModule] Silently refreshing dashboard due to authorization ${notification.ChangeType} event`);
            this.silentRefresh();
        }, 500); // 500ms debounce
    }

    /**
     * Handle consent completed (kiosk check-in) notification from SignalR
     * @private
     * @param {Object} notification - The consent completion notification
     * @param {number} notification.PatientId - Patient ID
     * @param {string} notification.PatientName - Patient name
     * @param {number} notification.AppointmentId - Appointment ID
     * @param {string} notification.AppointmentTime - Appointment time
     * @param {number} notification.LocationId - Location ID
     * @param {string} notification.LocationName - Location name
     */
    _handleConsentCompletedNotification(notification) {
        // Only refresh if we're on the dashboard page
        if (!this.container && !this.scheduleTable) {
            return;
        }

        console.log(`[DashboardModule] Patient ${notification.PatientName} completed kiosk check-in for appointment ${notification.AppointmentId}`);

        // Debounce rapid updates
        if (this._consentRefreshTimeout) {
            clearTimeout(this._consentRefreshTimeout);
        }

        this._consentRefreshTimeout = setTimeout(() => {
            console.log('[DashboardModule] Silently refreshing dashboard due to kiosk check-in completion');
            this.silentRefresh();
        }, 500); // 500ms debounce
    }

    /**
     * Refresh dashboard data without showing loading overlay
     * Used for real-time SignalR updates to avoid disrupting user workflow
     * @returns {Promise<void>}
     */
    async silentRefresh() {
        // Set flag to indicate silent refresh (no loader)
        this._isSilentRefresh = true;
        try {
            await this.load();
        } finally {
            this._isSilentRefresh = false;
        }
    }

    /**
     * Handle widget action button clicks (event delegation)
     * @private
     * @param {Event} e - Click event
     */
    _handleWidgetAction(e) {
        const button = e.target.closest('[data-action]');
        if (!button) return;

        const action = button.dataset.action;
        const id = button.dataset.id;
        const patientId = button.dataset.patientId;
        const careEpisodeId = button.dataset.careEpisodeId;
        const sessionId = button.dataset.sessionId;
        const type = button.dataset.type;

        console.log('[DashboardModule] Widget action:', action, { id, patientId, careEpisodeId, sessionId, type });

        switch (action) {
            case 'viewAppointment':
                // Open appointment details (view only)
                if (id && typeof openAppointmentDetails === 'function') {
                    openAppointmentDetails(parseInt(id));
                }
                break;

            case 'writeNote':
                // Open clinical note creation for appointment
                this._openWriteNoteForAppointment(parseInt(id));
                break;

            case 'recordSession':
                // Open recording session for appointment
                this._openRecordSessionForAppointment(parseInt(id));
                break;

            case 'signNote':
                // Open clinical note for signing
                if (id && typeof viewClinicalNote === 'function') {
                    viewClinicalNote(parseInt(id));
                }
                break;

            case 'schedulePatient':
                // Open new appointment for patient from Require Schedule widget
                // Uses scheduleFromRequireCard if available (validates authorization and pre-selects follow-up)
                const careEpisodeId = button.dataset.careEpisodeId ? parseInt(button.dataset.careEpisodeId) : null;
                if (patientId && typeof scheduleFromRequireCard === 'function') {
                    scheduleFromRequireCard(parseInt(patientId), careEpisodeId);
                } else if (patientId && typeof openNewAppointmentForPatient === 'function') {
                    openNewAppointmentForPatient(parseInt(patientId));
                }
                break;

            case 'viewAll':
                // Open the appropriate modal based on type
                this._handleViewAll(type);
                break;

            default:
                console.warn('[DashboardModule] Unknown action:', action);
        }
    }

    /**
     * Handle "View All" button clicks
     * @private
     * @param {string} type - Widget type
     */
    _handleViewAll(type) {
        switch (type) {
            case 'notes':
                if (typeof openMissingNotesModal === 'function') {
                    openMissingNotesModal();
                }
                break;
            case 'signatures':
                if (typeof openMissingSignatureModal === 'function') {
                    openMissingSignatureModal();
                }
                break;
            case 'requireSchedule':
                if (typeof openRequireScheduleModal === 'function') {
                    openRequireScheduleModal();
                }
                break;
            default:
                console.warn('[DashboardModule] Unknown viewAll type:', type);
        }
    }

    /**
     * Open write note interface for an appointment
     * @private
     * @param {number} appointmentId - Appointment ID
     */
    async _openWriteNoteForAppointment(appointmentId) {
        try {
            const appointment = await this._apiGet(`/appointments/${appointmentId}`);
            if (appointment && typeof openCreateClinicalNote === 'function') {
                openCreateClinicalNote(appointment);
            }
        } catch (error) {
            console.error('[DashboardModule] Failed to load appointment for note:', error);
            if (typeof showToast === 'function') {
                showToast('Error', 'Failed to load appointment', 'error');
            }
        }
    }

    /**
     * Open recording session for an appointment
     * @private
     * @param {number} appointmentId - Appointment ID
     */
    async _openRecordSessionForAppointment(appointmentId) {
        try {
            const appointment = await this._apiGet(`/appointments/${appointmentId}`);
            if (appointment) {
                // Set the current appointment for recording (required by recording service)
                window.currentAppointmentForNotes = appointment;
                if (typeof openRecordSessionModal === 'function') {
                    openRecordSessionModal();
                }
            }
        } catch (error) {
            console.error('[DashboardModule] Failed to load appointment for recording:', error);
            if (typeof showToast === 'function') {
                showToast('Error', 'Failed to load appointment', 'error');
            }
        }
    }

    /**
     * Handle appointment row clicks
     * Opens appointment detail modal when clicking on row (except patient name link)
     * @private
     * @param {Event} e - Click event
     */
    _handleRowClick(e) {
        const row = e.target.closest('.dashboard-appointment-row');
        if (!row) return;

        const appointmentId = row.dataset.appointmentId;
        if (appointmentId) {
            // Emit event for any listeners
            this._emit('dashboard:appointmentClicked', { appointmentId: parseInt(appointmentId) });

            // Open appointment details modal
            if (typeof openAppointmentDetails === 'function') {
                openAppointmentDetails(parseInt(appointmentId));
            }
        }
    }

    /**
     * Load entire dashboard
     * @returns {Promise<void>}
     */
    async load() {
        try {
            const currentUser = this._getCurrentUser();

            // Check if Super Admin without tenant - need to select a clinic first
            const isSuperAdmin = currentUser?.Role === 0 && !currentUser?.TenantId;
            const selectedClinicId = localStorage.getItem('selectedClinicId') || '';

            if (isSuperAdmin && !selectedClinicId) {
                // Super Admin needs to select a clinic first
                this._showSuperAdminMessage();
                return;
            }

            const selectedLocationId = localStorage.getItem('selectedLocationId') || '';

            // Build query params
            const params = this._buildQueryParams(currentUser, selectedLocationId, selectedClinicId);

            // Update date display
            this._updateDateDisplay();

            // Load main dashboard data
            const [stats, dashboardData] = await Promise.all([
                this._apiGet(`/dashboard/stats${params}`).catch(() => null),
                this._apiGet(`/dashboard/appointments${params}${params ? '&' : '?'}todayOnly=true`).catch(() => ({ Appointments: [] }))
            ]);

            this.stats = stats;
            this.todayAppointments = dashboardData?.Appointments || [];

            // Update UI
            this._updateStats();
            this._renderTodayAppointments();
            this._renderActiveEncountersBanner();

            // Load widgets
            await this.loadWidgets();

            // Check if we should show the resume encounter popup
            this._checkResumePopup();

            this._emit('dashboard:loaded', {
                stats: this.stats,
                appointments: this.todayAppointments
            });
        } catch (error) {
            console.error('[DashboardModule] Load error:', error);
            throw error;
        }
    }

    /**
     * Load all dashboard widgets
     * @returns {Promise<void>}
     */
    async loadWidgets() {
        const currentUser = this._getCurrentUser();
        const selectedLocationId = localStorage.getItem('selectedLocationId') || '';
        const selectedClinicId = localStorage.getItem('selectedClinicId') || '';
        const params = this._buildQueryParams(currentUser, selectedLocationId, selectedClinicId);

        try {
            // Determine if user can see require-schedule (Admin/Front Desk only: roles 0, 1, 3)
            const canSeeRequireSchedule = currentUser?.Role === 0 || currentUser?.Role === 1 || currentUser?.Role === 3;

            // Load widget data in parallel
            const [missingNotes, missingSignatures, careEpisodeData, requireScheduleData] = await Promise.all([
                this._apiGet(`/dashboard/missing-notes${params}`).catch(() => []),
                this._apiGet(`/dashboard/missing-signature${params}`).catch(() => []),
                this._apiGet(`/care-episodes/dashboard${params}`).catch(() => ({})),
                canSeeRequireSchedule
                    ? this._apiGet(`/care-episodes/require-schedule${params}`).catch(() => [])
                    : Promise.resolve([])
            ]);

            this.missingNotes = missingNotes || [];
            this.missingSignatures = missingSignatures || [];
            this.careEpisodeData = careEpisodeData || {};
            this.requireScheduleData = requireScheduleData || [];

            // Update widgets
            this._updateMissingNotesWidget();
            this._updateMissingSignatureWidget();
            this._updateCareEpisodeWidgets();
            this._updateRequireScheduleWidget();

            // Load pending orders count
            this._loadPendingOrdersCount();

            this._emit('dashboard:widgetsLoaded', {
                missingNotes: this.missingNotes,
                missingSignatures: this.missingSignatures,
                careEpisodeData: this.careEpisodeData,
                requireScheduleData: this.requireScheduleData
            });
        } catch (error) {
            console.error('[DashboardModule] Load widgets error:', error);
        }
    }

    /**
     * Refresh dashboard data
     * @returns {Promise<void>}
     */
    async refresh() {
        await this.load();
    }

    /**
     * Show message for Super Admin to select a clinic
     * @private
     */
    _showSuperAdminMessage() {
        // Update stats to show zeros
        const statElements = ['statTodayAppts', 'statActivePatients', 'statCompletedToday', 'statPendingNotes'];
        statElements.forEach(id => {
            const el = document.getElementById(id);
            if (el) el.textContent = '0';
        });

        // Show message in schedule table
        if (this.scheduleTable) {
            this.scheduleTable.innerHTML = `
                <tr>
                    <td colspan="6" class="text-center text-muted py-4">
                        <i class="bi bi-building mb-2" style="font-size: 2rem;"></i>
                        <p class="mb-1">Please select a clinic from the dropdown above to view dashboard data.</p>
                        <small class="text-muted">As a Super Admin, you can manage all clinics by selecting one first.</small>
                    </td>
                </tr>
            `;
        }

        // Update widgets with empty state
        this._setElementContent('missingNotesWidget', '<div class="text-center text-muted py-2 small">Select a clinic first</div>');
        this._setElementContent('missingSignatureWidget', '<div class="text-center text-muted py-2 small">Select a clinic first</div>');
        this._setElementContent('noShowWidget', '<div class="text-center text-muted py-2 small">Select a clinic first</div>');
        this._setElementContent('missedVisitsWidget', '<div class="text-center text-muted py-2 small">Select a clinic first</div>');
        this._setElementContent('requireScheduleWidget', '<div class="text-center text-muted py-2 small">Select a clinic first</div>');

        this._updateDateDisplay();
    }

    /**
     * Build query parameters
     * @private
     * @param {Object} currentUser - Current user
     * @param {string} locationId - Selected location ID
     * @param {string} clinicId - Selected clinic ID (for Super Admin)
     * @returns {string} Query string
     */
    _buildQueryParams(currentUser, locationId, clinicId = '') {
        const params = [];

        // Super Admin with selected clinic - add tenantId
        if (clinicId) {
            params.push(`tenantId=${clinicId}`);
        }

        // Provider/Clinician sees only their appointments
        if (currentUser?.Role === 2 && currentUser?.ProviderId) {
            params.push(`providerId=${currentUser.ProviderId}`);
        }

        if (locationId) {
            params.push(`locationId=${locationId}`);
        }

        return params.length > 0 ? '?' + params.join('&') : '';
    }

    /**
     * Update date display
     * @private
     */
    _updateDateDisplay() {
        const dateEl = document.getElementById('dashboardDate');
        if (!dateEl) return;

        const now = new Date();
        const options = {
            weekday: 'short',
            month: 'short',
            day: 'numeric',
            year: 'numeric'
        };
        dateEl.textContent = now.toLocaleDateString('en-US', options);
    }

    /**
     * Update stats cards
     * @private
     */
    _updateStats() {
        const statElements = {
            statTodayAppts: this.todayAppointments.length,
            statActivePatients: this.stats?.ActivePatients || 0,
            statCompletedToday: this.stats?.CompletedToday || 0,
            statPendingNotes: this.stats?.PendingNotes || 0
        };

        Object.entries(statElements).forEach(([id, value]) => {
            const el = document.getElementById(id);
            if (el) {
                el.textContent = value;
            }
        });
    }

    /**
     * Render today's appointments
     * @private
     */
    _renderActiveEncountersBanner() {
        const banner = document.getElementById('activeEncountersBanner');
        if (!banner) return;

        // Find InProgress appointments (status 3 = InProgress)
        const activeVisits = this.todayAppointments.filter(a => a.Status === 3);
        if (activeVisits.length === 0) {
            banner.innerHTML = '';
            return;
        }

        const user = this._getCurrentUser();
        const role = parseInt(user?.Role ?? user?.role ?? -1);
        const isAdminFrontDesk = role === UserRoles.CLINIC_ADMIN || role === UserRoles.FRONT_DESK;

        // Admin/Front Desk: compact read-only summary card (no action buttons)
        if (isAdminFrontDesk) {
            const count = activeVisits.length;
            const listItems = activeVisits.map(apt => {
                const time = this._formatTime(apt.StartTime || apt.AppointmentTime || '');
                const patientName = this._escape(apt.PatientName || 'Unknown Patient');
                const providerName = this._escape(apt.ProviderName || '');
                return `
                    <div class="d-flex align-items-center justify-content-between py-1 border-bottom" style="font-size: 13px;">
                        <div class="text-truncate me-2">
                            <span class="fw-semibold">${patientName}</span>
                            <span class="text-muted ms-1 small">${providerName}</span>
                        </div>
                        <div class="text-end flex-shrink-0">
                            <small class="text-muted">${time}</small>
                        </div>
                    </div>
                `;
            }).join('');

            banner.innerHTML = `
                <div class="card border-success shadow-sm" style="max-width: 480px;">
                    <div class="card-header bg-success bg-opacity-10 py-1 px-3 d-flex align-items-center justify-content-between">
                        <h6 class="mb-0 text-success" style="font-size: 13px;">
                            <i class="bi bi-activity me-1"></i>Active Encounters
                        </h6>
                        <span class="badge bg-success" style="font-size: 11px;">${count}</span>
                    </div>
                    <div class="card-body py-1 px-3" style="max-height: 160px; overflow-y: auto;">
                        ${listItems}
                    </div>
                </div>
            `;
            return;
        }

        // Provider/Nurse: actionable resume banners
        banner.innerHTML = activeVisits.map(apt => {
            const time = this._formatTime(apt.StartTime || apt.AppointmentTime || '');
            const patientName = apt.PatientName || 'Unknown Patient';
            const providerName = apt.ProviderName || '';
            return `
                <div class="alert alert-success d-flex align-items-center justify-content-between py-2 mb-2 shadow-sm">
                    <div>
                        <i class="bi bi-circle-fill text-success me-2" style="font-size: 0.6rem;"></i>
                        <strong>Active Visit</strong> —
                        <span class="fw-semibold">${patientName}</span>
                        <span class="text-muted ms-2">${providerName} · Started ${time}</span>
                    </div>
                    <button class="btn btn-success" onclick="window._resumeEncounter(${apt.AppointmentId}, ${apt.PatientId})">
                        <i class="bi bi-arrow-right-circle me-1"></i>Resume Visit
                    </button>
                </div>
            `;
        }).join('');
    }

    /**
     * Check if we should show the resume encounter popup for the current provider.
     * @private
     */
    async _checkResumePopup() {
        try {
            const user = this._getCurrentUser();
            if (!user) return;

            // Only show for Clinicians (role 2) who have a ProviderId
            const role = parseInt(user.Role ?? user.role ?? -1);
            const providerId = user.ProviderId || user.providerId;
            if (role !== 2 || !providerId) return;

            // Only show popup once per login session (not on every dashboard visit)
            if (sessionStorage.getItem('resumePopupShown')) return;

            // Check if there are active visits
            const activeVisits = (this.todayAppointments || []).filter(a => a.Status === 3);
            if (activeVisits.length === 0) return;

            // Fetch provider preferences
            const prefs = await this._apiGet('/providers/my-preferences').catch(() => null);
            if (!prefs || prefs.ShowResumePopup === false) return;

            // Show popup for the first active visit
            const visit = activeVisits[0];
            const patientName = visit.PatientName || 'Unknown Patient';
            const startTime = this._formatTime(visit.StartTime) || visit.StartTime || '';
            const visitType = visit.AppointmentType || visit.VisitType || 'Visit';

            document.getElementById('resumePopupPatientName').textContent = patientName;
            document.getElementById('resumePopupDetails').textContent = `${visitType} · Started at ${startTime}`;

            const popupEl = document.getElementById('resumeEncounterPopup');
            if (!popupEl) return;

            const modal = new bootstrap.Modal(popupEl);

            // Resume button
            document.getElementById('btnResumeFromPopup').onclick = () => {
                modal.hide();
                window._resumeEncounter(visit.AppointmentId, visit.PatientId);
            };

            // Stay on Dashboard button
            document.getElementById('btnStayDashboard').onclick = async () => {
                const dontShow = document.getElementById('chkDontShowAgain')?.checked;
                if (dontShow) {
                    // Save preference to DB
                    try {
                        await apiRequest('/providers/my-preferences', {
                            method: 'PUT',
                            body: { ShowResumePopup: false },
                            showLoader: false
                        });
                    } catch (e) {
                        console.warn('[Dashboard] Failed to save resume popup preference:', e);
                    }
                }
                modal.hide();
            };

            // Reset checkbox on show
            document.getElementById('chkDontShowAgain').checked = false;

            // Mark as shown for this login session
            sessionStorage.setItem('resumePopupShown', 'true');
            modal.show();
        } catch (err) {
            console.warn('[Dashboard] Resume popup check error:', err);
        }
    }

    /**
     * Render today's appointment list as row cards.
     * Each card has:
     *   • a left-edge color stripe matching the visit type
     *   • the time (large, with TZ abbreviation)
     *   • patient avatar (40px) + name + MRN + sticky note + intake indicator
     *   • provider avatar (32px) + name
     *   • type pill, status pill, flow actions
     * The wrapper keeps the existing `.dashboard-appointment-row` class so the
     * single delegated click handler (line ~395) continues to open the
     * appointment detail modal.
     * @private
     */
    _renderTodayAppointments() {
        if (!this.scheduleTable) return;

        if (!this.todayAppointments.length) {
            this.scheduleTable.innerHTML = `
                <div class="ew-today-empty">
                    <i class="bi bi-calendar-x"></i>
                    <div class="mt-2">No appointments scheduled for today</div>
                </div>
            `;
            return;
        }

        this.scheduleTable.innerHTML = this.todayAppointments.map(apt => {
            const patientAvatar = (window.AvatarUtils)
                ? AvatarUtils.renderPatientAvatar({
                    patientId: apt.PatientId,
                    name: apt.PatientName,
                    hasProfilePicture: apt.PatientHasProfilePicture,
                    size: 'md'
                  })
                : '';
            const providerAvatar = (window.AvatarUtils)
                ? AvatarUtils.renderProviderAvatar({
                    providerId: apt.ProviderId,
                    name: apt.ProviderName,
                    color: apt.ProviderColor,
                    hasProfilePicture: apt.ProviderHasProfilePicture,
                    size: 'sm'
                  })
                : '';

            // Format time as "2:30" + "PM" caption on a second line.
            const timeStr = this._formatTime(apt.StartTime) || '';
            const timeMatch = timeStr.match(/^(\d{1,2}:\d{2})\s*([AP]M)?/i);
            const timeMain = timeMatch ? timeMatch[1] : timeStr;
            const timeAmpm = timeMatch && timeMatch[2] ? timeMatch[2].toUpperCase() : '';

            // Visit type → CSS modifier class for the left-edge color stripe.
            const typeClass = this._typeStripClass(apt.Type);

            // Sticky note button — preserve the previous behavior (different
            // look for "has notes" vs "no notes").
            const stickyBtn = apt.StickyNoteCount > 0
                ? `<span class="ew-today-sticky has-notes" role="button"
                          onclick="event.stopPropagation(); openPatientStickyNotesModal(${apt.PatientId}, '${this._escape(apt.PatientName).replace(/'/g, "\\'")}'); return false;"
                          title="${apt.StickyNoteCount} sticky note(s)">
                       <i class="bi bi-sticky-fill"></i> ${apt.StickyNoteCount}
                   </span>`
                : `<span class="ew-today-sticky" role="button"
                          onclick="event.stopPropagation(); openPatientStickyNotesModal(${apt.PatientId}, '${this._escape(apt.PatientName).replace(/'/g, "\\'")}'); return false;"
                          title="Add sticky note">
                       <i class="bi bi-sticky"></i>
                   </span>`;

            const intakeBadge = window.IntakeStatusIndicator
                ? window.IntakeStatusIndicator.render({ patientId: apt.PatientId, intakeStatus: apt.IntakeStatus, context: 'compact' })
                : '';

            return `
                <div class="ew-today-card dashboard-appointment-row ${typeClass}"
                     data-appointment-id="${apt.AppointmentId}">
                    <div class="ew-today-time">
                        <div class="ew-today-time-main">${timeMain}</div>
                        ${timeAmpm ? `<div class="ew-today-time-ampm">${timeAmpm}</div>` : ''}
                    </div>

                    <div class="ew-today-patient">
                        ${patientAvatar}
                        <div class="ew-today-patient-info">
                            <div class="ew-today-patient-name">
                                <a href="#" class="text-decoration-none text-primary fw-semibold"
                                   onclick="event.stopPropagation(); viewPatient(${apt.PatientId}); return false;"
                                   title="View Patient">
                                    ${this._escape(apt.PatientName)}
                                </a>
                                ${stickyBtn}
                                ${intakeBadge}
                            </div>
                            <div class="ew-today-patient-mrn">${this._escape(apt.PatientMRN || '')}</div>
                        </div>
                    </div>

                    <div class="ew-today-provider">
                        ${providerAvatar}
                        <span>${this._escape(apt.ProviderName || '')}</span>
                    </div>

                    <div class="ew-today-type">
                        <span class="badge bg-light text-dark">${this._getAppointmentTypeName(apt.Type)}</span>
                    </div>

                    <div class="ew-today-status">
                        ${this._getStatusBadge(apt.Status)}
                    </div>

                    <div class="ew-today-actions">
                        ${this._getFlowActions(apt)}
                    </div>
                </div>
            `;
        }).join('');

        // Async-update copay buttons with real outstanding balance
        this._updateCopayButtons();
    }

    /**
     * Visit type → left-edge stripe color. Kept simple (one extra class per
     * card). Default = indigo (Follow-Up). Add a case here when adding new
     * appointment types in AllEnums.cs.
     * @private
     */
    _typeStripClass(type) {
        switch (parseInt(type)) {
            case 0:  return 'is-newpatient';   // New Patient — green
            case 2:  return 'is-annual';       // Annual Physical — purple
            case 3:  return 'is-wellness';     // Wellness — cyan
            case 4:  return 'is-consultation'; // Consultation — orange
            case 5:  return 'is-telehealth';   // Telehealth — purple
            case 6:  return 'is-procedure';    // Procedure — pink
            case 7:  return 'is-urgent';       // Urgent — red
            case 8:  return 'is-labreview';    // Lab Review — brown
            case 9:  return 'is-medreview';    // Med Review — blue
            case 10: case 11: return 'is-longevity'; // Longevity — teal
            default: return ''; // Follow-Up — default indigo stripe via CSS
        }
    }

    /**
     * Fetch real outstanding balance for each copay button and update/remove accordingly
     * @private
     */
    async _updateCopayButtons() {
        const placeholders = document.querySelectorAll('[id^="copayBtn_"]');
        for (const el of placeholders) {
            const pid = el.dataset.patientId;
            const aid = el.dataset.appointmentId;
            const name = el.dataset.patientName;
            if (!pid) continue;
            try {
                const bal = await this._apiGet(`/payments/patient/${pid}/outstanding`);
                if (bal && bal.CurrentBalance > 0) {
                    const amount = bal.CurrentBalance.toFixed(2);
                    el.innerHTML = `<button class="btn btn-outline-success px-3 py-2" onclick="event.stopPropagation(); openCollectPaymentModal(${pid}, ${aid}, ${amount}, '${name}')" title="Collect Copay ($${amount})">
                        <i class="bi bi-cash-coin me-1"></i>Copay $${amount}</button>`;
                } else {
                    // No outstanding balance — remove the button
                    el.remove();
                }
            } catch (e) {
                el.remove();
            }
        }
    }

    /**
     * Update missing notes widget
     * @private
     */
    _updateMissingNotesWidget() {
        const count = this.missingNotes.length;
        const widgetHtml = this._renderWidgetItems(this.missingNotes, 'notes', 3);

        // Update widget containers
        this._setElementContent('missingNotesWidget', widgetHtml);
        this._setElementContent('missingNotesWidgetTherapist', widgetHtml);
        this._setElementText('missingNotesCount', count);
        this._setElementText('missingNotesBadge', count);
        this._setElementText('missingNotesBadgeTherapist', count);
    }

    /**
     * Update missing signature widget
     * @private
     */
    _updateMissingSignatureWidget() {
        const count = this.missingSignatures.length;
        const widgetHtml = this._renderWidgetItems(this.missingSignatures, 'signatures', 3);

        this._setElementContent('missingSignatureWidget', widgetHtml);
        this._setElementContent('missingSignatureWidgetTherapist', widgetHtml);
        this._setElementText('missingSignatureCount', count);
        this._setElementText('missingSignatureBadge', count);
        this._setElementText('missingSignatureBadgeTherapist', count);
    }

    /**
     * Update draft recordings widget
     * @private
     */

    /**
     * Update care episode widgets
     * @private
     */
    _updateCareEpisodeWidgets() {
        const data = this.careEpisodeData || {};

        this._setElementText('noShowCount', data.TotalNoShowAlerts || 0);
        this._setElementText('missedAppointmentsCount', data.TotalMissedAppointments || 0);
        this._setElementText('noShowBadge', data.TotalNoShowAlerts || 0);
        this._setElementText('missedAppointmentsBadge', data.TotalMissedAppointments || 0);

        // Always render no show and missed widgets (renderAlertItems handles empty arrays)
        this._setElementContent('noShowWidget', this._renderAlertItems(data.NoShowAlerts || [], 'noshow', 3));
        this._setElementContent('missedVisitsWidget', this._renderAlertItems(data.MissedAppointments || [], 'missed', 3));
    }

    /**
     * Update require schedule widget
     * @private
     */
    _updateRequireScheduleWidget() {
        const items = this.requireScheduleData || [];
        const count = items.length;

        this._setElementText('requireScheduleCount', count);
        this._setElementText('requireScheduleBadge', count);
        this._setElementContent('requireScheduleWidget', this._renderRequireScheduleItems(items, 3));
    }

    /**
     * Load pending orders count for dashboard stat card
     * @private
     */
    async _loadPendingOrdersCount() {
        try {
            const data = await this._apiGet('/orders/pending-counts').catch(() => null);
            if (data) {
                this._setElementText('pendingOrdersCount', data.TotalPending || 0);
            }
        } catch (err) {
            // Silently ignore - dashboard still works without orders count
        }
    }

    /**
     * Render require schedule items
     * @private
     * @param {Array} items - Require schedule items
     * @param {number} limit - Max items to show
     * @returns {string} HTML
     */
    _renderRequireScheduleItems(items, limit) {
        if (!items?.length) {
            return '<div class="text-center text-muted py-2 small">All appointments scheduled</div>';
        }

        const displayItems = items.slice(0, limit);
        const html = displayItems.map(item => `
            <div class="dashboard-widget-item">
                <div class="d-flex justify-content-between align-items-center">
                    <div>
                        <div class="item-title">
                            <a href="#" class="text-decoration-none text-dark fw-bold patient-link"
                               onclick="event.stopPropagation(); viewPatient(${item.PatientId}); return false;"
                               title="View Patient Profile">
                                ${this._escape(item.PatientName)}
                            </a>
                        </div>
                        ${this._renderContactInfo(item.PatientPhone, item.PatientEmail)}
                        <div class="item-subtitle">
                            <span class="text-info">${item.RequiredAppointments || 0} needed</span> | ${item.ScheduledAppointments || 0}/${item.ExpectedVisits || 0} scheduled
                        </div>
                    </div>
                    <button class="btn btn-info item-action"
                            data-action="schedulePatient"
                            data-patient-id="${item.PatientId}"
                            data-care-episode-id="${item.CareEpisodeId}">
                        Make Schedule
                    </button>
                </div>
            </div>
        `).join('');

        const viewAllLink = items.length > limit ? `
            <div class="text-center mt-2">
                <button class="btn btn-sm btn-link text-info" data-action="viewAll" data-type="requireSchedule">
                    View all ${items.length} <i class="bi bi-arrow-right"></i>
                </button>
            </div>
        ` : '';

        return `<div class="dashboard-widget-items">${html}${viewAllLink}</div>`;
    }

    /**
     * Render widget items
     * @private
     * @param {Array} items - Widget items
     * @param {string} type - Widget type
     * @param {number} limit - Max items to show
     * @returns {string} HTML
     */
    _renderWidgetItems(items, type, limit) {
        if (!items?.length) {
            const emptyMessages = {
                notes: 'No missing notes',
                signatures: 'No notes needing signature'
            };
            return `<div class="text-center text-muted py-2 small">${emptyMessages[type] || 'No items'}</div>`;
        }

        const currentUser = this._getCurrentUser();
        const displayItems = items.slice(0, limit);

        const html = displayItems.map(item => {
            if (type === 'notes') {
                // Missing Notes widget - show View, Write Note, and Record buttons
                const isOwnAppointment = currentUser && currentUser.ProviderId === item.ProviderId;

                return `
                    <div class="dashboard-widget-item">
                        <div class="d-flex justify-content-between align-items-start gap-2">
                            <div class="flex-grow-1">
                                <div class="item-title">
                                    <a href="#" class="text-decoration-none text-dark fw-bold patient-link"
                                       onclick="event.stopPropagation(); viewPatient(${item.PatientId}); return false;"
                                       title="View Patient Profile">
                                        ${this._escape(item.PatientName)}
                                    </a>
                                </div>
                                ${this._renderContactInfo(item.PatientPhone, item.PatientEmail)}
                                <div class="item-subtitle">
                                    ${this._formatDate(item.AppointmentDate)} | ${this._getAppointmentTypeName(item.Type)}
                                </div>
                                <div class="item-subtitle">${this._escape(item.ProviderName || '')}</div>
                            </div>
                            <div class="d-flex align-items-start gap-1 flex-wrap justify-content-end">
                                <button class="btn btn-sm btn-outline-secondary"
                                        data-action="viewAppointment"
                                        data-id="${item.AppointmentId}"
                                        title="View appointment details">
                                    <i class="bi bi-eye me-1"></i>View
                                </button>
                                ${isOwnAppointment ? `
                                <button class="btn btn-sm btn-outline-orange"
                                        data-action="writeNote"
                                        data-id="${item.AppointmentId}"
                                        title="Write Note">
                                    <i class="bi bi-pencil-square me-1"></i>Write
                                </button>
                                <button class="btn btn-sm btn-outline-primary"
                                        data-action="recordSession"
                                        data-id="${item.AppointmentId}"
                                        title="Record Session">
                                    <i class="bi bi-mic-fill me-1"></i>Record
                                </button>
                                ` : ''}
                            </div>
                        </div>
                    </div>
                `;
            } else {
                // Missing Signature widget - show Sign button for providers, View button for admin
                // Admin (Role 1) and Front Desk (Role 3) can only view, not sign
                const canSign = currentUser && (currentUser.Role === 2 || currentUser.Role === 0) && currentUser.ProviderId === item.ProviderId;
                const buttonText = canSign ? 'Sign' : 'View';
                const buttonIcon = canSign ? 'bi-pen' : 'bi-eye';

                return `
                    <div class="dashboard-widget-item">
                        <div class="d-flex justify-content-between align-items-start">
                            <div>
                                <div class="item-title">
                                    <a href="#" class="text-decoration-none text-dark fw-bold patient-link"
                                       onclick="event.stopPropagation(); viewPatient(${item.PatientId}); return false;"
                                       title="View Patient Profile">
                                        ${this._escape(item.PatientName)}
                                    </a>
                                </div>
                                ${this._renderContactInfo(item.PatientPhone, item.PatientEmail)}
                                <div class="item-subtitle">
                                    ${this._formatDate(item.AppointmentDate)} | ${this._escape(item.TemplateName || 'Note')}
                                </div>
                                <div class="item-subtitle">${this._escape(item.ProviderName || '')}</div>
                            </div>
                            <button class="btn btn-sm btn-outline-purple item-action"
                                    data-action="signNote"
                                    data-id="${item.ClinicalNoteId}">
                                <i class="bi ${buttonIcon} me-1"></i>${buttonText}
                            </button>
                        </div>
                    </div>
                `;
            }
        }).join('');

        const linkClass = type === 'notes' ? 'text-orange' : 'text-purple';
        const viewAllLink = items.length > limit ? `
            <div class="text-center mt-2">
                <button class="btn btn-sm btn-link ${linkClass}" data-action="viewAll" data-type="${type}">
                    View all ${items.length} <i class="bi bi-arrow-right"></i>
                </button>
            </div>
        ` : '';

        return `<div class="dashboard-widget-items">${html}${viewAllLink}</div>`;
    }

    /**
     * Render alert items (no show / missed)
     * @private
     * @param {Array} items - Alert items
     * @param {string} type - Alert type ('noshow' or 'missed')
     * @param {number} limit - Max items to show
     * @returns {string} HTML
     */
    _renderAlertItems(items, type, limit) {
        if (!items?.length) {
            return `<div class="text-center text-muted py-2 small">No ${type === 'noshow' ? 'no-show' : 'missed'} alerts</div>`;
        }

        const displayItems = items.slice(0, limit);

        if (type === 'missed') {
            // Render missed appointments with date, days ago, and reschedule button
            const html = displayItems.map(item => {
                // Calculate days missed
                const startTime = item.StartTime || item.AppointmentDate;
                const daysMissed = item.DaysMissed || this._calculateDaysMissed(startTime);
                const dateDisplay = this._formatDate(startTime);
                const timeDisplay = this._formatTime(startTime);
                const appointmentType = this._getAppointmentTypeName(item.Type);

                return `
                <div class="dashboard-widget-item">
                    <div class="d-flex justify-content-between align-items-start">
                        <div>
                            <div class="item-title">
                                <a href="#" class="text-decoration-none text-dark fw-bold patient-link"
                                   onclick="event.stopPropagation(); viewPatient(${item.PatientId}); return false;"
                                   title="View Patient Profile">
                                    ${this._escape(item.PatientName)}
                                </a>
                            </div>
                            ${this._renderContactInfo(item.PatientPhone, item.PatientEmail)}
                            <div class="item-subtitle">
                                ${dateDisplay} ${timeDisplay} | <span class="text-danger">${daysMissed} days ago</span>
                            </div>
                            <div class="item-subtitle">
                                ${appointmentType} | ${this._escape(item.ProviderName || '')}
                            </div>
                        </div>
                        <div class="d-flex gap-1">
                            <button class="btn btn-sm btn-outline-secondary item-action"
                                    onclick="openAppointmentDetails(${item.AppointmentId})"
                                    title="View Details">
                                <i class="bi bi-eye me-1"></i>View
                            </button>
                            <button class="btn btn-sm btn-info item-action"
                                    onclick="rescheduleFromMissed(${item.AppointmentId}, ${item.PatientId}, ${item.ProviderId})"
                                    title="Reschedule">
                                Reschedule
                            </button>
                        </div>
                    </div>
                </div>
            `}).join('');

            const viewAllLink = items.length > limit ? `
                <div class="text-center mt-2">
                    <button class="btn btn-sm btn-link text-danger" onclick="openMissedAppointmentsModal()">
                        View all ${items.length} <i class="bi bi-arrow-right"></i>
                    </button>
                </div>
            ` : '';

            return `<div class="dashboard-widget-items">${html}${viewAllLink}</div>`;
        } else {
            // Render no-show alerts with time overdue and notify button
            const html = displayItems.map(item => {
                // Use StartTime, AppointmentDate, or ScheduledTime as the date source
                const appointmentDateTime = item.StartTime || item.AppointmentDate || item.ScheduledTime;
                const dateDisplay = this._formatDate(appointmentDateTime);
                const timeDisplay = this._formatTime(appointmentDateTime);
                const overdueMinutes = item.OverdueMinutes || this._calculateOverdueMinutes(appointmentDateTime);
                const overdueDisplay = overdueMinutes > 60
                    ? `${Math.floor(overdueMinutes / 60)}h ${overdueMinutes % 60}m`
                    : `${overdueMinutes}m`;
                const appointmentType = this._getAppointmentTypeName(item.Type);

                return `
                <div class="dashboard-widget-item">
                    <div class="d-flex justify-content-between align-items-start">
                        <div>
                            <div class="item-title">
                                <a href="#" class="text-decoration-none text-dark fw-bold patient-link"
                                   onclick="event.stopPropagation(); viewPatient(${item.PatientId}); return false;"
                                   title="View Patient Profile">
                                    ${this._escape(item.PatientName)}
                                </a>
                            </div>
                            ${this._renderContactInfo(item.PatientPhone, item.PatientEmail)}
                            <div class="item-subtitle">
                                Scheduled: ${dateDisplay} ${timeDisplay} | <span class="text-warning">${overdueDisplay} overdue</span>
                            </div>
                            <div class="item-subtitle">
                                ${appointmentType} | ${this._escape(item.ProviderName || '')}
                            </div>
                        </div>
                        <div class="d-flex gap-1">
                            <button class="btn btn-sm btn-outline-primary item-action"
                                    onclick="sendNoShowNotification(${item.AppointmentId}, this)"
                                    title="Send Notification">
                                <i class="bi bi-bell me-1"></i>Notify
                            </button>
                            <button class="btn btn-sm btn-outline-secondary item-action"
                                    onclick="openAppointmentDetails(${item.AppointmentId})"
                                    title="View Details">
                                <i class="bi bi-eye me-1"></i>View
                            </button>
                        </div>
                    </div>
                </div>
            `}).join('');

            const viewAllLink = items.length > limit ? `
                <div class="text-center mt-2">
                    <button class="btn btn-sm btn-link text-warning" onclick="openNoShowModal()">
                        View all ${items.length} <i class="bi bi-arrow-right"></i>
                    </button>
                </div>
            ` : '';

            return `<div class="dashboard-widget-items">${html}${viewAllLink}</div>`;
        }
    }

    /**
     * Calculate days missed from a date
     * @private
     * @param {string} dateString - Date string
     * @returns {number} Days missed
     */
    _calculateDaysMissed(dateString) {
        if (!dateString) return 0;
        const date = new Date(dateString);
        const now = new Date();

        // Compare dates only (ignore time portion) to get accurate day count
        const dateOnly = new Date(date.getFullYear(), date.getMonth(), date.getDate());
        const todayOnly = new Date(now.getFullYear(), now.getMonth(), now.getDate());

        const diffMs = todayOnly - dateOnly;
        const days = Math.floor(diffMs / (1000 * 60 * 60 * 24));

        return Math.max(0, days); // Ensure non-negative
    }

    /**
     * Calculate overdue minutes from scheduled time
     * @private
     * @param {string} dateString - Date string
     * @returns {number} Overdue minutes
     */
    _calculateOverdueMinutes(dateString) {
        if (!dateString) return 0;
        // Use parseServerDateTime to correctly interpret server UTC times
        const date = window.parseServerDateTime ? window.parseServerDateTime(dateString) : new Date(dateString);
        const now = new Date();
        const diffMs = now - date;
        return Math.max(0, Math.floor(diffMs / (1000 * 60)));
    }

    /**
     * Format time for display
     * @private
     * @param {string} dateString - Date string
     * @returns {string} Formatted time
     */
    _formatTime(dateString) {
        if (!dateString) return '';
        // Use parseServerDateTime to correctly interpret server UTC times (handles missing 'Z' suffix)
        const date = window.parseServerDateTime ? window.parseServerDateTime(dateString) : new Date(dateString);
        // Get current location timezone for proper display
        const locationTz = window.getCurrentLocationTimezone ? window.getCurrentLocationTimezone() : null;
        const options = { hour: 'numeric', minute: '2-digit', hour12: true };
        if (locationTz?.timeZoneId) {
            options.timeZone = locationTz.timeZoneId;
        }
        return date.toLocaleTimeString('en-US', options);
    }

    /**
     * Get appointment type name
     * @private
     * @param {number} type - Appointment type code
     * @returns {string} Type name
     */
    _getAppointmentTypeName(type) {
        const types = ['New Patient', 'Follow-Up', 'Annual Physical', 'Wellness', 'Consultation', 'Telehealth', 'Procedure', 'Urgent', 'Lab Review', 'Med Review', 'New Longevity Patient', 'Follow-Up Longevity Patient'];
        return types[type] || 'Appointment';
    }

    /**
     * Get appointment status badge
     * @private
     * @param {number} status - Status code
     * @returns {string} HTML badge
     */
    _getStatusBadge(status) {
        // AppointmentStatus enum: Scheduled=0, Confirmed=1, CheckedIn=2, InProgress=3,
        // Completed=4, NoShow=5, Cancelled=6, Rescheduled=7, Missed=8
        const statuses = {
            0: { class: 'bg-secondary', text: 'Scheduled' },
            1: { class: 'bg-info', text: 'Confirmed' },
            2: { class: 'bg-primary', text: 'Checked In' },
            3: { class: 'bg-warning text-dark', text: 'In Progress' },
            4: { class: 'bg-success', text: 'Completed' },
            5: { class: 'bg-dark', text: 'No Show' },
            6: { class: 'bg-danger', text: 'Cancelled' },
            7: { class: 'bg-secondary', text: 'Rescheduled' },
            8: { class: 'bg-warning text-dark', text: 'Missed' }
        };
        const s = statuses[status] || { class: 'bg-secondary', text: 'Unknown' };
        return `<span class="badge ${s.class}">${s.text}</span>`;
    }

    /**
     * Get contextual flow action buttons based on appointment status
     * @private
     */
    _getFlowActions(apt) {
        const id = apt.AppointmentId;
        const pid = apt.PatientId;
        const stop = 'event.stopPropagation();';
        const user = JSON.parse(localStorage.getItem('currentUser') || '{}');
        const role = parseInt(user.Role ?? user.role ?? -1);
        const isMaNurse = UserRoles.isMaNurse(role);
        const isAdminFrontDesk = role === UserRoles.CLINIC_ADMIN || role === UserRoles.FRONT_DESK;
        const isTelehealth = apt.Type === 5 || apt.IsTelehealth;

        switch (apt.Status) {
            case 0: // Scheduled
            case 1: // Confirmed
                // Telehealth: no manual check-in — patient checks in by joining the link
                if (isTelehealth) {
                    if (isAdminFrontDesk) {
                        return `<span class="badge bg-info text-white" title="Telehealth patients check in when they join the video call">
                            <i class="bi bi-camera-video me-1"></i>Telehealth</span>`;
                    }
                    // Clinical users can start telehealth visit directly (skip check-in)
                    return `<button class="btn btn-info text-white px-4 py-2" onclick="${stop} startVisitAppointment(${id})" title="Start Telehealth Visit">
                        <i class="bi bi-camera-video-fill me-1"></i>Start Visit</button>`;
                }
                // Standard in-person: Check In + Collect Copay placeholder (async-updated with real outstanding balance)
                if (isAdminFrontDesk && apt.CopayDue && parseFloat(apt.CopayDue) > 0) {
                    const escapedName = (apt.PatientName || '').replace(/'/g, "\\'");
                    return `<div class="d-flex gap-2">
                        <button class="btn btn-primary px-3 py-2" onclick="${stop} checkInAppointment(${id})" title="Check In">
                            <i class="bi bi-box-arrow-in-right me-1"></i>Check In</button>
                        <span id="copayBtn_${id}" data-patient-id="${pid}" data-appointment-id="${id}" data-patient-name="${escapedName}"></span>
                    </div>`;
                }
                return `<button class="btn btn-primary px-4 py-2" onclick="${stop} checkInAppointment(${id})" title="Check In">
                    <i class="bi bi-box-arrow-in-right me-1"></i>Check In</button>`;

            case 2: // CheckedIn
                // Admin/Front Desk cannot start visits
                if (isAdminFrontDesk) {
                    return `<span class="badge bg-primary fs-6 px-3 py-2"><i class="bi bi-hourglass-split me-1"></i>Waiting</span>`;
                }
                // Telehealth: Start Visit with video icon
                if (isTelehealth) {
                    return `<button class="btn btn-info text-white px-4 py-2" onclick="${stop} startVisitAppointment(${id})" title="Start Telehealth Visit">
                        <i class="bi bi-camera-video-fill me-1"></i>Start Visit</button>`;
                }
                return `<button class="btn btn-warning px-4 py-2" onclick="${stop} startVisitAppointment(${id})" title="Start Visit">
                    <i class="bi bi-play-fill me-1"></i>Start Visit</button>`;

            case 3: // InProgress
                // Admin/Front Desk see view-only status
                if (isAdminFrontDesk) {
                    return `<span class="badge bg-warning text-dark fs-6 px-3 py-2"><i class="bi bi-activity me-1"></i>In Progress</span>`;
                }
                // Telehealth: Resume with video icon
                if (isTelehealth) {
                    return `<button class="btn btn-success px-4 py-2" onclick="${stop} window._resumeEncounter(${id}, ${pid})" title="Resume Telehealth Visit">
                        <i class="bi bi-camera-video me-1"></i>Resume</button>`;
                }
                // MA/Nurse or Provider: Resume button
                return `<button class="btn btn-success px-4 py-2" onclick="${stop} window._resumeEncounter(${id}, ${pid})" title="Resume Visit">
                    <i class="bi bi-arrow-right-circle me-1"></i>Resume</button>`;

            case 4: // Completed
                return `<span class="text-success"><i class="bi bi-check-circle-fill me-1"></i>Done</span>`;

            case 5: // NoShow
                return `<button class="btn btn-sm btn-outline-secondary px-3 py-1" onclick="${stop} rescheduleFromMissed(${id})" title="Reschedule">
                    <i class="bi bi-calendar-plus me-1"></i>Reschedule</button>`;

            default:
                return '';
        }
    }

    // === Helper Methods ===

    /**
     * Set element text content
     * @private
     */
    _setElementText(id, text) {
        const el = document.getElementById(id);
        if (el) el.textContent = text;
    }

    /**
     * Set element HTML content
     * @private
     */
    _setElementContent(id, html) {
        const el = document.getElementById(id);
        if (el) el.innerHTML = html;
    }

    // === API Methods ===

    async _apiGet(url) {
        // Use silent mode (no loader) if this is a SignalR-triggered refresh
        const showLoader = !this._isSilentRefresh;
        if (this.api) {
            return this.api.get(url, { showLoader });
        }
        // Fallback fetch - manually control loader
        if (showLoader && window.LoadingState) {
            LoadingState.show();
        }
        try {
            const response = await fetch(`/api${url}`, {
                headers: this._getHeaders()
            });
            if (!response.ok) {
                const error = await response.json().catch(() => ({}));
                throw new Error(error.message || 'API request failed');
            }
            return response.json();
        } finally {
            if (showLoader && window.LoadingState) {
                LoadingState.hide();
            }
        }
    }

    async _apiDelete(url) {
        if (this.api && this.api.delete) {
            return this.api.delete(url);
        }
        const response = await fetch(`/api${url}`, {
            method: 'DELETE',
            headers: this._getHeaders()
        });
        if (!response.ok) {
            const error = await response.json().catch(() => ({}));
            throw new Error(error.message || 'API request failed');
        }
        if (response.status === 204) return null;
        const text = await response.text();
        return text ? JSON.parse(text) : null;
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

    /**
     * Render contact information (phone and email) for dashboard items
     * @private
     * @param {string} phone - Patient phone number
     * @param {string} email - Patient email address
     * @returns {string} HTML for contact info
     */
    _renderContactInfo(phone, email) {
        if (!phone && !email) {
            return '';
        }
        let html = '<div class="item-contact small">';
        if (phone) {
            html += `<div><a href="tel:${this._escape(phone)}" class="text-muted text-decoration-none contact-link" onclick="event.stopPropagation();" title="Call ${this._escape(phone)}"><i class="bi bi-telephone-fill me-1"></i>${this._escape(phone)}</a></div>`;
        }
        if (email) {
            html += `<div><a href="mailto:${this._escape(email)}" class="text-muted text-decoration-none contact-link" onclick="event.stopPropagation();" title="Email ${this._escape(email)}"><i class="bi bi-envelope-fill me-1"></i>${this._escape(email)}</a></div>`;
        }
        html += '</div>';
        return html;
    }

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
    // Note: _formatTime is defined earlier in this class (line ~977)

    _formatDuration(seconds) {
        if (!seconds || seconds <= 0) return '0:00';
        const mins = Math.floor(seconds / 60);
        const secs = seconds % 60;
        return `${mins}:${secs.toString().padStart(2, '0')}`;
    }

    _emit(event, data = {}) {
        if (this.eventBus) {
            this.eventBus.emit(event, data);
        }
    }

    /**
     * Destroy the module and clean up
     */
    destroy() {
        if (this.scheduleTable) {
            this.scheduleTable.removeEventListener('click', this._handleRowClick);
        }
        if (this.container) {
            this.container.removeEventListener('click', this._handleWidgetAction);
        }

        this.stats = null;
        this.todayAppointments = [];
        this.missingNotes = [];
        this.missingSignatures = [];
        this.careEpisodeData = null;
        this.requireScheduleData = [];
        this.container = null;
        this.scheduleTable = null;
        this.isInitialized = false;
    }
}

// Export for module usage
window.DashboardModule = DashboardModule;

// Auto-initialize when DOM is ready
document.addEventListener('DOMContentLoaded', function() {
    const container = document.getElementById('dashboardPage');
    if (!container) return;

    const initWhenReady = () => {
        const isAuthenticated = (typeof currentUser !== 'undefined' && currentUser) ||
                               (window.App && window.App.isAuthenticated && window.App.isAuthenticated());

        if (!isAuthenticated) {
            setTimeout(initWhenReady, 200);
            return;
        }

        if (window.dashboardModule) {
            window.dashboardModule.load();
            return;
        }

        window.dashboardModule = new DashboardModule({
            api: window.apiService || (window.App && window.App.api),
            eventBus: window.eventBus || (window.App && window.App.events)
        });

        window.dashboardModule.init();
        window.dashboardModule.load();
    };

    initWhenReady();
});
