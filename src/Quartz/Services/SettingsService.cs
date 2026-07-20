using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Quartz.Models;

namespace Quartz.Services;

internal sealed class SettingsService
{
    private const string TestFileEnvironmentVariable = "QUARTZ_SETTINGS_FILE";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _filePath;

    private SettingsService(string filePath)
    {
        _filePath = filePath;
        Current = Load();
    }

    public BrowserPreferences Current { get; private set; }

    public static SettingsService CreateDefault()
    {
        var testFilePath = Environment.GetEnvironmentVariable(TestFileEnvironmentVariable);
        var filePath = string.IsNullOrWhiteSpace(testFilePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Quartz",
                "settings.json")
            : testFilePath;

        return new SettingsService(filePath);
    }

    public void Update(
        string homepageInput,
        SearchEngine searchEngine,
        StartupBehavior startupBehavior,
        BrowserTheme theme,
        AccentPreset accent,
        bool sidebarVisible)
    {
        var homepage = AddressInterpreter.ToWebUri(homepageInput) ??
                       throw new ArgumentException("Enter a valid HTTP or HTTPS homepage.", nameof(homepageInput));
        if (!Enum.IsDefined(searchEngine) ||
            !Enum.IsDefined(startupBehavior) ||
            !Enum.IsDefined(theme) ||
            !Enum.IsDefined(accent))
        {
            throw new ArgumentException("The selected setting is not supported.");
        }

        var updated = new BrowserPreferences
        {
            HomepageUrl = homepage.AbsoluteUri,
            SearchEngine = searchEngine,
            StartupBehavior = startupBehavior,
            Theme = theme,
            Accent = accent,
            SidebarVisible = sidebarVisible
        };

        Save(updated);
        Current = updated;
    }

    public void UpdateAppearance(BrowserTheme theme, AccentPreset accent, bool sidebarVisible)
    {
        if (!Enum.IsDefined(theme) || !Enum.IsDefined(accent))
        {
            throw new ArgumentException("The selected appearance setting is not supported.");
        }

        var updated = new BrowserPreferences
        {
            HomepageUrl = Current.HomepageUrl,
            SearchEngine = Current.SearchEngine,
            StartupBehavior = Current.StartupBehavior,
            Theme = theme,
            Accent = accent,
            SidebarVisible = sidebarVisible
        };

        Save(updated);
        Current = updated;
    }

    private BrowserPreferences Load()
    {
        if (!File.Exists(_filePath))
        {
            return CreateDefaults();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var stored = JsonSerializer.Deserialize<BrowserPreferences>(json, JsonOptions);
            var homepage = AddressInterpreter.ToWebUri(stored?.HomepageUrl);
            if (stored is null ||
                homepage is null ||
                !Enum.IsDefined(stored.SearchEngine) ||
                !Enum.IsDefined(stored.StartupBehavior) ||
                !Enum.IsDefined(stored.Theme) ||
                !Enum.IsDefined(stored.Accent))
            {
                return CreateDefaults();
            }

            return new BrowserPreferences
            {
                HomepageUrl = homepage.AbsoluteUri,
                SearchEngine = stored.SearchEngine,
                StartupBehavior = stored.StartupBehavior,
                Theme = stored.Theme,
                Accent = stored.Accent,
                SidebarVisible = stored.SidebarVisible
            };
        }
        catch (JsonException)
        {
            return CreateDefaults();
        }
        catch (IOException)
        {
            return CreateDefaults();
        }
        catch (UnauthorizedAccessException)
        {
            return CreateDefaults();
        }
    }

    private void Save(BrowserPreferences settings)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_filePath, json);
    }

    private static BrowserPreferences CreateDefaults() => new();
}
