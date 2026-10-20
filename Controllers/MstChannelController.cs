using Master.Models;

namespace Master.Controllers;

// ============================================
// CHANNEL MASTER  (Mst_Channel)
// Phone / WhatsApp / Email / Walk-in
// ============================================

public class MstChannelController : LookupMasterControllerBase
{
    public MstChannelController(IConfiguration configuration)
        : base(configuration)
    {
    }

    protected override LookupMasterDefinition Definition => new()
    {
        TableName = "Mst_Channel",
        ControllerName = "MstChannel",
        Title = "Channel Master",
        Subtitle = "Support kaise aayi — Phone, WhatsApp, Email, Walk-in",
        IdColumn = "ChannelId",
        NameColumn = "ChannelName",
        NameLabel = "Channel Name",
        SortColumn = "SortOrder",
        ActiveColumn = "IsActive"
    };

    // DailySupport.ChannelId is value ko use karta hai.
    protected override (string Table, string Column)[] UsageRefs =>
        new[] { ("DailySupport", "ChannelId") };
}
