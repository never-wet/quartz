namespace Quartz.Models;

internal sealed class HistoryEntry
{
    public string Title { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public DateTimeOffset VisitedAt { get; init; }
}
