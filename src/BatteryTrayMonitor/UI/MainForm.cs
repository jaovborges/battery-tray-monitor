using BatteryTrayMonitor.Configuration;
using BatteryTrayMonitor.Models;

namespace BatteryTrayMonitor.UI;
public sealed class MainForm : Form
{
    private readonly Settings settings;
    private readonly Action save;
    private readonly Action refresh;
    private readonly DataGridView grid = new();
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(820, 0), ForeColor = Color.DimGray };
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private List<BatteryDevice> devices = [];
    private bool loading;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Quitting { get; set; }
    public MainForm(Settings settings, Action save, Action refresh)
    {
        this.settings = settings; this.save = save; this.refresh = refresh;
        Text = "Battery Tray Monitor"; Font = new("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(900, 530); MinimumSize = new Size(740, 460); StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.White; ShowInTaskbar = true;
        var title = new Label { Text = "Battery Tray Monitor", Font = new("Segoe UI", 19, FontStyle.Bold), Dock = DockStyle.Top, Height = 58, Padding = new Padding(16, 10, 0, 0) };
        Controls.Add(tabs); Controls.Add(title);
        BuildDevices(); BuildAppearance(); BuildGeneral();
        FormClosing += (_, e) => { if (!Quitting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
    }
    private FlowLayoutPanel Page(string title)
    {
        var page = new TabPage(title) { BackColor = Color.White, Padding = new Padding(14) };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        page.Controls.Add(flow); tabs.TabPages.Add(page); return flow;
    }
    private static Button Button(string text, Action action)
    {
        var b = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Padding = new Padding(7, 3, 7, 3), Margin = new Padding(3, 6, 3, 6) };
        b.Click += (_, _) => action(); return b;
    }
    private void BuildDevices()
    {
        var page = new TabPage("Dispositivos") { BackColor = Color.White, Padding = new Padding(12) }; tabs.TabPages.Add(page);
        grid.Dock = DockStyle.Fill; grid.AutoGenerateColumns = false; grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false;
        grid.RowHeadersVisible = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.MultiSelect = false;
        grid.EditMode = DataGridViewEditMode.EditOnEnter;
        grid.BackgroundColor = Color.White; grid.BorderStyle = BorderStyle.None; grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "visible", HeaderText = "Bandeja", Width = 65 });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "notify", HeaderText = "Alertas", Width = 60 });
        foreach (var (name, title, width) in new[] { ("name", "Dispositivo", 190), ("battery", "Bateria", 145), ("connection", "Conexão", 130), ("status", "Status", 105), ("source", "Fonte", 170), ("type", "Tipo", 90) })
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = title, Width = width, ReadOnly = true });
        grid.CurrentCellDirtyStateChanged += (_, _) => { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        grid.CellValueChanged += (_, e) =>
        {
            if (loading || e.RowIndex < 0 || e.ColumnIndex > 1) return;
            var row = grid.Rows[e.RowIndex]; var pref = settings.For((string)row.Tag!);
            pref.Visible = row.Cells[0].Value is true; pref.Notifications = row.Cells[1].Value is true; save();
        };
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 88, FlowDirection = FlowDirection.LeftToRight };
        footer.Controls.Add(Button("Atualizar agora", refresh));
        footer.Controls.Add(Button("↑ Mover", () => MoveDevice(-1))); footer.Controls.Add(Button("↓ Mover", () => MoveDevice(1)));
        footer.Controls.Add(status); page.Controls.Add(grid); page.Controls.Add(footer);
    }
    private void MoveDevice(int offset)
    {
        if (grid.SelectedRows.Count == 0) return;
        int index = grid.SelectedRows[0].Index, other = index + offset;
        if (other < 0 || other >= grid.Rows.Count) return;
        var sorted = devices.OrderBy(d => settings.For(d.Id).Order).ToList();
        (sorted[index], sorted[other]) = (sorted[other], sorted[index]);
        for (int i = 0; i < sorted.Count; i++) settings.For(sorted[i].Id).Order = i;
        save(); UpdateDevices(devices, "Ordem salva; a disposição final da bandeja é controlada pelo Windows.");
        grid.Rows[other].Selected = true;
    }
    public void UpdateDevices(List<BatteryDevice> value, string message)
    {
        devices = value; loading = true;
        try
        {
            grid.Rows.Clear();
            foreach (var d in devices.OrderBy(d => settings.For(d.Id).Order))
            {
                var pref = settings.For(d.Id);
                int i = grid.Rows.Add(pref.Visible, pref.Notifications, d.Name, d.BatteryText, d.Connection,
                    d.Connected ? "Conectado" : "Desconectado", d.Source, d.Type);
                grid.Rows[i].Tag = d.Id;
                grid.Rows[i].Cells[3].Style.ForeColor = d.Percent is int p ? ColorTranslator.FromHtml(settings.ColorFor(p)) : Color.DimGray;
                foreach (DataGridViewCell cell in grid.Rows[i].Cells) cell.ToolTipText = d.Detail + $"\nÚltima consulta: {d.Updated:HH:mm:ss}";
            }
            status.Text = devices.Count == 0 ? "Nenhum periférico compatível detectado. Emparelhe/conecte um dispositivo e atualize." : message;
        }
        finally { loading = false; }
    }
    private void Check(FlowLayoutPanel panel, string text, bool initial, Action<bool> change)
    {
        var c = new CheckBox { Text = text, Checked = initial, AutoSize = true, Margin = new Padding(4, 7, 4, 7) };
        c.CheckedChanged += (_, _) => { change(c.Checked); save(); }; panel.Controls.Add(c);
    }
    private void BuildAppearance()
    {
        var flow = Page("Aparência");
        flow.Controls.Add(new Label { Text = "Ícones dos dispositivos", AutoSize = true, Font = new(Font, FontStyle.Bold) });
        var mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
        mode.Items.AddRange(["Somente número", "Número com %", "Ícone de bateria"]); mode.SelectedIndex = (int)settings.Mode;
        mode.SelectedIndexChanged += (_, _) => { settings.Mode = (IconMode)mode.SelectedIndex; save(); }; flow.Controls.Add(mode);
        Check(flow, "Adaptar contraste ao tema do Windows", settings.AdaptTheme, v => settings.AdaptTheme = v);
        Check(flow, "Fonte em negrito", settings.Bold, v => settings.Bold = v);
        var fontRow = new FlowLayoutPanel { AutoSize = true };
        fontRow.Controls.Add(new Label { Text = "Tamanho da fonte (%)", AutoSize = true });
        var size = new NumericUpDown { Minimum = 60, Maximum = 110, Value = settings.FontPercent, Width = 70 };
        size.ValueChanged += (_, _) => { settings.FontPercent = (int)size.Value; save(); }; fontRow.Controls.Add(size); flow.Controls.Add(fontRow);
        flow.Controls.Add(new Label { Text = "Faixas: valor mínimo inclusivo e cor. A faixa mais alta aplicável vence.", AutoSize = true });
        foreach (var band in settings.Bands.OrderByDescending(b => b.Minimum))
        {
            var row = new FlowLayoutPanel { AutoSize = true };
            var min = new NumericUpDown { Minimum = 0, Maximum = 100, Value = band.Minimum, Width = 70, Enabled = band.Minimum != 0 };
            min.ValueChanged += (_, _) => { band.Minimum = (int)min.Value; save(); };
            var color = Button(band.Color, () => { }); color.BackColor = ColorTranslator.FromHtml(band.Color);
            color.Click += (_, _) =>
            {
                using var dialog = new ColorDialog { Color = ColorTranslator.FromHtml(band.Color), FullOpen = true };
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                band.Color = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
                color.Text = band.Color; color.BackColor = dialog.Color; save();
            };
            row.Controls.Add(min); row.Controls.Add(color); flow.Controls.Add(row);
        }
    }
    private void BuildGeneral()
    {
        var flow = Page("Geral");
        Check(flow, "Usar níveis do Logitech G HUB (exige G HUB em execução)", settings.UseGHub, v => { settings.UseGHub = v; refresh(); });
        flow.Controls.Add(new Label { Text = "Consulta local somente leitura. Integração não oficial; nenhuma estimativa de tensão é feita pelo monitor.", AutoSize = true, MaximumSize = new Size(760, 0), ForeColor = Color.DimGray });
        Check(flow, "Iniciar com Windows", StartupRegistration.Enabled, v =>
        { try { StartupRegistration.Set(v); } catch (Exception e) { MessageBox.Show(this, e.Message, "Inicialização"); } });
        Check(flow, "Iniciar minimizado na bandeja", settings.StartMinimized, v => settings.StartMinimized = v);
        Check(flow, "Notificações de bateria baixa", settings.Notifications, v => settings.Notifications = v);
        Check(flow, "Notificar retorno de conexão", settings.NotifyReconnect, v => settings.NotifyReconnect = v);
        var alert = new NumericUpDown { Minimum = 0, Maximum = 100, Value = settings.AlertPercent, Width = 85 };
        flow.Controls.Add(new Label { Text = "Alertar quando a bateria chegar a (%)", AutoSize = true }); flow.Controls.Add(alert);
        alert.ValueChanged += (_, _) => { settings.AlertPercent = (int)alert.Value; save(); };
        flow.Controls.Add(new Label { Text = "Intervalo de atualização", AutoSize = true });
        var interval = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
        int[] seconds = [30, 60, 300, 600, 900]; interval.Items.AddRange(["30 segundos", "1 minuto", "5 minutos", "10 minutos", "15 minutos"]);
        interval.SelectedIndex = Array.IndexOf(seconds, settings.IntervalSeconds);
        interval.SelectedIndexChanged += (_, _) => { settings.IntervalSeconds = seconds[interval.SelectedIndex]; save(); }; flow.Controls.Add(interval);
        flow.Controls.Add(Button("Atualizar dispositivos agora", refresh));
        flow.Controls.Add(new Label { Text = "Fechar o painel mantém o monitoramento. Para encerrar, use Sair no menu da bandeja.\nConfigurações: %AppData%\\BatteryTrayMonitor\\settings.json", AutoSize = true, MaximumSize = new Size(760, 0), ForeColor = Color.DimGray });
    }
    public void Open(int tab = 0) { tabs.SelectedIndex = tab; Show(); WindowState = FormWindowState.Normal; Activate(); }
}
