/**
 * LoadingState - Global loading state management
 * Tracks pending requests and shows/hides loading overlay
 *
 * Usage:
 *   LoadingState.show();
 *   LoadingState.hide();
 *   LoadingState.isLoading();
 */
const LoadingState = {
    // Pending request counter
    pendingRequests: 0,

    // DOM elements
    loaderElement: null,
    appLoadingScreen: null,

    /**
     * Initialize loading state elements
     */
    init() {
        this.loaderElement = document.getElementById('globalLoader');
        this.appLoadingScreen = document.getElementById('appLoadingScreen');
    },

    /**
     * Show loading indicator
     */
    show() {
        this.pendingRequests++;

        if (!this.loaderElement) {
            this.init();
        }

        if (this.loaderElement && this.pendingRequests > 0) {
            this.loaderElement.classList.remove('d-none');
        }
    },

    /**
     * Hide loading indicator (decrements counter)
     */
    hide() {
        this.pendingRequests--;

        if (this.pendingRequests < 0) {
            this.pendingRequests = 0;
        }

        if (!this.loaderElement) {
            this.init();
        }

        if (this.loaderElement && this.pendingRequests === 0) {
            this.loaderElement.classList.add('d-none');
        }
    },

    /**
     * Force hide loading indicator (resets counter)
     */
    forceHide() {
        this.pendingRequests = 0;

        if (!this.loaderElement) {
            this.init();
        }

        if (this.loaderElement) {
            this.loaderElement.classList.add('d-none');
        }
    },

    /**
     * Check if currently loading
     * @returns {boolean}
     */
    isLoading() {
        return this.pendingRequests > 0;
    },

    /**
     * Get pending request count
     * @returns {number}
     */
    getPendingCount() {
        return this.pendingRequests;
    },

    /**
     * Hide the app loading screen (initial boot screen)
     */
    hideAppLoadingScreen() {
        if (!this.appLoadingScreen) {
            this.appLoadingScreen = document.getElementById('appLoadingScreen');
        }

        if (this.appLoadingScreen) {
            this.appLoadingScreen.classList.add('d-none');
        }
    },

    /**
     * Update the app loading status message
     * @param {string} message - Status message
     */
    updateAppLoadingStatus(message) {
        const statusEl = document.querySelector('.app-loading-status');
        if (statusEl) {
            statusEl.textContent = message;
        }
    },

    /**
     * Set loading text
     * @param {string} text - Loading text
     */
    setLoadingText(text) {
        if (!this.loaderElement) {
            this.init();
        }

        const textEl = this.loaderElement?.querySelector('.loader-text');
        if (textEl) {
            textEl.textContent = text;
        }
    },

    /**
     * Reset loading text to default
     */
    resetLoadingText() {
        this.setLoadingText('Loading...');
    }
};

// Export for global access
window.LoadingState = LoadingState;

// Backward compatibility
window.showGlobalLoader = LoadingState.show.bind(LoadingState);
window.hideGlobalLoader = LoadingState.hide.bind(LoadingState);
window.hideAppLoadingScreen = LoadingState.hideAppLoadingScreen.bind(LoadingState);
window.updateAppLoadingStatus = LoadingState.updateAppLoadingStatus.bind(LoadingState);
