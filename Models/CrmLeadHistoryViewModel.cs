namespace Master.Models;

public class CrmLeadHistoryViewModel : CrmFormViewModel
{
    public List<MasterRowViewModel> Events { get; set; } = new();

    public int TotalRecords { get; set; }

    public string? Search { get; set; }

    public int? LeadId { get; set; }

    public string? LeadName { get; set; }
}
