using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AppleMusicWidget.Models;
using AppleMusicWidget.Services;

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

    private IntPtr _hwnd;
    private bool _isScrubbing;

    public PlayerFlyout(PlayerViewModel vm, TaskbarService taskbar, WidgetSettings settings)
    {
        InitializeComponent();
        _vm = vm;
        _taskbar = taskbar;
        _settings = settings;
        DataContext = vm;

        // Any foreground change while open = the user clicked outside (neither the
        // flyout nor the strip can take focus), so fold. Reposition while open.
        _taskbar.ForegroundChanged += () => Dispatcher.InvokeAsync(() => { if (IsVisible) Hide(); });
        _taskbar.Changed += _ => Dispatcher.InvokeAsync(() => { if (IsVisible) Reposition(); });

        _vm.PropertyChanged += OnVmPropertyChanged;
        Loaded += (_, _) =>
        {
            SeekSlider.Value = _vm.ProgressFraction;
            SeekSlider.ApplyTemplate();
            if (SeekSlider.Template.FindName("PART_Track", SeekSlider) is Track track)
            {
                track.Thumb.DragStarted += (_, _) => _isScrubbing = true;
                track.Thumb.DragCompleted += (_, _) =>
                {
                    _vm.SeekTo(SeekSlider.Value);
                    _isScrubbing = false;
                };
            }
        };
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
        Reposition();
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

    // ---------- seek slider / artwork clip ----------

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.ProgressFraction) && !_isScrubbing)
            SeekSlider.Value = _vm.ProgressFraction;
    }

    private void OnSeekDown(object sender, MouseButtonEventArgs e) => _isScrubbing = true;

    private void OnSeekUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isScrubbing) return;
        _vm.SeekTo(SeekSlider.Value);
        _isScrubbing = false;
    }

    private void OnArtworkSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ArtworkImage.ActualWidth <= 0 || ArtworkImage.ActualHeight <= 0) return;
        ArtworkImage.Clip = new RectangleGeometry(
            new Rect(0, 0, ArtworkImage.ActualWidth, ArtworkImage.ActualHeight), 8, 8);
    }

    private static class Native
    {
        public const int GWL_EXSTYLE = -20;
        public const long WS_EX_TOOLWINDOW = 0x00000080;
        public const long WS_EX_NOACTIVATE = 0x08000000;
        public const uint SWP_NOACTIVATE = 0x0010;
        public static readonly IntPtr HWND_TOPMOST = new(-1);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    }
}
