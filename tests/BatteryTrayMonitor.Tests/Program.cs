using BatteryTrayMonitor.Configuration;
using BatteryTrayMonitor.Models;
using BatteryTrayMonitor.Monitoring;
using BatteryTrayMonitor.Providers;
using BatteryTrayMonitor.UI;
using System.Text.Json;

internal static class Program
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flags);
    private static int count;
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
    [STAThread]
    private static async Task Main()
    {
        var settings = new Settings();
        foreach (var (p, expected) in new[] { (0, "#DC2626"), (14, "#DC2626"), (15, "#EA580C"), (29, "#EA580C"), (30, "#CA8A04"), (49, "#CA8A04"), (50, "#65A30D"), (79, "#65A30D"), (80, "#16A34A"), (100, "#16A34A") })
            Check(settings.ColorFor(p) == expected, "color boundary " + p);
        var dir = Path.Combine(Path.GetTempPath(), "BatteryTrayTests-" + Guid.NewGuid()); Directory.CreateDirectory(dir);
        try
        {
            var store = new SettingsStore(Path.Combine(dir, "settings.json"));
            settings.For("test:mouse").Visible = true; settings.For("test:mouse").Notifications = false;
            settings.Mode = IconMode.Percent; settings.Bands[0].Color = "#112233"; settings.For("test:mouse").Order = 4;
            store.Save(settings); var loaded = store.Load();
            Check(loaded.For("TEST:MOUSE").Visible && !loaded.For("test:mouse").Notifications && loaded.For("test:mouse").Order == 4, "device settings roundtrip and identity casing");
            Check(loaded.Mode == IconMode.Percent && loaded.ColorFor(90) == "#112233", "appearance roundtrip");
            File.WriteAllText(store.Path, "{broken"); Check(store.Load().IntervalSeconds == 300, "corrupt settings recovery");
            File.WriteAllText(store.Path, "{\"Bands\":null,\"Devices\":null,\"IntervalSeconds\":-1,\"AlertPercent\":200}");
            loaded = store.Load(); Check(loaded.Bands.Count == 5 && loaded.AlertPercent == 100 && loaded.IntervalSeconds == 300, "settings validation");
        }
        finally { Directory.Delete(dir, true); }
        BatteryDevice Device(string id, int? p, bool connected = true, int priority = 10) => new(id, "Same name", "Mouse", "Test", p, connected, "test", priority, DateTimeOffset.Now);
        var merged = DeviceMerger.Merge([Device("a", 75), Device("A", 80, priority: 30), Device("b", 20), Device("a", null, priority: 40)]);
        Check(merged.Count == 2 && merged[0].Percent == 80, "merge physical identity, prefer available high confidence percentage");
        Check(DeviceMerger.Merge([Device("a", 50, false, 40), Device("a", 60)]).Single().Percent == 60, "connected data wins over disconnected data");
        TestGHub();
        var alerts = new AlertTracker();
        Check(alerts.Observe(Device("a", 15), 15).Low, "first low alert");
        Check(!alerts.Observe(Device("a", 14), 15).Low, "suppress repeated low alert");
        alerts.Observe(Device("a", null, false), 15);
        Check(alerts.Observe(Device("a", 14), 15).Reconnected, "reconnection detected");
        alerts.Observe(Device("a", 18), 15); Check(!alerts.Observe(Device("a", 15), 15).Low, "hysteresis prevents noisy alerts");
        alerts.Observe(Device("a", 20), 15); Check(alerts.Observe(Device("a", 15), 15).Low, "rearm after charge recovery");
        var request = HidppProtocol.Request(1, 4, 1, 0xAB);
        Check(request.Length == 20 && request[0] == 0x11 && request[3] == 0x1B && request[4] == 0xAB, "HID++ request framing");
        var reply = (byte[])request.Clone(); Check(HidppProtocol.Matches(request, reply, 20), "HID++ response correlation");
        reply[1] = 2; Check(!HidppProtocol.Matches(request, reply, 20), "reject unrelated slot");
        Check(HidppProtocol.Error(request, [0x11, 1, 0xFF, 4, 0x1B, 2, 0], 7), "HID++ error correlation");
        Check(HidppProtocol.Battery1004([0, 0, 0, 0, 0, 0], [0, 0, 0, 0, 80, 0, 0, 0]) is null, "never turn qualitative battery into percentage");
        Check(HidppProtocol.Battery1004([0, 0, 0, 0, 0, 2], [0, 0, 0, 0, 80, 0, 0, 0]) == 80, "unified battery state of charge");
        Check(HidppProtocol.Battery1000([0, 0, 0, 0, 0, 0, 1]) is null, "unknown charge value is not zero percent");
        Check(HidppProtocol.Battery1000([0, 0, 0, 0, 45, 0, 0]) == 45, "HID++ discharging percentage");
        Check(HidppProtocol.Battery1000([0, 0, 0, 0, 0, 0, 3]) == 100, "protocol explicitly complete means full charge");
        Check(HidppProtocol.Battery1000([0, 0, 0, 0, 255, 0, 0]) is null, "reject out of range percentage");
        string tip = TrayIconRenderer.Tooltip(new string('x', 250), null, false, DateTimeOffset.Now);
        Check(tip.Length <= 63 && tip.Contains("indisponível"), "native tooltip length and unavailable state");
        foreach (int size in new[] { 16, 20, 24, 32, 48 })
            foreach (IconMode mode in Enum.GetValues<IconMode>())
                foreach (int? p in new int?[] { null, 0, 9, 15, 95, 100 })
                {
                    settings.Mode = mode;
                    using var icon = TrayIconRenderer.Create(p, true, settings, size);
                    Check(icon.Width == size && icon.Height == size, $"icon {size}px {mode} {p}");
                }
        using (var process = System.Diagnostics.Process.GetCurrentProcess())
        {
            uint beforeGdi = GetGuiResources(process.Handle, 0), beforeUser = GetGuiResources(process.Handle, 1);
            for (int i = 0; i < 1000; i++) { using var icon = TrayIconRenderer.Create(i % 101, true, settings); }
            Check(GetGuiResources(process.Handle, 0) <= beforeGdi + 3 && GetGuiResources(process.Handle, 1) <= beforeUser + 3, "1000 icon changes do not leak GDI/USER handles");
        }
        var broken = new FakeProvider(true); var good = new FakeProvider(false);
        using (var monitor = new BatteryMonitor([broken, good]))
        {
            await monitor.RefreshAsync();
            Check(monitor.Devices.Count == 1 && monitor.Diagnostics["broken"].StartsWith("Falha:"), "provider fault isolation");
            good.Empty = true; await monitor.RefreshAsync();
            Check(!monitor.Devices.Single().Connected && monitor.Devices.Single().Percent is null, "removed device clears current percentage");
        }
        using (var disabledGHub = new GHubBatteryProvider(() => false))
            Check((await disabledGHub.QueryAsync(CancellationToken.None)).Count == 0, "disabled G HUB performs no connection");
        Console.WriteLine($"All {count} checks passed.");
    }
    private static JsonElement Json(string json) { using var doc = JsonDocument.Parse(json); return doc.RootElement.Clone(); }
    private static void TestGHub()
    {
        const string container = "11111111-2222-3333-4444-555555555555";
        var info = Json("""
        {"id":"dev00000001","capabilities":{"hasBatteryStatus":true},"state":"ACTIVE","deviceType":"MOUSE",
         "extendedDisplayName":"Test mouse","displayConnectionType":"WIRELESS","activeInterfaces":[
          {"type":"DEVIO","path":"usb\\vid_046d&pid_c539:0","containerId":"11111111-2222-3333-4444-555555555555"}]}
        """);
        var mouse = GHubProtocol.Describe(info)!;
        Check(mouse.Id == LogitechIdentity.Physical(Guid.Parse(container), 1) && mouse.Connected && mouse.Type == "Mouse", "G HUB receiver identity matches native slot");
        Check(LogitechIdentity.SlotFromGHubPath(@"\\?\hid#test") == 255 && LogitechIdentity.SlotFromGHubPath("usb:test:5") == 6, "G HUB direct and receiver slot identities");
        Check(LogitechIdentity.SlotFromGHubPath("usb:test:99") == 255, "reject invalid receiver slot");
        var headset = GHubProtocol.Describe(Json("""
        {"capabilities":{"hasBatteryStatus":true},"state":"ACTIVE","deviceType":"HEADSET","displayName":"Test headset",
         "activeInterfaces":[{"type":"DEVIO","path":"\\\\?\\hid#vid_046d&pid_0a87&col03#test#{guid}","containerId":"11111111-2222-3333-4444-555555555555"}]}
        """))!;
        Check(headset.Id.EndsWith(":255") && headset.Aliases!.Single().Contains("pid_0a87#"), "G HUB direct headset preserves legacy identity alias");
        Check(GHubProtocol.Describe(Json("{\"capabilities\":{\"hasBatteryStatus\":false}}")) is null, "skip devices without battery capability");
        Check(GHubProtocol.Describe(Json("{\"id\":\"dev0001\",\"capabilities\":{\"hasBatteryStatus\":true}}")) is null, "never persist a volatile G HUB device id");
        var unit = GHubProtocol.Describe(Json("{\"deviceModel\":\"mouse\",\"deviceUnitId\":\"unique1\",\"capabilities\":{\"hasBatteryStatus\":true},\"state\":\"INACTIVE\"}"))!;
        Check(unit.Id == "ghub-unit:mouse:unique1" && !unit.Connected, "stable unit fallback and inactive device");
        BatteryDevice Read(string payload) => GHubProtocol.ReadBattery(mouse, "dev00000001", Json(payload));
        Check(Read("{\"deviceId\":\"dev00000001\",\"percentage\":49}").Percent == 49, "G HUB integer percentage");
        var fractional = Read("{\"deviceId\":\"dev00000001\",\"percentage\":92.05,\"charging\":true,\"batteryStateOfChargeSupport\":\"STATE_OF_CHARGE_SUPPORTED\"}");
        Check(fractional.Percent == 92 && fractional.Detail.Contains("Carregando"), "G HUB fractional percentage rounds for tray and preserves charging detail");
        Check(Read("{\"deviceId\":\"dev00000001\",\"percentage\":49.5}").Percent == 50, "G HUB midpoint rounding");
        Check(Read("{\"deviceId\":\"dev00000001\",\"percentage\":0}").Percent == 0, "G HUB explicit zero is valid");
        Check(Read("{\"deviceId\":\"dev00000001\",\"percentage\":100}").Percent == 100, "G HUB explicit full is valid");
        foreach (var invalid in new[] { "-1", "100.01", "null", "\"49\"" })
            Check(Read("{\"deviceId\":\"dev00000001\",\"percentage\":" + invalid + "}").Percent is null, "reject invalid G HUB percentage " + invalid);
        Check(Read("{\"deviceId\":\"other\",\"percentage\":49}").Percent is null, "reject another device's battery response");
        Check(Read("{\"deviceId\":\"dev00000001\"}").Percent is null, "missing percentage is not zero");
        foreach (string flag in new[] { "isBatteryRemoved", "chargingError" })
            Check(Read("{\"deviceId\":\"dev00000001\",\"percentage\":49,\"" + flag + "\":true}").Percent is null, "reject unavailable G HUB battery " + flag);
        Check(Read("{\"deviceId\":\"dev00000001\",\"percentage\":0,\"batteryStateOfChargeSupport\":\"STATE_OF_CHARGE_NOT_SUPPORTED\"}").Percent is null, "reject unsupported state of charge");
        Check(GHubProtocol.ReadBattery(mouse with { Connected = false }, "dev00000001", Json("{\"deviceId\":\"dev00000001\",\"percentage\":49}")).Percent is null, "do not show a cached percentage for an inactive device");
        var native = mouse with { Source = "HID++", Percent = null, Priority = 40, Aliases = ["hidpp:legacy:1"] };
        var combined = DeviceMerger.Merge([native, Read("{\"deviceId\":\"dev00000001\",\"percentage\":49}")]);
        Check(combined.Count == 1 && combined[0].Percent == 49 && combined[0].Source == "Logitech G HUB", "combine G HUB and HID++ without duplicate icons");
        Check(combined[0].Aliases!.Contains("hidpp:legacy:1"), "retain native preference aliases when G HUB wins");
        var preferences = new Settings(); preferences.For("hidpp:legacy:1").Visible = true; preferences.For("hidpp:legacy:1").Notifications = false;
        preferences.MigrateAliases(mouse.Id, combined[0].Aliases!);
        Check(preferences.For(mouse.Id).Visible && !preferences.For(mouse.Id).Notifications && !preferences.Devices.ContainsKey("hidpp:legacy:1"), "migrate existing selected device without losing alert preference");
        Check(new Settings().UseGHub, "enable G HUB integration by default");
    }
    private sealed class FakeProvider(bool fail) : IBatteryProvider
    {
        public bool Empty;
        public string Name => fail ? "broken" : "good";
        public Task<IReadOnlyList<BatteryDevice>> QueryAsync(CancellationToken token)
        {
            if (fail) throw new IOException("injected failure");
            IReadOnlyList<BatteryDevice> result = Empty ? [] : [new("test", "Test", "Mouse", "Test", 50, true, Name, 1, DateTimeOffset.Now)];
            return Task.FromResult(result);
        }
        public void Dispose() { }
    }
}
