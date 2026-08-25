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
- **Tests:** `275 passing, 1 skipped, 0 failing`. The test project had **never
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
2. **`Dob` is not encrypted.** It is a `DATE` column; encrypting it needs a type
   change and its own migration, and the clinical side does not encrypt
   `DateOfBirth` either. Flagged rather than quietly skipped.
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
   instead of the raw ints DME still puts in URLs). Rebase or cherry-pick is a
   real decision with real cost.
2. **IMEHR has the same client-side-only login gate** on its own `HomeController`.
   Not touched, per the standing constraint, but the live EHR is worth a look.
3. **An order can be saved with zero lines.** The client hit this: `ORD-01013` in
   their screenshots has no lines and a $0.00 total. Lines are added by JavaScript
   only and `CreateOrder` has no guard. One-line fix, not in tonight's scope.
4. **Four quarantined test files** stay excluded until a decision on item 1.

---

## Next

The client's eight feedback points are untouched, as instructed. The two that
need new schema (dashboard paid/denied/top-denial-code, and POD file
attachments) now have a foundation to sit on: `DmeClaims` has no payment or
denial columns yet, and there is no attachment table anywhere in the DME schema.
