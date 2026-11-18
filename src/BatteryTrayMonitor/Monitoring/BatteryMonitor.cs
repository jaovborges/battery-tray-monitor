using BatteryTrayMonitor.Models;
using BatteryTrayMonitor.Providers;

namespace BatteryTrayMonitor.Monitoring;
public sealed class BatteryMonitor(IEnumerable<IBatteryProvider> providers) : IDisposable
{
    private readonly List<IBatteryProvider> providers = providers.ToList();
    private readonly CancellationTokenSource stop = new();
    private bool busy;
    public List<BatteryDevice> Devices { get; private set; } = [];
    public Dictionary<string, string> Diagnostics { get; } = [];
    public event Action? Changed;
    public bool Busy => busy;
    public async Task RefreshAsync()
    {
        if (busy || stop.IsCancellationRequested) return;
        busy = true;
        try
        {
            List<BatteryDevice> current = [];
            foreach (var provider in providers)
            {
                stop.Token.ThrowIfCancellationRequested();
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(45));
                    var items = await provider.QueryAsync(timeout.Token);
                    current.AddRange(items);
                    Diagnostics[provider.Name] = $"{items.Count} resultado(s) · {DateTime.Now:HH:mm:ss}";
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
                catch (Exception e) { Diagnostics[provider.Name] = "Falha: " + e.Message; }
            }
            var merged = DeviceMerger.Merge(current);
            foreach (var old in Devices.Where(d => !merged.Any(n => n.Id.Equals(d.Id, StringComparison.OrdinalIgnoreCase))))
                merged.Add(old with { Connected = false, Percent = null, Detail = "Não encontrado nesta consulta; desligado, removido ou provedor indisponível." });
            Devices = merged;
            Changed?.Invoke();
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally { busy = false; }
    }
    public void Dispose() { stop.Cancel(); foreach (var p in providers) p.Dispose(); }
}
