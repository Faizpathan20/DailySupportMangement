namespace Master.Models;

public class ClientVisitingViewModel
{
    public HashSet<string> AvailableColumns { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    public int TotalRecords { get; set; }

    public int PlannedCount { get; set; }

    public int CompletedCount { get; set; }

    public int CancelledCount { get; set; }

    public bool HasStatusKpi { get; set; }

    public List<MasterColumnViewModel> Fields { get; set; }
        = new();

    public List<MasterColumnViewModel> FormFields { get; set; }
        = new();

    public List<MasterRowViewModel> Visits { get; set; }
        = new();

    public List<LookupOptionViewModel> Clients { get; set; }
        = new();

    public List<LookupOptionViewModel> Meetings { get; set; }
        = new();

    public Dictionary<string, string> FieldErrors { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> FormValues { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    public string OpenModalMode { get; set; } = "";

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