using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EHR.Services;
using EHR.Models;

namespace EHR.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersApiController : ControllerBase
{
    private readonly IOrderService _service;

    public OrdersApiController(IOrderService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult> GetAll(
        [FromQuery] int? patientId,
        [FromQuery] int? orderType,
        [FromQuery] int? status,
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo)
    {
        var orders = await _service.GetAllAsync(patientId, orderType, status, dateFrom, dateTo);
        return Ok(orders);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult> GetById(int id)
    {
        var order = await _service.GetByIdAsync(id);
        if (order == null) return NotFound();
        return Ok(order);
    }

    [HttpGet("~/api/patients/{patientId}/orders")]
    public async Task<ActionResult> GetByPatient(int patientId)
    {
        var orders = await _service.GetByPatientAsync(patientId);
        return Ok(orders);
    }

    [HttpPost]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult> Create([FromBody] OrderCreateDto dto)
    {
        var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
        var order = await _service.CreateAsync(dto, userId);
        return Ok(order);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult> Update(int id, [FromBody] OrderUpdateDto dto)
    {
        var order = await _service.UpdateAsync(id, dto);
        if (order == null) return NotFound();
        return Ok(order);
    }

    [HttpPost("{id}/cancel")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult> Cancel(int id)
    {
        var userId = int.Parse(User.FindFirst("UserId")?.Value ?? "0");
        var result = await _service.CancelAsync(id, userId);
        if (!result) return NotFound();
        return Ok(new { message = "Order cancelled" });
    }

    [HttpPost("{id}/results")]
    [Authorize(Roles = "0,1,2")]
    public async Task<ActionResult> AddResults(int id, [FromBody] OrderResultCreateDto dto)
    {
        var result = await _service.AddResultsAsync(id, dto);
        if (!result) return NotFound();
        return Ok(new { message = "Results added" });
    }

    [HttpGet("pending-counts")]
    public async Task<ActionResult> GetPendingCounts()
    {
        var counts = await _service.GetPendingCountsAsync();
        return Ok(counts);
    }

    /// <summary>
    /// Returns the one-shot payload for the lab-order print template
    /// (clinic + patient + provider + order + results + audit), with all
    /// date/time strings pre-formatted in the active location's timezone.
    /// Used by the front-end print button on the Order Details modal.
    /// </summary>
    [HttpGet("{id}/print-data")]
    [Authorize(Roles = "0,1,2,3")]
    public async Task<ActionResult> GetPrintData(int id)
    {
        var printedByName =
            (User.FindFirst("FirstName")?.Value + " " + User.FindFirst("LastName")?.Value).Trim();
        if (string.IsNullOrWhiteSpace(printedByName))
            printedByName = User.Identity?.Name ?? "";

        var data = await _service.GetLabOrderPrintDataAsync(id, printedByName);
        if (data == null) return NotFound(new { message = "Lab order not found" });
        return Ok(data);
    }
}
