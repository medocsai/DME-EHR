/**
 * ValidationUtils - Form and data validation utilities
 *
 * Usage:
 *   ValidationUtils.isEmail('user@example.com');
 *   ValidationUtils.isPhone('555-123-4567');
 *   ValidationUtils.validateForm(formElement, rules);
 */
const ValidationUtils = {
    /**
     * Email regex pattern
     */
    EMAIL_PATTERN: /^[^\s@]+@[^\s@]+\.[^\s@]+$/,

    /**
     * Phone regex pattern (US format)
     */
    PHONE_PATTERN: /^[\d\s\-()]+$/,

    /**
     * Check if value is a valid email
     * @param {string} email - Email to validate
     * @returns {boolean}
     */
    isEmail(email) {
        if (!email) return false;
        return this.EMAIL_PATTERN.test(email.trim());
    },

    /**
     * Check if value is a valid phone number
     * @param {string} phone - Phone to validate
     * @returns {boolean}
     */
    isPhone(phone) {
        if (!phone) return false;
        const digits = phone.replace(/\D/g, '');
        return digits.length >= 10 && digits.length <= 11;
    },

    /**
     * Check if value is required (not empty)
     * @param {*} value - Value to check
     * @returns {boolean}
     */
    isRequired(value) {
        if (value === null || value === undefined) return false;
        if (typeof value === 'string') return value.trim().length > 0;
        if (Array.isArray(value)) return value.length > 0;
        return true;
    },

    /**
     * Check minimum length
     * @param {string} str - String to check
     * @param {number} min - Minimum length
     * @returns {boolean}
     */
    minLength(str, min) {
        if (!str) return min === 0;
        return str.length >= min;
    },

    /**
     * Check maximum length
     * @param {string} str - String to check
     * @param {number} max - Maximum length
     * @returns {boolean}
     */
    maxLength(str, max) {
        if (!str) return true;
        return str.length <= max;
    },

    /**
     * Check if value is within range
     * @param {number} value - Number to check
     * @param {number} min - Minimum value
     * @param {number} max - Maximum value
     * @returns {boolean}
     */
    inRange(value, min, max) {
        if (value === null || value === undefined || isNaN(value)) return false;
        return value >= min && value <= max;
    },

    /**
     * Check if value is a valid number
     * @param {*} value - Value to check
     * @returns {boolean}
     */
    isNumber(value) {
        if (value === null || value === undefined || value === '') return false;
        return !isNaN(parseFloat(value)) && isFinite(value);
    },

    /**
     * Check if value is a valid integer
     * @param {*} value - Value to check
     * @returns {boolean}
     */
    isInteger(value) {
        return this.isNumber(value) && Number.isInteger(parseFloat(value));
    },

    /**
     * Check if value is a valid date
     * @param {*} value - Value to check
     * @returns {boolean}
     */
    isDate(value) {
        if (!value) return false;
        const d = new Date(value);
        return !isNaN(d.getTime());
    },

    /**
     * Check if date is in the future
     * @param {Date|string} date - Date to check
     * @returns {boolean}
     */
    isFutureDate(date) {
        if (!this.isDate(date)) return false;
        const d = typeof date === 'string' ? new Date(date) : date;
        return d > new Date();
    },

    /**
     * Check if date is in the past
     * @param {Date|string} date - Date to check
     * @returns {boolean}
     */
    isPastDate(date) {
        if (!this.isDate(date)) return false;
        const d = typeof date === 'string' ? new Date(date) : date;
        return d < new Date();
    },

    /**
     * Check if value matches a regex pattern
     * @param {string} value - Value to check
     * @param {RegExp} pattern - Regex pattern
     * @returns {boolean}
     */
    matches(value, pattern) {
        if (!value) return false;
        return pattern.test(value);
    },

    /**
     * Check if value is a valid SSN
     * @param {string} ssn - SSN to validate
     * @returns {boolean}
     */
    isSSN(ssn) {
        if (!ssn) return false;
        const digits = ssn.replace(/\D/g, '');
        return digits.length === 9;
    },

    /**
     * Check if value is a valid ZIP code
     * @param {string} zip - ZIP to validate
     * @returns {boolean}
     */
    isZipCode(zip) {
        if (!zip) return false;
        const digits = zip.replace(/\D/g, '');
        return digits.length === 5 || digits.length === 9;
    },

    /**
     * Check if password meets strength requirements
     * @param {string} password - Password to check
     * @param {Object} [options] - Requirements
     * @returns {Object} { valid: boolean, errors: string[] }
     */
    passwordStrength(password, options = {}) {
        const {
            minLength = 8,
            requireUppercase = true,
            requireLowercase = true,
            requireNumber = true,
            requireSpecial = false
        } = options;

        const errors = [];

        if (!password || password.length < minLength) {
            errors.push(`Password must be at least ${minLength} characters`);
        }
        if (requireUppercase && !/[A-Z]/.test(password)) {
            errors.push('Password must contain an uppercase letter');
        }
        if (requireLowercase && !/[a-z]/.test(password)) {
            errors.push('Password must contain a lowercase letter');
        }
        if (requireNumber && !/\d/.test(password)) {
            errors.push('Password must contain a number');
        }
        if (requireSpecial && !/[!@#$%^&*(),.?":{}|<>]/.test(password)) {
            errors.push('Password must contain a special character');
        }

        return {
            valid: errors.length === 0,
            errors
        };
    },

    /**
     * Validate a form element against rules
     * @param {HTMLFormElement} form - Form element
     * @param {Object} rules - Validation rules by field name
     * @returns {Object} { valid: boolean, errors: Object }
     *
     * @example
     * ValidationUtils.validateForm(form, {
     *   email: { required: true, email: true },
     *   password: { required: true, minLength: 8 },
     *   age: { number: true, min: 18, max: 120 }
     * });
     */
    validateForm(form, rules) {
        const errors = {};
        let valid = true;

        Object.entries(rules).forEach(([fieldName, fieldRules]) => {
            const field = form.elements[fieldName];
            if (!field) return;

            const value = field.value;
            const fieldErrors = [];

            // Required
            if (fieldRules.required && !this.isRequired(value)) {
                fieldErrors.push(fieldRules.requiredMessage || 'This field is required');
            }

            // Only validate other rules if value exists
            if (value) {
                // Email
                if (fieldRules.email && !this.isEmail(value)) {
                    fieldErrors.push(fieldRules.emailMessage || 'Please enter a valid email');
                }

                // Phone
                if (fieldRules.phone && !this.isPhone(value)) {
                    fieldErrors.push(fieldRules.phoneMessage || 'Please enter a valid phone number');
                }

                // Min length
                if (fieldRules.minLength && !this.minLength(value, fieldRules.minLength)) {
                    fieldErrors.push(fieldRules.minLengthMessage || `Must be at least ${fieldRules.minLength} characters`);
                }

                // Max length
                if (fieldRules.maxLength && !this.maxLength(value, fieldRules.maxLength)) {
                    fieldErrors.push(fieldRules.maxLengthMessage || `Must be no more than ${fieldRules.maxLength} characters`);
                }

                // Number
                if (fieldRules.number && !this.isNumber(value)) {
                    fieldErrors.push(fieldRules.numberMessage || 'Please enter a valid number');
                }

                // Min value
                if (fieldRules.min !== undefined && parseFloat(value) < fieldRules.min) {
                    fieldErrors.push(fieldRules.minMessage || `Must be at least ${fieldRules.min}`);
                }

                // Max value
                if (fieldRules.max !== undefined && parseFloat(value) > fieldRules.max) {
                    fieldErrors.push(fieldRules.maxMessage || `Must be no more than ${fieldRules.max}`);
                }

                // Pattern
                if (fieldRules.pattern && !this.matches(value, fieldRules.pattern)) {
                    fieldErrors.push(fieldRules.patternMessage || 'Please enter a valid value');
                }

                // Custom validator
                if (fieldRules.custom && typeof fieldRules.custom === 'function') {
                    const customResult = fieldRules.custom(value, form);
                    if (customResult !== true) {
                        fieldErrors.push(customResult || 'Invalid value');
                    }
                }
            }

            if (fieldErrors.length > 0) {
                errors[fieldName] = fieldErrors;
                valid = false;

                // Add error styling
                field.classList.add('is-invalid');
            } else {
                field.classList.remove('is-invalid');
                field.classList.add('is-valid');
            }
        });

        return { valid, errors };
    },

    /**
     * Clear validation state from a form
     * @param {HTMLFormElement} form - Form element
     */
    clearValidation(form) {
        const fields = form.querySelectorAll('.is-invalid, .is-valid');
        fields.forEach(field => {
            field.classList.remove('is-invalid', 'is-valid');
        });

        const feedback = form.querySelectorAll('.invalid-feedback');
        feedback.forEach(el => el.remove());
    },

    /**
     * Show validation errors on a form
     * @param {HTMLFormElement} form - Form element
     * @param {Object} errors - Errors by field name
     */
    showErrors(form, errors) {
        Object.entries(errors).forEach(([fieldName, fieldErrors]) => {
            const field = form.elements[fieldName];
            if (!field) return;

            field.classList.add('is-invalid');

            // Remove existing feedback
            const existingFeedback = field.parentElement.querySelector('.invalid-feedback');
            if (existingFeedback) existingFeedback.remove();

            // Add new feedback
            const feedback = document.createElement('div');
            feedback.className = 'invalid-feedback';
            feedback.textContent = fieldErrors[0]; // Show first error
            field.parentElement.appendChild(feedback);
        });
    }
};

// Export for global access
window.ValidationUtils = ValidationUtils;
