using EHR.Models.Generated;
using Microsoft.EntityFrameworkCore;

namespace EHR.Services.Intake;

/// <summary>
/// Generates, rotates, and resolves the opaque Patient.IntakePortalToken GUID used on
/// tablet intake URLs (/intake/p/{token}). Token never contains PatientId;
/// lookup is exclusively via the column. Tokens carry a 30-day expiry —
/// a leaked URL stops working after that window.
/// </summary>
public interface IIntakeAccessTokenService
{
    Task<Guid> GetOrCreateTokenAsync(int patientId);
    Task<Guid> RotateTokenAsync(int patientId);
    Task<int?> ResolvePatientIdAsync(Guid token);
}

public class IntakeAccessTokenService : IIntakeAccessTokenService
{
    private readonly EhrDbContext _context;

    /// <summary>How long a freshly-issued or rotated intake token stays valid.</summary>
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromDays(30);

    public IntakeAccessTokenService(EhrDbContext context)
    {
        _context = context;
    }

    public async Task<Guid> GetOrCreateTokenAsync(int patientId)
    {
        var patient = await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == patientId);
        if (patient == null) throw new InvalidOperationException("Patient not found");

        // Issue a new token if absent, or rotate if explicitly expired or
        // expiring within a day. NULL expiry = legacy row pre-migration —
        // treated as still-valid for backward compat (matches the read-side
        // behavior in ResolvePatientIdAsync); explicit rotation via
        // RotateTokenAsync will give it a real expiry.
        var now = DateTime.UtcNow;
        var needsNew = patient.IntakePortalToken == null
                       || patient.IntakePortalToken == Guid.Empty
                       || (patient.IntakePortalTokenExpiresAt.HasValue
                           && patient.IntakePortalTokenExpiresAt.Value < now.AddDays(1));

        if (needsNew)
        {
            patient.IntakePortalToken = Guid.NewGuid();
            patient.IntakePortalTokenExpiresAt = now.Add(TokenLifetime);
            patient.UpdatedAt = now;
            await _context.SaveChangesAsync();
        }
        return patient.IntakePortalToken!.Value;
    }

    public async Task<Guid> RotateTokenAsync(int patientId)
    {
        var patient = await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == patientId);
        if (patient == null) throw new InvalidOperationException("Patient not found");

        var now = DateTime.UtcNow;
        patient.IntakePortalToken = Guid.NewGuid();
        patient.IntakePortalTokenExpiresAt = now.Add(TokenLifetime);
        patient.UpdatedAt = now;
        await _context.SaveChangesAsync();
        return patient.IntakePortalToken.Value;
    }

    public async Task<int?> ResolvePatientIdAsync(Guid token)
    {
        if (token == Guid.Empty) return null;
        var now = DateTime.UtcNow;

        // Reject expired tokens. NULL expiry = legacy row predating the
        // migration (treated as "still valid" for backward compat until
        // explicitly rotated).
        var id = await _context.Patients
            .Where(p => p.IntakePortalToken == token
                        && p.IsDeleted != true
                        && (p.IntakePortalTokenExpiresAt == null
                            || p.IntakePortalTokenExpiresAt > now))
            .Select(p => (int?)p.PatientId)
            .FirstOrDefaultAsync();
        return id;
    }
}
