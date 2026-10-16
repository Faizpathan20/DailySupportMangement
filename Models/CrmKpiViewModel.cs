namespace Master.Models;

// Modern KPI tile used by every CRM page (see
// Views/Shared/_CrmKpi.cshtml).
public class CrmKpiViewModel
{
    public string Label { get; set; } = "";

    public string Value { get; set; } = "0";

    // Short icon key resolved to an inline SVG in the partial.
    public string Icon { get; set; } = "chart";

    // Hex accent colour driving the left bar + icon chip.
    public string Accent { get; set; } = "#2563eb";

    // Optional soft background for the icon chip.
    public string Soft { get; set; } = "#eff6ff";

    // Optional destination; renders the whole tile as a link.
    public string? Href { get; set; }

    // Refresh key: written to [data-kpi] so masterRefreshTable /
    // crm.js can update the value in place after a save.
    public string? Kpi { get; set; }
}
