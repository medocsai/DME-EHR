/**
 * LongevityLocationTogglesModule (Employee of Settings page)
 *
 * Why: let ClinicAdmin flip Location.EnableLongevity inline from Settings
 *      without opening the full Edit Location dialog per location.
 * What: lists all tenant locations with a toggle per row, saves on click via
 *       POST /api/locations/{id}/longevity.
 * Who calls: Views/Settings/Index.cshtml mounts via #longevityLocationToggles.
 * Returns: DOM rendered into that root.
 */
(function () {
    'use strict';

    const ROOT_ID = 'longevityLocationToggles';

    function authHeaders() {
        const t = localStorage.getItem('authToken');
        return t ? { 'Authorization': 'Bearer ' + t } : {};
    }

    async function fetchLocations() {
        try {
            const res = await fetch('/api/locations?includeInactive=false', { headers: authHeaders() });
            if (!res.ok) return null;
            return await res.json();
        } catch { return null; }
    }

    async function toggle(locationId, newValue, rowEl, nameEl) {
        rowEl.classList.add('opacity-50');
        try {
            const res = await fetch('/api/locations/' + locationId + '/longevity', {
                method: 'POST',
                headers: Object.assign({ 'Content-Type': 'application/json' }, authHeaders()),
                body: JSON.stringify({ enableLongevity: newValue })
            });
            if (!res.ok) throw new Error('Save failed');
            // Light toast via Bootstrap-ish inline status
            nameEl.classList.add('text-success');
            setTimeout(() => nameEl.classList.remove('text-success'), 800);
        } catch (e) {
            // Revert
            const toggleEl = rowEl.querySelector('input[type=checkbox]');
            if (toggleEl) toggleEl.checked = !newValue;
            alert('Could not save. Please try again.');
        } finally {
            rowEl.classList.remove('opacity-50');
        }
    }

    function render(locations) {
        const root = document.getElementById(ROOT_ID);
        if (!root) return;

        if (!locations || locations.length === 0) {
            root.innerHTML = '<div class="text-muted small">No locations found.</div>';
            return;
        }

        // The API serializes with PascalCase (PropertyNamingPolicy = null in
        // Program.cs). Read both casings just in case anything ever flips.
        const pick = (obj, ...keys) => {
            for (const k of keys) {
                if (obj[k] !== undefined && obj[k] !== null) return obj[k];
            }
            return undefined;
        };

        const html = locations.map(loc => {
            const id = pick(loc, 'LocationId', 'locationId');
            const name = pick(loc, 'Name', 'name');
            const city = pick(loc, 'City', 'city');
            const state = pick(loc, 'State', 'state');
            const isPrimary = pick(loc, 'IsPrimary', 'isPrimary');
            const enableLongevity = pick(loc, 'EnableLongevity', 'enableLongevity');

            return `
            <div class="d-flex align-items-center justify-content-between border rounded p-2 mb-2 llt-row" data-location-id="${id}">
                <div class="d-flex align-items-center gap-2 flex-grow-1">
                    <i class="bi bi-geo-alt text-primary"></i>
                    <div>
                        <div class="fw-semibold llt-name">${escapeHtml(name || 'Unnamed')}</div>
                        <div class="text-muted small">${escapeHtml(city || '')}${state ? ', ' + escapeHtml(state) : ''}${isPrimary ? ' <span class="badge bg-info ms-1">Primary</span>' : ''}</div>
                    </div>
                </div>
                <div class="form-check form-switch">
                    <input class="form-check-input llt-toggle" type="checkbox" role="switch"
                           id="lltSw_${id}" ${enableLongevity ? 'checked' : ''}>
                    <label class="form-check-label small" for="lltSw_${id}">
                        ${enableLongevity ? 'Enabled' : 'Disabled'}
                    </label>
                </div>
            </div>
        `;
        }).join('');

        root.innerHTML = html;

        root.querySelectorAll('.llt-toggle').forEach(cb => {
            cb.addEventListener('change', (e) => {
                const rowEl = e.target.closest('.llt-row');
                const nameEl = rowEl.querySelector('.llt-name');
                const labelEl = rowEl.querySelector('.form-check-label');
                const locId = parseInt(rowEl.dataset.locationId, 10);
                const newVal = e.target.checked;
                if (labelEl) labelEl.textContent = newVal ? 'Enabled' : 'Disabled';
                toggle(locId, newVal, rowEl, nameEl);
            });
        });
    }

    function escapeHtml(s) {
        return String(s || '').replace(/[&<>"']/g, c => ({
            '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
        }[c]));
    }

    async function init() {
        const root = document.getElementById(ROOT_ID);
        if (!root) return;
        const locations = await fetchLocations();
        render(locations);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
