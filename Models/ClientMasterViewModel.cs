namespace Master.Models;

public class ClientMasterViewModel
{
    public int TotalRecords { get; set; }

    public int LastWeekRecords { get; set; }

    public int ActiveRecords { get; set; }

    public int NonActiveRecords { get; set; }

    public bool HasLastWeekKpi { get; set; }

    public bool HasActiveKpi { get; set; }

    public List<MasterColumnViewModel> Fields { get; set; }
        = new();

    public List<MasterColumnViewModel> FormFields { get; set; }
        = new();

    public List<MasterRowViewModel> Clients { get; set; }
        = new();
}