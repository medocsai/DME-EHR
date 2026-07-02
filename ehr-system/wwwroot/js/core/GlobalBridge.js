/**
 * GlobalBridge - Legacy Global Function Bridge
 *
 * Provides global functions for legacy HTML onclick handlers.
 * These functions delegate to the appropriate module instances.
 *
 * This file exists for backward compatibility with existing HTML.
 * New code should use data-action attributes and event delegation.
 */

// ============================================
// Global State Variables (Legacy Support)
// ============================================
let currentUser = null;
let authToken = null;
let calendar = null;
let selectedPatient = null;
let selectedProvider = null;
let selectedClinicId = null;
let currentLocation = null;
let availableLocations = [];
let consentHub = null;

// Schedule filter state
let scheduleFilters = {
    types: new Set(),
    status: new Set(),
    workflow: new Set(),
    patientId: null,
    providerId: null,
    totalEvents: 0,
    filteredEvents: 0
};

// ============================================
// Core Utility Functions
// ============================================

/**
 * Show toast notification
 */
function showToast(title, message, type = 'success') {
    if (window.Toast) {
        switch (type) {
            case 'error':
            case 'danger':
                Toast.error(title, message);
                break;
            case 'warning':
                Toast.warning(title, message);
                break;
            case 'info':
                Toast.info(title, message);
                break;
            default:
                Toast.success(title, message);
        }
    } else {
        console.log(`[${type.toUpperCase()}] ${title}: ${message}`);
    }
}

/**
 * Escape HTML to prevent XSS
 */
function escapeHtml(unsafe) {
    if (unsafe == null) return '';
    return String(unsafe)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#039;');
}

/**
 * Cleanup orphaned modal backdrops
 * Call this when multiple modals may have been opened/closed and backdrops remain
 */
function cleanupModalBackdrops() {
    const openModals = document.querySelectorAll('.modal.show').length;
    const backdrops = document.querySelectorAll('.modal-backdrop');

    if (openModals === 0) {
        // No modals open → every backdrop is orphaned.
        backdrops.forEach(backdrop => backdrop.remove());
        document.body.classList.remove('modal-open');
        document.body.style.removeProperty('overflow');
        document.body.style.removeProperty('padding-right');
    } else if (backdrops.length > openModals) {
        // Stacked modals left too many backdrops behind (e.g. a dynamically-
        // created addendum modal closed while its parent modal is still open).
        // Trim the excess from the top-most backdrops so the remaining visible
        // modal isn't overlaid by a dark layer it doesn't own.
        const excess = backdrops.length - openModals;
        for (let i = 0; i < excess; i++) {
            backdrops[backdrops.length - 1 - i]?.remove();
        }
    }
}

/**
 * Render contact information (phone/email) for modal tables
 * @param {string} phone - Phone number
 * @param {string} email - Email address
 * @returns {string} HTML string with contact links
 */
function renderModalContactInfo(phone, email) {
    const parts = [];
    if (phone) {
        parts.push(`<a href="tel:${escapeHtml(phone)}" class="text-muted text-decoration-none" title="Call ${escapeHtml(phone)}"><i class="bi bi-telephone-fill"></i> ${escapeHtml(phone)}</a>`);
    }
    if (email) {
        parts.push(`<a href="mailto:${escapeHtml(email)}" class="text-muted text-decoration-none" title="Email ${escapeHtml(email)}"><i class="bi bi-envelope-fill"></i> ${escapeHtml(email)}</a>`);
    }
    if (parts.length === 0) {
        return '<span class="text-muted">-</span>';
    }
    return parts.join('<br>');
}

/**
 * Debounce function calls
 */
function debounce(func, wait) {
    let timeout;
    return function executedFunction(...args) {
        const later = () => {
            clearTimeout(timeout);
            func(...args);
        };
        clearTimeout(timeout);
        timeout = setTimeout(later, wait);
    };
}

// ============================================
// Loading State Management
// ============================================

function showGlobalLoader() {
    if (window.LoadingState) {
        LoadingState.show();
    }
}

function hideGlobalLoader() {
    if (window.LoadingState) {
        LoadingState.hide();
    }
}

function hideAppLoadingScreen() {
    const loadingScreen = document.getElementById('appLoadingScreen');
    if (loadingScreen) {
        loadingScreen.classList.add('d-none');
    }
}

function updateAppLoadingStatus(message) {
    const statusEl = document.querySelector('.app-loading-status');
    if (statusEl) {
        statusEl.textContent = message;
    }
}

// ============================================
// Date/Time Formatting (Timezone-aware)
// ============================================

function parseServerDateTime(dateString) {
    if (!dateString) return null;
    // DateOnly strings (e.g. "2026-04-12") must be parsed as local dates, NOT UTC.
    // new Date("2026-04-12") interprets as midnight UTC, shifting back 1 day in western timezones.
    if (/^\d{4}-\d{2}-\d{2}$/.test(dateString)) {
        const [y, m, d] = dateString.split('-').map(Number);
        return new Date(y, m - 1, d);
    }
    if (dateString.endsWith('Z') || /[+-]\d{2}:\d{2}$/.test(dateString)) {
        return new Date(dateString);
    }
    return new Date(dateString + 'Z');
}
window.parseServerDateTime = parseServerDateTime;

function formatDate(dateString) {
    if (!dateString) return '-';
    const date = parseServerDateTime(dateString);
    if (!date || isNaN(date.getTime())) return '-';
    return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
}

function formatTime(dateString) {
    if (!dateString) return '-';
    const date = parseServerDateTime(dateString);
    if (!date || isNaN(date.getTime())) return '-';
    return date.toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit' });
}

function formatDateTime(dateString) {
    if (!dateString) return '-';
    return `${formatDate(dateString)} ${formatTime(dateString)}`;
}

function formatCurrency(amount) {
    if (amount == null) return '$0.00';
    return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(amount);
}

function formatDateForInput(date) {
    if (!date) return '';
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const day = String(date.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
}

function formatTimeForInput(date) {
    if (!date) return '';
    const hours = String(date.getHours()).padStart(2, '0');
    const minutes = String(date.getMinutes()).padStart(2, '0');
    return `${hours}:${minutes}`;
}

function getUserTimezone() {
    return Intl.DateTimeFormat().resolvedOptions().timeZone;
}

// ============================================
// Location Timezone Functions
// ============================================

function getCurrentLocationTimezone() {
    if (window.locationModule) {
        return window.locationModule.getCurrentTimezone();
    }
    if (currentLocation && currentLocation.TimeZoneId) {
        return {
            timeZoneId: currentLocation.TimeZoneId,
            timeZoneAbbreviation: currentLocation.TimeZoneAbbreviation || 'ET'
        };
    }
    const stored = localStorage.getItem('currentLocationTimezone');
    if (stored) {
        try {
            return JSON.parse(stored);
        } catch (e) {
            console.error('Error parsing stored timezone:', e);
        }
    }
    return { timeZoneId: 'America/Chicago', timeZoneAbbreviation: 'CT' };
}

function setCurrentLocationTimezone(timeZoneId, timeZoneAbbreviation) {
    const tzInfo = { timeZoneId, timeZoneAbbreviation };
    localStorage.setItem('currentLocationTimezone', JSON.stringify(tzInfo));
    if (currentLocation) {
        currentLocation.TimeZoneId = timeZoneId;
        currentLocation.TimeZoneAbbreviation = timeZoneAbbreviation;
    }
}

function convertUtcToTimezone(utcDateTime, ianaTimeZoneId) {
    const date = typeof utcDateTime === 'string' ? parseServerDateTime(utcDateTime) : utcDateTime;
    if (!date || !ianaTimeZoneId) {
        return {
            year: date?.getFullYear() || 0,
            month: date?.getMonth() || 0,
            day: date?.getDate() || 0,
            hours: date?.getHours() || 0,
            minutes: date?.getMinutes() || 0,
            dateString: date?.toISOString().split('T')[0] || '',
            timeString: date?.toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit' }) || '',
            isoString: date?.toISOString() || ''
        };
    }

    const formatter = new Intl.DateTimeFormat('en-US', {
        timeZone: ianaTimeZoneId,
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit',
        hour12: false
    });

    const parts = formatter.formatToParts(date);
    const getPart = (type) => parts.find(p => p.type === type)?.value || '0';

    const year = parseInt(getPart('year'));
    const month = parseInt(getPart('month')) - 1;
    const day = parseInt(getPart('day'));
    const hours = parseInt(getPart('hour'));
    const minutes = parseInt(getPart('minute'));

    const dateString = `${year}-${String(month + 1).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
    const timeString = `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}:00`;
    const isoString = `${dateString}T${timeString}`;

    return { year, month, day, hours, minutes, dateString, timeString, isoString };
}

// ============================================
// Status & Type Helpers
// ============================================

function getAppointmentTypeName(type) {
    const types = ['New Patient', 'Follow-Up', 'Annual Physical', 'Wellness', 'Consultation', 'Telehealth', 'Procedure', 'Urgent', 'Lab Review', 'Med Review'];
    return types[type] || 'Appointment';
}

function getAppointmentTypeCode(type) {
    const codes = { 'New Patient Visit': 0, 'Follow-Up Visit': 1, 'Annual Physical': 2, 'Wellness Exam': 3, 'Consultation': 4, 'Telehealth': 5, 'Procedure Visit': 6, 'Urgent Visit': 7, 'Lab Review': 8, 'Medication Review': 9 };
    return codes[type] ?? type;
}

function getStatusBadge(status, type = 'appointment') {
    if (type === 'appointment') {
        const statuses = {
            0: { class: 'bg-secondary', text: 'Scheduled' },
            1: { class: 'bg-info', text: 'Confirmed' },
            2: { class: 'bg-primary', text: 'Checked In' },
            3: { class: 'bg-warning text-dark', text: 'In Progress' },
            4: { class: 'bg-success', text: 'Completed' },
            5: { class: 'bg-danger', text: 'Cancelled' },
            6: { class: 'bg-dark', text: 'No Show' }
        };
        const s = statuses[status] || { class: 'bg-secondary', text: 'Unknown' };
        return `<span class="badge ${s.class}">${s.text}</span>`;
    }
    return `<span class="badge bg-secondary">${status}</span>`;
}

// ============================================
// Navigation
// ============================================

function navigateTo(page, updateHash = true) {
    const routes = {
        'dashboard': '/Home/Dashboard',
        'schedule': '/Home/Schedule',
        'patients': '/Home/Patients',
        'providers': '/Home/Providers',
        'notes': '/Home/Notes',
        'billing': '/Home/Billing',
        'unavailability': '/Home/Unavailability',
        'reports': '/Home/Reports',
        'templates': '/Home/Templates',
        'users': '/Home/Users',
        'settings': '/Home/Settings',
        'tenants': '/Home/Tenants',
        'consent-forms': '/Home/ConsentForms'
    };

    const url = routes[page.toLowerCase()];
    if (url) {
        window.location.href = url;
    }
}

// ============================================
// Authentication Functions
// ============================================

function showForgotPassword() {
    // Login page uses two sibling cards: .login-card holding #loginForm,
    // and #forgotPasswordCard. Toggle visibility via d-none.
    const loginForm = document.getElementById('loginForm');
    const loginCard = loginForm ? loginForm.closest('.login-card') : null;
    const forgotCard = document.getElementById('forgotPasswordCard');

    if (loginCard) loginCard.classList.add('d-none');
    if (forgotCard) {
        forgotCard.classList.remove('d-none');
        // Reset any previous state
        const errEl = document.getElementById('forgotPasswordError');
        const okEl = document.getElementById('forgotPasswordSuccess');
        const emailEl = document.getElementById('forgotPasswordEmail');
        if (errEl) { errEl.classList.add('d-none'); errEl.textContent = ''; }
        if (okEl) { okEl.classList.add('d-none'); okEl.textContent = ''; }
        if (emailEl) {
            // Pre-fill from login email if entered
            const loginEmail = document.getElementById('loginEmail')?.value?.trim();
            if (loginEmail) emailEl.value = loginEmail;
            setTimeout(() => emailEl.focus(), 50);
        }
    }
    _ensureForgotPasswordHandlerBound();
}

function hideForgotPassword() {
    const loginForm = document.getElementById('loginForm');
    const loginCard = loginForm ? loginForm.closest('.login-card') : null;
    const forgotCard = document.getElementById('forgotPasswordCard');

    if (forgotCard) forgotCard.classList.add('d-none');
    if (loginCard) loginCard.classList.remove('d-none');
}

// Bind the forgot-password form submit handler exactly once.
function _ensureForgotPasswordHandlerBound() {
    const form = document.getElementById('forgotPasswordForm');
    if (!form || form.dataset.handlerBound === '1') return;
    form.dataset.handlerBound = '1';

    form.addEventListener('submit', async function (e) {
        e.preventDefault();
        const errEl = document.getElementById('forgotPasswordError');
        const okEl = document.getElementById('forgotPasswordSuccess');
        const emailEl = document.getElementById('forgotPasswordEmail');
        const btn = form.querySelector('button[type="submit"]');

        if (errEl) { errEl.classList.add('d-none'); errEl.textContent = ''; }
        if (okEl) { okEl.classList.add('d-none'); okEl.textContent = ''; }

        const email = emailEl?.value?.trim();
        if (!email) {
            if (errEl) { errEl.textContent = 'Please enter your email address.'; errEl.classList.remove('d-none'); }
            return;
        }

        const originalHtml = btn ? btn.innerHTML : '';
        if (btn) {
            btn.disabled = true;
            btn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Sending...';
        }

        // Use a generic success message regardless of result to prevent email enumeration
        const genericSuccess = 'If an account with that email exists, a password reset link has been sent. Please check your inbox.';

        try {
            await fetch('/api/auth/forgot-password', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ Email: email })
            });
            if (okEl) { okEl.textContent = genericSuccess; okEl.classList.remove('d-none'); }
            if (emailEl) emailEl.value = '';
        } catch (err) {
            // Still show generic success to avoid leaking account existence
            if (okEl) { okEl.textContent = genericSuccess; okEl.classList.remove('d-none'); }
        } finally {
            if (btn) {
                btn.disabled = false;
                btn.innerHTML = originalHtml || '<i class="bi bi-envelope me-2"></i>Send Reset Link';
            }
        }
    });
}

function cancelTenantSelection() {
    if (window.authModule) {
        window.authModule.cancelTenantSelection();
    }
    const modal = document.getElementById('tenantSelectionModal');
    if (modal) {
        const bsModal = bootstrap.Modal.getInstance(modal);
        if (bsModal) bsModal.hide();
    }
}

function togglePasswordVisibility(inputId, button) {
    const input = document.getElementById(inputId);
    if (!input) return;

    const icon = button?.querySelector('i');
    if (input.type === 'password') {
        input.type = 'text';
        if (icon) {
            icon.classList.remove('bi-eye');
            icon.classList.add('bi-eye-slash');
        }
    } else {
        input.type = 'password';
        if (icon) {
            icon.classList.remove('bi-eye-slash');
            icon.classList.add('bi-eye');
        }
    }
}

// ============================================
// Dashboard Widget Functions
// ============================================

async function openMissingNotesModal() {
    const modal = document.getElementById('missingNotesModal');
    if (modal) {
        const bsModal = new bootstrap.Modal(modal);
        bsModal.show();
        await loadMissingNotesModalData();
    }
}

async function openMissingSignatureModal() {
    const modal = document.getElementById('missingSignatureModal');
    if (modal) {
        const bsModal = new bootstrap.Modal(modal);
        bsModal.show();
        await loadMissingSignatureModalData();
    }
}

async function openRequireScheduleModal() {
    const modal = document.getElementById('requireScheduleModal');
    if (modal) {
        const bsModal = new bootstrap.Modal(modal);
        bsModal.show();
        await loadRequireScheduleModalData();
    }
}

async function openNoShowModal() {
    const modal = document.getElementById('noShowModal');
    if (modal) {
        const bsModal = new bootstrap.Modal(modal);
        bsModal.show();
        await loadNoShowModalData();
    }
}

async function openMissedAppointmentsModal() {
    const modal = document.getElementById('missedAppointmentsModal');
    if (modal) {
        const bsModal = new bootstrap.Modal(modal);
        bsModal.show();
        await loadMissedAppointmentsModalData();
    }
}

/**
 * Open Patient Sticky Notes Modal from Dashboard
 * @param {number} patientId - Patient ID
 * @param {string} patientName - Patient name for display
 */
async function openPatientStickyNotesModal(patientId, patientName) {
    const modal = document.getElementById('patientStickyNotesModal');
    if (!modal) return;

    // Store patientId on the modal for add/delete operations
    modal.dataset.patientId = patientId;

    // Set patient name in header
    const nameEl = document.getElementById('stickyNotesPatientName');
    if (nameEl) nameEl.textContent = patientName;

    // Show modal
    const bsModal = bootstrap.Modal.getInstance(modal) || new bootstrap.Modal(modal);
    bsModal.show();

    // Load notes
    await _loadDashboardStickyNotes(patientId);

    // Bind add button (remove old listener first to avoid duplicates)
    const btnAdd = document.getElementById('btnAddDashboardStickyNote');
    const input = document.getElementById('dashboardStickyNoteInput');

    if (btnAdd) {
        const newBtnAdd = btnAdd.cloneNode(true);
        btnAdd.parentNode.replaceChild(newBtnAdd, btnAdd);
        newBtnAdd.addEventListener('click', () => _addDashboardStickyNote(patientId));
    }

    if (input) {
        const newInput = input.cloneNode(true);
        input.parentNode.replaceChild(newInput, input);
        newInput.value = '';
        newInput.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') _addDashboardStickyNote(patientId);
        });
        setTimeout(() => newInput.focus(), 300);
    }
}

/**
 * Load sticky notes for the modal
 * @param {number} patientId
 */
async function _loadDashboardStickyNotes(patientId) {
    const listEl = document.getElementById('dashboardStickyNotesList');
    if (!listEl) return;

    try {
        const token = localStorage.getItem('authToken') || window.App?.getAuthToken();
        const response = await fetch(`/api/patients/${patientId}/sticky-notes`, {
            headers: { 'Authorization': `Bearer ${token}` }
        });
        if (!response.ok) throw new Error('Failed to load sticky notes');

        const notes = await response.json();

        if (!notes || notes.length === 0) {
            listEl.innerHTML = '<div class="text-center text-muted py-3"><i class="bi bi-sticky me-2"></i>No sticky notes yet</div>';
            return;
        }

        listEl.innerHTML = notes.map(note => `
            <div class="d-flex justify-content-between align-items-start p-2 mb-2 rounded" style="background: #fef9c3;">
                <div class="flex-grow-1">
                    <div class="small">${_escapeStickyHtml(note.Content)}</div>
                    <div class="text-muted" style="font-size: 0.7rem;">
                        ${_escapeStickyHtml(note.CreatedByName)} &bull; ${_formatStickyNoteTime(note.CreatedAt)}
                    </div>
                </div>
                <button class="btn btn-sm btn-link text-danger p-0 ms-2" title="Delete"
                        onclick="event.stopPropagation(); _deleteDashboardStickyNote(${patientId}, ${note.PatientStickyNoteId});">
                    <i class="bi bi-x-lg"></i>
                </button>
            </div>
        `).join('');

    } catch (error) {
        console.error('[StickyNotes] Failed to load:', error);
        listEl.innerHTML = '<div class="text-center text-danger py-3">Failed to load notes</div>';
    }
}

/**
 * Add a sticky note from the dashboard modal
 * @param {number} patientId
 */
async function _addDashboardStickyNote(patientId) {
    const input = document.getElementById('dashboardStickyNoteInput');
    const content = input?.value?.trim();
    if (!content) return;

    try {
        const token = localStorage.getItem('authToken') || window.App?.getAuthToken();
        const response = await fetch(`/api/patients/${patientId}/sticky-notes`, {
            method: 'POST',
            headers: {
                'Authorization': `Bearer ${token}`,
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({ Content: content })
        });

        if (!response.ok) throw new Error('Failed to add note');

        if (input) input.value = '';
        await _loadDashboardStickyNotes(patientId);
        _updateDashboardStickyBadge(patientId, 1);
        Toast.success('Added', 'Sticky note added');

    } catch (error) {
        console.error('[StickyNotes] Failed to add:', error);
        Toast.error('Error', 'Failed to add sticky note');
    }
}

/**
 * Delete a sticky note from the dashboard modal
 * @param {number} patientId
 * @param {number} noteId
 */
async function _deleteDashboardStickyNote(patientId, noteId) {
    try {
        const token = localStorage.getItem('authToken') || window.App?.getAuthToken();
        const response = await fetch(`/api/patients/${patientId}/sticky-notes/${noteId}`, {
            method: 'DELETE',
            headers: { 'Authorization': `Bearer ${token}` }
        });

        if (!response.ok) throw new Error('Failed to delete note');

        await _loadDashboardStickyNotes(patientId);
        _updateDashboardStickyBadge(patientId, -1);
        Toast.success('Removed', 'Sticky note removed');

    } catch (error) {
        console.error('[StickyNotes] Failed to delete:', error);
        Toast.error('Error', 'Failed to delete sticky note');
    }
}

/**
 * Update the sticky note badge count in the appointments table after add/delete
 * @param {number} patientId
 * @param {number} delta - +1 for add, -1 for delete
 */
function _updateDashboardStickyBadge(patientId, delta) {
    // Find all appointment rows for this patient and update their badges
    const table = document.getElementById('todayScheduleTable');
    if (!table) return;

    table.querySelectorAll('tr[data-appointment-id]').forEach(row => {
        const patientLink = row.querySelector(`a[onclick*="viewPatient(${patientId})"]`);
        if (!patientLink) return;

        const badge = row.querySelector('.sticky-note-badge, .sticky-note-badge-empty');
        if (!badge) return;

        // Parse current count
        let currentCount = 0;
        const countText = badge.textContent.trim();
        if (badge.classList.contains('sticky-note-badge')) {
            currentCount = parseInt(countText) || 0;
        }
        const newCount = Math.max(0, currentCount + delta);

        // Rebuild the badge
        const patientName = patientLink.textContent.trim().replace(/'/g, "\\'");
        if (newCount > 0) {
            badge.outerHTML = `<span class="badge bg-warning text-dark ms-1 sticky-note-badge" role="button"
                onclick="event.stopPropagation(); openPatientStickyNotesModal(${patientId}, '${patientName}'); return false;"
                title="${newCount} sticky note(s)">
                <i class="bi bi-sticky-fill"></i> ${newCount}
            </span>`;
        } else {
            badge.outerHTML = `<span class="text-muted ms-1 sticky-note-badge-empty" role="button"
                onclick="event.stopPropagation(); openPatientStickyNotesModal(${patientId}, '${patientName}'); return false;"
                title="Add sticky note">
                <i class="bi bi-sticky"></i>
            </span>`;
        }
    });
}

/** Escape HTML for sticky notes display */
function _escapeStickyHtml(str) {
    if (!str) return '';
    const div = document.createElement('div');
    div.textContent = str;
    return div.innerHTML;
}

/** Format sticky note timestamp as relative time */
function _formatStickyNoteTime(dateStr) {
    if (!dateStr) return '';
    const date = new Date(dateStr);
    const now = new Date();
    const diffMs = now - date;
    const diffMins = Math.floor(diffMs / 60000);
    const diffHrs = Math.floor(diffMs / 3600000);
    const diffDays = Math.floor(diffMs / 86400000);

    if (diffMins < 1) return 'Just now';
    if (diffMins < 60) return `${diffMins}m ago`;
    if (diffHrs < 24) return `${diffHrs}h ago`;
    if (diffDays < 7) return `${diffDays}d ago`;
    return date.toLocaleDateString();
}

/**
 * Load Missing Notes Modal Data
 */
async function loadMissingNotesModalData() {
    const body = document.getElementById('missingNotesModalBody');
    if (!body) return;

    body.innerHTML = '<div class="text-center py-4"><div class="spinner-border text-orange"></div></div>';

    try {
        const selectedLocationId = localStorage.getItem('selectedLocationId') || '';
        const paramString = selectedLocationId ? `?locationId=${selectedLocationId}` : '';

        const notes = await apiRequest('/dashboard/missing-notes' + paramString);

        if (!notes || notes.length === 0) {
            body.innerHTML = '<div class="text-center text-muted py-4">No missing notes</div>';
            return;
        }

        body.innerHTML = `
            <div class="table-responsive">
                <table class="table table-sm table-hover mb-0">
                    <thead>
                        <tr>
                            <th>Patient</th>
                            <th>Contact</th>
                            <th>Date</th>
                            <th>Type</th>
                            <th>Provider</th>
                            <th>Actions</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${notes.map(note => `
                            <tr>
                                <td>
                                    <a href="#" class="text-decoration-none fw-bold patient-link-modal" onclick="viewPatient(${note.PatientId}); return false;" title="View Patient Profile">
                                        ${escapeHtml(note.PatientName)}
                                    </a>
                                </td>
                                <td class="small">
                                    ${renderModalContactInfo(note.PatientPhone, note.PatientEmail)}
                                </td>
                                <td>${formatDate(note.AppointmentDate)}</td>
                                <td><span class="badge bg-light text-dark">${getAppointmentTypeName(note.Type)}</span></td>
                                <td>${escapeHtml(note.ProviderName || '')}</td>
                                <td>
                                    <div class="d-flex gap-1 flex-wrap">
                                        <button class="btn btn-sm btn-outline-secondary" onclick="openAppointmentDetails(${note.AppointmentId})" title="View appointment details">
                                            <i class="bi bi-eye me-1"></i>View
                                        </button>
                                        <button class="btn btn-sm btn-outline-orange" onclick="openCreateClinicalNote({AppointmentId: ${note.AppointmentId}, PatientId: ${note.PatientId}, PatientName: '${escapeHtml(note.PatientName).replace(/'/g, "\\'")}', Type: ${note.Type}, ProviderId: ${note.ProviderId}, ProviderName: '${escapeHtml(note.ProviderName || '').replace(/'/g, "\\'")}', LocationId: ${note.LocationId || 'null'}, StartTime: '${note.AppointmentDate}'})" title="Write Note">
                                            <i class="bi bi-pencil-square me-1"></i>Write
                                        </button>
                                        <button class="btn btn-sm btn-outline-primary" onclick="openRecordSessionFromModal(${note.AppointmentId}, ${note.PatientId}, '${escapeHtml(note.PatientName).replace(/'/g, "\\'")}')" title="Record Session">
                                            <i class="bi bi-mic-fill me-1"></i>Record
                                        </button>
                                    </div>
                                </td>
                            </tr>
                        `).join('')}
                    </tbody>
                </table>
            </div>
        `;
    } catch (error) {
        body.innerHTML = '<div class="text-center text-danger py-4">Failed to load data</div>';
    }
}

/**
 * Open Record Session from Modal
 * Wrapper function that fetches appointment data before opening recording modal
 */
async function openRecordSessionFromModal(appointmentId, patientId, patientName) {
    try {
        const appointment = await apiRequest(`/appointments/${appointmentId}`);
        if (appointment) {
            window.currentAppointmentForNotes = appointment;
            if (typeof openRecordSessionModal === 'function') {
                openRecordSessionModal();
            }
        }
    } catch (error) {
        console.error('[GlobalBridge] Failed to load appointment for recording:', error);
        if (typeof showToast === 'function') {
            showToast('Error', 'Failed to load appointment', 'error');
        }
    }
}

/**
 * Load Missing Signature Modal Data
 */
async function loadMissingSignatureModalData() {
    const body = document.getElementById('missingSignatureModalBody');
    if (!body) return;

    body.innerHTML = '<div class="text-center py-4"><div class="spinner-border text-purple"></div></div>';

    try {
        const selectedLocationId = localStorage.getItem('selectedLocationId') || '';
        const paramString = selectedLocationId ? `?locationId=${selectedLocationId}` : '';

        const notes = await apiRequest('/dashboard/missing-signature' + paramString);

        if (!notes || notes.length === 0) {
            body.innerHTML = '<div class="text-center text-muted py-4">No notes needing signature</div>';
            return;
        }

        const currentUserData = JSON.parse(localStorage.getItem('currentUser') || '{}');
        const isClinician = currentUserData?.Role === 2 || currentUserData?.Role === 0;

        body.innerHTML = `
            <div class="table-responsive">
                <table class="table table-sm table-hover mb-0">
                    <thead>
                        <tr>
                            <th>Patient</th>
                            <th>Contact</th>
                            <th>Note Type</th>
                            <th>Date</th>
                            <th>Provider</th>
                            <th>Actions</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${notes.map(note => {
                            const canSign = isClinician && currentUserData?.ProviderId === note.ProviderId;
                            const actionLabel = canSign ? 'Sign' : 'View Note';
                            return `
                            <tr>
                                <td>
                                    <a href="#" class="text-decoration-none fw-bold patient-link-modal" onclick="viewPatient(${note.PatientId}); return false;" title="View Patient Profile">
                                        ${escapeHtml(note.PatientName)}
                                    </a>
                                </td>
                                <td class="small">
                                    ${renderModalContactInfo(note.PatientPhone, note.PatientEmail)}
                                </td>
                                <td>${escapeHtml(note.NoteType || '')}</td>
                                <td>${formatDate(note.NoteDate || note.AppointmentDate)}</td>
                                <td>${escapeHtml(note.ProviderName || '')}</td>
                                <td>
                                    <button class="btn btn-sm btn-outline-primary" onclick="viewClinicalNote(${note.ClinicalNoteId})">
                                        ${actionLabel}
                                    </button>
                                </td>
                            </tr>
                        `}).join('')}
                    </tbody>
                </table>
            </div>
        `;
    } catch (error) {
        body.innerHTML = '<div class="text-center text-danger py-4">Failed to load data</div>';
    }
}

/**
 * Load Require Schedule Modal Data
 */
async function loadRequireScheduleModalData() {
    const body = document.getElementById('requireScheduleModalBody');
    if (!body) return;

    body.innerHTML = '<div class="text-center py-4"><div class="spinner-border text-info"></div></div>';

    try {
        const selectedLocationId = localStorage.getItem('selectedLocationId') || '';
        const paramString = selectedLocationId ? `?locationId=${selectedLocationId}` : '';

        const episodes = await apiRequest('/care-episodes/require-schedule' + paramString);

        if (!episodes || episodes.length === 0) {
            body.innerHTML = '<div class="text-center text-muted py-4">All appointments are scheduled</div>';
            return;
        }

        body.innerHTML = `
            <div class="table-responsive">
                <table class="table table-sm table-hover mb-0">
                    <thead>
                        <tr>
                            <th>Patient</th>
                            <th>Contact</th>
                            <th>Required Appts</th>
                            <th>Scheduled</th>
                            <th>Expected</th>
                            <th>Action</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${episodes.map(ep => `
                            <tr>
                                <td>
                                    <a href="#" class="text-decoration-none fw-bold patient-link-modal" onclick="viewPatient(${ep.PatientId}); return false;" title="View Patient Profile">
                                        ${escapeHtml(ep.PatientName)}
                                    </a>
                                    ${ep.PatientMRN ? `<br><small class="text-muted">${escapeHtml(ep.PatientMRN)}</small>` : ''}
                                </td>
                                <td class="small">
                                    ${renderModalContactInfo(ep.PatientPhone, ep.PatientEmail)}
                                </td>
                                <td><span class="badge bg-info">${ep.RequiredAppointments || 0}</span></td>
                                <td>${ep.ScheduledAppointments || 0}</td>
                                <td>${ep.ExpectedVisits || 0}</td>
                                <td>
                                    <button class="btn btn-sm btn-outline-info" onclick="scheduleFromRequireCard(${ep.PatientId}, ${ep.CareEpisodeId}); bootstrap.Modal.getInstance(document.getElementById('requireScheduleModal')).hide();">
                                        <i class="bi bi-calendar-plus me-1"></i>Schedule
                                    </button>
                                </td>
                            </tr>
                        `).join('')}
                    </tbody>
                </table>
            </div>
        `;
    } catch (error) {
        body.innerHTML = '<div class="text-center text-danger py-4">Failed to load data</div>';
    }
}

/**
 * Load No Show Modal Data
 */
async function loadNoShowModalData() {
    const body = document.getElementById('noShowModalBody');
    if (!body) return;

    body.innerHTML = '<div class="text-center py-4"><div class="spinner-border text-warning"></div></div>';

    try {
        const selectedLocationId = localStorage.getItem('selectedLocationId') || '';
        const paramString = selectedLocationId ? `?locationId=${selectedLocationId}` : '';

        const data = await apiRequest('/care-episodes/dashboard' + paramString);
        const alerts = data?.NoShowAlerts || [];

        if (alerts.length === 0) {
            body.innerHTML = '<div class="text-center text-muted py-4">No no-show alerts</div>';
            return;
        }

        body.innerHTML = `
            <div class="table-responsive">
                <table class="table table-sm table-hover mb-0">
                    <thead>
                        <tr>
                            <th>Patient</th>
                            <th>Contact</th>
                            <th>Scheduled Time</th>
                            <th>Overdue</th>
                            <th>Actions</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${alerts.map(alert => `
                            <tr>
                                <td>
                                    <a href="#" class="text-decoration-none fw-bold patient-link-modal" onclick="viewPatient(${alert.PatientId}); return false;" title="View Patient Profile">
                                        ${escapeHtml(alert.PatientName)}
                                    </a>
                                </td>
                                <td class="small">
                                    ${renderModalContactInfo(alert.PatientPhone, alert.PatientEmail)}
                                </td>
                                <td>${alert.ScheduledTime || formatTime(alert.StartTime)}</td>
                                <td><span class="text-warning">${alert.MinutesOverdue || alert.OverdueMinutes || '?'} min</span></td>
                                <td>
                                    <div class="btn-group btn-group-sm">
                                        <button class="btn btn-outline-success" onclick="checkInAppointment(${alert.AppointmentId})" title="Check In">
                                            <i class="bi bi-box-arrow-in-right"></i>
                                        </button>
                                        <button class="btn btn-outline-primary" onclick="sendNoShowNotification(${alert.AppointmentId}, this)" title="Send Notification">
                                            <i class="bi bi-bell"></i>
                                        </button>
                                    </div>
                                </td>
                            </tr>
                        `).join('')}
                    </tbody>
                </table>
            </div>
        `;
    } catch (error) {
        body.innerHTML = '<div class="text-center text-danger py-4">Failed to load data</div>';
    }
}

/**
 * Load Missed Appointments Modal Data
 */
async function loadMissedAppointmentsModalData() {
    const body = document.getElementById('missedAppointmentsModalBody');
    if (!body) return;

    body.innerHTML = '<div class="text-center py-4"><div class="spinner-border text-danger"></div></div>';

    try {
        const selectedLocationId = localStorage.getItem('selectedLocationId') || '';
        const paramString = selectedLocationId ? `?locationId=${selectedLocationId}` : '';

        const data = await apiRequest('/care-episodes/dashboard' + paramString);
        const appointments = data?.MissedAppointments || [];

        if (appointments.length === 0) {
            body.innerHTML = '<div class="text-center text-muted py-4">No missed appointments</div>';
            return;
        }

        body.innerHTML = `
            <div class="table-responsive">
                <table class="table table-sm table-hover mb-0">
                    <thead>
                        <tr>
                            <th>Patient</th>
                            <th>Contact</th>
                            <th>Date</th>
                            <th>Days Ago</th>
                            <th>Type</th>
                            <th>Actions</th>
                        </tr>
                    </thead>
                    <tbody>
                        ${appointments.map(apt => {
                            const startTime = apt.StartTime || apt.AppointmentDate;
                            const daysMissed = apt.DaysMissed || Math.floor((Date.now() - new Date(startTime)) / (1000 * 60 * 60 * 24));
                            return `
                            <tr>
                                <td>
                                    <a href="#" class="text-decoration-none fw-bold patient-link-modal" onclick="viewPatient(${apt.PatientId}); return false;" title="View Patient Profile">
                                        ${escapeHtml(apt.PatientName)}
                                    </a>
                                </td>
                                <td class="small">
                                    ${renderModalContactInfo(apt.PatientPhone, apt.PatientEmail)}
                                </td>
                                <td>${formatDate(startTime)}</td>
                                <td><span class="text-danger">${daysMissed} days</span></td>
                                <td><span class="badge bg-light text-dark">${getAppointmentTypeName(apt.Type)}</span></td>
                                <td>
                                    <button class="btn btn-sm btn-outline-primary" onclick="rescheduleFromMissed(${apt.AppointmentId}, ${apt.PatientId}, ${apt.ProviderId})" title="Re-Schedule">
                                        <i class="bi bi-calendar-plus"></i>
                                    </button>
                                </td>
                            </tr>
                        `}).join('')}
                    </tbody>
                </table>
            </div>
        `;
    } catch (error) {
        body.innerHTML = '<div class="text-center text-danger py-4">Failed to load data</div>';
    }
}

// ============================================
// Users Module Functions
// ============================================

function openAddUserModal() {
    if (window.usersModule) {
        window.usersModule.openAddModal();
    } else {
        const modal = document.getElementById('userModal');
        if (modal) {
            const form = modal.querySelector('form');
            if (form) form.reset();
            modal.querySelector('.modal-title').textContent = 'Add User';
            const bsModal = new bootstrap.Modal(modal);
            bsModal.show();
        }
    }
}

function filterUsers(filter) {
    if (window.usersModule) {
        window.usersModule.setFilter(filter);
    }
}

// ============================================
// Templates Module Functions
// ============================================

function clearTemplateFilters() {
    if (window.templatesModule) {
        window.templatesModule.clearFilters();
    } else {
        const searchInput = document.getElementById('templateSearch');
        const typeFilter = document.getElementById('templateTypeFilter');
        const clinicFilter = document.getElementById('templateClinicFilter');
        if (searchInput) searchInput.value = '';
        if (typeFilter) typeFilter.value = '';
        if (clinicFilter) clinicFilter.value = '';
    }
}

// ============================================
// Settings Module Functions
// ============================================

function initializeDefaultSettings() {
    if (window.settingsModule) {
        window.settingsModule.initializeDefaults();
    }
}

// ============================================
// Clinical Notes Functions
// ============================================

/**
 * Save clinical note as draft (no signing)
 */
async function saveClinicalNote() {
    await saveClinicalNoteInternal(false);
}

/**
 * Save and sign clinical note
 */
async function saveAndSignClinicalNote() {
    await saveClinicalNoteInternal(true);
}

/**
 * Internal function to save clinical note with optional signing
 * @param {boolean} alsoSign - Whether to sign after saving
 */
async function saveClinicalNoteInternal(alsoSign = false) {
    // Get content from Trumbowyg or textarea
    let htmlContent;
    if (typeof $.fn.trumbowyg !== 'undefined' && $('#noteHtmlContent').data('trumbowyg')) {
        htmlContent = $('#noteHtmlContent').trumbowyg('html');
    } else {
        htmlContent = document.getElementById('noteHtmlContent')?.value || '';
    }

    // Restore signature placeholder if it was replaced with the line
    htmlContent = htmlContent.replace(
        /<div class="signature-placeholder[^>]*><\/div>/gi,
        '{{provider_signature}}'
    );

    const data = {
        PatientId: parseInt(document.getElementById('notePatientId')?.value) || null,
        ProviderId: parseInt(document.getElementById('noteProviderId')?.value) || null,
        AppointmentId: parseInt(document.getElementById('noteAppointmentId')?.value) || null,
        TemplateId: parseInt(document.getElementById('noteTemplateSelect')?.value) || null,
        Type: parseInt(document.getElementById('noteType')?.value) || 2,
        ServiceDate: document.getElementById('noteServiceDate')?.value || new Date().toISOString().split('T')[0],
        HtmlContent: htmlContent
    };

    if (!data.PatientId || !data.ProviderId) {
        showToast('Error', 'Patient and Provider are required', 'error');
        return;
    }

    if (!htmlContent || htmlContent.trim() === '' || htmlContent.trim() === '<p><br></p>') {
        showToast('Error', 'Note content is required', 'error');
        return;
    }

    try {
        let noteId = document.getElementById('clinicalNoteId')?.value;
        const isNewNote = !noteId;

        if (noteId) {
            // Update existing note
            await apiRequest(`/clinical-notes/${noteId}`, {
                method: 'PUT',
                body: { HtmlContent: data.HtmlContent }
            });
        } else {
            // Create new note
            const result = await apiRequest('/clinical-notes', {
                method: 'POST',
                body: data
            });
            noteId = result.ClinicalNoteId;
            // Store the new note ID
            const noteIdEl = document.getElementById('clinicalNoteId');
            if (noteIdEl) noteIdEl.value = noteId;
        }

        if (alsoSign && noteId) {
            // Sign the note
            const signSuccess = await signClinicalNoteSimple(noteId);
            if (signSuccess) {
                // Close modal
                bootstrap.Modal.getInstance(document.getElementById('clinicalNoteModal'))?.hide();
                // Refresh related modules
                refreshAfterNoteChange();
            }
        } else {
            // Just saved, not signing
            bootstrap.Modal.getInstance(document.getElementById('clinicalNoteModal'))?.hide();
            refreshAfterNoteChange();
        }

    } catch (error) {
        console.error('[GlobalBridge] Save clinical note error:', error);
        showToast('Error', error.message || 'Failed to save clinical note', 'error');
    }
}

// ============================================
// Clinical Note Sign Functions (delegated to ClinicalNoteSignModule)
// ============================================

/**
 * Sign clinical note using the ClinicalNoteSignModule
 * Handles Initial Evaluation/Re-evaluation validation and Care Episode creation
 * @param {number} noteId - The note ID to sign
 * @param {Object} options - Configuration options
 * @returns {Promise<{success: boolean, careEpisodeCreated: boolean, careEpisodeUpdated: boolean}>}
 */
async function signClinicalNoteCore(noteId, options = {}) {
    if (window.clinicalNoteSignModule) {
        return window.clinicalNoteSignModule.sign(noteId, options);
    }
    // Fallback if module not loaded
    console.warn('[GlobalBridge] ClinicalNoteSignModule not loaded, using simple sign');
    try {
        await apiRequest(`/clinical-notes/${noteId}/sign`, { method: 'POST', body: {} });
        return { success: true, careEpisodeCreated: false, careEpisodeUpdated: false };
    } catch (error) {
        showToast('Error', error.message || 'Failed to sign note', 'error');
        return { success: false, careEpisodeCreated: false, careEpisodeUpdated: false };
    }
}

// Note: validateInitialEvaluationNote, showIEMissingFieldsModal, showIEInsuranceMismatchModal,
// showIENoInsuranceModal, showCareEpisodeConfirmModal are now in ClinicalNoteSignModule.js

// Functions moved to ClinicalNoteSignModule.js:
// - validateInitialEvaluationNote
// - showIEMissingFieldsModal
// - showIEInsuranceMismatchModal
// - showIENoInsuranceModal
// - showCareEpisodeConfirmModal
// - showConfirmModal (generic)


/**
 * Simple sign function for clinical notes (uses signClinicalNoteCore)
 * @param {number} noteId - The note ID to sign
 * @returns {Promise<boolean>} Whether signing was successful
 */
async function signClinicalNoteSimple(noteId) {
    try {
        const result = await signClinicalNoteCore(noteId);
        return result.success;
    } catch (error) {
        console.error('[GlobalBridge] Sign clinical note error:', error);
        showToast('Error', error.message || 'Failed to sign clinical note', 'error');
        return false;
    }
}

/**
 * Refresh modules after note changes
 */
function refreshAfterNoteChange() {
    // Refresh clinical notes list if on that page
    if (window.clinicalNotesModule) {
        window.clinicalNotesModule.load?.();
    }
    // Refresh dashboard
    if (window.dashboardModule) {
        window.dashboardModule.loadAll?.();
    }
    // Refresh appointment notes if appointment detail modal was open
    const appointmentModule = window.App?.modules?.get('appointments') || window.appointmentModule;
    if (appointmentModule?.currentAppointment) {
        appointmentModule.loadAppointmentNotes?.(appointmentModule.currentAppointment.AppointmentId);
    }
}

function clearNoteFilters() {
    if (window.clinicalNotesModule) {
        window.clinicalNotesModule.clearFilters();
    }
}

// ============================================
// Care Episode Functions
// ============================================

/**
 * Get or create CareEpisodeModule instance
 */
async function getCareEpisodeModule() {
    let careEpisodeModule = window.App?.modules?.get('careEpisodes') || window.careEpisodeModule;

    if (!careEpisodeModule && window.CareEpisodeModule) {
        console.log('[GlobalBridge] CareEpisodeModule not initialized, creating instance...');
        window.careEpisodeModule = new CareEpisodeModule({
            api: window.apiService || (window.App && window.App.api),
            eventBus: window.eventBus || (window.App && window.App.events)
        });
        await window.careEpisodeModule.init();
        careEpisodeModule = window.careEpisodeModule;

        // Register with App if available
        if (window.App && window.App.modules && !window.App.modules.has('careEpisodes')) {
            window.App.modules.register('careEpisodes', window.careEpisodeModule);
        }
    }

    return careEpisodeModule;
}

/**
 * Open care episode modal for a patient
 * @param {number} patientId - Patient ID
 * @param {number|null} careEpisodeId - Care Episode ID for edit mode (optional)
 */
async function openCareEpisodeModal(patientId, careEpisodeId = null) {
    const careEpisodeModule = await getCareEpisodeModule();
    if (careEpisodeModule && careEpisodeModule.openModal) {
        await careEpisodeModule.openModal(patientId, careEpisodeId);
    } else {
        console.error('[GlobalBridge] CareEpisodeModule.openModal not available');
        showToast('Error', 'Unable to open care episode modal', 'error');
    }
}

/**
 * Save care episode (called from form submit)
 */
async function saveCareEpisode() {
    const careEpisodeModule = await getCareEpisodeModule();
    if (careEpisodeModule && careEpisodeModule.save) {
        await careEpisodeModule.save();
    } else {
        console.error('[GlobalBridge] CareEpisodeModule.save not available');
        showToast('Error', 'Unable to save care episode', 'error');
    }
}

function saveCareEpisodeAndSchedule() {
    if (window.careEpisodeModule) {
        window.careEpisodeModule.saveAndSchedule();
    }
}

function confirmCareEpisodeCreation() {
    // Delegate to ClinicalNoteSignModule if available (sign workflow)
    if (window.clinicalNoteSignModule) {
        window.clinicalNoteSignModule.confirmCareEpisode();
    } else if (window.careEpisodeModule) {
        // Legacy flow - delegate to careEpisodeModule
        window.careEpisodeModule.confirmCreation();
    }
}

function cancelCareEpisodeConfirmation() {
    // Delegate to ClinicalNoteSignModule if available (sign workflow)
    if (window.clinicalNoteSignModule) {
        window.clinicalNoteSignModule.cancelCareEpisode();
    } else {
        // Legacy flow - just close the modal
        const modal = document.getElementById('careEpisodeConfirmModal');
        if (modal) {
            const bsModal = bootstrap.Modal.getInstance(modal);
            if (bsModal) bsModal.hide();
        }
    }
}

function confirmCareEpisodeDischarge() {
    if (window.careEpisodeModule) {
        window.careEpisodeModule.confirmDischarge();
    }
}

function confirmCareEpisodeExtend() {
    if (window.careEpisodeModule) {
        window.careEpisodeModule.confirmExtend();
    }
}

// ============================================
// Patient Functions
// ============================================

function confirmDeletePatient() {
    if (window.patientModule) {
        window.patientModule.confirmDelete();
    }
}

function validatePrimaryInsurance() {
    if (window.patientModule) {
        window.patientModule.validateInsurance('primary');
    }
}

function validateSecondaryInsurance() {
    if (window.patientModule) {
        window.patientModule.validateInsurance('secondary');
    }
}

function refreshPrimaryAuthHistory() {
    if (window.patientModule) {
        window.patientModule.refreshAuthHistory('primary');
    }
}

function refreshSecondaryAuthHistory() {
    if (window.patientModule) {
        window.patientModule.refreshAuthHistory('secondary');
    }
}

function openAddAuthorizationModal(type, action) {
    if (window.patientModule) {
        window.patientModule.openAuthorizationModal(type, action);
    }
}

function saveAuthorization(event) {
    if (window.patientModule) {
        window.patientModule.saveAuthorization(event);
    }
}

function confirmDeleteAuthorization() {
    if (window.patientModule) {
        window.patientModule.confirmDeleteAuthorization();
    }
}

function fetchMockAuthorization(type) {
    if (window.patientModule) {
        window.patientModule.fetchMockAuthorization(type || 'primary');
    }
}

// ============================================
// Consent Functions
// ============================================

function refreshPatientConsentHistory() {
    if (window.consentModule) {
        window.consentModule.refreshHistory();
    }
}

function copyKioskUrl() {
    if (window.locationModule) {
        window.locationModule.copyKioskUrl();
    } else {
        const urlInput = document.getElementById('kioskUrlInput');
        if (urlInput) {
            navigator.clipboard.writeText(urlInput.value)
                .then(() => showToast('Success', 'Kiosk URL copied to clipboard'))
                .catch(() => showToast('Error', 'Failed to copy URL', 'error'));
        }
    }
}

function regenerateKioskToken() {
    if (window.locationModule) {
        window.locationModule.regenerateKioskToken();
    }
}

// ============================================
// Appointment Wizard Functions
// ============================================

function wizardNextStep() {
    const appointmentModule = window.App?.modules?.get('appointments') || window.appointmentModule;
    if (appointmentModule && appointmentModule.wizardNext) {
        appointmentModule.wizardNext();
    } else {
        console.error('[GlobalBridge] appointmentModule.wizardNext not available');
    }
}

function wizardPrevStep() {
    const appointmentModule = window.App?.modules?.get('appointments') || window.appointmentModule;
    if (appointmentModule && appointmentModule.wizardPrev) {
        appointmentModule.wizardPrev();
    } else {
        console.error('[GlobalBridge] appointmentModule.wizardPrev not available');
    }
}

function wizardGoToStep(step) {
    const appointmentModule = window.App?.modules?.get('appointments') || window.appointmentModule;
    if (appointmentModule && appointmentModule.wizardGoTo) {
        appointmentModule.wizardGoTo(step);
    } else {
        console.error('[GlobalBridge] appointmentModule.wizardGoTo not available');
    }
}

function clearWizardPatientSelection() {
    const appointmentModule = window.App?.modules?.get('appointments') || window.appointmentModule;
    if (appointmentModule && appointmentModule._clearPatientSelection) {
        appointmentModule._clearPatientSelection();
    } else {
        console.error('[GlobalBridge] appointmentModule._clearPatientSelection not available');
    }
}

function refreshAvailableSlots() {
    const appointmentModule = window.App?.modules?.get('appointments') || window.appointmentModule;
    if (appointmentModule && appointmentModule.refreshSlots) {
        appointmentModule.refreshSlots();
    } else {
        console.error('[GlobalBridge] appointmentModule.refreshSlots not available');
    }
}

function selectScheduleType(type) {
    const appointmentModule = window.App?.modules?.get('appointments') || window.appointmentModule;
    if (appointmentModule && appointmentModule.selectScheduleType) {
        appointmentModule.selectScheduleType(type);
    } else {
        console.error('[GlobalBridge] appointmentModule.selectScheduleType not available');
    }
}

function selectAppointmentType(type) {
    const appointmentModule = window.App?.modules?.get('appointments') || window.appointmentModule;
    if (appointmentModule && appointmentModule.selectType) {
        appointmentModule.selectType(type);
    } else {
        console.error('[GlobalBridge] appointmentModule.selectType not available');
    }
}

function confirmCancelAppointment() {
    const appointmentModule = window.App?.modules?.get('appointments') || window.appointmentModule;
    if (appointmentModule) {
        if (appointmentModule.confirmCancel) {
            appointmentModule.confirmCancel();
        } else {
            // Fallback: get values from modal and call cancel directly
            const appointmentId = document.getElementById('cancelAppointmentId')?.value;
            const reason = document.getElementById('cancellationReason')?.value?.trim();

            if (!appointmentId) {
                showToast('Error', 'No appointment selected', 'error');
                return;
            }

            if (!reason) {
                const reasonField = document.getElementById('cancellationReason');
                if (reasonField) reasonField.classList.add('is-invalid');
                showToast('Error', 'Please provide a cancellation reason', 'error');
                return;
            }

            appointmentModule.cancel(parseInt(appointmentId), reason);
        }
    } else {
        console.error('[GlobalBridge] appointmentModule not available for confirmCancelAppointment');
    }
}

function confirmReinstateAppointment() {
    const appointmentModule = window.App?.modules?.get('appointments') || window.appointmentModule;
    if (appointmentModule) {
        if (appointmentModule.confirmReinstate) {
            appointmentModule.confirmReinstate();
        } else {
            // Fallback: get value from modal and call reinstate directly
            const appointmentId = document.getElementById('reinstateAppointmentId')?.value;
            if (!appointmentId) {
                showToast('Error', 'No appointment selected', 'error');
                return;
            }
            appointmentModule.reinstate(parseInt(appointmentId));
        }
    } else {
        console.error('[GlobalBridge] appointmentModule not available for confirmReinstateAppointment');
    }
}

function checkInFromDetail() {
    const appointmentModule = window.App?.modules?.get('appointments') || window.appointmentModule;
    if (!appointmentModule) {
        console.error('[GlobalBridge] appointmentModule not available for check-in');
        return;
    }

    const currentAppt = appointmentModule.currentAppointment;
    if (!currentAppt || !currentAppt.AppointmentId) {
        console.error('[GlobalBridge] No current appointment for check-in');
        showToast('Error', 'No appointment selected', 'error');
        return;
    }

    // Check for copay from appointment or insurance
    const copayDue = currentAppt.CopayDue || currentAppt.CopayAmount || null;
    const hasCopay = copayDue && parseFloat(copayDue) > 0;

    // Skip copay modal if no copay — check in directly
    if (!hasCopay) {
        appointmentModule.checkIn(currentAppt.AppointmentId).then(() => {
            const dashboardModule = window.App?.modules?.get('dashboard') || window.dashboardModule;
            if (dashboardModule && dashboardModule.refresh) dashboardModule.refresh();
        });
        return;
    }

    const patientName = currentAppt.PatientName || '';
    const patientId = currentAppt.PatientId;
    _showCheckInCopayPrompt(patientName, copayDue, patientId, currentAppt.AppointmentId, appointmentModule);
}

function startVisitFromDetail() {
    const appointmentModule = window.App?.modules?.get('appointments') || window.appointmentModule;
    if (!appointmentModule) {
        console.error('[GlobalBridge] appointmentModule not available for start-visit');
        return;
    }

    const currentAppt = appointmentModule.currentAppointment;
    if (!currentAppt || !currentAppt.AppointmentId) {
        console.error('[GlobalBridge] No current appointment for start-visit');
        showToast('Error', 'No appointment selected', 'error');
        return;
    }

    // Close the detail modal before starting
    const detailModal = document.getElementById('appointmentDetailModal');
    if (detailModal) {
        const bsInstance = bootstrap.Modal.getInstance(detailModal);
        if (bsInstance) bsInstance.hide();
    }

    // If appointment is already InProgress, go directly to the encounter (Resume)
    if (currentAppt.Status === 3) {
        if (window._resumeEncounter) {
            window._resumeEncounter(currentAppt.AppointmentId, currentAppt.PatientId);
        } else {
            // Fallback: look up encounter and navigate
            apiRequest(`/patients/${currentAppt.PatientId}/encounters/by-appointment/${currentAppt.AppointmentId}`, { showLoader: false })
                .then(enc => {
                    if (enc && enc.EncounterId) window.location.href = `/Encounter/${enc.EncounterId}`;
                    else showToast('Error', 'Encounter not found', 'error');
                })
                .catch(() => showToast('Error', 'Failed to find encounter', 'error'));
        }
        return;
    }

    startVisitAppointment(currentAppt.AppointmentId);
}

function _showCheckInCopayPrompt(patientName, copayDue, patientId, appointmentId, appointmentModule) {
    // Remove existing prompt if any
    document.getElementById('checkInCopayPrompt')?.remove();

    const copayDisplay = copayDue ? `$${parseFloat(copayDue).toFixed(2)}` : 'N/A';
    const hasCopay = copayDue && parseFloat(copayDue) > 0;

    // Also check outstanding balance
    const balanceHtml = `<div id="checkInBalanceInfo" class="text-muted small mt-2" style="display:none;"></div>`;

    const promptHtml = `
    <div class="modal fade" id="checkInCopayPrompt" tabindex="-1" data-bs-backdrop="static" data-bs-keyboard="false">
        <div class="modal-dialog modal-dialog-centered">
            <div class="modal-content">
                <div class="modal-header">
                    <h5 class="modal-title"><i class="bi bi-box-arrow-in-right me-2"></i>Check In Patient</h5>
                    <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
                </div>
                <div class="modal-body">
                    <p class="mb-3 fw-semibold fs-5">${patientName}</p>
                    <div class="d-flex justify-content-between align-items-center p-3 rounded mb-2" style="background:#f0fdfa; border:1px solid #d1fae5;">
                        <span class="fw-semibold">Copay Due</span>
                        <span class="fw-bold ${hasCopay ? 'text-primary' : 'text-muted'}" style="font-size:24px;">${copayDisplay}</span>
                    </div>
                    ${balanceHtml}
                    ${hasCopay ? `
                    <hr>
                    <h6 class="fw-semibold mb-3">Collect Payment</h6>
                    <div class="row">
                        <div class="col-md-6 mb-3">
                            <label class="form-label fw-semibold">Amount</label>
                            <div class="input-group">
                                <span class="input-group-text">$</span>
                                <input type="number" class="form-control" id="checkInCopayAmount" value="${parseFloat(copayDue).toFixed(2)}" step="0.01" min="0">
                            </div>
                        </div>
                        <div class="col-md-6 mb-3">
                            <label class="form-label fw-semibold">Method</label>
                            <select class="form-select" id="checkInPaymentMethod">
                                <option value="0">Cash</option>
                                <option value="1">Check</option>
                                <option value="2">Credit Card</option>
                                <option value="3">Debit Card</option>
                            </select>
                        </div>
                    </div>
                    <!-- Check fields (shown when Check selected) -->
                    <div class="row" id="checkInCheckFields" style="display:none;">
                        <div class="col-md-6 mb-3">
                            <label class="form-label fw-semibold">Check Number <span class="text-danger">*</span></label>
                            <input type="text" class="form-control" id="checkInCheckNumber" placeholder="Check number">
                        </div>
                        <div class="col-md-6 mb-3">
                            <label class="form-label fw-semibold">Check Date</label>
                            <input type="date" class="form-control" id="checkInCheckDate">
                        </div>
                    </div>
                    <!-- Card fields (shown when Credit/Debit Card selected) -->
                    <div class="row" id="checkInCardFields" style="display:none;">
                        <div class="col-md-6 mb-3">
                            <label class="form-label fw-semibold">Card Last 4</label>
                            <input type="text" class="form-control" id="checkInCardReference" placeholder="e.g. 4242" maxlength="4">
                        </div>
                        <div class="col-md-6 mb-3">
                            <label class="form-label fw-semibold">Reference #</label>
                            <input type="text" class="form-control" id="checkInReferenceNumber" placeholder="POS reference">
                        </div>
                    </div>
                    <div class="mb-3">
                        <label class="form-label fw-semibold">Notes</label>
                        <input type="text" class="form-control" id="checkInCopayNotes" placeholder="Optional notes...">
                    </div>` : ''}
                </div>
                <div class="modal-footer">
                    <button type="button" class="btn btn-lg btn-outline-secondary" id="checkInSkipCopayBtn">
                        ${hasCopay ? 'Skip & Check In' : '<i class="bi bi-box-arrow-in-right me-1"></i>Check In'}
                    </button>
                    ${hasCopay ? `<button type="button" class="btn btn-lg btn-primary" id="checkInCollectCopayBtn">
                        <i class="bi bi-cash-coin me-1"></i>Collect & Check In
                    </button>` : ''}
                </div>
            </div>
        </div>
    </div>`;

    document.body.insertAdjacentHTML('beforeend', promptHtml);
    const promptModal = new bootstrap.Modal(document.getElementById('checkInCopayPrompt'));

    // Toggle conditional fields based on payment method
    document.getElementById('checkInPaymentMethod')?.addEventListener('change', function () {
        const checkFields = document.getElementById('checkInCheckFields');
        const cardFields = document.getElementById('checkInCardFields');
        if (checkFields) checkFields.style.display = this.value === '1' ? '' : 'none';
        if (cardFields) cardFields.style.display = (this.value === '2' || this.value === '3') ? '' : 'none';
    });

    // Close detail modal first
    const detailModal = document.getElementById('appointmentDetailModal');
    if (detailModal) {
        const detailInstance = bootstrap.Modal.getInstance(detailModal);
        if (detailInstance) detailInstance.hide();
    }

    setTimeout(() => promptModal.show(), 300);

    // Load outstanding balance in background
    if (patientId) {
        fetch(`/api/payments/patient/${patientId}/balance`, {
            headers: { 'Authorization': `Bearer ${localStorage.getItem('authToken')}` }
        }).then(r => r.ok ? r.json() : null).then(bal => {
            if (bal && bal.CurrentBalance > 0) {
                const el = document.getElementById('checkInBalanceInfo');
                if (el) {
                    el.innerHTML = `<i class="bi bi-exclamation-circle text-warning me-1"></i>Outstanding balance: <strong class="text-danger">$${bal.CurrentBalance.toFixed(2)}</strong>`;
                    el.style.display = 'block';
                }
            }
        }).catch(() => {});
    }

    // Skip & Check In
    document.getElementById('checkInSkipCopayBtn').addEventListener('click', () => {
        promptModal.hide();
        _proceedCheckIn(appointmentId, null, appointmentModule);
    });

    // Collect & Check In
    document.getElementById('checkInCollectCopayBtn')?.addEventListener('click', () => {
        const amount = parseFloat(document.getElementById('checkInCopayAmount')?.value || 0);
        const method = parseInt(document.getElementById('checkInPaymentMethod')?.value || 0);
        const checkNumber = document.getElementById('checkInCheckNumber')?.value || null;
        const checkDate = document.getElementById('checkInCheckDate')?.value || null;
        const cardReference = document.getElementById('checkInCardReference')?.value || null;
        const referenceNumber = document.getElementById('checkInReferenceNumber')?.value || null;
        const notes = document.getElementById('checkInCopayNotes')?.value || 'Copay collected at check-in';

        // Validate check number if check method
        if (method === 1 && !checkNumber) {
            showToast('Error', 'Check number is required.', 'error');
            return;
        }

        promptModal.hide();
        if (amount > 0) {
            const today = new Date().toISOString().split('T')[0];
            fetch('/api/payments', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'Authorization': `Bearer ${localStorage.getItem('authToken')}`
                },
                body: JSON.stringify({
                    PatientId: patientId,
                    AppointmentId: appointmentId,
                    Amount: amount,
                    Type: 0, // Copay
                    Method: method,
                    PaymentDate: today,
                    CheckNumber: method === 1 ? checkNumber : null,
                    CheckDate: method === 1 && checkDate ? checkDate : null,
                    CardReference: (method === 2 || method === 3) ? cardReference : null,
                    ReferenceNumber: referenceNumber || null,
                    Notes: notes
                })
            }).then(r => {
                if (r.ok) {
                    showToast('Payment', `Copay $${amount.toFixed(2)} collected`, 'success');
                    window.dispatchEvent(new CustomEvent('payment:created', { detail: { patientId, amount } }));
                }
            }).catch(() => {});
        }
        _proceedCheckIn(appointmentId, amount > 0 ? amount : null, appointmentModule);
    });

    // Cleanup on modal close
    document.getElementById('checkInCopayPrompt').addEventListener('hidden.bs.modal', () => {
        document.getElementById('checkInCopayPrompt')?.remove();
    });
}

function _proceedCheckIn(appointmentId, copayCollected, appointmentModule) {
    appointmentModule.checkIn(appointmentId, copayCollected).then(() => {
        // Refresh dashboard
        const dashboardModule = window.App?.modules?.get('dashboard') || window.dashboardModule;
        if (dashboardModule && dashboardModule.refresh) dashboardModule.refresh();

        // Refresh calendar
        const calendarModule = window.App?.modules?.get('calendar') || window.calendarModule;
        if (calendarModule && calendarModule.refresh) calendarModule.refresh();
    });
}

/**
 * Open appointment details modal (called when clicking calendar event)
 */
async function openAppointmentDetails(appointmentId, options = {}) {
    console.log('[GlobalBridge] openAppointmentDetails called with ID:', appointmentId);

    // Try multiple ways to get the appointment module
    let appointmentModule = window.App?.modules?.get('appointments') || window.appointmentModule;

    // If not found, try to create/initialize it
    if (!appointmentModule && window.AppointmentModule) {
        console.log('[GlobalBridge] AppointmentModule not initialized, creating instance...');
        window.appointmentModule = new AppointmentModule({
            api: window.apiService || (window.App && window.App.api),
            eventBus: window.eventBus || (window.App && window.App.events)
        });
        await window.appointmentModule.init();
        appointmentModule = window.appointmentModule;

        // Register with App if available
        if (window.App && window.App.modules && !window.App.modules.has('appointments')) {
            window.App.modules.register('appointments', window.appointmentModule);
        }
    }

    if (appointmentModule && appointmentModule.openDetails) {
        console.log('[GlobalBridge] Calling appointmentModule.openDetails...');
        await appointmentModule.openDetails(appointmentId, options);
    } else {
        console.error('[GlobalBridge] appointmentModule.openDetails not available');
        console.error('[GlobalBridge] appointmentModule:', appointmentModule);
        console.error('[GlobalBridge] window.appointmentModule:', window.appointmentModule);
        console.error('[GlobalBridge] window.AppointmentModule:', window.AppointmentModule);

        // Last resort fallback - try to fetch and show modal directly
        try {
            const token = localStorage.getItem('authToken');
            const response = await fetch(`/api/appointments/${appointmentId}`, {
                headers: {
                    'Content-Type': 'application/json',
                    'Authorization': token ? `Bearer ${token}` : ''
                }
            });

            if (response.ok) {
                const appt = await response.json();
                console.log('[GlobalBridge] Fallback: Loaded appointment:', appt);

                // Try to show the modal directly
                const modal = document.getElementById('appointmentDetailModal');
                if (modal && window.bootstrap) {
                    // Set basic info
                    const setEl = (id, text) => {
                        const el = document.getElementById(id);
                        if (el) el.textContent = text;
                    };
                    setEl('apptDetailProvider', appt.ProviderName || 'Provider');
                    setEl('apptDetailTime', `${new Date(appt.StartTime).toLocaleString()}`);
                    setEl('apptDetailType', `Type: ${getAppointmentTypeName(appt.Type)}`);
                    setEl('apptDetailReason', appt.Reason || '-');

                    const patientEl = document.getElementById('apptDetailPatient');
                    if (patientEl) {
                        patientEl.innerHTML = `<a href="#" onclick="viewPatient(${appt.PatientId}); return false;">${escapeHtml(appt.PatientName)}</a> <span class="text-muted">(${escapeHtml(appt.PatientMRN)})</span>`;
                    }

                    new bootstrap.Modal(modal).show();
                }
            }
        } catch (err) {
            console.error('[GlobalBridge] Fallback also failed:', err);
            showToast('Error', 'Unable to load appointment details', 'error');
        }
    }
}

/**
 * Edit appointment (called from detail modal)
 */
function editAppointment(appt) {
    const appointmentModule = window.App?.modules?.get('appointments') || window.appointmentModule;
    if (appointmentModule && appointmentModule.edit) {
        appointmentModule.edit(appt.AppointmentId || appt);
    } else {
        console.error('[GlobalBridge] appointmentModule.edit not available');
    }
}

/**
 * Open cancel appointment modal
 */
function openCancelAppointmentModal(appointmentId) {
    // LEGACY FIX: Close the appointment detail modal first (as in legacy code)
    const detailModal = document.getElementById('appointmentDetailModal');
    if (detailModal) {
        const detailInstance = bootstrap.Modal.getInstance(detailModal);
        if (detailInstance) {
            detailInstance.hide();
        }
    }

    // Set the appointment ID in the hidden field
    const idField = document.getElementById('cancelAppointmentId');
    if (idField) {
        idField.value = appointmentId;
    }
    // Clear previous reason and validation state
    const reasonField = document.getElementById('cancellationReason');
    if (reasonField) {
        reasonField.value = '';
        reasonField.classList.remove('is-invalid');
    }
    // Show the cancel modal
    const modal = document.getElementById('cancelAppointmentModal');
    if (modal) {
        const bsModal = new bootstrap.Modal(modal);
        bsModal.show();
    }
}

/**
 * Open reinstate appointment modal
 */
function openReinstateModal(appointmentId) {
    // Set the appointment ID in the hidden field
    const idField = document.getElementById('reinstateAppointmentId');
    if (idField) {
        idField.value = appointmentId;
    }
    // Show the reinstate modal
    const modal = document.getElementById('reinstateAppointmentModal');
    if (modal) {
        const bsModal = new bootstrap.Modal(modal);
        bsModal.show();
    }
}

/**
 * Check in appointment directly (from dashboard or calendar quick action)
 */
async function checkInAppointment(appointmentId) {
    const appointmentModule = window.App?.modules?.get('appointments') || window.appointmentModule;
    if (!appointmentModule || !appointmentModule.checkIn) {
        console.error('[GlobalBridge] appointmentModule.checkIn not available');
        return;
    }

    // Fetch appointment details to get patient info for copay prompt
    try {
        const appt = await apiRequest(`/appointments/${appointmentId}`);
        if (appt) {
            const copayDue = appt.CopayDue || null;
            const hasCopay = copayDue && parseFloat(copayDue) > 0;

            // Skip copay modal if no copay — check in directly
            if (!hasCopay) {
                await appointmentModule.checkIn(appointmentId);
                const dashboardModule = window.App?.modules?.get('dashboard') || window.dashboardModule;
                if (dashboardModule && dashboardModule.refresh) dashboardModule.refresh();
                const calendarModule = window.App?.modules?.get('calendar') || window.calendarModule;
                if (calendarModule && calendarModule.refresh) calendarModule.refresh();
                return;
            }

            const patientName = appt.PatientName || '';
            const patientId = appt.PatientId;
            _showCheckInCopayPrompt(patientName, copayDue, patientId, appointmentId, appointmentModule);
            return;
        }
    } catch (e) {
        console.error('[GlobalBridge] Failed to fetch appointment details:', e);
    }

    // Fallback: check in directly without copay prompt
    await appointmentModule.checkIn(appointmentId);
    const dashboardModule = window.App?.modules?.get('dashboard') || window.dashboardModule;
    if (dashboardModule && dashboardModule.refresh) dashboardModule.refresh();
    const calendarModule = window.App?.modules?.get('calendar') || window.calendarModule;
    if (calendarModule && calendarModule.refresh) calendarModule.refresh();
}

/**
 * Start visit for a checked-in appointment (transition to InProgress)
 */
async function startVisitAppointment(appointmentId) {
    try {
        const result = await apiRequest(`/appointments/${appointmentId}/start-visit`, { method: 'POST' });
        if (result) {
            // Look up encounter for this appointment, then navigate to Encounter Workspace
            const patientId = result.PatientId;
            if (patientId) {
                try {
                    const encounter = await apiRequest(`/patients/${patientId}/encounters/by-appointment/${appointmentId}`, { showLoader: false });
                    if (encounter && encounter.EncounterId) {
                        window.location.href = `/Encounter/${encounter.EncounterId}`;
                        return;
                    }
                } catch (encErr) {
                    console.warn('[GlobalBridge] Could not find encounter for appointment, falling back to dashboard refresh', encErr);
                }
            }

            // Fallback: refresh dashboard if encounter lookup fails
            const dashboardModule = window.App?.modules?.get('dashboard') || window.dashboardModule;
            if (dashboardModule?.refresh) dashboardModule.refresh();
            const calendarModule = window.App?.modules?.get('calendar') || window.calendarModule;
            if (calendarModule?.refresh) calendarModule.refresh();
        }
    } catch (error) {
        showToast('Error', 'Failed to start visit', 'error');
        console.error('[GlobalBridge] startVisitAppointment error:', error);
    }
}

/**
 * Check out a patient from their appointment (transition to Completed, encounter marked Signed)
 */
async function checkOutAppointment(appointmentId) {
    try {
        const result = await apiRequest(`/appointments/${appointmentId}/checkout`, { method: 'POST' });
        if (result) {
            const dashboardModule = window.App?.modules?.get('dashboard') || window.dashboardModule;
            if (dashboardModule?.refresh) dashboardModule.refresh();
            const calendarModule = window.App?.modules?.get('calendar') || window.calendarModule;
            if (calendarModule?.refresh) calendarModule.refresh();
        }
    } catch (error) {
        showToast('Error', 'Failed to check out patient', 'error');
        console.error('[GlobalBridge] checkOutAppointment error:', error);
    }
}

/**
 * View patient in modal (from appointment detail modal or dashboard)
 * Opens Patient Detail modal instead of navigating to patients page
 */
async function viewPatient(patientId) {
    const patientModule = window.App?.modules?.get('patients') || window.patientModule;

    if (patientModule && patientModule.view) {
        // Use PatientModule to open patient detail modal
        await patientModule.view(patientId);
    } else {
        // Fallback: render patient detail directly if module not available
        await viewPatientFallback(patientId);
    }
}

/**
 * Fallback patient view when PatientModule is not available
 * Renders patient detail directly in the modal
 */
async function viewPatientFallback(patientId) {
    try {
        const patient = await apiRequest(`/patients/${patientId}`);
        if (!patient) {
            showToast('Error', 'Patient not found', 'error');
            return;
        }

        const content = document.getElementById('patientDetailContent');
        if (!content) {
            console.error('[GlobalBridge] patientDetailContent not found');
            return;
        }

        // Format dates
        const dob = patient.DateOfBirth ? formatDate(patient.DateOfBirth) : '-';
        const age = patient.DateOfBirth ? calculateAge(patient.DateOfBirth) : '-';

        // Get primary insurance
        const primaryInsurance = patient.Insurances?.find(i => i.Type === 0);

        content.innerHTML = `
            <div class="patient-header mb-4">
                <div class="d-flex align-items-center gap-3">
                    ${AvatarUtils.renderPatientAvatar({ patientId: patient.PatientId, name: patient.FullName || `${patient.FirstName} ${patient.LastName}`, hasProfilePicture: patient.HasProfilePicture, size: 'lg' })}
                    <div>
                        <h4 class="mb-1">${escapeHtml(patient.FullName || `${patient.FirstName} ${patient.LastName}`)}</h4>
                        <div class="text-muted">
                            <span class="me-3"><i class="bi bi-hash me-1"></i>MRN: ${escapeHtml(patient.Mrn || '-')}</span>
                            <span class="me-3"><i class="bi bi-calendar me-1"></i>DOB: ${dob}</span>
                            <span><i class="bi bi-person me-1"></i>Age: ${age}</span>
                        </div>
                    </div>
                </div>
            </div>

            <div class="row g-3">
                <div class="col-md-6">
                    <div class="card h-100">
                        <div class="card-header bg-light">
                            <i class="bi bi-person-lines-fill me-2"></i>Contact Information
                        </div>
                        <div class="card-body">
                            <p class="mb-2"><strong>Phone:</strong> ${escapeHtml(patient.Phone || '-')}</p>
                            <p class="mb-2"><strong>Email:</strong> ${escapeHtml(patient.Email || '-')}</p>
                            <p class="mb-2"><strong>Address:</strong> ${escapeHtml(patient.Address || '-')}</p>
                            <p class="mb-0"><strong>City/State/ZIP:</strong> ${escapeHtml([patient.City, patient.State, patient.ZipCode].filter(Boolean).join(', ') || '-')}</p>
                        </div>
                    </div>
                </div>
                <div class="col-md-6">
                    <div class="card h-100">
                        <div class="card-header bg-light">
                            <i class="bi bi-shield-check me-2"></i>Insurance
                        </div>
                        <div class="card-body">
                            ${primaryInsurance ? `
                                <p class="mb-2"><strong>Payer:</strong> ${escapeHtml(primaryInsurance.PayerName || '-')}</p>
                                <p class="mb-2"><strong>Policy #:</strong> ${escapeHtml(primaryInsurance.PolicyNumber || '-')}</p>
                                <p class="mb-0"><strong>Group #:</strong> ${escapeHtml(primaryInsurance.GroupNumber || '-')}</p>
                            ` : '<p class="text-muted mb-0">No insurance on file</p>'}
                        </div>
                    </div>
                </div>
            </div>

            <div class="mt-3 d-flex gap-2">
                <button class="btn btn-outline-primary" onclick="navigateToPatient(${patientId})">
                    <i class="bi bi-box-arrow-up-right me-1"></i>View Full Profile
                </button>
                <button class="btn btn-outline-success" onclick="openNewAppointmentForPatient(${patientId}); bootstrap.Modal.getInstance(document.getElementById('patientDetailModal'))?.hide();">
                    <i class="bi bi-calendar-plus me-1"></i>Schedule Appointment
                </button>
            </div>
        `;

        // Show modal
        const modal = new bootstrap.Modal(document.getElementById('patientDetailModal'));
        modal.show();

    } catch (error) {
        console.error('[GlobalBridge] Failed to load patient:', error);
        showToast('Error', 'Failed to load patient details', 'error');
    }
}

/**
 * Calculate age from date of birth
 */
function calculateAge(dateOfBirth) {
    const dob = new Date(dateOfBirth);
    const today = new Date();
    let age = today.getFullYear() - dob.getFullYear();
    const monthDiff = today.getMonth() - dob.getMonth();
    if (monthDiff < 0 || (monthDiff === 0 && today.getDate() < dob.getDate())) {
        age--;
    }
    return age;
}

/**
 * Navigate to patient page (full profile view)
 */
function navigateToPatient(patientId) {
    // Close the modal first
    bootstrap.Modal.getInstance(document.getElementById('patientDetailModal'))?.hide();
    // Navigate to patients page
    window.location.href = `/Home/Patients?id=${patientId}`;
}

// ============================================
// Global Patient Search (Header Bar)
// ============================================

/**
 * Initialize the global patient search autocomplete in the header.
 * Uses the existing Autocomplete component and /api/patients/search endpoint.
 * Selecting a patient opens their detail modal via viewPatient().
 */
function initGlobalPatientSearch() {
    const input = document.getElementById('headerPatientSearchInput');
    const results = document.getElementById('headerPatientSearchResults');

    if (!input || !results) {
        return;
    }

    const headerSearch = new Autocomplete({
        inputId: 'headerPatientSearchInput',
        resultsId: 'headerPatientSearchResults',
        placeholder: 'Search patients...',
        minChars: 2,
        debounceMs: 250,
        maxResults: 8,
        emptyMessage: 'No patients found',
        loadingMessage: 'Searching...',
        itemClass: 'autocomplete-item',
        activeClass: 'active',

        onSearch: async (query) => {
            try {
                const response = await apiRequest(
                    `/patients/search?q=${encodeURIComponent(query)}&take=8&activeOnly=false`,
                    { showLoader: false, showErrors: false }
                );
                return response?.Results || [];
            } catch (error) {
                console.error('[GlobalSearch] Search failed:', error);
                return [];
            }
        },

        onSelect: (patient) => {
            headerSearch.clear();
            viewPatient(patient.PatientId);
        },

        renderItem: (patient) => {
            const statusBadge = PatientStatuses.getBadgeHtml(patient.Status);
            return `
                <div>
                    <div class="d-flex align-items-center">
                        <span class="header-search-result-name">${escapeHtml(patient.DisplayName || patient.FullName)}</span>
                        <span class="header-search-result-mrn">${escapeHtml(patient.MRN)}</span>
                        ${statusBadge}
                    </div>
                    <div class="header-search-result-details">
                        DOB: ${escapeHtml(patient.DOBFormatted)} (${patient.Age}y)
                        ${patient.PhoneLast4 ? ' &bull; Ph: ***' + escapeHtml(patient.PhoneLast4) : ''}
                        ${patient.PrimaryInsurance ? ' &bull; ' + escapeHtml(patient.PrimaryInsurance) : ''}
                    </div>
                </div>
            `;
        }
    });

    // Keyboard shortcut: Ctrl+K (or Cmd+K on Mac) to focus search
    document.addEventListener('keydown', (e) => {
        if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
            e.preventDefault();
            input.focus();
            input.select();
        }
    });

    // Escape key to blur when results are already closed
    input.addEventListener('keydown', (e) => {
        if (e.key === 'Escape' && !headerSearch.isOpen) {
            input.blur();
        }
    });

    window.headerPatientSearch = headerSearch;
}

// ============================================
// Clinical Notes Functions
// ============================================

/**
 * Open create clinical note modal
 */
async function openCreateClinicalNote(appt) {
    // Close appointment detail modal if open
    bootstrap.Modal.getInstance(document.getElementById('appointmentDetailModal'))?.hide();

    const modal = document.getElementById('clinicalNoteModal');
    if (!modal) return;

    // Reset form
    const form = document.getElementById('clinicalNoteForm');
    if (form) form.reset();

    // Reset clinical note ID for new note
    const clinicalNoteId = document.getElementById('clinicalNoteId');
    if (clinicalNoteId) clinicalNoteId.value = '';

    // Load templates filtered by location (no appointment type filter)
    const locationId = appt.LocationId;

    let templatesUrl = `/clinical-note-templates/for-location?activeOnly=true`;
    if (locationId) {
        templatesUrl += `&locationId=${locationId}`;
    }

    try {
        const templates = await apiRequest(templatesUrl);
        const templateSelect = document.getElementById('noteTemplateSelect');
        const templateFilterInfo = document.getElementById('templateFilterInfo');

        if (templateSelect) {
            templateSelect.disabled = false;
            templateSelect.innerHTML = '<option value="">Select Template</option>' +
                (templates?.map(t => `<option value="${t.TemplateId}">${escapeHtml(t.Name)}</option>`).join('') || '');

            // Auto-select if only one template available
            if (templates?.length === 1) {
                templateSelect.value = templates[0].TemplateId;
                // Trigger template content load
                await loadClinicalNoteTemplateContent(templates[0].TemplateId);
            }
        }

        // Update filter info text
        if (templateFilterInfo) {
            templateFilterInfo.textContent = `Showing ${templates?.length || 0} available template(s)`;
        }
    } catch (error) {
        console.error('[GlobalBridge] Error loading templates:', error);
    }

    // Set appointment context
    const noteAppointmentId = document.getElementById('noteAppointmentId');
    const notePatientId = document.getElementById('notePatientId');
    const noteProviderId = document.getElementById('noteProviderId');
    const notePatientInfo = document.getElementById('notePatientInfo');
    const noteProviderInfo = document.getElementById('noteProviderInfo');
    const noteServiceDate = document.getElementById('noteServiceDate');

    if (noteAppointmentId) noteAppointmentId.value = appt.AppointmentId;
    if (notePatientId) notePatientId.value = appt.PatientId;
    if (noteProviderId) noteProviderId.value = appt.ProviderId;
    if (notePatientInfo) notePatientInfo.textContent = appt.PatientName || 'Patient';
    if (noteProviderInfo) noteProviderInfo.textContent = appt.ProviderName || 'Provider';

    // Set service date from appointment (use timezone-converted date, not UTC extraction)
    if (noteServiceDate) {
        let serviceDate;
        if (appt.DateFormatted && appt.StartTime) {
            // Convert appointment StartTime using location timezone to get the correct local date
            const tz = appt.TimeZoneId || (window.getCurrentLocationTimezone ? window.getCurrentLocationTimezone()?.timeZoneId : null);
            if (tz && appt.StartTime) {
                try {
                    const d = new Date(appt.StartTime);
                    const parts = d.toLocaleDateString('en-CA', { timeZone: tz }); // en-CA gives YYYY-MM-DD
                    serviceDate = parts;
                } catch { /* fall through */ }
            }
        }
        if (!serviceDate && appt.StartTime) {
            serviceDate = appt.StartTime.split('T')[0];
        }
        if (!serviceDate) {
            serviceDate = new Date().toISOString().split('T')[0];
        }
        noteServiceDate.value = serviceDate;
    }

    // Update modal title
    const modalTitle = modal.querySelector('.modal-title');
    if (modalTitle) modalTitle.textContent = 'Create Clinical Note';

    // Hide Delete button for new notes
    const deleteBtn = document.getElementById('deleteNoteFromEditBtn');
    if (deleteBtn) deleteBtn.classList.add('d-none');

    // Initialize Trumbowyg editor
    initClinicalNoteTrumbowyg();

    // Check if there's a last signed note available for duplicate feature
    // Only show duplicate option for Follow-Up appointments (Type = 1)
    if ((appt.Type || 0) === 1) {
        await checkAndShowDuplicateOption(appt.PatientId, appt.ProviderId);
    }

    // Show modal
    const bsModal = new bootstrap.Modal(modal);
    bsModal.show();
}

/**
 * Initialize Trumbowyg editor for clinical note modal
 */
function initClinicalNoteTrumbowyg() {
    const editorEl = document.getElementById('noteHtmlContent');
    if (!editorEl) return;

    // Destroy existing instance if any
    if (typeof $.fn.trumbowyg !== 'undefined' && $('#noteHtmlContent').data('trumbowyg')) {
        $('#noteHtmlContent').trumbowyg('destroy');
    }

    // Initialize Trumbowyg if available
    if (typeof $.fn.trumbowyg !== 'undefined') {
        $('#noteHtmlContent').trumbowyg({
            btns: [
                ['viewHTML'],
                ['undo', 'redo'],
                ['formatting'],
                ['strong', 'em', 'del'],
                ['superscript', 'subscript'],
                ['justifyLeft', 'justifyCenter', 'justifyRight', 'justifyFull'],
                ['unorderedList', 'orderedList'],
                ['horizontalRule'],
                ['removeformat'],
                ['fullscreen']
            ],
            autogrow: true,
            semantic: true,
            removeformatPasted: true
        });
    }
}

/**
 * Load template content into the clinical note editor.
 * If encounter context is available (vitals, CC/HPI, history entered by MA/Nurse),
 * uses Gemini AI to intelligently pre-fill the template with that data.
 * Falls back to raw template if prefill fails or no encounter data exists.
 */
async function loadClinicalNoteTemplateContent(templateId) {
    if (!templateId) return;

    const patientId = document.getElementById('notePatientId')?.value;
    const appointmentId = document.getElementById('noteAppointmentId')?.value;
    const editorContainer = document.querySelector('.trumbowyg-box') || document.getElementById('noteHtmlContent')?.parentElement;

    // Show loading overlay on the editor area
    let prefillOverlay = null;
    if (editorContainer) {
        prefillOverlay = document.createElement('div');
        prefillOverlay.id = 'prefillLoadingOverlay';
        prefillOverlay.style.cssText = 'position:absolute;top:0;left:0;right:0;bottom:0;background:rgba(255,255,255,0.85);display:flex;align-items:center;justify-content:center;z-index:10;border-radius:4px;';
        prefillOverlay.innerHTML = `
            <div class="text-center">
                <div class="spinner-border spinner-border-sm text-primary me-2" role="status"></div>
                <span class="text-muted">Pre-filling with encounter data...</span>
            </div>`;
        editorContainer.style.position = 'relative';
        editorContainer.appendChild(prefillOverlay);
    }

    try {
        // Attempt AI-powered prefill if we have patient context
        if (patientId) {
            try {
                const prefillResponse = await apiRequest('/clinical-notes/prefill-template', {
                    method: 'POST',
                    body: {
                        TemplateId: parseInt(templateId),
                        PatientId: parseInt(patientId),
                        AppointmentId: appointmentId ? parseInt(appointmentId) : null
                    },
                    showLoader: false
                });

                if (prefillResponse && prefillResponse.Success && prefillResponse.PrefilledHtml) {
                    // Set pre-filled content in editor
                    setClinicalNoteEditorContent(prefillResponse.PrefilledHtml);

                    return; // Success — done
                }
            } catch (prefillError) {
                console.warn('[GlobalBridge] Template prefill failed, falling back to raw template:', prefillError);
            }
        }

        // Fallback: Load raw template (original behavior)
        const template = await apiRequest(`/clinical-note-templates/${templateId}`);
        if (template && template.HtmlContent) {
            setClinicalNoteEditorContent(template.HtmlContent);
        }
    } catch (error) {
        console.error('[GlobalBridge] Load template error:', error);
    } finally {
        // Remove loading overlay
        if (prefillOverlay && prefillOverlay.parentElement) {
            prefillOverlay.remove();
        }
    }
}

/**
 * Set HTML content in the clinical note editor (Trumbowyg or fallback textarea).
 */
function setClinicalNoteEditorContent(htmlContent) {
    if (typeof $.fn.trumbowyg !== 'undefined' && $('#noteHtmlContent').data('trumbowyg')) {
        $('#noteHtmlContent').trumbowyg('html', htmlContent);
    } else {
        const editorEl = document.getElementById('noteHtmlContent');
        if (editorEl) editorEl.value = htmlContent;
    }
}

// ============================================
// Duplicate Note Functions
// ============================================

// Store the last signed note data for duplicate feature
let _lastSignedNoteData = null;

/**
 * Check if there's a last signed note available and show the duplicate option
 * Only checks for Follow-Up (Daily Progress Note) appointment notes.
 * @param {number} patientId - Patient ID
 * @param {number} providerId - Provider ID
 */
async function checkAndShowDuplicateOption(patientId, providerId) {
    const duplicateSection = document.getElementById('duplicateNoteSection');
    if (!duplicateSection) return;

    // Hide by default
    duplicateSection.classList.add('d-none');
    _lastSignedNoteData = null;

    if (!patientId || !providerId) return;

    try {
        // Check if there's a last signed Follow-Up note (silent API call without loader)
        // appointmentType=1 ensures we only get notes from Follow-Up appointments (Daily Progress Notes)
        const response = await fetch(`/api/clinical-notes/last-signed?patientId=${patientId}&providerId=${providerId}&appointmentType=1`, {
            headers: {
                'Authorization': `Bearer ${localStorage.getItem('authToken')}`
            }
        });

        if (response.ok) {
            const note = await response.json();
            if (note && note.HtmlContent) {
                // Store for later use
                _lastSignedNoteData = note;
                // Show the duplicate section
                duplicateSection.classList.remove('d-none');
            }
        }
    } catch (error) {
        // Silently fail - duplicate feature is optional
        console.log('[GlobalBridge] No previous signed note found for duplicate');
    }
}

/**
 * Duplicate content from the last signed note into the current editor
 */
async function duplicateFromLastNote() {
    if (!_lastSignedNoteData || !_lastSignedNoteData.HtmlContent) {
        showToast('Info', 'No previous signed note available to duplicate', 'info');
        return;
    }

    // Get the note content, excluding signature-related fields
    let content = _lastSignedNoteData.HtmlContent;

    // Remove any existing provider signature (replace placeholder or embedded signature)
    content = content.replace(/\{\{provider_signature\}\}/gi, '{{provider_signature}}'); // Keep placeholder
    content = content.replace(/<img[^>]*alt="Provider Signature"[^>]*>/gi, '{{provider_signature}}'); // Replace embedded signature with placeholder

    // Set content in editor
    if (typeof $.fn.trumbowyg !== 'undefined' && $('#noteHtmlContent').data('trumbowyg')) {
        $('#noteHtmlContent').trumbowyg('html', content);
    } else {
        const editorEl = document.getElementById('noteHtmlContent');
        if (editorEl) editorEl.value = content;
    }

    // Hide the duplicate section after use
    const duplicateSection = document.getElementById('duplicateNoteSection');
    if (duplicateSection) {
        duplicateSection.classList.add('d-none');
    }

}

/**
 * Edit clinical note — fetches note data and opens the editor modal directly.
 * Works on any page (Encounter Workspace, Clinical Notes sidebar, etc.)
 */
async function editClinicalNote(noteId) {
    try {
        const note = await apiRequest(`/clinical-notes/${noteId}`);
        if (!note) {
            showToast('Error', 'Note not found', 'error');
            return;
        }

        const modal = document.getElementById('clinicalNoteModal');
        if (!modal) {
            console.warn('[GlobalBridge] clinicalNoteModal not found');
            return;
        }

        // Set note ID for update
        const noteIdEl = document.getElementById('clinicalNoteId');
        if (noteIdEl) noteIdEl.value = note.ClinicalNoteId;

        // Set appointment context
        const noteAppointmentId = document.getElementById('noteAppointmentId');
        const notePatientId = document.getElementById('notePatientId');
        const noteProviderId = document.getElementById('noteProviderId');
        const notePatientInfo = document.getElementById('notePatientInfo');
        const noteProviderInfo = document.getElementById('noteProviderInfo');
        const noteServiceDate = document.getElementById('noteServiceDate');

        if (noteAppointmentId) noteAppointmentId.value = note.AppointmentId || '';
        if (notePatientId) notePatientId.value = note.PatientId || '';
        if (noteProviderId) noteProviderId.value = note.ProviderId || '';
        if (notePatientInfo) notePatientInfo.textContent = note.PatientName || 'Patient';
        if (noteProviderInfo) noteProviderInfo.textContent = note.ProviderName || 'Provider';
        if (noteServiceDate) noteServiceDate.value = note.ServiceDate ? note.ServiceDate.split('T')[0] : '';

        // Set template (disable since editing existing note)
        const templateSelect = document.getElementById('noteTemplateSelect');
        if (templateSelect) {
            templateSelect.innerHTML = `<option value="${note.TemplateId || ''}">${escapeHtml(note.TemplateName || 'Clinical Note')}</option>`;
            templateSelect.disabled = true;
        }

        // Update modal title
        const modalTitle = modal.querySelector('.modal-title');
        if (modalTitle) modalTitle.textContent = 'Edit Clinical Note';

        // Show Delete button for existing notes and bind click handler
        const deleteBtn = document.getElementById('deleteNoteFromEditBtn');
        if (deleteBtn) {
            deleteBtn.classList.remove('d-none');
            deleteBtn.onclick = () => {
                const noteTitle = note.TemplateName || note.TypeName || 'Clinical Note';
                deleteClinicalNote(note.ClinicalNoteId, noteTitle);
            };
        }

        // Initialize editor and set content (sanitized — strips <script>, event
        // handlers, iframes; preserves clinical formatting tags).
        initClinicalNoteTrumbowyg();
        setTimeout(() => {
            const editorEl = document.getElementById('noteHtmlContent');
            const safeHtml = (window.sanitizeClinicalHtml || ((s) => s))(note.HtmlContent || '');
            if (editorEl && typeof $.fn.trumbowyg !== 'undefined') {
                $('#noteHtmlContent').trumbowyg('html', safeHtml);
            } else if (editorEl) {
                editorEl.innerHTML = safeHtml;
            }
        }, 200);

        // Show modal
        const bsModal = new bootstrap.Modal(modal);
        bsModal.show();
    } catch (error) {
        console.error('[GlobalBridge] Edit clinical note error:', error);
        showToast('Error', 'Failed to load clinical note for editing', 'error');
    }
}

/**
 * View clinical note
 * Opens the note in VIEW mode - if the note has an appointment, uses tabbed interface
 * which includes Edit/Sign buttons. Otherwise falls back to simple view modal.
 */
async function viewClinicalNote(noteId) {
    try {
        // Close dashboard modals if open (Missing Notes, Missing Signature, etc.)
        // These modals open note views via buttons, and we need to close them first
        const dashboardModalIds = [
            'missingNotesModal',
            'missingSignatureModal',
            'requireScheduleModal',
            'noShowModal',
            'missedAppointmentsModal'
        ];

        for (const modalId of dashboardModalIds) {
            const modalEl = document.getElementById(modalId);
            if (modalEl && modalEl.classList.contains('show')) {
                bootstrap.Modal.getInstance(modalEl)?.hide();
                // Wait for modal to close
                await new Promise(resolve => setTimeout(resolve, 300));
                break; // Only one dashboard modal should be open at a time
            }
        }

        // First get the note to find its appointment
        const note = await apiRequest(`/clinical-notes/${noteId}`);
        if (!note) {
            showToast('Error', 'Note not found', 'error');
            return;
        }

        if (note.AppointmentId) {
            // Has appointment - use tabbed interface in VIEW mode
            await openTabbedClinicalNotes(note.AppointmentId, noteId, { viewMode: true });
        } else {
            // Standalone note (no appointment) — not expected in current workflow.
            // The legacy single-note modal has been removed; log and toast.
            console.warn('[GlobalBridge] Note has no appointment; single-note view is deprecated.', note);
            showToast('Info', 'This note is not linked to an encounter.', 'warning');
        }
    } catch (error) {
        console.error('[GlobalBridge] View clinical note error:', error);
        showToast('Error', 'Failed to load clinical note', 'error');
    }
}


/**
 * Delete clinical note
 */
async function deleteClinicalNote(noteId, title) {
    if (window.clinicalNotesModule && window.clinicalNotesModule.delete) {
        window.clinicalNotesModule.delete(noteId, title);
    } else {
        // Fallback: delete directly via API (for encounter workspace context)
        if (!window.ConfirmDialog) {
            if (!confirm(`Delete "${title}"? This cannot be undone.`)) return;
        } else {
            const confirmed = await ConfirmDialog.show({
                title: 'Delete Clinical Note',
                message: `Are you sure you want to delete "${title}"? This action cannot be undone.`,
                confirmText: 'Delete',
                cancelText: 'Cancel',
                confirmClass: 'btn-danger'
            });
            if (!confirmed) return;
        }

        try {
            await apiRequest(`/clinical-notes/${noteId}`, { method: 'DELETE', showLoader: false });
            // Close the edit modal
            const modal = document.getElementById('clinicalNoteModal');
            if (modal) {
                const bsModal = bootstrap.Modal.getInstance(modal);
                if (bsModal) bsModal.hide();
            }
            if (typeof showToast === 'function') showToast('Success', 'Clinical note deleted', 'success');
            // Refresh encounter note step if in workspace
            if (window._encounterWorkspace) {
                setTimeout(() => window._encounterWorkspace._renderNoteStep(), 500);
            }
        } catch (err) {
            if (typeof showToast === 'function') showToast('Error', 'Failed to delete note', 'error');
        }
    }
}

/**
 * State for tabbed clinical notes modal
 */
let tabbedNotesState = {
    appointmentId: null,
    appointment: null,
    notes: [],
    activeNoteId: null,
    editors: {}, // Store editor instances per tab
    viewMode: false // true = read-only VIEW mode, false = EDIT mode
};

/**
 * Open tabbed clinical notes view
 * Shows multiple notes for an appointment in a tabbed interface
 * options.viewMode: true for read-only view, false for edit mode
 */
async function openTabbedClinicalNotes(appointmentId, initialNoteId = null, options = {}) {
    try {
        // Close any open modals first
        const appointmentModal = document.getElementById('appointmentDetailModal');
        if (appointmentModal && appointmentModal.classList.contains('show')) {
            window._cameFromAppointmentModal = true;
            bootstrap.Modal.getInstance(appointmentModal)?.hide();
        }

        // (Legacy viewClinicalNoteModal close call removed — modal no longer exists.)

        // Fetch appointment details if not provided
        let appointment = options.appointment;
        if (!appointment) {
            appointment = await apiRequest(`/appointments/${appointmentId}`);
        }

        // Fetch all notes for this appointment
        const notes = await apiRequest(`/clinical-notes/by-appointment/${appointmentId}`);

        if (!notes || notes.length === 0) {
            showToast('Info', 'No clinical notes found for this appointment', 'info');
            return;
        }

        // Store state including viewMode
        tabbedNotesState = {
            appointmentId,
            appointment,
            notes: notes,
            activeNoteId: initialNoteId || notes[0].ClinicalNoteId,
            editors: {},
            viewMode: options.viewMode === true
        };

        // Check if tabbed modal exists (defensive — should always exist)
        const tabbedModal = document.getElementById('tabbedClinicalNoteModal');
        if (!tabbedModal) {
            console.warn('[GlobalBridge] tabbedClinicalNoteModal not found — cannot render note view.');
            if (typeof showToast === 'function') showToast('Error', 'Clinical note viewer unavailable.', 'error');
            return;
        }

        // Update modal title based on mode
        const modalTitle = document.getElementById('tabbedNoteModalTitle');
        if (modalTitle) {
            modalTitle.textContent = tabbedNotesState.viewMode ? 'View Clinical Notes' : 'Clinical Notes';
        }

        // Update header info
        const tabbedNotePatient = document.getElementById('tabbedNotePatient');
        const tabbedNoteProvider = document.getElementById('tabbedNoteProvider');
        const tabbedNoteDate = document.getElementById('tabbedNoteDate');

        if (tabbedNotePatient) {
            tabbedNotePatient.textContent = `${appointment.PatientName || 'Unknown'} (${appointment.PatientMRN || 'N/A'})`;
        }
        if (tabbedNoteProvider) {
            tabbedNoteProvider.textContent = appointment.ProviderName || 'Unknown';
        }
        if (tabbedNoteDate) {
            tabbedNoteDate.textContent = formatDate(appointment.StartTime?.split('T')[0] || notes[0].ServiceDate);
        }

        // Build tabs and tab content
        buildNoteTabs();

        // Wait a short moment for DOM to update, then activate the initial tab
        await new Promise(resolve => setTimeout(resolve, 100));
        activateNoteTab(tabbedNotesState.activeNoteId);

        // Show modal
        const modal = new bootstrap.Modal(tabbedModal);
        modal.show();

    } catch (error) {
        console.error('[GlobalBridge] Error opening tabbed clinical notes:', error);
        showToast('Error', 'Failed to load clinical notes', 'error');
    }
}

/**
 * Build the tabs for all notes
 */
function buildNoteTabs() {
    const tabsContainer = document.getElementById('clinicalNoteTabs');
    const contentContainer = document.getElementById('clinicalNoteTabContent');

    if (!tabsContainer || !contentContainer) {
        console.error('[GlobalBridge] Tab containers not found');
        return;
    }

    // Clear existing tabs and content
    tabsContainer.innerHTML = '';
    contentContainer.innerHTML = '';

    tabbedNotesState.notes.forEach((note, index) => {
        const isActive = note.ClinicalNoteId === tabbedNotesState.activeNoteId;
        const isSigned = note.Status === 2 || note.Status === 4;
        const statusIcon = isSigned ? '<i class="bi bi-check-circle-fill text-success me-1"></i>' : '<i class="bi bi-pencil-fill text-warning me-1"></i>';
        const noteName = note.TemplateName || note.TypeName || `Note ${index + 1}`;

        // Create tab button
        const tabLi = document.createElement('li');
        tabLi.className = 'nav-item';
        tabLi.setAttribute('role', 'presentation');
        tabLi.innerHTML = `
            <button class="nav-link ${isActive ? 'active' : ''}"
                    id="note-tab-${note.ClinicalNoteId}"
                    data-note-id="${note.ClinicalNoteId}"
                    type="button"
                    role="tab"
                    aria-controls="note-pane-${note.ClinicalNoteId}"
                    aria-selected="${isActive}"
                    onclick="activateNoteTab(${note.ClinicalNoteId})">
                ${statusIcon}${escapeHtml(noteName)}
            </button>
        `;
        tabsContainer.appendChild(tabLi);

        // Create tab pane
        const tabPane = document.createElement('div');
        tabPane.className = `tab-pane fade ${isActive ? 'show active' : ''}`;
        tabPane.id = `note-pane-${note.ClinicalNoteId}`;
        tabPane.setAttribute('role', 'tabpanel');
        tabPane.setAttribute('aria-labelledby', `note-tab-${note.ClinicalNoteId}`);
        tabPane.setAttribute('data-note-id', note.ClinicalNoteId);
        contentContainer.appendChild(tabPane);
    });
}

/**
 * Activate a specific note tab
 */
async function activateNoteTab(noteId) {
    const note = tabbedNotesState.notes.find(n => n.ClinicalNoteId === noteId);
    if (!note) return;

    tabbedNotesState.activeNoteId = noteId;

    // Update tab active states
    document.querySelectorAll('#clinicalNoteTabs .nav-link').forEach(tab => {
        const tabNoteId = parseInt(tab.getAttribute('data-note-id'));
        if (tabNoteId === noteId) {
            tab.classList.add('active');
            tab.setAttribute('aria-selected', 'true');
        } else {
            tab.classList.remove('active');
            tab.setAttribute('aria-selected', 'false');
        }
    });

    // Update tab pane visibility
    document.querySelectorAll('#clinicalNoteTabContent .tab-pane').forEach(pane => {
        const paneNoteId = parseInt(pane.getAttribute('data-note-id'));
        if (paneNoteId === noteId) {
            pane.classList.add('show', 'active');
        } else {
            pane.classList.remove('show', 'active');
        }
    });

    // Fetch full note details
    const fullNote = await apiRequest(`/clinical-notes/${noteId}`);
    if (!fullNote) return;

    // Update the note in our state with full details
    const noteIndex = tabbedNotesState.notes.findIndex(n => n.ClinicalNoteId === noteId);
    if (noteIndex >= 0) {
        tabbedNotesState.notes[noteIndex] = fullNote;
    }

    // Render the tab content
    renderNoteTabContent(fullNote);
}

/**
 * Set up the side-by-side note view for a signed note inside the tabbed modal.
 *
 * Renders the activity timeline on the right, the version bar on the left, the
 * current version's content in the note pane, and the addendum blocks appended
 * below. Wires the interactions:
 *
 *   - Click a VERSION card on the right  → swap left pane to that version.
 *     Green "Viewing current" bar becomes yellow "Viewing historical" with
 *     a "Return to current" link. Timeline re-renders with the active card
 *     indicator moved. When viewing historical, addendums are NOT shown —
 *     they belong to the current record.
 *
 *   - Click an ADDENDUM card on the right → scroll left pane down to that
 *     addendum block + yellow highlight flash. If currently viewing historical,
 *     first swap back to current (so the addendums actually exist on the left).
 *
 * Interactions live entirely on the client. Version content is fetched on
 * demand via /api/clinical-notes/{id}/version/{n} (only for non-current
 * versions — the current content comes in the history response).
 */
async function setupSideBySideNoteView(tabPane, note, history) {
    const noteId = note.ClinicalNoteId;
    const A = window.AmendmentAddendum;
    if (!A) return;

    const leftPane = tabPane.querySelector('.note-pane');
    const timelinePane = tabPane.querySelector(`#viewTimelinePane-${noteId}`);
    const versionBarHost = tabPane.querySelector(`#viewVersionBar-${noteId}`);
    const contentHost = tabPane.querySelector(`#viewNoteContentHost-${noteId}`);
    if (!leftPane || !timelinePane || !versionBarHost || !contentHost) return;

    // Keep the class attribute stable — Print/PDF + Word selectors key off it.
    if (!contentHost.classList.contains('note-content-view')) {
        contentHost.classList.add('note-content-view');
    }

    const versions = history.Versions || history.versions || [];
    const addendums = history.Addendums || history.addendums || [];
    const currentVN = (() => {
        // Current version number = highest VersionNumber that has IsCurrent=true,
        // fallback to the max versionNumber.
        for (const v of versions) {
            if (v.IsCurrent || v.isCurrent) return v.VersionNumber || v.versionNumber;
        }
        const nums = versions.map(v => v.VersionNumber || v.versionNumber).filter(n => typeof n === 'number');
        return nums.length ? Math.max(...nums) : 1;
    })();

    // Per-note state object — lets the closure-free handlers know what's going on.
    const state = {
        noteId,
        viewingVN: currentVN,    // what the left pane is currently rendering
        currentVN,
        history
    };

    const esc = (t) => (t ?? '').toString().replace(/[&<>]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;'}[c]));

    // Helper: find the version metadata for a given VN
    const versionMeta = (vn) => versions.find(v => (v.VersionNumber || v.versionNumber) === vn);

    // Helper: render the version-bar above the note content
    function renderVersionBar() {
        const vn = state.viewingVN;
        const meta = versionMeta(vn);
        if (!meta) { versionBarHost.innerHTML = ''; return; }
        const isOrig = meta.IsOriginal || meta.isOriginal;
        const isHistorical = vn !== state.currentVN;
        const signedAt = meta.SignedAt || meta.signedAt;
        const signedByName = meta.SignedByName || meta.signedByName || '';
        const versionLabel = isOrig ? `v${vn} · Original` : `v${vn} · Amendment`;
        const prettyDate = signedAt ? new Date(signedAt).toLocaleString([], { month: '2-digit', day: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' }) : '';

        if (isHistorical) {
            versionBarHost.innerHTML = `
                <div class="version-bar historical">
                    <span class="label"><i class="bi bi-clock-history me-1"></i>Viewing historical</span>
                    <span>${esc(versionLabel)}</span>
                    <span class="vb-meta">Signed ${esc(prettyDate)} · ${esc(signedByName)}</span>
                    <button type="button" class="return-link" id="returnToCurrentBtn-${noteId}">
                        <i class="bi bi-arrow-left-circle me-1"></i>Return to current (v${state.currentVN})
                    </button>
                </div>`;
            const btn = versionBarHost.querySelector(`#returnToCurrentBtn-${noteId}`);
            if (btn) btn.addEventListener('click', () => swapToVersion(state.currentVN));
        } else {
            versionBarHost.innerHTML = `
                <div class="version-bar current">
                    <span class="label"><i class="bi bi-check-circle-fill me-1"></i>Viewing current</span>
                    <span>${esc(versionLabel)}</span>
                    <span class="vb-meta">Signed ${esc(prettyDate)} · ${esc(signedByName)}</span>
                </div>`;
        }
    }

    // Helper: render the addendum blocks on the left (below note content).
    // Only called when viewing current. Historical views skip this.
    function appendAddendumsToLeft() {
        // Remove any existing blocks first (in case we're re-rendering)
        contentHost.querySelectorAll(':scope > .note-addendum-block').forEach(n => n.remove());
        if (!addendums.length) return;
        addendums.forEach(a => {
            const id = a.AddendumId || a.addendumId;
            const when = new Date(a.SignedAt || a.signedAt || a.CreatedAt || a.createdAt).toLocaleString();
            const by = a.SignedByName || a.signedByName || '';
            const reason = a.Reason || a.reason || '';
            const content = a.Content || a.content || '';
            const div = document.createElement('div');
            div.className = 'note-addendum-block';
            div.id = `addendum-block-${id}`;
            div.innerHTML = `
                <div class="addendum-header">
                    <span class="badge bg-primary"><i class="bi bi-plus-circle me-1"></i>Addendum</span>
                    <span class="text-muted">${esc(when)}</span>
                    <span class="text-muted">by ${esc(by)}</span>
                </div>
                <div class="addendum-reason"><strong>Reason:</strong> ${esc(reason)}</div>
                <div class="addendum-content">${content}</div>`;
            contentHost.appendChild(div);
        });
    }

    // Helper: render the timeline with current active-highlight state
    function renderTimeline() {
        A.renderTimelinePanel(timelinePane, history, {
            currentVersionNumber: state.currentVN,
            viewingVersionNumber: state.viewingVN,
            onVersionClick: (vn) => swapToVersion(vn),
            onAddendumClick: (addendumId) => jumpToAddendum(addendumId)
        });
    }

    // Swap the left pane to a given version. Loads content from history (current)
    // or /version/{n} (historical) and updates the version bar + timeline.
    async function swapToVersion(vn) {
        state.viewingVN = vn;
        let html = '';
        if (vn === state.currentVN) {
            html = history.CurrentHtmlContent || history.currentHtmlContent || (note.HtmlContent || '');
        } else {
            const resp = await A.fetchVersionContent(state.noteId, vn);
            html = (resp && (resp.HtmlContent || resp.htmlContent)) || '';
        }
        // Replace signature placeholder, then sanitize before injection.
        const replaced = html.replace(/\{\{provider_signature\}\}/gi,
            '<span class="text-muted fst-italic">[Signed]</span>');
        contentHost.innerHTML = (window.sanitizeClinicalHtml || ((s) => s))(replaced);
        // Addendums only render when viewing current
        if (vn === state.currentVN) appendAddendumsToLeft();
        renderVersionBar();
        renderTimeline();
        // Scroll the left pane back to the top on a swap
        leftPane.scrollTo({ top: 0, behavior: 'smooth' });
    }

    // Jump the left pane to an addendum block. If currently historical, swap to
    // current first (so the addendum block actually exists in the DOM).
    async function jumpToAddendum(addendumId) {
        if (state.viewingVN !== state.currentVN) {
            await swapToVersion(state.currentVN);
            // Wait one frame so DOM insertions settle before scrolling
            await new Promise(r => requestAnimationFrame(() => r()));
        }
        const target = document.getElementById(`addendum-block-${addendumId}`);
        if (!target) return;
        A.scrollAndFlash(leftPane, target, 'flash');
    }

    // Initial render — viewing current by default
    // If the history has no amendments, contentHost already shows note.HtmlContent
    // from the initial render; swap to current to normalize (applies CurrentHtmlContent
    // which may include signature-placeholder replacement).
    await swapToVersion(state.currentVN);
}

/**
 * Render content for a note tab
 */
async function renderNoteTabContent(note) {
    const tabPane = document.getElementById(`note-pane-${note.ClinicalNoteId}`);
    const modalFooter = document.getElementById('tabbedNoteModalFooter');
    if (!tabPane) return;

    const isSigned = note.Status === 2 || note.Status === 4;
    const currentUserData = JSON.parse(localStorage.getItem('currentUser') || '{}');

    // Check permissions for actions
    const canEdit = note.Status == 0 && currentUserData?.Role != 1 && (
        note.CreatedByUserId == currentUserData?.UserId ||
        (currentUserData?.Role == 2 && note.ProviderId == currentUserData?.ProviderId) ||
        currentUserData?.Role == 0
    );

    const canSign = note.Status == 0 && currentUserData?.Role != 1 && (
        note.CreatedByUserId == currentUserData?.UserId ||
        (currentUserData?.Role == 2 && note.ProviderId == currentUserData?.ProviderId) ||
        currentUserData?.Role == 0
    );

    const canDelete = canEdit;

    // Process content - replace signature placeholder
    let processedContent = note.HtmlContent || '';
    if (isSigned) {
        processedContent = processedContent.replace(
            /\{\{provider_signature\}\}/gi,
            '<span class="text-muted fst-italic">[Signed]</span>'
        );
    } else {
        processedContent = processedContent.replace(
            /\{\{provider_signature\}\}/gi,
            '<div class="signature-placeholder border-bottom border-dark d-inline-block" style="min-width: 200px; height: 40px;"></div>'
        );
    }

    if (isSigned || tabbedNotesState.viewMode) {
        // View-only mode for signed notes or when viewMode is true.
        // Layout is a 60/40 split: note content on the left, activity timeline on the right.
        // When NOT signed (pure viewMode, rare), we still render the split but the right
        // timeline is hidden — no activity to show.
        tabPane.innerHTML = `
            <div class="note-split">
                <div class="note-pane">
                    ${isSigned ? `
                        <div class="alert alert-success py-2 mb-3" id="viewNoteSignedBanner-${note.ClinicalNoteId}">
                            <i class="bi bi-check-circle-fill me-2"></i>
                            <strong>Signed</strong> on ${formatDate(note.SignedAt)}
                            ${note.SignedByName ? ` by ${escapeHtml(note.SignedByName)}` : ''}
                        </div>
                    ` : ''}
                    <div class="version-bar-host" id="viewVersionBar-${note.ClinicalNoteId}"></div>
                    <div class="note-content-view p-3 border rounded bg-light" id="viewNoteContentHost-${note.ClinicalNoteId}">
                        ${processedContent}
                    </div>
                </div>
                ${isSigned ? `<div class="timeline-pane" id="viewTimelinePane-${note.ClinicalNoteId}"></div>` : ''}
            </div>
        `;

        // Update modal footer with action buttons. Print/PDF and Word target the
        // ACTIVE tab's note-content-view — so they capture whatever version is
        // currently being viewed on the left, which is the "print what you see"
        // rule (see rules/technical/amendment-addendum.md §10.3).
        const tabbedNoteFilename = `${escapeHtml(note.PatientName || 'Patient')}_${escapeHtml(note.TemplateName || note.TypeName || 'Note')}`;
        if (modalFooter) {
            modalFooter.className = 'modal-footer d-flex justify-content-between';
            modalFooter.innerHTML = `
                <div>
                    <button type="button" class="btn btn-secondary" data-bs-dismiss="modal">Close</button>
                </div>
                <div class="d-flex gap-2">
                    ${isSigned ? `
                        <button class="btn btn-outline-primary" onclick="exportClinicalNotePDF('#clinicalNoteTabContent .tab-pane.active .note-content-view')">
                            <i class="bi bi-file-earmark-pdf me-1"></i>Print/PDF
                        </button>
                        <button class="btn btn-outline-primary" onclick="exportClinicalNoteWord('#clinicalNoteTabContent .tab-pane.active .note-content-view', '${tabbedNoteFilename}')">
                            <i class="bi bi-file-earmark-word me-1"></i>Word
                        </button>
                    ` : ''}
                    ${!isSigned && canDelete ? `
                        <button class="btn btn-outline-danger" onclick="deleteClinicalNoteFromTab(${note.ClinicalNoteId}, '${escapeHtml(note.TemplateName || note.TypeName || 'Note')}')">
                            <i class="bi bi-trash me-1"></i>Delete
                        </button>
                    ` : ''}
                    ${!isSigned && canEdit ? `
                        <button class="btn btn-warning" onclick="switchToEditMode(${note.ClinicalNoteId})">
                            <i class="bi bi-pencil me-1"></i>Edit
                        </button>
                    ` : ''}
                    ${!isSigned && canSign ? `
                        <button class="btn btn-success" onclick="signClinicalNoteFromTab(${note.ClinicalNoteId})">
                            <i class="bi bi-check-lg me-1"></i>Sign
                        </button>
                    ` : ''}
                </div>
            `;

            // Amendment / Addendum buttons + countdown — server-driven, only for the
            // original signing clinician on a signed note, and subject to the 24h window.
            if (isSigned && window.AmendmentAddendum) {
                const rightSide = modalFooter.querySelector('.d-flex.gap-2');
                if (rightSide) {
                    try {
                        window.AmendmentAddendum.renderActionButtons(note.ClinicalNoteId, rightSide);
                    } catch (e) {
                        console.warn('[GlobalBridge] renderActionButtons failed', e);
                    }
                }
            }
        }

        // For signed notes, fetch history and set up the split view:
        //   - right timeline (Note Activity)
        //   - left version bar (green when viewing current, yellow when historical)
        //   - current content replacement + addendums appended (only when viewing current)
        //   - version swap + addendum jump interactions
        if (isSigned && window.AmendmentAddendum) {
            try {
                const history = await window.AmendmentAddendum.fetchHistory(note.ClinicalNoteId);
                if (history) {
                    await setupSideBySideNoteView(tabPane, note, history);
                }
            } catch (e) {
                console.warn('[GlobalBridge] side-by-side setup failed', e);
            }
        }
    } else {
        // Edit mode - show editor
        tabPane.innerHTML = `
            <div class="note-edit-container">
                <textarea id="noteEditor-${note.ClinicalNoteId}" class="form-control" style="min-height: 400px;">${escapeHtml(note.HtmlContent || '')}</textarea>
            </div>
        `;

        // Update modal footer with edit mode buttons
        // Close on left, Delete Save Draft Save & Sign on right
        if (modalFooter) {
            modalFooter.className = 'modal-footer d-flex justify-content-between';
            modalFooter.innerHTML = `
                <div>
                    <button type="button" class="btn btn-secondary" data-bs-dismiss="modal">Close</button>
                </div>
                <div class="d-flex gap-2">
                    ${canDelete ? `
                        <button class="btn btn-outline-danger" onclick="deleteClinicalNoteFromTab(${note.ClinicalNoteId}, '${escapeHtml(note.TemplateName || note.TypeName || 'Note')}')">
                            <i class="bi bi-trash me-1"></i>Delete
                        </button>
                    ` : ''}
                    <button class="btn btn-outline-secondary" onclick="saveClinicalNoteFromTab(${note.ClinicalNoteId}, false)">
                        <i class="bi bi-save me-1"></i>Save Draft
                    </button>
                    ${canSign ? `
                        <button class="btn btn-success" onclick="saveClinicalNoteFromTab(${note.ClinicalNoteId}, true)">
                            <i class="bi bi-check-lg me-1"></i>Save & Sign
                        </button>
                    ` : ''}
                </div>
            `;
        }

        // Initialize Trumbowyg editor if available
        if (typeof $.fn.trumbowyg !== 'undefined') {
            $(`#noteEditor-${note.ClinicalNoteId}`).trumbowyg({
                btns: [
                    ['viewHTML'],
                    ['undo', 'redo'],
                    ['formatting'],
                    ['strong', 'em', 'del'],
                    ['superscript', 'subscript'],
                    ['justifyLeft', 'justifyCenter', 'justifyRight', 'justifyFull'],
                    ['unorderedList', 'orderedList'],
                    ['horizontalRule'],
                    ['removeformat'],
                    ['fullscreen']
                ],
                autogrow: true,
                semantic: true,
                removeformatPasted: true
            });
        }
    }
}

/**
 * Switch from view mode to edit mode for a note tab
 */
async function switchToEditMode(noteId) {
    tabbedNotesState.viewMode = false;
    await activateNoteTab(noteId);
}

/**
 * Sign clinical note from tabbed interface
 */
async function signClinicalNoteFromTab(noteId) {
    const result = await signClinicalNoteCore(noteId);

    if (result.success) {
        // Update the note status in state
        const noteIndex = tabbedNotesState.notes.findIndex(n => n.ClinicalNoteId === noteId);
        if (noteIndex >= 0) {
            tabbedNotesState.notes[noteIndex].Status = 2; // Signed
        }

        // Rebuild tabs to update status icons
        buildNoteTabs();

        // Re-render the content in view mode
        tabbedNotesState.viewMode = true;
        await activateNoteTab(noteId);

        // Refresh dashboard
        if (window.dashboardModule) {
            window.dashboardModule.load();
        }

        // Refresh clinical notes list if on that page
        if (window.clinicalNotesModule) {
            window.clinicalNotesModule.load?.();
        }
    }
}

/**
 * Save clinical note from tabbed interface
 */
async function saveClinicalNoteFromTab(noteId, alsoSign = false) {
    try {
        // Get content from editor
        let htmlContent;
        const editorId = `noteEditor-${noteId}`;
        if (typeof $.fn.trumbowyg !== 'undefined' && $(`#${editorId}`).data('trumbowyg')) {
            htmlContent = $(`#${editorId}`).trumbowyg('html');
        } else {
            htmlContent = document.getElementById(editorId)?.value || '';
        }

        // Restore signature placeholder if it was replaced with the line
        htmlContent = htmlContent.replace(
            /<div class="signature-placeholder[^>]*><\/div>/gi,
            '{{provider_signature}}'
        );

        // Update the note
        await apiRequest(`/clinical-notes/${noteId}`, {
            method: 'PUT',
            body: { HtmlContent: htmlContent }
        });

        if (alsoSign) {
            await signClinicalNoteFromTab(noteId);
        } else {
            // Refresh clinical notes list if on that page
            if (window.clinicalNotesModule) {
                window.clinicalNotesModule.load?.();
            }
        }

    } catch (error) {
        console.error('[GlobalBridge] Save clinical note from tab error:', error);
        showToast('Error', 'Failed to save note', 'error');
    }
}

/**
 * Delete clinical note from tabbed interface
 */
async function deleteClinicalNoteFromTab(noteId, noteTitle) {
    // Use Bootstrap ConfirmDialog for consistent UI
    const confirmed = await ConfirmDialog.confirmDelete(`"${noteTitle}"`);
    if (!confirmed) return;

    try {
        await apiRequest(`/clinical-notes/${noteId}`, {
            method: 'DELETE'
        });

        // Remove the deleted note from state
        tabbedNotesState.notes = tabbedNotesState.notes.filter(n => n.ClinicalNoteId !== noteId);

        // Check if any notes remain
        if (tabbedNotesState.notes.length === 0) {
            // No more notes - close the modal
            const modal = bootstrap.Modal.getInstance(document.getElementById('tabbedClinicalNoteModal'));
            if (modal) modal.hide();

            // Refresh clinical notes list
            if (window.clinicalNotesModule) {
                window.clinicalNotesModule.load?.();
            }

            // Refresh dashboard
            if (window.dashboardModule) {
                window.dashboardModule.load();
            }

            return;
        }

        // Switch to another tab
        const newActiveNoteId = tabbedNotesState.notes[0].ClinicalNoteId;
        tabbedNotesState.activeNoteId = newActiveNoteId;

        // Rebuild tabs
        buildNoteTabs();

        // Activate the new tab
        await activateNoteTab(newActiveNoteId);

        // Refresh clinical notes list
        if (window.clinicalNotesModule) {
            window.clinicalNotesModule.load?.();
        }

        // Refresh dashboard
        if (window.dashboardModule) {
            window.dashboardModule.load();
        }

    } catch (error) {
        console.error('[GlobalBridge] Delete note from tab error:', error);
        showToast('Error', error.message || 'Failed to delete clinical note', 'error');
    }
}

// ============================================
// Recording / Scribe Functions
// ============================================

// Store current appointment for notes (used by recording modal)
// Exposed on window for cross-module access
window.currentAppointmentForNotes = null;

/**
 * Open record session modal from appointment detail
 * This is called from the appointment detail modal "Record Session" button
 */
async function openRecordSessionModal() {
    // Get the current appointment from multiple sources:
    // 1. appointmentModule.currentAppointment (from appointment detail modal)
    // 2. window.currentAppointmentForNotes (set by DashboardModule or other callers)
    const appointmentModule = window.App?.modules?.get('appointments') || window.appointmentModule;
    const appointment = appointmentModule?.currentAppointment || window.currentAppointmentForNotes;

    console.log('[GlobalBridge] openRecordSessionModal called', {
        fromAppointmentModule: !!appointmentModule?.currentAppointment,
        fromWindowGlobal: !!window.currentAppointmentForNotes,
        appointment: appointment
    });

    if (!appointment) {
        showToast('Error', 'No appointment selected', 'error');
        return;
    }

    // Initialize scribe module if needed
    if (window.scribeModule) {
        if (!window.scribeModule.isInitialized) {
            await window.scribeModule.init();
        }
        // Start recording instantly — skip pre-recording modal
        // Falls back to modal if there's an unfinished session to resume
        await window.scribeModule.startInstantly(appointment);
    } else {
        console.error('[GlobalBridge] ScribeModule not available');
        showToast('Error', 'Recording module not available', 'error');
    }
}

/**
 * Open record session from Missing Notes dashboard card
 */
async function openRecordSessionFromMissingNotes(appointmentId) {
    try {
        const appointment = await apiRequest(`/appointments/${appointmentId}`);
        if (appointment) {
            window.currentAppointmentForNotes = appointment;
            await openRecordSessionModal();
        }
    } catch (error) {
        showToast('Error', 'Failed to load appointment', 'error');
    }
}

function closeRecordSessionBeforeStart() {
    if (window.scribeModule) {
        window.scribeModule.closeModalBeforeStart();
    } else {
        const modal = document.getElementById('recordSessionModal');
        if (modal) {
            const bsModal = bootstrap.Modal.getInstance(modal);
            if (bsModal) bsModal.hide();
        }
    }
}

function hideRecordSessionError() {
    const errorEl = document.getElementById('recordSessionError');
    if (errorEl) {
        errorEl.classList.add('d-none');
    }
}

function discardRecording() {
    if (window.scribeModule) {
        window.scribeModule.discardRecording();
    }
}

// ============================================
// Post-Save Handlers
// ============================================

/**
 * Handle Initial Evaluation creation from post-save modal
 * Opens appointment wizard with patient pre-selected and IE type chosen
 */
async function handlePostSaveInitialEvaluation() {
    const patientId = document.getElementById('postSavePatientId')?.value;
    if (!patientId) {
        console.error('[GlobalBridge] No patient ID found in post-save modal');
        return;
    }

    // Close post-save modal
    const postSaveModalEl = document.getElementById('postSaveModal');
    if (postSaveModalEl) {
        const postSaveModal = bootstrap.Modal.getInstance(postSaveModalEl);
        if (postSaveModal) {
            postSaveModal.hide();
        }
    }

    // Open appointment modal with patient pre-selected for Initial Evaluation
    setTimeout(async () => {
        const appointmentModule = window.App?.modules?.get('appointments') || window.appointmentModule;

        if (appointmentModule && appointmentModule.openForInitialEvaluation) {
            await appointmentModule.openForInitialEvaluation(parseInt(patientId));
        } else {
            // Fallback: open regular appointment modal and preselect patient
            console.warn('[GlobalBridge] openForInitialEvaluation not available, using fallback');
            if (appointmentModule) {
                await appointmentModule.openNewAppointment();
                if (appointmentModule.preselectPatient) {
                    await appointmentModule.preselectPatient(parseInt(patientId));
                }
            }
        }
    }, 300);
}

// ============================================
// Utility - Scroll to Section
// ============================================

function scrollToSection(sectionId) {
    const element = document.getElementById(sectionId);
    if (element) {
        element.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
}

// ============================================
// API Request Helper (Legacy Support)
// ============================================

async function apiRequest(endpoint, options = {}) {
    const {
        method = 'GET',
        body = null,
        showLoader = true,
        showErrors = true
    } = options;

    if (showLoader) showGlobalLoader();

    try {
        const token = localStorage.getItem('authToken');
        const fetchOptions = {
            method,
            headers: {
                'Content-Type': 'application/json',
                'Authorization': token ? `Bearer ${token}` : ''
            }
        };

        if (body && method !== 'GET') {
            fetchOptions.body = JSON.stringify(body);
        }

        const response = await fetch(`/api${endpoint}`, fetchOptions);

        if (response.status === 401) {
            // Clear invalid session and show login page — avoid redirect to prevent loop
            // (HomeController.Index redirects to Dashboard, causing an infinite reload cycle)
            if (!window._handling401) {
                window._handling401 = true;
                localStorage.removeItem('authToken');
                localStorage.removeItem('currentUser');
                localStorage.removeItem('currentLocation');
                sessionStorage.removeItem('resumePopupShown');
                // Use auth module logout if available, otherwise toggle UI directly
                if (window.App?.auth?.logout) {
                    window.App.auth.logout();
                } else {
                    const loginPage = document.getElementById('loginPage');
                    const mainApp = document.getElementById('mainApp');
                    if (loginPage) loginPage.classList.remove('d-none');
                    if (mainApp) mainApp.classList.add('d-none');
                }
                // Reset flag after a short delay to allow future 401 handling
                setTimeout(() => { window._handling401 = false; }, 3000);
            }
            return null;
        }

        if (!response.ok) {
            // Try to extract a clean message from a JSON response body. Anything
            // else (HTML developer exception page, plain text dump, etc.) gets a
            // generic friendly message — never dump raw response text into the
            // toast since it can contain stack traces, headers, cookies, JWTs.
            let errorMessage = '';
            try {
                const responseText = await response.text();
                try {
                    const json = JSON.parse(responseText);
                    errorMessage = json?.message || json?.Message || '';
                } catch { /* not JSON — leave errorMessage empty */ }
            } catch { /* could not read body */ }

            const friendlyMessage = errorMessage && errorMessage.length <= 200
                ? errorMessage
                : `Request failed (${response.status}). Please try again.`;

            if (showErrors) {
                showToast('Error', friendlyMessage, 'error');
            }
            throw new Error(friendlyMessage);
        }

        if (response.status === 204) return null;

        const text = await response.text();
        if (!text) return null;
        return JSON.parse(text);
    } finally {
        if (showLoader) hideGlobalLoader();
    }
}

// ============================================
// Initialize Global State from Storage
// ============================================

function initGlobalState() {
    try {
        const savedUser = localStorage.getItem('currentUser');
        const savedToken = localStorage.getItem('authToken');
        const savedLocation = localStorage.getItem('currentLocation');
        const savedLocations = localStorage.getItem('availableLocations');

        if (savedUser) currentUser = JSON.parse(savedUser);
        if (savedToken) authToken = savedToken;
        if (savedLocation) currentLocation = JSON.parse(savedLocation);
        if (savedLocations) availableLocations = JSON.parse(savedLocations);
    } catch (e) {
        console.error('[GlobalBridge] Failed to restore global state:', e);
    }
}

// Initialize on load
document.addEventListener('DOMContentLoaded', initGlobalState);

// Initialize Clinical Note Modal Event Listeners
document.addEventListener('DOMContentLoaded', function() {
    // Template select change listener
    const templateSelect = document.getElementById('noteTemplateSelect');
    if (templateSelect) {
        templateSelect.addEventListener('change', async function() {
            const templateId = this.value;
            if (templateId) {
                await loadClinicalNoteTemplateContent(templateId);
            }
        });
    }

    // Save & Sign button click listener
    const saveSignBtn = document.getElementById('saveClinicalNoteBtn');
    if (saveSignBtn) {
        saveSignBtn.addEventListener('click', saveAndSignClinicalNote);
    }

    // Modal-stacking helper: when a modal opens while another modal is already
    // visible (e.g. clicking View Note inside the Patient Details modal), Bootstrap
    // does NOT automatically increment z-index. The result is that the newer modal
    // ends up BEHIND the older one. We manually bump the newer modal + its backdrop
    // to sit above any currently-visible modals.
    //
    // Attached to clinicalNoteModal (legacy edit) and tabbedClinicalNoteModal (new
    // view/edit). Both are commonly opened from inside the Patient Details modal.
    function _bumpModalZIndexIfStacked(modalEl) {
        // Count already-visible modals (excluding this one)
        const openModals = document.querySelectorAll('.modal.show');
        let otherOpen = 0;
        openModals.forEach(m => { if (m !== modalEl) otherOpen++; });
        if (otherOpen === 0) return;   // nothing to stack over — leave Bootstrap defaults

        const baseZ = 1055;            // Bootstrap 5 .modal default
        const bump = 20;               // leave room for backdrop beneath modal
        const newZ = baseZ + (otherOpen * bump);
        modalEl.style.zIndex = String(newZ);

        // Backdrops are appended LAST to <body>; the one that belongs to this
        // modal is the most recent. Push it just under the modal.
        setTimeout(() => {
            const backdrops = document.querySelectorAll('.modal-backdrop');
            const last = backdrops[backdrops.length - 1];
            if (last) last.style.zIndex = String(newZ - 1);
        }, 0);
    }
    // Expose so dynamically-created modals (AmendmentAddendumModule's addendum /
    // amendment / Mode 2 prompt modals) can use the same stacking logic.
    window.bumpModalZIndexIfStacked = _bumpModalZIndexIfStacked;

    // Clean up Trumbowyg when modal is hidden
    const clinicalNoteModal = document.getElementById('clinicalNoteModal');
    if (clinicalNoteModal) {
        clinicalNoteModal.addEventListener('show.bs.modal', () => _bumpModalZIndexIfStacked(clinicalNoteModal));
        clinicalNoteModal.addEventListener('hidden.bs.modal', function() {
            // Destroy Trumbowyg instance to prevent issues
            if (typeof $.fn.trumbowyg !== 'undefined' && $('#noteHtmlContent').data('trumbowyg')) {
                $('#noteHtmlContent').trumbowyg('destroy');
            }
            // Clear the textarea
            const noteContent = document.getElementById('noteHtmlContent');
            if (noteContent) noteContent.value = '';

            // Cleanup any orphaned backdrops (safety net)
            setTimeout(() => cleanupModalBackdrops(), 100);
        });
    }

    // Clean up tabbedClinicalNoteModal when hidden
    const tabbedClinicalNoteModal = document.getElementById('tabbedClinicalNoteModal');
    if (tabbedClinicalNoteModal) {
        tabbedClinicalNoteModal.addEventListener('show.bs.modal', () => _bumpModalZIndexIfStacked(tabbedClinicalNoteModal));
        tabbedClinicalNoteModal.addEventListener('hidden.bs.modal', function() {
            // Destroy any Trumbowyg editors in the tabbed modal
            if (typeof $.fn.trumbowyg !== 'undefined' && tabbedNotesState && tabbedNotesState.notes) {
                tabbedNotesState.notes.forEach(note => {
                    const editorEl = $(`#noteEditor-${note.ClinicalNoteId}`);
                    if (editorEl.data('trumbowyg')) {
                        editorEl.trumbowyg('destroy');
                    }
                });
            }

            // Clean up state
            if (tabbedNotesState && tabbedNotesState.editors) {
                tabbedNotesState.editors = {};
            }

            // Cleanup any orphaned backdrops (safety net)
            setTimeout(() => cleanupModalBackdrops(), 100);
        });
    }

    // (Legacy viewClinicalNoteModal hidden-handler removed — modal no longer exists.)

    // Add backdrop cleanup handlers for dashboard modals
    const dashboardModalIds = [
        'missingNotesModal',
        'missingSignatureModal',
        'requireScheduleModal',
        'noShowModal',
        'missedAppointmentsModal'
    ];

    dashboardModalIds.forEach(modalId => {
        const modalEl = document.getElementById(modalId);
        if (modalEl) {
            modalEl.addEventListener('hidden.bs.modal', function() {
                // Cleanup any orphaned backdrops (safety net)
                setTimeout(() => cleanupModalBackdrops(), 100);
            });
        }
    });

    // Patient Detail Modal cleanup (handles nested modals like confirm dialogs)
    const patientDetailModal = document.getElementById('patientDetailModal');
    if (patientDetailModal) {
        patientDetailModal.addEventListener('hidden.bs.modal', function() {
            // Cleanup any orphaned backdrops (safety net for nested modals)
            setTimeout(() => cleanupModalBackdrops(), 100);
        });
    }
});

// Initialize Care Episode Form Event Listeners
document.addEventListener('DOMContentLoaded', function() {
    // Care Episode form submit handler
    const careEpisodeForm = document.getElementById('careEpisodeForm');
    if (careEpisodeForm) {
        careEpisodeForm.addEventListener('submit', function(e) {
            e.preventDefault();
            saveCareEpisode();
        });
    }

    // Initialize CareEpisodeModule if available
    if (window.CareEpisodeModule && !window.careEpisodeModule) {
        window.careEpisodeModule = new CareEpisodeModule({
            api: window.apiService || (window.App && window.App.api),
            eventBus: window.eventBus || (window.App && window.App.events)
        });
        window.careEpisodeModule.init().then(() => {
            console.log('[GlobalBridge] CareEpisodeModule initialized');
        }).catch(err => {
            console.error('[GlobalBridge] CareEpisodeModule init failed:', err);
        });
    }

    // Global click handler for care episode actions (works in modals too)
    document.addEventListener('click', async function(e) {
        const target = e.target.closest('[data-action]');
        if (!target) return;

        const action = target.dataset.action;
        const patientId = target.dataset.patientId;
        const episodeId = target.dataset.episodeId;

        switch (action) {
            case 'create-care-episode':
                e.preventDefault();
                e.stopPropagation();
                console.log('[GlobalBridge] Create care episode for patient:', patientId);
                await openCareEpisodeModal(parseInt(patientId));
                break;
            case 'edit-care-episode':
                e.preventDefault();
                e.stopPropagation();
                console.log('[GlobalBridge] Edit care episode:', episodeId, 'for patient:', patientId);
                await openCareEpisodeModal(parseInt(patientId), parseInt(episodeId));
                break;
            case 'view-care-episode':
                e.preventDefault();
                e.stopPropagation();
                console.log('[GlobalBridge] View care episode:', episodeId);
                const careEpisodeModule = await getCareEpisodeModule();
                if (careEpisodeModule && careEpisodeModule.viewDetails) {
                    await careEpisodeModule.viewDetails(parseInt(episodeId));
                }
                break;
        }
    });
});

/**
 * Open new appointment modal with a specific patient pre-selected
 */
async function openNewAppointmentForPatient(patientId) {
    const appointmentModule = window.App?.modules?.get('appointments') || window.appointmentModule;

    if (appointmentModule) {
        // Open the appointment wizard
        await appointmentModule.openNewAppointment();

        // Pre-select the patient if method available
        if (appointmentModule.preselectPatient) {
            await appointmentModule.preselectPatient(patientId);
        } else {
            // Fallback: try to load and select patient manually
            try {
                const patient = await apiRequest(`/patients/${patientId}/autocomplete`);
                if (patient && appointmentModule._selectPatient) {
                    appointmentModule._selectPatient(patient);
                }
            } catch (error) {
                console.error('[GlobalBridge] Failed to preselect patient:', error);
            }
        }
    } else {
        console.error('[GlobalBridge] AppointmentModule not available');
        showToast('Error', 'Unable to open appointment form', 'error');
    }
}

// Initialize Quick Add Button (works on all pages)
document.addEventListener('DOMContentLoaded', function() {
    // Wait for App to be ready
    const initQuickAddBtn = () => {
        if (!window.App || !window.App.isInitialized()) {
            setTimeout(initQuickAddBtn, 100);
            return;
        }

        const quickAddBtn = document.getElementById('quickAddBtn');
        if (quickAddBtn) {
            quickAddBtn.addEventListener('click', function() {
                // Get appointment module from App
                const appointmentModule = window.App.modules.get('appointments');
                if (appointmentModule) {
                    appointmentModule.openNewAppointment();
                } else if (window.appointmentModule) {
                    // Fallback to global instance
                    window.appointmentModule.openNewAppointment();
                } else {
                    console.error('[GlobalBridge] AppointmentModule not available');
                }
            });
        }
    };

    initQuickAddBtn();

    // Initialize Global Patient Search
    const initSearch = () => {
        if (!window.App || !window.App.isInitialized()) {
            setTimeout(initSearch, 100);
            return;
        }
        initGlobalPatientSearch();
    };
    initSearch();
});

// ============================================
// Recurring Appointment Functions
// ============================================

function updateRecurringPreview() {
    console.log('[GlobalBridge] updateRecurringPreview called');
    const appointmentModule = window.App?.modules?.get('appointments') || window.appointmentModule;
    console.log('[GlobalBridge] appointmentModule:', appointmentModule ? 'found' : 'NOT FOUND');
    if (appointmentModule && appointmentModule.updateRecurringPreview) {
        console.log('[GlobalBridge] Calling appointmentModule.updateRecurringPreview()');
        appointmentModule.updateRecurringPreview();
    } else {
        console.warn('[GlobalBridge] updateRecurringPreview not available on appointmentModule');
    }
}

// ============================================
// Calendar Helper Functions
// ============================================

/**
 * Initialize the calendar (called on page load if calendar exists)
 */
function initCalendar() {
    const calendarModule = window.App?.modules?.get('calendar') || window.calendarModule;
    if (calendarModule && !calendarModule.isInitialized) {
        calendarModule.init();
    }
}

/**
 * Load/refresh appointments on the calendar
 */
function loadAppointments() {
    const calendarModule = window.App?.modules?.get('calendar') || window.calendarModule;
    if (calendarModule) {
        calendarModule.refresh();
    }
}

/**
 * Render calendar legend with modern light colors
 */
function renderCalendarLegend() {
    const legendEl = document.getElementById('calendarLegend');
    if (!legendEl) return;

    // Modern light color palette matching CalendarModule
    const appointmentTypeColors = {
        0: { bg: '#D1FAE5', border: '#10B981' },  // New Patient - Soft Green
        1: { bg: '#DBEAFE', border: '#3B82F6' },  // Follow-Up - Soft Blue
        2: { bg: '#EDE9FE', border: '#8B5CF6' },  // Annual Physical - Soft Violet
        3: { bg: '#CFFAFE', border: '#06B6D4' },  // Wellness - Soft Cyan
        4: { bg: '#FEF3C7', border: '#F59E0B' },  // Consultation - Soft Amber
        5: { bg: '#F1F5F9', border: '#64748B' },  // Telehealth - Soft Slate
        6: { bg: '#FCE7F3', border: '#EC4899' },  // Procedure - Soft Pink
        7: { bg: '#FEE2E2', border: '#EF4444' },  // Urgent - Soft Red
        8: { bg: '#EFEBE9', border: '#8D6E63' },  // Lab Review - Soft Brown
        9: { bg: '#E8EAF6', border: '#5C6BC0' },  // Med Review - Soft Indigo
    };

    const items = Object.keys(appointmentTypeColors).map(key => {
        const idx = parseInt(key, 10);
        const name = getAppointmentTypeName(idx);
        const colors = appointmentTypeColors[idx];
        return `<div class="legend-item d-inline-flex align-items-center">
                    <span style="width:12px;height:12px;background:${colors.bg};display:inline-block;border-radius:4px;margin-right:6px;box-shadow:0 1px 2px rgba(0,0,0,0.08)"></span>
                    <span style="font-size:12px;color:#374151">${escapeHtml(name)}</span>
                </div>`;
    }).join('');

    legendEl.innerHTML = `
        <div class="d-flex flex-wrap align-items-center gap-3 mb-2">
            <span style="font-size:11px;font-weight:600;color:#6B7280;text-transform:uppercase;letter-spacing:0.05em">Appointment Types:</span>
            ${items}
            <div class="legend-item d-inline-flex align-items-center">
                <span style="width:12px;height:12px;background:#FEE2E2;display:inline-block;border-radius:4px;margin-right:6px;box-shadow:0 0 0 1px #EF4444"></span>
                <span style="font-size:12px;color:#374151">Missed (Need Reschedule)</span>
            </div>
            <div class="legend-item d-inline-flex align-items-center">
                <span style="width:12px;height:12px;background:#FECACA;display:inline-block;border-radius:4px;margin-right:6px;box-shadow:0 0 0 1px #F87171"></span>
                <span style="font-size:12px;color:#374151">Missed (Rescheduled)</span>
            </div>
            <div class="legend-item d-inline-flex align-items-center">
                <span style="width:12px;height:12px;background:#F3F4F6;display:inline-block;border-radius:4px;margin-right:6px;box-shadow:0 1px 2px rgba(0,0,0,0.08)"></span>
                <span style="font-size:12px;color:#374151">Cancelled</span>
            </div>
        </div>
        <div class="d-flex flex-wrap align-items-center gap-3">
            <span style="font-size:11px;font-weight:600;color:#6B7280;text-transform:uppercase;letter-spacing:0.05em">Workflow Status:</span>
            <div class="legend-item d-inline-flex align-items-center">
                <span style="width:12px;height:12px;background:#F59E0B;display:inline-block;border-radius:3px;margin-right:6px"></span>
                <span style="font-size:12px;color:#374151">In Progress</span>
            </div>
            <div class="legend-item d-inline-flex align-items-center">
                <span style="width:12px;height:12px;background:#10B981;display:inline-block;border-radius:3px;margin-right:6px"></span>
                <span style="font-size:12px;color:#374151">Completed</span>
            </div>
        </div>
    `;
}

/**
 * Get calendar event datetime for FullCalendar positioning
 * Converts UTC datetime to location timezone format without 'Z' suffix
 */
function getCalendarEventDateTime(apt, field) {
    const dateStr = apt[field];
    if (!dateStr) return null;

    const tzId = apt.TimeZoneId || getCurrentLocationTimezone().timeZoneId || 'America/Chicago';
    const converted = convertUtcToTimezone(dateStr, tzId);

    // Return ISO string without 'Z' suffix so FullCalendar treats as local time
    return converted.isoString;
}

/**
 * Show unavailability details (placeholder - can be expanded)
 */
function showUnavailabilityDetails(props) {
    console.log('[GlobalBridge] Unavailability clicked:', props);
    // Could show a modal with unavailability details
    if (props.providerName && props.typeName) {
        showToast('Unavailability', `${props.providerName} - ${props.typeName}: ${props.reason || 'Unavailable'}`, 'info');
    }
}

/**
 * Send no-show notification to patient
 */
async function sendNoShowNotification(appointmentId, buttonEl) {
    if (buttonEl) {
        buttonEl.disabled = true;
        buttonEl.innerHTML = '<span class="spinner-border spinner-border-sm"></span>';
    }

    try {
        const result = await apiRequest(`/notifications/no-show/${appointmentId}`, {
            method: 'POST'
        });

        if (result.Success) {
            showToast('Success', result.Message || 'Notification sent successfully', 'success');
            if (buttonEl) {
                buttonEl.innerHTML = '<i class="bi bi-check"></i>';
                buttonEl.classList.remove('btn-outline-primary');
                buttonEl.classList.add('btn-success');
            }
        } else {
            showToast('Warning', result.Message || 'Failed to send notification', 'warning');
            if (buttonEl) {
                buttonEl.innerHTML = '<i class="bi bi-bell"></i>';
                buttonEl.disabled = false;
            }
        }
    } catch (error) {
        console.error('Error sending notification:', error);
        showToast('Error', 'Failed to send notification', 'error');
        if (buttonEl) {
            buttonEl.innerHTML = '<i class="bi bi-bell"></i>';
            buttonEl.disabled = false;
        }
    }
}

/**
 * Reschedule from a missed appointment
 */
async function rescheduleFromMissed(appointmentId, patientId, providerId) {
    console.log('[GlobalBridge] rescheduleFromMissed called:', appointmentId, patientId, providerId);

    let appointmentModule = window.App?.modules?.get('appointments') || window.appointmentModule;

    // If module not found, try to create it
    if (!appointmentModule && window.AppointmentModule) {
        console.log('[GlobalBridge] Creating AppointmentModule instance for reschedule...');
        window.appointmentModule = new AppointmentModule({
            api: window.apiService || (window.App && window.App.api),
            eventBus: window.eventBus || (window.App && window.App.events)
        });
        await window.appointmentModule.init();
        appointmentModule = window.appointmentModule;
    }

    if (appointmentModule && appointmentModule.rescheduleFromMissed) {
        await appointmentModule.rescheduleFromMissed(appointmentId, patientId, providerId);
    } else if (appointmentModule && appointmentModule.openNewAppointment) {
        // Fallback: open new appointment with pre-filled patient
        console.log('[GlobalBridge] Using fallback - opening new appointment for reschedule');
        appointmentModule.openNewAppointment();
        // Set reschedule context
        const rescheduleField = document.getElementById('rescheduleFromAppointmentId');
        if (rescheduleField) {
            rescheduleField.value = appointmentId;
        }
    } else {
        console.error('[GlobalBridge] Cannot reschedule - no appointmentModule available');
        showToast('Error', 'Unable to start reschedule process', 'error');
    }
}

/**
 * Reschedule any appointment (works for all statuses)
 * This is the new universal reschedule function that handles both future and past appointments
 */
async function rescheduleAppointment(appointmentId) {
    console.log('[GlobalBridge] rescheduleAppointment called:', appointmentId);

    let appointmentModule = window.App?.modules?.get('appointments') || window.appointmentModule;

    // If module not found, try to create it
    if (!appointmentModule && window.AppointmentModule) {
        console.log('[GlobalBridge] Creating AppointmentModule instance for reschedule...');
        window.appointmentModule = new AppointmentModule({
            api: window.apiService || (window.App && window.App.api),
            eventBus: window.eventBus || (window.App && window.App.events)
        });
        await window.appointmentModule.init();
        appointmentModule = window.appointmentModule;
    }

    if (appointmentModule && appointmentModule.rescheduleAppointment) {
        await appointmentModule.rescheduleAppointment(appointmentId);
    } else {
        console.error('[GlobalBridge] Cannot reschedule - no appointmentModule available');
        showToast('Error', 'Unable to start reschedule process', 'error');
    }
}

/**
 * Format timezone-aware date/time display functions
 */
function formatDateTimeWithTimezone(slot) {
    if (!slot) return '-';
    const startTime = slot.StartTime || slot.start;
    const endTime = slot.EndTime || slot.end;
    const tz = slot.TimeZoneAbbreviation || slot.timeZoneAbbreviation || '';

    const startDate = new Date(startTime);
    const endDate = new Date(endTime);

    const dateStr = startDate.toLocaleDateString('en-US', {
        weekday: 'short',
        month: 'short',
        day: 'numeric',
        year: 'numeric'
    });
    const startStr = startDate.toLocaleTimeString('en-US', {
        hour: 'numeric',
        minute: '2-digit',
        hour12: true
    });
    const endStr = endDate.toLocaleTimeString('en-US', {
        hour: 'numeric',
        minute: '2-digit',
        hour12: true
    });

    return `${dateStr}, ${startStr} - ${endStr}${tz ? ' ' + tz : ''}`;
}

function formatDateWithTimezone(slot) {
    if (!slot) return '-';
    const startTime = slot.StartTime || slot.start;
    const startDate = new Date(startTime);
    return startDate.toLocaleDateString('en-US', {
        weekday: 'short',
        month: 'short',
        day: 'numeric',
        year: 'numeric'
    });
}

function formatTimeRangeWithTimezone(slot) {
    if (!slot) return '-';
    const startTime = slot.StartTime || slot.start;
    const endTime = slot.EndTime || slot.end;
    const tz = slot.TimeZoneAbbreviation || slot.timeZoneAbbreviation || '';

    const startDate = new Date(startTime);
    const endDate = new Date(endTime);

    const startStr = startDate.toLocaleTimeString('en-US', {
        hour: 'numeric',
        minute: '2-digit',
        hour12: true
    });
    const endStr = endDate.toLocaleTimeString('en-US', {
        hour: 'numeric',
        minute: '2-digit',
        hour12: true
    });

    return `${startStr} - ${endStr}${tz ? ' ' + tz : ''}`;
}

function formatDateForInputInTimezone(dateStr, tzId) {
    if (!dateStr) return '';
    const converted = convertUtcToTimezone(dateStr, tzId);
    return converted.dateString;
}

function formatTimeForInputInTimezone(dateStr, tzId) {
    if (!dateStr) return '';
    const converted = convertUtcToTimezone(dateStr, tzId);
    const hours = String(converted.hours).padStart(2, '0');
    const minutes = String(converted.minutes).padStart(2, '0');
    return `${hours}:${minutes}`;
}

function convertTimezoneToUtc(localDate, tzId) {
    // Note: This is a simplified conversion - for production, consider using a library like Luxon
    // This assumes localDate is already in the target timezone
    return localDate;
}

// Export key functions to window for global access
window.showToast = showToast;
window.escapeHtml = escapeHtml;
window.cleanupModalBackdrops = cleanupModalBackdrops;
window.apiRequest = apiRequest;
window.formatDate = formatDate;
window.formatTime = formatTime;
window.formatDateTime = formatDateTime;
window.formatCurrency = formatCurrency;
window.parseServerDateTime = parseServerDateTime;
window.getCurrentLocationTimezone = getCurrentLocationTimezone;
window.convertUtcToTimezone = convertUtcToTimezone;
window.getAppointmentTypeName = getAppointmentTypeName;
window.getStatusBadge = getStatusBadge;
window.navigateTo = navigateTo;

// Appointment functions
window.openAppointmentDetails = openAppointmentDetails;
window.editAppointment = editAppointment;
window.openCancelAppointmentModal = openCancelAppointmentModal;
window.openReinstateModal = openReinstateModal;
window.checkInAppointment = checkInAppointment;
window.startVisitAppointment = startVisitAppointment;
window._resumeEncounter = async function(appointmentId, patientId) {
    try {
        const encounter = await apiRequest(`/patients/${patientId}/encounters/by-appointment/${appointmentId}`, { showLoader: false });
        if (encounter && encounter.EncounterId) {
            window.location.href = `/Encounter/${encounter.EncounterId}`;
        } else {
            showToast('Error', 'Could not find encounter for this visit', 'error');
        }
    } catch (e) {
        console.error('[GlobalBridge] Resume encounter error:', e);
        showToast('Error', 'Could not resume encounter', 'error');
    }
};
window.checkOutAppointment = checkOutAppointment;
window.viewPatient = viewPatient;
window.viewPatientFallback = viewPatientFallback;
window.navigateToPatient = navigateToPatient;
window.initGlobalPatientSearch = initGlobalPatientSearch;
window.calculateAge = calculateAge;
window.rescheduleFromMissed = rescheduleFromMissed;
window.rescheduleAppointment = rescheduleAppointment;

// Calendar functions
window.initCalendar = initCalendar;
window.loadAppointments = loadAppointments;
window.renderCalendarLegend = renderCalendarLegend;
window.getCalendarEventDateTime = getCalendarEventDateTime;
window.showUnavailabilityDetails = showUnavailabilityDetails;
window.updateRecurringPreview = updateRecurringPreview;

// Timezone formatting
window.formatDateTimeWithTimezone = formatDateTimeWithTimezone;
window.formatDateWithTimezone = formatDateWithTimezone;
window.formatTimeRangeWithTimezone = formatTimeRangeWithTimezone;
window.formatDateForInputInTimezone = formatDateForInputInTimezone;
window.formatTimeForInputInTimezone = formatTimeForInputInTimezone;
window.convertTimezoneToUtc = convertTimezoneToUtc;

// Clinical notes functions
window.openCreateClinicalNote = openCreateClinicalNote;
window.editClinicalNote = editClinicalNote;
window.viewClinicalNote = viewClinicalNote;
window.deleteClinicalNote = deleteClinicalNote;
window.openTabbedClinicalNotes = openTabbedClinicalNotes;
window.saveClinicalNote = saveClinicalNote;
window.saveAndSignClinicalNote = saveAndSignClinicalNote;
window.initClinicalNoteTrumbowyg = initClinicalNoteTrumbowyg;
window.loadClinicalNoteTemplateContent = loadClinicalNoteTemplateContent;
window.setClinicalNoteEditorContent = setClinicalNoteEditorContent;

// Duplicate note functions
window.checkAndShowDuplicateOption = checkAndShowDuplicateOption;
window.duplicateFromLastNote = duplicateFromLastNote;

// Tabbed clinical notes functions
window.buildNoteTabs = buildNoteTabs;
window.activateNoteTab = activateNoteTab;
window.renderNoteTabContent = renderNoteTabContent;
window.switchToEditMode = switchToEditMode;
window.signClinicalNoteFromTab = signClinicalNoteFromTab;
window.saveClinicalNoteFromTab = saveClinicalNoteFromTab;
window.deleteClinicalNoteFromTab = deleteClinicalNoteFromTab;

// (Legacy view-modal sign/edit exports removed with the single-note modal.)

// Clinical note export functions
/**
 * Export clinical note content as PDF via browser print dialog
 */
function exportClinicalNotePDF(containerId) {
    const content = document.querySelector(containerId);
    if (!content) return;
    const printWindow = window.open('', '_blank');
    printWindow.document.write(`
    <html>
    <head>
        <title>Clinical Note</title>
        <link href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.2/dist/css/bootstrap.min.css" rel="stylesheet">
        <style>
            @media print {
                @page {
                    size: A4;
                    margin: 0;
                }
                body {
                    margin: 0;
                    padding: 0;
                }
                .page-break {
                    page-break-before: always;
                    display: block;
                    height: 0.75in;
                }
                .avoid-break {
                    page-break-inside: avoid;
                }
            }
        </style>
    </head>
    <body>
        <div class="first-page">
            ${content.innerHTML}
        </div>
        <script>
            window.onload = function() {
                window.print();
                window.onafterprint = function() { window.close(); };
            };
        </script>
    </body>
    </html>`);
    printWindow.document.close();
}

/**
 * Export clinical note content to Word document
 */
function exportClinicalNoteWord(containerId, filename) {
    const content = document.querySelector(containerId);
    if (!content) return;
    const html = `<html xmlns:o='urn:schemas-microsoft-com:office:office'
                 xmlns:w='urn:schemas-microsoft-com:office:word'
                 xmlns='http://www.w3.org/TR/REC-html40'>
           <head><meta charset='UTF-8'>
             <style>
                body { font-family: 'Calibri', sans-serif; }
             </style>
           </head>
           <body>${content.innerHTML}</body>
           </html>`;
    const blob = new Blob(['\ufeff', html], { type: 'application/msword' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = (filename || 'Clinical_Note') + '.doc';
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
}
window.exportClinicalNotePDF = exportClinicalNotePDF;
window.exportClinicalNoteWord = exportClinicalNoteWord;

// Clinical note signing functions (delegates to ClinicalNoteSignModule)
// Note: Full sign workflow is in ClinicalNoteSignModule.js
window.signClinicalNoteCore = signClinicalNoteCore;
window.signClinicalNoteSimple = signClinicalNoteSimple;

// Dashboard modal functions
window.openMissingNotesModal = openMissingNotesModal;
window.openMissingSignatureModal = openMissingSignatureModal;
window.openRequireScheduleModal = openRequireScheduleModal;
window.openNoShowModal = openNoShowModal;
window.openMissedAppointmentsModal = openMissedAppointmentsModal;
window.scrollToSection = scrollToSection;

// Portal Invitation Management
window.openPortalInvitations = function() {
    const modal = document.getElementById('portalInvitationModal');
    if (modal) new bootstrap.Modal(modal).show();
};

window.copyPortalLinkToClipboard = async function() {
    try {
        const currentLocation = JSON.parse(localStorage.getItem('currentLocation') || '{}');
        const locationId = currentLocation.LocationId;
        if (!locationId) {
            showToast('No location selected', 'warning');
            return;
        }
        const data = await apiRequest(`/portal/location-code/${locationId}`, { showLoader: false });
        if (data?.PortalUrl) {
            await navigator.clipboard.writeText(data.PortalUrl);
            showToast('Portal link copied to clipboard!', 'success');
        } else {
            showToast('No portal code configured for this location. Go to Locations to set one up.', 'warning');
        }
    } catch (e) {
        showToast('Failed to copy portal link', 'error');
    }
};

// Appointment wizard functions (bridge to AppointmentModule)
window.wizardNextStep = wizardNextStep;
window.wizardPrevStep = wizardPrevStep;
window.wizardGoToStep = wizardGoToStep;
window.clearWizardPatientSelection = clearWizardPatientSelection;
window.refreshAvailableSlots = refreshAvailableSlots;
window.selectScheduleType = selectScheduleType;
window.selectAppointmentType = selectAppointmentType;

// Appointment modal confirmation functions (bridge to AppointmentModule)
window.confirmCancelAppointment = confirmCancelAppointment;
window.confirmReinstateAppointment = confirmReinstateAppointment;
window.confirmDeletePatient = confirmDeletePatient;
window.checkInFromDetail = checkInFromDetail;
window.openRecordSessionModal = openRecordSessionModal;
window.openRecordSessionFromMissingNotes = openRecordSessionFromMissingNotes;
window.closeRecordSessionBeforeStart = closeRecordSessionBeforeStart;
window.hideRecordSessionError = hideRecordSessionError;
window.discardRecording = discardRecording;
window.handlePostSaveInitialEvaluation = handlePostSaveInitialEvaluation;

// Loader functions
window.showGlobalLoader = showGlobalLoader;
window.hideGlobalLoader = hideGlobalLoader;

// Debounce utility
window.debounce = debounce;

// No-show notification function
window.sendNoShowNotification = sendNoShowNotification;

// Patient appointment scheduling
window.openNewAppointmentForPatient = openNewAppointmentForPatient;

// Care Episode functions
window.getCareEpisodeModule = getCareEpisodeModule;
window.openCareEpisodeModal = openCareEpisodeModal;
window.saveCareEpisode = saveCareEpisode;
window.saveCareEpisodeAndSchedule = saveCareEpisodeAndSchedule;
window.confirmCareEpisodeCreation = confirmCareEpisodeCreation;
window.cancelCareEpisodeConfirmation = cancelCareEpisodeConfirmation;
window.confirmCareEpisodeDischarge = confirmCareEpisodeDischarge;
window.confirmCareEpisodeExtend = confirmCareEpisodeExtend;

// ============================================
// REQUIRE SCHEDULE - SCHEDULING VALIDATION
// ============================================

// Validation issue types for scheduling
const SCHEDULING_VALIDATION_ISSUES = {
    NO_INSURANCE: 'no_insurance',
    NO_AUTHORIZATION: 'no_authorization',
    EXPIRED_AUTHORIZATION: 'expired_authorization',
    VISITS_CONSUMED: 'visits_consumed'
};

/**
 * Validate patient insurance and authorization before scheduling
 * @param {number} patientId - Patient ID
 * @returns {Promise<Object>} Validation result
 */
async function validateSchedulingInsurance(patientId) {
    try {
        // Fetch full patient data including insurances
        const patient = await apiRequest(`/patients/${patientId}`);

        // Check if patient has any insurance
        const insurances = patient.Insurances || [];
        const primaryInsurance = insurances.find(i => i.Type === 0);

        if (!primaryInsurance) {
            return {
                isValid: false,
                issueType: SCHEDULING_VALIDATION_ISSUES.NO_INSURANCE,
                message: 'Patient does not have insurance on file.',
                details: null
            };
        }

        // Check if insurance type is Self Pay (InsuranceCategory = 3)
        // If Self Pay, skip all validation
        if (primaryInsurance.InsuranceCategory === 3) {
            return { isValid: true, issueType: null, message: null, details: null };
        }

        // Check for active authorization using the insurance ID
        // The API endpoint is /authorizations/insurance/{insuranceId}, not /authorizations/patient/{patientId}
        const insuranceId = primaryInsurance.InsuranceId || primaryInsurance.PatientInsuranceId;
        if (!insuranceId) {
            // No insurance ID found, skip authorization check
            return { isValid: true, issueType: null, message: null, details: null };
        }

        try {
            const authorizations = await apiRequest(`/authorizations/insurance/${insuranceId}`, { showErrors: false });

            if (!authorizations || authorizations.length === 0) {
                return {
                    isValid: false,
                    issueType: SCHEDULING_VALIDATION_ISSUES.NO_AUTHORIZATION,
                    message: 'Patient does not have an authorization on file.',
                    details: { insurance: primaryInsurance }
                };
            }

            // Find active authorization (not expired)
            const today = new Date();
            today.setHours(0, 0, 0, 0);

            const activeAuth = authorizations.find(auth => {
                if (!auth.EndDate) return true; // No end date means still active
                const endDate = new Date(auth.EndDate);
                return endDate >= today;
            });

            if (!activeAuth) {
                return {
                    isValid: false,
                    issueType: SCHEDULING_VALIDATION_ISSUES.EXPIRED_AUTHORIZATION,
                    message: 'Patient\'s authorization has expired.',
                    details: { insurance: primaryInsurance, authorizations }
                };
            }

            // Check if visits are consumed
            if (activeAuth.TotalVisitsAuthorized && activeAuth.VisitsUsed >= activeAuth.TotalVisitsAuthorized) {
                return {
                    isValid: false,
                    issueType: SCHEDULING_VALIDATION_ISSUES.VISITS_CONSUMED,
                    message: 'All authorized visits have been used.',
                    details: {
                        insurance: primaryInsurance,
                        authorization: activeAuth,
                        visitsUsed: activeAuth.VisitsUsed,
                        visitsAuthorized: activeAuth.TotalVisitsAuthorized
                    }
                };
            }

            // All checks passed
            return { isValid: true, issueType: null, message: null, details: null };

        } catch (authError) {
            // If authorizations endpoint fails (404 or any error), treat as no authorization
            // The error message may contain "not found" or the status code info
            const errorMsg = (authError.message || '').toLowerCase();
            if (errorMsg.includes('404') || errorMsg.includes('not found') || errorMsg.includes('no authorization')) {
                return {
                    isValid: false,
                    issueType: SCHEDULING_VALIDATION_ISSUES.NO_AUTHORIZATION,
                    message: 'Patient does not have an authorization on file.',
                    details: { insurance: primaryInsurance }
                };
            }
            // For other errors, also treat as no authorization (fail safely)
            console.warn('[GlobalBridge] Authorization check failed, treating as no authorization:', authError);
            return {
                isValid: false,
                issueType: SCHEDULING_VALIDATION_ISSUES.NO_AUTHORIZATION,
                message: 'Patient does not have an authorization on file.',
                details: { insurance: primaryInsurance }
            };
        }

    } catch (error) {
        console.error('[GlobalBridge] Error validating insurance:', error);
        // On error, allow scheduling to proceed (fail open for usability)
        return { isValid: true, issueType: null, message: null, details: null };
    }
}

/**
 * Show scheduling validation modal with issue and options
 * @param {number} patientId - Patient ID
 * @param {number} careEpisodeId - Care Episode ID
 * @param {Object} validationResult - Validation result object
 */
function showSchedulingValidationModal(patientId, careEpisodeId, validationResult) {
    const { issueType, message, details } = validationResult;

    // Build modal content based on issue type
    let title = 'Scheduling Warning';
    let icon = 'bi-exclamation-triangle-fill text-warning';
    let bodyContent = '';
    let actionButtons = '';

    switch (issueType) {
        case SCHEDULING_VALIDATION_ISSUES.NO_INSURANCE:
            title = 'Missing Insurance';
            icon = 'bi-shield-exclamation text-warning';
            bodyContent = `
                <p>${message}</p>
                <p class="text-muted">Please add insurance information to the patient record before scheduling, or proceed with self-pay.</p>
            `;
            actionButtons = `
                <button type="button" class="btn btn-outline-primary" onclick="openPatientForInsurance(${patientId}); bootstrap.Modal.getInstance(document.getElementById('schedulingValidationModal')).hide();">
                    <i class="bi bi-person-plus me-1"></i>Add Insurance
                </button>
                <button type="button" class="btn btn-warning" onclick="proceedWithScheduling(${patientId}, ${careEpisodeId}); bootstrap.Modal.getInstance(document.getElementById('schedulingValidationModal')).hide();">
                    <i class="bi bi-calendar-plus me-1"></i>Schedule Anyway
                </button>
            `;
            break;

        case SCHEDULING_VALIDATION_ISSUES.NO_AUTHORIZATION:
            title = 'Missing Authorization';
            icon = 'bi-file-earmark-x text-warning';
            bodyContent = `
                <p>${message}</p>
                <p class="text-muted">An authorization is typically required for insurance-covered visits. You may add one or proceed without.</p>
            `;
            actionButtons = `
                <button type="button" class="btn btn-outline-primary" onclick="openAuthorizationModal(${patientId}); bootstrap.Modal.getInstance(document.getElementById('schedulingValidationModal')).hide();">
                    <i class="bi bi-file-earmark-plus me-1"></i>Add Authorization
                </button>
                <button type="button" class="btn btn-warning" onclick="proceedWithScheduling(${patientId}, ${careEpisodeId}); bootstrap.Modal.getInstance(document.getElementById('schedulingValidationModal')).hide();">
                    <i class="bi bi-calendar-plus me-1"></i>Schedule Anyway
                </button>
            `;
            break;

        case SCHEDULING_VALIDATION_ISSUES.EXPIRED_AUTHORIZATION:
            title = 'Expired Authorization';
            icon = 'bi-calendar-x text-danger';
            bodyContent = `
                <p>${message}</p>
                <p class="text-muted">The patient's authorization has expired. Please request a new authorization or proceed at your discretion.</p>
            `;
            actionButtons = `
                <button type="button" class="btn btn-outline-primary" onclick="openAuthorizationModal(${patientId}); bootstrap.Modal.getInstance(document.getElementById('schedulingValidationModal')).hide();">
                    <i class="bi bi-file-earmark-plus me-1"></i>Add New Authorization
                </button>
                <button type="button" class="btn btn-warning" onclick="proceedWithScheduling(${patientId}, ${careEpisodeId}); bootstrap.Modal.getInstance(document.getElementById('schedulingValidationModal')).hide();">
                    <i class="bi bi-calendar-plus me-1"></i>Schedule Anyway
                </button>
            `;
            break;

        case SCHEDULING_VALIDATION_ISSUES.VISITS_CONSUMED:
            title = 'Visits Exhausted';
            icon = 'bi-exclamation-octagon text-danger';
            const visitsInfo = details ? `(${details.visitsUsed}/${details.visitsAuthorized} used)` : '';
            bodyContent = `
                <p>${message} ${visitsInfo}</p>
                <p class="text-muted">All authorized visits have been used. A new authorization may be needed to continue treatment.</p>
            `;
            actionButtons = `
                <button type="button" class="btn btn-outline-primary" onclick="openAuthorizationModal(${patientId}); bootstrap.Modal.getInstance(document.getElementById('schedulingValidationModal')).hide();">
                    <i class="bi bi-file-earmark-plus me-1"></i>Request New Authorization
                </button>
                <button type="button" class="btn btn-warning" onclick="proceedWithScheduling(${patientId}, ${careEpisodeId}); bootstrap.Modal.getInstance(document.getElementById('schedulingValidationModal')).hide();">
                    <i class="bi bi-calendar-plus me-1"></i>Schedule Anyway
                </button>
            `;
            break;

        default:
            bodyContent = `<p>${message || 'There was an issue validating this patient for scheduling.'}</p>`;
            actionButtons = `
                <button type="button" class="btn btn-warning" onclick="proceedWithScheduling(${patientId}, ${careEpisodeId}); bootstrap.Modal.getInstance(document.getElementById('schedulingValidationModal')).hide();">
                    <i class="bi bi-calendar-plus me-1"></i>Schedule Anyway
                </button>
            `;
    }

    // Create or update the validation modal
    let modal = document.getElementById('schedulingValidationModal');
    if (!modal) {
        modal = document.createElement('div');
        modal.id = 'schedulingValidationModal';
        modal.className = 'modal fade';
        modal.tabIndex = -1;
        document.body.appendChild(modal);
    }

    // Set full modal content each time to avoid element reference issues
    modal.innerHTML = `
        <div class="modal-dialog modal-dialog-centered">
            <div class="modal-content">
                <div class="modal-header bg-warning text-dark">
                    <h5 class="modal-title"><i class="${icon} me-2"></i>${title}</h5>
                    <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
                </div>
                <div class="modal-body">${bodyContent}</div>
                <div class="modal-footer">
                    <button type="button" class="btn btn-secondary" data-bs-dismiss="modal">Cancel</button>
                    ${actionButtons}
                </div>
            </div>
        </div>
    `;

    // Show the modal
    const bsModal = new bootstrap.Modal(modal);
    bsModal.show();
}

/**
 * Open Edit Patient modal on Insurance tab (on current page)
 * @param {number} patientId - Patient ID
 */
async function openPatientForInsurance(patientId) {
    await openPatientEditWithTab(patientId, 'insurance');
}

/**
 * Open Edit Patient modal on Insurance tab for adding authorization (on current page)
 * @param {number} patientId - Patient ID
 */
async function openAuthorizationModal(patientId) {
    await openPatientEditWithTab(patientId, 'insurance');
}

/**
 * Open Edit Patient modal and switch to a specific tab
 * @param {number} patientId - Patient ID
 * @param {string} tabName - Tab name ('demographics', 'insurance', 'documents')
 */
async function openPatientEditWithTab(patientId, tabName = 'demographics') {
    const patientModule = window.App?.modules?.get('patients') || window.patientModule;

    if (patientModule && patientModule.edit) {
        // Open the patient edit modal
        await patientModule.edit(patientId);

        // Switch to the specified tab after a short delay (to ensure modal is rendered)
        setTimeout(() => {
            // The patientModal uses button tabs with id like "insurance-tab" and data-bs-target="#insurance"
            const tabMapping = {
                'demographics': 'demographics-tab',
                'insurance': 'insurance-tab',
                'documents': 'documents-tab'
            };

            const tabButtonId = tabMapping[tabName] || tabMapping['demographics'];
            const tabButton = document.getElementById(tabButtonId);

            if (tabButton) {
                // Use Bootstrap Tab API to switch tabs
                const tab = new bootstrap.Tab(tabButton);
                tab.show();
                console.log('[GlobalBridge] Switched to tab:', tabName);
            } else {
                console.warn('[GlobalBridge] Tab button not found:', tabButtonId);
            }
        }, 300);
    } else {
        // Fallback: show a message
        showToast('Info', 'Please open the patient record to manage insurance and authorizations.', 'info');
    }
}

/**
 * Proceed with scheduling after validation (or skip)
 * Opens appointment modal with patient and care episode pre-selected
 * @param {number} patientId - Patient ID
 * @param {number} careEpisodeId - Care Episode ID (optional)
 */
async function proceedWithScheduling(patientId, careEpisodeId) {
    const appointmentModule = window.App?.modules?.get('appointments') || window.appointmentModule;

    if (appointmentModule) {
        // Open the appointment wizard
        await appointmentModule.openNewAppointment();

        // Pre-select the patient with care episode context
        if (appointmentModule.preselectPatientWithCareEpisode) {
            await appointmentModule.preselectPatientWithCareEpisode(patientId, careEpisodeId);
        } else if (appointmentModule.preselectPatient) {
            await appointmentModule.preselectPatient(patientId);
            // Manually set care episode if preselectPatientWithCareEpisode is not available
            if (careEpisodeId && appointmentModule.wizardState) {
                appointmentModule.wizardState.careEpisodeId = careEpisodeId;
                appointmentModule.wizardState.hasActiveCareEpisode = true;
                // Auto-select Follow-Up type for require schedule
                appointmentModule.wizardState.selectedType = 1;
                if (appointmentModule._selectTypeCard) {
                    appointmentModule._selectTypeCard(1);
                }
                if (appointmentModule._updateTypeCardsAvailability) {
                    appointmentModule._updateTypeCardsAvailability();
                }
            }
        } else {
            // Fallback: try to load and select patient manually
            try {
                const patient = await apiRequest(`/patients/${patientId}/autocomplete`);
                if (patient && appointmentModule._selectPatient) {
                    await appointmentModule._selectPatient(patient);
                    // Set care episode if available
                    if (careEpisodeId && appointmentModule.wizardState) {
                        appointmentModule.wizardState.careEpisodeId = careEpisodeId;
                        appointmentModule.wizardState.hasActiveCareEpisode = true;
                    }
                }
            } catch (error) {
                console.error('[GlobalBridge] Failed to preselect patient:', error);
            }
        }
    } else {
        console.error('[GlobalBridge] AppointmentModule not available');
        showToast('Error', 'Unable to open appointment form', 'error');
    }
}

/**
 * Schedule appointment from Require Schedule card
 * Validates insurance/authorization before proceeding
 * @param {number} patientId - Patient ID
 * @param {number} careEpisodeId - Care Episode ID
 */
async function scheduleFromRequireCard(patientId, careEpisodeId) {
    try {
        // Validate insurance/authorization before scheduling
        const validationResult = await validateSchedulingInsurance(patientId);

        if (!validationResult.isValid) {
            // Show validation modal with issue
            showSchedulingValidationModal(patientId, careEpisodeId, validationResult);
            return;
        }

        // Validation passed, proceed with scheduling
        await proceedWithScheduling(patientId, careEpisodeId);

    } catch (error) {
        console.error('[GlobalBridge] Error preparing schedule:', error);
        // Still open the modal even if we couldn't validate
        await proceedWithScheduling(patientId, careEpisodeId);
    }
}

// Export scheduling validation functions
window.scheduleFromRequireCard = scheduleFromRequireCard;
window.validateSchedulingInsurance = validateSchedulingInsurance;
window.showSchedulingValidationModal = showSchedulingValidationModal;
window.proceedWithScheduling = proceedWithScheduling;
window.openPatientForInsurance = openPatientForInsurance;
window.openAuthorizationModal = openAuthorizationModal;

// ============================================
// Mobile Sidebar Toggle
// ============================================

/**
 * Initialize mobile sidebar toggle functionality
 * Handles opening/closing sidebar on mobile devices
 */
document.addEventListener('DOMContentLoaded', function() {
    const sidebarToggle = document.getElementById('sidebarToggle');
    const sidebar = document.getElementById('sidebar');

    if (sidebarToggle && sidebar) {
        // Toggle sidebar when button is clicked
        sidebarToggle.addEventListener('click', function(e) {
            e.stopPropagation();
            sidebar.classList.toggle('show');

            // Toggle aria-expanded for accessibility
            const isExpanded = sidebar.classList.contains('show');
            sidebarToggle.setAttribute('aria-expanded', isExpanded);
        });

        // Close sidebar when clicking outside on mobile
        document.addEventListener('click', function(e) {
            if (sidebar.classList.contains('show') &&
                !sidebar.contains(e.target) &&
                !sidebarToggle.contains(e.target)) {
                sidebar.classList.remove('show');
                sidebarToggle.setAttribute('aria-expanded', 'false');
            }
        });

        // Close sidebar when pressing Escape key
        document.addEventListener('keydown', function(e) {
            if (e.key === 'Escape' && sidebar.classList.contains('show')) {
                sidebar.classList.remove('show');
                sidebarToggle.setAttribute('aria-expanded', 'false');
            }
        });

        // Close sidebar when a navigation link is clicked (mobile UX improvement)
        sidebar.querySelectorAll('a.nav-link').forEach(link => {
            link.addEventListener('click', function() {
                if (window.innerWidth < 768) {
                    sidebar.classList.remove('show');
                    sidebarToggle.setAttribute('aria-expanded', 'false');
                }
            });
        });
    }

    // ========== PREFERENCES MODAL ==========
    // Show/hide preferences button based on role (clinician-only)
    const _u = JSON.parse(localStorage.getItem('currentUser') || '{}');
    const _role = parseInt(_u.Role ?? _u.role ?? -1);
    document.querySelectorAll('.clinician-only').forEach(el => {
        if (_role !== 2) el.style.display = 'none';
    });

    // Load preferences when modal opens
    const prefsModal = document.getElementById('preferencesModal');
    if (prefsModal) {
        prefsModal.addEventListener('show.bs.modal', async () => {
            try {
                const prefs = await apiRequest('/providers/my-preferences', { showLoader: false });
                const toggle = document.getElementById('prefShowResumePopup');
                if (toggle && prefs) toggle.checked = prefs.ShowResumePopup !== false;
            } catch (e) {
                console.warn('[Preferences] Failed to load:', e);
            }
        });
    }

    // Save preferences
    document.getElementById('btnSavePreferences')?.addEventListener('click', async () => {
        try {
            const ShowResumePopup = document.getElementById('prefShowResumePopup')?.checked ?? true;
            await apiRequest('/providers/my-preferences', {
                method: 'PUT',
                body: { ShowResumePopup },
                showLoader: false
            });
            showToast('Preferences Saved', 'Your preferences have been updated.', 'success');
            bootstrap.Modal.getInstance(document.getElementById('preferencesModal'))?.hide();
        } catch (e) {
            showToast('Error', 'Failed to save preferences.', 'error');
            console.error('[Preferences] Save error:', e);
        }
    });

    // ============================================
    // PATIENT PORTAL INVITATION MANAGEMENT
    // ============================================

    const portalInvModal = document.getElementById('portalInvitationModal');
    if (portalInvModal) {
        portalInvModal.addEventListener('show.bs.modal', async () => {
            // Clear form fields
            document.getElementById('inviteFirstName').value = '';
            document.getElementById('inviteLastName').value = '';
            document.getElementById('inviteEmail').value = '';
            document.getElementById('inviteError')?.classList.add('d-none');
            document.getElementById('inviteSuccess')?.classList.add('d-none');
            await loadPortalInvitations();
            await loadPortalLink();
        });
    }

    async function loadPortalLink() {
        try {
            const currentLocation = JSON.parse(localStorage.getItem('currentLocation') || '{}');
            const locationId = currentLocation.LocationId;
            if (!locationId) return;

            const data = await apiRequest(`/portal/location-code/${locationId}`, { showLoader: false });
            if (data?.PortalUrl) {
                document.getElementById('portalLinkDisplay').value = data.PortalUrl;
                document.getElementById('portalLinkSection')?.classList.remove('d-none');
            }
        } catch (e) {
            console.warn('[Portal Invite] Failed to load portal link:', e);
        }
    }

    async function loadPortalInvitations() {
        const tbody = document.getElementById('invitationListBody');
        if (!tbody) return;

        tbody.innerHTML = '<tr><td colspan="5" class="text-center text-muted py-3"><div class="spinner-border spinner-border-sm"></div> Loading...</td></tr>';

        try {
            const invitations = await apiRequest('/portal/invitations', { showLoader: false });
            if (!invitations?.length) {
                tbody.innerHTML = '<tr><td colspan="5" class="text-center text-muted py-3">No invitations sent yet.</td></tr>';
                return;
            }

            const statusLabels = { 0: 'Pending', 1: 'Registered', 2: 'Expired' };
            const statusClasses = { 0: 'warning', 1: 'success', 2: 'secondary' };

            tbody.innerHTML = invitations.map(inv => `
                <tr>
                    <td>${escapeHtml(inv.PatientName || '-')}</td>
                    <td><small>${escapeHtml(inv.Email || '-')}</small></td>
                    <td><span class="badge bg-${statusClasses[inv.Status] || 'secondary'}">${statusLabels[inv.Status] || 'Unknown'}</span></td>
                    <td><small>${inv.CreatedAt ? new Date(inv.CreatedAt).toLocaleDateString() : '-'}</small></td>
                    <td>
                        ${inv.Status === 0 ? `<button class="btn btn-outline-info btn-sm" onclick="window.resendPortalInvite(${inv.InvitationId})" title="Resend">
                            <i class="bi bi-arrow-repeat"></i>
                        </button>` : ''}
                    </td>
                </tr>
            `).join('');
        } catch (e) {
            tbody.innerHTML = '<tr><td colspan="5" class="text-center text-danger py-3">Failed to load invitations.</td></tr>';
            console.error('[Portal Invite] Load error:', e);
        }
    }

    document.getElementById('btnSendPortalInvite')?.addEventListener('click', async () => {
        const errorEl = document.getElementById('inviteError');
        const successEl = document.getElementById('inviteSuccess');
        errorEl?.classList.add('d-none');
        successEl?.classList.add('d-none');

        const firstName = document.getElementById('inviteFirstName')?.value?.trim();
        const lastName = document.getElementById('inviteLastName')?.value?.trim();
        const email = document.getElementById('inviteEmail')?.value?.trim();

        if (!firstName || !lastName || !email) {
            errorEl.textContent = 'Please enter first name, last name, and email address.';
            errorEl.classList.remove('d-none');
            return;
        }

        const currentLocation = JSON.parse(localStorage.getItem('currentLocation') || '{}');
        const locationId = currentLocation.LocationId;

        const btn = document.getElementById('btnSendPortalInvite');
        btn.disabled = true;
        btn.innerHTML = '<span class="spinner-border spinner-border-sm"></span>';

        try {
            const result = await apiRequest('/portal/invitations/send', {
                method: 'POST',
                body: { FirstName: firstName, LastName: lastName, Email: email, LocationId: locationId },
                showLoader: false
            });

            if (result?.Success) {
                successEl.textContent = result.Message || 'Invitation sent successfully!';
                successEl.classList.remove('d-none');
                document.getElementById('inviteFirstName').value = '';
                document.getElementById('inviteLastName').value = '';
                document.getElementById('inviteEmail').value = '';
                await loadPortalInvitations();
                // Refresh patient list if on Patients page
                if (window._patientModule) {
                    try { window._patientModule.load(); } catch(_) {}
                }
            } else {
                errorEl.textContent = result?.Message || 'Failed to send invitation.';
                errorEl.classList.remove('d-none');
            }
        } catch (e) {
            errorEl.textContent = e.message || 'Failed to send invitation.';
            errorEl.classList.remove('d-none');
        } finally {
            btn.disabled = false;
            btn.innerHTML = '<i class="bi bi-send me-1"></i>Send';
        }
    });

    window.resendPortalInvite = async function(invitationId) {
        try {
            const result = await apiRequest(`/portal/invitations/${invitationId}/resend`, {
                method: 'POST',
                showLoader: false
            });
            if (result?.Success) {
                showToast('Success', 'Invitation resent successfully.', 'success');
                await loadPortalInvitations();
            } else {
                showToast('Error', result?.Message || 'Failed to resend invitation.', 'error');
            }
        } catch (e) {
            showToast('Error', e.message || 'Failed to resend invitation.', 'error');
        }
    };

    document.getElementById('refreshInvitationList')?.addEventListener('click', loadPortalInvitations);

    document.getElementById('copyPortalLink')?.addEventListener('click', () => {
        const input = document.getElementById('portalLinkDisplay');
        if (input?.value) {
            navigator.clipboard.writeText(input.value).then(() => {
                showToast('Copied', 'Portal link copied to clipboard.', 'success');
            });
        }
    });

    // ============================================
    // COLLECT PAYMENT MODAL
    // ============================================

    // Show/hide conditional fields based on payment method
    document.getElementById('collectPaymentMethod')?.addEventListener('change', function () {
        const checkFields = document.getElementById('collectPaymentCheckFields');
        const cardFields = document.getElementById('collectPaymentCardFields');
        if (checkFields) checkFields.style.display = this.value === '1' ? '' : 'none';
        if (cardFields) cardFields.style.display = (this.value === '2' || this.value === '3') ? '' : 'none';
    });

    // Set default date to today
    const payDateInput = document.getElementById('collectPaymentDate');
    if (payDateInput && !payDateInput.value) payDateInput.value = new Date().toISOString().split('T')[0];

    // Collect Payment - Patient search inside modal
    const cpSearchInput = document.getElementById('collectPaymentPatientSearch');
    const cpDropdown = document.getElementById('collectPaymentPatientDropdown');
    let cpSearchTimeout = null;

    cpSearchInput?.addEventListener('input', function () {
        clearTimeout(cpSearchTimeout);
        const q = this.value.trim();
        if (q.length < 3) { if (cpDropdown) cpDropdown.style.display = 'none'; return; }
        cpSearchTimeout = setTimeout(() => searchCollectPaymentPatient(q), 400);
    });

    async function searchCollectPaymentPatient(query) {
        try {
            let patients = [];
            const searchResp = await fetch(`/api/patients/search?q=${encodeURIComponent(query)}`, {
                headers: { 'Authorization': `Bearer ${localStorage.getItem('authToken')}` }
            });
            if (searchResp.ok) {
                const data = await searchResp.json();
                patients = data.Results || data;
            } else {
                const listResp = await fetch('/api/patients', {
                    headers: { 'Authorization': `Bearer ${localStorage.getItem('authToken')}` }
                });
                if (!listResp.ok) return;
                const all = await listResp.json();
                const q = query.toLowerCase();
                patients = all.filter(p =>
                    (p.FirstName && p.FirstName.toLowerCase().includes(q)) ||
                    (p.LastName && p.LastName.toLowerCase().includes(q)) ||
                    (p.FullName && p.FullName.toLowerCase().includes(q)) ||
                    (p.MRN && p.MRN.toLowerCase().includes(q))
                );
            }

            if (!cpDropdown) return;
            if (patients.length === 0) {
                cpDropdown.innerHTML = '<div class="px-3 py-2 text-muted small">No patients found</div>';
                cpDropdown.style.display = 'block';
                return;
            }

            cpDropdown.innerHTML = patients.slice(0, 8).map(p => {
                const name = (p.FirstName || '') + ' ' + (p.LastName || '');
                const mrn = p.MRN || '';
                return `<div class="cp-patient-item px-3 py-2 border-bottom" style="cursor:pointer;"
                    data-id="${p.PatientId}" data-name="${name.trim()}">
                    <div class="fw-semibold small">${name.trim()}</div>
                    <div class="text-muted" style="font-size:11px;">${mrn}</div>
                </div>`;
            }).join('');

            cpDropdown.querySelectorAll('.cp-patient-item').forEach(item => {
                item.addEventListener('click', function () {
                    selectCollectPaymentPatient(parseInt(this.dataset.id), this.dataset.name);
                });
                item.addEventListener('mouseenter', function () { this.style.background = '#f3f4f6'; });
                item.addEventListener('mouseleave', function () { this.style.background = ''; });
            });

            cpDropdown.style.display = 'block';
        } catch (e) {
            console.error('Collect payment search error:', e);
        }
    }

    function selectCollectPaymentPatient(patientId, patientName) {
        document.getElementById('collectPaymentPatientId').value = patientId;
        document.getElementById('collectPaymentPatientName').textContent = patientName;
        document.getElementById('collectPaymentPatientInfo').style.display = 'block';
        document.getElementById('collectPaymentSearchGroup').style.display = 'none';
        if (cpDropdown) cpDropdown.style.display = 'none';

        // Fetch and display outstanding balance for selected patient
        const balanceDiv = document.getElementById('collectPaymentBalanceInfo');
        const balanceAmt = document.getElementById('collectPaymentBalanceAmount');
        if (balanceDiv) balanceDiv.style.display = 'none';
        if (patientId) {
            apiRequest(`/payments/patient/${patientId}/outstanding`, { showLoader: false })
                .then(bal => {
                    if (bal && bal.CurrentBalance > 0 && balanceDiv && balanceAmt) {
                        balanceAmt.textContent = '$' + bal.CurrentBalance.toFixed(2);
                        balanceDiv.style.display = 'block';
                    }
                }).catch(() => {});
        }
    }

    // Change patient button
    document.getElementById('collectPaymentChangePatient')?.addEventListener('click', function () {
        document.getElementById('collectPaymentPatientId').value = '';
        document.getElementById('collectPaymentPatientInfo').style.display = 'none';
        document.getElementById('collectPaymentSearchGroup').style.display = 'block';
        document.getElementById('collectPaymentPatientSearch').value = '';
        document.getElementById('collectPaymentPatientSearch').focus();
        const balanceDiv = document.getElementById('collectPaymentBalanceInfo');
        if (balanceDiv) balanceDiv.style.display = 'none';
    });

    // Global function to open collect payment modal
    window.openCollectPaymentModal = function (patientId, appointmentId, suggestedAmount, patientName) {
        const modal = document.getElementById('collectPaymentModal');
        if (!modal) return;

        // Reset form
        document.getElementById('collectPaymentPatientId').value = patientId || '';
        document.getElementById('collectPaymentAppointmentId').value = appointmentId || '';
        document.getElementById('collectPaymentAmount').value = suggestedAmount || '';
        document.getElementById('collectPaymentType').value = '0';
        document.getElementById('collectPaymentMethod').value = '0';
        const checkNumEl = document.getElementById('collectPaymentCheckNumber');
        if (checkNumEl) checkNumEl.value = '';
        const checkDateEl = document.getElementById('collectPaymentCheckDate');
        if (checkDateEl) checkDateEl.value = '';
        const cardRefEl = document.getElementById('collectPaymentCardReference');
        if (cardRefEl) cardRefEl.value = '';
        const refNumEl = document.getElementById('collectPaymentReferenceNumber');
        if (refNumEl) refNumEl.value = '';
        const notesEl = document.getElementById('collectPaymentNotes');
        if (notesEl) notesEl.value = '';
        const payDateEl = document.getElementById('collectPaymentDate');
        if (payDateEl) payDateEl.value = new Date().toISOString().split('T')[0];
        const checkFields = document.getElementById('collectPaymentCheckFields');
        if (checkFields) checkFields.style.display = 'none';
        const cardFields = document.getElementById('collectPaymentCardFields');
        if (cardFields) cardFields.style.display = 'none';
        if (cpSearchInput) cpSearchInput.value = '';
        if (cpDropdown) cpDropdown.style.display = 'none';

        const infoDiv = document.getElementById('collectPaymentPatientInfo');
        const searchGroup = document.getElementById('collectPaymentSearchGroup');

        const balanceDiv = document.getElementById('collectPaymentBalanceInfo');
        const balanceAmt = document.getElementById('collectPaymentBalanceAmount');
        if (balanceDiv) balanceDiv.style.display = 'none';

        if (patientId && patientName) {
            // Patient pre-selected (from ledger, check-in, etc.)
            document.getElementById('collectPaymentPatientName').textContent = patientName;
            if (infoDiv) infoDiv.style.display = 'block';
            if (searchGroup) searchGroup.style.display = 'none';

            // Fetch and display outstanding balance
            apiRequest(`/payments/patient/${patientId}/outstanding`, { showLoader: false })
                .then(bal => {
                    if (bal && bal.CurrentBalance > 0 && balanceDiv && balanceAmt) {
                        balanceAmt.textContent = '$' + bal.CurrentBalance.toFixed(2);
                        balanceDiv.style.display = 'block';
                    }
                }).catch(() => {});
        } else {
            // No patient — show search
            if (infoDiv) infoDiv.style.display = 'none';
            if (searchGroup) searchGroup.style.display = 'block';
        }

        const bsModal = new bootstrap.Modal(modal);
        // When opened over the Patient Profile modal, Bootstrap creates a new backdrop at
        // the default z-index (1050), which sits under the parent modal's 1055. Bump both
        // the modal and its newly-inserted backdrop so this modal stacks correctly on top.
        modal.addEventListener('shown.bs.modal', function onShown() {
            modal.removeEventListener('shown.bs.modal', onShown);
            try {
                modal.style.zIndex = '1065';
                const backdrops = document.querySelectorAll('.modal-backdrop');
                if (backdrops.length > 0) {
                    const latest = backdrops[backdrops.length - 1];
                    latest.style.zIndex = '1063';
                }
            } catch (e) { /* no-op */ }
        });
        bsModal.show();
    };

    // Submit collect payment
    window.submitCollectPayment = async function () {
        const patientId = document.getElementById('collectPaymentPatientId').value;
        const amount = parseFloat(document.getElementById('collectPaymentAmount').value);
        const type = parseInt(document.getElementById('collectPaymentType').value);
        const method = parseInt(document.getElementById('collectPaymentMethod').value);
        const checkNumber = document.getElementById('collectPaymentCheckNumber')?.value;
        const checkDate = document.getElementById('collectPaymentCheckDate')?.value;
        const cardReference = document.getElementById('collectPaymentCardReference')?.value;
        const referenceNumber = document.getElementById('collectPaymentReferenceNumber')?.value;
        const notes = document.getElementById('collectPaymentNotes').value;
        const appointmentId = document.getElementById('collectPaymentAppointmentId').value;
        const paymentDate = document.getElementById('collectPaymentDate')?.value || new Date().toISOString().split('T')[0];

        if (!patientId) {
            showToast('Error', 'Patient is required.', 'error');
            return;
        }
        if (!amount || amount <= 0) {
            showToast('Error', 'Please enter a valid amount.', 'error');
            return;
        }
        if (method === 1 && !checkNumber) {
            showToast('Error', 'Check number is required.', 'error');
            return;
        }

        const btn = document.getElementById('collectPaymentSubmitBtn');
        const originalText = btn.innerHTML;
        btn.disabled = true;
        btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Recording...';

        try {
            const payload = {
                PatientId: parseInt(patientId),
                Amount: amount,
                Type: type,
                Method: method,
                PaymentDate: paymentDate,
                CheckNumber: method === 1 ? checkNumber : null,
                CheckDate: method === 1 && checkDate ? checkDate : null,
                CardReference: (method === 2 || method === 3) ? cardReference : null,
                ReferenceNumber: referenceNumber || null,
                Notes: notes || null,
                AppointmentId: appointmentId ? parseInt(appointmentId) : null
            };

            const resp = await fetch('/api/payments', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'Authorization': `Bearer ${localStorage.getItem('authToken')}`
                },
                body: JSON.stringify(payload)
            });

            if (!resp.ok) throw new Error('Failed to record payment');

            showToast('Success', `Payment of $${amount.toFixed(2)} recorded.`, 'success');
            bootstrap.Modal.getInstance(document.getElementById('collectPaymentModal'))?.hide();

            // Dispatch event so other modules can refresh
            window.dispatchEvent(new CustomEvent('payment:created', { detail: { patientId, amount } }));
        } catch (e) {
            showToast('Error', e.message || 'Failed to record payment.', 'error');
        } finally {
            btn.disabled = false;
            btn.innerHTML = originalText;
        }
    };

    // ============================================
    // COPAY BALANCES TAB
    // ============================================

    let copayBalancesPage = 1;
    let copayBalancesSearch = '';
    let selectedLedgerPatientId = null;

    // Load copay balances when tab is shown
    document.querySelector('[href="#copayBalancesTab"]')?.addEventListener('shown.bs.tab', () => {
        copayBalancesPage = 1;
        loadCopayBalances();
    });

    // Search filter
    let copaySearchTimeout = null;
    document.getElementById('copayBalanceSearch')?.addEventListener('input', function () {
        clearTimeout(copaySearchTimeout);
        copaySearchTimeout = setTimeout(() => {
            copayBalancesSearch = this.value.trim();
            copayBalancesPage = 1;
            loadCopayBalances();
        }, 400);
    });

    async function loadCopayBalances() {
        const tbody = document.querySelector('#copayBalancesTable tbody');
        if (!tbody) return;
        tbody.innerHTML = '<tr><td colspan="4" class="text-center text-muted py-4"><span class="spinner-border spinner-border-sm me-2"></span>Loading...</td></tr>';

        try {
            const resp = await fetch(`/api/payments/copay-balances?search=${encodeURIComponent(copayBalancesSearch)}&page=${copayBalancesPage}&pageSize=25`, {
                headers: { 'Authorization': `Bearer ${localStorage.getItem('authToken')}` }
            });
            if (!resp.ok) throw new Error('Failed to load');
            const data = await resp.json();

            if (!data.Items || data.Items.length === 0) {
                tbody.innerHTML = '<tr><td colspan="4" class="text-center text-muted py-4"><i class="bi bi-check-circle me-2"></i>No patients with outstanding copay balances</td></tr>';
                document.getElementById('copayBalancesPagination').style.display = 'none';
                return;
            }

            tbody.innerHTML = data.Items.map(p => {
                const initials = ((p.FirstName || '')[0] || '') + ((p.LastName || '')[0] || '');
                const profilePic = p.ProfilePicturePath
                    ? `<img src="${p.ProfilePicturePath}" class="rounded-circle me-2" style="width:36px;height:36px;object-fit:cover;">`
                    : `<div class="rounded-circle me-2 d-inline-flex align-items-center justify-content-center" style="width:36px;height:36px;background:#DBEAFE;color:#1B72BE;font-weight:600;font-size:13px;">${initials.toUpperCase()}</div>`;

                return `<tr>
                    <td>
                        <div class="d-flex align-items-center">
                            ${profilePic}
                            <div>
                                <a href="/Patients?id=${p.PatientId}" class="fw-semibold text-decoration-none">${p.PatientName}</a>
                                <div class="text-muted" style="font-size:11px;">MRN: ${p.Mrn || 'N/A'}</div>
                            </div>
                        </div>
                    </td>
                    <td>
                        <div style="font-size:13px;">${p.Email || '<span class="text-muted">—</span>'}</div>
                        <div class="text-muted" style="font-size:12px;">${p.Phone || ''}</div>
                    </td>
                    <td class="text-end">
                        <span class="fw-bold text-danger" style="font-size:16px;">$${p.Balance.toFixed(2)}</span>
                    </td>
                    <td class="text-center">
                        <button class="btn btn-sm btn-primary me-1" onclick="window.openCollectPaymentModal(${p.PatientId}, null, null, '${p.PatientName.replace(/'/g, "\\'")}')">
                            <i class="bi bi-cash-coin me-1"></i>Collect
                        </button>
                        <button class="btn btn-sm btn-outline-secondary" onclick="window.openLedgerModal(${p.PatientId}, '${p.PatientName.replace(/'/g, "\\'")}')">
                            <i class="bi bi-journal-text me-1"></i>Ledger
                        </button>
                    </td>
                </tr>`;
            }).join('');

            // Pagination
            const totalPages = Math.ceil(data.TotalCount / data.PageSize);
            const pagContainer = document.getElementById('copayBalancesPagination');
            if (totalPages > 1 && pagContainer) {
                const start = (data.Page - 1) * data.PageSize + 1;
                const end = Math.min(data.Page * data.PageSize, data.TotalCount);
                pagContainer.innerHTML = `
                    <div class="billing-pagination-info">Showing ${start}-${end} of ${data.TotalCount} | Total Outstanding: <strong class="text-danger">$${data.TotalBalance.toFixed(2)}</strong></div>
                    <div class="billing-pagination-pages">
                        <button class="billing-page-btn" ${data.Page <= 1 ? 'disabled' : ''} onclick="window._copayBalancesPage(${data.Page - 1})"><i class="bi bi-chevron-left"></i></button>
                        <span class="px-2 small">Page ${data.Page} of ${totalPages}</span>
                        <button class="billing-page-btn" ${data.Page >= totalPages ? 'disabled' : ''} onclick="window._copayBalancesPage(${data.Page + 1})"><i class="bi bi-chevron-right"></i></button>
                    </div>`;
                pagContainer.style.display = 'flex';
            } else if (pagContainer) {
                pagContainer.innerHTML = `<div class="billing-pagination-info">Total: ${data.TotalCount} patient(s) | Outstanding: <strong class="text-danger">$${data.TotalBalance.toFixed(2)}</strong></div>`;
                pagContainer.style.display = 'flex';
            }
        } catch (e) {
            tbody.innerHTML = '<tr><td colspan="4" class="text-center text-danger py-4">Failed to load copay balances</td></tr>';
        }
    }

    window._copayBalancesPage = function (page) { copayBalancesPage = page; loadCopayBalances(); };

    // ============================================
    // LEDGER DETAIL MODAL
    // ============================================

    window.openLedgerModal = async function (patientId, patientName) {
        selectedLedgerPatientId = patientId;
        document.getElementById('ledgerModalPatientName').textContent = patientName;

        // Set up Collect Payment button in modal footer
        document.getElementById('ledgerModalCollectBtn').onclick = () => {
            bootstrap.Modal.getInstance(document.getElementById('ledgerDetailModal'))?.hide();
            setTimeout(() => window.openCollectPaymentModal(patientId, null, null, patientName), 300);
        };

        new bootstrap.Modal(document.getElementById('ledgerDetailModal')).show();

        try {
            const [balanceResp, ledgerResp] = await Promise.all([
                fetch(`/api/payments/patient/${patientId}/balance`, {
                    headers: { 'Authorization': `Bearer ${localStorage.getItem('authToken')}` }
                }),
                fetch(`/api/payments/patient/${patientId}/ledger`, {
                    headers: { 'Authorization': `Bearer ${localStorage.getItem('authToken')}` }
                })
            ]);

            if (balanceResp.ok) {
                const balance = await balanceResp.json();
                document.getElementById('ledgerTotalCharges').textContent = '$' + (balance.TotalCharges || 0).toFixed(2);
                document.getElementById('ledgerInsurancePaid').textContent = '$' + (balance.InsurancePayments || 0).toFixed(2);
                document.getElementById('ledgerPatientPaid').textContent = '$' + (balance.PatientPayments || 0).toFixed(2);
                document.getElementById('ledgerBalance').textContent = '$' + (balance.CurrentBalance || 0).toFixed(2);
            }

            if (ledgerResp.ok) {
                const entries = await ledgerResp.json();
                renderLedgerTable(entries);
            }
        } catch (e) {
            showToast('Error', 'Failed to load patient ledger.', 'error');
        }
    };

    function renderLedgerTable(entries) {
        const tbody = document.querySelector('#ledgerTable tbody');
        if (!tbody) return;

        if (!entries || entries.length === 0) {
            tbody.innerHTML = '<tr><td colspan="8" class="text-center text-muted py-4">No ledger entries found</td></tr>';
            return;
        }

        tbody.innerHTML = entries.map(e => {
            const date = new Date(e.Date).toLocaleDateString();
            const typeBadge = e.EntryType === 1 ? '<span class="badge bg-primary-subtle text-primary">Charge</span>'
                : e.EntryType === 2 ? '<span class="badge bg-success-subtle text-success">Payment</span>'
                : e.EntryType === 3 ? '<span class="badge bg-warning-subtle text-warning">Adjustment</span>'
                : '<span class="badge bg-info-subtle text-info">Insurance</span>';
            return `<tr>
                <td>${date}</td>
                <td>${typeBadge}</td>
                <td class="text-primary fw-semibold">${e.CptCode || ''}</td>
                <td>${e.Description || ''}</td>
                <td>${e.ChargeAmount ? '$' + e.ChargeAmount.toFixed(2) : ''}</td>
                <td>${e.PaymentAmount ? '$' + e.PaymentAmount.toFixed(2) : ''}</td>
                <td>${e.AdjustmentAmount ? '$' + e.AdjustmentAmount.toFixed(2) : ''}</td>
                <td class="fw-semibold ${e.RunningBalance > 0 ? 'text-danger' : 'text-success'}">$${e.RunningBalance.toFixed(2)}</td>
            </tr>`;
        }).join('');
    }

    // Refresh copay balances & ledger when a payment is created
    window.addEventListener('payment:created', (e) => {
        loadCopayBalances();
        if (selectedLedgerPatientId && e.detail?.patientId == selectedLedgerPatientId) {
            window.openLedgerModal(selectedLedgerPatientId, document.getElementById('ledgerModalPatientName')?.textContent || '');
        }
    });

    // ============================================
    // SEND COPAY REMINDERS (with confirmation modal)
    // ============================================
    window.sendCopayReminders = function () {
        // Remove existing modal if any
        document.getElementById('confirmCopayReminderModal')?.remove();

        const modalHtml = `
        <div class="modal fade" id="confirmCopayReminderModal" tabindex="-1" data-bs-backdrop="static" data-bs-keyboard="false">
            <div class="modal-dialog modal-dialog-centered">
                <div class="modal-content">
                    <div class="modal-header">
                        <h5 class="modal-title"><i class="bi bi-envelope-fill me-2 text-warning"></i>Send Copay Reminders</h5>
                        <button type="button" class="btn-close" data-bs-dismiss="modal"></button>
                    </div>
                    <div class="modal-body">
                        <p>This will send email reminders to all patients with outstanding copay balances.</p>
                        <p class="text-muted small mb-0">Patients with active installment plans will be excluded.</p>
                    </div>
                    <div class="modal-footer">
                        <button type="button" class="btn btn-lg btn-outline-secondary" data-bs-dismiss="modal">Cancel</button>
                        <button type="button" class="btn btn-lg btn-warning" id="confirmSendRemindersBtn">
                            <i class="bi bi-send-fill me-1"></i>Yes, Send Reminders
                        </button>
                    </div>
                </div>
            </div>
        </div>`;

        document.body.insertAdjacentHTML('beforeend', modalHtml);
        const modal = new bootstrap.Modal(document.getElementById('confirmCopayReminderModal'));
        modal.show();

        document.getElementById('confirmSendRemindersBtn').addEventListener('click', async () => {
            const btn = document.getElementById('confirmSendRemindersBtn');
            btn.disabled = true;
            btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span>Sending...';

            try {
                const resp = await fetch('/api/payments/send-copay-reminders', {
                    method: 'POST',
                    headers: { 'Authorization': `Bearer ${localStorage.getItem('authToken')}` }
                });
                const data = await resp.json();
                modal.hide();
                if (resp.ok) {
                    showToast('Success', data.message || 'Copay reminders sent', 'success');
                } else {
                    showToast('Error', data.message || 'Failed to send reminders', 'error');
                }
            } catch (e) {
                modal.hide();
                showToast('Error', 'Failed to send copay reminders', 'error');
            }
        });

        // Cleanup on close
        document.getElementById('confirmCopayReminderModal').addEventListener('hidden.bs.modal', () => {
            document.getElementById('confirmCopayReminderModal')?.remove();
        });
    };
});
