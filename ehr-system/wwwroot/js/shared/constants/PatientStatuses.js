/**
 * PatientStatuses - Patient status constants and utilities
 *
 * Usage:
 *   PatientStatuses.ACTIVE            // 0
 *   PatientStatuses.getName(0)        // "Active"
 *   PatientStatuses.getBadgeHtml(0)   // Full badge HTML
 */
const PatientStatuses = {
    // Status enum values
    ACTIVE: 0,
    INACTIVE: 1,
    DISCHARGED: 2,
    DECEASED: 3,

    // Status config
    _config: {
        0: { name: 'Active', cssClass: 'active', colorClass: 'bg-success' },
        1: { name: 'Inactive', cssClass: 'inactive', colorClass: 'bg-secondary' },
        2: { name: 'Discharged', cssClass: 'discharged', colorClass: 'bg-info' },
        3: { name: 'Deceased', cssClass: 'archived', colorClass: 'bg-dark' }
    },

    /**
     * Get display name for a status
     * @param {number} status - Status enum value
     * @returns {string} Display name
     */
    getName(status) {
        return this._config[status]?.name || 'Unknown';
    },

    /**
     * Get CSS class for status badge
     * @param {number} status - Status enum value
     * @returns {string} CSS class
     */
    getBadgeClass(status) {
        return this._config[status]?.cssClass || 'inactive';
    },

    /**
     * Get Bootstrap color class
     * @param {number} status - Status enum value
     * @returns {string} Bootstrap color class
     */
    getColorClass(status) {
        return this._config[status]?.colorClass || 'bg-secondary';
    },

    /**
     * Get full badge HTML
     * @param {number} status - Status enum value
     * @returns {string} HTML for status badge
     */
    getBadgeHtml(status) {
        const config = this._config[status] || { name: 'Unknown', cssClass: 'inactive' };
        return `<span class="status-badge status-${config.cssClass}">${config.name}</span>`;
    },

    /**
     * Check if patient is actively receiving care
     * @param {number} status - Status enum value
     * @returns {boolean}
     */
    isActive(status) {
        return status === 0;
    },

    /**
     * Check if patient can be scheduled
     * @param {number} status - Status enum value
     * @returns {boolean}
     */
    canSchedule(status) {
        return status === 0 || status === 1;
    },

    /**
     * Get all statuses for dropdowns
     * @returns {Array}
     */
    getAll() {
        return Object.entries(this._config).map(([value, config]) => ({
            value: parseInt(value),
            label: config.name
        }));
    },

    /**
     * Get options for select dropdown
     * @returns {string} HTML options
     */
    getOptions() {
        return this.getAll()
            .map(s => `<option value="${s.value}">${s.label}</option>`)
            .join('');
    }
};

/**
 * NoteStatuses - Clinical note status constants
 */
const NoteStatuses = {
    DRAFT: 0,
    PENDING_SIGNATURE: 1,
    SIGNED: 2,
    PENDING_COSIGN: 3,
    FINALIZED: 4,
    AMENDED: 5,
    VOIDED: 6,

    _config: {
        0: { name: 'Draft', cssClass: 'pending', colorClass: 'bg-secondary' },
        1: { name: 'Pending Signature', cssClass: 'pending', colorClass: 'bg-warning' },
        2: { name: 'Signed', cssClass: 'approved', colorClass: 'bg-success' },
        3: { name: 'Pending Co-Sign', cssClass: 'pending', colorClass: 'bg-warning' },
        4: { name: 'Finalized', cssClass: 'approved', colorClass: 'bg-success' },
        5: { name: 'Amended', cssClass: 'active', colorClass: 'bg-info' },
        6: { name: 'Voided', cssClass: 'inactive', colorClass: 'bg-danger' }
    },

    getName(status) {
        return this._config[status]?.name || 'Unknown';
    },

    getBadgeClass(status) {
        return this._config[status]?.cssClass || 'inactive';
    },

    getBadgeHtml(status) {
        const config = this._config[status] || { name: 'Unknown', cssClass: 'inactive' };
        return `<span class="status-badge status-${config.cssClass}">${config.name}</span>`;
    },

    canEdit(status) {
        return status === 0 || status === 1;
    },

    canSign(status) {
        return status === 1 || status === 3;
    },

    isSigned(status) {
        return status === 2 || status === 4;
    }
};

/**
 * ClaimStatuses - Billing claim status constants
 */
const ClaimStatuses = {
    DRAFT: 0,
    READY: 1,
    SUBMITTED: 2,
    ACKNOWLEDGED: 3,
    PENDING: 4,
    PAID: 5,
    PARTIALLY_PAID: 6,
    DENIED: 7,
    REJECTED: 8,

    _config: {
        0: { name: 'Draft', cssClass: 'pending', colorClass: 'bg-secondary' },
        1: { name: 'Ready', cssClass: 'active', colorClass: 'bg-info' },
        2: { name: 'Submitted', cssClass: 'active', colorClass: 'bg-primary' },
        3: { name: 'Acknowledged', cssClass: 'active', colorClass: 'bg-info' },
        4: { name: 'Pending', cssClass: 'pending', colorClass: 'bg-warning' },
        5: { name: 'Paid', cssClass: 'approved', colorClass: 'bg-success' },
        6: { name: 'Partially Paid', cssClass: 'active', colorClass: 'bg-info' },
        7: { name: 'Denied', cssClass: 'denied', colorClass: 'bg-danger' },
        8: { name: 'Rejected', cssClass: 'denied', colorClass: 'bg-danger' }
    },

    getName(status) {
        return this._config[status]?.name || 'Unknown';
    },

    getBadgeClass(status) {
        return this._config[status]?.cssClass || 'inactive';
    },

    getBadgeHtml(status) {
        const config = this._config[status] || { name: 'Unknown', cssClass: 'inactive' };
        return `<span class="status-badge status-${config.cssClass}">${config.name}</span>`;
    }
};

/**
 * TenantStatuses - Clinic/Tenant status constants
 */
const TenantStatuses = {
    PENDING: 0,
    ACTIVE: 1,
    SUSPENDED: 2,
    CANCELLED: 3,

    _config: {
        0: { name: 'Pending', cssClass: 'pending', colorClass: 'bg-warning' },
        1: { name: 'Active', cssClass: 'approved', colorClass: 'bg-success' },
        2: { name: 'Suspended', cssClass: 'denied', colorClass: 'bg-danger' },
        3: { name: 'Cancelled', cssClass: 'inactive', colorClass: 'bg-secondary' }
    },

    getName(status) {
        return this._config[status]?.name || 'Unknown';
    },

    getBadgeClass(status) {
        return this._config[status]?.cssClass || 'inactive';
    },

    getBadgeHtml(status) {
        const config = this._config[status] || { name: 'Unknown', cssClass: 'inactive' };
        return `<span class="status-badge status-${config.cssClass}">${config.name}</span>`;
    }
};

// Export for global access
window.PatientStatuses = PatientStatuses;
window.NoteStatuses = NoteStatuses;
window.ClaimStatuses = ClaimStatuses;
window.TenantStatuses = TenantStatuses;

/**
 * Universal status badge generator (backward compatible)
 * @param {number} status - Status enum value
 * @param {string} type - Status type (appointment, patient, note, claim, tenant)
 * @returns {string} HTML badge
 */
function getStatusBadge(status, type = 'appointment') {
    const statusMap = {
        appointment: AppointmentStatuses,
        patient: PatientStatuses,
        note: NoteStatuses,
        claim: ClaimStatuses,
        tenant: TenantStatuses
    };

    const statusObj = statusMap[type];
    if (statusObj) {
        return statusObj.getBadgeHtml(status);
    }

    return `<span class="status-badge status-inactive">Unknown</span>`;
}

window.getStatusBadge = getStatusBadge;
