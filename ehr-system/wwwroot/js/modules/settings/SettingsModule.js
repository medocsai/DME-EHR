/**
 * SettingsModule - System settings management
 *
 * Manages application settings: loading, displaying by category,
 * saving individual settings, and initializing defaults.
 *
 * @example
 *   // Initialize via App
 *   const settings = App.modules.get('settings');
 *   await settings.init();
 *   settings.getValue('SessionTimeout');
 */
class SettingsModule {
    /**
     * @param {Object} options - Module options
     * @param {Object} options.api - API service instance
     * @param {Object} options.eventBus - Event bus for cross-module communication
     */
    constructor(options = {}) {
        this.api = options.api || null;
        this.eventBus = options.eventBus || null;

        // State
        this.settings = [];
        this.groupedSettings = new Map();
        this.isInitialized = false;

        // DOM references
        this.container = null;

        // Bind methods
        this._handleSaveClick = this._handleSaveClick.bind(this);
    }

    /**
     * Initialize the module
     * @returns {Promise<void>}
     */
    async init() {
        this.container = document.getElementById('settingsContainer');

        if (!this.container) {
            console.warn('[SettingsModule] Container #settingsContainer not found');
            return;
        }

        this._bindEvents();
        await this.load();

        this.isInitialized = true;
        this._emit('settings:initialized');
    }

    /**
     * Bind event handlers using event delegation
     * @private
     */
    _bindEvents() {
        this.container.addEventListener('click', this._handleSaveClick);
    }

    /**
     * Handle save button click via event delegation
     * @private
     * @param {Event} e - Click event
     */
    _handleSaveClick(e) {
        const btn = e.target.closest('.btn-save-setting');
        if (btn) {
            const key = btn.dataset.key;
            if (key) {
                this.save(key);
            }
        }
    }

    /**
     * Load all settings from API
     * @returns {Promise<void>}
     */
    async load() {
        try {
            this.settings = await this._apiGet('/settings') || [];
            this._groupByCategory();
            this._render();
            this._emit('settings:loaded', { settings: this.settings });
        } catch (error) {
            console.error('[SettingsModule] Failed to load settings:', error);
            this._renderError('Failed to load settings');
            throw error;
        }
    }

    /**
     * Save a single setting
     * @param {string} key - Setting key
     * @returns {Promise<void>}
     */
    async save(key) {
        const input = document.getElementById(`setting-${key}`);
        if (!input) {
            console.error('[SettingsModule] Input not found for key:', key);
            return;
        }

        const value = input.dataset.type === 'bool'
            ? (input.checked ? 'true' : 'false')
            : input.value;
        const originalValue = input.dataset.original;

        // Skip if unchanged
        if (value === originalValue) {
            this._showInfo('No Changes', 'Setting value has not changed');
            return;
        }

        try {
            await this._apiPut(`/settings/${key}`, { SettingValue: value });

            // Update tracking
            input.dataset.original = value;

            // Update local state
            const setting = this.settings.find(s => s.SettingKey === key);
            if (setting) {
                setting.SettingValue = value;
            }

            this._showSuccess('Setting saved successfully');
            this._emit('settings:saved', { key, value });
        } catch (error) {
            console.error('[SettingsModule] Failed to save setting:', error);
            this._showError(`Failed to save setting: ${error.message}`);
            throw error;
        }
    }

    /**
     * Get a setting value by key
     * @param {string} key - Setting key
     * @returns {string|null} Setting value or null if not found
     */
    getValue(key) {
        const setting = this.settings.find(s => s.SettingKey === key);
        return setting?.SettingValue || null;
    }

    /**
     * Initialize default settings
     * @returns {Promise<void>}
     */
    async initializeDefaults() {
        const confirmed = await this._confirm({
            title: 'Initialize Default Settings',
            message: 'This will create default settings for any missing configuration. Existing settings will not be modified. Continue?',
            confirmText: 'Initialize',
            confirmClass: 'btn-primary'
        });

        if (!confirmed) return;

        try {
            await this._apiPost('/settings/initialize');
            this._showSuccess('Default settings initialized');
            await this.load();
            this._emit('settings:defaultsInitialized');
        } catch (error) {
            console.error('[SettingsModule] Failed to initialize settings:', error);
            this._showError(`Failed to initialize settings: ${error.message}`);
            throw error;
        }
    }

    /**
     * Refresh settings from server
     * @returns {Promise<void>}
     */
    async refresh() {
        await this.load();
    }

    /**
     * Group settings by category
     * @private
     */
    _groupByCategory() {
        this.groupedSettings.clear();

        this.settings.forEach(setting => {
            const category = setting.Category || 'General';
            if (!this.groupedSettings.has(category)) {
                this.groupedSettings.set(category, []);
            }
            this.groupedSettings.get(category).push(setting);
        });
    }

    /**
     * Render settings to the container
     * @private
     */
    _render() {
        if (!this.container) return;

        if (this.groupedSettings.size === 0) {
            this.container.innerHTML = `
                <div class="alert alert-info">
                    <i class="bi bi-info-circle me-2"></i>
                    No settings found. Click "Initialize Defaults" to create default settings.
                </div>
            `;
            return;
        }

        let html = '';
        this.groupedSettings.forEach((items, category) => {
            html += `
                <div class="card mb-3">
                    <div class="card-header">
                        <h6 class="mb-0">${this._escape(category)}</h6>
                    </div>
                    <div class="card-body">
                        ${items.map(setting => this._renderSettingRow(setting)).join('')}
                    </div>
                </div>
            `;
        });

        this.container.innerHTML = html;
    }

    /**
     * Render a single setting row
     * @private
     * @param {Object} setting - Setting object
     * @returns {string} HTML
     */
    _renderSettingRow(setting) {
        const label = this._formatLabel(setting.SettingKey);
        const value = this._escape(setting.SettingValue || '');

        // Boolean settings render as toggle switches
        if (setting.DataType === 'bool') {
            const isChecked = (setting.SettingValue || '').toLowerCase() === 'true';
            return `
                <div class="row align-items-center mb-3">
                    <div class="col-md-4">
                        <label class="form-label mb-0">
                            <strong>${this._escape(label)}</strong>
                        </label>
                        ${setting.Description ? `
                            <div class="small text-muted">${this._escape(setting.Description)}</div>
                        ` : ''}
                    </div>
                    <div class="col-md-6">
                        <div class="form-check form-switch">
                            <input type="checkbox"
                                   class="form-check-input setting-input"
                                   id="setting-${setting.SettingKey}"
                                   ${isChecked ? 'checked' : ''}
                                   data-key="${setting.SettingKey}"
                                   data-original="${isChecked ? 'true' : 'false'}"
                                   data-type="bool">
                        </div>
                    </div>
                    <div class="col-md-2">
                        <button class="btn btn-sm btn-primary btn-save-setting"
                                data-key="${setting.SettingKey}">
                            <i class="bi bi-check-lg me-1"></i>Save
                        </button>
                    </div>
                </div>
            `;
        }

        const inputType = this._getInputType(setting);

        return `
            <div class="row align-items-center mb-3">
                <div class="col-md-4">
                    <label class="form-label mb-0">
                        <strong>${this._escape(label)}</strong>
                    </label>
                    ${setting.Description ? `
                        <div class="small text-muted">${this._escape(setting.Description)}</div>
                    ` : ''}
                </div>
                <div class="col-md-6">
                    <input type="${inputType}"
                           class="form-control setting-input"
                           id="setting-${setting.SettingKey}"
                           value="${value}"
                           data-key="${setting.SettingKey}"
                           data-original="${value}">
                </div>
                <div class="col-md-2">
                    <button class="btn btn-sm btn-primary btn-save-setting"
                            data-key="${setting.SettingKey}">
                        <i class="bi bi-check-lg me-1"></i>Save
                    </button>
                </div>
            </div>
        `;
    }

    /**
     * Get input type based on setting data type
     * @private
     * @param {Object} setting
     * @returns {string}
     */
    _getInputType(setting) {
        if (setting.DataType === 'int' || setting.DataType === 'number') {
            return 'number';
        }
        if (setting.SettingKey?.toLowerCase().includes('password')) {
            return 'password';
        }
        if (setting.SettingKey?.toLowerCase().includes('email')) {
            return 'email';
        }
        return 'text';
    }

    /**
     * Format setting key as readable label
     * @private
     * @param {string} key - Setting key (PascalCase)
     * @returns {string} Formatted label
     */
    _formatLabel(key) {
        return key.replace(/([A-Z])/g, ' $1').trim();
    }

    /**
     * Render error state
     * @private
     * @param {string} message - Error message
     */
    _renderError(message) {
        if (!this.container) return;

        this.container.innerHTML = `
            <div class="alert alert-danger">
                <i class="bi bi-exclamation-triangle me-2"></i>
                ${this._escape(message)}
                <button class="btn btn-sm btn-outline-danger ms-3 retry-btn">
                    Retry
                </button>
            </div>
        `;

        // Bind retry button
        const retryBtn = this.container.querySelector('.retry-btn');
        if (retryBtn) {
            retryBtn.addEventListener('click', () => this.load());
        }
    }

    // === API Methods ===

    /**
     * API GET request
     * @private
     */
    async _apiGet(url) {
        if (this.api) {
            return this.api.get(url);
        }
        const response = await fetch(`/api${url}`, {
            headers: this._getHeaders()
        });
        if (!response.ok) {
            const error = await response.json().catch(() => ({}));
            throw new Error(error.message || 'API request failed');
        }
        return response.json();
    }

    /**
     * API PUT request
     * @private
     */
    async _apiPut(url, data) {
        if (this.api) {
            return this.api.put(url, data);
        }
        const response = await fetch(`/api${url}`, {
            method: 'PUT',
            headers: this._getHeaders(),
            body: JSON.stringify(data)
        });
        if (!response.ok) {
            const error = await response.json().catch(() => ({}));
            throw new Error(error.message || 'API request failed');
        }
        return response.json();
    }

    /**
     * API POST request
     * @private
     */
    async _apiPost(url, data = {}) {
        if (this.api) {
            return this.api.post(url, data);
        }
        const response = await fetch(`/api${url}`, {
            method: 'POST',
            headers: this._getHeaders(),
            body: JSON.stringify(data)
        });
        if (!response.ok) {
            const error = await response.json().catch(() => ({}));
            throw new Error(error.message || 'API request failed');
        }
        return response.json();
    }

    /**
     * Get auth headers
     * @private
     */
    _getHeaders() {
        const token = localStorage.getItem('authToken');
        return {
            'Content-Type': 'application/json',
            'Authorization': token ? `Bearer ${token}` : ''
        };
    }

    // === Utility Methods ===

    /**
     * Escape HTML to prevent XSS
     * @private
     */
    _escape(str) {
        if (str === null || str === undefined) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    }

    /**
     * Show confirmation dialog
     * @private
     */
    async _confirm(options) {
        if (window.ConfirmDialog) {
            return ConfirmDialog.show(options);
        }
        return confirm(options.message);
    }

    /**
     * Show success notification
     * @private
     */
    _showSuccess(message) {
        if (window.Toast) {
            Toast.success('Success', message);
        }
    }

    /**
     * Show error notification
     * @private
     */
    _showError(message) {
        if (window.Toast) {
            Toast.error('Error', message);
        }
    }

    /**
     * Show info notification
     * @private
     */
    _showInfo(title, message) {
        if (window.Toast) {
            Toast.info(title, message);
        }
    }

    /**
     * Emit event via EventBus
     * @private
     */
    _emit(event, data = {}) {
        if (this.eventBus) {
            this.eventBus.emit(event, data);
        }
    }

    /**
     * Destroy the module and clean up
     */
    destroy() {
        if (this.container) {
            this.container.removeEventListener('click', this._handleSaveClick);
        }
        this.settings = [];
        this.groupedSettings.clear();
        this.container = null;
        this.isInitialized = false;
    }
}

// Export for module usage
window.SettingsModule = SettingsModule;

// Auto-initialize when DOM is ready
document.addEventListener('DOMContentLoaded', function() {
    const container = document.getElementById('settingsPage');
    if (!container) return;

    const initWhenReady = () => {
        const isAuthenticated = (typeof currentUser !== 'undefined' && currentUser) ||
                               (window.App && window.App.isAuthenticated && window.App.isAuthenticated());

        if (!isAuthenticated) {
            setTimeout(initWhenReady, 200);
            return;
        }

        if (window.settingsModule) {
            window.settingsModule.load();
            return;
        }

        window.settingsModule = new SettingsModule({
            api: window.apiService || (window.App && window.App.api),
            eventBus: window.eventBus || (window.App && window.App.events)
        });

        window.settingsModule.init();
        window.settingsModule.load();
    };

    initWhenReady();
});
