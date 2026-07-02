using EHR.Models.Generated;
using Microsoft.EntityFrameworkCore;
using static EHR.Services.DemoData.DemoDataConstants;

namespace EHR.Services.DemoData;

/// <summary>
/// Generates Charges, BillingClaims, Payments, PatientLedger entries, ClaimStatusHistory,
/// and InstallmentPlans (display-only, no Stripe).
/// </summary>
public class DemoBillingGenerator
{
    private readonly EhrDbContext _db;

    public DemoBillingGenerator(EhrDbContext db)
    {
        _db = db;
    }

    public record BillingResult(int ChargeCount, int ClaimCount, int PaymentCount, int LedgerCount, int InstallmentPlanCount);

    public async Task<BillingResult> GenerateAsync(
        int tenantId,
        List<Encounter> encounters,
        List<ClinicalNote> notes,
        List<Appointment> appointments,
        List<Insurance> insurances,
        int createdByUserId)
    {
        int chargeCount = 0, claimCount = 0, paymentCount = 0, ledgerCount = 0, installmentPlanCount = 0;
        int claimSeq = 1;

        // Build insurance lookup by patient
        var insuranceByPatient = insurances
            .Where(i => i.IsActive == true && i.Type == 0) // Primary only
            .GroupBy(i => i.PatientId)
            .ToDictionary(g => g.Key, g => g.First());

        // Only process signed encounters (Status >= 1) that have notes
        var signedEncounters = encounters.Where(e => e.Status >= 1).ToList();
        var noteByEncounter = notes.ToDictionary(n => n.EncounterId ?? 0, n => n);
        var apptById = appointments.ToDictionary(a => a.AppointmentId, a => a);

        // Track patients with outstanding balances for installment plans later
        var patientsWithBalance = new Dictionary<int, decimal>(); // PatientId -> outstanding amount

        foreach (var enc in signedEncounters)
        {
            if (!noteByEncounter.TryGetValue(enc.EncounterId, out var note)) continue;
            if (!enc.AppointmentId.HasValue || !apptById.TryGetValue(enc.AppointmentId.Value, out var appt)) continue;

            // ── Pick CPT code based on appointment type ─────────────
            var cpt = appt.Type switch
            {
                0 => CptCodes[Between(3, 5)],   // New patient: 99203-99205
                2 => Chance(30) ? CptCodes[Between(11, 18)] : CptCodes[Between(6, 8)], // Preventive: 30% pediatric, 70% adult
                5 => CptCodes[Between(9, 10)],   // Telehealth: 99441-99442
                3 => Chance(30) ? CptCodes[Between(15, 18)] : CptCodes[Between(6, 8)], // Wellness: mix peds/adult
                _ => CptCodes[Between(0, 2)]     // Follow-up: 99213-99215
            };

            // ── Create Charge ───────────────────────────────────────
            var charge = new Charge
            {
                TenantId = tenantId,
                PatientId = enc.PatientId,
                ClinicalNoteId = note.ClinicalNoteId,
                AppointmentId = appt.AppointmentId,
                ProviderId = enc.ProviderId,
                ServiceDate = enc.EncounterDate,
                Cptcode = cpt.Code,
                Cptdescription = cpt.Desc,
                Units = 1,
                ChargeAmount = cpt.Amount,
                Status = 1, // Billed
                CreatedAt = enc.CreatedAt.AddHours(Between(1, 4))
            };
            _db.Charges.Add(charge);
            await _db.SaveChangesAsync();
            chargeCount++;

            // ── Create Billing Claim ────────────────────────────────
            insuranceByPatient.TryGetValue(enc.PatientId, out var insurance);

            // Claim status distribution: 60% Paid, 10% PartiallyPaid, 10% Pending, 10% Submitted, 5% Denied, 5% Draft
            var claimStatus = Chance(60) ? 5 :
                              Chance(10) ? 6 :
                              Chance(10) ? 4 :
                              Chance(10) ? 2 :
                              Chance(5) ? 7 :
                              0;

            var totalCharged = cpt.Amount;
            var allowedPct = BetweenDec(0.60m, 0.90m);
            var totalAllowed = Math.Round(totalCharged * allowedPct, 2);
            decimal? totalPaid = null;
            decimal? totalAdjustment = null;
            decimal? patientResp = null;
            DateTime? submittedAt = null;
            DateTime? processedAt = null;

            if (claimStatus >= 2)
                submittedAt = enc.CreatedAt.AddDays(Between(1, 5));
            if (claimStatus >= 4)
                processedAt = submittedAt?.AddDays(Between(7, 45));
            if (claimStatus == 5) // Paid
            {
                totalPaid = totalAllowed;
                totalAdjustment = totalCharged - totalAllowed;
                patientResp = insurance?.Copay ?? BetweenDec(25, 50);
                charge.Status = 2;
                charge.PaidAmount = totalPaid;
                charge.AllowedAmount = totalAllowed;
                charge.AdjustmentAmount = totalAdjustment;
                charge.PatientResponsibility = patientResp;
            }
            else if (claimStatus == 6) // Partially paid
            {
                totalPaid = Math.Round(totalAllowed * BetweenDec(0.40m, 0.70m), 2);
                totalAdjustment = Math.Round(totalCharged * BetweenDec(0.05m, 0.15m), 2);
                patientResp = totalCharged - (totalPaid ?? 0) - (totalAdjustment ?? 0);
                charge.Status = 2;
                charge.PaidAmount = totalPaid;
            }
            else if (claimStatus == 7) // Denied
            {
                totalPaid = 0;
                charge.Status = 3;
            }

            var claimYear = enc.EncounterDate.Year;
            var claimNumber = $"CLM-{claimYear}-{claimSeq:D5}";
            claimSeq++;

            var payerClaimNumber = (claimStatus == 5 || claimStatus == 6)
                ? $"PCN{Rng.Next(1_000_000_000, int.MaxValue):D10}"
                : null;

            var claim = new BillingClaim
            {
                TenantId = tenantId,
                PatientId = enc.PatientId,
                InsuranceId = insurance?.InsuranceId,
                ClinicalNoteId = note.ClinicalNoteId,
                AppointmentId = appt.AppointmentId,
                ProviderId = enc.ProviderId,
                LocationId = appt.LocationId,
                ServiceDateFrom = enc.EncounterDate,
                ServiceDateTo = enc.EncounterDate,
                TotalCharged = totalCharged,
                TotalAllowed = claimStatus >= 5 ? totalAllowed : null,
                TotalPaid = totalPaid,
                TotalAdjustment = totalAdjustment,
                PatientResponsibility = patientResp,
                Status = claimStatus,
                Type = 0, // Professional
                SubmittedAt = submittedAt,
                ProcessedAt = processedAt,
                DiagnosisCodes = Pick(IcdCodes).Code,
                PlaceOfServiceCode = "11",
                AcceptAssignment = true,
                ClaimNumber = claimNumber,
                PayerClaimNumber = payerClaimNumber,
                CreatedAt = charge.CreatedAt,
                CreatedBy = createdByUserId
            };
            _db.BillingClaims.Add(claim);
            await _db.SaveChangesAsync();
            claimCount++;

            charge.ClaimId = claim.ClaimId;

            // ── Claim Status History ────────────────────────────────
            _db.ClaimStatusHistories.Add(new ClaimStatusHistory
            {
                TenantId = tenantId,
                ClaimId = claim.ClaimId,
                Status = 0,
                Notes = "Claim created",
                CreatedAt = claim.CreatedAt,
                CreatedBy = createdByUserId
            });
            if (claimStatus >= 2)
            {
                _db.ClaimStatusHistories.Add(new ClaimStatusHistory
                {
                    TenantId = tenantId,
                    ClaimId = claim.ClaimId,
                    Status = 2,
                    Notes = "Submitted to payer",
                    CreatedAt = submittedAt ?? (claim.CreatedAt ?? DateTime.UtcNow).AddDays(1),
                    CreatedBy = createdByUserId
                });
            }
            if (claimStatus >= 4)
            {
                _db.ClaimStatusHistories.Add(new ClaimStatusHistory
                {
                    TenantId = tenantId,
                    ClaimId = claim.ClaimId,
                    Status = claimStatus,
                    Notes = claimStatus == 7 ? "Denied: Missing prior authorization" : "Processed by payer",
                    CreatedAt = processedAt ?? (claim.CreatedAt ?? DateTime.UtcNow).AddDays(14),
                    CreatedBy = createdByUserId
                });
            }

            // ── Payment (for paid/partially paid claims) ────────────
            if (claimStatus == 5 || claimStatus == 6)
            {
                // Insurance payment
                var payment = new Payment
                {
                    TenantId = tenantId,
                    PatientId = enc.PatientId,
                    AppointmentId = appt.AppointmentId,
                    ClaimId = claim.ClaimId,
                    Type = 4, // InsurancePayment
                    Method = 4, // EFT
                    Amount = totalPaid ?? 0,
                    PayerName = insurance?.PayerName ?? "Insurance",
                    Status = 1, // Completed
                    PaymentDate = DateOnly.FromDateTime(processedAt ?? DateTime.UtcNow),
                    CreatedAt = processedAt ?? DateTime.UtcNow,
                    CreatedBy = createdByUserId
                };
                _db.Payments.Add(payment);
                await _db.SaveChangesAsync();
                paymentCount++;

                // ── Patient Ledger: Charge entry ────────────────────
                _db.PatientLedgers.Add(new PatientLedger
                {
                    TenantId = tenantId,
                    PatientId = enc.PatientId,
                    EntryType = 0,
                    ChargeId = charge.ChargeId,
                    ClaimId = claim.ClaimId,
                    TransactionDate = enc.EncounterDate,
                    Amount = totalCharged,
                    Description = $"Charge: {cpt.Code} - {cpt.Desc}",
                    CreatedAt = charge.CreatedAt
                });
                ledgerCount++;

                // ── Patient Ledger: Insurance payment entry ─────────
                _db.PatientLedgers.Add(new PatientLedger
                {
                    TenantId = tenantId,
                    PatientId = enc.PatientId,
                    EntryType = 1,
                    PaymentId = payment.PaymentId,
                    ClaimId = claim.ClaimId,
                    TransactionDate = DateOnly.FromDateTime(processedAt ?? DateTime.UtcNow),
                    Amount = -(totalPaid ?? 0),
                    Description = $"Insurance payment: {insurance?.PayerName ?? "Payer"}",
                    CreatedAt = processedAt ?? DateTime.UtcNow
                });
                ledgerCount++;

                // 60% of paid claims also have a copay payment from patient
                if (Chance(60) && patientResp > 0)
                {
                    var copayPayment = new Payment
                    {
                        TenantId = tenantId,
                        PatientId = enc.PatientId,
                        AppointmentId = appt.AppointmentId,
                        Type = 0, // Copay
                        Method = Chance(50) ? 2 : Chance(30) ? 0 : 1, // CC, Cash, or Check
                        Amount = patientResp ?? 0,
                        Status = 1,
                        PaymentDate = DateOnly.FromDateTime(appt.StartTime),
                        CreatedAt = appt.StartTime,
                        CreatedBy = createdByUserId
                    };
                    _db.Payments.Add(copayPayment);
                    await _db.SaveChangesAsync();
                    paymentCount++;

                    _db.PatientLedgers.Add(new PatientLedger
                    {
                        TenantId = tenantId,
                        PatientId = enc.PatientId,
                        EntryType = 1,
                        PaymentId = copayPayment.PaymentId,
                        TransactionDate = DateOnly.FromDateTime(appt.StartTime),
                        Amount = -(patientResp ?? 0),
                        Description = "Patient copay payment",
                        CreatedAt = appt.StartTime
                    });
                    ledgerCount++;
                }
                else if (patientResp > 0)
                {
                    // 40% of paid claims: copay NOT collected — patient owes
                    if (!patientsWithBalance.ContainsKey(enc.PatientId))
                        patientsWithBalance[enc.PatientId] = 0;
                    patientsWithBalance[enc.PatientId] += patientResp ?? 0;
                }

                // Adjustment ledger entry
                if (totalAdjustment > 0)
                {
                    _db.PatientLedgers.Add(new PatientLedger
                    {
                        TenantId = tenantId,
                        PatientId = enc.PatientId,
                        EntryType = 2,
                        ChargeId = charge.ChargeId,
                        ClaimId = claim.ClaimId,
                        TransactionDate = DateOnly.FromDateTime(processedAt ?? DateTime.UtcNow),
                        Amount = -(totalAdjustment ?? 0),
                        Description = "Contractual adjustment",
                        CreatedAt = processedAt ?? DateTime.UtcNow
                    });
                    ledgerCount++;
                }
            }
            else
            {
                // Non-paid claims still get a charge ledger entry
                _db.PatientLedgers.Add(new PatientLedger
                {
                    TenantId = tenantId,
                    PatientId = enc.PatientId,
                    EntryType = 0,
                    ChargeId = charge.ChargeId,
                    TransactionDate = enc.EncounterDate,
                    Amount = totalCharged,
                    Description = $"Charge: {cpt.Code} - {cpt.Desc}",
                    CreatedAt = charge.CreatedAt
                });
                ledgerCount++;

                // Track outstanding for pending/submitted claims too
                if (claimStatus >= 2 && claimStatus <= 4)
                {
                    if (!patientsWithBalance.ContainsKey(enc.PatientId))
                        patientsWithBalance[enc.PatientId] = 0;
                    patientsWithBalance[enc.PatientId] += totalCharged;
                }
            }
        }

        await _db.SaveChangesAsync();

        // ══════════════════════════════════════════════════════════════
        // INSTALLMENT PLANS (display-only, NO Stripe integration)
        // Pick 12-15 patients with outstanding balances and create
        // completed/active installment plans for demo display.
        // StripeCustomerId, StripePaymentMethodId, and StripeConnectAccountId
        // are LEFT NULL so the background processor will skip them entirely.
        // ══════════════════════════════════════════════════════════════

        // Find a default location for this tenant (for LocationId column)
        var defaultLocationId = await _db.Locations
            .Where(l => l.TenantId == tenantId && l.IsActive == true)
            .OrderBy(l => l.LocationId)
            .Select(l => (int?)l.LocationId)
            .FirstOrDefaultAsync();

        var installmentCandidates = patientsWithBalance
            .Where(kv => kv.Value >= 50) // Only if balance >= $50
            .OrderByDescending(kv => kv.Value)
            .Take(15)
            .ToList();

        foreach (var (patientId, balance) in installmentCandidates)
        {
            var planAmount = Math.Round(Math.Min(balance, BetweenDec(150, 500)), 2);
            var numInstallments = Chance(40) ? 3 : Chance(40) ? 4 : 6;
            var isCompleted = Chance(40); // 40% completed, 60% active
            var createdDate = DateTime.UtcNow.AddDays(-Between(30, 75));

            var plan = new InstallmentPlan
            {
                TenantId = tenantId,
                PatientId = patientId,
                LocationId = defaultLocationId,    // Demo: tenant's first active location
                StripeConnectAccountId = null,     // NO Stripe — display only
                TotalAmount = planAmount,
                NumberOfInstallments = numInstallments,
                Status = isCompleted ? 1 : 0, // Completed=1, Active=0
                StripeCustomerId = null,       // NO Stripe — display only
                StripePaymentMethodId = null,  // NO Stripe — display only
                CreatedAt = createdDate,
                CreatedBy = createdByUserId
            };
            _db.InstallmentPlans.Add(plan);
            await _db.SaveChangesAsync();
            installmentPlanCount++;

            var installmentAmount = Math.Round(planAmount / numInstallments, 2);
            var remainder = planAmount - (installmentAmount * numInstallments);

            for (int inst = 1; inst <= numInstallments; inst++)
            {
                var dueDate = DateOnly.FromDateTime(createdDate.AddMonths(inst));
                var amount = inst == numInstallments ? installmentAmount + remainder : installmentAmount;
                var isPastDue = dueDate <= DateOnly.FromDateTime(DateTime.UtcNow);

                // For completed plans: all paid. For active plans: past-due ones are paid
                var isPaid = isCompleted || (isPastDue && Chance(85));

                var detail = new InstallmentDetail
                {
                    PlanId = plan.PlanId,
                    TenantId = tenantId,
                    InstallmentNumber = inst,
                    DueDate = dueDate,
                    Amount = amount,
                    Status = isPaid ? 1 : 0, // Paid=1, Pending=0
                    RetryCount = 0,
                    StripePaymentIntentId = null, // NO Stripe
                    CreatedAt = createdDate
                };

                if (isPaid)
                {
                    // Create a completed payment for this installment
                    var instPayment = new Payment
                    {
                        TenantId = tenantId,
                        PatientId = patientId,
                        Type = 0, // Copay (patient responsibility payment)
                        Method = 2, // CreditCard
                        Amount = amount,
                        Status = 1, // Completed
                        PaymentDate = dueDate,
                        Notes = $"Installment {inst} of {numInstallments}",
                        CreatedAt = dueDate.ToDateTime(new TimeOnly(10, 0)),
                        CreatedBy = createdByUserId
                    };
                    _db.Payments.Add(instPayment);
                    await _db.SaveChangesAsync();
                    paymentCount++;

                    detail.PaymentId = instPayment.PaymentId;
                    detail.PaidAt = dueDate.ToDateTime(new TimeOnly(10, 0));

                    // Ledger entry for installment payment
                    _db.PatientLedgers.Add(new PatientLedger
                    {
                        TenantId = tenantId,
                        PatientId = patientId,
                        EntryType = 1, // Payment
                        PaymentId = instPayment.PaymentId,
                        TransactionDate = dueDate,
                        Amount = -amount,
                        Description = $"Patient Payment - Installment {inst}/{numInstallments} (Online)",
                        CreatedAt = dueDate.ToDateTime(new TimeOnly(10, 0))
                    });
                    ledgerCount++;
                }

                _db.InstallmentDetails.Add(detail);
            }
        }

        await _db.SaveChangesAsync();
        return new BillingResult(chargeCount, claimCount, paymentCount, ledgerCount, installmentPlanCount);
    }
}
