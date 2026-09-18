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
    private readonly DynamicTableService _tableService;

    public UsersController(
        IConfiguration configuration,
        DynamicTableService tableService)
    {
        _configuration = configuration;
        _tableService = tableService;
    }

    private bool IsAjax =>
        DynamicTableService.IsAjaxRequest(
            Request);

    private const string TableName = "LoginUsers";

    // ============================================
    // USER LIST
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
        // LOAD USERS LIST (dynamic columns)
        // ========================================

        var selectParts =
            new List<string>();

        var searchParts =
            new List<string>();


        foreach (var field in model.Fields)
        {
            selectParts.Add(
                $"u.{field.Name}");

            if (DynamicTableService.IsText(field.SqlType))
            {
                searchParts.Add(
                    $"u.{field.Name} " +
                    $"LIKE '%' + @Search + '%'");
            }
        }


        if (fields.Any(
                f => f.Name.Equals(
                    idColumn,
                    StringComparison.OrdinalIgnoreCase)))
        {
            searchParts.Add(
                $"CAST(u.{idColumn} AS NVARCHAR(10)) " +
                $"LIKE '%' + @Search + '%'");
        }


        var sql = new StringBuilder();

        sql.Append("SELECT ");
        sql.Append(
            string.Join(", ", selectParts));

        sql.Append(
            $" FROM {TableName} u");


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
                $" ORDER BY u.{idColumn} ASC");
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

            foreach (var field in model.Fields)
            {
                object value = reader[field.Name];

                row.Values[field.Name] =
                    DynamicTableService.FormatCellValue(
                        field,
                        value);
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
    // CREATE (metadata driven)
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
                "Users");
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
            if (auto.AutoWrite == "true")
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
    // EDIT (metadata driven, excludes create-only)
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
                "Users");
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


        await connection.OpenAsync();


        string idColumn =
            await _tableService
                .GetPrimaryKeyColumnAsync(
                    connection,
                    TableName);


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