using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EHR.Controllers;

/// <summary>
/// Serves the tablet-facing intake view pages (HTML shells).
/// Mirrors the KioskViewController pattern.
///
/// Why: the tablet wizard is not logged in and must live outside portal auth.
/// The actual token validation + DOB/SSN verify happen via JS calls to IntakeController
/// (the API). These actions just return the page shells.
///
/// Routes:
///   GET /intake/p/{token}                       -> verify entry page (anonymous)
///   GET /intake/p/{token}/wizard                -> tablet wizard page (verify cookie)
///   GET /clinic/patients/{id}/intake-frame      -> staff iframe host (Step 1)
/// </summary>
public class IntakeViewController : Controller
{
    /// <summary>
    /// Tablet verify entry page. Renders the minimal shell; JS validates token
    /// against /api/intake/p/{token}, collects DOB + SSN last-4, submits to
    /// /api/intake/p/{token}/verify, then navigates to /intake/p/{token}/wizard.
    /// </summary>
    [HttpGet("/intake/p/{token}")]
    [AllowAnonymous]
    public IActionResult TabletEntry(string token)
    {
        ViewBag.Token = token ?? "";
        return View("~/Views/Intake/TabletEntry.cshtml");
    }

    /// <summary>
    /// Tablet wizard page. Requires a valid verify cookie (set server-side on verify).
    /// JS calls /api/intake/p/{token}/progress; if 401, redirects back to the verify page.
    /// </summary>
    [HttpGet("/intake/p/{token}/wizard")]
    [AllowAnonymous]
    public IActionResult TabletWizard(string token)
    {
        ViewBag.Token = token ?? "";
        return View("~/Views/Intake/TabletWizard.cshtml");
    }

    /// <summary>
    /// Clinic staff iframe host for the intake wizard. Embedded inside the encounter
    /// (Clinical Note step for provider, FORMS group for nurse/MA). Returns the
    /// ClinicFrame.cshtml shell which loads the same wizard configured with
    /// /api/clinic/patients/{id}/intake/... endpoints.
    ///
    /// AUTH NOTE: This view is intentionally AllowAnonymous — the iframe is loaded
    /// via browser navigation (`src="..."`) which CANNOT carry the JWT bearer header
    /// the rest of the app uses (auth scheme is JWT-only, no cookie auth).
    /// The HTML shell contains no PHI — just bootstrap CSS, the patientId in a data
    /// attribute, and script tags. Real access control happens on the API endpoints
    /// the iframe calls (/api/clinic/patients/{id}/intake/*), all of which are
    /// [Authorize] + tenant-checked + role-guarded. The iframe JS reads the
    /// authToken from localStorage (same origin, shared with parent) and attaches
    /// it as a Bearer header on every API call.
    ///
    /// This mirrors the TabletWizard pattern: view is anonymous, API enforces.
    /// Spec: rules/technical/intake-on-clinical-note.md §4.1.
    /// </summary>
    [HttpGet("/clinic/patients/{id:int}/intake-frame")]
    [AllowAnonymous]
    public IActionResult ClinicFrame(int id)
    {
        ViewBag.PatientId = id;
        return View("~/Views/Intake/ClinicFrame.cshtml");
    }
}
