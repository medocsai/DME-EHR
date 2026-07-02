/**
 * DomUtils - DOM manipulation utilities
 * Common DOM operations used throughout the application
 *
 * Usage:
 *   DomUtils.debounce(fn, 300);
 *   DomUtils.on('#myBtn', 'click', handler);
 *   DomUtils.show('#myElement');
 */
const DomUtils = {
    /**
     * Create a debounced function
     * @param {Function} func - Function to debounce
     * @param {number} wait - Milliseconds to wait
     * @returns {Function} Debounced function
     */
    debounce(func, wait) {
        let timeout;
        return function executedFunction(...args) {
            const later = () => {
                clearTimeout(timeout);
                func.apply(this, args);
            };
            clearTimeout(timeout);
            timeout = setTimeout(later, wait);
        };
    },

    /**
     * Create a throttled function
     * @param {Function} func - Function to throttle
     * @param {number} limit - Milliseconds between calls
     * @returns {Function} Throttled function
     */
    throttle(func, limit) {
        let inThrottle;
        return function(...args) {
            if (!inThrottle) {
                func.apply(this, args);
                inThrottle = true;
                setTimeout(() => inThrottle = false, limit);
            }
        };
    },

    /**
     * Add event listener (supports selector string)
     * @param {string|HTMLElement} target - Element or selector
     * @param {string} event - Event type
     * @param {Function} handler - Event handler
     * @param {Object} [options] - AddEventListener options
     */
    on(target, event, handler, options = {}) {
        const elements = typeof target === 'string'
            ? document.querySelectorAll(target)
            : [target];

        elements.forEach(el => {
            if (el) el.addEventListener(event, handler, options);
        });
    },

    /**
     * Remove event listener
     * @param {string|HTMLElement} target - Element or selector
     * @param {string} event - Event type
     * @param {Function} handler - Event handler
     */
    off(target, event, handler) {
        const elements = typeof target === 'string'
            ? document.querySelectorAll(target)
            : [target];

        elements.forEach(el => {
            if (el) el.removeEventListener(event, handler);
        });
    },

    /**
     * Event delegation
     * @param {string|HTMLElement} container - Container element
     * @param {string} selector - Child selector
     * @param {string} event - Event type
     * @param {Function} handler - Event handler
     */
    delegate(container, selector, event, handler) {
        const containerEl = typeof container === 'string'
            ? document.querySelector(container)
            : container;

        if (!containerEl) return;

        containerEl.addEventListener(event, (e) => {
            const target = e.target.closest(selector);
            if (target && containerEl.contains(target)) {
                handler.call(target, e, target);
            }
        });
    },

    /**
     * Show an element
     * @param {string|HTMLElement} target - Element or selector
     */
    show(target) {
        const el = typeof target === 'string' ? document.querySelector(target) : target;
        if (el) el.classList.remove('d-none');
    },

    /**
     * Hide an element
     * @param {string|HTMLElement} target - Element or selector
     */
    hide(target) {
        const el = typeof target === 'string' ? document.querySelector(target) : target;
        if (el) el.classList.add('d-none');
    },

    /**
     * Toggle element visibility
     * @param {string|HTMLElement} target - Element or selector
     * @param {boolean} [visible] - Force visible state
     */
    toggle(target, visible) {
        const el = typeof target === 'string' ? document.querySelector(target) : target;
        if (!el) return;

        if (visible === undefined) {
            el.classList.toggle('d-none');
        } else {
            el.classList.toggle('d-none', !visible);
        }
    },

    /**
     * Check if element is visible
     * @param {string|HTMLElement} target - Element or selector
     * @returns {boolean}
     */
    isVisible(target) {
        const el = typeof target === 'string' ? document.querySelector(target) : target;
        return el && !el.classList.contains('d-none') && el.offsetParent !== null;
    },

    /**
     * Set HTML content
     * @param {string|HTMLElement} target - Element or selector
     * @param {string} html - HTML content
     */
    html(target, html) {
        const el = typeof target === 'string' ? document.querySelector(target) : target;
        if (el) el.innerHTML = html;
    },

    /**
     * Set text content
     * @param {string|HTMLElement} target - Element or selector
     * @param {string} text - Text content
     */
    text(target, text) {
        const el = typeof target === 'string' ? document.querySelector(target) : target;
        if (el) el.textContent = text;
    },

    /**
     * Add class
     * @param {string|HTMLElement} target - Element or selector
     * @param {...string} classes - Classes to add
     */
    addClass(target, ...classes) {
        const el = typeof target === 'string' ? document.querySelector(target) : target;
        if (el) el.classList.add(...classes);
    },

    /**
     * Remove class
     * @param {string|HTMLElement} target - Element or selector
     * @param {...string} classes - Classes to remove
     */
    removeClass(target, ...classes) {
        const el = typeof target === 'string' ? document.querySelector(target) : target;
        if (el) el.classList.remove(...classes);
    },

    /**
     * Toggle class
     * @param {string|HTMLElement} target - Element or selector
     * @param {string} className - Class to toggle
     * @param {boolean} [force] - Force state
     */
    toggleClass(target, className, force) {
        const el = typeof target === 'string' ? document.querySelector(target) : target;
        if (el) el.classList.toggle(className, force);
    },

    /**
     * Check if element has class
     * @param {string|HTMLElement} target - Element or selector
     * @param {string} className - Class to check
     * @returns {boolean}
     */
    hasClass(target, className) {
        const el = typeof target === 'string' ? document.querySelector(target) : target;
        return el ? el.classList.contains(className) : false;
    },

    /**
     * Set attribute
     * @param {string|HTMLElement} target - Element or selector
     * @param {string} name - Attribute name
     * @param {string} value - Attribute value
     */
    attr(target, name, value) {
        const el = typeof target === 'string' ? document.querySelector(target) : target;
        if (el) el.setAttribute(name, value);
    },

    /**
     * Get attribute
     * @param {string|HTMLElement} target - Element or selector
     * @param {string} name - Attribute name
     * @returns {string|null}
     */
    getAttr(target, name) {
        const el = typeof target === 'string' ? document.querySelector(target) : target;
        return el ? el.getAttribute(name) : null;
    },

    /**
     * Remove attribute
     * @param {string|HTMLElement} target - Element or selector
     * @param {string} name - Attribute name
     */
    removeAttr(target, name) {
        const el = typeof target === 'string' ? document.querySelector(target) : target;
        if (el) el.removeAttribute(name);
    },

    /**
     * Set data attribute
     * @param {string|HTMLElement} target - Element or selector
     * @param {string} key - Data key (without 'data-' prefix)
     * @param {*} value - Value
     */
    setData(target, key, value) {
        const el = typeof target === 'string' ? document.querySelector(target) : target;
        if (el) el.dataset[key] = value;
    },

    /**
     * Get data attribute
     * @param {string|HTMLElement} target - Element or selector
     * @param {string} key - Data key
     * @returns {string|undefined}
     */
    getData(target, key) {
        const el = typeof target === 'string' ? document.querySelector(target) : target;
        return el ? el.dataset[key] : undefined;
    },

    /**
     * Find element
     * @param {string} selector - CSS selector
     * @param {HTMLElement} [context=document] - Context element
     * @returns {HTMLElement|null}
     */
    find(selector, context = document) {
        return context.querySelector(selector);
    },

    /**
     * Find all elements
     * @param {string} selector - CSS selector
     * @param {HTMLElement} [context=document] - Context element
     * @returns {NodeList}
     */
    findAll(selector, context = document) {
        return context.querySelectorAll(selector);
    },

    /**
     * Create element from HTML string
     * @param {string} html - HTML string
     * @returns {HTMLElement}
     */
    create(html) {
        const template = document.createElement('template');
        template.innerHTML = html.trim();
        return template.content.firstChild;
    },

    /**
     * Remove element
     * @param {string|HTMLElement} target - Element or selector
     */
    remove(target) {
        const el = typeof target === 'string' ? document.querySelector(target) : target;
        if (el && el.parentNode) el.parentNode.removeChild(el);
    },

    /**
     * Empty element (remove all children)
     * @param {string|HTMLElement} target - Element or selector
     */
    empty(target) {
        const el = typeof target === 'string' ? document.querySelector(target) : target;
        if (el) el.innerHTML = '';
    },

    /**
     * Check if element exists
     * @param {string} selector - CSS selector
     * @returns {boolean}
     */
    exists(selector) {
        return document.querySelector(selector) !== null;
    },

    /**
     * Scroll element into view
     * @param {string|HTMLElement} target - Element or selector
     * @param {Object} [options] - ScrollIntoView options
     */
    scrollTo(target, options = { behavior: 'smooth', block: 'start' }) {
        const el = typeof target === 'string' ? document.querySelector(target) : target;
        if (el) el.scrollIntoView(options);
    },

    /**
     * Focus element
     * @param {string|HTMLElement} target - Element or selector
     */
    focus(target) {
        const el = typeof target === 'string' ? document.querySelector(target) : target;
        if (el) el.focus();
    },

    /**
     * Wait for DOM ready
     * @param {Function} callback - Callback function
     */
    ready(callback) {
        if (document.readyState !== 'loading') {
            callback();
        } else {
            document.addEventListener('DOMContentLoaded', callback);
        }
    }
};

// Export for global access
window.DomUtils = DomUtils;

// Backward compatibility
window.debounce = DomUtils.debounce;
