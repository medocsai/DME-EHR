/**
 * ConsentSignalRService - Real-time consent notifications via SignalR
 *
 * Connects to the ConsentNotificationHub to receive real-time kiosk
 * consent completion notifications. Triggers alerts and dashboard refreshes
 * when patients complete check-in at the kiosk.
 *
 * @example
 *   const service = new ConsentSignalRService();
 *   await service.connect();
 *   service.onConsentCompleted((notification) => {
 *       console.log('Patient completed kiosk:', notification);
 *   });
 */
class ConsentSignalRService {
    constructor() {
        this._connection = null;
        this._isConnected = false;
        this._reconnectAttempts = 0;
        this._maxReconnectAttempts = 10;
        this._reconnectDelay = 2000; // Start with 2 seconds
        this._consentCallbacks = [];
        this._locationId = null;

        // Bind methods
        this._handleConsentCompleted = this._handleConsentCompleted.bind(this);
    }

    /**
     * Connect to the SignalR consent hub
     * @returns {Promise<boolean>} - True if connected successfully
     */
    async connect() {
        if (this._isConnected) {
            console.log('[ConsentSignalR] Already connected');
            return true;
        }

        // Check if SignalR is available
        if (typeof signalR === 'undefined') {
            console.warn('[ConsentSignalR] SignalR library not loaded');
            return false;
        }

        // Get the auth token (try multiple possible keys)
        const token = localStorage.getItem('authToken') ||
                      localStorage.getItem('auth_token') ||
                      sessionStorage.getItem('token');
        if (!token) {
            console.warn('[ConsentSignalR] No auth token available');
            return false;
        }

        try {
            // Build the connection to consent hub
            this._connection = new signalR.HubConnectionBuilder()
                .withUrl('/hubs/consent', {
                    accessTokenFactory: () => token
                })
                .withAutomaticReconnect({
                    nextRetryDelayInMilliseconds: (retryContext) => {
                        // Exponential backoff: 2s, 4s, 8s, 16s, 32s (max)
                        const delay = Math.min(
                            this._reconnectDelay * Math.pow(2, retryContext.previousRetryCount),
                            32000
                        );
                        console.log(`[ConsentSignalR] Reconnecting in ${delay}ms (attempt ${retryContext.previousRetryCount + 1})`);
                        return delay;
                    }
                })
                .configureLogging(signalR.LogLevel.Information)
                .build();

            // Set up event handlers
            this._connection.on('ConsentCompleted', this._handleConsentCompleted);

            // Connection state change handlers
            this._connection.onreconnecting((error) => {
                console.log('[ConsentSignalR] Reconnecting...', error?.message);
                this._isConnected = false;
            });

            this._connection.onreconnected((connectionId) => {
                console.log('[ConsentSignalR] Reconnected:', connectionId);
                this._isConnected = true;
                this._reconnectAttempts = 0;

                // Rejoin location group if we had one
                if (this._locationId) {
                    this.joinLocationGroup(this._locationId);
                }
            });

            this._connection.onclose((error) => {
                console.log('[ConsentSignalR] Connection closed:', error?.message);
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

            console.log('[ConsentSignalR] Connected successfully to consent hub');
            return true;
        } catch (error) {
            console.error('[ConsentSignalR] Connection failed:', error);
            this._isConnected = false;

            // Retry connection
            if (this._reconnectAttempts < this._maxReconnectAttempts) {
                this._reconnectAttempts++;
                const delay = Math.min(this._reconnectDelay * Math.pow(2, this._reconnectAttempts), 32000);
                console.log(`[ConsentSignalR] Retrying in ${delay}ms`);
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
                console.log('[ConsentSignalR] Disconnected');
            } catch (error) {
                console.error('[ConsentSignalR] Error disconnecting:', error);
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
            console.warn('[ConsentSignalR] Not connected, cannot join location group');
            this._locationId = locationId; // Store for later when we connect
            return;
        }

        try {
            await this._connection.invoke('JoinLocationGroup', locationId);
            this._locationId = locationId;
            console.log(`[ConsentSignalR] Joined location group: ${locationId}`);
        } catch (error) {
            console.error('[ConsentSignalR] Error joining location group:', error);
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
            console.log(`[ConsentSignalR] Left location group: ${locationId}`);
        } catch (error) {
            console.error('[ConsentSignalR] Error leaving location group:', error);
        }
    }

    /**
     * Register a callback for consent completion notifications
     * @param {Function} callback - Function to call when consent is completed
     * @returns {Function} - Unsubscribe function
     */
    onConsentCompleted(callback) {
        if (typeof callback !== 'function') {
            console.warn('[ConsentSignalR] Invalid callback');
            return () => {};
        }

        this._consentCallbacks.push(callback);

        // Return unsubscribe function
        return () => {
            const index = this._consentCallbacks.indexOf(callback);
            if (index > -1) {
                this._consentCallbacks.splice(index, 1);
            }
        };
    }

    /**
     * Handle consent completed notification from SignalR
     * @private
     * @param {Object} notification - Consent completed notification
     * @param {number} notification.ConsentId - The consent ID
     * @param {number} notification.PatientId - The patient ID
     * @param {string} notification.PatientName - The patient's full name
     * @param {number} notification.AppointmentId - The appointment ID
     * @param {string} notification.AppointmentTime - The appointment time
     * @param {string} notification.AppointmentType - Type of appointment
     * @param {number} notification.LocationId - Location ID
     * @param {string} notification.LocationName - Location name
     * @param {number} notification.FormCount - Number of forms completed
     * @param {string} notification.CompletedAt - Completion timestamp
     */
    _handleConsentCompleted(notification) {
        console.log('[ConsentSignalR] Consent completed:', notification);

        // Notify all registered callbacks
        this._consentCallbacks.forEach(callback => {
            try {
                callback(notification);
            } catch (error) {
                console.error('[ConsentSignalR] Callback error:', error);
            }
        });

        // Emit event for modules listening on the window
        if (window.App?.eventBus) {
            window.App.eventBus.emit('signalr:consentCompleted', notification);
        }

        // Also dispatch a custom DOM event for broader compatibility
        const event = new CustomEvent('consentCompleted', { detail: notification });
        document.dispatchEvent(event);

        // Show toast notification for admins
        this._showConsentCompletedAlert(notification);
    }

    /**
     * Show a toast/alert notification when consent is completed
     * @private
     * @param {Object} notification - The notification data
     */
    _showConsentCompletedAlert(notification) {
        // Normalize property names (handle both PascalCase and camelCase from SignalR)
        const patientName = notification.PatientName || notification.patientName || 'Patient';
        const appointmentTimeRaw = notification.AppointmentTime || notification.appointmentTime;
        const appointmentType = notification.AppointmentType || notification.appointmentType || '';
        const locationName = notification.LocationName || notification.locationName || '';
        const formCount = notification.FormCount || notification.formCount || 0;
        const consentId = notification.ConsentId || notification.consentId;

        // Format the time for display
        const appointmentTime = appointmentTimeRaw
            ? new Date(appointmentTimeRaw).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
            : 'Scheduled';

        // Always use custom notification with action button
        this._showRichNotification({
            patientName,
            appointmentTime,
            appointmentType,
            locationName,
            formCount,
            consentId
        });

        // Play notification sound if available
        this._playNotificationSound();
    }

    /**
     * Show a rich notification with action buttons
     * @private
     */
    _showRichNotification(data) {
        const { patientName, appointmentTime, appointmentType, locationName, formCount, consentId } = data;

        // Create toast container if needed
        const toastContainer = document.querySelector('.toast-container.kiosk-notifications') || this._createToastContainer();

        const toast = document.createElement('div');
        toast.className = 'toast align-items-center border-0 show';
        toast.setAttribute('role', 'alert');
        toast.style.cssText = 'min-width: 320px; background: linear-gradient(135deg, #28a745 0%, #20c997 100%); color: white;';

        toast.innerHTML = `
            <div class="toast-header bg-transparent text-white border-0">
                <i class="bi bi-check-circle-fill me-2"></i>
                <strong class="me-auto">Kiosk Check-in Complete</strong>
                <button type="button" class="btn-close btn-close-white" data-bs-dismiss="toast" aria-label="Close"></button>
            </div>
            <div class="toast-body">
                <div class="mb-2">
                    <strong>${this._escapeHtml(patientName)}</strong> has completed check-in
                </div>
                <div class="small mb-2">
                    <i class="bi bi-clock me-1"></i>${appointmentTime}${appointmentType ? ` - ${this._escapeHtml(appointmentType)}` : ''}
                    ${locationName ? `<br><i class="bi bi-geo-alt me-1"></i>${this._escapeHtml(locationName)}` : ''}
                    ${formCount > 0 ? `<br><i class="bi bi-file-text me-1"></i>${formCount} consent form${formCount > 1 ? 's' : ''} signed` : ''}
                </div>
                ${consentId ? `
                    <div class="mt-2">
                        <button type="button" class="btn btn-sm btn-light" onclick="window.consentSignalRService._viewConsent(${consentId})">
                            <i class="bi bi-eye me-1"></i>View
                        </button>
                        <button type="button" class="btn btn-sm btn-outline-light ms-1" onclick="window.consentSignalRService._downloadConsent(${consentId})">
                            <i class="bi bi-download me-1"></i>Download PDF
                        </button>
                    </div>
                ` : ''}
            </div>
        `;

        toastContainer.appendChild(toast);

        // Auto-remove after 15 seconds (longer since it has actions)
        const autoRemoveTimeout = setTimeout(() => {
            toast.classList.remove('show');
            setTimeout(() => toast.remove(), 300);
        }, 15000);

        // Allow manual close
        const closeBtn = toast.querySelector('.btn-close');
        if (closeBtn) {
            closeBtn.addEventListener('click', () => {
                clearTimeout(autoRemoveTimeout);
                toast.classList.remove('show');
                setTimeout(() => toast.remove(), 300);
            });
        }
    }

    /**
     * View consent details
     * @param {number} consentId - The consent ID to view
     */
    _viewConsent(consentId) {
        // Navigate to patient consents or open modal
        if (window.consentModule && typeof window.consentModule.viewConsentDetails === 'function') {
            window.consentModule.viewConsentDetails(consentId);
        } else {
            // Fallback: trigger download which will show the consent
            this._downloadConsent(consentId);
        }
    }

    /**
     * Download consent PDF
     * @param {number} consentId - The consent ID to download
     */
    async _downloadConsent(consentId) {
        try {
            const token = localStorage.getItem('authToken');
            const response = await fetch(`/api/consents/${consentId}/pdf`, {
                headers: {
                    'Authorization': `Bearer ${token}`
                }
            });

            if (!response.ok) {
                throw new Error('Failed to download PDF');
            }

            const blob = await response.blob();
            const url = window.URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = `consent-${consentId}.pdf`;
            document.body.appendChild(a);
            a.click();
            window.URL.revokeObjectURL(url);
            a.remove();
        } catch (error) {
            console.error('[ConsentSignalR] Error downloading consent:', error);
            if (typeof window.showToast === 'function') {
                window.showToast('Failed to download consent PDF', 'error');
            }
        }
    }

    /**
     * Escape HTML to prevent XSS
     * @private
     */
    _escapeHtml(str) {
        if (!str) return '';
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }

    /**
     * Show fallback notification if no toast library is available
     * @private
     * @param {Object} notification - The notification data
     */
    _showFallbackNotification(notification) {
        // Create a temporary toast element if the standard toast functions aren't available
        const toastContainer = document.querySelector('.toast-container') || this._createToastContainer();

        const toast = document.createElement('div');
        toast.className = 'toast align-items-center text-white bg-info border-0 show';
        toast.setAttribute('role', 'alert');
        toast.innerHTML = `
            <div class="d-flex">
                <div class="toast-body">
                    <i class="bi bi-check-circle-fill me-2"></i>
                    <strong>${notification.PatientName}</strong> completed kiosk check-in
                    ${notification.LocationName ? `<br><small class="text-light">at ${notification.LocationName}</small>` : ''}
                </div>
                <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast"></button>
            </div>
        `;

        toastContainer.appendChild(toast);

        // Auto-remove after 8 seconds
        setTimeout(() => {
            toast.classList.remove('show');
            setTimeout(() => toast.remove(), 300);
        }, 8000);

        // Allow manual close
        const closeBtn = toast.querySelector('.btn-close');
        if (closeBtn) {
            closeBtn.addEventListener('click', () => {
                toast.classList.remove('show');
                setTimeout(() => toast.remove(), 300);
            });
        }
    }

    /**
     * Create toast container if it doesn't exist
     * @private
     * @returns {HTMLElement}
     */
    _createToastContainer() {
        let container = document.querySelector('.toast-container.kiosk-notifications');
        if (!container) {
            container = document.createElement('div');
            container.className = 'toast-container kiosk-notifications position-fixed top-0 end-0 p-3';
            container.style.zIndex = '1100';
            document.body.appendChild(container);
        }
        return container;
    }

    /**
     * Play a notification sound
     * @private
     */
    _playNotificationSound() {
        try {
            // Try to play a notification sound if available
            const audio = new Audio('/sounds/notification.mp3');
            audio.volume = 0.3;
            audio.play().catch(() => {
                // Silent fail if audio can't play (browser restrictions)
            });
        } catch (error) {
            // Silent fail
        }
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
window.consentSignalRService = new ConsentSignalRService();

// Auto-connect when the page loads (if user is authenticated and is admin)
document.addEventListener('DOMContentLoaded', () => {
    const token = localStorage.getItem('authToken') ||
                  localStorage.getItem('auth_token') ||
                  sessionStorage.getItem('token');

    // Check if user is admin (role 0 or 1) - only admins receive these notifications
    // Also include FrontDesk (3) as they need to see kiosk check-ins
    const userDataStr = localStorage.getItem('currentUser') || localStorage.getItem('userData') || localStorage.getItem('user');
    let isAdmin = false;

    if (userDataStr) {
        try {
            const userData = JSON.parse(userDataStr);
            // SuperAdmin (0), ClinicAdmin (1), or FrontDesk (3) can receive kiosk notifications
            isAdmin = userData.Role === 0 || userData.Role === 1 || userData.Role === 3 ||
                      userData.role === 0 || userData.role === 1 || userData.role === 3;
        } catch (e) {
            console.warn('[ConsentSignalR] Could not parse user data');
        }
    }

    if (token && isAdmin) {
        // Delay connection slightly to ensure page is fully loaded
        setTimeout(() => {
            window.consentSignalRService.connect();
        }, 1500); // Connect after schedule SignalR
    }
});

// Export for module usage
if (typeof module !== 'undefined' && module.exports) {
    module.exports = ConsentSignalRService;
}
