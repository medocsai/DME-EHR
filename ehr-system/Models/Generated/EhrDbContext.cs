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


    public virtual DbSet<AuditLog> AuditLogs { get; set; }















    public virtual DbSet<Location> Locations { get; set; }











    public virtual DbSet<Tenant> Tenants { get; set; }


    public virtual DbSet<User> Users { get; set; }

    // Trusted devices for "Remember this device" (15-day OTP bypass)
    public virtual DbSet<TrustedDevice> TrustedDevices { get; set; }




    // Patient Consent System entities






    // Recording Session entities


    // Telehealth Transcription Chunks (persisted from AI Scribe)

    // Medical Lien Template entities

    // Internal Messaging System entities


    // Internal Medicine entities

    // E-Prescribing

    // Orders Module (Labs, Imaging, Referrals)

    // Patient Sticky Notes (informal, non-clinical)

    // Care Notes (structured staff -> provider communication)

    // Patient Portal Auth (Email + Password + OTP)

    // Copay Integration - Installment Plans

    // Patient-Provider Messaging System

    // Stripe Connect (Phase 1)

    // Patient Intake (Phase 1)


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





        // ============================================
        // PATIENT SEARCH TOKENS (Blind Index for HIPAA-compliant search)
        // ============================================







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




        // ============================================
        // PATIENT SSN INDEX (for consent verification)
        // ============================================

        // ============================================
        // LOCATION KIOSK SETTINGS
        // ============================================

        // ============================================
        // CONSENT FORM TEMPLATES
        // ============================================

        // ============================================
        // CARE EPISODE CONSENTS
        // ============================================

        // ============================================
        // CARE EPISODE CONSENT FORMS
        // ============================================

        // ============================================
        // KIOSK VERIFICATION ATTEMPTS (Rate Limiting)
        // ============================================

        // ============================================
        // KIOSK SESSIONS
        // ============================================

        // ============================================
        // RECORDING SESSIONS
        // ============================================

        // ============================================
        // TRANSCRIPTION CHUNKS
        // ============================================

        // ============================================
        // TELEHEALTH TRANSCRIPTION CHUNKS
        // ============================================

        // ============================================
        // MEDICAL LIEN TEMPLATES
        // ============================================

        // ============================================
        // INTERNAL MESSAGING SYSTEM - CONVERSATIONS
        // ============================================

        // ============================================
        // INTERNAL MESSAGING SYSTEM - MESSAGES
        // ============================================

        // ============================================
        // INTERNAL MESSAGING SYSTEM - USER PRESENCE
        // ============================================

        // ============================================
        // PROVIDER FAVORITE CODES (Dx & CPT step)
        // Per-user favorite ICD-10 / CPT codes. Strict per-user isolation.
        // ============================================

        // ============================================
        // INTERNAL MEDICINE ENTITIES
        // ============================================










        // E-Prescribing: Prescription

        // E-Prescribing: Pharmacy

        // E-Prescribing: DrugDatabase (shared, no tenant)

        // ============================================
        // ORDERS MODULE (Labs, Imaging, Referrals)
        // ============================================





        // ============================================
        // CARE NOTES
        // Structured staff -> provider communication.
        // Content is PHI and encrypted by EncryptionHelper at save time.
        // ============================================

        // ============================================
        // PATIENT-PROVIDER MESSAGING SYSTEM - CONVERSATIONS
        // ============================================

        // ============================================
        // PATIENT-PROVIDER MESSAGING SYSTEM - MESSAGES
        // ============================================

        // ============================================
        // Stripe Connect Phase 1
        // ============================================




        // Location → StripeConnectAccount FK + new fee columns
        modelBuilder.Entity<Location>(entity =>
        {
            entity.Property(e => e.OnlineFeePercent).HasColumnType("decimal(5, 2)");
            entity.Property(e => e.CardPresentFeePercent).HasColumnType("decimal(5, 2)");

        });

        // Charge → Location FK

        // Payment → Location, StripeConnectAccount FKs + new columns

        // InstallmentPlan → Location, StripeConnectAccount FKs

        // ============================================
        // PATIENT INTAKE (Phase 1)
        // ============================================







        // Wire IntakeSubmission + Source default on the 6 existing clinical tables.






        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
