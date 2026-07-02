using EHR.Models.Generated;
using EHR.Services.Intake.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EHR.Services.Intake;

/// <summary>
/// Rate-limits tablet intake verify attempts and logs every attempt to IntakeVerificationAttempts.
/// Mirrors KioskService's rate-limit logic but writes to a parallel table
/// (per rules, do not share kiosk's table in v1).
/// Limit: 5 failed attempts per (LocationId, IpAddress) per 15 minutes.
/// </summary>
public interface IIntakeAttemptThrottler
{
    Task<ThrottleResultDto> CheckAsync(int tenantId, int locationId, string ipAddress);
    Task LogAttemptAsync(
        int tenantId,
        int locationId,
        int? patientId,
        DateOnly? attemptedDob,
        string attemptedSsnLast4Hash,
        bool success,
        string ipAddress,
        string userAgent);
}

public class IntakeAttemptThrottler : IIntakeAttemptThrottler
{
    private const int MaxFailedAttempts = 5;
    private const int WindowMinutes = 15;

    private readonly EhrDbContext _context;
    private readonly ILogger<IntakeAttemptThrottler> _logger;

    public IntakeAttemptThrottler(EhrDbContext context, ILogger<IntakeAttemptThrottler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ThrottleResultDto> CheckAsync(int tenantId, int locationId, string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return new ThrottleResultDto { Allowed = true, RecentFailures = 0 };
        }

        var windowStart = DateTime.UtcNow.AddMinutes(-WindowMinutes);

        var recentFailures = await _context.IntakeVerificationAttempts
            .Where(a => a.TenantId == tenantId
                && a.LocationId == locationId
                && a.IpAddress == ipAddress
                && !a.IsSuccessful
                && a.AttemptedAt > windowStart)
            .CountAsync();

        if (recentFailures >= MaxFailedAttempts)
        {
            var oldestInWindow = await _context.IntakeVerificationAttempts
                .Where(a => a.TenantId == tenantId
                    && a.LocationId == locationId
                    && a.IpAddress == ipAddress
                    && !a.IsSuccessful
                    && a.AttemptedAt > windowStart)
                .OrderBy(a => a.AttemptedAt)
                .Select(a => a.AttemptedAt)
                .FirstOrDefaultAsync();

            var lockoutEndAt = oldestInWindow.AddMinutes(WindowMinutes);

            _logger.LogWarning(
                "Intake rate limit exceeded: tenant {TenantId}, location {LocationId}, IP {Ip}, failures {Count}",
                tenantId, locationId, ipAddress, recentFailures);

            return new ThrottleResultDto
            {
                Allowed = false,
                LockoutEndAt = lockoutEndAt,
                RecentFailures = recentFailures
            };
        }

        return new ThrottleResultDto { Allowed = true, RecentFailures = recentFailures };
    }

    public async Task LogAttemptAsync(
        int tenantId,
        int locationId,
        int? patientId,
        DateOnly? attemptedDob,
        string attemptedSsnLast4Hash,
        bool success,
        string ipAddress,
        string userAgent)
    {
        var attempt = new IntakeVerificationAttempt
        {
            TenantId = tenantId,
            LocationId = locationId,
            PatientId = patientId,
            AttemptedDob = attemptedDob,
            AttemptedSsnLast4Hash = attemptedSsnLast4Hash,
            IsSuccessful = success,
            IpAddress = ipAddress,
            UserAgent = userAgent?.Length > 500 ? userAgent[..500] : userAgent,
            AttemptedAt = DateTime.UtcNow
        };
        _context.IntakeVerificationAttempts.Add(attempt);
        await _context.SaveChangesAsync();
    }
}
