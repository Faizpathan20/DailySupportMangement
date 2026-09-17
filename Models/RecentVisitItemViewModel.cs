namespace Master.Models;

// One row of the Recent Visits table in the
// Dashboard Visits tab (COMPONENT 11).
public class RecentVisitItemViewModel
{
    public string VisitDateText { get; set; } = "";

    public string ClientName { get; set; } = "";

    public string UserName { get; set; } = "";

    public string VisitType { get; set; } = "";

    public string Subject { get; set; } = "";

    public string Status { get; set; } = "";

    // Existing .badge-* pill class for the Status column.
    public string StatusClass { get; set; } = "badge-default";

    public string FollowUpDateText { get; set; } = "";
}