using System.Diagnostics;
using System.Management;

namespace AppleMusicWidget.Lifecycle;

/// <summary>
/// Detects AppleMusic.exe start/exit with minimal cost.
/// Primary: WMI __InstanceCreationEvent/__InstanceDeletionEvent (verified non-elevated, see Phase 0).
/// Fallback: 2s polling reconcile (also covers missed WMI events).
/// Exit: Process.WaitForExitAsync + Exited event for zero-cost immediate notification.
/// </summary>
public sealed class AppleMusicProcessMonitor : IDisposable
{
    private const string ProcessName = "AppleMusic"; // excludes AMPLibraryAgent.exe
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    public event Action<Process>? Started;
    public event Action? Exited;

    private readonly object _gate = new();
    private ManagementEventWatcher? _createdWatcher;
    private ManagementEventWatcher? _deletedWatcher;
    private System.Threading.Timer? _pollTimer;
    private Process? _current;
    private bool _disposed;

    public bool IsRunning { get { lock (_gate) return _current is not null; } }

    public void Start()
    {
        TryStartWmi();
        CheckNow();
        _pollTimer = new System.Threading.Timer(_ => CheckNow(), null, PollInterval, PollInterval);
    }

    public void CheckNow()
    {
        if (_disposed) return;
        Process[] candidates;
        try { candidates = Process.GetProcessesByName(ProcessName); }
        catch { return; }

        Process? started = null;
        Process? replaced = null;
        Process? duplicate = null;
        bool exited = false;
        lock (_gate)
        {
            var found = candidates.FirstOrDefault();
            // Dispose every Process instance we do not keep; each holds a handle.
            foreach (var p in candidates)
                if (!ReferenceEquals(p, found)) p.Dispose();

            if (found is not null)
            {
                if (_current is null || _current.Id != found.Id)
                {
                    // First sighting or restart with a new PID.
                    if (_current is not null) { replaced = _current; exited = true; }
                    _current = found;
                    started = found;
                }
                else
                {
                    // Same PID already tracked; this array element is a duplicate.
                    duplicate = found;
                }
            }
            else if (_current is not null)
            {
                replaced = _current;
                _current = null;
                exited = true;
            }
        }

        duplicate?.Dispose();

        if (started is not null)
        {
            HookExit(started);
            Started?.Invoke(started);
        }
        if (exited) Exited?.Invoke();
        replaced?.Dispose();
    }

    private void HookExit(Process p)
    {
        try
        {
            p.EnableRaisingEvents = true;
            p.Exited += (_, _) => CheckNow();
            _ = Task.Run(async () =>
            {
                try { await p.WaitForExitAsync(); CheckNow(); }
                catch { /* process object disposed mid-flight; poll reconciles */ }
            });
        }
        catch { /* non-critical: polling covers exit detection */ }
    }

    private void TryStartWmi()
    {
        try
        {
            _createdWatcher = new ManagementEventWatcher(new WqlEventQuery(
                "__InstanceCreationEvent", TimeSpan.FromSeconds(1),
                $"TargetInstance isa 'Win32_Process' AND TargetInstance.Name='{ProcessName}.exe'"));
            _createdWatcher.EventArrived += (_, _) => CheckNow();
            _createdWatcher.Start();

            _deletedWatcher = new ManagementEventWatcher(new WqlEventQuery(
                "__InstanceDeletionEvent", TimeSpan.FromSeconds(1),
                $"TargetInstance isa 'Win32_Process' AND TargetInstance.Name='{ProcessName}.exe'"));
            _deletedWatcher.EventArrived += (_, _) => CheckNow();
            _deletedWatcher.Start();
        }
        catch
        {
            // WMI unavailable -> 2s polling alone is sufficient (verified ~0% CPU in Phase 0).
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _pollTimer?.Dispose();
        foreach (var w in new[] { _createdWatcher, _deletedWatcher })
        {
            try { w?.Stop(); w?.Dispose(); } catch { }
        }
        _current?.Dispose();
    }
}
