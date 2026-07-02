/**
 * CareNotesModule
 * ----------------
 * Why it exists:
 *   Care Notes are structured staff -> provider relays ("patient called about
 *   BP", "pharmacy needs PA"). This module owns three surfaces:
 *     1. Top-nav bell + dropdown (Clinician role only) showing unseen notes
 *        addressed to the logged-in provider. Real-time via SignalR
 *        (/hubs/care-notes), with 60s polling as a fallback.
 *     2. Care Notes tab inside the patient profile -- list, add, edit (creator
 *        only), soft-delete (creator only).
 *     3. Mic dictation in the add form -- records audio, sends to
 *        /api/care-notes/transcribe, fills the textarea with polished text.
 *
 * Who calls it:
 *   - Top-nav: self-initialized on DOMContentLoaded for clinician role.
 *   - Patient profile tab: PatientRenderer attaches a click handler on the
 *     Care Notes tab; the handler calls window.careNotesModule.loadPatientTab(patientId).
 *
 * Endpoints:
 *   GET    /api/care-notes/unseen-count
 *   GET    /api/care-notes/unseen
 *   GET    /api/patients/{id}/care-notes      (auto-marks seen for clinicians)
 *   POST   /api/patients/{id}/care-notes
 *   PUT    /api/care-notes/{id}
 *   DELETE /api/care-notes/{id}
 *   POST   /api/care-notes/mark-patient-seen/{patientId}
 *   POST   /api/care-notes/transcribe         (multipart audio -> polished text)
 *
 * Hub:
 *   /hubs/care-notes
 *   server-emit "UnseenChanged" { count } -> we update the badge instantly.
 *
 * Timezone:
 *   All datetimes from the server are UTC. Render via the Location's IANA
 *   timezone (App.state.locationTimeZoneId), per CLAUDE.md timezone rule.
 */
(function () {
    'use strict';

    const POLL_INTERVAL_MS = 60000; // 60s — fallback only when SignalR is offline
    const HUB_URL = '/hubs/care-notes';

    const CareNotesModule = {
        _pollHandle: null,
        _currentPatientId: null,
        _providerCache: null,

        // SignalR
        _hubConnection: null,
        _hubConnected: false,

        // Voice (delegated to CareNotesVoice contractor)
        _voice: null,

        // ====== Init (top-nav bell) ======
        init() {
            const user = this._getUser();
            const role = parseInt(user.Role ?? user.role ?? -1);

            // Only Clinician (role=2) sees the top-nav bell + polls + connects to hub
            if (role !== 2) return;

            const btn = document.getElementById('careNotesBtn');
            if (!btn) return;

            // Click toggles dropdown (and refreshes the list each time)
            btn.addEventListener('click', (e) => {
                e.stopPropagation();
                this._toggleDropdown();
            });

            // Click outside closes dropdown
            document.addEventListener('click', (e) => {
                const dd = document.getElementById('careNotesDropdown');
                const container = document.getElementById('careNotesContainer');
                if (dd && container && !container.contains(e.target)) {
                    dd.style.display = 'none';
                }
            });

            // Initial fetch
            this._refreshCount();

            // Real-time push via SignalR. Polling stays as a fallback for the
            // case where the hub is down or auth is stale.
            this._connectHub();
            this._pollHandle = setInterval(() => this._refreshCount(), POLL_INTERVAL_MS);
        },

        // ====== SignalR ======
        async _connectHub() {
            if (typeof signalR === 'undefined') {
                console.warn('[CareNotes] signalR library not loaded — falling back to polling only');
                return;
            }
            const token = localStorage.getItem('authToken');
            if (!token) return;

            try {
                this._hubConnection = new signalR.HubConnectionBuilder()
                    .withUrl(HUB_URL, { accessTokenFactory: () => token })
                    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
                    .configureLogging(signalR.LogLevel.Warning)
                    .build();

                // Server -> client: unseen count changed (push the fresh value)
                this._hubConnection.on('UnseenChanged', (payload) => {
                    const count = (payload && typeof payload.count === 'number') ? payload.count : 0;
                    this._setBadge(count);
                });

                this._hubConnection.onreconnected(() => {
                    this._hubConnected = true;
                    // After a reconnect, immediately re-sync — we may have missed events
                    this._refreshCount();
                });
                this._hubConnection.onclose(() => {
                    this._hubConnected = false;
                });

                await this._hubConnection.start();
                this._hubConnected = true;
                console.log('[CareNotes] SignalR connected');
            } catch (e) {
                console.warn('[CareNotes] SignalR connect failed; relying on polling:', e?.message);
                this._hubConnected = false;
            }
        },

        _setBadge(count) {
            const badge = document.getElementById('careNotesBadge');
            if (!badge) return;
            if (count > 0) {
                badge.textContent = count > 99 ? '99+' : String(count);
                badge.classList.remove('d-none');
            } else {
                badge.textContent = '0';
                badge.classList.add('d-none');
            }
        },

        // ====== Top-nav bell helpers ======
        async _refreshCount() {
            try {
                const res = await apiRequest('/care-notes/unseen-count', { showLoader: false, showErrors: false });
                const count = (res && typeof res.count === 'number') ? res.count : 0;
                this._setBadge(count);
            } catch (e) {
                // silent — never disrupt the rest of the app for a poll error
            }
        },

        async _toggleDropdown() {
            const dd = document.getElementById('careNotesDropdown');
            if (!dd) return;
            const willOpen = dd.style.display === 'none' || !dd.style.display;
            if (!willOpen) { dd.style.display = 'none'; return; }

            // Render loading state, then fetch
            dd.innerHTML = `
                <div class="cn-dd-header">
                    <span><i class="bi bi-journal-medical me-1"></i>Care Notes</span>
                </div>
                <div class="cn-dd-body text-center text-muted p-3" style="font-size:12px;">
                    <div class="spinner-border spinner-border-sm"></div> Loading...
                </div>`;
            dd.style.display = 'block';

            try {
                const rows = await apiRequest('/care-notes/unseen', { showLoader: false, showErrors: false });
                this._renderDropdown(Array.isArray(rows) ? rows : []);
            } catch (e) {
                dd.innerHTML = `
                    <div class="cn-dd-header"><span>Care Notes</span></div>
                    <div class="cn-dd-body p-3 text-center text-danger" style="font-size:12px;">
                        Failed to load.
                    </div>`;
            }
        },

        _renderDropdown(rows) {
            const dd = document.getElementById('careNotesDropdown');
            if (!dd) return;

            if (rows.length === 0) {
                dd.innerHTML = `
                    <div class="cn-dd-header"><span><i class="bi bi-journal-medical me-1"></i>Care Notes</span></div>
                    <div class="cn-dd-empty">
                        <i class="bi bi-check2-circle"></i>
                        <div class="cn-dd-empty-title">No care notes require your attention.</div>
                        <div class="cn-dd-empty-sub">To view a patient's care notes, open their patient profile.</div>
                    </div>`;
                return;
            }

            const itemsHtml = rows.map(r => {
                const safeName = this._esc(r.PatientName || 'Patient');
                const safeMrn = this._esc(r.PatientMrn || '');
                const safePreview = this._esc(r.Preview || '');
                const safeAuthor = this._esc(r.CreatedByName || '');
                const when = this._formatLocal(r.CreatedAt);
                return `
                    <div class="cn-dd-item" data-patient-id="${r.PatientId}">
                        <div class="d-flex align-items-start gap-2">
                            <span class="cn-dot"></span>
                            <div style="flex:1; min-width:0;">
                                <div class="cn-dd-patient">
                                    ${safeName}
                                    ${safeMrn ? `<span class="cn-dd-mrn">MRN: ${safeMrn}</span>` : ''}
                                </div>
                                <div class="cn-dd-preview">${safePreview}</div>
                                <div class="cn-dd-time">${when} by ${safeAuthor}</div>
                            </div>
                        </div>
                    </div>`;
            }).join('');

            dd.innerHTML = `
                <div class="cn-dd-header"><span><i class="bi bi-journal-medical me-1"></i>Unseen Care Notes</span></div>
                <div class="cn-dd-body">${itemsHtml}</div>`;

            // Wire row clicks: open patient profile -> Care Notes tab + mark seen
            dd.querySelectorAll('.cn-dd-item').forEach(el => {
                el.addEventListener('click', () => {
                    const pid = parseInt(el.getAttribute('data-patient-id'));
                    if (!pid) return;
                    dd.style.display = 'none';
                    this._openPatientCareNotes(pid);
                });
            });
        },

        _openPatientCareNotes(patientId) {
            if (typeof window.viewPatient !== 'function') return;

            // Attach the shown.bs.modal listener FIRST, then call viewPatient.
            // (Same pattern as IntakeStatusIndicator.openIntakeTab.) Otherwise
            // the modal may already be shown by the time we attach -- race.
            const modal = document.getElementById('patientDetailModal');
            if (modal) {
                const handler = () => {
                    modal.removeEventListener('shown.bs.modal', handler);
                    // Defer so the tab markup exists
                    setTimeout(() => {
                        const link = document.querySelector('#patientDetailModal a[href="#patientCareNotes"]');
                        if (link && typeof link.click === 'function') link.click();
                    }, 50);
                };
                modal.addEventListener('shown.bs.modal', handler);
            }

            window.viewPatient(patientId);

            // Refresh count after the seen-mark fires
            setTimeout(() => this._refreshCount(), 1500);
        },

        // ====== Patient profile Care Notes tab ======
        async loadPatientTab(patientId) {
            this._currentPatientId = patientId;
            const container = document.getElementById('patientCareNotesContent');
            if (!container) return;

            container.innerHTML = `
                <div class="text-center p-4 text-muted">
                    <div class="spinner-border spinner-border-sm"></div> Loading care notes...
                </div>`;

            try {
                // Load providers (for "For Provider" dropdown) + notes in parallel
                const [providers, notes] = await Promise.all([
                    this._loadProviders(),
                    apiRequest(`/patients/${patientId}/care-notes`, { showLoader: false })
                ]);

                this._renderTab(patientId, providers, Array.isArray(notes) ? notes : []);

                // Refresh top-nav count -- opening the tab auto-marks seen for clinicians
                this._refreshCount();
            } catch (e) {
                container.innerHTML = `
                    <div class="alert alert-danger m-3">
                        Failed to load care notes. Please try again.
                    </div>`;
            }
        },

        async _loadProviders() {
            if (this._providerCache) return this._providerCache;
            try {
                const list = await apiRequest('/providers', { showLoader: false });
                this._providerCache = Array.isArray(list) ? list : [];
            } catch (e) {
                this._providerCache = [];
            }
            return this._providerCache;
        },

        _renderTab(patientId, providers, notes) {
            const container = document.getElementById('patientCareNotesContent');
            if (!container) return;

            const user = this._getUser();
            const currentUserId = parseInt(user.UserId ?? user.userId ?? user.Id ?? 0);

            const providerOpts = providers.map(p => {
                const id = p.ProviderId ?? p.providerId;
                const name = `Dr. ${p.FirstName ?? p.firstName ?? ''} ${p.LastName ?? p.lastName ?? ''}`.trim();
                return `<option value="${id}">${this._esc(name)}</option>`;
            }).join('');

            container.innerHTML = `
                <div class="cn-tab">
                    <div class="cn-add-card" id="cnAddCard">
                        <div class="d-flex align-items-center gap-2 mb-2">
                            <i class="bi bi-journal-plus text-primary" style="font-size:18px;"></i>
                            <strong>New Care Note</strong>
                        </div>
                        <div class="row g-2 mb-2">
                            <div class="col-md-6">
                                <label class="form-label cn-label">For Provider <span class="text-muted">(optional)</span></label>
                                <select class="form-select" id="cnForProvider">
                                    <option value="">-- General note (no specific provider) --</option>
                                    ${providerOpts}
                                </select>
                            </div>
                        </div>
                        <div class="mb-2">
                            <label class="form-label cn-label">Note <span class="text-danger">*</span></label>
                            <div class="d-flex gap-2 align-items-start">
                                <textarea class="form-control" id="cnContent" rows="4"
                                          placeholder="Type your note or click the mic to dictate..."></textarea>
                                <div class="d-flex flex-column align-items-center gap-1">
                                    <button class="btn-mic" id="cnMicBtn" title="Start recording" type="button">
                                        <i class="bi bi-mic-fill"></i>
                                    </button>
                                    <small class="text-muted" style="font-size:10px;" id="cnMicLabel">Mic</small>
                                </div>
                            </div>
                            <div id="cnRecordingBar" class="d-none align-items-center gap-2 mt-2 p-2 rounded"
                                 style="background:#fef2f2;border:1px solid #fecaca;">
                                <span class="text-danger" style="font-size:12px;">
                                    <i class="bi bi-record-circle me-1"></i>Recording in progress
                                </span>
                                <span class="text-muted" style="font-size:11px;" id="cnRecordingTimer">0:00</span>
                                <div class="ms-auto d-flex gap-2">
                                    <button class="btn btn-secondary" id="cnRecordCancelBtn" type="button">Cancel</button>
                                    <button class="btn btn-danger" id="cnRecordStopBtn" type="button">
                                        <i class="bi bi-stop-fill me-1"></i>Stop & Transcribe
                                    </button>
                                </div>
                            </div>
                            <small class="text-muted mt-1 d-block" style="font-size:11px;">
                                <i class="bi bi-stars me-1"></i>Text appears live as you speak.
                            </small>
                        </div>
                        <div class="d-flex justify-content-end gap-2">
                            <button class="btn btn-secondary" id="cnClearBtn">Clear</button>
                            <button class="btn btn-primary" id="cnSaveBtn">
                                <i class="bi bi-check-lg me-1"></i>Save Note
                            </button>
                        </div>
                    </div>

                    <div class="cn-list-header d-flex align-items-center mt-3 mb-2">
                        <strong>Care Notes</strong>
                        <span class="badge bg-light text-dark ms-2" id="cnCountBadge">${notes.length}</span>
                    </div>
                    <div id="cnList">${this._renderNotesList(notes, currentUserId)}</div>
                </div>`;

            // Wire save
            document.getElementById('cnSaveBtn').addEventListener('click', () => this._handleSave(patientId));
            document.getElementById('cnClearBtn').addEventListener('click', () => {
                document.getElementById('cnContent').value = '';
                document.getElementById('cnForProvider').value = '';
            });

            // Voice mic — delegate to CareNotesVoice (separate contractor file).
            // Tear down any previous instance from a prior render.
            if (this._voice) {
                try { this._voice.destroy(); } catch (e) { /* ignore */ }
                this._voice = null;
            }
            if (typeof window.CareNotesVoice === 'function') {
                this._voice = new window.CareNotesVoice({
                    textareaId: 'cnContent',
                    micBtnId: 'cnMicBtn',
                    micLabelId: 'cnMicLabel',
                    recordingBarId: 'cnRecordingBar',
                    timerId: 'cnRecordingTimer',
                    stopBtnId: 'cnRecordStopBtn',
                    cancelBtnId: 'cnRecordCancelBtn',
                    patientId: patientId
                });
            }

            // Wire row actions (edit/delete) via delegation
            const list = document.getElementById('cnList');
            list.addEventListener('click', (e) => this._handleListClick(e));
        },

        _renderNotesList(notes, currentUserId) {
            if (notes.length === 0) {
                return `<div class="cn-empty text-center text-muted p-4">
                    <i class="bi bi-journal" style="font-size:24px;"></i>
                    <div class="mt-2">No care notes for this patient yet.</div>
                </div>`;
            }
            return notes.map(n => this._renderNoteItem(n, currentUserId)).join('');
        },

        _renderNoteItem(n, currentUserId) {
            const isOwner = n.CreatedByUserId === currentUserId;
            const isUnseen = !n.SeenAt;
            const created = this._formatLocal(n.CreatedAt);
            const edited = n.IsEdited && n.EditedAt ? this._formatLocal(n.EditedAt) : null;
            const forProvider = n.ForProviderName ? this._esc(n.ForProviderName) : null;

            const ownerActions = isOwner ? `
                <button class="btn btn-link p-0 cn-action-edit" data-id="${n.CareNoteId}" title="Edit">
                    <i class="bi bi-pencil"></i> Edit
                </button>
                <button class="btn btn-link p-0 text-danger cn-action-delete" data-id="${n.CareNoteId}" title="Delete">
                    <i class="bi bi-trash"></i> Delete
                </button>` : '';

            return `
                <div class="cn-item ${isUnseen ? 'cn-unseen' : ''}" data-id="${n.CareNoteId}">
                    <div class="cn-meta">
                        ${isUnseen ? '<span class="cn-dot"></span><span class="cn-tag-new">New</span>' : '<span class="cn-tag-seen"><i class="bi bi-check-circle-fill me-1"></i>Seen</span>'}
                        ${forProvider ? `<span class="cn-tag-for"><i class="bi bi-person me-1"></i>For: ${forProvider}</span>` : ''}
                        <span>by <strong>${this._esc(n.CreatedByName)}</strong></span>
                        <span>${created}</span>
                        ${edited ? `<span class="cn-edited" title="Edited by ${this._esc(n.EditedByName || '')}">(edited ${edited}${n.EditedByName ? ' by ' + this._esc(n.EditedByName) : ''})</span>` : ''}
                    </div>
                    <div class="cn-content" id="cnContent_${n.CareNoteId}">${this._esc(n.Content).replace(/\n/g, '<br>')}</div>
                    ${ownerActions ? `<div class="cn-actions mt-2 d-flex gap-3">${ownerActions}</div>` : ''}
                </div>`;
        },

        async _handleSave(patientId) {
            const contentEl = document.getElementById('cnContent');
            const providerEl = document.getElementById('cnForProvider');
            const content = (contentEl?.value || '').trim();
            if (!content) {
                showToast('Required', 'Please enter the care note content.', 'warning');
                return;
            }
            const forProviderId = providerEl?.value ? parseInt(providerEl.value) : null;

            try {
                await apiRequest(`/patients/${patientId}/care-notes`, {
                    method: 'POST',
                    body: { Content: content, ForProviderId: forProviderId }
                });
                contentEl.value = '';
                providerEl.value = '';
                showToast('Saved', 'Care note added.', 'success');
                // Reload the tab
                this.loadPatientTab(patientId);
            } catch (e) {
                showToast('Error', 'Failed to save care note.', 'error');
            }
        },

        async _handleListClick(e) {
            const editBtn = e.target.closest('.cn-action-edit');
            const deleteBtn = e.target.closest('.cn-action-delete');
            const cancelBtn = e.target.closest('.cn-edit-cancel');
            const saveBtn = e.target.closest('.cn-edit-save');

            if (editBtn) {
                const id = parseInt(editBtn.getAttribute('data-id'));
                this._enterEditMode(id);
            } else if (deleteBtn) {
                const id = parseInt(deleteBtn.getAttribute('data-id'));
                this._confirmDelete(id);
            } else if (cancelBtn) {
                const id = parseInt(cancelBtn.getAttribute('data-id'));
                this._exitEditMode(id);
            } else if (saveBtn) {
                const id = parseInt(saveBtn.getAttribute('data-id'));
                this._saveEdit(id);
            }
        },

        _enterEditMode(careNoteId) {
            const item = document.querySelector(`.cn-item[data-id="${careNoteId}"]`);
            if (!item) return;
            const contentDiv = item.querySelector(`#cnContent_${careNoteId}`);
            const actions = item.querySelector('.cn-actions');
            const currentText = contentDiv.innerText;

            contentDiv.innerHTML = `
                <textarea class="form-control" id="cnEdit_${careNoteId}" rows="4">${this._esc(currentText)}</textarea>`;
            if (actions) {
                actions.innerHTML = `
                    <button class="btn btn-secondary cn-edit-cancel" data-id="${careNoteId}">Cancel</button>
                    <button class="btn btn-primary cn-edit-save" data-id="${careNoteId}">
                        <i class="bi bi-check-lg me-1"></i>Save Changes
                    </button>`;
            }
        },

        _exitEditMode(careNoteId) {
            // Simplest path: reload the tab
            if (this._currentPatientId) this.loadPatientTab(this._currentPatientId);
        },

        async _saveEdit(careNoteId) {
            const ta = document.getElementById(`cnEdit_${careNoteId}`);
            if (!ta) return;
            const newContent = (ta.value || '').trim();
            if (!newContent) {
                showToast('Required', 'Note content cannot be empty.', 'warning');
                return;
            }
            try {
                await apiRequest(`/care-notes/${careNoteId}`, {
                    method: 'PUT',
                    body: { Content: newContent, ForProviderId: null }
                });
                showToast('Saved', 'Care note updated.', 'success');
                if (this._currentPatientId) this.loadPatientTab(this._currentPatientId);
            } catch (e) {
                showToast('Error', 'Failed to update care note.', 'error');
            }
        },

        async _confirmDelete(careNoteId) {
            // Use the shared Bootstrap ConfirmDialog (defined in
            // wwwroot/js/shared/ui/ConfirmDialog.js) for consistency with
            // delete confirmations elsewhere in the app. Falls back to
            // native confirm if the helper isn't loaded for some reason.
            const ok = window.ConfirmDialog
                ? await window.ConfirmDialog.confirmDelete('this care note')
                : window.confirm('Delete this care note? This cannot be undone.');
            if (!ok) return;
            try {
                await apiRequest(`/care-notes/${careNoteId}`, { method: 'DELETE' });
                showToast('Deleted', 'Care note removed.', 'success');
                if (this._currentPatientId) this.loadPatientTab(this._currentPatientId);
            } catch (e) {
                showToast('Error', 'Failed to delete care note.', 'error');
            }
        },

        // ====== Helpers ======
        _getUser() {
            try {
                return JSON.parse(localStorage.getItem('currentUser') || '{}');
            } catch (e) {
                return {};
            }
        },

        _esc(s) {
            if (s == null) return '';
            return String(s)
                .replace(/&/g, '&amp;')
                .replace(/</g, '&lt;')
                .replace(/>/g, '&gt;')
                .replace(/"/g, '&quot;')
                .replace(/'/g, '&#39;');
        },

        /**
         * Format a UTC datetime in the active Location's IANA timezone.
         * Per CLAUDE.md timezone rule: never display UTC, never use the browser
         * default timezone implicitly.
         */
        _formatLocal(utc) {
            if (!utc) return '';
            try {
                const tz = (window.App && window.App.state && window.App.state.get && window.App.state.get('locationTimeZoneId')) || undefined;
                const d = new Date(utc);
                return new Intl.DateTimeFormat('en-US', {
                    month: 'short', day: 'numeric',
                    hour: 'numeric', minute: '2-digit',
                    timeZone: tz
                }).format(d);
            } catch (e) {
                return new Date(utc).toLocaleString();
            }
        }
    };

    // Expose
    window.careNotesModule = CareNotesModule;

    // Self-init on DOM ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', () => CareNotesModule.init());
    } else {
        CareNotesModule.init();
    }
})();
