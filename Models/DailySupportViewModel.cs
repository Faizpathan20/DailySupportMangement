namespace Master.Models;

public class DailySupportViewModel
{
    public HashSet<string> AvailableColumns { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    public int TotalRecords { get; set; }

    // KPI cards (same labels as the reference Daily Support page)
    public int TodayCalls { get; set; }

    public int OpenCalls { get; set; }

    public int ClosedToday { get; set; }

    public int PendingCount { get; set; }

    // Naye KPI cards (ticketing system)
    public int PendingReviewCount { get; set; }

    public int OverdueCount { get; set; }

    // On Hold tickets (KPI card)
    public int OnHoldCount { get; set; }

    // ---- List page filters ----
    public int? FilterStatusId { get; set; }

    public int? FilterAssignedTo { get; set; }

    public int? FilterPriorityId { get; set; }

    public string? FilterView { get; set; }

    public string? Search { get; set; }

    public List<TicketOption> StatusOptions { get; set; } = new();

    public List<TicketOption> PriorityOptions { get; set; } = new();

    public List<TicketOption> UserOptions { get; set; } = new();

    // Logged-in user ke permissions.
    // IsAdmin -> Assigned To dropdown dikhta hai aur saare tickets dikhte hain;
    // baaki users sirf apne assigned tickets dekhte hain.
    public bool IsAdmin { get; set; }

    public int? CurrentUserId { get; set; }

    // Ticket id -> review pill ("pending" | "approved" | "rejected").
    // Index table me Ticket No ke aage dikhta hai.
    public Dictionary<int, string> ReviewPills { get; set; } = new();

    // ---- Filter cascade maps (real tickets se) ----
    // Ye batate hain kaun sa combo DailySupport me
    // actually maujood hai, taake dropdown aapas me
    // connect ho jaayein. Key = parent id,
    // Value = uske saath allowed sibling ids.

    public Dictionary<int, List<int>> UsersByStatus { get; set; } = new();

    public Dictionary<int, List<int>> PrioritiesByStatus { get; set; } = new();

    public Dictionary<int, List<int>> StatusesByUser { get; set; } = new();

    public Dictionary<int, List<int>> PrioritiesByUser { get; set; } = new();

    public Dictionary<int, List<int>> StatusesByPriority { get; set; } = new();

    public Dictionary<int, List<int>> UsersByPriority { get; set; } = new();

    public bool HasStatusKpi { get; set; }

    public List<MasterColumnViewModel> Fields { get; set; }
        = new();

    public List<MasterColumnViewModel> FormFields { get; set; }
        = new();

    public List<MasterRowViewModel> Supports { get; set; }
        = new();

    public List<LookupOptionViewModel> Clients { get; set; }
        = new();

    public Dictionary<string, string> FieldErrors { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> FormValues { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    public bool OpenEditModal { get; set; }

    public string EditPreviousStatus { get; set; } = "";

    public string GetFormValue(string name)
    {
        return FormValues.TryGetValue(
            name,
            out string? value)
                ? value
                : "";
    }

    public string FieldError(string name)
    {
        return FieldErrors.TryGetValue(
            name,
            out string? value)
                ? value
                : "";
    }
}