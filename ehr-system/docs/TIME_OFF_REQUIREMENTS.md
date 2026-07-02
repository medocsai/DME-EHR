# Time Off Functionality - Requirements Specification

## Document Information
- **Version:** 1.0
- **Last Updated:** January 29, 2026
- **Status:** Ready for Implementation

---

## 1. Overview

### 1.1 Problem Statement
The Time Off functionality was working correctly in the legacy code (`legacy frontend` folder) but is currently not functioning properly in the new system. This document outlines the requirements to restore and enhance this functionality.

### 1.2 Objective
Restore full Time Off functionality by referencing the legacy implementation and adapting it to the current module-based architecture.

### 1.3 Reference Materials
- **Legacy Code Location:** `legacy frontend/wwwroot/js/app.js`
- **Current UI Location:** `Views/Unavailability/Index.cshtml`
- **Related Modals:** `Views/Shared/_Modals.cshtml`

---

## 2. Test Accounts

| Role | Email | Password |
|------|-------|----------|
| Super Admin | rehabdoxllc@gmail.com | Admin@123 |
| Clinic Admin | ahmed.khan@kw.com | Demo@123 |
| Clinician | sarah.johnson@kw.com | Demo@123 |

**Base URL:** `https://localhost:52230/`

---

## 3. Functional Requirements

### 3.1 Time Off Creation

#### 3.1.1 Who Can Create
- **Providers:** Can create time off for themselves only
- **Clinic Admins:** Can create time off for any provider in their clinic

#### 3.1.2 Required Fields
| Field | Type | Validation |
|-------|------|------------|
| Provider | Dropdown | Required |
| Start Date | Date | Required, must not be in past |
| Start Time | Time | Required |
| End Date | Date | Required, must be >= Start Date |
| End Time | Time | Required, must be > Start Time if same day |
| Reason | Text/Dropdown | Required |

#### 3.1.3 Time Off Types
- Single day (full day)
- Single day (partial - e.g., 2 PM to 5 PM)
- Multi-day (spans multiple calendar days)

#### 3.1.4 Validation Rules
- Start date/time must be before end date/time
- Start date cannot be in the past
- Overlapping time off periods should show warning
- TenantId and LocationId filtering must be enforced

---

### 3.2 Appointment Scheduling Impact

#### 3.2.1 Time Slot Blocking
When a provider has time off:
- Time slots during the time off period must NOT be available for selection
- Blocked slots should be visually distinct (grayed out or marked unavailable)
- Clear indication that provider is unavailable due to time off

#### 3.2.2 Booking Prevention
- System must prevent booking appointments during provider's time off
- If user attempts to book during time off, show appropriate error message
- Backend validation required (not just frontend)

#### 3.2.3 Visual Indicators
```
Schedule View:
+------------------+------------------+------------------+
| 9:00 AM          | 10:00 AM         | 11:00 AM         |
| [Available]      | [TIME OFF]       | [TIME OFF]       |
|                  | (grayed out)     | (grayed out)     |
+------------------+------------------+------------------+
| 12:00 PM         | 1:00 PM          | 2:00 PM          |
| [TIME OFF]       | [Available]      | [Available]      |
| (grayed out)     |                  |                  |
+------------------+------------------+------------------+
```

---

### 3.3 Provider Availability

#### 3.3.1 Availability Display
- Provider availability view must show time off periods
- Time off should be clearly marked in provider schedule
- Other users should see when provider is on time off

#### 3.3.2 Calendar Integration
- Time off should appear on calendar views
- Different visual styling than appointments
- Show reason for time off (if appropriate for viewer role)

---

### 3.4 Recurring Appointments Interaction

#### 3.4.1 Creating Recurring Appointments
When creating a recurring appointment that would conflict with future time off:
- Show warning about conflicting dates
- Option to: skip conflicting dates OR prevent creation

#### 3.4.2 Creating Time Off with Existing Appointments
When creating time off that overlaps with existing appointments:
- Show list of affected appointments
- Require confirmation or cancellation of affected appointments
- Do NOT silently override existing appointments

---

### 3.5 Viewing Time Off

#### 3.5.1 Provider View (Own Time Off)
- View own time off requests only
- Filter by status (Pending, Approved, Rejected)
- Filter by date range

#### 3.5.2 Clinic Admin View (All Time Off)
- View time off for all providers in clinic
- Filter by provider
- Filter by status
- Filter by date range
- Approve/Reject pending requests (if workflow requires)

---

### 3.6 Editing Time Off

#### 3.6.1 Editable Fields
- Start date/time
- End date/time
- Reason

#### 3.6.2 Edit Validation
- Same validation rules as creation
- Check for new conflicts with appointments
- Cannot edit past time off

---

### 3.7 Deleting/Canceling Time Off

#### 3.7.1 Deletion Rules
- Can delete future time off
- Cannot delete past time off
- Confirm deletion with user

#### 3.7.2 Effects of Deletion
- Previously blocked time slots become available again
- Calendar updates immediately
- No automatic rescheduling of affected appointments

---

## 4. Non-Functional Requirements

### 4.1 Performance
| Operation | Target Response Time |
|-----------|---------------------|
| Create time off | < 2 seconds |
| Check availability | < 1 second |
| Filter appointment slots | No UI freeze |
| Load time off list | < 1 second |

### 4.2 Security
- Filter all queries by TenantId and LocationId
- Validate all user input
- Enforce role-based permissions
- Log time off creation/modification for audit trail
- Never log PHI (Protected Health Information)

### 4.3 Error Handling
- Use try-catch for database and API operations
- Show user-friendly error messages
- Log errors with context
- Application must never crash

---

## 5. Technical Implementation

### 5.1 Database Schema

#### TimeOff Table (if not exists)
```sql
CREATE TABLE TimeOff (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    ProviderId INT NOT NULL,
    TenantId INT NOT NULL,
    LocationId INT NOT NULL,
    StartDateTime DATETIME NOT NULL,
    EndDateTime DATETIME NOT NULL,
    Reason NVARCHAR(500),
    Status NVARCHAR(50) DEFAULT 'Approved', -- Pending, Approved, Rejected
    CreatedBy INT NOT NULL,
    CreatedDate DATETIME DEFAULT GETDATE(),
    ModifiedBy INT,
    ModifiedDate DATETIME,
    IsDeleted BIT DEFAULT 0,

    CONSTRAINT FK_TimeOff_Provider FOREIGN KEY (ProviderId) REFERENCES Provider(Id)
);

-- Indexes for performance
CREATE INDEX IX_TimeOff_Provider ON TimeOff(ProviderId);
CREATE INDEX IX_TimeOff_Dates ON TimeOff(StartDateTime, EndDateTime);
CREATE INDEX IX_TimeOff_Tenant ON TimeOff(TenantId, LocationId);
```

### 5.2 API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/timeoff` | Get time off (with filters) |
| GET | `/api/timeoff/{id}` | Get specific time off |
| POST | `/api/timeoff` | Create time off |
| PUT | `/api/timeoff/{id}` | Update time off |
| DELETE | `/api/timeoff/{id}` | Delete time off |
| GET | `/api/timeoff/provider/{providerId}` | Get time off for specific provider |
| GET | `/api/timeoff/conflicts` | Check for conflicts with appointments |

### 5.3 Architecture Layers

```
+------------------+
|       UI         |  Views/Unavailability/Index.cshtml
|   (Razor View)   |  Views/Shared/_Modals.cshtml
+------------------+
         |
+------------------+
|    JavaScript    |  wwwroot/js/modules/timeoff/TimeOffModule.js
|     (Module)     |
+------------------+
         |
+------------------+
|   Controller     |  Controllers/TimeOffController.cs
+------------------+
         |
+------------------+
|    Service       |  Services/TimeOffService.cs
+------------------+
         |
+------------------+
|   Repository     |  Models/Generated/EhrDbContext.cs
|   (EF Core)      |
+------------------+
```

---

## 6. Test Scenarios

### 6.1 Basic CRUD Operations

#### Test 1: Create Time Off
1. Login as Clinician (sarah.johnson@kw.com)
2. Navigate to Time Off section
3. Create time off request:
   - Start: Tomorrow 9:00 AM
   - End: Tomorrow 5:00 PM
   - Reason: Personal
4. **Expected:** Success message, time off appears in list, no console errors

#### Test 2: Edit Time Off
1. Find existing time off
2. Edit dates or times
3. Save changes
4. **Expected:** Changes saved, availability updates accordingly

#### Test 3: Delete Time Off
1. Find existing time off
2. Delete/cancel it
3. **Expected:** Time off removed, time slots become available again

---

### 6.2 Appointment Scheduling Integration

#### Test 4: Time Off Blocks Scheduling
1. Login as Clinic Admin (ahmed.khan@kw.com)
2. Go to Schedule page
3. Try to create appointment during provider's time off
4. **Expected:** Time slots grayed out, cannot select, clear "Time Off" indicator

#### Test 5: Provider Availability Shows Time Off
1. Login as Clinic Admin
2. View provider schedule
3. Navigate to date with time off
4. **Expected:** Time off clearly visible, shows reason

---

### 6.3 Time Off Variations

#### Test 6: Multi-Day Time Off
1. Create time off: Monday 9 AM to Friday 5 PM
2. **Expected:** All days blocked, appointments blocked for entire period

#### Test 7: Partial Day Time Off
1. Create time off: Tomorrow 2:00 PM to 5:00 PM
2. **Expected:** Only afternoon slots blocked, morning available

---

### 6.4 Recurring Appointments

#### Test 8: Recurring Appointment Conflict
1. Create recurring appointment that conflicts with existing time off
2. **Expected:** Warning shown or conflict dates skipped

#### Test 9: Time Off Over Existing Recurring
1. Create time off that overlaps with existing recurring appointments
2. **Expected:** System handles appropriately (warning or prevention)

---

### 6.5 Role-Based Access

#### Test 10: Provider Views Own Time Off Only
1. Login as Clinician
2. View time off
3. **Expected:** Only own time off visible

#### Test 11: Admin Views All Provider Time Off
1. Login as Clinic Admin
2. View time off for all providers
3. **Expected:** Can see all providers, can filter by provider

---

### 6.6 Edge Cases

| Test Case | Expected Behavior |
|-----------|-------------------|
| Create time off in the past | Validation error |
| End date before start date | Validation error |
| Overlapping time off periods | Warning shown |
| Time off for different tenant | Not allowed |
| Very long time off (weeks/months) | Should work correctly |
| Time off at midnight boundaries | Should work correctly |

---

### 6.7 Performance Tests

| Test | Expected |
|------|----------|
| Create time off | < 2 seconds |
| Check availability | < 1 second |
| Filter appointment slots | No UI freeze |
| Multiple operations | No memory leaks |

---

### 6.8 Error Handling Tests

| Scenario | Expected |
|----------|----------|
| Network disconnect during save | Appropriate error message |
| Invalid data submission | Validation message |
| Insufficient permissions | Access denied message |
| Server error | Friendly error, no crash |

---

## 7. Code Quality Standards

### 7.1 Clean Code Requirements
- Meaningful names for variables, methods, classes
- Single responsibility - one method does one thing
- DRY - create reusable functions
- Comments for complex business logic only
- Follow existing code patterns

### 7.2 SOLID Principles
- **S**ingle Responsibility: Separate UI, API, business logic, data
- **O**pen/Closed: Use interfaces for extensibility
- **L**iskov Substitution: Proper inheritance hierarchies
- **I**nterface Segregation: Small, focused interfaces
- **D**ependency Inversion: Use dependency injection

### 7.3 Error Handling Pattern
```csharp
public async Task<Result<TimeOff>> CreateTimeOff(TimeOffDto dto)
{
    try
    {
        // Validation
        var validationResult = Validate(dto);
        if (!validationResult.IsValid)
            return Result<TimeOff>.Failure(validationResult.Errors);

        // Business logic
        var timeOff = await _repository.CreateAsync(dto);

        return Result<TimeOff>.Success(timeOff);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error creating time off for provider {ProviderId}", dto.ProviderId);
        return Result<TimeOff>.Failure("An error occurred while creating time off.");
    }
}
```

### 7.4 Pre-Completion Checklist
- [ ] Remove all `console.log` statements
- [ ] Remove debug code and breakpoints
- [ ] Remove commented-out code
- [ ] Ensure consistent code style
- [ ] Verify no PHI in logs

---

## 8. Legacy Code Analysis Tasks

Before implementing, study the legacy code to understand:

1. **Data Model**
   - How time off was stored
   - Relationships with providers and appointments

2. **Business Logic**
   - Validation rules
   - Conflict checking algorithms
   - Permission checks

3. **UI/UX Patterns**
   - Form layout and fields
   - Calendar integration
   - Error message display

4. **API Patterns**
   - Endpoint structure
   - Request/response formats
   - Error handling

---

## 9. Completion Checklist

### Functionality
- [ ] Time off can be created
- [ ] Time off blocks appointment scheduling
- [ ] Time off shows in provider availability
- [ ] Time off works with recurring appointments
- [ ] Time off can be edited
- [ ] Time off can be deleted
- [ ] All features match legacy functionality

### Testing
- [ ] All test scenarios pass
- [ ] Edge cases handled
- [ ] No console errors
- [ ] Performance targets met
- [ ] Cross-feature integration works

### Code Quality
- [ ] Clean, readable code
- [ ] Follows SOLID principles
- [ ] Proper error handling
- [ ] No debug code remaining
- [ ] Consistent with existing patterns

### Documentation
- [ ] Code comments for complex logic
- [ ] API endpoints documented
- [ ] Database schema documented

---

## 10. Appendix

### A. Related Files to Review/Modify

**Backend:**
- `Controllers/TimeOffController.cs` (create if needed)
- `Services/TimeOffService.cs` (create if needed)
- `Services/AppointmentService.cs` (modify for availability check)
- `Models/Generated/EhrDbContext.cs` (add TimeOff entity)

**Frontend:**
- `Views/Unavailability/Index.cshtml`
- `Views/Shared/_Modals.cshtml`
- `wwwroot/js/modules/timeoff/TimeOffModule.js` (create)
- `wwwroot/js/modules/appointments/AppointmentModule.js` (modify)
- `wwwroot/js/modules/calendar/CalendarModule.js` (modify)

**Legacy Reference:**
- `legacy frontend/wwwroot/js/app.js`

### B. Cross-Feature Testing Matrix

| Feature | Test With Time Off |
|---------|-------------------|
| Schedule page | Slots blocked |
| Appointment creation | Cannot book during time off |
| Appointment editing | Cannot move to time off period |
| Provider schedule view | Time off visible |
| Recurring appointments | Conflicts handled |
| Calendar views | Time off displayed |
| Reports | Time off affects reports (if applicable) |
