using BatteryTrayMonitor.Models;
namespace BatteryTrayMonitor.Monitoring;
public sealed class AlertTracker
{
    private readonly HashSet<string> low = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> connected = new(StringComparer.OrdinalIgnoreCase);
    public (bool Low, bool Reconnected) Observe(BatteryDevice device, int threshold)
    {
        var reconnect = connected.TryGetValue(device.Id, out var was) && !was && device.Connected;
        connected[device.Id] = device.Connected;
        bool alert = false;
        if (device.Connected && device.Percent is int p)
        {
            if (p <= threshold) alert = low.Add(device.Id);
            else if (p >= threshold + 5) low.Remove(device.Id);
        }
        return (alert, reconnect);
    }
}
