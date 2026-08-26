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
    private readonly IDmePaymentService _payments;
    private readonly IDmeSftpAccountService _sftp;
    private readonly IDmePayerCatalog _payers;
    private readonly IDmeIcdCatalog _icd;

    public DmeController(IDmeDb db, IDmeAudit audit, DmeCustomerPhi phi,
                         IDmePaymentService payments, IDmeSftpAccountService sftp,
                         IDmePayerCatalog payers, IDmeIcdCatalog icd)
    {
        _icd = icd;
        _db = db;
        _audit = audit;
        _phi = phi;
        _payments = payments;
        _sftp = sftp;
        _payers = payers;
    }

    /// <summary>
    /// The supplier this tenant bills as, assembled by vDmeBillingProvider from
    /// the tenant record plus the three DME-only fields. Null only if the
    /// supplier profile row is missing, which the migration prevents.
    ///
    /// The view is scoped by its INNER JOIN to DmeSupplierProfile, which row
    /// level security filters. dbo.Tenants itself is not in the policy, so
    /// reading Tenants directly here would return every tenant on the server.
    /// </summary>
    private Dictionary<string, object?>? BillingProvider()
        => _db.QueryOne("SELECT * FROM dbo.vDmeBillingProvider");

    /// <summary>
    /// SQL fragment that narrows a read to the branch the caller is looking at,
    /// or leaves it alone when they are looking at all of them.
    ///
    /// It reads @LocationId, which DmeDb puts on every command, so a caller
    /// never passes it. The `@LocationId IS NULL` half is the all-branches
    /// roll-up: the owner of a three-depot supplier needs the whole business in
    /// one number, and that is why location is a filter here rather than a row
    /// level security predicate in the database.
    ///
    /// Every table it is applied to DERIVES LocationId from the customer, except
    /// inventory, which owns its own because stock is physical.
    ///
    /// It now also carries what the caller MAY see, from dbo.UserLocations, so a
    /// restricted user cannot reach another depot by switching to it. That half
    /// is composed in DmeDb rather than here, because forgetting it on one query
    /// is the whole failure mode.
    /// </summary>
    private string LocationFilter => " " + _db.LocationScope() + " ";

    /// <summary>
    /// The branches this tenant has, and which one is being viewed, for the
    /// header switcher and the pickers on create forms.
    /// </summary>
    private void LoadLocationContext()
    {
        // Only the branches this caller may see. Listing all of them would put
        // depots a restricted user cannot open into the New Customer picker,
        // where choosing one produces a refusal instead of a customer.
        ViewBag.Locations = _db.Query(
            "SELECT LocationId, Name, IsPrimary FROM dbo.Locations " +
            "WHERE TenantId=@TenantId AND IsActive=1 AND " + _db.LocationGrants() +
            " ORDER BY IsPrimary DESC, Name");
        ViewBag.CurrentLocationId = _db.LocationId;
    }

    // ---------------------------------------------------------------- Dashboard
    /// <summary>
    /// Operations overview, plus the three monthly money figures the client
    /// asked for: amount paid, amount denied and the most frequent denial code.
    ///
    /// <paramref name="month"/> is yyyy-MM and defaults to the current month.
    /// All three figures are by POSTING date, so they reconcile against a bank
    /// statement for the same period; the screen says so, because an accountant
    /// reading a number has to know which question it answers.
    /// </summary>
    public IActionResult Dashboard(string? month = null)
    {
        ViewData["Title"] = "Dashboard";
        ViewData["ActivePage"] = "dashboard";

        var selectedMonth = ParseMonth(month);
        ViewBag.Money = _payments.MonthlySummary(selectedMonth);
        ViewBag.SelectedMonth = selectedMonth;
        ViewBag.MonthOptions = Enumerable.Range(0, 12)
            .Select(i => new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-i))
            .ToList();

        LoadLocationContext();

        var rentals = _db.Query("SELECT * FROM dbo.vDmeRentals WHERE Status='active' AND" + LocationFilter + "ORDER BY NextBillDate");
        var orders = _db.Query("SELECT TOP 6 * FROM dbo.vDmeOrders WHERE" + LocationFilter + "ORDER BY CreatedAt DESC");
        var claims = _db.Query("SELECT * FROM dbo.vDmeClaims WHERE" + LocationFilter);

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
        var deliveries = _db.Query("SELECT CustomerFirstName, CustomerLastName, DeliveryDate FROM dbo.vDmeOrders WHERE DeliveryDate IS NOT NULL AND" + LocationFilter);
        _phi.ComposeCustomerNames(deliveries);
        foreach (var o in deliveries)
        {
            var d = Convert.ToDateTime(o["DeliveryDate"]).Date.AddHours(11);
            ev.Add(new { title = "Delivery · " + F.S(o["CustomerName"]), start = Iso(d), end = Iso(d.AddHours(1)), backgroundColor = BLUE, borderColor = BLUE, extendedProps = new { type = "Delivery" } });
        }
        // Rental bill-due markers (all-day)
        var billDue = _db.Query("SELECT CustomerFirstName, CustomerLastName, NextBillDate FROM dbo.vDmeRentals WHERE Status='active' AND NextBillDate IS NOT NULL AND" + LocationFilter);
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
        LoadLocationContext();
        // Location is on the customer, which is the one place it is stored. The
        // join gives the screen a branch name without a second copy of the id.
        // No longer const: the branch predicate is composed per request from who
        // the caller is and what they are allowed to see.
        var baseSql = @"
            SELECT c.*, l.Name AS LocationName,
                   (SELECT TOP 1 PayerName FROM dbo.DmeCustomerInsurances i
                    WHERE i.CustomerId=c.CustomerId AND i.Kind='primary') AS PrimaryPayer
            FROM dbo.DmeCustomers c
            LEFT JOIN dbo.Locations l ON l.LocationId = c.LocationId
            WHERE " + _db.LocationScope("c.LocationId");

        List<Dictionary<string, object?>> rows;
        if (!string.IsNullOrWhiteSpace(q))
        {
            // Names and phone numbers are ciphertext, so LIKE cannot match them.
            // The search term is hashed the same way the stored prefix tokens
            // were, and the match happens on hashes. AccountNo stays a plain
            // LIKE because it is not PHI and is not encrypted.
            // Bracketed, because BaseSql already carries the location filter and
            // an unbracketed OR would let a search result from another branch
            // through. AND binds tighter than OR, so without the brackets this
            // reads as "(location AND accountNo) OR (name matches anywhere)".
            rows = _db.Query(baseSql + @"
                AND (c.AccountNo LIKE @accountLike
                     OR EXISTS (SELECT 1 FROM dbo.DmeCustomerSearchTokens t
                                WHERE t.CustomerId = c.CustomerId AND t.TokenHash = @hash))
                ORDER BY c.LastName",
                new { accountLike = "%" + q + "%", hash = (object?)_phi.SearchHash(q) ?? DBNull.Value });
        }
        else rows = _db.Query(baseSql);

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

    [HttpGet]
    public IActionResult NewCustomer()
    {
        ViewData["Title"] = "New Customer";
        ViewData["ActivePage"] = "customers";
        // Neither the payer list nor the diagnosis list is sent to the view.
        // They are Office Ally's 4,017 payers and CMS's 74,719 ICD-10-CM codes,
        // searched through /Lookups as the operator types rather than rendered
        // into a <select>.
        // A customer belongs to a branch, and it is the ONE place a location is
        // stored for people, so the form has to ask. Defaults to the branch
        // being viewed; when that is "all branches" there is no sensible default
        // and the picker starts on the primary.
        LoadLocationContext();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCustomer(
        string firstName, string lastName, string? dob, string? gender, string? ssnLast4,
        int? heightInches, int? weightLbs, string? phone, string? email,
        string? addressLine1, string? city, string? state, string? zip,
        string? emergencyName, string? emergencyRel, string? emergencyPhone,
        int insPayerId, string? insMemberId, string? insGroup, decimal insCopay, int insCoins, decimal insDeductible,
        string? dxCode, int locationId = 0)
    {
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
            return RedirectToAction("NewCustomer");

        // The branch is checked against this tenant rather than trusted from the
        // form. A posted id from another supplier would otherwise plant a
        // customer in their branch: the row level security policy covers
        // TenantId, and would happily accept a foreign LocationId beside it.
        var branch = _db.Scalar(
            "SELECT LocationId FROM dbo.Locations WHERE LocationId=@locationId AND TenantId=@TenantId AND IsActive=1",
            new { locationId });

        if (branch == null)
        {
            TempData["CustomerError"] = "Choose which location this customer belongs to.";
            return RedirectToAction("NewCustomer");
        }

        // PHI is encrypted before it reaches the database. Nothing downstream
        // gets a chance to forget: the encrypted column list lives in
        // DmeCustomerPhi, not here. Blank values pass through untouched so an
        // empty column stays empty rather than becoming ciphertext for "".
        object Enc(string? v) => (object?)_phi.Encrypt(v) ?? DBNull.Value;

        var acct = _db.NextNumber("LMS", 4);
        var custId = Convert.ToInt32(_db.Scalar(@"
            INSERT INTO dbo.DmeCustomers
            (AccountNo,FirstName,LastName,Dob,Gender,SsnLast4,HeightInches,WeightLbs,Phone,Email,AddressLine1,City,State,Zip,EmergencyName,EmergencyRel,EmergencyPhone,Status,TenantId,LocationId)
            OUTPUT inserted.CustomerId
            VALUES (@acct,@fn,@ln,@dob,@g,@ssn,@h,@w,@ph,@em,@a1,@city,@st,@zip,@en,@er,@ep,'active',@TenantId,@branchId)",
            new {
                acct,
                // @branchId, NOT @locationId. DmeDb puts an @LocationId on every
                // command holding the branch the caller is VIEWING, and SQL
                // parameter names are case insensitive, so a parameter called
                // locationId here is the same name. This statement never passed
                // one, so the insert silently took the viewed branch instead of
                // the chosen one: file a customer into Dallas while looking at
                // Houston and they landed in Houston. Viewing ALL branches made
                // it NULL and the save failed outright with a 500.
                branchId = Convert.ToInt32(branch),
                fn = Enc(firstName), ln = Enc(lastName),
                // Typed or pasted, in any of the spellings DateInput accepts.
                dob = Enc(DmeCustomerPhi.FormatDob(DateInput.Parse(dob))), g = (object?)gender ?? DBNull.Value,
                ssn = Enc(ssnLast4),
                h = (object?)heightInches ?? DBNull.Value, w = (object?)weightLbs ?? DBNull.Value,
                ph = Enc(phone), em = Enc(email),
                a1 = Enc(addressLine1), city = Enc(city), st = Enc(state), zip = Enc(zip),
                en = Enc(emergencyName), er = Enc(emergencyRel), ep = Enc(emergencyPhone)
            }));

        // Index the plaintext (which only exists here, in memory) so the
        // customer stays findable once the row is ciphertext.
        IndexCustomerForSearch(custId, firstName, lastName, phone);

        // The form posts only the catalog id. The payer's name and the Payer ID
        // an 837 is addressed to are read back from the catalog here, never
        // taken from the browser: a posted name would let a typo, or a forged
        // field, become the payer a claim is billed to.
        //
        // Both are then STORED on the insurance record rather than referenced by
        // id, and that is deliberate. It is the point-in-time record of who this
        // customer was insured with, and it must not move if Office Ally later
        // corrects a name in the catalog.
        var payer = _payers.Find(insPayerId);
        if (payer != null)
        {
            _db.Execute(@"INSERT INTO dbo.DmeCustomerInsurances (CustomerId,Kind,PayerName,PayerId,MemberId,GroupNumber,Copay,Coinsurance,Deductible,SubscriberRel,EligStatus,TenantId)
                            VALUES (@cid,'primary',@pn,@pid,@mid,@grp,@copay,@coins,@ded,'Self','active',@TenantId)",
                new { cid = custId, pn = payer.Name, pid = payer.PayerCode, mid = (object?)insMemberId ?? DBNull.Value,
                      grp = (object?)insGroup ?? DBNull.Value, copay = insCopay, coins = insCoins, ded = insDeductible });
        }

        // Same rule as the payer: the browser posts a code, and the description
        // filed against the customer is read out of the catalog. A code that is
        // not valid ICD-10-CM files no diagnosis at all rather than one with a
        // blank description, which is what the old hardcoded list produced for
        // anything outside its twelve entries.
        //
        // The description is STORED, not joined, because it is the point-in-time
        // record of what was billed: CMS rewords codes every October.
        var diagnosis = _icd.Find(dxCode);
        if (diagnosis != null)
        {
            _db.Execute("INSERT INTO dbo.DmeCustomerDiagnoses (CustomerId,IcdCode,Description,IsPrimary,TenantId) VALUES (@cid,@code,@desc,1,@TenantId)",
                new { cid = custId, code = diagnosis.Code, desc = diagnosis.Description });
        }

        // Creation has no "before" state. Record the identifying fields only:
        // the full row is retrievable from the customer record, and copying PHI
        // into the audit log would widen the blast radius of a log leak.
        await _audit.RecordAsync("DME_CUSTOMER_CREATED", "DmeCustomer", custId,
            before: null,
            after: new { AccountNo = acct, Payer = payer?.Name, PrimaryDx = diagnosis?.Code });

        return RedirectToAction("Customer", new { id = custId });
    }

    // ---------------------------------------------------------------- Inventory
    public IActionResult Inventory()
    {
        ViewData["Title"] = "Inventory";
        ViewData["ActivePage"] = "inventory";
        LoadLocationContext();
        // The catalog and its prices are tenant data, so OnHand here is the
        // whole company. StockHere is what this branch physically holds, from
        // vDmeStockByLocation. Both matter and they are different questions:
        // "do we own one" and "can I hand one over today".
        ViewBag.Catalog = _db.Query(@"
            SELECT h.*,
                   StockHere = CASE WHEN @LocationId IS NULL THEN NULL
                                    ELSE ISNULL((SELECT s.OnHand FROM dbo.vDmeStockByLocation s
                                                 WHERE s.Hcpcs = h.Hcpcs AND s.LocationId = @LocationId), 0) END
            FROM dbo.vHcpcsCatalog h
            ORDER BY h.Category, h.Hcpcs");
        // Serialised units carry their own LocationId: a wheelchair is at a
        // depot, and no customer owns the ones still in stock.
        ViewBag.Units = _db.Query(@"
            SELECT u.*, l.Name AS LocationName
            FROM dbo.DmeSerializedUnits u
            LEFT JOIN dbo.Locations l ON l.LocationId = u.LocationId
            WHERE " + _db.LocationScope("u.LocationId") + @"
            ORDER BY u.Status, u.Hcpcs");
        return View();
    }

    // ---------------------------------------------------------------- Orders
    public IActionResult Orders()
    {
        ViewData["Title"] = "Orders / Delivery";
        ViewData["ActivePage"] = "orders";
        // LineCount and Total live in the view, so this screen and the order
        // detail screen cannot disagree about what an order is worth.
        var rows = _db.Query("SELECT * FROM dbo.vDmeOrders WHERE" + LocationFilter + "ORDER BY CreatedAt DESC");
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
        // Decrypt before the picker renders, and sort after, for the same reason
        // the customer list does: names are ciphertext, so an unsorted-looking
        // dropdown of base64 is what you get otherwise.
        // The picker offers the branch you are working in, so an order cannot be
        // raised against a customer from a depot you are not looking at.
        var customers = _db.Query("SELECT CustomerId, AccountNo, FirstName, LastName FROM dbo.DmeCustomers WHERE " + _db.LocationScope());
        _phi.DecryptRows(customers);
        ViewBag.Customers = customers
            .OrderBy(c => F.S(c["LastName"]), StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => F.S(c["FirstName"]), StringComparer.OrdinalIgnoreCase)
            .ToList();

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
        // An order with no lines is not an order. Without this guard the form
        // saves an empty ticket that later generates a $0.00 claim, and the
        // client hit exactly that: ORD-01013 in their feedback has no lines and
        // a zero total. The lines are added by JavaScript, so submitting before
        // clicking Add is a normal thing for a real user to do, which is why the
        // check has to be here on the server and not in the page.
        var hasLine = hcpcs?.Any(h => !string.IsNullOrWhiteSpace(h)) == true;
        if (!hasLine)
        {
            TempData["OrderError"] = "Add at least one item before saving the order.";
            return RedirectToAction("NewOrder", new { customerId });
        }

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

        // The branch this delivery comes out of. Taken from vDmeOrders, which
        // derives it from the customer, so it is right whether the person
        // clicking is viewing that branch, another one, or all of them.
        var fulfillingLocation = F.I(o["LocationId"]);

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

            // The unit and the stock movement both belong to the branch that
            // fulfilled the order, which is the CUSTOMER's branch rather than
            // whichever one the person clicking happens to be viewing. A
            // delivery made while looking at "all branches" still has to come
            // out of a real depot's stock.
            _db.Execute("INSERT INTO dbo.DmeSerializedUnits (Hcpcs,ItemName,SerialNumber,Status,CustomerId,InServiceDate,TenantId,LocationId) VALUES (@h,@n,@s,@st,@custId,@isd,@TenantId,@fulfillingLocation)",
                new { h = F.S(l["Hcpcs"]), n = F.S(l["ItemName"]), s = serial ?? "—", st = isRental ? "rented" : "sold", custId, isd = deliveryDate, fulfillingLocation });

            // Stock leaves the warehouse. Recording the movement is what lets
            // on-hand be a SUM rather than a counter somebody has to remember to
            // decrement, which is exactly how the old OnHand column drifted to
            // 14 when one unit was actually in stock.
            _db.Execute(@"INSERT INTO dbo.DmeStockMovements (TenantId,LocationId,Hcpcs,Qty,Reason,RefType,RefId,Note)
                          VALUES (@TenantId,@fulfillingLocation,@h,@qty,'delivery','DmeOrder',@id,@note)",
                new { h = F.S(l["Hcpcs"]), qty = -Math.Max(F.I(l["Qty"]), 1), id, fulfillingLocation, note = "Delivered on " + F.S(o["OrderNumber"]) });
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
        var rows = _db.Query("SELECT * FROM dbo.vDmeRentals WHERE" + LocationFilter + "ORDER BY CASE Status WHEN 'active' THEN 0 ELSE 1 END, NextBillDate");
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
    /// <summary>
    /// The claim list, now with the money.
    ///
    /// Two statuses, deliberately, because they answer two different questions.
    /// Status is the submission lifecycle the application owns (ready then
    /// submitted). PaymentStatus is derived entirely from the payments posted
    /// against the claim and is never stored, so it cannot disagree with them.
    /// <paramref name="status"/> filters on either one.
    /// </summary>
    public IActionResult Billing(string? status = null)
    {
        ViewData["Title"] = "Billing";
        ViewData["ActivePage"] = "billing";
        var rows = _db.Query("SELECT * FROM dbo.vDmeClaims WHERE" + LocationFilter + "ORDER BY ServiceDate DESC");
        _phi.ComposeCustomerNames(rows);
        ViewBag.Filter = status ?? "";
        ViewBag.ReadyTotal = rows.Where(r => F.S(r["Status"]) == "ready").Sum(r => F.Dec(r["Total"]));
        ViewBag.SubmittedTotal = rows.Where(r => F.S(r["Status"]) == "submitted").Sum(r => F.Dec(r["Total"]));
        ViewBag.OutstandingTotal = rows.Sum(r => F.Dec(r["Balance"]));
        ViewBag.PaidTotal = rows.Sum(r => F.Dec(r["PaidTotal"]));
        ViewBag.DeniedTotal = rows.Sum(r => F.Dec(r["DeniedCharge"]));
        ViewBag.DeniedCount = rows.Count(r => F.Dec(r["DeniedCharge"]) > 0);

        // The banner tells the truth about what is and is not wired, from the
        // data rather than from a hardcoded sentence: whether this supplier can
        // legally be billed under, and whether a clearinghouse account exists.
        var provider = BillingProvider();
        ViewBag.SupplierComplete = provider != null && F.B(provider["IsComplete"]);
        ViewBag.SupplierName = provider == null ? "" : F.S(provider["BillingName"]);
        ViewBag.HasClearinghouseAccount = _sftp.List().Any(a => F.B(a["IsActive"]));

        if (string.IsNullOrEmpty(status)) return View(rows);

        // "denied" means anything with a refused line on it. A claim where three
        // lines paid and one was refused is exactly what a biller opens this
        // filter to find, so leaving it out would make the filter lie.
        return View(rows
            .Where(r => status == "denied"
                ? F.Dec(r["DeniedCharge"]) > 0
                : F.S(r["Status"]) == status || F.S(r["PaymentStatus"]) == status)
            .ToList());
    }

    // ---------------------------------------------------------------- Payments
    /// <summary>
    /// Every receipt posted, payer and customer alike, newest first.
    ///
    /// UnappliedAmount is on the list on purpose. A check entered but not fully
    /// allocated to claim lines is the commonest posting mistake there is, and
    /// it makes the dashboard's amount-paid tile read low. Showing the gap is
    /// what stops it being invisible.
    /// </summary>
    public IActionResult Payments()
    {
        ViewData["Title"] = "Payments";
        ViewData["ActivePage"] = "payments";
        var rows = _db.Query("SELECT * FROM dbo.vDmePayments WHERE" + LocationFilter + "ORDER BY PostedDate DESC, PaymentId DESC");
        _phi.ComposeCustomerNames(rows);
        ViewBag.Received = rows.Where(r => !F.B(r["IsVoided"])).Sum(r => F.Dec(r["Amount"]));
        ViewBag.Applied = rows.Where(r => !F.B(r["IsVoided"])).Sum(r => F.Dec(r["AppliedAmount"]));
        ViewBag.Unapplied = rows.Where(r => !F.B(r["IsVoided"])).Sum(r => F.Dec(r["UnappliedAmount"]));
        return View(rows);
    }

    /// <summary>
    /// The posting screen for one claim: every line, what it was billed, what
    /// has already been adjudicated, and what is left.
    /// </summary>
    [HttpGet]
    public IActionResult PostPayment(int id)
    {
        ViewData["ActivePage"] = "billing";
        var claim = _db.QueryOne("SELECT * FROM dbo.vDmeClaims WHERE ClaimId=@id", new { id });
        if (claim == null) return NotFound();
        _phi.ComposeCustomerName(claim);

        ViewData["Title"] = "Post payment " + F.S(claim["ClaimNumber"]);
        ViewBag.Claim = claim;
        // Read through vDmeClaimLines, not the base table: the per-line paid and
        // adjusted figures are computed there, so this screen and the claim
        // detail cannot disagree about what is still outstanding on a line.
        ViewBag.Lines = _db.Query("SELECT * FROM dbo.vDmeClaimLines WHERE ClaimId=@id ORDER BY ClaimLineId", new { id });
        ViewBag.Carc = _db.Query("SELECT Code, Description FROM dbo.DmeCarcCodes ORDER BY LEN(Code), Code");
        ViewBag.History = _db.Query(
            "SELECT * FROM dbo.vDmePaymentLines WHERE ClaimId=@id ORDER BY PostedDate DESC, PaymentLineId DESC",
            new { id });
        return View();
    }

    /// <summary>
    /// Post one receipt against a claim.
    ///
    /// The parallel-array shape is what an HTML form can express: one entry per
    /// claim line, plus a flat adjustment list where adjLine says which line
    /// each adjustment belongs to. The rules live in DmePaymentService, not
    /// here, because an 835 parser will need the same ones without going
    /// through a form.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePayment(
        int claimId, string source, string? payerName, DateTime postedDate, string method,
        string? referenceNumber, decimal amount, string? note,
        int[]? claimLineId, decimal[]? allowed, decimal[]? paid,
        int[]? adjLine, string[]? adjGroup, string[]? adjCode, decimal[]? adjAmount)
    {
        var lines = new List<PaymentLineInput>();
        for (int i = 0; claimLineId != null && i < claimLineId.Length; i++)
        {
            var adjustments = new List<PaymentAdjustmentInput>();
            for (int j = 0; adjLine != null && j < adjLine.Length; j++)
            {
                if (adjLine[j] != i) continue;
                var group = adjGroup != null && j < adjGroup.Length ? adjGroup[j] : "";
                var code = adjCode != null && j < adjCode.Length ? adjCode[j] : "";
                var value = adjAmount != null && j < adjAmount.Length ? adjAmount[j] : 0m;
                if (string.IsNullOrWhiteSpace(group) || string.IsNullOrWhiteSpace(code)) continue;
                adjustments.Add(new PaymentAdjustmentInput(group, code, value));
            }

            lines.Add(new PaymentLineInput(
                claimLineId[i],
                allowed != null && i < allowed.Length ? allowed[i] : 0m,
                paid != null && i < paid.Length ? paid[i] : 0m,
                adjustments));
        }

        // A line the biller left completely blank is not an adjudication. Drop
        // it rather than writing a row of zeroes that would later read as "the
        // payer allowed nothing", which is a denial-shaped statement nobody made.
        lines = lines
            .Where(l => l.PaidAmount != 0 || l.AllowedAmount != 0 || l.Adjustments.Count > 0)
            .ToList();

        // For a customer payment the payer name is not ours to invent.
        var customerId = source == "customer" ? F.I(_db.Scalar(
            "SELECT CustomerId FROM dbo.DmeClaims WHERE ClaimId=@claimId", new { claimId })) : (int?)null;

        var result = await _payments.PostAsync(new PaymentInput(
            source, source == "payer" ? payerName : null, customerId,
            postedDate, method, referenceNumber, amount, note, lines));

        if (!result.Ok)
        {
            TempData["PaymentError"] = result.Error;
            return RedirectToAction("PostPayment", new { id = claimId });
        }

        TempData["PaymentResult"] = $"Posted {result.PaymentNumber}.";
        return RedirectToAction("PostPayment", new { id = claimId });
    }

    /// <summary>
    /// Reverse a posting. Never an edit and never a delete: the entry stays and
    /// stops counting, so the audit trail keeps a record of a payment that was
    /// posted and then taken back.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VoidPayment(int id, string? reason)
    {
        var result = await _payments.VoidAsync(id, reason);
        TempData[result.Ok ? "PaymentResult" : "PaymentError"] =
            result.Ok ? $"Voided {result.PaymentNumber}." : result.Error;
        return RedirectToAction("Payments");
    }

    // ---------------------------------------------------------------- Settings
    /// <summary>
    /// Who this supplier is, and how it connects to the clearinghouse.
    ///
    /// Admin roles only. The page holds the identity every claim is filed under
    /// and the clearinghouse credentials, neither of which is a delivery
    /// driver's business. The credentials themselves never reach this screen:
    /// it reads vDmeSftpAccounts, which does not expose them at all.
    /// </summary>
    [Authorize(Roles = "0,1")]
    public IActionResult Settings()
    {
        ViewData["Title"] = "Settings";
        ViewData["ActivePage"] = "settings";
        ViewBag.Provider = BillingProvider();
        // The clearinghouse section is super admin only. The list is not even
        // fetched for a clinic admin: a section they cannot act on is noise, and
        // hiding it in the view alone would still have loaded the data.
        ViewBag.IsSuperAdmin = User.IsInRole("0");
        ViewBag.SftpAccounts = User.IsInRole("0")
            ? _sftp.List()
            : new List<Dictionary<string, object?>>();
        return View();
    }

    /// <summary>
    /// Save the supplier identity.
    ///
    /// Writes two tables: the shared fields live on Tenants, which every product
    /// on this platform already uses for them, and only PTAN, taxonomy and
    /// accepts-assignment live in DmeSupplierProfile because they have no other
    /// home. Copying the name and address into a DME table would have been six
    /// duplicated columns waiting to disagree.
    ///
    /// The UPDATE on Tenants is explicitly scoped to the caller's tenant. That
    /// WHERE clause is load-bearing in a way the DME tables' is not: Tenants is
    /// the platform registry and is NOT covered by the row level security
    /// policy, so without it this would rename every tenant on the server.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "0,1")]
    public async Task<IActionResult> SaveSupplier(
        string billingName, string? npi, string? taxId, string? ptan, string? taxonomyCode,
        string? address, string? city, string? state, string? zipCode, string? phone,
        bool acceptsAssignment = false)
    {
        if (string.IsNullOrWhiteSpace(billingName))
        {
            TempData["SettingsError"] = "The billing name is what appears on every claim. It cannot be blank.";
            return RedirectToAction("Settings");
        }

        var before = BillingProvider();

        _db.Execute(@"
            UPDATE dbo.Tenants
               SET Name=@billingName, NPI=@npi, TaxId=@taxId,
                   Address=@address, City=@city, State=@state, ZipCode=@zipCode, Phone=@phone,
                   UpdatedAt=SYSUTCDATETIME()
             WHERE TenantId=@TenantId",
            new
            {
                billingName = billingName.Trim(),
                npi = (object?)npi?.Trim() ?? DBNull.Value,
                taxId = (object?)taxId?.Trim() ?? DBNull.Value,
                address = (object?)address?.Trim() ?? DBNull.Value,
                city = (object?)city?.Trim() ?? DBNull.Value,
                state = (object?)state?.Trim() ?? DBNull.Value,
                zipCode = (object?)zipCode?.Trim() ?? DBNull.Value,
                phone = (object?)phone?.Trim() ?? DBNull.Value,
            });

        _db.Execute(@"
            UPDATE dbo.DmeSupplierProfile
               SET Ptan=@ptan, TaxonomyCode=@taxonomyCode, AcceptsAssignment=@acceptsAssignment,
                   UpdatedAt=SYSUTCDATETIME()
             WHERE TenantId=@TenantId",
            new
            {
                ptan = (object?)ptan?.Trim() ?? DBNull.Value,
                taxonomyCode = (object?)taxonomyCode?.Trim() ?? DBNull.Value,
                acceptsAssignment,
            });

        var after = BillingProvider();
        await _audit.RecordAsync("DME_SUPPLIER_UPDATED", "DmeSupplierProfile", _db.TenantId,
            before: before == null ? null : new { Name = F.S(before["BillingName"]), Npi = F.S(before["Npi"]), TaxId = F.S(before["TaxId"]), Ptan = F.S(before["Ptan"]) },
            after: after == null ? null : new { Name = F.S(after["BillingName"]), Npi = F.S(after["Npi"]), TaxId = F.S(after["TaxId"]), Ptan = F.S(after["Ptan"]) });

        TempData["SettingsResult"] = "Supplier details saved. Every claim form now bills as " + billingName.Trim() + ".";
        return RedirectToAction("Settings");
    }

    /// <summary>
    /// Save a clearinghouse SFTP account. The rules and the encryption live in
    /// DmeSftpAccountService, because the 837 sender will need the same ones.
    ///
    /// SUPER ADMIN (role 0) ONLY, unlike the supplier identity above.
    /// The two look like one settings page and are not the same kind of data.
    /// The supplier's name and NPI are the supplier's own business and a clinic
    /// admin maintains them. The clearinghouse credential is issued during an
    /// onboarding that we run, it is the key to filing claims as this supplier,
    /// and a clinic admin has no occasion to touch it. Narrower is correct here:
    /// nobody is blocked from work they actually do.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "0")]
    public async Task<IActionResult> SaveSftpAccount(
        int sftpAccountId, string label, string host, int port,
        string? username, string? password, bool isTestMode = false, bool isActive = false)
    {
        var result = await _sftp.SaveAsync(new SftpAccountInput(
            sftpAccountId, label, host, port, username, password, isTestMode, isActive));

        TempData[result.Ok ? "SettingsResult" : "SettingsError"] =
            result.Ok ? "Clearinghouse account saved." : result.Error;
        return RedirectToAction("Settings");
    }

    /// <summary>
    /// Take a clearinghouse account in or out of service. Never a delete.
    /// Super admin only, for the same reason as SaveSftpAccount.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "0")]
    public async Task<IActionResult> SetSftpActive(int id, bool isActive)
    {
        var result = await _sftp.SetActiveAsync(id, isActive);
        TempData[result.Ok ? "SettingsResult" : "SettingsError"] =
            result.Ok ? (isActive ? "Account is back in service." : "Account taken out of service.") : result.Error;
        return RedirectToAction("Settings");
    }

    /// <summary>
    /// Parse a yyyy-MM month selector, falling back to the current month.
    ///
    /// Invariant culture on purpose: the value round-trips through a query
    /// string, and parsing it with the server's regional settings would make
    /// the dashboard show a different month on a differently configured host.
    /// </summary>
    private static DateTime ParseMonth(string? month)
    {
        if (!string.IsNullOrWhiteSpace(month) &&
            DateTime.TryParseExact(month, "yyyy-MM",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var parsed))
        {
            return new DateTime(parsed.Year, parsed.Month, 1);
        }
        return new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
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
        // Box 33 used to be a hardcoded string in the view, so every claim this
        // product produced carried a made up NPI. It comes from the supplier
        // record now, and the screen says plainly when that record is not
        // filled in rather than printing something plausible.
        ViewBag.Provider = BillingProvider();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(int id)
    {
        var claim = _db.QueryOne("SELECT ClaimNumber, Status FROM dbo.DmeClaims WHERE ClaimId=@id", new { id });
        if (claim == null) return NotFound();

        // A claim filed under a supplier with no NPI or tax ID is rejected by
        // the payer at best and misattributed at worst. The check is here rather
        // than only on the settings page because this is the point of no return:
        // once a claim is marked submitted it is a document that went out.
        var provider = BillingProvider();
        if (provider == null || !F.B(provider["IsComplete"]))
        {
            TempData["BillingError"] =
                "This claim cannot be submitted until the supplier's billing name, NPI and tax ID are filled in. " +
                "An administrator sets them under Settings.";
            return RedirectToAction("Billing");
        }

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
