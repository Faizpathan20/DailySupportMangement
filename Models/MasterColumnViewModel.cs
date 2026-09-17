namespace Master.Models;

public class MasterColumnViewModel
{
    public string Name { get; set; } = string.Empty;

    public string Display { get; set; } = string.Empty;

    public string Type { get; set; } = "text";

    public string SortType { get; set; } = "text";

    public string Control { get; set; } = "input";

    public string InputType { get; set; } = "text";

    public bool Required { get; set; }

    public bool Editable { get; set; }

    public bool ShowInTable { get; set; } = true;

    public bool CreateOnly { get; set; }

    public string? LookupKey { get; set; }

    public double Width { get; set; } = 10;
}