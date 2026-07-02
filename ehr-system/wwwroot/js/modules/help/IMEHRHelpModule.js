/**
 * IMEHRHelpModule — MEDOCS AI Help Widget
 *
 * Floating help chatbox powered by Gemini AI.
 * Provides multi-turn conversation, role-aware answers,
 * and feature request submission.
 */
class IMEHRHelpModule {
    constructor(options = {}) {
        this.api = options.api || null;
        this.eventBus = options.eventBus || null;

        // State
        this._isOpen = false;
        this._isLoading = false;
        this._messages = [];
        this._conversationHistory = [];
        this._pendingFeatureRequest = null;
        this._storageKey = 'imehrHelpChat';

        // DOM refs
        this._widget = null;
        this._toggleBtn = null;
        this._messagesContainer = null;
        this._input = null;
        this._sendBtn = null;
        this._typingIndicator = null;
        this._welcomeScreen = null;

        // Bind methods
        this._handleSend = this._handleSend.bind(this);
        this._handleKeyDown = this._handleKeyDown.bind(this);
        this._toggle = this._toggle.bind(this);
    }

    async init() {
        this._createWidget();
        this._bindEvents();
        this._restoreSession();

        // Auto-open if URL has ?openHelp=1
        const params = new URLSearchParams(window.location.search);
        if (params.get('openHelp') === '1') {
            setTimeout(() => {
                if (!this._isOpen) this._toggle();
                if (this._input) this._input.focus();
            }, 500);
            // Clean up URL
            const url = new URL(window.location);
            url.searchParams.delete('openHelp');
            window.history.replaceState({}, '', url);
        }
    }

    destroy() {
        if (this._toggleBtn) this._toggleBtn.remove();
        if (this._widget) this._widget.remove();
    }

    // =============================================
    // DOM Creation
    // =============================================

    _createWidget() {
        // Toggle button
        this._toggleBtn = document.createElement('button');
        this._toggleBtn.className = 'imehr-help-toggle-btn';
        this._toggleBtn.innerHTML = '<i class="bi bi-question-circle"></i> <span>Help</span>';
        this._toggleBtn.title = 'MEDOCS AI Help';
        document.body.appendChild(this._toggleBtn);

        // Widget panel
        this._widget = document.createElement('div');
        this._widget.className = 'imehr-help-widget';
        this._widget.innerHTML = `
            <div class="imehr-help-header">
                <div class="imehr-help-header-avatar"><i class="bi bi-robot"></i></div>
                <div class="imehr-help-header-title">MEDOCS AI</div>
                <div class="imehr-help-header-actions">
                    <button class="imehr-help-header-btn" id="imehrHelpClearBtn" title="Clear conversation">
                        <i class="bi bi-arrow-counterclockwise"></i>
                    </button>
                    <button class="imehr-help-header-btn" id="imehrHelpCloseBtn" title="Close">
                        <i class="bi bi-x-lg"></i>
                    </button>
                </div>
            </div>
            <div class="imehr-help-messages" id="imehrHelpMessages">
                <div class="imehr-help-welcome" id="imehrHelpWelcome">
                    <div class="imehr-help-welcome-icon"><i class="bi bi-robot"></i></div>
                    <h4>Welcome to MEDOCS AI</h4>
                    <p>I'm your MEDOCS DME assistant. Ask me anything about using the system.</p>
                    <a href="/Documentation" class="imehr-help-welcome-docs">
                        <i class="bi bi-book"></i> Browse Full Documentation
                    </a>
                    <div class="imehr-help-suggestions">
                        <button class="imehr-help-suggestion-btn" data-question="How do I create a new DME order with HCPCS items?">How do I create an order?</button>
                        <button class="imehr-help-suggestion-btn" data-question="How do I record a delivery and capture proof of delivery?">How do I record a delivery?</button>
                        <button class="imehr-help-suggestion-btn" data-question="How do I bill a monthly rental?">How do I bill a rental?</button>
                        <button class="imehr-help-suggestion-btn" data-question="What can I do based on my role?">What can my role access?</button>
                    </div>
                </div>
            </div>
            <div class="imehr-help-typing" id="imehrHelpTyping">
                <div class="imehr-help-typing-dots">
                    <div class="imehr-help-typing-dot"></div>
                    <div class="imehr-help-typing-dot"></div>
                    <div class="imehr-help-typing-dot"></div>
                </div>
            </div>
            <div class="imehr-help-input-area">
                <div class="imehr-help-input-row">
                    <input type="text" class="imehr-help-input" id="imehrHelpInput"
                           placeholder="Ask about MEDOCS DME..." maxlength="1000" autocomplete="off">
                    <button class="imehr-help-send-btn" id="imehrHelpSendBtn" title="Send">
                        <i class="bi bi-send-fill"></i>
                    </button>
                </div>
                <div class="imehr-help-powered">Powered by MEDOCS AI</div>
            </div>
        `;
        document.body.appendChild(this._widget);

        // Cache DOM refs
        this._messagesContainer = this._widget.querySelector('#imehrHelpMessages');
        this._input = this._widget.querySelector('#imehrHelpInput');
        this._sendBtn = this._widget.querySelector('#imehrHelpSendBtn');
        this._typingIndicator = this._widget.querySelector('#imehrHelpTyping');
        this._welcomeScreen = this._widget.querySelector('#imehrHelpWelcome');
    }

    // =============================================
    // Event Binding
    // =============================================

    _bindEvents() {
        // Toggle
        this._toggleBtn.addEventListener('click', this._toggle);

        // Close
        this._widget.querySelector('#imehrHelpCloseBtn').addEventListener('click', () => {
            if (this._isOpen) this._toggle();
        });

        // Clear
        this._widget.querySelector('#imehrHelpClearBtn').addEventListener('click', () => {
            this._clearConversation();
        });

        // Send
        this._sendBtn.addEventListener('click', this._handleSend);
        this._input.addEventListener('keydown', this._handleKeyDown);

        // Suggestion buttons
        this._widget.querySelectorAll('.imehr-help-suggestion-btn').forEach(btn => {
            btn.addEventListener('click', () => {
                const question = btn.getAttribute('data-question');
                if (question) {
                    this._input.value = question;
                    this._handleSend();
                }
            });
        });

        // Global escape to close
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape' && this._isOpen) {
                this._toggle();
            }
        });
    }

    // =============================================
    // Toggle / Clear
    // =============================================

    _toggle(skipSave) {
        // skipSave must be exactly true (not an Event object from addEventListener)
        const shouldSave = skipSave !== true;
        this._isOpen = !this._isOpen;
        if (this._isOpen) {
            this._widget.classList.add('open');
            this._toggleBtn.style.display = 'none';
            setTimeout(() => this._input.focus(), 100);
        } else {
            this._widget.classList.remove('open');
            this._toggleBtn.style.display = '';
        }
        if (shouldSave) this._saveSession();
    }

    _clearConversation() {
        this._messages = [];
        this._conversationHistory = [];
        this._pendingFeatureRequest = null;

        // Remove all messages except welcome
        const msgs = this._messagesContainer.querySelectorAll('.imehr-help-msg, .imehr-help-feature-prompt');
        msgs.forEach(m => m.remove());

        // Show welcome screen again
        if (this._welcomeScreen) {
            this._welcomeScreen.style.display = '';
        }

        this._saveSession();
    }

    // =============================================
    // Session Persistence (survives page navigation)
    // =============================================

    _saveSession() {
        try {
            const data = {
                messages: this._messages,
                conversationHistory: this._conversationHistory,
                isOpen: this._isOpen
            };
            sessionStorage.setItem(this._storageKey, JSON.stringify(data));
        } catch { /* storage full or unavailable — ignore */ }
    }

    _restoreSession() {
        try {
            const raw = sessionStorage.getItem(this._storageKey);
            if (!raw) return;

            const data = JSON.parse(raw);
            if (!data || !Array.isArray(data.messages) || data.messages.length === 0) return;

            // Restore conversation history for API calls
            this._conversationHistory = data.conversationHistory || [];

            // Hide welcome, re-render messages
            if (this._welcomeScreen) {
                this._welcomeScreen.style.display = 'none';
            }

            for (const msg of data.messages) {
                this._addMessage(msg.type, msg.text, true); // true = skip saving
            }

            // Restore open/closed state
            if (data.isOpen && !this._isOpen) {
                this._toggle(true); // true = skip saving
            }
        } catch { /* corrupt data — ignore */ }
    }

    // =============================================
    // Send Message
    // =============================================

    _handleKeyDown(e) {
        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            this._handleSend();
        }
    }

    async _handleSend() {
        const question = this._input.value.trim();
        if (!question || this._isLoading) return;

        // Hide welcome
        if (this._welcomeScreen) {
            this._welcomeScreen.style.display = 'none';
        }

        // Add user message
        this._addMessage('user', question);
        this._input.value = '';

        // Show loading
        this._setLoading(true);

        try {
            const response = await this._askApi(question);

            if (response.success) {
                this._addMessage('ai', response.answer);

                // Update conversation history
                this._conversationHistory.push({ role: 'user', text: question });
                this._conversationHistory.push({ role: 'model', text: response.answer });

                // Trim to last 20 turns
                if (this._conversationHistory.length > 20) {
                    this._conversationHistory = this._conversationHistory.slice(-20);
                }

                this._saveSession();

                // Feature request prompt
                if (response.isFeatureRequest) {
                    this._pendingFeatureRequest = response.featureRequestSuggestion || question;
                    this._showFeatureRequestPrompt();
                }
            } else {
                this._addMessage('ai', response.answer || "I'm sorry, I'm having trouble right now. Please try again.");
            }
        } catch (err) {
            console.error('MEDOCS AI Help error:', err);
            this._addMessage('ai', "I'm sorry, something went wrong. Please try again in a moment.");
        }

        this._setLoading(false);
    }

    // =============================================
    // Message Display
    // =============================================

    _addMessage(type, text, skipSave = false) {
        const msg = document.createElement('div');
        msg.className = `imehr-help-msg imehr-help-msg-${type}`;

        const avatar = document.createElement('div');
        avatar.className = 'imehr-help-msg-avatar';
        avatar.innerHTML = type === 'user'
            ? '<i class="bi bi-person-fill"></i>'
            : '<i class="bi bi-robot"></i>';

        const bubble = document.createElement('div');
        bubble.className = 'imehr-help-msg-bubble';
        bubble.innerHTML = type === 'ai' ? this._formatResponse(text) : this._escapeHtml(text);

        msg.appendChild(avatar);
        msg.appendChild(bubble);
        this._messagesContainer.appendChild(msg);

        if (!skipSave) {
            this._messages.push({ type, text });
        }
        this._scrollToBottom();
    }

    _showFeatureRequestPrompt() {
        const prompt = document.createElement('div');
        prompt.className = 'imehr-help-feature-prompt';
        prompt.innerHTML = `
            <p><i class="bi bi-lightbulb me-1"></i> Want to send this as a feature request to the IMEHR team?</p>
            <button class="imehr-help-feature-btn">
                <i class="bi bi-send"></i> Send Feature Request
            </button>
        `;

        prompt.querySelector('.imehr-help-feature-btn').addEventListener('click', async () => {
            if (!this._pendingFeatureRequest) return;

            const btn = prompt.querySelector('.imehr-help-feature-btn');
            btn.disabled = true;
            btn.innerHTML = '<i class="bi bi-hourglass-split"></i> Sending...';

            try {
                const result = await this._sendFeatureRequestApi(this._pendingFeatureRequest);
                if (result.success) {
                    prompt.innerHTML = '<p style="color: #059669; margin: 0;"><i class="bi bi-check-circle me-1"></i> Your feature request has been sent to the IMEHR team!</p>';
                } else {
                    prompt.innerHTML = '<p style="color: #dc2626; margin: 0;"><i class="bi bi-exclamation-circle me-1"></i> Could not send. Please email contact@medocs.ai directly.</p>';
                }
            } catch {
                prompt.innerHTML = '<p style="color: #dc2626; margin: 0;"><i class="bi bi-exclamation-circle me-1"></i> Could not send. Please email contact@medocs.ai directly.</p>';
            }

            this._pendingFeatureRequest = null;
        });

        this._messagesContainer.appendChild(prompt);
        this._scrollToBottom();
    }

    _setLoading(loading) {
        this._isLoading = loading;
        this._sendBtn.disabled = loading;
        if (this._typingIndicator) {
            this._typingIndicator.classList.toggle('active', loading);
        }
        if (loading) this._scrollToBottom();
    }

    _scrollToBottom() {
        requestAnimationFrame(() => {
            this._messagesContainer.scrollTop = this._messagesContainer.scrollHeight;
        });
    }

    // =============================================
    // Text Formatting
    // =============================================

    _formatResponse(text) {
        if (!text) return '';

        // Escape HTML first
        let html = this._escapeHtml(text);

        // Process numbered lists: "1. item" → <ol><li>
        html = html.replace(/(?:^|\n)(\d+)\.\s+(.+?)(?=\n\d+\.\s|\n\n|$)/gs, (match) => {
            const items = match.trim().split('\n').map(line => {
                const m = line.match(/^\d+\.\s+(.+)/);
                return m ? `<li>${m[1]}</li>` : '';
            }).filter(Boolean).join('');
            return `<ol>${items}</ol>`;
        });

        // Process bullet lists: "- item" → <ul><li>
        html = html.replace(/(?:^|\n)-\s+(.+?)(?=\n-\s|\n\n|$)/gs, (match) => {
            const items = match.trim().split('\n').map(line => {
                const m = line.match(/^-\s+(.+)/);
                return m ? `<li>${m[1]}</li>` : '';
            }).filter(Boolean).join('');
            return `<ul>${items}</ul>`;
        });

        // Bold: **text** → <strong>
        html = html.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>');

        // Italic: *text* → <em>
        html = html.replace(/\*(.+?)\*/g, '<em>$1</em>');

        // Paragraphs
        html = html.replace(/\n\n/g, '</p><p>');
        html = html.replace(/\n/g, '<br>');
        html = `<p>${html}</p>`;

        // Clean up stray <br> around lists
        html = html.replace(/<br>\s*<(ol|ul)/g, '<$1');
        html = html.replace(/<\/(ol|ul)>\s*<br>/g, '</$1>');

        // Remove empty paragraphs
        html = html.replace(/<p>\s*<\/p>/g, '');

        return html;
    }

    _escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    // =============================================
    // API Calls
    // =============================================

    async _askApi(question) {
        const token = localStorage.getItem('authToken') || sessionStorage.getItem('authToken');
        const response = await fetch('/api/IMEHRHelp/ask', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Authorization': `Bearer ${token}`
            },
            body: JSON.stringify({
                question: question,
                history: this._conversationHistory
            })
        });

        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        return await response.json();
    }

    async _sendFeatureRequestApi(description) {
        const token = localStorage.getItem('authToken') || sessionStorage.getItem('authToken');
        const response = await fetch('/api/IMEHRHelp/feature-request', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Authorization': `Bearer ${token}`
            },
            body: JSON.stringify({ description })
        });

        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }

        return await response.json();
    }
}

// Auto-initialize when DOM is ready
document.addEventListener('DOMContentLoaded', () => {
    // Only init if we're in the main app (not on login, kiosk, portal, or docs page)
    const mainApp = document.getElementById('mainApp');
    if (!mainApp) return;

    // Don't init on documentation page (it has its own help link)
    if (window.location.pathname.toLowerCase() === '/documentation') return;

    window._imehrHelpModule = new IMEHRHelpModule();
    window._imehrHelpModule.init();
});
