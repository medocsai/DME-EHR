using EHR.Models.Generated;
using static EHR.Services.DemoData.DemoDataConstants;

namespace EHR.Services.DemoData;

/// <summary>
/// Generates Appointments, Encounters, ClinicalNotes, PatientVitals, Orders, OrderResults, Prescriptions.
/// </summary>
public class DemoClinicalGenerator
{
    private readonly EhrDbContext _db;

    public DemoClinicalGenerator(EhrDbContext db)
    {
        _db = db;
    }

    public record ClinicalResult(
        List<Appointment> Appointments,
        List<Encounter> Encounters,
        List<ClinicalNote> Notes,
        List<Order> Orders,
        List<Prescription> Prescriptions);

    public async Task<ClinicalResult> GenerateAsync(
        int tenantId, List<Patient> patients, List<Provider> providers,
        List<Location> locations, List<User> users, int createdByUserId)
    {
        // Map ProviderId -> UserId for correct signing references
        var providerUserMap = users
            .Where(u => u.ProviderId.HasValue)
            .ToDictionary(u => u.ProviderId!.Value, u => u.UserId);
        var appointments = new List<Appointment>();
        var encounters = new List<Encounter>();
        var notes = new List<ClinicalNote>();
        var orders = new List<Order>();
        var prescriptions = new List<Prescription>();

        var now = DateTime.UtcNow;
        var threeMonthsAgo = now.AddMonths(-3);
        var twoMonthsAgo = now.AddMonths(-2);

        // ── Generate ~1000 Appointments ─────────────────────────────
        // Each patient gets 1-6 visits, weighted heavier in last 2 months
        foreach (var patient in patients)
        {
            var visitCount = Between(1, 6);
            for (int v = 0; v < visitCount; v++)
            {
                // 70% in last 2 months, 30% in month 3
                var isRecent = Chance(70);
                var rangeStart = isRecent ? twoMonthsAgo : threeMonthsAgo;
                var rangeEnd = isRecent ? now.AddDays(-1) : twoMonthsAgo;

                var provider = Pick(providers.ToArray());
                var location = Pick(locations.ToArray());

                // Random weekday time slot 9am-4:30pm
                var apptDate = RandomDateTime(rangeStart, rangeEnd);
                // Force weekday
                while (apptDate.DayOfWeek == DayOfWeek.Saturday || apptDate.DayOfWeek == DayOfWeek.Sunday)
                    apptDate = apptDate.AddDays(1);
                var hour = Between(9, 16);
                var minute = Pick(new[] { 0, 15, 30, 45 });
                apptDate = new DateTime(apptDate.Year, apptDate.Month, apptDate.Day, hour, minute, 0, DateTimeKind.Utc);

                // Appointment type distribution
                var type = Chance(60) ? 1 :  // Follow-up
                           Chance(15) ? 0 :  // New Patient
                           Chance(10) ? 2 :  // Annual Physical
                           Chance(5) ? 5 :   // Telehealth
                           Chance(5) ? 3 :   // Wellness
                           Between(4, 9);    // Other

                // Status: future appts are Scheduled, past ones have outcome distribution
                int status;
                bool isFuture = apptDate > now;
                if (isFuture)
                {
                    status = Chance(50) ? 0 : 1; // Scheduled or Confirmed
                }
                else
                {
                    status = Chance(80) ? 4 :  // Completed
                             Chance(8) ? 5 :   // NoShow
                             Chance(5) ? 6 :   // Cancelled
                             Chance(4) ? 3 :   // InProgress
                             8;                // Missed
                }

                var duration = type == 0 ? 45 : 30; // New patients get 45 min
                var copayDue = Chance(70) ? BetweenDec(20, 50) : 0;

                var appt = new Appointment
                {
                    TenantId = tenantId,
                    PatientId = patient.PatientId,
                    ProviderId = provider.ProviderId,
                    LocationId = location.LocationId,
                    Type = type,
                    StartTime = apptDate,
                    EndTime = apptDate.AddMinutes(duration),
                    Status = status,
                    Reason = type switch
                    {
                        0 => "New patient evaluation",
                        1 => "Follow-up visit",
                        2 => "Annual physical exam",
                        3 => "Wellness exam",
                        5 => "Telehealth follow-up",
                        _ => "Office visit"
                    },
                    IsTelehealth = type == 5,
                    CopayDue = copayDue,
                    CopayCollected = status == 4 ? copayDue : 0,
                    InsuranceVerified = Chance(90),
                    CheckInTime = status >= 2 && status <= 4 ? apptDate.AddMinutes(-Between(5, 15)) : null,
                    CheckOutTime = status == 4 ? apptDate.AddMinutes(duration + Between(5, 15)) : null,
                    CreatedAt = apptDate.AddDays(-Between(1, 14)),
                    CreatedByUserId = createdByUserId
                };
                _db.Appointments.Add(appt);
                appointments.Add(appt);
            }
        }
        await _db.SaveChangesAsync();

        // ── Encounters (1:1 with completed/inprogress appointments) ─
        var completedAppts = appointments.Where(a => a.Status >= 3 && a.Status <= 4).ToList();
        foreach (var appt in completedAppts)
        {
            // Encounter status: 85% Signed, 10% Open, 5% Amended
            var encStatus = Chance(85) ? 1 : Chance(10) ? 0 : 3;
            var signedAt = encStatus >= 1
                ? appt.StartTime.AddDays(Between(0, 3)).AddHours(Between(1, 8))
                : (DateTime?)null;

            var provUserId = providerUserMap.TryGetValue(appt.ProviderId, out var uid) ? uid : createdByUserId;

            var encounter = new Encounter
            {
                TenantId = tenantId,
                PatientId = appt.PatientId,
                ProviderId = appt.ProviderId,
                AppointmentId = appt.AppointmentId,
                EncounterDate = DateOnly.FromDateTime(appt.StartTime),
                ChiefComplaint = appt.Reason ?? "Follow-up",
                Status = encStatus,
                CreatedByUserId = provUserId,
                CreatedAt = appt.StartTime,
                SignedAt = signedAt,
                SignedByUserId = encStatus >= 1 ? provUserId : null
            };
            _db.Encounters.Add(encounter);
            encounters.Add(encounter);
        }
        await _db.SaveChangesAsync();

        // ── Clinical Notes (1:1 with encounters) ────────────────────
        foreach (var enc in encounters)
        {
            // Note status mirrors encounter: Signed→2(Signed), Open→0(Draft), Amended→5(Amended)
            var noteStatus = enc.Status switch { 1 => 2, 2 => 4, 3 => 5, _ => 0 };
            var noteType = enc.ChiefComplaint?.Contains("Annual") == true ? 6 :
                           enc.ChiefComplaint?.Contains("New patient") == true ? 0 :
                           1; // SOAP

            var note = new ClinicalNote
            {
                TenantId = tenantId,
                PatientId = enc.PatientId,
                ProviderId = enc.ProviderId,
                AppointmentId = enc.AppointmentId,
                EncounterId = enc.EncounterId,
                Type = noteType,
                Status = noteStatus,
                ServiceDate = enc.EncounterDate,
                HtmlContent = $"<p><strong>CC:</strong> {enc.ChiefComplaint}</p><p><strong>Assessment:</strong> Patient seen and evaluated.</p><p><strong>Plan:</strong> Continue current management. Follow up as scheduled.</p>",
                SignedAt = enc.SignedAt,
                SignedByUserId = enc.SignedByUserId,
                CreatedByUserId = enc.CreatedByUserId,
                CreatedAt = enc.CreatedAt
            };
            _db.ClinicalNotes.Add(note);
            notes.Add(note);
        }
        await _db.SaveChangesAsync();

        // ── Patient Vitals (1 per encounter) ────────────────────────
        foreach (var enc in encounters)
        {
            var systolic = Between(110, 155);
            var weight = BetweenDec(120, 280);
            var height = BetweenDec(60, 76); // inches
            var bmi = Math.Round(weight / (height * height) * 703, 1);

            _db.PatientVitals.Add(new PatientVital
            {
                TenantId = tenantId,
                PatientId = enc.PatientId,
                EncounterId = enc.EncounterId,
                RecordedAt = enc.CreatedAt,
                RecordedByUserId = createdByUserId,
                SystolicBp = systolic,
                DiastolicBp = systolic - Between(30, 50),
                HeartRate = Between(60, 100),
                RespiratoryRate = Between(14, 20),
                Temperature = BetweenDec(97.0m, 99.5m),
                SpO2 = BetweenDec(95, 100),
                Weight = weight,
                Height = height,
                Bmi = bmi
            });
        }
        await _db.SaveChangesAsync();

        // ── Orders (45% of encounters get 1-2 orders) ───────────────
        foreach (var enc in encounters)
        {
            if (!Chance(45)) continue;
            var orderCount = Between(1, 2);
            for (int o = 0; o < orderCount; o++)
            {
                // Type distribution: 60% Lab, 25% Imaging, 15% Referral
                var orderType = Chance(60) ? 0 : Chance(25) ? 1 : 2;
                var isCompleted = Chance(70);
                var orderStatus = isCompleted ? 5 :
                                  Chance(15) ? 1 :
                                  Chance(10) ? 2 :
                                  6; // Cancelled

                var order = new Order
                {
                    TenantId = tenantId,
                    PatientId = enc.PatientId,
                    ProviderId = enc.ProviderId,
                    EncounterId = enc.EncounterId,
                    OrderType = orderType,
                    Status = orderStatus,
                    Priority = Chance(10) ? 2 : Chance(20) ? 1 : 0, // 10% STAT, 20% Urgent, 70% Routine
                    OrderDate = enc.EncounterDate,
                    DiagnosisCode = Pick(IcdCodes).Code,
                    CompletedAt = isCompleted ? enc.CreatedAt.AddDays(Between(1, 7)) : null,
                    CreatedByUserId = createdByUserId,
                    CreatedAt = enc.CreatedAt
                };

                // Set type-specific fields
                if (orderType == 0) // Lab
                {
                    var panel = Pick(LabPanels);
                    order.LabPanelName = panel.Panel;
                    order.FastingRequired = panel.Panel.Contains("Lipid") || panel.Panel.Contains("Glucose");
                    order.ClinicalIndication = "Routine monitoring";
                }
                else if (orderType == 1) // Imaging
                {
                    var study = Pick(ImagingStudies);
                    order.ClinicalIndication = study.Study;
                    order.Modality = study.Modality;
                    order.BodyPart = study.BodyPart;
                    order.ImagingFacility = "Austin Imaging Center";
                }
                else // Referral
                {
                    var referral = Pick(Referrals);
                    order.ReferralSpecialty = referral.Specialty;
                    order.ReferredToFacility = referral.Facility;
                    order.ReferralReason = referral.Reason;
                    order.ReferralUrgency = 0; // Routine
                }

                _db.Orders.Add(order);
                orders.Add(order);
            }
        }
        await _db.SaveChangesAsync();

        // ── Order Results (for completed Lab orders) ────────────────
        var completedLabOrders = orders.Where(o => o.OrderType == 0 && o.Status == 5).ToList();
        foreach (var order in completedLabOrders)
        {
            var panel = LabPanels.FirstOrDefault(p => p.Panel == order.LabPanelName);
            if (panel.Tests == null) panel = Pick(LabPanels);

            foreach (var test in panel.Tests)
            {
                // Generate realistic value with ~15% abnormal
                var isAbnormal = Chance(15);
                var resultValue = test.Test switch
                {
                    "HbA1c" => isAbnormal ? BetweenDec(7.0m, 10.5m).ToString() : BetweenDec(4.5m, 5.6m).ToString(),
                    "Glucose" => isAbnormal ? Between(120, 250).ToString() : Between(75, 99).ToString(),
                    "Total Cholesterol" => isAbnormal ? Between(210, 280).ToString() : Between(150, 199).ToString(),
                    "LDL" => isAbnormal ? Between(110, 180).ToString() : Between(60, 99).ToString(),
                    "TSH" => isAbnormal ? BetweenDec(5.0m, 12.0m).ToString() : BetweenDec(0.5m, 3.5m).ToString(),
                    "Creatinine" => isAbnormal ? BetweenDec(1.5m, 3.0m).ToString() : BetweenDec(0.7m, 1.2m).ToString(),
                    "Potassium" => isAbnormal ? BetweenDec(5.5m, 6.5m).ToString() : BetweenDec(3.6m, 5.0m).ToString(),
                    _ => Between(80, 120).ToString()
                };

                _db.OrderResults.Add(new OrderResult
                {
                    OrderId = order.OrderId,
                    TestName = test.Test,
                    ResultValue = resultValue,
                    ResultUnit = test.Unit,
                    ReferenceRange = test.Range,
                    IsAbnormal = isAbnormal,
                    ResultDate = order.CompletedAt,
                    CreatedAt = order.CompletedAt ?? DateTime.UtcNow
                });
            }
        }
        await _db.SaveChangesAsync();

        // ── Prescriptions (55% of encounters get 1-3) ───────────────
        foreach (var enc in encounters)
        {
            if (!Chance(55)) continue;
            var rxCount = Between(1, 3);
            var used = new HashSet<int>();
            for (int r = 0; r < rxCount; r++)
            {
                int idx;
                do { idx = Rng.Next(Drugs.Length); } while (used.Contains(idx));
                used.Add(idx);
                var d = Drugs[idx];

                var rx = new Prescription
                {
                    TenantId = tenantId,
                    PatientId = enc.PatientId,
                    ProviderId = enc.ProviderId,
                    EncounterId = enc.EncounterId,
                    DrugName = d.Drug,
                    GenericName = d.Generic,
                    Strength = d.Strength,
                    DosageForm = d.Form,
                    Quantity = d.Qty,
                    DaysSupply = d.DaysSupply,
                    Route = d.Route,
                    Frequency = d.Freq,
                    Refills = d.Controlled ? 0 : Between(0, 5),
                    DAW = false,
                    Status = Chance(80) ? 1 : Chance(10) ? 2 : 0, // 80% Active, 10% Sent, 10% Draft
                    IsControlledSubstance = d.Controlled,
                    DEASchedule = d.Schedule,
                    PrescribedDate = enc.EncounterDate,
                    DiagnosisCode = Pick(IcdCodes).Code,
                    CreatedByUserId = createdByUserId,
                    CreatedAt = enc.CreatedAt
                };
                _db.Prescriptions.Add(rx);
                prescriptions.Add(rx);
            }
        }
        await _db.SaveChangesAsync();

        return new ClinicalResult(appointments, encounters, notes, orders, prescriptions);
    }
}
