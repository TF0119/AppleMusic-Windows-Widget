namespace AppleMusicWidget.Media;

public enum PlaybackState
{
    Unknown,
    Opened,
    Playing,
    Paused,
    Stopped,
}

public sealed record PlaybackSnapshot(
    PlaybackState State,
    bool CanPlay,
    bool CanPause,
    bool CanPrevious,
    bool CanNext,
    bool CanSeek);

public sealed record TimelineSnapshot(TimeSpan Position, TimeSpan End, DateTimeOffset UpdatedAt);
