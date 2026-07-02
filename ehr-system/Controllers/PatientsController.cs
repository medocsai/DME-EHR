using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EHR.Helpers;
using EHR.Services;
using EHR.Models;
using Patient = EHR.Models.Generated.Patient;

namespace EHR.Controllers;

/// <summary>
/// Controller for patient management operations.
/// Handles CRUD operations for patient demographics, status management, and archiving.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[PhiAccessAudit(EntityType = "Patient")]
public class PatientsController : ControllerBase
{
    private readonly IPatientService _patientService;
    private readonly IProfilePictureService _profilePictureService;

    public PatientsController(IPatientService patientService, IProfilePictureService profilePictureService)
    {
        _patientService = patientService;
        _profilePictureService = profilePictureService;
    }

    /// <summary>
    /// Get all patients with optional filtering.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<PatientListDto>>> GetPatients(
        [FromQuery] string? search,
        [FromQuery] string? statuses,
        [FromQuery] int? providerId,
        [FromQuery] bool noCareEpisode = false,
        [FromQuery] bool? isArchived = null,
        [FromQuery] bool? isProfileComplete = null)
    {
        try
        {
            // Parse comma-separated statuses into list
            List<PatientStatus>? statusList = null;
            if (!string.IsNullOrWhiteSpace(statuses))
            {
                statusList = statuses.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => int.TryParse(s, out _))
                    .Select(s => (PatientStatus)int.Parse(s))
                    .ToList();
            }

            var patients = await _patientService.GetPatientsAsync(search, statusList, providerId, noCareEpisode, isArchived, isProfileComplete);
            return Ok(patients);
        }
        catch (Exception ex) { }
        return BadRequest();
    }

    /// <summary>
    /// Get patients with pagination.
    /// </summary>
    [HttpGet("paged")]
    public async Task<ActionResult<PagedPatientResponse>> GetPaged(
        [FromQuery] string? search,
        [FromQuery] string? statuses,
        [FromQuery] int? providerId,
        [FromQuery] bool noCareEpisode = false,
        [FromQuery] bool? isArchived = null,
        [FromQuery] bool? isProfileComplete = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        try
        {
            List<PatientStatus>? statusList = null;
            if (!string.IsNullOrWhiteSpace(statuses))
            {
                statusList = statuses.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => int.TryParse(s, out _))
                    .Select(s => (PatientStatus)int.Parse(s))
                    .ToList();
            }

            var allPatients = await _patientService.GetPatientsAsync(search, statusList, providerId, noCareEpisode, isArchived, isProfileComplete);
            var totalCount = allPatients.Count;
            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
            var items = allPatients.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return Ok(new PagedPatientResponse
            {
                Items = items,
                TotalCount = totalCount,
                TotalPages = totalPages,
                Page = page,
                PageSize = pageSize
            });
        }
        catch (Exception ex) { }
        return BadRequest();
    }

    /// <summary>
    /// Get patient by ID.
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<PatientDetailDto>> GetPatient(int id, [FromQuery] int? providerId)
    {
        var patient = await _patientService.GetPatientByIdAsync(id, providerId);
        if (patient == null) return NotFound();
        return Ok(patient);
    }

    /// <summary>
    /// Check if a patient email is unique within the tenant.
    /// </summary>
    [HttpGet("check-email")]
    [Authorize(Roles = "0,1,2,3")]
    public async Task<ActionResult> CheckEmail([FromQuery] string email, [FromQuery] int? excludePatientId)
    {
        var (isUnique, existingPatientName) = await _patientService.CheckEmailUniqueAsync(email, excludePatientId);
        return Ok(new { isUnique, existingPatientName });
    }

    /// <summary>
    /// Create a new patient.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "0,1,2,3")] // SuperAdmin, ClinicAdmin, Clinician, FrontDesk can create patients
    public async Task<ActionResult<Patient>> CreatePatient([FromBody] PatientCreateDto dto)
    {
        try
        {
            var patient = await _patientService.CreatePatientAsync(dto);
            return CreatedAtAction(nameof(GetPatient), new { id = patient.PatientId }, patient);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already in use"))
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Update an existing patient.
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "0,1,2,3")] // SuperAdmin, ClinicAdmin, Clinician, FrontDesk can edit patients
    public async Task<ActionResult<Patient>> UpdatePatient(int id, [FromBody] PatientUpdateDto dto)
    {
        try
        {
            var patient = await _patientService.UpdatePatientAsync(id, dto);
            if (patient == null) return NotFound();
            return Ok(patient);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already in use"))
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Delete a patient.
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "0,1")]
    public async Task<ActionResult> DeletePatient(int id)
    {
        var result = await _patientService.DeletePatientAsync(id);
        if (!result) return NotFound();
        return NoContent();
    }

    /// <summary>
    /// Archive a patient. Archived patients can be viewed but not edited.
    /// </summary>
    [HttpPost("{id}/archive")]
    [Authorize(Roles = "0,1")] // Only SuperAdmin and ClinicAdmin can archive
    public async Task<ActionResult> ArchivePatient(int id)
    {
        var result = await _patientService.ArchivePatientAsync(id);
        if (!result) return NotFound();
        return Ok(new { message = "Patient archived successfully" });
    }

    /// <summary>
    /// Unarchive a patient. Restores the patient to normal editable state.
    /// </summary>
    [HttpPost("{id}/unarchive")]
    [Authorize(Roles = "0,1")] // Only SuperAdmin and ClinicAdmin can unarchive
    public async Task<ActionResult> UnarchivePatient(int id)
    {
        var result = await _patientService.UnarchivePatientAsync(id);
        if (!result) return NotFound();
        return Ok(new { message = "Patient unarchived successfully" });
    }

    // ============================================
    // PROFILE PICTURE ENDPOINTS
    // ============================================

    /// <summary>
    /// Upload a profile picture for a patient.
    /// </summary>
    [HttpPost("{id}/profile-picture")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10MB limit
    [Authorize(Roles = "0,1,2,3")] // SuperAdmin, ClinicAdmin, Clinician, FrontDesk (same as edit patient)
    public async Task<ActionResult<ProfilePictureResponseDto>> UploadProfilePicture(
        int id,
        [FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new ProfilePictureResponseDto
            {
                EntityId = id,
                EntityType = "Patient",
                HasProfilePicture = false,
                Message = "No file provided"
            });
        }

        var result = await _profilePictureService.UploadPatientProfilePictureAsync(id, file);
        if (result == null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// Get a patient's profile picture.
    /// AllowAnonymous because img tags cannot send JWT Authorization headers.
    /// Tenant isolation is enforced in the service layer.
    /// </summary>
    [HttpGet("{id}/profile-picture")]
    [AllowAnonymous]
    public async Task<IActionResult> GetProfilePicture(int id)
    {
        var result = await _profilePictureService.GetPatientProfilePictureAsync(id);
        if (result == null)
            return NotFound(new { message = "Profile picture not found" });

        var (stream, contentType) = result.Value;
        Response.Headers["Cache-Control"] = "private, max-age=3600";
        return File(stream, contentType);
    }

    /// <summary>
    /// Delete a patient's profile picture.
    /// </summary>
    [HttpDelete("{id}/profile-picture")]
    [Authorize(Roles = "0,1,2,3")] // SuperAdmin, ClinicAdmin, Clinician, FrontDesk (same as edit patient)
    public async Task<IActionResult> DeleteProfilePicture(int id)
    {
        var result = await _profilePictureService.DeletePatientProfilePictureAsync(id);
        if (!result)
            return NotFound(new { message = "Patient or profile picture not found" });

        return Ok(new ProfilePictureResponseDto
        {
            EntityId = id,
            EntityType = "Patient",
            HasProfilePicture = false,
            Message = "Profile picture deleted successfully"
        });
    }
}
