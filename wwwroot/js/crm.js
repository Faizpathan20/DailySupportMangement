/* =========================================================
   CRM module - shared grid / modal / export behaviour.

   SPA-safe: the app swaps <body> and re-injects scripts on
   soft navigation (inline scripts run before external ones
   finish loading). So we NEVER rely on an inline init call;
   instead each page renders a JSON config block and this file
   self-initialises on DOMContentLoaded (which the shell
   dispatches only after every external script has loaded).
========================================================= */

(function () {
    "use strict";

    var cfg = null;
    var currentPage = 1;
    var recordsPerPage = 10;
    var totalRecords = 0;
    var dataRows = [];
    var filteredRows = [];

    function id(x) {
        return document.getElementById(x);
    }

    function text(cell) {
        return (cell ? cell.innerText || cell.textContent || "" : "").trim();
    }

    // ==========================================
    // MODAL STYLE SAFETY NET
    // The modal's look comes from site.css. If that stylesheet
    // is stale or not yet deployed, the overlay falls back to a
    // plain block in the page flow: it is never hidden and the
    // page shows through it while scrolling. Detect that case
    // (position is not "fixed") and apply the critical styles
    // inline so the modal is always a solid, full-screen overlay.
    // When the stylesheet IS present this does nothing.
    // ==========================================

    function modalStylesLoaded(overlay) {
        if (!window.getComputedStyle) {
            return true;
        }
        var pos = window.getComputedStyle(overlay).position;
        return pos === "fixed" || pos === "absolute";
    }

    function ensureModalBase(overlay) {
        if (!overlay || modalStylesLoaded(overlay)) {
            return;
        }

        overlay.style.position = "fixed";
        overlay.style.top = "0";
        overlay.style.left = "0";
        overlay.style.right = "0";
        overlay.style.bottom = "0";
        overlay.style.zIndex = "2000";
        overlay.style.background = "rgba(15, 23, 42, 0.6)";
        overlay.style.alignItems = "flex-start";
        overlay.style.justifyContent = "center";
        overlay.style.padding = "24px";
        overlay.style.overflowY = "auto";

        var box = overlay.querySelector(".crm-modal-box") ||
            overlay.firstElementChild;
        if (box) {
            box.style.background = "#ffffff";
            box.style.width = "100%";
            box.style.maxWidth = "760px";
            box.style.borderRadius = "18px";
            box.style.margin = "auto";
            box.style.boxShadow =
                "0 30px 70px -30px rgba(15, 23, 42, 0.6)";
            box.style.display = "flex";
            box.style.flexDirection = "column";
            box.style.maxHeight = "calc(100vh - 48px)";
            box.style.overflow = "hidden";

            var body = box.querySelector(".crm-modal-body");
            if (body) {
                body.style.overflowY = "auto";
                body.style.flex = "1 1 auto";
                body.style.minHeight = "0";
            }
        }
    }

    // ==========================================
    // INIT
    // ==========================================

    // Re-read the grid rows from the DOM (used after the table
    // body has been swapped by refresh()).
    function bindRows() {
        var table = id(cfg.tableId);

        dataRows = table
            ? Array.prototype.filter.call(
                table.querySelectorAll("tbody tr"),
                function (tr) {
                    return !tr.classList.contains("no-data-row");
                })
            : [];

        totalRecords = cfg.totalRecords || dataRows.length;
    }

    function init(config) {
        cfg = config || {};
        recordsPerPage = cfg.recordsPerPage || 10;
        totalRecords = cfg.totalRecords || 0;

        var rp = id(cfg.recordsId);
        if (rp) {
            rp.value = String(recordsPerPage);
        }

        bindRows();
        render();

        var overlay = id(cfg.modalId);
        if (overlay && overlay.dataset.crmBackdrop !== "1") {
            overlay.dataset.crmBackdrop = "1";

            overlay.addEventListener("click", function (event) {
                if (event.target === overlay) {
                    closeModal();
                }
            });
        }

        // Without the stylesheet the modal is a plain block and
        // would be visible on load. Hide it until it is opened.
        if (overlay && !modalStylesLoaded(overlay)) {
            overlay.style.display = "none";
        }

        // The Log Outcome modal (Daily Follow-up) behaves the same
        // as the edit modal: click-outside closes, and it stays
        // hidden if the stylesheet has not arrived yet.
        var outcomeOverlay = id(cfg.outcomeModalId);

        if (outcomeOverlay
            && outcomeOverlay.dataset.crmBackdrop !== "1") {

            outcomeOverlay.dataset.crmBackdrop = "1";

            outcomeOverlay.addEventListener("click", function (event) {
                if (event.target === outcomeOverlay) {
                    closeOutcome();
                }
            });
        }

        if (outcomeOverlay && !modalStylesLoaded(outcomeOverlay)) {
            outcomeOverlay.style.display = "none";
        }

        if (cfg.openAddOnLoad && id(cfg.modalId)) {
            openAdd();
        }
    }

    // ==========================================
    // SEARCH / RENDER
    // ==========================================

    function render() {
        var searchEl = id(cfg.searchId);
        var searchValue = searchEl
            ? (searchEl.value || "").toLowerCase().trim()
            : "";

        filteredRows = dataRows.filter(function (tr) {
            var s = (tr.getAttribute("data-search") || "").toLowerCase();
            return s.indexOf(searchValue) !== -1;
        });

        totalRecords = filteredRows.length;

        var totalPages = Math.ceil(totalRecords / recordsPerPage) || 0;

        if (currentPage > totalPages) {
            currentPage = totalPages > 0 ? totalPages : 1;
        }

        var start = (currentPage - 1) * recordsPerPage;
        var end = start + recordsPerPage;
        if (end > totalRecords) {
            end = totalRecords;
        }

        dataRows.forEach(function (tr) {
            tr.style.display = "none";
        });

        for (var i = start; i < end; i++) {
            filteredRows[i].style.display = "";
        }

        var noRec = id(cfg.noRecordsId);
        if (noRec) {
            noRec.style.display =
                filteredRows.length === 0 ? "" : "none";
        }

        var info = id(cfg.infoId);
        if (info) {
            info.innerText = totalRecords === 0
                ? "Showing 0 to 0 of 0 entries"
                : "Showing " + (start + 1) + " to " + end
                  + " of " + totalRecords + " entries";
        }

        renderPagination(totalPages);
    }

    function renderPagination(totalPages) {
        var el = id(cfg.paginationId);
        if (!el) {
            return;
        }

        var html = "";

        html += currentPage === 1
            ? '<button class="crm-page-btn" disabled>&larr; Prev</button>'
            : '<button class="crm-page-btn" onclick="crmGoToPage('
              + (currentPage - 1) + ')">&larr; Prev</button>';

        for (var i = 1; i <= totalPages; i++) {
            html += i === currentPage
                ? '<button class="crm-page-btn active">' + i + '</button>'
                : '<button class="crm-page-btn" onclick="crmGoToPage('
                  + i + ')">' + i + '</button>';
        }

        html += (currentPage === totalPages || totalPages === 0)
            ? '<button class="crm-page-btn" disabled>Next &rarr;</button>'
            : '<button class="crm-page-btn" onclick="crmGoToPage('
              + (currentPage + 1) + ')">Next &rarr;</button>';

        el.innerHTML = html;
    }

    // ==========================================
    // SORT
    // ==========================================

    function parseDateValue(s) {
        var m = s.match(
            /^(\d{2})\/(\d{2})\/(\d{4})(?:\s+(\d{2}):(\d{2})\s*(AM|PM))?/i);
        if (!m) {
            return NaN;
        }
        var d = new Date(
            parseInt(m[3]), parseInt(m[2]) - 1, parseInt(m[1]));
        if (m[4]) {
            var h = parseInt(m[4]) % 12;
            if ((m[6] || "").toUpperCase() === "PM") {
                h += 12;
            }
            d.setHours(h, parseInt(m[5]));
        }
        return d.getTime();
    }

    function sortTable(th) {
        if (!cfg) {
            return;
        }

        var table = id(cfg.tableId);
        var headerRow = th.parentNode;
        var index = Array.prototype.indexOf.call(headerRow.children, th);

        var type = th.getAttribute("data-sort") || "text";
        var asc = th.getAttribute("data-asc") !== "true";
        th.setAttribute("data-asc", asc ? "true" : "false");

        var tbody = table.querySelector("tbody");

        dataRows.sort(function (a, b) {
            var ca = a.children[index];
            var cb = b.children[index];
            var va = text(ca);
            var vb = text(cb);

            var na, nb;

            if (type === "num") {
                na = parseFloat(va.replace(/[^0-9.\-]/g, ""));
                nb = parseFloat(vb.replace(/[^0-9.\-]/g, ""));
                if (isNaN(na)) { na = -Infinity; }
                if (isNaN(nb)) { nb = -Infinity; }
            } else if (type === "date") {
                na = parseDateValue(va);
                nb = parseDateValue(vb);
                if (isNaN(na)) { na = -Infinity; }
                if (isNaN(nb)) { nb = -Infinity; }
            } else {
                na = va.toLowerCase();
                nb = vb.toLowerCase();
            }

            if (na < nb) { return asc ? -1 : 1; }
            if (na > nb) { return asc ? 1 : -1; }
            return 0;
        });

        dataRows.forEach(function (tr) {
            tbody.appendChild(tr);
        });

        currentPage = 1;
        render();
    }

    function goToPage(p) {
        currentPage = p;
        render();
    }

    function changeRecordsPerPage() {
        var rp = id(cfg.recordsId);
        recordsPerPage = parseInt(rp.value, 10) || 10;
        currentPage = 1;
        render();
    }

    function filterTable() {
        currentPage = 1;
        render();
    }

    // ==========================================
    // MODAL
    // ==========================================

    function toast(message, type) {
        if (typeof window.masterShowToast === "function") {
            window.masterShowToast(message, type || "success");
        }
    }

    function clearErrors() {
        var form = id(cfg.formId);
        if (!form) {
            return;
        }
        Array.prototype.forEach.call(
            form.querySelectorAll(".crm-field-error"),
            function (e) { e.remove(); });
        Array.prototype.forEach.call(
            form.querySelectorAll(".crm-has-error"),
            function (e) { e.classList.remove("crm-has-error"); });
    }

    function resetForm() {
        var form = id(cfg.formId);
        if (!form) {
            return;
        }
        form.reset();
        var idf = id(cfg.idFieldId);
        if (idf) {
            idf.value = "0";
        }
        clearErrors();
    }

    function openAdd() {
        resetForm();
        var form = id(cfg.formId);
        if (!form) {
            return;
        }
        form.action = cfg.saveUrl;
        setModalMode(false);
        showModal();
    }

    // Open the add modal and pre-fill some fields (e.g. LeadId
    // when adding a follow-up from the Lead Detail page).
    function openAddPrefilled(values) {
        openAdd();

        var form = id(cfg.formId);
        if (!form || !values) {
            return;
        }

        Object.keys(values).forEach(function (name) {
            var el = form.elements.namedItem(name);
            if (el) {
                fillField(el, values[name]);
            }
        });
    }

    function fillField(el, value) {
        if (el.tagName === "SELECT") {
            el.value = value;
        } else if (el.type === "checkbox") {
            el.checked = value === "1" || value === "true";
        } else {
            el.value = value;
        }
    }

    function openEdit(btn) {
        var data;
        try {
            data = JSON.parse(btn.getAttribute("data-json"));
        } catch (e) {
            data = {};
        }

        var form = id(cfg.formId);
        if (!form) {
            return;
        }

        form.reset();
        clearErrors();

        Array.prototype.forEach.call(form.elements, function (el) {
            var name = el.name;
            if (!name || name === "__RequestVerificationToken") {
                return;
            }

            var value = "";
            if (data[name + "_key"] !== undefined) {
                value = data[name + "_key"];
            } else if (data[name + "_edit"] !== undefined) {
                value = data[name + "_edit"];
            } else if (data[name] !== undefined) {
                value = data[name];
            }

            fillField(el, value);
        });

        var idf = id(cfg.idFieldId);
        if (idf) {
            idf.value = data["Id"] || "0";
        }

        form.action = cfg.saveUrl;
        setModalMode(true);
        showModal();
    }

    function setModalMode(editing) {
        var title = id(cfg.modalTitleId);
        var submit = id(cfg.submitId);
        if (title) {
            title.innerText = editing
                ? ("Edit " + cfg.entityLabel)
                : ("Add " + cfg.entityLabel);
        }
        if (submit) {
            submit.innerText = editing ? "Update" : "Save";
        }
    }

    function showModal() {
        var m = id(cfg.modalId);
        if (m) {
            ensureModalBase(m);
            m.style.display = "flex";
            document.body.classList.add("crm-modal-open");
        }
    }

    function closeModal() {
        var m = id(cfg.modalId);
        if (m) {
            m.style.display = "none";
            document.body.classList.remove("crm-modal-open");
        }
    }

    // ==========================================
    // IN-PLACE REFRESH (after a save / delete)
    // Re-fetches the current URL and swaps the table body and
    // any [data-kpi] counters, so toasts stay visible and no
    // full page reload is needed.
    // ==========================================

    function refresh(done) {
        fetch(window.location.href, {
            credentials: "same-origin",
            headers: { "X-Requested-With": "XMLHttpRequest" }
        })
            .then(function (r) { return r.text(); })
            .then(function (html) {
                var doc = new DOMParser()
                    .parseFromString(html, "text/html");

                var next = doc.querySelector(
                    "#" + cfg.tableId + " tbody");
                var current = document.querySelector(
                    "#" + cfg.tableId + " tbody");

                if (!next || !current) {
                    window.location.reload();
                    return;
                }

                current.innerHTML = next.innerHTML;

                var keys = doc.querySelectorAll("[data-kpi]");

                Array.prototype.forEach.call(keys, function (src) {
                    var key = src.getAttribute("data-kpi");

                    if (!key) {
                        return;
                    }

                    var mine = document.querySelector(
                        '[data-kpi="' + key + '"]');

                    if (mine) {
                        mine.textContent = src.textContent;
                    }
                });

                bindRows();
                render();

                if (done) {
                    done();
                }
            })
            .catch(function () {
                window.location.reload();
            });
    }

    // ==========================================
    // SAVE
    // ==========================================

    function submitForm(event) {
        if (event) {
            event.preventDefault();
        }

        var form = id(cfg.formId);
        clearErrors();

        function saved(data) {
            toast(
                data && data.message
                    ? data.message
                    : "Saved.",
                "success");

            refresh(function () {
                closeModal();
            });
        }

        if (typeof window.masterAjaxPost === "function") {
            window.masterAjaxPost(form, saved);
            return false;
        }

        fetch(cfg.saveUrl, {
            method: "POST",
            headers: { "X-Requested-With": "XMLHttpRequest" },
            body: new FormData(form)
        })
            .then(function (r) { return r.json(); })
            .then(function (data) {
                if (data.success) {
                    saved(data);
                } else {
                    toast(data.message || "Could not save.", "error");
                }
            })
            .catch(function () {
                toast("Network error. Please try again.", "error");
            });

        return false;
    }

    // ==========================================
    // CONFIRM + SUBMIT
    // ==========================================

    function confirmAndSubmit(form, message) {
        if (!window.confirm(message)) {
            return false;
        }

        function done(data) {
            toast(
                data && data.message
                    ? data.message
                    : "Done.",
                "success");

            refresh();
        }

        if (typeof window.masterAjaxPost === "function") {
            window.masterAjaxPost(form, done);
            return false;
        }

        fetch(form.getAttribute("action") || form.action, {
            method: "POST",
            headers: { "X-Requested-With": "XMLHttpRequest" },
            body: new FormData(form)
        })
            .then(function (r) { return r.json(); })
            .then(function (data) {
                if (data.success) {
                    done(data);
                } else {
                    toast(data.message || "Request failed.", "error");
                }
            })
            .catch(function () {
                toast("Network error. Please try again.", "error");
            });

        return false;
    }

    // ==========================================
    // LOG OUTCOME (Daily Follow-up)
    // Discipline rule: a done follow-up must either get its next
    // date or close the lead - enforced on the server too.
    // ==========================================

    function isoDay(date) {
        var m = date.getMonth() + 1;
        var d = date.getDate();

        return date.getFullYear()
            + "-" + (m < 10 ? "0" : "") + m
            + "-" + (d < 10 ? "0" : "") + d;
    }

    function tomorrow() {
        var d = new Date();
        d.setDate(d.getDate() + 1);
        return isoDay(d);
    }

    function pick(data, primary, fallback) {
        if (data[primary] !== undefined && data[primary] !== "") {
            return data[primary];
        }
        return data[fallback] !== undefined ? data[fallback] : "";
    }

    function setVal(elId, value) {
        var el = id(elId);
        if (el && value !== undefined && value !== null) {
            el.value = value;
        }
    }

    function openOutcome(btn) {
        var data;
        try {
            data = JSON.parse(btn.getAttribute("data-json"));
        } catch (e) {
            data = {};
        }

        var form = id(cfg.outcomeFormId);
        if (!form) {
            return;
        }

        form.reset();

        setVal("outcomeId", data["Id"] || "0");
        setVal("outcomeRemarks", pick(data, "Remarks_edit", "Remarks"));
        setVal("outcomeNextAction",
            pick(data, "NextAction_edit", "NextAction"));
        setVal("outcomeContactMode", data["ContactMode"] || "");

        var time = pick(data, "FollowUpTime_edit", "FollowUpTime");
        setVal("outcomeNextTime", time ? String(time).slice(0, 5) : "");

        // Default the next date: the row's own plan, else tomorrow.
        var nextDate = pick(data, "NextFollowUpDate_edit", "");
        setVal("outcomeNextDate", nextDate || tomorrow());

        var leadEl = id("outcomeLead");
        if (leadEl) {
            var lead = btn.getAttribute("data-lead") || data["LeadId"] || "";
            leadEl.textContent = lead ? ("Lead: " + lead) : "";
        }

        var closeEl = id("outcomeCloseLead");
        if (closeEl) {
            closeEl.checked = false;
        }

        toggleCloseReason();

        ensureModalBase(id(cfg.outcomeModalId));

        var overlay = id(cfg.outcomeModalId);
        if (overlay) {
            overlay.style.display = "flex";
            document.body.classList.add("crm-modal-open");
        }
    }

    function closeOutcome() {
        var overlay = id(cfg.outcomeModalId);
        if (overlay) {
            overlay.style.display = "none";
            document.body.classList.remove("crm-modal-open");
        }
    }

    function toggleCloseReason() {
        var closeEl = id("outcomeCloseLead");
        var wrap = id("outcomeReasonWrap");

        if (wrap) {
            wrap.style.display =
                closeEl && closeEl.checked ? "" : "none";
        }
    }

    function submitOutcome(event) {
        if (event) {
            event.preventDefault();
        }

        var form = id(cfg.outcomeFormId);
        if (!form) {
            return false;
        }

        var closeEl = id("outcomeCloseLead");
        var dateEl = id("outcomeNextDate");
        var isClose = closeEl && closeEl.checked;

        if (!isClose && (!dateEl || !dateEl.value)) {
            toast(
                "Pick the next follow-up date, or close the lead.",
                "error");

            if (dateEl) {
                dateEl.focus();
            }

            return false;
        }

        function saved(data) {
            toast(
                data && data.message ? data.message : "Outcome saved.",
                "success");

            closeOutcome();

            refresh(function () {
                if (typeof window.refreshFollowUpBadge === "function") {
                    window.refreshFollowUpBadge();
                }
            });
        }

        if (typeof window.masterAjaxPost === "function") {
            window.masterAjaxPost(form, saved);
            return false;
        }

        fetch(cfg.completeUrl, {
            method: "POST",
            headers: { "X-Requested-With": "XMLHttpRequest" },
            body: new FormData(form)
        })
            .then(function (r) { return r.json(); })
            .then(function (data) {
                if (data.success) {
                    saved(data);
                } else {
                    toast(data.message || "Could not save.", "error");
                }
            })
            .catch(function () {
                toast("Network error. Please try again.", "error");
            });

        return false;
    }

    // ==========================================
    // SNOOZE (one-click "move to tomorrow")
    // ==========================================

    function snooze(btn) {
        var rowId = btn.getAttribute("data-id");
        if (!rowId || !cfg.snoozeUrl) {
            return;
        }

        btn.disabled = true;

        var tokenEl = document.querySelector(
            '#' + cfg.formId
            + ' input[name="__RequestVerificationToken"]');

        var body = new FormData();
        body.append("id", rowId);
        body.append("newDate", tomorrow());

        if (tokenEl) {
            body.append("__RequestVerificationToken", tokenEl.value);
        }

        function done(ok) {
            btn.disabled = false;
            if (ok) {
                refresh(function () {
                    if (typeof window.refreshFollowUpBadge === "function") {
                        window.refreshFollowUpBadge();
                    }
                });
            }
        }

        fetch(cfg.snoozeUrl, {
            method: "POST",
            credentials: "same-origin",
            headers: { "X-Requested-With": "XMLHttpRequest" },
            body: body
        })
            .then(function (r) { return r.json(); })
            .then(function (data) {
                if (data && data.success) {
                    toast(data.message || "Follow-up moved.", "success");
                    done(true);
                } else {
                    toast(
                        (data && data.message)
                            || "Could not reschedule.",
                        "error");
                    done(false);
                }
            })
            .catch(function () {
                toast("Network error. Please try again.", "error");
                done(false);
            });
    }

    // ==========================================
    // EXPORT
    // ==========================================

    function visibleRows() {
        return dataRows.filter(function (tr) {
            return tr.style.display !== "none";
        });
    }

    function exportExcel() {
        if (typeof XLSX === "undefined") {
            toast("Excel export library not loaded yet.", "error");
            return;
        }

        var table = id(cfg.tableId);
        var headerCells = table.querySelectorAll("thead th");
        var aoa = [];

        var head = [];
        for (var h = 0; h < headerCells.length - 1; h++) {
            head.push(text(headerCells[h]));
        }
        aoa.push(head);

        visibleRows().forEach(function (tr) {
            var cells = tr.querySelectorAll("td");
            var row = [];
            for (var c = 0; c < cells.length - 1; c++) {
                row.push(text(cells[c]));
            }
            aoa.push(row);
        });

        var ws = XLSX.utils.aoa_to_sheet(aoa);
        var wb = XLSX.utils.book_new();
        XLSX.utils.book_append_sheet(wb, ws, cfg.entityLabel);
        XLSX.writeFile(wb, cfg.exportName + ".xlsx");
    }

    function exportCSV() {
        var table = id(cfg.tableId);
        var headerCells = table.querySelectorAll("thead th");

        var lines = [];
        var head = [];
        for (var h = 0; h < headerCells.length - 1; h++) {
            head.push('"' + text(headerCells[h]).replace(/"/g, '""') + '"');
        }
        lines.push(head.join(","));

        visibleRows().forEach(function (tr) {
            var cells = tr.querySelectorAll("td");
            var row = [];
            for (var c = 0; c < cells.length - 1; c++) {
                row.push('"' + text(cells[c]).replace(/"/g, '""') + '"');
            }
            lines.push(row.join(","));
        });

        var blob = new Blob([lines.join("\r\n")], {
            type: "text/csv;charset=utf-8;"
        });

        var a = document.createElement("a");
        a.href = URL.createObjectURL(blob);
        a.download = cfg.exportName + ".csv";
        a.click();
    }

    function printTable() {
        var table = id(cfg.tableId);
        var w = window.open("", "_blank");

        w.document.write(
            "<html><head><title>" + cfg.entityLabel + "</title>"
            + "<style>body{font-family:Arial,sans-serif;padding:20px;}"
            + "table{width:100%;border-collapse:collapse;}"
            + "th,td{padding:8px;text-align:left;border-bottom:1px solid #ddd;}"
            + "th{background:#f3f4f6;}</style></head><body>");

        w.document.write("<h1>" + cfg.entityLabel + "</h1>");

        var clone = table.cloneNode(true);

        Array.prototype.forEach.call(
            clone.querySelectorAll("tbody tr"),
            function (tr) {
                if (tr.style.display === "none"
                    || tr.classList.contains("no-data-row")) {
                    tr.remove();
                } else {
                    var cells = tr.querySelectorAll("td");
                    if (cells.length) {
                        cells[cells.length - 1].remove();
                    }
                }
            });

        Array.prototype.forEach.call(
            clone.querySelectorAll("thead th.crm-actions-col"),
            function (th) { th.remove(); });

        w.document.write(clone.outerHTML);
        w.document.write("</body></html>");
        w.document.close();
        w.print();
    }

    // ==========================================
    // BOOT (SPA-safe)
    // ==========================================

    function boot() {
        var el = document.querySelector(".crm-config");
        if (!el || el.dataset.crmInited === "1") {
            return;
        }
        el.dataset.crmInited = "1";

        var parsed;
        try {
            parsed = JSON.parse(el.textContent);
        } catch (e) {
            return;
        }

        init(parsed);
    }

    document.addEventListener("DOMContentLoaded", boot);

    if (document.readyState !== "loading") {
        boot();
    }

    // ==========================================
    // PUBLIC API
    // ==========================================

    window.crmInit = init;
    window.crmRefresh = refresh;
    window.crmFilter = filterTable;
    window.crmChangeRecordsPerPage = changeRecordsPerPage;
    window.crmSort = sortTable;
    window.crmGoToPage = goToPage;
    window.crmOpenAdd = openAdd;
    window.crmOpenAddPrefilled = openAddPrefilled;
    window.crmOpenEdit = openEdit;
    window.crmCloseModal = closeModal;
    window.crmSubmitForm = submitForm;
    window.crmConfirmAndSubmit = confirmAndSubmit;
    window.crmOpenOutcome = openOutcome;
    window.crmCloseOutcome = closeOutcome;
    window.crmSubmitOutcome = submitOutcome;
    window.crmToggleCloseReason = toggleCloseReason;
    window.crmSnooze = snooze;
    window.crmExportExcel = exportExcel;
    window.crmExportCSV = exportCSV;
    window.crmPrint = printTable;
})();
