using Master.Models;

namespace Master.Controllers;

// ============================================
// PRIORITY MASTER  (Mst_Priority)
// Urgent / High / Medium / Low + SLA hours
// ============================================

public class MstPriorityController : LookupMasterControllerBase
{
    public MstPriorityController(IConfiguration configuration)
        : base(configuration)
    {
    }

    protected override LookupMasterDefinition Definition => new()
    {
        TableName = "Mst_Priority",
        ControllerName = "MstPriority",
        Title = "Priority Master",
        Subtitle = "Priority aur uska SLA time (kitne ghante me solve karna hai)",
        IdColumn = "PriorityId",
        NameColumn = "PriorityName",
        NameLabel = "Priority Name",
        SortColumn = "SortOrder",
        ActiveColumn = "IsActive",

        // SLA hours
        ExtraNumberColumn = "SlaHours",
        ExtraNumberLabel = "SLA Hours",
        ExtraNumberDefault = 24
    };

    protected override (string Table, string Column)[] UsageRefs =>
        new[] { ("DailySupport", "PriorityId") };
}
