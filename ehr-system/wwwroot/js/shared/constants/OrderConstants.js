/**
 * OrderConstants - Constants for Orders module (Labs, Imaging, Referrals)
 */
const OrderConstants = {
    OrderType: { Lab: 0, Imaging: 1, Referral: 2 },
    OrderStatus: { Draft: 0, Pending: 1, Sent: 2, InProgress: 3, ResultsReceived: 4, Completed: 5, Cancelled: 6 },
    OrderPriority: { Routine: 0, Urgent: 1, STAT: 2 },

    TypeBadge: {
        0: '<span class="badge bg-primary"><i class="bi bi-droplet-half"></i> Lab</span>',
        1: '<span class="badge bg-info"><i class="bi bi-image"></i> Imaging</span>',
        2: '<span class="badge bg-success"><i class="bi bi-arrow-right-circle"></i> Referral</span>'
    },

    StatusBadge: {
        0: '<span class="badge bg-secondary">Draft</span>',
        1: '<span class="badge bg-warning text-dark">Pending</span>',
        2: '<span class="badge bg-info">Sent</span>',
        3: '<span class="badge bg-primary">In Progress</span>',
        4: '<span class="badge bg-purple" style="background-color:#6f42c1!important">Results Received</span>',
        5: '<span class="badge bg-success">Completed</span>',
        6: '<span class="badge bg-danger">Cancelled</span>'
    },

    PriorityBadge: {
        0: '<span class="badge bg-light text-dark border">Routine</span>',
        1: '<span class="badge bg-warning text-dark">Urgent</span>',
        2: '<span class="badge bg-danger">STAT</span>'
    },

    ImagingModality: {
        0: 'X-Ray', 1: 'CT Scan', 2: 'MRI', 3: 'Ultrasound',
        4: 'Mammogram', 5: 'DEXA Scan', 6: 'Fluoroscopy', 7: 'PET Scan', 8: 'Nuclear Medicine'
    },

    ReferralSpecialties: [
        'Allergy & Immunology', 'Cardiology', 'Dermatology', 'Endocrinology',
        'Gastroenterology', 'Hematology/Oncology', 'Infectious Disease',
        'Nephrology', 'Neurology', 'Obstetrics & Gynecology', 'Ophthalmology',
        'Orthopedics', 'Otolaryngology (ENT)', 'Pain Management', 'Podiatry',
        'Psychiatry', 'Pulmonology', 'Rheumatology', 'Surgery (General)',
        'Urology', 'Vascular Surgery'
    ],

    ReferralUrgency: { 0: 'Routine', 1: 'Urgent', 2: 'Emergent' },

    /** Get a short description for a given order */
    getOrderDescription(order) {
        switch (order.OrderType) {
            case 0: return order.LabPanelName || 'Lab Order';
            case 1: return (order.ModalityName || '') + (order.BodyPart ? ' - ' + order.BodyPart : '') || 'Imaging Order';
            case 2: return order.ReferralSpecialty || 'Referral';
            default: return 'Order';
        }
    }
};

window.OrderConstants = OrderConstants;
