using Microsoft.EntityFrameworkCore;
using EHR.Helpers;
using EHR.Models.Generated;

namespace EHR.Services;

/// <summary>
/// One-time backfill: ensures every patient with a non-empty email has an
/// "EmailExact" search token (full-length hash of the normalized email).
///
/// Why this exists:
///   The original "Email" tokens are prefix-bounded (1..15 chars), which
///   meant CheckEmailUniqueAsync silently missed any email longer than 15
///   characters. The fix introduces a separate "EmailExact" token that
///   stores the full-length hash. This service backfills that token for
///   patients created before the fix shipped.
///
/// Runs once at startup, then exits. Safe to leave deployed indefinitely
/// because IndexPatientAsync removes existing tokens for the patient before
/// re-inserting, so re-running the backfill is a no-op.
/// </summary>
public class EmailExactBackfillService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailExactBackfillService> _logger;

    public EmailExactBackfillService(
        IServiceScopeFactory scopeFactory,
        ILogger<EmailExactBackfillService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the app finish startup before we hit the DB.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
        catch (OperationCanceledException) { return; }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EhrDbContext>();
            var blindIndex = scope.ServiceProvider.GetRequiredService<IBlindIndexService>();
            var encryptionHelper = scope.ServiceProvider.GetRequiredService<EncryptionHelper>();

            // Patients that already have an EmailExact token — skip them.
            var alreadyIndexed = await db.PatientSearchTokens
                .Where(t => t.FieldType == "EmailExact")
                .Select(t => t.PatientId)
                .Distinct()
                .ToListAsync(stoppingToken);

            var alreadyIndexedSet = new HashSet<int>(alreadyIndexed);

            // Pull all non-archived patients in batches; encrypted email is
            // decrypted in-memory so we can re-index. AsNoTracking to avoid
            // accidentally flushing decrypted values back to disk.
            var pageSize = 200;
            var skipped = 0;
            var reindexed = 0;

            while (!stoppingToken.IsCancellationRequested)
            {
                var batch = await db.Patients
                    .AsNoTracking()
                    .Where(p => p.IsDeleted != true)
                    .OrderBy(p => p.PatientId)
                    .Skip(reindexed + skipped)
                    .Take(pageSize)
                    .ToListAsync(stoppingToken);

                if (batch.Count == 0) break;

                foreach (var patient in batch)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    if (alreadyIndexedSet.Contains(patient.PatientId))
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        encryptionHelper.DecryptEntity(patient);

                        if (string.IsNullOrWhiteSpace(patient.Email))
                        {
                            // Nothing to index — record as processed so we don't
                            // re-scan on every restart.
                            skipped++;
                            continue;
                        }

                        await blindIndex.IndexPatientAsync(patient);
                        reindexed++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "EmailExactBackfill: failed to re-index patient {PatientId}",
                            patient.PatientId);
                    }
                }
            }

            if (reindexed > 0)
            {
                _logger.LogInformation(
                    "EmailExactBackfill complete: re-indexed {Reindexed} patient(s), {Skipped} already up-to-date.",
                    reindexed, skipped);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EmailExactBackfill failed");
        }
    }
}
