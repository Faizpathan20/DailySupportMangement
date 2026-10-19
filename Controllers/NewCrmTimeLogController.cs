using Master.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Master.Controllers;

// ============================================
// TIME LOG (Meeting In / Out tracking)
// - Single LIVE timer per user (server-side).
// - Manual entry (date + in + out).
// - Daily report (user-wise, date-wise).
// ============================================

[Authorize]
public class NewCrmTimeLogController : NewCrmControllerBase
{
    public NewCrmTimeLogController(IConfiguration configuration)
        : base(configuration)
    {
    }

    private static readonly string[] MeetingTypes =
        { "Meeting", "Call", "Visit" };

    // ============================================
    // LIST + LIVE TIMER + KPIs + DAILY REPORT
    // ============================================

    [HttpGet]
    public async Task<IActionResult> Index(
        DateTime? fromDate,
        DateTime? toDate,
        string? user,
        string? search,
        int? leadId)
    {
        using SqlConnection connection = await OpenAsync();

        var model = new NewCrmTimeLogIndexModel
        {
            FromDate = fromDate,
            ToDate = toDate,
            UserFilter = user,
            Search = search,
            LeadId = leadId
        };

        // ---- Current user's LIVE timer ----
        model.ActiveTimer =
            await LoadActiveTimerAsync(connection);

        // ---- Entries (filters applied) ----
        model.Entries =
            await LoadEntriesAsync(connection, model);

        // ---- KPIs ----
        await LoadKpisAsync(connection, model);

        // ---- Daily report (from filtered entries) ----
        BuildDailyReport(model);

        // ---- Dropdowns ----
        model.Users = await LoadUsersAsync(connection);
        model.Leads = await LoadLeadsDropdownAsync(connection);

        return View(model);
    }

    // ============================================
    // START TIMER (single per user)
    // ====================================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StartTimer(
        int leadId,
        string meetingType,
        string? notes)
    {
        if (CurrentUserId is not int userId || userId <= 0)
        {
            return Respond(false, "Your login session has expired. Please log in again.");
        }

        if (leadId <= 0)
        {
            return Respond(false, "Please select a lead.");
        }

        if (!MeetingTypes.Contains(meetingType))
        {
            meetingType = "Meeting";
        }

        notes = notes?.Trim() ?? "";

        using SqlConnection connection = await OpenAsync();

        // Lead exists & active?
        int leadExists = await ScalarIntAsync(connection, @"
            SELECT COUNT(*) FROM [lead_master]
            WHERE [id] = @Id AND [is_active] = 1",
            ("@Id", leadId));

        if (leadExists == 0)
        {
            return Respond(false, "Lead not found.");
        }

        // Previous LIVE timer? -> auto-stop (single timer rule).
        await AutoStopTimerAsync(connection, userId,
            "Auto-stopped: a new timer was started.");

        // Start a new timer.
        const string sql = @"
            INSERT INTO [lead_time_log]
                ([lead_id], [user_id], [meeting_type],
                 [meeting_in], [notes])
            VALUES
                (@LeadId, @UserId, @Type,
                 DATEADD(MINUTE, 330, SYSUTCDATETIME()), @Notes)";

        using (SqlCommand command =
               new SqlCommand(sql, connection))
        {
            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@LeadId", leadId);
            command.Parameters.AddWithValue("@UserId", userId);
            command.Parameters.AddWithValue("@Type", meetingType);
            command.Parameters.AddWithValue(
                "@Notes",
                string.IsNullOrWhiteSpace(notes)
                    ? DBNull.Value
                    : notes);

            await command.ExecuteNonQueryAsync();
        }

        return Respond(true, "Timer started. All the best! ⏱");
    }

    // ============================================
    // STOP TIMER (current user's LIVE timer)
    // id provided -> stop that specific entry only; otherwise stop
    // all LIVE entries for the current user (single timer rule).
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StopTimer(
        string? notes,
        int? id = null)
    {
        if (CurrentUserId is not int userId || userId <= 0)
        {
            return Respond(false, "Your login session has expired. Please log in again.");
        }

        notes = notes?.Trim() ?? "";

        using SqlConnection connection = await OpenAsync();

        bool stopped = await AutoStopTimerAsync(
            connection, userId,
            string.IsNullOrWhiteSpace(notes)
                ? null
                : notes,
            id);

        if (!stopped)
        {
            // Timer already stopped by another tab/action —
            // reload the page to clear the stale LIVE card.
            return Json(new
            {
                success = false,
                reload = true,
                message =
                    "No active timer found (it may already be stopped). The page will refresh."
            });
        }

        return Respond(
            true,
            "Meeting saved. Time logged! ✅");
    }

    // ============================================
    // SAVE MANUAL ENTRY (date + in + out)
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveManual(
        int leadId,
        string meetingType,
        string entryDate,
        string inTime,
        string outTime,
        string? notes)
    {
        if (CurrentUserId is not int userId || userId <= 0)
        {
            return Respond(false, "Your login session has expired. Please log in again.");
        }

        if (leadId <= 0)
        {
            return Respond(false, "Please select a lead.");
        }

        if (!MeetingTypes.Contains(meetingType))
        {
            meetingType = "Meeting";
        }

        // Date + In time (compulsory).
        if (!DateTime.TryParse(
                entryDate + " " + inTime,
                out DateTime meetingIn))
        {
            return Respond(
                false,
                "Please enter a valid date and meeting start time.");
        }

        // Out time (optional - blank = LIVE).
        DateTime? meetingOut = null;

        if (!string.IsNullOrWhiteSpace(outTime))
        {
            if (!DateTime.TryParse(
                    entryDate + " " + outTime,
                    out DateTime parsedOut))
            {
                return Respond(
                    false,
                    "Please enter a valid meeting end time.");
            }

            // Out must be after In.
            if (parsedOut <= meetingIn)
            {
                return Respond(
                    false,
                    "Meeting out time must be after meeting in time.");
            }

            meetingOut = parsedOut;
        }

        // Future time check (IST).
        if (meetingIn > NowIst.AddMinutes(5))
        {
            return Respond(
                false,
                "Meeting in time cannot be in the future.");
        }

        if (meetingOut.HasValue &&
            meetingOut.Value > NowIst.AddMinutes(5))
        {
            return Respond(
                false,
                "Meeting out time cannot be in the future.");
        }

        notes = notes?.Trim() ?? "";

        using SqlConnection connection = await OpenAsync();

        // Lead exists?
        int leadExists = await ScalarIntAsync(connection, @"
            SELECT COUNT(*) FROM [lead_master]
            WHERE [id] = @Id AND [is_active] = 1",
            ("@Id", leadId));

        if (leadExists == 0)
        {
            return Respond(false, "Lead not found.");
        }

        int? duration = meetingOut.HasValue
            ? (int)(meetingOut.Value - meetingIn).TotalMinutes
            : null;

        const string sql = @"
            INSERT INTO [lead_time_log]
                ([lead_id], [user_id], [meeting_type],
                 [meeting_in], [meeting_out],
                 [duration_min], [notes])
            VALUES
                (@LeadId, @UserId, @Type,
                 @In, @Out, @Duration, @Notes)";

        using (SqlCommand command =
               new SqlCommand(sql, connection))
        {
            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@LeadId", leadId);
            command.Parameters.AddWithValue("@UserId", userId);
            command.Parameters.AddWithValue("@Type", meetingType);
            command.Parameters.AddWithValue("@In", meetingIn);
            command.Parameters.AddWithValue(
                "@Out",
                meetingOut.HasValue
                    ? (object)meetingOut.Value
                    : DBNull.Value);
            command.Parameters.AddWithValue(
                "@Duration",
                (object?)duration ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "@Notes",
                string.IsNullOrWhiteSpace(notes)
                    ? DBNull.Value
                    : notes);

            await command.ExecuteNonQueryAsync();
        }

        return Respond(true, "Time entry saved.");
    }

    // ============================================
    // DELETE ENTRY
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        using SqlConnection connection = await OpenAsync();

        const string sql = @"
            DELETE FROM [lead_time_log]
            WHERE [id] = @Id
              AND [user_id] = @UserId";

        using SqlCommand command =
            new SqlCommand(sql, connection);

        command.CommandTimeout = 0;
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue(
            "@UserId", CurrentUserId ?? 0);

        int rows = await command.ExecuteNonQueryAsync();

        return Respond(
            rows > 0,
            rows > 0
                ? "Entry deleted."
                : "Entry not found (or it does not belong to you).");
    }

    // ============================================
    // LIVE STATUS (sidebar badge ke liye, lightweight)
    // ============================================

    [HttpGet]
    public async Task<IActionResult> LiveStatus()
    {
        if (CurrentUserId is not int userId || userId <= 0)
        {
            return Json(new { live = false });
        }

        using SqlConnection connection = await OpenAsync();

        int count = await ScalarIntAsync(connection, @"
            SELECT COUNT(*) FROM [lead_time_log]
            WHERE [user_id] = @UserId
              AND [meeting_out] IS NULL",
            ("@UserId", userId));

        return Json(new { live = count > 0 });
    }

    // ============================================
    // HELPERS
    // ============================================

    // Current user's LIVE timer (if any).
    private async Task<NewCrmTimeLogItem?>
        LoadActiveTimerAsync(SqlConnection connection)
    {
        if (CurrentUserId is not int userId || userId <= 0)
        {
            return null;
        }

        const string query = @"
            SELECT TOP 1
                t.[id], t.[lead_id], t.[user_id],
                t.[meeting_type], t.[meeting_in],
                t.[meeting_out], t.[duration_min],
                t.[notes], t.[created_at],
                l.[lead_no], c.[ClientName],
                u.[UserName]
            FROM [lead_time_log] t
            LEFT JOIN [lead_master] l
                ON l.[id] = t.[lead_id]
            LEFT JOIN [ClientMaster] c
                ON c.[Id] = l.[client_id]
            LEFT JOIN [LoginUsers] u
                ON u.[Id] = t.[user_id]
            WHERE t.[user_id] = @UserId
              AND t.[meeting_out] IS NULL
            ORDER BY t.[meeting_in] DESC";

        using SqlCommand command =
            new SqlCommand(query, connection);

        command.CommandTimeout = 0;
        command.Parameters.AddWithValue("@UserId", userId);

        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return ReadEntry(reader);
    }

    // Entries with filters.
    private async Task<List<NewCrmTimeLogItem>>
        LoadEntriesAsync(
            SqlConnection connection,
            NewCrmTimeLogIndexModel model)
    {
        var conditions = new List<string>
        {
            "1=1"
        };

        var command = new SqlCommand
        {
            Connection = connection,
            CommandTimeout = 0
        };

        if (model.FromDate.HasValue)
        {
            conditions.Add("t.[meeting_in] >= @From");
            command.Parameters.AddWithValue(
                "@From", model.FromDate.Value.Date);
        }

        if (model.ToDate.HasValue)
        {
            conditions.Add("t.[meeting_in] < @To");
            command.Parameters.AddWithValue(
                "@To", model.ToDate.Value.Date.AddDays(1));
        }

        if (!string.IsNullOrWhiteSpace(model.UserFilter)
            && int.TryParse(model.UserFilter, out int uid)
            && uid > 0)
        {
            conditions.Add("t.[user_id] = @UserId");
            command.Parameters.AddWithValue("@UserId", uid);
        }

        if (model.LeadId.HasValue && model.LeadId.Value > 0)
        {
            conditions.Add("t.[lead_id] = @LeadId");
            command.Parameters.AddWithValue(
                "@LeadId", model.LeadId.Value);
        }

        if (!string.IsNullOrWhiteSpace(model.Search))
        {
            conditions.Add(@"(
                l.[lead_no] LIKE @Search
                OR c.[ClientName] LIKE @Search
                OR t.[notes] LIKE @Search)");

            command.Parameters.AddWithValue(
                "@Search", "%" + model.Search.Trim() + "%");
        }

        string query = @"
            SELECT TOP 500
                t.[id], t.[lead_id], t.[user_id],
                t.[meeting_type], t.[meeting_in],
                t.[meeting_out], t.[duration_min],
                t.[notes], t.[created_at],
                l.[lead_no], c.[ClientName],
                u.[UserName]
            FROM [lead_time_log] t
            LEFT JOIN [lead_master] l
                ON l.[id] = t.[lead_id]
            LEFT JOIN [ClientMaster] c
                ON c.[Id] = l.[client_id]
            LEFT JOIN [LoginUsers] u
                ON u.[Id] = t.[user_id]
            WHERE " + string.Join(" AND ", conditions) + @"
            ORDER BY t.[meeting_in] DESC";

        command.CommandText = query;

        var entries = new List<NewCrmTimeLogItem>();

        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            entries.Add(ReadEntry(reader));
        }

        return entries;
    }

    // KPIs: total, aaj, hafte, average.
    private async Task LoadKpisAsync(
        SqlConnection connection,
        NewCrmTimeLogIndexModel model)
    {
        // Server runs UTC — we compare against IST (UTC+5:30).
        const string sql = @"
            DECLARE @Now datetime =
                DATEADD(MINUTE, 330, SYSUTCDATETIME());

            SELECT
                COUNT(*) AS TotalEntries,
                SUM(CASE
                    WHEN [meeting_in] >= CAST(@Now AS DATE)
                    THEN ISNULL([duration_min],
                        DATEDIFF(MINUTE, [meeting_in], @Now))
                    ELSE 0 END) AS TodayMinutes,
                SUM(CASE
                    WHEN [meeting_in] >=
                         DATEADD(DAY,
                             -( (DATEPART(WEEKDAY, @Now) + 5) % 7 ),
                             CAST(@Now AS DATE))
                    THEN ISNULL([duration_min],
                        DATEDIFF(MINUTE, [meeting_in], @Now))
                    ELSE 0 END) AS WeekMinutes,
                AVG(CASE
                    WHEN [meeting_out] IS NOT NULL
                    THEN CAST([duration_min] AS FLOAT)
                    ELSE NULL END) AS AvgMinutes
            FROM [lead_time_log]
            WHERE [meeting_in] >= DATEADD(DAY, -90, @Now)";

        using SqlCommand command =
            new SqlCommand(sql, connection);

        command.CommandTimeout = 0;

        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            model.TotalEntries =
                reader["TotalEntries"] == DBNull.Value
                    ? 0
                    : Convert.ToInt32(reader["TotalEntries"]);

            model.TodayMinutes =
                reader["TodayMinutes"] == DBNull.Value
                    ? 0
                    : Convert.ToInt32(reader["TodayMinutes"]);

            model.WeekMinutes =
                reader["WeekMinutes"] == DBNull.Value
                    ? 0
                    : Convert.ToInt32(reader["WeekMinutes"]);

            model.AvgMinutes =
                reader["AvgMinutes"] == DBNull.Value
                    ? 0
                    : Convert.ToDouble(reader["AvgMinutes"]);
        }
    }

    // Auto-stop: stop the current user's LIVE timer.
    // If entryId is provided, stop only that specific entry.
    // Returns true if any timer was stopped.
    private static async Task<bool> AutoStopTimerAsync(
        SqlConnection connection,
        int userId,
        string? remarks,
        int? entryId = null)
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
              AND [meeting_out] IS NULL
              AND (@EntryId IS NULL OR [id] = @EntryId)";

        using SqlCommand command =
            new SqlCommand(sql, connection);

        command.CommandTimeout = 0;
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue(
            "@EntryId",
            (object?)entryId ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@Remarks",
            string.IsNullOrWhiteSpace(remarks)
                ? DBNull.Value
                : remarks);

        int rows = await command.ExecuteNonQueryAsync();

        return rows > 0;
    }

    // Daily report: date -> user -> entries group.
    private static void BuildDailyReport(
        NewCrmTimeLogIndexModel model)
    {
        var byDate = model.Entries
            .GroupBy(e => e.MeetingIn.Date)
            .OrderByDescending(g => g.Key);

        foreach (var dayGroup in byDate)
        {
            var day = new NewCrmTimeLogDayGroup
            {
                Date = dayGroup.Key
            };

            var byUser = dayGroup
                .GroupBy(e => e.UserName)
                .OrderBy(g => g.Key);

            foreach (var userGroup in byUser)
            {
                var user = new NewCrmTimeLogUserGroup
                {
                    UserName = userGroup.Key
                };

                foreach (var entry in userGroup
                             .OrderBy(e => e.MeetingIn))
                {
                    user.Entries.Add(entry);

                    // LIVE entry = time elapsed so far (IST).
                    int mins = entry.DurationMin
                        ?? (int)(NowIst - entry.MeetingIn)
                            .TotalMinutes;

                    if (mins < 0)
                    {
                        mins = 0;
                    }

                    user.TotalMinutes += mins;
                }

                day.ByUser.Add(user);
                day.TotalMinutes += user.TotalMinutes;
            }

            model.DailyReport.Add(day);
        }
    }

    private static NewCrmTimeLogItem ReadEntry(
        SqlDataReader reader) =>
        new()
        {
            Id = Convert.ToInt32(reader["id"]),
            LeadId = Convert.ToInt32(reader["lead_id"]),
            LeadNo = ToStr(reader["lead_no"]),
            ClientName = ToStr(reader["ClientName"]),
            UserId = Convert.ToInt32(reader["user_id"]),
            UserName = ToStr(reader["UserName"]),
            MeetingType = ToStr(reader["meeting_type"]),
            MeetingIn = Convert.ToDateTime(reader["meeting_in"]),
            MeetingOut = reader["meeting_out"] == DBNull.Value
                ? null
                : Convert.ToDateTime(reader["meeting_out"]),
            DurationMin = reader["duration_min"] == DBNull.Value
                ? null
                : Convert.ToInt32(reader["duration_min"]),
            Notes = ToStr(reader["notes"]),
            CreatedAt = Convert.ToDateTime(reader["created_at"])
        };

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

    // Lead dropdown (Log Time modal) - SIRF lead_master se.
    // Previously this was populated from ClientMaster; with Client ID
    // also stored as leadId, this returned "Lead not found" in some cases.)
    private static async Task<List<NewCrmTimeLogLeadItem>>
        LoadLeadsDropdownAsync(SqlConnection connection)
    {
        var leads = new List<NewCrmTimeLogLeadItem>();

        using SqlCommand command = new SqlCommand(@"
            SELECT TOP 500
                l.[id], l.[lead_no], c.[ClientName]
            FROM [lead_master] l
            LEFT JOIN [ClientMaster] c
                ON c.[Id] = l.[client_id]
            WHERE l.[is_active] = 1
            ORDER BY l.[id] DESC",
            connection);

        command.CommandTimeout = 0;

        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            leads.Add(new NewCrmTimeLogLeadItem
            {
                Id = Convert.ToInt32(reader["id"]),
                LeadNo = ToStr(reader["lead_no"]),
                ClientName = ToStr(reader["ClientName"])
            });
        }

        return leads;
    }
}
