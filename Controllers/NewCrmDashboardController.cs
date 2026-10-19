using Master.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Master.Controllers;

[Authorize]
public class NewCrmDashboardController : NewCrmControllerBase
{
    public NewCrmDashboardController(IConfiguration configuration)
        : base(configuration)
    {
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        using SqlConnection connection = await OpenAsync();

        var model = new NewCrmDashboardModel();

        // ---- KPI ----
        const string kpiQuery = @"
            SELECT
                COUNT(*) AS Total,
                SUM(CASE WHEN [status] = 'Open' THEN 1 ELSE 0 END) AS OpenCount,
                SUM(CASE WHEN [status] = 'Won'  THEN 1 ELSE 0 END) AS WonCount,
                SUM(CASE WHEN [status] = 'Lost' THEN 1 ELSE 0 END) AS LostCount,
                SUM(CASE
                    WHEN [status] = 'Open'
                     AND [next_followup_date] IS NOT NULL
                     AND [next_followup_date] < CAST(GETDATE() AS DATE)
                    THEN 1 ELSE 0 END) AS OverdueCount,
                SUM(CASE
                    WHEN [status] = 'Open'
                     AND [next_followup_date] = CAST(GETDATE() AS DATE)
                    THEN 1 ELSE 0 END) AS DueTodayCount,
                ISNULL(SUM(CASE WHEN [status] = 'Won'
                    THEN [expected_value] ELSE 0 END), 0) AS WonValue,
                ISNULL(SUM(CASE WHEN [status] = 'Open'
                    THEN [expected_value] ELSE 0 END), 0) AS OpenValue
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
                model.DueToday = ReadInt(reader, "DueTodayCount");
                model.WonValue = ReadDec(reader, "WonValue");
                model.OpenValue = ReadDec(reader, "OpenValue");
            }
        }

        // ---- Funnel: step-wise open lead count ----
        const string funnelQuery = @"
            SELECT
                s.[step_name],
                s.[step_order],
                COUNT(l.[id]) AS lead_count,
                ISNULL(SUM(l.[expected_value]), 0) AS lead_value
            FROM [step_master] s
            LEFT JOIN [lead_master] l
                ON l.[current_step] = s.[id]
               AND l.[is_active] = 1
               AND l.[status] = 'Open'
            WHERE s.[is_active] = 1
            GROUP BY s.[id], s.[step_name], s.[step_order]
            ORDER BY s.[step_order]";

        using (SqlCommand command =
               new SqlCommand(funnelQuery, connection))
        {
            command.CommandTimeout = 0;

            using SqlDataReader reader =
                await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                model.Funnel.Add(new NewCrmFunnelRow
                {
                    StepName = ToStr(reader["step_name"]),
                    StepOrder = Convert.ToInt32(reader["step_order"]),
                    Count = ReadInt(reader, "lead_count"),
                    Value = ReadDec(reader, "lead_value")
                });
            }
        }

        // ---- Overdue leads (top 15) ----
        List<NewCrmLeadListItem> leads =
            await LoadLeadsAsync(
                connection,
                "l.[status] = 'Open' " +
                "AND l.[next_followup_date] IS NOT NULL " +
                "AND l.[next_followup_date] < CAST(GETDATE() AS DATE)");

        model.OverdueLeads = leads.Take(15).ToList();

        // ---- Team performance ----
        const string teamQuery = @"
            SELECT
                u.[UserName],
                COUNT(l.[id]) AS Total,
                SUM(CASE WHEN l.[status] = 'Open' THEN 1 ELSE 0 END) AS OpenCount,
                SUM(CASE WHEN l.[status] = 'Won'  THEN 1 ELSE 0 END) AS WonCount,
                SUM(CASE WHEN l.[status] = 'Lost' THEN 1 ELSE 0 END) AS LostCount,
                ISNULL(SUM(CASE WHEN l.[status] = 'Won'
                    THEN l.[expected_value] ELSE 0 END), 0) AS WonValue
            FROM [LoginUsers] u
            INNER JOIN [lead_master] l
                ON l.[assigned_user_id] = u.[Id]
               AND l.[is_active] = 1
            GROUP BY u.[Id], u.[UserName]
            ORDER BY WonCount DESC, Total DESC";

        using (SqlCommand command =
               new SqlCommand(teamQuery, connection))
        {
            command.CommandTimeout = 0;

            using SqlDataReader reader =
                await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                model.Team.Add(new NewCrmTeamRow
                {
                    UserName = ToStr(reader["UserName"]),
                    Total = ReadInt(reader, "Total"),
                    Open = ReadInt(reader, "OpenCount"),
                    Won = ReadInt(reader, "WonCount"),
                    Lost = ReadInt(reader, "LostCount"),
                    WonValue = ReadDec(reader, "WonValue")
                });
            }
        }

        return View(model);
    }

    // ReadInt: NewCrmControllerBase se milta hai.

    private static decimal ReadDec(
        SqlDataReader reader, string column) =>
        reader[column] == DBNull.Value
            ? 0
            : Convert.ToDecimal(reader[column]);
}
