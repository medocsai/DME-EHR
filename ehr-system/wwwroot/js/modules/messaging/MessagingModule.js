/**
 * MessagingModule - Internal Communication System
 *
 * Features:
 * - Real-time text messaging
 * - Voice note recording and playback
 * - File attachments
 * - Read receipts
 * - Typing indicators
 * - User presence (online/offline)
 */
class MessagingModule {
    constructor(options = {}) {
        this.api = options.api || null;
        this.eventBus = options.eventBus || null;

        // State
        this.conversations = [];
        this.currentConversation = null;
        this.currentMessages = [];
        this.availableUsers = [];
        this.isOpen = false;
        this.isTyping = false;
        this.typingTimeout = null;

        // Voice recording state
        this.mediaRecorder = null;
        this.audioChunks = [];
        this.recordingStartTime = null;
        this.voiceNoteTimerInterval = null;

        // Voice playback state
        this._currentAudio = null;
        this._currentAudioMessageId = null;

        // SignalR connection
        this.signalRService = null;

        // Bound handlers
        this._boundHandlers = {};
    }

    /**
     * Initialize the module
     */
    async init() {
        // Always bind events first - they check auth before performing actions
        this._bindEvents();

        // Check authentication for API operations
        if (!this._isAuthenticated()) {
            // Hide the chat toggle button for non-authenticated users
            const toggleBtn = document.getElementById('chatToggleBtn');
            if (toggleBtn) toggleBtn.style.display = 'none';
            return;
        }

        // Initialize SignalR and load unread count (non-blocking)
        try {
            await this._initSignalR();
        } catch (e) {
            console.warn('[Messaging] SignalR init failed:', e.message);
        }

        try {
            await this._loadUnreadCount();
        } catch (e) {
            console.warn('[Messaging] Failed to load unread count:', e.message);
        }
    }

    /**
     * Check if user is authenticated
     */
    _isAuthenticated() {
        // Check if App is available and user is authenticated
        if (window.App?.auth) {
            // isAuthenticated could be a function or a boolean property
            if (typeof window.App.auth.isAuthenticated === 'function') {
                return window.App.auth.isAuthenticated();
            }
            if (typeof window.App.auth.isAuthenticated === 'boolean') {
                return window.App.auth.isAuthenticated;
            }
        }
        // Fallback: check for token in localStorage
        return !!localStorage.getItem('authToken');
    }

    /**
     * Initialize SignalR connection for real-time features
     */
    async _initSignalR() {
        if (typeof MessagingSignalRService !== 'undefined') {
            this.signalRService = new MessagingSignalRService({
                onNewMessage: (notification) => this._handleNewMessage(notification),
                onTypingIndicator: (notification) => this._handleTypingIndicator(notification),
                onMessageRead: (notification) => this._handleMessageRead(notification),
                onPresenceChanged: (notification) => this._handlePresenceChanged(notification)
            });
            await this.signalRService.start();
        }
    }

    /**
     * Bind event handlers
     */
    _bindEvents() {
        // Mark as bound for debugging
        this._eventsBound = true;

        // Chat toggle button
        const chatToggle = document.getElementById('chatToggleBtn');
        if (chatToggle) {
            chatToggle.addEventListener('click', (e) => {
                e.preventDefault();
                e.stopPropagation();
                this.toggleChat();
            });
        }

        // Close chat button
        const closeChat = document.getElementById('closeChatBtn');
        if (closeChat) {
            closeChat.addEventListener('click', () => this.closeChat());
        }

        // New conversation button
        const newConvBtn = document.getElementById('newConversationBtn');
        if (newConvBtn) {
            newConvBtn.addEventListener('click', () => this.showNewConversationModal());
        }

        // Message input
        const messageInput = document.getElementById('messageInput');
        if (messageInput) {
            messageInput.addEventListener('keypress', (e) => {
                if (e.key === 'Enter' && !e.shiftKey) {
                    // Don't send if mention dropdown is open (user is selecting a patient)
                    if (this.mentionHelper && this.mentionHelper.isDropdownOpen) return;
                    e.preventDefault();
                    this.sendMessage();
                }
            });
            messageInput.addEventListener('input', () => this._handleTyping());

            // Initialize patient mention helper (@mention support)
            if (typeof PatientMentionHelper !== 'undefined') {
                this.mentionHelper = new PatientMentionHelper({
                    textarea: messageInput,
                    apiGet: (endpoint) => this._apiGet(endpoint)
                });
            }
        }

        // Send button
        const sendBtn = document.getElementById('sendMessageBtn');
        if (sendBtn) {
            sendBtn.addEventListener('click', () => this.sendMessage());
        }

        // Attachment button
        const attachBtn = document.getElementById('attachFileBtn');
        if (attachBtn) {
            attachBtn.addEventListener('click', () => this._triggerFileInput());
        }

        // Voice note button - click to start recording
        const voiceBtn = document.getElementById('voiceNoteBtn');
        if (voiceBtn) {
            voiceBtn.addEventListener('click', () => this.startRecording());
        }

        // Cancel recording button
        const cancelRecBtn = document.getElementById('cancelRecordingBtn');
        if (cancelRecBtn) {
            cancelRecBtn.addEventListener('click', () => this.cancelRecording());
        }

        // Send recording button
        const sendRecBtn = document.getElementById('sendRecordingBtn');
        if (sendRecBtn) {
            sendRecBtn.addEventListener('click', () => this.stopRecording());
        }

        // File input change
        const fileInput = document.getElementById('messageFileInput');
        if (fileInput) {
            fileInput.addEventListener('change', (e) => this._handleFileSelect(e));
        }

        // Back button (mobile view)
        const backBtn = document.getElementById('backToConversationsBtn');
        if (backBtn) {
            backBtn.addEventListener('click', () => this._showConversationList());
        }

        // Search input
        const searchInput = document.getElementById('conversationSearchInput');
        if (searchInput) {
            searchInput.addEventListener('input', (e) => this._filterConversations(e.target.value));
        }

        // Messages container scroll (infinite scroll)
        const messagesContainer = document.getElementById('messagesContainer');
        if (messagesContainer) {
            messagesContainer.addEventListener('scroll', () => this._handleScroll());
        }
    }

    /**
     * Toggle chat panel visibility
     */
    async toggleChat() {
        const chatPanel = document.getElementById('messagingPanel');
        if (!chatPanel) return;

        this.isOpen = !this.isOpen;

        if (this.isOpen) {
            chatPanel.classList.add('open');
            await this.loadConversations();
        } else {
            chatPanel.classList.remove('open');
        }
    }

    /**
     * Open chat panel
     */
    openChat() {
        const chatPanel = document.getElementById('messagingPanel');
        if (!chatPanel) return;

        this.isOpen = true;
        chatPanel.classList.add('open');
        this.loadConversations();
    }

    /**
     * Close chat panel
     */
    closeChat() {
        const chatPanel = document.getElementById('messagingPanel');
        if (!chatPanel) return;

        this.isOpen = false;
        chatPanel.classList.remove('open');

        // Leave current conversation group
        if (this.currentConversation && this.signalRService) {
            this.signalRService.leaveConversation(this.currentConversation.ConversationId);
        }
    }

    /**
     * Load all conversations
     */
    async loadConversations() {
        const listContainer = document.getElementById('conversationsList');
        if (!listContainer) return;

        try {
            listContainer.innerHTML = '<div class="text-center py-4"><div class="spinner-border spinner-border-sm"></div></div>';

            this.conversations = await this._apiGet('/messaging/conversations');

            if (this.conversations.length === 0) {
                listContainer.innerHTML = `
                    <div class="text-center py-4 text-muted">
                        <i class="bi bi-chat-square-dots display-6"></i>
                        <p class="mt-2 mb-0">No conversations yet</p>
                        <small>Start a new conversation with a colleague</small>
                    </div>
                `;
                return;
            }

            listContainer.innerHTML = this.conversations.map(c => this._renderConversationItem(c)).join('');

        } catch (error) {
            console.error('Error loading conversations:', error);
            listContainer.innerHTML = '<div class="text-center py-4 text-danger">Failed to load conversations</div>';
        }
    }

    /**
     * Open a conversation
     */
    async openConversation(conversationId) {
        const conversation = this.conversations.find(c => c.ConversationId === conversationId);
        if (!conversation) return;

        // Leave previous conversation group
        if (this.currentConversation && this.signalRService) {
            this.signalRService.leaveConversation(this.currentConversation.ConversationId);
        }

        this.currentConversation = conversation;

        // Update UI
        this._showChatWindow();
        this._updateChatHeader();

        // Join conversation group for real-time updates
        if (this.signalRService) {
            this.signalRService.joinConversation(conversationId);
        }

        // Load messages
        await this.loadMessages();

        // Mark as read
        if (conversation.UnreadCount > 0) {
            await this._markConversationAsRead(conversationId);
        }

        // Focus input
        const input = document.getElementById('messageInput');
        if (input) input.focus();
    }

    /**
     * Load messages for current conversation
     */
    async loadMessages(beforeMessageId = null) {
        if (!this.currentConversation) return;

        const container = document.getElementById('messagesContainer');
        if (!container) return;

        try {
            if (!beforeMessageId) {
                container.innerHTML = '<div class="text-center py-4"><div class="spinner-border spinner-border-sm"></div></div>';
            }

            let url = `/messaging/conversations/${this.currentConversation.ConversationId}/messages`;
            if (beforeMessageId) {
                url += `?beforeMessageId=${beforeMessageId}`;
            }

            const response = await this._apiGet(url);
            const messages = response.Messages || [];

            if (beforeMessageId) {
                // Prepend older messages
                this.currentMessages = [...messages, ...this.currentMessages];
                const oldScrollHeight = container.scrollHeight;
                container.innerHTML = this.currentMessages.map(m => this._renderMessage(m)).join('');
                // Maintain scroll position
                container.scrollTop = container.scrollHeight - oldScrollHeight;
            } else {
                this.currentMessages = messages;
                container.innerHTML = messages.length > 0
                    ? messages.map(m => this._renderMessage(m)).join('')
                    : '<div class="text-center py-4 text-muted">No messages yet. Say hello!</div>';

                // Scroll to bottom
                this._scrollToBottom();
            }

        } catch (error) {
            console.error('Error loading messages:', error);
            container.innerHTML = '<div class="text-center py-4 text-danger">Failed to load messages</div>';
        }
    }

    /**
     * Send a text message
     */
    async sendMessage() {
        const input = document.getElementById('messageInput');
        if (!input || !this.currentConversation) return;

        let text = input.value.trim();
        if (!text) return;

        // Serialize any @patient mentions into wire format
        if (this.mentionHelper) {
            text = this.mentionHelper.serializeMessage(text);
        }

        try {
            // Clear input immediately for better UX
            input.value = '';
            this._stopTyping();
            if (this.mentionHelper) this.mentionHelper.reset();

            const result = await this._apiPost('/messaging/messages', {
                RecipientId: this.currentConversation.OtherUser.UserId,
                MessageText: text
            });

            if (result.Success) {
                // Add message to UI optimistically
                this._addMessageToUI({
                    MessageId: result.MessageId,
                    ConversationId: result.ConversationId,
                    SenderId: this._getCurrentUserId(),
                    MessageText: text,
                    MessageType: 0,
                    IsMine: true,
                    CreatedAt: result.CreatedAt || new Date().toISOString(),
                    CreatedAtFormatted: 'Just now',
                    IsRead: false
                });

                // Update conversation in list
                this._updateConversationPreview(result.ConversationId, text);
            } else {
                this._showToast('Error', result.Message || 'Failed to send message', 'error');
                input.value = text; // Restore input
            }

        } catch (error) {
            console.error('Error sending message:', error);
            this._showToast('Error', 'Failed to send message', 'error');
            input.value = text;
        }
    }

    /**
     * Start voice recording
     */
    async startRecording() {
        // If already recording, do nothing
        if (this.mediaRecorder && this.mediaRecorder.state === 'recording') {
            return;
        }

        try {
            const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
            this.audioStream = stream; // Store stream reference for cleanup
            this.mediaRecorder = new MediaRecorder(stream);
            this.audioChunks = [];
            this.recordingStartTime = Date.now();

            this.mediaRecorder.ondataavailable = (e) => {
                if (e.data.size > 0) {
                    this.audioChunks.push(e.data);
                }
            };

            // Start recording with timeslice to collect data periodically
            this.mediaRecorder.start(100);

            // Show recording UI
            this._showRecordingUI();

            // Start timer
            this.voiceNoteTimerInterval = setInterval(() => this._updateRecordingTimer(), 100);

        } catch (error) {
            console.error('Error starting recording:', error);
            this._showToast('Error', 'Could not access microphone. Please allow microphone access.', 'error');
        }
    }

    /**
     * Stop voice recording and send
     */
    async stopRecording() {
        if (!this.mediaRecorder || this.mediaRecorder.state !== 'recording') {
            this._hideRecordingUI();
            return;
        }

        const duration = (Date.now() - this.recordingStartTime) / 1000;

        // Minimum 1 second recording
        if (duration < 1) {
            this._showToast('Info', 'Recording too short. Please record at least 1 second.', 'info');
            this.cancelRecording();
            return;
        }

        // Stop the timer
        if (this.voiceNoteTimerInterval) {
            clearInterval(this.voiceNoteTimerInterval);
            this.voiceNoteTimerInterval = null;
        }

        // Create a promise to wait for the stop event
        const stopPromise = new Promise(resolve => {
            this.mediaRecorder.onstop = resolve;
        });

        // Stop recording
        this.mediaRecorder.stop();

        // Wait for stop event
        await stopPromise;

        // Stop all audio tracks
        if (this.audioStream) {
            this.audioStream.getTracks().forEach(track => track.stop());
            this.audioStream = null;
        }

        // Hide recording UI
        this._hideRecordingUI();

        // Create blob and send
        if (this.audioChunks.length > 0) {
            const audioBlob = new Blob(this.audioChunks, { type: 'audio/webm' });
            await this._sendVoiceNote(audioBlob, duration);
        }

        this.mediaRecorder = null;
        this.audioChunks = [];
    }

    /**
     * Cancel voice recording
     */
    cancelRecording() {
        // Stop the timer
        if (this.voiceNoteTimerInterval) {
            clearInterval(this.voiceNoteTimerInterval);
            this.voiceNoteTimerInterval = null;
        }

        if (this.mediaRecorder && this.mediaRecorder.state === 'recording') {
            this.mediaRecorder.stop();
        }

        // Stop all audio tracks
        if (this.audioStream) {
            this.audioStream.getTracks().forEach(track => track.stop());
            this.audioStream = null;
        }

        // Hide recording UI
        this._hideRecordingUI();

        this.mediaRecorder = null;
        this.audioChunks = [];

        this._showToast('Info', 'Recording cancelled', 'info');
    }

    /**
     * Send voice note
     */
    async _sendVoiceNote(audioBlob, duration) {
        if (!this.currentConversation) return;

        try {
            const formData = new FormData();
            formData.append('audioFile', audioBlob, 'voicenote.webm');
            formData.append('recipientId', this.currentConversation.OtherUser.UserId);
            formData.append('durationSeconds', duration.toFixed(2));

            const result = await this._apiPostFormData('/messaging/voice-notes', formData);

            if (result.Success) {
                this._addMessageToUI({
                    MessageId: result.MessageId,
                    ConversationId: result.ConversationId,
                    SenderId: this._getCurrentUserId(),
                    MessageType: 1,
                    FileUrl: result.FileUrl,
                    FileDurationSeconds: duration,
                    DurationFormatted: this._formatDuration(duration),
                    IsMine: true,
                    CreatedAt: result.CreatedAt || new Date().toISOString(),
                    CreatedAtFormatted: 'Just now',
                    IsRead: false
                });

                this._updateConversationPreview(result.ConversationId, 'Voice note');
            } else {
                this._showToast('Error', result.Message || 'Failed to send voice note', 'error');
            }

        } catch (error) {
            console.error('Error sending voice note:', error);
            this._showToast('Error', 'Failed to send voice note', 'error');
        }
    }

    /**
     * Handle file selection for attachment
     */
    async _handleFileSelect(event) {
        const file = event.target.files[0];
        if (!file || !this.currentConversation) return;

        // Reset input
        event.target.value = '';

        // Validate file size (10MB max)
        if (file.size > 10 * 1024 * 1024) {
            this._showToast('Error', 'File size must be less than 10MB', 'error');
            return;
        }

        try {
            const formData = new FormData();
            formData.append('file', file);
            formData.append('recipientId', this.currentConversation.OtherUser.UserId);

            // Show inline upload indicator
            this._showUploadIndicator(file.name);

            const result = await this._apiPostFormData('/messaging/attachments', formData);

            // Hide upload indicator
            this._hideUploadIndicator();

            if (result.Success) {
                this._addMessageToUI({
                    MessageId: result.MessageId,
                    ConversationId: result.ConversationId,
                    SenderId: this._getCurrentUserId(),
                    MessageType: 2,
                    FileUrl: result.FileUrl,
                    FileName: file.name,
                    FileSize: file.size,
                    FileSizeFormatted: this._formatFileSize(file.size),
                    FileMimeType: file.type,
                    IsMine: true,
                    CreatedAt: result.CreatedAt || new Date().toISOString(),
                    CreatedAtFormatted: 'Just now',
                    IsRead: false
                });

                this._updateConversationPreview(result.ConversationId, file.name);
            } else {
                this._showToast('Error', result.Message || 'Failed to send file', 'error');
            }

        } catch (error) {
            console.error('Error sending file:', error);
            this._hideUploadIndicator();
            this._showToast('Error', 'Failed to send file', 'error');
        }
    }

    /**
     * Show new conversation modal
     */
    async showNewConversationModal() {
        const modal = document.getElementById('newConversationModal');
        const usersList = document.getElementById('newConversationUsersList');

        if (!modal || !usersList) return;

        try {
            usersList.innerHTML = '<div class="text-center py-4"><div class="spinner-border spinner-border-sm"></div></div>';

            const bsModal = new bootstrap.Modal(modal);
            bsModal.show();

            this.availableUsers = await this._apiGet('/messaging/users');

            if (this.availableUsers.length === 0) {
                usersList.innerHTML = '<div class="text-center py-4 text-muted">No other users available</div>';
                return;
            }

            usersList.innerHTML = this.availableUsers.map(u => `
                <button type="button" class="list-group-item list-group-item-action d-flex align-items-center"
                        onclick="messagingModule.startConversation(${u.UserId})">
                    <div class="avatar-sm me-3 ${u.IsOnline ? 'online' : ''}">
                        ${u.Initials}
                    </div>
                    <div class="flex-grow-1">
                        <div class="fw-medium">${this._escape(u.FullName)}</div>
                        <small class="text-muted">${this._escape(u.RoleName)}</small>
                    </div>
                    ${u.IsOnline ? '<span class="badge bg-success">Online</span>' : ''}
                </button>
            `).join('');

        } catch (error) {
            console.error('Error loading users:', error);
            usersList.innerHTML = '<div class="text-center py-4 text-danger">Failed to load users</div>';
        }
    }

    /**
     * Start a new conversation with a user
     */
    async startConversation(userId) {
        try {
            // Close modal
            const modal = bootstrap.Modal.getInstance(document.getElementById('newConversationModal'));
            if (modal) modal.hide();

            const conversation = await this._apiPost(`/messaging/conversations/${userId}`, {});

            if (conversation) {
                // Add to conversations list if not exists
                const existing = this.conversations.find(c => c.ConversationId === conversation.ConversationId);
                if (!existing) {
                    this.conversations.unshift(conversation);
                    this.loadConversations();
                }

                // Open the conversation
                await this.openConversation(conversation.ConversationId);
            }

        } catch (error) {
            console.error('Error starting conversation:', error);
            this._showToast('Error', 'Failed to start conversation', 'error');
        }
    }

    /**
     * Load unread count for badge
     */
    async _loadUnreadCount() {
        try {
            const summary = await this._apiGet('/messaging/unread-summary');
            this._updateUnreadBadge(summary.TotalUnreadCount);
        } catch (error) {
            console.error('Error loading unread count:', error);
        }
    }

    // ========================================
    // SignalR Event Handlers
    // ========================================

    _handleNewMessage(notification) {
        // Don't process if it's our own message (we already added it optimistically)
        if (notification.SenderId === this._getCurrentUserId()) {
            return;
        }

        // Update unread count
        this._loadUnreadCount();

        // If chat is open and this is the current conversation, add message silently
        if (this.isOpen && this.currentConversation?.ConversationId === notification.ConversationId) {
            // Load the full message - no toast needed, message appears in chat
            this._fetchAndAddNewMessage(notification.MessageId);

            // Mark as read since user is viewing this conversation
            this._markConversationAsRead(notification.ConversationId);
        } else {
            // Chat is closed or different conversation - show subtle notification
            // Only show if we have valid sender info
            if (notification.SenderName && notification.MessagePreview) {
                this._showMessageNotification(notification.SenderName, notification.MessagePreview, notification.ConversationId);
            }
        }

        // Update conversation list
        this._updateConversationFromNotification(notification);
    }

    _handleTypingIndicator(notification) {
        if (this.currentConversation?.ConversationId !== notification.ConversationId) return;

        const indicator = document.getElementById('typingIndicator');
        if (indicator) {
            if (notification.IsTyping) {
                indicator.innerHTML = `<em>${this._escape(notification.UserName)} is typing...</em>`;
                indicator.style.display = 'block';
            } else {
                indicator.style.display = 'none';
            }
        }
    }

    _handleMessageRead(notification) {
        if (this.currentConversation?.ConversationId !== notification.ConversationId) return;

        // Check if this is a single message read or all messages read
        if (notification.MessageId) {
            // Single message read - update that specific message
            const messageEl = document.querySelector(`[data-message-id="${notification.MessageId}"]`);
            if (messageEl) {
                const statusEl = messageEl.querySelector('.message-status');
                if (statusEl) {
                    statusEl.innerHTML = '<i class="bi bi-check-all text-primary"></i>';
                }
            }
        } else {
            // All messages read - update all my messages to show read status
            const messagesContainer = document.getElementById('messagesContainer');
            if (messagesContainer) {
                // Get all messages that are mine (have message-status element)
                const myMessages = messagesContainer.querySelectorAll('.message.mine .message-status');
                myMessages.forEach(statusEl => {
                    statusEl.innerHTML = '<i class="bi bi-check-all text-primary"></i>';
                });
            }

            // Also update internal message state
            this.currentMessages.forEach(msg => {
                if (msg.IsMine) {
                    msg.IsRead = true;
                }
            });
        }
    }

    _handlePresenceChanged(notification) {
        console.log('[Messaging] Presence changed:', notification);

        // Update user online status in conversation list
        const convItem = document.querySelector(`[data-user-id="${notification.UserId}"]`);
        if (convItem) {
            const avatar = convItem.querySelector('.avatar-sm');
            if (avatar) {
                if (notification.IsOnline) {
                    avatar.classList.add('online');
                } else {
                    avatar.classList.remove('online');
                }
            }
        }

        // Update internal conversation data
        const conv = this.conversations.find(c => c.OtherUser?.UserId === notification.UserId);
        if (conv) {
            conv.OtherUser.IsOnline = notification.IsOnline;
        }

        // Update current conversation header
        if (this.currentConversation?.OtherUser?.UserId === notification.UserId) {
            this.currentConversation.OtherUser.IsOnline = notification.IsOnline;
            this._updateChatHeader();
        }
    }

    // ========================================
    // UI Helpers
    // ========================================

    _renderConversationItem(conversation) {
        const unreadClass = conversation.UnreadCount > 0 ? 'unread' : '';
        let lastMessage = conversation.LastMessagePreview || 'No messages yet';
        if (typeof PatientMentionHelper !== 'undefined') {
            lastMessage = PatientMentionHelper.cleanPreviewText(lastMessage);
        }
        const timeAgo = conversation.LastMessageAt
            ? this._formatTimeAgo(conversation.LastMessageAt)
            : '';

        return `
            <div class="conversation-item ${unreadClass}"
                 data-conversation-id="${conversation.ConversationId}"
                 data-user-id="${conversation.OtherUser.UserId}"
                 onclick="messagingModule.openConversation(${conversation.ConversationId})">
                <div class="avatar-sm ${conversation.OtherUser.IsOnline ? 'online' : ''}">
                    ${conversation.OtherUser.Initials}
                </div>
                <div class="conversation-info">
                    <div class="conversation-name">
                        ${this._escape(conversation.OtherUser.FullName)}
                    </div>
                    <div class="conversation-preview">
                        ${this._escape(lastMessage)}
                    </div>
                </div>
                <div class="conversation-meta">
                    <div class="conversation-time">${timeAgo}</div>
                    ${conversation.UnreadCount > 0
                        ? `<div class="unread-badge">${conversation.UnreadCount}</div>`
                        : ''}
                </div>
            </div>
        `;
    }

    _renderMessage(message) {
        const isFile = message.MessageType === 2;
        const isVoiceNote = message.MessageType === 1;

        let content = '';

        if (isVoiceNote) {
            content = this._renderVoiceNote(message);
        } else if (isFile) {
            content = this._renderFileAttachment(message);
        } else {
            let escapedText = this._escape(message.MessageText || '');
            if (typeof PatientMentionHelper !== 'undefined') {
                escapedText = PatientMentionHelper.renderMentions(escapedText);
            }
            content = `<div class="message-text">${escapedText}</div>`;
        }

        const readStatus = message.IsMine
            ? `<span class="message-status">${message.IsRead
                ? '<i class="bi bi-check-all text-primary"></i>'
                : '<i class="bi bi-check"></i>'}</span>`
            : '';

        return `
            <div class="message ${message.IsMine ? 'mine' : 'theirs'}"
                 data-message-id="${message.MessageId}">
                ${content}
                <div class="message-meta">
                    <span class="message-time">${message.CreatedAtFormatted || ''}</span>
                    ${readStatus}
                </div>
            </div>
        `;
    }

    _renderVoiceNote(message) {
        return `
            <div class="voice-note">
                <button class="voice-play-btn" onclick="messagingModule.playVoiceNote(${message.MessageId}, '${message.FileUrl}')">
                    <i class="bi bi-play-fill"></i>
                </button>
                <div class="voice-waveform">
                    <div class="voice-progress" id="voiceProgress_${message.MessageId}"></div>
                </div>
                <span class="voice-duration">${message.DurationFormatted || '0:00'}</span>
            </div>
        `;
    }

    _renderFileAttachment(message) {
        const icon = this._getFileIcon(message.FileMimeType);
        return `
            <div class="file-attachment" onclick="messagingModule.downloadFile('${message.FileUrl}', '${this._escape(message.FileName)}')">
                <i class="bi ${icon} file-icon"></i>
                <div class="file-info">
                    <div class="file-name">${this._escape(message.FileName || 'File')}</div>
                    <div class="file-size">${message.FileSizeFormatted || ''}</div>
                </div>
                <i class="bi bi-download"></i>
            </div>
        `;
    }

    _showChatWindow() {
        const list = document.getElementById('conversationsListView');
        const chat = document.getElementById('chatWindowView');
        if (list) list.style.display = 'none';
        if (chat) chat.style.display = 'flex';
    }

    _showConversationList() {
        const list = document.getElementById('conversationsListView');
        const chat = document.getElementById('chatWindowView');
        if (list) list.style.display = 'flex';
        if (chat) chat.style.display = 'none';

        // Leave conversation group
        if (this.currentConversation && this.signalRService) {
            this.signalRService.leaveConversation(this.currentConversation.ConversationId);
        }
        this.currentConversation = null;
    }

    _updateChatHeader() {
        const nameEl = document.getElementById('chatUserName');
        const statusEl = document.getElementById('chatUserStatus');

        if (this.currentConversation) {
            if (nameEl) nameEl.textContent = this.currentConversation.OtherUser.FullName;
            if (statusEl) {
                statusEl.innerHTML = this.currentConversation.OtherUser.IsOnline
                    ? '<span class="status-online">Online</span>'
                    : '<span class="status-offline">Offline</span>';
            }
        }
    }

    _addMessageToUI(message) {
        const container = document.getElementById('messagesContainer');
        if (!container) return;

        // Remove "no messages" placeholder
        const placeholder = container.querySelector('.text-muted');
        if (placeholder) placeholder.remove();

        const html = this._renderMessage(message);
        container.insertAdjacentHTML('beforeend', html);

        this.currentMessages.push(message);
        this._scrollToBottom();
    }

    _scrollToBottom() {
        const container = document.getElementById('messagesContainer');
        if (container) {
            container.scrollTop = container.scrollHeight;
        }
    }

    _updateUnreadBadge(count) {
        const badge = document.getElementById('messagingUnreadBadge');
        if (badge) {
            if (count > 0) {
                badge.textContent = count > 99 ? '99+' : count;
                badge.style.display = 'flex';
            } else {
                badge.style.display = 'none';
            }
        }
    }

    _updateConversationPreview(conversationId, preview) {
        if (typeof PatientMentionHelper !== 'undefined') {
            preview = PatientMentionHelper.cleanPreviewText(preview);
        }
        const item = document.querySelector(`[data-conversation-id="${conversationId}"]`);
        if (item) {
            const previewEl = item.querySelector('.conversation-preview');
            if (previewEl) previewEl.textContent = preview;

            const timeEl = item.querySelector('.conversation-time');
            if (timeEl) timeEl.textContent = 'Just now';

            // Move to top
            const parent = item.parentElement;
            if (parent) {
                parent.prepend(item);
            }
        }
    }

    _updateConversationFromNotification(notification) {
        const existing = this.conversations.find(c => c.ConversationId === notification.ConversationId);
        if (existing) {
            existing.LastMessagePreview = notification.MessagePreview;
            existing.LastMessageAt = notification.CreatedAt;
            if (!this.isOpen || this.currentConversation?.ConversationId !== notification.ConversationId) {
                existing.UnreadCount = (existing.UnreadCount || 0) + 1;
            }
        }
        this.loadConversations();
    }

    async _markConversationAsRead(conversationId) {
        try {
            await this._apiPut(`/messaging/conversations/${conversationId}/read`);

            const conv = this.conversations.find(c => c.ConversationId === conversationId);
            if (conv) conv.UnreadCount = 0;

            this._loadUnreadCount();

            // Also notify via SignalR so sender sees read receipts
            if (this.signalRService) {
                this.signalRService.markAllAsRead(conversationId);
            }
        } catch (error) {
            console.error('Error marking as read:', error);
        }
    }

    async _fetchAndAddNewMessage(messageId) {
        // Fetch the message and add to UI
        try {
            const response = await this._apiGet(`/messaging/conversations/${this.currentConversation.ConversationId}/messages?afterMessageId=${this.currentMessages[this.currentMessages.length - 1]?.MessageId || 0}`);
            const newMessages = response.Messages || [];

            for (const msg of newMessages) {
                if (!this.currentMessages.some(m => m.MessageId === msg.MessageId)) {
                    this._addMessageToUI(msg);
                }
            }
        } catch (error) {
            console.error('Error fetching new message:', error);
        }
    }

    // ========================================
    // Recording UI
    // ========================================

    _showRecordingUI() {
        const normalArea = document.getElementById('normalInputArea');
        const recordingArea = document.getElementById('recordingInputArea');

        if (normalArea) normalArea.style.display = 'none';
        if (recordingArea) recordingArea.style.display = 'flex';

        // Reset timer display
        const timerEl = document.getElementById('voiceNoteTimer');
        if (timerEl) timerEl.textContent = '0:00';
    }

    _hideRecordingUI() {
        const normalArea = document.getElementById('normalInputArea');
        const recordingArea = document.getElementById('recordingInputArea');

        if (normalArea) normalArea.style.display = 'flex';
        if (recordingArea) recordingArea.style.display = 'none';
    }

    _updateRecordingTimer() {
        const duration = (Date.now() - this.recordingStartTime) / 1000;
        const timerEl = document.getElementById('voiceNoteTimer');
        if (timerEl) {
            timerEl.textContent = this._formatDuration(duration);
        }
    }

    // Legacy methods for backward compatibility
    _showRecordingIndicator() {
        this._showRecordingUI();
    }

    _hideRecordingIndicator() {
        this._hideRecordingUI();
    }

    // ========================================
    // Upload Indicator
    // ========================================

    _showUploadIndicator(fileName) {
        // Remove any existing indicator
        this._hideUploadIndicator();

        // Create upload indicator element
        const indicator = document.createElement('div');
        indicator.id = 'uploadIndicator';
        indicator.className = 'upload-indicator';
        indicator.innerHTML = `
            <div class="upload-indicator-content">
                <div class="spinner-border spinner-border-sm text-primary" role="status">
                    <span class="visually-hidden">Uploading...</span>
                </div>
                <span class="upload-filename">${this._escape(fileName.length > 25 ? fileName.substring(0, 22) + '...' : fileName)}</span>
            </div>
        `;

        // Insert before the message input area
        const inputArea = document.getElementById('normalInputArea');
        if (inputArea) {
            inputArea.parentNode.insertBefore(indicator, inputArea);
        }

        // Disable attachment button during upload
        const attachBtn = document.getElementById('attachFileBtn');
        if (attachBtn) {
            attachBtn.disabled = true;
        }
    }

    _hideUploadIndicator() {
        const indicator = document.getElementById('uploadIndicator');
        if (indicator) {
            indicator.remove();
        }

        // Re-enable attachment button
        const attachBtn = document.getElementById('attachFileBtn');
        if (attachBtn) {
            attachBtn.disabled = false;
        }
    }

    // ========================================
    // Typing Indicator
    // ========================================

    _handleTyping() {
        if (!this.currentConversation || !this.signalRService) return;

        if (!this.isTyping) {
            this.isTyping = true;
            this.signalRService.sendTypingIndicator(this.currentConversation.ConversationId, true);
        }

        clearTimeout(this.typingTimeout);
        this.typingTimeout = setTimeout(() => this._stopTyping(), 2000);
    }

    _stopTyping() {
        if (this.isTyping && this.currentConversation && this.signalRService) {
            this.isTyping = false;
            this.signalRService.sendTypingIndicator(this.currentConversation.ConversationId, false);
        }
        clearTimeout(this.typingTimeout);
    }

    // ========================================
    // Voice/File Playback
    // ========================================

    playVoiceNote(messageId, url) {
        console.log('[Messaging] Playing voice note:', messageId, 'URL:', url);

        if (!url || url === 'undefined' || url === 'null') {
            console.error('[Messaging] Invalid audio URL for message:', messageId);
            this._showToast('Error', 'Audio not available', 'error');
            return;
        }

        const btn = document.querySelector(`[data-message-id="${messageId}"] .voice-play-btn`);
        const progressEl = document.getElementById(`voiceProgress_${messageId}`);

        // Check if we're already playing this audio
        if (this._currentAudio && this._currentAudioMessageId === messageId) {
            // Toggle pause/play
            if (this._currentAudio.paused) {
                this._currentAudio.play().catch(err => console.error('Audio playback error:', err));
                if (btn) btn.innerHTML = '<i class="bi bi-pause-fill"></i>';
            } else {
                this._currentAudio.pause();
                if (btn) btn.innerHTML = '<i class="bi bi-play-fill"></i>';
            }
            return;
        }

        // Stop any currently playing audio
        if (this._currentAudio) {
            this._currentAudio.pause();
            this._currentAudio.currentTime = 0;
            // Reset the previous button
            const prevBtn = document.querySelector(`[data-message-id="${this._currentAudioMessageId}"] .voice-play-btn`);
            if (prevBtn) prevBtn.innerHTML = '<i class="bi bi-play-fill"></i>';
            const prevProgress = document.getElementById(`voiceProgress_${this._currentAudioMessageId}`);
            if (prevProgress) prevProgress.style.width = '0';
        }

        // Create new audio instance
        const audio = new Audio(url);
        this._currentAudio = audio;
        this._currentAudioMessageId = messageId;

        audio.onplay = () => {
            if (btn) btn.innerHTML = '<i class="bi bi-pause-fill"></i>';
        };

        audio.onpause = () => {
            if (btn) btn.innerHTML = '<i class="bi bi-play-fill"></i>';
        };

        audio.onended = () => {
            if (btn) btn.innerHTML = '<i class="bi bi-play-fill"></i>';
            if (progressEl) progressEl.style.width = '0';
            this._currentAudio = null;
            this._currentAudioMessageId = null;
        };

        audio.ontimeupdate = () => {
            if (progressEl && audio.duration) {
                const percent = (audio.currentTime / audio.duration) * 100;
                progressEl.style.width = percent + '%';
            }
        };

        audio.onerror = (e) => {
            console.error('[Messaging] Audio load error:', e, 'URL:', url);
            if (btn) btn.innerHTML = '<i class="bi bi-play-fill"></i>';
            this._currentAudio = null;
            this._currentAudioMessageId = null;
            this._showToast('Error', 'Failed to load audio', 'error');
        };

        audio.play().catch(err => {
            console.error('[Messaging] Audio playback error:', err);
            if (btn) btn.innerHTML = '<i class="bi bi-play-fill"></i>';
            this._showToast('Error', 'Failed to play audio', 'error');
        });
    }

    downloadFile(url, fileName) {
        if (!url) return;

        const a = document.createElement('a');
        a.href = url;
        a.download = fileName || 'download';
        a.target = '_blank';
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
    }

    // ========================================
    // Utilities
    // ========================================

    _filterConversations(query) {
        const items = document.querySelectorAll('.conversation-item');
        const lowerQuery = query.toLowerCase();

        items.forEach(item => {
            const name = item.querySelector('.conversation-name')?.textContent?.toLowerCase() || '';
            item.style.display = name.includes(lowerQuery) ? '' : 'none';
        });
    }

    _handleScroll() {
        const container = document.getElementById('messagesContainer');
        if (!container || !this.currentConversation) return;

        // Load older messages when scrolled to top
        if (container.scrollTop === 0 && this.currentMessages.length > 0) {
            const oldestId = this.currentMessages[0]?.MessageId;
            if (oldestId) {
                this.loadMessages(oldestId);
            }
        }
    }

    _triggerFileInput() {
        const fileInput = document.getElementById('messageFileInput');
        if (fileInput) fileInput.click();
    }

    _getCurrentUserId() {
        // Get from JWT token stored in localStorage (check both keys)
        const token = localStorage.getItem('authToken') || localStorage.getItem('token');
        if (!token) return null;

        try {
            const payload = JSON.parse(atob(token.split('.')[1]));
            return parseInt(payload.sub || payload.nameid);
        } catch {
            return null;
        }
    }

    _formatTimeAgo(dateString) {
        const date = new Date(dateString);
        const now = new Date();
        const diff = Math.floor((now - date) / 1000);

        if (diff < 60) return 'Just now';
        if (diff < 3600) return Math.floor(diff / 60) + 'm';
        if (diff < 86400) return Math.floor(diff / 3600) + 'h';
        if (diff < 604800) return Math.floor(diff / 86400) + 'd';
        return date.toLocaleDateString();
    }

    _formatDuration(seconds) {
        const mins = Math.floor(seconds / 60);
        const secs = Math.floor(seconds % 60);
        return `${mins}:${secs.toString().padStart(2, '0')}`;
    }

    _formatFileSize(bytes) {
        if (!bytes) return '';
        const sizes = ['B', 'KB', 'MB', 'GB'];
        let i = 0;
        let size = bytes;
        while (size >= 1024 && i < sizes.length - 1) {
            size /= 1024;
            i++;
        }
        return `${size.toFixed(1)} ${sizes[i]}`;
    }

    _getFileIcon(mimeType) {
        if (!mimeType) return 'bi-file-earmark';
        if (mimeType.startsWith('image/')) return 'bi-file-earmark-image';
        if (mimeType.includes('pdf')) return 'bi-file-earmark-pdf';
        if (mimeType.includes('word')) return 'bi-file-earmark-word';
        if (mimeType.includes('excel') || mimeType.includes('spreadsheet')) return 'bi-file-earmark-excel';
        return 'bi-file-earmark';
    }

    _escape(str) {
        if (!str) return '';
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }

    _showToast(title, message, type = 'info') {
        if (window.showToast) {
            window.showToast(title, message, type);
        } else {
            console.log(`[${type}] ${title}: ${message}`);
        }
    }

    /**
     * Show a subtle banner notification for new messages
     * Auto-dismisses after 4 seconds, clickable to open the conversation
     */
    _showMessageNotification(senderName, preview, conversationId) {
        // Clean mention tokens from preview text
        if (typeof PatientMentionHelper !== 'undefined') {
            preview = PatientMentionHelper.cleanPreviewText(preview);
        }

        // Remove any existing notification
        const existing = document.getElementById('messagingNotificationBanner');
        if (existing) existing.remove();

        // Create notification banner
        const banner = document.createElement('div');
        banner.id = 'messagingNotificationBanner';
        banner.className = 'messaging-notification-banner';
        banner.innerHTML = `
            <div class="notification-content">
                <i class="bi bi-chat-dots-fill notification-icon"></i>
                <div class="notification-text">
                    <strong>${this._escape(senderName)}</strong>
                    <span>${this._escape(preview.length > 50 ? preview.substring(0, 47) + '...' : preview)}</span>
                </div>
                <button class="notification-close" aria-label="Close">
                    <i class="bi bi-x"></i>
                </button>
            </div>
        `;

        // Add click handler to open conversation
        banner.querySelector('.notification-content').addEventListener('click', (e) => {
            if (!e.target.closest('.notification-close')) {
                this.openChat();
                if (conversationId) {
                    setTimeout(() => this.openConversation(conversationId), 300);
                }
                banner.remove();
            }
        });

        // Add close button handler
        banner.querySelector('.notification-close').addEventListener('click', () => {
            banner.classList.add('hiding');
            setTimeout(() => banner.remove(), 300);
        });

        // Add to page
        document.body.appendChild(banner);

        // Trigger animation
        requestAnimationFrame(() => {
            banner.classList.add('show');
        });

        // Auto-dismiss after 4 seconds
        setTimeout(() => {
            if (banner.parentNode) {
                banner.classList.add('hiding');
                setTimeout(() => banner.remove(), 300);
            }
        }, 4000);
    }

    // ========================================
    // API Helpers (all messaging API calls disable global loader)
    // ========================================

    async _apiGet(endpoint) {
        if (this.api) {
            // Disable global loader for messaging - use inline spinners instead
            return await this.api.get(endpoint, { showLoader: false });
        }

        const response = await fetch(`/api${endpoint}`, {
            headers: this._getHeaders()
        });
        if (!response.ok) throw new Error('API request failed');
        return response.json();
    }

    async _apiPost(endpoint, data) {
        if (this.api) {
            // Disable global loader for messaging - use inline spinners instead
            return await this.api.post(endpoint, data, { showLoader: false });
        }

        const response = await fetch(`/api${endpoint}`, {
            method: 'POST',
            headers: this._getHeaders(),
            body: JSON.stringify(data)
        });
        if (!response.ok) throw new Error('API request failed');
        return response.json();
    }

    async _apiPut(endpoint, data) {
        if (this.api) {
            // Disable global loader for messaging - use inline spinners instead
            return await this.api.put(endpoint, data, { showLoader: false });
        }

        const response = await fetch(`/api${endpoint}`, {
            method: 'PUT',
            headers: this._getHeaders(),
            body: data ? JSON.stringify(data) : null
        });
        if (!response.ok) throw new Error('API request failed');
        return response.status === 204 ? {} : response.json();
    }

    async _apiPostFormData(endpoint, formData) {
        const token = this._getAuthToken();
        const headers = {};
        if (token) {
            headers['Authorization'] = `Bearer ${token}`;
        }
        const response = await fetch(`/api${endpoint}`, {
            method: 'POST',
            headers: headers,
            body: formData
        });
        if (!response.ok) {
            const errorText = await response.text();
            console.error('API Error:', response.status, errorText);
            throw new Error(`API request failed: ${response.status}`);
        }
        return response.json();
    }

    _getAuthToken() {
        // Try multiple token storage keys
        return localStorage.getItem('authToken') ||
               localStorage.getItem('token') ||
               window.App?.auth?.getToken?.();
    }

    _getHeaders() {
        const token = this._getAuthToken();
        return {
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${token}`
        };
    }

    // ========================================
    // Cleanup / Destroy
    // ========================================

    /**
     * Destroy the module and cleanup resources
     * Called when user logs out to stop SignalR connection
     */
    destroy() {
        console.log('[Messaging] Destroying module...');

        // Stop SignalR connection - this triggers OnDisconnectedAsync on server
        // which will update user presence to offline
        if (this.signalRService) {
            this.signalRService.stop();
            this.signalRService = null;
        }

        // Destroy mention helper
        if (this.mentionHelper) {
            this.mentionHelper.destroy();
            this.mentionHelper = null;
        }

        // Clear timeouts
        if (this.typingTimeout) {
            clearTimeout(this.typingTimeout);
            this.typingTimeout = null;
        }

        if (this.voiceNoteTimerInterval) {
            clearInterval(this.voiceNoteTimerInterval);
            this.voiceNoteTimerInterval = null;
        }

        // Stop any playing audio
        if (this._currentAudio) {
            this._currentAudio.pause();
            this._currentAudio = null;
            this._currentAudioMessageId = null;
        }

        // Stop any recording
        if (this.mediaRecorder && this.mediaRecorder.state !== 'inactive') {
            this.mediaRecorder.stop();
            this.mediaRecorder = null;
        }

        // Clear state
        this.conversations = [];
        this.currentConversation = null;
        this.currentMessages = [];
        this.availableUsers = [];
        this.isOpen = false;
        this.isTyping = false;

        // Close chat UI if open
        this.closeChat();

        console.log('[Messaging] Module destroyed');
    }
}

// Global instance
let messagingModule = null;

// Initialize messaging module
function initMessagingModule() {
    messagingModule = new MessagingModule({
        api: window.App?.api,
        eventBus: window.App?.events
    });
    messagingModule.init().catch(err => {
        console.error('[Messaging] Init error:', err);
    });

    // Register with App module manager for proper cleanup on logout
    if (window.App?.modules?.register) {
        window.App.modules.register('messaging', messagingModule);
    }
}

// Initialize when window is fully loaded to ensure all DOM elements are ready
window.addEventListener('load', () => {
    // Small delay to ensure other scripts have initialized
    setTimeout(initMessagingModule, 50);
});
