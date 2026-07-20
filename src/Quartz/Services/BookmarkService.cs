using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using Quartz.Models;

namespace Quartz.Services;

internal sealed class BookmarkService
{
    private const string TestFileEnvironmentVariable = "QUARTZ_BOOKMARKS_FILE";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _filePath;

    private BookmarkService(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    public ObservableCollection<Bookmark> Bookmarks { get; } = [];

    public static BookmarkService CreateDefault()
    {
        var testFilePath = Environment.GetEnvironmentVariable(TestFileEnvironmentVariable);
        var filePath = string.IsNullOrWhiteSpace(testFilePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Quartz",
                "bookmarks.json")
            : testFilePath;

        return new BookmarkService(filePath);
    }

    public bool IsBookmarked(string url) =>
        Bookmarks.Any(bookmark => string.Equals(bookmark.Url, url, StringComparison.Ordinal));

    public bool Add(string title, string url)
    {
        if (IsBookmarked(url))
        {
            return false;
        }

        var bookmark = new Bookmark
        {
            Title = string.IsNullOrWhiteSpace(title) ? url : title.Trim(),
            Url = url
        };

        Bookmarks.Add(bookmark);
        try
        {
            Save();
            return true;
        }
        catch
        {
            Bookmarks.Remove(bookmark);
            throw;
        }
    }

    public bool Remove(string url)
    {
        var bookmark = Bookmarks.FirstOrDefault(
            item => string.Equals(item.Url, url, StringComparison.Ordinal));
        if (bookmark is null)
        {
            return false;
        }

        var index = Bookmarks.IndexOf(bookmark);
        Bookmarks.RemoveAt(index);
        try
        {
            Save();
            return true;
        }
        catch
        {
            Bookmarks.Insert(index, bookmark);
            throw;
        }
    }

    public void Clear()
    {
        if (Bookmarks.Count == 0)
        {
            return;
        }

        var previousBookmarks = Bookmarks.ToList();
        Bookmarks.Clear();
        try
        {
            Save();
        }
        catch
        {
            foreach (var bookmark in previousBookmarks)
            {
                Bookmarks.Add(bookmark);
            }

            throw;
        }
    }

    private void Load()
    {
        Bookmarks.Clear();
        if (!File.Exists(_filePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var storedBookmarks = JsonSerializer.Deserialize<List<Bookmark>>(json, JsonOptions) ?? [];
            var seenUrls = new HashSet<string>(StringComparer.Ordinal);

            foreach (var bookmark in storedBookmarks)
            {
                if (bookmark is null ||
                    !IsValidWebUrl(bookmark.Url) ||
                    !seenUrls.Add(bookmark.Url))
                {
                    continue;
                }

                Bookmarks.Add(new Bookmark
                {
                    Title = string.IsNullOrWhiteSpace(bookmark.Title) ? bookmark.Url : bookmark.Title.Trim(),
                    Url = bookmark.Url
                });
            }
        }
        catch (JsonException)
        {
            Bookmarks.Clear();
        }
        catch (IOException)
        {
            Bookmarks.Clear();
        }
        catch (UnauthorizedAccessException)
        {
            Bookmarks.Clear();
        }
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(Bookmarks, JsonOptions);
        File.WriteAllText(_filePath, json);
    }

    private static bool IsValidWebUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
         uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
}
