using System.Text;
using Master.Configuration;
using Master.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Master.Controllers;

[Authorize]
public class ClientVisitingController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly DynamicTableService _tableService;

    public ClientVisitingController(
        IConfiguration configuration,
        DynamicTableService tableService)
    {
        _configuration = configuration;
        _tableService = tableService;
    }

    private bool IsAjax =>
        DynamicTableService.IsAjaxRequest(Request);

    private const string TableName = "ClientVisiting";

    // ============================================
    // VISIT LIST
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

        var model = new ClientVisitingViewModel
        {
            Fields = fields,
            FormFields = fields
                .Where(f => f.Editable)
                .ToList(),
            HasStatusKpi = fields.Any(
                f => f.Name.Equals(
                    "Status",
                    StringComparison.OrdinalIgnoreCase)),
            Meetings =
                await LoadMeetingsAsync(connection)
        };

        // ========================================
        // LOAD KPI COUNTS
        // ========================================

        var kpiSelect = new List<string>();

        if (model.HasStatusKpi)
        {
            kpiSelect.Add(
                "SUM(CASE WHEN [Status] = 'Planned' " +
                "THEN 1 ELSE 0 END) AS PlannedCount");

            kpiSelect.Add(
                "SUM(CASE WHEN [Status] = 'Completed' " +
                "THEN 1 ELSE 0 END) AS CompletedCount");

            kpiSelect.Add(
                "SUM(CASE WHEN [Status] = 'Cancelled' " +
                "THEN 1 ELSE 0 END) AS CancelledCount");
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

                model.PlannedCount =
                    kpiReader["PlannedCount"] == DBNull.Value
                        ? 0
                        : Convert.ToInt32(kpiReader["PlannedCount"]);

                model.CompletedCount =
                    kpiReader["CompletedCount"] == DBNull.Value
                        ? 0
                        : Convert.ToInt32(kpiReader["CompletedCount"]);

                model.CancelledCount =
                    kpiReader["CancelledCount"] == DBNull.Value
                        ? 0
                        : Convert.ToInt32(kpiReader["CancelledCount"]);
            }
        }

        // ========================================
        // LOAD VISIT ROWS (dynamic columns)
        // ========================================

        model.Visits =
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

        var model = new ClientVisitingViewModel
        {
            Fields = fields,
            FormFields = fields
                .Where(f => f.Editable)
                .ToList(),
            Meetings =
                await LoadMeetingsAsync(connection)
        };

        (var dropdowns, var staticOptions) =
            await _tableService.LoadFormOptionsAsync(
                connection,
                TableName,
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
        // BUSINESS RULES (visit type / meeting / dates)
        // ========================================

        var errors =
            ValidateBusinessRules(
                fields,
                form,
                out Dictionary<string, string> overrides,
                out int previousMeetingId);

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

        // ---- Previous Meeting (meeting chain) ----

        if (fields.Any(f => f.Name.Equals(
                "PreviousMeetingId",
                StringComparison.OrdinalIgnoreCase)))
        {
            insertColumns.Add("PreviousMeetingId");
            placeholders.Add("@PreviousMeetingId");

            command.Parameters.Add(
                new SqlParameter(
                    "@PreviousMeetingId",
                    System.Data.SqlDbType.Int)
                {
                    Value = previousMeetingId
                });
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
                    "Visit record added successfully."
            });
        }

        TempData["Success"] =
            "Visit record added successfully.";

        return RedirectToAction(nameof(Index));
    }

    // ============================================
    // EDIT
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        int id)
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
                out Dictionary<string, string> overrides,
                out int previousMeetingId);

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
                "ClientVisiting");
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

        // ---- Previous Meeting (only when modal posted
        //       the hidden MeetingMode select) ----

        if (fields.Any(f => f.Name.Equals(
                "PreviousMeetingId",
                StringComparison.OrdinalIgnoreCase))
            && form.ContainsKey("MeetingMode"))
        {
            setParts.Add("[PreviousMeetingId] = @PreviousMeetingId");

            command.Parameters.Add(
                new SqlParameter(
                    "@PreviousMeetingId",
                    System.Data.SqlDbType.Int)
                {
                    Value = previousMeetingId
                });
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
                        "Visit record not found."
                });
            }

            TempData["Error"] =
                "Visit record not found.";
        }
        else
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = true,
                    message =
                        "Visit record updated successfully."
                });
            }

            TempData["Success"] =
                "Visit record updated successfully.";
        }

        return RedirectToAction("Index", "ClientVisiting");
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
                        "Visit record not found."
                });
            }

            TempData["Error"] =
                "Visit record not found.";
        }
        else
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = true,
                    message =
                        "Visit record deleted successfully."
                });
            }

            TempData["Success"] =
                "Visit record deleted successfully.";
        }

        return RedirectToAction("Index", "ClientVisiting");
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

            // PreviousMeetingId is a self-reference handled by
            // the MeetingMode block; never render it as a FK
            // dropdown (no display column exists) and never
            // expose it as an editable form field.
            if (field.Name.Equals(
                    "PreviousMeetingId",
                    StringComparison.OrdinalIgnoreCase))
            {
                field.Editable = false;
                field.Type = "number";
                field.SortType = "num";
                field.Control = "input";
                field.InputType = "number";
                field.LookupKey = null;
            }
        }
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

    private static bool EqualsIgnoreCase(
        string? value,
        string compare) =>
        string.Equals(
            value,
            compare,
            StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> ValidateBusinessRules(
        IEnumerable<MasterColumnViewModel> fields,
        IFormCollection form,
        out Dictionary<string, string> overrides,
        out int previousMeetingId)
    {
        var errors =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        overrides =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        previousMeetingId = 0;

        void AddError(string name, string message)
        {
            if (!errors.ContainsKey(name))
            {
                errors[name] = message;
            }
        }

        // ---- Visit Type: "Other" composition ----

        if (FindField(fields, "VisitType") != null)
        {
            string raw =
                form["VisitType"].ToString().Trim();

            if (EqualsIgnoreCase(raw, "Other"))
            {
                string otherText =
                    form["OtherVisitType"]
                        .ToString()
                        .Trim();

                if (string.IsNullOrWhiteSpace(otherText))
                {
                    AddError(
                        "OtherVisitType",
                        "Other Visit Type is required.");
                }
                else if (otherText.Length > 92)
                {
                    AddError(
                        "OtherVisitType",
                        "Other Visit Type is too long"
                            + " (maximum 92 characters).");
                }
                else
                {
                    overrides["VisitType"] =
                        ComposeOther(raw, otherText);
                }
            }
        }

        // ---- Visit Date + Follow-up ----

        var visitDateField =
            FindField(fields, "VisitDate");

        DateTime visitDate = default;

        if (visitDateField != null
            && !DateTime.TryParse(
                form["VisitDate"].ToString(),
                out visitDate))
        {
            visitDate = DateTime.Today;
        }

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
                    < visitDate.Date)
            {
                AddError(
                    "FollowUpDate",
                    "Follow-up date cannot be earlier"
                        + " than visit date.");
            }
        }

        // ---- Meeting chain (PreviousMeetingId) ----

        if (FindField(fields, "PreviousMeetingId") != null)
        {
            string meetingMode =
                form["MeetingMode"].ToString().Trim();

            string previousMeetingRaw =
                form["PreviousMeetingId"]
                    .ToString()
                    .Trim();

            if (EqualsIgnoreCase(
                    meetingMode,
                    "Add to Existing Meeting"))
            {
                if (!int.TryParse(
                        previousMeetingRaw,
                        out int selectedMeetingId)
                    || selectedMeetingId <= 0)
                {
                    AddError(
                        "PreviousMeetingId",
                        "Please select an existing meeting.");
                }
                else
                {
                    string idRaw =
                        form["id"].ToString().Trim();

                    if (int.TryParse(
                            idRaw,
                            out int postedId)
                        && postedId == selectedMeetingId)
                    {
                        AddError(
                            "PreviousMeetingId",
                            "A meeting cannot reference itself.");
                    }
                    else
                    {
                        previousMeetingId =
                            selectedMeetingId;
                    }
                }
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

    private async Task<List<LookupOptionViewModel>> LoadMeetingsAsync(
        SqlConnection connection)
    {
        var meetings =
            new List<LookupOptionViewModel>();

        DynamicTableService.SchemaColumns schema =
            await _tableService.GetSchemaAsync(
                connection,
                "ClientVisiting",
                "ClientMaster");

        string idColumn =
            await _tableService
                .GetPrimaryKeyColumnAsync(
                    connection,
                    "ClientVisiting");

        if (schema.Has("ClientMaster", "ClientName")
            && schema.Has("ClientVisiting", "ClientId")
            && schema.Has("ClientVisiting", "VisitDate")
            && schema.Has("ClientVisiting", "Subject")
            && schema.Has("ClientMaster", "Id"))
        {
            string query = $@"
                SELECT
                    v.[{idColumn}],
                    v.[VisitDate],
                    v.[Subject],
                    c.[ClientName]
                        AS ClientName

                FROM
                    [{TableName}] v

                INNER JOIN
                    [ClientMaster] c
                    ON c.[Id]
                    = v.[ClientId]

                ORDER BY
                    v.[{idColumn}] ASC";

            using SqlCommand command =
                new SqlCommand(query, connection);

            command.CommandTimeout = 0;

            using SqlDataReader reader =
                await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                int id =
                    Convert.ToInt32(
                        reader[idColumn]);

                string date =
                    FormatDate(
                        reader["VisitDate"]);

                string subject =
                    reader["Subject"]
                        .ToString() ?? "";

                string clientName =
                    reader["ClientName"].ToString() ?? "";

                meetings.Add(
                    new LookupOptionViewModel
                    {
                        Id = id,
                        Name =
                            $"MTG-{id:D3}"
                                + $" - {clientName}"
                                + $" - {date}"
                                + $" - {subject}"
                    });
            }
        }

        return meetings;
    }

    private static string FormatDate(object value)
    {
        if (value == DBNull.Value
            || value == null)
        {
            return "";
        }

        return Convert.ToDateTime(value)
            .ToString("dd/MM/yyyy");
    }
}