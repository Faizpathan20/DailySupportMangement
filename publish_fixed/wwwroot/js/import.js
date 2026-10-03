// ==========================================
// IMPORT PAGE
//
// The file is read in the browser with SheetJS,
// painted into the grid, and only sent to the
// server when "Save to Database" is pressed.
//
// State:
//   state.headers  - one label per column
//   state.rows     - array of array of strings
//   state.readOnly - true when an already saved
//                    table is being viewed
// ==========================================

var state = {
    headers: [],
    rows: [],
    types: [],
    searchText: [],
    fileName: "",
    readOnly: false,
    loadedFrom: "",

    // What the browser asked for as the SQL type of
    // each column. Empty string means "use what the
    // file says". Kept apart from state.types, which is
    // always the value that will actually be sent.
    typeOverrides: [],

    // One flag per row: 1 means the row is ticked and
    // will be written. A Uint8Array is used because it
    // stays cheap at 20,000 rows.
    rowSel: null,

    // Cells the user typed into, as "row:column". The
    // grid paints them so an edit is not lost in a long
    // list of rows.
    edited: {},

    // The hidden row id the server sent with each row of
    // an opened table, in the same order as rows. It is
    // what an update is matched on, so it has to stay
    // lined up with the row it belongs to no matter what
    // the grid does with sorting or filtering. Empty for
    // anything read out of a file.
    rowIds: [],

    // "append", "replace" or "update".
    mode: "append"
};

var grid = {
    page: 1,
    pageSize: 25,
    sortIndex: -1,
    sortAsc: true,
    view: []          // filtered + sorted row indexes
};

var MAX_ROWS = window.IMPORT_MAX_ROWS || 20000;

var TOKEN = window.IMPORT_TOKEN || "";

// Set while a file is being read, so Cancel can stop it.
var loading = { active: false, reader: null };


// ==========================================
// SMALL HELPERS
// ==========================================

function $(id) {
    return document.getElementById(id);
}


function escapeHtml(value) {

    return String(value === null || value === undefined
        ? ""
        : value)
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;");

}


// Points at one of the <symbol> icons defined at the top
// of the page. Replaces an emoji so the glyph matches the
// rest of the page and takes its colour from the text
// around it.
function setIcon(element, iconId) {

    if (!element) {
        return;
    }

    element.innerHTML = '<svg class="ip-ico" viewBox="0 0 24 24">'
        + '<use href="#' + iconId + '"></use></svg>';

}


function showToast(message, isError) {

    var box = $("masterToastContainer");

    if (!box) {
        box = document.createElement("div");
        box.id = "masterToastContainer";
        document.body.appendChild(box);
    }

    var toast = document.createElement("div");

    toast.className = "master-toast "
        + (isError
            ? "master-toast-error"
            : "master-toast-success");

    toast.innerText = message;

    box.appendChild(toast);

    setTimeout(function () {
        toast.classList.add("show");
    }, 10);

    setTimeout(function () {
        toast.classList.remove("show");

        setTimeout(function () {
            if (toast.parentNode) {
                toast.parentNode.removeChild(toast);
            }
        }, 300);
    }, isError ? 8000 : 4500);

}


function setBusy(button, busy, busyText) {

    if (!button) {
        return;
    }

    if (busy) {
        button.dataset.text = button.innerText;
        button.disabled = true;
        button.innerText = busyText || "Working...";
    } else {
        button.disabled = false;

        button.innerText = button.dataset.text
            || button.innerText;
    }

}


function busyOverlay(id, on) {

    var overlay = $(id);

    if (overlay) {
        overlay.classList.toggle("visible", !!on);
    }

}


// Lower-cased copy of every row, built once, so
// typing in the search box stays instant even with
// 20,000 rows on screen.
function indexRows() {

    state.searchText = state.rows.map(function (row) {
        return row.join(" ").toLowerCase();
    });

}


// Turns any file / sheet name into something SQL
// Server will accept, so the suggestion matches
// what actually gets created.
function cleanName(value) {

    var text = String(value || "")
        .replace(/[^A-Za-z0-9_]+/g, "_")
        .replace(/_+/g, "_")
        .replace(/^_+/, "")
        .replace(/_+$/, "");

    if (!text) {
        return "";
    }

    if (/^[0-9]/.test(text)) {
        text = "T" + text;
    }

    return text.substring(0, 100);

}


// ==========================================
// FILE READING (SheetJS)
// ==========================================

function pad2(n) {
    return n < 10 ? "0" + n : "" + n;
}


// Date to the one format the server can read back:
// dd/MM/yyyy, with the time appended only when it is
// not midnight. A plain date is then stored as a date
// column and a real date-time as datetime2.
//
// The UTC getters are the right ones here. An Excel
// date is a day count with no time zone, so SheetJS
// builds it on a UTC midnight. Reading the local hour
// instead would tag every midnight value with the
// machine's offset - 00:00 comes out as 05:30 on a
// UTC+5:30 box, and the column stops being a plain
// date.
function dateToText(d) {

    var text = pad2(d.getUTCDate())
        + "/" + pad2(d.getUTCMonth() + 1)
        + "/" + d.getUTCFullYear();

    var hours = d.getUTCHours();
    var minutes = d.getUTCMinutes();

    if (hours || minutes
        || d.getUTCSeconds()
        || d.getUTCMilliseconds()) {

        text += " " + pad2(hours) + ":" + pad2(minutes);
    }

    return text;

}


// One workbook cell to one plain string.
//
// The cells are walked by hand rather than through
// sheet_to_json because a date in .xlsx is stored as a
// plain number with a date format attached - 15/01/2026
// really is the number 46037 - and cellDates:true does
// not always turn that number into a Date. Reading the
// format off the cell and decoding the serial by hand
// is the only way to be sure the date survives.
function cellToText(cell) {

    if (!cell || cell.v === null || cell.v === undefined) {
        return "";
    }

    if (cell.t === "d") {
        return dateToText(
            cell.v instanceof Date
                ? cell.v
                : new Date(cell.v));
    }

    if (cell.t === "b") {
        return cell.v ? "Yes" : "No";
    }

    if (cell.t === "n" || cell.t === "e") {

        // Date or time format on a number cell.
        if (cell.z && XLSX.SSF
            && XLSX.SSF.is_date(cell.z)) {

            var parts = XLSX.SSF.parse_date_code(cell.v);

            if (parts && parts.y) {

                // Built on a UTC epoch to match how
                // dateToText reads it back.
                return dateToText(new Date(Date.UTC(
                    parts.y,
                    parts.m - 1,
                    parts.d,
                    parts.H || 0,
                    parts.M || 0,
                    Math.floor(parts.S || 0))));
            }
        }

        if (cell.t === "e") {
            return String(cell.w || cell.v);
        }

        // Excel keeps every number as a float, so
        // 1250.5 can arrive as 1250.4999999999998.
        var rounded = Math.round(cell.v * 1e10) / 1e10;

        return Object.is(rounded, -0) ? "0" : String(rounded);
    }

    return String(cell.v).trim();

}


// The progress bar is shown while a file is being read.
//
// XLSX.read() is one blocking call with nothing to
// report from inside it, so the bar is indeterminate for
// that part. The walk over the rows afterwards is done in
// small chunks with a yield between them, which is what
// makes the bar a real percentage and is also what lets
// Cancel take effect before a large sheet is finished.
function showProgress(label, percent) {

    var wrap = $("parseProgress");

    if (!wrap) {
        return;
    }

    wrap.hidden = false;

    $("progressLabel").innerText = label;

    var fill = $("progressFill");

    // A null percent is the striped bar that just moves,
    // used while the reader is inside XLSX.read().
    var ratio = percent === null || percent === undefined
        ? null
        : Math.max(0, Math.min(1, percent));

    if (ratio === null) {
        fill.classList.add("is-indeterminate");
        fill.style.width = "";
    } else {
        fill.classList.remove("is-indeterminate");
        fill.style.width = Math.round(ratio * 100) + "%";
    }

}


function hideProgress() {

    var wrap = $("parseProgress");

    if (wrap) {
        wrap.hidden = true;
    }

    loading.active = false;

}


function cancelLoad() {

    loading.cancelled = true;

    if (loading.reader
        && loading.reader.readyState === 1) {
        loading.reader.abort();
    }

    hideProgress();

    showToast("Loading cancelled.", false);

}


function readWorkbook(file, wantedSheet, done, fail) {

    loading.active = true;
    loading.cancelled = false;

    showProgress("Reading " + file.name + "…", null);

    var reader = new FileReader();

    loading.reader = reader;

    reader.onerror = function () {
        hideProgress();
        fail("The file could not be read.");
    };

    reader.onabort = function () {
        hideProgress();
    };

    reader.onload = function (event) {

        if (loading.cancelled) {
            hideProgress();
            return;
        }

        var bytes = new Uint8Array(event.target.result);

        showProgress("Opening the workbook…", null);

        var book;

        try {
            book = XLSX.read(bytes, {
                type: "array",
                cellDates: true,
                cellNF: true,
                cellStyles: true
            });
        } catch (e) {
            hideProgress();
            fail("'" + file.name + "' could not be "
                + "opened: " + (e && e.message ? e.message : e));
            return;
        }

        if (loading.cancelled) {
            hideProgress();
            return;
        }

        var sheets = book.SheetNames || [];

        if (sheets.length === 0) {
            hideProgress();
            fail("The workbook has no sheets.");
            return;
        }

        var sheetName = sheets.indexOf(wantedSheet) !== -1
            ? wantedSheet
            : sheets[0];

        var sheet = book.Sheets[sheetName];

        if (!sheet || !sheet["!ref"]) {
            hideProgress();
            fail("Sheet \"" + sheetName + "\" is empty.");
            return;
        }

        var range = XLSX.utils.decode_range(sheet["!ref"]);

        var total = range.e.r - range.s.r + 1;

        var matrix = [];

        var row = range.s.r;

        // Rows are taken in slices small enough that the
        // browser gets a turn between them. Without the
        // yield the bar would jump from 0 to 100 and the
        // Cancel button would not answer until the whole
        // sheet had been walked.
        var CHUNK = 1500;

        function step() {

            if (loading.cancelled) {
                hideProgress();
                return;
            }

            var stop = Math.min(row + CHUNK, range.e.r + 1);

            for (; row < stop; row++) {

                var values = [];

                for (var c = range.s.c; c <= range.e.c; c++) {
                    values.push(cellToText(sheet[
                        XLSX.utils.encode_cell({ r: row, c: c })]));
                }

                // A row of nothing but blanks is a gap in
                // the sheet, not a record.
                if (values.some(function (v) {
                        return v !== "";
                    })) {
                    matrix.push(values);
                }

            }

            var seen = Math.min(row - range.s.r, total);

            showProgress(
                "Reading rows…",
                total > 0 ? seen / total : 1);

            if (row <= range.e.r) {
                setTimeout(step, 0);
                return;
            }

            hideProgress();

            done(sheets, sheetName, matrix);

        }

        step();

    };

    reader.readAsArrayBuffer(file);

}


// Picks the delimiter by counting the candidates on
// the first non-empty line. Comma, semicolon and tab
// are the ones that turn up in real exports.
function detectDelimiter(text) {

    var line = text.split(/\r?\n/).filter(function (l) {
        return l.trim() !== "";
    })[0] || "";

    var commas = (line.match(/,/g) || []).length;
    var semis = (line.match(/;/g) || []).length;
    var tabs = (line.match(/\t/g) || []).length;

    if (tabs > commas && tabs > semis) {
        return "\t";
    }

    return semis > commas ? ";" : ",";

}


// RFC 4180 reader: quoted fields, "" for a literal
// quote, CRLF or LF endings. Written here instead of
// being handed to SheetJS, whose CSV reader rewrites
// the header row (it upper-cases it) and the column
// names have to come through exactly as they are in
// the file.
function parseCsvText(text) {

    var delimiter = detectDelimiter(text);

    // Strip the UTF-8 byte order mark, or it ends up
    // glued to the first column name.
    var bom = String.fromCharCode(0xFEFF);

    text = text.indexOf(bom) === 0
        ? text.substring(bom.length)
        : text;

    var rows = [];
    var row = [];
    var field = "";
    var quoted = false;

    for (var i = 0; i < text.length; i++) {

        var c = text[i];

        if (quoted) {

            if (c === "\"") {

                // "" inside a quoted field is one quote
                if (text[i + 1] === "\"") {
                    field += "\"";
                    i++;
                } else {
                    quoted = false;
                }

            } else {
                field += c;
            }

            continue;
        }

        if (c === "\"") {
            quoted = true;
            continue;
        }

        if (c === delimiter) {
            row.push(field.trim());
            field = "";
            continue;
        }
if (c === "\r") {
            continue;
        }

        if (c === "\n") {
            row.push(field.trim());
            rows.push(row);
            row = [];
            field = "";
            continue;
        }

        field += c;

    }

    if (field.length > 0 || row.length > 0) {
        row.push(field.trim());
        rows.push(row);
    }

    return rows;

}


function readCsvText(file, done, fail) {

    loading.active = true;
    loading.cancelled = false;

    showProgress("Reading " + file.name + "…", null);

    var reader = new FileReader();

    loading.reader = reader;

    reader.onerror = function () {
        hideProgress();
        fail("The file could not be read.");
    };

    reader.onabort = function () {
        hideProgress();
    };

    reader.onload = function (event) {

        if (loading.cancelled) {
            hideProgress();
            return;
        }

        showProgress("Splitting the text…", 0.4);

        var text = String(event.target.result || "");

        // The parser runs on one go, so the yield is
        // before it rather than inside it. That still
        // lets a Cancel land before the work starts.
        setTimeout(function () {

            if (loading.cancelled) {
                hideProgress();
                return;
            }

            try {
                done(["CSV"], "CSV",
                    parseCsvText(text));
            } catch (e) {
                hideProgress();
                fail("The CSV file could not be read: "
                    + (e && e.message ? e.message : e));
                return;
            }

            hideProgress();

        }, 0);

    };

    // Excel writes a BOM on UTF-8 CSVs; reading it as
    // windows-1252 would mangle any other character,
    // so UTF-8 is used and the BOM is stripped in the
    // parser.
    reader.readAsText(file, "UTF-8");

}


// The reader is a deferred script, so it is still arriving
// when a fast user reaches the file picker. Waiting for it is
// the right move: telling them to refresh only worked because
// the second visit found it cached. It is already downloading
// in parallel, so the wait is normally a fraction of a second.

function readerReady() {

    return typeof XLSX !== "undefined"
        && XLSX
        && typeof XLSX.read === "function";

}

// Two minutes is far longer than a 932 KB file can need over
// any link that has not already failed, so reaching this means
// the file itself is missing rather than slow.
var READER_TIMEOUT = 120000;

function whenReaderReady(done) {

    if (readerReady()) {
        done();
        return;
    }

    showProgress("Preparing the Excel reader…", null);

    var started = Date.now();

    var wait = setInterval(function () {

        if (readerReady()) {
            clearInterval(wait);
            hideProgress();
            done();
            return;
        }

        if (Date.now() - started > READER_TIMEOUT) {

            clearInterval(wait);
            hideProgress();

            showToast("The Excel reader "
                + "(xlsx.full.min.js) did not load, so the "
                + "file cannot be read. Please check your "
                + "connection and refresh the page.", true);

        }

    }, 120);

}


function loadFile(file) {

    if (!file) {
        return;
    }

    var name = (file.name || "").toLowerCase();

    if (!/\.(xlsx|xls|csv|txt)$/.test(name)) {
        showToast("Only .xlsx, .xls and .csv files "
            + "are supported.", true);
        return;
    }

    if (file.size > 10 * 1024 * 1024) {
        showToast("The file is larger than 10 MB.",
            true);
        return;
    }

    var picker = $("sheetPicker");
    var wanted = picker && picker.value ? picker.value : "";

    var onDone = function (sheets, sheetName, matrix) {
        applyMatrix(file.name, sheets, sheetName, matrix);
    };

    var onFail = function (message) {
        showToast(message, true);
    };

    var start = function () {

        readWorkbook(file, wanted, onDone, onFail);

    };

    // csv and txt never touch the reader, so they are not made
    // to wait for it. Only the workbook formats need XLSX.
    if (/\.csv$/.test(name) || /\.txt$/.test(name)) {
        readCsvText(file, onDone, onFail);
        return;
    }

    whenReaderReady(start);

}


function applyMatrix(fileName, sheets, sheetName, matrix) {

    // Drop completely empty rows.
    matrix = (matrix || []).filter(function (row) {
        return row.some(function (cell) {
            return String(cell).trim() !== "";
        });
    });

    if (matrix.length < 2) {

        // Say which of the two cases it is, because
        // "no data rows" sends people looking for a
        // filter that was never applied.
        showToast(matrix.length === 0
            ? "The sheet is empty. Nothing to import."
            : "The file has column names but no data "
                + "rows under them.", true);

        return;
    }

    var width = 0;

    matrix.forEach(function (row) {
        if (row.length > width) {
            width = row.length;
        }
    });

    var headers = [];

    for (var c = 0; c < width; c++) {

        var label = String(matrix[0][c] || "").trim();

        headers.push(label === ""
            ? "Column " + (c + 1)
            : label);

    }

    // Two identical headers would become the same
    // SQL column, so they are numbered apart here.
    var seen = {};

    headers = headers.map(function (label) {
        var key = label.toLowerCase();

        if (!seen[key]) {
            seen[key] = 1;
            return label;
        }

        seen[key]++;

        return label + " (" + seen[key] + ")";
    });

    var rows = matrix.slice(1).map(function (row) {
        var out = [];

        for (var c = 0; c < width; c++) {
            out.push(String(row[c] === undefined
                ? ""
                : row[c]));
        }

        return out;
    });

    var truncated = false;

    if (rows.length > MAX_ROWS) {
        rows = rows.slice(0, MAX_ROWS);
        truncated = true;
    }

    state.headers = headers;
    state.rows = rows;
    state.fileName = fileName;
    state.readOnly = false;
    state.loadedFrom = "";
    state.types = [];
    state.typeOverrides = [];
    state.edited = {};
    state.mode = "append";

    // Rows read out of a file have no identity on the
    // server yet, so nothing is carried for an update.
    state.rowIds = [];

    // Every row starts ticked, so the common case stays
    // one click instead of a trip through a select-all.
    state.rowSel = new Uint8Array(rows.length);

    for (var s = 0; s < rows.length; s++) {
        state.rowSel[s] = 1;
    }

    indexRows();

    resetGrid();

    $("sheetPicker").innerHTML =
        sheets.map(function (name) {
            return '<option value="' + escapeHtml(name)
                + '"' + (name === sheetName
                    ? " selected"
                    : "") + ">"
                + escapeHtml(name) + "</option>";
        }).join("");

    // One sheet only means there is nothing to pick.
    $("sheetPicker").hidden = sheets.length < 2;

    $("dzFileName").innerText = fileName;

    setIcon($("dzFileIcon"),
        /\.csv$|\.txt$/i.test(fileName) ? "i-file" : "i-sheet");

    $("dzFileInfo").innerText =
        headers.length + " columns"
        + "  •  " + rows.length + " rows"
        + (sheets.length > 1
            ? "  •  " + sheets.length + " sheets"
            : "")
        + (truncated
            ? "  •  only the first "
                + MAX_ROWS + " rows were loaded"
            : "");

    $("dzIdle").hidden = true;
    $("dzLoaded").hidden = false;

    // Opening the picker left focus on the file input, and
    // that input now sits inside the panel just hidden. Focus
    // cannot stay on a hidden element, and leaving it there
    // means the next Tab starts from the top of the document.
    // It is dropped deliberately so the move is predictable.
    var picker = $("importFile");

    if (picker && picker === document.activeElement) {
        picker.blur();
    }

    $("tableName").value =
        cleanName(/\.(csv|txt)$/i.test(fileName)
            ? fileName.replace(/\.[^.]+$/, "")
            : (sheetName || fileName.replace(/\.[^.]+$/, "")));

    showBanner(
        truncated
            ? "Only the first " + MAX_ROWS
                + " rows of the file were loaded."
            : "",
        false);

    refreshSaveBar();
    render();

    $("previewSection").hidden = false;
    $("saveSection").hidden = false;

    $("previewSection").scrollIntoView({
        behavior: "smooth",
        block: "start"
    });

}


function showBanner(message, readOnly) {

    var banner = $("importBanner");

    if (!message) {
        banner.hidden = true;
        return;
    }

    $("bannerText").innerText = message;

    setIcon($("bannerIcon"), readOnly ? "i-eye" : "i-alert");

    banner.classList.toggle(
        "import-banner-view", !!readOnly);

    banner.hidden = false;

}


// ==========================================
// GRID
// ==========================================

function resetGrid() {
    grid.page = 1;
    grid.sortIndex = -1;
    grid.sortAsc = true;
}


// Column type hint for the grid header.
//
// This has to agree with ImportController.InferType, or
// the grid advertises nvarchar for a date column and
// the table created on Save turns out to be date. Only
// the first 400 rows are looked at, which is what the
// server does too.
function columnType(index) {

    // A type the user picked in the mapping panel wins
    // over anything guessed from the values.
    if (state.typeOverrides[index]) {
        return state.typeOverrides[index];
    }

    if (state.types[index]) {
        return state.types[index];
    }

    var allInt = true;
    var allNum = true;
    var allDate = true;
    var allBool = true;
    var longest = 0;
    var any = false;

    // Widest integer seen, to split int from bigint.
    var widest = 0;

    var limit = Math.min(state.rows.length, 400);

    for (var r = 0; r < limit; r++) {

        var value = String(
            state.rows[r][index] || "").trim();

        if (!value) {
            continue;
        }

        any = true;

        if (value.length > longest) {
            longest = value.length;
        }

        if (/^-?\d+$/.test(value)) {

            var n = Number(value);

            if (Math.abs(n) > Math.abs(widest)) {
                widest = n;
            }

        } else {
            allInt = false;
        }

        if (isNaN(Number(value))) {
            allNum = false;
        }

        // Read back as dd/MM/yyyy or dd/MM/yyyy hh:mm,
        // which is the shape dateToText writes. Turning
        // it into yyyy-mm-dd first avoids relying on
        // which side of the slash the browser calls the
        // month.
        //
        // The end of the date is matched with a
        // lookahead, not a group: a captured group gets
        // dropped along with the space it matched, and
        // "15/01/2026 14:23" would become the
        // unparseable "2026-01-1514:23".
        if (isNaN(Date.parse(
                value.replace(
                    /^(\d{2})\/(\d{2})\/(\d{4})(?=\s|$)/,
                    "$3-$2-$1")))) {
            allDate = false;
        }

        // Yes/No for booleans, case-insensitive
        var lower = value.toLowerCase();

        if (!(lower === "yes" || lower === "y"
            || lower === "no" || lower === "n"
            || lower === "true" || lower === "false")) {
            allBool = false;
        }

    }

    var type;

    if (!any) {
        type = "nvarchar(255)";
    } else if (allInt) {
        type = Math.abs(widest) <= 2147483647
            ? "int"
            : "bigint";
    } else if (allBool) {
        type = "bit";
    } else if (allNum) {
        type = "decimal(18,2)";
    } else if (allDate) {
        // Any row carrying a clock time makes the whole
        // column a datetime2.
        type = state.rows.some(function (row) {
            return /\d{2}\/\d{2}\/\d{4}\s\d{2}:/
                .test(String(row[index] || "").trim());
        })
            ? "datetime2"
            : "date";
    } else {
        type = longest <= 4000
            ? "nvarchar(" + longest + ")"
            : "nvarchar(max)";
    }

    state.types[index] = type;

    return type;

}


function computeView() {

    var needle = $("tableSearch").value
        .toLowerCase()
        .trim();

    var haystack = state.searchText;
    var view = [];

    for (var r = 0; r < state.rows.length; r++) {

        if (needle === ""
            || haystack[r].indexOf(needle) !== -1) {
            view.push(r);
        }

    }

    if (grid.sortIndex >= 0) {

        var index = grid.sortIndex;
        var sign = grid.sortAsc ? 1 : -1;

        view.sort(function (a, b) {

            var left = String(
                state.rows[a][index] || "");
            var right = String(
                state.rows[b][index] || "");

            var leftNum = Number(left);
            var rightNum = Number(right);

            if (left !== "" && right !== ""
                && !isNaN(leftNum) && !isNaN(rightNum)) {
                return (leftNum - rightNum) * sign;
            }

            return left.localeCompare(right,
                undefined, { numeric: true }) * sign;

        });

    }

    grid.view = view;

}


function renderHead() {

    // The tick column only appears while there is
    // something to save. A saved table being viewed is
    // read-only, so a column of dead checkboxes would
    // only get in the way.
    var select = !state.readOnly && state.rows.length > 0;

    var html = "<tr><th class=\"rs-sr\">#</th>";

    if (select) {
        html += "<th class=\"col-tick\">"
            + "<input type=\"checkbox\""
            + " id=\"checkAllRows\""
            + " title=\"Select every row\" />"
            + "</th>";
    }

    state.headers.forEach(function (header, index) {

        var arrow = "";

        if (grid.sortIndex === index) {
            arrow = grid.sortAsc ? " ▲" : " ▼";
        }

        var overridden = state.typeOverrides[index]
            ? " is-overridden"
            : "";

        html += "<th class=\"sortable\" data-index=\""
            + index + "\">"

            + "<span class=\"th-text\">"
            + escapeHtml(header)
            + arrow
            + "</span>"

            + "<span class=\"import-th-sub"
            + overridden + "\">"
            + escapeHtml(columnType(index))
            + "</span>"

            + "<span class=\"sort-indicator\">"
            + "<span class=\"sort-up\"></span>"
            + "<span class=\"sort-down\"></span>"
            + "</span></th>";

    });

    html += "</tr>";

    $("dataHead").innerHTML = html;

    $("dataHead").querySelectorAll("th.sortable")
        .forEach(function (th) {
            th.addEventListener("click", function () {
                var index = parseInt(
                    th.dataset.index, 10);

                if (grid.sortIndex === index) {
                    grid.sortAsc = !grid.sortAsc;
                } else {
                    grid.sortIndex = index;
                    grid.sortAsc = true;
                }

                grid.page = 1;

                render();
            });
        });

    var all = $("checkAllRows");

    if (all) {
        all.checked = allRowsPicked();

        all.indeterminate =
            !all.checked && someRowsPicked();

        all.addEventListener("change",
            function () {
                setAllRows(this.checked);
            });
    }

}


// How many rows are ticked, and whether that is all of
// them or only some. Both the toolbar and the sticky
// bar show this, so it is worked out in one place.
function pickedCount() {

    if (!state.rowSel) {
        return 0;
    }

    var count = 0;

    for (var r = 0; r < state.rowSel.length; r++) {
        if (state.rowSel[r]) {
            count++;
        }
    }

    return count;

}


function allRowsPicked() {

    return state.rowSel
        && pickedCount() === state.rows.length;

}


function someRowsPicked() {

    var picked = pickedCount();

    return picked > 0 && picked < state.rows.length;

}


function setAllRows(ticked) {

    if (!state.rowSel) {
        return;
    }

    for (var r = 0; r < state.rowSel.length; r++) {
        state.rowSel[r] = ticked ? 1 : 0;
    }

    render();

}


function renderStats() {

    var columns = state.headers.length;
    var rows = state.rows.length;

    var cards = [
        { label: "Columns", value: columns, cls: "is-blue" },
        { label: "Rows", value: rows, cls: "is-green" }
    ];

    if (state.readOnly) {

        cards.push({
            label: "Saved Table",
            value: state.loadedFrom,
            cls: "is-slate"
        });

    } else {

        var empty = 0;

        for (var c = 0; c < columns; c++) {

            var blank = 0;

            for (var r = 0; r < rows; r++) {
                if (!String(
                        state.rows[r][c] || "").trim()) {
                    blank++;
                }
            }

            empty += blank;
        }

        cards.push({
            label: "Empty Cells",
            value: empty,
            cls: empty > 0 ? "is-amber" : "is-green"
        });

    }

    $("importStats").innerHTML = cards.map(
        function (card) {
            return '<div class="istat ' + card.cls + '">'
                + '<span class="istat-value '
                + card.cls + '" title="'
                + escapeHtml(card.value)
                + '">'
                + escapeHtml(card.value)
                + "</span>"
                + '<span class="istat-label">'
                + card.label
                + "</span></div>";
        }).join("");

    // The four counts that only make sense once a file is
    // open sit beside the first two.
    var live = $("importStatsLive");

    if (live) {
        live.hidden = state.readOnly;

        if (!state.readOnly) {
            var edits = Object.keys(
                state.edited).length;

            var overrides = state
                .typeOverrides.filter(
                    function (type) {
                        return !!type;
                    }).length;

            live.innerHTML = [
                stat(
                    "To Save",
                    pickedCount(),
                    pickedCount() === state.rows.length
                        ? "is-green" : "is-blue"),
                stat("Edited Cells", edits,
                    edits > 0 ? "is-amber" : "is-slate"),
                stat("Type Overrides", overrides,
                    overrides > 0 ? "is-amber" : "is-slate")
            ].join("");
        }
    }

    $("previewSub").innerText = state.readOnly
        ? "Saved table: " + state.loadedFrom
        : state.fileName;

    renderSelectionBar();

}


function stat(label, value, cls) {
    return '<div class="istat ' + cls + '">'
        + '<span class="istat-value ' + cls
        + '" title="' + escapeHtml(value) + '">'
        + escapeHtml(value)
        + '</span><span class="istat-label">'
        + escapeHtml(label)
        + "</span></div>";
}


// The count that sits under the grid and repeats on the
// sticky bar. The toolbar buttons are rebuilt rather
// than hidden so the two never disagree.
function renderSelectionBar() {

    var bar = $("selectionBar");

    if (!bar) {
        return;
    }

    if (state.readOnly || state.rows.length === 0) {
        bar.hidden = true;
        return;
    }

    bar.hidden = false;

    var picked = pickedCount();

    $("selectionInfo").innerText =
        picked + " of " + state.rows.length
            + " row(s) selected";

    $("selAll").hidden = picked === state.rows.length;
    $("selNone").hidden = picked === 0;

}


// ==========================================
// COLUMN TYPES
//
// The type of each column is guessed from the values
// underneath it, which is right most of the time and
// wrong in the usual ways: a column of dates written as
// 15/01/2026 looks like text, a number with a stray
// currency mark stops being a number. This panel lets
// that guess be corrected before the table is built,
// because a column created as the wrong type stays that
// way once the rows are in.
//
// The server keeps its own list of the types it will
// accept, so these entries are a convenience rather
// than the rule.
// ==========================================

var TYPE_CHOICES = [
    "@auto",
    "nvarchar(max)", "nvarchar(255)", "nvarchar(100)",
    "nvarchar(50)", "varchar(255)",
    "int", "bigint", "smallint", "tinyint", "bit",
    "decimal(18,2)", "decimal(10,0)", "money",
    "float", "real",
    "date", "datetime2", "time"
];


// "@auto" is not a real type, it is the choice that puts
// the column back on whatever the file says.
var TYPE_LABELS = {
    "@auto": "From file"
};


function renderTypePanel() {

    var wrap = $("typeMap");
    var body = $("typeMapRows");

    if (!wrap || !body) {
        return;
    }

    // The panel only has anything to say once a file has
    // been read, and a saved table is read-only anyway.
    wrap.hidden = state.readOnly
        || state.headers.length === 0;

    if (wrap.hidden) {
        return;
    }

    var anyOverride = state.typeOverrides.some(
        function (type) { return !!type; });

    $("typeMapToggle").innerHTML =
        (anyOverride ? "&#9662; " : "&#9656; ")
            + "Column types"
            + (anyOverride
                ? " (changed)"
                : "");

    body.innerHTML = state.headers.map(
        function (header, index) {

            var current = columnType(index);

            var chosen = state.typeOverrides[index]
                || "@auto";

            var options = TYPE_CHOICES.map(
                function (type) {
                    return '<option value="' + type
                        + '"' + (type === chosen
                            ? " selected" : "")
                        + ">" + (TYPE_LABELS[type] || type)
                        + "</option>";
                }).join("");

            return "<tr><td class=\"ipm-name\">"
                + escapeHtml(header)
                + "</td><td>"
                + '<select class="ipm-select"'
                + ' data-col="' + index + '">'
                + options
                + "</select></td>"
                + "<td class=\"ipm-sample\">"
                + escapeHtml(sampleFor(index))
                + "</td></tr>";

        }).join("");

    body.querySelectorAll("select.ipm-select")
        .forEach(function (select) {

            select.addEventListener("change",
                function () {
                    var index = parseInt(
                        select.dataset.col, 10);

                    // An empty choice means "go back to
                    // what the file says".
                    state.typeOverrides[index] =
                        select.value === "@auto"
                            ? ""
                            : select.value;

                    state.types[index] = "";

                    render();

                    if (!state.readOnly) {
                        renderTypePanel();
                    }
                });

        });

}


function sampleFor(index) {

    for (var r = 0;
        r < Math.min(state.rows.length, 3);
        r++) {

        var value = String(
            state.rows[r][index] || "").trim();

        if (value) {
            return value.length > 24
                ? value.slice(0, 24) + "..."
                : value;
        }
    }

    return "(empty)";

}


// Puts every column back on the type read from the file
// and drops the overrides.
function resetTypes() {

    for (var c = 0;
        c < state.typeOverrides.length;
        c++) {
        state.typeOverrides[c] = "";
        state.types[c] = "";
    }

    render();

    renderTypePanel();

    showToast(
        "Column types are back to the values "
            + "found in the file.", false);

}


// ==========================================
// FAILED ROWS
// ==========================================

// Modals in this app are shown by setting display on the
// wrapper, the same as the add / edit dialogs on the
// other pages. site.css gives .modal display:none, so a
// hidden attribute here would leave it shut whatever
// the script said.
function errorModalOpen() {

    var modal = $("errorModal");

    return modal
        && modal.style.display === "flex";

}


function showFailures(failures) {

    var modal = $("errorModal");

    if (!modal) {
        return;
    }

    $("errorBody").innerHTML = failures.map(
        function (line) {
            return "<li>" + escapeHtml(line)
                + "</li>";
        }).join("");

    modal.style.display = "flex";

    $("btnErrorCsv").focus();

}


// Kept on the object so the download button has them
// even after the grid has been repainted.
var lastFailures = [];


function downloadFailures() {

    if (!lastFailures.length) {
        return;
    }

    var lines = ["Row,Reason"];

    lastFailures.forEach(function (line) {
        lines.push(
            '"' + line.replace(/"/g, '""') + '"');
    });

    var blob = new Blob(
        [lines.join("\r\n")],
        { type: "text/csv;charset=utf-8" });

    var link = document.createElement("a");

    link.href =
        URL.createObjectURL(blob);

    link.download =
        "import-errors-"
            + new Date()
                .toISOString()
                .slice(0, 10)
            + ".csv";

    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);

    URL.revokeObjectURL(link.href);

}


function closeErrorModal() {

    var modal = $("errorModal");

    if (modal) {
        modal.style.display = "none";
    }

}


function renderBody() {

    computeView();

    var totalPages = Math.max(1,
        Math.ceil(grid.view.length / grid.pageSize));

    if (grid.page > totalPages) {
        grid.page = totalPages;
    }

    var start = (grid.page - 1) * grid.pageSize;
    var end = Math.min(
        start + grid.pageSize, grid.view.length);

    var selectable = !state.readOnly
        && state.rows.length > 0;

    var html = "";

    for (var i = start; i < end; i++) {

        var rowIndex = grid.view[i];

        // The line number in the file, where the header
        // sits on line 1.
        var line = String(rowIndex + 2);

        html += "<tr data-row=\""
            + rowIndex + "\">"
            + "<td class=\"rs-sr\">" + line + "</td>";

        if (selectable) {
            html += "<td class=\"col-tick\">"
                + "<input type=\"checkbox\""
                + " class=\"row-check\""
                + " data-row=\"" + rowIndex + "\""
                + (state.rowSel
                    && state.rowSel[rowIndex]
                    ? " checked" : "")
                + " /></td>";
        }

        var row = state.rows[rowIndex];

        for (var c = 0; c < state.headers.length; c++) {

            var key = rowIndex + ":" + c;

            var cls = state.edited[key]
                ? " class=\"cell-edited\""
                : "";

            html += "<td" + cls + " data-row=\""
                + rowIndex + "\" data-col=\"" + c
                + "\">" + escapeHtml(row[c]) + "</td>";
        }

        html += "</tr>";

    }

    if (grid.view.length === 0) {
        html = '<tr class="no-data-row"><td colspan="'
            + (state.headers.length + 2)
            + '" class="empty-state">'
            + (state.rows.length === 0
                ? "This table has no rows."
                : "No records match your search.")
            + "</td></tr>";
    }

    $("dataBody").innerHTML = html;

    $("dataBody").querySelectorAll(
            ".row-check")
        .forEach(function (box) {
            box.addEventListener("change",
                function () {
                    var row = parseInt(
                        this.dataset.row, 10);

                    if (state.rowSel) {
                        state.rowSel[row] =
                            this.checked ? 1 : 0;
                    }

                    renderStats();
                    refreshSaveBar();
                    renderSelectionBar();
                });
        });

    if (selectable) {
        bindCellEditing();
    }

    $("tableInfo").innerText = grid.view.length === 0
        ? "Showing 0 to 0 of 0 rows"
        : "Showing " + (start + 1) + " to " + end
            + " of " + grid.view.length + " rows";

    renderPagination(totalPages);

}


// A cell turns into a text box on a double click, so a
// wrong value can be corrected on screen instead of
// needing the file to be fixed and uploaded again.
//
// The input is added on the spot and taken away again on
// Enter or Escape, so nothing is left in the markup for
// the next paint of the grid to trip over.
function bindCellEditing() {

    $("dataBody").querySelectorAll("td[data-col]")
        .forEach(function (cell) {

            cell.addEventListener("dblclick",
                function () {

                    if (cell.querySelector("input")) {
                        return;
                    }

                    var row = parseInt(
                        cell.dataset.row, 10);
                    var col = parseInt(
                        cell.dataset.col, 10);

                    var original =
                        state.rows[row][col];

                    var input =
                        document.createElement("input");

                    input.type = "text";
                    input.className = "cell-edit";
                    input.value = original;

                    cell.textContent = "";
                    cell.appendChild(input);

                    input.focus();
                    input.select();

                    var finished = false;

                    function commit() {

                        if (finished) {
                            return;
                        }

                        finished = true;

                        var next = input.value;

                        if (next !== original) {

                            state.rows[row][col] = next;

                            // The search copy and the
                            // cached type both went out
                            // of date with the edit.
                            state.edited[row + ":" + col]
                                = true;

                            state.types[col] = "";
                            state.typeOverrides[col] = "";

                            state.searchText[row] =
                                state.rows[row]
                                    .join(" ")
                                    .toLowerCase();

                            render();
                            return;
                        }

                        cell.textContent =
                            escapeHtml(original);
                    }

                    input.addEventListener(
                        "blur", commit);

                    input.addEventListener("keydown",
                        function (event) {

                            if (event.key
                                === "Enter") {
                                event.preventDefault();
                                input.blur();
                            } else if (
                                event.key === "Escape") {

                                event.preventDefault();

                                finished = true;

                                cell.textContent =
                                    escapeHtml(
                                        original);
                            }

                        });

                });

        });

}


function renderPagination(totalPages) {

    var html = grid.page === 1
        ? '<button class="page-btn" disabled>‹</button>'
        : '<button class="page-btn" '
            + 'data-page="' + (grid.page - 1)
            + '">‹</button>';

    // A short window around the current page so the
    // bar never grows past a handful of buttons.
    var from = Math.max(1, grid.page - 2);
    var to = Math.min(totalPages, from + 4);
    from = Math.max(1, to - 4);

    if (from > 1) {
        html += '<button class="page-btn" data-page="1">1</button>';

        if (from > 2) {
            html += '<span class="page-gap">…</span>';
        }
    }

    for (var p = from; p <= to; p++) {
        html += p === grid.page
            ? '<button class="page-btn active">'
                + p + "</button>"
            : '<button class="page-btn" data-page="'
                + p + '">' + p + "</button>";
    }

    if (to < totalPages) {
        if (to < totalPages - 1) {
            html += '<span class="page-gap">…</span>';
        }

        html += '<button class="page-btn" data-page="'
            + totalPages + '">' + totalPages + "</button>";
    }

    html += grid.page === totalPages
        ? '<button class="page-btn" disabled>›</button>'
        : '<button class="page-btn" data-page="'
            + (grid.page + 1) + '">›</button>';

    $("pagination").innerHTML = html;

    $("pagination")
        .querySelectorAll("button[data-page]")
        .forEach(function (button) {
            button.addEventListener("click", function () {
                grid.page = parseInt(
                    button.dataset.page, 10);
                renderBody();
            });
        });

}


function render() {

    if (state.headers.length === 0) {
        return;
    }

    renderHead();
    renderStats();
    renderBody();
    renderTypePanel();

}


// The three steps at the top are the only place the page
// says where a reader has got to. Step 1 is done once a
// file is open, step 2 once something has been changed or
// ticked, and step 3 is current from the moment there is
// a name to save to.
function markStep(step) {

    document.querySelectorAll(
            "#ipSteps .ip-step")
        .forEach(function (item) {

            var index = parseInt(
                item.dataset.step, 10);

            item.classList.toggle("is-done",
                index < step);

            item.classList.toggle("is-current",
                index === step);

        });

}


// Called on every change, so it works out the step rather
// than each caller saying which one it reached.
function syncSteps() {

    if (state.headers.length === 0) {
        markStep(1);
        return;
    }

    if (state.readOnly) {
        // Nothing left to check or choose, so the last
        // step reads as the one in hand.
        markStep(3);
        return;
    }

    // A table that came back for updating is already on the
    // server, so the file step is behind us and the work
    // in hand is the review. It jumps straight to the last
    // step like the read-only view does.
    if (state.mode === "update") {
        markStep(3);
        return;
    }

    var touched = Object.keys(state.edited).length > 0
        || state.typeOverrides.some(function (type) {
            return !!type;
        })
        || (state.rowSel
            && pickedCount() < state.rows.length);

    markStep(touched ? 2 : 1);

}


// ==========================================
// SAVE BAR
// ==========================================

function refreshSaveBar() {

    var input = $("tableName");
    var hint = $("tableHint");
    var save = $("btnSave");

    var name = cleanName(input.value);

    var picked = pickedCount();

    if (!state.readOnly) {

        var options = Array.prototype.slice.call(
            $("existingTables").options);

        var exists = options.some(function (option) {
            return option.value.toLowerCase()
                === name.toLowerCase();
        });

        var replace = state.mode === "replace";

        var updating = state.mode === "update";

        if (updating) {

            hint.innerText = "Changes will be written "
                + "back onto the selected rows of \""
                + name + "\". Rows that are not "
                + "ticked stay as they are.";
            hint.className = "isb-hint is-info";

        } else if (name === "") {
            hint.innerText = "";
            hint.className = "isb-hint";
        } else if (name !== input.value.trim()) {
            hint.innerText = "Will be saved as: " + name;
            hint.className = "isb-hint is-warn";
        } else if (replace) {
            hint.innerText = "Every row already in \""
                + name + "\" will be deleted, then the "
                + picked + " selected row(s) written.";
            hint.className = "isb-hint is-danger";
        } else if (exists) {
            hint.innerText = "Table \"" + name
                + "\" already exists — rows will be "
                + "added to it.";
            hint.className = "isb-hint is-info";
        } else {
            hint.innerText = "A new table called \""
                + name + "\" will be created.";
            hint.className = "isb-hint is-new";
        }

        if (updating) {

            // An update writes onto rows that are already
            // there, so the name is not something to be
            // edited here. Changing it would either land
            // the changes on a different table or, worse,
            // create a new one and leave this one alone.
            input.readOnly = true;

            $("modeUpdateOption").hidden = false;

            var updateRadio = document.querySelector(
                "input[name='importMode'][value='update']");

            if (updateRadio) {
                updateRadio.checked = true;
            }

        } else {

            input.readOnly = false;

            $("modeUpdateOption").hidden = true;

        }

        if (picked === 0 && state.rows.length > 0) {
            hint.innerText =
                "Every row has been unticked. Tick at "
                    + "least one row to save.";
            hint.className = "isb-hint is-danger";
        }

        save.disabled = state.rows.length === 0
            || name === ""
            || picked === 0;

    } else {

        hint.innerText = "Read-only view. Press Clear "
            + "to load another file.";
        hint.className = "isb-hint is-info";

        save.disabled = true;

    }

    // The same two numbers are repeated on the bar that
    // stays at the bottom of the window.
    var ready = $("stickyInfo");

    if (ready) {

        // Whether the bar is wanted at all is decided by
        // syncStickyBar, which knows whether the real save
        // button is currently on screen. This block only
        // fills in the numbers.
        if (!state.readOnly) {
            $("stickyRows").innerText =
                picked.toLocaleString()
                    + " of "
                    + state.rows.length.toLocaleString()
                    + " rows";

            $("stickyTable").innerText =
                name === "" ? "no table name" : name;

            $("stickyMode").innerText =
                state.mode === "replace"
                    ? "Replace"
                    : state.mode === "update"
                        ? "Update" : "Append";

            $("stickyMode").className =
                "ipm-tag " + (state.mode === "replace"
                    ? "is-replace"
                    : state.mode === "update"
                        ? "is-update" : "is-append");
        }
    }

    // Two save buttons, one state, so the sticky one is
    // driven off the real one rather than worked out a
    // second time.
    var stickySave = $("stickySave");

    if (stickySave) {
        stickySave.disabled = !!save.disabled;
    }

    syncSteps();
    syncStickyBar();

}


// ==========================================
// STICKY SAVE BAR
// ==========================================

// The page carries two save buttons: the real one on the
// save card, and a copy pinned to the bottom of the window
// for when the grid is being read and the card has
// scrolled away. Both being on screen at once made one
// action look like it was offered twice, so the copy is
// only revealed once the real button is out of sight.
//
// It is still the same action, still wired to the same
// handler, and the disabled state is kept in step by
// refreshSaveBar, so nothing about the save itself changed.

// Set once, on first use. Recreating the observer on every
// refresh would leave the old ones attached to a detached
// node.
var saveCardWatcher = null;

function syncStickyBar() {

    var sticky = $("stickyInfo");
    var card = $("saveSection");

    if (!sticky || !card) {
        return;
    }

    if (state.readOnly) {

        // Nothing to save, so the bar is not wanted at all.
        sticky.hidden = true;

        if (saveCardWatcher) {
            saveCardWatcher.disconnect();
            saveCardWatcher = null;
        }

        return;
    }

    // A card that is on screen means the real button can be
    // pressed, so the pinned copy stays out of the way. The
    // observer only ever reports a change in this direction.
    function reveal() {
        sticky.hidden = false;
    }

    function conceal() {
        sticky.hidden = true;
    }

    if (typeof IntersectionObserver === "undefined") {

        // No observer support. Showing the copy is the safe
        // direction, because a hidden save would look like a
        // page that cannot save.
        reveal();
        return;
    }

    if (!saveCardWatcher) {

        saveCardWatcher = new IntersectionObserver(
            function (entries) {
                if (entries[0].isIntersecting) {
                    conceal();
                } else {
                    reveal();
                }
            },
            // rootMargin of 0 means "any part off screen". The
            // small positive bottom margin lets the copy come up
            // just as the real button leaves, rather than after a
            // visible gap where neither is on screen.
            { threshold: 0, rootMargin: "-64px 0px 0px 0px" }
        );

        saveCardWatcher.observe(card);
    }

    // The position is read here rather than left to the first
    // callback. The observer reports asynchronously, so waiting
    // for it shows the copy alongside the real button for a
    // frame, and leaves it showing for good on any browser that
    // never reports at all. Reading the box makes this call
    // correct on its own, whichever way it is reached.
    var box = card.getBoundingClientRect();

    var onScreen = box.bottom > 0
        && box.top < (window.innerHeight || 0);

    if (onScreen) {
        conceal();
    } else {
        reveal();
    }

}


// ==========================================
// CLEAR
// ==========================================

function clearAll() {

    state.headers = [];
    state.rows = [];
    state.types = [];
    state.searchText = [];
    state.fileName = "";
    state.readOnly = false;
    state.loadedFrom = "";
    state.typeOverrides = [];
    state.edited = {};
    state.rowSel = null;
    state.rowIds = [];
    state.mode = "append";

    resetGrid();

    var file = $("importFile");

    if (file) {
        file.value = "";
    }

    var picker = $("sheetPicker");
    if (picker) {
        picker.innerHTML = "";
        picker.hidden = true;
    }

    $("dzIdle").hidden = false;
    $("dzLoaded").hidden = true;

    $("dzFileName").innerText = "";
    $("dzFileInfo").innerText = "";

    $("tableName").value = "";
    $("tableHint").innerText = "";

    $("previewSection").hidden = true;
    $("saveSection").hidden = true;
    $("importBanner").hidden = true;

    var stats = $("importStatsLive");
    if (stats) {
        stats.hidden = true;
    }

    var sticky = $("stickyInfo");
    if (sticky) {
        sticky.hidden = true;
    }

    // The watcher is bound to the save card, which is about
    // to go away with the file. Leaving it attached would
    // keep firing against a hidden section on the next load.
    if (saveCardWatcher) {
        saveCardWatcher.disconnect();
        saveCardWatcher = null;
    }

    var bar = $("selectionBar");
    if (bar) {
        bar.hidden = true;
    }

    // The mode radio is the source of truth for the user,
    // so it is put back to the safe default as well.
    var append = document.querySelector(
        "input[name=\"importMode\"][value=\"append\"]");

    if (append) {
        append.checked = true;
    }

    lastFailures = [];

    $("saveLoading").classList.remove("visible");

    markStep(1);

    window.scrollTo({ top: 0, behavior: "smooth" });

}


// ==========================================
// SAVE
// ==========================================

// A replace throws away whatever the table already
// holds, and nothing can undo it, so it is always asked
// about first. The answer is remembered for the rest of
// the session so the same table does not have to be
// confirmed twice in a row.
var replaceConfirmed = {};


function confirmReplace(tableName, count) {

    var key = tableName.toLowerCase();

    if (replaceConfirmed[key]) {
        return true;
    }

    var answer = window.confirm(
        "Replace the contents of \"" + tableName + "\"?\n\n"
            + "Every row already in that table will be "
            + "deleted, then the " + count
            + " selected row(s) written.\n\n"
            + "This cannot be undone.");

    if (answer) {
        replaceConfirmed[key] = true;
    }

    return answer;

}


function saveToDatabase() {

    if (state.readOnly || state.rows.length === 0) {
        return;
    }

    var tableName = cleanName($("tableName").value);

    if (!tableName) {
        showToast("Please enter a valid table name.",
            true);
        return;
    }

    // The whole set of rows goes to the server along with
    // the positions that are ticked. Sending only the
    // ticked rows would leave the positions pointing at
    // the wrong entries, because they are counted from
    // the start of the full list. The server picks them
    // out, which also keeps them in file order.
    var rowIndexes = [];

    for (var r = 0; r < state.rows.length; r++) {
        if (state.rowSel && state.rowSel[r]) {
            rowIndexes.push(r);
        }
    }

    if (rowIndexes.length === 0) {
        showToast("No rows are selected.", true);
        return;
    }

    // A replace empties the table, so it is worth a second
    // look before anything is written.
    if (state.mode === "replace"
        && !confirmReplace(tableName, rowIndexes.length)) {
        return;
    }

    var button = $("btnSave");

    setBusy(button, true, "Saving...");

    busyOverlay("saveLoading", true);

    fetch("/Import/Save", {
        method: "POST",
        headers: {
            "Content-Type": "application/json",
            "RequestVerificationToken": TOKEN,
            "X-Requested-With": "XMLHttpRequest"
        },
        body: JSON.stringify({
            tableName: tableName,
            fileName: state.fileName,
            columns: state.headers,
            rows: state.rows,
            columnTypes: state.typeOverrides,
            mode: state.mode,
            rowIndexes: rowIndexes,
            rowIds: state.rowIds
        })
    })
        .then(function (response) {
            return response.json();
        })
        .then(function (data) {

            busyOverlay("saveLoading", false);

            setBusy(button, false);

            if (!data || data.totalRows === undefined) {
                showToast(
                    (data && data.message)
                        || "The file could not be saved.",
                    true);
                return;
            }

            lastFailures =
                data.failures || [];

            if (data.failedRows > 0) {
                showToast(
                    state.mode === "update"
                        ? "Updated with errors. Changed: "
                            + data.importedRows
                            + ", Failed: " + data.failedRows
                            + ". Opening the list."
                        : "Saved with errors. Saved: "
                            + data.importedRows
                            + ", Failed: " + data.failedRows
                            + ". Opening the list.",
                    true);
            } else {
                showToast(
                    state.mode === "update"
                        ? "Updated table \"" + data.table
                            + "\" — " + data.importedRows
                            + " row(s) changed."
                        : "Saved to table \"" + data.table
                            + "\" — " + data.importedRows
                            + " row(s) written.", false);
            }

            refreshHistory();

            // The rows now live in the database, so the
            // grid goes back to what was just written. An
            // update reopens still editable, because the
            // point of it was to change what is there and
            // a second pass may be wanted.
            openTable(
                data.table,
                state.mode === "update");

            if (lastFailures.length) {
                showFailures(lastFailures);
            }

        })
        .catch(function () {

            busyOverlay("saveLoading", false);

            setBusy(button, false);

            showToast("The file could not be saved. "
                + "Please try again.", true);

        });

}


// ==========================================
// OPEN A SAVED TABLE
// ==========================================

// "editable" is what the Update button asks for. The
// rows come back exactly as the View button gets them,
// but instead of being pinned shut they can be ticked,
// typed into and written straight back onto the rows
// they came from.
function openTable(tableName, editable) {

    busyOverlay("saveLoading", true);

    fetch("/Import/TableData?table="
        + encodeURIComponent(tableName), {
            headers: { "X-Requested-With": "XMLHttpRequest" }
        })
        .then(function (response) {
            return response.json();
        })
        .then(function (data) {

            busyOverlay("saveLoading", false);

            if (!data || !data.success) {
                showToast(
                    (data && data.message)
                        || "The table could not be read.",
                    true);
                return;
            }

            state.headers = (data.columns || [])
                .map(function (column) {
                    return column.header;
                });

            state.types = (data.columns || [])
                .map(function (column) {
                    return column.sqlType;
                });

            state.rows = data.rows || [];
            state.fileName = tableName;
            state.readOnly = !editable;
            state.loadedFrom = data.table;

            // The hidden row ids travel with the rows. They
            // are kept even when the table is opened just to
            // look at it, so switching to Update later does
            // not have to fetch anything again.
            state.rowIds = data.rowIds || [];

            // Nothing here can be changed or saved when the
            // table is only being read, so the editing state
            // is left empty. An update, on the other hand,
            // starts in update mode: it writes back onto the
            // rows that are already there rather than
            // adding new ones or clearing the table.
            state.typeOverrides = [];
            state.edited = {};

            // An update has to start with every row ticked,
            // the same as a fresh file does. Leaving this
            // null is what a read-only view wants, but the
            // grid reads it while drawing the tick column
            // and the save bar counts it, so a table opened
            // for updating has to have one.
            state.rowSel = editable
                ? new Uint8Array(state.rows.length).fill(1)
                : null;

            state.mode = editable ? "update" : "append";

            indexRows();

            resetGrid();

            $("previewSub").innerText =
                "Saved table: " + data.table;

            showBanner(
                editable
                    ? "You are updating the saved table \""
                        + data.table + "\""
                        + (data.truncated
                            ? " — only the first "
                                + state.rows.length
                                + " rows can be changed."
                            : ".")
                    : "You are viewing the saved table \""
                        + data.table + "\""
                        + (data.truncated
                            ? " — only the first "
                                + state.rows.length
                                + " rows are shown."
                            : "."),
                !editable);

            $("dzIdle").hidden = true;
            $("dzLoaded").hidden = false;

            setIcon($("dzFileIcon"), "i-database");

            $("dzFileName").innerText = data.table;

            $("dzFileInfo").innerText =
                state.headers.length + " columns"
                + "  •  " + state.rows.length + " rows"
                + "  •  "
                + (editable ? "editable" : "read-only");

            $("sheetPicker").hidden = true;

            $("tableName").value = data.table;

            refreshSaveBar();
            render();

            $("previewSection").hidden = false;
            $("saveSection").hidden = false;

            $("previewSection").scrollIntoView({
                behavior: "smooth",
                block: "start"
            });

        })
        .catch(function () {

            busyOverlay("saveLoading", false);

            showToast("The table could not be read. "
                + "Please try again.", true);

        });

}


// ==========================================
// HISTORY
// ==========================================

// The search box and the range picker on the history
// table. Returns the query string for the request, or
// an empty string when nothing is being filtered.
function historyQuery() {

    var parts = [];

    var term = ($("historySearch") || {}).value;

    if (term && term.trim()) {
        parts.push("search="
            + encodeURIComponent(term.trim()));
    }

    var days = ($("historyRange") || {}).value;

    if (days && days !== "all") {
        var from = new Date();

        from.setDate(
            from.getDate() - parseInt(days, 10));

        // A plain date, so the server reads it the same
        // way whatever the machine's locale is.
        parts.push("from="
            + encodeURIComponent(
                from.toISOString()
                    .slice(0, 10)));
    }

    return parts.join("&");

}


function refreshHistory() {

    var url = "/Import/History";

    var query = historyQuery();

    if (query) {
        url += "?" + query;
    }

    fetch(url, {
        headers: { "X-Requested-With": "XMLHttpRequest" }
    })
        .then(function (response) {
            return response.json();
        })
        .then(function (list) {

            if (!Array.isArray(list)) {
                return;
            }

            var body = $("historyBody");

            if (list.length === 0) {
                body.innerHTML =
                    '<tr class="no-data-row"><td colspan="10"'
                    + ' class="empty-state">'
                    + (historyQuery()
                        ? "No imports match this filter."
                        : "No files have been saved yet.")
                    + "</td></tr>";
                return;
            }

            body.innerHTML = list.map(function (h) {

                var failed = h.failedRows > 0
                    ? '<span class="badge-cancelled">'
                        + h.failedRows + "</span>"
                    : '<span class="badge-default">0</span>';

                var mode = h.mode === "replace"
                    ? '<span class="ipm-tag is-replace">'
                        + "Replace</span>"
                    : h.mode === "update"
                        ? '<span class="ipm-tag is-update">'
                            + "Update</span>"
                        : '<span class="ipm-tag is-append">'
                            + "Append</span>";

                var when = h.importedOn
                    ? new Date(h.importedOn).toLocaleString(
                        undefined, {
                            day: "2-digit",
                            month: "short",
                            year: "numeric",
                            hour: "2-digit",
                            minute: "2-digit"
                        })
                    : "";

                return "<tr>"
                    + '<td class="rs-sr">' + h.id + "</td>"
                    + '<td><span class="hf-table">'
                        + escapeHtml(h.tableName)
                        + "</span></td>"
                    + '<td class="hf-file">'
                        + escapeHtml(h.fileName) + "</td>"
                    + '<td class="col-num">' + h.totalRows
                        + "</td>"
                    + '<td class="col-num">'
                        + '<span class="badge-active">'
                        + h.importedRows + "</span></td>"
                    + '<td class="col-num">' + failed + "</td>"
                    + '<td class="col-mode">' + mode + "</td>"
                    + "<td>" + escapeHtml(h.userName) + "</td>"
                    + '<td class="col-nowrap">' + when + "</td>"
                    + '<td class="col-act">'
                        + '<div class="col-act-row">'
                        + '<button type="button"'
                        + ' class="action-btn view-btn"'
                        + ' data-table="'
                        + escapeHtml(h.tableName)
                        + '" title="View '
                        + escapeHtml(h.tableName)
                        + '" aria-label="View '
                        + escapeHtml(h.tableName)
                        + '">'
                        + '<svg class="ip-ico" viewBox="0 0 24 24"'
                        + ' aria-hidden="true">'
                        + '<use href="#i-eye"></use></svg>'
                        + "</button>"
                        + '<button type="button"'
                        + ' class="action-btn update-btn"'
                        + ' data-table="'
                        + escapeHtml(h.tableName)
                        + '" title="Update '
                        + escapeHtml(h.tableName)
                        + '" aria-label="Update '
                        + escapeHtml(h.tableName)
                        + '">'
                        + '<svg class="ip-ico" viewBox="0 0 24 24"'
                        + ' aria-hidden="true">'
                        + '<use href="#i-edit"></use></svg>'
                        + "</button>"
                        + "</div></td>"
                    + "</tr>";

            }).join("");

        })
        .catch(function () {
            // the list simply keeps the old rows
        });

}


// ==========================================
// INIT
// ==========================================

// This used to be wrapped in a DOMContentLoaded listener,
// which turned out to be the reason a first upload did
// nothing at all.
//
// xlsx.full.min.js is a 932 KB deferred script, and
// deferred scripts have to finish downloading before
// DOMContentLoaded fires. Until that finished, every
// listener below was missing, so the page was completely
// inert. The dropzone is a <label for="importFile">, so it
// still opened the file picker with no JavaScript involved
// - the user could pick a file and the change event landed
// on a page with nothing listening to it. A silent no-op,
// which read as a broken upload. A refresh fixed it only
// because the reader was cached by then and DOMContentLoaded
// arrived quickly.
//
// import.js is a classic script at the end of <body>, so
// everything it needs is already parsed by the time it
// runs. Wiring up straight away is both correct and
// immediate; the reader is no longer on the critical path
// for the page becoming usable.

function initImportPage() {

        var file = $("importFile");
        var zone = $("dropzone");

        file.addEventListener("change", function () {
            loadFile(file.files[0]);
        });

        // The idle half of the box is a <label> that
        // points at the input, so a click there opens
        // the picker on its own. Only the rest of the
        // box, and the remove / sheet controls inside
        // the loaded half, need to be handled here.
        zone.addEventListener("click", function (event) {

            var origin = event.target;

            if (origin && origin.closest
                && origin.closest(
                    "#dzIdle, #btnRemove, #sheetPicker")) {
                return;
            }

            file.click();

        });

        zone.addEventListener("dragover", function (event) {
            event.preventDefault();
            zone.classList.add("is-over");
        });

        zone.addEventListener("dragleave", function () {
            zone.classList.remove("is-over");
        });

        zone.addEventListener("drop", function (event) {

            event.preventDefault();

            zone.classList.remove("is-over");

            var dropped = event.dataTransfer
                ? event.dataTransfer.files[0]
                : null;

            if (dropped) {
                loadFile(dropped);
            }

        });

$("btnRemove").addEventListener("click", clearAll);

        $("btnClear").addEventListener("click", function () {

            // Rows that were ticked off or typed into are
            // work the user cannot get back, so it is
            // worth one question first.
            var edits = Object.keys(
                state.edited).length;

            var unticked = state.rowSel
                && pickedCount() < state.rows.length;

            if ((edits > 0 || unticked)
                && !window.confirm(
                    "Clear the file? Any rows you have "
                        + "edited or unticked will be "
                        + "lost.")) {
                return;
            }

            clearAll();

        });

        $("btnSave").addEventListener("click", saveToDatabase);

        $("btnCancelLoad").addEventListener(
            "click", cancelLoad);

        $("btnResetTypes").addEventListener(
            "click", resetTypes);

        $("selAll").addEventListener(
            "click", function () {
                setAllRows(true);
            });

        $("selNone").addEventListener(
            "click", function () {
                setAllRows(false);
            });

        $("stickySave").addEventListener(
            "click", saveToDatabase);

        // The mapping panel folds away so a wide sheet
        // still has room to breathe.
        $("typeMapToggle").addEventListener(
            "click", function () {
                var body = $("typeMapBody");

                var open =
                    body.hidden === false;

                body.hidden = open;

                this.setAttribute(
                    "aria-expanded",
                    open ? "false" : "true");

            });

        document.querySelectorAll(
                "input[name=\"importMode\"]")
            .forEach(function (radio) {

                radio.addEventListener("change",
                    function () {
                        if (!this.checked) {
                            return;
                        }

                        // A table opened for updating keeps
                        // that mode. Dropping back to append
                        // here would add copies of rows that
                        // are already stored.
                        if (state.mode === "update") {

                            this.checked = true;
                            return;
                        }

                        state.mode = this.value;

                        refreshSaveBar();
                        renderStats();
                    });

            });

        $("tableName").addEventListener(
            "input", refreshSaveBar);

        $("tableSearch").addEventListener("input", function () {
            grid.page = 1;
            renderBody();
        });

        $("recordsPerPage").addEventListener(
            "change",
            function () {
                grid.pageSize =
                    parseInt(this.value, 10) || 25;
                grid.page = 1;
                renderBody();
            });

        $("sheetPicker").addEventListener("change", function () {

            if (!state.fileName || state.readOnly) {
                return;
            }

            // Re-read the same file on the picked sheet.
            if (file.files[0]) {
                loadFile(file.files[0]);
            }

        });

        // The history list is short enough that typing is
        // cheap, but a request per keystroke still adds
        // up, so the list settles first.
        var historyTimer = null;

        function refreshHistorySoon() {

            if (historyTimer) {
                clearTimeout(historyTimer);
            }

            historyTimer = setTimeout(
                refreshHistory, 250);

        }

        $("historySearch").addEventListener(
            "input", refreshHistorySoon);

        $("historyRange").addEventListener(
            "change", refreshHistory);

        $("btnErrorClose").addEventListener(
            "click", closeErrorModal);

        $("btnErrorCsv").addEventListener(
            "click", downloadFailures);

        document.addEventListener("click",
            function (event) {

                var origin = event.target;

                if (!origin
                    || !origin.closest) {
                    return;
                }

                var button = origin.closest(".view-btn");

                if (button) {
                    openTable(button.dataset.table, false);
                    return;
                }

                var update = origin.closest(".update-btn");

                if (update) {

                    // Confirming first, because Update does
                    // not start from a fresh copy. The rows
                    // that come back are the rows that will
                    // be written over, and anything ticked
                    // is what changes.
                    if (confirm(
                        "Open \"" + update.dataset.table
                            + "\" for updating?\n\n"
                            + "The rows are loaded into the "
                            + "grid so you can change them. "
                            + "Nothing is written to the "
                            + "table until you press Save.")) {

                        openTable(
                            update.dataset.table, true);
                    }

                    return;
                }

                // Clicking the dimmed area around the
                // dialog closes it, the same as the
                // button does.
                if (origin.id === "errorModal") {
                    closeErrorModal();
                }

            });

        document.addEventListener("keydown",
            function (event) {

                if ((event.ctrlKey || event.metaKey)
                    && event.key.toLowerCase() === "s") {

                    event.preventDefault();

                    if (!$("saveSection").hidden
                        && !$("btnSave").disabled) {
                        saveToDatabase();
                    }

                    return;
                }

                if (event.key !== "Escape") {
                    return;
                }

                // Escape backs out of one thing at a time:
                // an open cell is already handled by its own
                // handler, so the next thing is the failure
                // list, then a running load, and only then
                // the file itself.
                if (errorModalOpen()) {
                    event.preventDefault();
                    closeErrorModal();
                    return;
                }

if (loading.active) {
                        event.preventDefault();
                        cancelLoad();
                        return;
                    }

                    if (state.headers.length > 0) {
                        event.preventDefault();
                        $("btnClear").click();
                    }

                });

        // First paint.
        //
        // The markup ships in its empty state on purpose, so
        // the grid, the footer counts, the pagination, the
        // stats and the table hint all arrive blank. Left
        // alone, the page sat half-built until the user
        // happened to click something that happened to call a
        // render. Everything below is safe on an empty state
        // and only fills in what the server already knows.
        syncSteps();
        renderStats();
        refreshSaveBar();
        renderBody();

        // The saved list is rendered server side, so this is
        // not needed to see anything. It is here so the page
        // is live from the first paint and the list does not
        // depend on somebody saving a file to look current.
        refreshHistory();

}

// Normally the document is already parsed when this runs, so
// init goes ahead straight away. The readyState check keeps
// the page working if this file is ever loaded with defer or
// moved into <head>, where the elements it needs would not
// exist yet.
if (document.readyState === "loading") {
    document.addEventListener(
        "DOMContentLoaded", initImportPage);
} else {
    initImportPage();
}
