using Master.Configuration;
using Master.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Master.Controllers;

[Authorize]
public class CrmLeadHistoryController : CrmControllerBase
{
    public CrmLeadHistoryController(
        IConfiguration configuration,
        DynamicTableService tableService)
        : base(configuration, tableService)
    {
    }

    protected override string TableName => "CrmLeadHistory";

    [HttpGet]
    public async Task<IActionResult> Index(string? search, int? leadId)
    {
        using SqlConnection connection = await OpenAsync();

        List<MasterColumnViewModel> fields =
            await LoadFieldsAsync(connection);

        var model = new CrmLeadHistoryViewModel
        {
            Fields = fields,
            FormFields = fields.Where(f => f.Editable).ToList(),
            Search = search,
            LeadId = leadId
        };

        model.TotalRecords = await ScalarIntAsync(
            connection,
            "SELECT COUNT(*) FROM [CrmLeadHistory]");

        string? extraWhere = leadId.HasValue && leadId.Value > 0
            ? $"s.[LeadId] = {leadId.Value}"
            : null;

        model.Events = await LoadGridAsync(
            connection, fields, search, extraWhere);

        if (leadId.HasValue && leadId.Value > 0)
        {
            using SqlCommand nameCommand = new SqlCommand(
                "SELECT [LeadName] FROM [CrmLead] WHERE [Id] = @Id",
                connection);

            nameCommand.CommandTimeout = 0;
            nameCommand.Parameters.AddWithValue("@Id", leadId.Value);

            object? name = await nameCommand.ExecuteScalarAsync();

            model.LeadName = name == null || name == DBNull.Value
                ? ""
                : name.ToString();
        }

        ViewBag.Search = search;

        return View(model);
    }
}
