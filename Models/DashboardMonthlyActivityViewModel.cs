using System.Collections.Generic;
using System.Linq;

namespace Master.Models;

// Monthly Activity panel (COMPONENT 8).
// DailySupport grouped by month behind the shared DashboardFilter.
// Shows the months of the selected date range (capped to the most
// recent 12); months without records are rendered as empty bars.
public class DashboardMonthlyActivityViewModel
{
    public List<MonthlyActivityPointViewModel> Months { get; set; } =
        new List<MonthlyActivityPointViewModel>();


    // Sum of the displayed months (header "X activities").
    public int Total =>
        Months.Sum(m => m.Count);


    // Busiest month (drives the relative bar heights).
    public int MaxCount =>
        Months.Count == 0
            ? 0
            : Months.Max(m => m.Count);
}