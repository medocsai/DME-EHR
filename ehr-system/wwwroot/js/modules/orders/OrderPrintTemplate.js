/**
 * OrderPrintTemplate
 *
 * Builds the full HTML document for the lab-order print preview. Output is
 * a self-contained <html> string suitable for `srcdoc` on a hidden iframe
 * or `document.write()` in a popup window.
 *
 * Design matches mocks/lab-order-print.html: serif "receipt" style,
 * single-page Letter layout, header band + patient block + clinical row +
 * panel banner + results table + comments + signature + footer.
 *
 * Data shape: LabOrderPrintDto (see Models/DTOs/AllDtos.cs). All date/time
 * strings are already pre-formatted in the location's timezone server-side
 * (per the strict timezone rule in CLAUDE.md) — this module never calls
 * new Date() / toLocaleString for the report itself.
 */
(function () {
    'use strict';

    function escape(s) {
        if (s === null || s === undefined) return '';
        return String(s)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    /** First letter of each word in a clinic name for the logo fallback. */
    function logoInitials(name) {
        if (!name) return 'C';
        const parts = String(name).trim().split(/\s+/).filter(Boolean);
        if (parts.length === 1) return parts[0][0].toUpperCase();
        return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
    }

    function buildLabReportHtml(d) {
        if (!d) return '<html><body><p>No data</p></body></html>';

        // ---------- HEADER LOGO ----------
        // Real logo if the tenant has one; otherwise gradient initials block.
        const logoBlock = d.HasLogo && d.TenantId
            ? `<img src="/api/tenants/${d.TenantId}/logo" alt="${escape(d.ClinicName)} logo"
                   style="width:48px;height:48px;object-fit:contain;border-radius:6px;background:#fff;"
                   onerror="this.outerHTML='<div class=&quot;logo-mark&quot;>${logoInitials(d.ClinicName)}</div>'">`
            : `<div class="logo-mark">${escape(logoInitials(d.ClinicName))}</div>`;

        // ---------- CLINIC RIGHT BLOCK ----------
        // CLIA/ISO are intentionally hidden (not stored in DB). Show only
        // values we actually have: tenant NPI, tax ID. If neither, leave blank.
        const clinicRightLines = [];
        if (d.ClinicNpi) clinicRightLines.push(`<strong>NPI:</strong> ${escape(d.ClinicNpi)}`);
        if (d.ClinicTaxId) clinicRightLines.push(`<strong>Tax ID:</strong> ${escape(d.ClinicTaxId)}`);
        const clinicRight = clinicRightLines.length
            ? clinicRightLines.join('<br>')
            : '&nbsp;';

        // ---------- CLINICAL INDICATION ROW ----------
        const clinBits = [];
        if (d.DiagnosisCode) {
            const dx = d.DiagnosisDescription
                ? `${escape(d.DiagnosisCode)} &mdash; ${escape(d.DiagnosisDescription)}`
                : escape(d.DiagnosisCode);
            clinBits.push(`<span class="lbl">Diagnosis:</span> ${dx}`);
        }
        if (d.ClinicalIndication) clinBits.push(`<span class="lbl">Indication:</span> ${escape(d.ClinicalIndication)}`);
        if (d.SpecimenType) clinBits.push(`<span class="lbl">Specimen:</span> ${escape(d.SpecimenType)}`);
        if (d.FastingRequired === true) clinBits.push(`<span class="lbl">Fasting:</span> Yes`);
        else if (d.FastingRequired === false) clinBits.push(`<span class="lbl">Fasting:</span> No`);
        if (d.PriorityName) clinBits.push(`<span class="lbl">Priority:</span> ${escape(d.PriorityName)}`);
        const clinicalRow = clinBits.length
            ? `<div class="clinical-row">${clinBits.join(' &nbsp;&middot;&nbsp; ')}</div>`
            : '';

        // ---------- RESULTS ROWS ----------
        const resultRows = (d.Results || []).map(r => `
            <tr class="${r.IsAbnormal ? 'abnormal' : ''}">
                <td>${escape(r.TestName)}</td>
                <td>${escape(r.ResultValue)}</td>
                <td>${escape(r.ResultUnit)}</td>
                <td>${escape(r.ReferenceRange)}</td>
                <td class="${r.IsAbnormal ? 'flag-abn' : 'flag-nrm'}">${escape(r.FlagText)}</td>
            </tr>
        `).join('');

        // ---------- ORDERING PROVIDER LINE ----------
        const providerNpiPart = d.ProviderNpi ? ` &middot; NPI ${escape(d.ProviderNpi)}` : '';

        // ---------- VERIFIED LINE ----------
        const verifiedLine = d.VerifiedAtFormatted
            ? `Electronically verified on ${escape(d.VerifiedAtFormatted)}. No signature required.`
            : 'Results pending final verification.';

        // ---------- CONTACT LINE ----------
        const contactParts = [];
        if (d.ClinicAddressLine || d.ClinicCityStateZip) {
            contactParts.push([d.ClinicAddressLine, d.ClinicCityStateZip].filter(Boolean).join(', '));
        }
        if (d.ClinicPhone) contactParts.push(`Tel: ${d.ClinicPhone}`);
        if (d.ClinicEmail) contactParts.push(`Email: ${d.ClinicEmail}`);
        const contactLine = contactParts.length ? contactParts.map(escape).join(' &middot; ') : '';

        // ---------- COMMENTS BLOCK ----------
        const commentsBlock = d.Notes
            ? `<div class="comments"><span class="lbl">Notes:</span> ${escape(d.Notes)}</div>`
            : '';

        // ---------- DOCUMENT ----------
        return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<title>Lab Report — ${escape(d.AccessionNumber || ('Order ' + d.OrderId))}</title>
<style>
  @page { size: Letter; margin: 0.4in 0.45in 0.4in 0.45in; }
  * { box-sizing: border-box; }
  html, body { margin: 0; padding: 0; font-family: "Times New Roman", "Liberation Serif", Georgia, serif; color: #000; font-size: 9pt; line-height: 1.25; background: #fff; }
  .page { padding: 0.4in 0.5in 0.4in 0.5in; position: relative; min-height: 10.5in; }
  .report-header { display: grid; grid-template-columns: 1fr 2fr 1fr; align-items: center; padding-bottom: 6px; border-bottom: 2px solid #1B72BE; margin-bottom: 8px; }
  .logo-left { display: flex; align-items: center; gap: 8px; }
  .logo-mark { width: 44px; height: 44px; border-radius: 8px; background: linear-gradient(135deg, #1B72BE 0%, #2563eb 100%); color: #fff; display: flex; align-items: center; justify-content: center; font-weight: 700; font-size: 16pt; font-family: Arial, sans-serif; flex-shrink: 0; }
  .logo-text { font-family: Arial, sans-serif; font-size: 7pt; color: #1B72BE; font-weight: 700; letter-spacing: 0.5px; line-height: 1.1; }
  .clinic-center { text-align: center; }
  .clinic-center .clinic-name { font-size: 18pt; font-weight: 700; color: #1B72BE; letter-spacing: 1.5px; line-height: 1; }
  .clinic-center .clinic-sub { font-size: 8.5pt; color: #111; margin-top: 2px; font-weight: 600; }
  .clinic-right { text-align: right; font-size: 7.5pt; color: #4b5563; line-height: 1.3; }
  .clinic-right strong { color: #111; }
  .patient-block { border-top: 1px solid #000; border-bottom: 1px solid #000; padding: 4px 0; margin-bottom: 4px; display: grid; grid-template-columns: 1fr 1fr 100px; gap: 6px; align-items: stretch; }
  .patient-cell { display: grid; grid-template-columns: 78px 1fr; gap: 3px 8px; align-items: start; font-size: 9pt; line-height: 1.35; }
  .patient-cell .lbl { font-weight: 700; }
  .accession-box { border: 1px solid #555; padding: 4px 6px; text-align: center; font-size: 7.5pt; line-height: 1.25; display: flex; flex-direction: column; justify-content: center; }
  .accession-box .acc-num { font-family: 'Courier New', monospace; font-size: 8pt; font-weight: 700; margin-top: 1px; }
  .accession-box .acc-status { font-size: 7pt; color: #15803D; font-weight: 700; margin-top: 3px; letter-spacing: 1px; }
  .clinical-row { font-size: 8.5pt; padding: 2px 0; border-bottom: 1px solid #000; margin-bottom: 4px; }
  .clinical-row .lbl { font-weight: 700; }
  .panel-banner { text-align: center; font-weight: 700; font-size: 10pt; padding: 3px 0; border-bottom: 1px solid #000; margin-bottom: 2px; letter-spacing: 0.5px; }
  table.results { width: 100%; border-collapse: collapse; font-size: 9pt; }
  table.results thead th { text-align: left; padding: 3px 6px; border-bottom: 1px solid #000; font-weight: 700; font-size: 8.5pt; }
  table.results td { padding: 2px 6px; vertical-align: top; font-size: 9pt; }
  table.results tr.abnormal td { font-weight: 700; }
  table.results td.flag-abn { color: #991B1B; font-weight: 700; }
  table.results td.flag-nrm { color: #166534; }
  .comments { margin-top: 6px; padding: 4px 0; border-top: 1px solid #000; font-size: 8.5pt; }
  .comments .lbl { font-weight: 700; }
  .signature-row { margin-top: 12px; font-size: 8.5pt; }
  .signature-row .lbl { font-weight: 700; }
  .report-footer { position: absolute; left: 0.5in; right: 0.5in; bottom: 0.4in; font-size: 7.5pt; color: #000; }
  .verified-line { text-align: center; font-size: 8pt; border-top: 1px solid #000; padding-top: 3px; font-style: italic; }
  .printed-line { display: grid; grid-template-columns: 1fr 2fr 1fr; align-items: center; padding-top: 2px; font-size: 7.5pt; }
  .printed-line .right { text-align: right; }
  .printed-line .center { text-align: center; }
  .printed-line strong { font-weight: 700; }
  .contact-line { text-align: center; font-size: 7pt; color: #4b5563; border-top: 1px solid #000; padding-top: 3px; margin-top: 3px; line-height: 1.3; }
  @media print { html, body { background: #fff; } }
</style>
</head>
<body>
<div class="page">

  <div class="report-header">
    <div class="logo-left">
      ${logoBlock}
      <div class="logo-text">${escape((d.ClinicName || '').toUpperCase())}<br>LABORATORY</div>
    </div>
    <div class="clinic-center">
      <div class="clinic-name">${escape(d.ClinicName || '—')}</div>
      <div class="clinic-sub">${escape(d.ClinicTagline || 'Diagnostic Reference Laboratory')}</div>
    </div>
    <div class="clinic-right">${clinicRight}</div>
  </div>

  <div class="patient-block">
    <div>
      <div class="patient-cell"><span class="lbl">MR No:</span><span>${escape(d.PatientMrn)}</span></div>
      <div class="patient-cell"><span class="lbl">Lab No:</span><span>${escape(d.AccessionNumber)}</span></div>
      <div class="patient-cell"><span class="lbl">Name:</span><span>${escape(d.PatientNameFormal)}</span></div>
    </div>
    <div>
      <div class="patient-cell"><span class="lbl">Age/Gender:</span><span>${escape(d.PatientAge || '—')}Y / ${escape(d.PatientGender || '—')}</span></div>
      <div class="patient-cell"><span class="lbl">Referred By:</span><span>${escape(d.ProviderName)}${providerNpiPart}</span></div>
      <div class="patient-cell"><span class="lbl">Sample Date:</span><span>${escape(d.OrderDateFormatted)}</span></div>
    </div>
    <div class="accession-box">
      <div>Accession</div>
      <div class="acc-num">${escape((d.AccessionNumber || '').replace(/^LAB-\d+-/, ''))}</div>
      <div class="acc-status">${escape((d.StatusName || '').toUpperCase())}</div>
    </div>
  </div>

  ${clinicalRow}

  ${d.LabPanelName ? `<div class="panel-banner">${escape(d.LabPanelName.toUpperCase())}</div>` : ''}

  <table class="results">
    <thead>
      <tr>
        <th style="width: 30%;">TEST(s)</th>
        <th style="width: 18%;">RESULT(s)</th>
        <th style="width: 14%;">UNITS</th>
        <th style="width: 24%;">REFERENCE RANGE(s)</th>
        <th style="width: 14%;">FLAG</th>
      </tr>
    </thead>
    <tbody>
      ${resultRows || '<tr><td colspan="5" style="text-align:center;color:#6b7280;padding:14px 0;">No results recorded</td></tr>'}
    </tbody>
  </table>

  ${commentsBlock}

  <div class="report-footer">
    <div class="verified-line">${verifiedLine}</div>
    <div class="printed-line">
      <div><strong>Printed By:</strong> ${escape(d.PrintedByName || '—')}</div>
      <div class="center"><strong>Printed On:</strong> ${escape(d.PrintedAtFormatted || '')}</div>
      <div class="right">Page 1 of 1</div>
    </div>
    ${contactLine ? `<div class="contact-line">${contactLine}</div>` : ''}
  </div>

</div>
</body>
</html>`;
    }

    window.OrderPrintTemplate = { buildLabReportHtml: buildLabReportHtml };
})();
