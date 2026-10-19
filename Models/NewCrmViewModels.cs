namespace Master.Models;

// ============================================
// NEW CRM  -  Sales Lead Management
// ============================================

public class NewCrmLeadListItem
{
    public int Id { get; set; }

    public string LeadNo { get; set; } = string.Empty;

    public int ClientId { get; set; }

    public string ClientName { get; set; } = string.Empty;

    public string ClientMobile { get; set; } = string.Empty;

    // Lead ke apne contact details.
    public string ContactNo { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public int? StateId { get; set; }

    public string StateName { get; set; } = string.Empty;

    public string Requirement { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public decimal ExpectedValue { get; set; }

    public string Priority { get; set; } = "Med";

    public int CurrentStep { get; set; }

    public string StepName { get; set; } = string.Empty;

    public int StepOrder { get; set; }

    public string Status { get; set; } = "Open";

    public string LostReason { get; set; } = string.Empty;

    public DateTime? NextFollowupDate { get; set; }

    public string NextRemarks { get; set; } = string.Empty;

    public string AssignedTo { get; set; } = string.Empty;

    // Jo user login hai (creator) - "Lead By".
    public string LeadBy { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    // Reports: is lead par logged total meeting time
    // (selected date range ke andar). Baaki pages par 0 rehta hai.
    public int TimeMinutes { get; set; }

    public int TimeEntries { get; set; }

    // "3 · 1h 20m" / "1h 20m" / "-"
    public string TimeDisplay
    {
        get
        {
            if (TimeEntries == 0)
            {
                return "-";
            }

            int h = TimeMinutes / 60;
            int m = TimeMinutes % 60;
            string dur = h > 0 ? $"{h}h {m}m" : $"{m}m";

            return TimeEntries > 1
                ? $"{TimeEntries} · {dur}"
                : dur;
        }
    }

    // Overdue: Open lead jiski next follow-up date
    // aaj se pehle ki hai (ya blank).
    public bool IsOverdue =>
        Status == "Open"
        && NextFollowupDate.HasValue
        && NextFollowupDate.Value.Date < DateTime.Today;
}

public class NewCrmStepItem
{
    public int Id { get; set; }

    public string StepName { get; set; } = string.Empty;

    public int StepOrder { get; set; }
}

public class NewCrmTimelineItem
{
    public DateTime EventDate { get; set; }

    public string Kind { get; set; } = "FollowUp";

    public string Title { get; set; } = string.Empty;

    public string Remarks { get; set; } = string.Empty;

    public string By { get; set; } = string.Empty;
}

public class NewCrmLeadIndexModel
{
    public List<NewCrmLeadListItem> Leads { get; set; } = new();

    public List<NewCrmStepItem> Steps { get; set; } = new();

    // KPI
    public int Total { get; set; }

    public int Open { get; set; }

    public int Won { get; set; }

    public int Lost { get; set; }

    public int Overdue { get; set; }

    public decimal WonValue { get; set; }

    // Filters
    public string? Search { get; set; }

    public string? StatusFilter { get; set; }

    public string? StepFilter { get; set; }

    public string? PriorityFilter { get; set; }
}

public class NewCrmLeadDetailModel
{
    public NewCrmLeadListItem Lead { get; set; } = new();

    public List<NewCrmStepItem> Steps { get; set; } = new();

    public List<NewCrmTimelineItem> Timeline { get; set; } = new();

    // Reassign dropdown ke liye.
    public List<(int Id, string Name)> Users { get; set; } = new();
}

public class NewCrmFunnelRow
{
    public string StepName { get; set; } = string.Empty;

    public int StepOrder { get; set; }

    public int Count { get; set; }

    public decimal Value { get; set; }
}

public class NewCrmTeamRow
{
    public string UserName { get; set; } = string.Empty;

    public int Total { get; set; }

    public int Open { get; set; }

    public int Won { get; set; }

    public int Lost { get; set; }

    public decimal WonValue { get; set; }
}

public class NewCrmDashboardModel
{
    public int Total { get; set; }

    public int Open { get; set; }

    public int Won { get; set; }

    public int Lost { get; set; }

    public int Overdue { get; set; }

    public int DueToday { get; set; }

    public decimal WonValue { get; set; }

    public decimal OpenValue { get; set; }

    public List<NewCrmFunnelRow> Funnel { get; set; } = new();

    public List<NewCrmLeadListItem> OverdueLeads { get; set; } = new();

    public List<NewCrmTeamRow> Team { get; set; } = new();
}
