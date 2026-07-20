using System.Windows;
using System.Windows.Controls;
using CefSharp.Wpf;

namespace Quartz;

internal sealed class BrowserTab(ChromiumWebBrowser browser) : IDisposable
{
    public ChromiumWebBrowser Browser { get; } = browser;

    // The strip owns a lightweight header view; the Chromium browser remains independent of it.
    public FrameworkElement HeaderItem { get; set; } = null!;

    public TextBlock TitleBlock { get; set; } = null!;

    public Image FaviconImage { get; set; } = null!;

    public string Title { get; set; } = "New tab";

    public string Status { get; set; } = "Starting browser engine...";

    public bool IsInitialized { get; set; }

    public bool IsLoading { get; set; }

    public bool IsNewTabPage { get; set; }

    public bool IsRenderingNewTabPage { get; set; }

    public bool LastLoadFailed { get; set; }

    public string? FaviconUrl { get; set; }

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
