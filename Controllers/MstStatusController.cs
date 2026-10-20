using Master.Models;

namespace Master.Controllers;

// ============================================
// STATUS MASTER  (Mst_Status)
// Open / In Progress / On Hold / Pending Review /
// Resolved / Closed + IsClosed flag
// ============================================

public class MstStatusController : LookupMasterControllerBase
{
    public MstStatusController(IConfiguration configuration)
        : base(configuration)
    {
    }

    protected override LookupMasterDefinition Definition => new()
    {
        TableName = "Mst_Status",
        ControllerName = "MstStatus",
        Title = "Status Master",
        Subtitle = "Ticket ke stages — Open se Closed tak",
        IdColumn = "StatusId",
        NameColumn = "StatusName",
        NameLabel = "Status Name",
        SortColumn = "SortOrder",
        ActiveColumn = "IsActive",

        // IsClosed = ticket band ho gaya
        ExtraBoolColumn = "IsClosed",
        ExtraBoolLabel = "Closes Ticket"
    };

    protected override (string Table, string Column)[] UsageRefs =>
        new[] { ("DailySupport", "StatusId") };
}
