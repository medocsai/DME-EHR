/**
 * StateManager - Global application state management
 * Provides centralized state with change notifications
 *
 * Usage:
 *   App.state.set('currentUser', userData);
 *   const user = App.state.get('currentUser');
 *   App.state.subscribe('currentUser', (newValue, oldValue) => { ... });
 */
class StateManager {
    constructor(eventBus) {
        this.state = new Map();
        this.eventBus = eventBus;
        this.persistKeys = new Set(); // Keys to persist to localStorage
    }

    /**
     * Get a state value
     * @param {string} key - State key
     * @param {*} [defaultValue] - Default value if key doesn't exist
     * @returns {*} State value
     */
    get(key, defaultValue = null) {
        if (this.state.has(key)) {
            return this.state.get(key);
        }
        return defaultValue;
    }

    /**
     * Set a state value
     * @param {string} key - State key
     * @param {*} value - Value to set
     * @param {boolean} [silent=false] - If true, don't emit change event
     */
    set(key, value, silent = false) {
        const oldValue = this.state.get(key);
        this.state.set(key, value);

        // Persist to localStorage if configured
        if (this.persistKeys.has(key)) {
            this.persistToStorage(key, value);
        }

        // Emit change event
        if (!silent && this.eventBus) {
            this.eventBus.emit(`state:${key}:changed`, { key, value, oldValue });
            this.eventBus.emit('state:changed', { key, value, oldValue });
        }
    }

    /**
     * Delete a state value
     * @param {string} key - State key
     */
    delete(key) {
        const oldValue = this.state.get(key);
        this.state.delete(key);

        // Remove from localStorage
        if (this.persistKeys.has(key)) {
            localStorage.removeItem(`state_${key}`);
        }

        if (this.eventBus) {
            this.eventBus.emit(`state:${key}:changed`, { key, value: null, oldValue });
        }
    }

    /**
     * Check if a state key exists
     * @param {string} key - State key
     * @returns {boolean}
     */
    has(key) {
        return this.state.has(key);
    }

    /**
     * Subscribe to changes on a specific state key
     * @param {string} key - State key to watch
     * @param {Function} callback - Function to call on change (newValue, oldValue)
     * @returns {Function} Unsubscribe function
     */
    subscribe(key, callback) {
        if (!this.eventBus) {
            console.error('StateManager: No EventBus configured');
            return () => {};
        }
        return this.eventBus.on(`state:${key}:changed`, ({ value, oldValue }) => {
            callback(value, oldValue);
        });
    }

    /**
     * Subscribe to any state change
     * @param {Function} callback - Function to call on any change
     * @returns {Function} Unsubscribe function
     */
    subscribeAll(callback) {
        if (!this.eventBus) {
            return () => {};
        }
        return this.eventBus.on('state:changed', callback);
    }

    /**
     * Configure a key to be persisted to localStorage
     * @param {string} key - State key
     * @param {boolean} [loadExisting=true] - Load existing value from storage
     */
    persist(key, loadExisting = true) {
        this.persistKeys.add(key);

        if (loadExisting) {
            const stored = localStorage.getItem(`state_${key}`);
            if (stored) {
                try {
                    this.state.set(key, JSON.parse(stored));
                } catch {
                    this.state.set(key, stored);
                }
            }
        }
    }

    /**
     * Persist a value to localStorage
     * @param {string} key - State key
     * @param {*} value - Value to persist
     */
    persistToStorage(key, value) {
        if (value === null || value === undefined) {
            localStorage.removeItem(`state_${key}`);
        } else {
            localStorage.setItem(`state_${key}`, JSON.stringify(value));
        }
    }

    /**
     * Clear all state
     * @param {boolean} [clearStorage=false] - Also clear persisted values
     */
    clear(clearStorage = false) {
        this.state.clear();

        if (clearStorage) {
            this.persistKeys.forEach(key => {
                localStorage.removeItem(`state_${key}`);
            });
        }
    }

    /**
     * Get all state as a plain object (useful for debugging)
     * @returns {Object}
     */
    toObject() {
        const obj = {};
        this.state.forEach((value, key) => {
            obj[key] = value;
        });
        return obj;
    }

    /**
     * Initialize state from an object
     * @param {Object} initialState - Initial state values
     * @param {boolean} [silent=true] - Don't emit events during initialization
     */
    initialize(initialState, silent = true) {
        Object.entries(initialState).forEach(([key, value]) => {
            this.set(key, value, silent);
        });
    }
}

// Export class
window.StateManager = StateManager;
