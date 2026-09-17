namespace Master.Models;

// One row of the Clients Overview table in the
// Dashboard Clients tab (COMPONENT 12).
public class RecentClientItemViewModel
{
    public string ClientName { get; set; } = "";

    public string StateName { get; set; } = "";

    public string EntryDateText { get; set; } = "";

    public string UserName { get; set; } = "";

    public bool IsActive { get; set; }

    // "Active" / "Non Active" — matches the module display.
    public string StatusText { get; set; } = "";

    // Existing .status-active / .status-inactive pill class.
    public string StatusClass { get; set; } = "status-active";

    public string MobileNo { get; set; } = "";

    public string Email { get; set; } = "";
}