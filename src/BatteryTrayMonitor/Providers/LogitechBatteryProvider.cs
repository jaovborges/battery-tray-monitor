using System.Text;
using System.Text.RegularExpressions;
using BatteryTrayMonitor.Models;
using HidSharp;
using BatteryTrayMonitor.Native;

namespace BatteryTrayMonitor.Providers;
public sealed class LogitechBatteryProvider : IBatteryProvider
{
    public Action<string>? Trace { get; init; }
    public string Name => "Logitech HID++ 2.0 (experimental)";
    public Task<IReadOnlyList<BatteryDevice>> QueryAsync(CancellationToken token) => Task.Run<IReadOnlyList<BatteryDevice>>(() =>
    {
        List<BatteryDevice> devices = [];
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        foreach (var hid in DeviceList.Local.GetHidDevices(0x046D))
        {
            token.ThrowIfCancellationRequested();
            // Bluetooth uses the Windows/GATT providers. Do not probe input/keyboard collections.
            if (!hid.DevicePath.Contains("vid_046d", StringComparison.OrdinalIgnoreCase) ||
                hid.DevicePath.Contains("bth", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                if (hid.GetMaxInputReportLength() < 20 || hid.GetMaxOutputReportLength() < 20) continue;
                var usages = hid.GetReportDescriptor().DeviceItems.SelectMany(x => x.Usages.GetAllValues()).ToArray();
                if (!usages.Any(u => (u >> 16) >= 0xFF00)) continue;
                string identity = Regex.Replace(hid.DevicePath.ToLowerInvariant(), "&col[0-9a-f]{2}", "");
                Guid container = DeviceProperties.HidContainer(hid.DevicePath);
                if (visited.Contains(identity)) continue;
                if (!hid.TryOpen(out var stream)) continue;
                using (stream)
                {
                    stream.ReadTimeout = 250; stream.WriteTimeout = 250;
                    visited.Add(identity);
                    // Receiver slots 1..6; direct USB HID++ endpoint is FF.
                    foreach (byte slot in new byte[] { 1, 2, 3, 4, 5, 6, 0xFF })
                    {
                        token.ThrowIfCancellationRequested();
                        var ping = Exchange(stream, hid, HidppProtocol.Request(slot, 0, 1, 0, 0, 0x5A), token);
                        if (ping is null || ping[4] < 2 || ping[6] != 0x5A) continue;
                        int? percentage = null; bool online = true;
                        string detail = "O dispositivo não expõe percentual nas funções HID++ implementadas.";
                        byte unified = Feature(stream, hid, slot, 0x1004, token);
                        Trace?.Invoke($"{hid.ProductID:X4}/{slot}: feature1004={unified}");
                        if (unified != 0)
                        {
                            var caps = Exchange(stream, hid, HidppProtocol.Request(slot, unified, 0), token);
                            var status = Exchange(stream, hid, HidppProtocol.Request(slot, unified, 1), token);
                            Trace?.Invoke($"1004 caps={Convert.ToHexString(caps ?? [])} status={Convert.ToHexString(status ?? [])}");
                            if (caps is not null && status is not null) percentage = HidppProtocol.Battery1004(caps, status);
                        }
                        if (!percentage.HasValue)
                        {
                            byte battery = Feature(stream, hid, slot, 0x1000, token);
                            Trace?.Invoke($"{hid.ProductID:X4}/{slot}: feature1000={battery}");
                            if (battery != 0)
                            {
                                var reply = Exchange(stream, hid, HidppProtocol.Request(slot, battery, 0), token);
                                Trace?.Invoke($"1000 status={Convert.ToHexString(reply ?? [])}");
                                if (reply is not null) percentage = HidppProtocol.Battery1000(reply);
                            }
                        }
                        // Voltage is useful diagnostic data, but is never converted to a guessed percentage.
                        if (!percentage.HasValue)
                        {
                            foreach (ushort voltageFeature in new ushort[] { 0x1001, 0x1F20 })
                            {
                                byte index = Feature(stream, hid, slot, voltageFeature, token);
                                if (index == 0) continue;
                                var value = Exchange(stream, hid, HidppProtocol.Request(slot, index, 0), token);
                                Trace?.Invoke($"{hid.ProductID:X4}/{slot}: feature{voltageFeature:X4}={index} value={Convert.ToHexString(value ?? [])}");
                                if (value is not null && value.Length >= 7)
                                {
                                    detail = $"O dispositivo informa tensão ({(value[4] << 8) | value[5]} mV, HID++ {voltageFeature:X4}), sem percentual. Não é feita estimativa por curva de tensão.";
                                    if (voltageFeature == 0x1F20) online = value[6] is 1 or 3 or 7 && (value[4] != 0 || value[5] != 0);
                                }
                            }
                        }
                        string name = $"Logitech {hid.ProductID:X4} · dispositivo {slot}";
                        string type = "Periférico";
                        byte naming = Feature(stream, hid, slot, 0x0005, token);
                        if (naming != 0)
                        {
                            var length = Exchange(stream, hid, HidppProtocol.Request(slot, naming, 0), token);
                            if (length is not null)
                            {
                                List<byte> bytes = [];
                                for (int offset = 0; offset < Math.Min((int)length[4], 80); offset += 16)
                                {
                                    var part = Exchange(stream, hid, HidppProtocol.Request(slot, naming, 1, (byte)offset), token);
                                    if (part is null) break;
                                    bytes.AddRange(part.Skip(4).Take(Math.Min(16, length[4] - offset)));
                                }
                                if (bytes.Count > 0) name = Encoding.UTF8.GetString(bytes.ToArray()).TrimEnd('\0');
                            }
                            var kind = Exchange(stream, hid, HidppProtocol.Request(slot, naming, 2), token);
                            type = kind?[4] switch { 0 => "Teclado", 3 => "Mouse", _ => "Periférico" };
                        }
                        if (percentage.HasValue) detail = "Percentual informado pelo dispositivo via HID++; validar neste modelo de hardware.";
                        string legacy = LogitechIdentity.Legacy(hid.DevicePath, slot);
                        devices.Add(new(container != Guid.Empty ? LogitechIdentity.Physical(container, slot) : legacy, name, type,
                            slot == 0xFF ? "USB HID++" : "Receptor USB / HID++", percentage, online, Name, 40, DateTimeOffset.Now, detail, [legacy]));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException or NotSupportedException) { }
        }
        return devices;
    }, token);
    private static byte Feature(HidStream stream, HidDevice hid, byte slot, ushort id, CancellationToken token) =>
        Exchange(stream, hid, HidppProtocol.Request(slot, 0, 0, (byte)(id >> 8), (byte)id), token)?[4] ?? 0;
    private static byte[]? Exchange(HidStream stream, HidDevice hid, byte[] request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var output = new byte[hid.GetMaxOutputReportLength()]; request.CopyTo(output, 0);
        try
        {
            stream.Write(output); var buffer = new byte[hid.GetMaxInputReportLength()];
            var deadline = Environment.TickCount64 + 650;
            while (Environment.TickCount64 < deadline)
            {
                token.ThrowIfCancellationRequested();
                int count = stream.Read(buffer);
                if (HidppProtocol.Error(request, buffer, count)) return null;
                if (HidppProtocol.Matches(request, buffer, count)) return buffer[..count];
            }
        }
        catch (TimeoutException) { }
        return null;
    }
    public void Dispose() { }
}
