using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
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
    private readonly string _managedExtensionsDirectory;

    private ExtensionService(string filePath)
    {
        _filePath = filePath;
        _managedExtensionsDirectory = Path.Combine(Path.GetDirectoryName(filePath)!, "Extensions");
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

    public IReadOnlyList<string> GetEnabledFolders()
    {
        var valid = new List<string>();
        foreach (var extension in Extensions.Where(extension => extension.IsEnabled))
        {
            if (IsValidExtensionFolder(extension.FolderPath)) valid.Add(extension.FolderPath);
            else QuartzLog.Error("Extension startup validation", new InvalidDataException($"Skipped invalid extension folder: {extension.Name}"));
        }
        return valid.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public BrowserExtension AddUnpacked(string folderPath, string sourceType = "Local folder", bool isEnabled = true)
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
        var description = root.TryGetProperty("description", out var descriptionElement) && descriptionElement.ValueKind == JsonValueKind.String ? descriptionElement.GetString() ?? string.Empty : string.Empty;
        var permissions = root.TryGetProperty("permissions", out var permissionsElement) && permissionsElement.ValueKind == JsonValueKind.Array
            ? string.Join(", ", permissionsElement.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()))
            : string.Empty;
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
            existing.Description = description;
            existing.Permissions = permissions;
            existing.SourceType = sourceType;
            existing.IsEnabled = isEnabled;
            Save();
            return existing;
        }

        var extension = new BrowserExtension
        {
            Name = name,
            Version = version,
            FolderPath = fullPath,
            ManifestVersion = manifestVersion,
            IsEnabled = isEnabled,
            Id = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fullPath))).ToLowerInvariant()[..32],
            Description = description,
            Permissions = permissions,
            SourceType = sourceType,
            InstalledAt = DateTimeOffset.Now
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

    public BrowserExtension InstallZipPackage(string packagePath, string sourceType, bool isEnabled = false)
    {
        if (!string.Equals(Path.GetExtension(packagePath), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only .zip extension packages are supported. .crx packages must be unpacked manually.");
        }

        Directory.CreateDirectory(_managedExtensionsDirectory);
        var destination = Path.Combine(_managedExtensionsDirectory, $"extension-{Guid.NewGuid():N}");
        Directory.CreateDirectory(destination);
        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            foreach (var entry in archive.Entries)
            {
                var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
                if (!target.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("The extension archive contains an unsafe path.");
                }

                if (string.IsNullOrEmpty(entry.Name)) Directory.CreateDirectory(target);
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: false);
                }
            }

            var manifestFolder = File.Exists(Path.Combine(destination, "manifest.json"))
                ? destination
                : Directory.EnumerateDirectories(destination).FirstOrDefault(folder => File.Exists(Path.Combine(folder, "manifest.json")));
            if (manifestFolder is null) throw new ArgumentException("The package does not contain a manifest.json extension folder.");
            return AddUnpacked(manifestFolder, sourceType, isEnabled);
        }
        catch
        {
            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
            throw;
        }
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
