/**
 * AppointmentTypes - Appointment type constants and utilities
 * Eliminates magic numbers throughout the codebase
 *
 * Usage:
 *   AppointmentTypes.NEW_PATIENT_VISIT   // 0
 *   AppointmentTypes.getName(0)          // "New Patient Visit"
 *   AppointmentTypes.getCode(0)          // "N"
 *   AppointmentTypes.getColor(0)         // "#4CAF50"
 */
const AppointmentTypes = {
    // Type enum values
    NEW_PATIENT_VISIT: 0,
    FOLLOW_UP_VISIT: 1,
    ANNUAL_PHYSICAL: 2,
    WELLNESS_EXAM: 3,
    CONSULTATION: 4,
    TELEHEALTH: 5,
    PROCEDURE_VISIT: 6,
    URGENT_VISIT: 7,
    LAB_REVIEW: 8,
    MEDICATION_REVIEW: 9,
    NEW_LONGEVITY_PATIENT: 10,
    FOLLOW_UP_LONGEVITY_PATIENT: 11,

    // Type names
    _names: {
        0: 'New Patient Visit',
        1: 'Follow-Up Visit',
        2: 'Annual Physical',
        3: 'Wellness Exam',
        4: 'Consultation',
        5: 'Telehealth',
        6: 'Procedure Visit',
        7: 'Urgent Visit',
        8: 'Lab Review',
        9: 'Medication Review',
        10: 'New Longevity Patient',
        11: 'Follow-Up Longevity Patient'
    },

    // Type codes for grid display
    _codes: {
        0: 'N',   // New Patient
        1: 'F',   // Follow-Up
        2: 'A',   // Annual Physical
        3: 'W',   // Wellness
        4: 'C',   // Consultation
        5: 'T',   // Telehealth
        6: 'P',   // Procedure
        7: 'U',   // Urgent
        8: 'L',   // Lab Review
        9: 'M',   // Medication Review
        10: 'NL', // New Longevity Patient
        11: 'FL'  // Follow-Up Longevity Patient
    },

    // Type colors for calendar/UI
    _colors: {
        0: '#4CAF50',  // Green - New Patient Visit
        1: '#2196F3',  // Blue - Follow-Up Visit
        2: '#9C27B0',  // Purple - Annual Physical
        3: '#00BCD4',  // Cyan - Wellness Exam
        4: '#FF9800',  // Orange - Consultation
        5: '#607D8B',  // Blue Grey - Telehealth
        6: '#E91E63',  // Pink - Procedure Visit
        7: '#F44336',  // Red - Urgent Visit
        8: '#795548',  // Brown - Lab Review
        9: '#3F51B5', // Indigo - Medication Review
        10: '#0D9488', // Teal - New Longevity Patient
        11: '#B45309'  // Amber - Follow-Up Longevity Patient
    },

    /**
     * Get the display name for a type
     * @param {number} type - Type enum value
     * @returns {string} Display name
     */
    getName(type) {
        return this._names[type] || 'Other';
    },

    /**
     * Get the short code for a type
     * @param {number} type - Type enum value
     * @returns {string} Short code (N, F, A, W, C, T, P, U, L, M)
     */
    getCode(type) {
        return this._codes[type] || '';
    },

    /**
     * Get the color for a type
     * @param {number} type - Type enum value
     * @returns {string} Hex color code
     */
    getColor(type) {
        return this._colors[type] || '#2196F3';
    },

    /**
     * Get lightened color for backgrounds
     * @param {number} type - Type enum value
     * @param {number} [percent=85] - Lighten percentage
     * @returns {string} Lightened hex color
     */
    getLightColor(type, percent = 85) {
        const hex = this.getColor(type);
        return ColorUtils.lighten(hex, percent);
    },

    /**
     * Get all types as array for dropdowns
     * @returns {Array} Array of { value, label, code, color }
     */
    getAll() {
        return Object.entries(this._names).map(([value, label]) => ({
            value: parseInt(value),
            label,
            code: this._codes[value],
            color: this._colors[value]
        }));
    },

    /**
     * Get options for select dropdown
     * @returns {string} HTML options string
     */
    getOptions() {
        return this.getAll()
            .map(t => `<option value="${t.value}">${t.label}</option>`)
            .join('');
    },

    /**
     * Check if a type value is valid
     * @param {number} type - Type to check
     * @returns {boolean}
     */
    isValid(type) {
        return type !== null && type !== undefined && this._names.hasOwnProperty(type);
    },

    /**
     * Get type from code
     * @param {string} code - Type code (N, F, A, W, C, T, P, U, L, M)
     * @returns {number|null} Type enum value
     */
    fromCode(code) {
        const entry = Object.entries(this._codes).find(([, c]) => c === code?.toUpperCase());
        return entry ? parseInt(entry[0]) : null;
    }
};

// Color utility for lightening (used by AppointmentTypes)
const ColorUtils = {
    /**
     * Convert hex to RGB
     * @param {string} hex - Hex color
     * @returns {Object} { r, g, b }
     */
    hexToRgb(hex) {
        const h = hex.replace('#', '');
        const full = h.length === 3 ? h.split('').map(c => c + c).join('') : h;
        const bigint = parseInt(full, 16);
        return {
            r: (bigint >> 16) & 255,
            g: (bigint >> 8) & 255,
            b: bigint & 255
        };
    },

    /**
     * Lighten a hex color
     * @param {string} hex - Hex color
     * @param {number} percent - Lighten percentage
     * @returns {string} Lightened hex color
     */
    lighten(hex, percent) {
        const { r, g, b } = this.hexToRgb(hex);
        const calc = (v) => Math.round(v + (255 - v) * (percent / 100));
        return `#${((1 << 24) + (calc(r) << 16) + (calc(g) << 8) + calc(b)).toString(16).slice(1)}`;
    },

    /**
     * Darken a hex color
     * @param {string} hex - Hex color
     * @param {number} percent - Darken percentage
     * @returns {string} Darkened hex color
     */
    darken(hex, percent) {
        const { r, g, b } = this.hexToRgb(hex);
        const calc = (v) => Math.round(v * (1 - percent / 100));
        return `#${((1 << 24) + (calc(r) << 16) + (calc(g) << 8) + calc(b)).toString(16).slice(1)}`;
    }
};

// Export for global access
window.AppointmentTypes = AppointmentTypes;
window.ColorUtils = ColorUtils;

// Backward compatibility
window.appointmentTypeColors = AppointmentTypes._colors;
window.getAppointmentTypeName = AppointmentTypes.getName.bind(AppointmentTypes);
window.getAppointmentTypeCode = AppointmentTypes.getCode.bind(AppointmentTypes);
window.hexToRgb = ColorUtils.hexToRgb.bind(ColorUtils);
window.lightenHex = ColorUtils.lighten.bind(ColorUtils);
