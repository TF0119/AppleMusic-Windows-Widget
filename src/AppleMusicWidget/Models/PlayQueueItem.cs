namespace AppleMusicWidget.Models;

public sealed record PlayQueueItem(string Title, string Subtitle, string Duration, bool IsHeader = false);

public sealed record PlayQueueSnapshot(
    IReadOnlyList<PlayQueueItem> History,
    IReadOnlyList<PlayQueueItem> Upcoming);
