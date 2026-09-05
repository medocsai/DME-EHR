/**
 * ApiService - Centralized HTTP API wrapper
 * Handles all API calls with authentication, loading states, and error handling
 *
 * Usage:
 *   const data = await App.api.get('/patients');
 *   const result = await App.api.post('/patients', patientData);
 *   await App.api.put('/patients/123', updateData);
 *   await App.api.delete('/patients/123');
 */
class ApiService {
    constructor(options = {}) {
        this.baseUrl = options.baseUrl || '/api';
        this.authToken = null;
        this.defaultHeaders = {
            'Content-Type': 'application/json'
        };
    }

    /**
     * Set the authentication token
     * @param {string} token - JWT token
     */
    /**
     * The session slides on activity, and a renewed token comes back on this
     * header. Every response is checked, because any request can be the one that
     * crossed the halfway mark. Silent by design: nothing about the call changes.
     */
    _takeRenewedToken(response) {
        try {
            const renewed = response && response.headers && response.headers.get('X-Session-Token');
            if (renewed) {
                this.authToken = renewed;
                if (window.App && App.auth && App.auth.applyRenewedToken) {
                    App.auth.applyRenewedToken(renewed);
                }
            }
        } catch (e) {
            // Never let housekeeping break the caller's request.
        }
    }

    setAuthToken(token) {
        this.authToken = token;
    }

    /**
     * Get the current auth token
     * @returns {string|null} Current token
     */
    getAuthToken() {
        return this.authToken;
    }

    /**
     * Clear the authentication token
     */
    clearAuthToken() {
        this.authToken = null;
    }

    /**
     * Build headers for a request
     * @param {Object} customHeaders - Additional headers
     * @returns {Object} Combined headers
     */
    buildHeaders(customHeaders = {}) {
        const headers = { ...this.defaultHeaders, ...customHeaders };

        if (this.authToken) {
            headers['Authorization'] = `Bearer ${this.authToken}`;
        }

        return headers;
    }

    /**
     * Make an API request
     * @param {string} endpoint - API endpoint (e.g., '/patients')
     * @param {Object} options - Request options
     * @param {string} [options.method='GET'] - HTTP method
     * @param {Object} [options.body] - Request body (will be JSON stringified)
     * @param {boolean} [options.showLoader=true] - Show global loader
     * @param {boolean} [options.showErrors=true] - Show error toasts
     * @param {Object} [options.headers] - Additional headers
     * @returns {Promise<*>} Response data
     */
    async request(endpoint, options = {}) {
        const {
            method = 'GET',
            body = null,
            showLoader = true,
            showErrors = true,
            headers: customHeaders = {}
        } = options;

        // Show loading indicator
        if (showLoader && window.LoadingState) {
            LoadingState.show();
        }

        try {
            const url = `${this.baseUrl}${endpoint}`;
            const headers = this.buildHeaders(customHeaders);

            const fetchOptions = {
                method,
                headers
            };

            if (body && method !== 'GET') {
                fetchOptions.body = JSON.stringify(body);
            }

            const response = await fetch(url, fetchOptions);
            this._takeRenewedToken(response);

            // Handle 401 Unauthorized
            if (response.status === 401) {
                if (window.App && App.auth) {
                    App.auth.logout();
                }
                return null;
            }

            // Handle error responses
            if (!response.ok) {
                let errorMessage = '';
                try {
                    const json = await response.json();
                    errorMessage = json?.message || json?.Message || JSON.stringify(json);
                } catch {
                    try {
                        errorMessage = await response.text();
                    } catch {
                        errorMessage = '';
                    }
                }

                const fullMessage = `HTTP ${response.status} ${response.statusText}${errorMessage ? ' - ' + errorMessage : ''}`;
                console.error('API Error:', { url, status: response.status, body: errorMessage });

                if (showErrors && window.Toast) {
                    Toast.error('API Error', fullMessage);
                }

                throw new ApiError(fullMessage, response.status, errorMessage);
            }

            // Handle 204 No Content
            if (response.status === 204) {
                return null;
            }

            // Parse response
            const text = await response.text();
            if (!text) return null;

            try {
                return JSON.parse(text);
            } catch {
                return text;
            }

        } catch (error) {
            if (!(error instanceof ApiError)) {
                console.error('API Request Error:', error);
                if (showErrors && window.Toast) {
                    Toast.error('Network Error', 'Failed to connect to server');
                }
            }
            throw error;
        } finally {
            if (showLoader && window.LoadingState) {
                LoadingState.hide();
            }
        }
    }

    /**
     * GET request
     * @param {string} endpoint - API endpoint
     * @param {Object} [options] - Request options
     * @returns {Promise<*>} Response data
     */
    get(endpoint, options = {}) {
        return this.request(endpoint, { ...options, method: 'GET' });
    }

    /**
     * POST request
     * @param {string} endpoint - API endpoint
     * @param {Object} body - Request body
     * @param {Object} [options] - Request options
     * @returns {Promise<*>} Response data
     */
    post(endpoint, body, options = {}) {
        return this.request(endpoint, { ...options, method: 'POST', body });
    }

    /**
     * PUT request
     * @param {string} endpoint - API endpoint
     * @param {Object} body - Request body
     * @param {Object} [options] - Request options
     * @returns {Promise<*>} Response data
     */
    put(endpoint, body, options = {}) {
        return this.request(endpoint, { ...options, method: 'PUT', body });
    }

    /**
     * PATCH request
     * @param {string} endpoint - API endpoint
     * @param {Object} body - Request body
     * @param {Object} [options] - Request options
     * @returns {Promise<*>} Response data
     */
    patch(endpoint, body, options = {}) {
        return this.request(endpoint, { ...options, method: 'PATCH', body });
    }

    /**
     * DELETE request
     * @param {string} endpoint - API endpoint
     * @param {Object} [options] - Request options
     * @returns {Promise<*>} Response data
     */
    delete(endpoint, options = {}) {
        return this.request(endpoint, { ...options, method: 'DELETE' });
    }

    /**
     * Upload a file
     * @param {string} endpoint - API endpoint
     * @param {FormData} formData - Form data with file
     * @param {Object} [options] - Request options
     * @returns {Promise<*>} Response data
     */
    async upload(endpoint, formData, options = {}) {
        const { showLoader = true, showErrors = true } = options;

        if (showLoader && window.LoadingState) {
            LoadingState.show();
        }

        try {
            const url = `${this.baseUrl}${endpoint}`;
            const headers = {};

            if (this.authToken) {
                headers['Authorization'] = `Bearer ${this.authToken}`;
            }
            // Don't set Content-Type for FormData - browser will set it with boundary

            const response = await fetch(url, {
                method: 'POST',
                headers,
                body: formData
            });
            this._takeRenewedToken(response);

            if (!response.ok) {
                let errorMessage = '';
                try {
                    const json = await response.json();
                    errorMessage = json?.message || JSON.stringify(json);
                } catch {
                    errorMessage = await response.text();
                }

                if (showErrors && window.Toast) {
                    Toast.error('Upload Error', errorMessage);
                }

                throw new ApiError(errorMessage, response.status);
            }

            return await response.json();

        } finally {
            if (showLoader && window.LoadingState) {
                LoadingState.hide();
            }
        }
    }
}

/**
 * Custom API Error class
 */
class ApiError extends Error {
    constructor(message, status, body = null) {
        super(message);
        this.name = 'ApiError';
        this.status = status;
        this.body = body;
    }
}

// Export classes
window.ApiService = ApiService;
window.ApiError = ApiError;
