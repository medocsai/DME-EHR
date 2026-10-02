/**
 * Shared behaviour for the hand-typed fields on the DME forms.
 *
 * Rules are attached with a class rather than wired per page, because the same
 * kind of field appears on several forms and the rules must not drift apart.
 *
 *   .js-dateinput  a date somebody types or pastes
 *   .js-phone      a telephone number: letters dropped, punctuation kept
 *   .js-digits     digits only (SSN last 4, ZIP)
 *   .js-name       a person's name: digits dropped
 *   .js-upper      forced upper case, letters only (state)
 *   .js-email      flagged on blur if it is not an address
 *
 * Two kinds of rule, and the difference matters:
 *
 *   FILTERS run as you type and simply remove what does not belong. There is
 *   nothing to report, because the wrong character never lands.
 *
 *   CHECKS run on blur and flag the field, because the value is wrong as a
 *   whole rather than character by character. A check needs somewhere to speak:
 *   put a hidden .js-field-error next to the input and it is shown.
 *
 * Everything here is a COURTESY. The server validates independently and has the
 * final say; Helpers/DateInput.cs owns the one list of date spellings. Nothing
 * in this file is a security control, and a field this script never reached
 * still saves correctly.
 */
(function () {
    'use strict';

    /* ------------------------------------------------------------- plumbing */

    // Removing characters mid-string moves the caret to the end unless we put
    // it back, which makes correcting a typo in a long number infuriating.
    function filter(el, notAllowed, transform) {
        var before = el.value;
        var after = before.replace(notAllowed, '');
        if (transform) after = transform(after);
        if (before === after) return;

        var pos = el.selectionStart || 0;
        var kept = before.slice(0, pos).replace(notAllowed, '').length;
        el.value = after;
        if (el.setSelectionRange) el.setSelectionRange(kept, kept);
    }

    function flag(el, bad) {
        el.classList.toggle('is-invalid', bad);
        el.setAttribute('aria-invalid', bad ? 'true' : 'false');
        var msg = el.parentNode.querySelector('.js-field-error');
        if (msg) msg.hidden = !bad;
    }

    function onEach(selector, fn) {
        var els = document.querySelectorAll(selector);
        for (var i = 0; i < els.length; i++) fn(els[i]);
    }

    /* -------------------------------------------------------------- filters */

    var NOT_PHONE  = /[^0-9+()\-.\s]/g;   // letters out, the punctuation people write stays
    var PHONE_DIGITS = 10;                // (214) 555-0101. Punctuation is not counted.
    var NOT_DIGIT  = /[^0-9]/g;
    var NOT_NAME   = /[0-9]/g;            // only digits are removed; hyphens and apostrophes are names
    var NOT_LETTER = /[^A-Za-z]/g;

    // A US number is ten digits, written (214) 555-0101. The operator types the
    // digits and the shape appears around them: without it the box takes as many
    // characters as you can hold a key down for, and every customer ends up
    // punctuated differently.
    //
    // Rebuilt from the digits each time rather than patched in place, so a paste,
    // a backspace and an edit in the middle all end up in the same shape.
    function formatPhone(el) {
        var digits = el.value.replace(/[^0-9]/g, '').slice(0, PHONE_DIGITS);

        var out = digits;
        if (digits.length > 6)      out = '(' + digits.slice(0, 3) + ') ' + digits.slice(3, 6) + '-' + digits.slice(6);
        else if (digits.length > 3) out = '(' + digits.slice(0, 3) + ') ' + digits.slice(3);
        else if (digits.length > 0) out = '(' + digits;

        if (out === el.value) return;

        // Put the caret back after the same DIGIT it was after, not the same
        // character index: the brackets and the space shift everything right as
        // they appear, and counting characters would walk the caret backwards.
        var typedBefore = el.value.slice(0, el.selectionStart || 0).replace(/[^0-9]/g, '').length;
        el.value = out;

        var seen = 0, caret = out.length;
        for (var i = 0; i < out.length; i++) {
            if (out[i] >= '0' && out[i] <= '9') {
                seen++;
                if (seen === typedBefore) { caret = i + 1; break; }
            }
        }
        if (typedBefore === 0) caret = out.length ? 1 : 0;   // just inside the bracket
        if (el.setSelectionRange) el.setSelectionRange(caret, caret);
    }

    /* --------------------------------------------------------------- checks */

    // Deliberately loose. This is here to catch "not an address at all", not to
    // adjudicate the RFC, and anything borderline is still decided server side.
    var EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;

    function checkEmail(el) {
        var raw = el.value.trim();
        flag(el, raw !== '' && !EMAIL.test(raw));
    }

    // Every date a human types in DME is a plain text box, never
    // <input type="date">: Chrome refuses a paste into the native control, and
    // these dates are normally copied off a referral or an insurance card.
    function normaliseDate(el) {
        // Only the ISO spelling is rewritten. "1950-01-15" is the one form that
        // is hard to read back at a glance, and it is what a copy out of another
        // system produces. Everything else is left as written, because silently
        // reformatting what somebody just typed is its own kind of surprise.
        var iso = el.value.trim().match(/^(\d{4})[-\/](\d{1,2})[-\/](\d{1,2})$/);
        if (iso) el.value = ('0' + iso[2]).slice(-2) + '/' + ('0' + iso[3]).slice(-2) + '/' + iso[1];
    }

    function checkDate(el) {
        var raw = el.value.trim();
        // Empty is not wrong: a date of birth is optional. Date.parse is looser
        // than DateInput.Parse on the server, which is the safe direction to
        // err: it catches obvious rubbish and never rejects a date the server
        // would have accepted.
        flag(el, raw !== '' && isNaN(Date.parse(raw)));
    }

    /* ----------------------------------------------------------------- bind */

    function bind() {
        onEach('.js-phone',  function (el) {
            el.addEventListener('input', function () { formatPhone(el); });
        });

        onEach('.js-digits', function (el) {
            el.addEventListener('input', function () { filter(el, NOT_DIGIT); });
        });

        onEach('.js-name',   function (el) {
            el.addEventListener('input', function () { filter(el, NOT_NAME); });
        });

        onEach('.js-upper',  function (el) {
            el.addEventListener('input', function () {
                filter(el, NOT_LETTER, function (v) { return v.toUpperCase(); });
            });
        });

        onEach('.js-email',  function (el) {
            el.addEventListener('blur', function () { checkEmail(el); });
            el.addEventListener('input', function () {
                if (el.classList.contains('is-invalid')) checkEmail(el);
            });
        });

        onEach('.js-dateinput', function (el) {
            el.addEventListener('blur', function () { normaliseDate(el); checkDate(el); });
            el.addEventListener('input', function () {
                // Clear the complaint as soon as they start fixing it.
                if (el.classList.contains('is-invalid')) checkDate(el);
            });
        });
    }

    // Unlike Typeahead.js this binds nothing during parsing, so waiting for the
    // document is safe and picks up every field however far down the page it is.
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', bind);
    } else {
        bind();
    }
})();
