/**
 * DataTable - Reusable data table component with pagination and sorting
 *
 * Usage:
 *   const table = new DataTable({
 *     containerId: 'patientsTable',
 *     columns: [
 *       { key: 'FullName', label: 'Name', sortable: true },
 *       { key: 'DateOfBirth', label: 'DOB', format: 'date' },
 *       { key: 'Status', label: 'Status', render: (val) => getStatusBadge(val) }
 *     ],
 *     onRowClick: (row) => viewPatient(row.PatientId),
 *     onPageChange: (page) => loadPatients(page)
 *   });
 *   table.setData(patients, totalCount);
 */
class DataTable {
    /**
     * Create a data table instance
     * @param {Object} options - Configuration options
     */
    constructor(options) {
        this.options = {
            containerId: null,
            columns: [],
            data: [],
            pageSize: 25,
            currentPage: 1,
            totalCount: 0,
            sortColumn: null,
            sortDirection: 'asc',
            emptyMessage: 'No data available',
            loadingMessage: 'Loading...',
            tableClass: 'table table-hover',
            rowClass: '',
            onRowClick: null,
            onPageChange: null,
            onSort: null,
            showPagination: true,
            showHeader: true,
            stickyHeader: false,
            actions: [],
            ...options
        };

        this.container = null;
        this.tableBody = null;
        this.isLoading = false;

        this.init();
    }

    /**
     * Initialize the table
     */
    init() {
        this.container = document.getElementById(this.options.containerId);
        if (!this.container) {
            console.error('DataTable: Container not found:', this.options.containerId);
            return;
        }

        this.render();
    }

    /**
     * Set table data
     * @param {Array} data - Row data
     * @param {number} [totalCount] - Total row count for pagination
     */
    setData(data, totalCount = null) {
        this.options.data = data || [];
        if (totalCount !== null) {
            this.options.totalCount = totalCount;
        } else {
            this.options.totalCount = data?.length || 0;
        }
        this.isLoading = false;
        this.renderBody();
        this.renderPagination();
    }

    /**
     * Set current page
     * @param {number} page - Page number (1-indexed)
     */
    setPage(page) {
        this.options.currentPage = page;
        if (this.options.onPageChange) {
            this.options.onPageChange(page);
        }
    }

    /**
     * Show loading state
     */
    showLoading() {
        this.isLoading = true;
        if (this.tableBody) {
            const colspan = this.options.columns.length + (this.options.actions.length > 0 ? 1 : 0);
            this.tableBody.innerHTML = `
                <tr>
                    <td colspan="${colspan}" class="text-center py-4">
                        <span class="spinner-border spinner-border-sm me-2"></span>
                        ${this.options.loadingMessage}
                    </td>
                </tr>
            `;
        }
    }

    /**
     * Render the complete table
     */
    render() {
        const hasActions = this.options.actions.length > 0;

        let html = `
            <div class="table-responsive">
                <table class="${this.options.tableClass}">
        `;

        // Header
        if (this.options.showHeader) {
            html += '<thead';
            if (this.options.stickyHeader) {
                html += ' class="sticky-top bg-white"';
            }
            html += '><tr>';

            this.options.columns.forEach(col => {
                const sortable = col.sortable ? 'sortable' : '';
                const sortClass = this.options.sortColumn === col.key
                    ? `sorted sorted-${this.options.sortDirection}`
                    : '';

                html += `
                    <th class="${sortable} ${sortClass}" data-column="${col.key}">
                        ${col.label}
                        ${col.sortable ? '<i class="bi bi-chevron-expand sort-icon"></i>' : ''}
                    </th>
                `;
            });

            if (hasActions) {
                html += '<th class="text-end">Actions</th>';
            }

            html += '</tr></thead>';
        }

        // Body
        html += '<tbody id="' + this.options.containerId + '_body"></tbody>';

        html += '</table></div>';

        // Pagination
        if (this.options.showPagination) {
            html += `<div id="${this.options.containerId}_pagination" class="d-flex justify-content-between align-items-center mt-3"></div>`;
        }

        this.container.innerHTML = html;

        this.tableBody = document.getElementById(this.options.containerId + '_body');

        // Bind header click for sorting
        this.container.querySelectorAll('th.sortable').forEach(th => {
            th.addEventListener('click', () => {
                this.handleSort(th.dataset.column);
            });
        });

        // Render body content
        this.renderBody();
        this.renderPagination();
    }

    /**
     * Render table body
     */
    renderBody() {
        if (!this.tableBody) return;

        if (this.isLoading) {
            this.showLoading();
            return;
        }

        if (this.options.data.length === 0) {
            const colspan = this.options.columns.length + (this.options.actions.length > 0 ? 1 : 0);
            this.tableBody.innerHTML = `
                <tr>
                    <td colspan="${colspan}" class="text-center text-muted py-4">
                        ${this.options.emptyMessage}
                    </td>
                </tr>
            `;
            return;
        }

        const html = this.options.data.map((row, rowIndex) => this.renderRow(row, rowIndex)).join('');
        this.tableBody.innerHTML = html;

        // Bind row click
        if (this.options.onRowClick) {
            this.tableBody.querySelectorAll('tr').forEach((tr, index) => {
                tr.style.cursor = 'pointer';
                tr.addEventListener('click', (e) => {
                    // Don't trigger if clicking action buttons
                    if (e.target.closest('.action-btn')) return;
                    this.options.onRowClick(this.options.data[index], index);
                });
            });
        }
    }

    /**
     * Render a single row
     * @param {Object} row - Row data
     * @param {number} rowIndex - Row index
     * @returns {string} HTML
     */
    renderRow(row, rowIndex) {
        let html = `<tr class="${this.options.rowClass}">`;

        // Data columns
        this.options.columns.forEach(col => {
            let value = this.getNestedValue(row, col.key);

            // Apply format
            if (col.format) {
                value = this.formatValue(value, col.format);
            }

            // Apply custom render
            if (col.render) {
                value = col.render(value, row, rowIndex);
            } else {
                value = StringUtils.escape(value);
            }

            const className = col.className || '';
            html += `<td class="${className}">${value}</td>`;
        });

        // Actions column
        if (this.options.actions.length > 0) {
            html += '<td class="text-end">';
            this.options.actions.forEach(action => {
                if (action.visible && !action.visible(row)) return;

                const btnClass = action.class || 'btn-sm btn-outline-primary';
                const icon = action.icon ? `<i class="bi bi-${action.icon}"></i>` : '';
                const label = action.label || '';

                html += `
                    <button type="button"
                            class="btn ${btnClass} action-btn me-1"
                            onclick="${action.handler}(${JSON.stringify(row).replace(/"/g, '&quot;')})"
                            title="${action.title || action.label || ''}">
                        ${icon}${label ? ' ' + label : ''}
                    </button>
                `;
            });
            html += '</td>';
        }

        html += '</tr>';
        return html;
    }

    /**
     * Get nested object value by key path
     * @param {Object} obj - Object
     * @param {string} path - Dot-separated path
     * @returns {*}
     */
    getNestedValue(obj, path) {
        return path.split('.').reduce((o, k) => (o || {})[k], obj);
    }

    /**
     * Format a value based on type
     * @param {*} value - Value to format
     * @param {string} format - Format type
     * @returns {string}
     */
    formatValue(value, format) {
        switch (format) {
            case 'date':
                return DateUtils.format(value);
            case 'time':
                return DateUtils.formatTime(value);
            case 'datetime':
                return DateUtils.formatDateTime(value);
            case 'currency':
                return FormatUtils.currency(value);
            case 'phone':
                return FormatUtils.phone(value);
            case 'yesno':
                return FormatUtils.yesNo(value);
            default:
                return value;
        }
    }

    /**
     * Handle column sort
     * @param {string} column - Column key
     */
    handleSort(column) {
        if (this.options.sortColumn === column) {
            this.options.sortDirection = this.options.sortDirection === 'asc' ? 'desc' : 'asc';
        } else {
            this.options.sortColumn = column;
            this.options.sortDirection = 'asc';
        }

        if (this.options.onSort) {
            this.options.onSort(column, this.options.sortDirection);
        } else {
            // Client-side sort
            this.options.data.sort((a, b) => {
                const aVal = this.getNestedValue(a, column);
                const bVal = this.getNestedValue(b, column);
                const cmp = aVal < bVal ? -1 : aVal > bVal ? 1 : 0;
                return this.options.sortDirection === 'asc' ? cmp : -cmp;
            });
            this.render();
        }
    }

    /**
     * Render pagination
     */
    renderPagination() {
        if (!this.options.showPagination) return;

        const paginationEl = document.getElementById(this.options.containerId + '_pagination');
        if (!paginationEl) return;

        const totalPages = Math.ceil(this.options.totalCount / this.options.pageSize);
        const currentPage = this.options.currentPage;

        // Info
        const start = (currentPage - 1) * this.options.pageSize + 1;
        const end = Math.min(currentPage * this.options.pageSize, this.options.totalCount);

        let html = `
            <div class="text-muted small">
                Showing ${start}-${end} of ${this.options.totalCount}
            </div>
        `;

        if (totalPages > 1) {
            html += '<nav><ul class="pagination pagination-sm mb-0">';

            // Previous
            html += `
                <li class="page-item ${currentPage === 1 ? 'disabled' : ''}">
                    <a class="page-link" href="#" data-page="${currentPage - 1}">
                        <i class="bi bi-chevron-left"></i>
                    </a>
                </li>
            `;

            // Page numbers
            const maxPages = 5;
            let startPage = Math.max(1, currentPage - Math.floor(maxPages / 2));
            let endPage = Math.min(totalPages, startPage + maxPages - 1);

            if (endPage - startPage < maxPages - 1) {
                startPage = Math.max(1, endPage - maxPages + 1);
            }

            if (startPage > 1) {
                html += `<li class="page-item"><a class="page-link" href="#" data-page="1">1</a></li>`;
                if (startPage > 2) {
                    html += `<li class="page-item disabled"><span class="page-link">...</span></li>`;
                }
            }

            for (let i = startPage; i <= endPage; i++) {
                html += `
                    <li class="page-item ${i === currentPage ? 'active' : ''}">
                        <a class="page-link" href="#" data-page="${i}">${i}</a>
                    </li>
                `;
            }

            if (endPage < totalPages) {
                if (endPage < totalPages - 1) {
                    html += `<li class="page-item disabled"><span class="page-link">...</span></li>`;
                }
                html += `<li class="page-item"><a class="page-link" href="#" data-page="${totalPages}">${totalPages}</a></li>`;
            }

            // Next
            html += `
                <li class="page-item ${currentPage === totalPages ? 'disabled' : ''}">
                    <a class="page-link" href="#" data-page="${currentPage + 1}">
                        <i class="bi bi-chevron-right"></i>
                    </a>
                </li>
            `;

            html += '</ul></nav>';
        }

        paginationEl.innerHTML = html;

        // Bind pagination clicks
        paginationEl.querySelectorAll('[data-page]').forEach(link => {
            link.addEventListener('click', (e) => {
                e.preventDefault();
                const page = parseInt(link.dataset.page);
                if (page >= 1 && page <= totalPages && page !== currentPage) {
                    this.setPage(page);
                }
            });
        });
    }

    /**
     * Refresh the table
     */
    refresh() {
        this.render();
    }

    /**
     * Destroy the table
     */
    destroy() {
        if (this.container) {
            this.container.innerHTML = '';
        }
        this.container = null;
        this.tableBody = null;
    }
}

// Export for global access
window.DataTable = DataTable;
