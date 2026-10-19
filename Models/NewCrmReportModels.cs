namespace Master.Models;

public class NewCrmReportKpi
{
    public int Total { get; set; }
    public int Open { get; set; }
    public int Won { get; set; }
    public int Lost { get; set; }
    public decimal WinRate { get; set; }
    public decimal WonValue { get; set; }
    public decimal OpenValue { get; set; }
    public int Overdue { get; set; }
    public int DueToday { get; set; }
}

public class NewCrmReportModel
{
    // Filters
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? Status { get; set; }
    public string? LeadBy { get; set; }
    public string? Source { get; set; }
    public string? Priority { get; set; }

    // Data
    public NewCrmReportKpi Kpi { get; set; } = new();

    public List<NewCrmLeadListItem> DetailLeads { get; set; } = new();
    public List<NewCrmLeadListItem> OverdueLeads { get; set; } = new();
    public List<NewCrmLeadListItem> DueTodayLeads { get; set; } = new();

    // Filter lists
    public List<string> Statuses { get; set; } = new() { "All", "Open", "Won", "Lost" };
    public List<string> Priorities { get; set; } = new() { "All", "Low", "Med", "High" };
    public List<string> Sources { get; set; } = new();
    public List<string> LeadByList { get; set; } = new();
}