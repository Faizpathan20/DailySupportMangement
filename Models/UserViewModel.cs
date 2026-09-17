namespace Master.Models;

public class UserViewModel
{
    public int Id { get; set; }

    public string UserName { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public string Status =>
        IsActive ? "Active" : "Non Active";
}
