using System.Runtime.InteropServices;
using BatteryTrayMonitor.Configuration;
using BatteryTrayMonitor.Models;
using BatteryTrayMonitor.Monitoring;
using BatteryTrayMonitor.Providers;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace BatteryTrayMonitor.UI;
public sealed class TrayApplication : ApplicationContext
{
    private readonly SettingsStore store = new();
    private readonly Settings settings;
    private readonly BatteryMonitor monitor;
    private readonly AlertTracker alerts = new();
    private readonly MainForm panel;
    private readonly NotifyIcon manager;
    private readonly ContextMenuStrip menu = new();
    private readonly Dictionary<string, (NotifyIcon Tray, Icon Icon, string Key)> icons = [];
    private readonly System.Windows.Forms.Timer timer = new();
    private readonly System.Windows.Forms.Timer debounce = new() { Interval = 1800 };
    private readonly MessageWindow messages;
    private Form? popup;
    private bool disposed;
    private bool refreshPending;
    private readonly List<DeviceWatcher> watchers = [];
    public TrayApplication(bool forceTray, bool forcePanel = false)
    {
        settings = store.Load();
        monitor = new([new WindowsBatteryProvider(), new BluetoothBatteryProvider(), new LogitechBatteryProvider(), new GHubBatteryProvider(() => settings.UseGHub)]);
        panel = new(settings, Save, () => _ = Refresh());
        _ = panel.Handle; // Establish WindowsForms synchronization context before WinRT calls.
        menu.Items.Add("Abrir painel", null, (_, _) => panel.Open());
        menu.Items.Add("Atualizar agora", null, (_, _) => _ = Refresh());
        menu.Items.Add("Mostrar dispositivos", null, (_, _) => panel.Open());
        menu.Items.Add("Configurações", null, (_, _) => panel.Open(1));
        var startup = new ToolStripMenuItem("Iniciar com Windows") { CheckOnClick = true };
        startup.Click += (_, _) => { try { StartupRegistration.Set(startup.Checked); } catch (Exception e) { MessageBox.Show(e.Message); } };
        menu.Opening += (_, _) => startup.Checked = StartupRegistration.Enabled;
        menu.Items.Add(startup); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Sair", null, (_, _) => ExitThread());
        manager = new() { Icon = SystemIcons.Application, Text = "Battery Tray Monitor · abrir painel", ContextMenuStrip = menu, Visible = true };
        manager.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowPopup(); };
        timer.Interval = settings.IntervalSeconds * 1000; timer.Tick += (_, _) => _ = Refresh(); timer.Start();
        debounce.Tick += (_, _) => { debounce.Stop(); _ = Refresh(); };
        messages = new(() => { debounce.Stop(); debounce.Start(); }, () => { RebuildIcons(); manager.Visible = false; manager.Visible = true; });
        foreach (var selector in new[] { BluetoothDevice.GetDeviceSelectorFromPairingState(true), BluetoothLEDevice.GetDeviceSelectorFromPairingState(true) })
        {
            try
            {
                var watcher = DeviceInformation.CreateWatcher(selector, new[] { "System.Devices.Aep.IsConnected" });
                watcher.Added += (_, _) => ScheduleRefresh(); watcher.Updated += (_, _) => ScheduleRefresh(); watcher.Removed += (_, _) => ScheduleRefresh();
                watchers.Add(watcher); watcher.Start();
            }
            catch (Exception) { /* Polling remains available when the watcher is unavailable. */ }
        }
        monitor.Changed += OnChanged;
        if (forcePanel || !forceTray && !settings.StartMinimized) panel.Open();
        _ = Refresh();
    }
    private void ScheduleRefresh()
    {
        if (disposed || panel.IsDisposed) return;
        try { panel.BeginInvoke(() => { if (!disposed) { debounce.Stop(); debounce.Start(); } }); }
        catch (InvalidOperationException) { }
    }
    private async Task Refresh()
    {
        if (disposed) return;
        if (monitor.Busy) { refreshPending = true; return; }
        manager.Text = "Battery Tray Monitor · atualizando…";
        await monitor.RefreshAsync();
        if (!disposed) manager.Text = "Battery Tray Monitor · abrir painel";
        if (!disposed && refreshPending) { refreshPending = false; debounce.Stop(); debounce.Start(); }
    }
    private void Save()
    {
        settings.StartWithWindows = StartupRegistration.Enabled;
        try { store.Save(settings); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { MessageBox.Show(panel, "Não foi possível salvar: " + e.Message); }
        timer.Interval = settings.IntervalSeconds * 1000;
        SyncIcons();
    }
    private void OnChanged()
    {
        if (disposed) return;
        foreach (var device in monitor.Devices)
        {
            settings.MigrateAliases(device.Id, device.Aliases ?? []);
            var pref = settings.For(device.Id); pref.Name = device.Name; pref.Type = device.Type; pref.Connection = device.Connection;
        }
        foreach (var (id, pref) in settings.Devices)
            if (pref.Visible && !string.IsNullOrWhiteSpace(pref.Name) && !monitor.Devices.Any(d => d.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                monitor.Devices.Add(new(id, pref.Name, pref.Type, pref.Connection, null, false, "Histórico local", 0, DateTimeOffset.Now, "Dispositivo selecionado anteriormente; não encontrado na consulta atual."));
        panel.UpdateDevices(monitor.Devices, string.Join(" | ", monitor.Diagnostics.Select(x => x.Key + ": " + x.Value)));
        SyncIcons();
        foreach (var d in monitor.Devices)
        {
            var alert = alerts.Observe(d, settings.AlertPercent);
            var pref = settings.For(d.Id);
            if (!pref.Visible || !pref.Notifications || !settings.Notifications) continue;
            if (alert.Low) manager.ShowBalloonTip(6000, "Bateria baixa", $"{d.Name} — Bateria em {d.Percent}%.", ToolTipIcon.Warning);
            else if (alert.Reconnected && settings.NotifyReconnect) manager.ShowBalloonTip(4000, "Dispositivo conectado", d.Name, ToolTipIcon.Info);
        }
    }
    private int TraySize => Math.Max(16, (int)Math.Round(16 * panel.DeviceDpi / 96d));
    private void SyncIcons()
    {
        var selected = monitor.Devices.Where(d => settings.For(d.Id).Visible).OrderBy(d => settings.For(d.Id).Order).ToList();
        foreach (string id in icons.Keys.Where(id => !selected.Any(d => d.Id == id)).ToArray()) RemoveIcon(id);
        foreach (var d in selected)
        {
            string key = $"{d.Percent}:{d.Connected}:{settings.Mode}:{settings.Bold}:{settings.FontPercent}:{settings.AdaptTheme}:{TrayIconRenderer.LightTheme}:{TraySize}:" +
                string.Join(',', settings.Bands.Select(b => $"{b.Minimum}{b.Color}"));
            if (!icons.TryGetValue(d.Id, out var item))
            {
                var image = TrayIconRenderer.Create(d.Percent, d.Connected, settings, TraySize);
                var tray = new NotifyIcon { Icon = image, ContextMenuStrip = menu, Visible = true };
                tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowPopup(); };
                item = (tray, image, key); icons.Add(d.Id, item);
            }
            else if (item.Key != key)
            {
                var image = TrayIconRenderer.Create(d.Percent, d.Connected, settings, TraySize);
                item.Tray.Icon = image; item.Icon.Dispose(); item = (item.Tray, image, key); icons[d.Id] = item;
            }
            item.Tray.Text = TrayIconRenderer.Tooltip(d.Name, d.Percent, d.Connected, d.Updated);
        }
    }
    private void RemoveIcon(string id) { var item = icons[id]; item.Tray.Visible = false; item.Tray.Dispose(); item.Icon.Dispose(); icons.Remove(id); }
    private void RebuildIcons() { foreach (var id in icons.Keys.ToArray()) RemoveIcon(id); SyncIcons(); }
    private void ShowPopup()
    {
        if (popup is { IsDisposed: false }) { popup.Close(); popup = null; return; }
        popup = new Form { Text = "Baterias", FormBorderStyle = FormBorderStyle.FixedToolWindow, ShowInTaskbar = false, TopMost = true,
            StartPosition = FormStartPosition.Manual, ClientSize = new Size(350, 95), Font = new("Segoe UI", 10), BackColor = Color.White };
        var list = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(12), AutoScroll = true };
        var selected = monitor.Devices.Where(d => settings.For(d.Id).Visible).OrderBy(d => settings.For(d.Id).Order).ToList();
        foreach (var d in selected) list.Controls.Add(new Label { Text = $"{d.Name}\n{d.BatteryText} · {(d.Connected ? "Conectado" : "Desconectado")}", AutoSize = true, MaximumSize = new Size(310, 0), Margin = new Padding(0, 0, 0, 10) });
        if (selected.Count == 0) list.Controls.Add(new Label { Text = "Selecione seus dispositivos no painel.", AutoSize = true });
        var open = new Button { Text = "Abrir painel", AutoSize = true }; open.Click += (_, _) => { popup?.Close(); panel.Open(); }; list.Controls.Add(open);
        popup.Height = Math.Min(430, 110 + selected.Count * 62); popup.Controls.Add(list);
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        popup.Location = new Point(Math.Clamp(Cursor.Position.X - popup.Width / 2, area.Left, Math.Max(area.Left, area.Right - popup.Width)), area.Bottom - popup.Height - 8);
        popup.Deactivate += (_, _) => popup?.Close(); popup.Show();
    }
    protected override void ExitThreadCore()
    {
        if (disposed) return;
        disposed = true; timer.Stop(); debounce.Stop(); monitor.Dispose();
        foreach (var watcher in watchers)
            if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) watcher.Stop();
        try { store.Save(settings); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        foreach (var id in icons.Keys.ToArray()) RemoveIcon(id);
        manager.Visible = false; manager.Dispose(); menu.Dispose(); timer.Dispose(); debounce.Dispose(); messages.Dispose(); popup?.Dispose();
        panel.Quitting = true; panel.Dispose(); base.ExitThreadCore();
    }
    private sealed class MessageWindow : NativeWindow, IDisposable
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int RegisterWindowMessage(string message);
        private readonly int taskbar = RegisterWindowMessage("TaskbarCreated");
        private readonly Action changed, restart;
        public MessageWindow(Action changed, Action restart) { this.changed = changed; this.restart = restart; CreateHandle(new CreateParams { Caption = "BatteryTrayMonitor.Events" }); }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == taskbar) restart();
            else if (m.Msg is 0x219 or 0x1A or 0x7E) changed(); // device, settings, display changes
            base.WndProc(ref m);
        }
        public void Dispose() => DestroyHandle();
    }
}
