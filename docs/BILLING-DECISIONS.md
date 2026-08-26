# MEDOCS DME Billing and Payments: Closed Decisions

**Date closed:** 2026-08-26
**Closed by:** Hammas (CTO), in session with the PM
**Supersedes:** `HANDOFF.md` section 7 "Still to close"
**Status:** decisions 1 to 4 closed and **built the same day**; decision 5 (where
the clearinghouse credentials live) closed and built the same evening. Migrations
`2026-08-26_DME_Payments_And_Denials.sql` and `2026-08-26_DME_Supplier_And_Sftp.sql`,
`Services/DmePaymentService.cs`, `Services/DmeSftpAccountService.cs`,
`/Dme/Payments`, `/Dme/PostPayment/{id}`, `/Dme/Settings`, the three dashboard tiles.
151 tests passing, 83 end-to-end checks passing. See section 7 below for what
changed between the decision and the implementation.

This file is the decision record. It carries the reasoning, not just the
conclusion, so the next session inherits the argument. It is also the source text
for the client-facing "how the numbers work" documentation, drafted in section 6
below.

---

## The problem being solved

The client (Dr. Roland Okwen, rottamllc) asked for three numbers on the
dashboard, monthly: **amount paid**, **amount denied**, **most frequent denial
code**.

There is no payment data in the system at all. The claim lifecycle ends at
`submitted` and nothing ever comes back. So this is not a dashboard task. It is
building the money-coming-back half of the billing cycle. The three tiles are the
readout on top of it.

---

## Decision 1: How money enters the system

**Decided: manual payment posting, built now. No 835/ERA parser yet.**

A biller reads a paper EOB or a remittance on screen and types the adjudication
into the app. Money from the customer directly (copay, deductible, cash sale,
non-covered rental) posts through the same screen into the same tables.

**We do not have an Office Ally account.** So everything on our side gets built
to completion, and the one thing we genuinely cannot do is blocked at the
`Submit` button, which stays disabled and clearly labelled as awaiting a
clearinghouse account. Nothing else is stubbed.

### Reasoning

- Manual posting is the real workflow for a supplier this size. It is not a
  placeholder for the 835, it is a permanent feature. Even suppliers with full
  EDI post paper checks and counter cash by hand.
- 835 parsing cannot be verified without real files and a real account. Building
  an unverifiable parser is building on faith.
- A supplier takes money from the payer **and** from the customer. Both must land
  in one model or the balance is wrong. An 835-only design has nowhere to record
  cash handed over at the counter.
- The tables are shaped so an 835 parser is later just a **second writer** into
  the same tables. It is not a second money model and it forces no rework.

### Consequence

`Submit` stays blocked pending a clearinghouse account. That is an ops decision
for the client, not an engineering gap.

---

## Decision 2: What "amount denied" means

**Decided: line level. A claim line is denied when paid is zero and it carries a
denial reason code. Amount denied is the sum of the billed charge on those
lines.**

### Reasoning

- **Claims rarely deny whole.** One line denies for a missing CMN while the other
  three pay. A claim-level number would report that claim as fully denied
  (overstating) or as not denied at all (hiding it). Neither is the truth.
- **"Billed minus paid" is the wrong number.** It folds in contractual
  write-offs, which are normal and agreed in advance, and patient
  responsibility, which is money we still expect to collect. A tile built that
  way reads alarmingly high every single month and the client stops trusting it.
- A denial is a payer refusing a line and giving a reason. The definition and the
  data are then the same thing.

### Standing rule that came out of this

**Every money tile and every money column in a report carries a short definition
on screen.** The user must be able to read what the number counts without asking
anyone. This applies to all three tiles and to the billing reports, not only to
"amount denied".

---

## Decision 3: "Monthly" by which date

**Decided: posting date. The same date drives all three tiles. The screen says
so, in plain words.**

Tile subtitle reads, for example, "posted in August 2026".

### Reasoning

- The client asked what came in this month. That is a cash question, and the
  answer has to reconcile against the bank statement, which is also posting date.
- Service date answers a different question, what was earned. A rental delivered
  in June that pays in August would never appear in any August tile.
- Service date also means a closed month never stays closed. Every late payment
  silently restates a prior month, which is exactly the kind of number an
  accountant cannot work with.
- All three tiles on one date means paid, denied and top denial code always
  describe the same set of postings. Mixing dates across tiles would make them
  quietly incomparable.

### Consequence

An accountant looking at the dashboard must not have to assume. The date basis is
stated on the screen, not only in this document.

---

## Decision 4: How much of the money model

**Decided: the full adjudication set, but only two amounts are stored.**

### The shape

Per payment line, store exactly two amounts:

- `AllowedAmount`, what the payer says the line is worth
- `PaidAmount`, what the payer actually sent

Everything else comes from child rows that mirror the X12 CAS segment. One row
per reason the payer moved money:

- group code (`CO` contractual, `PR` patient responsibility, `OA` other, `PI`
  payer initiated)
- reason code (the CARC, for example `CO-45`, `PR-1`, `CO-50`)
- amount

### What is derived, never stored

| Number | How it is computed |
|---|---|
| Adjustment total | sum of `CO` and `PI` rows |
| Patient responsibility | sum of `PR` rows |
| Line denied | paid is zero and a `CO`/`PI` row carries a denial CARC |
| Amount denied (tile) | sum of billed charge on those lines |
| Most frequent denial code (tile) | count over the same reason code rows |
| Claim paid to date | sum of `PaidAmount` across its payment lines |
| Claim balance | billed charge minus paid minus adjustments minus patient responsibility |
| Claim payment status (unpaid / partial / part-denied / denied / patient-due / paid) | computed in `vDmeClaims` |

**Nothing is written back to `DmeClaims`.** No stored totals, no stored status, no
stored paid-to-date. This is the same rule the foundation already applied when
`MonthsBilled`, `OnHand` and `Claims.Total` were dropped. See `HANDOFF.md`
section 3.4.

### Reasoning

- **The third tile falls out of the model for free.** "Most frequent denial code"
  was called out in the handoff as new work in either codebase, because neither
  RehabDox nor IMEHR stores the CARC per line. Storing the CAS rows is what makes
  it a `COUNT` instead of a project.
- **"Denied" is meaningless without separating a denial from a contractual
  write-off.** Both reduce what we get paid. Only one is a problem. The group
  code is what tells them apart.
- **Patient responsibility needs a home** or there is no patient statement, and
  decision 1 already committed to customer money landing in the same model.
- **Storing an adjustment total or a patient responsibility total would be
  storing a sum of rows we already hold.** That is the stock problem. They are
  computed.
- COB, secondary payer crossover and balance forwarding are deliberately **out**.
  Real DME need eventually, not asked for, and not testable without live payer
  data.

### How the entry paths use it

- **Payer payment, manual:** biller types allowed and paid per line, then picks
  the reason codes off the EOB. Full CAS rows.
- **Customer payment:** one line, `PaidAmount` only, no CAS rows. A cash sale, a
  copay, a non-covered rental.
- **835 file, later:** the parser writes identical rows. No schema change.

---

## What stays point-in-time and must not be derived

Unchanged from the foundation work, restated here because billing touches all of
it. A submitted claim is a document as filed. Deriving these would rewrite
history.

- `DmeClaims.CustomerName`, `DmeClaims.PayerName`
- order and claim line prices
- `DmeRentals.MonthlyRate`
- the billed charge on a claim line, once submitted

---

## 6. Client documentation draft: "How the numbers work"

This is the plain-English text for the client. It also drives the on-screen
definitions required by the standing rule in decision 2.

**Amount paid.** The total money received during the selected month, from
insurance payers and from customers together. A payment counts in the month it
was posted in the system, which is the month you received it, not the month the
equipment was delivered.

**Amount denied.** The billed value of the individual claim lines the payer
refused during the selected month. A claim can be partly paid and partly denied,
and only the refused lines are counted here. Contractual discounts, meaning the
difference between what you billed and what the payer agreed to pay, are not
counted as denials. Neither is the balance the patient still owes.

**Most frequent denial code.** The denial reason the payers gave most often
during the selected month, across all refused lines.

**About the month.** All three numbers cover the month the payment or denial was
posted, so they reconcile against your bank statement for the same period. A
rental delivered in June that pays in August appears in August.

---

## Decision 5: Where the clearinghouse credentials live

**Decided 2026-08-26. Tenant scoped, hanging off a supplier profile. DME does
not get locations.**

### The question as asked

RehabDox holds the Office Ally SFTP login per location. Does DME need locations
to match, or is one credential per tenant enough?

### What RehabDox actually does

It is not location based. `OfficeAllySftpAccounts` is owned by the **tenant**,
and `Locations.OfficeAllySftpAccountId` is a nullable pointer. Many locations can
share one account. The pointer exists because Office Ally issues one account per
**billing entity**, and in that product a billing entity is "a tenant, or a
location with its own NPI".

### Why DME is different

**No DME table carries a `LocationId`.** Customers, orders, rentals, claims,
inventory and payments are scoped by tenant and nothing else. There is no second
entity for a credential to belong to, so a location pointer would point at
nothing.

Making DME location aware means adding the column to those tables, putting a
location picker on the order screen, and rethinking the tenant isolation story
around it. That is a large change, for a client with one supplier and no
clearinghouse account to test any of it against.

### What was built instead

`DmeSftpAccounts` is tenant owned and belongs to `DmeSupplierProfile`, not to the
tenant directly. Medicare DMEPOS does require each location that furnishes
equipment to be separately enrolled and accredited, so a supplier genuinely can
end up with two billing identities. When that day comes it is a second profile
row and a pointer column, not a rewrite.

### The bigger half of the same problem

The credential turned out to be the small half. The CMS-1500 billing provider was
a **hardcoded string in the Razor view**:

```
33 Billing provider   Lakeview Medical Supply  NPI 1980000000
```

Every claim this product produced carried an NPI belonging to nobody, and DME had
nowhere to store the real one. A clearinghouse login is worthless while that is
true: it would faithfully upload a claim billed under a provider that does not
exist. So the supplier identity was built first and the credential second.

### The single source of truth test, run before adding the table

| Field of a billing provider | Where it already lives |
|---|---|
| legal name, address, city, state, ZIP, phone | `dbo.Tenants` |
| Tax ID (box 25) | `dbo.Tenants` |
| NPI (box 33a) | `dbo.Tenants` |
| PTAN / supplier number | nowhere |
| Taxonomy code (box 33b) | nowhere |
| Accepts assignment (box 27) | nowhere |

Six of nine already had a home. A `DmeBillingProvider` table holding all nine
would have been six duplicated columns waiting to disagree with `Tenants`. Only
the three with no home are stored, in `DmeSupplierProfile`, keyed on `TenantId`.
`vDmeBillingProvider` joins the rest.

### The trap that came with it

`dbo.Tenants` is the platform registry and is **not** covered by the row level
security policy that protects every DME table. An `UPDATE dbo.Tenants` without an
explicit `WHERE TenantId=@TenantId` renames every tenant on the server and
nothing reports an error. A test enforces that clause on every such statement in
`DmeController`.

### Credential handling

- Username and password are **both** AES-GCM ciphertext. A username is half a
  credential.
- Neither ever reaches a screen: `vDmeSftpAccounts` does not expose the columns,
  and a test fails if any screen reads the base table.
- Neither reaches the audit log, which is kept for six years. The audit records
  that a credential was set, not what it is.
- `IsTestMode` defaults to true. Test versus live belongs to the account, not the
  deployment, so a newly entered account cannot transmit a live claim by
  accident.
- An account is taken out of service, never deleted: the row is the record of
  what past claims were submitted under.
- There is deliberately **no decrypt path and no connection test**. Nothing
  transmits yet, and a decrypt method with no caller is an unguarded way to read
  a password that exists only to look finished. Both arrive with the 837 sender.

### What is still blocked, and it is only this

Building and transmitting the 837 file. It needs a live account to test against.
`/Dme/Submit` is honest about it: the button says **"Mark as submitted"**,
because that is what it does.

---

## 7. What the implementation added to these decisions

Three things the decisions did not cover, decided while building and recorded
here so the reasoning survives.

### `part-denied` as a sixth payment status

Decision 2 defined denial at line level. It did not say what a CLAIM reads as
when one of its lines is refused and the rest pay.

The first implementation had five statuses and such a claim came out as **paid**,
because every line was either paid or written off and the balance was zero. That
is arithmetically true and operationally useless: the refused line would never be
appealed, and appeal windows expire. `part-denied` is now tested before `paid`.

This was caught by loading the page, not by a test. A partial denial is the
ordinary case in DME, not an edge case.

### The tiles count applied money, not receipts

`DmePayments.Amount` is the face value of the check. The payment LINES are what
was allocated to claims. They are different numbers whenever a biller posts a
check and does not fully allocate it, which is the commonest posting mistake
there is.

The tiles count applied money, because "amount paid" has to pair with "amount
denied", which is line level. The gap is therefore reported next to it:
`/Dme/Payments` has an Unapplied figure and the dashboard footnotes it whenever
it is non-zero. The alternative, counting receipts, would have made the tile
agree with the bank while disagreeing with every claim.

### A payment is voided, never edited

Not stated in decision 1. RehabDox reverses a posting by storing the prior values
alongside it. Here the payment is immutable and a mistake is voided, which is
what a biller does on paper.

The whole reversal is one `WHERE IsVoided = 0` in the views. There is no
subtraction to get wrong, and the original entry survives for the audit trail. A
void requires a reason, and the UPDATE is guarded on `VoidedAt IS NULL` so two
people clicking Void produce one reversal and one "already voided".

---

## Verification

```bash
cd ehr-system && dotnet run --urls http://localhost:5077
bash scripts/verify-dme-foundation.sh
```

83 checks. Section 9 covers the payment cycle end to end: a real remittance
posted through the form, the patient balance clearing when the customer pays
cash, a zero dollar denial, a contractual write-off NOT counted as a denial, the
dashboard tiles matching the database to the cent, the server refusing an
over-applied and a future-dated posting, and a void taking every derived number
back with it.

`dotnet test ehr-system/EHR.Tests`: 151 passing. `DmePaymentPostingTests` pins
the rules and the atomicity of the write; its guards were mutation tested.
