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
    private readonly IDmeDistributors _distributors;
    private readonly IDmeDoctors _doctors;
    private readonly IDmeOrderDocuments _documents;
    private readonly IDmeCustomerDocuments _customerDocs;

    public DmeController(IDmeDb db, IDmeAudit audit, DmeCustomerPhi phi,
                         IDmePaymentService payments, IDmeSftpAccountService sftp,
                         IDmePayerCatalog payers, IDmeIcdCatalog icd,
                         IDmeDistributors distributors, IDmeOrderDocuments documents,
                         IDmeCustomerDocuments customerDocs,
                         IDmeDoctors doctors)
    {
        _icd = icd;
        _distributors = distributors;
        _doctors = doctors;
        _documents = documents;
        _customerDocs = customerDocs;
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

        // Two colours, because two things are on this calendar and both come out
        // of the database. There used to be a hardcoded week of pickups, setups
        // and service visits here to make the screen look busy: names that were
        // not customers, for work the product cannot record. A calendar that
        // invents its own entries is worse than a thin one, because the first
        // question anybody asks is how to add another.
        const string BLUE = "#2f6bdf", PURPLE = "#7a3ff2";
        var ev = new List<object>();
        string Iso(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm:ss");

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
    public IActionResult Customers(string? q = null, string? status = null)
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

        // Archived customers are hidden unless asked for. A list that shows
        // everybody who ever bought something stops being a working list within
        // a year, which is why archiving exists at all.
        var view = status switch
        {
            "archived" => "archived",
            "all" => "all",
            _ => "active"
        };

        if (view != "all")
            baseSql += " AND c.Status = '" + view + "'";

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
        ViewBag.StatusView = view;
        return View(rows);
    }

    /// <summary>
    /// Take a customer off the working list without destroying them.
    ///
    /// Never deleted: their orders, claims and payments are the record of money
    /// that changed hands, and a customer row that vanished would leave all of
    /// it pointing at nothing. Archiving is the reversible half of the same
    /// idea, exactly like retiring a distributor or voiding a payment.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "0,1")]
    public async Task<IActionResult> ArchiveCustomer(int id)
    {
        var c = _db.QueryOne(
            "SELECT CustomerId, AccountNo, Status FROM dbo.DmeCustomers WHERE CustomerId=@id", new { id });

        if (c == null) return NotFound();

        // Somebody still renting equipment is somebody you are still billing.
        // Archiving them would hide a live obligation from the list the biller
        // works off, and the rental would go on asking to be billed from a
        // screen where the customer no longer appears.
        var live = F.I(_db.Scalar(
            "SELECT COUNT(*) FROM dbo.vDmeRentals WHERE CustomerId=@id AND Status='active'", new { id }));

        if (live > 0)
        {
            TempData["CustomerError"] =
                $"This customer still has {live} active rental(s). End those first.";
            return RedirectToAction("Customer", new { id });
        }

        // Guarded on the current status so two clicks archive once.
        var done = _db.Execute(
            "UPDATE dbo.DmeCustomers SET Status='archived' " +
            "WHERE CustomerId=@id AND TenantId=@TenantId AND Status='active'", new { id }) == 1;

        if (done)
        {
            await _audit.RecordAsync("DME_CUSTOMER_ARCHIVED", "DmeCustomer", id,
                before: new { Status = "active" },
                after: new { Status = "archived", AccountNo = F.S(c["AccountNo"]) });
        }

        return RedirectToAction("Customer", new { id });
    }

    /// <summary>Put an archived customer back on the working list.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "0,1")]
    public async Task<IActionResult> RestoreCustomer(int id)
    {
        var done = _db.Execute(
            "UPDATE dbo.DmeCustomers SET Status='active' " +
            "WHERE CustomerId=@id AND TenantId=@TenantId AND Status='archived'", new { id }) == 1;

        if (done)
        {
            await _audit.RecordAsync("DME_CUSTOMER_RESTORED", "DmeCustomer", id,
                before: new { Status = "archived" }, after: new { Status = "active" });
        }

        return RedirectToAction("Customer", new { id });
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
    /// <summary>
    /// The shape rules for a customer, shared by creating and correcting one so
    /// the two cannot drift apart.
    ///
    /// Each is checked only when something was actually typed, because all of
    /// these are optional: an empty field is a fact ("we do not have it"), a
    /// malformed one is a mistake.
    ///
    /// The browser filters these as they are typed, which is a courtesy and
    /// nothing more. It is turned off, bypassed, or simply not reached by a form
    /// posted from anywhere else, so the rule has to live here as well. State
    /// and ZIP in particular are printed onto the CMS-1500, where a wrong one is
    /// a denial rather than a cosmetic problem.
    ///
    /// Returns null when everything is acceptable.
    /// </summary>
    private static string? CustomerFieldProblem(
        string? dob, string? email, string? ssnLast4, string? state, string? zip)
    {
        static bool Filled(string? v) => !string.IsNullOrWhiteSpace(v);
        static bool Match(string? v, string pattern) =>
            System.Text.RegularExpressions.Regex.IsMatch(v!.Trim(), pattern);

        // A date of birth is optional, so blank is fine. Something that was
        // typed and cannot be read is NOT: DateInput.Parse returns null for
        // both, so without this the customer saves with the date silently
        // dropped and nobody is told. A missing DOB fails eligibility later, a
        // long way from the screen where it was lost.
        if (Filled(dob) && DateInput.Parse(dob) == null)
            return "That date of birth could not be read. Use mm/dd/yyyy.";

        if (Filled(email) && !Match(email, @"^[^\s@]+@[^\s@]+\.[^\s@]{2,}$"))
            return "That email address is not valid.";

        if (Filled(ssnLast4) && !Match(ssnLast4, @"^\d{4}$"))
            return "The SSN box takes the last four digits only.";

        if (Filled(state) && !Match(state, @"^[A-Za-z]{2}$"))
            return "State is the two letter code, for example TX.";

        if (Filled(zip) && !Match(zip, @"^\d{5}$"))
            return "ZIP is five digits.";

        return null;
    }

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

        // The kinds, so the picker on the attachments panel is the same list on
        // both screens even though nothing can be attached until the customer
        // exists. The panel says so.
        ViewBag.DocumentKinds = _customerDocs.Kinds;

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
        int secPayerId, string? secMemberId, string? secGroup, decimal secCopay, int secCoins, decimal secDeductible,
        string[]? dxCodes, int locationId = 0)
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

        // A date of birth is optional, so blank is fine. Something that was typed
        // and cannot be read is NOT fine: DateInput.Parse returns null for both,
        // so without this the customer saves with the date silently dropped and
        // nobody is told. A missing DOB fails eligibility later, a long way from
        // the screen where it was lost.
        var problem = CustomerFieldProblem(dob, email, ssnLast4, state, zip);
        if (problem != null)
        {
            TempData["CustomerError"] = problem;
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

        // The payer and the diagnoses are filed by the same two methods
        // UpdateCustomer calls, because New Customer and Edit customer are one
        // form. Their rules, and the reasons for them, are written once above
        // each method rather than twice here.
        var payer = SaveCustomerInsurance(
            custId, "primary", insPayerId, insMemberId, insGroup, insCopay, insCoins, insDeductible);

        // Secondary is optional and usually absent, so a form with nothing in it
        // writes no row at all rather than an empty one.
        var secondary = SaveCustomerInsurance(
            custId, "secondary", secPayerId, secMemberId, secGroup, secCopay, secCoins, secDeductible);

        var filed = SaveCustomerDiagnoses(custId, dxCodes);

        // Creation has no "before" state. Record the identifying fields only:
        // the full row is retrievable from the customer record, and copying PHI
        // into the audit log would widen the blast radius of a log leak.
        await _audit.RecordAsync("DME_CUSTOMER_CREATED", "DmeCustomer", custId,
            before: null,
            after: new { AccountNo = acct, Payer = payer, Secondary = secondary,
                         PrimaryDx = filed.FirstOrDefault(), DxCount = filed.Count });

        return RedirectToAction("Customer", new { id = custId });
    }

    /// <summary>
    /// The referring doctors this supplier takes orders from.
    ///
    /// The table was seeded with three rows and had no way to add a fourth. The
    /// ordering physician is not optional on DMEPOS: their name and NPI go in
    /// boxes 17 and 17b of the CMS-1500, and a claim missing them is rejected.
    /// </summary>
    public IActionResult Doctors()
    {
        ViewData["Title"] = "Referring doctors";
        ViewData["ActivePage"] = "doctors";
        LoadLocationContext();

        ViewBag.Doctors = _doctors.All(includeRetired: true);
        ViewBag.DoctorError = TempData["DoctorError"];
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "0,1")]
    public async Task<IActionResult> AddDoctor(
        string? firstName, string? lastName, string? npi, string? specialty, string? phone)
    {
        var id = _doctors.Add(firstName, lastName, npi, specialty, phone);
        if (id == null)
        {
            TempData["DoctorError"] = _doctors.LastProblem ?? "That doctor could not be added.";
            return RedirectToAction("Doctors");
        }

        await _audit.RecordAsync("DME_DOCTOR_ADDED", "DmeDoctor", id.Value,
            before: null,
            after: new { Name = $"{firstName?.Trim()} {lastName?.Trim()}", Npi = npi, Specialty = specialty });

        return RedirectToAction("Doctors");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "0,1")]
    public async Task<IActionResult> EditDoctor(
        int id, string? firstName, string? lastName, string? npi, string? specialty, string? phone)
    {
        var before = _doctors.Find(id);
        if (before == null)
        {
            TempData["DoctorError"] = "That doctor is not on your list.";
            return RedirectToAction("Doctors");
        }

        if (!_doctors.Update(id, firstName, lastName, npi, specialty, phone))
        {
            TempData["DoctorError"] = _doctors.LastProblem ?? "That doctor could not be updated.";
            return RedirectToAction("Doctors");
        }

        await _audit.RecordAsync("DME_DOCTOR_EDITED", "DmeDoctor", id,
            before: new { before.FirstName, before.LastName, before.Npi, before.Specialty },
            after: new { FirstName = firstName?.Trim(), LastName = lastName?.Trim(), Npi = npi, Specialty = specialty });

        return RedirectToAction("Doctors");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "0,1")]
    public async Task<IActionResult> RetireDoctor(int id)
    {
        if (!_doctors.Retire(id))
        {
            TempData["DoctorError"] = "That doctor is already out of service.";
            return RedirectToAction("Doctors");
        }

        await _audit.RecordAsync("DME_DOCTOR_RETIRED", "DmeDoctor", id,
            before: new { Retired = false }, after: new { Retired = true });

        return RedirectToAction("Doctors");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "0,1")]
    public async Task<IActionResult> RestoreDoctor(int id)
    {
        if (!_doctors.Restore(id))
        {
            TempData["DoctorError"] = "That doctor is already in service.";
            return RedirectToAction("Doctors");
        }

        await _audit.RecordAsync("DME_DOCTOR_RESTORED", "DmeDoctor", id,
            before: new { Retired = true }, after: new { Retired = false });

        return RedirectToAction("Doctors");
    }

    /// <summary>
    /// Stop a rental, and put the equipment back where it came from.
    ///
    /// A rental could be started and never stopped, so equipment that came back
    /// went on asking to be billed every month from the Rentals screen. Billing
    /// a returned item is a claim for something the customer does not have.
    ///
    /// Three things happen together, and none of them make sense alone:
    ///   the rental stops,
    ///   a serialised unit goes back to stock and stops belonging to anybody,
    ///   the stock ledger gains the item back.
    ///
    /// The ledger entry matters most. On-hand is a SUM over that ledger, not a
    /// counter, so without this row the returned item is invisible to the
    /// business: on the shelf but not in the figures.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "0,1")]
    public async Task<IActionResult> EndRental(int id, string? reason, bool backToStock = true)
    {
        var r = _db.QueryOne("SELECT * FROM dbo.vDmeRentals WHERE RentalId=@id", new { id });
        if (r == null) return NotFound();
        _phi.ComposeCustomerName(r);

        if (F.S(r["Status"]) != "active")
        {
            TempData["RentalError"] = "That rental has already ended.";
            return RedirectToAction("Rentals");
        }

        // A reason, for the same purpose voiding a payment needs one: months
        // from now somebody has to be able to say why the billing stopped.
        if (string.IsNullOrWhiteSpace(reason))
        {
            TempData["RentalError"] = "Say why the rental is ending.";
            return RedirectToAction("Rentals");
        }

        // Guarded on EndedAt IS NULL so two clicks end it once and the date
        // stays the first one. Same shape as retiring a distributor.
        var done = _db.Execute(@"
            UPDATE dbo.DmeRentals
               SET EndedAt = SYSUTCDATETIME(), EndReason = @reason, EndedBy = @userId
             WHERE RentalId=@id AND TenantId=@TenantId AND EndedAt IS NULL",
            new { id, reason = reason.Trim(), userId = CurrentUserId() }) == 1;

        if (!done)
        {
            TempData["RentalError"] = "That rental has already ended.";
            return RedirectToAction("Rentals");
        }

        var serial = F.S(r["Serial"]);
        var hcpcs = F.S(r["Hcpcs"]);

        // backToStock is false when the equipment is not coming back: written
        // off, kept by the customer at the end of a cap, or lost. Saying so is
        // the point. Putting it back regardless would inflate on-hand with
        // equipment nobody has.
        if (backToStock)
        {
            // The unit stops belonging to a customer. "—" is what Deliver
            // writes for a bulk item, which has no unit to return.
            if (!string.IsNullOrWhiteSpace(serial) && serial != "—")
            {
                _db.Execute(@"
                    UPDATE dbo.DmeSerializedUnits
                       SET Status='in-stock', CustomerId=NULL
                     WHERE SerialNumber=@serial AND TenantId=@TenantId",
                    new { serial });
            }

            // The branch the customer belongs to is where it comes back to,
            // which is where it went out from.
            var backTo = _db.Scalar(
                "SELECT LocationId FROM dbo.DmeCustomers WHERE CustomerId=@cid",
                new { cid = F.I(r["CustomerId"]) });

            if (backTo != null)
            {
                _db.Execute(@"INSERT INTO dbo.DmeStockMovements (TenantId,LocationId,Hcpcs,Qty,Reason,RefType,RefId,Note)
                              VALUES (@TenantId,@backTo,@h,1,'return','DmeRental',@id,@note)",
                    new { backTo = Convert.ToInt32(backTo), h = hcpcs, id, note = "Returned from rental: " + reason.Trim() });
            }
        }

        await _audit.RecordAsync("DME_RENTAL_ENDED", "DmeRental", id,
            before: new { Status = "active", MonthsBilled = F.I(r["MonthsBilled"]) },
            after: new
            {
                Status = "ended",
                Reason = reason.Trim(),
                ReturnedToStock = backToStock,
                Hcpcs = hcpcs,
                Serial = serial
            });

        return RedirectToAction("Rentals");
    }

    /// <summary>
    /// Call off an order that has not been delivered.
    ///
    /// A wrong customer, a wrong item, a prescription withdrawn: until now the
    /// order simply stayed on the list for good, and the Open orders tile went
    /// on counting it as work outstanding.
    ///
    /// A DELIVERED order is never cancellable. Delivering wrote a claim, a
    /// rental, a stock movement and a serialised unit; cancelling the order
    /// would leave all of it pointing at something that officially never
    /// happened. Undoing a delivery is a different, larger act: void the
    /// payment, end the rental, correct the stock. Not this button.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "0,1")]
    public async Task<IActionResult> CancelOrder(int id, string? reason)
    {
        var o = _db.QueryOne(
            "SELECT OrderId, OrderNumber, Status, Stage FROM dbo.DmeOrders WHERE OrderId=@id", new { id });

        if (o == null) return NotFound();

        if (F.S(o["Status"]) == "delivered")
        {
            TempData["OrderError"] =
                "A delivered order cannot be cancelled. It has already produced a claim and, "
                + "for a rental, a billing schedule.";
            return RedirectToAction("Order", new { id });
        }

        // A reason, for the same purpose voiding a payment needs one: somebody
        // will ask why this order stops at nothing months from now.
        if (string.IsNullOrWhiteSpace(reason))
        {
            TempData["OrderError"] = "Say why the order is being cancelled.";
            return RedirectToAction("Order", new { id });
        }

        // Guarded on the current status so two clicks cancel once.
        var done = _db.Execute(
            "UPDATE dbo.DmeOrders SET Status='cancelled' " +
            "WHERE OrderId=@id AND TenantId=@TenantId AND Status <> 'delivered' AND Status <> 'cancelled'",
            new { id }) == 1;

        if (done)
        {
            await _audit.RecordAsync("DME_ORDER_CANCELLED", "DmeOrder", id,
                before: new { Status = F.S(o["Status"]), Stage = F.S(o["Stage"]) },
                after: new { Status = "cancelled", OrderNumber = F.S(o["OrderNumber"]), Reason = reason.Trim() });
        }

        return RedirectToAction("Order", new { id });
    }

    /// <summary>Put a cancelled order back. Cancelling is one click, so it needs a way back.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "0,1")]
    public async Task<IActionResult> RestoreOrder(int id)
    {
        // Back to draft, not to whatever it was. Confirmed means somebody
        // checked eligibility and stock, and that check is stale by now.
        var done = _db.Execute(
            "UPDATE dbo.DmeOrders SET Status='draft', Stage='receive-order' " +
            "WHERE OrderId=@id AND TenantId=@TenantId AND Status='cancelled'",
            new { id }) == 1;

        if (done)
        {
            await _audit.RecordAsync("DME_ORDER_RESTORED", "DmeOrder", id,
                before: new { Status = "cancelled" }, after: new { Status = "draft" });
        }

        return RedirectToAction("Order", new { id });
    }

    /// <summary>
    /// The customer form, filled in.
    ///
    /// Until now a customer could be created and never corrected: a phone typed
    /// wrong, a house moved, a name misspelled off a referral. All of it is on
    /// the CMS-1500, so a wrong address is a denied claim, not a cosmetic
    /// blemish.
    ///
    /// It is the SAME form as New Customer, rendered from the same partial, and
    /// that is the point. It used to be a second file carrying Basic Info and
    /// Emergency Contact only, with a note saying insurance and diagnosis "are
    /// changed from the customer's own page". They were not: no action anywhere
    /// in the product changed either, so a payer entered wrong stayed wrong.
    /// </summary>
    [HttpGet]
    public IActionResult EditCustomer(int id)
    {
        var c = _db.QueryOne("SELECT * FROM dbo.DmeCustomers WHERE CustomerId=@id", new { id });
        if (c == null) return NotFound();

        // Decrypted in memory for the form. The row on disk stays ciphertext.
        _phi.DecryptRow(c);

        ViewData["Title"] = "Edit customer";
        ViewData["ActivePage"] = "customers";
        ViewBag.Customer = c;
        ViewBag.Locations = _db.Query(
            "SELECT LocationId, Name, IsPrimary FROM dbo.Locations " +
            "WHERE TenantId=@TenantId AND IsActive=1 AND" + _db.LocationGrants("LocationId") +
            " ORDER BY IsPrimary DESC, Name");

        // The primary insurance and the diagnoses on file, so the form opens
        // showing what is there rather than empty boxes over stored values. The
        // form is the same partial New Customer uses, and it reads both as null
        // when they are absent, which is exactly the New case.
        ViewBag.Insurance = Insurance(id, "primary");
        ViewBag.SecondaryInsurance = Insurance(id, "secondary");
        ViewBag.Documents = _customerDocs.ForCustomer(id);
        ViewBag.DocumentKinds = _customerDocs.Kinds;
        ViewBag.Diagnoses = _db.Query(
            "SELECT IcdCode, Description, IsPrimary FROM dbo.DmeCustomerDiagnoses " +
            "WHERE CustomerId=@id ORDER BY IsPrimary DESC, DiagnosisId", new { id });

        return View();
    }

    /// <summary>
    /// One insurance row of the given kind, or null.
    ///
    /// UX_DmeCustomerInsurances_Kind makes "one of each per customer" a rule the
    /// database enforces, so this returns at most one row by construction rather
    /// than by hope. The TOP 1 stays because a query that returned two would be
    /// a bug worth surviving rather than an exception on a customer screen.
    /// </summary>
    private Dictionary<string, object?>? Insurance(int customerId, string kind) => _db.QueryOne(
        "SELECT TOP 1 * FROM dbo.DmeCustomerInsurances " +
        "WHERE CustomerId=@customerId AND Kind=@kind ORDER BY InsuranceId",
        new { customerId, kind });

    /// <summary>
    /// File the customer's primary insurance. Returns the payer now on the
    /// record, or null if there is none.
    ///
    /// WHO CALLS IT
    /// CreateCustomer and UpdateCustomer, which are one form. It exists because
    /// they were about to become two copies of the same rules, and the last time
    /// this product had two copies of one thing the edit screen quietly lost
    /// three of the five cards the new screen had.
    ///
    /// THE RULES
    /// The form posts a catalog id and nothing else. The payer's name and the
    /// Payer ID an 837 is addressed to are read back from the catalog here,
    /// never taken from the browser: a posted name would let a typo, or a forged
    /// field, become the payer a claim is billed to.
    ///
    /// Both are then STORED on the insurance record rather than referenced by
    /// id, and that is deliberate. It is the point-in-time record of who this
    /// customer was insured with, and it must not move if Office Ally later
    /// corrects a name in the catalog.
    ///
    /// A posted id of 0 means KEEP the payer already on file, not "remove the
    /// insurance". The picker cannot start on the stored payer, because what is
    /// stored is a name and a code and the catalog id is recoverable from
    /// neither: Office Ally issues ALLCA to two different payers. So an
    /// untouched picker posts 0, and 0 has to mean "unchanged" or opening the
    /// edit screen to fix a phone number would drop the customer's insurance.
    ///
    /// <paramref name="remove"/> is how a SECONDARY policy ends, and there is
    /// deliberately no equivalent for the primary. A secondary genuinely lapses:
    /// a spouse changes job, COBRA runs out, and "they no longer have one" is a
    /// fact the record has to be able to hold. A supplier with no primary cannot
    /// bill at all, so removing it is not a state worth building a button for;
    /// it is a different payer, which is what the picker is for.
    ///
    /// Deleting is safe here, and only here, because a claim carries its own
    /// PayerName copied at the moment it was raised. Ending a policy today
    /// cannot change what a claim says it was billed under.
    /// </summary>
    private string? SaveCustomerInsurance(
        int customerId, string kind, int insPayerId, string? memberId, string? group,
        decimal copay, int coins, decimal deductible, bool remove = false)
    {
        var chosen = _payers.Find(insPayerId);
        var existing = Insurance(customerId, kind);

        if (remove)
        {
            if (existing != null)
            {
                _db.Execute("DELETE FROM dbo.DmeCustomerInsurances WHERE InsuranceId=@insuranceId AND TenantId=@TenantId",
                    new { insuranceId = F.I(existing["InsuranceId"]) });
            }

            return null;
        }

        // No payer chosen and none on file: there is nothing to write. The other
        // boxes are meaningless without a payer, so they are not filed either.
        if (chosen == null && existing == null) return null;

        var mid = (object?)memberId ?? DBNull.Value;
        var grp = (object?)group ?? DBNull.Value;

        if (existing == null)
        {
            _db.Execute(@"INSERT INTO dbo.DmeCustomerInsurances (CustomerId,Kind,PayerName,PayerId,MemberId,GroupNumber,Copay,Coinsurance,Deductible,SubscriberRel,EligStatus,TenantId)
                            VALUES (@customerId,@kind,@pn,@pid,@mid,@grp,@copay,@coins,@ded,'Self','active',@TenantId)",
                new { customerId, kind, pn = chosen!.Name, pid = chosen.PayerCode,
                      mid, grp, copay, coins, ded = deductible });

            return chosen.Name;
        }

        // Keep what is stored when the picker was not touched, which is the
        // common case: somebody opened this screen to fix a phone number.
        var name = chosen?.Name ?? F.S(existing["PayerName"]);
        var code = chosen?.PayerCode ?? F.S(existing["PayerId"]);

        _db.Execute(@"UPDATE dbo.DmeCustomerInsurances
                         SET PayerName=@pn, PayerId=@pid, MemberId=@mid, GroupNumber=@grp,
                             Copay=@copay, Coinsurance=@coins, Deductible=@ded
                       WHERE InsuranceId=@insuranceId AND TenantId=@TenantId",
            new { insuranceId = F.I(existing["InsuranceId"]), pn = name, pid = code,
                  mid, grp, copay, coins, ded = deductible });

        return name;
    }

    /// <summary>
    /// File the customer's diagnoses. Returns the codes now on the record, in
    /// order; the first is the primary.
    ///
    /// WHO CALLS IT
    /// CreateCustomer and UpdateCustomer, for the same reason as the insurance
    /// above: they are one form.
    ///
    /// THE RULES
    /// The browser posts codes, and the description filed against the customer
    /// is read out of the catalog. A code that is not valid ICD-10-CM files no
    /// diagnosis at all rather than one with a blank description, which is what
    /// the old hardcoded list produced for anything outside its twelve entries.
    ///
    /// The description is STORED, not joined, because it is the point-in-time
    /// record of what was billed: CMS rewords codes every October.
    ///
    /// More than one, because one was never enough. CMS-1500 carries up to
    /// twelve diagnosis pointers, and DME routinely needs two or three: oxygen
    /// justified by COPD alone is thin, and thin medical necessity is what comes
    /// back as CO-50. The FIRST is primary. The order is the operator's, and it
    /// decides which diagnosis a service line points at on the claim.
    ///
    /// The posted list REPLACES what is on file, so removing a chip removes a
    /// diagnosis. That is only safe because the chips are rendered by the
    /// server: a page whose script failed to run still posts the list it was
    /// given, so a dead script leaves the record exactly as it was rather than
    /// emptying it.
    ///
    /// An unchanged list is not rewritten. Nothing points at DiagnosisId, so
    /// churning the rows would be harmless and still wrong: it would make every
    /// saved phone number look like a clinical change in the audit log.
    /// </summary>
    private IReadOnlyList<string> SaveCustomerDiagnoses(int customerId, string[]? dxCodes)
    {
        var filed = new List<IcdMatch>();

        foreach (var code in dxCodes ?? Array.Empty<string>())
        {
            var diagnosis = _icd.Find(code);

            // An unknown code files nothing, rather than a diagnosis with blank
            // wording. A duplicate is dropped: the same code twice is a repeated
            // pointer, not a stronger case.
            if (diagnosis == null || filed.Any(f => f.Code == diagnosis.Code)) continue;

            filed.Add(diagnosis);
        }

        var codes = filed.Select(f => f.Code).ToList();

        var current = _db.Query(
                "SELECT IcdCode FROM dbo.DmeCustomerDiagnoses WHERE CustomerId=@customerId " +
                "ORDER BY IsPrimary DESC, DiagnosisId", new { customerId })
            .Select(r => F.S(r["IcdCode"]))
            .ToList();

        if (current.SequenceEqual(codes)) return codes;

        _db.Execute("DELETE FROM dbo.DmeCustomerDiagnoses WHERE CustomerId=@customerId AND TenantId=@TenantId",
            new { customerId });

        foreach (var diagnosis in filed)
        {
            _db.Execute("INSERT INTO dbo.DmeCustomerDiagnoses (CustomerId,IcdCode,Description,IsPrimary,TenantId) VALUES (@customerId,@code,@desc,@primary,@TenantId)",
                new { customerId, code = diagnosis.Code, desc = diagnosis.Description,
                      primary = diagnosis.Code == codes[0] });
        }

        return codes;
    }

    /// <summary>
    /// Save a corrected customer.
    ///
    /// Same field rules as creating one, for the same reason: state and ZIP are
    /// printed onto the claim, so a malformed one is a denial. The blind index
    /// is rebuilt from the new plaintext, because a renamed customer must stop
    /// being findable under the old name.
    ///
    /// It takes the same insurance and diagnosis arguments CreateCustomer takes,
    /// because it is the same form. Two things differ, and both are about not
    /// destroying what is already there:
    ///
    ///   insPayerId = 0 means KEEP the payer on file, not "no payer". The stored
    ///   row holds the payer's name and Payer ID rather than the catalog id, so
    ///   the picker has nothing to start on and comes back empty when untouched.
    ///
    ///   The diagnosis list is REPLACED by what was posted. The chips carrying
    ///   it are rendered by the server, so an untouched form posts the list back
    ///   unchanged even with no JavaScript running.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "0,1,2,4")]
    public async Task<IActionResult> UpdateCustomer(
        int id, string firstName, string lastName, string? dob, string? gender, string? ssnLast4,
        int? heightInches, int? weightLbs, string? phone, string? email,
        string? addressLine1, string? city, string? state, string? zip,
        string? emergencyName, string? emergencyRel, string? emergencyPhone,
        int insPayerId, string? insMemberId, string? insGroup, decimal insCopay, int insCoins, decimal insDeductible,
        int secPayerId, string? secMemberId, string? secGroup, decimal secCopay, int secCoins, decimal secDeductible,
        bool secRemove,
        string[]? dxCodes, int locationId = 0)
    {
        var existing = _db.QueryOne(
            "SELECT CustomerId, AccountNo, Status FROM dbo.DmeCustomers WHERE CustomerId=@id", new { id });

        if (existing == null) return NotFound();

        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
        {
            TempData["CustomerError"] = "First and last name are both required.";
            return RedirectToAction("EditCustomer", new { id });
        }

        // Checked against this tenant rather than trusted from the form, exactly
        // as on create: RLS covers TenantId and would accept a foreign
        // LocationId sitting beside it.
        var branch = _db.Scalar(
            "SELECT LocationId FROM dbo.Locations WHERE LocationId=@locationId AND TenantId=@TenantId AND IsActive=1",
            new { locationId });

        if (branch == null)
        {
            TempData["CustomerError"] = "Choose which location this customer belongs to.";
            return RedirectToAction("EditCustomer", new { id });
        }

        var problem = CustomerFieldProblem(dob, email, ssnLast4, state, zip);
        if (problem != null)
        {
            TempData["CustomerError"] = problem;
            return RedirectToAction("EditCustomer", new { id });
        }

        object Enc(string? v) => (object?)_phi.Encrypt(v) ?? DBNull.Value;

        _db.Execute(@"
            UPDATE dbo.DmeCustomers SET
                FirstName=@fn, LastName=@ln, Dob=@dob, Gender=@g, SsnLast4=@ssn,
                HeightInches=@h, WeightLbs=@w, Phone=@ph, Email=@em,
                AddressLine1=@a1, City=@city, State=@st, Zip=@zip,
                EmergencyName=@en, EmergencyRel=@er, EmergencyPhone=@ep,
                LocationId=@branchId
            WHERE CustomerId=@id AND TenantId=@TenantId",
            new
            {
                id,
                // @branchId, never @locationId: DmeDb injects an @LocationId on
                // every command holding the branch being VIEWED, and SQL
                // parameter names are case insensitive. Naming it locationId
                // here would silently write the viewed branch instead of the
                // chosen one. Same trap as the insert.
                branchId = Convert.ToInt32(branch),
                fn = Enc(firstName), ln = Enc(lastName),
                dob = Enc(DmeCustomerPhi.FormatDob(DateInput.Parse(dob))),
                g = (object?)gender ?? DBNull.Value,
                ssn = Enc(ssnLast4),
                h = (object?)heightInches ?? DBNull.Value, w = (object?)weightLbs ?? DBNull.Value,
                ph = Enc(phone), em = Enc(email),
                a1 = Enc(addressLine1), city = Enc(city), st = Enc(state), zip = Enc(zip),
                en = Enc(emergencyName), er = Enc(emergencyRel), ep = Enc(emergencyPhone)
            });

        // Rebuilt, not added to. IndexCustomerForSearch deletes first, so the
        // old name stops matching: leaving it would be both wrong and a privacy
        // problem, since the previous name would still be discoverable.
        IndexCustomerForSearch(id, firstName, lastName, phone);

        var payer = SaveCustomerInsurance(
            id, "primary", insPayerId, insMemberId, insGroup, insCopay, insCoins, insDeductible);

        var secondary = SaveCustomerInsurance(
            id, "secondary", secPayerId, secMemberId, secGroup, secCopay, secCoins, secDeductible,
            remove: secRemove);

        var filed = SaveCustomerDiagnoses(id, dxCodes);

        // No PHI in the audit row, same rule as creation. What changed is
        // recoverable from the record; what matters here is that it changed.
        // The payer and the diagnosis list are named because they decide what a
        // claim is addressed to and whether it establishes medical necessity,
        // so "somebody edited this customer" is not enough to answer a denial.
        await _audit.RecordAsync("DME_CUSTOMER_EDITED", "DmeCustomer", id,
            before: new { AccountNo = F.S(existing["AccountNo"]) },
            after: new
            {
                AccountNo = F.S(existing["AccountNo"]),
                Branch = Convert.ToInt32(branch),
                Payer = payer,
                Secondary = secondary,
                PrimaryDx = filed.FirstOrDefault(),
                DxCount = filed.Count
            });

        return RedirectToAction("Customer", new { id });
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

        // The third number, and the client's actual complaint: most of what they
        // deliver ships direct from a distributor and never reaches a shelf, so
        // it appeared in neither of the two above. It is kept SEPARATE rather
        // than added to either, because an item in somebody else's warehouse is
        // not stock this supplier holds.
        var drops = _db.Query(
            "SELECT * FROM dbo.vDmeDropShipments WHERE " + _db.LocationScope() +
            " ORDER BY HasArrived, OrderId DESC");
        _phi.ComposeCustomerNames(drops);
        ViewBag.DropShipments = drops;

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
        ViewBag.Documents = _documents.ForOrder(id);
        ViewBag.PodError = TempData["PodError"];
        return View();
    }

    [HttpGet]
    public IActionResult NewOrder(int? customerId = null)
    {
        ViewData["Title"] = "New Order";
        ViewData["ActivePage"] = "orders";
        LoadOrderPickers();
        ViewBag.PreCustomer = customerId;
        return View();
    }

    /// <summary>
    /// The four lists the order form is built from.
    ///
    /// WHO CALLS IT
    /// NewOrder and EditOrder, which render the same partial. A picker loaded on
    /// one screen and forgotten on the other is a form that renders with an
    /// empty dropdown and no clue why, so there is one method rather than two
    /// copies to keep in step.
    /// </summary>
    private void LoadOrderPickers()
    {
        // Decrypt before the picker renders, and sort after, for the same reason
        // the customer list does: names are ciphertext, so an unsorted-looking
        // dropdown of base64 is what you get otherwise.
        // The picker offers the branch you are working in, so an order cannot be
        // raised against a customer from a depot you are not looking at.
        // Archived customers are off the working list, so they are off this
        // picker too: raising a new order against somebody you have retired is
        // never the intent, and letting it happen puts them back in the billing
        // run without anybody deciding to.
        var customers = _db.Query(
            "SELECT CustomerId, AccountNo, FirstName, LastName FROM dbo.DmeCustomers " +
            "WHERE Status='active' AND " + _db.LocationScope());
        _phi.DecryptRows(customers);
        ViewBag.Customers = customers
            .OrderBy(c => F.S(c["LastName"]), StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => F.S(c["FirstName"]), StringComparer.OrdinalIgnoreCase)
            .ToList();

        // In service only, and through the service rather than its own query, so
        // the picker and the Doctors screen cannot disagree about who is
        // available. A retired doctor still resolves on an old order.
        ViewBag.Doctors = _doctors.All();
        ViewBag.Catalog = _db.Query("SELECT * FROM dbo.vHcpcsCatalog ORDER BY Category, Hcpcs");
        // Live distributors only. A retired one still resolves on an old order,
        // but offering it on a NEW line would file an order against a supplier
        // this business no longer buys from.
        ViewBag.Distributors = _distributors.All();

        // And the full list, retired included, purely so an existing LINE can
        // still print who shipped it. The picker and the label answer different
        // questions: "who may I choose now" and "who did we use then".
        ViewBag.AllDistributors = _distributors.All(includeRetired: true);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateOrder(int customerId, int? doctorId, string? deliveryDate, decimal deposit,
                                     string[]? hcpcs, string[]? mode, int[]? qty,
                                     int[]? distributorId, string[]? distributorRef,
                                     string? status = null)
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

        // Which button was pressed. Every order used to be born confirmed, which
        // said "somebody has checked eligibility and stock" on the day it was
        // typed, and on most orders that is not true yet. Anything other than
        // the draft button confirms, so a form posted without the field behaves
        // exactly as it did before.
        var (orderStatus, orderStage) = OrderState(status);

        var number = _db.NextNumber("ORD");
        var orderId = Convert.ToInt32(_db.Scalar(@"
            INSERT INTO dbo.DmeOrders (OrderNumber,CustomerId,Status,Stage,DoctorId,DeliveryDate,Deposit,TenantId)
            OUTPUT inserted.OrderId
            VALUES (@number,@customerId,@orderStatus,@orderStage,@doctorId,@dd,@deposit,@TenantId)",
            new {
                number, customerId, orderStatus, orderStage,
                doctorId = (object?)doctorId ?? DBNull.Value,
                // Typed or pasted, in any of the spellings DateInput accepts. Never
                // model binding: that parses under the server's locale, so 03/04/2026
                // would be March or April depending on the machine.
                dd = (object?)DateInput.Parse(deliveryDate) ?? DBNull.Value,
                deposit
            }));

        var lineCount = SaveOrderLines(orderId, hcpcs, mode, qty, distributorId, distributorRef);

        await _audit.RecordAsync("DME_ORDER_CREATED", "DmeOrder", orderId,
            before: null,
            after: new { OrderNumber = number, CustomerId = customerId, DoctorId = doctorId, DeliveryDate = deliveryDate, Deposit = deposit, Lines = lineCount, Status = orderStatus });

        return RedirectToAction("Order", new { id = orderId });
    }

    /// <summary>
    /// File an order's lines. Returns how many were filed.
    ///
    /// WHO CALLS IT
    /// CreateOrder and UpdateOrder, which are one form. It exists so those two
    /// cannot drift, the same reason SaveCustomerInsurance exists: the customer
    /// screens were two files describing one thing and had already come apart.
    ///
    /// THE RULES
    /// The browser posts a HCPCS code, a mode and a quantity. Everything a claim
    /// is built from is read out of the supplier's item master here: the name,
    /// the category, the price, the modifier, whether the item is serialised. A
    /// posted price would let the browser decide what a claim is worth.
    ///
    /// An unknown code files nothing rather than a line with a blank name, which
    /// is the same rule the diagnosis picker follows.
    ///
    /// It REPLACES what is on the order. That is only safe because nothing in
    /// the product references a line by id, and because the caller guards on the
    /// order not being delivered: after delivery the lines have produced stock
    /// movements, serialised units and claim lines, and rewriting them would
    /// leave all three pointing at something that no longer exists.
    /// </summary>
    private int SaveOrderLines(
        int orderId, string[]? hcpcs, string[]? mode, int[]? qty,
        int[]? distributorId, string[]? distributorRef)
    {
        _db.Execute("DELETE FROM dbo.DmeOrderLines WHERE OrderId=@orderId AND TenantId=@TenantId",
            new { orderId });

        var lineCount = 0;
        if (hcpcs == null) return lineCount;

        for (int i = 0; i < hcpcs.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(hcpcs[i])) continue;
            var item = _db.QueryOne("SELECT * FROM dbo.HcpcsCodes WHERE Hcpcs=@h", new { h = hcpcs[i] });
            if (item == null) continue;
            var m = (mode != null && i < mode.Length) ? mode[i] : "purchase";
            var q = (qty != null && i < qty.Length && qty[i] > 0) ? qty[i] : 1;

            // Drop shipping. A posted distributor is checked against THIS
            // tenant before it is filed, the same way the branch on the
            // customer form is: row level security covers TenantId on the
            // insert and would happily accept a foreign DistributorId
            // sitting beside it.
            //
            // NULL means "out of our own stock", which is the answer for
            // almost every line, and the absence is the whole record: there
            // is no separate is-drop-shipped flag to disagree with it.
            var postedDist = (distributorId != null && i < distributorId.Length) ? distributorId[i] : 0;
            var dist = postedDist <= 0 ? null : _db.Scalar(
                "SELECT DistributorId FROM dbo.DmeDistributors WHERE DistributorId=@postedDist AND TenantId=@TenantId AND RetiredAt IS NULL",
                new { postedDist });

            var distRef = (dist != null && distributorRef != null && i < distributorRef.Length)
                ? distributorRef[i] : null;

            _db.Execute(@"INSERT INTO dbo.DmeOrderLines (OrderId,Hcpcs,ItemName,Category,Mode,Qty,UnitPrice,MonthlyRate,Modifiers,IsSerialized,DistributorId,DistributorRef,TenantId)
                            VALUES (@orderId,@h,@name,@cat,@m,@q,@up,@mr,@mods,@ser,@dist,@distRef,@TenantId)",
                new {
                    orderId, h = hcpcs[i], name = F.S(item["Name"]), cat = F.S(item["Category"]), m, q,
                    up = m == "purchase" ? F.Dec(item["PurchasePrice"]) : 0m,
                    mr = m == "purchase" ? 0m : F.Dec(item["MonthlyRate"]),
                    mods = m == "purchase" ? "NU" : "RR",
                    ser = F.B(item["IsSerialized"]),
                    dist = (object?)dist ?? DBNull.Value,
                    distRef = string.IsNullOrWhiteSpace(distRef) ? DBNull.Value : (object)distRef.Trim()
                });
            lineCount++;
        }

        return lineCount;
    }

    /// <summary>
    /// What the form's button means, as a status and the stage beside it.
    ///
    /// Stage travels WITH status rather than being set separately, because the
    /// two disagreeing is the kind of thing nobody notices: the order screen
    /// reads one and the delivery list reads the other. Only the draft button
    /// produces a draft; anything else, including a form posted with no status
    /// at all, confirms.
    /// </summary>
    private static (string Status, string Stage) OrderState(string? posted) =>
        posted == "draft" ? ("draft", "receive-order") : ("confirmed", "order-ship");

    /// <summary>
    /// Statuses an order can still be corrected in.
    ///
    /// NOT a list of "draft-like" words. It is the question "has anything
    /// irreversible happened yet", and the answer is no until Deliver runs.
    /// Deliver writes stock movements, serialised units, a rental and a claim,
    /// and none of those can be un-written by editing the order they came from.
    /// A cancelled order is reopened first, deliberately, because reopening
    /// resets it to draft and says out loud that the eligibility and stock check
    /// behind "confirmed" is stale.
    /// </summary>
    private static bool IsEditable(string status) => status is not ("delivered" or "cancelled");

    /// <summary>
    /// The order form, filled in.
    ///
    /// It is the SAME form as New Order, rendered from the same partial. Until
    /// now an order could be raised and never corrected: a quantity typed wrong,
    /// the wrong HCPCS picked off a similar name, a delivery date moved. The
    /// only way out was to cancel it and raise another, which spends an order
    /// number and leaves a cancelled row whose real reason was a typo.
    /// </summary>
    [HttpGet]
    public IActionResult EditOrder(int id)
    {
        var o = _db.QueryOne("SELECT * FROM dbo.vDmeOrders WHERE OrderId=@id", new { id });
        if (o == null) return NotFound();

        // vDmeOrders returns CustomerFirstName and CustomerLastName separately,
        // both ciphertext, because two ciphertexts concatenated in SQL cannot be
        // decrypted. Without this the screen renders base64 the moment anything
        // on it shows a name.
        _phi.ComposeCustomerName(o);

        if (!IsEditable(F.S(o["Status"])))
        {
            TempData["OrderError"] = F.S(o["Status"]) == "delivered"
                ? "A delivered order cannot be edited. It has already produced a claim and moved stock."
                : "Reopen this order as a draft before editing it.";
            return RedirectToAction("Order", new { id });
        }

        ViewData["Title"] = "Edit order";
        ViewData["ActivePage"] = "orders";
        ViewBag.Order = o;
        ViewBag.Lines = _db.Query(
            "SELECT * FROM dbo.DmeOrderLines WHERE OrderId=@id ORDER BY LineId", new { id });

        LoadOrderPickers();
        return View();
    }

    /// <summary>
    /// Save a corrected order.
    ///
    /// Same rules as raising one, through the same writer, because it is the
    /// same form. Two things are its own:
    ///
    ///   The status is re-read and checked HERE, not trusted from the screen the
    ///   operator opened. An order can be delivered by somebody else while this
    ///   form is open, and the delivery is the thing that must win.
    ///
    ///   The lines are REPLACED by what was posted. That is safe on an
    ///   undelivered order because nothing references a line by id, and it is
    ///   only safe there.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateOrder(
        int id, int customerId, int? doctorId, string? deliveryDate, decimal deposit,
        string[]? hcpcs, string[]? mode, int[]? qty,
        int[]? distributorId, string[]? distributorRef,
        string? status = null)
    {
        var existing = _db.QueryOne(
            "SELECT OrderId, OrderNumber, Status, DoctorId, DeliveryDate, Deposit FROM dbo.DmeOrders WHERE OrderId=@id",
            new { id });

        if (existing == null) return NotFound();

        // Re-read, never trusted from the form. Somebody may have delivered this
        // order while the edit screen was open, and that has to win.
        if (!IsEditable(F.S(existing["Status"])))
        {
            TempData["OrderError"] = F.S(existing["Status"]) == "delivered"
                ? "This order was delivered while you were editing it. Nothing was changed."
                : "This order was cancelled. Reopen it before editing.";
            return RedirectToAction("Order", new { id });
        }

        // Same guard as creating one, and for the same reason: an order with no
        // lines is not an order, it is a ticket that later produces a $0.00
        // claim. The lines are added by JavaScript, so posting with none is a
        // normal thing for a real person to do.
        if (hcpcs?.Any(h => !string.IsNullOrWhiteSpace(h)) != true)
        {
            TempData["OrderError"] = "An order needs at least one item. Nothing was changed.";
            return RedirectToAction("EditOrder", new { id });
        }

        var custExists = _db.Scalar(
            "SELECT CustomerId FROM dbo.DmeCustomers WHERE CustomerId=@customerId", new { customerId });

        if (custExists == null)
        {
            TempData["OrderError"] = "Choose the customer this order is for.";
            return RedirectToAction("EditOrder", new { id });
        }

        // A draft can be saved as a draft or saved and confirmed, from the two
        // buttons on the form. A confirmed order stays confirmed: its form
        // offers one button, and walking it backwards would take an order
        // somebody is expecting to deliver off the delivery list without
        // anybody deciding to.
        var (orderStatus, orderStage) = F.S(existing["Status"]) == "draft"
            ? OrderState(status)
            : ("confirmed", "order-ship");

        _db.Execute(@"
            UPDATE dbo.DmeOrders
               SET CustomerId=@customerId, DoctorId=@doctorId, DeliveryDate=@dd, Deposit=@deposit,
                   Status=@orderStatus, Stage=@orderStage
             WHERE OrderId=@id AND TenantId=@TenantId AND Status NOT IN ('delivered','cancelled')",
            new
            {
                id, customerId, orderStatus, orderStage,
                doctorId = (object?)doctorId ?? DBNull.Value,
                // Never model binding: that parses under the server's locale, so
                // 03/04/2026 would be March or April depending on the machine.
                dd = (object?)DateInput.Parse(deliveryDate) ?? DBNull.Value,
                deposit
            });

        var lineCount = SaveOrderLines(id, hcpcs, mode, qty, distributorId, distributorRef);

        await _audit.RecordAsync("DME_ORDER_EDITED", "DmeOrder", id,
            before: new
            {
                OrderNumber = F.S(existing["OrderNumber"]),
                DoctorId = existing["DoctorId"],
                DeliveryDate = existing["DeliveryDate"],
                Deposit = F.Dec(existing["Deposit"])
            },
            after: new
            {
                OrderNumber = F.S(existing["OrderNumber"]),
                CustomerId = customerId, DoctorId = doctorId,
                DeliveryDate = deliveryDate, Deposit = deposit, Lines = lineCount,
                Status = orderStatus
            });

        return RedirectToAction("Order", new { id });
    }

    /// <summary>
    /// Take a draft off the shelf: eligibility and stock have been checked, and
    /// the order is ready to go out.
    ///
    /// One click from the order screen, rather than opening the whole form and
    /// saving it, because that is the shape of the job. The CMN arrives, the
    /// prior authorisation comes back, and nothing about the order itself needs
    /// to change.
    ///
    /// Guarded on Status='draft' so two clicks confirm once, and so a delivered
    /// order cannot be walked backwards into the delivery list.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmOrder(int id)
    {
        var lines = Convert.ToInt32(_db.Scalar(
            "SELECT COUNT(*) FROM dbo.DmeOrderLines WHERE OrderId=@id", new { id }) ?? 0);

        // Same rule as saving one. An order with no lines is not an order, it is
        // a ticket that later produces a $0.00 claim, and confirming it says it
        // is ready to deliver.
        if (lines == 0)
        {
            TempData["OrderError"] = "Add at least one item before confirming this order.";
            return RedirectToAction("Order", new { id });
        }

        var done = _db.Execute(
            "UPDATE dbo.DmeOrders SET Status='confirmed', Stage='order-ship' " +
            "WHERE OrderId=@id AND TenantId=@TenantId AND Status='draft'",
            new { id }) == 1;

        if (done)
        {
            await _audit.RecordAsync("DME_ORDER_CONFIRMED", "DmeOrder", id,
                before: new { Status = "draft" },
                after: new { Status = "confirmed" });
        }

        return RedirectToAction("Order", new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deliver(int id, string signedBy, string? signature)
    {
        var o = _db.QueryOne("SELECT * FROM dbo.vDmeOrders WHERE OrderId=@id", new { id });
        if (o == null) return NotFound();
        _phi.ComposeCustomerName(o);

        // Delivery happens once. This action writes a claim, a rental, a stock
        // movement and a serialised unit, none of which are guarded against
        // being written twice, so a second POST (a refreshed confirmation, a
        // double click, a back button) would bill the customer again for
        // equipment they were given once.
        var already = F.S(o["Status"]);
        if (already is "delivered" or "cancelled" or "draft")
        {
            TempData["OrderError"] = already switch
            {
                "delivered" => "This order has already been delivered.",
                "cancelled" => "This order was cancelled. Restore it before delivering.",
                // The draft case is the one that was missing. The order screen
                // hides the delivery panel on a draft, but hiding a button is
                // not a guard: a reopened order could be delivered by a POST
                // with the stale eligibility and stock check that reopening it
                // as a draft was meant to flag.
                _ => "This order is still a draft. Confirm it before delivering."
            };
            return RedirectToAction("Order", new { id });
        }

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

            // DROP SHIPPED lines touch neither the unit register nor the stock
            // ledger, and that absence is the entire feature.
            //
            // The item went from the distributor to the customer's home. It was
            // never on a shelf and this supplier never held it, so there is no
            // movement to record and no unit of theirs to register. Writing one
            // anyway would drive on-hand negative for the MAJORITY of what this
            // supplier delivers, because most of their items ship this way.
            //
            // Derived from the distributor being present. There is no
            // is-drop-shipped flag to fall out of step with it.
            var dropShipped = l["DistributorId"] is not (null or DBNull);
            if (dropShipped) continue;

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
    // Money is the biller's and the owner's. Delivery and Intake never see it:
    // the person who hands equipment over must not also be the person who
    // records what was paid for it.
    [Authorize(Roles = DmeRoles.Money)]
    public async Task<IActionResult> BillNow(int id)
    {
        var r = _db.QueryOne("SELECT * FROM dbo.vDmeRentals WHERE RentalId=@id", new { id });
        if (r == null) return NotFound();
        _phi.ComposeCustomerName(r);

        // An ended rental is equipment that has come back. Billing it is a claim
        // for something nobody has.
        if (F.S(r["Status"]) != "active")
        {
            TempData["RentalError"] = "That rental has ended and cannot be billed.";
            return RedirectToAction("Rentals");
        }

        // The cap is Medicare's rule, not a display. Past it the equipment
        // belongs to the customer and billing stops; carrying on is money that
        // gets clawed back with a penalty on top. The screen counted the months
        // and then let you bill anyway, which is the worst of both.
        if (F.B(r["CapReached"]))
        {
            TempData["RentalError"] =
                $"This rental has reached its {F.I(r["CapMonths"])} month cap. "
                + "The equipment now belongs to the customer, so it cannot be billed again.";
            return RedirectToAction("Rentals");
        }

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
    // Money is the biller's and the owner's. Delivery and Intake never see it:
    // the person who hands equipment over must not also be the person who
    // records what was paid for it.
    [Authorize(Roles = DmeRoles.Money)]
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
    // Money is the biller's and the owner's. Delivery and Intake never see it:
    // the person who hands equipment over must not also be the person who
    // records what was paid for it.
    [Authorize(Roles = DmeRoles.Money)]
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
    // Money is the biller's and the owner's. Delivery and Intake never see it:
    // the person who hands equipment over must not also be the person who
    // records what was paid for it.
    [Authorize(Roles = DmeRoles.Money)]
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
    // Money is the biller's and the owner's. Delivery and Intake never see it:
    // the person who hands equipment over must not also be the person who
    // records what was paid for it.
    [Authorize(Roles = DmeRoles.Money)]
    public async Task<IActionResult> CreatePayment(
        int claimId, string source, string? payerName, string postedDate, string method,
        string? referenceNumber, decimal amount, string? note,
        int[]? claimLineId, decimal[]? allowed, decimal[]? paid,
        int[]? adjLine, string[]? adjGroup, string[]? adjCode, decimal[]? adjAmount)
    {
        // Unlike a date of birth this one is required, so an unreadable value is
        // refused out loud. Defaulting it to today would post real money into the
        // wrong month without anybody being told.
        var posted = DateInput.Parse(postedDate);
        if (posted == null)
        {
            TempData["PaymentError"] = "The posting date could not be read. Use mm/dd/yyyy.";
            return RedirectToAction("PostPayment", new { id = claimId });
        }

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
            posted.Value, method, referenceNumber, amount, note, lines));

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
    // Money is the biller's and the owner's. Delivery and Intake never see it:
    // the person who hands equipment over must not also be the person who
    // records what was paid for it.
    [Authorize(Roles = DmeRoles.Money)]
    public async Task<IActionResult> VoidPayment(int id, string? reason)
    {
        var result = await _payments.VoidAsync(id, reason);
        TempData[result.Ok ? "PaymentResult" : "PaymentError"] =
            result.Ok ? $"Voided {result.PaymentNumber}." : result.Error;
        return RedirectToAction("Payments");
    }

    // ---------------------------------------------------------------- Settings
    /// <summary>
    /// The signed-in user, for "who attached this". Same two claims DmeAudit
    /// reads, so a document and its audit row always name the same person.
    /// Null rather than a guess when neither claim is present: an attribution
    /// that might be wrong is worse than none on a document that defends a claim.
    /// </summary>
    private int? CurrentUserId()
    {
        var raw = User.FindFirst("UserId")?.Value
               ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        return int.TryParse(raw, out var id) ? id : null;
    }

    /// <summary>
    /// Attach a proof of delivery to an order: the ticket the driver
    /// photographed, the signed paper the customer scanned, the carrier's
    /// paperwork on a drop-shipped item.
    ///
    /// The document is what defends the claim in an audit, so it is worth an
    /// audit row of its own. The file itself is validated, encrypted and stored
    /// by DmeOrderDocuments; nothing about that is repeated here.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(DmeOrderDocuments.MaxFileBytes + 1024 * 1024)]
    public async Task<IActionResult> AttachPod(int id, IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            TempData["PodError"] = "Choose a file to attach.";
            return RedirectToAction("Order", new { id });
        }

        // Read once into memory. The cap is 25MB and the bytes have to be hashed
        // AND encrypted, so streaming would buy nothing but complexity.
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);

        var result = await _documents.AttachAsync(
            id, file.FileName, file.ContentType ?? "application/octet-stream", buffer.ToArray(), CurrentUserId());

        if (!result.Success)
        {
            TempData["PodError"] = result.Error;
            return RedirectToAction("Order", new { id });
        }

        await _audit.RecordAsync("DME_POD_ATTACHED", "DmeOrder", id,
            before: null,
            after: new { result.DocumentId, file.FileName, Size = file.Length });

        return RedirectToAction("Order", new { id });
    }

    /// <summary>
    /// Send an attached document back to the browser.
    ///
    /// Deliberately NOT a signed storage URL. A signed URL is valid for anyone
    /// holding it and leaves both the tenant check and the audit trail behind.
    /// This action authenticates, is scoped by row level security through the
    /// view, and writes a PHI read row like every other read in this controller.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> DownloadPod(int id)
    {
        var doc = await _documents.OpenAsync(id);
        if (doc == null) return NotFound();

        // Inline so a PDF or a photo opens in the browser instead of landing in
        // the downloads folder, which is what somebody checking a delivery wants.
        Response.Headers.ContentDisposition =
            $"inline; filename=\"{Uri.EscapeDataString(doc.Value.FileName)}\"";

        return File(doc.Value.Bytes, doc.Value.ContentType);
    }

    /// <summary>
    /// Take a document off an order.
    ///
    /// The ROW survives with a DeletedAt: the record that a proof of delivery
    /// was attached and then withdrawn is itself evidence. The bytes do not.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "0,1")]
    public async Task<IActionResult> RemovePod(int id, int orderId)
    {
        if (await _documents.RemoveAsync(id))
        {
            await _audit.RecordAsync("DME_POD_REMOVED", "DmeOrder", orderId,
                before: new { DocumentId = id, Attached = true },
                after: new { DocumentId = id, Attached = false });
        }

        return RedirectToAction("Order", new { id = orderId });
    }

    /// <summary>
    /// Attach a document to a customer: the CMN, the prescription, a photo of
    /// the insurance card, the referral that started the whole thing.
    ///
    /// Separate from the customer form on purpose. The form posts and redirects,
    /// so attaching through it would mean a failed upload had to be reported
    /// alongside a saved customer, and an operator with four documents would
    /// save the customer four times. This attaches one file and comes straight
    /// back to the same screen.
    ///
    /// The file is validated, encrypted and stored by DmeCustomerDocuments;
    /// nothing about that is repeated here.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(DmeDocumentStore.MaxFileBytes + 1024 * 1024)]
    public async Task<IActionResult> AttachCustomerDoc(int id, string kind, IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            TempData["DocError"] = "Choose a file to attach.";
            return RedirectToAction("EditCustomer", new { id });
        }

        // Read once into memory. The cap is 25MB and the bytes have to be hashed
        // AND encrypted, so streaming would buy nothing but complexity.
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);

        var result = await _customerDocs.AttachAsync(
            id, kind, file.FileName, file.ContentType ?? "application/octet-stream",
            buffer.ToArray(), CurrentUserId());

        if (!result.Success)
        {
            TempData["DocError"] = result.Error;
            return RedirectToAction("EditCustomer", new { id });
        }

        // A CMN is what defends an oxygen claim in an audit, so attaching one is
        // worth a row of its own. The file NAME is not recorded: it is PHI, and
        // copying it into the audit log would widen the blast radius of a leak.
        await _audit.RecordAsync("DME_CUSTOMER_DOC_ATTACHED", "DmeCustomer", id,
            before: null,
            after: new { result.DocumentId, Kind = kind, Size = file.Length });

        return RedirectToAction("EditCustomer", new { id });
    }

    /// <summary>
    /// Send an attached customer document back to the browser.
    ///
    /// Deliberately NOT a signed storage URL, for the same reason DownloadPod is
    /// not: a signed URL is valid for anyone holding it and leaves both the
    /// tenant check and the audit trail behind.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> DownloadCustomerDoc(int id)
    {
        var doc = await _customerDocs.OpenAsync(id);
        if (doc == null) return NotFound();

        // Inline so a PDF or a photo opens in the browser instead of landing in
        // the downloads folder, which is what somebody checking a CMN wants.
        Response.Headers.ContentDisposition =
            $"inline; filename=\"{Uri.EscapeDataString(doc.Value.FileName)}\"";

        return File(doc.Value.Bytes, doc.Value.ContentType);
    }

    /// <summary>
    /// Take a document off a customer.
    ///
    /// The ROW survives with a DeletedAt: that a document was attached and then
    /// withdrawn is itself a fact worth keeping. The bytes do not.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = DmeRoles.Admin)]
    public async Task<IActionResult> RemoveCustomerDoc(int id, int customerId)
    {
        if (await _customerDocs.RemoveAsync(id))
        {
            await _audit.RecordAsync("DME_CUSTOMER_DOC_REMOVED", "DmeCustomer", customerId,
                before: new { DocumentId = id, Attached = true },
                after: new { DocumentId = id, Attached = false });
        }

        return RedirectToAction("EditCustomer", new { id = customerId });
    }

    /// <summary>
    /// Who this supplier buys from, for the items they never hold themselves.
    ///
    /// Admin roles only: which distributors the business deals with is a
    /// commercial relationship, the same kind of decision as item pricing.
    /// </summary>
    [Authorize(Roles = "0,1")]
    public IActionResult Distributors()
    {
        ViewData["Title"] = "Distributors";
        ViewData["ActivePage"] = "distributors";
        LoadLocationContext();

        // Retired ones included: the screen is where you see the whole history
        // of who you have bought from, and where you retire the current ones.
        ViewBag.Distributors = _distributors.All(includeRetired: true);
        ViewBag.DistributorError = TempData["DistributorError"];
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "0,1")]
    public async Task<IActionResult> RestoreDistributor(int id)
    {
        if (!_distributors.Restore(id))
        {
            TempData["DistributorError"] = "That distributor is already in service.";
            return RedirectToAction("Distributors");
        }

        await _audit.RecordAsync("DME_DISTRIBUTOR_RESTORED", "DmeDistributor", id,
            before: new { Retired = true }, after: new { Retired = false });

        return RedirectToAction("Distributors");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "0,1")]
    public async Task<IActionResult> EditDistributor(
        int id, string? name, string? accountNo, string? phone, string? email)
    {
        var before = _distributors.Find(id);
        if (before == null)
        {
            TempData["DistributorError"] = "That distributor is not on your list.";
            return RedirectToAction("Distributors");
        }

        if (!_distributors.Update(id, name, accountNo, phone, email))
        {
            TempData["DistributorError"] = string.IsNullOrWhiteSpace(name)
                ? "Give the distributor a name."
                : $"You already have a distributor called '{name.Trim()}'.";
            return RedirectToAction("Distributors");
        }

        await _audit.RecordAsync("DME_DISTRIBUTOR_EDITED", "DmeDistributor", id,
            before: new { before.Name, before.AccountNo, before.Phone, before.Email },
            after: new { Name = name!.Trim(), AccountNo = accountNo, Phone = phone, Email = email });

        return RedirectToAction("Distributors");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "0,1")]
    public async Task<IActionResult> AddDistributor(string? name, string? accountNo, string? phone, string? email)
    {
        var id = _distributors.Add(name, accountNo, phone, email);
        if (id == null)
        {
            TempData["DistributorError"] = string.IsNullOrWhiteSpace(name)
                ? "Give the distributor a name."
                : $"You already have a distributor called '{name.Trim()}'.";
            return RedirectToAction("Distributors");
        }

        await _audit.RecordAsync("DME_DISTRIBUTOR_ADDED", "DmeDistributor", id.Value,
            before: null, after: new { Name = name!.Trim(), AccountNo = accountNo });

        return RedirectToAction("Distributors");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "0,1")]
    public async Task<IActionResult> RetireDistributor(int id)
    {
        // Snapshot first: after the update the row no longer says it was live.
        var before = _distributors.Find(id);
        if (before == null || before.IsRetired) return RedirectToAction("Distributors");

        if (_distributors.Retire(id))
        {
            await _audit.RecordAsync("DME_DISTRIBUTOR_RETIRED", "DmeDistributor", id,
                before: new { before.Name, Retired = false },
                after: new { before.Name, Retired = true });
        }

        return RedirectToAction("Distributors");
    }

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

    // Money is the biller's and the owner's. Delivery and Intake never see it:
    // the person who hands equipment over must not also be the person who
    // records what was paid for it.
    [Authorize(Roles = DmeRoles.Money)]
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
        ViewBag.PrimaryIns = cust == null ? null : Insurance(F.I(cust["CustomerId"]), "primary");
        // Box 33 used to be a hardcoded string in the view, so every claim this
        // product produced carried a made up NPI. It comes from the supplier
        // record now, and the screen says plainly when that record is not
        // filled in rather than printing something plausible.
        ViewBag.Provider = BillingProvider();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    // Money is the biller's and the owner's. Delivery and Intake never see it:
    // the person who hands equipment over must not also be the person who
    // records what was paid for it.
    [Authorize(Roles = DmeRoles.Money)]
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
