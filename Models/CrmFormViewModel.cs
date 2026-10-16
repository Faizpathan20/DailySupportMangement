namespace Master.Models;

// Shared base for every CRM create/edit form.
public abstract class CrmFormViewModel
{
    public List<MasterColumnViewModel> Fields { get; set; } = new();

    public List<MasterColumnViewModel> FormFields { get; set; } = new();

    public Dictionary<string, string> FormValues { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> FieldErrors { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    public bool OpenEditModal { get; set; }

    public string GetFormValue(string name) =>
        FormValues.TryGetValue(name, out var value)
            ? value ?? ""
            : "";

    public string FieldError(string name) =>
        FieldErrors.TryGetValue(name, out var value)
            ? value ?? ""
            : "";
}
