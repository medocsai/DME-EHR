using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EHR.Helpers;
using EHR.Services;
using EHR.Models;
using EHR.Models.Generated;

namespace EHR.Controllers;

/// <summary>
/// Controller for billing and revenue cycle management.
/// Handles charge capture, claim creation, submission, and A/R aging reports.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[PhiAccessAudit(EntityType = "Billing")]
public class BillingController : ControllerBase
{
    private readonly IBillingService _billingService;
    private readonly IClinicalNoteAmendmentService _amendmentService;

    public BillingController(IBillingService billingService, IClinicalNoteAmendmentService amendmentService)
    {
        _billingService = billingService;
        _amendmentService = amendmentService;
    }

    /// <summary>
    /// Get charges with optional filtering.
    /// </summary>
    [HttpGet("get-charges")]
    public async Task<ActionResult<List<ChargeListDto>>> GetCharges(
        [FromQuery] int? patientId,
        [FromQuery] ChargeStatus? status)
    {
        var charges = await _billingService.GetChargesAsync(patientId, status);
        return Ok(charges);
    }

    /// <summary>
    /// Create a new charge.
    /// </summary>
    [HttpPost("charges")]
    [Authorize(Roles = "0,1,4,2")]
    public async Task<ActionResult<Charge>> CreateCharge([FromBody] ChargeCreateDto dto)
    {
        var charge = await _billingService.CreateChargeAsync(dto);
        return Ok(charge);
    }

    /// <summary>
    /// Get claims with optional status filtering.
    /// </summary>
    [HttpGet("claims")]
    public async Task<ActionResult<List<ClaimListDto>>> GetClaims([FromQuery] ClaimStatus? status)
    {
        var claims = await _billingService.GetClaimsAsync(status);
        return Ok(claims);
    }

    /// <summary>
    /// Create a new billing claim.
    /// </summary>
    [HttpPost("claims")]
    [Authorize(Roles = "0,1,4")]
    public async Task<ActionResult<BillingClaim>> CreateClaim([FromBody] ClaimCreateDto dto)
    {
        try
        {
            var claim = await _billingService.CreateClaimAsync(dto);
            return Ok(claim);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Submit a claim for processing.
    /// </summary>
    [HttpPost("claims/{id}/submit")]
    [Authorize(Roles = "0,1,4")]
    public async Task<ActionResult<BillingClaim>> SubmitClaim(int id)
    {
        // Amendment window check: if the linked encounter still has an open amendment window,
        // the claim cannot be submitted yet. The provider may still amend the note and
        // change CPT/ICD codes, so submitting now would risk sending stale data to Office Ally.
        if (await _amendmentService.IsClaimSubmitLockedAsync(id))
        {
            return Conflict(new
            {
                message = "Amendment window is still open for this encounter. Submission is temporarily locked until the window closes."
            });
        }

        var claim = await _billingService.SubmitClaimAsync(id);
        if (claim == null) return NotFound();
        return Ok(claim);
    }

    /// <summary>
    /// AI: suggest ICD-10 codes from a raw note-text body (no appointment / DB lookup).
    /// Used by the amendment modal where the provider has edited the note in-memory
    /// but not yet saved — the normal /appointments/{id}/suggest-icd endpoint would
    /// only see the OLD saved text. This endpoint reads the text from the request body
    /// so the AI sees exactly what the provider is currently looking at.
    /// </summary>
    [HttpPost("ai/suggest-icd-from-text")]
    public async Task<ActionResult<IcdSuggestionResponse>> SuggestIcdFromText([FromBody] IcdSuggestionRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.NoteContent))
            return BadRequest(new IcdSuggestionResponse { Success = false, ErrorMessage = "NoteContent is required." });
        var result = await _billingService.SuggestIcdCodesAsync(request);
        return Ok(result);
    }

    /// <summary>
    /// AI: suggest CPT codes from a raw note-text body + already-picked ICD codes.
    /// Sibling of SuggestIcdFromText — same rationale. Used when amendment is in
    /// progress and the note hasn't been saved yet.
    /// </summary>
    [HttpPost("ai/suggest-cpt-from-text")]
    public async Task<ActionResult<CptSuggestionResponse>> SuggestCptFromText([FromBody] CptSuggestionRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.NoteContent))
            return BadRequest(new CptSuggestionResponse { Success = false, ErrorMessage = "NoteContent is required." });
        var result = await _billingService.SuggestCptCodesAsync(request);
        return Ok(result);
    }

    /// <summary>
    /// Get A/R aging report.
    /// </summary>
    [HttpGet("ar-aging")]
    public async Task<ActionResult<ARAgingDto>> GetARAging()
    {
        var aging = await _billingService.GetARAgingAsync();
        return Ok(aging);
    }

    /// <summary>
    /// Get paged claims with filtering and summary totals.
    /// </summary>
    [HttpGet("claims/paged")]
    public async Task<ActionResult<BillingPagedResult<ClaimListDto>>> GetClaimsPaged(
        [FromQuery] int? patientId,
        [FromQuery] int? providerId,
        [FromQuery] string? payerSearch,
        [FromQuery] List<int>? statuses,
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        var result = await _billingService.GetClaimsPagedAsync(patientId, providerId, payerSearch, statuses, dateFrom, dateTo, page, pageSize);
        return Ok(result);
    }

    /// <summary>
    /// Get paged charges with filtering and summary totals.
    /// </summary>
    [HttpGet("get-charges/paged")]
    public async Task<ActionResult<BillingPagedResult<ChargeListDto>>> GetChargesPaged(
        [FromQuery] int? patientId,
        [FromQuery] int? providerId,
        [FromQuery] List<int>? statuses,
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        var result = await _billingService.GetChargesPagedAsync(patientId, providerId, statuses, dateFrom, dateTo, page, pageSize);
        return Ok(result);
    }

    // ── CMS 1500 / UB04 Claim Management ──────────────────────

    [HttpGet("claims/{id}/detail")]
    public async Task<ActionResult<ClaimDetailDto>> GetClaimDetail(int id)
    {
        var detail = await _billingService.GetClaimDetailAsync(id);
        if (detail == null) return NotFound();
        return Ok(detail);
    }

    [HttpPut("claims/{id}")]
    [Authorize(Roles = "0,1,4")]
    public async Task<ActionResult<BillingClaim>> UpdateClaim(int id, [FromBody] ClaimUpdateDto dto)
    {
        var claim = await _billingService.UpdateClaimFieldsAsync(id, dto);
        if (claim == null) return NotFound();
        return Ok(claim);
    }

    [HttpPost("claims/{id}/charges")]
    [Authorize(Roles = "0,1,4")]
    public async Task<ActionResult<Charge>> AddChargeToClaim(int id, [FromBody] ClaimChargeCreateDto dto)
    {
        try
        {
            dto.ClaimId = id;
            var charge = await _billingService.AddChargeToClaimAsync(dto);
            return Ok(charge);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("charges/{id}")]
    [Authorize(Roles = "0,1,4")]
    public async Task<ActionResult> UpdateCharge(int id, [FromBody] ClaimChargeCreateDto dto)
    {
        var result = await _billingService.UpdateChargeOnClaimAsync(id, dto);
        if (!result) return NotFound();
        return Ok(new { success = true });
    }

    [HttpDelete("charges/{id}")]
    [Authorize(Roles = "0,1,4")]
    public async Task<ActionResult> DeleteCharge(int id)
    {
        var result = await _billingService.RemoveChargeFromClaimAsync(id);
        if (!result) return NotFound();
        return Ok(new { success = true });
    }

    [HttpPost("claims/{id}/ready")]
    [Authorize(Roles = "0,1,4")]
    public async Task<ActionResult<BillingClaim>> MarkClaimReady(int id)
    {
        try
        {
            var claim = await _billingService.MarkClaimReadyAsync(id);
            if (claim == null) return NotFound();
            return Ok(claim);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// AI-powered CPT code suggestions based on clinical note and diagnosis codes
    /// </summary>
    [HttpPost("suggest-cpt")]
    [Authorize(Roles = "0,1,2,4")]
    public async Task<ActionResult<CptSuggestionResponse>> SuggestCptCodes([FromBody] CptSuggestionRequest request)
    {
        try
        {
            var result = await _billingService.SuggestCptCodesAsync(request);
            return Ok(result);
        }
        catch (Exception)
        {
            return StatusCode(500, new CptSuggestionResponse
            {
                Success = false,
                ErrorMessage = "CPT suggestion service unavailable"
            });
        }
    }

    [HttpGet("claims/{id}/pdf")]
    [Authorize(Roles = "0,1,4")]
    public async Task<ActionResult> GetClaimPdf(int id)
    {
        try
        {
            var pdf = await _billingService.GenerateClaimPdfAsync(id);
            return File(pdf, "application/pdf", $"CMS1500-{id}.pdf");
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
