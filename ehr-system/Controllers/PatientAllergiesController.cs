using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EHR.Helpers;
using EHR.Services;
using EHR.Models;

namespace EHR.Controllers;

[ApiController]
[Route("api/patients/{patientId}/allergies")]
[Authorize]
[PhiAccessAudit(EntityType = "PatientAllergy", IdRouteParam = "patientId")]
public class PatientAllergiesController : ControllerBase
{
    private readonly IPatientAllergyService _service;

    public PatientAllergiesController(IPatientAllergyService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult> GetAllergies(int patientId)
    {
        var allergies = await _service.GetByPatientAsync(patientId);
        return Ok(allergies);
    }

    [HttpPost]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> CreateAllergy(int patientId, [FromBody] PatientAllergyCreateDto dto)
    {
        var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
        var allergy = await _service.CreateAsync(patientId, dto, userId);
        return Ok(allergy);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> UpdateAllergy(int patientId, int id, [FromBody] PatientAllergyUpdateDto dto)
    {
        var allergy = await _service.UpdateAsync(id, dto);
        if (allergy == null) return NotFound();
        return Ok(allergy);
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> DeleteAllergy(int patientId, int id)
    {
        var result = await _service.DeleteAsync(id);
        if (!result) return NotFound();
        return Ok(new { message = "Deleted successfully" });
    }
}
