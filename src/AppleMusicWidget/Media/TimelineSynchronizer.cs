using System.Diagnostics;

namespace AppleMusicWidget.Media;

/// <summary>
/// PLAN §5.4: local interpolation of the playback position between GSMTC
/// timeline updates. The 500ms tick runs only while playing (PLAN §4.4/§16).
/// Must be created on a thread with a Dispatcher.
/// </summary>
public sealed class TimelineSynchronizer
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(500);

    private readonly System.Windows.Threading.DispatcherTimer _timer;
    private TimeSpan _basePosition;
    private long _baseTimestamp;
    private bool _isPlaying;

    public event Action? Tick;

    public TimelineSynchronizer()
    {
        _timer = new System.Windows.Threading.DispatcherTimer { Interval = TickInterval };
        _timer.Tick += (_, _) => Tick?.Invoke();
    }

    public TimeSpan End { get; private set; }

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying == value) return;
            // Re-base so interpolation stays continuous across the toggle.
            var pos = CurrentPosition;
            _basePosition = pos;
            _baseTimestamp = Stopwatch.GetTimestamp();
            _isPlaying = value;
            if (value) _timer.Start(); else _timer.Stop();
        }
    }

    public TimeSpan CurrentPosition
    {
        get
        {
            var pos = _isPlaying
                ? _basePosition + Stopwatch.GetElapsedTime(_baseTimestamp)
                : _basePosition;
            if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
            if (End > TimeSpan.Zero && pos > End) pos = End;
            return pos;
        }
    }

    public void Sync(TimelineSnapshot s, bool isPlaying)
    {
        _basePosition = s.Position;
        _baseTimestamp = Stopwatch.GetTimestamp();
        End = s.End;
        IsPlaying = isPlaying;
    }

    /// <summary>Stops the tick and clears the timeline (release path).</summary>
    public void Stop()
    {
        _isPlaying = false;
        _timer.Stop();
        _basePosition = TimeSpan.Zero;
        End = TimeSpan.Zero;
    }
}
