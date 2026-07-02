/**
 * BookingProviderStep (Hired Guy)
 *
 * Why: let patients pick a specific provider, or fall back to "Any Available".
 * What: fetches /api/portal/providers on render, paints "Any Available"
 *       (recommended/default) plus one card per provider. Selection updates state;
 *       changing provider voids any previously selected slot (handled by setting
 *       selectedSlot:null in patch).
 * Who calls: PortalBookingWizard.
 *
 * onLoaded(providers) — if the wizard wants to know how many bookable providers
 *   exist (e.g. to auto-skip this step when there's only 1), it passes a callback.
 */
(function () {
    'use strict';

    function escapeHtml(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, c => ({
            '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
        }[c]));
    }

    class BookingProviderStep {
        constructor(state, api, options) {
            this.state = state;
            this.api = api;
            this.onLoaded = (options && options.onLoaded) || null;
            this.container = null;
            this._providers = null;
        }

        async render(container) {
            this.container = container;
            container.innerHTML = `
                <h3 class="pbw-step-title">Choose your provider</h3>
                <div class="pbw-step-lede">Pick a specific doctor, or let us match you with the next available one.</div>
                <div class="pbw-provider-grid" id="pbwProviderGrid">
                    <div class="text-center text-muted py-4 w-100"><div class="spinner-border spinner-border-sm me-2"></div> Loading providers...</div>
                </div>`;

            try {
                if (!this._providers) {
                    this._providers = await this.api.getProviders() || [];
                    if (this.onLoaded) this.onLoaded(this._providers);
                }
                this._paintGrid(this._providers);
            } catch (e) {
                container.querySelector('#pbwProviderGrid').innerHTML =
                    '<div class="alert alert-warning w-100">Could not load providers. Please refresh.</div>';
            }
        }

        _paintGrid(providers) {
            const grid = this.container.querySelector('#pbwProviderGrid');
            if (!grid) return;
            const s = this.state.get();
            const sel = s.providerId;

            const anyCard = `
                <button type="button" class="pbw-provider-card pbw-prov-featured${sel == null ? ' selected' : ''}"
                        data-provider-id="" data-provider-name="Any Available">
                    <div class="pbw-prov-badge">RECOMMENDED</div>
                    <div class="pbw-prov-avatar pbw-prov-avatar-any"><i class="bi bi-people-fill"></i></div>
                    <div class="pbw-prov-name">Any Available</div>
                    <div class="pbw-prov-spec">We'll pick the next open doctor</div>
                </button>`;

            const cards = (providers || []).map(p => {
                const photoUrl = p.HasPhoto ? `/api/providers/${p.ProviderId}/profile-picture` : null;
                const avatar = photoUrl
                    ? `<img class="pbw-prov-avatar" src="${escapeHtml(photoUrl)}" alt="" onerror="this.outerHTML='<div class=&quot;pbw-prov-avatar&quot; style=&quot;background:${escapeHtml(p.Color || '#1B72BE')}&quot;>${escapeHtml(p.Initials || '?')}</div>'">`
                    : `<div class="pbw-prov-avatar" style="background:${escapeHtml(p.Color || '#1B72BE')}">${escapeHtml(p.Initials || '?')}</div>`;
                return `
                    <button type="button" class="pbw-provider-card${sel === p.ProviderId ? ' selected' : ''}"
                            data-provider-id="${p.ProviderId}" data-provider-name="${escapeHtml(p.DisplayName || '')}">
                        ${avatar}
                        <div class="pbw-prov-name">${escapeHtml(p.DisplayName || '')}</div>
                        <div class="pbw-prov-spec">${escapeHtml(p.Specialty || '')}</div>
                    </button>`;
            }).join('');

            grid.innerHTML = anyCard + cards;

            grid.querySelectorAll('.pbw-provider-card').forEach(card => {
                card.addEventListener('click', () => {
                    const previousId = this.state.get().providerId;
                    grid.querySelectorAll('.pbw-provider-card').forEach(c => c.classList.remove('selected'));
                    card.classList.add('selected');
                    const newIdRaw = card.dataset.providerId;
                    const newId = newIdRaw ? parseInt(newIdRaw, 10) : null;
                    const newName = card.dataset.providerName;
                    // Provider change voids any previously selected slot
                    const slotChange = previousId !== newId ? { selectedSlot: null } : {};
                    this.state.patch(Object.assign({ providerId: newId, providerName: newName }, slotChange));
                });
            });
        }

        validate() {
            // Always valid — "Any Available" is the default selection.
            return true;
        }

        cleanup() { /* No external listeners */ }
    }

    window.PortalBookingProviderStep = BookingProviderStep;
})();
