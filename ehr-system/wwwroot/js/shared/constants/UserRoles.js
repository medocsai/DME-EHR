/**
 * UserRoles - User role constants and utilities
 *
 * Usage:
 *   UserRoles.SUPER_ADMIN          // 0
 *   UserRoles.getName(0)           // "Super Admin"
 *   UserRoles.canAccessAdmin(2)    // false (Clinician)
 */
const UserRoles = {
    // Role enum values (match server-side enum)
    SUPER_ADMIN: 0,
    CLINIC_ADMIN: 1,
    CLINICIAN: 2,
    FRONT_DESK: 3,
    BILLER: 4,
    READ_ONLY: 5,
    MEDICAL_ASSISTANT: 6,
    NURSE: 7,
    PATIENT: 8,

    // Role names
    _names: {
        0: 'Super Admin',
        1: 'Clinic Admin',
        2: 'Clinician',
        3: 'Front Desk',
        4: 'Biller',
        5: 'Read Only',
        6: 'Medical Assistant',
        7: 'Nurse',
        8: 'Patient'
    },

    // Role descriptions
    _descriptions: {
        0: 'Full platform access across all tenants',
        1: 'Full clinic management access',
        2: 'Clinical documentation and patient care',
        3: 'Scheduling and patient check-in',
        4: 'Billing and claims management',
        5: 'View-only access to patient data',
        6: 'Vitals, CC/HPI documentation, and visit support',
        7: 'Vitals, CC/HPI documentation, and visit support',
        8: 'Patient portal - view-only access to own records'
    },

    /**
     * Get display name for a role
     * @param {number} role - Role enum value
     * @returns {string} Display name
     */
    getName(role) {
        const r = parseInt(role, 10);
        return this._names[r] || 'User';
    },

    /**
     * Get description for a role
     * @param {number} role - Role enum value
     * @returns {string} Role description
     */
    getDescription(role) {
        return this._descriptions[role] || '';
    },

    /**
     * Check if role has admin access
     * @param {number} role - Role to check
     * @param {number} [tenantId] - Tenant ID (for Super Admin distinction)
     * @returns {boolean}
     */
    isAdmin(role, tenantId = null) {
        // Super Admin without tenant = Super Admin
        // Super Admin with tenant OR Clinic Admin = Admin
        return role === 0 || role === 1;
    },

    /**
     * Check if role is Super Admin (platform-level)
     * @param {number} role - Role to check
     * @param {number} [tenantId] - Tenant ID
     * @returns {boolean}
     */
    isSuperAdmin(role, tenantId = null) {
        return role === 0 && !tenantId;
    },

    /**
     * Check if role can access admin pages
     * @param {number} role - Role to check
     * @returns {boolean}
     */
    canAccessAdmin(role) {
        return role === 0 || role === 1;
    },

    /**
     * Check if role can schedule appointments
     * @param {number} role - Role to check
     * @returns {boolean}
     */
    canSchedule(role) {
        // Everyone except Biller, Read Only, Medical Assistant, and Nurse
        return role !== 4 && role !== 5 && role !== 6 && role !== 7;
    },

    /**
     * Check if role can edit clinical notes
     * @param {number} role - Role to check
     * @returns {boolean}
     */
    canEditNotes(role) {
        return role === 0 || role === 1 || role === 2;
    },

    /**
     * Check if role has billing access
     * @param {number} role - Role to check
     * @returns {boolean}
     */
    hasBillingAccess(role) {
        return role === 0 || role === 1 || role === 4;
    },

    /**
     * Check if role can manage users
     * @param {number} role - Role to check
     * @returns {boolean}
     */
    canManageUsers(role) {
        return role === 0 || role === 1;
    },

    /**
     * Check if role can manage providers
     * @param {number} role - Role to check
     * @returns {boolean}
     */
    canManageProviders(role) {
        return role === 0 || role === 1;
    },

    /**
     * Check if role can view reports
     * @param {number} role - Role to check
     * @returns {boolean}
     */
    canViewReports(role) {
        return role === 0 || role === 1;
    },

    /**
     * Check if role can access consent form kiosk management
     * @param {number} role - Role to check
     * @returns {boolean}
     */
    canManageConsent(role) {
        return role === 0 || role === 1 || role === 3;
    },

    /**
     * Check if role can record audio for transcription
     * @param {number} role - Role to check
     * @returns {boolean}
     */
    canRecord(role) {
        return role === 0 || role === 1 || role === 2;
    },

    /**
     * Check if role is Medical Assistant or Nurse
     * @param {number} role - Role to check
     * @returns {boolean}
     */
    isMaNurse(role) {
        return role === 6 || role === 7;
    },

    /**
     * Check if role is Patient (portal access)
     * @param {number} role - Role to check
     * @returns {boolean}
     */
    isPatient(role) {
        return role === 8;
    },

    /**
     * Check if role can enter vitals
     * @param {number} role - Role to check
     * @returns {boolean}
     */
    canEnterVitals(role) {
        return role === 0 || role === 1 || role === 2 || role === 6 || role === 7;
    },

    /**
     * Check if role can document CC/HPI
     * @param {number} role - Role to check
     * @returns {boolean}
     */
    canDocumentCcHpi(role) {
        return role === 0 || role === 1 || role === 2 || role === 6 || role === 7;
    },

    /**
     * Check if role can create/edit orders
     * @param {number} role - Role to check
     * @returns {boolean}
     */
    canEditOrders(role) {
        return role === 0 || role === 1 || role === 2;
    },

    /**
     * Check if role can create/edit prescriptions
     * @param {number} role - Role to check
     * @returns {boolean}
     */
    canEditPrescriptions(role) {
        return role === 0 || role === 1 || role === 2;
    },

    /**
     * Check if role can add/edit patients
     * @param {number} role - Role to check
     * @returns {boolean}
     */
    canEditPatients(role) {
        return role === 0 || role === 1 || role === 3;
    },

    /**
     * Get CSS class for role-based visibility
     * @param {number} role - Role enum value
     * @param {number} [tenantId] - Tenant ID
     * @returns {string} CSS class
     */
    getBodyClass(role, tenantId = null) {
        if (role === 0 && !tenantId) return 'super-admin';
        if (role === 0 || role === 1) return 'admin';
        if (role === 2) return 'clinician';
        if (role === 3) return 'front-desk';
        if (role === 4) return 'biller';
        if (role === 5) return 'read-only';
        if (role === 6) return 'medical-assistant';
        if (role === 7) return 'nurse';
        if (role === 8) return 'patient';
        return '';
    },

    /**
     * Get all roles for dropdowns
     * @param {boolean} [includeSuperAdmin=false] - Include Super Admin option
     * @returns {Array} Array of { value, label }
     */
    getAll(includeSuperAdmin = false) {
        return Object.entries(this._names)
            .filter(([value]) => includeSuperAdmin || parseInt(value) !== 0)
            .map(([value, label]) => ({
                value: parseInt(value),
                label
            }));
    },

    /**
     * Get options for select dropdown
     * @param {boolean} [includeSuperAdmin=false] - Include Super Admin
     * @returns {string} HTML options
     */
    getOptions(includeSuperAdmin = false) {
        return this.getAll(includeSuperAdmin)
            .map(r => `<option value="${r.value}">${r.label}</option>`)
            .join('');
    },

    /**
     * Check if role value is valid
     * @param {number} role - Role to check
     * @returns {boolean}
     */
    isValid(role) {
        return role !== null && role !== undefined && this._names.hasOwnProperty(role);
    },

    /**
     * Compare roles for hierarchy (lower number = higher rank)
     * @param {number} role1 - First role
     * @param {number} role2 - Second role
     * @returns {number} Negative if role1 > role2, positive if role1 < role2
     */
    compare(role1, role2) {
        return role1 - role2;
    },

    /**
     * Check if role1 has equal or higher privileges than role2
     * @param {number} role1 - Role to check
     * @param {number} role2 - Required role
     * @returns {boolean}
     */
    hasAtLeast(role1, role2) {
        return role1 <= role2;
    }
};

// Export for global access
window.UserRoles = UserRoles;

// Backward compatibility
window.getRoleName = UserRoles.getName.bind(UserRoles);
