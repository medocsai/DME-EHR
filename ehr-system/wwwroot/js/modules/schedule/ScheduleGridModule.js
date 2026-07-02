/**
 * ScheduleGridModule
 * ------------------
 * Custom week / day / month schedule. Replaces FullCalendar on /Schedule.
 *
 * Design reference: mocks/schedule-google-style.html
 *
 * Critical correctness rule: ALL hour/minute/day extraction for positioning
 * MUST go through the location's IANA timezone (CLAUDE.md timezone rule).
 * Never call getHours() / getDate() on appointment Date objects — that uses
 * the BROWSER's timezone, which is rarely the clinic's. Use
 * TimezoneUtils.convertUtcToTimezone(date, tzId) instead.
 *
 * Backend untouched: same /api/appointments range, same DTOs, same modals.
 */
(function () {
    'use strict';

    // ===== TUNING =====
    const HOUR_HEIGHT_PX = 72;
    const DAY_START_HOUR = 7;
    const DAY_END_HOUR = 20;            // exclusive
    const HOURS_VISIBLE = DAY_END_HOUR - DAY_START_HOUR;
    const PX_PER_MINUTE = HOUR_HEIGHT_PX / 60;
    const DEFAULT_TZ = 'America/Chicago';

    // AppointmentType enum → CSS class
    const TYPE_CLASS = {
        0: 'sg-t-newpatient', 1: 'sg-t-followup', 2: 'sg-t-physical',
        3: 'sg-t-followup', 4: 'sg-t-consult', 5: 'sg-t-telehealth',
        6: 'sg-t-procedure', 7: 'sg-t-urgent', 8: 'sg-t-labreview',
        9: 'sg-t-medreview', 10: 'sg-t-longevity', 11: 'sg-t-longevity'
    };
    const TYPE_NAMES = {
        0: 'New Patient', 1: 'Follow-Up', 2: 'Annual Physical',
        3: 'Wellness', 4: 'Consultation', 5: 'Telehealth',
        6: 'Procedure', 7: 'Urgent', 8: 'Lab Review',
        9: 'Med Review', 10: 'Longevity', 11: 'Longevity F/U'
    };
    const TYPE_ICONS = {
        0: 'bi-stars', 2: 'bi-heart-pulse', 5: 'bi-camera-video',
        6: 'bi-bandaid', 7: 'bi-exclamation-triangle'
    };

    function escapeHtml(s) {
        if (s === null || s === undefined) return '';
        return String(s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }
    function avatarColorClass(id) {
        const n = Math.abs(parseInt(id, 10) || 0);
        return 'sg-av-c' + ((n % 8) + 1);
    }
    function initials(name) {
        if (!name) return '?';
        return String(name)
            .replace(/^Dr\.?\s+/i, '')
            .trim()
            .split(/\s+/)
            .slice(0, 2)
            .map(w => w[0] ? w[0].toUpperCase() : '')
            .join('') || '?';
    }
    function renderAvatar({ id, name, hasPic, kind, sizeClass }) {
        const sizeCls = sizeClass || '';
        const init = initials(name);
        if (id && hasPic) {
            const url = kind === 'provider'
                ? `/api/providers/${id}/profile-picture`
                : `/api/patients/${id}/profile-picture`;
            const fallback = `'${init.replace(/'/g, '&#39;')}'`;
            return `<span class="sg-av ${sizeCls} ${avatarColorClass(id)}">
                <img src="${url}" alt="" onerror="this.outerHTML=${fallback};">
            </span>`;
        }
        return `<span class="sg-av ${sizeCls} ${avatarColorClass(id)}">${escapeHtml(init)}</span>`;
    }
    function parseServer(dt) {
        if (window.parseServerDateTime) return window.parseServerDateTime(dt);
        if (!dt) return null;
        const hasTz = typeof dt === 'string' &&
            (dt.endsWith('Z') || /[+-]\d{2}:\d{2}$/.test(dt));
        return new Date(hasTz ? dt : dt + 'Z');
    }

    /** Get a YYYY-MM-DD string from a Date in BROWSER local time
     *  (used only for column-date arrays which iterate browser-local calendar days). */
    function isoDate(d) {
        return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
    }

    class ScheduleGridModule {
        constructor() {
            this.container = null;
            this.view = 'week';          // 'week' | 'day' | 'month'
            this.weekStart = null;       // Date; for week/day = Sunday/today; for month = 1st of month
            this.appointments = [];
            this.filters = {
                providerId: null,
                patientId: null,
                typeFilters: new Set(),
                workflowFilters: new Set()
            };
            this._nowTimer = null;
        }

        async init() {
            this.container = document.getElementById('scheduleGrid');
            if (!this.container) return;

            this.weekStart = this._initialAnchor('week');

            this._bindTopbar();
            this._bindFilters();
            this._bindSignalR();

            // Render the appointment-type legend (same global helper used by
            // the old FullCalendar path). Safe no-op if function missing.
            if (typeof window.renderCalendarLegend === 'function') {
                window.renderCalendarLegend();
            }

            await this.loadAndRender();
            this._nowTimer = setInterval(() => this._updateNowLine(), 60000);
        }

        // ============================================================
        // TIMEZONE HELPERS — the heart of the correctness fix
        // ============================================================

        /** The IANA tz for the current location, or default. */
        _locationTz() {
            if (window.getCurrentLocationTimezone) {
                const t = window.getCurrentLocationTimezone();
                if (t && t.timeZoneId) return t.timeZoneId;
            }
            return DEFAULT_TZ;
        }

        /**
         * Get LOCATION-LOCAL { year, month (0-indexed), day, hours, minutes,
         * dateString "YYYY-MM-DD" } for a UTC instant.
         * Critical: never use Date.getHours()/getDate() on appointment times
         * because those are BROWSER local — wrong for any tester not in the
         * clinic's timezone.
         */
        _localParts(utcDate, tzOverride) {
            const tz = tzOverride || this._locationTz();
            if (window.TimezoneUtils && window.TimezoneUtils.convertUtcToTimezone) {
                return window.TimezoneUtils.convertUtcToTimezone(utcDate, tz);
            }
            // Fallback (browser TZ — only used if TimezoneUtils missing)
            return {
                year: utcDate.getFullYear(),
                month: utcDate.getMonth(),
                day: utcDate.getDate(),
                hours: utcDate.getHours(),
                minutes: utcDate.getMinutes(),
                dateString: isoDate(utcDate)
            };
        }

        /** Compute an anchor Date for the given view: Sunday for week, today
         *  for day, 1st of month for month. The Date object stores the
         *  calendar values that ARE the clinic's local date — they're held
         *  in browser-local Date for convenient calendar arithmetic
         *  (setDate / setMonth), but never converted to/from instants. */
        _initialAnchor(view) {
            const tz = this._locationTz();
            const loc = this._localParts(new Date(), tz);  // clinic's today
            const d = new Date(loc.year, loc.month, loc.day);  // browser midnight, but values = clinic date
            if (view === 'week') {
                d.setDate(d.getDate() - d.getDay());      // back to Sunday
            } else if (view === 'month') {
                d.setDate(1);                              // 1st of month
            }
            d.setHours(0, 0, 0, 0);
            return d;
        }

        /** Calendar dates of the visible columns. Used for matching
         *  appointments to columns (no TZ math — pure calendar). */
        _columnDateStrs() {
            const days = this.view === 'day' ? 1 : 7;
            const result = [];
            const start = new Date(this.weekStart);
            for (let i = 0; i < days; i++) {
                const d = new Date(start);
                d.setDate(d.getDate() + i);
                result.push(isoDate(d));
            }
            return result;
        }

        // ============================================================
        // PUBLIC NAVIGATION
        // ============================================================

        async loadAndRender(silent = false) {
            this._showLoading(!silent);
            await this._fetchAppointments();
            this._render();
        }

        prev() {
            const d = new Date(this.weekStart);
            if (this.view === 'day') d.setDate(d.getDate() - 1);
            else if (this.view === 'week') d.setDate(d.getDate() - 7);
            else if (this.view === 'month') d.setMonth(d.getMonth() - 1);
            this.weekStart = d;
            this.loadAndRender();
        }
        next() {
            const d = new Date(this.weekStart);
            if (this.view === 'day') d.setDate(d.getDate() + 1);
            else if (this.view === 'week') d.setDate(d.getDate() + 7);
            else if (this.view === 'month') d.setMonth(d.getMonth() + 1);
            this.weekStart = d;
            this.loadAndRender();
        }
        today() {
            this.weekStart = this._initialAnchor(this.view);
            this.loadAndRender();
        }
        setView(view) {
            if (!['week', 'day', 'month'].includes(view)) return;
            this.view = view;
            this.weekStart = this._initialAnchor(view);
            document.querySelectorAll('#sgViewToggle button').forEach(b => {
                b.classList.toggle('active', b.dataset.view === view);
            });
            this.loadAndRender();
        }

        // ============================================================
        // DATA
        // ============================================================

        async _fetchAppointments() {
            // Compute the location-timezone-bounded UTC range that covers
            // every calendar date we'll display.
            const tz = this._locationTz();
            const cols = this._columnDateStrs();
            let firstDateStr, lastDateStr;
            if (this.view === 'month') {
                // Range covers the full visible month grid (6 weeks). Compute
                // first Sunday of the month grid and the Saturday after.
                const monthGrid = this._monthGridDates();
                firstDateStr = isoDate(monthGrid[0]);
                lastDateStr = isoDate(monthGrid[monthGrid.length - 1]);
            } else {
                firstDateStr = cols[0];
                lastDateStr = cols[cols.length - 1];
            }
            // End of range = midnight after lastDate (exclusive)
            const lastDate = (() => {
                const [y, m, d] = lastDateStr.split('-').map(Number);
                const x = new Date(y, m - 1, d);
                x.setDate(x.getDate() + 1);
                return isoDate(x);
            })();

            let startUtc, endUtc;
            if (window.TimezoneUtils && window.TimezoneUtils.convertLocalToUtc) {
                startUtc = window.TimezoneUtils.convertLocalToUtc(`${firstDateStr}T00:00:00`, tz);
                endUtc = window.TimezoneUtils.convertLocalToUtc(`${lastDate}T00:00:00`, tz);
            } else {
                const [sy, sm, sd] = firstDateStr.split('-').map(Number);
                const [ey, em, ed] = lastDate.split('-').map(Number);
                startUtc = new Date(sy, sm - 1, sd);
                endUtc = new Date(ey, em - 1, ed);
            }

            let url = `/appointments?startDate=${encodeURIComponent(startUtc.toISOString())}&endDate=${encodeURIComponent(endUtc.toISOString())}`;
            const user = ScheduleGridModule._currentUser();
            if (user && user.Role === 2 && user.ProviderId) {
                url += `&providerId=${user.ProviderId}`;
            } else if (this.filters.providerId) {
                url += `&providerId=${this.filters.providerId}`;
            }
            if (this.filters.patientId) url += `&patientId=${this.filters.patientId}`;

            try {
                let data;
                if (window.apiRequest) {
                    data = await window.apiRequest(url, { showLoader: false });
                } else {
                    const r = await fetch('/api' + url, {
                        headers: { 'Authorization': 'Bearer ' + (localStorage.getItem('authToken') || '') }
                    });
                    data = r.ok ? await r.json() : [];
                }
                this.appointments = data || [];
            } catch (err) {
                console.error('[ScheduleGrid] fetch failed', err);
                this.appointments = [];
            }
        }

        // ============================================================
        // RENDER ROUTER
        // ============================================================

        _showLoading(visible) {
            if (!this.container) return;
            if (visible) {
                this.container.innerHTML = `<div class="text-center text-muted py-5" style="grid-column: 1 / -1;">
                    <div class="spinner-border spinner-border-sm me-2"></div>Loading...
                </div>`;
                this.container.style.gridTemplateColumns = '';
                this.container.classList.remove('sg-month');
            }
        }

        _render() {
            if (!this.container) return;
            this._updateRangeLabel();
            if (this.view === 'month') this._renderMonth();
            else this._renderTimeGrid();
        }

        // ============================================================
        // WEEK / DAY (time-grid) RENDER
        // ============================================================

        _renderTimeGrid() {
            this.container.classList.remove('sg-month');
            const days = this.view === 'day' ? 1 : 7;
            const cols = ['60px'];
            for (let i = 0; i < days; i++) cols.push('1fr');
            this.container.style.gridTemplateColumns = cols.join(' ');

            this.container.innerHTML = this._buildStaticHtml(days);
            this._positionAppointments(days);
            this._updateNowLine();
            this._wirePopoverPositioning();
            this._wireCardClicks();
            this._wireEmptyCellClicks();
        }

        _buildStaticHtml(days) {
            const today = new Date();
            const todayStr = isoDate(today);
            let html = '';

            // HEADER ROW
            html += '<div class="sg-dh sg-dh-corner"></div>';
            const colStrs = this._columnDateStrs();
            for (let i = 0; i < days; i++) {
                const d = new Date(this.weekStart);
                d.setDate(d.getDate() + i);
                const isToday = colStrs[i] === todayStr;
                html += `<div class="sg-dh ${isToday ? 'today' : ''}">
                    <div class="sg-dh-name">${ScheduleGridModule._dayName(d)}</div>
                    <div class="sg-dh-num">${d.getDate()}</div>
                </div>`;
            }

            // TIME COLUMN
            html += '<div class="sg-time-col">';
            for (let h = DAY_START_HOUR; h < DAY_END_HOUR; h++) {
                html += `<div class="sg-time-label">${ScheduleGridModule._fmtHour(h)}</div>`;
            }
            html += '</div>';

            // DAY COLUMNS
            for (let i = 0; i < days; i++) {
                const d = new Date(this.weekStart);
                d.setDate(d.getDate() + i);
                const dStr = isoDate(d);
                const isToday = dStr === todayStr;
                html += `<div class="sg-day ${isToday ? 'today' : ''}" data-day-idx="${i}" data-date="${dStr}">`;
                for (let h = 0; h < HOURS_VISIBLE; h++) {
                    html += `<div class="sg-hour-cell" data-hour="${DAY_START_HOUR + h}"></div>`;
                }
                html += '</div>';
            }
            return html;
        }

        _positionAppointments(days) {
            const cols = this._columnDateStrs();
            const visible = this.appointments.filter(a => this._matchesFilters(a));
            const byDay = Array.from({ length: days }, () => []);
            for (const apt of visible) {
                const idx = this._dayIndexFor(apt, cols);
                if (idx >= 0 && idx < days) byDay[idx].push(apt);
            }
            for (let i = 0; i < days; i++) {
                const col = this.container.querySelector(`.sg-day[data-day-idx="${i}"]`);
                if (!col) continue;
                this._renderDayCards(col, byDay[i]);
            }
        }

        _renderDayCards(col, apts) {
            const sorted = apts.slice().sort((a, b) =>
                parseServer(a.StartTime) - parseServer(b.StartTime));
            const groups = [];
            for (const a of sorted) {
                const aStart = parseServer(a.StartTime);
                const aEnd = parseServer(a.EndTime);
                let placed = false;
                for (const g of groups) {
                    if (g.some(e => parseServer(e.StartTime) < aEnd &&
                                    parseServer(e.EndTime) > aStart)) {
                        g.push(a);
                        placed = true;
                        break;
                    }
                }
                if (!placed) groups.push([a]);
            }
            for (const group of groups) {
                if (group.length === 1) {
                    col.appendChild(this._buildCard(group[0], 0, 1));
                } else if (group.length === 2) {
                    col.appendChild(this._buildCard(group[0], 0, 2));
                    col.appendChild(this._buildCard(group[1], 1, 2));
                } else if (group.length === 3) {
                    col.appendChild(this._buildCard(group[0], 0, 3));
                    col.appendChild(this._buildCard(group[1], 1, 3));
                    col.appendChild(this._buildCard(group[2], 2, 3));
                } else {
                    col.appendChild(this._buildCard(group[0], 0, 3));
                    col.appendChild(this._buildCard(group[1], 1, 3));
                    col.appendChild(this._buildMorePill(group.slice(2)));
                }
            }
        }

        _buildCard(apt, slotIdx, slotCount) {
            const start = parseServer(apt.StartTime);
            const end = parseServer(apt.EndTime);
            const tz = apt.TimeZoneId || this._locationTz();

            // CRITICAL: use the location's local hour/minute, not browser's.
            const startLocal = this._localParts(start, tz);
            const startMin = (startLocal.hours - DAY_START_HOUR) * 60 + startLocal.minutes;
            const durMin = Math.max(15, Math.round((end - start) / 60000));

            const top = Math.max(0, startMin * PX_PER_MINUTE);
            const height = durMin * PX_PER_MINUTE;
            const isTall = durMin >= 50;

            const now = new Date();
            const isCancelled = apt.Status === 6;
            const isMissed = apt.Status === 8 ||
                ((apt.Status === 0 || apt.Status === 1) && end < now &&
                 start.toDateString() !== now.toDateString());
            const isRescheduled = !!apt.RescheduledToAppointmentId;

            const typeClass = TYPE_CLASS[apt.Type] || 'sg-t-followup';
            const classes = ['sg-appt', typeClass];
            if (isTall) classes.push('sg-tall');
            if (isMissed) classes.push('sg-is-missed');
            if (isCancelled) classes.push('sg-is-cancelled');
            if (slotCount === 2) classes.push(slotIdx === 0 ? 'sg-col-2-l' : 'sg-col-2-r');
            if (slotCount === 3) classes.push(['sg-col-3-l', 'sg-col-3-m', 'sg-col-3-r'][slotIdx]);

            // Status dot
            let statusDot = '';
            // Status-dot title attributes removed too — the rich popover
            // shows the same status text already. Leaving the dots visible
            // as the color cue without a duplicate browser tooltip.
            if (apt.DocumentationStatus === 1) statusDot = '<span class="sg-status-dot sg-dot-ip"></span>';
            else if (apt.DocumentationStatus === 2 || apt.DocumentationStatus === 3) statusDot = '<span class="sg-status-dot sg-dot-done"></span>';
            else if (apt.IntakeStatus && apt.IntakeStatus.Status === 0) statusDot = '<span class="sg-status-dot sg-dot-miss"></span>';

            const cornerBadge = (() => {
                if (isMissed && isRescheduled) return '<span class="sg-corner-chip sg-resched">RESCH</span>';
                if (isMissed)                  return '<span class="sg-corner-chip">MISSED</span>';
                if (isCancelled)               return '<span class="sg-corner-chip sg-cancel">CANCEL</span>';
                return '';
            })();

            const patientAvatar = renderAvatar({
                id: apt.PatientId,
                name: apt.PatientName,
                hasPic: apt.PatientHasProfilePicture,
                kind: 'patient',
                sizeClass: isTall ? 'sg-av-lg' : ''
            });

            // Time string — prefer server-formatted (already in location TZ)
            const timeStr = apt.StartTimeFormatted && apt.EndTimeFormatted
                ? `${apt.StartTimeFormatted} – ${apt.EndTimeFormatted}`
                : '';
            const typeName = TYPE_NAMES[apt.Type] || '';

            let detailRow = '';
            if (isTall) {
                const provAvatar = renderAvatar({
                    id: apt.ProviderId,
                    name: apt.ProviderName,
                    hasPic: apt.ProviderHasProfilePicture,
                    kind: 'provider',
                    sizeClass: 'sg-av-xs'
                });
                let chip = '';
                if (apt.IntakeStatus && apt.IntakeStatus.Status === 1) {
                    chip = '<span class="sg-a-chip"><i class="bi bi-clipboard-check"></i>Intake</span>';
                } else if (apt.IntakeStatus && apt.IntakeStatus.Status === 0) {
                    chip = '<span class="sg-a-chip" style="background:#FEE2E2;color:#991B1B"><i class="bi bi-x-circle"></i>No intake</span>';
                }
                detailRow = `
                    <div class="sg-a-detail">
                        <div class="sg-a-provider">
                            ${provAvatar}
                            <span class="sg-prov-name">${escapeHtml(apt.ProviderName || '')}</span>
                        </div>
                        <div class="sg-a-chips">${chip}</div>
                    </div>`;
            }

            const el = document.createElement('div');
            el.className = classes.join(' ');
            el.style.top = top + 'px';
            el.style.height = height + 'px';
            el.dataset.appointmentId = apt.AppointmentId;
            // Native browser tooltip disabled — the rich .sg-pop popover
            // replaces it and shows the same info more elegantly.
            // el.title = `${apt.PatientName || ''}\n${apt.ProviderName || ''}\n${typeName}\n${timeStr}`;
            el.innerHTML = `
                ${cornerBadge}
                <div class="sg-a-head">
                    ${patientAvatar}
                    <span class="sg-a-name">${escapeHtml(apt.PatientName || '—')}</span>
                    ${statusDot}
                </div>
                <div class="sg-a-time">${escapeHtml(timeStr)}${typeName && isTall ? ' · ' + escapeHtml(typeName) : ''}</div>
                ${detailRow}
                ${this._buildPopover(apt, isMissed, isCancelled)}
            `;
            return el;
        }

        _buildPopover(apt, isMissed, isCancelled) {
            const start = parseServer(apt.StartTime);
            const end = parseServer(apt.EndTime);
            const durMin = Math.round((end - start) / 60000);
            const timeStr = apt.StartTimeFormatted && apt.EndTimeFormatted
                ? `${apt.StartTimeFormatted} – ${apt.EndTimeFormatted}`
                : '';

            const patientAvatar = renderAvatar({
                id: apt.PatientId,
                name: apt.PatientName,
                hasPic: apt.PatientHasProfilePicture,
                kind: 'patient'
            });

            let dotCls = '';
            if (isMissed) dotCls = 'sg-dot-miss';
            else if (apt.DocumentationStatus === 1) dotCls = 'sg-dot-ip';
            else if (apt.DocumentationStatus === 2 || apt.DocumentationStatus === 3) dotCls = 'sg-dot-done';

            const typeName = TYPE_NAMES[apt.Type] || '';
            const typeIcon = TYPE_ICONS[apt.Type] || '';

            const statusChips = [];
            if (isMissed) statusChips.push('<span class="sg-pop-chip missed">MISSED</span>');
            else if (isCancelled) statusChips.push('<span class="sg-pop-chip cancel">CANCELLED</span>');
            else if (apt.DocumentationStatus === 1) statusChips.push('<span class="sg-pop-chip ip"><i class="bi bi-record-circle"></i>In Progress</span>');
            else if (apt.DocumentationStatus === 2 || apt.DocumentationStatus === 3) statusChips.push('<span class="sg-pop-chip done"><i class="bi bi-check-circle-fill"></i>Completed</span>');
            if (apt.IntakeStatus) {
                const s = apt.IntakeStatus.Status;
                if (s === 0) statusChips.push('<span class="sg-pop-chip intake-bad"><i class="bi bi-x-circle"></i>Intake not submitted</span>');
                else if (s === 1) statusChips.push('<span class="sg-pop-chip intake"><i class="bi bi-clipboard-check"></i>Intake in progress</span>');
                else if (s === 2) statusChips.push('<span class="sg-pop-chip intake"><i class="bi bi-clipboard-check"></i>Intake submitted</span>');
                else if (s === 3) statusChips.push('<span class="sg-pop-chip intake-ok"><i class="bi bi-check2-circle"></i>Intake complete</span>');
            }

            const provAvatar = renderAvatar({
                id: apt.ProviderId,
                name: apt.ProviderName,
                hasPic: apt.ProviderHasProfilePicture,
                kind: 'provider'
            });

            const reasonRow = apt.Reason ? `
                <div class="sg-pop-row">
                    <i class="sg-pop-ico bi bi-chat-square-text"></i>
                    <div class="sg-pop-content">
                        <div class="sg-pop-label">Reason</div>
                        <div class="sg-pop-notes">${escapeHtml(apt.Reason)}</div>
                    </div>
                </div>` : '';
            const phoneRow = apt.PatientPhone ? `
                <div class="sg-pop-row">
                    <i class="sg-pop-ico bi bi-telephone"></i>
                    <div class="sg-pop-content">
                        <div class="sg-pop-label">Patient contact</div>
                        <div class="sg-pop-value">${escapeHtml(apt.PatientPhone)}</div>
                    </div>
                </div>` : '';
            const locationRow = apt.LocationName ? `
                <div class="sg-pop-row">
                    <i class="sg-pop-ico bi bi-geo-alt"></i>
                    <div class="sg-pop-content">
                        <div class="sg-pop-label">Location</div>
                        <div class="sg-pop-value">${escapeHtml(apt.LocationName)}</div>
                    </div>
                </div>` : '';

            return `
                <div class="sg-pop">
                    <div class="sg-pop-header">
                        ${patientAvatar}
                        <div class="sg-pop-id">
                            <div class="sg-pop-name">${escapeHtml(apt.PatientName || '—')}</div>
                            <div class="sg-pop-mrn">${escapeHtml(apt.PatientMRN || '')}</div>
                        </div>
                        ${dotCls ? `<span class="sg-pop-status-dot ${dotCls}"></span>` : ''}
                    </div>
                    <div class="sg-pop-body">
                        <div class="sg-pop-time">
                            <i class="bi bi-clock"></i>
                            ${escapeHtml(timeStr)}
                            <span class="sg-pop-duration">· ${durMin} min</span>
                        </div>
                        ${typeName ? `<span class="sg-pop-type">${typeIcon ? '<i class="bi ' + typeIcon + '"></i> ' : ''}${escapeHtml(typeName)}</span>` : ''}
                        ${statusChips.length ? `
                            <div class="sg-pop-row">
                                <i class="sg-pop-ico bi bi-activity"></i>
                                <div class="sg-pop-content">
                                    <div class="sg-pop-label">Status</div>
                                    <div class="sg-pop-value sg-pop-chips">${statusChips.join('')}</div>
                                </div>
                            </div>` : ''}
                        <div class="sg-pop-row">
                            <i class="sg-pop-ico bi bi-person-badge"></i>
                            <div class="sg-pop-content">
                                <div class="sg-pop-label">Provider</div>
                                <div class="sg-pop-value">
                                    ${provAvatar}
                                    <span>${escapeHtml(apt.ProviderName || '—')}</span>
                                </div>
                            </div>
                        </div>
                        ${locationRow}
                        ${phoneRow}
                        ${reasonRow}
                    </div>
                </div>`;
        }

        _buildMorePill(hiddenApts) {
            const first = hiddenApts[0];
            const startUtc = parseServer(first.StartTime);
            const tz = first.TimeZoneId || this._locationTz();
            const startLocal = this._localParts(startUtc, tz);
            const startMin = (startLocal.hours - DAY_START_HOUR) * 60 + startLocal.minutes;
            const top = Math.max(0, startMin * PX_PER_MINUTE);

            const maxDur = hiddenApts.reduce((m, a) => {
                const d = (parseServer(a.EndTime) - parseServer(a.StartTime)) / 60000;
                return Math.max(m, d);
            }, 30);
            const height = Math.max(36, Math.min(maxDur, 90) * PX_PER_MINUTE);

            const avatars = hiddenApts.slice(0, 4).map(a => `
                <span class="sg-av ${avatarColorClass(a.PatientId)}">${escapeHtml(initials(a.PatientName).charAt(0))}</span>
            `).join('');

            // Hover popover lists every hidden appointment. Each row is
            // clickable to open that appointment's detail modal.
            const rowsHtml = hiddenApts.map(a => this._buildMoreRow(a)).join('');
            const headerLabel = hiddenApts.length === 1
                ? '1 more appointment at this time'
                : `${hiddenApts.length} more appointments at this time`;

            const el = document.createElement('div');
            el.className = 'sg-more sg-col-3-r';
            el.style.top = top + 'px';
            el.style.height = height + 'px';
            el.innerHTML = `
                +${hiddenApts.length}
                <div class="sg-av-stack">${avatars}</div>
                <div class="sg-more-pop">
                    <div class="sg-mp-header">${escapeHtml(headerLabel)}</div>
                    <div class="sg-mp-list">${rowsHtml}</div>
                </div>
            `;
            // Click a list row → open that specific appointment's detail
            el.querySelectorAll('.sg-mp-row').forEach(row => {
                row.addEventListener('click', (e) => {
                    e.stopPropagation();
                    const id = parseInt(row.dataset.appointmentId, 10);
                    if (id) this._openAppointment(id);
                });
            });
            // Clicking the pill itself (outside any row) still falls back to
            // opening the first hidden appointment — preserves the old
            // single-click behaviour for muscle memory.
            el.addEventListener('click', (e) => {
                if (e.target.closest('.sg-mp-row') || e.target.closest('.sg-more-pop')) return;
                e.stopPropagation();
                this._openAppointment(hiddenApts[0].AppointmentId);
            });
            return el;
        }

        /** Render one row inside the "+N more" hover popover. */
        _buildMoreRow(apt) {
            const typeName = TYPE_NAMES[apt.Type] || '';
            const typeClass = TYPE_CLASS[apt.Type] || 'sg-t-followup';
            const now = new Date();
            const start = parseServer(apt.StartTime);
            const end = parseServer(apt.EndTime);
            const isCancelled = apt.Status === 6;
            const isMissed = apt.Status === 8 ||
                ((apt.Status === 0 || apt.Status === 1) && end < now &&
                 start.toDateString() !== now.toDateString());

            const cls = ['sg-mp-row', typeClass];
            if (isMissed) cls.push('is-missed');

            let tag = '';
            if (isMissed)        tag = '<span class="sg-mp-tag missed">MISSED</span>';
            else if (isCancelled) tag = '<span class="sg-mp-tag cancel">CANCEL</span>';

            const avatar = renderAvatar({
                id: apt.PatientId,
                name: apt.PatientName,
                hasPic: apt.PatientHasProfilePicture,
                kind: 'patient'
            });

            const timeStr = apt.StartTimeFormatted && apt.EndTimeFormatted
                ? `${apt.StartTimeFormatted} – ${apt.EndTimeFormatted}`
                : (apt.StartTimeFormatted || '');

            return `
                <div class="${cls.join(' ')}" data-appointment-id="${apt.AppointmentId}">
                    ${avatar}
                    <div class="sg-mp-info">
                        <div class="sg-mp-name">${escapeHtml(apt.PatientName || '—')}</div>
                        <div class="sg-mp-meta">${escapeHtml(timeStr)}${typeName ? ' · ' + escapeHtml(typeName) : ''}</div>
                    </div>
                    ${tag}
                </div>`;
        }

        // ============================================================
        // MONTH VIEW RENDER
        // ============================================================

        _monthGridDates() {
            // Returns 42 Date objects (6 weeks) starting from the Sunday on
            // or before the 1st of the month being viewed.
            const start = new Date(this.weekStart);   // 1st of month
            const firstSun = new Date(start);
            firstSun.setDate(firstSun.getDate() - firstSun.getDay());
            const dates = [];
            for (let i = 0; i < 42; i++) {
                const d = new Date(firstSun);
                d.setDate(d.getDate() + i);
                dates.push(d);
            }
            return dates;
        }

        _renderMonth() {
            this.container.classList.add('sg-month');
            this.container.style.gridTemplateColumns = '';

            const today = new Date();
            const todayStr = isoDate(today);
            const currentMonth = this.weekStart.getMonth();

            // Day-of-week headers
            const dows = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
            let html = dows.map(d => `<div class="sg-month-dow">${d}</div>`).join('');

            // Group appointments by location-local date string
            const visible = this.appointments.filter(a => this._matchesFilters(a));
            const byDate = {};
            for (const a of visible) {
                const tz = a.TimeZoneId || this._locationTz();
                const loc = this._localParts(parseServer(a.StartTime), tz);
                const key = loc.dateString;
                (byDate[key] = byDate[key] || []).push(a);
            }

            // 42 cells
            for (const d of this._monthGridDates()) {
                const dStr = isoDate(d);
                const outside = d.getMonth() !== currentMonth;
                const isToday = dStr === todayStr;
                const appts = (byDate[dStr] || []).sort((a, b) =>
                    parseServer(a.StartTime) - parseServer(b.StartTime));

                const visibleChips = appts.slice(0, 3);
                const overflow = appts.length - visibleChips.length;

                const chipsHtml = visibleChips.map(a => this._buildMonthChip(a)).join('');
                const moreHtml = overflow > 0
                    ? `<div class="sg-month-more" data-date="${dStr}">+${overflow} more</div>`
                    : '';

                html += `
                    <div class="sg-month-cell ${outside ? 'outside' : ''} ${isToday ? 'today' : ''}" data-date="${dStr}">
                        <div class="sg-month-day">${d.getDate()}</div>
                        ${chipsHtml}
                        ${moreHtml}
                    </div>`;
            }
            this.container.innerHTML = html;
            this._wireMonthClicks();
        }

        _buildMonthChip(apt) {
            const typeClass = TYPE_CLASS[apt.Type] || 'sg-t-followup';
            const isCancelled = apt.Status === 6;
            const start = parseServer(apt.StartTime);
            const end = parseServer(apt.EndTime);
            const now = new Date();
            const isMissed = apt.Status === 8 ||
                ((apt.Status === 0 || apt.Status === 1) && end < now &&
                 start.toDateString() !== now.toDateString());
            const cls = ['sg-month-chip', typeClass];
            if (isMissed) cls.push('sg-is-missed');
            if (isCancelled) cls.push('sg-is-cancelled');

            // Short time like "8:30 AM" — strip the end time and timezone
            const t = (apt.StartTimeFormatted || '').replace(/\s+[A-Z]{2,4}$/, '');
            return `
                <div class="${cls.join(' ')}" data-appointment-id="${apt.AppointmentId}" title="${escapeHtml(apt.PatientName || '')} · ${escapeHtml(apt.StartTimeFormatted || '')}">
                    <span class="sg-month-chip-time">${escapeHtml(t)}</span>
                    <span class="sg-month-chip-name">${escapeHtml(apt.PatientName || '—')}</span>
                </div>`;
        }

        _wireMonthClicks() {
            this.container.querySelectorAll('.sg-month-chip').forEach(c => {
                c.addEventListener('click', (e) => {
                    e.stopPropagation();
                    const id = parseInt(c.dataset.appointmentId, 10);
                    if (id) this._openAppointment(id);
                });
            });
            this.container.querySelectorAll('.sg-month-more').forEach(m => {
                m.addEventListener('click', (e) => {
                    e.stopPropagation();
                    // Switch to day view for this date
                    const [y, mo, d] = m.dataset.date.split('-').map(Number);
                    const date = new Date(y, mo - 1, d);
                    date.setHours(0, 0, 0, 0);
                    this.view = 'day';
                    this.weekStart = date;
                    document.querySelectorAll('#sgViewToggle button').forEach(b => {
                        b.classList.toggle('active', b.dataset.view === 'day');
                    });
                    this.loadAndRender();
                });
            });
            this.container.querySelectorAll('.sg-month-cell').forEach(c => {
                c.addEventListener('click', (e) => {
                    if (e.target.closest('.sg-month-chip')) return;
                    if (e.target.closest('.sg-month-more')) return;
                    const dStr = c.dataset.date;
                    if (!dStr) return;
                    const [y, mo, d] = dStr.split('-').map(Number);
                    const start = new Date(y, mo - 1, d, 9, 0, 0);
                    const end = new Date(y, mo - 1, d, 10, 0, 0);
                    const apptModule = (window.App && window.App.modules && window.App.modules.get('appointments'))
                        || window.appointmentModule;
                    if (apptModule && apptModule.openNewAppointment) {
                        apptModule.openNewAppointment(start, end);
                    } else if (typeof openNewAppointment === 'function') {
                        openNewAppointment(start, end);
                    }
                });
            });
        }

        // ============================================================
        // NOW LINE (week / day only)
        // ============================================================

        _updateNowLine() {
            if (!this.container || this.view === 'month') return;
            this.container.querySelectorAll('.sg-now-line').forEach(n => n.remove());
            const now = new Date();
            const todayStr = isoDate(now);
            const tz = this._locationTz();
            const nowLocal = this._localParts(now, tz);
            if (nowLocal.hours < DAY_START_HOUR || nowLocal.hours >= DAY_END_HOUR) return;

            const minutes = (nowLocal.hours - DAY_START_HOUR) * 60 + nowLocal.minutes;
            const top = minutes * PX_PER_MINUTE;

            const days = this.view === 'day' ? 1 : 7;
            const cols = this._columnDateStrs();
            for (let i = 0; i < days; i++) {
                if (cols[i] !== todayStr) continue;
                const col = this.container.querySelector(`.sg-day[data-day-idx="${i}"]`);
                if (!col) continue;
                const line = document.createElement('div');
                line.className = 'sg-now-line';
                line.style.top = top + 'px';
                col.appendChild(line);
            }
        }

        // ============================================================
        // POPOVER POSITIONING — smart 4-direction
        // ============================================================

        /**
         * Smart popover positioning. On mouseenter:
         *   1. Measure the popover's actual rendered height (works because
         *      visibility:hidden still produces layout in CSS).
         *   2. Pick the side (right > left > below > above) that has the most
         *      space, accounting for popover dimensions.
         *   3. Position the popover so it ALWAYS fits inside the viewport,
         *      regardless of card position. For right/left placement, clamp
         *      vertical position. For above/below, clamp horizontal position.
         */
        /**
         * Smart popover positioning, viewport- AND sidebar-aware.
         *
         * Safe horizontal area = the .sg-card element's bounding box. This
         * automatically excludes the sidebar on the left and any whitespace
         * past the grid on the right. Without this, "open left" on a Sunday
         * card would put the popover BEHIND the sidebar.
         *
         * Safe vertical area = the viewport (sticky headers might overlap
         * but z-index 9999 keeps popover above them).
         */
        _wirePopoverPositioning() {
            const MARGIN = 12, GUTTER = 10;
            const safeEl = this.container.closest('.sg-card') || document.body;

            // Shared smart-positioning routine. Used by both .sg-appt (single
            // appointment popovers, 320 px wide) and .sg-more (overflow list
            // popovers, 280 px wide). Same edge-clamping logic for both.
            const wire = (host, pop, popWidth) => {
                host.addEventListener('mouseenter', () => {
                    pop.classList.remove('sg-pop-right', 'sg-pop-left', 'sg-pop-above', 'sg-pop-below');
                    pop.style.top = pop.style.left = pop.style.right = pop.style.bottom = '';

                    const popH = pop.offsetHeight || 360;
                    const r = host.getBoundingClientRect();
                    const safe = safeEl.getBoundingClientRect();
                    const vh = window.innerHeight;

                    const roomRight = safe.right - r.right;
                    const roomLeft  = r.left - safe.left;
                    const roomBelow = vh - r.bottom;
                    const roomAbove = r.top;

                    let side;
                    if (roomRight >= popWidth + GUTTER)     side = 'right';
                    else if (roomLeft >= popWidth + GUTTER) side = 'left';
                    else if (roomBelow >= popH + GUTTER)    side = 'below';
                    else if (roomAbove >= popH + GUTTER)    side = 'above';
                    else {
                        const max = Math.max(roomRight, roomLeft, roomBelow, roomAbove);
                        if (max === roomRight) side = 'right';
                        else if (max === roomLeft) side = 'left';
                        else if (max === roomBelow) side = 'below';
                        else side = 'above';
                    }

                    if (side === 'right' || side === 'left') {
                        pop.classList.add(side === 'right' ? 'sg-pop-right' : 'sg-pop-left');
                        let vpTop = r.top - 8;
                        if (vpTop + popH > vh - MARGIN) vpTop = vh - popH - MARGIN;
                        if (vpTop < MARGIN) vpTop = MARGIN;
                        pop.style.top = (vpTop - r.top) + 'px';
                    } else {
                        pop.classList.add(side === 'below' ? 'sg-pop-below' : 'sg-pop-above');
                        let vpLeft = r.left;
                        if (vpLeft + popWidth > safe.right - MARGIN) vpLeft = safe.right - popWidth - MARGIN;
                        if (vpLeft < safe.left + MARGIN) vpLeft = safe.left + MARGIN;
                        pop.style.left = (vpLeft - r.left) + 'px';
                    }
                });
            };

            this.container.querySelectorAll('.sg-appt').forEach(card => {
                const pop = card.querySelector('.sg-pop');
                if (pop) wire(card, pop, 320);
            });
            this.container.querySelectorAll('.sg-more').forEach(pill => {
                const pop = pill.querySelector('.sg-more-pop');
                if (pop) wire(pill, pop, 280);
            });
        }

        // ============================================================
        // CLICK HANDLERS
        // ============================================================

        _wireCardClicks() {
            this.container.querySelectorAll('.sg-appt').forEach(card => {
                card.addEventListener('click', (e) => {
                    if (e.target.closest('.sg-pop')) return;
                    const id = parseInt(card.dataset.appointmentId, 10);
                    if (id) this._openAppointment(id);
                });
            });
        }
        _wireEmptyCellClicks() {
            this.container.querySelectorAll('.sg-hour-cell').forEach(cell => {
                cell.addEventListener('click', () => {
                    const dayCol = cell.parentElement;
                    if (!dayCol) return;
                    const dateStr = dayCol.dataset.date;
                    const hour = parseInt(cell.dataset.hour, 10);
                    if (!dateStr || isNaN(hour)) return;
                    const [y, m, d] = dateStr.split('-').map(Number);
                    const start = new Date(y, m - 1, d, hour, 0, 0);
                    const end = new Date(y, m - 1, d, hour + 1, 0, 0);
                    const apptModule = (window.App && window.App.modules && window.App.modules.get('appointments'))
                        || window.appointmentModule;
                    if (apptModule && apptModule.openNewAppointment) {
                        apptModule.openNewAppointment(start, end);
                    } else if (typeof openNewAppointment === 'function') {
                        openNewAppointment(start, end);
                    }
                });
            });
        }
        _openAppointment(id) {
            if (typeof openAppointmentDetails === 'function') {
                openAppointmentDetails(id);
                return;
            }
            const apptModule = (window.App && window.App.modules && window.App.modules.get('appointments'))
                || window.appointmentModule;
            if (apptModule && apptModule.openDetails) apptModule.openDetails(id);
        }

        // ============================================================
        // FILTERS
        // ============================================================

        _matchesFilters(apt) {
            const f = this.filters;
            if (f.providerId && apt.ProviderId !== f.providerId) return false;
            if (f.patientId && apt.PatientId !== f.patientId) return false;

            if (f.typeFilters && f.typeFilters.size > 0) {
                const isMissed = apt.Status === 8 ||
                    ((apt.Status === 0 || apt.Status === 1) &&
                     parseServer(apt.EndTime) < new Date() &&
                     parseServer(apt.StartTime).toDateString() !== new Date().toDateString());
                const isCancelled = apt.Status === 6;
                const isResch = !!apt.RescheduledToAppointmentId;
                let matched = false;
                if (f.typeFilters.has(String(apt.Type))) matched = true;
                if (!matched && f.typeFilters.has('missed') && isMissed && !isResch) matched = true;
                if (!matched && f.typeFilters.has('missedRescheduled') && isMissed && isResch) matched = true;
                if (!matched && f.typeFilters.has('cancelled') && isCancelled) matched = true;
                if (!matched) return false;
            }

            if (f.workflowFilters && f.workflowFilters.size > 0) {
                let matched = false;
                if (f.workflowFilters.has('inProgress') && apt.DocumentationStatus === 1) matched = true;
                if (f.workflowFilters.has('completed') &&
                    (apt.DocumentationStatus === 2 || apt.DocumentationStatus === 3)) matched = true;
                if (!matched) return false;
            }
            return true;
        }
        _bindFilters() {
            const rerender = () => {
                if (!this.container) return;
                if (this.view === 'month') {
                    this._renderMonth();
                } else {
                    this.container.querySelectorAll('.sg-appt, .sg-more, .sg-now-line').forEach(n => n.remove());
                    this._positionAppointments(this.view === 'day' ? 1 : 7);
                    this._updateNowLine();
                    this._wirePopoverPositioning();
                    this._wireCardClicks();
                }
            };
            document.querySelectorAll('.type-filter').forEach(cb => {
                cb.addEventListener('change', () => {
                    this.filters.typeFilters = new Set(
                        Array.from(document.querySelectorAll('.type-filter:checked')).map(c => c.value));
                    rerender();
                });
            });
            document.querySelectorAll('.workflow-filter').forEach(cb => {
                cb.addEventListener('change', () => {
                    this.filters.workflowFilters = new Set(
                        Array.from(document.querySelectorAll('.workflow-filter:checked')).map(c => c.value));
                    rerender();
                });
            });
            const clearBtn = document.getElementById('clearScheduleFilters');
            if (clearBtn) {
                clearBtn.addEventListener('click', () => setTimeout(() => {
                    this.filters.typeFilters.clear();
                    this.filters.workflowFilters.clear();
                    this.filters.patientId = null;
                    this.filters.providerId = null;
                    this.loadAndRender();
                }, 50));
            }
        }

        // ============================================================
        // TOPBAR / DAY INDEX / RANGE LABEL
        // ============================================================

        _bindTopbar() {
            const prev = document.getElementById('sgPrev');
            const next = document.getElementById('sgNext');
            const today = document.getElementById('sgToday');
            if (prev) prev.addEventListener('click', () => this.prev());
            if (next) next.addEventListener('click', () => this.next());
            if (today) today.addEventListener('click', () => this.today());
            document.querySelectorAll('#sgViewToggle button').forEach(b => {
                b.addEventListener('click', () => this.setView(b.dataset.view));
            });
        }

        _updateRangeLabel() {
            const el = document.getElementById('sgRangeLabel');
            if (!el) return;
            if (this.view === 'month') {
                el.textContent = this.weekStart.toLocaleDateString('en-US', { month: 'long', year: 'numeric' });
                return;
            }
            const start = new Date(this.weekStart);
            const days = this.view === 'day' ? 1 : 7;
            const end = new Date(start);
            end.setDate(end.getDate() + days - 1);
            const fmt = (d) => d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
            el.textContent = days === 1
                ? start.toLocaleDateString('en-US', { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric' })
                : `${fmt(start)} – ${fmt(end)}, ${end.getFullYear()}`;
        }

        _bindSignalR() {
            if (window.scheduleSignalRService && window.scheduleSignalRService.onUpdate) {
                window.scheduleSignalRService.onUpdate(() => this.loadAndRender(true));
            }
        }

        _dayIndexFor(apt, columnDates) {
            const tz = apt.TimeZoneId || this._locationTz();
            const aptDateStr = this._localParts(parseServer(apt.StartTime), tz).dateString;
            return columnDates.indexOf(aptDateStr);
        }

        // ============================================================
        // STATIC HELPERS
        // ============================================================

        static _currentUser() {
            try { return JSON.parse(localStorage.getItem('currentUser') || '{}'); }
            catch { return {}; }
        }
        static _dayName(d) {
            return d.toLocaleDateString('en-US', { weekday: 'short' });
        }
        static _fmtHour(h) {
            const ampm = h >= 12 ? 'PM' : 'AM';
            const h12 = h % 12 || 12;
            return `${h12} ${ampm}`;
        }
    }

    // Auto-init on Schedule page
    function initWhenReady() {
        const grid = document.getElementById('scheduleGrid');
        if (!grid) return;
        const authed = !!localStorage.getItem('authToken');
        if (!authed) { setTimeout(initWhenReady, 200); return; }
        if (window.scheduleGridModule) { window.scheduleGridModule.init(); return; }
        window.scheduleGridModule = new ScheduleGridModule();
        if (window.App && window.App.modules && !window.App.modules.has('scheduleGrid')) {
            window.App.modules.register('scheduleGrid', window.scheduleGridModule);
        }
        window.scheduleGridModule.init();
    }
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initWhenReady);
    } else {
        initWhenReady();
    }
    window.ScheduleGridModule = ScheduleGridModule;
})();
