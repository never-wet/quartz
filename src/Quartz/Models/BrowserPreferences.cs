namespace Quartz.Models;

internal enum SearchEngine
{
    DuckDuckGo,
    Google,
    Bing
}

internal enum StartupBehavior
{
    Homepage,
    BlankPage
}

internal enum BrowserTheme
{
    Light,
    Dark,
    Midnight,
    Neon
}

internal enum AccentPreset
{
    Violet,
    Blue,
    Teal,
    Rose,
    Lime
}

internal sealed class BrowserPreferences
{
    public string HomepageUrl { get; set; } = BrowserSettings.DefaultHomePage;

    public SearchEngine SearchEngine { get; set; } = SearchEngine.DuckDuckGo;

    public StartupBehavior StartupBehavior { get; set; } = StartupBehavior.Homepage;

    public BrowserTheme Theme { get; set; } = BrowserTheme.Light;

    public AccentPreset Accent { get; set; } = AccentPreset.Violet;

    public bool SidebarVisible { get; set; } = true;
}
