using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EHR.Services;

namespace EHR.Controllers;

[ApiController]
[Route("api/history-review")]
[Authorize]
public class HistoryReviewActionsController : ControllerBase
{
    private readonly IHistoryReviewActionService _service;

    public HistoryReviewActionsController(IHistoryReviewActionService service)
    {
        _service = service;
    }

    public class HistoryActionRequest
    {
        public int RecordId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    // ========== Allergies ==========

    [HttpPost("allergy/inactivate")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> AllergyInactivate([FromBody] HistoryActionRequest req)
    {
        if (req == null || req.RecordId <= 0 || string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { message = "recordId and reason are required" });
        var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
        try
        {
            var ok = await _service.InactivateAllergyAsync(req.RecordId, req.Reason.Trim(), userId);
            if (!ok) return NotFound();
            return Ok(new { success = true });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("allergy/reactivate")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> AllergyReactivate([FromBody] HistoryActionRequest req)
    {
        if (req == null || req.RecordId <= 0 || string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { message = "recordId and reason are required" });
        var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
        try
        {
            var ok = await _service.ReactivateAllergyAsync(req.RecordId, req.Reason.Trim(), userId);
            if (!ok) return NotFound();
            return Ok(new { success = true });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("allergy/delete")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> AllergyDelete([FromBody] HistoryActionRequest req)
    {
        if (req == null || req.RecordId <= 0 || string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { message = "recordId and reason are required" });
        var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
        try
        {
            var ok = await _service.DeleteAllergyAsync(req.RecordId, req.Reason.Trim(), userId);
            if (!ok) return NotFound();
            return Ok(new { success = true });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ========== Medications ==========

    [HttpPost("medication/discontinue")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> MedicationDiscontinue([FromBody] HistoryActionRequest req)
    {
        if (req == null || req.RecordId <= 0 || string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { message = "recordId and reason are required" });
        var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
        try
        {
            var ok = await _service.DiscontinueMedicationAsync(req.RecordId, req.Reason.Trim(), userId);
            if (!ok) return NotFound();
            return Ok(new { success = true });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("medication/revert-discontinue")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> MedicationRevertDiscontinue([FromBody] HistoryActionRequest req)
    {
        if (req == null || req.RecordId <= 0 || string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { message = "recordId and reason are required" });
        var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
        try
        {
            var ok = await _service.RevertDiscontinueMedicationAsync(req.RecordId, req.Reason.Trim(), userId);
            if (!ok) return NotFound();
            return Ok(new { success = true });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("medication/mark-on-hold")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> MedicationMarkOnHold([FromBody] HistoryActionRequest req)
    {
        if (req == null || req.RecordId <= 0 || string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { message = "recordId and reason are required" });
        var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
        try
        {
            var ok = await _service.MarkMedicationOnHoldAsync(req.RecordId, req.Reason.Trim(), userId);
            if (!ok) return NotFound();
            return Ok(new { success = true });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("medication/resume-from-hold")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> MedicationResumeFromHold([FromBody] HistoryActionRequest req)
    {
        if (req == null || req.RecordId <= 0 || string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { message = "recordId and reason are required" });
        var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
        try
        {
            var ok = await _service.ResumeMedicationFromHoldAsync(req.RecordId, req.Reason.Trim(), userId);
            if (!ok) return NotFound();
            return Ok(new { success = true });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("medication/complete")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> MedicationComplete([FromBody] HistoryActionRequest req)
    {
        if (req == null || req.RecordId <= 0 || string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { message = "recordId and reason are required" });
        var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
        try
        {
            var ok = await _service.CompleteMedicationAsync(req.RecordId, req.Reason.Trim(), userId);
            if (!ok) return NotFound();
            return Ok(new { success = true });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("medication/delete")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> MedicationDelete([FromBody] HistoryActionRequest req)
    {
        if (req == null || req.RecordId <= 0 || string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { message = "recordId and reason are required" });
        var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
        try
        {
            var ok = await _service.DeleteMedicationAsync(req.RecordId, req.Reason.Trim(), userId);
            if (!ok) return NotFound();
            return Ok(new { success = true });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ========== Problems ==========

    [HttpPost("problem/mark-resolved")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> ProblemMarkResolved([FromBody] HistoryActionRequest req)
    {
        if (req == null || req.RecordId <= 0 || string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { message = "recordId and reason are required" });
        var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
        try
        {
            var ok = await _service.MarkProblemResolvedAsync(req.RecordId, req.Reason.Trim(), userId);
            if (!ok) return NotFound();
            return Ok(new { success = true });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("problem/mark-inactive")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> ProblemMarkInactive([FromBody] HistoryActionRequest req)
    {
        if (req == null || req.RecordId <= 0 || string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { message = "recordId and reason are required" });
        var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
        try
        {
            var ok = await _service.MarkProblemInactiveAsync(req.RecordId, req.Reason.Trim(), userId);
            if (!ok) return NotFound();
            return Ok(new { success = true });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("problem/reactivate")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> ProblemReactivate([FromBody] HistoryActionRequest req)
    {
        if (req == null || req.RecordId <= 0 || string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { message = "recordId and reason are required" });
        var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
        try
        {
            var ok = await _service.ReactivateProblemAsync(req.RecordId, req.Reason.Trim(), userId);
            if (!ok) return NotFound();
            return Ok(new { success = true });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("problem/delete")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> ProblemDelete([FromBody] HistoryActionRequest req)
    {
        if (req == null || req.RecordId <= 0 || string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { message = "recordId and reason are required" });
        var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
        try
        {
            var ok = await _service.DeleteProblemAsync(req.RecordId, req.Reason.Trim(), userId);
            if (!ok) return NotFound();
            return Ok(new { success = true });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ========== Supplements ==========

    [HttpPost("supplement/inactivate")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> SupplementInactivate([FromBody] HistoryActionRequest req)
    {
        if (req == null || req.RecordId <= 0 || string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { message = "recordId and reason are required" });
        var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
        try
        {
            var ok = await _service.InactivateSupplementAsync(req.RecordId, req.Reason.Trim(), userId);
            if (!ok) return NotFound();
            return Ok(new { success = true });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("supplement/reactivate")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> SupplementReactivate([FromBody] HistoryActionRequest req)
    {
        if (req == null || req.RecordId <= 0 || string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { message = "recordId and reason are required" });
        var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
        try
        {
            var ok = await _service.ReactivateSupplementAsync(req.RecordId, req.Reason.Trim(), userId);
            if (!ok) return NotFound();
            return Ok(new { success = true });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("supplement/delete")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> SupplementDelete([FromBody] HistoryActionRequest req)
    {
        if (req == null || req.RecordId <= 0 || string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { message = "recordId and reason are required" });
        var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
        try
        {
            var ok = await _service.DeleteSupplementAsync(req.RecordId, req.Reason.Trim(), userId);
            if (!ok) return NotFound();
            return Ok(new { success = true });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    // ========== Delete-only sections ==========

    [HttpPost("family-history/delete")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> FamilyHistoryDelete([FromBody] HistoryActionRequest req)
    {
        if (req == null || req.RecordId <= 0 || string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { message = "recordId and reason are required" });
        var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
        try
        {
            var ok = await _service.DeleteFamilyHistoryAsync(req.RecordId, req.Reason.Trim(), userId);
            if (!ok) return NotFound();
            return Ok(new { success = true });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("social-history/delete")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> SocialHistoryDelete([FromBody] HistoryActionRequest req)
    {
        if (req == null || req.RecordId <= 0 || string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { message = "recordId and reason are required" });
        var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
        try
        {
            var ok = await _service.DeleteSocialHistoryAsync(req.RecordId, req.Reason.Trim(), userId);
            if (!ok) return NotFound();
            return Ok(new { success = true });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost("immunization/delete")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> ImmunizationDelete([FromBody] HistoryActionRequest req)
    {
        if (req == null || req.RecordId <= 0 || string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { message = "recordId and reason are required" });
        var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
        try
        {
            var ok = await _service.DeleteImmunizationAsync(req.RecordId, req.Reason.Trim(), userId);
            if (!ok) return NotFound();
            return Ok(new { success = true });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }
}
