using Master.Configuration;
using Master.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Master.Controllers;

[Authorize]
public class CrmDashboardController : CrmControllerBase
{
    public CrmDashboardController(
        IConfiguration configuration,
        DynamicTableService tableService)
        : base(configuration, tableService)
    {
    }

    protected override string TableName => "CrmLead";

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        using SqlConnection connection = await OpenAsync();

        var model = new CrmDashboardViewModel();

        // ---- Lead KPIs ----
        const string leadKpi = @"
            SELECT
                COUNT(*) AS TotalLeads,
                SUM(CASE WHEN [Status] = 'Converted'
                    THEN 1 ELSE 0 END) AS ConvertedLeads,
                SUM(CASE WHEN [EntryOn] >=
                    DATEADD(DAY, 1 - DAY(GETDATE()),
                        CAST(CAST(GETDATE() AS DATE) AS DATETIME))
                    THEN 1 ELSE 0 END) AS NewThisMonth
            FROM [CrmLead]";

        using (SqlCommand command = new SqlCommand(leadKpi, connection))
        {
            command.CommandTimeout = 0;

            using SqlDataReader reader =
                await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                model.TotalLeads = ReadInt(reader, "TotalLeads");
                model.ConvertedLeads = ReadInt(reader, "ConvertedLeads");
                model.NewThisMonth = ReadInt(reader, "NewThisMonth");
            }
        }

        // ---- Follow-up KPIs ----
        const string followKpi = @"
            SELECT
                SUM(CASE WHEN [Status] = 'Pending'
                    AND CAST([FollowUpDate] AS DATE)
                        = CAST(GETDATE() AS DATE)
                    THEN 1 ELSE 0 END) AS TodayFollowUps,
                SUM(CASE WHEN [Status] = 'Pending'
                    THEN 1 ELSE 0 END) AS PendingFollowUps,
                SUM(CASE WHEN [Status] = 'Pending'
                    AND CAST([FollowUpDate] AS DATE)
                        < CAST(GETDATE() AS DATE)
                    THEN 1 ELSE 0 END) AS OverdueFollowUps,
                SUM(CASE WHEN [Status] = 'Pending'
                    AND CAST([FollowUpDate] AS DATE)
                        <= CAST(GETDATE() AS DATE)
                    THEN 1 ELSE 0 END) AS DueFollowUps
            FROM [CrmFollowUp]";

        using (SqlCommand command = new SqlCommand(followKpi, connection))
        {
            command.CommandTimeout = 0;

            using SqlDataReader reader =
                await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                model.TodayFollowUps = ReadInt(reader, "TodayFollowUps");
                model.PendingFollowUps = ReadInt(reader, "PendingFollowUps");
                model.OverdueFollowUps = ReadInt(reader, "OverdueFollowUps");
                model.DueFollowUps = ReadInt(reader, "DueFollowUps");
            }
        }

        // ---- Breakdowns ----
        model.StatusBreakdown = await LoadBreakdownAsync(
            connection, "CrmLead", "Status");

        model.SourceBreakdown = await LoadBreakdownAsync(
            connection, "CrmLead", "Source");

        // ---- Lists ----
        List<MasterColumnViewModel> leadFields =
            await LoadFieldsAsync(connection);

        model.RecentLeads = (await LoadGridAsync(
                connection, leadFields, null))
            .Take(8)
            .ToList();

        List<MasterColumnViewModel> followFields =
            await TableService.GetTableFieldsAsync(
                connection, "CrmFollowUp");

        model.TodayFollowUpsList = (await LoadGridAsync(
                connection,
                followFields,
                null,
                "CAST(s.[FollowUpDate] AS DATE) = CAST(GETDATE() AS DATE)",
                "CrmFollowUp"))
            .Take(10)
            .ToList();

        // ---- Follow-up discipline watchlists ----

        // Open leads with nothing scheduled next.
        const string noNextWhere = @"
            s.[IsActive] = 1
            AND s.[Status] NOT IN ('Converted', 'Lost')
            AND s.[NextFollowUpDate] IS NULL
            AND NOT EXISTS (
                SELECT 1 FROM [CrmFollowUp] f
                WHERE f.[LeadId] = s.[Id] AND f.[Status] = 'Pending')";

        model.NoNextFollowUp = await ScalarIntAsync(
            connection,
            "SELECT COUNT(*) FROM [CrmLead] s "
            + "WHERE " + noNextWhere);

        model.NoNextFollowUpList = (await LoadGridAsync(
                connection, leadFields, null, noNextWhere))
            .Take(6)
            .ToList();

        // Open leads untouched for more than a week and with no
        // upcoming follow-up - the ones most likely to go cold.
        const string staleWhere = @"
            s.[IsActive] = 1
            AND s.[Status] NOT IN ('Converted', 'Lost')
            AND COALESCE(
                    (SELECT MAX(h.[EventDate])
                     FROM [CrmLeadHistory] h
                     WHERE h.[LeadId] = s.[Id]),
                    s.[EntryOn])
                < DATEADD(DAY, -7, GETDATE())
            AND (s.[NextFollowUpDate] IS NULL
                 OR CAST(s.[NextFollowUpDate] AS DATE)
                    <= CAST(GETDATE() AS DATE))";

        model.StaleLeads = await ScalarIntAsync(
            connection,
            "SELECT COUNT(*) FROM [CrmLead] s "
            + "WHERE " + staleWhere);

        model.StaleLeadsList = (await LoadGridAsync(
                connection, leadFields, null, staleWhere))
            .Take(6)
            .ToList();

        return View(model);
    }

    private static async Task<List<CrmStatusCount>> LoadBreakdownAsync(
        SqlConnection connection,
        string table,
        string column)
    {
        var list = new List<CrmStatusCount>();

        string query =
            $"SELECT [{column}] AS Label, COUNT(*) AS Cnt "
            + $"FROM [{table}] GROUP BY [{column}] ORDER BY Cnt DESC";

        using SqlCommand command = new SqlCommand(query, connection);
        command.CommandTimeout = 0;

        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            list.Add(new CrmStatusCount
            {
                Status = reader["Label"] == DBNull.Value
                    ? ""
                    : reader["Label"].ToString() ?? "",
                Count = Convert.ToInt32(reader["Cnt"])
            });
        }

        return list;
    }

    private static int ReadInt(SqlDataReader reader, string column) =>
        reader[column] == DBNull.Value
            ? 0
            : Convert.ToInt32(reader[column]);
}
