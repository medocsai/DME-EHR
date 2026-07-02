using EHR.Models.Generated;
using EHR.Services;
using EHR.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EHR.Tests.Services;

/// <summary>
/// Unit tests for the two new Patient Profile tab methods added to PatientMessagingService:
///   - GetAllConversationsForPatientAsync  (any staff can view all providers for a patient)
///   - GetMessagesForProfileAsync           (any staff can view a thread without owning it)
///
/// Covers tenant isolation, decryption, ordering, sender-name resolution, and edge cases.
/// Uses InMemory EF + real EncryptionHelper (same pattern as existing service tests).
/// </summary>
public class PatientMessagingServiceProfileTests
{
    private const int TenantId = 1;
    private const int OtherTenantId = 99;
    private const int PatientId = 10;
    private const int UserId1 = 101;
    private const int UserId2 = 102;

    // ── factory ──────────────────────────────────────────────────────────────

    private PatientMessagingService BuildSut(EhrDbContext db, int tenantId = TenantId)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(t => t.TenantId).Returns(tenantId);

        return new PatientMessagingService(
            db,
            tenantProvider.Object,
            TestEncryptionHelper.Create(),
            NullLogger<PatientMessagingService>.Instance);
    }

    private static EhrDbContext SeedBaseData(int tenantId = TenantId)
    {
        var db = InMemoryDbFactory.Create();

        db.Patients.Add(new Patient
        {
            PatientId = PatientId,
            TenantId = tenantId,
            Mrn = "MRN-10",
            FirstName = "Jane",
            LastName = "Doe",
            Gender = "F",
            DateOfBirth = new DateOnly(1985, 1, 1)
        });

        db.Users.Add(new User
        {
            UserId = UserId1,
            TenantId = tenantId,
            FirstName = "Alice",
            LastName = "Smith",
            Role = 2, // Clinician
            Email = "alice@test.com",
            PasswordHash = "hash",
            Phone = "",
            RefreshToken = "",
            PasswordResetToken = "",
            OtpCode = ""
        });

        db.Users.Add(new User
        {
            UserId = UserId2,
            TenantId = tenantId,
            FirstName = "Bob",
            LastName = "Jones",
            Role = 7, // Nurse
            Email = "bob@test.com",
            PasswordHash = "hash",
            Phone = "",
            RefreshToken = "",
            PasswordResetToken = "",
            OtpCode = ""
        });

        db.SaveChanges();
        return db;
    }

    // ── GetAllConversationsForPatientAsync ────────────────────────────────────

    [Fact]
    public async Task GetAllConversations_Returns_All_Active_Conversations_For_Patient()
    {
        var db = SeedBaseData();
        db.PatientConversations.AddRange(
            new PatientConversation { PatientConversationId = 1, TenantId = TenantId, PatientId = PatientId, UserId = UserId1, IsActive = true, CreatedAt = DateTime.UtcNow.AddDays(-2) },
            new PatientConversation { PatientConversationId = 2, TenantId = TenantId, PatientId = PatientId, UserId = UserId2, IsActive = true, CreatedAt = DateTime.UtcNow.AddDays(-1) }
        );
        db.SaveChanges();

        var sut = BuildSut(db);
        var result = await sut.GetAllConversationsForPatientAsync(PatientId);

        result.Should().HaveCount(2);
        result.Select(c => c.UserId).Should().BeEquivalentTo(new[] { UserId1, UserId2 });
    }

    [Fact]
    public async Task GetAllConversations_Excludes_Inactive_Conversations()
    {
        var db = SeedBaseData();
        db.PatientConversations.AddRange(
            new PatientConversation { PatientConversationId = 1, TenantId = TenantId, PatientId = PatientId, UserId = UserId1, IsActive = true,  CreatedAt = DateTime.UtcNow },
            new PatientConversation { PatientConversationId = 2, TenantId = TenantId, PatientId = PatientId, UserId = UserId2, IsActive = false, CreatedAt = DateTime.UtcNow }
        );
        db.SaveChanges();

        var sut = BuildSut(db);
        var result = await sut.GetAllConversationsForPatientAsync(PatientId);

        result.Should().HaveCount(1);
        result.Single().UserId.Should().Be(UserId1);
    }

    [Fact]
    public async Task GetAllConversations_Excludes_Other_Tenant_Conversations()
    {
        // Tenant 1 patient + conversation
        var db = SeedBaseData();
        db.PatientConversations.Add(
            new PatientConversation { PatientConversationId = 1, TenantId = TenantId, PatientId = PatientId, UserId = UserId1, IsActive = true, CreatedAt = DateTime.UtcNow }
        );
        // Tenant 99 conversation for same patient ID (cross-tenant attack)
        db.PatientConversations.Add(
            new PatientConversation { PatientConversationId = 2, TenantId = OtherTenantId, PatientId = PatientId, UserId = UserId2, IsActive = true, CreatedAt = DateTime.UtcNow }
        );
        db.SaveChanges();

        var sut = BuildSut(db, tenantId: TenantId); // service sees TenantId=1 only
        var result = await sut.GetAllConversationsForPatientAsync(PatientId);

        result.Should().HaveCount(1);
        result.Single().PatientConversationId.Should().Be(1);
    }

    [Fact]
    public async Task GetAllConversations_Returns_Empty_When_No_Conversations()
    {
        var db = SeedBaseData();
        var sut = BuildSut(db);

        var result = await sut.GetAllConversationsForPatientAsync(PatientId);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllConversations_Orders_By_LastMessageAt_Descending()
    {
        var db = SeedBaseData();
        var older = DateTime.UtcNow.AddDays(-5);
        var newer = DateTime.UtcNow.AddDays(-1);

        db.PatientConversations.AddRange(
            new PatientConversation { PatientConversationId = 1, TenantId = TenantId, PatientId = PatientId, UserId = UserId1, IsActive = true, LastMessageAt = older, CreatedAt = older },
            new PatientConversation { PatientConversationId = 2, TenantId = TenantId, PatientId = PatientId, UserId = UserId2, IsActive = true, LastMessageAt = newer, CreatedAt = newer }
        );
        db.SaveChanges();

        var sut = BuildSut(db);
        var result = await sut.GetAllConversationsForPatientAsync(PatientId);

        result[0].PatientConversationId.Should().Be(2, "newer conversation should be first");
        result[1].PatientConversationId.Should().Be(1);
    }

    [Fact]
    public async Task GetAllConversations_Decrypts_LastMessageText()
    {
        var enc = TestEncryptionHelper.Create();
        var plaintext = "Hello, this is a test message preview.";
        var encrypted = enc.Encrypt(plaintext);

        var db = SeedBaseData();
        db.PatientConversations.Add(new PatientConversation
        {
            PatientConversationId = 1, TenantId = TenantId, PatientId = PatientId,
            UserId = UserId1, IsActive = true, CreatedAt = DateTime.UtcNow,
            LastMessageText = encrypted
        });
        db.SaveChanges();

        var sut = BuildSut(db);
        var result = await sut.GetAllConversationsForPatientAsync(PatientId);

        result.Single().LastMessageText.Should().Be(plaintext);
    }

    [Fact]
    public async Task GetAllConversations_Includes_Provider_Name_And_RoleLabel()
    {
        var db = SeedBaseData();
        db.PatientConversations.Add(new PatientConversation
        {
            PatientConversationId = 1, TenantId = TenantId, PatientId = PatientId,
            UserId = UserId1, IsActive = true, CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        var sut = BuildSut(db);
        var result = await sut.GetAllConversationsForPatientAsync(PatientId);

        var dto = result.Single();
        dto.UserName.Should().Be("Alice Smith");
        dto.UserRoleLabel.Should().Be("Provider"); // Role=2 maps to "Provider"
        dto.UserId.Should().Be(UserId1);
    }

    // ── GetMessagesForProfileAsync ────────────────────────────────────────────

    [Fact]
    public async Task GetMessages_Returns_Messages_In_Chronological_Order()
    {
        var enc = TestEncryptionHelper.Create();
        var db = SeedBaseData();

        db.PatientConversations.Add(new PatientConversation
        {
            PatientConversationId = 1, TenantId = TenantId, PatientId = PatientId,
            UserId = UserId1, IsActive = true, CreatedAt = DateTime.UtcNow
        });

        var t1 = DateTime.UtcNow.AddMinutes(-10);
        var t2 = DateTime.UtcNow.AddMinutes(-5);
        var t3 = DateTime.UtcNow;

        db.PatientMessages.AddRange(
            new PatientMessage { PatientMessageId = 1, TenantId = TenantId, PatientConversationId = 1, SenderType = "Provider", SenderUserId = UserId1, MessageText = enc.Encrypt("First"),  CreatedAt = t1 },
            new PatientMessage { PatientMessageId = 2, TenantId = TenantId, PatientConversationId = 1, SenderType = "Patient",  SenderPatientId = PatientId, MessageText = enc.Encrypt("Second"), CreatedAt = t2 },
            new PatientMessage { PatientMessageId = 3, TenantId = TenantId, PatientConversationId = 1, SenderType = "Provider", SenderUserId = UserId1, MessageText = enc.Encrypt("Third"),  CreatedAt = t3 }
        );
        db.SaveChanges();

        var sut = BuildSut(db);
        var result = await sut.GetMessagesForProfileAsync(1, PatientId);

        result.Should().HaveCount(3);
        result[0].MessageText.Should().Be("First");
        result[1].MessageText.Should().Be("Second");
        result[2].MessageText.Should().Be("Third");
    }

    [Fact]
    public async Task GetMessages_Decrypts_MessageText()
    {
        var enc = TestEncryptionHelper.Create();
        var plaintext = "Your appointment is confirmed.";
        var db = SeedBaseData();

        db.PatientConversations.Add(new PatientConversation
        {
            PatientConversationId = 1, TenantId = TenantId, PatientId = PatientId,
            UserId = UserId1, IsActive = true, CreatedAt = DateTime.UtcNow
        });
        db.PatientMessages.Add(new PatientMessage
        {
            PatientMessageId = 1, TenantId = TenantId, PatientConversationId = 1,
            SenderType = "Provider", SenderUserId = UserId1,
            MessageText = enc.Encrypt(plaintext), CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        var sut = BuildSut(db);
        var result = await sut.GetMessagesForProfileAsync(1, PatientId);

        result.Single().MessageText.Should().Be(plaintext);
    }

    [Fact]
    public async Task GetMessages_Sets_SenderName_For_Provider_Messages()
    {
        var enc = TestEncryptionHelper.Create();
        var db = SeedBaseData();

        db.PatientConversations.Add(new PatientConversation
        {
            PatientConversationId = 1, TenantId = TenantId, PatientId = PatientId,
            UserId = UserId1, IsActive = true, CreatedAt = DateTime.UtcNow
        });
        db.PatientMessages.Add(new PatientMessage
        {
            PatientMessageId = 1, TenantId = TenantId, PatientConversationId = 1,
            SenderType = "Provider", SenderUserId = UserId1,
            MessageText = enc.Encrypt("Hi there"), CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        var sut = BuildSut(db);
        var result = await sut.GetMessagesForProfileAsync(1, PatientId);

        result.Single().SenderName.Should().Be("Alice Smith");
        result.Single().SenderType.Should().Be("Provider");
    }

    [Fact]
    public async Task GetMessages_Sets_SenderName_For_Patient_Messages()
    {
        var enc = TestEncryptionHelper.Create();
        var db = SeedBaseData();

        db.PatientConversations.Add(new PatientConversation
        {
            PatientConversationId = 1, TenantId = TenantId, PatientId = PatientId,
            UserId = UserId1, IsActive = true, CreatedAt = DateTime.UtcNow
        });
        db.PatientMessages.Add(new PatientMessage
        {
            PatientMessageId = 1, TenantId = TenantId, PatientConversationId = 1,
            SenderType = "Patient", SenderPatientId = PatientId,
            MessageText = enc.Encrypt("Thank you!"), CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        var sut = BuildSut(db);
        var result = await sut.GetMessagesForProfileAsync(1, PatientId);

        // Patient entity "Jane Doe" was seeded in SeedBaseData
        result.Single().SenderName.Should().Be("Jane Doe");
        result.Single().SenderType.Should().Be("Patient");
    }

    [Fact]
    public async Task GetMessages_Returns_Empty_For_Wrong_Patient()
    {
        var enc = TestEncryptionHelper.Create();
        var db = SeedBaseData();

        // Conversation belongs to PatientId=10, but we query with PatientId=999
        db.PatientConversations.Add(new PatientConversation
        {
            PatientConversationId = 1, TenantId = TenantId, PatientId = PatientId,
            UserId = UserId1, IsActive = true, CreatedAt = DateTime.UtcNow
        });
        db.PatientMessages.Add(new PatientMessage
        {
            PatientMessageId = 1, TenantId = TenantId, PatientConversationId = 1,
            SenderType = "Provider", SenderUserId = UserId1,
            MessageText = enc.Encrypt("Secret"), CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        var sut = BuildSut(db);
        var result = await sut.GetMessagesForProfileAsync(conversationId: 1, patientId: 999);

        result.Should().BeEmpty("conversation belongs to a different patient");
    }

    [Fact]
    public async Task GetMessages_Returns_Empty_For_Wrong_Tenant()
    {
        var enc = TestEncryptionHelper.Create();
        var db = SeedBaseData();

        db.PatientConversations.Add(new PatientConversation
        {
            PatientConversationId = 1, TenantId = OtherTenantId, PatientId = PatientId,
            UserId = UserId1, IsActive = true, CreatedAt = DateTime.UtcNow
        });
        db.PatientMessages.Add(new PatientMessage
        {
            PatientMessageId = 1, TenantId = OtherTenantId, PatientConversationId = 1,
            SenderType = "Provider", SenderUserId = UserId1,
            MessageText = enc.Encrypt("Cross-tenant data"), CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        // Service sees TenantId=1, conversation is tenant 99
        var sut = BuildSut(db, tenantId: TenantId);
        var result = await sut.GetMessagesForProfileAsync(conversationId: 1, patientId: PatientId);

        result.Should().BeEmpty("conversation belongs to a different tenant");
    }

    [Fact]
    public async Task GetMessages_Excludes_System_Messages()
    {
        // System messages contain raw [DOC_UPLOAD] strings — only the Inbox module can render them.
        // The profile tab must never show them.
        var enc = TestEncryptionHelper.Create();
        var db = SeedBaseData();

        db.PatientConversations.Add(new PatientConversation
        {
            PatientConversationId = 1, TenantId = TenantId, PatientId = PatientId,
            UserId = UserId1, IsActive = true, CreatedAt = DateTime.UtcNow
        });
        db.PatientMessages.AddRange(
            new PatientMessage { PatientMessageId = 1, TenantId = TenantId, PatientConversationId = 1, SenderType = "Provider", SenderUserId = UserId1, MessageText = enc.Encrypt("Hello"),                      CreatedAt = DateTime.UtcNow.AddMinutes(-2) },
            new PatientMessage { PatientMessageId = 2, TenantId = TenantId, PatientConversationId = 1, SenderType = "System",   MessageText = enc.Encrypt("[DOC_UPLOAD]file.pdf|123|1|/api/..."), CreatedAt = DateTime.UtcNow.AddMinutes(-1) },
            new PatientMessage { PatientMessageId = 3, TenantId = TenantId, PatientConversationId = 1, SenderType = "Patient",  SenderPatientId = PatientId, MessageText = enc.Encrypt("Thanks"),   CreatedAt = DateTime.UtcNow }
        );
        db.SaveChanges();

        var sut = BuildSut(db);
        var result = await sut.GetMessagesForProfileAsync(1, PatientId);

        result.Should().HaveCount(2, "System message must be excluded");
        result.Should().NotContain(m => m.SenderType == "System");
        result[0].MessageText.Should().Be("Hello");
        result[1].MessageText.Should().Be("Thanks");
    }

    [Fact]
    public async Task GetMessages_Returns_Empty_When_Conversation_Has_No_Messages()
    {
        var db = SeedBaseData();
        db.PatientConversations.Add(new PatientConversation
        {
            PatientConversationId = 1, TenantId = TenantId, PatientId = PatientId,
            UserId = UserId1, IsActive = true, CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        var sut = BuildSut(db);
        var result = await sut.GetMessagesForProfileAsync(1, PatientId);

        result.Should().BeEmpty();
    }
}
