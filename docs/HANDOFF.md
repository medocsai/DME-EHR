# MEDOCS DME — Handoff

**Last updated:** 2026-08-25
**Branch:** `foundation/security-and-ssot` (7 commits ahead of `DME-EHR`)
**Status:** foundation complete and verified. No client feature work started.

Read this first. It is the current state of the product and the open decisions.
`CLAUDE.md` holds the day-to-day operating detail (how to run, where things live,
the traps). This document holds the story and the decisions.

---

## 1. What MEDOCS DME is

A **standalone** DME/HME web application: customers, inventory, orders and
delivery, rentals, billing. Sold to DME suppliers, multi-tenant, resellable as
SaaS.

It is **not** an EHR and no longer contains one. That matters, because it was
built by copying the IMEHR EHR wholesale and adding DME tables beside it, and
until 2026-08-25 both halves were still in the codebase and the database.

**IMEHR and RehabDox are reference-only.** Read them to compare; never modify
them. The constraint is in `CLAUDE.md` and it is absolute.

---

## 2. Where it stands

| | Before this work | Now |
|---|---|---|
| Database tables | 100 | **22** |
| Controllers | 68 | **7** |
| Services | 74 | **9** |
| EF entities | 88 | **6** |
| DTO types | 289 | **26** |
| Enums | 48 | **3** |
| Migration files | 46 | **6** |
| Tests | 304 red-then-green | **111 passing** |

Roughly 175,000 lines removed. The product does the same thing it did before.

**Build:** clean. **Tests:** 111 passing, 0 failing, 0 skipped.
**End to end:** `bash ehr-system/scripts/verify-dme-foundation.sh` → 39 checks,
0 failures against a running app.

---

## 3. What was wrong, and what was done

### 3.1 Anyone could read and write customer PHI

Proven by running it, not by reading it:

```
GET  /Dme/Customers  -> 200  names, phone numbers, account numbers
GET  /Dme/Customer/1 -> 200  home address, DOB, SSN last 4
POST /Dme/Submit     -> 302  claim CLM-02006 moved ready -> submitted
```

No token, no cookie, and no audit row for any of it. The login gate was a
client-side JavaScript check that hid a `<div>`.

**Fixed.** The hard part was not `[Authorize]`. The SPA keeps its JWT in
localStorage and sends it as a header, but a browser navigating to a Razor page
sends no header, so `[Authorize]` alone would have 401'd every page.

- A **policy scheme** (`MedocsSmartAuth`) picks per request: Authorization header
  wins, else the session cookie, else Bearer so API 401s keep their shape.
- The JWT is mirrored into an **HttpOnly, SameSite=Strict** cookie at verify-otp,
  rolled forward on refresh, cleared on logout. HttpOnly makes it strictly less
  exposed than the localStorage copy it shadows.
- Both schemes run the **same validation** (`Configuration/JwtBearerSetup.cs`),
  so `TokenVersion` revocation kills the cookie session too. Verified: a replayed
  pre-logout cookie is rejected.
- A 401 on a browser GET redirects to sign-in instead of a blank body.

`HomeController` had 23 view actions and no `[Authorize]` at all, **including the
Super Admin clinic console**. Now class-level `[Authorize]`, with
`[AllowAnonymous]` on only sign-in, emailed password reset and error, and
`Roles = "0"` on `/Home/Tenants`.

### 3.2 No tenant isolation on any DME table

`TenantId` added to the 10 DME tables that lacked it. `DmeSeq` rekeyed on
`(Name, TenantId)` so tenants cannot share order-number sequences.

**Row level security** now covers every DME table with FILTER *and* BLOCK
predicates. FILTER alone stops a cross-tenant read but still lets a write plant a
row in another tenant.

`DmeDb` is request-scoped, sets `SESSION_CONTEXT` on every connection, and
**throws** when the tenant is unknown. That throw is load-bearing: the RLS
predicate treats "no context" as "show everything" so background jobs work, which
means an unscoped connection would silently return every tenant's rows.

Verified through HTTP: moving one customer to tenant 2 removed them from the list
and made `/Dme/Customer/2` return 404 for the tenant-1 session.

### 3.3 Audit that proved nothing

`[PhiAccessAudit]` logs every successful read. `Services/DmeAudit.cs` logs every
mutation with before/after, user and IP:

```
admin@md.com | {"Status":"ready"} -> {"Status":"submitted","ClaimNumber":"CLM-02006"} | ::1
```

A no-op writes **no** row. An audit trail that records events which did not
happen is worse than none.

`AuditLogs` carries `TR_AuditLogs_Immutable`: deletes need an explicit session
flag, and only the retention sweep sets it. Retention is 6 years.

### 3.4 Stored values that had already drifted

| Value | Stored said | Truth was |
|---|---|---|
| `DmeRentals.MonthsBilled` | 3 / 2 / 4 | 2 / 0 / 0 |
| `HcpcsCodes.OnHand` (E1390) | 14 | 1 |
| `DmeClaims.Total` | agreed | nothing kept it so |

For each, the question was what fact exists nowhere else:

- **MonthsBilled** could not be derived *even in principle* — nothing recorded
  which rental a claim billed. `DmeClaimLines.RentalId` stores that link; the
  count is now a `COUNT`.
- **OnHand** could not be derived either — consumables have no serialised unit
  rows (120 test strips are not 120 rows). `DmeStockMovements` is the ledger; the
  balance is a `SUM`. Delivery writes a movement.
- **Total** and the copied names were already derivable. Views do it.

The columns were **dropped**, not deprecated. Read through `vDmeRentals`,
`vDmeClaims`, `vDmeOrders`, `vHcpcsCatalog` — never the base tables.

**Deliberately still stored, and documented as such:** `DmeClaims.CustomerName`
and `PayerName`, order and claim line prices, `DmeRentals.MonthlyRate`. A
submitted claim is a document as filed; deriving those would rewrite history.

### 3.5 PHI in plaintext

Clinical patient names were AES-GCM ciphertext while DME customer names sat in
the clear, for the same kind of person.

DME customer PHI is now encrypted with the **shared** `EncryptionHelper`, so the
DME product never grows its own cryptography. Search survives via a blind index
(prefix HMACs in `DmeCustomerSearchTokens`). `Dob` is included: the column was
converted from `DATE` to `NVARCHAR` holding ISO 8601 first.

`POST /Dme/BackfillPhi` (admin only) encrypts existing rows and **skips** rows
already ciphertext. That skip matters: `Decrypt` returns its input unchanged on
failure, so a decrypt-then-encrypt loop would silently double-encrypt anything it
could not read and leave a plausible-looking string behind.

### 3.6 Secrets and session

The JWT signing key had a hardcoded fallback in two places:

```csharp
config["Jwt:Key"] ?? "YourSecretKeyHere12345678901234567890"
```

A deployment with a missing setting would not fail. It would start, sign tokens
with a string printed in this repository, and look healthy — and anyone who had
read the source could mint a Super Admin token for any tenant.

Gone. `Configuration/SecretsGuard.cs` refuses startup outside Development on a
missing, placeholder or too-short key.

`HIPAA:SessionTimeoutMinutes` had been sitting in config declaring 15 minutes and
was read by nothing. It now drives the token lifetime and therefore automatic
logoff. Verified: the cookie expires exactly 15 minutes after login.

Staff passwords had **no policy at all** — a Super Admin password could be `a`.
`Helpers/PasswordPolicy.cs`: 12 minimum, no composition rules (NIST 800-63B
advises against them), max 64 to stay under BCrypt's 72-byte truncation.

Six endpoints returned **full stack traces** to callers, four on the Super Admin
console. `Helpers/ApiError.cs` logs the detail and returns a correlation id.

### 3.7 Two products in one database

Of 100 tables, DME used 23. The other 77 were the copied EHR: 634 patient
records, 1,745 clinical claims, ~60 controllers that could render them.

Not one row crossed between the halves, so nothing was drifting. It was dead
weight — but dead weight a HIPAA product was carrying.

All of it removed: code first (verified working without it), then the tables,
then the EF model, then the migration files, partials, stylesheets, scripts,
enums, config sections and NuGet packages.

---

## 4. Traps. Read these before writing code

**1. Composing customer names.** `vDmeOrders` and `vDmeRentals` return
`CustomerFirstName` and `CustomerLastName` **separately**, encrypted. Two
ciphertexts concatenated in SQL can never be decrypted, by anyone, with any key.
Always call `_phi.ComposeCustomerNames(rows)` after reading those views.

This shipped twice and neither time did anything throw — the pages returned 200
and rendered base64. `DmePhiRenderingTests` is now a ratchet that names the
offending action.

**2. A column drop and its EF property ship together.** Dropping
`Users.ProviderId` while the entity still declared it made EF emit
`SELECT ... ProviderId` on every login. Build clean, app started, and every
request 302'd because authentication itself was throwing. The verify script went
39/39 → 17/39 and named it.

**3. `ALTER SECURITY POLICY` and dropped columns are validated at COMPILE time.**
An `IF NOT EXISTS` guard does not protect them. Use `sp_executesql`.

**4. Filtered indexes need `SET QUOTED_IDENTIFIER ON`.** Any ad-hoc SQL touching
`DmeClaimLines` fails without it.

**5. A clean build proves nothing.** Both of the worst regressions here had clean
builds and passing unit tests. Load the pages.

---

## 5. How to verify anything

```bash
cd ehr-system && dotnet run --urls http://localhost:5077
bash scripts/verify-dme-foundation.sh
```

39 checks: anonymous access refused, cookie flags, authenticated pages, PHI
ciphertext at rest and readable in the app, blind-index search, tenant isolation
both directions, derived values, audit trail. It restores the one claim it
changes.

Fresh database from the repo:

```
1. 2026-06-19_DME_Core_Schema.sql
2. 2026-08-25_DME_Tenant_Isolation.sql
3. 2026-08-25_DME_Single_Source_Of_Truth.sql
4. 2026-08-25_DME_Customer_PHI_Encryption.sql
5. 2026-08-25_Drop_Clinical_Schema.sql   (only on a database forked from IMEHR)
```

Then `POST /Dme/BackfillPhi` once as an admin. Verified end to end on a scratch
database.

New tenant: create it at `/Home/Tenants`, then run
`Migrations/Manual/DME_Onboard_New_Tenant.sql` by hand. It is a script and not a
button on purpose — every DME connection is pinned to the caller's tenant and RLS
blocks cross-tenant writes, so an in-app version would have to punch a hole
through the isolation. It was written as an endpoint first and thrown away.

---

## 6. Open decisions — yours, not mine

**1. Rotate the three committed secrets.** `appsettings.json` is tracked and
holds the JWT signing key, the PHI encryption key and the SMTP password. They are
in git history and must be treated as compromised. This blocks **go-live, not
development**. Deliberately not automated: rotating the encryption key means
re-encrypting every encrypted row, rotating the JWT key logs everyone out. Env
vars `Jwt__Key` and `Encryption__Key` already override without a code change.

**2. Billing: how payments arrive.** Discussed, not started. See section 7.

**3. CSP is `Content-Security-Policy-Report-Only`.** Switching it to enforcing
needs a pass through the app watching for violations — browser work, not a code
change.

---

## 7. The next piece of work: payments and denials

The client (Dr. Roland Okwen, rottamllc) asked for three numbers on the
dashboard, monthly: **amount paid**, **amount denied**, **most frequent denial
code**.

### What we established

There is **no payment data in the system at all**. `DmeClaims` has
`ClaimNumber, OrderId, CustomerId, CustomerName, PayerName, Status, ServiceDate`
and nothing else. The claim lifecycle **ends at `submitted`**. Nothing ever comes
back. So this is not a dashboard task; it is building the money-coming-back half
of the billing cycle, and the tiles are the readout.

### The manager and Hammas were each right about a different half

**Payments work the same as RehabDox.** An 835 says "against claim X line Y:
allowed this, paid this, adjusted this, patient owes this". It does not care
whether the line carried a CPT or a HCPCS code.

**The claim content differs.** CMS-1500 box 24D is HCPCS not CPT; box 17
referring provider is the *ordering physician* and is mandatory for DMEPOS; box
24J rendering provider is usually absent because the supplier bills as box 33;
and modifiers (RR rental, NU new, KX) are load-bearing rather than occasional.

So: **reuse the plumbing, rewrite the assembler.**

### What exists to reuse (read-only reference)

RehabDox is **live and proven** with PTP (Office Ally account `ptpinc1`):
837P submission, 999/277CA acknowledgement ingestion, 835 ERA line posting with
reversal. IMEHR has a port of the same work that nobody uses — **RehabDox is the
reference, not IMEHR.**

`rehabdox-webapp/ehr-system/Services/OfficeAlly/` — 12 files. Roughly half is
pure X12 with no table dependency (`Edi837PGenerator`, `EdiResponseParser`,
`X12CodeTranslator`, `X12ControlNumberSequence`, `X12NameAndAddressSplitter`) and
lifts across nearly untouched. The table-coupled half
(`ClaimSubmissionService`, `EraLinePostingService`, `Era835LineMatcher`,
`ResponseIngestionService`, `ClaimValidator`, `Edi837ClaimAssembler`) is the part
that has to be rewritten for DME anyway.

Their money model: money lives in `Charges` line cells (`ChargeAmount`,
`AllowedAmount`, `PaidAmount`, `AdjustmentAmount`, `PatientResponsibility`) plus
`Payments`. `ClaimLinePostings` records the posting event **and the prior
values**, so a posting can be reversed. Read the reasoning in that file's header
before designing ours; it is good.

Also read, in `rehabdox-webapp/`: `HANDOFF-OFFICEALLY-CLAIMS.md`,
`OFFICEALLY-SUBMISSION-DECISIONS.md`, `OFFICEALLY-PORT-TO-IMEHR.md`.

### The catch on the client's actual ask

**Neither codebase stores the denial reason code.** RehabDox's ERA posting
captures CAS *group* totals (CO write-off, PR patient responsibility) but not the
individual CARC code per line. `ReasonCode` exists only on the 277CA *rejection*
path, which is a different thing from an adjudicated denial.

So "amount paid" comes with the port. **"Most frequent denial code" is new work
either way**: parse and store CAS reason codes per line. Not large, but not
there.

### Decided already

The billing model question is **closed by the clinical removal**:
`BillingClaims`, `Charges` and `Payments` are gone. DME payments get built on
`DmeClaims` / `DmeClaimLines`. There is no second option left.

### Still to close, in this order

1. **How money enters.** Manual payment posting, 835/ERA file, or both.
   Recommendation: manual posting first — it is the real workflow for a supplier
   this size, it is testable, and 835 parsing is a large separate build the demo
   already labels as simulated. Hammas raised manual payments independently and
   he is right: a supplier takes money from the payer *and* from the customer
   (copay, deductible, cash sale, non-covered rental). Both must land in one
   model or the balance is wrong.
2. **What "amount denied" means.** Billed amount of denied claims, or the portion
   not paid? Billers use both and they are very different numbers.
3. **"Monthly" by which date.** Posting date (cash received) or service date
   (revenue earned).
4. **How much of the money model.** Paid and denied only, or also allowed /
   adjustment / patient responsibility. Leaning to including them, because
   "denied" is meaningless without separating a denial from a contractual
   write-off.

### Other client asks, untouched

From the same email: exhaustive payer list, exhaustive ICD-10 list, exhaustive
HCPCS list (all data loading, cheap), copy-pasteable date of birth, POD file
attachments (needs schema — there is no attachment table anywhere), and
drop-shipping from manufacturers/distributors (needs a supplier concept).

---

## 8. Working agreement

`D:\Common\Work Style\` — read all four files at the start of every session.
Short version: Hammas is CTO, the session is PM, shortest possible answers
section by section, compute never store, hired-guy separation, long-term fixes
only, no over-engineering, no work outside the paid scope, never use em dashes,
one decision at a time and close it before the next.

Every file in this codebase carries a header saying what it is, why it exists,
who calls it and what the tradeoff was. Keep that up. If something stops being
used, delete it.
