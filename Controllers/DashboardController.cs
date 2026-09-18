using System.Collections.Generic;
using Master.Configuration;
using Master.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Master.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly IConfiguration _configuration;

    private readonly DynamicTableService _tableService;

    public DashboardController(
        IConfiguration configuration,
        DynamicTableService tableService)
    {
        _configuration = configuration;
        _tableService = tableService;
    }


    public async Task<IActionResult> Index(
        string? filter,
        DateTime? from,
        DateTime? to,
        int? stateId,
        int? userId,
        int? clientId,
        string? status,
        string? priority,
        bool partial = false)
    {
        string? connectionString =
            _configuration.GetConnectionString(
                "DefaultConnection");

        var viewModel = new DashboardViewModel
        {
            DateFilter = DashboardDateFilter.Resolve(
                filter,
                from,
                to),
            StateId = stateId,
            UserId = userId,
            ClientId = clientId,
            Status = DashboardViewModel.NormalizeStatus(
                status),
            Priority = DashboardViewModel.NormalizePriority(
                priority)
        };


        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            await using var connection =
                new SqlConnection(connectionString);

            await connection.OpenAsync();

            var schema =
                await _tableService.GetSchemaAsync(
                    connection,
                    "ClientMaster",
                    "States",
                    "LoginUsers",
                    "DailySupport",
                    "ClientVisiting");

            viewModel.States =
                await LoadStatesAsync(
                    connection,
                    schema);

            viewModel.Users =
                await LoadUsersAsync(
                    connection,
                    schema);

            viewModel.Clients =
                await LoadClientsAsync(
                    connection,
                    schema);

            // Drop selected ids that are not in the
            // lookup lists (invalid / stale values).
            viewModel.StateId = DashboardViewModel.NormalizeId(
                viewModel.StateId,
                viewModel.States);

            viewModel.UserId = DashboardViewModel.NormalizeId(
                viewModel.UserId,
                viewModel.Users);

            viewModel.ClientId = DashboardViewModel.NormalizeId(
                viewModel.ClientId,
                viewModel.Clients);


            // The analytics + tab queries are NOT dependent
            // on each other, so they run in parallel waves
            // (max 3 open connections at a time) instead of
            // ~20 sequential round-trips to the hosted DB.
            // This makes date-filter loading much faster.

            var summaryTask =
                LoadWithNewConnectionAsync(
                    connectionString,
                    schema,
                    viewModel,
                    LoadSummaryAsync);

            var statusTask =
                LoadWithNewConnectionAsync(
                    connectionString,
                    schema,
                    viewModel,
                    LoadSupportStatusAsync);

            var priorityTask =
                LoadWithNewConnectionAsync(
                    connectionString,
                    schema,
                    viewModel,
                    LoadSupportPriorityAsync);

            await Task.WhenAll(
                summaryTask,
                statusTask,
                priorityTask);

            viewModel.Summary =
                await summaryTask;

            viewModel.SupportStatus =
                await statusTask;

            viewModel.SupportPriority =
                await priorityTask;


            var clientsByStateTask =
                LoadWithNewConnectionAsync(
                    connectionString,
                    schema,
                    viewModel,
                    LoadClientsByStateAsync);

            var monthlyTask =
                LoadWithNewConnectionAsync(
                    connectionString,
                    schema,
                    viewModel,
                    LoadMonthlyActivityAsync);

            var upcomingTask =
                LoadWithNewConnectionAsync(
                    connectionString,
                    schema,
                    viewModel,
                    LoadUpcomingFollowUpsAsync);

            await Task.WhenAll(
                clientsByStateTask,
                monthlyTask,
                upcomingTask);

            viewModel.ClientsByState =
                await clientsByStateTask;

            viewModel.MonthlyActivity =
                await monthlyTask;

            viewModel.UpcomingFollowUps =
                await upcomingTask;


            var supportTabTask =
                LoadWithNewConnectionAsync(
                    connectionString,
                    schema,
                    viewModel,
                    LoadSupportTabAsync);

            var visitsTabTask =
                LoadWithNewConnectionAsync(
                    connectionString,
                    schema,
                    viewModel,
                    LoadVisitsTabAsync);

            var clientsTabTask =
                LoadWithNewConnectionAsync(
                    connectionString,
                    schema,
                    viewModel,
                    LoadClientsTabAsync);

            await Task.WhenAll(
                supportTabTask,
                visitsTabTask,
                clientsTabTask);

            viewModel.SupportTab =
                await supportTabTask;

            viewModel.VisitsTab =
                await visitsTabTask;

            viewModel.ClientsTab =
                await clientsTabTask;
        }


        return partial
            ? (IActionResult)PartialView(
                "_DashboardContent",
                viewModel)
            : View(viewModel);
    }


    // Runs a dashboard loader on its own connection so
    // independent queries can run in parallel against
    // the slow hosted database (each wave caps at 3).
    private static async Task<T>
        LoadWithNewConnectionAsync<T>(
            string connectionString,
            DynamicTableService.SchemaColumns schema,
            DashboardViewModel viewModel,
            Func<
                SqlConnection,
                DynamicTableService.SchemaColumns,
                DashboardViewModel,
                Task<T>> loader)
    {
        await using var connection =
            new SqlConnection(connectionString);

        await connection.OpenAsync();

        return await loader(
            connection,
            schema,
            viewModel);
    }


    // Resolves alias + [column] for a column that exists
    // in the current database schema (else null).
    private static string? Col(
        DynamicTableService.SchemaColumns schema,
        string table,
        string column)
    {
        return schema.Has(table, column)
            ? $"[{column}]"
            : null;
    }


    // =====================================================
    // SUMMARY CARDS (COMPONENT 4)
    //
    // Per-card filter matrix ("where logically applicable"):
    //   Total Users    - none (plain master total)
    //   Total Clients  - date(EntryOn), state, user, client
    //   Total Support  - date(SupportDate), state, user,
    //                    client, status, priority
    //   Total Visits   - date(VisitDate), state, user, client
    //   Pending        - date, state, user, client, priority
    //                    (status pinned to 'Pending')
    //   In Progress    - date, state, user, client, priority
    //                    (status pinned to 'In Progress')
    //   Follow-up      - date(FollowUpDate), state, user,
    //                    client, status, priority (excludes Cancelled)
    //   Active States  - date(EntryOn), state, user
    // =====================================================

    private static async Task<DashboardSummaryViewModel>
        LoadSummaryAsync(
            SqlConnection connection,
            DynamicTableService.SchemaColumns schema,
            DashboardViewModel viewModel)
    {
        var summary =
            new DashboardSummaryViewModel();

        summary.TotalUsers =
            await CountUsersAsync(
                connection,
                schema);

        summary.TotalClients =
            await CountClientsAsync(
                connection,
                schema,
                viewModel);

        summary.TotalVisits =
            await CountVisitsAsync(
                connection,
                schema,
                viewModel);

        summary.ActiveStates =
            await CountActiveStatesAsync(
                connection,
                schema,
                viewModel);

        await LoadDailySupportSummaryAsync(
            connection,
            schema,
            viewModel,
            summary);

        return summary;
    }


    private static async Task<int> CountUsersAsync(
        SqlConnection connection,
        DynamicTableService.SchemaColumns schema)
    {
        if (!schema.HasTable("LoginUsers"))
        {
            return 0;
        }

        string query = $@"
            SELECT
                COUNT(*)

            FROM
                [LoginUsers]";

        using SqlCommand command =
            new SqlCommand(query, connection);
            command.CommandTimeout = 0;

        return Convert.ToInt32(
            await command.ExecuteScalarAsync());
    }


    private static async Task<int> CountClientsAsync(
        SqlConnection connection,
        DynamicTableService.SchemaColumns schema,
        DashboardViewModel viewModel)
    {
        string? cEntry =
            Col(schema, "ClientMaster", "EntryOn");

        string? cStateId =
            Col(schema, "ClientMaster", "StateId");

        string? cUserId =
            Col(schema, "ClientMaster", "UserId");

        string? cId =
            Col(schema, "ClientMaster", "Id");

        // Without the primary key there is no client table
        // to count — degrade to 0.
        if (cId == null)
        {
            return 0;
        }

        var wheres =
            new List<string>();

        using (var command = new SqlCommand())
        {
            command.Connection = connection;
            command.CommandTimeout = 0;

            AddDateRange(
                wheres,
                command.Parameters,
                viewModel.DateFilter,
                cEntry);

            AddIf(
                wheres,
                command.Parameters,
                viewModel.StateId.HasValue && cStateId != null,
                $"c.{cStateId} = @StateId",
                "@StateId",
                viewModel.StateId);

            AddIf(
                wheres,
                command.Parameters,
                viewModel.UserId.HasValue && cUserId != null,
                $"c.{cUserId} = @UserId",
                "@UserId",
                viewModel.UserId);

            AddIf(
                wheres,
                command.Parameters,
                viewModel.ClientId.HasValue,
                $"c.{cId} = @ClientId",
                "@ClientId",
                viewModel.ClientId);

            string whereSql =
                wheres.Count > 0
                    ? " WHERE "
                        + string.Join(
                            " AND ",
                            wheres)
                    : "";

            command.CommandText = $@"
                SELECT
                    COUNT(*)

                FROM
                    [ClientMaster] c
                {whereSql}";

            return Convert.ToInt32(
                await command.ExecuteScalarAsync());
        }
    }


    private static async Task<int> CountVisitsAsync(
        SqlConnection connection,
        DynamicTableService.SchemaColumns schema,
        DashboardViewModel viewModel)
    {
        string? vVisitDate =
            Col(schema, "ClientVisiting", "VisitDate");

        string? vUserId =
            Col(schema, "ClientVisiting", "UserId");

        string? vClientId =
            Col(schema, "ClientVisiting", "ClientId");

        string? cId =
            Col(schema, "ClientMaster", "Id");

        string? cStateId =
            Col(schema, "ClientMaster", "StateId");

        // The count depends on the ClientMaster join —
        // without the linking columns it cannot run.
        if (cId == null || vClientId == null)
        {
            return 0;
        }

        var wheres =
            new List<string>();

        using (var command = new SqlCommand())
        {
            command.Connection = connection;
            command.CommandTimeout = 0;

            AddDateRange(
                wheres,
                command.Parameters,
                viewModel.DateFilter,
                vVisitDate);

            AddIf(
                wheres,
                command.Parameters,
                viewModel.StateId.HasValue && cStateId != null,
                $"c.{cStateId} = @StateId",
                "@StateId",
                viewModel.StateId);

            AddIf(
                wheres,
                command.Parameters,
                viewModel.UserId.HasValue && vUserId != null,
                $"v.{vUserId} = @UserId",
                "@UserId",
                viewModel.UserId);

            AddIf(
                wheres,
                command.Parameters,
                viewModel.ClientId.HasValue,
                $"v.{vClientId} = @ClientId",
                "@ClientId",
                viewModel.ClientId);

            string whereSql =
                wheres.Count > 0
                    ? " WHERE "
                        + string.Join(
                            " AND ",
                            wheres)
                    : "";

            command.CommandText = $@"
                SELECT
                    COUNT(*)

                FROM
                    [ClientVisiting] v

                INNER JOIN
                    [ClientMaster] c
                    ON c.{cId}
                    = v.{vClientId}
                {whereSql}";

            return Convert.ToInt32(
                await command.ExecuteScalarAsync());
        }
    }


    private static async Task<int> CountActiveStatesAsync(
        SqlConnection connection,
        DynamicTableService.SchemaColumns schema,
        DashboardViewModel viewModel)
    {
        string? sIsActive =
            Col(schema, "States", "IsActive");

        string? sEntryOn =
            Col(schema, "States", "EntryOn");

        string? sId =
            Col(schema, "States", "Id");

        string? sUserId =
            Col(schema, "States", "UserId");

        // "Active states" is meaningless without the
        // IsActive flag — degrade to 0.
        if (sIsActive == null)
        {
            return 0;
        }

        var wheres =
            new List<string>
            {
                $"s.{sIsActive} = 1"
            };

        using (var command = new SqlCommand())
        {
            command.Connection = connection;
            command.CommandTimeout = 0;

            AddDateRange(
                wheres,
                command.Parameters,
                viewModel.DateFilter,
                sEntryOn);

            AddIf(
                wheres,
                command.Parameters,
                viewModel.StateId.HasValue && sId != null,
                $"s.{sId} = @StateId",
                "@StateId",
                viewModel.StateId);

            AddIf(
                wheres,
                command.Parameters,
                viewModel.UserId.HasValue && sUserId != null,
                $"s.{sUserId} = @UserId",
                "@UserId",
                viewModel.UserId);

            string whereSql =
                " WHERE "
                + string.Join(
                    " AND ",
                    wheres);

            command.CommandText = $@"
                SELECT
                    COUNT(*)

                FROM
                    [States] s
                {whereSql}";

            return Convert.ToInt32(
                await command.ExecuteScalarAsync());
        }
    }


    // Total Support + Pending + In Progress in one pass,
    // then the Follow-up count in its own query.
    private static async Task
        LoadDailySupportSummaryAsync(
            SqlConnection connection,
            DynamicTableService.SchemaColumns schema,
            DashboardViewModel viewModel,
            DashboardSummaryViewModel summary)
    {
        string? dsSupportDate =
            Col(schema, "DailySupport", "SupportDate");

        string? dsStatus =
            Col(schema, "DailySupport", "Status");

        string? dsClientId =
            Col(schema, "DailySupport", "ClientId");

        string? dsFollowUpDate =
            Col(schema, "DailySupport", "FollowUpDate");

        string? cId =
            Col(schema, "ClientMaster", "Id");

        bool supportsStatus =
            dsStatus != null;

        if (cId != null && dsClientId != null)
        {
            var wheres =
                new List<string>();

            using (var command = new SqlCommand())
            {
                command.Connection = connection;
                command.CommandTimeout = 0;

                AddDailySupportFilters(
                    wheres,
                    command.Parameters,
                    viewModel,
                    schema,
                    dsSupportDate);

                string whereSql =
                    wheres.Count > 0
                        ? " WHERE "
                            + string.Join(
                                " AND ",
                                wheres)
                        : "";

                string pendingTerm =
                    supportsStatus
                        ? $"SUM(CASE WHEN ds.{dsStatus}"
                            + " = 'Pending' THEN 1 ELSE 0 END)"
                        : "0";

                string inProgressTerm =
                    supportsStatus
                        ? $"SUM(CASE WHEN ds.{dsStatus}"
                            + " = 'In Progress' THEN 1 ELSE 0 END)"
                        : "0";

                command.CommandText = $@"
                    SELECT
                        COUNT(*) AS TotalSupport,
                        {pendingTerm} AS PendingCount,
                        {inProgressTerm} AS InProgressCount

                    FROM
                        [DailySupport] ds

                    INNER JOIN
                        [ClientMaster] c
                        ON c.{cId}
                        = ds.{dsClientId}
                    {whereSql}";

                using SqlDataReader reader =
                    await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    summary.TotalSupport =
                        Convert.ToInt32(
                            reader["TotalSupport"]);

                    summary.PendingCount =
                        reader["PendingCount"] == DBNull.Value
                            ? 0
                            : Convert.ToInt32(
                                reader["PendingCount"]);

                    summary.InProgressCount =
                        reader["InProgressCount"] == DBNull.Value
                            ? 0
                            : Convert.ToInt32(
                                reader["InProgressCount"]);
                }
            }
        }


        // ---- Follow-up: upcoming FollowUpDate, not Cancelled ----
        if (dsFollowUpDate != null && dsStatus != null
            && cId != null && dsClientId != null)
        {
            var followWheres =
                new List<string>
                {
                    $"ds.{dsFollowUpDate}"
                        + " >= @Today",
                    $"ds.{dsStatus}"
                        + " <> 'Cancelled'"
                };

            using (var command = new SqlCommand())
            {
                command.Connection = connection;
                command.CommandTimeout = 0;

                command.Parameters.AddWithValue(
                    "@Today",
                    DateTime.Today);

                AddDailySupportFilters(
                    followWheres,
                    command.Parameters,
                    viewModel,
                    schema,
                    dsFollowUpDate);

                command.CommandText = $@"
                    SELECT
                        COUNT(*)

                    FROM
                        [DailySupport] ds

                    INNER JOIN
                        [ClientMaster] c
                        ON c.{cId}
                        = ds.{dsClientId}

                    WHERE
                        {string.Join(" AND ", followWheres)}";

                summary.FollowUpCount =
                    Convert.ToInt32(
                        await command.ExecuteScalarAsync());
            }
        }
    }


    // Support Status analytics (COMPONENT 5) —
    // one pass, five conditional counts.
    // The global Status dropdown is intentionally
    // NOT applied (the section IS the status dimension).
    private static async Task<
        DashboardSupportStatusViewModel>
        LoadSupportStatusAsync(
            SqlConnection connection,
            DynamicTableService.SchemaColumns schema,
            DashboardViewModel viewModel)
    {
        var status =
            new DashboardSupportStatusViewModel();

        string? dsSupportDate =
            Col(schema, "DailySupport", "SupportDate");

        string? dsStatus =
            Col(schema, "DailySupport", "Status");

        string? dsClientId =
            Col(schema, "DailySupport", "ClientId");

        string? cId =
            Col(schema, "ClientMaster", "Id");

        // The whole section IS the status dimension — it
        // cannot render without a Status column.
        if (dsStatus == null || cId == null
            || dsClientId == null)
        {
            return status;
        }

        var wheres =
            new List<string>();

        using (var command = new SqlCommand())
        {
            command.Connection = connection;
            command.CommandTimeout = 0;

            AddDailySupportFilters(
                wheres,
                command.Parameters,
                viewModel,
                schema,
                dsSupportDate,
                applyStatusFilter: false);

            string whereSql =
                wheres.Count > 0
                    ? " WHERE "
                        + string.Join(
                            " AND ",
                            wheres)
                    : "";

            command.CommandText = $@"
                SELECT
                    SUM(CASE WHEN ds.{dsStatus}
                        = 'Open' THEN 1 ELSE 0 END) AS OpenCount,
                    SUM(CASE WHEN ds.{dsStatus}
                        = 'In Progress' THEN 1 ELSE 0 END) AS InProgressCount,
                    SUM(CASE WHEN ds.{dsStatus}
                        = 'Pending' THEN 1 ELSE 0 END) AS PendingCount,
                    SUM(CASE WHEN ds.{dsStatus}
                        = 'Completed' THEN 1 ELSE 0 END) AS CompletedCount,
                    SUM(CASE WHEN ds.{dsStatus}
                        = 'Cancelled' THEN 1 ELSE 0 END) AS CancelledCount

                FROM
                    [DailySupport] ds

                INNER JOIN
                    [ClientMaster] c
                    ON c.{cId}
                    = ds.{dsClientId}
                {whereSql}";

            using SqlDataReader reader =
                await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                status.OpenCount =
                    reader["OpenCount"] == DBNull.Value
                        ? 0
                        : Convert.ToInt32(
                            reader["OpenCount"]);

                status.InProgressCount =
                    reader["InProgressCount"] == DBNull.Value
                        ? 0
                        : Convert.ToInt32(
                            reader["InProgressCount"]);

                status.PendingCount =
                    reader["PendingCount"] == DBNull.Value
                        ? 0
                        : Convert.ToInt32(
                            reader["PendingCount"]);

                status.CompletedCount =
                    reader["CompletedCount"] == DBNull.Value
                        ? 0
                        : Convert.ToInt32(
                            reader["CompletedCount"]);

                status.CancelledCount =
                    reader["CancelledCount"] == DBNull.Value
                        ? 0
                        : Convert.ToInt32(
                            reader["CancelledCount"]);
            }
        }

        return status;
    }


    // Support Priority analytics (COMPONENT 6) —
    // one pass, four conditional counts.
    // ALL filters apply here, including Status and Priority
    // (the selected Priority filter must affect the result).
    private static async Task<
        DashboardSupportPriorityViewModel>
        LoadSupportPriorityAsync(
            SqlConnection connection,
            DynamicTableService.SchemaColumns schema,
            DashboardViewModel viewModel)
    {
        var priority =
            new DashboardSupportPriorityViewModel();

        string? dsSupportDate =
            Col(schema, "DailySupport", "SupportDate");

        string? dsPriority =
            Col(schema, "DailySupport", "Priority");

        string? dsClientId =
            Col(schema, "DailySupport", "ClientId");

        string? cId =
            Col(schema, "ClientMaster", "Id");

        // The section IS the priority dimension — it cannot
        // render without a Priority column.
        if (dsPriority == null || cId == null
            || dsClientId == null)
        {
            return priority;
        }

        var wheres =
            new List<string>();

        using (var command = new SqlCommand())
        {
            command.Connection = connection;
            command.CommandTimeout = 0;

            AddDailySupportFilters(
                wheres,
                command.Parameters,
                viewModel,
                schema,
                dsSupportDate);

            string whereSql =
                wheres.Count > 0
                    ? " WHERE "
                        + string.Join(
                            " AND ",
                            wheres)
                    : "";

            command.CommandText = $@"
                SELECT
                    SUM(CASE WHEN ds.{dsPriority}
                        = 'Low' THEN 1 ELSE 0 END) AS LowCount,
                    SUM(CASE WHEN ds.{dsPriority}
                        = 'Medium' THEN 1 ELSE 0 END) AS MediumCount,
                    SUM(CASE WHEN ds.{dsPriority}
                        = 'High' THEN 1 ELSE 0 END) AS HighCount,
                    SUM(CASE WHEN ds.{dsPriority}
                        = 'Urgent' THEN 1 ELSE 0 END) AS UrgentCount

                FROM
                    [DailySupport] ds

                INNER JOIN
                    [ClientMaster] c
                    ON c.{cId}
                    = ds.{dsClientId}
                {whereSql}";

            using SqlDataReader reader =
                await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                priority.LowCount =
                    reader["LowCount"] == DBNull.Value
                        ? 0
                        : Convert.ToInt32(
                            reader["LowCount"]);

                priority.MediumCount =
                    reader["MediumCount"] == DBNull.Value
                        ? 0
                        : Convert.ToInt32(
                            reader["MediumCount"]);

                priority.HighCount =
                    reader["HighCount"] == DBNull.Value
                        ? 0
                        : Convert.ToInt32(
                            reader["HighCount"]);

                priority.UrgentCount =
                    reader["UrgentCount"] == DBNull.Value
                        ? 0
                        : Convert.ToInt32(
                            reader["UrgentCount"]);
            }
        }

        return priority;
    }


    // Clients by State (COMPONENT 7) —
    // ClientMaster.StateId -> States.Id group count.
    // Applies Date(EntryOn), State, User, Client.
    // Status/Priority are NOT applied (no valid relationship).
    // States with zero matching clients are omitted.
    // Sorted by count descending (ties alphabetical).
    private static async Task<
        DashboardClientsByStateViewModel>
        LoadClientsByStateAsync(
            SqlConnection connection,
            DynamicTableService.SchemaColumns schema,
            DashboardViewModel viewModel)
    {
        var model =
            new DashboardClientsByStateViewModel();

        string? cEntry =
            Col(schema, "ClientMaster", "EntryOn");

        string? cStateId =
            Col(schema, "ClientMaster", "StateId");

        string? cUserId =
            Col(schema, "ClientMaster", "UserId");

        string? cId =
            Col(schema, "ClientMaster", "Id");

        string? sId =
            Col(schema, "States", "Id");

        string? sStateName =
            Col(schema, "States", "StateName");

        // The group-by needs the StateId -> States.Id link
        // and a displayable StateName — without those the
        // whole panel is skipped.
        if (cStateId == null || sId == null
            || sStateName == null || cId == null)
        {
            return model;
        }

        var wheres =
            new List<string>();

        using (var command = new SqlCommand())
        {
            command.Connection = connection;
            command.CommandTimeout = 0;

            AddDateRange(
                wheres,
                command.Parameters,
                viewModel.DateFilter,
                cEntry);

            AddIf(
                wheres,
                command.Parameters,
                viewModel.StateId.HasValue,
                $"c.{cStateId} = @StateId",
                "@StateId",
                viewModel.StateId);

            AddIf(
                wheres,
                command.Parameters,
                viewModel.UserId.HasValue && cUserId != null,
                $"c.{cUserId} = @UserId",
                "@UserId",
                viewModel.UserId);

            AddIf(
                wheres,
                command.Parameters,
                viewModel.ClientId.HasValue,
                $"c.{cId} = @ClientId",
                "@ClientId",
                viewModel.ClientId);

            string whereSql =
                wheres.Count > 0
                    ? " WHERE "
                        + string.Join(
                            " AND ",
                            wheres)
                    : "";

            command.CommandText = $@"
                SELECT
                    s.{sStateName},
                    COUNT(c.{cId})
                        AS ClientCount

                FROM
                    [States] s

                INNER JOIN
                    [ClientMaster] c
                    ON c.{cStateId}
                    = s.{sId}
                {whereSql}

                GROUP BY
                    s.{sId},
                    s.{sStateName}

                ORDER BY
                    ClientCount DESC,
                    s.{sStateName} ASC";

            using SqlDataReader reader =
                await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                model.States.Add(
                    new ClientStateCountViewModel
                    {
                        StateName =
                            reader["StateName"]
                                .ToString() ?? "",
                        ClientCount =
                            Convert.ToInt32(
                                reader["ClientCount"])
                    });
            }
        }

        return model;
    }


    // Monthly Activity (COMPONENT 8) —
    // DailySupport + ClientVisiting grouped by month behind the shared
    // DashboardFilter. The month window comes from the quick date filter
    // and is capped to the most recent 12 months; each month bar aggregates
    // both tables, months without records are rendered as empty bars.
    private static async Task<
        DashboardMonthlyActivityViewModel>
        LoadMonthlyActivityAsync(
            SqlConnection connection,
            DynamicTableService.SchemaColumns schema,
            DashboardViewModel viewModel)
    {
        var model =
            new DashboardMonthlyActivityViewModel();

        var today = DateTime.Today;

        // Window end month (quick date filter, else current month).
        var endMonth =
            viewModel.DateFilter.ToDate.HasValue
                ? new DateTime(
                    viewModel.DateFilter.ToDate.Value.Year,
                    viewModel.DateFilter.ToDate.Value.Month,
                    1)
                : new DateTime(today.Year, today.Month, 1);

        // Window start month (quick date filter, else 12 months back),
        // capped so we never render more than 12 columns.
        var startMonth =
            viewModel.DateFilter.FromDate.HasValue
                ? new DateTime(
                    viewModel.DateFilter.FromDate.Value.Year,
                    viewModel.DateFilter.FromDate.Value.Month,
                    1)
                : endMonth.AddMonths(-11);

        var minStart =
            endMonth.AddMonths(-11);

        if (startMonth < minStart)
        {
            startMonth = minStart;
        }

        var monthAfterEnd =
            endMonth.AddMonths(1);


        var supportCounts =
            await LoadMonthlySupportCountsAsync(
                connection,
                schema,
                viewModel,
                startMonth,
                monthAfterEnd);

        var visitingCounts =
            await LoadMonthlyVisitingCountsAsync(
                connection,
                schema,
                viewModel,
                startMonth,
                monthAfterEnd);


        // Build the columns (one per month of the window),
        // filling months with no records as 0.
        var culture =
            System.Globalization.CultureInfo
                .InvariantCulture;

        var current =
            startMonth;

        while (current <= endMonth)
        {
            int key =
                (current.Year * 12)
                + (current.Month - 1);

            supportCounts.TryGetValue(
                key,
                out int supportCount);

            visitingCounts.TryGetValue(
                key,
                out int visitingCount);

            model.Months.Add(
                new MonthlyActivityPointViewModel
                {
                    MonthLabel =
                        current.ToString("MMM", culture),
                    YearLabel =
                        current.ToString("yyyy", culture),
                    SupportCount = supportCount,
                    VisitingCount = visitingCount
                });

            current =
                current.AddMonths(1);
        }

        return model;
    }


    // DailySupport rows in the month window, grouped by month.
    // Date(SupportDate) + State, User, Client, Status, Priority.
    private static async Task<Dictionary<int, int>>
        LoadMonthlySupportCountsAsync(
            SqlConnection connection,
            DynamicTableService.SchemaColumns schema,
            DashboardViewModel viewModel,
            DateTime rangeStart,
            DateTime rangeEnd)
    {
        var counts =
            new Dictionary<int, int>();

        string? dsSupportDate =
            Col(schema, "DailySupport", "SupportDate");

        string? dsUserId =
            Col(schema, "DailySupport", "UserId");

        string? dsClientId =
            Col(schema, "DailySupport", "ClientId");

        string? dsStatus =
            Col(schema, "DailySupport", "Status");

        string? dsPriority =
            Col(schema, "DailySupport", "Priority");

        string? cId =
            Col(schema, "ClientMaster", "Id");

        string? cStateId =
            Col(schema, "ClientMaster", "StateId");

        // The month grouping is based on SupportDate and the
        // ClientMaster join — skip when those are missing.
        if (dsSupportDate == null || cId == null
            || dsClientId == null)
        {
            return counts;
        }

        var wheres =
            new List<string>();

        using (var command = new SqlCommand())
        {
            command.Connection = connection;
            command.CommandTimeout = 0;

            command.Parameters.AddWithValue(
                "@RangeStart",
                rangeStart);

            command.Parameters.AddWithValue(
                "@RangeEnd",
                rangeEnd);

            wheres.Add(
                $"ds.{dsSupportDate}"
                + " >= @RangeStart"
                + $" AND ds.{dsSupportDate}"
                + " < @RangeEnd");

            AddIf(
                wheres,
                command.Parameters,
                viewModel.StateId.HasValue && cStateId != null,
                $"c.{cStateId} = @StateId",
                "@StateId",
                viewModel.StateId);

            AddIf(
                wheres,
                command.Parameters,
                viewModel.UserId.HasValue && dsUserId != null,
                $"ds.{dsUserId} = @UserId",
                "@UserId",
                viewModel.UserId);

            AddIf(
                wheres,
                command.Parameters,
                viewModel.ClientId.HasValue,
                $"ds.{dsClientId} = @ClientId",
                "@ClientId",
                viewModel.ClientId);

            AddIf(
                wheres,
                command.Parameters,
                viewModel.Status != "All" && dsStatus != null,
                $"ds.{dsStatus} = @Status",
                "@Status",
                viewModel.Status);

            AddIf(
                wheres,
                command.Parameters,
                viewModel.Priority != "All" && dsPriority != null,
                $"ds.{dsPriority} = @Priority",
                "@Priority",
                viewModel.Priority);

            string whereSql =
                " WHERE "
                + string.Join(
                    " AND ",
                    wheres);

            command.CommandText = $@"
                SELECT
                    YEAR(ds.{dsSupportDate})
                        AS ActivityYear,
                    MONTH(ds.{dsSupportDate})
                        AS ActivityMonth,
                    COUNT(*) AS ActivityCount

                FROM
                    [DailySupport] ds

                INNER JOIN
                    [ClientMaster] c
                    ON c.{cId}
                    = ds.{dsClientId}
                {whereSql}

                GROUP BY
                    YEAR(ds.{dsSupportDate}),
                    MONTH(ds.{dsSupportDate})";

            using SqlDataReader reader =
                await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                int year =
                    Convert.ToInt32(reader["ActivityYear"]);

                int month =
                    Convert.ToInt32(reader["ActivityMonth"]);

                int key =
                    (year * 12) + (month - 1);

                counts[key] =
                    Convert.ToInt32(reader["ActivityCount"]);
            }
        }

        return counts;
    }


    // ClientVisiting rows in the month window, grouped by month.
    // Date(VisitDate) + State, User, Client (no status/priority —
    // consistent with the Total Visits card).
    private static async Task<Dictionary<int, int>>
        LoadMonthlyVisitingCountsAsync(
            SqlConnection connection,
            DynamicTableService.SchemaColumns schema,
            DashboardViewModel viewModel,
            DateTime rangeStart,
            DateTime rangeEnd)
    {
        var counts =
            new Dictionary<int, int>();

        string? vVisitDate =
            Col(schema, "ClientVisiting", "VisitDate");

        string? vUserId =
            Col(schema, "ClientVisiting", "UserId");

        string? vClientId =
            Col(schema, "ClientVisiting", "ClientId");

        string? cId =
            Col(schema, "ClientMaster", "Id");

        string? cStateId =
            Col(schema, "ClientMaster", "StateId");

        // The month grouping is based on VisitDate and the
        // ClientMaster join — skip when those are missing.
        if (vVisitDate == null || cId == null
            || vClientId == null)
        {
            return counts;
        }

        var wheres =
            new List<string>();

        using (var command = new SqlCommand())
        {
            command.Connection = connection;
            command.CommandTimeout = 0;

            command.Parameters.AddWithValue(
                "@RangeStart",
                rangeStart);

            command.Parameters.AddWithValue(
                "@RangeEnd",
                rangeEnd);

            wheres.Add(
                $"v.{vVisitDate}"
                + " >= @RangeStart"
                + $" AND v.{vVisitDate}"
                + " < @RangeEnd");

            AddIf(
                wheres,
                command.Parameters,
                viewModel.StateId.HasValue && cStateId != null,
                $"c.{cStateId} = @StateId",
                "@StateId",
                viewModel.StateId);

            AddIf(
                wheres,
                command.Parameters,
                viewModel.UserId.HasValue && vUserId != null,
                $"v.{vUserId} = @UserId",
                "@UserId",
                viewModel.UserId);

            AddIf(
                wheres,
                command.Parameters,
                viewModel.ClientId.HasValue,
                $"v.{vClientId} = @ClientId",
                "@ClientId",
                viewModel.ClientId);

            string whereSql =
                " WHERE "
                + string.Join(
                    " AND ",
                    wheres);

            command.CommandText = $@"
                SELECT
                    YEAR(v.{vVisitDate})
                        AS ActivityYear,
                    MONTH(v.{vVisitDate})
                        AS ActivityMonth,
                    COUNT(*) AS ActivityCount

                FROM
                    [ClientVisiting] v

                INNER JOIN
                    [ClientMaster] c
                    ON c.{cId}
                    = v.{vClientId}
                {whereSql}

                GROUP BY
                    YEAR(v.{vVisitDate}),
                    MONTH(v.{vVisitDate})";

            using SqlDataReader reader =
                await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                int year =
                    Convert.ToInt32(reader["ActivityYear"]);

                int month =
                    Convert.ToInt32(reader["ActivityMonth"]);

                int key =
                    (year * 12) + (month - 1);

                counts[key] =
                    Convert.ToInt32(reader["ActivityCount"]);
            }
        }

        return counts;
    }


    // Upcoming Follow-ups (COMPONENT 9) —
    // DailySupport rows with an upcoming FollowUpDate (not Cancelled),
    // honouring the same filters as the Component 4 Follow-up card.
    // Resolves ClientName via ClientMaster and the assigned user via
    // LoginUsers (DailySupport.UserId -> LoginUsers.Id).
    private static async Task<
        DashboardUpcomingFollowUpsViewModel>
        LoadUpcomingFollowUpsAsync(
            SqlConnection connection,
            DynamicTableService.SchemaColumns schema,
            DashboardViewModel viewModel)
    {
        var model =
            new DashboardUpcomingFollowUpsViewModel();

        var today = DateTime.Today;

        string? dsFollowUpDate =
            Col(schema, "DailySupport", "FollowUpDate");

        string? dsStatus =
            Col(schema, "DailySupport", "Status");

        string? dsClientId =
            Col(schema, "DailySupport", "ClientId");

        string? dsUserId =
            Col(schema, "DailySupport", "UserId");

        string? dsSupportType =
            Col(schema, "DailySupport", "SupportType");

        string? dsSubject =
            Col(schema, "DailySupport", "Subject");

        string? cId =
            Col(schema, "ClientMaster", "Id");

        string? cName =
            Col(schema, "ClientMaster", "ClientName");

        string? uId =
            Col(schema, "LoginUsers", "Id");

        string? uName =
            Col(schema, "LoginUsers", "UserName");


        // ---- Total upcoming count ----
        if (dsFollowUpDate != null && dsStatus != null
            && cId != null && dsClientId != null)
        {
            var countWheres =
                new List<string>
                {
                    $"ds.{dsFollowUpDate}"
                        + " >= @Today",
                    $"ds.{dsStatus}"
                        + " <> 'Cancelled'"
                };

            using (var command = new SqlCommand())
            {
                command.Connection = connection;
                command.CommandTimeout = 0;

                command.Parameters.AddWithValue(
                    "@Today",
                    today);

                AddDailySupportFilters(
                    countWheres,
                    command.Parameters,
                    viewModel,
                    schema,
                    dsFollowUpDate);

                command.CommandText = $@"
                    SELECT
                        COUNT(*)

                    FROM
                        [DailySupport] ds

                    INNER JOIN
                        [ClientMaster] c
                        ON c.{cId}
                        = ds.{dsClientId}

                    WHERE
                        {string.Join(" AND ", countWheres)}";

                model.TotalUpcoming =
                    Convert.ToInt32(
                        await command.ExecuteScalarAsync());
            }
        }


        // ---- Next 6 upcoming rows ----
        if (dsFollowUpDate != null && dsStatus != null
            && cId != null && dsClientId != null
            && cName != null && dsSupportType != null
            && dsSubject != null && uId != null
            && dsUserId != null && uName != null)
        {
            var listWheres =
                new List<string>
                {
                    $"ds.{dsFollowUpDate}"
                        + " >= @Today",
                    $"ds.{dsStatus}"
                        + " <> 'Cancelled'"
                };

            using (var command = new SqlCommand())
            {
                command.Connection = connection;
                command.CommandTimeout = 0;

                command.Parameters.AddWithValue(
                    "@Today",
                    today);

                AddDailySupportFilters(
                    listWheres,
                    command.Parameters,
                    viewModel,
                    schema,
                    dsFollowUpDate);

                command.CommandText = $@"
                    SELECT TOP 6
                        c.{cName}
                            AS ClientName,
                        ds.{dsFollowUpDate}
                            AS FollowUpDate,
                        ds.{dsSupportType}
                            AS SupportType,
                        ds.{dsSubject}
                            AS Subject,
                        ds.{dsStatus}
                            AS Status,
                        u.{uName}
                            AS UserName

                    FROM
                        [DailySupport] ds

                    INNER JOIN
                        [ClientMaster] c
                        ON c.{cId}
                        = ds.{dsClientId}

                    INNER JOIN
                        [LoginUsers] u
                        ON u.{uId}
                        = ds.{dsUserId}

                    WHERE
                        {string.Join(" AND ", listWheres)}

                    ORDER BY
                        ds.{dsFollowUpDate} ASC";

                using SqlDataReader reader =
                    await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    string status =
                        reader["Status"].ToString() ?? "";

                    string dateText =
                        reader["FollowUpDate"] == DBNull.Value
                            ? ""
                            : Convert.ToDateTime(
                                    reader["FollowUpDate"])
                                .ToString(
                                    "dd MMM yyyy",
                                    System.Globalization
                                        .CultureInfo
                                        .InvariantCulture);

                    model.Items.Add(
                        new FollowUpItemViewModel
                        {
                            ClientName =
                                reader["ClientName"].ToString() ?? "",
                            FollowUpDateText = dateText,
                            SupportType =
                                reader["SupportType"].ToString() ?? "",
                            Subject =
                                reader["Subject"].ToString() ?? "",
                            Status = status,
                            StatusClass =
                                StatusPillClass(status),
                            UserName =
                                reader["UserName"].ToString() ?? ""
                        });
                }
            }
        }

        return model;
    }


    // Support Tab (COMPONENT 10) —
    // Management/analytics view, not a CRUD duplicate. Reuses the
    // Component 5 + 6 loaders (they run behind the same shared
    // DashboardFilter) and a dedicated TOP-6 recent records query.
    private static async Task<
        DashboardSupportTabViewModel>
        LoadSupportTabAsync(
            SqlConnection connection,
            DynamicTableService.SchemaColumns schema,
            DashboardViewModel viewModel)
    {
        var model =
            new DashboardSupportTabViewModel();

        model.Status =
            await LoadSupportStatusAsync(
                connection,
                schema,
                viewModel);

        model.Priority =
            await LoadSupportPriorityAsync(
                connection,
                schema,
                viewModel);

        model.Recent =
            await LoadRecentSupportsAsync(
                connection,
                schema,
                viewModel);

        return model;
    }


    // Recent Support records (COMPONENT 10) —
    // Newest 6 DailySupport rows matching ALL Dashboard filters
    // (Date on SupportDate, State via ClientMaster, User via
    // DailySupport.UserId -> LoginUsers.Id, Client, Status, Priority).
    private static async Task<System.Collections.Generic.List<
        RecentSupportItemViewModel>>
        LoadRecentSupportsAsync(
            SqlConnection connection,
            DynamicTableService.SchemaColumns schema,
            DashboardViewModel viewModel)
    {
        var items =
            new System.Collections.Generic.List<
                RecentSupportItemViewModel>();

        string? dsSupportDate =
            Col(schema, "DailySupport", "SupportDate");

        string? dsUserId =
            Col(schema, "DailySupport", "UserId");

        string? dsClientId =
            Col(schema, "DailySupport", "ClientId");

        string? dsSupportType =
            Col(schema, "DailySupport", "SupportType");

        string? dsSubject =
            Col(schema, "DailySupport", "Subject");

        string? dsStatus =
            Col(schema, "DailySupport", "Status");

        string? dsPriority =
            Col(schema, "DailySupport", "Priority");

        string? dsFollowUpDate =
            Col(schema, "DailySupport", "FollowUpDate");

        string? dsId =
            Col(schema, "DailySupport", "Id");

        string? cId =
            Col(schema, "ClientMaster", "Id");

        string? cName =
            Col(schema, "ClientMaster", "ClientName");

        string? uId =
            Col(schema, "LoginUsers", "Id");

        string? uName =
            Col(schema, "LoginUsers", "UserName");

        // The list needs the core display columns + both
        // joins — otherwise it is skipped entirely.
        if (dsSupportDate == null || dsClientId == null
            || cId == null || cName == null
            || dsUserId == null || uId == null
            || uName == null || dsStatus == null)
        {
            return items;
        }

        string followUpTerm =
            dsFollowUpDate != null
                ? $", ds.{dsFollowUpDate} AS FollowUpDate"
                : "";

        string priorityTerm =
            dsPriority != null
                ? $", ds.{dsPriority} AS Priority"
                : "";

        string orderBy =
            dsId != null
                ? $"ds.{dsSupportDate} DESC, ds.{dsId} DESC"
                : $"ds.{dsSupportDate} DESC";

        var wheres =
            new List<string>();

        using (var command = new SqlCommand())
        {
            command.Connection = connection;
            command.CommandTimeout = 0;

            AddDailySupportFilters(
                wheres,
                command.Parameters,
                viewModel,
                schema,
                dsSupportDate);

            string whereSql =
                wheres.Count > 0
                    ? " WHERE "
                        + string.Join(
                            " AND ",
                            wheres)
                    : "";

            command.CommandText = $@"
                SELECT TOP 6
                    ds.{dsSupportDate}
                        AS SupportDate,
                    c.{cName}
                        AS ClientName,
                    u.{uName}
                        AS UserName,
                    ds.{dsSupportType}
                        AS SupportType,
                    ds.{dsSubject}
                        AS Subject,
                    ds.{dsStatus}
                        AS Status
                    {priorityTerm}
                    {followUpTerm}

                FROM
                    [DailySupport] ds

                INNER JOIN
                    [ClientMaster] c
                    ON c.{cId}
                    = ds.{dsClientId}

                INNER JOIN
                    [LoginUsers] u
                    ON u.{uId}
                    = ds.{dsUserId}
                {whereSql}

                ORDER BY
                    {orderBy}";

            using SqlDataReader reader =
                await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                string status =
                    reader["Status"].ToString() ?? "";

                string priority =
                    dsPriority != null
                        ? reader["Priority"].ToString() ?? ""
                        : "";

                items.Add(
                    new RecentSupportItemViewModel
                    {
                        SupportDateText =
                            Convert.ToDateTime(
                                    reader["SupportDate"])
                                .ToString(
                                    "dd MMM yyyy",
                                    System.Globalization
                                        .CultureInfo
                                        .InvariantCulture),
                        ClientName =
                            reader["ClientName"].ToString() ?? "",
                        UserName =
                            reader["UserName"].ToString() ?? "",
                        SupportType =
                            reader["SupportType"].ToString() ?? "",
                        Subject =
                            reader["Subject"].ToString() ?? "",
                        Status = status,
                        StatusClass =
                            StatusPillClass(status),
                        Priority = priority,
                        PriorityClass =
                            PriorityPillClass(priority),
                        FollowUpDateText =
                            dsFollowUpDate == null
                                || reader["FollowUpDate"]
                                    == DBNull.Value
                                ? "—"
                                : Convert.ToDateTime(
                                        reader["FollowUpDate"])
                                    .ToString(
                                        "dd MMM yyyy",
                                        System.Globalization
                                            .CultureInfo
                                            .InvariantCulture)
                    });
            }
        }

        return items;
    }


    // Visits Tab (COMPONENT 11) —
    // Management/analytics view, not a CRUD duplicate. Uses only
    // existing ClientVisiting fields; Add/Edit stays in the
    // Sidebar > Client Visiting module. All counts behind the shared
    // DashboardFilter; one query returns the four KPIs conditionally.
    private static async Task<
        DashboardVisitsTabViewModel>
        LoadVisitsTabAsync(
            SqlConnection connection,
            DynamicTableService.SchemaColumns schema,
            DashboardViewModel viewModel)
    {
        var model =
            new DashboardVisitsTabViewModel();

        var today = DateTime.Today;

        var monthStart =
            new DateTime(today.Year, today.Month, 1);

        var monthEnd =
            monthStart.AddMonths(1);

        string? vVisitDate =
            Col(schema, "ClientVisiting", "VisitDate");

        string? vClientId =
            Col(schema, "ClientVisiting", "ClientId");

        string? vStatus =
            Col(schema, "ClientVisiting", "Status");

        string? cId =
            Col(schema, "ClientMaster", "Id");

        if (vVisitDate != null && cId != null
            && vClientId != null)
        {
            var wheres =
                new List<string>();

            using (var command = new SqlCommand())
            {
                command.Connection = connection;
                command.CommandTimeout = 0;

                command.Parameters.AddWithValue(
                    "@Today",
                    today);

                command.Parameters.AddWithValue(
                    "@MonthStart",
                    monthStart);

                command.Parameters.AddWithValue(
                    "@MonthEnd",
                    monthEnd);

                AddVisitFilters(
                    wheres,
                    command.Parameters,
                    viewModel,
                    schema,
                    vVisitDate);

                string whereSql =
                    wheres.Count > 0
                        ? " WHERE "
                            + string.Join(
                                " AND ",
                                wheres)
                        : "";

                string upcomingCondition =
                    vStatus != null
                        ? $"v.{vVisitDate} >= @Today"
                            + $" AND v.{vStatus}"
                            + " <> 'Cancelled'"
                        : $"v.{vVisitDate} >= @Today";

                command.CommandText = $@"
                    SELECT
                        COUNT(*) AS TotalVisits,
                        SUM(CASE WHEN CAST(v.{vVisitDate}
                            AS DATE) = @Today THEN 1 ELSE 0 END) AS TodayVisits,
                        SUM(CASE WHEN v.{vVisitDate}
                            >= @MonthStart
                            AND v.{vVisitDate}
                            < @MonthEnd THEN 1 ELSE 0 END) AS MonthVisits,
                        SUM(CASE WHEN {upcomingCondition}
                            THEN 1 ELSE 0 END) AS UpcomingVisits

                    FROM
                        [ClientVisiting] v

                    INNER JOIN
                        [ClientMaster] c
                        ON c.{cId}
                        = v.{vClientId}
                    {whereSql}";

                using SqlDataReader reader =
                    await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    model.TotalVisits =
                        Convert.ToInt32(reader["TotalVisits"]);

                    model.TodayVisits =
                        reader["TodayVisits"] == DBNull.Value
                            ? 0
                            : Convert.ToInt32(reader["TodayVisits"]);

                    model.ThisMonthVisits =
                        reader["MonthVisits"] == DBNull.Value
                            ? 0
                            : Convert.ToInt32(reader["MonthVisits"]);

                    model.UpcomingVisits =
                        reader["UpcomingVisits"] == DBNull.Value
                            ? 0
                            : Convert.ToInt32(reader["UpcomingVisits"]);
                }
            }
        }


        model.Recent =
            await LoadRecentVisitsAsync(
                connection,
                schema,
                viewModel);

        return model;
    }


    // Recent Visits (COMPONENT 11) —
    // Newest 6 ClientVisiting rows matching ALL Dashboard filters
    // (Date on VisitDate, State via ClientMaster, User via
    // ClientVisiting.UserId -> LoginUsers.Id, Client, Status).
    private static async Task<System.Collections.Generic.List<
        RecentVisitItemViewModel>>
        LoadRecentVisitsAsync(
            SqlConnection connection,
            DynamicTableService.SchemaColumns schema,
            DashboardViewModel viewModel)
    {
        var items =
            new System.Collections.Generic.List<
                RecentVisitItemViewModel>();

        string? vVisitDate =
            Col(schema, "ClientVisiting", "VisitDate");

        string? vUserId =
            Col(schema, "ClientVisiting", "UserId");

        string? vClientId =
            Col(schema, "ClientVisiting", "ClientId");

        string? vVisitType =
            Col(schema, "ClientVisiting", "VisitType");

        string? vSubject =
            Col(schema, "ClientVisiting", "Subject");

        string? vStatus =
            Col(schema, "ClientVisiting", "Status");

        string? vFollowUpDate =
            Col(schema, "ClientVisiting", "FollowUpDate");

        string? vId =
            Col(schema, "ClientVisiting", "Id");

        string? cId =
            Col(schema, "ClientMaster", "Id");

        string? cName =
            Col(schema, "ClientMaster", "ClientName");

        string? uId =
            Col(schema, "LoginUsers", "Id");

        string? uName =
            Col(schema, "LoginUsers", "UserName");

        // The list needs the core display columns + both
        // joins — otherwise it is skipped entirely.
        if (vVisitDate == null || vClientId == null
            || cId == null || cName == null
            || vUserId == null || uId == null
            || uName == null || vStatus == null)
        {
            return items;
        }

        string followUpTerm =
            vFollowUpDate != null
                ? $", v.{vFollowUpDate} AS FollowUpDate"
                : "";

        string visitTypeTerm =
            vVisitType != null
                ? $", v.{vVisitType} AS VisitType"
                : "";

        string subjectTerm =
            vSubject != null
                ? $", v.{vSubject} AS Subject"
                : "";

        string orderBy =
            vId != null
                ? $"v.{vVisitDate} DESC, v.{vId} DESC"
                : $"v.{vVisitDate} DESC";

        var wheres =
            new List<string>();

        using (var command = new SqlCommand())
        {
            command.Connection = connection;
            command.CommandTimeout = 0;

            AddVisitFilters(
                wheres,
                command.Parameters,
                viewModel,
                schema,
                vVisitDate);

            string whereSql =
                wheres.Count > 0
                    ? " WHERE "
                        + string.Join(
                            " AND ",
                            wheres)
                    : "";

            command.CommandText = $@"
                SELECT TOP 6
                    v.{vVisitDate}
                        AS VisitDate,
                    c.{cName}
                        AS ClientName,
                    u.{uName}
                        AS UserName,
                    v.{vStatus}
                        AS Status
                    {visitTypeTerm}
                    {subjectTerm}
                    {followUpTerm}

                FROM
                    [ClientVisiting] v

                INNER JOIN
                    [ClientMaster] c
                    ON c.{cId}
                    = v.{vClientId}

                INNER JOIN
                    [LoginUsers] u
                    ON u.{uId}
                    = v.{vUserId}
                {whereSql}

                ORDER BY
                    {orderBy}";

            using SqlDataReader reader =
                await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                string status =
                    reader["Status"].ToString() ?? "";

                items.Add(
                    new RecentVisitItemViewModel
                    {
                        VisitDateText =
                            Convert.ToDateTime(
                                    reader["VisitDate"])
                                .ToString(
                                    "dd MMM yyyy",
                                    System.Globalization
                                        .CultureInfo
                                        .InvariantCulture),
                        ClientName =
                            reader["ClientName"].ToString() ?? "",
                        UserName =
                            reader["UserName"].ToString() ?? "",
                        VisitType =
                            vVisitType != null
                                ? reader["VisitType"].ToString() ?? ""
                                : "",
                        Subject =
                            vSubject != null
                                ? reader["Subject"].ToString() ?? ""
                                : "",
                        Status = status,
                        StatusClass =
                            VisitPillClass(status),
                        FollowUpDateText =
                            vFollowUpDate == null
                                || reader["FollowUpDate"]
                                    == DBNull.Value
                                ? "—"
                                : Convert.ToDateTime(
                                        reader["FollowUpDate"])
                                    .ToString(
                                        "dd MMM yyyy",
                                        System.Globalization
                                            .CultureInfo
                                            .InvariantCulture)
                    });
            }
        }

        return items;
    }


    // Clients Tab (COMPONENT 12) —
    // Management/overview view, not a CRUD duplicate. Uses only existing
    // ClientMaster fields; Add/Edit stays in the Sidebar > Clients module.
    // All counts behind the shared DashboardFilter; one query returns the
    // four KPIs conditionally. The "Clients by State" panel is the
    // existing Component 7 one (reused in the Overview tab).
    private static async Task<
        DashboardClientsTabViewModel>
        LoadClientsTabAsync(
            SqlConnection connection,
            DynamicTableService.SchemaColumns schema,
            DashboardViewModel viewModel)
    {
        var model =
            new DashboardClientsTabViewModel();

        var today = DateTime.Today;

        var monthStart =
            new DateTime(today.Year, today.Month, 1);

        var monthEnd =
            monthStart.AddMonths(1);

        string? cIsActive =
            Col(schema, "ClientMaster", "IsActive");

        string? cEntryOn =
            Col(schema, "ClientMaster", "EntryOn");

        string? cId =
            Col(schema, "ClientMaster", "Id");

        if (cId != null)
        {
            var wheres =
                new List<string>();

            using (var command = new SqlCommand())
            {
                command.Connection = connection;
                command.CommandTimeout = 0;

                command.Parameters.AddWithValue(
                    "@MonthStart",
                    monthStart);

                command.Parameters.AddWithValue(
                    "@MonthEnd",
                    monthEnd);

                AddClientFilters(
                    wheres,
                    command.Parameters,
                    viewModel,
                    schema,
                    cEntryOn);

                string whereSql =
                    wheres.Count > 0
                        ? " WHERE "
                            + string.Join(
                                " AND ",
                                wheres)
                        : "";

                string activeTerm =
                    cIsActive != null
                        ? $"SUM(CASE WHEN c.{cIsActive}"
                            + " = 1 THEN 1 ELSE 0 END)"
                        : "0";

                string nonActiveTerm =
                    cIsActive != null
                        ? $"SUM(CASE WHEN c.{cIsActive}"
                            + " = 0 THEN 1 ELSE 0 END)"
                        : "0";

                string addedThisMonthTerm =
                    cEntryOn != null
                        ? $"SUM(CASE WHEN c.{cEntryOn}"
                            + " >= @MonthStart"
                            + $" AND c.{cEntryOn}"
                            + " < @MonthEnd THEN 1 ELSE 0 END)"
                        : "0";

                command.CommandText = $@"
                    SELECT
                        COUNT(*) AS TotalClients,
                        {activeTerm} AS ActiveClients,
                        {nonActiveTerm} AS NonActiveClients,
                        {addedThisMonthTerm} AS AddedThisMonth

                    FROM
                        [ClientMaster] c
                    {whereSql}";

                using SqlDataReader reader =
                    await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    model.TotalClients =
                        Convert.ToInt32(reader["TotalClients"]);

                    model.ActiveClients =
                        reader["ActiveClients"] == DBNull.Value
                            ? 0
                            : Convert.ToInt32(reader["ActiveClients"]);

                    model.NonActiveClients =
                        reader["NonActiveClients"] == DBNull.Value
                            ? 0
                            : Convert.ToInt32(reader["NonActiveClients"]);

                    model.AddedThisMonth =
                        reader["AddedThisMonth"] == DBNull.Value
                            ? 0
                            : Convert.ToInt32(reader["AddedThisMonth"]);
                }
            }
        }


        model.Overview =
            await LoadClientOverviewAsync(
                connection,
                schema,
                viewModel);

        model.ClientsByState =
            await LoadClientsByStateAsync(
                connection,
                schema,
                viewModel);

        return model;
    }


    // Clients Overview (COMPONENT 12) —
    // Newest 6 ClientMaster rows matching the shared DashboardFilter
    // (Date on EntryOn, State via c.StateId -> States.Id, User via
    // c.UserId -> LoginUsers.Id, Client via c.Id). Resolves StateName and
    // UserName through the existing ClientMaster -> States and
    // ClientMaster -> LoginUsers relationships (INNER JOIN like the module).
    private static async Task<System.Collections.Generic.List<
        RecentClientItemViewModel>>
        LoadClientOverviewAsync(
            SqlConnection connection,
            DynamicTableService.SchemaColumns schema,
            DashboardViewModel viewModel)
    {
        var items =
            new System.Collections.Generic.List<
                RecentClientItemViewModel>();

        string? cId =
            Col(schema, "ClientMaster", "Id");

        string? cName =
            Col(schema, "ClientMaster", "ClientName");

        string? cMobile =
            Col(schema, "ClientMaster", "MobileNo");

        string? cEmail =
            Col(schema, "ClientMaster", "Email");

        string? cEntryOn =
            Col(schema, "ClientMaster", "EntryOn");

        string? cIsActive =
            Col(schema, "ClientMaster", "IsActive");

        string? cStateId =
            Col(schema, "ClientMaster", "StateId");

        string? cUserId =
            Col(schema, "ClientMaster", "UserId");

        string? sId =
            Col(schema, "States", "Id");

        string? sName =
            Col(schema, "States", "StateName");

        string? uId =
            Col(schema, "LoginUsers", "Id");

        string? uName =
            Col(schema, "LoginUsers", "UserName");

        // Without the client identity + entry date the list
        // cannot be ordered — skip the whole panel.
        if (cId == null || cName == null || cEntryOn == null)
        {
            return items;
        }

        var joins =
            new List<string>();

        bool joinState =
            cStateId != null && sId != null && sName != null;

        if (joinState)
        {
            joins.Add(
                $"INNER JOIN [States] st "
                + $"ON st.{sId} = c.{cStateId}");
        }

        bool joinUser =
            cUserId != null && uId != null && uName != null;

        if (joinUser)
        {
            joins.Add(
                $"INNER JOIN [LoginUsers] lu "
                + $"ON lu.{uId} = c.{cUserId}");
        }

        string stateTerm =
            joinState
                ? $", st.{sName} AS StateName"
                : "";

        string userTerm =
            joinUser
                ? $", lu.{uName} AS UserName"
                : "";

        string mobileTerm =
            cMobile != null
                ? $", c.{cMobile} AS MobileNo"
                : "";

        string emailTerm =
            cEmail != null
                ? $", c.{cEmail} AS Email"
                : "";

        string isActiveTerm =
            cIsActive != null
                ? $", c.{cIsActive} AS IsActive"
                : "";

        var wheres =
            new List<string>();

        using (var command = new SqlCommand())
        {
            command.Connection = connection;
            command.CommandTimeout = 0;

            AddClientFilters(
                wheres,
                command.Parameters,
                viewModel,
                schema,
                cEntryOn);

            string whereSql =
                wheres.Count > 0
                    ? " WHERE "
                        + string.Join(
                            " AND ",
                            wheres)
                    : "";

            command.CommandText = $@"
                SELECT TOP 6
                    c.{cName}
                        AS ClientName
                    {mobileTerm}
                    {emailTerm}
                    {isActiveTerm}
                    {stateTerm}
                    {userTerm}
                    , c.{cEntryOn}
                        AS EntryOn

                FROM
                    [ClientMaster] c

                {string.Join("\n\n                ", joins)}
                {whereSql}

                ORDER BY
                    c.{cEntryOn} DESC,
                    c.{cId} DESC";

            using SqlDataReader reader =
                await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                bool isActive =
                    cIsActive == null
                        || reader["IsActive"] == DBNull.Value
                        ? false
                        : Convert.ToBoolean(reader["IsActive"]);

                items.Add(
                    new RecentClientItemViewModel
                    {
                        ClientName =
                            reader["ClientName"].ToString() ?? "",
                        StateName =
                            joinState
                                ? reader["StateName"].ToString() ?? ""
                                : "",
                        EntryDateText =
                            Convert.ToDateTime(
                                    reader["EntryOn"])
                                .ToString(
                                    "dd MMM yyyy",
                                    System.Globalization
                                        .CultureInfo
                                        .InvariantCulture),
                        UserName =
                            joinUser
                                ? reader["UserName"].ToString() ?? ""
                                : "",
                        IsActive = isActive,
                        StatusText =
                            isActive
                                ? "Active"
                                : "Non Active",
                        StatusClass =
                            isActive
                                ? "status-active"
                                : "status-inactive",
                        MobileNo =
                            cMobile != null
                                && !string.IsNullOrWhiteSpace(
                                    reader["MobileNo"]?.ToString())
                                ? reader["MobileNo"].ToString()!
                                : "—",
                        Email =
                            cEmail != null
                                && !string.IsNullOrWhiteSpace(
                                    reader["Email"]?.ToString())
                                ? reader["Email"].ToString()!
                                : "—"
                    });
            }
        }

        return items;
    }


    // Maps a ClientVisiting status to a .badge-* pill class.
    // Planned is visit-specific; Completed/Cancelled share the
    // existing palette.
    private static string VisitPillClass(
        string status)
    {
        switch (status)
        {
            case "Planned":
                return "badge-planned";

            case "Completed":
                return "badge-completed";

            case "Cancelled":
                return "badge-cancelled";

            default:
                return "badge-default";
        }
    }


    // Maps a DailySupport status to the existing .badge-* pill class.
    private static string StatusPillClass(
        string status)
    {
        switch (status)
        {
            case "Open":
                return "badge-open";

            case "In Progress":
                return "badge-inprogress";

            case "Pending":
                return "badge-pending";

            case "Completed":
                return "badge-completed";

            case "Cancelled":
                return "badge-cancelled";

            default:
                return "badge-default";
        }
    }


    // Maps a DailySupport priority to the .prio-* pill class
    // (same palette as the Component 6 priority bars).
    private static string PriorityPillClass(
        string priority)
    {
        switch (priority)
        {
            case "Low":
                return "prio-low";

            case "Medium":
                return "prio-medium";

            case "High":
                return "prio-high";

            case "Urgent":
                return "prio-urgent";

            default:
                return "prio-default";
        }
    }


    // Reusable "helper" methods -------------------------------------------------

    // Appends [column] >= @From AND [column] < @ToExclusive
    // when the quick date filter has a resolved range and the
    // column actually exists in the schema.
    private static void AddDateRange(
        List<string> wheres,
        SqlParameterCollection parameters,
        DashboardDateFilter dateFilter,
        string? dateColumn)
    {
        if (dateColumn is null
            || !dateFilter.FromDate.HasValue
            || !dateFilter.ToDateExclusive.HasValue)
        {
            return;
        }

        wheres.Add(
            $"{dateColumn} >= @From"
            + $" AND {dateColumn} < @ToExclusive");

        parameters.AddWithValue(
            "@From",
            dateFilter.FromDate.Value.Date);

        parameters.AddWithValue(
            "@ToExclusive",
            dateFilter.ToDateExclusive.Value);
    }


    // Appends sql to the WHERE list and registers its parameter,
    // but only when the filter is actually active.
    private static void AddIf(
        List<string> wheres,
        SqlParameterCollection parameters,
        bool active,
        string sql,
        string parameterName,
        object? value)
    {
        if (!active)
        {
            return;
        }

        wheres.Add(sql);
        parameters.AddWithValue(parameterName, value);
    }


    // Filters shared by every DailySupport card.
    // pinnedStatus pins the card to one Status value
    // (Pending / In Progress); null honours the global
    // Status dropdown instead.
    // Set applyStatusFilter = false when the caller IS the
    // status-distribution section (COMPONENT 5) so the
    // global Status dropdown is ignored.
    private static void AddDailySupportFilters(
        List<string> wheres,
        SqlParameterCollection parameters,
        DashboardViewModel viewModel,
        DynamicTableService.SchemaColumns schema,
        string? dateColumn,
        string? pinnedStatus = null,
        bool applyStatusFilter = true)
    {
        string? cStateId =
            Col(schema, "ClientMaster", "StateId");

        string? dsUserId =
            Col(schema, "DailySupport", "UserId");

        string? dsClientId =
            Col(schema, "DailySupport", "ClientId");

        string? dsStatus =
            Col(schema, "DailySupport", "Status");

        string? dsPriority =
            Col(schema, "DailySupport", "Priority");

        AddDateRange(
            wheres,
            parameters,
            viewModel.DateFilter,
            dateColumn);

        AddIf(
            wheres,
            parameters,
            viewModel.StateId.HasValue && cStateId != null,
            $"c.{cStateId} = @StateId",
            "@StateId",
            viewModel.StateId);

        AddIf(
            wheres,
            parameters,
            viewModel.UserId.HasValue && dsUserId != null,
            $"ds.{dsUserId} = @UserId",
            "@UserId",
            viewModel.UserId);

        AddIf(
            wheres,
            parameters,
            viewModel.ClientId.HasValue && dsClientId != null,
            $"ds.{dsClientId} = @ClientId",
            "@ClientId",
            viewModel.ClientId);

        string status =
            pinnedStatus
            ?? (applyStatusFilter
                ? viewModel.Status
                : "All");

        AddIf(
            wheres,
            parameters,
            status != "All" && dsStatus != null,
            $"ds.{dsStatus} = @Status",
            "@Status",
            status);

        AddIf(
            wheres,
            parameters,
            viewModel.Priority != "All" && dsPriority != null,
            $"ds.{dsPriority} = @Priority",
            "@Priority",
            viewModel.Priority);
    }


    // Appends EntryOn / State / User / Client filter fragments for
    // ClientMaster queries behind the shared DashboardFilter.
    private static void AddClientFilters(
        List<string> wheres,
        SqlParameterCollection parameters,
        DashboardViewModel viewModel,
        DynamicTableService.SchemaColumns schema,
        string? dateColumn)
    {
        string? cStateId =
            Col(schema, "ClientMaster", "StateId");

        string? cUserId =
            Col(schema, "ClientMaster", "UserId");

        string? cId =
            Col(schema, "ClientMaster", "Id");

        AddDateRange(
            wheres,
            parameters,
            viewModel.DateFilter,
            dateColumn);

        AddIf(
            wheres,
            parameters,
            viewModel.StateId.HasValue && cStateId != null,
            $"c.{cStateId} = @StateId",
            "@StateId",
            viewModel.StateId);

        AddIf(
            wheres,
            parameters,
            viewModel.UserId.HasValue && cUserId != null,
            $"c.{cUserId} = @UserId",
            "@UserId",
            viewModel.UserId);

        AddIf(
            wheres,
            parameters,
            viewModel.ClientId.HasValue && cId != null,
            $"c.{cId} = @ClientId",
            "@ClientId",
            viewModel.ClientId);
    }


    // Appends VisitDate / State / User / Client / Status
    // filter fragments for ClientVisiting queries behind the
    // shared DashboardFilter.
    private static void AddVisitFilters(
        List<string> wheres,
        SqlParameterCollection parameters,
        DashboardViewModel viewModel,
        DynamicTableService.SchemaColumns schema,
        string? dateColumn)
    {
        string? cStateId =
            Col(schema, "ClientMaster", "StateId");

        string? vUserId =
            Col(schema, "ClientVisiting", "UserId");

        string? vClientId =
            Col(schema, "ClientVisiting", "ClientId");

        string? vStatus =
            Col(schema, "ClientVisiting", "Status");

        AddDateRange(
            wheres,
            parameters,
            viewModel.DateFilter,
            dateColumn);

        AddIf(
            wheres,
            parameters,
            viewModel.StateId.HasValue && cStateId != null,
            $"c.{cStateId} = @StateId",
            "@StateId",
            viewModel.StateId);

        AddIf(
            wheres,
            parameters,
            viewModel.UserId.HasValue && vUserId != null,
            $"v.{vUserId} = @UserId",
            "@UserId",
            viewModel.UserId);

        AddIf(
            wheres,
            parameters,
            viewModel.ClientId.HasValue && vClientId != null,
            $"v.{vClientId} = @ClientId",
            "@ClientId",
            viewModel.ClientId);

        AddIf(
            wheres,
            parameters,
            viewModel.Status != "All" && vStatus != null,
            $"v.{vStatus} = @Status",
            "@Status",
            viewModel.Status);
    }


    private static async Task<
        List<LookupOptionViewModel>>
        LoadStatesAsync(
            SqlConnection connection,
            DynamicTableService.SchemaColumns schema)
    {
        string? sId =
            Col(schema, "States", "Id");

        string? sName =
            Col(schema, "States", "StateName");

        string? sIsActive =
            Col(schema, "States", "IsActive");

        if (sId == null || sName == null)
        {
            return new List<LookupOptionViewModel>();
        }

        string isActiveWhere =
            sIsActive != null
                ? $"WHERE {sIsActive} = 1"
                : "";

        string query = $@"
            SELECT
                {sId},
                {sName}

            FROM
                [States]

            {isActiveWhere}

            ORDER BY
                {sName}";

        using SqlCommand command =
            new SqlCommand(query, connection);
            command.CommandTimeout = 0;

        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        var items =
            new List<LookupOptionViewModel>();

        while (await reader.ReadAsync())
        {
            items.Add(new LookupOptionViewModel
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1)
            });
        }

        return items;
    }


    private static async Task<
        List<LookupOptionViewModel>>
        LoadUsersAsync(
            SqlConnection connection,
            DynamicTableService.SchemaColumns schema)
    {
        string? uId =
            Col(schema, "LoginUsers", "Id");

        string? uName =
            Col(schema, "LoginUsers", "UserName");

        string? uIsActive =
            Col(schema, "LoginUsers", "IsActive");

        if (uId == null || uName == null)
        {
            return new List<LookupOptionViewModel>();
        }

        string isActiveWhere =
            uIsActive != null
                ? $"WHERE {uIsActive} = 1"
                : "";

        string query = $@"
            SELECT
                {uId},
                {uName}

            FROM
                [LoginUsers]

            {isActiveWhere}

            ORDER BY
                {uName}";

        using SqlCommand command =
            new SqlCommand(query, connection);
            command.CommandTimeout = 0;

        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        var items =
            new List<LookupOptionViewModel>();

        while (await reader.ReadAsync())
        {
            items.Add(new LookupOptionViewModel
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1)
            });
        }

        return items;
    }


    private static async Task<
        List<LookupOptionViewModel>>
        LoadClientsAsync(
            SqlConnection connection,
            DynamicTableService.SchemaColumns schema)
    {
        string? cId =
            Col(schema, "ClientMaster", "Id");

        string? cName =
            Col(schema, "ClientMaster", "ClientName");

        string? cIsActive =
            Col(schema, "ClientMaster", "IsActive");

        if (cId == null || cName == null)
        {
            return new List<LookupOptionViewModel>();
        }

        string isActiveWhere =
            cIsActive != null
                ? $"WHERE {cIsActive} = 1"
                : "";

        string query = $@"
            SELECT
                {cId},
                {cName}

            FROM
                [ClientMaster]

            {isActiveWhere}

            ORDER BY
                {cName}";

        using SqlCommand command =
            new SqlCommand(query, connection);
            command.CommandTimeout = 0;

        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        var items =
            new List<LookupOptionViewModel>();

        while (await reader.ReadAsync())
        {
            items.Add(new LookupOptionViewModel
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1)
            });
        }

        return items;
    }
}