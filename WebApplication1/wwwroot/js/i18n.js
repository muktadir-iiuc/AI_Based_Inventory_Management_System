// Interface translation (English -> Bangla). The server renders English and sets <html lang="bn"> when the user chose Bangla;
// this script then swaps every visible phrase using the dictionary in /i18n/bn.js and keeps doing so for content added later
// (modals, AJAX, DataTables). English pages never load the dictionary.
(function () {
    'use strict';
    var data = window.IMS_I18N;
    if (document.documentElement.lang !== 'bn' || !data) { return; }

    var dict = data.dict || {};
    var lower = {};
    Object.keys(dict).forEach(function (k) { lower[k.toLowerCase()] = dict[k]; });
    var rules = (data.rules || []).map(function (r) { return { re: new RegExp(r[0]), to: r[1] }; });

    var cache = {};
    function lookup(key) {
        if (Object.prototype.hasOwnProperty.call(dict, key)) { return dict[key]; }
        var l = key.toLowerCase();
        return Object.prototype.hasOwnProperty.call(lower, l) ? lower[l] : null;
    }

    var months = { Jan: 'জানুয়ারি', Feb: 'ফেব্রুয়ারি', Mar: 'মার্চ', Apr: 'এপ্রিল', May: 'মে', Jun: 'জুন', Jul: 'জুলাই', Aug: 'আগস্ট', Sep: 'সেপ্টেম্বর', Oct: 'অক্টোবর', Nov: 'নভেম্বর', Dec: 'ডিসেম্বর' };
    function bnDigits(s) { return s.replace(/\d/g, function (d) { return '০১২৩৪৫৬৭৮৯'.charAt(+d); }); }

    function translateCore(text) {
        // "Oct 02, 2026 06:44 AM" / "Oct 02, 2026"
        var dm = /^([A-Z][a-z]{2}) (\d{1,2}), (\d{4})(?: (\d{1,2}:\d{2}) (AM|PM))?$/.exec(text);
        if (dm && months[dm[1]]) {
            var out = bnDigits(dm[2]) + ' ' + months[dm[1]] + ' ' + bnDigits(dm[3]);
            if (dm[4]) { out += ', ' + bnDigits(dm[4]) + (dm[5] === 'AM' ? ' পূর্বাহ্ণ' : ' অপরাহ্ণ'); }
            return out;
        }
        var hit = lookup(text);
        if (hit !== null) { return hit; }
        // "Label:" / "Label..." / "Label *"
        var m = /^(.*?)(\s*[:*…]+|\s*\.{2,3})$/.exec(text);
        if (m && m[1]) {
            hit = lookup(m[1]);
            if (hit !== null) { return hit + m[2]; }
        }
        for (var i = 0; i < rules.length; i++) {
            var rm = rules[i].re.exec(text);
            if (rm) {
                return rules[i].to.replace(/([$@])(\d)/g, function (_, kind, n) {
                    var part = rm[+n] === undefined ? '' : rm[+n];
                    if (kind === '@') { var t = translateCore(part); return t === null ? part : t; }
                    return part;
                });
            }
        }
        return null;
    }

    // Returns the Bangla text for `raw`, or null when nothing matches. Surrounding whitespace is kept.
    function translate(raw) {
        if (!raw) { return null; }
        var lead = /^\s*/.exec(raw)[0];
        var trail = /\s*$/.exec(raw)[0];
        var core = raw.trim().replace(/\s+/g, ' ');
        if (!core || !/[A-Za-z]/.test(core)) { return null; }
        var res;
        if (Object.prototype.hasOwnProperty.call(cache, core)) { res = cache[core]; }
        else { res = translateCore(core); cache[core] = res; }
        return res === null ? null : lead + res + trail;
    }
    window.imsT = function (text) { var t = translate(text); return t === null ? text : t; };

    var skipTags = { SCRIPT: 1, STYLE: 1, TEXTAREA: 1, CODE: 1, PRE: 1, NOSCRIPT: 1 };
    var attrs = ['placeholder', 'title', 'aria-label', 'alt', 'data-bs-original-title', 'data-confirm'];

    function skipped(el) {
        for (var n = el; n && n.nodeType === 1; n = n.parentNode) {
            if (skipTags[n.tagName] || n.hasAttribute('data-i18n-skip')) { return true; }
        }
        return false;
    }

    function translateAttrs(el) {
        for (var i = 0; i < attrs.length; i++) {
            var v = el.getAttribute(attrs[i]);
            if (v) { var t = translate(v); if (t !== null && t !== v) { el.setAttribute(attrs[i], t); } }
        }
        if (el.tagName === 'INPUT' && /^(submit|button|reset)$/i.test(el.type) && el.value) {
            var tv = translate(el.value);
            if (tv !== null && tv !== el.value) { el.value = tv; }
        }
    }

    function walk(root) {
        if (root.nodeType === 3) {
            if (root.parentNode && !skipped(root.parentNode)) { translateText(root); }
            return;
        }
        if (root.nodeType !== 1 || skipped(root)) { return; }
        translateAttrs(root);
        for (var c = root.firstChild; c; c = c.nextSibling) { walk(c); }
    }

    function translateText(node) {
        var t = translate(node.nodeValue);
        if (t !== null && t !== node.nodeValue) { node.nodeValue = t; }
    }

    function translateTitle() {
        var parts = document.title.split(' - ');
        var out = parts.map(function (p) { return window.imsT(p); }).join(' - ');
        if (out !== document.title) { document.title = out; }
    }

    // Native dialogs.
    var nativeConfirm = window.confirm, nativeAlert = window.alert;
    window.confirm = function (m) { return nativeConfirm.call(window, window.imsT(String(m))); };
    window.alert = function (m) { return nativeAlert.call(window, window.imsT(String(m))); };

    // DataTables wording, picked up by site.js.
    window.IMS_DT_LANG = {
        emptyTable: 'কোনো রেকর্ড পাওয়া যায়নি।',
        zeroRecords: 'কোনো মিল পাওয়া যায়নি।',
        info: '_TOTAL_ টির মধ্যে _START_ থেকে _END_ দেখানো হচ্ছে',
        infoEmpty: '০ টি এন্ট্রি',
        infoFiltered: '(মোট _MAX_ টি থেকে ছাঁকা)',
        lengthMenu: '_MENU_ টি দেখান',
        search: 'খুঁজুন:',
        paginate: { first: 'প্রথম', last: 'শেষ', next: 'পরের', previous: 'আগের' }
    };

    var busy = false;
    var observer = new MutationObserver(function (records) {
        if (busy) { return; }
        busy = true;
        try {
            records.forEach(function (r) {
                if (r.type === 'childList') { r.addedNodes.forEach(function (n) { walk(n); }); }
                else if (r.type === 'characterData') { if (r.target.parentNode && !skipped(r.target.parentNode)) { translateText(r.target); } }
                else if (r.type === 'attributes' && !skipped(r.target)) { translateAttrs(r.target); }
            });
            translateTitle();
        } finally { busy = false; }
    });

    function start() {
        busy = true;
        try { walk(document.body); translateTitle(); } finally { busy = false; }
        observer.observe(document.body, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: attrs });
    }
    if (document.readyState === 'loading') { document.addEventListener('DOMContentLoaded', start); } else { start(); }
})();
