/**
 * PaymentIntegrationModule
 *
 * ClinicAdmin UI for managing Stripe Connect onboarding and account linking.
 * Loads connected accounts + locations, lets the admin connect new accounts
 * via Stripe-hosted onboarding, link existing accounts, or disconnect.
 */
class PaymentIntegrationModule {
    // Polling cadence for non-final accounts. Stripe verification typically
    // finishes within 2 to 4 minutes; 15s tick keeps the UI responsive without
    // hammering the API. Max duration is a hard ceiling after which we stop
    // polling and surface a manual fallback.
    static POLL_INTERVAL_MS = 15000;
    static POLL_MAX_DURATION_MS = 6 * 60 * 1000;

    constructor() {
        this.accounts = [];
        this.locations = [];
        this.isInitialized = false;

        // Polling state for non-final (Pending/Restricted) accounts.
        this._pollIntervalId = null;
        this._pollStartedAt = 0;
        this._lastCheckedAt = 0;
        this._bannerTickerId = null;
        this._previousNonFinalIds = new Set();
        this._pollTimedOut = false;
        this._visibilityHandler = null;
        this._pageHideHandler = null;
    }

    async init() {
        const page = document.getElementById('paymentIntegrationPage');
        if (!page) return;

        // Pause polling while tab is hidden (saves API hits when user is on
        // another tab). On return, fire an immediate refresh so they don't
        // wait the full interval.
        this._visibilityHandler = () => {
            if (!this._pollIntervalId) return;
            if (!document.hidden) {
                this._pollTick({ immediate: true });
            }
        };
        document.addEventListener('visibilitychange', this._visibilityHandler);

        // Clean up timers if the page is being unloaded.
        this._pageHideHandler = () => this._stopPolling();
        window.addEventListener('pagehide', this._pageHideHandler);

        await this.load();
        this.isInitialized = true;
    }

    async load() {
        try {
            const [accounts, locations] = await Promise.all([
                this._fetch('/api/stripe-connect/accounts'),
                this._fetch('/api/locations')
            ]);

            // Normalize in case backend returns PascalCase property names.
            // Adds camelCase aliases on every object without losing originals.
            this.accounts = this._camelize(accounts || []);
            this.locations = this._camelize(locations || []).filter(l => l.isActive !== false);

            this._renderAccounts();
            this._renderLocations();

            // Decide what to do based on whether non-final accounts exist.
            // _evaluatePollingState handles: detecting status transitions
            // (toast on Active), starting/stopping the background poller,
            // and rendering the verification banner.
            //
            // Why polling instead of relying on Stripe webhooks:
            //   Stripe is shifting Connect lifecycle to v2 thin events which
            //   require a separate webhook destination + signature scheme.
            //   For an internal admin page visited rarely, polling on page
            //   load + every 15s while non-final is simpler, more reliable,
            //   and self-healing.
            // Detect Stripe redirect once, then strip the query param so the
            // recursive load() inside the poll tick doesn't see it again.
            // Without stripping, every recursive load would re-trigger the
            // immediate-refresh path and the "completed" toast.
            const params = new URLSearchParams(window.location.search);
            const cameFromStripe = params.get('stripe') === 'success';
            if (cameFromStripe) {
                this._showToast('Stripe onboarding completed. Verifying status...', 'success');
                params.delete('stripe');
                const newQuery = params.toString();
                const newUrl = window.location.pathname + (newQuery ? '?' + newQuery : '') + window.location.hash;
                window.history.replaceState({}, '', newUrl);
            }
            await this._evaluatePollingState({ cameFromStripe });
        } catch (err) {
            console.error('[PaymentIntegrationModule] Load error', err);
            this._showToast('Failed to load payment integration data: ' + err.message, 'error');
        }
    }

    _renderAccounts() {
        const container = document.getElementById('paymentIntegrationAccountsList');
        if (!container) return;

        if (this.accounts.length === 0) {
            container.innerHTML = `
                <div class="text-center text-muted py-4">
                    <i class="bi bi-bank2" style="font-size:2rem;"></i>
                    <p class="mt-2">No Stripe accounts connected yet.</p>
                    <p class="small">Use the Locations section below to connect a Stripe account.</p>
                </div>
            `;
            return;
        }

        const rows = this.accounts.map(a => {
            const statusClass = this._statusBadgeClass(a.statusName);
            const linked = (a.linkedLocations || []).map(l => l.name).join(', ') || '<em class="text-muted">none</em>';
            return `
                <tr>
                    <td>
                        <strong>${this._escape(a.displayName)}</strong>
                        <div class="small text-muted">${this._escape(a.businessEmail || '')}</div>
                    </td>
                    <td><span class="badge ${statusClass}">${a.statusName}</span></td>
                    <td>
                        <div class="small">
                            Charges: ${a.chargesEnabled ? '<i class="bi bi-check-circle text-success"></i>' : '<i class="bi bi-x-circle text-danger"></i>'}
                            Payouts: ${a.payoutsEnabled ? '<i class="bi bi-check-circle text-success"></i>' : '<i class="bi bi-x-circle text-danger"></i>'}
                        </div>
                    </td>
                    <td class="small">${linked}</td>
                    <td class="text-end">
                        ${a.statusName === 'Pending' ? `
                            <button class="btn btn-outline-primary me-1" onclick="window._paymentIntegrationModule.refreshOnboarding(${a.stripeConnectAccountId}, this)">
                                <i class="bi bi-arrow-clockwise me-1"></i>Continue Onboarding
                            </button>` : ''}
                        ${a.statusName === 'Active' ? `
                            <a href="https://dashboard.stripe.com/${a.stripeAccountId}" target="_blank" class="btn btn-outline-secondary me-1">
                                <i class="bi bi-box-arrow-up-right me-1"></i>Stripe Dashboard
                            </a>` : ''}
                        ${a.statusName !== 'Disconnected' ? `
                            <button class="btn btn-outline-danger" onclick="window._paymentIntegrationModule.disconnect(${a.stripeConnectAccountId}, '${this._escape(a.displayName)}', this)">
                                <i class="bi bi-link-45deg me-1"></i>Disconnect
                            </button>` : ''}
                    </td>
                </tr>
            `;
        }).join('');

        container.innerHTML = `
            <table class="table table-hover">
                <thead><tr>
                    <th>Account</th>
                    <th>Status</th>
                    <th>Capabilities</th>
                    <th>Linked Locations</th>
                    <th></th>
                </tr></thead>
                <tbody>${rows}</tbody>
            </table>
        `;
    }

    _renderLocations() {
        const container = document.getElementById('paymentIntegrationLocationsList');
        if (!container) return;

        if (this.locations.length === 0) {
            container.innerHTML = '<p class="text-muted">No active locations found.</p>';
            return;
        }

        // Build locationId -> account map by walking each account's linkedLocations list.
        // Tolerant of both camelCase and PascalCase JSON property names.
        const locationToAccount = new Map();
        for (const a of this.accounts) {
            const links = a.linkedLocations || a.LinkedLocations || [];
            for (const ll of links) {
                const lid = ll.locationId ?? ll.LocationId;
                if (lid != null) locationToAccount.set(lid, a);
            }
        }

        const rows = this.locations.map(loc => {
            // Accept either camelCase or PascalCase — defensive against DTO serialization
            const locId = loc.locationId ?? loc.LocationId;
            const locName = loc.name ?? loc.Name ?? '';
            const locAddress = loc.address ?? loc.Address ?? '';
            const locCity = loc.city ?? loc.City ?? '';

            const account = locationToAccount.get(locId) || null;
            const isConnected = account && account.statusName === 'Active';
            const isPending = account && account.statusName === 'Pending';

            return `
                <tr>
                    <td>
                        <strong>${this._escape(locName)}</strong>
                        <div class="small text-muted">${this._escape(locAddress + (locCity ? ', ' + locCity : ''))}</div>
                    </td>
                    <td>
                        ${account ? `
                            <span class="badge ${this._statusBadgeClass(account.statusName)}">${account.statusName}</span>
                            <div class="small">${this._escape(account.displayName)}</div>
                        ` : '<span class="badge bg-secondary">Not Connected</span>'}
                    </td>
                    <td class="text-end">
                        ${!account ? `
                            <button class="btn btn-primary" onclick="window._paymentIntegrationModule.openConnectModal(${locId}, '${this._escape(locName)}')">
                                <i class="bi bi-plus-circle me-1"></i>Connect Stripe
                            </button>
                        ` : isPending ? `
                            <button class="btn btn-warning" onclick="window._paymentIntegrationModule.refreshOnboarding(${account.stripeConnectAccountId}, this)">
                                <i class="bi bi-arrow-clockwise me-1"></i>Resume Onboarding
                            </button>
                        ` : isConnected ? `
                            <span class="text-success"><i class="bi bi-check-circle me-1"></i>Ready to accept payments</span>
                        ` : `
                            <span class="text-danger"><i class="bi bi-exclamation-triangle me-1"></i>${this._escape(account.statusName)}</span>
                        `}
                    </td>
                </tr>
            `;
        }).join('');

        container.innerHTML = `
            <table class="table table-hover">
                <thead><tr>
                    <th>Location</th>
                    <th>Stripe Status</th>
                    <th></th>
                </tr></thead>
                <tbody>${rows}</tbody>
            </table>
        `;
    }

    openConnectModal(locationId, locationName) {
        document.getElementById('connectStripeLocationId').value = locationId;
        document.getElementById('connectStripeLocationName').textContent = locationName;
        document.getElementById('connectStripeBusinessEmail').value = '';

        // Pre-populate the existing accounts dropdown if there are reusable accounts
        const reusable = this.accounts.filter(a => a.statusName === 'Active');
        const dropdown = document.getElementById('connectStripeExistingDropdown');
        const optionsContainer = document.getElementById('connectStripeOptionsExisting');
        const divider = document.getElementById('connectStripeDivider');

        if (reusable.length > 0) {
            dropdown.innerHTML = '<option value="">Choose existing account</option>'
                + reusable.map(a => `<option value="${a.stripeConnectAccountId}">${this._escape(a.displayName)}</option>`).join('');
            optionsContainer.style.display = 'block';
            divider.style.display = 'block';
        } else {
            optionsContainer.style.display = 'none';
            divider.style.display = 'none';
        }

        const modal = new bootstrap.Modal(document.getElementById('connectStripeModal'));
        modal.show();
    }

    async startOnboarding() {
        const rawValue = document.getElementById('connectStripeLocationId').value;
        const locationId = parseInt(rawValue, 10);
        const businessEmail = document.getElementById('connectStripeBusinessEmail').value.trim();

        if (!businessEmail) {
            this._showToast('Please enter a business email address.', 'warning');
            return;
        }

        if (!locationId || Number.isNaN(locationId)) {
            this._showToast('Invalid location ID. Please close and re-open this dialog.', 'error');
            return;
        }

        const btn = document.querySelector('#connectStripeModal .modal-footer .btn-primary');

        await this._withButtonSpinner(btn, 'Preparing Stripe onboarding…', async () => {
            try {
                const result = await this._fetch('/api/stripe-connect/start-onboarding', {
                    method: 'POST',
                    body: JSON.stringify({ locationId, businessEmail })
                });

                // Support both camelCase and PascalCase response
                const onboardingUrl = result.onboardingUrl || result.OnboardingUrl;
                if (onboardingUrl) {
                    bootstrap.Modal.getInstance(document.getElementById('connectStripeModal'))?.hide();
                    this._showToast('Redirecting to Stripe onboarding…', 'info');
                    // Redirect is happening — leave the spinner state until the browser navigates away
                    setTimeout(() => { window.location.href = onboardingUrl; }, 400);
                    // Return a never-resolving promise so _withButtonSpinner leaves the spinner in place
                    return new Promise(() => {});
                } else {
                    this._showToast('Server did not return an onboarding URL.', 'error');
                }
            } catch (err) {
                this._showToast('Failed to start onboarding: ' + err.message, 'error');
            }
        });
    }

    async linkExisting() {
        const locationId = parseInt(document.getElementById('connectStripeLocationId').value, 10);
        const stripeConnectAccountId = parseInt(document.getElementById('connectStripeExistingDropdown').value, 10);

        if (!stripeConnectAccountId) {
            this._showToast('Please choose an existing account.', 'warning');
            return;
        }

        const btn = document.querySelector('#connectStripeOptionsExisting .btn-outline-primary');

        await this._withButtonSpinner(btn, 'Linking…', async () => {
            try {
                await this._fetch('/api/stripe-connect/link-existing', {
                    method: 'POST',
                    body: JSON.stringify({ locationId, stripeConnectAccountId })
                });

                bootstrap.Modal.getInstance(document.getElementById('connectStripeModal'))?.hide();
                this._showToast('Location linked to existing Stripe account.', 'success');
                await this.load();
            } catch (err) {
                this._showToast('Failed to link account: ' + err.message, 'error');
            }
        });
    }

    async refreshOnboarding(stripeConnectAccountId, btn) {
        await this._withButtonSpinner(btn, 'Preparing Stripe onboarding…', async () => {
            try {
                const result = await this._fetch('/api/stripe-connect/refresh-onboarding-link', {
                    method: 'POST',
                    body: JSON.stringify({ stripeConnectAccountId })
                });
                const onboardingUrl = result.onboardingUrl || result.OnboardingUrl;
                if (onboardingUrl) {
                    this._showToast('Redirecting to Stripe onboarding…', 'info');
                    setTimeout(() => { window.location.href = onboardingUrl; }, 400);
                    // Keep spinner in place until navigation
                    return new Promise(() => {});
                }
            } catch (err) {
                this._showToast('Failed to refresh onboarding link: ' + err.message, 'error');
            }
        });
    }

    async disconnect(stripeConnectAccountId, displayName, btn) {
        if (!confirm(`Disconnect "${displayName}" from MEDOCS?\n\nAll locations using this account will no longer be able to accept payments until reconnected.`)) {
            return;
        }

        await this._withButtonSpinner(btn, 'Disconnecting…', async () => {
            try {
                await this._fetch(`/api/stripe-connect/disconnect/${stripeConnectAccountId}`, { method: 'POST' });
                this._showToast('Stripe account disconnected.', 'success');
                await this.load();
            } catch (err) {
                this._showToast('Failed to disconnect: ' + err.message, 'error');
            }
        });
    }

    /**
     * Pulls live state from Stripe for any account that hasn't reached a final
     * status (Active or Disconnected). Restricted accounts must be included
     * because Stripe leaves new Connect accounts in Restricted until async
     * capability verification finishes — once it does, no reliable v1 webhook
     * fires, so the only way our DB learns is by calling refresh-status.
     *
     * Returns the number of accounts that were refreshed (0 if all were final).
     * Does NOT trigger a re-render itself — the caller is responsible for that
     * (typically by awaiting `load()` afterward). This keeps the polling loop
     * in `_pollTick` in control of when re-renders happen.
     */
    async _refreshNonFinalAccounts() {
        const needsRefresh = this.accounts.filter(a =>
            a.statusName === 'Pending' || a.statusName === 'Restricted');
        if (needsRefresh.length === 0) return 0;
        for (const a of needsRefresh) {
            try {
                await this._fetch(`/api/stripe-connect/refresh-status/${a.stripeConnectAccountId}`, { method: 'POST' });
            } catch (e) {
                console.warn('Failed to refresh status', e);
            }
        }
        this._lastCheckedAt = Date.now();
        return needsRefresh.length;
    }

    /**
     * The orchestrator called at the end of every `load()`. Decides whether to
     * start polling, stop polling, or just update the banner. Also fires a
     * success toast when an account transitions from non-final to Active so
     * the user sees confirmation even if their eyes are off the row.
     *
     * @param {{cameFromStripe?: boolean}} opts Forces an immediate refresh on
     *   first load after Stripe redirect so the user doesn't wait 15s.
     */
    async _evaluatePollingState(opts = {}) {
        // Detect transitions: any account that was non-final last load and is
        // now Active deserves a toast.
        const currentNonFinalIds = new Set(
            this.accounts
                .filter(a => a.statusName === 'Pending' || a.statusName === 'Restricted')
                .map(a => a.stripeConnectAccountId));

        const justBecameActive = this.accounts.filter(a =>
            a.statusName === 'Active' &&
            this._previousNonFinalIds.has(a.stripeConnectAccountId));

        for (const a of justBecameActive) {
            const label = a.displayName || a.businessEmail || 'Connected account';
            this._showToast(`${label} is now active. Ready to accept payments.`, 'success');
        }

        this._previousNonFinalIds = currentNonFinalIds;

        if (currentNonFinalIds.size === 0) {
            // All accounts final — tear down polling + banner.
            this._stopPolling();
            return;
        }

        // Non-final accounts exist. Ensure banner is on screen and polling is
        // running. On first time through (or after a Stripe return) do an
        // immediate refresh so the user doesn't wait a full interval.
        const justStarted = !this._pollIntervalId;
        this._startPolling();
        this._renderVerifyingBanner();

        if (justStarted || opts.cameFromStripe) {
            // Reset polling clock if we came back from Stripe — gives the user
            // a fresh 6-minute window for this verification cycle.
            if (opts.cameFromStripe) {
                this._pollStartedAt = Date.now();
                this._pollTimedOut = false;
            }
            await this._pollTick({ immediate: true });
        }
    }

    /**
     * Runs one poll cycle. Refreshes all non-final accounts, then re-loads.
     * Skipped if the tab is hidden (Page Visibility API saves API hits while
     * the user is elsewhere). Stops polling if max duration exceeded.
     */
    async _pollTick(opts = {}) {
        if (this._pollTimedOut) return;
        if (!opts.immediate && document.hidden) return;

        const elapsed = Date.now() - this._pollStartedAt;
        if (elapsed > PaymentIntegrationModule.POLL_MAX_DURATION_MS) {
            this._pollTimedOut = true;
            this._stopPollingInterval();
            this._renderVerifyingBanner();
            return;
        }

        if (this._tickInFlight) return;
        this._tickInFlight = true;
        try {
            const refreshed = await this._refreshNonFinalAccounts();
            if (refreshed === 0) {
                // Nothing to do — defensive, _evaluatePollingState should've
                // already torn down polling in this case.
                this._stopPolling();
                return;
            }
            await this.load();
        } finally {
            this._tickInFlight = false;
        }
    }

    _startPolling() {
        if (this._pollIntervalId) return;
        this._pollStartedAt = Date.now();
        this._pollTimedOut = false;
        this._pollIntervalId = setInterval(() => this._pollTick(), PaymentIntegrationModule.POLL_INTERVAL_MS);
        // Refresh the "Last checked X ago" text every 5s so it feels live.
        this._bannerTickerId = setInterval(() => this._updateBannerTimer(), 5000);
    }

    _stopPollingInterval() {
        if (this._pollIntervalId) {
            clearInterval(this._pollIntervalId);
            this._pollIntervalId = null;
        }
        if (this._bannerTickerId) {
            clearInterval(this._bannerTickerId);
            this._bannerTickerId = null;
        }
    }

    _stopPolling() {
        this._stopPollingInterval();
        this._pollTimedOut = false;
        this._removeVerifyingBanner();
    }

    /**
     * Inject or update the verification banner above the connected accounts
     * card. Created once, then mutated. Removed by `_removeVerifyingBanner`.
     */
    _renderVerifyingBanner() {
        let banner = document.getElementById('paymentIntegrationVerifyingBanner');
        const accountsCard = document.querySelector('#paymentIntegrationAccountsList')?.closest('.card');
        if (!accountsCard) return;

        if (!banner) {
            banner = document.createElement('div');
            banner.id = 'paymentIntegrationVerifyingBanner';
            banner.className = 'alert alert-info d-flex align-items-start mb-3';
            accountsCard.parentNode.insertBefore(banner, accountsCard);
        }

        if (this._pollTimedOut) {
            banner.classList.remove('alert-info');
            banner.classList.add('alert-warning');
            banner.innerHTML = `
                <i class="bi bi-exclamation-triangle me-2 mt-1"></i>
                <div class="flex-grow-1">
                    <strong>Verification is taking longer than usual.</strong>
                    <div class="small text-muted">Stripe is still verifying your account. You can check again or come back in a few minutes.</div>
                </div>
                <button type="button" class="btn btn-outline-primary ms-3" id="paymentIntegrationCheckNowBtn">
                    <i class="bi bi-arrow-clockwise me-1"></i>Check now
                </button>
            `;
        } else {
            banner.classList.remove('alert-warning');
            banner.classList.add('alert-info');
            banner.innerHTML = `
                <div class="spinner-border spinner-border-sm text-info me-2 mt-1" role="status" aria-hidden="true"></div>
                <div class="flex-grow-1">
                    <strong>Stripe is verifying your connected account.</strong>
                    <div class="small text-muted">
                        Status will update automatically (usually 2 to 4 minutes).
                        <span id="paymentIntegrationLastChecked"></span>
                    </div>
                </div>
                <button type="button" class="btn btn-outline-primary ms-3" id="paymentIntegrationCheckNowBtn">
                    <i class="bi bi-arrow-clockwise me-1"></i>Check now
                </button>
            `;
        }

        const checkNowBtn = banner.querySelector('#paymentIntegrationCheckNowBtn');
        if (checkNowBtn) {
            checkNowBtn.onclick = () => this._handleCheckNowClick(checkNowBtn);
        }

        this._updateBannerTimer();
    }

    _removeVerifyingBanner() {
        const banner = document.getElementById('paymentIntegrationVerifyingBanner');
        if (banner) banner.remove();
    }

    _updateBannerTimer() {
        const el = document.getElementById('paymentIntegrationLastChecked');
        if (!el) return;
        if (!this._lastCheckedAt) {
            el.textContent = '';
            return;
        }
        const secondsAgo = Math.max(0, Math.round((Date.now() - this._lastCheckedAt) / 1000));
        if (secondsAgo < 5) {
            el.textContent = 'Last checked just now.';
        } else if (secondsAgo < 60) {
            el.textContent = `Last checked ${secondsAgo} seconds ago.`;
        } else {
            const min = Math.floor(secondsAgo / 60);
            const sec = secondsAgo % 60;
            el.textContent = `Last checked ${min}m ${sec}s ago.`;
        }
    }

    async _handleCheckNowClick(btn) {
        await this._withButtonSpinner(btn, 'Checking…', async () => {
            // Reset the 6-minute timeout window so a manual click after timeout
            // gets a fresh chance to auto-poll.
            if (this._pollTimedOut) {
                this._pollTimedOut = false;
                this._pollStartedAt = Date.now();
                // Re-establish the interval that was cleared on timeout.
                if (!this._pollIntervalId) {
                    this._pollIntervalId = setInterval(() => this._pollTick(), PaymentIntegrationModule.POLL_INTERVAL_MS);
                    this._bannerTickerId = setInterval(() => this._updateBannerTimer(), 5000);
                }
            }
            await this._pollTick({ immediate: true });
        });
    }

    // ============================================
    // Helpers
    // ============================================

    async _fetch(url, options = {}) {
        const token = localStorage.getItem('authToken');
        const headers = { 'Content-Type': 'application/json' };
        if (token) headers['Authorization'] = 'Bearer ' + token;

        const response = await fetch(url, { ...options, headers });

        if (!response.ok) {
            const text = await response.text();
            let message = `HTTP ${response.status}`;
            try {
                const parsed = JSON.parse(text);
                // Handle common error shapes: our own {message}, ASP.NET ProblemDetails {title}, ModelState {errors}
                message = parsed.message
                    || parsed.title
                    || (parsed.errors ? JSON.stringify(parsed.errors) : null)
                    || message;
            } catch {}
            throw new Error(message);
        }

        return response.status === 204 ? null : response.json();
    }

    /**
     * Recursively walks a value and adds camelCase aliases for any PascalCase keys
     * on plain objects. Arrays are walked element-by-element. Primitives pass through.
     * Preserves original keys so downstream code can use either naming style.
     * Needed because the backend may serialize anonymous objects with PascalCase names.
     */
    _camelize(value) {
        if (Array.isArray(value)) {
            return value.map(v => this._camelize(v));
        }
        if (value && typeof value === 'object' && value.constructor === Object) {
            const result = {};
            for (const [key, v] of Object.entries(value)) {
                const processed = this._camelize(v);
                result[key] = processed;
                if (/^[A-Z]/.test(key)) {
                    const camel = key.charAt(0).toLowerCase() + key.slice(1);
                    if (!(camel in result)) {
                        result[camel] = processed;
                    }
                }
            }
            return result;
        }
        return value;
    }

    /**
     * Wraps an async operation with a button spinner state.
     * - Disables the button and replaces its content with a spinner + label for the duration.
     * - Restores the original content and disabled state on completion (success or error).
     * - If the action returns a never-resolving promise (e.g. for redirects), the spinner stays on.
     * @param {HTMLElement|null} btn The button to show the spinner on. If null, runs without spinner.
     * @param {string} label Text shown next to the spinner (e.g. "Preparing Stripe onboarding…").
     * @param {Function} asyncFn The async function to run.
     */
    async _withButtonSpinner(btn, label, asyncFn) {
        if (!btn) { return await asyncFn(); }
        const originalHTML = btn.innerHTML;
        const originalDisabled = btn.disabled;
        btn.disabled = true;
        btn.innerHTML = `<span class="spinner-border spinner-border-sm me-2" role="status" aria-hidden="true"></span>${label}`;
        try {
            return await asyncFn();
        } finally {
            btn.innerHTML = originalHTML;
            btn.disabled = originalDisabled;
        }
    }

    _statusBadgeClass(statusName) {
        switch (statusName) {
            case 'Active': return 'bg-success';
            case 'Pending': return 'bg-warning text-dark';
            case 'Restricted': return 'bg-danger';
            case 'Disconnected': return 'bg-secondary';
            default: return 'bg-secondary';
        }
    }

    _escape(str) {
        if (str == null) return '';
        return String(str)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    _showToast(message, type) {
        // Try to use existing toast system if available
        if (window.App?.showToast) {
            window.App.showToast(message, type);
            return;
        }
        // Fallback: alert for now
        console.log(`[${type}] ${message}`);
        if (type === 'error') alert(message);
    }
}

// Auto-init on page load
const _paymentIntegrationModule = new PaymentIntegrationModule();
window._paymentIntegrationModule = _paymentIntegrationModule;
document.addEventListener('DOMContentLoaded', () => {
    _paymentIntegrationModule.init();
});

export default PaymentIntegrationModule;
