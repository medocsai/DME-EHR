/**
 * BillingModule - Handles billing, charges, claims, payments, and AR aging
 *
 * Features:
 * - Claims & Charges with summary cards, filters, and pagination
 * - Claims creation and tracking
 * - Payments listing
 * - A/R Aging reports
 * - Patient search for claims
 */
class BillingModule {
    constructor(options = {}) {
        this.api = options.api || null;
        this.eventBus = options.eventBus || null;

        // State
        this.selectedClaimPatient = null;
        this.charges = [];
        this.claims = [];
        this.payments = [];
        this.providers = [];

        // Filter state
        this.claimsFilters = { patientId: null, patientName: '', providerId: null, providerName: '', payerSearch: '', statuses: [], dateFrom: null, dateTo: null };
        this.chargesFilters = { patientId: null, patientName: '', providerId: null, providerName: '', statuses: [], dateFrom: null, dateTo: null };
        this.paymentsFilters = { patientId: null, patientName: '', dateFrom: null, dateTo: null };

        // Pagination state
        this.claimsPage = { current: 1, pageSize: 25, totalCount: 0 };
        this.chargesPage = { current: 1, pageSize: 25, totalCount: 0 };

        // DOM references
        this.containers = {
            chargesTable: '#chargesTable tbody',
            claimsTable: '#claimsTable tbody',
            paymentsTable: '#paymentsTable tbody',
            arAgingDetails: '#arAgingDetails',
            claimPatientSearch: '#claimPatientSearch',
            claimPatientSearchResults: '#claimPatientSearchResults',
            claimSelectedPatient: '#claimSelectedPatient',
            claimPatientId: '#claimPatientId',
            claimInsuranceSelect: '#claimInsuranceSelect',
            claimChargesList: '#claimChargesList',
            newClaimForm: '#newClaimForm',
            newClaimModal: '#newClaimModal'
        };

        // Bound handlers for cleanup
        this._boundHandlers = {};
    }

    /**
     * Initialize the module
     */
    async init() {
        this._bindEvents();
        this._initClaimPatientAutocomplete();
        this._setDefaultDateRanges();
        await this._loadProviders();
        this._initFilters();

        // Auto-refresh payments when a payment is created
        window.addEventListener('payment:created', () => {
            this.loadPayments();
        });
    }

    /**
     * Load all billing data
     */
    async load() {
        try {
            await Promise.all([
                this.loadClaimsPaged(),
                this.loadChargesPaged(),
                this.loadPayments(),
                this.loadARAging()
            ]);
        } catch (error) {
            console.error('Error loading billing data:', error);
            this._showToast('Error', 'Failed to load some billing data', 'error');
        }
    }

    /**
     * Load paged claims with filters
     */
    async loadClaimsPaged() {
        try {
            const params = new URLSearchParams();
            if (this.claimsFilters.patientId) params.set('patientId', this.claimsFilters.patientId);
            if (this.claimsFilters.providerId) params.set('providerId', this.claimsFilters.providerId);
            if (this.claimsFilters.payerSearch) params.set('payerSearch', this.claimsFilters.payerSearch);
            this.claimsFilters.statuses.forEach(s => params.append('statuses', s));
            if (this.claimsFilters.dateFrom) params.set('dateFrom', this.claimsFilters.dateFrom);
            if (this.claimsFilters.dateTo) params.set('dateTo', this.claimsFilters.dateTo);
            params.set('page', this.claimsPage.current);
            params.set('pageSize', this.claimsPage.pageSize);

            const result = await this._apiGet(`/billing/claims/paged?${params}`);
            this.claims = result.Items ?? result.items ?? [];
            this.claimsPage.totalCount = result.TotalCount ?? result.totalCount ?? this.claims.length;
            this._renderClaimsSummary(result);
            this._renderClaims();
            this._renderPagination('claims');

            // Update tab badge
            const badge = document.getElementById('claimsTabCount');
            if (badge) badge.textContent = this.claimsPage.totalCount || '';
        } catch (error) {
            console.error('Load claims paged error:', error);
        }
    }

    /**
     * Load paged charges with filters
     */
    async loadChargesPaged() {
        try {
            const params = new URLSearchParams();
            if (this.chargesFilters.patientId) params.set('patientId', this.chargesFilters.patientId);
            if (this.chargesFilters.providerId) params.set('providerId', this.chargesFilters.providerId);
            this.chargesFilters.statuses.forEach(s => params.append('statuses', s));
            if (this.chargesFilters.dateFrom) params.set('dateFrom', this.chargesFilters.dateFrom);
            if (this.chargesFilters.dateTo) params.set('dateTo', this.chargesFilters.dateTo);
            params.set('page', this.chargesPage.current);
            params.set('pageSize', this.chargesPage.pageSize);

            const result = await this._apiGet(`/billing/get-charges/paged?${params}`);
            this.charges = result.Items ?? result.items ?? [];
            this.chargesPage.totalCount = result.TotalCount ?? result.totalCount ?? this.charges.length;
            this._renderChargesSummary(result);
            this._renderCharges();
            this._renderPagination('charges');

            // Update tab badge
            const badge = document.getElementById('chargesTabCount');
            if (badge) badge.textContent = this.chargesPage.totalCount || '';
        } catch (error) {
            console.error('Load charges paged error:', error);
        }
    }

    /**
     * Load payments
     */
    async loadPayments() {
        try {
            let params = [];
            if (this.paymentsFilters.patientId) params.push(`patientId=${this.paymentsFilters.patientId}`);
            if (this.paymentsFilters.dateFrom) params.push(`startDate=${this.paymentsFilters.dateFrom}`);
            if (this.paymentsFilters.dateTo) params.push(`endDate=${this.paymentsFilters.dateTo}`);
            const query = params.length ? '?' + params.join('&') : '';
            const payments = await this._apiGet(`/payments${query}`);
            this.payments = payments || [];
            this._renderPayments();
        } catch (error) {
            console.error('Load payments error:', error);
        }
    }

    /**
     * Load A/R Aging data
     */
    async loadARAging() {
        const details = document.getElementById('arAgingDetails');
        if (!details) return;

        try {
            const aging = await this._apiGet('/billing/ar-aging');
            if (aging) {
                details.innerHTML = this._buildAgingHtml(aging);
            } else {
                details.innerHTML = this._buildAgingHtml({});
            }
        } catch (error) {
            console.error('Load AR aging error:', error);
            details.innerHTML = '<div class="alert alert-danger">Failed to load AR aging data</div>';
        }
    }

    /**
     * Create a new claim
     */
    async createClaim(formData) {
        if (!this.selectedClaimPatient) {
            this._showToast('Error', 'Please select a patient', 'error');
            return false;
        }

        const selectedCharges = [];
        document.querySelectorAll('#claimChargesList input[type="checkbox"]:checked').forEach(cb => {
            selectedCharges.push(parseInt(cb.value));
        });

        if (selectedCharges.length === 0) {
            this._showToast('Error', 'Please select at least one charge to include', 'error');
            return false;
        }

        try {
            await this._apiPost('/billing/claims', {
                PatientId: this.selectedClaimPatient.PatientId,
                InsuranceId: parseInt(formData.get('InsuranceId')),
                ServiceDateFrom: formData.get('ServiceDateFrom'),
                ChargeIds: selectedCharges
            });

            this._showToast('Success', 'Claim created successfully');
            this._closeModal(this.containers.newClaimModal);
            this.clearClaimPatientSelection();
            await this.loadClaimsPaged();
            return true;
        } catch (error) {
            console.error('Create claim error:', error);
            this._showToast('Error', error.message || 'Failed to create claim', 'error');
            return false;
        }
    }

    /**
     * Select a patient for claim creation
     */
    async selectClaimPatient(patient) {
        this.selectedClaimPatient = patient;

        const searchInput = document.querySelector(this.containers.claimPatientSearch);
        const resultsDiv = document.querySelector(this.containers.claimPatientSearchResults);
        const selectedDiv = document.querySelector(this.containers.claimSelectedPatient);
        const hiddenInput = document.querySelector(this.containers.claimPatientId);
        const insuranceSelect = document.querySelector(this.containers.claimInsuranceSelect);
        const chargesList = document.querySelector(this.containers.claimChargesList);

        // Hide search, show selected patient
        if (searchInput) searchInput.classList.add('d-none');
        if (resultsDiv) resultsDiv.classList.add('d-none');

        if (hiddenInput) hiddenInput.value = patient.PatientId;

        if (selectedDiv) {
            selectedDiv.classList.remove('d-none');
            selectedDiv.innerHTML = `
                <div class="d-flex align-items-center justify-content-between p-2 bg-light rounded">
                    <div>
                        <strong>${this._escape(patient.FirstName)} ${this._escape(patient.LastName)}</strong>
                        <span class="text-muted ms-2">MRN: ${this._escape(patient.MRN || 'N/A')}</span>
                    </div>
                    <button type="button" class="btn btn-sm btn-outline-secondary" data-action="clear-claim-patient">
                        <i class="bi bi-x"></i> Change
                    </button>
                </div>
            `;
        }

        // Load patient's insurances
        try {
            const insurances = await this._apiGet(`/patients/${patient.PatientId}/insurances`);
            if (insuranceSelect && insurances?.length) {
                insuranceSelect.innerHTML = '<option value="">Select Insurance</option>' +
                    insurances.map(ins => `<option value="${ins.InsuranceId}">${this._escape(ins.PayerName)} - ${this._escape(ins.PlanName || ins.MemberId || '')}</option>`).join('');
                insuranceSelect.closest('.mb-3')?.classList.remove('d-none');
            }
        } catch (error) {
            console.error('Load patient insurances error:', error);
        }

        // Load patient's unbilled charges
        try {
            const charges = await this._apiGet(`/billing/get-charges?patientId=${patient.PatientId}&status=0`);
            if (chargesList) {
                if (charges?.length) {
                    chargesList.innerHTML = charges.map(c => `
                        <div class="form-check border-bottom py-2">
                            <input class="form-check-input" type="checkbox" value="${c.ChargeId}" id="charge_${c.ChargeId}" checked>
                            <label class="form-check-label w-100" for="charge_${c.ChargeId}">
                                <div class="d-flex justify-content-between">
                                    <span><strong>${this._escape(c.CPTCode || c.CptCode)}</strong> - ${this._escape(c.CPTDescription || c.Description || '')}</span>
                                    <span>${this._formatCurrency(c.ChargeAmount)}</span>
                                </div>
                                <small class="text-muted">${this._formatDate(c.ServiceDate)} | Units: ${c.Units}</small>
                            </label>
                        </div>
                    `).join('');
                } else {
                    chargesList.innerHTML = '<div class="text-muted text-center py-3">No unbilled charges found for this patient</div>';
                }
                chargesList.closest('.mb-3')?.classList.remove('d-none');
            }
        } catch (error) {
            console.error('Load patient charges error:', error);
        }
    }

    clearClaimPatientSelection() {
        this.selectedClaimPatient = null;

        const searchInput = document.querySelector(this.containers.claimPatientSearch);
        const resultsDiv = document.querySelector(this.containers.claimPatientSearchResults);
        const selectedDiv = document.querySelector(this.containers.claimSelectedPatient);
        const hiddenInput = document.querySelector(this.containers.claimPatientId);

        if (searchInput) {
            searchInput.value = '';
            searchInput.classList.remove('d-none');
        }
        if (resultsDiv) {
            resultsDiv.innerHTML = '';
            resultsDiv.classList.add('d-none');
        }
        if (selectedDiv) {
            selectedDiv.innerHTML = '';
            selectedDiv.classList.add('d-none');
        }
        if (hiddenInput) hiddenInput.value = '';

        // Hide insurance and charges sections
        const insuranceSelect = document.querySelector(this.containers.claimInsuranceSelect);
        if (insuranceSelect) {
            insuranceSelect.innerHTML = '<option value="">Select Insurance</option>';
            insuranceSelect.closest('.mb-3')?.classList.add('d-none');
        }
        const chargesList = document.querySelector(this.containers.claimChargesList);
        if (chargesList) {
            chargesList.innerHTML = '';
            chargesList.closest('.mb-3')?.classList.add('d-none');
        }
    }

    // ========================================
    // Private Methods - Rendering
    // ========================================

    _renderClaimsSummary(result) {
        const el = (id, val) => { const e = document.getElementById(id); if (e) e.textContent = val; };
        el('claimsTotalCount', result.TotalCount ?? result.totalCount ?? 0);
        el('claimsTotalBilled', this._formatCurrency(result.TotalBilled ?? result.totalBilled ?? 0));
        el('claimsTotalPaid', this._formatCurrency(result.TotalPaid ?? result.totalPaid ?? 0));
        el('claimsTotalBalance', this._formatCurrency(result.TotalOutstanding ?? result.totalOutstanding ?? 0));
    }

    _renderChargesSummary(result) {
        const el = (id, val) => { const e = document.getElementById(id); if (e) e.textContent = val; };
        el('chargesTotalCount', result.TotalCount ?? result.totalCount ?? 0);
        el('chargesTotalAmount', this._formatCurrency(result.TotalBilled ?? result.totalBilled ?? 0));
        el('chargesBilledCount', result.BilledCount ?? result.billedCount ?? 0);
        el('chargesPendingCount', result.PendingCount ?? result.pendingCount ?? 0);
    }

    _renderCharges() {
        const tbody = document.querySelector(this.containers.chargesTable);
        if (!tbody) return;

        tbody.innerHTML = this.charges.length ? this.charges.map(c => `
            <tr>
                <td>${this._formatDate(c.ServiceDate ?? c.serviceDate)}</td>
                <td>${this._escape(c.PatientName ?? c.patientName)}</td>
                <td>${this._escape(c.ProviderName ?? c.providerName ?? '')}</td>
                <td><strong>${this._escape(c.CPTCode ?? c.CptCode ?? c.cptCode)}</strong></td>
                <td>${c.Units ?? c.units ?? 1}</td>
                <td>${this._formatCurrency(c.ChargeAmount ?? c.chargeAmount)}</td>
                <td>${this._formatCurrency(c.PaidAmount ?? c.paidAmount ?? 0)}</td>
                <td>${this._formatCurrency(c.Balance ?? c.balance ?? 0)}</td>
                <td>${this._getStatusBadge(c.Status ?? c.status ?? 0, 'charge')}</td>
            </tr>
        `).join('') : '<tr><td colspan="9" class="text-center text-muted py-4">No charges found</td></tr>';
    }

    _renderClaims() {
        const tbody = document.querySelector(this.containers.claimsTable);
        if (!tbody) return;

        tbody.innerHTML = this.claims.length ? this.claims.map(c => {
            const isLocked = !!(c.IsSubmitLocked ?? c.isSubmitLocked);
            const windowEnd = c.AmendmentWindowEndsAt || c.amendmentWindowEndsAt;
            const lockTitle = isLocked && windowEnd
                ? `Submission locked until ${new Date(windowEnd).toLocaleString()} — amendment window is still open.`
                : '';
            const lockedPill = isLocked
                ? `<span class="badge bg-warning text-dark ms-1" title="${this._escape(lockTitle)}"><i class="bi bi-lock-fill me-1"></i>Locked</span>`
                : '';
            return `
            <tr>
                <td><strong>${this._escape(c.ClaimNumber ?? c.claimNumber ?? '-')}</strong></td>
                <td>${this._escape(c.PatientName ?? c.patientName)}</td>
                <td>${this._escape(c.ProviderName ?? c.providerName ?? '')}</td>
                <td>${this._escape(c.PayerName ?? c.payerName)}</td>
                <td>${this._formatDate(c.ServiceDateFrom ?? c.serviceDateFrom)}</td>
                <td>${this._formatCurrency(c.TotalCharged ?? c.totalCharged)}</td>
                <td>${this._formatCurrency(c.TotalPaid ?? c.totalPaid)}</td>
                <td>${this._formatCurrency(c.Balance ?? c.balance ?? 0)}</td>
                <td>${this._getStatusBadge(c.Status ?? c.status ?? 0, 'claim')}${lockedPill}</td>
                <td>
                    <button class="btn btn-outline-primary" data-action="view-claim" data-claim-id="${c.ClaimId ?? c.claimId}" title="Open Claim">
                        <i class="bi bi-pencil-square"></i>
                    </button>
                </td>
            </tr>
        `;}).join('') : '<tr><td colspan="10" class="text-center text-muted py-4">No claims found</td></tr>';
    }

    /**
     * View claim detail — loads CMS 1500 form data into the detail modal
     */
    async viewClaimDetail(claimId) {
        // Use ClaimFormModule for full editable CMS-1500 form
        if (window.openClaimDetail) {
            window.openClaimDetail(claimId);
        }
    }

    /**
     * Mark claim as Ready
     */
    async markClaimReady(claimId) {
        try {
            await this._apiPost(`/billing/claims/${claimId}/ready`, {});
            this._showToast('Success', 'Claim marked as Ready');
            await this.loadClaimsPaged();
        } catch (error) {
            console.error('Mark claim ready error:', error);
            this._showToast('Error', error.message || 'Failed to mark claim ready', 'error');
        }
    }

    _renderClaimDetailModal(claim) {
        // Build diagnosis codes display
        let diagHtml = '';
        if (claim.DiagnosisCodes) {
            try {
                const codes = JSON.parse(claim.DiagnosisCodes);
                const letters = 'ABCDEFGHIJKL';
                diagHtml = codes.map((c, i) => `<span class="badge bg-light text-dark me-1">${letters[i] || ''}. ${this._escape(c)}</span>`).join('');
            } catch { diagHtml = this._escape(claim.DiagnosisCodes); }
        }

        // Build charge lines table
        const chargeRows = (claim.ChargeLines || []).map(ch => `
            <tr>
                <td>${this._formatDate(ch.ServiceDate)}</td>
                <td><strong>${this._escape(ch.CptCode)}</strong></td>
                <td>${this._escape(ch.CptDescription || '')}</td>
                <td>${[ch.Modifier1, ch.Modifier2, ch.Modifier3, ch.Modifier4].filter(Boolean).join(', ') || '-'}</td>
                <td>${this._escape(ch.IcdPointers || '-')}</td>
                <td class="text-end">${this._formatCurrency(ch.ChargeAmount)}</td>
                <td class="text-center">${ch.Units || 1}</td>
            </tr>
        `).join('') || '<tr><td colspan="7" class="text-center text-muted">No charge lines</td></tr>';

        const statusNames = { 0: 'Draft', 1: 'Ready', 2: 'Submitted', 3: 'Acknowledged', 4: 'Pending', 5: 'Paid', 6: 'Partially Paid', 7: 'Denied', 8: 'Rejected' };
        const typeNames = { 0: 'Professional (CMS 1500)', 1: 'Institutional (UB04)', 2: 'Dental' };

        const html = `
        <div class="modal fade" id="claimDetailModal" tabindex="-1">
            <div class="modal-dialog modal-xl modal-dialog-scrollable">
                <div class="modal-content">
                    <div class="modal-header bg-primary text-white">
                        <h5 class="modal-title"><i class="bi bi-file-medical me-2"></i>CMS-1500 Claim Detail — ${this._escape(claim.ClaimNumber)}</h5>
                        <button type="button" class="btn-close btn-close-white" data-bs-dismiss="modal"></button>
                    </div>
                    <div class="modal-body">
                        <div class="row g-3 mb-3">
                            <div class="col-md-3"><label class="text-muted small">Status</label><div>${this._getStatusBadge(claim.Status, 'claim')}</div></div>
                            <div class="col-md-3"><label class="text-muted small">Type</label><div>${typeNames[claim.Type] || 'Unknown'}</div></div>
                            <div class="col-md-3"><label class="text-muted small">Service Date</label><div>${this._formatDate(claim.ServiceDateFrom)}</div></div>
                            <div class="col-md-3"><label class="text-muted small">Created</label><div>${this._formatDate(claim.CreatedAt)}</div></div>
                        </div>
                        <hr>
                        <div class="row g-3 mb-3">
                            <div class="col-md-6">
                                <h6 class="text-primary"><i class="bi bi-person me-1"></i>Patient Information (Box 2-5)</h6>
                                <div class="ps-2">
                                    <div><strong>${this._escape(claim.PatientLastName)}, ${this._escape(claim.PatientFirstName)}</strong></div>
                                    <div>${this._escape(claim.PatientAddress || '')}, ${this._escape(claim.PatientCity || '')}, ${this._escape(claim.PatientState || '')} ${this._escape(claim.PatientZip || '')}</div>
                                    <div>DOB: ${this._formatDate(claim.PatientDob)} | Gender: ${this._escape(claim.PatientGender || '-')} | MRN: ${this._escape(claim.PatientMrn || '-')}</div>
                                </div>
                            </div>
                            <div class="col-md-6">
                                <h6 class="text-primary"><i class="bi bi-shield-check me-1"></i>Insured Information (Box 4-11)</h6>
                                <div class="ps-2">
                                    <div><strong>${this._escape(claim.InsuredName || '-')}</strong> (${this._escape(claim.SubscriberRelationship || 'Self')})</div>
                                    <div>${this._escape(claim.InsuredAddress || '')}, ${this._escape(claim.InsuredCity || '')}, ${this._escape(claim.InsuredState || '')} ${this._escape(claim.InsuredZip || '')}</div>
                                    <div>Policy: ${this._escape(claim.InsuredPolicyNumber || '-')} | Group: ${this._escape(claim.InsuredGroupNumber || '-')}</div>
                                    <div>Payer: <strong>${this._escape(claim.PayerName || '-')}</strong></div>
                                </div>
                            </div>
                        </div>
                        <div class="row g-3 mb-3">
                            <div class="col-md-6"><label class="text-muted small">Box 12 — Patient Signature</label><div>${claim.PatientSignatureOnFile ? '<span class="badge bg-success">SIGNATURE ON FILE</span>' : '-'}</div></div>
                            <div class="col-md-6"><label class="text-muted small">Box 13 — Insured Signature</label><div>${claim.InsuredSignatureOnFile ? '<span class="badge bg-success">SIGNATURE ON FILE</span>' : '-'}</div></div>
                        </div>
                        <div class="row g-3 mb-3">
                            <div class="col-md-6"><label class="text-muted small">Box 17 — Referring Provider</label><div>${this._escape(claim.ReferringProviderName || '-')} ${claim.ReferringProviderNpi ? '(NPI: ' + this._escape(claim.ReferringProviderNpi) + ')' : ''}</div></div>
                            <div class="col-md-6"><label class="text-muted small">Box 23 — Prior Authorization</label><div>${this._escape(claim.PriorAuthorizationNumber || '-')}</div></div>
                        </div>
                        <div class="mb-3"><label class="text-muted small">Box 21 — Diagnosis Codes (ICD-10)</label><div>${diagHtml || '<span class="text-muted">None</span>'}</div></div>
                        <hr>
                        <h6 class="text-primary"><i class="bi bi-list-columns me-1"></i>Box 24 — Service Lines</h6>
                        <div class="table-responsive">
                            <table class="table table-sm table-bordered mb-3">
                                <thead class="table-light"><tr><th>Date</th><th>CPT</th><th>Description</th><th>Modifiers</th><th>Dx Ptr</th><th class="text-end">Amount</th><th class="text-center">Units</th></tr></thead>
                                <tbody>${chargeRows}</tbody>
                            </table>
                        </div>
                        <div class="row g-3 mb-3">
                            <div class="col-md-3"><label class="text-muted small">Box 25 — Federal Tax ID</label><div>${this._escape(claim.FederalTaxId || '-')}</div></div>
                            <div class="col-md-3"><label class="text-muted small">Box 26 — Patient Account</label><div>${this._escape(claim.PatientAccountNumber || '-')}</div></div>
                            <div class="col-md-3"><label class="text-muted small">Box 27 — Accept Assignment</label><div>${claim.AcceptAssignment ? 'YES' : 'NO'}</div></div>
                            <div class="col-md-3"><label class="text-muted small">Box 24b — POS Code</label><div>${this._escape(claim.PlaceOfServiceCode || '-')}</div></div>
                        </div>
                        <hr>
                        <div class="row g-3 mb-3">
                            <div class="col-md-4">
                                <h6 class="text-primary">Box 31 — Rendering Provider</h6>
                                <div>${this._escape(claim.RenderingProviderName || '-')}</div>
                                <div class="text-muted small">NPI: ${this._escape(claim.RenderingProviderNpi || '-')}</div>
                            </div>
                            <div class="col-md-4">
                                <h6 class="text-primary">Box 32 — Facility</h6>
                                <div>${this._escape(claim.FacilityName || '-')}</div>
                                <div class="text-muted small">${this._escape(claim.FacilityAddress || '')}</div>
                                <div class="text-muted small">NPI: ${this._escape(claim.FacilityNpi || '-')}</div>
                            </div>
                            <div class="col-md-4">
                                <h6 class="text-primary">Box 33 — Billing Provider</h6>
                                <div>${this._escape(claim.BillingProviderName || '-')}</div>
                                <div class="text-muted small">${this._escape(claim.BillingProviderAddress || '')}</div>
                                <div class="text-muted small">NPI: ${this._escape(claim.BillingProviderNpi || '-')} | Tax: ${this._escape(claim.BillingProviderTaxonomy || '-')}</div>
                            </div>
                        </div>
                        <hr>
                        <div class="row g-3">
                            <div class="col-md-3"><label class="text-muted small">Total Charged</label><div class="fw-bold fs-5">${this._formatCurrency(claim.TotalCharged)}</div></div>
                            <div class="col-md-3"><label class="text-muted small">Total Paid</label><div class="fw-bold fs-5 text-success">${this._formatCurrency(claim.TotalPaid)}</div></div>
                            <div class="col-md-3"><label class="text-muted small">Adjustment</label><div>${this._formatCurrency(claim.TotalAdjustment)}</div></div>
                            <div class="col-md-3"><label class="text-muted small">Patient Responsibility</label><div>${this._formatCurrency(claim.PatientResponsibility)}</div></div>
                        </div>
                        ${claim.DenialReason ? `<div class="alert alert-danger mt-3"><strong>Denial:</strong> ${this._escape(claim.DenialReason)} (${this._escape(claim.DenialReasonCode || '')})</div>` : ''}
                    </div>
                    <div class="modal-footer">
                        <a href="/api/billing/claims/${claim.ClaimId}/pdf" target="_blank" class="btn btn-outline-secondary"><i class="bi bi-file-pdf me-1"></i>Download PDF</a>
                        <button type="button" class="btn btn-secondary" data-bs-dismiss="modal">Close</button>
                    </div>
                </div>
            </div>
        </div>`;

        // Remove existing modal if present, then inject and show
        const existing = document.getElementById('claimDetailModal');
        if (existing) existing.remove();
        document.body.insertAdjacentHTML('beforeend', html);
        const modal = new bootstrap.Modal(document.getElementById('claimDetailModal'));
        modal.show();
        // Cleanup modal element on hide
        document.getElementById('claimDetailModal').addEventListener('hidden.bs.modal', function() { this.remove(); });
    }

    _renderPayments() {
        const tbody = document.querySelector(this.containers.paymentsTable);
        if (!tbody) return;

        tbody.innerHTML = this.payments.length ? this.payments.map(p => `
            <tr>
                <td>${this._formatDate(p.PaymentDate)}</td>
                <td>${this._escape(p.PatientName)}</td>
                <td>${p.TypeName || '-'}</td>
                <td>${p.MethodName || '-'}</td>
                <td>${this._formatCurrency(p.Amount)}</td>
                <td>${this._getStatusBadge(p.Status ?? 1, 'payment')}</td>
            </tr>
        `).join('') : '<tr><td colspan="6" class="text-center text-muted py-4">No payments found</td></tr>';
    }

    _buildAgingHtml(aging) {
        return `
            <div class="aging-bucket"><span>0-30 days</span><span>${this._formatCurrency(aging.Current || 0)}</span></div>
            <div class="aging-bucket"><span>31-60 days</span><span>${this._formatCurrency(aging.Days31To60 || 0)}</span></div>
            <div class="aging-bucket"><span>61-90 days</span><span>${this._formatCurrency(aging.Days61To90 || 0)}</span></div>
            <div class="aging-bucket"><span>91-120 days</span><span>${this._formatCurrency(aging.Days91To120 || 0)}</span></div>
            <div class="aging-bucket"><span>120+ days</span><span>${this._formatCurrency(aging.Over120Days || 0)}</span></div>
            <div class="aging-bucket"><span><strong>Total</strong></span><span><strong>${this._formatCurrency(aging.Total || 0)}</strong></span></div>
        `;
    }

    _renderPagination(type) {
        const page = type === 'claims' ? this.claimsPage : this.chargesPage;
        const container = document.getElementById(`${type}Pagination`);
        if (!container) return;

        const totalPages = Math.ceil(page.totalCount / page.pageSize);

        if (totalPages <= 1) {
            container.style.display = 'none';
            return;
        }

        container.style.display = 'flex';
        const start = (page.current - 1) * page.pageSize + 1;
        const end = Math.min(page.current * page.pageSize, page.totalCount);

        let pagesHtml = '';
        // Previous
        pagesHtml += `<button class="billing-page-btn" data-action="page-${type}" data-page="${page.current - 1}" ${page.current === 1 ? 'disabled' : ''}><i class="bi bi-chevron-left"></i></button>`;

        // Page numbers with ellipsis
        const maxVisible = 7;
        let startPage = Math.max(1, page.current - Math.floor(maxVisible / 2));
        let endPage = Math.min(totalPages, startPage + maxVisible - 1);
        if (endPage - startPage < maxVisible - 1) startPage = Math.max(1, endPage - maxVisible + 1);

        if (startPage > 1) {
            pagesHtml += `<button class="billing-page-btn" data-action="page-${type}" data-page="1">1</button>`;
            if (startPage > 2) pagesHtml += `<span class="px-1 text-muted">...</span>`;
        }
        for (let i = startPage; i <= endPage; i++) {
            pagesHtml += `<button class="billing-page-btn ${i === page.current ? 'active' : ''}" data-action="page-${type}" data-page="${i}">${i}</button>`;
        }
        if (endPage < totalPages) {
            if (endPage < totalPages - 1) pagesHtml += `<span class="px-1 text-muted">...</span>`;
            pagesHtml += `<button class="billing-page-btn" data-action="page-${type}" data-page="${totalPages}">${totalPages}</button>`;
        }

        // Next
        pagesHtml += `<button class="billing-page-btn" data-action="page-${type}" data-page="${page.current + 1}" ${page.current === totalPages ? 'disabled' : ''}><i class="bi bi-chevron-right"></i></button>`;

        container.innerHTML = `
            <div class="billing-pagination-info">Showing ${start}-${end} of ${page.totalCount}</div>
            <div class="billing-pagination-pages">${pagesHtml}</div>
        `;
    }

    _getStatusBadge(status, type) {
        const statusMaps = {
            charge: {
                0: { class: 'bg-warning', label: 'Pending' },
                1: { class: 'bg-info', label: 'Billed' },
                2: { class: 'bg-success', label: 'Paid' },
                3: { class: 'bg-danger', label: 'Denied' },
                4: { class: 'bg-secondary', label: 'Written Off' }
            },
            claim: {
                0: { class: 'bg-warning', label: 'Draft' },
                1: { class: 'bg-info', label: 'Ready' },
                2: { class: 'bg-primary', label: 'Submitted' },
                3: { class: 'bg-secondary', label: 'Acknowledged' },
                4: { class: 'bg-secondary', label: 'Pending' },
                5: { class: 'bg-success', label: 'Paid' },
                6: { class: 'bg-success-subtle text-success', label: 'Partially Paid' },
                7: { class: 'bg-danger', label: 'Denied' },
                8: { class: 'bg-danger-subtle text-danger', label: 'Rejected' }
            },
            payment: {
                0: { class: 'bg-warning', label: 'Pending' },
                1: { class: 'bg-success', label: 'Completed' },
                2: { class: 'bg-danger', label: 'Failed' },
                3: { class: 'bg-info', label: 'Refunded' },
                4: { class: 'bg-secondary', label: 'Voided' }
            }
        };

        const map = statusMaps[type] || statusMaps.charge;
        const statusInfo = map[status] || { class: 'bg-secondary', label: 'Unknown' };
        return `<span class="badge ${statusInfo.class}">${statusInfo.label}</span>`;
    }

    // ========================================
    // Private Methods - Event Handling
    // ========================================

    _bindEvents() {
        // Form submission
        const form = document.querySelector(this.containers.newClaimForm);
        if (form) {
            this._boundHandlers.formSubmit = (e) => this._handleFormSubmit(e);
            form.addEventListener('submit', this._boundHandlers.formSubmit);
        }

        // Modal reset
        const modal = document.querySelector(this.containers.newClaimModal);
        if (modal) {
            this._boundHandlers.modalShow = () => this._handleModalShow();
            modal.addEventListener('show.bs.modal', this._boundHandlers.modalShow);
        }

        // Delegated click events
        this._boundHandlers.docClick = (e) => this._handleDocumentClick(e);
        document.addEventListener('click', this._boundHandlers.docClick);
    }

    _unbindEvents() {
        const form = document.querySelector(this.containers.newClaimForm);
        if (form && this._boundHandlers.formSubmit) {
            form.removeEventListener('submit', this._boundHandlers.formSubmit);
        }

        const modal = document.querySelector(this.containers.newClaimModal);
        if (modal && this._boundHandlers.modalShow) {
            modal.removeEventListener('show.bs.modal', this._boundHandlers.modalShow);
        }

        if (this._boundHandlers.docClick) {
            document.removeEventListener('click', this._boundHandlers.docClick);
        }
    }

    _handleDocumentClick(e) {
        const target = e.target.closest('[data-action]');
        if (!target) return;

        const action = target.getAttribute('data-action');

        if (action === 'clear-claim-patient') {
            this.clearClaimPatientSelection();
        } else if (action === 'view-claim') {
            const claimId = target.getAttribute('data-claim-id');
            if (claimId) this.viewClaimDetail(parseInt(claimId));
        } else if (action === 'ready-claim') {
            const claimId = target.getAttribute('data-claim-id');
            if (claimId) this.markClaimReady(parseInt(claimId));
        } else if (action === 'page-claims') {
            const page = parseInt(target.getAttribute('data-page'));
            if (page && page !== this.claimsPage.current) {
                this.claimsPage.current = page;
                this.loadClaimsPaged();
            }
        } else if (action === 'page-charges') {
            const page = parseInt(target.getAttribute('data-page'));
            if (page && page !== this.chargesPage.current) {
                this.chargesPage.current = page;
                this.loadChargesPaged();
            }
        }
    }

    async _handleFormSubmit(e) {
        e.preventDefault();
        const formData = new FormData(e.target);
        await this.createClaim(formData);
    }

    _handleModalShow() {
        const form = document.querySelector(this.containers.newClaimForm);
        if (form) form.reset();
        this.clearClaimPatientSelection();
    }

    // ========================================
    // Private Methods - Filters
    // ========================================

    _setDefaultDateRanges() {
        const today = new Date();
        const past90 = new Date();
        past90.setDate(today.getDate() - 90);

        const fmt = d => d.toISOString().split('T')[0];

        ['claims', 'charges'].forEach(type => {
            const fromEl = document.getElementById(`${type}DateFrom`);
            const toEl = document.getElementById(`${type}DateTo`);
            if (fromEl) { fromEl.value = fmt(past90); this[`${type}Filters`].dateFrom = fmt(past90); }
            if (toEl) { toEl.value = fmt(today); this[`${type}Filters`].dateTo = fmt(today); }
        });
    }

    async _loadProviders() {
        try {
            this.providers = await this._apiGet('/providers/dropdown') || [];
        } catch (error) {
            console.error('Load providers error:', error);
            this.providers = [];
        }
    }

    _initFilters() {
        this._initFilterPatientSearch('claims');
        this._initFilterPatientSearch('charges');
        this._initFilterPatientSearch('payments');
        this._initFilterProviderSearch('claims');
        this._initFilterProviderSearch('charges');
        this._initStatusFilters();
        this._initPayerFilter();
        this._initDateFilters();
        this._initPaymentsDateFilter();
        this._initClearFilters();
    }

    _initFilterPatientSearch(type) {
        const input = document.getElementById(`${type}PatientFilterSearch`);
        const list = document.getElementById(`${type}PatientFilterList`);
        const clearBtn = document.getElementById(`clear${type.charAt(0).toUpperCase() + type.slice(1)}PatientFilter`);
        const btn = document.getElementById(`${type}PatientFilterBtn`);
        if (!input || !list) return;

        let debounceTimer;
        input.addEventListener('input', () => {
            clearTimeout(debounceTimer);
            const q = input.value.trim();
            if (q.length < 3) { list.innerHTML = ''; return; }

            debounceTimer = setTimeout(async () => {
                try {
                    let patients = [];
                    try {
                        const data = await this._apiGet(`/patients/search?q=${encodeURIComponent(q)}`);
                        patients = data?.Results || data || [];
                    } catch {
                        // Fallback to patient list with client-side filter
                        const all = await this._apiGet('/patients');
                        const qLower = q.toLowerCase();
                        patients = (all || []).filter(p =>
                            (p.FirstName && p.FirstName.toLowerCase().includes(qLower)) ||
                            (p.LastName && p.LastName.toLowerCase().includes(qLower)) ||
                            (p.FullName && p.FullName.toLowerCase().includes(qLower)) ||
                            (p.MRN && p.MRN.toLowerCase().includes(qLower))
                        );
                    }
                    list.innerHTML = patients.map(p => `
                        <div class="schedule-filter-item px-2 py-1" style="cursor:pointer;" data-patient-id="${p.PatientId}" data-patient-name="${this._escape(p.FirstName)} ${this._escape(p.LastName)}">
                            <strong>${this._escape(p.FirstName)} ${this._escape(p.LastName)}</strong>
                            <span class="text-muted ms-1 small">MRN: ${this._escape(p.MRN || 'N/A')}</span>
                        </div>
                    `).join('') || '<div class="text-muted small p-2">No patients found</div>';
                } catch (e) { console.error(e); }
            }, 300);
        });

        list.addEventListener('click', (e) => {
            const item = e.target.closest('[data-patient-id]');
            if (!item) return;
            this[`${type}Filters`].patientId = parseInt(item.dataset.patientId);
            this[`${type}Filters`].patientName = item.dataset.patientName;
            if (btn) { btn.classList.add('active'); btn.innerHTML = `<i class="bi bi-person me-1"></i>${this._escape(item.dataset.patientName)}`; }
            if (clearBtn) clearBtn.style.display = '';
            list.innerHTML = '';
            input.value = '';
            // Close dropdown
            const dropdown = btn?.closest('.dropdown');
            if (dropdown) bootstrap.Dropdown.getInstance(btn)?.hide();
            this._resetPage(type);
        });

        if (clearBtn) {
            clearBtn.addEventListener('click', () => {
                this[`${type}Filters`].patientId = null;
                this[`${type}Filters`].patientName = '';
                if (btn) { btn.classList.remove('active'); btn.innerHTML = `<i class="bi bi-person me-1"></i>Patient`; }
                clearBtn.style.display = 'none';
                this._resetPage(type);
            });
        }
    }

    _initFilterProviderSearch(type) {
        const input = document.getElementById(`${type}ProviderFilterSearch`);
        const list = document.getElementById(`${type}ProviderFilterList`);
        const clearBtn = document.getElementById(`clear${type.charAt(0).toUpperCase() + type.slice(1)}ProviderFilter`);
        const btn = document.getElementById(`${type}ProviderFilterBtn`);
        if (!input || !list) return;

        const renderProviders = (filter = '') => {
            const filtered = filter ? this.providers.filter(p => {
                const name = `${p.FirstName ?? p.firstName ?? ''} ${p.LastName ?? p.lastName ?? ''}`.toLowerCase();
                return name.includes(filter.toLowerCase());
            }) : this.providers;

            list.innerHTML = filtered.map(p => {
                const name = `${p.FirstName ?? p.firstName ?? ''} ${p.LastName ?? p.lastName ?? ''}`;
                const id = p.ProviderId ?? p.providerId;
                return `<div class="schedule-filter-item px-2 py-1" style="cursor:pointer;" data-provider-id="${id}" data-provider-name="${this._escape(name)}">${this._escape(name)}</div>`;
            }).join('') || '<div class="text-muted small p-2">No providers found</div>';
        };

        // Show all providers when dropdown opens
        const dropdown = btn?.closest('.dropdown');
        if (dropdown) {
            dropdown.addEventListener('shown.bs.dropdown', () => {
                renderProviders(input.value.trim());
                input.focus();
            });
        }

        input.addEventListener('input', () => renderProviders(input.value.trim()));

        list.addEventListener('click', (e) => {
            const item = e.target.closest('[data-provider-id]');
            if (!item) return;
            this[`${type}Filters`].providerId = parseInt(item.dataset.providerId);
            this[`${type}Filters`].providerName = item.dataset.providerName;
            if (btn) { btn.classList.add('active'); btn.innerHTML = `<i class="bi bi-person-badge me-1"></i>${this._escape(item.dataset.providerName)}`; }
            if (clearBtn) clearBtn.style.display = '';
            input.value = '';
            bootstrap.Dropdown.getInstance(btn)?.hide();
            this._resetPage(type);
        });

        if (clearBtn) {
            clearBtn.addEventListener('click', () => {
                this[`${type}Filters`].providerId = null;
                this[`${type}Filters`].providerName = '';
                if (btn) { btn.classList.remove('active'); btn.innerHTML = `<i class="bi bi-person-badge me-1"></i>Provider`; }
                clearBtn.style.display = 'none';
                this._resetPage(type);
            });
        }
    }

    _initStatusFilters() {
        // Claims status
        document.querySelectorAll('.claims-status-filter').forEach(cb => {
            cb.addEventListener('change', () => {
                this.claimsFilters.statuses = Array.from(document.querySelectorAll('.claims-status-filter:checked')).map(c => parseInt(c.value));
                const count = this.claimsFilters.statuses.length;
                const badge = document.getElementById('claimsStatusFilterCount');
                const btn = document.getElementById('claimsStatusFilterBtn');
                if (badge) { badge.textContent = count; badge.classList.toggle('d-none', count === 0); }
                if (btn) btn.classList.toggle('active', count > 0);
                this._resetPage('claims');
            });
        });

        // Charges status
        document.querySelectorAll('.charges-status-filter').forEach(cb => {
            cb.addEventListener('change', () => {
                this.chargesFilters.statuses = Array.from(document.querySelectorAll('.charges-status-filter:checked')).map(c => parseInt(c.value));
                const count = this.chargesFilters.statuses.length;
                const badge = document.getElementById('chargesStatusFilterCount');
                const btn = document.getElementById('chargesStatusFilterBtn');
                if (badge) { badge.textContent = count; badge.classList.toggle('d-none', count === 0); }
                if (btn) btn.classList.toggle('active', count > 0);
                this._resetPage('charges');
            });
        });
    }

    _initPayerFilter() {
        const applyBtn = document.getElementById('applyClaimsPayerFilter');
        const clearBtn = document.getElementById('clearClaimsPayerFilter');
        const input = document.getElementById('claimsPayerFilterSearch');
        const btn = document.getElementById('claimsPayerFilterBtn');

        if (applyBtn && input) {
            applyBtn.addEventListener('click', () => {
                this.claimsFilters.payerSearch = input.value.trim();
                if (btn) btn.classList.toggle('active', !!this.claimsFilters.payerSearch);
                if (clearBtn) clearBtn.style.display = this.claimsFilters.payerSearch ? '' : 'none';
                bootstrap.Dropdown.getInstance(btn)?.hide();
                this._resetPage('claims');
            });
        }

        if (clearBtn) {
            clearBtn.addEventListener('click', () => {
                if (input) input.value = '';
                this.claimsFilters.payerSearch = '';
                if (btn) btn.classList.remove('active');
                clearBtn.style.display = 'none';
                this._resetPage('claims');
            });
        }
    }

    _initDateFilters() {
        ['claims', 'charges'].forEach(type => {
            const fromEl = document.getElementById(`${type}DateFrom`);
            const toEl = document.getElementById(`${type}DateTo`);

            if (fromEl) {
                fromEl.addEventListener('change', () => {
                    this[`${type}Filters`].dateFrom = fromEl.value || null;
                    this._resetPage(type);
                });
            }
            if (toEl) {
                toEl.addEventListener('change', () => {
                    this[`${type}Filters`].dateTo = toEl.value || null;
                    this._resetPage(type);
                });
            }
        });
    }

    _initPaymentsDateFilter() {
        const applyBtn = document.getElementById('applyPaymentsDateFilter');
        const clearBtn = document.getElementById('clearPaymentsDateFilter');
        const dateBtn = document.getElementById('paymentsDateFilterBtn');
        const fromInput = document.getElementById('paymentsDateFrom');
        const toInput = document.getElementById('paymentsDateTo');
        if (!applyBtn || !fromInput || !toInput) return;

        applyBtn.addEventListener('click', () => {
            this.paymentsFilters.dateFrom = fromInput.value || null;
            this.paymentsFilters.dateTo = toInput.value || null;
            if (this.paymentsFilters.dateFrom || this.paymentsFilters.dateTo) {
                if (dateBtn) dateBtn.classList.add('active');
                if (clearBtn) clearBtn.style.display = '';
            }
            const dropdown = dateBtn?.closest('.dropdown');
            if (dropdown) bootstrap.Dropdown.getInstance(dateBtn)?.hide();
            this._resetPage('payments');
        });

        if (clearBtn) {
            clearBtn.addEventListener('click', () => {
                this.paymentsFilters.dateFrom = null;
                this.paymentsFilters.dateTo = null;
                fromInput.value = '';
                toInput.value = '';
                if (dateBtn) { dateBtn.classList.remove('active'); dateBtn.innerHTML = '<i class="bi bi-calendar-range me-1"></i>Date Range'; }
                clearBtn.style.display = 'none';
                this._resetPage('payments');
            });
        }
    }

    _initClearFilters() {
        const clearClaims = document.getElementById('clearClaimsFilters');
        if (clearClaims) {
            clearClaims.addEventListener('click', () => {
                this.claimsFilters = { patientId: null, patientName: '', providerId: null, providerName: '', payerSearch: '', statuses: [], dateFrom: null, dateTo: null };
                // Reset UI
                document.querySelectorAll('.claims-status-filter').forEach(cb => cb.checked = false);
                const payerInput = document.getElementById('claimsPayerFilterSearch');
                if (payerInput) payerInput.value = '';
                ['claimsPatientFilterBtn', 'claimsProviderFilterBtn', 'claimsPayerFilterBtn', 'claimsStatusFilterBtn'].forEach(id => {
                    const el = document.getElementById(id);
                    if (el) el.classList.remove('active');
                });
                const patBtn = document.getElementById('claimsPatientFilterBtn');
                if (patBtn) patBtn.innerHTML = '<i class="bi bi-person me-1"></i>Patient';
                const provBtn = document.getElementById('claimsProviderFilterBtn');
                if (provBtn) provBtn.innerHTML = '<i class="bi bi-person-badge me-1"></i>Provider';
                const payBtn = document.getElementById('claimsPayerFilterBtn');
                if (payBtn) payBtn.innerHTML = '<i class="bi bi-shield me-1"></i>Payer';
                document.getElementById('claimsStatusFilterCount')?.classList.add('d-none');
                ['clearClaimsPatientFilter', 'clearClaimsProviderFilter', 'clearClaimsPayerFilter'].forEach(id => {
                    const el = document.getElementById(id);
                    if (el) el.style.display = 'none';
                });
                this._setDefaultDateRanges();
                this._resetPage('claims');
            });
        }

        const clearCharges = document.getElementById('clearChargesFilters');
        if (clearCharges) {
            clearCharges.addEventListener('click', () => {
                this.chargesFilters = { patientId: null, patientName: '', providerId: null, providerName: '', statuses: [], dateFrom: null, dateTo: null };
                document.querySelectorAll('.charges-status-filter').forEach(cb => cb.checked = false);
                ['chargesPatientFilterBtn', 'chargesProviderFilterBtn', 'chargesStatusFilterBtn'].forEach(id => {
                    const el = document.getElementById(id);
                    if (el) el.classList.remove('active');
                });
                const patBtn = document.getElementById('chargesPatientFilterBtn');
                if (patBtn) patBtn.innerHTML = '<i class="bi bi-person me-1"></i>Patient';
                const provBtn = document.getElementById('chargesProviderFilterBtn');
                if (provBtn) provBtn.innerHTML = '<i class="bi bi-person-badge me-1"></i>Provider';
                document.getElementById('chargesStatusFilterCount')?.classList.add('d-none');
                ['clearChargesPatientFilter', 'clearChargesProviderFilter'].forEach(id => {
                    const el = document.getElementById(id);
                    if (el) el.style.display = 'none';
                });
                this._setDefaultDateRanges();
                this._resetPage('charges');
            });
        }

        const clearPayments = document.getElementById('clearPaymentsFilters');
        if (clearPayments) {
            clearPayments.addEventListener('click', () => {
                this.paymentsFilters = { patientId: null, patientName: '', dateFrom: null, dateTo: null };
                const patBtn = document.getElementById('paymentsPatientFilterBtn');
                if (patBtn) { patBtn.classList.remove('active'); patBtn.innerHTML = '<i class="bi bi-person me-1"></i>Patient'; }
                const dateBtn = document.getElementById('paymentsDateFilterBtn');
                if (dateBtn) { dateBtn.classList.remove('active'); dateBtn.innerHTML = '<i class="bi bi-calendar-range me-1"></i>Date Range'; }
                const fromInput = document.getElementById('paymentsDateFrom');
                const toInput = document.getElementById('paymentsDateTo');
                if (fromInput) fromInput.value = '';
                if (toInput) toInput.value = '';
                ['clearPaymentsPatientFilter', 'clearPaymentsDateFilter'].forEach(id => {
                    const el = document.getElementById(id);
                    if (el) el.style.display = 'none';
                });
                this._resetPage('payments');
            });
        }
    }

    _resetPage(type) {
        if (this[`${type}Page`]) this[`${type}Page`].current = 1;
        this._updateFilterBadges(type);
        if (type === 'claims') this.loadClaimsPaged();
        else if (type === 'payments') this.loadPayments();
        else this.loadChargesPaged();
    }

    _updateFilterBadges(type) {
        const filters = this[`${type}Filters`];
        if (!filters) return;
        let activeCount = 0;
        if (filters.patientId) activeCount++;
        if (filters.providerId) activeCount++;
        if (filters.statuses?.length) activeCount++;
        if (type === 'claims' && filters.payerSearch) activeCount++;
        if (filters.dateFrom) activeCount++;
        if (filters.dateTo) activeCount++;

        const badge = document.getElementById(`${type}FilterCount`);
        const clearBtn = document.getElementById(`clear${type.charAt(0).toUpperCase() + type.slice(1)}Filters`);
        if (badge) {
            badge.textContent = activeCount;
            badge.style.display = activeCount > 0 ? '' : 'none';
        }
        if (clearBtn) clearBtn.style.display = activeCount > 0 ? '' : 'none';
    }

    // ========================================
    // Private Methods - Patient Autocomplete
    // ========================================

    _initClaimPatientAutocomplete() {
        const searchInput = document.querySelector(this.containers.claimPatientSearch);
        const resultsDiv = document.querySelector(this.containers.claimPatientSearchResults);

        if (!searchInput || !resultsDiv) return;

        let debounceTimer = null;

        searchInput.addEventListener('input', () => {
            clearTimeout(debounceTimer);
            const query = searchInput.value.trim();

            if (query.length < 2) {
                resultsDiv.classList.add('d-none');
                resultsDiv.innerHTML = '';
                return;
            }

            debounceTimer = setTimeout(async () => {
                try {
                    const patients = await this._apiGet(`/patients/search?q=${encodeURIComponent(query)}`);

                    if (patients?.length) {
                        resultsDiv.innerHTML = patients.map(p => `
                            <div class="patient-result-item" data-patient='${JSON.stringify(p).replace(/'/g, "&#39;")}'>
                                <strong>${this._escape(p.FirstName)} ${this._escape(p.LastName)}</strong>
                                <span class="text-muted ms-2">MRN: ${this._escape(p.MRN || 'N/A')}</span>
                            </div>
                        `).join('');
                        resultsDiv.classList.remove('d-none');
                    } else {
                        resultsDiv.innerHTML = '<div class="p-2 text-muted">No patients found</div>';
                        resultsDiv.classList.remove('d-none');
                    }
                } catch (error) {
                    console.error('Claim patient search error:', error);
                    resultsDiv.classList.add('d-none');
                }
            }, 300);
        });

        // Handle result selection
        resultsDiv.addEventListener('click', (e) => {
            const item = e.target.closest('.patient-result-item');
            if (item) {
                const patientData = JSON.parse(item.getAttribute('data-patient'));
                this.selectClaimPatient(patientData);
            }
        });

        // Close results on outside click
        document.addEventListener('click', (e) => {
            if (!searchInput.contains(e.target) && !resultsDiv.contains(e.target)) {
                resultsDiv.classList.add('d-none');
            }
        });
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

    async _apiDelete(endpoint) {
        if (this.api) {
            return await this.api.delete(endpoint);
        }
        return await window.apiRequest(endpoint, { method: 'DELETE' });
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

    _formatCurrency(amount) {
        if (amount === null || amount === undefined) return '$0.00';
        return new Intl.NumberFormat('en-US', {
            style: 'currency',
            currency: 'USD'
        }).format(amount);
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

    _emit(event, data) {
        if (this.eventBus) {
            this.eventBus.emit(event, data);
        }
    }
}

// Export for module usage
if (typeof module !== 'undefined' && module.exports) {
    module.exports = BillingModule;
}
window.BillingModule = BillingModule;

// Auto-initialize when DOM is ready
document.addEventListener('DOMContentLoaded', function() {
    const container = document.getElementById('billingPage');
    if (!container) return;

    const initWhenReady = () => {
        const isAuthenticated = (typeof currentUser !== 'undefined' && currentUser) ||
                               (window.App && window.App.isAuthenticated && window.App.isAuthenticated());

        if (!isAuthenticated) {
            setTimeout(initWhenReady, 200);
            return;
        }

        if (window.billingModule) {
            window.billingModule.load();
            return;
        }

        window.billingModule = new BillingModule({
            api: window.apiService || (window.App && window.App.api),
            eventBus: window.eventBus || (window.App && window.App.events)
        });

        window.billingModule.init();
        window.billingModule.load();
    };

    initWhenReady();
});
