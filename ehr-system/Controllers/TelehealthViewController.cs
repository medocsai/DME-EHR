using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace EHR.Controllers;

/// <summary>
/// Controller for serving the patient telehealth join page.
/// This is separate from TelehealthController (API) which handles the data operations.
/// </summary>
[AllowAnonymous]
public class TelehealthViewController : Controller
{
    /// <summary>
    /// Patient telehealth waiting room page.
    /// Accessible via /Telehealth/Join/{token}
    /// </summary>
    [Route("Telehealth/Join/{token}")]
    public IActionResult Join(string token)
    {
        ViewBag.Token = token ?? "";
        return View("~/Views/Telehealth/Join.cshtml");
    }
}
