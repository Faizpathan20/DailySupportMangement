using Master.Models;

namespace Master.Controllers;

// ============================================
// CALL TYPE MASTER  (Mst_CallType)
// Inquiry / Complaint / Service / New Requirement
// ============================================

public class MstCallTypeController : LookupMasterControllerBase
{
    public MstCallTypeController(IConfiguration configuration)
        : base(configuration)
    {
    }

    protected override LookupMasterDefinition Definition => new()
    {
        TableName = "Mst_CallType",
        ControllerName = "MstCallType",
        Title = "Call Type Master",
        Subtitle = "Call ka type — Inquiry, Complaint, Service, Requirement",
        IdColumn = "CallTypeId",
        NameColumn = "CallTypeName",
        NameLabel = "Call Type Name",
        SortColumn = "SortOrder",
        ActiveColumn = "IsActive"
    };

    protected override (string Table, string Column)[] UsageRefs =>
        new[] { ("DailySupport", "CallTypeId") };
}
