/**
 * Unit tests for PatientModule
 */

// Load the module
const fs = require('fs');
const path = require('path');
const modulePath = path.resolve(__dirname, '../../modules/patients/PatientModule.js');
const moduleCode = fs.readFileSync(modulePath, 'utf8');
eval(moduleCode);

describe('PatientModule', () => {
    let module;
    let mockApi;
    let mockEventBus;

    // Sample patient data
    const mockPatients = [
        {
            PatientId: 1,
            FirstName: 'John',
            LastName: 'Doe',
            FullName: 'John Doe',
            MRN: 'MRN001',
            DateOfBirth: '1990-05-15',
            Age: 35,
            Phone: '555-1234',
            Email: 'john@example.com',
            Status: 0,
            IsArchived: false,
            Insurances: [
                {
                    InsuranceId: 1,
                    Type: 0,
                    PayerName: 'Blue Cross',
                    IsActive: true
                }
            ]
        },
        {
            PatientId: 2,
            FirstName: 'Jane',
            LastName: 'Smith',
            FullName: 'Jane Smith',
            MRN: 'MRN002',
            DateOfBirth: '1985-08-22',
            Age: 40,
            Phone: '555-5678',
            Email: 'jane@example.com',
            Status: 0,
            IsArchived: false,
            Insurances: []
        },
        {
            PatientId: 3,
            FirstName: 'Bob',
            LastName: 'Wilson',
            FullName: 'Bob Wilson',
            MRN: 'MRN003',
            DateOfBirth: '1975-12-01',
            Age: 50,
            Phone: '555-9999',
            Status: 1,
            IsArchived: true,
            Insurances: []
        }
    ];

    const mockPatientDetail = {
        ...mockPatients[0],
        Address: '123 Main St',
        City: 'Springfield',
        State: 'IL',
        ZipCode: '62701',
        Gender: 'Male',
        EmergencyContactName: 'Mary Doe',
        EmergencyContactPhone: '555-0000',
        EmergencyContactRelation: 'Spouse',
        CareEpisodes: [
            {
                CareEpisodeId: 1,
                PrimaryDiagnosis: 'Lower back pain',
                Status: 0,
                StartDate: '2024-01-01',
                VisitsUsed: 5,
                PrimaryProviderName: 'Dr. Smith'
            }
        ]
    };

    beforeEach(() => {
        // Create mock API
        mockApi = {
            get: jest.fn(),
            post: jest.fn(),
            put: jest.fn(),
            delete: jest.fn()
        };

        // Create mock EventBus
        mockEventBus = {
            emit: jest.fn()
        };

        // Create container elements
        createTestElement('div', 'patientsGrid');
        createTestElement('tbody', 'patientsTableBody');
        createTestElement('input', 'patientSearch');
        createTestElement('div', 'patientDetailContent');
        createTestElement('form', 'patientForm');
        createTestElement('input', 'patientId');

        // Create filter buttons
        const filterGroup = document.createElement('div');
        filterGroup.className = 'btn-group';
        filterGroup.innerHTML = `
            <button class="btn" data-patient-filter="status" data-filter-value="all">All</button>
            <button class="btn" data-patient-filter="status" data-filter-value="active">Active</button>
        `;
        document.body.appendChild(filterGroup);

        // Create module with mocks
        module = new PatientModule({
            api: mockApi,
            eventBus: mockEventBus
        });
    });

    afterEach(() => {
        if (module) {
            module.destroy();
        }
        document.body.innerHTML = '';
    });

    describe('constructor', () => {
        it('should initialize with default state', () => {
            expect(module.patients).toEqual([]);
            expect(module.currentPatient).toBeNull();
            expect(module.currentFilters.status).toBe('all');
            expect(module.isInitialized).toBe(false);
            expect(module.api).toBe(mockApi);
            expect(module.eventBus).toBe(mockEventBus);
        });

        it('should work without options', () => {
            const defaultModule = new PatientModule();
            expect(defaultModule.api).toBeNull();
            expect(defaultModule.eventBus).toBeNull();
        });
    });

    describe('init', () => {
        it('should warn if container not found', async () => {
            document.body.innerHTML = '';
            const warnSpy = jest.spyOn(console, 'warn');

            await module.init();

            expect(warnSpy).toHaveBeenCalledWith('[PatientModule] Container not found');
            expect(module.isInitialized).toBe(false);
        });

        it('should initialize with container', async () => {
            await module.init();

            expect(module.isInitialized).toBe(true);
            expect(mockEventBus.emit).toHaveBeenCalledWith('patients:initialized');
        });
    });

    describe('load', () => {
        beforeEach(async () => {
            await module.init();
        });

        it('should load patients from API', async () => {
            mockApi.get.mockResolvedValue(mockPatients);

            await module.load();

            expect(mockApi.get).toHaveBeenCalledWith('/patients');
            expect(module.patients).toEqual(mockPatients);
        });

        it('should render patients to table', async () => {
            mockApi.get.mockResolvedValue(mockPatients);

            await module.load();

            const tbody = document.getElementById('patientsTableBody');
            expect(tbody.innerHTML).toContain('John Doe');
            expect(tbody.innerHTML).toContain('Jane Smith');
        });

        it('should emit loaded event', async () => {
            mockApi.get.mockResolvedValue(mockPatients);

            await module.load();

            expect(mockEventBus.emit).toHaveBeenCalledWith('patients:loaded', {
                patients: mockPatients,
                filters: expect.any(Object)
            });
        });

        it('should handle empty patients', async () => {
            mockApi.get.mockResolvedValue([]);

            await module.load();

            const tbody = document.getElementById('patientsTableBody');
            expect(tbody.innerHTML).toContain('No patients found');
        });

        it('should handle API errors', async () => {
            mockApi.get.mockRejectedValue(new Error('Network error'));

            await expect(module.load()).rejects.toThrow('Network error');
        });

        it('should apply filters to query', async () => {
            mockApi.get.mockResolvedValue(mockPatients);
            module.currentFilters.status = 'active';
            module.currentFilters.search = 'john';

            await module.load();

            expect(mockApi.get).toHaveBeenCalledWith('/patients?status=active&search=john');
        });
    });

    describe('view', () => {
        beforeEach(async () => {
            await module.init();
        });

        it('should load and display patient details', async () => {
            mockApi.get.mockResolvedValue(mockPatientDetail);

            await module.view(1);

            expect(mockApi.get).toHaveBeenCalledWith('/patients/1');
            expect(module.currentPatient).toEqual(mockPatientDetail);
        });

        it('should emit viewed event', async () => {
            mockApi.get.mockResolvedValue(mockPatientDetail);

            await module.view(1);

            expect(mockEventBus.emit).toHaveBeenCalledWith('patients:viewed', {
                patient: mockPatientDetail
            });
        });

        it('should render patient modal content', async () => {
            mockApi.get.mockResolvedValue(mockPatientDetail);

            await module.view(1);

            const content = document.getElementById('patientDetailContent');
            expect(content.innerHTML).toContain('John Doe');
            expect(content.innerHTML).toContain('MRN001');
            expect(content.innerHTML).toContain('Lower back pain');
        });
    });

    describe('edit', () => {
        beforeEach(async () => {
            await module.init();
            // Add form fields
            const form = document.getElementById('patientForm');
            form.innerHTML = `
                <input name="FirstName">
                <input name="LastName">
                <input name="DateOfBirth">
                <input name="Phone">
                <input name="Email">
            `;
        });

        it('should load patient data into form', async () => {
            mockApi.get.mockResolvedValue(mockPatientDetail);

            await module.edit(1);

            expect(mockApi.get).toHaveBeenCalledWith('/patients/1');
            expect(document.getElementById('patientId').value).toBe('1');
        });

        it('should emit editing event', async () => {
            mockApi.get.mockResolvedValue(mockPatientDetail);

            await module.edit(1);

            expect(mockEventBus.emit).toHaveBeenCalledWith('patients:editing', {
                patient: mockPatientDetail
            });
        });

        it('should block therapist from editing', async () => {
            localStorage.setItem('currentUser', JSON.stringify({ Role: 2 }));
            global.Toast = { warning: jest.fn() };

            await module.edit(1);

            expect(mockApi.get).not.toHaveBeenCalled();
            expect(Toast.warning).toHaveBeenCalled();

            localStorage.removeItem('currentUser');
        });
    });

    describe('delete', () => {
        beforeEach(async () => {
            await module.init();
            mockApi.get.mockResolvedValue(mockPatients);
        });

        it('should delete patient after confirmation', async () => {
            global.ConfirmDialog = { show: jest.fn().mockResolvedValue(true) };
            mockApi.delete.mockResolvedValue({});

            await module.delete(1, 'John Doe');

            expect(mockApi.delete).toHaveBeenCalledWith('/patients/1');
            expect(mockEventBus.emit).toHaveBeenCalledWith('patients:deleted', {
                patientId: 1,
                patientName: 'John Doe'
            });
        });

        it('should not delete if user cancels', async () => {
            global.ConfirmDialog = { show: jest.fn().mockResolvedValue(false) };

            await module.delete(1, 'John Doe');

            expect(mockApi.delete).not.toHaveBeenCalled();
        });
    });

    describe('archive/unarchive', () => {
        beforeEach(async () => {
            await module.init();
            mockApi.get.mockResolvedValue(mockPatients);
        });

        it('should archive patient after confirmation', async () => {
            global.ConfirmDialog = { show: jest.fn().mockResolvedValue(true) };
            mockApi.post.mockResolvedValue({});

            await module.archive(1, 'John Doe');

            expect(mockApi.post).toHaveBeenCalledWith('/patients/1/archive');
            expect(mockEventBus.emit).toHaveBeenCalledWith('patients:archived', {
                patientId: 1,
                patientName: 'John Doe'
            });
        });

        it('should unarchive patient after confirmation', async () => {
            global.ConfirmDialog = { show: jest.fn().mockResolvedValue(true) };
            mockApi.post.mockResolvedValue({});

            await module.unarchive(3, 'Bob Wilson');

            expect(mockApi.post).toHaveBeenCalledWith('/patients/3/unarchive');
            expect(mockEventBus.emit).toHaveBeenCalledWith('patients:unarchived', {
                patientId: 3,
                patientName: 'Bob Wilson'
            });
        });
    });

    describe('filters', () => {
        beforeEach(async () => {
            await module.init();
            mockApi.get.mockResolvedValue(mockPatients);
        });

        it('should apply status filter', async () => {
            module.currentFilters.status = 'active';

            await module.applyFilters();

            expect(mockApi.get).toHaveBeenCalledWith('/patients?status=active');
        });

        it('should clear all filters', async () => {
            module.currentFilters.status = 'active';
            module.currentFilters.search = 'john';

            await module.clearFilters();

            expect(module.currentFilters.status).toBe('all');
            expect(module.currentFilters.search).toBe('');
            expect(mockApi.get).toHaveBeenCalledWith('/patients');
        });

        it('should handle archived filter', async () => {
            module.currentFilters.showArchived = true;

            await module.applyFilters();

            expect(mockApi.get).toHaveBeenCalledWith('/patients?includeArchived=true');
        });
    });

    describe('_extractFormData', () => {
        it('should extract basic patient fields', () => {
            const formData = new FormData();
            formData.set('FirstName', 'John');
            formData.set('LastName', 'Doe');
            formData.set('DateOfBirth', '1990-05-15');
            formData.set('Phone', '555-1234');

            const data = module._extractFormData(formData);

            expect(data.FirstName).toBe('John');
            expect(data.LastName).toBe('Doe');
            expect(data.DateOfBirth).toBe('1990-05-15');
            expect(data.Phone).toBe('555-1234');
        });

        it('should extract primary insurance when present', () => {
            const formData = new FormData();
            formData.set('FirstName', 'John');
            formData.set('LastName', 'Doe');
            formData.set('PrimaryInsurance.PayerName', 'Blue Cross');
            formData.set('PrimaryInsurance.PolicyNumber', 'POL123');

            const data = module._extractFormData(formData);

            expect(data.PrimaryInsurance).toBeDefined();
            expect(data.PrimaryInsurance.PayerName).toBe('Blue Cross');
            expect(data.PrimaryInsurance.PolicyNumber).toBe('POL123');
        });
    });

    describe('_getValidationBadge', () => {
        it('should return "No Insurance" for patient without insurance', () => {
            const patient = { Insurances: [] };
            const badge = module._getValidationBadge(patient);
            expect(badge).toContain('No Insurance');
        });

        it('should return "No Primary" for patient without primary insurance', () => {
            const patient = { Insurances: [{ Type: 1 }] };
            const badge = module._getValidationBadge(patient);
            expect(badge).toContain('No Primary');
        });

        it('should return "Validated" for valid authorization', () => {
            const patient = {
                Insurances: [{
                    Type: 0,
                    CurrentAuthorization: {
                        IsExpired: false,
                        RemainingVisits: 10
                    }
                }]
            };
            const badge = module._getValidationBadge(patient);
            expect(badge).toContain('Validated');
        });

        it('should return "Auth Expired" for expired authorization', () => {
            const patient = {
                Insurances: [{
                    Type: 0,
                    CurrentAuthorization: {
                        IsExpired: true
                    }
                }]
            };
            const badge = module._getValidationBadge(patient);
            expect(badge).toContain('Auth Expired');
        });

        it('should return "Low Visits" when visits are low', () => {
            const patient = {
                Insurances: [{
                    Type: 0,
                    CurrentAuthorization: {
                        IsExpired: false,
                        RemainingVisits: 2
                    }
                }]
            };
            const badge = module._getValidationBadge(patient);
            expect(badge).toContain('Low Visits');
        });
    });

    describe('_getStatusBadge', () => {
        it('should return Active for status 0', () => {
            const badge = module._getStatusBadge(0);
            expect(badge).toContain('Active');
            expect(badge).toContain('bg-success');
        });

        it('should return Inactive for status 1', () => {
            const badge = module._getStatusBadge(1);
            expect(badge).toContain('Inactive');
            expect(badge).toContain('bg-warning');
        });

        it('should return Discharged for status 2', () => {
            const badge = module._getStatusBadge(2);
            expect(badge).toContain('Discharged');
            expect(badge).toContain('bg-secondary');
        });
    });

    describe('_escape', () => {
        it('should escape HTML special characters', () => {
            expect(module._escape('<script>alert("xss")</script>'))
                .toBe('&lt;script&gt;alert(&quot;xss&quot;)&lt;/script&gt;');
        });

        it('should handle null and undefined', () => {
            expect(module._escape(null)).toBe('');
            expect(module._escape(undefined)).toBe('');
        });
    });

    describe('_formatDate', () => {
        it('should format valid date', () => {
            const result = module._formatDate('2024-01-15');
            expect(result).toMatch(/\d{1,2}\/\d{1,2}\/\d{4}/);
        });

        it('should handle null/undefined', () => {
            expect(module._formatDate(null)).toBe('-');
            expect(module._formatDate(undefined)).toBe('-');
        });
    });

    describe('destroy', () => {
        beforeEach(async () => {
            mockApi.get.mockResolvedValue(mockPatients);
            await module.init();
            await module.load();
        });

        it('should clean up state', () => {
            module.destroy();

            expect(module.patients).toEqual([]);
            expect(module.currentPatient).toBeNull();
            expect(module.container).toBeNull();
            expect(module.tableBody).toBeNull();
            expect(module.isInitialized).toBe(false);
        });
    });

    describe('event delegation', () => {
        beforeEach(async () => {
            mockApi.get.mockResolvedValue(mockPatients);
            await module.init();
            await module.load();
        });

        it('should handle view button click', async () => {
            mockApi.get.mockResolvedValueOnce(mockPatients).mockResolvedValueOnce(mockPatientDetail);

            const viewBtn = document.querySelector('[data-action="view"]');
            viewBtn.click();

            await new Promise(resolve => setTimeout(resolve, 0));

            expect(mockApi.get).toHaveBeenCalledWith('/patients/1');
        });
    });

    describe('fallback API (without injected api)', () => {
        beforeEach(() => {
            module = new PatientModule({ eventBus: mockEventBus });
            createTestElement('tbody', 'patientsTableBody');
        });

        it('should use fetch when no api provided', async () => {
            mockApiResponse(mockPatients);
            await module.init();

            await module.load();

            expect(global.fetch).toHaveBeenCalledWith(
                '/api/patients',
                expect.objectContaining({
                    headers: expect.any(Object)
                })
            );
        });

        it('should include auth token in headers', async () => {
            localStorage.setItem('authToken', 'test-token');
            mockApiResponse(mockPatients);
            await module.init();

            await module.load();

            expect(global.fetch).toHaveBeenCalledWith(
                '/api/patients',
                expect.objectContaining({
                    headers: expect.objectContaining({
                        Authorization: 'Bearer test-token'
                    })
                })
            );
        });
    });
});
