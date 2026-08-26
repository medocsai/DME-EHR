# MEDOCS DME: Open Threads

**Purpose:** the parking lot. Everything raised and not yet closed, so a
conversation that jumps around does not lose anything.

**Rule:** one thread at a time. Close it, move it to the bottom, start the next.
Nothing here is being worked on unless it says so.

**Last updated:** 2026-08-27

---

## Thread 7: Client feedback, 2026-08-27 (OPEN, one item at a time)

Their email after seeing the platform. Every item, what it actually is and what
was found, lives in **`docs/CLIENT-REQUESTS-2026-08-27.md`**. Summary only here:

| # | Ask | Status |
|---|---|---|
| D1 | Dashboard monthly payments: paid, denied, top denial code | ALREADY BUILT, they have not seen it. Closes on a deploy |
| C1 | Copy and paste into the customer form, e.g. DOB | DONE 2026-08-27 |
| C2 | Insurance list is not exhaustive | DONE 2026-08-27. 4 payers became 4,017, global catalog, typeahead |
| C3 | ICD-10 codes for DME not exhaustive | DONE 2026-08-27. 12 codes became CMS's 74,719 |
| O1 / H1 | HCPCS item list not exhaustive (raised on two screens, one job) | DONE 2026-08-27. CMS's 8,623, plus an Add item form |
| O2 | Proof of delivery: attach PDFs and pictures | Parked by Hammas. Storage decided: new Google bucket, RehabDox pattern. Approach written up |
| I1 | Most delivered items are drop-shipped from distributors | Not started. Needs a conversation, not a spec |

**Five of the seven are closed.** The three "not exhaustive" complaints were
decided together and all three lists are now global national reference data,
never copied per tenant. **Two remain: O2, parked by Hammas, and I1, which needs
a conversation with the client before it can be specced.**

**Nothing is deployed**, so the client is still looking at the old build. D1 in
particular closes on a deploy, not on code.

**Raised by C2, not part of it:**

- **CMS-1500 box 1 is not implemented.** Medicare / Medicaid / Tricare / group
  health. Found while deciding what to do with the unused `PayerType` column.
  Reported, not built.
- **Frank Marsh (LMS-1005) was deleted and restored on 2026-08-27** by a bug in
  a verification section I wrote. His `LocationId` is a reconstruction (1, the
  primary branch), not the original value. Everything else came from the seed
  script. Worth knowing if that customer ever looks wrong.

---

## Thread 1: Super admin (CLOSED 2026-08-26)

**Asked:** is super admin complete? Can I create tenants and manage SFTP?

**Answer: yes, now.** Both gaps found on inspection are closed, tested and
verified end to end. Details below; the login is still the thing you need.

**Login (local only, set 2026-08-26):**

| | |
|---|---|
| Email | `contact@medocs.ai` |
| Password | `MedocsSuper@2026` |
| OTP | `123456` (Development only) |

The password was unknown, so it was set directly against the local `DMEEHR`
database. **This is the local machine only.** It does nothing to any deployed
environment, where the password is still whatever it was and would have to be
reset there separately.

**Verified working:**

- Sign in, OTP, lands on the clinic console.
- `/Home/Tenants` lists every clinic. Add Clinic, view, edit, suspend, delete.
- The Add Clinic form creates the clinic, its first location and its admin user.
- `/Dme/Settings?tenantId=N` opens a supplier's DME settings.
- Saving a clearinghouse account as super admin works, and a clinic admin gets
  403 and does not even see the form.
- A super admin landing on a DME page with no clinic chosen now gets a redirect
  to the console instead of a 500.

**Both gaps closed:**

1. **The clinic switcher now drives the DME screens.** It writes a
   `medocs_clinic` cookie that `TenantResolutionMiddleware` reads as its last
   source. Previously it wrote only localStorage and raised a JavaScript event,
   which the SPA modules see and the server-rendered DME pages do not, so
   switching clinic did nothing to them.

   The cookie is safe because every resolution step is guarded on the tenant
   still being unknown and step 1 fills it from the token, so it is only ever
   reached by an identity with no TenantId claim. A clinic admin forging it
   changes nothing. That guard was mutation tested: removing it breaks two tests.

   `?tenantId=` still beats the cookie, because a link naming a tenant is an
   instruction for this request while a cookie is a preference from earlier.

2. **Creating a clinic enforces the password policy.** `TenantService` validates
   before writing anything. The refusal has to come first because the tenant,
   its location and its admin are written across two SaveChanges calls, and
   failing partway would leave a clinic with no administrator.

   Writing the test found three MORE password fields in the same admin modals
   saying "minimum 8" against a policy of 12. All of them now say 12, and a test
   reads the number out of `PasswordPolicy.MinimumLength` so they cannot drift
   apart again.

**Still true, and yours to do:** the deployed environment has a different super
admin password and has not been touched. Only this machine was changed.

**Also changed locally for testing:** `lisa.henderson@demo.clinic` (Front Desk)
now has the same password and is granted ONLY the Main Clinic branch, so the
verification script has a genuinely restricted user to sign in as. Local
database only.

---

## Thread 2: Locations in DME (CLOSED 2026-08-26, built)

**Asked:** does DME need Locations, and does `LocationId` go on customers only?

**Answer: yes, and yes, with one exception.** Built, tested and verified end to
end. Operating detail in `CLAUDE.md`. The analysis that decided it is below,
kept because it is the argument, not just the conclusion.

The evidence is in the RehabDox database, and it settled the second question too:

- `Appointments.LocationId` is NULL on **164 of 165 rows**.
- `Patients` has no `LocationId` at all, only `PreferredLocationId`.
- So the filter, repeated in eight or more places, is a fallback chain:
  `a.LocationId == locId || (!a.LocationId.HasValue && (a.Patient.PreferredLocationId == locId || a.Patient.PreferredLocationId == null))`
- Where they derive from the patient instead (`CareEpisodeServices`), there is no
  fallback and no bug.

The copied column drifted to NULL. Deriving did not.

**What was built, and what it cost:**

- `LocationId` on `DmeCustomers`, `DmeStockMovements` and `DmeSerializedUnits`.
  All three NOT NULL. Nothing else has one, and a test fails if anyone adds one.
- Every DME screen filters by branch, with `(@LocationId IS NULL OR ...)` so the
  all-branches roll-up works. A test fails on any filter missing that half,
  because a bare equality silently returns an empty product.
- The header switcher gained "All locations", and `/api/locations/switch` now
  re-issues the SESSION COOKIE as well as the token. Without that half only the
  SPA followed the switch and every server-rendered DME screen ignored it.
- Inventory shows company stock and branch stock side by side.

Four things turned up on the way that were not in the plan:

1. **`Locations` had two rows flagged primary for tenant 1.** Everything defaults
   to "the tenant's primary branch", so the backfill would have been arbitrary.
   Normalised, and `UX_Locations_OnePrimaryPerTenant` stops it recurring.
2. **The location switcher modal did not exist.** It was lost in the 2026-08-25
   modal cleanup, so clicking the location in the sidebar had silently done
   nothing since then. Put back in `_Layout.cshtml`.
3. **The header could disagree with the server.** The JS treated a falsy
   LocationId as "none set" and snapped to the primary branch, so after choosing
   all locations the sidebar said "Main Office" while the server returned every
   branch. The active branch now comes from the token, which is what the server
   actually filters by.
4. **An index blocks `ALTER COLUMN`.** Creating the index before tightening to
   NOT NULL fails with "the index is dependent on column LocationId". Indexes
   last.

---

## Thread 2b: Per-user branch grants (CLOSED 2026-08-26, built)

**Asked:** do UserLocations as well, same as RehabDox.

Built. `dbo.UserLocations`, the same shape RehabDox uses, plus the rule their own
`LocationScopeService` is written around: **an empty grant set sees nothing.**

- Roles 0 and 1 bypass branch scoping, roles 2 and up are restricted to their
  granted branches. Pinned by a test so changing it has to be deliberate.
- The User Management screen has a branch checklist, hidden for Clinic Admin who
  is not scoped by it, and the user list shows each person's branches.
- The migration backfilled every existing active user with every active branch of
  their tenant. There were 27 restricted users; creating the table empty would
  have blanked all of them, which is an outage rather than a security gain.

Three things surfaced while building it:

1. **Login handed restricted users a branch they did not hold.** It picked the
   tenant's primary, so a front-desk user granted only one depot signed in to a
   product that was completely empty with nothing explaining why. The default now
   comes from the same grants the screens filter by.
2. **The branch list needed its own predicate.** Applying the "which branch is
   chosen" half to a list OF branches reduces the switcher to the branch already
   selected, so `LocationGrants()` exists alongside `LocationScope()`.
3. **Qualifying the column with a string replace was wrong.** `LocationScope`
   contains `@LocationId`, so a blind replace rewrote the parameter name too and
   produced SQL that parsed and matched nothing. It takes a column argument now.

**Still not built:** nothing asked for here is outstanding.

---

## Thread 6: User Management screen never finished loading (CLOSED 2026-08-26)

`/UserManagement` renders server side in 6ms, and every API it calls answers in
under 150ms, but the page never reaches idle in a browser.

**Cause, confirmed by reverting this session's changes and reproducing it:**
`UsersModule.js` bootstraps with

```js
const initWhenReady = () => {
    if (!isAuthenticated) { setTimeout(initWhenReady, 200); return; }
    ...
};
```

an unbounded 200ms poll with no give-up. When the SPA auth state does not
resolve, it spins forever and the document never becomes idle. Server-rendered
DME pages are unaffected because they authenticate off the session cookie.

**Fixed 2026-08-26.** The poll is now bounded at 10 seconds and, when it gives
up, says so in the table instead of leaving the user on a screen that is loading
and always will be. A retry with no end is not a retry, it is a hang with a
timer.

A second contributing factor was fixed with it: `/api/providers` no longer exists in DME (it
went with the clinical schema on 2026-08-25) and `UsersModule` still calls it in
three places; inside a `Promise.all` its 404 rejected the whole load. Those calls
now tolerate failure.

**Still not clicked by hand.** The Chrome extension went offline before the fix
could be exercised in a browser, so the screen is verified by markup, by the API,
and by 205 unit tests plus section 12 of the verification script, which signs a
genuinely restricted user in over HTTP. Worth one manual look next session.

**Also worth removing while in there:** the Provider Association block on the
user form is a clinical leftover with no backing endpoint.

---

**The analysis that decided it, kept for the reasoning:**

- `LocationId` on `DmeCustomers` only. Orders, rentals, claims, claim lines,
  CMNs, insurances and diagnoses all derive by join.
- **One exception: inventory.** Stock is physical. `DmeStockMovements` and
  `DmeSerializedUnits` need their own `LocationId`, because a concentrator in the
  Dallas depot is not in the Houston depot and no customer owns it. That is a
  different fact, not a duplicate.
- **Location never goes into row level security.** Tenant is enforced by the
  database and must stay that way. Location stays an optional application
  filter, or the owner's all-locations roll-up cannot be done without punching a
  hole in the isolation.
- **A `UserLocations` join table** decides who sees which branch. DMEEHR has no
  such table; PTEHR has 94 rows.

**No longer blocks Thread 3.**

---

## Thread 3: SFTP and supplier identity per location (READY, unblocked)

Each location can hold its own NPI and PTAN, so it can have its own
clearinghouse account. Correct model, which is what RehabDox already does:

- The account row stays **tenant owned**. Several branches usually share one
  login, and two branches sharing an account must not mean two copies of the
  same password.
- Each **location points at** an account, the way
  `Locations.OfficeAllySftpAccountId` does.
- `DmeSupplierProfile` becomes one row per billing entity rather than one per
  tenant, and a location points at that too.

Thread 2 has landed, so the locations now exist to point at. Ready when you
want it, and worth doing only once a supplier actually has a second accredited
DMEPOS location: today there is one billing entity per tenant and the model
already fits it.

---

## Thread 4: 837 transmission (BLOCKED on the client)

Needs a live clearinghouse account. Everything it depends on is built: supplier
identity, credential storage, the settings screen. `/Dme/Submit` says "Mark as
submitted" because that is all it does.

835/ERA ingestion follows the same way, and writes the same tables the manual
posting screen already writes.

---

## Thread 5: Ops decisions that block go-live, not development

1. **Rotate the three committed secrets.** `appsettings.json` is tracked and
   holds the JWT signing key, the PHI encryption key and the SMTP password.
2. **CSP is report-only.** Switching it to enforcing needs a pass through the app
   watching for violations.
3. ~~**`Locations` is dead furniture.**~~ Closed by thread 2: it is now the
   spine of branch separation across the whole product.

---

## Closed

- **Billing decisions 1 to 5.** See `docs/BILLING-DECISIONS.md`. Payments,
  denials, the three dashboard tiles, supplier identity, credential storage.
- **SFTP is super admin only, not clinic admin.** Decided 2026-08-26. The
  credential is issued during an onboarding Medocs runs; a clinic admin has no
  occasion to touch it. Enforced on both POST actions and pinned by a test.
- **Thread 1, super admin.** Closed 2026-08-26, see above.
- **Thread 2, locations.** Closed 2026-08-26 and built: branch separation with an
  all-branches roll-up, location stored once on the customer plus separately on
  inventory, and never made a security boundary.
