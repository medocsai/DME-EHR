using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// dbo.DmeDoctors was seeded with three rows and had no INSERT anywhere in the
/// product, so a supplier taking an order from a fourth doctor could not record
/// them. The ordering physician is not optional on DMEPOS: their name and NPI
/// go in boxes 17 and 17b of the CMS-1500, and a claim missing them is rejected.
/// </summary>
public class DmeDoctorsTests
{
    /// <summary>
    /// An NPI identifies a person nationally, so two doctors sharing one is a
    /// duplicate, and picking the wrong one puts the wrong prescriber on a
    /// claim. Checked in the service as well as by the index, so the operator
    /// gets a sentence rather than a constraint violation.
    /// </summary>
    [Fact]
    public void OneNpiBelongsToOneDoctor()
    {
        var svc = Service();

        Assert.Contains("already belongs to another doctor", svc);
        Assert.Contains("UX_DmeDoctors_Npi", Migration());
    }

    /// <summary>
    /// Retired doctors do not hold an NPI hostage: taking up with a prescriber
    /// again is normal, and the unique index is filtered the same way the check
    /// is.
    /// </summary>
    [Fact]
    public void ARetiredDoctorDoesNotBlockReAddingTheSameNpi()
    {
        Assert.Contains("RetiredAt IS NULL AND DoctorId<>@exceptDoctorId", Service());
        Assert.Contains("WHERE Npi IS NOT NULL AND RetiredAt IS NULL", Migration());
    }

    /// <summary>
    /// An NPI copied off a referral arrives punctuated. Stripping is friendlier
    /// than refusing, but ten digits is the shape, and anything else is a typo
    /// worth reporting rather than storing.
    /// </summary>
    [Fact]
    public void AnNpiIsTenDigitsPunctuationStripped()
    {
        var svc = Service();

        Assert.Contains("digits.Length == 10 ? digits : \"\"", svc);
        Assert.Contains("An NPI is ten digits.", svc);
    }

    /// <summary>
    /// Retired, never deleted: past orders and the claims billed from them name
    /// this prescriber, and that has to survive the referral ending. Guarded so
    /// a double click retires once.
    /// </summary>
    [Fact]
    public void DoctorsAreRetiredAndCanComeBack()
    {
        var svc = Service();

        Assert.Contains("RetiredAt = SYSUTCDATETIME()", svc);
        Assert.Contains("AND RetiredAt IS NULL", svc);
        Assert.Contains("RetiredAt = NULL", svc);
        Assert.Contains("AND RetiredAt IS NOT NULL", svc);
        Assert.DoesNotContain("DELETE FROM dbo.DmeDoctors", svc);
    }

    /// <summary>
    /// Find deliberately does not filter retired doctors: an order placed before
    /// the referral ended still has to say who prescribed it.
    /// </summary>
    [Fact]
    public void AnOldOrderCanStillNameARetiredDoctor()
    {
        var svc = Service();
        var find = svc[svc.IndexOf("public Doctor? Find(", StringComparison.Ordinal)..];
        find = find[..find.IndexOf("\n    }", StringComparison.Ordinal)];

        // It SELECTS RetiredAt, because IsRetired is mapped from it. What it must
        // not do is filter on it.
        Assert.Contains("WHERE DoctorId=@doctorId", find);
        Assert.DoesNotContain("RetiredAt IS NULL", find);
    }

    /// <summary>
    /// The New Order picker and the Doctors screen read the same service, so
    /// they cannot disagree about who is available to prescribe.
    /// </summary>
    [Fact]
    public void TheOrderPickerOffersDoctorsInServiceThroughTheSameService()
    {
        var controller = File.ReadAllText(Path.Combine(
            ProductionRoot(), "Controllers", "DmeController.cs"));

        Assert.Contains("ViewBag.Doctors = _doctors.All();", controller);
        Assert.DoesNotContain("SELECT * FROM dbo.DmeDoctors", controller);
    }

    [Fact]
    public void EveryDoctorWriteIsTenantScopedAndAudited()
    {
        var svc = Service();
        var controller = File.ReadAllText(Path.Combine(
            ProductionRoot(), "Controllers", "DmeController.cs"));

        Assert.Contains("TenantId=@TenantId", svc);
        foreach (var action in new[] { "DME_DOCTOR_ADDED", "DME_DOCTOR_EDITED",
                                       "DME_DOCTOR_RETIRED", "DME_DOCTOR_RESTORED" })
            Assert.Contains(action, controller);
    }

    private static string Service() => File.ReadAllText(Path.Combine(
        ProductionRoot(), "Services", "DmeDoctors.cs"));

    private static string Migration() => File.ReadAllText(Path.Combine(
        ProductionRoot(), "Migrations", "Manual", "2026-08-31_DME_Doctors.sql"));

    private static string ProductionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && dir.Name != "ehr-system") dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException(
            "Could not locate ehr-system root from " + AppContext.BaseDirectory);
    }
}

/// <summary>
/// The role list came across whole from the clinical product. A DME supplier has
/// no clinicians and no nurses; it has somebody who takes the order, somebody
/// who delivers, somebody who bills, and an owner.
/// </summary>
public class DmeRoleTests
{
    /// <summary>
    /// Twenty eight [Authorize] attributes name these numbers and twenty eight
    /// live accounts carry them. Renumbering to tidy the list would silently
    /// change what people can do, so only the words changed.
    /// </summary>
    [Theory]
    [InlineData("SuperAdmin = 0")]
    [InlineData("Admin = 1")]
    [InlineData("Intake = 2")]
    [InlineData("Delivery = 3")]
    [InlineData("Biller = 4")]
    public void TheFiveRolesKeepTheirNumbers(string declaration)
    {
        Assert.Contains(declaration, Read("Models", "Enums", "AllEnums.cs"));
    }

    [Theory]
    [InlineData("Clinician")]
    [InlineData("FrontDesk")]
    [InlineData("ReadOnly")]
    [InlineData("MedicalAssistant")]
    [InlineData("Nurse")]
    public void TheClinicalRolesAreGone(string leftover)
    {
        // The DECLARATION, not the word. The comment above the enum names them
        // to explain what was removed and why, which is worth keeping.
        Assert.DoesNotContain($"{leftover} =", Read("Models", "Enums", "AllEnums.cs"));
    }

    /// <summary>
    /// The old fall-through named every unknown value "Read Only". Roles 6 and 7
    /// were Medical Assistant and Nurse and were never in the list, so thirteen
    /// accounts displayed as a role they did not hold: the screen answering a
    /// question about permissions with a guess.
    /// </summary>
    [Fact]
    public void AnUnknownRoleSaysSoRatherThanNamingARealOne()
    {
        var dtos = Read("Models", "DTOs", "AllDtos.cs");

        Assert.Contains("_ => \"Unknown\"", dtos);
        Assert.DoesNotContain("_ => \"Read Only\"", dtos);
    }

    /// <summary>
    /// Super Admin belongs to Medocs and is not something a supplier hands out,
    /// so it is not on the picker a tenant creates users from.
    /// </summary>
    [Fact]
    public void TheUserFormOffersTheFourTenantRolesOnly()
    {
        var modal = Read("Views", "Shared", "_ModalsAdmin.cshtml");

        foreach (var role in new[] { "Admin", "Intake", "Delivery", "Biller" })
            Assert.Contains($">{role}</option>", modal);

        Assert.DoesNotContain("value=\"0\">Super Admin", modal);
        Assert.DoesNotContain(">Nurse</option>", modal);
    }

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && dir.Name != "ehr-system") dir = dir.Parent;
        var root = dir?.FullName ?? throw new InvalidOperationException(
            "Could not locate ehr-system root from " + AppContext.BaseDirectory);

        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }
}
