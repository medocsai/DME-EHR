/**
 * ClaimFormModule - CMS-1500 Claim Editor
 * Ported from PTEHR with adaptations for IMEHR
 *
 * Supports: Full CMS-1500 form editing, inline charge line editing,
 * diagnosis management, save, mark ready, PDF download.
 */
class ClaimFormModule {
    constructor(options = {}) {
        this._api = options.api || window.apiService || (window.App && window.App.api);
        this._claimId = null;
        this._claim = null;
        this._modal = null;
        this._dirty = false;
        this._headerButtonsBound = false;
        this._onSaveCallback = options.onSave || null;
    }

    // ═══════════════════════════════════════════
    // Public API
    // ═══════════════════════════════════════════

    async openClaim(claimId) {
        this._claimId = claimId;
        await this._loadClaim();
        if (!this._claim) return;
        this._showModal();
    }

    // ═══════════════════════════════════════════
    // Data Loading
    // ═══════════════════════════════════════════

    async _loadClaim() {
        try {
            const resp = await this._apiGet(`/billing/claims/${this._claimId}/detail`);
            this._claim = this._toCamel(resp);
        } catch (err) {
            console.error('[ClaimForm] Failed to load claim:', err);
            this._toast('Failed to load claim details', 'error');
        }
    }

    // ═══════════════════════════════════════════
    // Modal Display
    // ═══════════════════════════════════════════

    _showModal() {
        const modalEl = document.getElementById('claimDetailModal');
        if (!modalEl) { console.error('claimDetailModal not found'); return; }

        const isUB04 = this._claim.type === 1;

        // Update title
        const title = document.getElementById('claimDetailTitle');
        if (title) title.innerHTML = `<i class="bi bi-file-medical me-2"></i>${isUB04 ? 'UB-04' : 'CMS-1500'} · ${this._e(this._claim.claimNumber || '')}`;

        // Render form body
        const body = document.getElementById('claimDetailBody');
        if (body) body.innerHTML = isUB04 ? this._renderUB04Form() : this._renderCMS1500Form();

        // Bind events
        this._bindFormEvents();
        this._updateButtonVisibility();

        // Show modal
        this._modal = bootstrap.Modal.getOrCreateInstance(modalEl);
        this._modal.show();
    }

    _refreshBody() {
        const isUB04 = this._claim?.type === 1;
        const body = document.getElementById('claimDetailBody');
        if (body) body.innerHTML = isUB04 ? this._renderUB04Form() : this._renderCMS1500Form();
        const title = document.getElementById('claimDetailTitle');
        if (title) title.innerHTML = `<i class="bi bi-file-medical me-2"></i>${isUB04 ? 'UB-04' : 'CMS-1500'} · ${this._e(this._claim?.claimNumber || '')}`;
        this._bindFormEvents();
        this._updateButtonVisibility();
    }

    _updateButtonVisibility() {
        const c = this._claim;
        const status = c?.status ?? 0;
        const isDraft = status === 0;
        const isReady = status === 1;

        // Save — only for editable (Draft) claims
        const saveBtn = document.getElementById('cf_saveBtn');
        if (saveBtn) saveBtn.style.display = isDraft ? '' : 'none';

        // Submit to Office Ally — visible for Draft/Ready claims.
        // Disabled with tooltip while the linked encounter's amendment window
        // is still open (server returns 409 if a locked submit slips through).
        const submitWrap = document.getElementById('cf_submitBtnWrap');
        const submitBtn  = document.getElementById('cf_submitBtn');
        const lockBadge  = document.getElementById('cf_submitLockBadge');
        const showSubmit = isDraft || isReady;
        if (submitWrap) submitWrap.style.display = showSubmit ? '' : 'none';

        const isLocked = !!(c?.isSubmitLocked || c?.IsSubmitLocked);
        const windowEnd = c?.amendmentWindowEndsAt || c?.AmendmentWindowEndsAt;

        if (submitBtn) {
            submitBtn.disabled = isLocked;
            submitBtn.title = isLocked && windowEnd
                ? `Submission is locked until the amendment window closes (${new Date(windowEnd).toLocaleString()}). The provider can still amend this encounter's note and codes until then.`
                : 'Submit this claim to the clearing house';
            submitBtn.classList.toggle('opacity-50', isLocked);
            submitBtn.style.cursor = isLocked ? 'not-allowed' : '';
        }

        // Countdown badge next to the buttons — only while locked
        if (lockBadge) {
            if (isLocked && windowEnd) {
                lockBadge.style.display = '';
                lockBadge.dataset.windowEnd = windowEnd;
                this._updateLockCountdownText(lockBadge, windowEnd);
                this._startLockCountdownTicker(lockBadge, windowEnd);
            } else {
                lockBadge.style.display = 'none';
                if (this._lockTickerHandle) { clearInterval(this._lockTickerHandle); this._lockTickerHandle = null; }
            }
        }
    }

    _updateLockCountdownText(badge, windowEnd) {
        const end = new Date(windowEnd).getTime();
        const msLeft = end - Date.now();
        const textEl = badge.querySelector('.countdown-text');
        if (!textEl) return;
        if (msLeft <= 0) {
            // Window just closed — refresh the claim so the lock flag flips off
            textEl.textContent = 'Unlocking...';
            if (this._lockTickerHandle) { clearInterval(this._lockTickerHandle); this._lockTickerHandle = null; }
            this._refetchAndRefresh();
            return;
        }
        const mins = Math.floor(msLeft / 60000);
        const h = Math.floor(mins / 60);
        const m = mins % 60;
        textEl.textContent = h > 0 ? `${h}h ${String(m).padStart(2, '0')}m` : `${m}m`;
        // Colour shift: red pulsing when less than 1h left
        if (msLeft < 60 * 60 * 1000) badge.classList.add('ending');
        else badge.classList.remove('ending');
    }

    _startLockCountdownTicker(badge, windowEnd) {
        if (this._lockTickerHandle) clearInterval(this._lockTickerHandle);
        this._lockTickerHandle = setInterval(() => this._updateLockCountdownText(badge, windowEnd), 30000);
    }

    async _refetchAndRefresh() {
        if (!this._claim?.claimId) return;
        try {
            const fresh = await this._apiGet(`/billing/claims/${this._claim.claimId}`);
            if (fresh) {
                this._claim = fresh;
                this._refreshBody();
            }
        } catch (e) { /* noop — next open will pick up changes */ }
    }

    async _submitClaim() {
        const id = this._claim?.claimId;
        if (!id) return;
        if (!confirm('Submit this claim to the clearing house? This action cannot be undone.')) return;
        const btn = document.getElementById('cf_submitBtn');
        const original = btn?.innerHTML;
        if (btn) { btn.disabled = true; btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Submitting...'; }
        try {
            await this._apiPost(`/billing/claims/${id}/submit`, {});
            this._toast('Claim submitted.', 'success');
            // Reload with new status
            await this._refetchAndRefresh();
        } catch (e) {
            // Server returns 409 if the amendment window is still open —
            // surface the message from the response so the biller knows why.
            const msg = e?.responseJSON?.message || e?.responseJSON?.error || e?.message || 'Submission failed. Please try again.';
            this._toast(msg, 'error');
            if (btn) { btn.disabled = false; btn.innerHTML = original; }
        }
    }

    // ═══════════════════════════════════════════
    // CMS-1500 Form Rendering
    // ═══════════════════════════════════════════

    _renderCMS1500Form() {
        const c = this._claim;
        if (!c) return '<div class="alert alert-warning">No claim data</div>';

        const diagCodes = this._parseDiagCodes(c.diagnosisCodes);
        const readonly = (c.status ?? 0) > 0;
        const dis = readonly ? 'disabled' : '';

        return `
        <div class="container-fluid">
            <!-- Status Bar -->
            <div class="d-flex align-items-center gap-3 mb-3 pb-3 border-bottom">
                <span class="fw-bold">${this._e(c.claimNumber)}</span>
                ${this._statusBadge(c.status)}
                <span class="text-muted small">Created: ${this._fmtDate(c.createdAt)}</span>
                <div class="ms-auto">
                    <button class="btn btn-sm btn-outline-primary" data-claim-action="switch-ub04" title="Switch to UB04"><i class="bi bi-arrow-left-right me-1"></i>UB04</button>
                </div>
            </div>

            <!-- Box 1: Insurance Type -->
            <div class="card mb-3">
                <div class="card-header bg-light py-2"><strong>Box 1 — Insurance Type</strong></div>
                <div class="card-body py-2">
                    <div class="row g-2">
                        <div class="col-md-4">
                            <label class="form-label small text-muted">Insurance Type</label>
                            <select class="form-select form-select-sm" id="cf_insuranceTypeCode" ${dis}>
                                <option value="1" ${c.insuranceTypeCode === 1 ? 'selected' : ''}>Medicare</option>
                                <option value="2" ${c.insuranceTypeCode === 2 ? 'selected' : ''}>Medicaid</option>
                                <option value="3" ${c.insuranceTypeCode === 3 ? 'selected' : ''}>Tricare</option>
                                <option value="4" ${c.insuranceTypeCode === 4 ? 'selected' : ''}>CHAMPVA</option>
                                <option value="5" ${c.insuranceTypeCode === 5 ? 'selected' : ''}>Group Health Plan</option>
                                <option value="6" ${c.insuranceTypeCode === 6 ? 'selected' : ''}>FECA</option>
                            </select>
                        </div>
                        <div class="col-md-4">
                            <label class="form-label small text-muted">Policy # (Box 1a)</label>
                            <input type="text" class="form-control form-control-sm" id="cf_insuredPolicyNumber" value="${this._e(c.insuredPolicyNumber)}" ${dis}>
                        </div>
                        <div class="col-md-4">
                            <label class="form-label small text-muted">Payer</label>
                            <input type="text" class="form-control form-control-sm" value="${this._e(c.payerName)}" readonly>
                        </div>
                    </div>
                </div>
            </div>

            <!-- Patient & Insured Info -->
            <div class="row g-3 mb-3">
                <!-- Box 2-5: Patient Info (Read-only) -->
                <div class="col-md-6">
                    <div class="card h-100">
                        <div class="card-header bg-light py-2"><strong>Box 2-5 — Patient</strong></div>
                        <div class="card-body py-2">
                            <div class="mb-1"><strong>${this._e(c.patientLastName)}, ${this._e(c.patientFirstName)}</strong></div>
                            <div class="small text-muted">${this._e(c.patientAddress || '')}${c.patientCity ? ', ' + this._e(c.patientCity) : ''}${c.patientState ? ', ' + this._e(c.patientState) : ''} ${this._e(c.patientZip || '')}</div>
                            <div class="small text-muted">DOB: ${this._fmtDate(c.patientDob)} | Gender: ${this._e(c.patientGender || '-')} | MRN: ${this._e(c.patientMrn || '-')}</div>
                        </div>
                    </div>
                </div>
                <!-- Box 4-11: Insured Info (Editable) -->
                <div class="col-md-6">
                    <div class="card h-100">
                        <div class="card-header bg-light py-2"><strong>Box 4-11 — Insured</strong></div>
                        <div class="card-body py-2">
                            <div class="row g-2">
                                <div class="col-8"><input type="text" class="form-control form-control-sm" id="cf_insuredName" value="${this._e(c.insuredName)}" placeholder="Insured Name" ${dis}></div>
                                <div class="col-4">
                                    <select class="form-select form-select-sm" id="cf_subscriberRelationship" ${dis}>
                                        <option value="Self" ${c.subscriberRelationship === 'Self' ? 'selected' : ''}>Self</option>
                                        <option value="Spouse" ${c.subscriberRelationship === 'Spouse' ? 'selected' : ''}>Spouse</option>
                                        <option value="Child" ${c.subscriberRelationship === 'Child' ? 'selected' : ''}>Child</option>
                                        <option value="Other" ${c.subscriberRelationship === 'Other' ? 'selected' : ''}>Other</option>
                                    </select>
                                </div>
                                <div class="col-4"><input type="date" class="form-control form-control-sm" id="cf_insuredDob" value="${this._fmtDateOnly(c.insuredDob)}" ${dis}></div>
                                <div class="col-4">
                                    <select class="form-select form-select-sm" id="cf_insuredGender" ${dis}>
                                        <option value="M" ${c.insuredGender === 'M' ? 'selected' : ''}>Male</option>
                                        <option value="F" ${c.insuredGender === 'F' ? 'selected' : ''}>Female</option>
                                        <option value="U" ${(c.insuredGender || 'U') === 'U' ? 'selected' : ''}>Unknown</option>
                                    </select>
                                </div>
                                <div class="col-4"><input type="text" class="form-control form-control-sm" id="cf_insuredGroupNumber" value="${this._e(c.insuredGroupNumber)}" placeholder="Group #" ${dis}></div>
                                <div class="col-8"><input type="text" class="form-control form-control-sm" id="cf_insuredAddress" value="${this._e(c.insuredAddress)}" placeholder="Address" ${dis}></div>
                                <div class="col-4"><input type="text" class="form-control form-control-sm" id="cf_insuredCity" value="${this._e(c.insuredCity)}" placeholder="City" ${dis}></div>
                                <div class="col-4"><input type="text" class="form-control form-control-sm" id="cf_insuredState" value="${this._e(c.insuredState)}" placeholder="State" maxlength="2" ${dis}></div>
                            </div>
                        </div>
                    </div>
                </div>
            </div>

            <!-- Box 12-13-17: Signatures & Referring Provider -->
            <div class="card mb-3">
                <div class="card-header bg-light py-2"><strong>Box 12-13-17 — Signatures & Referring</strong></div>
                <div class="card-body py-2">
                    <div class="row g-2">
                        <div class="col-md-3">
                            <label class="form-label small text-muted">Patient Sig on File (12)</label>
                            <select class="form-select form-select-sm" id="cf_patientSignatureOnFile" ${dis}>
                                <option value="true" ${c.patientSignatureOnFile ? 'selected' : ''}>Yes</option>
                                <option value="false" ${!c.patientSignatureOnFile ? 'selected' : ''}>No</option>
                            </select>
                        </div>
                        <div class="col-md-3">
                            <label class="form-label small text-muted">Insured Sig on File (13)</label>
                            <select class="form-select form-select-sm" id="cf_insuredSignatureOnFile" ${dis}>
                                <option value="true" ${c.insuredSignatureOnFile ? 'selected' : ''}>Yes</option>
                                <option value="false" ${!c.insuredSignatureOnFile ? 'selected' : ''}>No</option>
                            </select>
                        </div>
                        <div class="col-md-3">
                            <label class="form-label small text-muted">Referring Provider (17)</label>
                            <input type="text" class="form-control form-control-sm" id="cf_referringProviderName" value="${this._e(c.referringProviderName)}" ${dis}>
                        </div>
                        <div class="col-md-3">
                            <label class="form-label small text-muted">Referring NPI (17a)</label>
                            <input type="text" class="form-control form-control-sm" id="cf_referringProviderNpi" value="${this._e(c.referringProviderNpi)}" maxlength="10" ${dis}>
                        </div>
                    </div>
                </div>
            </div>

            <!-- Box 21: Diagnosis Codes -->
            <div class="card mb-3">
                <div class="card-header bg-light py-2 d-flex align-items-center justify-content-between">
                    <strong>Box 21 — Diagnosis Codes (ICD-10)</strong>
                    ${!readonly ? '<button class="btn btn-sm btn-outline-primary" data-claim-action="add-diagnosis"><i class="bi bi-plus"></i> Add</button>' : ''}
                </div>
                <div class="card-body py-2">
                    <div class="row g-2" id="cf_diagnosisContainer">
                        ${this._renderDiagInputs(diagCodes, readonly)}
                    </div>
                </div>
            </div>

            <!-- Box 23-24b-25-26-27: Auth, POS, Tax, Account -->
            <div class="card mb-3">
                <div class="card-header bg-light py-2"><strong>Box 23-27 — Authorization & Identifiers</strong></div>
                <div class="card-body py-2">
                    <div class="row g-2">
                        <div class="col-md-3">
                            <label class="form-label small text-muted">Prior Auth # (23)</label>
                            <input type="text" class="form-control form-control-sm" id="cf_priorAuthorizationNumber" value="${this._e(c.priorAuthorizationNumber)}" ${dis}>
                        </div>
                        <div class="col-md-2">
                            <label class="form-label small text-muted">POS (24b)</label>
                            <input type="text" class="form-control form-control-sm" id="cf_placeOfServiceCode" value="${this._e(c.placeOfServiceCode || '11')}" maxlength="2" ${dis}>
                        </div>
                        <div class="col-md-3">
                            <label class="form-label small text-muted">Federal Tax ID (25)</label>
                            <input type="text" class="form-control form-control-sm" id="cf_federalTaxId" value="${this._e(c.federalTaxId)}" ${dis}>
                        </div>
                        <div class="col-md-2">
                            <label class="form-label small text-muted">Patient Acct # (26)</label>
                            <input type="text" class="form-control form-control-sm" id="cf_patientAccountNumber" value="${this._e(c.patientAccountNumber)}" ${dis}>
                        </div>
                        <div class="col-md-2">
                            <label class="form-label small text-muted">Accept Assign (27)</label>
                            <select class="form-select form-select-sm" id="cf_acceptAssignment" ${dis}>
                                <option value="true" ${c.acceptAssignment ? 'selected' : ''}>Yes</option>
                                <option value="false" ${!c.acceptAssignment ? 'selected' : ''}>No</option>
                            </select>
                        </div>
                    </div>
                </div>
            </div>

            <!-- Box 24: Service Lines -->
            <div class="card mb-3">
                <div class="card-header bg-light py-2 d-flex align-items-center justify-content-between">
                    <strong>Box 24 — Service Lines</strong>
                    <div class="d-flex gap-2">
                        <button class="btn btn-sm btn-outline-primary" data-claim-action="view-notes" title="View clinical notes for this appointment"><i class="bi bi-journal-medical me-1"></i>View Notes</button>
                        ${!readonly ? '<button class="btn btn-sm btn-outline-info" data-claim-action="suggest-cpt" title="AI-powered CPT suggestions"><i class="bi bi-stars me-1"></i>Suggest CPT</button>' : ''}
                        <span class="fw-bold">Total: <span id="cf_totalCharged">$${(c.totalCharged || 0).toFixed(2)}</span></span>
                        ${!readonly ? '<button class="btn btn-sm btn-outline-primary" data-claim-action="add-charge"><i class="bi bi-plus"></i> Add Line</button>' : ''}
                    </div>
                </div>
                <div class="card-body p-0">
                    <div class="table-responsive">
                        <table class="table table-sm table-bordered mb-0" id="cf_chargeTable">
                            <thead class="table-light">
                                <tr>
                                    <th style="width:110px">Date</th>
                                    <th style="width:90px">CPT</th>
                                    <th>Description</th>
                                    <th style="width:200px">Modifiers</th>
                                    <th style="width:70px">Dx Ptr</th>
                                    <th style="width:100px">Amount</th>
                                    <th style="width:60px">Units</th>
                                    ${!readonly ? '<th style="width:40px"></th>' : ''}
                                </tr>
                            </thead>
                            <tbody>
                                ${this._renderChargeRows(c.chargeLines, readonly)}
                            </tbody>
                        </table>
                    </div>
                </div>
            </div>

            <!-- Box 29: Amount Paid -->
            <div class="card mb-3">
                <div class="card-header bg-light py-2"><strong>Box 29 — Amount Paid</strong></div>
                <div class="card-body py-2">
                    <div class="row g-2">
                        <div class="col-md-3">
                            <input type="number" step="0.01" class="form-control form-control-sm" id="cf_amountPaid" value="${c.amountPaid || ''}" placeholder="0.00" ${dis}>
                        </div>
                    </div>
                </div>
            </div>

            <!-- Provider & Facility -->
            <div class="row g-3 mb-3">
                <!-- Box 31: Rendering Provider -->
                <div class="col-md-4">
                    <div class="card h-100">
                        <div class="card-header bg-light py-2"><strong>Box 31 — Rendering Provider</strong></div>
                        <div class="card-body py-2">
                            <div class="mb-2"><input type="text" class="form-control form-control-sm" id="cf_renderingProviderName" value="${this._e(c.renderingProviderName)}" placeholder="Name" ${dis}></div>
                            <div><input type="text" class="form-control form-control-sm" id="cf_renderingProviderNpi" value="${this._e(c.renderingProviderNpi)}" placeholder="NPI" maxlength="10" ${dis}></div>
                        </div>
                    </div>
                </div>
                <!-- Box 32: Facility -->
                <div class="col-md-4">
                    <div class="card h-100">
                        <div class="card-header bg-light py-2"><strong>Box 32 — Facility</strong></div>
                        <div class="card-body py-2">
                            <div class="mb-2"><input type="text" class="form-control form-control-sm" id="cf_facilityName" value="${this._e(c.facilityName)}" placeholder="Facility Name" ${dis}></div>
                            <div class="mb-2"><input type="text" class="form-control form-control-sm" id="cf_facilityAddress" value="${this._e(c.facilityAddress)}" placeholder="Address" ${dis}></div>
                            <div><input type="text" class="form-control form-control-sm" id="cf_facilityNpi" value="${this._e(c.facilityNpi)}" placeholder="NPI" maxlength="10" ${dis}></div>
                        </div>
                    </div>
                </div>
                <!-- Box 33: Billing Provider -->
                <div class="col-md-4">
                    <div class="card h-100">
                        <div class="card-header bg-light py-2"><strong>Box 33 — Billing Provider</strong></div>
                        <div class="card-body py-2">
                            <div class="mb-2"><input type="text" class="form-control form-control-sm" id="cf_billingProviderName" value="${this._e(c.billingProviderName)}" placeholder="Name" ${dis}></div>
                            <div class="mb-2"><input type="text" class="form-control form-control-sm" id="cf_billingProviderAddress" value="${this._e(c.billingProviderAddress)}" placeholder="Address" ${dis}></div>
                            <div class="mb-2"><input type="text" class="form-control form-control-sm" id="cf_billingProviderNpi" value="${this._e(c.billingProviderNpi)}" placeholder="NPI" maxlength="10" ${dis}></div>
                            <div><input type="text" class="form-control form-control-sm" id="cf_billingProviderTaxonomy" value="${this._e(c.billingProviderTaxonomy)}" placeholder="Taxonomy" ${dis}></div>
                        </div>
                    </div>
                </div>
            </div>

            <!-- Notes -->
            <div class="card mb-3">
                <div class="card-header bg-light py-2"><strong>Notes</strong></div>
                <div class="card-body py-2">
                    <textarea class="form-control form-control-sm" id="cf_notes" rows="3" ${dis}>${this._e(c.notes || '')}</textarea>
                </div>
            </div>

            <!-- Financial Summary (for non-draft claims) -->
            ${(c.status ?? 0) > 0 ? `
            <div class="card mb-3">
                <div class="card-header bg-light py-2"><strong>Financial Summary</strong></div>
                <div class="card-body py-2">
                    <div class="row g-3">
                        <div class="col-md-3"><label class="small text-muted">Total Charged</label><div class="fw-bold fs-5">${this._fmtCurrency(c.totalCharged)}</div></div>
                        <div class="col-md-3"><label class="small text-muted">Total Paid</label><div class="fw-bold fs-5 text-success">${this._fmtCurrency(c.totalPaid)}</div></div>
                        <div class="col-md-3"><label class="small text-muted">Adjustment</label><div>${this._fmtCurrency(c.totalAdjustment)}</div></div>
                        <div class="col-md-3"><label class="small text-muted">Patient Resp.</label><div>${this._fmtCurrency(c.patientResponsibility)}</div></div>
                    </div>
                </div>
            </div>` : ''}

            ${c.denialReason ? `<div class="alert alert-danger"><strong>Denial:</strong> ${this._e(c.denialReason)} ${c.denialReasonCode ? '(' + this._e(c.denialReasonCode) + ')' : ''}</div>` : ''}
        </div>`;
    }

    // ═══════════════════════════════════════════
    // Sub-Renderers
    // ═══════════════════════════════════════════

    _renderDiagInputs(codes, readonly) {
        const letters = 'ABCDEFGHIJKL';
        const count = Math.max(codes.length, 4);
        let html = '';
        for (let i = 0; i < count && i < 12; i++) {
            html += `<div class="col-md-3">
                <div class="input-group input-group-sm">
                    <span class="input-group-text" style="width:30px">${letters[i]}</span>
                    <input type="text" class="form-control form-control-sm diag-input" value="${this._e(codes[i] || '')}" placeholder="ICD-10" ${readonly ? 'disabled' : ''}>
                </div>
            </div>`;
        }
        return html;
    }

    _renderChargeRows(lines, readonly) {
        if (!lines || !lines.length)
            return `<tr><td colspan="${readonly ? 7 : 8}" class="text-center text-muted py-3">No charge lines yet. Click "Add Line" to add one.</td></tr>`;

        const dis = readonly ? 'disabled' : '';
        return lines.map(l => {
            const sd = this._fmtDateOnly(l.serviceDate);
            return `
            <tr data-charge-id="${l.chargeId}">
                <td><input type="date" class="form-control form-control-sm charge-date" value="${sd}" ${dis}></td>
                <td><input type="text" class="form-control form-control-sm charge-cpt" value="${this._e(l.cptCode)}" ${dis}></td>
                <td><input type="text" class="form-control form-control-sm charge-desc" value="${this._e(l.cptDescription || '')}" ${dis}></td>
                <td>
                    <div class="d-flex gap-1">
                        <input type="text" class="form-control form-control-sm charge-mod1" value="${this._e(l.modifier1 || '')}" placeholder="M1" maxlength="2" style="width:42px" ${dis}>
                        <input type="text" class="form-control form-control-sm charge-mod2" value="${this._e(l.modifier2 || '')}" placeholder="M2" maxlength="2" style="width:42px" ${dis}>
                        <input type="text" class="form-control form-control-sm charge-mod3" value="${this._e(l.modifier3 || '')}" placeholder="M3" maxlength="2" style="width:42px" ${dis}>
                        <input type="text" class="form-control form-control-sm charge-mod4" value="${this._e(l.modifier4 || '')}" placeholder="M4" maxlength="2" style="width:42px" ${dis}>
                    </div>
                </td>
                <td><input type="text" class="form-control form-control-sm charge-dxptr" value="${this._e(l.icdPointers || '')}" ${dis}></td>
                <td><input type="number" step="0.01" class="form-control form-control-sm charge-amount" value="${l.chargeAmount || 0}" ${dis}></td>
                <td><input type="number" class="form-control form-control-sm charge-units" value="${l.units || 1}" min="1" ${dis}></td>
                ${!readonly ? `<td><button class="btn btn-sm btn-outline-danger" data-claim-action="remove-charge" data-charge-id="${l.chargeId}" title="Remove"><i class="bi bi-trash"></i></button></td>` : ''}
            </tr>`;
        }).join('');
    }

    // ═══════════════════════════════════════════
    // UB04 Form
    // ═══════════════════════════════════════════

    _renderUB04Form() {
        const c = this._claim;
        if (!c) return '<div class="alert alert-warning">No claim data</div>';

        const diagCodes = this._parseDiagCodes(c.diagnosisCodes);
        const readonly = (c.status ?? 0) > 0;
        const dis = readonly ? 'disabled' : '';

        return `
        <div class="container-fluid">
            <!-- Status Bar -->
            <div class="d-flex align-items-center gap-3 mb-3 pb-3 border-bottom">
                <span class="fw-bold">${this._e(c.claimNumber)}</span>
                ${this._statusBadge(c.status)}
                <span class="text-muted small">Created: ${this._fmtDate(c.createdAt)}</span>
                <div class="ms-auto">
                    <button class="btn btn-sm btn-outline-primary" data-claim-action="switch-cms1500" title="Switch to CMS 1500"><i class="bi bi-arrow-left-right me-1"></i>CMS 1500</button>
                </div>
            </div>

            <!-- FL 1/4/5: Facility & Bill Type -->
            <div class="card mb-3">
                <div class="card-header bg-light py-2"><strong>FL 1/4/5 — Facility & Bill Type</strong></div>
                <div class="card-body py-2">
                    <div class="row g-2">
                        <div class="col-md-4">
                            <label class="form-label small text-muted">FL 1 — Facility Name</label>
                            <input type="text" class="form-control form-control-sm" id="cf_facilityName" value="${this._e(c.facilityName)}" ${dis}>
                        </div>
                        <div class="col-md-2">
                            <label class="form-label small text-muted">FL 4 — Type of Bill</label>
                            <input type="text" class="form-control form-control-sm" id="cf_typeOfBill" value="${this._e(c.typeOfBill)}" placeholder="e.g. 0111" maxlength="4" ${dis}>
                        </div>
                        <div class="col-md-3">
                            <label class="form-label small text-muted">FL 5 — Federal Tax ID</label>
                            <input type="text" class="form-control form-control-sm" id="cf_federalTaxId" value="${this._e(c.federalTaxId)}" ${dis}>
                        </div>
                        <div class="col-md-3">
                            <label class="form-label small text-muted">Facility NPI</label>
                            <input type="text" class="form-control form-control-sm" id="cf_facilityNpi" value="${this._e(c.facilityNpi)}" maxlength="10" ${dis}>
                        </div>
                    </div>
                </div>
            </div>

            <!-- FL 8-14: Patient Info -->
            <div class="card mb-3">
                <div class="card-header bg-light py-2"><strong>FL 8-14 — Patient</strong></div>
                <div class="card-body py-2">
                    <div class="row g-2">
                        <div class="col-md-3">
                            <label class="form-label small text-muted">FL 8 — Patient Name</label>
                            <input type="text" class="form-control form-control-sm" value="${this._e(c.patientLastName)}, ${this._e(c.patientFirstName)}" readonly>
                        </div>
                        <div class="col-md-2">
                            <label class="form-label small text-muted">FL 10 — DOB</label>
                            <input type="text" class="form-control form-control-sm" value="${this._fmtDate(c.patientDob)}" readonly>
                        </div>
                        <div class="col-md-1">
                            <label class="form-label small text-muted">FL 11</label>
                            <input type="text" class="form-control form-control-sm" value="${this._e(c.patientGender || '-')}" readonly>
                        </div>
                        <div class="col-md-3">
                            <label class="form-label small text-muted">FL 12 — Admission Date</label>
                            <input type="date" class="form-control form-control-sm" id="cf_admissionDate" value="${this._fmtDateOnly(c.admissionDate)}" ${dis}>
                        </div>
                        <div class="col-md-3">
                            <label class="form-label small text-muted">FL 14 — Admission Type</label>
                            <select class="form-select form-select-sm" id="cf_admissionType" ${dis}>
                                <option value="">Select...</option>
                                <option value="1" ${c.admissionType === 1 ? 'selected' : ''}>1 — Emergency</option>
                                <option value="2" ${c.admissionType === 2 ? 'selected' : ''}>2 — Urgent</option>
                                <option value="3" ${c.admissionType === 3 ? 'selected' : ''}>3 — Elective</option>
                                <option value="4" ${c.admissionType === 4 ? 'selected' : ''}>4 — Newborn</option>
                            </select>
                        </div>
                    </div>
                </div>
            </div>

            <!-- FL 38/58-63: Insurance / Payer -->
            <div class="card mb-3">
                <div class="card-header bg-light py-2"><strong>FL 38/58-63 — Insurance</strong></div>
                <div class="card-body py-2">
                    <div class="row g-2">
                        <div class="col-md-3">
                            <label class="form-label small text-muted">FL 38 — Payer</label>
                            <input type="text" class="form-control form-control-sm" value="${this._e(c.payerName)}" readonly>
                        </div>
                        <div class="col-md-3">
                            <label class="form-label small text-muted">FL 58 — Insured Name</label>
                            <input type="text" class="form-control form-control-sm" id="cf_insuredName" value="${this._e(c.insuredName)}" ${dis}>
                        </div>
                        <div class="col-md-2">
                            <label class="form-label small text-muted">FL 60 — Policy #</label>
                            <input type="text" class="form-control form-control-sm" id="cf_insuredPolicyNumber" value="${this._e(c.insuredPolicyNumber)}" ${dis}>
                        </div>
                        <div class="col-md-2">
                            <label class="form-label small text-muted">FL 62 — Group #</label>
                            <input type="text" class="form-control form-control-sm" id="cf_insuredGroupNumber" value="${this._e(c.insuredGroupNumber)}" ${dis}>
                        </div>
                        <div class="col-md-2">
                            <label class="form-label small text-muted">FL 63 — Prior Auth</label>
                            <input type="text" class="form-control form-control-sm" id="cf_priorAuthorizationNumber" value="${this._e(c.priorAuthorizationNumber)}" ${dis}>
                        </div>
                    </div>
                </div>
            </div>

            <!-- Diagnosis Codes -->
            <div class="card mb-3">
                <div class="card-header bg-light py-2 d-flex align-items-center justify-content-between">
                    <strong>Diagnosis Codes (ICD-10)</strong>
                    ${!readonly ? '<button class="btn btn-sm btn-outline-primary" data-claim-action="add-diagnosis"><i class="bi bi-plus"></i> Add</button>' : ''}
                </div>
                <div class="card-body py-2">
                    <div class="row g-2" id="cf_diagnosisContainer">
                        ${this._renderDiagInputs(diagCodes, readonly)}
                    </div>
                </div>
            </div>

            <!-- FL 42-49: Revenue / Service Lines -->
            <div class="card mb-3">
                <div class="card-header bg-light py-2 d-flex align-items-center justify-content-between">
                    <strong>FL 42-49 — Revenue / Service Lines</strong>
                    <div class="d-flex gap-2">
                        <button class="btn btn-sm btn-outline-primary" data-claim-action="view-notes" title="View clinical notes"><i class="bi bi-journal-medical me-1"></i>View Notes</button>
                        ${!readonly ? '<button class="btn btn-sm btn-outline-info" data-claim-action="suggest-cpt" title="AI-suggest CPT codes"><i class="bi bi-stars me-1"></i>Suggest CPT</button>' : ''}
                        <span class="fw-bold">Total: <span id="cf_totalCharged">$${(c.totalCharged || 0).toFixed(2)}</span></span>
                        ${!readonly ? '<button class="btn btn-sm btn-outline-primary" data-claim-action="add-charge"><i class="bi bi-plus"></i> Add Line</button>' : ''}
                    </div>
                </div>
                <div class="card-body p-0">
                    <div class="table-responsive">
                        <table class="table table-sm table-bordered mb-0" id="cf_chargeTable">
                            <thead class="table-light">
                                <tr>
                                    <th style="width:80px">Rev Code</th>
                                    <th style="width:90px">CPT</th>
                                    <th>Description</th>
                                    <th style="width:110px">Date</th>
                                    <th style="width:60px">Units</th>
                                    <th style="width:100px">Amount</th>
                                    ${!readonly ? '<th style="width:40px"></th>' : ''}
                                </tr>
                            </thead>
                            <tbody>
                                ${this._renderUB04ChargeRows(c.chargeLines, readonly)}
                            </tbody>
                        </table>
                    </div>
                </div>
            </div>

            <!-- FL 76/56: Providers -->
            <div class="row g-3 mb-3">
                <div class="col-md-4">
                    <div class="card h-100">
                        <div class="card-header bg-light py-2"><strong>FL 76 — Attending Provider</strong></div>
                        <div class="card-body py-2">
                            <div class="mb-2"><input type="text" class="form-control form-control-sm" id="cf_renderingProviderName" value="${this._e(c.renderingProviderName)}" placeholder="Name" ${dis}></div>
                            <div><input type="text" class="form-control form-control-sm" id="cf_renderingProviderNpi" value="${this._e(c.renderingProviderNpi)}" placeholder="NPI" maxlength="10" ${dis}></div>
                        </div>
                    </div>
                </div>
                <div class="col-md-4">
                    <div class="card h-100">
                        <div class="card-header bg-light py-2"><strong>FL 56 — Billing Provider</strong></div>
                        <div class="card-body py-2">
                            <div class="mb-2"><input type="text" class="form-control form-control-sm" id="cf_billingProviderName" value="${this._e(c.billingProviderName)}" placeholder="Name" ${dis}></div>
                            <div><input type="text" class="form-control form-control-sm" id="cf_billingProviderNpi" value="${this._e(c.billingProviderNpi)}" placeholder="NPI" maxlength="10" ${dis}></div>
                        </div>
                    </div>
                </div>
                <div class="col-md-4">
                    <div class="card h-100">
                        <div class="card-header bg-light py-2"><strong>Patient Account</strong></div>
                        <div class="card-body py-2">
                            <div class="mb-2"><input type="text" class="form-control form-control-sm" id="cf_patientAccountNumber" value="${this._e(c.patientAccountNumber)}" placeholder="Account #" ${dis}></div>
                            <div><input type="text" class="form-control form-control-sm" id="cf_billingProviderTaxonomy" value="${this._e(c.billingProviderTaxonomy)}" placeholder="Taxonomy" ${dis}></div>
                        </div>
                    </div>
                </div>
            </div>

            <!-- Notes -->
            <div class="card mb-3">
                <div class="card-header bg-light py-2"><strong>Notes</strong></div>
                <div class="card-body py-2">
                    <textarea class="form-control form-control-sm" id="cf_notes" rows="2" ${dis}>${this._e(c.notes || '')}</textarea>
                </div>
            </div>

            ${c.denialReason ? `<div class="alert alert-danger"><strong>Denial:</strong> ${this._e(c.denialReason)} ${c.denialReasonCode ? '(' + this._e(c.denialReasonCode) + ')' : ''}</div>` : ''}
        </div>`;
    }

    _renderUB04ChargeRows(lines, readonly) {
        if (!lines || !lines.length)
            return `<tr><td colspan="${readonly ? 6 : 7}" class="text-center text-muted py-3">No service lines yet. Click "Add Line" to add one.</td></tr>`;

        const dis = readonly ? 'disabled' : '';
        return lines.map(l => `
            <tr data-charge-id="${l.chargeId}">
                <td><input type="text" class="form-control form-control-sm charge-revcode" value="${this._e(l.revenueCode || '')}" style="width:70px" ${dis}></td>
                <td><input type="text" class="form-control form-control-sm charge-cpt" value="${this._e(l.cptCode)}" style="width:80px" ${dis}></td>
                <td><input type="text" class="form-control form-control-sm charge-desc" value="${this._e(l.cptDescription || '')}" ${dis}></td>
                <td><input type="date" class="form-control form-control-sm charge-date" value="${this._fmtDateOnly(l.serviceDate)}" ${dis}></td>
                <td><input type="number" class="form-control form-control-sm charge-units" value="${l.units || 1}" min="1" style="width:50px" ${dis}></td>
                <td><input type="number" step="0.01" class="form-control form-control-sm charge-amount" value="${l.chargeAmount || 0}" ${dis}></td>
                ${!readonly ? `<td><button class="btn btn-sm btn-outline-danger" data-claim-action="remove-charge" data-charge-id="${l.chargeId}" title="Remove"><i class="bi bi-trash"></i></button></td>` : ''}
            </tr>
        `).join('');
    }

    async _switchFormType(type) {
        try {
            await this._apiPut(`/billing/claims/${this._claimId}`, { Type: type });
            this._claim.type = type;
            this._refreshBody();
        } catch (err) {
            console.error('[ClaimForm] Switch form type error:', err);
            this._toast('Failed to switch form type', 'error');
        }
    }

    // ═══════════════════════════════════════════
    // Event Handling
    // ═══════════════════════════════════════════

    _bindFormEvents() {
        const modalEl = document.getElementById('claimDetailModal');
        if (!modalEl) return;

        // Delegated click events on modal body
        modalEl.onclick = (e) => {
            const target = e.target.closest('[data-claim-action]');
            if (!target) return;
            const action = target.dataset.claimAction;

            if (action === 'add-charge') this._addChargeLine();
            else if (action === 'remove-charge') this._removeChargeLine(target.dataset.chargeId);
            else if (action === 'add-diagnosis') this._addDiagnosis();
            else if (action === 'switch-ub04') this._switchFormType(1);
            else if (action === 'switch-cms1500') this._switchFormType(0);
            else if (action === 'suggest-cpt') this._suggestCpt(target);
            else if (action === 'view-notes') this._viewNotes();
        };

        // Header buttons
        if (!this._headerButtonsBound) {
            document.getElementById('cf_saveBtn')?.addEventListener('click', () => this._saveClaim());
            // Submit-to-Office-Ally — amendment-window gating is handled inside _submitClaim
            // (button is disabled client-side when locked; server returns 409 as a safety net)
            document.getElementById('cf_submitBtn')?.addEventListener('click', () => this._submitClaim());
            this._headerButtonsBound = true;
        }

        // Live total recalculation
        const chargeTable = document.getElementById('cf_chargeTable');
        if (chargeTable) {
            chargeTable.addEventListener('input', (e) => {
                if (e.target.classList.contains('charge-amount') || e.target.classList.contains('charge-units')) {
                    this._recalcTotal();
                }
            });
        }
    }

    // ═══════════════════════════════════════════
    // Form Data Collection
    // ═══════════════════════════════════════════

    _collectFormData() {
        const val = id => document.getElementById(id)?.value?.trim() || '';
        const sel = id => document.getElementById(id)?.value || '';

        // Diagnosis codes
        const diagInputs = document.querySelectorAll('#cf_diagnosisContainer .diag-input');
        const diagCodes = Array.from(diagInputs).map(i => i.value.trim()).filter(Boolean);

        return {
            InsuranceTypeCode: parseInt(sel('cf_insuranceTypeCode')) || null,
            InsuredPolicyNumber: val('cf_insuredPolicyNumber'),
            InsuredName: val('cf_insuredName'),
            SubscriberRelationship: sel('cf_subscriberRelationship'),
            InsuredDob: val('cf_insuredDob') || null,
            InsuredGender: sel('cf_insuredGender'),
            InsuredAddress: val('cf_insuredAddress'),
            InsuredCity: val('cf_insuredCity'),
            InsuredState: val('cf_insuredState'),
            InsuredGroupNumber: val('cf_insuredGroupNumber'),
            PatientSignatureOnFile: sel('cf_patientSignatureOnFile') === 'true',
            InsuredSignatureOnFile: sel('cf_insuredSignatureOnFile') === 'true',
            ReferringProviderName: val('cf_referringProviderName'),
            ReferringProviderNpi: val('cf_referringProviderNpi'),
            DiagnosisCodes: JSON.stringify(diagCodes),
            PriorAuthorizationNumber: val('cf_priorAuthorizationNumber'),
            PlaceOfServiceCode: val('cf_placeOfServiceCode'),
            FederalTaxId: val('cf_federalTaxId'),
            PatientAccountNumber: val('cf_patientAccountNumber'),
            AcceptAssignment: sel('cf_acceptAssignment') === 'true',
            AmountPaid: parseFloat(val('cf_amountPaid')) || null,
            RenderingProviderName: val('cf_renderingProviderName'),
            RenderingProviderNpi: val('cf_renderingProviderNpi'),
            FacilityName: val('cf_facilityName'),
            FacilityAddress: val('cf_facilityAddress'),
            FacilityNpi: val('cf_facilityNpi'),
            BillingProviderName: val('cf_billingProviderName'),
            BillingProviderAddress: val('cf_billingProviderAddress'),
            BillingProviderNpi: val('cf_billingProviderNpi'),
            BillingProviderTaxonomy: val('cf_billingProviderTaxonomy'),
            Notes: document.getElementById('cf_notes')?.value || '',
            // UB04-specific fields
            TypeOfBill: val('cf_typeOfBill'),
            AdmissionDate: val('cf_admissionDate') || null,
            AdmissionType: parseInt(sel('cf_admissionType')) || null
        };
    }

    _collectChargeData(row) {
        return {
            ClaimId: this._claimId,
            ServiceDate: row.querySelector('.charge-date')?.value || null,
            CptCode: row.querySelector('.charge-cpt')?.value?.trim() || '',
            CptDescription: row.querySelector('.charge-desc')?.value?.trim() || '',
            Units: parseInt(row.querySelector('.charge-units')?.value) || 1,
            ChargeAmount: parseFloat(row.querySelector('.charge-amount')?.value) || 0,
            Modifier1: row.querySelector('.charge-mod1')?.value?.trim() || null,
            Modifier2: row.querySelector('.charge-mod2')?.value?.trim() || null,
            Modifier3: row.querySelector('.charge-mod3')?.value?.trim() || null,
            Modifier4: row.querySelector('.charge-mod4')?.value?.trim() || null,
            IcdPointers: row.querySelector('.charge-dxptr')?.value?.trim() || null,
            RevenueCode: row.querySelector('.charge-revcode')?.value?.trim() || null,
            PlaceOfServiceCode: document.getElementById('cf_placeOfServiceCode')?.value || '11'
        };
    }

    // ═══════════════════════════════════════════
    // Save / Ready / Charges
    // ═══════════════════════════════════════════

    async _saveClaim() {
        try {
            const data = this._collectFormData();
            await this._apiPut(`/billing/claims/${this._claimId}`, data);

            // Save each charge row
            const rows = document.querySelectorAll('#cf_chargeTable tbody tr[data-charge-id]');
            for (const row of rows) {
                const chargeId = row.dataset.chargeId;
                const chargeData = this._collectChargeData(row);
                await this._apiPut(`/billing/charges/${chargeId}`, chargeData);
            }

            this._toast('Claim saved successfully');
            await this._loadClaim();
            this._refreshBody();

            if (this._onSaveCallback) this._onSaveCallback();
        } catch (err) {
            console.error('[ClaimForm] Save error:', err);
            this._toast(err.message || 'Failed to save claim', 'error');
        }
    }

    async _markReady() {
        try {
            // Save first
            await this._saveClaim();
            // Then mark ready
            await this._apiPost(`/billing/claims/${this._claimId}/ready`, {});
            this._toast('Claim marked as Ready');
            await this._loadClaim();
            this._refreshBody();
            if (this._onSaveCallback) this._onSaveCallback();
        } catch (err) {
            console.error('[ClaimForm] Mark ready error:', err);
            this._toast(err.message || 'Failed to mark claim as ready', 'error');
        }
    }

    async _addChargeLine() {
        try {
            // Save claim first
            await this._saveClaim();
            // Add empty charge line
            const pos = document.getElementById('cf_placeOfServiceCode')?.value || '11';
            const serviceDate = this._claim?.serviceDateFrom;
            await this._apiPost(`/billing/claims/${this._claimId}/charges`, {
                ClaimId: this._claimId,
                ServiceDate: serviceDate,
                CptCode: '',
                CptDescription: '',
                Units: 1,
                ChargeAmount: 0,
                PlaceOfServiceCode: pos
            });
            await this._loadClaim();
            this._refreshBody();
        } catch (err) {
            console.error('[ClaimForm] Add charge error:', err);
            this._toast(err.message || 'Failed to add charge line', 'error');
        }
    }

    async _removeChargeLine(chargeId) {
        if (!chargeId) return;
        if (!confirm('Remove this charge line?')) return;
        try {
            await this._apiDelete(`/billing/charges/${chargeId}`);
            await this._loadClaim();
            this._refreshBody();
        } catch (err) {
            console.error('[ClaimForm] Remove charge error:', err);
            this._toast(err.message || 'Failed to remove charge', 'error');
        }
    }

    _addDiagnosis() {
        const container = document.getElementById('cf_diagnosisContainer');
        if (!container) return;
        const current = container.querySelectorAll('.diag-input').length;
        if (current >= 12) { this._toast('Maximum 12 diagnosis codes allowed', 'error'); return; }
        const letters = 'ABCDEFGHIJKL';
        const div = document.createElement('div');
        div.className = 'col-md-3';
        div.innerHTML = `<div class="input-group input-group-sm">
            <span class="input-group-text" style="width:30px">${letters[current]}</span>
            <input type="text" class="form-control form-control-sm diag-input" value="" placeholder="ICD-10">
        </div>`;
        container.appendChild(div);
    }

    _recalcTotal() {
        const rows = document.querySelectorAll('#cf_chargeTable tbody tr[data-charge-id]');
        let total = 0;
        rows.forEach(row => {
            const amount = parseFloat(row.querySelector('.charge-amount')?.value) || 0;
            const units = parseInt(row.querySelector('.charge-units')?.value) || 1;
            total += amount * units;
        });
        const el = document.getElementById('cf_totalCharged');
        if (el) el.textContent = `$${total.toFixed(2)}`;
    }

    // ═══════════════════════════════════════════
    // AI CPT Suggestion & View Notes
    // ═══════════════════════════════════════════

    async _suggestCpt(btn) {
        const origHtml = btn.innerHTML;
        btn.disabled = true;
        btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Analyzing...';

        try {
            const diagInputs = document.querySelectorAll('.diag-input');
            const diagCodes = Array.from(diagInputs).map(i => i.value).filter(v => v.trim());

            const resp = await this._apiPost('/billing/suggest-cpt', {
                claimId: this._claimId,
                diagnosisCodes: diagCodes.join(', ')
            });

            const data = this._toCamel(resp);

            if (!data.success || !data.suggestions || !data.suggestions.length) {
                this._toast(data.errorMessage || 'No suggestions returned', 'error');
                return;
            }

            this._showSuggestionsPicker(data.suggestions);
        } catch (err) {
            console.error('[ClaimForm] CPT suggestion failed:', err);
            this._toast('CPT suggestion failed', 'error');
        } finally {
            btn.disabled = false;
            btn.innerHTML = origHtml;
        }
    }

    _showSuggestionsPicker(suggestions) {
        document.getElementById('cptSuggestionPicker')?.remove();

        const rows = suggestions.map(s => `
            <tr>
                <td><strong>${this._e(s.cptCode)}</strong></td>
                <td>${this._e(s.description)}</td>
                <td class="text-center">${s.units || 1}</td>
                <td><small class="text-muted">${this._e(s.rationale || '')}</small></td>
                <td class="text-center">
                    <button class="btn btn-sm btn-success" data-add-suggested="${this._e(s.cptCode)}"
                        data-desc="${this._e(s.description)}" data-units="${s.units || 1}"
                        data-rate="${s.defaultRate || 0}">
                        <i class="bi bi-plus-lg"></i>
                    </button>
                </td>
            </tr>
        `).join('');

        const picker = document.createElement('div');
        picker.id = 'cptSuggestionPicker';
        picker.className = 'card border-info mb-2';
        picker.innerHTML = `
            <div class="card-header bg-info bg-opacity-10 d-flex justify-content-between align-items-center py-1">
                <span><i class="bi bi-stars me-1"></i><strong>AI CPT Suggestions</strong></span>
                <button class="btn btn-sm btn-close" data-dismiss-suggestions></button>
            </div>
            <div class="card-body p-0">
                <table class="table table-sm table-hover mb-0">
                    <thead><tr><th>CPT</th><th>Description</th><th>Units</th><th>Rationale</th><th></th></tr></thead>
                    <tbody>${rows}</tbody>
                </table>
            </div>
        `;

        const serviceSection = document.getElementById('cf_chargeTable')?.closest('.card');
        if (serviceSection) {
            serviceSection.parentNode.insertBefore(picker, serviceSection);
        }

        picker.addEventListener('click', async (e) => {
            const addBtn = e.target.closest('[data-add-suggested]');
            const dismissBtn = e.target.closest('[data-dismiss-suggestions]');

            if (dismissBtn) { picker.remove(); return; }

            if (addBtn) {
                const cptCode = addBtn.dataset.addSuggested;
                const desc = (addBtn.dataset.desc || '').substring(0, 195);
                const units = parseInt(addBtn.dataset.units) || 1;
                const rate = parseFloat(addBtn.dataset.rate) || 0;

                try {
                    await this._apiPost(`/billing/claims/${this._claimId}/charges`, {
                        claimId: this._claimId,
                        serviceDate: this._claim.serviceDateFrom,
                        cptCode: cptCode,
                        cptDescription: desc,
                        units: units,
                        chargeAmount: rate * units,
                        placeOfServiceCode: this._claim.placeOfServiceCode || '11'
                    });
                    addBtn.closest('tr').remove();
                    await this._loadClaim();
                    this._refreshBody();
                } catch (err) {
                    this._toast('Failed to add suggested CPT', 'error');
                }
            }
        });
    }

    _viewNotes() {
        const appointmentId = this._claim?.appointmentId;
        if (!appointmentId) {
            this._toast('No appointment linked to this claim', 'error');
            return;
        }

        if (typeof window.openTabbedClinicalNotes === 'function') {
            // Hide claim modal so notes appear on top
            const claimModal = document.getElementById('claimDetailModal');
            if (claimModal) bootstrap.Modal.getInstance(claimModal)?.hide();

            // Reopen claim when notes modal is closed
            const claimId = this._claimId;
            const notesModal = document.getElementById('tabbedClinicalNoteModal');
            if (notesModal) {
                const handler = () => {
                    notesModal.removeEventListener('hidden.bs.modal', handler);
                    setTimeout(() => window.openClaimDetail(claimId), 200);
                };
                notesModal.addEventListener('hidden.bs.modal', handler);
            }

            window.openTabbedClinicalNotes(appointmentId, null, { viewMode: true, fromClaim: true });
        } else {
            this._toast('Clinical notes viewer not available', 'error');
        }
    }

    // ═══════════════════════════════════════════
    // Utility Methods
    // ═══════════════════════════════════════════

    _toCamel(obj) {
        if (obj === null || obj === undefined) return obj;
        if (Array.isArray(obj)) return obj.map(o => this._toCamel(o));
        if (typeof obj !== 'object') return obj;
        const out = {};
        for (const [k, v] of Object.entries(obj)) {
            const camelKey = k.charAt(0).toLowerCase() + k.slice(1);
            out[camelKey] = this._toCamel(v);
        }
        return out;
    }

    _e(val) {
        if (val === null || val === undefined) return '';
        const div = document.createElement('div');
        div.textContent = String(val);
        return div.innerHTML;
    }

    _fmtDate(dt) {
        if (!dt) return '-';
        return new Date(dt).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
    }

    _fmtDateOnly(d) {
        if (!d) return '';
        if (typeof d === 'string' && d.length >= 10) return d.substring(0, 10);
        return '';
    }

    _fmtCurrency(amount) {
        if (amount === null || amount === undefined) return '$0.00';
        return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(amount);
    }

    _parseDiagCodes(json) {
        if (!json) return [];
        try { return JSON.parse(json); } catch { return []; }
    }

    _statusBadge(status) {
        const map = {
            0: 'bg-warning|Draft', 1: 'bg-info|Ready', 2: 'bg-primary|Submitted',
            3: 'bg-secondary|Acknowledged', 4: 'bg-secondary|Pending', 5: 'bg-success|Paid',
            6: 'bg-success-subtle text-success|Partially Paid', 7: 'bg-danger|Denied', 8: 'bg-danger-subtle text-danger|Rejected'
        };
        const [cls, label] = (map[status] || 'bg-secondary|Unknown').split('|');
        return `<span class="badge ${cls}">${label}</span>`;
    }

    _toast(msg, type = 'success') {
        if (window.Toast) window.Toast.show(type === 'error' ? 'Error' : 'Success', msg, type);
        else if (window.showToast) window.showToast(type === 'error' ? 'Error' : 'Success', msg, type);
    }

    async _apiGet(url) {
        if (this._api) return await this._api.get(url);
        return await window.apiRequest(url);
    }
    async _apiPut(url, data) {
        if (this._api) return await this._api.put(url, data);
        return await window.apiRequest(url, { method: 'PUT', body: JSON.stringify(data) });
    }
    async _apiPost(url, data) {
        if (this._api) return await this._api.post(url, data);
        return await window.apiRequest(url, { method: 'POST', body: JSON.stringify(data) });
    }
    async _apiDelete(url) {
        if (this._api) return await this._api.delete(url);
        return await window.apiRequest(url, { method: 'DELETE' });
    }
}

// Global exports
window.ClaimFormModule = ClaimFormModule;
window.claimFormModule = null;

window.openClaimDetail = function(claimId) {
    if (!window.claimFormModule) {
        window.claimFormModule = new ClaimFormModule({
            api: window.apiService || (window.App && window.App.api),
            onSave: () => {
                // Reload billing list if module exists
                if (window.billingModule) window.billingModule.loadClaimsPaged();
            }
        });
    }
    window.claimFormModule.openClaim(claimId);
};
