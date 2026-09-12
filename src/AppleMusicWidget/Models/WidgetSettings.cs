namespace AppleMusicWidget.Models;

public sealed class WidgetSettings
{
    public bool LaunchAtStartup { get; set; } = true;
    public bool HideWhenAppleMusicClosed { get; set; } = true;
    public bool ShowWhenAppleMusicLaunches { get; set; } = true;
    /// <summary>Gap in physical px between the strip's right edge and the tray's left edge.</summary>
    public int TaskbarGapPx { get; set; } = 8;
}
