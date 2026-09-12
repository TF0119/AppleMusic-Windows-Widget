using System.Windows;
using AppleMusicWidget.Lifecycle;
using AppleMusicWidget.Media;
using AppleMusicWidget.Models;
using AppleMusicWidget.Services;

namespace AppleMusicWidget;

public partial class App : System.Windows.Application
{
    private AppleMusicProcessMonitor? _monitor;
    private WidgetLifecycleController? _lifecycle;
    private TrayService? _tray;
    private TaskbarService? _taskbar;
    private readonly SettingsService _settingsService = new();
    private WidgetSettings _settings = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

#if DEBUG
        // Debug-only file sink so TaskbarService/Provider Debug output can be inspected.
        var log = new System.Diagnostics.TextWriterTraceListener(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "amw-debug.log"));
        System.Diagnostics.Trace.Listeners.Add(log);
        System.Diagnostics.Trace.AutoFlush = true;
#endif

        _settings = _settingsService.Load();
        _taskbar = new TaskbarService(); // UI thread: SetWinEventHook needs this message loop
        _lifecycle = new WidgetLifecycleController(_settings, new GsmtcSessionProvider(), _taskbar);

        _tray = new TrayService(_settings);
        _tray.ToggleVisibilityRequested += () => _lifecycle.ToggleWidgetVisibility();
        _tray.ExitRequested += () => Shutdown();

        _monitor = new AppleMusicProcessMonitor();
        _monitor.Started += p => _lifecycle.OnAppleMusicStarted(p);
        _monitor.Exited += () => _lifecycle.OnAppleMusicExited();
        _monitor.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _settingsService.Save(_settings);
        _monitor?.Dispose();
        _taskbar?.Dispose();
        _tray?.Dispose();
        base.OnExit(e);
    }
}
