using System.Collections.Generic;

namespace Master.Models;

// Visits tab of the Dashboard (COMPONENT 11) — a management /
// analytics view, NOT a CRUD duplicate. Real ClientVisiting counts
// behind the shared DashboardFilter; Add/Edit lives in the
// Sidebar > Client Visiting module.
public class DashboardVisitsTabViewModel
{
    // Total ClientVisiting rows for the shared filters.
    public int TotalVisits { get; set; }

    // Visits whose VisitDate is today.
    public int TodayVisits { get; set; }

    // Visits whose VisitDate falls in the current calendar month.
    public int ThisMonthVisits { get; set; }

    // Visits with VisitDate >= today and not Cancelled.
    public int UpcomingVisits { get; set; }

    // Newest records matching every Dashboard filter.
    public List<RecentVisitItemViewModel> Recent { get; set; } =
        new List<RecentVisitItemViewModel>();
}