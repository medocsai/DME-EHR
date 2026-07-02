using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EHR.Helpers;
using EHR.Services;
using EHR.Models;
using System.Security.Claims;

namespace EHR.Controllers;

/// <summary>
/// HIPAA-compliant Patient Document Management API
/// Handles secure document upload, download, and management
/// </summary>
[ApiController]
[Route("api/patients/{patientId}/documents")]
[Authorize]
[PhiAccessAudit(EntityType = "PatientDocument", IdRouteParam = "patientId")]
public class PatientDocumentsController : ControllerBase
{
    private readonly IPatientDocumentService _documentService;
    private readonly IAuditService _auditService;

    public PatientDocumentsController(
        IPatientDocumentService documentService,
        IAuditService auditService)
    {
        _documentService = documentService;
        _auditService = auditService;
    }

    private int GetUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");

    private int GetTenantId() =>
        int.Parse(User.FindFirstValue("TenantId") ?? "0");

    /// <summary>
    /// Get all documents for a patient
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<PatientDocumentListDto>>> GetDocuments(int patientId)
    {
        try
        {
            var documents = await _documentService.GetPatientDocumentsAsync(patientId);
            return Ok(documents);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Failed to load documents", error = ex.Message });
        }
    }

    /// <summary>
    /// Upload a document for a patient
    /// Supports multipart/form-data with file upload
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10MB limit
    [Authorize(Roles = "0,1,2,3")] // SuperAdmin, ClinicAdmin, Clinician, FrontDesk
    public async Task<ActionResult<PatientDocumentUploadResultDto>> UploadDocument(
        int patientId,
        [FromForm] IFormFile file,
        [FromForm] int category = 5,
        [FromForm] string? description = null)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new PatientDocumentUploadResultDto
            {
                Success = false,
                Message = "No file provided"
            });
        }

        var userId = GetUserId();

        using var stream = file.OpenReadStream();
        var result = await _documentService.UploadDocumentAsync(
            patientId,
            stream,
            file.FileName,
            file.ContentType,
            category,
            description,
            userId
        );

        if (result.Success)
        {
            return Ok(result);
        }

        return BadRequest(result);
    }

    /// <summary>
    /// Download a document
    /// </summary>
    [HttpGet("{documentId}")]
    public async Task<IActionResult> DownloadDocument(int patientId, int documentId)
    {
        var result = await _documentService.DownloadDocumentAsync(documentId);

        if (result == null)
        {
            return NotFound(new { message = "Document not found" });
        }

        var (stream, fileName, contentType) = result.Value;

        // Log audit for document access
        await _auditService.LogAccessAsync(
            GetUserId(),
            User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value,
            "Read",
            "PatientDocument",
            documentId
        );

        // Set content disposition to inline for viewing, attachment for downloading
        var contentDisposition = contentType.StartsWith("image/") || contentType == "application/pdf"
            ? $"inline; filename=\"{fileName}\""
            : $"attachment; filename=\"{fileName}\"";

        Response.Headers["Content-Disposition"] = contentDisposition;

        return File(stream, contentType, fileName);
    }

    /// <summary>
    /// Delete a document (soft delete)
    /// </summary>
    [HttpDelete("{documentId}")]
    [Authorize(Roles = "0,1,2,3")] // SuperAdmin, ClinicAdmin, Clinician, FrontDesk
    public async Task<IActionResult> DeleteDocument(int patientId, int documentId)
    {
        var userId = GetUserId();
        var result = await _documentService.DeleteDocumentAsync(documentId, userId);

        if (!result)
        {
            return NotFound(new { message = "Document not found" });
        }

        return Ok(new { message = "Document deleted successfully" });
    }
}

/// <summary>
/// Alternative endpoint for document upload (simpler URL structure)
/// </summary>
[ApiController]
[Route("api/documents")]
[Authorize]
public class DocumentsController : ControllerBase
{
    private readonly IPatientDocumentService _documentService;

    public DocumentsController(IPatientDocumentService documentService)
    {
        _documentService = documentService;
    }

    private int GetUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");

    /// <summary>
    /// Upload a document with patient ID in form data
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10MB limit
    [Authorize(Roles = "0,1,2,3")]
    public async Task<ActionResult<PatientDocumentUploadResultDto>> UploadDocument(
        [FromForm] int patientId,
        [FromForm] IFormFile file,
        [FromForm] int category = 5,
        [FromForm] string? description = null)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new PatientDocumentUploadResultDto
            {
                Success = false,
                Message = "No file provided"
            });
        }

        var userId = GetUserId();

        using var stream = file.OpenReadStream();
        var result = await _documentService.UploadDocumentAsync(
            patientId,
            stream,
            file.FileName,
            file.ContentType,
            category,
            description,
            userId
        );

        if (result.Success)
        {
            return Ok(result);
        }

        return BadRequest(result);
    }
}
