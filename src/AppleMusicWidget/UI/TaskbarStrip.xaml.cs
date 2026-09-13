using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Color = System.Windows.Media.Color;
using AppleMusicWidget.Models;
using AppleMusicWidget.Services;
using Microsoft.Win32;

namespace AppleMusicWidget.UI;

/// <summary>
/// 48px strip docked on the taskbar immediately left of the notification area
/// (PLAN §0.1/§0.3). Overlay tool window: WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE so it
/// never steals focus. Position is pushed in physical pixels via SetWindowPos.
/// </summary>
public partial class TaskbarStrip : Window
{
    private const int WidthDip = 340;
    private const int CompactWidthDip = 136;

    private readonly PlayerViewModel _vm;
    private readonly TaskbarService _taskbar;
    private readonly WidgetSettings _settings;

    private IntPtr _hwnd;
    private PlayerFlyout? _flyout;
    private TaskbarService.TaskbarGeometry _geo;
    private bool _fullscreen;
    private bool _collides;
    private bool _compact;
    private readonly DispatcherTimer _occlusionWatch = new() { Interval = TimeSpan.FromSeconds(1) };

    public TaskbarStrip(PlayerViewModel vm, TaskbarService taskbar, WidgetSettings settings)
    {
        InitializeComponent();
        _vm = vm;
        _taskbar = taskbar;
        _settings = settings;
        DataContext = vm;

        ApplyTheme();
        SystemEvents.UserPreferenceChanged += (_, _) => Dispatcher.InvokeAsync(ApplyTheme);

        _taskbar.Changed += g => Dispatcher.InvokeAsync(() => { _geo = g; Reposition(); ApplyVisibility(); });
        _taskbar.FullscreenChanged += fs => Dispatcher.InvokeAsync(() => { _fullscreen = fs; ApplyVisibility(); });
        _taskbar.ForegroundChanged += () => Dispatcher.InvokeAsync(OnForegroundChanged);
        _occlusionWatch.Tick += (_, _) => { RaiseToTop(); UpdateOcclusionWatch(); };
        if (_taskbar.Current is { } g0) _geo = g0;

        _vm.PropertyChanged += OnVmPropertyChanged;
        ProgressTrack.SizeChanged += (_, _) => UpdateProgress();
    }

    private bool _wantVisible;
    /// <summary>Lifecycle's intent; actual visibility also needs taskbar visible, no fullscreen, no collision.</summary>
    public bool WantVisible
    {
        get => _wantVisible;
        set { _wantVisible = value; ApplyVisibility(); }
    }

    // ---------- positioning ----------

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        var ex = Native.GetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLongPtr(_hwnd, Native.GWL_EXSTYLE,
            new IntPtr(ex.ToInt64() | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE));
        Reposition();
        ApplyVisibility();
    }

    private void Reposition()
    {
        if (_hwnd == IntPtr.Zero || !_geo.IsVisible) return;
        var scale = _geo.Dpi;
        var hPx = (int)Math.Round(_geo.TaskbarRect.Height);
        var right = (int)Math.Round(_geo.TrayRect.Left) - _settings.TaskbarGapPx;

        int widthDip = WidthDip;
        if (_geo.AppAreaRect.Width > 0)
        {
            var avail = right - ((int)Math.Round(_geo.AppAreaRect.Right) + 8);
            if (avail >= (int)Math.Round(WidthDip * scale)) widthDip = WidthDip;
            else if (avail >= (int)Math.Round(CompactWidthDip * scale)) widthDip = CompactWidthDip;
            else widthDip = 0;
        }
        if (widthDip == 0)
        {
            _collides = true;
            Debug.WriteLine($"[TaskbarStrip] collision with app area (apps end {_geo.AppAreaRect.Right}); hidden");
            ApplyVisibility();
            return;
        }
        _collides = false;
        SetCompactMode(widthDip == CompactWidthDip);

        var wPx = (int)Math.Round(widthDip * scale);
        var x = right - wPx;
        var y = (int)Math.Round(_geo.TaskbarRect.Top);
        Height = _geo.TaskbarRect.Height / scale;
        Width = widthDip;
        // HWND_TOPMOST on every reposition: a recreated taskbar (Explorer restart)
        // can land above us in the topmost band.
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, wPx, hPx,
            Native.SWP_NOACTIVATE);
    }

    private void SetCompactMode(bool compact)
    {
        if (_compact == compact) return;
        _compact = compact;
        ArtworkColumn.Width = compact ? new GridLength(0) : new GridLength(32);
        TrackGapColumn.Width = compact ? new GridLength(0) : new GridLength(10);
        TrackTextColumn.Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        TrackHoverBorder.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
    }

    // Shell flyouts promote the taskbar to a higher z-band; while occluded, keep re-asserting.
    private void OnForegroundChanged()
    {
        RaiseToTop();
        UpdateOcclusionWatch();
    }

    // Clicking the taskbar raises Shell_TrayWnd above us in the topmost band; re-assert.
    private void RaiseToTop()
    {
        if (_hwnd == IntPtr.Zero || !IsVisible) return;
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    // True while the top-level window at our center belongs to another process
    // (taskbar in the promoted z-band, a shell flyout, or any other topmost window).
    private bool IsOccluded()
    {
        if (_hwnd == IntPtr.Zero || !IsVisible) return false;
        if (!Native.GetWindowRect(_hwnd, out var r)) return false;
        var root = Native.GetAncestor(Native.WindowFromPoint(new Native.POINT
        {
            X = (r.Left + r.Right) / 2,
            Y = (r.Top + r.Bottom) / 2,
        }), Native.GA_ROOT);
        if (root == _hwnd) return false;
        Native.GetWindowThreadProcessId(root, out var pid);
        return pid != Environment.ProcessId;
    }

    private void UpdateOcclusionWatch()
    {
        if (IsOccluded())
        {
            if (!_occlusionWatch.IsEnabled) _occlusionWatch.Start();
        }
        else _occlusionWatch.Stop();
    }

    private void ApplyVisibility()
    {
        var show = WantVisible && _geo.IsVisible && !_fullscreen && !_collides;
        if (show && !IsVisible)
        {
            Show();
            show = WantVisible && _geo.IsVisible && !_fullscreen && !_collides;
        }
        if (!show) Hide();
        if (!show) _flyout?.Hide(); // no anchor while the strip is hidden
        UpdateOcclusionWatch();
    }

    // ---------- theme (follows SystemUsesLightTheme — the taskbar's theme) ----------

    private void ApplyTheme()
    {
        bool light = Theme.SystemUsesLightTheme;
        Set("TxtPrimary", light ? Color.FromArgb(0xE4, 0x00, 0x00, 0x00) : Colors.White);
        Set("TxtSecondary", light ? Color.FromArgb(0x9B, 0x00, 0x00, 0x00) : Color.FromArgb(0xC5, 0xFF, 0xFF, 0xFF));
        Set("TxtDisabled", light ? Color.FromArgb(0x66, 0x00, 0x00, 0x00) : Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
        // The real taskbar hover lightens (whitish pill), it does not darken.
        Set("BtnHover", light ? Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
        Set("BtnPressed", light ? Color.FromArgb(0x20, 0x00, 0x00, 0x00) : Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        Set("TrackHover", light ? Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
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

    private void OnArtworkSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ArtworkImage.ActualWidth <= 0 || ArtworkImage.ActualHeight <= 0) return;
        ArtworkImage.Clip = new RectangleGeometry(
            new Rect(0, 0, ArtworkImage.ActualWidth, ArtworkImage.ActualHeight), 4, 4);
    }

    // The transport buttons handle their own clicks; only the text/artwork area
    // reaches Root -> toggle the flyout.
    private void OnBodyClick(object sender, MouseButtonEventArgs e)
    {
        _flyout ??= new PlayerFlyout(_vm, _taskbar, _settings, this)
        {
            OpenAppleMusicRequested = AppleMusicActivator.BringToFront,
        };
        _flyout.Toggle();
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
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint GA_ROOT = 2;
        public static readonly IntPtr HWND_TOPMOST = new(-1);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT pt);
        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }
    }
}
