using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EHR.Helpers;

namespace EHR.Controllers;

/// <summary>
/// Serves the small set of non-DME pages the product still has: the sign-in
/// landing page, staff user management, the Super Admin clinic console, and the
/// password-reset page.
///
/// WHY IT IS THIS SHORT
/// It had 23 actions serving the clinical EHR this product was copied from:
/// patients, encounters, notes, prescriptions, schedule, reports, telehealth and
/// the rest. MEDOCS DME is a standalone DME application. None of those pages
/// were reachable from its navigation, none of them were part of the product,
/// and every one of them was a page that could render patient data. They and
/// their views are gone (2026-08-25).
///
/// SECURITY
/// [Authorize] at the class level. Three actions are deliberately anonymous and
/// marked individually: Index (the sign-in landing page, where the 401 handler
/// sends people), ResetPassword (reached from an emailed link, before any
/// session exists) and Error. Tenants is Super Admin only.
/// </summary>
[Authorize]
public class HomeController : Controller
{
    /// <summary>
    /// Application entry point and the sign-in landing page.
    ///
    /// Authenticated callers go straight to the DME dashboard. Everyone else
    /// gets the Login view, which renders the shared layout so the existing
    /// client-side login card is shown. Deliberately anonymous: this is where
    /// the 401 handler sends unauthenticated page navigations, so requiring auth
    /// here would produce a redirect loop.
    /// </summary>
    [AllowAnonymous]
    public IActionResult Index(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Dashboard", "Dme");

        // Only ever bounce back to a local path. An absolute URL here would let
        // a crafted link turn the login page into an open redirect.
        ViewBag.ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        return View("Login");
    }

    /// <summary>
    /// Staff user management. Linked from the navigation as /UserManagement.
    ///
    /// The guard used to live only on UsersController, and this comment used to
    /// say that was enough. It was not: an Intake user who typed the URL got the
    /// page, the toolbar and the Add User button, and an empty table where the
    /// API had refused them. Being shown an administration screen and left to
    /// work out from a blank grid that you are not an administrator is not a
    /// permission model. The page now answers the same way its data does.
    /// </summary>
    [Authorize(Roles = DmeRoles.Admin)]
    public IActionResult Users()
    {
        ViewData["Title"] = "User Management";
        return View("~/Views/Users/Index.cshtml");
    }

    /// <summary>
    /// Super Admin clinic console: creates and manages tenants. Role 0 only,
    /// matching the POST /api/tenants endpoint behind it. Until 2026-08-25 this
    /// page had no authorization at all and rendered to anyone who typed the URL.
    /// </summary>
    [Authorize(Roles = "0")]
    public IActionResult Tenants()
    {
        ViewData["Title"] = "Clinics";
        return View("~/Views/Tenants/Index.cshtml");
    }

    /// <summary>
    /// Reset password landing page, linked from the password reset email.
    /// Anonymous by necessity: the user has no session at this point. The token
    /// in the query string is validated by /api/auth/validate-reset-token.
    /// </summary>
    [AllowAnonymous]
    [Route("reset-password")]
    public IActionResult ResetPassword()
    {
        ViewData["Title"] = "Reset Password";
        return View("~/Views/Auth/ResetPassword.cshtml");
    }

    /// <summary>Error page. Anonymous so a failure before sign-in can still render.</summary>
    [AllowAnonymous]
    public IActionResult Error()
    {
        return View();
    }
}
