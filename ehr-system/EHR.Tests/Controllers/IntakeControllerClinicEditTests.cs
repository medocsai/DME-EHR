using System.Security.Claims;
using System.Text.Json;
using EHR.Controllers;
using EHR.Models;
using EHR.Models.Generated;
using EHR.Services.Intake;
using EHR.Services.Intake.Dtos;
using EHR.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EHR.Tests.Controllers;

/// <summary>
/// Step 1 controller tests for the clinic-side intake edit endpoints introduced in
/// rules/technical/intake-on-clinical-note.md §4.4.
///
/// Locks (HIPAA-critical):
///   1. Tenant isolation — staff cannot touch a patient outside their tenant.
///   2. Role guard — FrontDesk/Biller/ReadOnly cannot write through these endpoints.
///   3. Happy path — Clinician and Nurse POST/DELETE delegate to the existing service unchanged.
///   4. GET endpoints accept any authenticated staff in tenant.
///
/// We mock all service dependencies and use an InMemory EhrDbContext for the tenant
/// lookup. JWT claims are forged onto ControllerContext.HttpContext.User.
/// </summary>
public class IntakeControllerClinicEditTests
{
    private const int TenantId = 1;
    private const int OtherTenantId = 99;
    private const int PatientId = 42;

    // Role enum values (from Models/Enums/AllEnums.cs UserRole):
    //   SuperAdmin=0, ClinicAdmin=1, Clinician=2, FrontDesk=3, Biller=4,
    //   ReadOnly=5, MedicalAssistant=6, Nurse=7
    private const int RoleClinician = 2;
    private const int RoleFrontDesk = 3;
    private const int RoleBiller = 4;
    private const int RoleNurse = 7;

    // ---------- helpers ----------

    private static EhrDbContext NewDbWithPatient(int patientId = PatientId, int tenantId = TenantId)
    {
        var db = InMemoryDbFactory.Create();
        db.Patients.Add(new Patient
        {
            PatientId = patientId,
            TenantId = tenantId,
            Mrn = $"MRN-{patientId}",
            FirstName = "Test",
            LastName = "Patient",
            Gender = "M",
            DateOfBirth = new DateOnly(1985, 6, 15)
        });
        db.SaveChanges();
        return db;
    }

    private static IntakeController NewController(
        EhrDbContext db,
        Mock<IIntakeSubmissionService> submissions,
        Mock<IIntakeProgressCalculator> progress,
        Mock<IIntakePrefillService> prefill,
        int callerTenantId,
        int callerRole,
        int callerUserId = 1)
    {
        var controller = new IntakeController(
            db,
            submissions.Object,
            progress.Object,
            new Mock<IPatientEnteredDataReader>().Object,
            new Mock<IIntakeAccessTokenService>().Object,
            new Mock<IPatientIdentityMatcher>().Object,
            new Mock<IIntakeAttemptThrottler>().Object,
            TestEncryptionHelper.Create(),
            prefill.Object,
            NullLogger<IntakeController>.Instance);

        var claims = new List<Claim>
        {
            new("UserId", callerUserId.ToString()),
            new("TenantId", callerTenantId.ToString()),
            new("Role", callerRole.ToString())
        };
        var identity = new ClaimsIdentity(claims, "Test");
        var user = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = user };
        // Connection.RemoteIpAddress is read by GetIpAddress() on save — DefaultHttpContext
        // gives us null which the helper coerces to "unknown".
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private static JsonElement EmptyJsonObject() => JsonDocument.Parse("{}").RootElement;

    // ================================================================
    // 1. Tenant isolation (HIPAA-critical)
    // ================================================================

    [Fact]
    public async Task SaveSection_PatientNotInCallerTenant_Returns403()
    {
        // Patient belongs to TenantId; caller's JWT carries OtherTenantId.
        // PatientInCallerTenantAsync must reject — no service call should happen.
        using var db = NewDbWithPatient(tenantId: TenantId);

        var submissions = new Mock<IIntakeSubmissionService>(MockBehavior.Strict);
        var progress = new Mock<IIntakeProgressCalculator>();
        var prefill = new Mock<IIntakePrefillService>();

        var controller = NewController(db, submissions, progress, prefill,
            callerTenantId: OtherTenantId, callerRole: RoleClinician);

        var result = await controller.SaveClinicIntakeSection(PatientId, "demographics", EmptyJsonObject());

        result.Result.Should().BeOfType<ForbidResult>();
        submissions.Verify(s => s.SaveSectionAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(),
            It.IsAny<JsonElement>(), It.IsAny<IntakeChannel>(),
            It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ================================================================
    // 2. Role guard on POST blocks non-write roles
    // ================================================================

    [Fact]
    public async Task SaveSection_FrontDeskRole_Returns403()
    {
        using var db = NewDbWithPatient();

        var submissions = new Mock<IIntakeSubmissionService>(MockBehavior.Strict);
        var progress = new Mock<IIntakeProgressCalculator>();
        var prefill = new Mock<IIntakePrefillService>();

        var controller = NewController(db, submissions, progress, prefill,
            callerTenantId: TenantId, callerRole: RoleFrontDesk);

        var result = await controller.SaveClinicIntakeSection(PatientId, "demographics", EmptyJsonObject());

        result.Result.Should().BeOfType<ForbidResult>();
        submissions.Verify(s => s.SaveSectionAsync(
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(),
            It.IsAny<JsonElement>(), It.IsAny<IntakeChannel>(),
            It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ================================================================
    // 3. Happy path: Clinician POST delegates to existing service
    // ================================================================

    [Fact]
    public async Task SaveSection_Clinician_DelegatesToService()
    {
        using var db = NewDbWithPatient();

        var submissions = new Mock<IIntakeSubmissionService>();
        submissions.Setup(s => s.SaveSectionAsync(
                PatientId, TenantId, "demographics",
                It.IsAny<JsonElement>(), IntakeChannel.Portal,
                It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new IntakeSubmissionResultDto { Success = true, SubmissionId = 1 });

        var progress = new Mock<IIntakeProgressCalculator>();
        var prefill = new Mock<IIntakePrefillService>();

        var controller = NewController(db, submissions, progress, prefill,
            callerTenantId: TenantId, callerRole: RoleClinician);

        var result = await controller.SaveClinicIntakeSection(PatientId, "demographics", EmptyJsonObject());

        var ok = result.Result as OkObjectResult;
        ok.Should().NotBeNull();
        var dto = ok!.Value as IntakeSubmissionResultDto;
        dto!.Success.Should().BeTrue();

        submissions.Verify(s => s.SaveSectionAsync(
                PatientId, TenantId, "demographics",
                It.IsAny<JsonElement>(), IntakeChannel.Portal,
                It.IsAny<string>(), It.IsAny<string>()),
            Times.Once);
    }

    // ================================================================
    // 4. Nurse role works (manager-added role per 2026-04-28 discussion)
    // ================================================================

    [Fact]
    public async Task SaveSection_Nurse_DelegatesToService()
    {
        using var db = NewDbWithPatient();

        var submissions = new Mock<IIntakeSubmissionService>();
        submissions.Setup(s => s.SaveSectionAsync(
                PatientId, TenantId, "concerns",
                It.IsAny<JsonElement>(), IntakeChannel.Portal,
                It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new IntakeSubmissionResultDto { Success = true, SubmissionId = 2 });

        var progress = new Mock<IIntakeProgressCalculator>();
        var prefill = new Mock<IIntakePrefillService>();

        var controller = NewController(db, submissions, progress, prefill,
            callerTenantId: TenantId, callerRole: RoleNurse);

        var result = await controller.SaveClinicIntakeSection(PatientId, "concerns", EmptyJsonObject());

        result.Result.Should().BeOfType<OkObjectResult>();
        submissions.Verify(s => s.SaveSectionAsync(
                PatientId, TenantId, "concerns",
                It.IsAny<JsonElement>(), IntakeChannel.Portal,
                It.IsAny<string>(), It.IsAny<string>()),
            Times.Once);
    }

    // ================================================================
    // 5. Role guard on DELETE blocks non-write roles
    // ================================================================

    [Fact]
    public async Task DeleteRow_BillerRole_Returns403()
    {
        using var db = NewDbWithPatient();

        var submissions = new Mock<IIntakeSubmissionService>(MockBehavior.Strict);
        var progress = new Mock<IIntakeProgressCalculator>();
        var prefill = new Mock<IIntakePrefillService>();

        var controller = NewController(db, submissions, progress, prefill,
            callerTenantId: TenantId, callerRole: RoleBiller);

        var result = await controller.DeleteClinicIntakeRow(PatientId, "PatientAllergies", 100);

        result.Should().BeOfType<ForbidResult>();
        submissions.Verify(s => s.DeletePatientEnteredRowAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    // ================================================================
    // 6. GET endpoints accept any authenticated staff in tenant
    // ================================================================

    [Fact]
    public async Task GetProgress_AnyAuthenticatedStaff_ReturnsOk()
    {
        // Even view-only roles (FrontDesk/Biller/ReadOnly) should be able to GET
        // progress. Only the patient-tenant check applies on GET — no role guard.
        using var db = NewDbWithPatient();

        var submissions = new Mock<IIntakeSubmissionService>();
        var progress = new Mock<IIntakeProgressCalculator>();
        progress.Setup(p => p.CalculateAsync(PatientId))
            .ReturnsAsync(new IntakeProgressDto { Completed = 3, Total = 8 });
        var prefill = new Mock<IIntakePrefillService>();

        var controller = NewController(db, submissions, progress, prefill,
            callerTenantId: TenantId, callerRole: RoleFrontDesk);

        var result = await controller.GetClinicIntakeProgress(PatientId);

        var ok = result.Result as OkObjectResult;
        ok.Should().NotBeNull();
        var dto = ok!.Value as IntakeProgressDto;
        dto!.Completed.Should().Be(3);
        dto.Total.Should().Be(8);
    }
}
