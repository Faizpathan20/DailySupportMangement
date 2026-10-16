using Master.Configuration;
using Master.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Master.Controllers;

[Authorize]
public class CrmLeadController : CrmControllerBase
{
    public CrmLeadController(
        IConfiguration configuration,
        DynamicTableService tableService)
        : base(configuration, tableService)
    {
    }

    protected override string TableName => "CrmLead";

    // ============================================
    // LIST
    // ============================================

    [HttpGet]
    public async Task<IActionResult> Index(
        string? search,
        string? status,
        string? source)
    {
        using SqlConnection connection = await OpenAsync();

        List<MasterColumnViewModel> fields =
            await LoadFieldsAsync(connection);

        var model = new CrmLeadViewModel
        {
            Fields = fields,
            FormFields = fields.Where(f => f.Editable).ToList(),
            Search = search,
            StatusFilter = status,
            SourceFilter = source
        };

        // ---- KPI ----
        const string kpiQuery = @"
            SELECT
                COUNT(*) AS TotalRecords,
                SUM(CASE WHEN [Status] = 'New' THEN 1 ELSE 0 END) AS NewCount,
                SUM(CASE WHEN [Status] = 'Contacted' THEN 1 ELSE 0 END) AS ContactedCount,
                SUM(CASE WHEN [Status] = 'Qualified' THEN 1 ELSE 0 END) AS QualifiedCount,
                SUM(CASE WHEN [Status] = 'Negotiation' THEN 1 ELSE 0 END) AS NegotiationCount,
                SUM(CASE WHEN [Status] = 'Converted' THEN 1 ELSE 0 END) AS ConvertedCount,
                SUM(CASE WHEN [Status] = 'Lost' THEN 1 ELSE 0 END) AS LostCount
            FROM [CrmLead]";

        using (SqlCommand kpiCommand =
            new SqlCommand(kpiQuery, connection))
        {
            kpiCommand.CommandTimeout = 0;

            using SqlDataReader kpiReader =
                await kpiCommand.ExecuteReaderAsync();

            if (await kpiReader.ReadAsync())
            {
                model.TotalRecords = ReadInt(kpiReader, "TotalRecords");
                model.NewCount = ReadInt(kpiReader, "NewCount");
                model.ContactedCount = ReadInt(kpiReader, "ContactedCount");
                model.QualifiedCount = ReadInt(kpiReader, "QualifiedCount");
                model.NegotiationCount = ReadInt(kpiReader, "NegotiationCount");
                model.ConvertedCount = ReadInt(kpiReader, "ConvertedCount");
                model.LostCount = ReadInt(kpiReader, "LostCount");
            }
        }

        // ---- Filter (whitelisted values only) ----
        var conditions = new List<string>();

        var statuses = DynamicTableService
            .GetStaticOptions("CrmLead", "Status").ToList();
        var sources = DynamicTableService
            .GetStaticOptions("CrmLead", "Source").ToList();

        if (!string.IsNullOrWhiteSpace(status)
            && statuses.Contains(status))
        {
            conditions.Add($"s.[Status] = '{status.Replace("'", "''")}'");
        }

        if (!string.IsNullOrWhiteSpace(source)
            && sources.Contains(source))
        {
            conditions.Add($"s.[Source] = '{source.Replace("'", "''")}'");
        }

        model.Leads = await LoadGridAsync(
            connection,
            fields,
            search,
            conditions.Count > 0
                ? string.Join(" AND ", conditions)
                : null);

        (var dropdowns, var staticOptions) =
            await TableService.LoadFormOptionsAsync(
                connection,
                TableName,
                model.FormFields);

        ViewBag.Dropdowns = dropdowns;
        ViewBag.StaticOptions = staticOptions;
        ViewBag.Search = search;
        ViewBag.Statuses = statuses;
        ViewBag.Sources = sources;

        return View(model);
    }

    // ============================================
    // LEAD DETAIL + TIMELINE
    // ============================================

    [HttpGet]
    public async Task<IActionResult> Detail(int id)
    {
        using SqlConnection connection = await OpenAsync();

        List<MasterColumnViewModel> fields =
            await LoadFieldsAsync(connection);

        List<MasterRowViewModel> rows =
            await LoadGridAsync(
                connection,
                fields,
                null,
                $"s.[Id] = {id}");

        MasterRowViewModel? lead = rows.FirstOrDefault();

        if (lead == null)
        {
            return RedirectToAction(nameof(Index));
        }

        var model = new CrmLeadViewModel
        {
            Fields = fields,
            FormFields = fields.Where(f => f.Editable).ToList(),
            Leads = rows
        };

        (var dropdowns, var staticOptions) =
            await TableService.LoadFormOptionsAsync(
                connection,
                TableName,
                model.FormFields);

        ViewBag.Dropdowns = dropdowns;
        ViewBag.StaticOptions = staticOptions;
        ViewBag.Lead = lead;
        ViewBag.LeadId = id;

        ViewBag.FollowUps = await LoadChildRowsAsync(
            connection, "CrmFollowUp", id);

        ViewBag.History = await LoadChildRowsAsync(
            connection, "CrmLeadHistory", id);

        // ---- metadata for the inline "Add follow-up" modal ----
        List<MasterColumnViewModel> followFields =
            await TableService.GetTableFieldsAsync(
                connection, "CrmFollowUp");

        var followModel = new CrmFollowUpViewModel
        {
            Fields = followFields,
            FormFields = followFields.Where(f => f.Editable).ToList()
        };

        followModel.FormValues["LeadId"] = id.ToString();
        followModel.FormValues["Status"] = "Pending";
        followModel.FormValues["FollowUpDate"] =
            DateTime.Today.ToString("yyyy-MM-dd");

        (var followDropdowns, var followStatic) =
            await TableService.LoadFormOptionsAsync(
                connection,
                "CrmFollowUp",
                followModel.FormFields);

        ViewBag.FollowUpModel = followModel;
        ViewBag.FollowUpDropdowns = followDropdowns;
        ViewBag.FollowUpStaticOptions = followStatic;

        return View(model);
    }

    private async Task<List<MasterRowViewModel>> LoadChildRowsAsync(
        SqlConnection connection,
        string childTable,
        int leadId)
    {
        List<MasterColumnViewModel> childFields =
            await TableService.GetTableFieldsAsync(
                connection,
                childTable);

        return await LoadGridAsync(
            connection,
            childFields,
            null,
            $"s.[LeadId] = {leadId}",
            childTable);
    }

    // ============================================
    // SAVE (insert / update)
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(IFormCollection form, int id)
    {
        try
        {
            return await SaveCoreAsync(form, id);
        }
        catch (Exception ex)
        {
            return Respond(false, "Save failed: " + ex.Message);
        }
    }

    private async Task<IActionResult> SaveCoreAsync(
        IFormCollection form, int id)
    {
        using SqlConnection connection = await OpenAsync();

        List<MasterColumnViewModel> fields =
            await LoadFieldsAsync(connection);

        var editable = fields.Where(f => f.Editable).ToList();

        string? validationError = ValidateRequired(editable, form);

        if (!string.IsNullOrWhiteSpace(validationError))
        {
            return Respond(false, validationError);
        }

        var overrides =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        // Default the lead date to today when left blank.
        if (string.IsNullOrWhiteSpace(form["LeadDate"].ToString()))
        {
            overrides["LeadDate"] = DateTime.Today.ToString("yyyy-MM-dd");
        }

        if (id <= 0)
        {
            // ---- INSERT ----
            var extra = new Dictionary<string, object?>
            {
                ["LeadNo"] = await GenerateLeadNoAsync(connection)
            };

            int newId = await InsertAsync(
                connection, fields, form, overrides, extra);

            if (newId > 0)
            {
                await WriteHistoryAsync(
                    connection,
                    newId,
                    "Created",
                    null,
                    null,
                    "Lead created");
            }

            return Respond(
                newId > 0,
                "Lead added successfully.");
        }

        // ---- UPDATE ----
        (string oldStatus, int? oldAssigned) =
            await ReadLeadSnapshotAsync(connection, id);

        int rows = await UpdateAsync(
            connection, fields, form, id, overrides);

        if (rows == 0)
        {
            return Respond(false, "Lead not found.");
        }

        string newStatus = form["Status"].ToString().Trim();
        int newAssigned = int.TryParse(
            form["AssignedTo"].ToString(), out int a) ? a : 0;

        if (!string.Equals(
                oldStatus,
                newStatus,
                StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(newStatus))
        {
            await WriteHistoryAsync(
                connection, id, "Status Changed",
                oldStatus, newStatus, null);

            if (string.Equals(
                    newStatus, "Converted",
                    StringComparison.OrdinalIgnoreCase))
            {
                await WriteHistoryAsync(
                    connection, id, "Converted",
                    oldStatus, newStatus, null);
            }
            else if (string.Equals(
                         newStatus, "Lost",
                         StringComparison.OrdinalIgnoreCase))
            {
                await WriteHistoryAsync(
                    connection, id, "Lost",
                    oldStatus, newStatus,
                    form["LostReason"].ToString());
            }
        }

        if (oldAssigned.GetValueOrDefault() != newAssigned)
        {
            string oldName = await GetUserNameAsync(
                connection, oldAssigned);
            string newName = await GetUserNameAsync(
                connection, newAssigned > 0 ? newAssigned : null);

            await WriteHistoryAsync(
                connection, id, "Assigned",
                oldName, newName, null);
        }

        return Respond(true, "Lead updated successfully.");
    }

    // ============================================
    // QUICK STATUS CHANGE
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(int id, string status)
    {
        var allowed = DynamicTableService
            .GetStaticOptions("CrmLead", "Status").ToList();

        if (!allowed.Contains(status))
        {
            return Respond(false, "Invalid status.");
        }

        using SqlConnection connection = await OpenAsync();

        (string oldStatus, _) =
            await ReadLeadSnapshotAsync(connection, id);

        if (string.IsNullOrWhiteSpace(oldStatus)
            && await ScalarIntAsync(
                connection,
                "SELECT COUNT(*) FROM [CrmLead] WHERE [Id] = @Id",
                ("@Id", id)) == 0)
        {
            return Respond(false, "Lead not found.");
        }

        using (SqlCommand command = new SqlCommand(
            "UPDATE [CrmLead] SET [Status] = @Status WHERE [Id] = @Id",
            connection))
        {
            command.CommandTimeout = 0;
            command.Parameters.AddWithValue("@Status", status);
            command.Parameters.AddWithValue("@Id", id);
            await command.ExecuteNonQueryAsync();
        }

        await WriteHistoryAsync(
            connection, id, "Status Changed",
            oldStatus, status, null);

        if (string.Equals(status, "Converted", StringComparison.OrdinalIgnoreCase))
        {
            await WriteHistoryAsync(
                connection, id, "Converted", oldStatus, status, null);
        }

        return Respond(true, $"Status updated to {status}.");
    }

    // ============================================
    // DELETE
    // ============================================

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        using SqlConnection connection = await OpenAsync();

        using SqlCommand command = new SqlCommand(
            "DELETE FROM [CrmLead] WHERE [Id] = @Id",
            connection);

        command.CommandTimeout = 0;
        command.Parameters.AddWithValue("@Id", id);

        int rows = await command.ExecuteNonQueryAsync();

        return Respond(
            rows > 0,
            rows > 0 ? "Lead deleted successfully." : "Lead not found.");
    }

    // ============================================
    // HELPERS
    // ============================================

    private IActionResult Respond(bool success, string message)
    {
        if (IsAjax)
        {
            return Json(new { success, message });
        }

        TempData[success ? "Success" : "Error"] = message;

        return RedirectToAction(nameof(Index));
    }

    private static int ReadInt(SqlDataReader reader, string column) =>
        reader[column] == DBNull.Value
            ? 0
            : Convert.ToInt32(reader[column]);

    private static async Task<(string Status, int? Assigned)>
        ReadLeadSnapshotAsync(SqlConnection connection, int id)
    {
        using SqlCommand command = new SqlCommand(
            "SELECT [Status], [AssignedTo] FROM [CrmLead] WHERE [Id] = @Id",
            connection);

        command.CommandTimeout = 0;
        command.Parameters.AddWithValue("@Id", id);

        using SqlDataReader reader =
            await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return ("", null);
        }

        string status = reader["Status"] == DBNull.Value
            ? ""
            : reader["Status"].ToString() ?? "";

        int? assigned = reader["AssignedTo"] == DBNull.Value
            ? null
            : Convert.ToInt32(reader["AssignedTo"]);

        return (status, assigned);
    }

    private static async Task<string> GetUserNameAsync(
        SqlConnection connection,
        int? userId)
    {
        if (userId == null || userId <= 0)
        {
            return "";
        }

        using SqlCommand command = new SqlCommand(
            "SELECT [UserName] FROM [LoginUsers] WHERE [Id] = @Id",
            connection);

        command.CommandTimeout = 0;
        command.Parameters.AddWithValue("@Id", userId.Value);

        object? result = await command.ExecuteScalarAsync();

        return result == null || result == DBNull.Value
            ? ""
            : result.ToString() ?? "";
    }
}
