using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Master.Configuration;
using Master.Models;
using Master.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Master.Controllers;

// Standalone Reports page (route /Reports).
//
// Reports has its OWN independent filter system and does NOT reuse the
// Dashboard global filters. It only reads the existing tables and
// relationships (via DatabaseMapping) — no schema changes.
public partial class ReportsController : Controller
{
    private readonly IConfiguration _configuration;

    public ReportsController(IConfiguration configuration)
    {
        _configuration = configuration;
    }


    [HttpGet]
    public async Task<IActionResult> Index(
        string? reportType,
        DateTime? fromDate,
        DateTime? toDate,
        int? stateId,
        int? userId,
        int? clientId,
        string? status,
        string? priority,
        bool partial = false)
    {
        var model = await BuildModelAsync(
            reportType, fromDate, toDate,
            stateId, userId, clientId, status, priority);

        if (partial)
        {
            return PartialView("_ReportResults", model);
        }

        return View(model);
    }


    [HttpGet]
    public async Task<IActionResult> ExportPdf(
        string? reportType,
        DateTime? fromDate,
        DateTime? toDate,
        int? stateId,
        int? userId,
        int? clientId,
        string? status,
        string? priority)
    {
        var model = await BuildModelAsync(
            reportType, fromDate, toDate,
            stateId, userId, clientId, status, priority);

        var metaLines = new List<string>
        {
            "Report: " + model.ReportTypeLabel,
            "Generated: " + DateTime.Now.ToString(
                "dd MMM yyyy HH:mm", CultureInfo.InvariantCulture)
        };

        metaLines.AddRange(model.FilterSummary);

        var summaryLines = model.Summary
            .Select(s => s.Title + ": " + s.Value)
            .ToList();

        byte[] pdf = PdfReportBuilder.Build(
            model.Title,
            metaLines,
            summaryLines,
            model.Columns,
            model.Rows,
            "Total records: " + model.TotalRecords,
            model.ColumnWidths);

        string fileName =
            model.ReportTypeLabel.Replace(" ", "_")
            + "_"
            + DateTime.Now.ToString(
                "yyyyMMdd_HHmm", CultureInfo.InvariantCulture)
            + ".pdf";

        return File(pdf, "application/pdf", fileName);
    }


    // ==========================================================
    // Model building
    // ==========================================================

    private async Task<ReportsViewModel> BuildModelAsync(
        string? reportType,
        DateTime? fromDate,
        DateTime? toDate,
        int? stateId,
        int? userId,
        int? clientId,
        string? status,
        string? priority)
    {
        if (fromDate.HasValue
            && toDate.HasValue
            && fromDate.Value.Date > toDate.Value.Date)
        {
            (fromDate, toDate) = (toDate, fromDate);
        }

        var model = new ReportsViewModel
        {
            ReportType =
                ReportsViewModel.NormalizeReportType(reportType),
            FromDate = fromDate,
            ToDate = toDate,
            StateId = stateId,
            UserId = userId,
            ClientId = clientId,
            Status = DashboardViewModel.NormalizeStatus(status),
            Priority =
                DashboardViewModel.NormalizePriority(priority)
        };

        string? connectionString =
            _configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return model;
        }

        await using var connection =
            new SqlConnection(connectionString);

        await connection.OpenAsync();

        model.States = await LoadStatesAsync(connection);
        model.Users = await LoadUsersAsync(connection);
        model.Clients = await LoadClientsAsync(connection);

        // Drop stale ids, like the Dashboard does.
        model.StateId =
            DashboardViewModel.NormalizeId(model.StateId, model.States);
        model.UserId =
            DashboardViewModel.NormalizeId(model.UserId, model.Users);
        model.ClientId =
            DashboardViewModel.NormalizeId(
                model.ClientId, model.Clients);

        FillFilterSummary(model);

        switch (model.ReportType)
        {
            case ReportsViewModel.VisitReport:
                await BuildVisitReportAsync(connection, model);
                break;

            case ReportsViewModel.ClientReport:
                await BuildClientReportAsync(connection, model);
                break;

            case ReportsViewModel.UserActivityReport:
                await BuildUserActivityReportAsync(connection, model);
                break;

            case ReportsViewModel.StateWiseReport:
                await BuildStateWiseReportAsync(connection, model);
                break;

            default:
                await BuildSupportReportAsync(connection, model);
                break;
        }

        // ---- Prepend Sr No. column ----
        if (model.Columns.Count > 0)
        {
            model.Columns.Insert(0, "Sr No.");

            if (model.ColumnWidths != null
                && model.ColumnWidths.Count == model.Columns.Count - 1)
            {
                model.ColumnWidths.Insert(0, 0.05);
            }

            for (int i = 0; i < model.Rows.Count; i++)
            {
                model.Rows[i] = new[]
                {
                    (i + 1).ToString(CultureInfo.InvariantCulture)
                }
                .Concat(model.Rows[i])
                .ToArray();
            }
        }

        return model;
    }


    private static void FillFilterSummary(ReportsViewModel model)
    {
        var lines = new List<string>();

        string dates;

        if (model.FromDate.HasValue && model.ToDate.HasValue)
        {
            dates = model.FromDate.Value.ToString(
                        "dd MMM yyyy", CultureInfo.InvariantCulture)
                    + " to "
                    + model.ToDate.Value.ToString(
                        "dd MMM yyyy", CultureInfo.InvariantCulture);
        }
        else if (model.FromDate.HasValue)
        {
            dates = "From " + model.FromDate.Value.ToString(
                "dd MMM yyyy", CultureInfo.InvariantCulture);
        }
        else if (model.ToDate.HasValue)
        {
            dates = "Up to " + model.ToDate.Value.ToString(
                "dd MMM yyyy", CultureInfo.InvariantCulture);
        }
        else
        {
            dates = "All dates";
        }

        lines.Add("Date: " + dates);

        lines.Add(
            "State: "
            + (model.States
                .FirstOrDefault(s => s.Id == model.StateId)
                ?.Name ?? "All States"));

        lines.Add(
            "User: "
            + (model.Users
                .FirstOrDefault(u => u.Id == model.UserId)
                ?.Name ?? "All Users"));

        lines.Add(
            "Client: "
            + (model.Clients
                .FirstOrDefault(c => c.Id == model.ClientId)
                ?.Name ?? "All Clients"));

        lines.Add("Status: " + model.Status);
        lines.Add("Priority: " + model.Priority);

        model.FilterSummary = lines;
    }


    // ==========================================================
    // Shared filter helpers
    // ==========================================================

    private sealed class ReportFilter
    {
        public string Where = "";
        public string Name = "";
        public object Value = default!;
    }


    private static List<ReportFilter> BuildFilters(
        ReportsViewModel model,
        string dateColumn,
        string userColumn,
        string? statusColumn,
        string? priorityColumn)
    {
        var filters = new List<ReportFilter>();

        if (model.FromDate.HasValue)
        {
            filters.Add(new ReportFilter
            {
                Where = dateColumn + " >= @FromDate",
                Name = "@FromDate",
                Value = model.FromDate.Value.Date
            });
        }

        if (model.ToDate.HasValue)
        {
            filters.Add(new ReportFilter
            {
                Where = dateColumn + " < @ToDateEnd",
                Name = "@ToDateEnd",
                Value = model.ToDate.Value.Date.AddDays(1)
            });
        }

        if (model.StateId.HasValue)
        {
            filters.Add(new ReportFilter
            {
                Where =
                    "c."
                    + DatabaseMapping.ClientMaster.StateId
                    + " = @RptStateId",
                Name = "@RptStateId",
                Value = model.StateId.Value
            });
        }

        if (model.UserId.HasValue)
        {
            filters.Add(new ReportFilter
            {
                Where = userColumn + " = @RptUserId",
                Name = "@RptUserId",
                Value = model.UserId.Value
            });
        }

        if (model.ClientId.HasValue)
        {
            filters.Add(new ReportFilter
            {
                Where =
                    "c."
                    + DatabaseMapping.ClientMaster.Id
                    + " = @RptClientId",
                Name = "@RptClientId",
                Value = model.ClientId.Value
            });
        }

        if (statusColumn != null && model.Status != "All")
        {
            filters.Add(new ReportFilter
            {
                Where = statusColumn + " = @RptStatus",
                Name = "@RptStatus",
                Value = model.Status
            });
        }

        if (priorityColumn != null && model.Priority != "All")
        {
            filters.Add(new ReportFilter
            {
                Where = priorityColumn + " = @RptPriority",
                Name = "@RptPriority",
                Value = model.Priority
            });
        }

        return filters;
    }


    private static void ApplyFilters(
        SqlCommand command,
        List<ReportFilter> filters)
    {
        foreach (var filter in filters)
        {
            command.Parameters.AddWithValue(
                filter.Name, filter.Value);
        }
    }


    private static string WhereOf(List<ReportFilter> filters)
    {
        if (filters.Count == 0)
        {
            return "";
        }

        return "WHERE "
            + string.Join(
                " AND ",
                filters.Select(f => f.Where));
    }


    private static async Task<Dictionary<string, int>>
        GroupCountAsync(
            SqlConnection connection,
            string fromJoins,
            string groupColumn,
            List<ReportFilter> filters)
    {
        var map = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);

        using var command = new SqlCommand();

        command.Connection = connection;
        command.CommandTimeout = 0;

        ApplyFilters(command, filters);

        command.CommandText =
            "SELECT ISNULL(" + groupColumn + ", '') AS Grp, "
            + "COUNT(*) AS Cnt "
            + fromJoins + " "
            + WhereOf(filters) + " "
            + "GROUP BY " + groupColumn;

        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            map[reader.GetString(0)] =
                Convert.ToInt32(reader.GetValue(1));
        }

        return map;
    }


    // ==========================================================
    // Value formatting
    // ==========================================================

    private static string Str(object value)
    {
        if (value == null || value == DBNull.Value)
        {
            return "";
        }

        return value.ToString()?.Trim() ?? "";
    }


    private static string FmtDate(object value)
    {
        if (value == null || value == DBNull.Value)
        {
            return "—";
        }

        return Convert.ToDateTime(value).ToString(
            "dd MMM yyyy", CultureInfo.InvariantCulture);
    }


    private static string FmtTime(object value)
    {
        if (value == null || value == DBNull.Value)
        {
            return "—";
        }

        if (value is TimeSpan span)
        {
            var dt = new DateTime(1, 1, 1,
                span.Hours, span.Minutes, span.Seconds);

            return dt.ToString(
                "hh:mm tt",
                CultureInfo.InvariantCulture);
        }

        return Convert.ToDateTime(value).ToString(
            "hh:mm tt", CultureInfo.InvariantCulture);
    }


    // ==========================================================
    // Lookups (same pattern as the Dashboard)
    // ==========================================================

    private static async Task<List<LookupOptionViewModel>>
        LoadStatesAsync(SqlConnection connection)
    {
        string query = $@"
            SELECT
                {DatabaseMapping.States.Id},
                {DatabaseMapping.States.StateName}
            FROM {DatabaseMapping.States.Table}
            WHERE {DatabaseMapping.States.IsActive} = 1
            ORDER BY {DatabaseMapping.States.StateName}";

        return await ReadLookupsAsync(connection, query);
    }


    private static async Task<List<LookupOptionViewModel>>
        LoadUsersAsync(SqlConnection connection)
    {
        string query = $@"
            SELECT
                {DatabaseMapping.LoginUsers.Id},
                {DatabaseMapping.LoginUsers.UserName}
            FROM {DatabaseMapping.LoginUsers.Table}
            WHERE {DatabaseMapping.LoginUsers.IsActive} = 1
            ORDER BY {DatabaseMapping.LoginUsers.UserName}";

        return await ReadLookupsAsync(connection, query);
    }


    private static async Task<List<LookupOptionViewModel>>
        LoadClientsAsync(SqlConnection connection)
    {
        string query = $@"
            SELECT
                {DatabaseMapping.ClientMaster.Id},
                {DatabaseMapping.ClientMaster.ClientName}
            FROM {DatabaseMapping.ClientMaster.Table}
            WHERE {DatabaseMapping.ClientMaster.IsActive} = 1
            ORDER BY {DatabaseMapping.ClientMaster.ClientName}";

        return await ReadLookupsAsync(connection, query);
    }


    private static async Task<List<LookupOptionViewModel>>
        ReadLookupsAsync(
            SqlConnection connection,
            string query)
    {
        var items = new List<LookupOptionViewModel>();

        using var command = new SqlCommand(query, connection);
        command.CommandTimeout = 0;

        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            items.Add(new LookupOptionViewModel
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1)
            });
        }

        return items;
    }
}