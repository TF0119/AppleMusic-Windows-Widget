using System.Diagnostics;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;
using AppleMusicWidget.Models;
using PlaybackState = AppleMusicWidget.Media.PlaybackState;
using WinRtPlaybackStatus = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;

namespace AppleMusicWidget.Media;

/// <summary>
/// Owns the GSMTC session manager and at most one bound Apple Music session.
/// Event-driven only: binds on SessionsChanged, never polls. All WinRT calls are
/// guarded — COMException during session teardown is expected and logged, never
/// thrown out of an event handler.
/// </summary>
public sealed class GsmtcSessionProvider : IMediaSessionProvider
{
    public event Action<TrackInfo>? TrackChanged;
    public event Action<PlaybackSnapshot>? PlaybackChanged;
    public event Action<TimelineSnapshot>? TimelineChanged;
    public event Action<IRandomAccessStreamReference?>? ArtworkChanged;
    public event Action? SessionConnected;
    public event Action? SessionLost;

    // TODO Phase 3: guard with a lock; ConnectAsync and OnSessionsChanged can race
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private string? _lastTrackKey;
    private PlaybackState _lastState = PlaybackState.Unknown;
    private bool _sessionsHooked;
    private bool _wantConnection;

    public bool IsConnected => _session is not null;

    public async Task ConnectAsync()
    {
        _wantConnection = true;
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (!_sessionsHooked)
            {
                _manager.SessionsChanged += OnSessionsChanged;
                _sessionsHooked = true;
            }
            TryBind();
        }
        catch (Exception ex)
        {
            Log($"ConnectAsync failed: {ex.Message}");
        }
    }

    /// <summary>Safe to call twice. Keeps the manager for app lifetime.</summary>
    public void Disconnect()
    {
        _wantConnection = false;
        Unbind();
        if (_sessionsHooked && _manager is not null)
        {
            try { _manager.SessionsChanged -= OnSessionsChanged; } catch (Exception ex) { Log($"unhook SessionsChanged: {ex.Message}"); }
            _sessionsHooked = false;
        }
    }

    public async Task PlayAsync() => await TryControl(s => s.TryPlayAsync());
    public async Task PauseAsync() => await TryControl(s => s.TryPauseAsync());
    public async Task NextAsync() => await TryControl(s => s.TrySkipNextAsync());
    public async Task PreviousAsync() => await TryControl(s => s.TrySkipPreviousAsync());

    public async Task TogglePlayPauseAsync()
    {
        if (_lastState == PlaybackState.Playing) await PauseAsync();
        else await PlayAsync();
    }

    public async Task SeekAsync(TimeSpan position) =>
        await TryControl(s => s.TryChangePlaybackPositionAsync(position.Ticks));

    private async Task TryControl(Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> op)
    {
        var s = _session;
        if (s is null) return;
        try { await op(s); }
        catch (Exception ex) { Log($"control call failed: {ex.Message}"); }
    }

    // ---------- binding ----------

    private void TryBind()
    {
        if (_manager is null || _session is not null) return;
        GlobalSystemMediaTransportControlsSession? s;
        try { s = _manager.GetSessions().FirstOrDefault(AppleMusicSessionMatcher.IsAppleMusic); }
        catch (Exception ex) { Log($"GetSessions failed: {ex.Message}"); return; }
        if (s is null) return; // SessionsChanged re-fires when one appears.

        _session = s;
        _lastTrackKey = null; // force full emit on first bind
        _lastState = PlaybackState.Unknown;
        try
        {
            s.MediaPropertiesChanged += OnMediaPropertiesChanged;
            s.PlaybackInfoChanged += OnPlaybackInfoChanged;
            s.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
        }
        catch (Exception ex) { Log($"subscribe failed: {ex.Message}"); }
        Log($"bound session {s.SourceAppUserModelId}");
        SessionConnected?.Invoke();
        _ = EmitSnapshotAsync(s);
    }

    private void Unbind()
    {
        var s = _session;
        _session = null;
        _lastTrackKey = null;
        _lastState = PlaybackState.Unknown;
        if (s is null) return;
        try { s.MediaPropertiesChanged -= OnMediaPropertiesChanged; } catch { }
        try { s.PlaybackInfoChanged -= OnPlaybackInfoChanged; } catch { }
        try { s.TimelinePropertiesChanged -= OnTimelinePropertiesChanged; } catch { }
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
    {
        try
        {
            var sessions = sender.GetSessions();
            if (_session is not null && !sessions.Contains(_session))
            {
                Log("bound session vanished");
                Unbind();
                SessionLost?.Invoke();
            }
            if (_session is null && _wantConnection)
                TryBind();
        }
        catch (Exception ex) { Log($"SessionsChanged failed: {ex.Message}"); }
    }

    // ---------- snapshots ----------

    private async Task EmitSnapshotAsync(GlobalSystemMediaTransportControlsSession s)
    {
        await EmitMediaPropertiesAsync(s, force: true);
        EmitPlayback(s);
        EmitTimeline(s);
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) =>
        _ = EmitMediaPropertiesAsync(sender, force: false);

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) =>
        EmitPlayback(sender);

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) =>
        EmitTimeline(sender);

    private async Task EmitMediaPropertiesAsync(GlobalSystemMediaTransportControlsSession s, bool force)
    {
        try
        {
            var p = await s.TryGetMediaPropertiesAsync();
            if (!ReferenceEquals(_session, s)) return; // rebound mid-await
            var track = TrackInfo.Create(p.Title ?? "", p.Artist ?? "");
            // MediaPropertiesChanged fires several times per track; dedupe by key.
            if (!force && track.Key == _lastTrackKey) return;
            _lastTrackKey = track.Key;
            TrackChanged?.Invoke(track);
            ArtworkChanged?.Invoke(p.Thumbnail);
        }
        catch (Exception ex) { Log($"media props failed: {ex.Message}"); }
    }

    private void EmitPlayback(GlobalSystemMediaTransportControlsSession s)
    {
        try
        {
            var info = s.GetPlaybackInfo();
            var c = info.Controls;
            var state = info.PlaybackStatus switch
            {
                WinRtPlaybackStatus.Opened => PlaybackState.Opened,
                WinRtPlaybackStatus.Playing => PlaybackState.Playing,
                WinRtPlaybackStatus.Paused => PlaybackState.Paused,
                _ => PlaybackState.Stopped, // Stopped / Closed / Changing
            };
            // Apple Music reports False and does ignore seeks (Phase 2 verified); trust it.
            var canSeek = c.IsPlaybackPositionEnabled;
            _lastState = state;
            PlaybackChanged?.Invoke(new PlaybackSnapshot(
                state, c.IsPlayEnabled, c.IsPauseEnabled,
                c.IsPreviousEnabled, c.IsNextEnabled, canSeek));
        }
        catch (Exception ex) { Log($"playback info failed: {ex.Message}"); }
    }

    private void EmitTimeline(GlobalSystemMediaTransportControlsSession s)
    {
        try
        {
            var t = s.GetTimelineProperties();
            TimelineChanged?.Invoke(new TimelineSnapshot(t.Position, t.EndTime, t.LastUpdatedTime));
        }
        catch (Exception ex) { Log($"timeline failed: {ex.Message}"); }
    }

    private static void Log(string msg) => Debug.WriteLine($"[GsmtcSessionProvider] {msg}");
}
