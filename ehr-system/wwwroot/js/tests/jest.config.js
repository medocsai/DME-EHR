/**
 * Jest configuration for PT EHR JavaScript module tests
 */
module.exports = {
  // Use jsdom for browser environment simulation
  testEnvironment: 'jsdom',

  // Root directory for tests
  rootDir: '..',

  // Test file patterns
  testMatch: [
    '<rootDir>/tests/**/*.test.js',
    '<rootDir>/tests/**/*.spec.js'
  ],

  // Setup files to run before each test
  setupFilesAfterEnv: ['<rootDir>/tests/setup.js'],

  // Module paths for imports
  moduleDirectories: ['node_modules', '<rootDir>'],

  // Coverage configuration
  collectCoverageFrom: [
    '<rootDir>/core/**/*.js',
    '<rootDir>/shared/**/*.js',
    '<rootDir>/modules/**/*.js',
    '!<rootDir>/tests/**'
  ],

  // Coverage thresholds
  coverageThreshold: {
    global: {
      branches: 70,
      functions: 80,
      lines: 80,
      statements: 80
    }
  },

  // Transform settings (no transform needed for vanilla JS)
  transform: {},

  // Verbose output
  verbose: true,

  // Clear mocks between tests
  clearMocks: true,

  // Restore mocks automatically
  restoreMocks: true
};
