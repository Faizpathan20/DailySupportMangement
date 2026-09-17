namespace Master.Models;

// One row of the Recent Support records table in the
// Dashboard Support tab (COMPONENT 10).
public class RecentSupportItemViewModel
{
    public string SupportDateText { get; set; } = "";

    public string ClientName { get; set; } = "";

    public string UserName { get; set; } = "";

    public string SupportType { get; set; } = "";

    public string Subject { get; set; } = "";

    public string Status { get; set; } = "";

    // Existing .badge-* pill class for the Status column.
    public string StatusClass { get; set; } = "badge-default";

    public string Priority { get; set; } = "";

    // .prio-* pill class for the Priority column.
    public string PriorityClass { get; set; } = "prio-default";

    public string FollowUpDateText { get; set; } = "";
}