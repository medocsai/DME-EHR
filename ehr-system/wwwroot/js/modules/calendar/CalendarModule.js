/**
 * CalendarModule - FullCalendar integration for appointment scheduling
 *
 * Manages calendar display, appointment events, provider unavailability,
 * and schedule filtering.
 *
 * @example
 *   const calendar = App.modules.get('calendar');
 *   await calendar.init();
 *   calendar.refresh();
 */
class CalendarModule {
    /**
     * @param {Object} options - Module options
     * @param {Object} options.api - API service instance
     * @param {Object} options.eventBus - Event bus for cross-module communication
     */
    constructor(options = {}) {
        this.api = options.api || null;
        this.eventBus = options.eventBus || null;

        // State
        this.calendar = null;
        this.filters = {
            providerId: null,
            patientId: null
        };
        this.isInitialized = false;

        // Appointment type colors - Light Modern Pastel Palette
        this.typeColors = {
            0: { bg: '#D1FAE5', border: '#10B981', text: '#065F46' },  // New Patient - Soft Green
            1: { bg: '#DBEAFE', border: '#3B82F6', text: '#1E40AF' },  // Follow-Up - Soft Blue
            2: { bg: '#EDE9FE', border: '#8B5CF6', text: '#5B21B6' },  // Annual Physical - Soft Violet
            3: { bg: '#CFFAFE', border: '#06B6D4', text: '#155E75' },  // Wellness - Soft Cyan
            4: { bg: '#FEF3C7', border: '#F59E0B', text: '#92400E' },  // Consultation - Soft Amber
            5: { bg: '#F1F5F9', border: '#64748B', text: '#334155' },  // Telehealth - Soft Slate
            6: { bg: '#FCE7F3', border: '#EC4899', text: '#9D174D' },  // Procedure - Soft Pink
            7: { bg: '#FEE2E2', border: '#EF4444', text: '#991B1B' },  // Urgent - Soft Red
            8: { bg: '#EFEBE9', border: '#8D6E63', text: '#4E342E' },  // Lab Review - Soft Brown
            9: { bg: '#E8EAF6', border: '#5C6BC0', text: '#283593' },  // Med Review - Soft Indigo
        };

        // DOM reference
        this.container = null;

        // Bind methods
        this._handleEventClick = this._handleEventClick.bind(this);
        this._handleSelect = this._handleSelect.bind(this);
        this._loadAppointments = this._loadAppointments.bind(this);
        this._loadUnavailability = this._loadUnavailability.bind(this);
    }

    /**
     * Initialize the module
     * @returns {Promise<void>}
     */
    async init() {
        this.container = document.getElementById('calendar');

        if (!this.container) {
            console.warn('[CalendarModule] Calendar container not found');
            return;
        }

        // Check if calendar already exists
        if (this.calendar) {
            console.warn('[CalendarModule] Calendar already initialized');
            return;
        }

        this._createCalendar();
        this._setupEventListeners();
        this.isInitialized = true;

        // Render calendar legend
        if (typeof renderCalendarLegend === 'function') {
            renderCalendarLegend();
        }

        this._emit('calendar:initialized');

        // Initialize schedule filters
        this._initScheduleFilters();
    }

    /**
     * Setup event listeners for appointment changes
     * @private
     */
    _setupEventListeners() {
        if (!this.eventBus) return;

        // Listen for appointment created/updated events to refresh calendar
        this.eventBus.on('appointments:created', () => {
            console.log('[CalendarModule] Appointment created, refreshing calendar');
            this.refresh();
        });

        this.eventBus.on('appointments:updated', () => {
            console.log('[CalendarModule] Appointment updated, refreshing calendar');
            this.refresh();
        });

        this.eventBus.on('appointments:cancelled', () => {
            console.log('[CalendarModule] Appointment cancelled, refreshing calendar');
            this.refresh();
        });

        this.eventBus.on('appointments:reinstated', () => {
            console.log('[CalendarModule] Appointment reinstated, refreshing calendar');
            this.refresh();
        });

        this.eventBus.on('appointments:checkedIn', () => {
            console.log('[CalendarModule] Appointment checked in, refreshing calendar');
            this.refresh();
        });

        // Listen for SignalR appointment change notifications
        this.eventBus.on('signalr:appointmentChanged', (notification) => {
            this._handleSignalRAppointmentChanged(notification);
        });

        // Also listen via DOM event for broader compatibility
        document.addEventListener('appointmentChanged', (event) => {
            this._handleSignalRAppointmentChanged(event.detail);
        });
    }

    /**
     * Handle SignalR appointment change notification
     * @private
     * @param {Object} notification - The appointment change notification
     */
    _handleSignalRAppointmentChanged(notification) {
        // Only refresh if we're on the schedule page and calendar is initialized
        if (!this.calendar) {
            return;
        }

        console.log(`[CalendarModule] SignalR appointment ${notification.ChangeType}:`, notification.AppointmentId);

        // Debounce rapid updates
        if (this._signalRRefreshTimeout) {
            clearTimeout(this._signalRRefreshTimeout);
        }

        this._signalRRefreshTimeout = setTimeout(() => {
            console.log('[CalendarModule] Silently refreshing calendar due to SignalR notification');
            this.silentRefresh();
        }, 500); // 500ms debounce
    }

    /**
     * Refresh calendar without showing loading overlay
     * Used for real-time SignalR updates to avoid disrupting user workflow
     * @public
     */
    silentRefresh() {
        if (this.calendar) {
            // Set flag to indicate silent refresh (no loader)
            this._isSilentRefresh = true;
            this.calendar.refetchEvents();
            // Reset flag after a short delay (events are fetched asynchronously)
            setTimeout(() => {
                this._isSilentRefresh = false;
            }, 100);
        }
    }

    /**
     * Initialize schedule filters (patient, provider, type, workflow)
     * @private
     */
    async _initScheduleFilters() {
        // Load patients for filter
        await this._loadFilterPatients();

        // Load providers for filter
        await this._loadFilterProviders();

        // Setup filter event handlers
        this._setupFilterHandlers();
    }

    /**
     * Initialize patient filter dropdown (no preloading - uses search-on-type)
     * @private
     */
    async _loadFilterPatients() {
        const patientList = document.getElementById('patientFilterList');
        if (!patientList) return;

        // Show initial instruction instead of loading all patients
        patientList.innerHTML = '<div class="text-muted small text-center py-2">Type at least 3 characters to search...</div>';
    }

    /**
     * Search patients for filter dropdown using blind index
     * @private
     */
    async _searchFilterPatients(query) {
        const patientList = document.getElementById('patientFilterList');
        if (!patientList) return;

        // Require minimum 3 characters to avoid returning too many results
        if (query.length > 0 && query.length < 3) {
            patientList.innerHTML = '<div class="text-muted small text-center py-2">Type at least 3 characters to search...</div>';
            return;
        }

        // Show loading state
        if (query.length >= 3) {
            patientList.innerHTML = '<div class="text-muted small text-center py-2">Searching...</div>';
        }

        try {
            // Use the patient search API endpoint (uses blind index for scalability)
            const response = await this._apiGet(`/patients/search?q=${encodeURIComponent(query)}&take=20`);
            const patients = response?.Results || [];

            if (!patients || patients.length === 0) {
                patientList.innerHTML = query.length >= 3
                    ? '<div class="text-muted small text-center py-2">No patients found</div>'
                    : '<div class="text-muted small text-center py-2">Type at least 3 characters to search...</div>';
                return;
            }

            patientList.innerHTML = patients.map(p => `
                <div class="schedule-filter-item patient-filter-item" data-patient-id="${p.PatientId}" data-patient-name="${this._escape(p.FullName)}" data-patient-mrn="${this._escape(p.MRN || '')}">
                    <div class="fw-medium">${this._escape(p.FullName)}</div>
                    <small class="text-muted">${p.MRN || ''}</small>
                </div>
            `).join('');

            // Add click handlers
            patientList.querySelectorAll('.patient-filter-item').forEach(item => {
                item.addEventListener('click', () => {
                    const patientId = parseInt(item.dataset.patientId);
                    const patientName = item.dataset.patientName;
                    this._selectPatientFilter(patientId, patientName);
                });
            });
        } catch (error) {
            console.error('[CalendarModule] Error searching patients for filter:', error);
            patientList.innerHTML = '<div class="text-danger small text-center py-2">Error searching patients</div>';
        }
    }

    /**
     * Load providers for the filter dropdown
     * @private
     */
    async _loadFilterProviders() {
        const providerList = document.getElementById('providerFilterList');
        if (!providerList) return;

        try {
            const providers = await this._apiGet('/providers?activeOnly=true');

            if (!providers || providers.length === 0) {
                providerList.innerHTML = '<div class="text-muted small text-center py-2">No providers found</div>';
                return;
            }

            providerList.innerHTML = providers.map(p => `
                <div class="schedule-filter-item provider-filter-item" data-provider-id="${p.ProviderId}" data-provider-name="${this._escape(p.FullName)}">
                    <div class="fw-medium">${this._escape(p.FullName)}</div>
                    <small class="text-muted">${p.Specialty || 'Provider'}</small>
                </div>
            `).join('');

            // Add click handlers
            providerList.querySelectorAll('.provider-filter-item').forEach(item => {
                item.addEventListener('click', () => {
                    const providerId = parseInt(item.dataset.providerId);
                    const providerName = item.dataset.providerName;
                    this._selectProviderFilter(providerId, providerName);
                });
            });
        } catch (error) {
            console.error('[CalendarModule] Error loading providers for filter:', error);
            providerList.innerHTML = '<div class="text-danger small text-center py-2">Error loading providers</div>';
        }
    }

    /**
     * Setup filter event handlers
     * @private
     */
    _setupFilterHandlers() {
        // Patient search - uses API for scalable search
        const patientSearch = document.getElementById('patientFilterSearch');
        if (patientSearch) {
            let searchTimeout = null;
            patientSearch.addEventListener('input', (e) => {
                const searchTerm = e.target.value.trim();

                // Debounce search to avoid excessive API calls
                if (searchTimeout) clearTimeout(searchTimeout);
                searchTimeout = setTimeout(() => {
                    this._searchFilterPatients(searchTerm);
                }, 300); // Wait 300ms after user stops typing
            });
        }

        // Provider search
        const providerSearch = document.getElementById('providerFilterSearch');
        if (providerSearch) {
            providerSearch.addEventListener('input', (e) => {
                const searchTerm = e.target.value.toLowerCase();
                const items = document.querySelectorAll('#providerFilterList .provider-filter-item');
                items.forEach(item => {
                    const name = (item.dataset.providerName || '').toLowerCase();
                    item.style.display = name.includes(searchTerm) ? '' : 'none';
                });
            });
        }

        // Clear patient filter button
        const clearPatientBtn = document.getElementById('clearPatientFilter');
        if (clearPatientBtn) {
            clearPatientBtn.addEventListener('click', () => this._clearPatientFilter());
        }

        // Clear provider filter button
        const clearProviderBtn = document.getElementById('clearProviderFilter');
        if (clearProviderBtn) {
            clearProviderBtn.addEventListener('click', () => this._clearProviderFilter());
        }

        // Clear all filters button
        const clearAllBtn = document.getElementById('clearScheduleFilters');
        if (clearAllBtn) {
            clearAllBtn.addEventListener('click', () => this._clearAllFilters());
        }

        // Type filter checkboxes
        document.querySelectorAll('.type-filter').forEach(checkbox => {
            checkbox.addEventListener('change', () => this._applyTypeFilters());
        });

        // Workflow filter checkboxes
        document.querySelectorAll('.workflow-filter').forEach(checkbox => {
            checkbox.addEventListener('change', () => this._applyWorkflowFilters());
        });
    }

    /**
     * Select a patient filter
     * @private
     * @param {number} patientId - Patient ID
     * @param {string} patientName - Patient name
     */
    _selectPatientFilter(patientId, patientName) {
        this.filters.patientId = patientId;

        // Update label
        const label = document.getElementById('patientFilterLabel');
        if (label) label.textContent = patientName;

        // Show clear button
        const clearBtn = document.getElementById('clearPatientFilter');
        if (clearBtn) clearBtn.style.display = '';

        // Update button style
        const btn = document.getElementById('patientFilterBtn');
        if (btn) btn.classList.add('active');

        // Close dropdown
        const dropdown = bootstrap.Dropdown.getInstance(document.getElementById('patientFilterBtn'));
        if (dropdown) dropdown.hide();

        // Refresh calendar
        this.refresh();
        this._updateFilterCount();
    }

    /**
     * Select a provider filter
     * @private
     * @param {number} providerId - Provider ID
     * @param {string} providerName - Provider name
     */
    _selectProviderFilter(providerId, providerName) {
        this.filters.providerId = providerId;

        // Update label
        const label = document.getElementById('providerFilterLabel');
        if (label) label.textContent = providerName;

        // Show clear button
        const clearBtn = document.getElementById('clearProviderFilter');
        if (clearBtn) clearBtn.style.display = '';

        // Update button style
        const btn = document.getElementById('providerFilterBtn');
        if (btn) btn.classList.add('active');

        // Close dropdown
        const dropdown = bootstrap.Dropdown.getInstance(document.getElementById('providerFilterBtn'));
        if (dropdown) dropdown.hide();

        // Refresh calendar
        this.refresh();
        this._updateFilterCount();
    }

    /**
     * Clear patient filter
     * @private
     */
    _clearPatientFilter() {
        this.filters.patientId = null;

        // Reset label
        const label = document.getElementById('patientFilterLabel');
        if (label) label.textContent = 'Patient';

        // Hide clear button
        const clearBtn = document.getElementById('clearPatientFilter');
        if (clearBtn) clearBtn.style.display = 'none';

        // Update button style
        const btn = document.getElementById('patientFilterBtn');
        if (btn) btn.classList.remove('active');

        // Refresh calendar
        this.refresh();
        this._updateFilterCount();
    }

    /**
     * Clear provider filter
     * @private
     */
    _clearProviderFilter() {
        this.filters.providerId = null;

        // Reset label
        const label = document.getElementById('providerFilterLabel');
        if (label) label.textContent = 'Provider';

        // Hide clear button
        const clearBtn = document.getElementById('clearProviderFilter');
        if (clearBtn) clearBtn.style.display = 'none';

        // Update button style
        const btn = document.getElementById('providerFilterBtn');
        if (btn) btn.classList.remove('active');

        // Refresh calendar
        this.refresh();
        this._updateFilterCount();
    }

    /**
     * Clear all filters
     * @private
     */
    _clearAllFilters() {
        this._clearPatientFilter();
        this._clearProviderFilter();

        // Clear type filters
        document.querySelectorAll('.type-filter').forEach(cb => cb.checked = false);
        this.typeFilters = new Set();
        this._updateTypeFilterCount();

        // Clear workflow filters
        document.querySelectorAll('.workflow-filter').forEach(cb => cb.checked = false);
        this.workflowFilters = new Set();
        this._updateWorkflowFilterCount();

        // Hide clear all button
        const clearAllBtn = document.getElementById('clearScheduleFilters');
        if (clearAllBtn) clearAllBtn.style.display = 'none';

        this.refresh();
    }

    /**
     * Apply type filters (client-side filtering)
     * @private
     */
    _applyTypeFilters() {
        this.typeFilters = new Set();
        document.querySelectorAll('.type-filter:checked').forEach(cb => {
            this.typeFilters.add(cb.value);
        });

        this._updateTypeFilterCount();
        this._updateFilterCount();

        // Re-render calendar events with filter
        if (this.calendar) {
            this.calendar.refetchEvents();
        }
    }

    /**
     * Apply workflow filters (client-side filtering)
     * @private
     */
    _applyWorkflowFilters() {
        this.workflowFilters = new Set();
        document.querySelectorAll('.workflow-filter:checked').forEach(cb => {
            this.workflowFilters.add(cb.value);
        });

        this._updateWorkflowFilterCount();
        this._updateFilterCount();

        // Re-render calendar events with filter
        if (this.calendar) {
            this.calendar.refetchEvents();
        }
    }

    /**
     * Update type filter count badge
     * @private
     */
    _updateTypeFilterCount() {
        const count = this.typeFilters?.size || 0;
        const badge = document.getElementById('typeFilterCount');
        const btn = document.getElementById('typeFilterBtn');

        if (badge) {
            badge.textContent = count;
            badge.classList.toggle('d-none', count === 0);
        }
        if (btn) {
            btn.classList.toggle('active', count > 0);
        }
    }

    /**
     * Update workflow filter count badge
     * @private
     */
    _updateWorkflowFilterCount() {
        const count = this.workflowFilters?.size || 0;
        const badge = document.getElementById('workflowFilterCount');
        const btn = document.getElementById('workflowFilterBtn');

        if (badge) {
            badge.textContent = count;
            badge.classList.toggle('d-none', count === 0);
        }
        if (btn) {
            btn.classList.toggle('active', count > 0);
        }
    }

    /**
     * Update overall filter count and show/hide clear all button
     * @private
     */
    _updateFilterCount() {
        const hasFilters = this.filters.patientId || this.filters.providerId ||
                          (this.typeFilters && this.typeFilters.size > 0) ||
                          (this.workflowFilters && this.workflowFilters.size > 0);

        const clearAllBtn = document.getElementById('clearScheduleFilters');
        if (clearAllBtn) {
            clearAllBtn.style.display = hasFilters ? '' : 'none';
        }
    }

    /**
     * Refresh calendar to reload events
     * @public
     */
    refresh() {
        if (this.calendar) {
            console.log('[CalendarModule] Refetching events');
            this.calendar.refetchEvents();
        }
    }

    /**
     * Create FullCalendar instance
     * @private
     */
    _createCalendar() {
        if (!window.FullCalendar) {
            console.error('[CalendarModule] FullCalendar library not loaded');
            return;
        }

        this.calendar = new FullCalendar.Calendar(this.container, {
            initialView: 'dayGridWeek',
            headerToolbar: {
                left: 'prev,next today',
                center: 'title',
                right: 'dayGridMonth,dayGridWeek,timeGridDay'
            },
            eventDisplay: 'block',
            nowIndicator: true,
            selectable: true,
            selectMirror: true,
            eventClick: this._handleEventClick,
            select: this._handleSelect,
            eventSources: [
                { events: this._loadAppointments },
                { events: this._loadUnavailability }
            ],
            eventDidMount: (info) => this._handleEventMount(info)
        });

        this.calendar.render();
    }

    /**
     * Handle event click
     * @private
     * @param {Object} info - FullCalendar event info
     */
    _handleEventClick(info) {
        console.log('[CalendarModule] Event clicked:', info.event.id, info.event.title);

        const props = info.event.extendedProps;
        console.log('[CalendarModule] Event extendedProps:', props);

        if (props.isUnavailability) {
            console.log('[CalendarModule] This is an unavailability event');
            this._emit('calendar:unavailabilityClicked', { unavailability: props });
            // Call global function if available
            if (typeof showUnavailabilityDetails === 'function') {
                showUnavailabilityDetails(props);
            }
        } else {
            const appointmentId = props.appointmentId;
            console.log('[CalendarModule] Opening appointment details for ID:', appointmentId);

            if (!appointmentId) {
                console.error('[CalendarModule] No appointmentId found in event props');
                return;
            }

            this._emit('calendar:appointmentClicked', { appointmentId });

            // Open the appointment details modal
            if (typeof openAppointmentDetails === 'function') {
                console.log('[CalendarModule] Calling openAppointmentDetails...');
                openAppointmentDetails(appointmentId);
            } else {
                console.log('[CalendarModule] openAppointmentDetails not found, using fallback...');
                // Fallback: use module directly
                const appointmentModule = window.App?.modules?.get('appointments') || window.appointmentModule;
                if (appointmentModule && appointmentModule.openDetails) {
                    appointmentModule.openDetails(appointmentId);
                } else {
                    console.error('[CalendarModule] Cannot open appointment details - no handler available');
                }
            }
        }
    }

    /**
     * Handle calendar selection (for creating appointments)
     * @private
     * @param {Object} info - FullCalendar selection info
     */
    _handleSelect(info) {
        this._emit('calendar:slotSelected', {
            start: info.start,
            end: info.end
        });

        // Open new appointment wizard
        const appointmentModule = window.App?.modules?.get('appointments') || window.appointmentModule;
        if (appointmentModule && appointmentModule.openNewAppointment) {
            appointmentModule.openNewAppointment(info.start, info.end);
        } else if (typeof openNewAppointment === 'function') {
            openNewAppointment(info.start, info.end);
        } else {
            console.warn('[CalendarModule] Cannot open new appointment - no handler available');
        }
    }

    /**
     * Load appointments for calendar
     * @private
     * @param {Object} info - FullCalendar fetch info
     * @param {Function} successCallback - Success callback
     * @param {Function} failureCallback - Failure callback
     */
    async _loadAppointments(info, successCallback, failureCallback) {
        try {
            const currentUser = this._getCurrentUser();
            const startIso = info.start.toISOString();
            const endIso = info.end.toISOString();

            let url = `/appointments?startDate=${encodeURIComponent(startIso)}&endDate=${encodeURIComponent(endIso)}`;

            // Provider filtering
            if (currentUser?.Role === 2 && currentUser?.ProviderId) {
                url += `&providerId=${currentUser.ProviderId}`;
            } else if (this.filters.providerId) {
                url += `&providerId=${this.filters.providerId}`;
            }

            // Patient filtering
            if (this.filters.patientId) {
                url += `&patientId=${this.filters.patientId}`;
            }

            // Use silent mode (no loader) if this is a SignalR-triggered refresh
            const showLoader = !this._isSilentRefresh;
            const appointments = await this._apiGet(url.replace('/appointments', '/appointments'), { showLoader });

            // Map to events and apply client-side filters (type and workflow)
            let events = (appointments || []).map(apt => this._mapAppointmentToEvent(apt));

            // Apply type filters if any are selected
            if (this.typeFilters && this.typeFilters.size > 0) {
                events = events.filter(event => this._matchesTypeFilter(event));
            }

            // Apply workflow filters if any are selected
            if (this.workflowFilters && this.workflowFilters.size > 0) {
                events = events.filter(event => this._matchesWorkflowFilter(event));
            }

            successCallback(events);
        } catch (error) {
            console.error('[CalendarModule] Failed to load appointments:', error);
            failureCallback(error);
        }
    }

    /**
     * Load unavailability for calendar
     * @private
     * @param {Object} info - FullCalendar fetch info
     * @param {Function} successCallback - Success callback
     * @param {Function} failureCallback - Failure callback
     */
    async _loadUnavailability(info, successCallback, failureCallback) {
        try {
            const currentUser = this._getCurrentUser();
            const startIso = info.start.toISOString().split('T')[0];
            const endIso = info.end.toISOString().split('T')[0];

            let url = `/therapist-unavailability/calendar-events?startDate=${startIso}&endDate=${endIso}`;

            if (currentUser?.Role === 2 && currentUser?.ProviderId) {
                url += `&providerId=${currentUser.ProviderId}`;
            }

            // Use silent mode (no loader) if this is a SignalR-triggered refresh
            const showLoader = !this._isSilentRefresh;
            const events = await this._apiGet(url, { showLoader });
            successCallback(events || []);
        } catch (error) {
            console.error('[CalendarModule] Failed to load unavailability:', error);
            successCallback([]);
        }
    }

    /**
     * Map appointment to FullCalendar event
     * @private
     * @param {Object} apt - Appointment data
     * @returns {Object} FullCalendar event object
     */
    _mapAppointmentToEvent(apt) {
        const isCancelled = apt.Status === 6;
        const isMissed = apt.Status === 8 || this._isCalculatedMissed(apt);
        const isRescheduled = apt.RescheduledToAppointmentId != null;
        const isFromReschedule = apt.RescheduledFromAppointmentId != null;

        // Determine colors
        const colors = this._getEventColors(apt, isMissed, isRescheduled, isCancelled);

        // Get calendar datetime
        const calendarStart = this._getCalendarDateTime(apt, 'StartTime');
        const calendarEnd = this._getCalendarDateTime(apt, 'EndTime');

        // Format time display - use current location timezone as fallback
        const locationTz = window.getCurrentLocationTimezone ? window.getCurrentLocationTimezone() : { timeZoneId: null, abbreviation: '' };
        const effectiveTzId = apt.TimeZoneId || locationTz.timeZoneId;
        const startTime = this._formatEventTime(apt.StartTime, effectiveTzId);
        const endTime = this._formatEventTime(apt.EndTime, effectiveTzId);
        const tz = apt.TimeZoneAbbreviation || locationTz.abbreviation || '';
        const timeDisplay = tz ? `${startTime} - ${endTime} ${tz}` : `${startTime} - ${endTime}`;

        // Build class names for styling
        const classNames = ['modern-event'];
        if (isMissed) classNames.push('missed-appointment');
        if (isCancelled) classNames.push('cancelled-appointment');
        if (isFromReschedule) classNames.push('rescheduled-appointment');

        return {
            id: `apt-${apt.AppointmentId}`,
            title: apt.ProviderName, // Primary title - provider name
            start: calendarStart,
            end: calendarEnd,
            backgroundColor: colors.background,
            borderColor: colors.border,
            textColor: colors.text,
            classNames: classNames,
            extendedProps: {
                appointmentId: apt.AppointmentId,
                patientId: apt.PatientId,
                patientName: apt.PatientName,
                patientMRN: apt.PatientMRN,
                providerId: apt.ProviderId,
                providerName: apt.ProviderName,
                type: apt.Type,
                typeName: this._getTypeName(apt.Type),
                status: apt.Status,
                documentationStatus: apt.DocumentationStatus,
                reason: apt.Reason,
                hasNote: apt.HasNote,
                hasSignedNote: apt.HasSignedNote,
                intakeStatus: apt.IntakeStatus,
                isUnavailability: false,
                isCancelled,
                isMissed,
                isRescheduled,
                isFromReschedule,
                timeZoneId: apt.TimeZoneId,
                timeZoneAbbreviation: tz,
                timeDisplay: timeDisplay
            }
        };
    }

    /**
     * Check if appointment is calculated as missed
     * @private
     * @param {Object} apt - Appointment data
     * @returns {boolean} Whether appointment is missed
     */
    _isCalculatedMissed(apt) {
        // Missed if scheduled/confirmed and appointment date is in the past
        if (apt.Status !== 0 && apt.Status !== 1) return false;

        const now = new Date();
        const aptDate = new Date(apt.StartTime);
        return aptDate < now && aptDate.toDateString() !== now.toDateString();
    }

    /**
     * Get event colors based on status
     * @private
     * @param {Object} apt - Appointment data
     * @param {boolean} isMissed - Is missed appointment
     * @param {boolean} isRescheduled - Has been rescheduled
     * @param {boolean} isCancelled - Is cancelled
     * @returns {Object} Color object
     */
    _getEventColors(apt, isMissed, isRescheduled, isCancelled) {
        if (isMissed && isRescheduled) {
            return { background: '#FECACA', border: '#F87171', text: '#991B1B' };
        }
        if (isMissed && !isRescheduled) {
            return { background: '#FEE2E2', border: '#EF4444', text: '#991B1B' };
        }
        if (isCancelled) {
            return { background: '#F3F4F6', border: '#9CA3AF', text: '#6B7280' };
        }

        // Get color palette for appointment type
        const colorPalette = this.typeColors[apt.Type] || this.typeColors[1];
        return {
            background: colorPalette.bg,
            border: colorPalette.border,
            text: colorPalette.text
        };
    }

    /**
     * Get calendar datetime string for FullCalendar
     * @private
     * @param {Object} apt - Appointment data
     * @param {string} field - Field name (StartTime or EndTime)
     * @returns {string} ISO datetime string without Z suffix for local positioning
     */
    _getCalendarDateTime(apt, field) {
        const dateStr = apt[field];
        if (!dateStr) return null;

        // Use parseServerDateTime to correctly interpret server UTC times (handles missing 'Z' suffix)
        const date = window.parseServerDateTime ? window.parseServerDateTime(dateStr) : new Date(dateStr);

        // Use appointment's timezone, fallback to current location's timezone
        const tzId = apt.TimeZoneId || (window.getCurrentLocationTimezone ? window.getCurrentLocationTimezone().timeZoneId : null);

        // TIMEZONE FIX: Convert UTC time to location's timezone for proper calendar positioning
        if (tzId && window.convertUtcToTimezone) {
            const converted = window.convertUtcToTimezone(date, tzId);
            return converted.isoString;
        }

        // Fallback: Parse and return without timezone suffix for local positioning
        return date.toISOString().replace('Z', '');
    }

    /**
     * Handle event mount (add ribbons, tooltips, custom rendering)
     * @private
     * @param {Object} info - FullCalendar mount info
     */
    _handleEventMount(info) {
        const props = info.event.extendedProps;

        // Add tooltip
        if (props.isUnavailability) {
            info.el.title = `${props.providerName} - ${props.typeName}: ${props.reason || 'Unavailable'}`;
        } else {
            info.el.title = `${props.patientName} (${props.patientMRN})\n${props.providerName}\n${props.typeName}\n${props.timeDisplay}`;

            // Custom render the event content for modern look
            this._renderModernEventContent(info.el, props);

            // Add documentation status ribbon
            if (!props.isCancelled && !props.isMissed) {
                this._addStatusRibbon(info.el, props.documentationStatus);
            }
        }

        // Apply filter visibility
        if (this._hasActiveFilters() && !this._eventMatchesFilters(props)) {
            info.el.style.display = 'none';
        }
    }

    /**
     * Render modern event content
     * @private
     * @param {HTMLElement} el - Event element
     * @param {Object} props - Event extended props
     */
    _renderModernEventContent(el, props) {
        const mainContent = el.querySelector('.fc-event-main');
        if (!mainContent) return;

        // Build status badge if needed
        let statusBadge = '';
        if (props.isMissed && props.isRescheduled) {
            statusBadge = '<span class="event-status-badge badge-rescheduled">Rescheduled</span>';
        } else if (props.isMissed) {
            statusBadge = '<span class="event-status-badge badge-missed">Missed</span>';
        } else if (props.isCancelled) {
            statusBadge = '<span class="event-status-badge badge-cancelled">Cancelled</span>';
        }

        const intakeStrip = window.IntakeStatusIndicator
            ? window.IntakeStatusIndicator.render({ patientId: props.patientId, intakeStatus: props.intakeStatus, context: 'calendar-event' })
            : '';

        mainContent.innerHTML = `
            <div class="event-content-modern">
                <div class="event-provider">${this._escape(props.providerName)}</div>
                <div class="event-patient">${this._escape(props.patientName)}</div>
                <div class="event-meta">
                    <span class="event-type">${this._escape(props.typeName)}</span>
                </div>
                <div class="event-time">${props.timeDisplay}</div>
                ${intakeStrip}
                ${statusBadge}
            </div>
        `;
    }

    /**
     * Add status ribbon to event element
     * @private
     * @param {HTMLElement} el - Event element
     * @param {number} documentationStatus - Documentation status code
     */
    _addStatusRibbon(el, documentationStatus) {
        let ribbonText = '';
        let ribbonClass = '';

        switch (documentationStatus) {
            case 2:
            case 3: // legacy value before server restart
                ribbonText = 'Completed';
                ribbonClass = 'calendar-ribbon-completed';
                break;
            case 1:
                ribbonText = 'In Progress';
                ribbonClass = 'calendar-ribbon-in-progress';
                break;
        }

        if (ribbonText) {
            // Add class to parent for padding adjustment
            el.classList.add('has-status-ribbon');

            const ribbon = document.createElement('div');
            ribbon.className = `calendar-event-ribbon ${ribbonClass}`;
            ribbon.textContent = ribbonText;
            el.appendChild(ribbon);
        }
    }

    // === Public Methods ===

    /**
     * Refresh calendar events
     * @returns {Promise<void>}
     */
    async refresh() {
        if (this.calendar) {
            this.calendar.refetchEvents();
            this._emit('calendar:refreshed');
        }
    }

    /**
     * Set filter values
     * @param {Object} filters - Filter object
     * @param {number|null} filters.providerId - Provider ID filter
     * @param {number|null} filters.patientId - Patient ID filter
     */
    setFilters(filters) {
        this.filters = { ...this.filters, ...filters };
        this.refresh();
        this._emit('calendar:filtersChanged', { filters: this.filters });
    }

    /**
     * Clear all filters
     */
    clearFilters() {
        this.filters = { providerId: null, patientId: null };
        this.refresh();
        this._emit('calendar:filtersCleared');
    }

    /**
     * Navigate to specific date
     * @param {Date} date - Date to navigate to
     */
    gotoDate(date) {
        if (this.calendar) {
            this.calendar.gotoDate(date);
        }
    }

    /**
     * Change calendar view
     * @param {string} view - View name (dayGridMonth, dayGridWeek, timeGridDay)
     */
    changeView(view) {
        if (this.calendar) {
            this.calendar.changeView(view);
        }
    }

    /**
     * Get current date range
     * @returns {Object} Object with start and end dates
     */
    getDateRange() {
        if (!this.calendar) return null;
        const view = this.calendar.view;
        return {
            start: view.activeStart,
            end: view.activeEnd
        };
    }

    // === Helper Methods ===

    /**
     * Check if filters are active
     * @private
     * @returns {boolean}
     */
    _hasActiveFilters() {
        return this.filters.providerId != null || this.filters.patientId != null;
    }

    /**
     * Check if event matches filters
     * @private
     * @param {Object} props - Event extended props
     * @returns {boolean}
     */
    _eventMatchesFilters(props) {
        if (this.filters.providerId && props.providerId !== this.filters.providerId) {
            return false;
        }
        if (this.filters.patientId && props.patientId !== this.filters.patientId) {
            return false;
        }
        return true;
    }

    /**
     * Check if event matches type filter
     * @private
     * @param {Object} event - Calendar event
     * @returns {boolean}
     */
    _matchesTypeFilter(event) {
        if (!this.typeFilters || this.typeFilters.size === 0) return true;

        const props = event.extendedProps;

        // Check for special filters
        if (this.typeFilters.has('missed') && props.isMissed && !props.isRescheduled) {
            return true;
        }
        if (this.typeFilters.has('missedRescheduled') && props.isMissed && props.isRescheduled) {
            return true;
        }
        if (this.typeFilters.has('cancelled') && props.isCancelled) {
            return true;
        }

        // Check for type filters (0, 1, 2, 3 for appointment types)
        const typeValue = String(props.type);
        if (this.typeFilters.has(typeValue)) {
            // Only match if not already matched by special status filters
            if (!props.isMissed && !props.isCancelled) {
                return true;
            }
        }

        return false;
    }

    /**
     * Check if event matches workflow filter
     * @private
     * @param {Object} event - Calendar event
     * @returns {boolean}
     */
    _matchesWorkflowFilter(event) {
        if (!this.workflowFilters || this.workflowFilters.size === 0) return true;

        const props = event.extendedProps;

        // DocumentationStatus: 0=NotApplicable, 1=InProgress, 2=Complete
        if (this.workflowFilters.has('inProgress') && props.documentationStatus === 1) {
            return true;
        }
        if (this.workflowFilters.has('completed') && props.documentationStatus === 2) {
            return true;
        }

        return false;
    }

    /**
     * Get appointment type name
     * @private
     * @param {number} type - Type code
     * @returns {string}
     */
    _getTypeName(type) {
        const types = ['New Patient', 'Follow-Up', 'Annual Physical', 'Wellness', 'Consultation', 'Telehealth', 'Procedure', 'Urgent', 'Lab Review', 'Med Review'];
        return types[type] || 'Appointment';
    }

    /**
     * Format time for event display
     * @private
     * @param {string} dateStr - Date string
     * @param {string} tzId - Timezone ID (IANA format, e.g., 'Asia/Karachi')
     * @returns {string}
     */
    _formatEventTime(dateStr, tzId) {
        if (!dateStr) return '';
        try {
            // Use parseServerDateTime to correctly interpret server UTC times (handles missing 'Z' suffix)
            const date = window.parseServerDateTime ? window.parseServerDateTime(dateStr) : new Date(dateStr);

            // Use provided timezone, fallback to current location's timezone
            const effectiveTzId = tzId || (window.getCurrentLocationTimezone ? window.getCurrentLocationTimezone().timeZoneId : null);
            const options = {
                hour: 'numeric',
                minute: '2-digit',
                hour12: true
            };
            // TIMEZONE FIX: Use the location's timezone for formatting if provided
            if (effectiveTzId) {
                options.timeZone = effectiveTzId;
            }
            return date.toLocaleTimeString('en-US', options);
        } catch {
            return '';
        }
    }

    // === API Methods ===

    async _apiGet(url, options = {}) {
        const { showLoader = true } = options;
        if (this.api) {
            return this.api.get(url, { showLoader });
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

    _showWarning(message) {
        if (window.Toast) {
            Toast.warning('Warning', message);
        }
    }

    /**
     * Escape HTML to prevent XSS
     * @private
     * @param {string} str - String to escape
     * @returns {string} Escaped string
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

    _emit(event, data = {}) {
        if (this.eventBus) {
            this.eventBus.emit(event, data);
        }
    }

    /**
     * Destroy the module and clean up
     */
    destroy() {
        if (this.calendar) {
            this.calendar.destroy();
            this.calendar = null;
        }
        this.filters = { providerId: null, patientId: null };
        this.container = null;
        this.isInitialized = false;
    }
}

// Export for module usage
window.CalendarModule = CalendarModule;

// Auto-initialize when DOM is ready
document.addEventListener('DOMContentLoaded', function() {
    const container = document.getElementById('schedulePage');
    if (!container) return;

    const initWhenReady = () => {
        const isAuthenticated = (typeof currentUser !== 'undefined' && currentUser) ||
                               (window.App && window.App.isAuthenticated && window.App.isAuthenticated());

        if (!isAuthenticated) {
            setTimeout(initWhenReady, 200);
            return;
        }

        if (window.calendarModule) {
            window.calendarModule.init();
            return;
        }

        // Initialize calendar module
        window.calendarModule = new CalendarModule({
            api: window.apiService || (window.App && window.App.api),
            eventBus: window.eventBus || (window.App && window.App.events)
        });

        // Initialize appointment module if not already initialized
        if (!window.appointmentModule && window.AppointmentModule) {
            window.appointmentModule = new AppointmentModule({
                api: window.apiService || (window.App && window.App.api),
                eventBus: window.eventBus || (window.App && window.App.events)
            });
            window.appointmentModule.init();
        }

        // Register modules with App if available
        if (window.App && window.App.modules) {
            if (!window.App.modules.has('calendar')) {
                window.App.modules.register('calendar', window.calendarModule);
            }
            if (!window.App.modules.has('appointments') && window.appointmentModule) {
                window.App.modules.register('appointments', window.appointmentModule);
            }
        }

        window.calendarModule.init();
    };

    initWhenReady();
});
