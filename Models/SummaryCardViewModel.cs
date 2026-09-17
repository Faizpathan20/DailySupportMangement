namespace Master.Models;

// Model consumed by the reusable _SummaryCard partial (COMPONENT 4).
// Keeping this tiny on purpose so the partial can render the same
// card structure for all 8 summary cards without duplicating markup.
public class SummaryCardViewModel
{
    // Short uppercase label shown above the number.
    public string Title { get; set; } = "";

    // The count/statistic to display.
    public int Value { get; set; }

    // Icon rendered inside the existing .card-icon box.
    public string Icon { get; set; } = "";
}