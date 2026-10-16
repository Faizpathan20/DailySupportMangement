namespace Master.Models;

public class CrmLeadViewModel : CrmFormViewModel
{
    public List<MasterRowViewModel> Leads { get; set; } = new();

    public int TotalRecords { get; set; }

    public int NewCount { get; set; }

    public int ContactedCount { get; set; }

    public int QualifiedCount { get; set; }

    public int NegotiationCount { get; set; }

    public int ConvertedCount { get; set; }

    public int LostCount { get; set; }

    public string? Search { get; set; }

    public string? StatusFilter { get; set; }

    public string? SourceFilter { get; set; }
}
