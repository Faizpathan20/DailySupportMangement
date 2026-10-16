namespace Master.Models;

public class CrmFollowUpViewModel : CrmFormViewModel
{
    public List<MasterRowViewModel> FollowUps { get; set; } = new();

    public int TotalRecords { get; set; }

    public int TodayCount { get; set; }

    public int PendingCount { get; set; }

    public int OverdueCount { get; set; }

    // Pending and dated today or earlier - the "my day" number.
    public int DueCount { get; set; }

    public int DoneCount { get; set; }

    public string? Search { get; set; }

    // all | due | today | pending | overdue | done
    public string Range { get; set; } = "due";

    // mine | team
    public string Scope { get; set; } = "mine";
}
