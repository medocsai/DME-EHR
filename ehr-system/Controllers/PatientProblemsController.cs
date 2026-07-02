using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EHR.Helpers;
using EHR.Services;
using EHR.Models;

namespace EHR.Controllers;

[ApiController]
[Route("api/patients/{patientId}/problems")]
[Authorize]
[PhiAccessAudit(EntityType = "PatientProblem", IdRouteParam = "patientId")]
public class PatientProblemsController : ControllerBase
{
    private readonly IPatientProblemService _service;

    public PatientProblemsController(IPatientProblemService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult> GetProblems(int patientId)
    {
        var problems = await _service.GetByPatientAsync(patientId);
        return Ok(problems);
    }

    [HttpPost]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> CreateProblem(int patientId, [FromBody] PatientProblemCreateDto dto)
    {
        var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
        var problem = await _service.CreateAsync(patientId, dto, userId);
        return Ok(problem);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> UpdateProblem(int patientId, int id, [FromBody] PatientProblemUpdateDto dto)
    {
        var problem = await _service.UpdateAsync(id, dto);
        if (problem == null) return NotFound();
        return Ok(problem);
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "0,1,2,6,7")]
    public async Task<ActionResult> DeleteProblem(int patientId, int id)
    {
        var result = await _service.DeleteAsync(id);
        if (!result) return NotFound();
        return Ok(new { message = "Deleted successfully" });
    }
}
