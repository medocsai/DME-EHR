/**
 * Autocomplete - Reusable autocomplete component
 * Provides search-as-you-type functionality for various entity types
 *
 * Usage:
 *   const autocomplete = new Autocomplete({
 *     inputId: 'patientSearch',
 *     resultsId: 'patientResults',
 *     onSearch: async (query) => await App.api.get(`/patients/search?q=${query}`),
 *     onSelect: (item) => selectPatient(item),
 *     renderItem: (item) => `<div>${item.FullName}</div>`
 *   });
 */
class Autocomplete {
    /**
     * Create an autocomplete instance
     * @param {Object} options - Configuration options
     */
    constructor(options) {
        this.options = {
            inputId: null,
            resultsId: null,
            onSearch: null,
            onSelect: null,
            renderItem: null,
            minChars: 2,
            debounceMs: 300,
            maxResults: 10,
            emptyMessage: 'No results found',
            loadingMessage: 'Searching...',
            placeholder: 'Search...',
            itemClass: 'autocomplete-item',
            activeClass: 'active',
            ...options
        };

        this.inputElement = null;
        this.resultsElement = null;
        this.selectedIndex = -1;
        this.results = [];
        this.isOpen = false;
        this.debounceTimer = null;

        this.init();
    }

    /**
     * Initialize the autocomplete
     */
    init() {
        // Get elements
        this.inputElement = document.getElementById(this.options.inputId);
        this.resultsElement = document.getElementById(this.options.resultsId);

        if (!this.inputElement || !this.resultsElement) {
            console.error('Autocomplete: Input or results element not found');
            return;
        }

        // Set placeholder
        if (this.options.placeholder) {
            this.inputElement.placeholder = this.options.placeholder;
        }

        // Bind events
        this.bindEvents();
    }

    /**
     * Bind event listeners
     */
    bindEvents() {
        // Input events
        this.inputElement.addEventListener('input', (e) => {
            this.handleInput(e.target.value);
        });

        this.inputElement.addEventListener('focus', () => {
            if (this.results.length > 0) {
                this.showResults();
            }
        });

        this.inputElement.addEventListener('blur', () => {
            // Delay hide to allow click on result
            setTimeout(() => this.hideResults(), 200);
        });

        // Keyboard navigation
        this.inputElement.addEventListener('keydown', (e) => {
            this.handleKeydown(e);
        });

        // Click on result
        this.resultsElement.addEventListener('click', (e) => {
            const item = e.target.closest(`.${this.options.itemClass}`);
            if (item) {
                const index = parseInt(item.dataset.index);
                this.selectItem(index);
            }
        });
    }

    /**
     * Handle input changes
     * @param {string} value - Input value
     */
    handleInput(value) {
        // Clear previous timer
        if (this.debounceTimer) {
            clearTimeout(this.debounceTimer);
        }

        // Check minimum characters
        if (value.length < this.options.minChars) {
            this.hideResults();
            this.results = [];
            return;
        }

        // Debounce search
        this.debounceTimer = setTimeout(() => {
            this.search(value);
        }, this.options.debounceMs);
    }

    /**
     * Handle keyboard navigation
     * @param {KeyboardEvent} e - Keyboard event
     */
    handleKeydown(e) {
        if (!this.isOpen) return;

        switch (e.key) {
            case 'ArrowDown':
                e.preventDefault();
                this.moveSelection(1);
                break;
            case 'ArrowUp':
                e.preventDefault();
                this.moveSelection(-1);
                break;
            case 'Enter':
                e.preventDefault();
                if (this.selectedIndex >= 0) {
                    this.selectItem(this.selectedIndex);
                }
                break;
            case 'Escape':
                this.hideResults();
                break;
        }
    }

    /**
     * Perform search
     * @param {string} query - Search query
     */
    async search(query) {
        if (!this.options.onSearch) {
            console.error('Autocomplete: No onSearch handler provided');
            return;
        }

        // Show loading
        this.showLoading();

        try {
            const results = await this.options.onSearch(query);
            this.results = Array.isArray(results) ? results.slice(0, this.options.maxResults) : [];
            this.selectedIndex = -1;
            this.renderResults();
        } catch (error) {
            console.error('Autocomplete search error:', error);
            this.results = [];
            this.showEmpty();
        }
    }

    /**
     * Render search results
     */
    renderResults() {
        if (this.results.length === 0) {
            this.showEmpty();
            return;
        }

        const html = this.results.map((item, index) => {
            const content = this.options.renderItem
                ? this.options.renderItem(item)
                : StringUtils.escape(item.toString());

            return `
                <div class="${this.options.itemClass}" data-index="${index}">
                    ${content}
                </div>
            `;
        }).join('');

        this.resultsElement.innerHTML = html;
        this.showResults();
    }

    /**
     * Show loading state
     */
    showLoading() {
        this.resultsElement.innerHTML = `
            <div class="autocomplete-loading text-muted p-2">
                <span class="spinner-border spinner-border-sm me-2"></span>
                ${this.options.loadingMessage}
            </div>
        `;
        this.showResults();
    }

    /**
     * Show empty state
     */
    showEmpty() {
        this.resultsElement.innerHTML = `
            <div class="autocomplete-empty text-muted p-2">
                ${this.options.emptyMessage}
            </div>
        `;
        this.showResults();
    }

    /**
     * Show results dropdown
     */
    showResults() {
        this.resultsElement.classList.remove('d-none');
        this.resultsElement.classList.add('show');
        this.isOpen = true;
    }

    /**
     * Hide results dropdown
     */
    hideResults() {
        this.resultsElement.classList.add('d-none');
        this.resultsElement.classList.remove('show');
        this.isOpen = false;
        this.selectedIndex = -1;
    }

    /**
     * Move selection up or down
     * @param {number} direction - 1 for down, -1 for up
     */
    moveSelection(direction) {
        const items = this.resultsElement.querySelectorAll(`.${this.options.itemClass}`);
        if (items.length === 0) return;

        // Remove current selection
        if (this.selectedIndex >= 0 && items[this.selectedIndex]) {
            items[this.selectedIndex].classList.remove(this.options.activeClass);
        }

        // Calculate new index
        this.selectedIndex += direction;
        if (this.selectedIndex < 0) {
            this.selectedIndex = items.length - 1;
        } else if (this.selectedIndex >= items.length) {
            this.selectedIndex = 0;
        }

        // Add new selection
        items[this.selectedIndex].classList.add(this.options.activeClass);
        items[this.selectedIndex].scrollIntoView({ block: 'nearest' });
    }

    /**
     * Select an item
     * @param {number} index - Item index
     */
    selectItem(index) {
        if (index < 0 || index >= this.results.length) return;

        const item = this.results[index];

        if (this.options.onSelect) {
            this.options.onSelect(item);
        }

        this.hideResults();
    }

    /**
     * Set the input value
     * @param {string} value - Value to set
     */
    setValue(value) {
        if (this.inputElement) {
            this.inputElement.value = value;
        }
    }

    /**
     * Get the input value
     * @returns {string}
     */
    getValue() {
        return this.inputElement?.value || '';
    }

    /**
     * Clear the input and results
     */
    clear() {
        this.setValue('');
        this.results = [];
        this.hideResults();
    }

    /**
     * Destroy the autocomplete
     */
    destroy() {
        if (this.debounceTimer) {
            clearTimeout(this.debounceTimer);
        }
        // Note: Event listeners will be garbage collected with the elements
        this.inputElement = null;
        this.resultsElement = null;
        this.results = [];
    }
}

// Export for global access
window.Autocomplete = Autocomplete;
