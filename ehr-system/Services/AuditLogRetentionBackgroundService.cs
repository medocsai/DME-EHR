using EHR.Models.Generated;
using Microsoft.EntityFrameworkCore;

namespace EHR.Services;

/// <summary>
/// Daily background sweep that purges AuditLog rows older than
/// HIPAA:AuditRetentionDays (default 2190 = 6 years per HIPAA recommendation).
/// AuditLogs has an INSTEAD OF UPDATE/DELETE trigger that blocks deletion
/// unless SESSION_CONTEXT('AllowAuditDelete') = 1, so this is the only path
/// that can prune the table — a malicious admin or compromised app account
/// cannot wipe their own access trail.
///
/// Deletion is batched (5 000 rows per loop) so a multi-million-row purge
/// does not lock the table or blow the log. Runs once on startup (after a
/// 1-minute warm-up) and then every 24h.
/// </summary>
public class AuditLogRetentionBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditLogRetentionBackgroundService> _logger;
    private readonly IConfiguration _config;

    private const int BatchSize = 5_000;
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan WarmupDelay = TimeSpan.FromMinutes(1);

    public AuditLogRetentionBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<AuditLogRetentionBackgroundService> logger,
        IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AuditLog retention service started");

        try { await Task.Delay(WarmupDelay, stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AuditLog retention sweep failed; will retry in {Hours}h", SweepInterval.TotalHours);
            }

            try { await Task.Delay(SweepInterval, stoppingToken); }
            catch (TaskCanceledException) { return; }
        }
    }

    private async Task SweepOnceAsync(CancellationToken ct)
    {
        // Default 2190 days = 6 years. HIPAA Security Rule §164.316(b)(2) requires
        // documentation be retained at least 6 years from the date of creation
        // or last effective date, whichever is later.
        var retentionDays = _config.GetValue("HIPAA:AuditRetentionDays", 2190);
        if (retentionDays <= 0)
        {
            _logger.LogWarning("HIPAA:AuditRetentionDays is {Days} (<=0); skipping retention sweep to avoid wiping the table", retentionDays);
            return;
        }

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EhrDbContext>();

        int totalDeleted = 0;
        int loop = 0;

        while (!ct.IsCancellationRequested)
        {
            loop++;

            // Set the session-context flag the trigger checks. This is per-
            // connection — must be re-set if the connection rolls over. EF
            // Core re-uses the connection for the duration of this scope.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"EXEC sp_set_session_context N'AllowAuditDelete', 1", ct);

            var deleted = await db.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE TOP ({BatchSize}) FROM dbo.AuditLogs
                WHERE [Timestamp] < {cutoff}", ct);

            totalDeleted += deleted;

            if (deleted < BatchSize)
                break; // nothing more to purge this cycle

            // Yield briefly between batches so we don't pin the connection.
            try { await Task.Delay(TimeSpan.FromMilliseconds(250), ct); }
            catch (TaskCanceledException) { return; }
        }

        if (totalDeleted > 0)
        {
            _logger.LogInformation(
                "AuditLog retention sweep removed {Count} row(s) older than {Cutoff:O} ({Days} days); {Loops} batch loop(s)",
                totalDeleted, cutoff, retentionDays, loop);
        }
        else
        {
            _logger.LogInformation(
                "AuditLog retention sweep — no rows older than {Cutoff:O}",
                cutoff);
        }
    }
}
