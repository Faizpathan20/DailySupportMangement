namespace Master.Models;

public class MasterRowViewModel
{
    public Dictionary<string, string> Values { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    public string Get(string name) =>
        Values.TryGetValue(name, out var value)
            ? value ?? ""
            : "";
}