using EHR.Helpers;
using EHR.Models.Generated;
using Microsoft.EntityFrameworkCore;

namespace EHR.Services.Intake;

/// <summary>
/// Pure function. Checks LastName + DOB + ZipCode against a specific patient row.
/// Used by the tablet verify flow AFTER IntakeAccessTokenService.ResolvePatientIdAsync
/// has produced the candidate patient from the opaque token.
///
/// (2026-05) Switched from SSN-last4 to LastName + ZipCode for identity verification.
/// LastName and ZipCode are encrypted on Patient, so the row is loaded and the
/// values are decrypted + compared in memory.
/// </summary>
public interface IPatientIdentityMatcher
{
    Task<bool> MatchesAsync(int patientId, string lastName, DateOnly dob, string zipCode);
}

public class PatientIdentityMatcher : IPatientIdentityMatcher
{
    private readonly EhrDbContext _context;
    private readonly EncryptionHelper _encryption;

    public PatientIdentityMatcher(EhrDbContext context, EncryptionHelper encryption)
    {
        _context = context;
        _encryption = encryption;
    }

    public async Task<bool> MatchesAsync(int patientId, string lastName, DateOnly dob, string zipCode)
    {
        if (string.IsNullOrWhiteSpace(lastName) || string.IsNullOrWhiteSpace(zipCode))
            return false;

        var submittedLast = lastName.Trim();
        var submittedZip = System.Text.RegularExpressions.Regex.Replace(zipCode, @"[^\d]", "");
        if (submittedZip.Length < 5) return false;
        if (submittedZip.Length > 5) submittedZip = submittedZip[..5];

        var patient = await _context.Patients
            .Where(p => p.PatientId == patientId
                && p.IsDeleted != true
                && p.DateOfBirth == dob)
            .Select(p => new { p.LastName, p.ZipCode })
            .FirstOrDefaultAsync();

        if (patient == null) return false;

        var candLast = (_encryption.Decrypt(patient.LastName) ?? "").Trim();
        var candZip = System.Text.RegularExpressions.Regex.Replace(_encryption.Decrypt(patient.ZipCode) ?? "", @"[^\d]", "");
        if (candZip.Length > 5) candZip = candZip[..5];

        return string.Equals(candLast, submittedLast, StringComparison.OrdinalIgnoreCase)
            && candZip == submittedZip;
    }
}
