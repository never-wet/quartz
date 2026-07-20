using System.Windows;
using System.Windows.Threading;
using CefSharp;
using CefSharp.Handler;

namespace Quartz.Services;

internal sealed class CefPermissionHandler(
    Window owner,
    Dispatcher dispatcher) : PermissionHandler
{
    protected override bool OnRequestMediaAccessPermission(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IFrame frame,
        string requestingOrigin,
        MediaAccessPermissionType requestedPermissions,
        IMediaAccessCallback callback)
    {
        dispatcher.BeginInvoke(() =>
        {
            var permission = DescribeMediaPermission(requestedPermissions);
            var allowed = ShowPrompt(requestingOrigin, permission);
            callback.Continue(allowed ? requestedPermissions : MediaAccessPermissionType.None);
            callback.Dispose();
        });
        return true;
    }

    protected override bool OnShowPermissionPrompt(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        ulong promptId,
        string requestingOrigin,
        PermissionRequestType requestedPermissions,
        IPermissionPromptCallback callback)
    {
        dispatcher.BeginInvoke(() =>
        {
            var permission = DescribePermission(requestedPermissions);
            var allowed = ShowPrompt(requestingOrigin, permission);
            callback.Continue(allowed ? PermissionRequestResult.Accept : PermissionRequestResult.Deny);
            callback.Dispose();
        });
        return true;
    }

    private bool ShowPrompt(string origin, string permission)
    {
        var domain = Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
                     !string.IsNullOrWhiteSpace(uri.Host)
            ? uri.Host
            : "This site";
        var dialog = new PermissionPromptWindow(domain, permission, origin)
        {
            Owner = owner
        };
        return dialog.ShowDialog() == true;
    }

    private static string DescribeMediaPermission(MediaAccessPermissionType permissions)
    {
        var hasAudio = permissions.HasFlag(MediaAccessPermissionType.AudioCapture);
        var hasVideo = permissions.HasFlag(MediaAccessPermissionType.VideoCapture);
        return (hasAudio, hasVideo) switch
        {
            (true, true) => "camera and microphone",
            (true, false) => "microphone",
            (false, true) => "camera",
            _ => "media devices"
        };
    }

    private static string DescribePermission(PermissionRequestType permissions)
    {
        var value = permissions.ToString();
        if (value.Contains("Geo", StringComparison.OrdinalIgnoreCase))
        {
            return "location";
        }
        if (value.Contains("Notification", StringComparison.OrdinalIgnoreCase))
        {
            return "notifications";
        }
        if (value.Contains("Camera", StringComparison.OrdinalIgnoreCase))
        {
            return "camera";
        }
        if (value.Contains("Microphone", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Audio", StringComparison.OrdinalIgnoreCase))
        {
            return "microphone";
        }

        return value.Replace(",", " and", StringComparison.Ordinal).ToLowerInvariant();
    }
}
