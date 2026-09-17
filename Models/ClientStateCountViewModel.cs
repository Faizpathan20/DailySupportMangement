using System.Globalization;

namespace Master.Models;

// One row of the Clients by State panel (COMPONENT 7).
public class ClientStateCountViewModel
{
    public string StateName { get; set; } = "";

    public int ClientCount { get; set; }


    // Row-bar width (0-100) relative to the largest state.
    // Returns an InvariantCulture string so Razor always renders
    // "12.5" never "12,5".
    public string BarWidth(int maxCount)
    {
        if (maxCount <= 0)
        {
            return "0";
        }

        return (ClientCount * 100.0 / maxCount)
            .ToString(
                "0.##",
                CultureInfo.InvariantCulture);
    }
}