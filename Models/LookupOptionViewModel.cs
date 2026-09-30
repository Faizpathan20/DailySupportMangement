namespace Master.Models;

public class LookupOptionViewModel
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;


    // ---- Cascade relationship keys ----
    // Filter dropdowns are cascading: each option carries
    // the foreign keys that decide whether it is still a
    // valid choice when the sibling filters are narrowed.
    //
    //   States        -> ParentId = States.UserId
    //   ClientMaster  -> ParentId = ClientMaster.UserId
    //                    GroupId  = ClientMaster.StateId
    //
    // These stay null for plain lookup lists (foreign-key
    // dropdowns on the Create forms), which simply ignore
    // the cascade.
    public int? ParentId { get; set; }

    public int? GroupId { get; set; }
}