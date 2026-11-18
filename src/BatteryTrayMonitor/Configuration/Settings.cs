using System.Text.Json;
using Microsoft.Win32;

namespace BatteryTrayMonitor.Configuration;

public enum IconMode { Number, Percent, Battery }
public sealed class ColorBand
{
    public int Minimum { get; set; }
    public string Color { get; set; } = "#15803D";
}
public sealed class DevicePreference
{
    public bool Visible { get; set; }
    public bool Notifications { get; set; } = true;
    public int Order { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "Periférico";
    public string Connection { get; set; } = "";
}
public sealed class Settings
{
    public IconMode Mode { get; set; }
    public bool StartMinimized { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool UseGHub { get; set; } = true;
    public bool Notifications { get; set; } = true;
    public bool NotifyReconnect { get; set; }
    public bool AdaptTheme { get; set; } = true;
    public bool Bold { get; set; } = true;
    public int FontPercent { get; set; } = 94;
    public int AlertPercent { get; set; } = 15;
    public int IntervalSeconds { get; set; } = 300;
    public Dictionary<string, DevicePreference> Devices { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ColorBand> Bands { get; set; } = [
        new() { Minimum = 80, Color = "#16A34A" }, new() { Minimum = 50, Color = "#65A30D" },
        new() { Minimum = 30, Color = "#CA8A04" }, new() { Minimum = 15, Color = "#EA580C" },
        new() { Minimum = 0, Color = "#DC2626" }];
    public DevicePreference For(string id)
    {
        if (!Devices.TryGetValue(id, out var value)) Devices[id] = value = new() { Order = Devices.Count };
        return value;
    }
    public void MigrateAliases(string id, IEnumerable<string> aliases)
    {
        foreach (string alias in aliases)
            if (!alias.Equals(id, StringComparison.OrdinalIgnoreCase) && Devices.Remove(alias, out var preference))
            {
                if (!Devices.TryGetValue(id, out var current)) Devices[id] = preference;
                else if (preference.Visible) { current.Visible = true; current.Notifications = preference.Notifications; current.Order = preference.Order; }
            }
    }
    public string ColorFor(int percent) => Bands.OrderByDescending(b => b.Minimum)
        .FirstOrDefault(b => percent >= b.Minimum)?.Color ?? "#DC2626";
    public void Normalize()
    {
        if (!new[] { 30, 60, 300, 600, 900 }.Contains(IntervalSeconds)) IntervalSeconds = 300;
        AlertPercent = Math.Clamp(AlertPercent, 0, 100);
        FontPercent = Math.Clamp(FontPercent, 60, 110);
        if (!Enum.IsDefined(Mode)) Mode = IconMode.Number;
        Devices = new(Devices ?? [], StringComparer.OrdinalIgnoreCase);
        foreach (var id in Devices.Keys.ToArray()) if (Devices[id] is null) Devices[id] = new();
        if (Bands is null || Bands.Count != 5 || Bands.Any(b => b is null) || !Bands.Any(b => b.Minimum == 0) ||
            Bands.Any(b => b.Minimum < 0 || b.Minimum > 100 || !System.Text.RegularExpressions.Regex.IsMatch(b.Color ?? "", "^#[0-9a-fA-F]{6}$")))
            Bands = new Settings().Bands;
    }
}
public sealed class SettingsStore
{
    public string Path { get; }
    public SettingsStore(string? path = null) => Path = path ?? System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BatteryTrayMonitor", "settings.json");
    public Settings Load()
    {
        try { var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path)) ?? new(); s.Normalize(); return s; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or ArgumentException) { return new(); }
    }
    public void Save(Settings value)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, Path, true);
    }
}
public static class StartupRegistration
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool Enabled { get { using var k = Registry.CurrentUser.OpenSubKey(Key); return k?.GetValue("BatteryTrayMonitor") is string; } }
    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Key);
        if (enabled) key.SetValue("BatteryTrayMonitor", $"\"{Environment.ProcessPath}\" --tray");
        else key.DeleteValue("BatteryTrayMonitor", false);
    }
}
