/**
 * IntakeDemographicsRenderer (Employee of IntakeFormRenderer)
 *
 * Why: render Section 1 (Demographics) of the intake wizard. Mirrors the
 *      Clinic Patient Form's basic-info + emergency-contact fields so patient
 *      and clinic see the same shape.
 * What: First/Last name, DOB (read-only), Gender, SSN last-4 (read-only, masked),
 *       Phone (input-masked), Email (read-only), Address, City, State, ZIP,
 *       Emergency Contact Name / Relationship / Phone / Alt Phone.
 *       Pre-fills all fields from GET /api/intake/demographics/prefill on mount.
 * Who calls: IntakeFormRenderer.
 * Returns: DOM + getPayload() for section "demographics".
 *
 * Server enforces read-only on DOB / Email / SSN even if client tampers —
 * see SaveDemographicsAsync in IntakeSubmissionService.cs.
 */
(function () {
    'use strict';

    // Prefill URL + auth headers are injected by IntakeFormRenderer (host config).
    // Same renderer works for portal (JWT) and tablet (verify cookie).
    async function fetchPrefillFor(renderer) {
        if (!renderer || !renderer._prefillUrl) return null;
        try {
            const headers = (typeof renderer._authHeaders === 'function') ? renderer._authHeaders() : {};
            const res = await fetch(renderer._prefillUrl, { headers });
            if (!res.ok) return null;
            return await res.json();
        } catch { return null; }
    }

    // Phone mask: (XXX) XXX-XXXX. Forgiving — accepts any digit input, formats live.
    function attachPhoneMask(el) {
        if (!el) return;
        el.addEventListener('input', () => {
            const d = (el.value || '').replace(/\D/g, '').slice(0, 10);
            let v = d;
            if (d.length >= 7) v = `(${d.slice(0, 3)}) ${d.slice(3, 6)}-${d.slice(6)}`;
            else if (d.length >= 4) v = `(${d.slice(0, 3)}) ${d.slice(3)}`;
            else if (d.length >= 1) v = `(${d}`;
            el.value = v;
        });
    }

    class IntakeDemographicsRenderer {
        constructor() {
            this.container = null;
            this.hint = 'Tell us about yourself. This helps us reach you and personalize your care.';
        }

        async render(container, data, mode) {
            this.container = container;

            // Show a skeleton immediately so the page isn't blank while we fetch.
            container.innerHTML = `
                <div class="text-center text-muted py-4">
                    <div class="spinner-border spinner-border-sm me-2"></div> Loading your information...
                </div>
            `;

            const prefill = await fetchPrefillFor(this);

            const ssnDisplay = prefill && prefill.ssnLast4
                ? `***-**-${prefill.ssnLast4}`
                : '';

            container.innerHTML = `
                <div class="row g-3">
                    <div class="col-md-6"><label class="form-label">First Name</label>
                        <input class="form-control" id="idfFirstName" value="${attr(prefill?.firstName)}" /></div>
                    <div class="col-md-6"><label class="form-label">Last Name</label>
                        <input class="form-control" id="idfLastName" value="${attr(prefill?.lastName)}" /></div>

                    <div class="col-md-4"><label class="form-label">Date of Birth</label>
                        <input class="form-control" id="idfDob" type="date" value="${attr(prefill?.dateOfBirth)}" readonly />
                        <div class="form-text"><i class="bi bi-lock text-muted"></i> Tied to your account</div>
                    </div>
                    <div class="col-md-4"><label class="form-label">Gender</label>
                        <select class="form-select" id="idfGender">
                            <option value="">Select...</option>
                            <option value="Male">Male</option>
                            <option value="Female">Female</option>
                            <option value="Other">Other</option>
                        </select>
                    </div>
                    <div class="col-md-4"><label class="form-label">SSN (last 4)</label>
                        <input class="form-control" id="idfSsn" value="${attr(ssnDisplay)}" readonly />
                        <div class="form-text"><i class="bi bi-lock text-muted"></i> Tied to your account</div>
                    </div>

                    <div class="col-md-6"><label class="form-label">Cell Phone</label>
                        <input class="form-control" id="idfPhone" type="tel" placeholder="(555) 555-5555" value="${attr(prefill?.phone)}" /></div>
                    <div class="col-md-6"><label class="form-label">Email</label>
                        <input class="form-control" id="idfEmail" type="email" value="${attr(prefill?.email)}" readonly />
                        <div class="form-text"><i class="bi bi-lock text-muted"></i> Tied to your account</div>
                    </div>

                    <div class="col-12"><label class="form-label">Home Address</label>
                        <input class="form-control" id="idfAddress" value="${attr(prefill?.address)}" /></div>
                    <div class="col-md-5"><label class="form-label">City</label>
                        <input class="form-control" id="idfCity" value="${attr(prefill?.city)}" /></div>
                    <div class="col-md-4"><label class="form-label">State</label>
                        <input class="form-control" id="idfState" maxlength="2" placeholder="XX" value="${attr(prefill?.state)}" /></div>
                    <div class="col-md-3"><label class="form-label">ZIP</label>
                        <input class="form-control" id="idfZip" maxlength="10" value="${attr(prefill?.zipCode)}" /></div>

                    <div class="col-12 pt-2"><h6 class="mb-0"><i class="bi bi-person-heart text-primary me-2"></i>Emergency Contact</h6></div>
                    <div class="col-md-6"><label class="form-label">Contact Name</label>
                        <input class="form-control" id="idfEcName" value="${attr(prefill?.emergencyContactName)}" /></div>
                    <div class="col-md-6"><label class="form-label">Relationship</label>
                        <select class="form-select" id="idfEcRelation">
                            <option value="">Select...</option>
                            <option value="Spouse">Spouse</option>
                            <option value="Parent">Parent</option>
                            <option value="Child">Child</option>
                            <option value="Sibling">Sibling</option>
                            <option value="Friend">Friend</option>
                            <option value="Other">Other</option>
                        </select>
                    </div>
                    <div class="col-md-6"><label class="form-label">Contact Phone</label>
                        <input class="form-control" id="idfEcPhone" type="tel" placeholder="(555) 555-5555" value="${attr(prefill?.emergencyContactPhone)}" /></div>
                    <div class="col-md-6"><label class="form-label">Alternate Phone</label>
                        <input class="form-control" id="idfEcAltPhone" type="tel" placeholder="(555) 555-5555" value="${attr(prefill?.emergencyContactAltPhone)}" /></div>
                </div>
            `;

            // Set selects after render (can't set via value attribute for <select>).
            if (prefill?.gender) {
                const g = document.getElementById('idfGender');
                if (g) g.value = prefill.gender;
            }
            if (prefill?.emergencyContactRelation) {
                const r = document.getElementById('idfEcRelation');
                if (r) r.value = prefill.emergencyContactRelation;
            }

            // Live phone masks on all 3 phone fields.
            attachPhoneMask(document.getElementById('idfPhone'));
            attachPhoneMask(document.getElementById('idfEcPhone'));
            attachPhoneMask(document.getElementById('idfEcAltPhone'));
        }

        getPayload(sectionName) {
            const v = id => (document.getElementById(id)?.value || '').trim();
            // NOTE: dateOfBirth, email, ssn are NOT sent — read-only. Server
            // ignores them anyway but we don't even include them for clarity.
            return {
                firstName: v('idfFirstName'),
                lastName: v('idfLastName'),
                gender: v('idfGender'),
                phone: v('idfPhone'),
                address: v('idfAddress'),
                city: v('idfCity'),
                state: v('idfState'),
                zipCode: v('idfZip'),
                emergencyContactName: v('idfEcName'),
                emergencyContactRelation: v('idfEcRelation'),
                emergencyContactPhone: v('idfEcPhone'),
                emergencyContactAltPhone: v('idfEcAltPhone')
            };
        }
    }

    // (fetchPrefill now lives at top of module as fetchPrefillFor(renderer);
    //  callers pass `this` so the renderer's injected URL is used.)

    function attr(v) {
        if (v === null || v === undefined) return '';
        return String(v).replace(/"/g, '&quot;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

    window.IntakeDemographicsRenderer = IntakeDemographicsRenderer;
})();
