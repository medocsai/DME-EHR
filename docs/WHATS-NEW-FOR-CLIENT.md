# MEDOCS DME: what changed

**For:** Roland Okwen, PhD, PMP
**Date:** 27 August 2026

Thank you for the feedback. Every point in your email is done. Here is what to
look at, screen by screen.

---

### Dashboard

**Monthly payments are there now:** amount paid, amount denied, and the most
frequent denial code with how many times it occurred. There is a month picker,
and every figure is by posting date so it reconciles against a bank statement
for the same period.

*Note: this was already built when you wrote. You were looking at an older
version of the site.*

---

### Customers

**Date of birth can be pasted.** It also accepts however you type it:
`01/15/1950`, `1950-01-15`, `Jan 15 1950`. It tidies itself up when you move to
the next field.

**Insurance: 4 payers became 4,017.** This is Office Ally's full published list,
with the Payer ID for each. Start typing and it searches as you go. It knows
that "Blue Cross" and "BCBS" are the same insurer, so you can type what is
printed on the customer's card rather than how the list happens to spell it. You
can also paste a Payer ID straight off a remittance.

**ICD-10: 12 codes became 74,719.** The complete current CMS code set, including
the E66.9 you linked to. Search by condition ("obesity", "knee") or by code, with
or without the dot. Only billable codes are offered, so you cannot accidentally
put a category header on a claim and have it denied.

---

### HCPCS Catalog

**You can add items now.** Search the full national list of 8,623 HCPCS Level II
codes, then set your own price and rental terms. Your catalog stays yours: the
national list supplies the code and the official wording, you supply what you
charge.

The system will not let you add a code that does not exist, or one CMS has
retired, or one you already have.

---

### Orders and Delivery

**Delivery documents.** Every order has a panel for attaching the proof of
delivery: the ticket the driver photographed, the signed paperwork the customer
scanned, the carrier's confirmation. PDF or picture, up to 25MB each. Open them
any time from the order.

Documents are encrypted, and only your staff can open them: there is no shareable
link that would work for anyone who got hold of it. Every attachment, view and
removal is recorded.

---

### Inventory: drop shipping

You said most of what you deliver ships direct from the manufacturer or
distributor. That is now handled properly, and the important part is what it
does **not** do.

**A drop-shipped item never touches your stock figures.** It was never on your
shelf, so counting it in and out would make your inventory numbers wrong for the
majority of what you sell.

Instead:

- Set up your distributors once, under **Distributors**.
- On any order line, choose who it ships direct from and record their order or
  tracking reference. Leave it blank and the item comes out of your own stock, as
  before. The same item can work either way on different orders.
- Inventory shows a third section, **Direct from distributor**, listing what is
  on its way and what has arrived, kept separate from your own stock.
- On an order that ships entirely direct, we no longer ask your driver for a
  signature they were never there to collect. The tracking reference is the
  delivery record.

Billing does not change. A drop-shipped item is still delivered, still rented,
still billed exactly as before.

---

## Two things worth knowing

**Your code lists stay current on their own.** The payer, ICD-10 and HCPCS lists
are national ones we maintain centrally. When CMS or Office Ally publish an
update, everyone gets it. You never have to type a code list in.

**Your prices are always yours.** Nothing in the national lists touches what you
charge, what you stock, or what you have already billed. A claim you sent last
month reads exactly as it did when you sent it, even if a code is reworded later.

---

Anything that does not look right, tell us and we will take a look.
