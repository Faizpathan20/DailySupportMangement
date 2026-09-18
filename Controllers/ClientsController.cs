using System.Security.Claims;
using System.Text;
using Master.Configuration;
using Master.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Master.Controllers;

[Authorize]
public class ClientsController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly DynamicTableService _tableService;

    public ClientsController(
        IConfiguration configuration,
        DynamicTableService tableService)
    {
        _configuration = configuration;
        _tableService = tableService;
    }

    private bool IsAjax =>
        DynamicTableService.IsAjaxRequest(
            Request);

    private const string TableName = "ClientMaster";

    // ============================================
    // CLIENT LIST
    // Fields, KPI columns and grid columns are all
    // derived from live SQL Server metadata.
    // ============================================

    [HttpGet]
    public async Task<IActionResult> Index(
        string? search)
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


        ClientMasterViewModel model =
            new ClientMasterViewModel();

        model.Fields = fields;

        model.FormFields =
            fields.Where(
                    f => f.Editable)
                .ToList();

        model.HasLastWeekKpi =
            fields.Any(
                f => f.Name.Equals(
                    "EntryOn",
                    StringComparison.OrdinalIgnoreCase));

        model.HasActiveKpi =
            fields.Any(
                f => f.Name.Equals(
                    "IsActive",
                    StringComparison.OrdinalIgnoreCase));


        // ========================================
        // LOAD KPIs (only for existing columns)
        // ========================================

        var kpiSelect = new List<string>
        {
            "COUNT(*) AS TotalRecords"
        };


        if (model.HasLastWeekKpi)
        {
            string entryOn =
                fields.First(
                    f => f.Name.Equals(
                        "EntryOn",
                        StringComparison.OrdinalIgnoreCase))
                .Name;

            kpiSelect.Add(
                $"SUM(CASE WHEN " +
                $"[{entryOn}]" +
                $" >= DATEADD(DAY, -7, GETDATE()) " +
                $"THEN 1 ELSE 0 END) " +
                $"AS LastWeekRecords");
        }


        if (model.HasActiveKpi)
        {
            string active =
                fields.First(
                    f => f.Name.Equals(
                        "IsActive",
                        StringComparison.OrdinalIgnoreCase))
                .Name;

            kpiSelect.Add(
                $"SUM(CASE WHEN " +
                $"[{active}]" +
                $" = 1 THEN 1 ELSE 0 END) " +
                $"AS ActiveRecords");

            kpiSelect.Add(
                $"SUM(CASE WHEN " +
                $"[{active}]" +
                $" = 0 THEN 1 ELSE 0 END) " +
                $"AS NonActiveRecords");
        }


        string kpiQuery =
            $"SELECT {string.Join(", ", kpiSelect)} " +
            $"FROM {TableName}";


        using SqlCommand kpiCommand =
            new SqlCommand(kpiQuery, connection);
            kpiCommand.CommandTimeout = 0;


        using SqlDataReader kpiReader =
            await kpiCommand.ExecuteReaderAsync();


        if (await kpiReader.ReadAsync())
        {
            model.TotalRecords =
                Convert.ToInt32(
                    kpiReader["TotalRecords"]);


            model.LastWeekRecords =
                kpiReader["LastWeekRecords"] == DBNull.Value
                    ? 0
                    : Convert.ToInt32(
                        kpiReader["LastWeekRecords"]);


            model.ActiveRecords =
                kpiReader["ActiveRecords"] == DBNull.Value
                    ? 0
                    : Convert.ToInt32(
                        kpiReader["ActiveRecords"]);


            model.NonActiveRecords =
                kpiReader["NonActiveRecords"] == DBNull.Value
                    ? 0
                    : Convert.ToInt32(
                        kpiReader["NonActiveRecords"]);
        }


        kpiReader.Close();


        string idColumn =
            fields.FirstOrDefault(
                    f => f.IsPrimaryKey)
                ?.Name ?? "Id";


        // ========================================
        // LOAD CLIENTS LIST (dynamic columns)
        // Foreign key fields are joined to their
        // referenced table for a friendly display.
        // ========================================

        var selectParts =
            new List<string>();

        var joinClauses =
            new List<string>();

        var searchParts =
            new List<string>();

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
                        $" INNER JOIN {field.LookupKey} {alias} " +
                        $"ON {alias}.{field.LookupRefColumn ?? "Id"} " +
                        $"= c.{field.Name}");
                }

                selectParts.Add(
                    $"{alias}.{displayColumn} AS {field.Name}");

                // Raw FK id, used to pre-fill the dropdown
                // when the Edit modal opens.
                if (field.Editable
                    && field.Control == "select")
                {
                    selectParts.Add(
                        $"c.{field.Name} AS {field.Name}_key");
                }

                searchParts.Add(
                    $"{alias}.{displayColumn} " +
                    $"LIKE '%' + @Search + '%'");
            }
            else
            {
                selectParts.Add(
                    $"c.{field.Name}");

                if (DynamicTableService.IsText(field.SqlType))
                {
                    searchParts.Add(
                        $"c.{field.Name} " +
                        $"LIKE '%' + @Search + '%'");
                }
            }
        }


        if (fields.Any(
                f => f.Name.Equals(
                    idColumn,
                    StringComparison.OrdinalIgnoreCase)))
        {
            searchParts.Add(
                $"CAST(c.{idColumn} AS NVARCHAR(10)) " +
                $"LIKE '%' + @Search + '%'");
        }


        var sql = new StringBuilder();

        sql.Append("SELECT ");
        sql.Append(
            string.Join(", ", selectParts));

        sql.Append($" FROM {TableName} c");

        foreach (string join in joinClauses)
        {
            sql.Append(join);
        }


        if (searchParts.Count > 0)
        {
            sql.Append(" WHERE (@Search = '' OR ");
            sql.Append(
                string.Join(" OR ", searchParts));
            sql.Append(')');
        }
        else
        {
            sql.Append(" WHERE @Search = ''");
        }


        if (fields.Any(
                f => f.Name.Equals(
                    idColumn,
                    StringComparison.OrdinalIgnoreCase)))
        {
            sql.Append(
                $" ORDER BY c.{idColumn} ASC");
        }
        else
        {
            sql.Append(
                " ORDER BY (SELECT NULL)");
        }


        using SqlCommand command =
            new SqlCommand(sql.ToString(), connection);
            command.CommandTimeout = 0;


        command.Parameters.Add(
            new SqlParameter(
                "@Search",
                System.Data.SqlDbType.NVarChar,
                100)
            {
                Value =
                    search?.Trim() ?? ""
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

                if (field.Editable
                    && field.Control == "select")
                {
                    object? keyValue =
                        reader[$"{field.Name}_key"];

                    row.Values[$"{field.Name}_key"] =
                        keyValue == DBNull.Value
                            ? ""
                            : keyValue.ToString() ?? "";
                }
            }

            model.Clients.Add(row);
        }


        reader.Close();


        // ========================================
        // LOAD DROPDOWNS (metadata driven)
        // ========================================

        var dropdowns =
            new Dictionary<string, List<LookupOptionViewModel>>(
                StringComparer.OrdinalIgnoreCase);


        foreach (var field in model.FormFields.Where(
                     f => f.Control == "select"
                          && f.LookupKey != null))
        {
            string displayColumn =
                _tableService.GetLookupDisplayColumn(
                    field.LookupKey!);

            string refColumn =
                field.LookupRefColumn ?? "Id";

            string lookupQuery = $@"
                SELECT
                    [{refColumn}],
                    [{displayColumn}]

                FROM
                    [{field.LookupKey}]

                ORDER BY
                    [{displayColumn}] ASC";


            using SqlCommand lookupCommand =
                new SqlCommand(lookupQuery, connection);
                lookupCommand.CommandTimeout = 0;


            var options =
                new List<LookupOptionViewModel>();


            using SqlDataReader lookupReader =
                await lookupCommand.ExecuteReaderAsync();


            while (await lookupReader.ReadAsync())
            {
                options.Add(
                    new LookupOptionViewModel
                    {
                        Id =
                            Convert.ToInt32(
                                lookupReader[refColumn]),

                        Name =
                            lookupReader[displayColumn]
                                .ToString() ?? ""
                    });
            }


            dropdowns[field.LookupKey!] = options;
        }


        ViewBag.Dropdowns = dropdowns;

        ViewBag.Search = search;

        return View(model);
    }


    // ============================================
    // CREATE (metadata driven)
    // Server-generated and auto columns (UserId,
    // IsActive, defaults, identity) need no form
    // entry; everything else comes from the form
    // with type-safe parameterized SQL.
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create()
    {
        var form = Request.Form;

        string? connectionString =
            _configuration.GetConnectionString(
                "DefaultConnection");


        string? loggedInUserId =
            DynamicTableService.GetLoggedInUserId(
                User);


        using SqlConnection connection =
            new SqlConnection(connectionString);


        await connection.OpenAsync();


        List<MasterColumnViewModel> fields =
            await _tableService.GetTableFieldsAsync(
                connection,
                TableName);


        var formFields =
            fields.Where(f => f.Editable)
                .ToList();


        string? validationError =
            _tableService.ValidateRequiredFields(
                formFields,
                form);


        if (!string.IsNullOrWhiteSpace(validationError))
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message = validationError
                });
            }

            TempData["Error"] = validationError;

            return RedirectToAction(
                "Index",
                "Clients");
        }


        if (fields.Any(
                f => f.AutoWrite == "auth-user")
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


        var insertColumns =
            new List<string>();

        var placeholders =
            new List<string>();


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

            insertColumns.Add(field.Name);

            placeholders.Add($"@{field.Name}");

            _tableService.AddParameter(
                command,
                field,
                form[field.Name].ToString());
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
                            Convert.ToInt32(
                                loggedInUserId)
                    });
            }
            else if (auto.AutoWrite == "true")
            {
                insertColumns.Add(auto.Name);

                placeholders.Add("1");
            }
        }


        command.CommandText =
            $"INSERT INTO {TableName} " +
            $"({string.Join(", ", insertColumns)}) " +
            $"VALUES ({string.Join(", ", placeholders)})";


        try
        {
            await command.ExecuteNonQueryAsync();
        }
        catch (SqlException ex) when (
            ex.Number == 2627 || ex.Number == 2601)
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message =
                        "This Client is already added. " +
                        "Please add a new one."
                });
            }

            TempData["Error"] =
                "This Client is already added. Please add a new one.";

            return RedirectToAction(
                "Index",
                "Clients");
        }


        if (IsAjax)
        {
            return Json(new
            {
                success = true,
                message = "Client added successfully."
            });
        }

        TempData["Success"] =
            "Client added successfully.";

        return RedirectToAction(
            "Index",
            "Clients");
    }


    // ============================================
    // EDIT (metadata driven)
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


        var editFields =
            fields.Where(
                    f => f.Editable
                         && !f.CreateOnly)
                .ToList();


        string idColumn =
            fields.FirstOrDefault(
                    f => f.IsPrimaryKey)
                ?.Name ?? "Id";


        string? validationError =
            _tableService.ValidateRequiredFields(
                editFields,
                form);


        if (!string.IsNullOrWhiteSpace(validationError))
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message = validationError
                });
            }

            TempData["Error"] = validationError;

            return RedirectToAction(
                "Index",
                "Clients");
        }


        var setParts =
            new List<string>();


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

            setParts.Add(
                $"{field.Name} = @{field.Name}");

            _tableService.AddParameter(
                command,
                field,
                form[field.Name].ToString());
        }


        foreach (var auto in fields.Where(
                     f => f.AutoWrite == "true"))
        {
            setParts.Add(
                $"{auto.Name} = 1");
        }


        command.CommandText =
            $"UPDATE {TableName} " +
            $"SET {string.Join(", ", setParts)} " +
            $"WHERE {idColumn} = @Id";


        try
        {
            int rows =
                await command.ExecuteNonQueryAsync();

            if (rows == 0)
            {
                if (IsAjax)
                {
                    return Json(new
                    {
                        success = false,
                        message = "Client not found."
                    });
                }

                TempData["Error"] =
                    "Client not found.";
            }
            else
            {
                if (IsAjax)
                {
                    return Json(new
                    {
                        success = true,
                        message =
                            "Client updated successfully."
                    });
                }

                TempData["Success"] =
                    "Client updated successfully.";
            }
        }
        catch (SqlException ex) when (
            ex.Number == 2627 || ex.Number == 2601)
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message =
                        "This Client is already added. " +
                        "Please add a new one."
                });
            }

            TempData["Error"] =
                "This Client is already added. Please add a new one.";
        }


        return RedirectToAction(
            "Index",
            "Clients");
    }


    // ============================================
    // DELETE
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(
        int id)
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


        DynamicTableService.SchemaColumns schema =
            await _tableService.GetSchemaAsync(
                connection,
                "ClientVisiting",
                "DailySupport");


        var checks =
            new List<(string Table,
                string Column,
                string Label)>();

        if (schema.Has("ClientVisiting", "ClientId"))
        {
            checks.Add(
                ("ClientVisiting",
                 "ClientId",
                 "visit"));
        }

        if (schema.Has("DailySupport", "ClientId"))
        {
            checks.Add(
                ("DailySupport",
                 "ClientId",
                 "support"));
        }


        var dependencies =
            new List<string>();

        foreach (var check in checks)
        {
            string dependentQuery = $@"
                SELECT COUNT(*)
                FROM {check.Table}
                WHERE {check.Column} = @Id";

            using SqlCommand dependentCmd =
                new SqlCommand(dependentQuery, connection);
                dependentCmd.CommandTimeout = 0;

            dependentCmd.Parameters.AddWithValue(
                "@Id",
                id);

            int count =
                (int)await dependentCmd.ExecuteScalarAsync();

            if (count > 0)
            {
                dependencies.Add(
                    $"{count} {check.Label}"
                        + (count == 1 ? "" : "s"));
            }
        }

        if (dependencies.Count > 0)
        {
            string message =
                $"Cannot delete this client. "
                    + $"It is referenced by "
                    + $"{string.Join(", ", dependencies)}."
                    + " Delete or reassign those records first.";

            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message
                });
            }

            TempData["Error"] = message;

            return RedirectToAction(
                "Index",
                "Clients");
        }


        string query = $@"
            DELETE FROM
                {TableName}

            WHERE
                {idColumn}
                = @Id";


        using SqlCommand command =
            new SqlCommand(query, connection);
            command.CommandTimeout = 0;


        command.Parameters.AddWithValue(
            "@Id",
            id);


        int rows =
            await command.ExecuteNonQueryAsync();


        if (rows == 0)
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message = "Client not found."
                });
            }

            TempData["Error"] =
                "Client not found.";
        }
        else
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = true,
                    message =
                        "Client deleted successfully."
                });
            }

            TempData["Success"] =
                "Client deleted successfully.";
        }


        return RedirectToAction(
            "Index",
            "Clients");
    }
}