using EHR.Helpers;

namespace EHR.Services;

/// <summary>One referring doctor. Retirement is derived from the date, never stored twice.</summary>
public record Doctor(
    int DoctorId, string FirstName, string LastName,
    string? Npi, string? Specialty, string? Phone, bool IsRetired)
{
    public string DisplayName => $"Dr. {FirstName} {LastName}".Trim();
}

public interface IDmeDoctors
{
    /// <summary>Doctors in service. Retired ones only on request.</summary>
    IReadOnlyList<Doctor> All(bool includeRetired = false);

    /// <summary>
    /// One doctor by id, retired or not: an order placed before the referral
    /// relationship ended still has to say who prescribed it.
    /// </summary>
    Doctor? Find(int doctorId);

    /// <summary>Returns the new id, or null when the name is missing, the NPI is
    /// malformed, or that NPI already belongs to another doctor here.</summary>
    int? Add(string? firstName, string? lastName, string? npi, string? specialty, string? phone);

    bool Update(int doctorId, string? firstName, string? lastName, string? npi, string? specialty, string? phone);

    bool Retire(int doctorId);

    /// <summary>Undo a retirement. Retiring is one click and needs a way back.</summary>
    bool Restore(int doctorId);

    /// <summary>
    /// Why the last Add or Update was refused, for the screen to show. Null when
    /// nothing has been refused.
    /// </summary>
    string? LastProblem { get; }
}

/// <summary>
/// The referring doctors a supplier takes orders from.
///
/// This table was seeded with three rows and had no INSERT anywhere in the
/// product, so a supplier could not record a fourth. That is not a cosmetic
/// gap: the ordering physician's name and NPI go in boxes 17 and 17b of the
/// CMS-1500, and DMEPOS claims without them are rejected.
///
/// Shaped exactly like DmeDistributors on purpose. The two do the same job for
/// different people, and a reader who knows one should not have to learn the
/// other.
/// </summary>
public sealed class DmeDoctors : IDmeDoctors
{
    private readonly IDmeDb _db;

    public DmeDoctors(IDmeDb db) => _db = db;

    /// <inheritdoc />
    public string? LastProblem { get; private set; }

    private const string Columns =
        "DoctorId, FirstName, LastName, Npi, Specialty, Phone, RetiredAt";

    /// <inheritdoc />
    public IReadOnlyList<Doctor> All(bool includeRetired = false)
    {
        var rows = _db.Query(
            $"SELECT {Columns} FROM dbo.DmeDoctors " +
            (includeRetired ? "" : "WHERE RetiredAt IS NULL ") +
            "ORDER BY CASE WHEN RetiredAt IS NULL THEN 0 ELSE 1 END, LastName, FirstName");

        return rows.Select(Map).ToList();
    }

    /// <inheritdoc />
    public Doctor? Find(int doctorId)
    {
        if (doctorId <= 0) return null;

        // No RetiredAt filter, deliberately: an order placed before the referral
        // ended still has to name who prescribed it.
        var r = _db.QueryOne(
            $"SELECT {Columns} FROM dbo.DmeDoctors WHERE DoctorId=@doctorId", new { doctorId });

        return r == null ? null : Map(r);
    }

    /// <inheritdoc />
    public int? Add(string? firstName, string? lastName, string? npi, string? specialty, string? phone)
    {
        LastProblem = null;

        var first = (firstName ?? "").Trim();
        var last = (lastName ?? "").Trim();

        if (first.Length == 0 || last.Length == 0)
        {
            LastProblem = "A doctor needs a first and last name.";
            return null;
        }

        var cleanNpi = CleanNpi(npi);
        if (cleanNpi == "" && !string.IsNullOrWhiteSpace(npi))
        {
            LastProblem = "An NPI is ten digits.";
            return null;
        }

        if (cleanNpi.Length > 0 && NpiTaken(cleanNpi, exceptDoctorId: 0))
        {
            LastProblem = $"NPI {cleanNpi} already belongs to another doctor on your list.";
            return null;
        }

        return Convert.ToInt32(_db.Scalar(@"
            INSERT INTO dbo.DmeDoctors (TenantId, FirstName, LastName, Npi, Specialty, Phone)
            OUTPUT inserted.DoctorId
            VALUES (@TenantId, @first, @last, @npi, @specialty, @phone)",
            new
            {
                first,
                last,
                npi = Blank(cleanNpi),
                specialty = Blank(specialty),
                phone = Blank(phone)
            }));
    }

    /// <inheritdoc />
    public bool Update(int doctorId, string? firstName, string? lastName, string? npi, string? specialty, string? phone)
    {
        LastProblem = null;
        if (doctorId <= 0) return false;

        var first = (firstName ?? "").Trim();
        var last = (lastName ?? "").Trim();

        if (first.Length == 0 || last.Length == 0)
        {
            LastProblem = "A doctor needs a first and last name.";
            return false;
        }

        var cleanNpi = CleanNpi(npi);
        if (cleanNpi == "" && !string.IsNullOrWhiteSpace(npi))
        {
            LastProblem = "An NPI is ten digits.";
            return false;
        }

        if (cleanNpi.Length > 0 && NpiTaken(cleanNpi, exceptDoctorId: doctorId))
        {
            LastProblem = $"NPI {cleanNpi} already belongs to another doctor on your list.";
            return false;
        }

        // Retired doctors stay editable: a misspelled name still shows on every
        // old order that names them, and on the claim that was billed from it.
        return _db.Execute(@"
            UPDATE dbo.DmeDoctors
               SET FirstName=@first, LastName=@last, Npi=@npi, Specialty=@specialty, Phone=@phone
             WHERE DoctorId=@doctorId AND TenantId=@TenantId",
            new
            {
                doctorId,
                first,
                last,
                npi = Blank(cleanNpi),
                specialty = Blank(specialty),
                phone = Blank(phone)
            }) == 1;
    }

    /// <inheritdoc />
    public bool Retire(int doctorId)
    {
        if (doctorId <= 0) return false;

        // Guarded on RetiredAt IS NULL so two clicks retire once and the date
        // stays the first one.
        return _db.Execute(
            "UPDATE dbo.DmeDoctors SET RetiredAt = SYSUTCDATETIME() " +
            "WHERE DoctorId=@doctorId AND TenantId=@TenantId AND RetiredAt IS NULL",
            new { doctorId }) == 1;
    }

    /// <inheritdoc />
    public bool Restore(int doctorId)
    {
        if (doctorId <= 0) return false;

        return _db.Execute(
            "UPDATE dbo.DmeDoctors SET RetiredAt = NULL " +
            "WHERE DoctorId=@doctorId AND TenantId=@TenantId AND RetiredAt IS NOT NULL",
            new { doctorId }) == 1;
    }

    /// <summary>
    /// An NPI reduced to its digits, or "" when it is not ten of them.
    ///
    /// Punctuation is stripped rather than refused, because an NPI copied off a
    /// referral routinely arrives as "1093-847-562" or with a stray space.
    ///
    /// The ten digit CHECKSUM is deliberately not enforced. A real NPI carries a
    /// Luhn check over "80840" plus the first nine digits, and validating it
    /// would catch a mistyped one. It is left out because every NPI in the demo
    /// and seed data is a made-up number that fails it, so turning it on would
    /// reject the product's own sample data. Worth switching on the day the
    /// seeds carry real check digits.
    /// </summary>
    private static string CleanNpi(string? npi)
    {
        if (string.IsNullOrWhiteSpace(npi)) return "";

        var digits = new string(npi.Where(char.IsDigit).ToArray());
        return digits.Length == 10 ? digits : "";
    }

    /// <summary>
    /// Checked here as well as by the unique index, so the operator gets a
    /// sentence rather than a constraint violation. Retired doctors do NOT
    /// count: re-adding somebody you have started working with again is normal,
    /// and the index is filtered the same way.
    /// </summary>
    private bool NpiTaken(string npi, int exceptDoctorId) =>
        _db.Scalar(
            "SELECT DoctorId FROM dbo.DmeDoctors " +
            "WHERE TenantId=@TenantId AND Npi=@npi AND RetiredAt IS NULL AND DoctorId<>@exceptDoctorId",
            new { npi, exceptDoctorId }) != null;

    private static object Blank(string? v) =>
        string.IsNullOrWhiteSpace(v) ? DBNull.Value : v.Trim();

    private static Doctor Map(Dictionary<string, object?> r) => new(
        F.I(r["DoctorId"]),
        F.S(r["FirstName"]),
        F.S(r["LastName"]),
        r["Npi"] is null or DBNull ? null : F.S(r["Npi"]),
        r["Specialty"] is null or DBNull ? null : F.S(r["Specialty"]),
        r["Phone"] is null or DBNull ? null : F.S(r["Phone"]),
        r["RetiredAt"] is not (null or DBNull));
}
