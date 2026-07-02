/**
 * PatientMessagingProviderModule — Provider-side patient messaging.
 * Shows patient conversations in header badge, opens modal with chat.
 */
class PatientMessagingProviderModule {
    constructor() {
        this.conversations = [];
        this.currentConversation = null;
        this.signalRConnection = null;
        this.isConnected = false;
        this.typingTimeout = null;
    }

    init() {
        console.log('[PatientMessaging] init() called');

        this.token = localStorage.getItem('authToken') || localStorage.getItem('token');
        console.log('[PatientMessaging] token:', this.token ? 'found' : 'NOT FOUND');
        if (!this.token) return;

        const user = JSON.parse(localStorage.getItem('currentUser') || '{}');
        console.log('[PatientMessaging] user:', user);
        const role = parseInt(user.Role ?? user.role);
        console.log('[PatientMessaging] role:', role);

        // ============================================
        // MESSAGING ROLE RESTRICTION
        // All staff roles can VIEW patient messages (document upload notifications, etc.)
        // Currently: Only Providers (Clinician, Role=2) can SEND messages to patients.
        // Future: To allow other roles to send messages, expand sendRoles array.
        // The backend architecture already supports any user role (UserId-based).
        // ============================================
        const viewRoles = [0, 1, 2, 3, 6, 7]; // All staff roles can VIEW messages
        const sendRoles = [2]; // Only Clinicians can SEND — expand this array to add more roles
        if (!viewRoles.includes(role)) {
            console.log('[PatientMessaging] Skipping — role not authorized for patient messaging');
            return;
        }
        this._canSendMessages = sendRoles.includes(role);

        this.apiBase = '/api/patient-messaging';

        this.bindElements();
        console.log('[PatientMessaging] messagesBtn:', this.messagesBtn);

        // Show the button (hidden by default via d-none)
        if (this.messagesBtn) {
            this.messagesBtn.classList.remove('d-none');
            console.log('[PatientMessaging] Button shown');
        } else {
            console.log('[PatientMessaging] Button element NOT FOUND in DOM');
        }

        this.bindEvents();
        this.connectSignalR();
        this.loadUnreadCount();
        this._listenLocationChange();
    }

    bindElements() {
        this.messagesBtn = document.getElementById('patientMessagesBtn');
        this.messagesBadge = document.getElementById('patientMessagesBadge');
        this.modal = document.getElementById('providerPatientMsgModal');
        this.conversationsView = document.getElementById('ppConversationsView');
        this.chatView = document.getElementById('ppChatView');
        this.conversationsList = document.getElementById('ppConversationsList');
        this.conversationsEmpty = document.getElementById('ppConversationsEmpty');
        this.chatMessages = document.getElementById('ppChatMessages');
        this.chatPatientName = document.getElementById('ppChatPatientName');
        this.messageInput = document.getElementById('ppMessageInput');
        this.sendBtn = document.getElementById('ppSendBtn');
        this.typingIndicator = document.getElementById('ppTypingIndicator');
    }

    bindEvents() {
        this.messagesBtn?.addEventListener('click', () => this.openModal());

        document.getElementById('ppBackFromChat')?.addEventListener('click', () => this.showConversations());

        this.sendBtn?.addEventListener('click', () => this.sendMessage());
        this.messageInput?.addEventListener('keydown', (e) => {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                this.sendMessage();
            }
        });

        this.messageInput?.addEventListener('input', () => {
            this.sendBtn.disabled = !this.messageInput.value.trim();
            this.sendTypingIndicator(true);
            // Auto-resize
            this.messageInput.style.height = 'auto';
            this.messageInput.style.height = Math.min(this.messageInput.scrollHeight, 100) + 'px';
        });

        // Search patients
        const searchInput = document.getElementById('ppPatientSearch');
        let searchDebounce = null;
        searchInput?.addEventListener('input', () => {
            clearTimeout(searchDebounce);
            searchDebounce = setTimeout(() => {
                const query = searchInput.value.trim();
                if (query.length >= 2 || query.length === 0) {
                    this.searchPatients(query);
                }
            }, 300);
        });

        this.modal?.addEventListener('hidden.bs.modal', () => {
            if (this.currentConversation) {
                this.leaveConversation(this.currentConversation.patientConversationId);
                this.currentConversation = null;
            }
        });
    }

    // Refresh patient list when user switches location
    _listenLocationChange() {
        this._currentLocationId = this._getLocationId();
        // Listen for App state changes (location switch)
        if (window.App?.state?.on) {
            App.state.on('locationId', () => {
                const newLoc = this._getLocationId();
                if (newLoc !== this._currentLocationId) {
                    this._currentLocationId = newLoc;
                    // Clear cached data so next open fetches fresh
                    this.allPatients = [];
                    // If modal is open, refresh immediately
                    if (this.modal?.classList.contains('show')) {
                        this.loadConversations();
                    }
                }
            });
        }
    }

    // ============================================
    // SignalR
    // ============================================

    async connectSignalR() {
        if (!window.signalR) return;

        this.signalRConnection = new signalR.HubConnectionBuilder()
            .withUrl('/hubs/patient-messaging', {
                accessTokenFactory: () => this.token
            })
            .withAutomaticReconnect([1000, 2000, 5000, 10000, 30000])
            .configureLogging(signalR.LogLevel.Warning)
            .build();

        this.signalRConnection.on('PatientMsgReceive', (message) => {
            const convId = message.PatientConversationId ?? message.patientConversationId;
            const curId = this.currentConversation?.PatientConversationId ?? this.currentConversation?.patientConversationId;
            if (this.currentConversation && convId === curId) {
                this.appendMessage(message);
                this.scrollToBottom();
                this.markAsRead(curId);
            }
        });

        this.signalRConnection.on('PatientMsgNew', (notification) => {
            if (!this.chatView?.classList.contains('d-none')) {
                // In chat view — message already handled by PatientMsgReceive
            } else if (this.modal && this.modal.classList.contains('show')) {
                // Modal open on conversations list — refresh
                this.loadConversations();
            }
        });

        this.signalRConnection.on('PatientMsgUnreadUpdate', (unread) => {
            this.updateBadge(unread.TotalUnreadCount ?? unread.totalUnreadCount ?? 0);
        });

        this.signalRConnection.on('PatientMsgTyping', (data) => {
            const convId = data.ConversationId ?? data.conversationId;
            const curId = this.currentConversation?.PatientConversationId ?? this.currentConversation?.patientConversationId;
            if (this.currentConversation && convId === curId) {
                const typing = data.IsTyping ?? data.isTyping;
                this.typingIndicator?.classList.toggle('d-none', !typing);
            }
        });

        this.signalRConnection.on('PatientMsgRead', () => { });

        try {
            await this.signalRConnection.start();
            this.isConnected = true;
        } catch (err) {
            console.warn('Patient messaging SignalR failed:', err);
        }

        this.signalRConnection.onreconnected(() => { this.isConnected = true; });
        this.signalRConnection.onclose(() => { this.isConnected = false; });
    }

    async joinConversation(id) {
        if (this.isConnected) {
            try { await this.signalRConnection.invoke('JoinConversation', id); } catch (e) { }
        }
    }

    async leaveConversation(id) {
        if (this.isConnected) {
            try { await this.signalRConnection.invoke('LeaveConversation', id); } catch (e) { }
        }
    }

    sendTypingIndicator(isTyping) {
        if (!this.currentConversation || !this.isConnected) return;
        clearTimeout(this.typingTimeout);
        try {
            const curId = this.currentConversation.PatientConversationId ?? this.currentConversation.patientConversationId;
            this.signalRConnection.invoke('SendTypingIndicator', curId, isTyping);
        } catch (e) { }
        if (isTyping) {
            this.typingTimeout = setTimeout(() => this.sendTypingIndicator(false), 3000);
        }
    }

    // ============================================
    // API
    // ============================================

    async apiFetch(url, options = {}) {
        const resp = await fetch(url, {
            ...options,
            headers: {
                'Content-Type': 'application/json',
                'Authorization': `Bearer ${this.token}`,
                ...(options.headers || {})
            }
        });
        if (!resp.ok) throw new Error(`API error: ${resp.status}`);
        const text = await resp.text();
        return text ? JSON.parse(text) : null;
    }

    async loadUnreadCount() {
        try {
            const data = await this.apiFetch(`${this.apiBase}/unread`);
            this.updateBadge(data.TotalUnreadCount ?? data.totalUnreadCount ?? 0);
        } catch (e) { }
    }

    _getLocationId() {
        // Get current location from App state or localStorage
        try {
            if (window.App?.state?.get) return App.state.get('locationId') || '';
        } catch (e) { }
        try {
            const user = JSON.parse(localStorage.getItem('currentUser') || '{}');
            return user.LocationId ?? user.locationId ?? '';
        } catch (e) { }
        return '';
    }

    async loadConversations() {
        try {
            // Load all patients (WhatsApp style) filtered by location
            const locationId = this._getLocationId();
            const result = await this.apiFetch(`${this.apiBase}/patients?skip=0&take=100${locationId ? `&locationId=${locationId}` : ''}`);
            this.allPatients = result.Items ?? result.items ?? [];
            this.renderConversations();
        } catch (e) {
            console.error('Failed to load patient conversations:', e);
        }
    }

    async loadMessages(conversationId) {
        try {
            const messages = await this.apiFetch(`${this.apiBase}/conversations/${conversationId}/messages`);
            this.renderMessages(messages);
            this.scrollToBottom();
        } catch (e) {
            console.error('Failed to load messages:', e);
        }
    }

    async sendMessage() {
        const text = this.messageInput?.value?.trim();
        if (!text || !this.currentConversation) return;

        this.sendBtn.disabled = true;
        this.messageInput.value = '';
        this.messageInput.style.height = 'auto';
        this.sendTypingIndicator(false);

        try {
            const curId = this.currentConversation.PatientConversationId ?? this.currentConversation.patientConversationId;
            await this.apiFetch(`${this.apiBase}/messages`, {
                method: 'POST',
                body: JSON.stringify({
                    ConversationId: curId,
                    MessageText: text
                })
            });
        } catch (e) {
            console.error('Failed to send message:', e);
            this.messageInput.value = text;
            if (window.Toast) Toast.error('Failed to send message');
        }

        this.sendBtn.disabled = !this.messageInput.value.trim();
    }

    async markAsRead(conversationId) {
        try {
            await this.apiFetch(`${this.apiBase}/conversations/${conversationId}/read`, { method: 'PUT' });
            if (this.isConnected) {
                try { await this.signalRConnection.invoke('MarkAsRead', conversationId); } catch (e) { }
            }
            // Refresh badge count
            await this.loadUnreadCount();
        } catch (e) { }
    }

    // ============================================
    // UI
    // ============================================

    openModal() {
        this.showConversations();
        this.loadConversations();
        const bsModal = new bootstrap.Modal(this.modal);
        bsModal.show();
    }

    showConversations() {
        if (this.currentConversation) {
            const curId = this.currentConversation.PatientConversationId ?? this.currentConversation.patientConversationId;
            this.leaveConversation(curId);
            this.currentConversation = null;
        }
        this.conversationsView?.classList.remove('d-none');
        this.chatView?.classList.add('d-none');
    }

    async openChat(conversation) {
        this.currentConversation = conversation;
        this.conversationsView?.classList.add('d-none');
        this.chatView?.classList.remove('d-none');

        const convId = conversation.PatientConversationId ?? conversation.patientConversationId;
        this.chatPatientName.textContent = conversation.PatientName ?? conversation.patientName ?? 'Patient';
        this.chatMessages.innerHTML = '';
        this.messageInput.value = '';
        this.sendBtn.disabled = true;

        // Hide entire input area for non-sender roles (view-only)
        const inputArea = this.messageInput?.closest('.d-flex, .input-group, div');
        if (inputArea && inputArea.contains(this.sendBtn)) {
            inputArea.style.display = this._canSendMessages ? '' : 'none';
        } else {
            // Fallback: hide individual elements
            if (this.messageInput) this.messageInput.style.display = this._canSendMessages ? '' : 'none';
            if (this.sendBtn) this.sendBtn.style.display = this._canSendMessages ? '' : 'none';
        }

        await this.joinConversation(convId);
        await this.loadMessages(convId);

        const unread = conversation.UnreadCount ?? conversation.unreadCount ?? 0;
        if (unread > 0) {
            await this.markAsRead(convId);
        }

        if (this._canSendMessages) this.messageInput.focus();
    }

    renderConversations() {
        const items = this.allPatients || [];
        this.conversationsList.querySelectorAll('.pp-conversation-item, .pp-section-header').forEach(el => el.remove());

        if (!items.length) {
            this.conversationsEmpty?.classList.remove('d-none');
            return;
        }
        this.conversationsEmpty?.classList.add('d-none');

        // Split into patients with messages and those without
        const withMessages = items.filter(p => p.HasConversation || p.hasConversation);
        const withoutMessages = items.filter(p => !(p.HasConversation || p.hasConversation));

        // Section: Recent Messages
        if (withMessages.length) {
            const header = document.createElement('div');
            header.className = 'pp-section-header';
            header.style.cssText = 'padding:4px 16px;font-size:10px;font-weight:700;color:#9CA3AF;text-transform:uppercase;letter-spacing:0.8px;background:#f9fafb;border-bottom:1px solid #e5e7eb;';
            header.textContent = 'Recent Messages';
            this.conversationsList.appendChild(header);
            withMessages.forEach(p => this._renderPatientItem(p, true));
        }

        // Section: All Patients — only shown to users who can initiate conversations (Providers)
        if (withoutMessages.length && this._canSendMessages) {
            const header = document.createElement('div');
            header.className = 'pp-section-header';
            header.style.cssText = 'padding:4px 16px;font-size:10px;font-weight:700;color:#9CA3AF;text-transform:uppercase;letter-spacing:0.8px;background:#f9fafb;border-bottom:1px solid #e5e7eb;';
            header.textContent = 'All Patients';
            this.conversationsList.appendChild(header);
            withoutMessages.forEach(p => this._renderPatientItem(p, false));
        }
    }

    _renderPatientItem(patient, hasConversation) {
        const el = document.createElement('div');
        el.className = 'pp-conversation-item d-flex align-items-center p-3 border-bottom';
        el.style.cursor = 'pointer';
        const unread = patient.UnreadCount ?? patient.unreadCount ?? 0;
        if (unread > 0) el.style.backgroundColor = '#f0f7ff';

        const name = patient.PatientName ?? patient.patientName ?? 'Patient';
        const mrn = patient.Mrn ?? patient.mrn ?? '';
        const dob = patient.DateOfBirth ?? patient.dateOfBirth ?? '';
        const locationName = patient.LocationName ?? patient.locationName ?? '';
        const docCount = patient.PatientDocumentCount ?? patient.patientDocumentCount ?? 0;
        const lastMsg = patient.LastMessageText ?? patient.lastMessageText ?? '';
        const lastTime = patient.LastMessageAt ?? patient.lastMessageAt;
        const lastSenderType = patient.LastMessageSenderType ?? patient.lastMessageSenderType ?? '';
        const timeStr = lastTime ? this.formatTime(lastTime) : '';

        // Location badge
        const locationBadge = locationName ? `<span style="display:inline-flex;align-items:center;gap:2px;background:#F3F4F6;color:#6B7280;font-size:10px;font-weight:500;padding:1px 6px;border-radius:8px;margin-left:4px;"><i class="bi bi-geo-alt" style="font-size:9px;"></i>${this.escapeHtml(locationName)}</span>` : '';

        // Format preview — detect system doc upload messages
        let preview = '';
        if (hasConversation && lastMsg) {
            if (lastMsg.startsWith('[DOC_UPLOAD]')) {
                const parts = lastMsg.replace('[DOC_UPLOAD]', '').split('|');
                preview = `<span style="color:#1D4ED8;"><i class="bi bi-file-earmark-arrow-up"></i> Uploaded: ${this.escapeHtml(parts[0])}</span>`;
            } else {
                preview = this.escapeHtml(lastMsg);
            }
        } else if (!hasConversation) {
            preview = `<span style="color:#9CA3AF;">MRN: ${this.escapeHtml(mrn)} · DOB: ${dob}</span>`;
        }

        // Doc badge
        const docBadge = docCount > 0 ? `<span style="display:inline-flex;align-items:center;gap:3px;background:#DBEAFE;color:#1D4ED8;font-size:10px;font-weight:600;padding:2px 7px;border-radius:10px;margin-left:6px;"><i class="bi bi-file-earmark-arrow-up"></i>${docCount} doc${docCount > 1 ? 's' : ''}</span>` : '';

        el.innerHTML = `
            <div class="flex-shrink-0 me-3">
                <div class="rounded-circle bg-info text-white d-flex align-items-center justify-content-center" style="width: 40px; height: 40px; font-size: 0.85rem;">
                    ${this.getInitials(name)}
                </div>
            </div>
            <div class="flex-grow-1 min-width-0">
                <div class="d-flex justify-content-between align-items-center">
                    <div class="d-flex align-items-center flex-wrap">
                        <strong class="text-truncate" style="font-size: 0.9rem;">${this.escapeHtml(name)}</strong>
                        ${locationBadge}${docBadge}
                    </div>
                    <small class="${unread > 0 ? 'text-primary fw-bold' : 'text-muted'} ms-2 flex-shrink-0">${timeStr}</small>
                </div>
                <div class="d-flex justify-content-between">
                    <small class="text-muted text-truncate">${preview}</small>
                    ${unread > 0 ? `<span class="badge bg-primary rounded-pill ms-2 flex-shrink-0">${unread}</span>` : ''}
                </div>
            </div>
        `;

        el.addEventListener('click', () => {
            if (hasConversation) {
                // Open existing conversation
                const convId = patient.ConversationId ?? patient.conversationId;
                this.openChat({
                    PatientConversationId: convId,
                    PatientName: name,
                    PatientId: patient.PatientId ?? patient.patientId,
                    UnreadCount: unread
                });
            } else if (this._canSendMessages) {
                // Start new conversation — only providers can initiate
                this.startNewChat(patient.PatientId ?? patient.patientId, name);
            }
        });

        this.conversationsList.appendChild(el);
    }

    async startNewChat(patientId, patientName) {
        try {
            const conv = await this.apiFetch(`${this.apiBase}/conversations/create`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ PatientId: patientId })
            });
            if (conv) {
                this.openChat({
                    PatientConversationId: conv.PatientConversationId ?? conv.patientConversationId,
                    PatientName: patientName,
                    PatientId: patientId,
                    UnreadCount: 0
                });
            }
        } catch (e) {
            console.error('Failed to start new chat:', e);
        }
    }

    renderMessages(messages) {
        this.chatMessages.innerHTML = '';
        messages.forEach(msg => this.appendMessage(msg));
    }

    appendMessage(msg) {
        const senderType = msg.SenderType ?? msg.senderType;
        const text = msg.MessageText ?? msg.messageText ?? '';
        const time = msg.CreatedAt ?? msg.createdAt ?? '';
        const el = document.createElement('div');

        // System message (document upload notification)
        if (senderType === 'System' && text.startsWith('[DOC_UPLOAD]')) {
            const parts = text.replace('[DOC_UPLOAD]', '').split('|');
            const fileName = parts[0] || 'Document';
            const fileSize = parts[1] ? this._formatFileSize(parseInt(parts[1])) : '';
            const docId = parts[2] || '';
            const docUrl = parts[3] || '#';

            const fileIcon = fileName.toLowerCase().endsWith('.pdf') ? 'bi-file-earmark-pdf' : fileName.match(/\.(jpg|jpeg|png|gif|webp)$/i) ? 'bi-file-earmark-image' : 'bi-file-earmark';
            const iconBg = fileName.toLowerCase().endsWith('.pdf') ? '#FEE2E2' : fileName.match(/\.(jpg|jpeg|png|gif|webp)$/i) ? '#DBEAFE' : '#F3F4F6';
            const iconColor = fileName.toLowerCase().endsWith('.pdf') ? '#DC2626' : fileName.match(/\.(jpg|jpeg|png|gif|webp)$/i) ? '#2563EB' : '#6B7280';

            el.className = 'd-flex justify-content-center mb-3';
            el.innerHTML = `
                <div style="background:#FEF3C7;border:1px solid #FDE68A;border-radius:10px;padding:10px 16px;text-align:center;max-width:90%;">
                    <div style="font-size:11px;color:#92400E;margin-bottom:6px;">
                        <i class="bi bi-cloud-arrow-up me-1"></i>Patient uploaded a document
                    </div>
                    <a href="javascript:void(0)" onclick="FileViewerModal.showFromFetch({fetchUrl:'${docUrl}',fileName:'${this.escapeHtml(fileName).replace(/'/g, "\\'")}'});" style="display:inline-flex;align-items:center;gap:6px;background:#fff;border:1px solid #E5E7EB;border-radius:8px;padding:6px 12px;text-decoration:none;color:#1D4ED8;font-weight:500;font-size:12px;cursor:pointer;">
                        <div style="width:28px;height:28px;border-radius:6px;background:${iconBg};color:${iconColor};display:flex;align-items:center;justify-content:center;font-size:14px;">
                            <i class="bi ${fileIcon}"></i>
                        </div>
                        <div style="text-align:left;">
                            <div style="font-size:12px;font-weight:600;">${this.escapeHtml(fileName)}</div>
                            <div style="font-size:10px;color:#6B7280;font-weight:400;">${fileSize} · Click to view</div>
                        </div>
                    </a>
                    <div style="font-size:10px;color:#6B7280;margin-top:6px;"><i class="bi bi-check-circle me-1" style="color:#059669;"></i>Automatically saved in patient's Attachments</div>
                    <div style="font-size:10px;color:#92400E;margin-top:2px;text-align:right;">${this.formatTime(time)}</div>
                </div>
            `;
        } else {
            const isMe = senderType === 'Provider';
            el.className = `d-flex mb-2 ${isMe ? 'justify-content-end' : 'justify-content-start'}`;
            const bubbleColor = isMe ? 'bg-primary text-white' : 'bg-white border';
            const timeColor = isMe ? 'text-white-50' : 'text-muted';

            el.innerHTML = `
                <div class="${bubbleColor} rounded-3 px-3 py-2" style="max-width: 75%; word-wrap: break-word;">
                    <div style="font-size: 0.9rem; white-space: pre-wrap;">${this.escapeHtml(text)}</div>
                    <div class="${timeColor}" style="font-size: 0.7rem; text-align: right;">${this.formatTime(time)}</div>
                </div>
            `;
        }

        this.chatMessages.appendChild(el);
    }

    async searchPatients(query) {
        try {
            const locationId = this._getLocationId();
            let url = `${this.apiBase}/patients?skip=0&take=100`;
            if (query) url += `&search=${encodeURIComponent(query)}`;
            if (locationId) url += `&locationId=${locationId}`;
            const result = await this.apiFetch(url);
            this.allPatients = result.Items ?? result.items ?? [];
            this.renderConversations();
        } catch (e) {
            console.error('Search failed:', e);
        }
    }

    _formatFileSize(bytes) {
        if (!bytes || isNaN(bytes)) return '';
        if (bytes < 1024) return bytes + ' B';
        if (bytes < 1048576) return (bytes / 1024).toFixed(0) + ' KB';
        return (bytes / 1048576).toFixed(1) + ' MB';
    }

    scrollToBottom() {
        if (this.chatMessages) {
            setTimeout(() => {
                this.chatMessages.scrollTop = this.chatMessages.scrollHeight;
            }, 50);
        }
    }

    updateBadge(count) {
        if (this.messagesBadge) {
            this.messagesBadge.textContent = count;
            this.messagesBadge.classList.toggle('d-none', count <= 0);
        }
    }

    // ============================================
    // Helpers
    // ============================================

    getInitials(name) {
        if (!name) return '?';
        return name.split(' ').filter(Boolean).map(w => w[0]).join('').substring(0, 2).toUpperCase();
    }

    escapeHtml(text) {
        if (!text) return '';
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    formatTime(dateStr) {
        if (!dateStr) return '';
        const date = new Date(dateStr.endsWith('Z') ? dateStr : dateStr + 'Z');
        return date.toLocaleString('en-US', { month: 'short', day: 'numeric', year: 'numeric', hour: 'numeric', minute: '2-digit', hour12: true });
    }
}

// Auto-initialize
document.addEventListener('DOMContentLoaded', () => {
    window._patientMessagingProviderModule = new PatientMessagingProviderModule();
    window._patientMessagingProviderModule.init();
});
