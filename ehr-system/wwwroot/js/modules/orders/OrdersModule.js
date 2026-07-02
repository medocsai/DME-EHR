/**
 * OrdersModule - Labs, Imaging & Referrals management
 * Handles order list, create/edit/view, results entry, status transitions
 */
class OrdersModule {
    constructor() {
        this._debounceTimers = {};
        this._selectedPatient = null;
        this._currentTypeFilter = '';
        this._labPanels = [];
        this._confirmCallback = null;
        const _user = JSON.parse(localStorage.getItem('currentUser') || '{}');
        this._userRole = parseInt(_user.Role ?? _user.role ?? -1);
        this._isMaNurse = UserRoles.isMaNurse(this._userRole);
    }

    init() {
        // Always bind modal events so the order modal works from any page (e.g. Encounter Workspace)
        this._bindModalEvents();
        this._loadProviders();
        this._loadLabPanels();
        this._populateReferralSpecialties();

        if (!document.getElementById('ordersPage')) return;
        this._bindPageEvents();
        this.load();
    }

    // ========== DATA LOADING ==========

    async load() {
        try {
            let url = '/orders?';
            const search = document.getElementById('orderFilterSearch')?.value?.trim();
            const status = document.getElementById('orderFilterStatus')?.value;
            const dateFrom = document.getElementById('orderFilterStartDate')?.value;
            const dateTo = document.getElementById('orderFilterEndDate')?.value;

            if (this._currentTypeFilter !== '') url += `orderType=${this._currentTypeFilter}&`;
            if (status) url += `status=${status}&`;
            if (dateFrom) url += `dateFrom=${dateFrom}&`;
            if (dateTo) url += `dateTo=${dateTo}&`;

            const data = await apiRequest(url, { showLoader: false });
            let orders = Array.isArray(data) ? data : [];

            // Client-side text search
            if (search) {
                const q = search.toLowerCase();
                orders = orders.filter(o =>
                    (o.PatientName || '').toLowerCase().includes(q) ||
                    (o.PatientMRN || '').toLowerCase().includes(q) ||
                    (o.DiagnosisCode || '').toLowerCase().includes(q) ||
                    (o.ClinicalIndication || '').toLowerCase().includes(q) ||
                    (o.LabPanelName || '').toLowerCase().includes(q) ||
                    (o.ReferralSpecialty || '').toLowerCase().includes(q) ||
                    (o.BodyPart || '').toLowerCase().includes(q)
                );
            }

            this._renderTable(orders);
        } catch (err) {
            console.error('OrdersModule.load error:', err);
        }
    }

    _renderTable(orders) {
        const tbody = document.querySelector('#ordersTable tbody');
        if (!tbody) return;

        if (!orders.length) {
            tbody.innerHTML = '<tr><td colspan="8" class="text-center text-muted py-4">No orders found</td></tr>';
            document.getElementById('orderPageInfo').textContent = 'Showing 0 orders';
            return;
        }

        tbody.innerHTML = orders.map(o => {
            const desc = OrderConstants.getOrderDescription(o);
            const actions = this._getActions(o);
            return `<tr>
                <td>${o.OrderDate || ''}</td>
                <td><strong>${o.PatientName}</strong><br><small class="text-muted">${o.PatientMRN}</small></td>
                <td>${OrderConstants.TypeBadge[o.OrderType] || ''}</td>
                <td>${desc}</td>
                <td>${OrderConstants.PriorityBadge[o.Priority] || ''}</td>
                <td>${o.ProviderName || ''}</td>
                <td>${OrderConstants.StatusBadge[o.Status] || ''}</td>
                <td>${actions}</td>
            </tr>`;
        }).join('');

        document.getElementById('orderPageInfo').textContent = `Showing ${orders.length} order${orders.length !== 1 ? 's' : ''}`;
    }

    _getActions(o) {
        let html = `<button class="btn btn-sm btn-outline-info" onclick="window._ordersModule._viewOrder(${o.OrderId})" title="View"><i class="bi bi-eye"></i></button> `;

        // MA/Nurse: view-only, no action buttons
        if (this._isMaNurse) return html;

        // Draft: edit + submit
        if (o.Status === 0) {
            html += `<button class="btn btn-sm btn-outline-secondary" onclick="window._ordersModule._editOrder(${o.OrderId})" title="Edit"><i class="bi bi-pencil"></i></button> `;
            html += `<button class="btn btn-sm btn-outline-primary" onclick="window._ordersModule._submitOrder(${o.OrderId})" title="Submit"><i class="bi bi-send"></i></button> `;
        }

        // Pending/Sent/InProgress: add results (labs/imaging) or complete (referral)
        if (o.Status >= 1 && o.Status <= 3) {
            html += `<button class="btn btn-sm btn-outline-success" onclick="window._ordersModule._openResultsModal(${o.OrderId})" title="Add Results"><i class="bi bi-clipboard-check"></i></button> `;
        }

        // ResultsReceived: complete
        if (o.Status === 4) {
            html += `<button class="btn btn-sm btn-outline-success" onclick="window._ordersModule._completeOrder(${o.OrderId})" title="Complete"><i class="bi bi-check-circle"></i></button> `;
        }

        // Not cancelled/completed: cancel
        if (o.Status < 5) {
            html += `<button class="btn btn-sm btn-outline-danger" onclick="window._ordersModule._cancelOrder(${o.OrderId})" title="Cancel"><i class="bi bi-x-circle"></i></button>`;
        }

        return html;
    }

    // ========== EVENT BINDING ==========

    _bindModalEvents() {
        // Order type selector in modal — needed on any page that opens the order modal
        document.querySelectorAll('#orderTypeSelector input[name="orderType"]').forEach(radio => {
            radio.addEventListener('change', () => this._onOrderTypeChange());
        });

        // Lab panel change
        document.getElementById('orderLabPanel')?.addEventListener('change', (e) => this._onLabPanelChange(e.target.value));

        // Diagnosis suggestion via AI
        document.getElementById('btnSuggestDiagnosis')?.addEventListener('click', () => this._suggestDiagnosis());

        // Save/Submit
        document.getElementById('btnSaveOrderDraft')?.addEventListener('click', () => this._saveOrder(0));
        document.getElementById('btnSubmitOrder')?.addEventListener('click', () => this._saveOrder(1));

        // Save Results
        document.getElementById('btnSaveResults')?.addEventListener('click', () => this._saveResults());

        // Patient search
        document.getElementById('orderPatientSearch')?.addEventListener('input', () => this._debounce('patSearch', () => this._searchPatients(), 300));

        // Confirm modal action
        document.getElementById('btnOrderConfirmAction')?.addEventListener('click', () => {
            if (this._confirmCallback) {
                this._confirmCallback();
                this._confirmCallback = null;
            }
            bootstrap.Modal.getInstance(document.getElementById('orderConfirmModal'))?.hide();
        });
    }

    _bindPageEvents() {
        // New Order
        document.getElementById('btnNewOrder')?.addEventListener('click', () => this._openNewOrderModal());

        // Filters
        document.getElementById('orderFilterSearch')?.addEventListener('input', () => this._debounce('filter', () => this.load(), 400));
        document.getElementById('orderFilterStatus')?.addEventListener('change', () => this.load());
        document.getElementById('orderFilterStartDate')?.addEventListener('change', () => this.load());
        document.getElementById('orderFilterEndDate')?.addEventListener('change', () => this.load());
        document.getElementById('btnClearOrderFilters')?.addEventListener('click', () => this._clearFilters());

        // Type tabs
        document.querySelectorAll('#orderTypeTabs .nav-link').forEach(tab => {
            tab.addEventListener('click', (e) => {
                e.preventDefault();
                document.querySelectorAll('#orderTypeTabs .nav-link').forEach(t => t.classList.remove('active'));
                tab.classList.add('active');
                this._currentTypeFilter = tab.dataset.type;
                this.load();
            });
        });
    }

    // ========== MODALS ==========

    _openNewOrderModal() {
        document.getElementById('orderEditId').value = '';
        document.getElementById('orderModalTitle').textContent = 'New Order';
        this._resetOrderForm();
        // Default to Lab
        document.getElementById('orderTypeLab').checked = true;
        this._onOrderTypeChange();
        new bootstrap.Modal(document.getElementById('orderModal')).show();
    }

    async _editOrder(id) {
        try {
            const order = await apiRequest(`/orders/${id}`, { showLoader: false });
            if (!order) return;

            document.getElementById('orderEditId').value = order.OrderId;
            document.getElementById('orderModalTitle').textContent = 'Edit Order';

            // Set type
            const typeRadio = document.querySelector(`#orderTypeSelector input[value="${order.OrderType}"]`);
            if (typeRadio) typeRadio.checked = true;
            this._onOrderTypeChange();

            // Common fields
            document.getElementById('orderPatientSearch').value = order.PatientName;
            document.getElementById('orderPatientId').value = order.PatientId;
            this._selectedPatient = { PatientId: order.PatientId, PatientName: order.PatientName };
            document.getElementById('orderProviderId').value = order.ProviderId;
            document.getElementById('orderPriority').value = order.Priority;
            document.getElementById('orderDiagnosisCode').value = order.DiagnosisCode || '';
            document.getElementById('orderClinicalIndication').value = order.ClinicalIndication || '';
            document.getElementById('orderNotes').value = order.Notes || '';

            // Lab fields
            if (order.OrderType === 0) {
                document.getElementById('orderLabPanel').value = order.LabPanelName || '';
                document.getElementById('orderSpecimenType').value = order.SpecimenType || 'Blood';
                document.getElementById('orderFastingRequired').checked = order.FastingRequired || false;
                if (order.LabPanelName) this._onLabPanelChange(order.LabPanelName);
            }

            // Imaging fields
            if (order.OrderType === 1) {
                document.getElementById('orderModality').value = order.Modality ?? '';
                document.getElementById('orderBodyPart').value = order.BodyPart || '';
                document.getElementById('orderImagingFacility').value = order.ImagingFacility || '';
                document.getElementById('orderContrastRequired').checked = order.ContrastRequired || false;
            }

            // Referral fields
            if (order.OrderType === 2) {
                document.getElementById('orderReferralSpecialty').value = order.ReferralSpecialty || '';
                document.getElementById('orderReferredToProvider').value = order.ReferredToProvider || '';
                document.getElementById('orderReferralUrgency').value = order.ReferralUrgency ?? 0;
                document.getElementById('orderReferredToFacility').value = order.ReferredToFacility || '';
                document.getElementById('orderReferredToPhone').value = order.ReferredToPhone || '';
                document.getElementById('orderReferredToFax').value = order.ReferredToFax || '';
                document.getElementById('orderReferralReason').value = order.ReferralReason || '';
            }

            new bootstrap.Modal(document.getElementById('orderModal')).show();
        } catch (err) {
            console.error('_editOrder error:', err);
        }
    }

    async _viewOrder(id) {
        try {
            const order = await apiRequest(`/orders/${id}`, { showLoader: false });
            if (!order) return;

            let html = `
                <div class="row mb-3">
                    <div class="col-6"><strong>Patient:</strong> ${order.PatientName} (${order.PatientMRN})</div>
                    <div class="col-6"><strong>Provider:</strong> ${order.ProviderName}</div>
                </div>
                <div class="row mb-3">
                    <div class="col-4"><strong>Type:</strong> ${OrderConstants.TypeBadge[order.OrderType]}</div>
                    <div class="col-4"><strong>Priority:</strong> ${OrderConstants.PriorityBadge[order.Priority]}</div>
                    <div class="col-4"><strong>Status:</strong> ${OrderConstants.StatusBadge[order.Status]}</div>
                </div>
                <div class="row mb-3">
                    <div class="col-6"><strong>Date:</strong> ${order.OrderDate}</div>
                    <div class="col-6"><strong>Diagnosis:</strong> ${order.DiagnosisCode || 'N/A'}</div>
                </div>`;

            if (order.ClinicalIndication) {
                html += `<div class="mb-3"><strong>Clinical Indication:</strong> ${order.ClinicalIndication}</div>`;
            }

            // Type-specific details
            if (order.OrderType === 0) {
                html += `<hr><h6 class="text-primary"><i class="bi bi-droplet-half"></i> Lab Details</h6>
                    <div class="row mb-2">
                        <div class="col-4"><strong>Panel:</strong> ${order.LabPanelName || 'N/A'}</div>
                        <div class="col-4"><strong>Specimen:</strong> ${order.SpecimenType || 'N/A'}</div>
                        <div class="col-4"><strong>Fasting:</strong> ${order.FastingRequired ? 'Yes' : 'No'}</div>
                    </div>`;
            } else if (order.OrderType === 1) {
                html += `<hr><h6 class="text-info"><i class="bi bi-image"></i> Imaging Details</h6>
                    <div class="row mb-2">
                        <div class="col-4"><strong>Modality:</strong> ${order.ModalityName || 'N/A'}</div>
                        <div class="col-4"><strong>Body Part:</strong> ${order.BodyPart || 'N/A'}</div>
                        <div class="col-4"><strong>Contrast:</strong> ${order.ContrastRequired ? 'Yes' : 'No'}</div>
                    </div>
                    ${order.ImagingFacility ? `<div class="mb-2"><strong>Facility:</strong> ${order.ImagingFacility}</div>` : ''}`;
            } else if (order.OrderType === 2) {
                html += `<hr><h6 class="text-success"><i class="bi bi-arrow-right-circle"></i> Referral Details</h6>
                    <div class="row mb-2">
                        <div class="col-4"><strong>Specialty:</strong> ${order.ReferralSpecialty || 'N/A'}</div>
                        <div class="col-4"><strong>To Provider:</strong> ${order.ReferredToProvider || 'N/A'}</div>
                        <div class="col-4"><strong>Urgency:</strong> ${OrderConstants.ReferralUrgency[order.ReferralUrgency] || 'Routine'}</div>
                    </div>
                    ${order.ReferredToFacility ? `<div class="mb-2"><strong>Facility:</strong> ${order.ReferredToFacility}</div>` : ''}
                    ${order.ReferralReason ? `<div class="mb-2"><strong>Reason:</strong> ${order.ReferralReason}</div>` : ''}`;
            }

            // Results section
            if (order.Results && order.Results.length > 0) {
                html += `<hr><h6><i class="bi bi-clipboard-check"></i> Results</h6>`;
                if (order.OrderType === 0) {
                    html += `<div class="table-responsive"><table class="table table-sm table-bordered">
                        <thead class="table-light"><tr><th>Test</th><th>Result</th><th>Unit</th><th>Reference</th><th>Flag</th></tr></thead><tbody>`;
                    order.Results.forEach(r => {
                        const abnClass = r.IsAbnormal ? 'text-danger fw-bold' : '';
                        html += `<tr class="${abnClass}">
                            <td>${r.TestName}</td>
                            <td>${r.ResultValue || ''}</td>
                            <td>${r.ResultUnit || ''}</td>
                            <td>${r.ReferenceRange || ''}</td>
                            <td>${r.IsAbnormal ? '<i class="bi bi-exclamation-triangle text-danger"></i> Abnormal' : '<i class="bi bi-check text-success"></i> Normal'}</td>
                        </tr>`;
                    });
                    html += '</tbody></table></div>';
                } else {
                    order.Results.forEach(r => {
                        if (r.FindingsText) {
                            html += `<div class="border rounded p-3 bg-light"><pre class="mb-0" style="white-space:pre-wrap;">${r.FindingsText}</pre></div>`;
                        }
                    });
                }
            }

            if (order.Notes) {
                html += `<hr><div><strong>Notes:</strong> ${order.Notes}</div>`;
            }

            document.getElementById('orderDetailBody').innerHTML = html;

            // Footer actions
            let footerHtml = '<button type="button" class="btn btn-secondary" data-bs-dismiss="modal">Close</button>';
            // Print button: only for Lab orders (type 0) with results available.
            // Status 4 = ResultsReceived, 5 = Completed.
            if (order.OrderType === 0 && (order.Status === 4 || order.Status === 5)) {
                footerHtml += ` <button class="btn btn-primary" onclick="window._ordersModule._printLabOrder(${order.OrderId})"><i class="bi bi-printer"></i> Print Report</button>`;
            }
            if (order.Status >= 1 && order.Status <= 3) {
                footerHtml += ` <button class="btn btn-success" onclick="window._ordersModule._openResultsModal(${order.OrderId}); bootstrap.Modal.getInstance(document.getElementById('orderDetailModal')).hide();"><i class="bi bi-clipboard-check"></i> Add Results</button>`;
            }
            if (order.Status === 4) {
                footerHtml += ` <button class="btn btn-success" onclick="window._ordersModule._completeOrder(${order.OrderId}); bootstrap.Modal.getInstance(document.getElementById('orderDetailModal')).hide();"><i class="bi bi-check-circle"></i> Complete</button>`;
            }
            document.getElementById('orderDetailFooter').innerHTML = footerHtml;

            new bootstrap.Modal(document.getElementById('orderDetailModal')).show();
        } catch (err) {
            console.error('_viewOrder error:', err);
        }
    }

    // ========== PRINT LAB REPORT ==========

    /**
     * Print a finalized lab order. Pulls the server-built print payload
     * (clinic + patient + provider + results, all dates pre-formatted in
     * the location's timezone), renders the HTML template into a hidden
     * iframe, waits for the logo image to load, and triggers the browser
     * print dialog. The iframe is removed after print so the page state
     * stays clean.
     *
     * Falls back to a new tab if the iframe approach fails (e.g. some
     * embedded browsers lock down `contentWindow.print`).
     */
    async _printLabOrder(orderId) {
        if (!window.OrderPrintTemplate || !window.OrderPrintTemplate.buildLabReportHtml) {
            console.error('[OrdersModule] OrderPrintTemplate not loaded');
            if (window.showToast) window.showToast('Print template missing', 'error');
            return;
        }

        let data;
        try {
            data = await apiRequest(`/orders/${orderId}/print-data`, { showLoader: false });
        } catch (err) {
            console.error('[OrdersModule] failed to load print data', err);
            if (window.showToast) window.showToast('Failed to load report data', 'error');
            return;
        }
        if (!data) return;

        const html = window.OrderPrintTemplate.buildLabReportHtml(data);

        // Hidden iframe — cleaner than window.open (no popup blockers).
        const iframe = document.createElement('iframe');
        iframe.setAttribute('aria-hidden', 'true');
        iframe.style.position = 'fixed';
        iframe.style.right = '0';
        iframe.style.bottom = '0';
        iframe.style.width = '0';
        iframe.style.height = '0';
        iframe.style.border = '0';
        document.body.appendChild(iframe);

        const cleanup = () => {
            // Defer removal — Safari fires afterprint before the dialog
            // actually closes in some cases.
            setTimeout(() => {
                if (iframe.parentNode) iframe.parentNode.removeChild(iframe);
            }, 1000);
        };

        // Wait for the iframe document AND the logo image (if any) before printing.
        iframe.onload = () => {
            try {
                const win = iframe.contentWindow;
                const doc = iframe.contentDocument || win.document;
                const img = doc.querySelector('img');
                const fireprint = () => {
                    try {
                        win.focus();
                        win.print();
                    } catch (e) {
                        console.error('[OrdersModule] iframe print failed', e);
                    }
                    if ('onafterprint' in win) {
                        win.onafterprint = cleanup;
                    } else {
                        cleanup();
                    }
                };
                if (img && !img.complete) {
                    img.addEventListener('load', fireprint);
                    img.addEventListener('error', fireprint); // print anyway with fallback initials
                    setTimeout(fireprint, 2000); // hard timeout
                } else {
                    fireprint();
                }
            } catch (err) {
                console.error('[OrdersModule] print pipeline failed', err);
                cleanup();
            }
        };

        // Write HTML via srcdoc — supported in all modern browsers and avoids
        // a same-origin write to a blank document.
        iframe.srcdoc = html;
    }

    // ========== ORDER TYPE SWITCHING ==========

    _onOrderTypeChange() {
        const type = parseInt(document.querySelector('#orderTypeSelector input[name="orderType"]:checked')?.value || '0');
        document.getElementById('orderLabSection').style.display = type === 0 ? '' : 'none';
        document.getElementById('orderImagingSection').style.display = type === 1 ? '' : 'none';
        document.getElementById('orderReferralSection').style.display = type === 2 ? '' : 'none';
    }

    // ========== LAB PANELS ==========

    async _loadLabPanels() {
        try {
            const panels = await apiRequest('/lab-tests/panels', { showLoader: false });
            this._labPanels = panels || [];
            const sel = document.getElementById('orderLabPanel');
            if (!sel) return;
            sel.innerHTML = '<option value="">Select Panel...</option>';
            panels.forEach(p => {
                sel.innerHTML += `<option value="${p}">${p}</option>`;
            });
        } catch (err) {
            console.error('_loadLabPanels error:', err);
        }
    }

    async _onLabPanelChange(panelName) {
        const preview = document.getElementById('orderLabTestsPreview');
        const tbody = document.getElementById('orderLabTestsBody');
        if (!panelName) {
            if (preview) preview.style.display = 'none';
            return;
        }
        try {
            const tests = await apiRequest(`/lab-tests/panels/${encodeURIComponent(panelName)}`, { showLoader: false });
            if (!tests || !tests.length) {
                if (preview) preview.style.display = 'none';
                return;
            }
            tbody.innerHTML = tests.map(t => `<tr>
                <td>${t.TestName}</td>
                <td><code>${t.TestCode || ''}</code></td>
                <td>${t.Unit || ''}</td>
                <td>${t.ReferenceRange || ''}</td>
            </tr>`).join('');
            preview.style.display = '';
        } catch (err) {
            console.error('_onLabPanelChange error:', err);
        }
    }

    // ========== PROVIDERS ==========

    async _loadProviders() {
        try {
            const providers = await apiRequest('/providers', { showLoader: false });
            this._providers = providers || [];
            const sel = document.getElementById('orderProviderId');
            if (!sel || !providers) return;
            sel.innerHTML = '<option value="">Select Provider</option>';
            providers.forEach(p => {
                const name = (p.FirstName || '') + ' ' + (p.LastName || '') + (p.Credentials ? ', ' + p.Credentials : '');
                sel.innerHTML += `<option value="${p.ProviderId}">${name.trim()}</option>`;
            });
            // Auto-select current provider from user profile
            const _user = JSON.parse(localStorage.getItem('currentUser') || '{}');
            const currentProviderId = _user.ProviderId || _user.providerId;
            const userRole = parseInt(_user.Role ?? _user.role ?? -1);
            if (currentProviderId) {
                sel.value = currentProviderId;
                // Only ClinicAdmin (1), SuperAdmin (0), and FrontDesk (3) can select a different provider
                const canChangeProvider = [0, 1, 3].includes(userRole);
                sel.disabled = !canChangeProvider;
            }
        } catch (err) {
            console.error('_loadProviders error:', err);
        }
    }

    // ========== REFERRAL SPECIALTIES ==========

    _populateReferralSpecialties() {
        const sel = document.getElementById('orderReferralSpecialty');
        if (!sel) return;
        sel.innerHTML = '<option value="">Select Specialty...</option>';
        OrderConstants.ReferralSpecialties.forEach(s => {
            sel.innerHTML += `<option value="${s}">${s}</option>`;
        });
    }

    // ========== PATIENT SEARCH ==========

    async _searchPatients() {
        const query = document.getElementById('orderPatientSearch')?.value?.trim();
        const resultsDiv = document.getElementById('orderPatientResults');
        if (!query || query.length < 2) {
            resultsDiv.style.display = 'none';
            return;
        }
        try {
            const response = await apiRequest(`/patients/search?q=${encodeURIComponent(query)}&take=8&activeOnly=true`, { showLoader: false, showErrors: false });
            const patients = response?.Results || response?.Patients || (Array.isArray(response) ? response : []);
            if (!patients.length) {
                resultsDiv.style.display = 'none';
                return;
            }
            resultsDiv.innerHTML = patients.map(p => {
                const name = (p.FirstName || '') + ' ' + (p.LastName || '');
                return `<a href="#" class="list-group-item list-group-item-action py-2" data-id="${p.PatientId}" data-name="${name.trim()}" data-mrn="${p.MRN || p.Mrn || ''}">
                    <strong>${name.trim()}</strong> <small class="text-muted">(${p.MRN || p.Mrn || ''})</small>
                </a>`;
            }).join('');
            resultsDiv.style.display = '';

            // Bind click
            resultsDiv.querySelectorAll('.list-group-item').forEach(item => {
                item.addEventListener('click', (e) => {
                    e.preventDefault();
                    document.getElementById('orderPatientSearch').value = item.dataset.name + ' (' + item.dataset.mrn + ')';
                    document.getElementById('orderPatientId').value = item.dataset.id;
                    this._selectedPatient = { PatientId: parseInt(item.dataset.id), PatientName: item.dataset.name };
                    resultsDiv.style.display = 'none';
                });
            });
        } catch (err) {
            resultsDiv.style.display = 'none';
        }
    }

    // ========== SAVE ORDER ==========

    async _saveOrder(status) {
        const patientId = parseInt(document.getElementById('orderPatientId')?.value);
        const providerId = parseInt(document.getElementById('orderProviderId')?.value);

        if (!patientId) {
            if (window.showToast) window.showToast('Please select a patient', 'warning'); else alert('Please select a patient');
            return;
        }
        if (!providerId) {
            if (window.showToast) window.showToast('Please select a provider', 'warning'); else alert('Please select a provider');
            return;
        }

        const orderType = parseInt(document.querySelector('#orderTypeSelector input[name="orderType"]:checked')?.value || '0');

        const dto = {
            PatientId: patientId,
            ProviderId: providerId,
            OrderType: orderType,
            Status: status,
            Priority: parseInt(document.getElementById('orderPriority')?.value || '0'),
            DiagnosisCode: document.getElementById('orderDiagnosisCode')?.value?.trim() || null,
            ClinicalIndication: document.getElementById('orderClinicalIndication')?.value?.trim() || null,
            Notes: document.getElementById('orderNotes')?.value?.trim() || null
        };

        // Lab fields
        if (orderType === 0) {
            dto.LabPanelName = document.getElementById('orderLabPanel')?.value || null;
            dto.FastingRequired = document.getElementById('orderFastingRequired')?.checked || false;
            dto.SpecimenType = document.getElementById('orderSpecimenType')?.value?.trim() || null;
            if (!dto.LabPanelName) {
                if (window.showToast) window.showToast('Please select a lab panel', 'warning'); else alert('Please select a lab panel');
                return;
            }
        }

        // Imaging fields
        if (orderType === 1) {
            const modality = document.getElementById('orderModality')?.value;
            dto.Modality = modality !== '' ? parseInt(modality) : null;
            dto.BodyPart = document.getElementById('orderBodyPart')?.value?.trim() || null;
            dto.ContrastRequired = document.getElementById('orderContrastRequired')?.checked || false;
            dto.ImagingFacility = document.getElementById('orderImagingFacility')?.value?.trim() || null;
            if (dto.Modality === null) {
                if (window.showToast) window.showToast('Please select a modality', 'warning'); else alert('Please select a modality');
                return;
            }
            if (!dto.BodyPart) {
                if (window.showToast) window.showToast('Please enter body part', 'warning'); else alert('Please enter body part');
                return;
            }
        }

        // Referral fields
        if (orderType === 2) {
            dto.ReferralSpecialty = document.getElementById('orderReferralSpecialty')?.value || null;
            dto.ReferredToProvider = document.getElementById('orderReferredToProvider')?.value?.trim() || null;
            dto.ReferredToFacility = document.getElementById('orderReferredToFacility')?.value?.trim() || null;
            dto.ReferredToPhone = document.getElementById('orderReferredToPhone')?.value?.trim() || null;
            dto.ReferredToFax = document.getElementById('orderReferredToFax')?.value?.trim() || null;
            dto.ReferralReason = document.getElementById('orderReferralReason')?.value?.trim() || null;
            const urgency = document.getElementById('orderReferralUrgency')?.value;
            dto.ReferralUrgency = urgency !== '' ? parseInt(urgency) : 0;
            if (!dto.ReferralSpecialty) {
                if (window.showToast) window.showToast('Please select a specialty', 'warning'); else alert('Please select a specialty');
                return;
            }
        }

        try {
            const editId = document.getElementById('orderEditId')?.value;
            if (editId) {
                await apiRequest(`/orders/${editId}`, { method: 'PUT', body: dto });
            } else {
                await apiRequest('/orders', { method: 'POST', body: dto });
            }

            bootstrap.Modal.getInstance(document.getElementById('orderModal'))?.hide();
            const msg = status === 0 ? 'Order saved as draft' : 'Order submitted successfully';
            if (window.showToast) window.showToast(msg, 'success'); else alert(msg);
            this.load();
        } catch (err) {
            console.error('_saveOrder error:', err);
        }
    }

    // ========== STATUS TRANSITIONS ==========

    async _submitOrder(id) {
        this._showConfirm('Submit Order', 'Submit this order?', async () => {
            try {
                await apiRequest(`/orders/${id}`, { method: 'PUT', body: { Status: 1 } });
                window.showToast?.('Order submitted', 'success');
                this.load();
            } catch (err) {
                console.error('_submitOrder error:', err);
            }
        });
    }

    async _cancelOrder(id) {
        this._showConfirm('Cancel Order', 'Are you sure you want to cancel this order?', async () => {
            try {
                await apiRequest(`/orders/${id}/cancel`, { method: 'POST' });
                window.showToast?.('Order cancelled', 'success');
                this.load();
            } catch (err) {
                console.error('_cancelOrder error:', err);
            }
        });
    }

    async _completeOrder(id) {
        this._showConfirm('Complete Order', 'Mark this order as completed?', async () => {
            try {
                await apiRequest(`/orders/${id}`, { method: 'PUT', body: { Status: 5 } });
                window.showToast?.('Order completed', 'success');
                this.load();
            } catch (err) {
                console.error('_completeOrder error:', err);
            }
        });
    }

    // ========== RESULTS ==========

    async _openResultsModal(orderId) {
        try {
            const order = await apiRequest(`/orders/${orderId}`, { showLoader: false });
            if (!order) return;

            document.getElementById('resultsOrderId').value = order.OrderId;
            document.getElementById('resultsOrderType').value = order.OrderType;
            document.getElementById('orderResultsTitle').textContent =
                `Add Results - ${OrderConstants.getOrderDescription(order)} (${order.PatientName})`;

            if (order.OrderType === 0) {
                // Lab: show editable results table
                document.getElementById('labResultsSection').style.display = '';
                document.getElementById('findingsSection').style.display = 'none';

                // Pre-populate from catalog
                const panelName = order.LabPanelName;
                let tests = [];
                if (panelName) {
                    tests = await apiRequest(`/lab-tests/panels/${encodeURIComponent(panelName)}`, { showLoader: false }) || [];
                }

                const tbody = document.getElementById('labResultsBody');
                if (tests.length) {
                    tbody.innerHTML = tests.map((t, i) => `<tr>
                        <td><input type="text" class="form-control form-control-sm" value="${t.TestName}" data-field="TestName" readonly></td>
                        <td><input type="text" class="form-control form-control-sm" data-field="ResultValue" placeholder="Value"></td>
                        <td><input type="text" class="form-control form-control-sm" value="${t.Unit || ''}" data-field="ResultUnit" readonly></td>
                        <td><input type="text" class="form-control form-control-sm" value="${t.ReferenceRange || ''}" data-field="ReferenceRange" readonly></td>
                        <td><input type="checkbox" class="form-check-input" data-field="IsAbnormal"></td>
                    </tr>`).join('');
                } else {
                    tbody.innerHTML = `<tr>
                        <td><input type="text" class="form-control form-control-sm" data-field="TestName" placeholder="Test name"></td>
                        <td><input type="text" class="form-control form-control-sm" data-field="ResultValue" placeholder="Value"></td>
                        <td><input type="text" class="form-control form-control-sm" data-field="ResultUnit" placeholder="Unit"></td>
                        <td><input type="text" class="form-control form-control-sm" data-field="ReferenceRange" placeholder="Range"></td>
                        <td><input type="checkbox" class="form-check-input" data-field="IsAbnormal"></td>
                    </tr>`;
                }
            } else {
                // Imaging/Referral: textarea
                document.getElementById('labResultsSection').style.display = 'none';
                document.getElementById('findingsSection').style.display = '';
                document.getElementById('findingsLabel').textContent =
                    order.OrderType === 1 ? 'Imaging Findings' : 'Consultation Notes';
                document.getElementById('findingsText').value = '';
            }

            new bootstrap.Modal(document.getElementById('orderResultsModal')).show();
        } catch (err) {
            console.error('_openResultsModal error:', err);
        }
    }

    async _saveResults() {
        const orderId = parseInt(document.getElementById('resultsOrderId')?.value);
        const orderType = parseInt(document.getElementById('resultsOrderType')?.value);

        const dto = { OrderId: orderId, Results: [] };

        if (orderType === 0) {
            // Lab results from table rows
            const rows = document.querySelectorAll('#labResultsBody tr');
            rows.forEach(row => {
                const testName = row.querySelector('[data-field="TestName"]')?.value?.trim();
                const resultValue = row.querySelector('[data-field="ResultValue"]')?.value?.trim();
                if (!testName || !resultValue) return;
                dto.Results.push({
                    TestName: testName,
                    ResultValue: resultValue,
                    ResultUnit: row.querySelector('[data-field="ResultUnit"]')?.value?.trim() || null,
                    ReferenceRange: row.querySelector('[data-field="ReferenceRange"]')?.value?.trim() || null,
                    IsAbnormal: row.querySelector('[data-field="IsAbnormal"]')?.checked || false,
                    FindingsText: null
                });
            });
        } else {
            // Imaging/Referral findings
            const text = document.getElementById('findingsText')?.value?.trim();
            if (!text) {
                if (window.showToast) window.showToast('Please enter findings', 'warning'); else alert('Please enter findings');
                return;
            }
            dto.Results.push({
                TestName: orderType === 1 ? 'Imaging Findings' : 'Consultation Notes',
                ResultValue: null,
                ResultUnit: null,
                ReferenceRange: null,
                IsAbnormal: false,
                FindingsText: text
            });
        }

        if (!dto.Results.length) {
            if (window.showToast) window.showToast('Please enter at least one result', 'warning'); else alert('Please enter at least one result');
            return;
        }

        try {
            await apiRequest(`/orders/${orderId}/results`, { method: 'POST', body: dto });
            bootstrap.Modal.getInstance(document.getElementById('orderResultsModal'))?.hide();
            window.showToast?.('Results saved successfully', 'success');
            this.load();
        } catch (err) {
            console.error('_saveResults error:', err);
        }
    }

    // ========== HELPERS ==========

    _resetOrderForm() {
        document.getElementById('orderPatientSearch').value = '';
        document.getElementById('orderPatientId').value = '';
        this._selectedPatient = null;
        document.getElementById('orderPriority').value = '0';
        document.getElementById('orderDiagnosisCode').value = '';
        document.getElementById('orderClinicalIndication').value = '';
        document.getElementById('orderNotes').value = '';
        // Lab
        document.getElementById('orderLabPanel').value = '';
        document.getElementById('orderSpecimenType').value = 'Blood';
        document.getElementById('orderFastingRequired').checked = false;
        document.getElementById('orderLabTestsPreview').style.display = 'none';
        // Imaging
        document.getElementById('orderModality').value = '';
        document.getElementById('orderBodyPart').value = '';
        document.getElementById('orderImagingFacility').value = '';
        document.getElementById('orderContrastRequired').checked = false;
        // Referral
        document.getElementById('orderReferralSpecialty').value = '';
        document.getElementById('orderReferredToProvider').value = '';
        document.getElementById('orderReferralUrgency').value = '0';
        document.getElementById('orderReferredToFacility').value = '';
        document.getElementById('orderReferredToPhone').value = '';
        document.getElementById('orderReferredToFax').value = '';
        document.getElementById('orderReferralReason').value = '';
        // Re-select provider from user profile
        const _user = JSON.parse(localStorage.getItem('currentUser') || '{}');
        const currentProviderId = _user.ProviderId || _user.providerId;
        if (currentProviderId) {
            const sel = document.getElementById('orderProviderId');
            if (sel) sel.value = currentProviderId;
        }
    }

    _clearFilters() {
        document.getElementById('orderFilterSearch').value = '';
        document.getElementById('orderFilterStatus').value = '';
        document.getElementById('orderFilterStartDate').value = '';
        document.getElementById('orderFilterEndDate').value = '';
        this.load();
    }

    _showConfirm(title, message, callback) {
        document.getElementById('orderConfirmTitle').textContent = title;
        document.getElementById('orderConfirmMessage').textContent = message;
        this._confirmCallback = callback;
        new bootstrap.Modal(document.getElementById('orderConfirmModal')).show();
    }

    // ========== AI DIAGNOSIS SUGGESTION ==========

    async _suggestDiagnosis() {
        const btn = document.getElementById('btnSuggestDiagnosis');
        const suggestionsDiv = document.getElementById('orderDiagnosisSuggestions');

        // Get CC & HPI from encounter workspace if available, or from clinical indication
        let cc = '', hpi = '';
        if (window._encounterWorkspace?.encounter) {
            cc = window._encounterWorkspace.encounter.ChiefComplaint || '';
            hpi = window._encounterWorkspace.encounter.HistoryOfPresentIllness || '';
        }
        const indication = document.getElementById('orderClinicalIndication')?.value?.trim() || '';

        if (!cc && !hpi && !indication) {
            if (window.showToast) window.showToast('Please enter Chief Complaint or Clinical Indication first', 'warning');
            return;
        }

        // Show loading state
        if (btn) { btn.disabled = true; btn.innerHTML = '<i class="bi bi-hourglass-split spin me-1"></i>Thinking...'; }

        try {
            const results = await apiRequest('/ai/diagnosis/suggest', {
                method: 'POST',
                body: { ChiefComplaint: cc || indication, HPI: hpi },
                showLoader: false,
                showErrors: false
            });

            if (!results || results.length === 0) {
                if (window.showToast) window.showToast('No diagnosis codes suggested', 'info');
                return;
            }

            // Show dropdown with suggestions
            suggestionsDiv.innerHTML = results.map(r =>
                `<a href="#" class="list-group-item list-group-item-action py-2" data-code="${r.Code}">
                    <strong>${r.Code}</strong> <span class="text-muted">— ${r.Description}</span>
                </a>`
            ).join('');
            suggestionsDiv.style.display = '';

            // Bind click handlers
            suggestionsDiv.querySelectorAll('.list-group-item').forEach(item => {
                item.addEventListener('click', (e) => {
                    e.preventDefault();
                    document.getElementById('orderDiagnosisCode').value = item.dataset.code;
                    suggestionsDiv.style.display = 'none';
                });
            });

            // Auto-hide on outside click
            const hideHandler = (e) => {
                if (!e.target.closest('#orderDiagnosisSuggestions') && e.target.id !== 'btnSuggestDiagnosis') {
                    suggestionsDiv.style.display = 'none';
                    document.removeEventListener('click', hideHandler);
                }
            };
            setTimeout(() => document.addEventListener('click', hideHandler), 100);

        } catch (err) {
            console.error('_suggestDiagnosis error:', err);
            if (window.showToast) window.showToast('Failed to get diagnosis suggestions', 'error');
        } finally {
            if (btn) { btn.disabled = false; btn.innerHTML = '<i class="bi bi-stars me-1"></i>Suggest Diagnosis from CC & HPI'; }
        }
    }

    _debounce(key, fn, ms) {
        clearTimeout(this._debounceTimers[key]);
        this._debounceTimers[key] = setTimeout(fn, ms);
    }
}

// Auto-initialize (slight delay to ensure App.js + auth token are ready)
document.addEventListener('DOMContentLoaded', () => {
    const mod = new OrdersModule();
    window._ordersModule = mod;
    setTimeout(() => mod.init(), 250);
});
