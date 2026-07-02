using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EHR.Helpers;
using EHR.Services;
using EHR.Models;

namespace EHR.Controllers;

[ApiController]
[Route("api/patients/{patientId}/social-history")]
[Authorize]
[PhiAccessAudit(EntityType = "PatientSocialHistory", IdRouteParam = "patientId")]
public class PatientSocialHistoryController : ControllerBase
{
    private readonly IPatientSocialHistoryService _service;

    public PatientSocialHistoryController(IPatientSocialHistoryService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult> GetSocialHistory(int patientId)
    {
        var history = await _service.GetByPatientAsync(patientId);
        return Ok(history);
    }

    [HttpPost]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> CreateSocialHistory(int patientId, [FromBody] PatientSocialHistoryCreateDto dto)
    {
        var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
        var history = await _service.CreateAsync(patientId, dto, userId);
        return Ok(history);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> UpdateSocialHistory(int patientId, int id, [FromBody] PatientSocialHistoryCreateDto dto)
    {
        var history = await _service.UpdateAsync(id, dto);
        if (history == null) return NotFound();
        return Ok(history);
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> DeleteSocialHistory(int patientId, int id)
    {
        var result = await _service.DeleteAsync(id);
        if (!result) return NotFound();
        return Ok(new { message = "Deleted successfully" });
    }
}
