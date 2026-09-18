using System;
using System.Collections.Generic;
using System.Linq;

namespace Master.Models;

// Reports page (standalone /Reports) model.
// IMPORTANT: this is an INDEPENDENT filter system — it does NOT
// reuse the Dashboard global filters. All table/column names used
// by the report builders are resolved at runtime from live SQL
// Server metadata (see DynamicTableService.SchemaColumns).
public class ReportsViewModel
{
    // ---- Report type keys ----
    public const string SupportReport = "support";
    public const string VisitReport = "visit";
    public const string ClientReport = "client";
    public const string UserActivityReport = "user";
    public const string StateWiseReport = "state";


    // ---- Filter selections ----
    public string ReportType { get; set; } = SupportReport;

    public DateTime? FromDate { get; set; }

    public DateTime? ToDate { get; set; }

    public int? StateId { get; set; }

    public int? UserId { get; set; }

    public int? ClientId { get; set; }

    public string Status { get; set; } = "All";

    public string Priority { get; set; } = "All";


    // ---- Dropdown lookup data ----
    public List<LookupOptionViewModel> States { get; set; } =
        new List<LookupOptionViewModel>();

    public List<LookupOptionViewModel> Users { get; set; } =
        new List<LookupOptionViewModel>();

    public List<LookupOptionViewModel> Clients { get; set; } =
        new List<LookupOptionViewModel>();


    // Report type options for the dropdown.
    public IReadOnlyList<ReportTypeOptionViewModel> ReportTypes { get; } =
        new List<ReportTypeOptionViewModel>
        {
            new() { Key = SupportReport, Label = "Support Report" },
            new() { Key = VisitReport, Label = "Visit Report" },
            new() { Key = ClientReport, Label = "Client Report" },
            new() { Key = UserActivityReport, Label = "User Activity" },
            new() { Key = StateWiseReport, Label = "State-wise Report" }
        };


    // Status / Priority values come from the existing DailySupport
    // value set (no dedicated lookup table exists).
    public IReadOnlyList<string> StatusOptions { get; } =
        new List<string> { "All" }
            .Concat(DashboardViewModel.DailySupportStatuses)
            .ToList();

    public IReadOnlyList<string> PriorityOptions { get; } =
        new List<string> { "All" }
            .Concat(DashboardViewModel.DailySupportPriorities)
            .ToList();


    // ---- Built report output ----
    public string Title { get; set; } = "Report";

    public List<SummaryCardViewModel> Summary { get; set; } =
        new List<SummaryCardViewModel>();

    public List<string> Columns { get; set; } =
        new List<string>();

    public List<string[]> Rows { get; set; } =
        new List<string[]>();

    // Optional relative column widths used by the PDF export
    // (must match Columns.Count when provided).
    public List<double>? ColumnWidths { get; set; }

    public int TotalRecords { get; set; }


    // Human readable selected filters (used in the PDF header).
    public List<string> FilterSummary { get; set; } =
        new List<string>();


    public static string NormalizeReportType(string? value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();

        return normalized switch
        {
            SupportReport => SupportReport,
            VisitReport => VisitReport,
            ClientReport => ClientReport,
            UserActivityReport => UserActivityReport,
            StateWiseReport => StateWiseReport,
            _ => SupportReport
        };
    }


    // Label shown for the current report type.
    public string ReportTypeLabel =>
        ReportTypes
            .FirstOrDefault(r => r.Key == ReportType)
            ?.Label
            ?? "Support Report";
}