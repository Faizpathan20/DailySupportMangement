using Master.Models;
using Microsoft.Data.SqlClient;

namespace Master.Configurations;

// ============================================
// TICKET SERVICE
// Ticketing system ke saare DB operations ek
// jagah — masters load, ticket create/detail,
// work updates, status workflow, review.
//
// Ye service raw ADO.NET use karti hai (project
// ka existing pattern — EF nahi).
// ============================================

public class TicketService
{
    private readonly IConfiguration _configuration;

    public TicketService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    // IST (UTC+5:30) — SQL server apne timezone me hai,
    // isliye hamesha IST use karo.
    public static DateTime NowIst =>
        DateTime.UtcNow.AddMinutes(330);

    // SQL-side IST now (SQL text me inline use ke liye).
    public const string SqlNowIstText =
        "DATEADD(MINUTE, 330, SYSUTCDATETIME())";

    public async Task<SqlConnection> OpenAsync()
    {
        string? cs =
            _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(cs))
        {
            throw new InvalidOperationException(
                "Database connection string is missing.");
        }

        var connection = new SqlConnection(cs);

        await connection.OpenAsync();

        return connection;
    }

    // ============================================
    // MASTERS LOAD
    // ============================================

    public async Task<List<TicketOption>> LoadLookupAsync(
        SqlConnection connection,
        string table,
        string idColumn,
        string nameColumn,
        bool includeParent = false,
        string? slaColumn = null)
    {
        var list = new List<TicketOption>();

        string parentSelect = includeParent
            ? ", [CategoryId]"
            : "";

        string slaSelect = slaColumn != null
            ? $", [{slaColumn}]"
            : "";

        string sql = $@"
            SELECT [{idColumn}] AS Id, [{nameColumn}] AS Name
                   {parentSelect}
                   {slaSelect}
            FROM [{table}]
            WHERE [IsActive] = 1
            ORDER BY [SortOrder], [{nameColumn}]";

        using SqlCommand command =
            new SqlCommand(sql, connection);

        command.CommandTimeout = 0;

        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var option = new TicketOption
            {
                Id = Convert.ToInt32(reader["Id"]),
                Name = ToStr(reader["Name"])
            };

            if (includeParent
                && reader["CategoryId"] != DBNull.Value)
            {
                option.ParentId =
                    Convert.ToInt32(reader["CategoryId"]);
            }

            if (slaColumn != null
                && reader[slaColumn] != DBNull.Value)
            {
                option.SlaHours =
                    Convert.ToInt32(reader[slaColumn]);
            }

            list.Add(option);
        }

        return list;
    }

    // SubCategory ko Category se cascade karne ke liye static list.
    public async Task<List<TicketOption>> LoadSubCategoriesAsync(
        SqlConnection connection)
    {
        return await LoadLookupAsync(
            connection,
            "Mst_SubCategory",
            "SubCategoryId",
            "SubCategoryName",
            includeParent: true);
    }

    // LoginUsers (users) — IsActive column optional.
    public async Task<List<TicketOption>> LoadUsersAsync(
        SqlConnection connection)
    {
        var list = new List<TicketOption>();

        // LoginUsers me IsActive/SortOrder ho ya na ho — safe query.
        const string sql = @"
            SELECT [Id], [UserName]
            FROM [LoginUsers]
            ORDER BY [UserName]";

        using SqlCommand command =
            new SqlCommand(sql, connection);

        command.CommandTimeout = 0;

        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            list.Add(new TicketOption
            {
                Id = Convert.ToInt32(reader["Id"]),
                Name = ToStr(reader["UserName"])
            });
        }

        return list;
    }

    // ============================================
    // TICKET NO GENERATE  (TKT-YYMMDD-NNN)
    // ============================================

    public async Task<string> GenerateTicketNoAsync(
        SqlConnection connection)
    {
        string prefix = "TKT-" + NowIst.ToString("yyMMdd");

        string sql = @"
            SELECT COUNT(*)
            FROM [DailySupport]
            WHERE [TicketNo] LIKE @Prefix + '-%'";

        using SqlCommand command =
            new SqlCommand(sql, connection);

        command.CommandTimeout = 0;
        command.Parameters.AddWithValue("@Prefix", prefix);

        int count = 0;

        try
        {
            object? result = await command.ExecuteScalarAsync();

            count = result == null || result == DBNull.Value
                ? 0
                : Convert.ToInt32(result);
        }
        catch
        {
            count = 0;
        }

        return $"{prefix}-{(count + 1):D3}";
    }

    // ============================================
    // SLA CALCULATION
    // SupportDate + Priority.SlaHours
    // ============================================

    public async Task<DateTime?> CalculateSlaDueAsync(
        SqlConnection connection,
        DateTime supportDate,
        int? priorityId)
    {
        if (priorityId == null)
        {
            return null;
        }

        string sql =
            "SELECT [SlaHours] FROM [Mst_Priority] " +
            "WHERE [PriorityId] = @Id";

        using SqlCommand command =
            new SqlCommand(sql, connection);

        command.CommandTimeout = 0;
        command.Parameters.AddWithValue("@Id", priorityId.Value);

        object? result = await command.ExecuteScalarAsync();

        if (result == null || result == DBNull.Value)
        {
            return null;
        }

        int hours = Convert.ToInt32(result);

        return supportDate.AddHours(hours);
    }

    // ============================================
    // DUPLICATE CHECK  (same Mobile ka open ticket)
    // ============================================

    public async Task<List<TicketOption>> FindOpenTicketsByMobileAsync(
        SqlConnection connection,
        string mobile)
    {
        var list = new List<TicketOption>();

        if (string.IsNullOrWhiteSpace(mobile))
        {
            return list;
        }

        // Open == IsClosed = 0
        const string sql = @"
            SELECT TOP 5
                d.[Id],
                d.[TicketNo],
                s.[StatusName]
            FROM [DailySupport] d
            LEFT JOIN [Mst_Status] s ON s.[StatusId] = d.[StatusId]
            WHERE d.[Mobile] = @Mobile
              AND ISNULL(s.[IsClosed], 0) = 0
            ORDER BY d.[Id] DESC";

        using SqlCommand command =
            new SqlCommand(sql, connection);

        command.CommandTimeout = 0;
        command.Parameters.AddWithValue("@Mobile", mobile.Trim());

        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            list.Add(new TicketOption
            {
                Id = Convert.ToInt32(reader["Id"]),
                Name = ToStr(reader["TicketNo"]),
                SlaHours = 0
            });
        }

        return list;
    }

    // ============================================
    // TICKET CREATE
    // ============================================

    public async Task<int> CreateTicketAsync(
        SqlConnection connection,
        TicketCreateViewModel model,
        int? createdBy)
    {
        string ticketNo =
            await GenerateTicketNoAsync(connection);

        DateTime slaDue =
            await CalculateSlaDueAsync(
                connection,
                model.SupportDate,
                model.PriorityId) ?? model.SupportDate;

        const string sql = @"
            INSERT INTO [DailySupport]
            (
                [TicketNo], [SupportDate], [ClientId], [UserId],
                [SupportType], [Subject], [Description],
                [Status], [Priority], [EntryOn],
                [Mobile], [CallerName],
                [ChannelId], [CallTypeId], [CategoryId], [SubCategoryId],
                [PriorityId], [StatusId], [AssignedTo],
                [SlaDue], [CreatedBy], [CreatedAt], [UpdatedAt]
            )
            VALUES
            (
                @TicketNo, @SupportDate, @ClientId, @UserId,
                @SupportType, @Subject, @Description,
                @StatusText, @PriorityText, @EntryOn,
                @Mobile, @CallerName,
                @ChannelId, @CallTypeId, @CategoryId, @SubCategoryId,
                @PriorityId, @StatusId, @AssignedTo,
                @SlaDue, @CreatedBy, @Now, @Now
            );
            SELECT CAST(SCOPE_IDENTITY() AS INT);";

        int ticketId;

        using (SqlCommand command =
               new SqlCommand(sql, connection))
        {
            command.CommandTimeout = 0;

            int userId = model.AssignedTo ?? createdBy ?? 0;

            command.Parameters.AddWithValue("@TicketNo", ticketNo);
            command.Parameters.AddWithValue(
                "@SupportDate", model.SupportDate);
            command.Parameters.AddWithValue("@ClientId", model.ClientId ?? 0);
            command.Parameters.AddWithValue("@UserId", userId);
            command.Parameters.AddWithValue("@SupportType", "Client Query");
            command.Parameters.AddWithValue("@Subject", model.Subject ?? "");
            command.Parameters.AddWithValue(
                "@Description", model.Description ?? "");
            command.Parameters.AddWithValue("@StatusText", "Open");
            command.Parameters.AddWithValue("@PriorityText", "Medium");
            command.Parameters.AddWithValue("@EntryOn", NowIst);
            command.Parameters.AddWithValue("@Mobile", model.Mobile ?? "");
            command.Parameters.AddWithValue(
                "@CallerName", model.CallerName ?? "");
            command.Parameters.AddWithValue(
                "@ChannelId", (object?)model.ChannelId ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "@CallTypeId", (object?)model.CallTypeId ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "@CategoryId", (object?)model.CategoryId ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "@SubCategoryId", (object?)model.SubCategoryId ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "@PriorityId", (object?)model.PriorityId ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "@StatusId", (object?)model.StatusId ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "@AssignedTo", (object?)model.AssignedTo ?? DBNull.Value);
            command.Parameters.AddWithValue("@SlaDue", slaDue);
            command.Parameters.AddWithValue(
                "@CreatedBy", (object?)createdBy ?? DBNull.Value);
            command.Parameters.AddWithValue("@Now", NowIst);

            object? result = await command.ExecuteScalarAsync();

            ticketId = result == null || result == DBNull.Value
                ? 0
                : Convert.ToInt32(result);
        }

        // History: Created
        if (ticketId > 0)
        {
            await AddHistoryAsync(
                connection,
                ticketId,
                "Created",
                null,
                ticketNo,
                "Ticket created",
                createdBy);
        }

        return ticketId;
    }

    // ============================================
    // HISTORY
    // ============================================

    public async Task AddHistoryAsync(
        SqlConnection connection,
        int ticketId,
        string action,
        string? oldValue,
        string? newValue,
        string? remarks,
        int? changedBy)
    {
        const string sql = @"
            INSERT INTO [Trn_DailySupport_History]
            (
                [TicketId], [Action], [OldValue], [NewValue],
                [Remarks], [ChangedBy], [ChangedAt]
            )
            VALUES
            (
                @TicketId, @Action, @OldValue, @NewValue,
                @Remarks, @ChangedBy, @ChangedAt
            )";

        using SqlCommand command =
            new SqlCommand(sql, connection);

        command.CommandTimeout = 0;

        command.Parameters.AddWithValue("@TicketId", ticketId);
        command.Parameters.AddWithValue("@Action", action);
        command.Parameters.AddWithValue(
            "@OldValue", (object?)oldValue ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@NewValue", (object?)newValue ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@Remarks", (object?)remarks ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@ChangedBy", (object?)changedBy ?? DBNull.Value);
        command.Parameters.AddWithValue("@ChangedAt", NowIst);

        await command.ExecuteNonQueryAsync();
    }

    // ============================================
    // WORK UPDATE (roz ka kaam)
    // ============================================

    public async Task<int> AddUpdateAsync(
        SqlConnection connection,
        int ticketId,
        DateTime workDate,
        string workDone,
        int? timeSpent,
        string? nextAction,
        int? oldStatusId,
        int? newStatusId,
        int? updatedBy,
        string? waitingOn = null,
        string? resolutionNote = null,
        DateTime? followUpDate = null)
    {
        const string sql = @"
            INSERT INTO [Trn_DailySupport_Updates]
            (
                [TicketId], [WorkDate], [WorkDone], [TimeSpent],
                [NextAction], [OldStatusId], [NewStatusId],
                [WaitingOn], [ResolutionNote], [FollowUpDate],
                [UpdatedBy], [EntryOn]
            )
            VALUES
            (
                @TicketId, @WorkDate, @WorkDone, @TimeSpent,
                @NextAction, @OldStatusId, @NewStatusId,
                @WaitingOn, @ResolutionNote, @FollowUpDate,
                @UpdatedBy, @EntryOn
            );
            SELECT CAST(SCOPE_IDENTITY() AS INT);";

        int updateId;

        using (SqlCommand command =
               new SqlCommand(sql, connection))
        {
            command.CommandTimeout = 0;

            command.Parameters.AddWithValue("@TicketId", ticketId);
            command.Parameters.AddWithValue("@WorkDate", workDate.Date);
            command.Parameters.AddWithValue("@WorkDone", workDone ?? "");
            command.Parameters.AddWithValue(
                "@TimeSpent", (object?)timeSpent ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "@NextAction", (object?)nextAction ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "@OldStatusId", (object?)oldStatusId ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "@NewStatusId", (object?)newStatusId ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "@WaitingOn", (object?)waitingOn ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "@ResolutionNote", (object?)resolutionNote ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "@FollowUpDate",
                (object?)followUpDate?.Date ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "@UpdatedBy", (object?)updatedBy ?? DBNull.Value);
            command.Parameters.AddWithValue("@EntryOn", NowIst);

            object? result = await command.ExecuteScalarAsync();

            updateId = result == null || result == DBNull.Value
                ? 0
                : Convert.ToInt32(result);
        }

        // Ticket ka total TimeSpent recalculate.
        await RecalculateTimeSpentAsync(connection, ticketId);

        return updateId;
    }

    // Ek existing work update ko edit karo (naya row nahi).
    // Status / WorkDate nahi badalte — sirf work details.
    public async Task UpdateWorkUpdateAsync(
        SqlConnection connection,
        int updateId,
        string workDone,
        int? timeSpent,
        string? nextAction,
        string? waitingOn,
        string? resolutionNote,
        DateTime? followUpDate)
    {
        const string sql = @"
            UPDATE [Trn_DailySupport_Updates] SET
                [WorkDone] = @WorkDone,
                [TimeSpent] = @TimeSpent,
                [NextAction] = @NextAction,
                [WaitingOn] = @WaitingOn,
                [ResolutionNote] = @ResolutionNote,
                [FollowUpDate] = @FollowUpDate
            WHERE [UpdateId] = @UpdateId";

        int ticketId = 0;

        using (SqlCommand command =
               new SqlCommand(sql, connection))
        {
            command.CommandTimeout = 0;

            command.Parameters.AddWithValue("@WorkDone", workDone ?? "");
            command.Parameters.AddWithValue(
                "@TimeSpent", (object?)timeSpent ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "@NextAction", (object?)nextAction ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "@WaitingOn", (object?)waitingOn ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "@ResolutionNote", (object?)resolutionNote ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "@FollowUpDate",
                (object?)followUpDate?.Date ?? DBNull.Value);
            command.Parameters.AddWithValue("@UpdateId", updateId);

            await command.ExecuteNonQueryAsync();
        }

        // Ticket ka total TimeSpent recalculate (jo ticket is update ka hai).
        const string ticketSql =
            "SELECT [TicketId] FROM [Trn_DailySupport_Updates] WHERE [UpdateId] = @UpdateId";

        using (SqlCommand command =
               new SqlCommand(ticketSql, connection))
        {
            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@UpdateId", updateId);

            object? result = await command.ExecuteScalarAsync();

            if (result != null && result != DBNull.Value)
            {
                ticketId = Convert.ToInt32(result);
            }
        }

        if (ticketId > 0)
        {
            await RecalculateTimeSpentAsync(connection, ticketId);
        }
    }

    public async Task RecalculateTimeSpentAsync(
        SqlConnection connection,
        int ticketId)
    {
        const string sql = @"
            UPDATE [DailySupport] SET
                [TimeSpent] = (
                    SELECT ISNULL(SUM([TimeSpent]), 0)
                    FROM [Trn_DailySupport_Updates]
                    WHERE [TicketId] = @Id
                ),
                [UpdatedAt] = @Now
            WHERE [Id] = @Id";

        using SqlCommand command =
            new SqlCommand(sql, connection);

        command.CommandTimeout = 0;
        command.Parameters.AddWithValue("@Id", ticketId);
        command.Parameters.AddWithValue("@Now", NowIst);

        await command.ExecuteNonQueryAsync();
    }

    // ============================================
    // STATUS CHANGE
    // ============================================

    public async Task ChangeStatusAsync(
        SqlConnection connection,
        int ticketId,
        int newStatusId,
        string newStatusName,
        int? changedBy,
        string? remarks = null)
    {
        // Purana status padho.
        int? oldStatusId = null;
        string oldStatusName = "";

        using (SqlCommand command = new SqlCommand(
            "SELECT [StatusId] FROM [DailySupport] WHERE [Id] = @Id",
            connection))
        {
            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@Id", ticketId);

            object? result = await command.ExecuteScalarAsync();

            if (result != null && result != DBNull.Value)
            {
                oldStatusId = Convert.ToInt32(result);
            }
        }

        if (oldStatusId.HasValue)
        {
            using SqlCommand command = new SqlCommand(
                "SELECT [StatusName] FROM [Mst_Status] " +
                "WHERE [StatusId] = @Id", connection);

            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@Id", oldStatusId.Value);

            object? result = await command.ExecuteScalarAsync();

            if (result != null && result != DBNull.Value)
            {
                oldStatusName = result.ToString() ?? "";
            }
        }

        // Resolved / Closed time stamp + review flag.
        bool isResolved = string.Equals(
            newStatusName, "Resolved", StringComparison.OrdinalIgnoreCase);
        bool isClosed = string.Equals(
            newStatusName, "Closed", StringComparison.OrdinalIgnoreCase);

        string sql = @"
            UPDATE [DailySupport] SET
                [StatusId] = @StatusId,
                [Status] = @StatusName,
                [UpdatedAt] = @Now,
                [ResolvedOn] = CASE WHEN @IsResolved = 1
                    THEN @Now ELSE [ResolvedOn] END,
                [ClosedOn] = CASE WHEN @IsClosed = 1
                    THEN @Now ELSE [ClosedOn] END
            WHERE [Id] = @Id";

        using (SqlCommand command =
               new SqlCommand(sql, connection))
        {
            command.CommandTimeout = 0;

            command.Parameters.AddWithValue("@StatusId", newStatusId);
            command.Parameters.AddWithValue("@StatusName", newStatusName);
            command.Parameters.AddWithValue("@Now", NowIst);
            command.Parameters.AddWithValue("@IsResolved", isResolved ? 1 : 0);
            command.Parameters.AddWithValue("@IsClosed", isClosed ? 1 : 0);
            command.Parameters.AddWithValue("@Id", ticketId);

            await command.ExecuteNonQueryAsync();
        }

        await AddHistoryAsync(
            connection,
            ticketId,
            "StatusChanged",
            oldStatusName,
            newStatusName,
            remarks,
            changedBy);
    }

    // Status name se id nikalo.
    public async Task<(int Id, string Name)?> GetStatusByNameAsync(
        SqlConnection connection,
        string statusName)
    {
        using SqlCommand command = new SqlCommand(
            "SELECT [StatusId], [StatusName] FROM [Mst_Status] " +
            "WHERE [StatusName] = @Name", connection);

        command.CommandTimeout = 0;
        command.Parameters.AddWithValue("@Name", statusName);

        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return (
                Convert.ToInt32(reader["StatusId"]),
                ToStr(reader["StatusName"]));
        }

        return null;
    }

    // Status id se name nikalo.
    public async Task<string?> GetStatusNameByIdAsync(
        SqlConnection connection,
        int statusId)
    {
        using SqlCommand command = new SqlCommand(
            "SELECT [StatusName] FROM [Mst_Status] " +
            "WHERE [StatusId] = @Id", connection);

        command.CommandTimeout = 0;
        command.Parameters.AddWithValue("@Id", statusId);

        object? result = await command.ExecuteScalarAsync();

        return result == null || result == DBNull.Value
            ? null
            : result.ToString();
    }

    // ============================================
    // REVIEW (boss decision)
    // ============================================

    public async Task AddReviewAsync(
        SqlConnection connection,
        int ticketId,
        int reviewedBy,
        string action,
        int? rating,
        string? remarks)
    {
        const string sql = @"
            INSERT INTO [Trn_DailySupport_Review]
            (
                [TicketId], [ReviewedBy], [ReviewAction],
                [Rating], [Remarks], [ReviewedAt]
            )
            VALUES
            (
                @TicketId, @ReviewedBy, @ReviewAction,
                @Rating, @Remarks, @ReviewedAt
            )";

        using SqlCommand command =
            new SqlCommand(sql, connection);

        command.CommandTimeout = 0;

        command.Parameters.AddWithValue("@TicketId", ticketId);
        command.Parameters.AddWithValue("@ReviewedBy", reviewedBy);
        command.Parameters.AddWithValue("@ReviewAction", action);
        command.Parameters.AddWithValue(
            "@Rating", (object?)rating ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@Remarks", (object?)remarks ?? DBNull.Value);
        command.Parameters.AddWithValue("@ReviewedAt", NowIst);

        await command.ExecuteNonQueryAsync();

        // Ticket pe review flags update.
        string reviewStatus = string.Equals(
            action, "Approved", StringComparison.OrdinalIgnoreCase)
            ? "Approved"
            : "Rejected";

        string updateSql = @"
            UPDATE [DailySupport] SET
                [ReviewStatus] = @ReviewStatus,
                [ReviewedBy] = @ReviewedBy,
                [ReviewedAt] = @Now,
                [UpdatedAt] = @Now
            WHERE [Id] = @Id";

        using SqlCommand update =
            new SqlCommand(updateSql, connection);

        update.CommandTimeout = 0;
        update.Parameters.AddWithValue("@ReviewStatus", reviewStatus);
        update.Parameters.AddWithValue("@ReviewedBy", reviewedBy);
        update.Parameters.AddWithValue("@Now", NowIst);
        update.Parameters.AddWithValue("@Id", ticketId);

        await update.ExecuteNonQueryAsync();
    }


    // ============================================
    // NOTIFICATIONS (in-app)
    // ============================================

    // Ek notification insert karta hai.
    public async Task AddNotificationAsync(
        SqlConnection connection,
        int userId,
        int? ticketId,
        string title,
        string? body,
        string kind,
        int? rating)
    {
        const string sql = @"
            INSERT INTO [Trn_Notification]
            (
                [UserId], [TicketId], [Title], [Body],
                [Kind], [Rating], [IsRead], [CreatedAt]
            )
            VALUES
            (
                @UserId, @TicketId, @Title, @Body,
                @Kind, @Rating, 0, @CreatedAt
            )";

        using SqlCommand command = new SqlCommand(sql, connection);

        command.CommandTimeout = 0;

        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue(
            "@TicketId", (object?)ticketId ?? DBNull.Value);
        command.Parameters.AddWithValue("@Title", title);
        command.Parameters.AddWithValue(
            "@Body", (object?)body ?? DBNull.Value);
        command.Parameters.AddWithValue("@Kind", kind);
        command.Parameters.AddWithValue(
            "@Rating", (object?)rating ?? DBNull.Value);
        command.Parameters.AddWithValue("@CreatedAt", NowIst);

        await command.ExecuteNonQueryAsync();
    }

    // Ek user ke unread notifications ka count.
    public async Task<int> UnreadCountAsync(
        SqlConnection connection, int userId)
    {
        const string sql = @"
            SELECT COUNT(*)
            FROM [Trn_Notification]
            WHERE [UserId] = @UserId AND [IsRead] = 0";

        using SqlCommand command = new SqlCommand(sql, connection);

        command.CommandTimeout = 0;
        command.Parameters.AddWithValue("@UserId", userId);

        object? result = await command.ExecuteScalarAsync();

        return result == null || result == DBNull.Value
            ? 0
            : Convert.ToInt32(result);
    }

    // Ek user ki recent notifications (nayi pehle).
    // ticketId diya to sirf us ticket ki notifications.
    public async Task<List<TicketNotification>> RecentNotificationsAsync(
        SqlConnection connection, int userId, int take = 20,
        int? ticketId = null)
    {
        var list = new List<TicketNotification>();

        string sql = @"
            SELECT TOP (@Take)
                n.[Id], n.[UserId], n.[TicketId], n.[Title],
                n.[Body], n.[Kind], n.[Rating], n.[IsRead],
                n.[CreatedAt],
                ISNULL(d.[TicketNo], '') AS [TicketNo]
            FROM [Trn_Notification] n
            LEFT JOIN [DailySupport] d ON d.[Id] = n.[TicketId]
            WHERE n.[UserId] = @UserId";

        if (ticketId.HasValue)
        {
            sql += " AND n.[TicketId] = @TicketId";
        }

        sql += " ORDER BY n.[Id] DESC";

        using SqlCommand command = new SqlCommand(sql, connection);

        command.CommandTimeout = 0;
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@Take", take);

        if (ticketId.HasValue)
        {
            command.Parameters.AddWithValue(
                "@TicketId", ticketId.Value);
        }

        using SqlDataReader reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            list.Add(new TicketNotification
            {
                Id = Convert.ToInt32(reader["Id"]),
                UserId = Convert.ToInt32(reader["UserId"]),
                TicketId = ToIntOrNull(reader["TicketId"]),
                TicketNo = ToStr(reader["TicketNo"]),
                Title = ToStr(reader["Title"]),
                Body = ToStr(reader["Body"]),
                Kind = ToStr(reader["Kind"]),
                Rating = ToIntOrNull(reader["Rating"]),
                IsRead = Convert.ToBoolean(reader["IsRead"]),
                CreatedAt = Convert.ToDateTime(reader["CreatedAt"])
            });
        }

        return list;
    }

    // Saare notifications ko read mark karo.
    public async Task MarkAllReadAsync(
        SqlConnection connection, int userId)
    {
        const string sql = @"
            UPDATE [Trn_Notification]
            SET [IsRead] = 1
            WHERE [UserId] = @UserId AND [IsRead] = 0";

        using SqlCommand command = new SqlCommand(sql, connection);

        command.CommandTimeout = 0;
        command.Parameters.AddWithValue("@UserId", userId);

        await command.ExecuteNonQueryAsync();
    }

    // Ek ticket ki review history (nayi pehle).
    public async Task<List<TicketReviewItem>> ReviewsForTicketAsync(
        SqlConnection connection, int ticketId)
    {        var list = new List<TicketReviewItem>();

        const string sql = @"
            SELECT
                r.[ReviewId], r.[ReviewAction], r.[Rating],
                r.[Remarks], r.[ReviewedAt],
                ISNULL(u.[UserName], '') AS [ReviewedByName]
            FROM [Trn_DailySupport_Review] r
            LEFT JOIN [LoginUsers] u ON u.[Id] = r.[ReviewedBy]
            WHERE r.[TicketId] = @TicketId
            ORDER BY r.[ReviewId] DESC";

        using SqlCommand command = new SqlCommand(sql, connection);

        command.CommandTimeout = 0;
        command.Parameters.AddWithValue("@TicketId", ticketId);

        using SqlDataReader reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            list.Add(new TicketReviewItem
            {
                ReviewId = Convert.ToInt32(reader["ReviewId"]),
                ReviewedByName = ToStr(reader["ReviewedByName"]),
                ReviewAction = ToStr(reader["ReviewAction"]),
                Rating = ToIntOrNull(reader["Rating"]),
                Remarks = ToStr(reader["Remarks"]),
                ReviewedAt = Convert.ToDateTime(reader["ReviewedAt"])
            });
        }

        return list;
    }

    // Un tickets ka review pill (pending/approved/rejected) jinke
    // notifications hain — Index table ke liye.
    // Outstanding review = Pending Review status waale tickets.
    public async Task<Dictionary<int, string>> ReviewPillMapAsync(
        SqlConnection connection, int userId)
    {
        var map = new Dictionary<int, string>();

        // 1) Pending Review status waale apne tickets -> "pending"
        const string pendingSql = @"
            SELECT d.[Id]
            FROM [DailySupport] d
            INNER JOIN [Mst_Status] st ON st.[StatusId] = d.[StatusId]
            WHERE st.[StatusName] = 'Pending Review'
              AND (d.[AssignedTo] = @UserId OR d.[CreatedBy] = @UserId)";

        using (SqlCommand command =
               new SqlCommand(pendingSql, connection))
        {
            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@UserId", userId);

            using SqlDataReader reader =
                await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                map[Convert.ToInt32(reader["Id"])] = "pending";
            }
        }

        // 2) Latest review decision (approved/rejected) per ticket.
        const string reviewSql = @"
            SELECT r.[TicketId], r.[ReviewAction]
            FROM [Trn_DailySupport_Review] r
            INNER JOIN (
                SELECT [TicketId], MAX([ReviewId]) AS [MaxId]
                FROM [Trn_DailySupport_Review]
                GROUP BY [TicketId]
            ) m ON m.[MaxId] = r.[ReviewId]
            INNER JOIN [DailySupport] d ON d.[Id] = r.[TicketId]
            WHERE d.[AssignedTo] = @UserId OR d.[CreatedBy] = @UserId";

        using (SqlCommand command =
               new SqlCommand(reviewSql, connection))
        {
            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@UserId", userId);

            using SqlDataReader reader =
                await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                int ticketId = Convert.ToInt32(reader["TicketId"]);

                // Pending ko decision override na kare.
                if (map.TryGetValue(ticketId, out string? existing)
                    && existing == "pending")
                {
                    continue;
                }

                string action = ToStr(reader["ReviewAction"]);

                map[ticketId] = string.Equals(
                    action, "Approved", StringComparison.OrdinalIgnoreCase)
                    ? "approved"
                    : "rejected";
            }
        }

        return map;
    }

    // ============================================
    // ASSIGN / REASSIGN
    // ============================================

    public async Task AssignAsync(
        SqlConnection connection,
        int ticketId,
        int newUserId,
        int? changedBy)
    {
        // Purana assignee.
        string oldName = "";

        using (SqlCommand command = new SqlCommand(@"
            SELECT ISNULL(u.[UserName], '') AS UserName
            FROM [DailySupport] d
            LEFT JOIN [LoginUsers] u ON u.[Id] = d.[AssignedTo]
            WHERE d.[Id] = @Id", connection))
        {
            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@Id", ticketId);

            object? result = await command.ExecuteScalarAsync();

            if (result != null && result != DBNull.Value)
            {
                oldName = result.ToString() ?? "";
            }
        }

        string newName = "";

        using (SqlCommand command = new SqlCommand(
            "SELECT [UserName] FROM [LoginUsers] WHERE [Id] = @Id",
            connection))
        {
            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@Id", newUserId);

            object? result = await command.ExecuteScalarAsync();

            if (result != null && result != DBNull.Value)
            {
                newName = result.ToString() ?? "";
            }
        }

        using (SqlCommand command = new SqlCommand(@"
            UPDATE [DailySupport] SET
                [AssignedTo] = @UserId,
                [UserId] = @UserId,
                [UpdatedAt] = @Now
            WHERE [Id] = @Id", connection))
        {
            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@UserId", newUserId);
            command.Parameters.AddWithValue("@Now", NowIst);
            command.Parameters.AddWithValue("@Id", ticketId);

            await command.ExecuteNonQueryAsync();
        }

        await AddHistoryAsync(
            connection,
            ticketId,
            string.IsNullOrEmpty(oldName) ? "Assigned" : "Reassigned",
            oldName,
            newName,
            null,
            changedBy);
    }

    // ============================================
    // SMALL HELPERS
    // ============================================

    public static string ToStr(object value) =>
        value == null || value == DBNull.Value
            ? ""
            : value.ToString() ?? "";

    public static int? ToIntOrNull(object value) =>
        value == null || value == DBNull.Value
            ? null
            : Convert.ToInt32(value);

    public static DateTime? ToDateOrNull(object value) =>
        value == null || value == DBNull.Value
            ? null
            : Convert.ToDateTime(value);
}
