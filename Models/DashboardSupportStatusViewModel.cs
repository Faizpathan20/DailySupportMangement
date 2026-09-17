namespace Master.Models;

// Support Status analytics for the Dashboard Overview tab (COMPONENT 5).
// Real DailySupport counts from the database behind the shared
// DashboardFilter (date + global filters) inside DashboardViewModel.
public class DashboardSupportStatusViewModel
{
    public int OpenCount { get; set; }

    public int InProgressCount { get; set; }

    public int PendingCount { get; set; }

    public int CompletedCount { get; set; }

    public int CancelledCount { get; set; }


    public int Total =>
        OpenCount
        + InProgressCount
        + PendingCount
        + CompletedCount
        + CancelledCount;


    // Share (0-100) of one status inside the stacked bar.
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