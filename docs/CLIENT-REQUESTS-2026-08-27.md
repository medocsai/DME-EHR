# Client feedback, 2026-08-27

Source: client email ("I think the platform looks good... our feedback on your
platform is as follows"), relayed by Hammas with screenshots.

**Rule: one item at a time.** Close it, mark it, move to the next. Nothing here
is being worked on unless it says IN PROGRESS. More items are expected from the
same email; append them under their screen heading as they arrive.

| # | Screen | Ask | Status |
|---|---|---|---|
| D1 | Dashboard | Monthly payments: amount paid, amount denied, most frequent denial code | ALREADY BUILT |
| C1 | Customers | Allow copy and paste of information, e.g. date of birth | DONE 2026-08-27 |
| C2 | Customers | List of insurance is not exhaustive | DONE 2026-08-27 |
| C3 | Customers | ICD-10 codes for DME not exhaustive | DONE 2026-08-27 |
| O1 | Orders / Delivery | HCPCS item list not exhaustive | DONE 2026-08-27 |
| O2 | Orders / Delivery | Proof of delivery: attach files (PDF, pictures, etc) | NOT STARTED |
| H1 | HCPCS Catalog | List not exhaustive | DONE 2026-08-27, same item as O1 |
| I1 | Inventory | Most delivered items are drop-shipped from manufacturers / distributors. Can that be included? | DONE 2026-08-27 |

**That is the whole email.** Sent by Roland Okwen, PhD, PMP, CEO and Sales
Director. Nothing else outstanding from it.

---

## D1. Dashboard: monthly payments (ALREADY BUILT)

**Asked:** add monthly payments to the dashboard: amount paid, amount denied,
most frequent denial code.

**Already there.** Built 2026-08-26 with the payments work, as the "Money this
month" section with a month picker: amount paid (with an unapplied footnote),
amount denied (with the refused line count), and the top denial code with its
description and how many times it occurred. All computed from the payment views,
nothing stored.

**The client has not seen it.** Their screenshot is the older dashboard. The work
is uncommitted and unpublished, so they keep seeing the old screen until we
deploy. Nothing to build; this closes on a deploy, not on code.

---

## C1. Copy and paste into the customer form (DONE 2026-08-27)

**Asked:** allow copying and pasting of information, e.g. date of birth.

**Cause:** the Date of Birth field was `<input type="date">`. Chrome refuses a
paste into the native date control, so a DOB copied off a referral could only be
retyped one segment at a time. Every other field on the form already pasted fine,
which is why the client named this one.

**Built:**

- The field is plain text now, labelled "Type or paste. mm/dd/yyyy".
- `Helpers/DateInput.cs` is the one place that reads a typed date. It accepts
  `01/15/1950`, `1/15/1950`, `01-15-1950`, `1950-01-15`, `1950/01/15`,
  `Jan 15 1950`, `Jan 15, 1950`, `15 Jan 1950`, `January 15, 1950`, and trims
  the non-breaking spaces a paste out of a PDF carries.
- An unreadable value is left null, never guessed at.
- The field reformats to mm/dd/yyyy on blur so the operator sees what was
  understood.

**Why the server changed too, not just the input:** the action used to take a
`DateTime?` and let model binding parse it under `CultureInfo.CurrentCulture`,
which is whatever locale the server happens to run under. `03/04/1950` would be
March on one machine and April on another, silently, with no error either way.
A DOB off by a month fails eligibility and the claim comes back denied. The
format list is now explicit and month-first.

**Verified:** `DmeDateInputTests` covers every accepted spelling, the month-first
order under en-US / en-GB / de-DE, and the refusals. See the shared verification
note at the end of this document.

**Mutation survivor, reported not hidden:** swapping `InvariantCulture` for
`CurrentCulture` fails no test, because `/` and `Jan` mean the same thing in the
three locales tested. The guard that actually carries the weight is the explicit
month-first format list, and mutating that to day-first fails 6 tests.
`InvariantCulture` stays as defence, but it is not what the tests are pinning.

**Not done, deliberately:** there is no customer EDIT form in the product, so
this was a one-field change. If one is added, it calls `DateInput` too.

---

## C2. The insurance list is not exhaustive (DONE 2026-08-27)

**Built.** 4 payers became **4,017**, the Office Ally national list, with a
typeahead instead of a dropdown. Operating detail is in `CLAUDE.md`; the
investigation and the three decisions that shaped it are kept below, because
they are the argument and not just the conclusion.

**The three questions, as decided:**

1. **One list, global, no per-tenant additions.** A payer off the Office Ally
   list has no Payer ID, so no 837 can be addressed to it. Letting a supplier
   invent one produces a record that looks billable and fails weeks later.
2. **`PayerType` dropped.** Nothing read it and the vendor export has no
   equivalent.
3. **The `ALLCA` duplicate kept, both rows.** Office Ally issues that code to two
   payers. `PayerCode` is indexed, not unique.

**Three things turned up on the way that were not in the plan:**

1. **The search failed the client's own use case.** Typing "Blue Cross of Texas"
   returned NOTHING, because the vendor spells it "BCBS Texas". 145 rows say
   BCBS and 18 say Blue Cross. Found by typing into the control in a browser,
   not by any test. Matching is now word by word in any order, noise words are
   dropped, and the catalog knows the two spellings are the same insurer.
2. **A real pre-existing bug in the customer form.** `DmeDb` puts an
   `@LocationId` on every command holding the branch being VIEWED, and SQL
   parameter names are case insensitive, so the customer INSERT writing
   `@locationId` was naming that injected parameter and passing no value of its
   own. Filing a customer into Dallas while viewing Houston put them in Houston,
   and creating one while viewing ALL branches failed with a 500. The branch
   picker on the form was decorative and nothing said so. Fixed, and pinned by a
   test.
3. **The verification section I wrote deleted a seeded customer.** It took "the
   newest customer" as the one it had just created; the POST hit the 500 above,
   so that was Frank Marsh (LMS-1005), and the cleanup removed him. Restored
   from the seed and re-encrypted through `POST /Dme/BackfillPhi`. The section
   now takes `MAX(CustomerId)` before it writes and deletes only above that
   line, so it cannot touch a row it did not create. **His `LocationId` was
   restored as 1 (Main Office), which is the primary branch and matches where he
   appeared on the dashboard, but it is a reconstruction rather than the
   original value.**

**Verified:** see the shared verification note at the end of this document.

---

### The investigation that decided it

**Asked:** the list of insurance is not exhaustive. Hammas: Office Ally supplied
a payer list with Payer IDs during that integration; find it in RehabDox or the
PTEHR database first, then we start.

**Found, in both places:**

- `PTEHR.dbo.Payers` — **4,017 rows**, all active, every row has a Payer ID.
  One duplicate code: `ALLCA` appears twice.
- The file they were loaded from:
  `rehabdox-ptehr/rehabdox-webapp/ehr-system/App_Data/payers_import.json`,
  320KB, `name` + `payerIdCode` only. That is the clean import and the thing to
  reuse. RehabDox loads it through `Data/DbSeeder.cs` and a
  `LookupsController` import action.

**What DME has today:** `DmePayers` holds **4 rows** (Medicare Railroad/Part B,
BCBS of Texas, Aetna, Texas Medicaid), seeded in
`2026-06-19_DME_Core_Schema.sql`, tenant scoped, and copied into every new tenant
by `DME_Onboard_New_Tenant.sql`. There is no screen to add to it, so a supplier
cannot fix it themselves.

**Recommendation (not yet approved):** load the 4,017 as ONE GLOBAL catalog, the
way `DmeCarcCodes` already is, not tenant scoped, and search it with a typeahead
on the customer form instead of a dropdown of 4,017 options. Copying a national
list into every tenant is 4,017 duplicates per supplier of a list none of them
owns, and they drift the first time one copy is corrected.

`DmeCustomerInsurances` already stores `PayerName` and `PayerId` on the customer
record, so what a claim was billed under stays pinned even if the catalog is
corrected later. That is the point-in-time fact and it is already in the right
place.

**Reported, not built:** CMS-1500 **box 1** (Medicare / Medicaid / Tricare /
group health) is not implemented on the claim form at all. It turned up while
deciding what to do with `PayerType`. Real, unrelated to this item, and its own
piece of work if you want it.

---

## C3. ICD-10 codes for DME are not exhaustive (DONE 2026-08-27)

**Built.** 12 hardcoded codes became **74,719**, the whole CMS FY2026 ICD-10-CM
code set, with a typeahead. Operating detail is in `CLAUDE.md`.

**Where the list came from, and why not the client's link.** Their link is one
browsable page of a commercial site. CMS publishes the code set itself, free,
and it is what Office Ally adjudicates against:
`https://www.cms.gov/files/zip/2026-code-descriptions-tabular-order.zip`.

**The decision that mattered: the CODES file, not the ORDER file.** The order
file has 98,186 rows and includes header codes; the codes file has 74,719 and
holds only codes VALID FOR SUBMISSION. E66.9, the code the client linked to, is
in it. The E66 header it sits under is not, and a header code on a claim is a
denial. A test and a verification check both pin this.

**The whole list, not a DME subset.** A curated subset is a judgement call, and
a wrong one produces this exact complaint again in three months.

**Two things worth knowing:**

1. **A code is found with or without its dot.** CMS ships `E669`; the product
   stores `E66.9`; a biller may type either. Both sides are compared undotted.
2. **The description is stored on the diagnosis, not joined.** CMS rewords codes
   every October and what a claim was billed under must not move. Same reasoning
   as `DmeClaims.CustomerName`.

**A mistyped code now files NOTHING.** The old hardcoded list produced a
diagnosis with a blank description for anything outside its twelve entries.

**Annual refresh:** ICD-10-CM changes every October 1. Reloading is the same
script with a new VALUES block.

---

### The investigation that decided it

**Asked:** ICD-10 codes for DME not exhaustive. Client supplied a reference link:
`https://www.icd10data.com/ICD10CM/Codes/E00-E89/E65-E68/E66-/E66.9`

**What is there today:** 12 codes, hardcoded as a C# array in
`Controllers/DmeController.cs` (`IcdList`, around line 278). It feeds the primary
diagnosis dropdown on the customer form, and `CreateCustomer` looks the
description back out of the same array before writing
`DmeCustomerDiagnoses`.

**Direction, not yet specced:** a real `IcdCodes` table with a searchable
typeahead, the same shape `HcpcsCodes` already uses, and global rather than
tenant scoped like `DmeCarcCodes`, so a new tenant gets it free.

**To settle before building:** where the code list comes from. The client's link
is one page of the ICD-10-CM tree, not a data source. The full CMS ICD-10-CM
release is ~74,000 codes; a DME-relevant subset is a judgement call, and a wrong
subset produces exactly this complaint again. Check whether IMEHR or RehabDox
already carries an ICD-10 table before sourcing one, the same way the payer list
turned out to already exist.

---

## O1 and H1. HCPCS item list is not exhaustive (DONE 2026-08-27)

One piece of work, raised twice because they hit it on two screens.

**Built.** The CMS national HCPCS Level II list, **8,623 codes**, plus an **Add
item** form on `/Hcpcs`. Operating detail is in `CLAUDE.md`.

**This one was NOT the same shape as C2 and C3, and that is the whole point.**

`dbo.HcpcsCodes` is not a code list. It is the supplier's ITEM MASTER: 22 items
at their prices, with their rental terms and their reorder points. Those are
facts the supplier owns and they differ between suppliers, so the table is
correctly tenant scoped. The national release has none of them.

Pouring 8,623 national codes into it would have left every row with blank
pricing and turned a national list into tenant data, which is exactly the
mistake the payer work had just undone. So there are two tables:

| | |
|---|---|
| `dbo.HcpcsNationalCodes` | what CMS publishes. Global. |
| `dbo.HcpcsCodes` | what THIS supplier sells, at THIS supplier's price. |

Adding an item is now "find the code, set your price".

**Where it came from:**
`https://www.cms.gov/files/zip/january-2026-alpha-numeric-hcpcs-file.zip`, a
fixed width file whose layout ships in the same zip.

**Three things the file forced:**

1. **Modifier records are not codes.** The file interleaves them with procedure
   records. Only procedure records are loaded.
2. **Level II only, which also avoids a copyright problem.** Level I is CPT and
   its descriptions are AMA copyright; the CMS layout says so in as many words.
   Every code in this file is letter-prefixed, and a test fails on a numeric one.
3. **Some descriptions run past 4,000 characters**, so `LongDescription` is
   `NVARCHAR(MAX)` and cannot be indexed. At 8,623 rows that costs nothing.

**Adding an item is guarded three ways:** an invented code, a retired code, and
a duplicate are each refused with a sentence rather than a stack trace. Admin
roles only, and audited. **1,300 of the 8,623 codes are retired**, so that middle
guard is not hypothetical.

**Terminated codes are kept, unlike ICD.** A supplier's item master may already
point at one, and "retired on 2024-12-31" is a better answer than a blank.

**Verified on the real data:** all 22 items the supplier already stocks name a
real HCPCS code. That is checked on every verification run.

---

## O2. Proof of delivery: attach files (NOT STARTED)

**Asked:** allow the option to attach proof of delivery files as PDF, pictures,
etc.

Today the POD panel on an order captures "Received by" and a drawn signature
only. There is nowhere to put the delivery ticket the driver photographed or the
signed paper the customer scanned, and that is the document that defends the
claim in an audit.

**Note, and it matters:** the Attachments panel on the New Customer screen is
DEMO UI. It lists what you pick and persists nothing, and the code says so. So
this is the first real file upload in the product, not a second one, which means
it brings storage, size and type limits, and the question of where PHI-bearing
files live at rest. Bigger than it looks from the screenshot.

**Storage is decided (Hammas, 2026-08-27): Google Cloud Storage, a new bucket
alongside the RehabDox one.** RehabDox already does this end to end and it was
read for the pattern. Not being built now; the approach is written below so the
next session starts from a decision instead of a survey.

### How to do it, when we do it

**What RehabDox already has, and we copy the shape of:**

- `Google.Cloud.Storage.V1` 4.10.0, one package.
- `Services/Storage/GoogleCloudStorageService.cs` behind an `IFileStorageService`
  interface, registered as a singleton, options bound from a `GoogleCloudStorage`
  section (`BucketName`, `ProjectId`, `CredentialsPath`, `MaxFileSizeBytes`).
- `Services/Storage/Helpers/FilePathBuilder.cs` builds every object key in one
  place: `patient-documents/{tenantId}/{patientId}/...`.
- `Services/PatientDocumentService.cs` is the domain half: validate, encrypt,
  upload, then write one database row.

**The five things that carry over, and the two we change:**

1. **New bucket, ours.** Never the RehabDox bucket. The Google Cloud billing
   account is shared with RehabDox, and that is a billing arrangement only: this
   is a standalone product and the bucket is its own, named for DME rather than
   as a RehabDox or Medocs sub-thing. **Hammas names it at build time.** Object
   key `pod/{tenantId}/{orderId}/{storageFileName}`, so
   a tenant's files are separable by prefix as well as by row.
2. **The file is encrypted before it leaves the app**, with the same
   `EncryptionHelper` the customer PHI uses, and uploaded as
   `application/octet-stream`. A proof of delivery carries the customer's name,
   their address and their signature. It is PHI and the bucket must never hold it
   in the clear.
3. **The stored filename carries no PHI.** RehabDox generates an opaque storage
   name and encrypts the original filename into the database row. Same here: a
   bucket listing must not read `john-doe-oxygen-pod.pdf`.
4. **Validate three ways, not one:** extension allow-list, MIME allow-list, and
   the leading magic bytes actually matching the extension. RehabDox does all
   three (`IsValidFileContent`) and it is the check that stops a renamed
   executable. PDF, JPG, PNG, HEIC and TIFF are the realistic POD set.
5. **Size cap in configuration**, and enforced server side. A phone photo of a
   delivery ticket is 3 to 12MB, so 25MB is generous. RehabDox allows 70MB.

**Changed from RehabDox, deliberately:**

- **No signed URLs for PHI.** RehabDox can hand out a `GetSignedUrlAsync` link
  that is valid for an hour to anyone holding it. Serve the file through our own
  controller instead: authenticated, tenant checked, and `[PhiAccessAudit]`
  logged like every other PHI read in this product. A signed URL leaves the
  audit trail and the tenant check behind, which is exactly what the foundation
  work was for.
- **No `IsDeleted` or `IsEncrypted` columns.** Every file is encrypted, so the
  flag is a constant. Deletion follows the payments pattern already in this
  product: store `DeletedAt` and let `IsDeleted` be derived in the view. A POD is
  the evidence the claim was legitimate, so it is retired, never erased.

**The table**, `DmeOrderDocuments`, one row per file, nothing derived:
`DocumentId`, `TenantId`, `OrderId`, `FileName` (ciphertext, the original name),
`StoragePath` (the object key), `ContentType`, `FileSize`, `FileHash` (SHA-256 of
the plaintext, so tampering is detectable), `UploadedByUserId`, `CreatedAt`,
`DeletedAt` NULL. In the RLS policy like every other DME table. `LocationId` does
NOT go on it: the branch derives from the order, per the locations rule.

**Credentials.** A service account JSON key. It must not be committed, and
`SecretsGuard` should refuse to start outside Development when the bucket is
configured and the key is missing, the same way it already guards `Jwt:Key` and
`Encryption:Key`. RehabDox keeps theirs at
`App_Data/Credentials/gcs-service-account.json`; check whether that path is in
their git history before copying the habit.

**Scope note.** The customer Attachments panel is the same service with a
different path prefix (`customer-documents/{tenantId}/{customerId}`), so making
it real is a small addition once this exists, not a second build. Decide at spec
time whether it is in the same pass. It is currently demo UI that silently
persists nothing, which is worse than not having it.

---

## H1. HCPCS catalog: list not exhaustive (DONE 2026-08-27)

The same complaint as O1, raised twice because they hit it on two screens: the
order line picker and the HCPCS Catalog screen itself. **One piece of work, and
it is written up under O1 above.**

The Add item form lives on the HCPCS Catalog screen, which is where a supplier
decides what they sell. The order line picker keeps reading the item master,
because you can only sell what you stock and have priced.

---

## I1. Drop-shipped items in inventory (DONE 2026-08-27)

**Built, and no client conversation was needed after all.** Their sentence sits
under Inventory, so the ask is visibility, not a new billing path. Operating
detail is in `CLAUDE.md`.

**The fix is mostly an ABSENCE, and that is the whole design.** A drop-shipped
line writes **no stock movement and no serialised unit**, because nothing
entered or left a warehouse. On-hand therefore stays correct by construction
rather than by remembering to compensate. Forcing these through the ledger would
make the inventory figures wrong for the MAJORITY of what they deliver, since
most of their items ship this way. That is the stock problem at full scale.

**Two facts stored, both on the ORDER LINE**, because the same item ships from
stock one week and direct the next: `DistributorId` and `DistributorRef`.
**There is no is-drop-shipped flag**; it is derived from the distributor being
present, so there is no second copy to fall out of step.

**Distributors are tenant data**, unlike the three national catalogs: each
supplier negotiates their own. Retired, never deleted, and a retired one still
resolves on an old order so it still says who shipped it.

**Three consequences, all deliberate:**

1. **Inventory shows a third panel**, kept separate from both stock numbers. An
   item in somebody else's warehouse is not stock this supplier holds.
2. **An all drop-shipped order asks for no signature.** Nobody from this supplier
   is at the door; the tracking reference is the delivery evidence. A MIXED order
   still asks, because somebody is there with part of it.
3. **Billing is untouched.** The guard sits AFTER the rental and the claim line,
   so a drop-shipped item is still delivered, rented and billed. Moving it up
   three lines would silently stop billing most of their business, and two tests
   pin the ordering in both directions.

**Proved on a MIXED order end to end:** the drop-shipped hospital bed moved no
stock and created no unit but was still billed $135; the walker from stock moved
-2 and created a unit. Both on one order, one delivery.

**One thing found and avoided:** a filtered index on `DmeOrderLines` would have
made every future write to that table require `QUOTED_IDENTIFIER ON`, which
`sqlcmd` does not set by default. Three tables already carry that constraint
from earlier migrations; this one deliberately does not join them.

---

### The analysis, before it was built

**Asked, in their words:** "Most of the items we deliver are drop-shipped from
manufacturer/distributors. Is it possible include it here?"

This is the only item in the email that is not a longer list or a file upload. It
is a model question, and it is the important one.

**Why it matters.** Inventory today assumes the supplier holds the goods:
`DmeStockMovements` is a ledger of physical stock in a branch, `DmeSerializedUnits`
tracks individual units, and the Inventory screen shows what the company holds
and what this branch holds. A drop-shipped item never enters a branch. It goes
from the distributor to the customer's home, so it has no stock movement, no
serial in our possession, and no on-hand number that means anything. Forcing it
through the stock ledger would produce inventory figures that are wrong for the
majority of what they deliver, which is precisely the stock problem.

**Do NOT assume this is a checkbox on the item.** Before a line of code:
establish what they actually need to see and do. Likely candidates, to be
confirmed with the client rather than guessed:

1. Mark an item, or a line, as drop-shipped so it is excluded from stock counts.
2. Record which distributor it ships from, and their order or tracking reference.
3. Know it shipped, and when, so the POD and the claim still line up.

Billing and POD do not change: a drop-shipped item is still delivered, still
signed for, still billed. Only the stock half changes.

**This one needs a conversation with the client before a spec.** Every other item
in the email is a known answer; this one is a requirement that has not been
stated yet.

---

## Verification, all six closed items

Everything below was proved by running it, not by reading it.

```
dotnet build                                     clean, 0 errors
dotnet test ehr-system/EHR.Tests                 303 passing, 0 failing
bash ehr-system/scripts/verify-dme-foundation.sh 159 checks, 0 failures
```

Tests went 205 → 303 across this work. Every guard written was **mutation
tested**: broken deliberately, and the right test confirmed to fail. One
survivor was found and reported rather than quietly patched (see C1).

Verification sections **13**, **14** and **15** cover the three code catalogs and drop shipping end to
end over HTTP: that each list is global and outside the security policy, that
the searches answer the way a human types, that the lookups need a session, and
that the server refuses a forged payer, an invalid diagnosis, an invented HCPCS
code, a retired one and a duplicate, and that a drop-shipped line touches no stock while still being billed.

All four migrations were run **twice** on the local database to prove they are
re-runnable, and every screen was driven in a real browser.

**Two rules these sections now follow, both learned the hard way here:**

1. **A check that cleans up must prove it made the mess.** Take the high water
   mark before writing anything and delete only above it.
2. **Count matches, not lines.** `grep -c` on a one-line JSON response is always
   1, so a check comparing two such counts passes when both searches return
   nothing.

3. **`sqlcmd -I`, not `sqlcmd`.** Four tables carry filtered indexes, which makes
   every write against them require `QUOTED_IDENTIFIER ON`. Without `-I` the
   cleanup fails with an error that mentions neither the index nor the table.

**Nothing is deployed.** The client is still looking at the old build.