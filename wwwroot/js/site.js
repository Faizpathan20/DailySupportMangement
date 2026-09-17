// ==========================================
// SIDEBAR COLLAPSE + ACTIVE MENU
// ==========================================

(function () {

    var sidebar =
        document.getElementById("appSidebar");

    var toggle =
        document.getElementById("sidebarToggle");

    if (!sidebar || !toggle) {
        return;
    }

    var STORAGE_KEY = "sidebarCollapsed";


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

    function injectScripts(doc) {

        var scripts =
            doc.querySelectorAll("script");

        for (var i = 0; i < scripts.length; i++) {

            var old = scripts[i];

            var fresh =
                document.createElement("script");

            if (old.src) {

                fresh.src = old.src;
                fresh.defer = old.defer;
                fresh.async = false;

            } else if (old.textContent) {

                fresh.textContent =
                    old.textContent;

            } else {

                continue;

            }

            document.body.appendChild(fresh);

        }

    }

    function swapPage(url, html, push) {

        var doc =
            new DOMParser().parseFromString(
                html,
                "text/html"
            );

        document.title = doc.title;

        document.body.innerHTML =
            doc.body.innerHTML;

        if (push) {
            history.pushState({}, "", url);
        }

        injectScripts(doc);

        // Let the new page's script blocks that wait
        // for the load event run against the new DOM.
        document.dispatchEvent(
            new Event("DOMContentLoaded")
        );

        window.scrollTo(0, 0);
        hideAppLoader();
        navInProgress = false;

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

}


// ==========================================
// SEARCHABLE SELECT (combobox)
// Turns a <select data-searchable> into a
// type-to-search dropdown. The native select
// stays hidden in the DOM so form posting and
// existing .value reads still work.
// ==========================================

(function () {

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

        refreshOptionsData();

        syncLabel();

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

})();