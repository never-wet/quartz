using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.Web.WebView2.Core;

namespace Quartz.Models;

internal sealed class DownloadItem : INotifyPropertyChanged
{
    private long _bytesReceived;
    private ulong? _totalBytes;
    private string _status;
    private bool _isActive;
    private bool _isCompleted;
    private DateTimeOffset? _completedAt;

    private DownloadItem(
        string fileName,
        string sourceUrl,
        string savePath,
        string status,
        bool isActive,
        bool isCompleted,
        long bytesReceived,
        ulong? totalBytes,
        DateTimeOffset? completedAt)
    {
        FileName = fileName;
        SourceUrl = sourceUrl;
        SavePath = savePath;
        _status = status;
        _isActive = isActive;
        _isCompleted = isCompleted;
        _bytesReceived = bytesReceived;
        _totalBytes = totalBytes;
        _completedAt = completedAt;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string FileName { get; }

    public string SourceUrl { get; }

    public string SavePath { get; }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public bool IsActive
    {
        get => _isActive;
        private set => SetField(ref _isActive, value);
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        private set => SetField(ref _isCompleted, value);
    }

    public DateTimeOffset? CompletedAt
    {
        get => _completedAt;
        private set => SetField(ref _completedAt, value);
    }

    public double ProgressPercent =>
        _totalBytes is > 0
            ? Math.Clamp((double)_bytesReceived / _totalBytes.Value * 100, 0, 100)
            : IsCompleted ? 100 : 0;

    public bool IsIndeterminate => IsActive && _totalBytes is not > 0;

    public string ProgressText =>
        _totalBytes is > 0
            ? $"{FormatBytes(_bytesReceived)} of {FormatBytes(_totalBytes.Value)} ({ProgressPercent:0}%)"
            : $"{FormatBytes(_bytesReceived)} downloaded";

    internal CoreWebView2DownloadOperation? Operation { get; private set; }

    internal static DownloadItem CreateActive(
        CoreWebView2DownloadOperation operation,
        string savePath) =>
        new(
            Path.GetFileName(savePath),
            operation.Uri ?? string.Empty,
            savePath,
            "Downloading",
            isActive: true,
            isCompleted: false,
            operation.BytesReceived,
            operation.TotalBytesToReceive,
            completedAt: null)
        {
            Operation = operation
        };

    internal static DownloadItem FromRecord(DownloadRecord record)
    {
        var fileName = string.IsNullOrWhiteSpace(record.FileName)
            ? Path.GetFileName(record.SavePath)
            : record.FileName;

        return new(
            string.IsNullOrWhiteSpace(fileName) ? "download" : fileName,
            record.SourceUrl,
            record.SavePath,
            "Completed",
            isActive: false,
            isCompleted: true,
            record.BytesReceived,
            (ulong)Math.Max(0, record.BytesReceived),
            record.CompletedAt);
    }

    internal void UpdateProgress(long bytesReceived, ulong? totalBytes)
    {
        _bytesReceived = bytesReceived;
        _totalBytes = totalBytes;
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(IsIndeterminate));
        OnPropertyChanged(nameof(ProgressText));
    }

    internal void MarkCompleted(DateTimeOffset completedAt)
    {
        IsActive = false;
        IsCompleted = true;
        CompletedAt = completedAt;
        Status = "Completed";
        Operation = null;
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(IsIndeterminate));
        OnPropertyChanged(nameof(ProgressText));
    }

    internal void MarkInterrupted(CoreWebView2DownloadInterruptReason reason)
    {
        IsActive = false;
        IsCompleted = false;
        Status = reason == CoreWebView2DownloadInterruptReason.UserCanceled
            ? "Canceled"
            : $"Failed: {AddSpaces(reason.ToString())}";
        Operation = null;
        OnPropertyChanged(nameof(IsIndeterminate));
    }

    internal DownloadRecord ToRecord() =>
        new()
        {
            FileName = FileName,
            SourceUrl = SourceUrl,
            SavePath = SavePath,
            BytesReceived = _bytesReceived,
            CompletedAt = CompletedAt ?? DateTimeOffset.Now
        };

    private static string FormatBytes(long bytes) =>
        FormatBytes((ulong)Math.Max(0, bytes));

    private static string FormatBytes(ulong bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var unitIndex = 0;
        var displayValue = (double)bytes;
        while (displayValue >= 1024 && unitIndex < units.Length - 1)
        {
            displayValue /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{displayValue:0} {units[unitIndex]}"
            : $"{displayValue:0.##} {units[unitIndex]}";
    }

    private static string AddSpaces(string value) =>
        System.Text.RegularExpressions.Regex.Replace(value, "(?<!^)([A-Z])", " $1");

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
