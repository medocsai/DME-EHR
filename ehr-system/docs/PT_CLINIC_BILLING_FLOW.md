# Physical Therapy Clinic - Complete Billing & Authorization Flow

## Overview

This document outlines the complete patient journey from registration to billing for a Physical Therapy clinic. It identifies what currently exists in the system vs what needs to be built.

---

## PHASE 1: PATIENT REGISTRATION (New Patient)

### Step 1.1: Create Patient Profile
**User: Front Desk**

```
┌─────────────────────────────────────────────────────────────────┐
│                    NEW PATIENT REGISTRATION                      │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  DEMOGRAPHICS (Patient Table)                                    │
│  ├── First Name, Last Name                                       │
│  ├── Date of Birth, Gender                                       │
│  ├── Phone, Email                                                │
│  ├── Address, City, State, Zip                                   │
│  ├── Emergency Contact                                           │
│  └── SSN (encrypted)                                             │
│                                                                  │
│  [Save & Continue to Insurance →]                                │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

**Status: ✅ EXISTS** - Patient registration works

---

### Step 1.2: Add Insurance Information
**User: Front Desk**

```
┌─────────────────────────────────────────────────────────────────┐
│                    INSURANCE INFORMATION                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Insurance Category: [Dropdown]                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ ○ Workers' Compensation                                   │   │
│  │ ○ Personal Injury (Auto Accident)                         │   │
│  │ ○ Private Insurance                                       │   │
│  │ ○ Self Pay (No Insurance)                                 │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
│  IF Workers' Comp or Personal Injury:                            │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ Date of Injury: [____/____/________]  ← CRITICAL FIELD    │   │
│  │ Claim Number:   [__________________]                      │   │
│  │ Adjuster Name:  [__________________]                      │   │
│  │ Adjuster Phone: [__________________]                      │   │
│  │ Attorney Name:  [__________________] (PI only)            │   │
│  │ Attorney Phone: [__________________]                      │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
│  IF Private Insurance:                                           │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ Insurance Type: ○ Primary  ○ Secondary                    │   │
│  │ Payer Name:     [__________________]                      │   │
│  │ Payer ID:       [__________________]                      │   │
│  │ Policy Number:  [__________________]                      │   │
│  │ Group Number:   [__________________]                      │   │
│  │ Subscriber:     [__________________]                      │   │
│  │ Subscriber DOB: [____/____/________]                      │   │
│  │ Relationship:   [Self/Spouse/Child/Other]                 │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
│  IF Self Pay:                                                    │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │ ⚠️ Patient will pay out-of-pocket for all visits          │   │
│  │                                                           │   │
│  │ Self-Pay Rates (from clinic settings):                    │   │
│  │   Initial Evaluation: $200.00                             │   │
│  │   Follow-up Visit:    $150.00                             │   │
│  │                                                           │   │
│  │ □ Patient acknowledges self-pay responsibility            │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
│  [← Back]  [Save & Continue to Verification →]                   │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

**Status: ⚠️ PARTIAL**
- Insurance entry exists
- Self-pay option exists but rates not configured
- Date of Injury on Patient, should be on Care Episode

---

### Step 1.3: Insurance Verification & Authorization
**User: Front Desk**

```
┌─────────────────────────────────────────────────────────────────┐
│              INSURANCE VERIFICATION & AUTHORIZATION              │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Insurance: Blue Cross Blue Shield                               │
│  Policy #: BCB123456789                                          │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │ [🔍 Verify Eligibility]  [📋 Get Authorization]          │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                  │
│  ELIGIBILITY RESULTS:                                            │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │ ✅ Patient is ELIGIBLE                                   │    │
│  │                                                          │    │
│  │ Coverage Details:                                        │    │
│  │   Co-Pay:        $35.00 per visit                        │    │
│  │   Deductible:    $500.00 (Met: $150.00)                  │    │
│  │   Coinsurance:   20% after deductible                    │    │
│  │   Plan Limit:    60 visits per year                      │    │
│  │                                                          │    │
│  │ Last Verified: 01/29/2026 10:30 AM                       │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                  │
│  AUTHORIZATION:                                                  │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │ Authorization #:  AUTH-2026-12345                        │    │
│  │ Start Date:       01/29/2026        ← NEED TO ADD        │    │
│  │ End Date:         04/29/2026                             │    │
│  │ Visits Authorized: 12                                    │    │
│  │ Visits Used:       0                                     │    │
│  │ Visits Remaining:  12                                    │    │
│  │                                                          │    │
│  │ [+ Add New Authorization]  [✏️ Edit]                     │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                  │
│  [← Back]  [Save & Create Care Episode →]                        │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

**Status: ⚠️ PARTIAL**
- Authorization entry exists
- Missing Start Date field
- Verification is mock data

---

## PHASE 2: CARE EPISODE CREATION

### Step 2.1: Create Care Episode
**User: Front Desk / Clinician**

```
┌─────────────────────────────────────────────────────────────────┐
│                    CREATE CARE EPISODE                           │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Patient: John Smith (DOB: 05/15/1980)                           │
│                                                                  │
│  EPISODE DETAILS:                                                │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │ Episode Start Date: [01/29/2026]                         │    │
│  │ Primary Provider:   [Dr. Sarah Johnson ▼]                │    │
│  │ Primary Diagnosis:  [M54.5 - Low Back Pain ▼]            │    │
│  │ Secondary Dx:       [+ Add]                              │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                  │
│  BILLING INFORMATION FOR THIS EPISODE:     ← NEW SECTION         │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │ Billing Type:                                            │    │
│  │   ○ Insurance    ○ Self-Pay    ○ Workers' Comp   ○ LOP   │    │
│  │                                                          │    │
│  │ IF Insurance/WC/LOP:                                     │    │
│  │   Select Insurance: [Blue Cross (Primary) ▼]             │    │
│  │   Authorization:    [AUTH-2026-12345 ▼] (12 visits)      │    │
│  │   Date of Injury:   [01/15/2026] (if WC/PI)              │    │
│  │                                                          │    │
│  │ IF Self-Pay:                                             │    │
│  │   Rate per Visit:   [$150.00]                            │    │
│  │   □ Patient signed financial agreement                   │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                  │
│  TREATMENT PLAN:                                                 │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │ Expected Visits:    [12]                                 │    │
│  │ Visit Frequency:    [3] times per week                   │    │
│  │ Expected Duration:  [4 weeks]                            │    │
│  │ Episode End Date:   [02/26/2026] (auto-calculated)       │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                  │
│  [Cancel]  [Create Care Episode]                                 │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

**Status: ⚠️ PARTIAL**
- Care Episode creation exists
- Missing: BillingType, InsuranceId, DateOfInjury on episode
- Missing: Link to specific Authorization

---

## PHASE 3: SCHEDULING APPOINTMENTS

### Step 3.1: Schedule from Dashboard
**User: Front Desk**

```
┌─────────────────────────────────────────────────────────────────┐
│                    SCHEDULE APPOINTMENT                          │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Patient: John Smith                                             │
│  Care Episode: Low Back Pain (Started 01/29/2026)                │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │ AUTHORIZATION STATUS                                     │    │
│  │ ┌─────────────────────────────────────────────────────┐ │    │
│  │ │ Authorization: AUTH-2026-12345                      │ │    │
│  │ │ Valid: 01/29/2026 - 04/29/2026                      │ │    │
│  │ │ Visits: 3 used / 12 authorized                      │ │    │
│  │ │ Remaining: 9 visits                                 │ │    │
│  │ │ Status: ✅ Active                                   │ │    │
│  │ └─────────────────────────────────────────────────────┘ │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                  │
│  APPOINTMENT DETAILS:                                            │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │ Date:      [02/05/2026]                                  │    │
│  │ Time:      [10:00 AM ▼]                                  │    │
│  │ Duration:  [45 minutes]                                  │    │
│  │ Provider:  [Dr. Sarah Johnson ▼]                         │    │
│  │ Location:  [Main Clinic ▼]                               │    │
│  │ Type:      [Follow-Up Visit ▼]                           │    │
│  │                                                          │    │
│  │ Billing Type: ○ Insurance  ○ Self-Pay   ← FROM EPISODE   │    │
│  │ Co-Pay Due:   $35.00       ← AUTO-FILLED FROM INSURANCE  │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                  │
│  ⚠️ ALERTS:                                                      │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │ (none currently)                                         │    │
│  │                                                          │    │
│  │ Examples of alerts that would show:                      │    │
│  │ • "Only 2 authorized visits remaining"                   │    │
│  │ • "Authorization expires in 7 days"                      │    │
│  │ • "No active authorization - proceed as self-pay?"       │    │
│  │ • "Patient has outstanding balance: $150.00"             │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                  │
│  [Cancel]  [Schedule Appointment]                                │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

**Status: ⚠️ PARTIAL**
- Scheduling exists
- Authorization alerts exist (basic)
- Missing: BillingType on appointment
- Missing: Patient balance display
- Missing: Link to specific authorization

---

### Step 3.2: Authorization Exhausted - Switch to Self-Pay
**User: Front Desk**

```
┌─────────────────────────────────────────────────────────────────┐
│              ⚠️ AUTHORIZATION VISITS EXHAUSTED                   │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Patient: John Smith                                             │
│  Care Episode: Low Back Pain                                     │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │ ❌ All 12 authorized visits have been used.              │    │
│  │                                                          │    │
│  │ Authorization: AUTH-2026-12345                           │    │
│  │ Visits Used: 12 / 12                                     │    │
│  │ Expires: 04/29/2026                                      │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                  │
│  OPTIONS:                                                        │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │                                                          │    │
│  │ ○ Request New Authorization                              │    │
│  │   Contact insurance for additional visits                │    │
│  │                                                          │    │
│  │ ○ Continue as Self-Pay                      ← IMPORTANT  │    │
│  │   Patient pays $150.00 per visit                         │    │
│  │   ⚠️ These visits will NOT count against authorization   │    │
│  │                                                          │    │
│  │ ○ Discharge Patient                                      │    │
│  │   End care episode                                       │    │
│  │                                                          │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                  │
│  [Cancel]  [Continue with Selected Option]                       │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

**Status: ❌ DOES NOT EXIST**
- No workflow to switch to self-pay mid-episode
- No way to mark individual appointments as self-pay

---

## PHASE 4: PATIENT CHECK-IN

### Step 4.1: Check-In at Front Desk / Kiosk
**User: Front Desk / Patient (Kiosk)**

```
┌─────────────────────────────────────────────────────────────────┐
│                      PATIENT CHECK-IN                            │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Patient: John Smith                                             │
│  Appointment: 02/05/2026 10:00 AM - Follow-Up Visit              │
│  Provider: Dr. Sarah Johnson                                     │
│                                                                  │
│  PAYMENT DUE TODAY:                                              │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │                                                          │    │
│  │ IF INSURANCE VISIT:                                      │    │
│  │   Co-Pay Due:        $35.00                              │    │
│  │   Deductible Due:    $0.00 (met)                         │    │
│  │   ─────────────────────────                              │    │
│  │   Total Due Today:   $35.00                              │    │
│  │                                                          │    │
│  │ IF SELF-PAY VISIT:                                       │    │
│  │   Visit Charge:      $150.00                             │    │
│  │   ─────────────────────────                              │    │
│  │   Total Due Today:   $150.00                             │    │
│  │                                                          │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                  │
│  ACCOUNT STATUS:                           ← NEW SECTION         │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │ Previous Balance:    $70.00                              │    │
│  │ Today's Charges:     $35.00                              │    │
│  │ ─────────────────────────                                │    │
│  │ Total Owed:          $105.00                             │    │
│  │                                                          │    │
│  │ ⚠️ Patient has outstanding balance                       │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                  │
│  COLLECT PAYMENT:                                                │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │ Amount Collecting: [$35.00        ]                      │    │
│  │ Payment Method:    [Credit Card ▼]                       │    │
│  │                                                          │    │
│  │ □ Collect today's co-pay only ($35.00)                   │    │
│  │ □ Collect full balance ($105.00)                         │    │
│  │ □ Custom amount                                          │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                  │
│  [Skip Payment]  [Collect & Check-In]                            │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

**Status: ⚠️ PARTIAL**
- Check-in exists
- Co-pay collection exists
- Missing: Patient balance display
- Missing: Self-pay amount handling
- Missing: Deductible tracking

---

## PHASE 5: VISIT COMPLETION & CHARGE CREATION

### Step 5.1: Provider Completes Visit
**User: Clinician**

```
┌─────────────────────────────────────────────────────────────────┐
│                    COMPLETE VISIT                                │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Patient: John Smith                                             │
│  Appointment: 02/05/2026 - Follow-Up Visit                       │
│                                                                  │
│  CLINICAL NOTE: ✅ Signed                                        │
│                                                                  │
│  BILLING CODES (Auto-populated from note):                       │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │ CPT Code    Description              Units    Charge    │    │
│  │ ─────────────────────────────────────────────────────── │    │
│  │ 97110       Therapeutic Exercise     2        $80.00    │    │
│  │ 97140       Manual Therapy           1        $45.00    │    │
│  │ 97530       Therapeutic Activities   1        $50.00    │    │
│  │ ─────────────────────────────────────────────────────── │    │
│  │ TOTAL                                         $175.00   │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                  │
│  DIAGNOSES (from Care Episode):                                  │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │ M54.5 - Low back pain                                    │    │
│  │ M54.16 - Radiculopathy, lumbar region                    │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                  │
│  [Save as Draft]  [Complete Visit & Create Charges]              │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

**Status: ⚠️ PARTIAL**
- Clinical notes exist
- Charge model exists
- Missing: Auto-charge creation from note signing
- Missing: CPT code management UI

---

### Step 5.2: Charge Created & Patient Responsibility Calculated
**System: Automatic**

```
┌─────────────────────────────────────────────────────────────────┐
│              CHARGE PROCESSING (Automatic)                       │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Visit Date: 02/05/2026                                          │
│  Billing Type: Insurance                                         │
│                                                                  │
│  CHARGE BREAKDOWN:                                               │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │ Total Charges:           $175.00                         │    │
│  │                                                          │    │
│  │ Insurance Allowed:       $140.00  (contracted rate)      │    │
│  │ Adjustment (write-off):  -$35.00  (billed - allowed)     │    │
│  │                                                          │    │
│  │ Insurance Pays:          $112.00  (80% of allowed)       │    │
│  │ Patient Coinsurance:     $28.00   (20% of allowed)       │    │
│  │ Patient Co-Pay:          $35.00   (already collected)    │    │
│  │                                                          │    │
│  │ ─────────────────────────────────────────────────────── │    │
│  │ Patient Responsibility:  $63.00   (copay + coinsurance)  │    │
│  │ Already Paid (co-pay):   -$35.00                         │    │
│  │ Patient Balance:         $28.00                          │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                  │
│  → Charge record created                                         │
│  → Patient ledger updated                                        │
│  → Ready for claim submission                                    │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

**Status: ❌ MOSTLY MISSING**
- Charge model exists
- Payment model exists
- Missing: Auto-calculation of patient responsibility
- Missing: Coinsurance/deductible calculation
- Missing: Automatic ledger updates

---

## PHASE 6: PATIENT RETURNS (New Injury)

### Step 6.1: Returning Patient with New Condition
**User: Front Desk**

```
┌─────────────────────────────────────────────────────────────────┐
│                RETURNING PATIENT - NEW EPISODE                   │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Patient Found: John Smith (DOB: 05/15/1980)                     │
│                                                                  │
│  PREVIOUS CARE EPISODES:                                         │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │ Episode               Status      Dates          DOI     │    │
│  │ ─────────────────────────────────────────────────────── │    │
│  │ Low Back Pain        Completed   01/29-03/15/26  01/15  │    │
│  │   Insurance: Blue Cross, 12 visits used                  │    │
│  │                                                          │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                  │
│  NEW EPISODE:                                                    │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │ This is a:                                               │    │
│  │   ○ Continuation of existing episode (reopen)            │    │
│  │   ● New condition/injury (create new episode)            │    │
│  │                                                          │    │
│  │ NEW EPISODE DETAILS:                                     │    │
│  │   Condition:         [Knee Pain              ]           │    │
│  │   Date of Injury:    [06/20/2026]  ← SEPARATE FROM OLD   │    │
│  │   Insurance:         [Same ▼] [Different Insurance]      │    │
│  │                                                          │    │
│  │ ⚠️ Previous episode data will be preserved                │    │
│  │ ⚠️ New authorization may be required                      │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                  │
│  [Cancel]  [Create New Episode]                                  │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

**Status: ❌ DOES NOT EXIST**
- Currently would overwrite DOI on patient record
- No clear workflow for new episode with different insurance/DOI

---

## PHASE 7: PATIENT BALANCE & STATEMENTS

### Step 7.1: View Patient Balance
**User: Front Desk / Patient**

```
┌─────────────────────────────────────────────────────────────────┐
│                    PATIENT ACCOUNT                               │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Patient: John Smith                                             │
│                                                                  │
│  ACCOUNT SUMMARY:                                                │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │                                                          │    │
│  │   Total Charges:           $2,100.00                     │    │
│  │   Insurance Payments:      -$1,680.00                    │    │
│  │   Insurance Adjustments:   -$315.00                      │    │
│  │   Patient Payments:        -$35.00                       │    │
│  │   ────────────────────────────────────                   │    │
│  │   PATIENT BALANCE:         $70.00                        │    │
│  │                                                          │    │
│  │   [Make Payment]  [View Statement]  [Payment Plan]       │    │
│  │                                                          │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                  │
│  RECENT TRANSACTIONS:                                            │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │ Date        Description                 Amount   Balance │    │
│  │ ─────────────────────────────────────────────────────── │    │
│  │ 02/05/26    Visit Charge               +$175.00  $245.00│    │
│  │ 02/05/26    Co-Pay Collected           -$35.00   $210.00│    │
│  │ 02/10/26    Insurance Payment          -$112.00  $98.00 │    │
│  │ 02/10/26    Insurance Adjustment       -$28.00   $70.00 │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                  │
│  PENDING CLAIMS:                                                 │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │ Date        Service                Status      Amount    │    │
│  │ ─────────────────────────────────────────────────────── │    │
│  │ 02/12/26    Follow-up Visit       Submitted   $175.00  │    │
│  │ 02/14/26    Follow-up Visit       Ready       $175.00  │    │
│  └─────────────────────────────────────────────────────────┘    │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

**Status: ⚠️ PARTIAL**
- API exists: `GET /api/payments/patient/{id}/balance`
- Missing: UI to display this
- Missing: Statement generation
- Missing: Patient portal view

---

## SUMMARY: IMPLEMENTATION STATUS

### ✅ FULLY EXISTS
| Feature | Location |
|---------|----------|
| Patient registration | PatientModule.js |
| Insurance entry (multiple per patient) | Insurance model, UI |
| Authorization entry | Authorization model, UI |
| Care Episode creation | CareEpisode model, UI |
| Appointment scheduling | AppointmentModule.js |
| Basic scheduling alerts | GlobalBridge.js |
| Check-in with co-pay | KioskModule.js |
| Clinical notes | ClinicalNotesModule.js |
| Charge model | Charge.cs |
| Payment model | Payment.cs |
| Patient Ledger model | PatientLedger.cs |
| Balance API | PaymentsController.cs |

### ⚠️ PARTIAL / NEEDS ENHANCEMENT
| Feature | What's Missing |
|---------|----------------|
| Authorization tracking | Start Date, link to appointments |
| Self-pay handling | Rates not configurable, no workflow |
| Check-in | Balance display, self-pay amounts |
| Visits remaining | Counting all visits, not per-auth |
| Charge creation | Not auto-created from notes |

### ❌ DOES NOT EXIST
| Feature | Description |
|---------|-------------|
| BillingType on Appointment | Insurance vs Self-Pay per visit |
| Authorization link on Appointment | Which auth covers this visit |
| DOI on Care Episode | Episode-specific injury date |
| Insurance on Care Episode | Episode-specific insurance |
| Self-pay rate settings | Clinic-configurable rates |
| Patient balance UI | Display balance during workflow |
| Switch to self-pay workflow | When auth exhausted |
| Charge auto-creation | From signed clinical notes |
| Patient responsibility calc | Copay + coinsurance + deductible |
| Statement generation | Patient statements |

---

## RECOMMENDED IMPLEMENTATION ORDER

### Phase 1: Database & Model Changes (Foundation)
1. Add `StartDate` to Authorization
2. Add `BillingType`, `AuthorizationId` to Appointment
3. Add `DateOfInjury`, `InsuranceId`, `BillingType` to CareEpisode
4. Add Self-Pay rate settings to SystemSettings

### Phase 2: Core Workflow Fixes
5. Fix visits remaining calculation (per-auth, exclude self-pay)
6. Update Care Episode creation UI (billing info section)
7. Update Scheduling UI (show auth status, billing type)

### Phase 3: Check-In & Payment
8. Add patient balance display to check-in
9. Handle self-pay appointments at check-in
10. Update payment collection workflow

### Phase 4: Billing Integration
11. Auto-create charges from signed notes
12. Calculate patient responsibility
13. Update patient ledger automatically

### Phase 5: Reporting & Statements
14. Patient balance UI in patient profile
15. Statement generation
16. Aging reports

---

## NEXT STEPS

Which phase would you like to start with? I recommend starting with **Phase 1** (Database changes) as everything else depends on having the correct data model.
