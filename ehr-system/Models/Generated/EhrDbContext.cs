using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace EHR.Models.Generated;

public partial class EhrDbContext : DbContext
{
    public EhrDbContext()
    {
    }

    public EhrDbContext(DbContextOptions<EhrDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Appointment> Appointments { get; set; }

    public virtual DbSet<AuditLog> AuditLogs { get; set; }

    public virtual DbSet<Authorization> Authorizations { get; set; }

    public virtual DbSet<BillingClaim> BillingClaims { get; set; }

    public virtual DbSet<CareEpisode> CareEpisodes { get; set; }

    public virtual DbSet<Charge> Charges { get; set; }

    public virtual DbSet<ClaimStatusHistory> ClaimStatusHistories { get; set; }

    public virtual DbSet<ClinicalNote> ClinicalNotes { get; set; }

    public virtual DbSet<ClinicalNoteAmendment> ClinicalNoteAmendments { get; set; }

    public virtual DbSet<ClinicalNoteAddendum> ClinicalNoteAddendums { get; set; }

    public virtual DbSet<ClinicalNoteTemplate> ClinicalNoteTemplates { get; set; }

    public virtual DbSet<Consent> Consents { get; set; }

    public virtual DbSet<Cptcode> Cptcodes { get; set; }

    public virtual DbSet<CredentialingRecord> CredentialingRecords { get; set; }

    public virtual DbSet<Icdcode> Icdcodes { get; set; }

    public virtual DbSet<Insurance> Insurances { get; set; }

    public virtual DbSet<Location> Locations { get; set; }

    public virtual DbSet<Note> Notes { get; set; }

    public virtual DbSet<Patient> Patients { get; set; }

    public virtual DbSet<PatientValidation> PatientValidations { get; set; }

    public virtual DbSet<PatientDocument> PatientDocuments { get; set; }

    public virtual DbSet<PatientLedger> PatientLedgers { get; set; }

    public virtual DbSet<PatientSearchToken> PatientSearchTokens { get; set; }

    public virtual DbSet<Payer> Payers { get; set; }

    public virtual DbSet<Payment> Payments { get; set; }

    public virtual DbSet<Provider> Providers { get; set; }

    public virtual DbSet<ProviderSchedule> ProviderSchedules { get; set; }

    public virtual DbSet<Tenant> Tenants { get; set; }

    public virtual DbSet<TherapistUnavailability> TherapistUnavailabilities { get; set; }

    public virtual DbSet<User> Users { get; set; }

    // Trusted devices for "Remember this device" (15-day OTP bypass)
    public virtual DbSet<TrustedDevice> TrustedDevices { get; set; }

    public virtual DbSet<VwAraging> VwAragings { get; set; }

    public virtual DbSet<VwTodaySchedule> VwTodaySchedules { get; set; }

    public virtual DbSet<SystemSetting> SystemSettings { get; set; }

    // Patient Consent System entities
    public virtual DbSet<LocationKioskSettings> LocationKioskSettings { get; set; }

    public virtual DbSet<ConsentFormTemplate> ConsentFormTemplates { get; set; }

    public virtual DbSet<CareEpisodeConsent> CareEpisodeConsents { get; set; }

    public virtual DbSet<CareEpisodeConsentForm> CareEpisodeConsentForms { get; set; }

    public virtual DbSet<KioskVerificationAttempt> KioskVerificationAttempts { get; set; }

    public virtual DbSet<KioskSession> KioskSessions { get; set; }

    // Recording Session entities
    public virtual DbSet<RecordingSession> RecordingSessions { get; set; }

    public virtual DbSet<TranscriptionChunk> TranscriptionChunks { get; set; }

    // Telehealth Transcription Chunks (persisted from AI Scribe)
    public virtual DbSet<TelehealthTranscriptionChunk> TelehealthTranscriptionChunks { get; set; }

    // Medical Lien Template entities
    public virtual DbSet<MedicalLienTemplate> MedicalLienTemplates { get; set; }

    // Internal Messaging System entities
    public virtual DbSet<Conversation> Conversations { get; set; }
    public virtual DbSet<Message> Messages { get; set; }
    public virtual DbSet<UserPresence> UserPresences { get; set; }

    public virtual DbSet<ProviderFavoriteCode> ProviderFavoriteCodes { get; set; }

    // Internal Medicine entities
    public virtual DbSet<Encounter> Encounters { get; set; }
    public virtual DbSet<PatientProblem> PatientProblems { get; set; }
    public virtual DbSet<PatientAllergy> PatientAllergies { get; set; }
    public virtual DbSet<PatientMedication> PatientMedications { get; set; }
    public virtual DbSet<PatientVital> PatientVitals { get; set; }
    public virtual DbSet<PatientImmunization> PatientImmunizations { get; set; }
    public virtual DbSet<PatientFamilyHistory> PatientFamilyHistories { get; set; }
    public virtual DbSet<PatientSocialHistory> PatientSocialHistories { get; set; }
    public virtual DbSet<TreatmentPlan> TreatmentPlans { get; set; }

    // E-Prescribing
    public virtual DbSet<Prescription> Prescriptions { get; set; }
    public virtual DbSet<Pharmacy> Pharmacies { get; set; }
    public virtual DbSet<DrugDatabase> DrugDatabases { get; set; }

    // Orders Module (Labs, Imaging, Referrals)
    public virtual DbSet<Order> Orders { get; set; }
    public virtual DbSet<OrderResult> OrderResults { get; set; }
    public virtual DbSet<LabTestCatalog> LabTestCatalogs { get; set; }

    // Patient Sticky Notes (informal, non-clinical)
    public virtual DbSet<PatientStickyNote> PatientStickyNotes { get; set; }

    // Care Notes (structured staff -> provider communication)
    public virtual DbSet<CareNote> CareNotes { get; set; }

    // Patient Portal Auth (Email + Password + OTP)
    public virtual DbSet<PatientPortalAccount> PatientPortalAccounts { get; set; }
    public virtual DbSet<PatientPortalInvitation> PatientPortalInvitations { get; set; }
    public virtual DbSet<PatientPortalOtp> PatientPortalOtps { get; set; }
    public virtual DbSet<PatientPortalPasswordReset> PatientPortalPasswordResets { get; set; }

    // Copay Integration - Installment Plans
    public virtual DbSet<InstallmentPlan> InstallmentPlans { get; set; }
    public virtual DbSet<InstallmentDetail> InstallmentDetails { get; set; }
    public virtual DbSet<CopayPaymentToken> CopayPaymentTokens { get; set; }

    // Patient-Provider Messaging System
    public virtual DbSet<PatientConversation> PatientConversations { get; set; }
    public virtual DbSet<PatientMessage> PatientMessages { get; set; }

    // Stripe Connect (Phase 1)
    public virtual DbSet<StripeConnectAccount> StripeConnectAccounts { get; set; }
    public virtual DbSet<StripeWebhookEvent> StripeWebhookEvents { get; set; }
    public virtual DbSet<PaymentRefund> PaymentRefunds { get; set; }
    public virtual DbSet<InstallmentPlanAuditLog> InstallmentPlanAuditLogs { get; set; }

    // Patient Intake (Phase 1)
    public virtual DbSet<PatientIntakeSubmission> PatientIntakeSubmissions { get; set; }

    public virtual DbSet<PatientGenderHealth> PatientGenderHealths { get; set; }
    public virtual DbSet<PatientHealthConcern> PatientHealthConcerns { get; set; }
    public virtual DbSet<PatientSupplement> PatientSupplements { get; set; }
    public virtual DbSet<PatientLongevityProfile> PatientLongevityProfiles { get; set; }
    public virtual DbSet<IntakeVerificationAttempt> IntakeVerificationAttempts { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Only use fallback if not already configured via DI (Program.cs)
        if (!optionsBuilder.IsConfigured)
        {
            // This is a development fallback - production uses appsettings.json via DI
            optionsBuilder.UseSqlServer("Server=(local);Database=IMEHR;Trusted_Connection=True;Encrypt=false");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Appointment>(entity =>
        {
            entity.HasKey(e => e.AppointmentId).HasName("PK__Appointm__8ECDFCC26AA138E3");

            entity.HasIndex(e => new { e.TenantId, e.ProviderId, e.StartTime }, "IX_Appointments_TenantId_ProviderId_StartTime");

            entity.HasIndex(e => new { e.TenantId, e.StartTime }, "IX_Appointments_TenantId_StartTime");

            entity.Property(e => e.CopayCollected).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.CopayDue).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.InsuranceVerified).HasDefaultValue(false);
            entity.Property(e => e.IsRecurring).HasDefaultValue(false);
            entity.Property(e => e.IsTelehealth).HasDefaultValue(false);
            entity.Property(e => e.RecurrencePattern).HasMaxLength(100);
            entity.Property(e => e.Status).HasDefaultValue(0);
            entity.Property(e => e.TelehealthUrl).HasMaxLength(500);
            entity.Property(e => e.CreatedByUserId).HasColumnName("CreatedByUserId");

            entity.HasOne(d => d.CareEpisode).WithMany(p => p.Appointments)
                .HasForeignKey(d => d.CareEpisodeId)
                .HasConstraintName("FK__Appointme__CareE__05D8E0BE");

            entity.HasOne(d => d.Location).WithMany(p => p.Appointments)
                .HasForeignKey(d => d.LocationId)
                .HasConstraintName("FK__Appointme__Locat__04E4BC85");

            entity.HasOne(d => d.Patient).WithMany(p => p.Appointments)
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Appointme__Patie__02FC7413");

            entity.HasOne(d => d.Provider).WithMany(p => p.Appointments)
                .HasForeignKey(d => d.ProviderId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Appointme__Provi__03F0984C");

            entity.HasOne(d => d.Tenant).WithMany(p => p.Appointments)
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Appointme__Tenan__02084FDA");
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.AuditId).HasName("PK__AuditLog__A17F2398BE9350B9");

            entity.HasIndex(e => new { e.TenantId, e.Timestamp }, "IX_AuditLogs_TenantId_Timestamp");

            entity.Property(e => e.Action)
                .IsRequired()
                .HasMaxLength(50);
            entity.Property(e => e.EntityType)
                .IsRequired()
                .HasMaxLength(100);
            entity.Property(e => e.IpAddress).HasMaxLength(50);
            entity.Property(e => e.Timestamp).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.UserAgent).HasMaxLength(500);
            entity.Property(e => e.UserEmail).HasMaxLength(100);

            entity.HasOne(d => d.Tenant).WithMany(p => p.AuditLogs)
                .HasForeignKey(d => d.TenantId)
                .HasConstraintName("FK__AuditLogs__Tenan__43D61337");
        });

        modelBuilder.Entity<BillingClaim>(entity =>
        {
            entity.HasKey(e => e.ClaimId).HasName("PK__Claims__EF2E139B3BE776A8");

            entity.HasIndex(e => new { e.TenantId, e.Status }, "IX_Claims_TenantId_Status");

            entity.Property(e => e.ClaimNumber).HasMaxLength(50);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.DenialReason).HasMaxLength(500);
            entity.Property(e => e.DenialReasonCode).HasMaxLength(50);
            entity.Property(e => e.Edidata).HasColumnName("EDIData");
            entity.Property(e => e.PatientResponsibility).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.PayerClaimNumber).HasMaxLength(50);
            entity.Property(e => e.Status).HasDefaultValue(0);
            entity.Property(e => e.TotalAdjustment).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.TotalAllowed).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.TotalCharged).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.TotalPaid).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.Type).HasDefaultValue(0);
            entity.Property(e => e.AmountPaid).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.ReferringProviderName).HasMaxLength(200);
            entity.Property(e => e.ReferringProviderNpi).HasMaxLength(20);
            entity.Property(e => e.PriorAuthorizationNumber).HasMaxLength(100);
            entity.Property(e => e.PlaceOfServiceCode).HasMaxLength(10);
            entity.Property(e => e.FederalTaxId).HasMaxLength(20);
            entity.Property(e => e.PatientAccountNumber).HasMaxLength(50);
            entity.Property(e => e.RenderingProviderName).HasMaxLength(200);
            entity.Property(e => e.RenderingProviderNpi).HasMaxLength(20);
            entity.Property(e => e.FacilityName).HasMaxLength(200);
            entity.Property(e => e.FacilityAddress).HasMaxLength(500);
            entity.Property(e => e.FacilityNpi).HasMaxLength(20);
            entity.Property(e => e.BillingProviderName).HasMaxLength(200);
            entity.Property(e => e.BillingProviderAddress).HasMaxLength(500);
            entity.Property(e => e.BillingProviderNpi).HasMaxLength(20);
            entity.Property(e => e.BillingProviderTaxonomy).HasMaxLength(20);
            entity.Property(e => e.InsuredName).HasMaxLength(200);
            entity.Property(e => e.InsuredAddress).HasMaxLength(500);
            entity.Property(e => e.InsuredCity).HasMaxLength(100);
            entity.Property(e => e.InsuredState).HasMaxLength(10);
            entity.Property(e => e.InsuredZip).HasMaxLength(20);
            entity.Property(e => e.InsuredGender).HasMaxLength(10);
            entity.Property(e => e.InsuredPolicyNumber).HasMaxLength(50);
            entity.Property(e => e.InsuredGroupNumber).HasMaxLength(50);
            entity.Property(e => e.SubscriberRelationship).HasMaxLength(20);
            entity.Property(e => e.TypeOfBill).HasMaxLength(10);
            entity.Property(e => e.PatientDischargeStatus).HasMaxLength(10);

            entity.HasOne(d => d.Insurance).WithMany(p => p.BillingClaims)
                .HasForeignKey(d => d.InsuranceId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Claims__Insuranc__2180FB33");

            entity.HasOne(d => d.Patient).WithMany(p => p.BillingClaims)
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Claims__PatientI__208CD6FA");

            entity.HasOne(d => d.Tenant).WithMany(p => p.BillingClaims)
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Claims__TenantId__1F98B2C1");

            entity.HasOne(d => d.ClinicalNote).WithMany()
                .HasForeignKey(d => d.ClinicalNoteId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_BillingClaims_ClinicalNotes");

            entity.HasOne(d => d.Appointment).WithMany()
                .HasForeignKey(d => d.AppointmentId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_BillingClaims_Appointments");

            entity.HasOne(d => d.CareEpisode).WithMany()
                .HasForeignKey(d => d.CareEpisodeId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_BillingClaims_CareEpisodes");

            entity.HasOne(d => d.Authorization).WithMany()
                .HasForeignKey(d => d.AuthorizationId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_BillingClaims_Authorizations");

            entity.HasOne(d => d.Provider).WithMany()
                .HasForeignKey(d => d.ProviderId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_BillingClaims_Providers");

            entity.HasOne(d => d.Location).WithMany()
                .HasForeignKey(d => d.LocationId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_BillingClaims_Locations");
        });

        modelBuilder.Entity<Authorization>(entity =>
        {
            entity.HasKey(e => e.AuthorizationId).HasName("PK__Authorizations");

            entity.HasIndex(e => new { e.TenantId, e.InsuranceId }, "IX_Authorizations_TenantId_InsuranceId");

            entity.HasIndex(e => new { e.InsuranceId, e.DateOfValidation }, "IX_Authorizations_InsuranceId_DateOfValidation").IsDescending(false, true);

            entity.Property(e => e.AuthorizationNumber)
                .IsRequired()
                .HasMaxLength(100);
            entity.Property(e => e.AuthorizedVisits).HasDefaultValue(0);
            entity.Property(e => e.DateOfValidation).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.Notes).HasMaxLength(1000);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");

            // Foreign key to Insurance (NO CASCADE DELETE - preserve historical records)
            entity.HasOne(d => d.Insurance).WithMany(p => p.Authorizations)
                .HasForeignKey(d => d.InsuranceId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_Authorizations_InsuranceId");

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Authorizations_TenantId");
        });

        modelBuilder.Entity<CareEpisode>(entity =>
        {
            entity.HasKey(e => e.CareEpisodeId).HasName("PK__CareEpis__C57E6C09E3C7B978");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.PrimaryDiagnosisCode).HasMaxLength(20);
            entity.Property(e => e.PrimaryDiagnosisDescription).HasMaxLength(500);
            entity.Property(e => e.Status).HasDefaultValue(0);
            entity.Property(e => e.ReferringProviderNpi).HasMaxLength(20);

            entity.HasOne(d => d.Patient).WithMany(p => p.CareEpisodes)
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__CareEpiso__Patie__787EE5A0");

            entity.HasOne(d => d.PrimaryProvider).WithMany(p => p.CareEpisodes)
                .HasForeignKey(d => d.PrimaryProviderId)
                .HasConstraintName("FK__CareEpiso__Prima__797309D9");

            entity.HasOne(d => d.Tenant).WithMany(p => p.CareEpisodes)
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__CareEpiso__Tenan__778AC167");
        });

        modelBuilder.Entity<Charge>(entity =>
        {
            entity.HasKey(e => e.ChargeId).HasName("PK__Charges__17FC361B518C6080");

            entity.HasIndex(e => new { e.TenantId, e.ServiceDate }, "IX_Charges_TenantId_ServiceDate");

            entity.Property(e => e.AdjustmentAmount).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.AllowedAmount).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.ChargeAmount).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.Cptcode)
                .IsRequired()
                .HasMaxLength(10)
                .HasColumnName("CPTCode");
            entity.Property(e => e.Cptdescription)
                .HasMaxLength(200)
                .HasColumnName("CPTDescription");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.Icdpointers).HasColumnName("ICDPointers");
            entity.Property(e => e.Modifier1).HasMaxLength(10);
            entity.Property(e => e.Modifier2).HasMaxLength(10);
            entity.Property(e => e.Modifier3).HasMaxLength(10);
            entity.Property(e => e.Modifier4).HasMaxLength(10);
            entity.Property(e => e.PaidAmount).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.PatientResponsibility).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.RevenueCode).HasMaxLength(10);
            entity.Property(e => e.Status).HasDefaultValue(0);
            entity.Property(e => e.Units).HasDefaultValue(1);

            entity.HasOne(d => d.Appointment).WithMany(p => p.Charges)
                .HasForeignKey(d => d.AppointmentId)
                .HasConstraintName("FK__Charges__Appoint__18EBB532");

            entity.HasOne(d => d.Claim).WithMany(p => p.Charges)
                .HasForeignKey(d => d.ClaimId)
                .HasConstraintName("FK__Charges__ClaimId__22751F6C");

            // NoteId FK is deprecated - will be removed in migration
            entity.HasOne(d => d.Note).WithMany(p => p.Charges)
                .HasForeignKey(d => d.NoteId)
                .HasConstraintName("FK__Charges__NoteId__17F790F9");

            // ClinicalNoteId FK - the preferred reference for clinical documentation
            entity.HasOne(d => d.ClinicalNote).WithMany()
                .HasForeignKey(d => d.ClinicalNoteId)
                .HasConstraintName("FK__Charges__ClinicalNoteId");

            entity.HasOne(d => d.Patient).WithMany(p => p.Charges)
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Charges__Patient__17036CC0");

            entity.HasOne(d => d.Provider).WithMany(p => p.Charges)
                .HasForeignKey(d => d.ProviderId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Charges__Provide__19DFD96B");

            entity.HasOne(d => d.Tenant).WithMany(p => p.Charges)
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Charges__TenantI__160F4887");
        });

        modelBuilder.Entity<ClaimStatusHistory>(entity =>
        {
            entity.HasKey(e => e.HistoryId).HasName("PK__ClaimSta__4D7B4ABDC7D0B9B1");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.Notes).HasMaxLength(500);

            entity.HasOne(d => d.Claim).WithMany(p => p.ClaimStatusHistories)
                .HasForeignKey(d => d.ClaimId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ClaimStat__Claim__2739D489");

            entity.HasOne(d => d.Tenant).WithMany(p => p.ClaimStatusHistories)
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ClaimStat__Tenan__2645B050");
        });

        modelBuilder.Entity<ClinicalNote>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.AppointmentId }, "IX_ClinicalNotes_Appointment");

            entity.HasIndex(e => new { e.TenantId, e.PatientId, e.ServiceDate }, "IX_ClinicalNotes_Patient").IsDescending(false, false, true);

            entity.HasIndex(e => new { e.TenantId, e.ProviderId, e.ServiceDate }, "IX_ClinicalNotes_Provider").IsDescending(false, false, true);

            entity.HasIndex(e => e.SearchHash, "IX_ClinicalNotes_SearchHash").HasFilter("([SearchHash] IS NOT NULL)");

            entity.HasIndex(e => new { e.TenantId, e.Status, e.ServiceDate }, "IX_ClinicalNotes_Status").IsDescending(false, false, true);

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.HtmlContent).IsRequired();
            entity.Property(e => e.SearchHash).HasMaxLength(100);

            entity.HasOne(d => d.Appointment).WithMany(p => p.ClinicalNotes)
                .HasForeignKey(d => d.AppointmentId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_ClinicalNotes_Appointments");

            entity.HasOne(d => d.Encounter).WithMany(p => p.ClinicalNotes)
                .HasForeignKey(d => d.EncounterId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_ClinicalNotes_Encounters");

            entity.HasOne(d => d.Patient).WithMany(p => p.ClinicalNotes)
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ClinicalNotes_Patients");

            entity.HasOne(d => d.Provider).WithMany(p => p.ClinicalNotes)
                .HasForeignKey(d => d.ProviderId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ClinicalNotes_Providers");

            entity.HasOne(d => d.Template).WithMany(p => p.ClinicalNotes)
                .HasForeignKey(d => d.TemplateId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_ClinicalNotes_Templates");

            entity.HasOne(d => d.Tenant).WithMany(p => p.ClinicalNotes)
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ClinicalNotes_Tenants");
        });

        modelBuilder.Entity<ClinicalNoteAmendment>(entity =>
        {
            entity.HasKey(e => e.AmendmentId);

            entity.HasIndex(e => new { e.ClinicalNoteId, e.VersionNumber }, "IX_ClinicalNoteAmendments_ClinicalNoteId");
            entity.HasIndex(e => e.TenantId, "IX_ClinicalNoteAmendments_TenantId");
            entity.HasIndex(e => new { e.ClinicalNoteId, e.VersionNumber }, "UX_ClinicalNoteAmendments_NoteVersion").IsUnique();

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.HtmlContent).IsRequired();
            entity.Property(e => e.Reason).IsRequired().HasMaxLength(500);

            entity.HasOne(d => d.ClinicalNote).WithMany(p => p.Amendments)
                .HasForeignKey(d => d.ClinicalNoteId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_ClinicalNoteAmendments_ClinicalNotes");

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ClinicalNoteAmendments_Tenants");

            entity.HasOne(d => d.SignedByUser).WithMany()
                .HasForeignKey(d => d.SignedByUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ClinicalNoteAmendments_Users");
        });

        modelBuilder.Entity<ClinicalNoteAddendum>(entity =>
        {
            entity.HasKey(e => e.AddendumId);

            entity.HasIndex(e => new { e.ClinicalNoteId, e.CreatedAt }, "IX_ClinicalNoteAddendums_ClinicalNoteId");
            entity.HasIndex(e => e.TenantId, "IX_ClinicalNoteAddendums_TenantId");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.Reason).IsRequired().HasMaxLength(500);

            entity.HasOne(d => d.ClinicalNote).WithMany(p => p.Addendums)
                .HasForeignKey(d => d.ClinicalNoteId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_ClinicalNoteAddendums_ClinicalNotes");

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ClinicalNoteAddendums_Tenants");

            entity.HasOne(d => d.SignedByUser).WithMany()
                .HasForeignKey(d => d.SignedByUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ClinicalNoteAddendums_Users");
        });

        modelBuilder.Entity<ClinicalNoteTemplate>(entity =>
        {
            entity.HasKey(e => e.TemplateId);

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.HtmlContent).IsRequired();
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(200);

            entity.HasOne(d => d.Location).WithMany()
                .HasForeignKey(d => d.LocationId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Consent>(entity =>
        {
            entity.HasKey(e => e.ConsentId).HasName("PK__Consents__374AB08600104622");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.DocumentUrl).HasMaxLength(500);
            entity.Property(e => e.IpAddress).HasMaxLength(50);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.SignedBy)
                .IsRequired()
                .HasMaxLength(200);
            entity.Property(e => e.Version)
                .HasMaxLength(20)
                .HasDefaultValue("1.0");

            entity.HasOne(d => d.Patient).WithMany(p => p.Consents)
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Consents__Patien__70DDC3D8");

            entity.HasOne(d => d.Tenant).WithMany(p => p.Consents)
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Consents__Tenant__6FE99F9F");
        });

        modelBuilder.Entity<Cptcode>(entity =>
        {
            entity.HasKey(e => e.Code).HasName("PK__CPTCodes__A25C5AA6F5A3B91D");

            entity.ToTable("CPTCodes");

            entity.Property(e => e.Code).HasMaxLength(10);
            entity.Property(e => e.Category).HasMaxLength(50);
            entity.Property(e => e.Description)
                .IsRequired()
                .HasMaxLength(500);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.IsTimeBased).HasDefaultValue(false);
        });

        modelBuilder.Entity<CredentialingRecord>(entity =>
        {
            entity.HasKey(e => e.CredentialingId).HasName("PK__Credenti__531E675F1CE73C60");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.PayerName).HasMaxLength(100);
            entity.Property(e => e.Source).HasMaxLength(100);
            entity.Property(e => e.Status).HasDefaultValue(0);

            entity.HasOne(d => d.Provider).WithMany(p => p.CredentialingRecords)
                .HasForeignKey(d => d.ProviderId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Credentia__Provi__5AEE82B9");

            entity.HasOne(d => d.Tenant).WithMany(p => p.CredentialingRecords)
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Credentia__Tenan__59FA5E80");
        });

        modelBuilder.Entity<Icdcode>(entity =>
        {
            entity.HasKey(e => e.Code).HasName("PK__ICDCodes__A25C5AA6902FF359");

            entity.ToTable("ICDCodes");

            entity.Property(e => e.Code).HasMaxLength(20);
            entity.Property(e => e.Category).HasMaxLength(50);
            entity.Property(e => e.Description)
                .IsRequired()
                .HasMaxLength(500);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        modelBuilder.Entity<Insurance>(entity =>
        {
            entity.HasKey(e => e.InsuranceId).HasName("PK__Insuranc__74231A243A87D557");

            entity.Property(e => e.Coinsurance).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.Copay).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.Deductible).HasColumnType("numeric(10, 2)");
            entity.Property(e => e.DeductibleMet).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.DeductibleTotal).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.AllowedVisits).HasDefaultValue(null);
            entity.Property(e => e.GroupNumber).HasMaxLength(500);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.PayerId).HasMaxLength(50);
            entity.Property(e => e.PayerName)
                .IsRequired()
                .HasMaxLength(100);
            entity.Property(e => e.PolicyNumber)
                .IsRequired()
                .HasMaxLength(500);
            entity.Property(e => e.SubscriberDob).HasColumnName("SubscriberDOB");
            entity.Property(e => e.SubscriberId).HasMaxLength(500);
            entity.Property(e => e.SubscriberName).HasMaxLength(500);
            entity.Property(e => e.SubscriberRelationship).HasMaxLength(50);
            entity.Property(e => e.Type).HasDefaultValue(0);

            entity.HasOne(d => d.Patient).WithMany(p => p.Insurances)
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Insurance__Patie__6A30C649");

            entity.HasOne(d => d.Tenant).WithMany(p => p.Insurances)
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Insurance__Tenan__693CA210");
        });

        modelBuilder.Entity<Location>(entity =>
        {
            entity.HasKey(e => e.LocationId).HasName("PK__Location__E7FEA497837E38AA");

            entity.Property(e => e.Address).HasMaxLength(500);
            entity.Property(e => e.City).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.IsPrimary).HasDefaultValue(false);
            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(100);
            entity.Property(e => e.Phone).HasMaxLength(20);
            entity.Property(e => e.State).HasMaxLength(50);
            entity.Property(e => e.ZipCode).HasMaxLength(20);
            entity.Property(e => e.PlaceOfServiceCode).HasMaxLength(10);
            entity.Property(e => e.FacilityNpi).HasMaxLength(20);

            entity.HasOne(d => d.Tenant).WithMany(p => p.Locations)
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Locations__Tenan__4316F928");
        });

        // NOTE: The Notes table is deprecated and will be removed in a future migration.
        // All clinical documentation should use ClinicalNotes instead.
        // The Note entity configuration is preserved for the migration script only.
        modelBuilder.Entity<Note>(entity =>
        {
            entity.HasKey(e => e.NoteId).HasName("PK__Notes__EACE355FB8248B2C");

            entity.HasIndex(e => new { e.TenantId, e.PatientId, e.ServiceDate }, "IX_Notes_TenantId_PatientId_ServiceDate");

            entity.Property(e => e.Cptcodes).HasColumnName("CPTCodes");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.Icdcodes).HasColumnName("ICDCodes");
            entity.Property(e => e.IsAddendum).HasDefaultValue(false);
            entity.Property(e => e.Status).HasDefaultValue(0);

            // Navigation configurations removed as the related navigation properties have been removed
            // The FK constraints still exist in the database and will be dropped in the migration

            entity.HasOne(d => d.Appointment).WithMany()
                .HasForeignKey(d => d.AppointmentId)
                .HasConstraintName("FK__Notes__Appointme__0E6E26BF");

            entity.HasOne(d => d.CareEpisode).WithMany()
                .HasForeignKey(d => d.CareEpisodeId)
                .HasConstraintName("FK__Notes__CareEpiso__0F624AF8");

            entity.HasOne(d => d.ParentNote).WithMany(p => p.InverseParentNote)
                .HasForeignKey(d => d.ParentNoteId)
                .HasConstraintName("FK__Notes__ParentNot__10566F31");

            entity.HasOne(d => d.Patient).WithMany()
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Notes__PatientId__0C85DE4D");

            entity.HasOne(d => d.Provider).WithMany()
                .HasForeignKey(d => d.ProviderId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Notes__ProviderI__0D7A0286");

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Notes__TenantId__0B91BA14");
        });

        modelBuilder.Entity<Patient>(entity =>
        {
            entity.HasKey(e => e.PatientId).HasName("PK__Patients__970EC366F88B725F");

            entity.HasIndex(e => new { e.TenantId, e.Mrn }, "IX_Patients_MRN_Search");

            entity.HasIndex(e => new { e.TenantId, e.LastName, e.FirstName }, "IX_Patients_Name_Search");

            entity.HasIndex(e => new { e.TenantId, e.Phone }, "IX_Patients_Phone_Search");

            entity.HasIndex(e => new { e.TenantId, e.Mrn }, "IX_Patients_TenantId_MRN").IsUnique();

            entity.Property(e => e.City).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.Email).HasMaxLength(500);
            entity.Property(e => e.EmergencyContactName).HasMaxLength(500);
            entity.Property(e => e.EmergencyContactPhone).HasMaxLength(500);
            entity.Property(e => e.EmergencyContactRelation).HasMaxLength(500);
            entity.Property(e => e.FirstName)
                .IsRequired()
                .HasMaxLength(500);
            entity.Property(e => e.Gender).HasMaxLength(20);
            entity.Property(e => e.IsDeleted).HasDefaultValue(false);
            entity.Property(e => e.LastName)
                .IsRequired()
                .HasMaxLength(500);
            entity.Property(e => e.Mrn)
                .IsRequired()
                .HasMaxLength(20)
                .HasColumnName("MRN");
            entity.Property(e => e.Phone).HasMaxLength(500);
            entity.Property(e => e.State).HasMaxLength(500);
            entity.Property(e => e.ZipCode).HasMaxLength(500);

            entity.HasOne(d => d.PreferredLocation).WithMany(p => p.Patients)
                .HasForeignKey(d => d.PreferredLocationId)
                .HasConstraintName("FK__Patients__Prefer__628FA481");

            entity.HasOne(d => d.PreferredProvider).WithMany(p => p.Patients)
                .HasForeignKey(d => d.PreferredProviderId)
                .HasConstraintName("FK__Patients__Prefer__619B8048");

            entity.HasOne(d => d.Tenant).WithMany(p => p.Patients)
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Patients__Tenant__60A75C0F");
        });

        modelBuilder.Entity<PatientValidation>(entity =>
        {
            entity.HasKey(e => e.PatientValidationId).HasName("PK__PatientValidations");

            entity.HasIndex(e => new { e.TenantId, e.PatientId }, "IX_PatientValidations_TenantId_PatientId").IsUnique();

            entity.HasIndex(e => new { e.TenantId, e.IsComplete }, "IX_PatientValidations_TenantId_IsComplete");

            entity.Property(e => e.IsComplete).HasDefaultValue(false);
            entity.Property(e => e.MissingFieldsCount).HasDefaultValue(0);
            entity.Property(e => e.MissingSections).HasMaxLength(1000);
            entity.Property(e => e.MissingFieldsDetails).HasMaxLength(4000);
            entity.Property(e => e.FirstIncompleteSection).HasMaxLength(50);
            entity.Property(e => e.ValidationSource).HasMaxLength(20);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");

            entity.HasOne(d => d.Patient).WithOne(p => p.PatientValidation)
                .HasForeignKey<PatientValidation>(d => d.PatientId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK__PatientValidations__PatientId");

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__PatientValidations__TenantId");
        });

        modelBuilder.Entity<PatientDocument>(entity =>
        {
            entity.HasKey(e => e.DocumentId).HasName("PK_PatientDocuments");

            entity.HasIndex(e => new { e.TenantId, e.PatientId }, "IX_PatientDocuments_TenantId_PatientId");

            entity.Property(e => e.FileName)
                .IsRequired()
                .HasMaxLength(500);
            entity.Property(e => e.StorageFileName)
                .IsRequired()
                .HasMaxLength(100);
            entity.Property(e => e.ContentType)
                .IsRequired()
                .HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.FileHash).HasMaxLength(100);
            entity.Property(e => e.IsEncrypted).HasDefaultValue(true);
            entity.Property(e => e.IsDeleted).HasDefaultValue(false);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");

            entity.HasOne(d => d.Patient).WithMany(p => p.PatientDocuments)
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PatientDocuments_Patients");

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PatientDocuments_Tenants");

            entity.HasOne(d => d.UploadedByUser).WithMany()
                .HasForeignKey(d => d.UploadedByUserId)
                .HasConstraintName("FK_PatientDocuments_Users");
        });

        modelBuilder.Entity<PatientLedger>(entity =>
        {
            entity.HasKey(e => e.LedgerId).HasName("PK__PatientL__AE70E0CF5E762058");

            entity.Property(e => e.Amount).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.Description).HasMaxLength(500);

            entity.HasOne(d => d.Charge).WithMany(p => p.PatientLedgers)
                .HasForeignKey(d => d.ChargeId)
                .HasConstraintName("FK__PatientLe__Charg__3587F3E0");

            entity.HasOne(d => d.Patient).WithMany(p => p.PatientLedgers)
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__PatientLe__Patie__3493CFA7");

            entity.HasOne(d => d.Payment).WithMany(p => p.PatientLedgers)
                .HasForeignKey(d => d.PaymentId)
                .HasConstraintName("FK__PatientLe__Payme__367C1819");

            entity.HasOne(d => d.Tenant).WithMany(p => p.PatientLedgers)
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__PatientLe__Tenan__339FAB6E");
        });

        // ============================================
        // PATIENT SEARCH TOKENS (Blind Index for HIPAA-compliant search)
        // ============================================
        modelBuilder.Entity<PatientSearchToken>(entity =>
        {
            entity.HasKey(e => e.PatientSearchTokenId);

            // Primary search index: TenantId + LocationId + TokenHash
            // This is the main index used for patient autocomplete searches
            entity.HasIndex(e => new { e.TenantId, e.LocationId, e.TokenHash }, "IX_PatientSearchTokens_Search");

            // Secondary index: TenantId + TokenHash (for searches without location filter)
            entity.HasIndex(e => new { e.TenantId, e.TokenHash }, "IX_PatientSearchTokens_TenantSearch");

            // Index for cleanup: PatientId (to delete all tokens when patient is deleted)
            entity.HasIndex(e => e.PatientId, "IX_PatientSearchTokens_PatientId");

            entity.Property(e => e.TokenHash)
                .IsRequired()
                .HasMaxLength(64); // Base64 of SHA256 = 44 chars, but allow some room

            entity.Property(e => e.FieldType)
                .IsRequired()
                .HasMaxLength(20);

            entity.HasOne(d => d.Patient).WithMany()
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_PatientSearchTokens_PatientId");

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PatientSearchTokens_TenantId");

            entity.HasOne(d => d.Location).WithMany()
                .HasForeignKey(d => d.LocationId)
                .HasConstraintName("FK_PatientSearchTokens_LocationId");
        });

        modelBuilder.Entity<Payer>(entity =>
        {
            entity.HasKey(e => e.PayerId).HasName("PK__Payers__0ADBE8677686BB41");

            entity.Property(e => e.Address).HasMaxLength(500);
            entity.Property(e => e.ClaimsUrl).HasMaxLength(100);
            entity.Property(e => e.EligibilityUrl).HasMaxLength(100);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(100);
            entity.Property(e => e.PayerIdCode).HasMaxLength(50);
            entity.Property(e => e.Phone).HasMaxLength(20);
            entity.Property(e => e.Website).HasMaxLength(100);
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasKey(e => e.PaymentId).HasName("PK__Payments__9B556A3827093A8E");

            entity.Property(e => e.Amount).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.CheckNumber).HasMaxLength(50);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.IsRefund).HasDefaultValue(false);
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.PayerName).HasMaxLength(100);
            entity.Property(e => e.Status).HasDefaultValue(1);
            entity.Property(e => e.TransactionId).HasMaxLength(100);
            entity.Property(e => e.StripePaymentIntentId).HasMaxLength(255);

            entity.HasOne(d => d.InstallmentDetail).WithOne(p => p.Payment)
                .HasForeignKey<Payment>(d => d.InstallmentDetailId)
                .HasConstraintName("FK_Payments_InstallmentDetails");

            entity.HasOne(d => d.Appointment).WithMany(p => p.Payments)
                .HasForeignKey(d => d.AppointmentId)
                .HasConstraintName("FK__Payments__Appoin__2EDAF651");

            entity.HasOne(d => d.Claim).WithMany(p => p.Payments)
                .HasForeignKey(d => d.ClaimId)
                .HasConstraintName("FK__Payments__ClaimI__2FCF1A8A");

            entity.HasOne(d => d.Patient).WithMany(p => p.Payments)
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Payments__Patien__2DE6D218");

            entity.HasOne(d => d.Tenant).WithMany(p => p.Payments)
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Payments__Tenant__2CF2ADDF");
        });

        modelBuilder.Entity<InstallmentPlan>(entity =>
        {
            entity.HasKey(e => e.PlanId);

            entity.Property(e => e.TotalAmount).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.StripeCustomerId).HasMaxLength(255);
            entity.Property(e => e.StripePaymentMethodId).HasMaxLength(255);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");

            entity.HasIndex(e => new { e.TenantId, e.PatientId })
                .HasDatabaseName("IX_InstallmentPlans_TenantId_PatientId");

            entity.HasOne(d => d.Patient).WithMany(p => p.InstallmentPlans)
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_InstallmentPlans_Patients");

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_InstallmentPlans_Tenants");
        });

        modelBuilder.Entity<InstallmentDetail>(entity =>
        {
            entity.HasKey(e => e.DetailId);

            entity.Property(e => e.Amount).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.StripePaymentIntentId).HasMaxLength(255);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");

            entity.HasIndex(e => new { e.TenantId, e.Status, e.DueDate })
                .HasDatabaseName("IX_InstallmentDetails_TenantId_Status_DueDate");

            entity.HasOne(d => d.Plan).WithMany(p => p.InstallmentDetails)
                .HasForeignKey(d => d.PlanId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_InstallmentDetails_Plans");

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_InstallmentDetails_Tenants");
        });

        modelBuilder.Entity<Provider>(entity =>
        {
            entity.HasKey(e => e.ProviderId).HasName("PK__Provider__B54C687DCE30F6EF");

            entity.Property(e => e.Color).HasMaxLength(7);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.CredentialStatus).HasDefaultValue(0);
            entity.Property(e => e.Credentials).HasMaxLength(50);
            entity.Property(e => e.DefaultAppointmentDuration).HasDefaultValue(30);
            entity.Property(e => e.Email).HasMaxLength(500);
            entity.Property(e => e.FirstName)
                .IsRequired()
                .HasMaxLength(100);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.LastName)
                .IsRequired()
                .HasMaxLength(100);
            entity.Property(e => e.LicenseNumber).HasMaxLength(50);
            entity.Property(e => e.LicenseState).HasMaxLength(50);
            entity.Property(e => e.Npi)
                .IsRequired()
                .HasMaxLength(10)
                .HasColumnName("NPI");
            entity.Property(e => e.Phone).HasMaxLength(500);
            entity.Property(e => e.Specialty).HasMaxLength(100);
            entity.Property(e => e.Taxonomy).HasMaxLength(20);
            entity.Property(e => e.ShowResumePopup).HasDefaultValue(true);

            entity.HasOne(d => d.Tenant).WithMany(p => p.Providers)
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Providers__Tenan__4F7CD00D");
        });

        modelBuilder.Entity<ProviderSchedule>(entity =>
        {
            entity.HasKey(e => e.ScheduleId).HasName("PK__Provider__9C8A5B49F3345BB5");

            entity.Property(e => e.IsAvailable).HasDefaultValue(true);

            entity.HasOne(d => d.Location).WithMany(p => p.ProviderSchedules)
                .HasForeignKey(d => d.LocationId)
                .HasConstraintName("FK__ProviderS__Locat__5535A963");

            entity.HasOne(d => d.Provider).WithMany(p => p.ProviderSchedules)
                .HasForeignKey(d => d.ProviderId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ProviderS__Provi__5441852A");

            entity.HasOne(d => d.Tenant).WithMany(p => p.ProviderSchedules)
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__ProviderS__Tenan__534D60F1");
        });

        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasKey(e => e.TenantId).HasName("PK__Tenants__2E9B47E1790FFCEE");

            entity.HasIndex(e => e.Subdomain, "UQ__Tenants__AD95567D6E20C3F5").IsUnique();

            entity.Property(e => e.Address).HasMaxLength(500);
            entity.Property(e => e.City).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.Email).HasMaxLength(100);
            entity.Property(e => e.IsDeleted).HasDefaultValue(false);
            entity.Property(e => e.LogoUrl).HasMaxLength(500);
            entity.Property(e => e.MaxPatients).HasDefaultValue(500);
            entity.Property(e => e.MaxUsers).HasDefaultValue(5);
            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(100);
            entity.Property(e => e.Npi)
                .HasMaxLength(10)
                .HasColumnName("NPI");
            entity.Property(e => e.Phone).HasMaxLength(20);
            entity.Property(e => e.Plan).HasDefaultValue(1);
            entity.Property(e => e.State).HasMaxLength(50);
            entity.Property(e => e.Status).HasDefaultValue(1);
            entity.Property(e => e.Subdomain)
                .IsRequired()
                .HasMaxLength(50);
            entity.Property(e => e.TaxId).HasMaxLength(20);
            entity.Property(e => e.ZipCode).HasMaxLength(20);
        });

        modelBuilder.Entity<TherapistUnavailability>(entity =>
        {
            entity.HasKey(e => e.UnavailabilityId);

            entity.HasIndex(e => new { e.TenantId, e.StartDate, e.EndDate }, "IX_TherapistUnavailabilities_Dates");

            entity.HasIndex(e => new { e.TenantId, e.ProviderId, e.StartDate, e.EndDate }, "IX_TherapistUnavailabilities_Provider_Dates");

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.IsFullDay).HasDefaultValue(true);
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.Property(e => e.RecurrencePattern).HasMaxLength(100);

            entity.HasOne(d => d.Provider).WithMany(p => p.TherapistUnavailabilities)
                .HasForeignKey(d => d.ProviderId)
                .HasConstraintName("FK_TherapistUnavailabilities_Providers");

            entity.HasOne(d => d.Tenant).WithMany(p => p.TherapistUnavailabilities)
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_TherapistUnavailabilities_Tenants");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PK__Users__1788CC4C65BDD67C");

            entity.HasIndex(e => new { e.Email, e.TenantId }, "IX_Users_Email_TenantId").IsUnique();

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.Email)
                .IsRequired()
                .HasMaxLength(500);
            entity.Property(e => e.FirstName)
                .IsRequired()
                .HasMaxLength(100);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.LastName)
                .IsRequired()
                .HasMaxLength(100);
            entity.Property(e => e.PasswordHash).IsRequired();
            entity.Property(e => e.PasswordResetToken).HasMaxLength(255);
            entity.Property(e => e.Phone).HasMaxLength(500);
            entity.Property(e => e.Role).HasDefaultValue(3);
            entity.Property(e => e.OtpCode).HasMaxLength(10);
            entity.Property(e => e.OtpAttempts).HasDefaultValue(0);

            entity.HasOne(d => d.Provider).WithMany(p => p.Users)
                .HasForeignKey(d => d.ProviderId)
                .HasConstraintName("FK__Users__ProviderI__44CA3770");

            entity.HasOne(d => d.Tenant).WithMany(p => p.Users)
                .HasForeignKey(d => d.TenantId)
                .HasConstraintName("FK__Users__TenantId__48CFD27E");
        });

        modelBuilder.Entity<TrustedDevice>(entity =>
        {
            entity.HasKey(e => e.TrustedDeviceId);
            entity.ToTable("TrustedDevices");

            entity.HasIndex(e => e.DeviceTokenHash, "UX_TrustedDevices_TokenHash").IsUnique();
            entity.HasIndex(e => new { e.UserId, e.ExpiresAt }, "IX_TrustedDevices_UserId_ExpiresAt");

            entity.Property(e => e.DeviceTokenHash).IsRequired().HasMaxLength(128);
            entity.Property(e => e.UserAgent).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");

            entity.HasOne(d => d.User).WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_TrustedDevices_Users");
        });

        modelBuilder.Entity<VwAraging>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_ARaging");

            entity.Property(e => e.Current0To30).HasColumnType("decimal(38, 2)");
            entity.Property(e => e.Days31To60).HasColumnType("decimal(38, 2)");
            entity.Property(e => e.Days61To90).HasColumnType("decimal(38, 2)");
            entity.Property(e => e.Days91To120).HasColumnType("decimal(38, 2)");
            entity.Property(e => e.Over120Days).HasColumnType("decimal(38, 2)");
            entity.Property(e => e.TotalAr)
                .HasColumnType("decimal(38, 2)")
                .HasColumnName("TotalAR");
        });

        modelBuilder.Entity<VwTodaySchedule>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_TodaySchedule");

            entity.Property(e => e.CopayCollected).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.CopayDue).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.LocationName).HasMaxLength(100);
            entity.Property(e => e.Mrn)
                .IsRequired()
                .HasMaxLength(20)
                .HasColumnName("MRN");
            entity.Property(e => e.PatientName)
                .IsRequired()
                .HasMaxLength(201);
            entity.Property(e => e.ProviderColor).HasMaxLength(7);
            entity.Property(e => e.ProviderName)
                .IsRequired()
                .HasMaxLength(201);
        });

        modelBuilder.Entity<SystemSetting>(entity =>
        {
            entity.HasKey(e => e.SystemSettingId).HasName("PK__SystemSettings");

            entity.HasIndex(e => new { e.TenantId, e.SettingKey }, "IX_SystemSettings_TenantId_SettingKey").IsUnique();

            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.SettingKey)
                .IsRequired()
                .HasMaxLength(100);
            entity.Property(e => e.SettingValue)
                .IsRequired()
                .HasMaxLength(1000);
            entity.Property(e => e.DataType)
                .IsRequired()
                .HasMaxLength(50)
                .HasDefaultValue("string");
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.Category).HasMaxLength(100);
            entity.Property(e => e.DefaultValue).HasMaxLength(1000);

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .HasConstraintName("FK__SystemSettings__TenantId");
        });

        // ============================================
        // PATIENT SSN INDEX (for consent verification)
        // ============================================
        modelBuilder.Entity<Patient>(entity =>
        {
            // Add index for patient verification lookup
            entity.HasIndex(e => new { e.TenantId, e.SsnLast4Hash, e.DateOfBirth, e.ZipCode }, "IX_Patients_Verification")
                .HasFilter("[SsnLast4Hash] IS NOT NULL");
        });

        // ============================================
        // LOCATION KIOSK SETTINGS
        // ============================================
        modelBuilder.Entity<LocationKioskSettings>(entity =>
        {
            entity.HasKey(e => e.LocationKioskSettingsId);

            entity.HasIndex(e => e.KioskToken, "IX_LocationKioskSettings_KioskToken")
                .IsUnique()
                .HasFilter("[KioskToken] IS NOT NULL");

            entity.HasIndex(e => new { e.TenantId, e.LocationId }, "IX_LocationKioskSettings_TenantId_LocationId")
                .IsUnique();

            entity.Property(e => e.KioskToken).HasMaxLength(64);
            entity.Property(e => e.SessionTimeoutMinutes).HasDefaultValue(30);
            entity.Property(e => e.IsEnabled).HasDefaultValue(false);
            entity.Property(e => e.LastAccessIpAddress).HasMaxLength(50);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");

            entity.HasOne(d => d.Location).WithOne(p => p.KioskSettings)
                .HasForeignKey<LocationKioskSettings>(d => d.LocationId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_LocationKioskSettings_LocationId");

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_LocationKioskSettings_TenantId");

            entity.HasOne(d => d.TokenGeneratedByUser).WithMany()
                .HasForeignKey(d => d.TokenGeneratedByUserId)
                .HasConstraintName("FK_LocationKioskSettings_TokenGeneratedByUserId");
        });

        // ============================================
        // CONSENT FORM TEMPLATES
        // ============================================
        modelBuilder.Entity<ConsentFormTemplate>(entity =>
        {
            entity.HasKey(e => e.ConsentFormTemplateId);

            entity.HasIndex(e => new { e.TenantId, e.FormType, e.IsActive }, "IX_ConsentFormTemplates_Tenant_Type")
                .HasFilter("[IsDeleted] = 0");

            entity.HasIndex(e => new { e.TenantId, e.LocationId, e.FormType, e.IsActive }, "IX_ConsentFormTemplates_Tenant_Location_Type")
                .HasFilter("[IsDeleted] = 0");

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.HtmlContent).IsRequired();
            entity.Property(e => e.DisplayOrder).HasDefaultValue(0);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Version).HasDefaultValue(1);
            entity.Property(e => e.IsDeleted).HasDefaultValue(false);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ConsentFormTemplates_TenantId");

            entity.HasOne(d => d.Location).WithMany(p => p.ConsentFormTemplates)
                .HasForeignKey(d => d.LocationId)
                .HasConstraintName("FK_ConsentFormTemplates_LocationId");

            entity.HasOne(d => d.CreatedByUser).WithMany()
                .HasForeignKey(d => d.CreatedByUserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_ConsentFormTemplates_CreatedByUserId");

            entity.HasOne(d => d.UpdatedByUser).WithMany()
                .HasForeignKey(d => d.UpdatedByUserId)
                .HasConstraintName("FK_ConsentFormTemplates_UpdatedByUserId");
        });

        // ============================================
        // CARE EPISODE CONSENTS
        // ============================================
        modelBuilder.Entity<CareEpisodeConsent>(entity =>
        {
            entity.HasKey(e => e.CareEpisodeConsentId);

            // Index for finding consents by care episode
            entity.HasIndex(e => new { e.TenantId, e.CareEpisodeId }, "IX_CareEpisodeConsents_CareEpisode")
                .HasFilter("[CareEpisodeId] IS NOT NULL");

            // Index for finding consents by patient
            entity.HasIndex(e => new { e.TenantId, e.PatientId }, "IX_CareEpisodeConsents_Patient");

            // Index for finding consents by appointment (important for orphan consents)
            // AppointmentId is nullable for manual uploads without an appointment
            entity.HasIndex(e => new { e.TenantId, e.AppointmentId }, "IX_CareEpisodeConsents_Appointment")
                .IsUnique()
                .HasFilter("[AppointmentId] IS NOT NULL");

            entity.Property(e => e.IpAddress).HasMaxLength(50);
            entity.Property(e => e.UserAgent).HasMaxLength(500);
            entity.Property(e => e.VerificationZipCode).HasMaxLength(20);
            entity.Property(e => e.PdfHash).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CareEpisodeConsents_TenantId");

            // CareEpisodeId is nullable - consent can exist before care episode
            entity.HasOne(d => d.CareEpisode).WithMany(p => p.CareEpisodeConsents)
                .HasForeignKey(d => d.CareEpisodeId)
                .HasConstraintName("FK_CareEpisodeConsents_CareEpisodeId");

            entity.HasOne(d => d.Patient).WithMany(p => p.CareEpisodeConsents)
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CareEpisodeConsents_PatientId");

            // AppointmentId is nullable - manual uploads don't require an appointment
            entity.HasOne(d => d.Appointment).WithMany(p => p.CareEpisodeConsents)
                .HasForeignKey(d => d.AppointmentId)
                .HasConstraintName("FK_CareEpisodeConsents_AppointmentId");

            entity.HasOne(d => d.Location).WithMany(p => p.CareEpisodeConsents)
                .HasForeignKey(d => d.LocationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CareEpisodeConsents_LocationId");
        });

        // ============================================
        // CARE EPISODE CONSENT FORMS
        // ============================================
        modelBuilder.Entity<CareEpisodeConsentForm>(entity =>
        {
            entity.HasKey(e => e.CareEpisodeConsentFormId);

            entity.HasIndex(e => new { e.TenantId, e.CareEpisodeConsentId }, "IX_CareEpisodeConsentForms_Consent");

            entity.Property(e => e.FormName)
                .IsRequired()
                .HasMaxLength(200);
            entity.Property(e => e.RenderedHtmlContent).IsRequired();
            entity.Property(e => e.SignaturesJson).IsRequired();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_CareEpisodeConsentForms_TenantId");

            entity.HasOne(d => d.CareEpisodeConsent).WithMany(p => p.CareEpisodeConsentForms)
                .HasForeignKey(d => d.CareEpisodeConsentId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_CareEpisodeConsentForms_CareEpisodeConsentId");

            // ConsentFormTemplateId is nullable for manual uploads without a template
            entity.HasOne(d => d.ConsentFormTemplate).WithMany(p => p.CareEpisodeConsentForms)
                .HasForeignKey(d => d.ConsentFormTemplateId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("FK_CareEpisodeConsentForms_ConsentFormTemplateId");
        });

        // ============================================
        // KIOSK VERIFICATION ATTEMPTS (Rate Limiting)
        // ============================================
        modelBuilder.Entity<KioskVerificationAttempt>(entity =>
        {
            entity.HasKey(e => e.KioskVerificationAttemptId);

            // Index for rate limiting queries
            entity.HasIndex(e => new { e.TenantId, e.LocationId, e.IpAddress, e.AttemptedAt }, "IX_KioskVerificationAttempts_RateLimit")
                .IsDescending(false, false, false, true);

            entity.Property(e => e.IpAddress)
                .IsRequired()
                .HasMaxLength(50);
            entity.Property(e => e.AttemptedSsnLast4Hash).HasMaxLength(100);
            entity.Property(e => e.AttemptedZip).HasMaxLength(20);
            entity.Property(e => e.UserAgent).HasMaxLength(500);
            entity.Property(e => e.AttemptedAt).HasDefaultValueSql("(getutcdate())");

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_KioskVerificationAttempts_TenantId");

            entity.HasOne(d => d.Location).WithMany()
                .HasForeignKey(d => d.LocationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_KioskVerificationAttempts_LocationId");

            entity.HasOne(d => d.Patient).WithMany()
                .HasForeignKey(d => d.PatientId)
                .HasConstraintName("FK_KioskVerificationAttempts_PatientId");
        });

        // ============================================
        // KIOSK SESSIONS
        // ============================================
        modelBuilder.Entity<KioskSession>(entity =>
        {
            entity.HasKey(e => e.KioskSessionId);

            entity.HasIndex(e => e.SessionToken, "IX_KioskSessions_SessionToken")
                .IsUnique();

            entity.HasIndex(e => new { e.TenantId, e.LocationId, e.ExpiresAt }, "IX_KioskSessions_Active")
                .HasFilter("[IsCompleted] = 0 AND [IsInvalidated] = 0");

            entity.Property(e => e.SessionToken)
                .IsRequired()
                .HasMaxLength(64);
            entity.Property(e => e.IpAddress).HasMaxLength(50);
            entity.Property(e => e.UserAgent).HasMaxLength(500);
            entity.Property(e => e.IsCompleted).HasDefaultValue(false);
            entity.Property(e => e.IsInvalidated).HasDefaultValue(false);
            entity.Property(e => e.CurrentFormIndex).HasDefaultValue(0);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_KioskSessions_TenantId");

            entity.HasOne(d => d.Location).WithMany()
                .HasForeignKey(d => d.LocationId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_KioskSessions_LocationId");

            entity.HasOne(d => d.Patient).WithMany()
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_KioskSessions_PatientId");

            entity.HasOne(d => d.Appointment).WithMany()
                .HasForeignKey(d => d.AppointmentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_KioskSessions_AppointmentId");

            entity.HasOne(d => d.CareEpisode).WithMany()
                .HasForeignKey(d => d.CareEpisodeId)
                .HasConstraintName("FK_KioskSessions_CareEpisodeId");
        });

        // ============================================
        // RECORDING SESSIONS
        // ============================================
        modelBuilder.Entity<RecordingSession>(entity =>
        {
            entity.HasKey(e => e.SessionId).HasName("PK_RecordingSessions");

            entity.HasIndex(e => e.TenantId, "IX_RecordingSessions_TenantId");
            entity.HasIndex(e => e.AppointmentId, "IX_RecordingSessions_AppointmentId");
            entity.HasIndex(e => e.ProviderId, "IX_RecordingSessions_ProviderId");
            entity.HasIndex(e => e.PatientId, "IX_RecordingSessions_PatientId");
            entity.HasIndex(e => e.RecordingStatus, "IX_RecordingSessions_RecordingStatus");
            entity.HasIndex(e => e.StartTime, "IX_RecordingSessions_StartTime").IsDescending(true);

            entity.Property(e => e.RecordingStatus)
                .IsRequired()
                .HasMaxLength(50)
                .HasDefaultValue("NotStarted");
            entity.Property(e => e.MergedAudioFileName).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_RecordingSessions_Tenants");

            entity.HasOne(d => d.Appointment).WithMany()
                .HasForeignKey(d => d.AppointmentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_RecordingSessions_Appointments");

            entity.HasOne(d => d.Provider).WithMany()
                .HasForeignKey(d => d.ProviderId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_RecordingSessions_Providers");

            entity.HasOne(d => d.Patient).WithMany()
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_RecordingSessions_Patients");
        });

        // ============================================
        // TRANSCRIPTION CHUNKS
        // ============================================
        modelBuilder.Entity<TranscriptionChunk>(entity =>
        {
            entity.HasKey(e => e.ChunkId).HasName("PK_TranscriptionChunks");

            entity.HasIndex(e => e.SessionId, "IX_TranscriptionChunks_SessionId");
            entity.HasIndex(e => e.TranscriptionStatus, "IX_TranscriptionChunks_TranscriptionStatus");
            entity.HasIndex(e => e.RecordedTimestamp, "IX_TranscriptionChunks_RecordedTimestamp").IsDescending(true);
            entity.HasIndex(e => new { e.SessionId, e.SequenceNumber }, "UQ_TranscriptionChunks_SessionSequence").IsUnique();

            entity.Property(e => e.ChunkAudioFileName).HasMaxLength(500);
            entity.Property(e => e.ChunkDurationSeconds).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.TranscriptionStatus)
                .IsRequired()
                .HasMaxLength(50)
                .HasDefaultValue("Pending");
            entity.Property(e => e.TranscriptionError).HasMaxLength(1000);
            entity.Property(e => e.RecordedTimestamp).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");

            entity.HasOne(d => d.RecordingSession).WithMany(p => p.TranscriptionChunks)
                .HasForeignKey(d => d.SessionId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_TranscriptionChunks_RecordingSessions");
        });

        // ============================================
        // TELEHEALTH TRANSCRIPTION CHUNKS
        // ============================================
        modelBuilder.Entity<TelehealthTranscriptionChunk>(entity =>
        {
            entity.HasKey(e => e.ChunkId).HasName("PK_TelehealthTranscriptionChunks");

            entity.HasIndex(e => e.EncounterId, "IX_TelehealthTranscriptionChunks_EncounterId");
            entity.HasIndex(e => new { e.TenantId, e.EncounterId }, "IX_TelehealthTranscriptionChunks_TenantId_EncounterId");

            entity.Property(e => e.TranscriptionText).IsRequired();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

            entity.HasOne(d => d.Encounter).WithMany()
                .HasForeignKey(d => d.EncounterId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_TelehealthTranscriptionChunks_Encounters");

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_TelehealthTranscriptionChunks_Tenants");
        });

        // ============================================
        // MEDICAL LIEN TEMPLATES
        // ============================================
        modelBuilder.Entity<MedicalLienTemplate>(entity =>
        {
            entity.HasKey(e => e.TemplateId);

            // Index for finding templates by tenant and location
            entity.HasIndex(e => new { e.TenantId, e.LocationId, e.IsActive }, "IX_MedicalLienTemplates_Tenant_Location")
                .IsUnique()
                .HasFilter("[IsActive] = 1");

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.HtmlContent).IsRequired();
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_MedicalLienTemplates_TenantId");

            entity.HasOne(d => d.Location).WithMany()
                .HasForeignKey(d => d.LocationId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_MedicalLienTemplates_LocationId");
        });

        // ============================================
        // INTERNAL MESSAGING SYSTEM - CONVERSATIONS
        // ============================================
        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.HasKey(e => e.ConversationId);

            // Index for finding conversations by tenant and user
            entity.HasIndex(e => new { e.TenantId, e.User1Id }, "IX_Conversations_TenantId_User1Id");
            entity.HasIndex(e => new { e.TenantId, e.User2Id }, "IX_Conversations_TenantId_User2Id");

            // Unique constraint to prevent duplicate conversations
            entity.HasIndex(e => new { e.TenantId, e.User1Id, e.User2Id }, "IX_Conversations_UniqueParticipants")
                .IsUnique();

            entity.Property(e => e.LastMessageText).HasMaxLength(500);
            entity.Property(e => e.User1UnreadCount).HasDefaultValue(0);
            entity.Property(e => e.User2UnreadCount).HasDefaultValue(0);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_Conversations_TenantId");

            entity.HasOne(d => d.User1).WithMany()
                .HasForeignKey(d => d.User1Id)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_Conversations_User1Id");

            entity.HasOne(d => d.User2).WithMany()
                .HasForeignKey(d => d.User2Id)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_Conversations_User2Id");

            entity.HasOne(d => d.LastMessageSender).WithMany()
                .HasForeignKey(d => d.LastMessageSenderId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_Conversations_LastMessageSenderId");
        });

        // ============================================
        // INTERNAL MESSAGING SYSTEM - MESSAGES
        // ============================================
        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.MessageId);

            // Index for fetching messages in a conversation
            entity.HasIndex(e => new { e.ConversationId, e.CreatedAt }, "IX_Messages_ConversationId_CreatedAt")
                .IsDescending(false, true);

            // Index for counting unread messages
            entity.HasIndex(e => new { e.TenantId, e.RecipientId, e.IsRead }, "IX_Messages_RecipientId_IsRead")
                .HasFilter("[IsRead] = 0");

            // Index for searching messages by sender
            entity.HasIndex(e => new { e.TenantId, e.SenderId, e.CreatedAt }, "IX_Messages_TenantId_SenderId")
                .IsDescending(false, false, true);

            entity.Property(e => e.MessageType).HasDefaultValue(0);
            entity.Property(e => e.FileUrl).HasMaxLength(1000);
            entity.Property(e => e.FileName).HasMaxLength(500);
            entity.Property(e => e.FileMimeType).HasMaxLength(100);
            entity.Property(e => e.FileDurationSeconds).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.IsRead).HasDefaultValue(false);
            entity.Property(e => e.IsDeletedBySender).HasDefaultValue(false);
            entity.Property(e => e.IsDeletedByRecipient).HasDefaultValue(false);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_Messages_TenantId");

            entity.HasOne(d => d.Conversation).WithMany(c => c.Messages)
                .HasForeignKey(d => d.ConversationId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_Messages_ConversationId");

            entity.HasOne(d => d.Sender).WithMany()
                .HasForeignKey(d => d.SenderId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_Messages_SenderId");

            entity.HasOne(d => d.Recipient).WithMany()
                .HasForeignKey(d => d.RecipientId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_Messages_RecipientId");
        });

        // ============================================
        // INTERNAL MESSAGING SYSTEM - USER PRESENCE
        // ============================================
        modelBuilder.Entity<UserPresence>(entity =>
        {
            entity.ToTable("UserPresence");
            entity.HasKey(e => e.UserPresenceId);

            // Unique constraint: one presence record per user per tenant
            entity.HasIndex(e => new { e.TenantId, e.UserId }, "IX_UserPresence_TenantId_UserId")
                .IsUnique();

            // Index for finding online users
            entity.HasIndex(e => new { e.TenantId, e.Status }, "IX_UserPresence_TenantId_Status")
                .HasFilter("[Status] = 1");

            entity.Property(e => e.Status).HasDefaultValue(0);
            entity.Property(e => e.LastActiveAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.ConnectionId).HasMaxLength(100);

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_UserPresence_TenantId");

            entity.HasOne(d => d.User).WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_UserPresence_UserId");
        });

        // ============================================
        // PROVIDER FAVORITE CODES (Dx & CPT step)
        // Per-user favorite ICD-10 / CPT codes. Strict per-user isolation.
        // ============================================
        modelBuilder.Entity<ProviderFavoriteCode>(entity =>
        {
            entity.ToTable("ProviderFavoriteCodes");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.CodeType).HasMaxLength(10).IsRequired();
            entity.Property(e => e.Code).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(500).IsRequired();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");

            // Unique per (user, codeType, code) — prevents duplicates
            entity.HasIndex(e => new { e.UserId, e.CodeType, e.Code }, "UX_ProviderFavoriteCodes_User_Type_Code")
                .IsUnique();

            // Fast lookup by user + type
            entity.HasIndex(e => new { e.UserId, e.CodeType }, "IX_ProviderFavoriteCodes_User_Type");

            entity.HasOne(d => d.User).WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_ProviderFavoriteCodes_UserId");
        });

        // ============================================
        // INTERNAL MEDICINE ENTITIES
        // ============================================

        modelBuilder.Entity<Encounter>(entity =>
        {
            entity.HasKey(e => e.EncounterId);
            entity.HasIndex(e => new { e.TenantId, e.PatientId, e.EncounterDate });
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.Status).HasDefaultValue(0);

            entity.HasOne(d => d.Patient).WithMany()
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.Provider).WithMany()
                .HasForeignKey(d => d.ProviderId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.Appointment).WithMany()
                .HasForeignKey(d => d.AppointmentId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<PatientProblem>(entity =>
        {
            entity.HasKey(e => e.PatientProblemId);
            entity.HasIndex(e => new { e.TenantId, e.PatientId });
            entity.Property(e => e.IcdCode).HasMaxLength(20);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.Status).HasDefaultValue(0);

            entity.HasOne(d => d.Patient).WithMany()
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<PatientAllergy>(entity =>
        {
            entity.HasKey(e => e.PatientAllergyId);
            entity.HasIndex(e => new { e.TenantId, e.PatientId });
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);

            entity.HasOne(d => d.Patient).WithMany()
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<PatientMedication>(entity =>
        {
            entity.HasKey(e => e.PatientMedicationId);
            entity.HasIndex(e => new { e.TenantId, e.PatientId });
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.Status).HasDefaultValue(0);

            entity.HasOne(d => d.Patient).WithMany()
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.PrescribedByProvider).WithMany()
                .HasForeignKey(d => d.PrescribedByProviderId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<PatientVital>(entity =>
        {
            entity.HasKey(e => e.PatientVitalId);
            entity.HasIndex(e => new { e.TenantId, e.PatientId, e.RecordedAt });
            entity.Property(e => e.RecordedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.Temperature).HasColumnType("decimal(5, 1)");
            entity.Property(e => e.SpO2).HasColumnType("decimal(5, 1)");
            entity.Property(e => e.Weight).HasColumnType("decimal(6, 1)");
            entity.Property(e => e.Height).HasColumnType("decimal(5, 1)");
            entity.Property(e => e.Bmi).HasColumnType("decimal(5, 1)");

            entity.HasOne(d => d.Patient).WithMany()
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.Encounter).WithMany(p => p.Vitals)
                .HasForeignKey(d => d.EncounterId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<PatientImmunization>(entity =>
        {
            entity.HasKey(e => e.PatientImmunizationId);
            entity.HasIndex(e => new { e.TenantId, e.PatientId });
            entity.Property(e => e.CvxCode).HasMaxLength(10);
            entity.Property(e => e.LotNumber).HasMaxLength(50);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");

            entity.HasOne(d => d.Patient).WithMany()
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.AdministeredByProvider).WithMany()
                .HasForeignKey(d => d.AdministeredByProviderId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<PatientFamilyHistory>(entity =>
        {
            entity.HasKey(e => e.PatientFamilyHistoryId);
            entity.HasIndex(e => new { e.TenantId, e.PatientId });
            entity.Property(e => e.Relation).HasMaxLength(50);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");

            entity.HasOne(d => d.Patient).WithMany()
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<PatientSocialHistory>(entity =>
        {
            entity.HasKey(e => e.PatientSocialHistoryId);
            entity.HasIndex(e => new { e.TenantId, e.PatientId });
            entity.Property(e => e.Category).HasMaxLength(50);
            entity.Property(e => e.Status).HasMaxLength(20);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");

            entity.HasOne(d => d.Patient).WithMany()
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<TreatmentPlan>(entity =>
        {
            entity.HasKey(e => e.TreatmentPlanId);
            entity.HasIndex(e => new { e.TenantId, e.PatientId });
            entity.Property(e => e.IcdCode).HasMaxLength(20);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.Status).HasDefaultValue(0);

            entity.HasOne(d => d.Patient).WithMany()
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.Provider).WithMany()
                .HasForeignKey(d => d.ProviderId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // E-Prescribing: Prescription
        modelBuilder.Entity<Prescription>(entity =>
        {
            entity.HasKey(e => e.PrescriptionId);
            entity.HasIndex(e => new { e.TenantId, e.PatientId, e.PrescribedDate });
            entity.HasIndex(e => new { e.TenantId, e.Status });
            entity.Property(e => e.DrugName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Quantity).HasPrecision(10, 2);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.Status).HasDefaultValue(0);
            entity.Property(e => e.DAW).HasDefaultValue(false);
            entity.Property(e => e.IsControlledSubstance).HasDefaultValue(false);

            entity.HasOne(d => d.Patient).WithMany()
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.Provider).WithMany()
                .HasForeignKey(d => d.ProviderId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.Encounter).WithMany()
                .HasForeignKey(d => d.EncounterId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // E-Prescribing: Pharmacy
        modelBuilder.Entity<Pharmacy>(entity =>
        {
            entity.HasKey(e => e.PharmacyId);
            entity.HasIndex(e => e.NCPDP);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // E-Prescribing: DrugDatabase (shared, no tenant)
        modelBuilder.Entity<DrugDatabase>(entity =>
        {
            entity.HasKey(e => e.DrugId);
            entity.HasIndex(e => e.NDCCode);
            entity.HasIndex(e => e.GenericName);
            entity.Property(e => e.BrandName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.GenericName).HasMaxLength(200);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        // ============================================
        // ORDERS MODULE (Labs, Imaging, Referrals)
        // ============================================

        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasKey(e => e.OrderId);
            entity.HasIndex(e => new { e.TenantId, e.PatientId, e.OrderDate }, "IX_Orders_TenantId_PatientId_OrderDate");
            entity.HasIndex(e => new { e.TenantId, e.Status, e.OrderType }, "IX_Orders_TenantId_Status_OrderType");
            entity.Property(e => e.DiagnosisCode).HasMaxLength(20);
            entity.Property(e => e.ClinicalIndication).HasMaxLength(500);
            entity.Property(e => e.LabPanelName).HasMaxLength(100);
            entity.Property(e => e.SpecimenType).HasMaxLength(100);
            entity.Property(e => e.BodyPart).HasMaxLength(200);
            entity.Property(e => e.ImagingFacility).HasMaxLength(200);
            entity.Property(e => e.ReferralSpecialty).HasMaxLength(100);
            entity.Property(e => e.ReferredToProvider).HasMaxLength(200);
            entity.Property(e => e.ReferredToFacility).HasMaxLength(200);
            entity.Property(e => e.ReferredToPhone).HasMaxLength(20);
            entity.Property(e => e.ReferredToFax).HasMaxLength(20);
            entity.Property(e => e.ReferralReason).HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.Status).HasDefaultValue(0);
            entity.Property(e => e.Priority).HasDefaultValue(0);

            entity.HasOne(d => d.Patient).WithMany()
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.Provider).WithMany()
                .HasForeignKey(d => d.ProviderId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.Encounter).WithMany()
                .HasForeignKey(d => d.EncounterId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<OrderResult>(entity =>
        {
            entity.HasKey(e => e.OrderResultId);
            entity.HasIndex(e => e.OrderId, "IX_OrderResults_OrderId");
            entity.Property(e => e.TestName).HasMaxLength(200);
            entity.Property(e => e.ResultValue).HasMaxLength(100);
            entity.Property(e => e.ResultUnit).HasMaxLength(50);
            entity.Property(e => e.ReferenceRange).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");

            entity.HasOne(d => d.Order).WithMany(p => p.Results)
                .HasForeignKey(d => d.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LabTestCatalog>(entity =>
        {
            entity.HasKey(e => e.LabTestId);
            entity.HasIndex(e => e.PanelName, "IX_LabTestCatalogs_PanelName");
            entity.Property(e => e.PanelName).HasMaxLength(100);
            entity.Property(e => e.TestName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.TestCode).HasMaxLength(20);
            entity.Property(e => e.Unit).HasMaxLength(50);
            entity.Property(e => e.ReferenceRange).HasMaxLength(100);
            entity.Property(e => e.SpecimenType).HasMaxLength(100);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
        });

        modelBuilder.Entity<PatientStickyNote>(entity =>
        {
            entity.HasKey(e => e.PatientStickyNoteId);
            entity.HasIndex(e => new { e.TenantId, e.PatientId }, "IX_PatientStickyNotes_TenantId_PatientId");
            entity.Property(e => e.Content).IsRequired().HasMaxLength(1000);
            entity.Property(e => e.CreatedByName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.IsDeleted).HasDefaultValue(false);

            entity.HasOne(d => d.Patient).WithMany()
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // ============================================
        // CARE NOTES
        // Structured staff -> provider communication.
        // Content is PHI and encrypted by EncryptionHelper at save time.
        // ============================================
        modelBuilder.Entity<CareNote>(entity =>
        {
            entity.HasKey(e => e.CareNoteId);
            entity.HasIndex(e => new { e.TenantId, e.PatientId }, "IX_CareNotes_TenantId_PatientId");
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.CreatedByName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.EditedByName).HasMaxLength(200);
            entity.Property(e => e.DeletedByName).HasMaxLength(200);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.IsEdited).HasDefaultValue(false);
            entity.Property(e => e.IsDeleted).HasDefaultValue(false);

            entity.HasOne(d => d.Patient).WithMany()
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        // ============================================
        // PATIENT-PROVIDER MESSAGING SYSTEM - CONVERSATIONS
        // ============================================
        modelBuilder.Entity<PatientConversation>(entity =>
        {
            entity.HasKey(e => e.PatientConversationId);

            // One conversation per patient-user pair per tenant
            entity.HasIndex(e => new { e.TenantId, e.PatientId, e.UserId }, "IX_PatientConversations_UniqueParticipants")
                .IsUnique();

            // Index for finding conversations by patient
            entity.HasIndex(e => new { e.TenantId, e.PatientId }, "IX_PatientConversations_TenantId_PatientId");

            // Index for finding conversations by user
            entity.HasIndex(e => new { e.TenantId, e.UserId }, "IX_PatientConversations_TenantId_UserId");

            entity.Property(e => e.LastMessageText).HasMaxLength(500);
            entity.Property(e => e.LastMessageSenderType).HasMaxLength(20);
            entity.Property(e => e.PatientUnreadCount).HasDefaultValue(0);
            entity.Property(e => e.UserUnreadCount).HasDefaultValue(0);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_PatientConversations_TenantId");

            entity.HasOne(d => d.Patient).WithMany()
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_PatientConversations_PatientId");

            entity.HasOne(d => d.User).WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_PatientConversations_UserId");
        });

        // ============================================
        // PATIENT-PROVIDER MESSAGING SYSTEM - MESSAGES
        // ============================================
        modelBuilder.Entity<PatientMessage>(entity =>
        {
            entity.HasKey(e => e.PatientMessageId);

            // Index for fetching messages in a conversation (chronological)
            entity.HasIndex(e => new { e.PatientConversationId, e.CreatedAt }, "IX_PatientMessages_ConversationId_CreatedAt")
                .IsDescending(false, true);

            // Index for counting unread messages by user
            entity.HasIndex(e => new { e.TenantId, e.IsReadByUser }, "IX_PatientMessages_UnreadByUser")
                .HasFilter("[IsReadByUser] = 0 AND [SenderType] = 'Patient'");

            // Index for counting unread messages by patient
            entity.HasIndex(e => new { e.TenantId, e.IsReadByPatient }, "IX_PatientMessages_UnreadByPatient")
                .HasFilter("[IsReadByPatient] = 0 AND [SenderType] = 'Provider'");

            entity.Property(e => e.SenderType).IsRequired().HasMaxLength(20);
            entity.Property(e => e.MessageText).IsRequired();
            entity.Property(e => e.IsReadByPatient).HasDefaultValue(false);
            entity.Property(e => e.IsReadByUser).HasDefaultValue(false);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_PatientMessages_TenantId");

            entity.HasOne(d => d.PatientConversation).WithMany(c => c.PatientMessages)
                .HasForeignKey(d => d.PatientConversationId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK_PatientMessages_ConversationId");

            entity.HasOne(d => d.SenderPatient).WithMany()
                .HasForeignKey(d => d.SenderPatientId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_PatientMessages_SenderPatientId");

            entity.HasOne(d => d.SenderUser).WithMany()
                .HasForeignKey(d => d.SenderUserId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_PatientMessages_SenderUserId");
        });

        // ============================================
        // Stripe Connect Phase 1
        // ============================================
        modelBuilder.Entity<StripeConnectAccount>(entity =>
        {
            entity.HasKey(e => e.StripeConnectAccountId);

            entity.Property(e => e.StripeAccountId).IsRequired().HasMaxLength(255);
            entity.Property(e => e.DisplayName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.BusinessEmail).HasMaxLength(255);
            entity.Property(e => e.Country).HasMaxLength(2).HasDefaultValue("US");
            entity.Property(e => e.DefaultCurrency).HasMaxLength(3).HasDefaultValue("usd");
            entity.Property(e => e.Status).HasDefaultValue(0);
            entity.Property(e => e.ChargesEnabled).HasDefaultValue(false);
            entity.Property(e => e.PayoutsEnabled).HasDefaultValue(false);
            entity.Property(e => e.DetailsSubmitted).HasDefaultValue(false);
            entity.Property(e => e.ConnectedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");

            entity.HasIndex(e => e.StripeAccountId)
                .IsUnique()
                .HasDatabaseName("UQ_StripeConnectAccounts_StripeAccountId");

            entity.HasIndex(e => e.TenantId)
                .HasDatabaseName("IX_StripeConnectAccounts_TenantId");

            entity.HasIndex(e => e.Status)
                .HasDatabaseName("IX_StripeConnectAccounts_Status");

            entity.HasOne(d => d.Tenant).WithMany(p => p.StripeConnectAccounts)
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_StripeConnectAccounts_Tenants");

            entity.HasOne(d => d.ConnectedByUser).WithMany()
                .HasForeignKey(d => d.ConnectedByUserId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_StripeConnectAccounts_Users");
        });

        modelBuilder.Entity<StripeWebhookEvent>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.StripeEventId).IsRequired().HasMaxLength(255);
            entity.Property(e => e.EventType).IsRequired().HasMaxLength(100);
            entity.Property(e => e.StripeAccountId).HasMaxLength(255);
            entity.Property(e => e.Payload).IsRequired();
            entity.Property(e => e.Status).HasDefaultValue(0);
            entity.Property(e => e.ReceivedAt).HasDefaultValueSql("(getutcdate())");

            entity.HasIndex(e => e.StripeEventId)
                .IsUnique()
                .HasDatabaseName("UQ_StripeWebhookEvents_StripeEventId");

            entity.HasIndex(e => e.EventType)
                .HasDatabaseName("IX_StripeWebhookEvents_EventType");

            entity.HasIndex(e => e.Status)
                .HasDatabaseName("IX_StripeWebhookEvents_Status");

            entity.HasIndex(e => e.ReceivedAt)
                .IsDescending()
                .HasDatabaseName("IX_StripeWebhookEvents_ReceivedAt");

            entity.HasIndex(e => e.StripeAccountId)
                .HasDatabaseName("IX_StripeWebhookEvents_StripeAccountId");
        });

        modelBuilder.Entity<PaymentRefund>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.StripeRefundId).IsRequired().HasMaxLength(255);
            entity.Property(e => e.StripeChargeId).HasMaxLength(255);
            entity.Property(e => e.Reason).HasMaxLength(100);
            entity.Property(e => e.Status).HasDefaultValue(0);
            entity.Property(e => e.RefundedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.CreatedByExternal).HasDefaultValue(true);

            entity.HasIndex(e => e.StripeRefundId)
                .IsUnique()
                .HasDatabaseName("UQ_PaymentRefunds_StripeRefundId");

            entity.HasIndex(e => e.PaymentId)
                .HasDatabaseName("IX_PaymentRefunds_PaymentId");

            entity.HasIndex(e => e.TenantId)
                .HasDatabaseName("IX_PaymentRefunds_TenantId");

            entity.HasOne(d => d.Payment).WithMany(p => p.Refunds)
                .HasForeignKey(d => d.PaymentId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PaymentRefunds_Payments");

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_PaymentRefunds_Tenants");
        });

        modelBuilder.Entity<InstallmentPlanAuditLog>(entity =>
        {
            // 2026-05: explicit table name. The DbSet is pluralized
            // (InstallmentPlanAuditLogs), but Manual/migration_008 created
            // the singular table InstallmentPlanAuditLog. Without this
            // override EF queried the non-existent plural table and
            // InstallmentProcessorBackgroundService logged "Invalid object
            // name 'InstallmentPlanAuditLogs'" on every sweep.
            entity.ToTable("InstallmentPlanAuditLog");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Action).IsRequired().HasMaxLength(50);
            entity.Property(e => e.ActorType).IsRequired().HasMaxLength(20);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");

            entity.HasIndex(e => e.PlanId)
                .HasDatabaseName("IX_InstallmentPlanAuditLog_PlanId");

            entity.HasIndex(e => e.DetailId)
                .HasDatabaseName("IX_InstallmentPlanAuditLog_DetailId");

            entity.HasIndex(e => e.CreatedAt)
                .IsDescending()
                .HasDatabaseName("IX_InstallmentPlanAuditLog_CreatedAt");

            entity.HasIndex(e => e.Action)
                .HasDatabaseName("IX_InstallmentPlanAuditLog_Action");

            entity.HasOne(d => d.Plan).WithMany(p => p.AuditLogs)
                .HasForeignKey(d => d.PlanId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_InstallmentPlanAuditLog_Plans");

            entity.HasOne(d => d.Detail).WithMany()
                .HasForeignKey(d => d.DetailId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_InstallmentPlanAuditLog_Details");

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_InstallmentPlanAuditLog_Tenants");
        });

        // Location → StripeConnectAccount FK + new fee columns
        modelBuilder.Entity<Location>(entity =>
        {
            entity.Property(e => e.OnlineFeePercent).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.CardPresentFeePercent).HasColumnType("decimal(5, 2)");

            entity.HasOne(d => d.StripeConnectAccount).WithMany(p => p.Locations)
                .HasForeignKey(d => d.StripeConnectAccountId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_Locations_StripeConnectAccounts");

            entity.HasIndex(e => e.StripeConnectAccountId)
                .HasDatabaseName("IX_Locations_StripeConnectAccountId");
        });

        // Charge → Location FK
        modelBuilder.Entity<Charge>(entity =>
        {
            entity.HasOne(d => d.Location).WithMany()
                .HasForeignKey(d => d.LocationId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_Charges_Locations");

            entity.HasIndex(e => e.LocationId)
                .HasDatabaseName("IX_Charges_LocationId");
        });

        // Payment → Location, StripeConnectAccount FKs + new columns
        modelBuilder.Entity<Payment>(entity =>
        {
            entity.Property(e => e.StripeChargeId).HasMaxLength(255);
            entity.Property(e => e.HasOpenDispute).HasDefaultValue(false);

            entity.HasOne(d => d.Location).WithMany()
                .HasForeignKey(d => d.LocationId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_Payments_Locations");

            entity.HasOne(d => d.StripeConnectAccount).WithMany(p => p.Payments)
                .HasForeignKey(d => d.StripeConnectAccountId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_Payments_StripeConnectAccounts");

            entity.HasIndex(e => e.LocationId)
                .HasDatabaseName("IX_Payments_LocationId");

            entity.HasIndex(e => e.StripeConnectAccountId)
                .HasDatabaseName("IX_Payments_StripeConnectAccountId");

            entity.HasIndex(e => e.StripeChargeId)
                .HasDatabaseName("IX_Payments_StripeChargeId");
        });

        // InstallmentPlan → Location, StripeConnectAccount FKs
        modelBuilder.Entity<InstallmentPlan>(entity =>
        {
            entity.HasOne(d => d.Location).WithMany()
                .HasForeignKey(d => d.LocationId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_InstallmentPlans_Locations");

            entity.HasOne(d => d.StripeConnectAccount).WithMany(p => p.InstallmentPlans)
                .HasForeignKey(d => d.StripeConnectAccountId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_InstallmentPlans_StripeConnectAccounts");

            entity.HasIndex(e => e.LocationId)
                .HasDatabaseName("IX_InstallmentPlans_LocationId");

            entity.HasIndex(e => e.StripeConnectAccountId)
                .HasDatabaseName("IX_InstallmentPlans_StripeConnectAccountId");
        });

        // ============================================
        // PATIENT INTAKE (Phase 1)
        // ============================================

        modelBuilder.Entity<PatientGenderHealth>(entity =>
        {
            entity.HasKey(e => e.PatientGenderHealthId);
            entity.HasIndex(e => e.PatientId).IsUnique()
                .HasFilter("[IsDeleted] = 0")
                .HasDatabaseName("UX_PatientGenderHealth_PatientId");
            entity.Property(e => e.BiologicalSex).HasMaxLength(20);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.Source).HasDefaultValue(1);
            entity.Property(e => e.IsDeleted).HasDefaultValue(false);

            entity.HasOne(d => d.Patient).WithMany()
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_PatientGenderHealth_Patients");

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_PatientGenderHealth_Tenants");
        });

        modelBuilder.Entity<PatientIntakeSubmission>(entity =>
        {
            entity.HasKey(e => e.PatientIntakeSubmissionId);
            entity.HasIndex(e => new { e.PatientId, e.StartedAt });
            entity.HasIndex(e => e.TenantId);

            entity.Property(e => e.SectionsTouched).HasMaxLength(500);
            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.UserAgent).HasMaxLength(500);
            entity.Property(e => e.StartedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.SourceChannel).HasDefaultValue(0);
            entity.Property(e => e.IsDeleted).HasDefaultValue(false);
            entity.Property(e => e.LastFeltWell).HasMaxLength(1000);
            entity.Property(e => e.WhatTriggered).HasMaxLength(1000);
            entity.Property(e => e.BetterFactors).HasMaxLength(1000);
            entity.Property(e => e.WorseFactors).HasMaxLength(1000);
            entity.Property(e => e.CancerSpecify).HasMaxLength(1000);

            entity.HasOne(d => d.Patient).WithMany(p => p.PatientIntakeSubmissions)
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_PatientIntakeSubmissions_Patients");

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_PatientIntakeSubmissions_Tenants");
        });

        modelBuilder.Entity<PatientHealthConcern>(entity =>
        {
            entity.HasKey(e => e.PatientHealthConcernId);
            entity.HasIndex(e => new { e.TenantId, e.PatientId, e.Priority });

            entity.Property(e => e.Concern).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Details).HasMaxLength(2000);
            entity.Property(e => e.Source).HasDefaultValue(1);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.IsDeleted).HasDefaultValue(false);

            entity.HasOne(d => d.Patient).WithMany(p => p.HealthConcerns)
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_PatientHealthConcerns_Patients");

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_PatientHealthConcerns_Tenants");

            entity.HasOne(d => d.IntakeSubmission).WithMany()
                .HasForeignKey(d => d.IntakeSubmissionId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_PatientHealthConcerns_IntakeSubmissions");
        });

        modelBuilder.Entity<PatientSupplement>(entity =>
        {
            entity.HasKey(e => e.PatientSupplementId);
            entity.HasIndex(e => new { e.TenantId, e.PatientId });

            entity.Property(e => e.SupplementName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Notes).HasMaxLength(1000);
            entity.Property(e => e.Source).HasDefaultValue(1);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.IsDeleted).HasDefaultValue(false);

            entity.HasOne(d => d.Patient).WithMany(p => p.PatientSupplements)
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_PatientSupplements_Patients");

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_PatientSupplements_Tenants");

            entity.HasOne(d => d.IntakeSubmission).WithMany()
                .HasForeignKey(d => d.IntakeSubmissionId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_PatientSupplements_IntakeSubmissions");
        });

        modelBuilder.Entity<PatientLongevityProfile>(entity =>
        {
            entity.HasKey(e => e.PatientLongevityProfileId);
            entity.HasIndex(e => e.PatientId).IsUnique();

            entity.Property(e => e.BiomarkerGoals).HasMaxLength(1000);
            entity.Property(e => e.OptimalHealthVision).HasMaxLength(1000);
            entity.Property(e => e.Source).HasDefaultValue(1);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.IsDeleted).HasDefaultValue(false);

            entity.HasOne(d => d.Patient).WithOne(p => p.PatientLongevityProfile)
                .HasForeignKey<PatientLongevityProfile>(d => d.PatientId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_PatientLongevityProfiles_Patients");

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_PatientLongevityProfiles_Tenants");

            entity.HasOne(d => d.IntakeSubmission).WithMany()
                .HasForeignKey(d => d.IntakeSubmissionId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_PatientLongevityProfiles_IntakeSubmissions");
        });

        modelBuilder.Entity<IntakeVerificationAttempt>(entity =>
        {
            entity.HasKey(e => e.IntakeVerificationAttemptId);

            // Rate-limit lookup index
            entity.HasIndex(e => new { e.TenantId, e.LocationId, e.IpAddress, e.AttemptedAt }, "IX_IntakeVerificationAttempts_RateLimit")
                .IsDescending(false, false, false, true);

            entity.Property(e => e.IpAddress).HasMaxLength(45);
            entity.Property(e => e.AttemptedSsnLast4Hash).HasMaxLength(128);
            entity.Property(e => e.UserAgent).HasMaxLength(500);
            entity.Property(e => e.AttemptedAt).HasDefaultValueSql("(getutcdate())");
            entity.Property(e => e.IsSuccessful).HasDefaultValue(false);

            entity.HasOne(d => d.Tenant).WithMany()
                .HasForeignKey(d => d.TenantId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_IntakeVerificationAttempts_Tenants");

            entity.HasOne(d => d.Location).WithMany()
                .HasForeignKey(d => d.LocationId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_IntakeVerificationAttempts_Locations");

            entity.HasOne(d => d.Patient).WithMany()
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_IntakeVerificationAttempts_Patients");
        });

        // Wire IntakeSubmission + Source default on the 6 existing clinical tables.
        modelBuilder.Entity<PatientAllergy>(entity =>
        {
            entity.Property(e => e.Source).HasDefaultValue(1);
            entity.HasOne(d => d.IntakeSubmission).WithMany()
                .HasForeignKey(d => d.IntakeSubmissionId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_PatientAllergies_IntakeSubmissions");
        });

        modelBuilder.Entity<PatientMedication>(entity =>
        {
            entity.Property(e => e.Source).HasDefaultValue(1);
            entity.HasOne(d => d.IntakeSubmission).WithMany()
                .HasForeignKey(d => d.IntakeSubmissionId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_PatientMedications_IntakeSubmissions");
        });

        modelBuilder.Entity<PatientProblem>(entity =>
        {
            entity.Property(e => e.Source).HasDefaultValue(1);
            entity.HasOne(d => d.IntakeSubmission).WithMany()
                .HasForeignKey(d => d.IntakeSubmissionId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_PatientProblems_IntakeSubmissions");
        });

        modelBuilder.Entity<PatientFamilyHistory>(entity =>
        {
            entity.Property(e => e.Source).HasDefaultValue(1);
            entity.HasOne(d => d.IntakeSubmission).WithMany()
                .HasForeignKey(d => d.IntakeSubmissionId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_PatientFamilyHistories_IntakeSubmissions");
        });

        modelBuilder.Entity<PatientSocialHistory>(entity =>
        {
            entity.Property(e => e.Source).HasDefaultValue(1);
            entity.HasOne(d => d.IntakeSubmission).WithMany()
                .HasForeignKey(d => d.IntakeSubmissionId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_PatientSocialHistories_IntakeSubmissions");
        });

        modelBuilder.Entity<PatientImmunization>(entity =>
        {
            entity.Property(e => e.Source).HasDefaultValue(1);
            entity.HasOne(d => d.IntakeSubmission).WithMany()
                .HasForeignKey(d => d.IntakeSubmissionId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_PatientImmunizations_IntakeSubmissions");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
