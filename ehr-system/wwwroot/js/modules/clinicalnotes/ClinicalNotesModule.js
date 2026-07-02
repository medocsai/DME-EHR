/**
 * ClinicalNotesModule - Clinical notes management
 *
 * Handles clinical note listing, filtering, viewing, editing,
 * and signing functionality.
 *
 * @example
 *   const notes = App.modules.get('clinicalNotes');
 *   await notes.init();
 *   notes.load();
 */
class ClinicalNotesModule {
    /**
     * @param {Object} options - Module options
     * @param {Object} options.api - API service instance
     * @param {Object} options.eventBus - Event bus for cross-module communication
     */
    constructor(options = {}) {
        this.api = options.api || null;
        this.eventBus = options.eventBus || null;

        // State
        this.notes = [];
        this.currentNote = null;
        this.currentPage = 1;
        this.pageSize = 20;
        this.totalCount = 0;
        this.totalPages = 0;
        this.filters = {
            search: '',
            startDate: '',
            endDate: '',
            providerId: null,
            status: ''
        };
        this.isInitialized = false;
        this.filterDebounceTimer = null;

        // DOM references
        this.tableBody = null;
        this.paginationControls = null;

        // Note statuses
        this.statuses = {
            0: { name: 'Draft', class: 'bg-warning text-dark' },
            1: { name: 'Pending Signature', class: 'bg-info' },
            2: { name: 'Signed', class: 'bg-success' },
            3: { name: 'Amended', class: 'bg-primary' },
            4: { name: 'Final', class: 'bg-success' }
        };

        // Bind methods
        this._handleTableClick = this._handleTableClick.bind(this);
        this._handleFilterChange = this._handleFilterChange.bind(this);
    }

    /**
     * Initialize the module
     * @returns {Promise<void>}
     */
    async init() {
        this.tableBody = document.querySelector('#clinicalNotesTable tbody');
        this.paginationControls = document.getElementById('notesPaginationControls');

        if (!this.tableBody) {
            console.warn('[ClinicalNotesModule] Table container not found');
            return;
        }

        this._bindEvents();
        this._initFilters();

        this.isInitialized = true;
        this._emit('clinicalNotes:initialized');
    }

    /**
     * Bind event handlers
     * @private
     */
    _bindEvents() {
        if (this.tableBody) {
            this.tableBody.addEventListener('click', this._handleTableClick);
        }

        // Pagination controls
        if (this.paginationControls) {
            this.paginationControls.addEventListener('click', this._handlePaginationClick.bind(this));
        }

        // Filter inputs
        const filterInputs = ['noteFilterSearch', 'noteFilterStartDate', 'noteFilterEndDate',
                            'noteFilterProvider', 'noteFilterStatus'];

        filterInputs.forEach(id => {
            const el = document.getElementById(id);
            if (el) {
                if (id === 'noteFilterSearch') {
                    el.addEventListener('input', this._debounce(this._handleFilterChange, 300));
                } else {
                    el.addEventListener('change', this._handleFilterChange);
                }
            }
        });
    }

    /**
     * Handle pagination click events
     * @private
     * @param {Event} e - Click event
     */
    _handlePaginationClick(e) {
        e.preventDefault();
        const target = e.target.closest('[data-page]');
        if (!target) return;

        const page = parseInt(target.dataset.page);
        if (page && page !== this.currentPage && page >= 1 && page <= this.totalPages) {
            this.load(page);
        }
    }

    /**
     * Initialize filter controls
     * @private
     */
    async _initFilters() {
        const currentUser = this._getCurrentUser();

        // Load providers dropdown for admins
        if (currentUser?.Role !== 2) {
            await this._loadProviderFilter();
        }
    }

    /**
     * Handle table click events
     * @private
     * @param {Event} e - Click event
     */
    _handleTableClick(e) {
        const target = e.target.closest('[data-action]');
        if (!target) return;

        const action = target.dataset.action;
        const noteId = target.dataset.noteId;
        const appointmentId = target.dataset.appointmentId;

        switch (action) {
            case 'view':
                this.view(parseInt(noteId), appointmentId ? parseInt(appointmentId) : null);
                break;
            case 'edit':
                this.edit(parseInt(noteId));
                break;
            case 'sign':
                this.sign(parseInt(noteId));
                break;
        }
    }

    /**
     * Handle filter change
     * @private
     */
    _handleFilterChange() {
        // Update filter state
        this.filters.search = document.getElementById('noteFilterSearch')?.value?.trim() || '';
        this.filters.startDate = document.getElementById('noteFilterStartDate')?.value || '';
        this.filters.endDate = document.getElementById('noteFilterEndDate')?.value || '';
        this.filters.providerId = document.getElementById('noteFilterProvider')?.value || null;
        this.filters.status = document.getElementById('noteFilterStatus')?.value || '';

        this.load(1);
    }

    /**
     * Load clinical notes with pagination
     * @param {number} page - Page number
     * @returns {Promise<void>}
     */
    async load(page = 1) {
        try {
            this.currentPage = page;

            const params = this._buildQueryParams();
            const url = `/clinical-notes/paged?${params}`;

            const result = await this._apiGet(url);

            this.notes = result?.Items || [];
            this.totalCount = result?.TotalCount || 0;
            this.totalPages = result?.TotalPages || 0;

            this._render();
            this._updatePagination(result);

            this._emit('clinicalNotes:loaded', {
                notes: this.notes,
                page: this.currentPage,
                totalCount: this.totalCount
            });
        } catch (error) {
            console.error('[ClinicalNotesModule] Load error:', error);
            this._renderError('Failed to load clinical notes');
            throw error;
        }
    }

    /**
     * View clinical note - delegates to GlobalBridge viewClinicalNote for consistent UI
     * Uses the tabbed view with Edit/Sign buttons when note has an appointment
     * @param {number} noteId - Note ID
     * @param {number} appointmentId - Optional appointment ID (not used, viewClinicalNote fetches it)
     */
    async view(noteId, appointmentId = null) {
        try {
            // Use the global viewClinicalNote function which provides the full tabbed view
            // with Edit/Sign/Delete, plus Amendment/Addendum and version history.
            // The legacy viewClinicalNoteModal fallback was removed in migration_016.
            if (typeof window.viewClinicalNote === 'function') {
                await window.viewClinicalNote(noteId);
            } else {
                console.warn('[ClinicalNotesModule] window.viewClinicalNote not loaded.');
                this._showError('Clinical note viewer unavailable.');
            }
            this._emit('clinicalNotes:viewed', { noteId });
        } catch (error) {
            console.error('[ClinicalNotesModule] View error:', error);
            this._showError('Failed to load clinical note');
        }
    }

    /**
     * Edit clinical note - delegates to GlobalBridge editClinicalNote
     * Opens the note editor modal for editing
     * @param {number} noteId - Note ID
     */
    async edit(noteId) {
        try {
            // Use the global editClinicalNote function which opens the editor modal
            if (typeof window.editClinicalNote === 'function') {
                window.editClinicalNote(noteId);
            } else {
                // Fallback: just emit event for legacy handling
                const note = await this._apiGet(`/clinical-notes/${noteId}`);
                if (!note) return;
                this.currentNote = note;
                this._emit('clinicalNotes:editing', { note });
            }
        } catch (error) {
            console.error('[ClinicalNotesModule] Edit error:', error);
            this._showError('Failed to load clinical note');
        }
    }

    /**
     * Sign clinical note
     * Signs the note and updates linked encounter
     * @param {number} noteId - Note ID
     */
    async sign(noteId) {
        try {
            // Use the core sign function which handles IE/Re-eval validation and care episode creation
            if (typeof window.signClinicalNoteCore === 'function') {
                const result = await window.signClinicalNoteCore(noteId);
                if (result.success) {
                    this._emit('clinicalNotes:signed', { noteId });
                    await this.load(this.currentPage);
                }
            } else {
                // Fallback to simple sign if core function not available
                const confirmed = await this._confirm({
                    title: 'Sign Clinical Note',
                    message: 'Are you sure you want to sign this clinical note? This action cannot be undone.',
                    confirmText: 'Sign Note',
                    confirmClass: 'btn-primary'
                });

                if (!confirmed) return;

                await this._apiPost(`/clinical-notes/${noteId}/sign`);
                this._showSuccess('Clinical note signed successfully');
                this._emit('clinicalNotes:signed', { noteId });
                await this.load(this.currentPage);
            }
        } catch (error) {
            console.error('[ClinicalNotesModule] Sign error:', error);
            this._showError('Failed to sign clinical note');
        }
    }

    /**
     * Create new clinical note
     * @param {Object} appointment - Appointment data
     */
    async create(appointment) {
        this._emit('clinicalNotes:creating', { appointment });
        // Note: Actual creation is handled by the note editor component
    }

    /**
     * Clear all filters
     */
    clearFilters() {
        this.filters = {
            search: '',
            startDate: '',
            endDate: '',
            providerId: null,
            status: ''
        };

        // Reset UI
        const filterIds = ['noteFilterSearch', 'noteFilterStartDate', 'noteFilterEndDate',
                         'noteFilterProvider', 'noteFilterStatus'];

        filterIds.forEach(id => {
            const el = document.getElementById(id);
            if (el) el.value = '';
        });

        this.load(1);
    }

    /**
     * Refresh notes
     * @returns {Promise<void>}
     */
    async refresh() {
        await this.load(this.currentPage);
    }

    /**
     * Build query parameters
     * @private
     * @returns {string} Query string
     */
    _buildQueryParams() {
        const params = new URLSearchParams();
        params.append('page', this.currentPage);
        params.append('pageSize', this.pageSize);

        const currentUser = this._getCurrentUser();
        if (currentUser?.Role === 2 && currentUser?.ProviderId) {
            params.append('providerId', currentUser.ProviderId);
        } else if (this.filters.providerId) {
            params.append('providerId', this.filters.providerId);
        }

        if (this.filters.search) params.append('search', this.filters.search);
        if (this.filters.startDate) params.append('startDate', this.filters.startDate);
        if (this.filters.endDate) params.append('endDate', this.filters.endDate);
        if (this.filters.status) params.append('status', this.filters.status);

        return params.toString();
    }

    /**
     * Render notes table
     * @private
     */
    _render() {
        if (!this.tableBody) return;

        if (!this.notes.length) {
            this.tableBody.innerHTML = `
                <tr>
                    <td colspan="7" class="text-center text-muted py-4">
                        No clinical notes found
                    </td>
                </tr>
            `;
            return;
        }

        const currentUser = this._getCurrentUser();
        const isAdmin = currentUser?.Role === 1;

        this.tableBody.innerHTML = this.notes.map(note => {
            // Admins can only view signed notes (status 2 or 4)
            const canView = !isAdmin || note.Status === 2 || note.Status === 4;

            return `
                <tr>
                    <td>${this._formatDate(note.ServiceDate)}</td>
                    <td>
                        <a href="#" onclick="App.modules.get('patients').view(${note.PatientId}); return false;">
                            ${this._escape(note.PatientName)}
                        </a>
                        <br><small class="text-muted">${this._escape(note.PatientMRN)}</small>
                    </td>
                    <td>${this._escape(note.ProviderName)}</td>
                    <td>${note.TemplateName || note.TypeName || '-'}</td>
                    <td>${this._getStatusBadge(note.Status)}</td>
                    <td>${note.SignedAt ? this._formatDateTime(note.SignedAt) : '-'}</td>
                    <td>
                        ${canView ? `
                            <button class="btn btn-primary"
                                    data-action="view"
                                    data-note-id="${note.ClinicalNoteId}"
                                    data-appointment-id="${note.AppointmentId || ''}">
                                View
                            </button>
                        ` : ''}
                    </td>
                </tr>
            `;
        }).join('');
    }

    /**
     * Render error state
     * @private
     * @param {string} message - Error message
     */
    _renderError(message) {
        if (!this.tableBody) return;

        this.tableBody.innerHTML = `
            <tr>
                <td colspan="7" class="text-center text-danger py-4">
                    <i class="bi bi-exclamation-triangle me-2"></i>
                    ${this._escape(message)}
                    <button class="btn btn-sm btn-outline-danger ms-2" onclick="App.modules.get('clinicalNotes').load()">
                        Retry
                    </button>
                </td>
            </tr>
        `;
    }

    /**
     * Render view modal content
     * @private
     * @param {Object} note - Note data
     */
    _renderViewModal(note) {
        // Populate the view modal elements
        const patientEl = document.getElementById('viewNotePatient');
        const providerEl = document.getElementById('viewNoteProvider');
        const dateEl = document.getElementById('viewNoteDate');
        const statusEl = document.getElementById('viewNoteStatus');
        const contentEl = document.getElementById('viewNoteContent');
        const titleEl = document.getElementById('viewNoteTitle');

        if (patientEl) {
            patientEl.innerHTML = `<strong>${this._escape(note.PatientName)}</strong><br><small class="text-muted">${this._escape(note.PatientMRN)}</small>`;
        }
        if (providerEl) {
            providerEl.textContent = note.ProviderName || '-';
        }
        if (dateEl) {
            dateEl.textContent = this._formatDate(note.ServiceDate);
        }
        if (statusEl) {
            statusEl.innerHTML = this._getStatusBadge(note.Status);
        }
        if (titleEl) {
            titleEl.textContent = note.TemplateName || 'Clinical Note';
        }
        if (contentEl) {
            let content = note.HtmlContent || note.Content || '<p class="text-muted">No content available</p>';
            if (note.SignedAt) {
                content += `
                    <hr>
                    <div class="note-signature text-muted small">
                        <i class="bi bi-pen me-1"></i>
                        Signed by ${this._escape(note.SignedByName || 'Provider')} on ${this._formatDateTime(note.SignedAt)}
                    </div>
                `;
            }
            contentEl.innerHTML = content;
        }
    }

    /**
     * Update pagination controls
     * @private
     * @param {Object} result - API result
     */
    _updatePagination(result) {
        const pageInfo = document.getElementById('notesPageInfo');

        if (!result) return;

        const { TotalCount, TotalPages, Page, PageSize } = result;
        const start = TotalCount > 0 ? (Page - 1) * PageSize + 1 : 0;
        const end = Math.min(Page * PageSize, TotalCount);

        if (pageInfo) {
            pageInfo.textContent = TotalCount > 0
                ? `Showing ${start}-${end} of ${TotalCount} notes`
                : 'No notes found';
        }

        if (this.paginationControls) {
            this.paginationControls.innerHTML = this._buildPaginationHtml(Page, TotalPages);
        }
    }

    /**
     * Build pagination HTML
     * @private
     * @param {number} currentPage - Current page
     * @param {number} totalPages - Total pages
     * @returns {string} HTML
     */
    _buildPaginationHtml(currentPage, totalPages) {
        if (totalPages <= 1) return '';

        let html = '';

        // Previous button
        html += `<li class="page-item ${currentPage <= 1 ? 'disabled' : ''}">
            <a class="page-link" href="#" data-page="${currentPage - 1}">
                <i class="bi bi-chevron-left"></i>
            </a>
        </li>`;

        // Page numbers
        const maxVisible = 5;
        let startPage = Math.max(1, currentPage - Math.floor(maxVisible / 2));
        let endPage = Math.min(totalPages, startPage + maxVisible - 1);
        startPage = Math.max(1, endPage - maxVisible + 1);

        if (startPage > 1) {
            html += `<li class="page-item"><a class="page-link" href="#" data-page="1">1</a></li>`;
            if (startPage > 2) {
                html += `<li class="page-item disabled"><span class="page-link">...</span></li>`;
            }
        }

        for (let i = startPage; i <= endPage; i++) {
            html += `<li class="page-item ${i === currentPage ? 'active' : ''}">
                <a class="page-link" href="#" data-page="${i}">${i}</a>
            </li>`;
        }

        if (endPage < totalPages) {
            if (endPage < totalPages - 1) {
                html += `<li class="page-item disabled"><span class="page-link">...</span></li>`;
            }
            html += `<li class="page-item"><a class="page-link" href="#" data-page="${totalPages}">${totalPages}</a></li>`;
        }

        // Next button
        html += `<li class="page-item ${currentPage >= totalPages ? 'disabled' : ''}">
            <a class="page-link" href="#" data-page="${currentPage + 1}">
                <i class="bi bi-chevron-right"></i>
            </a>
        </li>`;

        return html;
    }

    /**
     * Load provider filter dropdown
     * @private
     */
    async _loadProviderFilter() {
        try {
            const providers = await this._apiGet('/providers/dropdown');
            const select = document.getElementById('noteFilterProvider');
            if (select && providers) {
                select.innerHTML = '<option value="">All Providers</option>' +
                    providers.map(p =>
                        `<option value="${p.ProviderId}">${this._escape(p.FirstName)} ${this._escape(p.LastName)}</option>`
                    ).join('');
            }
        } catch (error) {
            console.error('[ClinicalNotesModule] Load providers error:', error);
        }
    }

    /**
     * Get status badge HTML
     * @private
     * @param {number} status - Status code
     * @returns {string} HTML
     */
    _getStatusBadge(status) {
        const s = this.statuses[status] || { name: 'Unknown', class: 'bg-secondary' };
        return `<span class="badge ${s.class}">${s.name}</span>`;
    }

    // === API Methods ===

    async _apiGet(url) {
        if (this.api) {
            return this.api.get(url);
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

    async _apiPost(url, data = {}) {
        if (this.api) {
            return this.api.post(url, data);
        }
        const response = await fetch(`/api${url}`, {
            method: 'POST',
            headers: this._getHeaders(),
            body: JSON.stringify(data)
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

    _escape(str) {
        if (str === null || str === undefined) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    }

    _formatDate(dateStr) {
        if (!dateStr) return '-';
        try {
            // Handle date-only strings (YYYY-MM-DD) to avoid timezone shifting
            if (typeof dateStr === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(dateStr)) {
                const [year, month, day] = dateStr.split('-').map(Number);
                return new Date(year, month - 1, day).toLocaleDateString();
            }
            return new Date(dateStr).toLocaleDateString();
        } catch {
            return dateStr;
        }
    }

    _formatDateTime(dateStr) {
        if (!dateStr) return '-';
        try {
            // Use parseServerDateTime to correctly interpret server UTC times
            const date = window.parseServerDateTime ? window.parseServerDateTime(dateStr) : new Date(dateStr);
            if (!date || isNaN(date.getTime())) return '-';

            // Get current location timezone for proper display
            const locationTz = window.getCurrentLocationTimezone ? window.getCurrentLocationTimezone() : null;
            const options = {
                month: 'numeric',
                day: 'numeric',
                year: 'numeric',
                hour: 'numeric',
                minute: '2-digit',
                hour12: true
            };
            if (locationTz?.timeZoneId) {
                options.timeZone = locationTz.timeZoneId;
            }

            const formatted = date.toLocaleString('en-US', options);
            const tzAbbr = locationTz?.timeZoneAbbreviation || '';
            return tzAbbr ? `${formatted} ${tzAbbr}` : formatted;
        } catch {
            return dateStr;
        }
    }

    _debounce(fn, delay) {
        return (...args) => {
            clearTimeout(this.filterDebounceTimer);
            this.filterDebounceTimer = setTimeout(() => fn.apply(this, args), delay);
        };
    }

    _showModal(id) {
        const el = document.getElementById(id);
        if (el && window.bootstrap) {
            new bootstrap.Modal(el).show();
        }
    }

    _hideModal(id) {
        const el = document.getElementById(id);
        if (el && window.bootstrap) {
            bootstrap.Modal.getInstance(el)?.hide();
        }
    }

    async _confirm(options) {
        if (window.ConfirmDialog) {
            return ConfirmDialog.show(options);
        }
        return confirm(options.message);
    }

    _showSuccess(message) {
        if (window.Toast) {
            Toast.success('Success', message);
        }
    }

    _showError(message) {
        if (window.Toast) {
            Toast.error('Error', message);
        }
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
        if (this.tableBody) {
            this.tableBody.removeEventListener('click', this._handleTableClick);
        }
        clearTimeout(this.filterDebounceTimer);

        this.notes = [];
        this.currentNote = null;
        this.tableBody = null;
        this.paginationControls = null;
        this.isInitialized = false;
    }
}

// Export for module usage
window.ClinicalNotesModule = ClinicalNotesModule;

// Auto-initialize when DOM is ready
document.addEventListener('DOMContentLoaded', function() {
    const container = document.getElementById('notesPage');
    if (!container) return;

    const initWhenReady = () => {
        const isAuthenticated = (typeof currentUser !== 'undefined' && currentUser) ||
                               (window.App && window.App.isAuthenticated && window.App.isAuthenticated());

        if (!isAuthenticated) {
            setTimeout(initWhenReady, 200);
            return;
        }

        if (window.clinicalNotesModule) {
            window.clinicalNotesModule.load();
            return;
        }

        window.clinicalNotesModule = new ClinicalNotesModule({
            api: window.apiService || (window.App && window.App.api),
            eventBus: window.eventBus || (window.App && window.App.events)
        });

        window.clinicalNotesModule.init();
        window.clinicalNotesModule.load();
    };

    initWhenReady();
});
