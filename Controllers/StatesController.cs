using Master.Configuration;
using Master.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Text;

namespace Master.Controllers;

[Authorize]
public class StatesController : Controller
{
    private readonly IConfiguration _configuration;

    public StatesController(
        IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private bool IsAjax =>
        DynamicTableHelper.IsAjaxRequest(
            Request);


    // ============================================
    // REGISTRY OF ALL KNOWN STATE MASTER COLUMNS
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
                Width = 6
            },

            new MasterColumnViewModel
            {
                Name = "StateName",
                Display = "State Name",
                Type = "text",
                SortType = "text",
                InputType = "text",
                Required = true,
                Editable = true,
                Width = 30
            },

            new MasterColumnViewModel
            {
                Name = "EntryOn",
                Display = "Entry On",
                Type = "date",
                SortType = "date",
                Width = 20
            },

            new MasterColumnViewModel
            {
                Name = "UserId",
                Display = "User Name",
                Type = "lookup",
                SortType = "text",
                LookupKey = "Users",
                Width = 18
            },

            new MasterColumnViewModel
            {
                Name = "IsActive",
                Display = "Status",
                Type = "status",
                SortType = "status",
                Width = 12
            }
        };


    private static readonly string[] StateSearchableFields =
    {
        "StateName"
    };


    // ============================================
    // STATE LIST
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
                DatabaseMapping.States.Table);


        List<MasterColumnViewModel> fields =
            DynamicTableHelper.BuildActiveFields(
                FieldRegistry,
                columns);


        StateMasterViewModel model =
            new StateMasterViewModel();

        model.Fields = fields;

        model.FormFields =
            fields.Where(
                    f => f.Editable)
                .ToList();

        model.HasLastWeekKpi =
            columns.Contains(
                DatabaseMapping.States.EntryOn);

        model.HasActiveKpi =
            columns.Contains(
                DatabaseMapping.States.IsActive);


        // ========================================
        // LOAD KPIs (only for existing columns)
        // ========================================

        var kpiSelect = new List<string>
        {
            "COUNT(*) AS TotalRecords"
        };


        if (columns.Contains(
                DatabaseMapping.States.EntryOn))
        {
            kpiSelect.Add(
                $"SUM(CASE WHEN " +
                $"{DatabaseMapping.States.EntryOn}" +
                $" >= DATEADD(DAY, -7, GETDATE()) " +
                $"THEN 1 ELSE 0 END) " +
                $"AS LastWeekRecords");
        }


        if (columns.Contains(
                DatabaseMapping.States.IsActive))
        {
            kpiSelect.Add(
                $"SUM(CASE WHEN " +
                $"{DatabaseMapping.States.IsActive}" +
                $" = 1 THEN 1 ELSE 0 END) " +
                $"AS ActiveRecords");

            kpiSelect.Add(
                $"SUM(CASE WHEN " +
                $"{DatabaseMapping.States.IsActive}" +
                $" = 0 THEN 1 ELSE 0 END) " +
                $"AS NonActiveRecords");
        }


        string kpiQuery =
            $"SELECT {string.Join(", ", kpiSelect)} " +
            $"FROM {DatabaseMapping.States.Table}";


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
        // LOAD STATES LIST (dynamic columns)
        // ========================================

        bool joinUser =
            columns.Contains("UserId");


        var selectParts =
            new List<string>();

        var searchParts =
            new List<string>();


        foreach (var field in fields)
        {
            if (field.Name == "UserId" && joinUser)
            {
                selectParts.Add(
                    $"lu.{DatabaseMapping.LoginUsers.UserName} AS {field.Name}");
            }
            else
            {
                selectParts.Add(
                    $"s.{field.Name}");
            }
        }


        var sql = new StringBuilder();

        sql.Append("SELECT ");
        sql.Append(
            string.Join(", ", selectParts));

        sql.Append($" FROM {DatabaseMapping.States.Table} s");


        if (joinUser)
        {
            sql.Append(
                $" INNER JOIN {DatabaseMapping.LoginUsers.Table} lu " +
                $"ON lu.{DatabaseMapping.LoginUsers.Id} " +
                $"= s.{DatabaseMapping.States.UserId}");
        }


        foreach (var name in StateSearchableFields)
        {
            if (columns.Contains(name))
            {
                searchParts.Add(
                    $"s.{name} LIKE '%' + @Search + '%'");
            }
        }


        if (columns.Contains("Id"))
        {
            searchParts.Add(
                "CAST(s.Id AS NVARCHAR(10)) " +
                "LIKE '%' + @Search + '%'");
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
                " ORDER BY s.Id ASC");
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

            model.States.Add(row);
        }


        reader.Close();


        ViewBag.Dropdowns =
            new Dictionary<string, List<LookupOptionViewModel>>(
                StringComparer.OrdinalIgnoreCase);

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
            DynamicTableHelper.GetLoggedInUserId(User);


        using SqlConnection connection =
            new SqlConnection(connectionString);


        await connection.OpenAsync();


        HashSet<string> columns =
            await DynamicTableHelper.GetTableColumnsAsync(
                connection,
                DatabaseMapping.States.Table);


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
                "States");
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
            $"INSERT INTO {DatabaseMapping.States.Table} " +
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
                        "This State is already added."
                        + " Please add a new one."
                });
            }

            TempData["Error"] =
                "This State is already added. Please add a new one.";

            return RedirectToAction(
                "Index",
                "States");
        }


        if (IsAjax)
        {
            return Json(new
            {
                success = true,
                message = "State added successfully."
            });
        }

        TempData["Success"] =
            "State added successfully.";

        return RedirectToAction(
            "Index",
            "States");
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
                DatabaseMapping.States.Table);


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
                "States");
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
            $"UPDATE {DatabaseMapping.States.Table} " +
            $"SET {string.Join(", ", setParts)} " +
            $"WHERE {DatabaseMapping.States.Id} = @Id";


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
                        message = "State not found."
                    });
                }

                TempData["Error"] =
                    "State not found.";
            }
            else
            {
                if (IsAjax)
                {
                    return Json(new
                    {
                        success = true,
                        message =
                            "State updated successfully."
                    });
                }

                TempData["Success"] =
                    "State updated successfully.";
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
                        "This State is already added."
                        + " Please add a new one."
                });
            }

            TempData["Error"] =
                "This State is already added. Please add a new one.";
        }


        return RedirectToAction(
            "Index",
            "States");
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


        string checkQuery = $@"
            SELECT COUNT(*)
            FROM {DatabaseMapping.ClientMaster.Table}
            WHERE {DatabaseMapping.ClientMaster.StateId}
                  = @Id";


        using SqlCommand checkCmd =
            new SqlCommand(checkQuery, connection);
            checkCmd.CommandTimeout = 0;


        checkCmd.Parameters.AddWithValue(
            "@Id",
            id);


        int clientCount =
            (int)await checkCmd.ExecuteScalarAsync();


        if (clientCount > 0)
        {
            string message =
                $"Cannot delete this state. "
                + $"{clientCount} client(s) are "
                + $"assigned to it. Reassign or "
                + $"delete those clients first.";

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
                "States");
        }


        string query = $@"
            DELETE FROM
                {DatabaseMapping.States.Table}

            WHERE
                {DatabaseMapping.States.Id}
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
                    message = "State not found."
                });
            }

            TempData["Error"] =
                "State not found.";
        }
        else
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = true,
                    message =
                        "State deleted successfully."
                });
            }

            TempData["Success"] =
                "State deleted successfully.";
        }


        return RedirectToAction(
            "Index",
            "States");
    }
}