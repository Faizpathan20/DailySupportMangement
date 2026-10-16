namespace Master.Models;

public class CrmDashboardViewModel
{
    public int TotalLeads { get; set; }

    public int TodayFollowUps { get; set; }

    public int PendingFollowUps { get; set; }

    public int OverdueFollowUps { get; set; }

    // Pending and dated today or earlier, across the whole team.
    public int DueFollowUps { get; set; }

    public int ConvertedLeads { get; set; }

    public int NewThisMonth { get; set; }

    // Leads that are still open but have no upcoming follow-up.
    public int NoNextFollowUp { get; set; }

    // Open leads with no activity for over a week.
    public int StaleLeads { get; set; }

    public List<MasterRowViewModel> RecentLeads { get; set; } = new();

    public List<MasterRowViewModel> TodayFollowUpsList { get; set; } = new();

    public List<MasterRowViewModel> NoNextFollowUpList { get; set; } = new();

    public List<MasterRowViewModel> StaleLeadsList { get; set; } = new();

    public List<CrmStatusCount> StatusBreakdown { get; set; } = new();

    public List<CrmStatusCount> SourceBreakdown { get; set; } = new();
}

public class CrmStatusCount
{
    public string Status { get; set; } = "";

    public int Count { get; set; }
}
