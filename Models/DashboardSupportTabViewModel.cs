using System.Collections.Generic;

namespace Master.Models;

// Support tab of the Dashboard (COMPONENT 10) — a management /
// analytics view, NOT a CRUD duplicate. Real DailySupport counts
// behind the shared DashboardFilter; the status/priority breakdowns
// reuse the Overview components, recent records come from a dedicated
// TOP-6 query. Add/Edit lives in Sidebar > Daily Support.
public class DashboardSupportTabViewModel
{
    // Status distribution (Total + Open/In Progress/Pending/
    // Completed/Cancelled). Date/State/User/Client/Priority filters
    // apply; the Status filter is intentionally ignored so the bar
    // always shows the true distribution (same as Component 5).
    public DashboardSupportStatusViewModel Status { get; set; } =
        new DashboardSupportStatusViewModel();

    // Priority distribution. All filters apply (same as Component 6).
    public DashboardSupportPriorityViewModel Priority { get; set; } =
        new DashboardSupportPriorityViewModel();

    // Newest records matching every Dashboard filter.
    public List<RecentSupportItemViewModel> Recent { get; set; } =
        new List<RecentSupportItemViewModel>();
}