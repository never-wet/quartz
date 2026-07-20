using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Quartz.Models;

namespace Quartz.Services;

internal sealed class UpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/never-wet/quartz/releases/latest";
    private static readonly HttpClient Client = CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private readonly string _updatesDirectory;
    private readonly string _statePath;

    private UpdateService(string root)
    {
        _updatesDirectory = Path.Combine(root, "Updates");
        _statePath = Path.Combine(_updatesDirectory, "update-state.json");
        State = Load();
        State.CurrentVersion = CurrentVersion.ToString();
    }

    public UpdateState State { get; }

    public static Version CurrentVersion => typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);

    public static UpdateService CreateDefault() => new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quartz"));

    public async Task<bool> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await Client.GetAsync(LatestReleaseUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                State.Status = response.StatusCode == System.Net.HttpStatusCode.Forbidden ? "GitHub temporarily limited update checks." : "Could not check for updates.";
                State.LastCheckedAt = DateTimeOffset.Now;
                Save();
                return false;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            if (root.TryGetProperty("prerelease", out var preRelease) && preRelease.GetBoolean())
            {
                State.Status = "The latest GitHub release is a pre-release and was ignored.";
                State.LastCheckedAt = DateTimeOffset.Now;
                Save();
                return false;
            }

            var tag = root.GetProperty("tag_name").GetString()?.Trim().TrimStart('v', 'V');
            if (!Version.TryParse(tag, out var latest))
            {
                State.Status = "GitHub returned an invalid release version.";
                State.LastCheckedAt = DateTimeOffset.Now;
                Save();
                return false;
            }

            State.LatestVersion = latest.ToString();
            State.LastCheckedAt = DateTimeOffset.Now;
            State.Status = latest > CurrentVersion ? $"Quartz {latest} is available." : "Quartz is up to date.";
            Save();
            return latest > CurrentVersion;
        }
        catch (HttpRequestException)
        {
            State.Status = "No network connection is available for update checks.";
        }
        catch (TaskCanceledException)
        {
            State.Status = "Update check was canceled.";
        }

        State.LastCheckedAt = DateTimeOffset.Now;
        Save();
        return false;
    }

    public async Task DownloadLatestAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        using var releaseResponse = await Client.GetAsync(LatestReleaseUrl, cancellationToken);
        releaseResponse.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await releaseResponse.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString()?.Trim().TrimStart('v', 'V') ?? throw new InvalidDataException("Release is missing a version.");
        var pattern = new Regex($"^Quartz-{Regex.Escape(tag)}-Setup\\.exe$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var assets = root.GetProperty("assets").EnumerateArray().ToArray();
        var installer = assets.FirstOrDefault(asset => pattern.IsMatch(asset.GetProperty("name").GetString() ?? string.Empty));
        if (installer.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidDataException("The latest release does not include a Quartz Windows installer asset.");
        }

        Directory.CreateDirectory(_updatesDirectory);
        var fileName = installer.GetProperty("name").GetString()!;
        var finalPath = Path.Combine(_updatesDirectory, fileName);
        var temporaryPath = finalPath + ".download";
        var downloadUrl = installer.GetProperty("browser_download_url").GetString()!;
        EnsureOfficialReleaseAssetUrl(downloadUrl);
        using (var response = await Client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = File.Create(temporaryPath);
            var total = response.Content.Headers.ContentLength;
            var buffer = new byte[81920];
            long received = 0;
            int count;
            while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                received += count;
                if (total is > 0) progress?.Report(received * 100d / total.Value);
            }
        }

        if (!File.Exists(temporaryPath) || new FileInfo(temporaryPath).Length == 0)
        {
            throw new InvalidDataException("The update installer download was empty.");
        }

        var sums = assets.FirstOrDefault(asset => string.Equals(asset.GetProperty("name").GetString(), "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));
        if (sums.ValueKind != JsonValueKind.Undefined)
        {
            var sumsUrl = sums.GetProperty("browser_download_url").GetString()!;
            EnsureOfficialReleaseAssetUrl(sumsUrl);
            var checksumText = await Client.GetStringAsync(sumsUrl, cancellationToken);
            var expected = Regex.Match(checksumText, $"(?im)^([a-f0-9]{{64}})\\s+[*]?{Regex.Escape(fileName)}\\s*$").Groups[1].Value;
            await using var installerStream = File.OpenRead(temporaryPath);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(installerStream, cancellationToken));
            if (string.IsNullOrWhiteSpace(expected) || !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(temporaryPath);
                throw new InvalidDataException("The downloaded installer did not pass SHA-256 verification.");
            }
        }

        File.Move(temporaryPath, finalPath, true);
        State.PendingInstallerPath = finalPath;
        State.LatestVersion = tag;
        State.Status = $"Quartz {tag} is ready to install after restart.";
        Save();
    }

    public bool HasReadyUpdate => !string.IsNullOrWhiteSpace(State.PendingInstallerPath) && File.Exists(State.PendingInstallerPath);

    public void StartPendingInstaller()
    {
        if (!HasReadyUpdate) throw new InvalidOperationException("No downloaded Quartz update is available.");
        Process.Start(new ProcessStartInfo(State.PendingInstallerPath!, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS") { UseShellExecute = true });
        State.Status = "Installing the update…";
        State.PendingInstallerPath = null;
        Save();
    }

    private UpdateState Load()
    {
        try { return File.Exists(_statePath) ? JsonSerializer.Deserialize<UpdateState>(File.ReadAllText(_statePath), JsonOptions) ?? new UpdateState() : new UpdateState(); }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException) { return new UpdateState(); }
    }

    private void Save()
    {
        Directory.CreateDirectory(_updatesDirectory);
        File.WriteAllText(_statePath, JsonSerializer.Serialize(State, JsonOptions));
    }

    private static void EnsureOfficialReleaseAssetUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.StartsWith("/never-wet/quartz/releases/download/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The release asset URL is not an official Quartz GitHub Releases URL.");
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("QuartzBrowser/2.1 (+https://github.com/never-wet/quartz)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
