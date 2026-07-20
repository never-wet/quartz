using System.Windows.Threading;
using CefSharp;
using CefSharp.Handler;

namespace Quartz.Services;

internal sealed class CefLifeSpanHandler(
    Dispatcher dispatcher,
    Action<string> openInNewTab) : LifeSpanHandler
{
    protected override bool OnBeforePopup(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IFrame frame,
        string targetUrl,
        string targetFrameName,
        WindowOpenDisposition targetDisposition,
        bool userGesture,
        IPopupFeatures popupFeatures,
        IWindowInfo windowInfo,
        IBrowserSettings browserSettings,
        ref bool noJavascriptAccess,
        out IWebBrowser? newBrowser)
    {
        newBrowser = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(targetUrl)) dispatcher.BeginInvoke(() => openInNewTab(targetUrl));
        }
        catch (Exception exception) { QuartzLog.Error("CEF popup callback", exception); }

        return true;
    }
}
