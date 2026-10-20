using Master.Models;

namespace Master.Controllers;

// ============================================
// SUB CATEGORY MASTER  (Mst_SubCategory)
// Har SubCategory ek Category ke andar aati hai.
// ============================================

public class MstSubCategoryController : LookupMasterControllerBase
{
    public MstSubCategoryController(IConfiguration configuration)
        : base(configuration)
    {
    }

    protected override LookupMasterDefinition Definition => new()
    {
        TableName = "Mst_SubCategory",
        ControllerName = "MstSubCategory",
        Title = "Sub Category Master",
        Subtitle = "Category ke andar ki detail — installation, toner, LAN...",
        IdColumn = "SubCategoryId",
        NameColumn = "SubCategoryName",
        NameLabel = "Sub Category Name",
        SortColumn = "SortOrder",
        ActiveColumn = "IsActive",

        // Parent = Category
        ParentColumn = "CategoryId",
        ParentTable = "Mst_Category",
        ParentIdColumn = "CategoryId",
        ParentNameColumn = "CategoryName",
        ParentLabel = "Category",
        ParentControllerName = "MstCategory"
    };

    protected override (string Table, string Column)[] UsageRefs =>
        new[] { ("DailySupport", "SubCategoryId") };
}
