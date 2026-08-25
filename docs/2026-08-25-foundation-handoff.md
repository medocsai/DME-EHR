# DME Foundation — handoff

**Branch:** `foundation/security-and-ssot`
**Date:** 2026-08-25 (overnight session)
**Scope:** foundation only. No client feature requests were built.

---

## What was wrong

The DME product was a working, database-backed application sitting on top of the
inherited IMEHR shell, but it used none of that shell's security. Proven by
running it, not by reading it:

```
GET  /Dme/Customers  -> 200  names, phone numbers, account numbers
GET  /Dme/Customer/1 -> 200  home address, DOB, SSN last 4
POST /Dme/Submit     -> 302  claim CLM-02006 moved ready -> submitted
```

No token. No cookie. No audit row written for any of it.

Alongside that, three numbers were stored and trusted instead of computed, and
two had already drifted in the seed data before anyone touched the system:

| Value | Stored said | Truth was |
|---|---|---|
| `DmeRentals.MonthsBilled` | 3 / 2 / 4 | 2 / 0 / 0 |
| `HcpcsCodes.OnHand` (E1390) | 14 | 1 |
| `DmeClaims.Total` | agreed | nothing kept it so |

And DME customer PHI was plaintext at rest while clinical patient PHI in the
same database was AES-GCM ciphertext, for the same kind of person.

---

## What was built

### 1. Authentication and authorization

The hard part was not adding `[Authorize]`. It was that the SPA keeps its JWT in
localStorage and sends it as a header, while DME pages are server-rendered Razor
and a browser navigation sends no header. `[Authorize]` alone would have 401'd
every page.

- A **policy scheme** (`MedocsSmartAuth`) routes each request: Authorization
  header wins, otherwise the session cookie, otherwise Bearer so API 401s keep
  their existing shape.
- The JWT is mirrored into an **HttpOnly, SameSite=Strict** cookie at verify-otp,
  rolled forward on refresh, cleared on logout. HttpOnly makes it strictly less
  exposed than the localStorage copy it shadows.
- Both schemes run the **same validation** (`Configuration/JwtBearerSetup.cs`),
  so `TokenVersion` revocation kills the cookie session too. Verified: a replayed
  pre-logout cookie is rejected.
- A 401 on a browser GET redirects to the sign-in page rather than returning a
  blank body.

### 2. Tenant isolation

- `TenantId` added to the 10 DME tables that lacked it; `DmeSeq` rekeyed on
  `(Name, TenantId)` so tenants cannot share or leak order-number sequences.
- **Row level security** extended over every DME table, with BLOCK predicates as
  well as FILTER. FILTER alone stops a cross-tenant read but still lets a write
  plant a row in another tenant.
- `DmeDb` became request-scoped, sets `SESSION_CONTEXT` on every connection, and
  **throws** when the tenant is unknown. That throw matters: the RLS predicate
  treats "no context" as "show everything" so background jobs work, which means
  an unscoped connection would silently return every tenant's rows rather than
  failing.

Verified end to end through HTTP: moving one customer to tenant 2 removed them
from the list and made `/Dme/Customer/2` return 404 for the tenant-1 session.

### 3. Audit

`[PhiAccessAudit]` on both controllers logs every successful read. `DmeAudit`
logs every mutation with before/after, user and IP:

```
admin@md.com | {"Status":"ready"} -> {"Status":"submitted","ClaimNumber":"CLM-02006"} | ::1
```

A no-op (re-submitting an already-submitted claim) writes **no** row. An audit
trail that records events that did not happen is worse than none.

### 4. Single source of truth

For each stored number, the question was what fact genuinely exists nowhere else:

- **MonthsBilled** could not be derived even in principle: nothing recorded which
  rental a claim billed. `DmeClaimLines.RentalId` stores that link; the count is
  now a `COUNT`.
- **OnHand** could not be derived either: consumables have no serialised unit
  rows (120 test strips are not 120 rows). `DmeStockMovements` is the ledger; the
  balance is a `SUM`. Delivery writes a movement.
- **Total** and the copied names were already derivable. Views do it.

The columns were **dropped**, not deprecated. Leaving them means someone writes
to them again next month.

Deliberately kept, and documented as kept: `DmeClaims.CustomerName` /
`PayerName`, order and claim line prices, `DmeRentals.MonthlyRate`. A submitted
claim is a document as filed; deriving those would rewrite history.

### 5. PHI at rest

DME customer PHI is now AES-GCM encrypted using the **shared** `EncryptionHelper`,
so the DME product never grows its own cryptography. Search survives via a blind
index (prefix HMACs in `DmeCustomerSearchTokens`), the same approach
`BlindIndexService` already uses for patients.

`POST /Dme/BackfillPhi` (SuperAdmin/ClinicAdmin only) encrypts existing rows. It
**skips** rows that are already ciphertext rather than decrypting and
re-encrypting, because `Decrypt` returns its input unchanged on failure, so a
decrypt-then-encrypt loop would silently double-encrypt anything it could not
read and leave a plausible-looking string behind.

---

## The bug that only browser testing caught

Combining points 4 and 5 created a defect that no unit test and no status-code
check would have found.

`vDmeOrders` derived `CustomerName` as `cu.FirstName + ' ' + cu.LastName`. Once
those columns were encrypted, the view concatenated **two ciphertexts**, which
cannot be decrypted as one value no matter what key you hold. The Orders,
Rentals, Dashboard and Schedule screens rendered base64 where the customer name
belonged.

Nothing threw. Every page still returned 200.

Fixed by having the views return `CustomerFirstName` and `CustomerLastName`
separately and composing the name in the application after decryption
(`DmeCustomerPhi.ComposeCustomerNames`). Four regression tests now cover it.

**This is the trap for whoever works here next:** always compose after reading
`vDmeOrders` or `vDmeRentals`.

---

## Verification

- **Build:** succeeds. New warnings are all `CS8632` (nullable annotation without
  an enabled context), the same class as the 816 already in the project.
- **Tests:** `297 passing, 1 skipped, 0 failing`. The test project had **never
  compiled** in this fork (12 errors) so the suite had been red since day one.
- **Mutations:** three guards were deliberately broken and each was caught by
  exactly the right test — removing `[Authorize]`, softening the tenant throw to
  a default, and reintroducing a stored `MonthsBilled` counter.
- **End to end:** `bash scripts/verify-dme-foundation.sh` — **39 checks, 0
  failures** against a running app.
- **Fresh rebuild:** a scratch database built from the four migration files alone
  reproduces the live database exactly (same stock, same derived values, same
  RLS coverage). Before this, `HcpcsCodes` had no `CREATE` in the repository at
  all, so a fresh checkout could not build.

Demo data was restored to its original state; test artifacts were removed.

---

## Decisions taken, for review

1. **`HcpcsCodes` is tenant data.** Each supplier keeps its own item master and
   contract pricing. The alternative (a shared national catalog plus a per-tenant
   pricing table) is a bigger product change and can still be done later.
2. **`Dob` IS encrypted**, as of the follow-up pass. The column was converted
   from `DATE` to `NVARCHAR` holding ISO 8601 first. This is stricter than the
   clinical side, which still stores `DateOfBirth` in the clear; that gap is in
   IMEHR code and is reported rather than reached into.
3. **`AccountNo` is not encrypted.** We generate it, it is not derived from the
   person, and staff search on it.
4. **`FindAsync` baseline raised 139 -> 140.** Not new work: this fork predates
   IMEHR's removal of that call site. Re-baselined and reported rather than
   hand-patched, because the fix belongs in whichever path is chosen below. The
   ratchet still fails on any NEW call site.
5. **Blind index means prefix search, not substring search.** "mar" finds
   Margaret; "argaret" does not. Inherent to blind indexing.
6. **No role matrix beyond authenticated.** Every DME action requires a logged-in
   user of the tenant; only `BackfillPhi` is admin-restricted. A finer matrix
   (who may bill, who may deliver) is a product decision, not mine.

---

## Found and NOT built — your call

1. **The fork is far behind IMEHR.** DME is 1 commit off an older IMEHR; IMEHR is
   at 231 commits and has since added `Services/Security/` (~1,700 lines: ~30
   entity access guards, CSRF middleware, denial rate limiting, GUID public IDs
   instead of the raw ints DME still puts in URLs). Measured: **125 of 307
   shared .cs files differ**, so this is a rewrite, not a merge.

   Recommendation: do NOT rebase. Those guards protect clinical entities the DME
   product does not expose; DME already has CSRF on every POST and RLS blocking
   cross-tenant access. The real question is whether the DME product should
   carry the clinical codebase at all. Deleting it would remove more risk than
   porting guards adds.

2. **IMEHR has the same client-side-only login gate** on its own `HomeController`.
   Not touched, per the standing constraint, but the live EHR is worth a look:
   the identical hole was open here until this work.

3. **Four quarantined test files** stay excluded until a decision on item 1.

---

## Next

The client's eight feedback points are untouched, as instructed. The two that
need new schema (dashboard paid/denied/top-denial-code, and POD file
attachments) now have a foundation to sit on: `DmeClaims` has no payment or
denial columns yet, and there is no attachment table anywhere in the DME schema.

---

## Follow-up pass (same day)

Everything above was committed as `bca8cf4`. The items below closed the open
list and swept for what else was missing, and are `94c43a1` plus the hardening
commit.

### Closed from the open list

- **Empty orders.** `CreateOrder` now rejects an order with no lines and shows
  the reason on the form. The client hit this: `ORD-01013` has no lines and a
  zero total. Verified both ways: an empty submission creates nothing, a valid
  one still saves.
- **`Dob` encrypted.** Column converted with an explicit `CONVERT(..., 23)`, not
  an implicit cast: an implicit `DATE`-to-string conversion uses the session's
  date format, so the same migration on a differently-localised machine would
  store `12/04/1958` and every age on every screen would be wrong for half the
  customers.
- **`FindAsync` baseline.** Held at 140, with the reasoning recorded in the test.
  The two extra call sites are `_context.Patients.FindAsync`, and the class
  comment says `FindAsync` is dangerous because it bypasses EF global query
  filters. In this fork `Patient` has **no** query filter, so rewriting them
  would respect a filter that does not exist. What actually scopes Patients here
  is row level security, which covers `FindAsync` like any other query.

### Found while checking, and fixed

- **A second ciphertext leak.** The New Order customer picker read
  `FirstName`/`LastName` straight from `DmeCustomers` without decrypting, so the
  dropdown listed base64. Same class as the first, same silence: HTTP 200,
  nothing thrown. `DmePhiRenderingTests` is now a ratchet that source-scans every
  action reading encrypted customer data and names the offender.
- **Admin and clinical pages were still anonymous.** `HomeController` had 23 view
  actions and no `[Authorize]` at all, **including the Super Admin clinic console
  at `/Home/Tenants`**. The API behind those pages was role-checked but the pages
  themselves rendered to anyone. Now: anonymous redirects to sign-in, ClinicAdmin
  gets 403 on `/Home/Tenants`, Super Admin gets in. Kiosk and Telehealth join
  pages stay anonymous by design, gated by a one-time token in the URL.
- **Staff passwords had no policy.** The patient portal validated strength; staff
  accounts did not, at any of the four places a password is set. A clinic
  administrator, or the Super Admin who creates tenants, could be given the
  password `a`. `Helpers/PasswordPolicy.cs` now applies everywhere, with a
  12-character minimum and no composition rules (NIST SP 800-63B advises against
  those; length is the control that helps). Maximum is 64, deliberately under
  BCrypt's 72-byte input limit so no part of a typed password is silently
  discarded.
- **Exception detail was returned to callers.** Six endpoints, four on the Super
  Admin console, returned the exception message **and the full stack trace** in
  the response body, in every environment. A stack trace maps internal
  namespaces, file paths and often the failing SQL; a `SqlException` message can
  carry column values, which on these tables means PHI. `Helpers/ApiError.cs`
  logs the detail server-side and returns a correlation id, with full detail only
  in Development. Applied across 22 call sites in 11 controllers, plus a
  catch-all middleware for anything nobody caught.

### New tenant onboarding

Making the catalog and payers tenant-scoped is correct, but left a new tenant
with an empty catalog and therefore unable to raise an order.
`Migrations/Manual/DME_Onboard_New_Tenant.sql` copies a catalog and payer list
from a nominated source tenant.

Deliberately a script and not a button: every DME connection is pinned to the
caller's tenant and RLS BLOCKs cross-tenant writes, so an in-app version would
have to punch a hole through the isolation this work exists to provide. It was
written as an endpoint first and thrown away for that reason. It copies rather
than invents because prices are commercial terms and made-up rates would end up
on real claims.

### Confirmed already sound

- **Audit logs are tamper-protected.** `TR_AuditLogs_Immutable` is present and
  enabled; deletes need `SESSION_CONTEXT('AllowAuditDelete')`. Retention is 6
  years (`HIPAA:AuditRetentionDays`, 2190).
- **Super Admin exists** (`contact@medocs.ai`, role 0) and tenant creation is
  role-0 only. Verified: anonymous 401, ClinicAdmin 403.
- **Remaining controllers without class-level `[Authorize]`** all have per-action
  authorization or are intentionally anonymous (patient portal has its own auth
  scheme, Stripe webhook is signature-verified, kiosk and telehealth are
  token-gated).
