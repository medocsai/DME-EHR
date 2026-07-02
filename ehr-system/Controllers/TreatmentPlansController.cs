using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EHR.Helpers;
using EHR.Services;
using EHR.Models;

namespace EHR.Controllers;

[ApiController]
[Route("api/patients/{patientId}/treatment-plans")]
[Authorize]
[PhiAccessAudit(EntityType = "TreatmentPlan", IdRouteParam = "patientId")]
public class TreatmentPlansController : ControllerBase
{
    private readonly ITreatmentPlanService _service;

    public TreatmentPlansController(ITreatmentPlanService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult> GetTreatmentPlans(int patientId)
    {
        var plans = await _service.GetByPatientAsync(patientId);
        return Ok(plans);
    }

    [HttpPost]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult> CreateTreatmentPlan(int patientId, [FromBody] TreatmentPlanCreateDto dto)
    {
        var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
        var plan = await _service.CreateAsync(patientId, dto, userId);
        return Ok(plan);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult> UpdateTreatmentPlan(int patientId, int id, [FromBody] TreatmentPlanUpdateDto dto)
    {
        var plan = await _service.UpdateAsync(id, dto);
        if (plan == null) return NotFound();
        return Ok(plan);
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult> DeleteTreatmentPlan(int patientId, int id)
    {
        var result = await _service.DeleteAsync(id);
        if (!result) return NotFound();
        return Ok(new { message = "Deleted successfully" });
    }
}
