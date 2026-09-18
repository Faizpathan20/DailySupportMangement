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
        "Open|Cancelled",
        "In Progress|Pending",
        "In Progress|Completed",
        "In Progress|Cancelled",
        "Pending|In Progress",
        "Pending|Completed",
        "Pending|Cancelled",
        "Completed|Cancelled"
    };

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
        // ========================================

        var kpiSelect = new List<string>();

        if (model.HasStatusKpi)
        {
            kpiSelect.Add(
                "SUM(CASE WHEN [Status] = 'Open' " +
                "THEN 1 ELSE 0 END) AS OpenCount");

            kpiSelect.Add(
                "SUM(CASE WHEN [Status] = 'In Progress' " +
                "THEN 1 ELSE 0 END) AS InProgressCount");

            kpiSelect.Add(
                "SUM(CASE WHEN [Status] = 'Completed' " +
                "THEN 1 ELSE 0 END) AS CompletedCount");
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

                model.OpenCount =
                    kpiReader["OpenCount"] == DBNull.Value
                        ? 0
                        : Convert.ToInt32(kpiReader["OpenCount"]);

                model.InProgressCount =
                    kpiReader["InProgressCount"] == DBNull.Value
                        ? 0
                        : Convert.ToInt32(kpiReader["InProgressCount"]);

                model.CompletedCount =
                    kpiReader["CompletedCount"] == DBNull.Value
                        ? 0
                        : Convert.ToInt32(kpiReader["CompletedCount"]);
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
                selectParts.Add($"s.[{field.Name}]");

                if (DynamicTableService.IsText(field.SqlType))
                {
                    searchParts.Add(
                        $"s.[{field.Name}] " +
                        $"LIKE '%' + @Search + '%'");
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
            sql.Append($" ORDER BY s.[{idColumn}] ASC");
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
                out Dictionary<string, string> overrides);

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
                out Dictionary<string, string> overrides);

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
                statusValue: "Completed",
                startFresh: false,
                startMessage:
                    "Support completed. Status updated to Completed.",
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
                    "Cannot cancel support. "
                    + "Allowed only from Open or In Progress.",
                statusValue: "Cancelled",
                startFresh: false,
                startMessage:
                    "Support cancelled. Status updated to Cancelled.",
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

        if (statusValue == "Completed"
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
        }
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
        out Dictionary<string, string> overrides)
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

        string status =
            form["Status"].ToString().Trim();

        DateTime supportDate = default;

        var supportDateField = FindField(fields, "SupportDate");

        if (supportDateField != null
            && !DateTime.TryParse(
                form["SupportDate"].ToString(),
                out supportDate))
        {
            supportDate = DateTime.Today;
        }

        // ---- Start Time ----

        DateTime? startTime = null;

        var startTimeField = FindField(fields, "StartTime");

        if (startTimeField != null)
        {
            string startRaw =
                form["StartTime"].ToString().Trim();

            if (string.IsNullOrWhiteSpace(startRaw))
            {
                if (IsStatus(status, "In Progress")
                    || IsStatus(status, "Pending")
                    || IsStatus(status, "Completed"))
                {
                    if (existingStart.HasValue)
                    {
                        overrides["StartTime"] =
                            existingStart.Value.ToString("HH:mm");

                        startTime = existingStart;
                    }
                    else
                    {
                        AddError(
                            "StartTime",
                            "Start Time is required for this status.");
                    }
                }
            }
            else if (DateTime.TryParse(startRaw, out DateTime parsed))
            {
                startTime = parsed;
            }
        }

        // ---- End Time ----

        DateTime? endTime = null;

        var endTimeField = FindField(fields, "EndTime");

        if (endTimeField != null)
        {
            string endRaw =
                form["EndTime"].ToString().Trim();

            if (IsStatus(status, "Pending")
                && !string.IsNullOrWhiteSpace(endRaw))
            {
                AddError(
                    "EndTime",
                    "End Time must be blank for Pending status.");
            }
            else if (string.IsNullOrWhiteSpace(endRaw))
            {
                if (IsStatus(status, "Completed"))
                {
                    if (existingEnd.HasValue)
                    {
                        overrides["EndTime"] =
                            existingEnd.Value.ToString("HH:mm");

                        endTime = existingEnd;
                    }
                    else
                    {
                        AddError(
                            "EndTime",
                            "End Time is required for Completed status.");
                    }
                }
            }
            else if (DateTime.TryParse(endRaw, out DateTime parsed))
            {
                endTime = parsed;
            }
        }

        // ---- Remarks ----

        var remarksField = FindField(fields, "Remarks");

        if (remarksField != null
            && (IsStatus(status, "Pending")
                || IsStatus(status, "Completed")
                || IsStatus(status, "Cancelled"))
            && string.IsNullOrWhiteSpace(
                form["Remarks"].ToString()))
        {
            AddError(
                "Remarks",
                "Remarks is required for this status.");
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
}