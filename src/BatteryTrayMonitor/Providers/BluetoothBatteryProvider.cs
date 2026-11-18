using BatteryTrayMonitor.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace BatteryTrayMonitor.Providers;
public sealed class BluetoothBatteryProvider : IBatteryProvider
{
    public string Name => "Bluetooth LE / GATT";
    private static readonly Guid ServiceId = new("0000180f-0000-1000-8000-00805f9b34fb");
    private static readonly Guid LevelId = new("00002a19-0000-1000-8000-00805f9b34fb");
    public async Task<IReadOnlyList<BatteryDevice>> QueryAsync(CancellationToken token)
    {
        var infos = await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true),
            new[] { "System.Devices.ContainerId", "System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsConnected" }).AsTask(token);
        List<BatteryDevice> result = [];
        foreach (var info in infos)
        {
            token.ThrowIfCancellationRequested();
            bool connected = info.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var c) && c is true;
            string identity = info.Properties.TryGetValue("System.Devices.ContainerId", out var id) && id is Guid g && g != Guid.Empty ? "container:" + g : "ble:" + info.Id;
            if (info.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var address) && address is string a)
                identity = "bluetooth:" + a.Replace(":", "").Replace("-", "").ToUpperInvariant();
            int? percent = null; string detail = "Serviço de bateria indisponível ou acesso recusado.";
            if (connected)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                try
                {
                    // Called on the UI synchronization context, as required when consent is needed.
                    using var device = await BluetoothLEDevice.FromIdAsync(info.Id).AsTask(timeout.Token);
                    if (device is not null)
                    {
                        connected = device.ConnectionStatus == BluetoothConnectionStatus.Connected;
                        var services = await device.GetGattServicesForUuidAsync(ServiceId, BluetoothCacheMode.Cached).AsTask(timeout.Token);
                        try
                        {
                            if (services.Status == GattCommunicationStatus.Success)
                                foreach (var service in services.Services)
                                {
                                    var chars = await service.GetCharacteristicsForUuidAsync(LevelId).AsTask(timeout.Token);
                                    if (chars.Status != GattCommunicationStatus.Success) continue;
                                    foreach (var ch in chars.Characteristics)
                                    {
                                        var value = await ch.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(timeout.Token);
                                        if (value.Status != GattCommunicationStatus.Success || value.Value.Length != 1) continue;
                                        using var reader = DataReader.FromBuffer(value.Value);
                                        var p = reader.ReadByte();
                                        if (p <= 100) { percent = p; detail = "Leitura direta GATT 180F / 2A19."; break; }
                                    }
                                    if (percent.HasValue) break;
                                }
                        }
                        finally { foreach (var service in services.Services) service.Dispose(); }
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { detail = "Tempo limite da leitura GATT."; }
                catch (Exception ex) when (ex is not OperationCanceledException) { detail = "GATT: " + ex.Message; }
            }
            result.Add(new(identity, info.Name, "Periférico", "Bluetooth LE", connected ? percent : null, connected, Name, 30, DateTimeOffset.Now, detail));
        }
        return result;
    }
    public void Dispose() { }
}
