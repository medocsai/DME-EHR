using EHR.Helpers;

namespace EHR.Services;

/// <summary>
/// Posts money against DME claims, voids a posting, and answers the monthly
/// money questions the dashboard asks.
///
/// WHY THIS EXISTS
/// Every other DME operation is a straightforward write that DmeController can
/// do inline. Posting a payment is not: it writes three related tables that
/// must land together, it enforces half a dozen rules that only make sense in
/// one place, and it will shortly have a SECOND caller. When there is a
/// clearinghouse account, an 835/ERA parser reads a remittance file and posts
/// exactly the same thing a biller types by hand. If the rules lived in the
/// controller action, the parser would either duplicate them or skip them.
///
/// So this is the hired guy for money in: one office, one job description,
/// anybody can call him.
///
/// WHY THE WRITE IS A SINGLE SQL BATCH
/// DmeDb opens a connection per call, so three separate Execute calls would be
/// three transactions. A crash between them leaves a payment header with some
/// of its lines, which is a corrupted set of books that nothing would report as
/// wrong: the totals would simply be short. The whole post therefore goes down
/// as one command inside BEGIN TRAN / COMMIT with XACT_ABORT ON, so it either
/// all happens or none of it does.
///
/// WHAT IT DELIBERATELY DOES NOT DO
/// It computes no totals and stores none. AllowedAmount, PaidAmount and the CAS
/// adjustment rows are the only facts written; paid-to-date, balance, denial
/// status and every dashboard figure are read back from vDmeClaims and
/// vDmePaymentLines. See docs/BILLING-DECISIONS.md.
///
/// WHO CALLS IT
/// DmeController (Payments, PostPayment, CreatePayment, VoidPayment, Dashboard).
/// </summary>
public interface IDmePaymentService
{
    /// <summary>
    /// Validate and post one receipt with its lines and adjustments.
    /// Returns the failure reason rather than throwing: every rule it enforces
    /// is something a biller can get wrong on a form, and a form error is not an
    /// exceptional condition.
    /// </summary>
    Task<PostPaymentResult> PostAsync(PaymentInput input);

    /// <summary>
    /// Reverse a posting. The row is marked voided, never edited or deleted, so
    /// the original entry survives for the audit trail while dropping out of
    /// every computed number.
    /// </summary>
    Task<PostPaymentResult> VoidAsync(int paymentId, string? reason);

    /// <summary>The three dashboard numbers for one calendar month.</summary>
    MonthlyMoney MonthlySummary(DateTime monthStart);
}

/// <summary>One CAS adjustment as it appears on the remittance.</summary>
public sealed record PaymentAdjustmentInput(string GroupCode, string ReasonCode, decimal Amount);

/// <summary>One claim line as adjudicated by this receipt.</summary>
public sealed record PaymentLineInput(
    int ClaimLineId,
    decimal AllowedAmount,
    decimal PaidAmount,
    List<PaymentAdjustmentInput> Adjustments);

/// <summary>One receipt: a check, an EFT, a card swipe or cash over the counter.</summary>
public sealed record PaymentInput(
    string Source,
    string? PayerName,
    int? CustomerId,
    DateTime PostedDate,
    string Method,
    string? ReferenceNumber,
    decimal Amount,
    string? Note,
    List<PaymentLineInput> Lines);

/// <summary>Outcome of a post or a void.</summary>
public sealed record PostPaymentResult(bool Ok, string? Error, int PaymentId = 0, string? PaymentNumber = null);

/// <summary>
/// The month's money, as the dashboard shows it. Every figure is by POSTING
/// date, so it reconciles against a bank statement for the same period.
/// </summary>
public sealed record MonthlyMoney(
    DateTime MonthStart,
    decimal AmountPaid,
    decimal AmountDenied,
    int DeniedLineCount,
    string? TopDenialCode,
    string? TopDenialDescription,
    int TopDenialCount,
    decimal Unapplied);

/// <inheritdoc cref="IDmePaymentService"/>
public sealed class DmePaymentService : IDmePaymentService
{
    private readonly IDmeDb _db;
    private readonly IDmeAudit _audit;

    private static readonly string[] ValidSources = { "payer", "customer" };
    private static readonly string[] ValidMethods = { "check", "eft", "card", "cash", "other" };
    private static readonly string[] ValidGroups = { "CO", "PR", "OA", "PI" };

    public DmePaymentService(IDmeDb db, IDmeAudit audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<PostPaymentResult> PostAsync(PaymentInput input)
    {
        var error = Validate(input);
        if (error != null) return new PostPaymentResult(false, error);

        var number = _db.NextNumber("PMT");

        // Parameters are collected as a dictionary because the statement is
        // built from a variable number of lines and adjustments. Every value is
        // still a bound parameter; nothing is concatenated into the SQL.
        var p = new Dictionary<string, object?>
        {
            ["number"] = number,
            ["source"] = input.Source,
            ["payerName"] = (object?)input.PayerName ?? DBNull.Value,
            ["customerId"] = (object?)input.CustomerId ?? DBNull.Value,
            ["postedDate"] = input.PostedDate.Date,
            ["method"] = input.Method,
            ["reference"] = (object?)input.ReferenceNumber ?? DBNull.Value,
            ["amount"] = input.Amount,
            ["note"] = (object?)input.Note ?? DBNull.Value,
        };

        var sql = new System.Text.StringBuilder();
        sql.AppendLine("SET XACT_ABORT ON;");
        sql.AppendLine("BEGIN TRAN;");
        sql.AppendLine(@"
            INSERT INTO dbo.DmePayments
                (TenantId,PaymentNumber,Source,PayerName,CustomerId,PostedDate,Method,ReferenceNumber,Amount,Note)
            VALUES
                (@TenantId,@number,@source,@payerName,@customerId,@postedDate,@method,@reference,@amount,@note);
            DECLARE @paymentId INT = SCOPE_IDENTITY();");

        for (int i = 0; i < input.Lines.Count; i++)
        {
            var line = input.Lines[i];
            p[$"cl{i}"] = line.ClaimLineId;
            p[$"al{i}"] = line.AllowedAmount;
            p[$"pa{i}"] = line.PaidAmount;

            sql.AppendLine($@"
            INSERT INTO dbo.DmePaymentLines (TenantId,PaymentId,ClaimLineId,AllowedAmount,PaidAmount)
            VALUES (@TenantId,@paymentId,@cl{i},@al{i},@pa{i});
            DECLARE @line{i} INT = SCOPE_IDENTITY();");

            for (int j = 0; j < line.Adjustments.Count; j++)
            {
                var adj = line.Adjustments[j];
                p[$"g{i}_{j}"] = adj.GroupCode;
                p[$"r{i}_{j}"] = adj.ReasonCode;
                p[$"m{i}_{j}"] = adj.Amount;

                sql.AppendLine($@"
            INSERT INTO dbo.DmePaymentLineAdjustments (TenantId,PaymentLineId,GroupCode,ReasonCode,Amount)
            VALUES (@TenantId,@line{i},@g{i}_{j},@r{i}_{j},@m{i}_{j});");
            }
        }

        sql.AppendLine("COMMIT;");
        sql.AppendLine("SELECT @paymentId;");

        var paymentId = Convert.ToInt32(_db.Scalar(sql.ToString(), p));

        // The audit entry records what was posted, not what it computes to. A
        // dispute six months from now asks "what did the biller enter", and a
        // snapshot of derived totals would answer a different question.
        await _audit.RecordAsync("DME_PAYMENT_POSTED", "DmePayment", paymentId,
            before: null,
            after: new
            {
                PaymentNumber = number,
                input.Source,
                input.PayerName,
                PostedDate = input.PostedDate.Date,
                input.Method,
                input.ReferenceNumber,
                input.Amount,
                Lines = input.Lines.Select(l => new
                {
                    l.ClaimLineId,
                    l.AllowedAmount,
                    l.PaidAmount,
                    Adjustments = l.Adjustments.Select(a => $"{a.GroupCode}-{a.ReasonCode} {a.Amount:0.00}").ToArray()
                }).ToArray()
            });

        return new PostPaymentResult(true, null, paymentId, number);
    }

    public async Task<PostPaymentResult> VoidAsync(int paymentId, string? reason)
    {
        var payment = _db.QueryOne(
            "SELECT PaymentNumber, Amount, VoidedAt FROM dbo.DmePayments WHERE PaymentId=@paymentId",
            new { paymentId });

        if (payment == null) return new PostPaymentResult(false, "That payment does not exist.");
        if (payment["VoidedAt"] != null)
            return new PostPaymentResult(false, "That payment is already voided.");

        if (string.IsNullOrWhiteSpace(reason))
            return new PostPaymentResult(false, "A void needs a reason. It is the only record of why the money moved back.");

        // Guarded on VoidedAt IS NULL so two people clicking Void at the same
        // moment produce one void and one "already voided", not two audit rows
        // claiming the same reversal.
        var affected = _db.Execute(@"
            UPDATE dbo.DmePayments
               SET VoidedAt = SYSUTCDATETIME(), VoidReason = @reason
             WHERE PaymentId = @paymentId AND VoidedAt IS NULL",
            new { paymentId, reason });

        if (affected == 0) return new PostPaymentResult(false, "That payment is already voided.");

        await _audit.RecordAsync("DME_PAYMENT_VOIDED", "DmePayment", paymentId,
            before: new { Voided = false, Amount = F.Dec(payment["Amount"]) },
            after: new { Voided = true, Reason = reason, PaymentNumber = F.S(payment["PaymentNumber"]) });

        return new PostPaymentResult(true, null, paymentId, F.S(payment["PaymentNumber"]));
    }

    public MonthlyMoney MonthlySummary(DateTime monthStart)
    {
        var from = new DateTime(monthStart.Year, monthStart.Month, 1);
        var to = from.AddMonths(1);

        // Amount paid and amount denied come out of the same rows in one pass,
        // so the two tiles can never describe different sets of postings.
        // Every figure here respects the branch being viewed, and shows the whole
        // business when that is "all branches". @LocationId comes from DmeDb on
        // every command, so nothing is passed in. Location is derived from the
        // claim's customer, so there is no stored copy to disagree with.
        var totals = _db.QueryOne(@"
            SELECT
                AmountPaid      = ISNULL(SUM(PaidAmount), 0),
                AmountDenied    = ISNULL(SUM(CASE WHEN IsDenied = 1 THEN Charge ELSE 0 END), 0),
                DeniedLineCount = ISNULL(SUM(CASE WHEN IsDenied = 1 THEN 1 ELSE 0 END), 0)
            FROM dbo.vDmePaymentLines
            WHERE IsVoided = 0 AND PostedDate >= @from AND PostedDate < @to
              AND " + _db.LocationScope(),
            new { from, to });

        var top = _db.QueryOne(@"
            SELECT TOP 1
                v.DenialCode,
                Description = c.Description,
                Denials     = COUNT(*)
            FROM dbo.vDmePaymentLines v
            LEFT JOIN dbo.DmeCarcCodes c ON c.Code = v.DenialReasonCode
            WHERE v.IsVoided = 0 AND v.IsDenied = 1
              AND v.PostedDate >= @from AND v.PostedDate < @to
              AND " + _db.LocationScope("v.LocationId") + @"
            GROUP BY v.DenialCode, c.Description
            ORDER BY COUNT(*) DESC, v.DenialCode",
            new { from, to });

        // Money received but not yet allocated to a claim line. It is not in
        // AmountPaid, which counts applied money only, so it is reported beside
        // it rather than left invisible: an unallocated check is the commonest
        // posting mistake and it makes the paid tile read low.
        var unapplied = _db.Scalar(@"
            SELECT ISNULL(SUM(UnappliedAmount), 0) FROM dbo.vDmePayments
            WHERE IsVoided = 0 AND PostedDate >= @from AND PostedDate < @to
              AND " + _db.LocationScope(),
            new { from, to });

        return new MonthlyMoney(
            from,
            F.Dec(totals?["AmountPaid"]),
            F.Dec(totals?["AmountDenied"]),
            F.I(totals?["DeniedLineCount"]),
            top == null ? null : F.S(top["DenialCode"]),
            top == null ? null : F.S(top["Description"]),
            top == null ? 0 : F.I(top["Denials"]),
            F.Dec(unapplied));
    }

    /// <summary>
    /// Every rule a posting has to satisfy, in one place.
    ///
    /// These are server side because that is the only side that counts: the
    /// posting screen checks the same things for a better error message, but a
    /// second client, a replayed form or a future 835 parser all arrive here
    /// and none of them run the page's JavaScript.
    /// </summary>
    private string? Validate(PaymentInput input)
    {
        if (!ValidSources.Contains(input.Source))
            return "A payment comes either from a payer or from the customer.";

        if (!ValidMethods.Contains(input.Method))
            return "Choose how the money arrived.";

        // A posting date in the future would put money into a month that has
        // not happened, and the month is what the whole dashboard is keyed on.
        if (input.PostedDate.Date > DateTime.Today)
            return "The posting date cannot be in the future.";

        if (input.Amount < 0)
            return "The amount received cannot be negative. Void the original posting instead.";

        if (input.Lines.Count == 0)
            return "Add at least one claim line before posting.";

        if (input.Lines.Any(l => l.PaidAmount < 0 || l.AllowedAmount < 0))
            return "Allowed and paid amounts cannot be negative.";

        // A posting that adjudicates nothing at all is an empty record that
        // still counts as an event. Note a zero-dollar remittance IS valid, and
        // is how a denial arrives, so the test is for any content rather than
        // for money.
        var hasContent = input.Lines.Any(l =>
            l.PaidAmount != 0 || l.AllowedAmount != 0 || l.Adjustments.Count > 0);
        if (!hasContent)
            return "Nothing was entered against any line. Record what the payer allowed, paid or adjusted.";

        var applied = input.Lines.Sum(l => l.PaidAmount);
        if (applied > input.Amount)
            return $"You have applied {applied:C} but the payment is only {input.Amount:C}. " +
                   "Reduce the amounts applied, or correct the payment total.";

        foreach (var adj in input.Lines.SelectMany(l => l.Adjustments))
        {
            if (!ValidGroups.Contains(adj.GroupCode))
                return $"'{adj.GroupCode}' is not a CAS group code. Use CO, PR, OA or PI.";
            if (string.IsNullOrWhiteSpace(adj.ReasonCode))
                return "Every adjustment needs a reason code. That code is the denial reporting.";
        }

        // Claim lines and reason codes are checked against the database rather
        // than trusted from the form. The claim line check also closes a tenant
        // hole: the SELECT runs through RLS, so a line id belonging to another
        // supplier simply does not come back and the post is refused.
        var lineIds = input.Lines.Select(l => l.ClaimLineId).Distinct().ToList();
        var known = _db.Query(
            $"SELECT ClaimLineId FROM dbo.DmeClaimLines WHERE ClaimLineId IN ({Ids(lineIds)})")
            .Select(r => F.I(r["ClaimLineId"]))
            .ToHashSet();

        if (lineIds.Any(id => !known.Contains(id)))
            return "One of the claim lines being paid no longer exists.";

        var reasonCodes = input.Lines
            .SelectMany(l => l.Adjustments)
            .Select(a => a.ReasonCode)
            .Distinct()
            .ToList();

        if (reasonCodes.Count > 0)
        {
            var knownCodes = _db.Query("SELECT Code FROM dbo.DmeCarcCodes")
                .Select(r => F.S(r["Code"]))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var unknown = reasonCodes.FirstOrDefault(c => !knownCodes.Contains(c));
            if (unknown != null)
                return $"'{unknown}' is not a known claim adjustment reason code.";
        }

        return null;
    }

    /// <summary>
    /// Render a list of integer ids for an IN clause.
    ///
    /// Safe because the values are ints that have already been parsed as ints:
    /// there is no string from the request in the result. Used only for the
    /// existence check, where a parameter per id would mean building the
    /// parameter list twice for no gain.
    /// </summary>
    private static string Ids(IEnumerable<int> ids)
        => ids.Any() ? string.Join(",", ids) : "0";
}
