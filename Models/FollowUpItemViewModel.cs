namespace Master.Models;

// One row of the Upcoming Follow-ups list (COMPONENT 9).
public class FollowUpItemViewModel
{
    public string ClientName { get; set; } = "";

    public string FollowUpDateText { get; set; } = "";

    public string SupportType { get; set; } = "";

    public string Subject { get; set; } = "";

    public string Status { get; set; } = "";

    // Existing .badge-* pill class for the Status column.
    public string StatusClass { get; set; } = "badge-default";

    public string UserName { get; set; } = "";
}