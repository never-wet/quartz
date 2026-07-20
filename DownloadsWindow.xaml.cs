using System.Diagnostics;
using System.IO;
using System.Windows;
using Quartz.Models;
using Quartz.Services;

namespace Quartz;

public partial class DownloadsWindow : Window
{
    private readonly DownloadService _service;

    public DownloadsWindow(DownloadService service)
    {
        InitializeComponent();
        _service = service;
        DataContext = service;
    }

    private void ClearCompleted_Click(object sender, RoutedEventArgs e) => _service.ClearCompleted();

    private static DownloadItem? Item(object sender) => (sender as FrameworkElement)?.Tag as DownloadItem;

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        var item = Item(sender);
        if (item is null || !File.Exists(item.SavePath)) return;
        Process.Start(new ProcessStartInfo(item.SavePath) { UseShellExecute = true });
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var item = Item(sender);
        var folder = item is null ? null : Path.GetDirectoryName(item.SavePath);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        var item = Item(sender);
        if (item is not null) _service.Cancel(item);
    }
}
