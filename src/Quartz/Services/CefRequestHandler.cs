using System.Windows.Threading;
using CefSharp;
using CefSharp.Handler;

namespace Quartz.Services;

internal sealed class CefRequestHandler(
    Dispatcher dispatcher,
    Action<string, CefErrorCode> certificateBlocked,
    Action<string> unsupportedNavigation) : RequestHandler
{
    protected override bool OnBeforeBrowse(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, bool userGesture, bool isRedirect)
    {
        try
        {
            var url = request?.Url;
            if (IsUnsupportedExtensionRequest(url))
            {
                dispatcher.BeginInvoke(() => unsupportedNavigation(url!));
                return true;
            }
        }
        catch (Exception exception)
        {
            QuartzLog.Error("CEF OnBeforeBrowse", exception);
            return true;
        }

        return false;
    }

    protected override bool OnCertificateError(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        CefErrorCode errorCode,
        string requestUrl,
        ISslInfo sslInfo,
        IRequestCallback callback)
    {
        try { dispatcher.BeginInvoke(() => certificateBlocked(requestUrl, errorCode)); }
        catch (Exception exception) { QuartzLog.Error("CEF certificate callback", exception); }
        return false;
    }

    private static bool IsUnsupportedExtensionRequest(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme is "chromewebstore" or "chrome-extension") return true;
        if (uri.Scheme == "chrome") return true;
        return uri.Host.Equals("chromewebstore.google.com", StringComparison.OrdinalIgnoreCase) ||
               (uri.Host.Equals("clients2.google.com", StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath.Contains("/service/update2/crx", StringComparison.OrdinalIgnoreCase));
    }
}
