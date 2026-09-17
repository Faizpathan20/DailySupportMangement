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

    public ClientsController(
        IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private bool IsAjax =>
        DynamicTableHelper.IsAjaxRequest(
            Request);


    // ============================================
    // REGISTRY OF ALL KNOWN CLIENT MASTER COLUMNS
    // Only columns that actually exist in the
    // database table are used dynamically.
    // ============================================

    private static readonly List<MasterColumnViewModel> FieldRegistry =
        new()
        {
            new MasterColumnViewModel
            {
                Name = "Id",
                Display = "ID",
                Type = "number",
                SortType = "num",
                Width = 7
            },

            new MasterColumnViewModel
            {
                Name = "ClientName",
                Display = "Client Name",
                Type = "text",
                SortType = "text",
                InputType = "text",
                Required = true,
                Editable = true,
                Width = 10
            },

            new MasterColumnViewModel
            {
                Name = "ContactPerson",
                Display = "Contact Person",
                Type = "text",
                SortType = "text",
                InputType = "text",
                Editable = true,
                Width = 9
            },

            new MasterColumnViewModel
            {
                Name = "MobileNo",
                Display = "Mobile No",
                Type = "text",
                SortType = "text",
                InputType = "tel",
                Editable = true,
                Width = 8
            },

            new MasterColumnViewModel
            {
                Name = "AlternateMobileNo",
                Display = "Alt Mobile No",
                Type = "text",
                SortType = "text",
                InputType = "tel",
                Editable = true,
                Width = 8
            },

            new MasterColumnViewModel
            {
                Name = "Email",
                Display = "Email",
                Type = "text",
                SortType = "text",
                InputType = "email",
                Editable = true,
                Width = 12
            },

            new MasterColumnViewModel
            {
                Name = "Address",
                Display = "Address",
                Type = "text",
                SortType = "text",
                Control = "textarea",
                Editable = true,
                Width = 12
            },

            new MasterColumnViewModel
            {
                Name = "City",
                Display = "City",
                Type = "text",
                SortType = "text",
                InputType = "text",
                Editable = true,
                Width = 7
            },

            new MasterColumnViewModel
            {
                Name = "StateId",
                Display = "State",
                Type = "dropdown",
                SortType = "text",
                Control = "select",
                Required = true,
                Editable = true,
                LookupKey = "States",
                Width = 8
            },

            new MasterColumnViewModel
            {
                Name = "UserId",
                Display = "User Name",
                Type = "lookup",
                SortType = "text",
                LookupKey = "Users",
                Width = 8
            },

            new MasterColumnViewModel
            {
                Name = "EntryOn",
                Display = "Entry On",
                Type = "date",
                SortType = "date",
                Width = 12
            },

            new MasterColumnViewModel
            {
                Name = "IsActive",
                Display = "Status",
                Type = "status",
                SortType = "status",
                Width = 7
            }
        };


    private static readonly string[] ClientSearchableFields =
    {
        "ClientName",
        "ContactPerson",
        "MobileNo",
        "AlternateMobileNo",
        "Email",
        "City"
    };


    // ============================================
    // CLIENT LIST
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


        HashSet<string> columns =
            await DynamicTableHelper.GetTableColumnsAsync(
                connection,
                DatabaseMapping.ClientMaster.Table);


        List<MasterColumnViewModel> fields =
            DynamicTableHelper.BuildActiveFields(
                FieldRegistry,
                columns);


        ClientMasterViewModel model =
            new ClientMasterViewModel();

        model.Fields = fields;

        model.FormFields =
            fields.Where(
                    f => f.Editable)
                .ToList();

        model.HasLastWeekKpi =
            columns.Contains(
                DatabaseMapping.ClientMaster.EntryOn);

        model.HasActiveKpi =
            columns.Contains(
                DatabaseMapping.ClientMaster.IsActive);


        // ========================================
        // LOAD KPIs (only for existing columns)
        // ========================================

        var kpiSelect = new List<string>
        {
            "COUNT(*) AS TotalRecords"
        };


        if (columns.Contains(
                DatabaseMapping.ClientMaster.EntryOn))
        {
            kpiSelect.Add(
                $"SUM(CASE WHEN " +
                $"{DatabaseMapping.ClientMaster.EntryOn}" +
                $" >= DATEADD(DAY, -7, GETDATE()) " +
                $"THEN 1 ELSE 0 END) " +
                $"AS LastWeekRecords");
        }


        if (columns.Contains(
                DatabaseMapping.ClientMaster.IsActive))
        {
            kpiSelect.Add(
                $"SUM(CASE WHEN " +
                $"{DatabaseMapping.ClientMaster.IsActive}" +
                $" = 1 THEN 1 ELSE 0 END) " +
                $"AS ActiveRecords");

            kpiSelect.Add(
                $"SUM(CASE WHEN " +
                $"{DatabaseMapping.ClientMaster.IsActive}" +
                $" = 0 THEN 1 ELSE 0 END) " +
                $"AS NonActiveRecords");
        }


        string kpiQuery =
            $"SELECT {string.Join(", ", kpiSelect)} " +
            $"FROM {DatabaseMapping.ClientMaster.Table}";


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


        // ========================================
        // LOAD CLIENTS LIST (dynamic columns)
        // ========================================

        bool joinState =
            columns.Contains("StateId");

        bool joinUser =
            columns.Contains("UserId");


        var selectParts =
            new List<string>();


        var searchParts =
            new List<string>();


        foreach (var field in fields)
        {
            if (field.Name == "StateId" && joinState)
            {
                selectParts.Add(
                    $"st.{DatabaseMapping.States.StateName} AS {field.Name}");
            }
            else if (field.Name == "UserId" && joinUser)
            {
                selectParts.Add(
                    $"lu.{DatabaseMapping.LoginUsers.UserName} AS {field.Name}");
            }
            else
            {
                selectParts.Add(
                    $"c.{field.Name}");
            }
        }


        var sql = new StringBuilder();

        sql.Append("SELECT ");
        sql.Append(
            string.Join(", ", selectParts));

        sql.Append($" FROM {DatabaseMapping.ClientMaster.Table} c");


        if (joinState)
        {
            sql.Append(
                $" INNER JOIN {DatabaseMapping.States.Table} st " +
                $"ON st.{DatabaseMapping.States.Id} " +
                $"= c.{DatabaseMapping.ClientMaster.StateId}");
        }


        if (joinUser)
        {
            sql.Append(
                $" INNER JOIN {DatabaseMapping.LoginUsers.Table} lu " +
                $"ON lu.{DatabaseMapping.LoginUsers.Id} " +
                $"= c.{DatabaseMapping.ClientMaster.UserId}");
        }


        foreach (var name in ClientSearchableFields)
        {
            if (columns.Contains(name))
            {
                searchParts.Add(
                    $"c.{name} LIKE '%' + @Search + '%'");
            }
        }


        if (columns.Contains("Id"))
        {
            searchParts.Add(
                "CAST(c.Id AS NVARCHAR(10)) " +
                "LIKE '%' + @Search + '%'");
        }


        if (joinState)
        {
            searchParts.Add(
                $"st.{DatabaseMapping.States.StateName} " +
                $"LIKE '%' + @Search + '%'");
        }


        if (joinUser)
        {
            searchParts.Add(
                $"lu.{DatabaseMapping.LoginUsers.UserName} " +
                $"LIKE '%' + @Search + '%'");
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


        if (columns.Contains("Id"))
        {
            sql.Append(
                " ORDER BY c.Id ASC");
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

                switch (field.Type)
                {
                    case "date":

                        row.Values[field.Name] =
                            value == DBNull.Value
                                ? ""
                                : Convert.ToDateTime(value)
                                    .ToString(
                                        "dd/MM/yyyy hh:mm tt");

                        break;

                    case "status":

                        row.Values[field.Name] =
                            value != DBNull.Value
                                && Convert.ToBoolean(value)
                                    ? "Active"
                                    : "Non Active";

                        break;

                    default:

                        row.Values[field.Name] =
                            value == DBNull.Value
                                ? ""
                                : value.ToString() ?? "";

                        break;
                }
            }

            model.Clients.Add(row);
        }


        reader.Close();


        // ========================================
        // LOAD DROPDOWNS (e.g. States)
        // ========================================

        var dropdowns =
            new Dictionary<string, List<LookupOptionViewModel>>(
                StringComparer.OrdinalIgnoreCase);


        if (fields.Any(
                f => f.Editable
                     && f.Control == "select"))
        {
            string statesQuery = $@"
                SELECT
                    {DatabaseMapping.States.Id},
                    {DatabaseMapping.States.StateName}

                FROM
                    {DatabaseMapping.States.Table}

                ORDER BY
                    {DatabaseMapping.States.StateName} ASC";


            using SqlCommand statesCommand =
                new SqlCommand(statesQuery, connection);
                statesCommand.CommandTimeout = 0;


            using SqlDataReader statesReader =
                await statesCommand.ExecuteReaderAsync();


            var options =
                new List<LookupOptionViewModel>();


            while (await statesReader.ReadAsync())
            {
                options.Add(
                    new LookupOptionViewModel
                    {
                        Id =
                            Convert.ToInt32(
                                statesReader[
                                    DatabaseMapping
                                        .States
                                        .Id]),

                        Name =
                            statesReader[
                                DatabaseMapping
                                    .States
                                    .StateName]
                            .ToString() ?? ""
                    });
            }


            dropdowns["States"] = options;
        }


        ViewBag.Dropdowns = dropdowns;

        ViewBag.Search = search;

        return View(model);
    }


    // ============================================
    // CREATE (dynamic columns)
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
            User.FindFirst(
                ClaimTypes.NameIdentifier)
            ?.Value;


        using SqlConnection connection =
            new SqlConnection(connectionString);


        await connection.OpenAsync();


        HashSet<string> columns =
            await DynamicTableHelper.GetTableColumnsAsync(
                connection,
                DatabaseMapping.ClientMaster.Table);


        List<MasterColumnViewModel> fields =
            DynamicTableHelper.BuildActiveFields(
                    FieldRegistry,
                    columns)
                .Where(f => f.Editable)
                .ToList();


        string? validationError =
            DynamicTableHelper.ValidateRequiredFields(
                fields,
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


        if (columns.Contains("UserId")
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
            new SqlCommand();
            command.CommandTimeout = 0;

        command.Connection = connection;
        command.CommandTimeout = 0;


        foreach (var field in fields)
        {
            insertColumns.Add(field.Name);

            placeholders.Add($"@{field.Name}");

            DynamicTableHelper.AddEditableParameter(
                command,
                field,
                form[field.Name].ToString());
        }


        if (columns.Contains("EntryOn"))
        {
            insertColumns.Add("EntryOn");

            placeholders.Add("GETDATE()");
        }


        if (columns.Contains("UserId"))
        {
            insertColumns.Add("UserId");

            placeholders.Add("@UserId");

            command.Parameters.Add(
                new SqlParameter(
                    "@UserId",
                    System.Data.SqlDbType.Int)
                {
                    Value =
                        Convert.ToInt32(
                            loggedInUserId)
                });
        }


        if (columns.Contains("IsActive"))
        {
            insertColumns.Add("IsActive");

            placeholders.Add("1");
        }


        command.CommandText =
            $"INSERT INTO {DatabaseMapping.ClientMaster.Table} " +
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
    // EDIT (dynamic columns)
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


        HashSet<string> columns =
            await DynamicTableHelper.GetTableColumnsAsync(
                connection,
                DatabaseMapping.ClientMaster.Table);


        List<MasterColumnViewModel> fields =
            DynamicTableHelper.BuildActiveFields(
                    FieldRegistry,
                    columns)
                .Where(f => f.Editable
                            && !f.CreateOnly)
                .ToList();


        string? validationError =
            DynamicTableHelper.ValidateRequiredFields(
                fields,
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
            new SqlCommand();
            command.CommandTimeout = 0;

        command.Connection = connection;
        command.CommandTimeout = 0;

        command.Parameters.Add(
            new SqlParameter(
                "@Id",
                System.Data.SqlDbType.Int)
            {
                Value = id
            });


        foreach (var field in fields)
        {
            setParts.Add($"{field.Name} = @{field.Name}");

            DynamicTableHelper.AddEditableParameter(
                command,
                field,
                form[field.Name].ToString());
        }


        if (columns.Contains("IsActive"))
        {
            setParts.Add("IsActive = 1");
        }


        command.CommandText =
            $"UPDATE {DatabaseMapping.ClientMaster.Table} " +
            $"SET {string.Join(", ", setParts)} " +
            $"WHERE {DatabaseMapping.ClientMaster.Id} = @Id";


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


        (string Table, string Column, string Label)[] checks =
        {
            (
                DatabaseMapping.ClientVisiting.Table,
                DatabaseMapping.ClientVisiting.ClientId,
                "visit"
            ),
            (
                DatabaseMapping.DailySupport.Table,
                DatabaseMapping.DailySupport.ClientId,
                "support"
            )
        };

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
                {DatabaseMapping.ClientMaster.Table}

            WHERE
                {DatabaseMapping.ClientMaster.Id}
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