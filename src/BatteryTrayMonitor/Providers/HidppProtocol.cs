namespace BatteryTrayMonitor.Providers;
public static class HidppProtocol
{
    public static byte[] Request(byte device, byte feature, byte function, params byte[] parameters)
    {
        var report = new byte[20]; report[0] = 0x11; report[1] = device;
        report[2] = feature; report[3] = (byte)((function << 4) | 0x0B);
        parameters.AsSpan(0, Math.Min(parameters.Length, 16)).CopyTo(report.AsSpan(4));
        return report;
    }
    public static bool Matches(byte[] request, byte[] reply, int length) => length >= 7 &&
        reply[0] is 0x10 or 0x11 && reply[1] == request[1] && reply[2] == request[2] && reply[3] == request[3];
    public static bool Error(byte[] request, byte[] reply, int length) => length >= 7 && reply[1] == request[1] &&
        reply[2] is 0x8F or 0xFF && reply[3] == request[2] && reply[4] == request[3];
    public static int? Battery1000(byte[] reply) => reply.Length < 7 ? null : reply[6] == 3 ? 100 : reply[6] == 0 && reply[4] <= 100 ? reply[4] : null;
    public static int? Battery1004(byte[] caps, byte[] reply) => caps.Length >= 6 && reply.Length >= 8 &&
        (caps[5] & 2) != 0 && reply[4] <= 100 ? reply[4] : null;
}
