using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Master.Configuration;
using Master.Models;
using Microsoft.Data.SqlClient;

namespace Master.Controllers;

// Reports — Support Report and Visit Report builders.
public partial class ReportsController
{
    // ==========================================================
    // Report: Support
    // ==========================================================

    private async Task BuildSupportReportAsync(
        SqlConnection connection,
        ReportsViewModel model,
        DynamicTableService.SchemaColumns schema)
    {
        model.Title = "Support Report";

        model.Columns = new List<string>
        {
            "Support Date", "Client", "State", "User", "Type",
            "Subject", "Status", "Priority", "Start", "End",
            "Follow-up"
        };

        model.ColumnWidths = new List<double>
        {
            0.09, 0.13, 0.09, 0.09, 0.09, 0.18,
            0.08, 0.07, 0.06, 0.06, 0.09
        };

        string? dsDate = Col(schema, "ds", "DailySupport", "SupportDate");
        string? dsUser = Col(schema, "ds", "DailySupport", "UserId");
        string? dsStatus = Col(schema, "ds", "DailySupport", "Status");
        string? dsPriority = Col(schema, "ds", "DailySupport", "Priority");
        string? cName = Col(schema, "c", "ClientMaster", "ClientName");
        string? stName = Col(schema, "st", "States", "StateName");
        string? luName = Col(schema, "lu", "LoginUsers", "UserName");
        string? dsType = Col(schema, "ds", "DailySupport", "SupportType");
        string? dsSubject = Col(schema, "ds", "DailySupport", "Subject");
        string? dsStart = Col(schema, "ds", "DailySupport", "StartTime");
        string? dsEnd = Col(schema, "ds", "DailySupport", "EndTime");
        string? dsFollow = Col(schema, "ds", "DailySupport", "FollowUpDate");
        string? dsId = Col(schema, "ds", "DailySupport", "Id");

        var joins = new List<string> { "FROM [DailySupport] ds" };

        string? joinClient = JoinIf(
            "INNER JOIN", schema,
            "DailySupport", "ClientId", "ds",
            "ClientMaster", "Id", "c");

        string? joinState = null;

        string? joinUser = JoinIf(
            "INNER JOIN", schema,
            "DailySupport", "UserId", "ds",
            "LoginUsers", "Id", "lu");

        var active = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase) { "ds" };

        if (joinClient != null)
        {
            joins.Add(joinClient);
            active.Add("c");

            joinState = JoinIf(
                "INNER JOIN", schema,
                "ClientMaster", "StateId", "c",
                "States", "Id", "st");

            if (joinState != null)
            {
                joins.Add(joinState);
                active.Add("st");
            }
        }

        if (joinUser != null)
        {
            joins.Add(joinUser);
            active.Add("lu");
        }

        string fromJoins = string.Join("\n            ", joins);

        var filters = BuildFilters(
            model,
            dsDate,
            dsUser,
            dsStatus,
            dsPriority,
            joinClient != null
                ? Col(schema, "c", "ClientMaster", "StateId")
                : null,
            joinClient != null
                ? Col(schema, "c", "ClientMaster", "Id")
                : null);

        using (var command = new SqlCommand())
        {
            command.Connection = connection;
            command.CommandTimeout = 0;

            ApplyFilters(command, filters);

            string orderBy = (dsDate != null && dsId != null)
                ? $"{dsDate} DESC, {dsId} DESC"
                : "(SELECT NULL)";

            command.CommandText = $@"
                SELECT
                    {Sel(dsDate, active, "ds")},
                    {Sel(cName, active, "c")},
                    {Sel(stName, active, "st")},
                    {Sel(luName, active, "lu")},
                    {Sel(dsType, active, "ds")},
                    {Sel(dsSubject, active, "ds")},
                    {Sel(dsStatus, active, "ds")},
                    {Sel(dsPriority, active, "ds")},
                    {Sel(dsStart, active, "ds")},
                    {Sel(dsEnd, active, "ds")},
                    {Sel(dsFollow, active, "ds")}
                {fromJoins}
                {WhereOf(filters)}
                ORDER BY
                    {orderBy}";

            using var reader =
                await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                model.Rows.Add(new[]
                {
                    FmtDate(reader.GetValue(0)),
                    Str(reader.GetValue(1)),
                    Str(reader.GetValue(2)),
                    Str(reader.GetValue(3)),
                    Str(reader.GetValue(4)),
                    Str(reader.GetValue(5)),
                    Str(reader.GetValue(6)),
                    Str(reader.GetValue(7)),
                    FmtTime(reader.GetValue(8)),
                    FmtTime(reader.GetValue(9)),
                    FmtDate(reader.GetValue(10))
                });
            }
        }

        model.TotalRecords = model.Rows.Count;

        Dictionary<string, int> byStatus =
            new(StringComparer.OrdinalIgnoreCase);

        if (dsStatus != null)
        {
            byStatus = await GroupCountAsync(
                connection,
                fromJoins,
                dsStatus,
                filters);
        }

        model.Summary.Add(new SummaryCardViewModel
        {
            Title = "TOTAL SUPPORT",
            Value = model.TotalRecords,
            Icon = "🛠️"
        });

        foreach (var status in
                 DashboardViewModel.DailySupportStatuses)
        {
            model.Summary.Add(new SummaryCardViewModel
            {
                Title = status.ToUpperInvariant(),
                Value = byStatus.TryGetValue(status, out var c)
                    ? c : 0,
                Icon = "•"
            });
        }
    }


    // ==========================================================
    // Report: Visits
    // ==========================================================

    private async Task BuildVisitReportAsync(
        SqlConnection connection,
        ReportsViewModel model,
        DynamicTableService.SchemaColumns schema)
    {
        model.Title = "Visit Report";

        model.Columns = new List<string>
        {
            "Visit Date", "Client", "State", "User", "Visit Type",
            "Person Met", "Subject", "Status", "Next Action",
            "Follow-up"
        };

        model.ColumnWidths = new List<double>
        {
            0.09, 0.13, 0.09, 0.09, 0.09, 0.10,
            0.15, 0.08, 0.10, 0.08
        };

        string? vDate = Col(schema, "v", "ClientVisiting", "VisitDate");
        string? vUser = Col(schema, "v", "ClientVisiting", "UserId");
        string? vStatus = Col(schema, "v", "ClientVisiting", "Status");
        string? cName = Col(schema, "c", "ClientMaster", "ClientName");
        string? stName = Col(schema, "st", "States", "StateName");
        string? luName = Col(schema, "lu", "LoginUsers", "UserName");
        string? vType = Col(schema, "v", "ClientVisiting", "VisitType");
        string? vPerson = Col(schema, "v", "ClientVisiting", "PersonMet");
        string? vSubject = Col(schema, "v", "ClientVisiting", "Subject");
        string? vNext = Col(schema, "v", "ClientVisiting", "NextAction");
        string? vFollow = Col(schema, "v", "ClientVisiting", "FollowUpDate");
        string? vId = Col(schema, "v", "ClientVisiting", "Id");

        var joins = new List<string> { "FROM [ClientVisiting] v" };

        string? joinClient = JoinIf(
            "INNER JOIN", schema,
            "ClientVisiting", "ClientId", "v",
            "ClientMaster", "Id", "c");

        string? joinState = null;

        string? joinUser = JoinIf(
            "INNER JOIN", schema,
            "ClientVisiting", "UserId", "v",
            "LoginUsers", "Id", "lu");

        var active = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase) { "v" };

        if (joinClient != null)
        {
            joins.Add(joinClient);
            active.Add("c");

            joinState = JoinIf(
                "INNER JOIN", schema,
                "ClientMaster", "StateId", "c",
                "States", "Id", "st");

            if (joinState != null)
            {
                joins.Add(joinState);
                active.Add("st");
            }
        }

        if (joinUser != null)
        {
            joins.Add(joinUser);
            active.Add("lu");
        }

        string fromJoins = string.Join("\n            ", joins);

        var filters = BuildFilters(
            model,
            vDate,
            vUser,
            vStatus,
            null,
            joinClient != null
                ? Col(schema, "c", "ClientMaster", "StateId")
                : null,
            joinClient != null
                ? Col(schema, "c", "ClientMaster", "Id")
                : null);

        using (var command = new SqlCommand())
        {
            command.Connection = connection;
            command.CommandTimeout = 0;

            ApplyFilters(command, filters);

            string orderBy = (vDate != null && vId != null)
                ? $"{vDate} DESC, {vId} DESC"
                : "(SELECT NULL)";

            command.CommandText = $@"
                SELECT
                    {Sel(vDate, active, "v")},
                    {Sel(cName, active, "c")},
                    {Sel(stName, active, "st")},
                    {Sel(luName, active, "lu")},
                    {Sel(vType, active, "v")},
                    {Sel(vPerson, active, "v")},
                    {Sel(vSubject, active, "v")},
                    {Sel(vStatus, active, "v")},
                    {Sel(vNext, active, "v")},
                    {Sel(vFollow, active, "v")}
                {fromJoins}
                {WhereOf(filters)}
                ORDER BY
                    {orderBy}";

            using var reader =
                await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                model.Rows.Add(new[]
                {
                    FmtDate(reader.GetValue(0)),
                    Str(reader.GetValue(1)),
                    Str(reader.GetValue(2)),
                    Str(reader.GetValue(3)),
                    Str(reader.GetValue(4)),
                    Str(reader.GetValue(5)),
                    Str(reader.GetValue(6)),
                    Str(reader.GetValue(7)),
                    Str(reader.GetValue(8)),
                    FmtDate(reader.GetValue(9))
                });
            }
        }

        model.TotalRecords = model.Rows.Count;

        Dictionary<string, int> byStatus =
            new(StringComparer.OrdinalIgnoreCase);

        if (vStatus != null)
        {
            byStatus = await GroupCountAsync(
                connection,
                fromJoins,
                vStatus,
                filters);
        }

        model.Summary.Add(new SummaryCardViewModel
        {
            Title = "TOTAL VISITS",
            Value = model.TotalRecords,
            Icon = "🚗"
        });

        foreach (var kvp in byStatus
                     .OrderByDescending(k => k.Value))
        {
            model.Summary.Add(new SummaryCardViewModel
            {
                Title = kvp.Key.ToUpperInvariant(),
                Value = kvp.Value,
                Icon = "•"
            });
        }
    }


    // Emits "alias.[column]" when the column exists and the
    // join for that alias is active, otherwise "NULL".
    private static string Sel(
        string? column,
        HashSet<string> activeAliases,
        string alias)
    {
        if (column == null
            || !activeAliases.Contains(alias))
        {
            return "NULL";
        }

        return column;
    }
}