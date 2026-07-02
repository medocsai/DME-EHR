using System;
using System.Collections.Generic;

namespace EHR.Models.Generated;

public partial class Note
{
    public int NoteId { get; set; }

    public int TenantId { get; set; }

    public int PatientId { get; set; }

    public int ProviderId { get; set; }

    public int? AppointmentId { get; set; }

    public int? CareEpisodeId { get; set; }

    public int Type { get; set; }

    public DateOnly ServiceDate { get; set; }

    public string Subjective { get; set; }

    public string Objective { get; set; }

    public string Assessment { get; set; }

    public string Plan { get; set; }

    public string VitalSigns { get; set; }

    public string FunctionalTests { get; set; }

    public string Interventions { get; set; }

    public string PatientEducation { get; set; }

    public string HomeExerciseProgram { get; set; }

    public string Cptcodes { get; set; }

    public string Icdcodes { get; set; }

    public int? TotalMinutes { get; set; }

    public int? DirectMinutes { get; set; }

    public int? IndirectMinutes { get; set; }

    public int? Status { get; set; }

    public DateTime? SignedAt { get; set; }

    public int? SignedBy { get; set; }

    public string SignatureData { get; set; }

    public int? CoSignedBy { get; set; }

    public DateTime? CoSignedAt { get; set; }

    public int? ParentNoteId { get; set; }

    public bool? IsAddendum { get; set; }

    public DateTime? CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public int? CreatedBy { get; set; }

    public virtual Appointment Appointment { get; set; }

    public virtual CareEpisode CareEpisode { get; set; }

    public virtual ICollection<Charge> Charges { get; set; } = new List<Charge>();

    public virtual ICollection<Note> InverseParentNote { get; set; } = new List<Note>();

    public virtual Note ParentNote { get; set; }

    public virtual Patient Patient { get; set; }

    public virtual Provider Provider { get; set; }

    public virtual Tenant Tenant { get; set; }
}
