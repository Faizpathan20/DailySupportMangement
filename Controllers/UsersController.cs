using Master.Configuration;
using Master.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Text;

namespace Master.Controllers;

[Authorize]
public class UsersController : Controller
{
    private readonly IConfiguration _configuration;

    public UsersController(
        IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private bool IsAjax =>
        DynamicTableHelper.IsAjaxRequest(
            Request);


    // ============================================
    // REGISTRY OF ALL KNOWN USER MASTER COLUMNS
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
                Width = 10
            },

            new MasterColumnViewModel
            {
                Name = "UserName",
                Display = "User Name",
                Type = "text",
                SortType = "text",
                InputType = "text",
                Required = true,
                Editable = true,
                Width = 55
            },

            new MasterColumnViewModel
            {
                Name = "Password",
                Display = "Password",
                Type = "password",
                SortType = "text",
                InputType = "password",
                Required = true,
                Editable = true,
                CreateOnly = true,
                ShowInTable = false,
                Width = 25
            },

            new MasterColumnViewModel
            {
                Name = "IsActive",
                Display = "Status",
                Type = "status",
                SortType = "status",
                Width = 16
            }
        };


    private static readonly string[] UserSearchableFields =
    {
        "UserName"
    };


    // ============================================
    // USER LIST
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
                DatabaseMapping.LoginUsers.Table);


        List<MasterColumnViewModel> fields =
            DynamicTableHelper.BuildActiveFields(
                FieldRegistry,
                columns);


        UserMasterViewModel model =
            new UserMasterViewModel();

        model.Fields =
            fields.Where(
                    f => f.ShowInTable)
                .ToList();

        model.FormFields =
            fields.Where(
                    f => f.Editable)
                .ToList();

        model.HasActiveKpi =
            columns.Contains(
                DatabaseMapping.LoginUsers.IsActive);


        // ========================================
        // LOAD KPIs (only for existing columns)
        // ========================================

        var kpiSelect = new List<string>
        {
            "COUNT(*) AS TotalRecords"
        };


        if (columns.Contains(
                DatabaseMapping.LoginUsers.IsActive))
        {
            kpiSelect.Add(
                $"SUM(CASE WHEN " +
                $"{DatabaseMapping.LoginUsers.IsActive}" +
                $" = 1 THEN 1 ELSE 0 END) " +
                $"AS ActiveRecords");

            kpiSelect.Add(
                $"SUM(CASE WHEN " +
                $"{DatabaseMapping.LoginUsers.IsActive}" +
                $" = 0 THEN 1 ELSE 0 END) " +
                $"AS NonActiveRecords");
        }


        string kpiQuery =
            $"SELECT {string.Join(", ", kpiSelect)} " +
            $"FROM {DatabaseMapping.LoginUsers.Table}";


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
        // LOAD USERS LIST (dynamic columns)
        // ========================================

        var selectParts =
            new List<string>();

        var searchParts =
            new List<string>();


        foreach (var field in fields.Where(f => f.ShowInTable))
        {
            selectParts.Add(
                $"u.{field.Name}");
        }


        var sql = new StringBuilder();

        sql.Append("SELECT ");
        sql.Append(
            string.Join(", ", selectParts));

        sql.Append(
            $" FROM {DatabaseMapping.LoginUsers.Table} u");


        foreach (var name in UserSearchableFields)
        {
            if (columns.Contains(name))
            {
                searchParts.Add(
                    $"u.{name} LIKE '%' + @Search + '%'");
            }
        }


        if (columns.Contains("Id"))
        {
            searchParts.Add(
                "CAST(u.Id AS NVARCHAR(10)) " +
                "LIKE '%' + @Search + '%'");
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
                " ORDER BY u.Id ASC");
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

            foreach (var field in fields.Where(f => f.ShowInTable))
            {
                object value = reader[field.Name];

                if (field.Type == "status")
                {
                    row.Values[field.Name] =
                        value != DBNull.Value
                            && Convert.ToBoolean(value)
                                ? "Active"
                                : "Non Active";
                }
                else
                {
                    row.Values[field.Name] =
                        value == DBNull.Value
                            ? ""
                            : value.ToString() ?? "";
                }
            }

            model.Users.Add(row);
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


        using SqlConnection connection =
            new SqlConnection(connectionString);


        await connection.OpenAsync();


        HashSet<string> columns =
            await DynamicTableHelper.GetTableColumnsAsync(
                connection,
                DatabaseMapping.LoginUsers.Table);


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
                "Users");
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


        if (columns.Contains("IsActive"))
        {
            insertColumns.Add("IsActive");

            placeholders.Add("1");
        }


        command.CommandText =
            $"INSERT INTO {DatabaseMapping.LoginUsers.Table} " +
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
                        "This User already exists. " +
                        "Please try another."
                });
            }

            TempData["Error"] =
                "This User already exists. Please try another.";

            return RedirectToAction(
                "Index",
                "Users");
        }


        if (IsAjax)
        {
            return Json(new
            {
                success = true,
                message = "User added successfully."
            });
        }

        TempData["Success"] =
            "User added successfully.";

        return RedirectToAction(
            "Index",
            "Users");
    }


    // ============================================
    // EDIT (dynamic columns, excludes create-only)
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
                DatabaseMapping.LoginUsers.Table);


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
                "Users");
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
            $"UPDATE {DatabaseMapping.LoginUsers.Table} " +
            $"SET {string.Join(", ", setParts)} " +
            $"WHERE {DatabaseMapping.LoginUsers.Id} = @Id";


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
                        message = "User not found."
                    });
                }

                TempData["Error"] =
                    "User not found.";
            }
            else
            {
                if (IsAjax)
                {
                    return Json(new
                    {
                        success = true,
                        message =
                            "User updated successfully."
                    });
                }

                TempData["Success"] =
                    "User updated successfully.";
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
                        "This User already exists. " +
                        "Please try another."
                });
            }

            TempData["Error"] =
                "This User already exists. Please try another.";
        }


        return RedirectToAction(
            "Index",
            "Users");
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


        string query = $@"
            DELETE FROM
                {DatabaseMapping.LoginUsers.Table}

            WHERE
                {DatabaseMapping.LoginUsers.Id}
                = @Id";


        using SqlCommand command =
            new SqlCommand(query, connection);
            command.CommandTimeout = 0;


        command.Parameters.AddWithValue(
            "@Id",
            id);


        await connection.OpenAsync();


        int rows =
            await command.ExecuteNonQueryAsync();


        if (rows == 0)
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = false,
                    message = "User not found."
                });
            }

            TempData["Error"] =
                "User not found.";
        }
        else
        {
            if (IsAjax)
            {
                return Json(new
                {
                    success = true,
                    message =
                        "User deleted successfully."
                });
            }

            TempData["Success"] =
                "User deleted successfully.";
        }


        return RedirectToAction(
            "Index",
            "Users");
    }
}