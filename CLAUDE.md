# MEDOCS DME — Claude Instructions

DME/HME (Durable Medical Equipment) platform, converted from the IMEHR EHR.
Built as a **separate copy** of IMEHR so the live EHR is never touched.

## Publish & Deploy
- **Publish path:** `D:\Professional Work\Servers\Medocs\DME Publish`
- **Publish command:**
  ```
  dotnet publish "D:\Professional Work\EHR Systems\imehr-dme\ehr-system\EHR.csproj" -c Release -o "D:\Professional Work\Servers\Medocs\DME Publish"
  ```
- Backend (`EHR.dll`) changes only take effect after the app process is **restarted/recycled** on the server. Static `wwwroot` assets load on a browser hard-refresh.

## Run (local)
```
cd "D:\Professional Work\EHR Systems\imehr-dme\ehr-system"
dotnet run
```
App runs at **http://localhost:5005**.
- **Demo login:** `admin@md.com` / `DemoPass@2026`
- **OTP (dev):** `123456` — hardcoded in Development via `AuthService.cs` (`_isDevelopment`).

## Project Structure
- **Backend:** ASP.NET Core 8 (`ehr-system/`)
- **Frontend:** Vanilla JS modules + Bootstrap 5 (`ehr-system/wwwroot/js/`)
- **Database:** SQL Server — `Server=localhost\SQLEXPRESS01;Database=DMEEHR`
  (NOTE: the default `localhost` instance does NOT exist on this machine — everything is on `SQLEXPRESS01`.)

## DME-specific architecture (added on top of the IMEHR shell)
- **Controller:** `Controllers/DmeController.cs` — Dashboard, Schedule (delivery calendar),
  Customers, Customer/{id}, NewCustomer/CreateCustomer, Inventory, Orders, Order/{id},
  NewOrder/CreateOrder, Deliver (POD → auto-creates rental + claim), Rentals/BillNow,
  Billing, Cms/{id}, Submit. Plus `Controllers/HcpcsController.cs` (HCPCS catalog).
- **Data layer:** `Helpers/DmeDb.cs` — thin raw-ADO (Microsoft.Data.SqlClient) against the
  DME tables, deliberately separate from the generated EF DbContext. `F` = view formatter
  (USD-locked) + `F.StatusChip`. Connection string wired in `Program.cs` via `DmeDb.Init`.
- **Views:** `Views/Dme/*.cshtml`, styled with `wwwroot/css/dme.css`.
- **Nav / branding:** DME nav + "MEDOCS DME / DME SUITE" branding live in
  `Views/Shared/_Layout.cshtml`. Clinical pages still exist but are unlinked at `/Home/*`.
- **Schema:** `Migrations/Manual/2026-06-19_DME_Core_Schema.sql` creates + seeds all DME
  tables (DmeCustomers, DmeOrders, DmeOrderLines, DmeRentals, DmeClaims, DmeClaimLines,
  DmeSerializedUnits, DmeCmns, DmeDoctors, DmePayers, HcpcsCodes, DmeSeq).

## JS / CSS Cache Busting
All JS/CSS are loaded in `Views/Shared/_Layout.cshtml` with `?v=N`. When you edit a `.js` or
`.css` file, bump its `?v` in `_Layout.cshtml` so browsers reload it.

## Constraints
- **NEVER touch** the IMEHR codebase (`..\imehr`), the rehabdox codebase, or the `IMEHR` /
  `PTEHR` databases. All work stays in `imehr-dme` + the `DMEEHR` database.

## Demo notes
- Core flow: New Customer → New Order (HCPCS items) → Deliver + POD signature →
  auto-bills the rental + queues the claim → Billing / CMS-1500. Schedule = delivery calendar.
- Deliberately faked (labeled in-app): live eligibility, 837/835 EDI clearinghouse.
