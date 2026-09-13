using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using AppleMusicWidget.Models;
using AppleMusicWidget.Services;
using Microsoft.Win32;
using Color = System.Windows.Media.Color;

namespace AppleMusicWidget.UI;

/// <summary>
/// Phase 4: the 350x150 card flown out upward from TaskbarStrip (PLAN §3.3).
/// ShowActivated=false + WS_EX_NOACTIVATE so it never steals focus; closes on
/// toggle, on any foreground change (an outside click), or when the strip hides.
/// Position is pushed in physical pixels via SetWindowPos like TaskbarStrip:
/// right edge on the strip's right edge, bottom 8px above the taskbar's top.
/// </summary>
public partial class PlayerFlyout : Window
{
    private const int WidthDip = 350;
    private const int HeightDip = 150;
    private const int GapAboveTaskbarPx = 8; // physical px between flyout bottom and taskbar top

    private readonly PlayerViewModel _vm;
    private readonly TaskbarService _taskbar;
    private readonly WidgetSettings _settings;
    private readonly Window _strip; // anchor: clicks inside it must not count as "outside"

    private IntPtr _hwnd;
    private Native.HookProc? _mouseProc; // field: must outlive the hook
    private IntPtr _mouseHook;

    /// <summary>Set by the owner; invoked when the track info area is clicked.</summary>
    public Action? OpenAppleMusicRequested { get; set; }

    public PlayerFlyout(PlayerViewModel vm, TaskbarService taskbar, WidgetSettings settings, Window strip)
    {
        InitializeComponent();
        _vm = vm;
        _taskbar = taskbar;
        _settings = settings;
        _strip = strip;
        DataContext = vm;

        ApplyTheme();
        SystemEvents.UserPreferenceChanged += (_, _) => Dispatcher.InvokeAsync(ApplyTheme);

        // Any foreground change while open = the user clicked outside (neither the
        // flyout nor the strip can take focus), so fold. Reposition while open.
        // Kept as a backstop: EVENT_SYSTEM_FOREGROUND does not fire when the click
        // lands inside the already-active window — the mouse hook below covers that.
        _taskbar.ForegroundChanged += () => Dispatcher.InvokeAsync(() => { if (IsVisible) Hide(); });
        _taskbar.Changed += _ => Dispatcher.InvokeAsync(() => { if (IsVisible) Reposition(); });

        // Low-level mouse hook only while visible: catches outside clicks that
        // produce no foreground change (clicking the already-focused window).
        IsVisibleChanged += (_, _) => { if (IsVisible) InstallMouseHook(); else RemoveMouseHook(); };
        Closed += (_, _) => RemoveMouseHook();

        _vm.PropertyChanged += OnVmPropertyChanged;
        ProgressTrack.SizeChanged += (_, _) => UpdateProgress();
    }

    /// <summary>Called by the strip's body click.</summary>
    public void Toggle()
    {
        if (IsVisible) { Hide(); return; }
        Show();
        Reposition();
    }

    // ---------- positioning ----------

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        var ex = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE,
            new IntPtr(ex.ToInt64() | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE));
        ApplyDwmAttributes();
        Reposition();
    }

    // Real Fluent material instead of an imitation: rounded overlay corners, the
    // transient acrylic system backdrop (same as Start / volume flyouts), and no
    // DWM border line. The window is a normal non-layered HWND (AllowsTransparency
    // is off); transparent pixels in FlyoutBg let the backdrop show through.
    private void ApplyDwmAttributes()
    {
        int corner = Native.DWMWCP_ROUND;
        _ = Native.DwmSetWindowAttribute(_hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        int backdrop = Native.DWMSBT_TRANSIENTWINDOW;
        _ = Native.DwmSetWindowAttribute(_hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
        int border = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        _ = Native.DwmSetWindowAttribute(_hwnd, Native.DWMWA_BORDER_COLOR, ref border, sizeof(int));
    }

    private void Reposition()
    {
        if (_hwnd == IntPtr.Zero) return;
        if (_taskbar.Current is not { IsVisible: true } g) return;
        var scale = g.Dpi;
        var wPx = (int)Math.Round(WidthDip * scale);
        var hPx = (int)Math.Round(HeightDip * scale);
        // Right edge = strip's right edge (tray left - gap); bottom sits above the taskbar.
        var x = (int)Math.Round(g.TrayRect.Left) - _settings.TaskbarGapPx - wPx;
        var y = (int)Math.Round(g.TaskbarRect.Top) - GapAboveTaskbarPx - hPx;
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, wPx, hPx,
            Native.SWP_NOACTIVATE);
    }

    // ---------- outside-click mouse hook (installed only while visible) ----------

    private void InstallMouseHook()
    {
        if (_mouseHook != IntPtr.Zero) return;
        _mouseProc ??= OnMouseHook;
        _mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _mouseProc, IntPtr.Zero, 0);
        if (_mouseHook == IntPtr.Zero)
            Log($"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");
    }

    private void RemoveMouseHook()
    {
        if (_mouseHook == IntPtr.Zero) return;
        Native.UnhookWindowsHookEx(_mouseHook);
        _mouseHook = IntPtr.Zero;
    }

    private IntPtr OnMouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsButtonDown(wParam))
        {
            // MSLLHOOKSTRUCT begins with pt (screen coords).
            var pt = Marshal.PtrToStructure<Native.POINT>(lParam);
            if (!Contains(_hwnd, pt) && !Contains(StripHwnd(), pt))
                Dispatcher.BeginInvoke(new Action(Hide)); // keep the hook callback fast
        }
        return Native.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private static bool IsButtonDown(IntPtr wParam) => (int)wParam is
        Native.WM_LBUTTONDOWN or Native.WM_RBUTTONDOWN or Native.WM_MBUTTONDOWN or
        Native.WM_NCLBUTTONDOWN or Native.WM_NCRBUTTONDOWN or Native.WM_NCMBUTTONDOWN;

    private IntPtr StripHwnd() => new WindowInteropHelper(_strip).Handle;

    private static bool Contains(IntPtr hwnd, Native.POINT pt) =>
        hwnd != IntPtr.Zero
        && Native.GetWindowRect(hwnd, out var r)
        && pt.X >= r.Left && pt.X < r.Right && pt.Y >= r.Top && pt.Y < r.Bottom;

    private static void Log(string msg) => Debug.WriteLine($"[PlayerFlyout] {msg}");

    // ---------- theme (follows SystemUsesLightTheme, same as TaskbarStrip) ----------

    private void ApplyTheme()
    {
        bool light = Theme.SystemUsesLightTheme;
        // Mostly-transparent tints: the acrylic system backdrop provides the material.
        Set("FlyoutBg", light ? Color.FromArgb(0x40, 0xF9, 0xF9, 0xF9) : Color.FromArgb(0x40, 0x20, 0x20, 0x20));
        Set("FlyoutBorder", light ? Color.FromArgb(0x1A, 0x00, 0x00, 0x00) : Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        Set("TxtPrimary", light ? Color.FromArgb(0xE4, 0x00, 0x00, 0x00) : Colors.White);
        Set("TxtSecondary", light ? Color.FromArgb(0x9B, 0x00, 0x00, 0x00) : Color.FromArgb(0xC5, 0xFF, 0xFF, 0xFF));
        Set("TxtDisabled", light ? Color.FromArgb(0x66, 0x00, 0x00, 0x00) : Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
        Set("BtnHover", light ? Color.FromArgb(0x0A, 0x00, 0x00, 0x00) : Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        Set("BtnPressed", light ? Color.FromArgb(0x14, 0x00, 0x00, 0x00) : Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
        // Track area hover needs to be clearly visible over the acrylic material.
        Set("TrackHover", light ? Color.FromArgb(0x17, 0x00, 0x00, 0x00) : Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
        Resources["AccentBrush"] = SystemParameters.WindowGlassBrush;
    }

    private void Set(string key, Color c) => Resources[key] = new SolidColorBrush(c);

    // ---------- progress line / artwork clip ----------

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.ProgressFraction))
            UpdateProgress();
    }

    private void UpdateProgress()
    {
        ProgressFill.Width = _vm.ProgressFraction * ProgressTrack.ActualWidth;
    }

    private void OnTrackClick(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        OpenAppleMusicRequested?.Invoke();

    private void OnArtworkSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ArtworkImage.ActualWidth <= 0 || ArtworkImage.ActualHeight <= 0) return;
        ArtworkImage.Clip = new RectangleGeometry(
            new Rect(0, 0, ArtworkImage.ActualWidth, ArtworkImage.ActualHeight), 8, 8);
    }

    private static class Theme
    {
        public static bool SystemUsesLightTheme
        {
            get
            {
                try
                {
                    var v = Registry.GetValue(
                        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                        "SystemUsesLightTheme", 1);
                    return v is int i ? i != 0 : true;
                }
                catch { return true; }
            }
        }
    }

    private static class Native
    {
        public const int GWL_EXSTYLE = -20;
        public const long WS_EX_TOOLWINDOW = 0x00000080;
        public const long WS_EX_NOACTIVATE = 0x08000000;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const int WH_MOUSE_LL = 14;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_RBUTTONDOWN = 0x0204;
        public const int WM_MBUTTONDOWN = 0x0207;
        public const int WM_NCLBUTTONDOWN = 0x00A1;
        public const int WM_NCRBUTTONDOWN = 0x00A4;
        public const int WM_NCMBUTTONDOWN = 0x00A7;
        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        public const int DWMWA_BORDER_COLOR = 34;
        public const int DWMWCP_ROUND = 2;
        public const int DWMSBT_TRANSIENTWINDOW = 3;
        public static readonly IntPtr HWND_TOPMOST = new(-1);

        public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }
    }
}
