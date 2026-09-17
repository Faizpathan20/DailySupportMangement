using System.Globalization;

namespace Master.Models;

// One month column of the Monthly Activity chart (COMPONENT 8).
// The bar aggregates DailySupport + ClientVisiting activity for
// the shared DashboardFilter; per-table counts are kept so the
// tooltip can show the breakdown.
public class MonthlyActivityPointViewModel
{
    public string MonthLabel { get; set; } = "";

    public string YearLabel { get; set; } = "";

    public int SupportCount { get; set; }

    public int VisitingCount { get; set; }


    // Combined activity for the month (heights + header total).
    public int Count =>
        SupportCount + VisitingCount;


    // Column-bar height (0-100) relative to the busiest month.
    // Returns an InvariantCulture string so Razor always renders
    // "12.5" never "12,5".
    public string BarHeight(int maxCount)
    {
        if (maxCount <= 0)
        {
            return "0";
        }

        return (Count * 100.0 / maxCount)
            .ToString(
                "0.##",
                CultureInfo.InvariantCulture);
    }
}