using Master.Configuration;
using Master.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Master.Controllers;

[Authorize]
public class CrmFollowUpController : CrmControllerBase
{
    public CrmFollowUpController(
        IConfiguration configuration,
        DynamicTableService tableService)
        : base(configuration, tableService)
    {
    }

    protected override string TableName => "CrmFollowUp";

    private static readonly string[] Ranges =
    {
        "all", "due", "today", "pending", "overdue", "done"
    };

    private static readonly string[] Scopes =
    {
        "mine", "team"
    };

    // ============================================
    // LIST
    // ============================================

    [HttpGet]
    public async Task<IActionResult> Index(
        string? search,
        string? range,
        string? scope)
    {
        if (string.IsNullOrWhiteSpace(range)
            || !Ranges.Contains(range))
        {
            range = "due";
        }

        if (string.IsNullOrWhiteSpace(scope)
            || !Scopes.Contains(scope))
        {
            scope = "mine";
        }

        using SqlConnection connection = await OpenAsync();

        List<MasterColumnViewModel> fields =
            await LoadFieldsAsync(connection);

        var model = new CrmFollowUpViewModel
        {
            Fields = fields,
            FormFields = fields.Where(f => f.Editable).ToList(),
            Search = search,
            Range = range,
            Scope = scope
        };

        string kpiWhere = BuildScopeCondition(scope, "");
        kpiWhere = string.IsNullOrWhiteSpace(kpiWhere)
            ? ""
            : " WHERE " + kpiWhere;

        string kpiQuery = @"
            SELECT
                COUNT(*) AS TotalRecords,
                SUM(CASE WHEN CAST([FollowUpDate] AS DATE)
                    = CAST(GETDATE() AS DATE)
                    THEN 1 ELSE 0 END) AS TodayCount,
                SUM(CASE WHEN [Status] = 'Pending'
                    THEN 1 ELSE 0 END) AS PendingCount,
                SUM(CASE WHEN [Status] = 'Pending'
                    AND CAST([FollowUpDate] AS DATE)
                        < CAST(GETDATE() AS DATE)
                    THEN 1 ELSE 0 END) AS OverdueCount,
                SUM(CASE WHEN [Status] = 'Pending'
                    AND CAST([FollowUpDate] AS DATE)
                        <= CAST(GETDATE() AS DATE)
                    THEN 1 ELSE 0 END) AS DueCount,
                SUM(CASE WHEN [Status] = 'Done'
                    THEN 1 ELSE 0 END) AS DoneCount
            FROM [CrmFollowUp]" + kpiWhere;

        using (SqlCommand kpiCommand =
            new SqlCommand(kpiQuery, connection))
        {
            kpiCommand.CommandTimeout = 0;

            using SqlDataReader kpiReader =
                await kpiCommand.ExecuteReaderAsync();

            if (await kpiReader.ReadAsync())
            {
                model.TotalRecords = ReadInt(kpiReader, "TotalRecords");
                model.TodayCount = ReadInt(kpiReader, "TodayCount");
                model.PendingCount = ReadInt(kpiReader, "PendingCount");
                model.OverdueCount = ReadInt(kpiReader, "OverdueCount");
                model.DueCount = ReadInt(kpiReader, "DueCount");
                model.DoneCount = ReadInt(kpiReader, "DoneCount");
            }
        }

        string? rangeWhere = range switch
        {
            "today" =>
                "CAST(s.[FollowUpDate] AS DATE) = CAST(GETDATE() AS DATE)",
            "due" =>
                "s.[Status] = 'Pending' AND CAST(s.[FollowUpDate] AS DATE) "
                + "<= CAST(GETDATE() AS DATE)",
            "pending" => "s.[Status] = 'Pending'",
            "overdue" =>
                "s.[Status] = 'Pending' AND CAST(s.[FollowUpDate] AS DATE) "
                + "< CAST(GETDATE() AS DATE)",
            "done" => "s.[Status] = 'Done'",
            _ => null
        };

        var whereParts = new List<string>();

        if (!string.IsNullOrWhiteSpace(rangeWhere))
        {
            whereParts.Add(rangeWhere);
        }

        string scopeWhere = BuildScopeCondition(scope, "s.");

        if (!string.IsNullOrWhiteSpace(scopeWhere))
        {
            whereParts.Add(scopeWhere);
        }

        string? extraWhere = whereParts.Count == 0
            ? null
            : string.Join(" AND ", whereParts);

        model.FollowUps = await LoadGridAsync(
            connection, fields, search, extraWhere);

        (var dropdowns, var staticOptions) =
            await TableService.LoadFormOptionsAsync(
                connection,
                TableName,
                model.FormFields);

        ViewBag.Dropdowns = dropdowns;
        ViewBag.StaticOptions = staticOptions;
        ViewBag.Search = search;

        return View(model);
    }

    // ============================================
    // DUE COUNT (sidebar badge + My Day header)
    // "due" = still pending and dated today or earlier.
    // Mine is the logged-in user; team is everyone.
    // ============================================

    [HttpGet]
    public async Task<IActionResult> DueCount()
    {
        using SqlConnection connection = await OpenAsync();

        object? me = int.TryParse(LoggedInUserId, out int uid)
            ? uid
            : null;

        async Task<int> CountAsync(bool mineOnly, bool overdueOnly)
        {
            string sql =
                "SELECT COUNT(*) FROM [CrmFollowUp] "
                + "WHERE [Status] = 'Pending' "
                + (overdueOnly
                    ? "AND CAST([FollowUpDate] AS DATE) "
                      + "< CAST(GETDATE() AS DATE) "
                    : "AND CAST([FollowUpDate] AS DATE) "
                      + "= CAST(GETDATE() AS DATE) ");

            if (!mineOnly)
            {
                return await ScalarIntAsync(connection, sql);
            }

            // No parsed user id -> no mine rows (never SQL-injects:
            // the parameter is only added once it is a validated int).
            if (me == null)
            {
                return 0;
            }

            return await ScalarIntAsync(
                connection,
                sql + "AND [UserId] = @UserId",
                ("@UserId", me));
        }

        int mineToday = await CountAsync(mineOnly: true, overdueOnly: false);
        int mineOverdue = await CountAsync(mineOnly: true, overdueOnly: true);
        int teamToday = await CountAsync(mineOnly: false, overdueOnly: false);
        int teamOverdue = await CountAsync(mineOnly: false, overdueOnly: true);

        return Json(new
        {
            mine = new
            {
                today = mineToday,
                overdue = mineOverdue,
                due = mineToday + mineOverdue
            },
            team = new
            {
                today = teamToday,
                overdue = teamOverdue,
                due = teamToday + teamOverdue
            }
        });
    }

    // ============================================
    // SAVE (insert / update)
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(IFormCollection form, int id)
    {
        try
        {
            return await SaveCoreAsync(form, id);
        }
        catch (Exception ex)
        {
            // Surface the real reason instead of a bare HTTP 500
            // so the failing column / constraint is visible.
            return Respond(false, "Save failed: " + ex.Message);
        }
    }

    private async Task<IActionResult> SaveCoreAsync(
        IFormCollection form, int id)
    {
        using SqlConnection connection = await OpenAsync();

        List<MasterColumnViewModel> fields =
            await LoadFieldsAsync(connection);

        var editable = fields.Where(f => f.Editable).ToList();

        string? validationError = ValidateRequired(editable, form);

        if (!string.IsNullOrWhiteSpace(validationError))
        {
            return Respond(false, validationError);
        }

        var overrides =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(form["FollowUpDate"].ToString()))
        {
            overrides["FollowUpDate"] =
                DateTime.Today.ToString("yyyy-MM-dd");
        }

        int leadId = int.TryParse(
            form["LeadId"].ToString(), out int lid) ? lid : 0;

        string remarks = form["Remarks"].ToString();

        if (id <= 0)
        {
            int newId = await InsertAsync(
                connection, fields, form, overrides);

            if (leadId > 0)
            {
                await WriteHistoryAsync(
                    connection, leadId, "Follow-Up",
                    null, null,
                    string.IsNullOrWhiteSpace(remarks)
                        ? "Follow-up scheduled"
                        : remarks);
            }

            return Respond(newId > 0, "Follow-up added successfully.");
        }

        int rows = await UpdateAsync(
            connection, fields, form, id, overrides);

        return Respond(
            rows > 0,
            rows > 0
                ? "Follow-up updated successfully."
                : "Follow-up not found.");
    }

    // ============================================
    // MARK DONE
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkDone(int id)
    {
        using SqlConnection connection = await OpenAsync();

        int leadId;
        string remarks;
        DateTime? nextDate;

        using (SqlCommand read = new SqlCommand(
            "SELECT [LeadId], [Remarks], [NextFollowUpDate] "
            + "FROM [CrmFollowUp] WHERE [Id] = @Id",
            connection))
        {
            read.CommandTimeout = 0;
            read.Parameters.AddWithValue("@Id", id);

            using SqlDataReader reader =
                await read.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return Respond(false, "Follow-up not found.");
            }

            leadId = reader["LeadId"] == DBNull.Value
                ? 0
                : Convert.ToInt32(reader["LeadId"]);

            remarks = reader["Remarks"] == DBNull.Value
                ? ""
                : reader["Remarks"].ToString() ?? "";

            nextDate = reader["NextFollowUpDate"] == DBNull.Value
                ? null
                : Convert.ToDateTime(reader["NextFollowUpDate"]);
        }

        using (SqlCommand done = new SqlCommand(
            "UPDATE [CrmFollowUp] "
            + "SET [Status] = 'Done', [DoneOn] = GETDATE() "
            + "WHERE [Id] = @Id",
            connection))
        {
            done.CommandTimeout = 0;
            done.Parameters.AddWithValue("@Id", id);
            await done.ExecuteNonQueryAsync();
        }

        if (leadId > 0)
        {
            await WriteHistoryAsync(
                connection, leadId, "Call",
                null, null,
                string.IsNullOrWhiteSpace(remarks)
                    ? "Follow-up completed"
                    : remarks);

            if (nextDate.HasValue)
            {
                using SqlCommand update = new SqlCommand(
                    "UPDATE [CrmLead] SET [NextFollowUpDate] = @Date "
                    + "WHERE [Id] = @Id",
                    connection);

                update.CommandTimeout = 0;
                update.Parameters.AddWithValue("@Date", nextDate.Value);
                update.Parameters.AddWithValue("@Id", leadId);

                await update.ExecuteNonQueryAsync();
            }
        }

        return Respond(true, "Follow-up marked as done.");
    }

    // ============================================
    // COMPLETE (log outcome + force the next one)
    // Discipline rule: a follow-up may not simply end.
    // Either schedule the next date, or close the lead.
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(
        int id,
        string? remarks,
        string? nextAction,
        string? nextDate,
        string? nextTime,
        string? contactMode,
        bool closeLead,
        string? closeReason)
    {
        try
        {
            return await CompleteCoreAsync(
                id, remarks, nextAction, nextDate, nextTime,
                contactMode, closeLead, closeReason);
        }
        catch (Exception ex)
        {
            return Respond(false, "Could not save: " + ex.Message);
        }
    }

    private async Task<IActionResult> CompleteCoreAsync(
        int id,
        string? remarks,
        string? nextAction,
        string? nextDate,
        string? nextTime,
        string? contactMode,
        bool closeLead,
        string? closeReason)
    {
        using SqlConnection connection = await OpenAsync();

        int leadId = 0;
        string existingRemarks = "";
        string existingMode = "";
        TimeSpan? existingTime = null;

        using (SqlCommand read = new SqlCommand(
            "SELECT [LeadId], [Remarks], [ContactMode], [FollowUpTime] "
            + "FROM [CrmFollowUp] WHERE [Id] = @Id",
            connection))
        {
            read.CommandTimeout = 0;
            read.Parameters.AddWithValue("@Id", id);

            using SqlDataReader reader =
                await read.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return Respond(false, "Follow-up not found.");
            }

            leadId = reader["LeadId"] == DBNull.Value
                ? 0
                : Convert.ToInt32(reader["LeadId"]);

            existingRemarks = ToDisplay(reader["Remarks"]);
            existingMode = ToDisplay(reader["ContactMode"]);

            existingTime = reader["FollowUpTime"] == DBNull.Value
                ? null
                : (TimeSpan)reader["FollowUpTime"];
        }

        DateTime? parsedNextDate = ParseIsoDate(nextDate);

        if (!closeLead && !parsedNextDate.HasValue)
        {
            return Respond(
                false,
                "Next follow-up date is required - pick a date, "
                + "or close the lead.");
        }

        if (leadId <= 0 && !closeLead)
        {
            return Respond(
                false,
                "This follow-up has no lead attached, so the chain "
                + "cannot continue. Close it instead.");
        }

        string outcome = NullIfEmpty(remarks) ?? existingRemarks;
        string? note = NullIfEmpty(nextAction);

        // 1) close the current follow-up and record what is next.
        using (SqlCommand done = new SqlCommand(@"
            UPDATE [CrmFollowUp]
            SET [Status] = 'Done',
                [DoneOn] = GETDATE(),
                [Remarks] = @Remarks,
                [NextAction] = @NextAction,
                [NextFollowUpDate] = @NextDate
            WHERE [Id] = @Id", connection))
        {
            done.CommandTimeout = 0;
            done.Parameters.AddWithValue("@Id", id);
            done.Parameters.AddWithValue(
                "@Remarks", (object?)outcome ?? DBNull.Value);
            done.Parameters.AddWithValue(
                "@NextAction", (object?)note ?? DBNull.Value);
            done.Parameters.AddWithValue(
                "@NextDate",
                (object?)parsedNextDate?.Date ?? DBNull.Value);

            await done.ExecuteNonQueryAsync();
        }

        if (leadId <= 0)
        {
            return Respond(true, "Follow-up marked as done.");
        }

        if (closeLead)
        {
            string oldStatus = "";

            using (SqlCommand status = new SqlCommand(
                "SELECT [Status] FROM [CrmLead] WHERE [Id] = @Id",
                connection))
            {
                status.Parameters.AddWithValue("@Id", leadId);

                object? current =
                    await status.ExecuteScalarAsync();

                oldStatus = current == null
                    || current == DBNull.Value
                    ? ""
                    : current.ToString() ?? "";
            }

            using (SqlCommand close = new SqlCommand(@"
                UPDATE [CrmLead]
                SET [Status] = 'Lost',
                    [LostReason] = @Reason,
                    [NextFollowUpDate] = NULL
                WHERE [Id] = @Id", connection))
            {
                close.CommandTimeout = 0;
                close.Parameters.AddWithValue("@Id", leadId);
                close.Parameters.AddWithValue(
                    "@Reason",
                    (object?)NullIfEmpty(closeReason) ?? DBNull.Value);

                await close.ExecuteNonQueryAsync();
            }

            await WriteHistoryAsync(
                connection, leadId, "Lost",
                NullIfEmpty(oldStatus), "Lost",
                NullIfEmpty(closeReason)
                    ?? "Lead closed from a follow-up");

            await WriteHistoryAsync(
                connection, leadId, "Call",
                null, null,
                string.IsNullOrWhiteSpace(outcome)
                    ? "Follow-up completed"
                    : outcome);

            return Respond(true, "Follow-up done - lead closed as lost.");
        }

        DateTime next = parsedNextDate!.Value;
        TimeSpan? nextTimeValue = ParseIsoTime(nextTime) ?? existingTime;
        string mode = NullIfEmpty(contactMode)
            ?? (string.IsNullOrWhiteSpace(existingMode)
                ? "Call"
                : existingMode);

        // 2) the chain must continue: create the next follow-up row.
        using (SqlCommand insert = new SqlCommand(@"
            INSERT INTO [CrmFollowUp]
                ([LeadId], [FollowUpDate], [FollowUpTime],
                 [ContactMode], [Remarks], [NextAction], [Status],
                 [UserId], [EntryOn])
            VALUES
                (@LeadId, @FollowUpDate, @FollowUpTime,
                 @ContactMode, NULL, @NextAction, 'Pending',
                 @UserId, GETDATE());", connection))
        {
            insert.CommandTimeout = 0;
            insert.Parameters.AddWithValue("@LeadId", leadId);
            insert.Parameters.AddWithValue(
                "@FollowUpDate", next.Date);
            insert.Parameters.AddWithValue("@ContactMode", mode);
            insert.Parameters.AddWithValue(
                "@NextAction", (object?)note ?? DBNull.Value);
            insert.Parameters.AddWithValue(
                "@UserId",
                int.TryParse(LoggedInUserId, out int nextUser)
                    ? nextUser
                    : (object)DBNull.Value);

            if (nextTimeValue.HasValue)
            {
                insert.Parameters.Add(
                    new SqlParameter(
                        "@FollowUpTime", System.Data.SqlDbType.Time)
                    {
                        Value = nextTimeValue.Value
                    });
            }
            else
            {
                insert.Parameters.AddWithValue(
                    "@FollowUpTime", DBNull.Value);
            }

            await insert.ExecuteNonQueryAsync();
        }

        using (SqlCommand leadNext = new SqlCommand(
            "UPDATE [CrmLead] SET [NextFollowUpDate] = @Date "
            + "WHERE [Id] = @Id",
            connection))
        {
            leadNext.CommandTimeout = 0;
            leadNext.Parameters.AddWithValue("@Date", next.Date);
            leadNext.Parameters.AddWithValue("@Id", leadId);

            await leadNext.ExecuteNonQueryAsync();
        }

        await WriteHistoryAsync(
            connection, leadId, "Call",
            null, null,
            string.IsNullOrWhiteSpace(outcome)
                ? "Follow-up completed"
                : outcome);

        await WriteHistoryAsync(
            connection, leadId, "Follow-Up",
            null, null,
            "Next follow-up scheduled for "
            + next.ToString("dd MMM yyyy"));

        return Respond(
            true,
            "Follow-up done - next one scheduled for "
            + next.ToString("dd MMM yyyy") + ".");
    }

    // ============================================
    // RESCHEDULE (one-click snooze to a new date)
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reschedule(
        int id,
        string? newDate)
    {
        using SqlConnection connection = await OpenAsync();

        DateTime target =
            ParseIsoDate(newDate) ?? DateTime.Today.AddDays(1);

        int leadId = 0;
        DateTime oldDate = target;
        string status = "";

        using (SqlCommand read = new SqlCommand(
            "SELECT [LeadId], [FollowUpDate], [Status] "
            + "FROM [CrmFollowUp] WHERE [Id] = @Id",
            connection))
        {
            read.CommandTimeout = 0;
            read.Parameters.AddWithValue("@Id", id);

            using SqlDataReader reader =
                await read.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                return Respond(false, "Follow-up not found.");
            }

            leadId = reader["LeadId"] == DBNull.Value
                ? 0
                : Convert.ToInt32(reader["LeadId"]);

            oldDate = reader["FollowUpDate"] == DBNull.Value
                ? target
                : Convert.ToDateTime(reader["FollowUpDate"]);

            status = ToDisplay(reader["Status"]);
        }

        if (!status.Equals(
                "Pending", StringComparison.OrdinalIgnoreCase))
        {
            return Respond(
                false,
                "Only pending follow-ups can be moved.");
        }

        using (SqlCommand move = new SqlCommand(
            "UPDATE [CrmFollowUp] SET [FollowUpDate] = @Date "
            + "WHERE [Id] = @Id",
            connection))
        {
            move.CommandTimeout = 0;
            move.Parameters.AddWithValue("@Date", target.Date);
            move.Parameters.AddWithValue("@Id", id);

            await move.ExecuteNonQueryAsync();
        }

        // Keep the lead's "next" pointer on the earliest due date.
        if (leadId > 0)
        {
            using (SqlCommand leadNext = new SqlCommand(@"
                UPDATE [CrmLead]
                SET [NextFollowUpDate] = @Date
                WHERE [Id] = @LeadId
                  AND ([NextFollowUpDate] IS NULL
                       OR CAST([NextFollowUpDate] AS DATE)
                          <= CAST(@OldDate AS DATE))", connection))
            {
                leadNext.CommandTimeout = 0;
                leadNext.Parameters.AddWithValue("@Date", target.Date);
                leadNext.Parameters.AddWithValue("@LeadId", leadId);
                leadNext.Parameters.AddWithValue("@OldDate", oldDate);

                await leadNext.ExecuteNonQueryAsync();
            }

            await WriteHistoryAsync(
                connection, leadId, "Follow-Up",
                oldDate.ToString("yyyy-MM-dd"),
                target.ToString("yyyy-MM-dd"),
                "Follow-up rescheduled");
        }

        return Respond(
            true,
            "Follow-up moved to " + target.ToString("dd MMM yyyy") + ".");
    }

    // ============================================
    // DELETE
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        using SqlConnection connection = await OpenAsync();

        using SqlCommand command = new SqlCommand(
            "DELETE FROM [CrmFollowUp] WHERE [Id] = @Id",
            connection);

        command.CommandTimeout = 0;
        command.Parameters.AddWithValue("@Id", id);

        int rows = await command.ExecuteNonQueryAsync();

        return Respond(
            rows > 0,
            rows > 0
                ? "Follow-up deleted successfully."
                : "Follow-up not found.");
    }

    // ============================================
    // HELPERS
    // ============================================

    // "mine" narrows to the rows assigned to the signed-in user.
    // The grid aliases its table as "s." and the KPI query does not,
    // so the alias is passed in. LoggedInUserId only reaches SQL once
    // it has been validated as an int, so the value is interpolated
    // rather than concatenated from raw input.
    private string BuildScopeCondition(string scope, string prefix)
    {
        if (!scope.Equals("mine", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        return int.TryParse(LoggedInUserId, out int uid)
            ? $"{prefix}[UserId] = {uid}"
            : "1 = 0";
    }

    private static DateTime? ParseIsoDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTime.TryParse(
                value.Trim(),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out DateTime parsed))
        {
            return parsed.Date;
        }

        if (DateTime.TryParse(value.Trim(), out parsed))
        {
            return parsed.Date;
        }

        return null;
    }

    private static TimeSpan? ParseIsoTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (TimeSpan.TryParse(value.Trim(), out TimeSpan parsed))
        {
            return parsed;
        }

        if (DateTime.TryParse(value.Trim(), out DateTime date))
        {
            return date.TimeOfDay;
        }

        return null;
    }

    private IActionResult Respond(bool success, string message)
    {
        if (IsAjax)
        {
            return Json(new { success, message });
        }

        TempData[success ? "Success" : "Error"] = message;

        return RedirectToAction(nameof(Index), new { range = "due" });
    }

    private static int ReadInt(SqlDataReader reader, string column) =>
        reader[column] == DBNull.Value
            ? 0
            : Convert.ToInt32(reader[column]);
}
