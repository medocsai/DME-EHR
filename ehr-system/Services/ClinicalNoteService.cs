using Microsoft.EntityFrameworkCore;
using EHR.Models;
using EHR.Models.Generated;
using EHR.Helpers;
using EHR.Services.Storage;
using EHR.Hubs;
using System.Text.RegularExpressions;

namespace EHR.Services;

public interface IClinicalNoteTemplateService
{
    Task<List<ClinicalNoteTemplateDto>> GetTemplatesAsync(bool activeOnly = true);
    Task<List<ClinicalNoteTemplateDto>> GetTemplatesForLocationAsync(int? locationId = null, bool activeOnly = true);
    Task<ClinicalNoteTemplateDetailDto?> GetByIdAsync(int templateId);
    Task<ClinicalNoteTemplate> CreateAsync(ClinicalNoteTemplateCreateDto dto, int createdByUserId);
    Task<ClinicalNoteTemplate?> UpdateAsync(int templateId, ClinicalNoteTemplateUpdateDto dto);
    Task<bool> DeleteAsync(int templateId);
    Task SeedDefaultTemplatesAsync();
}

public interface IClinicalNoteService
{
    Task<List<ClinicalNoteListDto>> GetNotesAsync(
        int? patientId = null,
        int? providerId = null,
        int? appointmentId = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        int? status = null);
    Task<ClinicalNotePagedResponse> GetNotesPagedAsync(
        string? search = null,
        int? patientId = null,
        int? providerId = null,
        int? appointmentId = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        int? status = null,
        int page = 1,
        int pageSize = 20);
    Task<ClinicalNoteDetailDto?> GetDetailAsync(int noteId);
    Task<ClinicalNote> CreateAsync(ClinicalNoteCreateDto dto, int createdByUserId);
    Task<ClinicalNote?> UpdateAsync(int noteId, ClinicalNoteUpdateDto dto);
    Task<ClinicalNote?> SignNoteAsync(int noteId, int signedByUserId, string? signatureData = null);
    Task<(bool Success, string? ErrorMessage)> DeleteAsync(int noteId, int userId, string? userIp);
    Task<bool> HasNoteForAppointmentAsync(int appointmentId);
    Task<int?> GetNoteIdForAppointmentAsync(int appointmentId);
    Task<ClinicalNoteDetailDto?> GetLastSignedNoteAsync(int patientId, int providerId, int? templateId = null, int? appointmentType = null);
}

public class ClinicalNoteTemplateService : IClinicalNoteTemplateService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    
    public ClinicalNoteTemplateService(EhrDbContext context, ITenantProvider tenantProvider)
    {
        _context = context;
        _tenantProvider = tenantProvider;
    }
    
    public async Task<List<ClinicalNoteTemplateDto>> GetTemplatesAsync(bool activeOnly = true)
    {
        var query = _context.ClinicalNoteTemplates
            .Include(t => t.Location)
            .Include(t => t.Tenant)
            .AsQueryable();

        // Include system templates (TenantId = null) and tenant-specific templates
        if (_tenantProvider.TenantId.HasValue)
        {
            query = query.Where(t => t.TenantId == null || t.TenantId == _tenantProvider.TenantId.Value);
        }

        if (activeOnly)
            query = query.Where(t => t.IsActive);

        return await query
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Name)
            .Select(t => new ClinicalNoteTemplateDto
            {
                TemplateId = t.TemplateId,
                Name = t.Name,
                TenantId = t.TenantId,
                TenantName = t.Tenant != null ? t.Tenant.Name : null,
                LocationId = t.LocationId,
                LocationName = t.Location != null ? t.Location.Name : null,
                HtmlContent = t.HtmlContent,
                IsSystemTemplate = t.IsSystemTemplate,
                IsActive = t.IsActive,
                SortOrder = t.SortOrder
            })
            .ToListAsync();
    }

    public async Task<List<ClinicalNoteTemplateDto>> GetTemplatesForLocationAsync(int? locationId = null, bool activeOnly = true)
    {
        var query = _context.ClinicalNoteTemplates
            .Include(t => t.Location)
            .AsQueryable();

        // Filter by location: include templates with no location (available to all) OR matching location
        if (locationId.HasValue)
        {
            query = query.Where(t => t.LocationId == null || t.LocationId == locationId.Value);
        }

        if (activeOnly)
            query = query.Where(t => t.IsActive);

        // Handle tenant-specific vs system templates:
        // If tenant has their own templates for the matching criteria, use only tenant templates.
        // Otherwise, include system templates as fallback.
        if (_tenantProvider.TenantId.HasValue)
        {
            // Check if tenant has any templates matching the criteria
            var hasTenantTemplates = await query
                .AnyAsync(t => t.TenantId == _tenantProvider.TenantId.Value);

            if (hasTenantTemplates)
            {
                // Tenant has templates assigned - use only tenant templates (no system templates)
                query = query.Where(t => t.TenantId == _tenantProvider.TenantId.Value);
            }
            else
            {
                // No tenant templates - use system templates as fallback
                query = query.Where(t => t.TenantId == null);
            }
        }
        else
        {
            // No tenant context - only show system templates
            query = query.Where(t => t.TenantId == null);
        }

        return await query
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Name)
            .Select(t => new ClinicalNoteTemplateDto
            {
                TemplateId = t.TemplateId,
                Name = t.Name,
                TenantId = t.TenantId,
                LocationId = t.LocationId,
                LocationName = t.Location != null ? t.Location.Name : null,
                HtmlContent = t.HtmlContent,
                IsSystemTemplate = t.IsSystemTemplate,
                IsActive = t.IsActive,
                SortOrder = t.SortOrder
            })
            .ToListAsync();
    }

    public async Task<ClinicalNoteTemplateDetailDto?> GetByIdAsync(int templateId)
    {
        var query = _context.ClinicalNoteTemplates
            .Include(t => t.Location)
            .AsQueryable();

        return await query
            .Where(t => t.TemplateId == templateId)
            .Select(t => new ClinicalNoteTemplateDetailDto
            {
                TemplateId = t.TemplateId,
                Name = t.Name,
                TenantId = t.TenantId,
                LocationId = t.LocationId,
                LocationName = t.Location != null ? t.Location.Name : null,
                HtmlContent = t.HtmlContent,
                IsSystemTemplate = t.IsSystemTemplate,
                IsActive = t.IsActive,
                SortOrder = t.SortOrder
            })
            .FirstOrDefaultAsync();
    }

    public async Task<ClinicalNoteTemplate> CreateAsync(ClinicalNoteTemplateCreateDto dto, int createdByUserId)
    {
        var template = new ClinicalNoteTemplate
        {
            // Use TenantId from DTO if provided (SuperAdmin), otherwise use TenantProvider (regular Admin)
            TenantId = dto.TenantId ?? _tenantProvider.TenantId,
            LocationId = dto.LocationId,
            Name = dto.Name,
            HtmlContent = dto.HtmlContent,
            SortOrder = dto.SortOrder,
            IsSystemTemplate = false,
            IsActive = dto.IsActive,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = createdByUserId
        };

        _context.ClinicalNoteTemplates.Add(template);
        await _context.SaveChangesAsync();

        return template;
    }
    
    public async Task<ClinicalNoteTemplate?> UpdateAsync(int templateId, ClinicalNoteTemplateUpdateDto dto)
    {
        var template = await _context.ClinicalNoteTemplates.FindAsync(templateId);

        if (template == null || template.IsSystemTemplate)
            return null;

        if (dto.Name != null) template.Name = dto.Name;
        // Always update TenantId and LocationId (null means "All Clinics"/"All Locations")
        template.TenantId = dto.TenantId;
        template.LocationId = dto.LocationId;
        if (dto.HtmlContent != null) template.HtmlContent = dto.HtmlContent;
        if (dto.SortOrder.HasValue) template.SortOrder = dto.SortOrder.Value;
        if (dto.IsActive.HasValue) template.IsActive = dto.IsActive.Value;

        template.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return template;
    }
    
    public async Task<bool> DeleteAsync(int templateId)
    {
        var template = await _context.ClinicalNoteTemplates.FindAsync(templateId);
        
        if (template == null || template.IsSystemTemplate)
            return false;
        
        _context.ClinicalNoteTemplates.Remove(template);
        await _context.SaveChangesAsync();
        return true;
    }
    
    public async Task SeedDefaultTemplatesAsync()
    {
        // Check if templates already exist
        if (await _context.ClinicalNoteTemplates.AnyAsync(t => t.IsSystemTemplate))
            return;
        
        var templates = GetDefaultTemplates();
        _context.ClinicalNoteTemplates.AddRange(templates);
        await _context.SaveChangesAsync();
    }
    
    private List<ClinicalNoteTemplate> GetDefaultTemplates()
    {
        return new List<ClinicalNoteTemplate>
        {
            new ClinicalNoteTemplate
            {
                Name = "History & Physical",
                HtmlContent = GetInitialEvalTemplate(),
                IsSystemTemplate = true,
                IsActive = true,
                SortOrder = 1,
                CreatedAt = DateTime.UtcNow
            },
            new ClinicalNoteTemplate
            {
                Name = "SOAP Note",
                HtmlContent = GetDailyVisitTemplate(),
                IsSystemTemplate = true,
                IsActive = true,
                SortOrder = 2,
                CreatedAt = DateTime.UtcNow
            },
            new ClinicalNoteTemplate
            {
                Name = "Progress Note",
                HtmlContent = GetProgressNoteTemplate(),
                IsSystemTemplate = true,
                IsActive = true,
                SortOrder = 3,
                CreatedAt = DateTime.UtcNow
            },
            new ClinicalNoteTemplate
            {
                Name = "Consultation Note",
                HtmlContent = GetConsultationNoteTemplate(),
                IsSystemTemplate = true,
                IsActive = true,
                SortOrder = 4,
                CreatedAt = DateTime.UtcNow
            }
        };
    }
    
    private string GetInitialEvalTemplate() => @"<h2>HISTORY &amp; PHYSICAL</h2>
<h3>CHIEF COMPLAINT</h3>
<p></p>

<h3>HISTORY OF PRESENT ILLNESS</h3>
<p></p>

<h3>PAST MEDICAL HISTORY</h3>
<p></p>

<h3>MEDICATIONS</h3>
<p></p>

<h3>ALLERGIES</h3>
<p></p>

<h3>REVIEW OF SYSTEMS</h3>
<p><strong>Constitutional:</strong> </p>
<p><strong>HEENT:</strong> </p>
<p><strong>Cardiovascular:</strong> </p>
<p><strong>Respiratory:</strong> </p>
<p><strong>GI:</strong> </p>
<p><strong>Musculoskeletal:</strong> </p>
<p><strong>Neurological:</strong> </p>

<h3>PHYSICAL EXAM</h3>
<p><strong>Vitals:</strong> </p>
<p><strong>General:</strong> </p>
<p><strong>HEENT:</strong> </p>
<p><strong>Cardiovascular:</strong> </p>
<p><strong>Lungs:</strong> </p>
<p><strong>Abdomen:</strong> </p>
<p><strong>Extremities:</strong> </p>

<h3>ASSESSMENT</h3>
<p><strong>Diagnosis:</strong> </p>
<p><strong>Problem List:</strong> </p>

<h3>PLAN</h3>
<p></p>";

    private string GetDailyVisitTemplate() => @"<h2>SOAP NOTE</h2>
<h3>SUBJECTIVE</h3>
<p><strong>Chief Complaint:</strong> </p>
<p><strong>HPI:</strong> </p>
<p><strong>Review of Systems:</strong> </p>

<h3>OBJECTIVE</h3>
<p><strong>Vitals:</strong> </p>
<p><strong>Physical Exam:</strong> </p>

<h3>ASSESSMENT</h3>
<p><strong>Diagnosis:</strong> </p>

<h3>PLAN</h3>
<p><strong>Medications:</strong> </p>
<p><strong>Orders:</strong> </p>
<p><strong>Follow-Up:</strong> </p>";

    private string GetProgressNoteTemplate() => @"<h2>PROGRESS NOTE</h2>
<h3>SUBJECTIVE</h3>
<p><strong>Interval History:</strong> </p>
<p><strong>Current Symptoms:</strong> </p>
<p><strong>Medication Compliance:</strong> </p>

<h3>OBJECTIVE</h3>
<p><strong>Vitals:</strong> </p>
<p><strong>Pertinent Exam Findings:</strong> </p>
<p><strong>Lab/Test Results:</strong> </p>

<h3>ASSESSMENT</h3>
<p><strong>Active Problems:</strong> </p>
<p><strong>Response to Treatment:</strong> </p>

<h3>PLAN</h3>
<p><strong>Medication Changes:</strong> </p>
<p><strong>Orders:</strong> </p>
<p><strong>Follow-Up:</strong> </p>";

    private string GetConsultationNoteTemplate() => @"<h2>CONSULTATION NOTE</h2>
<h3>REASON FOR CONSULTATION</h3>
<p></p>

<h3>HISTORY OF PRESENT ILLNESS</h3>
<p></p>

<h3>PAST MEDICAL/SURGICAL HISTORY</h3>
<p></p>

<h3>REVIEW OF SYSTEMS</h3>
<p></p>

<h3>PHYSICAL EXAM</h3>
<p><strong>Vitals:</strong> </p>
<p><strong>General:</strong> </p>
<p><strong>Pertinent Findings:</strong> </p>

<h3>ASSESSMENT</h3>
<p></p>

<h3>RECOMMENDATIONS</h3>
<p></p>";
}

public class ClinicalNoteService : IClinicalNoteService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly EncryptionHelper? _encryptionHelper;
    private readonly IAuditService? _auditService;
    private readonly IFileStorageService? _storageService;
    private readonly IScheduleNotificationService? _notificationService;
    private readonly IBillingService? _billingService;
    private readonly IServiceScopeFactory? _serviceScopeFactory;

    public ClinicalNoteService(
        EhrDbContext context,
        ITenantProvider tenantProvider,
        EncryptionHelper? encryptionHelper = null,
        IAuditService? auditService = null,
        IFileStorageService? storageService = null,
        IScheduleNotificationService? notificationService = null,
        IBillingService? billingService = null,
        IServiceScopeFactory? serviceScopeFactory = null)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _encryptionHelper = encryptionHelper;
        _auditService = auditService;
        _storageService = storageService;
        _notificationService = notificationService;
        _billingService = billingService;
        _serviceScopeFactory = serviceScopeFactory;
    }

    public async Task<List<ClinicalNoteListDto>> GetNotesAsync(
        int? patientId = null,
        int? providerId = null,
        int? appointmentId = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        int? status = null)
    {
        // AsNoTracking: read-only list projection; Patient/Provider are decrypted
        // in-place for DTOs, must not be tracked.
        var query = _context.ClinicalNotes
            .AsNoTracking()
            .Include(n => n.Patient)
            .Include(n => n.Provider)
            .Include(n => n.Template)
            .AsQueryable();

        if (_tenantProvider.TenantId.HasValue)
            query = query.Where(n => n.TenantId == _tenantProvider.TenantId.Value);

        if (patientId.HasValue)
            query = query.Where(n => n.PatientId == patientId.Value);

        if (providerId.HasValue)
            query = query.Where(n => n.ProviderId == providerId.Value);

        if (appointmentId.HasValue)
            query = query.Where(n => n.AppointmentId == appointmentId.Value);

        if (status.HasValue)
            query = query.Where(n => n.Status == status.Value);

        if (startDate.HasValue)
        {
            var startDateOnly = DateOnly.FromDateTime(startDate.Value);
            query = query.Where(n => n.ServiceDate >= startDateOnly);
        }

        if (endDate.HasValue)
        {
            var endDateOnly = DateOnly.FromDateTime(endDate.Value);
            query = query.Where(n => n.ServiceDate <= endDateOnly);
        }

        var notes = await query
            .OrderByDescending(n => n.ServiceDate)
            .ThenByDescending(n => n.CreatedAt)
            .ToListAsync();

        // Decrypt PHI fields for Patient and Provider
        if (_encryptionHelper != null)
        {
            foreach (var n in notes)
            {
                if (n.Patient != null)
                    _encryptionHelper.DecryptEntity(n.Patient);
                if (n.Provider != null)
                    _encryptionHelper.DecryptEntity(n.Provider);
            }
        }

        return notes.Select(n => new ClinicalNoteListDto
        {
            ClinicalNoteId = n.ClinicalNoteId,
            PatientId = n.PatientId,
            PatientName = n.Patient != null ? $"{n.Patient.FirstName} {n.Patient.LastName}" : "",
            PatientMRN = n.Patient != null ? n.Patient.Mrn : "",
            ProviderId = n.ProviderId,
            ProviderName = n.Provider != null ? $"{n.Provider.FirstName} {n.Provider.LastName}" : "",
            AppointmentId = n.AppointmentId,
            Type = n.Type,
            Status = n.Status,
            ServiceDate = n.ServiceDate,
            TemplateName = n.Template?.Name,
            CreatedAt = n.CreatedAt,
            CreatedByUserId = n.CreatedByUserId,
            SignedAt = n.SignedAt
        }).ToList();
    }

    public async Task<ClinicalNotePagedResponse> GetNotesPagedAsync(
        string? search = null,
        int? patientId = null,
        int? providerId = null,
        int? appointmentId = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        int? status = null,
        int page = 1,
        int pageSize = 20)
    {
        // AsNoTracking: read-only paged projection; Patient/Provider are decrypted
        // in-place for search/DTO construction, must not be tracked.
        var query = _context.ClinicalNotes
            .AsNoTracking()
            .Include(n => n.Patient)
            .Include(n => n.Provider)
            .Include(n => n.Template)
            .AsQueryable();

        if (_tenantProvider.TenantId.HasValue)
            query = query.Where(n => n.TenantId == _tenantProvider.TenantId.Value);

        if (patientId.HasValue)
            query = query.Where(n => n.PatientId == patientId.Value);

        if (providerId.HasValue)
            query = query.Where(n => n.ProviderId == providerId.Value);

        if (appointmentId.HasValue)
            query = query.Where(n => n.AppointmentId == appointmentId.Value);

        if (status.HasValue)
            query = query.Where(n => n.Status == status.Value);

        if (startDate.HasValue)
        {
            var startDateOnly = DateOnly.FromDateTime(startDate.Value);
            query = query.Where(n => n.ServiceDate >= startDateOnly);
        }

        if (endDate.HasValue)
        {
            var endDateOnly = DateOnly.FromDateTime(endDate.Value);
            query = query.Where(n => n.ServiceDate <= endDateOnly);
        }

        // Fetch notes from database (search will be done in memory after decryption)
        var notes = await query
            .OrderByDescending(n => n.ServiceDate)
            .ThenByDescending(n => n.CreatedAt)
            .ToListAsync();

        // Decrypt PHI fields for Patient and Provider BEFORE searching
        if (_encryptionHelper != null)
        {
            foreach (var n in notes)
            {
                if (n.Patient != null)
                    _encryptionHelper.DecryptEntity(n.Patient);
                if (n.Provider != null)
                    _encryptionHelper.DecryptEntity(n.Provider);
            }
        }

        // Search by patient name, MRN, or provider name (in memory after decryption)
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLower();
            notes = notes.Where(n =>
                (n.Patient != null && (
                    (n.Patient.FirstName?.ToLower().Contains(searchLower) ?? false) ||
                    (n.Patient.LastName?.ToLower().Contains(searchLower) ?? false) ||
                    (n.Patient.Mrn?.ToLower().Contains(searchLower) ?? false)
                )) ||
                (n.Provider != null && (
                    (n.Provider.FirstName?.ToLower().Contains(searchLower) ?? false) ||
                    (n.Provider.LastName?.ToLower().Contains(searchLower) ?? false)
                ))
            ).ToList();
        }

        // Get total count after filtering
        var totalCount = notes.Count;
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        // Apply pagination in memory
        var pagedNotes = notes
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var items = pagedNotes.Select(n => new ClinicalNoteListDto
        {
            ClinicalNoteId = n.ClinicalNoteId,
            PatientId = n.PatientId,
            PatientName = n.Patient != null ? $"{n.Patient.FirstName} {n.Patient.LastName}" : "",
            PatientMRN = n.Patient != null ? n.Patient.Mrn : "",
            ProviderId = n.ProviderId,
            ProviderName = n.Provider != null ? $"{n.Provider.FirstName} {n.Provider.LastName}" : "",
            AppointmentId = n.AppointmentId,
            Type = n.Type,
            Status = n.Status,
            ServiceDate = n.ServiceDate,
            TemplateName = n.Template?.Name,
            CreatedAt = n.CreatedAt,
            CreatedByUserId = n.CreatedByUserId,
            SignedAt = n.SignedAt
        }).ToList();

        return new ClinicalNotePagedResponse
        {
            Items = items,
            TotalCount = totalCount,
            TotalPages = totalPages,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<ClinicalNoteDetailDto?> GetDetailAsync(int noteId)
    {
        // AsNoTracking: read-only detail projection; Patient/Provider/User
        // decrypted in-place for DTO, must not be tracked.
        var note = await _context.ClinicalNotes
            .AsNoTracking()
            .Include(n => n.Patient)
            .Include(n => n.Provider)
            .Include(n => n.Template)
            .FirstOrDefaultAsync(n => n.ClinicalNoteId == noteId);

        if (note == null) return null;

        // Decrypt PHI fields for Patient and Provider
        if (_encryptionHelper != null)
        {
            if (note.Patient != null)
                _encryptionHelper.DecryptEntity(note.Patient);
            if (note.Provider != null)
                _encryptionHelper.DecryptEntity(note.Provider);
        }

        // Get signer name
        string? signedByName = null;

        if (note.SignedByUserId.HasValue)
        {
            // Users table: User entity fields are not registered for encryption
            // per EncryptionConfiguration, but DecryptEntity is a no-op on such
            // entities. Still, detach defensively so reflection mutations (if any
            // future field becomes encrypted) don't get tracked.
            var signer = await _context.Users.FindAsync(note.SignedByUserId.Value);
            if (signer != null)
            {
                _context.Entry(signer).State = EntityState.Detached;
                _encryptionHelper?.DecryptEntity(signer);
                signedByName = $"{signer.FirstName} {signer.LastName}";
            }
        }

        // Decrypt content if encryption helper available
        var htmlContent = note.HtmlContent;
        if (_encryptionHelper != null && !string.IsNullOrEmpty(htmlContent))
        {
            htmlContent = _encryptionHelper.Decrypt(htmlContent) ?? htmlContent;
        }

        return new ClinicalNoteDetailDto
        {
            ClinicalNoteId = note.ClinicalNoteId,
            PatientId = note.PatientId,
            PatientName = note.Patient != null ? $"{note.Patient.FirstName} {note.Patient.LastName}" : "",
            PatientMRN = note.Patient?.Mrn ?? "",
            ProviderId = note.ProviderId,
            ProviderName = note.Provider != null ? $"{note.Provider.FirstName} {note.Provider.LastName}" : "",
            AppointmentId = note.AppointmentId,
            Type = note.Type,
            Status = note.Status,
            ServiceDate = note.ServiceDate,
            TemplateName = note.Template?.Name,
            CreatedAt = note.CreatedAt,
            HtmlContent = htmlContent,
            TemplateId = note.TemplateId,
            SignedAt = note.SignedAt,
            SignedByUserId = note.SignedByUserId,
            SignedByName = signedByName
        };
    }

    public async Task<ClinicalNote> CreateAsync(ClinicalNoteCreateDto dto, int createdByUserId)
    {
        if (!_tenantProvider.TenantId.HasValue)
            throw new InvalidOperationException("Tenant context required");

        // Encrypt content if encryption helper available
        var htmlContent = dto.HtmlContent;
        string? searchHash = null;

        if (_encryptionHelper != null && !string.IsNullOrEmpty(htmlContent))
        {
            searchHash = _encryptionHelper.GenerateSearchHash(StripHtml(htmlContent));
            htmlContent = _encryptionHelper.Encrypt(htmlContent) ?? htmlContent;
        }

        var note = new ClinicalNote
        {
            TenantId = _tenantProvider.TenantId.Value,
            PatientId = dto.PatientId,
            ProviderId = dto.ProviderId,
            AppointmentId = dto.AppointmentId,
            TemplateId = dto.TemplateId,
            Type = dto.Type,
            Status = 0, // Draft
            ServiceDate = dto.ServiceDate,
            HtmlContent = htmlContent,
            SearchHash = searchHash,
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTime.UtcNow
        };

        // Link note to encounter if appointment has one
        if (dto.AppointmentId.HasValue)
        {
            var encounter = await _context.Encounters
                .FirstOrDefaultAsync(e => e.AppointmentId == dto.AppointmentId.Value && e.TenantId == _tenantProvider.TenantId.Value);

            if (encounter != null)
            {
                note.EncounterId = encounter.EncounterId;
            }
            else
            {
                // Auto-create encounter if none exists (e.g., note created before check-in)
                var appointment = await _context.Appointments.FindAsync(dto.AppointmentId.Value);
                if (appointment != null)
                {
                    var newEncounter = new Encounter
                    {
                        TenantId = _tenantProvider.TenantId.Value,
                        PatientId = dto.PatientId,
                        ProviderId = dto.ProviderId,
                        AppointmentId = dto.AppointmentId,
                        EncounterDate = DateOnly.FromDateTime(DateTime.UtcNow),
                        ChiefComplaint = appointment.Reason ?? "",
                        Status = 0, // Open
                        CreatedByUserId = createdByUserId,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.Encounters.Add(newEncounter);
                    await _context.SaveChangesAsync(); // Save to get EncounterId
                    note.EncounterId = newEncounter.EncounterId;
                }
            }
        }
        else if (dto.EncounterId.HasValue)
        {
            note.EncounterId = dto.EncounterId;
        }

        _context.ClinicalNotes.Add(note);

        // AUTO-CHECK-IN: When a clinical note is created for an appointment,
        // automatically check in the patient if not already checked in
        if (dto.AppointmentId.HasValue)
        {
            var appt = await _context.Appointments.FindAsync(dto.AppointmentId.Value);
            if (appt != null &&
                (appt.Status == (int)AppointmentStatus.Scheduled ||
                 appt.Status == (int)AppointmentStatus.Confirmed))
            {
                appt.Status = (int)AppointmentStatus.CheckedIn;
                appt.CheckInTime = DateTime.UtcNow;
                appt.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync();

        // Send SignalR notification for real-time dashboard updates
        await SendNoteChangeNotificationAsync(note, "created", createdByUserId);

        return note;
    }

    public async Task<ClinicalNote?> UpdateAsync(int noteId, ClinicalNoteUpdateDto dto)
    {
        var note = await _context.ClinicalNotes.FindAsync(noteId);

        // Only allow updating draft notes (Status == 0)
        if (note == null || note.Status != 0)
            return null;

        if (dto.HtmlContent != null)
        {
            var htmlContent = dto.HtmlContent;
            if (_encryptionHelper != null)
            {
                note.SearchHash = _encryptionHelper.GenerateSearchHash(StripHtml(htmlContent));
                htmlContent = _encryptionHelper.Encrypt(htmlContent) ?? htmlContent;
            }
            note.HtmlContent = htmlContent;
        }

        note.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return note;
    }

    public async Task<ClinicalNote?> SignNoteAsync(int noteId, int signedByUserId, string? signatureData = null)
    {
        var note = await _context.ClinicalNotes
            .Include(n => n.Provider)
            .Include(n => n.Appointment)
            .FirstOrDefaultAsync(n => n.ClinicalNoteId == noteId);

        // Only allow signing draft notes
        if (note == null || note.Status != 0)
            return null;

        note.SignedByUserId = signedByUserId;
        note.SignedAt = DateTime.UtcNow;

        if (signatureData != null && _encryptionHelper != null)
        {
            note.SignatureData = _encryptionHelper.Encrypt(signatureData);
        }

        // Replace {{provider_signature}} placeholder with actual signature image if present
        await ReplaceProviderSignaturePlaceholderAsync(note);

        note.Status = 2; // Signed
        note.UpdatedAt = DateTime.UtcNow;

        // Update linked encounter with note summary when signed
        if (note.EncounterId.HasValue)
        {
            var encounter = await _context.Encounters.FindAsync(note.EncounterId.Value);
            if (encounter != null)
            {
                // Extract Assessment/Plan from note HTML if available
                var plainText = !string.IsNullOrEmpty(note.HtmlContent)
                    ? StripHtml(_encryptionHelper != null ? (_encryptionHelper.Decrypt(note.HtmlContent) ?? note.HtmlContent) : note.HtmlContent)
                    : "";
                if (!string.IsNullOrEmpty(plainText) && string.IsNullOrEmpty(encounter.Assessment))
                {
                    // Set a summary from the note content (first 500 chars as assessment summary)
                    encounter.Assessment = plainText.Length > 500 ? plainText.Substring(0, 500) + "..." : plainText;
                }
                encounter.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync();

        // Send SignalR notification for real-time dashboard updates
        await SendNoteChangeNotificationAsync(note, "signed", signedByUserId);

        // Auto-claim creation moved to AppointmentService.CheckOutAsync (encounter completion)

        return note;
    }

    private async Task ReplaceProviderSignaturePlaceholderAsync(ClinicalNote note)
    {
        if (note.Provider == null || string.IsNullOrEmpty(note.HtmlContent))
            return;

        // Decrypt HTML content if encrypted
        var htmlContent = note.HtmlContent;
        if (_encryptionHelper != null)
        {
            try
            {
                htmlContent = _encryptionHelper.Decrypt(note.HtmlContent);
            }
            catch
            {
                // Content may not be encrypted
            }
        }

        // Check if {{provider_signature}} placeholder exists (case-insensitive)
        if (!htmlContent.Contains("{{provider_signature}}", StringComparison.OrdinalIgnoreCase))
            return;

        // Get provider's signature image path
        if (string.IsNullOrEmpty(note.Provider.SignatureImagePath))
        {
            // No signature uploaded - leave placeholder as is
            // Frontend will replace it with "[No signature on file]" message
            return;
        }

        // Download signature from cloud storage if storage service is available
        if (_storageService == null)
        {
            // Storage service not available - leave placeholder as is
            return;
        }

        try
        {
            // Download the signature file from cloud storage
            var signatureBytes = await _storageService.DownloadBytesAsync(note.Provider.SignatureImagePath);

            if (signatureBytes == null || signatureBytes.Length == 0)
            {
                // Signature file not found in cloud storage - leave placeholder as is
                return;
            }

            // Convert to base64
            var base64Signature = Convert.ToBase64String(signatureBytes);

            // Determine content type from extension
            var extension = Path.GetExtension(note.Provider.SignatureImagePath).ToLowerInvariant();
            var contentType = extension switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                _ => "image/png"
            };

            // Create the img tag with base64 data
            var signatureHtml = $"<img src=\"data:{contentType};base64,{base64Signature}\" alt=\"Provider Signature\" style=\"max-height: 80px; max-width: 300px;\" />";

            // Replace the placeholder (case-insensitive)
            htmlContent = Regex.Replace(htmlContent, @"\{\{provider_signature\}\}", signatureHtml, RegexOptions.IgnoreCase);

            // Encrypt and save back
            if (_encryptionHelper != null)
            {
                note.HtmlContent = _encryptionHelper.Encrypt(htmlContent);
            }
            else
            {
                note.HtmlContent = htmlContent;
            }
        }
        catch (Exception)
        {
            // Failed to download or process signature - leave placeholder as is
            // Frontend will handle displaying the "[No signature on file]" message
            return;
        }
    }

    public async Task<(bool Success, string? ErrorMessage)> DeleteAsync(int noteId, int userId, string? userIp)
    {
        var note = await _context.ClinicalNotes
            .Include(n => n.Patient)
            .Include(n => n.Template)
            .FirstOrDefaultAsync(n => n.ClinicalNoteId == noteId);

        if (note == null)
            return (false, "Clinical note not found");

        // Only allow deletion of draft notes (Status == 0)
        if (note.Status != 0)
            return (false, "Only draft notes can be deleted. Signed notes cannot be deleted.");

        // Get the user to check their role
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return (false, "User not found");

        // Check if user is the creator, the provider on the note, or is SuperAdmin
        bool isCreator = note.CreatedByUserId == userId;
        bool isNoteProvider = user.ProviderId.HasValue && user.ProviderId == note.ProviderId;
        bool isSuperAdmin = user.Role == 0;

        // ClinicAdmin (Role 1) cannot delete - they can only view signed notes
        if (user.Role == 1)
            return (false, "Clinic administrators can only view signed clinical notes");

        if (!isCreator && !isNoteProvider && !isSuperAdmin)
            return (false, "You can only delete clinical notes that you created or are the provider for");

        // Store note info for audit before deletion
        var patientName = note.Patient != null
            ? $"{note.Patient.FirstName} {note.Patient.LastName}"
            : "Unknown";
        var noteTitle = note.Template?.Name ?? "Untitled";

        // Delete the note
        _context.ClinicalNotes.Remove(note);
        await _context.SaveChangesAsync();

        // Audit log the deletion
        if (_auditService != null)
        {
            await _auditService.LogAccessAsync(
                userId,
                null, // userEmail
                "CLINICAL_NOTE_DELETED",
                "ClinicalNote",
                noteId,
                null, // oldValues
                $"{{\"noteId\":{noteId},\"noteTitle\":\"{noteTitle.Replace("\"", "'")}\",\"patientId\":{note.PatientId},\"patientName\":\"{patientName.Replace("\"", "'")}\",\"deletedBy\":{userId},\"wasCreator\":{isCreator.ToString().ToLower()},\"wasProvider\":{isNoteProvider.ToString().ToLower()}}}",
                userIp);
        }

        return (true, null);
    }

    public async Task<bool> HasNoteForAppointmentAsync(int appointmentId)
    {
        return await _context.ClinicalNotes.AnyAsync(n => n.AppointmentId == appointmentId);
    }

    public async Task<int?> GetNoteIdForAppointmentAsync(int appointmentId)
    {
        return await _context.ClinicalNotes
            .Where(n => n.AppointmentId == appointmentId)
            .Select(n => (int?)n.ClinicalNoteId)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// Get the last signed clinical note for a patient by a specific provider.
    /// Used for the "Duplicate from Last" feature to pre-fill new notes.
    /// Only returns notes from specific appointment types (e.g., Follow-Up for Daily Progress Notes).
    /// </summary>
    /// <param name="patientId">Patient ID</param>
    /// <param name="providerId">Provider ID (to only get notes signed by the same provider)</param>
    /// <param name="templateId">Optional template ID to filter by note type</param>
    /// <param name="appointmentType">Optional appointment type to filter (1 = Follow-Up for Daily Progress Notes)</param>
    /// <returns>The last signed note with decrypted content, or null if none found</returns>
    public async Task<ClinicalNoteDetailDto?> GetLastSignedNoteAsync(int patientId, int providerId, int? templateId = null, int? appointmentType = null)
    {
        var query = _context.ClinicalNotes
            .Include(n => n.Patient)
            .Include(n => n.Provider)
            .Include(n => n.Template)
            .Include(n => n.Appointment)
            .Where(n => n.PatientId == patientId
                     && n.ProviderId == providerId
                     && n.Status == 2); // Only signed notes

        if (_tenantProvider.TenantId.HasValue)
            query = query.Where(n => n.TenantId == _tenantProvider.TenantId.Value);

        // Filter by appointment type (e.g., only Follow-Up notes for Daily Progress Note duplication)
        if (appointmentType.HasValue)
            query = query.Where(n => n.Appointment != null && n.Appointment.Type == appointmentType.Value);

        // If templateId provided, filter by the same template (same note type)
        if (templateId.HasValue)
            query = query.Where(n => n.TemplateId == templateId.Value);

        // Get the most recent signed note
        // AsNoTracking: read-only duplicate lookup; Patient/Provider decrypted
        // for DTO, must not be tracked.
        var note = await query
            .AsNoTracking()
            .OrderByDescending(n => n.ServiceDate)
            .ThenByDescending(n => n.SignedAt)
            .FirstOrDefaultAsync();

        if (note == null) return null;

        // Decrypt PHI fields
        if (_encryptionHelper != null)
        {
            if (note.Patient != null)
                _encryptionHelper.DecryptEntity(note.Patient);
            if (note.Provider != null)
                _encryptionHelper.DecryptEntity(note.Provider);
        }

        // Decrypt content
        var htmlContent = note.HtmlContent;
        if (_encryptionHelper != null && !string.IsNullOrEmpty(htmlContent))
        {
            htmlContent = _encryptionHelper.Decrypt(htmlContent) ?? htmlContent;
        }

        return new ClinicalNoteDetailDto
        {
            ClinicalNoteId = note.ClinicalNoteId,
            PatientId = note.PatientId,
            PatientName = note.Patient != null ? $"{note.Patient.FirstName} {note.Patient.LastName}" : "",
            PatientMRN = note.Patient?.Mrn ?? "",
            ProviderId = note.ProviderId,
            ProviderName = note.Provider != null ? $"{note.Provider.FirstName} {note.Provider.LastName}" : "",
            AppointmentId = note.AppointmentId,
            Type = note.Type,
            Status = note.Status,
            ServiceDate = note.ServiceDate,
            TemplateName = note.Template?.Name,
            CreatedAt = note.CreatedAt,
            HtmlContent = htmlContent,
            TemplateId = note.TemplateId,
            SignedAt = note.SignedAt,
            SignedByUserId = note.SignedByUserId,
            SignedByName = null // Not needed for duplicate feature
        };
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        return Regex.Replace(html, "<[^>]*>", " ").Trim();
    }

    /// <summary>
    /// Sends SignalR notification for clinical note changes to enable real-time dashboard updates
    /// </summary>
    private async Task SendNoteChangeNotificationAsync(ClinicalNote note, string changeType, int? changedByUserId)
    {
        if (_notificationService == null || !_tenantProvider.TenantId.HasValue)
            return;

        try
        {
            // Load patient and provider if not already loaded
            var patient = note.Patient ?? await _context.Patients.FindAsync(note.PatientId);
            var provider = note.Provider ?? await _context.Providers.FindAsync(note.ProviderId);
            var template = note.Template ?? await _context.ClinicalNoteTemplates.FindAsync(note.TemplateId);

            // CRITICAL (PHI encryption safety):
            // Detach patient/provider before decrypting. This method is called from
            // note create/update/sign flows which do SaveChangesAsync. Without
            // detach, the decrypted patient/provider would be flushed to the DB
            // as plaintext.
            if (patient != null && _context.Entry(patient).State != EntityState.Detached)
                _context.Entry(patient).State = EntityState.Detached;
            if (provider != null && _context.Entry(provider).State != EntityState.Detached)
                _context.Entry(provider).State = EntityState.Detached;

            // Decrypt names if needed
            string patientName = "";
            string providerName = "";

            if (patient != null)
            {
                _encryptionHelper?.DecryptEntity(patient);
                patientName = $"{patient.FirstName} {patient.LastName}";
            }

            if (provider != null)
            {
                _encryptionHelper?.DecryptEntity(provider);
                providerName = $"{provider.FirstName} {provider.LastName}";
            }

            var statusNames = new Dictionary<int, string>
            {
                { 0, "Draft" },
                { 1, "PendingSignature" },
                { 2, "Signed" },
                { 3, "Amended" },
                { 4, "Final" }
            };

            var notification = new ClinicalNoteChangeNotification
            {
                ClinicalNoteId = note.ClinicalNoteId,
                ChangeType = changeType,
                PatientId = note.PatientId,
                PatientName = patientName,
                ProviderId = note.ProviderId,
                ProviderName = providerName,
                AppointmentId = note.AppointmentId,
                Status = note.Status,
                StatusName = statusNames.GetValueOrDefault(note.Status, "Unknown"),
                TemplateName = template?.Name,
                ServiceDate = new DateTime(note.ServiceDate.Year, note.ServiceDate.Month, note.ServiceDate.Day),
                ChangedAt = DateTime.UtcNow,
                ChangedByUserId = changedByUserId
            };

            await _notificationService.NotifyClinicalNoteChangedAsync(_tenantProvider.TenantId.Value, notification);
        }
        catch (Exception)
        {
            // Don't fail the main operation if notification fails
            // Logging is handled by the notification service
        }
    }
}
