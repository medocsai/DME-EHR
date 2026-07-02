/**
 * PrescriptionConstants - Prescription status, dosage form, route, and frequency constants
 */
const PrescriptionStatuses = {
    DRAFT: 0, ACTIVE: 1, SENT: 2, FILLED: 3, CANCELLED: 4, EXPIRED: 5,

    _config: {
        0: { name: 'Draft', cssClass: 'bg-secondary', textClass: 'text-secondary' },
        1: { name: 'Active', cssClass: 'bg-primary', textClass: 'text-primary' },
        2: { name: 'Sent', cssClass: 'bg-info', textClass: 'text-info' },
        3: { name: 'Filled', cssClass: 'bg-success', textClass: 'text-success' },
        4: { name: 'Cancelled', cssClass: 'bg-danger', textClass: 'text-danger' },
        5: { name: 'Expired', cssClass: 'bg-warning', textClass: 'text-warning' }
    },

    getName(status) { return this._config[status]?.name || 'Unknown'; },
    getBadgeHtml(status) {
        const c = this._config[status] || { name: 'Unknown', cssClass: 'bg-secondary' };
        return `<span class="badge ${c.cssClass}">${c.name}</span>`;
    }
};

const DosageForms = {
    0: 'Tablet', 1: 'Capsule', 2: 'Liquid', 3: 'Cream', 4: 'Ointment',
    5: 'Patch', 6: 'Injection', 7: 'Inhaler', 8: 'Drops', 9: 'Suppository', 99: 'Other',
    getName(val) { return this[val] || 'Unknown'; }
};

const MedicationRoutes = {
    0: 'Oral', 1: 'Topical', 2: 'Subcutaneous', 3: 'Intramuscular', 4: 'Intravenous',
    5: 'Rectal', 6: 'Ophthalmic', 7: 'Otic', 8: 'Nasal', 9: 'Transdermal', 10: 'Inhalation',
    getName(val) { return this[val] || 'Unknown'; }
};

const MedicationFrequencies = {
    0: 'Once daily', 1: 'Twice daily (BID)', 2: 'Three times daily (TID)',
    3: 'Four times daily (QID)', 4: 'At bedtime (QHS)',
    5: 'Every 4 hours', 6: 'Every 6 hours', 7: 'Every 8 hours', 8: 'Every 12 hours',
    9: 'As needed (PRN)', 10: 'Weekly', 11: 'Every 2 weeks', 12: 'Monthly', 99: 'As directed',
    getName(val) { return this[val] || 'Unknown'; }
};

window.PrescriptionStatuses = PrescriptionStatuses;
window.DosageForms = DosageForms;
window.MedicationRoutes = MedicationRoutes;
window.MedicationFrequencies = MedicationFrequencies;
