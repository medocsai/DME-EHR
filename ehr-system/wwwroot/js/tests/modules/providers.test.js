/**
 * Unit tests for ProviderModule
 */

// Load the module
const fs = require('fs');
const path = require('path');
const modulePath = path.resolve(__dirname, '../../modules/providers/ProviderModule.js');
const moduleCode = fs.readFileSync(modulePath, 'utf8');
eval(moduleCode);

describe('ProviderModule', () => {
    let module;
    let mockApi;
    let mockEventBus;

    // Sample provider data
    const mockProviders = [
        {
            ProviderId: 1,
            FirstName: 'John',
            LastName: 'Smith',
            FullName: 'John Smith',
            Npi: '1234567890',
            Credentials: 'PT, DPT',
            Specialty: 'Physical Therapy',
            Email: 'john@clinic.com',
            Phone: '555-1234',
            Color: '#2196F3',
            IsActive: true
        },
        {
            ProviderId: 2,
            FirstName: 'Jane',
            LastName: 'Doe',
            FullName: 'Jane Doe',
            Npi: '0987654321',
            Credentials: 'PT',
            Specialty: 'Orthopedic PT',
            Email: 'jane@clinic.com',
            Phone: '555-5678',
            Color: '#4CAF50',
            IsActive: true
        },
        {
            ProviderId: 3,
            FirstName: 'Bob',
            LastName: 'Wilson',
            FullName: 'Bob Wilson',
            Npi: '1122334455',
            Credentials: 'PT',
            Specialty: 'Sports PT',
            IsActive: false
        }
    ];

    const mockProviderDetail = {
        ...mockProviders[0],
        Taxonomy: '225100000X',
        LicenseNumber: 'PT12345',
        LicenseState: 'CA',
        LicenseExpiry: '2025-12-31',
        DefaultAppointmentDuration: 45,
        ProviderSchedules: [
            { DayOfWeek: 1, StartTime: '09:00', EndTime: '17:00', IsAvailable: true },
            { DayOfWeek: 2, StartTime: '09:00', EndTime: '17:00', IsAvailable: true },
            { DayOfWeek: 3, StartTime: '09:00', EndTime: '17:00', IsAvailable: true }
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
        createTestElement('div', 'providersGrid');
        createTestElement('div', 'providerDetailContent');
        createTestElement('form', 'providerForm');
        createTestElement('input', 'providerId');
        createTestElement('select', 'providerStatusFilter');
        createTestElement('input', 'signatureFileInput');
        createTestElement('div', 'signaturePreview');
        createTestElement('div', 'noSignatureMessage');
        createTestElement('img', 'signatureImage');
        createTestElement('button', 'deleteSignatureBtn');
        createTestElement('div', 'providerUserInfo');

        // Create schedule inputs for Monday
        const scheduleContainer = document.createElement('div');
        scheduleContainer.innerHTML = `
            <input type="checkbox" class="schedule-available" data-day="1" checked>
            <input type="time" class="schedule-start" data-day="1" value="08:00">
            <input type="time" class="schedule-end" data-day="1" value="17:00">
        `;
        document.body.appendChild(scheduleContainer);

        // Create module with mocks
        module = new ProviderModule({
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
            expect(module.providers).toEqual([]);
            expect(module.currentProvider).toBeNull();
            expect(module.currentProviderId).toBeNull();
            expect(module.pendingSignatureFile).toBeNull();
            expect(module.isNewProviderMode).toBe(false);
            expect(module.isInitialized).toBe(false);
            expect(module.api).toBe(mockApi);
            expect(module.eventBus).toBe(mockEventBus);
        });

        it('should work without options', () => {
            const defaultModule = new ProviderModule();
            expect(defaultModule.api).toBeNull();
            expect(defaultModule.eventBus).toBeNull();
        });

        it('should have default schedule configuration', () => {
            expect(module.defaultSchedule).toBeDefined();
            expect(module.defaultSchedule[0].available).toBe(false); // Sunday
            expect(module.defaultSchedule[1].available).toBe(true);  // Monday
            expect(module.defaultSchedule[6].available).toBe(false); // Saturday
        });
    });

    describe('init', () => {
        it('should warn if container not found', async () => {
            document.body.innerHTML = '';
            const warnSpy = jest.spyOn(console, 'warn');

            await module.init();

            expect(warnSpy).toHaveBeenCalledWith('[ProviderModule] Container not found');
            expect(module.isInitialized).toBe(false);
        });

        it('should initialize with grid', async () => {
            await module.init();

            expect(module.isInitialized).toBe(true);
            expect(mockEventBus.emit).toHaveBeenCalledWith('providers:initialized');
        });
    });

    describe('load', () => {
        beforeEach(async () => {
            await module.init();
        });

        it('should load providers from API', async () => {
            mockApi.get.mockResolvedValue(mockProviders);

            await module.load();

            expect(mockApi.get).toHaveBeenCalledWith('/providers');
            expect(module.providers).toEqual(mockProviders);
        });

        it('should render providers to grid', async () => {
            mockApi.get.mockResolvedValue(mockProviders);

            await module.load();

            const grid = document.getElementById('providersGrid');
            expect(grid.innerHTML).toContain('John Smith');
            expect(grid.innerHTML).toContain('Jane Doe');
        });

        it('should emit loaded event', async () => {
            mockApi.get.mockResolvedValue(mockProviders);

            await module.load();

            expect(mockEventBus.emit).toHaveBeenCalledWith('providers:loaded', {
                providers: mockProviders
            });
        });

        it('should handle empty providers', async () => {
            mockApi.get.mockResolvedValue([]);

            await module.load();

            const grid = document.getElementById('providersGrid');
            expect(grid.innerHTML).toContain('No providers found');
        });

        it('should filter by active status', async () => {
            mockApi.get.mockResolvedValue(mockProviders);

            await module.load(true);

            expect(mockApi.get).toHaveBeenCalledWith('/providers?activeOnly=true');
        });

        it('should handle API errors', async () => {
            mockApi.get.mockRejectedValue(new Error('Network error'));

            await expect(module.load()).rejects.toThrow('Network error');
        });
    });

    describe('view', () => {
        beforeEach(async () => {
            await module.init();
        });

        it('should load and display provider details', async () => {
            mockApi.get.mockResolvedValue(mockProviderDetail);

            await module.view(1);

            expect(mockApi.get).toHaveBeenCalledWith('/providers/1');
            expect(module.currentProvider).toEqual(mockProviderDetail);
            expect(module.currentProviderId).toBe(1);
        });

        it('should emit viewed event', async () => {
            mockApi.get.mockResolvedValue(mockProviderDetail);

            await module.view(1);

            expect(mockEventBus.emit).toHaveBeenCalledWith('providers:viewed', {
                provider: mockProviderDetail
            });
        });

        it('should render provider modal content', async () => {
            mockApi.get.mockResolvedValue(mockProviderDetail);

            await module.view(1);

            const content = document.getElementById('providerDetailContent');
            expect(content.innerHTML).toContain('John');
            expect(content.innerHTML).toContain('Smith');
            expect(content.innerHTML).toContain('1234567890');
        });
    });

    describe('edit', () => {
        beforeEach(async () => {
            await module.init();
            // Add form fields
            const form = document.getElementById('providerForm');
            form.innerHTML = `
                <input name="FirstName">
                <input name="LastName">
                <input name="Npi">
                <input name="Email">
                <input name="IsActive" type="checkbox">
            `;
        });

        it('should load provider data for editing', async () => {
            mockApi.get.mockResolvedValue(mockProviderDetail);

            await module.edit(1);

            expect(mockApi.get).toHaveBeenCalledWith('/providers/1');
            expect(module.currentProviderId).toBe(1);
            expect(module.isNewProviderMode).toBe(false);
        });

        it('should emit editing event', async () => {
            mockApi.get.mockResolvedValue(mockProviderDetail);

            await module.edit(1);

            expect(mockEventBus.emit).toHaveBeenCalledWith('providers:editing', {
                provider: mockProviderDetail
            });
        });
    });

    describe('openNewForm', () => {
        beforeEach(async () => {
            await module.init();
        });

        it('should reset state for new provider', () => {
            module.currentProvider = mockProviderDetail;
            module.currentProviderId = 1;

            module.openNewForm();

            expect(module.currentProvider).toBeNull();
            expect(module.currentProviderId).toBeNull();
            expect(module.isNewProviderMode).toBe(true);
            expect(module.pendingSignatureFile).toBeNull();
        });
    });

    describe('delete', () => {
        beforeEach(async () => {
            await module.init();
            mockApi.get.mockResolvedValue(mockProviders);
        });

        it('should deactivate provider after confirmation', async () => {
            global.ConfirmDialog = { show: jest.fn().mockResolvedValue(true) };
            mockApi.delete.mockResolvedValue({});

            await module.delete(1);

            expect(mockApi.delete).toHaveBeenCalledWith('/providers/1');
            expect(mockEventBus.emit).toHaveBeenCalledWith('providers:deleted', {
                providerId: 1
            });
        });

        it('should not delete if user cancels', async () => {
            global.ConfirmDialog = { show: jest.fn().mockResolvedValue(false) };

            await module.delete(1);

            expect(mockApi.delete).not.toHaveBeenCalled();
        });
    });

    describe('_extractFormData', () => {
        it('should extract basic provider fields', () => {
            const formData = new FormData();
            formData.set('FirstName', 'John');
            formData.set('LastName', 'Smith');
            formData.set('Npi', '1234567890');
            formData.set('Email', 'john@clinic.com');
            formData.set('IsActive', 'on');
            formData.set('DefaultAppointmentDuration', '45');
            formData.set('Color', '#2196F3');

            const data = module._extractFormData(formData, false);

            expect(data.FirstName).toBe('John');
            expect(data.LastName).toBe('Smith');
            expect(data.Npi).toBe('1234567890');
            expect(data.Email).toBe('john@clinic.com');
            expect(data.IsActive).toBe(true);
            expect(data.DefaultAppointmentDuration).toBe(45);
        });

        it('should include password for new providers', () => {
            const formData = new FormData();
            formData.set('FirstName', 'John');
            formData.set('LastName', 'Smith');
            formData.set('Npi', '1234567890');
            formData.set('Password', 'securepass123');

            const data = module._extractFormData(formData, false);

            expect(data.Password).toBe('securepass123');
        });

        it('should not include password for edit', () => {
            const formData = new FormData();
            formData.set('FirstName', 'John');
            formData.set('LastName', 'Smith');
            formData.set('Npi', '1234567890');
            formData.set('Password', 'securepass123');

            const data = module._extractFormData(formData, true);

            expect(data.Password).toBeUndefined();
        });

        it('should include work schedule', () => {
            const formData = new FormData();
            formData.set('FirstName', 'John');
            formData.set('LastName', 'Smith');
            formData.set('Npi', '1234567890');

            const data = module._extractFormData(formData, false);

            expect(data.WorkSchedule).toBeDefined();
            expect(Array.isArray(data.WorkSchedule)).toBe(true);
        });
    });

    describe('_collectScheduleFromForm', () => {
        it('should collect schedule from form inputs', () => {
            const schedules = module._collectScheduleFromForm();

            expect(schedules.length).toBeGreaterThan(0);
            const monday = schedules.find(s => s.DayOfWeek === 1);
            expect(monday).toBeDefined();
            expect(monday.StartTime).toBe('08:00');
            expect(monday.EndTime).toBe('17:00');
            expect(monday.IsAvailable).toBe(true);
        });
    });

    describe('_formatTimeForInput', () => {
        it('should format time correctly', () => {
            expect(module._formatTimeForInput('09:00:00')).toBe('09:00');
            expect(module._formatTimeForInput('9:00')).toBe('09:00');
            expect(module._formatTimeForInput('17:30')).toBe('17:30');
        });

        it('should return default for invalid input', () => {
            expect(module._formatTimeForInput(null)).toBe('08:00');
            expect(module._formatTimeForInput('')).toBe('08:00');
            expect(module._formatTimeForInput('invalid')).toBe('08:00');
        });
    });

    describe('signature handling', () => {
        beforeEach(async () => {
            await module.init();
        });

        it('should validate file size', () => {
            const largeFile = new File([new ArrayBuffer(3 * 1024 * 1024)], 'signature.jpg', { type: 'image/jpeg' });
            global.Toast = { error: jest.fn() };

            module._handleSignatureSelect(largeFile);

            expect(Toast.error).toHaveBeenCalled();
            expect(module.pendingSignatureFile).toBeNull();
        });

        it('should validate file type', () => {
            const pdfFile = new File(['test'], 'signature.pdf', { type: 'application/pdf' });
            global.Toast = { error: jest.fn() };

            module._handleSignatureSelect(pdfFile);

            expect(Toast.error).toHaveBeenCalled();
        });

        it('should store pending file for new providers', () => {
            module.isNewProviderMode = true;
            const validFile = new File(['test'], 'signature.png', { type: 'image/png' });

            module._handleSignatureSelect(validFile);

            expect(module.pendingSignatureFile).toBe(validFile);
        });
    });

    describe('deleteSignature', () => {
        beforeEach(async () => {
            await module.init();
        });

        it('should clear pending file for new providers', async () => {
            module.isNewProviderMode = true;
            module.pendingSignatureFile = new File(['test'], 'sig.png', { type: 'image/png' });
            global.Toast = { success: jest.fn() };

            await module.deleteSignature();

            expect(module.pendingSignatureFile).toBeNull();
        });

        it('should call API delete for existing providers', async () => {
            module.currentProviderId = 1;
            module.isNewProviderMode = false;
            global.ConfirmDialog = { show: jest.fn().mockResolvedValue(true) };
            mockApi.delete.mockResolvedValue({});
            // Mock fetch for signature reload
            mockApiResponse({}, false);

            await module.deleteSignature();

            expect(mockApi.delete).toHaveBeenCalledWith('/providers/1/signature');
        });
    });

    describe('_renderProviderCard', () => {
        it('should render provider card with credentials', () => {
            const html = module._renderProviderCard(mockProviders[0]);

            expect(html).toContain('John Smith');
            expect(html).toContain('PT');
            expect(html).toContain('DPT');
            expect(html).toContain('#2196F3');
        });

        it('should show inactive badge for inactive providers', () => {
            const html = module._renderProviderCard(mockProviders[2]);

            expect(html).toContain('Inactive');
            expect(html).toContain('opacity: 0.5');
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
            const result = module._formatDate('2024-12-31');
            expect(result).toMatch(/\d{1,2}\/\d{1,2}\/\d{4}/);
        });

        it('should handle null/undefined', () => {
            expect(module._formatDate(null)).toBe('-');
            expect(module._formatDate(undefined)).toBe('-');
        });
    });

    describe('destroy', () => {
        beforeEach(async () => {
            mockApi.get.mockResolvedValue(mockProviders);
            await module.init();
            await module.load();
        });

        it('should clean up state', () => {
            module.destroy();

            expect(module.providers).toEqual([]);
            expect(module.currentProvider).toBeNull();
            expect(module.currentProviderId).toBeNull();
            expect(module.grid).toBeNull();
            expect(module.isInitialized).toBe(false);
        });
    });

    describe('fallback API (without injected api)', () => {
        beforeEach(() => {
            module = new ProviderModule({ eventBus: mockEventBus });
            createTestElement('div', 'providersGrid');
        });

        it('should use fetch when no api provided', async () => {
            mockApiResponse(mockProviders);
            await module.init();

            await module.load();

            expect(global.fetch).toHaveBeenCalledWith(
                '/api/providers',
                expect.objectContaining({
                    headers: expect.any(Object)
                })
            );
        });

        it('should include auth token in headers', async () => {
            localStorage.setItem('authToken', 'test-token');
            mockApiResponse(mockProviders);
            await module.init();

            await module.load();

            expect(global.fetch).toHaveBeenCalledWith(
                '/api/providers',
                expect.objectContaining({
                    headers: expect.objectContaining({
                        Authorization: 'Bearer test-token'
                    })
                })
            );
        });
    });
});
