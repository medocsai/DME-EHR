/**
 * PatientInsuranceManager - Handles insurance validation and authorization management
 */

class PatientInsuranceManager {
    constructor(options = {}) {
        this.parentModule = options.parentModule;
        this.utilities = options.utilities || PatientUtilities;
        this.api = options.api;
    }

    /**
     * Get insurance ID for a given type
     * @param {Object} currentPatient - Current patient data
     * @param {string} type - 'primary' or 'secondary'
     * @returns {number|null}
     */
    getInsuranceId(currentPatient, type) {
        if (currentPatient?.Insurances) {
            const insurance = currentPatient.Insurances.find(i =>
                i.Type === (type === 'primary' ? 0 : 1)
            );
            return insurance?.InsuranceId;
        }
        return null;
    }

    /**
     * Validate insurance with payer
     * @param {Object} options - Options
     * @param {string} options.type - Insurance type ('primary' or 'secondary')
     * @param {Object} options.currentPatient - Current patient data
     * @param {Function} options.apiFn - API function
     * @param {Function} options.onSuccess - Success callback
     * @param {Function} options.onError - Error callback
     * @param {Function} options.onRefresh - Refresh callback
     */
    async validateInsurance(options = {}) {
        const type = options.type || 'primary';
        const statusEl = document.getElementById(`${type}InsuranceValidationStatus`);
        const historySection = document.getElementById(`${type}AuthHistorySection`);
        const allowedVisitsInput = document.getElementById(`${type}AllowedVisits`);

        if (statusEl) {
            statusEl.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Validating with payer...';
        }

        try {
            const prefix = type === 'primary' ? 'PrimaryInsurance' : 'SecondaryInsurance';
            const form = document.getElementById('patientForm');

            // Always read from form fields
            const payerName = form.querySelector(`[name="${prefix}.PayerName"]`)?.value;
            const payerId = form.querySelector(`[name="${prefix}.PayerId"]`)?.value;
            const policyNumber = form.querySelector(`[name="${prefix}.PolicyNumber"]`)?.value;
            const groupNumber = form.querySelector(`[name="${prefix}.GroupNumber"]`)?.value;
            const subscriberFirstName = form.querySelector(`[name="${prefix}.SubscriberFirstName"]`)?.value;
            const subscriberLastName = form.querySelector(`[name="${prefix}.SubscriberLastName"]`)?.value;
            const subscriberDob = form.querySelector(`[name="${prefix}.SubscriberDob"]`)?.value;
            const insuranceCategory = form.querySelector(`[name="${prefix}.InsuranceCategory"]`)?.value;

            if (!payerName) {
                throw new Error('Please enter a payer name first');
            }
            if (!policyNumber) {
                throw new Error('Please enter a Member ID / Policy Number');
            }
            if (!subscriberFirstName || !subscriberLastName) {
                throw new Error('Please enter subscriber first and last name');
            }

            // Build subscriber name from first + last
            const subscriberName = `${subscriberFirstName} ${subscriberLastName}`.trim();

            // Include InsuranceId if editing existing insurance (so results persist to DB)
            const insuranceIdInput = form.querySelector(`[name="${prefix}.InsuranceId"]`);
            const insuranceId = insuranceIdInput?.value ? parseInt(insuranceIdInput.value) : null;

            // Always call the direct verify endpoint with form data
            const apiFn = options.apiFn || this._defaultApiPost;
            const requestBody = {
                InsuranceId: insuranceId,
                Type: type === 'primary' ? 0 : 1,
                PayerName: payerName,
                PayerId: payerId || null,
                PolicyNumber: policyNumber,
                GroupNumber: groupNumber || null,
                SubscriberName: subscriberName,
                SubscriberDob: subscriberDob || null
            };

            const result = await apiFn('/insurance/verify', requestBody);

            if (result && result.Success) {
                if (statusEl) {
                    statusEl.innerHTML = this._renderVerificationResult(result);
                }

                if (allowedVisitsInput && result.AllowedVisits) {
                    allowedVisitsInput.value = result.AllowedVisits;
                }

                if (historySection) {
                    historySection.style.display = 'block';
                }

                if (options.onRefresh) {
                    await options.onRefresh(type);
                }

                if (options.onSuccess) {
                    options.onSuccess(`${type === 'primary' ? 'Primary' : 'Secondary'} insurance validated`);
                }
                return result;
            } else {
                throw new Error(result?.ErrorMessage || result?.Message || 'Validation failed');
            }
        } catch (error) {
            console.error('[PatientInsuranceManager] Insurance validation failed:', error);
            const statusEl2 = document.getElementById(`${type}InsuranceValidationStatus`);
            if (statusEl2) {
                statusEl2.innerHTML = `<span class="text-danger"><i class="bi bi-exclamation-triangle me-1"></i>${error.message || 'Validation failed'}</span>`;
            }
            if (options.onError) {
                options.onError(error.message || 'Failed to validate insurance');
            }
            throw error;
        }
    }

    /**
     * Refresh authorization history
     * @param {string} type - Insurance type ('primary' or 'secondary')
     * @param {Object} options - Options
     * @param {Object} options.currentPatient - Current patient data
     * @param {Function} options.apiFn - API function
     * @param {Function} options.renderFn - Render function for authorization rows
     */
    async refreshAuthHistory(type, options = {}) {
        const historyBody = document.getElementById(`${type}AuthHistoryBody`);
        if (!historyBody) {
            return;
        }

        historyBody.innerHTML = `
            <tr>
                <td colspan="6" class="text-center py-3">
                    <span class="spinner-border spinner-border-sm me-1"></span>Loading...
                </td>
            </tr>
        `;

        try {
            const insuranceId = this.getInsuranceId(options.currentPatient, type);

            if (!insuranceId) {
                historyBody.innerHTML = `
                    <tr>
                        <td colspan="6" class="text-center text-muted py-3">
                            <i class="bi bi-info-circle me-1"></i>No authorization history available
                            <br><small class="text-muted">Authorizations will appear here after saving the patient</small>
                        </td>
                    </tr>
                `;
                return;
            }

            const apiFn = options.apiFn || this._defaultApiGet;
            const history = await apiFn(`/authorizations/insurance/${insuranceId}/history`);
            const authorizations = history?.AllAuthorizations || history?.Authorizations || [];

            if (!history || authorizations.length === 0) {
                historyBody.innerHTML = `
                    <tr>
                        <td colspan="6" class="text-center text-muted py-3">
                            <i class="bi bi-info-circle me-1"></i>No authorization history available
                            <br><button class="btn btn-sm btn-primary mt-2" onclick="window.patientModule.openAuthorizationModal('${type}', 'create')">
                                <i class="bi bi-plus me-1"></i>Add Authorization
                            </button>
                        </td>
                    </tr>
                `;
                return;
            }

            const renderFn = options.renderFn;
            if (renderFn) {
                historyBody.innerHTML = authorizations.map(auth => renderFn(auth, insuranceId)).join('');
            }

            const currentAuth = authorizations.find(auth => !auth.IsExpired);
            if (currentAuth) {
                const allowedVisitsInput = document.getElementById(`${type}AllowedVisits`);
                if (allowedVisitsInput) {
                    allowedVisitsInput.value = currentAuth.AuthorizedVisits || '';
                }
            }
        } catch (error) {
            console.error('[PatientInsuranceManager] Failed to load authorization history:', error);
            historyBody.innerHTML = `
                <tr>
                    <td colspan="6" class="text-center text-danger py-3">
                        <i class="bi bi-exclamation-triangle me-1"></i>Failed to load authorization history
                    </td>
                </tr>
            `;
        }
    }

    /**
     * Open authorization modal
     * @param {string} type - Insurance type
     * @param {string} action - Action ('create' or 'edit')
     * @param {Object} options - Options
     * @param {Object} options.currentPatient - Current patient data
     * @param {Function} options.onError - Error callback
     * @param {Function} options.onWarning - Warning callback
     */
    openAuthorizationModal(type, action = 'create', options = {}) {
        const modal = document.getElementById('authorizationModal');
        if (!modal) {
            if (options.onError) {
                options.onError('Authorization modal not found');
            }
            return;
        }

        const insuranceId = this.getInsuranceId(options.currentPatient, type);
        if (!insuranceId) {
            if (options.onWarning) {
                options.onWarning('Please save the patient with insurance information first');
            }
            return;
        }

        // Reset form
        const form = document.getElementById('authorizationForm');
        if (form) form.reset();

        // Set hidden fields
        document.getElementById('authorizationId').value = '';
        document.getElementById('authorizationInsuranceId').value = insuranceId;
        document.getElementById('authorizationMode').value = action;
        document.getElementById('authorizationContext').value = type;

        // Update title
        const titleEl = document.getElementById('authorizationModalTitle');
        if (titleEl) {
            titleEl.innerHTML = '<i class="bi bi-shield-plus me-2"></i>Add Authorization';
        }

        // Show modal
        const bsModal = new bootstrap.Modal(modal);
        bsModal.show();
    }

    /**
     * Fetch mock authorization data and pre-fill form
     * @param {string} type - Insurance type ('primary' or 'secondary')
     * @param {Object} options - Options
     * @param {Object} options.currentPatient - Current patient data
     * @param {Function} options.apiFn - API function
     * @param {Function} options.onSuccess - Success callback
     * @param {Function} options.onError - Error callback
     * @param {Function} options.onWarning - Warning callback
     */
    async fetchMockAuthorization(type = 'primary', options = {}) {
        const insuranceId = this.getInsuranceId(options.currentPatient, type);
        if (!insuranceId) {
            if (options.onWarning) {
                options.onWarning('Please save the patient with insurance information first');
            }
            return;
        }

        try {
            if (options.onWarning) {
                options.onWarning('Fetching authorization data from payer...');
            }

            const apiFn = options.apiFn || this._defaultApiPost;
            const result = await apiFn(`/authorizations/insurance/${insuranceId}/fetch`);

            if (!result || !result.Success) {
                throw new Error(result?.Message || 'Failed to fetch authorization data');
            }

            const modal = document.getElementById('authorizationModal');
            if (!modal) {
                if (options.onError) {
                    options.onError('Authorization modal not found');
                }
                return;
            }

            // Reset form
            const form = document.getElementById('authorizationForm');
            if (form) form.reset();

            // Set hidden fields
            document.getElementById('authorizationId').value = '';
            document.getElementById('authorizationInsuranceId').value = insuranceId;
            document.getElementById('authorizationMode').value = 'create';
            document.getElementById('authorizationContext').value = type;

            // Pre-fill form
            document.getElementById('authNumber').value = result.AuthorizationNumber || '';
            document.getElementById('authVisits').value = result.AuthorizedVisits || '';
            document.getElementById('authExpiry').value = result.ExpiryDate ? result.ExpiryDate.split('T')[0] : '';
            document.getElementById('authNotes').value = result.Notes || `Fetched from payer on ${new Date().toLocaleDateString()}`;

            // Update title
            const titleEl = document.getElementById('authorizationModalTitle');
            if (titleEl) {
                titleEl.innerHTML = '<i class="bi bi-shield-plus me-2"></i>Add Authorization (Pre-filled from Payer)';
            }

            const bsModal = new bootstrap.Modal(modal);
            bsModal.show();

            if (options.onSuccess) {
                options.onSuccess(`Authorization data fetched: ${result.AuthorizedVisits} visits authorized`);
            }
        } catch (error) {
            console.error('[PatientInsuranceManager] Failed to fetch authorization:', error);
            if (options.onError) {
                options.onError(error.message || 'Failed to fetch authorization data from payer');
            }
        }
    }

    /**
     * Edit authorization
     * @param {number} authId - Authorization ID
     * @param {number} insuranceId - Insurance ID
     * @param {Object} options - Options
     * @param {Function} options.apiFn - API function
     * @param {Function} options.onError - Error callback
     */
    async editAuthorization(authId, insuranceId, options = {}) {
        const modal = document.getElementById('authorizationModal');
        if (!modal) {
            if (options.onError) {
                options.onError('Authorization modal not found');
            }
            return;
        }

        try {
            const apiFn = options.apiFn || this._defaultApiGet;
            const auth = await apiFn(`/authorizations/${authId}`);
            if (!auth) {
                if (options.onError) {
                    options.onError('Authorization not found');
                }
                return;
            }

            // Populate form
            document.getElementById('authorizationId').value = authId;
            document.getElementById('authorizationInsuranceId').value = insuranceId;
            document.getElementById('authorizationMode').value = 'edit';
            document.getElementById('authNumber').value = auth.AuthorizationNumber || '';
            document.getElementById('authVisits').value = auth.AuthorizedVisits || '';
            document.getElementById('authExpiry').value = auth.ExpiryDate ? auth.ExpiryDate.split('T')[0] : '';
            document.getElementById('authNotes').value = auth.Notes || '';

            // Update title
            const titleEl = document.getElementById('authorizationModalTitle');
            if (titleEl) {
                titleEl.innerHTML = '<i class="bi bi-pencil me-2"></i>Edit Authorization';
            }

            const bsModal = new bootstrap.Modal(modal);
            bsModal.show();
        } catch (error) {
            console.error('[PatientInsuranceManager] Failed to load authorization:', error);
            if (options.onError) {
                options.onError('Failed to load authorization');
            }
        }
    }

    /**
     * Save authorization
     * @param {Event} e - Form submit event
     * @param {Object} options - Options
     * @param {Function} options.apiFn - API POST function
     * @param {Function} options.apiPutFn - API PUT function
     * @param {Function} options.onSuccess - Success callback
     * @param {Function} options.onError - Error callback
     * @param {Function} options.onRefresh - Refresh callback for history
     */
    async saveAuthorization(e, options = {}) {
        if (e) e.preventDefault();

        const mode = document.getElementById('authorizationMode')?.value;
        const authId = document.getElementById('authorizationId')?.value;
        const insuranceId = document.getElementById('authorizationInsuranceId')?.value;
        const context = document.getElementById('authorizationContext')?.value;

        const data = {
            InsuranceId: parseInt(insuranceId),
            AuthorizationNumber: document.getElementById('authNumber')?.value,
            AuthorizedVisits: parseInt(document.getElementById('authVisits')?.value) || 0,
            ExpiryDate: document.getElementById('authExpiry')?.value || null,
            Notes: document.getElementById('authNotes')?.value || null
        };

        try {
            if (mode === 'edit' && authId) {
                const apiPutFn = options.apiPutFn;
                await apiPutFn(`/authorizations/${authId}`, data);
                if (options.onSuccess) {
                    options.onSuccess('Authorization updated successfully');
                }
            } else {
                const apiFn = options.apiFn;
                await apiFn('/authorizations', data);
                if (options.onSuccess) {
                    options.onSuccess('Authorization created successfully');
                }
            }

            // Close modal
            const modal = document.getElementById('authorizationModal');
            if (modal) {
                const bsModal = bootstrap.Modal.getInstance(modal);
                if (bsModal) bsModal.hide();
            }

            // Refresh history
            if (context && options.onRefresh) {
                await options.onRefresh(context);
            }
        } catch (error) {
            console.error('[PatientInsuranceManager] Failed to save authorization:', error);
            if (options.onError) {
                options.onError(error.message || 'Failed to save authorization');
            }
        }
    }

    /**
     * Delete authorization
     * @param {number} authId - Authorization ID
     * @param {number} insuranceId - Insurance ID
     * @param {string} authNumber - Authorization number for display
     * @param {number} visits - Number of visits
     * @param {Object} options - Options
     * @param {Function} options.onConfirm - Confirmation callback
     */
    deleteAuthorization(authId, insuranceId, authNumber, visits, options = {}) {
        const modal = document.getElementById('deleteAuthorizationModal');
        if (!modal) {
            if (options.onConfirm) {
                options.onConfirm(authId, insuranceId);
            }
            return;
        }

        // Populate confirmation modal
        document.getElementById('deleteAuthNumber').textContent = authNumber || '-';
        document.getElementById('deleteAuthVisits').textContent = visits || 0;
        document.getElementById('deleteAuthorizationId').value = authId;
        document.getElementById('deleteAuthorizationInsuranceId').value = insuranceId;
        document.getElementById('deleteAuthorizationContext').value = options.type || 'primary';

        // Show modal
        const bsModal = new bootstrap.Modal(modal);
        bsModal.show();
    }

    /**
     * Confirm delete authorization
     * @param {number} authId - Authorization ID
     * @param {number} insuranceId - Insurance ID
     * @param {string} context - Context type ('primary' or 'secondary')
     * @param {Object} options - Options
     * @param {Function} options.apiFn - API delete function
     * @param {Function} options.onSuccess - Success callback
     * @param {Function} options.onError - Error callback
     * @param {Function} options.onRefresh - Refresh callback
     */
    async confirmDeleteAuthorization(authId, insuranceId, context = 'primary', options = {}) {
        try {
            const apiFn = options.apiFn;
            await apiFn(`/authorizations/${authId}`);

            if (options.onSuccess) {
                options.onSuccess('Authorization deleted successfully');
            }

            // Close modal
            const modal = document.getElementById('deleteAuthorizationModal');
            if (modal) {
                const bsModal = bootstrap.Modal.getInstance(modal);
                if (bsModal) bsModal.hide();
            }

            // Refresh history
            if (options.onRefresh) {
                await options.onRefresh(context);
            }
        } catch (error) {
            console.error('[PatientInsuranceManager] Failed to delete authorization:', error);
            if (options.onError) {
                options.onError(error.message || 'Failed to delete authorization');
            }
        }
    }

    /**
     * Render authorization row HTML
     * @param {Object} auth - Authorization data
     * @param {number} insuranceId - Insurance ID
     * @returns {string} HTML
     */
    renderAuthorizationRow(auth, insuranceId) {
        const statusClass = auth.IsActive ? 'bg-success' : (auth.IsExpired ? 'bg-danger' : 'bg-secondary');
        const statusText = auth.IsActive ? 'Active' : (auth.IsExpired ? 'Expired' : 'Inactive');

        return `
            <tr>
                <td><span class="badge ${statusClass}">${statusText}</span></td>
                <td>${this.utilities.escape(auth.AuthorizationNumber || '-')}</td>
                <td>${auth.AuthorizedVisits || 0}</td>
                <td>${auth.ExpiryDate ? this.utilities.formatDate(auth.ExpiryDate) : '-'}</td>
                <td>${auth.ValidatedOn ? this.utilities.formatDate(auth.ValidatedOn) : '-'}</td>
                <td class="text-end">
                    <button type="button" class="btn btn-sm btn-outline-primary me-1" onclick="window.patientModule.editAuthorization(${auth.AuthorizationId}, ${insuranceId})">
                        <i class="bi bi-pencil"></i>
                    </button>
                    <button type="button" class="btn btn-sm btn-outline-danger" onclick="window.patientModule.deleteAuthorization(${auth.AuthorizationId}, ${insuranceId}, '${this.utilities.escape(auth.AuthorizationNumber || '')}', ${auth.AuthorizedVisits || 0})">
                        <i class="bi bi-trash"></i>
                    </button>
                </td>
            </tr>
        `;
    }

    /**
     * Render verification result panel
     * @param {Object} result - InsuranceVerificationResult
     * @returns {string} HTML
     */
    _renderVerificationResult(result) {
        // If rich eligibility details are available, use the shared rich renderer
        if (result.EligibilityDetails) {
            return PatientRenderer.prototype.buildRichEligibilityHtml.call(
                { utilities: this.utilities || PatientUtilities },
                result.EligibilityDetails
            );
        }

        // Fallback: basic rendering from flat result fields
        const eligible = result.IsEligible;
        const badge = eligible
            ? '<span class="badge bg-success"><i class="bi bi-check-circle me-1"></i>Eligible</span>'
            : '<span class="badge bg-danger"><i class="bi bi-x-circle me-1"></i>Not Eligible</span>';

        const formatMoney = (v) => v != null ? `$${parseFloat(v).toFixed(2)}` : '-';
        const verifiedDate = result.VerifiedAt ? new Date(result.VerifiedAt).toLocaleString() : 'Just now';

        let html = `<div class="card border-${eligible ? 'success' : 'danger'} mt-2">
            <div class="card-header bg-${eligible ? 'success' : 'danger'} bg-opacity-10 py-2">
                <div class="d-flex justify-content-between align-items-center">
                    <strong>Eligibility Verification</strong>
                    ${badge}
                </div>
                <small class="text-muted">Verified: ${verifiedDate}</small>
            </div>
            <div class="card-body py-2">`;

        if (result.PlanName || result.PlanType) {
            html += `<div class="row mb-2">
                ${result.PlanName ? `<div class="col-md-6"><small class="text-muted">Plan:</small> <strong>${result.PlanName}</strong></div>` : ''}
                ${result.PlanType ? `<div class="col-md-3"><small class="text-muted">Type:</small> <strong>${result.PlanType}</strong></div>` : ''}
                ${result.InNetwork != null ? `<div class="col-md-3"><small class="text-muted">Network:</small> <span class="badge ${result.InNetwork ? 'bg-success' : 'bg-warning'}">${result.InNetwork ? 'In-Network' : 'Out-of-Network'}</span></div>` : ''}
            </div>`;
        }

        html += `<div class="row mb-1">
            <div class="col-md-3"><small class="text-muted">Copay:</small> <strong>${formatMoney(result.CopayInNetwork || result.Copay)}</strong></div>
            <div class="col-md-3"><small class="text-muted">Coinsurance:</small> <strong>${result.CoinsuranceInNetwork != null || result.Coinsurance != null ? (parseFloat(result.CoinsuranceInNetwork || result.Coinsurance) + '%') : '-'}</strong></div>
            <div class="col-md-3"><small class="text-muted">Deductible:</small> <strong>${formatMoney(result.IndividualDeductible || result.Deductible)}</strong></div>
            <div class="col-md-3"><small class="text-muted">Ded. Remaining:</small> <strong>${formatMoney(result.IndividualDeductibleRemaining)}</strong></div>
        </div>
        <div class="row mb-1">
            <div class="col-md-3"><small class="text-muted">OOP Max:</small> <strong>${formatMoney(result.IndividualOopMax || result.OutOfPocketMax)}</strong></div>
            <div class="col-md-3"><small class="text-muted">OOP Met:</small> <strong>${formatMoney(result.IndividualOopMet)}</strong></div>
            <div class="col-md-3"><small class="text-muted">Allowed Visits:</small> <strong>${result.AllowedVisits ?? '-'}</strong></div>
            <div class="col-md-3"><small class="text-muted">Remaining:</small> <strong>${result.VisitsRemaining ?? result.RemainingVisits ?? '-'}</strong></div>
        </div>`;

        if (result.RequiresPriorAuthorization) {
            html += `<div class="mt-1"><span class="badge bg-warning text-dark"><i class="bi bi-exclamation-triangle me-1"></i>Prior Authorization Required</span></div>`;
        }

        if (result.CoverageNotes) {
            html += `<div class="mt-1"><small class="text-muted">Notes:</small> <small>${result.CoverageNotes}</small></div>`;
        }

        html += `</div></div>`;
        return html;
    }

    /**
     * Show saved verification status when editing existing insurance
     * @param {string} type - 'primary' or 'secondary'
     * @param {Object} insurance - Insurance data from API (InsuranceDto)
     */
    showSavedVerificationStatus(type, insurance) {
        if (!insurance) return;

        const statusEl = document.getElementById(`${type}InsuranceValidationStatus`);
        if (!statusEl) return;

        // Only show if insurance has been verified before
        if (insurance.EligibilityStatus == null && !insurance.LastVerifiedAt) return;

        // Show basic info immediately, then try to load rich details
        const eligible = insurance.EligibilityStatus === 1;
        const badge = eligible
            ? '<span class="badge bg-success"><i class="bi bi-check-circle me-1"></i>Eligible</span>'
            : '<span class="badge bg-danger"><i class="bi bi-x-circle me-1"></i>Not Eligible</span>';

        const verifiedDate = insurance.LastVerifiedAt
            ? new Date(insurance.LastVerifiedAt).toLocaleString()
            : 'Unknown';

        const formatMoney = (v) => v != null ? `$${parseFloat(v).toFixed(2)}` : '-';

        // Basic fallback HTML
        let basicHtml = `<div class="card border-${eligible ? 'success' : 'danger'} mt-2">
            <div class="card-header bg-${eligible ? 'success' : 'danger'} bg-opacity-10 py-2">
                <div class="d-flex justify-content-between align-items-center">
                    <strong>Last Verification Result</strong>
                    ${badge}
                </div>
                <small class="text-muted">Verified: ${verifiedDate}</small>
            </div>
            <div class="card-body py-2">
                <div class="row mb-1">
                    <div class="col-md-3"><small class="text-muted">Copay:</small> <strong>${formatMoney(insurance.Copay)}</strong></div>
                    <div class="col-md-3"><small class="text-muted">Coinsurance:</small> <strong>${insurance.Coinsurance != null ? (parseFloat(insurance.Coinsurance) + '%') : '-'}</strong></div>
                    <div class="col-md-3"><small class="text-muted">Deductible:</small> <strong>${formatMoney(insurance.Deductible)}</strong></div>
                    <div class="col-md-3"><small class="text-muted">OOP Max:</small> <strong>${formatMoney(insurance.OutOfPocketMax)}</strong></div>
                </div>
                <div class="row mb-1">
                    <div class="col-md-3"><small class="text-muted">Allowed Visits:</small> <strong>${insurance.AllowedVisits ?? '-'}</strong></div>
                    ${insurance.PlanName ? `<div class="col-md-3"><small class="text-muted">Plan:</small> <strong>${insurance.PlanName}</strong></div>` : ''}
                    ${insurance.InNetwork != null ? `<div class="col-md-3"><span class="badge ${insurance.InNetwork ? 'bg-success' : 'bg-warning text-dark'}">${insurance.InNetwork ? 'In-Network' : 'Out-of-Network'}</span></div>` : ''}
                </div>
            </div>
        </div>`;

        statusEl.innerHTML = basicHtml;

        // Try to load rich details from API (async, replaces basic HTML when available)
        if (insurance.InsuranceId) {
            this._loadRichEligibilityDetails(statusEl, insurance.InsuranceId);
        }
    }

    /**
     * Load rich eligibility details from API and render in the given element
     * @param {HTMLElement} targetEl - Element to render into
     * @param {number} insuranceId - Insurance ID
     * @private
     */
    async _loadRichEligibilityDetails(targetEl, insuranceId) {
        try {
            const headers = { 'Content-Type': 'application/json' };
            const currentUser = JSON.parse(localStorage.getItem('currentUser') || '{}');
            if (currentUser.token) headers['Authorization'] = `Bearer ${currentUser.token}`;

            const response = await fetch(`/api/insurance/${insuranceId}/eligibility-details`, {
                method: 'GET',
                headers
            });
            if (!response.ok) return; // Keep basic display

            const details = await response.json();
            if (details) {
                // Use shared rich renderer from PatientRenderer
                const richHtml = PatientRenderer.prototype.buildRichEligibilityHtml.call(
                    { utilities: this.utilities || PatientUtilities },
                    details
                );
                targetEl.innerHTML = richHtml;
            }
        } catch (err) {
            // Keep basic display on error
            console.warn('[PatientInsuranceManager] Could not load rich eligibility details:', err.message);
        }
    }

    // Placeholder methods for API calls - will be replaced with actual calls from parent
    async _defaultApiGet(url) {
        throw new Error('API function not provided');
    }

    async _defaultApiPost(url, data) {
        throw new Error('API function not provided');
    }
}

// Export for use in both modern and legacy environments
if (typeof module !== 'undefined' && module.exports) {
    module.exports = PatientInsuranceManager;
}
window.PatientInsuranceManager = PatientInsuranceManager;
