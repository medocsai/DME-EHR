global using EHR.Models;
global using EHR.Models.Generated;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using System.Threading.RateLimiting;
using EHR.Data;
using EHR.Services;
using EHR.Services.Storage;
using EHR.Services.Storage.Helpers;
using EHR.Middleware;
using EHR.Helpers;
using EHR.Configuration;
using EHR.Hubs;



var builder = WebApplication.CreateBuilder(args);

// Add services to the container
// Configure controllers with views (MVC) and JSON options for API endpoints
// Enable Razor runtime compilation in Development for faster view development
var mvcBuilder = builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

if (builder.Environment.IsDevelopment())
{
    // Enable Razor runtime compilation for development - allows live editing of cshtml files
    try
    {
        mvcBuilder.AddRazorRuntimeCompilation();
    }
    catch
    {
        // If runtime compilation package is not available, ignore and continue.
        // This package is optional and only enhances development experience.
    }
}

// Database context
builder.Services.AddDbContext<EhrDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Tenant provider - scoped per request
builder.Services.AddScoped<TenantProvider>();
builder.Services.AddScoped<ITenantProvider>(sp => sp.GetRequiredService<TenantProvider>());

// Location provider - scoped per request (Multi-Location Support)
builder.Services.AddScoped<LocationProvider>();
builder.Services.AddScoped<ILocationProvider>(sp => sp.GetRequiredService<LocationProvider>());

// Register services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITenantService, TenantService>();
builder.Services.AddScoped<ILocationService, LocationService>();  // Multi-Location Support
builder.Services.AddScoped<IPatientService, PatientService>();
builder.Services.AddScoped<IAppointmentService, AppointmentService>();
builder.Services.AddScoped<IProviderService, ProviderService>();
builder.Services.AddScoped<IProfilePictureService, ProfilePictureService>();
// Office Ally Eligibility API
builder.Services.Configure<EHR.Configuration.OfficeAllyOptions>(
    builder.Configuration.GetSection(EHR.Configuration.OfficeAllyOptions.SectionName));
builder.Services.AddHttpClient<IEligibilityApiService, EligibilityApiService>(client =>
{
    var oaConfig = builder.Configuration.GetSection("OfficeAlly");
    client.BaseAddress = new Uri(oaConfig["EligibilityApiBaseUrl"] ?? "https://edi.officeally.io");
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Authorization service must be registered before Insurance and CareEpisode services (dependency)
builder.Services.AddScoped<IInsuranceAuthorizationService, AuthorizationService>();
builder.Services.AddScoped<IInsuranceService, InsuranceService>();
builder.Services.AddScoped<INoteService, NoteService>();
builder.Services.AddScoped<IBillingService, BillingService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<ICareEpisodeService, CareEpisodeService>();

// Care Episode System Services
builder.Services.AddScoped<ISystemSettingsService, SystemSettingsService>();
builder.Services.AddScoped<IInsuranceValidationService, InsuranceValidationService>();
builder.Services.AddScoped<IEnhancedCareEpisodeService, EnhancedCareEpisodeService>();



// HIPAA Compliance Services
// Encryption helper for PHI at rest (AES-256-GCM)
builder.Services.AddSingleton<EncryptionHelper>();
// Audit logging for all PHI access (HIPAA Security Rule 45 CFR 164.312(b))
builder.Services.AddScoped<IAuditService, AuditService>();

// DME change auditing. Needs the request context to attribute the change to a
// user and an IP, so the accessor is registered alongside it.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IDmeAudit, DmeAudit>();

// DME data access (raw ADO against the DME-native tables in DMEEHR).
// Scoped, not static: it resolves the caller's tenant from ITenantProvider and
// binds every connection to it so the SQL Server row level security policy can
// filter. See Helpers/DmeDb.cs.
builder.Services.AddScoped<EHR.Helpers.IDmeDb, EHR.Helpers.DmeDb>();

// DME customer PHI: encryption at rest plus the blind index that keeps search
// working over ciphertext. Wraps the shared EncryptionHelper so the DME product
// never grows its own cryptography. Singleton because it is stateless and its
// only dependency already is one.
builder.Services.AddSingleton<EHR.Helpers.DmeCustomerPhi>();

// Google Cloud Storage Services
builder.Services.Configure<GoogleCloudStorageOptions>(
    builder.Configuration.GetSection("GoogleCloudStorage"));
builder.Services.AddSingleton<FilePathBuilder>();
builder.Services.AddSingleton<FileValidator>();
builder.Services.AddSingleton<MetadataBuilder>();
builder.Services.AddSingleton<IFileStorageService, GoogleCloudStorageService>();

// Patient Intake (Phase 1) — backend contractors + employees
builder.Services.AddMemoryCache();
builder.Services.AddScoped<EHR.Services.Intake.IProvenanceTagger, EHR.Services.Intake.ProvenanceTagger>();
builder.Services.AddScoped<EHR.Services.Intake.IIntakeSubmissionService, EHR.Services.Intake.IntakeSubmissionService>();
builder.Services.AddScoped<EHR.Services.Intake.IIntakeProgressCalculator, EHR.Services.Intake.IntakeProgressCalculator>();
builder.Services.AddScoped<EHR.Services.Intake.IPatientEnteredDataReader, EHR.Services.Intake.PatientEnteredDataReader>();
builder.Services.AddScoped<EHR.Services.Intake.IIntakePrefillService, EHR.Services.Intake.IntakePrefillService>();
builder.Services.AddScoped<EHR.Services.Intake.ILongevityFeatureGate, EHR.Services.Intake.LongevityFeatureGate>();
builder.Services.AddScoped<EHR.Services.Intake.IIntakeAccessTokenService, EHR.Services.Intake.IntakeAccessTokenService>();
builder.Services.AddScoped<EHR.Services.Intake.IPatientIdentityMatcher, EHR.Services.Intake.PatientIdentityMatcher>();
builder.Services.AddScoped<EHR.Services.Intake.IIntakeAttemptThrottler, EHR.Services.Intake.IntakeAttemptThrottler>();
builder.Services.AddScoped<EHR.Helpers.IIntakeStatusHelper, EHR.Helpers.IntakeStatusHelper>();

// Additional services
builder.Services.AddScoped<IBlindIndexService, BlindIndexService>();
builder.Services.AddScoped<IPatientSearchService, PatientSearchService>();
builder.Services.AddScoped<ITherapistUnavailabilityService, TherapistUnavailabilityService>();
builder.Services.AddScoped<IClinicalNoteTemplateService, ClinicalNoteTemplateService>();
builder.Services.AddScoped<IClinicalNoteService, ClinicalNoteService>();
builder.Services.AddScoped<IClinicalNoteAmendmentService, ClinicalNoteAmendmentService>();
builder.Services.AddScoped<IEncounterSummaryManager, EncounterSummaryManager>();

// Provider Favorite Codes (Dx & CPT step) — per-user starred ICD-10/CPT codes
builder.Services.AddScoped<IFavoritesService, FavoritesService>();

// Dashboard service for alert widgets
builder.Services.AddScoped<IDashboardService, DashboardService>();

// Internal Medicine clinical services
builder.Services.AddScoped<IEncounterService, EncounterService>();
builder.Services.AddScoped<IPatientProblemService, PatientProblemService>();
builder.Services.AddScoped<IPatientAllergyService, PatientAllergyService>();
builder.Services.AddScoped<IPatientMedicationService, PatientMedicationService>();
builder.Services.AddScoped<IPatientVitalService, PatientVitalService>();
builder.Services.AddScoped<IPatientImmunizationService, PatientImmunizationService>();
builder.Services.AddScoped<IPatientFamilyHistoryService, PatientFamilyHistoryService>();
builder.Services.AddScoped<IPatientSocialHistoryService, PatientSocialHistoryService>();
builder.Services.AddScoped<IEncounterContextService, EncounterContextService>();
builder.Services.AddScoped<ITreatmentPlanService, TreatmentPlanService>();
builder.Services.AddScoped<IPatientStickyNoteService, PatientStickyNoteService>();
builder.Services.AddScoped<ICareNoteService, CareNoteService>();
builder.Services.AddScoped<ICareNotesNotifier, CareNotesNotifier>();
builder.Services.AddScoped<IAudioTranscriptionService, AudioTranscriptionService>();
builder.Services.AddScoped<IHistoryReviewActionService, HistoryReviewActionService>();

// E-Prescribing services
builder.Services.AddScoped<IPrescriptionService, PrescriptionService>();
builder.Services.AddScoped<IPharmacyService, PharmacyService>();
builder.Services.AddScoped<IDrugDatabaseService, DrugDatabaseService>();

// Orders Module (Labs, Imaging, Referrals)
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<ILabTestCatalogService, LabTestCatalogService>();

// User management and email services
builder.Services.AddScoped<IUserManagementService, UserManagementService>();
builder.Services.AddScoped<IEmailService, EmailService>();

// SMS and Notification services
builder.Services.AddScoped<ISmsService, SmsService>();
builder.Services.AddScoped<INotificationService, NotificationService>();

// Patient document service (HIPAA-compliant file uploads)
builder.Services.AddScoped<IPatientDocumentService, PatientDocumentService>();

// Patient profile validation service
builder.Services.AddScoped<IPatientValidationService, PatientValidationService>();

// Telehealth services
builder.Services.AddScoped<ITelehealthService, TelehealthService>();
builder.Services.AddSingleton<ITelehealthNotificationService, TelehealthNotificationService>();

// Patient Consent System services
builder.Services.AddScoped<IKioskService, KioskService>();
builder.Services.AddScoped<IConsentTemplateService, ConsentTemplateService>();
builder.Services.AddScoped<IConsentService, ConsentService>();
builder.Services.AddSingleton<IHtmlToPdfService, HtmlToPdfService>();

// Medical Lien Form services
builder.Services.AddScoped<IMedicalLienService, MedicalLienService>();
builder.Services.AddScoped<IMedicalLienTemplateService, MedicalLienTemplateService>();

// Patient Portal services
builder.Services.AddScoped<IPatientPortalAuthService, PatientPortalAuthService>();
builder.Services.AddScoped<IPatientPortalInvitationService, PatientPortalInvitationService>();

// SignalR for real-time notifications
builder.Services.AddSignalR();
builder.Services.AddScoped<IConsentNotificationService, ConsentNotificationService>();
builder.Services.AddScoped<IRecordingProgressService, RecordingProgressService>();
builder.Services.AddScoped<IMobileNotificationService, MobileNotificationService>();
builder.Services.AddScoped<IScheduleNotificationService, ScheduleNotificationService>();

// Internal Messaging System services
builder.Services.AddScoped<IMessagingService, MessagingService>();
builder.Services.AddScoped<IMessagingNotificationService, MessagingNotificationService>();

// Patient-Provider Messaging System services
builder.Services.AddScoped<IPatientMessagingService, PatientMessagingService>();
builder.Services.AddScoped<IPatientMessagingNotificationService, PatientMessagingNotificationService>();

// Recording Session service
builder.Services.AddScoped<IRecordingSessionService, RecordingSessionService>();

// Transcription service for audio chunk processing
builder.Services.AddScoped<ITranscriptionService, TranscriptionService>();

// Centralized Gemini API service - all Gemini calls go through this
builder.Services.AddSingleton<IGeminiService, GeminiService>();
builder.Services.AddSingleton<INoteGenerationJobService, NoteGenerationJobService>();

// MEDOCS AI Help Assistant services
builder.Services.AddSingleton<IUserGuideProvider, UserGuideProvider>();
builder.Services.AddScoped<IIMEHRHelpService, IMEHRHelpService>();

// MEDOCS AI Voice Entry service for Encounter Workspace
builder.Services.AddScoped<IMedocsVoiceProcessingService, MedocsVoiceProcessingService>();

// Care episode extraction service for Initial Evaluation notes
builder.Services.AddScoped<ICareEpisodeExtractionService, CareEpisodeExtractionService>();

// Copay Integration - Stripe + Installment + Reminder services
builder.Services.AddScoped<IStripeService, StripeService>();
builder.Services.AddScoped<IStripeConnectService, StripeConnectService>();
builder.Services.AddScoped<IPlatformFeeCalculator, PlatformFeeCalculator>();
builder.Services.AddScoped<IInstallmentService, InstallmentService>();
builder.Services.AddScoped<ICopayReminderService, CopayReminderService>();
builder.Services.AddHostedService<InstallmentProcessorBackgroundService>();

// Appointment Reminder Background Service
builder.Services.AddHostedService<AppointmentReminderBackgroundService>();

// Audit log retention sweep (HIPAA §164.316(b)(2)) — deletes AuditLog rows
// older than HIPAA:AuditRetentionDays. Only this service can delete; the
// AuditLogs table has an INSTEAD OF DELETE trigger gated by session context.
builder.Services.AddHostedService<AuditLogRetentionBackgroundService>();

// One-time backfill: ensures every existing patient has a full-length
// "EmailExact" search token so the duplicate-email check matches emails
// longer than 15 characters (the prefix-token cap). Safe to leave deployed.
builder.Services.AddHostedService<EmailExactBackfillService>();

// Refuse to start with missing or publicly-known secrets. Runs before anything
// reads a key, so a misconfigured deployment fails loudly instead of quietly
// signing tokens with a value that is printed in the source. See SecretsGuard.
using (var secretsLoggerFactory = LoggerFactory.Create(b => b.AddConsole()))
{
    EHR.Configuration.SecretsGuard.Validate(
        builder.Configuration,
        builder.Environment,
        secretsLoggerFactory.CreateLogger("SecretsGuard"));
}

// JWT Authentication
// Two schemes, one set of rules (see Configuration/JwtBearerSetup.cs):
//   Bearer        - the SPA, token in the Authorization header
//   SessionCookie - server-rendered Razor pages, token in an HttpOnly cookie
// A browser navigation sends no Authorization header, so without the cookie
// scheme a server-rendered page can never identify the caller.
builder.Services.AddAuthentication(options =>
{
    // A policy scheme picks the real scheme per request, so HttpContext.User is
    // populated the same way everywhere and plain [Authorize] works on both API
    // controllers and server-rendered pages. Without this the default scheme is
    // Bearer, and a page navigation (no Authorization header) always looks
    // anonymous even when a valid session cookie is present.
    options.DefaultScheme = EHR.Helpers.SessionCookie.PolicyScheme;
    options.DefaultChallengeScheme = EHR.Helpers.SessionCookie.PolicyScheme;
})
.AddPolicyScheme(EHR.Helpers.SessionCookie.PolicyScheme, "Bearer header, else session cookie", options =>
{
    options.ForwardDefaultSelector = ctx =>
    {
        // An explicit Authorization header always wins: API and fetch callers
        // keep behaving exactly as before this scheme existed.
        string? header = ctx.Request.Headers.Authorization;
        if (!string.IsNullOrEmpty(header) && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return JwtBearerDefaults.AuthenticationScheme;

        // Otherwise fall back to the cookie when one is present. Falling back to
        // Bearer when it is not keeps 401 responses shaped the way API clients
        // already expect.
        return EHR.Helpers.SessionCookie.Read(ctx.Request) != null
            ? EHR.Helpers.SessionCookie.Scheme
            : JwtBearerDefaults.AuthenticationScheme;
    };
})
.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    EHR.Configuration.JwtBearerSetup.Configure(options, builder.Configuration, tokenFromCookie: false))
.AddJwtBearer(EHR.Helpers.SessionCookie.Scheme, options =>
    EHR.Configuration.JwtBearerSetup.Configure(options, builder.Configuration, tokenFromCookie: true));

builder.Services.AddAuthorization();

// Per-IP rate limiting on authentication endpoints. Without this, login /
// OTP verify / resend / forgot-password are unbounded — combined with the
// account-lockout we already enforce per user, the only thing left was
// distributed brute force from many IPs. The limiter caps each remote IP
// regardless of which user it targets. Authenticated traffic and other
// endpoints are not limited.
builder.Services.AddRateLimiter(options =>
{
    // Common partition key: caller IP. Falls back to "unknown" if not visible
    // (test server, malformed proxy chain) so requests are not allowed to
    // bypass the limiter by hiding their IP.
    static string GetIpKey(HttpContext ctx) =>
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    // Login + verify-otp: 5 attempts / minute / IP. Account lockout already
    // enforces 5 wrong OTPs per user; this is the distributed-attack cap.
    options.AddPolicy("auth-login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: GetIpKey(ctx),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));

    // Resend-OTP: 3 / minute / IP. Resend triggers an outbound email; spamming
    // it is a DoS / mailbox-flood vector even though the existing 60s cooldown
    // exists per user.
    options.AddPolicy("auth-resend", ctx => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: GetIpKey(ctx),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 3,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));

    // Forgot-password: 3 / 5min / IP. Reset emails reveal account existence
    // patterns (timing) and consume mailbox quota; the per-IP cap blunts
    // both abuse cases.
    options.AddPolicy("auth-forgot", ctx => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: GetIpKey(ctx),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 3,
            Window = TimeSpan.FromMinutes(5),
            QueueLimit = 0,
            AutoReplenishment = true
        }));

    // Default rejection response — clients see 429 Too Many Requests, NOT a
    // detailed message that would help an attacker tune their loop.
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (ctx, _) =>
    {
        ctx.HttpContext.Response.Headers["Retry-After"] = "60";
        return ValueTask.CompletedTask;
    };
});

// CORS — explicit origin allowlist (required when AllowCredentials is on; SignalR
// also rejects wildcard origins with credentials). Origins are read from
// appsettings.json "Cors:AllowedOrigins". Wildcard fallback removed.
var allowedCorsOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("CorsPolicy", policy =>
    {
        policy.WithOrigins(allowedCorsOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "MEDOCS EHR API",
        Version = "v1",
        Description = "Multi-tenant SaaS EHR system by MEDOCS LLC"
    });
    
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT Authorization header using the Bearer scheme."
    });
    
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// HTTPS + HSTS in non-development environments. Production load balancer
// (or IIS) terminates TLS but we still emit redirect + HSTS so browsers cache it.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

// Catch anything no controller caught. First in the pipeline because it can
// only protect what runs after it.
app.UseMiddleware<EHR.Helpers.UnhandledExceptionMiddleware>();

// Security headers on every response — set before the rest of the pipeline so
// even errors / 404s carry them. CSP starts in Report-Only mode so violations
// surface in browser console without breaking working features; switch the
// header name to "Content-Security-Policy" once the policy is verified clean.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "SAMEORIGIN";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    // 2026-05: extended camera+microphone allow-list to include the Jitsi
    // origins so the embedded telehealth iframe can request the camera/mic.
    // Previously `camera=(self)` blocked the cross-origin iframe at the parent
    // policy level — patient saw "Failed to access your camera" even with
    // OS + browser permissions granted, on every browser (server-side block).
    // The CSP on the next line already trusts these same origins for scripts.
    headers["Permissions-Policy"] = "geolocation=(), camera=(self \"https://8x8.vc\" \"https://meet.jit.si\"), microphone=(self \"https://8x8.vc\" \"https://meet.jit.si\"), payment=(self), interest-cohort=()";
    headers["X-Permitted-Cross-Domain-Policies"] = "none";

    var csp = string.Join("; ", new[]
    {
        "default-src 'self'",
        "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://cdn.jsdelivr.net https://code.jquery.com https://cdnjs.cloudflare.com https://js.stripe.com https://8x8.vc https://*.8x8.vc",
        "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://cdnjs.cloudflare.com",
        "font-src 'self' data: https://cdn.jsdelivr.net https://cdnjs.cloudflare.com",
        "img-src 'self' data: blob: https:",
        "media-src 'self' blob:",
        "connect-src 'self' https: wss:",
        "frame-src 'self' https://js.stripe.com https://8x8.vc https://*.8x8.vc",
        "frame-ancestors 'self'",
        "base-uri 'self'",
        "form-action 'self'",
        "object-src 'none'"
    });
    headers["Content-Security-Policy-Report-Only"] = csp;

    await next();
});

// Configure the HTTP request pipeline
// Swagger UI exposes the full API surface — only enable in Development.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "PT EHR API v1");
    });
}

// Turn a 401 on a browser page navigation into a redirect to the sign-in page.
// Without this, a logged-out user clicking a bookmarked /Dme/... link gets a
// blank 401 body instead of the login card. Deliberately narrow: API callers
// and fetch/XHR still receive a real 401 with the response shape they expect,
// so nothing that already works changes behaviour.
app.UseStatusCodePages(context =>
{
    var http = context.HttpContext;
    var isPageNavigation =
        http.Response.StatusCode == StatusCodes.Status401Unauthorized
        && HttpMethods.IsGet(http.Request.Method)
        && !http.Request.Path.StartsWithSegments("/api")
        && http.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);

    if (isPageNavigation)
    {
        var returnUrl = http.Request.Path + http.Request.QueryString;
        http.Response.Redirect("/?returnUrl=" + Uri.EscapeDataString(returnUrl));
    }

    return Task.CompletedTask;
});

app.UseCors("CorsPolicy");
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseTenantResolution();
app.UseAuthorization();
app.UseRateLimiter();

// Map API controllers (attribute routing)
app.MapControllers();

// Map MVC routes for view-based controllers
// Clean URL routes - map /Page directly to HomeController actions for professional URLs
app.MapControllerRoute(
    name: "patients",
    pattern: "Patients",
    defaults: new { controller = "Home", action = "Patients" });

app.MapControllerRoute(
    name: "schedule",
    pattern: "Schedule",
    defaults: new { controller = "Home", action = "Schedule" });

app.MapControllerRoute(
    name: "providers",
    pattern: "Providers",
    defaults: new { controller = "Home", action = "Providers" });

app.MapControllerRoute(
    name: "clinical-notes",
    pattern: "ClinicalNotes",
    defaults: new { controller = "Home", action = "Notes" });

app.MapControllerRoute(
    name: "prescriptions",
    pattern: "Prescriptions",
    defaults: new { controller = "Home", action = "Prescriptions" });

app.MapControllerRoute(
    name: "orders",
    pattern: "Orders",
    defaults: new { controller = "Home", action = "Orders" });

app.MapControllerRoute(
    name: "encounter-workspace",
    pattern: "Encounter/{id?}",
    defaults: new { controller = "Home", action = "Encounter" });

app.MapControllerRoute(
    name: "billing",
    pattern: "Billing",
    defaults: new { controller = "Home", action = "Billing" });

app.MapControllerRoute(
    name: "time-off",
    pattern: "TimeOff",
    defaults: new { controller = "Home", action = "Unavailability" });

app.MapControllerRoute(
    name: "reports",
    pattern: "Reports",
    defaults: new { controller = "Home", action = "Reports" });

app.MapControllerRoute(
    name: "user-management",
    pattern: "UserManagement",
    defaults: new { controller = "Home", action = "Users" });

app.MapControllerRoute(
    name: "locations",
    pattern: "Locations",
    defaults: new { controller = "Home", action = "Locations" });

app.MapControllerRoute(
    name: "settings",
    pattern: "Settings",
    defaults: new { controller = "Home", action = "Settings" });

app.MapControllerRoute(
    name: "templates",
    pattern: "Templates",
    defaults: new { controller = "Home", action = "Templates" });

app.MapControllerRoute(
    name: "clinics",
    pattern: "Clinics",
    defaults: new { controller = "Home", action = "Tenants" });

app.MapControllerRoute(
    name: "consent-forms",
    pattern: "ConsentForms",
    defaults: new { controller = "Home", action = "ConsentForms" });

app.MapControllerRoute(
    name: "medical-lien-templates",
    pattern: "MedicalLienTemplates",
    defaults: new { controller = "Home", action = "MedicalLienTemplates" });

app.MapControllerRoute(
    name: "organization",
    pattern: "Organization",
    defaults: new { controller = "Home", action = "Organization" });

app.MapControllerRoute(
    name: "documentation",
    pattern: "Documentation",
    defaults: new { controller = "Home", action = "Documentation" });

// Patient Portal routes (standalone pages, separate from main EHR)
app.MapControllerRoute(
    name: "portal-login",
    pattern: "Portal",
    defaults: new { controller = "PatientPortal", action = "Login" });

app.MapControllerRoute(
    name: "portal-pages",
    pattern: "Portal/{action}",
    defaults: new { controller = "PatientPortal" });

// Default MVC route: /{controller=Home}/{action=Index}/{id?}
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Map SignalR hubs
app.MapHub<ConsentNotificationHub>("/hubs/consent");
app.MapHub<RecordingProgressHub>("/hubs/recording-progress");
app.MapHub<MobileHub>("/hubs/mobile");
app.MapHub<ScheduleNotificationHub>("/hubs/schedule");
app.MapHub<MessagingHub>("/hubs/messaging");
app.MapHub<TelehealthHub>("/hubs/telehealth");
app.MapHub<PatientMessagingHub>("/hubs/patient-messaging");
app.MapHub<CareNotesHub>("/hubs/care-notes");

// Initialize HIPAA-compliant encryption configuration
// IMPORTANT: Call this BEFORE any database operations to ensure PHI is encrypted
EncryptionConfiguration.Initialize();

// Seed reference data on startup (Pharmacies, Drugs, Payers) — dev only.
// Production data is managed via SQL migrations under Migrations/Manual/.
if (app.Environment.IsDevelopment())
{
    using (var scope = app.Services.CreateScope())
    {
        var context = scope.ServiceProvider.GetRequiredService<EhrDbContext>();
        DbSeeder.Seed(context);
    }
}

// Note: No fallback to index.html - MVC views handle all UI rendering via HomeController

app.Run();
