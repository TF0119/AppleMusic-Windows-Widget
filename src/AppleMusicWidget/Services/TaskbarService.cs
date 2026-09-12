using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace AppleMusicWidget.Services;

/// <summary>
/// Tracks the main taskbar (Shell_TrayWnd) geometry in physical pixels and
/// raises <see cref="Changed"/> event-driven via SetWinEventHook on explorer.exe
/// (PLAN §0.4 — no polling; the only timer is a 2s retry while the taskbar is
/// missing, e.g. Explorer restarting, plus a 100ms debounce for LOCATIONCHANGE
/// animation spam).
/// Must be constructed on the UI thread (the hooks need its message loop).
/// </summary>
public sealed class TaskbarService : IDisposable
{
    public record struct TaskbarGeometry(Rect TaskbarRect, Rect TrayRect, Rect AppAreaRect, double Dpi, bool IsVisible);

    public event Action<TaskbarGeometry>? Changed;
    public event Action<bool>? FullscreenChanged;

    private const int EventObjectDestroy = 0x8001;
    private const int EventObjectShow = 0x8002;
    private const int EventObjectHide = 0x8003;
    private const int EventSystemForeground = 0x0003;
    private const int EventObjectLocationChange = 0x800B;
    private const uint WinEventOutOfContext = 0x0000;
    private const uint WinEventSkipOwnProcess = 0x0002;

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _debounce;
    private readonly DispatcherTimer _missingRetry;
    private readonly Native.WinEventProc _winEventProc; // field: must outlive the hooks

    private IntPtr _tray, _trayNotify, _start, _appArea;
    private IntPtr _hLocHook, _hObjHook, _hFgHook;
    private uint _explorerPid;
    private TaskbarGeometry _current;
    private bool _hasCurrent;
    private bool _fullscreen;
    private bool _disposed;

    public TaskbarService()
    {
        _winEventProc = OnWinEvent;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Recompute(); };
        _missingRetry = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _missingRetry.Tick += (_, _) => { if (_tray == IntPtr.Zero) { Relocate(); Recompute(); } };

        SystemEvents.DisplaySettingsChanged += OnSystemEvent;
        SystemEvents.UserPreferenceChanged += OnSystemEvent;

        _hFgHook = Native.SetWinEventHook(EventSystemForeground, EventSystemForeground,
            IntPtr.Zero, _winEventProc, 0, 0, WinEventOutOfContext | WinEventSkipOwnProcess);

        Relocate();
        Recompute();
    }

    public TaskbarGeometry? Current => _hasCurrent ? _current : null;
    public bool IsForegroundFullscreen => _fullscreen;

    // ---------- location ----------

    private void Relocate()
    {
        var prevTray = _tray;
        _tray = Native.FindWindow("Shell_TrayWnd", null);
        if (_tray == IntPtr.Zero)
        {
            _trayNotify = _start = _appArea = IntPtr.Zero;
            _explorerPid = 0;
            if (!_missingRetry.IsEnabled) _missingRetry.Start();
            Log("Shell_TrayWnd not found (Explorer restarting?)");
            return;
        }
        _missingRetry.Stop();
        Native.GetWindowThreadProcessId(_tray, out _explorerPid);

        _trayNotify = Native.FindWindowEx(_tray, IntPtr.Zero, "TrayNotifyWnd", null);
        _start = Native.FindWindowEx(_tray, IntPtr.Zero, "Start", null);
        _appArea = Native.FindWindowEx(_tray, IntPtr.Zero, "ReBarWindow32", null);
        if (_appArea == IntPtr.Zero)
            _appArea = Native.FindWindowEx(_tray, IntPtr.Zero, "Windows.UI.Composition.DesktopWindowContentBridge", null);

        Log($"located: Shell_TrayWnd={_tray} TrayNotifyWnd={_trayNotify} Start={_start} " +
            $"appArea={_appArea}({ClassName(_appArea)}) explorerPid={_explorerPid}");

        if (_tray != prevTray) HookTaskbarEvents(); // new hwnd after Explorer restart -> re-hook
    }

    private void HookTaskbarEvents()
    {
        Unhook(ref _hLocHook);
        Unhook(ref _hObjHook);
        if (_explorerPid == 0) return;
        _hLocHook = Native.SetWinEventHook(EventObjectLocationChange, EventObjectLocationChange,
            IntPtr.Zero, _winEventProc, _explorerPid, 0, WinEventOutOfContext | WinEventSkipOwnProcess);
        _hObjHook = Native.SetWinEventHook(EventObjectDestroy, EventObjectHide,
            IntPtr.Zero, _winEventProc, _explorerPid, 0, WinEventOutOfContext | WinEventSkipOwnProcess);
        Log($"hooks loc={_hLocHook} obj={_hObjHook} fg={_hFgHook}");
    }

    private void Unhook(ref IntPtr hook)
    {
        if (hook == IntPtr.Zero) return;
        try { Native.UnhookWinEvent(hook); } catch { }
        hook = IntPtr.Zero;
    }

    // ---------- event handling ----------

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
    {
        try
        {
            if (_disposed) return;
            if (eventType == EventSystemForeground)
            {
                EvaluateFullscreen();
                return;
            }
            if (hwnd == _tray || hwnd == _trayNotify || hwnd == _start || hwnd == _appArea)
            {
                _debounce.Stop(); // restart -> coalesce LOCATIONCHANGE spam
                _debounce.Start();
                return;
            }
            // Taskbar destroyed/recreated: the new Shell_TrayWnd hwnd differs from ours.
            if (_tray == IntPtr.Zero || !Native.IsWindow(_tray))
            {
                if (ClassName(hwnd) == "Shell_TrayWnd") { Relocate(); Recompute(); }
            }
            else if ((eventType == EventObjectDestroy || eventType == EventObjectHide) && hwnd == _tray)
            {
                Relocate(); Recompute();
            }
        }
        catch (Exception ex) { Log($"OnWinEvent failed: {ex.Message}"); }
    }

    private void OnSystemEvent(object? sender, EventArgs e) => ScheduleRecompute();

    private void ScheduleRecompute() { _debounce.Stop(); _debounce.Start(); }

    // ---------- geometry ----------

    private void Recompute()
    {
        try
        {
            if (_tray != IntPtr.Zero && !Native.IsWindow(_tray)) Relocate();

            TaskbarGeometry geo;
            if (_tray == IntPtr.Zero)
            {
                geo = new TaskbarGeometry(default, default, default, 1.0, false);
            }
            else
            {
                var taskbarRect = ToRect(_tray);
                var trayRect = _trayNotify != IntPtr.Zero ? ToRect(_trayNotify) : default;
                var appRect = _appArea != IntPtr.Zero ? ToRect(_appArea) : default;
                var dpi = Native.GetDpiForWindow(_tray);
                var mon = MonitorRect(Native.MonitorFromWindow(_tray, Native.MONITOR_DEFAULTTONEAREST));
                var visible = IsTaskbarVisible(taskbarRect, mon);
                geo = new TaskbarGeometry(taskbarRect, trayRect, appRect, dpi / 96.0, visible);
            }
            Emit(geo);
        }
        catch (Exception ex) { Log($"Recompute failed: {ex.Message}"); }
    }

    private static bool IsTaskbarVisible(Rect taskbar, Rect monitor)
    {
        // Auto-hide: taskbar slides off-screen leaving a ~2px sliver.
        var visibleHeight = Math.Min(taskbar.Bottom, monitor.Bottom) - Math.Max(taskbar.Top, monitor.Top);
        var visibleWidth = Math.Min(taskbar.Right, monitor.Right) - Math.Max(taskbar.Left, monitor.Left);
        var shortest = Math.Min(visibleHeight, visibleWidth);
        if (shortest <= 2) return false;
        return taskbar.Width > 0 && taskbar.Height > 0;
    }

    private void Emit(TaskbarGeometry geo)
    {
        if (_hasCurrent && _current.Equals(geo)) return;
        _current = geo;
        _hasCurrent = true;
        Log($"geometry: taskbar={Fmt(geo.TaskbarRect)} tray={Fmt(geo.TrayRect)} apps={Fmt(geo.AppAreaRect)} dpi={geo.Dpi} visible={geo.IsVisible}");
        Changed?.Invoke(geo);
    }

    private static Rect ToRect(IntPtr hwnd)
    {
        Native.GetWindowRect(hwnd, out var r);
        return new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    private static Rect MonitorRect(IntPtr hMon)
    {
        var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        return Native.GetMonitorInfo(hMon, ref mi)
            ? new Rect(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right - mi.rcMonitor.Left, mi.rcMonitor.Bottom - mi.rcMonitor.Top)
            : default;
    }

    // ---------- fullscreen ----------

    private void EvaluateFullscreen()
    {
        bool fs = false;
        var fg = Native.GetForegroundWindow();
        if (fg != IntPtr.Zero && fg != _tray)
        {
            Native.GetWindowThreadProcessId(fg, out var pid);
            var cls = ClassName(fg);
            if (pid != Environment.ProcessId && cls is not ("Progman" or "WorkerW"))
            {
                var r = ToRect(fg);
                var mon = MonitorRect(Native.MonitorFromWindow(_tray == IntPtr.Zero ? fg : _tray, Native.MONITOR_DEFAULTTONEAREST));
                fs = Math.Abs(r.Left - mon.Left) <= 1 && Math.Abs(r.Top - mon.Top) <= 1
                    && Math.Abs(r.Right - mon.Right) <= 1 && Math.Abs(r.Bottom - mon.Bottom) <= 1;
            }
        }
        if (fs != _fullscreen)
        {
            _fullscreen = fs;
            Log($"fullscreen={fs}");
            FullscreenChanged?.Invoke(fs);
        }
    }

    // ---------- misc ----------

    private static string ClassName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "";
        var sb = new StringBuilder(256);
        Native.GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string Fmt(Rect r) => $"{r.X},{r.Y}->{r.Right},{r.Bottom}";
    private static void Log(string msg) => Debug.WriteLine($"[TaskbarService] {msg}");

    public void Dispose()
    {
        _disposed = true;
        SystemEvents.DisplaySettingsChanged -= OnSystemEvent;
        SystemEvents.UserPreferenceChanged -= OnSystemEvent;
        _debounce.Stop();
        _missingRetry.Stop();
        Unhook(ref _hLocHook);
        Unhook(ref _hObjHook);
        Unhook(ref _hFgHook);
    }

    private static class Native
    {
        public const int MONITOR_DEFAULTTONEAREST = 2;
        public const int GWL_EXSTYLE = -20;
        public const int GWL_STYLE = -16;

        public delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint idEventThread, uint dwmsEventTime);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? lpClassName, string? lpWindowName);
        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromWindow(IntPtr hWnd, int dwFlags);
        [DllImport("user32.dll")]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
        [DllImport("user32.dll")]
        public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
        [DllImport("user32.dll")]
        public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public int dwFlags; }
    }
}
