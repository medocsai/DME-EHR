using EHR.Models;
using EHR.Models.Generated;
using EHR.Services.Intake;
using FluentAssertions;

namespace EHR.Tests.Services.Intake;

/// <summary>
/// Pure-logic tests for ProvenanceTagger — stamps Source + IntakeSubmissionId
/// on clinical rows. No DB, no I/O; just asserts the contract of each overload.
/// </summary>
public class ProvenanceTaggerTests
{
    private readonly ProvenanceTagger _tagger = new();

    [Fact]
    public void IntakeSource_EnumValues_MatchExpected()
    {
        ((int)IntakeSource.System).Should().Be(0);
        ((int)IntakeSource.Clinic).Should().Be(1);
        ((int)IntakeSource.Patient).Should().Be(2);
        ((int)IntakeSource.Kiosk).Should().Be(3);
        ((int)IntakeSource.Import).Should().Be(4);
    }

    [Fact]
    public void Tag_Allergy_Patient_StampsSourceAndSubmissionId()
    {
        var entity = new PatientAllergy();

        _tagger.Tag(entity, IntakeSource.Patient, intakeSubmissionId: 17);

        entity.Source.Should().Be((int)IntakeSource.Patient);
        entity.IntakeSubmissionId.Should().Be(17);
    }

    [Fact]
    public void Tag_Medication_Patient_StampsSourceAndSubmissionId()
    {
        var entity = new PatientMedication();

        _tagger.Tag(entity, IntakeSource.Patient, intakeSubmissionId: 17);

        entity.Source.Should().Be((int)IntakeSource.Patient);
        entity.IntakeSubmissionId.Should().Be(17);
    }

    [Fact]
    public void Tag_Problem_Patient_StampsSourceAndSubmissionId()
    {
        var entity = new PatientProblem();

        _tagger.Tag(entity, IntakeSource.Patient, intakeSubmissionId: 17);

        entity.Source.Should().Be((int)IntakeSource.Patient);
        entity.IntakeSubmissionId.Should().Be(17);
    }

    [Fact]
    public void Tag_FamilyHistory_Patient_StampsSourceAndSubmissionId()
    {
        var entity = new PatientFamilyHistory();

        _tagger.Tag(entity, IntakeSource.Patient, intakeSubmissionId: 17);

        entity.Source.Should().Be((int)IntakeSource.Patient);
        entity.IntakeSubmissionId.Should().Be(17);
    }

    [Fact]
    public void Tag_SocialHistory_Patient_StampsSourceAndSubmissionId()
    {
        var entity = new PatientSocialHistory();

        _tagger.Tag(entity, IntakeSource.Patient, intakeSubmissionId: 17);

        entity.Source.Should().Be((int)IntakeSource.Patient);
        entity.IntakeSubmissionId.Should().Be(17);
    }

    [Fact]
    public void Tag_Immunization_Patient_StampsSourceAndSubmissionId()
    {
        var entity = new PatientImmunization();

        _tagger.Tag(entity, IntakeSource.Patient, intakeSubmissionId: 17);

        entity.Source.Should().Be((int)IntakeSource.Patient);
        entity.IntakeSubmissionId.Should().Be(17);
    }

    [Fact]
    public void Tag_HealthConcern_Patient_StampsSourceAndSubmissionId()
    {
        var entity = new PatientHealthConcern();

        _tagger.Tag(entity, IntakeSource.Patient, intakeSubmissionId: 17);

        entity.Source.Should().Be((int)IntakeSource.Patient);
        entity.IntakeSubmissionId.Should().Be(17);
    }

    [Fact]
    public void Tag_Supplement_Patient_StampsSourceAndSubmissionId()
    {
        var entity = new PatientSupplement();

        _tagger.Tag(entity, IntakeSource.Patient, intakeSubmissionId: 17);

        entity.Source.Should().Be((int)IntakeSource.Patient);
        entity.IntakeSubmissionId.Should().Be(17);
    }

    [Fact]
    public void Tag_LongevityProfile_Patient_StampsSourceAndSubmissionId()
    {
        var entity = new PatientLongevityProfile();

        _tagger.Tag(entity, IntakeSource.Patient, intakeSubmissionId: 17);

        entity.Source.Should().Be((int)IntakeSource.Patient);
        entity.IntakeSubmissionId.Should().Be(17);
    }

    [Fact]
    public void Tag_ClinicSource_StampsSourceOne()
    {
        var entity = new PatientAllergy();

        _tagger.Tag(entity, IntakeSource.Clinic, intakeSubmissionId: 5);

        entity.Source.Should().Be((int)IntakeSource.Clinic);
        entity.Source.Should().Be(1);
    }

    [Fact]
    public void Tag_NullSubmissionId_LeavesIntakeSubmissionIdUnchanged()
    {
        var entity = new PatientAllergy { IntakeSubmissionId = 99 };

        _tagger.Tag(entity, IntakeSource.Clinic, intakeSubmissionId: null);

        entity.Source.Should().Be((int)IntakeSource.Clinic);
        entity.IntakeSubmissionId.Should().Be(99);
    }

    [Fact]
    public void Tag_SubmissionIdOmitted_DefaultsToNullParam_LeavesFieldUnchanged()
    {
        var entity = new PatientAllergy { IntakeSubmissionId = 123 };

        _tagger.Tag(entity, IntakeSource.Patient);

        entity.Source.Should().Be((int)IntakeSource.Patient);
        entity.IntakeSubmissionId.Should().Be(123);
    }
}
