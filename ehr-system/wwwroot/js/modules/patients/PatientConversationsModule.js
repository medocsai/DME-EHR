/**
 * PatientConversationsModule — Patient Profile "Conversations" tab
 * Read-only view of all patient-provider messaging threads for a patient.
 * Renders a two-pane layout (provider list + thread) when multiple providers exist,
 * or a single full-width thread when only one provider has conversed with this patient.
 */
class PatientConversationsModule {
    constructor() {
        this._currentPatientId = null;
    }

    loadPatientTab(patientId) {
        this._currentPatientId = patientId;
        const el = document.getElementById('patientConversationsContent');
        if (!el) return;
        el.innerHTML = '<div class="text-center p-4"><div class="spinner-border spinner-border-sm text-secondary"></div></div>';

        fetch(`/api/patient-messaging/patients/${patientId}/conversations`, { headers: PatientUtilities.getHeaders() })
            .then(r => { if (!r.ok) throw new Error(r.status); return r.json(); })
            .then(conversations => this._renderTab(el, patientId, conversations))
            .catch(() => {
                el.innerHTML = '<div class="text-center p-4 text-muted small">Could not load conversations.</div>';
            });
    }

    _renderTab(el, patientId, conversations) {
        if (!conversations || !conversations.length) {
            el.innerHTML = this._emptyState();
            return;
        }
        if (conversations.length === 1) {
            el.innerHTML = this._singleLayout(conversations[0]);
            this._loadThread(patientId, conversations[0].PatientConversationId, el.querySelector('.pc-messages'), conversations[0]);
        } else {
            el.innerHTML = this._multiLayout(conversations);
            this._wireProviderList(el, patientId, conversations);
            const firstRow = el.querySelector('.pc-provider-row');
            if (firstRow) firstRow.click();
        }
    }

    _wireProviderList(el, patientId, conversations) {
        el.querySelectorAll('.pc-provider-row').forEach(row => {
            row.addEventListener('click', () => {
                el.querySelectorAll('.pc-provider-row').forEach(r => r.classList.remove('active'));
                row.classList.add('active');
                const convId = parseInt(row.dataset.convId, 10);
                const conv = conversations.find(c => c.PatientConversationId === convId);
                if (!conv) return;
                const hdr = el.querySelector('.pc-thread-header-inner');
                if (hdr) hdr.innerHTML = this._threadHeaderInner(conv);
                const msgs = el.querySelector('.pc-messages');
                if (msgs) this._loadThread(patientId, convId, msgs, conv);
            });
        });
    }

    _loadThread(patientId, conversationId, messagesEl, conv) {
        messagesEl.innerHTML = '<div class="text-center p-3"><div class="spinner-border spinner-border-sm text-secondary"></div></div>';
        fetch(`/api/patient-messaging/patients/${patientId}/conversations/${conversationId}/messages`, { headers: PatientUtilities.getHeaders() })
            .then(r => { if (!r.ok) throw new Error(r.status); return r.json(); })
            .then(messages => {
                messagesEl.innerHTML = this._renderMessages(messages, conv);
                messagesEl.scrollTop = messagesEl.scrollHeight;
            })
            .catch(() => {
                messagesEl.innerHTML = '<div class="text-center p-3 text-muted small">Could not load messages.</div>';
            });
    }

    _renderMessages(messages, conv) {
        if (!messages || !messages.length) {
            return '<div class="text-center p-3 text-muted small">No messages in this conversation.</div>';
        }
        const tz = (window.App && window.App.state && window.App.state.get && window.App.state.get('locationTimeZoneId')) || undefined;
        let html = '';
        let lastDateLabel = null;

        messages.forEach(msg => {
            const dateLabel = this._formatDateLabel(msg.CreatedAt, tz);
            if (dateLabel !== lastDateLabel) {
                html += `<div class="pc-date-divider"><span>${this._esc(dateLabel)}</span></div>`;
                lastDateLabel = dateLabel;
            }
            const fromPatient = msg.SenderType === 'Patient';
            const color = fromPatient ? '#0d6efd' : this._avatarColor(conv.UserId);
            const initials = this._initials(msg.SenderName);
            html += `
                <div class="pc-msg-row${fromPatient ? ' from-patient' : ''}">
                    <div class="pc-msg-avatar" style="background:${color}">${initials}</div>
                    <div class="pc-msg-wrap">
                        <div class="pc-msg-sender">${this._esc(msg.SenderName)}</div>
                        <div class="pc-msg-bubble">${this._esc(msg.MessageText)}</div>
                        <div class="pc-msg-time">${this._formatTime(msg.CreatedAt, tz)}</div>
                    </div>
                </div>`;
        });
        return html;
    }

    // ── Layout templates ──

    _singleLayout(conv) {
        return `
            <div class="pc-single">
                <div class="pc-thread-header">
                    <div class="pc-thread-header-inner">${this._threadHeaderInner(conv)}</div>
                </div>
                <div class="pc-messages"></div>
                <div class="pc-footer">View only. If you are a provider, go to <strong>Patient Inbox</strong> (top left) to start or continue a conversation with this patient.</div>
            </div>`;
    }

    _multiLayout(conversations) {
        const rows = conversations.map((c, i) => {
            const color = this._avatarColor(c.UserId);
            const initials = this._initials(c.UserName);
            const subtitle = [c.UserRoleLabel, c.UserSpecialty].filter(Boolean).join(' · ');
            const preview = c.LastMessageText ? this._esc(c.LastMessageText.substring(0, 50)) + (c.LastMessageText.length > 50 ? '...' : '') : '';
            const time = c.LastMessageAt ? this._formatRelativeTime(c.LastMessageAt) : '';
            return `
                <div class="pc-provider-row${i === 0 ? ' active' : ''}" data-conv-id="${c.PatientConversationId}">
                    <div class="pc-pr-avatar" style="background:${color}">${initials}</div>
                    <div class="pc-pr-info">
                        <div class="pc-pr-name">${this._esc(c.UserName)}</div>
                        ${subtitle ? `<div class="pc-pr-role">${this._esc(subtitle)}</div>` : ''}
                        ${preview ? `<div class="pc-pr-preview">${preview}</div>` : ''}
                    </div>
                    <div class="pc-pr-time">${time}</div>
                </div>`;
        }).join('');

        return `
            <div class="pc-layout">
                <div class="pc-provider-list">
                    <div class="pc-list-header">Providers</div>
                    <div class="pc-list-body">${rows}</div>
                </div>
                <div class="pc-thread-pane">
                    <div class="pc-thread-header">
                        <div class="pc-thread-header-inner">${this._threadHeaderInner(conversations[0])}</div>
                    </div>
                    <div class="pc-messages"></div>
                    <div class="pc-footer">View only. If you are a provider, go to <strong>Patient Inbox</strong> (top left) to start or continue a conversation with this patient.</div>
                </div>
            </div>`;
    }

    _threadHeaderInner(conv) {
        const color = this._avatarColor(conv.UserId);
        const initials = this._initials(conv.UserName);
        const subtitle = [conv.UserRoleLabel, conv.UserSpecialty].filter(Boolean).join(' · ');
        return `
            <div class="pc-th-avatar" style="background:${color}">${initials}</div>
            <div>
                <div class="pc-th-name">${this._esc(conv.UserName)}</div>
                ${subtitle ? `<div class="pc-th-role">${this._esc(subtitle)}</div>` : ''}
            </div>`;
    }

    _emptyState() {
        return `<div class="pc-empty">
            <i class="bi bi-chat-square-dots"></i>
            <p class="fw-semibold mb-1">No conversations yet</p>
            <p class="small text-muted mb-0">Messages between this patient and their providers will appear here.</p>
        </div>`;
    }

    // ── Helpers ──

    _initials(name) {
        if (!name) return '?';
        const parts = name.trim().split(' ');
        return parts.length >= 2
            ? (parts[0][0] + parts[parts.length - 1][0]).toUpperCase()
            : name.substring(0, 2).toUpperCase();
    }

    _avatarColor(userId) {
        const colors = ['#1a56db', '#0891b2', '#7c3aed', '#059669', '#dc2626', '#ea580c', '#0284c7', '#be185d'];
        return colors[(userId || 0) % colors.length];
    }

    _esc(str) {
        if (!str) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    _formatTime(isoString, tz) {
        try {
            return new Intl.DateTimeFormat('en-US', {
                timeZone: tz, hour: 'numeric', minute: '2-digit', hour12: true
            }).format(new Date(isoString));
        } catch { return ''; }
    }

    _formatDateLabel(isoString, tz) {
        try {
            const opts = { timeZone: tz, year: 'numeric', month: 'numeric', day: 'numeric' };
            const d = new Intl.DateTimeFormat('en-US', opts);
            const msgDate = d.format(new Date(isoString));
            const today = d.format(new Date());
            const yesterday = d.format(new Date(Date.now() - 86400000));
            if (msgDate === today) return 'Today';
            if (msgDate === yesterday) return 'Yesterday';
            return new Intl.DateTimeFormat('en-US', {
                timeZone: tz, month: 'short', day: 'numeric', year: 'numeric'
            }).format(new Date(isoString));
        } catch { return ''; }
    }

    _formatRelativeTime(isoString) {
        try {
            const date = new Date(isoString);
            const diffDays = Math.floor((Date.now() - date) / 86400000);
            if (diffDays === 0) return new Intl.DateTimeFormat('en-US', { hour: 'numeric', minute: '2-digit', hour12: true }).format(date);
            if (diffDays < 7) return ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'][date.getDay()];
            return new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric' }).format(date);
        } catch { return ''; }
    }
}

window.patientConversationsModule = new PatientConversationsModule();
