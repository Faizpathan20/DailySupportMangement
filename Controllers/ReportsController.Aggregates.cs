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

    private async Task BuildClientReportAsync(
        SqlConnection connection,
        ReportsViewModel model,
        DynamicTableService.SchemaColumns schema)
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

        string? cName = Col(schema, "c", "ClientMaster", "ClientName");
        string? cCity = Col(schema, "c", "ClientMaster", "City");
        string? cMobile = Col(schema, "c", "ClientMaster", "MobileNo");
        string? cEmail = Col(schema, "c", "ClientMaster", "Email");
        string? cEntry = Col(schema, "c", "ClientMaster", "EntryOn");
        string? cActive = Col(schema, "c", "ClientMaster", "IsActive");
        string? cId = Col(schema, "c", "ClientMaster", "Id");
        string? cStateId = Col(schema, "c", "ClientMaster", "StateId");
        string? cUserId = Col(schema, "c", "ClientMaster", "UserId");
        string? stName = Col(schema, "st", "States", "StateName");
        string? stId = Col(schema, "st", "States", "Id");
        string? luName = Col(schema, "lu", "LoginUsers", "UserName");
        string? luId = Col(schema, "lu", "LoginUsers", "Id");

        // LEFT JOINs so clients without a State/User still appear.
        var joins = new List<string> { "FROM [ClientMaster] c" };

        var active = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase) { "c" };

        string? joinState = JoinIf(
            "LEFT JOIN", schema,
            "ClientMaster", "StateId", "c",
            "States", "Id", "st");

        if (joinState != null)
        {
            joins.Add(joinState);
            active.Add("st");
        }

        string? joinUser = JoinIf(
            "LEFT JOIN", schema,
            "ClientMaster", "UserId", "c",
            "LoginUsers", "Id", "lu");

        if (joinUser != null)
        {
            joins.Add(joinUser);
            active.Add("lu");
        }

        string fromJoins =
            string.Join("\n            ", joins);

        var filters = BuildFilters(
            model,
            cEntry,
            cUserId,
            null,
            null,
            cStateId,
            cId);

        using (var command = new SqlCommand())
        {
            command.Connection = connection;
            command.CommandTimeout = 0;

            ApplyFilters(command, filters);

            string orderBy = (cEntry != null && cId != null)
                ? $"{cEntry} DESC, {cId} DESC"
                : "(SELECT NULL)";

            command.CommandText = $@"
                SELECT
                    {Sel(cName, active, "c")},
                    {Sel(stName, active, "st")},
                    {Sel(cCity, active, "c")},
                    {Sel(cMobile, active, "c")},
                    {Sel(cEmail, active, "c")},
                    {Sel(luName, active, "lu")},
                    {Sel(cEntry, active, "c")},
                    {Sel(cActive, active, "c")}
                {fromJoins}
                {WhereOf(filters)}
                ORDER BY
                    {orderBy}";

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
        int activeCount = 0;
        int nonActiveCount = 0;

        using (var command = new SqlCommand())
        {
            command.Connection = connection;
            command.CommandTimeout = 0;

            ApplyFilters(command, filters);

            string countSelect = "COUNT(*)";

            if (cActive != null)
            {
                countSelect += ", "
                    + $"SUM(CASE WHEN {cActive} = 1 "
                    + "THEN 1 ELSE 0 END), "
                    + $"SUM(CASE WHEN {cActive} = 0 "
                    + "THEN 1 ELSE 0 END)";
            }

            command.CommandText = $@"
                SELECT
                    {countSelect}
                FROM [ClientMaster] c
                {WhereOf(filters)}";

            using var reader =
                await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                total = Convert.ToInt32(reader.GetValue(0));

                if (cActive != null)
                {
                    activeCount =
                        reader.GetValue(1) == DBNull.Value
                            ? 0
                            : Convert.ToInt32(reader.GetValue(1));

                    nonActiveCount =
                        reader.GetValue(2) == DBNull.Value
                            ? 0
                            : Convert.ToInt32(reader.GetValue(2));
                }
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
            Value = activeCount,
            Icon = "•"
        });

        model.Summary.Add(new SummaryCardViewModel
        {
            Title = "NON-ACTIVE CLIENTS",
            Value = nonActiveCount,
            Icon = "•"
        });
    }


    // ==========================================================
    // Report: User Activity
    // ==========================================================

    private async Task BuildUserActivityReportAsync(
        SqlConnection connection,
        ReportsViewModel model,
        DynamicTableService.SchemaColumns schema)
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

        string? cStateId = Col(schema, "c", "ClientMaster", "StateId");
        string? cId = Col(schema, "c", "ClientMaster", "Id");
        string? cEntry = Col(schema, "c", "ClientMaster", "EntryOn");
        string? cUserId = Col(schema, "c", "ClientMaster", "UserId");
        string? luName = Col(schema, "lu", "LoginUsers", "UserName");
        string? luId = Col(schema, "lu", "LoginUsers", "Id");
        string? dsDate = Col(schema, "ds", "DailySupport", "SupportDate");
        string? dsUser = Col(schema, "ds", "DailySupport", "UserId");
        string? dsStatus = Col(schema, "ds", "DailySupport", "Status");
        string? dsPriority = Col(schema, "ds", "DailySupport", "Priority");
        string? vDate = Col(schema, "v", "ClientVisiting", "VisitDate");
        string? vUser = Col(schema, "v", "ClientVisiting", "UserId");
        string? vStatus = Col(schema, "v", "ClientVisiting", "Status");

        // Support per user.
        var supportJoins =
            new List<string> { "FROM [DailySupport] ds" };

        var supportActive =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase) { "ds" };

        if (JoinIf(
                "INNER JOIN", schema,
                "DailySupport", "ClientId", "ds",
                "ClientMaster", "Id", "c") != null)
        {
            supportJoins.Add(
                $"INNER JOIN [ClientMaster] c "
                + $"ON {cId} = ds.[ClientId]");
            supportActive.Add("c");
        }

        if (JoinIf(
                "LEFT JOIN", schema,
                "DailySupport", "UserId", "ds",
                "LoginUsers", "Id", "lu") != null)
        {
            supportJoins.Add(
                $"LEFT JOIN [LoginUsers] lu "
                + $"ON {luId} = ds.[UserId]");
            supportActive.Add("lu");
        }

        var supportFilters = BuildFilters(
            model,
            dsDate,
            dsUser,
            dsStatus,
            dsPriority,
            supportActive.Contains("c") ? cStateId : null,
            supportActive.Contains("c") ? cId : null);

        var supportRows = await GroupByIntAsync(
            connection,
            $@"
                SELECT
                    {Sel(dsUser, supportActive, "ds")},
                    {Sel(luName, supportActive, "lu")},
                    COUNT(*)
                {string.Join("\n            ", supportJoins)}
                {WhereOf(supportFilters)}
                {GroupClause(dsUser, supportActive, "ds",
                    luName, supportActive, "lu")}",
            supportFilters,
            dsUser);

        // Visits per user.
        var visitJoins =
            new List<string> { "FROM [ClientVisiting] v" };

        var visitActive =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase) { "v" };

        if (JoinIf(
                "INNER JOIN", schema,
                "ClientVisiting", "ClientId", "v",
                "ClientMaster", "Id", "c") != null)
        {
            visitJoins.Add(
                $"INNER JOIN [ClientMaster] c "
                + $"ON {cId} = v.[ClientId]");
            visitActive.Add("c");
        }

        if (JoinIf(
                "LEFT JOIN", schema,
                "ClientVisiting", "UserId", "v",
                "LoginUsers", "Id", "lu") != null)
        {
            visitJoins.Add(
                $"LEFT JOIN [LoginUsers] lu "
                + $"ON {luId} = v.[UserId]");
            visitActive.Add("lu");
        }

        var visitFilters = BuildFilters(
            model,
            vDate,
            vUser,
            vStatus,
            null,
            visitActive.Contains("c") ? cStateId : null,
            visitActive.Contains("c") ? cId : null);

        var visitRows = await GroupByIntAsync(
            connection,
            $@"
                SELECT
                    {Sel(vUser, visitActive, "v")},
                    {Sel(luName, visitActive, "lu")},
                    COUNT(*)
                {string.Join("\n            ", visitJoins)}
                {WhereOf(visitFilters)}
                {GroupClause(vUser, visitActive, "v",
                    luName, visitActive, "lu")}",
            visitFilters,
            vUser);

        // Clients per user.
        var clientJoins =
            new List<string> { "FROM [ClientMaster] c" };

        var clientActive =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase) { "c" };

        if (JoinIf(
                "LEFT JOIN", schema,
                "ClientMaster", "UserId", "c",
                "LoginUsers", "Id", "lu") != null)
        {
            clientJoins.Add(
                $"LEFT JOIN [LoginUsers] lu "
                + $"ON {luId} = c.[UserId]");
            clientActive.Add("lu");
        }

        var clientFilters = BuildFilters(
            model,
            cEntry,
            cUserId,
            null,
            null,
            cStateId,
            cId);

        var clientRows = await GroupByIntAsync(
            connection,
            $@"
                SELECT
                    {Sel(cUserId, clientActive, "c")},
                    {Sel(luName, clientActive, "lu")},
                    COUNT(*)
                {string.Join("\n            ", clientJoins)}
                {WhereOf(clientFilters)}
                {GroupClause(cUserId, clientActive, "c",
                    luName, clientActive, "lu")}",
            clientFilters,
            cUserId);

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

    private async Task BuildStateWiseReportAsync(
        SqlConnection connection,
        ReportsViewModel model,
        DynamicTableService.SchemaColumns schema)
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

        string? cStateId = Col(schema, "c", "ClientMaster", "StateId");
        string? cId = Col(schema, "c", "ClientMaster", "Id");
        string? cEntry = Col(schema, "c", "ClientMaster", "EntryOn");
        string? cUserId = Col(schema, "c", "ClientMaster", "UserId");
        string? stName = Col(schema, "st", "States", "StateName");
        string? stId = Col(schema, "st", "States", "Id");
        string? dsDate = Col(schema, "ds", "DailySupport", "SupportDate");
        string? dsUser = Col(schema, "ds", "DailySupport", "UserId");
        string? dsStatus = Col(schema, "ds", "DailySupport", "Status");
        string? dsPriority = Col(schema, "ds", "DailySupport", "Priority");
        string? vDate = Col(schema, "v", "ClientVisiting", "VisitDate");
        string? vUser = Col(schema, "v", "ClientVisiting", "UserId");
        string? vStatus = Col(schema, "v", "ClientVisiting", "Status");

        // Clients per state.
        var clientJoins =
            new List<string> { "FROM [ClientMaster] c" };

        var clientActive =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase) { "c" };

        if (JoinIf(
                "LEFT JOIN", schema,
                "ClientMaster", "StateId", "c",
                "States", "Id", "st") != null)
        {
            clientJoins.Add(
                $"LEFT JOIN [States] st "
                + $"ON {stId} = c.[StateId]");
            clientActive.Add("st");
        }

        var clientFilters = BuildFilters(
            model,
            cEntry,
            cUserId,
            null,
            null,
            cStateId,
            cId);

        var clientRows = await GroupByIntAsync(
            connection,
            $@"
                SELECT
                    {Sel(cStateId, clientActive, "c")},
                    {Sel(stName, clientActive, "st")},
                    COUNT(*)
                {string.Join("\n            ", clientJoins)}
                {WhereOf(clientFilters)}
                {GroupClause(cStateId, clientActive, "c",
                    stName, clientActive, "st")}",
            clientFilters,
            cStateId);

        // Support per state.
        var supportJoins =
            new List<string> { "FROM [DailySupport] ds" };

        var supportActive =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase) { "ds" };

        if (JoinIf(
                "INNER JOIN", schema,
                "DailySupport", "ClientId", "ds",
                "ClientMaster", "Id", "c") != null)
        {
            supportJoins.Add(
                $"INNER JOIN [ClientMaster] c "
                + $"ON {cId} = ds.[ClientId]");
            supportActive.Add("c");
        }

        if (supportActive.Contains("c")
            && JoinIf(
                "LEFT JOIN", schema,
                "ClientMaster", "StateId", "c",
                "States", "Id", "st") != null)
        {
            supportJoins.Add(
                $"LEFT JOIN [States] st "
                + $"ON {stId} = c.[StateId]");
            supportActive.Add("st");
        }

        var supportFilters = BuildFilters(
            model,
            dsDate,
            dsUser,
            dsStatus,
            dsPriority,
            supportActive.Contains("c") ? cStateId : null,
            supportActive.Contains("c") ? cId : null);

        var supportRows = await GroupByIntAsync(
            connection,
            $@"
                SELECT
                    {Sel(cStateId, supportActive, "c")},
                    {Sel(stName, supportActive, "st")},
                    COUNT(*)
                {string.Join("\n            ", supportJoins)}
                {WhereOf(supportFilters)}
                {GroupClause(cStateId, supportActive, "c",
                    stName, supportActive, "st")}",
            supportFilters,
            cStateId);

        // Visits per state.
        var visitJoins =
            new List<string> { "FROM [ClientVisiting] v" };

        var visitActive =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase) { "v" };

        if (JoinIf(
                "INNER JOIN", schema,
                "ClientVisiting", "ClientId", "v",
                "ClientMaster", "Id", "c") != null)
        {
            visitJoins.Add(
                $"INNER JOIN [ClientMaster] c "
                + $"ON {cId} = v.[ClientId]");
            visitActive.Add("c");
        }

        if (visitActive.Contains("c")
            && JoinIf(
                "LEFT JOIN", schema,
                "ClientMaster", "StateId", "c",
                "States", "Id", "st") != null)
        {
            visitJoins.Add(
                $"LEFT JOIN [States] st "
                + $"ON {stId} = c.[StateId]");
            visitActive.Add("st");
        }

        var visitFilters = BuildFilters(
            model,
            vDate,
            vUser,
            vStatus,
            null,
            visitActive.Contains("c") ? cStateId : null,
            visitActive.Contains("c") ? cId : null);

        var visitRows = await GroupByIntAsync(
            connection,
            $@"
                SELECT
                    {Sel(cStateId, visitActive, "c")},
                    {Sel(stName, visitActive, "st")},
                    COUNT(*)
                {string.Join("\n            ", visitJoins)}
                {WhereOf(visitFilters)}
                {GroupClause(cStateId, visitActive, "c",
                    stName, visitActive, "st")}",
            visitFilters,
            cStateId);

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
            List<ReportFilter> filters,
            string? keyColumn)
    {
        var result =
            new Dictionary<int, (string Name, int Count)>();

        // Without the grouping key column the report cannot
        // group at all — degrade to "no rows" instead of
        // generating invalid SQL.
        if (string.IsNullOrWhiteSpace(keyColumn))
        {
            return result;
        }

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


    // Builds a "GROUP BY a, b" clause that only lists
    // columns which actually exist and are joined in.
    private static string GroupClause(
        string? colA,
        HashSet<string> activeAliases,
        string aliasA,
        string? colB,
        HashSet<string> activeAliasesB,
        string aliasB)
    {
        var parts = new List<string>();

        if (colA != null && activeAliases.Contains(aliasA))
        {
            parts.Add(colA);
        }

        if (colB != null
            && activeAliasesB.Contains(aliasB))
        {
            parts.Add(colB);
        }

        return parts.Count == 0
            ? ""
            : "GROUP BY " + string.Join(", ", parts);
    }
}