/**
 * OrganizationModule - Manages Organization/Practice settings page
 * Loads and saves tenant info (name, NPI, address, etc.)
 */
class OrganizationModule {
    constructor() {
        this.tenantId = null;
    }

    async init() {
        const page = document.getElementById('organizationPage');
        if (!page) return;

        const user = JSON.parse(localStorage.getItem('currentUser') || '{}');
        this.tenantId = user.TenantId;

        if (!this.tenantId) {
            document.getElementById('orgSaveStatus').innerHTML =
                '<span class="text-danger">Unable to determine clinic. Please log in again.</span>';
            return;
        }

        document.getElementById('organizationForm').addEventListener('submit', (e) => {
            e.preventDefault();
            this.save();
        });

        await this.load();
    }

    async load() {
        try {
            const response = await fetch(`/api/tenants/${this.tenantId}`, {
                headers: { 'Authorization': `Bearer ${localStorage.getItem('authToken')}` }
            });

            if (!response.ok) throw new Error('Failed to load organization data');

            const tenant = await response.json();

            document.getElementById('orgName').value = tenant.Name || '';
            document.getElementById('orgNpi').value = tenant.NPI || '';
            document.getElementById('orgTaxId').value = tenant.TaxId || '';
            document.getElementById('orgAddress').value = tenant.Address || '';
            document.getElementById('orgCity').value = tenant.City || '';
            document.getElementById('orgState').value = tenant.State || '';
            document.getElementById('orgZip').value = tenant.ZipCode || '';
            document.getElementById('orgPhone').value = tenant.Phone || '';
            document.getElementById('orgEmail').value = tenant.Email || '';
        } catch (err) {
            console.error('Failed to load organization:', err);
            document.getElementById('orgSaveStatus').innerHTML =
                '<span class="text-danger">Failed to load organization data.</span>';
        }
    }

    async save() {
        const btn = document.getElementById('saveOrgBtn');
        const status = document.getElementById('orgSaveStatus');
        btn.disabled = true;
        status.innerHTML = '<span class="text-muted">Saving...</span>';

        const dto = {
            Name: document.getElementById('orgName').value.trim(),
            NPI: document.getElementById('orgNpi').value.trim(),
            TaxId: document.getElementById('orgTaxId').value.trim(),
            Address: document.getElementById('orgAddress').value.trim(),
            City: document.getElementById('orgCity').value.trim(),
            State: document.getElementById('orgState').value.trim(),
            ZipCode: document.getElementById('orgZip').value.trim(),
            Phone: document.getElementById('orgPhone').value.trim(),
            Email: document.getElementById('orgEmail').value.trim()
        };

        try {
            const response = await fetch(`/api/tenants/${this.tenantId}`, {
                method: 'PUT',
                headers: {
                    'Content-Type': 'application/json',
                    'Authorization': `Bearer ${localStorage.getItem('authToken')}`
                },
                body: JSON.stringify(dto)
            });

            if (!response.ok) {
                const err = await response.json().catch(() => ({}));
                throw new Error(err.message || 'Failed to save');
            }

            status.innerHTML = '<span class="text-success"><i class="bi bi-check-circle me-1"></i>Saved successfully</span>';
            setTimeout(() => { status.innerHTML = ''; }, 3000);
        } catch (err) {
            console.error('Failed to save organization:', err);
            status.innerHTML = `<span class="text-danger"><i class="bi bi-exclamation-circle me-1"></i>${err.message}</span>`;
        } finally {
            btn.disabled = false;
        }
    }
}

document.addEventListener('DOMContentLoaded', () => {
    const module = new OrganizationModule();
    module.init();
});
