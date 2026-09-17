using System;

namespace Master.Models;

public enum DateFilterType
{
    Today,
    Last7Days,
    Last30Days,
    ThisMonth,
    LastMonth,
    AllTime,
    Custom
}


// Reusable date context for every dashboard
// component (summary cards, daily support,
// client visiting, reports, etc.).
// All components should read FromDate / ToDate / FilterType
// from one of these instead of building their own logic.
public class DashboardDateFilter
{
    // Which quick filter the user picked.
    public DateFilterType FilterType { get; set; }

    // Raw custom selections (only used when Custom).
    public DateTime? CustomFromDate { get; set; }

    public DateTime? CustomToDate { get; set; }

    // Resolved inclusive range (dates at midnight).
    // FromDate / ToDate are null for All Time.
    public DateTime? FromDate { get; set; }

    public DateTime? ToDate { get; set; }


    // Upper bound that is EXCLUSIVE, i.e. the day after ToDate.
    // Useful for SQL:  [col] >= FromDate AND [col] < ToDateExclusive
    public DateTime? ToDateExclusive =>
        ToDate.HasValue
            ? ToDate.Value.Date.AddDays(1)
            : (DateTime?)null;


    // Short human-readable summary used by the View.
    public string DisplayValue
    {
        get
        {
            if (FilterType == DateFilterType.AllTime)
            {
                return "All Time";
            }

            if (!FromDate.HasValue || !ToDate.HasValue)
            {
                return string.Empty;
            }

            if (FromDate.Value.Date == ToDate.Value.Date)
            {
                return FromDate.Value.ToString("dd MMM yyyy");
            }

            return
                $"{FromDate.Value:dd MMM yyyy}"
                + " – "
                + $"{ToDate.Value:dd MMM yyyy}";
        }
    }


    // Central place for ALL date-range logic.
    // Receives the raw query values from the controller
    // and produces the resolved FromDate / ToDate.
    public static DashboardDateFilter Resolve(
        string? filterType,
        DateTime? from,
        DateTime? to)
    {
        var today = DateTime.Today;

        var result = new DashboardDateFilter
        {
            FilterType = ParseFilterType(filterType),
            CustomFromDate = (from ?? today).Date,
            CustomToDate = (to ?? today).Date
        };

        switch (result.FilterType)
        {
            case DateFilterType.Today:
                result.FromDate = today;
                result.ToDate = today;
                break;

            case DateFilterType.Last7Days:
                // today + previous 6 days
                result.FromDate = today.AddDays(-6);
                result.ToDate = today;
                break;

            case DateFilterType.Last30Days:
                // today + previous 29 days
                result.FromDate = today.AddDays(-29);
                result.ToDate = today;
                break;

            case DateFilterType.ThisMonth:
                result.FromDate =
                    new DateTime(
                        today.Year,
                        today.Month,
                        1);
                result.ToDate = today;
                break;

            case DateFilterType.LastMonth:
                var firstOfLastMonth =
                    new DateTime(
                            today.Year,
                            today.Month,
                            1)
                        .AddMonths(-1);

                result.FromDate = firstOfLastMonth;

                result.ToDate = new DateTime(
                    firstOfLastMonth.Year,
                    firstOfLastMonth.Month,
                    DateTime.DaysInMonth(
                        firstOfLastMonth.Year,
                        firstOfLastMonth.Month));
                break;

            case DateFilterType.Custom:
                var customFrom = (from ?? today).Date;
                var customTo = (to ?? today).Date;

                if (customFrom > customTo)
                {
                    (customFrom, customTo) = (customTo, customFrom);
                }

                result.FromDate = customFrom;
                result.ToDate = customTo;
                break;

            case DateFilterType.AllTime:
            default:
                result.FromDate = null;
                result.ToDate = null;
                break;
        }

        return result;
    }


    private static DateFilterType ParseFilterType(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            // Default landing state = All Time.
            return DateFilterType.AllTime;
        }

        if (Enum.TryParse(
                value,
                true,
                out DateFilterType parsed))
        {
            return parsed;
        }

        return DateFilterType.AllTime;
    }
}