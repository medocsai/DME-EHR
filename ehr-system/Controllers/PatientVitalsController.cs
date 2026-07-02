using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EHR.Helpers;
using EHR.Services;
using EHR.Models;

namespace EHR.Controllers;

[ApiController]
[Route("api/patients/{patientId}/vitals")]
[Authorize]
[PhiAccessAudit(EntityType = "PatientVital", IdRouteParam = "patientId")]
public class PatientVitalsController : ControllerBase
{
    private readonly IPatientVitalService _service;

    public PatientVitalsController(IPatientVitalService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult> GetVitals(int patientId)
    {
        var vitals = await _service.GetByPatientAsync(patientId);
        return Ok(vitals);
    }

    [HttpPost]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> CreateVital(int patientId, [FromBody] PatientVitalCreateDto dto)
    {
        var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
        var vital = await _service.CreateAsync(patientId, dto, userId);
        return Ok(vital);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> UpdateVital(int patientId, int id, [FromBody] PatientVitalCreateDto dto)
    {
        var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
        var vital = await _service.UpdateAsync(id, dto, userId);
        if (vital == null) return NotFound();
        return Ok(vital);
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> DeleteVital(int patientId, int id)
    {
        var result = await _service.DeleteAsync(id);
        if (!result) return NotFound();
        return Ok(new { message = "Deleted successfully" });
    }
}
