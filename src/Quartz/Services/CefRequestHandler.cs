using System.Windows.Threading;
using CefSharp;
using CefSharp.Handler;

namespace Quartz.Services;

internal sealed class CefRequestHandler(
    Dispatcher dispatcher,
    Action<string, CefErrorCode> certificateBlocked) : RequestHandler
{
    protected override bool OnCertificateError(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        CefErrorCode errorCode,
        string requestUrl,
        ISslInfo sslInfo,
        IRequestCallback callback)
    {
        dispatcher.BeginInvoke(() => certificateBlocked(requestUrl, errorCode));
        return false;
    }
}
