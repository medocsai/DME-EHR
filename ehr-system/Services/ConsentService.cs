using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EHR.Helpers;
using EHR.Hubs;
using EHR.Models;
using EHR.Models.Generated;
using EHR.Services.Intake;
using EHR.Services.Intake.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EHR.Services;

/// <summary>
/// Service for managing patient consents.
/// Handles consent submission, PDF generation, and consent history retrieval.
/// </summary>
public interface IConsentService
{
    // Kiosk operations
    Task<KioskSubmitConsentResponseDto> SubmitConsentAsync(string sessionToken, KioskSubmitConsentRequestDto request, string ipAddress, string userAgent);

    // Portal operations (2026-05) — patient signs consent at home before arriving.
    Task<PortalConsentSubmitResponseDto> SubmitPortalConsentAsync(int patientId, int tenantId, PortalConsentSubmitRequestDto request, string ipAddress, string userAgent);
    Task<List<PortalAwaitingConsentDto>> GetPortalAwaitingConsentAsync(int patientId, int tenantId);
    Task<List<KioskConsentFormDto>> GetPortalConsentTemplatesAsync(int patientId, int tenantId, int appointmentId);

    // Kiosk "Yes, I am Here" — patient already consented from portal, just confirming arrival.
    Task<KioskSubmitConsentResponseDto> ConfirmKioskPresenceAsync(string sessionToken);

    // Admin operations
    Task<PatientConsentHistoryDto> GetPatientConsentHistoryAsync(int patientId);
    Task<PatientConsentStatusDto> GetPatientConsentStatusAsync(int patientId);
    Task<ConsentRecordDto> GetConsentByIdAsync(int consentId);
    Task<ConsentRecordDto> GetConsentForCareEpisodeAsync(int careEpisodeId);
    Task<ConsentRecordDto> GetConsentForAppointmentAsync(int appointmentId);
    Task<(byte[] pdfData, string fileName)?> GetConsentPdfAsync(int consentId);

    // Manual consent upload
    Task<ManualConsentUploadResponseDto> UploadManualConsentAsync(
        ManualConsentUploadRequestDto request,
        Stream pdfStream,
        string fileName,
        int uploadedByUserId,
        string ipAddress);

    // Care Episode linking
    Task LinkOrphanConsentsToCareepisodeAsync(int careEpisodeId);

    // Background work invoked fire-and-forget after a kiosk submit so the
    // patient's request returns instantly. Generates + stores the encrypted
    // consent PDF and pushes the SignalR notification to admins.
    Task GeneratePdfAndNotifyAsync(int consentId, int tenantId, ConsentCompletedNotification notification);
}

public class ConsentService : IConsentService
{
    private readonly EhrDbContext _context;
    private readonly EncryptionHelper _encryption;
    private readonly IConsentTemplateService _templateService;
    private readonly IKioskService _kioskService;
    private readonly IAuditService _auditService;
    private readonly IConsentNotificationService _notificationService;
    private readonly IHtmlToPdfService _htmlToPdfService;
    private readonly IIntakeProgressCalculator _intakeProgress;
    private readonly IIntakeAccessTokenService _intakeTokens;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ConsentService> _logger;
    private readonly byte[] _fileEncryptionKey;

    public ConsentService(
        EhrDbContext context,
        EncryptionHelper encryption,
        IConsentTemplateService templateService,
        IKioskService kioskService,
        IAuditService auditService,
        IConsentNotificationService notificationService,
        IHtmlToPdfService htmlToPdfService,
        IIntakeProgressCalculator intakeProgress,
        IIntakeAccessTokenService intakeTokens,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<ConsentService> logger)
    {
        _context = context;
        _encryption = encryption;
        _templateService = templateService;
        _kioskService = kioskService;
        _auditService = auditService;
        _notificationService = notificationService;
        _htmlToPdfService = htmlToPdfService;
        _intakeProgress = intakeProgress;
        _intakeTokens = intakeTokens;
        _scopeFactory = scopeFactory;
        _logger = logger;

        // Derive file encryption key
        var keyString = configuration["Encryption:Key"] ??
            throw new InvalidOperationException("Encryption:Key not found");
        using var deriveBytes = new Rfc2898DeriveBytes(
            keyString + "_CONSENT_PDF",
            Encoding.UTF8.GetBytes("PTEHR_CONSENT_SALT_2024"),
            100000,
            HashAlgorithmName.SHA256);
        _fileEncryptionKey = deriveBytes.GetBytes(32);
    }

    public async Task<KioskSubmitConsentResponseDto> SubmitConsentAsync(
        string sessionToken,
        KioskSubmitConsentRequestDto request,
        string ipAddress,
        string userAgent)
    {
        // Validate session
        var session = await _kioskService.GetValidSessionAsync(sessionToken);
        if (session == null)
        {
            return new KioskSubmitConsentResponseDto
            {
                Success = false,
                Message = "Your session has expired. Please start again."
            };
        }

        // Validate confirmation
        if (!request.ConfirmationChecked)
        {
            return new KioskSubmitConsentResponseDto
            {
                Success = false,
                Message = "Please confirm that you have read and understand the consent forms."
            };
        }

        // Validate all forms have required signatures
        var templates = await _templateService.GetRenderedFormsForPatientAsync(
            session.PatientId,
            session.AppointmentId,
            session.CareEpisodeId);

        if (request.Forms.Count != templates.Count)
        {
            return new KioskSubmitConsentResponseDto
            {
                Success = false,
                Message = "Not all forms have been completed. Please review all forms."
            };
        }

        foreach (var template in templates)
        {
            var submittedForm = request.Forms.FirstOrDefault(f => f.TemplateId == template.TemplateId);
            if (submittedForm == null)
            {
                return new KioskSubmitConsentResponseDto
                {
                    Success = false,
                    Message = $"Form '{template.FormName}' has not been completed."
                };
            }

            // Check all required signatures
            foreach (var sigField in template.SignatureFields.Where(s => s.IsRequired))
            {
                var signature = submittedForm.Signatures?.FirstOrDefault(s => s.FieldId == sigField.FieldId);
                if (signature == null || string.IsNullOrEmpty(signature.ImageData))
                {
                    return new KioskSubmitConsentResponseDto
                    {
                        Success = false,
                        Message = $"Signature '{sigField.Label}' on form '{template.FormName}' is required."
                    };
                }
            }
        }

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // Get patient for SSN verification info
            var patient = await _context.Patients.FindAsync(session.PatientId);
            var patientFirstName = _encryption.Decrypt(patient.FirstName) ?? patient.FirstName;

            // Determine consent type based on patient's care episode history
            // FirstTimePatient (NewCareEpisode=0): Patient's first care episode ever
            // ReturningPatient (ReturningVisit=1): Patient has at least one previous completed care episode
            var previousCompletedCareEpisodes = await _context.CareEpisodes
                .Where(ce => ce.PatientId == session.PatientId
                    && ce.Status == 1  // Status 1 = Completed
                    && (session.CareEpisodeId == null || ce.CareEpisodeId != session.CareEpisodeId))  // Exclude current
                .CountAsync();

            var consentType = previousCompletedCareEpisodes > 0
                ? (int)ConsentFormType.ReturningVisit
                : (int)ConsentFormType.NewCareEpisode;

            // Create the consent record
            var consent = new CareEpisodeConsent
            {
                TenantId = session.TenantId,
                CareEpisodeId = session.CareEpisodeId, // May be null for initial appointments
                PatientId = session.PatientId,
                AppointmentId = session.AppointmentId,
                LocationId = session.LocationId,
                ConsentType = consentType,
                SignedAt = DateTime.UtcNow,
                // SSN no longer captured for identity verification (2026-05).
                // Column kept on CareEpisodeConsent for historical audit rows;
                // new consents store null here.
                VerificationSsnLast4Encrypted = null,
                VerificationDob = patient.DateOfBirth,
                VerificationZipCode = _encryption.Decrypt(patient.ZipCode),
                IpAddress = ipAddress,
                UserAgent = userAgent?.Length > 500 ? userAgent[..500] : userAgent,
                FormCount = request.Forms.Count,
                CreatedAt = DateTime.UtcNow
            };
            _context.CareEpisodeConsents.Add(consent);
            await _context.SaveChangesAsync();

            // Create consent form records
            var order = 0;
            foreach (var submittedForm in request.Forms.OrderBy(f => f.TemplateId))
            {
                var template = templates.First(t => t.TemplateId == submittedForm.TemplateId);
                var templateEntity = await _context.ConsentFormTemplates.FindAsync(submittedForm.TemplateId);

                var consentForm = new CareEpisodeConsentForm
                {
                    TenantId = session.TenantId,
                    CareEpisodeConsentId = consent.CareEpisodeConsentId,
                    ConsentFormTemplateId = submittedForm.TemplateId,
                    TemplateVersion = templateEntity?.Version ?? 1,
                    FormName = template.FormName,
                    RenderedHtmlContent = _encryption.Encrypt(template.RenderedHtml),
                    SignaturesJson = JsonSerializer.Serialize(submittedForm.Signatures ?? new List<KioskSignatureSubmissionDto>()),
                    ViewedAt = submittedForm.ViewedAt,
                    ViewDurationSeconds = submittedForm.ViewDurationSeconds,
                    DisplayOrder = order++,
                    SignedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                };
                _context.CareEpisodeConsentForms.Add(consentForm);
            }
            await _context.SaveChangesAsync();

            // PDF generation is deferred to a fire-and-forget background task
            // (see end of this method) so the patient's request returns
            // instantly. PDF gets written back to consent.EncryptedPdfData
            // a few seconds later.

            // Update appointment status to Checked In (status 2)
            var appointment = await _context.Appointments.FindAsync(session.AppointmentId);
            if (appointment != null)
            {
                var wasNotCheckedIn = appointment.Status == 0 || appointment.Status == 1; // Scheduled or Confirmed
                appointment.Status = 2; // Checked In
                appointment.CheckInTime = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                // Auto-create Encounter (mirrors manual check-in behavior in AppointmentService.CheckInAsync)
                if (wasNotCheckedIn)
                {
                    var existingEncounter = await _context.Encounters
                        .AnyAsync(e => e.AppointmentId == session.AppointmentId);
                    if (!existingEncounter)
                    {
                        var encounter = new Encounter
                        {
                            TenantId = appointment.TenantId,
                            PatientId = appointment.PatientId,
                            ProviderId = appointment.ProviderId,
                            AppointmentId = appointment.AppointmentId,
                            EncounterDate = DateOnly.FromDateTime(DateTime.UtcNow),
                            ChiefComplaint = appointment.Reason ?? "",
                            Status = 0, // Open
                            CreatedByUserId = 0, // System-created
                            CreatedAt = DateTime.UtcNow
                        };
                        _context.Encounters.Add(encounter);
                        await _context.SaveChangesAsync();
                    }
                }
            }

            // Mark session as completed
            session.IsCompleted = true;
            await _context.SaveChangesAsync();

            await transaction.CommitAsync();

            _logger.LogInformation("Consent submitted for patient {PatientId}, appointment {AppointmentId}, consent {ConsentId}",
                session.PatientId, session.AppointmentId, consent.CareEpisodeConsentId);

            // Build SignalR notification payload now (needs decrypted patient
            // name + location lookup). The actual push is deferred along with
            // PDF generation in the fire-and-forget task below.
            var location = await _context.Locations.FindAsync(session.LocationId);
            var notification = new ConsentCompletedNotification
            {
                ConsentId = consent.CareEpisodeConsentId,
                PatientId = session.PatientId,
                PatientName = patientFirstName + " " + (_encryption.Decrypt(patient.LastName) ?? ""),
                AppointmentId = session.AppointmentId,
                AppointmentTime = appointment?.StartTime ?? DateTime.UtcNow,
                AppointmentType = GetAppointmentTypeName(appointment?.Type ?? 0),
                LocationId = session.LocationId,
                LocationName = location?.Name ?? "",
                FormCount = request.Forms.Count,
                CompletedAt = DateTime.UtcNow
            };

            // Fire-and-forget: generate PDF + push SignalR notification on a
            // background thread with its own DI scope. The patient's request
            // returns immediately after this. Failures are logged and do not
            // affect the consent record (PDF can be regenerated later).
            var consentIdForBg = consent.CareEpisodeConsentId;
            var tenantIdForBg = session.TenantId;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var bgService = scope.ServiceProvider.GetRequiredService<IConsentService>();
                    await bgService.GeneratePdfAndNotifyAsync(consentIdForBg, tenantIdForBg, notification);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background consent post-submit work failed for consent {ConsentId}", consentIdForBg);
                }
            });

            // Consent → intake handoff (rules/technical/consent-to-intake-handoff.md):
            // If the patient has an IntakePortalToken and intake is not yet
            // submitted, return the handoff block + signal data the controller
            // needs to set the verify cookie + write the audit row. Service
            // stays HttpContext-free; controller does the cookie work.
            //
            // Look up the location's stable kiosk URL token so the wizard can
            // redirect back to the SAME kiosk URL the patient came in on,
            // skipping the staff KioskSetup re-pick screen.
            var locationKioskToken = await _context.LocationKioskSettings
                .Where(k => k.LocationId == session.LocationId && k.IsEnabled == true)
                .Select(k => k.KioskToken)
                .FirstOrDefaultAsync();
            var (handoff, intakeTokenForCookie) = await BuildIntakeHandoffAsync(patient, locationKioskToken);

            return new KioskSubmitConsentResponseDto
            {
                Success = true,
                Message = "Your consent forms have been submitted successfully. You are now checked in.",
                ConsentId = consent.CareEpisodeConsentId,
                PatientFirstName = patientFirstName,
                AppointmentTime = appointment?.StartTime.ToString("h:mm tt") ?? "",
                ProviderName = session.Appointment?.Provider != null ?
                    $"{session.Appointment.Provider.FirstName} {session.Appointment.Provider.LastName}" : "",
                Intake = handoff,
                IntakeTokenForCookie = intakeTokenForCookie,
                PatientIdForAudit = handoff != null ? session.PatientId : null,
                KioskSessionIdForAudit = handoff != null ? session.KioskSessionId : null,
                TenantIdForAudit = handoff != null ? session.TenantId : null,
            };
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error submitting consent for session {SessionToken}", sessionToken);
            return new KioskSubmitConsentResponseDto
            {
                Success = false,
                Message = "An error occurred while submitting your consent. Please try again or see the front desk."
            };
        }
    }

    // ============================================================================
    // PORTAL CONSENT (2026-05)
    // ----------------------------------------------------------------------------
    // Patient signs consent forms from the patient portal BEFORE arriving at
    // the clinic. Identity is established by the portal JWT (no SSN/DOB re-ask).
    // When they later arrive at the kiosk, KioskService.VerifyPatientAsync sees
    // the consent already exists and the kiosk shows a "Yes, I am Here" screen
    // instead of the full forms-and-signatures flow.
    // ============================================================================

    /// <summary>
    /// List the patient's upcoming appointments (today through next 30 days)
    /// that don't yet have a consent record. Used by the portal Consent page +
    /// dashboard nudge widget.
    /// </summary>
    public async Task<List<PortalAwaitingConsentDto>> GetPortalAwaitingConsentAsync(int patientId, int tenantId)
    {
        var nowUtc = DateTime.UtcNow;
        var horizonUtc = nowUtc.AddDays(30);

        // Active appointments (Scheduled or Confirmed) where no consent row exists yet.
        var appointments = await _context.Appointments
            .Include(a => a.Provider)
            .Include(a => a.Location)
            .Where(a => a.TenantId == tenantId
                && a.PatientId == patientId
                && a.StartTime >= nowUtc
                && a.StartTime <= horizonUtc
                && (a.Status == (int)AppointmentStatus.Scheduled
                    || a.Status == (int)AppointmentStatus.Confirmed)
                && !_context.CareEpisodeConsents.Any(c => c.AppointmentId == a.AppointmentId))
            .OrderBy(a => a.StartTime)
            .ToListAsync();

        return appointments.Select(a =>
        {
            var tz = a.Location?.TimeZoneId ?? Helpers.TimezoneHelper.DefaultTimeZoneId;
            var providerName = a.Provider != null
                ? $"Dr. {_encryption.Decrypt(a.Provider.FirstName) ?? a.Provider.FirstName} " +
                  $"{_encryption.Decrypt(a.Provider.LastName) ?? a.Provider.LastName}".Trim()
                : "Provider";
            return new PortalAwaitingConsentDto
            {
                AppointmentId = a.AppointmentId,
                StartTime = a.StartTime,
                StartTimeFormatted = Helpers.TimezoneHelper.FormatTimeWithTimezone(a.StartTime, tz),
                DateFormatted = Helpers.TimezoneHelper.FormatDateWithTimezone(a.StartTime, tz),
                ProviderName = providerName,
                LocationName = a.Location?.Name,
                AppointmentType = GetAppointmentTypeName(a.Type),
                IsTelehealth = a.IsTelehealth ?? false
            };
        }).ToList();
    }

    /// <summary>
    /// Return the consent templates the patient needs to sign for a specific
    /// upcoming appointment. Reuses the same template-resolution logic the
    /// kiosk uses (location + form-type filters) so portal sees exactly the
    /// same forms they'd see at the kiosk.
    /// </summary>
    public async Task<List<KioskConsentFormDto>> GetPortalConsentTemplatesAsync(int patientId, int tenantId, int appointmentId)
    {
        // Verify appointment belongs to this patient within this tenant — defense
        // in depth against a malicious portal client passing someone else's
        // appointment id.
        var appointment = await _context.Appointments
            .FirstOrDefaultAsync(a => a.AppointmentId == appointmentId
                && a.PatientId == patientId
                && a.TenantId == tenantId);
        if (appointment == null) return new List<KioskConsentFormDto>();

        return await _templateService.GetRenderedFormsForPatientAsync(
            patientId, appointmentId, appointment.CareEpisodeId);
    }

    /// <summary>
    /// Submit consent from the patient portal. Same DB writes as the kiosk
    /// SubmitConsentAsync (CareEpisodeConsent + CareEpisodeConsentForm rows +
    /// fire-and-forget PDF generation), but skips the kiosk-specific bits:
    /// no KioskSession, no appointment-status transition (patient is still at
    /// home — they'll check in at the kiosk), no intake handoff (handled at
    /// the kiosk in the same flow that already exists).
    /// </summary>
    public async Task<PortalConsentSubmitResponseDto> SubmitPortalConsentAsync(
        int patientId, int tenantId, PortalConsentSubmitRequestDto request,
        string ipAddress, string userAgent)
    {
        // Patient + appointment must both exist and belong to this tenant.
        var patient = await _context.Patients
            .FirstOrDefaultAsync(p => p.PatientId == patientId && p.TenantId == tenantId);
        if (patient == null)
            return new PortalConsentSubmitResponseDto { Success = false, Message = "Patient record not found." };

        var appointment = await _context.Appointments
            .Include(a => a.Provider)
            .FirstOrDefaultAsync(a => a.AppointmentId == request.AppointmentId
                && a.PatientId == patientId
                && a.TenantId == tenantId);
        if (appointment == null)
            return new PortalConsentSubmitResponseDto { Success = false, Message = "Appointment not found." };

        // Idempotency: if a consent record already exists for this appointment,
        // return success without writing again (patient hit submit twice or kiosk
        // beat them to it).
        var existing = await _context.CareEpisodeConsents
            .FirstOrDefaultAsync(c => c.AppointmentId == request.AppointmentId);
        if (existing != null)
        {
            return new PortalConsentSubmitResponseDto
            {
                Success = true,
                ConsentId = existing.CareEpisodeConsentId,
                Message = "Your consent for this appointment is already on file."
            };
        }

        if (!request.ConfirmationChecked)
            return new PortalConsentSubmitResponseDto { Success = false, Message = "Please confirm that you have read and understood the consent forms." };

        // Resolve templates + validate signatures (same rules as kiosk).
        var templates = await _templateService.GetRenderedFormsForPatientAsync(
            patientId, request.AppointmentId, appointment.CareEpisodeId);
        if (request.Forms.Count != templates.Count)
            return new PortalConsentSubmitResponseDto { Success = false, Message = "Not all forms have been completed." };

        foreach (var template in templates)
        {
            var submittedForm = request.Forms.FirstOrDefault(f => f.TemplateId == template.TemplateId);
            if (submittedForm == null)
                return new PortalConsentSubmitResponseDto { Success = false, Message = $"Form '{template.FormName}' has not been completed." };

            foreach (var sigField in template.SignatureFields.Where(s => s.IsRequired))
            {
                var signature = submittedForm.Signatures?.FirstOrDefault(s => s.FieldId == sigField.FieldId);
                if (signature == null || string.IsNullOrEmpty(signature.ImageData))
                    return new PortalConsentSubmitResponseDto { Success = false, Message = $"Signature '{sigField.Label}' on form '{template.FormName}' is required." };
            }
        }

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var previousCompletedCareEpisodes = await _context.CareEpisodes
                .Where(ce => ce.PatientId == patientId && ce.Status == 1)
                .CountAsync();
            var consentType = previousCompletedCareEpisodes > 0
                ? (int)ConsentFormType.ReturningVisit
                : (int)ConsentFormType.NewCareEpisode;

            var locationId = appointment.LocationId ?? patient.PreferredLocationId ?? 0;

            var consent = new CareEpisodeConsent
            {
                TenantId = tenantId,
                CareEpisodeId = appointment.CareEpisodeId,
                PatientId = patientId,
                AppointmentId = request.AppointmentId,
                LocationId = locationId,
                ConsentType = consentType,
                SignedAt = DateTime.UtcNow,
                VerificationSsnLast4Encrypted = null,
                VerificationDob = patient.DateOfBirth,
                VerificationZipCode = _encryption.Decrypt(patient.ZipCode),
                IpAddress = ipAddress,
                UserAgent = userAgent?.Length > 500 ? userAgent[..500] : userAgent,
                FormCount = request.Forms.Count,
                CreatedAt = DateTime.UtcNow
            };
            _context.CareEpisodeConsents.Add(consent);
            await _context.SaveChangesAsync();

            var order = 0;
            foreach (var submittedForm in request.Forms.OrderBy(f => f.TemplateId))
            {
                var template = templates.First(t => t.TemplateId == submittedForm.TemplateId);
                var templateEntity = await _context.ConsentFormTemplates.FindAsync(submittedForm.TemplateId);

                _context.CareEpisodeConsentForms.Add(new CareEpisodeConsentForm
                {
                    TenantId = tenantId,
                    CareEpisodeConsentId = consent.CareEpisodeConsentId,
                    ConsentFormTemplateId = submittedForm.TemplateId,
                    TemplateVersion = templateEntity?.Version ?? 1,
                    FormName = template.FormName,
                    RenderedHtmlContent = _encryption.Encrypt(template.RenderedHtml),
                    SignaturesJson = JsonSerializer.Serialize(submittedForm.Signatures ?? new List<KioskSignatureSubmissionDto>()),
                    ViewedAt = submittedForm.ViewedAt,
                    ViewDurationSeconds = submittedForm.ViewDurationSeconds,
                    DisplayOrder = order++,
                    SignedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                });
            }
            await _context.SaveChangesAsync();

            await transaction.CommitAsync();

            _logger.LogInformation("Portal consent submitted for patient {PatientId}, appointment {AppointmentId}, consent {ConsentId}",
                patientId, request.AppointmentId, consent.CareEpisodeConsentId);

            // Background PDF + notification (same fire-and-forget pattern as kiosk).
            var patientFirstName = _encryption.Decrypt(patient.FirstName) ?? patient.FirstName;
            var patientLastName = _encryption.Decrypt(patient.LastName) ?? patient.LastName;
            var location = await _context.Locations.FindAsync(locationId);
            var notification = new ConsentCompletedNotification
            {
                ConsentId = consent.CareEpisodeConsentId,
                PatientId = patientId,
                PatientName = $"{patientFirstName} {patientLastName}".Trim(),
                AppointmentId = request.AppointmentId,
                AppointmentTime = appointment.StartTime,
                AppointmentType = GetAppointmentTypeName(appointment.Type),
                LocationId = locationId,
                LocationName = location?.Name ?? "",
                FormCount = request.Forms.Count,
                CompletedAt = DateTime.UtcNow
            };
            var consentIdForBg = consent.CareEpisodeConsentId;
            var tenantIdForBg = tenantId;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var bgService = scope.ServiceProvider.GetRequiredService<IConsentService>();
                    await bgService.GeneratePdfAndNotifyAsync(consentIdForBg, tenantIdForBg, notification);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background portal-consent post-submit work failed for consent {ConsentId}", consentIdForBg);
                }
            });

            var tz = location?.TimeZoneId ?? Helpers.TimezoneHelper.DefaultTimeZoneId;
            var providerName = appointment.Provider != null
                ? $"Dr. {_encryption.Decrypt(appointment.Provider.FirstName) ?? appointment.Provider.FirstName} " +
                  $"{_encryption.Decrypt(appointment.Provider.LastName) ?? appointment.Provider.LastName}".Trim()
                : "Your Provider";

            return new PortalConsentSubmitResponseDto
            {
                Success = true,
                ConsentId = consent.CareEpisodeConsentId,
                Message = "Your consent forms have been submitted. See you at your appointment.",
                AppointmentTime = Helpers.TimezoneHelper.FormatTimeWithTimezone(appointment.StartTime, tz),
                ProviderName = providerName
            };
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error submitting portal consent for patient {PatientId}, appointment {AppointmentId}",
                patientId, request.AppointmentId);
            return new PortalConsentSubmitResponseDto
            {
                Success = false,
                Message = "An error occurred while submitting your consent. Please try again."
            };
        }
    }

    /// <summary>
    /// Kiosk "Yes, I am Here" — called when the patient already has a consent
    /// record (typically signed via the portal) and is now physically at the
    /// kiosk confirming arrival. Transitions the appointment to CheckedIn,
    /// auto-creates the Encounter (same as the kiosk consent-submit flow does
    /// internally), and builds the same intake handoff. The response shape
    /// matches KioskSubmitConsentResponseDto so the kiosk JS can reuse its
    /// existing post-submit handler (intake-button + auto-advance screen).
    /// </summary>
    public async Task<KioskSubmitConsentResponseDto> ConfirmKioskPresenceAsync(string sessionToken)
    {
        var session = await _kioskService.GetValidSessionAsync(sessionToken);
        if (session == null)
        {
            return new KioskSubmitConsentResponseDto
            {
                Success = false,
                Message = "Your session has expired. Please start again."
            };
        }

        var patient = await _context.Patients.FindAsync(session.PatientId);
        if (patient == null)
        {
            return new KioskSubmitConsentResponseDto
            {
                Success = false,
                Message = "Patient record not found."
            };
        }

        var patientFirstName = _encryption.Decrypt(patient.FirstName) ?? patient.FirstName;

        // Appointment check-in + auto-encounter — same logic block as
        // SubmitConsentAsync (lines ~248-280) so behavior is identical whether
        // the patient signed at home or at the kiosk.
        var appointment = await _context.Appointments.FindAsync(session.AppointmentId);
        if (appointment != null)
        {
            var wasNotCheckedIn = appointment.Status == 0 || appointment.Status == 1;
            if (wasNotCheckedIn)
            {
                appointment.Status = 2; // CheckedIn
                appointment.CheckInTime = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                var existingEncounter = await _context.Encounters
                    .AnyAsync(e => e.AppointmentId == session.AppointmentId);
                if (!existingEncounter)
                {
                    _context.Encounters.Add(new Encounter
                    {
                        TenantId = appointment.TenantId,
                        PatientId = appointment.PatientId,
                        ProviderId = appointment.ProviderId,
                        AppointmentId = appointment.AppointmentId,
                        EncounterDate = DateOnly.FromDateTime(DateTime.UtcNow),
                        ChiefComplaint = appointment.Reason ?? "",
                        Status = 0,
                        CreatedByUserId = 0,
                        CreatedAt = DateTime.UtcNow
                    });
                    await _context.SaveChangesAsync();
                }
            }
        }

        session.IsCompleted = true;
        await _context.SaveChangesAsync();

        // Intake handoff — reuse the same path the kiosk consent submit uses.
        var locationKioskToken = await _context.LocationKioskSettings
            .Where(k => k.LocationId == session.LocationId && k.IsEnabled == true)
            .Select(k => k.KioskToken)
            .FirstOrDefaultAsync();
        var (handoff, intakeTokenForCookie) = await BuildIntakeHandoffAsync(patient, locationKioskToken);

        return new KioskSubmitConsentResponseDto
        {
            Success = true,
            Message = "You're checked in. We'll be with you shortly.",
            PatientFirstName = patientFirstName,
            AppointmentTime = appointment?.StartTime.ToString("h:mm tt") ?? "",
            ProviderName = session.Appointment?.Provider != null
                ? $"{session.Appointment.Provider.FirstName} {session.Appointment.Provider.LastName}"
                : "",
            Intake = handoff,
            IntakeTokenForCookie = intakeTokenForCookie,
            PatientIdForAudit = handoff != null ? session.PatientId : null,
            KioskSessionIdForAudit = handoff != null ? session.KioskSessionId : null,
            TenantIdForAudit = handoff != null ? session.TenantId : null,
        };
    }

    /// <summary>
    /// Decides whether a consent → intake handoff applies for this patient and
    /// builds the response block + cookie token signal for the controller.
    ///
    /// Returns (null, null) only when intake is already submitted
    /// (SubmittedAt != null) — the existing 5-second auto-advance Thank You
    /// behavior stays in that case.
    ///
    /// If the patient has no IntakePortalToken yet, one is auto-generated here.
    /// Without this, the feature would dead-end for any patient whose intake
    /// QR was never opened by staff (the common case for new patients walking
    /// in for their first appointment, since IntakePortalToken is lazily
    /// created on the first staff-side "Show QR" click).
    ///
    /// Otherwise returns (KioskIntakeHandoffDto, intakeToken) where State is
    /// "not_started" (Completed == 0) or "partial" (Completed > 0). The Url
    /// includes ?return=kiosk so the wizard exits back to the kiosk. When
    /// <paramref name="locationKioskToken"/> is non-empty it's also embedded as
    /// &amp;kt=&lt;token&gt; so the wizard can return directly to the same
    /// /Kiosk?token=&lt;token&gt;&amp;app=1 URL the patient came in on, instead
    /// of the staff KioskSetup re-pick screen.
    /// See rules/technical/consent-to-intake-handoff.md.
    /// </summary>
    internal async Task<(KioskIntakeHandoffDto, Guid?)> BuildIntakeHandoffAsync(
        Patient patient,
        string locationKioskToken = null)
    {
        if (patient == null) return (null, null);

        IntakeProgressDto progress;
        try
        {
            progress = await _intakeProgress.CalculateAsync(patient.PatientId);
        }
        catch (Exception ex)
        {
            // Defensive: a calculator failure must not break consent submit.
            // We return no handoff and let the Thank You screen behave as before.
            _logger.LogWarning(ex, "Intake progress calculation failed for patient {PatientId}; skipping handoff", patient.PatientId);
            return (null, null);
        }

        if (progress.SubmittedAt != null)
            return (null, null);

        // Auto-generate the intake token if missing. Same logic as the
        // staff-side "Show QR" button (IntakeAccessTokenService.GetOrCreateTokenAsync).
        Guid intakeToken;
        try
        {
            intakeToken = await _intakeTokens.GetOrCreateTokenAsync(patient.PatientId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get/create IntakePortalToken for patient {PatientId}; skipping handoff", patient.PatientId);
            return (null, null);
        }

        var state = progress.Completed > 0 ? "partial" : "not_started";
        var url = $"/intake/p/{intakeToken:D}/wizard?return=kiosk";
        if (!string.IsNullOrEmpty(locationKioskToken))
        {
            url += $"&kt={Uri.EscapeDataString(locationKioskToken)}";
        }

        return (new KioskIntakeHandoffDto
        {
            State = state,
            Url = url,
            Completed = progress.Completed,
            Total = progress.Total,
        }, intakeToken);
    }

    public async Task<PatientConsentHistoryDto> GetPatientConsentHistoryAsync(int patientId)
    {
        var patient = await _context.Patients
            .FirstOrDefaultAsync(p => p.PatientId == patientId);

        if (patient == null)
            return null;

        var patientName = $"{_encryption.Decrypt(patient.FirstName)} {_encryption.Decrypt(patient.LastName)}";

        // Get all care episodes for this patient
        var careEpisodes = await _context.CareEpisodes
            .Include(ce => ce.PrimaryProvider)
            .Where(ce => ce.PatientId == patientId)
            .OrderByDescending(ce => ce.StartDate)
            .ToListAsync();

        // Get all consents for this patient
        var consents = await _context.CareEpisodeConsents
            .Include(c => c.CareEpisodeConsentForms)
            .Include(c => c.Appointment)
                .ThenInclude(a => a.Location)
            .Where(c => c.PatientId == patientId)
            .OrderByDescending(c => c.SignedAt)
            .ToListAsync();

        // Group by care episode - get ALL consents for each care episode
        var careEpisodeConsents = new List<CareEpisodeConsentGroupDto>();
        foreach (var ce in careEpisodes)
        {
            // Get ALL consents for this care episode, not just the first one
            var episodeConsents = consents
                .Where(c => c.CareEpisodeId == ce.CareEpisodeId)
                .Select(c => MapToConsentRecordDto(c))
                .ToList();

            careEpisodeConsents.Add(new CareEpisodeConsentGroupDto
            {
                CareEpisodeId = ce.CareEpisodeId,
                Diagnosis = ce.PrimaryDiagnosisDescription ?? "No diagnosis",
                StartDate = ce.StartDate,
                EndDate = ce.EndDate,
                ProviderName = ce.PrimaryProvider != null ? $"{ce.PrimaryProvider.FirstName} {ce.PrimaryProvider.LastName}" : null,
                Status = ce.Status ?? 0,
                StatusName = GetCareEpisodeStatusName(ce.Status ?? 0),
                Consents = episodeConsents
            });
        }

        // Get orphan consents (no care episode)
        var orphanConsents = consents
            .Where(c => c.CareEpisodeId == null)
            .Select(c => MapToConsentRecordDto(c))
            .ToList();

        return new PatientConsentHistoryDto
        {
            PatientId = patientId,
            PatientName = patientName,
            CareEpisodeConsents = careEpisodeConsents,
            OrphanConsents = orphanConsents
        };
    }

    public async Task<PatientConsentStatusDto> GetPatientConsentStatusAsync(int patientId)
    {
        var patient = await _context.Patients
            .FirstOrDefaultAsync(p => p.PatientId == patientId);

        if (patient == null)
            return null;

        var patientName = $"{_encryption.Decrypt(patient.FirstName)} {_encryption.Decrypt(patient.LastName)}";

        // Get current (active) care episode
        var currentCareEpisode = await _context.CareEpisodes
            .Include(ce => ce.CareEpisodeConsents)
            .Where(ce => ce.PatientId == patientId && ce.Status == 0) // Active
            .OrderByDescending(ce => ce.StartDate)
            .FirstOrDefaultAsync();

        // Get upcoming appointment without consent
        var today = DateTime.Today;
        var upcomingAppointment = await _context.Appointments
            .Include(a => a.Provider)
            .Include(a => a.CareEpisodeConsents)
            .Where(a => a.PatientId == patientId
                && a.StartTime >= today
                && a.Status != 6 // Not cancelled
                && !a.CareEpisodeConsents.Any())
            .OrderBy(a => a.StartTime)
            .FirstOrDefaultAsync();

        ConsentRecordDto currentConsentDto = null;
        if (currentCareEpisode?.CareEpisodeConsents.Any() == true)
        {
            var consent = currentCareEpisode.CareEpisodeConsents.First();
            var fullConsent = await _context.CareEpisodeConsents
                .Include(c => c.CareEpisodeConsentForms)
                .Include(c => c.Appointment)
                    .ThenInclude(a => a.Location)
                .FirstOrDefaultAsync(c => c.CareEpisodeConsentId == consent.CareEpisodeConsentId);
            if (fullConsent != null)
                currentConsentDto = MapToConsentRecordDto(fullConsent);
        }

        return new PatientConsentStatusDto
        {
            PatientId = patientId,
            PatientName = patientName,
            CurrentCareEpisodeId = currentCareEpisode?.CareEpisodeId,
            CurrentCareEpisodeHasConsent = currentCareEpisode?.CareEpisodeConsents.Any() == true,
            CurrentCareEpisodeConsent = currentConsentDto,
            HasUpcomingAppointmentNeedingConsent = upcomingAppointment != null,
            UpcomingAppointment = upcomingAppointment != null ? new KioskAppointmentInfoDto
            {
                AppointmentId = upcomingAppointment.AppointmentId,
                StartTime = upcomingAppointment.StartTime,
                AppointmentType = GetAppointmentTypeName(upcomingAppointment.Type),
                ProviderName = upcomingAppointment.Provider != null ?
                    $"{upcomingAppointment.Provider.FirstName} {upcomingAppointment.Provider.LastName}" : "",
                CareEpisodeId = upcomingAppointment.CareEpisodeId
            } : null
        };
    }

    public async Task<ConsentRecordDto> GetConsentByIdAsync(int consentId)
    {
        var consent = await _context.CareEpisodeConsents
            .Include(c => c.CareEpisodeConsentForms)
            .Include(c => c.Appointment)
                .ThenInclude(a => a.Location)
            .FirstOrDefaultAsync(c => c.CareEpisodeConsentId == consentId);

        if (consent == null)
            return null;

        return MapToConsentRecordDto(consent);
    }

    public async Task<ConsentRecordDto> GetConsentForCareEpisodeAsync(int careEpisodeId)
    {
        var consent = await _context.CareEpisodeConsents
            .Include(c => c.CareEpisodeConsentForms)
            .Include(c => c.Appointment)
                .ThenInclude(a => a.Location)
            .FirstOrDefaultAsync(c => c.CareEpisodeId == careEpisodeId);

        if (consent == null)
            return null;

        return MapToConsentRecordDto(consent);
    }

    public async Task<ConsentRecordDto> GetConsentForAppointmentAsync(int appointmentId)
    {
        var consent = await _context.CareEpisodeConsents
            .Include(c => c.CareEpisodeConsentForms)
            .Include(c => c.Appointment)
                .ThenInclude(a => a.Location)
            .FirstOrDefaultAsync(c => c.AppointmentId == appointmentId);

        if (consent == null)
            return null;

        return MapToConsentRecordDto(consent);
    }

    public async Task<(byte[] pdfData, string fileName)?> GetConsentPdfAsync(int consentId)
    {
        var consent = await _context.CareEpisodeConsents
            .Include(c => c.Patient)
            .FirstOrDefaultAsync(c => c.CareEpisodeConsentId == consentId);

        if (consent == null || string.IsNullOrEmpty(consent.EncryptedPdfData))
            return null;

        try
        {
            var encryptedBytes = Convert.FromBase64String(consent.EncryptedPdfData);
            var pdfBytes = DecryptPdf(encryptedBytes);

            // Verify hash
            var hash = ComputeHash(pdfBytes);
            if (hash != consent.PdfHash)
            {
                _logger.LogError("PDF integrity check failed for consent {ConsentId}", consentId);
                return null;
            }

            var patientName = _encryption.Decrypt(consent.Patient?.LastName) ?? "Patient";
            var fileName = $"Consent_{patientName}_{consent.SignedAt:yyyyMMdd}.pdf";

            return (pdfBytes, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decrypting PDF for consent {ConsentId}", consentId);
            return null;
        }
    }

    public async Task LinkOrphanConsentsToCareepisodeAsync(int careEpisodeId)
    {
        var careEpisode = await _context.CareEpisodes
            .Include(ce => ce.Appointments)
            .FirstOrDefaultAsync(ce => ce.CareEpisodeId == careEpisodeId);

        if (careEpisode == null)
            return;

        // Get all appointments linked to this care episode
        var appointmentIds = careEpisode.Appointments.Select(a => a.AppointmentId).ToList();

        // Find orphan consents that should be linked:
        // 1. Consents with AppointmentId matching care episode's appointments, OR
        // 2. Consents without AppointmentId (manual uploads) signed within 30 days
        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
        var orphanConsents = await _context.CareEpisodeConsents
            .Where(c => c.PatientId == careEpisode.PatientId
                && c.CareEpisodeId == null
                && (
                    // Consents linked to care episode's appointments
                    (c.AppointmentId.HasValue && appointmentIds.Contains(c.AppointmentId.Value))
                    ||
                    // Manual uploads without appointment (signed within 30 days)
                    (!c.AppointmentId.HasValue && c.SignedAt >= thirtyDaysAgo)
                ))
            .ToListAsync();

        if (orphanConsents.Any())
        {
            foreach (var consent in orphanConsents)
            {
                consent.CareEpisodeId = careEpisodeId;
            }
            await _context.SaveChangesAsync();

            _logger.LogInformation("Linked {Count} orphan consent(s) to care episode {CareEpisodeId}",
                orphanConsents.Count, careEpisodeId);
        }
    }

    /// <summary>
    /// Upload a manually scanned consent form PDF.
    /// Used by clinic admin or front desk staff for paper-based consent collection.
    /// </summary>
    public async Task<ManualConsentUploadResponseDto> UploadManualConsentAsync(
        ManualConsentUploadRequestDto request,
        Stream pdfStream,
        string fileName,
        int uploadedByUserId,
        string ipAddress)
    {
        // Validate patient exists and belongs to current tenant
        var patient = await _context.Patients.FindAsync(request.PatientId);
        if (patient == null)
        {
            return new ManualConsentUploadResponseDto
            {
                Success = false,
                Message = "Patient not found"
            };
        }

        // Validate appointment if provided
        Appointment appointment = null;
        int? locationId = null;
        if (request.AppointmentId.HasValue)
        {
            appointment = await _context.Appointments
                .Include(a => a.Location)
                .FirstOrDefaultAsync(a => a.AppointmentId == request.AppointmentId.Value);

            if (appointment == null)
            {
                return new ManualConsentUploadResponseDto
                {
                    Success = false,
                    Message = "Appointment not found"
                };
            }

            if (appointment.PatientId != request.PatientId)
            {
                return new ManualConsentUploadResponseDto
                {
                    Success = false,
                    Message = "Appointment does not belong to the specified patient"
                };
            }

            locationId = appointment.LocationId;
        }

        // Validate care episode if provided, or auto-link to active care episode
        int? careEpisodeIdToUse = request.CareEpisodeId;
        if (request.CareEpisodeId.HasValue)
        {
            var careEpisode = await _context.CareEpisodes.FindAsync(request.CareEpisodeId.Value);
            if (careEpisode == null)
            {
                return new ManualConsentUploadResponseDto
                {
                    Success = false,
                    Message = "Care episode not found"
                };
            }

            if (careEpisode.PatientId != request.PatientId)
            {
                return new ManualConsentUploadResponseDto
                {
                    Success = false,
                    Message = "Care episode does not belong to the specified patient"
                };
            }
        }
        else
        {
            // Auto-link to active care episode if one exists for this patient
            var activeCareEpisode = await _context.CareEpisodes
                .Where(ce => ce.PatientId == request.PatientId && ce.Status == 0) // Status 0 = Active
                .OrderByDescending(ce => ce.StartDate)
                .FirstOrDefaultAsync();

            if (activeCareEpisode != null)
            {
                careEpisodeIdToUse = activeCareEpisode.CareEpisodeId;
                _logger.LogInformation(
                    "Auto-linking manual consent to active care episode {CareEpisodeId} for patient {PatientId}",
                    activeCareEpisode.CareEpisodeId, request.PatientId);
            }
        }

        // If no location from appointment, try to get from patient's preferred location or first tenant location
        if (!locationId.HasValue)
        {
            locationId = patient.PreferredLocationId;
        }

        // If still no location, get the first active location for this tenant
        if (!locationId.HasValue)
        {
            var defaultLocation = await _context.Locations
                .Where(l => l.TenantId == patient.TenantId && l.IsActive == true)
                .OrderByDescending(l => l.IsPrimary) // Prefer primary location
                .ThenBy(l => l.LocationId)
                .FirstOrDefaultAsync();

            if (defaultLocation != null)
            {
                locationId = defaultLocation.LocationId;
            }
            else
            {
                return new ManualConsentUploadResponseDto
                {
                    Success = false,
                    Message = "No location available for this clinic. Please create a location first."
                };
            }
        }

        // Read PDF bytes from stream
        byte[] pdfBytes;
        using (var memoryStream = new MemoryStream())
        {
            await pdfStream.CopyToAsync(memoryStream);
            pdfBytes = memoryStream.ToArray();
        }

        // Validate file size (max 10MB)
        const int maxFileSizeBytes = 10 * 1024 * 1024;
        if (pdfBytes.Length > maxFileSizeBytes)
        {
            return new ManualConsentUploadResponseDto
            {
                Success = false,
                Message = "File size exceeds maximum allowed (10MB)"
            };
        }

        // Validate PDF format (check PDF magic bytes)
        if (pdfBytes.Length < 4 ||
            pdfBytes[0] != 0x25 || // %
            pdfBytes[1] != 0x50 || // P
            pdfBytes[2] != 0x44 || // D
            pdfBytes[3] != 0x46)   // F
        {
            return new ManualConsentUploadResponseDto
            {
                Success = false,
                Message = "Invalid PDF file format"
            };
        }

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var signedAt = request.SignedAt ?? DateTime.UtcNow;

            // Create the consent record
            var consent = new CareEpisodeConsent
            {
                TenantId = patient.TenantId,
                CareEpisodeId = careEpisodeIdToUse, // Use auto-linked or provided care episode
                PatientId = request.PatientId,
                AppointmentId = request.AppointmentId, // null for manual uploads without appointment
                LocationId = locationId.Value, // locationId is guaranteed non-null at this point
                ConsentType = request.ConsentType,
                SignedAt = signedAt,
                VerificationDob = patient.DateOfBirth, // Use patient's DOB for verification
                IpAddress = ipAddress,
                UserAgent = "Manual Upload",
                FormCount = 1, // Manual uploads are single PDF
                CreatedAt = DateTime.UtcNow
            };

            _context.CareEpisodeConsents.Add(consent);
            await _context.SaveChangesAsync();

            // Create a consent form record with notes if provided
            var formName = !string.IsNullOrEmpty(request.Notes)
                ? $"Manual Upload - {request.Notes}"
                : "Manual Upload - Signed Consent Form";

            var consentForm = new CareEpisodeConsentForm
            {
                TenantId = patient.TenantId,
                CareEpisodeConsentId = consent.CareEpisodeConsentId,
                ConsentFormTemplateId = null, // NULL indicates manual upload (no template)
                TemplateVersion = 0,
                FormName = formName,
                RenderedHtmlContent = string.Empty, // No HTML content for manual uploads
                SignaturesJson = "[]", // Empty signatures array
                DisplayOrder = 0,
                SignedAt = signedAt,
                CreatedAt = DateTime.UtcNow
            };

            _context.CareEpisodeConsentForms.Add(consentForm);

            // Encrypt and store the PDF
            consent.EncryptedPdfData = Convert.ToBase64String(EncryptPdf(pdfBytes));
            consent.PdfHash = ComputeHash(pdfBytes);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation(
                "Manual consent uploaded for patient {PatientId}, consent {ConsentId}, uploaded by user {UserId}",
                request.PatientId, consent.CareEpisodeConsentId, uploadedByUserId);

            // Send notification for new consent
            var patientFirstName = _encryption.Decrypt(patient.FirstName) ?? "";
            var patientLastName = _encryption.Decrypt(patient.LastName) ?? "";

            await _notificationService.NotifyConsentCompletedAsync(patient.TenantId, new ConsentCompletedNotification
            {
                ConsentId = consent.CareEpisodeConsentId,
                PatientId = request.PatientId,
                PatientName = $"{patientFirstName} {patientLastName}".Trim(),
                AppointmentId = request.AppointmentId ?? 0,
                AppointmentTime = appointment?.StartTime ?? DateTime.UtcNow,
                AppointmentType = "Manual Upload",
                LocationId = locationId ?? 0,
                LocationName = appointment?.Location?.Name ?? "N/A",
                FormCount = 1,
                CompletedAt = DateTime.UtcNow
            });

            return new ManualConsentUploadResponseDto
            {
                Success = true,
                Message = "Consent form uploaded successfully",
                ConsentId = consent.CareEpisodeConsentId,
                UploadedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();

            // Get the full exception chain for detailed logging
            var innerEx = ex.InnerException;
            var fullMessage = ex.Message;
            while (innerEx != null)
            {
                fullMessage += $" --> {innerEx.Message}";
                innerEx = innerEx.InnerException;
            }

            _logger.LogError(ex, "Error uploading manual consent for patient {PatientId}. Full exception chain: {FullMessage}",
                request.PatientId, fullMessage);

            return new ManualConsentUploadResponseDto
            {
                Success = false,
                Message = $"Upload failed: {fullMessage}" // Include full error chain for debugging
            };
        }
    }

    #region Private Helper Methods

    private ConsentRecordDto MapToConsentRecordDto(CareEpisodeConsent consent)
    {
        return new ConsentRecordDto
        {
            CareEpisodeConsentId = consent.CareEpisodeConsentId,
            CareEpisodeId = consent.CareEpisodeId,
            AppointmentId = consent.AppointmentId,
            AppointmentDate = consent.Appointment?.StartTime ?? consent.SignedAt,
            AppointmentType = consent.Appointment != null ? GetAppointmentTypeName(consent.Appointment.Type) : "",
            ConsentType = consent.ConsentType,
            ConsentTypeName = consent.ConsentType == 0 ? "New Care Episode" : "Returning Visit",
            SignedAt = consent.SignedAt,
            FormCount = consent.FormCount,
            Forms = consent.CareEpisodeConsentForms?.Select(f => new ConsentFormSummaryDto
            {
                CareEpisodeConsentFormId = f.CareEpisodeConsentFormId,
                FormName = f.FormName,
                SignatureCount = CountSignatures(f.SignaturesJson),
                SignedAt = f.SignedAt
            }).ToList() ?? new List<ConsentFormSummaryDto>(),
            LocationName = consent.Appointment?.Location?.Name,
            IpAddress = consent.IpAddress
        };
    }

    private static int CountSignatures(string signaturesJson)
    {
        if (string.IsNullOrEmpty(signaturesJson))
            return 0;
        try
        {
            var signatures = JsonSerializer.Deserialize<List<KioskSignatureSubmissionDto>>(signaturesJson);
            return signatures?.Count ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Generates a PDF from the consent forms by rendering the stored HTML exactly as-is,
    /// with signatures embedded as images. No extra headers, footers, or labels are added.
    /// The PDF is an exact replica of the consent template.
    /// </summary>
    /// <summary>
    /// Background work invoked fire-and-forget after a kiosk consent submit.
    /// Generates the encrypted PDF + stores it on the consent row, then pushes
    /// the SignalR notification to admins. Runs inside a fresh DI scope (the
    /// original request scope is already disposed by this point). Errors are
    /// logged and swallowed — the consent record is already saved, so the
    /// patient flow is never affected; PDF can be regenerated on demand later.
    /// </summary>
    public async Task GeneratePdfAndNotifyAsync(int consentId, int tenantId, ConsentCompletedNotification notification)
    {
        try
        {
            var pdfBytes = await GenerateConsentPdfAsync(consentId);
            if (pdfBytes != null && pdfBytes.Length > 0)
            {
                var consent = await _context.CareEpisodeConsents
                    .FirstOrDefaultAsync(c => c.CareEpisodeConsentId == consentId);
                if (consent != null)
                {
                    consent.EncryptedPdfData = Convert.ToBase64String(EncryptPdf(pdfBytes));
                    consent.PdfHash = ComputeHash(pdfBytes);
                    await _context.SaveChangesAsync();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deferred PDF generation failed for consent {ConsentId}", consentId);
        }

        try
        {
            await _notificationService.NotifyConsentCompletedAsync(tenantId, notification);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deferred SignalR notification failed for consent {ConsentId}", consentId);
        }
    }

    private async Task<byte[]> GenerateConsentPdfAsync(int consentId)
    {
        var consent = await _context.CareEpisodeConsents
            .Include(c => c.CareEpisodeConsentForms)
            .FirstOrDefaultAsync(c => c.CareEpisodeConsentId == consentId);

        if (consent == null)
            return Array.Empty<byte>();

        // Build combined HTML document with all forms
        var htmlBuilder = new StringBuilder();
        htmlBuilder.AppendLine("<!DOCTYPE html>");
        htmlBuilder.AppendLine("<html><head>");
        htmlBuilder.AppendLine("<meta charset=\"UTF-8\">");
        htmlBuilder.AppendLine("<style>");
        // Page break CSS for multi-form documents
        htmlBuilder.AppendLine(".consent-form-page { page-break-after: always; }");
        htmlBuilder.AppendLine(".consent-form-page:last-child { page-break-after: avoid; }");
        // Hide interactive elements that shouldn't appear in PDF
        htmlBuilder.AppendLine(".signature-field canvas, .signature-actions, button, .clear-signature { display: none !important; }");
        // Basic styling for print
        htmlBuilder.AppendLine("body { font-family: Arial, sans-serif; font-size: 10pt; line-height: 1.4; margin: 0; padding: 0; }");
        htmlBuilder.AppendLine("</style>");
        htmlBuilder.AppendLine("</head><body>");

        var forms = consent.CareEpisodeConsentForms.OrderBy(f => f.DisplayOrder).ToList();

        for (int i = 0; i < forms.Count; i++)
        {
            var form = forms[i];

            // Decrypt the rendered HTML content
            var htmlContent = _encryption.Decrypt(form.RenderedHtmlContent) ?? "";

            // Embed signatures into the HTML
            if (!string.IsNullOrEmpty(form.SignaturesJson))
            {
                htmlContent = EmbedSignaturesInHtml(htmlContent, form.SignaturesJson);
            }

            // Wrap each form in a page-break container
            htmlBuilder.AppendLine("<div class=\"consent-form-page\">");
            htmlBuilder.AppendLine(htmlContent);
            htmlBuilder.AppendLine("</div>");
        }

        htmlBuilder.AppendLine("</body></html>");

        // Generate PDF from HTML using PuppeteerSharp
        var combinedHtml = htmlBuilder.ToString();
        return await _htmlToPdfService.GeneratePdfFromHtmlAsync(combinedHtml);
    }

    /// <summary>
    /// Embeds signature images into the HTML by replacing signature field divs with img tags.
    /// Only adds the signature image - no labels, timestamps, or other text.
    ///
    /// The signature field structure in rendered HTML is:
    /// &lt;div class="signature-field" data-field-id="fieldId" ...&gt;
    ///     &lt;div class="signature-canvas-container"&gt;...&lt;/div&gt;
    ///     &lt;div class="signature-actions"&gt;...&lt;/div&gt;
    /// &lt;/div&gt;
    /// </summary>
    private string EmbedSignaturesInHtml(string html, string signaturesJson)
    {
        try
        {
            var signatures = JsonSerializer.Deserialize<List<KioskSignatureSubmissionDto>>(signaturesJson);
            if (signatures == null || !signatures.Any())
                return html;

            foreach (var sig in signatures)
            {
                if (string.IsNullOrEmpty(sig.ImageData) || string.IsNullOrEmpty(sig.FieldId))
                    continue;

                // Create the signature image HTML (just the image, nothing else)
                var signatureImgHtml = $"<img src=\"{sig.ImageData}\" alt=\"Signature\" style=\"max-width: 400px; max-height: 100px; border: 1px solid #ccc;\" />";

                // Match the complete signature-field div structure with nested divs
                // Pattern matches: <div class="signature-field" data-field-id="fieldId" ...>...nested content...</div>
                // Need to match all closing </div> tags for the nested structure (3 total: signature-field, signature-canvas-container, signature-actions)
                var pattern = $@"<div\s+class\s*=\s*[""']signature-field[""']\s+data-field-id\s*=\s*[""']{Regex.Escape(sig.FieldId)}[""'][^>]*>\s*<div[^>]*class\s*=\s*[""']signature-canvas-container[""'][^>]*>[\s\S]*?</div>\s*<div[^>]*class\s*=\s*[""']signature-actions[""'][^>]*>[\s\S]*?</div>\s*</div>";

                if (Regex.IsMatch(html, pattern, RegexOptions.IgnoreCase))
                {
                    html = Regex.Replace(html, pattern, signatureImgHtml, RegexOptions.IgnoreCase);
                    _logger.LogDebug("Replaced signature field {FieldId} with image", sig.FieldId);
                }
                else
                {
                    // Fallback: try a simpler pattern that matches any div with signature-field class and the field ID
                    // This handles variations in attribute order
                    var fallbackPattern = $@"<div[^>]*class\s*=\s*[""']signature-field[""'][^>]*data-field-id\s*=\s*[""']{Regex.Escape(sig.FieldId)}[""'][^>]*>[\s\S]*?</div>\s*</div>\s*</div>";
                    if (Regex.IsMatch(html, fallbackPattern, RegexOptions.IgnoreCase))
                    {
                        html = Regex.Replace(html, fallbackPattern, signatureImgHtml, RegexOptions.IgnoreCase);
                        _logger.LogDebug("Replaced signature field {FieldId} with image (fallback pattern)", sig.FieldId);
                    }
                    else
                    {
                        _logger.LogWarning("Could not find signature field {FieldId} in HTML to embed signature", sig.FieldId);
                    }
                }
            }

            return html;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to embed signatures in HTML");
            return html;
        }
    }

    private byte[] EncryptPdf(byte[] pdfBytes)
    {
        using var aes = Aes.Create();
        aes.Key = _fileEncryptionKey;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        using var msEncrypt = new MemoryStream();

        msEncrypt.Write(aes.IV, 0, aes.IV.Length);

        using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
        {
            csEncrypt.Write(pdfBytes, 0, pdfBytes.Length);
        }

        return msEncrypt.ToArray();
    }

    private byte[] DecryptPdf(byte[] encryptedBytes)
    {
        using var aes = Aes.Create();
        aes.Key = _fileEncryptionKey;

        var iv = new byte[16];
        Buffer.BlockCopy(encryptedBytes, 0, iv, 0, 16);
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        using var msDecrypt = new MemoryStream();

        using (var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Write))
        {
            csDecrypt.Write(encryptedBytes, 16, encryptedBytes.Length - 16);
        }

        return msDecrypt.ToArray();
    }

    private static string ComputeHash(byte[] bytes)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hashBytes);
    }

    private string GetLast4Ssn(Patient patient)
    {
        if (string.IsNullOrEmpty(patient.SsnEncrypted))
            return "";

        var ssn = _encryption.Decrypt(patient.SsnEncrypted);
        if (string.IsNullOrEmpty(ssn))
            return "";

        var digits = System.Text.RegularExpressions.Regex.Replace(ssn, @"[^\d]", "");
        return digits.Length >= 4 ? digits[^4..] : "";
    }

    private static string GetAppointmentTypeName(int type)
    {
        // Only 4 appointment types are used, return "Other" for any legacy types
        return type switch
        {
            0 => "New Patient Visit",
            1 => "Follow-Up Visit",
            2 => "Annual Physical",
            3 => "Wellness Exam",
            4 => "Consultation",
            5 => "Telehealth",
            6 => "Procedure Visit",
            7 => "Urgent Visit",
            8 => "Lab Review",
            9 => "Medication Review",
            10 => "New Longevity Patient",
            11 => "Follow-Up Longevity Patient",
            _ => "Other"
        };
    }

    private static string GetCareEpisodeStatusName(int status)
    {
        return status switch
        {
            0 => "Active",
            1 => "Completed",
            2 => "Discharged",
            3 => "On Hold",
            _ => "Unknown"
        };
    }

    #endregion
}
