/**
 * InputMaskUtils - Input masking and validation utilities
 *
 * Usage:
 *   Add data-mask="ssn" to input field
 *   Call InputMaskUtils.initialize() on page load
 */
const InputMaskUtils = {
    /**
     * Initialize all input masks on the page
     */
    initialize() {
        // SSN masking
        const ssnInputs = document.querySelectorAll('input[data-mask="ssn"]');
        ssnInputs.forEach(input => {
            if (!input.dataset.maskApplied) {
                this.applySSNMask(input);
                input.dataset.maskApplied = 'true';
            }
        });

        // Phone masking
        const phoneInputs = document.querySelectorAll('input[data-mask="phone"]');
        phoneInputs.forEach(input => {
            if (!input.dataset.maskApplied) {
                this.applyPhoneMask(input);
                input.dataset.maskApplied = 'true';
            }
        });
    },

    /**
     * Apply SSN input mask (XXX-XX-XXXX)
     * @param {HTMLInputElement} input - Input element
     */
    applySSNMask(input) {
        if (!input) return;

        const errorElement = document.getElementById(input.id + 'Error') ||
                            input.parentElement.querySelector('.invalid-feedback');

        // Format on input
        input.addEventListener('input', (e) => {
            let value = e.target.value;

            // Remove all non-digit characters
            const digits = value.replace(/\D/g, '');

            // Limit to 9 digits
            const limitedDigits = digits.substring(0, 9);

            // Format as XXX-XX-XXXX
            let formatted = '';
            if (limitedDigits.length > 0) {
                formatted = limitedDigits.substring(0, 3);
            }
            if (limitedDigits.length > 3) {
                formatted += '-' + limitedDigits.substring(3, 5);
            }
            if (limitedDigits.length > 5) {
                formatted += '-' + limitedDigits.substring(5, 9);
            }

            e.target.value = formatted;

            // Clear any previous validation
            this.clearError(input, errorElement);
        });

        // Validate on blur
        input.addEventListener('blur', (e) => {
            this.validateSSN(e.target, errorElement);
        });

        // Prevent form submission if invalid
        const form = input.closest('form');
        if (form) {
            form.addEventListener('submit', (e) => {
                if (!this.validateSSN(input, errorElement)) {
                    e.preventDefault();
                    e.stopPropagation();
                    input.focus();
                    return false;
                }
            });
        }
    },

    /**
     * Validate SSN field
     * @param {HTMLInputElement} input - Input element
     * @param {HTMLElement} errorElement - Error display element
     * @returns {boolean} True if valid or empty, false if invalid
     */
    validateSSN(input, errorElement) {
        if (!input) return true;

        const value = input.value.trim();

        // Empty is allowed (optional field)
        if (value === '') {
            this.clearError(input, errorElement);
            return true;
        }

        const digits = value.replace(/\D/g, '');

        // Check if complete
        if (digits.length < 9) {
            this.showError(input, errorElement, 'SSN must be exactly 9 digits (XXX-XX-XXXX)');
            return false;
        }

        // Valid SSN
        this.clearError(input, errorElement);
        return true;
    },

    /**
     * Show validation error
     * @param {HTMLInputElement} input - Input element
     * @param {HTMLElement} errorElement - Error display element
     * @param {string} message - Error message
     */
    showError(input, errorElement, message) {
        if (!input) return;

        input.classList.add('is-invalid');
        input.classList.remove('is-valid');

        if (errorElement) {
            errorElement.textContent = message;
            errorElement.style.display = 'block';
        }
    },

    /**
     * Clear validation error
     * @param {HTMLInputElement} input - Input element
     * @param {HTMLElement} errorElement - Error display element
     */
    clearError(input, errorElement) {
        if (!input) return;

        input.classList.remove('is-invalid');
        input.classList.add('is-valid');

        if (errorElement) {
            errorElement.textContent = '';
            errorElement.style.display = 'none';
        }
    },

    /**
     * Apply phone input mask ((XXX) XXX-XXXX)
     * @param {HTMLInputElement} input - Input element
     */
    applyPhoneMask(input) {
        if (!input) return;

        input.addEventListener('input', (e) => {
            let value = e.target.value;

            // Remove all non-digit characters
            const digits = value.replace(/\D/g, '');

            // Limit to 10 digits
            const limitedDigits = digits.substring(0, 10);

            // Format as (XXX) XXX-XXXX
            let formatted = '';
            if (limitedDigits.length > 0) {
                formatted = '(' + limitedDigits.substring(0, 3);
            }
            if (limitedDigits.length > 3) {
                formatted += ') ' + limitedDigits.substring(3, 6);
            }
            if (limitedDigits.length > 6) {
                formatted += '-' + limitedDigits.substring(6, 10);
            }

            e.target.value = formatted;
        });
    },

    /**
     * Get raw digits from masked input
     * @param {string} value - Masked value
     * @returns {string} Digits only
     */
    getDigits(value) {
        if (!value) return '';
        return value.replace(/\D/g, '');
    },

    /**
     * Initialize modal listeners to apply masks when modals are shown
     */
    initModalListeners() {
        // Listen for Bootstrap modal shown events to re-initialize masks
        document.addEventListener('shown.bs.modal', (e) => {
            const modal = e.target;
            // Apply phone masks to any phone inputs in the modal
            const phoneInputs = modal.querySelectorAll('input[data-mask="phone"]');
            phoneInputs.forEach(input => {
                // Only apply if not already masked (check for listener)
                if (!input.dataset.maskApplied) {
                    this.applyPhoneMask(input);
                    input.dataset.maskApplied = 'true';
                }
            });
            // Apply SSN masks to any SSN inputs in the modal
            const ssnInputs = modal.querySelectorAll('input[data-mask="ssn"]');
            ssnInputs.forEach(input => {
                if (!input.dataset.maskApplied) {
                    this.applySSNMask(input);
                    input.dataset.maskApplied = 'true';
                }
            });
        });
    }
};

// Export for global access
window.InputMaskUtils = InputMaskUtils;

// Auto-initialize on DOM ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
        InputMaskUtils.initialize();
        InputMaskUtils.initModalListeners();
    });
} else {
    // DOM already loaded
    InputMaskUtils.initialize();
    InputMaskUtils.initModalListeners();
}
