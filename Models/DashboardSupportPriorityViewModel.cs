namespace Master.Models;

// Support Priority analytics for the Dashboard Overview tab (COMPONENT 6).
// Real DailySupport counts from the database behind the shared
// DashboardFilter (date + global filters) inside DashboardViewModel.
public class DashboardSupportPriorityViewModel
{
    public int LowCount { get; set; }

    public int MediumCount { get; set; }

    public int HighCount { get; set; }

    public int UrgentCount { get; set; }


    public int Total =>
        LowCount
        + MediumCount
        + HighCount
        + UrgentCount;


    // Share (0-100) of one priority inside the stacked bar.
    // Returns an InvariantCulture string so Razor always renders
    // "12.5" never "12,5".
    public string Percent(int value)
    {
        if (Total <= 0)
        {
            return "0";
        }

        return (value * 100.0 / Total)
            .ToString(
                "0.##",
                System.Globalization.CultureInfo
                    .InvariantCulture);
    }
}