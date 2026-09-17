using System.Collections.Generic;

namespace Master.Models;

// Clients tab of the Dashboard (COMPONENT 12) — a management /
// overview view, NOT a CRUD duplicate. Real ClientMaster data behind
// the shared DashboardFilter; Add/Edit lives in the
// Sidebar > Clients module.
public class DashboardClientsTabViewModel
{
    // Total ClientMaster rows for the shared filters.
    public int TotalClients { get; set; }

    // Clients with IsActive = 1.
    public int ActiveClients { get; set; }

    // Clients with IsActive = 0.
    public int NonActiveClients { get; set; }

    // Clients whose EntryOn falls in the current calendar month.
    public int AddedThisMonth { get; set; }

    // Newest clients (TOP 6) for the Overview table.
    public List<RecentClientItemViewModel> Overview { get; set; } =
        new List<RecentClientItemViewModel>();

    // Clients by State — reuses the Component 7 panel.
    public DashboardClientsByStateViewModel ClientsByState { get; set; } =
        new DashboardClientsByStateViewModel();
}