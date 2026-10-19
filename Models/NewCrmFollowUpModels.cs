namespace Master.Models;

// ============================================
// NEW CRM  -  FOLLOW-UP PAGE
// Daily follow-up workflow ke liye models.
// ============================================

public class NewCrmFollowUpIndexModel
{
    // Tab: overdue | due | upcoming | done
    public string Tab { get; set; } = "due";

    public string? UserFilter { get; set; }

    // Open leads jinki next_followup_date as per tab.
    public List<NewCrmLeadListItem> Leads { get; set; } = new();

    // Done tab: lead_followup log (status = Done).
    public List<NewCrmFollowUpDoneRow> DoneRows { get; set; } = new();

    // KPIs
    public int OverdueCount { get; set; }

    public int DueTodayCount { get; set; }

    public int UpcomingCount { get; set; }

    public int DoneTodayCount { get; set; }

    // Filter dropdown
    public List<(int Id, string Name)> Users { get; set; } = new();
}

public class NewCrmFollowUpDoneRow
{
    public int Id { get; set; }

    public int LeadId { get; set; }

    public string LeadNo { get; set; } = string.Empty;

    public string ClientName { get; set; } = string.Empty;

    public string ContactNo { get; set; } = string.Empty;

    public DateTime FollowupDate { get; set; }

    public string Remarks { get; set; } = string.Empty;

    public DateTime? DoneOn { get; set; }

    public string DoneByName { get; set; } = string.Empty;
}
