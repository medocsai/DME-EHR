/**
 * IntakeHealthConcernsRenderer (Employee of IntakeFormRenderer)
 *
 * Why: render Section 2 (Health Concerns). Writes per-concern rows to
 *      PatientHealthConcern, plus 4-5 section-level free-text answers to
 *      PatientIntakeSubmission (LastFeltWell, WhatTriggered, BetterFactors,
 *      WorseFactors, AdditionalTimeline).
 * What: priority-ranked list of up to 5 concerns, each concern = free-text
 *       "Concern/Symptom" + "Details". Auto-adds one empty row on load so
 *       patient doesn't have to click Add first.
 * Who calls: IntakeFormRenderer.
 * Returns: DOM + getPayload() for section "concerns".
 */
(function () {
    'use strict';

    // Prefill URL + auth headers injected by IntakeFormRenderer (host config).
    // Same renderer works for portal (JWT) and tablet (verify cookie).
    async function fetchPrefillFor(renderer) {
        if (!renderer || !renderer._prefillUrl) return null;
        try {
            const headers = (typeof renderer._authHeaders === 'function') ? renderer._authHeaders() : {};
            const res = await fetch(renderer._prefillUrl, { headers });
            if (!res.ok) return null;
            return await res.json();
        } catch { return null; }
    }

    class IntakeHealthConcernsRenderer {
        constructor() {
            this.container = null;
            this.hint = 'List what brings you in, most important first. Up to 5 concerns.';
            this._rowCount = 0;
        }

        async render(container, data, mode) {
            this.container = container;
            container.innerHTML = '<div class="text-center text-muted py-4"><div class="spinner-border spinner-border-sm me-2"></div> Loading...</div>';
            const prefill = await fetchPrefillFor(this);

            container.innerHTML = `
                <div id="ihcList"></div>
                <button type="button" class="btn btn-outline-primary mb-4" id="ihcAdd">
                    <i class="bi bi-plus-lg me-1"></i> Add Another Concern
                </button>

                <hr class="my-4">

                <h6 class="mb-3"><i class="bi bi-chat-left-text text-primary me-2"></i>Tell us more about what you're feeling</h6>
                <div class="row g-3">
                    <div class="col-md-6">
                        <label class="form-label">When did you last feel well?</label>
                        <textarea class="form-control" id="ihcLastFeltWell" rows="2"
                                  placeholder="Approximate date or period, what life was like then">${txt(prefill?.lastFeltWell)}</textarea>
                    </div>
                    <div class="col-md-6">
                        <label class="form-label">What triggered your health change?</label>
                        <textarea class="form-control" id="ihcWhatTriggered" rows="2"
                                  placeholder="Injury, illness, stress event, medication, diet change, etc.">${txt(prefill?.whatTriggered)}</textarea>
                    </div>
                    <div class="col-md-6">
                        <label class="form-label">What makes your symptoms BETTER?</label>
                        <textarea class="form-control" id="ihcBetterFactors" rows="2"
                                  placeholder="Rest, specific foods, movement, medications, weather, etc.">${txt(prefill?.betterFactors)}</textarea>
                    </div>
                    <div class="col-md-6">
                        <label class="form-label">What makes your symptoms WORSE?</label>
                        <textarea class="form-control" id="ihcWorseFactors" rows="2"
                                  placeholder="Stress, certain foods, time of day, weather, activity, etc.">${txt(prefill?.worseFactors)}</textarea>
                    </div>
                    <div class="col-12">
                        <label class="form-label">Additional Health History &amp; Timeline</label>
                        <textarea class="form-control" id="ihcAdditionalTimeline" rows="3"
                                  placeholder="Significant events, past illnesses, injuries. Anything else we should know.">${txt(prefill?.additionalTimeline)}</textarea>
                    </div>
                </div>
            `;
            this._rowCount = 0;

            // Pre-fill rows from existing concerns, or auto-add one empty row.
            const existingItems = prefill?.items || [];
            if (existingItems.length > 0) {
                existingItems.slice(0, 5).forEach(it => this._addRow(it.concern, it.details));
            } else {
                this._addRow();
            }
            document.getElementById('ihcAdd').addEventListener('click', () => this._addRow());
            container.addEventListener('click', (e) => {
                const btn = e.target.closest('[data-ihc-remove]');
                if (btn) {
                    const row = btn.closest('[data-ihc-row]');
                    if (row) row.remove();
                    this._renumber();
                }
            });
        }

        _addRow(concernValue, detailsValue) {
            const list = document.getElementById('ihcList');
            if (!list) return;
            const existing = list.querySelectorAll('[data-ihc-row]').length;
            if (existing >= 5) return;
            this._rowCount += 1;
            const idx = this._rowCount;
            const wrap = document.createElement('div');
            wrap.className = 'iw-item-card';
            wrap.setAttribute('data-ihc-row', idx);
            wrap.innerHTML = `
                <div class="row g-2 align-items-end">
                    <div class="col-md-1"><label class="form-label small">#</label>
                        <input class="form-control ihc-priority" value="${existing + 1}" readonly></div>
                    <div class="col-md-4"><label class="form-label small">Concern / Symptom</label>
                        <input class="form-control ihc-concern" placeholder="e.g. Chronic fatigue" value="${attr(concernValue)}"></div>
                    <div class="col-md-6"><label class="form-label small">Details (onset, frequency, severity, what helps or worsens)</label>
                        <textarea class="form-control ihc-details" rows="1">${txt(detailsValue)}</textarea></div>
                    <div class="col-md-1 text-end">
                        <button type="button" class="btn btn-outline-danger" data-ihc-remove title="Remove">
                            <i class="bi bi-trash"></i>
                        </button>
                    </div>
                </div>
            `;
            list.appendChild(wrap);
        }

        _renumber() {
            const rows = document.querySelectorAll('#ihcList [data-ihc-row]');
            rows.forEach((r, i) => {
                const p = r.querySelector('.ihc-priority');
                if (p) p.value = (i + 1);
            });
        }

        getPayload(sectionName) {
            const rows = Array.from(document.querySelectorAll('#ihcList [data-ihc-row]'));
            const items = rows.map((r, i) => {
                const concern = r.querySelector('.ihc-concern')?.value.trim() || '';
                const details = r.querySelector('.ihc-details')?.value.trim() || '';
                return concern ? { priority: i + 1, concern, details } : null;
            }).filter(Boolean);

            const tx = id => (document.getElementById(id)?.value || '').trim();

            return {
                items,
                lastFeltWell: tx('ihcLastFeltWell'),
                whatTriggered: tx('ihcWhatTriggered'),
                betterFactors: tx('ihcBetterFactors'),
                worseFactors: tx('ihcWorseFactors'),
                additionalTimeline: tx('ihcAdditionalTimeline')
            };
        }
    }

    function attr(v) {
        if (v === null || v === undefined) return '';
        return String(v).replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }
    function txt(v) {
        if (v === null || v === undefined) return '';
        return String(v).replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

    window.IntakeHealthConcernsRenderer = IntakeHealthConcernsRenderer;
})();
