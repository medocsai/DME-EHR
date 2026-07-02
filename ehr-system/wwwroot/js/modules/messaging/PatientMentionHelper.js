/**
 * PatientMentionHelper - @patient mention support for the messaging textarea
 *
 * Detects @ typing in the message input, shows a patient search dropdown,
 * handles selection, tracks active mentions, and provides serialization
 * and rendering utilities.
 *
 * Mention wire format: @[Patient Name](patient:123)
 */
class PatientMentionHelper {
    constructor(options) {
        this.textarea = options.textarea;
        this.apiGet = options.apiGet;

        // Active mentions in the current message being composed
        this.activeMentions = [];

        // Dropdown state
        this.dropdown = null;
        this.isDropdownOpen = false;
        this.selectedIndex = -1;
        this.searchResults = [];
        this.debounceTimer = null;
        this.mentionStartPos = -1;

        this._createDropdown();
        this._bindEvents();
    }

    // =========================================
    // Dropdown DOM
    // =========================================

    _createDropdown() {
        this.dropdown = document.createElement('div');
        this.dropdown.className = 'mention-dropdown';
        this.dropdown.style.display = 'none';

        // Insert inside #normalInputArea (not as sibling) so position: relative
        // on the input area acts as the positioning anchor, and overflow: hidden
        // on .chat-window doesn't clip the dropdown
        const inputArea = document.getElementById('normalInputArea');
        if (inputArea) {
            inputArea.style.position = 'relative';
            inputArea.insertBefore(this.dropdown, inputArea.firstChild);
        }
    }

    // =========================================
    // Event binding
    // =========================================

    _bindEvents() {
        this._boundHandleInput = () => this._handleInput();
        this._boundHandleKeydown = (e) => this._handleKeydown(e);
        this._boundHandleBlur = () => {
            setTimeout(() => this._closeDropdown(), 200);
        };

        this.textarea.addEventListener('input', this._boundHandleInput);
        this.textarea.addEventListener('keydown', this._boundHandleKeydown);
        this.textarea.addEventListener('blur', this._boundHandleBlur);
    }

    // =========================================
    // Input detection
    // =========================================

    _handleInput() {
        this._validateActiveMentions();

        const cursorPos = this.textarea.selectionStart;
        const textBeforeCursor = this.textarea.value.substring(0, cursorPos);

        const atIndex = textBeforeCursor.lastIndexOf('@');
        if (atIndex === -1) {
            this._closeDropdown();
            return;
        }

        // @ must be at start or preceded by whitespace
        const charBefore = atIndex > 0 ? textBeforeCursor[atIndex - 1] : ' ';
        if (charBefore !== ' ' && charBefore !== '\n' && atIndex !== 0) {
            this._closeDropdown();
            return;
        }

        // Skip if cursor is after an already-completed mention
        // (the @ belongs to a mention that was already selected)
        const isInsideExistingMention = this.activeMentions.some(m => {
            const mentionEnd = m.startIndex + m.fullName.length + 1; // +1 for the @
            return atIndex === m.startIndex && cursorPos > mentionEnd;
        });
        if (isInsideExistingMention) {
            this._closeDropdown();
            return;
        }

        const query = textBeforeCursor.substring(atIndex + 1);

        // Abandon if query is too long
        if (query.length > 40) {
            this._closeDropdown();
            return;
        }

        // If query contains a space and matches a completed mention, skip
        // (user is typing after "@Name ", not searching)
        if (query.includes(' ')) {
            const isCompletedMention = this.activeMentions.some(m => {
                return atIndex === m.startIndex && query.startsWith(m.fullName);
            });
            if (isCompletedMention) {
                this._closeDropdown();
                return;
            }
        }

        this.mentionStartPos = atIndex;

        clearTimeout(this.debounceTimer);

        if (query.length === 0) {
            // Just typed @, show initial prompt
            this._showMinCharsMessage();
        } else if (query.length < 3) {
            // 1-2 chars — show prompt, no API call
            this._showMinCharsMessage();
        } else {
            // 3+ chars — debounced search
            this.debounceTimer = setTimeout(() => this._searchPatients(query), 300);
        }
    }

    // =========================================
    // Patient search
    // =========================================

    async _searchPatients(query) {
        try {
            this._showLoading();
            const response = await this.apiGet(`/patients/search?q=${encodeURIComponent(query)}&take=8&activeOnly=true`);
            this.searchResults = response.Results || [];
            this.selectedIndex = this.searchResults.length > 0 ? 0 : -1;
            this._renderResults();
        } catch (error) {
            console.error('[MentionHelper] Search error:', error);
            this._closeDropdown();
        }
    }

    // =========================================
    // Dropdown rendering
    // =========================================

    _renderResults() {
        if (this.searchResults.length === 0) {
            this.dropdown.innerHTML = '<div class="mention-empty">No patients found</div>';
            this._openDropdown();
            return;
        }

        this.dropdown.innerHTML = this.searchResults.map((patient, index) => {
            const escapedName = this._escapeHtml(patient.FullName);
            const escapedMRN = this._escapeHtml(patient.MRN);
            const escapedDOB = this._escapeHtml(patient.DOBFormatted);
            return `
                <div class="mention-item ${index === this.selectedIndex ? 'active' : ''}"
                     data-index="${index}" data-patient-id="${patient.PatientId}">
                    <div class="mention-item-name">${escapedName}</div>
                    <div class="mention-item-detail">MRN: ${escapedMRN} | DOB: ${escapedDOB}</div>
                </div>
            `;
        }).join('');

        this.dropdown.querySelectorAll('.mention-item').forEach(item => {
            item.addEventListener('mousedown', (e) => {
                e.preventDefault();
                const idx = parseInt(item.dataset.index);
                this._selectPatient(idx);
            });
        });

        this._openDropdown();
    }

    _showMinCharsMessage() {
        this.dropdown.innerHTML = '<div class="mention-empty">Type at least 3 characters to search...</div>';
        this._openDropdown();
    }

    _showLoading() {
        this.dropdown.innerHTML = `
            <div class="mention-loading">
                <span class="spinner-border spinner-border-sm me-2"></span>Searching...
            </div>
        `;
        this._openDropdown();
    }

    _openDropdown() {
        this.dropdown.style.display = 'block';
        this.isDropdownOpen = true;
    }

    _closeDropdown() {
        this.dropdown.style.display = 'none';
        this.isDropdownOpen = false;
        this.selectedIndex = -1;
        this.searchResults = [];
    }

    // =========================================
    // Keyboard navigation
    // =========================================

    _handleKeydown(e) {
        if (!this.isDropdownOpen) return;

        if (e.key === 'ArrowDown') {
            e.preventDefault();
            this._moveSelection(1);
        } else if (e.key === 'ArrowUp') {
            e.preventDefault();
            this._moveSelection(-1);
        } else if (e.key === 'Enter' && this.selectedIndex >= 0) {
            e.preventDefault();
            e.stopPropagation();
            this._selectPatient(this.selectedIndex);
        } else if (e.key === 'Escape') {
            e.preventDefault();
            this._closeDropdown();
        }
    }

    _moveSelection(direction) {
        const items = this.dropdown.querySelectorAll('.mention-item');
        if (items.length === 0) return;

        if (this.selectedIndex >= 0 && items[this.selectedIndex]) {
            items[this.selectedIndex].classList.remove('active');
        }

        this.selectedIndex += direction;
        if (this.selectedIndex < 0) this.selectedIndex = items.length - 1;
        if (this.selectedIndex >= items.length) this.selectedIndex = 0;

        items[this.selectedIndex].classList.add('active');
        items[this.selectedIndex].scrollIntoView({ block: 'nearest' });
    }

    // =========================================
    // Patient selection
    // =========================================

    _selectPatient(index) {
        const patient = this.searchResults[index];
        if (!patient) return;

        const value = this.textarea.value;
        const beforeMention = value.substring(0, this.mentionStartPos);
        const afterCursor = value.substring(this.textarea.selectionStart);

        const mentionDisplayText = `@${patient.FullName} `;

        this.textarea.value = beforeMention + mentionDisplayText + afterCursor;

        const newCursorPos = this.mentionStartPos + mentionDisplayText.length;
        this.textarea.selectionStart = newCursorPos;
        this.textarea.selectionEnd = newCursorPos;

        this.activeMentions.push({
            patientId: patient.PatientId,
            fullName: patient.FullName,
            startIndex: this.mentionStartPos
        });

        this._closeDropdown();
        this.textarea.focus();
    }

    // =========================================
    // Mention tracking & validation
    // =========================================

    _validateActiveMentions() {
        const currentText = this.textarea.value;
        this.activeMentions = this.activeMentions.filter(mention => {
            const expected = `@${mention.fullName}`;
            // Search near the expected position (allowing some drift from edits)
            const searchStart = Math.max(0, mention.startIndex - 10);
            const searchEnd = Math.min(currentText.length, mention.startIndex + expected.length + 10);
            const searchRegion = currentText.substring(searchStart, searchEnd);
            const foundAt = searchRegion.indexOf(expected);
            if (foundAt !== -1) {
                // Update the start index to the actual position
                mention.startIndex = searchStart + foundAt;
                return true;
            }
            return false;
        });
    }

    // =========================================
    // Serialization (compose → wire format)
    // =========================================

    serializeMessage(rawText) {
        if (this.activeMentions.length === 0) return rawText;

        this._validateActiveMentions();

        // Sort by startIndex descending so replacement doesn't shift earlier indices
        const sorted = [...this.activeMentions].sort((a, b) => b.startIndex - a.startIndex);
        let result = rawText;

        for (const mention of sorted) {
            const displayText = `@${mention.fullName}`;
            const pos = result.indexOf(displayText, Math.max(0, mention.startIndex - 5));
            if (pos !== -1) {
                const tokenized = `@[${mention.fullName}](patient:${mention.patientId})`;
                result = result.substring(0, pos) + tokenized + result.substring(pos + displayText.length);
            }
        }

        return result;
    }

    // =========================================
    // Reset (after message sent)
    // =========================================

    reset() {
        this.activeMentions = [];
        this.mentionStartPos = -1;
        this._closeDropdown();
    }

    // =========================================
    // Static: Render mentions in displayed messages
    // =========================================

    /**
     * Parse mention tokens in already-escaped HTML text and convert to clickable links.
     * Input MUST be HTML-escaped first (call _escape() before this).
     * @param {string} escapedText - HTML-escaped message text
     * @returns {string} HTML with clickable patient mention links
     */
    static renderMentions(escapedText) {
        return escapedText.replace(
            /@\[([^\]]+)\]\(patient:(\d+)\)/g,
            (match, name, patientId) => {
                return `<a href="#" class="patient-mention" onclick="viewPatient(${patientId}); return false;" title="View patient profile">@${name}</a>`;
            }
        );
    }

    /**
     * Strip mention token syntax for preview text.
     * Converts @[Name](patient:123) → @Name
     * @param {string} text - Raw message text
     * @returns {string} Cleaned text for previews
     */
    static cleanPreviewText(text) {
        if (!text) return '';
        return text.replace(/@\[([^\]]+)\]\(patient:\d+\)/g, '@$1');
    }

    // =========================================
    // Utilities
    // =========================================

    _escapeHtml(str) {
        if (!str) return '';
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }

    destroy() {
        if (this.debounceTimer) clearTimeout(this.debounceTimer);
        if (this.textarea) {
            this.textarea.removeEventListener('input', this._boundHandleInput);
            this.textarea.removeEventListener('keydown', this._boundHandleKeydown);
            this.textarea.removeEventListener('blur', this._boundHandleBlur);
        }
        if (this.dropdown && this.dropdown.parentNode) {
            this.dropdown.parentNode.removeChild(this.dropdown);
        }
        this.textarea = null;
        this.dropdown = null;
        this.activeMentions = [];
        this.searchResults = [];
    }
}

window.PatientMentionHelper = PatientMentionHelper;
