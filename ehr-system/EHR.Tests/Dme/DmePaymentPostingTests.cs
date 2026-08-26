using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EHR.Helpers;
using EHR.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace EHR.Tests.Dme;

/// <summary>
/// Posting money is the one DME operation where being wrong is expensive and
/// silent.
///
/// WHY THIS EXISTS
/// A wrong delivery date shows up on a screen and somebody complains. A payment
/// posted against the wrong claim, applied for more than the check was worth, or
/// half written because the connection dropped between two INSERTs, produces
/// books that look completely normal and are short by an amount nobody can
/// find. None of those failures raise an error at the time.
///
/// So the rules are pinned here rather than trusted to the posting screen. The
/// screen enforces the same things for a better message, but the screen is
/// JavaScript: a replayed form, a second client, or the 835/ERA parser that
/// will exist once there is a clearinghouse account all arrive at this service
/// directly.
/// </summary>
public class DmePaymentPostingTests
{
    private const int KnownClaimLine = 11;
    private const int ForeignClaimLine = 99;

    /// <summary>
    /// A DmeDb that knows about exactly one claim line and the real CARC codes,
    /// and records the SQL it was asked to run so the shape of the write can be
    /// asserted without a database.
    /// </summary>
    private sealed class FakeDb
    {
        public readonly Mock<IDmeDb> Mock = new();
        public readonly List<string> ExecutedSql = new();

        public FakeDb()
        {
            Mock.SetupGet(d => d.TenantId).Returns(1);
            Mock.Setup(d => d.NextNumber(It.IsAny<string>(), It.IsAny<int>())).Returns("PMT-01001");

            // Claim line existence check. Only KnownClaimLine comes back, which
            // is also how a cross-tenant line id behaves in production: RLS
            // filters it out of the SELECT and the post is refused.
            Mock.Setup(d => d.Query(It.Is<string>(s => s.Contains("FROM dbo.DmeClaimLines")), It.IsAny<object>()))
                .Returns(new List<Dictionary<string, object?>>
                {
                    new(StringComparer.OrdinalIgnoreCase) { ["ClaimLineId"] = KnownClaimLine }
                });

            Mock.Setup(d => d.Query(It.Is<string>(s => s.Contains("DmeCarcCodes")), It.IsAny<object>()))
                .Returns(new List<Dictionary<string, object?>>
                {
                    new(StringComparer.OrdinalIgnoreCase) { ["Code"] = "45" },
                    new(StringComparer.OrdinalIgnoreCase) { ["Code"] = "50" },
                    new(StringComparer.OrdinalIgnoreCase) { ["Code"] = "2" },
                });

            Mock.Setup(d => d.Scalar(It.IsAny<string>(), It.IsAny<object>()))
                .Callback<string, object?>((sql, _) => ExecutedSql.Add(sql))
                .Returns(7);
        }

        public string LastBatch => ExecutedSql.LastOrDefault() ?? "";
    }

    private static (DmePaymentService service, FakeDb db, Mock<IDmeAudit> audit) Build()
    {
        var db = new FakeDb();
        var audit = new Mock<IDmeAudit>();
        return (new DmePaymentService(db.Mock.Object, audit.Object), db, audit);
    }

    private static PaymentInput Receipt(
        decimal amount,
        decimal paid,
        DateTime? postedDate = null,
        int claimLineId = KnownClaimLine,
        params PaymentAdjustmentInput[] adjustments)
        => new(
            "payer", "Medicare (Railroad/Part B)", null,
            postedDate ?? DateTime.Today, "eft", "EFT-1", amount, null,
            new List<PaymentLineInput>
            {
                new(claimLineId, paid, paid, adjustments.ToList())
            });

    // ------------------------------------------------------------------ rules

    [Fact]
    public async Task PostedDateInTheFuture_IsRefused()
    {
        var (service, _, _) = Build();

        var result = await service.PostAsync(Receipt(100m, 100m, DateTime.Today.AddDays(1)));

        result.Ok.Should().BeFalse(
            "the posting date is what every monthly figure is keyed on, so a future date " +
            "puts money into a month that has not happened and quietly inflates it when it does");
        result.Error.Should().Contain("future");
    }

    [Fact]
    public async Task ApplyingMoreThanTheReceiptIsWorth_IsRefused()
    {
        var (service, _, _) = Build();

        var result = await service.PostAsync(Receipt(amount: 50m, paid: 120m));

        result.Ok.Should().BeFalse(
            "a $50 check cannot pay $120 of claim lines. Without this guard a mistyped " +
            "amount reports revenue that never arrived and the claim reads as settled");
    }

    [Fact]
    public async Task ApplyingLessThanTheReceiptIsWorth_IsAllowed()
    {
        var (service, _, _) = Build();

        var result = await service.PostAsync(Receipt(amount: 500m, paid: 120m));

        result.Ok.Should().BeTrue(
            "a payer check routinely covers several claims, so a partially applied receipt " +
            "is normal. The unapplied remainder is reported on the payments screen, not blocked");
    }

    [Fact]
    public async Task AClaimLineThatDoesNotComeBackFromTheDatabase_IsRefused()
    {
        var (service, _, _) = Build();

        var result = await service.PostAsync(Receipt(100m, 100m, claimLineId: ForeignClaimLine));

        result.Ok.Should().BeFalse(
            "this check is also the tenant guard: another supplier's claim line is filtered " +
            "out of the SELECT by row level security, so it looks exactly like a line that " +
            "does not exist and must be refused the same way");
    }

    [Fact]
    public async Task AnUnknownReasonCode_IsRefused()
    {
        var (service, _, _) = Build();

        var result = await service.PostAsync(Receipt(100m, 0m, null, KnownClaimLine,
            new PaymentAdjustmentInput("CO", "ZZZ", 100m)));

        result.Ok.Should().BeFalse(
            "the reason code IS the denial reporting. A free-text code would make the " +
            "most-frequent-denial figure a count of typos");
    }

    [Fact]
    public async Task AnInvalidGroupCode_IsRefused()
    {
        var (service, _, _) = Build();

        var result = await service.PostAsync(Receipt(100m, 0m, null, KnownClaimLine,
            new PaymentAdjustmentInput("XX", "50", 100m)));

        result.Ok.Should().BeFalse(
            "the group code is what separates a denial from a contractual write-off. " +
            "An unrecognised group would land in neither bucket and vanish from both figures");
    }

    [Fact]
    public async Task APostingWithNoLines_IsRefused()
    {
        var (service, _, _) = Build();

        var result = await service.PostAsync(new PaymentInput(
            "payer", "Aetna", null, DateTime.Today, "check", "1234", 100m, null,
            new List<PaymentLineInput>()));

        result.Ok.Should().BeFalse("a receipt that adjudicates nothing is a record of nothing");
    }

    /// <summary>
    /// The case most likely to be broken by a well meaning validation tidy-up.
    /// A denial arrives as a remittance worth zero dollars, and refusing it
    /// would mean the product could record payments but never denials, which is
    /// half of what this whole feature is for.
    /// </summary>
    [Fact]
    public async Task AZeroDollarDenialRemittance_IsAccepted()
    {
        var (service, _, _) = Build();

        var result = await service.PostAsync(Receipt(amount: 0m, paid: 0m, postedDate: null,
            claimLineId: KnownClaimLine,
            adjustments: new PaymentAdjustmentInput("CO", "50", 178m)));

        result.Ok.Should().BeTrue(
            "a payer denies by sending a remittance that pays nothing and explains why. " +
            "Rejecting a zero dollar receipt would make denials unrecordable");
    }

    // --------------------------------------------------------- write integrity

    [Fact]
    public async Task ThePostIsOneAtomicBatch()
    {
        var (service, db, _) = Build();

        await service.PostAsync(Receipt(100m, 100m, null, KnownClaimLine,
            new PaymentAdjustmentInput("PR", "2", 20m)));

        db.ExecutedSql.Should().HaveCount(1,
            "header, lines and adjustments written on separate connections would be separate " +
            "transactions, and a failure between them leaves a payment with only some of its " +
            "lines: books that are short by an amount nothing reports as missing");

        db.LastBatch.Should().Contain("XACT_ABORT ON");
        db.LastBatch.Should().Contain("BEGIN TRAN");
        db.LastBatch.Should().Contain("COMMIT");
        db.LastBatch.Should().Contain("INSERT INTO dbo.DmePayments");
        db.LastBatch.Should().Contain("INSERT INTO dbo.DmePaymentLines");
        db.LastBatch.Should().Contain("INSERT INTO dbo.DmePaymentLineAdjustments");
    }

    /// <summary>
    /// Paid to date, balance and payment status are computed in vDmeClaims.
    /// The moment posting also updates the claim there are two answers to the
    /// same question, and the stored one starts drifting the first time a
    /// posting is voided.
    /// </summary>
    [Fact]
    public async Task PostingNeverWritesBackToTheClaim()
    {
        var (service, db, _) = Build();

        await service.PostAsync(Receipt(100m, 100m));

        db.LastBatch.Should().NotContain("UPDATE dbo.DmeClaims",
            "the claim's payment status is derived, not stored");
        db.LastBatch.Should().NotContain("INSERT INTO dbo.DmeClaims");
    }

    [Fact]
    public async Task EveryValueInTheBatchIsABoundParameter()
    {
        var (service, db, _) = Build();

        await service.PostAsync(Receipt(100m, 100m, null, KnownClaimLine,
            new PaymentAdjustmentInput("CO", "45", 35.60m)));

        // The statement is assembled from a variable number of lines, so the
        // thing worth pinning is that the assembly puts placeholders in it and
        // never the values themselves.
        db.LastBatch.Should().NotContain("35.60");
        db.LastBatch.Should().NotContain("'CO'");
        db.LastBatch.Should().Contain("@TenantId");
    }

    [Fact]
    public async Task ASuccessfulPost_IsAudited()
    {
        var (service, _, audit) = Build();

        await service.PostAsync(Receipt(100m, 100m));

        audit.Verify(a => a.RecordAsync("DME_PAYMENT_POSTED", "DmePayment",
            It.IsAny<int?>(), null, It.IsAny<object>()), Times.Once);
    }

    [Fact]
    public async Task ARefusedPost_WritesNothingAndAuditsNothing()
    {
        var (service, db, audit) = Build();

        await service.PostAsync(Receipt(amount: 10m, paid: 500m));

        db.ExecutedSql.Should().BeEmpty();
        audit.VerifyNoOtherCalls();
    }

    // ------------------------------------------------------------------- void

    [Fact]
    public async Task VoidingWithoutAReason_IsRefused()
    {
        var (service, db, _) = Build();
        SetupExistingPayment(db, alreadyVoided: false);

        var result = await service.VoidAsync(7, "  ");

        result.Ok.Should().BeFalse(
            "the reason is the only record of why money moved back off the books");
    }

    [Fact]
    public async Task VoidingAPaymentThatIsAlreadyVoided_IsRefused()
    {
        var (service, db, _) = Build();
        SetupExistingPayment(db, alreadyVoided: true);

        var result = await service.VoidAsync(7, "duplicate posting");

        result.Ok.Should().BeFalse("a second void would put a reversal in the trail that never happened");
    }

    [Fact]
    public async Task AVoidThatChangesNothing_IsNotAudited()
    {
        var (service, db, audit) = Build();
        SetupExistingPayment(db, alreadyVoided: false);
        // The UPDATE is guarded on VoidedAt IS NULL, so a race loses and
        // affects no rows. That is a no-op, and a no-op is not an event.
        db.Mock.Setup(d => d.Execute(It.IsAny<string>(), It.IsAny<object>())).Returns(0);

        var result = await service.VoidAsync(7, "duplicate posting");

        result.Ok.Should().BeFalse();
        audit.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AVoidThatSucceeds_IsAudited()
    {
        var (service, db, audit) = Build();
        SetupExistingPayment(db, alreadyVoided: false);
        db.Mock.Setup(d => d.Execute(It.IsAny<string>(), It.IsAny<object>())).Returns(1);

        var result = await service.VoidAsync(7, "posted against the wrong claim");

        result.Ok.Should().BeTrue();
        audit.Verify(a => a.RecordAsync("DME_PAYMENT_VOIDED", "DmePayment",
            7, It.IsAny<object>(), It.IsAny<object>()), Times.Once);
    }

    [Fact]
    public async Task VoidingNeverDeletesTheRow()
    {
        var (service, db, _) = Build();
        SetupExistingPayment(db, alreadyVoided: false);
        var statements = new List<string>();
        db.Mock.Setup(d => d.Execute(It.IsAny<string>(), It.IsAny<object>()))
            .Callback<string, object?>((sql, _) => statements.Add(sql))
            .Returns(1);

        await service.VoidAsync(7, "posted against the wrong claim");

        statements.Should().ContainSingle();
        statements[0].Should().NotContain("DELETE",
            "a deleted payment leaves an audit trail describing a row nobody can look at");
        statements[0].Should().Contain("VoidedAt IS NULL",
            "two people clicking Void at the same moment must produce one reversal, not two");
    }

    private static void SetupExistingPayment(FakeDb db, bool alreadyVoided)
        => db.Mock.Setup(d => d.QueryOne(It.Is<string>(s => s.Contains("FROM dbo.DmePayments")), It.IsAny<object>()))
            .Returns(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["PaymentNumber"] = "PMT-01001",
                ["Amount"] = 113.92m,
                ["VoidedAt"] = alreadyVoided ? DateTime.UtcNow : null,
            });
}
