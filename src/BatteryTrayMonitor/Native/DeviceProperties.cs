using System.Runtime.InteropServices;
using System.Text;
using BatteryTrayMonitor.Models;

namespace BatteryTrayMonitor.Native;
internal static class DeviceProperties
{
    [StructLayout(LayoutKind.Sequential)] private struct Info { public int Size; public Guid Class; public uint DevInst; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct Key(Guid guid, uint pid) { public Guid Guid = guid; public uint Pid = pid; }
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr SetupDiGetClassDevsW(IntPtr guid, string? enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref Info info);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref Info info, ref Key key, out uint type, byte[] buffer, uint size, out uint required, uint flags);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] private static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref Info info, StringBuilder id, int size, out int required);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Locate_DevNodeW(out uint node, string id, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Get_DevNode_PropertyW(uint node, ref Key key, out uint type, byte[] data, ref uint size, uint flags);
    public static Guid HidContainer(string path)
    {
        if (!path.StartsWith(@"\\?\hid#", StringComparison.OrdinalIgnoreCase)) return Guid.Empty;
        int end = path.LastIndexOf("#{", StringComparison.Ordinal);
        if (end < 4) return Guid.Empty;
        string id = path[4..end].Replace('#', '\\');
        if (CM_Locate_DevNodeW(out uint node, id, 0) != 0) return Guid.Empty;
        var key = new Key(new("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2);
        var bytes = new byte[16]; uint size = 16;
        return CM_Get_DevNode_PropertyW(node, ref key, out uint type, bytes, ref size, 0) == 0 && type == 13 && size == 16 ? new Guid(bytes) : Guid.Empty;
    }
    private static readonly Guid General = new("a45c254e-df1c-4efd-8020-67d146a850e0");
    private static byte[]? Read(IntPtr set, ref Info info, Guid guid, uint pid, out uint type)
    {
        var key = new Key(guid, pid); var bytes = new byte[4096];
        return SetupDiGetDevicePropertyW(set, ref info, ref key, out type, bytes, (uint)bytes.Length, out _, 0) ? bytes : null;
    }
    private static string Text(IntPtr set, ref Info info, Guid guid, uint pid)
    {
        var b = Read(set, ref info, guid, pid, out _);
        return b is null ? "" : Encoding.Unicode.GetString(b).TrimEnd('\0');
    }
    public static IReadOnlyList<BatteryDevice> ReadAll(CancellationToken token)
    {
        List<BatteryDevice> result = [];
        var set = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, 6); // PRESENT | ALLCLASSES
        if (set == new IntPtr(-1)) return result;
        try
        {
            for (uint i = 0; ; i++)
            {
                token.ThrowIfCancellationRequested();
                var info = new Info { Size = Marshal.SizeOf<Info>() };
                if (!SetupDiEnumDeviceInfo(set, i, ref info)) break;
                var id = new StringBuilder(1024);
                if (!SetupDiGetDeviceInstanceIdW(set, ref info, id, id.Capacity, out _)) continue;
                string instance = id.ToString();
                if (!instance.StartsWith("BTH", StringComparison.OrdinalIgnoreCase)) continue;
                var battery = Read(set, ref info, new("104ea319-6ee2-4701-bd47-8ddbf425bbe5"), 2, out var type);
                int? percent = battery is not null && type == 3 && battery[0] <= 100 ? battery[0] : null;
                var container = Read(set, ref info, new("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2, out _);
                var name = Text(set, ref info, General, 14);
                if (string.IsNullOrWhiteSpace(name)) name = Text(set, ref info, General, 2);
                // Only real endpoints or nodes with a battery property; skip transport/service scaffolding.
                if (!percent.HasValue && !instance.StartsWith("BTHENUM\\DEV_", StringComparison.OrdinalIgnoreCase) && !instance.StartsWith("BTHLE\\DEV_", StringComparison.OrdinalIgnoreCase)) continue;
                var connectedBytes = Read(set, ref info, new("83da6326-97a6-4088-9453-a1923f573b29"), 15, out var connectedType);
                bool connected = connectedBytes is not null && connectedType == 17 && connectedBytes[0] != 0;
                var physical = container is null ? Guid.Empty : new Guid(container.AsSpan(0, 16));
                string identity = physical != Guid.Empty ? "container:" + physical : "pnp:" + instance;
                var address = Text(set, ref info, new("2bd67d8b-8beb-48d5-87e0-6cda3428040a"), 1).Replace(":", "").Replace("-", "");
                if (!System.Text.RegularExpressions.Regex.IsMatch(address, "^[0-9a-fA-F]{12}$"))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(instance, @"(?:DEV_|&)([0-9A-Fa-f]{12})(?:_|$)");
                    address = match.Success ? match.Groups[1].Value : "";
                }
                if (address.Length == 12) identity = "bluetooth:" + address.ToUpperInvariant();
                var category = Text(set, ref info, General, 9);
                result.Add(new(identity, string.IsNullOrWhiteSpace(name) ? instance : name,
                    category.Contains("Mouse", StringComparison.OrdinalIgnoreCase) ? "Mouse" : category.Contains("Keyboard", StringComparison.OrdinalIgnoreCase) ? "Teclado" : "Periférico",
                    "Bluetooth", connected ? percent : null, connected, "Windows / PnP", 10, DateTimeOffset.Now,
                    "Propriedade publicada pelo driver; atualização controlada pelo Windows."));
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }
}
