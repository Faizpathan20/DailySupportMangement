namespace Master.Models;

// Summary card counts for the Dashboard Overview tab (COMPONENT 4).
// Every value is computed from the existing database while honouring
// the reusable DashboardFilter (quick date + global filters) that lives
// inside DashboardViewModel. Cards with no logical filter keep their
// plain totals (see the controller for the per-card filter matrix).
public class DashboardSummaryViewModel
{
    // ---- Row 1 ----
    public int TotalUsers { get; set; }

    public int TotalClients { get; set; }

    public int TotalSupport { get; set; }

    public int TotalVisits { get; set; }

    // ---- Row 2 ----
    public int PendingCount { get; set; }

    public int InProgressCount { get; set; }

    public int FollowUpCount { get; set; }

    public int ActiveStates { get; set; }
}