using System.Collections.Generic;

namespace Master.Models;

// Upcoming Follow-ups panel (COMPONENT 9).
// DailySupport rows with an upcoming FollowUpDate (not Cancelled),
// behind the shared DashboardFilter. Shows the next few rows
// (Client / Follow-up Date / Support Type / Subject / Status / User)
// plus the total number of upcoming follow-ups.
public class DashboardUpcomingFollowUpsViewModel
{
    public List<FollowUpItemViewModel> Items { get; set; } =
        new List<FollowUpItemViewModel>();

    public int TotalUpcoming { get; set; }
}