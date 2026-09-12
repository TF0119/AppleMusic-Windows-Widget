using System.Drawing;
using System.Windows.Forms;
using AppleMusicWidget.Models;

namespace AppleMusicWidget.Services;

/// <summary>
/// Taskbar tray icon. Minimal menu per PLAN §9.6; WinForms NotifyIcon is used
/// because WPF has no built-in tray support.
/// </summary>
public sealed class TrayService : IDisposable
{
    public event Action? ToggleVisibilityRequested;
    public event Action? ExitRequested;

    private readonly NotifyIcon _icon;
    private readonly Icon _trayIcon;

    public TrayService(WidgetSettings settings)
    {
        _trayIcon = CreateIcon();
        var menu = new ContextMenuStrip();
        menu.Items.Add("表示 / 非表示", null, (_, _) => ToggleVisibilityRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => ExitRequested?.Invoke());

        _icon = new NotifyIcon
        {
            Icon = _trayIcon,
            Text = "Apple Music Widget",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => ToggleVisibilityRequested?.Invoke();
    }

    private static Icon CreateIcon()
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(Color.FromArgb(250, 45, 66)); // Apple Music red
            g.FillEllipse(brush, 1, 1, 14, 14);
        }
        // GetHicon creates a handle that outlives the bitmap; destroyed in Dispose.
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _trayIcon.Dispose();
    }
}
