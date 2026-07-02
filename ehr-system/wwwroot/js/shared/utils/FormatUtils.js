/**
 * FormatUtils - Number, currency, and data formatting utilities
 *
 * Usage:
 *   FormatUtils.currency(1234.56);  // "$1,234.56"
 *   FormatUtils.phone('5551234567'); // "(555) 123-4567"
 *   FormatUtils.percentage(0.75);   // "75%"
 */
const FormatUtils = {
    /**
     * Format a number as currency
     * @param {number} amount - Amount to format
     * @param {string} [currency='USD'] - Currency code
     * @param {string} [locale='en-US'] - Locale
     * @returns {string} Formatted currency string
     */
    currency(amount, currency = 'USD', locale = 'en-US') {
        if (amount == null || isNaN(amount)) return '$0.00';
        return new Intl.NumberFormat(locale, {
            style: 'currency',
            currency: currency
        }).format(amount);
    },

    /**
     * Format a phone number
     * @param {string} phone - Phone number (digits only or formatted)
     * @returns {string} Formatted phone number
     */
    phone(phone) {
        if (!phone) return '';

        // Remove all non-digit characters
        const digits = phone.replace(/\D/g, '');

        // Format based on length
        if (digits.length === 10) {
            return `(${digits.slice(0, 3)}) ${digits.slice(3, 6)}-${digits.slice(6)}`;
        } else if (digits.length === 11 && digits[0] === '1') {
            return `+1 (${digits.slice(1, 4)}) ${digits.slice(4, 7)}-${digits.slice(7)}`;
        }

        return phone; // Return as-is if not standard format
    },

    /**
     * Format a Social Security Number (masked or full)
     * @param {string} ssn - SSN (digits only or formatted)
     * @param {boolean} [masked=true] - Show masked format
     * @returns {string} Formatted SSN
     */
    ssn(ssn, masked = true) {
        if (!ssn) return '';

        const digits = ssn.replace(/\D/g, '');

        if (digits.length !== 9) return ssn;

        if (masked) {
            return `***-**-${digits.slice(5)}`;
        }
        return `${digits.slice(0, 3)}-${digits.slice(3, 5)}-${digits.slice(5)}`;
    },

    /**
     * Format a number as percentage
     * @param {number} value - Decimal value (0.75 = 75%)
     * @param {number} [decimals=0] - Decimal places
     * @returns {string} Formatted percentage
     */
    percentage(value, decimals = 0) {
        if (value == null || isNaN(value)) return '0%';
        return `${(value * 100).toFixed(decimals)}%`;
    },

    /**
     * Format a number with commas
     * @param {number} num - Number to format
     * @param {number} [decimals] - Fixed decimal places (optional)
     * @returns {string} Formatted number
     */
    number(num, decimals) {
        if (num == null || isNaN(num)) return '0';

        if (decimals !== undefined) {
            return num.toLocaleString('en-US', {
                minimumFractionDigits: decimals,
                maximumFractionDigits: decimals
            });
        }

        return num.toLocaleString('en-US');
    },

    /**
     * Format bytes to human readable size
     * @param {number} bytes - Number of bytes
     * @param {number} [decimals=2] - Decimal places
     * @returns {string} Formatted size (e.g., "1.5 MB")
     */
    fileSize(bytes, decimals = 2) {
        if (bytes === 0) return '0 Bytes';
        if (!bytes || isNaN(bytes)) return '';

        const k = 1024;
        const sizes = ['Bytes', 'KB', 'MB', 'GB', 'TB'];
        const i = Math.floor(Math.log(bytes) / Math.log(k));

        return `${parseFloat((bytes / Math.pow(k, i)).toFixed(decimals))} ${sizes[i]}`;
    },

    /**
     * Format duration in minutes to human readable
     * @param {number} minutes - Duration in minutes
     * @returns {string} Formatted duration (e.g., "1h 30m")
     */
    duration(minutes) {
        if (!minutes || isNaN(minutes)) return '0m';

        const hours = Math.floor(minutes / 60);
        const mins = minutes % 60;

        if (hours === 0) return `${mins}m`;
        if (mins === 0) return `${hours}h`;
        return `${hours}h ${mins}m`;
    },

    /**
     * Format duration in seconds to MM:SS or HH:MM:SS
     * @param {number} seconds - Duration in seconds
     * @returns {string} Formatted time
     */
    timeFromSeconds(seconds) {
        if (!seconds || isNaN(seconds)) return '0:00';

        const hrs = Math.floor(seconds / 3600);
        const mins = Math.floor((seconds % 3600) / 60);
        const secs = Math.floor(seconds % 60);

        if (hrs > 0) {
            return `${hrs}:${String(mins).padStart(2, '0')}:${String(secs).padStart(2, '0')}`;
        }
        return `${mins}:${String(secs).padStart(2, '0')}`;
    },

    /**
     * Format a ZIP code
     * @param {string} zip - ZIP code
     * @returns {string} Formatted ZIP
     */
    zipCode(zip) {
        if (!zip) return '';
        const digits = zip.replace(/\D/g, '');

        if (digits.length === 9) {
            return `${digits.slice(0, 5)}-${digits.slice(5)}`;
        }
        return digits.slice(0, 5);
    },

    /**
     * Format a credit card number (masked)
     * @param {string} cardNumber - Card number
     * @returns {string} Masked card number
     */
    creditCard(cardNumber) {
        if (!cardNumber) return '';
        const digits = cardNumber.replace(/\D/g, '');
        if (digits.length < 4) return '****';
        return `**** **** **** ${digits.slice(-4)}`;
    },

    /**
     * Ordinalize a number (1st, 2nd, 3rd, etc.)
     * @param {number} num - Number to ordinalize
     * @returns {string} Ordinalized number
     */
    ordinal(num) {
        const s = ['th', 'st', 'nd', 'rd'];
        const v = num % 100;
        return num + (s[(v - 20) % 10] || s[v] || s[0]);
    },

    /**
     * Format age from date of birth
     * @param {Date|string} dateOfBirth - DOB
     * @returns {string} Age string (e.g., "35 years")
     */
    age(dateOfBirth) {
        if (!dateOfBirth) return '';

        const dob = typeof dateOfBirth === 'string' ? new Date(dateOfBirth) : dateOfBirth;
        if (isNaN(dob.getTime())) return '';

        const today = new Date();
        let age = today.getFullYear() - dob.getFullYear();
        const monthDiff = today.getMonth() - dob.getMonth();

        if (monthDiff < 0 || (monthDiff === 0 && today.getDate() < dob.getDate())) {
            age--;
        }

        return `${age} year${age !== 1 ? 's' : ''}`;
    },

    /**
     * Format a boolean as Yes/No
     * @param {boolean} value - Boolean value
     * @param {string} [yes='Yes'] - Text for true
     * @param {string} [no='No'] - Text for false
     * @returns {string}
     */
    yesNo(value, yes = 'Yes', no = 'No') {
        return value ? yes : no;
    },

    /**
     * Format address as single line
     * @param {Object} addr - Address object
     * @returns {string} Formatted address
     */
    address(addr) {
        if (!addr) return '';
        const parts = [
            addr.Address || addr.address || addr.Street || addr.street,
            addr.City || addr.city,
            addr.State || addr.state,
            addr.ZipCode || addr.zipCode || addr.Zip || addr.zip
        ].filter(Boolean);
        return parts.join(', ');
    },

    /**
     * Format a list as comma-separated string
     * @param {Array} items - Array of items
     * @param {string} [conjunction='and'] - Word before last item
     * @returns {string}
     */
    list(items, conjunction = 'and') {
        if (!items || items.length === 0) return '';
        if (items.length === 1) return String(items[0]);
        if (items.length === 2) return `${items[0]} ${conjunction} ${items[1]}`;

        const allButLast = items.slice(0, -1).join(', ');
        const last = items[items.length - 1];
        return `${allButLast}, ${conjunction} ${last}`;
    }
};

// Export for global access
window.FormatUtils = FormatUtils;

// Backward compatibility
window.formatCurrency = FormatUtils.currency.bind(FormatUtils);
