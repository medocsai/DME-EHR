using System.Collections.Concurrent;

namespace EHR.Services;

/// <summary>
/// Tracks background note generation jobs in memory.
/// Singleton service — jobs persist across requests but not across app restarts.
/// </summary>
public interface INoteGenerationJobService
{
    string CreateJob(int encounterId, int userId, string patientName, List<int> templateIds);
    NoteGenerationJob? GetJob(string jobId);
    void UpdateProgress(string jobId, int completedCount, NoteGenerationNoteResult? latestNote = null);
    void CompleteJob(string jobId);
    void FailJob(string jobId, string errorMessage);
    List<NoteGenerationJob> GetActiveJobsForUser(int userId);
}

public class NoteGenerationJobService : INoteGenerationJobService
{
    private readonly ConcurrentDictionary<string, NoteGenerationJob> _jobs = new();
    private readonly Timer _cleanupTimer;

    public NoteGenerationJobService()
    {
        // Clean up expired jobs every 15 minutes
        _cleanupTimer = new Timer(_ => CleanupExpiredJobs(), null, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15));
    }

    public string CreateJob(int encounterId, int userId, string patientName, List<int> templateIds)
    {
        var jobId = Guid.NewGuid().ToString("N")[..12];
        var job = new NoteGenerationJob
        {
            JobId = jobId,
            EncounterId = encounterId,
            UserId = userId,
            PatientName = patientName,
            TotalNotes = templateIds.Count,
            TemplateIds = templateIds,
            Status = NoteGenerationStatus.Processing,
            CreatedAt = DateTime.UtcNow
        };
        _jobs[jobId] = job;
        return jobId;
    }

    public NoteGenerationJob? GetJob(string jobId)
    {
        _jobs.TryGetValue(jobId, out var job);
        return job;
    }

    public void UpdateProgress(string jobId, int completedCount, NoteGenerationNoteResult? latestNote = null)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.CompletedNotes = completedCount;
            if (latestNote != null)
                job.GeneratedNotes.Add(latestNote);
        }
    }

    public void CompleteJob(string jobId)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.Status = NoteGenerationStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
        }
    }

    public void FailJob(string jobId, string errorMessage)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.Status = job.CompletedNotes > 0
                ? NoteGenerationStatus.PartiallyCompleted
                : NoteGenerationStatus.Failed;
            job.ErrorMessage = errorMessage;
            job.CompletedAt = DateTime.UtcNow;
        }
    }

    public List<NoteGenerationJob> GetActiveJobsForUser(int userId)
    {
        return _jobs.Values
            .Where(j => j.UserId == userId && j.Status == NoteGenerationStatus.Processing)
            .OrderByDescending(j => j.CreatedAt)
            .ToList();
    }

    private void CleanupExpiredJobs()
    {
        var cutoff = DateTime.UtcNow.AddHours(-1);
        var expiredKeys = _jobs.Where(kvp => kvp.Value.CreatedAt < cutoff).Select(kvp => kvp.Key).ToList();
        foreach (var key in expiredKeys)
            _jobs.TryRemove(key, out _);
    }
}

public class NoteGenerationJob
{
    public string JobId { get; set; } = "";
    public int EncounterId { get; set; }
    public int UserId { get; set; }
    public string PatientName { get; set; } = "";
    public int TotalNotes { get; set; }
    public int CompletedNotes { get; set; }
    public List<int> TemplateIds { get; set; } = new();
    public List<NoteGenerationNoteResult> GeneratedNotes { get; set; } = new();
    public NoteGenerationStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class NoteGenerationNoteResult
{
    public int ClinicalNoteId { get; set; }
    public int TemplateId { get; set; }
    public string TemplateName { get; set; } = "";
}

public enum NoteGenerationStatus
{
    Processing = 0,
    Completed = 1,
    Failed = 2,
    PartiallyCompleted = 3
}
