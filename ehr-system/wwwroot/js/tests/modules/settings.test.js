/**
 * Unit tests for SettingsModule
 */

// Load the module
const fs = require('fs');
const path = require('path');
const modulePath = path.resolve(__dirname, '../../modules/settings/SettingsModule.js');
const moduleCode = fs.readFileSync(modulePath, 'utf8');
eval(moduleCode);

describe('SettingsModule', () => {
    let module;
    let mockApi;
    let mockEventBus;

    // Sample settings data
    const mockSettings = [
        { SettingKey: 'SessionTimeout', SettingValue: '30', Category: 'Security', DataType: 'int' },
        { SettingKey: 'MaxLoginAttempts', SettingValue: '5', Category: 'Security', DataType: 'int' },
        { SettingKey: 'CompanyName', SettingValue: 'Test Clinic', Category: 'General' },
        { SettingKey: 'SupportEmail', SettingValue: 'support@test.com', Category: 'General' }
    ];

    beforeEach(() => {
        // Create mock API
        mockApi = {
            get: jest.fn(),
            put: jest.fn(),
            post: jest.fn()
        };

        // Create mock EventBus
        mockEventBus = {
            emit: jest.fn()
        };

        // Create container element
        createTestElement('div', 'settingsContainer');

        // Create module with mocks
        module = new SettingsModule({
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
            expect(module.settings).toEqual([]);
            expect(module.groupedSettings).toBeInstanceOf(Map);
            expect(module.isInitialized).toBe(false);
            expect(module.api).toBe(mockApi);
            expect(module.eventBus).toBe(mockEventBus);
        });

        it('should work without options', () => {
            const defaultModule = new SettingsModule();
            expect(defaultModule.api).toBeNull();
            expect(defaultModule.eventBus).toBeNull();
        });
    });

    describe('init', () => {
        it('should warn if container not found', async () => {
            document.body.innerHTML = '';
            const warnSpy = jest.spyOn(console, 'warn');

            await module.init();

            expect(warnSpy).toHaveBeenCalledWith(
                '[SettingsModule] Container #settingsContainer not found'
            );
            expect(module.isInitialized).toBe(false);
        });

        it('should initialize and load settings', async () => {
            mockApi.get.mockResolvedValue(mockSettings);

            await module.init();

            expect(mockApi.get).toHaveBeenCalledWith('/settings');
            expect(module.settings).toEqual(mockSettings);
            expect(module.isInitialized).toBe(true);
        });

        it('should emit initialized event', async () => {
            mockApi.get.mockResolvedValue(mockSettings);

            await module.init();

            expect(mockEventBus.emit).toHaveBeenCalledWith('settings:initialized');
        });
    });

    describe('load', () => {
        beforeEach(async () => {
            mockApi.get.mockResolvedValue(mockSettings);
            await module.init();
        });

        it('should load and group settings by category', async () => {
            expect(module.groupedSettings.has('Security')).toBe(true);
            expect(module.groupedSettings.has('General')).toBe(true);
            expect(module.groupedSettings.get('Security').length).toBe(2);
            expect(module.groupedSettings.get('General').length).toBe(2);
        });

        it('should emit loaded event with settings', async () => {
            expect(mockEventBus.emit).toHaveBeenCalledWith('settings:loaded', {
                settings: mockSettings
            });
        });

        it('should render settings to container', async () => {
            const container = document.getElementById('settingsContainer');
            expect(container.innerHTML).toContain('Security');
            expect(container.innerHTML).toContain('General');
            expect(container.innerHTML).toContain('Session Timeout');
        });

        it('should handle empty settings', async () => {
            mockApi.get.mockResolvedValue([]);
            await module.load();

            const container = document.getElementById('settingsContainer');
            expect(container.innerHTML).toContain('No settings found');
        });

        it('should handle API errors', async () => {
            mockApi.get.mockRejectedValue(new Error('Network error'));

            await expect(module.load()).rejects.toThrow('Network error');
        });
    });

    describe('save', () => {
        beforeEach(async () => {
            mockApi.get.mockResolvedValue(mockSettings);
            await module.init();
        });

        it('should save setting with new value', async () => {
            mockApi.put.mockResolvedValue({});

            // Change the input value
            const input = document.getElementById('setting-SessionTimeout');
            input.value = '60';

            await module.save('SessionTimeout');

            expect(mockApi.put).toHaveBeenCalledWith('/settings/SessionTimeout', {
                SettingValue: '60'
            });
        });

        it('should skip save if value unchanged', async () => {
            // Value matches original
            const input = document.getElementById('setting-SessionTimeout');
            input.value = '30';
            input.dataset.original = '30';

            await module.save('SessionTimeout');

            expect(mockApi.put).not.toHaveBeenCalled();
        });

        it('should emit saved event on success', async () => {
            mockApi.put.mockResolvedValue({});

            const input = document.getElementById('setting-SessionTimeout');
            input.value = '60';

            await module.save('SessionTimeout');

            expect(mockEventBus.emit).toHaveBeenCalledWith('settings:saved', {
                key: 'SessionTimeout',
                value: '60'
            });
        });

        it('should update local state after save', async () => {
            mockApi.put.mockResolvedValue({});

            const input = document.getElementById('setting-SessionTimeout');
            input.value = '60';

            await module.save('SessionTimeout');

            const setting = module.settings.find(s => s.SettingKey === 'SessionTimeout');
            expect(setting.SettingValue).toBe('60');
        });

        it('should handle missing input', async () => {
            const errorSpy = jest.spyOn(console, 'error');

            await module.save('NonExistentKey');

            expect(errorSpy).toHaveBeenCalledWith(
                '[SettingsModule] Input not found for key:',
                'NonExistentKey'
            );
        });

        it('should handle API errors', async () => {
            mockApi.put.mockRejectedValue(new Error('Save failed'));

            const input = document.getElementById('setting-SessionTimeout');
            input.value = '60';

            await expect(module.save('SessionTimeout')).rejects.toThrow('Save failed');
        });
    });

    describe('getValue', () => {
        beforeEach(async () => {
            mockApi.get.mockResolvedValue(mockSettings);
            await module.init();
        });

        it('should return value for existing key', () => {
            expect(module.getValue('SessionTimeout')).toBe('30');
            expect(module.getValue('CompanyName')).toBe('Test Clinic');
        });

        it('should return null for non-existent key', () => {
            expect(module.getValue('NonExistentKey')).toBeNull();
        });
    });

    describe('initializeDefaults', () => {
        beforeEach(async () => {
            mockApi.get.mockResolvedValue([]);
            await module.init();
        });

        it('should call API to initialize defaults', async () => {
            mockApi.post.mockResolvedValue({});
            mockApi.get.mockResolvedValue(mockSettings);

            // Mock confirmation
            global.ConfirmDialog = { show: jest.fn().mockResolvedValue(true) };

            await module.initializeDefaults();

            expect(mockApi.post).toHaveBeenCalledWith('/settings/initialize');
        });

        it('should reload settings after initialization', async () => {
            mockApi.post.mockResolvedValue({});
            mockApi.get.mockResolvedValue(mockSettings);
            global.ConfirmDialog = { show: jest.fn().mockResolvedValue(true) };

            await module.initializeDefaults();

            // Should have called get twice - once in init, once after initialize
            expect(mockApi.get).toHaveBeenCalledTimes(2);
        });

        it('should not proceed if user cancels', async () => {
            global.ConfirmDialog = { show: jest.fn().mockResolvedValue(false) };

            await module.initializeDefaults();

            expect(mockApi.post).not.toHaveBeenCalled();
        });
    });

    describe('_getInputType', () => {
        it('should return number for int data type', () => {
            const setting = { DataType: 'int' };
            expect(module._getInputType(setting)).toBe('number');
        });

        it('should return password for password keys', () => {
            const setting = { SettingKey: 'AdminPassword' };
            expect(module._getInputType(setting)).toBe('password');
        });

        it('should return email for email keys', () => {
            const setting = { SettingKey: 'SupportEmail' };
            expect(module._getInputType(setting)).toBe('email');
        });

        it('should return text by default', () => {
            const setting = { SettingKey: 'CompanyName' };
            expect(module._getInputType(setting)).toBe('text');
        });
    });

    describe('_formatLabel', () => {
        it('should convert PascalCase to spaced words', () => {
            expect(module._formatLabel('SessionTimeout')).toBe('Session Timeout');
            expect(module._formatLabel('MaxLoginAttempts')).toBe('Max Login Attempts');
        });

        it('should handle single word', () => {
            expect(module._formatLabel('Timeout')).toBe('Timeout');
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

        it('should convert numbers to string', () => {
            expect(module._escape(123)).toBe('123');
        });
    });

    describe('destroy', () => {
        beforeEach(async () => {
            mockApi.get.mockResolvedValue(mockSettings);
            await module.init();
        });

        it('should clean up state', () => {
            module.destroy();

            expect(module.settings).toEqual([]);
            expect(module.groupedSettings.size).toBe(0);
            expect(module.container).toBeNull();
            expect(module.isInitialized).toBe(false);
        });
    });

    describe('event delegation', () => {
        beforeEach(async () => {
            mockApi.get.mockResolvedValue(mockSettings);
            await module.init();
        });

        it('should handle save button clicks', async () => {
            mockApi.put.mockResolvedValue({});

            // Change value
            const input = document.getElementById('setting-SessionTimeout');
            input.value = '60';

            // Find and click save button
            const saveBtn = document.querySelector('[data-key="SessionTimeout"].btn-save-setting');
            saveBtn.click();

            // Wait for async operation
            await new Promise(resolve => setTimeout(resolve, 0));

            expect(mockApi.put).toHaveBeenCalledWith('/settings/SessionTimeout', {
                SettingValue: '60'
            });
        });
    });

    describe('fallback API (without injected api)', () => {
        beforeEach(() => {
            // Create module without API injection
            module = new SettingsModule({ eventBus: mockEventBus });
            createTestElement('div', 'settingsContainer');
        });

        it('should use fetch when no api provided', async () => {
            mockApiResponse(mockSettings);

            await module.init();

            expect(global.fetch).toHaveBeenCalledWith(
                '/api/settings',
                expect.objectContaining({
                    headers: expect.any(Object)
                })
            );
        });

        it('should include auth token in headers', async () => {
            localStorage.setItem('authToken', 'test-token');
            mockApiResponse(mockSettings);

            await module.init();

            expect(global.fetch).toHaveBeenCalledWith(
                '/api/settings',
                expect.objectContaining({
                    headers: expect.objectContaining({
                        Authorization: 'Bearer test-token'
                    })
                })
            );
        });
    });
});
