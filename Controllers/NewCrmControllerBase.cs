using Master.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Master.Controllers;

[Authorize]
public class NewCrmControllerBase : Controller
{
    protected readonly IConfiguration Configuration;

    protected NewCrmControllerBase(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    protected bool IsAjax =>
        Request.Headers["X-Requested-With"] == "XMLHttpRequest";

    // IST (UTC+5:30) — users India mein hain, aur SQL server
    // apne local timezone (UTC+2) mein chalta hai, isliye server
    // time ki jagah hamesha ye use karo.
    protected static DateTime NowIst =>
        DateTime.UtcNow.AddMinutes(330);

    // SQL-side IST now: UTC se +5:30. GETUTCDATETIME() is DB par
    // blocked hai, isliye SYSUTCDATETIME() use karte hain.
    protected const string SqlNowIst =
        "DATEADD(MINUTE, 330, SYSUTCDATETIME())";

    protected int? CurrentUserId
    {
        get
        {
            var claim = User.FindFirst(
                System.Security.Claims.ClaimTypes.NameIdentifier);

            return claim != null
                && int.TryParse(claim.Value, out int id)
                ? id
                : null;
        }
    }

    protected async Task<SqlConnection> OpenAsync()
    {
        string? connectionString =
            Configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Database connection string is missing.");
        }

        var connection = new SqlConnection(connectionString);

        await connection.OpenAsync();

        return connection;
    }

    protected IActionResult Respond(bool success, string message)
    {
        if (IsAjax)
        {
            return Json(new { success, message });
        }

        TempData[success ? "Success" : "Error"] = message;

        return RedirectToAction("Index");
    }

    protected async Task<List<NewCrmStepItem>> LoadStepsAsync(
        SqlConnection connection)
    {
        var steps = new List<NewCrmStepItem>();

        using SqlCommand command = new SqlCommand(
            "SELECT [id], [step_name], [step_order] " +
            "FROM [step_master] " +
            "WHERE [is_active] = 1 " +
            "ORDER BY [step_order]",
            connection);

        command.CommandTimeout = 0;

        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            steps.Add(new NewCrmStepItem
            {
                Id = Convert.ToInt32(reader["id"]),
                StepName = reader["step_name"].ToString() ?? "",
                StepOrder = Convert.ToInt32(reader["step_order"])
            });
        }

        return steps;
    }

    protected async Task<NewCrmLeadListItem?> ReadLeadAsync(
        SqlConnection connection,
        int id)
    {
        List<NewCrmLeadListItem> rows =
            await LoadLeadsAsync(connection, "l.[id] = @Id", id);

        return rows.FirstOrDefault();
    }

    // Shared lead SELECT: ClientMaster + LoginUsers +
    // step_master LEFT JOINs (optional FKs).
    protected async Task<List<NewCrmLeadListItem>> LoadLeadsAsync(
        SqlConnection connection,
        string? extraWhere,
        int? whereId = null)
    {
        const string query = @"
            SELECT
                l.[id],
                l.[lead_no],
                l.[client_id],
                c.[ClientName],
                c.[MobileNo],
                l.[contact_no],
                l.[email],
                l.[city],
                l.[state_id],
                st.[StateName],
                l.[requirement],
                l.[source],
                l.[expected_value],
                l.[priority],
                l.[current_step],
                s.[step_name],
                s.[step_order],
                l.[status],
                l.[lost_reason],
                l.[next_followup_date],
                l.[next_remarks],
                u.[UserName] AS assigned_name,
                cb.[UserName] AS lead_by,
                l.[created_at]
            FROM [lead_master] l
            LEFT JOIN [ClientMaster] c
                ON c.[Id] = l.[client_id]
            LEFT JOIN [LoginUsers] u
                ON u.[Id] = l.[assigned_user_id]
            LEFT JOIN [LoginUsers] cb
                ON cb.[Id] = l.[created_by]
            LEFT JOIN [step_master] s
                ON s.[id] = l.[current_step]
            LEFT JOIN [States] st
                ON st.[Id] = l.[state_id]
            WHERE l.[is_active] = 1";

        var sql = query;

        if (!string.IsNullOrWhiteSpace(extraWhere))
        {
            sql += " AND " + extraWhere;
        }

        sql += " ORDER BY l.[id] DESC";

        var leads = new List<NewCrmLeadListItem>();

        using SqlCommand command = new SqlCommand(sql, connection);

        command.CommandTimeout = 0;

        if (whereId.HasValue)
        {
            command.Parameters.AddWithValue("@Id", whereId.Value);
        }

        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            leads.Add(ReadLead(reader));
        }

        return leads;
    }

    protected static NewCrmLeadListItem ReadLead(
        SqlDataReader reader) =>
        new()
        {
            Id = Convert.ToInt32(reader["id"]),
            LeadNo = ToStr(reader["lead_no"]),
            ClientId = Convert.ToInt32(reader["client_id"]),
            ClientName = ToStr(reader["ClientName"]),
            ClientMobile = ToStr(reader["MobileNo"]),
            ContactNo = ToStr(reader["contact_no"]),
            Email = ToStr(reader["email"]),
            City = ToStr(reader["city"]),
            StateId = reader["state_id"] == DBNull.Value
                ? null
                : Convert.ToInt32(reader["state_id"]),
            StateName = ToStr(reader["StateName"]),
            Requirement = ToStr(reader["requirement"]),
            Source = ToStr(reader["source"]),
            ExpectedValue = reader["expected_value"] == DBNull.Value
                ? 0
                : Convert.ToDecimal(reader["expected_value"]),
            Priority = ToStr(reader["priority"]),
            CurrentStep = Convert.ToInt32(reader["current_step"]),
            StepName = ToStr(reader["step_name"]),
            StepOrder = reader["step_order"] == DBNull.Value
                ? 0
                : Convert.ToInt32(reader["step_order"]),
            Status = ToStr(reader["status"]),
            LostReason = ToStr(reader["lost_reason"]),
            NextFollowupDate = reader["next_followup_date"] == DBNull.Value
                ? null
                : Convert.ToDateTime(reader["next_followup_date"]),
            NextRemarks = ToStr(reader["next_remarks"]),
            AssignedTo = ToStr(reader["assigned_name"]),
            LeadBy = ToStr(reader["lead_by"]),
            CreatedAt = Convert.ToDateTime(reader["created_at"])
        };

    protected static string ToStr(object value) =>
        value == DBNull.Value
            ? ""
            : value.ToString()?.Trim() ?? "";

    protected static int ReadInt(
        SqlDataReader reader,
        string column) =>
        reader[column] == DBNull.Value
            ? 0
            : Convert.ToInt32(reader[column]);

    // LEAD-0001, LEAD-0002 ...
    protected async Task<string> GenerateLeadNoAsync(
        SqlConnection connection)
    {
        const string query = @"
            SELECT ISNULL(MAX(
                CASE
                    WHEN ISNUMERIC(
                        REPLACE(lead_no, 'LEAD-', '')) = 1
                    THEN CAST(
                        REPLACE(lead_no, 'LEAD-', '') AS INT)
                    ELSE 0
                END), 0) + 1
            FROM [lead_master]
            WHERE lead_no LIKE 'LEAD-%'";

        using SqlCommand command = new SqlCommand(query, connection);

        command.CommandTimeout = 0;

        object? result = await command.ExecuteScalarAsync();

        int next = result == null || result == DBNull.Value
            ? 1
            : Convert.ToInt32(result);

        return $"LEAD-{next:D4}";
    }

    protected async Task<string> GetUserNameAsync(
        SqlConnection connection,
        int? userId)
    {
        if (userId == null || userId <= 0)
        {
            return "";
        }

        using SqlCommand command = new SqlCommand(
            "SELECT [UserName] FROM [LoginUsers] WHERE [Id] = @Id",
            connection);

        command.CommandTimeout = 0;
        command.Parameters.AddWithValue("@Id", userId.Value);

        object? result = await command.ExecuteScalarAsync();

        return result == null || result == DBNull.Value
            ? ""
            : result.ToString() ?? "";
    }

    // ============================================
    // TIME LOG HELPERS (auto follow-up logging)
    // ============================================

    // Current user ka us lead par LIVE timer
    // (meeting_out NULL). Agar ho toh use kar lete hain.
    protected static async Task<bool> StopLiveTimerForLeadAsync(
        SqlConnection connection,
        int userId,
        int leadId,
        string remarks)
    {
        const string sql = @"
            UPDATE [lead_time_log] SET
                [meeting_out] = DATEADD(MINUTE, 330, SYSUTCDATETIME()),
                [duration_min] = DATEDIFF(
                    MINUTE, [meeting_in],
                    DATEADD(MINUTE, 330, SYSUTCDATETIME())),
                [notes] = CASE
                    WHEN @Remarks IS NULL THEN [notes]
                    WHEN [notes] IS NULL THEN @Remarks
                    ELSE [notes] + ' | ' + @Remarks END
            WHERE [user_id] = @UserId
              AND [lead_id] = @LeadId
              AND [meeting_out] IS NULL";

        using SqlCommand command = new SqlCommand(sql, connection);

        command.CommandTimeout = 0;
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@LeadId", leadId);
        command.Parameters.AddWithValue(
            "@Remarks",
            string.IsNullOrWhiteSpace(remarks)
                ? DBNull.Value
                : remarks);

        int rows = await command.ExecuteNonQueryAsync();

        return rows > 0;
    }

    // Time Log entry auto-banao (follow-up actions par).
    // LIVE timer mila toh actual time, warna fallbackMinutes.
    protected async Task LogTimeAsync(
        SqlConnection connection,
        int leadId,
        string meetingType,
        string remarks,
        int? manualMinutes = null)
    {
        if (CurrentUserId is not int userId || userId <= 0)
        {
            return;
        }

        // Pehle LIVE timer auto-stop karo (single timer rule).
        bool stopped = await StopLiveTimerForLeadAsync(
            connection, userId, leadId, remarks);

        if (stopped)
        {
            // Actual time already update ho chuka hai.
            return;
        }

        int minutes = manualMinutes ?? 15;

        if (minutes <= 0)
        {
            minutes = 15;
        }

        const string sql = @"
            INSERT INTO [lead_time_log]
                ([lead_id], [user_id], [meeting_type],
                 [meeting_in], [meeting_out],
                 [duration_min], [notes])
            VALUES
                (@LeadId, @UserId, @Type,
                 DATEADD(MINUTE, -@Minutes,
                     DATEADD(MINUTE, 330, SYSUTCDATETIME())),
                 DATEADD(MINUTE, 330, SYSUTCDATETIME()),
                 @Minutes, @Notes)";

        using SqlCommand command = new SqlCommand(sql, connection);

        command.CommandTimeout = 0;
        command.Parameters.AddWithValue("@LeadId", leadId);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@Type", meetingType);
        command.Parameters.AddWithValue("@Minutes", minutes);
        command.Parameters.AddWithValue(
            "@Notes",
            string.IsNullOrWhiteSpace(remarks)
                ? DBNull.Value
                : remarks);

        await command.ExecuteNonQueryAsync();
    }
}
