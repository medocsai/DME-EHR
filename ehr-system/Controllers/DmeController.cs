using Microsoft.AspNetCore.Mvc;
using EHR.Helpers;

namespace EHR.Controllers;

/// <summary>
/// DME product — customers, inventory, orders/delivery, rentals, billing,
/// and the operations dashboard. Backed by the DME-native tables in DMEEHR
/// via DmeDb (raw ADO), served on the converted app shell.
/// </summary>
public class DmeController : Controller
{
    // ---------------------------------------------------------------- Dashboard
    public IActionResult Dashboard()
    {
        ViewData["Title"] = "Dashboard";
        ViewData["ActivePage"] = "dashboard";

        var rentals = DmeDb.Query("SELECT * FROM dbo.DmeRentals WHERE Status='active' ORDER BY NextBillDate");
        var orders = DmeDb.Query("SELECT TOP 6 * FROM dbo.DmeOrders ORDER BY CreatedAt DESC");
        var claims = DmeDb.Query("SELECT * FROM dbo.DmeClaims");

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
        foreach (var o in DmeDb.Query("SELECT CustomerName, DeliveryDate FROM dbo.DmeOrders WHERE DeliveryDate IS NOT NULL"))
        {
            var d = Convert.ToDateTime(o["DeliveryDate"]).Date.AddHours(11);
            ev.Add(new { title = "Delivery · " + F.S(o["CustomerName"]), start = Iso(d), end = Iso(d.AddHours(1)), backgroundColor = BLUE, borderColor = BLUE, extendedProps = new { type = "Delivery" } });
        }
        // Rental bill-due markers (all-day)
        foreach (var r in DmeDb.Query("SELECT CustomerName, NextBillDate FROM dbo.DmeRentals WHERE Status='active' AND NextBillDate IS NOT NULL"))
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
        var sql = @"SELECT c.*, (SELECT TOP 1 PayerName FROM dbo.DmeCustomerInsurances i WHERE i.CustomerId=c.CustomerId AND i.Kind='primary') AS PrimaryPayer
                    FROM dbo.DmeCustomers c";
        List<Dictionary<string, object?>> rows;
        if (!string.IsNullOrWhiteSpace(q))
        {
            sql += " WHERE c.FirstName LIKE @q OR c.LastName LIKE @q OR c.AccountNo LIKE @q OR c.Phone LIKE @q";
            rows = DmeDb.Query(sql + " ORDER BY c.LastName", new { q = "%" + q + "%" });
        }
        else rows = DmeDb.Query(sql + " ORDER BY c.LastName");
        ViewBag.Query = q ?? "";
        return View(rows);
    }

    public IActionResult Customer(int id)
    {
        ViewData["ActivePage"] = "customers";
        var c = DmeDb.QueryOne("SELECT * FROM dbo.DmeCustomers WHERE CustomerId=@id", new { id });
        if (c == null) return NotFound();
        ViewData["Title"] = F.S(c["FirstName"]) + " " + F.S(c["LastName"]);
        ViewBag.Customer = c;
        ViewBag.Insurances = DmeDb.Query("SELECT * FROM dbo.DmeCustomerInsurances WHERE CustomerId=@id ORDER BY CASE Kind WHEN 'primary' THEN 0 ELSE 1 END", new { id });
        ViewBag.Diagnoses = DmeDb.Query("SELECT * FROM dbo.DmeCustomerDiagnoses WHERE CustomerId=@id ORDER BY IsPrimary DESC", new { id });
        ViewBag.Equipment = DmeDb.Query("SELECT * FROM dbo.DmeSerializedUnits WHERE CustomerId=@id", new { id });
        ViewBag.Orders = DmeDb.Query("SELECT * FROM dbo.DmeOrders WHERE CustomerId=@id ORDER BY CreatedAt DESC", new { id });
        return View();
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
        ViewBag.Payers = DmeDb.Query("SELECT Name, PayerCode FROM dbo.DmePayers ORDER BY Name");
        ViewBag.Icd = IcdList;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult CreateCustomer(
        string firstName, string lastName, DateTime? dob, string? gender, string? ssnLast4,
        int? heightInches, int? weightLbs, string? phone, string? email,
        string? addressLine1, string? city, string? state, string? zip,
        string? emergencyName, string? emergencyRel, string? emergencyPhone,
        string? insPayer, string? insMemberId, string? insGroup, decimal insCopay, int insCoins, decimal insDeductible,
        string? dxCode)
    {
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            return RedirectToAction("NewCustomer");

        var acct = DmeDb.NextNumber("LMS", 4);
        DmeDb.Execute(@"INSERT INTO dbo.DmeCustomers
            (AccountNo,FirstName,LastName,Dob,Gender,SsnLast4,HeightInches,WeightLbs,Phone,Email,AddressLine1,City,State,Zip,EmergencyName,EmergencyRel,EmergencyPhone,Status)
            VALUES (@acct,@fn,@ln,@dob,@g,@ssn,@h,@w,@ph,@em,@a1,@city,@st,@zip,@en,@er,@ep,'active')",
            new {
                acct, fn = firstName, ln = lastName,
                dob = (object?)dob ?? DBNull.Value, g = (object?)gender ?? DBNull.Value, ssn = (object?)ssnLast4 ?? DBNull.Value,
                h = (object?)heightInches ?? DBNull.Value, w = (object?)weightLbs ?? DBNull.Value,
                ph = (object?)phone ?? DBNull.Value, em = (object?)email ?? DBNull.Value,
                a1 = (object?)addressLine1 ?? DBNull.Value, city = (object?)city ?? DBNull.Value, st = (object?)state ?? DBNull.Value, zip = (object?)zip ?? DBNull.Value,
                en = (object?)emergencyName ?? DBNull.Value, er = (object?)emergencyRel ?? DBNull.Value, ep = (object?)emergencyPhone ?? DBNull.Value
            });
        var custId = Convert.ToInt32(DmeDb.Scalar("SELECT CustomerId FROM dbo.DmeCustomers WHERE AccountNo=@acct", new { acct }));

        if (!string.IsNullOrWhiteSpace(insPayer))
        {
            var payerCode = DmeDb.Scalar("SELECT PayerCode FROM dbo.DmePayers WHERE Name=@n", new { n = insPayer });
            DmeDb.Execute(@"INSERT INTO dbo.DmeCustomerInsurances (CustomerId,Kind,PayerName,PayerId,MemberId,GroupNumber,Copay,Coinsurance,Deductible,SubscriberRel,EligStatus)
                            VALUES (@cid,'primary',@pn,@pid,@mid,@grp,@copay,@coins,@ded,'Self','active')",
                new { cid = custId, pn = insPayer, pid = (object?)payerCode ?? DBNull.Value, mid = (object?)insMemberId ?? DBNull.Value,
                      grp = (object?)insGroup ?? DBNull.Value, copay = insCopay, coins = insCoins, ded = insDeductible });
        }

        if (!string.IsNullOrWhiteSpace(dxCode))
        {
            var desc = IcdList.FirstOrDefault(x => x.code == dxCode).desc;
            DmeDb.Execute("INSERT INTO dbo.DmeCustomerDiagnoses (CustomerId,IcdCode,Description,IsPrimary) VALUES (@cid,@code,@desc,1)",
                new { cid = custId, code = dxCode, desc = (object?)desc ?? DBNull.Value });
        }

        return RedirectToAction("Customer", new { id = custId });
    }

    // ---------------------------------------------------------------- Inventory
    public IActionResult Inventory()
    {
        ViewData["Title"] = "Inventory";
        ViewData["ActivePage"] = "inventory";
        ViewBag.Catalog = DmeDb.Query("SELECT * FROM dbo.HcpcsCodes ORDER BY Category, Hcpcs");
        ViewBag.Units = DmeDb.Query("SELECT * FROM dbo.DmeSerializedUnits ORDER BY Status, Hcpcs");
        return View();
    }

    // ---------------------------------------------------------------- Orders
    public IActionResult Orders()
    {
        ViewData["Title"] = "Orders / Delivery";
        ViewData["ActivePage"] = "orders";
        var rows = DmeDb.Query(@"SELECT o.*, (SELECT COUNT(*) FROM dbo.DmeOrderLines l WHERE l.OrderId=o.OrderId) AS LineCount,
                                        (SELECT ISNULL(SUM(CASE WHEN l.Mode='purchase' THEN l.UnitPrice*l.Qty ELSE l.MonthlyRate END),0) FROM dbo.DmeOrderLines l WHERE l.OrderId=o.OrderId) AS Total
                                 FROM dbo.DmeOrders o ORDER BY o.CreatedAt DESC");
        return View(rows);
    }

    public IActionResult Order(int id)
    {
        ViewData["ActivePage"] = "orders";
        var o = DmeDb.QueryOne("SELECT * FROM dbo.DmeOrders WHERE OrderId=@id", new { id });
        if (o == null) return NotFound();
        ViewData["Title"] = F.S(o["OrderNumber"]);
        ViewBag.Order = o;
        ViewBag.Lines = DmeDb.Query("SELECT * FROM dbo.DmeOrderLines WHERE OrderId=@id", new { id });
        return View();
    }

    [HttpGet]
    public IActionResult NewOrder(int? customerId = null)
    {
        ViewData["Title"] = "New Order";
        ViewData["ActivePage"] = "orders";
        ViewBag.Customers = DmeDb.Query("SELECT CustomerId, AccountNo, FirstName, LastName FROM dbo.DmeCustomers ORDER BY LastName");
        ViewBag.Doctors = DmeDb.Query("SELECT * FROM dbo.DmeDoctors ORDER BY LastName");
        ViewBag.Catalog = DmeDb.Query("SELECT * FROM dbo.HcpcsCodes ORDER BY Category, Hcpcs");
        ViewBag.PreCustomer = customerId;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult CreateOrder(int customerId, int? doctorId, DateTime? deliveryDate, decimal deposit,
                                     string[]? hcpcs, string[]? mode, int[]? qty)
    {
        var cust = DmeDb.QueryOne("SELECT * FROM dbo.DmeCustomers WHERE CustomerId=@customerId", new { customerId });
        if (cust == null) return RedirectToAction("Orders");
        var doc = doctorId.HasValue ? DmeDb.QueryOne("SELECT * FROM dbo.DmeDoctors WHERE DoctorId=@doctorId", new { doctorId }) : null;

        var number = DmeDb.NextNumber("ORD");
        DmeDb.Execute(@"INSERT INTO dbo.DmeOrders (OrderNumber,CustomerId,CustomerName,Status,Stage,DoctorId,DoctorName,DeliveryDate,Deposit)
                        VALUES (@number,@customerId,@cn,'confirmed','order-ship',@doctorId,@dn,@dd,@deposit)",
            new {
                number, customerId,
                cn = F.S(cust["FirstName"]) + " " + F.S(cust["LastName"]),
                doctorId = (object?)doctorId ?? DBNull.Value,
                dn = doc == null ? "" : "Dr. " + F.S(doc["FirstName"]) + " " + F.S(doc["LastName"]),
                dd = (object?)deliveryDate ?? DBNull.Value,
                deposit
            });
        var orderId = Convert.ToInt32(DmeDb.Scalar("SELECT OrderId FROM dbo.DmeOrders WHERE OrderNumber=@number", new { number }));

        if (hcpcs != null)
        {
            for (int i = 0; i < hcpcs.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(hcpcs[i])) continue;
                var item = DmeDb.QueryOne("SELECT * FROM dbo.HcpcsCodes WHERE Hcpcs=@h", new { h = hcpcs[i] });
                if (item == null) continue;
                var m = (mode != null && i < mode.Length) ? mode[i] : "purchase";
                var q = (qty != null && i < qty.Length && qty[i] > 0) ? qty[i] : 1;
                DmeDb.Execute(@"INSERT INTO dbo.DmeOrderLines (OrderId,Hcpcs,ItemName,Category,Mode,Qty,UnitPrice,MonthlyRate,Modifiers,IsSerialized)
                                VALUES (@orderId,@h,@name,@cat,@m,@q,@up,@mr,@mods,@ser)",
                    new {
                        orderId, h = hcpcs[i], name = F.S(item["Name"]), cat = F.S(item["Category"]), m, q,
                        up = m == "purchase" ? F.Dec(item["PurchasePrice"]) : 0m,
                        mr = m == "purchase" ? 0m : F.Dec(item["MonthlyRate"]),
                        mods = m == "purchase" ? "NU" : "RR",
                        ser = F.B(item["IsSerialized"])
                    });
            }
        }
        return RedirectToAction("Order", new { id = orderId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Deliver(int id, string signedBy, string? signature)
    {
        var o = DmeDb.QueryOne("SELECT * FROM dbo.DmeOrders WHERE OrderId=@id", new { id });
        if (o == null) return NotFound();
        DmeDb.Execute("UPDATE dbo.DmeOrders SET Status='delivered', Stage='delivered', PodSignedBy=@sb, PodSignedAt=SYSUTCDATETIME(), PodSignature=@sig WHERE OrderId=@id",
            new { id, sb = signedBy ?? F.S(o["CustomerName"]), sig = (object?)signature ?? DBNull.Value });

        var lines = DmeDb.Query("SELECT * FROM dbo.DmeOrderLines WHERE OrderId=@id", new { id });
        var custId = F.I(o["CustomerId"]);
        var custName = F.S(o["CustomerName"]);
        var deliveryDate = o["DeliveryDate"] ?? (object)DateTime.Today;
        var payer = DmeDb.Scalar("SELECT TOP 1 PayerName FROM dbo.DmeCustomerInsurances WHERE CustomerId=@custId AND Kind='primary'", new { custId });

        decimal claimTotal = 0;
        var claimNumber = DmeDb.NextNumber("CLM");
        DmeDb.Execute(@"INSERT INTO dbo.DmeClaims (ClaimNumber,OrderId,CustomerId,CustomerName,PayerName,Status,ServiceDate,Total)
                        VALUES (@cn,@id,@custId,@custName,@payer,'ready',@sd,0)",
            new { cn = claimNumber, id, custId, custName, payer = payer ?? "Self-pay", sd = deliveryDate });
        var claimId = Convert.ToInt32(DmeDb.Scalar("SELECT ClaimId FROM dbo.DmeClaims WHERE ClaimNumber=@cn", new { cn = claimNumber }));

        foreach (var l in lines)
        {
            var serial = F.B(l["IsSerialized"]) ? "SN-" + Guid.NewGuid().ToString("N")[..6].ToUpper() : null;
            var charge = F.S(l["Mode"]) == "purchase" ? F.Dec(l["UnitPrice"]) * F.I(l["Qty"]) : F.Dec(l["MonthlyRate"]);
            claimTotal += charge;
            DmeDb.Execute("INSERT INTO dbo.DmeClaimLines (ClaimId,Hcpcs,ItemName,Modifier,Units,Charge) VALUES (@claimId,@h,@n,@mod,@u,@c)",
                new { claimId, h = F.S(l["Hcpcs"]), n = F.S(l["ItemName"]), mod = F.S(l["Modifiers"]), u = F.I(l["Qty"]), c = charge });

            DmeDb.Execute("INSERT INTO dbo.DmeSerializedUnits (Hcpcs,ItemName,SerialNumber,Status,CustomerId,InServiceDate) VALUES (@h,@n,@s,@st,@custId,@isd)",
                new { h = F.S(l["Hcpcs"]), n = F.S(l["ItemName"]), s = serial ?? "—", st = F.S(l["Mode"]) == "purchase" ? "sold" : "rented", custId, isd = deliveryDate });

            if (F.S(l["Mode"]) == "rental")
            {
                var cap = DmeDb.Scalar("SELECT CappedRentalMonths FROM dbo.HcpcsCodes WHERE Hcpcs=@h", new { h = F.S(l["Hcpcs"]) });
                var next = Convert.ToDateTime(deliveryDate).AddMonths(1);
                DmeDb.Execute(@"INSERT INTO dbo.DmeRentals (CustomerId,CustomerName,OrderId,Hcpcs,ItemName,Serial,MonthlyRate,StartDate,NextBillDate,MonthsBilled,CapMonths,Status)
                                VALUES (@custId,@custName,@id,@h,@n,@s,@mr,@sd,@nb,0,@cap,'active')",
                    new { custId, custName, id, h = F.S(l["Hcpcs"]), n = F.S(l["ItemName"]), s = serial ?? "—",
                          mr = F.Dec(l["MonthlyRate"]), sd = deliveryDate, nb = next, cap = (object?)(cap == null ? DBNull.Value : F.I(cap)) });
            }
        }
        DmeDb.Execute("UPDATE dbo.DmeClaims SET Total=@t WHERE ClaimId=@claimId", new { t = claimTotal, claimId });
        return RedirectToAction("Order", new { id });
    }

    // ---------------------------------------------------------------- Rentals
    public IActionResult Rentals()
    {
        ViewData["Title"] = "Rentals";
        ViewData["ActivePage"] = "rentals";
        var rows = DmeDb.Query("SELECT * FROM dbo.DmeRentals ORDER BY CASE Status WHEN 'active' THEN 0 ELSE 1 END, NextBillDate");
        ViewBag.Active = rows.Count(r => F.S(r["Status"]) == "active");
        ViewBag.Mrr = rows.Where(r => F.S(r["Status"]) == "active").Sum(r => F.Dec(r["MonthlyRate"]));
        ViewBag.DueSoon = rows.Count(r => F.S(r["Status"]) == "active" && (F.DaysUntil(r["NextBillDate"]) ?? 99) <= 7);
        return View(rows);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult BillNow(int id)
    {
        var r = DmeDb.QueryOne("SELECT * FROM dbo.DmeRentals WHERE RentalId=@id", new { id });
        if (r == null) return NotFound();
        var payer = DmeDb.Scalar("SELECT TOP 1 PayerName FROM dbo.DmeCustomerInsurances WHERE CustomerId=@cid AND Kind='primary'", new { cid = F.I(r["CustomerId"]) });
        var cn = DmeDb.NextNumber("CLM");
        DmeDb.Execute(@"INSERT INTO dbo.DmeClaims (ClaimNumber,OrderId,CustomerId,CustomerName,PayerName,Status,ServiceDate,Total)
                        VALUES (@cn,@oid,@cid,@cname,@payer,'ready',@sd,@total)",
            new { cn, oid = (object?)(r["OrderId"] ?? DBNull.Value), cid = F.I(r["CustomerId"]), cname = F.S(r["CustomerName"]),
                  payer = payer ?? "Self-pay", sd = r["NextBillDate"] ?? (object)DateTime.Today, total = F.Dec(r["MonthlyRate"]) });
        var claimId = Convert.ToInt32(DmeDb.Scalar("SELECT ClaimId FROM dbo.DmeClaims WHERE ClaimNumber=@cn", new { cn }));
        DmeDb.Execute("INSERT INTO dbo.DmeClaimLines (ClaimId,Hcpcs,ItemName,Modifier,Units,Charge) VALUES (@claimId,@h,@n,'RR',1,@c)",
            new { claimId, h = F.S(r["Hcpcs"]), n = F.S(r["ItemName"]), c = F.Dec(r["MonthlyRate"]) });
        var next = Convert.ToDateTime(r["NextBillDate"] ?? (object)DateTime.Today).AddMonths(1);
        DmeDb.Execute("UPDATE dbo.DmeRentals SET MonthsBilled=MonthsBilled+1, NextBillDate=@nb WHERE RentalId=@id", new { nb = next, id });
        return RedirectToAction("Rentals");
    }

    // ---------------------------------------------------------------- Billing
    public IActionResult Billing(string? status = null)
    {
        ViewData["Title"] = "Billing";
        ViewData["ActivePage"] = "billing";
        var rows = DmeDb.Query("SELECT * FROM dbo.DmeClaims ORDER BY ServiceDate DESC");
        ViewBag.Filter = status ?? "";
        ViewBag.ReadyTotal = rows.Where(r => F.S(r["Status"]) == "ready").Sum(r => F.Dec(r["Total"]));
        ViewBag.SubmittedTotal = rows.Where(r => F.S(r["Status"]) == "submitted").Sum(r => F.Dec(r["Total"]));
        return View(string.IsNullOrEmpty(status) ? rows : rows.Where(r => F.S(r["Status"]) == status).ToList());
    }

    public IActionResult Cms(int id)
    {
        var c = DmeDb.QueryOne("SELECT * FROM dbo.DmeClaims WHERE ClaimId=@id", new { id });
        if (c == null) return NotFound();
        ViewData["Title"] = "CMS-1500 " + F.S(c["ClaimNumber"]);
        ViewBag.Claim = c;
        ViewBag.Lines = DmeDb.Query("SELECT * FROM dbo.DmeClaimLines WHERE ClaimId=@id", new { id });
        var cust = DmeDb.QueryOne("SELECT * FROM dbo.DmeCustomers WHERE CustomerId=@cid", new { cid = F.I(c["CustomerId"]) });
        ViewBag.Cust = cust;
        ViewBag.Diagnoses = cust == null ? new List<Dictionary<string, object?>>() :
            DmeDb.Query("SELECT * FROM dbo.DmeCustomerDiagnoses WHERE CustomerId=@cid ORDER BY IsPrimary DESC", new { cid = F.I(cust["CustomerId"]) });
        ViewBag.PrimaryIns = cust == null ? null :
            DmeDb.QueryOne("SELECT TOP 1 * FROM dbo.DmeCustomerInsurances WHERE CustomerId=@cid AND Kind='primary'", new { cid = F.I(cust["CustomerId"]) });
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Submit(int id)
    {
        DmeDb.Execute("UPDATE dbo.DmeClaims SET Status='submitted' WHERE ClaimId=@id AND Status='ready'", new { id });
        return RedirectToAction("Billing");
    }
}
