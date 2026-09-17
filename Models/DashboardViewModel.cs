using System.Collections.Generic;
using System.Linq;

namespace Master.Models;

// One reusable filter model for the whole dashboard.
// Every component (summary cards, daily support,
// client visiting, reports, etc.) reads the selected
// values from here instead of building its own model.
public class DashboardViewModel
{
    // ---- Quick Date Filter (COMPONENT 2) ----
    public DashboardDateFilter DateFilter { get; set; } =
        new DashboardDateFilter();


    // ---- Global filter selections (COMPONENT 3) ----
    public int? StateId { get; set; }

    public int? UserId { get; set; }

    public int? ClientId { get; set; }

    public string Status { get; set; } = "All";

    public string Priority { get; set; } = "All";


    // ---- Summary card counts (COMPONENT 4) ----
    public DashboardSummaryViewModel Summary { get; set; } =
        new DashboardSummaryViewModel();


    // ---- Support status analytics (COMPONENT 5) ----
    public DashboardSupportStatusViewModel SupportStatus { get; set; } =
        new DashboardSupportStatusViewModel();


    // ---- Support priority analytics (COMPONENT 6) ----
    public DashboardSupportPriorityViewModel SupportPriority { get; set; } =
        new DashboardSupportPriorityViewModel();


    // ---- Clients by state (COMPONENT 7) ----
    public DashboardClientsByStateViewModel ClientsByState { get; set; } =
        new DashboardClientsByStateViewModel();


    // ---- Monthly activity (COMPONENT 8) ----
    public DashboardMonthlyActivityViewModel MonthlyActivity { get; set; } =
        new DashboardMonthlyActivityViewModel();


    // ---- Upcoming Follow-ups (COMPONENT 9) ----
    public DashboardUpcomingFollowUpsViewModel UpcomingFollowUps { get; set; } =
        new DashboardUpcomingFollowUpsViewModel();


    // ---- Support tab (COMPONENT 10) ----
    public DashboardSupportTabViewModel SupportTab { get; set; } =
        new DashboardSupportTabViewModel();


    // ---- Visits tab (COMPONENT 11) ----
    public DashboardVisitsTabViewModel VisitsTab { get; set; } =
        new DashboardVisitsTabViewModel();


    // ---- Clients tab (COMPONENT 12) ----
    public DashboardClientsTabViewModel ClientsTab { get; set; } =
        new DashboardClientsTabViewModel();


    // ---- Dropdown lookup data ----
    public List<LookupOptionViewModel> States { get; set; } =
        new List<LookupOptionViewModel>();

    public List<LookupOptionViewModel> Users { get; set; } =
        new List<LookupOptionViewModel>();

    public List<LookupOptionViewModel> Clients { get; set; } =
        new List<LookupOptionViewModel>();


    // ---- Status / Priority names (DailySupport values,
    //      no dedicated database table) ----
    public static readonly string[] DailySupportStatuses =
    {
        "Open",
        "In Progress",
        "Pending",
        "Completed",
        "Cancelled"
    };

    public static readonly string[] DailySupportPriorities =
    {
        "Low",
        "Medium",
        "High",
        "Urgent"
    };

    private static readonly List<string> _statusOptions =
        new List<string> { "All" }
            .Concat(DailySupportStatuses)
            .ToList();

    private static readonly List<string> _priorityOptions =
        new List<string> { "All" }
            .Concat(DailySupportPriorities)
            .ToList();


    // Dropdown values including the "All" option.
    public IReadOnlyList<string> StatusOptions =>
        _statusOptions;

    public IReadOnlyList<string> PriorityOptions =>
        _priorityOptions;


    public static string NormalizeStatus(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "All";
        }

        return DailySupportStatuses.Contains(value)
            ? value
            : "All";
    }


    public static string NormalizePriority(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "All";
        }

        return DailySupportPriorities.Contains(value)
            ? value
            : "All";
    }


    // Drops any id that is not present in the lookup
    // list, so garbage ids can never be kept in the filter.
    public static int? NormalizeId(
        int? id,
        List<LookupOptionViewModel> options)
    {
        if (!id.HasValue)
        {
            return null;
        }

        return options.Any(o => o.Id == id.Value)
            ? id
            : null;
    }
}