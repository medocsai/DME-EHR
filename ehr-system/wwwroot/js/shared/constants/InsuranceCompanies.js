/**
 * InsuranceCompanies - Major US insurance payers/companies
 *
 * Usage:
 *   InsuranceCompanies.getAll()          // Get all companies
 *   InsuranceCompanies.getOptions()      // Get HTML options for select
 *   InsuranceCompanies.getByName(name)   // Find company by name
 */
const InsuranceCompanies = {
    // Major US Insurance Companies
    _companies: [
        // Top National Health Insurance Companies
        { name: 'UnitedHealthcare', payerId: '87726' },
        { name: 'Anthem Blue Cross Blue Shield', payerId: '00590' },
        { name: 'Aetna', payerId: '60054' },
        { name: 'Cigna', payerId: '62308' },
        { name: 'Humana', payerId: '61101' },
        { name: 'Kaiser Permanente', payerId: '95168' },
        { name: 'Blue Cross Blue Shield', payerId: '00590' },
        { name: 'Centene Corporation', payerId: '47210' },
        { name: 'Molina Healthcare', payerId: '47204' },
        { name: 'WellCare Health Plans', payerId: '52052' },

        // Medicare & Medicaid
        { name: 'Medicare', payerId: '00000' },
        { name: 'Medicaid', payerId: 'State-specific' },

        // Other Major Carriers
        { name: 'HealthNet', payerId: '47234' },
        { name: 'Emblem Health', payerId: '14163' },
        { name: 'Highmark', payerId: '12345' },
        { name: 'Independence Blue Cross', payerId: '12121' },
        { name: 'BlueCross BlueShield of California', payerId: '95216' },
        { name: 'BlueCross BlueShield of Florida', payerId: '59088' },
        { name: 'BlueCross BlueShield of Texas', payerId: '75189' },
        { name: 'BlueCross BlueShield of Illinois', payerId: '60031' },
        { name: 'BlueCross BlueShield of Michigan', payerId: '48120' },
        { name: 'BlueCross BlueShield of North Carolina', payerId: '27709' },

        // Regional and Specialty Plans
        { name: 'Tricare', payerId: 'TRICARE' },
        { name: 'Veterans Affairs (VA)', payerId: 'VA' },
        { name: 'Oscar Health', payerId: '00000' },
        { name: 'Bright Health', payerId: '00000' },
        { name: 'Ambetter', payerId: '47210' },
        { name: 'CareFirst', payerId: '20785' },
        { name: 'Excellus BlueCross BlueShield', payerId: '14127' },
        { name: 'Florida Blue', payerId: '59088' },
        { name: 'Premera Blue Cross', payerId: '98301' },
        { name: 'Regence', payerId: '97225' },

        // Workers Compensation Carriers
        { name: 'Liberty Mutual', payerId: 'LIBERTY' },
        { name: 'The Hartford', payerId: 'HARTFORD' },
        { name: 'Travelers', payerId: 'TRAVELERS' },
        { name: 'Zurich American Insurance', payerId: 'ZURICH' },
        { name: 'AIG', payerId: 'AIG' },
        { name: 'Sedgwick', payerId: 'SEDGWICK' },
        { name: 'Gallagher Bassett', payerId: 'GALLAGHER' },
        { name: 'Broadspire', payerId: 'BROADSPIRE' },

        // Personal Injury / Auto Insurance
        { name: 'State Farm', payerId: 'STATEFARM' },
        { name: 'Geico', payerId: 'GEICO' },
        { name: 'Progressive', payerId: 'PROGRESSIVE' },
        { name: 'Allstate', payerId: 'ALLSTATE' },
        { name: 'USAA', payerId: 'USAA' },
        { name: 'Farmers Insurance', payerId: 'FARMERS' },
        { name: 'Nationwide', payerId: 'NATIONWIDE' }
    ],

    /**
     * Get all insurance companies
     * @returns {Array} Array of { name, payerId }
     */
    getAll() {
        return [...this._companies].sort((a, b) => a.name.localeCompare(b.name));
    },

    /**
     * Get company by name (case-insensitive partial match)
     * @param {string} searchName - Name to search
     * @returns {Object|null} Company object or null
     */
    getByName(searchName) {
        if (!searchName) return null;
        const search = searchName.toLowerCase();
        return this._companies.find(c => c.name.toLowerCase().includes(search)) || null;
    },

    /**
     * Get payer ID by company name
     * @param {string} name - Company name
     * @returns {string|null} Payer ID or null
     */
    getPayerId(name) {
        const company = this.getByName(name);
        return company ? company.payerId : null;
    },

    /**
     * Get options for select dropdown (sorted alphabetically)
     * @param {boolean} includeBlank - Include blank option at top
     * @returns {string} HTML options string
     */
    getOptions(includeBlank = true) {
        const sorted = this.getAll();
        let html = '';

        if (includeBlank) {
            html = '<option value="">Select Insurance Company...</option>';
        }

        html += sorted
            .map(c => `<option value="${this._escapeHtml(c.name)}" data-payer-id="${this._escapeHtml(c.payerId)}">${this._escapeHtml(c.name)}</option>`)
            .join('');

        return html;
    },

    /**
     * Check if a company name exists
     * @param {string} name - Company name
     * @returns {boolean}
     */
    exists(name) {
        return this.getByName(name) !== null;
    },

    /**
     * Get companies by category (approximate based on typical usage)
     * @param {string} category - 'health', 'workers-comp', 'auto', 'medicare'
     * @returns {Array} Array of companies
     */
    getByCategory(category) {
        const cat = category?.toLowerCase();

        if (cat === 'health') {
            return this._companies.filter(c =>
                !['Liberty Mutual', 'The Hartford', 'Travelers', 'State Farm', 'Geico', 'Progressive',
                  'Allstate', 'USAA', 'Farmers Insurance', 'Nationwide', 'Medicare', 'Medicaid',
                  'Tricare', 'Veterans Affairs (VA)'].includes(c.name)
            );
        }

        if (cat === 'workers-comp') {
            return this._companies.filter(c =>
                ['Liberty Mutual', 'The Hartford', 'Travelers', 'Zurich American Insurance',
                 'AIG', 'Sedgwick', 'Gallagher Bassett', 'Broadspire'].includes(c.name)
            );
        }

        if (cat === 'auto' || cat === 'personal-injury') {
            return this._companies.filter(c =>
                ['State Farm', 'Geico', 'Progressive', 'Allstate', 'USAA',
                 'Farmers Insurance', 'Nationwide'].includes(c.name)
            );
        }

        if (cat === 'medicare') {
            return this._companies.filter(c =>
                ['Medicare', 'Medicaid', 'Tricare', 'Veterans Affairs (VA)'].includes(c.name)
            );
        }

        return this._companies;
    },

    /**
     * Escape HTML for safe output
     * @private
     * @param {string} str - String to escape
     * @returns {string} Escaped string
     */
    _escapeHtml(str) {
        if (!str) return '';
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    },

    /**
     * Search companies by partial name match
     * @param {string} query - Search query
     * @returns {Array} Matching companies
     */
    search(query) {
        if (!query) return this.getAll();
        const q = query.toLowerCase();
        return this._companies
            .filter(c => c.name.toLowerCase().includes(q))
            .sort((a, b) => a.name.localeCompare(b.name));
    }
};

// Export for global access
window.InsuranceCompanies = InsuranceCompanies;
