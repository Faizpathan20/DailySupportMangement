using Master.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Master.Controllers;

// ============================================
// NEW CRM  -  FOLLOW-UP PAGE
// Daily follow-up workflow:
//   Overdue / Due Today / Upcoming / Done
// Mark Done + Reschedule ke saath.
// ============================================

[Authorize]
public class NewCrmFollowUpController : NewCrmControllerBase
{
    public NewCrmFollowUpController(IConfiguration configuration)
        : base(configuration)
    {
    }

    // ============================================
    // LIST (tabs + KPIs)
    // ============================================

    [HttpGet]
    public async Task<IActionResult> Index(
        string? tab,
        string? user)
    {
        using SqlConnection connection = await OpenAsync();

        var model = new NewCrmFollowUpIndexModel
        {
            Tab = string.IsNullOrWhiteSpace(tab) ? "due" : tab,
            UserFilter = user
        };

        // ---- KPIs ----
        await LoadKpisAsync(connection, model);

        // ---- Rows ----
        if (model.Tab == "done")
        {
            model.DoneRows =
                await LoadDoneAsync(connection, user);
        }
        else
        {
            model.Leads =
                await LoadLeadsAsync(connection, model.Tab, user);
        }

        // ---- Users dropdown ----
        model.Users = await LoadUsersAsync(connection);

        return View(model);
    }

    // ============================================
    // MARK DONE (follow-up poori hui)
    // next_followup_date NULL + log entry +
    // AUTO TIME LOG entry.
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkDone(
        int id,
        string remarks,
        int minutes)
    {
        if (CurrentUserId is not int userId || userId <= 0)
        {
            return Respond(
                false,
                "Your login session has expired. Please login again.");
        }

        remarks = remarks?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(remarks))
        {
            return Respond(false, "Remarks are required.");
        }

        if (minutes <= 0)
        {
            minutes = 15;
        }

        using SqlConnection connection = await OpenAsync();

        NewCrmLeadListItem? lead =
            await ReadLeadAsync(connection, id);

        if (lead == null)
        {
            return Respond(false, "Lead not found.");
        }

        // 1) Lead ki pending follow-up clear.
        const string updateSql = @"
            UPDATE [lead_master] SET
                [next_followup_date] = NULL,
                [next_remarks] = NULL,
                [updated_at] = SYSDATETIME()
            WHERE [id] = @Id";

        using (SqlCommand command =
               new SqlCommand(updateSql, connection))
        {
            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@Id", id);

            await command.ExecuteNonQueryAsync();
        }

        // 2) Follow-up log (status = Done).
        const string logSql = @"
            INSERT INTO [lead_followup]
                ([lead_id], [followup_date], [remarks],
                 [created_by], [status], [done_on], [done_by])
            VALUES
                (@LeadId, CAST(GETDATE() AS DATE), @Remarks,
                 @UserId, 'Done', SYSDATETIME(), @UserId)";

        using (SqlCommand command =
               new SqlCommand(logSql, connection))
        {
            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@LeadId", id);
            command.Parameters.AddWithValue("@Remarks", remarks);
            command.Parameters.AddWithValue("@UserId", userId);

            await command.ExecuteNonQueryAsync();
        }

        // 3) AUTO TIME LOG (LIVE timer hoga toh actual time).
        await LogTimeAsync(
            connection, id, "Follow-up",
            "Follow-up done: " + remarks, minutes);

        return Respond(
            true,
            $"Follow-up done for {lead.LeadNo}. Time logged ✅");
    }

    // ============================================
    // RESCHEDULE (nayi date + remarks)
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reschedule(
        int id,
        string nextDate,
        string remarks,
        int minutes)
    {
        if (CurrentUserId is not int userId || userId <= 0)
        {
            return Respond(
                false,
                "Your login session has expired. Please login again.");
        }

        if (!DateTime.TryParse(nextDate, out DateTime date))
        {
            return Respond(false, "Next follow-up date is required.");
        }

        remarks = remarks?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(remarks))
        {
            return Respond(false, "Remarks are required.");
        }

        using SqlConnection connection = await OpenAsync();

        NewCrmLeadListItem? lead =
            await ReadLeadAsync(connection, id);

        if (lead == null)
        {
            return Respond(false, "Lead not found.");
        }

        // 1) Lead par nayi follow-up date.
        const string updateSql = @"
            UPDATE [lead_master] SET
                [next_followup_date] = @Date,
                [next_remarks] = @Remarks,
                [updated_at] = SYSDATETIME()
            WHERE [id] = @Id";

        using (SqlCommand command =
               new SqlCommand(updateSql, connection))
        {
            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@Id", id);
            command.Parameters.AddWithValue("@Date", date);
            command.Parameters.AddWithValue("@Remarks", remarks);

            await command.ExecuteNonQueryAsync();
        }

        // 2) Log entry (Pending - nayi date).
        const string logSql = @"
            INSERT INTO [lead_followup]
                ([lead_id], [followup_date], [remarks],
                 [created_by], [status])
            VALUES
                (@LeadId, @Date, @Remarks,
                 @UserId, 'Pending')";

        using (SqlCommand command =
               new SqlCommand(logSql, connection))
        {
            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@LeadId", id);
            command.Parameters.AddWithValue("@Date", date);
            command.Parameters.AddWithValue("@Remarks", remarks);
            command.Parameters.AddWithValue("@UserId", userId);

            await command.ExecuteNonQueryAsync();
        }

        // 3) AUTO TIME LOG (har reschedule par entry —
        // LIVE timer hoga toh actual time, warna minutes).
        await LogTimeAsync(
            connection, id, "Follow-up",
            "Rescheduled: " + remarks, minutes);

        return Respond(
            true,
            $"Next follow-up: {date:dd-MM-yyyy} for {lead.LeadNo}.");
    }

    // ============================================
    // DUE COUNT (sidebar badge ke liye)
    // ============================================

    [HttpGet]
    public async Task<IActionResult> DueCount()
    {
        using SqlConnection connection = await OpenAsync();

        const string sql = @"
            SELECT COUNT(*) FROM [lead_master]
            WHERE [is_active] = 1
              AND [status] = 'Open'
              AND [next_followup_date] IS NOT NULL
              AND [next_followup_date] <= CAST(GETDATE() AS DATE)";

        using SqlCommand command = new SqlCommand(sql, connection);

        command.CommandTimeout = 0;

        object? result = await command.ExecuteScalarAsync();

        int due = result == null || result == DBNull.Value
            ? 0
            : Convert.ToInt32(result);

        return Json(new { due });
    }

    // ============================================
    // HELPERS
    // ============================================

    private async Task LoadKpisAsync(
        SqlConnection connection,
        NewCrmFollowUpIndexModel model)
    {
        const string sql = @"
            SELECT
                (SELECT COUNT(*) FROM [lead_master]
                 WHERE [is_active] = 1
                   AND [status] = 'Open'
                   AND [next_followup_date] IS NOT NULL
                   AND [next_followup_date]
                       < CAST(GETDATE() AS DATE))
                    AS OverdueCount,

                (SELECT COUNT(*) FROM [lead_master]
                 WHERE [is_active] = 1
                   AND [status] = 'Open'
                   AND [next_followup_date]
                       = CAST(GETDATE() AS DATE))
                    AS DueTodayCount,

                (SELECT COUNT(*) FROM [lead_master]
                 WHERE [is_active] = 1
                   AND [status] = 'Open'
                   AND [next_followup_date] IS NOT NULL
                   AND [next_followup_date]
                       > CAST(GETDATE() AS DATE))
                    AS UpcomingCount,

                (SELECT COUNT(*) FROM [lead_followup]
                 WHERE [status] = 'Done'
                   AND [done_on]
                       >= CAST(GETDATE() AS DATE))
                    AS DoneTodayCount";

        using SqlCommand command = new SqlCommand(sql, connection);

        command.CommandTimeout = 0;

        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            model.OverdueCount = ReadInt(reader, "OverdueCount");
            model.DueTodayCount = ReadInt(reader, "DueTodayCount");
            model.UpcomingCount = ReadInt(reader, "UpcomingCount");
            model.DoneTodayCount = ReadInt(reader, "DoneTodayCount");
        }
    }

    private async Task<List<NewCrmLeadListItem>> LoadLeadsAsync(
        SqlConnection connection,
        string tab,
        string? user)
    {
        // Tab -> date condition (server clock).
        string dateCondition = tab switch
        {
            "overdue" => @"l.[next_followup_date]
                            < CAST(GETDATE() AS DATE)",
            "upcoming" => @"l.[next_followup_date]
                             > CAST(GETDATE() AS DATE)",
            _ => @"l.[next_followup_date]
                    = CAST(GETDATE() AS DATE)"
        };

        int uid = 0;

        string userCondition = "";

        if (!string.IsNullOrWhiteSpace(user)
            && int.TryParse(user, out uid)
            && uid > 0)
        {
            userCondition = " AND l.[assigned_user_id] = @UserId";
        }
        else
        {
            uid = 0;
        }

        // LoadLeadsAsync (base) ka shared SELECT.
        string query = @"
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
            WHERE l.[is_active] = 1
              AND l.[status] = 'Open'
              AND l.[next_followup_date] IS NOT NULL
              AND " + dateCondition + userCondition + @"
            ORDER BY l.[next_followup_date]";

        using SqlCommand command = new SqlCommand(query, connection);

        command.CommandTimeout = 0;

        if (!string.IsNullOrEmpty(userCondition))
        {
            command.Parameters.AddWithValue("@UserId", uid);
        }

        var leads = new List<NewCrmLeadListItem>();

        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            leads.Add(ReadLead(reader));
        }

        return leads;
    }

    private async Task<List<NewCrmFollowUpDoneRow>> LoadDoneAsync(
        SqlConnection connection,
        string? user)
    {
        int uid = 0;

        string userCondition = "";

        if (!string.IsNullOrWhiteSpace(user)
            && int.TryParse(user, out uid)
            && uid > 0)
        {
            userCondition = " AND f.[done_by] = @UserId";
        }
        else
        {
            uid = 0;
        }

        string sql = @"
            SELECT TOP 300
                f.[id],
                f.[lead_id],
                l.[lead_no],
                c.[ClientName],
                c.[MobileNo],
                f.[followup_date],
                f.[remarks],
                f.[done_on],
                du.[UserName] AS done_by_name
            FROM [lead_followup] f
            JOIN [lead_master] l
                ON l.[id] = f.[lead_id]
            LEFT JOIN [ClientMaster] c
                ON c.[Id] = l.[client_id]
            LEFT JOIN [LoginUsers] du
                ON du.[Id] = f.[done_by]
            WHERE f.[status] = 'Done'" + userCondition + @"
            ORDER BY f.[done_on] DESC";

        using SqlCommand command = new SqlCommand(sql, connection);

        command.CommandTimeout = 0;

        if (!string.IsNullOrEmpty(userCondition))
        {
            command.Parameters.AddWithValue("@UserId", uid);
        }

        var rows = new List<NewCrmFollowUpDoneRow>();

        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new NewCrmFollowUpDoneRow
            {
                Id = Convert.ToInt32(reader["id"]),
                LeadId = Convert.ToInt32(reader["lead_id"]),
                LeadNo = ToStr(reader["lead_no"]),
                ClientName = ToStr(reader["ClientName"]),
                ContactNo = ToStr(reader["MobileNo"]),
                FollowupDate = Convert.ToDateTime(
                    reader["followup_date"]),
                Remarks = ToStr(reader["remarks"]),
                DoneOn = reader["done_on"] == DBNull.Value
                    ? null
                    : Convert.ToDateTime(reader["done_on"]),
                DoneByName = ToStr(reader["done_by_name"])
            });
        }

        return rows;
    }

    private static async Task<List<(int Id, string Name)>>
        LoadUsersAsync(SqlConnection connection)
    {
        var users = new List<(int, string)>();

        using SqlCommand command = new SqlCommand(
            "SELECT [Id], [UserName] " +
            "FROM [LoginUsers] " +
            "ORDER BY [UserName]",
            connection);

        command.CommandTimeout = 0;

        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            users.Add((
                Convert.ToInt32(reader["Id"]),
                ToStr(reader["UserName"])));
        }

        return users;
    }
}
