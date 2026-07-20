using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using Quartz.Models;

namespace Quartz.Services;

internal sealed class DownloadService
{
    private const int MaximumCompletedEntries = 200;
    private const string TestFileEnvironmentVariable = "QUARTZ_DOWNLOADS_FILE";
    private const string TestFolderEnvironmentVariable = "QUARTZ_DOWNLOADS_FOLDER";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _historyFilePath;
    private readonly string _downloadsFolder;
    private readonly Dictionary<int, DownloadItem> _activeDownloads = [];

    private DownloadService(string historyFilePath, string downloadsFolder)
    {
        _historyFilePath = historyFilePath;
        _downloadsFolder = downloadsFolder;
        Load();
    }

    public event EventHandler<Exception>? PersistenceFailed;

    public ObservableCollection<DownloadItem> Downloads { get; } = [];

    public static DownloadService CreateDefault()
    {
        var testFilePath = Environment.GetEnvironmentVariable(TestFileEnvironmentVariable);
        var historyFilePath = string.IsNullOrWhiteSpace(testFilePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Quartz",
                "downloads.json")
            : testFilePath;
        var testDownloadsFolder = Environment.GetEnvironmentVariable(TestFolderEnvironmentVariable);
        var downloadsFolder = string.IsNullOrWhiteSpace(testDownloadsFolder)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads")
            : testDownloadsFolder;

        return new DownloadService(historyFilePath, downloadsFolder);
    }

    public static DownloadService CreatePrivate(string directory)
    {
        Directory.CreateDirectory(directory);
        var downloadsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
        return new DownloadService(Path.Combine(directory, "downloads.json"), downloadsFolder);
    }

    public string CreateSavePath(string? suggestedPath, string? sourceUrl)
    {
        Directory.CreateDirectory(_downloadsFolder);

        var fileName = Path.GetFileName(suggestedPath);
        if (string.IsNullOrWhiteSpace(fileName) &&
            Uri.TryCreate(sourceUrl, UriKind.Absolute, out var sourceUri))
        {
            fileName = Uri.UnescapeDataString(Path.GetFileName(sourceUri.AbsolutePath));
        }

        fileName = SanitizeFileName(fileName);
        var extension = Path.GetExtension(fileName);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var candidate = Path.Combine(_downloadsFolder, fileName);
        var suffix = 1;

        while (File.Exists(candidate) ||
               Downloads.Any(item => string.Equals(item.SavePath, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = Path.Combine(_downloadsFolder, $"{baseName} ({suffix}){extension}");
            suffix++;
        }

        return candidate;
    }

    public DownloadItem Track(
        int engineId,
        string sourceUrl,
        string savePath,
        long bytesReceived,
        long totalBytes,
        Action cancel)
    {
        if (_activeDownloads.TryGetValue(engineId, out var existing))
        {
            return existing;
        }

        var item = DownloadItem.CreateActive(
            engineId,
            sourceUrl,
            savePath,
            bytesReceived,
            totalBytes,
            cancel);
        Downloads.Insert(0, item);
        _activeDownloads[engineId] = item;
        return item;
    }

    public void Update(
        int engineId,
        long bytesReceived,
        long totalBytes,
        bool isComplete,
        bool isCanceled,
        bool isInProgress)
    {
        if (!_activeDownloads.TryGetValue(engineId, out var item))
        {
            return;
        }

        item.UpdateProgress(bytesReceived, totalBytes > 0 ? (ulong)totalBytes : null);
        if (isInProgress)
        {
            return;
        }

        _activeDownloads.Remove(engineId);
        if (isComplete)
        {
            item.MarkCompleted(DateTimeOffset.Now);
            SaveCompletedHistory();
        }
        else
        {
            item.MarkInterrupted(isCanceled, isCanceled ? null : "Chromium interrupted the transfer");
        }
    }

    public void Cancel(DownloadItem item)
    {
        if (item.IsActive)
        {
            item.Cancel();
        }
    }

    public void ClearCompleted()
    {
        for (var index = Downloads.Count - 1; index >= 0; index--)
        {
            if (Downloads[index].IsCompleted)
            {
                Downloads.RemoveAt(index);
            }
        }

        SaveCompletedHistory();
    }

    private void Load()
    {
        Downloads.Clear();
        if (!File.Exists(_historyFilePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_historyFilePath);
            var records = JsonSerializer.Deserialize<List<DownloadRecord>>(json, JsonOptions) ?? [];

            foreach (var record in records
                         .Where(IsValidRecord)
                         .OrderByDescending(record => record.CompletedAt)
                         .Take(MaximumCompletedEntries))
            {
                Downloads.Add(DownloadItem.FromRecord(record));
            }
        }
        catch (JsonException)
        {
            Downloads.Clear();
        }
        catch (IOException)
        {
            Downloads.Clear();
        }
        catch (UnauthorizedAccessException)
        {
            Downloads.Clear();
        }
    }

    private void SaveCompletedHistory()
    {
        try
        {
            var directory = Path.GetDirectoryName(_historyFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var records = Downloads
                .Where(item => item.IsCompleted)
                .OrderByDescending(item => item.CompletedAt)
                .Take(MaximumCompletedEntries)
                .Select(item => item.ToRecord())
                .ToList();
            var json = JsonSerializer.Serialize(records, JsonOptions);
            File.WriteAllText(_historyFilePath, json);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            PersistenceFailed?.Invoke(this, exception);
        }
    }

    private static bool IsValidRecord(DownloadRecord? record) =>
        record is not null &&
        !string.IsNullOrWhiteSpace(record.SavePath) &&
        Path.IsPathFullyQualified(record.SavePath) &&
        record.CompletedAt != default;

    private static string SanitizeFileName(string? value)
    {
        var fileName = string.IsNullOrWhiteSpace(value) ? "download" : value.Trim();
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalidCharacter, '_');
        }

        fileName = fileName.TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "download";
        }

        if (fileName.Length > 150)
        {
            fileName = fileName[..150].TrimEnd('.', ' ');
        }

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        string[] reservedNames =
        [
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        ];
        if (reservedNames.Contains(baseName, StringComparer.OrdinalIgnoreCase))
        {
            fileName = $"_{fileName}";
        }

        return fileName;
    }
}
