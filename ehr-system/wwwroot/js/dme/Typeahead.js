/**
 * DME reference-data typeahead.
 *
 * WHY THIS EXISTS
 * Three of the product's pickers are lists nobody can render into a <select>:
 * 4,017 payers, 74,719 ICD-10-CM codes, 8,623 HCPCS codes. They all behave the
 * same way, and the two behaviours that are easy to get wrong are the same in
 * all three:
 *
 *   1. Typing again ABANDONS the previous choice. Without that the visible box
 *      can read one thing while the hidden field still posts another, which is
 *      how the wrong payer ends up on a claim.
 *   2. A slower earlier request must not overwrite a newer one. Every request
 *      carries a sequence number and a late reply is dropped.
 *   3. The list must still be there when the click arrives. It used to close on
 *      a timer, which worked with a mouse and failed on a laptop trackpad.
 *
 * The visible input is only a search box. The hidden input carries the id or
 * code, and the server re-reads the name from the catalog, so nothing the
 * browser types can become the value that is stored.
 *
 * USAGE
 *   dmeTypeahead({
 *       input:   '#payerSearch',   // what the operator types into
 *       hidden:  '#insPayerId',    // what actually gets posted
 *       results: '#payerResults',  // the dropdown container
 *       chosen:  '#payerChosen',   // optional confirmation line under the box
 *       url:     '/Lookups/Payers',
 *       value:   function (item) { return item.id; },
 *       label:   function (item) { return item.name; },
 *       note:    function (item) { return 'Payer ID ' + item.code; },
 *       empty:   'No payer matches that.'
 *   });
 */
(function (global) {
    'use strict';

    /** Minimum characters before asking the server. One letter matches thousands. */
    var MIN_CHARS = 2;

    /** Wait after the last keystroke, in ms. A request per keystroke is a request wasted. */
    var DEBOUNCE_MS = 180;

    function escapeHtml(s) {
        return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    }

    global.dmeTypeahead = function (opts) {
        var input = document.querySelector(opts.input),
            hidden = document.querySelector(opts.hidden),
            list = document.querySelector(opts.results),
            chosen = opts.chosen ? document.querySelector(opts.chosen) : null,
            emptyValue = opts.emptyValue === undefined ? '' : opts.emptyValue,
            timer = null,
            seq = 0;

        if (!input || !hidden || !list) return;

        // The card this picker lives in clips its children, so an open list has
        // to lift the card out of the way while it is showing. See
        // .dme-typeahead-open in dme.css.
        var card = input.closest('.dme-card');

        function open(isOpen) {
            list.hidden = !isOpen;
            if (card) card.classList.toggle('dme-typeahead-open', isOpen);
        }

        function hide() {
            open(false);
            list.innerHTML = '';
        }

        function clearChoice() {
            hidden.value = emptyValue;
            if (chosen) chosen.hidden = true;
        }

        function choose(item) {
            hidden.value = opts.value(item);
            input.value = opts.label(item);
            if (chosen && opts.note) {
                chosen.textContent = opts.note(item);
                chosen.hidden = false;
            }
            hide();
        }

        function render(items) {
            if (!items.length) {
                list.innerHTML = '<div class="dme-typeahead-empty">' +
                    escapeHtml(opts.empty || 'Nothing matches that.') + '</div>';
                open(true);
                return;
            }
            list.innerHTML = '';
            items.forEach(function (item) {
                var row = document.createElement('button');
                // type=button, because this sits inside a form and the default
                // is submit: picking from the list would save the record.
                row.type = 'button';
                row.className = 'dme-typeahead-item';
                row.innerHTML = '<span>' + escapeHtml(opts.label(item)) + '</span>' +
                    (opts.side ? '<span class="dme-typeahead-code">' + escapeHtml(opts.side(item)) + '</span>' : '');
                row.addEventListener('click', function () { choose(item); });
                list.appendChild(row);
            });
            open(true);
        }

        input.addEventListener('input', function () {
            clearChoice();

            var q = input.value.trim();
            if (q.length < MIN_CHARS) { hide(); return; }

            clearTimeout(timer);
            timer = setTimeout(function () {
                var mine = ++seq;
                fetch(opts.url + '?q=' + encodeURIComponent(q), { credentials: 'same-origin' })
                    .then(function (r) { return r.ok ? r.json() : []; })
                    .then(function (items) {
                        if (mine !== seq) return;   // a late reply to an older keystroke
                        render(items);
                    })
                    .catch(function () { hide(); });
            }, DEBOUNCE_MS);
        });

        // WHY THE LIST DOES NOT CLOSE ON A TIMER
        //
        // It used to. Leaving the box closed the list 150ms later, which was
        // meant to give a click on a result time to land first. That is a race,
        // and it was lost on every laptop trackpad we tried:
        //
        //   mouse     mousedown -> mouseup -> click, all inside ~20ms. Wins.
        //   trackpad  a tap has to be RECOGNISED as a tap before the click is
        //             synthesised, and any finger movement during it pushes that
        //             further out. Past 150ms the list has already been emptied,
        //             so the button the click was aimed at no longer exists and
        //             nothing happens at all.
        //
        // The operator sees a dropdown that ignores them, works when they plug a
        // mouse in, and gives no clue why. Reproduced by firing blur and then
        // clicking 250ms later, which is what the browser does on a tap.
        //
        // Preventing the default on mousedown stops the focus from moving, so
        // blur never fires while somebody is picking. There is nothing left to
        // race: the list closes when focus actually leaves, or on Escape.
        list.addEventListener('mousedown', function (e) { e.preventDefault(); });

        input.addEventListener('blur', hide);
        input.addEventListener('keydown', function (e) { if (e.key === 'Escape') hide(); });
    };
})(window);
