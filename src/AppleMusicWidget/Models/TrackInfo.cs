namespace AppleMusicWidget.Models;

/// <summary>
/// One track's display info. Subtitle is the raw GSMTC Artist string, which
/// Phase 0 verified already arrives as "artist — album" (AlbumTitle is always
/// empty), so it is used as-is. Key dedupes artwork re-decoding.
/// </summary>
public sealed record TrackInfo(string Title, string Subtitle, string Key)
{
    public static TrackInfo Empty { get; } = new("", "", "|");

    public static TrackInfo Create(string title, string subtitle) =>
        new(title, subtitle, $"{title}|{subtitle}");
}
