using Microsoft.AspNetCore.Mvc;

namespace EHR.Controllers;

/// <summary>
/// Controller for serving main MVC views.
/// Handles navigation between major application pages.
/// </summary>
public class HomeController : Controller
{
    /// <summary>
    /// Main application entry point - redirects to Dashboard.
    /// Login is handled client-side via JavaScript.
    /// </summary>
    public IActionResult Index()
    {
        return RedirectToAction("Dashboard", "Dme");
    }

    /// <summary>
    /// Dashboard page view.
    /// </summary>
    public IActionResult Dashboard()
    {
        ViewData["Title"] = "Dashboard";
        return View("~/Views/Dashboard/Index.cshtml");
    }

    /// <summary>
    /// Schedule/Calendar page view.
    /// </summary>
    public IActionResult Schedule()
    {
        ViewData["Title"] = "Schedule";
        return View("~/Views/Schedule/Index.cshtml");
    }

    /// <summary>
    /// Patients list page view.
    /// </summary>
    public IActionResult Patients()
    {
        ViewData["Title"] = "Patients";
        return View("~/Views/Patients/Index.cshtml");
    }

    /// <summary>
    /// Providers list page view.
    /// </summary>
    public IActionResult Providers()
    {
        ViewData["Title"] = "Providers";
        return View("~/Views/Providers/Index.cshtml");
    }

    /// <summary>
    /// Clinical notes page view.
    /// </summary>
    public IActionResult Notes()
    {
        ViewData["Title"] = "Clinical Notes";
        return View("~/Views/Notes/Index.cshtml");
    }

    /// <summary>
    /// E-Prescribing page view.
    /// </summary>
    public IActionResult Prescriptions()
    {
        ViewData["Title"] = "E-Prescribe";
        return View("~/Views/Prescriptions/Index.cshtml");
    }

    /// <summary>
    /// Orders page view (Labs, Imaging, Referrals).
    /// </summary>
    public IActionResult Orders()
    {
        ViewData["Title"] = "Orders";
        return View("~/Views/Orders/Index.cshtml");
    }

    /// <summary>
    /// Encounter Workspace page view.
    /// Opens the guided visit workflow for a specific encounter.
    /// </summary>
    public IActionResult Encounter(int? id)
    {
        if (id == null) return RedirectToAction("Dashboard");
        ViewData["Title"] = "Encounter Workspace";
        ViewData["ActivePage"] = "encounter";
        ViewData["EncounterId"] = id;
        return View("~/Views/Encounters/Index.cshtml");
    }

    /// <summary>
    /// Billing page view.
    /// </summary>
    public IActionResult Billing()
    {
        ViewData["Title"] = "Billing";
        return View("~/Views/Billing/Index.cshtml");
    }

    /// <summary>
    /// Unavailability/Time Off page view.
    /// </summary>
    public IActionResult Unavailability()
    {
        ViewData["Title"] = "Time Off";
        return View("~/Views/Unavailability/Index.cshtml");
    }

    /// <summary>
    /// Reports page view.
    /// </summary>
    public IActionResult Reports()
    {
        ViewData["Title"] = "Reports";
        return View("~/Views/Reports/Index.cshtml");
    }

    /// <summary>
    /// Note Templates page view.
    /// </summary>
    public IActionResult Templates()
    {
        ViewData["Title"] = "Note Templates";
        return View("~/Views/Templates/Index.cshtml");
    }

    /// <summary>
    /// Medical Lien Templates page view (Super Admin only).
    /// </summary>
    public IActionResult MedicalLienTemplates()
    {
        ViewData["Title"] = "Medical Lien Templates";
        return View("~/Views/Templates/MedicalLienTemplates.cshtml");
    }

    /// <summary>
    /// Users management page view.
    /// </summary>
    public IActionResult Users()
    {
        ViewData["Title"] = "User Management";
        return View("~/Views/Users/Index.cshtml");
    }

    /// <summary>
    /// Clinics/Tenants management page view (Super Admin only).
    /// </summary>
    public IActionResult Tenants()
    {
        ViewData["Title"] = "Clinics";
        return View("~/Views/Tenants/Index.cshtml");
    }

    /// <summary>
    /// Organization settings page view.
    /// </summary>
    public IActionResult Organization()
    {
        ViewData["Title"] = "Organization";
        ViewData["ActivePage"] = "organization";
        return View("~/Views/Organization/Index.cshtml");
    }

    /// <summary>
    /// Settings page view.
    /// </summary>
    public IActionResult Settings()
    {
        ViewData["Title"] = "Settings";
        return View("~/Views/Settings/Index.cshtml");
    }

    /// <summary>
    /// Payment Integration page (Stripe Connect onboarding for ClinicAdmin).
    /// Accessible at both /Home/PaymentIntegration (default conventional route)
    /// and /Settings/PaymentIntegration (attribute route) — the latter is used
    /// as the Stripe Connect onboarding return URL in appsettings.json.
    /// </summary>
    [Route("Settings/PaymentIntegration")]
    public IActionResult PaymentIntegration()
    {
        ViewData["Title"] = "Payment Integration";
        ViewData["ActivePage"] = "settings";
        return View("~/Views/Settings/PaymentIntegration.cshtml");
    }

    /// <summary>
    /// Consent forms page view.
    /// </summary>
    public IActionResult ConsentForms()
    {
        ViewData["Title"] = "Consent Forms";
        return View("~/Views/ConsentForms/Index.cshtml");
    }

    /// <summary>
    /// Documentation page view.
    /// </summary>
    public IActionResult Documentation()
    {
        ViewData["Title"] = "Documentation";
        ViewData["ActivePage"] = "documentation";
        return View("~/Views/Help/Index.cshtml");
    }

    /// <summary>
    /// Reset password landing page (linked from password reset email).
    /// Standalone page, accepts ?token=... in the query string.
    /// Token is validated client-side via /api/auth/validate-reset-token.
    /// </summary>
    [Route("reset-password")]
    public IActionResult ResetPassword()
    {
        ViewData["Title"] = "Reset Password";
        return View("~/Views/Auth/ResetPassword.cshtml");
    }

    /// <summary>
    /// Error page view.
    /// </summary>
    public IActionResult Error()
    {
        return View();
    }
}
