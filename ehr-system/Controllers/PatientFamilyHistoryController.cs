using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EHR.Helpers;
using EHR.Services;
using EHR.Models;

namespace EHR.Controllers;

[ApiController]
[Route("api/patients/{patientId}/family-history")]
[Authorize]
[PhiAccessAudit(EntityType = "PatientFamilyHistory", IdRouteParam = "patientId")]
public class PatientFamilyHistoryController : ControllerBase
{
    private readonly IPatientFamilyHistoryService _service;

    public PatientFamilyHistoryController(IPatientFamilyHistoryService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult> GetFamilyHistory(int patientId)
    {
        var history = await _service.GetByPatientAsync(patientId);
        return Ok(history);
    }

    [HttpPost]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> CreateFamilyHistory(int patientId, [FromBody] PatientFamilyHistoryCreateDto dto)
    {
        var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
        var history = await _service.CreateAsync(patientId, dto, userId);
        return Ok(history);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> UpdateFamilyHistory(int patientId, int id, [FromBody] PatientFamilyHistoryCreateDto dto)
    {
        var history = await _service.UpdateAsync(id, dto);
        if (history == null) return NotFound();
        return Ok(history);
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> DeleteFamilyHistory(int patientId, int id)
    {
        var result = await _service.DeleteAsync(id);
        if (!result) return NotFound();
        return Ok(new { message = "Deleted successfully" });
    }
}
