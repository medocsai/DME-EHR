using System;
using System.Collections.Generic;

namespace EHR.Services.Intake.Dtos;

public class IntakeSectionStatusDto
{
    public string Name { get; set; }
    public bool Filled { get; set; }
    public DateTime? Timestamp { get; set; }
}

public class IntakeProgressDto
{
    public int Completed { get; set; }
    public int Total { get; set; }
    public List<IntakeSectionStatusDto> Sections { get; set; } = new();
    public int? CurrentSubmissionId { get; set; }
    public DateTime? SubmittedAt { get; set; }
}

public class IntakeSectionItemDto
{
    public string Table { get; set; }
    public int Id { get; set; }
    public string Label { get; set; }
    public string Detail { get; set; }
    public DateTime? SavedAt { get; set; }
}

public class IntakeSectionViewDto
{
    public string Name { get; set; }
    public List<IntakeSectionItemDto> Items { get; set; } = new();
    public DateTime? LastSavedAt { get; set; }
}

public class PatientIntakeViewDto
{
    public int PatientId { get; set; }
    public List<IntakeSectionViewDto> Sections { get; set; } = new();
    public IntakeProgressDto Progress { get; set; }
}

public class ThrottleResultDto
{
    public bool Allowed { get; set; }
    public DateTime? LockoutEndAt { get; set; }
    public int RecentFailures { get; set; }
}

public class IntakeSubmissionResultDto
{
    public bool Success { get; set; }
    public int SubmissionId { get; set; }
    public IntakeProgressDto Progress { get; set; }
    public string Message { get; set; }
}
