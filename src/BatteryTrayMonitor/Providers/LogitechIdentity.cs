using System.Text.RegularExpressions;
namespace BatteryTrayMonitor.Providers;
public static class LogitechIdentity
{
    public static string Legacy(string path, int slot) => "hidpp:" + Regex.Replace(path.ToLowerInvariant(), "&col[0-9a-f]{2}", "") + ":" + slot;
    public static string Physical(Guid container, int slot) => $"logitech:{container:D}:{slot}";
    public static int SlotFromGHubPath(string path)
    {
        int colon = path.LastIndexOf(':');
        return colon >= 0 && int.TryParse(path[(colon + 1)..], out int index) && index is >= 0 and <= 5 ? index + 1 : 255;
    }
}
