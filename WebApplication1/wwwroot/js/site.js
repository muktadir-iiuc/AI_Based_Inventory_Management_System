document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('.confirm-delete').forEach(function (form) {
        form.addEventListener('submit', function (e) {
            if (!confirm(form.dataset.confirmMessage || 'Are you sure you want to delete this item?')) {
                e.preventDefault();
            }
        });
    });

    initSelect2(document);
    initEnterKeyNavigation(document);
    initSelectAllOnFocus(document);
    initDataTables(document);
});

// Selects all text in a numeric field as soon as it gets focus — by click, Tab, or a script
// moving focus there — so typing immediately overwrites the value instead of inserting at
// whatever cursor position the click landed on. Delegated on `document` via the bubbling
// 'focusin' event (plain 'focus' doesn't bubble) so it automatically covers number fields
// added to the DOM later — a new invoice line, a new note-calculator row — with no per-page
// wiring needed.
function initSelectAllOnFocus(root) {
    root.addEventListener('focusin', function (e) {
        var target = e.target;
        if (!target.matches || !target.matches('input[type=number]')) {
            return;
        }

        // Deferred a tick so the click that triggered focus doesn't collapse the selection
        // right back down to a cursor position afterward.
        setTimeout(function () {
            if (document.activeElement === target) {
                target.select();
            }
        }, 0);
    });
}

// Turns every table.data-table into a sortable, per-column-filterable grid via DataTables.
// These pages used to paginate server-side (20 rows/page via PagedList + _Pager); with
// DataTables client-side sorting/search/pagination this fully replaces that, so the
// controller now sends every row matching the page's own filters in one response and lets
// DataTables handle sort/search/paging entirely in the browser (fine at this data volume).
// Mark a <th> with data-filter="false" to skip the per-column filter box for it (action
// columns with just buttons/links, or anything not meaningfully text-searchable).
// Mark a <th> with data-filter="select" to get a dropdown of that column's distinct values
// instead of a free-text box (useful for low-cardinality columns like Warehouse or Status).
// Mark a <th> with data-filter="date" to get a native date-picker filter — pair it with a
// data-date="yyyy-MM-dd" attribute on that column's <td> so the picker can match exactly
// regardless of how the date is formatted for display.
function initDataTables(root) {
    if (typeof jQuery === 'undefined' || !jQuery.fn.DataTable) {
        return;
    }

    jQuery(root).find('table.data-table').each(function () {
        var $table = jQuery(this);
        if (jQuery.fn.DataTable.isDataTable(this)) {
            return;
        }

        var $headerRow = $table.find('thead tr').first();
        var $filterRow = jQuery('<tr class="filter-row"></tr>');
        var selectColumns = [];
        var dateColumns = [];

        $headerRow.find('th').each(function (index) {
            var $filterCell = jQuery('<th></th>');
            var filterType = jQuery(this).data('filter');
            if (filterType !== false) {
                if (filterType === 'select') {
                    $filterCell.html('<select class="form-select form-select-sm column-filter"><option value="">All</option></select>');
                    selectColumns.push(index);
                } else if (filterType === 'date') {
                    $filterCell.html('<input type="date" class="form-control form-control-sm column-filter" />');
                    dateColumns.push(index);
                } else {
                    $filterCell.html('<input type="text" class="form-control form-control-sm column-filter" placeholder="Filter" />');
                }
            }
            $filterRow.append($filterCell);
        });
        $table.find('thead').append($filterRow);

        var dt = $table.DataTable({
            order: [],
            orderCellsTop: true,
            pageLength: 20,
            lengthMenu: [10, 20, 50, 100],
            // Drop the built-in global search box (topEnd) — per-column filters below replace
            // it — while keeping DataTables 2's Bootstrap5 layout renderer (and its styling of
            // the length dropdown/pagination) intact, unlike the legacy 'dom' string option.
            layout: { topStart: 'pageLength', topEnd: null, bottomStart: 'info', bottomEnd: 'paging' },
            language: { emptyTable: 'No records found.' }
        });

        selectColumns.forEach(function (index) {
            var $select = $filterRow.find('th').eq(index).find('select');
            dt.column(index).data().unique().sort().each(function (value) {
                var text = jQuery('<div>').html(value).text().trim();
                if (text) {
                    $select.append(jQuery('<option></option>').val(text).text(text));
                }
            });
        });

        dateColumns.forEach(function (index) {
            var selectedDate = '';
            jQuery.fn.dataTable.ext.search.push(function (settings, searchData, rowIdx) {
                if (settings.nTable !== dt.table().node() || !selectedDate) {
                    return true;
                }
                var cell = dt.cell(rowIdx, index).node();
                return !!cell && cell.getAttribute('data-date') === selectedDate;
            });
            $filterRow.find('th').eq(index).find('input[type=date]').on('change', function () {
                selectedDate = this.value;
                dt.draw();
            });
        });

        $filterRow.find('th').each(function (index) {
            jQuery('input[type=text], input[type=date], select', this).on('click', function (e) { e.stopPropagation(); });
            jQuery('input[type=text]', this).on('input', function () { dt.column(index).search(this.value).draw(); });
            jQuery('select', this).on('change', function () {
                var value = this.value;
                var regex = value ? '^' + value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + '$' : '';
                dt.column(index).search(regex, true, false).draw();
            });
        });
    });
}

// Label for a product <option> in the line-item pickers: "SKU - Name - Brand - Size", leaving
// out whichever of brand/size isn't set. Returns HTML-safe text because callers build the
// <option> markup with innerHTML.
function productOptionLabel(p) {
    const esc = v => String(v).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    return [p.Sku, p.Name, p.Brand, p.Size].filter(v => v && String(v).trim()).map(esc).join(' - ');
}

// Initializes Select2 on every dropdown within the given root element or document.
// Call this again with a newly-created row/element after inserting it into the DOM
// (e.g. a dynamically added invoice line) so its <select> gets the same widget.
function initSelect2(root) {
    if (typeof jQuery === 'undefined' || !jQuery.fn.select2) {
        return;
    }

    var $root = jQuery(root);
    var $selects = $root.find('select.form-select');
    if ($root.is('select.form-select')) {
        $selects = $selects.add($root);
    }

    $selects.each(function () {
        var $el = jQuery(this);
        if (!$el.hasClass('select2-hidden-accessible')) {
            $el.select2({ width: '100%' });
            // Tag the visible widget with a direct reference back to its <select>, so code
            // elsewhere (Enter-key navigation) doesn't have to guess at Select2's DOM layout.
            $el.next('.select2-container').find('.select2-selection').data('select2Target', $el);
        }
    });
}

// Makes the Enter key move focus to the next field in a form instead of submitting it,
// the same way Tab does. When the next field is a Select2 box, focusing it also opens
// its dropdown list automatically (whether reached via this Enter navigation or via Tab).
function initEnterKeyNavigation(root) {
    if (typeof jQuery === 'undefined') {
        return;
    }

    var $root = jQuery(root);
    var FIELD_SELECTOR =
        'input:not([type=hidden]):not([type=button]):not([type=submit]):not([type=reset]):not([disabled]), ' +
        'select:not([disabled]):not(.select2-hidden-accessible), ' +
        'textarea:not([disabled]), ' +
        '.select2-selection[tabindex]';

    // Set when Enter is pressed while a Select2 dropdown is open, so the select2:select
    // handler below knows the resulting selection (if any) was keyboard-driven and should
    // advance focus. Select2's own "select" event doesn't carry the triggering key itself in
    // this build — it only includes originalEvent for a mouse-click selection, not a keyboard
    // one (verified against the vendored select2.min.js: the keyboard path's internal
    // "results:select" handler calls trigger("select", {data}) with no originalEvent at all)
    // — so this has to be tracked here instead of read off the select2:select event.
    var enterKeySelectPending = false;

    // Enter on a plain field, or on a closed Select2 box, moves to the next field.
    // Enter while a Select2 dropdown is open (searching/highlighting a result) is left
    // alone so Select2 can pick the highlighted option as usual. A field marked
    // data-enter-submit is left alone too, so a page can wire its own Enter handler
    // (e.g. "press Enter to add this row") instead of just tabbing to the next field.
    // Bound on the capture phase so it runs, and can stopPropagation, before Select2's
    // own bubble-phase keydown handler (which would otherwise just reopen the dropdown).
    root.addEventListener('keydown', function (e) {
        if (e.key !== 'Enter') {
            return;
        }

        var target = e.target;
        var $target = jQuery(target);
        if (!$target.is('input, select, textarea, .select2-selection')) {
            return;
        }
        if ($target.is('textarea, [type=button], [type=submit], [type=reset], [data-enter-submit]')) {
            return;
        }
        if ($target.closest('.select2-dropdown, .select2-search__field').length) {
            enterKeySelectPending = true;
            // Self-expires shortly after: if Enter had no effect at all (e.g. no results
            // matched the search), neither select2:select nor select2:closing fires to clear
            // it, and it must not sit around waiting to misattribute some later, unrelated
            // selection (e.g. a plain mouse click in a different dropdown) as Enter-driven.
            setTimeout(function () {
                enterKeySelectPending = false;
            }, 300);
            return;
        }
        if ($target.closest('.select2-container').hasClass('select2-container--open')) {
            return;
        }

        e.preventDefault();
        e.stopPropagation();
        focusNextField(target, $root, FIELD_SELECTOR);
    }, true);

    // Remember when a Select2 box was just closed (e.g. after picking an option) so the
    // refocus Select2 does internally doesn't immediately reopen the same dropdown.
    $root.on('select2:closing', function (e) {
        var $select = jQuery(e.target);
        $select.data('justClosedSelect2', true);
        setTimeout(function () {
            $select.removeData('justClosedSelect2');
        }, 250);

        // Covers Enter on a result that was already selected (Select2 just closes, with no
        // select2:select at all) and Escape/click-away (closes with nothing selected) — both
        // must not leave a stale pending flag for some later, unrelated selection to pick up.
        enterKeySelectPending = false;
    });

    $root.on('focus', '.select2-selection', function () {
        var $selection = jQuery(this);
        var $container = $selection.closest('.select2-container');
        var $select = $selection.data('select2Target');
        if (!$select || !$select.length || $container.hasClass('select2-container--open') || $select.data('justClosedSelect2')) {
            return;
        }
        // Opening synchronously inside the focus handler is unreliable in Select2 (it can
        // check focus state before the browser has fully committed it) - defer a tick.
        setTimeout(function () {
            if ($selection.is(':focus') && !$container.hasClass('select2-container--open')) {
                $select.select2('open');
            }
        }, 0);
    });

    // Picking a result with Enter (as opposed to a mouse click) selects it and closes the
    // dropdown, but leaves focus sitting on the Select2 box itself — the keydown handler
    // above deliberately left that Enter keypress alone so Select2 could handle it, so
    // nothing has moved focus onward yet. Finish the job here: once Select2 settles focus
    // back onto its own selection element, carry on to the next field, same as Enter does
    // everywhere else in the form.
    $root.on('select2:select', function () {
        if (!enterKeySelectPending) {
            return;
        }
        enterKeySelectPending = false;
        setTimeout(function () {
            var current = document.activeElement;
            if (current) {
                focusNextField(current, $root, FIELD_SELECTOR);
            }
        }, 0);
    });
}

function focusNextField(current, $root, fieldSelector) {
    var $current = jQuery(current);
    var $form = $current.closest('form');
    var $scope = $form.length ? $form : $root;

    var fields = $scope.find(fieldSelector).filter(':visible').toArray();
    var idx = fields.indexOf(current);
    if (idx === -1) {
        return;
    }

    for (var i = idx + 1; i < fields.length; i++) {
        if (jQuery(fields[i]).is(':visible')) {
            fields[i].focus();
            return;
        }
    }
}

// Shows the selected customer's/supplier's current due on an invoice Create page, fetched from
// that party's Due endpoint (Customers/Due or Suppliers/Due), which returns exactly the party
// ledger's closing balance for this user. Also fills an optional "previous due / due after this
// invoice" block, kept in step with the page's grand total. Options:
//   select       - the party <select>
//   dueUrl       - endpoint URL without the id (e.g. "/Customers/Due")
//   ledgerUrl    - ledger URL without the partyId query value (e.g. "/Customers/Ledger?partyId=")
//   dueEl        - element under the select for the one-line due message
//   summaryEl    - optional wrapper shown only once a party's due is known
//   previousEl, afterEl - optional elements for the two summary figures
//   grandTotalEl - optional element holding the invoice grand total (its text is watched)
//   currency     - currency symbol to prefix amounts with
//   advanceLabel - wording for a negative balance, e.g. "Advance from customer"
// Returns { refresh } so a page can re-check after changing the selection in code (e.g. an
// inline quick-add), since setting .value doesn't raise any event.
function initPartyDue(options) {
    var select = options.select;
    var requestSeq = 0;
    var balance = null;
    var settled = 0; // what an edited invoice has already received/paid/returned; 0 on Create

    function money(n) {
        return options.currency + Math.abs(Number(n || 0)).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    function grandTotal() {
        if (!options.grandTotalEl) return 0;
        return parseFloat(options.grandTotalEl.textContent.replace(/[^0-9.\-]/g, '')) || 0;
    }

    function describe(amount) {
        if (amount > 0) return { text: money(amount), cls: 'text-danger' };
        if (amount < 0) return { text: options.advanceLabel + ' ' + money(amount), cls: 'text-success' };
        return { text: money(0), cls: 'text-muted' };
    }

    function renderSummary() {
        if (!options.summaryEl) return;
        options.summaryEl.classList.toggle('d-none', balance === null);
        if (balance === null) return;
        var previous = describe(balance);
        var after = describe(balance + grandTotal() - settled);
        options.previousEl.textContent = previous.text;
        options.previousEl.className = 'col-5 text-end ' + previous.cls;
        options.afterEl.textContent = after.text;
        options.afterEl.className = 'col-5 text-end fw-semibold ' + after.cls;
    }

    function render(id, amount, totalPaid) {
        var el = options.dueEl;
        el.textContent = '';
        el.className = 'small mt-1';
        var label = document.createElement('span');
        var link = document.createElement('a');
        link.href = options.ledgerUrl + id;
        link.className = 'ms-1';
        link.textContent = 'View ledger';
        if (amount > 0) {
            label.className = 'text-danger fw-semibold';
            label.textContent = 'Current due: ' + money(amount);
        } else if (amount < 0) {
            label.className = 'text-success fw-semibold';
            label.textContent = options.advanceLabel + ': ' + money(amount);
        } else {
            label.className = 'text-muted';
            label.textContent = 'No outstanding due';
        }
        el.appendChild(label);
        el.appendChild(document.createTextNode(' · '));
        el.appendChild(link);
        if (options.paidLabel) {
            var paid = document.createElement('div');
            paid.className = 'text-muted';
            paid.textContent = options.paidLabel + ': ' + money(totalPaid);
            el.appendChild(paid);
        }
    }

    function hide() {
        balance = null;
        options.dueEl.className = 'small mt-1 d-none';
        options.dueEl.textContent = '';
        renderSummary();
    }

    function refresh() {
        var id = parseInt(select.value, 10);
        var seq = ++requestSeq;
        if (!id) {
            hide();
            return;
        }
        options.dueEl.className = 'small mt-1 text-muted';
        options.dueEl.textContent = 'Checking due…';
        fetch(options.dueUrl + '/' + id + (options.excludeInvoiceId ? '?excludeInvoiceId=' + options.excludeInvoiceId : ''), { headers: { 'Accept': 'application/json' } })
            .then(function (res) { return res.json(); })
            .then(function (data) {
                // A slower response for a party the user has since moved away from must not
                // overwrite the current one.
                if (seq !== requestSeq) return;
                if (!data.ok) {
                    hide();
                    return;
                }
                balance = Number(data.balance) || 0;
                settled = Number(data.settled) || 0;
                render(id, balance, Number(data.totalPaid) || 0);
                renderSummary();
            })
            .catch(function () {
                if (seq !== requestSeq) return;
                balance = null;
                options.dueEl.className = 'small mt-1 text-warning';
                options.dueEl.textContent = 'Could not load the current due.';
                renderSummary();
            });
    }

    select.addEventListener('change', refresh);
    if (typeof jQuery !== 'undefined') {
        jQuery(select).on('select2:select select2:clear', refresh);
    }
    if (options.grandTotalEl && typeof MutationObserver !== 'undefined') {
        new MutationObserver(renderSummary).observe(options.grandTotalEl, { childList: true, characterData: true, subtree: true });
    }
    refresh();
    return { refresh: refresh };
}
