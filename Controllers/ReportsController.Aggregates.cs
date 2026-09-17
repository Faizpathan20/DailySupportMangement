using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Master.Configuration;
using Master.Models;
using Microsoft.Data.SqlClient;

namespace Master.Controllers;

// Reports — Client Report, User Activity and State-wise Report builders.
public partial class ReportsController
{
    // ==========================================================
    // Report: Clients
    // ==========================================================

    private static async Task BuildClientReportAsync(
        SqlConnection connection,
        ReportsViewModel model)
    {
        model.Title = "Client Report";

        model.Columns = new List<string>
        {
            "Client", "State", "City", "Mobile", "Email",
            "Created By", "Entry Date", "Status"
        };

        model.ColumnWidths = new List<double>
        {
            0.16, 0.11, 0.11, 0.11, 0.16, 0.12, 0.11, 0.12
        };

        // LEFT JOINs so clients without a State/User still appear.
        string fromJoins = $@"
            FROM {DatabaseMapping.ClientMaster.Table} c
            LEFT JOIN {DatabaseMapping.States.Table} st
                ON st.{DatabaseMapping.States.Id}
                = c.{DatabaseMapping.ClientMaster.StateId}
            LEFT JOIN {DatabaseMapping.LoginUsers.Table} lu
                ON lu.{DatabaseMapping.LoginUsers.Id}
                = c.{DatabaseMapping.ClientMaster.UserId}";

        var filters = BuildFilters(
            model,
            "c." + DatabaseMapping.ClientMaster.EntryOn,
            "c." + DatabaseMapping.ClientMaster.UserId,
            null,
            null);

        using (var command = new SqlCommand())
        {
            command.Connection = connection;
            command.CommandTimeout = 0;

            ApplyFilters(command, filters);

            command.CommandText = $@"
                SELECT
                    c.{DatabaseMapping.ClientMaster.ClientName},
                    st.{DatabaseMapping.States.StateName},
                    c.{DatabaseMapping.ClientMaster.City},
                    c.{DatabaseMapping.ClientMaster.MobileNo},
                    c.{DatabaseMapping.ClientMaster.Email},
                    lu.{DatabaseMapping.LoginUsers.UserName},
                    c.{DatabaseMapping.ClientMaster.EntryOn},
                    c.{DatabaseMapping.ClientMaster.IsActive}
                {fromJoins}
                {WhereOf(filters)}
                ORDER BY
                    c.{DatabaseMapping.ClientMaster.EntryOn} DESC,
                    c.{DatabaseMapping.ClientMaster.Id} DESC";

            using var reader =
                await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                bool isActive =
                    reader.GetValue(7) != DBNull.Value
                    && Convert.ToBoolean(reader.GetValue(7));

                model.Rows.Add(new[]
                {
                    Str(reader.GetValue(0)),
                    Str(reader.GetValue(1)),
                    Str(reader.GetValue(2)),
                    Str(reader.GetValue(3)),
                    Str(reader.GetValue(4)),
                    Str(reader.GetValue(5)),
                    FmtDate(reader.GetValue(6)),
                    isActive ? "Active" : "Non Active"
                });
            }
        }

        model.TotalRecords = model.Rows.Count;

        int total = 0;
        int active = 0;
        int nonActive = 0;

        using (var command = new SqlCommand())
        {
            command.Connection = connection;
            command.CommandTimeout = 0;

            ApplyFilters(command, filters);

            command.CommandText = $@"
                SELECT
                    COUNT(*),
                    SUM(CASE WHEN c.{DatabaseMapping.ClientMaster.IsActive}
                        = 1 THEN 1 ELSE 0 END),
                    SUM(CASE WHEN c.{DatabaseMapping.ClientMaster.IsActive}
                        = 0 THEN 1 ELSE 0 END)
                FROM {DatabaseMapping.ClientMaster.Table} c
                {WhereOf(filters)}";

            using var reader =
                await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                total = Convert.ToInt32(reader.GetValue(0));

                active = reader.GetValue(1) == DBNull.Value
                    ? 0 : Convert.ToInt32(reader.GetValue(1));

                nonActive = reader.GetValue(2) == DBNull.Value
                    ? 0 : Convert.ToInt32(reader.GetValue(2));
            }
        }

        model.Summary.Add(new SummaryCardViewModel
        {
            Title = "TOTAL CLIENTS",
            Value = total,
            Icon = "🏢"
        });

        model.Summary.Add(new SummaryCardViewModel
        {
            Title = "ACTIVE CLIENTS",
            Value = active,
            Icon = "•"
        });

        model.Summary.Add(new SummaryCardViewModel
        {
            Title = "NON-ACTIVE CLIENTS",
            Value = nonActive,
            Icon = "•"
        });
    }


    // ==========================================================
    // Report: User Activity
    // ==========================================================

    private static async Task BuildUserActivityReportAsync(
        SqlConnection connection,
        ReportsViewModel model)
    {
        model.Title = "User Activity Report";

        model.Columns = new List<string>
        {
            "User", "Support", "Visits", "Clients", "Total"
        };

        model.ColumnWidths = new List<double>
        {
            0.40, 0.15, 0.15, 0.15, 0.15
        };

        // Support per user.
        var supportFilters = BuildFilters(
            model,
            "ds." + DatabaseMapping.DailySupport.SupportDate,
            "ds." + DatabaseMapping.DailySupport.UserId,
            "ds." + DatabaseMapping.DailySupport.Status,
            "ds." + DatabaseMapping.DailySupport.Priority);

        var supportRows = await GroupByIntAsync(
            connection,
            $@"
                SELECT
                    ds.{DatabaseMapping.DailySupport.UserId},
                    lu.{DatabaseMapping.LoginUsers.UserName},
                    COUNT(*)
                FROM {DatabaseMapping.DailySupport.Table} ds
                INNER JOIN {DatabaseMapping.ClientMaster.Table} c
                    ON c.{DatabaseMapping.ClientMaster.Id}
                    = ds.{DatabaseMapping.DailySupport.ClientId}
                LEFT JOIN {DatabaseMapping.LoginUsers.Table} lu
                    ON lu.{DatabaseMapping.LoginUsers.Id}
                    = ds.{DatabaseMapping.DailySupport.UserId}
                {WhereOf(supportFilters)}
                GROUP BY
                    ds.{DatabaseMapping.DailySupport.UserId},
                    lu.{DatabaseMapping.LoginUsers.UserName}",
            supportFilters);

        // Visits per user.
        var visitFilters = BuildFilters(
            model,
            "v." + DatabaseMapping.ClientVisiting.VisitDate,
            "v." + DatabaseMapping.ClientVisiting.UserId,
            "v." + DatabaseMapping.ClientVisiting.Status,
            null);

        var visitRows = await GroupByIntAsync(
            connection,
            $@"
                SELECT
                    v.{DatabaseMapping.ClientVisiting.UserId},
                    lu.{DatabaseMapping.LoginUsers.UserName},
                    COUNT(*)
                FROM {DatabaseMapping.ClientVisiting.Table} v
                INNER JOIN {DatabaseMapping.ClientMaster.Table} c
                    ON c.{DatabaseMapping.ClientMaster.Id}
                    = v.{DatabaseMapping.ClientVisiting.ClientId}
                LEFT JOIN {DatabaseMapping.LoginUsers.Table} lu
                    ON lu.{DatabaseMapping.LoginUsers.Id}
                    = v.{DatabaseMapping.ClientVisiting.UserId}
                {WhereOf(visitFilters)}
                GROUP BY
                    v.{DatabaseMapping.ClientVisiting.UserId},
                    lu.{DatabaseMapping.LoginUsers.UserName}",
            visitFilters);

        // Clients per user.
        var clientFilters = BuildFilters(
            model,
            "c." + DatabaseMapping.ClientMaster.EntryOn,
            "c." + DatabaseMapping.ClientMaster.UserId,
            null,
            null);

        var clientRows = await GroupByIntAsync(
            connection,
            $@"
                SELECT
                    c.{DatabaseMapping.ClientMaster.UserId},
                    lu.{DatabaseMapping.LoginUsers.UserName},
                    COUNT(*)
                FROM {DatabaseMapping.ClientMaster.Table} c
                LEFT JOIN {DatabaseMapping.LoginUsers.Table} lu
                    ON lu.{DatabaseMapping.LoginUsers.Id}
                    = c.{DatabaseMapping.ClientMaster.UserId}
                {WhereOf(clientFilters)}
                GROUP BY
                    c.{DatabaseMapping.ClientMaster.UserId},
                    lu.{DatabaseMapping.LoginUsers.UserName}",
            clientFilters);

        // Merge by user id.
        var keys = new HashSet<int>();

        keys.UnionWith(supportRows.Keys);
        keys.UnionWith(visitRows.Keys);
        keys.UnionWith(clientRows.Keys);

        var merged = new List<(string Name, int Support, int Visits,
            int Clients)>();

        foreach (var key in keys)
        {
            string name =
                supportRows.TryGetValue(key, out var s) ? s.Name
                : visitRows.TryGetValue(key, out var v) ? v.Name
                : clientRows.TryGetValue(key, out var cl) ? cl.Name
                : "Unassigned";

            merged.Add((
                name,
                s.Count,
                visitRows.TryGetValue(key, out var vv) ? vv.Count : 0,
                clientRows.TryGetValue(key, out var cc) ? cc.Count : 0));
        }

        foreach (var row in merged
                     .OrderByDescending(r =>
                         r.Support + r.Visits + r.Clients)
                     .ThenBy(r => r.Name))
        {
            int total = row.Support + row.Visits + row.Clients;

            model.Rows.Add(new[]
            {
                row.Name,
                row.Support.ToString(CultureInfo.InvariantCulture),
                row.Visits.ToString(CultureInfo.InvariantCulture),
                row.Clients.ToString(CultureInfo.InvariantCulture),
                total.ToString(CultureInfo.InvariantCulture)
            });
        }

        model.TotalRecords = model.Rows.Count;

        model.Summary.Add(new SummaryCardViewModel
        {
            Title = "USERS",
            Value = model.TotalRecords,
            Icon = "👥"
        });

        model.Summary.Add(new SummaryCardViewModel
        {
            Title = "TOTAL SUPPORT",
            Value = merged.Sum(r => r.Support),
            Icon = "🛠️"
        });

        model.Summary.Add(new SummaryCardViewModel
        {
            Title = "TOTAL VISITS",
            Value = merged.Sum(r => r.Visits),
            Icon = "🚗"
        });

        model.Summary.Add(new SummaryCardViewModel
        {
            Title = "TOTAL CLIENTS",
            Value = merged.Sum(r => r.Clients),
            Icon = "🏢"
        });
    }


    // ==========================================================
    // Report: State-wise
    // ==========================================================

    private static async Task BuildStateWiseReportAsync(
        SqlConnection connection,
        ReportsViewModel model)
    {
        model.Title = "State-wise Report";

        model.Columns = new List<string>
        {
            "State", "Clients", "Support", "Visits", "Total"
        };

        model.ColumnWidths = new List<double>
        {
            0.40, 0.15, 0.15, 0.15, 0.15
        };

        // Clients per state.
        var clientFilters = BuildFilters(
            model,
            "c." + DatabaseMapping.ClientMaster.EntryOn,
            "c." + DatabaseMapping.ClientMaster.UserId,
            null,
            null);

        var clientRows = await GroupByIntAsync(
            connection,
            $@"
                SELECT
                    c.{DatabaseMapping.ClientMaster.StateId},
                    st.{DatabaseMapping.States.StateName},
                    COUNT(*)
                FROM {DatabaseMapping.ClientMaster.Table} c
                LEFT JOIN {DatabaseMapping.States.Table} st
                    ON st.{DatabaseMapping.States.Id}
                    = c.{DatabaseMapping.ClientMaster.StateId}
                {WhereOf(clientFilters)}
                GROUP BY
                    c.{DatabaseMapping.ClientMaster.StateId},
                    st.{DatabaseMapping.States.StateName}",
            clientFilters);

        // Support per state.
        var supportFilters = BuildFilters(
            model,
            "ds." + DatabaseMapping.DailySupport.SupportDate,
            "ds." + DatabaseMapping.DailySupport.UserId,
            "ds." + DatabaseMapping.DailySupport.Status,
            "ds." + DatabaseMapping.DailySupport.Priority);

        var supportRows = await GroupByIntAsync(
            connection,
            $@"
                SELECT
                    c.{DatabaseMapping.ClientMaster.StateId},
                    st.{DatabaseMapping.States.StateName},
                    COUNT(*)
                FROM {DatabaseMapping.DailySupport.Table} ds
                INNER JOIN {DatabaseMapping.ClientMaster.Table} c
                    ON c.{DatabaseMapping.ClientMaster.Id}
                    = ds.{DatabaseMapping.DailySupport.ClientId}
                LEFT JOIN {DatabaseMapping.States.Table} st
                    ON st.{DatabaseMapping.States.Id}
                    = c.{DatabaseMapping.ClientMaster.StateId}
                {WhereOf(supportFilters)}
                GROUP BY
                    c.{DatabaseMapping.ClientMaster.StateId},
                    st.{DatabaseMapping.States.StateName}",
            supportFilters);

        // Visits per state.
        var visitFilters = BuildFilters(
            model,
            "v." + DatabaseMapping.ClientVisiting.VisitDate,
            "v." + DatabaseMapping.ClientVisiting.UserId,
            "v." + DatabaseMapping.ClientVisiting.Status,
            null);

        var visitRows = await GroupByIntAsync(
            connection,
            $@"
                SELECT
                    c.{DatabaseMapping.ClientMaster.StateId},
                    st.{DatabaseMapping.States.StateName},
                    COUNT(*)
                FROM {DatabaseMapping.ClientVisiting.Table} v
                INNER JOIN {DatabaseMapping.ClientMaster.Table} c
                    ON c.{DatabaseMapping.ClientMaster.Id}
                    = v.{DatabaseMapping.ClientVisiting.ClientId}
                LEFT JOIN {DatabaseMapping.States.Table} st
                    ON st.{DatabaseMapping.States.Id}
                    = c.{DatabaseMapping.ClientMaster.StateId}
                {WhereOf(visitFilters)}
                GROUP BY
                    c.{DatabaseMapping.ClientMaster.StateId},
                    st.{DatabaseMapping.States.StateName}",
            visitFilters);

        var keys = new HashSet<int>();

        keys.UnionWith(clientRows.Keys);
        keys.UnionWith(supportRows.Keys);
        keys.UnionWith(visitRows.Keys);

        var merged = new List<(string Name, int Clients, int Support,
            int Visits)>();

        foreach (var key in keys)
        {
            string name =
                clientRows.TryGetValue(key, out var cl) ? cl.Name
                : supportRows.TryGetValue(key, out var s) ? s.Name
                : visitRows.TryGetValue(key, out var v) ? v.Name
                : "Unassigned";

            merged.Add((
                name,
                cl.Count,
                supportRows.TryGetValue(key, out var ss) ? ss.Count : 0,
                visitRows.TryGetValue(key, out var vv) ? vv.Count : 0));
        }

        foreach (var row in merged
                     .OrderByDescending(r =>
                         r.Clients + r.Support + r.Visits)
                     .ThenBy(r => r.Name))
        {
            int total = row.Clients + row.Support + row.Visits;

            model.Rows.Add(new[]
            {
                row.Name,
                row.Clients.ToString(CultureInfo.InvariantCulture),
                row.Support.ToString(CultureInfo.InvariantCulture),
                row.Visits.ToString(CultureInfo.InvariantCulture),
                total.ToString(CultureInfo.InvariantCulture)
            });
        }

        model.TotalRecords = model.Rows.Count;

        model.Summary.Add(new SummaryCardViewModel
        {
            Title = "STATES",
            Value = model.TotalRecords,
            Icon = "📍"
        });

        model.Summary.Add(new SummaryCardViewModel
        {
            Title = "TOTAL CLIENTS",
            Value = merged.Sum(r => r.Clients),
            Icon = "🏢"
        });

        model.Summary.Add(new SummaryCardViewModel
        {
            Title = "TOTAL SUPPORT",
            Value = merged.Sum(r => r.Support),
            Icon = "🛠️"
        });

        model.Summary.Add(new SummaryCardViewModel
        {
            Title = "TOTAL VISITS",
            Value = merged.Sum(r => r.Visits),
            Icon = "🚗"
        });
    }


    // ==========================================================
    // Grouped-by-int helper (used by the aggregate reports)
    // ==========================================================

    private static async Task<
        Dictionary<int, (string Name, int Count)>>
        GroupByIntAsync(
            SqlConnection connection,
            string sql,
            List<ReportFilter> filters)
    {
        var result =
            new Dictionary<int, (string Name, int Count)>();

        using var command = new SqlCommand();

        command.Connection = connection;
        command.CommandTimeout = 0;

        ApplyFilters(command, filters);

        command.CommandText = sql;

        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            int key = reader.IsDBNull(0)
                ? 0
                : Convert.ToInt32(reader.GetValue(0));

            string name = reader.IsDBNull(1)
                ? "Unassigned"
                : reader.GetValue(1).ToString()!;

            int count = Convert.ToInt32(reader.GetValue(2));

            result[key] = (name, count);
        }

        return result;
    }
}