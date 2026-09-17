namespace Master.Models;

public class ClientViewModel
{
    public int Id { get; set; }

    public string ClientName { get; set; } = string.Empty;

    public string ContactPerson { get; set; } = string.Empty;

    public string MobileNo { get; set; } = string.Empty;

    public string AlternateMobileNo { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;

    public DateTime EntryOn { get; set; }

    public string StateName { get; set; } = string.Empty;

    public int StateId { get; set; }

    public string UserName { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public string Status =>
        IsActive ? "Active" : "Non Active";
}