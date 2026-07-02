/**
 * TimezoneUtils - Timezone handling for multi-location support
 * All times are stored in UTC on server, converted to location timezone for display
 *
 * Usage:
 *   TimezoneUtils.convertUtcToTimezone(date, 'America/Chicago');
 *   TimezoneUtils.formatTimeInTimezone(appointment);
 *   TimezoneUtils.getCurrentLocationTimezone();
 */
const TimezoneUtils = {
    /**
     * Get the current location's timezone info
     * @returns {Object} { timeZoneId: string, timeZoneAbbreviation: string }
     */
    getCurrentLocationTimezone() {
        // Check App state first
        if (window.App?.state) {
            const location = App.state.get('currentLocation');
            if (location?.TimeZoneId) {
                return {
                    timeZoneId: location.TimeZoneId,
                    timeZoneAbbreviation: location.TimeZoneAbbreviation || 'ET'
                };
            }
        }

        // Check localStorage
        const stored = localStorage.getItem('currentLocationTimezone');
        if (stored) {
            try {
                return JSON.parse(stored);
            } catch (e) {
                console.error('Error parsing stored timezone:', e);
            }
        }

        // Default to Central Time
        return { timeZoneId: 'America/Chicago', timeZoneAbbreviation: 'CT' };
    },

    /**
     * Store the current location's timezone info
     * @param {string} timeZoneId - IANA timezone identifier
     * @param {string} timeZoneAbbreviation - Short timezone abbreviation
     */
    setCurrentLocationTimezone(timeZoneId, timeZoneAbbreviation) {
        const tzInfo = { timeZoneId, timeZoneAbbreviation };
        localStorage.setItem('currentLocationTimezone', JSON.stringify(tzInfo));
    },

    /**
     * Convert a UTC datetime to a specific IANA timezone
     * @param {Date|string} utcDateTime - Date object or ISO string in UTC
     * @param {string} ianaTimeZoneId - IANA timezone identifier
     * @returns {Object} { year, month, day, hours, minutes, dateString, timeString, isoString }
     */
    convertUtcToTimezone(utcDateTime, ianaTimeZoneId) {
        const date = typeof utcDateTime === 'string' ? DateUtils.parseServerDateTime(utcDateTime) : utcDateTime;

        if (!date || !ianaTimeZoneId) {
            // Fallback to browser local time
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

        // Use Intl.DateTimeFormat to convert UTC to target timezone
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
        const month = parseInt(getPart('month')) - 1; // JS months are 0-indexed
        const day = parseInt(getPart('day'));
        const hours = parseInt(getPart('hour'));
        const minutes = parseInt(getPart('minute'));

        const dateString = `${year}-${String(month + 1).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
        const timeString = `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}:00`;
        const isoString = `${dateString}T${timeString}`;

        return {
            year,
            month,
            day,
            hours,
            minutes,
            dateString,
            timeString,
            isoString
        };
    },

    /**
     * Convert a local datetime in a specific timezone to UTC
     * @param {string} localDateTimeStr - DateTime string "YYYY-MM-DDTHH:mm:ss"
     * @param {string} ianaTimeZoneId - IANA timezone identifier
     * @returns {Date} Date object in UTC
     */
    convertLocalToUtc(localDateTimeStr, ianaTimeZoneId) {
        if (!localDateTimeStr || !ianaTimeZoneId) {
            return new Date(localDateTimeStr);
        }

        const [datePart, timePart] = localDateTimeStr.split('T');
        const [year, month, day] = datePart.split('-').map(Number);
        const [hours, minutes, seconds = 0] = (timePart || '00:00:00').split(':').map(Number);

        const formatter = new Intl.DateTimeFormat('en-US', {
            timeZone: ianaTimeZoneId,
            year: 'numeric',
            month: '2-digit',
            day: '2-digit',
            hour: '2-digit',
            minute: '2-digit',
            second: '2-digit',
            hour12: false
        });

        const parseFormattedDate = (utcMs) => {
            const parts = formatter.formatToParts(new Date(utcMs));
            const getPart = (type) => parts.find(p => p.type === type)?.value || '0';
            return {
                year: parseInt(getPart('year')),
                month: parseInt(getPart('month')),
                day: parseInt(getPart('day')),
                hour: parseInt(getPart('hour')),
                minute: parseInt(getPart('minute'))
            };
        };

        const compareToTarget = (utcMs) => {
            const tz = parseFormattedDate(utcMs);
            if (tz.year !== year) return year - tz.year;
            if (tz.month !== month) return month - tz.month;
            if (tz.day !== day) return day - tz.day;
            if (tz.hour !== hours) return hours - tz.hour;
            if (tz.minute !== minutes) return minutes - tz.minute;
            return 0;
        };

        const initialGuess = Date.UTC(year, month - 1, day, hours, minutes, seconds);
        let bestGuess = initialGuess;
        let bestDiff = Math.abs(compareToTarget(initialGuess));

        // Coarse search
        for (let offsetHours = -14; offsetHours <= 12; offsetHours++) {
            const guess = initialGuess - (offsetHours * 3600000);
            const diff = compareToTarget(guess);
            if (diff === 0) return new Date(guess);
            if (Math.abs(diff) < bestDiff) {
                bestDiff = Math.abs(diff);
                bestGuess = guess;
            }
        }

        // Fine-tune
        for (let i = 0; i < 5; i++) {
            const diff = compareToTarget(bestGuess);
            if (diff === 0) break;
            bestGuess += diff * 60000;
        }

        return new Date(bestGuess);
    },

    /**
     * Format time with timezone for display (e.g., "9:00 AM EST")
     * @param {Object} appointment - Appointment with StartTime, TimeZoneId, TimeZoneAbbreviation
     * @returns {string} Formatted time with timezone
     */
    formatTimeWithTimezone(appointment) {
        if (appointment.StartTimeFormatted) {
            return appointment.StartTimeFormatted;
        }

        const locationTzId = appointment.TimeZoneId || this.getCurrentLocationTimezone().timeZoneId;
        const tz = appointment.TimeZoneAbbreviation || this.getCurrentLocationTimezone().timeZoneAbbreviation;

        const date = DateUtils.parseServerDateTime(appointment.StartTime);
        if (!date || isNaN(date.getTime())) return '-';

        const timeStr = date.toLocaleTimeString('en-US', {
            timeZone: locationTzId,
            hour: 'numeric',
            minute: '2-digit'
        });
        return `${timeStr} ${tz}`;
    },

    /**
     * Format a time range with timezone (e.g., "9:00 AM - 9:45 AM EST")
     * @param {Object} appointment - Appointment with StartTime, EndTime, TimeZone info
     * @returns {string} Formatted time range
     */
    formatTimeRangeWithTimezone(appointment) {
        const locationTzId = appointment.TimeZoneId || this.getCurrentLocationTimezone().timeZoneId;
        const tz = appointment.TimeZoneAbbreviation || this.getCurrentLocationTimezone().timeZoneAbbreviation;

        if (appointment.StartTimeFormatted && appointment.EndTimeFormatted) {
            const tzPattern = /\s+(EST|EDT|CST|CDT|MST|MDT|PST|PDT|ET|CT|MT|PT|AKT|HST|AST|PKT|GMT|UTC[+-]?\d*:?\d*)$/i;
            const endTime = appointment.EndTimeFormatted.replace(tzPattern, '');
            const startTime = appointment.StartTimeFormatted.replace(tzPattern, '');
            return `${startTime} - ${endTime} ${tz}`;
        }

        const startDate = DateUtils.parseServerDateTime(appointment.StartTime);
        const endDate = DateUtils.parseServerDateTime(appointment.EndTime);
        if (!startDate || !endDate) return '-';

        const options = { timeZone: locationTzId, hour: 'numeric', minute: '2-digit' };
        const startTime = startDate.toLocaleTimeString('en-US', options);
        const endTime = endDate.toLocaleTimeString('en-US', options);
        return `${startTime} - ${endTime} ${tz}`;
    },

    /**
     * Format a date in location's timezone
     * @param {Object} appointment - Appointment with StartTime, DateFormatted, TimeZoneId
     * @returns {string} Formatted date
     */
    formatDateWithTimezone(appointment) {
        if (appointment.DateFormatted) {
            return appointment.DateFormatted;
        }

        const locationTzId = appointment.TimeZoneId || this.getCurrentLocationTimezone().timeZoneId;
        const date = DateUtils.parseServerDateTime(appointment.StartTime);
        if (!date || isNaN(date.getTime())) return '-';

        return date.toLocaleDateString('en-US', {
            timeZone: locationTzId,
            month: 'short',
            day: 'numeric',
            year: 'numeric'
        });
    },

    /**
     * Format full datetime with timezone
     * @param {Object} appointment - Appointment object
     * @returns {string} Formatted datetime
     */
    formatDateTimeWithTimezone(appointment) {
        return `${this.formatDateWithTimezone(appointment)} ${this.formatTimeWithTimezone(appointment)}`;
    },

    /**
     * Format a slot time with timezone
     * @param {Object} slot - Schedule slot object
     * @returns {string} Formatted time
     */
    formatSlotTime(slot) {
        if (slot.StartTimeWithTimezone) {
            return slot.StartTimeWithTimezone;
        }
        if (slot.StartTimeFormatted) {
            const tz = slot.TimeZoneAbbreviation || this.getCurrentLocationTimezone().timeZoneAbbreviation;
            return `${slot.StartTimeFormatted} ${tz}`;
        }

        const locationTzId = slot.TimeZoneId || this.getCurrentLocationTimezone().timeZoneId;
        const tz = slot.TimeZoneAbbreviation || this.getCurrentLocationTimezone().timeZoneAbbreviation;
        const date = DateUtils.parseServerDateTime(slot.StartTime);
        if (!date) return '-';

        const timeStr = date.toLocaleTimeString('en-US', {
            timeZone: locationTzId,
            hour: 'numeric',
            minute: '2-digit'
        });
        return `${timeStr} ${tz}`;
    },

    /**
     * Get appointment date in location's timezone (for calendar)
     * @param {Object} appointment - Appointment object
     * @returns {string} Date in YYYY-MM-DD format
     */
    getAppointmentDateInLocationTimezone(appointment) {
        if (appointment.DateFormatted) {
            const parsed = new Date(appointment.DateFormatted);
            if (!isNaN(parsed.getTime())) {
                const year = parsed.getFullYear();
                const month = String(parsed.getMonth() + 1).padStart(2, '0');
                const day = String(parsed.getDate()).padStart(2, '0');
                return `${year}-${month}-${day}`;
            }
        }

        const tzId = appointment.TimeZoneId || this.getCurrentLocationTimezone().timeZoneId;
        const converted = this.convertUtcToTimezone(appointment.StartTime, tzId);
        return converted.dateString;
    },

    /**
     * Get calendar event datetime string in location's timezone
     * @param {Object} appointment - Appointment object
     * @param {string} field - 'StartTime' or 'EndTime'
     * @returns {string} DateTime for FullCalendar
     */
    getCalendarEventDateTime(appointment, field) {
        const tzId = appointment.TimeZoneId || this.getCurrentLocationTimezone().timeZoneId;
        const utcTime = field === 'EndTime' ? appointment.EndTime : appointment.StartTime;
        const converted = this.convertUtcToTimezone(utcTime, tzId);
        return converted.isoString;
    },

    /**
     * Format date for input in timezone
     * @param {Date|string} utcDateTime - UTC datetime
     * @param {string} ianaTimeZoneId - IANA timezone
     * @returns {string} Date in YYYY-MM-DD format
     */
    formatDateForInputInTimezone(utcDateTime, ianaTimeZoneId) {
        if (!utcDateTime) return '';
        const converted = this.convertUtcToTimezone(utcDateTime, ianaTimeZoneId);
        return converted.dateString;
    },

    /**
     * Format time for input in timezone
     * @param {Date|string} utcDateTime - UTC datetime
     * @param {string} ianaTimeZoneId - IANA timezone
     * @returns {string} Time in HH:MM format
     */
    formatTimeForInputInTimezone(utcDateTime, ianaTimeZoneId) {
        if (!utcDateTime) return '';
        const converted = this.convertUtcToTimezone(utcDateTime, ianaTimeZoneId);
        const hours = String(converted.hours).padStart(2, '0');
        const minutes = String(converted.minutes).padStart(2, '0');
        return `${hours}:${minutes}`;
    },

    /**
     * Format time display in timezone (12-hour format)
     * @param {Date|string} utcDateTime - UTC datetime
     * @param {string} ianaTimeZoneId - IANA timezone
     * @returns {string} Time like "9:00 AM"
     */
    formatTimeDisplayInTimezone(utcDateTime, ianaTimeZoneId) {
        if (!utcDateTime) return '';
        const converted = this.convertUtcToTimezone(utcDateTime, ianaTimeZoneId);
        const hour = converted.hours;
        const min = String(converted.minutes).padStart(2, '0');
        const ampm = hour >= 12 ? 'PM' : 'AM';
        const h12 = hour % 12 || 12;
        return `${h12}:${min} ${ampm}`;
    }
};

// Export for global access
window.TimezoneUtils = TimezoneUtils;

// Backward compatibility exports
window.getCurrentLocationTimezone = TimezoneUtils.getCurrentLocationTimezone.bind(TimezoneUtils);
window.setCurrentLocationTimezone = TimezoneUtils.setCurrentLocationTimezone.bind(TimezoneUtils);
window.convertUtcToTimezone = TimezoneUtils.convertUtcToTimezone.bind(TimezoneUtils);
window.convertLocalToUtc = TimezoneUtils.convertLocalToUtc.bind(TimezoneUtils);
window.formatTimeWithTimezone = TimezoneUtils.formatTimeWithTimezone.bind(TimezoneUtils);
window.formatTimeRangeWithTimezone = TimezoneUtils.formatTimeRangeWithTimezone.bind(TimezoneUtils);
window.formatDateWithTimezone = TimezoneUtils.formatDateWithTimezone.bind(TimezoneUtils);
window.formatDateTimeWithTimezone = TimezoneUtils.formatDateTimeWithTimezone.bind(TimezoneUtils);
window.formatSlotTime = TimezoneUtils.formatSlotTime.bind(TimezoneUtils);
window.getAppointmentDateInLocationTimezone = TimezoneUtils.getAppointmentDateInLocationTimezone.bind(TimezoneUtils);
window.getCalendarEventDateTime = TimezoneUtils.getCalendarEventDateTime.bind(TimezoneUtils);
window.formatDateForInputInTimezone = TimezoneUtils.formatDateForInputInTimezone.bind(TimezoneUtils);
window.formatTimeForInputInTimezone = TimezoneUtils.formatTimeForInputInTimezone.bind(TimezoneUtils);
window.formatTimeDisplayInTimezone = TimezoneUtils.formatTimeDisplayInTimezone.bind(TimezoneUtils);
