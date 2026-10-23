// ==========================================
// SIDEBAR COLLAPSE + MOBILE DRAWER + ACTIVE MENU
// ==========================================

(function () {

    var sidebar =
        document.getElementById("appSidebar");

    var toggle =
        document.getElementById("sidebarToggle");

    var mobileBtn = document.getElementById("mobileMenuBtn");
    var backdrop = document.getElementById("sidebarBackdrop");

    var STORAGE_KEY = "sidebarCollapsed";
    var mobileQuery = window.matchMedia ? window.matchMedia("(max-width: 700px)") : null;

    function isMobile() {
        return mobileQuery ? mobileQuery.matches : window.innerWidth <= 700;
    }

    function openMobileSidebar() {
        document.body.classList.add("sidebar-open");
        if (mobileBtn) {
            mobileBtn.setAttribute("aria-expanded", "true");
        }
        document.body.style.overflow = "hidden";
    }

    function closeMobileSidebar() {
        document.body.classList.remove("sidebar-open");
        if (mobileBtn) {
            mobileBtn.setAttribute("aria-expanded", "false");
        }
        document.body.style.overflow = "";
    }

    function toggleMobileSidebar() {
        if (document.body.classList.contains("sidebar-open")) {
            closeMobileSidebar();
        } else {
            openMobileSidebar();
        }
    }

    if (mobileBtn) {
        mobileBtn.addEventListener("click", function (e) {
            e.preventDefault();
            e.stopPropagation();
            toggleMobileSidebar();
        });
    }

    if (backdrop) {
        backdrop.addEventListener("click", closeMobileSidebar);
    }

    document.addEventListener("keydown", function (e) {
        if (e.key === "Escape") {
            closeMobileSidebar();
        }
    });

    if (sidebar) {
        sidebar.addEventListener("click", function (e) {
            if (!isMobile()) return;
            var target = e.target.closest("a.menu-item[href], button.menu-item");
            if (target) {
                closeMobileSidebar();
            }
        });
    }

    if (mobileQuery && mobileQuery.addEventListener) {
        mobileQuery.addEventListener("change", function () {
            if (!isMobile()) {
                closeMobileSidebar();
            }
        });
    } else if (mobileQuery && mobileQuery.addListener) {
        mobileQuery.addListener(function () {
            if (!isMobile()) {
                closeMobileSidebar();
            }
        });
    }

    if (!sidebar || !toggle) {
        return;
    }


    function applyCollapsed(collapsed) {

        document.body.classList.toggle(
            "sidebar-collapsed",
            collapsed
        );

        toggle.setAttribute(
            "aria-expanded",
            collapsed ? "false" : "true"
        );

    }


    // Restore the persisted state (expanded by default).
    applyCollapsed(
        localStorage.getItem(STORAGE_KEY) === "1"
    );


    toggle.addEventListener("click", function () {

        if (isMobile()) {
            toggleMobileSidebar();
            return;
        }

        var collapsed =
            document.body.classList.contains(
                "sidebar-collapsed"
            );

        collapsed = !collapsed;

        localStorage.setItem(
            STORAGE_KEY,
            collapsed ? "1" : "0"
        );

        applyCollapsed(collapsed);

    });


    // Highlight the sidebar link for the current page.
    var currentPath =
        window.location.pathname;

    document
        .querySelectorAll(
            ".sidebar .menu-item[href]"
        )
        .forEach(function (link) {

            var linkPath =
                new URL(link.href).pathname;

            var matches =
                linkPath === currentPath
                || (currentPath.endsWith("/")
                    && linkPath
                        === currentPath.slice(0, -1));

            // The application root lands on Dashboard.
            if (
                currentPath === "/"
                && (linkPath === "/Dashboard"
                    || linkPath === "/Dashboard/")
            ) {
                matches = true;
            }

            if (matches) {
                link.classList.add("active");
            }

        });

})();

// ==========================================
// FULL-PAGE APP LOADER + SOFT NAVIGATION
// Sidebar links are fetched via AJAX so the
// loader covers the real server + database
// time, then the new page swaps in without a
// full page reload (no browser blank gap).
// ==========================================

(function () {

    var loader =
        document.getElementById("appLoader");

    function showAppLoader() {

        if (loader) {
            loader.classList.add("visible");
        }

    }

    function hideAppLoader() {

        if (loader) {
            loader.classList.remove("visible");
        }

    }

    // Exposed so pages can trigger it if needed.
    window.showAppLoader = showAppLoader;
    window.hideAppLoader = hideAppLoader;

    // On the first (full) page load the loader stays
    // visible until the DOM has been parsed - the real
    // server + render time. No fixed-time timers.
    if (loader && document.readyState === "loading") {

        showAppLoader();

        document.addEventListener(
            "DOMContentLoaded",
            hideAppLoader
        );

    }


    // ---------- SOFT NAVIGATION ----------

    var navInProgress = false;

    // Injects scripts from the fetched page into the
    // current document, then calls onAllLoaded once every
    // external script has fired its load event.
    //
    // IMPORTANT: old.src is the IDL attribute, which the
    // DOMParser resolves against the parsed document's base
    // URL (about:blank). That turns /js/import.js into
    // about:///js/import.js — an invalid URL that silently
    // fails to load. getAttribute("src") returns the raw
    // content-attribute value (/js/import.js), which the
    // browser resolves against window.location correctly.
    function injectScripts(doc, onAllLoaded) {

        var scripts =
            doc.querySelectorAll("script");

        var pending = 0;

        function done() {
            pending--;
            if (pending <= 0 && onAllLoaded) {
                onAllLoaded();
            }
        }

        for (var i = 0; i < scripts.length; i++) {

            var old = scripts[i];

            var fresh =
                document.createElement("script");

            // Use getAttribute to get the raw src path so the
            // browser resolves it against window.location, not
            // the DOMParser document's about:blank base URL.
            var rawSrc = old.getAttribute("src");

            if (rawSrc) {

                pending++;
                fresh.src = rawSrc;
                fresh.defer = false;
                fresh.async = false;
                fresh.addEventListener("load", done);
                fresh.addEventListener("error", done);

            } else if (old.textContent) {

                fresh.textContent =
                    old.textContent;

            } else {

                continue;

            }

            document.body.appendChild(fresh);

        }

        // If there were no external scripts at all, fire
        // the callback immediately.
        if (pending === 0 && onAllLoaded) {
            onAllLoaded();
        }

    }

    function swapPage(url, html, push) {

        var doc =
            new DOMParser().parseFromString(
                html,
                "text/html"
            );

        document.title = doc.title;

        // Body ke class attributes innerHTML replace
        // ke saath copy nahi hote. Page-specific body
        // class (e.g. gray canvas) soft navigation ke
        // baad bhi chahiye, isliye class bhi copy karo.
        // Sidebar ke state classes ko nahi chhedna.
        var stateClasses = [
            "sidebar-collapsed",
            "sidebar-open"
        ];

        var nextClasses =
            (doc.body.className || "")
                .split(/\s+/)
                .filter(function (c) {
                    return c.length > 0;
                });

        stateClasses.forEach(function (c) {

            if (document.body.classList.contains(c) &&
                nextClasses.indexOf(c) === -1) {

                nextClasses.push(c);

            }

        });

        document.body.className =
            nextClasses.join(" ");

        document.body.innerHTML =
            doc.body.innerHTML;

        // Page-specific stylesheets (@section Styles se aate
        // hain - jaise tailwind.css / dailysupport.css) head
        // me rehte hain. Soft navigation me head replace nahi
        // hota, isliye unhe yahan sync karte hain: naye page
        // ki CSS add, purani page ki CSS hata.
        var currentLinks =
            Array.prototype.slice.call(
                document.head.querySelectorAll(
                    'link[rel="stylesheet"]'
                )
            );

        var nextLinks =
            Array.prototype.slice.call(
                doc.head.querySelectorAll(
                    'link[rel="stylesheet"]'
                )
            );

        var nextHrefs = nextLinks.map(function (l) {
            return l.getAttribute("href");
        });

        currentLinks.forEach(function (link) {

            var href = link.getAttribute("href");

            // Sirf humne add kiye hue links hataye jaate
            // hain (site.css / dashboard.css safe rahenge).
            if (link.getAttribute("data-softnav") === "1" &&
                nextHrefs.indexOf(href) === -1) {

                link.remove();

            }

        });

        nextLinks.forEach(function (link) {

            var href = link.getAttribute("href");

            var exists = currentLinks.some(
                function (l) {
                    return l.getAttribute("href") === href;
                }
            );

            if (!exists) {

                var fresh =
                    document.createElement("link");

                fresh.rel = "stylesheet";
                fresh.setAttribute("href", href);
                fresh.setAttribute("data-softnav", "1");

                document.head.appendChild(fresh);

            }

        });

        if (push) {
            history.pushState({}, "", url);
        }

        // Inject scripts and dispatch DOMContentLoaded only
        // after every external script has loaded. Without
        // this, pages whose init code runs on DOMContentLoaded
        // would fire before their own scripts had executed.
        injectScripts(doc, function () {

            document.dispatchEvent(
                new Event("DOMContentLoaded")
            );

            window.scrollTo(0, 0);
            hideAppLoader();
            navInProgress = false;

        });

    }

    function loadPage(url, push) {

        if (navInProgress) {
            return;
        }

        navInProgress = true;
        showAppLoader();

        fetch(url, {
            credentials: "same-origin",
            headers: {
                "X-Requested-With": "XMLHttpRequest"
            }
        })
            .then(function (response) {

                // Auth failures bounce to the login page:
                // let the browser move there normally.
                if (response.redirected) {

                    hideAppLoader();
                    navInProgress = false;

                    window.location.href = url;

                    return null;

                }

                if (!response.ok) {
                    throw new Error(
                        "HTTP " + response.status
                    );
                }

                return response.text();

            })
            .then(function (html) {

                if (html !== null) {
                    swapPage(url, html, push);
                }

            })
            .catch(function () {

                hideAppLoader();
                navInProgress = false;

                window.location.href = url;

            });

    }

    function isSamePage(url) {

        var target =
            new URL(url).pathname;

        var current =
            window.location.pathname;

        return target === current
            || (target.endsWith("/")
                && target.slice(0, -1) === current)
            || (current.endsWith("/")
                && current.slice(0, -1) === target);

    }

    // Sidebar links -> load via fetch with the loader
    // visible for the whole server + database time.
    document
        .querySelectorAll(
            ".sidebar-menu a.menu-item[href]"
        )
        .forEach(function (link) {

            link.addEventListener(
                "click",
                function (event) {

                    if (event.defaultPrevented) {
                        return;
                    }

                    if (event.button !== 0) {
                        return;
                    }

                    if (
                        event.metaKey
                        || event.ctrlKey
                        || event.shiftKey
                        || event.altKey
                    ) {
                        return;
                    }

                    var href =
                        link.getAttribute("href");

                    if (!href || href === "#") {
                        return;
                    }

                    // Already on the target page:
                    // do nothing (no reload).
                    if (isSamePage(link.href)) {
                        event.preventDefault();
                        return;
                    }

                    event.preventDefault();

                    loadPage(link.href, true);

                }
            );

        });

    // Back / forward buttons re-load softly too.
    if (!window.__appSoftNavLinked) {

        window.__appSoftNavLinked = true;

        window.addEventListener(
            "popstate",
            function () {
                loadPage(
                    window.location.href,
                    false
                );
            }
        );

    }

})();

// ==========================================
// MASTER AJAX HELPERS (shared across pages)
// ==========================================

// Submit a form via AJAX. Server returns
// { success, message } JSON when the request
// carries the X-Requested-With header.
function masterAjaxPost(form, successCallback) {

    var url =
        form.getAttribute("action")
        || form.action;

    var data =
        new FormData(form);

    fetch(url, {
        method: "POST",
        body: data,
        credentials: "same-origin",
        headers: {
            "X-Requested-With": "XMLHttpRequest"
        }
    })
        .then(function (response) {

            return response
                .text()
                .then(function (text) {

                    var parsed = null;

                    try {
                        parsed = JSON.parse(text);
                    } catch (error) {
                        parsed = null;
                    }

                    return {
                        ok: response.ok,
                        status: response.status,
                        json: parsed
                    };

                });

        })
        .then(function (result) {

            // expected shape { success, message }
            if (
                result.json
                && typeof result.json.success === "boolean"
            ) {

                if (result.json.success) {

                    if (successCallback) {
                        successCallback(result.json);
                    }

                } else {

                    masterShowToast(
                        result.json.message
                            || "Request failed.",
                        "error"
                    );

                }

                return;

            }

            // fallback for unexpected responses
            masterShowToast(
                "Request failed (HTTP "
                    + result.status
                    + "). Please try again.",
                "error"
            );

        })
        .catch(function () {

            masterShowToast(
                "Network error. Please try again.",
                "error"
            );

        });

}


// Show a floating toast message.
function masterShowToast(message, type) {

    var container =
        document.getElementById(
            "masterToastContainer"
        );

    if (!container) {

        container =
            document.createElement("div");

        container.id = "masterToastContainer";

        document.body.appendChild(container);

    }

    var toast =
        document.createElement("div");

    toast.className =
        "master-toast "
        + (type === "error"
            ? "master-toast-error"
            : "master-toast-success");

    toast.textContent = message;

    container.appendChild(toast);

    requestAnimationFrame(function () {
        toast.classList.add("show");
    });

    setTimeout(function () {

        toast.classList.remove("show");

        setTimeout(function () {
            toast.remove();
        }, 300);

    }, 3500);

}


// ==========================================
// CRM - SIDEBAR DUE FOLLOW-UP BADGE
// Pulls the live count from the server so the
// number survives the sidebar being re-rendered
// on every soft navigation.
// ==========================================

function refreshFollowUpBadge() {

    var badge =
        document.getElementById("navFollowUpsBadge");

    if (!badge) {
        return;
    }

    fetch("/NewCrmFollowUp/DueCount", {
        method: "GET",
        credentials: "same-origin",
        headers: {
            "X-Requested-With": "XMLHttpRequest"
        }
    })
        .then(function (response) {
            return response.ok ? response.json() : null;
        })
        .then(function (data) {

            if (!data) {
                return;
            }

            var due = data.due || 0;

            if (due > 0) {

                badge.textContent = due > 99 ? "99+" : String(due);
                badge.hidden = false;

            }
            else {

                badge.textContent = "";
                badge.hidden = true;

            }

        })
        .catch(function () {
            // A missing badge is cosmetic - never break the page.
        });

}

window.refreshFollowUpBadge = refreshFollowUpBadge;


// Re-fetch the current page HTML and swap the
// table body + KPI values so no reload happens.
function masterRefreshTable(url, doneCallback) {

    fetch(url || window.location.href, {
        method: "GET",
        credentials: "same-origin",
        headers: {
            "X-Requested-With": "XMLHttpRequest"
        }
    })
        .then(function (response) {

            return response.text();

        })
        .then(function (html) {

            var doc =
                new DOMParser().parseFromString(
                    html,
                    "text/html"
                );

            var newTbody =
                doc.querySelector(
                    "#dataTable tbody"
                );

            var currentTbody =
                document.querySelector(
                    "#dataTable tbody"
                );

            if (newTbody && currentTbody) {
                currentTbody.innerHTML =
                    newTbody.innerHTML;
            }

            var newKpis =
                doc.querySelectorAll("[data-kpi]");

            for (var i = 0; i < newKpis.length; i++) {

                var key =
                    newKpis[i].getAttribute("data-kpi");

                var currentKpi =
                    document.querySelector(
                        "[data-kpi='" + key + "']"
                    );

                if (currentKpi) {
                    currentKpi.textContent =
                        newKpis[i].textContent;
                }

            }

            if (doneCallback) {
                doneCallback();
            }

        })
        .catch(function () {

            if (doneCallback) {
                doneCallback();
            }

        });

}


// Confirm then delete via AJAX. Returns false
// so the normal form post is skipped.
function masterConfirmAndSubmit(form, message) {

    if (!confirm(message)) {
        return false;
    }

    masterAjaxPost(form, function (result) {

        masterShowToast(
            result.message,
            "success"
        );

        masterRefreshTable(
            window.location.href,
            function () {

                if (
                    typeof renderTable === "function"
                ) {
                    renderTable();
                }

            }
        );

    });

    return false;

}


// Submit the Create / Edit modal form via AJAX.
function masterBindModalSubmit(formId, closeFn, modalId) {

    var form =
        document.getElementById(formId);

    if (!form) {
        return;
    }

    form.addEventListener("submit", function (event) {

        event.preventDefault();

        var closeModalFn =
            typeof closeFn === "function"
                ? closeFn
                : function () {};

        masterAjaxPost(this, function (result) {

            masterShowToast(
                result.message,
                "success"
            );

            closeModalFn();

            masterRefreshTable(
                window.location.href,
                function () {

                    if (
                        typeof renderTable === "function"
                    ) {
                        renderTable();
                    }

                }
            );

        });

});

};


// ==========================================
// MASTER TABLE SORT (shared across modules)
// Reports-style column sorting for any table:
// .sortable <th> with data-sort =
// text | num | date | time | status | priority.
// Sort column / direction / arrow state is
// kept per table id, so multiple tables on the
// same page stay independent. "no-data-row"
// placeholder rows are never sorted into place.
// ==========================================

var masterSortState = {};


function masterSortTable(th) {

    var table = th.closest("table");

    if (!table) {
        return;
    }

    var state =
        masterSortState[table.id]
        || { column: -1, direction: "asc" };

    var headers = Array.from(th.parentNode.children);

    var colIndex = headers.indexOf(th);

    var type =
        th.getAttribute("data-sort") || "text";

    if (state.column === colIndex) {
        state.direction =
            state.direction === "asc"
                ? "desc"
                : "asc";
    } else {
        state.column = colIndex;
        state.direction = "asc";
    }

    masterSortState[table.id] = state;

    var tbody = table.querySelector("tbody");

    if (!tbody) {
        return;
    }

    var rows = Array.from(
        tbody.querySelectorAll("tr")
    ).filter(function (row) {
        return !row.classList.contains("no-data-row");
    });

    rows.sort(function (a, b) {

        var aVal =
            a.querySelectorAll("td")[colIndex]
            ?.innerText.trim() || "";

        var bVal =
            b.querySelectorAll("td")[colIndex]
            ?.innerText.trim() || "";

        var aRank, bRank, aTime, bTime;

        if (type === "date") {

            aTime = Date.parse(aVal) || 0;
            bTime = Date.parse(bVal) || 0;

            return state.direction === "asc"
                ? aTime - bTime
                : bTime - aTime;

        } else if (type === "time") {

            aTime = masterTimeToMinutes(aVal);
            bTime = masterTimeToMinutes(bVal);

            return state.direction === "asc"
                ? aTime - bTime
                : bTime - aTime;

        } else if (type === "num") {

            aRank = parseFloat(aVal);
            bRank = parseFloat(bVal);

            if (isNaN(aRank) && isNaN(bRank)) {

                var cmpNum = aVal.localeCompare(bVal);

                return state.direction === "asc"
                    ? cmpNum
                    : -cmpNum;

            }

            aRank = aRank || 0;
            bRank = bRank || 0;

            return state.direction === "asc"
                ? aRank - bRank
                : bRank - aRank;

        } else if (type === "status") {

            var statusRank = {
                "Open": 0,
                "In Progress": 1,
                "Pending": 2,
                "Planned": 2,
                "On Hold": 3,
                "Closed": 4,
                "Completed": 4,
                "Cancelled": 5,
                "Active": 6,
                "Inactive": 7
            };

            aRank = statusRank[aVal] !== undefined
                ? statusRank[aVal]
                : 99;

            bRank = statusRank[bVal] !== undefined
                ? statusRank[bVal]
                : 99;

            return state.direction === "asc"
                ? aRank - bRank
                : bRank - aRank;

        } else if (type === "priority") {

            var priorityRank = {
                "Low": 0,
                "Medium": 1,
                "High": 2,
                "Urgent": 3
            };

            aRank = priorityRank[aVal] !== undefined
                ? priorityRank[aVal]
                : 99;

            bRank = priorityRank[bVal] !== undefined
                ? priorityRank[bVal]
                : 99;

            return state.direction === "asc"
                ? aRank - bRank
                : bRank - aRank;

        } else {

            var cmp = aVal.localeCompare(bVal);

            return state.direction === "asc"
                ? cmp
                : -cmp;

        }

    });

    for (var i = 0; i < rows.length; i++) {
        tbody.appendChild(rows[i]);
    }

    var allThs = table.querySelectorAll("th");

    for (var i = 0; i < allThs.length; i++) {

        allThs[i].classList.remove(
            "sort-asc", "sort-desc"
        );

    }

    if (allThs[colIndex]) {

        allThs[colIndex].classList.add(
            state.direction === "asc"
                ? "sort-asc"
                : "sort-desc"
        );

    }

}


function masterTimeToMinutes(value) {

    var match =
        value.match(
            /^(\d{1,2}):(\d{2})\s*([AP]M)$/i
        );

    if (!match) {
        return 0;
    }

    var hours = parseInt(match[1], 10) % 12;

    if (/pm/i.test(match[3])) {
        hours += 12;
    }

    return hours * 60 + parseInt(match[2], 10);

}


// ==========================================
// SEARCHABLE SELECT (combobox)
// Turns a <select data-searchable> into a
// type-to-search dropdown. The native select
// stays hidden in the DOM so form posting and
// existing .value reads still work.
// ==========================================

(function () {

    // Every enhanced select, kept so the cascading
    // filter engine can re-read a select's options
    // after it rebuilt them.
    var ssInstances = [];


    function buildSearchableSelect(select) {

        if (
            select.getAttribute(
                "data-ss-enhanced"
            ) === "1"
        ) {
            return;
        }

        select.setAttribute(
            "data-ss-enhanced",
            "1"
        );

        var wrapper =
            document.createElement("div");

        wrapper.className = "searchable-select";

        wrapper.setAttribute(
            "data-ss-for",
            select.id || select.name
        );

        var trigger =
            document.createElement("button");

        trigger.type = "button";

        trigger.className =
            "searchable-select-trigger";

        trigger.setAttribute(
            "aria-haspopup",
            "listbox"
        );

        trigger.setAttribute(
            "aria-expanded",
            "false"
        );

        var label = document.createElement("span");

        label.className = "ss-label";

        var caret =
            document.createElement("span");

        caret.className = "ss-caret";

        caret.textContent = "\u25BE";

        trigger.appendChild(label);

        trigger.appendChild(caret);

        var dropdown =
            document.createElement("div");

        dropdown.className =
            "searchable-select-dropdown";

        var search =
            document.createElement("input");

        search.type = "text";

        search.className =
            "searchable-select-search";

        search.placeholder = "Search...";

        search.autocomplete = "off";

        var options =
            document.createElement("div");

        options.className =
            "searchable-select-options";

        dropdown.appendChild(search);

        dropdown.appendChild(options);

        wrapper.appendChild(trigger);

        wrapper.appendChild(dropdown);

        select.parentNode.insertBefore(
            wrapper,
            select
        );

        select.style.display = "none";


        // ---------- LABEL SAFETY ----------
        //
        // These filters are wrapped in a <label>,
        // which makes this <button> the label's
        // first labelable descendant and therefore
        // its labeled control. A click on any
        // NON-interactive part of the dropdown is
        // therefore re-dispatched at the trigger by
        // the label's activation behaviour, which
        // immediately reopens the panel the click
        // just closed.
        //
        // Interactive content (the search box) is
        // exempt, so cancelling the default action
        // for the option rows and the "No options
        // found" row is enough. The trigger keeps
        // its native toggle.
        wrapper.addEventListener(
            "click",
            function (event) {

                var node = event.target;

                if (
                    !node
                    || !node.closest
                ) {
                    return;
                }

                if (
                    node.closest(
                        ".searchable-select-trigger"
                    ) === trigger
                ) {
                    return;
                }

                if (
                    node.closest(
                        ".searchable-select-options"
                    ) === null
                ) {
                    return;
                }

                event.preventDefault();

            },
            true
        );


        // ---------- OPTIONS LIST ----------

        var optionData = [];

        var selectedByValue = {};


        function refreshOptionsData() {

            var opts =
                select.querySelectorAll(
                    "option"
                );

            optionData = [];

            for (
                var i = 0;
                i < opts.length;
                i++
            ) {

                var value =
                    opts[i].value;

                optionData.push({
                    value: value,
                    text:
                        opts[i].textContent
                            .trim()
                });

                if (opts[i].selected) {
                    selectedByValue[value] = true;
                }

            }

        }


        function renderOptions(filterText) {

            options.innerHTML = "";

            var f =
                (filterText || "")
                    .trim()
                    .toLowerCase();

            var anyVisible = false;

            for (
                var i = 0;
                i < optionData.length;
                i++
            ) {

                if (
                    f
                    && optionData[i].text
                        .toLowerCase()
                        .indexOf(f) === -1
                ) {
                    continue;
                }

                anyVisible = true;

                var div =
                    document.createElement("div");

                div.className =
                    "searchable-select-option";

                if (
                    select.value
                    === optionData[i].value
                ) {
                    div.classList.add("selected");
                }

                div.setAttribute(
                    "data-value",
                    optionData[i].value
                );

                div.textContent =
                    optionData[i].text;

                div.addEventListener(
                    "click",
                    function (event) {
                        event.stopPropagation();

                        setValue(
                            this.getAttribute(
                                "data-value"
                            )
                        );

                        closeDropdown();
                    }
                );

                options.appendChild(div);

            }

            if (!anyVisible) {

                var empty =
                    document.createElement("div");

                empty.className =
                    "searchable-select-empty";

                empty.textContent =
                    "No options found.";

                options.appendChild(empty);

            }

        }


        function setValue(value) {

            // Skip if the value is not in the
            // native select (defensive).
            var matching = false;

            for (
                var i = 0;
                i < optionData.length;
                i++
            ) {
                if (
                    optionData[i].value
                    === value
                ) {
                    matching = true;
                    break;
                }
            }

            if (!matching) {
                return;
            }

            select.value = value;

            syncLabel();

            var changeEvent =
                document.createEvent("Event");

            changeEvent.initEvent(
                "change",
                true,
                true
            );

            select.dispatchEvent(changeEvent);

        }


        function syncLabel() {

            var text = "";

            for (
                var i = 0;
                i < optionData.length;
                i++
            ) {

                if (
                    optionData[i].value
                    === select.value
                ) {
                    text = optionData[i].text;
                    break;
                }

            }

            if (!text && optionData.length > 0) {
                text = optionData[0].text;
            }

            label.textContent = text;

            refreshOptionsData();

            renderOptions(
                search.value
            );

        }


        function openDropdown() {

            wrapper.classList.add("is-open");

            trigger.setAttribute(
                "aria-expanded",
                "true"
            );

            search.value = "";

            refreshOptionsData();

            renderOptions("");

            search.focus();

        }


        function closeDropdown() {

            wrapper.classList.remove("is-open");

            trigger.setAttribute(
                "aria-expanded",
                "false"
            );

        }


        function toggleDropdown() {

            if (
                wrapper.classList.contains(
                    "is-open"
                )
            ) {
                closeDropdown();
            } else {
                openDropdown();
            }

        }


        // ---------- EVENTS ----------

        trigger.addEventListener(
            "click",
            function (event) {
                event.stopPropagation();
                toggleDropdown();
            }
        );


        search.addEventListener(
            "input",
            function () {
                refreshOptionsData();
                renderOptions(this.value);
            }
        );


        search.addEventListener(
            "keydown",
            function (event) {

                var active =
                    options.querySelector(
                        ".searchable-select-option.highlighted"
                    );

                if (event.key === "ArrowDown") {

                    event.preventDefault();

                    var first =
                        options.querySelector(
                            ".searchable-select-option"
                        );

                    if (first) {

                        if (active) {
                            active.classList.remove(
                                "highlighted"
                            );
                        }

                        first.classList.add(
                            "highlighted"
                        );

                        first.scrollIntoView({
                            block: "nearest"
                        });

                    }

                } else if (
                    event.key === "ArrowUp"
                ) {

                    event.preventDefault();

                    if (active) {

                        var prev =
                            active.previousElementSibling;

                        active.classList.remove(
                            "highlighted"
                        );

                        if (
                            prev
                            && prev.classList.contains(
                                "searchable-select-option"
                            )
                        ) {

                            prev.classList.add(
                                "highlighted"
                            );

                            prev.scrollIntoView({
                                block: "nearest"
                            });

                        }

                    }

                } else if (event.key === "Enter") {

                    event.preventDefault();

                    var chosen =
                        options.querySelector(
                            ".searchable-select-option.highlighted"
                        )
                        || options.querySelector(
                            ".searchable-select-option"
                        );

                    if (chosen) {

                        setValue(
                            chosen.getAttribute(
                                "data-value"
                            )
                        );

                        closeDropdown();

                    }

                } else if (
                    event.key === "Escape"
                ) {

                    event.preventDefault();

                    closeDropdown();

                    trigger.focus();

                }

            }
        );


        document.addEventListener(
            "click",
            function (event) {

                if (
                    wrapper.contains(
                        event.target
                    )
                ) {
                    return;
                }

                if (
                    wrapper.classList.contains(
                        "is-open"
                    )
                ) {
                    closeDropdown();
                }

            }
        );


        // ---------- INIT ----------

        ssInstances.push({
            select: select,
            refresh: function () {
                refreshOptionsData();
                syncLabel();
            }
        });

        refreshOptionsData();

        syncLabel();

    }


    // Re-reads the native <option> list of an
    // enhanced select and repaints its trigger label
    // and its dropdown panel. Needed whenever
    // something rewrites the options in place (the
    // cascading filters do exactly that).
    function masterRefreshSearchableSelect(select) {

        if (!select) {
            return;
        }

        for (
            var i = 0;
            i < ssInstances.length;
            i++
        ) {
            if (
                ssInstances[i].select === select
            ) {
                ssInstances[i].refresh();
                return;
            }
        }

    }


    // Enhance every <select data-searchable> not
    // yet enhanced. Safe to call repeatedly (also
    // after AJAX partial re-renders).
    function masterInitSearchableSelects(root) {

        var scope =
            root || document;

        var selects =
            scope.querySelectorAll(
                "select[data-searchable]"
            );

        for (
            var i = 0;
            i < selects.length;
            i++
        ) {
            buildSearchableSelect(selects[i]);
        }

    }


    // Re-read the native select values and refresh
    // the trigger labels (used after programmatic
    // resets).
    function masterSyncSearchableSelects(root) {

        var scope =
            root || document;

        var select;

        var nativeSelects =
            scope.querySelectorAll(
                "select[data-searchable]"
            );

        for (
            var i = 0;
            i < nativeSelects.length;
            i++
        ) {

            select = nativeSelects[i];

            if (
                select.getAttribute(
                    "data-ss-enhanced"
                ) !== "1"
            ) {
                continue;
            }

            var wrapper =
                select.previousElementSibling;

            if (
                !wrapper
                || !wrapper.classList.contains(
                    "searchable-select"
                )
            ) {
                continue;
            }

            var lbl =
                wrapper.querySelector(
                    ".ss-label"
                );

            if (!lbl) {
                continue;
            }

            var text = "";

            var opts =
                select.querySelectorAll(
                    "option"
                );

            for (
                var j = 0;
                j < opts.length;
                j++
            ) {

                if (
                    opts[j].value
                    === select.value
                ) {
                    text =
                        opts[j].textContent.trim();
                    break;
                }

            }

            if (!text && opts.length > 0) {
                text = opts[0].textContent.trim();
            }

            lbl.textContent = text;

        }

    }


    // Auto-init on every page load (including soft
    // navigation, which re-dispatches this event).
    document.addEventListener(
        "DOMContentLoaded",
        function () {
            masterInitSearchableSelects(document);
        }
    );

    window.masterInitSearchableSelects =
        masterInitSearchableSelects;

    window.masterSyncSearchableSelects =
        masterSyncSearchableSelects;

    window.masterRefreshSearchableSelect =
        masterRefreshSearchableSelect;

})();


// ==========================================
// CASCADING FILTERS
//
// Makes the relationship filters (State /
// User / Client) behave as one interlinked
// set instead of three independent lists.
//
// The server renders the COMPLETE option list
// on every request and stamps each option
// with the foreign keys that decide whether it
// is still reachable:
//
//   <option value="2"
//           data-parent="1"   <- owning user id
//           data-group="4">    <- owning state id
//
// A select declares what constrains it via
// data-cascade-parent:
//
//   data-cascade-parent="userId:parent;
//                         stateId:group"
//
// meaning "this option survives only while the
// User select has an option whose value equals
// my data-parent, and the State select has an
// option whose value equals my data-group".
// An unselected ("All ...") parent imposes no
// constraint at all.
//
// Every select in a group declares the group
// through data-cascade-group, including the
// ones that constrain others but are not
// constrained themselves (the User filter).
//
// Selecting a value therefore narrows the
// sibling lists immediately, and any selection
// the new value makes impossible is cleared
// instead of being left to produce an empty,
// misleading result set. This mirrors the
// server side FilterCascade.Normalize() rules,
// so the browser and the SQL agree.
// ==========================================

(function () {

    // Reads "a:attr;b:attr" into a constraint list.
    function cascadeParseRules(select) {

        var raw =
            select.getAttribute(
                "data-cascade-parent"
            ) || "";

        var rules = [];

        var parts = raw.split(";");

        for (
            var i = 0;
            i < parts.length;
            i++
        ) {
            var piece =
                parts[i].trim();

            if (!piece) {
                continue;
            }

            var separator =
                piece.indexOf(":");

            if (separator === -1) {
                continue;
            }

            rules.push({
                key: piece
                    .slice(0, separator)
                    .trim(),
                attr: piece
                    .slice(separator + 1)
                    .trim()
            });

        }

        return rules;

    }


    // Captures the full option list once, so the
    // cascading can re-expand a narrowed select
    // without another server round-trip.
    function cascadeSnapshot(select) {

        var nodes =
            select.querySelectorAll("option");

        var items = [];

        for (
            var i = 0;
            i < nodes.length;
            i++
        ) {
            items.push({
                value: nodes[i].value,
                text:
                    nodes[i].textContent
                        .trim(),
                parent:
                    nodes[i].getAttribute(
                        "data-parent"
                    ) || "",
                group:
                    nodes[i].getAttribute(
                        "data-group"
                    ) || ""
            });
        }

        return items;

    }


    // An option survives when every parent it
    // declares is either unselected or points at
    // it.
    function cascadeIsAllowed(
        option,
        rules,
        selectedByKey) {

        // The leading "All ..." placeholder is
        // always reachable. It carries no
        // relationship key, and "no selection"
        // means "no constraint", so pruning it
        // would trap the user in a narrowed
        // filter with no way back out short of
        // the Reset button.
        if (option.value === "") {
            return true;
        }

        for (
            var i = 0;
            i < rules.length;
            i++
        ) {
            var parentValue =
                selectedByKey[rules[i].key] || "";

            if (!parentValue) {
                continue;
            }

            if (
                option[rules[i].attr]
                    !== parentValue
            ) {
                return false;
            }

        }

        return true;

    }


    // Rewrites the native <option> list in place.
    // Returns false when the current selection is
    // no longer reachable, which tells the caller
    // it was cleared and another pass is needed.
    function cascadeRender(
        entry,
        selectedByKey) {

        var select =
            entry.select;

        var previousValue =
            select.value;

        var allowed = [];

        for (
            var i = 0;
            i < entry.options.length;
            i++
        ) {
            if (
                cascadeIsAllowed(
                    entry.options[i],
                    entry.rules,
                    selectedByKey
                )
            ) {
                allowed.push(entry.options[i]);
            }
        }

        select.innerHTML = "";

        for (
            var j = 0;
            j < allowed.length;
            j++
        ) {

            var source = allowed[j];

            var node =
                document.createElement(
                    "option"
                );

            node.value = source.value;

            node.textContent = source.text;

            if (source.parent) {
                node.setAttribute(
                    "data-parent",
                    source.parent
                );
            }

            if (source.group) {
                node.setAttribute(
                    "data-group",
                    source.group
                );
            }

            select.appendChild(node);

        }

        // Restore the selection only if the
        // rewritten list still offers it,
        // otherwise fall back to the leading
        // "All ..." placeholder.
        var stillSelected = false;

        for (var k = 0; k < allowed.length; k++) {

            if (
                allowed[k].value
                    === previousValue
            ) {
                select.selectedIndex = k;
                stillSelected = true;
                break;
            }

        }

        if (!stillSelected) {
            select.selectedIndex = 0;
        }

        return stillSelected;

    }


    function cascadeApply(entries) {

        // Clearing one select can free or forbid
        // options on another, so re-run until the
        // selection set stops changing. Every pass
        // that continues clears at least one
        // selection, so this always terminates.
        for (
            var pass = 0;
            pass <= entries.length;
            pass++
        ) {

            var selectedByKey = {};

            for (
                var i = 0;
                i < entries.length;
                i++
            ) {
                var current =
                    entries[i].select.value;

                // A sibling may be referenced by its
                // name or by its id, so both resolve
                // to the same live value.
                for (
                    var k = 0;
                    k < entries[i].keys.length;
                    k++
                ) {
                    selectedByKey[
                        entries[i].keys[k]
                    ] = current;
                }

            }

            var clearedAny = false;

            for (
                var j = 0;
                j < entries.length;
                j++
            ) {
                if (
                    !cascadeRender(
                        entries[j],
                        selectedByKey
                    )
                ) {
                    clearedAny = true;
                }
            }

            if (!clearedAny) {
                break;
            }

        }


        // Repaint every combobox trigger / panel that
        // the rewrite invalidated.
        for (
            var k = 0;
            k < entries.length;
            k++
        ) {
            if (
                typeof window
                    .masterRefreshSearchableSelect
                    === "function"
            ) {
                window.masterRefreshSearchableSelect(
                    entries[k].select
                );
            }
        }

    }


    // Wires up every select that declares a
    // cascade. Safe to call repeatedly, which
    // matters because the Dashboard and the
    // Reports re-render their filter bar from an
    // AJAX partial.
    function masterInitCascadingFilters(
        root) {

        var scope = root || document;

        var selects =
            scope.querySelectorAll(
                "select[data-cascade-group]"
            );

        var groups = {};

        for (
            var i = 0;
            i < selects.length;
            i++
        ) {
            bindCascadeSelect(selects[i]);
        }

        for (var name in groups) {
            if (
                Object.prototype
                    .hasOwnProperty.call(
                        groups, name)
            ) {
                cascadeApply(groups[name]);
            }
        }

        function bindCascadeSelect(select) {

            var groupName =
                select.getAttribute(
                    "data-cascade-group"
                ) || "default";

            // The entry is cached on the element
            // so re-running this function does
            // not snapshot twice or stack a second
            // change listener.
            var entry = select.__ssCascade;

            if (!entry) {

                // Siblings may be referenced by
                // name or by id, so the same select
                // answers to both.
                var keys = [];

                if (select.name) {
                    keys.push(select.name);
                }

                if (select.id
                    && keys.indexOf(
                        select.id) === -1)
                {
                    keys.push(select.id);
                }

                entry = {
                    select: select,
                    keys: keys,
                    rules: cascadeParseRules(select),
                    options: cascadeSnapshot(select)
                };

                select.__ssCascade = entry;

                select.addEventListener(
                    "change",
                    function () {
                        cascadeApply(
                            groups[groupName]);
                    }
                );

            }

            if (!groups[groupName]) {
                groups[groupName] = [];
            }

            groups[groupName].push(entry);

        }

    }


    document.addEventListener(
        "DOMContentLoaded",
        function () {
            masterInitCascadingFilters(
                document);
        }
    );


    window.masterInitCascadingFilters =
        masterInitCascadingFilters;

})();


// Runs on first load and again on every soft navigation,
// because injectScripts re-creates site.js from the new page.
refreshFollowUpBadge();


// ==========================================
// SIDEBAR SUBMENUS (collapsible accordion)
// Har menu-group-parent group collapsible
// hai. State localStorage me save hota hai;
// active page par submenu automatically open
// rehta hai.
// ==========================================

(function () {

    var currentPath =
        window.location.pathname;

    var parentGroups =
        document.querySelectorAll(
            ".menu-group-parent");

    parentGroups.forEach(function (group) {

        var toggle =
            group.querySelector(
                ".menu-group-toggle");

        if (!toggle) {
            return;
        }

        var STORAGE_KEY =
            "submenuOpen:" + group.id;

        // Active submenu link? (page match)
        var hasActiveChild = false;

        var links =
            group.querySelectorAll(
                ".menu-item-sub[href]");

        for (var i = 0; i < links.length; i++) {

            var linkPath =
                new URL(links[i].href).pathname;

            if (linkPath === currentPath) {

                hasActiveChild = true;

            }

        }

        // Open state: active child hamesha open,
        // warna localStorage ki saved state.
        var open = hasActiveChild
            || localStorage.getItem(STORAGE_KEY) === "1";

        function applyOpen() {

            group.classList.toggle("open", open);

            toggle.setAttribute(
                "aria-expanded",
                open ? "true" : "false");

        }

        applyOpen();

        toggle.addEventListener("click", function () {

            // Collapsed rail (icon-only sidebar): pehle sidebar
            // kholo, phir group toggle karo. Warna click se kuch
            // nahi dikhta, kyunke submenu collapsed state me
            // hidden rehta hai.
            if (document.body.classList.contains(
                    "sidebar-collapsed")) {

                document.body.classList.remove(
                    "sidebar-collapsed"
                );

                localStorage.setItem(
                    "sidebarCollapsed",
                    "0"
                );

                var sidebarToggle =
                    document.getElementById(
                        "sidebarToggle"
                    );

                if (sidebarToggle) {
                    sidebarToggle.setAttribute(
                        "aria-expanded",
                        "true"
                    );
                }

            }

            open = !open;

            localStorage.setItem(
                STORAGE_KEY,
                open ? "1" : "0");

            applyOpen();

        });

    });

})();


// ==========================================
// SIDEBAR LIVE MEETING BADGE (Time Log)
// Agar current user ke paas LIVE timer hai toh
// Time Log menu par red dot dikhata hai.
// ==========================================

function refreshTimeLogBadge() {

    var badge =
        document.getElementById("navTimeLogBadge");

    if (!badge) {
        return;
    }

    fetch("/NewCrmTimeLog/LiveStatus", {
        method: "GET",
        credentials: "same-origin",
        headers: {
            "X-Requested-With": "XMLHttpRequest"
        }
    })
        .then(function (response) {
            return response.ok ? response.json() : null;
        })
        .then(function (data) {

            if (!data) {
                return;
            }

            badge.hidden = !data.live;

        })
        .catch(function () {
            // Cosmetic only - never break the page.
        });

}

window.refreshTimeLogBadge = refreshTimeLogBadge;

refreshTimeLogBadge();


// ==========================================
// SIDEBAR SUPPORT BADGE (Daily Support notifications)
// Unread review notifications ka count dikhata hai
// (admin ko "review aaya", user ko "approve/reject").
// ==========================================

function refreshSupportBadge() {

    var badge =
        document.getElementById("navSupportBadge");

    if (!badge) {
        return;
    }

    fetch("/DailySupport/NotifCount", {
        method: "GET",
        credentials: "same-origin",
        headers: {
            "X-Requested-With": "XMLHttpRequest"
        }
    })
        .then(function (response) {
            return response.ok ? response.json() : null;
        })
        .then(function (data) {

            if (!data) {
                return;
            }

            var count = data.count || 0;

            badge.hidden = count <= 0;

            badge.textContent = count > 99 ? "99+" : String(count);

        })
        .catch(function () {
            // Cosmetic only - never break the page.
        });

}

window.refreshSupportBadge = refreshSupportBadge;

refreshSupportBadge();


// ==========================================
// NOTIFICATIONS
// Index page  -> compact rows list
// Detail page -> ek slim alert strip (latest)
// Sidebar     -> sirf count badge
// ==========================================

// Ek notification ki compact row (Index page ke liye).
// Row click -> notification hat jati hai + ticket ka review page khulta hai.
function notifRowHtml(n) {

    var kind = (n.kind || "").toLowerCase();

    var icon = kind === "approved" ? "\u2705"
        : kind === "rejected" ? "\u274C"
        : kind === "assigned" ? "\uD83D\uDCCC"
        : kind === "unassigned" ? "\u21A9\uFE0F"
        : "\uD83D\uDD0D";

    var label = kind === "approved" ? "approved"
        : kind === "rejected" ? "rejected"
        : kind === "assigned" ? "assigned"
        : kind === "unassigned" ? "unassigned"
        : "pending";

    var stars = "";

    if (n.rating) {

        var s = "";

        for (var i = 1; i <= 5; i++) {
            s += (i <= n.rating) ? "\u2605" : "\u2606";
        }

        stars = '<span class="tkt-notif-row-stars">' + s + '</span>';
    }

    var ticket = n.ticketNo ? n.ticketNo : "Ticket";

    var msg = n.body
        ? '<span class="tkt-notif-row-msg">'
            + escapeHtml(n.body) + '</span>'
        : '';

    // Chhota "open" button -> ticket ka Detail page.
    var openBtn = n.ticketId
        ? '<a class="tkt-notif-row-open"'
            + ' href="/DailySupport/Detail/' + n.ticketId + '"'
            + ' title="Open ticket">\uD83D\uDC41</a>'
        : '';

    return '<div class="tkt-notif-row kind-' + kind
        + (n.isRead ? ' read' : '')
        + '" data-id="' + (n.id || '')
        + '" data-ticketid="' + (n.ticketId || '')
        + '" data-read="' + (n.isRead ? "1" : "0")
        + '" title="Click to open this ticket">'
        + '<span class="tkt-notif-row-icon">' + icon + '</span>'
        + '<span class="tkt-notif-row-ticket">' + escapeHtml(ticket) + '</span>'
        + '<span class="tkt-notif-row-label ' + label + '">'
        + label + '</span>'
        + msg
        + stars
        + '<span class="tkt-notif-row-time">'
        + (n.createdAt || '') + '</span>'
        + openBtn
        + '</div>';
}

// Ek slim alert strip (Detail page ke liye, latest notification).
function notifStripHtml(n) {

    if (!n) {
        return '';
    }

    var kind = (n.kind || "").toLowerCase();

    var icon = kind === "approved" ? "\u2705"
        : kind === "rejected" ? "\u274C"
        : kind === "assigned" ? "\uD83D\uDCCC"
        : kind === "unassigned" ? "\u21A9\uFE0F"
        : "\uD83D\uDD0D";

    var label = kind === "approved" ? "Review accepted"
        : kind === "rejected" ? "Review rejected"
        : kind === "assigned" ? "Assigned to you"
        : kind === "unassigned" ? "Unassigned from you"
        : "Review pending with admin";

    var stars = "";

    if (n.rating) {

        var s = "";

        for (var i = 1; i <= 5; i++) {
            s += (i <= n.rating) ? "\u2605" : "\u2606";
        }

        stars = '<span class="tkt-strip-stars">' + s + '</span>';
    }

    var ticket = n.ticketNo ? n.ticketNo : "Ticket";

    return '<div class="tkt-strip kind-' + kind + '">'
        + '<span class="tkt-strip-icon">' + icon + '</span>'
        + '<span class="tkt-strip-ticket">' + ticket + '</span>'
        + '<span class="tkt-strip-label">' + label + '</span>'
        + stars
        + '<span class="tkt-strip-time">'
        + (n.createdAt || '') + '</span>'
        + '</div>';
}

// Chhota HTML escape (body me user text aata hai).
function escapeHtml(text) {

    var div = document.createElement("div");

    div.textContent = text == null ? "" : String(text);

    return div.innerHTML;
}

// Ek container me notification rows bharo (Index page).
function renderNotifRows(containerId, items, emptyText) {

    var box = document.getElementById(containerId);

    if (!box) {
        return;
    }

    if (!items || items.length === 0) {
        box.innerHTML = '<div class="tkt-notif-empty">'
            + (emptyText || "No notifications yet.") + '</div>';
        return;
    }

    var html = "";

    for (var i = 0; i < items.length; i++) {
        html += notifRowHtml(items[i]);
    }

    box.innerHTML = html;
}

// Header badge me unread count set karo.
function setNotifCount(count) {

    var badge = document.getElementById("notifCountBadge");

    if (!badge) {
        return;
    }

    var n = count || 0;

    badge.textContent = n > 99 ? "99+" : String(n);

    badge.hidden = n <= 0;
}

// Badge count me +/- karo (optimistic UI ke liye).
function bumpNotifCount(delta) {

    var badge = document.getElementById("notifCountBadge");

    if (!badge) {
        return;
    }

    var cur = parseInt(badge.textContent, 10);

    if (isNaN(cur)) {
        cur = 0;
    }

    var next = Math.max(0, cur + delta);

    badge.textContent = next > 99 ? "99+" : String(next);

    badge.hidden = next <= 0;
}

// Detail page ka strip (latest notification).
function renderNotifStrip(containerId, items) {

    var box = document.getElementById(containerId);

    if (!box) {
        return;
    }

    if (!items || items.length === 0) {
        box.style.display = "none";
        return;
    }

    box.innerHTML = notifStripHtml(items[0]);
}

// Index page: rows (sirf unread).
function loadPageNotifs(containerId, take) {

    fetch("/DailySupport/NotifList?mode=unread&take=50", {
        method: "GET",
        credentials: "same-origin",
        headers: { "X-Requested-With": "XMLHttpRequest" }
    })
        .then(function (r) { return r.ok ? r.json() : null; })
        .then(function (data) {

            if (!data) {
                return;
            }

            setNotifCount(data.unreadCount);

            var tabCount =
                document.getElementById("notifTabUnreadCount");

            if (tabCount) {
                tabCount.textContent = data.unreadCount || 0;
            }

            renderNotifRows(
                containerId,
                (data.items || []).slice(0, take || 50),
                "Koi unread notification nahi hai.");

        })
        .catch(function () { });
}

window.loadPageNotifs = loadPageNotifs;

// Index page: HISTORY tab (padhi hui notifications).
function loadNotifHistory(containerId, take) {

    fetch("/DailySupport/NotifList?mode=read&take=50", {
        method: "GET",
        credentials: "same-origin",
        headers: { "X-Requested-With": "XMLHttpRequest" }
    })
        .then(function (r) { return r.ok ? r.json() : null; })
        .then(function (data) {

            if (!data) {
                return;
            }

            renderNotifRows(
                containerId,
                (data.items || []).slice(0, take || 50),
                "Abhi tak koi purani notification nahi hai.");

        })
        .catch(function () { });
}

window.loadNotifHistory = loadNotifHistory;

// Unread / History tab switch.
function switchNotifTab(tab) {

    var isHistory = tab === "history";

    var tabUnread = document.getElementById("notifTabUnread");

    var tabHistory = document.getElementById("notifTabHistory");

    if (tabUnread) {
        tabUnread.classList.toggle("active", !isHistory);
    }

    if (tabHistory) {
        tabHistory.classList.toggle("active", isHistory);
    }

    var gridUnread = document.getElementById("pageNotifGrid");

    var gridHistory = document.getElementById("pageNotifHistory");

    if (gridUnread) {
        gridUnread.hidden = isHistory;
    }

    if (gridHistory) {
        gridHistory.hidden = !isHistory;
    }

    // History tab pehli baar khulte hi load karo.
    if (isHistory && gridHistory && !gridHistory.dataset.loaded) {

        gridHistory.dataset.loaded = "1";

        loadNotifHistory("pageNotifHistory", 50);

    }
}

window.switchNotifTab = switchNotifTab;

// Detail page: is ticket ka latest strip.
function loadDetailNotifs() {

    var wrap = document.getElementById("detailNotifList");

    if (!wrap) {
        return;
    }

    var ticketId = wrap.getAttribute("data-ticket");

    if (!ticketId) {
        return;
    }

    fetch("/DailySupport/NotifList?ticketId=" + ticketId, {
        method: "GET",
        credentials: "same-origin",
        headers: { "X-Requested-With": "XMLHttpRequest" }
    })
        .then(function (r) { return r.ok ? r.json() : null; })
        .then(function (data) {

            if (!data) {
                return;
            }

            renderNotifStrip("detailNotifList", data.items || []);

        })
        .catch(function () { });
}

window.loadDetailNotifs = loadDetailNotifs;

// Sabko read mark karo.
function markAllNotifsRead(e) {

    if (e) {
        e.preventDefault();
        e.stopPropagation();
    }

    fetch("/DailySupport/MarkNotifsRead", {
        method: "POST",
        credentials: "same-origin",
        headers: {
            "X-Requested-With": "XMLHttpRequest",
            "RequestVerificationToken": getNotifToken()
        }
    })
        .then(function (r) { return r.ok ? r.json() : null; })
        .then(function () {

            refreshSupportBadge();

            var grid = document.getElementById("pageNotifGrid");

            if (grid) {
                grid.innerHTML =
                    '<div class="tkt-notif-empty">'
                    + 'Koi unread notification nahi hai.'
                    + '</div>';
            }

            setNotifCount(0);

            var tabCount =
                document.getElementById("notifTabUnreadCount");

            if (tabCount) {
                tabCount.textContent = "0";
            }

            // Ab sab read ho gaye -> history dobara load hogi.
            var gridHistory = document.getElementById("pageNotifHistory");

            if (gridHistory) {

                gridHistory.dataset.loaded = "";

                var historyTab =
                    document.getElementById("notifTabHistory");

                if (historyTab
                    && historyTab.classList.contains("active")) {
                    loadNotifHistory("pageNotifHistory", 50);
                }
            }

            var cards = document.querySelectorAll(
                ".tkt-notif-row, .tkt-strip");

            for (var i = 0; i < cards.length; i++) {
                cards[i].classList.add("read");
            }

        })
        .catch(function () { });
}

window.markAllNotifsRead = markAllNotifsRead;

// Ek notification read mark karo (sirf usi user ki).
function markNotifRead(notifId) {

    if (!notifId) {
        return Promise.resolve(null);
    }

    return fetch(
        "/DailySupport/MarkNotifRead?id=" + encodeURIComponent(notifId),
        {
            method: "POST",
            credentials: "same-origin",
            headers: {
                "X-Requested-With": "XMLHttpRequest",
                "RequestVerificationToken": getNotifToken()
            }
        })
        .then(function (r) { return r.ok ? r.json() : null; })
        .catch(function () { return null; });
}

window.markNotifRead = markNotifRead;

// Notification ke ticket ka review page kholo.
// focus=review -> Detail page par REVIEW section khud expand +
// highlight + scroll ho jata hai.
function openTicketFromNotif(ticketId) {

    if (!ticketId) {
        return;
    }

    window.location.href =
        "/DailySupport/Detail/" + encodeURIComponent(ticketId)
        + "?focus=review";
}

window.openTicketFromNotif = openTicketFromNotif;

// Notification card ko expand / collapse karo.
function toggleNotifCard(e) {

    if (e) {
        e.preventDefault();
        e.stopPropagation();
    }

    var card = document.getElementById("tktNotifCard");

    if (!card) {
        return;
    }

    var collapsed = card.classList.toggle("collapsed");

    var open = !collapsed;

    var toggle = document.getElementById("tktNotifToggle");

    if (toggle) {
        toggle.textContent = collapsed ? "+" : "\u2212";
        toggle.setAttribute("aria-expanded", open ? "true" : "false");
    }

    var head = document.getElementById("tktNotifHead");

    if (head) {
        head.setAttribute("aria-expanded", open ? "true" : "false");
    }

    try {
        localStorage.setItem("dsNotifCardOpen", open ? "1" : "0");
    } catch (err) {
        // localStorage blocked ho to bhi chalega.
    }
}

window.toggleNotifCard = toggleNotifCard;

// Anti-forgery token (page me kahin bhi ho).
function getNotifToken() {

    var el = document.querySelector(
        'input[name="__RequestVerificationToken"]');

    return el ? el.value : "";
}

// ---- Page ke notification rows load (Index + Detail) ----

(function () {

    // Collapsed / expanded state yaad rakho.
    function restoreCardState() {

        var card = document.getElementById("tktNotifCard");

        if (!card) {
            return;
        }

        var saved = null;

        try {
            saved = localStorage.getItem("dsNotifCardOpen");
        } catch (err) {
            saved = null;
        }

        if (saved !== "1") {
            return;
        }

        card.classList.remove("collapsed");

        var toggle = document.getElementById("tktNotifToggle");

        if (toggle) {
            toggle.textContent = "\u2212";
            toggle.setAttribute("aria-expanded", "true");
        }

        var head = document.getElementById("tktNotifHead");

        if (head) {
            head.setAttribute("aria-expanded", "true");
        }
    }

    // Card ka header poori row clickable hai (buttons ke alawa).
    function bindCardHeader() {

        var head = document.getElementById("tktNotifHead");

        if (!head) {
            return;
        }

        head.addEventListener("click", function (e) {

            if (e.target.closest("button, a")) {
                return;
            }

            toggleNotifCard(e);

        });

        head.addEventListener("keydown", function (e) {

            if (e.key === "Enter" || e.key === " ") {
                e.preventDefault();
                toggleNotifCard(e);
            }

        });
    }

    // Notification row click -> hat jaye + ticket ka review page khule.
    // (Unread grid aur History grid dono me chalta hai)
    function bindRowClick() {

        var card = document.getElementById("tktNotifCard");

        if (!card) {
            return;
        }

        card.addEventListener("click", function (e) {

            // "open" button -> normal Detail page navigation.
            if (e.target.closest(".tkt-notif-row-open")) {
                return;
            }

            var row = e.target.closest(".tkt-notif-row");

            if (!row || !card.contains(row)) {
                return;
            }

            var id = row.getAttribute("data-id");

            var ticketId = row.getAttribute("data-ticketid");

            var isRead = row.getAttribute("data-read") === "1";

            // ---- HISTORY row: already read, seedha ticket khule ----

            if (isRead) {

                openTicketFromNotif(ticketId);

                return;
            }

            // ---- UNREAD row: turant hatao + mark read + khule ----

            var grid = row.parentNode;

            if (row.parentNode) {
                row.parentNode.removeChild(row);
            }

            // List khaali ho gayi -> empty state dikhao.
            if (grid && grid.children.length === 0) {
                grid.innerHTML =
                    '<div class="tkt-notif-empty">'
                    + 'Koi unread notification nahi hai.'
                    + '</div>';
            }

            bumpNotifCount(-1);

            markNotifRead(id).then(function () {

                refreshSupportBadge();

                openTicketFromNotif(ticketId);

            });

        });
    }

    var booted = false;

    function boot() {

        // DOMContentLoaded + readyState dono se call ho sakta hai;
        // listeners double bind na ho -> ek hi baar chalao.
        if (booted) {
            return;
        }

        booted = true;

        restoreCardState();

        bindCardHeader();

        bindRowClick();

        if (document.getElementById("pageNotifGrid")) {
            loadPageNotifs("pageNotifGrid", 50);
        }

        if (document.getElementById("detailNotifList")) {
            loadDetailNotifs();
        }
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", boot);
    } else {
        boot();
    }

    document.addEventListener("DOMContentLoaded", boot);

})();
