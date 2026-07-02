/**
 * TemplatesModule - Clinical Note Templates management
 * Handles loading, filtering, creating, editing, and deleting templates
 *
 * Usage:
 *   const templatesModule = new TemplatesModule();
 *   templatesModule.init();
 *
 * Or via App:
 *   App.modules.register('templates', new TemplatesModule());
 */
class TemplatesModule {
    constructor() {
        // State
        this.templates = [];
        this.tenants = [];

        // DOM references
        this.container = null;
        this.tableBody = null;
        this.templateModal = null;
        this.templateForm = null;

        // Bound methods
        this.handleFormSubmit = this.handleFormSubmit.bind(this);
        this.handleModalShow = this.handleModalShow.bind(this);
        this.handleModalHidden = this.handleModalHidden.bind(this);
        this.handleTenantChange = this.handleTenantChange.bind(this);
        this.handleFilterChange = this.handleFilterChange.bind(this);
    }

    /**
     * Initialize the module
     */
    init() {
        this.container = document.getElementById('templatesPage');
        this.tableBody = this.container?.querySelector('tbody');
        this.templateModal = document.getElementById('templateModal');
        this.templateForm = document.getElementById('templateForm');

        if (!this.container) {
            console.warn('[TemplatesModule] Container not found');
            return;
        }

        this.bindEvents();
        this.load();

        // Register with App if available
        if (window.App && window.App.modules) {
            App.modules.register('templates', this);
        }

        console.log('[TemplatesModule] Initialized');
    }

    /**
     * Bind event handlers
     */
    bindEvents() {
        // Form submission
        if (this.templateForm) {
            this.templateForm.addEventListener('submit', this.handleFormSubmit);
        }

        // Modal events
        if (this.templateModal) {
            this.templateModal.addEventListener('show.bs.modal', this.handleModalShow);
            this.templateModal.addEventListener('hidden.bs.modal', this.handleModalHidden);
        }

        // Filter inputs
        const searchInput = document.getElementById('templateSearchInput');
        const clinicFilter = document.getElementById('templateClinicFilter');

        if (searchInput) {
            searchInput.addEventListener('input', DomUtils.debounce(this.handleFilterChange, 300));
        }
        if (clinicFilter) {
            clinicFilter.addEventListener('change', this.handleFilterChange);
        }

        // Tenant select change (for loading locations)
        const tenantSelect = document.getElementById('templateTenantSelect');
        if (tenantSelect && !tenantSelect.hasAttribute('data-change-handler')) {
            tenantSelect.setAttribute('data-change-handler', 'true');
            tenantSelect.addEventListener('change', this.handleTenantChange);
        }
    }

    /**
     * Load templates from API
     */
    async load() {
        try {
            const apiGet = window.App?.api
                ? (url) => App.api.get(url)
                : (url) => apiRequest(url);

            this.templates = await apiGet('/clinical-note-templates?activeOnly=false') || [];

            // Populate clinic filter dropdown
            this.populateClinicFilter();

            // Apply filters and render
            this.render();

        } catch (error) {
            console.error('[TemplatesModule] Failed to load templates:', error);
            Toast.error('Error', 'Failed to load templates: ' + error.message);
        }
    }

    /**
     * Populate clinic filter dropdown
     */
    populateClinicFilter() {
        const clinicFilter = document.getElementById('templateClinicFilter');
        if (!clinicFilter) return;

        // Get unique clinics from templates
        const clinics = new Map();
        this.templates.forEach(t => {
            if (t.TenantId && t.TenantName) {
                clinics.set(t.TenantId, t.TenantName);
            }
        });

        // Build options
        let options = '<option value="">All Clinics</option>';
        options += '<option value="system">System Templates (All Clinics)</option>';

        // Sort clinics by name
        const sortedClinics = Array.from(clinics.entries()).sort((a, b) => a[1].localeCompare(b[1]));
        sortedClinics.forEach(([id, name]) => {
            options += `<option value="${id}">${StringUtils.escape(name)}</option>`;
        });

        clinicFilter.innerHTML = options;
    }

    /**
     * Handle filter change
     */
    handleFilterChange() {
        this.render();
    }

    /**
     * Render templates table
     */
    render() {
        if (!this.tableBody) return;

        const searchTerm = (document.getElementById('templateSearchInput')?.value || '').toLowerCase().trim();
        const clinicFilter = document.getElementById('templateClinicFilter')?.value || '';

        // Filter templates
        const filteredTemplates = this.templates.filter(t => {
            // Search filter (name)
            if (searchTerm && !t.Name.toLowerCase().includes(searchTerm)) {
                return false;
            }

            // Clinic filter
            if (clinicFilter) {
                if (clinicFilter === 'system') {
                    if (t.TenantId) return false; // Only show system templates (no TenantId)
                } else {
                    if (String(t.TenantId) !== clinicFilter) return false;
                }
            }

            return true;
        });

        if (!filteredTemplates.length) {
            this.tableBody.innerHTML = '<tr><td colspan="6" class="text-center text-muted">No templates found</td></tr>';
            return;
        }

        this.tableBody.innerHTML = filteredTemplates.map(t => `
            <tr>
                <td><strong>${StringUtils.escape(t.Name)}</strong></td>
                <td>${t.TenantName ? StringUtils.escape(t.TenantName) : '<span class="text-muted">All Clinics</span>'}</td>
                <td>${t.LocationName ? StringUtils.escape(t.LocationName) : '<span class="text-muted">All Locations</span>'}</td>
                <td>${t.IsSystemTemplate ? '<span class="badge bg-info">System</span>' : '<span class="badge bg-secondary">Custom</span>'}</td>
                <td>
                    <span class="badge ${t.IsActive ? 'bg-success' : 'bg-secondary'}">
                        ${t.IsActive ? 'Active' : 'Inactive'}
                    </span>
                </td>
                <td>
                    <button class="btn btn-sm btn-outline-primary" onclick="templatesModule.edit(${t.TemplateId})">
                        <i class="bi bi-pencil"></i>
                    </button>
                    <button class="btn btn-sm btn-outline-danger" onclick="templatesModule.delete(${t.TemplateId})">
                        <i class="bi bi-trash"></i>
                    </button>
                </td>
            </tr>
        `).join('');
    }

    /**
     * Clear all filters
     */
    clearFilters() {
        const searchInput = document.getElementById('templateSearchInput');
        const clinicFilter = document.getElementById('templateClinicFilter');

        if (searchInput) searchInput.value = '';
        if (clinicFilter) clinicFilter.value = '';

        this.render();
    }

    /**
     * Edit a template
     * @param {number} templateId - Template ID
     */
    async edit(templateId) {
        try {
            const apiGet = window.App?.api
                ? (url) => App.api.get(url)
                : (url) => apiRequest(url);

            const template = await apiGet(`/clinical-note-templates/${templateId}`);

            if (!this.templateForm) return;

            this.templateForm.querySelector('[name="TemplateId"]').value = template.TemplateId;
            this.templateForm.querySelector('[name="Name"]').value = template.Name;
            this.templateForm.querySelector('[name="SortOrder"]').value = template.SortOrder || 0;
            this.templateForm.querySelector('[name="IsActive"]').checked = template.IsActive;

            // Set HTML content
            this.templateForm.querySelector('[name="HtmlContent"]').value = template.HtmlContent || '';

            // Set TenantId and LocationId for Super Admin
            const currentUserObj = window.App?.getCurrentUser() || window.currentUser;
            if (currentUserObj?.Role === 0 && !currentUserObj?.TenantId) {
                const tenantSelect = document.getElementById('templateTenantSelect');
                const locationSelect = document.getElementById('templateLocationSelect');

                if (tenantSelect && locationSelect) {
                    // Load tenant options if not loaded
                    if (tenantSelect.options.length <= 1) {
                        try {
                            const tenants = await apiGet('/tenants');
                            tenantSelect.innerHTML = '<option value="">System Template (All Clinics)</option>' +
                                (tenants?.map(t => `<option value="${t.TenantId}">${StringUtils.escape(t.Name)}</option>`).join('') || '');
                        } catch (error) {
                            console.error('[TemplatesModule] Error loading tenants:', error);
                        }
                    }

                    // Set tenant value
                    tenantSelect.value = template.TenantId || '';

                    // If template has a TenantId, load locations
                    if (template.TenantId) {
                        try {
                            const locations = await apiGet(`/locations?tenantId=${template.TenantId}`);
                            locationSelect.innerHTML = '<option value="">All Locations in Clinic</option>' +
                                (locations?.map(l => `<option value="${l.LocationId}">${StringUtils.escape(l.Name)}</option>`).join('') || '');
                            locationSelect.disabled = false;

                            if (template.LocationId) {
                                locationSelect.value = template.LocationId;
                            }
                        } catch (error) {
                            console.error('[TemplatesModule] Error loading locations:', error);
                            locationSelect.innerHTML = '<option value="">Error loading locations</option>';
                            locationSelect.disabled = true;
                        }
                    } else {
                        locationSelect.innerHTML = '<option value="">All Locations (Select Clinic First)</option>';
                        locationSelect.disabled = true;
                    }
                }
            }

            document.querySelector('#templateModal .modal-title').textContent = 'Edit Template';

            const modal = new bootstrap.Modal(this.templateModal);
            modal.show();

            // Set Trumbowyg content after modal is shown
            setTimeout(() => {
                const editor = $('#templateHtmlContent');
                if (editor.data('trumbowyg')) {
                    editor.trumbowyg('html', template.HtmlContent || '');
                }
            }, 100);

        } catch (error) {
            console.error('[TemplatesModule] Failed to edit template:', error);
            Toast.error('Error', 'Failed to load template for editing');
        }
    }

    /**
     * Delete a template
     * @param {number} templateId - Template ID
     */
    async delete(templateId) {
        const confirmed = await ConfirmDialog.show({
            title: 'Delete Template',
            message: 'Are you sure you want to delete this template?',
            confirmText: 'Delete',
            confirmClass: 'btn-danger',
            headerClass: 'bg-danger text-white'
        });
        if (!confirmed) return;

        try {
            if (window.App?.api) {
                await App.api.delete(`/clinical-note-templates/${templateId}`);
            } else {
                await apiRequest(`/clinical-note-templates/${templateId}`, { method: 'DELETE' });
            }

            Toast.success('Success', 'Template deleted successfully');
            this.load();

        } catch (error) {
            console.error('[TemplatesModule] Failed to delete template:', error);
            Toast.error('Error', 'Failed to delete template: ' + error.message);
        }
    }

    /**
     * Handle form submission
     * @param {Event} e - Submit event
     */
    async handleFormSubmit(e) {
        e.preventDefault();

        const formData = new FormData(e.target);
        const templateId = formData.get('TemplateId');

        // Get HTML content from Trumbowyg editor
        const editor = $('#templateHtmlContent');
        const htmlContent = editor.data('trumbowyg') ? editor.trumbowyg('html') : formData.get('HtmlContent');

        const data = {
            Name: formData.get('Name'),
            HtmlContent: htmlContent,
            SortOrder: parseInt(formData.get('SortOrder')) || 0,
            IsActive: formData.get('IsActive') === 'on'
        };

        // Add TenantId for Super Admin
        const tenantSelect = document.getElementById('templateTenantSelect');
        if (tenantSelect) {
            data.TenantId = tenantSelect.value ? parseInt(tenantSelect.value) : null;
        }

        // Add LocationId
        const locationSelect = document.getElementById('templateLocationSelect');
        if (locationSelect) {
            data.LocationId = locationSelect.value ? parseInt(locationSelect.value) : null;
        }

        try {
            const isEdit = templateId && templateId !== '';

            if (window.App?.api) {
                if (isEdit) {
                    await App.api.put(`/clinical-note-templates/${templateId}`, data);
                } else {
                    await App.api.post('/clinical-note-templates', data);
                }
            } else {
                if (isEdit) {
                    await apiRequest(`/clinical-note-templates/${templateId}`, {
                        method: 'PUT',
                        body: JSON.stringify(data)
                    });
                } else {
                    await apiRequest('/clinical-note-templates', {
                        method: 'POST',
                        body: JSON.stringify(data)
                    });
                }
            }

            Toast.success('Success', isEdit ? 'Template updated successfully' : 'Template created successfully');

            bootstrap.Modal.getInstance(this.templateModal)?.hide();
            this.load();

        } catch (error) {
            console.error('[TemplatesModule] Failed to save template:', error);
            Toast.error('Error', error.message || 'Failed to save template');
        }
    }

    /**
     * Handle modal show event
     */
    async handleModalShow() {
        // Initialize Trumbowyg WYSIWYG editor
        const editor = $('#templateHtmlContent');
        if (editor.length && !editor.data('trumbowyg')) {
            editor.trumbowyg({
                btns: [
                    ['viewHTML'],
                    ['undo', 'redo'],
                    ['formatting'],
                    ['strong', 'em', 'del'],
                    ['superscript', 'subscript'],
                    ['link'],
                    ['justifyLeft', 'justifyCenter', 'justifyRight', 'justifyFull'],
                    ['unorderedList', 'orderedList'],
                    ['horizontalRule'],
                    ['removeformat'],
                    ['fullscreen']
                ],
                autogrow: true,
                removeformatPasted: false
            });
        }

        // Load tenants for Super Admin
        const currentUserObj = window.App?.getCurrentUser() || window.currentUser;
        if (currentUserObj?.Role === 0 && !currentUserObj?.TenantId) {
            const select = document.getElementById('templateTenantSelect');
            if (select && select.options.length <= 1) {
                try {
                    const apiGet = window.App?.api
                        ? (url) => App.api.get(url)
                        : (url) => apiRequest(url);

                    const tenants = await apiGet('/tenants');
                    select.innerHTML = '<option value="">System Template (All Clinics)</option>' +
                        (tenants?.map(t => `<option value="${t.TenantId}">${StringUtils.escape(t.Name)}</option>`).join('') || '');
                } catch (error) {
                    console.error('[TemplatesModule] Error loading tenants:', error);
                }
            }
        }
    }

    /**
     * Handle modal hidden event
     */
    handleModalHidden() {
        document.getElementById('templateId').value = '';

        // Destroy Trumbowyg instance
        const editor = $('#templateHtmlContent');
        if (editor.data('trumbowyg')) {
            editor.trumbowyg('destroy');
        }

        if (this.templateForm) {
            this.templateForm.reset();
        }

        document.querySelector('#templateModal .modal-title').textContent = 'New Note Template';

        // Reset location dropdown
        const locationSelect = document.getElementById('templateLocationSelect');
        if (locationSelect) {
            locationSelect.innerHTML = '<option value="">All Locations (Select Clinic First)</option>';
            locationSelect.disabled = true;
        }
    }

    /**
     * Handle tenant select change (load locations)
     */
    async handleTenantChange() {
        const tenantSelect = document.getElementById('templateTenantSelect');
        const locationSelect = document.getElementById('templateLocationSelect');

        if (!tenantSelect || !locationSelect) return;

        const tenantId = tenantSelect.value;
        if (!tenantId) {
            locationSelect.innerHTML = '<option value="">All Locations (Select Clinic First)</option>';
            locationSelect.disabled = true;
            return;
        }

        try {
            const apiGet = window.App?.api
                ? (url) => App.api.get(url)
                : (url) => apiRequest(url);

            const locations = await apiGet(`/locations?tenantId=${tenantId}`);
            locationSelect.innerHTML = '<option value="">All Locations in Clinic</option>' +
                (locations?.map(l => `<option value="${l.LocationId}">${StringUtils.escape(l.Name)}</option>`).join('') || '');
            locationSelect.disabled = false;

        } catch (error) {
            console.error('[TemplatesModule] Error loading locations:', error);
            locationSelect.innerHTML = '<option value="">Error loading locations</option>';
            locationSelect.disabled = true;
        }
    }

    /**
     * Refresh templates from server
     */
    async refresh() {
        await this.load();
    }

    /**
     * Destroy the module
     */
    destroy() {
        if (this.templateForm) {
            this.templateForm.removeEventListener('submit', this.handleFormSubmit);
        }

        if (this.templateModal) {
            this.templateModal.removeEventListener('show.bs.modal', this.handleModalShow);
            this.templateModal.removeEventListener('hidden.bs.modal', this.handleModalHidden);
        }

        // Destroy Trumbowyg if active
        const editor = $('#templateHtmlContent');
        if (editor.data('trumbowyg')) {
            editor.trumbowyg('destroy');
        }

        this.templates = [];
        this.container = null;
        this.tableBody = null;
        this.templateModal = null;
        this.templateForm = null;
    }
}

// Export for module usage
window.TemplatesModule = TemplatesModule;

// Auto-initialize when DOM is ready
document.addEventListener('DOMContentLoaded', function() {
    const container = document.getElementById('templatesPage');
    if (!container) return;

    const initWhenReady = () => {
        const isAuthenticated = (typeof currentUser !== 'undefined' && currentUser) ||
                               (window.App && window.App.isAuthenticated && window.App.isAuthenticated());

        if (!isAuthenticated) {
            setTimeout(initWhenReady, 200);
            return;
        }

        if (window.templatesModule) {
            window.templatesModule.load();
            return;
        }

        window.templatesModule = new TemplatesModule({
            api: window.apiService || (window.App && window.App.api),
            eventBus: window.eventBus || (window.App && window.App.events)
        });

        window.templatesModule.init();
        window.templatesModule.load();
    };

    initWhenReady();
});
