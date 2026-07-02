# Care Episode and Insurance Workflow Documentation

## Overview

This document describes the architecture and workflow for Care Episodes and Insurance management in the PT EHR system. These features are core to managing patient treatment plans, insurance billing, and authorization tracking.

---

## Table of Contents

1. [Care Episode](#care-episode)
2. [Insurance Management](#insurance-management)
3. [Billing Integration](#billing-integration)
4. [Data Flow](#data-flow)
5. [API Endpoints](#api-endpoints)
6. [Frontend Components](#frontend-components)

---

## Care Episode

### Purpose

A Care Episode represents a continuous period of treatment for a patient with a specific diagnosis. It tracks:
- Treatment duration (start/end dates)
- Primary diagnosis and secondary diagnoses
- Assigned provider
- Insurance authorization details
- Treatment plan and goals
- Visit tracking

### Data Model

```
CareEpisode
├── CareEpisodeId (PK)
├── PatientId (FK)
├── TenantId (FK)
├── StartDate
├── EndDate (nullable - set on discharge)
├── PrimaryProviderId (FK)
├── InsuranceId (FK)
├── PrimaryDiagnosisCode (e.g., "M54.5")
├── PrimaryDiagnosisDescription
├── SecondaryDiagnoses (JSON array)
├── AuthorizationNumber
├── AuthorizedVisits
├── AuthorizationExpiry
├── ExpectedVisits
├── VisitFrequency (visits per week)
├── Goals (JSON array)
├── PlanOfCare
├── VisitsUsed (calculated from appointments)
├── Status (0=Active, 1=Discharged, 2=OnHold)
├── DischargeReason
└── Timestamps (CreatedAt, UpdatedAt)
```

### Care Episode Workflow

1. **Creation**:
   - Opened from Appointment Detail modal or Patient Detail
   - Requires: Patient selection, Start Date, Primary Diagnosis Code
   - Optional: Provider, Insurance, Authorization details, Treatment Plan

2. **Management**:
   - Visits are automatically tracked via linked appointments
   - Authorization status is calculated from AuthorizedVisits vs VisitsUsed
   - Expiry warnings when approaching AuthorizationExpiry

3. **Discharge**:
   - Set EndDate and DischargeReason
   - Status changes to "Discharged"
   - Discharge Reasons: Goals Met, Patient Request, Insurance Exhausted, Non-Compliance, Transferred, Other

### Frontend Location

- **Modal**: `index.html` lines 2138-2245 (`#careEpisodeModal`)
- **JavaScript**: `app.js` - `openCareEpisodeModal()`, `handleCareEpisodeSubmit()`, `loadCareEpisodeForEdit()`

---

## Insurance Management

### Purpose

Insurance records track patient coverage, authorization details, and eligibility status. The system supports multiple insurance types per patient.

### Insurance Types

| Type | Value | Description |
|------|-------|-------------|
| Primary | 0 | Main insurance coverage |
| Secondary | 1 | Secondary/supplemental coverage |
| Tertiary | 2 | Tertiary coverage (if applicable) |

### Data Model

```
Insurance
├── InsuranceId (PK)
├── PatientId (FK)
├── TenantId (FK)
├── Type (0=Primary, 1=Secondary, 2=Tertiary)
├── PayerName
├── PayerId (Payer ID for EDI)
├── PolicyNumber
├── GroupNumber
├── Subscriber Information
│   ├── SubscriberName
│   ├── SubscriberId
│   ├── SubscriberDob
│   └── SubscriberRelationship
├── Financial
│   ├── Copay
│   ├── Coinsurance
│   ├── Deductible
│   ├── DeductibleMet
│   └── DeductibleRemaining
├── Authorization
│   ├── AuthorizationNumber
│   ├── AuthorizedVisits
│   ├── VisitsUsed
│   └── AuthorizationExpiry
├── Coverage Period
│   ├── EffectiveFrom
│   └── EffectiveTo
├── Status
│   ├── EligibilityStatus
│   ├── LastVerifiedAt
│   └── IsActive
└── Timestamps
```

### Insurance Workflow

1. **Adding Insurance (via Patient Form)**:
   - Primary Insurance: Added in Insurance tab of Patient modal
   - Secondary Insurance: Added in same tab below Primary Insurance card
   - Both are saved when patient form is submitted

2. **Standalone Insurance Management**:
   - API endpoint: `POST /api/insurance/patient/{patientId}`
   - Used for adding additional insurances outside patient form

3. **Eligibility Verification**:
   - API endpoint: `POST /api/insurance/{id}/verify`
   - Updates EligibilityStatus and LastVerifiedAt
   - Statuses: Pending (0), Active (1), Inactive (2), Expired (3)

### Frontend Location

- **Patient Form Insurance Tab**: `index.html` lines 956-1098
- **Primary Insurance Card**: Lines 956-1031
- **Secondary Insurance Card**: Lines 1035-1098
- **JavaScript**: `app.js` - `handlePatientForm()`, `editPatient()`

---

## Billing Integration

### Charges

Charges are created from appointments/visits and linked to Care Episodes and Insurance.

```
Charge
├── ChargeId (PK)
├── PatientId (FK)
├── AppointmentId (FK, optional)
├── CareEpisodeId (FK, optional)
├── CPTCode
├── Units
├── ChargeAmount
├── ServiceDate
├── Status (0=Unbilled, 1=Billed, 2=Paid, 3=Denied, 4=Adjusted)
└── Timestamps
```

### Claims

Claims bundle charges for submission to insurance payers.

```
BillingClaim
├── ClaimId (PK)
├── PatientId (FK)
├── InsuranceId (FK)
├── ClaimNumber (auto-generated)
├── ServiceDateFrom
├── ServiceDateTo
├── TotalCharged
├── TotalPaid
├── Status (0=Draft, 1=Submitted, 2=Paid, 3=Denied, 4=Appealed)
└── Timestamps
```

### New Claim Workflow

1. Click "New Claim" button in Billing page
2. Search and select a patient (autocomplete search)
3. Select insurance from patient's active insurances
4. Set service date
5. Select unbilled charges to include
6. Submit claim

### Frontend Location

- **Billing Page**: `index.html` lines 478-577 (`#billingPage`)
- **New Claim Modal**: `index.html` lines 2089-2136 (`#newClaimModal`)
- **JavaScript**: `app.js` - `loadBillingData()`, `loadCharges()`, `loadClaims()`, `handleNewClaimForm()`

---

## Data Flow

### Patient Registration with Insurance

```
┌─────────────┐     ┌──────────────────┐     ┌─────────────────┐
│   Frontend  │ --> │  PatientsController  │ --> │  PatientService │
│  (Patient   │     │  POST /api/patients  │     │  CreatePatient  │
│   Form)     │     └──────────────────┘     │  + Insurance    │
└─────────────┘                              └─────────────────┘
                                                      │
                                                      v
                                             ┌─────────────────┐
                                             │   Database      │
                                             │  - Patients     │
                                             │  - Insurances   │
                                             └─────────────────┘
```

### Care Episode Creation

```
┌─────────────┐     ┌────────────────────┐     ┌──────────────────┐
│   Frontend  │ --> │ CareEpisodesController │ --> │ CareEpisodeService │
│  (Care      │     │ POST /api/careEpisodes │     │ CreateCareEpisode  │
│   Episode   │     └────────────────────┘     └──────────────────┘
│   Modal)    │                                          │
└─────────────┘                                          v
                                                ┌─────────────────┐
                                                │    Database     │
                                                │  - CareEpisodes │
                                                └─────────────────┘
```

### Claim Creation

```
┌─────────────┐     ┌───────────────────┐     ┌────────────────┐
│   Frontend  │ --> │ BillingController │ --> │ BillingService │
│  (New Claim │     │ POST /api/billing │     │ CreateClaim    │
│   Modal)    │     │     /claims       │     └────────────────┘
└─────────────┘     └───────────────────┘              │
                                                       v
                                              ┌─────────────────┐
                                              │    Database     │
                                              │ - BillingClaims │
                                              │ - Charges       │
                                              │   (updated)     │
                                              └─────────────────┘
```

---

## API Endpoints

### Care Episodes

| Method | Endpoint | Description | Auth Roles |
|--------|----------|-------------|------------|
| GET | `/api/careEpisodes` | List care episodes | All authenticated |
| GET | `/api/careEpisodes/{id}` | Get episode details | All authenticated |
| POST | `/api/careEpisodes` | Create care episode | SuperAdmin, Admin, Clinician |
| PUT | `/api/careEpisodes/{id}` | Update care episode | SuperAdmin, Admin, Clinician |

### Insurance

| Method | Endpoint | Description | Auth Roles |
|--------|----------|-------------|------------|
| GET | `/api/insurance/patient/{patientId}` | List patient insurances | All authenticated |
| POST | `/api/insurance/patient/{patientId}` | Add insurance | SuperAdmin, Admin, FrontDesk, Biller |
| PUT | `/api/insurance/{id}` | Update insurance | SuperAdmin, Admin, FrontDesk, Biller |
| POST | `/api/insurance/{id}/verify` | Verify eligibility | SuperAdmin, Admin, FrontDesk, Biller |

### Billing

| Method | Endpoint | Description | Auth Roles |
|--------|----------|-------------|------------|
| GET | `/api/billing/charges` | List charges | All authenticated |
| POST | `/api/billing/charges` | Create charge | SuperAdmin, Admin, Biller, Clinician |
| GET | `/api/billing/claims` | List claims | All authenticated |
| POST | `/api/billing/claims` | Create claim | SuperAdmin, Admin, Biller |
| POST | `/api/billing/claims/{id}/submit` | Submit claim | SuperAdmin, Admin, Biller |
| GET | `/api/billing/ar-aging` | Get AR aging | All authenticated |

---

## Frontend Components

### Care Episode Modal (`#careEpisodeModal`)

**Fields:**
- Patient Info (display only)
- Start Date (required)
- Primary Provider (dropdown - loaded from `/providers/dropdown`)
- Primary Diagnosis Code (required)
- Diagnosis Description
- Insurance (dropdown - loaded from patient's insurances)

**Authorization Section:**
- Authorization Number
- Authorized Visits
- Authorization Expiry

**Treatment Plan Section:**
- Expected Visits
- Frequency (visits/week)
- Goals (textarea)
- Plan of Care (textarea)

**Discharge Section (edit mode only):**
- End Date
- Discharge Reason (dropdown)

### Patient Form Insurance Tab

**Primary Insurance Card:**
- Payer Name, Payer ID
- Policy Number, Group Number
- Copay
- Subscriber Info (Name, ID, DOB, Relationship)
- Effective From/To
- Authorization fields (Number, Visits, Expiry)

**Secondary Insurance Card:**
- Same fields as Primary Insurance
- Saved separately with Type = 1 (Secondary)

### New Claim Modal (`#newClaimModal`)

**Fields:**
- Patient Search (autocomplete)
- Insurance (dropdown - populated after patient selection)
- Service Date From
- Charges List (checkboxes for unbilled charges)

---

## Best Practices

1. **Always verify insurance eligibility** before creating claims
2. **Track authorization usage** - system calculates VisitsUsed automatically
3. **Set authorization expiry alerts** - check AuthorizationExpiry regularly
4. **Use Primary Insurance for billing** unless exhausted, then use Secondary
5. **Complete Care Episode with discharge** when treatment ends

---

## Troubleshooting

### Provider dropdown not loading in Care Episode modal
- Check network tab for `/api/providers/dropdown` response
- Ensure user is authenticated
- Verify providers exist and are active

### Insurance not saving
- Check that PayerName is provided (required field)
- Verify PolicyNumber format
- Check browser console for API errors

### Charges not appearing in New Claim modal
- Ensure charges exist for the selected patient
- Check that charges have Status = 0 (Unbilled)
- Verify patient ID is correctly passed to the API

### 500 error on Billing page
- Check server logs for specific error
- Verify database connection
- Ensure all required services are registered in DI container
