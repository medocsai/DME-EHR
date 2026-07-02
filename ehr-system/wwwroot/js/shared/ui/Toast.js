/**
 * Toast - Toast notification system
 * Provides consistent notification display throughout the application
 *
 * Usage:
 *   Toast.success('Title', 'Message');
 *   Toast.error('Error', 'Something went wrong');
 *   Toast.warning('Warning', 'Be careful');
 *   Toast.info('Info', 'FYI...');
 */
const Toast = {
    // Default options
    defaults: {
        autohide: true,
        delay: 6000 // 6 seconds
    },

    // Toast container element
    container: null,
    toastElement: null,
    titleElement: null,
    messageElement: null,

    /**
     * Initialize toast elements
     */
    init() {
        this.toastElement = document.getElementById('toast');
        this.titleElement = document.getElementById('toastTitle');
        this.messageElement = document.getElementById('toastMessage');
        this.container = document.getElementById('toastContainer');
    },

    /**
     * Show a toast notification
     * @param {string} title - Toast title
     * @param {string} message - Toast message
     * @param {string} [type='success'] - Type: success, error, warning, info
     * @param {Object} [options] - Additional options
     */
    show(title, message, type = 'success', options = {}) {
        // Lazy init
        if (!this.toastElement) {
            this.init();
        }

        // If no toast elements exist, fallback to console/alert
        if (!this.toastElement || !this.titleElement || !this.messageElement) {
            if (type === 'error') {
                console.error(`${title}: ${message}`);
                alert(`${title}: ${message}`);
            } else {
                console.log(`${title}: ${message}`);
            }
            return;
        }

        // Set content
        this.titleElement.textContent = title;
        this.messageElement.textContent = message;

        // Set styling based on type
        this.toastElement.className = 'toast show-toast';
        this.toastElement.classList.add(`toast-${type === 'error' ? 'danger' : type}`);

        // Merge options with defaults
        const config = { ...this.defaults, ...options };

        // Create and show Bootstrap toast
        const bsToast = new bootstrap.Toast(this.toastElement, {
            autohide: config.autohide,
            delay: config.delay
        });
        bsToast.show();
    },

    /**
     * Show a success toast
     * @param {string} title - Toast title
     * @param {string} message - Toast message
     * @param {Object} [options] - Additional options
     */
    success(title, message, options = {}) {
        this.show(title, message, 'success', options);
    },

    /**
     * Show an error toast
     * @param {string} title - Toast title
     * @param {string} message - Toast message
     * @param {Object} [options] - Additional options
     */
    error(title, message, options = {}) {
        this.show(title, message, 'error', options);
    },

    /**
     * Show a warning toast
     * @param {string} title - Toast title
     * @param {string} message - Toast message
     * @param {Object} [options] - Additional options
     */
    warning(title, message, options = {}) {
        this.show(title, message, 'warning', options);
    },

    /**
     * Show an info toast
     * @param {string} title - Toast title
     * @param {string} message - Toast message
     * @param {Object} [options] - Additional options
     */
    info(title, message, options = {}) {
        this.show(title, message, 'info', options);
    },

    /**
     * Hide the current toast
     */
    hide() {
        if (!this.toastElement) return;

        const bsToast = bootstrap.Toast.getInstance(this.toastElement);
        if (bsToast) {
            bsToast.hide();
        }
    }
};

// Export for global access
window.Toast = Toast;

// Backward compatibility
window.showToast = function(title, message, type = 'success') {
    Toast.show(title, message, type);
};
