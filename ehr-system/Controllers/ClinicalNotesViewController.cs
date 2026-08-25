using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace EHR.Controllers;

/// <summary>
/// Controller for serving clinical notes views.
/// This is separate from ClinicalNotesController (API) which handles the data operations.
/// </summary>
[AllowAnonymous]
[Microsoft.AspNetCore.Authorization.Authorize]
public class ClinicalNotesViewController : Controller
{
    /// <summary>
    /// Mobile clinical note editor page.
    /// Accessible via /ClinicalNotes/MobileEditor
    /// Query parameters are passed through to the JavaScript module.
    /// </summary>
    /// <remarks>
    /// Expected query parameters:
    /// - appointmentId: Appointment context (for creating new note)
    /// - noteId: Existing note to edit
    /// - patientId: Patient ID
    /// - providerId: Provider ID
    /// - userId: Current user ID
    /// - token: Authentication token
    /// - apiUrl: API base URL
    /// </remarks>
    [Route("ClinicalNotes/MobileEditor")]
    public IActionResult MobileEditor()
    {
        return View("~/Views/ClinicalNotes/MobileEditor.cshtml");
    }

    // (Legacy /clinical-notes/amendment/{noteId} route removed 2026-04-20.
    // Amendment is now a modal opened from AmendmentAddendumModule.openAmendmentModal;
    // it stays within whatever page the provider is on and never navigates away.)
}
