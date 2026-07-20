using System.ComponentModel;

namespace Quartz.Models;

internal sealed class BrowserExtension : INotifyPropertyChanged
{
    private bool _isEnabled;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string FolderPath { get; set; } = string.Empty;

    public int ManifestVersion { get; set; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
            {
                return;
            }

            _isEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }
    }

    public string Status => IsEnabled
        ? "Enabled · applied when Quartz starts"
        : "Disabled";
}
