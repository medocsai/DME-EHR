using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EHR.Helpers;
using EHR.Services;
using EHR.Models;

namespace EHR.Controllers;

[ApiController]
[Route("api/prescriptions")]
[Authorize]
[PhiAccessAudit(EntityType = "Prescription")]
public class PrescriptionsApiController : ControllerBase
{
    private readonly IPrescriptionService _service;

    public PrescriptionsApiController(IPrescriptionService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult> GetAll(
        [FromQuery] int? patientId,
        [FromQuery] int? status,
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo)
    {
        var prescriptions = await _service.GetAllAsync(patientId, status, dateFrom, dateTo);
        return Ok(prescriptions);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult> GetById(int id)
    {
        var prescription = await _service.GetByIdAsync(id);
        if (prescription == null) return NotFound();
        return Ok(prescription);
    }

    [HttpGet("~/api/patients/{patientId}/prescriptions")]
    public async Task<ActionResult> GetByPatient(int patientId)
    {
        var prescriptions = await _service.GetByPatientAsync(patientId);
        return Ok(prescriptions);
    }

    [HttpPost]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult> Create([FromBody] PrescriptionCreateDto dto)
    {
        var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
        var prescription = await _service.CreateAsync(dto, userId);
        return Ok(prescription);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult> Update(int id, [FromBody] PrescriptionUpdateDto dto)
    {
        var prescription = await _service.UpdateAsync(id, dto);
        if (prescription == null) return NotFound();
        return Ok(prescription);
    }

    [HttpPost("{id}/cancel")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult> Cancel(int id)
    {
        var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
        var result = await _service.CancelAsync(id, userId);
        if (!result) return NotFound();
        return Ok(new { message = "Prescription cancelled" });
    }
}
