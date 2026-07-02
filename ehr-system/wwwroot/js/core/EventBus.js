/**
 * EventBus - Pub/Sub system for cross-module communication
 * Allows modules to communicate without direct dependencies
 *
 * Usage:
 *   App.events.on('patient:selected', (patient) => { ... });
 *   App.events.emit('patient:selected', patientData);
 *   const unsubscribe = App.events.on('event', handler);
 *   unsubscribe(); // Remove listener
 */
class EventBus {
    constructor() {
        this.listeners = new Map();
        this.onceListeners = new Map();
    }

    /**
     * Subscribe to an event
     * @param {string} event - Event name (e.g., 'patient:selected', 'location:changed')
     * @param {Function} callback - Function to call when event is emitted
     * @returns {Function} Unsubscribe function
     */
    on(event, callback) {
        if (typeof callback !== 'function') {
            console.error('EventBus.on: callback must be a function');
            return () => {};
        }

        if (!this.listeners.has(event)) {
            this.listeners.set(event, new Set());
        }
        this.listeners.get(event).add(callback);

        // Return unsubscribe function
        return () => this.off(event, callback);
    }

    /**
     * Subscribe to an event once (auto-removes after first trigger)
     * @param {string} event - Event name
     * @param {Function} callback - Function to call when event is emitted
     * @returns {Function} Unsubscribe function
     */
    once(event, callback) {
        if (typeof callback !== 'function') {
            console.error('EventBus.once: callback must be a function');
            return () => {};
        }

        if (!this.onceListeners.has(event)) {
            this.onceListeners.set(event, new Set());
        }
        this.onceListeners.get(event).add(callback);

        return () => {
            const listeners = this.onceListeners.get(event);
            if (listeners) {
                listeners.delete(callback);
            }
        };
    }

    /**
     * Unsubscribe from an event
     * @param {string} event - Event name
     * @param {Function} callback - The callback to remove
     */
    off(event, callback) {
        const listeners = this.listeners.get(event);
        if (listeners) {
            listeners.delete(callback);
            if (listeners.size === 0) {
                this.listeners.delete(event);
            }
        }
    }

    /**
     * Emit an event to all subscribers
     * @param {string} event - Event name
     * @param {*} data - Data to pass to subscribers
     */
    emit(event, data) {
        // Regular listeners
        const listeners = this.listeners.get(event);
        if (listeners) {
            listeners.forEach(callback => {
                try {
                    callback(data);
                } catch (error) {
                    console.error(`EventBus: Error in listener for "${event}":`, error);
                }
            });
        }

        // Once listeners (remove after calling)
        const onceListeners = this.onceListeners.get(event);
        if (onceListeners) {
            onceListeners.forEach(callback => {
                try {
                    callback(data);
                } catch (error) {
                    console.error(`EventBus: Error in once listener for "${event}":`, error);
                }
            });
            this.onceListeners.delete(event);
        }
    }

    /**
     * Remove all listeners for an event, or all listeners if no event specified
     * @param {string} [event] - Optional event name
     */
    clear(event) {
        if (event) {
            this.listeners.delete(event);
            this.onceListeners.delete(event);
        } else {
            this.listeners.clear();
            this.onceListeners.clear();
        }
    }

    /**
     * Get the count of listeners for an event
     * @param {string} event - Event name
     * @returns {number} Number of listeners
     */
    listenerCount(event) {
        const regular = this.listeners.get(event)?.size || 0;
        const once = this.onceListeners.get(event)?.size || 0;
        return regular + once;
    }
}

// Export singleton instance
window.EventBus = EventBus;
