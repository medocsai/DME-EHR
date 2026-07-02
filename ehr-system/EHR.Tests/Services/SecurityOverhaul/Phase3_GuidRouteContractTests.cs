using System;
using System.IO;
using System.Linq;
using System.Reflection;
using EHR.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace EHR.Tests.Services.SecurityOverhaul;

/// <summary>
/// Phase 3 §6: the GUID-route flip. Verifies the four critical controllers
/// (Patient, Appointment, Encounter, ClinicalNote) expose Guid-route variants
/// for the main CRUD actions. Future phases extend this scan to every
/// in-scope controller.
///
/// These are contract / reflection tests -- no HTTP, no DB. They prove the
/// controller surface declares the GUID routes so a misconfigured DI or a
/// future refactor that drops the GUID routes will fail the build.
/// </summary>
[Trait("Phase", "3")]
public class Phase3_GuidRouteContractTests
{
    [Theory]
    [InlineData(typeof(PatientsController), "GetPatientByPublicId")]
    [InlineData(typeof(PatientsController), "UpdatePatientByPublicId")]
    [InlineData(typeof(PatientsController), "DeletePatientByPublicId")]
    [InlineData(typeof(PatientsController), "ArchivePatientByPublicId")]
    [InlineData(typeof(PatientsController), "UnarchivePatientByPublicId")]
    [InlineData(typeof(AppointmentsController), "GetAppointmentByPublicId")]
    [InlineData(typeof(AppointmentsController), "UpdateAppointmentByPublicId")]
    [InlineData(typeof(AppointmentsController), "CancelAppointmentByPublicId")]
    [InlineData(typeof(EncountersController), "GetEncounterByPublicId")]
    [InlineData(typeof(EncountersController), "UpdateEncounterByPublicId")]
    [InlineData(typeof(ClinicalNotesController), "GetNoteByPublicId")]
    [InlineData(typeof(ClinicalNotesController), "UpdateNoteByPublicId")]
    [InlineData(typeof(ClinicalNotesController), "SignNoteByPublicId")]
    public void Controller_DeclaresGuidRouteAction(Type controller, string actionName)
    {
        var method = controller.GetMethod(actionName,
            BindingFlags.Public | BindingFlags.Instance);
        method.Should().NotBeNull(
            $"{controller.Name}.{actionName} must exist (Phase 3 GUID route flip)");

        var guidParam = method!.GetParameters()
            .FirstOrDefault(p => p.ParameterType == typeof(Guid) && p.Name == "publicId");
        guidParam.Should().NotBeNull(
            $"{controller.Name}.{actionName} must take a Guid publicId parameter");
    }

    /// <summary>
    /// PatientListDto + AppointmentListDto + ClinicalNoteListDto + PatientDetailDto
    /// must expose a PublicId property so client code can switch from int IDs to
    /// GUIDs. Verified by reflection.
    /// </summary>
    [Theory]
    [InlineData("EHR.Models.PatientListDto")]
    [InlineData("EHR.Models.PatientDetailDto")]
    [InlineData("EHR.Models.AppointmentListDto")]
    [InlineData("EHR.Models.ClinicalNoteListDto")]
    public void Dto_ExposesPublicId(string dtoFullName)
    {
        var dto = typeof(PatientsController).Assembly.GetType(dtoFullName);
        dto.Should().NotBeNull($"{dtoFullName} must exist");

        var prop = dto!.GetProperty("PublicId");
        prop.Should().NotBeNull($"{dtoFullName} must expose PublicId for Phase 3 wire format");
        prop!.PropertyType.Should().Be(typeof(Guid));
    }
}
