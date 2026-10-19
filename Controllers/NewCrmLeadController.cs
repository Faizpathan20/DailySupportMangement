using Master.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Master.Controllers;

[Authorize]
public class NewCrmLeadController : NewCrmControllerBase
{
    public NewCrmLeadController(IConfiguration configuration)
        : base(configuration)
    {
    }

    private static readonly string[] Sources =
    {
        "Reference", "Website", "Walk-in", "Exhibition",
        "Google Ads", "Social Media", "Cold Call", "Other"
    };

    // ============================================
    // LIST
    // ============================================

    [HttpGet]
    public async Task<IActionResult> Index(
        string? search,
        string? status,
        string? step,
        string? priority)
    {
        using SqlConnection connection = await OpenAsync();

        var model = new NewCrmLeadIndexModel
        {
            Search = search,
            StatusFilter = status,
            StepFilter = step,
            PriorityFilter = priority,
            Steps = await LoadStepsAsync(connection)
        };

        // ---- KPI ----
        const string kpiQuery = @"
            SELECT
                COUNT(*) AS Total,
                SUM(CASE WHEN [status] = 'Open'  THEN 1 ELSE 0 END) AS OpenCount,
                SUM(CASE WHEN [status] = 'Won'   THEN 1 ELSE 0 END) AS WonCount,
                SUM(CASE WHEN [status] = 'Lost'  THEN 1 ELSE 0 END) AS LostCount,
                SUM(CASE
                    WHEN [status] = 'Open'
                     AND [next_followup_date] IS NOT NULL
                     AND [next_followup_date] < CAST(GETDATE() AS DATE)
                    THEN 1 ELSE 0 END) AS OverdueCount,
                ISNULL(SUM(CASE WHEN [status] = 'Won'
                    THEN [expected_value] ELSE 0 END), 0) AS WonValue
            FROM [lead_master]
            WHERE [is_active] = 1";

        using (SqlCommand command =
               new SqlCommand(kpiQuery, connection))
        {
            command.CommandTimeout = 0;

            using SqlDataReader reader =
                await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                model.Total = ReadInt(reader, "Total");
                model.Open = ReadInt(reader, "OpenCount");
                model.Won = ReadInt(reader, "WonCount");
                model.Lost = ReadInt(reader, "LostCount");
                model.Overdue = ReadInt(reader, "OverdueCount");
                model.WonValue =
                    reader["WonValue"] == DBNull.Value
                        ? 0
                        : Convert.ToDecimal(reader["WonValue"]);
            }
        }

        // ---- Filters (server-side, parameterized) ----
        var conditions = new List<string>();
        var parameters = new List<(string, object)>();

        if (!string.IsNullOrWhiteSpace(status)
            && new[] { "Open", "Won", "Lost" }.Contains(status))
        {
            conditions.Add("l.[status] = @Status");
            parameters.Add(("@Status", status));
        }

        if (!string.IsNullOrWhiteSpace(priority)
            && new[] { "Hot", "Warm", "Cold" }.Contains(priority))
        {
            conditions.Add("l.[priority] = @Priority");
            parameters.Add(("@Priority", priority));
        }

        if (int.TryParse(step, out int stepId) && stepId > 0)
        {
            conditions.Add("l.[current_step] = @StepId");
            parameters.Add(("@StepId", stepId));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            conditions.Add(@"(
                l.[lead_no] LIKE @Search
                OR l.[requirement] LIKE @Search
                OR l.[contact_no] LIKE @Search
                OR l.[email] LIKE @Search
                OR l.[city] LIKE @Search
                OR c.[ClientName] LIKE @Search
                OR c.[MobileNo] LIKE @Search
                OR l.[source] LIKE @Search)");

            parameters.Add(
                ("@Search", "%" + search.Trim() + "%"));
        }

        model.Leads = conditions.Count > 0
            ? await LoadFilteredAsync(
                connection, conditions, parameters)
            : await LoadLeadsAsync(connection, null);

        // ---- Dropdown data for Add/Edit modal ----
        ViewBag.Clients = await LoadClientsAsync(connection);
        ViewBag.Users = await LoadUsersAsync(connection);
        ViewBag.Sources = Sources;
        ViewBag.Statuses = new[] { "Open", "Won", "Lost" };
        ViewBag.Priorities = new[] { "Low", "Med", "High" };
        ViewBag.States = await LoadStatesAsync(connection);
        ViewBag.LeadBy = CurrentUserId.HasValue
            ? await GetUserNameAsync(connection, CurrentUserId)
            : "";

        return View(model);
    }

    private async Task<List<NewCrmLeadListItem>> LoadFilteredAsync(
        SqlConnection connection,
        List<string> conditions,
        List<(string Name, object Value)> parameters)
    {
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
              AND " + string.Join(" AND ", conditions) + @"
            ORDER BY l.[id] DESC";

        var leads = new List<NewCrmLeadListItem>();

        using SqlCommand command = new SqlCommand(query, connection);

        command.CommandTimeout = 0;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            leads.Add(ReadLead(reader));
        }

        return leads;
    }

    // ReadInt: NewCrmControllerBase se milta hai.

    // ============================================
    // DETAIL + TIMELINE
    // ============================================

    [HttpGet]
    public async Task<IActionResult> Detail(int id)
    {
        using SqlConnection connection = await OpenAsync();

        NewCrmLeadListItem? lead =
            await ReadLeadAsync(connection, id);

        if (lead == null)
        {
            TempData["Error"] = "Lead not found.";
            return RedirectToAction(nameof(Index));
        }

        var model = new NewCrmLeadDetailModel
        {
            Lead = lead,
            Steps = await LoadStepsAsync(connection)
        };

        // ---- Timeline: follow-ups + history, merged ----
        const string timelineQuery = @"
            SELECT * FROM (
                SELECT
                    f.[created_at] AS event_date,
                    'FollowUp' AS kind,
                    'Follow-up' AS title,
                    f.[remarks],
                    u.[UserName] AS changed_by
                FROM [lead_followup] f
                LEFT JOIN [LoginUsers] u
                    ON u.[Id] = f.[created_by]
                WHERE f.[lead_id] = @LeadId

                UNION ALL

                SELECT
                    h.[changed_at] AS event_date,
                    'History' AS kind,
                    CASE
                        WHEN h.[new_step] IS NOT NULL
                            THEN 'Step: ' + h.[old_step]
                                 + ' -> ' + h.[new_step]
                        WHEN h.[new_status] IS NOT NULL
                            THEN 'Status: ' + h.[old_status]
                                 + ' -> ' + h.[new_status]
                        ELSE 'Created'
                    END AS title,
                    h.[remarks],
                    u.[UserName] AS changed_by
                FROM [lead_history] h
                LEFT JOIN [LoginUsers] u
                    ON u.[Id] = h.[changed_by]
                WHERE h.[lead_id] = @LeadId
            ) t
            ORDER BY t.event_date DESC, t.kind DESC";

        using (SqlCommand command =
               new SqlCommand(timelineQuery, connection))
        {
            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@LeadId", id);

            using SqlDataReader reader =
                await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                model.Timeline.Add(new NewCrmTimelineItem
                {
                    EventDate = Convert.ToDateTime(reader["event_date"]),
                    Kind = ToStr(reader["kind"]),
                    Title = ToStr(reader["title"]),
                    Remarks = ToStr(reader["remarks"]),
                    By = ToStr(reader["changed_by"])
                });
            }
        }

        ViewBag.Clients = await LoadClientsAsync(connection);
        ViewBag.Users = await LoadUsersAsync(connection);
        ViewBag.Sources = Sources;
        ViewBag.Statuses = new[] { "Open", "Won", "Lost" };
        ViewBag.Priorities = new[] { "Low", "Med", "High" };

        // Reassign dropdown ke liye (model mein bhi).
        model.Users = await LoadUsersAsync(connection);

        return View(model);
    }

    // ============================================
    // SAVE (insert / update)
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(
        IFormCollection form,
        int id,
        string? nextRemarks = null,
        bool force = false)
    {
        try
        {
            return await SaveCoreAsync(
                form, id, nextRemarks, force);
        }
        catch (Exception ex)
        {
            return Respond(false, "Save failed: " + ex.Message);
        }
    }

    private async Task<IActionResult> SaveCoreAsync(
        IFormCollection form,
        int id,
        string? nextRemarks,
        bool force)
    {
        int clientId = int.TryParse(
            form["ClientId"], out int c) ? c : 0;

        string contactNo = form["ContactNo"].ToString().Trim();
        string email = form["Email"].ToString().Trim();
        string city = form["City"].ToString().Trim();

        int stateId = int.TryParse(
            form["StateId"], out int st) && st > 0 ? st : 0;

        string requirement = form["Requirement"].ToString().Trim();
        string source = form["Source"].ToString().Trim();
        string priority = form["Priority"].ToString().Trim();

        decimal expectedValue = decimal.TryParse(
            form["ExpectedValue"], out decimal v) ? v : 0;

        string nextDateRaw = form["NextFollowupDate"].ToString().Trim();

        // ---- Validation ----
        if (clientId <= 0)
        {
            return Respond(false, "Please select a client.");
        }

        if (string.IsNullOrWhiteSpace(contactNo))
        {
            return Respond(false, "Contact number is required.");
        }

        if (!string.IsNullOrWhiteSpace(email)
            && !email.Contains('@'))
        {
            return Respond(false, "Please enter a valid email.");
        }

        if (string.IsNullOrWhiteSpace(requirement))
        {
            return Respond(false, "Requirement is required.");
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            return Respond(false, "Please select a source.");
        }

        if (!new[] { "Low", "Med", "High" }.Contains(priority))
        {
            priority = "Med";
        }

        // Lead By = jo user login hai (auto-assign).
        int assignedId = CurrentUserId ?? 0;

        if (assignedId <= 0)
        {
            return Respond(
                false,
                "Your login session has expired. Please login again.");
        }

        DateTime? nextDate = DateTime.TryParse(
            nextDateRaw, out DateTime nd)
            ? nd.Date
            : null;

        using SqlConnection connection = await OpenAsync();

        if (id <= 0)
        {
            // ---- Duplicate check (client / mobile / email) ----
            // force = true → user ne "Save Anyway" chuna.
            if (!force)
            {
                string dupSql = @"
                    SELECT TOP 1
                        l.[lead_no], l.[status],
                        c.[ClientName]
                    FROM [lead_master] l
                    JOIN [ClientMaster] c
                        ON c.[Id] = l.[client_id]
                    WHERE l.[is_active] = 1
                      AND (
                            l.[client_id] = @ClientId
                            OR l.[contact_no] = @ContactNo";

                var dupParams = new List<(string, object)>
                {
                    ("@ClientId", clientId),
                    ("@ContactNo", contactNo)
                };

                if (!string.IsNullOrWhiteSpace(email))
                {
                    dupSql += @"
                            OR l.[email] = @Email";
                    dupParams.Add(("@Email", email));
                }

                dupSql += ")";

                string dupLeadNo = "";

                using (SqlCommand command =
                       new SqlCommand(dupSql, connection))
                {
                    command.CommandTimeout = 0;

                    foreach (var (name, value) in dupParams)
                    {
                        command.Parameters.AddWithValue(name, value);
                    }

                    using SqlDataReader reader =
                        await command.ExecuteReaderAsync();

                    if (await reader.ReadAsync())
                    {
                        dupLeadNo =
                            $"{ToStr(reader["lead_no"])} " +
                            $"({ToStr(reader["status"])} - " +
                            $"{ToStr(reader["ClientName"])})";
                    }
                }

                if (dupLeadNo != "")
                {
                    return Respond(
                        false,
                        "DUPLICATE::⚠️ Duplicate lead found: " +
                        dupLeadNo + " — already exists. Click Save Anyway to keep it.");
                }
            }

            // ---- INSERT ----
            const string insertSql = @"
                INSERT INTO [lead_master]
                    ([lead_no], [client_id], [contact_no],
                     [email], [city], [state_id], [requirement],
                     [source], [expected_value], [priority],
                     [assigned_user_id], [current_step], [status],
                     [next_followup_date], [next_remarks],
                     [created_by])
                VALUES
                    (@LeadNo, @ClientId, @ContactNo,
                     @Email, @City, @StateId, @Requirement,
                     @Source, @ExpectedValue, @Priority,
                     @AssignedUserId, 1, 'Open',
                     @NextFollowupDate, @NextRemarks,
                     @CreatedBy);
                SELECT CAST(SCOPE_IDENTITY() AS INT);";

            int newId;

            using (SqlCommand command =
                   new SqlCommand(insertSql, connection))
            {
                command.CommandTimeout = 0;

                command.Parameters.AddWithValue(
                    "@LeadNo", await GenerateLeadNoAsync(connection));
                command.Parameters.AddWithValue("@ClientId", clientId);
                command.Parameters.AddWithValue(
                    "@ContactNo", contactNo);
                command.Parameters.AddWithValue(
                    "@Email", (object?)NullIfEmpty(email) ?? DBNull.Value);
                command.Parameters.AddWithValue(
                    "@City", (object?)NullIfEmpty(city) ?? DBNull.Value);
                command.Parameters.AddWithValue(
                    "@StateId", stateId > 0 ? stateId : DBNull.Value);
                command.Parameters.AddWithValue(
                    "@Requirement", requirement);
                command.Parameters.AddWithValue("@Source", source);
                command.Parameters.AddWithValue(
                    "@ExpectedValue", expectedValue);
                command.Parameters.AddWithValue(
                    "@Priority", priority);
                command.Parameters.AddWithValue(
                    "@AssignedUserId", assignedId);
                command.Parameters.AddWithValue(
                    "@NextFollowupDate",
                    (object?)nextDate ?? DBNull.Value);
                command.Parameters.AddWithValue(
                    "@NextRemarks",
                    (object?)NullIfEmpty(nextRemarks) ?? DBNull.Value);
                command.Parameters.AddWithValue(
                    "@CreatedBy",
                    CurrentUserId ?? (object)DBNull.Value);

                object? result = await command.ExecuteScalarAsync();

                newId = result == null || result == DBNull.Value
                    ? 0
                    : Convert.ToInt32(result);
            }

            if (newId > 0)
            {
                await WriteHistoryAsync(
                    connection, newId,
                    oldStep: null, newStep: "New",
                    oldStatus: null, newStatus: "Open",
                    remarks: "Lead created");
            }

            return Respond(newId > 0, "Lead saved successfully.");
        }

        // ---- UPDATE ----
        (string oldStatus, int oldStep) =
            await ReadSnapshotAsync(connection, id);

        if (oldStatus == "")
        {
            return Respond(false, "Lead not found.");
        }

        const string updateSql = @"
            UPDATE [lead_master] SET
                [client_id] = @ClientId,
                [contact_no] = @ContactNo,
                [email] = @Email,
                [city] = @City,
                [state_id] = @StateId,
                [requirement] = @Requirement,
                [source] = @Source,
                [expected_value] = @ExpectedValue,
                [priority] = @Priority,
                [assigned_user_id] = @AssignedUserId,
                [next_followup_date] = @NextFollowupDate,
                [next_remarks] = @NextRemarks,
                [updated_at] = SYSDATETIME()
            WHERE [id] = @Id";

        using (SqlCommand command =
               new SqlCommand(updateSql, connection))
        {
            command.CommandTimeout = 0;

            command.Parameters.AddWithValue("@Id", id);
            command.Parameters.AddWithValue("@ClientId", clientId);
            command.Parameters.AddWithValue(
                "@ContactNo", contactNo);
            command.Parameters.AddWithValue(
                "@Email", (object?)NullIfEmpty(email) ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "@City", (object?)NullIfEmpty(city) ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "@StateId", stateId > 0 ? stateId : DBNull.Value);
            command.Parameters.AddWithValue(
                "@Requirement", requirement);
            command.Parameters.AddWithValue("@Source", source);
            command.Parameters.AddWithValue(
                "@ExpectedValue", expectedValue);
            command.Parameters.AddWithValue(
                "@Priority", priority);
            command.Parameters.AddWithValue(
                "@AssignedUserId", assignedId);
            command.Parameters.AddWithValue(
                "@NextFollowupDate",
                (object?)nextDate ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "@NextRemarks",
                (object?)NullIfEmpty(nextRemarks) ?? DBNull.Value);

            await command.ExecuteNonQueryAsync();
        }

        await WriteHistoryAsync(
            connection, id,
            oldStep: null, newStep: null,
            oldStatus: oldStatus, newStatus: oldStatus,
            remarks: "Lead details updated");

        return Respond(true, "Lead updated successfully.");
    }

    // ============================================
    // ADVANCE STEP (Demo Done -> Quotation Done -> ...)
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AdvanceStep(
        int id,
        string remarks,
        string nextDate,
        string? nextRemarks = null,
        int minutes = 15)
    {
        remarks = remarks?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(remarks))
        {
            return Respond(
                false,
                "Remarks are required to move a step forward.");
        }

        // Har stage change par nayi follow-up date compulsory —
        // purani date kabhi nahi bachegi.
        if (!DateTime.TryParse(nextDate, out DateTime nd))
        {
            return Respond(
                false,
                "Next follow-up date is required for every stage change.");
        }

        using SqlConnection connection = await OpenAsync();

        NewCrmLeadListItem? lead =
            await ReadLeadAsync(connection, id);

        if (lead == null)
        {
            return Respond(false, "Lead not found.");
        }

        if (lead.Status != "Open")
        {
            return Respond(
                false,
                "This lead is already " + lead.Status +
                " - steps cannot be changed.");
        }

        // Next active step (order ke hisaab se).
        List<NewCrmStepItem> steps =
            await LoadStepsAsync(connection);

        NewCrmStepItem? next = steps
            .FirstOrDefault(s => s.StepOrder > lead.StepOrder);

        if (next == null)
        {
            return Respond(
                false,
                "This lead is already at the last step. " +
                "Mark it as Won.");
        }

        await MoveStepAsync(
            connection, id, lead, next, remarks,
            nextDate: nd.Date,
            nextRemarks: nextRemarks);

        // AUTO TIME LOG (LIVE timer hoga toh actual time,
        // warna user ke entered minutes).
        await LogTimeAsync(
            connection, id, "Follow-up",
            $"Step advance: {lead.StepName} -> {next.StepName}",
            minutes);

        return Respond(
            true,
            $"Step updated: {lead.StepName} -> {next.StepName}.");
    }

    private async Task MoveStepAsync(
        SqlConnection connection,
        int id,
        NewCrmLeadListItem lead,
        NewCrmStepItem next,
        string remarks,
        string? newStatus = null,
        DateTime? nextDate = null,
        string? nextRemarks = null)
    {
        const string updateSql = @"
            UPDATE [lead_master] SET
                [current_step] = @StepId,
                [status] = ISNULL(@Status, [status]),
                [next_followup_date] =
                    ISNULL(@NextDate, [next_followup_date]),
                [next_remarks] =
                    ISNULL(@NextRemarks, [next_remarks]),
                [updated_at] = SYSDATETIME()
            WHERE [id] = @Id";

        using (SqlCommand command =
               new SqlCommand(updateSql, connection))
        {
            command.CommandTimeout = 0;

            command.Parameters.AddWithValue("@Id", id);
            command.Parameters.AddWithValue("@StepId", next.Id);
            command.Parameters.AddWithValue(
                "@Status",
                (object?)NullIfEmpty(newStatus) ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "@NextDate",
                nextDate.HasValue
                    ? (object)nextDate.Value.Date
                    : DBNull.Value);
            command.Parameters.AddWithValue(
                "@NextRemarks",
                (object?)NullIfEmpty(nextRemarks) ?? DBNull.Value);

            await command.ExecuteNonQueryAsync();
        }

        await WriteHistoryAsync(
            connection, id,
            oldStep: lead.StepName, newStep: next.StepName,
            oldStatus: null, newStatus: null,
            remarks: remarks);

        await WriteFollowupAsync(
            connection, id, remarks);
    }

    // ============================================
    // MARK WON (PO Received)
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkWon(
        int id,
        string remarks,
        int minutes = 15)
    {
        remarks = remarks?.Trim() ?? "";

        using SqlConnection connection = await OpenAsync();

        NewCrmLeadListItem? lead =
            await ReadLeadAsync(connection, id);

        if (lead == null)
        {
            return Respond(false, "Lead not found.");
        }

        if (lead.Status != "Open")
        {
            return Respond(
                false,
                "This lead is already " + lead.Status + ".");
        }

        // Last step (PO Received) par le jao.
        List<NewCrmStepItem> steps =
            await LoadStepsAsync(connection);

        NewCrmStepItem? last = steps
            .OrderByDescending(s => s.StepOrder)
            .FirstOrDefault();

        if (last != null && last.Id != lead.CurrentStep)
        {
            await MoveStepAsync(
                connection, id, lead, last,
                string.IsNullOrWhiteSpace(remarks)
                    ? "PO Received"
                    : remarks,
                newStatus: "Won");
        }
        else
        {
            const string sql = @"
                UPDATE [lead_master] SET
                    [status] = 'Won',
                    [updated_at] = SYSDATETIME()
                WHERE [id] = @Id";

            using SqlCommand command =
                new SqlCommand(sql, connection);

            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@Id", id);

            await command.ExecuteNonQueryAsync();

            await WriteHistoryAsync(
                connection, id,
                oldStep: null, newStep: null,
                oldStatus: lead.Status, newStatus: "Won",
                remarks: string.IsNullOrWhiteSpace(remarks)
                    ? "PO Received"
                    : remarks);
        }

        // AUTO TIME LOG (LIVE timer hoga toh actual time,
        // warna user ke entered minutes).
        await LogTimeAsync(
            connection, id, "Follow-up",
            "Lead marked as WON", minutes);

        return Respond(true, "Lead marked as WON. 🎉");
    }

    // ============================================
    // MARK LOST (reason mandatory)
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkLost(
        int id,
        string lostReason,
        int minutes = 15)
    {
        lostReason = lostReason?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(lostReason))
        {
            return Respond(
                false,
                "Lost reason is required.");
        }

        using SqlConnection connection = await OpenAsync();

        NewCrmLeadListItem? lead =
            await ReadLeadAsync(connection, id);

        if (lead == null)
        {
            return Respond(false, "Lead not found.");
        }

        if (lead.Status != "Open")
        {
            return Respond(
                false,
                "This lead is already " + lead.Status + ".");
        }

        const string sql = @"
            UPDATE [lead_master] SET
                [status] = 'Lost',
                [lost_reason] = @LostReason,
                [next_followup_date] = NULL,
                [updated_at] = SYSDATETIME()
            WHERE [id] = @Id";

        using (SqlCommand command =
               new SqlCommand(sql, connection))
        {
            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@Id", id);
            command.Parameters.AddWithValue(
                "@LostReason", lostReason);

            await command.ExecuteNonQueryAsync();
        }

        await WriteHistoryAsync(
            connection, id,
            oldStep: null, newStep: null,
            oldStatus: lead.Status, newStatus: "Lost",
            remarks: lostReason);

        // AUTO TIME LOG (LIVE timer hoga toh actual time,
        // warna user ke entered minutes).
        await LogTimeAsync(
            connection, id, "Follow-up",
            "Lead marked as Lost: " + lostReason, minutes);

        return Respond(true, "Lead marked as Lost.");
    }

    // ============================================
    // ADD FOLLOW-UP (next date + remarks)
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddFollowUp(
        int id,
        string nextDate,
        string remarks,
        int minutes = 15)
    {
        remarks = remarks?.Trim() ?? "";

        if (!DateTime.TryParse(nextDate, out DateTime date))
        {
            return Respond(
                false,
                "Next follow-up date is required.");
        }

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

        const string sql = @"
            UPDATE [lead_master] SET
                [next_followup_date] = @Date,
                [next_remarks] = @Remarks,
                [updated_at] = SYSDATETIME()
            WHERE [id] = @Id";

        using (SqlCommand command =
               new SqlCommand(sql, connection))
        {
            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@Id", id);
            command.Parameters.AddWithValue("@Date", date.Date);
            command.Parameters.AddWithValue("@Remarks", remarks);

            await command.ExecuteNonQueryAsync();
        }

        await WriteFollowupAsync(connection, id, remarks);

        // AUTO TIME LOG (LIVE timer hoga toh actual time).
        await LogTimeAsync(
            connection, id, "Follow-up",
            "Follow-up: " + remarks, minutes);

        return Respond(
            true,
            $"Follow-up scheduled for {date:dd-MM-yyyy}.");
    }

    // ============================================
    // DEACTIVATE (soft delete)
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(int id)
    {
        using SqlConnection connection = await OpenAsync();

        const string sql = @"
            UPDATE [lead_master] SET
                [is_active] = 0,
                [updated_at] = SYSDATETIME()
            WHERE [id] = @Id";

        using SqlCommand command = new SqlCommand(sql, connection);

        command.CommandTimeout = 0;
        command.Parameters.AddWithValue("@Id", id);

        int rows = await command.ExecuteNonQueryAsync();

        return Respond(
            rows > 0,
            rows > 0
                ? "Lead deactivated."
                : "Lead not found.");
    }

    // ============================================
    // REASSIGN (kisi aur user ko lead do)
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reassign(
        int id,
        int assignedUserId)
    {
        if (assignedUserId <= 0)
        {
            return Respond(false, "Please select a user.");
        }

        using SqlConnection connection = await OpenAsync();

        NewCrmLeadListItem? lead =
            await ReadLeadAsync(connection, id);

        if (lead == null)
        {
            return Respond(false, "Lead not found.");
        }

        // Naya user valid hai?
        string newName =
            await GetUserNameAsync(connection, assignedUserId);

        if (newName == "")
        {
            return Respond(false, "User not found.");
        }

        const string sql = @"
            UPDATE [lead_master] SET
                [assigned_user_id] = @UserId,
                [updated_at] = SYSDATETIME()
            WHERE [id] = @Id";

        using (SqlCommand command =
               new SqlCommand(sql, connection))
        {
            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@Id", id);
            command.Parameters.AddWithValue(
                "@UserId", assignedUserId);

            await command.ExecuteNonQueryAsync();
        }

        await WriteHistoryAsync(
            connection, id,
            oldStep: null, newStep: null,
            oldStatus: null, newStatus: null,
            remarks:
                $"Reassigned: {lead.AssignedTo} -> {newName}");

        return Respond(
            true,
            $"Lead assigned to {newName}.");
    }

    // ============================================
    // HELPERS
    // ============================================

    private async Task<(string Status, int StepId)>
        ReadSnapshotAsync(SqlConnection connection, int id)
    {
        using SqlCommand command = new SqlCommand(
            "SELECT [status], [current_step] " +
            "FROM [lead_master] WHERE [id] = @Id",
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
            ToStr(reader["status"]),
            Convert.ToInt32(reader["current_step"]));
    }

    private async Task WriteHistoryAsync(
        SqlConnection connection,
        int leadId,
        string? oldStep,
        string? newStep,
        string? oldStatus,
        string? newStatus,
        string? remarks)
    {
        const string query = @"
            INSERT INTO [lead_history]
                ([lead_id], [old_step], [new_step],
                 [old_status], [new_status], [remarks],
                 [changed_by])
            VALUES
                (@LeadId, @OldStep, @NewStep,
                 @OldStatus, @NewStatus, @Remarks,
                 @ChangedBy)";

        using SqlCommand command =
            new SqlCommand(query, connection);

        command.CommandTimeout = 0;

        command.Parameters.AddWithValue("@LeadId", leadId);
        command.Parameters.AddWithValue(
            "@OldStep", (object?)NullIfEmpty(oldStep) ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@NewStep", (object?)NullIfEmpty(newStep) ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@OldStatus",
            (object?)NullIfEmpty(oldStatus) ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@NewStatus",
            (object?)NullIfEmpty(newStatus) ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@Remarks", (object?)NullIfEmpty(remarks) ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@ChangedBy",
            CurrentUserId ?? (object)DBNull.Value);

        await command.ExecuteNonQueryAsync();
    }

    private async Task WriteFollowupAsync(
        SqlConnection connection,
        int leadId,
        string remarks)
    {
        const string query = @"
            INSERT INTO [lead_followup]
                ([lead_id], [followup_date], [remarks], [created_by])
            VALUES
                (@LeadId, CAST(GETDATE() AS DATE), @Remarks, @CreatedBy)";

        using SqlCommand command =
            new SqlCommand(query, connection);

        command.CommandTimeout = 0;

        command.Parameters.AddWithValue("@LeadId", leadId);
        command.Parameters.AddWithValue(
            "@Remarks", (object?)NullIfEmpty(remarks) ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@CreatedBy",
            CurrentUserId ?? (object)DBNull.Value);

        await command.ExecuteNonQueryAsync();
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

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task<
        List<(int Id, string Name, string Mobile)>>
        LoadClientsAsync(SqlConnection connection)
    {
        var clients = new List<(int, string, string)>();

        using SqlCommand command = new SqlCommand(
            "SELECT [Id], [ClientName], [MobileNo] " +
            "FROM [ClientMaster] " +
            "WHERE [IsActive] = 1 " +
            "ORDER BY [ClientName]",
            connection);

        command.CommandTimeout = 0;

        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            clients.Add((
                Convert.ToInt32(reader["Id"]),
                ToStr(reader["ClientName"]),
                ToStr(reader["MobileNo"])));
        }

        return clients;
    }

    // StatesMaster dropdown (State -> State).
    private static async Task<List<(int Id, string Name)>>
        LoadStatesAsync(SqlConnection connection)
    {
        var states = new List<(int, string)>();

        using SqlCommand command = new SqlCommand(
            "SELECT [Id], [StateName] " +
            "FROM [States] " +
            "ORDER BY [StateName]",
            connection);

        command.CommandTimeout = 0;

        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            states.Add((
                Convert.ToInt32(reader["Id"]),
                ToStr(reader["StateName"])));
        }

        return states;
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
