using CefSharp;
using CefSharp.Handler;

namespace Quartz.Services;

internal sealed class CefDisplayHandler(Action<string?> faviconChanged) : DisplayHandler
{
    protected override void OnFaviconUrlChange(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IList<string> urls)
    {
        try { faviconChanged(urls?.FirstOrDefault()); }
        catch (Exception exception) { QuartzLog.Error("CEF favicon callback", exception); }
    }
}
