using System.Collections.Generic;
using System.Linq;

namespace Master.Models;

// Clients by State panel (COMPONENT 7).
// ClientMaster.StateId -> States.Id counts, from the database behind
// the shared DashboardFilter (date + global filters). States with zero
// matching clients are omitted; the panel shows a friendly empty state.
public class DashboardClientsByStateViewModel
{
    public List<ClientStateCountViewModel> States { get; set; } =
        new List<ClientStateCountViewModel>();


    // Sum of all displayed rows (header "X clients").
    public int TotalClients =>
        States.Sum(s => s.ClientCount);


    // Largest single-state count (drives the proportional row bars).
    public int MaxCount =>
        States.Count == 0
            ? 0
            : States.Max(s => s.ClientCount);
}