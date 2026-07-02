using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using EHR.Models;
using EHR.Models.Generated;
using EHR.Helpers;
using EHR.Services;
using System.Security.Claims;
using System.Linq;

namespace EHR.Controllers;

/// <summary>
/// Reports Controller - Clinic Admin only access
/// Provides endpoints for generating various reports
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "0,1")] // SuperAdmin and ClinicAdmin only
[PhiAccessAudit(EntityType = "Report")]
public class ReportsController : ControllerBase
{
    private readonly EhrDbContext _context;
    private readonly ILogger<ReportsController> _logger;
    private readonly EncryptionHelper _encryptionHelper;
    private readonly IInsuranceAuthorizationService _authorizationService;

    public ReportsController(EhrDbContext context, ILogger<ReportsController> logger, EncryptionHelper encryptionHelper, IInsuranceAuthorizationService authorizationService)
    {
        _context = context;
        _logger = logger;
        _encryptionHelper = encryptionHelper;
        _authorizationService = authorizationService;
    }

    // ── Analytical Report Helper Methods ────────────────────────────────

    private int GetTenantId()
    {
        return int.Parse(User.FindFirst("TenantId")?.Value ?? "0");
    }

    private (DateTime startDate, DateTime endDate) NormalizeDateRange(AnalyticalReportRequestDto request)
    {
        var start = request.StartDate == default ? new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc) : DateTime.SpecifyKind(request.StartDate, DateTimeKind.Utc);
        var end = request.EndDate == default ? start.AddMonths(1) : DateTime.SpecifyKind(request.EndDate, DateTimeKind.Utc).Date.AddDays(1);
        return (start, end);
    }

    private string GetAgeGroup(DateOnly? dob)
    {
        if (!dob.HasValue) return "Unknown";
        var age = DateTime.UtcNow.Year - dob.Value.Year;
        if (dob.Value > DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-age))) age--;
        return age switch { < 18 => "0-17", < 35 => "18-34", < 50 => "35-49", < 65 => "50-64", _ => "65+" };
    }

    private string GetWeekLabel(DateTime date, DateTime periodStart)
    {
        var weekNum = (int)Math.Ceiling((date.Date - periodStart.Date).TotalDays / 7.0);
        if (weekNum < 1) weekNum = 1;
        return $"Week {weekNum}";
    }

    private string GetMonthLabel(DateTime date) => date.ToString("MMM yyyy");

    // ── Analytical Report Endpoints ─────────────────────────────────────

    [HttpPost("encounter-completion")]
    public async Task<IActionResult> GetEncounterCompletion([FromBody] AnalyticalReportRequestDto request)
    {
        try
        {
            var tenantId = GetTenantId();
            var (startDate, endDate) = NormalizeDateRange(request);
            var periodLength = endDate - startDate;
            var prevStart = startDate - periodLength;

            var encounters = await _context.Encounters
                .Include(e => e.Provider)
                .Where(e => e.TenantId == tenantId && e.EncounterDate >= DateOnly.FromDateTime(startDate) && e.EncounterDate < DateOnly.FromDateTime(endDate))
                .ToListAsync();

            if (request.ProviderId.HasValue)
                encounters = encounters.Where(e => e.ProviderId == request.ProviderId.Value).ToList();

            // Get notes linked to these encounters for Draft detection
            var encounterIds = encounters.Select(e => e.EncounterId).ToList();
            var notes = await _context.ClinicalNotes
                .Where(n => n.TenantId == tenantId && encounterIds.Contains(n.EncounterId ?? 0))
                .ToListAsync();

            // Decrypt provider PHI
            var providers = encounters.Select(e => e.Provider).Where(p => p != null).DistinctBy(p => p.ProviderId).ToList();
            foreach (var p in providers) _encryptionHelper.DecryptEntity(p);

            // Previous period count for comparison
            var prevCount = await _context.Encounters
                .Where(e => e.TenantId == tenantId && e.EncounterDate >= DateOnly.FromDateTime(prevStart) && e.EncounterDate < DateOnly.FromDateTime(startDate))
                .CountAsync();

            var grouped = encounters.GroupBy(e => e.ProviderId).Select(g =>
            {
                var provider = g.First().Provider;
                var providerName = provider != null ? $"{provider.LastName}, {provider.FirstName}" : "Unknown";
                var total = g.Count();
                var signed = g.Count(e => e.Status == 1 || e.Status == 2); // Signed or Locked
                var open = g.Count(e => e.Status == 0);
                var amended = g.Count(e => e.Status == 3);
                // Draft = encounters that have a note with status 0 (Draft)
                var draft = g.Count(e => notes.Any(n => n.EncounterId == e.EncounterId && n.Status == 0));
                var completionRate = total > 0 ? Math.Round((decimal)signed / total * 100, 1) : 0;
                var signedEncounters = g.Where(e => e.SignedAt.HasValue);
                var avgDays = signedEncounters.Any() ? Math.Round((decimal)signedEncounters.Average(e => (e.SignedAt!.Value - e.CreatedAt).TotalDays), 1) : 0;

                return new EncounterCompletionRowDto
                {
                    ProviderId = g.Key,
                    ProviderName = providerName,
                    TotalEncounters = total,
                    Signed = signed,
                    Open = open,
                    Draft = draft,
                    Amended = amended,
                    CompletionRate = completionRate,
                    AvgDaysToSign = avgDays
                };
            }).OrderByDescending(r => r.TotalEncounters).ToList();

            var totalEnc = encounters.Count;
            var totalSigned = grouped.Sum(r => r.Signed);
            var totalOpen = grouped.Sum(r => r.Open);
            var overallRate = totalEnc > 0 ? Math.Round((decimal)totalSigned / totalEnc * 100, 1) : 0;
            var prevChange = prevCount > 0 ? Math.Round((decimal)(totalEnc - prevCount) / prevCount * 100, 1) : (decimal?)null;

            var avgDaysOverall = grouped.Where(r => r.AvgDaysToSign > 0).Select(r => r.AvgDaysToSign);
            var avgDaysVal = avgDaysOverall.Any() ? Math.Round(avgDaysOverall.Average(), 1) : 0;

            return Ok(new EncounterCompletionReportDto
            {
                Kpis = new List<ReportKpiDto>
                {
                    new() { Label = "Total Encounters", Value = totalEnc.ToString(), PreviousValue = prevCount.ToString(), ChangePercent = prevChange },
                    new() { Label = "Signed %", Value = $"{overallRate}%" },
                    new() { Label = "Open/Draft", Value = $"{totalOpen + grouped.Sum(r => r.Draft)}" },
                    new() { Label = "Avg Days to Sign", Value = $"{avgDaysVal}" }
                },
                ChartData = grouped.Select(r => new EncounterCompletionChartDto { ProviderName = r.ProviderName, Signed = r.Signed, Open = r.Open, Draft = r.Draft }).ToList(),
                Rows = grouped
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating encounter completion report");
            return StatusCode(500, "Error generating report");
        }
    }

    [HttpPost("orders-tracking")]
    public async Task<IActionResult> GetOrdersTracking([FromBody] AnalyticalReportRequestDto request)
    {
        try
        {
            var tenantId = GetTenantId();
            var (startDate, endDate) = NormalizeDateRange(request);

            var orders = await _context.Orders
                .Where(o => o.TenantId == tenantId && o.OrderDate >= DateOnly.FromDateTime(startDate) && o.OrderDate < DateOnly.FromDateTime(endDate))
                .ToListAsync();

            if (request.ProviderId.HasValue)
                orders = orders.Where(o => o.ProviderId == request.ProviderId.Value).ToList();

            var typeNames = new Dictionary<int, string> { { 0, "Lab" }, { 1, "Imaging" }, { 2, "Referral" } };

            var grouped = new[] { 0, 1, 2 }.Select(type =>
            {
                var typeOrders = orders.Where(o => o.OrderType == type).ToList();
                var total = typeOrders.Count;
                var pending = typeOrders.Count(o => o.Status == 1);
                var sent = typeOrders.Count(o => o.Status == 2);
                var inProgress = typeOrders.Count(o => o.Status == 3);
                var results = typeOrders.Count(o => o.Status == 4);
                var completed = typeOrders.Count(o => o.Status == 5);
                var cancelled = typeOrders.Count(o => o.Status == 6);
                var completedWithDates = typeOrders.Where(o => o.Status == 5 && o.CompletedAt.HasValue);
                var avgTurnaround = completedWithDates.Any() ? Math.Round((decimal)completedWithDates.Average(o => (o.CompletedAt!.Value - o.CreatedAt).TotalDays), 1) : 0;

                return new OrdersTrackingRowDto
                {
                    OrderType = typeNames[type],
                    TotalOrdered = total,
                    Pending = pending + sent + inProgress,
                    Sent = sent,
                    ResultsReceived = results,
                    Completed = completed,
                    Cancelled = cancelled,
                    AvgTurnaroundDays = avgTurnaround
                };
            }).ToList();

            var totalOrders = orders.Count;
            var totalPending = orders.Count(o => o.Status >= 1 && o.Status <= 3);
            var allCompleted = orders.Where(o => o.Status == 5 && o.CompletedAt.HasValue);
            var avgTurnAll = allCompleted.Any() ? Math.Round((decimal)allCompleted.Average(o => (o.CompletedAt!.Value - o.CreatedAt).TotalDays), 1) : 0;
            var statOrders = orders.Count(o => o.Priority == 2);

            return Ok(new OrdersTrackingReportDto
            {
                Kpis = new List<ReportKpiDto>
                {
                    new() { Label = "Total Orders", Value = totalOrders.ToString() },
                    new() { Label = "Pending Results", Value = totalPending.ToString() },
                    new() { Label = "Avg Turnaround (days)", Value = $"{avgTurnAll}" },
                    new() { Label = "STAT Orders", Value = statOrders.ToString() }
                },
                ChartData = grouped.Select(r => new OrdersTrackingChartDto { OrderType = r.OrderType, Pending = r.Pending, Sent = r.Sent, ResultsReceived = r.ResultsReceived, Completed = r.Completed, Cancelled = r.Cancelled }).ToList(),
                Rows = grouped
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating orders tracking report");
            return StatusCode(500, "Error generating report");
        }
    }

    [HttpPost("prescription-analytics")]
    public async Task<IActionResult> GetPrescriptionAnalytics([FromBody] AnalyticalReportRequestDto request)
    {
        try
        {
            var tenantId = GetTenantId();
            var (startDate, endDate) = NormalizeDateRange(request);

            var prescriptions = await _context.Prescriptions
                .Include(p => p.Provider)
                .Where(p => p.TenantId == tenantId && p.PrescribedDate >= DateOnly.FromDateTime(startDate) && p.PrescribedDate < DateOnly.FromDateTime(endDate))
                .ToListAsync();

            if (request.ProviderId.HasValue)
                prescriptions = prescriptions.Where(p => p.ProviderId == request.ProviderId.Value).ToList();

            // Decrypt provider PHI
            var rxProviders = prescriptions.Select(p => p.Provider).Where(p => p != null).DistinctBy(p => p.ProviderId).ToList();
            foreach (var p in rxProviders) _encryptionHelper.DecryptEntity(p);

            var grouped = prescriptions.GroupBy(p => p.DrugName ?? "Unknown").Select(g =>
            {
                var topProvider = g.GroupBy(p => p.ProviderId).OrderByDescending(pg => pg.Count()).FirstOrDefault();
                var topProviderEntity = topProvider?.First().Provider;
                var topPrescriberName = topProviderEntity != null ? $"{topProviderEntity.LastName}, {topProviderEntity.FirstName}" : "";

                return new PrescriptionAnalyticsRowDto
                {
                    DrugName = g.Key,
                    GenericName = g.First().GenericName ?? "",
                    TimesPrescribed = g.Count(),
                    AvgQuantity = Math.Round((decimal)g.Average(p => p.Quantity), 1),
                    AvgRefills = Math.Round((decimal)g.Average(p => p.Refills), 1),
                    ControlledCount = g.Count(p => p.IsControlledSubstance == true),
                    TopPrescriber = topPrescriberName
                };
            }).OrderByDescending(r => r.TimesPrescribed).ToList();

            var totalRx = prescriptions.Count;
            var controlledPct = totalRx > 0 ? Math.Round((decimal)prescriptions.Count(p => p.IsControlledSubstance == true) / totalRx * 100, 1) : 0;
            var avgRefills = totalRx > 0 ? Math.Round((decimal)prescriptions.Average(p => p.Refills), 1) : 0;
            var topDrug = grouped.FirstOrDefault()?.DrugName ?? "N/A";

            return Ok(new PrescriptionAnalyticsReportDto
            {
                Kpis = new List<ReportKpiDto>
                {
                    new() { Label = "Total Rx Written", Value = totalRx.ToString() },
                    new() { Label = "Controlled %", Value = $"{controlledPct}%" },
                    new() { Label = "Avg Refills", Value = $"{avgRefills}" },
                    new() { Label = "Top Drug", Value = topDrug }
                },
                ChartData = grouped.Take(10).Select(r => new PrescriptionAnalyticsChartDto { DrugName = r.DrugName, Count = r.TimesPrescribed }).ToList(),
                Rows = grouped
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating prescription analytics report");
            return StatusCode(500, "Error generating report");
        }
    }

    [HttpPost("chronic-disease")]
    public async Task<IActionResult> GetChronicDisease([FromBody] AnalyticalReportRequestDto request)
    {
        try
        {
            var tenantId = GetTenantId();

            var problems = await _context.PatientProblems
                .Where(p => p.TenantId == tenantId)
                .ToListAsync();

            var grouped = problems.GroupBy(p => p.IcdCode ?? "Unknown").Select(g =>
            {
                var active = g.Count(p => p.Status == 0);
                var resolved = g.Count(p => p.Status == 1);
                var withDuration = g.Where(p => p.Status == 1 && p.OnsetDate.HasValue && p.ResolvedDate.HasValue);
                var avgDuration = withDuration.Any() ? Math.Round((decimal)withDuration.Average(p => (p.ResolvedDate!.Value.ToDateTime(TimeOnly.MinValue) - p.OnsetDate!.Value.ToDateTime(TimeOnly.MinValue)).TotalDays), 1) : 0;

                return new ChronicDiseaseRowDto
                {
                    IcdCode = g.Key,
                    Description = g.First().Description ?? "",
                    ActivePatientCount = active,
                    ResolvedCount = resolved,
                    AvgDurationDays = avgDuration
                };
            }).OrderByDescending(r => r.ActivePatientCount).ToList();

            var totalActive = problems.Count(p => p.Status == 0);
            var uniqueCodes = grouped.Count;
            var patientsWithMultiple = problems.Where(p => p.Status == 0).GroupBy(p => p.PatientId).Count(g => g.Count() >= 3);
            var mostCommon = grouped.FirstOrDefault()?.Description ?? "N/A";

            return Ok(new ChronicDiseaseReportDto
            {
                Kpis = new List<ReportKpiDto>
                {
                    new() { Label = "Active Problems", Value = totalActive.ToString() },
                    new() { Label = "Unique ICD Codes", Value = uniqueCodes.ToString() },
                    new() { Label = "Patients w/ 3+", Value = patientsWithMultiple.ToString() },
                    new() { Label = "Most Common", Value = mostCommon }
                },
                ChartData = grouped.Take(15).Select(r => new ChronicDiseaseChartDto { IcdCode = r.IcdCode, Description = r.Description, ActivePatientCount = r.ActivePatientCount }).ToList(),
                Rows = grouped
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating chronic disease report");
            return StatusCode(500, "Error generating report");
        }
    }

    [HttpPost("visit-volume")]
    public async Task<IActionResult> GetVisitVolume([FromBody] AnalyticalReportRequestDto request)
    {
        try
        {
            var tenantId = GetTenantId();
            var (startDate, endDate) = NormalizeDateRange(request);
            var periodLength = endDate - startDate;
            var prevStart = startDate - periodLength;

            var query = _context.Appointments
                .Where(a => a.TenantId == tenantId && a.StartTime >= startDate && a.StartTime < endDate);

            if (request.ProviderId.HasValue)
                query = query.Where(a => a.ProviderId == request.ProviderId.Value);
            if (request.LocationId.HasValue)
                query = query.Where(a => a.LocationId == request.LocationId.Value);

            var appointments = await query.ToListAsync();

            var prevCount = await _context.Appointments
                .Where(a => a.TenantId == tenantId && a.StartTime >= prevStart && a.StartTime < startDate)
                .CountAsync();

            var grouped = appointments.GroupBy(a => GetWeekLabel(a.StartTime, startDate)).Select(g =>
            {
                var total = g.Count();
                var completed = g.Count(a => a.Status >= 2 && a.Status <= 4);
                var noshow = g.Count(a => a.Status == 5 || a.Status == 8);
                var cancelled = g.Count(a => a.Status == 6);
                return new VisitVolumeRowDto
                {
                    Period = g.Key,
                    TotalScheduled = total,
                    Completed = completed,
                    NoShow = noshow,
                    Cancelled = cancelled,
                    CompletionRate = total > 0 ? Math.Round((decimal)completed / total * 100, 1) : 0,
                    NewPatient = g.Count(a => a.Type == 0),
                    Telehealth = g.Count(a => a.IsTelehealth == true)
                };
            }).OrderBy(r => r.Period).ToList();

            var totalVisits = appointments.Count;
            var totalCompleted = appointments.Count(a => a.Status >= 2 && a.Status <= 4);
            var completionRate = totalVisits > 0 ? Math.Round((decimal)totalCompleted / totalVisits * 100, 1) : 0;
            var newPct = totalVisits > 0 ? Math.Round((decimal)appointments.Count(a => a.Type == 0) / totalVisits * 100, 1) : 0;
            var telePct = totalVisits > 0 ? Math.Round((decimal)appointments.Count(a => a.IsTelehealth == true) / totalVisits * 100, 1) : 0;
            var prevChange = prevCount > 0 ? Math.Round((decimal)(totalVisits - prevCount) / prevCount * 100, 1) : (decimal?)null;

            return Ok(new VisitVolumeReportDto
            {
                Kpis = new List<ReportKpiDto>
                {
                    new() { Label = "Total Visits", Value = totalVisits.ToString(), PreviousValue = prevCount.ToString(), ChangePercent = prevChange },
                    new() { Label = "Completion Rate", Value = $"{completionRate}%" },
                    new() { Label = "New Patient %", Value = $"{newPct}%" },
                    new() { Label = "Telehealth %", Value = $"{telePct}%" }
                },
                ChartData = grouped.Select(r => new VisitVolumeChartDto { Label = r.Period, Completed = r.Completed, NoShow = r.NoShow, Cancelled = r.Cancelled }).ToList(),
                Rows = grouped
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating visit volume report");
            return StatusCode(500, "Error generating report");
        }
    }

    [HttpPost("noshow-rate")]
    public async Task<IActionResult> GetNoShowRate([FromBody] AnalyticalReportRequestDto request)
    {
        try
        {
            var tenantId = GetTenantId();
            var (startDate, endDate) = NormalizeDateRange(request);

            var query = _context.Appointments
                .Include(a => a.Patient)
                .Where(a => a.TenantId == tenantId && a.StartTime >= startDate && a.StartTime < endDate);

            if (request.ProviderId.HasValue)
                query = query.Where(a => a.ProviderId == request.ProviderId.Value);
            if (request.LocationId.HasValue)
                query = query.Where(a => a.LocationId == request.LocationId.Value);

            var appointments = await query.ToListAsync();

            // Decrypt patient PHI
            var patients = appointments.Select(a => a.Patient).Where(p => p != null).DistinctBy(p => p.PatientId).ToList();
            foreach (var p in patients) _encryptionHelper.DecryptEntity(p);

            // Get insurance map
            var patientIds = patients.Select(p => p.PatientId).ToList();
            var insuranceMap = await _context.Insurances
                .Where(i => i.TenantId == tenantId && patientIds.Contains(i.PatientId) && i.IsActive == true && i.Type == 0)
                .GroupBy(i => i.PatientId)
                .Select(g => new { g.Key, PayerName = g.First().PayerName })
                .ToDictionaryAsync(x => x.Key, x => x.PayerName);

            var totalAppts = appointments.Count;
            var totalNoShow = appointments.Count(a => a.Status == 5 || a.Status == 8);
            var totalCancelled = appointments.Count(a => a.Status == 6);
            var noShowRate = totalAppts > 0 ? Math.Round((decimal)totalNoShow / totalAppts * 100, 1) : 0;
            var cancelRate = totalAppts > 0 ? Math.Round((decimal)totalCancelled / totalAppts * 100, 1) : 0;

            // By day of week
            var dayNames = new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
            var byDay = appointments.GroupBy(a => a.StartTime.DayOfWeek).Select(g =>
            {
                var total = g.Count();
                var noShows = g.Count(a => a.Status == 5 || a.Status == 8);
                return new NoShowByDayDto { DayOfWeek = dayNames[(int)g.Key], Total = total, NoShows = noShows, Rate = total > 0 ? Math.Round((decimal)noShows / total * 100, 1) : 0 };
            }).OrderBy(d => Array.IndexOf(dayNames, d.DayOfWeek)).ToList();

            var worstDay = byDay.OrderByDescending(d => d.Rate).FirstOrDefault()?.DayOfWeek ?? "N/A";

            // Estimated revenue loss ($150 per missed visit as default)
            var estimatedLoss = totalNoShow * 150m;

            // Frequent offenders
            var patientGroups = appointments.GroupBy(a => a.PatientId).Where(g => g.Any(a => a.Status == 5 || a.Status == 8)).Select(g =>
            {
                var patient = g.First().Patient;
                var name = patient != null ? $"{patient.LastName}, {patient.FirstName}" : "Unknown";
                var total = g.Count();
                var noShows = g.Count(a => a.Status == 5 || a.Status == 8);
                var cancels = g.Count(a => a.Status == 6);
                var lastNs = g.Where(a => a.Status == 5 || a.Status == 8).Max(a => (DateTime?)a.StartTime);

                return new NoShowRateRowDto
                {
                    PatientId = g.Key,
                    PatientName = name,
                    NoShowCount = noShows,
                    CancellationCount = cancels,
                    TotalAppointments = total,
                    Rate = total > 0 ? Math.Round((decimal)noShows / total * 100, 1) : 0,
                    LastNoShow = lastNs,
                    Insurance = insuranceMap.GetValueOrDefault(g.Key, "Self-Pay")
                };
            }).OrderByDescending(r => r.NoShowCount).ToList();

            return Ok(new NoShowRateReportDto
            {
                Kpis = new List<ReportKpiDto>
                {
                    new() { Label = "No-Show Rate", Value = $"{noShowRate}%" },
                    new() { Label = "Cancellation Rate", Value = $"{cancelRate}%" },
                    new() { Label = "Est. Revenue Loss", Value = $"${estimatedLoss:N0}" },
                    new() { Label = "Worst Day", Value = worstDay }
                },
                ByDayOfWeek = byDay,
                Rows = patientGroups
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating no-show rate report");
            return StatusCode(500, "Error generating report");
        }
    }

    [HttpPost("revenue-claims")]
    public async Task<IActionResult> GetRevenueClaims([FromBody] AnalyticalReportRequestDto request)
    {
        try
        {
            var tenantId = GetTenantId();
            var (startDate, endDate) = NormalizeDateRange(request);

            var claims = await _context.BillingClaims
                .Where(c => c.TenantId == tenantId && c.ServiceDateFrom >= DateOnly.FromDateTime(startDate) && c.ServiceDateFrom < DateOnly.FromDateTime(endDate))
                .ToListAsync();

            if (request.ProviderId.HasValue)
                claims = claims.Where(c => c.ProviderId == request.ProviderId.Value).ToList();

            var grouped = claims.GroupBy(c => c.ServiceDateFrom.ToString("MMM yyyy")).Select(g =>
            {
                var totalBilled = g.Sum(c => c.TotalCharged);
                var totalPaid = g.Sum(c => c.TotalPaid ?? 0);
                var denied = g.Where(c => c.Status == 7 || c.Status == 8).Sum(c => c.TotalCharged);
                var pending = g.Where(c => c.Status >= 0 && c.Status <= 4).Sum(c => c.TotalCharged);
                var collectionRate = totalBilled > 0 ? Math.Round(totalPaid / totalBilled * 100, 1) : 0;
                var withDates = g.Where(c => c.SubmittedAt.HasValue && c.ProcessedAt.HasValue);
                var avgDays = withDates.Any() ? Math.Round((decimal)withDates.Average(c => (c.ProcessedAt!.Value - c.SubmittedAt!.Value).TotalDays), 1) : 0;

                return new RevenueClaimsRowDto
                {
                    Period = g.Key,
                    TotalClaims = g.Count(),
                    TotalBilled = totalBilled,
                    TotalPaid = totalPaid,
                    TotalDenied = denied,
                    TotalPending = pending,
                    CollectionRate = collectionRate,
                    AvgDaysToPayment = avgDays
                };
            }).ToList();

            var totalBilledAll = claims.Sum(c => c.TotalCharged);
            var totalPaidAll = claims.Sum(c => c.TotalPaid ?? 0);
            var overallCollectionRate = totalBilledAll > 0 ? Math.Round(totalPaidAll / totalBilledAll * 100, 1) : 0;
            var allWithDates = claims.Where(c => c.SubmittedAt.HasValue && c.ProcessedAt.HasValue);
            var avgDaysAll = allWithDates.Any() ? Math.Round((decimal)allWithDates.Average(c => (c.ProcessedAt!.Value - c.SubmittedAt!.Value).TotalDays), 1) : 0;

            return Ok(new RevenueClaimsReportDto
            {
                Kpis = new List<ReportKpiDto>
                {
                    new() { Label = "Total Billed", Value = $"${totalBilledAll:N0}" },
                    new() { Label = "Total Collected", Value = $"${totalPaidAll:N0}" },
                    new() { Label = "Collection Rate", Value = $"{overallCollectionRate}%" },
                    new() { Label = "Avg Days to Pay", Value = $"{avgDaysAll}" }
                },
                ChartData = grouped.Select(r => new RevenueClaimsChartDto { Label = r.Period, TotalBilled = r.TotalBilled, TotalPaid = r.TotalPaid, TotalDenied = r.TotalDenied }).ToList(),
                Rows = grouped
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating revenue claims report");
            return StatusCode(500, "Error generating report");
        }
    }

    [HttpPost("payer-mix")]
    public async Task<IActionResult> GetPayerMix([FromBody] AnalyticalReportRequestDto request)
    {
        try
        {
            var tenantId = GetTenantId();
            var (startDate, endDate) = NormalizeDateRange(request);

            var insurances = await _context.Insurances
                .Where(i => i.TenantId == tenantId && i.IsActive == true)
                .ToListAsync();

            var patientIds = insurances.Select(i => i.PatientId).Distinct().ToList();
            var completedStatuses = new[] { 2, 3, 4 };

            var appointments = await _context.Appointments
                .Where(a => a.TenantId == tenantId && a.StartTime >= startDate && a.StartTime < endDate && completedStatuses.Contains(a.Status ?? -1))
                .ToListAsync();

            var claims = await _context.BillingClaims
                .Where(c => c.TenantId == tenantId && c.ServiceDateFrom >= DateOnly.FromDateTime(startDate) && c.ServiceDateFrom < DateOnly.FromDateTime(endDate))
                .ToListAsync();

            var totalVisits = appointments.Count;

            var grouped = insurances.GroupBy(i => i.PayerName ?? "Unknown").Select(g =>
            {
                var pids = g.Select(i => i.PatientId).Distinct().ToList();
                var visits = appointments.Count(a => pids.Contains(a.PatientId));
                var payerClaims = claims.Where(c => g.Select(i => i.InsuranceId).Contains(c.InsuranceId ?? 0));
                var billed = payerClaims.Sum(c => c.TotalCharged);
                var paid = payerClaims.Sum(c => c.TotalPaid ?? 0);
                var avgReimb = visits > 0 ? Math.Round(paid / visits, 2) : 0;

                return new PayerMixRowDto
                {
                    PayerName = g.Key,
                    ActivePatients = pids.Count,
                    TotalVisits = visits,
                    TotalBilled = billed,
                    TotalPaid = paid,
                    AvgReimbursement = avgReimb,
                    Percentage = totalVisits > 0 ? Math.Round((decimal)visits / totalVisits * 100, 1) : 0
                };
            }).OrderByDescending(r => r.TotalVisits).ToList();

            var totalPatients = insurances.Select(i => i.PatientId).Distinct().Count();
            var topPayer = grouped.FirstOrDefault()?.PayerName ?? "N/A";
            // Self-pay = patients with no active insurance
            var allPatients = await _context.Patients.Where(p => p.TenantId == tenantId && p.IsDeleted != true).CountAsync();
            var insuredPatients = insurances.Select(i => i.PatientId).Distinct().Count();
            var selfPayPct = allPatients > 0 ? Math.Round((decimal)(allPatients - insuredPatients) / allPatients * 100, 1) : 0;
            var avgReimbAll = totalVisits > 0 ? Math.Round(claims.Sum(c => c.TotalPaid ?? 0) / totalVisits, 2) : 0;

            return Ok(new PayerMixReportDto
            {
                Kpis = new List<ReportKpiDto>
                {
                    new() { Label = "Active Patients", Value = totalPatients.ToString() },
                    new() { Label = "Top Payer", Value = topPayer },
                    new() { Label = "Self-Pay %", Value = $"{selfPayPct}%" },
                    new() { Label = "Avg Reimbursement", Value = $"${avgReimbAll:N2}" }
                },
                ChartData = grouped.Select(r => new PayerMixChartDto { PayerName = r.PayerName, VisitCount = r.TotalVisits, Percentage = r.Percentage }).ToList(),
                Rows = grouped
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating payer mix report");
            return StatusCode(500, "Error generating report");
        }
    }

    [HttpPost("provider-productivity")]
    public async Task<IActionResult> GetProviderProductivity([FromBody] AnalyticalReportRequestDto request)
    {
        try
        {
            var tenantId = GetTenantId();
            var (startDate, endDate) = NormalizeDateRange(request);

            var providersList = await _context.Providers
                .Where(p => p.TenantId == tenantId && p.IsActive == true)
                .ToListAsync();

            foreach (var p in providersList) _encryptionHelper.DecryptEntity(p);

            if (request.ProviderId.HasValue)
                providersList = providersList.Where(p => p.ProviderId == request.ProviderId.Value).ToList();

            var providerIds = providersList.Select(p => p.ProviderId).ToList();

            var appointments = await _context.Appointments
                .Where(a => a.TenantId == tenantId && providerIds.Contains(a.ProviderId) && a.StartTime >= startDate && a.StartTime < endDate)
                .ToListAsync();

            var encounters = await _context.Encounters
                .Where(e => e.TenantId == tenantId && providerIds.Contains(e.ProviderId) && e.EncounterDate >= DateOnly.FromDateTime(startDate) && e.EncounterDate < DateOnly.FromDateTime(endDate))
                .ToListAsync();

            var prescriptions = await _context.Prescriptions
                .Where(p => p.TenantId == tenantId && providerIds.Contains(p.ProviderId) && p.PrescribedDate >= DateOnly.FromDateTime(startDate) && p.PrescribedDate < DateOnly.FromDateTime(endDate))
                .ToListAsync();

            var orders = await _context.Orders
                .Where(o => o.TenantId == tenantId && providerIds.Contains(o.ProviderId) && o.OrderDate >= DateOnly.FromDateTime(startDate) && o.OrderDate < DateOnly.FromDateTime(endDate))
                .ToListAsync();

            var totalDays = Math.Max(1, (endDate - startDate).Days);

            var rows = providersList.Select(prov =>
            {
                var provAppts = appointments.Where(a => a.ProviderId == prov.ProviderId).ToList();
                var provEnc = encounters.Where(e => e.ProviderId == prov.ProviderId).ToList();
                var provRx = prescriptions.Where(p => p.ProviderId == prov.ProviderId).ToList();
                var provOrd = orders.Where(o => o.ProviderId == prov.ProviderId).ToList();

                var scheduled = provAppts.Count;
                var completed = provAppts.Count(a => a.Status >= 2 && a.Status <= 4);
                var noShows = provAppts.Count(a => a.Status == 5 || a.Status == 8);
                var activePatients = provAppts.Where(a => a.Status >= 2 && a.Status <= 4).Select(a => a.PatientId).Distinct().Count();

                return new ProviderProductivityRowDto
                {
                    ProviderId = prov.ProviderId,
                    ProviderName = $"{prov.LastName}, {prov.FirstName}",
                    Specialty = prov.Specialty ?? "",
                    TotalScheduled = scheduled,
                    Completed = completed,
                    NoShows = noShows,
                    CompletionRate = scheduled > 0 ? Math.Round((decimal)completed / scheduled * 100, 1) : 0,
                    EncountersCreated = provEnc.Count,
                    EncountersSigned = provEnc.Count(e => e.Status >= 1),
                    PrescriptionsWritten = provRx.Count,
                    OrdersPlaced = provOrd.Count,
                    ActivePatients = activePatients
                };
            }).OrderByDescending(r => r.Completed).ToList();

            var totalProviders = providersList.Count;
            var avgPatients = totalProviders > 0 ? Math.Round((decimal)rows.Sum(r => r.ActivePatients) / totalProviders, 1) : 0;
            var totalEncounters = encounters.Count;
            var avgEncPerDay = totalDays > 0 ? Math.Round((decimal)totalEncounters / totalDays, 1) : 0;
            var totalScheduled = appointments.Count;
            var totalCompleted = appointments.Count(a => a.Status >= 2 && a.Status <= 4);
            var overallCompletion = totalScheduled > 0 ? Math.Round((decimal)totalCompleted / totalScheduled * 100, 1) : 0;

            return Ok(new ProviderProductivityReportDto
            {
                Kpis = new List<ReportKpiDto>
                {
                    new() { Label = "Active Providers", Value = totalProviders.ToString() },
                    new() { Label = "Avg Patients/Provider", Value = $"{avgPatients}" },
                    new() { Label = "Avg Encounters/Day", Value = $"{avgEncPerDay}" },
                    new() { Label = "Completion Rate", Value = $"{overallCompletion}%" }
                },
                ChartData = rows.Select(r => new ProviderProductivityChartDto { ProviderName = r.ProviderName, CompletedVisits = r.Completed, EncountersSigned = r.EncountersSigned, NoShows = r.NoShows }).ToList(),
                Rows = rows
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating provider productivity report");
            return StatusCode(500, "Error generating report");
        }
    }

    [HttpPost("patient-panel")]
    public async Task<IActionResult> GetPatientPanel([FromBody] AnalyticalReportRequestDto request)
    {
        try
        {
            var tenantId = GetTenantId();
            var (startDate, endDate) = NormalizeDateRange(request);

            var patientsList = await _context.Patients
                .Where(p => p.TenantId == tenantId && p.IsDeleted != true)
                .ToListAsync();

            foreach (var p in patientsList) _encryptionHelper.DecryptEntity(p);

            var patientIds = patientsList.Select(p => p.PatientId).ToList();
            var completedStatuses = new[] { 2, 3, 4 };

            var appointments = await _context.Appointments
                .Where(a => a.TenantId == tenantId && completedStatuses.Contains(a.Status ?? -1) && patientIds.Contains(a.PatientId))
                .ToListAsync();

            var apptsByPatient = appointments.GroupBy(a => a.PatientId).ToDictionary(g => g.Key, g => g.ToList());

            var now = DateTime.UtcNow;
            var ninetyDaysAgo = now.AddDays(-90);

            var totalPatients = patientsList.Count;
            var newPatients = patientsList.Count(p => p.CreatedAt.HasValue && p.CreatedAt.Value >= startDate && p.CreatedAt.Value < endDate);
            var seenInPeriod = appointments.Where(a => a.StartTime >= startDate && a.StartTime < endDate).Select(a => a.PatientId).Distinct().Count();
            var notSeenIn90 = patientsList.Count(p => {
                if (!apptsByPatient.ContainsKey(p.PatientId)) return true;
                var lastAppt = apptsByPatient[p.PatientId].Max(a => a.StartTime);
                return lastAppt < ninetyDaysAgo;
            });

            var ageGroups = patientsList.GroupBy(p => GetAgeGroup(p.DateOfBirth)).Select(g =>
            {
                var pids = g.Select(p => p.PatientId).ToList();
                var seen = appointments.Where(a => a.StartTime >= startDate && a.StartTime < endDate && pids.Contains(a.PatientId)).Select(a => a.PatientId).Distinct().Count();
                var notSeen = pids.Count(pid => {
                    if (!apptsByPatient.ContainsKey(pid)) return true;
                    var lastAppt = apptsByPatient[pid].Max(a => a.StartTime);
                    return lastAppt < ninetyDaysAgo;
                });
                var totalVisitsInGroup = appointments.Where(a => a.StartTime >= startDate && a.StartTime < endDate && pids.Contains(a.PatientId)).Count();
                var avgVisits = pids.Count > 0 ? Math.Round((decimal)totalVisitsInGroup / pids.Count, 1) : 0;

                return new PatientPanelRowDto
                {
                    AgeGroup = g.Key,
                    TotalPatients = g.Count(),
                    SeenInPeriod = seen,
                    NotSeenIn90Days = notSeen,
                    AvgVisitsPerPatient = avgVisits
                };
            }).OrderBy(r => r.AgeGroup switch { "0-17" => 0, "18-34" => 1, "35-49" => 2, "50-64" => 3, "65+" => 4, _ => 5 }).ToList();

            return Ok(new PatientPanelReportDto
            {
                Kpis = new List<ReportKpiDto>
                {
                    new() { Label = "Total Patients", Value = totalPatients.ToString() },
                    new() { Label = "New This Period", Value = newPatients.ToString() },
                    new() { Label = "Seen in Period", Value = seenInPeriod.ToString() },
                    new() { Label = "Not Seen 90+ Days", Value = notSeenIn90.ToString() }
                },
                ChartData = ageGroups.Select(r => new PatientPanelChartDto { AgeGroup = r.AgeGroup, TotalPatients = r.TotalPatients }).ToList(),
                Rows = ageGroups
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating patient panel report");
            return StatusCode(500, "Error generating report");
        }
    }

    // ── Financial Reports (Copay/Ledger/Installment) ─────────────────────

    [HttpPost("copay-collection")]
    public async Task<IActionResult> GetCopayCollection([FromBody] AnalyticalReportRequestDto request)
    {
        try
        {
            var tenantId = GetTenantId();
            var (startDate, endDate) = NormalizeDateRange(request);

            // Get all charges in date range
            var charges = await _context.Charges
                .Where(c => c.TenantId == tenantId && c.ServiceDate >= DateOnly.FromDateTime(startDate) && c.ServiceDate < DateOnly.FromDateTime(endDate))
                .ToListAsync();

            // Get all completed payments in date range
            var payments = await _context.Payments
                .Where(p => p.TenantId == tenantId && (p.Status == null || p.Status == 1) && p.PaymentDate >= DateOnly.FromDateTime(startDate) && p.PaymentDate < DateOnly.FromDateTime(endDate))
                .ToListAsync();

            // Get patients with active installment plans
            var activePlanPatients = await _context.InstallmentPlans
                .Where(ip => ip.TenantId == tenantId && ip.Status == 0)
                .Select(ip => ip.PatientId)
                .Distinct()
                .ToListAsync();

            // Get patient names
            var patientIds = charges.Select(c => c.PatientId).Union(payments.Select(p => p.PatientId)).Distinct().ToList();
            var patients = await _context.Patients
                .Where(p => p.TenantId == tenantId && patientIds.Contains(p.PatientId))
                .ToListAsync();
            foreach (var p in patients) _encryptionHelper.DecryptEntity(p);

            var patientMap = patients.ToDictionary(p => p.PatientId, p => $"{p.LastName}, {p.FirstName}");

            // Per-patient summary
            var rows = patientIds.Select(pid =>
            {
                var patientCharges = charges.Where(c => c.PatientId == pid).ToList();
                var patientPayments = payments.Where(p => p.PatientId == pid).ToList();
                var totalCharges = patientCharges.Sum(c => c.ChargeAmount);
                var insurancePaid = patientPayments.Where(p => p.Type == 4).Sum(p => p.Amount);
                var patientPaid = patientPayments.Where(p => p.Type != 4 && p.Type != 5).Sum(p => p.Amount);
                var adjustments = patientCharges.Sum(c => c.AdjustmentAmount ?? 0);
                var balance = totalCharges - insurancePaid - patientPaid - adjustments;
                var lastPayment = patientPayments.Where(p => p.Type != 4).OrderByDescending(p => p.PaymentDate).FirstOrDefault();

                return new CopayCollectionRowDto
                {
                    PatientId = pid,
                    PatientName = patientMap.GetValueOrDefault(pid, "Unknown"),
                    TotalCharges = totalCharges,
                    InsurancePaid = insurancePaid,
                    PatientPaid = patientPaid,
                    Adjustments = adjustments,
                    Balance = Math.Max(0, balance),
                    HasInstallmentPlan = activePlanPatients.Contains(pid),
                    LastPaymentDate = lastPayment?.PaymentDate.ToString("MM/dd/yyyy") ?? ""
                };
            }).Where(r => r.TotalCharges > 0).OrderByDescending(r => r.Balance).ToList();

            var totalOwed = rows.Sum(r => r.Balance);
            var totalCollected = rows.Sum(r => r.PatientPaid);
            var collectionRate = (totalOwed + totalCollected) > 0 ? Math.Round(totalCollected / (totalOwed + totalCollected) * 100, 1) : 0;
            var onPlan = rows.Count(r => r.HasInstallmentPlan);

            // Chart: monthly owed vs collected
            var allMonths = charges.Select(c => c.ServiceDate.ToString("MMM yyyy")).Union(
                payments.Where(p => p.Type != 4).Select(p => p.PaymentDate.ToString("MMM yyyy"))).Distinct().ToList();
            var chartData = allMonths.Select(m =>
            {
                var mCharges = charges.Where(c => c.ServiceDate.ToString("MMM yyyy") == m);
                var mPayments = payments.Where(p => p.Type != 4 && p.PaymentDate.ToString("MMM yyyy") == m);
                var owed = mCharges.Sum(c => c.PatientResponsibility ?? 0);
                var collected = mPayments.Sum(p => p.Amount);
                return new CopayCollectionChartDto { Label = m, TotalOwed = owed, TotalCollected = collected };
            }).ToList();

            return Ok(new CopayCollectionReportDto
            {
                Kpis = new List<ReportKpiDto>
                {
                    new() { Label = "Outstanding Balance", Value = $"${totalOwed:N0}" },
                    new() { Label = "Copay Collected", Value = $"${totalCollected:N0}" },
                    new() { Label = "Collection Rate", Value = $"{collectionRate}%" },
                    new() { Label = "On Payment Plan", Value = onPlan.ToString() }
                },
                ChartData = chartData,
                Rows = rows
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating copay collection report");
            return StatusCode(500, "Error generating report");
        }
    }

    [HttpPost("ar-aging")]
    public async Task<IActionResult> GetArAging([FromBody] AnalyticalReportRequestDto request)
    {
        try
        {
            var tenantId = GetTenantId();
            var today = DateTime.UtcNow;

            // Get all unpaid/partially-paid claims (status 0-4, 6 = not fully paid)
            var claims = await _context.BillingClaims
                .Include(c => c.Insurance)
                .Where(c => c.TenantId == tenantId && c.Status != null && c.Status != 5 && c.Status != 7 && c.Status != 8)
                .ToListAsync();

            if (request.ProviderId.HasValue)
                claims = claims.Where(c => c.ProviderId == request.ProviderId.Value).ToList();

            // Calculate age of each claim
            var agingData = claims.Select(c =>
            {
                var serviceDate = c.ServiceDateFrom.ToDateTime(TimeOnly.MinValue);
                var daysOld = (today - serviceDate).Days;
                var outstanding = c.TotalCharged - (c.TotalPaid ?? 0) - (c.TotalAdjustment ?? 0);
                var payerName = c.Insurance?.PayerName ?? "Self-Pay";
                return new { c.ClaimId, PayerName = payerName, DaysOld = daysOld, Outstanding = Math.Max(0, outstanding) };
            }).Where(x => x.Outstanding > 0).ToList();

            // Bucket chart data
            var buckets = new[] {
                ("0-30 Days", 0, 30),
                ("31-60 Days", 31, 60),
                ("61-90 Days", 61, 90),
                ("90+ Days", 91, int.MaxValue)
            };
            var chartData = buckets.Select(b => new ArAgingChartDto
            {
                Bucket = b.Item1,
                Amount = agingData.Where(x => x.DaysOld >= b.Item2 && x.DaysOld <= b.Item3).Sum(x => x.Outstanding),
                ClaimCount = agingData.Count(x => x.DaysOld >= b.Item2 && x.DaysOld <= b.Item3)
            }).ToList();

            // Group by payer
            var rows = agingData.GroupBy(x => x.PayerName).Select(g => new ArAgingRowDto
            {
                PayerName = g.Key,
                Current = g.Where(x => x.DaysOld <= 30).Sum(x => x.Outstanding),
                Days31To60 = g.Where(x => x.DaysOld >= 31 && x.DaysOld <= 60).Sum(x => x.Outstanding),
                Days61To90 = g.Where(x => x.DaysOld >= 61 && x.DaysOld <= 90).Sum(x => x.Outstanding),
                Over90 = g.Where(x => x.DaysOld > 90).Sum(x => x.Outstanding),
                Total = g.Sum(x => x.Outstanding),
                ClaimCount = g.Count()
            }).OrderByDescending(r => r.Total).ToList();

            var totalAr = agingData.Sum(x => x.Outstanding);
            var over90Total = chartData.Last().Amount;
            var over90Pct = totalAr > 0 ? Math.Round(over90Total / totalAr * 100, 1) : 0;
            var avgAge = agingData.Any() ? Math.Round((decimal)agingData.Average(x => x.DaysOld), 0) : 0;

            return Ok(new ArAgingReportDto
            {
                Kpis = new List<ReportKpiDto>
                {
                    new() { Label = "Total A/R", Value = $"${totalAr:N0}" },
                    new() { Label = "Open Claims", Value = agingData.Count.ToString() },
                    new() { Label = "Over 90 Days %", Value = $"{over90Pct}%" },
                    new() { Label = "Avg Claim Age", Value = $"{avgAge} days" }
                },
                ChartData = chartData,
                Rows = rows
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating A/R aging report");
            return StatusCode(500, "Error generating report");
        }
    }

    [HttpPost("payment-analysis")]
    public async Task<IActionResult> GetPaymentAnalysis([FromBody] AnalyticalReportRequestDto request)
    {
        try
        {
            var tenantId = GetTenantId();
            var (startDate, endDate) = NormalizeDateRange(request);
            var periodLength = endDate - startDate;
            var prevStart = startDate - periodLength;

            var payments = await _context.Payments
                .Where(p => p.TenantId == tenantId && (p.Status == null || p.Status == 1) && p.PaymentDate >= DateOnly.FromDateTime(startDate) && p.PaymentDate < DateOnly.FromDateTime(endDate))
                .ToListAsync();

            var prevPayments = await _context.Payments
                .Where(p => p.TenantId == tenantId && (p.Status == null || p.Status == 1) && p.PaymentDate >= DateOnly.FromDateTime(prevStart) && p.PaymentDate < DateOnly.FromDateTime(startDate))
                .ToListAsync();

            var typeNames = new Dictionary<int, string> { { 0, "Copay" }, { 1, "Coinsurance" }, { 2, "Deductible" }, { 3, "Self-Pay" }, { 4, "Insurance" }, { 5, "Refund" }, { 6, "Adjustment" } };
            var methodNames = new Dictionary<int, string> { { 0, "Cash" }, { 1, "Check" }, { 2, "Credit Card" }, { 3, "Debit Card" }, { 4, "EFT" }, { 5, "ERA" }, { 6, "Other" } };

            var totalAmount = payments.Sum(p => p.Amount);
            var prevTotal = prevPayments.Sum(p => p.Amount);
            var change = prevTotal > 0 ? Math.Round((totalAmount - prevTotal) / prevTotal * 100, 1) : (decimal?)null;

            var patientPayments = payments.Where(p => p.Type != 4 && p.Type != 5 && p.Type != 6).ToList();
            var insurancePayments = payments.Where(p => p.Type == 4).ToList();
            var patientTotal = patientPayments.Sum(p => p.Amount);
            var insuranceTotal = insurancePayments.Sum(p => p.Amount);
            var avgPayment = patientPayments.Any() ? Math.Round(patientPayments.Average(p => p.Amount), 2) : 0;

            // Chart: by payment type (doughnut)
            var chartData = payments.GroupBy(p => p.Type)
                .Select(g => new PaymentAnalysisChartDto
                {
                    Label = typeNames.GetValueOrDefault(g.Key, "Other"),
                    Amount = g.Sum(p => p.Amount)
                }).OrderByDescending(x => x.Amount).ToList();

            // Table: breakdown by type + method
            var rows = payments.GroupBy(p => new { p.Type, p.Method })
                .Select(g => new PaymentAnalysisRowDto
                {
                    PaymentType = typeNames.GetValueOrDefault(g.Key.Type, "Other"),
                    PaymentMethod = methodNames.GetValueOrDefault(g.Key.Method, "Other"),
                    Count = g.Count(),
                    TotalAmount = g.Sum(p => p.Amount),
                    AvgAmount = Math.Round(g.Average(p => p.Amount), 2),
                    Percentage = totalAmount > 0 ? Math.Round(g.Sum(p => p.Amount) / totalAmount * 100, 1) : 0
                }).OrderByDescending(r => r.TotalAmount).ToList();

            return Ok(new PaymentAnalysisReportDto
            {
                Kpis = new List<ReportKpiDto>
                {
                    new() { Label = "Total Payments", Value = $"${totalAmount:N0}", PreviousValue = $"${prevTotal:N0}", ChangePercent = change },
                    new() { Label = "Patient Payments", Value = $"${patientTotal:N0}" },
                    new() { Label = "Insurance Payments", Value = $"${insuranceTotal:N0}" },
                    new() { Label = "Avg Patient Payment", Value = $"${avgPayment:N2}" }
                },
                ChartData = chartData,
                Rows = rows
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating payment analysis report");
            return StatusCode(500, "Error generating report");
        }
    }

    // ── Existing Monthly Visit Grid Endpoint ────────────────────────────

    /// <summary>
    /// Get the Monthly Patient Visit Grid report data
    /// </summary>
    [HttpPost("monthly-visit-grid")]
    public async Task<ActionResult<MonthlyVisitGridResponseDto>> GetMonthlyVisitGrid([FromBody] MonthlyVisitGridRequestDto request)
    {
        try
        {
            // Validate request
            if (request.Month < 1 || request.Month > 12)
                return BadRequest(new { message = "Invalid month. Must be between 1 and 12." });
            if (request.Year < 2000 || request.Year > 2100)
                return BadRequest(new { message = "Invalid year." });

            // Get tenant from user claims
            var tenantIdClaim = User.FindFirst("TenantId")?.Value;
            int? tenantId = !string.IsNullOrEmpty(tenantIdClaim) ? int.Parse(tenantIdClaim) : null;

            // Get days in the requested month
            var daysInMonth = DateTime.DaysInMonth(request.Year, request.Month);
            var startDate = new DateTime(request.Year, request.Month, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            // Get clinic/agency name from tenant
            string agencyName = "MEDOCS";
            if (tenantId.HasValue)
            {
                var tenant = await _context.Tenants.FindAsync(tenantId.Value);
                agencyName = tenant?.Name ?? "MEDOCS";
            }

            // Build query for appointments in the month
            var appointmentsQuery = _context.Appointments
                .Include(a => a.Patient)
                .Include(a => a.Provider)
                .Include(a => a.CareEpisode)
                .Include(a => a.Location)
                .Where(a => a.StartTime >= startDate && a.StartTime <= endDate.AddDays(1))
                .AsQueryable();

            // Filter by tenant if applicable
            if (tenantId.HasValue)
            {
                appointmentsQuery = appointmentsQuery.Where(a => a.TenantId == tenantId.Value);
            }

            // Filter by provider if specified
            if (request.ProviderId.HasValue)
            {
                appointmentsQuery = appointmentsQuery.Where(a => a.ProviderId == request.ProviderId.Value);
            }

            // Get all appointments for the month
            var appointments = await appointmentsQuery.ToListAsync();

            // Decrypt PHI fields for patients and providers
            foreach (var appt in appointments)
            {
                if (appt.Patient != null)
                {
                    _encryptionHelper.DecryptEntity(appt.Patient);
                }
                if (appt.Provider != null)
                {
                    _encryptionHelper.DecryptEntity(appt.Provider);
                }
            }

            // Get all notes for these appointments to check documentation status
            var appointmentIds = appointments.Select(a => a.AppointmentId).ToList();
            var notes = await _context.ClinicalNotes
                .Where(n => n.AppointmentId.HasValue && appointmentIds.Contains(n.AppointmentId.Value))
                .Select(n => new { n.AppointmentId, n.Status })
                .ToListAsync();
            // Group by AppointmentId to handle multiple notes per appointment, taking the first status
            var notesByAppointment = notes
                .GroupBy(n => n.AppointmentId.Value)
                .ToDictionary(g => g.Key, g => g.First().Status);

            // Group appointments by patient
            var patientAppointments = appointments
                .GroupBy(a => a.PatientId)
                .ToList();

            // Get primary insurance for patients (including InsuranceId for authorization lookup)
            var patientIds = patientAppointments.Select(g => g.Key).ToList();
            var patientInsuranceData = (await _context.Insurances
                .Where(pi => patientIds.Contains(pi.PatientId))
                .ToListAsync())
                .GroupBy(pi => pi.PatientId)
                .ToDictionary(g => g.Key, g => new { InsuranceId = g.First().InsuranceId, PayerName = g.First().PayerName });

            // Build the grid rows
            var rows = new List<PatientVisitGridRowDto>();
            int totalVisits = 0;
            int totalEvaluations = 0;
            int totalInterimEvals = 0;

            foreach (var patientGroup in patientAppointments)
            {
                var firstAppointment = patientGroup.First();
                var patient = firstAppointment.Patient;
                var provider = firstAppointment.Provider;
                var careEpisode = firstAppointment.CareEpisode;

                // Get authorization data from patient's primary insurance
                AuthorizationDto currentAuth = null;
                var insuranceData = patientInsuranceData.GetValueOrDefault(patientGroup.Key);
                if (insuranceData != null)
                {
                    currentAuth = await _authorizationService.GetCurrentAuthorizationAsync(insuranceData.InsuranceId);
                }

                // Create row for this patient
                var row = new PatientVisitGridRowDto
                {
                    PatientId = patientGroup.Key,
                    PatientName = $"{patient?.LastName}, {patient?.FirstName}",
                    ProviderId = provider?.ProviderId ?? 0,
                    TherapistName = $"{provider?.LastName}, {provider?.FirstName}",
                    TherapistCode = GetProviderCode(provider),
                    Insurance = insuranceData?.PayerName ?? "",
                    AuthorizedVisits = currentAuth?.AuthorizedVisits,
                    PCP = careEpisode?.PhysicianName ?? "",
                    Frequency = careEpisode != null ? FormatFrequency(careEpisode.VisitFrequency, careEpisode.ExpectedVisits) : "",
                    CertPeriod = "", // Leave blank until confirmed
                    DayVisits = new Dictionary<int, List<DayVisitDto>>()
                };

                int attemptedCount = 0;
                int missedCount = 0;
                int evalCount = 0;
                int interimCount = 0;

                // Process each appointment for this patient
                foreach (var appt in patientGroup)
                {
                    // TIMEZONE FIX: Convert UTC to location timezone before extracting day
                    var timeZoneId = appt.Location?.TimeZoneId ?? "America/Chicago"; // Default to CST if not set
                    var localDateTime = ConvertUtcToLocationTime(appt.StartTime, timeZoneId);
                    var dayOfMonth = localDateTime.Day;
                    var code = GetAppointmentTypeCode(appt.Type, appt.Status);

                    var dayVisit = new DayVisitDto
                    {
                        AppointmentId = appt.AppointmentId,
                        Code = code,
                        Type = appt.Type,
                        Status = appt.Status ?? 0,
                        StartTime = appt.StartTime,
                        HasNote = notesByAppointment.ContainsKey(appt.AppointmentId),
                        NoteStatus = notesByAppointment.GetValueOrDefault(appt.AppointmentId, 0)
                    };

                    if (!row.DayVisits.ContainsKey(dayOfMonth))
                    {
                        row.DayVisits[dayOfMonth] = new List<DayVisitDto>();
                    }
                    row.DayVisits[dayOfMonth].Add(dayVisit);

                    var status = appt.Status ?? 0;

                    // Count attempted visits: status >= 2 (CheckedIn, InProgress, Completed)
                    if (status >= 2 && status <= 4)
                    {
                        attemptedCount++;
                    }

                    // Count missed visits: explicitly missed (status 8) OR past date with no check-in (status 0 or 1)
                    if (status == 8)
                    {
                        missedCount++;
                    }
                    else if ((status == 0 || status == 1) && localDateTime < DateTime.Now)
                    {
                        missedCount++;
                    }

                    // Count by type
                    if (code == "E") evalCount++;
                    else if (code == "I") interimCount++;
                }

                row.AuthorizedVisits = attemptedCount; // A.V. = Attempted Visits
                row.MonthlyVisitCount = missedCount; // M = Missed Visits
                row.EvaluationCount = evalCount;
                row.InterimEvalCount = interimCount;

                // Leave RV and PV empty as requested
                row.RemainingVisits = null;
                row.ProjectedVisits = null;

                rows.Add(row);
                totalVisits += missedCount; // Now represents total missed visits
                totalEvaluations += evalCount;
                totalInterimEvals += interimCount;
            }

            // Sort rows by patient name
            rows = rows.OrderBy(r => r.PatientName).ToList();

            // Get discipline if filtering by provider
            string discipline = "All Disciplines";
            if (request.ProviderId.HasValue)
            {
                var provider = await _context.Providers.FindAsync(request.ProviderId.Value);
                discipline = provider?.Specialty ?? "All Disciplines";
            }

            var response = new MonthlyVisitGridResponseDto
            {
                AgencyName = agencyName,
                Discipline = discipline,
                Month = request.Month,
                Year = request.Year,
                DaysInMonth = daysInMonth,
                Rows = rows,
                TotalVisits = totalVisits,
                TotalEvaluations = totalEvaluations,
                TotalInterimEvals = totalInterimEvals,
                GeneratedAt = DateTime.Now
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, ex.Message+" "+ex.InnerException);
            return StatusCode(500, new { message = "Error generating report. Please try again. " + ex.Message + " " + ex.InnerException });
        }
    }

    /// <summary>
    /// Convert appointment type to grid code.
    /// Only 4 appointment types are used: E, V, I, D
    /// Status indicators (A, C, M) are added by frontend based on appointment status.
    /// </summary>
    private string GetAppointmentTypeCode(int type, int? status)
    {
        // Map type to code - only return type code, NOT status
        // Status indicators are handled separately in frontend
        return type switch
        {
            0 => "N",   // New Patient Visit
            1 => "F",   // Follow-Up Visit
            2 => "A",   // Annual Physical
            3 => "W",   // Wellness Exam
            4 => "C",   // Consultation
            5 => "T",   // Telehealth
            6 => "P",   // Procedure Visit
            7 => "U",   // Urgent Visit
            8 => "L",   // Lab Review
            9 => "M",   // Medication Review
            _ => "F"    // Default to Follow-Up for legacy/other types
        };
    }

    /// <summary>
    /// Convert UTC datetime to location's local time using IANA timezone ID
    /// </summary>
    private DateTime ConvertUtcToLocationTime(DateTime utcDateTime, string timeZoneId)
    {
        try
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, timeZone);
        }
        catch
        {
            // If IANA ID doesn't work, try converting to Windows timezone ID
            try
            {
                // Common IANA to Windows mappings
                var windowsId = timeZoneId switch
                {
                    "America/Chicago" => "Central Standard Time",
                    "America/New_York" => "Eastern Standard Time",
                    "America/Los_Angeles" => "Pacific Standard Time",
                    "America/Denver" => "Mountain Standard Time",
                    "America/Phoenix" => "US Mountain Standard Time",
                    "America/Anchorage" => "Alaskan Standard Time",
                    "Pacific/Honolulu" => "Hawaiian Standard Time",
                    _ => "Central Standard Time" // Default to CST
                };
                var timeZone = TimeZoneInfo.FindSystemTimeZoneById(windowsId);
                return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, timeZone);
            }
            catch
            {
                // Fallback: assume CST (-6 hours from UTC)
                return utcDateTime.AddHours(-6);
            }
        }
    }

    /// <summary>
    /// Get provider initials or short code
    /// </summary>
    private string GetProviderCode(Provider? provider)
    {
        if (provider == null) return "";
        var first = provider.FirstName?.FirstOrDefault().ToString() ?? "";
        var last = provider.LastName?.FirstOrDefault().ToString() ?? "";
        return $"{first}{last}".ToUpper();
    }

    /// <summary>
    /// Format frequency string (e.g., "2x3" meaning 2 times per week for 3 weeks)
    /// Calculates weeks from expected visits: weeks = expectedVisits / frequency
    /// </summary>
    private string FormatFrequency(int? frequency, int? expectedVisits)
    {
        if (!frequency.HasValue) return "";
        if (!expectedVisits.HasValue) return $"{frequency}x";

        // Calculate number of weeks: expectedVisits / frequency
        int weeks = expectedVisits.Value / frequency.Value;
        return $"{frequency}x{weeks}";
    }

    /// <summary>
    /// Format certification period
    /// </summary>
    private string FormatCertPeriod(DateOnly? start, DateOnly? end)
    {
        if (!start.HasValue || !end.HasValue) return "";
        return $"{start.Value:M/d}-{end.Value:M/d}";
    }

    private string FormatCertPeriodFromAuth(AuthorizationDto auth)
    {
        if (auth == null || !auth.ExpiryDate.HasValue) return "";
        var startDate = DateOnly.FromDateTime(auth.DateOfValidation);
        return $"{startDate:M/d}-{auth.ExpiryDate.Value:M/d}";
    }
}
