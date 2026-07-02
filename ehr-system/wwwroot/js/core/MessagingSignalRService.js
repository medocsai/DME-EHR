/**
 * MessagingSignalRService - Real-time messaging via SignalR
 *
 * Handles:
 * - Connection management
 * - Message notifications
 * - Typing indicators
 * - Read receipts
 * - User presence updates
 */
class MessagingSignalRService {
    constructor(options = {}) {
        this.connection = null;
        this.isConnected = false;
        this.reconnectAttempts = 0;
        this.maxReconnectAttempts = 10;
        this.heartbeatInterval = null;

        // Event handlers
        this.onNewMessage = options.onNewMessage || (() => {});
        this.onTypingIndicator = options.onTypingIndicator || (() => {});
        this.onMessageRead = options.onMessageRead || (() => {});
        this.onPresenceChanged = options.onPresenceChanged || (() => {});
        this.onConnected = options.onConnected || (() => {});
        this.onDisconnected = options.onDisconnected || (() => {});
    }

    /**
     * Start the SignalR connection
     */
    async start() {
        // Prevent multiple simultaneous connections
        if (this.isConnected) {
            console.log('MessagingSignalR: Already connected');
            return;
        }

        // Prevent starting while a connection is in progress
        if (this._isConnecting) {
            console.log('MessagingSignalR: Connection already in progress');
            return;
        }

        const token = localStorage.getItem('authToken') || localStorage.getItem('token');
        if (!token) {
            console.warn('MessagingSignalR: No auth token available');
            return;
        }

        this._isConnecting = true;

        try {
            // Stop any existing connection first
            if (this.connection) {
                try {
                    await this.connection.stop();
                } catch (e) {
                    // Ignore errors when stopping
                }
                this.connection = null;
            }

            // Build connection
            this.connection = new signalR.HubConnectionBuilder()
                .withUrl('/hubs/messaging', {
                    accessTokenFactory: () => localStorage.getItem('authToken') || localStorage.getItem('token')
                })
                .withAutomaticReconnect({
                    nextRetryDelayInMilliseconds: (retryContext) => {
                        // Exponential backoff: 1s, 2s, 4s, 8s, 16s, 32s, then 60s
                        if (retryContext.elapsedMilliseconds < 60000) {
                            return Math.min(1000 * Math.pow(2, retryContext.previousRetryCount), 32000);
                        }
                        return 60000;
                    }
                })
                .configureLogging(signalR.LogLevel.Warning)
                .build();

            // Register event handlers
            this._registerEventHandlers();

            // Start connection
            await this.connection.start();

            this.isConnected = true;
            this._isConnecting = false;
            this.reconnectAttempts = 0;
            console.log('MessagingSignalR: Connected');

            // Start heartbeat for presence
            this._startHeartbeat();

            this.onConnected();

        } catch (error) {
            this._isConnecting = false;
            console.error('MessagingSignalR: Connection failed', error);
            this._scheduleReconnect();
        }
    }

    /**
     * Stop the connection
     */
    async stop() {
        console.log('MessagingSignalR: Stopping connection...');

        this._stopHeartbeat();
        this._isConnecting = false;
        this.reconnectAttempts = this.maxReconnectAttempts; // Prevent auto-reconnect

        if (this.connection) {
            try {
                // This will trigger OnDisconnectedAsync on the server
                // which will update user presence to offline
                await this.connection.stop();
                console.log('MessagingSignalR: Connection stopped');
            } catch (error) {
                console.error('MessagingSignalR: Error stopping connection', error);
            }
        }

        this.isConnected = false;
        this.connection = null;
    }

    /**
     * Join a conversation group for real-time updates
     */
    async joinConversation(conversationId) {
        if (!this.isConnected) return;

        try {
            await this.connection.invoke('JoinConversation', conversationId);
        } catch (error) {
            console.error('MessagingSignalR: Error joining conversation', error);
        }
    }

    /**
     * Leave a conversation group
     */
    async leaveConversation(conversationId) {
        if (!this.isConnected) return;

        try {
            await this.connection.invoke('LeaveConversation', conversationId);
        } catch (error) {
            console.error('MessagingSignalR: Error leaving conversation', error);
        }
    }

    /**
     * Send typing indicator
     */
    async sendTypingIndicator(conversationId, isTyping) {
        if (!this.isConnected) return;

        try {
            await this.connection.invoke('SendTypingIndicator', conversationId, isTyping);
        } catch (error) {
            console.error('MessagingSignalR: Error sending typing indicator', error);
        }
    }

    /**
     * Mark message as read
     */
    async markAsRead(conversationId, messageId) {
        if (!this.isConnected) return;

        try {
            await this.connection.invoke('MarkAsRead', conversationId, messageId);
        } catch (error) {
            console.error('MessagingSignalR: Error marking as read', error);
        }
    }

    /**
     * Mark all messages as read
     */
    async markAllAsRead(conversationId) {
        if (!this.isConnected) return;

        try {
            await this.connection.invoke('MarkAllAsRead', conversationId);
        } catch (error) {
            console.error('MessagingSignalR: Error marking all as read', error);
        }
    }

    // ========================================
    // Private Methods
    // ========================================

    _registerEventHandlers() {
        // New message received (sent to recipient's personal group)
        this.connection.on('NewMessage', (notification) => {
            console.log('MessagingSignalR: New message', notification);
            // Map camelCase properties from server to PascalCase for JS
            this.onNewMessage({
                MessageId: notification.messageId,
                ConversationId: notification.conversationId,
                SenderId: notification.senderId,
                SenderName: notification.senderName,
                MessagePreview: notification.messagePreview,
                MessageType: notification.messageType,
                CreatedAt: notification.createdAt
            });
        });

        // Message received in conversation (sent to conversation group)
        this.connection.on('ReceiveMessage', (message) => {
            console.log('MessagingSignalR: Receive message', message);
            this.onNewMessage({
                MessageId: message.messageId,
                ConversationId: message.conversationId,
                SenderId: message.senderId,
                SenderName: message.senderName,
                MessagePreview: message.messageText || message.fileName || 'Voice note',
                MessageType: message.messageType,
                CreatedAt: message.createdAt
            });
        });

        // Typing indicator
        this.connection.on('TypingIndicator', (notification) => {
            this.onTypingIndicator(notification);
        });

        // Message read receipt
        this.connection.on('MessageRead', (notification) => {
            console.log('MessagingSignalR: Message read', notification);
            // Map camelCase properties from server to PascalCase for JS
            this.onMessageRead({
                ConversationId: notification.conversationId,
                MessageId: notification.messageId,
                ReadByUserId: notification.readByUserId,
                ReadAt: notification.readAt
            });
        });

        // All messages read
        this.connection.on('AllMessagesRead', (notification) => {
            console.log('MessagingSignalR: All messages read', notification);
            // Map camelCase properties from server to PascalCase for JS
            this.onMessageRead({
                ConversationId: notification.conversationId,
                ReadByUserId: notification.readByUserId,
                ReadAt: notification.readAt
                // No MessageId means all messages were read
            });
        });

        // User presence changed
        this.connection.on('UserPresenceChanged', (notification) => {
            console.log('MessagingSignalR: User presence changed', notification);
            // Map camelCase properties from server to PascalCase for JS
            this.onPresenceChanged({
                UserId: notification.userId,
                UserName: notification.userName,
                IsOnline: notification.isOnline,
                LastActiveAt: notification.lastActiveAt
            });
        });

        // Connection state changes
        this.connection.onreconnecting((error) => {
            console.warn('MessagingSignalR: Reconnecting...', error);
            this.isConnected = false;
        });

        this.connection.onreconnected((connectionId) => {
            console.log('MessagingSignalR: Reconnected', connectionId);
            this.isConnected = true;
            this.reconnectAttempts = 0;

            // Restart heartbeat to maintain presence
            this._startHeartbeat();

            // Send immediate heartbeat to refresh presence status
            this.connection.invoke('Heartbeat').catch(() => {});

            this.onConnected();
        });

        this.connection.onclose((error) => {
            console.warn('MessagingSignalR: Connection closed', error);
            this.isConnected = false;
            this._stopHeartbeat();
            this.onDisconnected();
            this._scheduleReconnect();
        });
    }

    _scheduleReconnect() {
        if (this.reconnectAttempts >= this.maxReconnectAttempts) {
            console.error('MessagingSignalR: Max reconnection attempts reached');
            return;
        }

        const delay = Math.min(1000 * Math.pow(2, this.reconnectAttempts), 60000);
        this.reconnectAttempts++;

        console.log(`MessagingSignalR: Reconnecting in ${delay / 1000}s (attempt ${this.reconnectAttempts})`);

        setTimeout(() => {
            if (!this.isConnected) {
                this.start();
            }
        }, delay);
    }

    _startHeartbeat() {
        this._stopHeartbeat();

        // Send heartbeat every 30 seconds to maintain presence
        this.heartbeatInterval = setInterval(async () => {
            if (this.isConnected) {
                try {
                    await this.connection.invoke('Heartbeat');
                } catch (error) {
                    console.error('MessagingSignalR: Heartbeat failed', error);
                }
            }
        }, 30000);
    }

    _stopHeartbeat() {
        if (this.heartbeatInterval) {
            clearInterval(this.heartbeatInterval);
            this.heartbeatInterval = null;
        }
    }
}

// Export for use in modules
if (typeof module !== 'undefined' && module.exports) {
    module.exports = MessagingSignalRService;
}
