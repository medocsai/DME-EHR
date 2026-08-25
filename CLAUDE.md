# MEDOCS DME — Claude Instructions

DME/HME (Durable Medical Equipment) platform, converted from the IMEHR EHR.
Built as a **separate copy** of IMEHR so the live EHR is never touched.

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
That creates the tenant, a default location and an admin user. It does NOT create
DME reference data, and the HCPCS catalog and payer list are tenant-scoped, so a
new tenant has an empty catalog and cannot raise an order until you run
`Migrations/Manual/DME_Onboard_New_Tenant.sql` by hand (set `@TargetTenantId`).
Deliberately a script, not a button: every DME connection is pinned to the
caller tenant and RLS BLOCKs cross-tenant writes, so an in-app version would
have to punch a hole through the isolation. Order/claim numbering needs no
seeding.

### Migrations (run in this order on a fresh database)
1. `2026-06-19_DME_Core_Schema.sql`
2. `2026-08-25_DME_Tenant_Isolation.sql`
3. `2026-08-25_DME_Single_Source_Of_Truth.sql`
4. `2026-08-25_DME_Customer_PHI_Encryption.sql`
Then `POST /Dme/BackfillPhi` once as an admin. Verified end to end on a scratch
database. Note `ALTER SECURITY POLICY` and any batch naming a dropped column are
validated at COMPILE time, so `IF NOT EXISTS` guards do not protect them: use
`sp_executesql`.

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
`dotnet test ehr-system/EHR.Tests` — **304 passing, 1 skipped**. The DME suite is
in `EHR.Tests/Dme/`. Four SecurityOverhaul test files are excluded in the csproj
because they test `EHR.Services.Security`, which exists in IMEHR but was never
copied into this fork.

Run `bash scripts/verify-dme-foundation.sh` against a running app for the
end-to-end proof (39 checks).

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
