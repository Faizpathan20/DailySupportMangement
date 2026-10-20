namespace Master.Models;

// ============================================
// LOOKUP MASTER (Daily Support ke dropdown masters)
// Mst_Channel / Mst_CallType / Mst_Category /
// Mst_SubCategory / Mst_Priority / Mst_Status
//
// Ek hi ViewModel + ek hi View se saare 6 masters
// chalte hain. Har controller apni "definition"
// (table, columns, labels) pass karta hai.
// ============================================

public class LookupMasterViewModel
{
    // Controller se aata hai — page ka title, table name etc.
    public LookupMasterDefinition Definition { get; set; } = new();

    public List<LookupMasterRow> Rows { get; set; } = new();

    // Extra options (SubCategory ke liye parent Category list).
    public List<LookupOption> ParentOptions { get; set; } = new();

    // KPI
    public int TotalRecords { get; set; }
    public int ActiveRecords { get; set; }
    public int InActiveRecords { get; set; }
    public int UsedRecords { get; set; }
}

// Controller batata hai ye page kis master ka hai.
public class LookupMasterDefinition
{
    // e.g. "Mst_Channel"
    public string TableName { get; set; } = string.Empty;

    // Controller name for form posts (e.g. "MstChannel").
    public string ControllerName { get; set; } = string.Empty;

    // Page heading (e.g. "Channel Master").
    public string Title { get; set; } = string.Empty;

    // Sub-heading (e.g. "Phone, WhatsApp, Email, Walk-in").
    public string Subtitle { get; set; } = string.Empty;

    // Identity / name / sort / active column names.
    public string IdColumn { get; set; } = string.Empty;
    public string NameColumn { get; set; } = string.Empty;
    public string SortColumn { get; set; } = "SortOrder";
    public bool HasSortOrder { get; set; } = true;
    public bool HasIsActive { get; set; } = true;
    public string ActiveColumn { get; set; } = "IsActive";

    // Name column ka label (e.g. "Channel Name").
    public string NameLabel { get; set; } = "Name";

    // Extra numeric column (e.g. Priority ka SlaHours). Null = nahi.
    public string? ExtraNumberColumn { get; set; }
    public string ExtraNumberLabel { get; set; } = "";
    public int ExtraNumberDefault { get; set; } = 0;

    // Extra bool column (e.g. Status ka IsClosed). Null = nahi.
    public string? ExtraBoolColumn { get; set; }
    public string ExtraBoolLabel { get; set; } = "";

    // Parent FK column (e.g. SubCategory ka CategoryId). Null = nahi.
    public string? ParentColumn { get; set; }
    public string? ParentTable { get; set; }
    public string? ParentIdColumn { get; set; }
    public string? ParentNameColumn { get; set; }
    public string ParentLabel { get; set; } = "";
    public string? ParentControllerName { get; set; }
}

public class LookupMasterRow
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    // Optional extra fields
    public int? ExtraNumber { get; set; }
    public bool? ExtraBool { get; set; }

    // Optional parent (SubCategory -> Category)
    public int? ParentId { get; set; }
    public string ParentName { get; set; } = string.Empty;

    // Kitni tickets is value ko use kar rahe hain (delete guard).
    public int UsageCount { get; set; }

    public string StatusDisplay => IsActive ? "Active" : "Inactive";

    // Row JSON (Edit modal prefill ke liye).
    public string ToJson() =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            Id,
            Name,
            SortOrder,
            IsActive,
            ExtraNumber,
            ExtraBool,
            ParentId
        });
}

// Dropdown option (parent list ke liye).
public class LookupOption
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
