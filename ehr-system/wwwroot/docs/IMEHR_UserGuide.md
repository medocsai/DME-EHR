# IMEHR User Guide

> This document serves as the knowledge source for the MEDOCS AI help assistant and as the official user documentation for IMEHR — an Internal Medicine Electronic Health Records system.

## 1. Getting Started

### Logging In
1. Open the IMEHR application in your web browser
2. If your clinic uses multi-tenant mode, you will first see a **Clinic Selection** screen — choose your clinic from the list
3. Enter your **email address** and **password**
4. Click **Sign In**
5. If two-factor authentication (OTP) is enabled, enter the verification code sent to your email or phone
6. After successful login you will land on the **Dashboard**

### Forgot Password
1. On the login screen, click **Forgot Password?**
2. Enter the email associated with your account
3. Check your email for a password reset link
4. Click the link and set a new password (must meet complexity requirements)

### Changing Your Password
1. Click your **user avatar** in the bottom-left sidebar
2. Click the **key icon** button
3. Enter your current password, then your new password twice
4. Click **Change Password**

### Navigation Overview
The left sidebar contains all major sections of the application:
- **Dashboard** — Overview of today's schedule, alerts, and quick actions
- **Schedule** — Full calendar with appointment management
- **Patients** — Patient search, demographics, and clinical data
- **Providers** — Provider directory (Admin only)
- **Clinical Notes** — Browse and manage all clinical notes
- **E-Prescribe** — Electronic prescriptions
- **Orders** — Lab orders, imaging, and referrals
- **Provider Availability** — Time-off and schedule management
- **Reports** — Analytics and data exports (Admin only)
- **Note Templates** — Manage clinical note templates (Super Admin)
- **Lien Templates** — Medical lien document templates (Super Admin)
- **User Management** — Staff accounts and roles (Admin only)
- **Locations** — Multi-location management (Admin only)
- **Clinics** — Clinic/tenant management (Super Admin only)
- **Settings** — Clinic configuration (Admin only)
- **Consent Forms** — Patient consent template management (Admin only)
- **Documentation** — This help documentation

### Global Patient Search
Press **Ctrl+K** (or Cmd+K on Mac) from any page to open the global patient search. Type a patient's name, date of birth, or phone number to quickly find and navigate to their profile.

### Location Switcher
If your clinic has multiple locations, use the **Location Switcher** in the sidebar (below the clinic name) to switch between locations. Your calendar and patient views will update to reflect the selected location.

---

## 2. Quick Start by Role — What Should I Do First?

When you log in for the first time, your responsibilities depend on your role. This section tells you exactly what you need to do.

### If You Are a Clinic Admin

You are responsible for setting up your clinic's staff and operations. Follow these steps in order:

1. **Verify Clinic Settings** — Your clinic information (name, address, phone, fax, NPI, Tax ID, logo) is configured by the **Super Admin**. If any of this information needs to be updated, contact **contact@medocs.ai**.
2. **Create Locations** — If your clinic has multiple offices, go to **Locations** and add each one with its address and contact details. Set one as the default.
3. **Create Providers** — Go to **Providers** and add each clinician (doctor). Enter their name, specialty, NPI number, and assign a color for the calendar. Each provider must also have a user account (next step).
4. **Create User Accounts** — Go to **User Management** and create accounts for all staff:
   - Clinicians (doctors) — Role: **Clinician**
   - Medical Assistants — Role: **Medical Assistant**
   - Nurses — Role: **Nurse**
   - Front Desk staff — Role: **Front Desk**
   - Billers — Role: **Biller**
   - Each user receives an email invitation to set their password.
5. **Set up Consent Forms** — Go to **Consent Forms** and create templates patients will sign (e.g., HIPAA Consent, General Consent to Treat). Activate the ones you want to use.
6. **Configure Appointment Types** — Your schedule is now ready for bookings.

After these steps, your clinic is operational. Staff can log in and begin their daily work.

### If You Are a Front Desk Staff

Your daily responsibilities:

1. **Add New Patients** — Go to **Patients** > **New Patient**. Enter demographics (name, DOB, gender, contact info) and insurance details. You can also invite patients via the Patient Portal.
2. **Schedule Appointments** — Go to **Schedule** and click on an empty time slot or use **+ New Appointment**. Select the patient, provider, appointment type, date/time, and location.
3. **Check In Patients** — When a patient arrives, click their appointment on the calendar and select **Check In**. If consent forms are required, the patient signs them via the Kiosk or in-office.
4. **Schedule Follow-ups** — After the visit, schedule the patient's next appointment.

### If You Are a Medical Assistant or Nurse

When a patient is checked in and ready to be seen:

1. **Start the Visit** — From the **Schedule**, click the checked-in appointment and select **Start Encounter**. This opens the Encounter Workspace.
2. **Record Vitals** — In the Encounter Workspace, enter the patient's vital signs: Blood Pressure, Heart Rate, Temperature, Respiratory Rate, SpO2, Height, Weight. BMI is calculated automatically.
3. **Enter History** — Document the patient's relevant medical history updates.
4. **Enter Chief Complaint & HPI** — Record why the patient is here today (Chief Complaint) and the History of Present Illness (HPI) with details about symptoms, onset, duration, etc.
5. **Notify the Clinician** — The patient is now ready for the clinician to continue the visit.

**What you cannot do:** Create or edit clinical notes, sign notes, write prescriptions, or create orders. These are clinician-only actions.

### If You Are a Clinician

You have full clinical access. Your daily workflow:

1. **Review the Schedule** — Check your **Dashboard** or **Schedule** to see today's appointments.
2. **Open Encounters** — Click on a patient's appointment (already started by MA/Nurse) to open the Encounter Workspace. You can also start encounters yourself.
3. **Review & Complete the Clinical Visit:**
   - Review the vitals and CC/HPI entered by MA/Nurse
   - Complete the **Review of Systems (ROS)**
   - Perform and document the **Physical Examination**
   - Document the **Assessment & Plan** with diagnoses (ICD-10 codes)
4. **Write Prescriptions** — Use **E-Prescribe** from within the encounter to send electronic prescriptions to the patient's pharmacy.
5. **Create Orders** — Order labs, imaging, or referrals from the encounter. Select tests from the catalog, set priority, and add clinical notes.
6. **Create & Sign the Clinical Note** — Generate or write the clinical note for the visit. Review it, then **Sign** to finalize. Signing locks the note.
7. **Close the Encounter** — Once documentation is complete, close the encounter. MA/Nurse can also close encounters on your behalf.

### If You Are a Biller

1. **Review Clinical Notes** — Access signed clinical notes to extract billing codes (ICD-10, CPT).
2. **Review Insurance** — Check patient insurance details and authorization status.
3. **Run Financial Reports** — Use **Reports** to track revenue and collections.

### If You Are Read Only

You have view-only access to the system. You can browse patients, notes, and reports but cannot create, edit, or delete any records.

---

## 3. Complete Clinic Workflow — From Setup to Patient Visit

This section describes the entire flow of a patient visit from start to finish, showing how each role contributes.

```
CLINIC SETUP (One-Time)
═══════════════════════════════════════
Super Admin configures clinic info, logo, NPI, Tax ID
    │
Clinic Admin logs in
    │
    ├─► Locations: Add clinic locations
    ├─► Providers: Create provider profiles
    ├─► User Management: Create staff accounts (Clinician, MA, Nurse, Front Desk, Biller)
    └─► Consent Forms: Create and activate consent templates

DAILY OPERATIONS
═══════════════════════════════════════

Step 1: PATIENT REGISTRATION (Front Desk)
──────────────────────────────────────────
Front Desk creates patient record
    ├─► Enter demographics (name, DOB, gender, contact)
    ├─► Enter insurance information
    └─► Schedule appointment (select provider, type, date/time)

Step 2: PATIENT ARRIVAL & CHECK-IN (Front Desk)
────────────────────────────────────────────────
Patient arrives at the clinic
    ├─► Front Desk clicks "Check In" on the appointment
    ├─► Patient signs consent forms (Kiosk or manual)
    └─► Appointment status changes to "Checked In" (yellow)

Step 3: START VISIT & INTAKE (MA/Nurse)
───────────────────────────────────────
MA or Nurse starts the encounter
    ├─► Click "Start Encounter" on the checked-in appointment
    ├─► Record Vitals: BP, HR, Temp, RR, SpO2, Height, Weight
    ├─► Enter Chief Complaint (reason for visit)
    ├─► Enter HPI (History of Present Illness)
    └─► Patient is ready for the clinician

Step 4: CLINICAL VISIT (Clinician)
──────────────────────────────────
Clinician opens the encounter
    ├─► Review vitals and CC/HPI from MA/Nurse
    ├─► Complete Review of Systems (ROS)
    ├─► Perform Physical Examination
    ├─► Document Assessment & Plan (diagnoses + treatment plan)
    ├─► Write Prescriptions (E-Prescribe → Pharmacy)
    ├─► Create Orders (Labs, Imaging, Referrals)
    ├─► Create and Sign Clinical Note
    └─► Close Encounter

Step 5: FOLLOW-UP (Front Desk)
──────────────────────────────
    └─► Schedule the patient's next appointment
```

### Role Summary Table

| Action | Clinic Admin | Front Desk | MA/Nurse | Clinician | Biller |
|--------|:---:|:---:|:---:|:---:|:---:|
| Configure clinic settings | — (Super Admin only) | — | — | — | — |
| Create providers | Yes | — | — | — | — |
| Create user accounts | Yes | — | — | — | — |
| Create/edit patients | Yes | Yes | — | Yes | — |
| Schedule appointments | Yes | Yes | — | Yes | — |
| Check in patients | Yes | Yes | — | Yes | — |
| Start encounter | — | — | Yes | Yes | — |
| Enter vitals | — | — | Yes | Yes | — |
| Enter CC/HPI | — | — | Yes | Yes | — |
| Complete ROS & exam | — | — | — | Yes | — |
| Write prescriptions | — | — | — | Yes | — |
| Create orders | — | — | — | Yes | — |
| Create/sign clinical notes | — | — | — | Yes | — |
| Close encounter | — | — | Yes | Yes | — |
| View billing/insurance | Yes | — | — | — | Yes |
| Run reports | Yes | — | — | — | Yes |

---

## 4. Dashboard

The Dashboard is your home screen and provides an at-a-glance overview of your day.

### Summary Cards
- **Today's Appointments** — Count of scheduled appointments for today
- **Checked In** — Patients who have arrived and checked in
- **In Progress** — Encounters currently in progress
- **Completed** — Appointments completed today

### Alerts & Notifications
The dashboard displays important alerts including:
- Unsigned clinical notes requiring attention
- Pending lab results
- Upcoming appointment reminders
- System notifications

### Quick Actions
- **Quick Add Appointment** — Available via the "+" button in the top bar (not available for Billers or MA/Nurse roles)
- **Today's Schedule** — Direct link to the calendar filtered to today

---

## 5. Patients

### Patient Search
1. Navigate to **Patients** from the sidebar
2. Use the search bar to find patients by name, date of birth, phone number, or MRN
3. Results appear in a sortable, paginated table
4. Click any patient row to open their full profile

### Creating a New Patient
1. Click **New Patient** on the Patients page
2. Fill in required demographics: First Name, Last Name, Date of Birth, Gender
3. Add contact information: Phone, Email, Address
4. Add insurance information if available
5. Click **Save** to create the patient record

### Patient Profile
The patient profile displays comprehensive information organized in sections:

#### Demographics Tab
- Personal information (name, DOB, gender, SSN, MRN)
- Contact details (phone, email, address)
- Emergency contact information
- Preferred pharmacy

#### Insurance Tab
- Primary and secondary insurance details
- Insurance ID, group number, subscriber info
- Authorization tracking
- Insurance verification status

#### Clinical Tab
- **Problem List** — Active diagnoses and conditions (ICD-10 codes)
- **Allergies** — Drug, food, and environmental allergies with severity
- **Medications** — Current medication list with dosage and frequency
- **Vitals** — Historical vital sign recordings
- **Immunizations** — Vaccination records
- **Family History** — Hereditary conditions and family medical history
- **Social History** — Smoking status, alcohol use, occupation, etc.

#### Documents Tab
- Uploaded patient documents (lab reports, referral letters, imaging)
- Document categorization and date tracking
- Upload new documents with drag-and-drop support

#### Appointments & Notes Tab
- History of all appointments for this patient
- Associated clinical notes
- Quick access to create new appointments

#### Sticky Notes
- Quick notes attached to a patient's chart
- Visible to all staff for important reminders
- Color-coded for priority

---

## 6. Schedule & Calendar

### Viewing the Schedule
1. Navigate to **Schedule** from the sidebar
2. The calendar displays in **Week view** by default
3. Switch between **Day**, **Week**, and **Month** views using the buttons at the top
4. Use the date navigation arrows to move forward or backward
5. Filter by provider using the **Provider Dropdown** at the top

### Appointment Color Coding
Appointments are color-coded by status:
- **Blue** — Scheduled (confirmed)
- **Yellow** — Checked In (patient arrived)
- **Green** — In Progress (encounter started)
- **Gray** — Completed
- **Red** — Cancelled or No-Show

### Creating an Appointment
1. Click on an empty time slot in the calendar, or click the **+ New Appointment** button
2. Select or search for the **Patient**
3. Choose the **Provider**
4. Select the **Appointment Type** (e.g., New Patient Visit, Follow-Up, Annual Physical, Urgent Visit)
5. Set the **Date and Time**
6. Select the **Location** (if multi-location)
7. Add optional notes
8. Click **Save**

### Managing Appointments
- **Check In** — Click an appointment and select "Check In" to mark the patient as arrived
- **Start Encounter** — Click "Start Encounter" to begin the clinical visit (creates an encounter record)
- **Cancel** — Click "Cancel" to cancel the appointment (with reason)
- **No-Show** — Mark patients who did not arrive
- **Reschedule** — Drag-and-drop the appointment to a new time slot, or edit the appointment details

### Appointment Reminders
The system automatically sends appointment reminders to patients via SMS and/or email based on clinic settings. Reminders are typically sent 24 hours before the appointment.

---

## 7. Encounters

Encounters represent clinical visits. When a patient is seen, an encounter captures all clinical activity for that visit.

### Starting an Encounter
1. From the Schedule, click on a checked-in appointment
2. Click **Start Encounter**
3. This opens the **Encounter Workspace** — a dedicated page for the visit

### Encounter Workspace
The Encounter Workspace is a comprehensive view for managing the clinical visit. It includes:

#### Vitals Section
- Record vital signs: Blood Pressure, Heart Rate, Temperature, Respiratory Rate, SpO2, Height, Weight, BMI (auto-calculated)
- MA/Nurse roles can enter vitals

#### Chief Complaint & HPI
- Document the reason for the visit (Chief Complaint)
- Record the History of Present Illness (HPI)
- MA/Nurse roles can enter CC and HPI

#### Review of Systems (ROS)
- Systematic review organized by body system
- Quick checkboxes for common findings
- Free-text areas for additional details

#### Physical Examination
- Examination findings organized by body system
- Normal/Abnormal toggles with detail fields
- Template-based for consistency

#### Assessment & Plan
- Document diagnoses (link to ICD-10 codes from problem list)
- Treatment plan and follow-up instructions
- Patient education notes

#### Orders
- Create lab orders, imaging orders, and referrals directly from the encounter
- Link orders to encounter diagnoses

#### Prescriptions
- Write new prescriptions from within the encounter
- Review current medication list

#### Clinical Note
- Generate or create a clinical note for the encounter
- Auto-populate from encounter data
- Sign and finalize the note

### MEDOCS Voice Entry
The Encounter Workspace supports voice-based data entry:
1. Click the **microphone icon** in supported sections
2. Speak your clinical findings
3. The system transcribes your speech using AI
4. Review and edit the transcription before saving

---

## 8. Clinical Notes

### Browsing Notes
1. Navigate to **Clinical Notes** from the sidebar
2. View all notes in a filterable list
3. Filter by: Provider, Patient, Date Range, Status (Draft, Signed, Addendum)
4. Click any note to view its details

### Note Statuses
- **Draft** — Note is being written, not yet finalized
- **Signed** — Note has been signed by the provider (locked for editing)
- **Addendum** — An addition to a previously signed note

### Creating a Clinical Note
1. From a patient's profile or encounter, click **New Clinical Note**
2. Select a **Note Template** (e.g., SOAP Note, Progress Note, Annual Physical, Procedure Note)
3. The template pre-populates the note structure
4. Fill in each section: Subjective, Objective, Assessment, Plan
5. Add diagnoses (ICD-10), procedures (CPT)
6. Click **Save as Draft** or **Sign Note**

### Signing a Note
- Only **Clinicians** and **Super Admins** can sign notes
- Signing locks the note from further editing
- An addendum can be added after signing
- The signature includes the provider's name, credentials, and timestamp

### Note Templates
Note templates define the structure and default content for clinical notes. Templates are managed by Super Admins under **Note Templates** in the sidebar.

---

## 9. E-Prescribe

### Overview
The E-Prescribe module allows clinicians to write, manage, and send electronic prescriptions.

### Writing a Prescription
1. Navigate to **E-Prescribe** from the sidebar, or start from within an encounter
2. Click **New Prescription**
3. Search for the **Medication** by name (drug database search)
4. Select the formulation and strength
5. Enter **Directions** (sig): dose, route, frequency, duration
6. Set **Quantity** and **Refills**
7. Select the patient's **Pharmacy**
8. Review drug interaction warnings if any
9. Click **Send** to transmit the prescription

### Prescription Management
- View all prescriptions in a searchable list
- Filter by patient, provider, status, or date
- Statuses: Active, Discontinued, Expired
- Renew or discontinue existing prescriptions

### Drug Interaction Checks
The system automatically checks for:
- Drug-drug interactions
- Duplicate therapy
- Drug-allergy conflicts (based on patient's allergy list)

---

## 10. Orders (Labs, Imaging, Referrals)

### Creating an Order
1. Navigate to **Orders** from the sidebar, or create from within an encounter
2. Click **New Order**
3. Select the **Order Type**: Lab, Imaging, or Referral
4. For **Lab Orders**: Search the lab test catalog, select tests, add clinical indication
5. For **Imaging Orders**: Select modality (X-Ray, MRI, CT, etc.), body part, and clinical reason
6. For **Referrals**: Select specialty, referring provider, and reason for referral
7. Set **Priority** (Routine, Urgent, STAT)
8. Add clinical notes or special instructions
9. Click **Submit**

### Order Tracking
- View all orders in a filterable list
- Track order status: Pending, In Progress, Completed, Cancelled
- View results when available
- Filter by patient, provider, type, or status

---

## 11. Providers

### Provider Directory (Admin Only)
1. Navigate to **Providers** from the sidebar
2. View all providers in the clinic
3. Each provider card shows: Name, Specialty, NPI, Status

### Managing Providers
- **Add Provider** — Create a new provider record with credentials, specialty, and NPI
- **Edit Provider** — Update provider information
- **Provider Color** — Each provider is assigned a color for calendar display
- **Active/Inactive** — Toggle provider availability

---

## 12. Provider Availability & Time Off

### Viewing Availability
1. Navigate to **Provider Availability** from the sidebar
2. View a calendar of provider schedules and time-off blocks
3. Filter by provider

### Requesting Time Off
1. Click **Request Time Off**
2. Select the **Provider** (auto-selected if you are a clinician)
3. Choose **Start Date** and **End Date**
4. Select **Time Off Type** (Vacation, Sick, Personal, Conference, etc.)
5. Add optional notes
6. Submit the request

### Managing Time Off (Admin)
- Admins can approve or deny time-off requests
- Approved time off appears as blocked time on the calendar
- Conflicting appointments are flagged for rescheduling

---

## 13. Reports (Admin Only)

### Available Reports
- **Appointment Summary** — Appointment counts by provider, type, status, and date range
- **Patient Demographics** — Patient population analysis
- **Clinical Notes Report** — Note completion and signing metrics
- **Provider Productivity** — Encounter counts and time metrics per provider
- **Revenue Report** — Billing and collection summaries

### Running a Report
1. Navigate to **Reports** from the sidebar
2. Select the report type
3. Set filter criteria (date range, provider, location)
4. Click **Generate Report**
5. View results in charts and tables
6. Export to CSV or PDF if available

---

## 14. User Management (Admin Only)

### Viewing Users
1. Navigate to **User Management** from the sidebar
2. View all staff accounts with name, email, role, and status

### Creating a User
1. Click **Add User**
2. Enter: First Name, Last Name, Email
3. Select a **Role** (see Role Permissions below)
4. Assign to a **Location** if applicable
5. The user will receive an email invitation to set their password

### Editing a User
- Update name, email, role, or location
- Reset password (sends reset email)
- Deactivate account (user cannot log in)
- Reactivate account

---

## 15. Locations Management (Admin Only)

### Multi-Location Support
IMEHR supports clinics with multiple physical locations. Each location can have its own:
- Address and contact information
- Provider assignments
- Schedule and calendar
- Patient assignments

### Managing Locations
1. Click **Locations** in the sidebar
2. View all locations with address and status
3. **Add Location** — Create a new location with name, address, phone, and fax
4. **Edit Location** — Update location details
5. **Set Default** — Mark one location as the default for new records

---

## 16. Settings (Super Admin Only)

### Clinic Settings
Clinic settings are managed exclusively by the **Super Admin**. Clinic Admins cannot modify these settings. If your clinic information needs to be updated, contact **contact@medocs.ai**.

Settings include:
   - **Clinic Information** — Name, address, phone, fax, NPI, Tax ID
   - **Clinic Logo** — Upload a logo displayed in the sidebar and on documents
   - **Appointment Settings** — Default duration, buffer time, working hours
   - **Notification Settings** — SMS and email reminder configuration
   - **Clinical Settings** — Default note templates, auto-save preferences

---

## 17. Consent Forms (Admin Only)

### Managing Consent Templates
1. Navigate to **Consent Forms** from the sidebar
2. View existing consent form templates
3. **Create Template** — Build a new consent form with title, content, and signature fields
4. **Edit Template** — Modify existing templates
5. **Activate/Deactivate** — Control which forms are available for patient signing

### Patient Consent Workflow
1. Select consent forms for a patient to sign
2. Patient can sign via the **Kiosk Mode** (tablet/check-in device) or in-office
3. Signed forms are stored in the patient's Documents section
4. Digital signatures include timestamp and IP address

### Kiosk Mode
- Kiosk mode provides a simplified, patient-facing interface
- Patients can sign consent forms, update demographics, and check in
- Access via a dedicated kiosk URL with QR code support

---

## 18. Internal Messaging

### Overview
The built-in messaging system allows secure communication between staff members within the clinic.

### Sending a Message
1. Click the **messaging icon** in the sidebar or bottom toolbar
2. Click **New Conversation**
3. Select one or more recipients from the staff directory
4. Type your message
5. Press **Enter** or click **Send**

### Features
- Real-time message delivery (no page refresh needed)
- Read receipts
- Patient mentions — Reference a patient in the conversation for context
- Message search
- Conversation history

---

## 19. Role Permissions

IMEHR uses role-based access control. Here is what each role can do:

### Super Admin (Role 0)
- Full access to all features
- Manage clinics, note templates, lien templates
- Manage users, settings, locations
- Create, edit, sign clinical notes
- All clinical actions

### Clinic Admin (Role 1)
- Manage users, locations, providers, consent forms
- View and manage all appointments and patients
- View clinical notes and reports
- **Cannot** modify clinic settings (name, address, NPI, logo) — this is Super Admin only. Contact **contact@medocs.ai** for changes.
- Cannot sign clinical notes (unless also a clinician)

### Clinician (Role 2)
- Full clinical access: create/edit/sign notes, prescriptions, orders
- Manage own appointments and schedule
- View and manage patients
- Access to Encounter Workspace with all clinical tools
- Set personal preferences

### Front Desk (Role 3)
- Manage appointments (create, edit, cancel, check-in)
- Create and edit patient demographics
- View schedule and patient lists
- Cannot access prescriptions or clinical notes editing
- Cannot access billing

### Biller (Role 4)
- Access billing and financial reports
- View patient demographics and insurance
- View clinical notes (read-only) for coding
- Cannot manage appointments or clinical data
- Cannot access orders

### Read Only (Role 5)
- View-only access to all sections they can see
- Cannot create, edit, or delete any records
- Useful for auditors or compliance officers

### Medical Assistant (Role 6)
- Enter vital signs during encounters
- Enter Chief Complaint and HPI
- View all clinical data (read-only for notes, orders, prescriptions)
- Cannot create/edit clinical notes, orders, or prescriptions
- Cannot manage patients or appointments
- Cannot sign notes

### Nurse (Role 7)
- Same permissions as Medical Assistant
- Enter vital signs and CC/HPI
- View clinical data (read-only)
- Cannot create/edit/sign clinical notes
- Cannot manage prescriptions or orders

---

## 20. Common Workflows

### New Patient Visit Workflow
1. **Front Desk** creates the patient record with demographics and insurance
2. **Front Desk** schedules a New Patient appointment
3. Patient arrives — **Front Desk** checks them in (or patient checks in via Kiosk)
4. Patient signs consent forms (Kiosk or in-office)
5. **MA/Nurse** records vitals and Chief Complaint/HPI
6. **Clinician** starts the encounter from the calendar
7. **Clinician** completes the clinical note (exam, assessment, plan)
8. **Clinician** writes prescriptions and/or orders as needed
9. **Clinician** signs the clinical note
10. **Front Desk** schedules the follow-up appointment

### Follow-Up Visit Workflow
1. Patient arrives — **Front Desk** checks them in
2. **MA/Nurse** records updated vitals
3. **Clinician** opens the encounter
4. **Clinician** reviews previous notes, updates assessment and plan
5. **Clinician** adjusts medications/orders as needed
6. **Clinician** signs the follow-up note
7. **Front Desk** schedules the next visit

### Prescription Renewal Workflow
1. Patient calls or requests a refill
2. **Front Desk** or **Clinician** pulls up the patient's medication list
3. **Clinician** reviews the prescription
4. **Clinician** renews the prescription via E-Prescribe
5. Prescription is sent electronically to the patient's pharmacy

### Lab Order Workflow
1. **Clinician** creates a lab order from the Encounter Workspace or Orders page
2. Select tests from the lab catalog with clinical indication
3. Order is marked as Pending
4. When results return, update the order status to Completed
5. **Clinician** reviews results and updates the patient chart

### Referral Workflow
1. **Clinician** creates a referral order
2. Select the specialty and reason for referral
3. Include relevant clinical information
4. Referral is tracked in the Orders list
5. Update status when referral is completed

---

## 21. Medical Liens

### Overview
Medical liens allow clinics to document legal holds on patient treatment records, typically for personal injury cases.

### Creating a Medical Lien
1. From the patient profile, navigate to the **Medical Lien** section
2. Click **Generate Lien**
3. Select a **Lien Template** (managed by Super Admins)
4. Fill in case details: attorney information, date of injury, case number
5. The system generates a formatted lien document
6. Download as PDF for printing or sending

### Lien Templates (Super Admin)
- Navigate to **Lien Templates** in the sidebar
- Create and manage document templates with merge fields
- Templates support dynamic fields: patient name, DOB, provider, dates, etc.

---

## 22. Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| Ctrl+K (Cmd+K on Mac) | Open global patient search |
| Escape | Close current modal or dialog |

---

## 23. Troubleshooting

### Cannot Log In
- Verify your email and password are correct
- Check if your account has been deactivated (contact your admin)
- Try resetting your password via "Forgot Password"
- Clear browser cache and cookies

### Page Not Loading
- Check your internet connection
- Try refreshing the page (Ctrl+R or Cmd+R)
- Clear browser cache
- Try a different browser (Chrome is recommended)

### Session Expired
- Your session expires after a period of inactivity
- Log in again to continue working
- Unsaved changes may be lost — save frequently

### Missing Features or Menu Items
- Some features are only available to certain roles
- Contact your Clinic Admin to verify your role and permissions
- If you believe you should have access, ask your admin to update your role

---

## 24. Need More Help?

If you cannot find the answer to your question in this documentation:
1. Use the **MEDOCS AI** help assistant (click the Help button in the bottom-right corner)
2. The AI assistant can answer questions about using IMEHR
3. If a feature you need doesn't exist, the AI will offer to send your suggestion as a **feature request** to the development team
4. You can also email us directly at **contact@medocs.ai**
