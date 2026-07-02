/**
 * FormHandler - Form handling utilities
 * Provides consistent form serialization, validation, and submission
 *
 * Usage:
 *   const handler = new FormHandler('patientForm', {
 *     onSubmit: async (data) => await savePatient(data),
 *     validation: { ... }
 *   });
 */
class FormHandler {
    /**
     * Create a form handler
     * @param {string|HTMLFormElement} formOrId - Form element or ID
     * @param {Object} options - Configuration options
     */
    constructor(formOrId, options = {}) {
        this.form = typeof formOrId === 'string'
            ? document.getElementById(formOrId)
            : formOrId;

        this.options = {
            validation: {},
            onSubmit: null,
            onSuccess: null,
            onError: null,
            resetOnSuccess: false,
            showLoadingOnSubmit: true,
            preventDoubleSubmit: true,
            submitButton: null,
            ...options
        };

        this.isSubmitting = false;
        this.originalButtonText = '';

        if (this.form) {
            this.init();
        }
    }

    /**
     * Initialize form handling
     */
    init() {
        // Find submit button
        if (!this.options.submitButton) {
            this.options.submitButton = this.form.querySelector('[type="submit"]');
        }

        // Bind submit event
        this.form.addEventListener('submit', (e) => this.handleSubmit(e));

        // Bind validation on blur for better UX
        Object.keys(this.options.validation).forEach(fieldName => {
            const field = this.form.elements[fieldName];
            if (field) {
                field.addEventListener('blur', () => this.validateField(fieldName));
            }
        });
    }

    /**
     * Handle form submission
     * @param {Event} e - Submit event
     */
    async handleSubmit(e) {
        e.preventDefault();

        // Prevent double submit
        if (this.isSubmitting && this.options.preventDoubleSubmit) {
            return;
        }

        // Validate
        const { valid, errors } = this.validate();
        if (!valid) {
            this.showErrors(errors);
            return;
        }

        // Get form data
        const data = this.getData();

        // Set submitting state
        this.setSubmitting(true);

        try {
            if (this.options.onSubmit) {
                const result = await this.options.onSubmit(data);

                if (this.options.onSuccess) {
                    this.options.onSuccess(result);
                }

                if (this.options.resetOnSuccess) {
                    this.reset();
                }

                return result;
            }
        } catch (error) {
            console.error('Form submission error:', error);

            if (this.options.onError) {
                this.options.onError(error);
            }

            throw error;
        } finally {
            this.setSubmitting(false);
        }
    }

    /**
     * Set submitting state
     * @param {boolean} submitting - Is submitting
     */
    setSubmitting(submitting) {
        this.isSubmitting = submitting;

        if (this.options.submitButton) {
            if (submitting) {
                this.originalButtonText = this.options.submitButton.innerHTML;
                this.options.submitButton.innerHTML = `
                    <span class="spinner-border spinner-border-sm me-2"></span>
                    Saving...
                `;
                this.options.submitButton.disabled = true;
            } else {
                this.options.submitButton.innerHTML = this.originalButtonText;
                this.options.submitButton.disabled = false;
            }
        }

        if (this.options.showLoadingOnSubmit && window.LoadingState) {
            submitting ? LoadingState.show() : LoadingState.hide();
        }
    }

    /**
     * Validate the form
     * @returns {Object} { valid: boolean, errors: Object }
     */
    validate() {
        if (Object.keys(this.options.validation).length === 0) {
            return { valid: true, errors: {} };
        }

        return ValidationUtils.validateForm(this.form, this.options.validation);
    }

    /**
     * Validate a single field
     * @param {string} fieldName - Field name
     * @returns {boolean} Is valid
     */
    validateField(fieldName) {
        const rules = this.options.validation[fieldName];
        if (!rules) return true;

        const { valid, errors } = ValidationUtils.validateForm(this.form, { [fieldName]: rules });

        if (!valid && errors[fieldName]) {
            this.showFieldError(fieldName, errors[fieldName][0]);
        } else {
            this.clearFieldError(fieldName);
        }

        return valid;
    }

    /**
     * Show validation errors
     * @param {Object} errors - Errors object
     */
    showErrors(errors) {
        ValidationUtils.showErrors(this.form, errors);

        // Focus first error field
        const firstErrorField = Object.keys(errors)[0];
        if (firstErrorField) {
            this.form.elements[firstErrorField]?.focus();
        }
    }

    /**
     * Show error for a single field
     * @param {string} fieldName - Field name
     * @param {string} message - Error message
     */
    showFieldError(fieldName, message) {
        const field = this.form.elements[fieldName];
        if (!field) return;

        field.classList.add('is-invalid');
        field.classList.remove('is-valid');

        // Remove existing feedback
        const existing = field.parentElement.querySelector('.invalid-feedback');
        if (existing) existing.remove();

        // Add new feedback
        const feedback = document.createElement('div');
        feedback.className = 'invalid-feedback';
        feedback.textContent = message;
        field.parentElement.appendChild(feedback);
    }

    /**
     * Clear error for a single field
     * @param {string} fieldName - Field name
     */
    clearFieldError(fieldName) {
        const field = this.form.elements[fieldName];
        if (!field) return;

        field.classList.remove('is-invalid');
        field.classList.add('is-valid');

        const feedback = field.parentElement.querySelector('.invalid-feedback');
        if (feedback) feedback.remove();
    }

    /**
     * Clear all validation state
     */
    clearValidation() {
        ValidationUtils.clearValidation(this.form);
    }

    /**
     * Get form data as object
     * @returns {Object}
     */
    getData() {
        const formData = new FormData(this.form);
        const data = {};

        formData.forEach((value, key) => {
            // Handle array fields (checkboxes, multi-select)
            if (key.endsWith('[]')) {
                const realKey = key.slice(0, -2);
                if (!data[realKey]) data[realKey] = [];
                data[realKey].push(value);
            } else if (data.hasOwnProperty(key)) {
                // Multiple values for same key
                if (!Array.isArray(data[key])) {
                    data[key] = [data[key]];
                }
                data[key].push(value);
            } else {
                data[key] = value;
            }
        });

        // Handle checkboxes that aren't checked (they won't be in FormData)
        this.form.querySelectorAll('input[type="checkbox"]').forEach(checkbox => {
            if (!checkbox.name.endsWith('[]') && !formData.has(checkbox.name)) {
                data[checkbox.name] = false;
            } else if (data[checkbox.name] === 'on') {
                data[checkbox.name] = true;
            }
        });

        return data;
    }

    /**
     * Set form data from object
     * @param {Object} data - Data object
     */
    setData(data) {
        if (!data) return;

        Object.entries(data).forEach(([key, value]) => {
            const field = this.form.elements[key];
            if (!field) return;

            if (field.type === 'checkbox') {
                field.checked = Boolean(value);
            } else if (field.type === 'radio') {
                const radio = this.form.querySelector(`input[name="${key}"][value="${value}"]`);
                if (radio) radio.checked = true;
            } else if (field.tagName === 'SELECT' && field.multiple) {
                Array.from(field.options).forEach(opt => {
                    opt.selected = Array.isArray(value) ? value.includes(opt.value) : opt.value === value;
                });
            } else {
                field.value = value ?? '';
            }
        });
    }

    /**
     * Get a single field value
     * @param {string} fieldName - Field name
     * @returns {*}
     */
    getValue(fieldName) {
        const field = this.form.elements[fieldName];
        if (!field) return null;

        if (field.type === 'checkbox') {
            return field.checked;
        }
        return field.value;
    }

    /**
     * Set a single field value
     * @param {string} fieldName - Field name
     * @param {*} value - Value
     */
    setValue(fieldName, value) {
        const field = this.form.elements[fieldName];
        if (!field) return;

        if (field.type === 'checkbox') {
            field.checked = Boolean(value);
        } else {
            field.value = value ?? '';
        }
    }

    /**
     * Reset the form
     */
    reset() {
        this.form.reset();
        this.clearValidation();
    }

    /**
     * Enable/disable the form
     * @param {boolean} enabled - Is enabled
     */
    setEnabled(enabled) {
        const elements = this.form.elements;
        for (let i = 0; i < elements.length; i++) {
            elements[i].disabled = !enabled;
        }
    }

    /**
     * Check if form has unsaved changes
     * @param {Object} originalData - Original data to compare against
     * @returns {boolean}
     */
    hasChanges(originalData) {
        const currentData = this.getData();
        return JSON.stringify(currentData) !== JSON.stringify(originalData);
    }

    /**
     * Submit the form programmatically
     * @returns {Promise}
     */
    submit() {
        return this.handleSubmit(new Event('submit'));
    }

    /**
     * Destroy the handler
     */
    destroy() {
        // Note: Event listeners are bound to the form element
        // They will be garbage collected when the form is removed
        this.form = null;
    }
}

// Export for global access
window.FormHandler = FormHandler;
