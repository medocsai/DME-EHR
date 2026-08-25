using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EHR.Services;
using EHR.Helpers;

namespace EHR.Controllers;

/// <summary>
/// DME product — customers, inventory, orders/delivery, rentals, billing,
/// and the operations dashboard. Backed by the DME-native tables in DMEEHR
/// via DmeDb (raw ADO), served on the converted app shell.
///
/// SECURITY
/// [Authorize] is resolved through the MedocsSmartAuth policy scheme, so both
/// a page navigation (session cookie) and a fetch call (Bearer header) are
/// accepted, and neither is optional. Every row these actions touch is
/// additionally scoped to the caller's tenant inside DmeDb.
/// [PhiAccessAudit] writes one AuditLogs row per successful request; the
/// mutating actions add their own explicit before/after entries on top.
/// </summary>
[Authorize]
[PhiAccessAudit(EntityType = "DmeCustomer")]
public class DmeController : Controller
{
    private readonly IDmeDb _db;
    private readonly IDmeAudit _audit;
    private readonly DmeCustomerPhi _phi;

    public DmeController(IDmeDb db, IDmeAudit audit, DmeCustomerPhi phi)
    {
        _db = db;
        _audit = audit;
        _phi = phi;
    }

    // ---------------------------------------------------------------- Dashboard
    public IActionResult Dashboard()
    {
        ViewData["Title"] = "Dashboard";
        ViewData["ActivePage"] = "dashboard";

        var rentals = _db.Query("SELECT * FROM dbo.vDmeRentals WHERE Status='active' ORDER BY NextBillDate");
        var orders = _db.Query("SELECT TOP 6 * FROM dbo.vDmeOrders ORDER BY CreatedAt DESC");
        var claims = _db.Query("SELECT * FROM dbo.vDmeClaims");

        // Customer names arrive encrypted, in parts. Compose them before the
        // view renders, or the dashboard shows base64.
        _phi.ComposeCustomerNames(rentals);
        _phi.ComposeCustomerNames(orders);
        _phi.ComposeCustomerNames(claims);

        var ready = claims.Where(c => F.S(c["Status"]) == "ready").ToList();
        var dueSoon = rentals.Where(r => { var d = F.DaysUntil(r["NextBillDate"]); return d.HasValue && d.Value <= 7; }).ToList();

        ViewBag.Rentals = rentals;
        ViewBag.Orders = orders;
        ViewBag.ActiveRentals = rentals.Count;
        ViewBag.DueSoon = dueSoon.Count;
        ViewBag.OpenOrders = orders.Count(o => F.S(o["Status"]) is "draft" or "confirmed");
        ViewBag.ReadyTotal = ready.Sum(c => F.Dec(c["Total"]));
        ViewBag.ReadyCount = ready.Count;
        return View();
    }

    // ---------------------------------------------------------------- Schedule (Deliveries)
    public IActionResult Schedule()
    {
        ViewData["Title"] = "Schedule";
        ViewData["ActivePage"] = "schedule";

        const string BLUE = "#2f6bdf", AMBER = "#ba7517", TEAL = "#0c7d72", PURPLE = "#7a3ff2";
        var today = DateTime.Today;
        var ev = new List<object>();
        string Iso(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm:ss");
        void Timed(string title, int dayOff, int hour, int min, int dur, string color, string type)
        {
            var s = today.AddDays(dayOff).AddHours(hour).AddMinutes(min);
            ev.Add(new { title, start = Iso(s), end = Iso(s.AddMinutes(dur)), backgroundColor = color, borderColor = color, extendedProps = new { type } });
        }

        // Realistic week of DME logistics (demo)
        Timed("Delivery · Margaret Ellis — O2 concentrator", 0, 9, 0, 60, BLUE, "Delivery");
        Timed("Pickup · Walker return — J. Carter", 0, 11, 30, 45, AMBER, "Pickup");
        Timed("Setup · Hospital bed — D. Fairbanks", 0, 14, 0, 90, PURPLE, "Setup");
        Timed("O2 concentrator check · Frank Marsh", 1, 10, 0, 30, TEAL, "Maintenance");
        Timed("Delivery · Robert Nguyen — CPAP supplies", 1, 13, 0, 60, BLUE, "Delivery");
        Timed("Delivery · Diabetic supplies route (4 stops)", 2, 9, 0, 120, BLUE, "Delivery");
        Timed("Pickup · Hospital bed return", 2, 15, 0, 45, AMBER, "Pickup");
        Timed("Maintenance · Power wheelchair service", 3, 11, 0, 45, TEAL, "Maintenance");
        Timed("Delivery · John Doe — Wheelchair", 3, 9, 30, 60, BLUE, "Delivery");
        Timed("Setup · BiPAP — new patient", 4, 10, 30, 60, PURPLE, "Setup");
        Timed("Delivery · Oxygen tanks — refill route", 4, 13, 30, 90, BLUE, "Delivery");
        Timed("Pickup · CPAP swap", -1, 10, 0, 45, AMBER, "Pickup");

        // Real order deliveries from the DB
        var deliveries = _db.Query("SELECT CustomerFirstName, CustomerLastName, DeliveryDate FROM dbo.vDmeOrders WHERE DeliveryDate IS NOT NULL");
        _phi.ComposeCustomerNames(deliveries);
        foreach (var o in deliveries)
        {
            var d = Convert.ToDateTime(o["DeliveryDate"]).Date.AddHours(11);
            ev.Add(new { title = "Delivery · " + F.S(o["CustomerName"]), start = Iso(d), end = Iso(d.AddHours(1)), backgroundColor = BLUE, borderColor = BLUE, extendedProps = new { type = "Delivery" } });
        }
        // Rental bill-due markers (all-day)
        var billDue = _db.Query("SELECT CustomerFirstName, CustomerLastName, NextBillDate FROM dbo.vDmeRentals WHERE Status='active' AND NextBillDate IS NOT NULL");
        _phi.ComposeCustomerNames(billDue);
        foreach (var r in billDue)
        {
            ev.Add(new { title = "Rental bill due · " + F.S(r["CustomerName"]), start = Convert.ToDateTime(r["NextBillDate"]).ToString("yyyy-MM-dd"), allDay = true, backgroundColor = PURPLE, borderColor = PURPLE, extendedProps = new { type = "Rental billing" } });
        }

        ViewBag.EventsJson = System.Text.Json.JsonSerializer.Serialize(ev);
        return View();
    }

    // ---------------------------------------------------------------- Customers
    public IActionResult Customers(string? q = null)
    {
        ViewData["Title"] = "Customers";
        ViewData["ActivePage"] = "customers";
        const string BaseSql = @"
            SELECT c.*, (SELECT TOP 1 PayerName FROM dbo.DmeCustomerInsurances i
                         WHERE i.CustomerId=c.CustomerId AND i.Kind='primary') AS PrimaryPayer
            FROM dbo.DmeCustomers c";

        List<Dictionary<string, object?>> rows;
        if (!string.IsNullOrWhiteSpace(q))
        {
            // Names and phone numbers are ciphertext, so LIKE cannot match them.
            // The search term is hashed the same way the stored prefix tokens
            // were, and the match happens on hashes. AccountNo stays a plain
            // LIKE because it is not PHI and is not encrypted.
            rows = _db.Query(BaseSql + @"
                WHERE c.AccountNo LIKE @accountLike
                   OR EXISTS (SELECT 1 FROM dbo.DmeCustomerSearchTokens t
                              WHERE t.CustomerId = c.CustomerId AND t.TokenHash = @hash)
                ORDER BY c.LastName",
                new { accountLike = "%" + q + "%", hash = (object?)_phi.SearchHash(q) ?? DBNull.Value });
        }
        else rows = _db.Query(BaseSql);

        _phi.DecryptRows(rows);

        // Sorted AFTER decryption on purpose. ORDER BY LastName in SQL would now
        // sort by ciphertext, which is arbitrary order that happens to look
        // stable, and is the kind of bug nobody reports because the list is
        // merely "in a weird order".
        rows = rows
            .OrderBy(r => F.S(r["LastName"]), StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => F.S(r["FirstName"]), StringComparer.OrdinalIgnoreCase)
            .ToList();

        ViewBag.Query = q ?? "";
        return View(rows);
    }

    public IActionResult Customer(int id)
    {
        ViewData["ActivePage"] = "customers";
        var c = _db.QueryOne("SELECT * FROM dbo.DmeCustomers WHERE CustomerId=@id", new { id });
        if (c == null) return NotFound();
        _phi.DecryptRow(c);
        ViewData["Title"] = F.S(c["FirstName"]) + " " + F.S(c["LastName"]);
        ViewBag.Customer = c;
        ViewBag.Insurances = _db.Query("SELECT * FROM dbo.DmeCustomerInsurances WHERE CustomerId=@id ORDER BY CASE Kind WHEN 'primary' THEN 0 ELSE 1 END", new { id });
        ViewBag.Diagnoses = _db.Query("SELECT * FROM dbo.DmeCustomerDiagnoses WHERE CustomerId=@id ORDER BY IsPrimary DESC", new { id });
        ViewBag.Equipment = _db.Query("SELECT * FROM dbo.DmeSerializedUnits WHERE CustomerId=@id", new { id });
        var custOrders = _db.Query("SELECT * FROM dbo.vDmeOrders WHERE CustomerId=@id ORDER BY CreatedAt DESC", new { id });
        _phi.ComposeCustomerNames(custOrders);
        ViewBag.Orders = custOrders;
        return View();
    }

    /// <summary>
    /// Rebuild a customer's blind index entries.
    ///
    /// Deletes first so this is safe to call on update as well as create: a
    /// renamed customer must not stay findable under the old name, which is
    /// both a correctness and a privacy problem.
    ///
    /// Takes PLAINTEXT, because the only place the plaintext legitimately
    /// exists is in the request that supplied it.
    /// </summary>
    private void IndexCustomerForSearch(int customerId, string? firstName, string? lastName, string? phone)
    {
        _db.Execute("DELETE FROM dbo.DmeCustomerSearchTokens WHERE CustomerId=@customerId", new { customerId });

        foreach (var (hash, fieldType, prefixLength) in _phi.BuildSearchTokens(firstName, lastName, phone))
        {
            _db.Execute(@"INSERT INTO dbo.DmeCustomerSearchTokens (CustomerId,TenantId,TokenHash,FieldType,PrefixLength)
                          VALUES (@customerId,@TenantId,@hash,@fieldType,@prefixLength)",
                new { customerId, hash, fieldType, prefixLength });
        }
    }

    private static readonly (string code, string desc)[] IcdList = new[]
    {
        ("J96.11", "Chronic respiratory failure with hypoxia"),
        ("J44.9", "COPD, unspecified"),
        ("G47.33", "Obstructive sleep apnea"),
        ("E11.9", "Type 2 diabetes mellitus without complications"),
        ("E11.40", "Type 2 diabetes with neuropathy"),
        ("I50.9", "Heart failure, unspecified"),
        ("M62.81", "Muscle weakness (generalized)"),
        ("I69.354", "Hemiplegia following cerebral infarction"),
        ("M17.0", "Bilateral primary osteoarthritis of knee"),
        ("Z99.81", "Dependence on supplemental oxygen"),
        ("R26.2", "Difficulty in walking"),
        ("L89.90", "Pressure ulcer, unspecified stage")
    };

    [HttpGet]
    public IActionResult NewCustomer()
    {
        ViewData["Title"] = "New Customer";
        ViewData["ActivePage"] = "customers";
        ViewBag.Payers = _db.Query("SELECT Name, PayerCode FROM dbo.DmePayers ORDER BY Name");
        ViewBag.Icd = IcdList;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCustomer(
        string firstName, string lastName, DateTime? dob, string? gender, string? ssnLast4,
        int? heightInches, int? weightLbs, string? phone, string? email,
        string? addressLine1, string? city, string? state, string? zip,
        string? emergencyName, string? emergencyRel, string? emergencyPhone,
        string? insPayer, string? insMemberId, string? insGroup, decimal insCopay, int insCoins, decimal insDeductible,
        string? dxCode)
    {
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            return RedirectToAction("NewCustomer");

        // PHI is encrypted before it reaches the database. Nothing downstream
        // gets a chance to forget: the encrypted column list lives in
        // DmeCustomerPhi, not here. Blank values pass through untouched so an
        // empty column stays empty rather than becoming ciphertext for "".
        object Enc(string? v) => (object?)_phi.Encrypt(v) ?? DBNull.Value;

        var acct = _db.NextNumber("LMS", 4);
        var custId = Convert.ToInt32(_db.Scalar(@"
            INSERT INTO dbo.DmeCustomers
            (AccountNo,FirstName,LastName,Dob,Gender,SsnLast4,HeightInches,WeightLbs,Phone,Email,AddressLine1,City,State,Zip,EmergencyName,EmergencyRel,EmergencyPhone,Status,TenantId)
            OUTPUT inserted.CustomerId
            VALUES (@acct,@fn,@ln,@dob,@g,@ssn,@h,@w,@ph,@em,@a1,@city,@st,@zip,@en,@er,@ep,'active',@TenantId)",
            new {
                acct,
                fn = Enc(firstName), ln = Enc(lastName),
                dob = (object?)dob ?? DBNull.Value, g = (object?)gender ?? DBNull.Value,
                ssn = Enc(ssnLast4),
                h = (object?)heightInches ?? DBNull.Value, w = (object?)weightLbs ?? DBNull.Value,
                ph = Enc(phone), em = Enc(email),
                a1 = Enc(addressLine1), city = Enc(city), st = Enc(state), zip = Enc(zip),
                en = Enc(emergencyName), er = Enc(emergencyRel), ep = Enc(emergencyPhone)
            }));

        // Index the plaintext (which only exists here, in memory) so the
        // customer stays findable once the row is ciphertext.
        IndexCustomerForSearch(custId, firstName, lastName, phone);

        if (!string.IsNullOrWhiteSpace(insPayer))
        {
            var payerCode = _db.Scalar("SELECT PayerCode FROM dbo.DmePayers WHERE Name=@n", new { n = insPayer });
            _db.Execute(@"INSERT INTO dbo.DmeCustomerInsurances (CustomerId,Kind,PayerName,PayerId,MemberId,GroupNumber,Copay,Coinsurance,Deductible,SubscriberRel,EligStatus,TenantId)
                            VALUES (@cid,'primary',@pn,@pid,@mid,@grp,@copay,@coins,@ded,'Self','active',@TenantId)",
                new { cid = custId, pn = insPayer, pid = (object?)payerCode ?? DBNull.Value, mid = (object?)insMemberId ?? DBNull.Value,
                      grp = (object?)insGroup ?? DBNull.Value, copay = insCopay, coins = insCoins, ded = insDeductible });
        }

        if (!string.IsNullOrWhiteSpace(dxCode))
        {
            var desc = IcdList.FirstOrDefault(x => x.code == dxCode).desc;
            _db.Execute("INSERT INTO dbo.DmeCustomerDiagnoses (CustomerId,IcdCode,Description,IsPrimary,TenantId) VALUES (@cid,@code,@desc,1,@TenantId)",
                new { cid = custId, code = dxCode, desc = (object?)desc ?? DBNull.Value });
        }

        // Creation has no "before" state. Record the identifying fields only:
        // the full row is retrievable from the customer record, and copying PHI
        // into the audit log would widen the blast radius of a log leak.
        await _audit.RecordAsync("DME_CUSTOMER_CREATED", "DmeCustomer", custId,
            before: null,
            after: new { AccountNo = acct, Payer = insPayer, PrimaryDx = dxCode });

        return RedirectToAction("Customer", new { id = custId });
    }

    // ---------------------------------------------------------------- Inventory
    public IActionResult Inventory()
    {
        ViewData["Title"] = "Inventory";
        ViewData["ActivePage"] = "inventory";
        ViewBag.Catalog = _db.Query("SELECT * FROM dbo.vHcpcsCatalog ORDER BY Category, Hcpcs");
        ViewBag.Units = _db.Query("SELECT * FROM dbo.DmeSerializedUnits ORDER BY Status, Hcpcs");
        return View();
    }

    // ---------------------------------------------------------------- Orders
    public IActionResult Orders()
    {
        ViewData["Title"] = "Orders / Delivery";
        ViewData["ActivePage"] = "orders";
        // LineCount and Total live in the view, so this screen and the order
        // detail screen cannot disagree about what an order is worth.
        var rows = _db.Query("SELECT * FROM dbo.vDmeOrders ORDER BY CreatedAt DESC");
        _phi.ComposeCustomerNames(rows);
        return View(rows);
    }

    public IActionResult Order(int id)
    {
        ViewData["ActivePage"] = "orders";
        var o = _db.QueryOne("SELECT * FROM dbo.vDmeOrders WHERE OrderId=@id", new { id });
        if (o == null) return NotFound();
        _phi.ComposeCustomerName(o);
        ViewData["Title"] = F.S(o["OrderNumber"]);
        ViewBag.Order = o;
        ViewBag.Lines = _db.Query("SELECT * FROM dbo.DmeOrderLines WHERE OrderId=@id", new { id });
        return View();
    }

    [HttpGet]
    public IActionResult NewOrder(int? customerId = null)
    {
        ViewData["Title"] = "New Order";
        ViewData["ActivePage"] = "orders";
        ViewBag.Customers = _db.Query("SELECT CustomerId, AccountNo, FirstName, LastName FROM dbo.DmeCustomers ORDER BY LastName");
        ViewBag.Doctors = _db.Query("SELECT * FROM dbo.DmeDoctors ORDER BY LastName");
        ViewBag.Catalog = _db.Query("SELECT * FROM dbo.vHcpcsCatalog ORDER BY Category, Hcpcs");
        ViewBag.PreCustomer = customerId;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateOrder(int customerId, int? doctorId, DateTime? deliveryDate, decimal deposit,
                                     string[]? hcpcs, string[]? mode, int[]? qty)
    {
        // Existence check only. The customer and doctor NAMES are no longer
        // copied onto the order: vDmeOrders joins them, so correcting a spelling
        // on the customer record fixes every order at once instead of leaving
        // each one showing whatever the name was on the day it was raised.
        var custExists = _db.Scalar("SELECT CustomerId FROM dbo.DmeCustomers WHERE CustomerId=@customerId", new { customerId });
        if (custExists == null) return RedirectToAction("Orders");

        var number = _db.NextNumber("ORD");
        var orderId = Convert.ToInt32(_db.Scalar(@"
            INSERT INTO dbo.DmeOrders (OrderNumber,CustomerId,Status,Stage,DoctorId,DeliveryDate,Deposit,TenantId)
            OUTPUT inserted.OrderId
            VALUES (@number,@customerId,'confirmed','order-ship',@doctorId,@dd,@deposit,@TenantId)",
            new {
                number, customerId,
                doctorId = (object?)doctorId ?? DBNull.Value,
                dd = (object?)deliveryDate ?? DBNull.Value,
                deposit
            }));

        var lineCount = 0;
        if (hcpcs != null)
        {
            for (int i = 0; i < hcpcs.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(hcpcs[i])) continue;
                var item = _db.QueryOne("SELECT * FROM dbo.HcpcsCodes WHERE Hcpcs=@h", new { h = hcpcs[i] });
                if (item == null) continue;
                var m = (mode != null && i < mode.Length) ? mode[i] : "purchase";
                var q = (qty != null && i < qty.Length && qty[i] > 0) ? qty[i] : 1;
                _db.Execute(@"INSERT INTO dbo.DmeOrderLines (OrderId,Hcpcs,ItemName,Category,Mode,Qty,UnitPrice,MonthlyRate,Modifiers,IsSerialized,TenantId)
                                VALUES (@orderId,@h,@name,@cat,@m,@q,@up,@mr,@mods,@ser,@TenantId)",
                    new {
                        orderId, h = hcpcs[i], name = F.S(item["Name"]), cat = F.S(item["Category"]), m, q,
                        up = m == "purchase" ? F.Dec(item["PurchasePrice"]) : 0m,
                        mr = m == "purchase" ? 0m : F.Dec(item["MonthlyRate"]),
                        mods = m == "purchase" ? "NU" : "RR",
                        ser = F.B(item["IsSerialized"])
                    });
                lineCount++;
            }
        }

        await _audit.RecordAsync("DME_ORDER_CREATED", "DmeOrder", orderId,
            before: null,
            after: new { OrderNumber = number, CustomerId = customerId, DoctorId = doctorId, DeliveryDate = deliveryDate, Deposit = deposit, Lines = lineCount });

        return RedirectToAction("Order", new { id = orderId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deliver(int id, string signedBy, string? signature)
    {
        var o = _db.QueryOne("SELECT * FROM dbo.vDmeOrders WHERE OrderId=@id", new { id });
        if (o == null) return NotFound();
        _phi.ComposeCustomerName(o);

        // Snapshot before the update: this action turns an order into a
        // delivered order, a rental, a claim and a serialised unit in one step,
        // so the prior status is the only record of what it was.
        var statusBefore = F.S(o["Status"]);
        var stageBefore = F.S(o["Stage"]);

        _db.Execute("UPDATE dbo.DmeOrders SET Status='delivered', Stage='delivered', PodSignedBy=@sb, PodSignedAt=SYSUTCDATETIME(), PodSignature=@sig WHERE OrderId=@id",
            new { id, sb = signedBy ?? F.S(o["CustomerName"]), sig = (object?)signature ?? DBNull.Value });

        var lines = _db.Query("SELECT * FROM dbo.DmeOrderLines WHERE OrderId=@id", new { id });
        var custId = F.I(o["CustomerId"]);
        var custName = F.S(o["CustomerName"]);
        var deliveryDate = o["DeliveryDate"] ?? (object)DateTime.Today;
        var payer = _db.Scalar("SELECT TOP 1 PayerName FROM dbo.DmeCustomerInsurances WHERE CustomerId=@custId AND Kind='primary'", new { custId });

        // CustomerName and PayerName are stored on the claim on purpose: a
        // submitted claim is a document as filed and must not change if the
        // customer is renamed or switches insurer later. The claim TOTAL is not
        // stored, because a total that can disagree with its own lines is never
        // the right answer. vDmeClaims sums the lines instead.
        var claimNumber = _db.NextNumber("CLM");
        _db.Execute(@"INSERT INTO dbo.DmeClaims (ClaimNumber,OrderId,CustomerId,CustomerName,PayerName,Status,ServiceDate,TenantId)
                        VALUES (@cn,@id,@custId,@custName,@payer,'ready',@sd,@TenantId)",
            new { cn = claimNumber, id, custId, custName = _phi.Encrypt(custName), payer = payer ?? "Self-pay", sd = deliveryDate });
        var claimId = Convert.ToInt32(_db.Scalar("SELECT ClaimId FROM dbo.DmeClaims WHERE ClaimNumber=@cn", new { cn = claimNumber }));

        decimal claimTotal = 0;
        foreach (var l in lines)
        {
            var isRental = F.S(l["Mode"]) == "rental";
            var serial = F.B(l["IsSerialized"]) ? "SN-" + Guid.NewGuid().ToString("N")[..6].ToUpper() : null;
            var charge = isRental ? F.Dec(l["MonthlyRate"]) : F.Dec(l["UnitPrice"]) * F.I(l["Qty"]);
            claimTotal += charge;

            // The rental is created BEFORE its claim line so the line can carry
            // RentalId. That link is the fact with no other home; MonthsBilled
            // is then just a count of the lines pointing at the rental.
            int? rentalId = null;
            if (isRental)
            {
                var cap = _db.Scalar("SELECT CappedRentalMonths FROM dbo.HcpcsCodes WHERE Hcpcs=@h", new { h = F.S(l["Hcpcs"]) });
                var next = Convert.ToDateTime(deliveryDate).AddMonths(1);
                rentalId = Convert.ToInt32(_db.Scalar(@"
                    INSERT INTO dbo.DmeRentals (CustomerId,OrderId,Hcpcs,ItemName,Serial,MonthlyRate,StartDate,NextBillDate,CapMonths,Status,TenantId)
                    OUTPUT inserted.RentalId
                    VALUES (@custId,@id,@h,@n,@s,@mr,@sd,@nb,@cap,'active',@TenantId)",
                    new { custId, id, h = F.S(l["Hcpcs"]), n = F.S(l["ItemName"]), s = serial ?? "—",
                          mr = F.Dec(l["MonthlyRate"]), sd = deliveryDate, nb = next,
                          cap = (object?)(cap == null ? DBNull.Value : F.I(cap)) }));
            }

            _db.Execute("INSERT INTO dbo.DmeClaimLines (ClaimId,Hcpcs,ItemName,Modifier,Units,Charge,RentalId,TenantId) VALUES (@claimId,@h,@n,@mod,@u,@c,@rid,@TenantId)",
                new { claimId, h = F.S(l["Hcpcs"]), n = F.S(l["ItemName"]), mod = F.S(l["Modifiers"]),
                      u = F.I(l["Qty"]), c = charge, rid = (object?)rentalId ?? DBNull.Value });

            _db.Execute("INSERT INTO dbo.DmeSerializedUnits (Hcpcs,ItemName,SerialNumber,Status,CustomerId,InServiceDate,TenantId) VALUES (@h,@n,@s,@st,@custId,@isd,@TenantId)",
                new { h = F.S(l["Hcpcs"]), n = F.S(l["ItemName"]), s = serial ?? "—", st = isRental ? "rented" : "sold", custId, isd = deliveryDate });

            // Stock leaves the warehouse. Recording the movement is what lets
            // on-hand be a SUM rather than a counter somebody has to remember to
            // decrement, which is exactly how the old OnHand column drifted to
            // 14 when one unit was actually in stock.
            _db.Execute(@"INSERT INTO dbo.DmeStockMovements (TenantId,Hcpcs,Qty,Reason,RefType,RefId,Note)
                          VALUES (@TenantId,@h,@qty,'delivery','DmeOrder',@id,@note)",
                new { h = F.S(l["Hcpcs"]), qty = -Math.Max(F.I(l["Qty"]), 1), id, note = "Delivered on " + F.S(o["OrderNumber"]) });
        }

        // One entry covering everything this action produced. Proof of delivery
        // is the record that justifies the billing, so the signer and the claim
        // it generated have to be reconstructable from the log alone.
        await _audit.RecordAsync("DME_ORDER_DELIVERED", "DmeOrder", id,
            before: new { Status = statusBefore, Stage = stageBefore },
            after: new
            {
                Status = "delivered",
                Stage = "delivered",
                PodSignedBy = signedBy ?? custName,
                PodSignatureCaptured = !string.IsNullOrWhiteSpace(signature),
                ClaimNumber = claimNumber,
                ClaimId = claimId,
                ClaimTotal = claimTotal,
                LinesBilled = lines.Count
            });

        return RedirectToAction("Order", new { id });
    }

    // ---------------------------------------------------------------- Rentals
    public IActionResult Rentals()
    {
        ViewData["Title"] = "Rentals";
        ViewData["ActivePage"] = "rentals";
        var rows = _db.Query("SELECT * FROM dbo.vDmeRentals ORDER BY CASE Status WHEN 'active' THEN 0 ELSE 1 END, NextBillDate");
        _phi.ComposeCustomerNames(rows);
        ViewBag.Active = rows.Count(r => F.S(r["Status"]) == "active");
        ViewBag.Mrr = rows.Where(r => F.S(r["Status"]) == "active").Sum(r => F.Dec(r["MonthlyRate"]));
        ViewBag.DueSoon = rows.Count(r => F.S(r["Status"]) == "active" && (F.DaysUntil(r["NextBillDate"]) ?? 99) <= 7);
        return View(rows);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BillNow(int id)
    {
        var r = _db.QueryOne("SELECT * FROM dbo.vDmeRentals WHERE RentalId=@id", new { id });
        if (r == null) return NotFound();
        _phi.ComposeCustomerName(r);
        var billedThrough = r["NextBillDate"] ?? (object)DateTime.Today;
        var monthsBefore = F.I(r["MonthsBilled"]);

        var payer = _db.Scalar("SELECT TOP 1 PayerName FROM dbo.DmeCustomerInsurances WHERE CustomerId=@cid AND Kind='primary'", new { cid = F.I(r["CustomerId"]) });
        var cn = _db.NextNumber("CLM");
        _db.Execute(@"INSERT INTO dbo.DmeClaims (ClaimNumber,OrderId,CustomerId,CustomerName,PayerName,Status,ServiceDate,TenantId)
                        VALUES (@cn,@oid,@cid,@cname,@payer,'ready',@sd,@TenantId)",
            new { cn, oid = (object?)(r["OrderId"] ?? DBNull.Value), cid = F.I(r["CustomerId"]), cname = _phi.Encrypt(F.S(r["CustomerName"])),
                  payer = payer ?? "Self-pay", sd = billedThrough });
        var claimId = Convert.ToInt32(_db.Scalar("SELECT ClaimId FROM dbo.DmeClaims WHERE ClaimNumber=@cn", new { cn }));

        // RentalId on the line is what makes this month countable. Nothing
        // increments a stored MonthsBilled any more, so there is no counter left
        // to drift out of step with the claims actually raised.
        _db.Execute("INSERT INTO dbo.DmeClaimLines (ClaimId,Hcpcs,ItemName,Modifier,Units,Charge,RentalId,TenantId) VALUES (@claimId,@h,@n,'RR',1,@c,@rid,@TenantId)",
            new { claimId, h = F.S(r["Hcpcs"]), n = F.S(r["ItemName"]), c = F.Dec(r["MonthlyRate"]), rid = id });

        var next = Convert.ToDateTime(billedThrough).AddMonths(1);
        _db.Execute("UPDATE dbo.DmeRentals SET NextBillDate=@nb WHERE RentalId=@id", new { nb = next, id });

        await _audit.RecordAsync("DME_RENTAL_BILLED", "DmeRental", id,
            before: new { NextBillDate = billedThrough, MonthsBilled = monthsBefore },
            after: new { NextBillDate = next, MonthsBilled = monthsBefore + 1, ClaimNumber = cn, ClaimId = claimId, Charge = F.Dec(r["MonthlyRate"]) });

        return RedirectToAction("Rentals");
    }

    // ---------------------------------------------------------------- Billing
    public IActionResult Billing(string? status = null)
    {
        ViewData["Title"] = "Billing";
        ViewData["ActivePage"] = "billing";
        var rows = _db.Query("SELECT * FROM dbo.vDmeClaims ORDER BY ServiceDate DESC");
        _phi.ComposeCustomerNames(rows);
        ViewBag.Filter = status ?? "";
        ViewBag.ReadyTotal = rows.Where(r => F.S(r["Status"]) == "ready").Sum(r => F.Dec(r["Total"]));
        ViewBag.SubmittedTotal = rows.Where(r => F.S(r["Status"]) == "submitted").Sum(r => F.Dec(r["Total"]));
        return View(string.IsNullOrEmpty(status) ? rows : rows.Where(r => F.S(r["Status"]) == status).ToList());
    }

    public IActionResult Cms(int id)
    {
        var c = _db.QueryOne("SELECT * FROM dbo.vDmeClaims WHERE ClaimId=@id", new { id });
        if (c == null) return NotFound();
        _phi.ComposeCustomerName(c);
        ViewData["Title"] = "CMS-1500 " + F.S(c["ClaimNumber"]);
        ViewBag.Claim = c;
        ViewBag.Lines = _db.Query("SELECT * FROM dbo.DmeClaimLines WHERE ClaimId=@id", new { id });
        var cust = _db.QueryOne("SELECT * FROM dbo.DmeCustomers WHERE CustomerId=@cid", new { cid = F.I(c["CustomerId"]) });
        _phi.DecryptRow(cust);
        ViewBag.Cust = cust;
        ViewBag.Diagnoses = cust == null ? new List<Dictionary<string, object?>>() :
            _db.Query("SELECT * FROM dbo.DmeCustomerDiagnoses WHERE CustomerId=@cid ORDER BY IsPrimary DESC", new { cid = F.I(cust["CustomerId"]) });
        ViewBag.PrimaryIns = cust == null ? null :
            _db.QueryOne("SELECT TOP 1 * FROM dbo.DmeCustomerInsurances WHERE CustomerId=@cid AND Kind='primary'", new { cid = F.I(cust["CustomerId"]) });
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(int id)
    {
        var claim = _db.QueryOne("SELECT ClaimNumber, Status FROM dbo.DmeClaims WHERE ClaimId=@id", new { id });
        if (claim == null) return NotFound();

        var affected = _db.Execute(
            "UPDATE dbo.DmeClaims SET Status='submitted' WHERE ClaimId=@id AND Status='ready'", new { id });

        // Only log a change that actually happened. Re-posting the form for an
        // already-submitted claim updates nothing, and recording it as a
        // submission would put an event in the trail that never occurred.
        if (affected > 0)
        {
            await _audit.RecordAsync("DME_CLAIM_SUBMITTED", "DmeClaim", id,
                before: new { Status = F.S(claim["Status"]) },
                after: new { Status = "submitted", ClaimNumber = F.S(claim["ClaimNumber"]) });
        }

        return RedirectToAction("Billing");
    }

    // ---------------------------------------------------------------- PHI backfill
    /// <summary>
    /// Encrypt customer rows that are still stored as plaintext, and build their
    /// search index.
    ///
    /// WHY THIS IS AN ACTION AND NOT SQL
    /// The encryption key lives in application configuration and the algorithm
    /// lives in EncryptionHelper, shared with the clinical side. Doing this in
    /// T-SQL would mean a second implementation of the cryptography and the key
    /// sitting in a script file.
    ///
    /// WHY IT IS SAFE TO RUN TWICE
    /// A row whose values are already ciphertext is skipped entirely rather
    /// than decrypted and re-encrypted. That matters more than it looks:
    /// EncryptionHelper.Decrypt returns its input UNCHANGED when decryption
    /// fails, so a decrypt-then-encrypt loop would silently double-encrypt any
    /// value it could not read, and the column would still contain a plausible
    /// looking string afterwards. Checking IsEncrypted first is what makes a
    /// second run a genuine no-op instead of a quiet corruption.
    ///
    /// Restricted to SuperAdmin(0) and ClinicAdmin(1): it reads every customer's
    /// PHI into memory, which is not something a delivery driver's login should
    /// be able to trigger.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "0,1")]
    public async Task<IActionResult> BackfillPhi()
    {
        var customers = _db.Query("SELECT * FROM dbo.DmeCustomers");
        int encrypted = 0, indexed = 0;

        foreach (var c in customers)
        {
            var customerId = F.I(c["CustomerId"]);

            // Already ciphertext? Leave the stored values completely alone. Only
            // the search index is rebuilt, which is cheap and idempotent, and
            // covers a row encrypted before the index existed.
            var alreadyEncrypted = DmeCustomerPhi.EncryptedColumns
                .Select(col => c.TryGetValue(col, out var v) ? v as string : null)
                .Where(v => !string.IsNullOrEmpty(v))
                .All(v => _phi.IsEncrypted(v));

            _phi.DecryptRow(c);

            if (!alreadyEncrypted)
            {
                var sets = string.Join(", ", DmeCustomerPhi.EncryptedColumns.Select(col => $"{col}=@{col}"));
                var parameters = new Dictionary<string, object?> { ["customerId"] = customerId };
                foreach (var col in DmeCustomerPhi.EncryptedColumns)
                {
                    var plain = c.TryGetValue(col, out var v) ? v as string : null;
                    parameters[col] = (object?)_phi.Encrypt(plain) ?? DBNull.Value;
                }

                _db.Execute($"UPDATE dbo.DmeCustomers SET {sets} WHERE CustomerId=@customerId", parameters);
                encrypted++;
            }

            IndexCustomerForSearch(customerId, F.S(c["FirstName"]), F.S(c["LastName"]), F.S(c["Phone"]));
            indexed++;
        }

        // DmeClaims.CustomerName is stored rather than derived (a claim is a
        // document as filed) but it is still a patient name, so it needs the
        // same treatment. Same skip-if-already-encrypted rule.
        var claimsEncrypted = 0;
        foreach (var claim in _db.Query("SELECT ClaimId, CustomerName FROM dbo.DmeClaims"))
        {
            var name = claim["CustomerName"] as string;
            if (string.IsNullOrEmpty(name) || _phi.IsEncrypted(name)) continue;

            _db.Execute("UPDATE dbo.DmeClaims SET CustomerName=@name WHERE ClaimId=@claimId",
                new { name = (object?)_phi.Encrypt(name) ?? DBNull.Value, claimId = F.I(claim["ClaimId"]) });
            claimsEncrypted++;
        }

        await _audit.RecordAsync("DME_PHI_BACKFILL", "DmeCustomer", null,
            before: null,
            after: new { CustomersEncrypted = encrypted, CustomersIndexed = indexed, ClaimNamesEncrypted = claimsEncrypted });

        TempData["BackfillResult"] =
            $"Encrypted {encrypted} customer record(s) and {claimsEncrypted} claim name(s), " +
            $"rebuilt {indexed} search index entries.";
        return RedirectToAction("Customers");
    }
}
