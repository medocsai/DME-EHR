# MEDOCS DME — Billing and Payments: Closed Decisions

**Date closed:** 2026-08-26
**Closed by:** Hammas (CTO), in session with the PM
**Supersedes:** `HANDOFF.md` section 7 "Still to close"
**Status:** all four decisions closed. No code written yet. Spec and mockups next.

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

## Decision 1 — How money enters the system

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

## Decision 2 — What "amount denied" means

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

## Decision 3 — "Monthly" by which date

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

## Decision 4 — How much of the money model

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
| Claim status (paid / partial / denied) | computed in the view |

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

## Next steps, in order

1. Schema spec: `DmePayments`, `DmePaymentLines`, `DmePaymentLineAdjustments`,
   plus the views that compute claim paid, balance and status.
2. Mockups for the posting screen and the three dashboard tiles. Approved before
   implementation, saved in the repo.
3. Migration, written to run on a fresh database in the documented order.
4. Build, verify by execution, extend `scripts/verify-dme-foundation.sh`.
