using System.Security.Claims;

namespace EHR.Helpers;

/// <summary>
/// Who may reach what, written once.
///
/// The sidebar and the [Authorize] attributes were two copies of the same fact
/// and they had already drifted: Billing and Payments carried no role list at
/// all, so every signed in user could open them, while the nav offered the tabs
/// to everybody as if that were intended. Both halves now name these constants,
/// so a role added to one cannot go missing from the other.
///
/// The NUMBERS, not the names. The role claim carries the enum value:
/// 0 Super Admin, 1 Admin, 2 Intake, 3 Delivery, 4 Biller.
/// </summary>
public static class DmeRoles
{
    /// <summary>Medocs, and the supplier's owner.</summary>
    public const string Admin = "0,1";

    /// <summary>
    /// Claims, payments and denials.
    ///
    /// Intake and Delivery are deliberately absent, and that is the oldest
    /// control in this trade: the person who hands the equipment over must not
    /// also be the person who records what was paid for it, or a delivery that
    /// never happened and a payment that never arrived can be made to agree.
    /// </summary>
    public const string Money = "0,1,4";

    /// <summary>
    /// Can this person see money. Used by the sidebar so a tab is never offered
    /// to somebody the server will answer with a 403.
    /// </summary>
    public static bool CanSeeMoney(this ClaimsPrincipal user) => Holds(user, Money);

    /// <summary>Can this person administer the supplier.</summary>
    public static bool CanAdminister(this ClaimsPrincipal user) => Holds(user, Admin);

    private static bool Holds(ClaimsPrincipal user, string roles) =>
        roles.Split(',').Any(user.IsInRole);
}
