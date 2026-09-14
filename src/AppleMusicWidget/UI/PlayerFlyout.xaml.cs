using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
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
    private const int QueueHeightDip = 360;
    private const int GapAboveTaskbarPx = 8; // physical px between flyout bottom and taskbar top
    private const double QueueScrollDurationMs = 140;

    private sealed record QueueViewportBookmark(PlayQueueItem Item, int Occurrence, double Y);

    private readonly PlayerViewModel _vm;
    private readonly TaskbarService _taskbar;
    private readonly WidgetSettings _settings;
    private readonly Window _strip; // anchor: clicks inside it must not count as "outside"

    private IntPtr _hwnd;
    private Native.HookProc? _mouseProc; // field: must outlive the hook
    private IntPtr _mouseHook;
    private bool _queueVisible;
    private bool _queueLoading;
    private bool _suppressForegroundClose;
    private int _queueRefreshVersion;
    private bool _queueReloadPending;
    private static readonly TimeSpan QueueRecoverySettleDelay = TimeSpan.FromMilliseconds(700);
    private PlayQueueSnapshot? _queueCache;
    private bool _queueCacheDirty = true;
    private string _observedTrackTitle = "";
    private string _observedTrackSubtitle = "";
    private TimeSpan _observedTrackDuration;
    private bool _observedTrackIdle = true;
    private int _queueLoadVersion;
    private ScrollViewer? _queueScrollViewer;
    private double _queueScrollStart;
    private double _queueScrollTarget;
    private long _queueScrollStarted;
    private bool _queueScrollAnimating;

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
        _observedTrackTitle = _vm.Title;
        _observedTrackSubtitle = _vm.Subtitle;
        _observedTrackDuration = _vm.Duration;
        _observedTrackIdle = _vm.IsIdle;

        ApplyTheme();
        SystemEvents.UserPreferenceChanged += (_, _) => Dispatcher.InvokeAsync(ApplyTheme);

        // Any foreground change while open = the user clicked outside (neither the
        // flyout nor the strip can take focus), so fold. Reposition while open.
        // Kept as a backstop: EVENT_SYSTEM_FOREGROUND does not fire when the click
        // lands inside the already-active window — the mouse hook below covers that.
        _taskbar.ForegroundChanged += () => Dispatcher.InvokeAsync(() => { if (IsVisible && !_suppressForegroundClose) Hide(); });
        _taskbar.Changed += _ => Dispatcher.InvokeAsync(() => { if (IsVisible) Reposition(); });

        // Low-level mouse hook only while visible: catches outside clicks that
        // produce no foreground change (clicking the already-focused window).
        IsVisibleChanged += (_, _) => { if (IsVisible) InstallMouseHook(); else { RemoveMouseHook(); ShowPlayerView(); } };
        Closed += (_, _) => RemoveMouseHook();

        _vm.PropertyChanged += OnVmPropertyChanged;
        ProgressTrack.SizeChanged += (_, _) => UpdateProgress();
    }

    /// <summary>Called by the strip's body click.</summary>
    public void Toggle()
    {
        if (IsVisible) { Hide(); return; }
        ShowPlayerView();
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
        var hPx = (int)Math.Round((_queueVisible ? QueueHeightDip : HeightDip) * scale);
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
        // Match the shell's hover: a clearly visible lightening, not a dark tint.
        Set("TrackHover", light ? Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
        var accent = SystemParameters.WindowGlassColor;
        Set("CurrentTrackBg", Color.FromArgb(light ? (byte)0x24 : (byte)0x38, accent.R, accent.G, accent.B));
        Resources["AccentBrush"] = SystemParameters.WindowGlassBrush;
    }

    private void Set(string key, Color c) => Resources[key] = new SolidColorBrush(c);

    // ---------- progress line / artwork clip ----------

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.ProgressFraction))
            UpdateProgress();
        if (e.PropertyName == nameof(PlayerViewModel.Duration))
            _observedTrackDuration = _vm.Duration;
        if (e.PropertyName == nameof(PlayerViewModel.IsIdle))
            _observedTrackIdle = _vm.IsIdle;
        if (e.PropertyName == nameof(PlayerViewModel.TrackVersion))
        {
            var prevTitle = _observedTrackTitle;
            var prevSubtitle = _observedTrackSubtitle;
            var prevDuration = _observedTrackDuration;
            var prevIdle = _observedTrackIdle;
            _observedTrackTitle = _vm.Title;
            _observedTrackSubtitle = _vm.Subtitle;
            _observedTrackDuration = _vm.Duration;
            _observedTrackIdle = _vm.IsIdle;
            _queueRefreshVersion++;
            if (_vm.IsIdle)
            {
                _queueCache = null;
                _queueCacheDirty = true;
                return;
            }
            if (_queueCache is null)
            {
                if (_queueLoading) _queueReloadPending = true;
                return;
            }
            _queueCache = EstimateAdvancedCache(_queueCache, prevTitle, prevSubtitle, prevDuration, prevIdle, _vm.Title);
            _queueCacheDirty = true;
            if (_queueVisible && IsVisible)
                ApplyQueueSnapshot(_queueCache, preserveViewport: true);
        }
    }

    private static PlayQueueSnapshot EstimateAdvancedCache(
        PlayQueueSnapshot cache, string prevTitle, string prevSubtitle, TimeSpan prevDuration, bool prevIdle, string newTitle)
    {
        var history = cache.History.ToList();
        if (!prevIdle && prevTitle.Length > 0)
        {
            history.Insert(0, new PlayQueueItem(prevTitle, prevSubtitle, FormatQueueDuration(prevDuration)));
            if (history.Count > 50) history.RemoveRange(50, history.Count - 50);
        }
        var upcoming = cache.Upcoming.ToList();
        if (upcoming.Count > 0 && string.Equals(upcoming[0].Title, newTitle, StringComparison.Ordinal))
            upcoming.RemoveAt(0);
        return new PlayQueueSnapshot(history, upcoming);
    }

    private static string FormatQueueDuration(TimeSpan t)
        => t <= TimeSpan.Zero ? "" : t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
            : $"{(int)t.TotalMinutes}:{t.Seconds:D2}";

    private async Task RecoverQueueAfterSettleAsync(int version)
    {
        await Task.Delay(QueueRecoverySettleDelay);
        if (version != _queueRefreshVersion || !_queueVisible || !IsVisible || _queueCache is not null || _queueLoading) return;
        await LoadQueueAsync(showUpcomingEarly: true);
    }

    private void UpdateProgress()
    {
        ProgressFill.Width = _vm.ProgressFraction * ProgressTrack.ActualWidth;
    }

    private async void OnPlayQueueClick(object sender, RoutedEventArgs e) => await ShowQueueAsync();
    private async void OnQueueRefreshClick(object sender, RoutedEventArgs e)
    {
        _queueRefreshVersion++;
        await LoadQueueAsync(showUpcomingEarly: false);
    }
    private void OnQueueBackClick(object sender, RoutedEventArgs e) => ShowPlayerView();

    private void OnQueueSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (QueueItems.SelectedItem is not null) QueueItems.SelectedItem = null;
    }

    private ScrollViewer? QueueScrollViewer() => _queueScrollViewer ??= FindVisualChild<ScrollViewer>(QueueItems);

    private void StopQueueScroll()
    {
        if (!_queueScrollAnimating) return;
        _queueScrollAnimating = false;
        CompositionTarget.Rendering -= OnQueueScrollFrame;
    }

    private void OnQueuePreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var scroll = QueueScrollViewer();
        if (scroll is null || scroll.ScrollableHeight <= 0) return;
        var lines = SystemParameters.WheelScrollLines;
        if (lines == 0) return;
        var distance = lines < 0 ? scroll.ViewportHeight : Math.Max(48, lines * 16);
        var delta = -e.Delta / 120.0 * distance;
        var baseOffset = _queueScrollAnimating ? _queueScrollTarget : scroll.VerticalOffset;
        _queueScrollStart = scroll.VerticalOffset;
        _queueScrollTarget = Math.Clamp(baseOffset + delta, 0, scroll.ScrollableHeight);
        e.Handled = true;
        if (!SystemParameters.ClientAreaAnimation)
        {
            StopQueueScroll();
            scroll.ScrollToVerticalOffset(_queueScrollTarget);
            return;
        }
        _queueScrollStarted = Stopwatch.GetTimestamp();
        if (_queueScrollAnimating) return;
        _queueScrollAnimating = true;
        CompositionTarget.Rendering += OnQueueScrollFrame;
    }

    private void OnQueueDirectManipulationStarted(object sender, RoutedEventArgs e) => StopQueueScroll();

    private void OnQueueScrollFrame(object? sender, EventArgs e)
    {
        var scroll = QueueScrollViewer();
        if (!_queueScrollAnimating || scroll is null) { StopQueueScroll(); return; }
        var elapsed = Stopwatch.GetElapsedTime(_queueScrollStarted).TotalMilliseconds;
        var progress = Math.Clamp(elapsed / QueueScrollDurationMs, 0, 1);
        var eased = 1 - Math.Pow(1 - progress, 3);
        scroll.ScrollToVerticalOffset(_queueScrollStart + ((_queueScrollTarget - _queueScrollStart) * eased));
        if (progress >= 1) StopQueueScroll();
    }

    private QueueViewportBookmark? CaptureQueueViewportBookmark()
    {
        var scroll = QueueScrollViewer();
        if (scroll is null || QueueItems.Items.Count == 0) return null;
        ListBoxItem? topmost = null;
        var topmostY = double.MaxValue;
        for (var i = 0; i < QueueItems.Items.Count; i++)
        {
            if (QueueItems.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem container) continue;
            if (!container.IsVisible) continue;
            var y = container.TransformToAncestor(scroll).Transform(new Point()).Y;
            if (y + container.ActualHeight > 0 && y < topmostY) { topmostY = y; topmost = container; }
        }
        if (topmost is null || QueueItems.ItemContainerGenerator.ItemFromContainer(topmost) is not PlayQueueItem item) return null;
        var index = QueueItems.ItemContainerGenerator.IndexFromContainer(topmost);
        var occurrence = 0;
        for (var i = 0; i < index; i++)
            if (Equals(QueueItems.Items[i], item)) occurrence++;
        return new QueueViewportBookmark(item, occurrence, topmostY);
    }

    private static int IndexOfOccurrence(IReadOnlyList<PlayQueueItem> rows, PlayQueueItem item, int occurrence)
    {
        var seen = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            if (!rows[i].Equals(item)) continue;
            if (seen == occurrence) return i;
            seen++;
        }
        return -1;
    }

    private void RestoreQueueBookmark(int index, double savedY)
    {
        if (!_queueVisible || !IsVisible) return;
        StopQueueScroll();
        if (index < 0 || index >= QueueItems.Items.Count) return;
        QueueItems.ScrollIntoView(QueueItems.Items[index]);
        QueueItems.UpdateLayout();
        var scroll = QueueScrollViewer();
        if (scroll is null || QueueItems.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem container) return;
        var y = container.TransformToAncestor(scroll).Transform(new Point()).Y;
        scroll.ScrollToVerticalOffset(scroll.VerticalOffset + (y - savedY));
    }

    private async Task ShowQueueAsync()
    {
        _queueVisible = true;
        PlayerView.Visibility = Visibility.Collapsed;
        QueueView.Visibility = Visibility.Visible;
        Height = QueueHeightDip;
        Reposition();
        if (_queueCache is not null)
        {
            ApplyQueueSnapshot(_queueCache, preserveViewport: false);
            return;
        }
        ApplyQueueSnapshot(new PlayQueueSnapshot([], []), preserveViewport: false, historyLoading: true);
        QueueStatus.Text = "再生キューを読み込み中…";
        await LoadQueueAsync(showUpcomingEarly: true);
    }

    private void ShowPlayerView()
    {
        StopQueueScroll();
        _queueVisible = false;
        _queueRefreshVersion++;
        _queueReloadPending = false;
        PlayerView.Visibility = Visibility.Visible;
        QueueView.Visibility = Visibility.Collapsed;
        Height = HeightDip;
        QueueItems.ItemsSource = null;
        QueueStatus.Text = "";
        Reposition();
    }

    private void ApplyQueueSnapshot(PlayQueueSnapshot snapshot, bool preserveViewport, bool historyLoading = false)
    {
        var rows = new List<PlayQueueItem>();
        if (snapshot.History.Count > 0)
        {
            rows.Add(new PlayQueueItem("履歴", "", "", true));
            for (var i = snapshot.History.Count - 1; i >= 0; i--) rows.Add(snapshot.History[i]);
        }
        var nextHeader = new PlayQueueItem("次に再生", "", "", true);
        PlayQueueItem anchor;
        if (!_vm.IsIdle)
        {
            anchor = new PlayQueueItem("", "", "", IsCurrent: true);
            rows.Add(anchor);
        }
        else
        {
            anchor = nextHeader;
        }
        rows.Add(nextHeader);
        rows.AddRange(snapshot.Upcoming);
        var bookmark = preserveViewport ? CaptureQueueViewportBookmark() : null;
        QueueItems.ItemsSource = rows;
        var estimated = _queueCacheDirty && !historyLoading;
        QueueStatus.Text = (historyLoading
            ? $"次に再生 {snapshot.Upcoming.Count} 曲　·　履歴を読み込み中…"
            : snapshot.History.Count > 0
                ? $"履歴 {snapshot.History.Count} 曲　·　次に再生 {snapshot.Upcoming.Count} 曲"
                : $"次に再生 {snapshot.Upcoming.Count} 曲") + (estimated ? "　·　推定" : "");
        QueueStatus.ToolTip = estimated
            ? "曲送りから推定した表示です。更新ボタンでApple Musicと同期します。"
            : null;
        var bookmarkIndex = bookmark is null ? -1 : IndexOfOccurrence(rows, bookmark.Item, bookmark.Occurrence);
        if (bookmarkIndex >= 0)
            _ = Dispatcher.BeginInvoke(new Action(() => RestoreQueueBookmark(bookmarkIndex, bookmark!.Y)), DispatcherPriority.Loaded);
        else
            _ = Dispatcher.BeginInvoke(new Action(() => ScrollItemToTop(anchor)), DispatcherPriority.Loaded);
    }

    private async Task LoadQueueAsync(bool showUpcomingEarly)
    {
        if (_queueLoading)
        {
            if (_queueCacheDirty) _queueReloadPending = true;
            return;
        }
        _queueLoading = true;
        _suppressForegroundClose = true;
        QueueRefreshButton.IsEnabled = false;
        var loadVersion = ++_queueLoadVersion;
        var loadTrackVersion = _vm.TrackVersion;
        var coldLoad = showUpcomingEarly && _queueCache is null;
        var upcomingAvailable = 0;
        if (!coldLoad)
            QueueStatus.Text = QueueItems.Items.Count > 0 ? "更新中…" : "読み込み中…";
        try
        {
            var result = await AppleMusicUiAutomation.ReadPlayQueueAsync(
                coldLoad
                    ? upcoming =>
                    {
                        Interlocked.Exchange(ref upcomingAvailable, 1);
                        try
                        {
                            _ = Dispatcher.BeginInvoke(new Action(() =>
                            {
                                if (!_queueVisible || !IsVisible || loadVersion != _queueLoadVersion || _queueCache is not null || _vm.TrackVersion != loadTrackVersion) return;
                                ApplyQueueSnapshot(new PlayQueueSnapshot([], upcoming), QueueItems.Items.Count > 0, historyLoading: true);
                            }));
                        }
                        catch (Exception ex) { Debug.WriteLine($"[PlayerFlyout] queue progressive: {ex.Message}"); }
                    }
                    : null);
            var resultIsCurrent = _vm.TrackVersion == loadTrackVersion;
            if (result is not null && resultIsCurrent)
            {
                _queueCache = result;
                _queueCacheDirty = false;
            }
            if (!_queueVisible || !IsVisible) return;
            if (result is null)
            {
                if (_queueCache is not null)
                    ApplyQueueSnapshot(_queueCache, preserveViewport: true);
                else if (Volatile.Read(ref upcomingAvailable) != 0 && QueueItems.Items.Count > 0)
                    QueueStatus.Text = "履歴を取得できませんでした";
                else
                    QueueStatus.Text = "再生待ちリストを取得できませんでした";
            }
            else if (!resultIsCurrent)
                return;
            else if (result.History.Count == 0 && result.Upcoming.Count == 0)
            {
                QueueItems.ItemsSource = null;
                QueueStatus.Text = "再生待ちと履歴はありません";
            }
            else
                ApplyQueueSnapshot(result, preserveViewport: QueueItems.Items.Count > 0);
        }
        finally
        {
            QueueRefreshButton.IsEnabled = true;
            await Task.Delay(100);
            _suppressForegroundClose = false;
            _queueLoading = false;
            var trackChangedDuringLoad = _vm.TrackVersion != loadTrackVersion;
            var reschedule = (_queueReloadPending || trackChangedDuringLoad)
                && _queueCache is null && _queueVisible && IsVisible;
            _queueReloadPending = false;
            if (reschedule) _ = RecoverQueueAfterSettleAsync(++_queueRefreshVersion);
        }
    }

    private void ScrollItemToTop(object item)
    {
        if (!_queueVisible || !IsVisible) return;
        StopQueueScroll();
        QueueItems.ScrollIntoView(item);
        QueueItems.UpdateLayout();
        var scroll = FindVisualChild<ScrollViewer>(QueueItems);
        if (scroll is null || QueueItems.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem container) return;
        var y = container.TransformToAncestor(scroll).Transform(new Point()).Y;
        scroll.ScrollToVerticalOffset(scroll.VerticalOffset + y);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var found = FindVisualChild<T>(child);
            if (found != null) return found;
        }
        return null;
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
