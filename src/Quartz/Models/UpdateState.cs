namespace Quartz.Models;

internal sealed class UpdateState
{
    public string CurrentVersion { get; set; } = string.Empty;
    public string? LatestVersion { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
    public string Status { get; set; } = "Updates have not been checked.";
    public string? PendingInstallerPath { get; set; }
}
