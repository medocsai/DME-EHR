using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EHR.Helpers;
using EHR.Services;
using EHR.Models;

namespace EHR.Controllers;

[ApiController]
[Route("api/patients/{patientId}/medications")]
[Authorize]
[PhiAccessAudit(EntityType = "PatientMedication", IdRouteParam = "patientId")]
public class PatientMedicationsController : ControllerBase
{
    private readonly IPatientMedicationService _service;

    public PatientMedicationsController(IPatientMedicationService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult> GetMedications(int patientId)
    {
        var medications = await _service.GetByPatientAsync(patientId);
        return Ok(medications);
    }

    [HttpPost]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> CreateMedication(int patientId, [FromBody] PatientMedicationCreateDto dto)
    {
        var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
        var medication = await _service.CreateAsync(patientId, dto, userId);
        return Ok(medication);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> UpdateMedication(int patientId, int id, [FromBody] PatientMedicationUpdateDto dto)
    {
        var medication = await _service.UpdateAsync(id, dto);
        if (medication == null) return NotFound();
        return Ok(medication);
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> DeleteMedication(int patientId, int id)
    {
        var result = await _service.DeleteAsync(id);
        if (!result) return NotFound();
        return Ok(new { message = "Deleted successfully" });
    }
}
