using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using EHR.Services;

namespace EHR.Controllers;

[ApiController]
[Route("api/lab-tests")]
[Authorize]
public class LabTestCatalogController : ControllerBase
{
    private readonly ILabTestCatalogService _service;

    public LabTestCatalogController(ILabTestCatalogService service)
    {
        _service = service;
    }

    [HttpGet("panels")]
    public async Task<ActionResult> GetPanels()
    {
        var panels = await _service.GetPanelsAsync();
        return Ok(panels);
    }

    [HttpGet("panels/{panelName}")]
    public async Task<ActionResult> GetTestsByPanel(string panelName)
    {
        var tests = await _service.GetTestsByPanelAsync(panelName);
        return Ok(tests);
    }

    [HttpGet("search")]
    public async Task<ActionResult> Search([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q)) return Ok(new List<object>());
        var tests = await _service.SearchAsync(q);
        return Ok(tests);
    }
}
