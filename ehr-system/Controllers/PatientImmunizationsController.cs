using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EHR.Helpers;
using EHR.Services;
using EHR.Models;

namespace EHR.Controllers;

[ApiController]
[Route("api/patients/{patientId}/immunizations")]
[Authorize]
[PhiAccessAudit(EntityType = "PatientImmunization", IdRouteParam = "patientId")]
public class PatientImmunizationsController : ControllerBase
{
    private readonly IPatientImmunizationService _service;

    public PatientImmunizationsController(IPatientImmunizationService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult> GetImmunizations(int patientId)
    {
        var immunizations = await _service.GetByPatientAsync(patientId);
        return Ok(immunizations);
    }

    [HttpPost]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> CreateImmunization(int patientId, [FromBody] PatientImmunizationCreateDto dto)
    {
        var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
        var immunization = await _service.CreateAsync(patientId, dto, userId);
        return Ok(immunization);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> UpdateImmunization(int patientId, int id, [FromBody] PatientImmunizationCreateDto dto)
    {
        var immunization = await _service.UpdateAsync(id, dto);
        if (immunization == null) return NotFound();
        return Ok(immunization);
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> DeleteImmunization(int patientId, int id)
    {
        var result = await _service.DeleteAsync(id);
        if (!result) return NotFound();
        return Ok(new { message = "Deleted successfully" });
    }
}
