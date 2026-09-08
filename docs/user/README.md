# MEDOCS DME, user guide

The guide the client reads, and the script we train from.

**Audience: the people who will use the software.** Not developers. No table
names, no code, no architecture. Where a rule exists for a technical reason, the
page says what the rule means for the user and stops there.

## Pages

| | Page | Status |
|---|---|---|
| 01 | Signing in | not written |
| 02 | [The Dashboard](02-dashboard.md) | written |
| 03 | Customers | not written |
| 04 | Orders and delivery | not written |
| 05 | HCPCS catalog | not written |
| 06 | Inventory | not written |
| 07 | Distributors | not written |
| 08 | Rentals | not written |
| 09 | Billing and claims | not written |
| 10 | Payments | not written |
| 11 | Settings | not written |
| 12 | User management | not written |

## The rules this guide is written under

**It lives in the repository, not in a document or a PDF.** A guide kept
anywhere else is a second copy of a fact that already lives in the screen, and
it drifts the first time a button is renamed. Here, a change that renames a
button and does not touch the page is visible in review.

**A page is written only after its screen is clean.** Documenting a screen we
already know is wrong produces a screenshot we have to take again and an
instruction we have to retract. Fix, verify, then write.

**Screenshots are generated, never pasted.** `tools/docs-screenshots/` holds the
scripts: `shoot.js` signs in and captures a screen, `annotate.js` renders the
numbered callouts from a marker file. Re-shooting a screen is a re-run, not a
redraw, and the numbers stay where they were put.

**It will be served at `/docs`, open, with no sign-in.** So it can be sent
before onboarding and read by somebody who is locked out. It contains no patient
data and no customer data: every screenshot is the demo tenant.

## Regenerating a screenshot

With the app running on `http://localhost:5005`:

```bash
node tools/docs-screenshots/shoot.js <outDir> Dme/Dashboard
```

```bash
node tools/docs-screenshots/annotate.js <outDir>/Dme-Dashboard.png tools/docs-screenshots/dashboard-markers.json docs/user/images/dashboard.png 1600
```

Marker coordinates are in CSS pixels against a 1600 pixel wide viewport.
