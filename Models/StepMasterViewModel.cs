namespace Master.Models;

// ============================================
// STEP MASTER (New CRM pipeline steps)
// step_master table ka CRUD model.
// ============================================

public class StepMasterViewModel
{
    public List<StepMasterRow> Steps { get; set; } = new();

    // KPI
    public int TotalRecords { get; set; }

    public int ActiveRecords { get; set; }

    public int InActiveRecords { get; set; }

    // Jo step abhi leads par use ho raha hai (count).
    public int UsedRecords { get; set; }
}

public class StepMasterRow
{
    public int Id { get; set; }

    public string StepName { get; set; } = string.Empty;

    public int StepOrder { get; set; }

    public bool IsActive { get; set; } = true;

    // Is step par kitni active leads hain (delete guard).
    public int LeadCount { get; set; }

    public string StatusDisplay =>
        IsActive ? "Active" : "Inactive";

    // Row JSON (Edit modal prefill ke liye).
    public string ToJson() =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            Id,
            StepName,
            StepOrder,
            IsActive
        });
}
