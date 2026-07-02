using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace EHR.Controllers;

/// <summary>
/// Controller for serving the patient kiosk view.
/// This is separate from KioskController (API) which handles the data operations.
/// </summary>
[AllowAnonymous]
public class KioskViewController : Controller
{
    /// <summary>
    /// Patient kiosk check-in page.
    /// Accessible via /Kiosk or /Kiosk/{token}
    /// </summary>
    /// <param name="token">Optional kiosk token from URL path</param>
    [Route("Kiosk")]
    [Route("Kiosk/{token}")]
    public IActionResult Index(string token = null)
    {
        ViewBag.Token = token ?? "";
        return View("~/Views/Kiosk/Index.cshtml");
    }
}
