namespace Master.Models;

public class DailySupportViewModel
{
    public HashSet<string> AvailableColumns { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    public int TotalRecords { get; set; }

    // KPI cards (same labels as the reference Daily Support page)
    public int TodayCalls { get; set; }

    public int OpenCalls { get; set; }

    public int ClosedToday { get; set; }

    public int PendingCount { get; set; }

    public bool HasStatusKpi { get; set; }

    public List<MasterColumnViewModel> Fields { get; set; }
        = new();

    public List<MasterColumnViewModel> FormFields { get; set; }
        = new();

    public List<MasterRowViewModel> Supports { get; set; }
        = new();

    public List<LookupOptionViewModel> Clients { get; set; }
        = new();

    public Dictionary<string, string> FieldErrors { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> FormValues { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    public bool OpenEditModal { get; set; }

    public string EditPreviousStatus { get; set; } = "";

    public string GetFormValue(string name)
    {
        return FormValues.TryGetValue(
            name,
            out string? value)
                ? value
                : "";
    }

    public string FieldError(string name)
    {
        return FieldErrors.TryGetValue(
            name,
            out string? value)
                ? value
                : "";
    }
}