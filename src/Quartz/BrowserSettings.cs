using Quartz.Models;

namespace Quartz;

internal static class BrowserSettings
{
    public const string DefaultHomePage = "https://duckduckgo.com/";
    public const string BlankPage = "about:blank";
    public const string NewTabPage = "quartz://newtab";

    public static string GetSearchUrl(SearchEngine searchEngine) =>
        searchEngine switch
        {
            SearchEngine.Google => "https://www.google.com/search?q=",
            SearchEngine.Bing => "https://www.bing.com/search?q=",
            _ => "https://duckduckgo.com/?q="
        };
}
