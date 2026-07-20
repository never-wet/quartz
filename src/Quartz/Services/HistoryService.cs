using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using Quartz.Models;

namespace Quartz.Services;

internal sealed class HistoryService
{
    private const int MaximumEntries = 500;
    private const string TestFileEnvironmentVariable = "QUARTZ_HISTORY_FILE";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _filePath;

    private HistoryService(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    public ObservableCollection<HistoryEntry> Entries { get; } = [];

    public static HistoryService CreateDefault()
    {
        var testFilePath = Environment.GetEnvironmentVariable(TestFileEnvironmentVariable);
        var filePath = string.IsNullOrWhiteSpace(testFilePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Quartz",
                "history.json")
            : testFilePath;

        return new HistoryService(filePath);
    }

    public static HistoryService CreatePrivate(string directory) =>
        new(Path.Combine(directory, "history.json"));

    public bool RecordVisit(string? title, string? url, DateTimeOffset visitedAt)
    {
        if (!IsValidWebUrl(url) || visitedAt == default)
        {
            return false;
        }

        if (Entries.Count > 0 &&
            string.Equals(Entries[0].Url, url, StringComparison.Ordinal))
        {
            return false;
        }

        var validUrl = url!;
        var entry = new HistoryEntry
        {
            Title = string.IsNullOrWhiteSpace(title) ? validUrl : title.Trim(),
            Url = validUrl,
            VisitedAt = visitedAt
        };

        Entries.Insert(0, entry);
        HistoryEntry? removedEntry = null;
        if (Entries.Count > MaximumEntries)
        {
            removedEntry = Entries[^1];
            Entries.RemoveAt(Entries.Count - 1);
        }

        try
        {
            Save();
            return true;
        }
        catch
        {
            Entries.Remove(entry);
            if (removedEntry is not null)
            {
                Entries.Add(removedEntry);
            }

            throw;
        }
    }

    public void Clear()
    {
        if (Entries.Count == 0)
        {
            return;
        }

        var previousEntries = Entries.ToList();
        Entries.Clear();
        try
        {
            Save();
        }
        catch
        {
            foreach (var entry in previousEntries)
            {
                Entries.Add(entry);
            }

            throw;
        }
    }

    private void Load()
    {
        Entries.Clear();
        if (!File.Exists(_filePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var storedEntries = JsonSerializer.Deserialize<List<HistoryEntry>>(json, JsonOptions) ?? [];
            string? previousUrl = null;

            foreach (var entry in storedEntries
                         .Where(entry => entry is not null &&
                                         IsValidWebUrl(entry.Url) &&
                                         entry.VisitedAt != default)
                         .OrderByDescending(entry => entry.VisitedAt))
            {
                if (string.Equals(entry.Url, previousUrl, StringComparison.Ordinal))
                {
                    continue;
                }

                Entries.Add(new HistoryEntry
                {
                    Title = string.IsNullOrWhiteSpace(entry.Title) ? entry.Url : entry.Title.Trim(),
                    Url = entry.Url,
                    VisitedAt = entry.VisitedAt
                });
                previousUrl = entry.Url;

                if (Entries.Count == MaximumEntries)
                {
                    break;
                }
            }
        }
        catch (JsonException)
        {
            Entries.Clear();
        }
        catch (IOException)
        {
            Entries.Clear();
        }
        catch (UnauthorizedAccessException)
        {
            Entries.Clear();
        }
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(Entries, JsonOptions);
        File.WriteAllText(_filePath, json);
    }

    private static bool IsValidWebUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
         uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
}
