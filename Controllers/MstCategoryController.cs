using Master.Models;

namespace Master.Controllers;

// ============================================
// CATEGORY MASTER  (Mst_Category)
// Hardware / Software / Networking / Billing / Printer
// ============================================

public class MstCategoryController : LookupMasterControllerBase
{
    public MstCategoryController(IConfiguration configuration)
        : base(configuration)
    {
    }

    protected override LookupMasterDefinition Definition => new()
    {
        TableName = "Mst_Category",
        ControllerName = "MstCategory",
        Title = "Category Master",
        Subtitle = "Issue ki category — Hardware, Software, Networking...",
        IdColumn = "CategoryId",
        NameColumn = "CategoryName",
        NameLabel = "Category Name",
        SortColumn = "SortOrder",
        ActiveColumn = "IsActive"
    };

    protected override (string Table, string Column)[] UsageRefs =>
        new[]
        {
            ("DailySupport", "CategoryId"),
            ("Mst_SubCategory", "CategoryId")
        };
}
