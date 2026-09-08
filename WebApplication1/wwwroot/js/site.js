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
    initDataTables(document);
});

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

    // Enter on a plain field, or on a closed Select2 box, moves to the next field.
    // Enter while a Select2 dropdown is open (searching/highlighting a result) is left
    // alone so Select2 can pick the highlighted option as usual.
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
        if ($target.is('textarea, [type=button], [type=submit], [type=reset]')) {
            return;
        }
        if ($target.closest('.select2-dropdown, .select2-search__field').length) {
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
