/**
 * AppointmentStatuses - Appointment status constants and utilities
 *
 * Usage:
 *   AppointmentStatuses.SCHEDULED           // 0
 *   AppointmentStatuses.getName(0)          // "Scheduled"
 *   AppointmentStatuses.getBadgeClass(0)    // "scheduled"
 *   AppointmentStatuses.getBadgeHtml(0)     // Full badge HTML
 */
const AppointmentStatuses = {
    // Status enum values
    SCHEDULED: 0,
    CONFIRMED: 1,
    CHECKED_IN: 2,
    IN_PROGRESS: 3,
    COMPLETED: 4,
    NO_SHOW: 5,
    CANCELLED: 6,
    RESCHEDULED: 7,
    MISSED: 8,

    // Status display names and CSS classes
    _config: {
        0: { name: 'Scheduled', cssClass: 'scheduled', colorClass: 'bg-primary' },
        1: { name: 'Confirmed', cssClass: 'confirmed', colorClass: 'bg-info' },
        2: { name: 'Checked In', cssClass: 'checkedin', colorClass: 'bg-info' },
        3: { name: 'In Progress', cssClass: 'inprogress', colorClass: 'bg-warning' },
        4: { name: 'Completed', cssClass: 'completed', colorClass: 'bg-success' },
        5: { name: 'No Show', cssClass: 'noshow', colorClass: 'bg-danger' },
        6: { name: 'Cancelled', cssClass: 'cancelled', colorClass: 'bg-secondary' },
        7: { name: 'Rescheduled', cssClass: 'rescheduled', colorClass: 'bg-info' },
        8: { name: 'Missed', cssClass: 'missed', colorClass: 'bg-danger' }
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
     * Get status indicator for Monthly Grid report
     * @param {number} status - Status enum value
     * @param {string} startTime - Appointment start time (UTC)
     * @returns {Object} { indicator, colorClass, statusName }
     */
    getIndicator(status, startTime) {
        // (A) Attempted = Patient checked in (Status >= 2 and <= 4)
        if (status >= 2 && status <= 4) {
            return {
                indicator: 'A',
                colorClass: null,
                statusName: 'Attempted (Checked In)'
            };
        }

        // (C) Cancelled = Admin cancelled (Status == 6)
        if (status === 6) {
            return {
                indicator: 'C',
                colorClass: 'visit-code-cancelled',
                statusName: 'Cancelled'
            };
        }

        // (M) Missed = Explicitly marked (Status == 8)
        if (status === 8) {
            return {
                indicator: 'M',
                colorClass: 'visit-code-missed',
                statusName: 'Missed'
            };
        }

        // Calculated missed: past date + no check-in
        if ((status === 0 || status === 1) && startTime) {
            const appointmentDate = DateUtils.parseServerDateTime(startTime);
            const today = new Date();
            today.setHours(0, 0, 0, 0);
            if (appointmentDate && appointmentDate < today) {
                return {
                    indicator: 'M',
                    colorClass: 'visit-code-missed',
                    statusName: 'Missed'
                };
            }
        }

        // No indicator for normal/future scheduled appointments
        return {
            indicator: null,
            colorClass: null,
            statusName: ''
        };
    },

    /**
     * Check if status indicates appointment was attended
     * @param {number} status - Status enum value
     * @returns {boolean}
     */
    isAttended(status) {
        return status >= 2 && status <= 4;
    },

    /**
     * Check if status indicates appointment is active (not completed/cancelled)
     * @param {number} status - Status enum value
     * @returns {boolean}
     */
    isActive(status) {
        return status >= 0 && status <= 3;
    },

    /**
     * Check if status indicates a problem (no show, cancelled, missed)
     * @param {number} status - Status enum value
     * @returns {boolean}
     */
    isProblem(status) {
        return status === 5 || status === 6 || status === 8;
    },

    /**
     * Get all statuses for dropdowns
     * @returns {Array} Array of { value, label }
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
    },

    /**
     * Check if status value is valid
     * @param {number} status - Status to check
     * @returns {boolean}
     */
    isValid(status) {
        return status !== null && status !== undefined && this._config.hasOwnProperty(status);
    }
};

// Export for global access
window.AppointmentStatuses = AppointmentStatuses;

// Backward compatibility
window.getAppointmentStatusIndicator = AppointmentStatuses.getIndicator.bind(AppointmentStatuses);
