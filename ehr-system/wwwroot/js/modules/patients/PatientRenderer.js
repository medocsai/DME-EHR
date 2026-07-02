/**
 * PatientRenderer - Handles all patient data rendering and display logic
 */

class PatientRenderer {
    constructor(options = {}) {
        this.parentModule = options.parentModule;
        this.utilities = options.utilities || PatientUtilities;
    }

    /**
     * Render all patients in the patient table
     * @param {Array} patients - Array of patient objects
     * @param {HTMLElement} tableBody - Target element
     */
    render(patients, tableBody) {
        if (!tableBody) return;

        if (!patients?.length) {
            tableBody.innerHTML = this.renderEmptyState();
            return;
        }

        const currentUser = this.utilities.getCurrentUser();
        const isTherapist = currentUser?.Role === 2;

        tableBody.innerHTML = patients.map(patient => this.renderPatientRow(patient, isTherapist)).join('');
    }

    /**
     * Render a single patient row
     * @param {Object} patient - Patient data
     * @param {boolean} isTherapist - Whether current user is therapist
     * @returns {string} HTML
     */
    renderPatientRow(patient, isTherapist) {
        const validationBadge = this.getValidationBadge(patient);
        const statusBadge = this.getStatusBadge(patient.Status);
        const profileBadge = this.getProfileBadge(patient);
        const isArchived = patient.IsArchived;
        // Handle both list API (PrimaryInsurance as string) and detail API (Insurances array)
        const primaryInsuranceName = patient.PrimaryInsurance ||
            patient.Insurances?.find(i => i.Type === 0)?.PayerName;

        return `
            <tr class="${isArchived ? 'table-secondary' : ''}">
                <td>
                    <span class="text-primary fw-medium">${this.utilities.escape(patient.MRN)}</span>
                </td>
                <td>
                    <div class="d-flex align-items-center gap-2">
                        <div class="patient-list-avatar"
                             data-action="zoom-avatar"
                             data-patient-id="${patient.PatientId}"
                             data-patient-name="${this.utilities.escape(patient.FullName)}"
                             data-has-photo="${!!patient.HasProfilePicture}"
                             title="Click to enlarge">
                            ${AvatarUtils.renderPatientAvatar({ patientId: patient.PatientId, name: patient.FullName, hasProfilePicture: patient.HasProfilePicture, size: 'sm' })}
                        </div>
                        <div>
                            <a href="#" class="text-decoration-none fw-bold"
                               data-action="view"
                               data-patient-id="${patient.PatientId}">
                                ${this.utilities.escape(patient.FullName)}
                            </a>
                            ${isArchived ? '<span class="badge bg-secondary ms-1">Archived</span>' : ''}
                        </div>
                    </div>
                </td>
                <td>${this.utilities.formatDate(patient.DateOfBirth)} (${patient.Age}y)</td>
                <td>${this.utilities.escape(patient.Phone || '-')}</td>
                <td>${primaryInsuranceName ? this.utilities.escape(primaryInsuranceName) : '<span class="badge bg-secondary">No Insurance</span>'}</td>
                <td>${statusBadge}</td>
                <td class="hide-from-therapist hide-from-biller hide-from-ma-nurse">${profileBadge}</td>
                <td>${(patient.LastVisit || patient.LastVisitDate) ? this.utilities.formatDate(patient.LastVisit || patient.LastVisitDate) : '-'}</td>
                <td>
                    <div class="btn-group btn-group-sm">
                        <button class="btn btn-outline-info"
                                data-action="view"
                                data-patient-id="${patient.PatientId}"
                                title="View">
                            <i class="bi bi-eye"></i>
                        </button>
                        ${!isTherapist && !isArchived ? `
                            <button class="btn btn-outline-primary"
                                    data-action="edit"
                                    data-patient-id="${patient.PatientId}"
                                    title="Edit">
                                <i class="bi bi-pencil"></i>
                            </button>
                        ` : ''}
                        ${!isTherapist ? `
                            ${isArchived ? `
                                <button class="btn btn-outline-success"
                                        data-action="unarchive"
                                        data-patient-id="${patient.PatientId}"
                                        data-patient-name="${this.utilities.escape(patient.FullName)}"
                                        title="Unarchive">
                                    <i class="bi bi-arrow-counterclockwise"></i>
                                </button>
                            ` : `
                                <button class="btn btn-outline-secondary"
                                        data-action="archive"
                                        data-patient-id="${patient.PatientId}"
                                        data-patient-name="${this.utilities.escape(patient.FullName)}"
                                        title="Archive">
                                    <i class="bi bi-archive"></i>
                                </button>
                            `}
                        ` : ''}
                    </div>
                </td>
            </tr>
        `;
    }

    /**
     * Render empty state when no patients
     * @returns {string} HTML
     */
    renderEmptyState() {
        return `
            <tr>
                <td colspan="9" class="text-center text-muted py-5">
                    <i class="bi bi-people fs-1 d-block mb-2"></i>
                    No patients found matching your criteria
                </td>
            </tr>
        `;
    }

    /**
     * Render error state
     * @param {string} message - Error message
     * @param {HTMLElement} tableBody - Target element
     */
    renderError(message, tableBody) {
        if (!tableBody) return;

        tableBody.innerHTML = `
            <tr>
                <td colspan="9" class="text-center text-danger py-4">
                    <i class="bi bi-exclamation-triangle me-2"></i>
                    ${this.utilities.escape(message)}
                    <button class="btn btn-sm btn-outline-danger ms-2" onclick="App.modules.get('patients').load()">
                        Retry
                    </button>
                </td>
            </tr>
        `;
    }

    /**
     * Render patient detail modal
     * @param {Object} patient - Patient data
     * @param {Function} showModalFn - Function to show modal
     * @param {Function} loadDocumentsFn - Function to load documents
     */
    renderDetailModal(patient, showModalFn, loadDocumentsFn) {
        const content = document.getElementById('patientDetailContent');
        if (!content) return;

        const primaryInsurance = patient.Insurances?.find(i => i.Type === 0);
        const secondaryInsurance = patient.Insurances?.find(i => i.Type === 1);

        // Check if user has edit permissions (Super Admin=0, Clinic Admin=1, Clinician=2, Front Desk=3 can edit)
        const currentUser = this.utilities.getCurrentUser();
        const canEdit = currentUser && (currentUser.Role === 0 || currentUser.Role === 1 || currentUser.Role === 2 || currentUser.Role === 3);

        content.innerHTML = `
            <div class="pp-body">
                <aside class="pp-sidebar">
                    <div class="pp-identity">
                        <div class="pp-identity-row">
                            ${AvatarUtils.renderPatientAvatar({ patientId: patient.PatientId, name: patient.FullName, hasProfilePicture: patient.HasProfilePicture, size: 'xl' })}
                            <div class="pp-identity-info">
                                <h3>${this.utilities.escape(patient.FullName)}</h3>
                                <div class="pp-identity-badges">
                                    <span class="pp-mrn-badge">${this.utilities.escape(patient.MRN)}</span>
                                    ${this.getStatusBadge(patient.Status)}
                                </div>
                            </div>
                        </div>
                        <div class="pp-meta">
                            <div class="pp-meta-row"><i class="bi bi-calendar"></i><span>${this.utilities.formatDate(patient.DateOfBirth)} (${patient.Age}y)</span></div>
                            <div class="pp-meta-row"><i class="bi bi-telephone"></i><span class="${patient.Phone ? '' : 'pp-muted'}">${this.utilities.escape(patient.Phone || 'No phone')}</span></div>
                            <div class="pp-meta-row"><i class="bi bi-envelope"></i><span class="pp-overflow ${patient.Email ? '' : 'pp-muted'}">${this.utilities.escape(patient.Email || 'No email')}</span></div>
                        </div>
                    </div>

                    <ul class="nav nav-tabs" role="tablist">
                        <li class="pp-nav-label">Clinical</li>
                        <li class="nav-item">
                            <a class="nav-link active" data-bs-toggle="tab" href="#patientOverview"><i class="bi bi-grid-1x2"></i>Overview</a>
                        </li>
                        <li class="nav-item">
                            <a class="nav-link" data-bs-toggle="tab" href="#patientIntake"><i class="bi bi-clipboard-check"></i>Patient Intake<span id="patientIntakeStatusBadge"></span></a>
                        </li>
                        <li class="nav-item">
                            <a class="nav-link" data-bs-toggle="tab" href="#clinicalProblems"><i class="bi bi-list-check"></i>Problems</a>
                        </li>
                        <li class="nav-item">
                            <a class="nav-link" data-bs-toggle="tab" href="#clinicalMedications"><i class="bi bi-capsule"></i>Medications</a>
                        </li>
                        <li class="nav-item">
                            <a class="nav-link" data-bs-toggle="tab" href="#clinicalAllergies"><i class="bi bi-exclamation-triangle"></i>Allergies</a>
                        </li>
                        <li class="nav-item">
                            <a class="nav-link" data-bs-toggle="tab" href="#clinicalVitals"><i class="bi bi-activity"></i>Vitals</a>
                        </li>
                        <li class="nav-item">
                            <a class="nav-link" data-bs-toggle="tab" href="#clinicalImmunizations"><i class="bi bi-shield-check"></i>Immunizations</a>
                        </li>
                        <li class="nav-item">
                            <a class="nav-link" data-bs-toggle="tab" href="#clinicalHistory"><i class="bi bi-people"></i>History</a>
                        </li>

                        <li class="pp-nav-label">Administrative</li>
                        <li class="nav-item">
                            <a class="nav-link" data-bs-toggle="tab" href="#patientEncounters"><i class="bi bi-journal-medical"></i>Encounters</a>
                        </li>
                        <li class="nav-item">
                            <a class="nav-link" data-bs-toggle="tab" href="#patientCareNotes"><i class="bi bi-journal-text"></i>Care Notes</a>
                        </li>
                        <li class="nav-item">
                            <a class="nav-link" data-bs-toggle="tab" href="#patientConversations"><i class="bi bi-chat-dots"></i>Conversations</a>
                        </li>
                        <li class="nav-item">
                            <!-- COMING SOON: restore data-bs-toggle="tab" and remove nav-coming-soon to re-enable -->
                            <a class="nav-link nav-coming-soon" href="#patientPrescriptions"><i class="bi bi-prescription2"></i>Prescriptions</a>
                        </li>
                        <li class="nav-item">
                            <a class="nav-link" data-bs-toggle="tab" href="#patientOrders"><i class="bi bi-clipboard2-pulse"></i>Orders</a>
                        </li>
                        <li class="nav-item">
                            <a class="nav-link" data-bs-toggle="tab" href="#patientAttachments"><i class="bi bi-paperclip"></i>Attachments</a>
                        </li>
                        <li class="nav-item">
                            <a class="nav-link" data-bs-toggle="tab" href="#patientConsents"><i class="bi bi-file-earmark-check"></i>Consent</a>
                        </li>
                        <li class="nav-item">
                            <a class="nav-link" data-bs-toggle="tab" href="#patientAppointmentsNotes"><i class="bi bi-calendar-check"></i>Appt / Notes</a>
                        </li>
                        <li class="nav-item">
                            <a class="nav-link" data-bs-toggle="tab" href="#patientFinance"><i class="bi bi-wallet2"></i>Finance</a>
                        </li>
                        <li class="nav-item">
                            <a class="nav-link" data-bs-toggle="tab" href="#patientInsurances"><i class="bi bi-hospital"></i>Insurance</a>
                        </li>
                    </ul>
                </aside>

                <div class="pp-content">
                    <div class="tab-content">
                <div class="tab-pane fade show active" id="patientOverview">
                    ${this.renderOverviewTab(patient)}
                </div>
                <div class="tab-pane fade" id="patientIntake">
                    <div id="patientIntakeContent"><div class="text-center p-3"><div class="spinner-border spinner-border-sm"></div> Loading...</div></div>
                </div>
                <div class="tab-pane fade" id="clinicalProblems">
                    <div id="clinicalProblemsContent"><div class="text-center p-3"><div class="spinner-border spinner-border-sm"></div> Loading...</div></div>
                </div>
                <div class="tab-pane fade" id="clinicalMedications">
                    <div id="clinicalMedicationsContent"><div class="text-center p-3"><div class="spinner-border spinner-border-sm"></div> Loading...</div></div>
                </div>
                <div class="tab-pane fade" id="clinicalAllergies">
                    <div id="clinicalAllergiesContent"><div class="text-center p-3"><div class="spinner-border spinner-border-sm"></div> Loading...</div></div>
                </div>
                <div class="tab-pane fade" id="clinicalVitals">
                    <div id="clinicalVitalsContent"><div class="text-center p-3"><div class="spinner-border spinner-border-sm"></div> Loading...</div></div>
                </div>
                <div class="tab-pane fade" id="clinicalImmunizations">
                    <div id="clinicalImmunizationsContent"><div class="text-center p-3"><div class="spinner-border spinner-border-sm"></div> Loading...</div></div>
                </div>
                <div class="tab-pane fade" id="clinicalHistory">
                    <div id="clinicalHistoryContent"><div class="text-center p-3"><div class="spinner-border spinner-border-sm"></div> Loading...</div></div>
                </div>
                <div class="tab-pane fade" id="patientInsurances">
                    ${this.renderInsuranceTab(patient, primaryInsurance, secondaryInsurance)}
                </div>
                <div class="tab-pane fade" id="patientEncounters">
                    <div id="patientEncountersContent"><div class="text-center p-3"><div class="spinner-border spinner-border-sm"></div> Loading...</div></div>
                </div>
                <div class="tab-pane fade" id="patientCareNotes">
                    <div id="patientCareNotesContent"><div class="text-center p-3"><div class="spinner-border spinner-border-sm"></div> Loading...</div></div>
                </div>
                <div class="tab-pane fade" id="patientConversations">
                    <div id="patientConversationsContent"><div class="text-center p-3"><div class="spinner-border spinner-border-sm"></div> Loading...</div></div>
                </div>
                <div class="tab-pane fade" id="patientPrescriptions">
                    <div id="patientPrescriptionsContent"><div class="text-center p-3"><div class="spinner-border spinner-border-sm"></div> Loading...</div></div>
                </div>
                <div class="tab-pane fade" id="patientOrders">
                    <div id="patientOrdersContent"><div class="text-center p-3"><div class="spinner-border spinner-border-sm"></div> Loading...</div></div>
                </div>
                <div class="tab-pane fade" id="patientAttachments">
                    ${this.renderAttachmentsTab(patient)}
                </div>
                <div class="tab-pane fade" id="patientConsents">
                    ${this.renderConsentsTab(patient)}
                </div>
                <div class="tab-pane fade" id="patientAppointmentsNotes">
                    ${this.renderAppointmentsNotesTab(patient)}
                </div>
                <div class="tab-pane fade" id="patientFinance">
                    <div id="patientFinanceContent"><div class="text-center p-3"><div class="spinner-border spinner-border-sm"></div> Loading...</div></div>
                </div>
            </div>
                </div><!-- /.pp-content -->
            </div><!-- /.pp-body -->
        `;

        // Initialize clinical manager with patient ID
        if (window.patientClinicalManager) {
            window.patientClinicalManager.initialize(patient.PatientId);
        }

        // Set the patient ID hidden field for other modules to access
        const patientIdField = document.getElementById('patientId');
        if (patientIdField) {
            patientIdField.value = patient.PatientId;
        }

        // Patient Intake: footer QR button removed in Phase 4.
        // The QR trigger now lives inside the "Patient Intake" tab header (see ProfileIntakeAccordion).
        const modalFooter = document.querySelector('#patientDetailModal .modal-footer');
        if (modalFooter) {
            const existingIntakeBtn = modalFooter.querySelector('#openPatientIntakeQrBtn');
            if (existingIntakeBtn) existingIntakeBtn.remove();
        }

        // Lazy-load the Patient Intake accordion when its tab is first shown.
        const intakeTabLink = document.querySelector('#patientDetailModal a[href="#patientIntake"]');
        const intakeContent = document.getElementById('patientIntakeContent');
        if (intakeTabLink && intakeContent) {
            let loaded = false;
            const loadIntake = () => {
                if (loaded) return;
                loaded = true;
                if (window.ProfileIntakeAccordion && typeof window.ProfileIntakeAccordion.render === 'function') {
                    window.ProfileIntakeAccordion.render(intakeContent, patient.PatientId);
                } else {
                    intakeContent.innerHTML = '<div class="alert alert-warning m-3">Intake module not loaded. Please refresh the page.</div>';
                }
            };
            intakeTabLink.addEventListener('shown.bs.tab', loadIntake);
        }

        // Patient Intake status label in sidebar nav-link.
        // Fetch progress and render via IntakeStatusIndicator (sidebar-label context).
        // Spec: rules/technical/intake-status-indicator.md
        const intakeStatusBadge = document.getElementById('patientIntakeStatusBadge');
        if (intakeStatusBadge && window.IntakeStatusIndicator && patient && patient.PatientId) {
            const token = localStorage.getItem('authToken');
            const headers = token ? { 'Authorization': `Bearer ${token}` } : {};
            fetch(`/api/clinic/patients/${patient.PatientId}/intake/progress`, {
                credentials: 'same-origin',
                headers: headers
            })
                .then(r => r.ok ? r.json() : null)
                .then(p => {
                    if (!p) return;
                    const submittedAt = p.SubmittedAt || p.submittedAt;
                    const currentSubmissionId = p.CurrentSubmissionId || p.currentSubmissionId;
                    const status = submittedAt
                        ? window.IntakeStatusIndicator.STATE.SUBMITTED
                        : (currentSubmissionId ? window.IntakeStatusIndicator.STATE.IN_PROGRESS : window.IntakeStatusIndicator.STATE.NOT_SUBMITTED);
                    intakeStatusBadge.innerHTML = window.IntakeStatusIndicator.render({
                        patientId: patient.PatientId,
                        intakeStatus: { Status: status, SubmittedAt: submittedAt },
                        context: 'sidebar-label'
                    });
                })
                .catch(() => { /* silent fail, label just stays empty */ });
        }

        // Add Edit button to modal footer if user has permission
        if (modalFooter && canEdit) {
            // Remove any existing Edit button first
            const existingEditBtn = modalFooter.querySelector('#editPatientFromViewBtn');
            if (existingEditBtn) {
                existingEditBtn.remove();
            }

            // Create Edit button and add it to the right of Close button
            const editBtn = document.createElement('button');
            editBtn.type = 'button';
            editBtn.className = 'btn btn-primary';
            editBtn.id = 'editPatientFromViewBtn';
            editBtn.dataset.patientId = patient.PatientId;
            editBtn.title = 'Edit Patient';
            editBtn.innerHTML = '<i class="bi bi-pencil-square me-1"></i>Edit';
            editBtn.addEventListener('click', () => {
                this._handleEditFromView(patient.PatientId, content);
            });

            // Insert after the Close button (at the end of footer)
            modalFooter.appendChild(editBtn);
        }

        if (showModalFn) {
            showModalFn('patientDetailModal');
        }

        // When the user clicks any tab in the left sidebar (Overview / Problems /
        // Medications / ... / Appt / Notes / Finance / Insurance), reset the
        // scroll of the right content pane back to the top. Otherwise clicking
        // a new tab leaves the content area scrolled from wherever the previous
        // tab was, and the user sees a middle/bottom chunk instead of the top.
        content.querySelectorAll('a.nav-link[data-bs-toggle="tab"]').forEach(link => {
            link.addEventListener('shown.bs.tab', (e) => {
                const href = e.target.getAttribute('href');
                const pane = href ? document.querySelector(href) : null;
                // Scroll the pane itself (if it's the scroll container),
                // plus its .tab-content ancestor, plus the modal body.
                // Whichever of the three is actually scrollable will move to top.
                if (pane) pane.scrollTop = 0;
                const tabContent = pane?.closest('.tab-content');
                if (tabContent) tabContent.scrollTop = 0;
                const modalBody = document.querySelector('#patientDetailModal .modal-body');
                if (modalBody) modalBody.scrollTop = 0;
            });
        });

        // Add event listener to load consent history when Consent tab is clicked
        const consentsTab = content.querySelector('a[href="#patientConsents"]');
        if (consentsTab) {
            const loadConsentsFn = () => {
                if (window.consentModule) {
                    window.consentModule.loadPatientHistory(patient.PatientId);
                }
            };
            consentsTab.addEventListener('shown.bs.tab', loadConsentsFn);
            consentsTab.addEventListener('click', () => setTimeout(loadConsentsFn, 100));
        }

        // Add event listener to load documents when Attachments tab is clicked
        const attachmentsTab = content.querySelector('a[href="#patientAttachments"]');
        if (attachmentsTab && loadDocumentsFn) {
            attachmentsTab.addEventListener('shown.bs.tab', () => {
                loadDocumentsFn(patient.PatientId);
            });

            // Also handle click for browsers that don't fire shown.bs.tab
            attachmentsTab.addEventListener('click', () => {
                // Small delay to let tab become visible
                setTimeout(() => {
                    loadDocumentsFn(patient.PatientId);
                }, 100);
            });
        }

        // Add event listeners to load clinical tabs when clicked
        const clinicalTabs = [
            { href: '#clinicalProblems', loader: () => window.patientClinicalManager?.loadProblems(patient.PatientId) },
            { href: '#clinicalMedications', loader: () => window.patientClinicalManager?.loadMedications(patient.PatientId) },
            { href: '#clinicalAllergies', loader: () => window.patientClinicalManager?.loadAllergies(patient.PatientId) },
            { href: '#clinicalVitals', loader: () => window.patientClinicalManager?.loadVitals(patient.PatientId) },
            { href: '#clinicalImmunizations', loader: () => window.patientClinicalManager?.loadImmunizations(patient.PatientId) },
            { href: '#clinicalHistory', loader: () => window.patientClinicalManager?.loadHistory(patient.PatientId) },
            { href: '#patientEncounters', loader: () => window.patientClinicalManager?.loadEncounters(patient.PatientId) },
            { href: '#patientPrescriptions', loader: () => window.patientClinicalManager?.loadPrescriptions(patient.PatientId) },
            { href: '#patientOrders', loader: () => window.patientClinicalManager?.loadOrders(patient.PatientId) }
        ];

        clinicalTabs.forEach(({ href, loader }) => {
            const tab = content.querySelector(`a[href="${href}"]`);
            if (tab) {
                tab.addEventListener('shown.bs.tab', loader);
                tab.addEventListener('click', () => setTimeout(loader, 100));
            }
        });

        // Add event listener to load Appointments/Notes data when tab is clicked
        const apptNotesTab = content.querySelector('a[href="#patientAppointmentsNotes"]');
        if (apptNotesTab) {
            const loadApptNotesFn = () => {
                console.log('[PatientRenderer] Appointments/Notes tab activated for patient:', patient.PatientId);
                if (window.patientAppointmentsNotesModule) {
                    window.patientAppointmentsNotesModule.initialize(patient.PatientId);
                } else {
                    console.warn('[PatientRenderer] window.patientAppointmentsNotesModule not available');
                }
            };

            apptNotesTab.addEventListener('shown.bs.tab', loadApptNotesFn);

            // Also handle click for browsers that don't fire shown.bs.tab
            apptNotesTab.addEventListener('click', () => {
                setTimeout(loadApptNotesFn, 100);
            });
        }

        // Add event listener to load Finance tab when clicked
        const financeTab = content.querySelector('a[href="#patientFinance"]');
        if (financeTab) {
            const loadFinanceFn = () => this._loadPatientFinance(patient);
            financeTab.addEventListener('shown.bs.tab', loadFinanceFn);
            financeTab.addEventListener('click', () => setTimeout(loadFinanceFn, 100));
        }

        // Care Notes tab — delegate to CareNotesModule.
        // GET on /patients/{id}/care-notes also auto-marks unseen notes seen
        // for the calling clinician (server-side behavior in CareNotesController).
        const careNotesTab = content.querySelector('a[href="#patientCareNotes"]');
        if (careNotesTab) {
            const loadCareNotesFn = () => {
                if (window.careNotesModule) {
                    window.careNotesModule.loadPatientTab(patient.PatientId);
                } else {
                    console.warn('[PatientRenderer] careNotesModule not available');
                }
            };
            careNotesTab.addEventListener('shown.bs.tab', loadCareNotesFn);
            careNotesTab.addEventListener('click', () => setTimeout(loadCareNotesFn, 100));
        }

        // Conversations tab — delegate to PatientConversationsModule.
        const conversationsTab = content.querySelector('a[href="#patientConversations"]');
        if (conversationsTab) {
            const loadConvFn = () => {
                if (window.patientConversationsModule) {
                    window.patientConversationsModule.loadPatientTab(patient.PatientId);
                } else {
                    console.warn('[PatientRenderer] patientConversationsModule not available');
                }
            };
            conversationsTab.addEventListener('shown.bs.tab', loadConvFn);
            conversationsTab.addEventListener('click', () => setTimeout(loadConvFn, 100));
        }

        // Listen for payment:created to refresh finance tab if active
        window.addEventListener('payment:created', () => {
            const financePane = document.getElementById('patientFinance');
            if (financePane && financePane.classList.contains('active')) {
                this._loadPatientFinance(patient);
            }
        });

        // Load rich eligibility details for insurance tab (async, fills placeholders)
        this._loadProfileEligibilityDetails(patient);
    }

    /**
     * Handle Edit button click from View Patient modal
     * Preserves the current tab and opens Edit modal on the same tab
     * @param {number} patientId - Patient ID
     * @param {HTMLElement} content - Modal content element
     * @private
     */
    _handleEditFromView(patientId, content) {
        // Get the currently active tab from View modal
        const activeTab = content.querySelector('.nav-link.active');
        let activeTabName = 'basic-info'; // Default tab

        if (activeTab) {
            const href = activeTab.getAttribute('href');
            // Map view modal tab IDs to edit modal tab names
            const tabMapping = {
                '#patientOverview': 'basic-info',
                '#patientInsurances': 'insurance',
                '#patientEpisodes': 'basic-info', // Care Episodes not in edit modal, default to basic
                '#patientAttachments': 'attachments',
                '#patientConsents': 'consent',
                '#patientMedicalLien': 'basic-info', // No direct mapping, default to basic
                '#patientAppointmentsNotes': 'basic-info' // No direct mapping, default to basic
            };
            activeTabName = tabMapping[href] || 'basic-info';
        }

        // Close View Patient modal
        const viewModal = document.getElementById('patientDetailModal');
        if (viewModal) {
            const bsViewModal = bootstrap.Modal.getInstance(viewModal);
            if (bsViewModal) {
                bsViewModal.hide();
            }
        }

        // Wait for view modal to close, then open edit modal
        setTimeout(() => {
            // Call the global function to edit patient with tab context
            if (window.patientModule) {
                window.patientModule.edit(patientId, activeTabName);
            } else if (typeof editPatient === 'function') {
                editPatient(patientId, activeTabName);
            }
        }, 300);
    }

    /**
     * Render overview tab content
     * @param {Object} patient - Patient data
     * @returns {string} HTML
     */
    renderOverviewTab(patient) {
        const esc = (v) => this.utilities.escape(v);
        const fmt = (v) => v ? `<span class="pp-info-value">${esc(v)}</span>` : `<span class="pp-info-value pp-empty">—</span>`;

        return `
            <div class="pp-card">
                <div class="pp-card-header"><i class="bi bi-person-badge"></i>Basic Information</div>
                <div class="pp-card-body">
                    <div class="pp-info-grid">
                        <div class="pp-info-field">
                            <div class="pp-info-label">Date of Birth</div>
                            <div class="pp-info-value">${this.utilities.formatDate(patient.DateOfBirth)}<span class="pp-sub">(${patient.Age}y)</span></div>
                        </div>
                        <div class="pp-info-field">
                            <div class="pp-info-label">Email</div>
                            ${fmt(patient.Email)}
                        </div>
                        <div class="pp-info-field">
                            <div class="pp-info-label">Gender</div>
                            ${fmt(patient.Gender)}
                        </div>
                        <div class="pp-info-field">
                            <div class="pp-info-label">SSN</div>
                            ${fmt(patient.Ssn)}
                        </div>
                        <div class="pp-info-field">
                            <div class="pp-info-label">Phone</div>
                            ${fmt(patient.Phone)}
                        </div>
                        <div class="pp-info-field">
                            <div class="pp-info-label">MRN</div>
                            <div class="pp-info-value"><span class="pp-mono">${esc(patient.MRN)}</span></div>
                        </div>
                    </div>
                </div>
            </div>

            <div class="pp-card">
                <div class="pp-card-header"><i class="bi bi-geo-alt"></i>Address</div>
                <div class="pp-card-body">
                    <div class="pp-info-grid" style="grid-template-columns: 2fr 1fr 1fr 1fr;">
                        <div class="pp-info-field">
                            <div class="pp-info-label">Street</div>
                            ${fmt(patient.Address)}
                        </div>
                        <div class="pp-info-field">
                            <div class="pp-info-label">City</div>
                            ${fmt(patient.City)}
                        </div>
                        <div class="pp-info-field">
                            <div class="pp-info-label">State</div>
                            ${fmt(patient.State)}
                        </div>
                        <div class="pp-info-field">
                            <div class="pp-info-label">ZIP</div>
                            ${fmt(patient.ZipCode)}
                        </div>
                    </div>
                </div>
            </div>

            <div class="pp-card">
                <div class="pp-card-header"><i class="bi bi-person-lines-fill"></i>Emergency Contact</div>
                <div class="pp-card-body">
                    <div class="pp-info-grid">
                        <div class="pp-info-field">
                            <div class="pp-info-label">Name</div>
                            ${fmt(patient.EmergencyContactName)}
                        </div>
                        <div class="pp-info-field">
                            <div class="pp-info-label">Phone</div>
                            ${fmt(patient.EmergencyContactPhone)}
                        </div>
                        <div class="pp-info-field">
                            <div class="pp-info-label">Relationship</div>
                            ${fmt(patient.EmergencyContactRelation)}
                        </div>
                        <div class="pp-info-field">
                            <div class="pp-info-label">Alternate Phone</div>
                            ${fmt(patient.EmergencyContactAltPhone)}
                        </div>
                    </div>
                </div>
            </div>
        `;
    }

    /**
     * Render insurance tab content
     * @param {Object} patient - Patient data
     * @param {Object} primaryInsurance - Primary insurance
     * @param {Object} secondaryInsurance - Secondary insurance
     * @returns {string} HTML
     */
    renderInsuranceTab(patient, primaryInsurance, secondaryInsurance) {
        if (!patient.Insurances?.length) {
            return '<div class="alert alert-info"><i class="bi bi-info-circle me-2"></i>No insurance information on file.</div>';
        }

        // Active insurance records - show ALL, not just Primary/Secondary
        const activeInsurances = patient.Insurances.filter(i => i.IsActive !== false);
        const inactiveInsurances = patient.Insurances.filter(i => !i.IsActive);

        const typeLabels = { 0: 'Primary Insurance', 1: 'Secondary Insurance', 2: 'Additional Insurance' };

        let activeHtml = '';
        if (activeInsurances.length > 0) {
            activeHtml = activeInsurances
                .sort((a, b) => a.Type - b.Type)
                .map(ins => {
                    const label = typeLabels[ins.Type] || 'Additional Insurance';
                    return this.renderInsuranceCard(ins, label);
                }).join('');
        } else {
            activeHtml = '<div class="alert alert-info"><i class="bi bi-info-circle me-2"></i>No active insurance on file.</div>';
        }

        let historyHtml = '';
        if (inactiveInsurances.length > 0) {
            historyHtml = `
                <div class="mt-4">
                    <h6 class="text-muted mb-3"><i class="bi bi-clock-history me-2"></i>Insurance History (${inactiveInsurances.length} previous record${inactiveInsurances.length !== 1 ? 's' : ''})</h6>
                    ${inactiveInsurances.map(ins => {
                        const label = typeLabels[ins.Type] || 'Additional Insurance';
                        return this.renderInsuranceCard(ins, 'Previous ' + label);
                    }).join('')}
                </div>
            `;
        }

        return `${activeHtml}${historyHtml}`;
    }

    /**
     * Render insurance card
     * @param {Object} insurance - Insurance data
     * @param {string} title - Card title
     * @returns {string} HTML
     */
    renderInsuranceCard(insurance, title) {
        const categoryNames = ['Private Insurance', 'Medicare', 'Medicaid', 'Self Pay'];
        const categoryName = categoryNames[insurance.InsuranceCategory] || 'Unknown';
        const esc = (v) => this.utilities.escape(v || '-');

        // Render Attorney Information section
        let attorneyHtml = '';
        if (insurance.AttorneyName || insurance.AttorneyPhone || insurance.AttorneyEmail) {
            attorneyHtml = `
                <div class="mt-4 pt-3 border-top">
                    <h6 class="text-muted mb-3"><i class="bi bi-briefcase me-2"></i>Attorney Information</h6>
                    <div class="row">
                        <div class="col-md-6">
                            <p class="mb-2"><strong>Name:</strong> ${esc(insurance.AttorneyName)}</p>
                            <p class="mb-2"><strong>Phone:</strong> ${esc(insurance.AttorneyPhone)}</p>
                        </div>
                        <div class="col-md-6">
                            <p class="mb-2"><strong>Email:</strong> ${esc(insurance.AttorneyEmail)}</p>
                        </div>
                    </div>
                </div>
            `;
        }

        // Render authorizations list if available
        let authorizationsHtml = '';
        if (insurance.Authorizations && insurance.Authorizations.length > 0) {
            authorizationsHtml = `
                <div class="mt-4 pt-3 border-top">
                    <h6 class="text-muted mb-3"><i class="bi bi-clock-history me-2"></i>Authorization History</h6>
                    <div class="table-responsive">
                        <table class="table table-sm table-hover mb-0">
                            <thead class="table-light">
                                <tr>
                                    <th>Status</th>
                                    <th>Auth #</th>
                                    <th>Visits</th>
                                    <th>Expiry</th>
                                </tr>
                            </thead>
                            <tbody>
                                ${insurance.Authorizations.map(auth => {
                                    const statusClass = auth.IsActive ? 'bg-success' : (auth.IsExpired ? 'bg-danger' : 'bg-secondary');
                                    const statusText = auth.IsActive ? 'Active' : (auth.IsExpired ? 'Expired' : 'Inactive');
                                    return `
                                        <tr>
                                            <td><span class="badge ${statusClass}">${statusText}</span></td>
                                            <td>${this.utilities.escape(auth.AuthorizationNumber || '-')}</td>
                                            <td>${auth.AuthorizedVisits || 0}</td>
                                            <td>${auth.ExpiryDate ? this.utilities.formatDate(auth.ExpiryDate) : '-'}</td>
                                        </tr>
                                    `;
                                }).join('')}
                            </tbody>
                        </table>
                    </div>
                </div>
            `;
        } else {
            authorizationsHtml = `
                <div class="mt-4 pt-3 border-top">
                    <p class="text-muted mb-0"><i class="bi bi-info-circle me-1"></i>No authorizations on file</p>
                </div>
            `;
        }

        // Eligibility details placeholder — will auto-load if verified
        const eligibilityId = `eligibility_profile_${insurance.InsuranceId || 0}`;
        const eligibilitySection = insurance.LastVerifiedAt && insurance.InsuranceId
            ? `<div class="mt-3 pt-3 border-top">
                    <div id="${eligibilityId}">
                        <div class="text-center py-2">
                            <div class="spinner-border spinner-border-sm text-muted" role="status"></div>
                            <span class="text-muted small ms-2">Loading eligibility details...</span>
                        </div>
                    </div>
               </div>`
            : '';

        return `
            <div class="card mb-3">
                <div class="card-header d-flex justify-content-between align-items-center">
                    <strong><i class="bi bi-shield-check me-2"></i>${title}</strong>
                    <span class="badge ${insurance.IsActive ? 'bg-success' : 'bg-secondary'}">
                        ${insurance.IsActive ? 'Active' : 'Inactive'}
                    </span>
                </div>
                <div class="card-body">
                    <div class="row mb-3">
                        <div class="col-md-6">
                            <p class="mb-2"><strong>Type:</strong>
                                <span class="badge bg-info">${categoryName}</span>
                            </p>
                            <p class="mb-2"><strong>Payer Name:</strong> ${esc(insurance.PayerName)}</p>
                            <p class="mb-2"><strong>Payer ID:</strong> ${esc(insurance.PayerId)}</p>
                        </div>
                        <div class="col-md-6">
                            <p class="mb-2"><strong>Policy #:</strong> ${esc(insurance.PolicyNumber)}</p>
                            <p class="mb-2"><strong>Group #:</strong> ${esc(insurance.GroupNumber)}</p>
                            <p class="mb-2"><strong>Plan Allowed Visits:</strong> ${insurance.AllowedVisits || '-'}</p>
                        </div>
                    </div>
                    <div class="row mb-3">
                        <div class="col-md-6">
                            <p class="mb-2"><strong>Subscriber:</strong> ${esc(insurance.SubscriberFirstName ? `${insurance.SubscriberFirstName} ${insurance.SubscriberLastName || ''}`.trim() : insurance.SubscriberName)}</p>
                            <p class="mb-2"><strong>Subscriber DOB:</strong> ${insurance.SubscriberDob ? this.utilities.formatDate(insurance.SubscriberDob) : '-'}</p>
                        </div>
                        <div class="col-md-6">
                            <p class="mb-2"><strong>Relationship:</strong> ${esc(insurance.SubscriberRelationship)}</p>
                            ${insurance.EffectiveStartDate || insurance.EffectiveFrom ? `<p class="mb-2"><strong>Effective:</strong> ${this.utilities.formatDate(insurance.EffectiveStartDate || insurance.EffectiveFrom)}${insurance.EffectiveEndDate || insurance.EffectiveTo ? ' - ' + this.utilities.formatDate(insurance.EffectiveEndDate || insurance.EffectiveTo) : ''}</p>` : ''}
                        </div>
                    </div>
                    ${eligibilitySection}
                    ${attorneyHtml}
                    ${authorizationsHtml}
                </div>
            </div>
        `;
    }

    /**
     * Load rich eligibility details for all verified insurances in profile view.
     * @param {Object} patient - Patient data
     * @private
     */
    async _loadProfileEligibilityDetails(patient) {
        if (!patient.Insurances?.length) return;

        const verifiedInsurances = patient.Insurances.filter(i => i.LastVerifiedAt && i.InsuranceId);
        for (const ins of verifiedInsurances) {
            const el = document.getElementById(`eligibility_profile_${ins.InsuranceId}`);
            if (!el) continue;

            try {
                const currentUser = JSON.parse(localStorage.getItem('currentUser') || '{}');
                const headers = { 'Content-Type': 'application/json' };
                if (currentUser.token) headers['Authorization'] = `Bearer ${currentUser.token}`;

                const response = await fetch(`/api/insurance/${ins.InsuranceId}/eligibility-details`, {
                    method: 'GET',
                    headers
                });
                if (!response.ok) throw new Error('Not available');
                const details = await response.json();
                if (details) {
                    el.innerHTML = this.buildRichEligibilityHtml(details);
                } else {
                    el.innerHTML = this._buildBasicEligibilityHtml(ins);
                }
            } catch (err) {
                // Fallback to basic display from DB columns
                el.innerHTML = this._buildBasicEligibilityHtml(ins);
            }
        }
    }

    /**
     * EDI 271 service type code to human-readable name lookup.
     * Covers common medical codes from Office Ally.
     */
    static SERVICE_TYPE_NAMES = {
        '1': 'Medical Care', '2': 'Surgical', '3': 'Consultation', '4': 'Diagnostic X-Ray',
        '5': 'Diagnostic Lab', '6': 'Radiation Therapy', '7': 'Anesthesia', '8': 'Surgical Assistance',
        '12': 'Durable Medical Equipment', '14': 'Renal Supplies', '17': 'Pre-Admission Testing',
        '18': 'Durable Medical Equipment (Purchase)', '19': 'Pneumonia Vaccine', '20': 'Second Surgical Opinion',
        '21': 'Third Surgical Opinion', '22': 'Social Work', '23': 'Clinical Care', '24': 'Diagnostic Dental',
        '30': 'Health Benefit Plan Coverage', '33': 'Chiropractic', '34': 'Chiropractic Office Visits',
        '35': 'Dental Care', '42': 'Home Health Care',
        '45': 'Hospice', '46': 'Respite Care', '47': 'Hospital',
        '48': 'Hospital - Inpatient', '50': 'Hospital - Outpatient',
        '51': 'Hospital - Emergency Accident', '52': 'Hospital - Emergency Medical',
        '53': 'Hospital - Ambulatory Surgical', '54': 'Long Term Care', '55': 'Major Medical',
        '56': 'Medically Related Transportation', '60': 'General Benefits',
        '62': 'MRI/CAT Scan', '64': 'Acupuncture', '66': 'Pathology',
        '69': 'Maternity', '70': 'Transplants', '71': 'Audiology Exam',
        '73': 'Diagnostic Medical', '75': 'Prosthetic Device', '76': 'Dialysis',
        '78': 'Chemotherapy', '80': 'Immunizations', '81': 'Routine Physical',
        '82': 'Family Planning', '86': 'Emergency Services', '87': 'Cancer', '88': 'Pharmacy',
        '93': 'Podiatry', '96': 'Professional (Physician)',
        '98': 'Professional (Physician) Visit - Office',
        'A4': 'Psychiatric', 'A6': 'Psychotherapy',
        'A9': 'Rehabilitation', 'AB': 'Rehabilitation - Outpatient',
        'AC': 'Occupational Therapy', 'AD': 'Physical Medicine', 'AE': 'Speech Therapy',
        'AF': 'Skilled Nursing Care', 'AH': 'Substance Abuse',
        'AL': 'Vision (Optometry)', 'AN': 'Routine Eye Exam',
        'BD': 'Cognitive Therapy', 'BE': 'Massage Therapy',
        'BF': 'Pulmonary Rehabilitation', 'BG': 'Cardiac Rehabilitation',
        'BH': 'Pediatric', 'BK': 'Orthopedic',
        'BY': 'Physician Visit - Urgent Care', 'BZ': 'Physician Visit - Specialist',
        'MH': 'Mental Health', 'PT': 'Physical Therapy',
        'UC': 'Urgent Care'
    };

    /**
     * Build rich HTML from EligibilityDetailsDto (parsed from stored raw OA response).
     * Shows all sections: coverage, plan info, benefits, service-specific, messages.
     * Shared by PatientInsuranceManager (Edit tab) and PatientRenderer (Profile View).
     * @param {Object} d - EligibilityDetailsDto
     * @returns {string} HTML string
     */
    buildRichEligibilityHtml(d) {
        const esc = (v) => this.utilities.escape(String(v ?? ''));
        const money = (v) => {
            if (v == null || v === '') return '--';
            const n = Number(String(v).replace(/[^0-9.\-]/g, ''));
            return isNaN(n) ? '--' : `$${n.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
        };
        const pct = (v) => {
            if (v == null || v === '') return '--';
            const clean = String(v).replace(/%/g, '').trim();
            const n = Number(clean);
            return isNaN(n) ? '--' : `${n}%`;
        };
        const num = (v) => {
            if (v == null || v === '') return '--';
            const n = Number(v);
            return isNaN(n) ? String(v) : String(n);
        };
        const fmtDate = (ds) => {
            if (!ds) return '--';
            const dt = new Date(ds);
            return dt.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
        };
        const svcName = (code, fallback) => {
            if (fallback && fallback !== code) return fallback;
            return PatientRenderer.SERVICE_TYPE_NAMES[code] || fallback || `Service ${code}`;
        };

        const isEligible = d.IsEligible !== false;
        const statusIcon = isEligible
            ? '<i class="bi bi-check-circle-fill text-success me-1"></i>'
            : '<i class="bi bi-x-circle-fill text-danger me-1"></i>';
        const networkBadge = d.InNetwork === true
            ? '<span class="badge bg-success"><i class="bi bi-geo-alt-fill me-1"></i>In-Network</span>'
            : d.InNetwork === false
                ? '<span class="badge bg-warning text-dark"><i class="bi bi-geo-alt me-1"></i>Out-of-Network</span>'
                : '';

        let html = `
        <div class="card border-success mt-2 mb-2" style="font-size: 0.85rem;">
            <div class="card-header bg-success bg-opacity-10 d-flex justify-content-between align-items-center py-2">
                <span>${statusIcon}<strong>Eligibility Details</strong></span>
                <div>
                    ${d.VerifiedAt ? `<small class="text-muted me-2">Verified: ${fmtDate(d.VerifiedAt)}</small>` : ''}
                    ${networkBadge}
                </div>
            </div>
            <div class="card-body py-2 px-3">`;

        // ── Plan & Coverage Info ──
        html += `<div class="d-flex justify-content-between align-items-center mb-2">
                    <span class="fw-semibold">${esc(d.PlanName || 'Insurance Plan')}${d.PlanType ? ` <span class="text-muted">(${esc(d.PlanType)})</span>` : ''}</span>
                </div>`;

        if (d.GroupNumber || d.GroupName) {
            html += `<div class="small text-muted mb-2"><i class="bi bi-people me-1"></i>Group: ${esc(d.GroupNumber || '')}${d.GroupName ? ` - ${esc(d.GroupName)}` : ''}</div>`;
        }

        // Coverage dates
        if (d.CoverageEffectiveDate || d.CoverageTerminationDate || d.BenefitPeriod) {
            html += `<div class="small text-muted mb-2"><i class="bi bi-calendar-range me-1"></i>`;
            if (d.CoverageEffectiveDate || d.CoverageTerminationDate) {
                html += `Coverage: ${fmtDate(d.CoverageEffectiveDate)} – ${fmtDate(d.CoverageTerminationDate)}`;
            }
            if (d.BenefitPeriod) html += ` | ${esc(d.BenefitPeriod)}`;
            html += `</div>`;
        }

        // ── In-Network / Out-of-Network Benefits ──
        const inB = d.InNetworkBenefits;
        const outB = d.OutOfNetworkBenefits;
        const hasIn = inB && Object.values(inB).some(v => v != null);
        const hasOut = outB && Object.values(outB).some(v => v != null);

        if (hasIn || hasOut) {
            html += `<div class="row g-2 mb-2">`;

            if (hasIn) {
                html += `<div class="${hasOut ? 'col-md-6' : 'col-12'}">
                    <div class="border rounded p-2 h-100">
                        <div class="text-muted small mb-1 fw-semibold"><i class="bi bi-shield-check me-1"></i>In-Network Benefits</div>
                        ${inB.Copay != null ? `<div class="d-flex justify-content-between"><span>Copay:</span><strong>${money(inB.Copay)}</strong></div>` : ''}
                        ${inB.Coinsurance != null ? `<div class="d-flex justify-content-between"><span>Coinsurance:</span><strong>${pct(inB.Coinsurance)}</strong></div>` : ''}
                        ${inB.IndividualDeductible != null ? `<div class="d-flex justify-content-between"><span>Deductible:</span><strong>${money(inB.IndividualDeductible)}</strong></div>` : ''}
                        ${inB.IndividualDeductibleMet != null ? `<div class="d-flex justify-content-between"><span>Deductible Met:</span><strong>${money(inB.IndividualDeductibleMet)}</strong></div>` : ''}
                        ${inB.IndividualDeductibleRemaining != null ? `<div class="d-flex justify-content-between"><span>Deductible Remaining:</span><strong>${money(inB.IndividualDeductibleRemaining)}</strong></div>` : ''}
                        ${inB.FamilyDeductible != null ? `<div class="d-flex justify-content-between"><span>Family Deductible:</span><strong>${money(inB.FamilyDeductible)}</strong></div>` : ''}
                        ${inB.IndividualOopMax != null ? `<div class="d-flex justify-content-between"><span>OOP Max:</span><strong>${money(inB.IndividualOopMax)}</strong></div>` : ''}
                        ${inB.IndividualOopMet != null ? `<div class="d-flex justify-content-between"><span>OOP Met:</span><strong>${money(inB.IndividualOopMet)}</strong></div>` : ''}
                        ${inB.IndividualOopRemaining != null ? `<div class="d-flex justify-content-between"><span>OOP Remaining:</span><strong>${money(inB.IndividualOopRemaining)}</strong></div>` : ''}
                        ${inB.FamilyOopMax != null ? `<div class="d-flex justify-content-between"><span>Family OOP Max:</span><strong>${money(inB.FamilyOopMax)}</strong></div>` : ''}
                    </div>
                </div>`;
            }

            if (hasOut) {
                html += `<div class="${hasIn ? 'col-md-6' : 'col-12'}">
                    <div class="border rounded p-2 h-100">
                        <div class="text-muted small mb-1 fw-semibold"><i class="bi bi-shield-exclamation me-1"></i>Out-of-Network Benefits</div>
                        ${outB.Copay != null ? `<div class="d-flex justify-content-between"><span>Copay:</span><strong>${money(outB.Copay)}</strong></div>` : ''}
                        ${outB.Coinsurance != null ? `<div class="d-flex justify-content-between"><span>Coinsurance:</span><strong>${pct(outB.Coinsurance)}</strong></div>` : ''}
                        ${outB.IndividualDeductible != null ? `<div class="d-flex justify-content-between"><span>Deductible:</span><strong>${money(outB.IndividualDeductible)}</strong></div>` : ''}
                        ${outB.IndividualDeductibleMet != null ? `<div class="d-flex justify-content-between"><span>Deductible Met:</span><strong>${money(outB.IndividualDeductibleMet)}</strong></div>` : ''}
                        ${outB.IndividualDeductibleRemaining != null ? `<div class="d-flex justify-content-between"><span>Deductible Remaining:</span><strong>${money(outB.IndividualDeductibleRemaining)}</strong></div>` : ''}
                        ${outB.IndividualOopMax != null ? `<div class="d-flex justify-content-between"><span>OOP Max:</span><strong>${money(outB.IndividualOopMax)}</strong></div>` : ''}
                        ${outB.IndividualOopMet != null ? `<div class="d-flex justify-content-between"><span>OOP Met:</span><strong>${money(outB.IndividualOopMet)}</strong></div>` : ''}
                        ${outB.IndividualOopRemaining != null ? `<div class="d-flex justify-content-between"><span>OOP Remaining:</span><strong>${money(outB.IndividualOopRemaining)}</strong></div>` : ''}
                    </div>
                </div>`;
            }

            html += `</div>`;
        }

        // ── Visit Limits ──
        if (d.AllowedVisits != null || d.VisitsUsed != null || d.VisitsRemaining != null) {
            html += `<div class="border rounded p-2 mb-2">
                <div class="text-muted small mb-1 fw-semibold"><i class="bi bi-calendar-check me-1"></i>Visit Limits</div>
                <div class="row">
                    <div class="col-4 text-center"><div class="small text-muted">Allowed</div><strong>${num(d.AllowedVisits)}</strong></div>
                    <div class="col-4 text-center"><div class="small text-muted">Used</div><strong>${num(d.VisitsUsed)}</strong></div>
                    <div class="col-4 text-center"><div class="small text-muted">Remaining</div><strong>${num(d.VisitsRemaining)}</strong></div>
                </div>
            </div>`;
        }

        // ── Prior Auth Warning ──
        if (d.RequiresPriorAuthorization) {
            html += `<div class="alert alert-warning py-1 px-2 mb-2 small"><i class="bi bi-exclamation-triangle-fill me-1"></i>Prior authorization required</div>`;
        }

        // ── Service-Specific Benefits ──
        if (d.ServiceBenefits && d.ServiceBenefits.length > 0) {
            html += `<div class="mt-2 mb-2">
                <div class="text-muted small mb-1 fw-semibold"><i class="bi bi-list-check me-1"></i>Service-Specific Benefits</div>`;
            for (const svc of d.ServiceBenefits) {
                const displayName = svcName(svc.ServiceTypeCode, svc.ServiceType);
                html += `<div class="border rounded p-2 mb-1">
                    <div class="fw-semibold small">${esc(displayName)}${svc.ServiceTypeCode ? ` <span class="text-muted">(${esc(svc.ServiceTypeCode)})</span>` : ''}</div>`;
                if (svc.Items && svc.Items.length > 0) {
                    html += `<table class="table table-sm table-borderless mb-0" style="font-size: 0.8rem;">
                        <tbody>`;
                    for (const item of svc.Items) {
                        let val = '';
                        if (item.MonetaryAmount != null && item.MonetaryAmount !== '') val = money(item.MonetaryAmount);
                        else if (item.Percent != null && item.Percent !== '') val = pct(item.Percent);
                        else if (item.Quantity != null && item.Quantity !== '') val = num(item.Quantity);
                        const network = item.Network ? `<span class="badge ${item.Network === 'In' ? 'bg-success' : item.Network === 'Out' ? 'bg-warning text-dark' : 'bg-secondary'} me-1" style="font-size:0.7rem">${esc(item.Network)}</span>` : '';
                        const period = item.TimePeriod ? `<span class="text-muted">/ ${esc(item.TimePeriod)}</span>` : '';
                        html += `<tr>
                            <td class="py-1">${network}${esc(item.BenefitType || '')}</td>
                            <td class="py-1 text-end"><strong>${val}</strong> ${period}</td>
                        </tr>`;
                    }
                    html += `</tbody></table>`;
                }
                html += `</div>`;
            }
            html += `</div>`;
        }

        // ── Messages: Benefit Descriptions, Disclaimers, Exclusions, Limitations ──
        const messageSections = [
            { key: 'BenefitDescriptions', label: 'Benefit Descriptions', icon: 'bi-info-circle' },
            { key: 'Disclaimers', label: 'Disclaimers', icon: 'bi-exclamation-diamond' },
            { key: 'Exclusions', label: 'Exclusions', icon: 'bi-x-octagon' },
            { key: 'Limitations', label: 'Limitations', icon: 'bi-slash-circle' }
        ];
        for (const sec of messageSections) {
            if (d[sec.key] && d[sec.key].length > 0) {
                html += `<div class="mt-1 mb-1">
                    <div class="text-muted small fw-semibold"><i class="bi ${sec.icon} me-1"></i>${sec.label}</div>
                    <ul class="mb-0 small" style="padding-left: 1.2rem;">
                        ${d[sec.key].map(m => `<li>${esc(m)}</li>`).join('')}
                    </ul>
                </div>`;
            }
        }

        // ── Payer Info ──
        if (d.PayerName) {
            html += `<div class="mt-2 pt-2 border-top small text-muted">
                <i class="bi bi-building me-1"></i>Payer: ${esc(d.PayerName)}
                ${d.TransactionId ? ` | Transaction: ${esc(d.TransactionId)}` : ''}
            </div>`;
        }

        html += `</div></div>`;
        return html;
    }

    /**
     * Build basic eligibility display from stored DB columns (fallback).
     * @param {Object} ins - Insurance DTO
     * @returns {string} HTML
     * @private
     */
    _buildBasicEligibilityHtml(ins) {
        const esc = (v) => this.utilities.escape(String(v ?? ''));
        const money = (v) => v != null ? `$${Number(v).toFixed(2)}` : '--';
        const num = (v) => v != null ? String(v) : '--';
        const pct = (v) => v != null ? `${v}%` : '--';
        const isEligible = ins.EligibilityStatus === 1;
        const statusIcon = isEligible
            ? '<i class="bi bi-check-circle-fill text-success me-1"></i>'
            : '<i class="bi bi-x-circle-fill text-danger me-1"></i>';
        const verifiedStr = ins.LastVerifiedAt
            ? (window.formatTimestamp ? window.formatTimestamp(ins.LastVerifiedAt) : new Date(ins.LastVerifiedAt).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }))
            : '';

        return `
            <div class="card border-success mb-0" style="font-size: 0.85rem;">
                <div class="card-header bg-success bg-opacity-10 d-flex justify-content-between align-items-center py-2">
                    <span>${statusIcon}<strong>Eligibility</strong></span>
                    ${verifiedStr ? `<small class="text-muted">Verified: ${esc(verifiedStr)}</small>` : ''}
                </div>
                <div class="card-body py-2 px-3">
                    <div class="row g-2">
                        <div class="col-md-4">
                            <div class="d-flex justify-content-between"><span>Copay:</span><strong>${money(ins.Copay)}</strong></div>
                            <div class="d-flex justify-content-between"><span>Coinsurance:</span><strong>${pct(ins.Coinsurance)}</strong></div>
                        </div>
                        <div class="col-md-4">
                            <div class="d-flex justify-content-between"><span>Deductible:</span><strong>${money(ins.Deductible)}</strong></div>
                            <div class="d-flex justify-content-between"><span>OOP Max:</span><strong>${money(ins.OutOfPocketMax)}</strong></div>
                        </div>
                        <div class="col-md-4">
                            <div class="d-flex justify-content-between"><span>Allowed Visits:</span><strong>${num(ins.AllowedVisits)}</strong></div>
                        </div>
                    </div>
                </div>
            </div>`;
    }

    /**
     * Render care episodes tab content
     * @param {Object} patient - Patient data
     * @returns {string} HTML
     */
    renderCareEpisodesTab(patient) {
        // Check if patient has an active care episode (Status 0 = Active, 1 = Overdue)
        const hasActiveEpisode = patient.CareEpisodes?.some(ep => ep.Status === 0 || ep.Status === 1);

        // Check user role - only SuperAdmin (0), ClinicAdmin (1), and FrontDesk (3) can create/edit care episodes
        const currentUser = this.utilities.getCurrentUser();
        const canManageCareEpisodes = currentUser && [0, 1, 3].includes(currentUser.Role);

        // Build action buttons (only for authorized roles)
        const createButton = canManageCareEpisodes && !hasActiveEpisode ? `
            <button class="btn btn-primary btn-sm"
                    data-action="create-care-episode"
                    data-patient-id="${patient.PatientId}">
                <i class="bi bi-plus-lg me-1"></i>Create Care Episode
            </button>
        ` : '';

        // Header with create button
        const header = `
            <div class="d-flex justify-content-between align-items-center mb-3">
                <h6 class="mb-0"><i class="bi bi-clipboard2-pulse me-2"></i>Care Episodes</h6>
                ${createButton}
            </div>
        `;

        if (!patient.CareEpisodes?.length) {
            return `
                ${header}
                <div class="text-center text-muted py-4">
                    <i class="bi bi-clipboard2-x fs-1 d-block mb-2"></i>
                    No care episodes found for this patient.
                    ${canManageCareEpisodes ? `
                    <div class="mt-3">
                        <button class="btn btn-primary btn-sm"
                                data-action="create-care-episode"
                                data-patient-id="${patient.PatientId}">
                            <i class="bi bi-plus-lg me-1"></i>Create Care Episode
                        </button>
                    </div>
                    ` : ''}
                </div>
            `;
        }

        const episodeCards = patient.CareEpisodes.map(ep => {
            const statusClass = ep.Status === 0 ? 'bg-success' :
                              ep.Status === 1 ? 'bg-warning text-dark' : 'bg-secondary';
            const statusText = ep.Status === 0 ? 'Active' :
                             ep.Status === 1 ? 'Overdue' :
                             ep.Status === 2 ? 'Completed' : 'On Hold';

            // Action buttons for the episode (edit only for authorized roles)
            const editBtn = canManageCareEpisodes ? `
                <button class="btn btn-sm btn-outline-primary"
                        data-action="edit-care-episode"
                        data-patient-id="${patient.PatientId}"
                        data-episode-id="${ep.CareEpisodeId}"
                        title="Edit">
                    <i class="bi bi-pencil"></i>
                </button>
            ` : '';
            const viewBtn = `
                <button class="btn btn-sm btn-outline-info"
                        data-action="view-care-episode"
                        data-episode-id="${ep.CareEpisodeId}"
                        title="View Details">
                    <i class="bi bi-eye"></i>
                </button>
            `;

            // Complete/Restore button based on episode status (only for authorized roles)
            // Status 2 = Completed - show Restore button; otherwise show Complete button
            const completeRestoreBtn = canManageCareEpisodes ? (ep.Status === 2 ? `
                <button class="btn btn-sm btn-outline-warning"
                        data-action="restore-care-episode"
                        data-episode-id="${ep.CareEpisodeId}"
                        title="Restore">
                    <i class="bi bi-arrow-counterclockwise"></i>
                </button>
            ` : `
                <button class="btn btn-sm btn-outline-success"
                        data-action="complete-care-episode"
                        data-episode-id="${ep.CareEpisodeId}"
                        title="Complete">
                    <i class="bi bi-check-lg"></i>
                </button>
            `) : '';

            return `
                <div class="card mb-2">
                    <div class="card-body p-3">
                        <div class="d-flex justify-content-between align-items-start">
                            <div>
                                <strong>${this.utilities.escape(ep.PrimaryDiagnosis || '-')}</strong>
                                <span class="badge ${statusClass} ms-2">${statusText}</span>
                            </div>
                            <div class="btn-group btn-group-sm">
                                ${viewBtn}
                                ${editBtn}
                                ${completeRestoreBtn}
                            </div>
                        </div>
                        <div class="small text-muted mt-1">
                            ${this.utilities.formatDate(ep.StartDate)} - ${ep.EndDate ? this.utilities.formatDate(ep.EndDate) : 'Ongoing'} |
                            Visits: ${ep.VisitsUsed || 0} |
                            Provider: ${this.utilities.escape(ep.PrimaryProviderName || 'Not assigned')}
                        </div>
                    </div>
                </div>
            `;
        }).join('');

        return header + episodeCards;
    }

    /**
     * Render attachments tab content - shows loading state
     * @param {Object} patient - Patient data
     * @returns {string} HTML
     */
    renderAttachmentsTab(patient) {
        // Return loading state - documents will be loaded when tab is shown
        return `
            <div id="patientAttachmentsContent" data-patient-id="${patient.PatientId}">
                <div class="text-center py-3">
                    <span class="spinner-border spinner-border-sm me-1"></span>Loading documents...
                </div>
            </div>
        `;
    }

    /**
     * Render consents tab content
     * Returns a container that will be populated when tab is clicked
     * @param {Object} patient - Patient data
     * @returns {string} HTML
     */
    renderConsentsTab(patient) {
        // Return structure matching what ConsentModule expects
        // Content will be loaded via API when tab is clicked
        return `
            <div class="row g-3">
                <div class="col-12">
                    <div class="d-flex justify-content-between align-items-center mb-3">
                        <h6 class="mb-0"><i class="bi bi-file-earmark-check me-2"></i>Patient Consent History</h6>
                        <button type="button" class="btn btn-sm btn-outline-primary" onclick="refreshPatientConsentHistory()">
                            <i class="bi bi-arrow-clockwise me-1"></i>Refresh
                        </button>
                    </div>
                    <div id="patientConsentLoading" class="text-center py-4">
                        <div class="spinner-border spinner-border-sm text-primary" role="status"></div>
                        <span class="ms-2">Loading consent history...</span>
                    </div>
                    <div id="patientConsentNewPatient" class="alert alert-info d-none">
                        <i class="bi bi-info-circle me-2"></i>
                        Consent history will be available after the patient is created.
                    </div>
                    <div id="patientConsentNoRecords" class="alert alert-secondary d-none">
                        <i class="bi bi-file-earmark me-2"></i>
                        No consent records found for this patient. Consents are collected at the kiosk during check-in.
                    </div>
                    <div id="patientConsentContent" class="d-none">
                        <div class="d-none" id="currentConsentCard"><div id="currentConsentBody"></div></div>
                        <!-- Consent list -->
                        <div id="consentHistoryAccordion">
                            <!-- Consent items will be loaded here -->
                        </div>
                    </div>
                </div>
            </div>
        `;
    }

    /**
     * Render Medical Lien tab content
     * Displays form to generate Medical Lien PDF for the patient
     * @param {Object} patient - Patient data
     * @returns {string} HTML
     */
    renderMedicalLienTab(patient) {
        // Get attorney info from primary insurance if available
        const primaryInsurance = patient.Insurances?.find(i => i.Type === 0);
        const attorneyName = primaryInsurance?.AttorneyName || '-';
        const attorneyPhone = primaryInsurance?.AttorneyPhone || '-';
        const attorneyEmail = primaryInsurance?.AttorneyEmail || '-';

        // Format patient address
        const addressParts = [patient.Address, patient.City, patient.State, patient.ZipCode].filter(p => p);
        const patientAddress = addressParts.length > 0 ? addressParts.join(', ') : '-';

        // Format date of injury
        const dateOfInjury = patient.DateOfInjury ? this.utilities.formatDate(patient.DateOfInjury) : 'Not specified';

        return `
            <div class="row g-3">
                <div class="col-12">
                    <div class="alert alert-info">
                        <i class="bi bi-info-circle me-2"></i>
                        Generate a Notice of Medical Lien form for this patient. The form will be populated with patient information, attorney details (if available), and the selected provider's signature.
                    </div>
                </div>

                <!-- Provider Selection -->
                <div class="col-12">
                    <div class="card mb-3">
                        <div class="card-header">
                            <h6 class="mb-0"><i class="bi bi-person-badge me-2"></i>Select Provider</h6>
                        </div>
                        <div class="card-body">
                            <div class="row">
                                <div class="col-md-8">
                                    <label class="form-label">Provider <span class="text-danger">*</span></label>
                                    <select class="form-select" id="viewMedicalLienProviderSelect">
                                        <option value="">Select a provider...</option>
                                    </select>
                                    <small class="text-muted">The selected provider's name and signature will appear on the form.</small>
                                </div>
                                <div class="col-md-4">
                                    <label class="form-label">&nbsp;</label>
                                    <div id="viewMedicalLienProviderSignatureStatus" class="form-text mt-2"></div>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>

                <!-- Form Preview Info -->
                <div class="col-12">
                    <div class="card mb-3">
                        <div class="card-header">
                            <h6 class="mb-0"><i class="bi bi-file-earmark-text me-2"></i>Form Information</h6>
                        </div>
                        <div class="card-body" id="viewMedicalLienPreviewInfo">
                            <div class="row">
                                <div class="col-md-6">
                                    <h6 class="text-muted">Patient Information</h6>
                                    <p class="mb-1"><strong>Name:</strong> <span id="viewLienPreviewPatientName">${this.utilities.escape(patient.FullName || '-')}</span></p>
                                    <p class="mb-1"><strong>Address:</strong> <span id="viewLienPreviewPatientAddress">${this.utilities.escape(patientAddress)}</span></p>
                                    <p class="mb-1"><strong>Date of Injury:</strong> <span id="viewLienPreviewDateOfInjury">${this.utilities.escape(dateOfInjury)}</span></p>
                                </div>
                                <div class="col-md-6">
                                    <h6 class="text-muted">Attorney Information</h6>
                                    <p class="mb-1"><strong>Name:</strong> <span id="viewLienPreviewAttorneyName">${this.utilities.escape(attorneyName)}</span></p>
                                    <p class="mb-1"><strong>Phone:</strong> <span id="viewLienPreviewAttorneyPhone">${this.utilities.escape(attorneyPhone)}</span></p>
                                    <p class="mb-1"><strong>Email:</strong> <span id="viewLienPreviewAttorneyEmail">${this.utilities.escape(attorneyEmail)}</span></p>
                                </div>
                            </div>
                            <hr>
                            <div class="row">
                                <div class="col-12">
                                    <h6 class="text-muted">Clinic Information</h6>
                                    <p class="mb-1"><strong>Clinic:</strong> <span id="viewLienPreviewClinicName">-</span></p>
                                    <p class="mb-1"><strong>Address:</strong> <span id="viewLienPreviewClinicAddress">-</span></p>
                                    <p class="mb-0"><strong>Contact:</strong> <span id="viewLienPreviewClinicContact">-</span></p>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>

                <!-- Generate Button -->
                <div class="col-12 text-center">
                    <button type="button" class="btn btn-primary btn-lg" id="viewGenerateMedicalLienBtn" onclick="generateMedicalLienFormFromView(${patient.PatientId})">
                        <i class="bi bi-file-earmark-pdf me-2"></i>Generate & Download Medical Lien Form
                    </button>
                    <div id="viewMedicalLienGenerating" class="d-none mt-3">
                        <div class="spinner-border spinner-border-sm text-primary" role="status"></div>
                        <span class="ms-2">Generating PDF...</span>
                    </div>
                    <div id="viewMedicalLienError" class="alert alert-danger mt-3 d-none"></div>
                </div>

                <!-- Notes Section -->
                <div class="col-12 mt-4">
                    <div class="alert alert-secondary">
                        <h6><i class="bi bi-info-circle me-2"></i>Notes:</h6>
                        <ul class="mb-0 small">
                            <li>The notary section will be left blank for manual completion.</li>
                            <li>Treatment date range field is left blank for manual entry.</li>
                            <li>Attorney information is pulled from the patient's insurance record.</li>
                            <li>If any information is missing, those fields will be left blank on the form.</li>
                            <li>Provider signature is retrieved from the provider's profile.</li>
                        </ul>
                    </div>
                </div>
            </div>
        `;
    }

    /**
     * Render Appointments/Notes tab content
     * Returns a container that will be populated by PatientAppointmentsNotesModule
     * @param {Object} patient - Patient data
     * @returns {string} HTML
     */
    renderAppointmentsNotesTab(patient) {
        // Return a loading state container that will be populated when tab is clicked
        return `
            <div id="patientApptNotesContainer" data-patient-id="${patient.PatientId}">
                <div class="text-center py-4">
                    <div class="spinner-border spinner-border-sm text-primary" role="status"></div>
                    <span class="ms-2">Loading appointments and notes...</span>
                </div>
            </div>
        `;
    }

    /**
     * Get validation badge HTML
     * @param {Object} patient - Patient data
     * @returns {string} HTML badge
     */
    getValidationBadge(patient) {
        if (!patient.Insurances?.length) {
            return '<span class="badge bg-secondary">No Insurance</span>';
        }

        const primaryInsurance = patient.Insurances.find(i => i.Type === 0);
        if (!primaryInsurance) {
            return '<span class="badge bg-secondary">No Primary</span>';
        }

        // Check for valid authorization
        if (primaryInsurance.CurrentAuthorization) {
            const auth = primaryInsurance.CurrentAuthorization;
            if (auth.IsExpired) {
                return '<span class="badge bg-danger">Auth Expired</span>';
            }
            if (auth.RemainingVisits <= 2) {
                return '<span class="badge bg-warning text-dark">Low Visits</span>';
            }
            return '<span class="badge bg-success">Validated</span>';
        }

        return '<span class="badge bg-warning text-dark">Needs Auth</span>';
    }

    /**
     * Get status badge HTML
     * @param {number} status - Status code
     * @returns {string} HTML badge
     */
    getStatusBadge(status) {
        // Handle null/undefined - default to Active (0) for new patients
        const statusValue = (status === null || status === undefined) ? 0 : status;
        const statuses = {
            0: { class: 'bg-success', text: 'Active' },
            1: { class: 'bg-warning text-dark', text: 'Inactive' },
            2: { class: 'bg-secondary', text: 'Discharged' },
            3: { class: 'bg-dark', text: 'Deceased' }
        };
        const s = statuses[statusValue] || { class: 'bg-warning text-dark', text: 'Inactive' };
        return `<span class="badge ${s.class}">${s.text}</span>`;
    }

    /**
     * Get profile completeness badge HTML
     * @param {Object} patient - Patient data
     * @returns {string} HTML badge
     */
    getProfileBadge(patient) {
        const patientId = patient.PatientId;
        const clickableStyle = 'cursor: pointer;';
        const clickHandler = `onclick="window.patientModule.edit(${patientId})" title="Click to complete profile"`;

        // If ProfileCompleteness is available, show percentage
        if (patient.ProfileCompleteness !== undefined && patient.ProfileCompleteness !== null) {
            const percent = Math.round(patient.ProfileCompleteness);
            if (percent >= 100) {
                return `<span class="badge bg-success">Complete (${percent}%)</span>`;
            } else if (percent >= 50) {
                return `<span class="badge bg-warning text-dark" style="${clickableStyle}" ${clickHandler}>Incomplete (${percent}%)</span>`;
            } else {
                return `<span class="badge bg-danger" style="${clickableStyle}" ${clickHandler}>Incomplete (${percent}%)</span>`;
            }
        }

        // Fallback to ProfileStatus: 1 = Incomplete, 2 = Complete
        if (patient.ProfileStatus === 2) {
            return '<span class="badge bg-success">Complete</span>';
        } else if (patient.ProfileStatus === 1) {
            return `<span class="badge bg-warning text-dark" style="${clickableStyle}" ${clickHandler}>Incomplete</span>`;
        }

        // Calculate profile completeness if not provided
        const completeness = this.utilities.calculateProfileCompleteness(patient);
        if (completeness >= 100) {
            return `<span class="badge bg-success">Complete (${completeness}%)</span>`;
        } else if (completeness >= 50) {
            return `<span class="badge bg-warning text-dark" style="${clickableStyle}" ${clickHandler}>Incomplete (${completeness}%)</span>`;
        }
        return `<span class="badge bg-danger" style="${clickableStyle}" ${clickHandler}>Incomplete (${completeness}%)</span>`;
    }

    /**
     * Load patient finance tab content (balance + payment history)
     * @private
     */
    async _loadPatientFinance(patient) {
        const container = document.getElementById('patientFinanceContent');
        if (!container) return;

        container.innerHTML = '<div class="text-center p-3"><div class="spinner-border spinner-border-sm"></div> Loading...</div>';

        try {
            const token = localStorage.getItem('authToken');
            const headers = { 'Content-Type': 'application/json' };
            if (token) headers['Authorization'] = `Bearer ${token}`;

            const [balanceRes, paymentsRes] = await Promise.all([
                fetch(`/api/payments/patient/${patient.PatientId}/outstanding`, { headers }),
                fetch(`/api/payments?patientId=${patient.PatientId}`, { headers })
            ]);

            const balance = balanceRes.ok ? await balanceRes.json() : null;
            const payments = paymentsRes.ok ? await paymentsRes.json() : [];

            const currentBalance = balance?.CurrentBalance ?? 0;
            const totalCharges = balance?.TotalCharges ?? 0;
            const insurancePaid = balance?.InsurancePaid ?? 0;
            const patientPaid = balance?.PatientPaid ?? 0;
            const patientName = `${patient.FirstName || ''} ${patient.LastName || ''}`.trim();
            const escapedName = patientName.replace(/'/g, "\\'");

            let html = `
                <!-- Balance Summary Cards -->
                <div class="row g-3 mb-4">
                    <div class="col-md-3">
                        <div class="card border-0 shadow-sm h-100">
                            <div class="card-body text-center">
                                <div class="text-muted small mb-1">Total Charges</div>
                                <div class="fs-4 fw-bold text-primary">$${totalCharges.toFixed(2)}</div>
                            </div>
                        </div>
                    </div>
                    <div class="col-md-3">
                        <div class="card border-0 shadow-sm h-100">
                            <div class="card-body text-center">
                                <div class="text-muted small mb-1">Insurance Paid</div>
                                <div class="fs-4 fw-bold text-info">$${insurancePaid.toFixed(2)}</div>
                            </div>
                        </div>
                    </div>
                    <div class="col-md-3">
                        <div class="card border-0 shadow-sm h-100">
                            <div class="card-body text-center">
                                <div class="text-muted small mb-1">Patient Paid</div>
                                <div class="fs-4 fw-bold text-success">$${patientPaid.toFixed(2)}</div>
                            </div>
                        </div>
                    </div>
                    <div class="col-md-3">
                        <div class="card border-0 shadow-sm h-100">
                            <div class="card-body text-center">
                                <div class="text-muted small mb-1">Outstanding Balance</div>
                                <div class="fs-4 fw-bold ${currentBalance > 0 ? 'text-danger' : 'text-success'}">$${currentBalance.toFixed(2)}</div>
                            </div>
                        </div>
                    </div>
                </div>`;

            // Collect Payment button only if outstanding balance > 0
            if (currentBalance > 0) {
                html += `
                <div class="mb-3">
                    <button class="btn btn-primary" onclick="openCollectPaymentModal(${patient.PatientId}, null, ${currentBalance}, '${escapedName}')">
                        <i class="bi bi-cash-coin me-1"></i>Collect Payment
                    </button>
                </div>`;
            }

            // Payment History Table
            html += `
                <h6 class="fw-semibold mb-2"><i class="bi bi-clock-history me-1"></i>Payment History</h6>
                <div class="table-responsive">
                    <table class="table table-hover table-sm mb-0">
                        <thead>
                            <tr><th>Date</th><th>Type</th><th>Method</th><th>Amount</th><th>Status</th><th>Notes</th></tr>
                        </thead>
                        <tbody>`;

            if (payments && payments.length > 0) {
                payments.forEach(p => {
                    const date = p.PaymentDate ? new Date(p.PaymentDate).toLocaleDateString() : '-';
                    const typeNames = ['Copay', 'Coinsurance', 'Deductible', 'Self Pay'];
                    const methodNames = ['Cash', 'Check', 'Credit Card', 'Debit Card', 'EFT', 'ERA', 'Other'];
                    const typeName = typeNames[p.Type] || p.TypeName || '-';
                    const methodName = methodNames[p.Method] || p.MethodName || '-';
                    const amount = p.Amount != null ? '$' + parseFloat(p.Amount).toFixed(2) : '-';
                    const statusBadge = p.Status === 1 ? '<span class="badge bg-success">Completed</span>' :
                                        p.Status === 2 ? '<span class="badge bg-warning">Pending</span>' :
                                        p.Status === 3 ? '<span class="badge bg-danger">Voided</span>' :
                                        '<span class="badge bg-secondary">Unknown</span>';
                    html += `<tr><td>${date}</td><td>${typeName}</td><td>${methodName}</td><td class="fw-semibold text-success">${amount}</td><td>${statusBadge}</td><td class="text-muted small">${this.utilities.escape(p.Notes || '-')}</td></tr>`;
                });
            } else {
                html += '<tr><td colspan="6" class="text-center text-muted py-3">No payment records found</td></tr>';
            }

            html += `</tbody></table></div>`;
            container.innerHTML = html;
        } catch (error) {
            console.error('[PatientRenderer] Failed to load finance data:', error);
            container.innerHTML = '<div class="text-center text-danger p-3"><i class="bi bi-exclamation-triangle me-1"></i>Failed to load finance data</div>';
        }
    }

}

// Export for use in both modern and legacy environments
if (typeof module !== 'undefined' && module.exports) {
    module.exports = PatientRenderer;
}
window.PatientRenderer = PatientRenderer;
