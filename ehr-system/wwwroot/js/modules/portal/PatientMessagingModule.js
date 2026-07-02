/**
 * PatientMessagingModule — Patient portal messaging with providers.
 * Single list view: shows all providers (from appointments) with conversation preview if exists.
 * Click any provider to open/start chat.
 */
class PatientMessagingModule {
    constructor() {
        this.providers = [];
        this.conversations = [];
        this.currentConversation = null;
        this.signalRConnection = null;
        this.isConnected = false;
        this.typingTimeout = null;
    }

    init() {
        const token = localStorage.getItem('portalAuthToken');
        if (!token) return;

        this.token = token;
        this.apiBase = '/api/portal/messaging';

        this.bindElements();
        this.bindEvents();
        this.connectSignalR();
        this.loadUnreadCount();
        this.checkAutoOpenChat();
    }

    bindElements() {
        this.messagesBtn = document.getElementById('portalMessagesBtn');
        this.messagesBadge = document.getElementById('portalMessagesBadge');
        this.messagesBadgeMobile = document.getElementById('portalMessagesBadgeMobile');
        this.messagesMobileBtn = document.getElementById('portalMessagesMobileBtn');
        this.modal = document.getElementById('patientMessagingModal');
        this.conversationsView = document.getElementById('pmConversationsView');
        this.chatView = document.getElementById('pmChatView');
        this.conversationsList = document.getElementById('pmConversationsList');
        this.loadingEl = document.getElementById('pmConversationsLoading');
        this.chatMessages = document.getElementById('pmChatMessages');
        this.chatProviderName = document.getElementById('pmChatProviderName');
        this.chatProviderSpecialty = document.getElementById('pmChatProviderSpecialty');
        this.messageInput = document.getElementById('pmMessageInput');
        this.sendBtn = document.getElementById('pmSendBtn');
        this.typingIndicator = document.getElementById('pmTypingIndicator');
    }

    bindEvents() {
        this.messagesBtn?.addEventListener('click', () => this.openModal());
        this.messagesMobileBtn?.addEventListener('click', () => this.openModal());

        document.getElementById('pmBackFromChat')?.addEventListener('click', () => this.showList());

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
            this.messageInput.style.height = 'auto';
            this.messageInput.style.height = Math.min(this.messageInput.scrollHeight, 100) + 'px';
        });

        this.modal?.addEventListener('hidden.bs.modal', () => {
            if (this.currentConversation) {
                this.leaveConversation(this.currentConversation.patientConversationId);
                this.currentConversation = null;
            }
        });
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
            if (this.currentConversation && convId === this.currentConversation.PatientConversationId) {
                this.appendMessage(message);
                this.scrollToBottom();
                this.markAsRead(this.currentConversation.PatientConversationId);
            }
        });

        this.signalRConnection.on('PatientMsgNew', () => {
            if (this.chatView?.classList.contains('d-none')) {
                this.loadProviderList();
            }
        });

        this.signalRConnection.on('PatientMsgUnreadUpdate', (unread) => {
            this.updateBadge(unread.TotalUnreadCount ?? unread.totalUnreadCount);
        });

        this.signalRConnection.on('PatientMsgTyping', (data) => {
            const convId = data.ConversationId ?? data.conversationId;
            if (this.currentConversation && convId === this.currentConversation.PatientConversationId) {
                this.typingIndicator?.classList.toggle('d-none', !(data.IsTyping ?? data.isTyping));
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
            this.signalRConnection.invoke('SendTypingIndicator', this.currentConversation.patientConversationId, isTyping);
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

    async loadProviderList() {
        try {
            const [providers, conversations] = await Promise.all([
                this.apiFetch(`${this.apiBase}/providers`),
                this.apiFetch(`${this.apiBase}/conversations`)
            ]);
            this.providers = providers || [];
            this.conversations = conversations || [];
            this.renderList();
        } catch (e) {
            console.error('Failed to load provider list:', e);
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
            const convId = this.currentConversation.PatientConversationId ?? this.currentConversation.patientConversationId;
            await this.apiFetch(`${this.apiBase}/messages`, {
                method: 'POST',
                body: JSON.stringify({
                    ConversationId: convId,
                    MessageText: text
                })
            });
        } catch (e) {
            console.error('Failed to send message:', e);
            this.messageInput.value = text;
            this.showToast('Failed to send message', 'error');
        }

        this.sendBtn.disabled = !this.messageInput.value.trim();
    }

    async markAsRead(conversationId) {
        try {
            await this.apiFetch(`${this.apiBase}/conversations/${conversationId}/read`, { method: 'PUT' });
            if (this.isConnected) {
                try { await this.signalRConnection.invoke('MarkAsRead', conversationId); } catch (e) { }
            }
            await this.loadUnreadCount();
        } catch (e) { }
    }

    async startOrOpenChat(userId) {
        try {
            const conversation = await this.apiFetch(`${this.apiBase}/conversations/${userId}`, { method: 'POST' });
            this.openChat(conversation);
        } catch (e) {
            console.error('Failed to start conversation:', e);
            this.showToast('Cannot start conversation', 'error');
        }
    }

    // ============================================
    // UI
    // ============================================

    openModal() {
        this.showList();
        this.loadProviderList();
        const bsModal = new bootstrap.Modal(this.modal);
        bsModal.show();
    }

    showList() {
        if (this.currentConversation) {
            const convId = this.currentConversation.PatientConversationId ?? this.currentConversation.patientConversationId;
            this.leaveConversation(convId);
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
        const userName = conversation.UserName ?? conversation.userName ?? conversation.ProviderName ?? conversation.providerName ?? '';
        const userRole = conversation.UserRole ?? conversation.userRole ?? conversation.ProviderRole ?? conversation.providerRole;
        const roleLabel = conversation.UserRoleLabel ?? conversation.userRoleLabel ?? this.getRoleLabel(userRole);
        this.chatProviderName.textContent = roleLabel ? `${userName} (${roleLabel})` : userName;
        this.chatProviderSpecialty.textContent = conversation.UserSpecialty ?? conversation.userSpecialty ?? conversation.ProviderSpecialty ?? conversation.providerSpecialty ?? '';
        this.chatMessages.innerHTML = '';
        this.messageInput.value = '';
        this.sendBtn.disabled = true;

        await this.joinConversation(convId);
        await this.loadMessages(convId);

        const unread = conversation.UnreadCount ?? conversation.unreadCount ?? 0;
        if (unread > 0) {
            await this.markAsRead(convId);
        }

        this.messageInput.focus();
    }

    renderList() {
        if (this.loadingEl) this.loadingEl.classList.add('d-none');

        // Clear old items
        this.conversationsList.querySelectorAll('.pm-list-item, .pm-section-header').forEach(el => el.remove());

        if (!this.providers.length && !this.conversations.length) {
            const empty = document.createElement('div');
            empty.className = 'pm-list-item text-center text-muted py-4';
            empty.innerHTML = `
                <i class="bi bi-chat-dots" style="font-size: 2rem;"></i>
                <p class="mt-2 mb-0">No messages yet</p>
                <small>Your conversations with clinic staff will appear here</small>
            `;
            this.conversationsList.appendChild(empty);
            return;
        }

        // Track which userIds have conversations
        const convUserIds = new Set();
        this.conversations.forEach(c => convUserIds.add(c.UserId));

        // Section 1: Existing conversations (sorted by unread first, then time)
        const sortedConvs = [...this.conversations].sort((a, b) => {
            const unreadA = a.UnreadCount || 0;
            const unreadB = b.UnreadCount || 0;
            if (unreadB !== unreadA) return unreadB - unreadA;
            const timeA = a.LastMessageAt ? new Date(a.LastMessageAt).getTime() : 0;
            const timeB = b.LastMessageAt ? new Date(b.LastMessageAt).getTime() : 0;
            return timeB - timeA;
        });

        if (sortedConvs.length) {
            sortedConvs.forEach(conv => {
                this._renderConversationItem(conv);
            });
        }

        // Section 2: Providers from appointments who DON'T have a conversation yet
        const newProviders = this.providers.filter(p => !convUserIds.has(p.UserId));
        if (newProviders.length) {
            const header = document.createElement('div');
            header.className = 'pm-section-header';
            header.style.cssText = 'padding:6px 16px;font-size:10px;font-weight:700;color:#9CA3AF;text-transform:uppercase;letter-spacing:0.8px;background:#f9fafb;border-bottom:1px solid #e5e7eb;';
            header.textContent = 'Start New Conversation';
            this.conversationsList.appendChild(header);

            newProviders.forEach(provider => {
                this._renderNewProviderItem(provider);
            });
        }
    }

    _renderConversationItem(conv) {
        const el = document.createElement('div');
        el.className = 'pm-list-item d-flex align-items-center p-3 border-bottom';
        el.style.cursor = 'pointer';
        const unread = conv.UnreadCount || 0;
        if (unread > 0) el.style.backgroundColor = '#f0f7ff';

        const name = conv.UserName || '';
        const roleLabel = conv.UserRoleLabel || this.getRoleLabel(conv.UserRole);
        const specialty = conv.UserSpecialty || '';
        const subtitle = [roleLabel, specialty].filter(Boolean).join(' · ');
        const timeStr = conv.LastMessageAt ? this.formatTime(conv.LastMessageAt) : '';
        const preview = conv.LastMessageText || '';

        el.innerHTML = `
            <div class="flex-shrink-0 me-3">
                <div class="rounded-circle bg-primary text-white d-flex align-items-center justify-content-center" style="width: 45px; height: 45px; font-size: 0.85rem;">
                    ${this.getInitials(name)}
                </div>
            </div>
            <div class="flex-grow-1 min-width-0">
                <div class="d-flex justify-content-between">
                    <strong class="text-truncate" style="font-size: 0.9rem;">${this.escapeHtml(name)}</strong>
                    <small class="${unread > 0 ? 'text-primary fw-bold' : 'text-muted'} ms-2 flex-shrink-0">${timeStr}</small>
                </div>
                <small class="text-muted d-block">${this.escapeHtml(subtitle)}</small>
                ${preview ? `
                <div class="d-flex justify-content-between mt-1">
                    <small class="text-muted text-truncate">${this.escapeHtml(preview)}</small>
                    ${unread > 0 ? `<span class="badge bg-primary rounded-pill ms-2 flex-shrink-0">${unread}</span>` : ''}
                </div>` : ''}
            </div>
            <i class="bi bi-chevron-right text-muted ms-2"></i>
        `;

        el.addEventListener('click', () => this.openChat(conv));
        this.conversationsList.appendChild(el);
    }

    _renderNewProviderItem(provider) {
        const el = document.createElement('div');
        el.className = 'pm-list-item d-flex align-items-center p-3 border-bottom';
        el.style.cursor = 'pointer';

        const name = provider.FullName || `${provider.FirstName || ''} ${provider.LastName || ''}`.trim();
        const roleLabel = provider.RoleLabel || this.getRoleLabel(provider.Role);
        const specialty = provider.Specialty || '';
        const credentials = provider.Credentials || '';
        const subtitle = [roleLabel, specialty, credentials].filter(Boolean).join(' · ');

        el.innerHTML = `
            <div class="flex-shrink-0 me-3">
                <div class="rounded-circle bg-secondary text-white d-flex align-items-center justify-content-center" style="width: 45px; height: 45px; font-size: 0.85rem;">
                    ${this.getInitials(name)}
                </div>
            </div>
            <div class="flex-grow-1 min-width-0">
                <strong class="text-truncate d-block" style="font-size: 0.9rem;">${this.escapeHtml(name)}</strong>
                <small class="text-muted d-block">${this.escapeHtml(subtitle)}</small>
                <small class="text-muted fst-italic mt-1 d-block">Tap to start a conversation</small>
            </div>
            <i class="bi bi-chevron-right text-muted ms-2"></i>
        `;

        el.addEventListener('click', () => this.startOrOpenChat(provider.UserId));
        this.conversationsList.appendChild(el);
    }

    renderMessages(messages) {
        this.chatMessages.innerHTML = '';
        messages.forEach(msg => this.appendMessage(msg));
    }

    appendMessage(msg) {
        const senderType = msg.SenderType ?? msg.senderType;
        const isMe = senderType === 'Patient';
        const el = document.createElement('div');
        el.className = `d-flex mb-2 ${isMe ? 'justify-content-end' : 'justify-content-start'}`;

        const bubbleColor = isMe ? 'bg-primary text-white' : 'bg-white border';
        const timeColor = isMe ? 'text-white-50' : 'text-muted';
        const text = msg.MessageText ?? msg.messageText ?? '';
        const time = msg.CreatedAt ?? msg.createdAt ?? '';

        el.innerHTML = `
            <div class="${bubbleColor} rounded-3 px-3 py-2" style="max-width: 75%; word-wrap: break-word;">
                <div style="font-size: 0.9rem; white-space: pre-wrap;">${this.escapeHtml(text)}</div>
                <div class="${timeColor}" style="font-size: 0.7rem; text-align: right;">${this.formatTime(time)}</div>
            </div>
        `;

        this.chatMessages.appendChild(el);
    }

    scrollToBottom() {
        if (this.chatMessages) {
            setTimeout(() => { this.chatMessages.scrollTop = this.chatMessages.scrollHeight; }, 50);
        }
    }

    updateBadge(count) {
        const show = count > 0;
        if (this.messagesBadge) {
            this.messagesBadge.textContent = count;
            this.messagesBadge.classList.toggle('d-none', !show);
        }
        if (this.messagesBadgeMobile) {
            this.messagesBadgeMobile.textContent = count;
            this.messagesBadgeMobile.classList.toggle('d-none', !show);
        }
    }

    // ============================================
    // Auto-open from email link
    // ============================================

    checkAutoOpenChat() {
        // Check URL param first, then sessionStorage (set during login redirect)
        const params = new URLSearchParams(window.location.search);
        let openChatId = params.get('openChat');

        if (!openChatId) {
            openChatId = sessionStorage.getItem('portalOpenChat');
            if (openChatId) sessionStorage.removeItem('portalOpenChat');
        }

        if (!openChatId) return;

        // Clean URL if param was in URL
        if (params.get('openChat')) {
            const url = new URL(window.location);
            url.searchParams.delete('openChat');
            window.history.replaceState({}, '', url);
        }

        setTimeout(async () => {
            await this.loadProviderList();
            const conv = this.conversations.find(c => (c.PatientConversationId ?? c.patientConversationId) === parseInt(openChatId));
            if (conv) {
                const bsModal = new bootstrap.Modal(this.modal);
                bsModal.show();
                setTimeout(() => this.openChat(conv), 300);
            }
        }, 500);
    }

    // ============================================
    // Helpers
    // ============================================

    getRoleLabel(role) {
        const roleMap = { 0: 'Admin', 1: 'Clinic Admin', 2: 'Provider', 3: 'Front Desk', 4: 'Biller', 6: 'Medical Assistant', 7: 'Nurse' };
        return roleMap[role] || '';
    }

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

    showToast(message, type = 'info') {
        const toastEl = document.getElementById('portalToast');
        const toastMsg = document.getElementById('portalToastMessage');
        const toastTitle = document.getElementById('portalToastTitle');
        if (toastEl && toastMsg) {
            toastTitle.textContent = type === 'error' ? 'Error' : 'Info';
            toastMsg.textContent = message;
            const toast = new bootstrap.Toast(toastEl);
            toast.show();
        }
    }
}

document.addEventListener('DOMContentLoaded', () => {
    window._patientMessagingModule = new PatientMessagingModule();
    window._patientMessagingModule.init();
});
