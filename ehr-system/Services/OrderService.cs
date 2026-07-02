using Microsoft.EntityFrameworkCore;
using EHR.Models;
using EHR.Models.Generated;
using EHR.Helpers;

namespace EHR.Services;

public interface IOrderService
{
    Task<List<OrderDto>> GetAllAsync(int? patientId, int? orderType, int? status, DateOnly? dateFrom, DateOnly? dateTo);
    Task<List<OrderDto>> GetByPatientAsync(int patientId);
    Task<OrderDto?> GetByIdAsync(int id);
    Task<Order> CreateAsync(OrderCreateDto dto, int userId);
    Task<Order?> UpdateAsync(int id, OrderUpdateDto dto);
    Task<bool> CancelAsync(int id, int userId);
    Task<bool> AddResultsAsync(int orderId, OrderResultCreateDto dto);
    Task<OrdersPendingCountDto> GetPendingCountsAsync();
    Task<LabOrderPrintDto?> GetLabOrderPrintDataAsync(int orderId, string printedByName);
}

public class OrderService : IOrderService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly ILocationProvider _locationProvider;
    private readonly EncryptionHelper _encryptionHelper;

    public OrderService(
        EhrDbContext context,
        ITenantProvider tenantProvider,
        ILocationProvider locationProvider,
        EncryptionHelper encryptionHelper)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _locationProvider = locationProvider;
        _encryptionHelper = encryptionHelper;
    }

    public async Task<List<OrderDto>> GetAllAsync(int? patientId, int? orderType, int? status, DateOnly? dateFrom, DateOnly? dateTo)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        // AsNoTracking: read-only list. We load entities first (not .Select → SQL
        // projection) so we can DecryptEntity the Patient/Provider navigation
        // properties in memory before mapping to DTOs. Previous implementation
        // used .Select(o => MapToDto(o)) which sent the encrypted FirstName/
        // LastName columns straight through to the DTO as plaintext strings.
        var query = _context.Orders
            .AsNoTracking()
            .Include(o => o.Patient)
            .Include(o => o.Provider)
            .Include(o => o.Results)
            .Where(o => o.TenantId == tenantId);

        if (patientId.HasValue)
            query = query.Where(o => o.PatientId == patientId.Value);
        if (orderType.HasValue)
            query = query.Where(o => o.OrderType == orderType.Value);
        if (status.HasValue)
            query = query.Where(o => o.Status == status.Value);
        if (dateFrom.HasValue)
            query = query.Where(o => o.OrderDate >= dateFrom.Value);
        if (dateTo.HasValue)
            query = query.Where(o => o.OrderDate <= dateTo.Value);

        var orders = await query
            .OrderByDescending(o => o.OrderDate)
            .ThenByDescending(o => o.CreatedAt)
            .ToListAsync();

        foreach (var o in orders)
        {
            if (o.Patient != null) _encryptionHelper.DecryptEntity(o.Patient);
            if (o.Provider != null) _encryptionHelper.DecryptEntity(o.Provider);
        }

        return orders.Select(MapToDto).ToList();
    }

    public async Task<List<OrderDto>> GetByPatientAsync(int patientId)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        // AsNoTracking + in-memory decrypt before DTO mapping — same rationale
        // as GetAllAsync above.
        var orders = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Patient)
            .Include(o => o.Provider)
            .Include(o => o.Results)
            .Where(o => o.TenantId == tenantId && o.PatientId == patientId)
            .OrderByDescending(o => o.OrderDate)
            .ThenByDescending(o => o.CreatedAt)
            .ToListAsync();

        foreach (var o in orders)
        {
            if (o.Patient != null) _encryptionHelper.DecryptEntity(o.Patient);
            if (o.Provider != null) _encryptionHelper.DecryptEntity(o.Provider);
        }

        return orders.Select(MapToDto).ToList();
    }

    public async Task<OrderDto?> GetByIdAsync(int id)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        // AsNoTracking + in-memory decrypt before DTO mapping — same rationale.
        var order = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Patient)
            .Include(o => o.Provider)
            .Include(o => o.Results)
            .Where(o => o.TenantId == tenantId && o.OrderId == id)
            .FirstOrDefaultAsync();

        if (order == null) return null;

        if (order.Patient != null) _encryptionHelper.DecryptEntity(order.Patient);
        if (order.Provider != null) _encryptionHelper.DecryptEntity(order.Provider);

        return MapToDto(order);
    }

    public async Task<Order> CreateAsync(OrderCreateDto dto, int userId)
    {
        var order = new Order
        {
            TenantId = _tenantProvider.TenantId ?? 0,
            PatientId = dto.PatientId,
            ProviderId = dto.ProviderId,
            EncounterId = dto.EncounterId,
            OrderType = dto.OrderType,
            Status = dto.Status,
            Priority = dto.Priority,
            OrderDate = DateOnly.FromDateTime(DateTime.Today),
            DiagnosisCode = dto.DiagnosisCode,
            ClinicalIndication = dto.ClinicalIndication,
            Notes = dto.Notes,
            // Lab fields
            LabPanelName = dto.LabPanelName,
            FastingRequired = dto.FastingRequired,
            SpecimenType = dto.SpecimenType,
            // Imaging fields
            Modality = dto.Modality,
            BodyPart = dto.BodyPart,
            ContrastRequired = dto.ContrastRequired,
            ImagingFacility = dto.ImagingFacility,
            // Referral fields
            ReferralSpecialty = dto.ReferralSpecialty,
            ReferredToProvider = dto.ReferredToProvider,
            ReferredToFacility = dto.ReferredToFacility,
            ReferredToPhone = dto.ReferredToPhone,
            ReferredToFax = dto.ReferredToFax,
            ReferralReason = dto.ReferralReason,
            ReferralUrgency = dto.ReferralUrgency,
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow
        };
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();
        return order;
    }

    public async Task<Order?> UpdateAsync(int id, OrderUpdateDto dto)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        var order = await _context.Orders
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.OrderId == id);

        if (order == null) return null;

        if (dto.Status.HasValue) order.Status = dto.Status.Value;
        if (dto.Priority.HasValue) order.Priority = dto.Priority.Value;
        if (dto.DiagnosisCode != null) order.DiagnosisCode = dto.DiagnosisCode;
        if (dto.ClinicalIndication != null) order.ClinicalIndication = dto.ClinicalIndication;
        if (dto.Notes != null) order.Notes = dto.Notes;
        // Lab
        if (dto.LabPanelName != null) order.LabPanelName = dto.LabPanelName;
        if (dto.FastingRequired.HasValue) order.FastingRequired = dto.FastingRequired.Value;
        if (dto.SpecimenType != null) order.SpecimenType = dto.SpecimenType;
        // Imaging
        if (dto.Modality.HasValue) order.Modality = dto.Modality.Value;
        if (dto.BodyPart != null) order.BodyPart = dto.BodyPart;
        if (dto.ContrastRequired.HasValue) order.ContrastRequired = dto.ContrastRequired.Value;
        if (dto.ImagingFacility != null) order.ImagingFacility = dto.ImagingFacility;
        // Referral
        if (dto.ReferralSpecialty != null) order.ReferralSpecialty = dto.ReferralSpecialty;
        if (dto.ReferredToProvider != null) order.ReferredToProvider = dto.ReferredToProvider;
        if (dto.ReferredToFacility != null) order.ReferredToFacility = dto.ReferredToFacility;
        if (dto.ReferredToPhone != null) order.ReferredToPhone = dto.ReferredToPhone;
        if (dto.ReferredToFax != null) order.ReferredToFax = dto.ReferredToFax;
        if (dto.ReferralReason != null) order.ReferralReason = dto.ReferralReason;
        if (dto.ReferralUrgency.HasValue) order.ReferralUrgency = dto.ReferralUrgency.Value;

        order.UpdatedAt = DateTime.UtcNow;

        // If completed, set CompletedAt
        if (dto.Status.HasValue && dto.Status.Value == (int)OrderStatus.Completed)
            order.CompletedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return order;
    }

    public async Task<bool> CancelAsync(int id, int userId)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        var order = await _context.Orders
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.OrderId == id);

        if (order == null) return false;

        order.Status = (int)OrderStatus.Cancelled;
        order.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> AddResultsAsync(int orderId, OrderResultCreateDto dto)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        var order = await _context.Orders
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.OrderId == orderId);

        if (order == null) return false;

        foreach (var item in dto.Results)
        {
            _context.OrderResults.Add(new OrderResult
            {
                OrderId = orderId,
                TestName = item.TestName,
                ResultValue = item.ResultValue,
                ResultUnit = item.ResultUnit,
                ReferenceRange = item.ReferenceRange,
                IsAbnormal = item.IsAbnormal,
                FindingsText = item.FindingsText,
                ResultDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
        }

        // Auto-advance status to ResultsReceived
        if (order.Status < (int)OrderStatus.ResultsReceived)
            order.Status = (int)OrderStatus.ResultsReceived;

        order.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<OrdersPendingCountDto> GetPendingCountsAsync()
    {
        var tenantId = _tenantProvider.TenantId ?? 0;
        var pendingStatuses = new[] { (int)OrderStatus.Pending, (int)OrderStatus.Sent, (int)OrderStatus.InProgress };

        var counts = await _context.Orders
            .Where(o => o.TenantId == tenantId && pendingStatuses.Contains(o.Status))
            .GroupBy(o => o.OrderType)
            .Select(g => new { OrderType = g.Key, Count = g.Count() })
            .ToListAsync();

        return new OrdersPendingCountDto
        {
            PendingLabResults = counts.FirstOrDefault(c => c.OrderType == (int)OrderType.Lab)?.Count ?? 0,
            PendingImagingResults = counts.FirstOrDefault(c => c.OrderType == (int)OrderType.Imaging)?.Count ?? 0,
            PendingReferrals = counts.FirstOrDefault(c => c.OrderType == (int)OrderType.Referral)?.Count ?? 0,
            TotalPending = counts.Sum(c => c.Count)
        };
    }

    /// <summary>
    /// Build the one-shot payload for the lab-report print template.
    /// Joins order + patient + provider + tenant + active location, decrypts
    /// PHI, and formats all dates in the location's IANA timezone (per the
    /// strict timezone rule in CLAUDE.md). Returns null if the order is not
    /// a lab order or does not belong to the current tenant.
    /// </summary>
    public async Task<LabOrderPrintDto?> GetLabOrderPrintDataAsync(int orderId, string printedByName)
    {
        var tenantId = _tenantProvider.TenantId ?? 0;

        var order = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Patient)
            .Include(o => o.Provider)
            .Include(o => o.Tenant)
            .Include(o => o.Results)
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.OrderId == orderId);

        if (order == null || order.OrderType != (int)OrderType.Lab) return null;

        if (order.Patient != null) _encryptionHelper.DecryptEntity(order.Patient);
        if (order.Provider != null) _encryptionHelper.DecryptEntity(order.Provider);

        // Prefer the active location for clinic address (multi-location clinics
        // should print the location that ordered the test). Fall back to tenant.
        Location? location = null;
        if (_locationProvider.LocationId.HasValue)
        {
            location = await _context.Locations
                .AsNoTracking()
                .FirstOrDefaultAsync(l =>
                    l.LocationId == _locationProvider.LocationId.Value &&
                    l.TenantId == tenantId);
        }
        if (location == null)
        {
            location = await _context.Locations
                .AsNoTracking()
                .Where(l => l.TenantId == tenantId && l.IsActive == true && l.IsPrimary == true)
                .FirstOrDefaultAsync()
                ?? await _context.Locations
                    .AsNoTracking()
                    .Where(l => l.TenantId == tenantId && l.IsActive == true)
                    .OrderBy(l => l.LocationId)
                    .FirstOrDefaultAsync();
        }

        var tenant = order.Tenant;
        var tzId = location?.TimeZoneId ?? TimezoneHelper.DefaultTimeZoneId;
        var nowUtc = DateTime.UtcNow;
        var tzAbbr = TimezoneHelper.GetTimezoneAbbreviation(tzId, nowUtc);

        // Patient demographics
        var patient = order.Patient;
        var patientFirst = patient?.FirstName?.Trim() ?? "";
        var patientLast = patient?.LastName?.Trim() ?? "";
        var patientNameFormal = string.IsNullOrEmpty(patientLast) && string.IsNullOrEmpty(patientFirst)
            ? "—"
            : $"{patientLast.ToUpperInvariant()}, {patientFirst.ToUpperInvariant()}";

        var patientAge = 0;
        string patientDobFormatted = "";
        if (patient != null)
        {
            patientDobFormatted = patient.DateOfBirth.ToString("dd-MMM-yyyy");
            var today = DateOnly.FromDateTime(DateTime.Today);
            patientAge = today.Year - patient.DateOfBirth.Year;
            if (patient.DateOfBirth > today.AddYears(-patientAge)) patientAge--;
        }

        string patientAddressLine = "";
        if (patient != null)
        {
            var addrParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(patient.Address)) addrParts.Add(patient.Address.Trim());
            var cityStateZip = string.Join(", ", new[] { patient.City, patient.State }.Where(s => !string.IsNullOrWhiteSpace(s)));
            if (!string.IsNullOrWhiteSpace(cityStateZip))
            {
                if (!string.IsNullOrWhiteSpace(patient.ZipCode))
                    cityStateZip += " " + patient.ZipCode.Trim();
                addrParts.Add(cityStateZip);
            }
            else if (!string.IsNullOrWhiteSpace(patient.ZipCode))
            {
                addrParts.Add(patient.ZipCode.Trim());
            }
            patientAddressLine = string.Join(", ", addrParts);
        }

        // Provider
        var provider = order.Provider;
        var providerName = provider == null
            ? ""
            : $"{provider.FirstName} {provider.LastName}".Trim();
        if (!string.IsNullOrWhiteSpace(provider?.Credentials))
            providerName = $"{providerName}, {provider.Credentials.Trim()}";

        // Clinic address (prefer location, fall back to tenant)
        string clinicAddressLine;
        string clinicCityStateZip;
        string clinicPhone;
        if (location != null && !string.IsNullOrWhiteSpace(location.Address))
        {
            clinicAddressLine = location.Address.Trim();
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(location.City)) parts.Add(location.City.Trim());
            if (!string.IsNullOrWhiteSpace(location.State)) parts.Add(location.State.Trim());
            clinicCityStateZip = string.Join(", ", parts);
            if (!string.IsNullOrWhiteSpace(location.ZipCode))
                clinicCityStateZip = string.IsNullOrEmpty(clinicCityStateZip)
                    ? location.ZipCode.Trim()
                    : clinicCityStateZip + " " + location.ZipCode.Trim();
            clinicPhone = !string.IsNullOrWhiteSpace(location.Phone) ? location.Phone : (tenant?.Phone ?? "");
        }
        else
        {
            clinicAddressLine = tenant?.Address?.Trim() ?? "";
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(tenant?.City)) parts.Add(tenant.City.Trim());
            if (!string.IsNullOrWhiteSpace(tenant?.State)) parts.Add(tenant.State.Trim());
            clinicCityStateZip = string.Join(", ", parts);
            if (!string.IsNullOrWhiteSpace(tenant?.ZipCode))
                clinicCityStateZip = string.IsNullOrEmpty(clinicCityStateZip)
                    ? tenant.ZipCode.Trim()
                    : clinicCityStateZip + " " + tenant.ZipCode.Trim();
            clinicPhone = tenant?.Phone ?? "";
        }

        // Order dates
        string orderDateFormatted = order.OrderDate.ToString("dd-MMM-yyyy");
        string? reportedDateFormatted = null;
        string? verifiedAtFormatted = null;
        if (order.CompletedAt.HasValue)
        {
            var localCompleted = TimezoneHelper.FormatDateTimeWithTimezone(order.CompletedAt.Value, tzId);
            reportedDateFormatted = $"{localCompleted} {tzAbbr}";
            verifiedAtFormatted = reportedDateFormatted;
        }

        var printedAtLocal = TimezoneHelper.FormatDateTimeWithTimezone(nowUtc, tzId);
        var printedAtFormatted = $"{printedAtLocal} {tzAbbr}";

        // Results
        var results = (order.Results ?? new List<OrderResult>())
            .OrderBy(r => r.OrderResultId)
            .Select(r =>
            {
                var isAbn = r.IsAbnormal == true;
                return new LabResultPrintDto
                {
                    TestName = r.TestName ?? "",
                    ResultValue = r.ResultValue ?? "",
                    ResultUnit = r.ResultUnit ?? "",
                    ReferenceRange = r.ReferenceRange ?? "",
                    IsAbnormal = isAbn,
                    FlagText = isAbn ? "H · Abnormal" : "Normal"
                };
            })
            .ToList();

        return new LabOrderPrintDto
        {
            OrderId = order.OrderId,
            AccessionNumber = $"LAB-{tenantId}-{order.OrderId:D7}",
            OrderDateFormatted = orderDateFormatted,
            ReportedDateFormatted = reportedDateFormatted,
            PriorityName = EnumHelper.GetOrderPriorityName(order.Priority),
            StatusName = EnumHelper.GetOrderStatusName(order.Status),
            DiagnosisCode = order.DiagnosisCode,
            DiagnosisDescription = null, // not stored separately; UI may render the code alone
            ClinicalIndication = order.ClinicalIndication,
            LabPanelName = order.LabPanelName,
            SpecimenType = order.SpecimenType,
            FastingRequired = order.FastingRequired,
            Notes = order.Notes,
            Results = results,

            PatientNameFormal = patientNameFormal,
            PatientMrn = patient?.Mrn ?? "",
            PatientDobFormatted = patientDobFormatted,
            PatientAge = patientAge,
            PatientGender = patient?.Gender ?? "",
            PatientAddressLine = patientAddressLine,
            PatientPhone = patient?.Phone ?? "",

            ProviderName = providerName,
            ProviderNpi = provider?.Npi ?? "",
            ProviderCredentials = provider?.Credentials ?? "",
            ProviderSpecialty = provider?.Specialty ?? "",

            TenantId = tenantId,
            HasLogo = !string.IsNullOrEmpty(tenant?.LogoUrl),
            ClinicName = tenant?.Name ?? "",
            ClinicTagline = location?.Name ?? "",
            ClinicAddressLine = clinicAddressLine,
            ClinicCityStateZip = clinicCityStateZip,
            ClinicPhone = clinicPhone,
            ClinicEmail = tenant?.Email ?? "",
            ClinicNpi = tenant?.Npi ?? "",
            ClinicTaxId = tenant?.TaxId ?? "",
            TimeZoneAbbreviation = tzAbbr,
            TimeZoneId = tzId,

            PrintedByName = printedByName ?? "",
            PrintedAtFormatted = printedAtFormatted,
            VerifiedAtFormatted = verifiedAtFormatted
        };
    }

    private static OrderDto MapToDto(Order o) => new()
    {
        OrderId = o.OrderId,
        PatientId = o.PatientId,
        PatientName = o.Patient != null ? o.Patient.FirstName + " " + o.Patient.LastName : "",
        PatientMRN = o.Patient?.Mrn ?? "",
        ProviderId = o.ProviderId,
        ProviderName = o.Provider != null ? o.Provider.FirstName + " " + o.Provider.LastName : "",
        EncounterId = o.EncounterId,
        OrderType = o.OrderType,
        OrderTypeName = EnumHelper.GetOrderTypeName(o.OrderType),
        Status = o.Status,
        StatusName = EnumHelper.GetOrderStatusName(o.Status),
        Priority = o.Priority,
        PriorityName = EnumHelper.GetOrderPriorityName(o.Priority),
        OrderDate = o.OrderDate,
        DiagnosisCode = o.DiagnosisCode,
        ClinicalIndication = o.ClinicalIndication,
        Notes = o.Notes,
        // Lab
        LabPanelName = o.LabPanelName,
        FastingRequired = o.FastingRequired,
        SpecimenType = o.SpecimenType,
        // Imaging
        Modality = o.Modality,
        ModalityName = o.Modality.HasValue ? EnumHelper.GetImagingModalityName(o.Modality.Value) : null,
        BodyPart = o.BodyPart,
        ContrastRequired = o.ContrastRequired,
        ImagingFacility = o.ImagingFacility,
        // Referral
        ReferralSpecialty = o.ReferralSpecialty,
        ReferredToProvider = o.ReferredToProvider,
        ReferredToFacility = o.ReferredToFacility,
        ReferredToPhone = o.ReferredToPhone,
        ReferredToFax = o.ReferredToFax,
        ReferralReason = o.ReferralReason,
        ReferralUrgency = o.ReferralUrgency,
        ReferralUrgencyName = o.ReferralUrgency.HasValue ? EnumHelper.GetReferralUrgencyName(o.ReferralUrgency.Value) : null,
        CreatedAt = o.CreatedAt,
        CompletedAt = o.CompletedAt,
        Results = o.Results != null ? o.Results.OrderBy(r => r.OrderResultId).Select(r => new OrderResultDto
        {
            OrderResultId = r.OrderResultId,
            TestName = r.TestName,
            ResultValue = r.ResultValue,
            ResultUnit = r.ResultUnit,
            ReferenceRange = r.ReferenceRange,
            IsAbnormal = r.IsAbnormal,
            FindingsText = r.FindingsText,
            ResultDate = r.ResultDate
        }).ToList() : new List<OrderResultDto>()
    };
}
