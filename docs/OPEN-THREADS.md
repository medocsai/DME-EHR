# MEDOCS DME: Open Threads

**Purpose:** the parking lot. Everything raised and not yet closed, so a
conversation that jumps around does not lose anything.

**Rule:** one thread at a time. Close it, move it to the bottom, start the next.
Nothing here is being worked on unless it says so.

**Last updated:** 2026-08-27

---

## Thread 7: Client feedback, 2026-08-27 (CLOSED, all seven built)

Their email after seeing the platform. Every item, what it actually is and what
was found, lives in **`docs/CLIENT-REQUESTS-2026-08-27.md`**. Summary only here:

| # | Ask | Status |
|---|---|---|
| D1 | Dashboard monthly payments: paid, denied, top denial code | ALREADY BUILT, they have not seen it. Closes on a deploy |
| C1 | Copy and paste into the customer form, e.g. DOB | DONE 2026-08-27 |
| C2 | Insurance list is not exhaustive | DONE 2026-08-27. 4 payers became 4,017, global catalog, typeahead |
| C3 | ICD-10 codes for DME not exhaustive | DONE 2026-08-27. 12 codes became CMS's 74,719 |
| O1 / H1 | HCPCS item list not exhaustive (raised on two screens, one job) | DONE 2026-08-27. CMS's 8,623, plus an Add item form |
| O2 | Proof of delivery: attach PDFs and pictures | DONE 2026-08-27. Encrypted, served through an audited action. Bucket still to be created |
| I1 | Most delivered items are drop-shipped from distributors | DONE 2026-08-27. They write no stock movement, and are shown separately |

**All seven are closed.** The three "not exhaustive" complaints were decided
together and all three lists are now global national reference data, never
copied per tenant. I1 turned out not to need the client after all: the answer
was to write NOTHING to the stock ledger for a drop-shipped line.

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
- **Thread 6, separation of duties.** Closed 2026-09-05. Role 3 was renamed from
  Front Desk to Delivery on 2026-08-31 and the name was the only thing that
  changed. Billing, Payments, Cms, PostPayment, CreatePayment, VoidPayment,
  Submit and BillNow carried no role list at all, so the driver could post a
  payment against the order he had just delivered, and the sidebar offered him
  the tab. All eight now carry `DmeRoles.Money` and the sidebar reads the same
  constant. `/UserManagement` was the same shape one level up: the guard was on
  UsersController and not on the page, so an Intake user who typed the URL got
  the screen and an empty table. Pinned by `DmeSeparationOfDutiesTests`.

  Two things came out of it that are worth remembering:

  1. **`.requires-admin` was never a permission.** It is CSS keyed off a body
     class the SPA sets after `/api/auth/me` returns, so the links were in every
     user's markup and merely invisible, and hiding a link says nothing about
     whether the URL opens. The DME sidebar is server rendered against the
     session cookie, which already holds the role, so it now renders the truth.
  2. **The role NAME had three sources in JavaScript** and they had drifted. The
     sidebar read `AuthModule.getRoleName`, whose own array still said
     "Clinician" and "Front Desk", so a user the server called Intake was told
     by the only label they ever see that they were a Clinician. `UserRoles` is
     the list now; both it and `AllDtos.GetRoleName` answer "Unknown" for a
     number neither recognises, rather than naming a real role.

  **Still open, deliberately:** reads are not separated. A biller can open a
  customer and an order, because that is how you check what a claim was billed
  for, and Intake and Delivery can see stock. Only money is walled off. If the
  supplier later wants Delivery kept out of customer insurance detail, that is a
  new decision, not a bug.
- **Thread 7, one customer form.** Closed 2026-09-05. New Customer and Edit
  customer were two files. New carried Basic Info, Emergency Contact, Insurance,
  Diagnosis and Attachments; Edit carried the first two, and a note saying
  insurance and diagnosis "are changed from the customer's own page". They were
  not: no action anywhere in the product changed either, so a payer entered
  wrong on the day a customer was created stayed wrong for the life of the
  record and every claim went to it. Both pages now render
  `Views/Dme/_CustomerForm.cshtml`, and `CreateCustomer` and `UpdateCustomer`
  both file through `SaveCustomerInsurance` and `SaveCustomerDiagnoses`. Pinned
  by `DmeCustomerFormTests`.

  Two decisions inside it:

  1. **A posted payer id of 0 means KEEP the payer on file.** The insurance row
     stores the payer's NAME and Payer ID, never the catalog row id, because it
     is the point-in-time record of who somebody was insured with. The id is
     recoverable from neither: Office Ally issues ALLCA to two different payers.
     So the picker cannot start on the stored payer and an untouched form posts
     0. If 0 meant "no payer", opening the edit screen to fix a phone number
     would drop the customer's insurance. Mutation tested: rewriting that line
     to treat 0 as blank fails `AnUntouchedPayerPickerKeepsThePayerOnFile`.
  2. **The diagnosis chips are rendered by the SERVER.** The posted list
     replaces what is on file, so a page whose script failed to run would
     otherwise post nothing and silently empty a customer's diagnoses. Hidden
     inputs that exist in the markup before any script runs mean the worst a
     dead script can do is leave the record as it was.

### Still open, out of this thread

- ~~**Secondary insurance has a column and no screen.**~~ Closed 2026-09-05, see
  thread 8 below.
- ~~**Customer attachments store nothing.**~~ Closed 2026-09-05, see thread 9.
- **A claim has no diagnosis snapshot.** `Cms` reads the customer's CURRENT
  diagnoses, so editing them now changes what an already submitted claim prints.
  Until 2026-09-05 this could not happen, because nothing could edit a diagnosis
  at all. The fix is to store the codes on the claim when it is raised, the same
  way `DmeClaims` already keeps its own `CustomerName` and `PayerName`. It is a
  schema change and its own decision, so it is written down here rather than
  done in passing.

- **Thread 8, secondary insurance.** Closed 2026-09-05. `Kind` has carried
  `primary | secondary` since the schema was written and the customer page has
  always displayed both, but no form ever wrote a secondary, so the only
  customer who had one was the demo seed. Medicare plus a supplement is the
  ordinary case in this trade, not an edge case. Both forms now write both, and
  `SaveCustomerInsurance` takes the kind. Migration
  `2026-09-05_DME_Secondary_Insurance.sql`.

  Three decisions:

  1. **A secondary can be ENDED; a primary cannot.** A secondary genuinely
     lapses: a spouse changes job, COBRA runs out, and "they no longer have one"
     is a fact the record has to hold. A primary does not lapse into nothing, it
     becomes a different payer, which the picker already does, and a supplier
     with no primary cannot bill at all. So the checkbox exists on one and not
     the other, and it only appears when there is a policy to end.
  2. **Ending it DELETES the row, and that is safe here and nowhere else.**
     `DmeClaims.PayerName` is copied at the moment a claim is raised, so a past
     claim keeps saying what it was billed under. Verified by ending customer
     2's secondary and reading their three claims back unchanged. If that column
     ever became a join, this delete would start silently rewriting history.
  3. **`Kind` is now enforced by the database.** It was NVARCHAR(12) with a
     comment beside it and nothing checking it, which was survivable while one
     screen wrote the table and only ever wrote `primary`. Six places read
     `WHERE Kind='primary'`; a row stored as anything else would be found by
     none of them, and the claim raised at delivery would carry a NULL payer.
     Not an error, just a claim addressed to nobody, found weeks later by the
     biller. `CK_DmeCustomerInsurances_Kind` and
     `UX_DmeCustomerInsurances_Kind` (one of each kind per customer) close both.
     The data was queried first: six rows, five primary and one secondary, no
     duplicates, so neither guard is an outage. Both were exercised by trying to
     violate them.

  **Not built, and worth knowing:** the CMS-1500 has no box 9. Box 9, 9a and 9d
  are where the OTHER insured's policy goes, and `Views/Dme/Cms.cshtml` does not
  render them at all, so a secondary is recorded and displayed but does not yet
  reach a printed claim. Coordination of benefits is its own piece of work: the
  837 needs an SBR loop per payer and the secondary is only billed after the
  primary adjudicates. Recording it correctly is the half that had to come
  first.

- **Thread 9, customer documents.** Closed 2026-09-05. The Attachments panel on
  the New Customer screen had been demo UI since the product was written: it let
  an operator pick a file, listed it, and stored nothing at all. A list of file
  names reads as "saved", which made it the worst kind of broken. The intake
  clerk attaches the referral, sees it on the screen, and the referral is gone
  the moment the page navigates. Migration
  `2026-09-05_DME_Customer_Documents.sql`, services `DmeDocumentStore` and
  `DmeCustomerDocuments`, panel on both customer screens. Pinned by
  `DmeCustomerDocumentTests`.

  Four decisions:

  1. **The FILE rules moved into `DmeDocumentStore`, shared with proof of
     delivery.** Encrypt before the bytes leave the app, hash the plaintext
     first so the document survives a key rotation, opaque object key, magic
     bytes checked against the extension, 25MB. Copying those for this feature
     would have put the magic-bytes check in two places, and the copy that gets
     forgotten is always the one that mattered. `DmeOrderDocuments` was moved
     onto it in the same pass; its seventeen tests are what made that safe, and
     one of them now pins the hash ordering for both.
  2. **A customer document has a KIND and a proof of delivery does not.** Every
     row in `DmeOrderDocuments` is the same thing. A customer's documents are
     not: a biller answering a CO-50 is looking for the CMN that establishes
     medical necessity, not for "a file". `CK_DmeCustomerDocuments_Kind` pins
     the six values, and a test proves the picker offers exactly what the
     constraint accepts.
  3. **The panel is OUTSIDE the customer form**, which is why that card is no
     longer in the two column grid with the others. The uploader is a form of
     its own, HTML forbids nested forms, and a browser handed one silently drops
     the inner one: the file would go nowhere and nothing would say why. An
     earlier draft kept the card in the grid and moved the uploader out with a
     script, which produced the same nesting in the DOM instead of the markup
     and passed a weaker version of the test. The browser reported
     `nestedInCustomerForm: true` and that is how it was caught.
  4. **New Customer says "save the customer first" rather than showing a
     picker.** A document attaches to a record and there is not one yet. Showing
     a picker that quietly discards what it is given is precisely the bug being
     fixed.

  Proved end to end on the running app: a real PDF stored (69 plaintext bytes,
  97 on disk, `%PDF` absent from the file, object key carrying no name),
  downloaded back byte-identical, an executable renamed to `.pdf` refused with
  "That file is not really a PDF", an invented kind refused, and neither refusal
  leaving a row or a file behind. Removal leaves the row with a `DeletedAt`,
  empties the bytes from disk, and turns the download into a 404.

- **Thread 10, the picker nobody could click.** Closed 2026-09-05. Reported as
  "dropdowns work with an external mouse and not with the laptop trackpad", and
  it was exactly that. `Typeahead.js` closed its results list 150ms after the
  box lost focus, to give a click time to land first. A mouse does mousedown,
  mouseup and click inside about 20ms and wins. A trackpad has to RECOGNISE a
  tap before the click is synthesised, and finger movement during the tap pushes
  it further out; past 150ms the list has been emptied and the button the click
  was aimed at no longer exists, so nothing happens and nothing explains itself.

  Reproduced in the browser by firing `blur` and clicking 250ms later: the row
  was gone from the DOM and the hidden field still read `0`. The fix is to
  prevent the default on `mousedown` over the list, so focus never moves, `blur`
  never fires while somebody is picking, and there is no race to lose. Escape
  and leaving the box still close it, immediately, with no timer.

  **Worth knowing:** this affects all three pickers, payers, ICD-10 and HCPCS,
  because they are one component. And there is still no keyboard navigation in
  it: arrow keys and Enter do nothing, so a picker that cannot be clicked cannot
  be used at all. That is a separate piece of work.

- **Thread 11, editing an order.** Closed 2026-09-05. An order could be raised
  and never corrected. A quantity typed wrong, the wrong HCPCS picked off a
  similar name, a delivery date moved: the only way out was to cancel it and
  raise another, which spends an order number and leaves a cancelled row whose
  real reason was a typo. Every cancellation then reads as a business event when
  most of them were corrections. `NewOrder` and `EditOrder` now render
  `Views/Dme/_OrderForm.cshtml`, and `CreateOrder` and `UpdateOrder` both file
  through `SaveOrderLines`. Pinned by `DmeOrderEditTests`.

  Four decisions:

  1. **Delivery is the line, not "draft".** `IsEditable` asks whether anything
     irreversible has happened yet, and the answer is no until `Deliver` runs.
     Deliver writes a claim, a rental, a stock movement and a serialised unit,
     and none of those can be un-written by editing the order they came from. So
     `draft` and `confirmed` are both editable and `delivered` is not. A
     `cancelled` order is reopened first, deliberately: reopening resets it to
     draft and says out loud that the eligibility and stock check behind
     "confirmed" has gone stale.
  2. **The status is re-read on POST, never trusted from the open screen.**
     Somebody can deliver an order while the edit form is sitting open, and the
     delivery has to win. The UPDATE carries the same guard in its WHERE, so
     even a race past the check writes nothing. Proved by delivering order 23
     behind an open form: the save was refused, the deposit did not move, and
     the operator was told why.
  3. **The lines are REPLACED, and that is only safe here.** Nothing in the
     product references an order line by id, and before delivery the lines have
     produced nothing. After delivery they have produced stock movements,
     serialised units and claim lines, which is the whole reason the window
     closes.
  4. **The rows are rendered by the SERVER**, same as the diagnosis chips. The
     POST replaces the lines with whatever it carries, so a page whose script
     failed to run would post nothing and empty the order. The remove button is
     delegated rather than wired per row, or every line the order already had
     would have a button that does nothing.

  **What the test suite caught that a browser would not have:**
  `DmePhiRenderingTests` failed the moment `EditOrder` read `vDmeOrders` without
  calling `_phi.ComposeCustomerName`. That view returns the customer's first and
  last name separately and both are ciphertext, because two ciphertexts
  concatenated in SQL cannot be decrypted. The page would have rendered base64
  and still returned 200.

  **Mutation tested:** widening `IsEditable` to allow `delivered` fails
  `AnOrderPastThePointOfNoReturnIsNotEditable`.

- **Thread 12, the draft nobody could reach.** Closed 2026-09-06. `CreateOrder`
  wrote `'confirmed'` and nothing else in the product ever wrote `'draft'`
  except reopening a cancelled order. So the status existed, the chip for it
  existed, `IsEditable` allowed it, and no operator could produce one. Every
  order was born saying "somebody has checked eligibility and stock" on the day
  it was typed, which on most orders is not true yet.

  **The bug inside it killed orders outright.** Reopening a cancelled order made
  it a draft. The delivery button only ever rendered on a confirmed order, and
  nothing anywhere could confirm one. That order could never be delivered again,
  only cancelled. Hammas reproduced it on ORD-01031 without meaning to.

  Four parts, no migration:

  1. **Two submit buttons, not a hidden field.** A form with two submits sends
     only the one that was clicked, so the button IS the status. New Order and a
     draft both offer "Save as draft" and "Create Order" / "Save and confirm"; a
     confirmed order offers one button and stays confirmed, because walking it
     backwards would take an order somebody is expecting to deliver off the
     delivery list without anybody deciding to.
  2. **`OrderState` translates the button once**, returning the status AND the
     stage together. The two disagreeing is the kind of thing nobody notices:
     the order screen reads one and the delivery list reads the other. Only the
     draft button produces a draft; anything else, including a post with no
     status at all, confirms, so every existing caller behaves as before.
  3. **`ConfirmOrder`, one click from the order screen.** That is the shape of
     the job: the CMN arrives or the prior authorisation comes back and nothing
     about the order itself changes. Guarded on `Status='draft'` so two clicks
     confirm once, and it refuses an order with no lines for the same reason
     saving one does.
  4. **`Deliver` refuses a draft, on the SERVER.** The order screen has always
     hidden the delivery panel on a draft, and hiding a button is not a guard: a
     POST could deliver a reopened order carrying exactly the stale eligibility
     and stock check that reopening it as a draft was meant to flag. Proved by
     posting to `/Dme/Deliver` on a draft: refused, and no claim, no rental, no
     signature written.

  **Also fixed in passing:** `cancelled` was missing from the status chip list,
  so it fell through to the gray fallback and printed the raw column value in
  lowercase next to "Draft" and "Confirmed" in title case.
