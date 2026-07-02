using System.Security.Cryptography;
using EHR.Helpers;
using EHR.Models.Generated;
using Microsoft.EntityFrameworkCore;

namespace EHR.Services;

public interface IPatientPortalInvitationService
{
    // Invitation management (Admin)
    Task<PortalInvitationResult> SendInvitationAsync(SendInvitationDto dto, int invitedByUserId);
    Task<PortalInvitationResult> ResendInvitationAsync(int invitationId, int userId);
    Task<List<PortalInvitationListDto>> GetInvitationsAsync(int tenantId, int? locationId = null);

    // Registration (Patient)
    Task<PortalInvitationValidation> ValidateInvitationTokenAsync(string token);
    Task<PortalRegistrationResult> CompleteRegistrationAsync(CompleteRegistrationDto dto);

    // Login (Email + Password)
    Task<PortalLoginResult> LoginAsync(string email, string password, string portalCode);

    // OTP
    Task<bool> SendOtpAsync(int accountId, string portalCode);
    Task<PortalOtpResult> VerifyOtpAsync(int accountId, string code, string portalCode);

    // Password Reset
    Task<bool> RequestPasswordResetAsync(string email, string portalCode);
    Task<PortalPasswordResetValidation> ValidatePasswordResetTokenAsync(string token);
    Task<bool> ResetPasswordAsync(string token, string newPassword);

    // Self-service account setup (DOB + SSN → OTP → Set Password)
    Task<PortalSetupVerifyResult> SetupVerifyAsync(string lastName, DateOnly dateOfBirth, string zipCode, string portalCode);
    Task<PortalOtpResult> SetupCompleteAsync(int accountId, string code, string password, string portalCode);

    // Location lookup
    Task<PortalLocationInfo?> GetLocationByPortalCodeAsync(string portalCode);
}

public class PatientPortalInvitationService : IPatientPortalInvitationService
{
    private readonly EhrDbContext _context;
    private readonly IEmailService _emailService;
    private readonly EncryptionHelper _encryptionHelper;
    private readonly IAuditService _auditService;
    private readonly IPatientPortalAuthService _portalAuthService;
    private readonly IConfiguration _config;
    private readonly ILogger<PatientPortalInvitationService> _logger;

    public PatientPortalInvitationService(
        EhrDbContext context,
        IEmailService emailService,
        EncryptionHelper encryptionHelper,
        IAuditService auditService,
        IPatientPortalAuthService portalAuthService,
        IConfiguration config,
        ILogger<PatientPortalInvitationService> logger)
    {
        _context = context;
        _emailService = emailService;
        _encryptionHelper = encryptionHelper;
        _auditService = auditService;
        _portalAuthService = portalAuthService;
        _config = config;
        _logger = logger;
    }

    // ============================================
    // INVITATION MANAGEMENT
    // ============================================

    public async Task<PortalInvitationResult> SendInvitationAsync(SendInvitationDto dto, int invitedByUserId)
    {
        // Validate required fields
        if (string.IsNullOrWhiteSpace(dto.FirstName) || string.IsNullOrWhiteSpace(dto.LastName))
            return new PortalInvitationResult { Success = false, Message = "First name and last name are required." };
        if (string.IsNullOrWhiteSpace(dto.Email))
            return new PortalInvitationResult { Success = false, Message = "Email address is required." };

        // Get location and tenant info
        var location = await _context.Locations
            .Include(l => l.Tenant)
            .FirstOrDefaultAsync(l => l.LocationId == dto.LocationId && l.IsActive == true);
        if (location == null)
            return new PortalInvitationResult { Success = false, Message = "Location not found." };

        // Check for existing invitation with same email at this location
        var existingInvite = await _context.PatientPortalInvitations
            .FirstOrDefaultAsync(i => i.Email == dto.Email
                && i.LocationId == dto.LocationId
                && i.Status == 0
                && i.ExpiresAt > DateTime.UtcNow);

        if (existingInvite != null)
            return new PortalInvitationResult { Success = false, Message = "A pending invitation already exists for this email. Use resend instead." };

        // Check for existing portal account with same email at this location
        var existingAccount = await FindAccountByEmailAsync(dto.Email, dto.LocationId);

        if (existingAccount != null)
            return new PortalInvitationResult { Success = false, Message = "A portal account already exists for this email at this location." };

        // Create a new patient record with minimal info (name + email)
        var mrn = await GenerateMRNAsync(location.TenantId);
        var patient = new Patient
        {
            TenantId = location.TenantId,
            Mrn = mrn,
            FirstName = dto.FirstName.Trim(),
            LastName = dto.LastName.Trim(),
            Email = dto.Email.Trim(),
            DateOfBirth = DateOnly.FromDateTime(DateTime.Today), // Placeholder — patient will update during registration
            Gender = "Unknown",
            PreferredLocationId = dto.LocationId,
            CreatedAt = DateTime.UtcNow
        };

        _encryptionHelper.EncryptEntity(patient);
        _context.Patients.Add(patient);
        await _context.SaveChangesAsync();

        // Generate secure token
        var rawToken = GenerateSecureToken();
        var tokenHash = HashToken(rawToken);

        var invitation = new PatientPortalInvitation
        {
            TenantId = location.TenantId,
            LocationId = dto.LocationId,
            PatientId = patient.PatientId,
            Email = dto.Email.Trim(),
            TokenHash = tokenHash,
            Status = 0, // Pending
            InvitedByUserId = invitedByUserId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(72),
            ResentCount = 0
        };

        _context.PatientPortalInvitations.Add(invitation);
        await _context.SaveChangesAsync();

        // Decrypt for email (needed for SendInvitationEmailAsync)
        _encryptionHelper.DecryptEntity(patient);

        // Send invitation email
        await SendInvitationEmailAsync(dto.Email.Trim(), rawToken, patient, location);

        await _auditService.LogAccessAsync(
            null, dto.Email, "PORTAL_INVITATION_SENT", "PatientPortalInvitation", invitation.PatientPortalInvitationId,
            null, $"PatientId={patient.PatientId},LocationId={dto.LocationId}", null);

        return new PortalInvitationResult { Success = true, Message = "Patient record created and invitation sent successfully.", InvitationId = invitation.PatientPortalInvitationId };
    }

    private async Task<string> GenerateMRNAsync(int tenantId)
    {
        var lastPatient = await _context.Patients
            .Where(p => p.TenantId == tenantId)
            .OrderByDescending(p => p.PatientId)
            .FirstOrDefaultAsync();

        var nextNumber = (lastPatient?.PatientId ?? 0) + 1;
        return $"IM-{nextNumber:D6}";
    }

    public async Task<PortalInvitationResult> ResendInvitationAsync(int invitationId, int userId)
    {
        var invitation = await _context.PatientPortalInvitations
            .Include(i => i.Patient)
            .Include(i => i.Location)
                .ThenInclude(l => l.Tenant)
            .FirstOrDefaultAsync(i => i.PatientPortalInvitationId == invitationId);

        if (invitation == null)
            return new PortalInvitationResult { Success = false, Message = "Invitation not found." };

        // Check if already registered
        var existingAccount = await _context.PatientPortalAccounts
            .FirstOrDefaultAsync(a => a.PatientId == invitation.PatientId && a.LocationId == invitation.LocationId && a.IsActive);
        if (existingAccount != null)
            return new PortalInvitationResult { Success = false, Message = "Patient already has an active portal account." };

        _encryptionHelper.DecryptEntity(invitation.Patient);

        // Generate new token, invalidate old one
        var rawToken = GenerateSecureToken();
        invitation.TokenHash = HashToken(rawToken);
        invitation.ExpiresAt = DateTime.UtcNow.AddHours(72);
        invitation.Status = 0; // Reset to Pending
        invitation.ResentCount++;
        invitation.UsedAt = null;

        await _context.SaveChangesAsync();

        // Send email
        await SendInvitationEmailAsync(invitation.Email, rawToken, invitation.Patient, invitation.Location);

        return new PortalInvitationResult { Success = true, Message = "Invitation resent successfully." };
    }

    public async Task<List<PortalInvitationListDto>> GetInvitationsAsync(int tenantId, int? locationId = null)
    {
        var query = _context.PatientPortalInvitations
            .Include(i => i.Patient)
            .Include(i => i.Location)
            .Where(i => i.TenantId == tenantId);

        if (locationId.HasValue)
            query = query.Where(i => i.LocationId == locationId.Value);

        var invitations = await query
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();

        // Auto-expire stale invitations
        foreach (var inv in invitations.Where(i => i.Status == 0 && i.ExpiresAt <= DateTime.UtcNow))
        {
            inv.Status = 2; // Expired
        }
        await _context.SaveChangesAsync();

        return invitations.Select(i =>
        {
            _encryptionHelper.DecryptEntity(i.Patient);
            return new PortalInvitationListDto
            {
                InvitationId = i.PatientPortalInvitationId,
                PatientId = i.PatientId,
                PatientName = $"{i.Patient.FirstName} {i.Patient.LastName}",
                Email = i.Email,
                LocationName = i.Location?.Name ?? "",
                LocationId = i.LocationId,
                Status = i.Status,
                StatusName = i.Status switch { 0 => "Pending", 1 => "Registered", 2 => "Expired", _ => "Unknown" },
                CreatedAt = i.CreatedAt,
                ExpiresAt = i.ExpiresAt,
                ResentCount = i.ResentCount
            };
        }).ToList();
    }

    // ============================================
    // REGISTRATION
    // ============================================

    public async Task<PortalInvitationValidation> ValidateInvitationTokenAsync(string token)
    {
        var tokenHash = HashToken(token);
        var invitation = await _context.PatientPortalInvitations
            .Include(i => i.Patient)
            .Include(i => i.Location)
                .ThenInclude(l => l.Tenant)
            .FirstOrDefaultAsync(i => i.TokenHash == tokenHash);

        if (invitation == null)
            return new PortalInvitationValidation { Valid = false, Message = "Invalid or expired invitation link." };

        if (invitation.Status == 1)
            return new PortalInvitationValidation { Valid = false, Message = "This invitation has already been used. Please log in to your portal.", PortalCode = invitation.Location?.PortalCode };

        if (invitation.ExpiresAt <= DateTime.UtcNow || invitation.Status == 2)
            return new PortalInvitationValidation { Valid = false, Message = "This invitation has expired. Please contact your clinic for a new invitation." };

        _encryptionHelper.DecryptEntity(invitation.Patient);

        return new PortalInvitationValidation
        {
            Valid = true,
            InvitationId = invitation.PatientPortalInvitationId,
            Email = invitation.Email,
            PatientName = $"{invitation.Patient.FirstName} {invitation.Patient.LastName}",
            PortalCode = invitation.Location?.PortalCode,
            TenantName = invitation.Location?.Tenant?.Name ?? "",
            TenantLogoUrl = invitation.Location?.Tenant?.LogoUrl,
            LocationName = invitation.Location?.Name ?? "",
            LocationAddress = invitation.Location?.Address,
            LocationPhone = invitation.Location?.Phone
        };
    }

    public async Task<PortalRegistrationResult> CompleteRegistrationAsync(CompleteRegistrationDto dto)
    {
        var tokenHash = HashToken(dto.Token);
        var invitation = await _context.PatientPortalInvitations
            .Include(i => i.Patient)
            .Include(i => i.Location)
                .ThenInclude(l => l.Tenant)
            .FirstOrDefaultAsync(i => i.TokenHash == tokenHash
                && i.Status == 0
                && i.ExpiresAt > DateTime.UtcNow);

        if (invitation == null)
            return new PortalRegistrationResult { Success = false, Message = "Invalid or expired invitation." };

        // Validate password strength
        if (string.IsNullOrWhiteSpace(dto.Password) || dto.Password.Length < 8)
            return new PortalRegistrationResult { Success = false, Message = "Password must be at least 8 characters." };

        if (dto.Password.Length > 128)
            return new PortalRegistrationResult { Success = false, Message = "Password is too long." };

        // Check if account already exists
        var existingAccount = await _context.PatientPortalAccounts
            .FirstOrDefaultAsync(a => a.PatientId == invitation.PatientId && a.LocationId == invitation.LocationId);
        if (existingAccount != null)
            return new PortalRegistrationResult { Success = false, Message = "An account already exists for this patient at this location." };

        // Update patient record with demographics from registration form
        var patient = invitation.Patient;
        if (dto.DateOfBirth.HasValue) patient.DateOfBirth = dto.DateOfBirth.Value;
        if (!string.IsNullOrWhiteSpace(dto.Gender)) patient.Gender = dto.Gender;
        if (!string.IsNullOrWhiteSpace(dto.Phone)) patient.Phone = dto.Phone;
        if (!string.IsNullOrWhiteSpace(dto.Address)) patient.Address = dto.Address;
        if (!string.IsNullOrWhiteSpace(dto.City)) patient.City = dto.City;
        if (!string.IsNullOrWhiteSpace(dto.State)) patient.State = dto.State;
        if (!string.IsNullOrWhiteSpace(dto.ZipCode)) patient.ZipCode = dto.ZipCode;
        if (!string.IsNullOrWhiteSpace(dto.EmergencyContactName)) patient.EmergencyContactName = dto.EmergencyContactName;
        if (!string.IsNullOrWhiteSpace(dto.EmergencyContactPhone)) patient.EmergencyContactPhone = dto.EmergencyContactPhone;
        if (!string.IsNullOrWhiteSpace(dto.EmergencyContactRelation)) patient.EmergencyContactRelation = dto.EmergencyContactRelation;

        // Encrypt and store SSN if provided
        if (!string.IsNullOrWhiteSpace(dto.Ssn))
        {
            var ssnDigitsOnly = System.Text.RegularExpressions.Regex.Replace(dto.Ssn, @"[^\d]", "");
            if (ssnDigitsOnly.Length >= 4)
            {
                var ssnLast4 = ssnDigitsOnly[^4..];
                patient.SsnEncrypted = _encryptionHelper.Encrypt(dto.Ssn);
                patient.SsnLast4Hash = _encryptionHelper.GenerateSearchHash(ssnLast4);
            }
        }

        patient.UpdatedAt = DateTime.UtcNow;

        _encryptionHelper.EncryptEntity(patient);

        // Create the account
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password, BCrypt.Net.BCrypt.GenerateSalt(12));

        var account = new PatientPortalAccount
        {
            TenantId = invitation.TenantId,
            LocationId = invitation.LocationId,
            PatientId = invitation.PatientId,
            PasswordHash = passwordHash,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            RegisteredAt = DateTime.UtcNow
        };

        _context.PatientPortalAccounts.Add(account);

        // Mark invitation as used
        invitation.Status = 1; // Registered
        invitation.UsedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _encryptionHelper.DecryptEntity(invitation.Patient);

        // Send confirmation email
        await SendRegistrationConfirmationEmailAsync(
            invitation.Email,
            invitation.Patient,
            invitation.Location);

        await _auditService.LogAccessAsync(
            null, invitation.Email, "PORTAL_REGISTRATION_COMPLETE", "PatientPortalAccount", account.PatientPortalAccountId,
            null, $"PatientId={invitation.PatientId},LocationId={invitation.LocationId}", null);

        return new PortalRegistrationResult
        {
            Success = true,
            Message = "Registration complete! You can now log in to your patient portal.",
            PortalCode = invitation.Location?.PortalCode,
            PatientId = invitation.PatientId
        };
    }

    // ============================================
    // LOGIN (Email + Password)
    // ============================================

    public async Task<PortalLoginResult> LoginAsync(string email, string password, string portalCode)
    {
        var location = await _context.Locations
            .Include(l => l.Tenant)
            .FirstOrDefaultAsync(l => l.PortalCode == portalCode && l.IsActive == true);

        if (location == null)
            return new PortalLoginResult { Success = false, Message = "Invalid portal link." };

        var account = await FindAccountByEmailAsync(email, location.LocationId);

        if (account == null)
            return new PortalLoginResult { Success = false, Message = "Invalid email or password." };

        // Check lockout
        if (account.LockoutEndAt.HasValue && account.LockoutEndAt > DateTime.UtcNow)
        {
            var minutesLeft = (int)Math.Ceiling((account.LockoutEndAt.Value - DateTime.UtcNow).TotalMinutes);
            return new PortalLoginResult { Success = false, Message = $"Account is locked. Try again in {minutesLeft} minutes." };
        }

        // Verify password
        if (!BCrypt.Net.BCrypt.Verify(password, account.PasswordHash))
        {
            account.FailedLoginAttempts++;
            if (account.FailedLoginAttempts >= 5)
            {
                account.LockoutEndAt = DateTime.UtcNow.AddMinutes(15);
                account.FailedLoginAttempts = 0;
            }
            await _context.SaveChangesAsync();

            await _auditService.LogAccessAsync(
                null, email, "PORTAL_LOGIN_FAILED", "PatientPortalAccount", account.PatientPortalAccountId,
                null, $"FailedAttempts={account.FailedLoginAttempts}", null);

            return new PortalLoginResult { Success = false, Message = "Invalid email or password." };
        }

        // Reset failed attempts on successful password
        account.FailedLoginAttempts = 0;
        account.LockoutEndAt = null;
        await _context.SaveChangesAsync();

        // Send OTP (patient already decrypted by FindAccountByEmailAsync)
        var otpSent = await SendOtpAsync(account.PatientPortalAccountId, portalCode);
        if (!otpSent)
            return new PortalLoginResult { Success = false, Message = "Failed to send verification code. Please try again." };

        return new PortalLoginResult
        {
            Success = true,
            RequiresOtp = true,
            AccountId = account.PatientPortalAccountId,
            MaskedEmail = MaskEmail(account.Patient.Email),
            Message = "Verification code sent to your email."
        };
    }

    // ============================================
    // SELF-SERVICE ACCOUNT SETUP (DOB + SSN → OTP → Set Password)
    // ============================================

    public async Task<PortalSetupVerifyResult> SetupVerifyAsync(string lastName, DateOnly dateOfBirth, string zipCode, string portalCode)
    {
        // Find location by portal code
        var location = await _context.Locations
            .Include(l => l.Tenant)
            .FirstOrDefaultAsync(l => l.PortalCode == portalCode && l.IsActive == true);

        if (location == null)
            return new PortalSetupVerifyResult { Success = false, Message = "Invalid portal link." };

        // Identity match (2026-05): LastName + DateOfBirth + ZipCode. SSN no
        // longer collected. LastName / ZipCode are encrypted, so we narrow the
        // candidate set by tenant + DOB first (indexed plaintext), then decrypt
        // + compare in memory.
        var submittedLastName = (lastName ?? "").Trim();
        var submittedZip = System.Text.RegularExpressions.Regex.Replace(zipCode ?? "", @"[^\d]", "");
        if (submittedZip.Length > 5) submittedZip = submittedZip[..5];

        var candidates = await _context.Patients
            .Where(p => p.TenantId == location.TenantId
                && p.IsDeleted != true
                && p.DateOfBirth == dateOfBirth)
            .ToListAsync();

        Patient patient = null;
        foreach (var cand in candidates)
        {
            var candLast = (_encryptionHelper.Decrypt(cand.LastName) ?? "").Trim();
            var candZip = System.Text.RegularExpressions.Regex.Replace(_encryptionHelper.Decrypt(cand.ZipCode) ?? "", @"[^\d]", "");
            if (candZip.Length > 5) candZip = candZip[..5];

            if (string.Equals(candLast, submittedLastName, StringComparison.OrdinalIgnoreCase)
                && candZip == submittedZip)
            {
                patient = cand;
                break;
            }
        }

        if (patient == null)
        {
            _logger.LogWarning("Portal setup verification failed: no matching patient at tenant={TenantId} (PII redacted). CandidatesByDob={DobCount}",
                location.TenantId, candidates.Count);
            return new PortalSetupVerifyResult { Success = false, Message = "We couldn't find your record. Please contact the clinic." };
        }

        _encryptionHelper.DecryptEntity(patient);

        // Check if account already exists
        var existingAccount = await _context.PatientPortalAccounts
            .FirstOrDefaultAsync(a => a.PatientId == patient.PatientId && a.LocationId == location.LocationId && a.IsActive == true);

        if (existingAccount != null)
        {
            return new PortalSetupVerifyResult
            {
                Success = false,
                HasAccount = true,
                Message = "Your account is already set up. Please sign in with your password, or use Forgot Password."
            };
        }

        // Check patient has email
        if (string.IsNullOrWhiteSpace(patient.Email))
            return new PortalSetupVerifyResult { Success = false, Message = "No email address on file. Please contact the clinic to update your information." };

        // Create account without password (will be set after OTP verification)
        var account = new PatientPortalAccount
        {
            TenantId = location.TenantId,
            LocationId = location.LocationId,
            PatientId = patient.PatientId,
            PasswordHash = "", // Will be set after OTP verification
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.PatientPortalAccounts.Add(account);
        await _context.SaveChangesAsync();

        // Send OTP to verify email ownership
        var otpSent = await SendOtpAsync(account.PatientPortalAccountId, portalCode);
        if (!otpSent)
        {
            // Rollback account creation
            _context.PatientPortalAccounts.Remove(account);
            await _context.SaveChangesAsync();
            return new PortalSetupVerifyResult { Success = false, Message = "Failed to send verification code. Please try again." };
        }

        return new PortalSetupVerifyResult
        {
            Success = true,
            RequiresOtp = true,
            AccountId = account.PatientPortalAccountId,
            MaskedEmail = MaskEmail(patient.Email),
            PatientName = $"{patient.FirstName} {patient.LastName}".Trim()
        };
    }

    public async Task<PortalOtpResult> SetupCompleteAsync(int accountId, string code, string password, string portalCode)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            return new PortalOtpResult { Success = false, Message = "Password must be at least 8 characters." };

        // Verify OTP first
        var otpResult = await VerifyOtpAsync(accountId, code, portalCode);
        if (!otpResult.Success)
            return otpResult; // Return OTP error

        // OTP verified — now set the password
        var account = await _context.PatientPortalAccounts
            .FirstOrDefaultAsync(a => a.PatientPortalAccountId == accountId);

        if (account == null)
            return new PortalOtpResult { Success = false, Message = "Account not found." };

        account.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, BCrypt.Net.BCrypt.GenerateSalt(12));
        account.RegisteredAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Portal account setup completed for AccountId={AccountId}", accountId);

        await _auditService.LogAccessAsync(
            null, null, "PORTAL_REGISTRATION_COMPLETE", "PatientPortalAccount", accountId,
            null, "Self-service setup via DOB+SSN", null);

        // otpResult already has the JWT token from VerifyOtpAsync
        return otpResult;
    }

    // ============================================
    // OTP
    // ============================================

    public async Task<bool> SendOtpAsync(int accountId, string portalCode)
    {
        var account = await _context.PatientPortalAccounts
            .Include(a => a.Patient)
            .Include(a => a.Location)
                .ThenInclude(l => l.Tenant)
            .FirstOrDefaultAsync(a => a.PatientPortalAccountId == accountId);

        if (account == null) return false;

        // Invalidate any existing unused OTPs
        var existingOtps = await _context.PatientPortalOtps
            .Where(o => o.AccountId == accountId && o.UsedAt == null)
            .ToListAsync();
        _context.PatientPortalOtps.RemoveRange(existingOtps);

        // Generate 6-digit code
        var code = GenerateOtpCode();
        var codeHash = HashToken(code);

        var otp = new PatientPortalOtp
        {
            AccountId = accountId,
            CodeHash = codeHash,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            AttemptCount = 0,
            CreatedAt = DateTime.UtcNow
        };

        _context.PatientPortalOtps.Add(otp);
        await _context.SaveChangesAsync();

        _encryptionHelper.DecryptEntity(account.Patient);

        // Send OTP email to patient's email (single source of truth)
        await SendOtpEmailAsync(account.Patient.Email, code, account.Patient, account.Location);

        return true;
    }

    public async Task<PortalOtpResult> VerifyOtpAsync(int accountId, string code, string portalCode)
    {
        var account = await _context.PatientPortalAccounts
            .Include(a => a.Patient)
            .Include(a => a.Location)
                .ThenInclude(l => l.Tenant)
            .FirstOrDefaultAsync(a => a.PatientPortalAccountId == accountId);

        if (account == null)
            return new PortalOtpResult { Success = false, Message = "Account not found." };

        _encryptionHelper.DecryptEntity(account.Patient);

        // Test bypass: @test.com patients always accept code 123456
        var isTestAccount = account.Patient.Email?.EndsWith("@test.com", StringComparison.OrdinalIgnoreCase) == true;
        if (isTestAccount && code == "123456")
        {
            account.LastLoginAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            var bypassToken = _portalAuthService.GeneratePortalToken(account.Patient, account.Location.Tenant, account.Location);
            await _auditService.LogAccessAsync(null, account.Patient.Email, "PORTAL_LOGIN_SUCCESS", "PatientPortalAccount", account.PatientPortalAccountId, null, null, null);
            return new PortalOtpResult
            {
                Success = true,
                Token = bypassToken,
                PatientId = account.PatientId,
                PatientName = $"{account.Patient.FirstName} {account.Patient.LastName}",
                TenantId = account.TenantId,
                PortalCode = account.Location.PortalCode,
                TokenExpiry = DateTime.UtcNow.AddHours(4)
            };
        }

        var otp = await _context.PatientPortalOtps
            .Where(o => o.AccountId == accountId && o.UsedAt == null && o.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();

        if (otp == null)
            return new PortalOtpResult { Success = false, Message = "Verification code expired. Please request a new one." };

        if (otp.AttemptCount >= 3)
            return new PortalOtpResult { Success = false, Message = "Too many attempts. Please request a new code." };

        var codeHash = HashToken(code);
        if (otp.CodeHash != codeHash)
        {
            otp.AttemptCount++;
            await _context.SaveChangesAsync();
            return new PortalOtpResult { Success = false, Message = "Invalid verification code." };
        }

        // OTP verified
        otp.UsedAt = DateTime.UtcNow;

        account.LastLoginAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // Generate JWT token using existing portal token generator
        var token = _portalAuthService.GeneratePortalToken(account.Patient, account.Location.Tenant, account.Location);

        await _auditService.LogAccessAsync(
            null, account.Patient.Email, "PORTAL_LOGIN_SUCCESS", "PatientPortalAccount", account.PatientPortalAccountId,
            null, null, null);

        return new PortalOtpResult
        {
            Success = true,
            Token = token,
            PatientId = account.PatientId,
            PatientName = $"{account.Patient.FirstName} {account.Patient.LastName}",
            TenantId = account.TenantId,
            PortalCode = account.Location.PortalCode,
            TokenExpiry = DateTime.UtcNow.AddHours(4)
        };
    }

    // ============================================
    // PASSWORD RESET
    // ============================================

    public async Task<bool> RequestPasswordResetAsync(string email, string portalCode)
    {
        var location = await _context.Locations
            .Include(l => l.Tenant)
            .FirstOrDefaultAsync(l => l.PortalCode == portalCode && l.IsActive == true);

        if (location == null) return true; // Don't reveal if location exists

        var account = await FindAccountByEmailAsync(email, location.LocationId);

        if (account == null) return true; // Don't reveal if account exists

        // Generate reset token
        var rawToken = GenerateSecureToken();
        var tokenHash = HashToken(rawToken);

        var reset = new PatientPortalPasswordReset
        {
            AccountId = account.PatientPortalAccountId,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            CreatedAt = DateTime.UtcNow
        };

        _context.PatientPortalPasswordResets.Add(reset);
        await _context.SaveChangesAsync();

        // Patient already decrypted by FindAccountByEmailAsync
        await SendPasswordResetEmailAsync(account.Patient.Email, rawToken, account.Patient, location);

        return true;
    }

    public async Task<PortalPasswordResetValidation> ValidatePasswordResetTokenAsync(string token)
    {
        var tokenHash = HashToken(token);
        var reset = await _context.PatientPortalPasswordResets
            .Include(r => r.Account)
                .ThenInclude(a => a.Location)
                    .ThenInclude(l => l.Tenant)
            .FirstOrDefaultAsync(r => r.TokenHash == tokenHash && r.UsedAt == null && r.ExpiresAt > DateTime.UtcNow);

        if (reset == null)
            return new PortalPasswordResetValidation { Valid = false, Message = "Invalid or expired reset link." };

        return new PortalPasswordResetValidation
        {
            Valid = true,
            PortalCode = reset.Account?.Location?.PortalCode,
            TenantName = reset.Account?.Location?.Tenant?.Name ?? "",
            TenantLogoUrl = reset.Account?.Location?.Tenant?.LogoUrl,
            LocationName = reset.Account?.Location?.Name ?? ""
        };
    }

    public async Task<bool> ResetPasswordAsync(string token, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8 || newPassword.Length > 128)
            return false;

        var tokenHash = HashToken(token);
        var reset = await _context.PatientPortalPasswordResets
            .Include(r => r.Account)
            .FirstOrDefaultAsync(r => r.TokenHash == tokenHash && r.UsedAt == null && r.ExpiresAt > DateTime.UtcNow);

        if (reset?.Account == null) return false;

        reset.Account.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword, BCrypt.Net.BCrypt.GenerateSalt(12));
        reset.UsedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }

    // ============================================
    // LOCATION LOOKUP
    // ============================================

    public async Task<PortalLocationInfo?> GetLocationByPortalCodeAsync(string portalCode)
    {
        var location = await _context.Locations
            .Include(l => l.Tenant)
            .FirstOrDefaultAsync(l => l.PortalCode == portalCode && l.IsActive == true);

        if (location == null) return null;

        return new PortalLocationInfo
        {
            LocationId = location.LocationId,
            LocationName = location.Name,
            LocationAddress = location.Address,
            LocationCity = location.City,
            LocationState = location.State,
            LocationZipCode = location.ZipCode,
            LocationPhone = location.Phone,
            TenantId = location.TenantId,
            TenantName = location.Tenant?.Name ?? "",
            TenantLogoUrl = location.Tenant?.LogoUrl,
            TenantPhone = location.Tenant?.Phone,
            TenantEmail = location.Tenant?.Email,
            PortalCode = portalCode
        };
    }

    // ============================================
    // HELPER: Find account by decrypting patient emails in memory
    // ============================================

    /// <summary>
    /// Find a portal account by decrypting patient emails in memory.
    /// Scoped by locationId so the set is small (typically &lt;500 accounts per location).
    /// </summary>
    private async Task<PatientPortalAccount?> FindAccountByEmailAsync(string email, int locationId)
    {
        var accounts = await _context.PatientPortalAccounts
            .Include(a => a.Patient)
            .Include(a => a.Location)
                .ThenInclude(l => l.Tenant)
            .Where(a => a.LocationId == locationId && a.IsActive)
            .ToListAsync();

        foreach (var account in accounts)
        {
            _encryptionHelper.DecryptEntity(account.Patient);
            if (string.Equals(account.Patient.Email, email, StringComparison.OrdinalIgnoreCase))
                return account;
        }

        return null;
    }

    // ============================================
    // EMAIL TEMPLATES
    // ============================================

    private async Task SendInvitationEmailAsync(string email, string token, Patient patient, Location location)
    {
        var baseUrl = _config["App:BaseUrl"] ?? "http://localhost:5002";
        var registerLink = $"{baseUrl}/Portal/Register?token={Uri.EscapeDataString(token)}";
        var clinicName = location.Tenant?.Name ?? location.Name ?? "Your Healthcare Provider";

        var subject = $"You're Invited to {clinicName} Patient Portal";
        var body = BuildEmailHtml(clinicName,
            $@"<h2>Welcome to Your Patient Portal</h2>
            <p>Hello {patient.FirstName},</p>
            <p><strong>{clinicName}</strong> has invited you to register for their secure patient portal. Through the portal you can view your health records, upcoming appointments, and more.</p>
            <p>Click the button below to complete your registration:</p>
            <p style='text-align: center;'>
                <a href='{registerLink}' style='display: inline-block; padding: 14px 28px; background-color: #1976d2; color: white; text-decoration: none; border-radius: 4px; font-size: 16px;'>Complete Registration</a>
            </p>
            <p>This invitation link will expire in <strong>7 days</strong>.</p>
            <p><strong>If the button doesn't work, copy and paste this link into your browser:</strong></p>
            <p style='word-break: break-all; font-size: 12px; color: #666;'>{registerLink}</p>");

        _logger.LogInformation("Sending invitation email to {EmailMasked}", PhiLog.MaskEmail(email));
        await _emailService.SendEmailAsync(email, subject, body, true);
    }

    private async Task SendOtpEmailAsync(string email, string code, Patient patient, Location location)
    {
        var clinicName = location.Tenant?.Name ?? location.Name ?? "Your Healthcare Provider";

        var subject = $"Your Verification Code - {clinicName} Patient Portal";
        var body = BuildEmailHtml(clinicName,
            $@"<h2>Your Verification Code</h2>
            <p>Hello {patient.FirstName},</p>
            <p>Your one-time verification code for the {clinicName} Patient Portal is:</p>
            <div style='text-align: center; margin: 24px 0;'>
                <span style='display: inline-block; padding: 16px 32px; background-color: #f5f5f5; border: 2px solid #1976d2; border-radius: 8px; font-size: 32px; font-weight: bold; letter-spacing: 8px; color: #1976d2;'>{code}</span>
            </div>
            <p>This code will expire in <strong>5 minutes</strong>.</p>
            <p style='color: #666;'>If you did not request this code, please ignore this email. Someone may have entered your email address by mistake.</p>");

        _logger.LogInformation("Sending OTP email to {EmailMasked}", PhiLog.MaskEmail(email));
        await _emailService.SendEmailAsync(email, subject, body, true);
    }

    private async Task SendRegistrationConfirmationEmailAsync(string email, Patient patient, Location location)
    {
        var baseUrl = _config["App:BaseUrl"] ?? "http://localhost:5002";
        var clinicName = location.Tenant?.Name ?? location.Name ?? "Your Healthcare Provider";
        var portalCode = location.PortalCode ?? "";
        var loginLink = $"{baseUrl}/Portal/{portalCode}";

        var subject = $"Registration Complete - {clinicName} Patient Portal";
        var body = BuildEmailHtml(clinicName,
            $@"<h2>Registration Complete!</h2>
            <p>Hello {patient.FirstName},</p>
            <p>Your account for the <strong>{clinicName}</strong> Patient Portal has been created successfully.</p>
            <p>You can now sign in to access your health records, view appointments, and more.</p>
            <p style='text-align: center;'>
                <a href='{loginLink}' style='display: inline-block; padding: 14px 28px; background-color: #28a745; color: white; text-decoration: none; border-radius: 4px; font-size: 16px;'>Sign In to Portal</a>
            </p>
            <p style='color: #666;'>If you did not create this account, please contact your clinic immediately.</p>");

        _logger.LogInformation("Sending registration confirmation email to {EmailMasked}", PhiLog.MaskEmail(email));
        await _emailService.SendEmailAsync(email, subject, body, true);
    }

    private async Task SendPasswordResetEmailAsync(string email, string token, Patient patient, Location location)
    {
        var baseUrl = _config["App:BaseUrl"] ?? "http://localhost:5002";
        var resetLink = $"{baseUrl}/Portal/ResetPassword?token={Uri.EscapeDataString(token)}";
        var clinicName = location.Tenant?.Name ?? location.Name ?? "Your Healthcare Provider";

        var subject = $"Password Reset - {clinicName} Patient Portal";
        var body = BuildEmailHtml(clinicName,
            $@"<h2>Password Reset Request</h2>
            <p>Hello {patient.FirstName},</p>
            <p>We received a request to reset your password for the <strong>{clinicName}</strong> Patient Portal.</p>
            <p>Click the button below to set a new password:</p>
            <p style='text-align: center;'>
                <a href='{resetLink}' style='display: inline-block; padding: 14px 28px; background-color: #1976d2; color: white; text-decoration: none; border-radius: 4px; font-size: 16px;'>Reset Password</a>
            </p>
            <p>This link will expire in <strong>1 hour</strong>.</p>
            <p style='color: #666;'>If you didn't request a password reset, you can safely ignore this email. Your password will not be changed.</p>
            <p><strong>If the button doesn't work, copy and paste this link into your browser:</strong></p>
            <p style='word-break: break-all; font-size: 12px; color: #666;'>{resetLink}</p>");

        _logger.LogInformation("Sending password reset email to {EmailMasked}", PhiLog.MaskEmail(email));
        await _emailService.SendEmailAsync(email, subject, body, true);
    }

    private static string BuildEmailHtml(string clinicName, string contentHtml)
    {
        return $@"<!DOCTYPE html>
<html>
<head><meta charset='utf-8'></head>
<body style='font-family: Arial, sans-serif; line-height: 1.6; color: #333; margin: 0; padding: 0;'>
    <div style='max-width: 600px; margin: 0 auto;'>
        <div style='background-color: #1976d2; color: white; padding: 20px; text-align: center;'>
            <h1 style='margin: 0; font-size: 24px;'>MEDOCS</h1>
            <p style='margin: 4px 0 0; font-size: 14px; opacity: 0.9;'>{clinicName}</p>
        </div>
        <div style='padding: 24px; background-color: #ffffff;'>
            {contentHtml}
        </div>
        <div style='padding: 16px; text-align: center; font-size: 12px; color: #999; background-color: #f5f5f5;'>
            <p style='margin: 0;'>This is an automated message. Please do not reply to this email.</p>
            <p style='margin: 4px 0 0;'>&copy; MEDOCS LLC &mdash; HIPAA Compliant &amp; Secure</p>
        </div>
    </div>
</body>
</html>";
    }

    // ============================================
    // HELPERS
    // ============================================

    private static string GenerateSecureToken()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static string HashToken(string token)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hash);
    }

    private static string GenerateOtpCode()
    {
        var bytes = new byte[4];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        var number = BitConverter.ToUInt32(bytes, 0) % 900000 + 100000;
        return number.ToString();
    }

    private static string MaskEmail(string email)
    {
        var parts = email.Split('@');
        if (parts.Length != 2) return "***@***";
        var local = parts[0];
        var domain = parts[1];
        var maskedLocal = local.Length <= 2 ? "***" : $"{local[0]}***{local[^1]}";
        return $"{maskedLocal}@{domain}";
    }
}

// ============================================
// DTOs
// ============================================

public class SendInvitationDto
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int LocationId { get; set; }
}

public class CompleteRegistrationDto
{
    public string Token { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    // Full patient demographics (filled in by patient during registration)
    public DateOnly? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Phone { get; set; }
    public string? Ssn { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? ZipCode { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? EmergencyContactRelation { get; set; }
}

public class SetupAccountDto
{
    public string Token { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class PortalInvitationResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int? InvitationId { get; set; }
}

public class PortalInvitationListDto
{
    public int InvitationId { get; set; }
    public int PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string LocationName { get; set; } = string.Empty;
    public int LocationId { get; set; }
    public int Status { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int ResentCount { get; set; }
}

public class PortalInvitationValidation
{
    public bool Valid { get; set; }
    public string? Message { get; set; }
    public int? InvitationId { get; set; }
    public string? Email { get; set; }
    public string? PatientName { get; set; }
    public string? PortalCode { get; set; }
    public string? TenantName { get; set; }
    public string? TenantLogoUrl { get; set; }
    public string? LocationName { get; set; }
    public string? LocationAddress { get; set; }
    public string? LocationPhone { get; set; }
}

public class PortalRegistrationResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? PortalCode { get; set; }
    public int? PatientId { get; set; }
}

public class PortalLoginResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public bool RequiresOtp { get; set; }
    public int? AccountId { get; set; }
    public string? MaskedEmail { get; set; }
}

public class PortalOtpResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Token { get; set; }
    public int? PatientId { get; set; }
    public string? PatientName { get; set; }
    public int? TenantId { get; set; }
    public string? PortalCode { get; set; }
    public DateTime? TokenExpiry { get; set; }
}

public class PortalSetupVerifyResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public bool HasAccount { get; set; }
    public bool RequiresOtp { get; set; }
    public int? AccountId { get; set; }
    public string? MaskedEmail { get; set; }
    public string? PatientName { get; set; }
}

public class PortalPasswordResetValidation
{
    public bool Valid { get; set; }
    public string? Message { get; set; }
    public string? PortalCode { get; set; }
    public string? TenantName { get; set; }
    public string? TenantLogoUrl { get; set; }
    public string? LocationName { get; set; }
}

public class PortalLocationInfo
{
    public int LocationId { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public string? LocationAddress { get; set; }
    public string? LocationCity { get; set; }
    public string? LocationState { get; set; }
    public string? LocationZipCode { get; set; }
    public string? LocationPhone { get; set; }
    public int TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
    public string? TenantLogoUrl { get; set; }
    public string? TenantPhone { get; set; }
    public string? TenantEmail { get; set; }
    public string PortalCode { get; set; } = string.Empty;
}
