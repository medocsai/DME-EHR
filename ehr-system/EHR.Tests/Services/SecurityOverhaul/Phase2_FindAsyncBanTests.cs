using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace EHR.Tests.Services.SecurityOverhaul;

/// <summary>
/// Phase 2 of the security overhaul (rules/technical/security-overhaul-plan.md
/// §6 Phase 2 step 3): make DbSet&lt;T&gt;.FindAsync on tenant-scoped entities a
/// build error. FindAsync bypasses EF Core global query filters (it is a
/// primary-key lookup against the identity map) so a route handler that takes
/// a user-supplied int ID and passes it to FindAsync exposes cross-tenant rows
/// even when HasQueryFilter is configured. This was the mechanism behind the
/// pentest's appointment-creation cross-tenant exploit.
///
/// The test source-scans every production .cs file for the pattern
/// `_context.{InScopeDbSet}.FindAsync(` and asserts the count is zero. Phase 3
/// guards eliminate the legacy call sites by routing through the contractor
/// stack (PublicIdResolver.Resolve&lt;T&gt; -> int -> tenant + location check ->
/// hand back to service). Until Phase 3 ships, the assertion is recorded as a
/// baseline count rather than an outright failure so this file lands without
/// breaking the build.
/// </summary>
[Trait("Phase", "2")]
public class Phase2_FindAsyncBanTests
{
    /// <summary>
    /// In-scope DbSet names. Mirrors §4.1 / Phase0_SchemaTests.InScopeEntities,
    /// but uses the EhrDbContext DbSet property names (plural) since that is
    /// what code calls (e.g. `_context.Patients`, not `_context.Patient`).
    /// </summary>
    private static readonly string[] InScopeDbSets =
    {
        "Patients", "Providers", "Users", "Tenants", "Locations", "Appointments",
        "Encounters", "ClinicalNotes", "ClinicalNoteAmendments",
        "ClinicalNoteAddendums", "Prescriptions", "Orders", "BillingClaims",
        "Charges", "Insurances", "Authorizations", "CareEpisodes",
        "CareEpisodeConsents", "CareEpisodeConsentForms", "Consents",
        "ConsentFormTemplates", "PatientDocuments", "RecordingSessions",
        "TranscriptionChunks", "Messages", "Conversations", "PatientStickyNotes",
        "PatientHealthConcerns", "PatientProblems", "PatientMedications",
        "PatientSupplements", "PatientImmunizations", "PatientAllergies",
        "PatientFamilyHistories", "PatientSocialHistories",
        "PatientIntakeSubmissions", "CareNotes",
        // Phase 0a additions
        "OrderResults", "PatientVitals", "TreatmentPlans",
        "PatientLongevityProfiles", "PatientGenderHealths",
        "PatientConversations", "PatientMessages",
        "TelehealthTranscriptionChunks", "PatientLedgers", "Payments",
        "PaymentRefunds", "InstallmentPlans", "InstallmentDetails",
        "ClaimStatusHistories", "PatientPortalAccounts",
        "PatientPortalInvitations", "PatientPortalOtps",
        "PatientPortalPasswordResets", "TrustedDevices",
        "IntakeVerificationAttempts", "KioskSessions",
        "KioskVerificationAttempts", "ProviderSchedules",
        "TherapistUnavailabilities", "ClinicalNoteTemplates",
        "ProviderFavoriteCodes", "CredentialingRecords", "MedicalLienTemplates",
        "AuditLogs", "InstallmentPlanAuditLogs", "Notes",
        "StripeConnectAccounts", "SystemSettings",
    };

    /// <summary>
    /// Walks production source (ehr-system/Services + ehr-system/Controllers)
    /// and counts FindAsync call sites on in-scope DbSets. Returns the count
    /// and the file list for diagnostics.
    /// </summary>
    private static (int count, string[] files) ScanForFindAsync()
    {
        var root = ProductionRoot();
        var pattern = new Regex(
            @"_context\.(" + string.Join("|", InScopeDbSets) + @")\.FindAsync\(",
            RegexOptions.Compiled);

        var offenders = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}EHR.Tests{Path.DirectorySeparatorChar}"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
            .Select(f => new { File = f, Hits = pattern.Matches(File.ReadAllText(f)).Count })
            .Where(x => x.Hits > 0)
            .ToArray();

        return (offenders.Sum(x => x.Hits), offenders.Select(x => x.File).ToArray());
    }

    private static string ProductionRoot()
    {
        // Walk up from the test bin folder until we find the ehr-system dir.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && dir.Name != "ehr-system")
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException(
            "Could not locate ehr-system root from " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// Snapshot the current baseline so any NEW FindAsync call site added
    /// after this test lands fails the build. The baseline is updated each
    /// time Phase 3 eliminates call sites (or sooner via a targeted sweep).
    /// </summary>
    [Fact]
    public void FindAsync_OnInScopeEntities_DoesNotExceedBaseline()
    {
        // Baseline as of 2026-05-26 (Phase 2 ship): 139 known call sites
        // across 26 files (services + a handful of controllers). Phase 3 will
        // drive this to 0 as controllers/services flip to guard-based access.
        // Any NEW FindAsync added between now and then must lower this
        // baseline first.
        const int Baseline = 139;

        var (count, files) = ScanForFindAsync();

        count.Should().BeLessOrEqualTo(
            Baseline,
            $"FindAsync on tenant-scoped entities is banned (bypasses EF global query filters; this was the pentest exploit vector). " +
            $"Found {count} call sites across {files.Length} files. Use .Where(x => x.Id == id).FirstOrDefaultAsync() instead, " +
            $"or call the entity's access guard (Phase 1). Files: {string.Join(", ", files.Select(Path.GetFileName))}");
    }

    /// <summary>
    /// Hard zero-tolerance assertion. Enabled once Phase 3 finishes the
    /// guard-call-first flip and the baseline above hits 0.
    /// </summary>
    [Fact(Skip = "Enable after Phase 3 eliminates all legacy FindAsync calls on in-scope entities.")]
    public void FindAsync_OnInScopeEntities_IsForbidden()
    {
        var (count, files) = ScanForFindAsync();
        count.Should().Be(0,
            $"Phase 3 must eliminate every FindAsync call on tenant-scoped entities. " +
            $"Still {count} in {files.Length} files: {string.Join(", ", files.Select(Path.GetFileName))}");
    }
}
