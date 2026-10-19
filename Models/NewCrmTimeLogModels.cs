namespace Master.Models;

// ============================================
// NEW CRM  -  TIME LOG (Meeting In / Out)
// lead_time_log table ka tracking model.
// ============================================

public class NewCrmTimeLogItem
{
    public int Id { get; set; }

    public int LeadId { get; set; }

    public string LeadNo { get; set; } = string.Empty;

    public string ClientName { get; set; } = string.Empty;

    public int UserId { get; set; }

    public string UserName { get; set; } = string.Empty;

    // Call / Meeting / Visit
    public string MeetingType { get; set; } = "Meeting";

    public DateTime MeetingIn { get; set; }

    public DateTime? MeetingOut { get; set; }

    public int? DurationMin { get; set; }

    public string Notes { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    // IST (UTC+5:30) — SQL server UTC chalta hai.
    private static DateTime NowIst =>
        DateTime.UtcNow.AddMinutes(330);

    // LIVE: meeting_out NULL = abhi chal rahi hai.
    public bool IsLive => !MeetingOut.HasValue;

    // Duration display: "1h 30m" / "45m" / "LIVE 00:24:15".
    public string DurationDisplay
    {
        get
        {
            if (IsLive)
            {
                var live = NowIst - MeetingIn;

                if (live.TotalHours >= 1)
                {
                    return $"⏱ LIVE {live.Hours:D2}:{live.Minutes:D2}:{live.Seconds:D2}";
                }

                return $"⏱ LIVE {live.Minutes:D2}:{live.Seconds:D2}";
            }

            int total = DurationMin ?? 0;

            int h = total / 60;
            int m = total % 60;

            return h > 0 ? $"{h}h {m}m" : $"{m}m";
        }
    }

    // Total seconds (JS timer sync ke liye).
    public double LiveSeconds =>
        IsLive
            ? (NowIst - MeetingIn).TotalSeconds
            : 0;
}

public class NewCrmTimeLogIndexModel
{
    public List<NewCrmTimeLogItem> Entries { get; set; } = new();

    // ---- LIVE timer (current user) ----
    public NewCrmTimeLogItem? ActiveTimer { get; set; }

    // ---- KPIs ----
    public int TotalEntries { get; set; }

    // Aaj ka total time (minutes) - sab users ka.
    public int TodayMinutes { get; set; }

    // Is hafte ka total (Monday se).
    public int WeekMinutes { get; set; }

    // Average per meeting (completed).
    public double AvgMinutes { get; set; }

    // ---- Daily report (date -> user -> entries) ----
    public List<NewCrmTimeLogDayGroup> DailyReport { get; set; } = new();

    // ---- Filters ----
    public DateTime? FromDate { get; set; }

    public DateTime? ToDate { get; set; }

    public string? UserFilter { get; set; }

    public string? Search { get; set; }

    public int? LeadId { get; set; } // Leads list se aaye toh filter.

    // ---- Dropdowns ----
    public List<(int Id, string Name)> Users { get; set; } = new();

    // Lead dropdown (Log Time modal) - lead_master se.
    public List<NewCrmTimeLogLeadItem> Leads { get; set; } = new();
}

// Log Time modal ke dropdown ke liye lead row.
public class NewCrmTimeLogLeadItem
{
    public int Id { get; set; }

    public string LeadNo { get; set; } = string.Empty;

    public string ClientName { get; set; } = string.Empty;

    // "LEAD-0005 · Reliance"
    public string Display =>
        string.IsNullOrWhiteSpace(ClientName)
            ? LeadNo
            : LeadNo + " · " + ClientName;
}

// Daily report ke liye ek din ka group.
public class NewCrmTimeLogDayGroup
{
    public DateTime Date { get; set; }

    public int TotalMinutes { get; set; }

    public string TotalDisplay
    {
        get
        {
            int h = TotalMinutes / 60;
            int m = TotalMinutes % 60;

            return h > 0 ? $"{h}h {m}m" : $"{m}m";
        }
    }

    // User-wise breakdown.
    public List<NewCrmTimeLogUserGroup> ByUser { get; set; } = new();
}

public class NewCrmTimeLogUserGroup
{
    public string UserName { get; set; } = string.Empty;

    public int TotalMinutes { get; set; }

    public string TotalDisplay
    {
        get
        {
            int h = TotalMinutes / 60;
            int m = TotalMinutes % 60;

            return h > 0 ? $"{h}h {m}m" : $"{m}m";
        }
    }

    public List<NewCrmTimeLogItem> Entries { get; set; } = new();
}
