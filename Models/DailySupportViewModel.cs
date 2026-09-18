namespace Master.Models;

public class DailySupportViewModel
{
    public HashSet<string> AvailableColumns { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    public int TotalRecords { get; set; }

    public int OpenCount { get; set; }

    public int InProgressCount { get; set; }

    public int CompletedCount { get; set; }

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