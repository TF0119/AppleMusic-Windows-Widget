using System.ComponentModel;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AppleMusicWidget.Media;
using AppleMusicWidget.Models;
using AppleMusicWidget.Services;
using PlaybackState = AppleMusicWidget.Media.PlaybackState;

namespace AppleMusicWidget.UI;

/// <summary>
/// Single glue object between IMediaSessionProvider (thread-pool events) and the
/// widget UI. Every UI mutation is marshalled through the Dispatcher.
/// </summary>
public sealed class PlayerViewModel : INotifyPropertyChanged
{
    private const int ArtworkDecodeWidth = 128; // 2x the 64px display for HiDPI

    private readonly IMediaSessionProvider _provider;
    private readonly Dispatcher _dispatcher;
    private readonly TimelineSynchronizer _sync = new();

    private TrackInfo _track = TrackInfo.Empty;
    private PlaybackState _state = PlaybackState.Unknown;
    private BitmapSource? _artwork;
    private TimeSpan _position;
    private TimeSpan _duration;
    private bool _canPrevious;
    private bool _canNext;
    private bool _canSeek;
    private int _artworkSeq;

    public event PropertyChangedEventHandler? PropertyChanged;

    public PlayerViewModel(IMediaSessionProvider provider, Dispatcher dispatcher)
    {
        _provider = provider;
        _dispatcher = dispatcher;

        PlayPauseCommand = new RelayCommand(() => _provider.TogglePlayPauseAsync());
        PreviousCommand = new RelayCommand(() => _provider.PreviousAsync(), () => CanPrevious);
        NextCommand = new RelayCommand(() => _provider.NextAsync(), () => CanNext);
        ActionMenuCommand = new RelayCommand(AppleMusicUiAutomation.ShowActionMenuAsync);

        _provider.TrackChanged += t => _dispatcher.InvokeAsync(() => OnTrackChanged(t));
        // Marshal first: LoadArtworkAsync captures _artworkSeq/_track.Key, which
        // must happen on the Dispatcher after OnTrackChanged (InvokeAsync order).
        _provider.ArtworkChanged += thumb => _dispatcher.InvokeAsync(() => _ = LoadArtworkAsync(thumb));
        _provider.PlaybackChanged += s => _dispatcher.InvokeAsync(() => OnPlaybackChanged(s));
        _provider.TimelineChanged += s => _dispatcher.InvokeAsync(() => OnTimelineChanged(s));
        _sync.Tick += UpdatePosition;
    }

    // ---------- bound properties ----------

    public string Title => IsIdle ? "Apple Music" : _track.Title;
    public string Subtitle => IsIdle ? "再生する曲を選択してください" : _track.Subtitle;
    public bool IsIdle => _state == PlaybackState.Opened || string.IsNullOrEmpty(_track.Title);

    public BitmapSource? Artwork
    {
        get => _artwork;
        private set { if (!ReferenceEquals(_artwork, value)) { _artwork = value; Raise(nameof(Artwork)); } }
    }

    public TimeSpan Position => _position;
    public TimeSpan Duration => _duration;
    public string ElapsedText => Format(_position);
    public string RemainingText => "-" + Format(_duration - _position);
    public double ProgressFraction =>
        _duration > TimeSpan.Zero ? Math.Clamp(_position / _duration, 0.0, 1.0) : 0.0;

    public bool IsPlaying => _state == PlaybackState.Playing;
    public string PlayPauseGlyph => IsPlaying ? "❚❚" : "▶";

    public bool CanPrevious { get => _canPrevious; private set { if (_canPrevious != value) { _canPrevious = value; Raise(nameof(CanPrevious)); PreviousCommand.RaiseCanExecuteChanged(); } } }
    public bool CanNext { get => _canNext; private set { if (_canNext != value) { _canNext = value; Raise(nameof(CanNext)); NextCommand.RaiseCanExecuteChanged(); } } }
    public bool CanSeek { get => _canSeek; private set { if (_canSeek != value) { _canSeek = value; Raise(nameof(CanSeek)); } } }

    public RelayCommand PlayPauseCommand { get; }
    public RelayCommand PreviousCommand { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand ActionMenuCommand { get; }

    public void SeekTo(double fraction)
    {
        if (!CanSeek || _duration <= TimeSpan.Zero) return;
        var target = TimeSpan.FromTicks((long)(_duration.Ticks * Math.Clamp(fraction, 0.0, 1.0)));
        // Optimistic local re-sync so the UI jumps immediately.
        _sync.Sync(new TimelineSnapshot(target, _duration, DateTimeOffset.Now), IsPlaying);
        UpdatePosition();
        _ = _provider.SeekAsync(target);
    }

    /// <summary>Release path: stop the timer, drop artwork, back to idle-empty.</summary>
    public void Reset()
    {
        _sync.Stop();
        _artworkSeq++; // invalidate any in-flight artwork load
        _track = TrackInfo.Empty;
        _state = PlaybackState.Unknown;
        _position = TimeSpan.Zero;
        _duration = TimeSpan.Zero;
        Artwork = null;
        CanPrevious = CanNext = CanSeek = false;
        Raise(nameof(Title)); Raise(nameof(Subtitle)); Raise(nameof(IsIdle));
        Raise(nameof(IsPlaying)); Raise(nameof(PlayPauseGlyph));
        Raise(nameof(Position)); Raise(nameof(Duration));
        Raise(nameof(ElapsedText)); Raise(nameof(RemainingText)); Raise(nameof(ProgressFraction));
    }

    // ---------- provider event handlers (already on the Dispatcher) ----------

    private void OnTrackChanged(TrackInfo t)
    {
        _track = t;
        _artworkSeq++;
        Artwork = null; // PLAN §15: never show the previous track's art
        Raise(nameof(Title)); Raise(nameof(Subtitle)); Raise(nameof(IsIdle));
    }

    private void OnPlaybackChanged(PlaybackSnapshot s)
    {
        _state = s.State;
        _sync.IsPlaying = s.State == PlaybackState.Playing;
        CanPrevious = s.CanPrevious;
        CanNext = s.CanNext;
        CanSeek = s.CanSeek;
        Raise(nameof(IsPlaying)); Raise(nameof(PlayPauseGlyph)); Raise(nameof(IsIdle));
        Raise(nameof(Title)); Raise(nameof(Subtitle));
        UpdatePosition();
    }

    private void OnTimelineChanged(TimelineSnapshot s)
    {
        _duration = s.End;
        _sync.Sync(s, IsPlaying);
        Raise(nameof(Duration));
        UpdatePosition();
    }

    private void UpdatePosition()
    {
        _position = _sync.CurrentPosition;
        Raise(nameof(Position));
        Raise(nameof(ElapsedText));
        Raise(nameof(RemainingText));
        Raise(nameof(ProgressFraction));
    }

    private async Task LoadArtworkAsync(Windows.Storage.Streams.IRandomAccessStreamReference? thumb)
    {
        var seq = ++_artworkSeq;
        var key = _track.Key;
        var bmp = await AppleMusicWidget.Artwork.ArtworkProvider.LoadAsync(thumb, ArtworkDecodeWidth);
        await _dispatcher.InvokeAsync(() =>
        {
            if (seq != _artworkSeq || _track.Key != key) return; // fast-skip guard
            Artwork = bmp;
        });
    }

    private static string Format(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
            : $"{(int)t.TotalMinutes}:{t.Seconds:D2}";
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
