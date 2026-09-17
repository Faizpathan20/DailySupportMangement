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

    private static async Task BuildSupportReportAsync(
        SqlConnection connection,
        ReportsViewModel model)
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

        string fromJoins = $@"
            FROM {DatabaseMapping.DailySupport.Table} ds
            INNER JOIN {DatabaseMapping.ClientMaster.Table} c
                ON c.{DatabaseMapping.ClientMaster.Id}
                = ds.{DatabaseMapping.DailySupport.ClientId}
            INNER JOIN {DatabaseMapping.States.Table} st
                ON st.{DatabaseMapping.States.Id}
                = c.{DatabaseMapping.ClientMaster.StateId}
            INNER JOIN {DatabaseMapping.LoginUsers.Table} lu
                ON lu.{DatabaseMapping.LoginUsers.Id}
                = ds.{DatabaseMapping.DailySupport.UserId}";

        var filters = BuildFilters(
            model,
            "ds." + DatabaseMapping.DailySupport.SupportDate,
            "ds." + DatabaseMapping.DailySupport.UserId,
            "ds." + DatabaseMapping.DailySupport.Status,
            "ds." + DatabaseMapping.DailySupport.Priority);

        using (var command = new SqlCommand())
        {
            command.Connection = connection;
            command.CommandTimeout = 0;

            ApplyFilters(command, filters);

            command.CommandText = $@"
                SELECT
                    ds.{DatabaseMapping.DailySupport.SupportDate},
                    c.{DatabaseMapping.ClientMaster.ClientName},
                    st.{DatabaseMapping.States.StateName},
                    lu.{DatabaseMapping.LoginUsers.UserName},
                    ds.{DatabaseMapping.DailySupport.SupportType},
                    ds.{DatabaseMapping.DailySupport.Subject},
                    ds.{DatabaseMapping.DailySupport.Status},
                    ds.{DatabaseMapping.DailySupport.Priority},
                    ds.{DatabaseMapping.DailySupport.StartTime},
                    ds.{DatabaseMapping.DailySupport.EndTime},
                    ds.{DatabaseMapping.DailySupport.FollowUpDate}
                {fromJoins}
                {WhereOf(filters)}
                ORDER BY
                    ds.{DatabaseMapping.DailySupport.SupportDate} DESC,
                    ds.{DatabaseMapping.DailySupport.Id} DESC";

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

        var byStatus = await GroupCountAsync(
            connection,
            fromJoins,
            "ds." + DatabaseMapping.DailySupport.Status,
            filters);

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

    private static async Task BuildVisitReportAsync(
        SqlConnection connection,
        ReportsViewModel model)
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

        string fromJoins = $@"
            FROM {DatabaseMapping.ClientVisiting.Table} v
            INNER JOIN {DatabaseMapping.ClientMaster.Table} c
                ON c.{DatabaseMapping.ClientMaster.Id}
                = v.{DatabaseMapping.ClientVisiting.ClientId}
            INNER JOIN {DatabaseMapping.States.Table} st
                ON st.{DatabaseMapping.States.Id}
                = c.{DatabaseMapping.ClientMaster.StateId}
            INNER JOIN {DatabaseMapping.LoginUsers.Table} lu
                ON lu.{DatabaseMapping.LoginUsers.Id}
                = v.{DatabaseMapping.ClientVisiting.UserId}";

        var filters = BuildFilters(
            model,
            "v." + DatabaseMapping.ClientVisiting.VisitDate,
            "v." + DatabaseMapping.ClientVisiting.UserId,
            "v." + DatabaseMapping.ClientVisiting.Status,
            null);

        using (var command = new SqlCommand())
        {
            command.Connection = connection;
            command.CommandTimeout = 0;

            ApplyFilters(command, filters);

            command.CommandText = $@"
                SELECT
                    v.{DatabaseMapping.ClientVisiting.VisitDate},
                    c.{DatabaseMapping.ClientMaster.ClientName},
                    st.{DatabaseMapping.States.StateName},
                    lu.{DatabaseMapping.LoginUsers.UserName},
                    v.{DatabaseMapping.ClientVisiting.VisitType},
                    v.{DatabaseMapping.ClientVisiting.PersonMet},
                    v.{DatabaseMapping.ClientVisiting.Subject},
                    v.{DatabaseMapping.ClientVisiting.Status},
                    v.{DatabaseMapping.ClientVisiting.NextAction},
                    v.{DatabaseMapping.ClientVisiting.FollowUpDate}
                {fromJoins}
                {WhereOf(filters)}
                ORDER BY
                    v.{DatabaseMapping.ClientVisiting.VisitDate} DESC,
                    v.{DatabaseMapping.ClientVisiting.Id} DESC";

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

        var byStatus = await GroupCountAsync(
            connection,
            fromJoins,
            "v." + DatabaseMapping.ClientVisiting.Status,
            filters);

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
}