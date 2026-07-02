/**
 * DateUtils - Date manipulation and formatting utilities
 * Handles basic date operations (timezone-specific operations are in TimezoneUtils)
 *
 * Usage:
 *   DateUtils.format(date, 'MM/DD/YYYY');
 *   DateUtils.formatTime(date);
 *   DateUtils.isToday(date);
 */
const DateUtils = {
    /**
     * Parse a server datetime string as UTC
     * Server stores all times in UTC but may not include 'Z' suffix
     * @param {string} dateString - Date string from server
     * @returns {Date|null} Date object in local time
     */
    parseServerDateTime(dateString) {
        if (!dateString) return null;

        // If the string already has timezone info (Z or +/-), parse directly
        if (dateString.endsWith('Z') || /[+-]\d{2}:\d{2}$/.test(dateString)) {
            return new Date(dateString);
        }

        // Server returns UTC times without 'Z' suffix - append it to ensure UTC parsing
        return new Date(dateString + 'Z');
    },

    /**
     * Format a date for display (short date format)
     * @param {Date|string} date - Date object or date string
     * @param {string} [locale='en-US'] - Locale for formatting
     * @returns {string} Formatted date (e.g., "Dec 18, 2025")
     */
    format(date, locale = 'en-US') {
        if (!date) return '-';
        const d = typeof date === 'string' ? this.parseServerDateTime(date) : date;
        if (!d || isNaN(d.getTime())) return '-';
        return d.toLocaleDateString(locale, { month: 'short', day: 'numeric', year: 'numeric' });
    },

    /**
     * Format a date as time only
     * @param {Date|string} date - Date object or date string
     * @param {string} [locale='en-US'] - Locale for formatting
     * @returns {string} Formatted time (e.g., "9:30 AM")
     */
    formatTime(date, locale = 'en-US') {
        if (!date) return '-';
        const d = typeof date === 'string' ? this.parseServerDateTime(date) : date;
        if (!d || isNaN(d.getTime())) return '-';
        return d.toLocaleTimeString(locale, { hour: 'numeric', minute: '2-digit' });
    },

    /**
     * Format a date as full datetime
     * @param {Date|string} date - Date object or date string
     * @param {string} [locale='en-US'] - Locale for formatting
     * @returns {string} Formatted datetime (e.g., "Dec 18, 2025 9:30 AM")
     */
    formatDateTime(date, locale = 'en-US') {
        if (!date) return '-';
        return `${this.format(date, locale)} ${this.formatTime(date, locale)}`;
    },

    /**
     * Format a Date object for date input field (YYYY-MM-DD)
     * @param {Date} date - Date object
     * @returns {string} Formatted date for input
     */
    formatForInput(date) {
        if (!date) return '';
        const year = date.getFullYear();
        const month = String(date.getMonth() + 1).padStart(2, '0');
        const day = String(date.getDate()).padStart(2, '0');
        return `${year}-${month}-${day}`;
    },

    /**
     * Format a Date object for time input field (HH:MM)
     * @param {Date} date - Date object
     * @returns {string} Formatted time for input
     */
    formatTimeForInput(date) {
        if (!date) return '';
        const hours = String(date.getHours()).padStart(2, '0');
        const minutes = String(date.getMinutes()).padStart(2, '0');
        return `${hours}:${minutes}`;
    },

    /**
     * Format date as ISO string (YYYY-MM-DDTHH:MM:SS)
     * @param {Date} date - Date object
     * @returns {string} ISO formatted string (without Z)
     */
    formatISO(date) {
        if (!date) return '';
        const year = date.getFullYear();
        const month = String(date.getMonth() + 1).padStart(2, '0');
        const day = String(date.getDate()).padStart(2, '0');
        const hours = String(date.getHours()).padStart(2, '0');
        const minutes = String(date.getMinutes()).padStart(2, '0');
        const seconds = String(date.getSeconds()).padStart(2, '0');
        return `${year}-${month}-${day}T${hours}:${minutes}:${seconds}`;
    },

    /**
     * Check if a date is today
     * @param {Date|string} date - Date to check
     * @returns {boolean}
     */
    isToday(date) {
        const d = typeof date === 'string' ? this.parseServerDateTime(date) : date;
        if (!d) return false;
        const today = new Date();
        return d.getDate() === today.getDate() &&
               d.getMonth() === today.getMonth() &&
               d.getFullYear() === today.getFullYear();
    },

    /**
     * Check if a date is in the past
     * @param {Date|string} date - Date to check
     * @returns {boolean}
     */
    isPast(date) {
        const d = typeof date === 'string' ? this.parseServerDateTime(date) : date;
        if (!d) return false;
        return d < new Date();
    },

    /**
     * Check if a date is in the future
     * @param {Date|string} date - Date to check
     * @returns {boolean}
     */
    isFuture(date) {
        const d = typeof date === 'string' ? this.parseServerDateTime(date) : date;
        if (!d) return false;
        return d > new Date();
    },

    /**
     * Get start of day for a date
     * @param {Date} date - Date object
     * @returns {Date} Start of day
     */
    startOfDay(date) {
        const d = new Date(date);
        d.setHours(0, 0, 0, 0);
        return d;
    },

    /**
     * Get end of day for a date
     * @param {Date} date - Date object
     * @returns {Date} End of day
     */
    endOfDay(date) {
        const d = new Date(date);
        d.setHours(23, 59, 59, 999);
        return d;
    },

    /**
     * Add days to a date
     * @param {Date} date - Date object
     * @param {number} days - Number of days to add (can be negative)
     * @returns {Date} New date
     */
    addDays(date, days) {
        const d = new Date(date);
        d.setDate(d.getDate() + days);
        return d;
    },

    /**
     * Add hours to a date
     * @param {Date} date - Date object
     * @param {number} hours - Number of hours to add
     * @returns {Date} New date
     */
    addHours(date, hours) {
        const d = new Date(date);
        d.setHours(d.getHours() + hours);
        return d;
    },

    /**
     * Add minutes to a date
     * @param {Date} date - Date object
     * @param {number} minutes - Number of minutes to add
     * @returns {Date} New date
     */
    addMinutes(date, minutes) {
        const d = new Date(date);
        d.setMinutes(d.getMinutes() + minutes);
        return d;
    },

    /**
     * Get the difference in minutes between two dates
     * @param {Date} date1 - First date
     * @param {Date} date2 - Second date
     * @returns {number} Difference in minutes
     */
    differenceInMinutes(date1, date2) {
        return Math.round((date1 - date2) / 60000);
    },

    /**
     * Get the difference in hours between two dates
     * @param {Date} date1 - First date
     * @param {Date} date2 - Second date
     * @returns {number} Difference in hours
     */
    differenceInHours(date1, date2) {
        return Math.round((date1 - date2) / 3600000);
    },

    /**
     * Get the difference in days between two dates
     * @param {Date} date1 - First date
     * @param {Date} date2 - Second date
     * @returns {number} Difference in days
     */
    differenceInDays(date1, date2) {
        const d1 = this.startOfDay(date1);
        const d2 = this.startOfDay(date2);
        return Math.round((d1 - d2) / 86400000);
    },

    /**
     * Get relative time string (e.g., "2 hours ago", "in 3 days")
     * @param {Date|string} date - Date to compare
     * @returns {string} Relative time string
     */
    relativeTime(date) {
        const d = typeof date === 'string' ? this.parseServerDateTime(date) : date;
        if (!d) return '';

        const now = new Date();
        const diffMs = d - now;
        const diffMins = Math.round(diffMs / 60000);
        const diffHours = Math.round(diffMs / 3600000);
        const diffDays = Math.round(diffMs / 86400000);

        if (Math.abs(diffMins) < 1) return 'just now';
        if (Math.abs(diffMins) < 60) {
            return diffMins > 0 ? `in ${diffMins} minute${diffMins !== 1 ? 's' : ''}` : `${Math.abs(diffMins)} minute${Math.abs(diffMins) !== 1 ? 's' : ''} ago`;
        }
        if (Math.abs(diffHours) < 24) {
            return diffHours > 0 ? `in ${diffHours} hour${diffHours !== 1 ? 's' : ''}` : `${Math.abs(diffHours)} hour${Math.abs(diffHours) !== 1 ? 's' : ''} ago`;
        }
        if (Math.abs(diffDays) < 7) {
            return diffDays > 0 ? `in ${diffDays} day${diffDays !== 1 ? 's' : ''}` : `${Math.abs(diffDays)} day${Math.abs(diffDays) !== 1 ? 's' : ''} ago`;
        }

        return this.format(d);
    },

    /**
     * Get user's timezone
     * @returns {string} IANA timezone identifier
     */
    getUserTimezone() {
        return Intl.DateTimeFormat().resolvedOptions().timeZone;
    },

    /**
     * Get current date as YYYY-MM-DD string
     * @returns {string}
     */
    today() {
        return this.formatForInput(new Date());
    },

    /**
     * Get current time as HH:MM string
     * @returns {string}
     */
    now() {
        return this.formatTimeForInput(new Date());
    },

    /**
     * Parse a date string in various formats
     * @param {string} str - Date string
     * @returns {Date|null}
     */
    parse(str) {
        if (!str) return null;
        const d = new Date(str);
        return isNaN(d.getTime()) ? null : d;
    },

    /**
     * Check if a value is a valid date
     * @param {*} value - Value to check
     * @returns {boolean}
     */
    isValid(value) {
        if (!value) return false;
        const d = value instanceof Date ? value : new Date(value);
        return !isNaN(d.getTime());
    }
};

// Export for global access
window.DateUtils = DateUtils;

// Backward compatibility exports
window.parseServerDateTime = DateUtils.parseServerDateTime.bind(DateUtils);
window.formatDate = DateUtils.format.bind(DateUtils);
window.formatTime = DateUtils.formatTime.bind(DateUtils);
window.formatDateTime = DateUtils.formatDateTime.bind(DateUtils);
window.formatDateForInput = DateUtils.formatForInput.bind(DateUtils);
window.formatTimeForInput = DateUtils.formatTimeForInput.bind(DateUtils);
window.getUserTimezone = DateUtils.getUserTimezone.bind(DateUtils);
