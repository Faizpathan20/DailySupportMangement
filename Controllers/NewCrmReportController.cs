using Master.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Master.Controllers;

[Authorize]
public class NewCrmReportController : NewCrmControllerBase
{
    public NewCrmReportController(IConfiguration configuration)
        : base(configuration) { }

    [HttpGet]
    public async Task<IActionResult> Index(
        DateTime? fromDate,
        DateTime? toDate,
        string? status,
        string? leadBy,
        string? source,
        string? priority)
    {
        using SqlConnection connection = await OpenAsync();
        var model = new NewCrmReportModel
        {
            FromDate = fromDate,
            ToDate = toDate,
            Status = status,
            LeadBy = leadBy,
            Source = source,
            Priority = priority
        };

        string dateWhereBase = BuildDateWhere(model, out List<(string, object)> dp);

        // KPIs
        await LoadKpis(connection, model, dateWhereBase, dp);

        // Detail
        model.DetailLeads = await LoadDetail(connection, model, dateWhereBase, dp);
        model.OverdueLeads = await LoadOverdue(connection, model);
        model.DueTodayLeads = await LoadDueToday(connection, model);

        // Per-lead logged time (same date filters).
        await LoadLeadTimes(connection, model);

        // Filter lists
        model.Sources = await LoadDistinct(connection, "source", "lead_master");
        model.LeadByList = await LoadDistinctUsers(connection);

        return View(model);
    }

    // ============================================
    // PER-LEAD LOGGED TIME (Reports table ke liye)
    // meeting_in = IST (UTC+5:30) store hoti hai.
    // Same From/To date filter lagta hai.
    // ============================================

    private async Task LoadLeadTimes(
        SqlConnection conn, NewCrmReportModel m)
    {
        if (m.DetailLeads.Count == 0)
        {
            return;
        }

        var conditions = new List<string> { "t.[lead_id] IS NOT NULL" };
        var pars = new List<(string, object)>();

        if (m.FromDate.HasValue)
        {
            conditions.Add("t.[meeting_in] >= @TFrom");
            pars.Add(("@TFrom", m.FromDate.Value.Date));
        }
        if (m.ToDate.HasValue)
        {
            conditions.Add("t.[meeting_in] < @TTo");
            pars.Add(("@TTo", m.ToDate.Value.Date.AddDays(1)));
        }

        string where = string.Join(" AND ", conditions);

        string q = $@"
            SELECT t.[lead_id] AS LeadId,
                COUNT(*) AS Entries,
                ISNULL(SUM(ISNULL(t.[duration_min],
                    DATEDIFF(MINUTE, t.[meeting_in],
                        DATEADD(MINUTE, 330, SYSUTCDATETIME())))), 0) AS TotalMin
            FROM [lead_time_log] t
            WHERE {where}
            GROUP BY t.[lead_id]";

        var map = new Dictionary<int, (int Entries, int Minutes)>();

        using (SqlCommand cmd = new(q, conn))
        {
            cmd.CommandTimeout = 0;
            foreach (var (n, v) in pars) cmd.Parameters.AddWithValue(n, v);

            using SqlDataReader r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                map[ReadInt(r, "LeadId")] = (
                    ReadInt(r, "Entries"),
                    Convert.ToInt32(r["TotalMin"]));
            }
        }

        foreach (var lead in m.DetailLeads)
        {
            if (map.TryGetValue(lead.Id, out var t))
            {
                lead.TimeEntries = t.Entries;
                lead.TimeMinutes = t.Minutes;
            }
        }
    }

    private string BuildDateWhere(NewCrmReportModel m, out List<(string, object)> p)
    {
        p = new();
        List<string> c = new();
        if (m.FromDate.HasValue)
        {
            c.Add("l.created_at >= @From");
            p.Add(("@From", m.FromDate.Value.Date));
        }
        if (m.ToDate.HasValue)
        {
            c.Add("l.created_at < @To");
            p.Add(("@To", m.ToDate.Value.Date.AddDays(1)));
        }
        if (!string.IsNullOrWhiteSpace(m.Status) && m.Status != "All" &&
            (m.Status == "Open" || m.Status == "Won" || m.Status == "Lost"))
        {
            c.Add("l.status = @St");
            p.Add(("@St", m.Status));
        }
        if (!string.IsNullOrWhiteSpace(m.LeadBy))
            { c.Add("ISNULL(cb.UserName,'') = @Lb"); p.Add(("@Lb", m.LeadBy)); }
        if (!string.IsNullOrWhiteSpace(m.Source))
            { c.Add("ISNULL(l.source,'') = @Src"); p.Add(("@Src", m.Source)); }
        if (!string.IsNullOrWhiteSpace(m.Priority) && m.Priority != "All")
            { c.Add("ISNULL(l.priority,'') = @Pr"); p.Add(("@Pr", m.Priority)); }
        return c.Count == 0 ? "1=1" : string.Join(" AND ", c);
    }

    private async Task LoadKpis(SqlConnection conn, NewCrmReportModel m, string dw,
        List<(string,object)> dp)
    {
        string q = $@"
            SELECT
                COUNT(*) Total,
                SUM(CASE WHEN l.status='Open' THEN 1 ELSE 0 END) OpenCount,
                SUM(CASE WHEN l.status='Won' THEN 1 ELSE 0 END) WonCount,
                SUM(CASE WHEN l.status='Lost' THEN 1 ELSE 0 END) LostCount,
                ISNULL(SUM(CASE WHEN l.status='Won' THEN l.expected_value ELSE 0 END),0) WonValue,
                ISNULL(SUM(CASE WHEN l.status='Open' THEN l.expected_value ELSE 0 END),0) OpenValue,
                SUM(CASE WHEN l.status='Open' AND l.next_followup_date IS NOT NULL
                    AND l.next_followup_date < CAST(GETDATE() AS DATE) THEN 1 ELSE 0 END) Overdue,
                SUM(CASE WHEN l.status='Open' AND l.next_followup_date = CAST(GETDATE() AS DATE)
                    THEN 1 ELSE 0 END) DueToday
            FROM lead_master l
            LEFT JOIN LoginUsers cb ON cb.Id = l.created_by
            LEFT JOIN step_master s ON s.id = l.current_step
            LEFT JOIN States st ON st.Id = l.state_id
            WHERE l.is_active=1 AND {dw}";
        using SqlCommand cmd = new(q, conn); cmd.CommandTimeout=0;
        foreach(var (n,v) in dp) cmd.Parameters.AddWithValue(n,v);
        using SqlDataReader r = await cmd.ExecuteReaderAsync();
        if(await r.ReadAsync())
        {
            var k = m.Kpi;
            k.Total = ReadInt(r,"Total");
            k.Open = ReadInt(r,"OpenCount");
            k.Won = ReadInt(r,"WonCount");
            k.Lost = ReadInt(r,"LostCount");
            k.WonValue = r["WonValue"]==DBNull.Value?0:Convert.ToDecimal(r["WonValue"]);
            k.OpenValue = r["OpenValue"]==DBNull.Value?0:Convert.ToDecimal(r["OpenValue"]);
            k.Overdue = ReadInt(r,"Overdue");
            k.DueToday = ReadInt(r,"DueToday");
            k.WinRate = k.Total==0?0: Math.Round((k.Won/(decimal)k.Total)*100,2);
        }
    }

    private async Task<List<NewCrmLeadListItem>> LoadDetail(SqlConnection conn, NewCrmReportModel m,
        string dw, List<(string,object)> dp)
    {
        string q = $@"
            SELECT
                l.[id], l.[lead_no], l.[client_id], c.[ClientName], c.[MobileNo],
                l.[contact_no], l.[email], l.[city], l.[state_id], st.[StateName],
                l.[requirement], l.[source], l.[expected_value], l.[priority],
                l.[current_step], s.[step_name], s.[step_order], l.[status],
                l.[lost_reason], l.[next_followup_date], l.[next_remarks],
                u.[UserName] AS assigned_name, cb.[UserName] AS lead_by, l.[created_at]
            FROM lead_master l
            LEFT JOIN ClientMaster c ON c.Id=l.client_id
            LEFT JOIN LoginUsers u ON u.Id=l.assigned_user_id
            LEFT JOIN LoginUsers cb ON cb.Id=l.created_by
            LEFT JOIN step_master s ON s.id=l.current_step
            LEFT JOIN States st ON st.Id=l.state_id
            WHERE l.is_active=1 AND {dw}
            ORDER BY l.created_at DESC";
        var list = new List<NewCrmLeadListItem>();
        using SqlCommand cmd = new(q,conn); cmd.CommandTimeout=0;
        foreach(var (n,v) in dp) cmd.Parameters.AddWithValue(n,v);
        using SqlDataReader r = await cmd.ExecuteReaderAsync();
        while(await r.ReadAsync()) list.Add(ReadLead(r));
        return list;
    }

    private async Task<List<NewCrmLeadListItem>> LoadOverdue(SqlConnection conn, NewCrmReportModel m)
    {
        string q = @"
            SELECT
                l.[id], l.[lead_no], l.[client_id], c.[ClientName], c.[MobileNo],
                l.[contact_no], l.[email], l.[city], l.[state_id], st.[StateName],
                l.[requirement], l.[source], l.[expected_value], l.[priority],
                l.[current_step], s.[step_name], s.[step_order], l.[status],
                l.[lost_reason], l.[next_followup_date], l.[next_remarks],
                u.[UserName] AS assigned_name, cb.[UserName] AS lead_by, l.[created_at]
            FROM lead_master l
            LEFT JOIN ClientMaster c ON c.Id=l.client_id
            LEFT JOIN LoginUsers u ON u.Id=l.assigned_user_id
            LEFT JOIN LoginUsers cb ON cb.Id=l.created_by
            LEFT JOIN step_master s ON s.id=l.current_step
            LEFT JOIN States st ON st.Id=l.state_id
            WHERE l.is_active=1 AND l.status='Open'
              AND l.next_followup_date IS NOT NULL
              AND l.next_followup_date < CAST(GETDATE() AS DATE)
            ORDER BY l.next_followup_date ASC";
        var list = new List<NewCrmLeadListItem>();
        using SqlCommand cmd = new(q,conn); cmd.CommandTimeout=0;
        using SqlDataReader r = await cmd.ExecuteReaderAsync();
        while(await r.ReadAsync()) list.Add(ReadLead(r));
        return list;
    }

    private async Task<List<NewCrmLeadListItem>> LoadDueToday(SqlConnection conn, NewCrmReportModel m)
    {
        string q = @"
            SELECT
                l.[id], l.[lead_no], l.[client_id], c.[ClientName], c.[MobileNo],
                l.[contact_no], l.[email], l.[city], l.[state_id], st.[StateName],
                l.[requirement], l.[source], l.[expected_value], l.[priority],
                l.[current_step], s.[step_name], s.[step_order], l.[status],
                l.[lost_reason], l.[next_followup_date], l.[next_remarks],
                u.[UserName] AS assigned_name, cb.[UserName] AS lead_by, l.[created_at]
            FROM lead_master l
            LEFT JOIN ClientMaster c ON c.Id=l.client_id
            LEFT JOIN LoginUsers u ON u.Id=l.assigned_user_id
            LEFT JOIN LoginUsers cb ON cb.Id=l.created_by
            LEFT JOIN step_master s ON s.id=l.current_step
            LEFT JOIN States st ON st.Id=l.state_id
            WHERE l.is_active=1 AND l.status='Open'
              AND l.next_followup_date = CAST(GETDATE() AS DATE)
            ORDER BY l.next_followup_date ASC";
        var list = new List<NewCrmLeadListItem>();
        using SqlCommand cmd = new(q,conn); cmd.CommandTimeout=0;
        using SqlDataReader r = await cmd.ExecuteReaderAsync();
        while(await r.ReadAsync()) list.Add(ReadLead(r));
        return list;
    }

    private async Task<List<string>> LoadDistinct(SqlConnection conn, string col, string tbl)
    {
        var list = new List<string>();
        string q = $"SELECT DISTINCT ISNULL({col},'') v FROM {tbl} WHERE ISNULL({col},'') <> '' ORDER BY v";
        using SqlCommand cmd = new(q,conn); cmd.CommandTimeout=0;
        using SqlDataReader r = await cmd.ExecuteReaderAsync();
        while(await r.ReadAsync()) list.Add(ToStr(r["v"]));
        return list;
    }

    private async Task<List<string>> LoadDistinctUsers(SqlConnection conn)
    {
        var list = new List<string>();
        string q = @"SELECT DISTINCT ISNULL(cb.UserName,'') v
                      FROM lead_master l
                      LEFT JOIN LoginUsers cb ON cb.Id=l.created_by
                      WHERE l.is_active=1 AND ISNULL(cb.UserName,'') <> ''
                      ORDER BY v";
        using SqlCommand cmd = new(q,conn); cmd.CommandTimeout=0;
        using SqlDataReader r = await cmd.ExecuteReaderAsync();
        while(await r.ReadAsync()) list.Add(ToStr(r["v"]));
        return list;
    }
}