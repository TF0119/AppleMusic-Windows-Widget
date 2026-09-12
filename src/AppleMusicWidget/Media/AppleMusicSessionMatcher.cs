using Windows.Media.Control;

namespace AppleMusicWidget.Media;

/// <summary>The only file that knows the Apple Music AUMID prefix.</summary>
public static class AppleMusicSessionMatcher
{
    public static bool IsAppleMusic(GlobalSystemMediaTransportControlsSession s) =>
        s.SourceAppUserModelId.StartsWith("AppleInc.AppleMusic", StringComparison.Ordinal);
}
