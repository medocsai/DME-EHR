using System;
using System.Collections.Generic;
using System.Linq;
using EHR.Helpers;

namespace EHR.Services;

/// <summary>One referring or ordering physician.</summary>
/// <param name="DoctorId">Key.</param>
/// <param name="FirstName">Given name.</param>
/// <param name="LastName">Family name.</param>
/// <param name="Npi">Their national provider identifier, CMS-1500 box 17b.</param>
/// <param name="Specialty">Free text, for the picker.</param>
/// <param name="Phone">For chasing a missing order or a recertification.</param>
/// <param name="IsRetired">Derived from RetiredAt, never stored.</param>
public record Doctor(
    int DoctorId, string FirstName, string LastName, string? Npi,
    string? Specialty, string? Phone, bool IsRetired)
{
    /// <summary>How a picker shows them.</summary>
    public string DisplayName => ("Dr. " + FirstName + " " + LastName).Replace("  ", " ").Trim();
}

/// <summary>The outcome of adding or editing, with a sentence a person can act on.</summary>
public record DoctorResult(bool Success, string? Error = null, int DoctorId = 0);

/// <summary>
/// The referring physicians this supplier takes orders from.
///
/// WHY THIS EXISTS
/// dbo.DmeDoctors held three seeded rows and the product had no way to add a
/// fourth. Every DMEPOS claim needs an ordering physician in CMS-1500 boxes 17
/// and 17b, and referrals arrive from whichever doctor the patient happened to
/// see. A supplier who cannot record a new one has two choices, and both are
/// bad: raise the order under whichever of the three is closest, which is a
/// false claim, or not raise it at all.
/// </summary>
public interface IDmeDoctors
{
    /// <summary>Live doctors, retired ones last when asked for.</summary>
    IReadOnlyList<Doctor> All(bool includeRetired = false);

    /// <summary>
    /// One doctor by id, retired or not. An order signed before they stopped
    /// referring still has to say who signed it.
    /// </summary>
    Doctor? Find(int doctorId);

    /// <summary>Adds one. Refuses a duplicate NPI or an NPI that cannot be real.</summary>
    DoctorResult Add(string? firstName, string? lastName, string? npi, string? specialty, string? phone);

    /// <summary>Edits one. A typo in an NPI rejects every claim it appears on.</summary>
    DoctorResult Update(int doctorId, string? firstName, string? lastName, string? npi, string? specialty, string? phone);

    /// <summary>
    /// Takes one out of the picker. NEVER deletes: every order they signed is
    /// the record of who signed it.
    /// </summary>
    bool Retire(int doctorId);
}

/// <inheritdoc cref="IDmeDoctors"/>
public sealed class DmeDoctors : IDmeDoctors
{
    private readonly IDmeDb _db;

    public DmeDoctors(IDmeDb db) => _db = db;

    /// <inheritdoc />
    public IReadOnlyList<Doctor> All(bool includeRetired = false)
    {
        var rows = _db.Query(
            "SELECT DoctorId, FirstName, LastName, Npi, Specialty, Phone, RetiredAt FROM dbo.DmeDoctors " +
            (includeRetired ? "" : "WHERE RetiredAt IS NULL ") +
            "ORDER BY CASE WHEN RetiredAt IS NULL THEN 0 ELSE 1 END, LastName, FirstName");

        return rows.Select(Map).ToList();
    }

    /// <inheritdoc />
    public Doctor? Find(int doctorId)
    {
        if (doctorId <= 0) return null;

        // No RetiredAt filter, deliberately. An order signed before they stopped
        // referring still has to name them.
        var r = _db.QueryOne(
            "SELECT DoctorId, FirstName, LastName, Npi, Specialty, Phone, RetiredAt FROM dbo.DmeDoctors WHERE DoctorId=@doctorId",
            new { doctorId });

        return r == null ? null : Map(r);
    }

    /// <inheritdoc />
    public DoctorResult Add(string? firstName, string? lastName, string? npi, string? specialty, string? phone)
    {
        var check = Validate(firstName, lastName, ref npi, doctorId: 0);
        if (check != null) return new DoctorResult(false, check);

        var id = Convert.ToInt32(_db.Scalar(@"
            INSERT INTO dbo.DmeDoctors (TenantId, FirstName, LastName, Npi, Specialty, Phone)
            OUTPUT inserted.DoctorId
            VALUES (@TenantId, @fn, @ln, @npi, @specialty, @phone)",
            new
            {
                fn = firstName!.Trim(), ln = lastName!.Trim(),
                npi = Blank(npi), specialty = Blank(specialty), phone = Blank(phone)
            }));

        return new DoctorResult(true, DoctorId: id);
    }

    /// <inheritdoc />
    public DoctorResult Update(int doctorId, string? firstName, string? lastName, string? npi, string? specialty, string? phone)
    {
        if (Find(doctorId) == null) return new DoctorResult(false, "That doctor is not on this supplier's list.");

        var check = Validate(firstName, lastName, ref npi, doctorId);
        if (check != null) return new DoctorResult(false, check);

        _db.Execute(@"
            UPDATE dbo.DmeDoctors
               SET FirstName=@fn, LastName=@ln, Npi=@npi, Specialty=@specialty, Phone=@phone
             WHERE DoctorId=@doctorId AND TenantId=@TenantId",
            new
            {
                doctorId,
                fn = firstName!.Trim(), ln = lastName!.Trim(),
                npi = Blank(npi), specialty = Blank(specialty), phone = Blank(phone)
            });

        return new DoctorResult(true, DoctorId: doctorId);
    }

    /// <inheritdoc />
    public bool Retire(int doctorId)
    {
        if (doctorId <= 0) return false;

        // Guarded on RetiredAt IS NULL so two people clicking Retire produce one
        // retirement and the date stays the first one. Same shape as voiding a
        // payment and retiring a distributor.
        return _db.Execute(
            "UPDATE dbo.DmeDoctors SET RetiredAt = SYSUTCDATETIME() " +
            "WHERE DoctorId=@doctorId AND TenantId=@TenantId AND RetiredAt IS NULL",
            new { doctorId }) == 1;
    }

    /// <summary>
    /// Every rule, in one place, so add and edit cannot drift apart.
    /// Returns null when the doctor is acceptable, or the sentence to show.
    /// </summary>
    private string? Validate(string? firstName, string? lastName, ref string? npi, int doctorId)
    {
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            return "A doctor needs a first and last name.";

        // Punctuation and spaces are how people write an NPI down; the stored
        // form is ten digits.
        npi = Npi.Normalise(npi);

        if (npi != null)
        {
            if (npi.Length != 10)
                return "An NPI is exactly ten digits.";

            if (!Npi.IsPossible(npi))
                return "That NPI cannot be right: its check digit does not match. "
                     + "Compare it with the referral, digit by digit.";

            // Checked here as well as by the unique index, so the operator gets
            // a sentence rather than a constraint violation. Retired doctors
            // count: the same physician re-entered is still the same physician.
            var clash = _db.Scalar(
                "SELECT DoctorId FROM dbo.DmeDoctors WHERE TenantId=@TenantId AND Npi=@npi AND DoctorId<>@doctorId",
                new { npi, doctorId });

            if (clash != null)
                return "That NPI is already on the list. A doctor is entered once, however many customers they refer.";
        }

        return null;
    }

    private static object Blank(string? v)
        => string.IsNullOrWhiteSpace(v) ? DBNull.Value : v.Trim();

    private static Doctor Map(Dictionary<string, object?> r) => new(
        F.I(r["DoctorId"]),
        F.S(r["FirstName"]),
        F.S(r["LastName"]),
        r["Npi"] is null or DBNull ? null : F.S(r["Npi"]),
        r["Specialty"] is null or DBNull ? null : F.S(r["Specialty"]),
        r["Phone"] is null or DBNull ? null : F.S(r["Phone"]),
        r["RetiredAt"] is not (null or DBNull));
}
