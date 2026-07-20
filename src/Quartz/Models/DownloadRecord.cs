namespace Quartz.Models;

internal sealed class DownloadRecord
{
    public string FileName { get; init; } = string.Empty;

    public string SourceUrl { get; init; } = string.Empty;

    public string SavePath { get; init; } = string.Empty;

    public long BytesReceived { get; init; }

    public DateTimeOffset CompletedAt { get; init; }
}
