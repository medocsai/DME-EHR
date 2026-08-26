using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EHR.Services;

namespace EHR.Controllers;

/// <summary>
/// DME — reference-data typeaheads: payers, ICD-10-CM diagnoses, and the
/// national HCPCS code list.
///
/// WHY THEY ARE NOT ON DmeController
/// DmeController carries a class-level [PhiAccessAudit(EntityType =
/// "DmeCustomer")], which writes one audit row per successful request. A
/// typeahead fires on every keystroke, so hosting these there would bury the
/// real PHI access trail under thousands of rows saying a customer was read
/// when none was. Every list here is a published national code set and contains
/// no patient data, so there is nothing to audit.
///
/// WHY ONE CONTROLLER FOR ALL THREE
/// They are the same thing three times: search a global reference list, return
/// at most a page of matches as JSON. A controller per code list would be three
/// copies of that with nothing different in them.
///
/// They still require authentication. None of these lists is secret, but no
/// endpoint in this product is open, and an anonymous one is a free scan of
/// 74,719 rows.
/// </summary>
[Authorize]
public class LookupsController : Controller
{
    private readonly IDmePayerCatalog _payers;
    private readonly IDmeIcdCatalog _icd;
    private readonly IDmeHcpcsCatalog _hcpcs;

    public LookupsController(IDmePayerCatalog payers, IDmeIcdCatalog icd, IDmeHcpcsCatalog hcpcs)
    {
        _payers = payers;
        _icd = icd;
        _hcpcs = hcpcs;
    }

    /// <summary>GET /Lookups/Payers?q=aetna — for the customer form's payer box.</summary>
    [HttpGet]
    public IActionResult Payers(string? q)
        => Json(_payers.Search(q).Select(p => new { id = p.PayerId, name = p.Name, code = p.PayerCode }));

    /// <summary>GET /Lookups/Icd?q=obesity — for the diagnosis box.</summary>
    [HttpGet]
    public IActionResult Icd(string? q)
        => Json(_icd.Search(q).Select(d => new { code = d.Code, name = d.Description }));

    /// <summary>
    /// GET /Lookups/Hcpcs?q=wheelchair — for adding an item to the supplier's
    /// catalog. `retired` and `noPay` are sent so the picker can say so before
    /// somebody prices an item they will never be paid for.
    /// </summary>
    [HttpGet]
    public IActionResult Hcpcs(string? q)
        => Json(_hcpcs.Search(q).Select(h => new
        {
            code = h.Code,
            name = h.Description,
            shortName = h.ShortDescription,
            retired = h.IsRetired,
            noPay = h.MedicareNeverPays
        }));
}
