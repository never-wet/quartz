using System.Windows;
using Quartz.Services;

namespace Quartz;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var preferences = SettingsService.CreateDefault().Current;
        ThemeManager.Apply(preferences.Theme, preferences.Accent);
        base.OnStartup(e);
    }
}
