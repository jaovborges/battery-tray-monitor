using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using BatteryTrayMonitor.Models;

namespace BatteryTrayMonitor.Providers;

// Read-only client of G HUB's existing loopback endpoint. No server or extra service is created.
// This is an unofficial interface: fail closed and let the independent providers continue.
public sealed class GHubBatteryProvider(Func<bool>? enabled = null) : IBatteryProvider
{
    public string Name => "Logitech G HUB";
    public async Task<IReadOnlyList<BatteryDevice>> QueryAsync(CancellationToken token)
    {
        if (enabled is not null && !enabled()) return [];
        using var socket = new ClientWebSocket();
        socket.Options.AddSubProtocol("json");
        socket.Options.SetRequestHeader("Origin", "file://");
        using (var connect = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            connect.CancelAfter(TimeSpan.FromSeconds(3));
            await socket.ConnectAsync(new Uri("ws://127.0.0.1:9010"), connect.Token);
        }
        using var inventory = await Get(socket, "/devices/list", token);
        if (!inventory.RootElement.TryGetProperty("payload", out var payload) || !payload.TryGetProperty("deviceInfos", out var infos) || infos.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("G HUB: inventário em formato não reconhecido.");
        List<BatteryDevice> devices = [];
        foreach (var info in infos.EnumerateArray())
        {
            token.ThrowIfCancellationRequested();
            var description = GHubProtocol.Describe(info);
            if (description is null) continue;
            BatteryDevice device = description;
            string backendId = GHubProtocol.Text(info, "id");
            if (device.Connected && backendId.Length > 0)
            {
                try
                {
                    using var response = await Get(socket, "/battery/" + Uri.EscapeDataString(backendId) + "/state", token);
                    if (response.RootElement.TryGetProperty("payload", out var battery)) device = GHubProtocol.ReadBattery(device, backendId, battery);
                }
                catch (InvalidDataException e) { device = device with { Detail = e.Message }; }
                // A transport error invalidates the entire connection. Do not show cached G HUB values.
            }
            devices.Add(device);
        }
        return devices;
    }
    private static async Task<JsonDocument> Get(ClientWebSocket socket, string path, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        string id = Guid.NewGuid().ToString("N");
        byte[] request = JsonSerializer.SerializeToUtf8Bytes(new { msgId = id, verb = "GET", path });
        await socket.SendAsync(new ArraySegment<byte>(request), WebSocketMessageType.Text, true, timeout.Token);
        var buffer = new byte[8192];
        while (true)
        {
            using var body = new MemoryStream();
            WebSocketReceiveResult frame;
            do
            {
                frame = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
                if (frame.MessageType != WebSocketMessageType.Text) throw new InvalidDataException("G HUB encerrou a conexão ou enviou um formato inesperado.");
                if (body.Length + frame.Count > 1024 * 1024) throw new InvalidDataException("G HUB: mensagem excede 1 MiB.");
                body.Write(buffer, 0, frame.Count);
            } while (!frame.EndOfMessage);
            var document = JsonDocument.Parse(body.GetBuffer().AsMemory(0, (int)body.Length));
            var root = document.RootElement;
            if (GHubProtocol.Text(root, "msgId") != id || GHubProtocol.Text(root, "path") != path || GHubProtocol.Text(root, "verb") != "GET")
            { document.Dispose(); continue; } // Initial OPTIONS and unrelated events are not replies.
            if (!root.TryGetProperty("result", out var result) || GHubProtocol.Text(result, "code") != "SUCCESS")
            { document.Dispose(); throw new InvalidDataException("G HUB não disponibilizou " + path + "."); }
            return document;
        }
    }
    public void Dispose() { }
}

public static class GHubProtocol
{
    public static string Text(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
    private static bool Flag(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.True;
    public static BatteryDevice? Describe(JsonElement info)
    {
        if (!info.TryGetProperty("capabilities", out var caps) || !Flag(caps, "hasBatteryStatus")) return null;
        string identity = ""; List<string> aliases = [];
        if (info.TryGetProperty("activeInterfaces", out var interfaces) && interfaces.ValueKind == JsonValueKind.Array)
            foreach (var item in interfaces.EnumerateArray())
            {
                if (Text(item, "type") != "DEVIO") continue;
                string path = Text(item, "path");
                if (Guid.TryParse(Text(item, "containerId"), out var container) && container != Guid.Empty)
                {
                    int slot = LogitechIdentity.SlotFromGHubPath(path);
                    identity = LogitechIdentity.Physical(container, slot);
                    if (path.StartsWith(@"\\?\hid#", StringComparison.OrdinalIgnoreCase)) aliases.Add(LogitechIdentity.Legacy(path, slot));
                    break;
                }
            }
        if (identity.Length == 0)
        {
            // Never persist volatile dev000000xx ids or merge two anonymous devices by model name.
            string unit = Text(info, "deviceUnitId"), model = Text(info, "deviceModel");
            if (unit.Length == 0 || model.Length == 0) return null;
            identity = "ghub-unit:" + model + ":" + unit;
        }
        string name = Text(info, "extendedDisplayName");
        if (name.Length == 0) name = Text(info, "displayName");
        bool connected = Text(info, "state") == "ACTIVE";
        string type = Text(info, "deviceType") switch { "MOUSE" => "Mouse", "KEYBOARD" => "Teclado", "HEADSET" => "Headset", _ => "Periférico" };
        return new(identity, name, type, "G HUB / " + Text(info, "displayConnectionType"), null, connected,
            "Logitech G HUB", 100, DateTimeOffset.Now,
            connected ? "G HUB não retornou um percentual válido nesta consulta." : "G HUB informa que o dispositivo está inativo/desconectado.", aliases.ToArray());
    }
    public static BatteryDevice ReadBattery(BatteryDevice device, string backendId, JsonElement battery)
    {
        if (!device.Connected || Text(battery, "deviceId") != backendId || Flag(battery, "isBatteryRemoved") || Flag(battery, "chargingError") ||
            (Text(battery, "batteryStateOfChargeSupport") is var support && support.Length > 0 && support != "STATE_OF_CHARGE_SUPPORTED") ||
            !battery.TryGetProperty("percentage", out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out double number) ||
            !double.IsFinite(number) || number < 0 || number > 100)
            return device with { Percent = null, Detail = "G HUB: bateria ausente, leitura inválida ou percentual indisponível." };
        return device with { Percent = (int)Math.Round(number, MidpointRounding.AwayFromZero), Updated = DateTimeOffset.Now,
            Detail = $"Percentual fornecido pelo G HUB: {number:0.##}%. {(Flag(battery, "charging") ? "Carregando." : "Em uso.")} Consulta local somente leitura; exige G HUB em execução. Interface não oficial." };
    }
}
