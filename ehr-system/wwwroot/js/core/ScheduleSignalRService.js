/**
 * ScheduleSignalRService - Real-time schedule updates via SignalR
 *
 * Connects to the ScheduleNotificationHub to receive real-time appointment
 * change notifications. Triggers refreshes on dashboard and schedule views.
 *
 * @example
 *   const service = new ScheduleSignalRService();
 *   await service.connect();
 *   service.onAppointmentChanged((notification) => {
 *       console.log('Appointment changed:', notification);
 *   });
 */
class ScheduleSignalRService {
    constructor() {
        this._connection = null;
        this._isConnected = false;
        this._reconnectAttempts = 0;
        this._maxReconnectAttempts = 10;
        this._reconnectDelay = 2000; // Start with 2 seconds
        this._callbacks = [];
        this._clinicalNoteCallbacks = [];
        this._authorizationCallbacks = [];
        this._locationId = null;

        // Bind methods
        this._handleAppointmentChanged = this._handleAppointmentChanged.bind(this);
        this._handleClinicalNoteChanged = this._handleClinicalNoteChanged.bind(this);
        this._handleAuthorizationChanged = this._handleAuthorizationChanged.bind(this);
    }

    /**
     * Connect to the SignalR hub
     * @returns {Promise<boolean>} - True if connected successfully
     */
    async connect() {
        if (this._isConnected) {
            console.log('[ScheduleSignalR] Already connected');
            return true;
        }

        // Check if SignalR is available
        if (typeof signalR === 'undefined') {
            console.warn('[ScheduleSignalR] SignalR library not loaded');
            return false;
        }

        // Get the auth token (try multiple possible keys)
        const token = localStorage.getItem('authToken') ||
                      localStorage.getItem('auth_token') ||
                      sessionStorage.getItem('token');
        if (!token) {
            console.warn('[ScheduleSignalR] No auth token available');
            return false;
        }

        try {
            // Build the connection
            this._connection = new signalR.HubConnectionBuilder()
                .withUrl('/hubs/schedule', {
                    accessTokenFactory: () => token
                })
                .withAutomaticReconnect({
                    nextRetryDelayInMilliseconds: (retryContext) => {
                        // Exponential backoff: 2s, 4s, 8s, 16s, 32s (max)
                        const delay = Math.min(
                            this._reconnectDelay * Math.pow(2, retryContext.previousRetryCount),
                            32000
                        );
                        console.log(`[ScheduleSignalR] Reconnecting in ${delay}ms (attempt ${retryContext.previousRetryCount + 1})`);
                        return delay;
                    }
                })
                .configureLogging(signalR.LogLevel.Information)
                .build();

            // Set up event handlers
            this._connection.on('AppointmentChanged', this._handleAppointmentChanged);
            this._connection.on('ClinicalNoteChanged', this._handleClinicalNoteChanged);
            this._connection.on('AuthorizationChanged', this._handleAuthorizationChanged);

            // Connection state change handlers
            this._connection.onreconnecting((error) => {
                console.log('[ScheduleSignalR] Reconnecting...', error?.message);
                this._isConnected = false;
            });

            this._connection.onreconnected((connectionId) => {
                console.log('[ScheduleSignalR] Reconnected:', connectionId);
                this._isConnected = true;
                this._reconnectAttempts = 0;

                // Rejoin location group if we had one
                if (this._locationId) {
                    this.joinLocationGroup(this._locationId);
                }
            });

            this._connection.onclose((error) => {
                console.log('[ScheduleSignalR] Connection closed:', error?.message);
                this._isConnected = false;

                // Attempt manual reconnect if automatic reconnect fails
                if (this._reconnectAttempts < this._maxReconnectAttempts) {
                    this._reconnectAttempts++;
                    const delay = Math.min(this._reconnectDelay * Math.pow(2, this._reconnectAttempts), 32000);
                    setTimeout(() => this.connect(), delay);
                }
            });

            // Start the connection
            await this._connection.start();
            this._isConnected = true;
            this._reconnectAttempts = 0;

            console.log('[ScheduleSignalR] Connected successfully');
            return true;
        } catch (error) {
            console.error('[ScheduleSignalR] Connection failed:', error);
            this._isConnected = false;

            // Retry connection
            if (this._reconnectAttempts < this._maxReconnectAttempts) {
                this._reconnectAttempts++;
                const delay = Math.min(this._reconnectDelay * Math.pow(2, this._reconnectAttempts), 32000);
                console.log(`[ScheduleSignalR] Retrying in ${delay}ms`);
                setTimeout(() => this.connect(), delay);
            }

            return false;
        }
    }

    /**
     * Disconnect from the SignalR hub
     */
    async disconnect() {
        if (this._connection && this._isConnected) {
            try {
                await this._connection.stop();
                console.log('[ScheduleSignalR] Disconnected');
            } catch (error) {
                console.error('[ScheduleSignalR] Error disconnecting:', error);
            }
        }
        this._isConnected = false;
        this._connection = null;
    }

    /**
     * Join a location-specific group for targeted notifications
     * @param {number} locationId - The location ID to join
     */
    async joinLocationGroup(locationId) {
        if (!this._isConnected || !this._connection) {
            console.warn('[ScheduleSignalR] Not connected, cannot join location group');
            this._locationId = locationId; // Store for later when we connect
            return;
        }

        try {
            await this._connection.invoke('JoinLocationGroup', locationId);
            this._locationId = locationId;
            console.log(`[ScheduleSignalR] Joined location group: ${locationId}`);
        } catch (error) {
            console.error('[ScheduleSignalR] Error joining location group:', error);
        }
    }

    /**
     * Leave a location-specific group
     * @param {number} locationId - The location ID to leave
     */
    async leaveLocationGroup(locationId) {
        if (!this._isConnected || !this._connection) {
            return;
        }

        try {
            await this._connection.invoke('LeaveLocationGroup', locationId);
            if (this._locationId === locationId) {
                this._locationId = null;
            }
            console.log(`[ScheduleSignalR] Left location group: ${locationId}`);
        } catch (error) {
            console.error('[ScheduleSignalR] Error leaving location group:', error);
        }
    }

    /**
     * Register a callback for appointment change notifications
     * @param {Function} callback - Function to call when appointment changes
     * @returns {Function} - Unsubscribe function
     */
    onAppointmentChanged(callback) {
        if (typeof callback !== 'function') {
            console.warn('[ScheduleSignalR] Invalid callback');
            return () => {};
        }

        this._callbacks.push(callback);

        // Return unsubscribe function
        return () => {
            const index = this._callbacks.indexOf(callback);
            if (index > -1) {
                this._callbacks.splice(index, 1);
            }
        };
    }

    /**
     * Handle appointment changed notification from SignalR
     * @private
     */
    _handleAppointmentChanged(notification) {
        console.log('[ScheduleSignalR] Appointment changed:', notification);

        // Notify all registered callbacks
        this._callbacks.forEach(callback => {
            try {
                callback(notification);
            } catch (error) {
                console.error('[ScheduleSignalR] Callback error:', error);
            }
        });

        // Emit event for modules listening on the window
        if (window.App?.eventBus) {
            window.App.eventBus.emit('signalr:appointmentChanged', notification);
        }

        // Also dispatch a custom DOM event for broader compatibility
        const event = new CustomEvent('appointmentChanged', { detail: notification });
        document.dispatchEvent(event);
    }

    /**
     * Handle clinical note changed notification from SignalR
     * @private
     */
    _handleClinicalNoteChanged(notification) {
        console.log('[ScheduleSignalR] Clinical note changed:', notification);

        // Notify all registered callbacks
        this._clinicalNoteCallbacks.forEach(callback => {
            try {
                callback(notification);
            } catch (error) {
                console.error('[ScheduleSignalR] Clinical note callback error:', error);
            }
        });

        // Emit event for modules listening on the window
        if (window.App?.eventBus) {
            window.App.eventBus.emit('signalr:clinicalNoteChanged', notification);
        }

        // Also dispatch a custom DOM event for broader compatibility
        const event = new CustomEvent('clinicalNoteChanged', { detail: notification });
        document.dispatchEvent(event);
    }

    /**
     * Handle authorization changed notification from SignalR
     * @private
     */
    _handleAuthorizationChanged(notification) {
        console.log('[ScheduleSignalR] Authorization changed:', notification);

        // Notify all registered callbacks
        this._authorizationCallbacks.forEach(callback => {
            try {
                callback(notification);
            } catch (error) {
                console.error('[ScheduleSignalR] Authorization callback error:', error);
            }
        });

        // Emit event for modules listening on the window
        if (window.App?.eventBus) {
            window.App.eventBus.emit('signalr:authorizationChanged', notification);
        }

        // Also dispatch a custom DOM event for broader compatibility
        const event = new CustomEvent('authorizationChanged', { detail: notification });
        document.dispatchEvent(event);
    }

    /**
     * Register a callback for clinical note change notifications
     * @param {Function} callback - Function to call when clinical note changes
     * @returns {Function} - Unsubscribe function
     */
    onClinicalNoteChanged(callback) {
        if (typeof callback !== 'function') {
            console.warn('[ScheduleSignalR] Invalid callback');
            return () => {};
        }

        this._clinicalNoteCallbacks.push(callback);

        // Return unsubscribe function
        return () => {
            const index = this._clinicalNoteCallbacks.indexOf(callback);
            if (index > -1) {
                this._clinicalNoteCallbacks.splice(index, 1);
            }
        };
    }

    /**
     * Register a callback for authorization change notifications
     * @param {Function} callback - Function to call when authorization changes
     * @returns {Function} - Unsubscribe function
     */
    onAuthorizationChanged(callback) {
        if (typeof callback !== 'function') {
            console.warn('[ScheduleSignalR] Invalid callback');
            return () => {};
        }

        this._authorizationCallbacks.push(callback);

        // Return unsubscribe function
        return () => {
            const index = this._authorizationCallbacks.indexOf(callback);
            if (index > -1) {
                this._authorizationCallbacks.splice(index, 1);
            }
        };
    }

    /**
     * Check if currently connected
     * @returns {boolean}
     */
    get isConnected() {
        return this._isConnected;
    }
}

// Create global instance
window.scheduleSignalRService = new ScheduleSignalRService();

// Auto-connect when the page loads (if user is authenticated)
document.addEventListener('DOMContentLoaded', () => {
    const token = localStorage.getItem('authToken') ||
                  localStorage.getItem('auth_token') ||
                  sessionStorage.getItem('token');
    if (token) {
        // Delay connection slightly to ensure page is fully loaded
        setTimeout(() => {
            window.scheduleSignalRService.connect();
        }, 1000);
    }
});

// Export for module usage
if (typeof module !== 'undefined' && module.exports) {
    module.exports = ScheduleSignalRService;
}
