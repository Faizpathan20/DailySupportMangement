using System.Text;
using Master.Configuration;
using Master.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Master.Controllers;

[Authorize]
public class DailySupportController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly DynamicTableService _tableService;

    public DailySupportController(
        IConfiguration configuration,
        DynamicTableService tableService)
    {
        _configuration = configuration;
        _tableService = tableService;
    }

    private bool IsAjax =>
        DynamicTableService.IsAjaxRequest(Request);

    private const string TableName = "DailySupport";

    private static readonly string[] AllowedTransitions =
    {
        "Open|In Progress",
        "Open|On Hold",
        "Open|Closed",
        "In Progress|On Hold",
        "In Progress|Pending",
        "In Progress|Closed",
        "On Hold|In Progress",
        "On Hold|Pending",
        "On Hold|Closed",
        "Pending|In Progress",
        "Pending|On Hold",
        "Pending|Closed",
        "Closed|In Progress"
    };

    // Grid columns in exactly the same order/labels as the
    // reference Daily Support table (the Actions column is
    // always appended by the view).
    private static readonly string[] TableLayout =
    {
        "TicketNo",
        "SupportDate",
        "ClientId",
        "Mobile",
        "Channel",
        "Category",
        "Priority",
        "UserId",
        "Status",
        "Subject",
        "Description",
        "CallerName"
    };

    // Human labels for the table headers (grid shows these).
    private static readonly Dictionary<string, string>
        TableLabels =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["TicketNo"] = "Ticket No",
                ["SupportDate"] = "Date & Time",
                ["ClientId"] = "Client [Id]",
                ["Channel"] = "Channel / Type",
                ["UserId"] = "Assigned",
                ["Subject"] = "Issue",
                ["CallerName"] = "Caller Name"
            };

    // Columns that must NOT appear in the create/edit forms
    // (the reference form does not collect them).
    private static readonly string[] HiddenFormFields =
    {
        "StartTime",
        "EndTime",
        "Remarks"
    };

    private static int ReadKpiCount(
        SqlDataReader reader,
        string column) =>
        reader[column] == DBNull.Value
            ? 0
            : Convert.ToInt32(reader[column]);

    // ============================================
    // SUPPORT LIST
    // Columns, KPI cards and grid columns are all
    // derived from live SQL Server metadata.
    // ============================================

    [HttpGet]
    public async Task<IActionResult> Index(string? search)
    {
        string? connectionString =
            _configuration.GetConnectionString(
                "DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return StatusCode(
                500,
                "Database connection string is missing.");
        }

        using SqlConnection connection =
            new SqlConnection(connectionString);

        await connection.OpenAsync();

        List<MasterColumnViewModel> fields =
            await _tableService.GetTableFieldsAsync(
                connection,
                TableName);

        ApplyConventions(fields);

        var model = new DailySupportViewModel
        {
            Fields = fields,
            FormFields = fields
                .Where(f => f.Editable)
                .ToList(),
            HasStatusKpi = fields.Any(
                f => f.Name.Equals(
                    "Status",
                    StringComparison.OrdinalIgnoreCase))
        };

        // ========================================
        // LOAD KPI COUNTS
        // Same four cards as the reference page:
        // Today Calls / Open Calls / Closed Today / Pending
        // ========================================

        bool hasSupportDate =
            FindField(fields, "SupportDate") != null;

        var kpiSelect = new List<string>();

        if (model.HasStatusKpi && hasSupportDate)
        {
            kpiSelect.Add(
                "SUM(CASE WHEN [SupportDate] " +
                "= CAST(GETDATE() AS DATE) " +
                "THEN 1 ELSE 0 END) AS TodayCalls");

            kpiSelect.Add(
                "SUM(CASE WHEN [Status] IN " +
                "('Open', 'In Progress', 'On Hold', 'Pending') " +
                "THEN 1 ELSE 0 END) AS OpenCalls");

            kpiSelect.Add(
                "SUM(CASE WHEN [Status] = 'Closed' " +
                "AND [SupportDate] = CAST(GETDATE() AS DATE) " +
                "THEN 1 ELSE 0 END) AS ClosedToday");

            kpiSelect.Add(
                "SUM(CASE WHEN [Status] = 'In Progress' " +
                "THEN 1 ELSE 0 END) AS PendingCount");
        }

        string kpiQuery =
            $"SELECT COUNT(*) AS TotalRecords";

        if (kpiSelect.Count > 0)
        {
            kpiQuery += ", " + string.Join(", ", kpiSelect);
        }

        kpiQuery += $" FROM [{TableName}]";

        using (SqlCommand kpiCommand =
            new SqlCommand(kpiQuery, connection))
        {
            kpiCommand.CommandTimeout = 0;

            using SqlDataReader kpiReader =
                await kpiCommand.ExecuteReaderAsync();

            if (await kpiReader.ReadAsync())
            {
                model.TotalRecords =
                    Convert.ToInt32(kpiReader["TotalRecords"]);

                if (kpiSelect.Count > 0)
                {
                    model.TodayCalls =
                        ReadKpiCount(kpiReader, "TodayCalls");

                    model.OpenCalls =
                        ReadKpiCount(kpiReader, "OpenCalls");

                    model.ClosedToday =
                        ReadKpiCount(kpiReader, "ClosedToday");

                    model.PendingCount =
                        ReadKpiCount(kpiReader, "PendingCount");
                }
            }
        }

        // ========================================
        // LOAD SUPPORT ROWS (dynamic columns)
        // ========================================

        model.Supports =
            await LoadGridAsync(
                connection,
                fields,
                search);

        // ========================================
        // LOAD DROPDOWNS + STATIC OPTIONS
        // ========================================

        (var dropdowns, var staticOptions) =
            await _tableService.LoadFormOptionsAsync(
                connection,
                TableName,
                model.FormFields);

        ViewBag.Dropdowns = dropdowns;
        ViewBag.StaticOptions = staticOptions;
        ViewBag.Search = search;

        return View(model);
    }

    private async Task<List<MasterRowViewModel>> LoadGridAsync(
        SqlConnection connection,
        List<MasterColumnViewModel> fields,
        string? search)
    {
        var selectParts = new List<string>();
        var joinClauses = new List<string>();
        var searchParts = new List<string>();
        var joinAliases =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        int joinIndex = 0;

        foreach (var field in fields)
        {
            if (field.LookupKey != null
                && _tableService.TryGetLookupDisplayColumn(
                    field.LookupKey,
                    out string displayColumn))
            {
                string joinKey =
                    $"{field.LookupKey}|{field.Name}";

                string alias =
                    joinAliases.TryGetValue(
                        joinKey,
                        out string? existing)
                        ? existing
                        : string.Empty;

                if (string.IsNullOrWhiteSpace(alias))
                {
                    alias = $"j{joinIndex++}";

                    joinAliases[joinKey] = alias;

                    joinClauses.Add(
                        $" INNER JOIN [{field.LookupKey}] {alias} " +
                        $"ON {alias}.[{field.LookupRefColumn ?? "Id"}] " +
                        $"= s.[{field.Name}]");
                }

                selectParts.Add(
                    $"{alias}.[{displayColumn}] AS [{field.Name}]");

                if (field.Editable
                    && field.Control == "select")
                {
                    selectParts.Add(
                        $"s.[{field.Name}] AS [{field.Name}_key]");
                }

                searchParts.Add(
                    $"{alias}.[{displayColumn}] " +
                    $"LIKE '%' + @Search + '%'");
            }
            else
            {
                if (field.Name.Equals(
                        "Channel",
                        StringComparison.OrdinalIgnoreCase)
                    && FindField(fields, "CallType") != null)
                {
                    // Reference table shows a single
                    // "Channel / Type" column.
                    selectParts.Add(
                        "CASE " +
                        "WHEN ISNULL(s.[Channel], '') = '' " +
                        "THEN ISNULL(s.[CallType], '') " +
                        "WHEN ISNULL(s.[CallType], '') = '' " +
                        "THEN s.[Channel] " +
                        "ELSE s.[Channel] + ' / ' + s.[CallType] " +
                        "END AS [Channel]");

                    searchParts.Add(
                        "s.[Channel] " +
                        "LIKE '%' + @Search + '%'");

                    searchParts.Add(
                        "s.[CallType] " +
                        "LIKE '%' + @Search + '%'");
                }
                else
                {
                    selectParts.Add($"s.[{field.Name}]");

                    if (DynamicTableService.IsText(field.SqlType))
                    {
                        searchParts.Add(
                            $"s.[{field.Name}] " +
                            $"LIKE '%' + @Search + '%'");
                    }
                }
            }
        }

        string idColumn =
            fields.FirstOrDefault(
                    f => f.IsPrimaryKey)
                ?.Name ?? "Id";

        if (fields.Any(f => f.Name.Equals(
                idColumn,
                StringComparison.OrdinalIgnoreCase)))
        {
            searchParts.Add(
                $"CAST(s.[{idColumn}] AS NVARCHAR(10)) " +
                $"LIKE '%' + @Search + '%'");
        }

        var sql = new StringBuilder();

        sql.Append("SELECT ");
        sql.Append(string.Join(", ", selectParts));
        sql.Append($" FROM [{TableName}] s");

        foreach (string join in joinClauses)
        {
            sql.Append(join);
        }

        if (searchParts.Count > 0)
        {
            sql.Append(" WHERE (@Search = '' OR ");
            sql.Append(string.Join(" OR ", searchParts));
            sql.Append(')');
        }
        else
        {
            sql.Append(" WHERE @Search = ''");
        }

        if (fields.Any(f => f.Name.Equals(
                idColumn,
                StringComparison.OrdinalIgnoreCase)))
        {
            sql.Append($" ORDER BY s.[{idColumn}] DESC");
        }
        else
        {
            sql.Append(" ORDER BY (SELECT NULL)");
        }

        var rows = new List<MasterRowViewModel>();

        using SqlCommand command =
            new SqlCommand(sql.ToString(), connection);

        command.CommandTimeout = 0;

        command.Parameters.Add(
            new SqlParameter(
                "@Search",
                System.Data.SqlDbType.NVarChar,
                100)
            {
                Value = search?.Trim() ?? ""
            });

        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var row = new MasterRowViewModel();

            foreach (var field in fields)
            {
                object value = reader[field.Name];

                row.Values[field.Name] =
                    DynamicTableService.FormatCellValue(
                        field,
                        value);

                if (field.LookupKey != null
                    && field.Editable
                    && field.Control == "select"
                    && _tableService.TryGetLookupDisplayColumn(
                        field.LookupKey,
                        out _))
                {
                    object? keyValue =
                        reader[$"{field.Name}_key"];

                    row.Values[$"{field.Name}_key"] =
                        keyValue == DBNull.Value
                            ? ""
                            : keyValue.ToString() ?? "";
                }
            }

            rows.Add(row);
        }

        return rows;
    }

    private async Task<(
        Dictionary<string, List<LookupOptionViewModel>> Dropdowns,
        Dictionary<string, string[]> StaticOptions)>
        LoadOptionsAsync(
            SqlConnection connection,
            List<MasterColumnViewModel> formFields) =>

        await _tableService.LoadFormOptionsAsync(
            connection,
            TableName,
            formFields);

    // ============================================
    // CREATE
    // ============================================

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        string? connectionString =
            _configuration.GetConnectionString(
                "DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return StatusCode(
                500,
                "Database connection string is missing.");
        }

        using SqlConnection connection =
            new SqlConnection(connectionString);

        await connection.OpenAsync();

        List<MasterColumnViewModel> fields =
            await _tableService.GetTableFieldsAsync(
                connection,
                TableName);

        ApplyConventions(fields);

        var model = new DailySupportViewModel
        {
            Fields = fields,
            FormFields = fields
                .Where(f => f.Editable)
                .ToList()
        };

        (var dropdowns, var staticOptions) =
            await LoadOptionsAsync(
                connection,
                model.FormFields);

        ViewBag.Dropdowns = dropdowns;
        ViewBag.StaticOptions = staticOptions;
        ViewBag.TicketNo = await GenerateTicketNoAsync(connection);
        ViewBag.CurrentUserId = DynamicTableService.GetLoggedInUserId(User);
        var clientsWithMobile = new List<LookupOptionViewModel>();
        try
        {
            string clientsQuery = @"SELECT Id, ClientName, ISNULL(MobileNo,'') AS MobileNo FROM ClientMaster WHERE (IsActive=1 OR IsActive IS NULL) ORDER BY ClientName";
            using (SqlCommand cmd = new SqlCommand(clientsQuery, connection))
            {
                using SqlDataReader r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    clientsWithMobile.Add(new LookupOptionViewModel
                    {
                        Id = Convert.ToInt32(r["Id"]),
                        Name = r["ClientName"].ToString() ?? "",
                        Mobile = r["MobileNo"].ToString() ?? ""
                    });
                }
            }
        }
        catch { }
        ViewBag.ClientsWithMobile = clientsWithMobile;

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        IFormCollection form)
    {

        string? connectionString =
            _configuration.GetConnectionString(
                "DefaultConnection");

        string? loggedInUserId =
            DynamicTableService.GetLoggedInUserId(User);

        using SqlConnection connection =
            new SqlConnection(connectionString);

        await connection.OpenAsync();

        List<MasterColumnViewModel> fields =
            await _tableService.GetTableFieldsAsync(
                connection,
                TableName);

        ApplyConventions(fields);

        var formFields =
            fields.Where(f => f.Editable)
                .ToList();

        // ========================================
        // GENERIC VALIDATION
        // ========================================

        string? validationError =
            _tableService.ValidateRequiredFields(
                formFields,
                form);

        // ========================================
        // BUSINESS RULES (status/time/remarks...)
        // ========================================

        var errors =
            ValidateBusinessRules(
                fields,
                form,
                null,
                null,
                out Dictionary<string, string> overrides,
                true);

        try
        {
            string existingTicket = form["TicketNo"].ToString().Trim();
            using (SqlCommand check = new SqlCommand("SELECT COUNT(*) FROM [DailySupport] WHERE [TicketNo] = @t", connection))
            {
                check.Parameters.AddWithValue("@t", existingTicket);
                int c = (int)await check.ExecuteScalarAsync();
                if (c == 0 && !string.IsNullOrWhiteSpace(existingTicket))
                {
                    overrides["TicketNo"] = existingTicket;
                }
                else
                {
                    overrides["TicketNo"] = await GenerateTicketNoAsync(connection);
                }
            }
        }
        catch
        {
            overrides["TicketNo"] = await GenerateTicketNoAsync(connection);
        }
        if (!overrides.ContainsKey("SupportType") || string.IsNullOrWhiteSpace(overrides["SupportType"]))
        {
            string st = form["SupportType"].ToString().Trim();
            overrides["SupportType"] = string.IsNullOrWhiteSpace(st) ? "Client Query" : st;
        }

        if (!string.IsNullOrWhiteSpace(validationError))
        {
            errors.Clear();
            errors["_"] = validationError;
        }

        if (errors.Count > 0)
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message = errors.Values.First()
                });
            }

            TempData["Error"] = errors.Values.First();

            return RedirectToAction(nameof(Create));
        }

        if (fields.Any(f => f.AutoWrite == "auth-user")
            && string.IsNullOrWhiteSpace(loggedInUserId))
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message =
                        "User authentication is required."
                });
            }

            return Unauthorized();
        }

        // ========================================
        // BUILD INSERT (metadata driven)
        // ========================================

        var insertColumns = new List<string>();
        var placeholders = new List<string>();

        using SqlCommand command =
            new SqlCommand
            {
                Connection = connection,
                CommandTimeout = 0
            };

        foreach (var field in formFields)
        {
            if (field.AutoWrite != null)
            {
                continue;
            }

            string raw =
                overrides.TryGetValue(
                    field.Name,
                    out string? inline)
                    ? inline
                    : form[field.Name].ToString();

            insertColumns.Add(field.Name);
            placeholders.Add($"@{field.Name}");

            _tableService.AddParameter(
                command,
                field,
                raw);
        }

        foreach (var auto in fields.Where(
                     f => f.AutoWrite != null))
        {
            if (auto.AutoWrite == "auth-user"
                && !string.IsNullOrWhiteSpace(loggedInUserId))
            {
                insertColumns.Add(auto.Name);
                placeholders.Add($"@{auto.Name}");

                command.Parameters.Add(
                    new SqlParameter(
                        $"@{auto.Name}",
                        System.Data.SqlDbType.Int)
                    {
                        Value =
                            Convert.ToInt32(loggedInUserId)
                    });
            }
            else if (auto.AutoWrite == "true")
            {
                insertColumns.Add(auto.Name);
                placeholders.Add("1");
            }
            else if (auto.AutoWrite == "now")
            {
                insertColumns.Add(auto.Name);
                placeholders.Add("GETDATE()");
            }
        }

        command.CommandText =
            $"INSERT INTO [{TableName}] " +
            $"({string.Join(", ", insertColumns)}) " +
            $"VALUES ({string.Join(", ", placeholders)})";

        await command.ExecuteNonQueryAsync();

        if (IsAjax)
        {
            return Json(new
            {
                success = true,
                message =
                    "Support record added successfully."
            });
        }

        TempData["Success"] =
            "Support record added successfully.";

        return RedirectToAction(nameof(Index));
    }

    // ============================================
    // EDIT
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id)
    {
        var form = Request.Form;

        string? connectionString =
            _configuration.GetConnectionString(
                "DefaultConnection");

        using SqlConnection connection =
            new SqlConnection(connectionString);

        await connection.OpenAsync();

        List<MasterColumnViewModel> fields =
            await _tableService.GetTableFieldsAsync(
                connection,
                TableName);

        ApplyConventions(fields);

        var editFields =
            fields.Where(
                    f => f.Editable
                         && !f.CreateOnly)
                .ToList();

        string idColumn =
            fields.FirstOrDefault(
                    f => f.IsPrimaryKey)
                ?.Name ?? "Id";

        (bool found,
         string existingStatus,
         DateTime? existingStart,
         DateTime? existingEnd) =
            await ReadCurrentAsync(
                connection,
                fields,
                id);

        if (!found)
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message =
                        "Support record not found."
                });
            }

            TempData["Error"] =
                "Support record not found.";

            return RedirectToAction(
                "Index",
                "DailySupport");
        }

        // ========================================
        // GENERIC VALIDATION
        // ========================================

        string? validationError =
            _tableService.ValidateRequiredFields(
                editFields,
                form);

        // ========================================
        // BUSINESS RULES
        // ========================================

        var errors =
            ValidateBusinessRules(
                fields,
                form,
                existingStart,
                existingEnd,
                out Dictionary<string, string> overrides,
                false);

        if (fields.Any(f => f.Name.Equals(
                "Status",
                StringComparison.OrdinalIgnoreCase)))
        {
            string newStatus =
                form["Status"].ToString().Trim();

            if (!string.Equals(
                    existingStatus,
                    newStatus,
                    StringComparison.OrdinalIgnoreCase)
                && !IsAllowedTransition(
                    existingStatus,
                    newStatus))
            {
                errors["Status"] =
                    $"Status transition from "
                    + $"'{existingStatus}' to '{newStatus}'"
                    + " is not allowed.";
            }
        }

        if (!string.IsNullOrWhiteSpace(validationError))
        {
            errors.Clear();
            errors["_"] = validationError;
        }

        if (errors.Count > 0)
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message = errors.Values.First()
                });
            }

            TempData["Error"] = errors.Values.First();

            return RedirectToAction(
                "Index",
                "DailySupport");
        }

        // ========================================
        // BUILD UPDATE (metadata driven)
        // ========================================

        var setParts = new List<string>();

        using SqlCommand command =
            new SqlCommand
            {
                Connection = connection,
                CommandTimeout = 0
            };

        command.Parameters.Add(
            new SqlParameter(
                "@Id",
                System.Data.SqlDbType.Int)
            {
                Value = id
            });

        foreach (var field in editFields)
        {
            if (field.AutoWrite != null)
            {
                continue;
            }

            string raw =
                overrides.TryGetValue(
                    field.Name,
                    out string? inline)
                    ? inline
                    : form[field.Name].ToString();

            setParts.Add($"[{field.Name}] = @{field.Name}");

            _tableService.AddParameter(
                command,
                field,
                raw);
        }

        foreach (var auto in fields.Where(
                     f => f.AutoWrite == "true"))
        {
            setParts.Add($"[{auto.Name}] = 1");
        }

        command.CommandText =
            $"UPDATE [{TableName}] " +
            $"SET {string.Join(", ", setParts)} " +
            $"WHERE [{idColumn}] = @Id";

        int rows = await command.ExecuteNonQueryAsync();

        if (rows == 0)
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message =
                        "Support record not found."
                });
            }

            TempData["Error"] =
                "Support record not found.";
        }
        else
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = true,
                    message =
                        "Support record updated successfully."
                });
            }

            TempData["Success"] =
                "Support record updated successfully.";
        }

        return RedirectToAction(nameof(Index));
    }

    // ============================================
    // QUICK ACTIONS (Start / Pause / Complete / Cancel)
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(int id)
    {
        var result =
            await RunQuickActionAsync(id,
                allowedFrom: new[] { "Open", "Pending" },
                allowError:
                    "Cannot start support. "
                    + "Allowed only from Open or Pending.",
                statusValue: "In Progress",
                startFresh: true,
                startMessage: "Support started.",
                resumeMessage: "Support resumed.");

        return Done(result);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pause(int id)
    {
        var result =
            await RunQuickActionAsync(id,
                allowedFrom: new[] { "In Progress" },
                allowError:
                    "Cannot pause support. "
                    + "Allowed only from In Progress.",
                statusValue: "Pending",
                startFresh: false,
                startMessage: "Support paused. Status updated to Pending.",
                resumeMessage: "");

        return Done(result);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(int id)
    {
        var result =
            await RunQuickActionAsync(id,
                allowedFrom: new[] { "In Progress" },
                allowError:
                    "Cannot complete support. "
                    + "Allowed only from In Progress.",
                statusValue: "Closed",
                startFresh: false,
                startMessage:
                    "Support closed. Status updated to Closed.",
                resumeMessage: "");

        return Done(result);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var result =
            await RunQuickActionAsync(id,
                allowedFrom: new[] { "Open", "In Progress" },
                allowError:
                    "Cannot hold support. "
                    + "Allowed only from Open or In Progress.",
                statusValue: "On Hold",
                startFresh: false,
                startMessage:
                    "Support put on hold. Status updated to On Hold.",
                resumeMessage: "");

        return Done(result);
    }

    private async Task<(bool Success, string Message)>
        RunQuickActionAsync(
            int id,
            string[] allowedFrom,
            string allowError,
            string statusValue,
            bool startFresh,
            string startMessage,
            string resumeMessage)
    {
        string? connectionString =
            _configuration.GetConnectionString(
                "DefaultConnection");

        using SqlConnection connection =
            new SqlConnection(connectionString);

        await connection.OpenAsync();

        List<MasterColumnViewModel> fields =
            await _tableService.GetTableFieldsAsync(
                connection,
                TableName);

        var columns =
            new HashSet<string>(
                fields.Select(f => f.Name),
                StringComparer.OrdinalIgnoreCase);

        string idColumn =
            fields.FirstOrDefault(
                    f => f.IsPrimaryKey)
                ?.Name ?? "Id";

        (bool found,
         string existingStatus,
         DateTime? existingStart,
         DateTime? existingEnd) =
            await ReadCurrentAsync(
                connection,
                fields,
                id);

        if (!found)
        {
            return (false, "Support record not found.");
        }

        bool fromOpen =
            string.Equals(
                existingStatus,
                "Open",
                StringComparison.OrdinalIgnoreCase);

        if (!allowedFrom.Contains(
                existingStatus,
                StringComparer.OrdinalIgnoreCase))
        {
            return (false, allowError);
        }

        var setParts = new List<string>();

        if (columns.Contains("Status"))
        {
            setParts.Add("[Status] = '" + statusValue + "'");
        }

        if (startFresh
            && fromOpen
            && columns.Contains("StartTime"))
        {
            setParts.Add("[StartTime] = GETDATE()");
        }

        if (statusValue == "Closed"
            && columns.Contains("EndTime"))
        {
            setParts.Add("[EndTime] = GETDATE()");
        }

        if (setParts.Count == 0)
        {
            return (false, "Cannot update support:"
                + " required columns are missing.");
        }

        string query = $@"
            UPDATE [{TableName}]
            SET {string.Join(", ", setParts)}
            WHERE [{idColumn}] = @Id";

        using SqlCommand command =
            new SqlCommand(query, connection);

        command.CommandTimeout = 0;
        command.Parameters.AddWithValue("@Id", id);

        int rows = await command.ExecuteNonQueryAsync();

        if (rows == 0)
        {
            return (false, "Support record not found.");
        }

        if (statusValue == "In Progress")
        {
            return (true, fromOpen ? startMessage : resumeMessage);
        }

        return (true, startMessage);
    }

    private IActionResult Done(
        (bool Success, string Message) result)
    {
        if (IsAjax)
        {
            return Json(new
            {
                success = result.Success,
                message = result.Message
            });
        }

        if (result.Success)
        {
            TempData["Success"] = result.Message;
        }
        else
        {
            TempData["Error"] = result.Message;
        }

        return RedirectToAction("Index", "DailySupport");
    }

    // ============================================
    // DELETE
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        string? connectionString =
            _configuration.GetConnectionString(
                "DefaultConnection");

        using SqlConnection connection =
            new SqlConnection(connectionString);

        await connection.OpenAsync();

        string idColumn =
            await _tableService
                .GetPrimaryKeyColumnAsync(
                    connection,
                    TableName);

        string query = $@"
            DELETE FROM [{TableName}]
            WHERE [{idColumn}] = @Id";

        using SqlCommand command =
            new SqlCommand(query, connection);

        command.CommandTimeout = 0;
        command.Parameters.AddWithValue("@Id", id);

        int rows = await command.ExecuteNonQueryAsync();

        if (rows == 0)
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message =
                        "Support record not found."
                });
            }

            TempData["Error"] =
                "Support record not found.";
        }
        else
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = true,
                    message =
                        "Support record deleted successfully."
                });
            }

            TempData["Success"] =
                "Support record deleted successfully.";
        }

        return RedirectToAction("Index", "DailySupport");
    }

    // ============================================
    // HELPERS
    // ============================================

    private static void ApplyConventions(
        List<MasterColumnViewModel> fields)
    {
        foreach (var field in fields)
        {
            // EntryOn is always server-written (GETDATE()),
            // even if the live table has no column default.
            if (field.Name.Equals(
                    "EntryOn",
                    StringComparison.OrdinalIgnoreCase))
            {
                field.Editable = false;
                field.AutoWrite = "now";
                field.Control = "input";
                field.InputType = "text";
            }

            // Assign To (Users) dropdown for DailySupport
            if (field.Name.Equals(
                    "UserId",
                    StringComparison.OrdinalIgnoreCase))
            {
                field.Editable = true;
                field.AutoWrite = null;
                field.Required = true;
                field.Type = "dropdown";
                field.SortType = "text";
                field.Control = "select";
                field.InputType = "text";
                field.Display = "Assign To";
                field.LookupKey = "LoginUsers";
                field.LookupRefColumn = "Id";
            }
        }

        // ============================================
        // TABLE LAYOUT (same columns, order and labels
        // as the reference Daily Support table)
        // ============================================

        foreach (var field in fields)
        {
            int index = LayoutIndex(field.Name);

            field.ShowInTable = index >= 0;

            if (index >= 0
                && TableLabels.TryGetValue(
                    field.Name,
                    out string? label))
            {
                field.Display = label;
            }
        }

        // ============================================
        // FORM FIELDS (reference form does not collect
        // start/end time or remarks)
        // ============================================

        foreach (string name in HiddenFormFields)
        {
            MasterColumnViewModel? field =
                FindField(fields, name);

            if (field != null)
            {
                field.Editable = false;
                field.ShowInTable = false;
            }
        }

        // SupportType is posted as the hidden "Client Query"
        // value on Create; it must never be edited later.
        MasterColumnViewModel? supportType =
            FindField(fields, "SupportType");

        if (supportType != null)
        {
            supportType.CreateOnly = true;
        }

        // Reorder: grid columns first (reference order),
        // everything else keeps its original relative order.
        var reordered =
            fields
                .Where(f => LayoutIndex(f.Name) >= 0)
                .OrderBy(f => LayoutIndex(f.Name))
                .Concat(
                    fields.Where(
                        f => LayoutIndex(f.Name) < 0))
                .ToList();

        fields.Clear();
        fields.AddRange(reordered);
    }

    private static int LayoutIndex(string name)
    {
        for (int i = 0; i < TableLayout.Length; i++)
        {
            if (TableLayout[i].Equals(
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsAllowedTransition(
        string from,
        string to)
    {
        if (string.Equals(
                from,
                to,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (string rule in AllowedTransitions)
        {
            string[] parts =
                rule.Split('|');

            if (string.Equals(
                    from,
                    parts[0].Trim(),
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    to,
                    parts[1].Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static MasterColumnViewModel? FindField(
        IEnumerable<MasterColumnViewModel> fields,
        string name)
    {
        return fields.FirstOrDefault(
            f => f.Name.Equals(
                name,
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsStatus(
        string current,
        string check) =>
        string.Equals(
            current,
            check,
            StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> ValidateBusinessRules(
        IEnumerable<MasterColumnViewModel> fields,
        IFormCollection form,
        DateTime? existingStart,
        DateTime? existingEnd,
        out Dictionary<string, string> overrides,
        bool createMode = false)
    {
        var errors =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        overrides =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        void AddError(string name, string message)
        {
            if (!errors.ContainsKey(name))
            {
                errors[name] = message;
            }
        }

        // ---- Support Type / Visit Type: "Other" composition ----

        var supportType = FindField(fields, "SupportType");

        if (supportType != null)
        {
            string raw =
                form["SupportType"].ToString().Trim();

            if (IsStatus(raw, "Other"))
            {
                string otherText =
                    form["OtherSupportType"]
                        .ToString()
                        .Trim();

                if (string.IsNullOrWhiteSpace(otherText))
                {
                    AddError(
                        "OtherSupportType",
                        "Other Support Type is required.");
                }
                else if (otherText.Length > 92)
                {
                    AddError(
                        "OtherSupportType",
                        "Other Support Type is too long"
                            + " (maximum 92 characters).");
                }
                else
                {
                    overrides["SupportType"] =
                        ComposeOther(raw, otherText);
                }
            }
        }

        // ---- Status based rules ----

        DateTime supportDate = default;

        var supportDateField = FindField(fields, "SupportDate");

        if (supportDateField != null
            && !DateTime.TryParse(
                form["SupportDate"].ToString(),
                out supportDate))
        {
            supportDate = DateTime.Today;
        }

        // ---- Start / End Time ----

        // The reference form does not collect start/end/remarks
        // anymore (those columns are hidden), so there is no
        // requirement to enforce here - values already stored on
        // the row are simply never touched by the UPDATE.

        DateTime? startTime = existingStart;

        DateTime? endTime = existingEnd;

        string startRaw =
            form["StartTime"].ToString().Trim();

        if (DateTime.TryParse(
                startRaw,
                out DateTime parsedStart))
        {
            startTime = parsedStart;
        }

        string endRaw =
            form["EndTime"].ToString().Trim();

        if (DateTime.TryParse(
                endRaw,
                out DateTime parsedEnd))
        {
            endTime = parsedEnd;
        }

        // ---- End >= Start ----

        if (startTime.HasValue
            && endTime.HasValue
            && endTime.Value.TimeOfDay
                < startTime.Value.TimeOfDay)
        {
            AddError(
                "EndTime",
                "End time cannot be earlier"
                    + " than start time.");
        }

        // ---- Follow-up date >= Support date ----

        var followUpField =
            FindField(fields, "FollowUpDate");

        if (followUpField != null)
        {
            string followUpRaw =
                form["FollowUpDate"].ToString().Trim();

            if (DateTime.TryParse(
                    followUpRaw,
                    out DateTime followUpDate)
                && followUpDate.Date
                    < supportDate.Date)
            {
                AddError(
                    "FollowUpDate",
                    "Follow-up date cannot be earlier"
                        + " than support date.");
            }
        }

        return errors;
    }

    private static string ComposeOther(
        string raw,
        string otherText)
    {
        string value = "Other - " + otherText;

        if (value.Length > 100)
        {
            value = value.Substring(0, 100);
        }

        return value;
    }

    private static async Task<(bool found, string status,
        DateTime? startTime, DateTime? endTime)>
        ReadCurrentAsync(
            SqlConnection connection,
            List<MasterColumnViewModel> fields,
            int id)
    {
        bool hasStatus = FindField(fields, "Status") != null;
        bool hasStart = FindField(fields, "StartTime") != null;
        bool hasEnd = FindField(fields, "EndTime") != null;

        string idColumn =
            fields.FirstOrDefault(
                    f => f.IsPrimaryKey)
                ?.Name ?? "Id";

        var select = new List<string> { idColumn };

        if (hasStatus)
        {
            select.Add("Status");
        }

        if (hasStart)
        {
            select.Add("StartTime");
        }

        if (hasEnd)
        {
            select.Add("EndTime");
        }

        string query = $@"
            SELECT {string.Join(", ", select)}
            FROM [{TableName}]
            WHERE [{idColumn}] = @Id";

        using SqlCommand command =
            new SqlCommand(query, connection);

        command.CommandTimeout = 0;
        command.Parameters.AddWithValue("@Id", id);

        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return (false, "", null, null);
        }

        string status =
            hasStatus
                ? reader["Status"].ToString() ?? ""
                : "";

        DateTime? startTime =
            hasStart
                ? ReadAsDateTime(reader["StartTime"])
                : null;

        DateTime? endTime =
            hasEnd
                ? ReadAsDateTime(reader["EndTime"])
                : null;

        return (true, status, startTime, endTime);
    }

    private static DateTime? ReadAsDateTime(object value)
    {
        if (value == DBNull.Value
            || value == null)
        {
            return null;
        }

        if (value is TimeSpan timeSpan)
        {
            return DateTime.Today.Add(timeSpan);
        }

        return Convert.ToDateTime(value);
    }

    private static async Task<string> GenerateTicketNoAsync(SqlConnection connection)
    {
        string prefix = "TKT-" + DateTime.Today.ToString("ddMMyy");
        string query = @"SELECT COUNT(*) FROM [DailySupport] WHERE [TicketNo] LIKE @Prefix + '-%'";
        using var cmd = new SqlCommand(query, connection) { CommandTimeout = 0 };
        cmd.Parameters.AddWithValue("@Prefix", prefix);
        int count = 0;
        try
        {
            object? res = await cmd.ExecuteScalarAsync();
            count = res == DBNull.Value ? 0 : Convert.ToInt32(res);
        }
        catch { count = 0; }
        return $"{prefix}-{(count + 1):D2}";
    }

    [HttpGet]
    public async Task<IActionResult> SearchClientByMobile(string q)
    {
        string? connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString)) return Json(Array.Empty<object>());
        using SqlConnection connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var list = new List<object>();
        try
        {
            string query = @"SELECT TOP 5 Id, ClientName, ISNULL(MobileNo,'') AS MobileNo FROM ClientMaster WHERE (MobileNo LIKE '%' + @m + '%' OR ClientName LIKE '%' + @m + '%') AND (IsActive=1 OR IsActive IS NULL) ORDER BY ClientName";
            using SqlCommand cmd = new SqlCommand(query, connection) { CommandTimeout = 0 };
            cmd.Parameters.AddWithValue("@m", q?.Trim() ?? "");
            using SqlDataReader r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                list.Add(new { ClientId = Convert.ToInt32(r["Id"]), ClientName = r["ClientName"].ToString(), MobileNo = r["MobileNo"].ToString() });
            }
        }
        catch { }
        return Json(new { clients = list });
    }
}