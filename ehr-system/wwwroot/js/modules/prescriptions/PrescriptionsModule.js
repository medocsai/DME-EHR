/**
 * PrescriptionsModule - E-Prescribing management
 * Handles prescription list, create/edit/view, drug/pharmacy/patient search
 */
class PrescriptionsModule {
    constructor() {
        this._debounceTimers = {};
        this._selectedDrug = null;
        this._selectedPharmacy = null;
        this._selectedPatient = null;
        const _user = JSON.parse(localStorage.getItem('currentUser') || '{}');
        this._userRole = parseInt(_user.Role ?? _user.role ?? -1);
        this._isMaNurse = UserRoles.isMaNurse(this._userRole);
    }

    init() {
        // Always bind modal events and load providers (modal can open from any page)
        this._bindModalEvents();
        this._loadProviders();

        // Page-specific: only on prescriptions page
        if (!document.getElementById('prescriptionsPage')) return;
        this._bindPageEvents();
        this.load();
    }

    _bindModalEvents() {
        // Modal form events
        const btnBuildSIG = document.getElementById('btnBuildSIG');
        if (btnBuildSIG) btnBuildSIG.addEventListener('click', () => this._buildSIG());

        const btnSaveDraft = document.getElementById('btnSaveDraft');
        if (btnSaveDraft) btnSaveDraft.addEventListener('click', () => this._savePrescription(0));

        const btnSignSend = document.getElementById('btnSignAndSend');
        if (btnSignSend) btnSignSend.addEventListener('click', () => this._savePrescription(2));

        // AI drug suggest button
        const btnSuggestDrugs = document.getElementById('btnSuggestDrugs');
        if (btnSuggestDrugs) btnSuggestDrugs.addEventListener('click', () => this._suggestDrugs());

        // Drug search autocomplete
        const drugSearch = document.getElementById('rxDrugSearch');
        if (drugSearch) {
            drugSearch.addEventListener('input', () => this._debounce('drug', () => this._searchDrugs(), 300));
            drugSearch.addEventListener('blur', () => setTimeout(() => this._hideDropdown('rxDrugDropdown'), 200));
        }

        // Pharmacy search autocomplete
        const pharmSearch = document.getElementById('rxPharmacySearch');
        if (pharmSearch) {
            pharmSearch.addEventListener('input', () => this._debounce('pharm', () => this._searchPharmacies(), 300));
            pharmSearch.addEventListener('blur', () => setTimeout(() => this._hideDropdown('rxPharmacyDropdown'), 200));
        }

        // Patient search autocomplete
        const patSearch = document.getElementById('rxPatientSearch');
        if (patSearch) {
            patSearch.addEventListener('input', () => this._debounce('patient', () => this._searchPatients(), 300));
            patSearch.addEventListener('blur', () => setTimeout(() => this._hideDropdown('rxPatientDropdown'), 200));
        }

        // Close dropdowns on clicks outside
        document.addEventListener('click', (e) => {
            if (!e.target.closest('#rxDrugDropdown') && e.target.id !== 'rxDrugSearch') this._hideDropdown('rxDrugDropdown');
            if (!e.target.closest('#rxPharmacyDropdown') && e.target.id !== 'rxPharmacySearch') this._hideDropdown('rxPharmacyDropdown');
            if (!e.target.closest('#rxPatientDropdown') && e.target.id !== 'rxPatientSearch') this._hideDropdown('rxPatientDropdown');
        });
    }

    _bindPageEvents() {
        // New Rx button
        const btnNew = document.getElementById('btnNewPrescription');
        if (btnNew) btnNew.addEventListener('click', () => this._openNewRxModal());

        // Filter events
        const filterSearch = document.getElementById('rxFilterSearch');
        if (filterSearch) filterSearch.addEventListener('input', () => this._debounce('filter', () => this.load(), 400));
        const filterStatus = document.getElementById('rxFilterStatus');
        if (filterStatus) filterStatus.addEventListener('change', () => this.load());
        const filterStart = document.getElementById('rxFilterStartDate');
        if (filterStart) filterStart.addEventListener('change', () => this.load());
        const filterEnd = document.getElementById('rxFilterEndDate');
        if (filterEnd) filterEnd.addEventListener('change', () => this.load());

        // Clear filters
        const btnClear = document.getElementById('btnClearRxFilters');
        if (btnClear) btnClear.addEventListener('click', () => this._clearFilters());
    }

    async load() {
        const search = document.getElementById('rxFilterSearch')?.value || '';
        const status = document.getElementById('rxFilterStatus')?.value || '';
        const dateFrom = document.getElementById('rxFilterStartDate')?.value || '';
        const dateTo = document.getElementById('rxFilterEndDate')?.value || '';

        let url = '/prescriptions?';
        if (status) url += `status=${status}&`;
        if (dateFrom) url += `dateFrom=${dateFrom}&`;
        if (dateTo) url += `dateTo=${dateTo}&`;

        try {
            const data = await apiRequest(url, { showLoader: false });
            let items = data || [];

            // Client-side text search filter
            if (search) {
                const q = search.toLowerCase();
                items = items.filter(rx =>
                    (rx.PatientName || '').toLowerCase().includes(q) ||
                    (rx.DrugName || '').toLowerCase().includes(q) ||
                    (rx.GenericName || '').toLowerCase().includes(q)
                );
            }

            this._renderTable(items);
        } catch (err) {
            console.error('[Prescriptions] Load error:', err);
        }
    }

    _renderTable(items) {
        const tbody = document.querySelector('#prescriptionsTable tbody');
        if (!tbody) return;

        if (!items || items.length === 0) {
            tbody.innerHTML = '<tr><td colspan="10" class="text-center text-muted py-4">No prescriptions found</td></tr>';
            const info = document.getElementById('rxPageInfo');
            if (info) info.textContent = 'Showing 0 prescriptions';
            return;
        }

        tbody.innerHTML = items.map(rx => `
            <tr>
                <td>${rx.PrescribedDate || ''}</td>
                <td><strong>${rx.PatientName || ''}</strong></td>
                <td>${rx.DrugName || ''}<br><small class="text-muted">${rx.GenericName || ''}</small></td>
                <td>${rx.Strength || ''}</td>
                <td><small>${rx.DirectionsFreeText || ''}</small></td>
                <td>${rx.Quantity || ''}</td>
                <td>${rx.Refills ?? 0}</td>
                <td><small>${rx.PharmacyName || ''}</small></td>
                <td>${PrescriptionStatuses.getBadgeHtml(rx.Status)}</td>
                <td>
                    <button class="btn btn-sm btn-outline-info" onclick="window._prescriptionsModule._viewRx(${rx.PrescriptionId})" title="View">
                        <i class="bi bi-eye"></i>
                    </button>
                    ${!this._isMaNurse && rx.Status === 0 ? `
                        <button class="btn btn-sm btn-outline-secondary" onclick="window._prescriptionsModule._editRx(${rx.PrescriptionId})" title="Edit Draft">
                            <i class="bi bi-pencil"></i>
                        </button>
                        <button class="btn btn-sm btn-outline-primary" onclick="window._prescriptionsModule._sendRx(${rx.PrescriptionId})" title="Sign & Send">
                            <i class="bi bi-send-check"></i>
                        </button>
                    ` : ''}
                    ${!this._isMaNurse && rx.Status <= 2 ? `
                        <button class="btn btn-sm btn-outline-danger" onclick="window._prescriptionsModule._cancelRx(${rx.PrescriptionId})" title="Cancel">
                            <i class="bi bi-x-circle"></i>
                        </button>
                    ` : ''}
                </td>
            </tr>
        `).join('');

        const info = document.getElementById('rxPageInfo');
        if (info) info.textContent = `Showing ${items.length} prescription${items.length !== 1 ? 's' : ''}`;
    }

    // ---- Modal operations ----

    _openNewRxModal() {
        this._resetForm();
        this._editingId = null;
        document.getElementById('prescriptionModalTitle').innerHTML = '<i class="bi bi-capsule me-2"></i>New Prescription';
        document.getElementById('btnSaveDraft').classList.remove('d-none');
        document.getElementById('btnSignAndSend').classList.remove('d-none');
        const modal = new bootstrap.Modal(document.getElementById('prescriptionModal'));
        modal.show();
    }

    _resetForm() {
        document.getElementById('prescriptionForm')?.reset();
        document.getElementById('rxPrescriptionId').value = '';
        document.getElementById('rxPatientId').value = '';
        document.getElementById('rxPharmacyId').value = '';
        document.getElementById('rxPatientSearch').value = '';
        document.getElementById('rxDrugSearch').value = '';
        document.getElementById('rxPharmacySearch').value = '';
        document.getElementById('rxStrength').value = '';
        document.getElementById('rxPharmacyPhone').value = '';
        document.getElementById('rxPharmacyAddress').value = '';
        document.getElementById('rxDirections').value = '';
        document.getElementById('rxDrugName').value = '';
        document.getElementById('rxGenericName').value = '';
        document.getElementById('rxNDCCode').value = '';
        document.getElementById('rxDosageForm').value = '';
        document.getElementById('rxRouteHidden').value = '';
        document.getElementById('rxDEASchedule').value = '';
        document.getElementById('rxIsControlled').value = '';
        document.getElementById('rxWarningsArea').classList.add('d-none');
        document.getElementById('rxControlledArea').classList.add('d-none');
        document.getElementById('rxDoseAmount').value = '1';
        document.getElementById('rxQuantity').value = '30';
        document.getElementById('rxDaysSupply').value = '30';
        document.getElementById('rxRefills').value = '0';
        this._selectedDrug = null;
        this._selectedPharmacy = null;
        this._selectedPatient = null;
        // Re-select provider from user profile after form reset
        const _user = JSON.parse(localStorage.getItem('currentUser') || '{}');
        const currentProviderId = _user.ProviderId || _user.providerId;
        if (currentProviderId) {
            const sel = document.getElementById('rxProviderId');
            if (sel) sel.value = currentProviderId;
        }
        // Hide stale drug warnings from previous prescription
        document.getElementById('rxWarningsArea')?.classList.add('d-none');
    }

    async _loadProviders() {
        try {
            const providers = await apiRequest('/providers', { showLoader: false });
            const select = document.getElementById('rxProviderId');
            if (select && providers) {
                select.innerHTML = '<option value="">Select Provider</option>';
                (providers || []).forEach(p => {
                    select.innerHTML += `<option value="${p.ProviderId}">${p.FirstName} ${p.LastName}${p.Credentials ? ', ' + p.Credentials : ''}</option>`;
                });
                // Auto-select current provider from user profile
                const _user = JSON.parse(localStorage.getItem('currentUser') || '{}');
                const currentProviderId = _user.ProviderId || _user.providerId;
                const userRole = parseInt(_user.Role ?? _user.role ?? -1);
                if (currentProviderId) {
                    select.value = currentProviderId;
                    // Only ClinicAdmin (1), SuperAdmin (0), and FrontDesk (3) can select a different provider
                    const canChangeProvider = [0, 1, 3].includes(userRole);
                    select.disabled = !canChangeProvider;
                }
            }
        } catch (err) {
            console.error('[Prescriptions] Load providers error:', err);
        }
    }

    // ---- Drug search ----

    async _searchDrugs() {
        const input = document.getElementById('rxDrugSearch');
        const query = input?.value?.trim();
        if (!query || query.length < 2) { this._hideDropdown('rxDrugDropdown'); return; }

        try {
            // Try local DB first
            let drugs = await apiRequest(`/drugs/search?q=${encodeURIComponent(query)}&limit=10`, { showLoader: false, showErrors: false });
            let isAi = false;

            // Fallback to AI-powered search if DB returns no results
            if (!drugs || drugs.length === 0) {
                drugs = await apiRequest(`/ai/drugs/search?q=${encodeURIComponent(query)}&limit=10`, { showLoader: false, showErrors: false });
                isAi = true;
            }

            this._lastDrugResults = drugs || [];
            this._showDropdown('rxDrugDropdown', (drugs || []).map(d =>
                `<a class="dropdown-item" href="#" data-drug-id="${d.DrugId}">
                    <strong>${d.BrandName}</strong> <small class="text-muted">(${d.GenericName})</small> ${d.Strength}
                    ${d.DEASchedule ? '<span class="badge bg-danger ms-1">C-' + d.DEASchedule + '</span>' : ''}
                    ${isAi ? '<span class="badge bg-info ms-1">AI</span>' : ''}
                </a>`
            ).join(''), (el) => {
                const drugId = el.dataset.drugId;
                const drug = this._lastDrugResults.find(d => d.DrugId == drugId);
                if (drug) this._selectDrug(drug);
            });
        } catch (err) {
            console.error('[Prescriptions] Drug search error:', err);
        }
    }

    _selectDrug(drug) {
        this._selectedDrug = drug;
        document.getElementById('rxDrugSearch').value = drug.DisplayName;
        document.getElementById('rxDrugName').value = drug.BrandName;
        document.getElementById('rxGenericName').value = drug.GenericName;
        document.getElementById('rxNDCCode').value = drug.NDCCode || '';
        document.getElementById('rxStrength').value = drug.Strength;
        document.getElementById('rxDosageForm').value = drug.DosageForm;
        document.getElementById('rxRouteHidden').value = drug.Route;
        document.getElementById('rxRoute').value = drug.Route;

        // Set dose unit based on dosage form
        const unitMap = { 0: 'tablet', 1: 'capsule', 2: 'ml', 7: 'puff', 8: 'drop' };
        const doseUnit = document.getElementById('rxDoseUnit');
        if (doseUnit && unitMap[drug.DosageForm] !== undefined) {
            doseUnit.value = unitMap[drug.DosageForm];
        }

        // Auto-fill directions
        if (drug.CommonDirections) {
            document.getElementById('rxDirections').value = drug.CommonDirections;
        }

        // Show warnings
        if (drug.Warnings) {
            document.getElementById('rxWarningsArea').classList.remove('d-none');
            document.getElementById('rxWarningsText').textContent = drug.Warnings;
        } else {
            document.getElementById('rxWarningsArea').classList.add('d-none');
        }

        // Show controlled substance notice
        if (drug.DEASchedule) {
            document.getElementById('rxControlledArea').classList.remove('d-none');
            document.getElementById('rxScheduleText').textContent = drug.DEASchedule;
            document.getElementById('rxDEASchedule').value = drug.DEASchedule;
            document.getElementById('rxIsControlled').value = 'true';
        } else {
            document.getElementById('rxControlledArea').classList.add('d-none');
            document.getElementById('rxDEASchedule').value = '';
            document.getElementById('rxIsControlled').value = 'false';
        }

        this._hideDropdown('rxDrugDropdown');
    }

    // ---- AI Drug Suggest from CC & HPI ----

    async _suggestDrugs() {
        const btn = document.getElementById('btnSuggestDrugs');
        const suggestionsDiv = document.getElementById('rxDrugSuggestions');

        // Get CC & HPI from encounter workspace
        let cc = '', hpi = '';
        if (window._encounterWorkspace?.encounter) {
            cc = window._encounterWorkspace.encounter.ChiefComplaint || '';
            hpi = window._encounterWorkspace.encounter.HistoryOfPresentIllness || '';
        }

        if (!cc && !hpi) {
            if (window.showToast) window.showToast('No Chief Complaint or HPI available. Please fill them in the encounter first.', 'warning');
            return;
        }

        if (btn) { btn.disabled = true; btn.innerHTML = '<i class="bi bi-hourglass-split spin me-1"></i>Thinking...'; }

        try {
            const results = await apiRequest('/ai/drugs/suggest', {
                method: 'POST',
                body: { ChiefComplaint: cc, HPI: hpi },
                showLoader: false,
                showErrors: false
            });

            if (!results || results.length === 0) {
                if (window.showToast) window.showToast('No drug suggestions available', 'info');
                return;
            }

            suggestionsDiv.innerHTML = results.map(d =>
                `<a href="#" class="list-group-item list-group-item-action py-2" data-drug='${JSON.stringify(d).replace(/'/g, "&#39;")}'>
                    <div class="d-flex justify-content-between align-items-start">
                        <div>
                            <strong>${d.BrandName}</strong> <small class="text-muted">(${d.GenericName})</small> ${d.Strength}
                            ${d.DEASchedule ? '<span class="badge bg-danger ms-1">C-' + d.DEASchedule + '</span>' : ''}
                        </div>
                        <span class="badge bg-info">AI</span>
                    </div>
                    ${d.Rationale ? '<small class="text-muted">' + d.Rationale + '</small>' : ''}
                    ${d.CommonDirections ? '<br><small class="text-secondary fst-italic">' + d.CommonDirections + '</small>' : ''}
                </a>`
            ).join('');
            suggestionsDiv.style.display = '';

            suggestionsDiv.querySelectorAll('.list-group-item').forEach(item => {
                item.addEventListener('click', (e) => {
                    e.preventDefault();
                    const drug = JSON.parse(item.dataset.drug);
                    this._selectDrug(drug);
                    suggestionsDiv.style.display = 'none';
                });
            });

        } catch (err) {
            console.error('[Prescriptions] Drug suggest error:', err);
            if (window.showToast) window.showToast('Failed to get drug suggestions', 'error');
        } finally {
            if (btn) { btn.disabled = false; btn.innerHTML = '<i class="bi bi-stars me-1"></i>Suggest Drugs from CC & HPI'; }
        }
    }

    // ---- Pharmacy search ----

    async _searchPharmacies() {
        const input = document.getElementById('rxPharmacySearch');
        const query = input?.value?.trim();
        if (!query || query.length < 2) { this._hideDropdown('rxPharmacyDropdown'); return; }

        try {
            // Try local DB first
            let pharmacies = await apiRequest(`/pharmacies/search?q=${encodeURIComponent(query)}`, { showLoader: false, showErrors: false });
            let isAi = false;

            // Fallback to AI-powered search if DB returns no results
            if (!pharmacies || pharmacies.length === 0) {
                pharmacies = await apiRequest(`/ai/pharmacies/search?q=${encodeURIComponent(query)}`, { showLoader: false, showErrors: false });
                isAi = true;
            }

            this._lastPharmResults = pharmacies || [];
            this._showDropdown('rxPharmacyDropdown', (pharmacies || []).map(p =>
                `<a class="dropdown-item" href="#" data-pharm-id="${p.PharmacyId}">
                    <strong>${p.Name}</strong>${isAi ? ' <span class="badge bg-info">AI</span>' : ''}<br>
                    <small class="text-muted">${p.FullAddress} | ${p.Phone}</small>
                </a>`
            ).join(''), (el) => {
                const pharmId = el.dataset.pharmId;
                const pharm = this._lastPharmResults.find(p => p.PharmacyId == pharmId);
                if (pharm) this._selectPharmacy(pharm);
            });
        } catch (err) {
            console.error('[Prescriptions] Pharmacy search error:', err);
        }
    }

    _selectPharmacy(pharm) {
        this._selectedPharmacy = pharm;
        document.getElementById('rxPharmacySearch').value = pharm.Name;
        document.getElementById('rxPharmacyId').value = pharm.PharmacyId;
        document.getElementById('rxPharmacyPhone').value = pharm.Phone;
        document.getElementById('rxPharmacyAddress').value = pharm.FullAddress;
        this._hideDropdown('rxPharmacyDropdown');
    }

    // ---- Patient search ----

    async _searchPatients() {
        const input = document.getElementById('rxPatientSearch');
        const query = input?.value?.trim();
        if (!query || query.length < 2) { this._hideDropdown('rxPatientDropdown'); return; }

        try {
            const response = await apiRequest(`/patients/search?q=${encodeURIComponent(query)}&take=8&activeOnly=true`, { showLoader: false, showErrors: false });
            const patients = response?.Results || [];
            this._showDropdown('rxPatientDropdown', patients.map(p =>
                `<a class="dropdown-item" href="#" data-patient-id="${p.PatientId}">
                    <strong>${p.FirstName} ${p.LastName}</strong>
                    <small class="text-muted ms-2">MRN: ${p.MRN || 'N/A'} | DOB: ${p.DOBFormatted || p.DateOfBirth || ''}</small>
                </a>`
            ).join(''), (el) => {
                const patId = el.dataset.patientId;
                const pat = patients.find(p => p.PatientId == patId);
                if (pat) this._selectPatient(pat);
            });
        } catch (err) {
            console.error('[Prescriptions] Patient search error:', err);
        }
    }

    _selectPatient(patient) {
        this._selectedPatient = patient;
        const mrn = patient.MRN || '';
        const display = mrn ? `${patient.FirstName} ${patient.LastName} (${mrn})` : `${patient.FirstName} ${patient.LastName}`;
        document.getElementById('rxPatientSearch').value = display;
        document.getElementById('rxPatientId').value = patient.PatientId;
        this._hideDropdown('rxPatientDropdown');
    }

    // ---- SIG builder ----

    _buildSIG() {
        const amount = document.getElementById('rxDoseAmount')?.value || '1';
        const unit = document.getElementById('rxDoseUnit')?.value || 'tablet';
        const routeVal = document.getElementById('rxRoute')?.value;
        const freqVal = document.getElementById('rxFrequency')?.value;

        const routeName = MedicationRoutes.getName(parseInt(routeVal)) || 'oral';
        const freqName = MedicationFrequencies.getName(parseInt(freqVal)) || 'once daily';

        // Build the route preposition
        const routePrep = routeName === 'Oral' ? 'by mouth' :
            routeName === 'Topical' ? 'topically' :
            routeName === 'Subcutaneous' ? 'subcutaneously' :
            routeName === 'Inhalation' ? 'by inhalation' :
            routeName === 'Nasal' ? 'intranasally' :
            `via ${routeName.toLowerCase()} route`;

        const sig = `Take ${amount} ${unit}${parseInt(amount) > 1 ? 's' : ''} ${routePrep} ${freqName.toLowerCase()}`;
        document.getElementById('rxDirections').value = sig;
    }

    // ---- Save ----

    async _savePrescription(status) {
        const patientId = document.getElementById('rxPatientId')?.value;
        const providerId = document.getElementById('rxProviderId')?.value;
        const drugName = document.getElementById('rxDrugName')?.value;
        const directions = document.getElementById('rxDirections')?.value;

        if (!patientId) { showToast('Please select a patient', 'warning'); return; }
        if (!providerId) { showToast('Please select a provider', 'warning'); return; }
        if (!drugName) { showToast('Please select a drug', 'warning'); return; }
        if (!directions) { showToast('Please enter directions (SIG)', 'warning'); return; }

        const dto = {
            PatientId: parseInt(patientId),
            ProviderId: parseInt(providerId),
            DrugName: document.getElementById('rxDrugName').value,
            GenericName: document.getElementById('rxGenericName').value,
            NDCCode: document.getElementById('rxNDCCode').value,
            RxNormCode: '',
            Strength: document.getElementById('rxStrength').value,
            DosageForm: parseInt(document.getElementById('rxDosageForm').value) || 0,
            Quantity: parseFloat(document.getElementById('rxQuantity').value) || 30,
            DaysSupply: parseInt(document.getElementById('rxDaysSupply').value) || 30,
            DoseAmount: document.getElementById('rxDoseAmount').value,
            DoseUnit: document.getElementById('rxDoseUnit').value,
            Route: parseInt(document.getElementById('rxRoute').value) || 0,
            Frequency: parseInt(document.getElementById('rxFrequency').value) || 0,
            DirectionsFreeText: directions,
            Refills: parseInt(document.getElementById('rxRefills').value) || 0,
            DAW: document.getElementById('rxDAW')?.checked || false,
            PharmacyName: document.getElementById('rxPharmacySearch')?.value || '',
            PharmacyPhone: document.getElementById('rxPharmacyPhone')?.value || '',
            PharmacyAddress: document.getElementById('rxPharmacyAddress')?.value || '',
            Status: status,
            IsControlledSubstance: document.getElementById('rxIsControlled')?.value === 'true',
            DEASchedule: parseInt(document.getElementById('rxDEASchedule')?.value) || null,
            DiagnosisCode: document.getElementById('rxDiagnosisCode')?.value || '',
            Notes: document.getElementById('rxNotes')?.value || ''
        };

        try {
            const editId = this._editingId;
            if (editId) {
                // Update existing draft
                await apiRequest(`/prescriptions/${editId}`, {
                    method: 'PUT',
                    body: dto
                });
            } else {
                // Create new
                await apiRequest('/prescriptions', {
                    method: 'POST',
                    body: dto
                });
            }

            const statusName = status === 0 ? 'saved as draft' : 'signed and sent';
            showToast(`Prescription ${statusName} successfully`, 'success');

            // Close modal
            const modal = bootstrap.Modal.getInstance(document.getElementById('prescriptionModal'));
            if (modal) modal.hide();

            this._editingId = null;
            this.load();
        } catch (err) {
            console.error('[Prescriptions] Save error:', err);
            showToast('Failed to save prescription', 'danger');
        }
    }

    // ---- View / Edit / Cancel / Send ----

    async _viewRx(id) {
        try {
            const rx = await apiRequest(`/prescriptions/${id}`, { showLoader: false });
            if (!rx) return;

            const body = document.getElementById('prescriptionViewBody');
            body.innerHTML = `
                <div class="row">
                    <div class="col-md-6">
                        <h6 class="text-primary">Patient</h6>
                        <p class="mb-1"><strong>${rx.PatientName}</strong></p>
                        <h6 class="text-primary mt-3">Prescribing Provider</h6>
                        <p class="mb-1">${rx.ProviderName}</p>
                        <h6 class="text-primary mt-3">Date</h6>
                        <p class="mb-1">${rx.PrescribedDate}</p>
                    </div>
                    <div class="col-md-6">
                        <h6 class="text-primary">Status</h6>
                        <p class="mb-1">${PrescriptionStatuses.getBadgeHtml(rx.Status)}</p>
                        <h6 class="text-primary mt-3">Pharmacy</h6>
                        <p class="mb-1">${rx.PharmacyName || 'Not specified'}</p>
                        <small class="text-muted">${rx.PharmacyAddress || ''}</small>
                        ${rx.PharmacyPhone ? `<br><small>${rx.PharmacyPhone}</small>` : ''}
                    </div>
                </div>
                <hr>
                <div class="row">
                    <div class="col-md-6">
                        <h6 class="text-primary">Medication</h6>
                        <p class="mb-1"><strong>${rx.DrugName}</strong> (${rx.GenericName})</p>
                        <p class="mb-1">Strength: ${rx.Strength} | Form: ${rx.DosageFormName}</p>
                        ${rx.NDCCode ? `<small class="text-muted">NDC: ${rx.NDCCode}</small>` : ''}
                    </div>
                    <div class="col-md-6">
                        <h6 class="text-primary">Directions (SIG)</h6>
                        <p class="mb-1">${rx.DirectionsFreeText || ''}</p>
                        <p class="mb-1">Route: ${rx.RouteName} | Frequency: ${rx.FrequencyName}</p>
                    </div>
                </div>
                <hr>
                <div class="row">
                    <div class="col-md-3"><strong>Qty:</strong> ${rx.Quantity}</div>
                    <div class="col-md-3"><strong>Days Supply:</strong> ${rx.DaysSupply}</div>
                    <div class="col-md-3"><strong>Refills:</strong> ${rx.Refills}</div>
                    <div class="col-md-3"><strong>DAW:</strong> ${rx.DAW ? 'Yes' : 'No'}</div>
                </div>
                ${rx.IsControlledSubstance ? `<div class="alert alert-danger mt-3 mb-0"><i class="bi bi-shield-exclamation me-1"></i>Controlled Substance — DEA Schedule ${rx.DEASchedule}</div>` : ''}
                ${rx.DiagnosisCode ? `<p class="mt-2 mb-0"><strong>Diagnosis:</strong> ${rx.DiagnosisCode}</p>` : ''}
                ${rx.Notes ? `<p class="mt-2 mb-0"><strong>Notes:</strong> ${rx.Notes}</p>` : ''}
            `;

            const modal = new bootstrap.Modal(document.getElementById('prescriptionViewModal'));
            modal.show();
        } catch (err) {
            console.error('[Prescriptions] View error:', err);
        }
    }

    async _editRx(id) {
        try {
            const rx = await apiRequest(`/prescriptions/${id}`, { showLoader: false });
            if (!rx) return;
            if (rx.Status !== 0) {
                showToast('Only draft prescriptions can be edited', 'warning');
                return;
            }

            this._resetForm();
            this._editingId = id;

            // Populate form fields from the prescription data
            document.getElementById('rxPrescriptionId').value = rx.PrescriptionId;
            document.getElementById('rxPatientId').value = rx.PatientId;
            document.getElementById('rxPatientSearch').value = rx.PatientName || '';
            document.getElementById('rxProviderId').value = rx.ProviderId;
            document.getElementById('rxDrugSearch').value = [rx.DrugName, rx.GenericName ? `(${rx.GenericName})` : '', rx.Strength || ''].filter(Boolean).join(' ');
            document.getElementById('rxDrugName').value = rx.DrugName || '';
            document.getElementById('rxGenericName').value = rx.GenericName || '';
            document.getElementById('rxNDCCode').value = rx.NDCCode || '';
            document.getElementById('rxStrength').value = rx.Strength || '';
            document.getElementById('rxDosageForm').value = rx.DosageForm;
            document.getElementById('rxRouteHidden').value = rx.Route;
            document.getElementById('rxRoute').value = rx.Route;
            document.getElementById('rxFrequency').value = rx.Frequency;
            document.getElementById('rxDoseAmount').value = rx.DoseAmount || '1';
            document.getElementById('rxDoseUnit').value = rx.DoseUnit || 'tablet';
            document.getElementById('rxDirections').value = rx.DirectionsFreeText || '';
            document.getElementById('rxQuantity').value = rx.Quantity;
            document.getElementById('rxDaysSupply').value = rx.DaysSupply;
            document.getElementById('rxRefills').value = rx.Refills;
            document.getElementById('rxDAW').checked = rx.DAW;
            document.getElementById('rxPharmacySearch').value = rx.PharmacyName || '';
            document.getElementById('rxPharmacyPhone').value = rx.PharmacyPhone || '';
            document.getElementById('rxPharmacyAddress').value = rx.PharmacyAddress || '';
            document.getElementById('rxDiagnosisCode').value = rx.DiagnosisCode || '';
            document.getElementById('rxNotes').value = rx.Notes || '';

            if (rx.IsControlledSubstance) {
                document.getElementById('rxControlledArea').classList.remove('d-none');
                document.getElementById('rxScheduleText').textContent = rx.DEASchedule;
                document.getElementById('rxDEASchedule').value = rx.DEASchedule;
                document.getElementById('rxIsControlled').value = 'true';
            }

            document.getElementById('prescriptionModalTitle').innerHTML = '<i class="bi bi-pencil-square me-2"></i>Edit Prescription (Draft)';
            document.getElementById('btnSaveDraft').classList.remove('d-none');
            document.getElementById('btnSignAndSend').classList.remove('d-none');

            const modal = new bootstrap.Modal(document.getElementById('prescriptionModal'));
            modal.show();
        } catch (err) {
            console.error('[Prescriptions] Edit error:', err);
            showToast('Failed to load prescription for editing', 'danger');
        }
    }

    async _sendRx(id) {
        const confirmed = await this._confirmAction(
            'Sign & Send Prescription',
            'Are you sure you want to sign and send this prescription to the pharmacy? This action cannot be undone.',
            'btn-primary',
            'Sign & Send'
        );
        if (!confirmed) return;

        try {
            await apiRequest(`/prescriptions/${id}`, {
                method: 'PUT',
                body: { Status: 2 }
            });
            showToast('Prescription signed and sent', 'success');
            this.load();
        } catch (err) {
            showToast('Failed to send prescription', 'danger');
        }
    }

    async _cancelRx(id) {
        const confirmed = await this._confirmAction(
            'Cancel Prescription',
            'Are you sure you want to cancel this prescription? This action cannot be undone.',
            'btn-danger',
            'Cancel Prescription'
        );
        if (!confirmed) return;

        try {
            await apiRequest(`/prescriptions/${id}/cancel`, { method: 'POST' });
            showToast('Prescription cancelled', 'success');
            this.load();
        } catch (err) {
            showToast('Failed to cancel prescription', 'danger');
        }
    }

    // ---- Confirm modal helper ----

    _confirmAction(title, message, btnClass, btnText) {
        return new Promise((resolve) => {
            const modalEl = document.getElementById('rxConfirmModal');
            const titleEl = document.getElementById('rxConfirmTitle');
            const msgEl = document.getElementById('rxConfirmMessage');
            const btnEl = document.getElementById('rxConfirmBtn');

            titleEl.textContent = title;
            msgEl.textContent = message;
            btnEl.className = `btn btn-sm ${btnClass}`;
            btnEl.textContent = btnText;

            // Clean up any previous listeners
            const newBtn = btnEl.cloneNode(true);
            btnEl.parentNode.replaceChild(newBtn, btnEl);
            newBtn.id = 'rxConfirmBtn';

            const modal = new bootstrap.Modal(modalEl);

            newBtn.addEventListener('click', () => {
                modal.hide();
                resolve(true);
            });

            modalEl.addEventListener('hidden.bs.modal', function handler() {
                modalEl.removeEventListener('hidden.bs.modal', handler);
                resolve(false);
            });

            modal.show();
        });
    }

    // ---- Dropdown helpers ----

    _showDropdown(dropdownId, html, onClick) {
        const dd = document.getElementById(dropdownId);
        if (!dd) return;
        if (!html || html.trim() === '') {
            dd.innerHTML = '<span class="dropdown-item text-muted">No results found</span>';
        } else {
            dd.innerHTML = html;
        }
        dd.classList.add('show');

        // Bind click handlers
        dd.querySelectorAll('.dropdown-item').forEach(item => {
            item.addEventListener('mousedown', (e) => {
                e.preventDefault();
                if (onClick) onClick(item);
            });
        });
    }

    _hideDropdown(dropdownId) {
        const dd = document.getElementById(dropdownId);
        if (dd) dd.classList.remove('show');
    }

    _clearFilters() {
        const ids = ['rxFilterSearch', 'rxFilterStartDate', 'rxFilterEndDate', 'rxFilterStatus'];
        ids.forEach(id => {
            const el = document.getElementById(id);
            if (el) el.value = '';
        });
        this.load();
    }

    _debounce(key, fn, ms) {
        if (this._debounceTimers[key]) clearTimeout(this._debounceTimers[key]);
        this._debounceTimers[key] = setTimeout(fn, ms);
    }
}

// Auto-initialize
document.addEventListener('DOMContentLoaded', () => {
    const mod = new PrescriptionsModule();
    window._prescriptionsModule = mod;
    mod.init();
});
