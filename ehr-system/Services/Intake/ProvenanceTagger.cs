using EHR.Models;
using EHR.Models.Generated;

namespace EHR.Services.Intake;

/// <summary>
/// Contractor that stamps Source + IntakeSubmissionId on any clinical row.
/// Explicit per-entity methods (no reflection) to keep the pattern boring and discoverable.
/// Source stays immutable after first write; this helper is for create/first-touch only.
/// </summary>
public interface IProvenanceTagger
{
    void Tag(PatientAllergy entity, IntakeSource source, int? intakeSubmissionId = null);
    void Tag(PatientMedication entity, IntakeSource source, int? intakeSubmissionId = null);
    void Tag(PatientProblem entity, IntakeSource source, int? intakeSubmissionId = null);
    void Tag(PatientFamilyHistory entity, IntakeSource source, int? intakeSubmissionId = null);
    void Tag(PatientSocialHistory entity, IntakeSource source, int? intakeSubmissionId = null);
    void Tag(PatientImmunization entity, IntakeSource source, int? intakeSubmissionId = null);
    void Tag(PatientHealthConcern entity, IntakeSource source, int? intakeSubmissionId = null);
    void Tag(PatientSupplement entity, IntakeSource source, int? intakeSubmissionId = null);
    void Tag(PatientLongevityProfile entity, IntakeSource source, int? intakeSubmissionId = null);
}

public class ProvenanceTagger : IProvenanceTagger
{
    public void Tag(PatientAllergy entity, IntakeSource source, int? intakeSubmissionId = null)
    {
        entity.Source = (int)source;
        if (intakeSubmissionId.HasValue) entity.IntakeSubmissionId = intakeSubmissionId;
    }

    public void Tag(PatientMedication entity, IntakeSource source, int? intakeSubmissionId = null)
    {
        entity.Source = (int)source;
        if (intakeSubmissionId.HasValue) entity.IntakeSubmissionId = intakeSubmissionId;
    }

    public void Tag(PatientProblem entity, IntakeSource source, int? intakeSubmissionId = null)
    {
        entity.Source = (int)source;
        if (intakeSubmissionId.HasValue) entity.IntakeSubmissionId = intakeSubmissionId;
    }

    public void Tag(PatientFamilyHistory entity, IntakeSource source, int? intakeSubmissionId = null)
    {
        entity.Source = (int)source;
        if (intakeSubmissionId.HasValue) entity.IntakeSubmissionId = intakeSubmissionId;
    }

    public void Tag(PatientSocialHistory entity, IntakeSource source, int? intakeSubmissionId = null)
    {
        entity.Source = (int)source;
        if (intakeSubmissionId.HasValue) entity.IntakeSubmissionId = intakeSubmissionId;
    }

    public void Tag(PatientImmunization entity, IntakeSource source, int? intakeSubmissionId = null)
    {
        entity.Source = (int)source;
        if (intakeSubmissionId.HasValue) entity.IntakeSubmissionId = intakeSubmissionId;
    }

    public void Tag(PatientHealthConcern entity, IntakeSource source, int? intakeSubmissionId = null)
    {
        entity.Source = (int)source;
        if (intakeSubmissionId.HasValue) entity.IntakeSubmissionId = intakeSubmissionId;
    }

    public void Tag(PatientSupplement entity, IntakeSource source, int? intakeSubmissionId = null)
    {
        entity.Source = (int)source;
        if (intakeSubmissionId.HasValue) entity.IntakeSubmissionId = intakeSubmissionId;
    }

    public void Tag(PatientLongevityProfile entity, IntakeSource source, int? intakeSubmissionId = null)
    {
        entity.Source = (int)source;
        if (intakeSubmissionId.HasValue) entity.IntakeSubmissionId = intakeSubmissionId;
    }
}
