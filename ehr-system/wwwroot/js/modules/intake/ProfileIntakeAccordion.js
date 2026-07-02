/**
 * ProfileIntakeAccordion (Employee of Patient Profile)
 *
 * Why: render the "Patient Intake" section inside the patient profile. Drives the
 *      drill-down Option C view of patient-entered data (same shape as the encounter
 *      right panel).
 * What:
 *   - window.ProfileIntakeAccordion.render(containerEl, patientId)
 *   - Fetches /api/clinic/patients/{id}/intake-view
 *   - Header: title + progress badge + QR icon button (delegates to ProfileIntakeQrButton)
 *   - Body: sub-accordions per section (one open at a time)
 *   - Longevity sub-section only rendered if the view DTO returned a longevity section.
 * Who calls: PatientRenderer (Patient Profile tab content).
 */
(function () {
    'use strict';

    function esc(s) { return String(s == null ? '' : s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }

    function authHeaders() {
        const token =
            localStorage.getItem('authToken') ||
            localStorage.getItem('token') ||
            (window.App && window.App.state && typeof window.App.state.get === 'function' && window.App.state.get('authToken'));
        return token ? { 'Authorization': 'Bearer ' + token } : {};
    }

    function fmtTs(iso) {
        if (!iso) return '';
        try {
            const d = new Date(iso);
            return d.toLocaleString(undefined, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
        } catch { return ''; }
    }

    // Section display metadata (icon + label + category-css + bootstrap-icon for items)
    const SECTION_META = {
        'demographics':     { icon: 'bi-person',          label: 'Demographics',            cat: '',            itemIcon: 'bi-person' },
        'concerns':         { icon: 'bi-chat-left-quote', label: 'Health Concerns',         cat: 'concern',     itemIcon: 'bi-chat-left-quote' },
        'medications':      { icon: 'bi-capsule',         label: 'Medications',             cat: 'cat-meds',    itemIcon: 'bi-capsule-pill' },
        'supplements':      { icon: 'bi-droplet-fill',    label: 'Supplements',             cat: 'cat-supp',    itemIcon: 'bi-droplet-fill' },
        'allergies':        { icon: 'bi-exclamation-triangle', label: 'Allergies',           cat: 'cat-allergy', itemIcon: 'bi-exclamation-triangle-fill' },
        'medical-history':  { icon: 'bi-clock-history',   label: 'Medical History',         cat: 'cat-history', itemIcon: 'bi-clipboard2-pulse' },
        'family-history':   { icon: 'bi-people',          label: 'Family History',          cat: 'cat-family',  itemIcon: 'bi-person' },
        'lifestyle':        { icon: 'bi-heart',           label: 'Lifestyle',               cat: 'cat-lifestyle', itemIcon: 'bi-activity' },
        'immunizations':    { icon: 'bi-shield-check',    label: 'Immunizations',           cat: '',            itemIcon: 'bi-shield-check' },
        'gender-health':    { icon: 'bi-gender-ambiguous', label: 'Gender Health',          cat: '',            itemIcon: 'bi-heart-pulse' },
        'longevity':        { icon: 'bi-hourglass-split', label: 'Longevity',               cat: 'cat-history', itemIcon: 'bi-hourglass-split' }
    };

    async function fetchIntakeView(patientId) {
        const resp = await fetch(`/api/clinic/patients/${patientId}/intake-view`, {
            headers: { 'Accept': 'application/json', ...authHeaders() }
        });
        if (!resp.ok) throw new Error('Failed to load intake view: ' + resp.status);
        return await resp.json();
    }

    function renderItem(section, item) {
        const meta = SECTION_META[section.Name || section.name] || { cat: '', itemIcon: 'bi-dot' };
        const catCls = meta.cat ? `cat-${meta.cat.replace('cat-', '')}` : '';
        const cls = meta.cat && meta.cat.startsWith('cat-') ? meta.cat : '';
        const label = esc(item.Label || item.label || '');
        const detail = esc(item.Detail || item.detail || '');
        return `
            <div class="intake-item ${cls}">
                <div class="item-icon"><i class="bi ${meta.itemIcon}"></i></div>
                <div class="item-content">
                    <div class="item-title">${label}</div>
                    ${detail ? `<div class="item-notes">${detail}</div>` : ''}
                </div>
            </div>
        `;
    }

    function renderConcerns(items) {
        if (!items.length) return '<div class="text-muted small">No concerns entered.</div>';
        return items.map((c, i) => `
            <div class="concern-quote">
                <div class="concern-num">${i + 1}</div>
                <div class="concern-text">
                    <div class="concern-title">${esc(c.Label || c.label || '')}</div>
                    ${c.Detail ? `<div class="concern-words">"${esc(c.Detail)}"</div>` : ''}
                </div>
            </div>
        `).join('');
    }

    function renderSection(section) {
        const name = section.Name || section.name;
        const items = section.Items || section.items || [];
        const meta = SECTION_META[name] || { icon: 'bi-dot', label: name, cat: '' };
        const savedAt = section.LastSavedAt || section.lastSavedAt;
        const count = items.length;

        let bodyHtml = '';
        if (name === 'concerns') {
            bodyHtml = renderConcerns(items);
        } else if (!count) {
            bodyHtml = '<div class="text-muted small py-2">Nothing entered for this section yet.</div>';
        } else {
            bodyHtml = items.map(it => renderItem(section, it)).join('');
        }

        return `
            <div class="drill-item" data-section="${esc(name)}">
                <div class="drill-row" data-intake-drill-toggle>
                    <i class="bi ${meta.icon}" style="color:#6b7280"></i>
                    <span class="drill-name">${esc(meta.label)}</span>
                    ${count ? `<span class="drill-count">${count}</span>` : ''}
                    ${savedAt ? `<span class="text-muted small ms-2" style="font-size:10px;">saved ${esc(fmtTs(savedAt))}</span>` : ''}
                    <i class="bi bi-chevron-right drill-arrow"></i>
                </div>
                <div class="drill-body">${bodyHtml}</div>
            </div>
        `;
    }

    function renderEmptyState(patientId) {
        return `
            <div class="text-center py-4 px-3">
                <i class="bi bi-clipboard text-muted" style="font-size: 2.5rem;"></i>
                <p class="mt-2 mb-3 text-muted">Patient hasn't started intake yet.</p>
                <button type="button" class="btn btn-primary" data-intake-open-qr="${patientId}">
                    <i class="bi bi-qr-code me-1"></i>Show QR, hand tablet
                </button>
            </div>
        `;
    }

    function render(containerEl, patientId) {
        if (!containerEl) return;
        containerEl.innerHTML = `<div class="text-center p-3"><div class="spinner-border spinner-border-sm"></div> Loading intake...</div>`;

        fetchIntakeView(patientId).then(view => {
            const sections = (view.Sections || view.sections || []).filter(s => {
                const nm = s.Name || s.name;
                return !!SECTION_META[nm];
            });
            const progress = view.Progress || view.progress || { Completed: 0, Total: 8 };
            const completed = progress.Completed != null ? progress.Completed : progress.completed;
            const total = progress.Total != null ? progress.Total : progress.total;
            const totalItems = sections.reduce((acc, s) => acc + ((s.Items || s.items || []).length), 0);

            const hasAny = sections.some(s => ((s.Items || s.items || []).length) > 0);

            const progressBadge = `<span class="badge bg-${completed === total && total > 0 ? 'success' : 'primary'} ms-2">${completed}/${total}</span>`;

            containerEl.innerHTML = `
                <div class="card mb-3 intake-accordion-card">
                    <div class="card-header d-flex align-items-center">
                        <h5 class="mb-0 text-primary">
                            <i class="bi bi-clipboard-check text-primary me-2"></i>Patient Intake
                            ${progressBadge}
                        </h5>
                        <div class="ms-auto">
                            <button type="button" class="btn btn-outline-primary" data-intake-open-qr="${patientId}" title="Patient Intake (QR / Tablet link)">
                                <i class="bi bi-qr-code me-1"></i>Show QR
                            </button>
                        </div>
                    </div>
                    <div class="card-body p-0">
                        ${hasAny
                            ? `<div class="drill-list intake-profile-drill">${sections.map(renderSection).join('')}</div>`
                            : renderEmptyState(patientId)}
                    </div>
                </div>
            `;

            // Wire drill toggles — one open at a time
            containerEl.querySelectorAll('[data-intake-drill-toggle]').forEach(row => {
                row.addEventListener('click', () => {
                    const drill = row.closest('.drill-item');
                    const wasOpen = drill.classList.contains('open');
                    containerEl.querySelectorAll('.drill-item.open').forEach(o => o.classList.remove('open'));
                    if (!wasOpen) drill.classList.add('open');
                });
            });

            // Wire QR buttons (header + empty-state)
            containerEl.querySelectorAll('[data-intake-open-qr]').forEach(btn => {
                btn.addEventListener('click', (e) => {
                    e.preventDefault();
                    const pid = parseInt(btn.getAttribute('data-intake-open-qr'));
                    if (window.ProfileIntakeQrButton && typeof window.ProfileIntakeQrButton.openQrModal === 'function') {
                        window.ProfileIntakeQrButton.openQrModal(pid);
                    } else {
                        alert('Intake QR module not loaded.');
                    }
                });
            });
        }).catch(err => {
            console.error('[ProfileIntakeAccordion] load failed', err);
            containerEl.innerHTML = `<div class="alert alert-warning m-3">Unable to load patient intake data.</div>`;
        });
    }

    window.ProfileIntakeAccordion = { render };
})();
