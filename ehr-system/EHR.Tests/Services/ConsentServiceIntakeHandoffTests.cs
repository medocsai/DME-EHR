using EHR.Helpers;
using EHR.Hubs;
using EHR.Models;
using EHR.Models.Generated;
using EHR.Services;
using EHR.Services.Intake;
using EHR.Services.Intake.Dtos;
using EHR.Tests.TestHelpers;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EHR.Tests.Services;

/// <summary>
/// Pure unit tests for ConsentService.BuildIntakeHandoffAsync. Mocks
/// IIntakeProgressCalculator and exercises the handoff branching logic
/// described in rules/technical/consent-to-intake-handoff.md.
///
/// Covers test IDs A1–A7 (intake state detection / handoff payload shape).
/// </summary>
public class ConsentServiceIntakeHandoffTests
{
    /// <summary>Patient token store keyed by patientId — mocks the real
    /// GetOrCreateTokenAsync semantics (return existing if set, otherwise issue
    /// a new one and persist it for future calls).</summary>
    private readonly Dictionary<int, Guid> _tokenStore = new();

    private ConsentService BuildSut(
        IIntakeProgressCalculator progress,
        IIntakeAccessTokenService tokens = null)
    {
        // Constructor-required dependencies that BuildIntakeHandoffAsync does NOT use.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Encryption:Key"] = "test-key-must-be-long-enough-for-pbkdf2-derivation"
            })
            .Build();

        return new ConsentService(
            context: InMemoryDbFactory.Create(),
            encryption: new EncryptionHelper(config),
            templateService: Mock.Of<IConsentTemplateService>(),
            kioskService: Mock.Of<IKioskService>(),
            auditService: Mock.Of<IAuditService>(),
            notificationService: Mock.Of<IConsentNotificationService>(),
            htmlToPdfService: Mock.Of<IHtmlToPdfService>(),
            intakeProgress: progress,
            intakeTokens: tokens ?? DefaultTokens(),
            scopeFactory: Mock.Of<IServiceScopeFactory>(),
            configuration: config,
            logger: NullLogger<ConsentService>.Instance);
    }

    /// <summary>
    /// Default token mock that mirrors the real IntakeAccessTokenService:
    /// returns the same token for repeated calls on the same patient id, and
    /// auto-generates a fresh one on first call. Lets tests pre-seed
    /// _tokenStore[patientId] when they want a specific token in the response.
    /// </summary>
    private IIntakeAccessTokenService DefaultTokens()
    {
        var m = new Mock<IIntakeAccessTokenService>();
        m.Setup(x => x.GetOrCreateTokenAsync(It.IsAny<int>()))
            .ReturnsAsync((int pid) =>
            {
                if (!_tokenStore.TryGetValue(pid, out var t))
                {
                    t = Guid.NewGuid();
                    _tokenStore[pid] = t;
                }
                return t;
            });
        return m.Object;
    }

    private static Patient PatientWithToken(Guid? token, int id = 100) => new()
    {
        PatientId = id,
        TenantId = 1,
        IntakePortalToken = token,
    };

    private static IIntakeProgressCalculator MockProgress(IntakeProgressDto dto)
    {
        var mock = new Mock<IIntakeProgressCalculator>();
        mock.Setup(m => m.CalculateAsync(It.IsAny<int>())).ReturnsAsync(dto);
        return mock.Object;
    }

    [Fact] // A1
    public async Task IntakeAlreadySubmitted_ReturnsNoHandoff()
    {
        var sut = BuildSut(MockProgress(new IntakeProgressDto
        {
            Completed = 8, Total = 8, SubmittedAt = DateTime.UtcNow
        }));

        var (handoff, token) = await sut.BuildIntakeHandoffAsync(PatientWithToken(Guid.NewGuid()));

        handoff.Should().BeNull();
        token.Should().BeNull();
    }

    [Fact] // A2
    public async Task NotStarted_ReturnsNotStartedHandoff()
    {
        var intakeToken = Guid.NewGuid();
        _tokenStore[100] = intakeToken; // patient already has this token
        var sut = BuildSut(MockProgress(new IntakeProgressDto
        {
            Completed = 0, Total = 8, SubmittedAt = null
        }));

        var (handoff, token) = await sut.BuildIntakeHandoffAsync(PatientWithToken(intakeToken, id: 100));

        handoff.Should().NotBeNull();
        handoff!.State.Should().Be("not_started");
        handoff.Completed.Should().Be(0);
        handoff.Total.Should().Be(8);
        handoff.Url.Should().Contain(intakeToken.ToString());
        handoff.Url.Should().Contain("?return=kiosk");
        token.Should().Be(intakeToken);
    }

    [Fact] // A3
    public async Task Partial3of8_ReturnsPartialHandoff()
    {
        var sut = BuildSut(MockProgress(new IntakeProgressDto
        {
            Completed = 3, Total = 8, SubmittedAt = null
        }));

        var (handoff, _) = await sut.BuildIntakeHandoffAsync(PatientWithToken(Guid.NewGuid()));

        handoff.Should().NotBeNull();
        handoff!.State.Should().Be("partial");
        handoff.Completed.Should().Be(3);
        handoff.Total.Should().Be(8);
    }

    [Fact] // A4
    public async Task PartialAllSectionsButNotSubmitted_ReturnsPartial()
    {
        var sut = BuildSut(MockProgress(new IntakeProgressDto
        {
            Completed = 7, Total = 8, SubmittedAt = null
        }));

        var (handoff, _) = await sut.BuildIntakeHandoffAsync(PatientWithToken(Guid.NewGuid()));

        handoff.Should().NotBeNull();
        handoff!.State.Should().Be("partial");
        handoff.Completed.Should().Be(7);
    }

    [Fact] // A5 (revised) — patient has no IntakePortalToken: token is auto-generated
    //                       and the handoff still applies. Without this, the feature
    //                       would dead-end for any new patient whose intake QR was
    //                       never opened by staff.
    public async Task NoIntakeToken_AutoGeneratesAndReturnsHandoff()
    {
        var generatedToken = Guid.Parse("abcdef00-1111-2222-3333-444444444444");
        var tokens = new Mock<IIntakeAccessTokenService>();
        tokens.Setup(x => x.GetOrCreateTokenAsync(It.IsAny<int>()))
            .ReturnsAsync(generatedToken);

        var sut = BuildSut(
            MockProgress(new IntakeProgressDto { Completed = 0, Total = 8, SubmittedAt = null }),
            tokens.Object);

        var (handoff, token) = await sut.BuildIntakeHandoffAsync(PatientWithToken(token: null, id: 1620));

        handoff.Should().NotBeNull();
        handoff!.State.Should().Be("not_started");
        token.Should().Be(generatedToken);
        handoff.Url.Should().Contain(generatedToken.ToString());
        tokens.Verify(x => x.GetOrCreateTokenAsync(1620), Times.Once);
    }

    [Fact] // A5b — token-generator failure falls back gracefully (no handoff, no crash)
    public async Task TokenGeneratorThrows_ReturnsNoHandoff()
    {
        var tokens = new Mock<IIntakeAccessTokenService>();
        tokens.Setup(x => x.GetOrCreateTokenAsync(It.IsAny<int>()))
            .ThrowsAsync(new InvalidOperationException("DB blew up"));

        var sut = BuildSut(
            MockProgress(new IntakeProgressDto { Completed = 0, Total = 8, SubmittedAt = null }),
            tokens.Object);

        var (handoff, token) = await sut.BuildIntakeHandoffAsync(PatientWithToken(token: null));

        handoff.Should().BeNull();
        token.Should().BeNull();
    }

    [Fact] // A6 — null patient (defensive: should not throw)
    public async Task NullPatient_ReturnsNoHandoff()
    {
        var sut = BuildSut(MockProgress(new IntakeProgressDto()));

        var (handoff, token) = await sut.BuildIntakeHandoffAsync(patient: null);

        handoff.Should().BeNull();
        token.Should().BeNull();
    }

    [Fact] // A7 — calculator throws: handoff must degrade gracefully (no crash).
    public async Task ProgressCalculatorThrows_ReturnsNoHandoff()
    {
        var progressMock = new Mock<IIntakeProgressCalculator>();
        progressMock.Setup(m => m.CalculateAsync(It.IsAny<int>()))
            .ThrowsAsync(new InvalidOperationException("simulated failure"));
        var sut = BuildSut(progressMock.Object);

        var (handoff, token) = await sut.BuildIntakeHandoffAsync(PatientWithToken(Guid.NewGuid()));

        handoff.Should().BeNull();
        token.Should().BeNull();
    }

    [Fact] // Url format sanity (no location kiosk token)
    public async Task HandoffUrl_UsesGuidDFormat_NoLocationKioskToken()
    {
        var intakeToken = Guid.Parse("39f1689c-18a7-4e82-a574-aaaaaaaaaaaa");
        _tokenStore[100] = intakeToken;
        var sut = BuildSut(MockProgress(new IntakeProgressDto
        {
            Completed = 0, Total = 8, SubmittedAt = null
        }));

        var (handoff, _) = await sut.BuildIntakeHandoffAsync(PatientWithToken(intakeToken, id: 100));

        handoff!.Url.Should().Be($"/intake/p/{intakeToken:D}/wizard?return=kiosk");
    }

    [Fact] // Url format with &kt= so the wizard can return to the same /Kiosk URL
    public async Task HandoffUrl_AppendsLocationKioskToken_WhenProvided()
    {
        var intakeToken = Guid.Parse("39f1689c-18a7-4e82-a574-aaaaaaaaaaaa");
        var locKioskToken = "8752893042427534d00f00fb2a9429d02b2065890cb94bced9c114860faedef2";
        _tokenStore[100] = intakeToken;
        var sut = BuildSut(MockProgress(new IntakeProgressDto
        {
            Completed = 0, Total = 8, SubmittedAt = null
        }));

        var (handoff, _) = await sut.BuildIntakeHandoffAsync(PatientWithToken(intakeToken, id: 100), locKioskToken);

        handoff!.Url.Should().Be($"/intake/p/{intakeToken:D}/wizard?return=kiosk&kt={locKioskToken}");
    }

    [Fact] // Defensive: empty / null location kiosk token must not produce a stray &kt=
    public async Task HandoffUrl_OmitsKtWhenLocationKioskTokenIsEmpty()
    {
        var intakeToken = Guid.Parse("39f1689c-18a7-4e82-a574-aaaaaaaaaaaa");
        _tokenStore[100] = intakeToken;
        var sut = BuildSut(MockProgress(new IntakeProgressDto
        {
            Completed = 0, Total = 8, SubmittedAt = null
        }));

        var (handoff, _) = await sut.BuildIntakeHandoffAsync(PatientWithToken(intakeToken, id: 100), locationKioskToken: "");

        handoff!.Url.Should().NotContain("&kt=");
    }
}
