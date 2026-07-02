/**
 * BookingApiClient (Hired Guy)
 *
 * Why: wrap every HTTP call the wizard needs in one place — providers list,
 *      slot lookup, booking submit. Auth is JWT, identical to the rest of the
 *      portal (Authorization: Bearer <portalAuthToken> from localStorage).
 * What: getProviders(), getSlots({ date, providerId, duration }), book(payload).
 *       All return parsed JSON, throw on non-2xx with the server's message.
 * Who calls: BookingProviderStep, BookingDateTimeStep, PortalBookingWizard.
 * Returns: instance.
 */
(function () {
    'use strict';

    const AUTH_TOKEN_KEY = 'portalAuthToken';

    function authHeaders() {
        const token = localStorage.getItem(AUTH_TOKEN_KEY);
        return token ? { 'Authorization': 'Bearer ' + token } : {};
    }

    function formatDateLocal(d) {
        const year = d.getFullYear();
        const month = String(d.getMonth() + 1).padStart(2, '0');
        const day = String(d.getDate()).padStart(2, '0');
        return year + '-' + month + '-' + day;
    }

    async function fetchJson(url, opts) {
        const res = await fetch(url, opts);
        if (!res.ok) {
            const text = await res.text();
            let msg = 'Request failed';
            try { msg = JSON.parse(text)?.message || text; } catch { msg = text || msg; }
            throw new Error(msg);
        }
        if (res.status === 204) return null;
        const text = await res.text();
        return text ? JSON.parse(text) : null;
    }

    class BookingApiClient {
        async getProviders() {
            return await fetchJson('/api/portal/providers', {
                headers: Object.assign({ 'Content-Type': 'application/json' }, authHeaders())
            });
        }

        // date is a Date object. providerId may be null (any). duration in minutes.
        async getSlots({ date, providerId, duration }) {
            // Send T12:00:00Z to dodge UTC midnight rolling back a day in CDT/CST.
            const dateParam = formatDateLocal(date) + 'T12:00:00Z';
            const params = new URLSearchParams({ date: dateParam });
            if (providerId) params.set('providerId', String(providerId));
            if (duration) params.set('duration', String(duration));
            return await fetchJson('/api/portal/appointments/available-slots?' + params.toString(), {
                headers: Object.assign({ 'Content-Type': 'application/json' }, authHeaders())
            });
        }

        async book(payload) {
            return await fetchJson('/api/portal/appointments/book', {
                method: 'POST',
                headers: Object.assign({ 'Content-Type': 'application/json' }, authHeaders()),
                body: JSON.stringify(payload)
            });
        }
    }

    window.PortalBookingApiClient = BookingApiClient;
    window.PortalBookingApiClient.formatDateLocal = formatDateLocal;
})();
