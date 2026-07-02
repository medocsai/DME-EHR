/**
 * CareEpisodeModule - Handles care episode management
 *
 * Features:
 * - Care episode CRUD operations
 * - Patient care episodes listing
 * - Care episode details view
 * - Lifecycle management (complete, restore, extend, reauthorize)
 * - Dashboard widgets
 */
class CareEpisodeModule {
    constructor(options = {}) {
        this.api = options.api || null;
        this.eventBus = options.eventBus || null;

        // State
        this.currentEpisode = null;
        this.providers = [];

        // Confirmation callback for care episode creation from notes
        this._confirmCallback = null;
        this._confirmData = null;

        // DOM selectors
        this.selectors = {
            modal: '#careEpisodeModal',
            form: '#careEpisodeForm',
            detailsSidebar: '#careEpisodeDetailsSidebar',
            patientCareEpisodes: '#patientCareEpisodes'
        };

        // Bound handlers
        this._boundHandlers = {};
    }

    /**
     * Initialize the module
     */
    async init() {
        this._bindEvents();
    }

    /**
     * Open care episode modal for a patient
     */
    async openModal(patientId, careEpisodeId = null) {
        const modal = document.getElementById('careEpisodeModal');
        const form = document.getElementById('careEpisodeForm');
        if (!modal || !form) return;

        // Reset form
        form.reset();
        document.getElementById('careEpisodeId').value = '';
        document.getElementById('careEpisodePatientId').value = patientId;

        // Reset Re-Authorize button
        const reAuthorizeBtn = document.getElementById('reAuthorizeBtn');
        if (reAuthorizeBtn) reAuthorizeBtn.disabled = true;

        // Show/hide Save & Schedule button
        const saveAndScheduleBtn = document.getElementById('saveAndScheduleBtn');
        if (saveAndScheduleBtn) {
            saveAndScheduleBtn.classList.toggle('d-none', !!careEpisodeId);
        }

        // Load providers
        await this._loadProviders();

        if (careEpisodeId) {
            // Edit mode
            document.querySelector('#careEpisodeModal .modal-title').innerHTML =
                '<i class="bi bi-clipboard2-pulse me-2"></i>Edit Care Episode';

            try {
                const episode = await this._apiGet(`/care-episodes/${careEpisodeId}`);
                this._populateForm(episode);
            } catch (error) {
                this._showToast('Error', 'Failed to load care episode', 'error');
                return;
            }
        } else {
            // New episode - check eligibility
            document.querySelector('#careEpisodeModal .modal-title').innerHTML =
                '<i class="bi bi-clipboard2-pulse me-2"></i>New Care Episode';

            try {
                const eligibility = await this._apiGet(`/care-episodes/patient/${patientId}/eligibility`);
                if (!eligibility.CanCreateEpisode) {
                    this._showToast('Error', eligibility.Reason, 'error');
                    this._showExistingEpisodeInfo(patientId, eligibility);
                    return;
                }

                // Clear info alert
                document.getElementById('careEpisodePatientInfo')?.classList.add('d-none');

                // Pre-select today's date
                const today = this._getTodayInLocationTimezone();
                form.querySelector('[name="StartDate"]').value = today;

            } catch (error) {
                this._showToast('Error', 'Failed to check eligibility', 'error');
            }
        }

        new bootstrap.Modal(modal).show();
    }

    /**
     * Save care episode
     */
    async save() {
        const form = document.getElementById('careEpisodeForm');
        const formData = new FormData(form);
        const careEpisodeId = formData.get('CareEpisodeId');
        const patientId = parseInt(formData.get('PatientId'));

        const goals = formData.get('Goals')?.split('\n').filter(g => g.trim()) || [];

        const data = {
            PatientId: patientId,
            PrimaryProviderId: formData.get('PrimaryProviderId') ? parseInt(formData.get('PrimaryProviderId')) : null,
            StartDate: formData.get('StartDate'),
            EndDate: formData.get('EndDate') || null,
            PrimaryDiagnosisCode: formData.get('PrimaryDiagnosisCode'),
            PrimaryDiagnosisDescription: formData.get('PrimaryDiagnosisDescription'),
            PhysicianName: formData.get('PhysicianName') || null,
            ExpectedVisits: formData.get('ExpectedVisits') ? parseInt(formData.get('ExpectedVisits')) : null,
            VisitFrequency: formData.get('VisitFrequency') ? parseInt(formData.get('VisitFrequency')) : null,
            Goals: goals,
            PlanOfCare: formData.get('PlanOfCare'),
            DischargeReason: formData.get('DischargeReason')
        };

        try {
            if (careEpisodeId) {
                await this._apiPut(`/care-episodes/${careEpisodeId}`, data);
                this._showToast('Success', 'Care Episode updated successfully');
            } else {
                await this._apiPost('/care-episodes', data);
                this._showToast('Success', 'Care Episode created successfully');
            }

            this._closeModal(this.selectors.modal);
            form.reset();
            this.loadPatientEpisodes(patientId);
            this._emit('careEpisode:saved', { patientId, careEpisodeId });

        } catch (error) {
            this._showToast('Error', error.message, 'error');
        }
    }

    /**
     * Save care episode and open appointment scheduler
     */
    async saveAndSchedule() {
        const form = document.getElementById('careEpisodeForm');
        const formData = new FormData(form);
        const careEpisodeId = formData.get('CareEpisodeId');

        if (careEpisodeId) {
            this._showToast('Info', 'Use the regular Save button to update existing care episodes.', 'info');
            return;
        }

        const patientId = parseInt(formData.get('PatientId'));
        const goals = formData.get('Goals')?.split('\n').filter(g => g.trim()) || [];

        const data = {
            PatientId: patientId,
            PrimaryProviderId: formData.get('PrimaryProviderId') ? parseInt(formData.get('PrimaryProviderId')) : null,
            StartDate: formData.get('StartDate'),
            EndDate: formData.get('EndDate') || null,
            PrimaryDiagnosisCode: formData.get('PrimaryDiagnosisCode'),
            PrimaryDiagnosisDescription: formData.get('PrimaryDiagnosisDescription'),
            ExpectedVisits: formData.get('ExpectedVisits') ? parseInt(formData.get('ExpectedVisits')) : null,
            VisitFrequency: formData.get('VisitFrequency') ? parseInt(formData.get('VisitFrequency')) : null,
            Goals: goals,
            PlanOfCare: formData.get('PlanOfCare')
        };

        try {
            const newEpisode = await this._apiPost('/care-episodes', data);
            this._showToast('Success', 'Care Episode created. Opening appointment scheduler...');

            this._closeModal(this.selectors.modal);
            form.reset();
            this.loadPatientEpisodes(patientId);

            // Emit event to open appointment modal
            this._emit('careEpisode:scheduleRequested', {
                patientId,
                careEpisodeId: newEpisode.CareEpisodeId
            });

        } catch (error) {
            this._showToast('Error', error.message, 'error');
        }
    }

    /**
     * Load care episodes for a patient
     */
    async loadPatientEpisodes(patientId) {
        const container = document.getElementById('patientCareEpisodes');
        if (!container) return;

        try {
            const episodes = await this._apiGet(`/care-episodes?patientId=${patientId}`);

            if (!episodes || episodes.length === 0) {
                container.innerHTML = `
                    <div class="text-center text-muted py-3">
                        <i class="bi bi-clipboard2-x fs-1"></i>
                        <p>No Care Episodes found</p>
                        <button class="btn btn-primary btn-sm" data-action="open-care-episode" data-patient-id="${patientId}">
                            <i class="bi bi-plus-lg me-1"></i>Create Care Episode
                        </button>
                    </div>
                `;
                return;
            }

            container.innerHTML = episodes.map(ep => this._renderEpisodeCard(ep, patientId)).join('');

            // Add create button if no active episode
            const hasActiveEpisode = episodes.some(ep => ep.Status === 0 || ep.Status === 1);
            if (!hasActiveEpisode) {
                container.innerHTML += `
                    <button class="btn btn-primary w-100 mt-2" data-action="open-care-episode" data-patient-id="${patientId}">
                        <i class="bi bi-plus-lg me-1"></i>Create New Care Episode
                    </button>
                `;
            }
        } catch (error) {
            console.error('Failed to load care episodes:', error);
        }
    }

    /**
     * View care episode details in sidebar
     */
    async viewDetails(careEpisodeId) {
        try {
            const episode = await this._apiGet(`/care-episodes/${careEpisodeId}`);
            if (!episode) {
                this._showToast('Error', 'Failed to load care episode details', 'error');
                return;
            }

            this.currentEpisode = episode;
            this._populateDetailsSidebar(episode);

            // Wire up edit button
            const editBtn = document.getElementById('ceDetail_editBtn');
            if (editBtn) {
                editBtn.onclick = () => {
                    bootstrap.Offcanvas.getInstance(document.getElementById('careEpisodeDetailsSidebar'))?.hide();
                    setTimeout(() => this.openModal(episode.PatientId, careEpisodeId), 300);
                };
            }

            // Show sidebar (with backdrop disabled)
            const sidebarEl = document.getElementById('careEpisodeDetailsSidebar');
            const sidebar = bootstrap.Offcanvas.getOrCreateInstance(sidebarEl, { backdrop: false });
            sidebar.show();

        } catch (error) {
            console.error('Error loading care episode details:', error);
            this._showToast('Error', 'Failed to load care episode details', 'error');
        }
    }

    /**
     * Mark care episode as complete
     */
    async markComplete(careEpisodeId) {
        const confirmed = await this._confirm(
            'Complete Care Episode',
            'Are you sure you want to mark this care episode as complete?'
        );

        if (!confirmed) return;

        try {
            // Use POST method and send DischargeReason in body
            await this._apiPost(`/care-episodes/${careEpisodeId}/complete`, { DischargeReason: 'Treatment completed' });
            this._showToast('Success', 'Care Episode marked as complete');
            this._emit('careEpisode:completed', { careEpisodeId });

            // Refresh if sidebar is open
            if (this.currentEpisode?.CareEpisodeId === careEpisodeId) {
                this.viewDetails(careEpisodeId);
            }
        } catch (error) {
            this._showToast('Error', error.message, 'error');
        }
    }

    /**
     * Restore a discharged care episode
     */
    async restore(careEpisodeId) {
        const confirmed = await this._confirm(
            'Restore Care Episode',
            'Are you sure you want to restore this care episode?'
        );

        if (!confirmed) return;

        try {
            // Use POST method (matches controller)
            await this._apiPost(`/care-episodes/${careEpisodeId}/restore`, {});
            this._showToast('Success', 'Care Episode restored successfully');
            this._emit('careEpisode:restored', { careEpisodeId });

            // Refresh list
            if (this.currentEpisode?.PatientId) {
                this.loadPatientEpisodes(this.currentEpisode.PatientId);
            }
        } catch (error) {
            this._showToast('Error', 'Failed to restore care episode: ' + error.message, 'error');
        }
    }

    /**
     * Extend care episode end date
     */
    async extend(careEpisodeId) {
        // Show extend modal/form
        const newEndDate = prompt('Enter new end date (YYYY-MM-DD):');
        if (!newEndDate) return;

        try {
            // Use POST method (matches controller) - controller expects DateOnly directly in body
            await this._apiPost(`/care-episodes/${careEpisodeId}/extend`, newEndDate);
            this._showToast('Success', 'Care Episode extended successfully');
            this._emit('careEpisode:extended', { careEpisodeId });
        } catch (error) {
            this._showToast('Error', 'Failed to extend care episode: ' + error.message, 'error');
        }
    }

    /**
     * Request reauthorization for care episode
     */
    async reauthorize(careEpisodeId) {
        try {
            await this._apiPost(`/care-episodes/${careEpisodeId}/reauthorize`);
            this._showToast('Success', 'Re-authorization request submitted');
            this._emit('careEpisode:reauthorized', { careEpisodeId });
        } catch (error) {
            this._showToast('Error', 'Re-authorization failed: ' + error.message, 'error');
        }
    }

    /**
     * Load dashboard data
     */
    async loadDashboard() {
        try {
            const selectedLocationId = localStorage.getItem('selectedLocationId') || '';
            let params = [];

            // Check for therapist role
            const user = this._getCurrentUser();
            if (user?.Role === 2 && user?.ProviderId) {
                params.push(`providerId=${user.ProviderId}`);
            }
            if (selectedLocationId) {
                params.push(`locationId=${selectedLocationId}`);
            }

            const paramString = params.length > 0 ? '?' + params.join('&') : '';
            const dashboard = await this._apiGet('/care-episodes/dashboard' + paramString);

            // Update widget counts
            const noShowCount = document.getElementById('noShowCount');
            if (noShowCount) noShowCount.textContent = dashboard.TotalNoShowAlerts || 0;

            const missedCount = document.getElementById('missedAppointmentsCount');
            if (missedCount) missedCount.textContent = dashboard.TotalMissedAppointments || 0;

            // Update widgets
            this._updateNoShowWidget(dashboard.NoShowAlerts);
            this._updateMissedVisitsWidget(dashboard.MissedAppointments);

        } catch (error) {
            console.error('Failed to load dashboard:', error);
        }
    }

    /**
     * Clean up module resources
     */
    destroy() {
        this._unbindEvents();
        this.currentEpisode = null;
        this.providers = [];
    }

    // ========================================
    // Private Methods - Rendering
    // ========================================

    _renderEpisodeCard(episode, patientId) {
        const statusBadgeClass = this._getStatusBadgeClass(episode.Status);
        const actionButtons = episode.Status === 2
            ? `<button class="btn btn-outline-warning" data-action="restore-episode" data-episode-id="${episode.CareEpisodeId}" title="Restore">
                   <i class="bi bi-arrow-counterclockwise"></i>
               </button>`
            : `<button class="btn btn-outline-success" data-action="complete-episode" data-episode-id="${episode.CareEpisodeId}" title="Complete">
                   <i class="bi bi-check-lg"></i>
               </button>`;

        return `
            <div class="card mb-2 ${episode.IsOverdue ? 'border-warning' : ''} ${episode.HasLowVisits ? 'border-danger' : ''}">
                <div class="card-body p-2">
                    <div class="d-flex justify-content-between align-items-start">
                        <div>
                            <strong>${this._escape(episode.PrimaryDiagnosis)}</strong>
                            <span class="badge ${statusBadgeClass} ms-2">${episode.StatusName}</span>
                            ${episode.HasLowVisits ? '<span class="badge bg-danger ms-1">Low Visits</span>' : ''}
                            ${episode.IsOverdue ? '<span class="badge bg-warning text-dark ms-1">Overdue</span>' : ''}
                        </div>
                        <div class="btn-group btn-group-sm">
                            <button class="btn btn-outline-info" data-action="view-episode" data-episode-id="${episode.CareEpisodeId}" title="View Details">
                                <i class="bi bi-eye"></i>
                            </button>
                            <button class="btn btn-outline-primary" data-action="edit-episode" data-patient-id="${patientId}" data-episode-id="${episode.CareEpisodeId}" title="Edit">
                                <i class="bi bi-pencil"></i>
                            </button>
                            ${actionButtons}
                        </div>
                    </div>
                    <div class="small text-muted mt-1">
                        ${episode.StartDate} - ${episode.EndDate || 'Ongoing'} |
                        Visits: ${episode.VisitsUsed || 0} |
                        Provider: ${episode.PrimaryProviderName || 'Not assigned'}
                    </div>
                </div>
            </div>
        `;
    }

    _populateDetailsSidebar(episode) {
        const setContent = (id, content) => {
            const el = document.getElementById(id);
            if (el) el.textContent = content;
        };

        const setHtml = (id, html) => {
            const el = document.getElementById(id);
            if (el) el.innerHTML = html;
        };

        setContent('ceDetail_patient', episode.PatientName || '-');
        setContent('ceDetail_provider', episode.PrimaryProviderName || 'Not assigned');
        setContent('ceDetail_diagnosis', episode.PrimaryDiagnosisDescription || '-');
        setContent('ceDetail_diagnosisCode', episode.PrimaryDiagnosisCode || '-');

        // Status badge
        const statusNames = ['Active', 'Overdue', 'Completed', 'On Hold'];
        const statusClasses = ['bg-success', 'bg-warning text-dark', 'bg-secondary', 'bg-info'];
        setHtml('ceDetail_status',
            `<span class="badge ${statusClasses[episode.Status] || 'bg-secondary'}">${statusNames[episode.Status] || 'Unknown'}</span>`
        );

        // Dates
        setContent('ceDetail_startDate', episode.StartDate || '-');
        setContent('ceDetail_endDate', episode.EndDate || 'Ongoing');

        // Visits
        setContent('ceDetail_visits', episode.VisitsUsed || 0);

        // Treatment Plan
        setContent('ceDetail_frequency', episode.VisitFrequency ? `${episode.VisitFrequency}x per week` : '-');

        // Goals
        let goals = episode.Goals;
        if (typeof goals === 'string') {
            try { goals = JSON.parse(goals); } catch (e) { goals = goals ? [goals] : []; }
        }
        setHtml('ceDetail_goals', goals?.length
            ? '<ul class="mb-0 ps-3">' + goals.map(g => `<li>${this._escape(g)}</li>`).join('') + '</ul>'
            : '-'
        );

        setContent('ceDetail_planOfCare', episode.PlanOfCare || '-');

        // Appointments
        this._renderAppointments(episode.Appointments || []);
    }

    _renderAppointments(appointments) {
        const container = document.getElementById('ceDetail_appointmentsList');
        const countBadge = document.getElementById('ceDetail_appointmentCount');

        if (countBadge) countBadge.textContent = appointments.length;

        if (!container) return;

        if (!appointments || appointments.length === 0) {
            container.innerHTML = '<div class="text-center text-muted py-3">No appointments scheduled</div>';
            return;
        }

        const typeNames = ['Initial Eval', 'Follow Up', 'Re-Eval', 'Discharge', 'Consultation', 'Telehealth', 'Group Therapy', 'Wellness'];
        const statusNames = ['Scheduled', 'Confirmed', 'Checked In', 'In Progress', 'Completed', 'No Show', 'Cancelled', 'Rescheduled', 'Missed'];
        const statusClasses = ['bg-primary', 'bg-info', 'bg-warning text-dark', 'bg-warning text-dark', 'bg-success', 'bg-danger', 'bg-secondary', 'bg-info', 'bg-danger'];

        // Documentation status ribbon config (matches Schedule/Calendar view)
        // DocumentationStatus: 0=NotApplicable, 1=MissingNotes, 2=RequiresSignature, 3=Complete
        const documentationRibbons = {
            1: { text: 'Missing Notes', class: 'ce-ribbon-missing-notes' },
            2: { text: 'Requires Signature', class: 'ce-ribbon-requires-signature' },
            3: { text: 'Completed', class: 'ce-ribbon-completed' }
        };

        container.innerHTML = appointments.map(appt => {
            const dateStr = this._formatDate(appt.StartTime);
            const hasNotes = appt.HasNote || appt.HasNotes;
            const noteIcon = hasNotes ? '<i class="bi bi-file-text-fill text-success ms-1" title="Has clinical notes"></i>' : '';
            const intakeChip = window.IntakeStatusIndicator
                ? window.IntakeStatusIndicator.render({ patientId: appt.PatientId, intakeStatus: appt.IntakeStatus, context: 'compact' })
                : '';

            // Get documentation ribbon (only for checked-in appointments)
            const docStatus = appt.DocumentationStatus || 0;
            const ribbon = documentationRibbons[docStatus];
            const ribbonHtml = ribbon
                ? `<div class="ce-appointment-ribbon ${ribbon.class}">${ribbon.text.toUpperCase()}</div>`
                : '';

            return `
                <div class="ce-appointment-item ${ribbon ? 'has-ribbon' : ''}"
                     data-action="view-appointment"
                     data-appointment-id="${appt.AppointmentId}"
                     title="Click to view appointment details">
                    <div class="d-flex justify-content-between align-items-start p-2">
                        <div>
                            <strong>${dateStr}</strong>${noteIcon}${intakeChip}
                            <div class="small text-muted">${typeNames[appt.Type] || 'Appointment'}</div>
                        </div>
                        <span class="badge ${statusClasses[appt.Status] || 'bg-secondary'}">${statusNames[appt.Status] || 'Unknown'}</span>
                    </div>
                    ${ribbonHtml}
                </div>
            `;
        }).join('');
    }

    _updateNoShowWidget(alerts) {
        const container = document.getElementById('noShowWidget');
        if (!container) return;

        if (!alerts || alerts.length === 0) {
            container.innerHTML = '<div class="text-center text-muted py-2 small">No no-show alerts</div>';
            return;
        }

        const displayItems = alerts.slice(0, 3);
        container.innerHTML = `
            <div class="dashboard-widget-items">
                ${displayItems.map(alert => `
                    <div class="dashboard-widget-item">
                        <div class="d-flex justify-content-between align-items-start">
                            <div>
                                <div class="item-title">${this._escape(alert.PatientName)}</div>
                                <div class="item-subtitle">
                                    <span class="text-warning">${alert.MinutesOverdue} min overdue</span>
                                </div>
                            </div>
                            <button class="btn btn-sm btn-outline-success item-action"
                                    data-action="check-in-no-show" data-appointment-id="${alert.AppointmentId}" title="Check In">
                                <i class="bi bi-box-arrow-in-right"></i>
                            </button>
                        </div>
                    </div>
                `).join('')}
                ${alerts.length > 3 ? `
                    <div class="text-center mt-2">
                        <button class="btn btn-sm btn-link text-warning" data-action="view-all-no-shows">
                            View all ${alerts.length} <i class="bi bi-arrow-right"></i>
                        </button>
                    </div>
                ` : ''}
            </div>
        `;
    }

    _updateMissedVisitsWidget(alerts) {
        const container = document.getElementById('missedAppointmentsWidget');
        if (!container) return;

        if (!alerts || alerts.length === 0) {
            container.innerHTML = '<div class="text-center text-muted py-2 small">No missed appointments</div>';
            return;
        }

        container.innerHTML = alerts.slice(0, 5).map(alert => `
            <div class="dashboard-widget-item">
                <div class="d-flex justify-content-between align-items-center">
                    <div>
                        <strong>${this._escape(alert.PatientName)}</strong>
                        <div class="small text-muted">${this._formatDate(alert.ScheduledTime)}</div>
                    </div>
                </div>
            </div>
        `).join('');
    }

    _showExistingEpisodeInfo(patientId, eligibility) {
        const infoEl = document.getElementById('careEpisodePatientInfo');
        if (infoEl && eligibility.ActiveEpisode) {
            const ep = eligibility.ActiveEpisode;
            const totalVisits = ep.ExpectedVisits || ep.AuthorizedVisits || '?';
            infoEl.classList.remove('d-none');
            infoEl.innerHTML = `
                <strong>Cannot create new Care Episode</strong><br>
                Patient has an existing ${ep.StatusName} Care Episode:<br>
                <em>${ep.PrimaryDiagnosis}</em> (${ep.VisitsUsed}/${totalVisits} visits used)<br>
                <button class="btn btn-sm btn-outline-primary mt-2" data-action="edit-episode"
                        data-patient-id="${patientId}" data-episode-id="${ep.CareEpisodeId}">
                    Edit Existing Episode
                </button>
            `;
        }
    }

    _getStatusBadgeClass(status) {
        switch(status) {
            case 0: return 'bg-success'; // Active
            case 1: return 'bg-primary'; // Completed
            case 2: return 'bg-secondary'; // Discharged
            case 3: return 'bg-warning text-dark'; // On Hold
            default: return 'bg-secondary';
        }
    }

    // ========================================
    // Private Methods - Form Handling
    // ========================================

    _populateForm(episode) {
        const form = document.getElementById('careEpisodeForm');
        if (!form) return;

        document.getElementById('careEpisodeId').value = episode.CareEpisodeId;
        document.getElementById('careEpisodePatientId').value = episode.PatientId;

        const startDate = episode.StartDate ? episode.StartDate.split('T')[0] : '';
        form.querySelector('[name="StartDate"]').value = startDate;

        if (episode.PrimaryProviderId) {
            form.querySelector('[name="PrimaryProviderId"]').value = episode.PrimaryProviderId;
        }

        form.querySelector('[name="PrimaryDiagnosisCode"]').value = episode.PrimaryDiagnosisCode || '';
        form.querySelector('[name="PrimaryDiagnosisDescription"]').value = episode.PrimaryDiagnosisDescription || '';
        form.querySelector('[name="PhysicianName"]').value = episode.PhysicianName || '';
        form.querySelector('[name="ExpectedVisits"]').value = episode.ExpectedVisits || '';
        form.querySelector('[name="VisitFrequency"]').value = episode.VisitFrequency || '';

        // Parse goals
        let goals = episode.Goals || '';
        try {
            const parsed = JSON.parse(goals);
            if (Array.isArray(parsed)) goals = parsed.join('\n');
        } catch (e) {}
        form.querySelector('[name="Goals"]').value = goals;

        form.querySelector('[name="PlanOfCare"]').value = episode.PlanOfCare || '';
    }

    async _loadProviders() {
        try {
            const providers = await this._apiGet('/providers/dropdown');
            this.providers = providers || [];
            const select = document.getElementById('careEpisodeProvider');
            if (select) {
                select.innerHTML = '<option value="">Select provider...</option>' +
                    this.providers.map(p => `<option value="${p.ProviderId}">${p.DisplayName}</option>`).join('');
            }
        } catch (error) {
            console.error('Failed to load providers:', error);
        }
    }

    // ========================================
    // Private Methods - Event Handling
    // ========================================

    _bindEvents() {
        this._boundHandlers.docClick = (e) => this._handleDocumentClick(e);
        document.addEventListener('click', this._boundHandlers.docClick);
    }

    _unbindEvents() {
        if (this._boundHandlers.docClick) {
            document.removeEventListener('click', this._boundHandlers.docClick);
        }
    }

    _handleDocumentClick(e) {
        const target = e.target.closest('[data-action]');
        if (!target) return;

        const action = target.getAttribute('data-action');
        const episodeId = target.getAttribute('data-episode-id');
        const patientId = target.getAttribute('data-patient-id');
        const appointmentId = target.getAttribute('data-appointment-id');

        switch (action) {
            case 'open-care-episode':
                this.openModal(parseInt(patientId));
                break;
            case 'view-episode':
                this.viewDetails(parseInt(episodeId));
                break;
            case 'edit-episode':
                this.openModal(parseInt(patientId), parseInt(episodeId));
                break;
            case 'complete-episode':
                this.markComplete(parseInt(episodeId));
                break;
            case 'restore-episode':
                this.restore(parseInt(episodeId));
                break;
            case 'view-appointment':
                this._viewAppointment(parseInt(appointmentId));
                break;
        }
    }

    /**
     * View appointment details - delegates to appointment details function
     */
    _viewAppointment(appointmentId) {
        console.log('[CareEpisodeModule] Opening appointment details for ID:', appointmentId);

        // Primary method: use global openAppointmentDetails (same as CalendarModule)
        if (typeof openAppointmentDetails === 'function') {
            openAppointmentDetails(appointmentId);
            return;
        }

        // Fallback: use module directly
        const appointmentModule = window.App?.modules?.get('appointments') || window.appointmentModule;
        if (appointmentModule && typeof appointmentModule.openDetails === 'function') {
            appointmentModule.openDetails(appointmentId);
            return;
        }

        // Last resort: emit event for other modules to handle
        this._emit('appointment:view', { appointmentId });
        console.warn('[CareEpisodeModule] No appointment handler found, emitted event');
    }

    // ========================================
    // Private Methods - API
    // ========================================

    async _apiGet(endpoint) {
        if (this.api) {
            return await this.api.get(endpoint);
        }
        return await window.apiRequest(endpoint);
    }

    async _apiPost(endpoint, data) {
        if (this.api) {
            return await this.api.post(endpoint, data);
        }
        return await window.apiRequest(endpoint, { method: 'POST', body: JSON.stringify(data) });
    }

    async _apiPut(endpoint, data) {
        if (this.api) {
            return await this.api.put(endpoint, data);
        }
        return await window.apiRequest(endpoint, { method: 'PUT', body: JSON.stringify(data) });
    }

    // ========================================
    // Private Methods - Utilities
    // ========================================

    _escape(str) {
        if (str === null || str === undefined) return '';
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }

    _formatDate(dateStr) {
        if (!dateStr) return '-';
        const date = window.parseServerDateTime ? window.parseServerDateTime(dateStr) : new Date(dateStr);
        if (!date || isNaN(date.getTime())) return '-';
        return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
    }

    _getTodayInLocationTimezone() {
        const now = new Date();
        return now.toISOString().split('T')[0];
    }

    _getCurrentUser() {
        return window.currentUser || null;
    }

    _showToast(title, message, type = 'success') {
        if (window.Toast) {
            window.Toast.show(title, message, type);
        } else if (window.showToast) {
            window.showToast(title, message, type);
        }
    }

    _closeModal(selector) {
        const modal = document.querySelector(selector);
        if (modal) {
            const bsModal = bootstrap.Modal.getInstance(modal);
            if (bsModal) bsModal.hide();
        }
    }

    async _confirm(title, message) {
        if (window.ConfirmDialog) {
            return await window.ConfirmDialog.show({ title, message });
        }
        return confirm(message);
    }

    _emit(event, data) {
        if (this.eventBus) {
            this.eventBus.emit(event, data);
        }
    }
}

// Export for module usage
if (typeof module !== 'undefined' && module.exports) {
    module.exports = CareEpisodeModule;
}

// Export to window for browser usage
window.CareEpisodeModule = CareEpisodeModule;
