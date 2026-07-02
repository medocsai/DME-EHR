using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EHR.Services;

namespace EHR.Controllers;

[ApiController]
[Route("api/drugs")]
[Authorize]
public class DrugSearchController : ControllerBase
{
    private readonly IDrugDatabaseService _service;

    public DrugSearchController(IDrugDatabaseService service)
    {
        _service = service;
    }

    [HttpGet("search")]
    public async Task<ActionResult> Search([FromQuery] string q, [FromQuery] int limit = 20)
    {
        var drugs = await _service.SearchAsync(q, limit);
        return Ok(drugs);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult> GetById(int id)
    {
        var drug = await _service.GetByIdAsync(id);
        if (drug == null) return NotFound();
        return Ok(drug);
    }
}
