using BatteryTrayMonitor.Models;
using BatteryTrayMonitor.Native;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
namespace BatteryTrayMonitor.Providers;
public sealed class WindowsBatteryProvider : IBatteryProvider
{
    public string Name => "Windows / PnP";
    public async Task<IReadOnlyList<BatteryDevice>> QueryAsync(CancellationToken token)
    {
        var result = (await Task.Run(() => DeviceProperties.ReadAll(token), token)).ToList();
        var infos = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true),
            new[] { "System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsConnected", "System.Devices.BatteryLife" }).AsTask(token);
        foreach (var info in infos)
        {
            string identity = "classic:" + info.Id;
            if (info.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var a) && a is string address)
                identity = "bluetooth:" + address.Replace(":", "").Replace("-", "").ToUpperInvariant();
            bool connected = info.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var c) && c is true;
            int? battery = null;
            if (info.Properties.TryGetValue("System.Devices.BatteryLife", out var raw) && raw is byte or ushort or uint or int)
            {
                long value = Convert.ToInt64(raw);
                if (value is >= 0 and <= 100 && connected) battery = (int)value;
            }
            result.Add(new(identity, info.Name, "Periférico", "Bluetooth", battery, connected, Name, 10, DateTimeOffset.Now,
                "Informação publicada pelo driver Bluetooth do Windows; pode não estar disponível neste modelo."));
        }
        return result;
    }
    public void Dispose() { }
}
