/**
 * Jest test setup - runs before each test file
 * Sets up browser environment mocks and global utilities
 */

// Mock localStorage
const localStorageMock = (() => {
  let store = {};
  return {
    getItem: jest.fn(key => store[key] || null),
    setItem: jest.fn((key, value) => { store[key] = String(value); }),
    removeItem: jest.fn(key => { delete store[key]; }),
    clear: jest.fn(() => { store = {}; }),
    get length() { return Object.keys(store).length; },
    key: jest.fn(index => Object.keys(store)[index] || null)
  };
})();

Object.defineProperty(window, 'localStorage', {
  value: localStorageMock
});

// Mock sessionStorage
Object.defineProperty(window, 'sessionStorage', {
  value: localStorageMock
});

// Mock fetch API
global.fetch = jest.fn(() =>
  Promise.resolve({
    ok: true,
    json: () => Promise.resolve({}),
    text: () => Promise.resolve(''),
    headers: new Headers()
  })
);

// Mock Bootstrap Modal
class MockBootstrapModal {
  constructor(element) {
    this.element = element;
  }
  show() { this.element?.classList?.add('show'); }
  hide() { this.element?.classList?.remove('show'); }
  static getInstance(element) {
    return element?._mockModalInstance || null;
  }
}

global.bootstrap = {
  Modal: MockBootstrapModal,
  Toast: class {
    show() {}
    hide() {}
  }
};

// Mock jQuery (minimal for Trumbowyg compatibility)
global.$ = global.jQuery = function(selector) {
  const element = typeof selector === 'string'
    ? document.querySelector(selector)
    : selector;

  return {
    length: element ? 1 : 0,
    data: jest.fn(() => null),
    trumbowyg: jest.fn(),
    on: jest.fn(),
    off: jest.fn(),
    val: jest.fn(),
    html: jest.fn(),
    0: element
  };
};

// Mock console methods to suppress noise in tests (optional)
// global.console = {
//   ...console,
//   log: jest.fn(),
//   warn: jest.fn(),
//   error: jest.fn()
// };

// Reset mocks before each test
beforeEach(() => {
  jest.clearAllMocks();
  localStorageMock.clear();
  document.body.innerHTML = '';
});

// Helper function to load a module file
global.loadModule = (modulePath) => {
  const fs = require('fs');
  const path = require('path');
  const fullPath = path.resolve(__dirname, '..', modulePath);
  const code = fs.readFileSync(fullPath, 'utf8');
  eval(code);
};

// Helper to create DOM elements for testing
global.createTestElement = (tag, id, innerHTML = '') => {
  const element = document.createElement(tag);
  if (id) element.id = id;
  if (innerHTML) element.innerHTML = innerHTML;
  document.body.appendChild(element);
  return element;
};

// Helper to create form element
global.createTestForm = (id, fields = []) => {
  const form = document.createElement('form');
  form.id = id;
  fields.forEach(field => {
    const input = document.createElement('input');
    input.name = field.name;
    input.value = field.value || '';
    if (field.type) input.type = field.type;
    form.appendChild(input);
  });
  document.body.appendChild(form);
  return form;
};

// Helper to create modal structure
global.createTestModal = (id) => {
  const modal = document.createElement('div');
  modal.id = id;
  modal.className = 'modal';
  modal.innerHTML = `
    <div class="modal-dialog">
      <div class="modal-content">
        <div class="modal-header">
          <h5 class="modal-title"></h5>
        </div>
        <div class="modal-body"></div>
        <div class="modal-footer"></div>
      </div>
    </div>
  `;
  document.body.appendChild(modal);
  return modal;
};

// Helper to mock API response
global.mockApiResponse = (data, ok = true, status = 200) => {
  global.fetch.mockImplementationOnce(() =>
    Promise.resolve({
      ok,
      status,
      json: () => Promise.resolve(data),
      text: () => Promise.resolve(JSON.stringify(data)),
      headers: new Headers({ 'Content-Type': 'application/json' })
    })
  );
};

// Helper to mock API error
global.mockApiError = (message, status = 500) => {
  global.fetch.mockImplementationOnce(() =>
    Promise.resolve({
      ok: false,
      status,
      json: () => Promise.resolve({ message }),
      text: () => Promise.resolve(message)
    })
  );
};

// Export test utilities
module.exports = {
  localStorageMock,
  MockBootstrapModal
};
