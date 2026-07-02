// ============================================
// EHR ENUMS - SCAFFOLD SAFE
// These match database INT columns
// Put this file in: Models/Enums/AllEnums.cs
// ============================================

namespace EHR.Models
{
    // Note: The database stores these as INT columns.
    // The Generated models use int, not enums.
    // These enums are for reference and can be used in your code
    // but you'll need to cast to/from int when working with entities.

    // ============================================
    // APPOINTMENT ENUMS
    // ============================================
    public enum AppointmentStatus
    {
        Scheduled = 0,
        Confirmed = 1,
        CheckedIn = 2,
        InProgress = 3,
        Completed = 4,
        NoShow = 5,
        Cancelled = 6,
        Rescheduled = 7,
        Missed = 8  // Past appointment that was never checked in, then rescheduled
    }

    public enum AppointmentType
    {
        NewPatientVisit = 0,    // N - New Patient Visit
        FollowUpVisit = 1,      // F - Follow-Up Visit
        AnnualPhysical = 2,     // A - Annual Physical / Wellness Exam
        WellnessExam = 3,       // W - Wellness Exam
        Consultation = 4,       // C - Consultation
        Telehealth = 5,         // T - Telehealth
        ProcedureVisit = 6,     // P - Procedure Visit
        UrgentVisit = 7,        // U - Urgent / Walk-In
        LabReview = 8,          // L - Lab Review
        MedicationReview = 9,   // M - Medication Review
        NewLongevityPatient = 10,        // NL - First longevity consultation
        FollowUpLongevityPatient = 11    // FL - Continuing longevity care
    }

    public static class AppointmentTypeExtensions
    {
        public static bool IsLongevity(this AppointmentType type)
            => type == AppointmentType.NewLongevityPatient || type == AppointmentType.FollowUpLongevityPatient;
    }

    // ============================================
    // PATIENT ENUMS
    // ============================================
    public enum PatientStatus
    {
        Active = 0,
        Inactive = 1,
        Discharged = 2,
        Deceased = 3
    }

    // ============================================
    // PROVIDER ENUMS
    // ============================================
    public enum ProviderCredentialStatus
    {
        Pending = 0,
        Active = 1,
        Expired = 2,
        Suspended = 3
    }

    // Alias for CredentialStatus (used in DbSeeder)
    public enum CredentialStatus
    {
        Pending = 0,
        Approved = 1,
        Expired = 2,
        Suspended = 3
    }

    // ============================================
    // INSURANCE ENUMS
    // ============================================
    public enum InsuranceType
    {
        Primary = 0,
        Secondary = 1,
        Tertiary = 2,
        WorkersComp = 3,
        AutoAccident = 4,
        SelfPay = 5
    }

    public enum EligibilityStatus
    {
        Unknown = 0,
        Eligible = 1,
        NotEligible = 2,
        PendingVerification = 3
    }

    public enum InsuranceCategory
    {
        PrivateInsurance = 0,
        WorkersCompensation = 1,
        PersonalInjury = 2,
        SelfPay = 3
    }

    // ============================================
    // BILLING ENUMS
    // ============================================
    public enum ClaimStatus
    {
        Draft = 0,
        Ready = 1,
        Submitted = 2,
        Acknowledged = 3,
        Pending = 4,
        Paid = 5,
        PartiallyPaid = 6,
        Denied = 7,
        Rejected = 8
    }

    public enum ClaimType
    {
        Professional = 0,
        Institutional = 1,
        Dental = 2
    }

    public enum PaymentType
    {
        Copay = 0,
        Coinsurance = 1,
        Deductible = 2,
        SelfPay = 3,
        InsurancePayment = 4,
        Refund = 5,
        Adjustment = 6
    }

    public enum PaymentMethod
    {
        Cash = 0,
        Check = 1,
        CreditCard = 2,
        DebitCard = 3,
        EFT = 4,
        ERA = 5,
        Other = 6
    }

    public enum PaymentStatus
    {
        Pending = 0,
        Completed = 1,
        Failed = 2,
        Refunded = 3,
        Voided = 4
    }

    public enum ChargeStatus
    {
        Pending = 0,
        Billed = 1,
        Paid = 2,
        Denied = 3,
        WrittenOff = 4
    }

    // ============================================
    // INSTALLMENT PLAN ENUMS
    // ============================================
    public enum InstallmentPlanStatus
    {
        Active = 0,
        Completed = 1,
        Cancelled = 2,
        Defaulted = 3
    }

    public enum InstallmentDetailStatus
    {
        Pending = 0,
        Paid = 1,
        Failed = 2,
        Delinquent = 3
    }

    // ============================================
    // NOTE / CLINICAL NOTE ENUMS
    // ============================================
    public enum NoteStatus
    {
        Draft = 0,
        PendingSignature = 1,
        Signed = 2,
        PendingCoSignature = 3,
        Finalized = 4,
        Amended = 5,
        Voided = 6
    }

    public enum ClinicalNoteStatus
    {
        Draft = 0,
        PendingSignature = 1,
        Signed = 2,
        PendingCoSignature = 3,
        Finalized = 4,
        Amended = 5,
        Voided = 6
    }

    public enum ClinicalNoteTemplateType
    {
        HistoryAndPhysical = 0,
        SOAPNote = 1,
        OfficeVisitNote = 2,
        ProgressNote = 3,
        ConsultationNote = 4,
        ProcedureNote = 5,
        AnnualWellnessNote = 6,
        PhoneNote = 7,
        ReferralLetter = 8,
        LabReviewNote = 9,
        Custom = 99
    }

    public enum NoteType
    {
        HistoryAndPhysical = 0,
        SOAPNote = 1,
        OfficeVisitNote = 2,
        ProgressNote = 3,
        ConsultationNote = 4,
        ProcedureNote = 5,
        AnnualWellnessNote = 6,
        PhoneNote = 7,
        ReferralLetter = 8,
        LabReviewNote = 9,
        Custom = 99
    }

    // ============================================
    // CONSENT ENUMS
    // ============================================
    public enum ConsentType
    {
        TreatmentConsent = 0,
        PrivacyPolicy = 1,
        HIPAA = 2,
        ReleaseOfInformation = 3,
        FinancialResponsibility = 4,
        TelehealthConsent = 5,
        PhotoVideoConsent = 6,
        Other = 99
    }

    // ============================================
    // UNAVAILABILITY ENUMS
    // ============================================
    public enum UnavailabilityType
    {
        Vacation = 0,
        SickLeave = 1,
        Training = 2,
        PersonalLeave = 3,
        Holiday = 4,
        Conference = 5,
        AdminTime = 6,
        Other = 7
    }

    // ============================================
    // TENANT ENUMS
    // ============================================
    public enum TenantStatus
    {
        Pending = 0,
        Active = 1,
        Suspended = 2,
        Cancelled = 3
    }

    public enum TenantPlan
    {
        Trial = 0,
        Basic = 1,
        Professional = 2,
        Enterprise = 3
    }

    // Alias for SubscriptionPlan (used in DbSeeder and TenantService)
    public enum SubscriptionPlan
    {
        Trial = 0,
        Basic = 1,
        Professional = 2,
        Enterprise = 3
    }

    // ============================================
    // USER ENUMS
    // ============================================
    public enum UserRole
    {
        SuperAdmin = 0,
        ClinicAdmin = 1,
        Clinician = 2,
        FrontDesk = 3,
        Biller = 4,
        ReadOnly = 5,
        MedicalAssistant = 6,
        Nurse = 7,
        Patient = 8
    }

    public enum UserStatus
    {
        Active = 0,
        Inactive = 1,
        Locked = 2,
        PendingActivation = 3
    }

    // ============================================
    // APPOINTMENT DOCUMENTATION STATUS (for Dashboard)
    // ============================================
    public enum AppointmentDocumentationStatus
    {
        NotApplicable = 0,  // Not yet checked in
        InProgress = 1,     // Checked in, encounter active
        Complete = 2        // Appointment completed (encounter closed)
    }

    // ============================================
    // CARE EPISODE ENUMS
    // ============================================
    public enum CareEpisodeStatus
    {
        Active = 0,
        Overdue = 1,        // End date passed but not completed
        Completed = 2,      // Discharge completed or manually marked
        OnHold = 3          // Temporarily paused
    }

    public enum CareEpisodeCompletionMethod
    {
        None = 0,
        DischargeAppointment = 1,   // Completed via Discharge type appointment
        Manual = 2                   // Manually marked as completed by admin
    }

    // ============================================
    // INTERNAL MEDICINE ENUMS
    // ============================================

    public enum EncounterStatus
    {
        Open = 0,
        Signed = 1,
        Locked = 2,
        Amended = 3
    }

    public enum ProblemStatus
    {
        Active = 0,
        Resolved = 1,
        Inactive = 2
    }

    public enum MedicationStatus
    {
        Active = 0,
        Discontinued = 1,
        OnHold = 2,
        Completed = 3
    }

    public enum AllergyType
    {
        Drug = 0,
        Food = 1,
        Environmental = 2,
        Other = 3
    }

    public enum AllergySeverity
    {
        Mild = 0,
        Moderate = 1,
        Severe = 2,
        LifeThreatening = 3
    }

    public enum TreatmentPlanStatus
    {
        Active = 0,
        Completed = 1,
        OnHold = 2,
        Cancelled = 3
    }

    // ============================================
    // E-PRESCRIBING ENUMS
    // ============================================

    public enum PrescriptionStatus
    {
        Draft = 0,
        Active = 1,
        Sent = 2,
        Filled = 3,
        Cancelled = 4,
        Expired = 5
    }

    public enum DosageForm
    {
        Tablet = 0,
        Capsule = 1,
        Liquid = 2,
        Cream = 3,
        Ointment = 4,
        Patch = 5,
        Injection = 6,
        Inhaler = 7,
        Drops = 8,
        Suppository = 9,
        Other = 99
    }

    public enum MedicationRoute
    {
        Oral = 0,
        Topical = 1,
        Subcutaneous = 2,
        Intramuscular = 3,
        Intravenous = 4,
        Rectal = 5,
        Ophthalmic = 6,
        Otic = 7,
        Nasal = 8,
        Transdermal = 9,
        Inhalation = 10
    }

    public enum MedicationFrequency
    {
        Daily = 0,
        BID = 1,
        TID = 2,
        QID = 3,
        QHS = 4,
        Q4H = 5,
        Q6H = 6,
        Q8H = 7,
        Q12H = 8,
        PRN = 9,
        Weekly = 10,
        BiWeekly = 11,
        Monthly = 12,
        AsDirected = 99
    }

    // ============================================
    // ORDERS MODULE ENUMS
    // ============================================

    public enum OrderType
    {
        Lab = 0,
        Imaging = 1,
        Referral = 2
    }

    public enum OrderStatus
    {
        Draft = 0,
        Pending = 1,
        Sent = 2,
        InProgress = 3,
        ResultsReceived = 4,
        Completed = 5,
        Cancelled = 6
    }

    public enum OrderPriority
    {
        Routine = 0,
        Urgent = 1,
        STAT = 2
    }

    public enum ImagingModality
    {
        XRay = 0,
        CT = 1,
        MRI = 2,
        Ultrasound = 3,
        Mammogram = 4,
        DEXA = 5,
        Fluoroscopy = 6,
        PET = 7,
        Nuclear = 8
    }

    public enum ReferralUrgency
    {
        Routine = 0,
        Urgent = 1,
        Emergent = 2
    }

    // ============================================
    // PATIENT PORTAL INVITATION ENUMS
    // ============================================
    public enum PortalInvitationStatus
    {
        Pending = 0,
        Registered = 1,
        Expired = 2
    }

    // ============================================
    // PATIENT INTAKE ENUMS
    // ============================================
    /// <summary>
    /// Provenance of a clinical row. Stamped at create time, immutable thereafter.
    /// Default on existing rows (set by migration 017) = Clinic.
    /// </summary>
    public enum IntakeSource
    {
        System = 0,
        Clinic = 1,
        Patient = 2,
        Kiosk = 3,
        Import = 4
    }

    /// <summary>
    /// Channel through which a patient submitted an intake session.
    /// </summary>
    public enum IntakeChannel
    {
        Portal = 0,
        Tablet = 1
    }

    // ============================================
    // HELPER CLASS FOR ENUM DISPLAY NAMES
    // ============================================
    public static class EnumHelper
    {
        public static string GetAppointmentStatusName(int status) => ((AppointmentStatus)status).ToString();
        public static string GetAppointmentTypeName(int type) => ((AppointmentType)type).ToString();
        public static string GetPatientStatusName(int status) => ((PatientStatus)status).ToString();
        public static string GetNoteStatusName(int status) => ((NoteStatus)status).ToString();
        public static string GetNoteTypeName(int type) => ((NoteType)type).ToString();
        public static string GetUnavailabilityTypeName(int type) => ((UnavailabilityType)type).ToString();
        public static string GetClaimStatusName(int status) => ((ClaimStatus)status).ToString();
        public static string GetPaymentTypeName(int type) => ((PaymentType)type).ToString();
        public static string GetPaymentMethodName(int method) => ((PaymentMethod)method).ToString();
        public static string GetCareEpisodeStatusName(int status) => status switch
        {
            0 => "Active",
            1 => "Overdue",
            2 => "Completed",
            3 => "On Hold",
            _ => "Unknown"
        };
        public static string GetCareEpisodeCompletionMethodName(int method) => method switch
        {
            0 => "None",
            1 => "Discharge Appointment",
            2 => "Manual",
            _ => "Unknown"
        };

        public static string GetPrescriptionStatusName(int status) => status switch
        {
            0 => "Draft",
            1 => "Active",
            2 => "Sent",
            3 => "Filled",
            4 => "Cancelled",
            5 => "Expired",
            _ => "Unknown"
        };

        public static string GetDosageFormName(int form) => form switch
        {
            0 => "Tablet",
            1 => "Capsule",
            2 => "Liquid",
            3 => "Cream",
            4 => "Ointment",
            5 => "Patch",
            6 => "Injection",
            7 => "Inhaler",
            8 => "Drops",
            9 => "Suppository",
            99 => "Other",
            _ => "Unknown"
        };

        public static string GetMedicationRouteName(int route) => route switch
        {
            0 => "Oral",
            1 => "Topical",
            2 => "Subcutaneous",
            3 => "Intramuscular",
            4 => "Intravenous",
            5 => "Rectal",
            6 => "Ophthalmic",
            7 => "Otic",
            8 => "Nasal",
            9 => "Transdermal",
            10 => "Inhalation",
            _ => "Unknown"
        };

        public static string GetMedicationFrequencyName(int freq) => freq switch
        {
            0 => "Once daily",
            1 => "Twice daily (BID)",
            2 => "Three times daily (TID)",
            3 => "Four times daily (QID)",
            4 => "At bedtime (QHS)",
            5 => "Every 4 hours",
            6 => "Every 6 hours",
            7 => "Every 8 hours",
            8 => "Every 12 hours",
            9 => "As needed (PRN)",
            10 => "Weekly",
            11 => "Every 2 weeks",
            12 => "Monthly",
            99 => "As directed",
            _ => "Unknown"
        };

        // Orders Module helpers
        public static string GetOrderTypeName(int type) => type switch
        {
            0 => "Lab",
            1 => "Imaging",
            2 => "Referral",
            _ => "Unknown"
        };

        public static string GetOrderStatusName(int status) => status switch
        {
            0 => "Draft",
            1 => "Pending",
            2 => "Sent",
            3 => "In Progress",
            4 => "Results Received",
            5 => "Completed",
            6 => "Cancelled",
            _ => "Unknown"
        };

        public static string GetOrderPriorityName(int priority) => priority switch
        {
            0 => "Routine",
            1 => "Urgent",
            2 => "STAT",
            _ => "Unknown"
        };

        public static string GetImagingModalityName(int modality) => modality switch
        {
            0 => "X-Ray",
            1 => "CT Scan",
            2 => "MRI",
            3 => "Ultrasound",
            4 => "Mammogram",
            5 => "DEXA Scan",
            6 => "Fluoroscopy",
            7 => "PET Scan",
            8 => "Nuclear Medicine",
            _ => "Unknown"
        };

        public static string GetReferralUrgencyName(int urgency) => urgency switch
        {
            0 => "Routine",
            1 => "Urgent",
            2 => "Emergent",
            _ => "Unknown"
        };
    }
}
