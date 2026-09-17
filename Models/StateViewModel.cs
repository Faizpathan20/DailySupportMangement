namespace Master.Models;

public class StateViewModel
{
    public int Id { get; set; }

    public string StateName { get; set; } = string.Empty;

    public DateTime EntryOn { get; set; }

    public string UserName { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public string Status =>
        IsActive ? "Active" : "Non Active";
}