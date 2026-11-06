/* =========================================================
   FILE MANAGER
   - instant search + type filters (current folder)
   - grid / list toggle
   - viewer modal: video, PDF, Excel, images, audio, text
   - background "search all folders" with progress polling
   ========================================================= */

(function () {
    'use strict';

    var app = document.getElementById('fmApp');

    if (!app) {
        return;
    }

    var currentPath = app.dataset.path || '/';

    var searchInput = document.getElementById('fmSearch');
    var searchClear = document.getElementById('fmSearchClear');
    var chips = Array.prototype.slice.call(
        document.querySelectorAll('.fm-chip'));

    var chipsScroller = document.getElementById('fmChips');
    var chipsShell = document.querySelector('.fm-chips-shell');
    var chipsPrev = document.getElementById('fmChipsPrev');
    var chipsNext = document.getElementById('fmChipsNext');

    var folderSection = document.getElementById('fmFolderSection');
    var fileSection = document.getElementById('fmFileSection');
    var foldersGrid = document.getElementById('fmFolders');
    var filesGrid = document.getElementById('fmFiles');
    var fileCount = document.getElementById('fmFileCount');
    var folderCount = document.getElementById('fmFolderCount');
    var filterClearButtons = Array.prototype.slice.call(
        document.querySelectorAll('[data-filter-clear]'));

    var resultsSection = document.getElementById('fmResults');
    var resultsLocalGrid = document.getElementById('fmResultsLocalGrid');
    var resultsOtherGrid = document.getElementById('fmResultsOtherGrid');
    var resultsLocalGroup = document.getElementById('fmResultsLocalGroup');
    var resultsOtherGroup = document.getElementById('fmResultsOtherGroup');
    var resultsSummary = document.getElementById('fmResultsSummary');
    var resultsCount = document.getElementById('fmResultsCount');
    var resultsEmpty = document.getElementById('fmResultsEmpty');
    var resultsClose = document.getElementById('fmResultsClose');

    var overlay = document.getElementById('fmOverlay');
    var toastEl = document.getElementById('fmToast');

    var scanBar = document.getElementById('fmScanBar');
    var scanText = document.getElementById('fmScanText');
    var scanHide = document.getElementById('fmScanHide');

    var indexPill = document.getElementById('fmIndexPill');
    var indexText = document.getElementById('fmIndexText');

    var sortSelect = document.getElementById('fmSort');
    var viewToggle = document.getElementById('fmViewToggle');

    var crumbs = document.querySelector('.fm-crumbs');
    var toTop = document.getElementById('fmToTop');
    var resultsReset = document.getElementById('fmResultsReset');

    var viewerEl = document.getElementById('fmViewer');
    var viewerBody = document.getElementById('fmViewerBody');
    var viewerTitle = document.getElementById('fmViewerTitle');
    var viewerSub = document.getElementById('fmViewerSub');
    var viewerTile = document.getElementById('fmViewerTile');
    var viewerOpen = document.getElementById('fmViewerOpen');
    var viewerDownload = document.getElementById('fmViewerDownload');

    var viewerModal = window.bootstrap && viewerEl
        ? new window.bootstrap.Modal(viewerEl)
        : null;

    var term = '';
    var kind = 'all';
    var lastDeepQuery = '';
    var pollTimer = null;
    var globalTimer = null;
    var localTimer = null;
    var deepSeq = 0;

    var viewMode = 'grid';

    try {
        if (window.localStorage.getItem('fmView') === 'list') {
            viewMode = 'list';
        }
    } catch (ignored) {
        viewMode = 'grid';
    }

    var noMatch = document.createElement('div');
    noMatch.className = 'fm-empty';
    noMatch.hidden = true;
    noMatch.innerHTML =
        '<h3>No matching files</h3>' +
        '<p>Search ya filter clear karke dobara dekhiye.</p>';

    if (fileSection) {
        var pager = fileSection.querySelector('.fm-pager');
        fileSection.insertBefore(noMatch, pager || null);
    }

    // -----------------------------------------------------
    // SMALL HELPERS
    // -----------------------------------------------------

    function toast(message) {
        if (!toastEl) {
            return;
        }

        toastEl.textContent = message;
        toastEl.hidden = false;

        window.clearTimeout(toast._timer);

        toast._timer = window.setTimeout(function () {
            toastEl.hidden = true;
        }, 2600);
    }

    function showOverlay(show) {
        if (overlay) {
            overlay.hidden = !show;
        }
    }

    function svgIcon(id, className) {
        var namespace = 'http://www.w3.org/2000/svg';

        var svg = document.createElementNS(namespace, 'svg');
        svg.setAttribute('class', className || 'fm-svg');
        svg.setAttribute('aria-hidden', 'true');

        var use = document.createElementNS(namespace, 'use');
        use.setAttribute('href', id);

        svg.appendChild(use);

        return svg;
    }

    function element(tag, className, text) {
        var node = document.createElement(tag);

        if (className) {
            node.className = className;
        }

        if (text !== undefined && text !== null) {
            node.textContent = text;
        }

        return node;
    }

    function closeMenus(except) {
        Array.prototype.forEach.call(
            document.querySelectorAll('.fm-menu'),
            function (menu) {
                if (menu !== except) {
                    menu.hidden = true;
                }
            });
    }

    function copyLink(url) {
        function fallback() {
            var area = document.createElement('textarea');
            area.value = url;
            area.setAttribute('readonly', 'readonly');
            area.style.position = 'fixed';
            area.style.opacity = '0';

            document.body.appendChild(area);
            area.select();

            try {
                document.execCommand('copy');
                toast('Link copied');
            } catch (ignored) {
                toast('Copy nahi ho paaya.');
            }

            area.remove();
        }

        if (navigator.clipboard && window.isSecureContext) {
            navigator.clipboard.writeText(url)
                .then(function () { toast('Link copied'); })
                .catch(fallback);
        } else {
            fallback();
        }
    }

    // -----------------------------------------------------
    // VIEW MODE
    // -----------------------------------------------------

    function applyViewMode() {
        app.classList.toggle('fm-list-view', viewMode === 'list');

        if (!viewToggle) {
            return;
        }

        var use = viewToggle.querySelector('use');

        if (use) {
            use.setAttribute(
                'href',
                viewMode === 'list' ? '#fm-i-grid' : '#fm-i-list');
        }

        viewToggle.title = viewMode === 'list'
            ? 'Switch to grid view'
            : 'Switch to list view';
    }

    applyViewMode();

    if (viewToggle) {
        viewToggle.addEventListener('click', function () {
            viewMode = viewMode === 'list' ? 'grid' : 'list';

            try {
                window.localStorage.setItem('fmView', viewMode);
            } catch (ignored) {
                // Private mode: the toggle still works for
                // this page view.
            }

            applyViewMode();
        });
    }

    // -----------------------------------------------------
    // SEARCH / FILTER (CURRENT FOLDER)
    // -----------------------------------------------------

    function applyFilter() {
        if (!filesGrid) {
            return;
        }

        var visibleFiles = 0;
        var visibleFolders = 0;

        Array.prototype.forEach.call(
            filesGrid.querySelectorAll('.fm-card'),
            function (card) {
                var name = (card.dataset.name || '').toLowerCase();

                var matchesText = !term || name.indexOf(term) >= 0;
                var matchesKind = kind === 'all' || card.dataset.kind === kind;
                var visible = matchesText && matchesKind;

                card.hidden = !visible;

                if (visible) {
                    visibleFiles++;
                }
            });

        if (foldersGrid) {
            Array.prototype.forEach.call(
                foldersGrid.querySelectorAll('.fm-card'),
                function (card) {
                    var name = (card.dataset.name || '').toLowerCase();

                    var matchesText = !term || name.indexOf(term) >= 0;
                    var matchesKind = kind === 'all' || kind === 'folder';
                    var visible = matchesText && matchesKind;

                    card.hidden = !visible;

                    if (visible) {
                        visibleFolders++;
                    }
                });
        }

        if (folderSection) {
            folderSection.hidden =
                (resultsSection && !resultsSection.hidden) ||
                visibleFolders === 0;
        }

        if (fileSection) {
            fileSection.hidden = kind === 'folder' ||
                (resultsSection && !resultsSection.hidden);
        }

        var filtering = term.length > 0 || kind !== 'all';
        var serverEmpty = !filesGrid.querySelector('.fm-card');
        var totalFiles = fileCount
            ? Number(fileCount.dataset.total || 0)
            : 0;

        app.classList.toggle('fm-filtering', filtering);

        noMatch.hidden = !(filtering &&
            visibleFiles === 0 &&
            !serverEmpty &&
            kind !== 'folder');

        if (folderCount) {
            var totalFolders = Number(folderCount.dataset.total || 0);
            var showFolderFiltered = filtering &&
                (kind === 'all' || kind === 'folder');

            folderCount.textContent = showFolderFiltered
                ? visibleFolders + ' of ' + totalFolders
                : totalFolders;

            folderCount.classList.toggle('is-active', showFolderFiltered);
        }

        if (fileCount) {
            var showFiltered = filtering && kind !== 'folder';

            fileCount.textContent = showFiltered
                ? visibleFiles + ' of ' + totalFiles
                : totalFiles;

            fileCount.classList.toggle('is-active', showFiltered);
        }

        filterClearButtons.forEach(function (button) {
            button.hidden = !filtering;
        });

        updateChipsScrollUI();
    }

    function clearDeepResults() {
        deepSeq++;

        if (resultsSection) {
            resultsSection.hidden = true;
        }

        app.classList.remove('fm-results-open');
        lastDeepQuery = '';

        if (folderSection) {
            folderSection.hidden = false;
        }

        if (fileSection) {
            fileSection.hidden = false;
        }

        applyFilter();
    }

    if (searchInput) {
        searchInput.addEventListener('input', function () {
            window.clearTimeout(localTimer);
            window.clearTimeout(globalTimer);

            var value = searchInput.value.trim();

            localTimer = window.setTimeout(function () {
                term = value.toLowerCase();

                if (searchClear) {
                    searchClear.hidden = term.length === 0;
                }

                applyFilter();
            }, 90);

            globalTimer = window.setTimeout(function () {
                if (value.length < 2) {
                    if (resultsSection && !resultsSection.hidden) {
                        clearDeepResults();
                    }

                    return;
                }

                runDeepSearch(value);
            }, 650);
        });

        searchInput.addEventListener('keydown', function (event) {
            if (event.key === 'Enter' && searchInput.value.trim()) {
                event.preventDefault();
                window.clearTimeout(globalTimer);
                runDeepSearch(searchInput.value.trim());
            }

            if (event.key === 'Escape') {
                window.clearTimeout(localTimer);
                window.clearTimeout(globalTimer);

                searchInput.value = '';
                term = '';

                if (searchClear) {
                    searchClear.hidden = true;
                }

                clearDeepResults();
            }
        });
    }

    if (searchClear) {
        searchClear.addEventListener('click', function () {
            window.clearTimeout(localTimer);
            window.clearTimeout(globalTimer);

            if (searchInput) {
                searchInput.value = '';
                searchInput.focus();
            }

            term = '';
            searchClear.hidden = true;
            clearDeepResults();
        });
    }

    chips.forEach(function (chip) {
        chip.addEventListener('click', function () {
            kind = chip.dataset.kind || 'all';

            chips.forEach(function (other) {
                other.classList.toggle('is-active', other === chip);
            });

            applyFilter();

            var query = searchInput ? searchInput.value.trim() : '';

            if (query.length >= 2) {
                window.clearTimeout(globalTimer);
                runDeepSearch(query);
            } else if (resultsSection &&
                !resultsSection.hidden &&
                lastDeepQuery) {
                runDeepSearch(lastDeepQuery);
            }
        });
    });

    filterClearButtons.forEach(function (button) {
        button.addEventListener('click', function () {
            window.clearTimeout(localTimer);
            window.clearTimeout(globalTimer);

            term = '';
            kind = 'all';

            if (searchInput) {
                searchInput.value = '';
            }

            if (searchClear) {
                searchClear.hidden = true;
            }

            chips.forEach(function (chip) {
                chip.classList.toggle(
                    'is-active',
                    (chip.dataset.kind || 'all') === 'all');
            });

            clearDeepResults();
        });
    });

    if (sortSelect) {
        sortSelect.addEventListener('change', function () {
            var parts = sortSelect.value.split(':');

            var url = new URL(window.location.href);
            url.searchParams.set('path', currentPath);
            url.searchParams.set('sort', parts[0] || 'name');
            url.searchParams.set('dir', parts[1] || 'asc');
            url.searchParams.delete('page');

            showOverlay(true);
            window.location.href = url.toString();
        });
    }

    // -----------------------------------------------------
    // DEEP SEARCH + BACKGROUND INDEX
    // -----------------------------------------------------

    function updateIndexPill(status) {
        if (!status || !indexText) {
            return;
        }

        var dot = indexPill ? indexPill.querySelector('.fm-dot') : null;

        if (status.complete) {
            indexText.textContent = 'Search ready';

            if (indexPill) {
                indexPill.title =
                    Number(status.files || 0).toLocaleString() +
                    ' files indexed. Click to rebuild.';
            }

            if (dot) {
                dot.classList.add('is-ok');
            }
        } else if (status.running) {
            indexText.textContent = 'Indexing…';

            if (indexPill) {
                indexPill.title =
                    Number(status.files || 0).toLocaleString() +
                    ' files indexed so far.';
            }

            if (dot) {
                dot.classList.remove('is-ok');
            }
        } else {
            indexText.textContent = 'Search index not built';

            if (indexPill) {
                indexPill.title = 'Search index. Click to rebuild it.';
            }

            if (dot) {
                dot.classList.remove('is-ok');
            }
        }
    }

    function showScanBar(text) {
        if (!scanBar) {
            return;
        }

        if (scanText) {
            scanText.textContent = text;
        }

        scanBar.hidden = false;
    }

    function hideScanBar() {
        if (scanBar) {
            scanBar.hidden = true;
        }
    }

    if (scanHide) {
        scanHide.addEventListener('click', hideScanBar);
    }

    if (indexPill) {
        indexPill.addEventListener('click', function () {
            if (!window.confirm(
                'Rebuild the search index? It can take a few minutes.')) {
                return;
            }

            showScanBar('Starting index rebuild…');

            fetch('/FileManager/Scan', {
                headers: { 'Accept': 'application/json' }
            })
                .then(function (response) { return response.json(); })
                .then(function (status) {
                    updateScanText(status);
                    pollScan('');
                })
                .catch(function () {
                    hideScanBar();
                    toast('Index rebuild start nahi hua.');
                });
        });
    }

    function updateScanText(status) {
        if (!status || !scanText) {
            return;
        }

        scanText.textContent =
            'Indexing FTP tree… ' +
            Number(status.folders || 0).toLocaleString() + ' folders · ' +
            Number(status.files || 0).toLocaleString() + ' files · ' +
            (status.seconds || 0) + 's';
    }

    function pollScan(query) {
        if (pollTimer) {
            window.clearInterval(pollTimer);
        }

        pollTimer = window.setInterval(function () {
            fetch('/FileManager/ScanStatus', {
                headers: { 'Accept': 'application/json' }
            })
                .then(function (response) { return response.json(); })
                .then(function (status) {
                    updateScanText(status);
                    updateIndexPill(status);

                    if (status.running) {
                        return;
                    }

                    window.clearInterval(pollTimer);
                    pollTimer = null;

                    updateIndexPill(status);

                    if (status.complete) {
                        var current = searchInput
                            ? searchInput.value.trim()
                            : '';

                        if (query && current === query) {
                            runDeepSearch(query);
                        } else {
                            hideScanBar();
                        }
                    } else {
                        hideScanBar();
                        toast(status.error || 'Index scan complete nahi hua.');
                    }
                })
                .catch(function () {
                    // Keep polling; a transient failure should
                    // not kill the search.
                });
        }, 1500);
    }

    function warmIndex() {
        fetch('/FileManager/Warm', {
            headers: { 'Accept': 'application/json' }
        })
            .then(function (response) { return response.json(); })
            .then(function (status) {
                updateIndexPill(status);

                if (status.running) {
                    pollScan('');
                }
            })
            .catch(function () {
                // A warm-up failure should not block normal browsing.
            });
    }

    function runDeepSearch(query) {
        var sequence = ++deepSeq;

        lastDeepQuery = query;

        showScanBar('Searching all folders…');

        var url = '/FileManager/Search?deep=true' +
            '&q=' + encodeURIComponent(query) +
            '&kind=' + encodeURIComponent(kind) +
            '&path=' + encodeURIComponent(currentPath);

        fetch(url, { headers: { 'Accept': 'application/json' } })
            .then(function (response) { return response.json(); })
            .then(function (data) {
                if (sequence !== deepSeq) {
                    return;
                }

                if (data.error) {
                    hideScanBar();
                    toast(data.error);
                    return;
                }

                if (data.scanning) {
                    updateScanText(data.status);
                    updateIndexPill(data.status);
                    pollScan(query);
                    return;
                }

                hideScanBar();
                updateIndexPill(data.status);
                renderResults(data.items || []);
            })
            .catch(function () {
                if (sequence !== deepSeq) {
                    return;
                }

                hideScanBar();
                toast('Search request failed.');
            });
    }

    // -----------------------------------------------------
    // RESULTS
    // -----------------------------------------------------

    function buildFolderCard(item) {
        var card = document.createElement('a');

        card.className = 'fm-card fm-folder';
        card.href = item.viewUrl;
        card.dataset.name = item.name;
        card.dataset.kind = 'folder';

        var iconWrap = element('div', 'fm-card-icon');
        iconWrap.appendChild(svgIcon('#fm-i-folder', 'fm-folder-svg'));

        var body = element('div', 'fm-card-body');
        body.appendChild(element('div', 'fm-name', item.name));
        body.appendChild(element(
            'div',
            'fm-sub',
            item.parent && item.parent !== '/' ? item.parent : 'Folder'));

        card.appendChild(iconWrap);
        card.appendChild(body);

        card.addEventListener('click', function () {
            showOverlay(true);
        });

        return card;
    }

    function buildFileCard(item) {
        var card = document.createElement('article');

        card.className = 'fm-card fm-file';
        card.dataset.name = item.name;
        card.dataset.path = item.path;
        card.dataset.ext = item.ext || '';
        card.dataset.kind = item.kind || 'other';
        card.dataset.viewUrl = item.viewUrl || '';
        card.dataset.downloadUrl = item.downloadUrl || '';

        var iconWrap = element('div', 'fm-card-icon');
        var tile = element(
            'span',
            'fm-file-tile fm-kind-' + (item.kind || 'other'),
            item.label || 'FILE');

        iconWrap.appendChild(tile);

        if (item.kind === 'image' && item.viewUrl) {
            iconWrap.classList.add('fm-has-thumb');

            var thumb = document.createElement('img');
            thumb.className = 'fm-thumb';
            thumb.src = item.viewUrl;
            thumb.alt = '';
            thumb.loading = 'lazy';
            thumb.addEventListener('error', function () {
                thumb.remove();
            });

            iconWrap.appendChild(thumb);
        }

        var body = element('div', 'fm-card-body');
        body.appendChild(element('div', 'fm-name', item.name));

        if (item.parent && item.parent !== '/') {
            body.appendChild(element('div', 'fm-path', item.parent));
        }

        var sub = element(
            'div',
            'fm-sub',
            (item.sizeText || '') +
            (item.modified ? '  ·  ' + item.modified : ''));

        body.appendChild(sub);

        var actions = element('div', 'fm-card-actions');

        var view = element('button', 'fm-btn fm-btn-primary fm-view');
        view.type = 'button';
        view.title = 'View ' + item.name;
        view.setAttribute('aria-label', 'View ' + item.name);
        view.appendChild(svgIcon('#fm-i-eye'));
        view.appendChild(element('span', 'fm-btn-label', 'View'));
        view.addEventListener('click', function () { openViewer(item); });

        var download = element('a', 'fm-btn fm-btn-ghost');
        download.href = item.downloadUrl || '#';
        download.title = 'Download ' + item.name;
        download.setAttribute('aria-label', 'Download ' + item.name);
        download.appendChild(svgIcon('#fm-i-download'));
        download.appendChild(element('span', 'fm-btn-label', 'Download'));

        actions.appendChild(view);
        actions.appendChild(download);

        card.appendChild(iconWrap);
        card.appendChild(body);
        card.appendChild(actions);

        return card;
    }

    function renderResults(items) {
        if (!resultsSection || !resultsLocalGrid || !resultsOtherGrid) {
            return;
        }

        resultsLocalGrid.replaceChildren();
        resultsOtherGrid.replaceChildren();

        var local = [];
        var elsewhere = [];
        var base = (currentPath || '/').replace(/\/+$/, '') || '/';

        items.forEach(function (item) {
            var parent = (item.parent || '/').replace(/\/+$/, '') || '/';
            var inCurrent = parent === base;

            (inCurrent ? local : elsewhere).push(item);
        });

        local.forEach(function (item) {
            resultsLocalGrid.appendChild(
                item.folder ? buildFolderCard(item) : buildFileCard(item));
        });

        elsewhere.forEach(function (item) {
            resultsOtherGrid.appendChild(
                item.folder ? buildFolderCard(item) : buildFileCard(item));
        });

        if (resultsLocalGroup) {
            resultsLocalGroup.hidden = local.length === 0;
        }

        if (resultsOtherGroup) {
            resultsOtherGroup.hidden = elsewhere.length === 0;
        }

        if (resultsSummary) {
            var parts = [];

            if (local.length) {
                parts.push(local.length + ' in this folder');
            }

            if (elsewhere.length) {
                parts.push(elsewhere.length + ' elsewhere');
            }

            resultsSummary.textContent = parts.length
                ? parts.join(' · ')
                : 'No matches';
        }

        if (resultsCount) {
            resultsCount.textContent = items.length;
        }

        if (resultsEmpty) {
            resultsEmpty.hidden = items.length > 0;
        }

        if (folderSection) {
            folderSection.hidden = true;
        }

        if (fileSection) {
            fileSection.hidden = true;
        }

        resultsSection.hidden = false;
        app.classList.add('fm-results-open');

        window.scrollTo({ top: 0, behavior: 'smooth' });
    }

    if (resultsClose) {
        resultsClose.addEventListener('click', clearDeepResults);
    }

    // -----------------------------------------------------
    // VIEWER
    // -----------------------------------------------------

    var videoExtensions = ['mp4', 'webm', 'ogv', 'ogg', 'm4v'];
    var imageExtensions = ['png', 'jpg', 'jpeg', 'gif', 'webp', 'bmp'];
    var sheetExtensions = ['xlsx', 'xls', 'csv'];
    var textExtensions = [
        'txt', 'log', 'md', 'json', 'xml', 'csv',
        'cs', 'js', 'css', 'html', 'htm', 'config',
        'ini', 'sql', 'bat', 'ps1', 'yml', 'yaml'
    ];

    function noPreview(host, item, message) {
        var wrap = element('div', 'fm-no-preview');

        wrap.appendChild(element('p', '', message ||
            'Is file ka browser preview nahi hai.'));

        var link = element('a', 'fm-btn fm-btn-primary');
        link.href = item.downloadUrl ||
            '/FileManager/Download?path=' + encodeURIComponent(item.path);
        link.appendChild(svgIcon('#fm-i-download'));
        link.appendChild(document.createTextNode(' Download file'));

        wrap.appendChild(link);
        host.replaceChildren(wrap);
    }

    function openViewer(item) {
        if (!viewerBody || !viewerModal) {
            toast('Viewer load nahi hua.');
            return;
        }

        var ext = (item.ext || '').replace('.', '').toLowerCase();

        viewerTitle.textContent = item.name || 'File';

        var meta = [];

        if (item.sizeText) {
            meta.push(item.sizeText);
        }

        if (item.modified) {
            meta.push(item.modified);
        }

        viewerSub.textContent = meta.join('  ·  ');

        viewerTile.className =
            'fm-file-tile fm-kind-' + (item.kind || 'other');

        viewerTile.textContent = item.label ||
            (ext ? ext.toUpperCase().slice(0, 4) : 'FILE');

        viewerOpen.href = item.viewUrl || '#';
        viewerDownload.href = item.downloadUrl ||
            '/FileManager/Download?path=' + encodeURIComponent(item.path);

        viewerBody.replaceChildren();

        var rawUrl = '/FileManager/Raw?path=' +
            encodeURIComponent(item.path);

        if (videoExtensions.indexOf(ext) >= 0) {
            var video = element('video', 'fm-video');
            video.controls = true;
            video.autoplay = true;
            video.playsInline = true;
            video.src = item.viewUrl;

            viewerBody.appendChild(video);
        } else if (ext === 'pdf') {
            var pdf = element('iframe', 'fm-pdf');
            pdf.src = item.viewUrl;
            pdf.title = item.name;

            viewerBody.appendChild(pdf);
        } else if (imageExtensions.indexOf(ext) >= 0) {
            var imageWrap = element('div', 'fm-img-wrap');
            var image = element('img', 'fm-img');
            image.src = item.viewUrl;
            image.alt = item.name;

            imageWrap.appendChild(image);
            viewerBody.appendChild(imageWrap);
        } else if (['mp3', 'wav', 'm4a', 'aac'].indexOf(ext) >= 0) {
            var audio = element('audio', 'fm-audio');
            audio.controls = true;
            audio.autoplay = true;
            audio.src = item.viewUrl;

            viewerBody.appendChild(audio);
        } else if (sheetExtensions.indexOf(ext) >= 0) {
            renderSheet(rawUrl, viewerBody, item, ext);
        } else if (textExtensions.indexOf(ext) >= 0) {
            renderText(rawUrl, viewerBody, item);
        } else if (['doc', 'docx', 'ppt', 'pptx'].indexOf(ext) >= 0) {
            renderOffice(item, viewerBody);
        } else {
            noPreview(viewerBody, item);
        }

        viewerModal.show();
    }

    function renderOffice(item, host) {
        if (!/^https?:\/\//i.test(item.viewUrl || '')) {
            noPreview(host, item,
                'Office preview ke liye file publicly accessible honi chahiye.');
            return;
        }

        var frame = element('iframe', 'fm-pdf');

        frame.src = 'https://view.officeapps.live.com/op/embed.aspx?src=' +
            encodeURIComponent(item.viewUrl);

        frame.title = item.name;

        host.appendChild(frame);
    }

    function renderText(url, host, item) {
        host.replaceChildren(
            element('div', 'fm-loading'));

        var spinner = element('span', 'fm-spinner');
        host.firstChild.appendChild(spinner);

        fetch(url, { credentials: 'same-origin' })
            .then(function (response) {
                if (!response.ok) {
                    throw new Error('File load nahi hui (' + response.status + ').');
                }

                return response.text();
            })
            .then(function (text) {
                var pre = element('pre', 'fm-pre');
                pre.textContent = text.slice(0, 2000000);

                host.replaceChildren(pre);
            })
            .catch(function (error) {
                noPreview(host, item, error.message);
            });
    }

    function renderSheet(url, host, item, ext) {
        var loading = element('div', 'fm-loading');
        loading.appendChild(element('span', 'fm-spinner'));

        host.replaceChildren(loading);

        if (!window.XLSX) {
            noPreview(host, item,
                'Excel engine load nahi hua. Page refresh kijiye.');
            return;
        }

        var isCsv = ext === 'csv';

        fetch(url, { credentials: 'same-origin' })
            .then(function (response) {
                if (!response.ok) {
                    throw new Error('Excel file load nahi hui (' + response.status + ').');
                }

                return isCsv ? response.text() : response.arrayBuffer();
            })
            .then(function (data) {
                var workbook = isCsv
                    ? window.XLSX.read(data, { type: 'string' })
                    : window.XLSX.read(data, { type: 'array' });

                if (!workbook.SheetNames.length) {
                    throw new Error('Workbook me koi sheet nahi hai.');
                }

                var tabs = element('div', 'fm-tabs');
                var sheetWrap = element('div', 'fm-sheet-wrap');
                var note = element('div', 'fm-sheet-note');

                host.replaceChildren(tabs, sheetWrap, note);

                function draw(index) {
                    var sheet = workbook.Sheets[workbook.SheetNames[index]];

                    var rows = window.XLSX.utils.sheet_to_json(sheet, {
                        header: 1,
                        defval: '',
                        raw: false
                    });

                    var rowLimit = 2000;
                    var columnLimit = 60;
                    var table = document.createElement('table');
                    var tbody = document.createElement('tbody');

                    var count = Math.min(rows.length, rowLimit);

                    for (var r = 0; r < count; r++) {
                        var row = rows[r] || [];
                        var tr = document.createElement('tr');

                        var columns = Math.min(row.length, columnLimit);

                        for (var c = 0; c < columns; c++) {
                            var cell = document.createElement(
                                r === 0 ? 'th' : 'td');

                            cell.textContent = row[c] === null ||
                                row[c] === undefined
                                ? ''
                                : String(row[c]);

                            tr.appendChild(cell);
                        }

                        tbody.appendChild(tr);
                    }

                    table.appendChild(tbody);
                    sheetWrap.replaceChildren(table);

                    note.textContent = rows.length > rowLimit
                        ? 'Showing ' + rowLimit + ' of ' +
                          rows.length + ' rows. Download to see all.'
                        : rows.length + ' rows · ' +
                          ((rows[0] && rows[0].length) || 0) + ' columns';
                }

                workbook.SheetNames.forEach(function (name, index) {
                    var tab = element(
                        'button',
                        'fm-tab' + (index === 0 ? ' is-active' : ''),
                        name);

                    tab.type = 'button';

                    tab.addEventListener('click', function () {
                        Array.prototype.forEach.call(
                            tabs.querySelectorAll('.fm-tab'),
                            function (other) {
                                other.classList.toggle(
                                    'is-active', other === tab);
                            });

                        draw(index);
                    });

                    tabs.appendChild(tab);
                });

                draw(0);
            })
            .catch(function (error) {
                noPreview(host, item, error.message);
            });
    }

    // Open a viewer from a server-rendered card.
    document.addEventListener('click', function (event) {
        var button = event.target.closest('.fm-view');

        if (!button) {
            return;
        }

        var card = button.closest('.fm-card');

        if (!card) {
            return;
        }

        event.preventDefault();

        var sub = card.querySelector('.fm-sub');
        var subText = sub ? sub.textContent.trim() : '';
        var parts = subText.split('·');

        openViewer({
            name: card.dataset.name || 'File',
            path: card.dataset.path || '',
            ext: card.dataset.ext || '',
            kind: card.dataset.kind || 'other',
            viewUrl: card.dataset.viewUrl || '',
            downloadUrl: card.dataset.downloadUrl || '',
            sizeText: (parts[0] || '').trim(),
            modified: (parts[1] || '').trim(),
            label: (card.dataset.ext || '')
                .replace('.', '')
                .toUpperCase()
                .slice(0, 4)
        });
    });

    if (viewerEl) {
        viewerEl.addEventListener('hidden.bs.modal', function () {
            if (viewerBody) {
                viewerBody.replaceChildren();
            }
        });
    }

    // -----------------------------------------------------
    // CARD MENUS / COPY / NAVIGATION
    // -----------------------------------------------------

    document.addEventListener('click', function (event) {
        var toggle = event.target.closest('[data-menu-toggle]');

        if (toggle) {
            event.preventDefault();
            event.stopPropagation();

            var menu = toggle.parentElement.querySelector('.fm-menu');

            closeMenus(menu);

            if (menu) {
                menu.hidden = !menu.hidden;
            }

            return;
        }

        var copy = event.target.closest('[data-copy]');

        if (copy) {
            event.preventDefault();
            copyLink(copy.dataset.copy || '');
            closeMenus();
            return;
        }

        if (!event.target.closest('.fm-menu')) {
            closeMenus();
        }

        var folder = event.target.closest('a.fm-folder');

        if (folder &&
            !event.ctrlKey &&
            !event.metaKey &&
            !event.shiftKey &&
            folder.target !== '_blank') {
            showOverlay(true);
        }
    });

    // -----------------------------------------------------
    // STICKY BAR / BREADCRUMB / SHORTCUTS
    // -----------------------------------------------------

    function updateChipsScrollUI() {
        if (!chipsScroller) {
            return;
        }

        var maxScroll = Math.max(
            0,
            chipsScroller.scrollWidth - chipsScroller.clientWidth);

        var scrollLeft = chipsScroller.scrollLeft;
        var canScroll = maxScroll > 2;
        var atStart = scrollLeft <= 1;
        var atEnd = scrollLeft >= maxScroll - 1;

        if (chipsPrev) {
            chipsPrev.hidden = !canScroll || atStart;
        }

        if (chipsNext) {
            chipsNext.hidden = !canScroll || atEnd;
        }

        if (chipsShell) {
            chipsShell.classList.toggle(
                'has-prev', canScroll && !atStart);
            chipsShell.classList.toggle(
                'has-next', canScroll && !atEnd);
        }
    }

    if (chipsScroller) {
        chipsScroller.addEventListener('scroll', updateChipsScrollUI, {
            passive: true
        });
    }

    if (chipsPrev) {
        chipsPrev.addEventListener('click', function () {
            if (chipsScroller.scrollBy) {
                chipsScroller.scrollBy({ left: -220, behavior: 'smooth' });
            } else {
                chipsScroller.scrollLeft -= 220;
            }
        });
    }

    if (chipsNext) {
        chipsNext.addEventListener('click', function () {
            if (chipsScroller.scrollBy) {
                chipsScroller.scrollBy({ left: 220, behavior: 'smooth' });
            } else {
                chipsScroller.scrollLeft += 220;
            }

            window.setTimeout(refreshChipsScrollUI, 350);
        });
    }

    function refreshChipsScrollUI() {
        window.requestAnimationFrame(function () {
            updateChipsScrollUI();

            window.requestAnimationFrame(updateChipsScrollUI);
        });
    }

    if (window.ResizeObserver) {
        var chipsResizeObserver = new ResizeObserver(function () {
            refreshChipsScrollUI();
        });

        if (chipsScroller) {
            chipsResizeObserver.observe(chipsScroller);
        }

        if (chipsShell) {
            chipsResizeObserver.observe(chipsShell);
        }
    }

    window.addEventListener('load', refreshChipsScrollUI);
    window.addEventListener('orientationchange', refreshChipsScrollUI);

    if (document.fonts && document.fonts.ready) {
        document.fonts.ready.then(refreshChipsScrollUI);
    }

    window.setTimeout(refreshChipsScrollUI, 0);
    window.setTimeout(refreshChipsScrollUI, 250);

    function applyCrumbMode() {
        if (!crumbs) {
            return;
        }

        var anchorCount = crumbs.querySelectorAll('a').length;

        crumbs.classList.toggle(
            'is-compact',
            window.innerWidth <= 700 && anchorCount > 1);
    }

    var scrollTick = false;

    function updateScrollUI() {
        var scrollY = window.scrollY || window.pageYOffset || 0;

        if (toTop) {
            toTop.hidden = scrollY < 480;
        }
    }

    function requestScrollUI() {
        if (scrollTick) {
            return;
        }

        scrollTick = true;

        window.requestAnimationFrame(function () {
            scrollTick = false;
            updateScrollUI();
        });
    }

    window.addEventListener('scroll', requestScrollUI, { passive: true });
    window.addEventListener('resize', function () {
        applyCrumbMode();
        refreshChipsScrollUI();
        requestScrollUI();
    });

    if (toTop) {
        toTop.addEventListener('click', function () {
            window.scrollTo({ top: 0, behavior: 'smooth' });
        });
    }

    if (resultsReset) {
        resultsReset.addEventListener('click', function () {
            if (filterClearButtons.length) {
                filterClearButtons[0].click();
            } else {
                clearDeepResults();
            }
        });
    }

    document.addEventListener('keydown', function (event) {
        var target = event.target;
        var tag = target && target.tagName
            ? target.tagName.toLowerCase()
            : '';

        var editing = tag === 'input' ||
            tag === 'textarea' ||
            tag === 'select' ||
            (target && target.isContentEditable);

        var shortcut = event.key === '/' ||
            ((event.ctrlKey || event.metaKey) &&
                event.key.toLowerCase() === 'k');

        if (!editing && shortcut && searchInput) {
            event.preventDefault();
            searchInput.focus();
            searchInput.select();
        }
    });

    // -----------------------------------------------------
    // INITIAL STATE
    // -----------------------------------------------------

    applyCrumbMode();
    updateScrollUI();
    updateChipsScrollUI();
    applyFilter();
    warmIndex();
})();
