using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using AppleMusicWidget.Media;
using AppleMusicWidget.Models;
using AppleMusicWidget.Services;
using AppleMusicWidget.UI;
using PlaybackState = AppleMusicWidget.Media.PlaybackState;

namespace AppleMusicWidget.Lifecycle;

/// <summary>
/// Central state machine. Shows/hides the taskbar strip purely from Apple Music's
/// process lifecycle; owns all resource acquisition/release per state.
/// Phase 2.5: visible surface is TaskbarStrip docked left of the tray.
/// </summary>
public sealed class WidgetLifecycleController
{
    private readonly WidgetSettings _settings;
    private readonly IMediaSessionProvider _provider;
    private readonly TaskbarService _taskbar;
    private TaskbarStrip? _strip;
    private PlayerViewModel? _viewModel;

    public AppState State { get; private set; } = AppState.WaitingForAppleMusic;
    public event Action<AppState>? StateChanged;

    public WidgetLifecycleController(WidgetSettings settings, IMediaSessionProvider provider, TaskbarService taskbar)
    {
        _settings = settings;
        _provider = provider;
        _taskbar = taskbar;
        _provider.SessionConnected += () => OnUi(() => Transition(AppState.AppleMusicIdle));
        _provider.PlaybackChanged += s => OnUi(() => Transition(s.State switch
        {
            PlaybackState.Playing => AppState.Playing,
            PlaybackState.Paused => AppState.Paused,
            _ => AppState.AppleMusicIdle, // Opened / Stopped / Unknown
        }));
        _provider.SessionLost += () => OnUi(() =>
        {
            // PLAN §7.3: process still alive -> keep the strip, wait for rebind.
            if (State is not (AppState.WaitingForAppleMusic or AppState.AppleMusicClosing))
                Transition(AppState.Recovering);
        });
    }

    /// <summary>Called from any thread.</summary>
    public void OnAppleMusicStarted(Process process)
    {
        OnUi(() =>
        {
            if (_settings.ShowWhenAppleMusicLaunches)
                EnsureWidget().WantVisible = true;
            Transition(AppState.AppleMusicStarting);
            _ = _provider.ConnectAsync();
        });
    }

    /// <summary>Called from any thread.</summary>
    public void OnAppleMusicExited()
    {
        OnUi(() =>
        {
            Transition(AppState.AppleMusicClosing);
            ReleaseResources();
            if (_settings.HideWhenAppleMusicClosed && _strip is not null)
                _strip.WantVisible = false;
            Transition(AppState.WaitingForAppleMusic);
            TrimDormantWorkingSet();
        });
    }

    /// <summary>Drop the dormant footprint once the widget is hidden with no session.</summary>
    private static void TrimDormantWorkingSet()
    {
        var before = Environment.WorkingSet;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Native.SetProcessWorkingSetSize(Native.GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1));
        Debug.WriteLine($"[Lifecycle] ws trim: {before / 1048576.0:F1}MB -> {Environment.WorkingSet / 1048576.0:F1}MB");
    }

    /// <summary>Tray "show/hide" toggle. Only meaningful while Apple Music runs.</summary>
    public void ToggleWidgetVisibility()
    {
        var w = _strip;
        if (w is null) return;
        w.WantVisible = !w.IsVisible;
    }

    private TaskbarStrip EnsureWidget()
    {
        if (_strip is not null) return _strip;
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        _viewModel = new PlayerViewModel(_provider, dispatcher);
        _strip = new TaskbarStrip(_viewModel, _taskbar, _settings);
        return _strip;
    }

    private void ReleaseResources()
    {
        _provider.Disconnect();
        _viewModel?.Reset();
    }

    private void Transition(AppState next)
    {
        if (State == next) return;
        State = next;
        StateChanged?.Invoke(next);
    }

    /// <summary>Marshal onto the UI thread; provider events arrive on thread-pool threads.</summary>
    private static void OnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return;
        dispatcher.InvokeAsync(action);
    }

    private static class Native
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        public static extern System.IntPtr GetCurrentProcess();
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetProcessWorkingSetSize(System.IntPtr hProcess, System.IntPtr min, System.IntPtr max);
    }
}
