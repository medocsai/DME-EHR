using Microsoft.EntityFrameworkCore;
using EHR.Models.Generated;
using EHR.Helpers;
using EHR.Data;
using EHR.Hubs;

namespace EHR.Services;

public interface IPatientMessagingService
{
    // Patient-facing
    Task<List<PatientMessagingUserDto>> GetUsersForPatientAsync(int patientId);
    Task<List<PatientConversationListDto>> GetConversationsForPatientAsync(int patientId);
    Task<PatientConversationListDto?> GetOrCreateConversationAsync(int patientId, int userId);
    Task<List<PatientMessageDto>> GetMessagesAsync(int conversationId, int requesterId, string requesterType, int? beforeMessageId = null, int pageSize = 50);
    Task<PatientMessageSentDto> SendMessageFromPatientAsync(int patientId, int conversationId, string messageText);

    // Staff-facing (any role)
    Task<List<PatientConversationListDto>> GetConversationsForUserAsync(int userId);
    Task<PatientMessageSentDto> SendMessageFromUserAsync(int userId, int conversationId, string messageText);
    Task<PatientMessagingUnreadDto> GetUnreadSummaryForUserAsync(int userId);
    Task<PatientMessagingUnreadDto> GetUnreadSummaryForPatientAsync(int patientId);

    // Patient Profile tab (any staff — no user-specific filter)
    Task<List<PatientConversationListDto>> GetAllConversationsForPatientAsync(int patientId);
    Task<List<PatientMessageDto>> GetMessagesForProfileAsync(int conversationId, int patientId);

    // Shared
    Task<bool> MarkAsReadByPatientAsync(int conversationId, int patientId);
    Task<bool> MarkAsReadByUserAsync(int conversationId, int userId);
    Task<PatientConversation?> GetConversationAsync(int conversationId);

    // All patients list (WhatsApp-style)
    Task<PatientMessagingListResult> GetAllPatientsForMessagingAsync(int tenantId, int userId, int? locationId, string? search, int skip, int take);

    // System messages (document upload notifications — fans out to ALL active users in tenant)
    Task<List<SystemMessageResult>> CreateSystemMessageAsync(int patientId, int tenantId, string messageText);

    // Email fallback
    Task<string?> GetPatientEmailAsync(int patientId);
    Task<PatientEmailContextDto?> GetEmailContextAsync(int conversationId);
}

// ============================================
// DTOs
// ============================================

/// <summary>
/// Staff users that a patient can message (replaces provider-only list)
/// </summary>
public class PatientMessagingUserDto
{
    public int UserId { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string FullName => $"{FirstName} {LastName}".Trim();
    public int? Role { get; set; }
    public string RoleLabel { get; set; } = "";
    public string? Specialty { get; set; }
    public string? Credentials { get; set; }
    public string? ProfilePicturePath { get; set; }
    public int? ExistingConversationId { get; set; }
}

public class PatientConversationListDto
{
    public int PatientConversationId { get; set; }
    public int PatientId { get; set; }
    public string PatientName { get; set; } = "";
    public string? PatientProfilePicture { get; set; }
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public int? UserRole { get; set; }
    public string UserRoleLabel { get; set; } = "";
    public string? UserSpecialty { get; set; }
    public string? UserProfilePicture { get; set; }
    public string? LastMessageText { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public string? LastMessageSenderType { get; set; }
    public int UnreadCount { get; set; }
}

public class PatientMessageDto
{
    public int PatientMessageId { get; set; }
    public int PatientConversationId { get; set; }
    public string SenderType { get; set; } = "";
    public string SenderName { get; set; } = "";
    public string MessageText { get; set; } = "";
    public bool IsReadByPatient { get; set; }
    public bool IsReadByUser { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedAtFormatted => CreatedAt.ToString("MMM dd, yyyy h:mm tt");
}

public class PatientMessageSentDto
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int PatientMessageId { get; set; }
    public int PatientConversationId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class PatientMessagingUnreadDto
{
    public int TotalUnreadCount { get; set; }
    public int UnreadConversationsCount { get; set; }
}

public class PatientMessagingListResult
{
    public List<PatientMessagingListItem> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public bool HasMore { get; set; }
}

public class PatientMessagingListItem
{
    public int PatientId { get; set; }
    public string PatientName { get; set; } = "";
    public string? Mrn { get; set; }
    public string? DateOfBirth { get; set; }
    public string? ProfilePicture { get; set; }
    public string? LocationName { get; set; }
    // Conversation info (null if no conversation exists)
    public int? ConversationId { get; set; }
    public string? LastMessageText { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public string? LastMessageSenderType { get; set; }
    public int UnreadCount { get; set; }
    public bool HasConversation => ConversationId.HasValue;
    public int PatientDocumentCount { get; set; }
}

public class PatientEmailContextDto
{
    public string PatientEmail { get; set; } = "";
    public string PatientFirstName { get; set; } = "";
    public string UserName { get; set; } = "";
    public string MessageText { get; set; } = "";
    public int PatientConversationId { get; set; }
    public string? PortalCode { get; set; }
}

public class SystemMessageResult
{
    public int UserId { get; set; }
    public int PatientConversationId { get; set; }
    public int PatientMessageId { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ============================================
// Service Implementation
// ============================================

public class PatientMessagingService : IPatientMessagingService
{
    private readonly EhrDbContext _context;
    private readonly ITenantProvider _tenantProvider;
    private readonly EncryptionHelper _encryption;
    private readonly ILogger<PatientMessagingService> _logger;

    public PatientMessagingService(
        EhrDbContext context,
        ITenantProvider tenantProvider,
        EncryptionHelper encryption,
        ILogger<PatientMessagingService> logger)
    {
        _context = context;
        _tenantProvider = tenantProvider;
        _encryption = encryption;
        _logger = logger;
    }

    private int TenantId => _tenantProvider.TenantId ?? 0;

    // ============================================
    // Patient-facing methods
    // ============================================

    public async Task<List<PatientMessagingUserDto>> GetUsersForPatientAsync(int patientId)
    {
        // Get distinct providers from completed/checked-in/in-progress appointments,
        // then resolve their User accounts
        var providerIds = await _context.Appointments
            .Where(a => a.TenantId == TenantId
                && a.PatientId == patientId
                && a.Status >= 2 && a.Status <= 4)
            .Select(a => a.ProviderId)
            .Distinct()
            .ToListAsync();

        if (!providerIds.Any())
            return new List<PatientMessagingUserDto>();

        // Get the User accounts linked to these providers
        var users = await _context.Users
            .Include(u => u.Provider)
            .Where(u => u.TenantId == TenantId && u.ProviderId != null && providerIds.Contains(u.ProviderId.Value) && u.IsActive == true)
            .ToListAsync();

        // Get existing conversations for this patient
        var existingConversations = await _context.PatientConversations
            .Where(c => c.TenantId == TenantId && c.PatientId == patientId && c.IsActive)
            .ToDictionaryAsync(c => c.UserId, c => c.PatientConversationId);

        return users.Select(u => new PatientMessagingUserDto
        {
            UserId = u.UserId,
            FirstName = u.FirstName ?? "",
            LastName = u.LastName ?? "",
            Role = u.Role,
            RoleLabel = GetRoleLabel(u.Role),
            Specialty = u.Provider?.Specialty,
            Credentials = u.Provider?.Credentials,
            ProfilePicturePath = u.Provider?.ProfilePicturePath,
            ExistingConversationId = existingConversations.GetValueOrDefault(u.UserId)
        }).OrderBy(u => u.LastName).ThenBy(u => u.FirstName).ToList();
    }

    public async Task<List<PatientConversationListDto>> GetConversationsForPatientAsync(int patientId)
    {
        var conversations = await _context.PatientConversations
            .Include(c => c.User)
                .ThenInclude(u => u.Provider)
            .Where(c => c.TenantId == TenantId && c.PatientId == patientId && c.IsActive)
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .ToListAsync();

        // Filter out conversations where the only activity is system messages (patient shouldn't see those)
        return conversations
            .Where(c => c.LastMessageSenderType != "System" || c.PatientUnreadCount > 0)
            .Select(c => new PatientConversationListDto
            {
                PatientConversationId = c.PatientConversationId,
                PatientId = c.PatientId,
                UserId = c.UserId,
                UserName = $"{c.User.FirstName} {c.User.LastName}".Trim(),
                UserRole = c.User.Role,
                UserRoleLabel = GetRoleLabel(c.User.Role),
                UserSpecialty = c.User.Provider?.Specialty,
                UserProfilePicture = c.User.Provider?.ProfilePicturePath,
                // Hide system message previews from patient
                LastMessageText = c.LastMessageSenderType == "System" ? null
                    : c.LastMessageText != null ? _encryption.Decrypt(c.LastMessageText) : null,
                LastMessageAt = c.LastMessageSenderType == "System" ? null : c.LastMessageAt,
                LastMessageSenderType = c.LastMessageSenderType == "System" ? null : c.LastMessageSenderType,
                UnreadCount = c.PatientUnreadCount
            }).ToList();
    }

    public async Task<PatientConversationListDto?> GetOrCreateConversationAsync(int patientId, int userId)
    {
        var existing = await _context.PatientConversations
            .Include(c => c.User)
                .ThenInclude(u => u.Provider)
            .Include(c => c.Patient)
            .FirstOrDefaultAsync(c => c.TenantId == TenantId && c.PatientId == patientId && c.UserId == userId);

        if (existing != null)
        {
            // CRITICAL (PHI encryption safety):
            // Detach existing.Patient before decryption. Any subsequent
            // SaveChangesAsync in this request would otherwise flush the
            // decrypted patient back to the DB.
            if (existing.Patient != null)
                _context.Entry(existing.Patient).State = EntityState.Detached;
            _encryption.DecryptEntity(existing.Patient);
            return MapConversationDto(existing, "Patient");
        }

        // Verify patient exists in the same tenant
        var patientExists = await _context.Patients
            .AnyAsync(p => p.TenantId == TenantId && p.PatientId == patientId && p.IsDeleted != true);

        if (!patientExists)
        {
            _logger.LogWarning("Cannot create conversation — patient {PatientId} not found in tenant", patientId);
            return null;
        }

        var conversation = new PatientConversation
        {
            TenantId = TenantId,
            PatientId = patientId,
            UserId = userId,
            CreatedAt = DateTime.UtcNow
        };

        _context.PatientConversations.Add(conversation);
        await _context.SaveChangesAsync();

        // Reload with nav props
        await _context.Entry(conversation).Reference(c => c.User).LoadAsync();
        if (conversation.User?.ProviderId != null)
            await _context.Entry(conversation.User).Reference(u => u.Provider).LoadAsync();
        await _context.Entry(conversation).Reference(c => c.Patient).LoadAsync();

        // CRITICAL (PHI encryption safety): detach patient before decrypt so
        // any downstream SaveChangesAsync cannot flush plaintext to the DB.
        if (conversation.Patient != null)
            _context.Entry(conversation.Patient).State = EntityState.Detached;
        _encryption.DecryptEntity(conversation.Patient);

        return MapConversationDto(conversation, "Patient");
    }

    public async Task<List<PatientMessageDto>> GetMessagesAsync(int conversationId, int requesterId, string requesterType, int? beforeMessageId = null, int pageSize = 50)
    {
        // Verify access
        var conversation = await _context.PatientConversations
            .FirstOrDefaultAsync(c => c.PatientConversationId == conversationId && c.TenantId == TenantId);

        if (conversation == null) return new List<PatientMessageDto>();

        if (requesterType == "Patient" && conversation.PatientId != requesterId)
            return new List<PatientMessageDto>();
        if (requesterType == "Provider" && conversation.UserId != requesterId)
            return new List<PatientMessageDto>();

        var query = _context.PatientMessages
            .Where(m => m.PatientConversationId == conversationId && m.TenantId == TenantId);

        // Hide system messages from patient view (document upload notifications are clinic-only)
        if (requesterType == "Patient")
            query = query.Where(m => m.SenderType != "System");

        if (beforeMessageId.HasValue)
            query = query.Where(m => m.PatientMessageId < beforeMessageId.Value);

        // AsNoTracking: read-only messages list; SenderPatient decrypted for DTO.
        var messages = await query
            .AsNoTracking()
            .OrderByDescending(m => m.CreatedAt)
            .Take(pageSize)
            .Include(m => m.SenderPatient)
            .Include(m => m.SenderUser)
            .ToListAsync();

        return messages.Select(m =>
        {
            string senderName;
            if (m.SenderType == "Patient" && m.SenderPatient != null)
            {
                _encryption.DecryptEntity(m.SenderPatient);
                senderName = $"{m.SenderPatient.FirstName} {m.SenderPatient.LastName}".Trim();
            }
            else if (m.SenderType == "Provider" && m.SenderUser != null)
            {
                senderName = $"{m.SenderUser.FirstName} {m.SenderUser.LastName}".Trim();
            }
            else if (m.SenderType == "System")
            {
                senderName = "System";
            }
            else
            {
                senderName = "Unknown";
            }

            return new PatientMessageDto
            {
                PatientMessageId = m.PatientMessageId,
                PatientConversationId = m.PatientConversationId,
                SenderType = m.SenderType,
                SenderName = senderName,
                MessageText = _encryption.Decrypt(m.MessageText) ?? m.MessageText,
                IsReadByPatient = m.IsReadByPatient,
                IsReadByUser = m.IsReadByUser,
                CreatedAt = m.CreatedAt
            };
        }).OrderBy(m => m.CreatedAt).ToList(); // Return in chronological order
    }

    public async Task<PatientMessageSentDto> SendMessageFromPatientAsync(int patientId, int conversationId, string messageText)
    {
        return await SendMessageAsync(conversationId, "Patient", patientId, null, messageText);
    }

    // ============================================
    // Staff-facing methods (any role)
    // ============================================

    public async Task<List<PatientConversationListDto>> GetConversationsForUserAsync(int userId)
    {
        // AsNoTracking: read-only conversation list; Patient decrypted for DTO.
        var conversations = await _context.PatientConversations
            .AsNoTracking()
            .Include(c => c.Patient)
            .Where(c => c.TenantId == TenantId && c.UserId == userId && c.IsActive)
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .ToListAsync();

        return conversations.Select(c =>
        {
            _encryption.DecryptEntity(c.Patient);
            return new PatientConversationListDto
            {
                PatientConversationId = c.PatientConversationId,
                PatientId = c.PatientId,
                PatientName = $"{c.Patient.FirstName} {c.Patient.LastName}".Trim(),
                UserId = c.UserId,
                LastMessageText = c.LastMessageText != null ? _encryption.Decrypt(c.LastMessageText) : null,
                LastMessageAt = c.LastMessageAt,
                LastMessageSenderType = c.LastMessageSenderType,
                UnreadCount = c.UserUnreadCount
            };
        }).ToList();
    }

    public async Task<PatientMessageSentDto> SendMessageFromUserAsync(int userId, int conversationId, string messageText)
    {
        return await SendMessageAsync(conversationId, "Provider", null, userId, messageText);
    }

    public async Task<PatientMessagingUnreadDto> GetUnreadSummaryForUserAsync(int userId)
    {
        var conversations = await _context.PatientConversations
            .Where(c => c.TenantId == TenantId && c.UserId == userId && c.IsActive && c.UserUnreadCount > 0)
            .ToListAsync();

        return new PatientMessagingUnreadDto
        {
            TotalUnreadCount = conversations.Sum(c => c.UserUnreadCount),
            UnreadConversationsCount = conversations.Count
        };
    }

    public async Task<PatientMessagingUnreadDto> GetUnreadSummaryForPatientAsync(int patientId)
    {
        var conversations = await _context.PatientConversations
            .Where(c => c.TenantId == TenantId && c.PatientId == patientId && c.IsActive && c.PatientUnreadCount > 0)
            .ToListAsync();

        return new PatientMessagingUnreadDto
        {
            TotalUnreadCount = conversations.Sum(c => c.PatientUnreadCount),
            UnreadConversationsCount = conversations.Count
        };
    }

    // ============================================
    // Shared methods
    // ============================================

    public async Task<bool> MarkAsReadByPatientAsync(int conversationId, int patientId)
    {
        var conversation = await _context.PatientConversations
            .FirstOrDefaultAsync(c => c.PatientConversationId == conversationId
                && c.TenantId == TenantId && c.PatientId == patientId);

        if (conversation == null) return false;

        // Mark all unread provider messages as read
        var unreadMessages = await _context.PatientMessages
            .Where(m => m.PatientConversationId == conversationId
                && m.SenderType == "Provider"
                && !m.IsReadByPatient)
            .ToListAsync();

        foreach (var msg in unreadMessages)
        {
            msg.IsReadByPatient = true;
            msg.ReadByPatientAt = DateTime.UtcNow;
        }

        conversation.PatientUnreadCount = 0;
        conversation.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> MarkAsReadByUserAsync(int conversationId, int userId)
    {
        // Only the conversation's user can mark as read (1-to-1 messaging)
        var conversation = await _context.PatientConversations
            .FirstOrDefaultAsync(c => c.PatientConversationId == conversationId
                && c.TenantId == TenantId && c.UserId == userId);

        if (conversation == null) return false;

        var unreadMessages = await _context.PatientMessages
            .Where(m => m.PatientConversationId == conversationId
                && (m.SenderType == "Patient" || m.SenderType == "System")
                && !m.IsReadByUser)
            .ToListAsync();

        foreach (var msg in unreadMessages)
        {
            msg.IsReadByUser = true;
            msg.ReadByUserAt = DateTime.UtcNow;
        }

        conversation.UserUnreadCount = 0;
        conversation.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<PatientConversation?> GetConversationAsync(int conversationId)
    {
        return await _context.PatientConversations
            .FirstOrDefaultAsync(c => c.PatientConversationId == conversationId && c.TenantId == TenantId);
    }

    // ============================================
    // Email fallback helpers
    // ============================================

    public async Task<string?> GetPatientEmailAsync(int patientId)
    {
        // AsNoTracking: read-only email lookup; Patient decrypted to read .Email.
        var patient = await _context.Patients
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PatientId == patientId && p.TenantId == TenantId);

        if (patient == null) return null;
        _encryption.DecryptEntity(patient);
        return patient.Email;
    }

    public async Task<PatientEmailContextDto?> GetEmailContextAsync(int conversationId)
    {
        // AsNoTracking: read-only email context lookup; Patient decrypted for DTO.
        var conversation = await _context.PatientConversations
            .AsNoTracking()
            .Include(c => c.Patient)
            .Include(c => c.User)
            .FirstOrDefaultAsync(c => c.PatientConversationId == conversationId && c.TenantId == TenantId);

        if (conversation == null) return null;

        _encryption.DecryptEntity(conversation.Patient);

        // Get the portal code for the patient's location
        var portalAccount = await _context.PatientPortalAccounts
            .FirstOrDefaultAsync(a => a.PatientId == conversation.PatientId && a.TenantId == TenantId && a.IsActive);

        string? portalCode = null;
        if (portalAccount != null)
        {
            var location = await _context.Locations
                .FirstOrDefaultAsync(l => l.LocationId == portalAccount.LocationId && l.TenantId == TenantId);
            portalCode = location?.PortalCode;
        }

        return new PatientEmailContextDto
        {
            PatientEmail = conversation.Patient.Email ?? "",
            PatientFirstName = conversation.Patient.FirstName ?? "",
            UserName = $"{conversation.User.FirstName} {conversation.User.LastName}".Trim(),
            MessageText = conversation.LastMessageText != null ? _encryption.Decrypt(conversation.LastMessageText) ?? "" : "",
            PatientConversationId = conversationId,
            PortalCode = portalCode
        };
    }

    // ============================================
    // ALL PATIENTS LIST (WhatsApp-style)
    // ============================================

    public async Task<PatientMessagingListResult> GetAllPatientsForMessagingAsync(int tenantId, int userId, int? locationId, string? search, int skip, int take)
    {
        // Get all patients for this tenant, filtered by location
        // AsNoTracking: read-only messaging list; Patient decrypted for DTO.
        var query = _context.Patients
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.IsDeleted != true);

        // Location filter: patient's preferred location OR any appointment at this location
        if (locationId.HasValue && locationId.Value > 0)
        {
            query = query.Where(p =>
                p.PreferredLocationId == locationId.Value
                || p.Appointments.Any(a => a.LocationId == locationId.Value));
        }

        // Count total before pagination
        var totalCount = await query.CountAsync();

        // Get patients with pagination
        var patients = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        // Decrypt all patient names
        foreach (var p in patients) _encryption.DecryptEntity(p);

        // Apply search filter AFTER decryption (names are encrypted)
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            patients = patients.Where(p =>
                (p.FirstName?.ToLower().Contains(s) == true) ||
                (p.LastName?.ToLower().Contains(s) == true) ||
                (p.Mrn?.ToLower().Contains(s) == true)
            ).ToList();
        }

        // Get THIS USER's conversations with these patients (1-to-1 model)
        var patientIds = patients.Select(p => p.PatientId).ToList();
        var conversations = await _context.PatientConversations
            .Where(c => c.TenantId == tenantId && c.UserId == userId && patientIds.Contains(c.PatientId) && c.IsActive)
            .ToListAsync();

        // Get location names for display
        var locationIds = patients
            .Where(p => p.PreferredLocationId.HasValue)
            .Select(p => p.PreferredLocationId!.Value)
            .Distinct().ToList();
        var locationNames = locationIds.Any()
            ? await _context.Locations
                .Where(l => locationIds.Contains(l.LocationId))
                .ToDictionaryAsync(l => l.LocationId, l => l.Name ?? "")
            : new Dictionary<int, string>();

        // Get patient document counts (patient-uploaded only)
        var docCounts = await _context.PatientDocuments
            .Where(d => patientIds.Contains(d.PatientId) && d.IsPatientUploaded == true && d.IsDeleted != true)
            .GroupBy(d => d.PatientId)
            .Select(g => new { PatientId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PatientId, x => x.Count);

        // One conversation per patient per user (1-to-1 model)
        var convByPatient = conversations.ToDictionary(c => c.PatientId);

        var items = patients.Select(p =>
        {
            var conv = convByPatient.GetValueOrDefault(p.PatientId);
            return new PatientMessagingListItem
            {
                PatientId = p.PatientId,
                PatientName = $"{p.FirstName} {p.LastName}".Trim(),
                Mrn = p.Mrn,
                DateOfBirth = p.DateOfBirth.ToString("M/d/yyyy"),
                LocationName = p.PreferredLocationId.HasValue ? locationNames.GetValueOrDefault(p.PreferredLocationId.Value, "") : null,
                ConversationId = conv?.PatientConversationId,
                LastMessageText = conv?.LastMessageText != null ? SafeDecrypt(conv.LastMessageText) : null,
                LastMessageAt = conv?.LastMessageAt,
                LastMessageSenderType = conv?.LastMessageSenderType,
                UnreadCount = conv?.UserUnreadCount ?? 0,
                PatientDocumentCount = docCounts.GetValueOrDefault(p.PatientId, 0)
            };
        })
        // Sort: patients with unread messages first, then by last message time, then by name
        .OrderByDescending(x => x.UnreadCount > 0 ? 1 : 0)
        .ThenByDescending(x => x.LastMessageAt ?? DateTime.MinValue)
        .ThenBy(x => x.PatientName)
        .ToList();

        return new PatientMessagingListResult
        {
            Items = items,
            TotalCount = totalCount,
            HasMore = skip + take < totalCount
        };
    }

    private string? SafeDecrypt(string? val)
    {
        if (string.IsNullOrEmpty(val)) return null;
        try { return _encryption.Decrypt(val); } catch { return val; }
    }

    // ============================================
    // SYSTEM MESSAGES (document upload notifications)
    // ============================================

    public async Task<List<SystemMessageResult>> CreateSystemMessageAsync(int patientId, int tenantId, string messageText)
    {
        var results = new List<SystemMessageResult>();

        // Get ALL active users in the tenant
        var allUsers = await _context.Users
            .Where(u => u.TenantId == tenantId && u.IsActive == true)
            .Select(u => u.UserId)
            .ToListAsync();

        if (!allUsers.Any())
            return results;

        // Get existing conversations for this patient
        var existingConversations = await _context.PatientConversations
            .Where(c => c.TenantId == tenantId && c.PatientId == patientId && c.IsActive)
            .ToDictionaryAsync(c => c.UserId, c => c);

        // Encrypt once, reuse for all messages
        var encryptedText = _encryption.Encrypt(messageText);
        var previewText = messageText.Length > 500 ? messageText[..500] : messageText;
        var encryptedPreview = _encryption.Encrypt(previewText);
        var now = DateTime.UtcNow;

        foreach (var userId in allUsers)
        {
            // Get or create conversation for this user
            if (!existingConversations.TryGetValue(userId, out var conversation))
            {
                conversation = new PatientConversation
                {
                    TenantId = tenantId,
                    PatientId = patientId,
                    UserId = userId,
                    IsActive = true,
                    CreatedAt = now
                };
                _context.PatientConversations.Add(conversation);
                await _context.SaveChangesAsync(); // Save to get the ID
            }

            // Create system message in this conversation
            var message = new PatientMessage
            {
                TenantId = tenantId,
                PatientConversationId = conversation.PatientConversationId,
                SenderType = "System",
                SenderPatientId = null,
                SenderUserId = null,
                MessageText = encryptedText,
                IsReadByPatient = true, // Patient doesn't need to see system messages
                IsReadByUser = false,
                CreatedAt = now
            };
            _context.PatientMessages.Add(message);

            // Update conversation metadata
            conversation.LastMessageText = encryptedPreview;
            conversation.LastMessageAt = now;
            conversation.LastMessageSenderType = "System";
            conversation.UserUnreadCount++;
            conversation.UpdatedAt = now;

            await _context.SaveChangesAsync();

            results.Add(new SystemMessageResult
            {
                UserId = userId,
                PatientConversationId = conversation.PatientConversationId,
                PatientMessageId = message.PatientMessageId,
                CreatedAt = now
            });
        }

        _logger.LogInformation("System message created for patient {PatientId} — notified {UserCount} users", patientId, results.Count);
        return results;
    }

    // ============================================
    // Private helpers
    // ============================================

    private async Task<PatientMessageSentDto> SendMessageAsync(int conversationId, string senderType, int? senderPatientId, int? senderUserId, string messageText)
    {
        var conversation = await _context.PatientConversations
            .FirstOrDefaultAsync(c => c.PatientConversationId == conversationId && c.TenantId == TenantId && c.IsActive);

        if (conversation == null)
            return new PatientMessageSentDto { Success = false, Error = "Conversation not found" };

        // Verify sender belongs to this conversation
        if (senderType == "Patient" && conversation.PatientId != senderPatientId)
            return new PatientMessageSentDto { Success = false, Error = "Access denied" };
        if (senderType == "Provider" && conversation.UserId != senderUserId)
            return new PatientMessageSentDto { Success = false, Error = "Access denied" };

        var encryptedText = _encryption.Encrypt(messageText);
        var previewText = messageText.Length > 500 ? messageText[..500] : messageText;
        var encryptedPreview = _encryption.Encrypt(previewText);

        var message = new PatientMessage
        {
            TenantId = TenantId,
            PatientConversationId = conversationId,
            SenderType = senderType,
            SenderPatientId = senderPatientId,
            SenderUserId = senderUserId,
            MessageText = encryptedText,
            IsReadByPatient = senderType == "Patient", // Sender has read their own message
            IsReadByUser = senderType == "Provider",
            ReadByPatientAt = senderType == "Patient" ? DateTime.UtcNow : null,
            ReadByUserAt = senderType == "Provider" ? DateTime.UtcNow : null,
            CreatedAt = DateTime.UtcNow
        };

        _context.PatientMessages.Add(message);

        // Update conversation metadata
        conversation.LastMessageText = encryptedPreview;
        conversation.LastMessageAt = message.CreatedAt;
        conversation.LastMessageSenderType = senderType;
        conversation.UpdatedAt = DateTime.UtcNow;

        if (senderType == "Patient")
            conversation.UserUnreadCount++;
        else
            conversation.PatientUnreadCount++;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Patient message sent: ConversationId={ConversationId}, SenderType={SenderType}, MessageId={MessageId}",
            conversationId, senderType, message.PatientMessageId);

        return new PatientMessageSentDto
        {
            Success = true,
            PatientMessageId = message.PatientMessageId,
            PatientConversationId = conversationId,
            CreatedAt = message.CreatedAt
        };
    }

    public async Task<List<PatientConversationListDto>> GetAllConversationsForPatientAsync(int patientId)
    {
        var conversations = await _context.PatientConversations
            .AsNoTracking()
            .Include(c => c.User)
            .ThenInclude(u => u.Provider)
            .Where(c => c.PatientId == patientId && c.TenantId == TenantId && c.IsActive)
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .ToListAsync();

        return conversations.Select(c => MapConversationDto(c, "Staff")).ToList();
    }

    public async Task<List<PatientMessageDto>> GetMessagesForProfileAsync(int conversationId, int patientId)
    {
        var conversation = await _context.PatientConversations
            .FirstOrDefaultAsync(c => c.PatientConversationId == conversationId
                && c.PatientId == patientId
                && c.TenantId == TenantId);

        if (conversation == null) return new List<PatientMessageDto>();

        var messages = await _context.PatientMessages
            .AsNoTracking()
            .Where(m => m.PatientConversationId == conversationId && m.TenantId == TenantId
                && m.SenderType != "System") // System = doc-upload notifications; raw [DOC_UPLOAD] strings, not human messages
            .OrderBy(m => m.CreatedAt)
            .Include(m => m.SenderPatient)
            .Include(m => m.SenderUser)
            .ToListAsync();

        return messages.Select(m =>
        {
            string senderName;
            if (m.SenderType == "Patient" && m.SenderPatient != null)
            {
                _encryption.DecryptEntity(m.SenderPatient);
                senderName = $"{m.SenderPatient.FirstName} {m.SenderPatient.LastName}".Trim();
            }
            else if (m.SenderType == "Provider" && m.SenderUser != null)
                senderName = $"{m.SenderUser.FirstName} {m.SenderUser.LastName}".Trim();
            else
                senderName = m.SenderType == "System" ? "System" : "Unknown";

            return new PatientMessageDto
            {
                PatientMessageId = m.PatientMessageId,
                PatientConversationId = m.PatientConversationId,
                SenderType = m.SenderType,
                SenderName = senderName,
                MessageText = _encryption.Decrypt(m.MessageText) ?? m.MessageText,
                IsReadByPatient = m.IsReadByPatient,
                IsReadByUser = m.IsReadByUser,
                CreatedAt = m.CreatedAt
            };
        }).ToList();
    }

    private PatientConversationListDto MapConversationDto(PatientConversation c, string perspective)
    {
        return new PatientConversationListDto
        {
            PatientConversationId = c.PatientConversationId,
            PatientId = c.PatientId,
            PatientName = c.Patient != null ? $"{c.Patient.FirstName} {c.Patient.LastName}".Trim() : "",
            UserId = c.UserId,
            UserName = c.User != null ? $"{c.User.FirstName} {c.User.LastName}".Trim() : "",
            UserRole = c.User?.Role,
            UserRoleLabel = GetRoleLabel(c.User?.Role),
            UserSpecialty = c.User?.Provider?.Specialty,
            UserProfilePicture = c.User?.Provider?.ProfilePicturePath,
            LastMessageText = c.LastMessageText != null ? _encryption.Decrypt(c.LastMessageText) : null,
            LastMessageAt = c.LastMessageAt,
            LastMessageSenderType = c.LastMessageSenderType,
            UnreadCount = perspective == "Patient" ? c.PatientUnreadCount : c.UserUnreadCount
        };
    }

    private static string GetRoleLabel(int? role)
    {
        return role switch
        {
            0 => "Admin",
            1 => "Clinic Admin",
            2 => "Provider",
            3 => "Front Desk",
            4 => "Biller",
            6 => "Medical Assistant",
            7 => "Nurse",
            _ => ""
        };
    }
}
