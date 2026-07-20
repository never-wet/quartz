using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;

namespace Quartz;

internal sealed class BrowserTab(WebView2 browser) : IDisposable
{
    public WebView2 Browser { get; } = browser;

    public TabItem HeaderItem { get; set; } = null!;

    public TextBlock TitleBlock { get; set; } = null!;

    public string Title { get; set; } = "New tab";

    public string Status { get; set; } = "Starting browser engine...";

    public bool IsInitialized { get; set; }

    public bool IsLoading { get; set; }

    public bool IsNewTabPage { get; set; }

    public bool IsRenderingNewTabPage { get; set; }

    public bool IsClosed { get; private set; }

    public void Dispose()
    {
        if (IsClosed)
        {
            return;
        }

        IsClosed = true;
        Browser.Dispose();
    }
}
