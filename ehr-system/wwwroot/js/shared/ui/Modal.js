/**
 * Modal - Base modal class for creating dynamic modals
 * Provides common functionality for all modal dialogs
 *
 * Usage:
 *   const modal = new Modal('myModal');
 *   modal.show();
 *   modal.hide();
 *   modal.onHidden(() => { ... });
 */
class Modal {
    /**
     * Create a modal wrapper
     * @param {string|HTMLElement} elementOrId - Modal element or ID
     */
    constructor(elementOrId) {
        this.element = typeof elementOrId === 'string'
            ? document.getElementById(elementOrId)
            : elementOrId;

        this.bsModal = null;
        this.callbacks = {
            show: [],
            shown: [],
            hide: [],
            hidden: []
        };

        if (this.element) {
            this.initEvents();
        }
    }

    /**
     * Initialize Bootstrap modal events
     */
    initEvents() {
        this.element.addEventListener('show.bs.modal', (e) => {
            this.callbacks.show.forEach(cb => cb(e));
        });

        this.element.addEventListener('shown.bs.modal', (e) => {
            this.callbacks.shown.forEach(cb => cb(e));
        });

        this.element.addEventListener('hide.bs.modal', (e) => {
            this.callbacks.hide.forEach(cb => cb(e));
        });

        this.element.addEventListener('hidden.bs.modal', (e) => {
            this.callbacks.hidden.forEach(cb => cb(e));
        });
    }

    /**
     * Get or create Bootstrap modal instance
     * @returns {bootstrap.Modal}
     */
    getBootstrapModal() {
        if (!this.bsModal) {
            this.bsModal = new bootstrap.Modal(this.element);
        }
        return this.bsModal;
    }

    /**
     * Show the modal
     */
    show() {
        if (!this.element) return;
        this.getBootstrapModal().show();
    }

    /**
     * Hide the modal
     */
    hide() {
        if (!this.element) return;

        const instance = bootstrap.Modal.getInstance(this.element);
        if (instance) {
            instance.hide();
        }
    }

    /**
     * Toggle the modal
     */
    toggle() {
        if (!this.element) return;
        this.getBootstrapModal().toggle();
    }

    /**
     * Check if modal is visible
     * @returns {boolean}
     */
    isVisible() {
        return this.element?.classList.contains('show') || false;
    }

    /**
     * Register callback for when modal is about to show
     * @param {Function} callback
     * @returns {Modal} this (for chaining)
     */
    onShow(callback) {
        this.callbacks.show.push(callback);
        return this;
    }

    /**
     * Register callback for when modal is fully shown
     * @param {Function} callback
     * @returns {Modal} this (for chaining)
     */
    onShown(callback) {
        this.callbacks.shown.push(callback);
        return this;
    }

    /**
     * Register callback for when modal is about to hide
     * @param {Function} callback
     * @returns {Modal} this (for chaining)
     */
    onHide(callback) {
        this.callbacks.hide.push(callback);
        return this;
    }

    /**
     * Register callback for when modal is fully hidden
     * @param {Function} callback
     * @returns {Modal} this (for chaining)
     */
    onHidden(callback) {
        this.callbacks.hidden.push(callback);
        return this;
    }

    /**
     * Set the modal title
     * @param {string} title
     * @returns {Modal} this (for chaining)
     */
    setTitle(title) {
        const titleEl = this.element?.querySelector('.modal-title');
        if (titleEl) {
            titleEl.textContent = title;
        }
        return this;
    }

    /**
     * Set the modal body content
     * @param {string} html - HTML content
     * @returns {Modal} this (for chaining)
     */
    setBody(html) {
        const bodyEl = this.element?.querySelector('.modal-body');
        if (bodyEl) {
            bodyEl.innerHTML = html;
        }
        return this;
    }

    /**
     * Set the modal footer content
     * @param {string} html - HTML content
     * @returns {Modal} this (for chaining)
     */
    setFooter(html) {
        const footerEl = this.element?.querySelector('.modal-footer');
        if (footerEl) {
            footerEl.innerHTML = html;
        }
        return this;
    }

    /**
     * Focus an element within the modal
     * @param {string} selector - CSS selector
     */
    focus(selector) {
        const el = this.element?.querySelector(selector);
        if (el) {
            el.focus();
        }
    }

    /**
     * Get form data from the modal
     * @param {string} [formSelector='form'] - Form selector
     * @returns {Object} Form data as object
     */
    getFormData(formSelector = 'form') {
        const form = this.element?.querySelector(formSelector);
        if (!form) return {};

        const formData = new FormData(form);
        const data = {};

        formData.forEach((value, key) => {
            if (data[key]) {
                // Handle multiple values (checkboxes, multi-select)
                if (Array.isArray(data[key])) {
                    data[key].push(value);
                } else {
                    data[key] = [data[key], value];
                }
            } else {
                data[key] = value;
            }
        });

        return data;
    }

    /**
     * Reset a form within the modal
     * @param {string} [formSelector='form'] - Form selector
     */
    resetForm(formSelector = 'form') {
        const form = this.element?.querySelector(formSelector);
        if (form) {
            form.reset();
            ValidationUtils.clearValidation(form);
        }
    }

    /**
     * Set form values
     * @param {Object} data - Data object with field names as keys
     * @param {string} [formSelector='form'] - Form selector
     */
    setFormData(data, formSelector = 'form') {
        const form = this.element?.querySelector(formSelector);
        if (!form || !data) return;

        Object.entries(data).forEach(([key, value]) => {
            const field = form.elements[key];
            if (!field) return;

            if (field.type === 'checkbox') {
                field.checked = Boolean(value);
            } else if (field.type === 'radio') {
                const radio = form.querySelector(`input[name="${key}"][value="${value}"]`);
                if (radio) radio.checked = true;
            } else {
                field.value = value ?? '';
            }
        });
    }

    /**
     * Dispose of the modal
     */
    dispose() {
        if (this.bsModal) {
            this.bsModal.dispose();
            this.bsModal = null;
        }
        this.callbacks = { show: [], shown: [], hide: [], hidden: [] };
    }

    /**
     * Static helper to get or create a Modal wrapper
     * @param {string} id - Modal ID
     * @returns {Modal}
     */
    static get(id) {
        return new Modal(id);
    }
}

// Export for global access
window.Modal = Modal;
