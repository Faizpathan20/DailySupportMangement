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

    // ============================================
    // METADATA (populated from the database by
    // DynamicTableService; views and grid ignore
    // these unless a control needs them).
    // ============================================

    public string? SqlType { get; set; }

    public int? MaxLength { get; set; }

    public bool IsPrimaryKey { get; set; }

    public bool IsIdentity { get; set; }

    public bool HasDefault { get; set; }

    public string? LookupRefColumn { get; set; }

    // Write convention for system columns:
    //   null      -> value comes from the form
    //   "auth-user" -> value comes from the signed-in user (UserId)
    //   "true"    -> always written as 1 (IsActive)
    public string? AutoWrite { get; set; }
}