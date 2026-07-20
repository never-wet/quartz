using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using Quartz.Models;

namespace Quartz.Services;

internal sealed class ExtensionService
{
    private const string TestFileEnvironmentVariable = "QUARTZ_EXTENSIONS_FILE";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _filePath;

    private ExtensionService(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    public ObservableCollection<BrowserExtension> Extensions { get; } = [];

    public static ExtensionService CreateDefault()
    {
        var overridePath = Environment.GetEnvironmentVariable(TestFileEnvironmentVariable);
        return new(string.IsNullOrWhiteSpace(overridePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Quartz",
                "extensions.json")
            : Path.GetFullPath(overridePath));
    }

    public IReadOnlyList<string> GetEnabledFolders() =>
        Extensions
            .Where(extension => extension.IsEnabled && IsValidExtensionFolder(extension.FolderPath))
            .Select(extension => extension.FolderPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public BrowserExtension AddUnpacked(string folderPath)
    {
        var fullPath = Path.GetFullPath(folderPath);
        var manifestPath = Path.Combine(fullPath, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new ArgumentException("The selected folder does not contain manifest.json.");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;
        var name = GetRequiredString(root, "name");
        var version = GetRequiredString(root, "version");
        var manifestVersion = root.TryGetProperty("manifest_version", out var manifestVersionElement) &&
                              manifestVersionElement.TryGetInt32(out var parsedManifestVersion)
            ? parsedManifestVersion
            : 0;
        if (manifestVersion != 3)
        {
            throw new ArgumentException("Quartz 2.0 requires a Manifest V3 unpacked extension.");
        }

        var existing = Extensions.FirstOrDefault(extension =>
            string.Equals(extension.FolderPath, fullPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Name = name;
            existing.Version = version;
            existing.ManifestVersion = manifestVersion;
            existing.IsEnabled = true;
            Save();
            return existing;
        }

        var extension = new BrowserExtension
        {
            Name = name,
            Version = version,
            FolderPath = fullPath,
            ManifestVersion = manifestVersion,
            IsEnabled = true
        };
        Extensions.Add(extension);
        Save();
        return extension;
    }

    public void SetEnabled(BrowserExtension extension, bool isEnabled)
    {
        extension.IsEnabled = isEnabled;
        Save();
    }

    public void Remove(BrowserExtension extension)
    {
        Extensions.Remove(extension);
        Save();
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<List<BrowserExtension>>(
                File.ReadAllText(_filePath),
                JsonOptions) ?? [];
            foreach (var extension in loaded.Where(extension =>
                         !string.IsNullOrWhiteSpace(extension.Name) &&
                         !string.IsNullOrWhiteSpace(extension.FolderPath) &&
                         Path.IsPathFullyQualified(extension.FolderPath)))
            {
                Extensions.Add(extension);
            }
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            Extensions.Clear();
        }
    }

    private void Save()
    {
        var parent = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllText(_filePath, JsonSerializer.Serialize(Extensions, JsonOptions));
    }

    private static bool IsValidExtensionFolder(string folderPath) =>
        Directory.Exists(folderPath) && File.Exists(Path.Combine(folderPath, "manifest.json"));

    private static string GetRequiredString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element) ||
            element.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(element.GetString()))
        {
            throw new ArgumentException($"The extension manifest is missing a valid '{propertyName}' value.");
        }

        return element.GetString()!;
    }
}
