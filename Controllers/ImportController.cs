using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Master.Configuration;
using Master.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Master.Controllers;

// ================================================
// Excel / CSV -> SQL Server.
//
// Flow:
//   1. The browser parses the .xlsx / .xls / .csv
//      file with SheetJS (already loaded in the
//      layout) and paints the rows into the grid.
//   2. "Save to Database" posts the parsed grid to
//      this controller. The table is created if it
//      does not exist, the column types are inferred
//      from the values and every row is inserted.
//   3. Every save is written to dbo.ImportHistory so
//      the page can list the files that were already
//      saved, and "View" reads one of them back.
//
// Nothing is hard-coded: the column list and the
// data types come from the uploaded file, and every
// value is written with a parameterized INSERT.
// ================================================

[Authorize]
public class ImportController : Controller
{
    private readonly IConfiguration _configuration;

    public ImportController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    // The JSON payload of "Save to Database" carries
    // the whole sheet, so it is bigger than the upload
    // itself.
    private const int MaxSaveBytes = 25 * 1024 * 1024;

    public const int MaxRows = 20000;

    // Rows sent back when a saved table is opened.
    private const int MaxViewRows = 20000;

    // Every table built here carries this hidden IDENTITY
    // column. These tables have no primary key of their
    // own, and without a key of some kind there is no way
    // to say "change THAT row" once it is being edited in
    // the browser. The column is kept out of the column
    // list the grid draws, so it never shows up as a
    // heading, but it is what an UPDATE is matched on.
    private const string RowIdColumn = "__rid";

    private static readonly Regex SafeName =
        new(@"^[A-Za-z_][A-Za-z0-9_]*$",
            RegexOptions.Compiled);

    // Words that make a column a boolean column.
    private static readonly HashSet<string> BoolWords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "yes", "no", "true", "false", "y", "n"
        };

    // Only these words mean "true". BoolWords cannot be
    // used here because it also holds "no" / "false".
    private static readonly HashSet<string> TrueWords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "yes", "true", "y"
        };

    // The SQL types a client is allowed to ask for.
    //
    // The type is written straight into the DDL by
    // EnsureTableAsync and EnsureColumnsAsync, and
    // CREATE TABLE / ALTER TABLE cannot be
    // parameterized, so it is stitched into the command
    // as text. Letting the browser send an arbitrary
    // string there would be an injection hole, so a
    // request that asks for anything not on this list
    // is ignored and the value inferred from the file
    // is used instead.
    private static readonly HashSet<string> AllowedTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "bit", "int", "bigint", "smallint",
            "tinyint", "money", "float", "real",
            "date", "datetime2", "time",
            "decimal", "numeric"
        };

    // nvarchar / varchar / varbinary / binary need a
    // length, and decimal / numeric need a precision
    // and a scale, so they cannot be matched as a plain
    // word. These two patterns stand in for them.
    private static readonly Regex LengthType =
        new(@"^(nvarchar|varchar|varbinary|binary)\s*\(\s*(max|\d{1,4})\s*\)$",
            RegexOptions.Compiled
                | RegexOptions.IgnoreCase);

    private static readonly Regex PrecisionType =
        new(@"^(decimal|numeric)\s*\(\s*\d{1,2}\s*,\s*\d{1,2}\s*\)$",
            RegexOptions.Compiled
                | RegexOptions.IgnoreCase);

    // A type is only used when the whole request maps to
    // one of the shapes above. Anything else falls back
    // to what the file says.
    private static string? SafeType(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        string type = candidate.Trim();

        if (AllowedTypes.Contains(type)
            || LengthType.IsMatch(type)
            || PrecisionType.IsMatch(type))
        {
            return type;
        }

        return null;
    }


    // ============================================
    // VIEW MODELS
    // ============================================

    public class ImportViewModel
    {
        public List<string> Tables { get; set; } = new();

        public List<ImportHistoryViewModel> History
        {
            get;
            set;
        } = new();
    }


    // One file column: the header text plus the SQL
    // type that will be created for it.
    public class ImportColumnViewModel
    {
        public string Header { get; set; } = "";

        public string SqlType { get; set; } = "nvarchar(255)";
    }


    // Body of "Save to Database".
    public class ImportSaveRequest
    {
        public string TableName { get; set; } = "";

        public string FileName { get; set; } = "";

        public List<string> Columns { get; set; } = new();

        public List<List<string>> Rows { get; set; } = new();

        // One SQL type per column, sent by the browser so
        // a user can correct the value that was guessed
        // from the file. Anything not on the server's
        // whitelist is ignored. A shorter list than
        // Columns, or a missing entry, means "infer it".
        public List<string>? ColumnTypes { get; set; }

        // "append" (the default) adds the rows to
        // whatever the table already holds. "replace"
        // empties the table first. "update" rewrites rows
        // that are already there. Anything else is
        // treated as append.
        public string? Mode { get; set; }

        // Which rows to write, by their position in Rows.
        // Null means every row, which is what a page that
        // has no row selection sends.
        public List<int>? RowIndexes { get; set; }

        // The hidden __rid of each row, sent only by an
        // update. It lines up with Rows one to one and is
        // what the UPDATE is matched on. A short list is
        // refused rather than guessed at, because writing
        // a row to the wrong place is worse than not
        // writing it at all.
        public List<long>? RowIds { get; set; }
    }


    public class ImportSaveResponse
    {
        public bool Success { get; set; }

        public string Message { get; set; } = "";

        public string Table { get; set; } = "";

        public bool TableCreated { get; set; }

        public int TotalRows { get; set; }

        public int ImportedRows { get; set; }

        public int FailedRows { get; set; }

        public List<string> Failures { get; set; } = new();

        // "append" or "replace", echoing what was
        // actually done.
        public string Mode { get; set; } = "append";
    }


    // Answer of "open a table that was saved earlier".
    public class ImportTableDataResponse
    {
        public bool Success { get; set; }

        public string Message { get; set; } = "";

        public string Table { get; set; } = "";

        public List<ImportColumnViewModel> Columns
        {
            get;
            set;
        } = new();

        public List<List<string>> Rows { get; set; } = new();

        // The hidden __rid of each row, in the same order
        // as Rows. The browser keeps it beside the row so
        // an update can say which physical row to write
        // to. It is not a column of its own, so it is
        // never drawn.
        public List<long> RowIds { get; set; } = new();

        public bool Truncated { get; set; }
    }


    public class ImportResultViewModel
    {
        public string Table { get; set; } = "";

        public bool TableCreated { get; set; }

        public string FileName { get; set; } = "";

        public int TotalRows { get; set; }

        public int ImportedRows { get; set; }

        public int FailedRows { get; set; }

        public string Mode { get; set; } = "append";
    }


    public class ImportHistoryViewModel
    {
        public int Id { get; set; }

        public string TableName { get; set; } = "";

        public string FileName { get; set; } = "";

        public int TotalRows { get; set; }

        public int ImportedRows { get; set; }

        public int FailedRows { get; set; }

        public string UserName { get; set; } = "-";

        public DateTime ImportedOn { get; set; }

        public string Mode { get; set; } = "append";
    }


    private string? ConnectionString =>
        _configuration.GetConnectionString(
            "DefaultConnection");


    // ============================================
    // CONNECTION
    // ============================================

    // The database is reached over the internet, so the
    // TLS / pre-login handshake sometimes stalls and the
    // driver throws "The wait operation timed out". That
    // is a network hiccup, not a real failure, so the
    // open is retried a few times before giving up and
    // asking the user to retry.
    private async Task<SqlConnection> OpenConnectionAsync()
    {
        string? raw = ConnectionString;

        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException(
                "Connection string "
                    + "'DefaultConnection' is missing.");
        }

        var builder =
            new SqlConnectionStringBuilder(raw);

        if (builder.ConnectRetryCount < 4)
        {
            builder.ConnectRetryCount = 4;
        }

        if (builder.ConnectRetryInterval < 2)
        {
            builder.ConnectRetryInterval = 2;
        }

        if (builder.ConnectTimeout > 15)
        {
            builder.ConnectTimeout = 15;
        }

        SqlException? last = null;

        for (int attempt = 1; attempt <= 2; attempt++)
        {
            SqlConnection connection =
                new SqlConnection(
                    builder.ConnectionString);

            try
            {
                await connection.OpenAsync();

                return connection;
            }
            catch (SqlException ex)
                when (IsTransient(ex)
                      && attempt < 2)
            {
                last = ex;

                await connection.DisposeAsync();

                await Task.Delay(
                    TimeSpan.FromSeconds(
                        attempt * 2));
            }
            catch
            {
                await connection.DisposeAsync();

                throw;
            }
        }

        if (last != null)
        {
            throw last;
        }

        throw new InvalidOperationException(
            "The database did not respond.");
    }


    // Errors that are worth retrying: handshake and
    // socket timeouts, dropped connections, throttling
    // and the server restarting.
    private static bool IsTransient(SqlException ex)
    {
        foreach (SqlError error in ex.Errors)
        {
            switch (error.Number)
            {
                case -2:
                case 20:
                case 64:
                case 121:
                case 233:
                case 10053:
                case 10054:
                case 10060:
                case 11001:
                case 10928:
                case 10929:
                case 40197:
                case 40501:
                case 40613:
                case 41301:
                case 41302:
                case 41305:
                case 41325:
                case 49918:
                case 49919:
                case 49920:
                    return true;
            }
        }

        for (Exception? inner = ex.InnerException;
             inner != null;
             inner = inner.InnerException)
        {
            if (inner is TimeoutException
                || (inner is Win32Exception win32
                    && win32.NativeErrorCode
                        is 121 or 1160))
            {
                return true;
            }
        }

        return false;
    }


    // Message shown on the page when the database
    // cannot be reached, instead of a crash page.
    private static string FriendlyDatabaseError(Exception ex)
    {
        System.Diagnostics.Debug.WriteLine(ex);

        return "Could not reach the database. This is "
            + "usually a temporary network or SQL Server "
            + "connection problem, so please try again.";
    }


    // ============================================
    // INDEX
    // ============================================

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var model = new ImportViewModel();

        await LoadHistoryAsync(model);

        await LoadTablesAsync(model);

        return View(model);
    }


    // ============================================
    // SAVE TO DATABASE
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxSaveBytes)]
    public async Task<IActionResult> Save(
        [FromBody] ImportSaveRequest? request)
    {
        if (request == null)
        {
            return BadRequestJson(
                "No data was received.");
        }

        string tableName =
            CleanName(request.TableName);

        if (tableName.Length == 0)
        {
            return BadRequestJson(
                "Please enter a valid table name "
                    + "(letters, digits and underscore, "
                    + "starting with a letter or "
                    + "underscore).");
        }

        if (request.Columns.Count == 0)
        {
            return BadRequestJson(
                "The file does not have any columns.");
        }

        if (request.Rows.Count == 0)
        {
            return BadRequestJson(
                "The file does not have any data rows.");
        }

        if (request.Rows.Count > MaxRows)
        {
            return BadRequestJson(
                $"Only the first {MaxRows} rows can be "
                    + "saved at a time.");
        }

        if (request.Columns.Count > 500)
        {
            return BadRequestJson(
                "The file has too many columns.");
        }

        // "replace" empties the table before the insert.
        // Anything the browser sends that is not exactly
        // "replace" is treated as an append, so a broken
        // or missing value can never delete data by
        // accident.
        bool replace =
            string.Equals(
                request.Mode?.Trim(),
                "replace",
                StringComparison.OrdinalIgnoreCase);

        bool update =
            string.Equals(
                request.Mode?.Trim(),
                "update",
                StringComparison.OrdinalIgnoreCase);

        string mode =
            update ? "update"
                : replace ? "replace"
                : "append";

        // Only the ticked rows are written. The indexes
        // arrive sorted by the page, but they are sorted
        // here anyway so the order of the table matches
        // the order of the file.
        List<List<string>> rows;

        // The row number each entry had in the file, so a
        // failure can be pointed at the right line of the
        // sheet even when the user has unticked some rows.
        var sourceRows = new List<int>();

        if (request.RowIndexes is null)
        {
            rows = request.Rows;

            for (int i = 0; i < rows.Count; i++)
            {
                sourceRows.Add(i);
            }
        }
        else
        {
            var picked = new List<List<string>>();

            foreach (int index in request.RowIndexes
                .Where(i => i >= 0 && i < request.Rows.Count)
                .Distinct()
                .OrderBy(i => i))
            {
                picked.Add(request.Rows[index]);

                sourceRows.Add(index);
            }

            rows = picked;
        }

        if (rows.Count == 0)
        {
            return BadRequestJson(
                "No rows are selected.");
        }

        if (rows.Count > MaxRows)
        {
            return BadRequestJson(
                $"Only the first {MaxRows} rows can be "
                    + "saved at a time.");
        }

        // An update has to say which stored row each entry
        // belongs to. The list is filtered with exactly
        // the same indexes as the rows above, so the two
        // stay lined up, and a count that does not match
        // is refused instead of being padded. Writing a
        // value into the wrong row would be far worse
        // than not writing it at all.
        var rowIds = new List<long>();

        if (update)
        {
            if (request.RowIds is null
                || request.RowIds.Count != request.Rows.Count)
            {
                return BadRequestJson(
                    "The rows to update are missing. "
                        + "Reopen the table and try again.");
            }

            for (int i = 0; i < rows.Count; i++)
            {
                int index = sourceRows[i];

                if (request.RowIds[index] <= 0)
                {
                    return BadRequestJson(
                        "One of the rows to update has no "
                            + "identity. Reopen the table "
                            + "and try again.");
                }

                rowIds.Add(request.RowIds[index]);
            }
        }

        List<ImportColumnViewModel> columns =
            BuildColumns(
                request.Columns,
                rows,
                request.ColumnTypes);

        var result = new ImportResultViewModel
        {
            Table = tableName,
            FileName = request.FileName,
            TotalRows = rows.Count,
            Mode = mode
        };

        SqlConnection connection;

        try
        {
            connection =
                await OpenConnectionAsync();
        }
        catch (Exception ex)
        {
            return BadRequestJson(
                FriendlyDatabaseError(ex));
        }

        using (connection)
        {

        try
        {

        // An update rewrites rows that are already stored, so
        // the table has to be there and its shape is left
        // exactly as it is. Creating anything here would
        // quietly build an empty table if the name were
        // mistyped, which is the opposite of what an update
        // should do.
        if (update)
        {
            if (!await TableExistsAsync(
                    connection, tableName))
            {
                return BadRequestJson(
                    $"Table '{tableName}' does not exist "
                        + "any more.");
            }

            bool added =
                await EnsureRowIdColumnAsync(
                    connection, tableName);

            if (added)
            {
                // The stored rows were given their
                // identities just now, so the numbers the
                // browser is holding are meaningless.
                return BadRequestJson(
                    "This table was given row identities "
                        + "just now. Reopen it and make "
                        + "your changes again.");
            }

            (int changed, List<string> problems) =
                await UpdateRowsAsync(
                    connection,
                    tableName,
                    columns,
                    rows,
                    rowIds,
                    sourceRows);

            result.ImportedRows = changed;

            result.FailedRows =
                result.TotalRows
                    - result.ImportedRows;

            await SaveHistoryAsync(result);

            return Json(new ImportSaveResponse
            {
                Success = result.FailedRows == 0,
                Table = result.Table,
                TableCreated = false,
                TotalRows = result.TotalRows,
                ImportedRows = result.ImportedRows,
                FailedRows = result.FailedRows,
                Failures = problems.Take(50).ToList(),
                Mode = result.Mode,
                Message = result.FailedRows == 0
                    ? $"{changed} row(s) updated."
                    : ""
            });
        }

        result.TableCreated =
            await EnsureTableAsync(
                connection, tableName, columns);

        // The table may be older than the file, so any
        // column the file adds is put on it here.
        await EnsureColumnsAsync(
            connection, tableName, columns);

        // A replace on a table that was just created is
        // the same as an append, and skipping the delete
        // keeps that path cheaper.
        (int imported, List<string> failures) =
            await InsertRowsAsync(
                connection,
                tableName,
                columns,
                rows,
                sourceRows,
                replace && !result.TableCreated);

        result.ImportedRows = imported;

        result.FailedRows =
            result.TotalRows
                - result.ImportedRows;

        await SaveHistoryAsync(result);

        return Json(new ImportSaveResponse
        {
            Success = result.FailedRows == 0,
            Table = result.Table,
            TableCreated = result.TableCreated,
            TotalRows = result.TotalRows,
            ImportedRows = result.ImportedRows,
            FailedRows = result.FailedRows,
            Failures = failures,
            Mode = mode
        });

        }

        catch (Exception ex)
        {
            return BadRequestJson(
                FriendlyDatabaseError(ex));
        }

        }

    }


    private IActionResult BadRequestJson(string message) =>
        Json(new ImportSaveResponse
        {
            Success = false,
            Message = message
        });


    // ============================================
    // SAVED FILES (refreshed by the grid after a
    // save, so the list never looks stale)
    // ============================================

    [HttpGet]
    public async Task<IActionResult> History(
        string? search = null,
        DateTime? from = null,
        DateTime? to = null)
    {
        var model = new ImportViewModel();

        await LoadHistoryAsync(model, search, from, to);

        return Json(model.History);
    }


    // ============================================
    // OPEN A TABLE THAT WAS SAVED EARLIER
    // ============================================

    [HttpGet]
    public async Task<IActionResult> TableData(
        string? table)
    {
        string tableName = CleanName(table);

        if (tableName.Length == 0)
        {
            return BadRequestJson(
                "Please choose a table.");
        }

        var response =
            new ImportTableDataResponse
            {
                Table = tableName
            };

        SqlConnection connection;

        try
        {
            connection =
                await OpenConnectionAsync();
        }
        catch (Exception ex)
        {
            return BadRequestJson(
                FriendlyDatabaseError(ex));
        }

        using (connection)
        {

        try
        {

        List<(string Name, string Type)>
            schema =
                await ReadSchemaAsync(
                    connection, tableName);

        if (schema.Count == 0)
        {
            return BadRequestJson(
                $"Table '{tableName}' does not "
                    + "exist any more.");
        }

        foreach ((string name, string type) in schema)
        {
            response.Columns.Add(
                new ImportColumnViewModel
                {
                    Header = name,
                    SqlType = type
                });
        }

        // The column is added here rather than at save
        // time so that a table imported before this
        // existed can still be opened for an update.
        bool hadRowId =
            await EnsureRowIdColumnAsync(
                connection, tableName);

        System.Diagnostics.Debug.WriteLine(
            "TableData " + tableName
                + " rowIdAdded=" + hadRowId);

        // TOP is ordered by __rid so paging through the
        // rows always walks the table in the same order,
        // otherwise the same physical row could show up
        // at two different positions on two loads and an
        // update would land on the wrong one.
        string sql =
            "SELECT TOP (@Top) "
                + $"[{RowIdColumn}], "
                + string.Join(
                    ", ",
                    schema.Select(
                        c => $"[{c.Name}]"))
                + $" FROM [dbo].[{tableName}] "
                + $"ORDER BY [{RowIdColumn}]";

        using SqlCommand command =
            new SqlCommand(sql, connection);
            command.CommandTimeout = 0;

        command.Parameters.Add(
            new SqlParameter("@Top", SqlDbType.Int)
            {
                Value = MaxViewRows + 1
            });

        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            if (response.Rows.Count >= MaxViewRows)
            {
                response.Truncated = true;

                break;
            }

            var row = new List<string>(
                schema.Count);

            // Column 0 of the reader is __rid, which the
            // grid never shows. It is kept beside the row
            // so an update knows where to write.
            response.RowIds.Add(
                Convert.ToInt64(
                    reader.GetValue(0)));

            for (int c = 0; c < schema.Count; c++)
            {
                row.Add(
                    ReadCell(reader, c + 1));
            }

            response.Rows.Add(row);
        }

        response.Success = true;

        return Json(response);

        }

        catch (Exception ex)
        {
            return BadRequestJson(
                FriendlyDatabaseError(ex));
        }

        }

    }


    private static string ReadCell(
        SqlDataReader reader,
        int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return "";
        }

        return reader.GetValue(ordinal)
            switch
            {
                DateTime date =>
                    date.TimeOfDay == TimeSpan.Zero
                        ? date.ToString("dd/MM/yyyy")
                        : date.ToString("dd/MM/yyyy HH:mm"),

                TimeSpan time =>
                    time.ToString(@"hh\:mm"),

                bool flag =>
                    flag ? "Yes" : "No",

                byte[] blob =>
                    $"0x{Convert.ToHexString(blob)}",

                decimal number =>
                    number.ToString(
                        "0.############",
                        CultureInfo.InvariantCulture),

                IFormattable other =>
                    other.ToString(
                        null,
                        CultureInfo.InvariantCulture),

                _ => reader.GetValue(ordinal).ToString() ?? ""
            };
    }


    private async Task<
        List<(string Name, string Type)>>
        ReadSchemaAsync(
            SqlConnection connection,
            string tableName)
    {
        // __rid is left out here rather than at each call
        // site. It is bookkeeping for updates, not a
        // column of the table, so every caller that builds
        // a column list or a grid heading must not see it.
        const string sql = @"
SELECT COLUMN_NAME, DATA_TYPE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = @Table
  AND COLUMN_NAME <> '__rid'
ORDER BY ORDINAL_POSITION";

        var schema =
            new List<(string, string)>();

        using SqlCommand command =
            new SqlCommand(sql, connection);
            command.CommandTimeout = 0;

        command.Parameters.Add(
            new SqlParameter(
                "@Table",
                SqlDbType.NVarChar, 128)
            {
                Value = tableName
            });

        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            schema.Add(
                (reader.GetString(0), reader.GetString(1)));
        }

        return schema;
    }


    // ============================================
    // SCHEMA INFERENCE
    // ============================================

    // The column list and the SQL types come straight
    // from the file: one column per header, one type
    // inferred from the values underneath it.
    //
    // The browser may send a type for each column so a
    // wrong guess can be corrected before the table is
    // built. Anything not on the whitelist is dropped
    // and the inferred type is kept.
    private static List<ImportColumnViewModel> BuildColumns(
        List<string> headers,
        List<List<string>> data,
        List<string>? requestedTypes)
    {
        var columns =
            new List<ImportColumnViewModel>();

        for (int c = 0; c < headers.Count; c++)
        {
            string header =
                string.IsNullOrWhiteSpace(
                    headers[c])
                    ? $"Column {c + 1}"
                    : headers[c].Trim();

            string? asked =
                requestedTypes is not null
                    && c < requestedTypes.Count
                        ? SafeType(
                            requestedTypes[c])
                        : null;

            columns.Add(
                new ImportColumnViewModel
                {
                    Header = header,
                    SqlType =
                        asked ?? InferType(data, c)
                });
        }

        return columns;
    }


    private static string InferType(
        List<List<string>> data,
        int column)
    {
        bool any = false;
        bool allInt = true;
        bool allLong = true;
        bool allDecimal = true;
        bool allDate = true;
        bool allDateTime = true;
        bool allBool = true;
        bool allTime = true;
        bool anyTime = false;

        int maxLength = 0;

        int scanned = 0;

        foreach (List<string> row in data)
        {
            string value = CellAt(row, column).Trim();

            if (value.Length == 0)
            {
                continue;
            }

            any = true;
            scanned++;

            maxLength = Math.Max(
                maxLength, value.Length);

            if (!BoolWords.Contains(value)
                && value is not ("0" or "1"))
            {
                allBool = false;
            }

            if (!TryParseInteger(value, out long asLong))
            {
                allLong = false;
                allInt = false;
            }
            else if (asLong > int.MaxValue
                     || asLong < int.MinValue)
            {
                allInt = false;
            }

            if (!TryParseDecimal(value, out _))
            {
                allDecimal = false;
            }

            if (TryParseTime(value, out _))
            {
                anyTime = true;
            }
            else
            {
                allTime = false;
            }

            if (TryParseDate(value, out DateTime parsed))
            {
                if (parsed.TimeOfDay != TimeSpan.Zero)
                {
                    allDate = false;
                }
            }
            else
            {
                allDate = false;
                allDateTime = false;
            }

            if (scanned >= 1000)
            {
                break;
            }
        }

        if (!any)
        {
            return "nvarchar(255)";
        }

        if (allBool)
        {
            return "bit";
        }

        if (allInt)
        {
            return "int";
        }

        if (allLong)
        {
            return "bigint";
        }

        if (allDecimal)
        {
            return "decimal(18,2)";
        }

        if (allTime && anyTime)
        {
            return "time";
        }

        if (allDate)
        {
            return "date";
        }

        if (allDateTime)
        {
            return "datetime2";
        }

        return maxLength <= 4000
            ? $"nvarchar({maxLength})"
            : "nvarchar(max)";
    }


    private static bool TryParseInteger(
        string value,
        out long parsed) =>
        long.TryParse(
            value,
            NumberStyles.Integer
                | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out parsed)
            || long.TryParse(
            value,
            NumberStyles.Integer
                | NumberStyles.AllowThousands,
            CultureInfo.CurrentCulture,
            out parsed);


    private static bool TryParseDecimal(
        string value,
        out decimal parsed) =>
        decimal.TryParse(
            value,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out parsed)
            || decimal.TryParse(
            value,
            NumberStyles.Number,
            CultureInfo.CurrentCulture,
            out parsed);


    private static bool TryParseDate(
        string value,
        out DateTime parsed) =>
        DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out parsed)
            || DateTime.TryParse(
            value,
            CultureInfo.CurrentCulture,
            DateTimeStyles.None,
            out parsed)
            || DateTime.TryParseExact(
            value,
            new[]
            {
                "dd/MM/yyyy", "dd-MM-yyyy",
                "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss",
                "dd/MM/yyyy hh:mm tt",
                "yyyy-MM-dd", "yyyy-MM-dd HH:mm",
                "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm",
                "yyyy-MM-ddTHH:mm:ss",
                "MM/dd/yyyy", "MM/dd/yyyy HH:mm",
                "d-M-yyyy", "dd.MM.yyyy"
            },
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out parsed);


    private static bool TryParseTime(
        string value,
        out TimeSpan parsed) =>
        TimeSpan.TryParse(
            value,
            CultureInfo.InvariantCulture,
            out parsed)
            || TimeSpan.TryParse(
            value,
            CultureInfo.CurrentCulture,
            out parsed);


    // ============================================
    // TABLE NAME
    // ============================================

    // Keeps only characters SQL Server accepts in an
    // identifier and makes sure it does not start
    // with a digit. This is also what keeps any user
    // text out of the DDL string.
    private static string CleanName(string? value)
    {
        var sb = new StringBuilder();

        foreach (char c in value ?? "")
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                sb.Append(c);
            }
            else if (sb.Length > 0)
            {
                sb.Append('_');
            }

            if (sb.Length >= 100)
            {
                break;
            }
        }

        string name = sb.ToString().Trim('_');

        if (name.Length > 0 && char.IsDigit(name[0]))
        {
            name = "T" + name;
        }

        return SafeName.IsMatch(name) ? name : "";
    }


    // A safe, unique column name for the DDL.
    private static string ColumnName(
        string header,
        int index,
        HashSet<string> used)
    {
        string name = CleanName(header);

        if (name.Length == 0)
        {
            name = $"Column{index + 1}";
        }

        string candidate = name;
        int suffix = 2;

        while (!used.Add(candidate))
        {
            candidate =
                $"{name}_{suffix++}";
        }

        return candidate;
    }


    // The column names in the order the INSERT uses.
    private static List<string> ColumnNames(
        List<ImportColumnViewModel> columns)
    {
        var used = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        var names = new List<string>();

        for (int c = 0; c < columns.Count; c++)
        {
            names.Add(
                ColumnName(
                    columns[c].Header, c, used));
        }

        return names;
    }


    // ============================================
    // CREATE TABLE
    // ============================================

    // Returns true when the table was created by this
    // call, false when it already existed.
    private async Task<bool> EnsureTableAsync(
        SqlConnection connection,
        string tableName,
        List<ImportColumnViewModel> columns)
    {
        if (await TableExistsAsync(
                connection, tableName))
        {
            return false;
        }

        var definitions = new List<string>();

        // The hidden row number goes on first so every
        // row that is ever inserted or updated has a
        // stable name to be matched on later.
        definitions.Add(
            $"[{RowIdColumn}] BIGINT "
                + "IDENTITY(1,1) NOT NULL");

        List<string> names =
            ColumnNames(columns);

        for (int c = 0; c < names.Count; c++)
        {
            definitions.Add(
                $"[{names[c]}] "
                    + $"{columns[c].SqlType} NULL");
        }

        // CREATE TABLE cannot be parameterized, so the
        // identifiers are rebuilt from the validated
        // name above and wrapped in QUOTENAME brackets.
        string ddl =
            "CREATE TABLE [dbo].["
                + tableName + "] ("
                + string.Join(
                    ", ", definitions)
                + ")";

using SqlCommand create =
            new SqlCommand(ddl, connection);
        create.CommandTimeout = 60;

        await create.ExecuteNonQueryAsync();

        return true;
    }


    // Puts the hidden __rid on a table that was made
    // before this column existed, or that came from
    // somewhere else entirely.
    //
    // SQL Server fills an IDENTITY column in on the rows
    // that are already there, so this doubles as the
    // backfill: the rows already stored get a number just
    // like new ones will.
    //
    // Returns true when the column had to be added.
    private async Task<bool> EnsureRowIdColumnAsync(
        SqlConnection connection,
        string tableName)
    {
        const string sql = @"
SELECT CAST(CASE WHEN EXISTS (
    SELECT 1
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = @Table
      AND COLUMN_NAME = '__rid'
) THEN 0 ELSE 1 END AS int)";

        using SqlCommand check =
            new SqlCommand(sql, connection);
        check.CommandTimeout = 0;

        check.Parameters.Add(
            new SqlParameter(
                "@Table",
                SqlDbType.NVarChar, 128)
            {
                Value = tableName
            });

        bool needed =
            Convert.ToInt32(
                await check.ExecuteScalarAsync()) == 1;

        if (!needed)
        {
            return false;
        }

        // A table cannot hold two IDENTITY columns, so if
        // something already occupies that slot there is
        // no safe way to add a second one. Updating is
        // refused for that table rather than risking the
        // wrong row being written to.
        string ddl =
            "ALTER TABLE [dbo].["
                + tableName + "] "
                + "ADD ["
                + RowIdColumn
                + "] BIGINT IDENTITY(1,1) NOT NULL";

        try
        {
            using SqlCommand alter =
                new SqlCommand(ddl, connection);
            alter.CommandTimeout = 60;

            await alter.ExecuteNonQueryAsync();

            return true;
        }
        catch (SqlException ex)
            when (ex.Number == 1075)
        {
            // 1075 = "cannot create more than one identity
            // column in a table".
            System.Diagnostics.Debug.WriteLine(ex);

            return false;
        }
    }


    private async Task<bool> TableExistsAsync(
        SqlConnection connection,
        string tableName)
    {
        using SqlCommand check =
            new SqlCommand(
                "SELECT COUNT(*) "
                    + "FROM INFORMATION_SCHEMA.TABLES "
                    + "WHERE TABLE_NAME = @Table",
                connection);
            check.CommandTimeout = 0;

        check.Parameters.Add(
            new SqlParameter(
                "@Table",
                SqlDbType.NVarChar, 128)
            {
                Value = tableName
            });

        return Convert.ToInt32(
            await check.ExecuteScalarAsync()) > 0;
    }


    // Adds the columns the file has but the table does
    // not have yet, so importing a newer version of
    // the same sheet into an old table still works.
    // Returns how many columns were added.
    private async Task<int> EnsureColumnsAsync(
        SqlConnection connection,
        string tableName,
        List<ImportColumnViewModel> columns)
    {
        var existing =
            await ReadSchemaAsync(
                connection, tableName);

        if (existing.Count == 0)
        {
            return 0;
        }

        var known = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach ((string name, _) in existing)
        {
            known.Add(name);
        }

        List<string> names =
            ColumnNames(columns);

        int added = 0;

        for (int c = 0; c < names.Count; c++)
        {
            if (known.Contains(names[c]))
            {
                continue;
            }

            string ddl =
                "ALTER TABLE [dbo].[" + tableName + "] "
                    + "ADD [" + names[c] + "] "
                    + columns[c].SqlType + " NULL";

            using SqlCommand alter =
                new SqlCommand(ddl, connection);
                alter.CommandTimeout = 60;

            await alter.ExecuteNonQueryAsync();

            added++;
        }

        return added;
    }


    // ============================================
    // INSERT
    // ============================================

    // One prepared command reused for every row inside
    // a single transaction, so a 20,000 row sheet is
    // written in seconds instead of minutes.
    //
    // When emptyFirst is set the table is emptied inside
    // the same transaction as the insert, so either both
    // happen or neither does. A failure part way through
    // leaves the table as it was rather than half
    // replaced.
    private async Task<(int Imported, List<string> Failures)>
        InsertRowsAsync(
        SqlConnection connection,
        string tableName,
        List<ImportColumnViewModel> columns,
        List<List<string>> data,
        List<int> sourceRows,
        bool emptyFirst)
    {
        List<string> names =
            ColumnNames(columns);

        string sql =
            $"INSERT INTO [dbo].[{tableName}] ("
                + string.Join(
                    ", ",
                    names.Select(n => $"[{n}]"))
                + ") VALUES ("
                + string.Join(
                    ", ",
                    names.Select(n => $"@{n}"))
                + ")";

        int imported = 0;

        int skipped = 0;

        var failures = new List<string>();

        await using SqlTransaction transaction =
            (SqlTransaction)
                await connection.BeginTransactionAsync();

        if (emptyFirst)
        {
            using SqlCommand wipe =
                new SqlCommand(
                    "DELETE FROM [dbo].["
                        + tableName + "]",
                    connection,
                    transaction);
                wipe.CommandTimeout = 0;

            await wipe.ExecuteNonQueryAsync();
        }

        using SqlCommand command =
            new SqlCommand
            {
                Connection = connection,
                Transaction = transaction,
                CommandText = sql,
                CommandTimeout = 0
            };

        SqlParameter[] slots =
            new SqlParameter[names.Count];

        for (int c = 0; c < names.Count; c++)
        {
            slots[c] =
                new SqlParameter(
                    $"@{names[c]}",
                    SqlDbType.NVarChar);

            command.Parameters.Add(slots[c]);
        }

        for (int r = 0; r < data.Count; r++)
        {
            List<string> row = data[r];

            for (int c = 0; c < names.Count; c++)
            {
                FillParameter(
                    slots[c],
                    columns[c].SqlType,
                    CellAt(row, c));
            }

            try
            {
                await command.ExecuteNonQueryAsync();

                imported++;
            }
            catch (SqlException ex)
            {
                // The row number is the one it had in the
                // file, so it still lines up after unticked
                // rows have been filtered out. A bad value
                // on 20,000 rows would otherwise mean a
                // 20,000 line error list, so only the first
                // few are named and the rest are counted.
                if (failures.Count < 50)
                {
                    int sheetRow =
                        r < sourceRows.Count
                            ? sourceRows[r] + 2
                            : r + 2;

                    failures.Add(
                        $"Row {sheetRow}: {ex.Message}");
                }
                else
                {
                    skipped++;
                }

                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        await transaction.CommitAsync();

        if (skipped > 0)
        {
            failures.Add(
                $"... and {skipped} more row(s) failed "
                    + "for the same reason.");
        }

        if (failures.Count > 0)
        {
            System.Diagnostics.Debug.WriteLine(
                string.Join(
                    " | ",
                    failures));
        }

        return (imported, failures);
    }


    // Writes edits back onto rows that are already in the
    // table.
    //
    // The row is found by its hidden __rid, which the
    // browser carried down from the load, so an edited
    // row lands on the row it came from and not on
    // whichever one happens to sit at the same position
    // now. __rid is left out of the SET list because it
    // is generated and is not a value anyone edits.
    //
    // Every row runs in the same transaction, so if the
    // transaction cannot be opened, or the save fails in
    // a way that is not row specific, the table is left
    // exactly as it was. A value that is simply wrong
    // for its column is a per row problem and is
    // reported against that row, the same as an insert.
    private async Task<(int Imported, List<string> Failures)>
        UpdateRowsAsync(
        SqlConnection connection,
        string tableName,
        List<ImportColumnViewModel> columns,
        List<List<string>> data,
        List<long> rowIds,
        List<int> sourceRows)
    {
        List<string> names =
            ColumnNames(columns);

        string sql =
            "UPDATE [dbo].[" + tableName + "] SET "
                + string.Join(
                    ", ",
                    names.Select(n => $"[{n}] = @{n}"))
                + $" WHERE [{RowIdColumn}] = @__rid";

        int updated = 0;

        int skipped = 0;

        var failures = new List<string>();

        await using SqlTransaction transaction =
            (SqlTransaction)
                await connection.BeginTransactionAsync();

        using SqlCommand command =
            new SqlCommand
            {
                Connection = connection,
                Transaction = transaction,
                CommandText = sql,
                CommandTimeout = 0
            };

        SqlParameter[] slots =
            new SqlParameter[names.Count];

        for (int c = 0; c < names.Count; c++)
        {
            slots[c] =
                new SqlParameter(
                    $"@{names[c]}",
                    SqlDbType.NVarChar);

            command.Parameters.Add(slots[c]);
        }

        SqlParameter key =
            new SqlParameter(
                "@__rid", SqlDbType.BigInt);

        command.Parameters.Add(key);

        for (int r = 0; r < data.Count; r++)
        {
            List<string> row = data[r];

            for (int c = 0; c < names.Count; c++)
            {
                FillParameter(
                    slots[c],
                    columns[c].SqlType,
                    CellAt(row, c));
            }

            key.Value = rowIds[r];

            try
            {
                int affected =
                    await command.ExecuteNonQueryAsync();

                if (affected == 0)
                {
                    // The row was deleted from under the
                    // page while it was open. Nothing was
                    // written, so it is worth saying so
                    // rather than reporting a success.
                    if (failures.Count < 50)
                    {
                        int sheetRow =
                            r < sourceRows.Count
                                ? sourceRows[r] + 2
                                : r + 2;

                        failures.Add(
                            $"Row {sheetRow}: this row no "
                                + "longer exists in the "
                                + "table, so it was not "
                                + "changed.");
                    }
                    else
                    {
                        skipped++;
                    }

                    continue;
                }

                updated++;
            }
            catch (SqlException ex)
            {
                if (failures.Count < 50)
                {
                    int sheetRow =
                        r < sourceRows.Count
                            ? sourceRows[r] + 2
                            : r + 2;

                    failures.Add(
                        $"Row {sheetRow}: {ex.Message}");
                }
                else
                {
                    skipped++;
                }

                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        await transaction.CommitAsync();

        if (skipped > 0)
        {
            failures.Add(
                $"... and {skipped} more row(s) failed "
                    + "for the same reason.");
        }

        return (updated, failures);
    }


    // Each value is sent as a typed parameter, so the
    // column type inferred from the file is preserved.
    // The slot is reused, so only the value and the
    // type are swapped between rows.
    private static void FillParameter(
        SqlParameter slot,
        string sqlType,
        string rawValue)
    {
        string value = (rawValue ?? "").Trim();

        if (value.Length == 0)
        {
            slot.SqlDbType = SqlDbType.NVarChar;

            slot.Size = 0;

            slot.Precision = 0;

            slot.Scale = 0;

            slot.Value = DBNull.Value;

            return;
        }

        switch (sqlType)
        {
            case "bit":
                slot.SqlDbType = SqlDbType.Bit;
                slot.Value =
                    TrueWords.Contains(value)
                    || value == "1";
                break;

            case "int":
                slot.SqlDbType = SqlDbType.Int;
                slot.Value =
                    int.TryParse(
                        value,
                        NumberStyles.Integer
                            | NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture,
                        out int asInt)
                    || int.TryParse(
                        value,
                        NumberStyles.Integer
                            | NumberStyles.AllowThousands,
                        CultureInfo.CurrentCulture,
                        out asInt)
                    ? asInt
                    : (object)0;
                break;

            case "bigint":
                slot.SqlDbType = SqlDbType.BigInt;
                slot.Value =
                    TryParseInteger(value, out long asLong)
                        ? asLong
                        : (object)0L;
                break;

            case "decimal(18,2)":
                slot.SqlDbType = SqlDbType.Decimal;
                slot.Precision = 18;
                slot.Scale = 2;
                slot.Value =
                    TryParseDecimal(value, out decimal dec)
                        ? dec
                        : (object)0m;
                break;

            case "date":
                slot.SqlDbType = SqlDbType.Date;
                slot.Value =
                    TryParseDate(value, out DateTime asDate)
                        ? asDate.Date
                        : DBNull.Value;
                break;

            case "datetime2":
                slot.SqlDbType = SqlDbType.DateTime2;
                slot.Value =
                    TryParseDate(
                        value,
                        out DateTime asDateTime)
                    ? asDateTime
                    : DBNull.Value;
                break;

            case "time":
                slot.SqlDbType = SqlDbType.Time;
                slot.Value =
                    TryParseTime(value, out TimeSpan asTime)
                        ? asTime
                        : DBNull.Value;
                break;

            default:
                slot.SqlDbType = SqlDbType.NVarChar;
                slot.Value = value;
                break;
        }
    }


    private static string CellAt(
        List<string> row,
        int column) =>
        column >= 0 && column < row.Count
            ? row[column] ?? ""
            : "";


    // ============================================
    // EXISTING TABLES + IMPORT HISTORY
    // ============================================

    // Fills the "save into" dropdown with the tables
    // that are already in the database, so an import
    // can be appended to an existing one.
    private async Task LoadTablesAsync(
        ImportViewModel model)
    {
        SqlConnection connection;

        try
        {
            connection =
                await OpenConnectionAsync();
        }
        catch (Exception)
        {
            return;
        }

        using (connection)
        {

        try
        {

        using SqlCommand command =
            new SqlCommand(
                "SELECT TABLE_NAME "
                    + "FROM INFORMATION_SCHEMA.TABLES "
                    + "WHERE TABLE_TYPE = 'BASE TABLE' "
                    + "AND TABLE_NAME NOT IN "
                        + "('ImportHistory', "
                        + "'__EFMigrationsHistory') "
                    + "ORDER BY TABLE_NAME",
                connection);
            command.CommandTimeout = 0;

        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            model.Tables.Add(reader.GetString(0));
        }

        }

        catch (Exception)
        {
            // the dropdown is optional
        }

        }

    }


    // The log table is created on demand, so no extra
    // script or migration is needed.
    private async Task EnsureHistoryTableAsync(
        SqlConnection connection)
    {
        const string sql = @"
IF OBJECT_ID('dbo.ImportHistory', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ImportHistory
    (
        Id           INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_ImportHistory PRIMARY KEY,
        TableName    NVARCHAR(200) NOT NULL,
        FileName     NVARCHAR(300) NOT NULL,
        TotalRows    INT NOT NULL,
        ImportedRows INT NOT NULL,
        FailedRows   INT NOT NULL,
        ImportedBy   INT NULL,
        ImportedOn   DATETIME NOT NULL
            CONSTRAINT DF_ImportHistory_ImportedOn
                DEFAULT GETDATE(),
        Mode         NVARCHAR(20) NULL
    );
END
ELSE IF COL_LENGTH('dbo.ImportHistory', 'Mode') IS NULL
BEGIN
    -- A log table made before the append / replace
    -- choice existed, so the column is added rather than
    -- the page being left to fail.
    ALTER TABLE dbo.ImportHistory
        ADD Mode NVARCHAR(20) NULL;
END";

        using SqlCommand command =
            new SqlCommand(sql, connection);
            command.CommandTimeout = 30;

        await command.ExecuteNonQueryAsync();
    }


    private async Task LoadHistoryAsync(
        ImportViewModel model,
        string? search = null,
        DateTime? from = null,
        DateTime? to = null)
    {
        SqlConnection connection;

        try
        {
            connection =
                await OpenConnectionAsync();
        }
        catch (Exception)
        {
            // history is optional
            return;
        }

        using (connection)
        {

        try
        {

        await EnsureHistoryTableAsync(connection);

        string filter = string.Empty;
        var arguments = new List<SqlParameter>();

        if (!string.IsNullOrWhiteSpace(search))
        {
            // The term is matched against the table and
            // the file name, both of which are the things
            // a person remembers about an import.
            filter =
                " WHERE TableName LIKE @Search "
                    + "OR FileName LIKE @Search";

            arguments.Add(
                new SqlParameter(
                    "@Search",
                    SqlDbType.NVarChar, 300)
                {
                    Value = "%" + search + "%"
                });
        }

        if (from is not null)
        {
            filter +=
                (filter.Length > 0 ? " AND " : " WHERE ")
                    + "ImportedOn >= @From";

            arguments.Add(
                new SqlParameter(
                    "@From",
                    SqlDbType.DateTime)
                {
                    Value = from.Value
                });
        }

        if (to is not null)
        {
            filter +=
                (filter.Length > 0 ? " AND " : " WHERE ")
                    + "ImportedOn < @To";

            arguments.Add(
                new SqlParameter(
                    "@To",
                    SqlDbType.DateTime)
                {
                    Value = to.Value
                });
        }

        using SqlCommand command =
            new SqlCommand(
                "SELECT TOP 50 Id, TableName, FileName, "
                    + "TotalRows, ImportedRows, "
                    + "FailedRows, ImportedBy, ImportedOn, "
                    + "Mode "
                    + "FROM dbo.ImportHistory "
                    + filter
                    + " ORDER BY Id DESC",
                connection);
            command.CommandTimeout = 0;

        command.Parameters.AddRange(
            arguments.ToArray());

        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            model.History.Add(
                new ImportHistoryViewModel
                {
                    Id = reader.GetInt32(0),
                    TableName = reader.GetString(1),
                    FileName = reader.GetString(2),
                    TotalRows = reader.GetInt32(3),
                    ImportedRows = reader.GetInt32(4),
                    FailedRows = reader.GetInt32(5),
                    UserName =
                        reader.IsDBNull(6)
                            ? "-"
                            : reader.GetInt32(6)
                                .ToString(),
                    ImportedOn = reader.GetDateTime(7),
                    Mode =
                        reader.IsDBNull(8)
                            ? "append"
                            : reader.GetString(8)
                });
        }

        }

        catch (Exception)
        {
            // history is optional
        }

        }

    }


    private async Task SaveHistoryAsync(
        ImportResultViewModel result)
    {
        SqlConnection connection;

        try
        {
            connection =
                await OpenConnectionAsync();
        }
        catch (Exception)
        {
            // best effort
            return;
        }

        using (connection)
        {

        try
        {

        await EnsureHistoryTableAsync(connection);

        using SqlCommand command =
            new SqlCommand(
                "INSERT INTO dbo.ImportHistory "
                    + "(TableName, FileName, TotalRows, "
                    + "ImportedRows, FailedRows, "
                    + "ImportedBy, ImportedOn, Mode) "
                    + "VALUES (@TableName, @FileName, "
                    + "@TotalRows, @ImportedRows, "
                    + "@FailedRows, @ImportedBy, GETDATE(), "
                    + "@Mode)",
                connection);
            command.CommandTimeout = 0;

        command.Parameters.Add(
            new SqlParameter(
                "@TableName",
                SqlDbType.NVarChar, 200)
            {
                Value = result.Table
            });

        command.Parameters.Add(
            new SqlParameter(
                "@FileName",
                SqlDbType.NVarChar, 300)
            {
                Value = result.FileName
            });

        command.Parameters.Add(
            new SqlParameter(
                "@TotalRows", SqlDbType.Int)
            {
                Value = result.TotalRows
            });

        command.Parameters.Add(
            new SqlParameter(
                "@ImportedRows", SqlDbType.Int)
            {
                Value = result.ImportedRows
            });

        command.Parameters.Add(
            new SqlParameter(
                "@FailedRows", SqlDbType.Int)
            {
                Value = result.FailedRows
            });

        string? userId =
            DynamicTableService.GetLoggedInUserId(
                User);

        command.Parameters.Add(
            new SqlParameter(
                "@ImportedBy", SqlDbType.Int)
            {
                Value = int.TryParse(
                    userId, out int parsed)
                    ? parsed
                    : (object)DBNull.Value
            });

        command.Parameters.Add(
            new SqlParameter(
                "@Mode", SqlDbType.NVarChar, 20)
            {
                Value = result.Mode
            });

        await command.ExecuteNonQueryAsync();

        }

        catch (Exception)
        {
            // best effort
        }

        }

    }
}