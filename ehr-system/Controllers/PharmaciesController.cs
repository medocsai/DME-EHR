using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EHR.Services;

namespace EHR.Controllers;

[ApiController]
[Route("api/pharmacies")]
[Authorize]
public class PharmaciesController : ControllerBase
{
    private readonly IPharmacyService _service;

    public PharmaciesController(IPharmacyService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult> GetAll()
    {
        var pharmacies = await _service.GetAllAsync();
        return Ok(pharmacies);
    }

    [HttpGet("search")]
    public async Task<ActionResult> Search([FromQuery] string q)
    {
        var pharmacies = await _service.SearchAsync(q);
        return Ok(pharmacies);
    }
}
