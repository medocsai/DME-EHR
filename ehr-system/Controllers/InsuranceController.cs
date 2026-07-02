using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EHR.Helpers;
using EHR.Services;
using EHR.Models;
using EHR.Models.Generated;

namespace EHR.Controllers;

/// <summary>
/// Controller for patient insurance management.
/// Handles insurance CRUD operations and eligibility verification.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[PhiAccessAudit(EntityType = "Insurance")]
public class InsuranceController : ControllerBase
{
    private readonly IInsuranceService _insuranceService;

    public InsuranceController(IInsuranceService insuranceService)
    {
        _insuranceService = insuranceService;
    }

    /// <summary>
    /// Get all insurances for a patient.
    /// </summary>
    [HttpGet("patient/{patientId}")]
    public async Task<ActionResult<List<InsuranceDto>>> GetPatientInsurances(int patientId)
    {
        var insurances = await _insuranceService.GetPatientInsurancesAsync(patientId);
        return Ok(insurances);
    }

    /// <summary>
    /// Add insurance to a patient.
    /// </summary>
    [HttpPost("patient/{patientId}")]
    [Authorize(Roles = "0,1,3,4")]
    public async Task<ActionResult<Insurance>> AddInsurance(int patientId, [FromBody] InsuranceCreateDto dto)
    {
        var insurance = await _insuranceService.AddInsuranceAsync(patientId, dto);
        return Ok(insurance);
    }

    /// <summary>
    /// Update an insurance record.
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "0,1,3,4")]
    public async Task<ActionResult<Insurance>> UpdateInsurance(int id, [FromBody] InsuranceUpdateDto dto)
    {
        var insurance = await _insuranceService.UpdateInsuranceAsync(id, dto);
        if (insurance == null) return NotFound();
        return Ok(insurance);
    }

    /// <summary>
    /// Verify insurance eligibility by insurance ID.
    /// </summary>
    [HttpPost("{id}/verify")]
    [Authorize(Roles = "0,1,3,4")]
    public async Task<ActionResult<InsuranceVerificationResult>> VerifyInsurance(int id)
    {
        var result = await _insuranceService.VerifyInsuranceAsync(id);
        return Ok(result);
    }

    /// <summary>
    /// Verify insurance eligibility. Supports two flows:
    /// 1. Patient Form (unsaved): Pass Type, PayerName, PolicyNumber directly
    /// 2. Care Episode (saved): Pass InsuranceId to use existing record
    /// </summary>
    [HttpPost("verify")]
    [Authorize(Roles = "0,1,3,4")]
    public async Task<ActionResult<InsuranceVerificationResult>> VerifyInsuranceDirect([FromBody] InsuranceVerificationRequest request)
    {
        var result = await _insuranceService.VerifyInsuranceAsync(request);
        return Ok(result);
    }

    /// <summary>
    /// Get rich eligibility details parsed from the stored raw OA response.
    /// </summary>
    [HttpGet("{id}/eligibility-details")]
    [Authorize(Roles = "0,1,3,4")]
    public async Task<ActionResult<EligibilityDetailsDto>> GetEligibilityDetails(int id)
    {
        var details = await _insuranceService.GetEligibilityDetailsAsync(id);
        if (details == null)
            return NotFound(new { message = "No eligibility data available. Please verify insurance first." });
        return Ok(details);
    }
}
