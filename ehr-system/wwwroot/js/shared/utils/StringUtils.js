/**
 * StringUtils - String manipulation utilities
 * Common string operations used throughout the application
 *
 * Usage:
 *   StringUtils.escape('<script>alert("xss")</script>');
 *   StringUtils.truncate('Long text here', 10);
 *   StringUtils.capitalize('hello world');
 */
const StringUtils = {
    /**
     * Escape HTML special characters to prevent XSS
     * @param {string} unsafe - Unsafe string
     * @returns {string} Escaped string
     */
    escape(unsafe) {
        if (unsafe == null) return '';
        return String(unsafe)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    },

    /**
     * Alias for escape (commonly used name)
     */
    escapeHtml(unsafe) {
        return this.escape(unsafe);
    },

    /**
     * Truncate a string to a maximum length
     * @param {string} str - String to truncate
     * @param {number} length - Maximum length
     * @param {string} [suffix='...'] - Suffix to add if truncated
     * @returns {string} Truncated string
     */
    truncate(str, length, suffix = '...') {
        if (!str) return '';
        if (str.length <= length) return str;
        return str.substring(0, length - suffix.length) + suffix;
    },

    /**
     * Capitalize the first letter of a string
     * @param {string} str - String to capitalize
     * @returns {string} Capitalized string
     */
    capitalize(str) {
        if (!str) return '';
        return str.charAt(0).toUpperCase() + str.slice(1).toLowerCase();
    },

    /**
     * Capitalize the first letter of each word
     * @param {string} str - String to title case
     * @returns {string} Title cased string
     */
    titleCase(str) {
        if (!str) return '';
        return str.split(' ')
            .map(word => this.capitalize(word))
            .join(' ');
    },

    /**
     * Convert string to slug (URL-friendly)
     * @param {string} str - String to slugify
     * @returns {string} Slugified string
     */
    slugify(str) {
        if (!str) return '';
        return str
            .toLowerCase()
            .trim()
            .replace(/[^\w\s-]/g, '')
            .replace(/[\s_-]+/g, '-')
            .replace(/^-+|-+$/g, '');
    },

    /**
     * Convert camelCase to Title Case
     * @param {string} str - camelCase string
     * @returns {string} Title Case string
     */
    camelToTitle(str) {
        if (!str) return '';
        return str
            .replace(/([A-Z])/g, ' $1')
            .replace(/^./, char => char.toUpperCase())
            .trim();
    },

    /**
     * Check if string is empty or whitespace only
     * @param {string} str - String to check
     * @returns {boolean}
     */
    isEmpty(str) {
        return !str || str.trim().length === 0;
    },

    /**
     * Check if string is not empty
     * @param {string} str - String to check
     * @returns {boolean}
     */
    isNotEmpty(str) {
        return !this.isEmpty(str);
    },

    /**
     * Remove all whitespace from a string
     * @param {string} str - String to process
     * @returns {string} String without whitespace
     */
    removeWhitespace(str) {
        if (!str) return '';
        return str.replace(/\s/g, '');
    },

    /**
     * Normalize whitespace (collapse multiple spaces to single)
     * @param {string} str - String to normalize
     * @returns {string} Normalized string
     */
    normalizeWhitespace(str) {
        if (!str) return '';
        return str.replace(/\s+/g, ' ').trim();
    },

    /**
     * Pad a string on the left
     * @param {string} str - String to pad
     * @param {number} length - Target length
     * @param {string} [char='0'] - Character to pad with
     * @returns {string} Padded string
     */
    padLeft(str, length, char = '0') {
        str = String(str);
        while (str.length < length) {
            str = char + str;
        }
        return str;
    },

    /**
     * Pad a string on the right
     * @param {string} str - String to pad
     * @param {number} length - Target length
     * @param {string} [char=' '] - Character to pad with
     * @returns {string} Padded string
     */
    padRight(str, length, char = ' ') {
        str = String(str);
        while (str.length < length) {
            str = str + char;
        }
        return str;
    },

    /**
     * Generate a random string
     * @param {number} [length=8] - Length of string
     * @param {string} [chars] - Characters to use
     * @returns {string} Random string
     */
    random(length = 8, chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789') {
        let result = '';
        for (let i = 0; i < length; i++) {
            result += chars.charAt(Math.floor(Math.random() * chars.length));
        }
        return result;
    },

    /**
     * Strip HTML tags from a string
     * @param {string} str - String with HTML
     * @returns {string} Plain text string
     */
    stripHtml(str) {
        if (!str) return '';
        return str.replace(/<[^>]*>/g, '');
    },

    /**
     * Convert newlines to <br> tags
     * @param {string} str - String with newlines
     * @returns {string} String with <br> tags
     */
    nl2br(str) {
        if (!str) return '';
        return str.replace(/\n/g, '<br>');
    },

    /**
     * Get initials from a name
     * @param {string} name - Full name
     * @param {number} [count=2] - Number of initials
     * @returns {string} Initials
     */
    getInitials(name, count = 2) {
        if (!name) return '';
        return name
            .split(' ')
            .slice(0, count)
            .map(word => word.charAt(0).toUpperCase())
            .join('');
    },

    /**
     * Format a name as "Last, First"
     * @param {string} firstName - First name
     * @param {string} lastName - Last name
     * @returns {string} Formatted name
     */
    formatNameLastFirst(firstName, lastName) {
        if (!lastName && !firstName) return '';
        if (!lastName) return firstName;
        if (!firstName) return lastName;
        return `${lastName}, ${firstName}`;
    },

    /**
     * Check if string contains another string (case insensitive)
     * @param {string} str - String to search in
     * @param {string} search - String to search for
     * @returns {boolean}
     */
    containsIgnoreCase(str, search) {
        if (!str || !search) return false;
        return str.toLowerCase().includes(search.toLowerCase());
    }
};

// Export for global access
window.StringUtils = StringUtils;

// Also export escapeHtml as global function for backward compatibility
window.escapeHtml = StringUtils.escape.bind(StringUtils);
