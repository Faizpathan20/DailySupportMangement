using Master.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Master.Controllers;

// ============================================
// STEP MASTER (New CRM pipeline steps)
// Purane masters (States) jaisa style:
// KPI + toolbar + table + modal CRUD.
// ============================================

[Authorize]
public class StepMasterController : NewCrmControllerBase
{
    public StepMasterController(IConfiguration configuration)
        : base(configuration)
    {
    }

    private const string TableName = "step_master";

    // ============================================
    // LIST + KPIs
    // ============================================

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        using SqlConnection connection = await OpenAsync();

        var model = new StepMasterViewModel();

        // ---- Steps with lead usage count ----
        const string query = @"
            SELECT
                s.[id],
                s.[step_name],
                s.[step_order],
                s.[is_active],
                (SELECT COUNT(*)
                 FROM [lead_master] l
                 WHERE l.[current_step] = s.[id]
                   AND l.[is_active] = 1) AS lead_count
            FROM [step_master] s
            ORDER BY s.[step_order]";

        using (SqlCommand command =
               new SqlCommand(query, connection))
        {
            command.CommandTimeout = 0;

            using SqlDataReader reader =
                await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                model.Steps.Add(new StepMasterRow
                {
                    Id = Convert.ToInt32(reader["id"]),
                    StepName = ToStr(reader["step_name"]),
                    StepOrder = Convert.ToInt32(reader["step_order"]),
                    IsActive = Convert.ToBoolean(reader["is_active"]),
                    LeadCount = Convert.ToInt32(reader["lead_count"])
                });
            }
        }

        // ---- KPIs ----
        model.TotalRecords = model.Steps.Count;
        model.ActiveRecords = model.Steps.Count(s => s.IsActive);
        model.InActiveRecords = model.TotalRecords - model.ActiveRecords;
        model.UsedRecords = model.Steps.Count(s => s.LeadCount > 0);

        return View(model);
    }

    // ============================================
    // CREATE
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        string stepName,
        int stepOrder)
    {
        stepName = stepName?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(stepName))
        {
            return Respond(false, "Step name is required.");
        }

        if (stepOrder <= 0)
        {
            return Respond(false, "Step order must be greater than 0.");
        }

        using SqlConnection connection = await OpenAsync();

        // Duplicate name check.
        int nameExists = await ScalarIntAsync(connection, @"
            SELECT COUNT(*) FROM [step_master]
            WHERE LOWER([step_name]) = @Name",
            ("@Name", stepName.ToLower()));

        if (nameExists > 0)
        {
            return Respond(
                false,
                "This step name already exists. Please use a new one.");
        }

        // Duplicate order check (UNIQUE constraint friendly message).
        int orderExists = await ScalarIntAsync(connection, @"
            SELECT COUNT(*) FROM [step_master]
            WHERE [step_order] = @Order",
            ("@Order", stepOrder));

        if (orderExists > 0)
        {
            return Respond(
                false,
                $"Step order {stepOrder} is already used by another step. " +
                "Please pick a different order.");
        }

        const string sql = @"
            INSERT INTO [step_master]
                ([step_name], [step_order], [is_active])
            VALUES
                (@Name, @Order, 1)";

        using (SqlCommand command =
               new SqlCommand(sql, connection))
        {
            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@Name", stepName);
            command.Parameters.AddWithValue("@Order", stepOrder);

            await command.ExecuteNonQueryAsync();
        }

        return Respond(true, "Step added successfully.");
    }

    // ============================================
    // EDIT (name / order / active)
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        int id,
        string stepName,
        int stepOrder,
        bool isActive)
    {
        stepName = stepName?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(stepName))
        {
            return Respond(false, "Step name is required.");
        }

        if (stepOrder <= 0)
        {
            return Respond(false, "Step order must be greater than 0.");
        }

        using SqlConnection connection = await OpenAsync();

        // Row exists?
        int exists = await ScalarIntAsync(connection, @"
            SELECT COUNT(*) FROM [step_master]
            WHERE [id] = @Id",
            ("@Id", id));

        if (exists == 0)
        {
            return Respond(false, "Step not found.");
        }

        // Duplicate name (excluding self).
        int nameExists = await ScalarIntAsync(connection, @"
            SELECT COUNT(*) FROM [step_master]
            WHERE LOWER([step_name]) = @Name
              AND [id] <> @Id",
            ("@Name", stepName.ToLower()),
            ("@Id", id));

        if (nameExists > 0)
        {
            return Respond(
                false,
                "This step name already exists. Please use a new one.");
        }

        // Duplicate order (excluding self).
        int orderExists = await ScalarIntAsync(connection, @"
            SELECT COUNT(*) FROM [step_master]
            WHERE [step_order] = @Order
              AND [id] <> @Id",
            ("@Order", stepOrder),
            ("@Id", id));

        if (orderExists > 0)
        {
            return Respond(
                false,
                $"Step order {stepOrder} is already used by another step. " +
                "Please pick a different order.");
        }

        const string sql = @"
            UPDATE [step_master] SET
                [step_name] = @Name,
                [step_order] = @Order,
                [is_active] = @Active
            WHERE [id] = @Id";

        using (SqlCommand command =
               new SqlCommand(sql, connection))
        {
            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@Id", id);
            command.Parameters.AddWithValue("@Name", stepName);
            command.Parameters.AddWithValue("@Order", stepOrder);
            command.Parameters.AddWithValue("@Active", isActive);

            await command.ExecuteNonQueryAsync();
        }

        return Respond(true, "Step updated successfully.");
    }

    // ============================================
    // DELETE (lead use kar rahi ho toh block)
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        using SqlConnection connection = await OpenAsync();

        // Step exists?
        (string name, int order) =
            await ReadStepAsync(connection, id);

        if (name == "")
        {
            return Respond(false, "Step not found.");
        }

        // Lead usage check (delete guard).
        int leadCount = await ScalarIntAsync(connection, @"
            SELECT COUNT(*) FROM [lead_master]
            WHERE [current_step] = @Id
              AND [is_active] = 1",
            ("@Id", id));

        if (leadCount > 0)
        {
            return Respond(
                false,
                $"Cannot delete \"{name}\" — {leadCount} lead(s) " +
                "are on this step. Deactivate it instead " +
                "(leads will keep working).");
        }

        const string sql = @"
            DELETE FROM [step_master]
            WHERE [id] = @Id";

        using (SqlCommand command =
               new SqlCommand(sql, connection))
        {
            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@Id", id);

            int rows = await command.ExecuteNonQueryAsync();

            return Respond(
                rows > 0,
                rows > 0
                    ? $"Step \"{name}\" deleted."
                    : "Step not found.");
        }
    }

    // ============================================
    // TOGGLE ACTIVE (quick activate / deactivate)
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        using SqlConnection connection = await OpenAsync();

        const string sql = @"
            UPDATE [step_master] SET
                [is_active] = CASE
                    WHEN [is_active] = 1 THEN 0 ELSE 1 END
            WHERE [id] = @Id";

        using (SqlCommand command =
               new SqlCommand(sql, connection))
        {
            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@Id", id);

            int rows = await command.ExecuteNonQueryAsync();

            return Respond(
                rows > 0,
                rows > 0
                    ? "Step status toggled."
                    : "Step not found.");
        }
    }

    // ============================================
    // HELPERS
    // ============================================

    private static async Task<(string Name, int Order)>
        ReadStepAsync(SqlConnection connection, int id)
    {
        using SqlCommand command = new SqlCommand(
            "SELECT [step_name], [step_order] " +
            "FROM [step_master] WHERE [id] = @Id",
            connection);

        command.CommandTimeout = 0;
        command.Parameters.AddWithValue("@Id", id);

        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return ("", 0);
        }

        return (
            ToStr(reader["step_name"]),
            Convert.ToInt32(reader["step_order"]));
    }

    private static async Task<int> ScalarIntAsync(
        SqlConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        using SqlCommand command =
            new SqlCommand(sql, connection);

        command.CommandTimeout = 0;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        object? result = await command.ExecuteScalarAsync();

        return result == null || result == DBNull.Value
            ? 0
            : Convert.ToInt32(result);
    }
}
