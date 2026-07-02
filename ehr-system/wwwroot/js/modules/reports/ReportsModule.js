/**
 * ReportsModule - Handles reports generation and display
 *
 * Features:
 * - 10 Analytical reports with Chart.js charts, KPI cards, and data tables
 * - Category navigation (Clinical, Visits, Revenue, Providers, Patients)
 * - Monthly Patient Visit Grid report
 * - Export to PDF/Excel/Print
 * - Provider filtering
 */
class ReportsModule {
    constructor(options = {}) {
        this.api = options.api || null;
        this.eventBus = options.eventBus || null;

        // State
        this.currentReportData = null;
        this.providers = [];

        // Analytical reports definitions
        this.reports = [
            { id: 'encounter-completion', name: 'Encounter Completion', description: 'Track provider documentation compliance and signing rates', category: 'clinical', icon: 'bi-file-earmark-check', color: 'success', endpoint: '/reports/encounter-completion' },
            { id: 'orders-tracking', name: 'Orders Tracking', description: 'Monitor lab, imaging, and referral order turnaround', category: 'clinical', icon: 'bi-clipboard2-pulse', color: 'info', endpoint: '/reports/orders-tracking' },
            { id: 'prescription-analytics', name: 'Prescription Analytics', description: 'Prescribing patterns, controlled substances, top medications', category: 'clinical', icon: 'bi-capsule', color: 'primary', endpoint: '/reports/prescription-analytics' },
            { id: 'chronic-disease', name: 'Chronic Disease Panel', description: 'Population health - condition prevalence across patient panel', category: 'clinical', icon: 'bi-heart-pulse', color: 'danger', endpoint: '/reports/chronic-disease' },
            { id: 'visit-volume', name: 'Visit Volume & Type', description: 'Appointment mix, telehealth adoption, completion rates', category: 'visits', icon: 'bi-bar-chart-line', color: 'primary', endpoint: '/reports/visit-volume' },
            { id: 'noshow-rate', name: 'No-Show & Cancellation', description: 'No-show patterns, frequent offenders, revenue impact', category: 'visits', icon: 'bi-calendar-x', color: 'danger', endpoint: '/reports/noshow-rate' },
            { id: 'revenue-claims', name: 'Revenue & Claims', description: 'Claims lifecycle, collection rates, A/R performance', category: 'revenue', icon: 'bi-cash-stack', color: 'success', endpoint: '/reports/revenue-claims' },
            { id: 'payer-mix', name: 'Payer Mix', description: 'Insurance distribution and reimbursement analysis', category: 'revenue', icon: 'bi-pie-chart', color: 'info', endpoint: '/reports/payer-mix' },
            { id: 'provider-productivity', name: 'Provider Productivity', description: 'Provider output, caseload, documentation, and ordering metrics', category: 'providers', icon: 'bi-person-lines-fill', color: 'success', endpoint: '/reports/provider-productivity' },
            { id: 'patient-panel', name: 'Patient Panel Overview', description: 'Demographics, engagement, and care gaps analysis', category: 'patients', icon: 'bi-people-fill', color: 'warning', endpoint: '/reports/patient-panel' },
            { id: 'copay-collection', name: 'Copay Collection', description: 'Outstanding copay balances, collection rates, payment plans', category: 'revenue', icon: 'bi-wallet2', color: 'warning', endpoint: '/reports/copay-collection' },
            { id: 'ar-aging', name: 'A/R Aging', description: 'Accounts receivable aging by payer and time buckets', category: 'revenue', icon: 'bi-hourglass-split', color: 'danger', endpoint: '/reports/ar-aging' },
            { id: 'payment-analysis', name: 'Payment Analysis', description: 'Payment breakdown by type, method, and trend', category: 'revenue', icon: 'bi-credit-card-2-front', color: 'primary', endpoint: '/reports/payment-analysis' }
        ];
        this.currentReportId = null;
        this.currentCategory = 'all';
        this.chartInstance = null;
        this.locations = [];
        this.searchTerm = '';

        // Bound handlers
        this._boundHandlers = {};
    }

    /**
     * Initialize the module
     */
    async init() {
        this._bindEvents();
        this._initYearFilter();
        await this._loadTherapistFilter();
    }

    /**
     * Load reports page - enters fullwidth reports mode
     */
    async load() {
        // Check access
        const user = this._getCurrentUser();
        if (user?.Role > 1) {
            this._showToast('Access Denied', 'You do not have permission to view reports.', 'error');
            return;
        }

        this._enterReportsMode();
        await this._loadFilterData();
        this._updateBadgeCounts();
        this.showReportList('all');
    }

    // ========================================
    // Mode Switching
    // ========================================

    _enterReportsMode() {
        document.getElementById('sidebar')?.classList.add('d-none');
        document.querySelector('.main-content')?.classList.add('report-fullwidth');
        document.getElementById('reportsLayout')?.style.setProperty('display', 'flex');
    }

    exitReportsMode() {
        document.getElementById('sidebar')?.classList.remove('d-none');
        document.querySelector('.main-content')?.classList.remove('report-fullwidth');
        if (typeof window.navigateTo === 'function') {
            window.navigateTo('dashboard');
        } else {
            window.location.href = '/Home/Dashboard';
        }
    }

    // ========================================
    // Category Navigation
    // ========================================

    selectCategory(category, e) {
        if (e) e.preventDefault();
        this.currentCategory = category;
        document.querySelectorAll('.reports-nav-item').forEach(item => {
            item.classList.toggle('active', item.dataset.category === category);
        });
        if (category === 'monthly-grid') {
            this._showMonthlyGrid();
        } else {
            this.showReportList(category);
        }
    }

    /**
     * Show the reports list view with category filtering
     */
    showReportList(category) {
        document.getElementById('reportListView')?.classList.remove('d-none');
        document.getElementById('reportDetailView')?.classList.add('d-none');
        document.getElementById('monthlyPatientVisitGridView')?.classList.add('d-none');

        const title = category === 'all' ? 'All Reports' : category.charAt(0).toUpperCase() + category.slice(1) + ' Reports';
        const titleEl = document.getElementById('reportListTitle');
        if (titleEl) titleEl.textContent = title;

        const filtered = this._getFilteredReports(category);
        this._renderReportCards(filtered);
    }

    /**
     * Show the monthly patient visit grid view
     */
    _showMonthlyGrid() {
        document.getElementById('reportListView')?.classList.add('d-none');
        document.getElementById('reportDetailView')?.classList.add('d-none');
        document.getElementById('monthlyPatientVisitGridView')?.classList.remove('d-none');

        // Initialize filters if not already done
        this._initYearFilter();
        this._loadTherapistFilter();
    }

    /**
     * Legacy wrapper - Show the monthly patient visit grid view
     */
    showMonthlyPatientVisitGrid() {
        this._enterReportsMode();
        this._showMonthlyGrid();
    }

    _getFilteredReports(category) {
        let reports = category === 'all' ? this.reports : this.reports.filter(r => r.category === category);
        if (this.searchTerm) {
            const term = this.searchTerm.toLowerCase();
            reports = reports.filter(r => r.name.toLowerCase().includes(term) || r.description.toLowerCase().includes(term));
        }
        return reports;
    }

    searchReports(term) {
        this.searchTerm = term;
        this.showReportList(this.currentCategory);
    }

    _renderReportCards(reports) {
        const container = document.getElementById('reportListCards');
        if (!container) return;
        if (reports.length === 0) {
            container.innerHTML = '<div class="col-12 text-center py-5"><i class="bi bi-search display-4 text-muted"></i><p class="mt-2 text-muted">No reports found.</p></div>';
            return;
        }
        container.innerHTML = reports.map(r => `
            <div class="col-md-6 col-lg-4">
                <div class="card report-list-card" onclick="openReport('${r.id}')">
                    <div class="card-body">
                        <div class="d-flex align-items-center mb-2">
                            <div class="report-list-icon bg-${r.color} bg-opacity-10 text-${r.color}">
                                <i class="bi ${r.icon}"></i>
                            </div>
                            <h6 class="mb-0 ms-3">${r.name}</h6>
                        </div>
                        <p class="text-muted small mb-0">${r.description}</p>
                    </div>
                    <div class="card-footer bg-transparent border-top-0 pt-0">
                        <small class="text-primary"><i class="bi bi-arrow-right me-1"></i>View Report</small>
                    </div>
                </div>
            </div>
        `).join('');
    }

    _updateBadgeCounts() {
        const counts = { all: this.reports.length };
        this.reports.forEach(r => { counts[r.category] = (counts[r.category] || 0) + 1; });
        Object.keys(counts).forEach(cat => {
            const badge = document.getElementById(`badge${cat.charAt(0).toUpperCase() + cat.slice(1)}`);
            if (badge) badge.textContent = counts[cat];
        });
    }

    // ========================================
    // Report Detail - Open / Run / Back
    // ========================================

    async openReport(reportId) {
        const report = this.reports.find(r => r.id === reportId);
        if (!report) return;
        this.currentReportId = reportId;

        document.getElementById('reportListView')?.classList.add('d-none');
        document.getElementById('monthlyPatientVisitGridView')?.classList.add('d-none');
        document.getElementById('reportDetailView')?.classList.remove('d-none');
        const titleEl = document.getElementById('reportDetailTitle');
        if (titleEl) titleEl.textContent = report.name;

        await this._runReport(report);
    }

    async runCurrentReport() {
        const report = this.reports.find(r => r.id === this.currentReportId);
        if (report) await this._runReport(report);
    }

    async _runReport(report) {
        this._showReportLoading(true);
        try {
            const filters = this._getFilters();
            const data = await this._apiPost(report.endpoint, filters);
            this.currentReportData = data;

            const kpis = data.Kpis || data.kpis || [];
            const chartData = data.ChartData || data.chartData || data.ByDayOfWeek || data.byDayOfWeek || [];
            const rows = data.Rows || data.rows || [];

            if (rows.length === 0 && kpis.length === 0) {
                this._showReportEmpty(true);
                return;
            }

            this._showReportLoading(false);
            this._renderKpis(kpis);
            this._renderReportChart(report, data);
            this._renderReportTable(report, data);
            const countEl = document.getElementById('reportTableCount');
            if (countEl) countEl.textContent = `Showing ${rows.length} results`;
        } catch (err) {
            console.error('Report error:', err);
            this._showReportEmpty(true);
        }
    }

    /**
     * Go back from report detail to report list
     */
    backToReportList() {
        if (this.chartInstance) { this.chartInstance.destroy(); this.chartInstance = null; }
        this.currentReportId = null;
        this.currentReportData = null;
        document.getElementById('reportDetailView')?.classList.add('d-none');
        document.getElementById('monthlyPatientVisitGridView')?.classList.add('d-none');
        this.showReportList(this.currentCategory);
    }

    /**
     * Legacy back from monthly grid report
     */
    backFromReport() {
        this.currentReportData = null;
        const gridWrapper = document.getElementById('visitGridWrapper');
        const reportFooter = document.getElementById('reportFooter');
        if (gridWrapper) gridWrapper.style.display = 'none';
        if (reportFooter) reportFooter.style.display = 'none';
        this.backToReportList();
    }

    // ========================================
    // KPI Rendering
    // ========================================

    _renderKpis(kpis) {
        const container = document.getElementById('reportKpiCards');
        if (!container) return;
        if (!kpis || kpis.length === 0) { container.innerHTML = ''; return; }
        const colClass = kpis.length <= 3 ? 'col-md-4' : 'col-md-3';
        container.innerHTML = kpis.map(k => {
            const label = k.Label || k.label || '';
            const value = k.Value || k.value || '0';
            const change = k.ChangePercent ?? k.changePercent;
            let changeHtml = '';
            if (change !== null && change !== undefined) {
                const cls = change > 0 ? 'positive' : change < 0 ? 'negative' : 'neutral';
                const arrow = change > 0 ? '&#9650;' : change < 0 ? '&#9660;' : '';
                changeHtml = `<div class="report-kpi-change ${cls}">${arrow} ${Math.abs(change)}% vs prior period</div>`;
            }
            return `<div class="${colClass}"><div class="report-kpi-card"><div class="report-kpi-value">${this._escape(value)}</div><div class="report-kpi-label">${this._escape(label)}</div>${changeHtml}</div></div>`;
        }).join('');
    }

    // ========================================
    // Chart Rendering - Dispatcher + 10 Builders
    // ========================================

    _renderReportChart(report, data) {
        if (this.chartInstance) { this.chartInstance.destroy(); this.chartInstance = null; }
        const canvas = document.getElementById('reportChart');
        const chartCard = document.getElementById('reportChartCard');
        if (!canvas || !chartCard) return;
        const ctx = canvas.getContext('2d');
        let config = null;

        switch (report.id) {
            case 'encounter-completion': config = this._chartEncounterCompletion(data); break;
            case 'orders-tracking': config = this._chartOrdersTracking(data); break;
            case 'prescription-analytics': config = this._chartPrescriptionAnalytics(data); break;
            case 'chronic-disease': config = this._chartChronicDisease(data); break;
            case 'visit-volume': config = this._chartVisitVolume(data); break;
            case 'noshow-rate': config = this._chartNoShowRate(data); break;
            case 'revenue-claims': config = this._chartRevenueClaims(data); break;
            case 'payer-mix': config = this._chartPayerMix(data); break;
            case 'provider-productivity': config = this._chartProviderProductivity(data); break;
            case 'patient-panel': config = this._chartPatientPanel(data); break;
            case 'copay-collection': config = this._chartCopayCollection(data); break;
            case 'ar-aging': config = this._chartArAging(data); break;
            case 'payment-analysis': config = this._chartPaymentAnalysis(data); break;
            default: chartCard.classList.add('d-none'); return;
        }

        if (config) {
            chartCard.classList.remove('d-none');
            this.chartInstance = new Chart(ctx, config);
        } else {
            chartCard.classList.add('d-none');
        }
    }

    _defaultChartOptions(title, isCurrency = false) {
        return {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                title: { display: true, text: title, font: { size: 14 } },
                legend: { position: 'top', labels: { font: { size: 11 }, usePointStyle: true, padding: 12 } },
                tooltip: isCurrency ? { callbacks: { label: ctx => `${ctx.dataset.label}: $${ctx.parsed.y?.toLocaleString() ?? ctx.parsed.x?.toLocaleString()}` } } : {}
            },
            scales: { y: { beginAtZero: true } }
        };
    }

    _chartEncounterCompletion(data) {
        const cd = data.ChartData || data.chartData || [];
        if (cd.length === 0) return null;
        return {
            type: 'bar',
            data: {
                labels: cd.map(d => (d.ProviderName || d.providerName || '').split(',')[0]),
                datasets: [
                    { label: 'Signed', data: cd.map(d => d.Signed || d.signed || 0), backgroundColor: 'rgba(25,135,84,0.7)', borderRadius: 4 },
                    { label: 'Open', data: cd.map(d => d.Open || d.open || 0), backgroundColor: 'rgba(255,193,7,0.7)', borderRadius: 4 },
                    { label: 'Draft', data: cd.map(d => d.Draft || d.draft || 0), backgroundColor: 'rgba(108,117,125,0.5)', borderRadius: 4 }
                ]
            },
            options: { ...this._defaultChartOptions('Encounters by Provider'), scales: { x: { stacked: true }, y: { stacked: true, beginAtZero: true } } }
        };
    }

    _chartOrdersTracking(data) {
        const cd = data.ChartData || data.chartData || [];
        if (cd.length === 0) return null;
        return {
            type: 'bar',
            data: {
                labels: cd.map(d => d.OrderType || d.orderType),
                datasets: [
                    { label: 'Pending', data: cd.map(d => d.Pending || d.pending || 0), backgroundColor: 'rgba(255,193,7,0.7)', borderRadius: 4 },
                    { label: 'Completed', data: cd.map(d => d.Completed || d.completed || 0), backgroundColor: 'rgba(25,135,84,0.7)', borderRadius: 4 },
                    { label: 'Cancelled', data: cd.map(d => d.Cancelled || d.cancelled || 0), backgroundColor: 'rgba(220,53,69,0.7)', borderRadius: 4 }
                ]
            },
            options: this._defaultChartOptions('Orders by Type & Status')
        };
    }

    _chartPrescriptionAnalytics(data) {
        const cd = data.ChartData || data.chartData || [];
        if (cd.length === 0) return null;
        const colors = ['#0d6efd','#198754','#ffc107','#dc3545','#6f42c1','#0dcaf0','#fd7e14','#20c997','#6610f2','#d63384'];
        return {
            type: 'doughnut',
            data: {
                labels: cd.map(d => d.DrugName || d.drugName),
                datasets: [{ data: cd.map(d => d.Count || d.count || 0), backgroundColor: colors.slice(0, cd.length), borderWidth: 2 }]
            },
            options: { responsive: true, maintainAspectRatio: false, plugins: { title: { display: true, text: 'Top Prescribed Medications', font: { size: 14 } }, legend: { position: 'right', labels: { font: { size: 11 }, padding: 12 } } } }
        };
    }

    _chartChronicDisease(data) {
        const cd = data.ChartData || data.chartData || [];
        if (cd.length === 0) return null;
        return {
            type: 'bar',
            data: {
                labels: cd.map(d => `${d.IcdCode || d.icdCode} - ${(d.Description || d.description || '').substring(0, 30)}`),
                datasets: [{ label: 'Active Patients', data: cd.map(d => d.ActivePatientCount || d.activePatientCount || 0), backgroundColor: 'rgba(13,110,253,0.7)', borderRadius: 4 }]
            },
            options: { ...this._defaultChartOptions('Top Conditions by Patient Count'), indexAxis: 'y', plugins: { legend: { display: false }, title: { display: true, text: 'Top Conditions by Patient Count', font: { size: 14 } } }, scales: { x: { beginAtZero: true } } }
        };
    }

    _chartVisitVolume(data) {
        const cd = data.ChartData || data.chartData || [];
        if (cd.length === 0) return null;
        return {
            type: 'bar',
            data: {
                labels: cd.map(d => d.Label || d.label),
                datasets: [
                    { label: 'Completed', data: cd.map(d => d.Completed || d.completed || 0), backgroundColor: 'rgba(25,135,84,0.7)', borderRadius: 4 },
                    { label: 'No-Show', data: cd.map(d => d.NoShow || d.noShow || 0), backgroundColor: 'rgba(220,53,69,0.7)', borderRadius: 4 },
                    { label: 'Cancelled', data: cd.map(d => d.Cancelled || d.cancelled || 0), backgroundColor: 'rgba(108,117,125,0.5)', borderRadius: 4 }
                ]
            },
            options: { ...this._defaultChartOptions('Visit Volume by Period'), scales: { x: { stacked: true }, y: { stacked: true, beginAtZero: true } } }
        };
    }

    _chartNoShowRate(data) {
        const byDay = data.ByDayOfWeek || data.byDayOfWeek || [];
        if (byDay.length === 0) return null;
        return {
            type: 'bar',
            data: {
                labels: byDay.map(d => d.DayOfWeek || d.dayOfWeek),
                datasets: [{
                    label: 'No-Show Rate (%)',
                    data: byDay.map(d => d.Rate || d.rate || 0),
                    backgroundColor: byDay.map(d => {
                        const rate = d.Rate || d.rate || 0;
                        return rate > 20 ? 'rgba(220,53,69,0.7)' : rate > 10 ? 'rgba(255,193,7,0.7)' : 'rgba(25,135,84,0.7)';
                    }),
                    borderRadius: 4
                }]
            },
            options: this._defaultChartOptions('No-Show Rate by Day of Week')
        };
    }

    _chartRevenueClaims(data) {
        const cd = data.ChartData || data.chartData || [];
        if (cd.length === 0) return null;
        return {
            type: 'bar',
            data: {
                labels: cd.map(d => d.Label || d.label),
                datasets: [
                    { label: 'Billed', data: cd.map(d => d.TotalBilled || d.totalBilled || 0), backgroundColor: 'rgba(13,110,253,0.7)', borderRadius: 4 },
                    { label: 'Paid', data: cd.map(d => d.TotalPaid || d.totalPaid || 0), backgroundColor: 'rgba(25,135,84,0.7)', borderRadius: 4 },
                    { label: 'Denied', data: cd.map(d => d.TotalDenied || d.totalDenied || 0), backgroundColor: 'rgba(220,53,69,0.7)', borderRadius: 4 }
                ]
            },
            options: this._defaultChartOptions('Revenue by Period', true)
        };
    }

    _chartPayerMix(data) {
        const cd = data.ChartData || data.chartData || [];
        if (cd.length === 0) return null;
        const colors = ['#0d6efd','#198754','#ffc107','#dc3545','#6f42c1','#0dcaf0','#fd7e14','#20c997','#6610f2','#d63384'];
        return {
            type: 'doughnut',
            data: {
                labels: cd.map(d => d.PayerName || d.payerName),
                datasets: [{ data: cd.map(d => d.VisitCount || d.visitCount || 0), backgroundColor: colors.slice(0, cd.length), borderWidth: 2 }]
            },
            options: { responsive: true, maintainAspectRatio: false, plugins: { title: { display: true, text: 'Visit Distribution by Payer', font: { size: 14 } }, legend: { position: 'right', labels: { font: { size: 11 }, padding: 12 } } } }
        };
    }

    _chartProviderProductivity(data) {
        const cd = data.ChartData || data.chartData || [];
        if (cd.length === 0) return null;
        return {
            type: 'bar',
            data: {
                labels: cd.map(d => (d.ProviderName || d.providerName || '').split(',')[0]),
                datasets: [
                    { label: 'Completed Visits', data: cd.map(d => d.CompletedVisits || d.completedVisits || 0), backgroundColor: 'rgba(25,135,84,0.7)', borderRadius: 4 },
                    { label: 'Encounters Signed', data: cd.map(d => d.EncountersSigned || d.encountersSigned || 0), backgroundColor: 'rgba(13,110,253,0.7)', borderRadius: 4 },
                    { label: 'No-Shows', data: cd.map(d => d.NoShows || d.noShows || 0), backgroundColor: 'rgba(220,53,69,0.7)', borderRadius: 4 }
                ]
            },
            options: this._defaultChartOptions('Provider Comparison')
        };
    }

    _chartPatientPanel(data) {
        const cd = data.ChartData || data.chartData || [];
        if (cd.length === 0) return null;
        return {
            type: 'bar',
            data: {
                labels: cd.map(d => d.AgeGroup || d.ageGroup),
                datasets: [{ label: 'Patients', data: cd.map(d => d.TotalPatients || d.totalPatients || 0), backgroundColor: 'rgba(13,110,253,0.7)', borderRadius: 4 }]
            },
            options: { ...this._defaultChartOptions('Patients by Age Group'), plugins: { legend: { display: false }, title: { display: true, text: 'Patients by Age Group', font: { size: 14 } } } }
        };
    }

    _chartCopayCollection(data) {
        const cd = data.ChartData || data.chartData || [];
        if (cd.length === 0) return null;
        return {
            type: 'bar',
            data: {
                labels: cd.map(d => d.Label || d.label),
                datasets: [
                    { label: 'Owed', data: cd.map(d => d.TotalOwed || d.totalOwed || 0), backgroundColor: 'rgba(220,53,69,0.7)', borderRadius: 4 },
                    { label: 'Collected', data: cd.map(d => d.TotalCollected || d.totalCollected || 0), backgroundColor: 'rgba(25,135,84,0.7)', borderRadius: 4 }
                ]
            },
            options: this._defaultChartOptions('Copay: Owed vs Collected', true)
        };
    }

    _chartArAging(data) {
        const cd = data.ChartData || data.chartData || [];
        if (cd.length === 0) return null;
        const colors = ['rgba(25,135,84,0.7)', 'rgba(255,193,7,0.7)', 'rgba(253,126,20,0.7)', 'rgba(220,53,69,0.7)'];
        return {
            type: 'bar',
            data: {
                labels: cd.map(d => d.Bucket || d.bucket),
                datasets: [{ label: 'Outstanding', data: cd.map(d => d.Amount || d.amount || 0), backgroundColor: colors, borderRadius: 4 }]
            },
            options: { ...this._defaultChartOptions('A/R Aging Buckets', true), plugins: { legend: { display: false }, title: { display: true, text: 'A/R Aging Buckets', font: { size: 14 } } } }
        };
    }

    _chartPaymentAnalysis(data) {
        const cd = data.ChartData || data.chartData || [];
        if (cd.length === 0) return null;
        const colors = ['#0d6efd', '#198754', '#ffc107', '#dc3545', '#6f42c1', '#0dcaf0', '#fd7e14'];
        return {
            type: 'doughnut',
            data: {
                labels: cd.map(d => d.Label || d.label),
                datasets: [{ data: cd.map(d => d.Amount || d.amount || 0), backgroundColor: colors.slice(0, cd.length) }]
            },
            options: { ...this._defaultChartOptions('Payment Distribution'), plugins: { legend: { position: 'right', labels: { padding: 12 } }, title: { display: true, text: 'Payment Distribution', font: { size: 14 } } } }
        };
    }

    // ========================================
    // Table Rendering - Switch with Column Definitions
    // ========================================

    _renderReportTable(report, data) {
        const thead = document.getElementById('reportTableHead');
        const tbody = document.getElementById('reportTableBody');
        const tfoot = document.getElementById('reportTableFoot');
        if (!thead || !tbody) return;
        if (tfoot) tfoot.innerHTML = '';
        const rows = data.Rows || data.rows || [];

        switch (report.id) {
            case 'encounter-completion':
                thead.innerHTML = '<tr><th>Provider</th><th>Total</th><th>Signed</th><th>Open</th><th>Draft</th><th>Amended</th><th>Completion %</th><th>Avg Days to Sign</th></tr>';
                tbody.innerHTML = rows.map(r => `<tr><td>${this._escape(r.ProviderName || r.providerName)}</td><td>${r.TotalEncounters || r.totalEncounters || 0}</td><td>${r.Signed || r.signed || 0}</td><td>${r.Open || r.open || 0}</td><td>${r.Draft || r.draft || 0}</td><td>${r.Amended || r.amended || 0}</td><td>${r.CompletionRate || r.completionRate || 0}%</td><td>${r.AvgDaysToSign || r.avgDaysToSign || 0}</td></tr>`).join('');
                break;

            case 'orders-tracking':
                thead.innerHTML = '<tr><th>Order Type</th><th>Total</th><th>Pending</th><th>Sent</th><th>Results</th><th>Completed</th><th>Cancelled</th><th>Avg Turnaround</th></tr>';
                tbody.innerHTML = rows.map(r => `<tr><td>${this._escape(r.OrderType || r.orderType)}</td><td>${r.TotalOrdered || r.totalOrdered || 0}</td><td>${r.Pending || r.pending || 0}</td><td>${r.Sent || r.sent || 0}</td><td>${r.ResultsReceived || r.resultsReceived || 0}</td><td>${r.Completed || r.completed || 0}</td><td>${r.Cancelled || r.cancelled || 0}</td><td>${r.AvgTurnaroundDays || r.avgTurnaroundDays || 0} days</td></tr>`).join('');
                break;

            case 'prescription-analytics':
                thead.innerHTML = '<tr><th>Drug</th><th>Generic</th><th>Times Rx\'d</th><th>Avg Qty</th><th>Avg Refills</th><th>Controlled</th><th>Top Prescriber</th></tr>';
                tbody.innerHTML = rows.map(r => `<tr><td>${this._escape(r.DrugName || r.drugName)}</td><td>${this._escape(r.GenericName || r.genericName || '')}</td><td>${r.TimesPrescribed || r.timesPrescribed || 0}</td><td>${r.AvgQuantity || r.avgQuantity || 0}</td><td>${r.AvgRefills || r.avgRefills || 0}</td><td>${r.ControlledCount || r.controlledCount || 0}</td><td>${this._escape(r.TopPrescriber || r.topPrescriber || '')}</td></tr>`).join('');
                break;

            case 'chronic-disease':
                thead.innerHTML = '<tr><th>ICD Code</th><th>Description</th><th>Active Patients</th><th>Resolved</th><th>Avg Duration (days)</th></tr>';
                tbody.innerHTML = rows.map(r => `<tr><td><code>${this._escape(r.IcdCode || r.icdCode)}</code></td><td>${this._escape(r.Description || r.description)}</td><td>${r.ActivePatientCount || r.activePatientCount || 0}</td><td>${r.ResolvedCount || r.resolvedCount || 0}</td><td>${r.AvgDurationDays || r.avgDurationDays || 0}</td></tr>`).join('');
                break;

            case 'visit-volume':
                thead.innerHTML = '<tr><th>Period</th><th>Scheduled</th><th>Completed</th><th>No-Show</th><th>Cancelled</th><th>Completion %</th><th>New</th><th>Telehealth</th></tr>';
                tbody.innerHTML = rows.map(r => `<tr><td>${this._escape(r.Period || r.period)}</td><td>${r.TotalScheduled || r.totalScheduled || 0}</td><td>${r.Completed || r.completed || 0}</td><td>${r.NoShow || r.noShow || 0}</td><td>${r.Cancelled || r.cancelled || 0}</td><td>${r.CompletionRate || r.completionRate || 0}%</td><td>${r.NewPatient || r.newPatient || 0}</td><td>${r.Telehealth || r.telehealth || 0}</td></tr>`).join('');
                break;

            case 'noshow-rate':
                thead.innerHTML = '<tr><th>Patient</th><th>No-Shows</th><th>Cancellations</th><th>Total Appts</th><th>Rate %</th><th>Last No-Show</th><th>Insurance</th></tr>';
                tbody.innerHTML = rows.map(r => `<tr><td><a href="#" class="text-primary" data-action="view-patient" data-patient-id="${r.PatientId || r.patientId}">${this._escape(r.PatientName || r.patientName)}</a></td><td><span class="badge bg-danger">${r.NoShowCount || r.noShowCount || 0}</span></td><td>${r.CancellationCount || r.cancellationCount || 0}</td><td>${r.TotalAppointments || r.totalAppointments || 0}</td><td>${r.Rate || r.rate || 0}%</td><td>${this._formatDate(r.LastNoShow || r.lastNoShow)}</td><td>${this._escape(r.Insurance || r.insurance || '')}</td></tr>`).join('');
                break;

            case 'revenue-claims':
                thead.innerHTML = '<tr><th>Period</th><th>Claims</th><th>Billed</th><th>Paid</th><th>Denied</th><th>Pending</th><th>Collection %</th><th>Avg Days</th></tr>';
                tbody.innerHTML = rows.map(r => `<tr><td>${this._escape(r.Period || r.period)}</td><td>${r.TotalClaims || r.totalClaims || 0}</td><td>$${(r.TotalBilled || r.totalBilled || 0).toLocaleString()}</td><td>$${(r.TotalPaid || r.totalPaid || 0).toLocaleString()}</td><td>$${(r.TotalDenied || r.totalDenied || 0).toLocaleString()}</td><td>$${(r.TotalPending || r.totalPending || 0).toLocaleString()}</td><td>${r.CollectionRate || r.collectionRate || 0}%</td><td>${r.AvgDaysToPayment || r.avgDaysToPayment || 0}</td></tr>`).join('');
                break;

            case 'payer-mix':
                thead.innerHTML = '<tr><th>Payer</th><th>Patients</th><th>Visits</th><th>Billed</th><th>Paid</th><th>Avg Reimb.</th><th>Share</th></tr>';
                tbody.innerHTML = rows.map(r => `<tr><td>${this._escape(r.PayerName || r.payerName)}</td><td>${r.ActivePatients || r.activePatients || 0}</td><td>${r.TotalVisits || r.totalVisits || 0}</td><td>$${(r.TotalBilled || r.totalBilled || 0).toLocaleString()}</td><td>$${(r.TotalPaid || r.totalPaid || 0).toLocaleString()}</td><td>$${(r.AvgReimbursement || r.avgReimbursement || 0).toLocaleString()}</td><td><div class="d-flex align-items-center"><div class="progress flex-grow-1 me-2" style="height:6px;"><div class="progress-bar" style="width:${r.Percentage || r.percentage || 0}%"></div></div><small>${r.Percentage || r.percentage || 0}%</small></div></td></tr>`).join('');
                break;

            case 'provider-productivity':
                thead.innerHTML = '<tr><th>Provider</th><th>Scheduled</th><th>Completed</th><th>No-Shows</th><th>Completion %</th><th>Encounters</th><th>Signed</th><th>Rx</th><th>Orders</th><th>Patients</th></tr>';
                tbody.innerHTML = rows.map(r => `<tr><td>${this._escape(r.ProviderName || r.providerName)}</td><td>${r.TotalScheduled || r.totalScheduled || 0}</td><td>${r.Completed || r.completed || 0}</td><td>${r.NoShows || r.noShows || 0}</td><td>${r.CompletionRate || r.completionRate || 0}%</td><td>${r.EncountersCreated || r.encountersCreated || 0}</td><td>${r.EncountersSigned || r.encountersSigned || 0}</td><td>${r.PrescriptionsWritten || r.prescriptionsWritten || 0}</td><td>${r.OrdersPlaced || r.ordersPlaced || 0}</td><td>${r.ActivePatients || r.activePatients || 0}</td></tr>`).join('');
                break;

            case 'patient-panel':
                thead.innerHTML = '<tr><th>Age Group</th><th>Total Patients</th><th>Seen in Period</th><th>Not Seen 90+ Days</th><th>Avg Visits</th></tr>';
                tbody.innerHTML = rows.map(r => `<tr><td>${this._escape(r.AgeGroup || r.ageGroup)}</td><td>${r.TotalPatients || r.totalPatients || 0}</td><td>${r.SeenInPeriod || r.seenInPeriod || 0}</td><td>${r.NotSeenIn90Days || r.notSeenIn90Days || 0}</td><td>${r.AvgVisitsPerPatient || r.avgVisitsPerPatient || 0}</td></tr>`).join('');
                break;
            case 'copay-collection':
                thead.innerHTML = '<tr><th>Patient</th><th>Total Charges</th><th>Insurance Paid</th><th>Patient Paid</th><th>Adjustments</th><th>Balance</th><th>Plan</th><th>Last Payment</th></tr>';
                tbody.innerHTML = rows.map(r => `<tr><td>${this._escape(r.PatientName || r.patientName)}</td><td>$${(r.TotalCharges || r.totalCharges || 0).toLocaleString()}</td><td>$${(r.InsurancePaid || r.insurancePaid || 0).toLocaleString()}</td><td>$${(r.PatientPaid || r.patientPaid || 0).toLocaleString()}</td><td>$${(r.Adjustments || r.adjustments || 0).toLocaleString()}</td><td class="fw-bold ${(r.Balance || r.balance || 0) > 0 ? 'text-danger' : 'text-success'}">$${(r.Balance || r.balance || 0).toLocaleString()}</td><td>${(r.HasInstallmentPlan || r.hasInstallmentPlan) ? '<span class="badge bg-info">Active</span>' : '-'}</td><td>${this._escape(r.LastPaymentDate || r.lastPaymentDate || '-')}</td></tr>`).join('');
                break;
            case 'ar-aging':
                thead.innerHTML = '<tr><th>Payer</th><th>0-30 Days</th><th>31-60 Days</th><th>61-90 Days</th><th>90+ Days</th><th>Total</th><th>Claims</th></tr>';
                tbody.innerHTML = rows.map(r => `<tr><td>${this._escape(r.PayerName || r.payerName)}</td><td>$${(r.Current || r.current || 0).toLocaleString()}</td><td>$${(r.Days31To60 || r.days31To60 || 0).toLocaleString()}</td><td>$${(r.Days61To90 || r.days61To90 || 0).toLocaleString()}</td><td class="${(r.Over90 || r.over90 || 0) > 0 ? 'text-danger fw-bold' : ''}">$${(r.Over90 || r.over90 || 0).toLocaleString()}</td><td class="fw-bold">$${(r.Total || r.total || 0).toLocaleString()}</td><td>${r.ClaimCount || r.claimCount || 0}</td></tr>`).join('');
                tfoot.innerHTML = `<tr class="table-light fw-bold"><td>Total</td><td>$${rows.reduce((s,r) => s + (r.Current || r.current || 0), 0).toLocaleString()}</td><td>$${rows.reduce((s,r) => s + (r.Days31To60 || r.days31To60 || 0), 0).toLocaleString()}</td><td>$${rows.reduce((s,r) => s + (r.Days61To90 || r.days61To90 || 0), 0).toLocaleString()}</td><td>$${rows.reduce((s,r) => s + (r.Over90 || r.over90 || 0), 0).toLocaleString()}</td><td>$${rows.reduce((s,r) => s + (r.Total || r.total || 0), 0).toLocaleString()}</td><td>${rows.reduce((s,r) => s + (r.ClaimCount || r.claimCount || 0), 0)}</td></tr>`;
                break;
            case 'payment-analysis':
                thead.innerHTML = '<tr><th>Payment Type</th><th>Method</th><th>Count</th><th>Total</th><th>Avg Amount</th><th>Share %</th></tr>';
                tbody.innerHTML = rows.map(r => `<tr><td>${this._escape(r.PaymentType || r.paymentType)}</td><td>${this._escape(r.PaymentMethod || r.paymentMethod)}</td><td>${r.Count || r.count || 0}</td><td>$${(r.TotalAmount || r.totalAmount || 0).toLocaleString()}</td><td>$${(r.AvgAmount || r.avgAmount || 0).toLocaleString()}</td><td><div class="d-flex align-items-center"><div class="progress flex-grow-1 me-2" style="height:6px"><div class="progress-bar" style="width:${r.Percentage || r.percentage || 0}%"></div></div><small>${r.Percentage || r.percentage || 0}%</small></div></td></tr>`).join('');
                break;
        }

        document.getElementById('reportTableCard')?.classList.remove('d-none');
    }

    // ========================================
    // Filters
    // ========================================

    _getFilters() {
        const period = document.getElementById('filterPeriod')?.value || 'this-month';
        const dates = this._parsePeriod(period);
        const providerId = document.getElementById('filterProvider')?.value || '';
        const locationId = document.getElementById('filterLocation')?.value || '';
        return {
            StartDate: dates.start,
            EndDate: dates.end,
            ProviderId: providerId ? parseInt(providerId) : null,
            LocationId: locationId ? parseInt(locationId) : null,
            InsuranceId: null
        };
    }

    _parsePeriod(period) {
        const now = new Date();
        let start, end;
        switch (period) {
            case 'this-month':
                start = new Date(now.getFullYear(), now.getMonth(), 1);
                end = new Date(now.getFullYear(), now.getMonth() + 1, 0);
                break;
            case 'last-month':
                start = new Date(now.getFullYear(), now.getMonth() - 1, 1);
                end = new Date(now.getFullYear(), now.getMonth(), 0);
                break;
            case 'this-quarter':
                const qStart = Math.floor(now.getMonth() / 3) * 3;
                start = new Date(now.getFullYear(), qStart, 1);
                end = new Date(now.getFullYear(), qStart + 3, 0);
                break;
            case 'last-quarter':
                const lqStart = Math.floor(now.getMonth() / 3) * 3 - 3;
                start = new Date(now.getFullYear(), lqStart, 1);
                end = new Date(now.getFullYear(), lqStart + 3, 0);
                break;
            case 'ytd':
                start = new Date(now.getFullYear(), 0, 1);
                end = now;
                break;
            case 'custom':
                start = new Date(document.getElementById('filterStartDate')?.value || now);
                end = new Date(document.getElementById('filterEndDate')?.value || now);
                break;
            default:
                start = new Date(now.getFullYear(), now.getMonth(), 1);
                end = now;
        }
        return { start: start.toISOString(), end: end.toISOString() };
    }

    _onPeriodChange() {
        const period = document.getElementById('filterPeriod')?.value;
        const show = period === 'custom';
        document.getElementById('filterCustomDates')?.classList.toggle('d-none', !show);
        document.getElementById('filterCustomDatesEnd')?.classList.toggle('d-none', !show);
    }

    async _loadFilterData() {
        try {
            const apiGet = async (url) => {
                if (this.api) return await this.api.get(url);
                return await window.apiRequest(url, { method: 'GET' });
            };
            const [provData, locData] = await Promise.all([
                apiGet('/providers?activeOnly=true').catch(() => null),
                apiGet('/locations').catch(() => null)
            ]);
            if (provData) {
                const providers = provData.Items || provData.items || provData || [];
                const sel = document.getElementById('filterProvider');
                if (sel) {
                    providers.forEach(p => {
                        const name = `${p.LastName || p.lastName || ''}, ${p.FirstName || p.firstName || ''}`;
                        sel.innerHTML += `<option value="${p.ProviderId || p.providerId}">${name}</option>`;
                    });
                }
            }
            if (locData) {
                const locations = locData.Items || locData.items || locData || [];
                this.locations = locations;
                const sel = document.getElementById('filterLocation');
                if (sel) {
                    locations.forEach(l => {
                        sel.innerHTML += `<option value="${l.LocationId || l.locationId}">${l.Name || l.name}</option>`;
                    });
                }
            }
        } catch (err) {
            console.error('Failed to load filter data:', err);
        }
    }

    // ========================================
    // Export Methods for Analytical Reports
    // ========================================

    exportCurrentReport(format) {
        if (format === 'print' || format === 'pdf') {
            window.print();
        } else if (format === 'excel') {
            this._exportAnalyticalToCSV();
        }
    }

    _exportAnalyticalToCSV() {
        const table = document.getElementById('reportDataTable');
        if (!table) return;
        const csvRows = [];
        const headers = [];
        table.querySelectorAll('thead th').forEach(th => headers.push(th.textContent.trim()));
        csvRows.push(headers.join(','));
        table.querySelectorAll('tbody tr').forEach(tr => {
            const cells = [];
            tr.querySelectorAll('td').forEach(td => {
                let val = td.textContent.trim().replace(/"/g, '""');
                if (val.includes(',') || val.includes('"') || val.includes('\n')) val = `"${val}"`;
                cells.push(val);
            });
            csvRows.push(cells.join(','));
        });
        const csv = csvRows.join('\n');
        const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
        const link = document.createElement('a');
        link.href = URL.createObjectURL(blob);
        link.download = `${this.currentReportId || 'report'}-${new Date().toISOString().split('T')[0]}.csv`;
        link.click();
        URL.revokeObjectURL(link.href);
    }

    // ========================================
    // Monthly Patient Visit Grid (existing)
    // ========================================

    /**
     * Run the monthly patient visit report
     */
    async runMonthlyPatientVisitReport() {
        const month = parseInt(document.getElementById('reportMonthFilter')?.value);
        const year = parseInt(document.getElementById('reportYearFilter')?.value);
        const providerId = document.getElementById('reportTherapistFilter')?.value || null;

        if (!month || !year) {
            this._showToast('Error', 'Please select a month and year.', 'error');
            return;
        }

        // Show loading state
        document.getElementById('reportLoading')?.classList.remove('d-none');
        document.getElementById('reportEmptyState')?.classList.add('d-none');
        document.getElementById('visitGridWrapper').style.display = 'none';
        document.getElementById('reportFooter').style.display = 'none';

        try {
            const request = {
                Month: month,
                Year: year,
                ProviderId: providerId ? parseInt(providerId) : null
            };

            const data = await this._apiPost('/reports/monthly-visit-grid', request);
            this.currentReportData = data;

            // Hide loading
            document.getElementById('reportLoading')?.classList.add('d-none');

            if (!data || !data.Rows || data.Rows.length === 0) {
                document.getElementById('reportEmptyState')?.classList.remove('d-none');
                return;
            }

            // Render the grid
            this._renderMonthlyVisitGrid(data);

        } catch (error) {
            console.error('Error running report:', error);
            document.getElementById('reportLoading')?.classList.add('d-none');
            this._showToast('Error', 'Failed to load report data. Please try again.', 'error');
        }
    }

    /**
     * Export report to PDF (using print)
     */
    exportToPDF() {
        if (!this.currentReportData) {
            this._showToast('Error', 'Please run the report first.', 'error');
            return;
        }
        this.printReport();
    }

    /**
     * Export report to Excel (CSV)
     */
    exportToExcel() {
        if (!this.currentReportData) {
            this._showToast('Error', 'Please run the report first.', 'error');
            return;
        }

        const data = this.currentReportData;
        const daysInMonth = data.DaysInMonth;

        // Build CSV content
        let csv = '';

        // Header row
        let headers = ['Patient', 'Provider', 'Insurance', 'A.V.', 'PCP', 'Freq.', 'Cert. Pd.'];
        for (let day = 1; day <= daysInMonth; day++) {
            headers.push(day.toString());
        }
        headers.push('M', 'E', 'I', 'RV', 'PV');
        csv += headers.map(h => `"${h}"`).join(',') + '\n';

        // Data rows
        data.Rows.forEach(row => {
            let rowData = [
                row.PatientName || '',
                row.TherapistCode || '',
                row.Insurance || '',
                row.AuthorizedVisits ?? '',
                row.PCP || '',
                row.Frequency || '',
                row.CertPeriod || ''
            ];

            // Day cells
            for (let day = 1; day <= daysInMonth; day++) {
                const dayVisits = row.DayVisits?.[day] || [];
                const codes = dayVisits.map(v => v.Code || '').join('');
                rowData.push(codes);
            }

            // Summary
            rowData.push(row.MonthlyVisitCount || 0);
            rowData.push(row.EvaluationCount || 0);
            rowData.push(row.InterimEvalCount || 0);
            rowData.push(row.RemainingVisits ?? '');
            rowData.push(row.ProjectedVisits ?? '');

            csv += rowData.map(d => `"${String(d).replace(/"/g, '""')}"`).join(',') + '\n';
        });

        // Download
        const blob = new Blob([csv], { type: 'text/csv' });
        const url = window.URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `patient-visit-grid-${data.Month}-${data.Year}.csv`;
        a.click();
        window.URL.revokeObjectURL(url);
    }

    /**
     * Print report
     */
    printReport() {
        if (!this.currentReportData) {
            this._showToast('Error', 'Please run the report first.', 'error');
            return;
        }
        window.print();
    }

    /**
     * Filter report by provider
     */
    filterByTherapist(providerId) {
        const select = document.getElementById('reportTherapistFilter');
        if (select) {
            select.value = providerId;
            this.runMonthlyPatientVisitReport();
        }
    }

    /**
     * View patient from report
     */
    viewPatient(patientId) {
        this._emit('report:viewPatient', { patientId });
        // Fallback to global function
        if (typeof window.viewPatient === 'function') {
            window.viewPatient(patientId);
        }
    }

    /**
     * View provider from report
     */
    viewProvider(providerId) {
        this._emit('report:viewProvider', { providerId });
        // Fallback to global function
        if (typeof window.viewProvider === 'function') {
            window.viewProvider(providerId);
        }
    }

    /**
     * View appointment from report (view-only)
     */
    viewAppointment(appointmentId) {
        this._emit('report:viewAppointment', { appointmentId, viewOnly: true });
        // Fallback to global function
        if (typeof window.openAppointmentDetails === 'function') {
            window.openAppointmentDetails(appointmentId, { viewOnly: true, source: 'reports' });
        }
    }

    /**
     * Clean up module resources
     */
    destroy() {
        this._unbindEvents();
        if (this.chartInstance) { this.chartInstance.destroy(); this.chartInstance = null; }
        this.currentReportData = null;
        this.currentReportId = null;
        this.providers = [];
    }

    // ========================================
    // Private Methods - Rendering (Monthly Grid)
    // ========================================

    _renderMonthlyVisitGrid(data) {
        // Update header info
        document.getElementById('reportAgencyName').textContent = data.AgencyName || '-';
        document.getElementById('reportDiscipline').textContent = data.Discipline || 'All Disciplines';
        document.getElementById('reportMonthYear').textContent = `${data.Month}/${data.Year}`;

        const daysInMonth = data.DaysInMonth;
        const rows = data.Rows || [];

        // Build table header
        let headerHtml = '<tr>';
        headerHtml += '<th class="frozen frozen-patient">Patient</th>';
        headerHtml += '<th class="frozen frozen-therapist">Provider</th>';
        headerHtml += '<th>Ins.</th>';
        headerHtml += '<th>A.V.</th>';
        headerHtml += '<th>PCP</th>';
        headerHtml += '<th>Freq.</th>';
        headerHtml += '<th>Cert. Pd.</th>';

        for (let day = 1; day <= daysInMonth; day++) {
            headerHtml += `<th class="day-header">${day}</th>`;
        }

        headerHtml += '<th>M</th>';
        headerHtml += '<th>E</th>';
        headerHtml += '<th>I</th>';
        headerHtml += '<th>RV</th>';
        headerHtml += '<th>PV</th>';
        headerHtml += '</tr>';

        document.getElementById('visitGridHeader').innerHTML = headerHtml;

        // Build table body
        let bodyHtml = '';
        let totalVisits = 0;

        rows.forEach(row => {
            bodyHtml += '<tr>';

            // Patient name
            bodyHtml += `<td class="frozen frozen-patient patient-name-cell">
                <a href="#" class="patient-name-link" data-action="view-patient" data-patient-id="${row.PatientId}">
                    ${this._escape(row.PatientName || '')}
                </a>
            </td>`;

            // Provider
            bodyHtml += `<td class="frozen frozen-therapist">
                <a href="#" class="therapist-name-link" data-action="view-provider" data-provider-id="${row.ProviderId}"
                   title="${this._escape(row.TherapistName || '')}">
                    ${this._escape(row.TherapistCode || '')}
                </a>
            </td>`;

            // Insurance
            bodyHtml += `<td class="insurance-cell" title="${this._escape(row.Insurance || '')}">${this._escape(row.Insurance || '')}</td>`;

            // A.V., PCP, Frequency, Cert Period
            bodyHtml += `<td>${row.AuthorizedVisits ?? ''}</td>`;
            bodyHtml += `<td>${this._escape(row.PCP || '')}</td>`;
            bodyHtml += `<td>${this._escape(row.Frequency || '')}</td>`;
            bodyHtml += `<td>${this._escape(row.CertPeriod || '')}</td>`;

            // Day cells
            for (let day = 1; day <= daysInMonth; day++) {
                const dayVisits = row.DayVisits?.[day] || [];
                if (dayVisits.length > 0) {
                    const codes = dayVisits.map(v => {
                        const code = v.Code || this._getAppointmentTypeCode(v.Type);
                        const statusInfo = this._getAppointmentStatusIndicator(v.Status, v.StartTime);
                        const displayCode = statusInfo.indicator ? `${code}(${statusInfo.indicator})` : code;
                        const colorClass = statusInfo.colorClass || `visit-code-${code}`;
                        return `<span class="visit-code ${colorClass}" data-action="view-appointment"
                                data-appointment-id="${v.AppointmentId}" title="${statusInfo.tooltip || ''}">${displayCode}</span>`;
                    }).join('');
                    bodyHtml += `<td class="day-cell clickable">${codes}</td>`;
                } else {
                    bodyHtml += '<td class="day-cell"></td>';
                }
            }

            // Summary cells
            bodyHtml += `<td class="summary-cell">${row.MonthlyVisitCount || 0}</td>`;
            bodyHtml += `<td class="summary-cell">${row.EvaluationCount || 0}</td>`;
            bodyHtml += `<td class="summary-cell">${row.InterimEvalCount || 0}</td>`;
            bodyHtml += `<td class="summary-cell">${row.RemainingVisits ?? ''}</td>`;
            bodyHtml += `<td class="summary-cell">${row.ProjectedVisits ?? ''}</td>`;

            bodyHtml += '</tr>';
            totalVisits += row.MonthlyVisitCount || 0;
        });

        document.getElementById('visitGridBody').innerHTML = bodyHtml;

        // Update footer
        document.getElementById('reportTotalVisits').textContent = data.TotalVisits || totalVisits;
        document.getElementById('reportRowCount').textContent = rows.length;
        document.getElementById('reportPrintedDate').textContent = new Date().toLocaleString();
        document.getElementById('reportCompanyName').textContent = data.AgencyName || 'MEDOCS';

        // Show grid and footer
        document.getElementById('visitGridWrapper').style.display = 'block';
        document.getElementById('reportFooter').style.display = 'block';
    }

    _getAppointmentTypeCode(type) {
        const codes = ['E', 'V', 'I', 'D', 'C', 'T', 'G', 'W'];
        return codes[type] || 'V';
    }

    _getAppointmentStatusIndicator(status, startTime) {
        const statusNames = ['Scheduled', 'Confirmed', 'Checked In', 'In Progress', 'Completed', 'No Show', 'Cancelled', 'Rescheduled', 'Missed'];
        const statusInfo = { indicator: '', colorClass: '', tooltip: statusNames[status] || 'Unknown', statusName: statusNames[status] || 'Unknown' };

        switch (status) {
            case 5: // No Show
                statusInfo.indicator = 'NS';
                statusInfo.colorClass = 'visit-code-noshow';
                break;
            case 6: // Cancelled
                statusInfo.indicator = 'C';
                statusInfo.colorClass = 'visit-code-cancelled';
                break;
            case 7: // Rescheduled
                statusInfo.indicator = 'R';
                statusInfo.colorClass = 'visit-code-rescheduled';
                break;
            case 8: // Missed
                statusInfo.indicator = 'M';
                statusInfo.colorClass = 'visit-code-missed';
                break;
        }

        return statusInfo;
    }

    // ========================================
    // Private Methods - Initialization
    // ========================================

    _initYearFilter() {
        const yearSelect = document.getElementById('reportYearFilter');
        if (!yearSelect || yearSelect.options.length > 1) return;

        const currentYear = new Date().getFullYear();
        yearSelect.innerHTML = '';

        for (let year = currentYear; year >= currentYear - 5; year--) {
            const option = document.createElement('option');
            option.value = year;
            option.textContent = year;
            yearSelect.appendChild(option);
        }

        // Set current month
        const monthSelect = document.getElementById('reportMonthFilter');
        if (monthSelect) {
            monthSelect.value = new Date().getMonth() + 1;
        }
    }

    async _loadTherapistFilter() {
        try {
            const providers = await this._apiGet('/providers?activeOnly=true');
            this.providers = providers || [];

            const select = document.getElementById('reportTherapistFilter');
            if (!select) return;

            select.innerHTML = '<option value="">All Providers</option>';

            this.providers.forEach(provider => {
                const option = document.createElement('option');
                option.value = provider.ProviderId || provider.providerId;
                option.textContent = provider.FullName || provider.fullName ||
                    `${provider.FirstName || provider.firstName} ${provider.LastName || provider.lastName}`;
                select.appendChild(option);
            });
        } catch (error) {
            console.error('Error loading therapist filter:', error);
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

        switch (action) {
            case 'view-patient':
                e.preventDefault();
                this.viewPatient(parseInt(target.getAttribute('data-patient-id')));
                break;
            case 'view-provider':
                e.preventDefault();
                this.viewProvider(parseInt(target.getAttribute('data-provider-id')));
                break;
            case 'view-appointment':
                e.preventDefault();
                this.viewAppointment(parseInt(target.getAttribute('data-appointment-id')));
                break;
        }
    }

    // ========================================
    // Private Methods - API
    // ========================================

    async _apiGet(endpoint) {
        if (this.api) {
            return await this.api.get(endpoint);
        }
        return await window.apiRequest(endpoint, { showLoader: false });
    }

    async _apiPost(endpoint, data) {
        if (this.api) {
            return await this.api.post(endpoint, data);
        }
        return await window.apiRequest(endpoint, { method: 'POST', body: data });
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
        try {
            const date = window.parseServerDateTime ? window.parseServerDateTime(dateStr) : new Date(dateStr);
            if (!date || isNaN(date.getTime())) return '-';
            return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
        } catch { return '-'; }
    }

    _showReportLoading(show) {
        document.getElementById('reportDetailLoading')?.classList.toggle('d-none', !show);
        document.getElementById('reportDetailEmpty')?.classList.add('d-none');
        document.getElementById('reportKpiCards')?.classList.toggle('d-none', show);
        document.getElementById('reportChartCard')?.classList.toggle('d-none', show);
        document.getElementById('reportTableCard')?.classList.toggle('d-none', show);
    }

    _showReportEmpty(show) {
        document.getElementById('reportDetailLoading')?.classList.add('d-none');
        document.getElementById('reportDetailEmpty')?.classList.toggle('d-none', !show);
        document.getElementById('reportKpiCards')?.classList.toggle('d-none', show);
        document.getElementById('reportChartCard')?.classList.toggle('d-none', show);
        document.getElementById('reportTableCard')?.classList.toggle('d-none', show);
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

    _emit(event, data) {
        if (this.eventBus) {
            this.eventBus.emit(event, data);
        }
    }
}

// Export for module usage
if (typeof module !== 'undefined' && module.exports) {
    module.exports = ReportsModule;
}

// Global functions for analytical reports
function exitReports() { if (window.reportsModule) window.reportsModule.exitReportsMode(); }
function selectReportCategory(cat, e) { if (window.reportsModule) window.reportsModule.selectCategory(cat, e); }
function searchReports(term) { if (window.reportsModule) window.reportsModule.searchReports(term); }
function openReport(id) { if (window.reportsModule) window.reportsModule.openReport(id); }
function backToReportList() { if (window.reportsModule) window.reportsModule.backToReportList(); }
function runCurrentReport() { if (window.reportsModule) window.reportsModule.runCurrentReport(); }
function onPeriodChange() { if (window.reportsModule) window.reportsModule._onPeriodChange(); }
function exportCurrentReport(fmt) { if (window.reportsModule) window.reportsModule.exportCurrentReport(fmt); }

// Global functions for legacy onclick handlers (monthly grid)
function showMonthlyPatientVisitGrid() {
    if (window.reportsModule) {
        window.reportsModule.showMonthlyPatientVisitGrid();
    }
}

function backFromReport() {
    if (window.reportsModule) {
        window.reportsModule.backFromReport();
    }
}

function runMonthlyPatientVisitReport() {
    if (window.reportsModule) {
        window.reportsModule.runMonthlyPatientVisitReport();
    }
}

function exportReportToPDF() {
    if (window.reportsModule) {
        window.reportsModule.exportToPDF();
    }
}

function exportReportToExcel() {
    if (window.reportsModule) {
        window.reportsModule.exportToExcel();
    }
}

function printReport() {
    if (window.reportsModule) {
        window.reportsModule.printReport();
    }
}

function viewPatientFromReport(patientId) {
    if (window.reportsModule) {
        window.reportsModule.viewPatient(patientId);
    }
}

function viewProviderFromReport(providerId) {
    if (window.reportsModule) {
        window.reportsModule.viewProvider(providerId);
    }
}

function viewAppointmentFromReport(appointmentId) {
    if (window.reportsModule) {
        window.reportsModule.viewAppointment(appointmentId);
    }
}

// Auto-initialize when DOM is ready
document.addEventListener('DOMContentLoaded', function() {
    const container = document.getElementById('reportsPage');
    if (!container) return;

    const initWhenReady = () => {
        const isAuthenticated = (typeof currentUser !== 'undefined' && currentUser) ||
                               (window.App && window.App.isAuthenticated && window.App.isAuthenticated());

        if (!isAuthenticated) {
            setTimeout(initWhenReady, 200);
            return;
        }

        if (window.reportsModule) {
            window.reportsModule.load();
            return;
        }

        window.reportsModule = new ReportsModule({
            api: window.apiService || (window.App && window.App.api),
            eventBus: window.eventBus || (window.App && window.App.events)
        });

        window.reportsModule.init();
        window.reportsModule.load();
    };

    initWhenReady();
});
