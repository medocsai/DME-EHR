# PT Clinic EHR - Complete Workflow Guide

## For Clinic Staff: How to Use the System

---

# PART 1: NEW PATIENT REGISTRATION

## Step 1.1: Patient Arrives - Front Desk Creates Profile

**Who:** Front Desk Staff

**What You Do:**

1. Click **"New Patient"**
2. Enter patient demographics:
   - First Name, Last Name
   - Date of Birth
   - Gender
   - Phone Number
   - Email
   - Address, City, State, ZIP
   - Emergency Contact Name & Phone

3. Click **"Next: Insurance"**

---

## Step 1.2: Select Insurance Type

**Who:** Front Desk Staff

**What You See:**

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│   INSURANCE TYPE                                                │
│                                                                 │
│   How will this patient's treatment be paid?                    │
│                                                                 │
│   ○ Workers' Compensation                                       │
│     (Work-related injury, employer's insurance pays)            │
│                                                                 │
│   ○ Personal Injury / LOP                                       │
│     (Auto accident or injury, attorney involved)                │
│                                                                 │
│   ○ Private Insurance                                           │
│     (Blue Cross, Aetna, UnitedHealth, etc.)                     │
│                                                                 │
│   ○ Medicare / Medicaid                                         │
│     (Government insurance)                                      │
│                                                                 │
│   ○ Self-Pay                                                    │
│     (Patient pays out-of-pocket, no insurance)                  │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

**Based on selection, different screens appear...**

---

## Step 1.3A: Workers' Compensation Details

**When:** Patient selected "Workers' Compensation"

**What You Enter:**

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│   WORKERS' COMPENSATION                                         │
│                                                                 │
│   INJURY INFORMATION                                            │
│   ├── Date of Injury:         [  /  /    ]                      │
│   ├── Description:            [Lifted heavy box, back pain]     │
│   └── Body Part:              [Lower Back ▼]                    │
│                                                                 │
│   EMPLOYER INFORMATION                                          │
│   ├── Employer Name:          [ABC Company           ]          │
│   ├── Employer Phone:         [(555) 123-4567        ]          │
│   └── Employer Address:       [123 Main St, City     ]          │
│                                                                 │
│   WORKERS' COMP CARRIER                                         │
│   ├── Carrier Name:           [State Fund Insurance  ]          │
│   ├── Carrier Phone:          [(800) 555-1234        ]          │
│   ├── Claim Number:           [WC-2026-12345         ]          │
│   ├── Adjuster Name:          [Mary Johnson          ]          │
│   └── Adjuster Phone:         [(800) 555-1235        ]          │
│                                                                 │
│   AUTHORIZATION                                                 │
│   ├── Authorization #:        [AUTH-WC-67890         ]          │
│   ├── Visits Authorized:      [12    ]                          │
│   ├── Valid From:             [01/15/2026]                      │
│   └── Valid To:               [04/15/2026]                      │
│                                                                 │
│   ┌───────────────────────────────────────────────────────┐    │
│   │ PATIENT PAYMENT: $0.00 per visit                       │    │
│   │ Workers' Comp covers 100% - no patient responsibility  │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   [← Back]                              [Save & Continue →]     │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Step 1.3B: Personal Injury / LOP Details

**When:** Patient selected "Personal Injury / LOP"

**What You Enter:**

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│   PERSONAL INJURY / LETTER OF PROTECTION                        │
│                                                                 │
│   INJURY INFORMATION                                            │
│   ├── Date of Injury:         [  /  /    ]                      │
│   ├── Accident Type:          [Auto Accident ▼]                 │
│   │                           - Auto Accident                   │
│   │                           - Slip and Fall                   │
│   │                           - Other                           │
│   └── Description:            [Rear-ended at stoplight]         │
│                                                                 │
│   ATTORNEY INFORMATION                                          │
│   ├── Attorney Name:          [John Smith, Esq.      ]          │
│   ├── Law Firm:               [Smith & Associates    ]          │
│   ├── Attorney Phone:         [(555) 987-6543        ]          │
│   ├── Attorney Fax:           [(555) 987-6544        ]          │
│   ├── Attorney Email:         [john@smithlaw.com     ]          │
│   └── Case Number:            [PI-2026-5678          ]          │
│                                                                 │
│   AUTO INSURANCE (if auto accident)                             │
│   ├── Insurance Company:      [Geico                 ]          │
│   ├── Policy Number:          [POL-123456            ]          │
│   └── Claim Number:           [CLM-789012            ]          │
│                                                                 │
│   AUTHORIZATION / LOP                                           │
│   ├── LOP Signed:             [✓] Yes                           │
│   ├── LOP Date:               [01/20/2026]                      │
│   ├── Treatment Authorized:   [20    ] visits                   │
│   └── Authorization Expires:  [07/20/2026]                      │
│                                                                 │
│   ┌───────────────────────────────────────────────────────┐    │
│   │ PATIENT PAYMENT: $0.00 per visit                       │    │
│   │ Charges billed to case - settled when case closes      │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   [← Back]                              [Save & Continue →]     │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Step 1.3C: Private Insurance Details

**When:** Patient selected "Private Insurance"

**What You Enter:**

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│   PRIVATE INSURANCE                                             │
│                                                                 │
│   PRIMARY INSURANCE                                             │
│   ├── Payer Name:             [Blue Cross Blue Shield ▼]        │
│   ├── Payer ID:               [BCBS123              ]           │
│   ├── Policy Number:          [XYZ789456123         ]           │
│   ├── Group Number:           [GRP-001              ]           │
│   └── Phone:                  [(800) 555-2222       ]           │
│                                                                 │
│   SUBSCRIBER (Policy Holder)                                    │
│   ├── Same as Patient?        [✓] Yes  [ ] No                   │
│   │   (If No, enter subscriber details below)                   │
│   ├── Subscriber Name:        [John Smith           ]           │
│   ├── Subscriber DOB:         [05/15/1980           ]           │
│   ├── Subscriber ID:          [ABC123456            ]           │
│   └── Relationship:           [Self ▼]                          │
│                               - Self                            │
│                               - Spouse                          │
│                               - Child                           │
│                               - Other                           │
│                                                                 │
│   ┌───────────────────────────────────────────────────────┐    │
│   │ [+ Add Secondary Insurance]                            │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   ─────────────────────────────────────────────────────────    │
│                                                                 │
│   VERIFY ELIGIBILITY                                            │
│   ┌───────────────────────────────────────────────────────┐    │
│   │                                                        │    │
│   │   [🔍 Verify Insurance Eligibility]                    │    │
│   │                                                        │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   (After clicking Verify...)                                    │
│                                                                 │
│   ELIGIBILITY RESULTS                                           │
│   ┌───────────────────────────────────────────────────────┐    │
│   │ ✅ PATIENT IS ELIGIBLE                                 │    │
│   │                                                        │    │
│   │ Coverage Type:      Physical Therapy                   │    │
│   │ Effective Date:     01/01/2026                         │    │
│   │                                                        │    │
│   │ COST SHARING:                                          │    │
│   │ ├── Co-Pay:         $35.00 per visit                   │    │
│   │ ├── Deductible:     $500.00 per year                   │    │
│   │ │   └── Met:        $200.00 (Remaining: $300.00)       │    │
│   │ ├── Co-Insurance:   20% (patient pays after deductible)│    │
│   │ └── Out-of-Pocket Max: $3,000.00                       │    │
│   │                                                        │    │
│   │ LIMITS:                                                │    │
│   │ ├── Annual Visits:  60 visits per calendar year        │    │
│   │ └── Used This Year: 0 visits                           │    │
│   │                                                        │    │
│   │ Verified: 01/29/2026 10:30 AM                          │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   AUTHORIZATION                                                 │
│   ┌───────────────────────────────────────────────────────┐    │
│   │                                                        │    │
│   │ Does this payer require prior authorization?           │    │
│   │ [✓] Yes  [ ] No                                        │    │
│   │                                                        │    │
│   │ Authorization #:      [AUTH-2026-12345     ]           │    │
│   │ Visits Authorized:    [12    ]                         │    │
│   │ Valid From:           [01/29/2026]                     │    │
│   │ Valid To:             [04/29/2026]                     │    │
│   │                                                        │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   ┌───────────────────────────────────────────────────────┐    │
│   │ PATIENT PAYMENT PER VISIT:                             │    │
│   │                                                        │    │
│   │ Co-Pay:                    $35.00                      │    │
│   │ Deductible (until met):    up to $300.00 remaining     │    │
│   │ Co-Insurance (after ded):  20% of allowed amount       │    │
│   │                                                        │    │
│   │ ⚠️ Patient responsible for co-pay at each visit        │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   [← Back]                              [Save & Continue →]     │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Step 1.3D: Medicare / Medicaid Details

**When:** Patient selected "Medicare / Medicaid"

**What You Enter:**

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│   MEDICARE / MEDICAID                                           │
│                                                                 │
│   COVERAGE TYPE                                                 │
│   ○ Medicare Part B (Original Medicare)                         │
│   ○ Medicare Advantage (HMO/PPO plan)                           │
│   ○ Medicaid                                                    │
│   ○ Medicare + Medicaid (Dual Eligible)                         │
│                                                                 │
│   MEDICARE INFORMATION                                          │
│   ├── Medicare Number (MBI): [1EG4-TE5-MK72     ]               │
│   ├── Part B Effective:      [01/01/2024        ]               │
│   └── Medicare Advantage Plan: [Humana Gold Plus ▼] (if any)    │
│                                                                 │
│   SECONDARY INSURANCE (Medigap)                                 │
│   ├── Has Medigap Policy?    [ ] Yes  [✓] No                    │
│   │   (If Yes, enter Medigap details)                           │
│   └── [+ Add Medigap Policy]                                    │
│                                                                 │
│   [🔍 Verify Medicare Eligibility]                              │
│                                                                 │
│   ELIGIBILITY RESULTS                                           │
│   ┌───────────────────────────────────────────────────────┐    │
│   │ ✅ MEDICARE ELIGIBLE                                   │    │
│   │                                                        │    │
│   │ Coverage: Part B - Outpatient Therapy                  │    │
│   │                                                        │    │
│   │ COST SHARING:                                          │    │
│   │ ├── Deductible:     $240.00 (2026)                     │    │
│   │ │   └── Met:        $240.00 ✓                          │    │
│   │ └── Co-Insurance:   20% (patient pays)                 │    │
│   │                                                        │    │
│   │ THERAPY CAP:                                           │    │
│   │ ├── Annual Threshold: $2,330.00 (2026)                 │    │
│   │ └── Used:           $0.00                              │    │
│   │                                                        │    │
│   │ ⚠️ KX Modifier required after threshold reached        │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   ┌───────────────────────────────────────────────────────┐    │
│   │ PATIENT PAYMENT:                                       │    │
│   │ 20% Co-Insurance per visit (after deductible met)      │    │
│   │ Estimated: ~$30-40 per visit                           │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   [← Back]                              [Save & Continue →]     │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Step 1.3E: Self-Pay Details

**When:** Patient selected "Self-Pay"

**What You See:**

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│   SELF-PAY PATIENT                                              │
│                                                                 │
│   This patient will pay out-of-pocket for all services.        │
│   No insurance will be billed.                                  │
│                                                                 │
│   REASON FOR SELF-PAY                                           │
│   ○ No insurance coverage                                       │
│   ○ Insurance doesn't cover physical therapy                    │
│   ○ High deductible - patient prefers cash rate                 │
│   ○ Out-of-network - patient prefers cash rate                  │
│   ○ Other: [_________________________]                          │
│                                                                 │
│   ─────────────────────────────────────────────────────────    │
│                                                                 │
│   CLINIC SELF-PAY RATES                                         │
│   ┌───────────────────────────────────────────────────────┐    │
│   │                                                        │    │
│   │ Initial Evaluation:        $200.00                     │    │
│   │ Follow-Up Visit:           $150.00                     │    │
│   │ Re-Evaluation:             $175.00                     │    │
│   │                                                        │    │
│   │ ℹ️ These rates are set in Clinic Settings              │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   FINANCIAL AGREEMENT                                           │
│   ┌───────────────────────────────────────────────────────┐    │
│   │                                                        │    │
│   │ [✓] Patient understands they are responsible for       │    │
│   │     full payment at time of service                    │    │
│   │                                                        │    │
│   │ [✓] Patient has signed Financial Agreement form        │    │
│   │                                                        │    │
│   │ [📄 Print Financial Agreement]                         │    │
│   │                                                        │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   [← Back]                              [Save & Continue →]     │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Step 1.4: Schedule Initial Evaluation

**Who:** Front Desk Staff

**What You Do:**

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│   SCHEDULE INITIAL EVALUATION                                   │
│                                                                 │
│   Patient: John Smith (New Patient)                             │
│   Insurance: Blue Cross Blue Shield                             │
│                                                                 │
│   APPOINTMENT DETAILS                                           │
│   ├── Type:       Initial Evaluation (60 min)                   │
│   ├── Provider:   [Dr. Sarah Johnson ▼]                         │
│   ├── Location:   [Main Clinic ▼]                               │
│   ├── Date:       [01/30/2026]                                  │
│   └── Time:       [10:00 AM ▼]                                  │
│                                                                 │
│   PAYMENT EXPECTED                                              │
│   ┌───────────────────────────────────────────────────────┐    │
│   │ Co-Pay:              $35.00                            │    │
│   │ Deductible:          $150.00 (est. - $300 remaining)   │    │
│   │ ─────────────────────────────                          │    │
│   │ Estimated Due:       $185.00                           │    │
│   │                                                        │    │
│   │ ⚠️ Collect at check-in                                 │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   [Cancel]                              [Schedule Appointment]  │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

**Patient Registration Complete!**

---

# PART 2: PATIENT CHECK-IN

## Step 2.1: Patient Arrives for Appointment

**Who:** Front Desk Staff

**What You See:**

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│   CHECK-IN                                                      │
│                                                                 │
│   Patient: John Smith                                           │
│   Appointment: 01/30/2026 10:00 AM - Initial Evaluation         │
│   Provider: Dr. Sarah Johnson                                   │
│                                                                 │
│   ═══════════════════════════════════════════════════════════  │
│                                                                 │
│   INSURANCE STATUS                                              │
│   ┌───────────────────────────────────────────────────────┐    │
│   │ Insurance: Blue Cross Blue Shield                      │    │
│   │ Status: ✅ Verified (01/29/2026)                       │    │
│   │ Authorization: ✅ Active                               │    │
│   │   └── Visit 1 of 12 (11 remaining after today)         │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   PAYMENT DUE TODAY                                             │
│   ┌───────────────────────────────────────────────────────┐    │
│   │                                                        │    │
│   │ Co-Pay:                      $35.00                    │    │
│   │ Deductible (remaining):      $150.00                   │    │
│   │ Previous Balance:            $0.00                     │    │
│   │ ─────────────────────────────────────                  │    │
│   │ TOTAL DUE:                   $185.00                   │    │
│   │                                                        │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   COLLECT PAYMENT                                               │
│   ┌───────────────────────────────────────────────────────┐    │
│   │ Amount:        [$185.00        ]                       │    │
│   │ Method:        [Credit Card ▼]                         │    │
│   │                 - Cash                                 │    │
│   │                 - Check                                │    │
│   │                 - Credit Card                          │    │
│   │                 - HSA/FSA Card                         │    │
│   │                                                        │    │
│   │ [Process Payment]                                      │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   [Skip Payment]              [Check-In Without Payment]        │
│                               (Adds to patient balance)         │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Step 2.2: Check-In Scenarios

### Scenario A: Workers' Comp / Personal Injury

```
┌───────────────────────────────────────────────────────────────┐
│                                                               │
│   PAYMENT DUE TODAY                                           │
│   ┌───────────────────────────────────────────────────────┐  │
│   │ Insurance: Workers' Compensation                       │  │
│   │ Authorization: Visit 3 of 12                           │  │
│   │                                                        │  │
│   │ Amount Due:              $0.00                         │  │
│   │                                                        │  │
│   │ ✅ No patient payment required                         │  │
│   │    Workers' Comp covers 100%                           │  │
│   └───────────────────────────────────────────────────────┘  │
│                                                               │
│   [Check In]                                                  │
│                                                               │
└───────────────────────────────────────────────────────────────┘
```

### Scenario B: Private Insurance - Deductible Not Met

```
┌───────────────────────────────────────────────────────────────┐
│                                                               │
│   PAYMENT DUE TODAY                                           │
│   ┌───────────────────────────────────────────────────────┐  │
│   │ Insurance: Blue Cross Blue Shield                      │  │
│   │ Authorization: Visit 2 of 12                           │  │
│   │                                                        │  │
│   │ Co-Pay:                      $35.00                    │  │
│   │ Deductible (remaining):      $150.00                   │  │
│   │ ─────────────────────────────────────                  │  │
│   │ Total Due:                   $185.00                   │  │
│   │                                                        │  │
│   │ ⚠️ Deductible not yet met ($150 of $500 remaining)     │  │
│   │   Patient pays deductible until met                    │  │
│   └───────────────────────────────────────────────────────┘  │
│                                                               │
│   [Collect $185.00 & Check In]                                │
│                                                               │
└───────────────────────────────────────────────────────────────┘
```

### Scenario C: Private Insurance - Deductible Met

```
┌───────────────────────────────────────────────────────────────┐
│                                                               │
│   PAYMENT DUE TODAY                                           │
│   ┌───────────────────────────────────────────────────────┐  │
│   │ Insurance: Blue Cross Blue Shield                      │  │
│   │ Authorization: Visit 5 of 12                           │  │
│   │                                                        │  │
│   │ Co-Pay:                      $35.00                    │  │
│   │ Deductible:                  $0.00 (met ✓)             │  │
│   │ ─────────────────────────────────────                  │  │
│   │ Total Due:                   $35.00                    │  │
│   │                                                        │  │
│   │ ℹ️ Deductible has been met for this year               │  │
│   │   20% co-insurance will be billed after insurance pays │  │
│   └───────────────────────────────────────────────────────┘  │
│                                                               │
│   [Collect $35.00 & Check In]                                 │
│                                                               │
└───────────────────────────────────────────────────────────────┘
```

### Scenario D: Authorization Exhausted

```
┌───────────────────────────────────────────────────────────────┐
│                                                               │
│   ⚠️ AUTHORIZATION ALERT                                      │
│   ┌───────────────────────────────────────────────────────┐  │
│   │                                                        │  │
│   │ ❌ NO AUTHORIZED VISITS REMAINING                      │  │
│   │                                                        │  │
│   │ Insurance: Blue Cross Blue Shield                      │  │
│   │ Authorization: 12 of 12 USED                           │  │
│   │                                                        │  │
│   │ This visit cannot be billed to insurance.              │  │
│   │                                                        │  │
│   └───────────────────────────────────────────────────────┘  │
│                                                               │
│   OPTIONS:                                                    │
│   ┌───────────────────────────────────────────────────────┐  │
│   │                                                        │  │
│   │ ○ Request New Authorization                            │  │
│   │   (Reschedule until authorization received)            │  │
│   │                                                        │  │
│   │ ● Continue as Self-Pay Visit                           │  │
│   │   Patient pays: $150.00 (self-pay rate)                │  │
│   │   ⚠️ This visit will NOT count against any auth        │  │
│   │                                                        │  │
│   └───────────────────────────────────────────────────────┘  │
│                                                               │
│   [Reschedule]    [Collect $150.00 & Check In as Self-Pay]   │
│                                                               │
└───────────────────────────────────────────────────────────────┘
```

### Scenario E: Authorization Expiring Soon

```
┌───────────────────────────────────────────────────────────────┐
│                                                               │
│   ⚠️ AUTHORIZATION ALERT                                      │
│   ┌───────────────────────────────────────────────────────┐  │
│   │                                                        │  │
│   │ Authorization expires in 5 days!                       │  │
│   │                                                        │  │
│   │ Expires: 02/03/2026                                    │  │
│   │ Visits Remaining: 4                                    │  │
│   │                                                        │  │
│   │ 💡 Recommend requesting re-authorization               │  │
│   │                                                        │  │
│   └───────────────────────────────────────────────────────┘  │
│                                                               │
│   [Dismiss & Continue Check-In]                               │
│                                                               │
└───────────────────────────────────────────────────────────────┘
```

### Scenario F: Low Visits Remaining

```
┌───────────────────────────────────────────────────────────────┐
│                                                               │
│   ⚠️ AUTHORIZATION ALERT                                      │
│   ┌───────────────────────────────────────────────────────┐  │
│   │                                                        │  │
│   │ Only 2 authorized visits remaining!                    │  │
│   │                                                        │  │
│   │ Authorization: 10 of 12 used                           │  │
│   │ After today: 1 visit remaining                         │  │
│   │                                                        │  │
│   │ 💡 Recommend requesting re-authorization               │  │
│   │                                                        │  │
│   └───────────────────────────────────────────────────────┘  │
│                                                               │
│   [Dismiss & Continue Check-In]                               │
│                                                               │
└───────────────────────────────────────────────────────────────┘
```

### Scenario G: Self-Pay Patient

```
┌───────────────────────────────────────────────────────────────┐
│                                                               │
│   PAYMENT DUE TODAY                                           │
│   ┌───────────────────────────────────────────────────────┐  │
│   │ Insurance: None (Self-Pay Patient)                     │  │
│   │                                                        │  │
│   │ Visit Type: Follow-Up                                  │  │
│   │ Visit Rate:              $150.00                       │  │
│   │ Previous Balance:        $0.00                         │  │
│   │ ─────────────────────────────────────                  │  │
│   │ Total Due:               $150.00                       │  │
│   │                                                        │  │
│   └───────────────────────────────────────────────────────┘  │
│                                                               │
│   [Collect $150.00 & Check In]                                │
│                                                               │
└───────────────────────────────────────────────────────────────┘
```

### Scenario H: Patient Has Outstanding Balance

```
┌───────────────────────────────────────────────────────────────┐
│                                                               │
│   PAYMENT DUE TODAY                                           │
│   ┌───────────────────────────────────────────────────────┐  │
│   │ Insurance: Blue Cross Blue Shield                      │  │
│   │ Authorization: Visit 6 of 12                           │  │
│   │                                                        │  │
│   │ Co-Pay:                      $35.00                    │  │
│   │ Previous Balance:            $85.00 ⚠️                 │  │
│   │   └── 2 missed co-pays + co-insurance                  │  │
│   │ ─────────────────────────────────────                  │  │
│   │ Total Due:                   $120.00                   │  │
│   │                                                        │  │
│   │ ⚠️ Patient has outstanding balance                     │  │
│   │                                                        │  │
│   └───────────────────────────────────────────────────────┘  │
│                                                               │
│   COLLECT:                                                    │
│   ○ Today's co-pay only ($35.00)                              │
│   ● Full balance ($120.00)                                    │
│   ○ Custom amount: $[________]                                │
│                                                               │
│   [Collect & Check In]                                        │
│                                                               │
└───────────────────────────────────────────────────────────────┘
```

---

# PART 3: PROVIDER WORKFLOW

## Step 3.1: Initial Evaluation

**Who:** Physical Therapist / Provider

**What Happens:**

1. Provider sees patient on their schedule
2. Conducts evaluation
3. Creates **Initial Evaluation Note**
4. Documents:
   - History / Chief Complaint
   - Examination findings
   - Diagnosis (ICD-10 codes)
   - Goals
   - Plan of Care (visits, frequency, duration)
   - Treatment provided today

5. **Signs the Note**

---

## Step 3.2: Care Episode Auto-Created

**When Provider Signs Initial Evaluation:**

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│   ✅ CARE EPISODE CREATED                                       │
│                                                                 │
│   Patient: John Smith                                           │
│   Condition: Low Back Pain                                      │
│                                                                 │
│   DETAILS CAPTURED:                                             │
│   ┌───────────────────────────────────────────────────────┐    │
│   │ Start Date:         01/30/2026                         │    │
│   │ Date of Injury:     01/15/2026 (from patient record)   │    │
│   │ Insurance:          Blue Cross Blue Shield             │    │
│   │ Authorization:      AUTH-2026-12345 (12 visits)        │    │
│   │                                                        │    │
│   │ Primary Diagnosis:  M54.5 - Low back pain              │    │
│   │ Secondary Dx:       M54.16 - Lumbar radiculopathy      │    │
│   │                                                        │    │
│   │ Plan of Care:                                          │    │
│   │ ├── Expected Visits: 12                                │    │
│   │ ├── Frequency:       3x per week                       │    │
│   │ └── Duration:        4 weeks                           │    │
│   │                                                        │    │
│   │ Visit 1 of 12 completed today                          │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   ⚠️ This snapshot is preserved even if patient's insurance    │
│      or injury date changes later                               │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

**Key Point:** The system **snapshots** Date of Injury, Insurance, and Authorization at this moment. If patient returns later with a NEW injury, a NEW Care Episode is created with NEW snapshot.

---

## Step 3.3: Follow-Up Visits

**Each Follow-Up Visit:**

1. Patient checks in (co-pay collected if applicable)
2. Provider treats patient
3. Provider creates **Daily Visit Note**
4. Documents treatment provided
5. Signs the note
6. Visit count updated (2 of 12, 3 of 12, etc.)

---

# PART 4: BILLING & CHARGES

## Step 4.1: Charge Creation (After Each Visit)

**When Note is Signed, Charges Created:**

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│   CHARGES CREATED                                               │
│                                                                 │
│   Patient: John Smith                                           │
│   Date of Service: 01/30/2026                                   │
│   Provider: Dr. Sarah Johnson                                   │
│                                                                 │
│   SERVICES                                                      │
│   ┌───────────────────────────────────────────────────────┐    │
│   │ CPT        Description                Units    Amount  │    │
│   │ ──────────────────────────────────────────────────────│    │
│   │ 97110      Therapeutic Exercise       2        $80.00  │    │
│   │ 97140      Manual Therapy             1        $45.00  │    │
│   │ 97530      Therapeutic Activities     1        $50.00  │    │
│   │ ──────────────────────────────────────────────────────│    │
│   │ TOTAL CHARGES                                  $175.00 │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   DIAGNOSIS CODES                                               │
│   ├── M54.5 - Low back pain                                     │
│   └── M54.16 - Radiculopathy, lumbar region                     │
│                                                                 │
│   BILLING TYPE: Insurance (Blue Cross Blue Shield)              │
│   AUTHORIZATION: AUTH-2026-12345 (Visit 1 of 12)                │
│                                                                 │
│   → Charge ready for claim submission                           │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Step 4.2: Claim Submission

**Billing Staff Reviews and Submits Claims:**

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│   CLAIMS READY TO SUBMIT                                        │
│                                                                 │
│   Filter: [All Payers ▼]  [Last 7 Days ▼]  [Ready ▼]            │
│                                                                 │
│   ┌───────────────────────────────────────────────────────┐    │
│   │ □  Patient         DOS        Payer        Amount     │    │
│   │ ──────────────────────────────────────────────────────│    │
│   │ ✓  John Smith      01/30/26   BCBS         $175.00    │    │
│   │ ✓  Mary Johnson    01/30/26   Aetna        $150.00    │    │
│   │ ✓  Bob Wilson      01/30/26   Medicare     $175.00    │    │
│   │ ✓  Sue Davis       01/29/26   WC-StateFund $200.00    │    │
│   │ ──────────────────────────────────────────────────────│    │
│   │ 4 claims selected                          $700.00    │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   [Submit Selected Claims]                                      │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Step 4.3: Payment Posting (When Insurance Pays)

**When Insurance Sends Payment (ERA/EOB):**

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│   POST INSURANCE PAYMENT                                        │
│                                                                 │
│   Patient: John Smith                                           │
│   Date of Service: 01/30/2026                                   │
│   Payer: Blue Cross Blue Shield                                 │
│                                                                 │
│   ORIGINAL CHARGES                                              │
│   ┌───────────────────────────────────────────────────────┐    │
│   │ CPT        Billed      Allowed     Paid       Adj     │    │
│   │ ──────────────────────────────────────────────────────│    │
│   │ 97110      $80.00      $64.00      $51.20     $16.00  │    │
│   │ 97140      $45.00      $36.00      $28.80     $9.00   │    │
│   │ 97530      $50.00      $40.00      $32.00     $10.00  │    │
│   │ ──────────────────────────────────────────────────────│    │
│   │ TOTAL      $175.00     $140.00     $112.00    $35.00  │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   BREAKDOWN                                                     │
│   ┌───────────────────────────────────────────────────────┐    │
│   │ Billed:                         $175.00                │    │
│   │ Allowed Amount:                 $140.00                │    │
│   │ Contractual Adjustment:         -$35.00 (write-off)    │    │
│   │                                                        │    │
│   │ Insurance Paid (80%):           $112.00                │    │
│   │ Patient Co-Insurance (20%):     $28.00                 │    │
│   │                                                        │    │
│   │ Patient Already Paid (co-pay):  $35.00                 │    │
│   │ Patient Owes (co-insurance):    $28.00  ← ADD TO BAL   │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   [Post Payment]                                                │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Step 4.4: Secondary Insurance (If Applicable)

**If Patient Has Secondary Insurance:**

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│   SECONDARY INSURANCE BILLING                                   │
│                                                                 │
│   Patient: John Smith                                           │
│   Primary: Blue Cross Blue Shield - PAID                        │
│   Secondary: AARP Medicare Supplement                           │
│                                                                 │
│   PRIMARY PAYMENT SUMMARY                                       │
│   ┌───────────────────────────────────────────────────────┐    │
│   │ Allowed:                        $140.00                │    │
│   │ Primary Paid:                   $112.00                │    │
│   │ Patient Responsibility:         $28.00 (co-insurance)  │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   SUBMIT TO SECONDARY?                                          │
│   ┌───────────────────────────────────────────────────────┐    │
│   │                                                        │    │
│   │ Secondary insurance may cover the remaining $28.00     │    │
│   │                                                        │    │
│   │ [Submit to Secondary]    [Bill Patient Instead]        │    │
│   │                                                        │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

# PART 5: PATIENT BALANCE & STATEMENTS

## Step 5.1: Patient Account View

**Front Desk or Billing Staff Views Patient Account:**

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│   PATIENT ACCOUNT                                               │
│                                                                 │
│   Patient: John Smith                                           │
│   DOB: 05/15/1980                                               │
│   Phone: (555) 123-4567                                         │
│                                                                 │
│   BALANCE SUMMARY                                               │
│   ┌───────────────────────────────────────────────────────┐    │
│   │                                                        │    │
│   │   Total Charges:                 $1,575.00             │    │
│   │   Insurance Payments:            -$1,008.00            │    │
│   │   Adjustments:                   -$315.00              │    │
│   │   Patient Payments:              -$175.00              │    │
│   │   ──────────────────────────────────────               │    │
│   │   BALANCE DUE:                   $77.00                │    │
│   │                                                        │    │
│   │   [Make Payment]  [Payment Plan]  [Send Statement]     │    │
│   │                                                        │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   BALANCE AGING                                                 │
│   ┌───────────────────────────────────────────────────────┐    │
│   │ Current    30 Days    60 Days    90 Days    120+ Days  │    │
│   │ $28.00     $21.00     $28.00     $0.00      $0.00      │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   RECENT ACTIVITY                                               │
│   ┌───────────────────────────────────────────────────────┐    │
│   │ Date        Description               Charge   Payment │    │
│   │ ──────────────────────────────────────────────────────│    │
│   │ 02/15/26    Follow-Up Visit           $175.00          │    │
│   │ 02/15/26    Co-Pay Collected                   -$35.00 │    │
│   │ 02/18/26    BCBS Payment                      -$112.00 │    │
│   │ 02/18/26    Contractual Adjustment    -$35.00          │    │
│   │ 02/18/26    Patient Co-Insurance       $28.00  → BAL   │    │
│   │ ──────────────────────────────────────────────────────│    │
│   │ 02/12/26    Follow-Up Visit           $175.00          │    │
│   │ 02/12/26    Co-Pay Collected                   -$35.00 │    │
│   │ ...                                                    │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   CARE EPISODES                                                 │
│   ┌───────────────────────────────────────────────────────┐    │
│   │ Episode              Status      Visits    Balance     │    │
│   │ ──────────────────────────────────────────────────────│    │
│   │ Low Back Pain        Active      9/12      $77.00      │    │
│   │ (01/30/26 - present)                                   │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Step 5.2: Patient Statement

**Generate Statement to Send to Patient:**

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│   ╔═══════════════════════════════════════════════════════════╗│
│   ║           ABC PHYSICAL THERAPY CLINIC                     ║│
│   ║           123 Main Street, City, ST 12345                 ║│
│   ║           Phone: (555) 111-2222                           ║│
│   ╚═══════════════════════════════════════════════════════════╝│
│                                                                 │
│   STATEMENT DATE: February 20, 2026                             │
│   ACCOUNT NUMBER: PAT-10042                                     │
│                                                                 │
│   PATIENT:                                                      │
│   John Smith                                                    │
│   456 Oak Avenue                                                │
│   City, ST 12345                                                │
│                                                                 │
│   ───────────────────────────────────────────────────────────  │
│                                                                 │
│   ACCOUNT SUMMARY                                               │
│   ┌───────────────────────────────────────────────────────┐    │
│   │ Previous Balance:                    $49.00            │    │
│   │ New Charges:                         $28.00            │    │
│   │ Payments Received:                   $0.00             │    │
│   │ ──────────────────────────────────────                 │    │
│   │ AMOUNT DUE:                          $77.00            │    │
│   │                                                        │    │
│   │ PLEASE PAY BY: March 20, 2026                          │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   ACTIVITY DETAIL                                               │
���   ┌───────────────────────────────────────────────────────┐    │
│   │ Date       Service              Insurance   You Owe    │    │
│   │ ──────────────────────────────────────────────────────│    │
│   │ 01/30/26   Initial Evaluation   Paid $112   $28.00*    │    │
│   │ 02/03/26   Follow-Up Visit      Paid $112   $21.00*    │    │
│   │ 02/06/26   Follow-Up Visit      Paid $112   $28.00*    │    │
│   │ ──────────────────────────────────────────────────────│    │
│   │ * Co-insurance (20% of allowed amount)                 │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   PAYMENT OPTIONS                                               │
│   • Pay online: www.abcpt.com/pay                               │
│   • Call us: (555) 111-2222                                     │
│   • Mail check to address above                                 │
│                                                                 │
│   Thank you for choosing ABC Physical Therapy!                  │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘

[Print Statement]  [Email Statement]  [Download PDF]
```

---

# PART 6: PATIENT RETURNS (New Injury)

## Step 6.1: Find Existing Patient

**Front Desk Searches:**

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│   PATIENT SEARCH                                                │
│                                                                 │
│   Search: [John Smith                    ] [🔍]                 │
│                                                                 │
│   RESULTS                                                       │
│   ┌───────────────────────────────────────────────────────┐    │
│   │ ✓ John Smith    DOB: 05/15/1980    (555) 123-4567     │    │
│   │   Last Visit: 03/15/2026                               │    │
│   │   Status: Previous Patient                             │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   [Select Patient]                                              │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Step 6.2: New Visit or Continue Episode?

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│   RETURNING PATIENT                                             │
│                                                                 │
│   Patient: John Smith                                           │
│   DOB: 05/15/1980                                               │
│                                                                 │
│   PREVIOUS CARE HISTORY                                         │
│   ┌───────────────────────────────────────────────────────┐    │
│   │                                                        │    │
│   │ Episode 1: Low Back Pain                               │    │
│   │ ├── Date of Injury: 01/15/2026                         │    │
│   │ ├── Insurance: Workers' Compensation                   │    │
│   │ ├── Visits: 12 of 12 completed                         │    │
│   │ ├── Status: DISCHARGED (03/15/2026)                    │    │
│   │ └── Balance: $0.00                                     │    │
│   │                                                        │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   WHAT BRINGS THE PATIENT IN TODAY?                             │
│   ┌───────────────────────────────────────────────────────┐    │
│   │                                                        │    │
│   │ ○ Continue Previous Episode                            │    │
│   │   Same condition, need more treatment                  │    │
│   │   (Will reopen Low Back Pain episode)                  │    │
│   │                                                        │    │
│   │ ● New Injury / New Condition                           │    │
│   │   Different body part or new incident                  │    │
│   │   (Will create new episode)                            │    │
│   │                                                        │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   [Continue]                                                    │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Step 6.3: New Injury - Enter Details

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│   NEW INJURY / CONDITION                                        │
│                                                                 │
│   Patient: John Smith                                           │
│                                                                 │
│   INJURY DETAILS                                                │
│   ├── What happened?      [Car accident - neck pain    ]        │
│   ├── Date of Injury:     [06/20/2026]                          │
│   └── Body Part:          [Neck / Cervical ▼]                   │
│                                                                 │
│   INSURANCE FOR THIS INJURY                                     │
│   ┌───────────────────────────────────────────────────────┐    │
│   │                                                        │    │
│   │ ○ Use existing insurance on file:                      │    │
│   │   Workers' Compensation (WC-2026-12345)                │    │
│   │                                                        │    │
│   │ ● Different insurance for this injury                  │    │
│   │   (Enter new insurance details)                        │    │
│   │                                                        │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   NEW INSURANCE DETAILS                                         │
│   ├── Type: Personal Injury / LOP                               │
│   ├── Attorney: [Jane Doe, Esq.          ]                      │
│   ├── Law Firm: [Doe & Associates        ]                      │
│   └── Case #:   [PI-2026-9999            ]                      │
│                                                                 │
│   AUTHORIZATION                                                 │
│   ├── LOP Signed: [✓]                                           │
│   ├── Visits Authorized: [15]                                   │
│   └── Valid Until: [12/20/2026]                                 │
│                                                                 │
│   [Cancel]                              [Save & Schedule IE]    │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Step 6.4: Patient Now Has Multiple Episodes

**After New IE is Signed:**

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│   PATIENT: John Smith                                           │
│                                                                 │
│   CARE EPISODES                                                 │
│   ┌───────────────────────────────────────────────────────┐    │
│   │                                                        │    │
│   │ Episode 1: Low Back Pain                               │    │
│   │ ├── Date of Injury: 01/15/2026                         │    │
│   │ ├── Insurance: Workers' Compensation                   │    │
│   │ ├── Authorization: WC-AUTH-123 (12 visits)             │    │
│   │ ├── Visits: 12 of 12 ✓                                 │    │
│   │ ├── Status: DISCHARGED                                 │    │
│   │ └── Balance: $0.00                                     │    │
│   │                                                        │    │
│   │ ──────────────────────────────────────────────────────│    │
│   │                                                        │    │
│   │ Episode 2: Neck Pain (Cervical)              ← NEW     │    │
│   │ ├── Date of Injury: 06/20/2026                         │    │
│   │ ├── Insurance: Personal Injury / LOP                   │    │
│   │ ├── Authorization: LOP-2026-9999 (15 visits)           │    │
│   │ ├── Visits: 1 of 15                                    │    │
│   │ ├── Status: ACTIVE                                     │    │
│   │ └── Balance: $0.00                                     │    │
│   │                                                        │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   ✅ All history preserved                                      │
│   ✅ Each episode has own injury date, insurance, authorization │
│   ✅ Visits tracked separately per episode                      │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

# PART 7: DASHBOARD & ALERTS

## Step 7.1: Daily Dashboard

**What Clinic Staff Sees:**

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│   CLINIC DASHBOARD                          February 25, 2026   │
│                                                                 │
│   TODAY'S SCHEDULE                                              │
│   ┌───────────────────────────────────────────────────────┐    │
│   │ Time     Patient          Type        Provider   Pay   │    │
│   │ ─────────────────────────────────────────────────────│    │
│   │ 9:00 AM  John Smith       Follow-Up   Dr. Johnson      │    │
│   │          ✅ Insurance     (Visit 8/15)         $0      │    │
│   │                                                        │    │
│   │ 9:45 AM  Mary Johnson     Follow-Up   Dr. Johnson      │    │
│   │          ⚠️ 2 visits left  (Visit 10/12)       $35     │    │
│   │                                                        │    │
│   │ 10:30 AM Bob Wilson       IE          Dr. Patel        │    │
│   │          ✅ Insurance     (Visit 1/20)         $40     │    │
│   │                                                        │    │
│   │ 11:15 AM Sue Davis        Follow-Up   Dr. Patel        │    │
│   │          💰 Self-Pay                           $150    │    │
│   │                                                        │    │
│   │ 1:00 PM  Tom Brown        Follow-Up   Dr. Johnson      │    │
│   │          ❌ Auth Expired!                      ???     │    │
│   │                                                        │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   ═══════════════════════════════════════════════════════════  │
│                                                                 │
│   ALERTS                                                        │
│   ┌───────────────────────────────────────────────────────┐    │
│   │                                                        │    │
│   │ ⚠️ AUTHORIZATION ALERTS                                │    │
│   │ ├── Mary Johnson: Only 2 visits remaining              │    │
│   │ ├── Tom Brown: Authorization EXPIRED 02/20/2026        │    │
│   │ └── Lisa White: Authorization expires in 5 days        │    │
│   │                                                        │    │
│   │ 💰 BALANCE ALERTS                                      │    │
│   │ ├── 8 patients with balance > 90 days ($1,240 total)   │    │
│   │ └── 3 patients with balance > $200                     │    │
│   │                                                        │    │
│   │ 📋 PENDING TASKS                                       │    │
│   │ ├── 5 claims ready to submit                           │    │
│   │ ├── 3 insurance payments to post                       │    │
│   │ └── 2 authorizations need renewal                      │    │
│   │                                                        │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   QUICK STATS                                                   │
│   ┌───────────────────────────────────────────────────────┐    │
│   │                                                        │    │
│   │ Today's Appointments:     18                           │    │
│   │ Checked In:               5                            │    │
│   │ Completed:                2                            │    │
│   │ No-Shows:                 0                            │    │
│   │                                                        │    │
│   │ Expected Collections:     $1,250                       │    │
│   │ Collected So Far:         $350                         │    │
│   │                                                        │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

# PART 8: CLINIC SETTINGS

## Step 8.1: Admin Configures Clinic

**Clinic Administrator Sets Up:**

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│   CLINIC SETTINGS                                               │
│                                                                 │
│   SELF-PAY RATES                                                │
│   ┌───────────────────────────────────────────────────────┐    │
│   │ Initial Evaluation:            $[200.00]               │    │
│   │ Follow-Up Visit:               $[150.00]               │    │
│   │ Re-Evaluation:                 $[175.00]               │    │
│   │ Dry Needling (add-on):         $[50.00]                │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   NO-SHOW / CANCELLATION FEES                                   │
│   ┌───────────────────────────────────────────────────────┐    │
│   │ [✓] Charge no-show fee                                 │    │
│   │ No-Show Fee:                   $[50.00]                │    │
│   │ Late Cancel Fee (< 24 hrs):    $[25.00]                │    │
│   │                                                        │    │
│   │ [ ] Waive first no-show                                │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   AUTHORIZATION ALERTS                                          │
│   ┌───────────────────────────────────────────────────────┐    │
│   │ Alert when visits remaining ≤        [3] visits        │    │
│   │ Alert when auth expires within       [14] days         │    │
│   │ Alert when deductible remaining >    [$100]            │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   PAYMENT POLICIES                                              │
│   ┌───────────────────────────────────────────────────────┐    │
│   │ [✓] Require co-pay collection at check-in              │    │
│   │ [✓] Require self-pay collection at check-in            │    │
│   │ [✓] Show balance alert if patient owes > $[100]        │    │
│   │ [ ] Block scheduling if balance > $[500]               │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   STATEMENT SETTINGS                                            │
│   ┌───────────────────────────────────────────────────────┐    │
│   │ Send statements every:           [30] days             │    │
│   │ Payment due within:              [30] days             │    │
│   │ Include message: [Thank you for choosing us!     ]     │    │
│   └───────────────────────────────────────────────────────┘    │
│                                                                 │
│   [Save Settings]                                               │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

# SUMMARY: Quick Reference

## Who Does What?

| Task | Who | When |
|------|-----|------|
| Register new patient | Front Desk | Patient's first visit |
| Enter insurance info | Front Desk | Registration |
| Verify insurance | Front Desk | Registration |
| Enter authorization | Front Desk | After getting auth from payer |
| Schedule appointments | Front Desk | After registration, ongoing |
| Check-in patient | Front Desk | Each visit |
| Collect payment | Front Desk | Check-in |
| Create clinical notes | Provider | During/after visit |
| Sign notes | Provider | After visit |
| Review charges | Billing Staff | After notes signed |
| Submit claims | Billing Staff | Daily/weekly |
| Post payments | Billing Staff | When ERA received |
| Send statements | Billing Staff | Monthly |
| Configure settings | Clinic Admin | Initial setup, as needed |

## Payment Quick Reference

| Insurance Type | Per Visit (Authorized) | When Auth Exhausted |
|----------------|------------------------|---------------------|
| Workers' Comp | $0 | Self-Pay Rate |
| Personal Injury / LOP | $0 | Self-Pay Rate |
| Private Insurance | Co-Pay + Deductible + Co-Insurance | Self-Pay Rate |
| Medicare | 20% Co-Insurance | Self-Pay Rate |
| Self-Pay | Full Rate | N/A |

## Visit Counting Rules

| Visit Status | Counts Against Authorization? |
|--------------|------------------------------|
| Completed (Insurance) | ✅ YES |
| Completed (Self-Pay) | ❌ NO |
| No-Show | ❌ NO |
| Cancelled | ❌ NO |
| Scheduled | ⏳ Will count when completed |

---

**End of Workflow Guide**
