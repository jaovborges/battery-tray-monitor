using BatteryTrayMonitor.Models;
namespace BatteryTrayMonitor.Providers;
public interface IBatteryProvider : IDisposable
{
    string Name { get; }
    Task<IReadOnlyList<BatteryDevice>> QueryAsync(CancellationToken cancellationToken);
}
