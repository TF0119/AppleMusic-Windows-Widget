using System.Diagnostics;
using System.Management;
using System.Text;
using Windows.Media.Control;
using Windows.Storage.Streams;

// Phase 0 verification app (PLAN.md §20).
// Verifies:
//  1. Low-cost Apple Music start detection   (WMI creation event + 2s polling fallback)
//  2. Low-cost Apple Music exit detection    (WMI deletion event + Process.Exited/WaitForExitAsync)
//  3. GSMTC session acquisition after start  (SourceAppUserModelId prefix match)
//  4. Title / artist / artwork / playback / timeline retrieval + event-driven updates
//  5. Full release of session/artwork/timers on Apple Music exit
//  6. ~0% CPU while Apple Music is not running (self metrics every 10s)

const string ProcessName = "AppleMusic"; // GetProcessesByName excludes ".exe"
const string AumidPrefix = "AppleInc.AppleMusic";

string state = "WaitingForAppleMusic";
Process? amProcess = null;
GlobalSystemMediaTransportControlsSessionManager? mgr = null;
GlobalSystemMediaTransportControlsSession? session = null;
int wmiEvents = 0;
TimeSpan lastCpu = TimeSpan.Zero;
DateTime lastCpuStamp = DateTime.UtcNow;

Console.OutputEncoding = Encoding.UTF8;
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
Log("probe start");

lastCpu = Process.GetCurrentProcess().TotalProcessorTime;
TryStartWmiWatchers();
_ = MetricsLoop();

try
{
    mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
    mgr.SessionsChanged += OnSessionsChanged;
    Log($"[GSMTC] manager acquired; sessions: {ListSessions()}");
}
catch (Exception ex)
{
    Log($"[GSMTC] RequestAsync failed: {ex.Message}");
}

while (!cts.IsCancellationRequested)
{
    try
    {
        var proc = FindAppleMusic();
        if (proc is null)
        {
            if (state != "WaitingForAppleMusic")
            {
                ReleaseAll("Apple Music process gone");
                SetState("WaitingForAppleMusic");
            }
        }
        else
        {
            if (state == "WaitingForAppleMusic")
            {
                amProcess = proc;
                Log($"[proc] AppleMusic.exe detected pid={proc.Id}");
                SetState("AppleMusicStarting");
                AttachExitDetection(proc);
            }
            if (session is null && mgr is not null)
                TryConnectSession();
        }
    }
    catch (Exception ex)
    {
        Log($"[loop] error: {ex.Message}");
    }

    try { await Task.Delay(TimeSpan.FromSeconds(2), cts.Token); }
    catch (OperationCanceledException) { }
}

Log("probe exit");

// ---------- helpers ----------

static void Log(string msg) => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {msg}");
void SetState(string s) { state = s; Log($"[state] -> {s}"); }

Process? FindAppleMusic()
{
    // Cheap existence check; exact name match excludes AMPLibraryAgent.exe etc.
    var procs = Process.GetProcessesByName(ProcessName);
    return procs.Length == 0 ? null : procs[0];
}

void AttachExitDetection(Process p)
{
    try
    {
        p.EnableRaisingEvents = true;
        p.Exited += (_, _) => Log("[proc] Exited event fired");
        _ = Task.Run(async () =>
        {
            try { await p.WaitForExitAsync(); Log("[proc] WaitForExitAsync completed"); }
            catch (Exception ex) { Log($"[proc] WaitForExitAsync error: {ex.Message}"); }
        });
    }
    catch (Exception ex) { Log($"[proc] exit-detection attach failed: {ex.Message}"); }
}

void TryStartWmiWatchers()
{
    try
    {
        var created = new ManagementEventWatcher(new WqlEventQuery(
            "__InstanceCreationEvent", TimeSpan.FromSeconds(1),
            $"TargetInstance isa 'Win32_Process' AND TargetInstance.Name='{ProcessName}.exe'"));
        created.EventArrived += (_, _) => { wmiEvents++; Log("[WMI] __InstanceCreationEvent AppleMusic.exe"); };
        created.Start();

        var deleted = new ManagementEventWatcher(new WqlEventQuery(
            "__InstanceDeletionEvent", TimeSpan.FromSeconds(1),
            $"TargetInstance isa 'Win32_Process' AND TargetInstance.Name='{ProcessName}.exe'"));
        deleted.EventArrived += (_, _) => { wmiEvents++; Log("[WMI] __InstanceDeletionEvent AppleMusic.exe"); };
        deleted.Start();
        Log("[WMI] watchers started (non-elevated)");
    }
    catch (Exception ex) { Log($"[WMI] watcher start failed: {ex.Message}"); }
}

string ListSessions()
{
    if (mgr is null) return "(no manager)";
    var list = mgr.GetSessions();
    return list.Count == 0
        ? "(none)"
        : string.Join(" | ", list.Select(s => s.SourceAppUserModelId));
}

void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
{
    Log($"[GSMTC] SessionsChanged -> {ListSessions()}");
    if (session is not null && !sender.GetSessions().Contains(session))
    {
        Log("[GSMTC] bound session disappeared; will reconnect");
        DetachSession();
        if (state is "Playing" or "Paused" or "AppleMusicIdle")
            SetState("AppleMusicStarting");
    }
}

void TryConnectSession()
{
    var s = mgr!.GetSessions().FirstOrDefault(x => x.SourceAppUserModelId.StartsWith(AumidPrefix, StringComparison.Ordinal));
    if (s is null)
    {
        Log("[GSMTC] no Apple Music session yet (retrying on next poll)");
        return;
    }

    session = s;
    s.MediaPropertiesChanged += OnMediaProps;
    s.PlaybackInfoChanged += OnPlaybackInfo;
    s.TimelinePropertiesChanged += OnTimeline;
    Log($"[GSMTC] connected: {s.SourceAppUserModelId}");
    DumpMediaProps(s);
    DumpPlaybackInfo(s);
    DumpTimeline(s);
    SetState("AppleMusicIdle");
}

void DetachSession()
{
    if (session is null) return;
    session.MediaPropertiesChanged -= OnMediaProps;
    session.PlaybackInfoChanged -= OnPlaybackInfo;
    session.TimelinePropertiesChanged -= OnTimeline;
    session = null;
}

void ReleaseAll(string reason)
{
    Log($"[release] {reason}; releasing session/process refs");
    long before = Process.GetCurrentProcess().WorkingSet64;
    DetachSession();
    amProcess = null;
    GC.Collect();
    GC.WaitForPendingFinalizers();
    long after = Process.GetCurrentProcess().WorkingSet64;
    Log($"[release] working set {before / 1048576.0:F1}MB -> {after / 1048576.0:F1}MB");
}

async void OnMediaProps(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
{
    try { DumpMediaProps(sender); }
    catch (Exception ex) { Log($"[GSMTC] MediaPropertiesChanged error: {ex.Message}"); }
    await Task.CompletedTask;
}

void OnPlaybackInfo(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
{
    try
    {
        var st = sender.GetPlaybackInfo().PlaybackStatus;
        DumpPlaybackInfo(sender);
        if (st == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) SetState("Playing");
        else if (st == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused) SetState("Paused");
    }
    catch (Exception ex) { Log($"[GSMTC] PlaybackInfoChanged error: {ex.Message}"); }
}

void OnTimeline(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
{
    try { DumpTimeline(sender); }
    catch (Exception ex) { Log($"[GSMTC] TimelinePropertiesChanged error: {ex.Message}"); }
}

async void DumpMediaProps(GlobalSystemMediaTransportControlsSession s)
{
    try
    {
        var p = await s.TryGetMediaPropertiesAsync();
        Log($"[track] title='{p.Title}' artist='{p.Artist}' albumArtist='{p.AlbumArtist}' album='{p.AlbumTitle}' " +
            $"track#={p.TrackNumber}/{p.AlbumTrackCount} type={p.PlaybackType} genres='{string.Join(",", p.Genres)}'");
        if (p.Thumbnail is not null)
        {
            using IRandomAccessStreamWithContentType stream = await p.Thumbnail.OpenReadAsync();
            Log($"[track] thumbnail {stream.Size} bytes ({stream.ContentType})");
        }
        else Log("[track] no thumbnail");
    }
    catch (Exception ex) { Log($"[track] props error: {ex.Message}"); }
}

void DumpPlaybackInfo(GlobalSystemMediaTransportControlsSession s)
{
    var i = s.GetPlaybackInfo();
    var c = i.Controls;
    Log($"[play ] status={i.PlaybackStatus} play={c.IsPlayEnabled} pause={c.IsPauseEnabled} " +
        $"prev={c.IsPreviousEnabled} next={c.IsNextEnabled} shuffle={c.IsShuffleEnabled} repeat={c.IsRepeatEnabled} " +
        $"posChange={c.IsPlaybackPositionEnabled} rate={c.IsPlaybackRateEnabled}");
}

void DumpTimeline(GlobalSystemMediaTransportControlsSession s)
{
    var t = s.GetTimelineProperties();
    Log($"[time ] pos={t.Position} end={t.EndTime} seek=[{t.MinSeekTime}..{t.MaxSeekTime}] updated={t.LastUpdatedTime.LocalDateTime:HH:mm:ss}");
}

async Task MetricsLoop()
{
    while (!cts.IsCancellationRequested)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(10), cts.Token); }
        catch (OperationCanceledException) { break; }
        var p = Process.GetCurrentProcess();
        var now = DateTime.UtcNow;
        var cpu = p.TotalProcessorTime;
        var elapsed = (now - lastCpuStamp).TotalMilliseconds;
        var pct = elapsed > 0 ? (cpu - lastCpu).TotalMilliseconds / (elapsed * Environment.ProcessorCount) * 100 : 0;
        Log($"[metric] state={state} cpu={pct:F2}% ws={p.WorkingSet64 / 1048576.0:F1}MB gc={GC.GetTotalMemory(false) / 1048576.0:F1}MB wmiEvents={wmiEvents}");
        lastCpu = cpu; lastCpuStamp = now;
    }
}
