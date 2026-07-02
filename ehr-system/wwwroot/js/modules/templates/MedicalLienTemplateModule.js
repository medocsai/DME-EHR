/**
 * MedicalLienTemplateModule - Medical Lien Form Template management
 * Handles loading, creating, editing, and deleting Medical Lien templates
 * Super Admin only - templates are assigned to specific clinics and locations
 *
 * Usage:
 *   const medicalLienTemplateModule = new MedicalLienTemplateModule();
 *   medicalLienTemplateModule.init();
 */
class MedicalLienTemplateModule {
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
        this.container = document.getElementById('medicalLienTemplatesPage');
        this.tableBody = this.container?.querySelector('tbody');
        this.templateModal = document.getElementById('medicalLienTemplateModal');
        this.templateForm = document.getElementById('medicalLienTemplateForm');

        if (!this.container) {
            console.warn('[MedicalLienTemplateModule] Container not found');
            return;
        }

        this.bindEvents();
        this.load();

        // Register with App if available
        if (window.App && window.App.modules) {
            App.modules.register('medicalLienTemplates', this);
        }

        console.log('[MedicalLienTemplateModule] Initialized');
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
        const searchInput = document.getElementById('medicalLienTemplateSearchInput');
        const clinicFilter = document.getElementById('medicalLienTemplateClinicFilter');

        if (searchInput) {
            searchInput.addEventListener('input', DomUtils.debounce(this.handleFilterChange, 300));
        }
        if (clinicFilter) {
            clinicFilter.addEventListener('change', this.handleFilterChange);
        }

        // Tenant select change (for loading locations in modal)
        const tenantSelect = document.getElementById('medicalLienTemplateTenantSelect');
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

            this.templates = await apiGet('/medical-lien-templates?activeOnly=false') || [];

            // Populate clinic filter dropdown
            this.populateClinicFilter();

            // Apply filters and render
            this.render();

        } catch (error) {
            console.error('[MedicalLienTemplateModule] Failed to load templates:', error);
            Toast.error('Error', 'Failed to load templates: ' + error.message);
        }
    }

    /**
     * Populate clinic filter dropdown
     */
    populateClinicFilter() {
        const clinicFilter = document.getElementById('medicalLienTemplateClinicFilter');
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

        // Sort clinics by name
        const sortedClinics = Array.from(clinics.entries()).sort((a, b) => a[1].localeCompare(b[1]));
        sortedClinics.forEach(([id, name]) => {
            options += `<option value="${id}">${StringUtils.escape(name)}</option>`;
        });

        clinicFilter.innerHTML = options;
    }

    /**
     * Render templates table
     */
    render() {
        if (!this.tableBody) return;

        // Apply filters
        const searchTerm = document.getElementById('medicalLienTemplateSearchInput')?.value?.toLowerCase() || '';
        const clinicFilter = document.getElementById('medicalLienTemplateClinicFilter')?.value || '';

        let filteredTemplates = this.templates;

        // Search filter
        if (searchTerm) {
            filteredTemplates = filteredTemplates.filter(t =>
                t.Name?.toLowerCase().includes(searchTerm) ||
                t.Description?.toLowerCase().includes(searchTerm) ||
                t.TenantName?.toLowerCase().includes(searchTerm) ||
                t.LocationName?.toLowerCase().includes(searchTerm)
            );
        }

        // Clinic filter
        if (clinicFilter) {
            filteredTemplates = filteredTemplates.filter(t => t.TenantId == clinicFilter);
        }

        // Render table
        if (filteredTemplates.length === 0) {
            this.tableBody.innerHTML = `
                <tr>
                    <td colspan="6" class="text-center text-muted py-4">
                        <i class="bi bi-inbox display-6"></i>
                        <p class="mb-0 mt-2">No templates found</p>
                    </td>
                </tr>
            `;
            return;
        }

        this.tableBody.innerHTML = filteredTemplates.map(t => `
            <tr>
                <td><strong>${StringUtils.escape(t.Name)}</strong></td>
                <td>${StringUtils.escape(t.TenantName || 'Unknown')}</td>
                <td>${StringUtils.escape(t.LocationName || 'Unknown')}</td>
                <td>
                    <span class="badge ${t.IsActive ? 'bg-success' : 'bg-secondary'}">
                        ${t.IsActive ? 'Active' : 'Inactive'}
                    </span>
                </td>
                <td>${this.formatDate(t.UpdatedAt || t.CreatedAt)}</td>
                <td>
                    <button class="btn btn-sm btn-outline-primary me-1" onclick="medicalLienTemplateModule.edit(${t.TemplateId})" title="Edit">
                        <i class="bi bi-pencil"></i>
                    </button>
                    <button class="btn btn-sm btn-outline-danger" onclick="medicalLienTemplateModule.delete(${t.TemplateId})" title="Delete">
                        <i class="bi bi-trash"></i>
                    </button>
                </td>
            </tr>
        `).join('');
    }

    /**
     * Format date for display
     */
    formatDate(dateStr) {
        if (!dateStr) return '-';
        const date = new Date(dateStr);
        return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
    }

    /**
     * Handle filter changes
     */
    handleFilterChange() {
        this.render();
    }

    /**
     * Clear all filters
     */
    clearFilters() {
        const searchInput = document.getElementById('medicalLienTemplateSearchInput');
        const clinicFilter = document.getElementById('medicalLienTemplateClinicFilter');

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

            const template = await apiGet(`/medical-lien-templates/${templateId}`);

            if (!this.templateForm) return;

            this.templateForm.querySelector('[name="TemplateId"]').value = template.TemplateId;
            this.templateForm.querySelector('[name="Name"]').value = template.Name;
            this.templateForm.querySelector('[name="Description"]').value = template.Description || '';
            this.templateForm.querySelector('[name="IsActive"]').checked = template.IsActive;
            this.templateForm.querySelector('[name="HtmlContent"]').value = template.HtmlContent || '';

            // Load tenants for dropdown
            const tenantSelect = document.getElementById('medicalLienTemplateTenantSelect');
            const locationSelect = document.getElementById('medicalLienTemplateLocationSelect');

            if (tenantSelect && locationSelect) {
                // Load tenant options if not loaded
                if (tenantSelect.options.length <= 1) {
                    try {
                        const tenants = await apiGet('/tenants');
                        tenantSelect.innerHTML = '<option value="">Select a clinic...</option>' +
                            (tenants?.map(t => `<option value="${t.TenantId}">${StringUtils.escape(t.Name)}</option>`).join('') || '');
                    } catch (error) {
                        console.error('[MedicalLienTemplateModule] Error loading tenants:', error);
                    }
                }

                // Set tenant value
                tenantSelect.value = template.TenantId || '';

                // If template has a TenantId, load locations
                if (template.TenantId) {
                    try {
                        const locations = await apiGet(`/locations?tenantId=${template.TenantId}`);
                        locationSelect.innerHTML = '<option value="">Select a location...</option>' +
                            (locations?.map(l => `<option value="${l.LocationId}">${StringUtils.escape(l.Name)}</option>`).join('') || '');
                        locationSelect.disabled = false;

                        if (template.LocationId) {
                            locationSelect.value = template.LocationId;
                        }
                    } catch (error) {
                        console.error('[MedicalLienTemplateModule] Error loading locations:', error);
                        locationSelect.innerHTML = '<option value="">Error loading locations</option>';
                        locationSelect.disabled = true;
                    }
                } else {
                    locationSelect.innerHTML = '<option value="">Select clinic first...</option>';
                    locationSelect.disabled = true;
                }
            }

            document.querySelector('#medicalLienTemplateModal .modal-title').innerHTML =
                '<i class="bi bi-file-earmark-medical me-2"></i>Edit Medical Lien Template';

            const modal = new bootstrap.Modal(this.templateModal);
            modal.show();

        } catch (error) {
            console.error('[MedicalLienTemplateModule] Failed to edit template:', error);
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
            message: 'Are you sure you want to delete this Medical Lien template? This cannot be undone.',
            confirmText: 'Delete',
            confirmClass: 'btn-danger',
            headerClass: 'bg-danger text-white'
        });
        if (!confirmed) return;

        try {
            if (window.App?.api) {
                await App.api.delete(`/medical-lien-templates/${templateId}`);
            } else {
                await apiRequest(`/medical-lien-templates/${templateId}`, { method: 'DELETE' });
            }

            Toast.success('Template Deleted', 'Medical Lien template deleted successfully');
            this.load();
        } catch (error) {
            console.error('[MedicalLienTemplateModule] Failed to delete template:', error);
            Toast.error('Error', error.message || 'Failed to delete template');
        }
    }

    /**
     * Handle form submission
     * @param {Event} event - Submit event
     */
    async handleFormSubmit(event) {
        event.preventDefault();

        const formData = new FormData(this.templateForm);
        const templateId = formData.get('TemplateId');
        const isEdit = templateId && templateId !== '';

        const dto = {
            Name: formData.get('Name'),
            Description: formData.get('Description') || null,
            TenantId: parseInt(formData.get('TenantId')),
            LocationId: parseInt(formData.get('LocationId')),
            HtmlContent: formData.get('HtmlContent'),
            IsActive: formData.get('IsActive') === 'on'
        };

        // Validate required fields
        if (!dto.TenantId) {
            Toast.error('Validation Error', 'Please select a clinic');
            return;
        }
        if (!dto.LocationId) {
            Toast.error('Validation Error', 'Please select a location');
            return;
        }
        if (!dto.Name || !dto.Name.trim()) {
            Toast.error('Validation Error', 'Please enter a template name');
            return;
        }
        if (!dto.HtmlContent || !dto.HtmlContent.trim()) {
            Toast.error('Validation Error', 'Please enter template content');
            return;
        }

        try {
            if (isEdit) {
                if (window.App?.api) {
                    await App.api.put(`/medical-lien-templates/${templateId}`, dto);
                } else {
                    await apiRequest(`/medical-lien-templates/${templateId}`, { method: 'PUT', body: JSON.stringify(dto) });
                }
                Toast.success('Template Updated', 'Medical Lien template updated successfully');
            } else {
                if (window.App?.api) {
                    await App.api.post('/medical-lien-templates', dto);
                } else {
                    await apiRequest('/medical-lien-templates', { method: 'POST', body: JSON.stringify(dto) });
                }
                Toast.success('Template Created', 'Medical Lien template created successfully');
            }

            bootstrap.Modal.getInstance(this.templateModal)?.hide();
            this.load();

        } catch (error) {
            console.error('[MedicalLienTemplateModule] Failed to save template:', error);
            Toast.error('Error', error.message || 'Failed to save template');
        }
    }

    /**
     * Handle modal show event
     */
    async handleModalShow(event) {
        // Check if this is "New" (no templateId set)
        const templateIdField = this.templateForm?.querySelector('[name="TemplateId"]');
        const isNew = !templateIdField?.value;

        if (isNew) {
            document.querySelector('#medicalLienTemplateModal .modal-title').innerHTML =
                '<i class="bi bi-file-earmark-medical me-2"></i>New Medical Lien Template';
        }

        // Load tenants for dropdown
        const tenantSelect = document.getElementById('medicalLienTemplateTenantSelect');
        if (tenantSelect && tenantSelect.options.length <= 1) {
            try {
                const apiGet = window.App?.api
                    ? (url) => App.api.get(url)
                    : (url) => apiRequest(url);

                const tenants = await apiGet('/tenants');
                tenantSelect.innerHTML = '<option value="">Select a clinic...</option>' +
                    (tenants?.map(t => `<option value="${t.TenantId}">${StringUtils.escape(t.Name)}</option>`).join('') || '');
            } catch (error) {
                console.error('[MedicalLienTemplateModule] Error loading tenants:', error);
            }
        }
    }

    /**
     * Handle modal hidden event - reset form
     */
    handleModalHidden() {
        if (this.templateForm) {
            this.templateForm.reset();
            this.templateForm.querySelector('[name="TemplateId"]').value = '';
        }

        // Reset location dropdown
        const locationSelect = document.getElementById('medicalLienTemplateLocationSelect');
        if (locationSelect) {
            locationSelect.innerHTML = '<option value="">Select clinic first...</option>';
            locationSelect.disabled = true;
        }
    }

    /**
     * Handle tenant change - load locations
     */
    async handleTenantChange(event) {
        const tenantId = event.target.value;
        const locationSelect = document.getElementById('medicalLienTemplateLocationSelect');

        if (!locationSelect) return;

        if (!tenantId) {
            locationSelect.innerHTML = '<option value="">Select clinic first...</option>';
            locationSelect.disabled = true;
            return;
        }

        try {
            const apiGet = window.App?.api
                ? (url) => App.api.get(url)
                : (url) => apiRequest(url);

            const locations = await apiGet(`/locations?tenantId=${tenantId}`);

            locationSelect.innerHTML = '<option value="">Select a location...</option>' +
                (locations?.map(l => `<option value="${l.LocationId}">${StringUtils.escape(l.Name)}</option>`).join('') || '');
            locationSelect.disabled = false;

        } catch (error) {
            console.error('[MedicalLienTemplateModule] Error loading locations:', error);
            locationSelect.innerHTML = '<option value="">Error loading locations</option>';
            locationSelect.disabled = true;
        }
    }

    /**
     * Open modal for new template
     */
    openNew() {
        if (this.templateForm) {
            this.templateForm.reset();
            this.templateForm.querySelector('[name="TemplateId"]').value = '';
            this.templateForm.querySelector('[name="IsActive"]').checked = true;
        }

        // Reset location dropdown
        const locationSelect = document.getElementById('medicalLienTemplateLocationSelect');
        if (locationSelect) {
            locationSelect.innerHTML = '<option value="">Select clinic first...</option>';
            locationSelect.disabled = true;
        }

        document.querySelector('#medicalLienTemplateModal .modal-title').innerHTML =
            '<i class="bi bi-file-earmark-medical me-2"></i>New Medical Lien Template';

        const modal = new bootstrap.Modal(this.templateModal);
        modal.show();
    }
}

// Create global instance
const medicalLienTemplateModule = new MedicalLienTemplateModule();

// Auto-initialize when DOM is ready
document.addEventListener('DOMContentLoaded', () => {
    if (document.getElementById('medicalLienTemplatesPage')) {
        medicalLienTemplateModule.init();
    }
});
