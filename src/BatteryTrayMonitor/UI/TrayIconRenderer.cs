using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using BatteryTrayMonitor.Configuration;
using Microsoft.Win32;

namespace BatteryTrayMonitor.UI;
public static class TrayIconRenderer
{
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    public static bool LightTheme
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"); return key?.GetValue("SystemUsesLightTheme") is int v && v != 0; }
    }
    public static Icon Create(int? percentage, bool connected, Settings settings, int size = 32)
    {
        using var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var color = connected && percentage.HasValue ? ColorTranslator.FromHtml(settings.ColorFor(percentage.Value)) : Color.Gray;
        if (settings.AdaptTheme) color = Contrast(color, LightTheme);
        using var brush = new SolidBrush(color);
        if (settings.Mode == IconMode.Battery && percentage is int p && connected)
        {
            using var pen = new Pen(color, Math.Max(1, size / 16f));
            g.DrawRectangle(pen, size * .06f, size * .25f, size * .78f, size * .5f);
            g.FillRectangle(brush, size * .87f, size * .4f, size * .07f, size * .2f);
            g.FillRectangle(brush, size * .12f, size * .31f, size * .66f * p / 100f, size * .38f);
        }
        else
        {
            string text = !connected ? "–" : percentage?.ToString() ?? "?";
            if (settings.Mode == IconMode.Percent && percentage.HasValue && connected) text += "%";
            float pixels = size * settings.FontPercent / 100f;
            using var format = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            Font font = new("Segoe UI", pixels, settings.Bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
            try
            {
                while (g.MeasureString(text, font, int.MaxValue, format).Width > size && pixels > 6)
                { font.Dispose(); font = new("Segoe UI", --pixels, settings.Bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel); }
                g.DrawString(text, font, brush, new RectangleF(-1, -1, size + 2, size + 2), format);
            }
            finally { font.Dispose(); }
        }
        var handle = bitmap.GetHicon();
        try { using var owned = Icon.FromHandle(handle); return (Icon)owned.Clone(); }
        finally { DestroyIcon(handle); }
    }
    private static Color Contrast(Color c, bool light)
    {
        double luminance = (c.R * .2126 + c.G * .7152 + c.B * .0722) / 255;
        if (light && luminance > .65) return ControlPaint.Dark(c, .35f);
        if (!light && luminance < .38) return ControlPaint.Light(c, .4f);
        return c;
    }
    public static string Tooltip(string name, int? percent, bool connected, DateTimeOffset updated)
    {
        var suffix = $"\n{(percent.HasValue ? $"Bateria: {percent}%" : "Bateria indisponível")}\n{(connected ? "Conectado" : "Desconectado")} · {updated:HH:mm}";
        int available = Math.Max(0, 63 - suffix.Length);
        return (name.Length > available ? name[..Math.Max(0, available - 1)] + "…" : name) + suffix;
    }
}
