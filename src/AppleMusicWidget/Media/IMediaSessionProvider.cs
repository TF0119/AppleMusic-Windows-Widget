using Windows.Storage.Streams;
using AppleMusicWidget.Models;

namespace AppleMusicWidget.Media;

public interface IMediaSessionProvider
{
    event Action<TrackInfo> TrackChanged;
    event Action<PlaybackSnapshot> PlaybackChanged;
    event Action<TimelineSnapshot> TimelineChanged;
    /// <summary>Raw GSMTC thumbnail reference; null when the track has none.</summary>
    event Action<IRandomAccessStreamReference?> ArtworkChanged;
    event Action SessionConnected;
    event Action SessionLost;

    Task ConnectAsync();
    void Disconnect();
    bool IsConnected { get; }

    Task PlayAsync();
    Task PauseAsync();
    Task TogglePlayPauseAsync();
    Task NextAsync();
    Task PreviousAsync();
    Task SeekAsync(TimeSpan position);
}
