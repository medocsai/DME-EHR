/**
 * NoteGenerationTracker — Global notification bar for background note generation.
 * Renders a persistent slim bar below the navbar showing generation progress.
 * Polls status endpoint and fires toast + auto-dismiss on completion.
 *
 * Persists active jobs to localStorage so the bar survives page navigations (MPA).
 * Encounter-agnostic: works for telehealth, ambient scribe, or any transcription source.
 */
class NoteGenerationTracker {
    constructor() {
        this._jobs = new Map(); // jobId -> { encounterId, patientName, totalNotes, ... }
        this._pollIntervals = new Map();
        this._barEl = null;
        this._initialized = false;
        this._storageKey = 'noteGenActiveJobs';
    }

    init() {
        if (this._initialized) return;
        this._initialized = true;
        this._createBarElement();
        this._restoreFromStorage();
    }

    /**
     * Start tracking a note generation job.
     * @param {string} jobId
     * @param {number} encounterId
     * @param {string} patientName
     * @param {number} totalNotes
     */
    trackJob(jobId, encounterId, patientName, totalNotes) {
        this.init();

        this._jobs.set(jobId, {
            jobId,
            encounterId,
            patientName,
            totalNotes,
            completedNotes: 0,
            status: 'processing'
        });

        this._saveToStorage();
        this._renderBar();
        this._startPolling(jobId);
    }

    /**
     * Create the fixed notification bar element.
     */
    _createBarElement() {
        if (this._barEl) return;

        this._barEl = document.createElement('div');
        this._barEl.id = 'noteGenerationBar';
        this._barEl.style.cssText = `
            position: fixed;
            top: 0;
            left: 0;
            right: 0;
            z-index: 1060;
            display: none;
            transition: all 0.3s ease;
        `;
        document.body.prepend(this._barEl);
    }

    /**
     * Render the notification bar content based on active jobs.
     */
    _renderBar() {
        if (!this._barEl) return;

        const activeJobs = [...this._jobs.values()];
        if (activeJobs.length === 0) {
            this._barEl.style.display = 'none';
            return;
        }

        this._barEl.style.display = 'block';

        const items = activeJobs.map(job => {
            if (job.status === 'processing') {
                const pct = job.totalNotes > 0 ? Math.round((job.completedNotes / job.totalNotes) * 100) : 0;
                return `
                    <div class="d-flex align-items-center justify-content-between px-3 py-2"
                         style="background: #1a73e8; color: white; font-size: 0.875rem;">
                        <div class="d-flex align-items-center">
                            <span class="spinner-border spinner-border-sm me-2" style="width: 1rem; height: 1rem;"></span>
                            <span>Writing your report...</span>
                        </div>
                        <div class="d-flex align-items-center">
                            <div class="progress me-2" style="width: 100px; height: 6px;">
                                <div class="progress-bar bg-white" style="width: ${pct}%"></div>
                            </div>
                            <small>${pct}%</small>
                        </div>
                    </div>`;
            } else if (job.status === 'completed') {
                return `
                    <div class="d-flex align-items-center justify-content-between px-3 py-2"
                         style="background: #0f9d58; color: white; font-size: 0.875rem;">
                        <div class="d-flex align-items-center">
                            <i class="bi bi-check-circle me-2"></i>
                            <span>Notes ready for <strong>${this._escapeHtml(job.patientName)}</strong></span>
                        </div>
                        <div>
                            <button class="btn btn-sm btn-outline-light me-1" onclick="window._noteGenerationTracker._viewEncounter(${job.encounterId})">
                                View
                            </button>
                            <button class="btn btn-sm btn-outline-light" onclick="window._noteGenerationTracker._dismissJob('${job.jobId}')">
                                <i class="bi bi-x"></i>
                            </button>
                        </div>
                    </div>`;
            } else if (job.status === 'failed' || job.status === 'partiallycompleted') {
                const msg = job.status === 'partiallycompleted'
                    ? `${job.completedNotes}/${job.totalNotes} notes generated for <strong>${this._escapeHtml(job.patientName)}</strong> (some failed)`
                    : `Failed to generate notes for <strong>${this._escapeHtml(job.patientName)}</strong>`;
                return `
                    <div class="d-flex align-items-center justify-content-between px-3 py-2"
                         style="background: ${job.status === 'partiallycompleted' ? '#f9a825' : '#d93025'}; color: white; font-size: 0.875rem;">
                        <div class="d-flex align-items-center">
                            <i class="bi bi-exclamation-triangle me-2"></i>
                            <span>${msg}</span>
                        </div>
                        <div>
                            ${job.completedNotes > 0 ? `<button class="btn btn-sm btn-outline-light me-1" onclick="window._noteGenerationTracker._viewEncounter(${job.encounterId})">View</button>` : ''}
                            <button class="btn btn-sm btn-outline-light" onclick="window._noteGenerationTracker._dismissJob('${job.jobId}')">
                                <i class="bi bi-x"></i>
                            </button>
                        </div>
                    </div>`;
            }
            return '';
        }).join('');

        this._barEl.innerHTML = items;
    }

    /**
     * Start polling for a job's status.
     */
    _startPolling(jobId) {
        if (this._pollIntervals.has(jobId)) return;

        // Poll immediately on first tick (for restored jobs)
        this._pollOnce(jobId);

        const interval = setInterval(() => this._pollOnce(jobId), 5000);
        this._pollIntervals.set(jobId, interval);
    }

    async _pollOnce(jobId) {
        try {
            const result = await window.apiRequest(`/recording/note-generation-status/${jobId}`);
            if (!result || !result.Success) {
                // Job not found on server — it expired or server restarted. Clean up.
                this._stopPolling(jobId);
                this._jobs.delete(jobId);
                this._saveToStorage();
                this._renderBar();
                return;
            }

            const job = this._jobs.get(jobId);
            if (!job) {
                this._stopPolling(jobId);
                return;
            }

            job.completedNotes = result.CompletedNotes;
            job.status = result.Status;

            this._saveToStorage();
            this._renderBar();

            // Job finished — stop polling
            if (result.Status !== 'processing') {
                this._stopPolling(jobId);
                this._onJobFinished(job, result);
            }
        } catch (err) {
            console.error('[NoteGenTracker] Poll error:', err);
        }
    }

    _stopPolling(jobId) {
        const interval = this._pollIntervals.get(jobId);
        if (interval) {
            clearInterval(interval);
            this._pollIntervals.delete(jobId);
        }
    }

    /**
     * Handle job completion/failure.
     */
    _onJobFinished(job, result) {
        if (job.status === 'completed') {
            if (typeof Toast !== 'undefined') {
                Toast.success(`Clinical notes ready for ${job.patientName}`);
            }

            // If provider is currently viewing this encounter, refresh only the active step
            if (window._encounterWorkspace && window._encounterWorkspace.encounterId === job.encounterId) {
                const ws = window._encounterWorkspace;
                const step = ws.currentStep;
                // Re-render only the step the user is currently on
                if (step === 3) ws._renderNoteStep?.();
                else if (step === 4) ws._renderOrdersStep?.();
                else if (step === 5) ws._renderPrescriptionsStep?.();
                // Mark other steps as stale so they refresh on navigation
                ws._noteStepStale = true;
                ws._ordersStepStale = true;
                ws._prescriptionsStepStale = true;
            }

            // Auto-dismiss after 15 seconds
            setTimeout(() => this._dismissJob(job.jobId), 15000);
        } else if (job.status === 'partiallycompleted') {
            if (typeof Toast !== 'undefined') {
                Toast.warning(`${job.completedNotes}/${job.totalNotes} notes generated for ${job.patientName}. Some failed.`);
            }
            setTimeout(() => this._dismissJob(job.jobId), 20000);
        } else {
            if (typeof Toast !== 'undefined') {
                Toast.error(`Failed to generate notes for ${job.patientName}`);
            }
            setTimeout(() => this._dismissJob(job.jobId), 10000);
        }
    }

    _viewEncounter(encounterId) {
        // Navigate to the encounter workspace
        window.location.href = `/Encounter/${encounterId}`;
    }

    _dismissJob(jobId) {
        this._stopPolling(jobId);
        this._jobs.delete(jobId);
        this._saveToStorage();
        this._renderBar();
    }

    // =====================================================
    // localStorage persistence for MPA page navigations
    // =====================================================

    _saveToStorage() {
        try {
            const processing = [...this._jobs.values()].filter(j => j.status === 'processing');
            if (processing.length === 0) {
                localStorage.removeItem(this._storageKey);
            } else {
                localStorage.setItem(this._storageKey, JSON.stringify(processing));
            }
        } catch (e) { /* localStorage unavailable */ }
    }

    _restoreFromStorage() {
        try {
            const raw = localStorage.getItem(this._storageKey);
            if (!raw) return;

            const jobs = JSON.parse(raw);
            if (!Array.isArray(jobs) || jobs.length === 0) return;

            // Filter out stale jobs (older than 10 minutes)
            const cutoff = Date.now() - 10 * 60 * 1000;
            const valid = jobs.filter(j => !j._savedAt || j._savedAt > cutoff);

            if (valid.length === 0) {
                localStorage.removeItem(this._storageKey);
                return;
            }

            for (const job of valid) {
                delete job._savedAt;
                this._jobs.set(job.jobId, job);
                this._startPolling(job.jobId);
            }

            this._renderBar();
        } catch (e) {
            localStorage.removeItem(this._storageKey);
        }
    }

    _escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }
}

// Global singleton — auto-init on page load to restore any active jobs
window._noteGenerationTracker = new NoteGenerationTracker();
document.addEventListener('DOMContentLoaded', () => {
    window._noteGenerationTracker.init();
});
