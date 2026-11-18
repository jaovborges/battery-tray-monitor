namespace BatteryTrayMonitor.Models;

public sealed record BatteryDevice(string Id, string Name, string Type, string Connection,
    int? Percent, bool Connected, string Source, int Priority, DateTimeOffset Updated,
    string Detail = "", string[]? Aliases = null)
{
    public string BatteryText => Percent is int p ? $"{p}%" : "Bateria indisponível";
}

public static class DeviceMerger
{
    // Physical identities only: never merge distinct devices merely because names match.
    public static List<BatteryDevice> Merge(IEnumerable<BatteryDevice> devices) => devices
        .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
        .Select(g => g.OrderByDescending(d => d.Connected && d.Percent.HasValue)
            .ThenByDescending(d => d.Connected).ThenByDescending(d => d.Priority).First() with
            { Aliases = g.SelectMany(d => d.Aliases ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() })
        .ToList();
}
