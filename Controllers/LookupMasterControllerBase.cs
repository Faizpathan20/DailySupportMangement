using Master.Configuration;
using Master.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Master.Controllers;

// ============================================
// LOOKUP MASTER BASE
// Daily Support ke 6 dropdown masters ke liye
// common CRUD (Index / Create / Edit / Delete /
// ToggleActive). Har master ka controller isse
// inherit karta hai aur sirf Definition deta hai.
//
// Delete guard: agar value kisi ticket me use ho
// rahi hai to delete block hota hai — user ko
// "deactivate" karne ko kaha jata hai.
// ============================================

[Authorize]
public abstract class LookupMasterControllerBase : Controller
{
    protected readonly IConfiguration Configuration;

    protected LookupMasterControllerBase(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    // Har child controller apna definition deta hai.
    protected abstract LookupMasterDefinition Definition { get; }

    // Kaunsi tables is value ko reference karti hain
    // (delete guard ke liye). Format: (Table, Column).
    protected virtual (string Table, string Column)[] UsageRefs
        => Array.Empty<(string, string)>();

    private string ConnectionString =>
        Configuration.GetConnectionString("DefaultConnection") ?? "";

    private async Task<SqlConnection> OpenAsync()
    {
        SqlConnection connection =
            new SqlConnection(ConnectionString);

        await connection.OpenAsync();

        return connection;
    }

    // Is value ko kitne tickets use kar rahe hain.
    protected virtual async Task<int> CountUsageAsync(
        SqlConnection connection, int id)
    {
        int total = 0;

        foreach (var (table, column) in UsageRefs)
        {
            // Table/column names code se aate hain (safe),
            // values always parameterised.
            string sql =
                $"SELECT COUNT(*) FROM [{table}] WHERE [{column}] = @Id";

            using SqlCommand command =
                new SqlCommand(sql, connection);

            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@Id", id);

            object? result = await command.ExecuteScalarAsync();

            if (result != null && result != DBNull.Value)
            {
                total += Convert.ToInt32(result);
            }
        }

        return total;
    }

    // ============================================
    // LIST + KPIs
    // ============================================

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var def = Definition;

        var model = new LookupMasterViewModel
        {
            Definition = def
        };

        using SqlConnection connection = await OpenAsync();

        // Parent dropdown options (agar ho).
        if (!string.IsNullOrEmpty(def.ParentTable))
        {
            string parentSql =
                $"SELECT [{def.ParentIdColumn}], [{def.ParentNameColumn}] " +
                $"FROM [{def.ParentTable}] " +
                $"WHERE [{def.ActiveColumn}] = 1 " +
                $"ORDER BY [{def.ParentNameColumn}]";

            using SqlCommand command =
                new SqlCommand(parentSql, connection);

            command.CommandTimeout = 0;

            using SqlDataReader reader =
                await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                model.ParentOptions.Add(new LookupOption
                {
                    Id = Convert.ToInt32(reader[def.ParentIdColumn!]),
                    Name = ToStr(reader[def.ParentNameColumn!])
                });
            }
        }

        // Main rows.
        string sortSelect = def.HasSortOrder
            ? $", t.[{def.SortColumn}] AS RowSort"
            : "";

        string activeSelect = def.HasIsActive
            ? $", t.[{def.ActiveColumn}] AS RowActive"
            : "";

        string extraNumSelect =
            string.IsNullOrEmpty(def.ExtraNumberColumn)
                ? ""
                : $", t.[{def.ExtraNumberColumn}] AS RowExtraNum";

        string extraBoolSelect =
            string.IsNullOrEmpty(def.ExtraBoolColumn)
                ? ""
                : $", t.[{def.ExtraBoolColumn}] AS RowExtraBool";

        string parentSelect = "";
        string parentJoin = "";

        if (!string.IsNullOrEmpty(def.ParentTable))
        {
            parentSelect =
                $", p.[{def.ParentIdColumn}] AS ParentId, " +
                $"p.[{def.ParentNameColumn}] AS ParentName";

            parentJoin =
                $"LEFT JOIN [{def.ParentTable}] p " +
                $"ON p.[{def.ParentIdColumn}] = t.[{def.ParentColumn}]";
        }

        string orderBy = def.HasSortOrder
            ? $"t.[{def.SortColumn}], t.[{def.NameColumn}]"
            : $"t.[{def.NameColumn}]";

        string sql = $@"
            SELECT
                t.[{def.IdColumn}] AS Id,
                t.[{def.NameColumn}] AS Name
                {sortSelect}
                {activeSelect}
                {extraNumSelect}
                {extraBoolSelect}
                {parentSelect}
            FROM [{def.TableName}] t
            {parentJoin}
            ORDER BY {orderBy}";

        using (SqlCommand command = new SqlCommand(sql, connection))
        {
            command.CommandTimeout = 0;

            using SqlDataReader reader =
                await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                model.Rows.Add(new LookupMasterRow
                {
                    Id = Convert.ToInt32(reader["Id"]),
                    Name = ToStr(reader["Name"]),
                    SortOrder = def.HasSortOrder
                        ? Convert.ToInt32(reader["RowSort"])
                        : 0,
                    IsActive = def.HasIsActive
                        ? Convert.ToBoolean(reader["RowActive"])
                        : true,
                    ExtraNumber =
                        string.IsNullOrEmpty(def.ExtraNumberColumn)
                            ? null
                            : (reader["RowExtraNum"] == DBNull.Value
                                ? null
                                : Convert.ToInt32(
                                    reader["RowExtraNum"])),
                    ExtraBool =
                        string.IsNullOrEmpty(def.ExtraBoolColumn)
                            ? null
                            : (reader["RowExtraBool"] == DBNull.Value
                                ? null
                                : Convert.ToBoolean(
                                    reader["RowExtraBool"])),
                    ParentId =
                        string.IsNullOrEmpty(def.ParentTable)
                            ? null
                            : (reader["ParentId"] == DBNull.Value
                                ? null
                                : Convert.ToInt32(reader["ParentId"])),
                    ParentName =
                        string.IsNullOrEmpty(def.ParentTable)
                            ? ""
                            : ToStr(reader["ParentName"])
                });
            }
        }

        // Usage counts (delete guard ke liye).
        foreach (var row in model.Rows)
        {
            row.UsageCount = await CountUsageAsync(connection, row.Id);
        }

        // KPIs
        model.TotalRecords = model.Rows.Count;
        model.ActiveRecords = model.Rows.Count(r => r.IsActive);
        model.InActiveRecords =
            model.TotalRecords - model.ActiveRecords;
        model.UsedRecords = model.Rows.Count(r => r.UsageCount > 0);

        ViewData["Title"] = def.Title;

        return View("~/Views/LookupMaster/Index.cshtml", model);
    }

    // ============================================
    // CREATE
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        string name,
        int? sortOrder,
        int? extraNumber,
        bool? extraBool,
        int? parentId)
    {
        var def = Definition;

        name = name?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(name))
        {
            return Respond(false, $"{def.NameLabel} is required.");
        }

        // Parent zaroori hai agar defined hai (SubCategory -> Category).
        if (!string.IsNullOrEmpty(def.ParentTable)
            && (parentId == null || parentId <= 0))
        {
            return Respond(false, $"{def.ParentLabel} is required.");
        }

        using SqlConnection connection = await OpenAsync();

        // Duplicate name check (same parent ke andar, agar parent ho).
        string dupSql = !string.IsNullOrEmpty(def.ParentTable)
            ? $@"SELECT COUNT(*) FROM [{def.TableName}]
                 WHERE LOWER([{def.NameColumn}]) = @Name
                   AND [{def.ParentColumn}] = @Parent"
            : $@"SELECT COUNT(*) FROM [{def.TableName}]
                 WHERE LOWER([{def.NameColumn}]) = @Name";

        int nameExists;

        using (SqlCommand command =
               new SqlCommand(dupSql, connection))
        {
            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@Name", name.ToLower());

            if (!string.IsNullOrEmpty(def.ParentTable))
            {
                command.Parameters.AddWithValue(
                    "@Parent", parentId!.Value);
            }

            object? result = await command.ExecuteScalarAsync();

            nameExists = result == null || result == DBNull.Value
                ? 0
                : Convert.ToInt32(result);
        }

        if (nameExists > 0)
        {
            return Respond(
                false,
                $"This {def.NameLabel.ToLower()} already exists.");
        }

        // Dynamic INSERT columns.
        var columns = new List<string> { $"[{def.NameColumn}]" };
        var values = new List<string> { "@Name" };

        if (def.HasSortOrder)
        {
            columns.Add($"[{def.SortColumn}]");
            values.Add("@Sort");

            if (sortOrder == null)
            {
                sortOrder = await NextSortOrderAsync(connection);
            }
        }

        if (def.HasIsActive)
        {
            columns.Add($"[{def.ActiveColumn}]");
            values.Add("1");
        }

        if (!string.IsNullOrEmpty(def.ExtraNumberColumn))
        {
            columns.Add($"[{def.ExtraNumberColumn}]");
            values.Add("@ExtraNum");
        }

        if (!string.IsNullOrEmpty(def.ExtraBoolColumn))
        {
            columns.Add($"[{def.ExtraBoolColumn}]");
            values.Add("@ExtraBool");
        }

        if (!string.IsNullOrEmpty(def.ParentTable))
        {
            columns.Add($"[{def.ParentColumn}]");
            values.Add("@Parent");
        }

        string insertSql = $@"
            INSERT INTO [{def.TableName}]
                ({string.Join(", ", columns)})
            VALUES
                ({string.Join(", ", values)})";

        using (SqlCommand command =
               new SqlCommand(insertSql, connection))
        {
            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@Name", name);

            if (def.HasSortOrder)
            {
                command.Parameters.AddWithValue("@Sort", sortOrder!.Value);
            }

            if (!string.IsNullOrEmpty(def.ExtraNumberColumn))
            {
                command.Parameters.AddWithValue(
                    "@ExtraNum",
                    extraNumber ?? def.ExtraNumberDefault);
            }

            if (!string.IsNullOrEmpty(def.ExtraBoolColumn))
            {
                command.Parameters.AddWithValue(
                    "@ExtraBool", extraBool ?? false);
            }

            if (!string.IsNullOrEmpty(def.ParentTable))
            {
                command.Parameters.AddWithValue(
                    "@Parent", parentId!.Value);
            }

            await command.ExecuteNonQueryAsync();
        }

        return Respond(true, $"{def.NameLabel} added successfully.");
    }

    // ============================================
    // EDIT
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        int id,
        string name,
        int? sortOrder,
        bool? isActive,
        int? extraNumber,
        bool? extraBool,
        int? parentId)
    {
        var def = Definition;

        name = name?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(name))
        {
            return Respond(false, $"{def.NameLabel} is required.");
        }

        if (!string.IsNullOrEmpty(def.ParentTable)
            && (parentId == null || parentId <= 0))
        {
            return Respond(false, $"{def.ParentLabel} is required.");
        }

        using SqlConnection connection = await OpenAsync();

        // Row exists?
        int exists;

        using (SqlCommand command = new SqlCommand(
            $"SELECT COUNT(*) FROM [{def.TableName}] " +
            $"WHERE [{def.IdColumn}] = @Id", connection))
        {
            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@Id", id);

            object? result = await command.ExecuteScalarAsync();

            exists = result == null || result == DBNull.Value
                ? 0
                : Convert.ToInt32(result);
        }

        if (exists == 0)
        {
            return Respond(false, $"{def.NameLabel} not found.");
        }

        // Duplicate name (excluding self).
        string dupSql = !string.IsNullOrEmpty(def.ParentTable)
            ? $@"SELECT COUNT(*) FROM [{def.TableName}]
                 WHERE LOWER([{def.NameColumn}]) = @Name
                   AND [{def.IdColumn}] <> @Id
                   AND [{def.ParentColumn}] = @Parent"
            : $@"SELECT COUNT(*) FROM [{def.TableName}]
                 WHERE LOWER([{def.NameColumn}]) = @Name
                   AND [{def.IdColumn}] <> @Id";

        int nameExists;

        using (SqlCommand command =
               new SqlCommand(dupSql, connection))
        {
            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@Name", name.ToLower());
            command.Parameters.AddWithValue("@Id", id);

            if (!string.IsNullOrEmpty(def.ParentTable))
            {
                command.Parameters.AddWithValue(
                    "@Parent", parentId!.Value);
            }

            object? result = await command.ExecuteScalarAsync();

            nameExists = result == null || result == DBNull.Value
                ? 0
                : Convert.ToInt32(result);
        }

        if (nameExists > 0)
        {
            return Respond(
                false,
                $"This {def.NameLabel.ToLower()} already exists.");
        }

        // Dynamic UPDATE sets.
        var sets = new List<string> { $"[{def.NameColumn}] = @Name" };

        if (def.HasSortOrder)
        {
            sets.Add($"[{def.SortColumn}] = @Sort");
            sortOrder ??= 0;
        }

        if (def.HasIsActive)
        {
            sets.Add($"[{def.ActiveColumn}] = @Active");
        }

        if (!string.IsNullOrEmpty(def.ExtraNumberColumn))
        {
            sets.Add($"[{def.ExtraNumberColumn}] = @ExtraNum");
        }

        if (!string.IsNullOrEmpty(def.ExtraBoolColumn))
        {
            sets.Add($"[{def.ExtraBoolColumn}] = @ExtraBool");
        }

        if (!string.IsNullOrEmpty(def.ParentTable))
        {
            sets.Add($"[{def.ParentColumn}] = @Parent");
        }

        string updateSql = $@"
            UPDATE [{def.TableName}] SET
                {string.Join(", ", sets)}
            WHERE [{def.IdColumn}] = @Id";

        using (SqlCommand command =
               new SqlCommand(updateSql, connection))
        {
            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@Id", id);
            command.Parameters.AddWithValue("@Name", name);

            if (def.HasSortOrder)
            {
                command.Parameters.AddWithValue("@Sort", sortOrder!.Value);
            }

            if (def.HasIsActive)
            {
                command.Parameters.AddWithValue(
                    "@Active", isActive ?? true);
            }

            if (!string.IsNullOrEmpty(def.ExtraNumberColumn))
            {
                command.Parameters.AddWithValue(
                    "@ExtraNum",
                    extraNumber ?? def.ExtraNumberDefault);
            }

            if (!string.IsNullOrEmpty(def.ExtraBoolColumn))
            {
                command.Parameters.AddWithValue(
                    "@ExtraBool", extraBool ?? false);
            }

            if (!string.IsNullOrEmpty(def.ParentTable))
            {
                command.Parameters.AddWithValue(
                    "@Parent", parentId!.Value);
            }

            await command.ExecuteNonQueryAsync();
        }

        return Respond(true, $"{def.NameLabel} updated successfully.");
    }

    // ============================================
    // DELETE (use ho raha ho to block)
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var def = Definition;

        using SqlConnection connection = await OpenAsync();

        // Name padho.
        string name = "";

        using (SqlCommand command = new SqlCommand(
            $"SELECT [{def.NameColumn}] FROM [{def.TableName}] " +
            $"WHERE [{def.IdColumn}] = @Id", connection))
        {
            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@Id", id);

            object? result = await command.ExecuteScalarAsync();

            if (result != null && result != DBNull.Value)
            {
                name = result.ToString() ?? "";
            }
        }

        if (name == "")
        {
            return Respond(false, $"{def.NameLabel} not found.");
        }

        // Delete guard.
        int usage = await CountUsageAsync(connection, id);

        // Child tables ka check (jaise Category delete karte waqt
        // uske SubCategories dekhna). UsageRefs se aata hai.
        if (usage > 0)
        {
            return Respond(
                false,
                $"Cannot delete \"{name}\" — {usage} record(s) " +
                "already use it. Deactivate it instead.");
        }

        string sql =
            $"DELETE FROM [{def.TableName}] " +
            $"WHERE [{def.IdColumn}] = @Id";

        using (SqlCommand command = new SqlCommand(sql, connection))
        {
            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@Id", id);

            int rows = await command.ExecuteNonQueryAsync();

            return Respond(
                rows > 0,
                rows > 0
                    ? $"\"{name}\" deleted."
                    : $"{def.NameLabel} not found.");
        }
    }

    // ============================================
    // TOGGLE ACTIVE
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var def = Definition;

        if (!def.HasIsActive)
        {
            return Respond(false, "This master has no active flag.");
        }

        using SqlConnection connection = await OpenAsync();

        string sql = $@"
            UPDATE [{def.TableName}] SET
                [{def.ActiveColumn}] = CASE
                    WHEN [{def.ActiveColumn}] = 1 THEN 0 ELSE 1 END
            WHERE [{def.IdColumn}] = @Id";

        using (SqlCommand command = new SqlCommand(sql, connection))
        {
            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@Id", id);

            int rows = await command.ExecuteNonQueryAsync();

            return Respond(
                rows > 0,
                rows > 0
                    ? "Status toggled."
                    : $"{def.NameLabel} not found.");
        }
    }

    // ============================================
    // HELPERS
    // ============================================

    private async Task<int> NextSortOrderAsync(SqlConnection connection)
    {
        var def = Definition;

        using SqlCommand command = new SqlCommand(
            $"SELECT ISNULL(MAX([{def.SortColumn}]), 0) + 1 " +
            $"FROM [{def.TableName}]", connection);

        command.CommandTimeout = 0;

        object? result = await command.ExecuteScalarAsync();

        return result == null || result == DBNull.Value
            ? 1
            : Convert.ToInt32(result);
    }

    // AJAX/redirect dono handle karta hai.
    private IActionResult Respond(bool success, string message)
    {
        bool isAjax = Request.Headers["X-Requested-With"]
            == "XMLHttpRequest";

        if (isAjax)
        {
            return Json(new { success, message });
        }

        TempData[success ? "Success" : "Error"] = message;

        return RedirectToAction(nameof(Index));
    }

    private static string ToStr(object value) =>
        value == null || value == DBNull.Value
            ? ""
            : value.ToString() ?? "";
}
