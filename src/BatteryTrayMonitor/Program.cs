using System.Text.Json;
using BatteryTrayMonitor.Providers;
using BatteryTrayMonitor.UI;
using BatteryTrayMonitor.Models;

namespace BatteryTrayMonitor;
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 2 && args[0] == "--diagnostics") { RunDiagnostics(args[1]); return; }
        using var mutex = new Mutex(true, @"Local\BatteryTrayMonitor.1", out bool created);
        if (!created) return;
        try { using var context = new TrayApplication(args.Contains("--tray"), args.Contains("--panel")); Application.Run(context); }
        finally { mutex.ReleaseMutex(); }
    }
    private static void RunDiagnostics(string path)
    {
        using var host = new Form { ShowInTaskbar = false, Opacity = 0 };
        host.Shown += async (_, _) =>
        {
            var output = new List<object>();
            var readings = new List<BatteryDevice>();
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            List<string> trace = [];
            foreach (IBatteryProvider provider in new IBatteryProvider[] { new WindowsBatteryProvider(), new BluetoothBatteryProvider(), new LogitechBatteryProvider { Trace = trace.Add }, new GHubBatteryProvider() })
            {
                using (provider)
                    try { var devices = await provider.QueryAsync(stop.Token); readings.AddRange(devices); output.Add(new { provider.Name, Devices = devices, Error = (string?)null }); }
                    catch (Exception e) { output.Add(new { provider.Name, Devices = Array.Empty<object>(), Error = e.ToString() }); }
            }
            File.WriteAllText(path, JsonSerializer.Serialize(new { Providers = output, MergedDevices = DeviceMerger.Merge(readings), HidppTrace = trace }, new JsonSerializerOptions { WriteIndented = true }));
            host.Close();
        };
        Application.Run(host);
    }
}
