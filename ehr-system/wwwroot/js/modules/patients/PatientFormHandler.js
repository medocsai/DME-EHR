/**
 * PatientFormHandler - Handles form submission, data extraction, and population
 */

class PatientFormHandler {
    constructor(options = {}) {
        this.parentModule = options.parentModule;
        this.utilities = options.utilities || PatientUtilities;
        this.api = options.api;
        this._wireDigitsOnlyInputs();
        this._wireEmailDuplicateCheck();
    }

    /**
     * Inline duplicate-email validation. Blocks save (hard rule) by
     * painting a red error under the Email field as soon as the user
     * tabs out. The form submit handler in PatientModule also re-checks
     * before saving and aborts if a duplicate is still present.
     *
     * Delegation pattern because #patientModal is reused for both Add
     * and Edit; the email input is re-rendered on each open.
     */
    _wireEmailDuplicateCheck() {
        const form = document.getElementById('patientForm');
        if (!form) return;

        let lastChecked = null;
        form.addEventListener('blur', async (e) => {
            const el = e.target;
            if (!(el instanceof HTMLInputElement)) return;
            if (el.getAttribute('name') !== 'Email') return;

            const email = (el.value || '').trim();
            this._setEmailDuplicateError(el, null); // clear previous

            if (!email || !email.includes('@')) return;
            if (email === lastChecked) return;
            lastChecked = email;

            try {
                const patientId = document.getElementById('patientId')?.value;
                const qs = new URLSearchParams({ email });
                if (patientId) qs.set('excludePatientId', patientId);

                const result = this.api && this.api.get
                    ? await this.api.get(`/patients/check-email?${qs.toString()}`)
                    : await fetch(`/api/patients/check-email?${qs.toString()}`, {
                        headers: { 'Authorization': 'Bearer ' + (localStorage.getItem('authToken') || '') }
                      }).then(r => r.ok ? r.json() : null);

                if (result && result.isUnique === false) {
                    this._setEmailDuplicateError(el, result.existingPatientName);
                }
            } catch (err) {
                console.debug('[PatientFormHandler] email dup check failed', err);
            }
        }, true); // capture-phase so we get blur on inputs

        // Also clear the error as soon as the user starts editing the email
        // again — gives them visual feedback that the form is responsive.
        form.addEventListener('input', (e) => {
            const el = e.target;
            if (!(el instanceof HTMLInputElement)) return;
            if (el.getAttribute('name') !== 'Email') return;
            this._setEmailDuplicateError(el, null);
            lastChecked = null;
        });
    }

    /**
     * Render or remove a red inline error under the email field and a red
     * border on the input itself. Used by both the on-blur check above
     * and the form-submit guard in PatientModule. This is a HARD error —
     * the form-submit guard refuses to save while it's present.
     */
    _setEmailDuplicateError(emailInput, existingPatientName) {
        const parent = emailInput.parentElement;
        if (!parent) return;
        let note = parent.querySelector('.email-dup-error');

        if (!existingPatientName) {
            if (note) note.remove();
            emailInput.classList.remove('is-invalid');
            emailInput.style.borderColor = '';
            return;
        }

        if (!note) {
            note = document.createElement('div');
            note.className = 'email-dup-error small mt-1';
            note.style.color = '#991B1B';
            note.style.fontWeight = '500';
            parent.appendChild(note);
        }
        emailInput.classList.add('is-invalid');
        emailInput.style.borderColor = '#DC2626';
        note.innerHTML = `<i class="bi bi-x-circle-fill me-1"></i>This email is already in use by <strong></strong>. Please use a different email.`;
        note.querySelector('strong').textContent = existingPatientName;
    }

    /**
     * Strip non-digits in real time on any input tagged `data-digits-only="N"`.
     * Used for ZIP (5 digits) so a paste of "90210-1234" auto-clips to "90210"
     * and typed letters never land in the field. Idempotent — listener attached
     * once at module construction; the inputs live inside #patientModal which
     * is reused for both add + edit, so a single delegation covers both.
     */
    _wireDigitsOnlyInputs() {
        document.addEventListener('input', (e) => {
            const el = e.target;
            if (!(el instanceof HTMLInputElement)) return;
            const max = el.dataset.digitsOnly;
            if (!max) return;
            const digits = el.value.replace(/\D/g, '');
            const clipped = max ? digits.substring(0, parseInt(max, 10)) : digits;
            if (clipped !== el.value) el.value = clipped;
        });
    }

    /**
     * Populate form with patient data
     * @param {Object} patient - Patient data
     * @param {Object} options - Options
     * @param {Function} options.onLoadAttachments - Callback to load attachments
     * @param {Function} options.onLoadHistory - Callback to load authorization history
     */
    populateForm(patient, options = {}) {
        const form = document.getElementById('patientForm');
        if (!form) return;

        console.log('[PatientFormHandler] populateForm - patient data:', patient);

        form.reset();
        document.getElementById('patientId').value = patient.PatientId;

        // Basic fields
        const fields = ['FirstName', 'LastName', 'DateOfBirth', 'Gender', 'Ssn',
                       'Phone', 'Email', 'Address', 'City', 'State', 'ZipCode',
                       'EmergencyContactName', 'EmergencyContactPhone', 'EmergencyContactAltPhone', 'EmergencyContactRelation',
                       'DateOfInjury'];

        fields.forEach(field => {
            const input = form.querySelector(`[name="${field}"]`);
            if (input) {
                input.value = patient[field] || '';
                // Auto-normalize digit-only fields (e.g. ZIP) so legacy ZIP+4
                // values like "90210-1234" collapse to "90210" on populate.
                // Fires the same digits-only listener wired in the ctor.
                if (input.dataset.digitsOnly) {
                    input.dispatchEvent(new Event('input', { bubbles: true }));
                }
            }
        });

        // Primary insurance
        const primaryInsurance = patient.Insurances?.find(i => i.Type === 0);
        console.log('[PatientFormHandler] Primary insurance:', primaryInsurance);
        if (primaryInsurance) {
            this.populateInsuranceFields(form, 'PrimaryInsurance', primaryInsurance);
            this._populateAllowedVisits('primaryAllowedVisits', primaryInsurance);
        }

        // Secondary insurance
        const secondaryInsurance = patient.Insurances?.find(i => i.Type === 1);
        console.log('[PatientFormHandler] Secondary insurance:', secondaryInsurance);
        if (secondaryInsurance) {
            this.populateInsuranceFields(form, 'SecondaryInsurance', secondaryInsurance);
            this._populateAllowedVisits('secondaryAllowedVisits', secondaryInsurance);
        }

        // Update modal title
        const titleEl = document.querySelector('#patientModal .modal-title');
        if (titleEl) {
            titleEl.textContent = 'Edit Patient';
        }

        // Profile picture — render editable avatar
        const avatarContainer = document.getElementById('patientEditableAvatarContainer');
        if (avatarContainer && patient.PatientId) {
            avatarContainer.innerHTML = AvatarUtils.renderEditableAvatar({
                entityType: 'patient',
                entityId: patient.PatientId,
                name: patient.FullName || `${patient.FirstName} ${patient.LastName}`,
                hasProfilePicture: patient.HasProfilePicture,
                size: 'xl'
            });
        }

        // Show authorization history sections when editing
        const primaryAuthHistorySection = document.getElementById('primaryAuthHistorySection');
        if (primaryAuthHistorySection) {
            primaryAuthHistorySection.style.display = 'block';
        }

        const secondaryAuthHistorySection = document.getElementById('secondaryAuthHistorySection');
        if (secondaryAuthHistorySection) {
            secondaryAuthHistorySection.style.display = 'block';
        }

        // Load attachments if callback provided
        if (patient.PatientId && options.onLoadAttachments) {
            options.onLoadAttachments(patient.PatientId);
        }

        // Load authorization history after a short delay
        if (options.onLoadHistory) {
            if (primaryInsurance?.InsuranceId) {
                setTimeout(() => {
                    options.onLoadHistory('primary');
                }, 100);
            }
            if (secondaryInsurance?.InsuranceId) {
                setTimeout(() => {
                    options.onLoadHistory('secondary');
                }, 150);
            }
        }
    }

    /**
     * Populate allowed visits for insurance
     * @private
     * @param {string} fieldId - Field ID
     * @param {Object} insurance - Insurance data
     */
    _populateAllowedVisits(fieldId, insurance) {
        const input = document.getElementById(fieldId);
        if (!input) return;

        if (insurance.AllowedVisits != null) {
            input.value = insurance.AllowedVisits;
        } else if (insurance.CurrentAuthorization?.AuthorizedVisits) {
            input.value = insurance.CurrentAuthorization.AuthorizedVisits;
        }
    }

    /**
     * Populate insurance form fields
     * @param {HTMLFormElement} form - Form element
     * @param {string} prefix - Field prefix
     * @param {Object} insurance - Insurance data
     */
    populateInsuranceFields(form, prefix, insurance) {
        // All possible insurance fields (form may not have all of them)
        const fields = ['InsuranceId', 'InsuranceCategory', 'PayerName', 'PayerId', 'PolicyNumber',
                       'GroupNumber', 'Copay', 'Deductible', 'EffectiveFrom', 'EffectiveTo',
                       'SubscriberName', 'SubscriberFirstName', 'SubscriberLastName',
                       'SubscriberId', 'SubscriberDob', 'SubscriberRelationship',
                       'AttorneyName', 'AttorneyPhone', 'AttorneyEmail', 'AllowedVisits'];

        fields.forEach(field => {
            const input = form.querySelector(`[name="${prefix}.${field}"]`);
            if (input && insurance[field] != null) {
                input.value = insurance[field];
            }
        });
    }

    /**
     * Reset patient form for new patient entry
     * @param {Object} options - Options
     * @param {Function} options.onReset - Callback when reset
     */
    resetForm(options = {}) {
        const form = document.getElementById('patientForm');
        if (form) {
            form.reset();
        }

        // Clear patient ID
        const patientIdInput = document.getElementById('patientId');
        if (patientIdInput) {
            patientIdInput.value = '';
        }

        // Reset to first tab (Basic Info)
        const firstTab = document.querySelector('#patientModal .nav-link[data-bs-target="#basic-info"]') ||
                        document.querySelector('#patientModal .nav-link:first-child');
        if (firstTab) {
            // Remove active class from all tabs
            document.querySelectorAll('#patientModal .nav-link').forEach(tab => {
                tab.classList.remove('active');
                tab.setAttribute('aria-selected', 'false');
            });
            // Add active to first tab
            firstTab.classList.add('active');
            firstTab.setAttribute('aria-selected', 'true');

            // Hide all tab panes and show first one
            document.querySelectorAll('#patientModal .tab-pane').forEach(pane => {
                pane.classList.remove('show', 'active');
            });
            const firstPaneId = firstTab.getAttribute('data-bs-target') || firstTab.getAttribute('href');
            const firstPane = document.querySelector(firstPaneId);
            if (firstPane) {
                firstPane.classList.add('show', 'active');
            }
        }

        // Clear AllowedVisits fields
        const primaryAllowedVisits = document.getElementById('primaryAllowedVisits');
        if (primaryAllowedVisits) {
            primaryAllowedVisits.value = '';
        }
        const secondaryAllowedVisits = document.getElementById('secondaryAllowedVisits');
        if (secondaryAllowedVisits) {
            secondaryAllowedVisits.value = '';
        }

        // Clear validation status
        const primaryStatus = document.getElementById('primaryInsuranceValidationStatus');
        if (primaryStatus) {
            primaryStatus.innerHTML = '';
        }
        const secondaryStatus = document.getElementById('secondaryInsuranceValidationStatus');
        if (secondaryStatus) {
            secondaryStatus.innerHTML = '';
        }

        // Profile picture — interactive placeholder for new patient (allows preview before save)
        AvatarUtils.clearPendingPhoto('patient');
        const avatarContainer = document.getElementById('patientEditableAvatarContainer');
        if (avatarContainer) {
            avatarContainer.innerHTML = AvatarUtils.renderEditableAvatar({
                entityType: 'patient',
                entityId: null,
                name: '?',
                hasProfilePicture: false,
                size: 'xl'
            });
        }

        // Hide authorization history sections for new patient
        const primaryAuthHistorySection = document.getElementById('primaryAuthHistorySection');
        if (primaryAuthHistorySection) {
            primaryAuthHistorySection.style.display = 'none';
        }
        const secondaryAuthHistorySection = document.getElementById('secondaryAuthHistorySection');
        if (secondaryAuthHistorySection) {
            secondaryAuthHistorySection.style.display = 'none';
        }

        // Reset authorization history to empty state
        const primaryAuthBody = document.getElementById('primaryAuthHistoryBody');
        if (primaryAuthBody) {
            primaryAuthBody.innerHTML = `
                <tr>
                    <td colspan="6" class="text-center text-muted py-3">
                        <i class="bi bi-info-circle me-1"></i>No authorization history available
                    </td>
                </tr>
            `;
        }
        const secondaryAuthBody = document.getElementById('secondaryAuthHistoryBody');
        if (secondaryAuthBody) {
            secondaryAuthBody.innerHTML = `
                <tr>
                    <td colspan="6" class="text-center text-muted py-3">
                        <i class="bi bi-info-circle me-1"></i>No authorization history available
                    </td>
                </tr>
            `;
        }

        // Clear attachments list
        const attachmentsList = document.getElementById('patientAttachmentsList');
        if (attachmentsList) {
            attachmentsList.innerHTML = '';
        }

        // Update modal title
        const titleEl = document.querySelector('#patientModal .modal-title');
        if (titleEl) {
            titleEl.textContent = 'New Patient';
        }

        if (options.onReset) {
            options.onReset();
        }
    }

    /**
     * Extract form data into patient object
     * @param {FormData} formData - Form data
     * @returns {Object} Patient data
     */
    extractFormData(formData) {
        const data = {
            FirstName: formData.get('FirstName'),
            LastName: formData.get('LastName'),
            DateOfBirth: formData.get('DateOfBirth'),
            Gender: formData.get('Gender') || null,
            Ssn: formData.get('Ssn') || null,
            Status: parseInt(formData.get('Status')) || 0,
            Phone: formData.get('Phone') || null,
            Email: formData.get('Email')?.trim() ?? null,
            Address: formData.get('Address') || null,
            City: formData.get('City') || null,
            State: formData.get('State') || null,
            ZipCode: formData.get('ZipCode') || null,
            EmergencyContactName: formData.get('EmergencyContactName') || null,
            EmergencyContactPhone: formData.get('EmergencyContactPhone') || null,
            EmergencyContactAltPhone: formData.get('EmergencyContactAltPhone') || null,
            EmergencyContactRelation: formData.get('EmergencyContactRelation') || null,
            DateOfInjury: formData.get('DateOfInjury') || null
        };

        // Extract primary insurance
        const primaryInsuranceData = this._extractInsuranceData(formData, 'PrimaryInsurance');
        if (primaryInsuranceData) {
            data.PrimaryInsurance = primaryInsuranceData;
        }

        // Extract secondary insurance
        const secondaryInsuranceData = this._extractInsuranceData(formData, 'SecondaryInsurance');
        if (secondaryInsuranceData) {
            data.SecondaryInsurance = secondaryInsuranceData;
        }

        console.log('[PatientFormHandler] extractFormData - complete data:', data);
        return data;
    }

    /**
     * Extract insurance data from form
     * @private
     * @param {FormData} formData - Form data
     * @param {string} prefix - Field prefix
     * @returns {Object|null} Insurance data or null if no data
     */
    _extractInsuranceData(formData, prefix) {
        const payerName = formData.get(`${prefix}.PayerName`);
        if (!payerName) return null;

        const insuranceData = {
            InsuranceCategory: parseInt(formData.get(`${prefix}.InsuranceCategory`)) || 0,
            PayerName: payerName,
            PayerId: formData.get(`${prefix}.PayerId`) || null,
            PolicyNumber: formData.get(`${prefix}.PolicyNumber`) || null,
            GroupNumber: formData.get(`${prefix}.GroupNumber`) || null,
            Copay: formData.get(`${prefix}.Copay`) ? parseFloat(formData.get(`${prefix}.Copay`)) : null,
            Deductible: formData.get(`${prefix}.Deductible`) ? parseFloat(formData.get(`${prefix}.Deductible`)) : null,
            EffectiveFrom: formData.get(`${prefix}.EffectiveFrom`) || null,
            EffectiveTo: formData.get(`${prefix}.EffectiveTo`) || null,
            SubscriberName: formData.get(`${prefix}.SubscriberName`) || null,
            SubscriberFirstName: formData.get(`${prefix}.SubscriberFirstName`) || null,
            SubscriberLastName: formData.get(`${prefix}.SubscriberLastName`) || null,
            SubscriberId: formData.get(`${prefix}.SubscriberId`) || null,
            SubscriberDob: formData.get(`${prefix}.SubscriberDob`) || null,
            SubscriberRelationship: formData.get(`${prefix}.SubscriberRelationship`) || null,
            AttorneyName: formData.get(`${prefix}.AttorneyName`) || null,
            AttorneyPhone: formData.get(`${prefix}.AttorneyPhone`) || null,
            AttorneyEmail: formData.get(`${prefix}.AttorneyEmail`) || null,
            AllowedVisits: formData.get(`${prefix}.AllowedVisits`) ? parseInt(formData.get(`${prefix}.AllowedVisits`)) : null
        };

        // Include InsuranceId if editing existing insurance
        const insuranceId = formData.get(`${prefix}.InsuranceId`);
        if (insuranceId) {
            insuranceData.InsuranceId = parseInt(insuranceId);
        }

        console.log(`[PatientFormHandler] Extracted ${prefix}:`, insuranceData);
        return insuranceData;
    }

    /**
     * Handle form submission
     * @param {Event} e - Submit event
     * @param {Object} options - Options
     * @param {Function} options.apiFn - API POST function
     * @param {Function} options.apiPutFn - API PUT function
     * @param {Function} options.onSuccess - Success callback
     * @param {Function} options.onError - Error callback
     * @param {Function} options.onUploadFiles - Upload pending files callback
     * @param {Function} options.onReload - Reload data callback
     * @param {Function} options.onHideModal - Hide modal callback
     */
    async handleFormSubmit(e, options = {}) {
        e.preventDefault();
        e.stopPropagation();

        const formData = new FormData(e.target);
        const patientId = formData.get('PatientId');
        const isEdit = patientId && patientId !== '';

        const data = this.extractFormData(formData);

        try {
            let response;
            if (isEdit) {
                const apiPutFn = options.apiPutFn;
                response = await apiPutFn(`/patients/${patientId}`, data);
                if (options.onSuccess) {
                    options.onSuccess('Patient updated successfully');
                }
                if (options.onEvent) {
                    options.onEvent('patients:updated', { patientId, data });
                }
            } else {
                const apiFn = options.apiFn;
                response = await apiFn('/patients', data);
                if (!response) {
                    throw new Error('Server returned empty response. Please check if you are logged in.');
                }
                if (options.onSuccess) {
                    options.onSuccess('Patient created successfully');
                }
                if (options.onEvent) {
                    options.onEvent('patients:created', { patient: response });
                }

                // Upload any pending files for the new patient
                if (options.pendingFiles?.length > 0 && response.PatientId && options.onUploadFiles) {
                    await options.onUploadFiles(response.PatientId);
                }
            }

            if (options.onHideModal) {
                options.onHideModal('patientModal');
            }
            e.target.reset();

            // Only reload if we have a table to populate
            if (options.onReload) {
                await options.onReload();
            }
        } catch (error) {
            console.error('[PatientFormHandler] Save patient error:', error);
            if (options.onError) {
                options.onError(error.message || 'Failed to save patient');
            }
        }
    }
}

// Export for use in both modern and legacy environments
if (typeof module !== 'undefined' && module.exports) {
    module.exports = PatientFormHandler;
}
window.PatientFormHandler = PatientFormHandler;
