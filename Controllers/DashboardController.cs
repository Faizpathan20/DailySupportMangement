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

    public DashboardController(
        IConfiguration configuration)
    {
        _configuration = configuration;
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

            viewModel.States =
                await LoadStatesAsync(connection);

            viewModel.Users =
                await LoadUsersAsync(connection);

            viewModel.Clients =
                await LoadClientsAsync(connection);

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
                    viewModel,
                    LoadSummaryAsync);

            var statusTask =
                LoadWithNewConnectionAsync(
                    connectionString,
                    viewModel,
                    LoadSupportStatusAsync);

            var priorityTask =
                LoadWithNewConnectionAsync(
                    connectionString,
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
                    viewModel,
                    LoadClientsByStateAsync);

            var monthlyTask =
                LoadWithNewConnectionAsync(
                    connectionString,
                    viewModel,
                    LoadMonthlyActivityAsync);

            var upcomingTask =
                LoadWithNewConnectionAsync(
                    connectionString,
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
                    viewModel,
                    LoadSupportTabAsync);

            var visitsTabTask =
                LoadWithNewConnectionAsync(
                    connectionString,
                    viewModel,
                    LoadVisitsTabAsync);

            var clientsTabTask =
                LoadWithNewConnectionAsync(
                    connectionString,
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
            DashboardViewModel viewModel,
            Func<
                SqlConnection,
                DashboardViewModel,
                Task<T>> loader)
    {
        await using var connection =
            new SqlConnection(connectionString);

        await connection.OpenAsync();

        return await loader(
            connection,
            viewModel);
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
            DashboardViewModel viewModel)
    {
        var summary =
            new DashboardSummaryViewModel();

        summary.TotalUsers =
            await CountUsersAsync(connection);

        summary.TotalClients =
            await CountClientsAsync(
                connection,
                viewModel);

        summary.TotalVisits =
            await CountVisitsAsync(
                connection,
                viewModel);

        summary.ActiveStates =
            await CountActiveStatesAsync(
                connection,
                viewModel);

        await LoadDailySupportSummaryAsync(
            connection,
            viewModel,
            summary);

        return summary;
    }


    private static async Task<int> CountUsersAsync(
        SqlConnection connection)
    {
        string query = $@"
            SELECT
                COUNT(*)

            FROM
                {DatabaseMapping.LoginUsers.Table}";

        using SqlCommand command =
            new SqlCommand(query, connection);
            command.CommandTimeout = 0;

        return Convert.ToInt32(
            await command.ExecuteScalarAsync());
    }


    private static async Task<int> CountClientsAsync(
        SqlConnection connection,
        DashboardViewModel viewModel)
    {
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
                $"c.{DatabaseMapping.ClientMaster.EntryOn}");

            AddIf(
                wheres,
                command.Parameters,
                viewModel.StateId.HasValue,
                "c.StateId = @StateId",
                "@StateId",
                viewModel.StateId);

            AddIf(
                wheres,
                command.Parameters,
                viewModel.UserId.HasValue,
                "c.UserId = @UserId",
                "@UserId",
                viewModel.UserId);

            AddIf(
                wheres,
                command.Parameters,
                viewModel.ClientId.HasValue,
                "c.Id = @ClientId",
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
                    {DatabaseMapping.ClientMaster.Table} c
                {whereSql}";

            return Convert.ToInt32(
                await command.ExecuteScalarAsync());
        }
    }


    private static async Task<int> CountVisitsAsync(
        SqlConnection connection,
        DashboardViewModel viewModel)
    {
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
                $"v.{DatabaseMapping.ClientVisiting.VisitDate}");

            AddIf(
                wheres,
                command.Parameters,
                viewModel.StateId.HasValue,
                "c.StateId = @StateId",
                "@StateId",
                viewModel.StateId);

            AddIf(
                wheres,
                command.Parameters,
                viewModel.UserId.HasValue,
                "v.UserId = @UserId",
                "@UserId",
                viewModel.UserId);

            AddIf(
                wheres,
                command.Parameters,
                viewModel.ClientId.HasValue,
                "v.ClientId = @ClientId",
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
                    {DatabaseMapping.ClientVisiting.Table} v

                INNER JOIN
                    {DatabaseMapping.ClientMaster.Table} c
                    ON c.{DatabaseMapping.ClientMaster.Id}
                    = v.{DatabaseMapping.ClientVisiting.ClientId}
                {whereSql}";

            return Convert.ToInt32(
                await command.ExecuteScalarAsync());
        }
    }


    private static async Task<int> CountActiveStatesAsync(
        SqlConnection connection,
        DashboardViewModel viewModel)
    {
        var wheres =
            new List<string>
            {
                $"s.{DatabaseMapping.States.IsActive} = 1"
            };

        using (var command = new SqlCommand())
        {
            command.Connection = connection;
            command.CommandTimeout = 0;

            AddDateRange(
                wheres,
                command.Parameters,
                viewModel.DateFilter,
                $"s.{DatabaseMapping.States.EntryOn}");

            AddIf(
                wheres,
                command.Parameters,
                viewModel.StateId.HasValue,
                $"s.{DatabaseMapping.States.Id} = @StateId",
                "@StateId",
                viewModel.StateId);

            AddIf(
                wheres,
                command.Parameters,
                viewModel.UserId.HasValue,
                $"s.{DatabaseMapping.States.UserId} = @UserId",
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
                    {DatabaseMapping.States.Table} s
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
            DashboardViewModel viewModel,
            DashboardSummaryViewModel summary)
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
                $"ds.{DatabaseMapping.DailySupport.SupportDate}");

            string whereSql =
                wheres.Count > 0
                    ? " WHERE "
                        + string.Join(
                            " AND ",
                            wheres)
                    : "";

            command.CommandText = $@"
                SELECT
                    COUNT(*) AS TotalSupport,
                    SUM(CASE WHEN ds.{DatabaseMapping.DailySupport.Status}
                        = 'Pending' THEN 1 ELSE 0 END) AS PendingCount,
                    SUM(CASE WHEN ds.{DatabaseMapping.DailySupport.Status}
                        = 'In Progress' THEN 1 ELSE 0 END) AS InProgressCount

                FROM
                    {DatabaseMapping.DailySupport.Table} ds

                INNER JOIN
                    {DatabaseMapping.ClientMaster.Table} c
                    ON c.{DatabaseMapping.ClientMaster.Id}
                    = ds.{DatabaseMapping.DailySupport.ClientId}
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


        // ---- Follow-up: upcoming FollowUpDate, not Cancelled ----
        var followWheres =
            new List<string>
            {
                $"ds.{DatabaseMapping.DailySupport.FollowUpDate}"
                    + " >= @Today",
                $"ds.{DatabaseMapping.DailySupport.Status}"
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
                $"ds.{DatabaseMapping.DailySupport.FollowUpDate}");

            command.CommandText = $@"
                SELECT
                    COUNT(*)

                FROM
                    {DatabaseMapping.DailySupport.Table} ds

                INNER JOIN
                    {DatabaseMapping.ClientMaster.Table} c
                    ON c.{DatabaseMapping.ClientMaster.Id}
                    = ds.{DatabaseMapping.DailySupport.ClientId}

                WHERE
                    {string.Join(" AND ", followWheres)}";

            summary.FollowUpCount =
                Convert.ToInt32(
                    await command.ExecuteScalarAsync());
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
            DashboardViewModel viewModel)
    {
        var status =
            new DashboardSupportStatusViewModel();

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
                $"ds.{DatabaseMapping.DailySupport.SupportDate}",
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
                    SUM(CASE WHEN ds.{DatabaseMapping.DailySupport.Status}
                        = 'Open' THEN 1 ELSE 0 END) AS OpenCount,
                    SUM(CASE WHEN ds.{DatabaseMapping.DailySupport.Status}
                        = 'In Progress' THEN 1 ELSE 0 END) AS InProgressCount,
                    SUM(CASE WHEN ds.{DatabaseMapping.DailySupport.Status}
                        = 'Pending' THEN 1 ELSE 0 END) AS PendingCount,
                    SUM(CASE WHEN ds.{DatabaseMapping.DailySupport.Status}
                        = 'Completed' THEN 1 ELSE 0 END) AS CompletedCount,
                    SUM(CASE WHEN ds.{DatabaseMapping.DailySupport.Status}
                        = 'Cancelled' THEN 1 ELSE 0 END) AS CancelledCount

                FROM
                    {DatabaseMapping.DailySupport.Table} ds

                INNER JOIN
                    {DatabaseMapping.ClientMaster.Table} c
                    ON c.{DatabaseMapping.ClientMaster.Id}
                    = ds.{DatabaseMapping.DailySupport.ClientId}
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
            DashboardViewModel viewModel)
    {
        var priority =
            new DashboardSupportPriorityViewModel();

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
                $"ds.{DatabaseMapping.DailySupport.SupportDate}");

            string whereSql =
                wheres.Count > 0
                    ? " WHERE "
                        + string.Join(
                            " AND ",
                            wheres)
                    : "";

            command.CommandText = $@"
                SELECT
                    SUM(CASE WHEN ds.{DatabaseMapping.DailySupport.Priority}
                        = 'Low' THEN 1 ELSE 0 END) AS LowCount,
                    SUM(CASE WHEN ds.{DatabaseMapping.DailySupport.Priority}
                        = 'Medium' THEN 1 ELSE 0 END) AS MediumCount,
                    SUM(CASE WHEN ds.{DatabaseMapping.DailySupport.Priority}
                        = 'High' THEN 1 ELSE 0 END) AS HighCount,
                    SUM(CASE WHEN ds.{DatabaseMapping.DailySupport.Priority}
                        = 'Urgent' THEN 1 ELSE 0 END) AS UrgentCount

                FROM
                    {DatabaseMapping.DailySupport.Table} ds

                INNER JOIN
                    {DatabaseMapping.ClientMaster.Table} c
                    ON c.{DatabaseMapping.ClientMaster.Id}
                    = ds.{DatabaseMapping.DailySupport.ClientId}
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
            DashboardViewModel viewModel)
    {
        var model =
            new DashboardClientsByStateViewModel();

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
                $"c.{DatabaseMapping.ClientMaster.EntryOn}");

            AddIf(
                wheres,
                command.Parameters,
                viewModel.StateId.HasValue,
                "c.StateId = @StateId",
                "@StateId",
                viewModel.StateId);

            AddIf(
                wheres,
                command.Parameters,
                viewModel.UserId.HasValue,
                "c.UserId = @UserId",
                "@UserId",
                viewModel.UserId);

            AddIf(
                wheres,
                command.Parameters,
                viewModel.ClientId.HasValue,
                "c.Id = @ClientId",
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
                    s.{DatabaseMapping.States.StateName},
                    COUNT(c.{DatabaseMapping.ClientMaster.Id})
                        AS ClientCount

                FROM
                    {DatabaseMapping.States.Table} s

                INNER JOIN
                    {DatabaseMapping.ClientMaster.Table} c
                    ON c.{DatabaseMapping.ClientMaster.StateId}
                    = s.{DatabaseMapping.States.Id}
                {whereSql}

                GROUP BY
                    s.{DatabaseMapping.States.Id},
                    s.{DatabaseMapping.States.StateName}

                ORDER BY
                    ClientCount DESC,
                    s.{DatabaseMapping.States.StateName} ASC";

            using SqlDataReader reader =
                await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                model.States.Add(
                    new ClientStateCountViewModel
                    {
                        StateName =
                            reader[
                                DatabaseMapping.States.StateName]
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
            viewModel,
            startMonth,
            monthAfterEnd);

    var visitingCounts =
        await LoadMonthlyVisitingCountsAsync(
            connection,
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
        DashboardViewModel viewModel,
        DateTime rangeStart,
        DateTime rangeEnd)
{
    var counts =
        new Dictionary<int, int>();

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
            $"ds.{DatabaseMapping.DailySupport.SupportDate}"
            + " >= @RangeStart"
            + $" AND ds.{DatabaseMapping.DailySupport.SupportDate}"
            + " < @RangeEnd");

        AddIf(
            wheres,
            command.Parameters,
            viewModel.StateId.HasValue,
            "c.StateId = @StateId",
            "@StateId",
            viewModel.StateId);

        AddIf(
            wheres,
            command.Parameters,
            viewModel.UserId.HasValue,
            "ds.UserId = @UserId",
            "@UserId",
            viewModel.UserId);

        AddIf(
            wheres,
            command.Parameters,
            viewModel.ClientId.HasValue,
            "ds.ClientId = @ClientId",
            "@ClientId",
            viewModel.ClientId);

        AddIf(
            wheres,
            command.Parameters,
            viewModel.Status != "All",
            "ds.Status = @Status",
            "@Status",
            viewModel.Status);

        AddIf(
            wheres,
            command.Parameters,
            viewModel.Priority != "All",
            "ds.Priority = @Priority",
            "@Priority",
            viewModel.Priority);

        string whereSql =
            " WHERE "
            + string.Join(
                " AND ",
                wheres);

        command.CommandText = $@"
            SELECT
                YEAR(ds.{DatabaseMapping.DailySupport.SupportDate})
                    AS ActivityYear,
                MONTH(ds.{DatabaseMapping.DailySupport.SupportDate})
                    AS ActivityMonth,
                COUNT(*) AS ActivityCount

            FROM
                {DatabaseMapping.DailySupport.Table} ds

            INNER JOIN
                {DatabaseMapping.ClientMaster.Table} c
                ON c.{DatabaseMapping.ClientMaster.Id}
                = ds.{DatabaseMapping.DailySupport.ClientId}
            {whereSql}

            GROUP BY
                YEAR(ds.{DatabaseMapping.DailySupport.SupportDate}),
                MONTH(ds.{DatabaseMapping.DailySupport.SupportDate})";

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
        DashboardViewModel viewModel,
        DateTime rangeStart,
        DateTime rangeEnd)
{
    var counts =
        new Dictionary<int, int>();

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
            $"v.{DatabaseMapping.ClientVisiting.VisitDate}"
            + " >= @RangeStart"
            + $" AND v.{DatabaseMapping.ClientVisiting.VisitDate}"
            + " < @RangeEnd");

        AddIf(
            wheres,
            command.Parameters,
            viewModel.StateId.HasValue,
            "c.StateId = @StateId",
            "@StateId",
            viewModel.StateId);

        AddIf(
            wheres,
            command.Parameters,
            viewModel.UserId.HasValue,
            "v.UserId = @UserId",
            "@UserId",
            viewModel.UserId);

        AddIf(
            wheres,
            command.Parameters,
            viewModel.ClientId.HasValue,
            "v.ClientId = @ClientId",
            "@ClientId",
            viewModel.ClientId);

        string whereSql =
            " WHERE "
            + string.Join(
                " AND ",
                wheres);

        command.CommandText = $@"
            SELECT
                YEAR(v.{DatabaseMapping.ClientVisiting.VisitDate})
                    AS ActivityYear,
                MONTH(v.{DatabaseMapping.ClientVisiting.VisitDate})
                    AS ActivityMonth,
                COUNT(*) AS ActivityCount

            FROM
                {DatabaseMapping.ClientVisiting.Table} v

            INNER JOIN
                {DatabaseMapping.ClientMaster.Table} c
                ON c.{DatabaseMapping.ClientMaster.Id}
                = v.{DatabaseMapping.ClientVisiting.ClientId}
            {whereSql}

            GROUP BY
                YEAR(v.{DatabaseMapping.ClientVisiting.VisitDate}),
                MONTH(v.{DatabaseMapping.ClientVisiting.VisitDate})";

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
        DashboardViewModel viewModel)
{
    var model =
        new DashboardUpcomingFollowUpsViewModel();

    var today = DateTime.Today;


    // ---- Total upcoming count ----
    var countWheres =
        new List<string>
        {
            $"ds.{DatabaseMapping.DailySupport.FollowUpDate}"
                + " >= @Today",
            $"ds.{DatabaseMapping.DailySupport.Status}"
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
            $"ds.{DatabaseMapping.DailySupport.FollowUpDate}");

        command.CommandText = $@"
            SELECT
                COUNT(*)

            FROM
                {DatabaseMapping.DailySupport.Table} ds

            INNER JOIN
                {DatabaseMapping.ClientMaster.Table} c
                ON c.{DatabaseMapping.ClientMaster.Id}
                = ds.{DatabaseMapping.DailySupport.ClientId}

            WHERE
                {string.Join(" AND ", countWheres)}";

        model.TotalUpcoming =
            Convert.ToInt32(
                await command.ExecuteScalarAsync());
    }


    // ---- Next 6 upcoming rows ----
    var listWheres =
        new List<string>
        {
            $"ds.{DatabaseMapping.DailySupport.FollowUpDate}"
                + " >= @Today",
            $"ds.{DatabaseMapping.DailySupport.Status}"
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
            $"ds.{DatabaseMapping.DailySupport.FollowUpDate}");

        command.CommandText = $@"
            SELECT TOP 6
                c.{DatabaseMapping.ClientMaster.ClientName}
                    AS ClientName,
                ds.{DatabaseMapping.DailySupport.FollowUpDate}
                    AS FollowUpDate,
                ds.{DatabaseMapping.DailySupport.SupportType}
                    AS SupportType,
                ds.{DatabaseMapping.DailySupport.Subject}
                    AS Subject,
                ds.{DatabaseMapping.DailySupport.Status}
                    AS Status,
                u.{DatabaseMapping.LoginUsers.UserName}
                    AS UserName

            FROM
                {DatabaseMapping.DailySupport.Table} ds

            INNER JOIN
                {DatabaseMapping.ClientMaster.Table} c
                ON c.{DatabaseMapping.ClientMaster.Id}
                = ds.{DatabaseMapping.DailySupport.ClientId}

            INNER JOIN
                {DatabaseMapping.LoginUsers.Table} u
                ON u.{DatabaseMapping.LoginUsers.Id}
                = ds.{DatabaseMapping.DailySupport.UserId}

            WHERE
                {string.Join(" AND ", listWheres)}

            ORDER BY
                ds.{DatabaseMapping.DailySupport.FollowUpDate} ASC";

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
            DashboardViewModel viewModel)
    {
        var model =
            new DashboardSupportTabViewModel();

        model.Status =
            await LoadSupportStatusAsync(
                connection,
                viewModel);

        model.Priority =
            await LoadSupportPriorityAsync(
                connection,
                viewModel);

        model.Recent =
            await LoadRecentSupportsAsync(
                connection,
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
            DashboardViewModel viewModel)
    {
        var items =
            new System.Collections.Generic.List<
                RecentSupportItemViewModel>();

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
                $"ds.{DatabaseMapping.DailySupport.SupportDate}");

            string whereSql =
                wheres.Count > 0
                    ? " WHERE "
                        + string.Join(
                            " AND ",
                            wheres)
                    : "";

            command.CommandText = $@"
                SELECT TOP 6
                    ds.{DatabaseMapping.DailySupport.SupportDate}
                        AS SupportDate,
                    c.{DatabaseMapping.ClientMaster.ClientName}
                        AS ClientName,
                    u.{DatabaseMapping.LoginUsers.UserName}
                        AS UserName,
                    ds.{DatabaseMapping.DailySupport.SupportType}
                        AS SupportType,
                    ds.{DatabaseMapping.DailySupport.Subject}
                        AS Subject,
                    ds.{DatabaseMapping.DailySupport.Status}
                        AS Status,
                    ds.{DatabaseMapping.DailySupport.Priority}
                        AS Priority,
                    ds.{DatabaseMapping.DailySupport.FollowUpDate}
                        AS FollowUpDate

                FROM
                    {DatabaseMapping.DailySupport.Table} ds

                INNER JOIN
                    {DatabaseMapping.ClientMaster.Table} c
                    ON c.{DatabaseMapping.ClientMaster.Id}
                    = ds.{DatabaseMapping.DailySupport.ClientId}

                INNER JOIN
                    {DatabaseMapping.LoginUsers.Table} u
                    ON u.{DatabaseMapping.LoginUsers.Id}
                    = ds.{DatabaseMapping.DailySupport.UserId}
                {whereSql}

                ORDER BY
                    ds.{DatabaseMapping.DailySupport.SupportDate} DESC,
                    ds.{DatabaseMapping.DailySupport.Id} DESC";

            using SqlDataReader reader =
                await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                string status =
                    reader["Status"].ToString() ?? "";

                string priority =
                    reader["Priority"].ToString() ?? "";

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
                            reader["FollowUpDate"] == DBNull.Value
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
        DashboardViewModel viewModel)
{
    var model =
        new DashboardVisitsTabViewModel();

    var today = DateTime.Today;

    var monthStart =
        new DateTime(today.Year, today.Month, 1);

    var monthEnd =
        monthStart.AddMonths(1);


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
            $"v.{DatabaseMapping.ClientVisiting.VisitDate}");

        string whereSql =
            wheres.Count > 0
                ? " WHERE "
                    + string.Join(
                        " AND ",
                        wheres)
                : "";

        command.CommandText = $@"
            SELECT
                COUNT(*) AS TotalVisits,
                SUM(CASE WHEN CAST(v.{DatabaseMapping.ClientVisiting.VisitDate}
                    AS DATE) = @Today THEN 1 ELSE 0 END) AS TodayVisits,
                SUM(CASE WHEN v.{DatabaseMapping.ClientVisiting.VisitDate}
                    >= @MonthStart
                    AND v.{DatabaseMapping.ClientVisiting.VisitDate}
                    < @MonthEnd THEN 1 ELSE 0 END) AS MonthVisits,
                SUM(CASE WHEN v.{DatabaseMapping.ClientVisiting.VisitDate}
                    >= @Today
                    AND v.{DatabaseMapping.ClientVisiting.Status}
                    <> 'Cancelled' THEN 1 ELSE 0 END) AS UpcomingVisits

            FROM
                {DatabaseMapping.ClientVisiting.Table} v

            INNER JOIN
                {DatabaseMapping.ClientMaster.Table} c
                ON c.{DatabaseMapping.ClientMaster.Id}
                = v.{DatabaseMapping.ClientVisiting.ClientId}
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


    model.Recent =
        await LoadRecentVisitsAsync(
            connection,
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
        DashboardViewModel viewModel)
{
    var items =
        new System.Collections.Generic.List<
            RecentVisitItemViewModel>();

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
            $"v.{DatabaseMapping.ClientVisiting.VisitDate}");

        string whereSql =
            wheres.Count > 0
                ? " WHERE "
                    + string.Join(
                        " AND ",
                        wheres)
                : "";

        command.CommandText = $@"
            SELECT TOP 6
                v.{DatabaseMapping.ClientVisiting.VisitDate}
                    AS VisitDate,
                c.{DatabaseMapping.ClientMaster.ClientName}
                    AS ClientName,
                u.{DatabaseMapping.LoginUsers.UserName}
                    AS UserName,
                v.{DatabaseMapping.ClientVisiting.VisitType}
                    AS VisitType,
                v.{DatabaseMapping.ClientVisiting.Subject}
                    AS Subject,
                v.{DatabaseMapping.ClientVisiting.Status}
                    AS Status,
                v.{DatabaseMapping.ClientVisiting.FollowUpDate}
                    AS FollowUpDate

            FROM
                {DatabaseMapping.ClientVisiting.Table} v

            INNER JOIN
                {DatabaseMapping.ClientMaster.Table} c
                ON c.{DatabaseMapping.ClientMaster.Id}
                = v.{DatabaseMapping.ClientVisiting.ClientId}

            INNER JOIN
                {DatabaseMapping.LoginUsers.Table} u
                ON u.{DatabaseMapping.LoginUsers.Id}
                = v.{DatabaseMapping.ClientVisiting.UserId}
            {whereSql}

            ORDER BY
                v.{DatabaseMapping.ClientVisiting.VisitDate} DESC,
                v.{DatabaseMapping.ClientVisiting.Id} DESC";

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
                        reader["VisitType"].ToString() ?? "",
                    Subject =
                        reader["Subject"].ToString() ?? "",
                    Status = status,
                    StatusClass =
                        VisitPillClass(status),
                    FollowUpDateText =
                        reader["FollowUpDate"] == DBNull.Value
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
        DashboardViewModel viewModel)
{
    var model =
        new DashboardClientsTabViewModel();

    var today = DateTime.Today;

    var monthStart =
        new DateTime(today.Year, today.Month, 1);

    var monthEnd =
        monthStart.AddMonths(1);


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
            $"c.{DatabaseMapping.ClientMaster.EntryOn}");

        string whereSql =
            wheres.Count > 0
                ? " WHERE "
                    + string.Join(
                        " AND ",
                        wheres)
                : "";

        command.CommandText = $@"
            SELECT
                COUNT(*) AS TotalClients,
                SUM(CASE WHEN c.{DatabaseMapping.ClientMaster.IsActive}
                    = 1 THEN 1 ELSE 0 END) AS ActiveClients,
                SUM(CASE WHEN c.{DatabaseMapping.ClientMaster.IsActive}
                    = 0 THEN 1 ELSE 0 END) AS NonActiveClients,
                SUM(CASE WHEN c.{DatabaseMapping.ClientMaster.EntryOn}
                    >= @MonthStart
                    AND c.{DatabaseMapping.ClientMaster.EntryOn}
                    < @MonthEnd THEN 1 ELSE 0 END) AS AddedThisMonth

            FROM
                {DatabaseMapping.ClientMaster.Table} c
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


    model.Overview =
        await LoadClientOverviewAsync(
            connection,
            viewModel);

    model.ClientsByState =
        await LoadClientsByStateAsync(
            connection,
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
        DashboardViewModel viewModel)
{
    var items =
        new System.Collections.Generic.List<
            RecentClientItemViewModel>();

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
            $"c.{DatabaseMapping.ClientMaster.EntryOn}");

        string whereSql =
            wheres.Count > 0
                ? " WHERE "
                    + string.Join(
                        " AND ",
                        wheres)
                : "";

        command.CommandText = $@"
            SELECT TOP 6
                c.{DatabaseMapping.ClientMaster.ClientName}
                    AS ClientName,
                c.{DatabaseMapping.ClientMaster.MobileNo}
                    AS MobileNo,
                c.{DatabaseMapping.ClientMaster.Email}
                    AS Email,
                c.{DatabaseMapping.ClientMaster.EntryOn}
                    AS EntryOn,
                c.{DatabaseMapping.ClientMaster.IsActive}
                    AS IsActive,
                st.{DatabaseMapping.States.StateName}
                    AS StateName,
                lu.{DatabaseMapping.LoginUsers.UserName}
                    AS UserName

            FROM
                {DatabaseMapping.ClientMaster.Table} c

            INNER JOIN
                {DatabaseMapping.States.Table} st
                ON st.{DatabaseMapping.States.Id}
                = c.{DatabaseMapping.ClientMaster.StateId}

            INNER JOIN
                {DatabaseMapping.LoginUsers.Table} lu
                ON lu.{DatabaseMapping.LoginUsers.Id}
                = c.{DatabaseMapping.ClientMaster.UserId}
            {whereSql}

            ORDER BY
                c.{DatabaseMapping.ClientMaster.EntryOn} DESC,
                c.{DatabaseMapping.ClientMaster.Id} DESC";

        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            bool isActive =
                Convert.ToBoolean(reader["IsActive"]);

            items.Add(
                new RecentClientItemViewModel
                {
                    ClientName =
                        reader["ClientName"].ToString() ?? "",
                    StateName =
                        reader["StateName"].ToString() ?? "",
                    EntryDateText =
                        Convert.ToDateTime(
                                reader["EntryOn"])
                            .ToString(
                                "dd MMM yyyy",
                                System.Globalization
                                    .CultureInfo
                                    .InvariantCulture),
                    UserName =
                        reader["UserName"].ToString() ?? "",
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
                        string.IsNullOrWhiteSpace(
                            reader["MobileNo"]?.ToString())
                            ? "—"
                            : reader["MobileNo"].ToString()!,
                    Email =
                        string.IsNullOrWhiteSpace(
                            reader["Email"]?.ToString())
                            ? "—"
                            : reader["Email"].ToString()!
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
    // when the quick date filter has a resolved range.
    private static void AddDateRange(
        List<string> wheres,
        SqlParameterCollection parameters,
        DashboardDateFilter dateFilter,
        string dateColumn)
    {
        if (!dateFilter.FromDate.HasValue
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
        string dateColumn,
        string? pinnedStatus = null,
        bool applyStatusFilter = true)
    {
        AddDateRange(
            wheres,
            parameters,
            viewModel.DateFilter,
            dateColumn);

        AddIf(
            wheres,
            parameters,
            viewModel.StateId.HasValue,
            "c.StateId = @StateId",
            "@StateId",
            viewModel.StateId);

        AddIf(
            wheres,
            parameters,
            viewModel.UserId.HasValue,
            "ds.UserId = @UserId",
            "@UserId",
            viewModel.UserId);

        AddIf(
            wheres,
            parameters,
            viewModel.ClientId.HasValue,
            "ds.ClientId = @ClientId",
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
            status != "All",
            "ds.Status = @Status",
            "@Status",
            status);

        AddIf(
            wheres,
            parameters,
            viewModel.Priority != "All",
            "ds.Priority = @Priority",
            "@Priority",
            viewModel.Priority);
    }


    // Appends EntryOn / State / User / Client filter fragments for
    // ClientMaster queries behind the shared DashboardFilter.
    private static void AddClientFilters(
        List<string> wheres,
        SqlParameterCollection parameters,
        DashboardViewModel viewModel,
        string dateColumn)
    {
        AddDateRange(
            wheres,
            parameters,
            viewModel.DateFilter,
            dateColumn);

        AddIf(
            wheres,
            parameters,
            viewModel.StateId.HasValue,
            "c.StateId = @StateId",
            "@StateId",
            viewModel.StateId);

        AddIf(
            wheres,
            parameters,
            viewModel.UserId.HasValue,
            "c.UserId = @UserId",
            "@UserId",
            viewModel.UserId);

        AddIf(
            wheres,
            parameters,
            viewModel.ClientId.HasValue,
            "c.Id = @ClientId",
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
        string dateColumn)
    {
        AddDateRange(
            wheres,
            parameters,
            viewModel.DateFilter,
            dateColumn);

        AddIf(
            wheres,
            parameters,
            viewModel.StateId.HasValue,
            "c.StateId = @StateId",
            "@StateId",
            viewModel.StateId);

        AddIf(
            wheres,
            parameters,
            viewModel.UserId.HasValue,
            "v.UserId = @UserId",
            "@UserId",
            viewModel.UserId);

        AddIf(
            wheres,
            parameters,
            viewModel.ClientId.HasValue,
            "v.ClientId = @ClientId",
            "@ClientId",
            viewModel.ClientId);

        AddIf(
            wheres,
            parameters,
            viewModel.Status != "All",
            "v.Status = @Status",
            "@Status",
            viewModel.Status);
    }


    private static async Task<
        List<LookupOptionViewModel>>
        LoadStatesAsync(SqlConnection connection)
    {
        string query = $@"
            SELECT
                {DatabaseMapping.States.Id},
                {DatabaseMapping.States.StateName}

            FROM
                {DatabaseMapping.States.Table}

            WHERE
                {DatabaseMapping.States.IsActive} = 1

            ORDER BY
                {DatabaseMapping.States.StateName}";

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
        LoadUsersAsync(SqlConnection connection)
    {
        string query = $@"
            SELECT
                {DatabaseMapping.LoginUsers.Id},
                {DatabaseMapping.LoginUsers.UserName}

            FROM
                {DatabaseMapping.LoginUsers.Table}

            WHERE
                {DatabaseMapping.LoginUsers.IsActive} = 1

            ORDER BY
                {DatabaseMapping.LoginUsers.UserName}";

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
        LoadClientsAsync(SqlConnection connection)
    {
        string query = $@"
            SELECT
                {DatabaseMapping.ClientMaster.Id},
                {DatabaseMapping.ClientMaster.ClientName}

            FROM
                {DatabaseMapping.ClientMaster.Table}

            WHERE
                {DatabaseMapping.ClientMaster.IsActive} = 1

            ORDER BY
                {DatabaseMapping.ClientMaster.ClientName}";

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