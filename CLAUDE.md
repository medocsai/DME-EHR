# MEDOCS DME — Claude Instructions

> **Start here: [docs/HANDOFF.md](docs/HANDOFF.md)** — current state, what was
> built and why, the traps, and the open decisions. This file is the day-to-day
> operating detail.
>
> **Open threads: [docs/OPEN-THREADS.md](docs/OPEN-THREADS.md)** — the parking
> lot. Everything raised and not yet closed, one thread at a time. Read it before
> starting anything, and update it when a thread opens or closes.

DME/HME (Durable Medical Equipment) platform. **Standalone application** — the
clinical EHR it was originally copied from was removed on 2026-08-25.

## Publish & Deploy
- **Publish path:** `D:\Professional Work\Servers\Medocs\DME Publish`
- **Publish command:**
  ```
  dotnet publish "D:\Professional Work\EHR Systems\imehr-dme\ehr-system\EHR.csproj" -c Release -o "D:\Professional Work\Servers\Medocs\DME Publish"
  ```
- Backend (`EHR.dll`) changes only take effect after the app process is **restarted/recycled** on the server. Static `wwwroot` assets load on a browser hard-refresh.

## Run (local)
```
cd "D:\Professional Work\EHR Systems\imehr-dme\ehr-system"
dotnet run
```
App runs at **http://localhost:5005**.
- **Demo login:** `admin@md.com` / `DemoPass@2026`
- **OTP (dev):** `123456` — hardcoded in Development via `AuthService.cs` (`_isDevelopment`).

## Project Structure
- **Backend:** ASP.NET Core 8 (`ehr-system/`)
- **Frontend:** Vanilla JS modules + Bootstrap 5 (`ehr-system/wwwroot/js/`)
- **Database:** SQL Server — `Server=localhost\SQLEXPRESS01;Database=DMEEHR`
  (NOTE: the default `localhost` instance does NOT exist on this machine — everything is on `SQLEXPRESS01`.)

## DME-specific architecture (added on top of the IMEHR shell)
- **Controller:** `Controllers/DmeController.cs` — Dashboard, Schedule (delivery calendar),
  Customers, Customer/{id}, NewCustomer/CreateCustomer, Inventory, Orders, Order/{id},
  NewOrder/CreateOrder, Deliver (POD → auto-creates rental + claim), Rentals/BillNow,
  Billing, Cms/{id}, Submit. Plus `Controllers/HcpcsController.cs` (HCPCS catalog).
- **Data layer:** `Helpers/DmeDb.cs` — thin raw-ADO (Microsoft.Data.SqlClient) against the
  DME tables, deliberately separate from the generated EF DbContext. `F` = view formatter
  (USD-locked) + `F.StatusChip`. Connection string wired in `Program.cs` via `DmeDb.Init`.
- **Views:** `Views/Dme/*.cshtml`, styled with `wwwroot/css/dme.css`.
- **Nav / branding:** DME nav + "MEDOCS DME / DME SUITE" branding live in
  `Views/Shared/_Layout.cshtml`. Clinical pages still exist but are unlinked at `/Home/*`.
- **Schema:** `Migrations/Manual/2026-06-19_DME_Core_Schema.sql` creates + seeds all DME
  tables (DmeCustomers, DmeOrders, DmeOrderLines, DmeRentals, DmeClaims, DmeClaimLines,
  DmeSerializedUnits, DmeCmns, DmeDoctors, DmePayers, HcpcsCodes, DmeSeq).

## JS / CSS Cache Busting
All JS/CSS are loaded in `Views/Shared/_Layout.cshtml` with `?v=N`. When you edit a `.js` or
`.css` file, bump its `?v` in `_Layout.cshtml` so browsers reload it.

## Foundation (added 2026-08-25, branch foundation/security-and-ssot)

### Security
- **Auth on DME pages.** `DmeController` and `HcpcsController` carry `[Authorize]`.
  Two authentication schemes sit behind one policy scheme (`MedocsSmartAuth`,
  `Configuration/JwtBearerSetup.cs`): `Bearer` for the SPA, `SessionCookie` for
  server-rendered pages. A page navigation sends no Authorization header, so the
  JWT is mirrored into an HttpOnly SameSite=Strict cookie at verify-otp and
  refresh, and cleared at logout (`Helpers/SessionCookie.cs`). A 401 on a browser
  GET redirects to `/` instead of returning a blank body.
- **Tenant isolation.** SQL Server row level security (`TenantIsolationPolicy`)
  covers every DME table with FILTER (reads) and BLOCK (writes) predicates.
  `Helpers/DmeDb.cs` is request-scoped, sets `SESSION_CONTEXT(CurrentTenantId)`
  on every connection, and THROWS if the tenant is unknown; the policy treats
  "no context" as "show everything", so an unscoped connection must never happen.
  `` is injected into every command automatically.
- **PHI at rest.** DME customer PHI is AES-GCM encrypted via the shared
  `EncryptionHelper` (`Helpers/DmeCustomerPhi.cs` owns the column list). Search
  works through a blind index (`DmeCustomerSearchTokens`), prefix-hashed the same
  way `BlindIndexService` does it for patients. `POST /Dme/BackfillPhi` (admin
  only) encrypts existing rows and is safe to re-run.
- **Audit.** `[PhiAccessAudit]` logs every successful read. `Services/DmeAudit.cs`
  logs every mutation with before/after state, user and IP, into `AuditLogs`.

### Single source of truth
`MonthsBilled`, `OnHand`, `Claims.Total`, and the copied `CustomerName` /
`DoctorName` columns were **dropped**. They are computed by the views
`vDmeRentals`, `vDmeClaims`, `vDmeOrders`, `vHcpcsCatalog`. **Read through the
views, never the base tables.** Two facts that had no home are now stored:
`DmeClaimLines.RentalId` (which rental a month billed) and `DmeStockMovements`
(the stock ledger). Point-in-time facts are deliberately still stored and must
stay: `DmeClaims.CustomerName`/`PayerName`, line prices, `DmeRentals.MonthlyRate`.

**Trap:** the views return `CustomerFirstName` + `CustomerLastName` separately.
Two ciphertexts concatenated in SQL cannot be decrypted. Always call
`_phi.ComposeCustomerNames(rows)` after reading `vDmeOrders` / `vDmeRentals`,
or the screen renders base64.

### Onboarding a new tenant (SaaS)
Super Admin creates the tenant at `/Home/Tenants` (`POST /api/tenants`, role 0 only).
That creates the tenant, a default location and an admin user.

**The national code sets need nothing:** payers, ICD-10-CM, HCPCS Level II and
CARC codes are global, so a new tenant has all of them the moment it exists.

What it still does NOT create is the supplier's own **item master**
(`HcpcsCodes`, their items at their prices), so a new tenant cannot raise an
order until you run `Migrations/Manual/DME_Onboard_New_Tenant.sql` by hand (set
`@TargetTenantId`), or add items by hand on `/Hcpcs`.
Deliberately a script, not a button: every DME connection is pinned to the
caller tenant and RLS BLOCKs cross-tenant writes, so an in-app version would
have to punch a hole through the isolation. Order/claim numbering needs no
seeding.

### The database (32 tables, nothing unused)
DME owns 21 tables plus `HcpcsCodes` (the supplier's item master),
`DmeSupplierProfile` and `DmeSftpAccounts`. Four are GLOBAL national reference
data with no tenant column: `DmeCarcCodes`, `DmePayers`, `IcdCodes` and
`HcpcsNationalCodes`. The platform is `Users`,
`Tenants`, `Locations`, `UserLocations`, `AuditLogs`, `TrustedDevices`. The clinical EHR this
product was copied from was removed on 2026-08-25: 78 tables, 634 patient records
and 1,745 clinical claims that DME never read. Do not reintroduce them.

### Migrations (run in this order on a fresh database)
1. `2026-06-19_DME_Core_Schema.sql`
2. `2026-08-25_DME_Tenant_Isolation.sql`
3. `2026-08-25_DME_Single_Source_Of_Truth.sql`
4. `2026-08-25_DME_Customer_PHI_Encryption.sql`
5. `2026-08-25_Drop_Clinical_Schema.sql` (only on a database forked from IMEHR)
6. `2026-08-26_DME_Payments_And_Denials.sql`
7. `2026-08-26_DME_Supplier_And_Sftp.sql`
8. `2026-08-26_DME_Locations.sql`
9. `2026-08-26_DME_User_Locations.sql`
10. `2026-08-27_DME_Payer_Catalog.sql`
11. `2026-08-27_DME_Icd10_Catalog.sql`
12. `2026-08-27_DME_Hcpcs_Catalog.sql`
13. `2026-08-27_DME_Drop_Ship.sql`
Then `POST /Dme/BackfillPhi` once as an admin. Verified end to end on a scratch
database. Note `ALTER SECURITY POLICY` and any batch naming a dropped column are
validated at COMPILE time, so `IF NOT EXISTS` guards do not protect them: use
`sp_executesql`.

## Payments and denials (added 2026-08-26)

Decisions and the client-facing wording: **`docs/BILLING-DECISIONS.md`**.

- **Money in is MANUAL POSTING.** `Services/DmePaymentService.cs` is the only
  writer. Payer money and customer money land in one model. There is no 835/ERA
  parser and no clearinghouse account, so `/Dme/Submit` is the one thing left
  blocked, and the Billing screen says so.
- **Three tables.** `DmePayments` (one receipt), `DmePaymentLines` (one claim
  line adjudicated: `AllowedAmount` + `PaidAmount`, nothing else), and
  `DmePaymentLineAdjustments` (the X12 CAS segment: group `CO`/`PR`/`OA`/`PI`,
  CARC reason code, amount). `DmeCarcCodes` is the national code list and is
  deliberately NOT tenant scoped, so a new tenant gets it free.
- **Only two amounts are stored.** Adjustment total, patient responsibility,
  denied status, claim balance and every dashboard figure are computed in
  `vDmePaymentLines`, `vDmeClaimLines`, `vDmePayments` and `vDmeClaims`.
  **Nothing is ever written back to `DmeClaims`.**
- **`DmeClaims.Status` is the SUBMISSION lifecycle only** (`ready` |
  `submitted`), enforced by `CK_DmeClaims_Status`. The payment outcome is
  `vDmeClaims.PaymentStatus`: `unpaid` | `partial` | `part-denied` | `denied` |
  `patient-due` | `paid`.
- **Denied means paid nothing AND a `CO`/`PI` reason.** A line paid nothing
  because it went to the patient's deductible carries `PR` and is not a denial.
  A `CO-45` discount on a line that DID pay is not a denial either.
- **A payment is voided, never edited or deleted.** The views exclude voided
  rows, which is the whole reversal mechanism. `VoidedAt` is the only stored
  fact; `IsVoided` is derived.
- **A void needs a reason** and the UPDATE is guarded on `VoidedAt IS NULL`, so
  two people clicking Void produce one reversal.
- Onboarding a new tenant needs nothing extra: the CARC list is global and the
  `PMT` number sequence creates itself on first use.

**Trap:** the tiles count APPLIED money (payment lines), not the face value of
receipts. A check posted but not allocated makes the paid tile read low, which is
why `/Dme/Payments` shows `UnappliedAmount` and the dashboard footnotes it.

## Supplier identity and clearinghouse credentials (added 2026-08-26)

`/Dme/Settings`, admin roles only. Migration
`2026-08-26_DME_Supplier_And_Sftp.sql`.

- **The CMS-1500 billing provider was hardcoded in the Razor view**
  (`Lakeview Medical Supply · NPI 1980000000`), so every claim carried an NPI
  belonging to nobody. Boxes 25, 27, 32 and 33 now read `vDmeBillingProvider`.
  `DmeClearinghouseCredentialTests` fails if a ten digit number reappears in
  `Cms.cshtml`.
- **Box 17 / 17b, the ordering physician, was missing entirely.** It is
  mandatory on DMEPOS. Derived by join in `vDmeClaims`
  (`OrderingDoctorName` / `OrderingDoctorNpi`) from the claim's order.
- **Six of nine billing-provider fields already lived on `Tenants`**, so only
  `Ptan`, `TaxonomyCode` and `AcceptsAssignment` are stored, in
  `DmeSupplierProfile` (TenantId is the primary key). `vDmeBillingProvider`
  joins the rest and computes `IsComplete`.
- **`dbo.Tenants` is NOT in the RLS policy.** It is the platform registry. Any
  UPDATE against it must carry `WHERE TenantId=@TenantId` or it rewrites every
  tenant on the server. A test enforces this on `DmeController`.
- **Clearinghouse credentials are TENANT scoped, not location scoped.** No DME
  table carries a `LocationId`. RehabDox looks location based and is not: its
  `OfficeAllySftpAccounts` is tenant owned and `Locations` only points at it.
  The reasoning is in the migration header.
- **`DmeSftpAccounts.Username` and `Password` are both AES-GCM ciphertext.**
  `Services/DmeSftpAccountService.cs` is the only code that touches the base
  table; everything else reads `vDmeSftpAccounts`, which does not expose the two
  columns at all. There is deliberately no decrypt path yet: it arrives with the
  837 sender that needs it.
- **`IsTestMode` defaults to 1** and that is a safety property. Going live is a
  deliberate edit, because a supplier is onboarded in test mode first.
- **An account is taken out of service, never deleted.** The row is the record
  of what past claims were submitted under.
- **A claim cannot be marked submitted while the supplier has no name, NPI or
  tax ID.** Guarded in `Submit`, not only on the settings page.

**`/Dme/Submit` does not transmit anything.** The button says "Mark as
submitted" because that is what it does. Building and sending the 837 file is
separate work that needs a live clearinghouse account to test against.

## Payer catalog (added 2026-08-27)

The client's words: "List of insurance is not exhaustive." It had four rows.
Migration `2026-08-27_DME_Payer_Catalog.sql`, service
`Services/DmePayerCatalog.cs`.

- **`dbo.DmePayers` is now Office Ally's national list of 4,017 payers**, taken
  from the same export RehabDox loads
  (`rehabdox-webapp/ehr-system/App_Data/payers_import.json`, read only, never
  modified).
- **It is GLOBAL, like `DmeCarcCodes`.** No `TenantId`, out of
  `TenantIsolationPolicy`, and `DME_Onboard_New_Tenant.sql` no longer copies it.
  A national list copied per supplier is 4,017 duplicates of a fact none of them
  owns. The tenant fact is which payer a CUSTOMER is insured with, and that
  already lives on `DmeCustomerInsurances`.
- **`PayerName` and `PayerId` stay STORED on the insurance record.** That is the
  point-in-time record of what a claim was billed under, and it must not move if
  the vendor later corrects a name.
- **`PayerCode` is indexed but NOT unique.** Office Ally issues `ALLCA` to two
  different payers. A unique constraint would reject their own list.
- **`PayerType` was dropped.** Nothing read it. CMS-1500 box 1 is not
  implemented at all, so it was not feeding that either.
- **There is deliberately no "add your own payer".** A payer off the list has no
  Payer ID, so no 837 can be addressed to it; it would look billable and fail
  weeks later. Missing payers are added centrally.
- **The form posts an id, never a name.** `/Payers/Search` feeds the typeahead;
  `CreateCustomer` reads the name and code back out of the catalog. A posted
  name would let a typo become the payer on a claim.
- **Search matches every WORD, in any order, and knows Blue Cross means BCBS.**
  145 rows spell it BCBS and 18 spell it Blue Cross, so without the alias a
  biller typing what is printed on the card finds almost nothing. A search
  nobody can hit is the same complaint again.

## Drop shipping (added 2026-08-27)

The client, under Inventory: "Most of the items we deliver are drop-shipped from
manufacturer/distributors." Migration `2026-08-27_DME_Drop_Ship.sql`, service
`Services/DmeDistributors.cs`, screen `/Dme/Distributors`.

**The fix is mostly an ABSENCE.** A drop-shipped line writes **no stock
movement and no serialised unit**, because nothing entered or left a warehouse.
On-hand therefore stays correct by construction. `Deliver` skips both writes via
`if (dropShipped) continue;`, and that `continue` sits **after** the rental and
claim line: a drop-shipped item is still delivered, still rented, still billed.
Move the guard up by three lines and you silently stop billing the majority of
this supplier's business. Two tests pin the ordering in both directions.

- **Two columns, on the ORDER LINE**, because the same item ships from stock one
  week and direct the next: `DistributorId` and `DistributorRef` (their order or
  tracking number).
- **There is NO is-drop-shipped flag.** It is derived from `DistributorId` being
  present. A stored flag would be a second copy of the same fact.
- **`dbo.DmeDistributors` is TENANT data**, unlike the payer, ICD and HCPCS
  catalogs. Those are national lists; each supplier negotiates its own
  distributors. In the RLS policy like every other DME table.
- **A distributor is retired, never deleted** (`RetiredAt`, `IsRetired`
  derived). `Find` deliberately does NOT filter retired ones, so an old order
  still says who shipped it; only the picker hides them.
- **A posted `distributorId` is checked against the tenant** before it is filed,
  exactly like the branch on the customer form.
- **Inventory shows a third panel, `vDmeDropShipments`**, kept separate from
  both stock numbers: an item in somebody else's warehouse is not stock this
  supplier holds. Arrival is derived from the order's status, not a second date.
- **An all drop-shipped order asks for no signature.** Nobody from this supplier
  is at the door; the tracking reference is the delivery evidence. A MIXED order
  still asks, because somebody is there with part of it.

**Trap:** the signature script is only emitted when a canvas exists. Without the
`@if (!allDropShipped)` guard it throws on the first `getElementById` and takes
the form's submit handler down with it.

**Trap, and it is older than this work:** `Users`, `Locations` and
`DmeClaimLines` carry FILTERED indexes, which makes every INSERT, UPDATE and
DELETE against them require `QUOTED_IDENTIFIER ON`. `sqlcmd` defaults it OFF, so
a maintenance script against those tables fails with an error naming neither the
index nor the table's purpose. **Run `sqlcmd -I`.** The verification script uses
`$SQLW` for exactly this. `DmeOrderLines` was deliberately kept out of that set:
its distributor index is unfiltered even though a filter would be tidier.

## Code catalogs: ICD-10-CM and HCPCS (added 2026-08-27)

The client said three lists were "not exhaustive". Payers was one; these are the
other two. Migrations `2026-08-27_DME_Icd10_Catalog.sql` and
`2026-08-27_DME_Hcpcs_Catalog.sql`.

**Both come from CMS, free, and both are GLOBAL** (no `TenantId`, outside the
RLS policy, not copied by the onboarding script), for the same reason the payer
list is: a national code set is nobody's private copy.

### ICD-10-CM (`dbo.IcdCodes`, 74,719 codes)
- Was **twelve codes hardcoded in a C# array** in `DmeController`.
- Source: `https://www.cms.gov/files/zip/2026-code-descriptions-tabular-order.zip`
  → `icd10cm_codes_2026.txt`. **The codes file, not the order file**: it holds
  only codes VALID FOR SUBMISSION. E66.9 is in it, the E66 header it sits under
  is not, and a header code on a claim is a denial. A test pins this.
- **The dot is stored** (`E66.9`), matching what `DmeCustomerDiagnoses` already
  held and what a human reads. Search and lookup strip dots from BOTH sides, so
  `E669` and `E66.9` find the same row.
- **`DmeCustomerDiagnoses` keeps its own copy of the description.** That is not
  a duplicate: CMS rewords codes every October, and what a claim was billed
  under must not move. Same reasoning as `DmeClaims.CustomerName`.
- **Refreshed every October 1.** Reloading is the same script with a new VALUES
  block; it updates changed wording and removes retired codes.

### HCPCS Level II (`dbo.HcpcsNationalCodes`, 8,623 codes)
**The important part: this is NOT `dbo.HcpcsCodes`.**

| | |
|---|---|
| `dbo.HcpcsNationalCodes` | what CMS publishes. Global. Codes and claim wording. |
| `dbo.HcpcsCodes` | the SUPPLIER'S item master. Tenant scoped. Their price, their rental terms, their stock. |

Pouring 8,623 national codes into the item master would have left every row with
blank pricing and made a national list into tenant data. Adding an item is now
"find the code, set your price", via **Add item** on `/Hcpcs`.

- Source: `https://www.cms.gov/files/zip/january-2026-alpha-numeric-hcpcs-file.zip`.
  Fixed width; the layout file is in the same zip.
- **Only record identifiers 3 and 4** are loaded. 7 and 8 are MODIFIER records,
  and a modifier is not something a supplier stocks. Identifier 4 continues the
  previous code's long description and is appended.
- **Level II only, which also avoids a copyright problem.** Level I is CPT and
  its descriptions are AMA copyright; the CMS record layout says so. Every code
  in the alpha-numeric file is letter-prefixed. A test fails on a numeric one.
- **`LongDescription` is `NVARCHAR(MAX)`** because some G codes run past 4,000
  characters, so it cannot be indexed. At 8,623 rows that is fine.
- **Terminated codes are KEPT**, unlike ICD. A supplier's item master may
  already point at one, and "retired on 2024-12-31" is a better answer than a
  blank. `TerminatedOn` is stored; `IsRetired` is derived from it.
- **`AddItem` refuses** an invented code, a retired code, and a duplicate. Admin
  roles only, audited as `DME_CATALOG_ITEM_ADDED`.
- **No foreign key** from the item master to the national list, deliberately: a
  quarterly CMS release that dropped a code would then fail to load while any
  supplier still stocked it. The check lives in `DmeHcpcsCatalog`, where it can
  give a person a sentence instead of a constraint violation.

### The shared plumbing
- **`Controllers/LookupsController.cs`** serves all three typeaheads
  (`/Lookups/Payers`, `/Lookups/Icd`, `/Lookups/Hcpcs`).
- **`Helpers/SearchTerm.cs`** holds what is identical across them: word
  splitting, noise words, and LIKE escaping.
- **`wwwroot/js/dme/Typeahead.js`** is the one picker. It abandons the previous
  choice when you type again, and drops a slow reply to an older keystroke.

**Trap:** `LookupsController` is separate from `DmeController` because that
class carries `[PhiAccessAudit]`. A typeahead fires per keystroke and would bury
the real PHI access trail under thousands of rows about a customer nobody read.

**Trap:** `Typeahead.js` is loaded in the `<head>`, not with the other scripts at
the end of `<body>`. The DME views call `dmeTypeahead()` from an inline script
inside their own markup, and an inline script runs during parsing, so a tag at
the end of the body defines the function AFTER the call that needs it. Both
pickers then silently do nothing.

**Trap:** `.dme-card` sets `overflow:hidden` for its rounded corners, so an open
results list is clipped and the next card paints over it. `Typeahead.js` adds
`.dme-typeahead-open` to the card while a list is showing, which lifts it.

## Locations (added 2026-08-26)

Reasoning and the RehabDox evidence: **`docs/OPEN-THREADS.md`**, thread 2.
Migration `2026-08-26_DME_Locations.sql`.

One supplier, many branches. **The tenant is the security boundary; the location
is a working filter inside it.**

- **`LocationId` is stored on exactly THREE tables**, and a test enforces it:
  `DmeCustomers` (a person belongs to a branch), plus `DmeStockMovements` and
  `DmeSerializedUnits` because stock is physical and belongs to no customer.
  Orders, rentals, claims, claim lines and payments all DERIVE it by join.
- **Why not everywhere.** RehabDox put it on the children:
  `Appointments.LocationId` is NULL on 164 of 165 rows, and their filter had to
  become a three-way fallback repeated in eight query sites. All three columns
  here are NOT NULL, so there is nothing to drift into.
- **Location is NEVER in the row level security policy.** If it were, the owner's
  all-branches roll-up would need a hole punched through tenant isolation.
- **`@LocationId` is on every command**, like `@TenantId`. Filters are written
  `(@LocationId IS NULL OR LocationId = @LocationId)`. **The `IS NULL` half is
  mandatory:** a bare equality returns nothing at all for the all-branches view,
  because NULL never equals anything. A test fails on any filter missing it.
- **Switching branch goes through `/api/locations/switch`**, which issues a new
  token AND re-issues the session cookie. Without the cookie half only the SPA
  followed, and every server-rendered DME screen ignored the switch.
- **`LocationId = 0` means all branches**: a token with no LocationId claim. Not
  a permission, since a user who can switch to each branch one at a time can
  already see everything.
- **The active branch comes from the TOKEN**, never from localStorage. The module
  used to treat a falsy id as "none set" and snap back to the primary, so the
  header said "Main Office" while the server was returning every branch.
- **A posted `locationId` is checked against the tenant** before a customer is
  filed into it. RLS covers `TenantId` on the insert and would accept a foreign
  `LocationId` sitting beside it.
- **Inventory shows two numbers:** what the company holds and what this branch
  holds (`vDmeStockByLocation`). One number answers neither question when the
  wheelchair is 200 miles away.

**Trap:** `Locations` had TWO rows flagged `IsPrimary` for tenant 1. Everything
defaults to "the tenant's primary branch", so the migration normalises that and
adds `UX_Locations_OnePrimaryPerTenant` to stop it recurring.

**Trap:** the location switcher modal lives in `_Layout.cshtml`. It was lost in
the 2026-08-25 modal cleanup, so clicking the location in the sidebar silently
did nothing until it was put back.

### Per-user branch grants (added 2026-08-26)

`dbo.UserLocations`, migration `2026-08-26_DME_User_Locations.sql`. Same shape as
RehabDox: `(UserLocationId, UserId, LocationId, CreatedAt)`, no TenantId, because
the tenant comes through the user.

- **An empty grant set sees NOTHING.** `Services/DmeLocationScope.cs` renders
  `1=0` for a restricted caller with no grants. The shape to never write is
  `if (allowed.Any()) { ...Where... }`, which drops the filter for exactly the
  caller it was meant to contain. A test scans for it.
- **Roles 0 and 1 bypass branch scoping**, and that is a decision. A clinic
  admin administers the whole supplier and is usually the owner asking for the
  roll-up. Roles 2 and up are restricted. `DmeUserLocationScopeTests` pins it.
- **An unreadable role is treated as the MOST restricted**, never the least.
- **`_db.LocationScope(column)`** is what every DME read carries: what the caller
  chose AND what they may see. **`_db.LocationGrants(column)`** is the permission
  half alone, for lists of branches, where the chosen half would reduce the
  switcher to the branch already selected.
- **Login picks the default branch from the grants.** It used to hand out the
  tenant's primary branch, which a restricted user might not hold, producing a
  session that was signed in and completely empty with nothing explaining why.
- **Creating a restricted user with no branch is refused** on the server, not
  only in the form.
- **The migration backfills**: every existing active user was granted every
  active branch of their tenant, so nobody lost access the day it ran. There were
  27 restricted users; an empty table would have been an outage.

**Trap:** `DmeDb` puts an `@LocationId` on EVERY command, holding the branch the
caller is VIEWING, and SQL parameter names are case insensitive. So a write that
names its own `@locationId` is naming that same injected parameter. The customer
insert did exactly that and passed no value of its own, so filing a customer
into Dallas while viewing Houston put them in Houston, and viewing all branches
made it NULL and 500'd the save. Any INSERT that stores a CHOSEN branch must use
a different parameter name (`@branchId`) and pass the validated value. Pinned by
`DmeLocationScopingTests.TheBranchThatWasCheckedIsTheBranchThatIsWritten`.

**Trap:** `LocationScope` returns a predicate containing `@LocationId`. Qualify
the column with the parameter (`LocationScope("c.LocationId")`), never a string
replace on the result: that rewrites the PARAMETER too and yields SQL that parses
and matches nothing.

## Super admin (Medocs), added 2026-08-26

Role 0. **Belongs to no tenant**, which is why the DME screens need telling
which supplier they are looking at.

- **How a clinic is chosen.** The header switcher writes a `medocs_clinic`
  cookie, which `TenantResolutionMiddleware` reads as its LAST source. It used
  to write only localStorage and raise a JS event, which the SPA modules see and
  the server-rendered DME pages do not, so switching clinic did nothing to them.
- **Why the cookie is safe.** Every resolution step is guarded on the tenant
  still being unknown, and step 1 fills it from the token. So the cookie is only
  ever reached by an identity with no TenantId claim. A clinic admin forging it
  changes nothing. `SuperAdminTenantContextTests` pins this, and the guard was
  mutation tested.
- **Order matters.** `?tenantId=` beats the cookie: a link naming a tenant is an
  instruction for this request, a cookie is a preference from earlier. The gear
  icon on each clinic row uses that link.
- **No tenant chosen is a redirect, not a 500.**
  `Middleware/DmeTenantContextMiddleware.cs` sends them to `/Home/Tenants`,
  because `DmeDb` throws on a missing tenant and is constructed by DI before any
  action code runs.
- **SFTP credentials are role 0 only.** Supplier identity stays `0,1`: a clinic
  admin maintains their own name, NPI and tax ID. `DmeAuthorizationContractTests`
  pins the exact role list on each action.
- **Creating a clinic enforces the password policy.** `TenantService` validates
  before writing anything, because the tenant, its location and its admin are
  written across two SaveChanges calls and failing partway would leave a clinic
  with no administrator. Every admin password field now says 12, matching
  `PasswordPolicy.MinimumLength`.

### Hardening (same pass)
- **Staff password policy** (`Helpers/PasswordPolicy.cs`): 12 char minimum, no
  composition rules, maximum 64 (under BCrypt's 72-byte input limit). Applied at
  all four places a staff password is set. Previously there was none at all.
- **No exception detail to callers** (`Helpers/ApiError.cs`): use
  `this.ServerError(ex, "public message")` in a catch block. It logs the full
  exception and returns a correlation id. Stack traces and `ex.Message` in a 500
  body are blocked by a test.
- **Admin/clinical pages need auth.** `HomeController` is `[Authorize]` with
  `[AllowAnonymous]` on sign-in, password reset and error only; `/Home/Tenants`
  and `/Home/MedicalLienTemplates` are role 0.

### Secrets and session
- **`Configuration/SecretsGuard.cs`** refuses to start outside Development when
  `Jwt:Key` or `Encryption:Key` is missing, a known placeholder, or shorter than
  32 bytes. The hardcoded `"YourSecretKeyHere..."` fallback is gone: a silent
  fallback to a key printed in the source is worse than no key, because nothing
  looks wrong.
- **The keys currently in `appsettings.json` are in git history** and must be
  treated as compromised and rotated. Override per environment with `Jwt__Key`
  and `Encryption__Key`; ASP.NET Core layers env vars over appsettings. Rotating
  the encryption key means re-encrypting every encrypted row; rotating the JWT
  key logs everyone out. Both are ops decisions, deliberately not automated.
- **`HIPAA:SessionTimeoutMinutes` is now enforced** (it was read by nothing).
  It drives the access-token lifetime and therefore automatic logoff. Currently
  15 minutes. `AuthService.SessionMinutes` is the single source, because the
  session cookie is issued against the same expiry.

### Tests
`dotnet test ehr-system/EHR.Tests` — **303 passing**. It was 304 before the
clinical EHR was removed; 193 of those tested code that no longer exists. The DME suite is
in `EHR.Tests/Dme/`. Four SecurityOverhaul test files are excluded in the csproj
because they test `EHR.Services.Security`, which exists in IMEHR but was never
copied into this fork.

Run `bash scripts/verify-dme-foundation.sh` against a running app for the
end-to-end proof: **159 checks** with a super admin sign in, 144 without.

```bash
SUPERADMIN_EMAIL=you@example.com SUPERADMIN_PASSWORD=... bash scripts/verify-dme-foundation.sh
```

Sections 9, 10 and 13 create their own claim, payments, credential and customers
and delete them, so it is safe to re-run on any database. **A section that
cleans up must delete only what it created.** Section 13 takes
`MAX(CustomerId)` before it writes anything and deletes only above that line: an
earlier draft took "the newest customer" as the one it had just made, and when
the POST failed that was a SEEDED customer, which it then deleted. The Super Admin password is never
defaulted in the script; without it that half reports SKIP rather than counting
as passed.

## Constraints
- **NEVER touch** the IMEHR codebase (`..\imehr`), the rehabdox codebase, or the `IMEHR` /
  `PTEHR` databases. All work stays in `imehr-dme` + the `DMEEHR` database.

## Demo notes
- Core flow: New Customer → New Order (HCPCS items) → Deliver + POD signature →
  auto-bills the rental + queues the claim → Billing / CMS-1500. Schedule = delivery calendar.
- Deliberately faked (labeled in-app): live eligibility, 837/835 EDI clearinghouse.

## Working Style (READ FIRST, EVERY SESSION)
Read these before doing anything, every session:
- `D:\Common\Work Style\Must Rule.txt`
- `D:\Common\Work Style\workStyle.md`
- `D:\Common\Work Style\TEAM_WORKING_MODEL.md`
- `D:\Common\Work Style\single-source-of-truth.txt`

Short version: I am Hammas, the CTO. You are the PM. Shortest possible answers, section by
section. Compute, never store derived state. Hired-guy separation. Long-term fixes only.
No over-engineering, no work outside the paid scope. Never use em dashes.
