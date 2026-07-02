/**
 * PatientUtilities - Shared utility functions for patient module
 * Pure utility functions with no dependencies
 */

const PatientUtilities = {
    /**
     * Escape HTML to prevent XSS
     * @param {string} str - String to escape
     * @returns {string} Escaped string
     */
    escape(str) {
        if (str === null || str === undefined) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    },

    /**
     * Format date to locale string
     * @param {string} dateStr - ISO date string (YYYY-MM-DD format)
     * @returns {string} Formatted date or original string if invalid
     */
    formatDate(dateStr) {
        if (!dateStr) return '-';
        try {
            // Handle date-only strings (YYYY-MM-DD) to avoid timezone shifting
            // When parsed as UTC midnight, they can shift to previous day in local time
            if (typeof dateStr === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(dateStr)) {
                const [year, month, day] = dateStr.split('-').map(Number);
                // Create date using local timezone (months are 0-indexed)
                return new Date(year, month - 1, day).toLocaleDateString();
            }
            return new Date(dateStr).toLocaleDateString();
        } catch {
            return dateStr;
        }
    },

    /**
     * Debounce function for event handlers
     * @param {Function} fn - Function to debounce
     * @param {number} delay - Delay in milliseconds
     * @param {Object} context - Context to bind to (optional)
     * @returns {Function} Debounced function
     */
    debounce(fn, delay, context) {
        let timeoutId;
        return (...args) => {
            clearTimeout(timeoutId);
            timeoutId = setTimeout(() => {
                if (context) {
                    fn.apply(context, args);
                } else {
                    fn(...args);
                }
            }, delay);
        };
    },

    /**
     * Get avatar color based on patient name
     * @param {Object} patient - Patient data
     * @returns {string} Color hex value
     */
    getAvatarColor(patient) {
        const colors = ['#2196F3', '#4CAF50', '#FF9800', '#9C27B0', '#00BCD4', '#E91E63'];
        const hash = (patient.FullName || '').split('').reduce((acc, char) => acc + char.charCodeAt(0), 0);
        return colors[hash % colors.length];
    },

    /**
     * Get current user from localStorage
     * @returns {Object|null} User object or null if not found
     */
    getCurrentUser() {
        try {
            return JSON.parse(localStorage.getItem('currentUser'));
        } catch {
            return null;
        }
    },

    /**
     * Get authorization headers with token
     * @returns {Object} Headers object with auth token
     */
    getHeaders() {
        const token = localStorage.getItem('authToken');
        return {
            'Content-Type': 'application/json',
            'Authorization': token ? `Bearer ${token}` : ''
        };
    },

    /**
     * Get file icon class based on file extension
     * @param {string} fileName - File name
     * @returns {string} Bootstrap icon class
     */
    getFileIcon(fileName) {
        if (!fileName) return 'bi-file-earmark';
        const ext = fileName.split('.').pop().toLowerCase();
        const iconMap = {
            'pdf': 'bi-file-earmark-pdf text-danger',
            'doc': 'bi-file-earmark-word text-info',
            'docx': 'bi-file-earmark-word text-info',
            'xls': 'bi-file-earmark-excel text-success',
            'xlsx': 'bi-file-earmark-excel text-success',
            'jpg': 'bi-file-earmark-image text-warning',
            'jpeg': 'bi-file-earmark-image text-warning',
            'png': 'bi-file-earmark-image text-warning',
            'txt': 'bi-file-earmark-text',
            'zip': 'bi-file-earmark-zip'
        };
        return iconMap[ext] || 'bi-file-earmark';
    },

    /**
     * Format bytes to human-readable size
     * @param {number} bytes - Size in bytes
     * @returns {string} Formatted size
     */
    formatFileSize(bytes) {
        if (bytes === 0) return '0 Bytes';
        const k = 1024;
        const sizes = ['Bytes', 'KB', 'MB', 'GB'];
        const i = Math.floor(Math.log(bytes) / Math.log(k));
        return Math.round((bytes / Math.pow(k, i)) * 100) / 100 + ' ' + sizes[i];
    },

    /**
     * Calculate profile completeness percentage
     * @param {Object} patient - Patient data
     * @returns {number} Completeness percentage (0-100)
     */
    calculateProfileCompleteness(patient) {
        const requiredFields = ['FirstName', 'LastName', 'DateOfBirth', 'Phone', 'Email', 'Address', 'City', 'State', 'ZipCode'];
        const optionalFields = ['Gender', 'EmergencyContactName', 'EmergencyContactPhone'];

        let filled = 0;
        let total = requiredFields.length;

        requiredFields.forEach(field => {
            if (patient[field] && patient[field].toString().trim() !== '') {
                filled++;
            }
        });

        // Add bonus for optional fields
        optionalFields.forEach(field => {
            if (patient[field] && patient[field].toString().trim() !== '') {
                filled += 0.5;
                total += 0.5;
            }
        });

        // Add bonus for insurance
        if (patient.Insurances && patient.Insurances.length > 0) {
            filled += 1;
            total += 1;
        }

        return Math.round((filled / total) * 100);
    }
};

// Export for use in both modern and legacy environments
if (typeof module !== 'undefined' && module.exports) {
    module.exports = PatientUtilities;
}
window.PatientUtilities = PatientUtilities;
