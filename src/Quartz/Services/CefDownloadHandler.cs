using System.IO;
using System.Windows.Threading;
using CefSharp;
using CefSharp.Handler;
using CefDownloadItem = CefSharp.DownloadItem;

namespace Quartz.Services;

internal sealed class CefDownloadHandler(
    DownloadService downloadService,
    Dispatcher dispatcher,
    Action<string> updateStatus) : DownloadHandler
{
    private readonly object _callbackLock = new();
    private readonly Dictionary<int, IDownloadItemCallback> _callbacks = [];

    protected override bool OnBeforeDownload(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        CefDownloadItem downloadItem,
        IBeforeDownloadCallback callback)
    {
        var savePath = dispatcher.Invoke(() =>
            downloadService.CreateSavePath(downloadItem.SuggestedFileName, downloadItem.Url));

        dispatcher.Invoke(() =>
        {
            downloadService.Track(
                downloadItem.Id,
                downloadItem.Url ?? string.Empty,
                savePath,
                downloadItem.ReceivedBytes,
                downloadItem.TotalBytes,
                () => Cancel(downloadItem.Id));
            updateStatus($"Downloading {Path.GetFileName(savePath)}...");
        });

        callback.Continue(savePath, showDialog: false);
        return true;
    }

    protected override void OnDownloadUpdated(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        CefDownloadItem downloadItem,
        IDownloadItemCallback callback)
    {
        lock (_callbackLock)
        {
            if (_callbacks.Remove(downloadItem.Id, out var previous) && !ReferenceEquals(previous, callback))
            {
                previous.Dispose();
            }

            _callbacks[downloadItem.Id] = callback;
        }

        dispatcher.BeginInvoke(() =>
        {
            downloadService.Update(
                downloadItem.Id,
                downloadItem.ReceivedBytes,
                downloadItem.TotalBytes,
                downloadItem.IsComplete,
                downloadItem.IsCancelled,
                downloadItem.IsInProgress);

            if (downloadItem.IsComplete)
            {
                updateStatus($"Downloaded {downloadItem.SuggestedFileName}.");
                ReleaseCallback(downloadItem.Id);
            }
            else if (downloadItem.IsCancelled)
            {
                updateStatus($"Canceled {downloadItem.SuggestedFileName}.");
                ReleaseCallback(downloadItem.Id);
            }
            else if (!downloadItem.IsInProgress)
            {
                updateStatus($"Download failed: {downloadItem.SuggestedFileName}.");
                ReleaseCallback(downloadItem.Id);
            }
        });
    }

    private void Cancel(int downloadId)
    {
        lock (_callbackLock)
        {
            if (_callbacks.TryGetValue(downloadId, out var callback) && !callback.IsDisposed)
            {
                callback.Cancel();
            }
        }
    }

    private void ReleaseCallback(int downloadId)
    {
        lock (_callbackLock)
        {
            if (_callbacks.Remove(downloadId, out var callback))
            {
                callback.Dispose();
            }
        }
    }
}
