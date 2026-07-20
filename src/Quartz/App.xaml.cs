using System.IO;
using System.Windows;
using CefSharp;
using Quartz.Services;

namespace Quartz;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var preferences = SettingsService.CreateDefault().Current;
        ThemeManager.Apply(preferences.Theme, preferences.Accent);

        var profileRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Quartz",
            "Chromium");
        Directory.CreateDirectory(profileRoot);

        CefSharpSettings.SubprocessExitIfParentProcessClosed = true;
        var cefSettings = new CefSharp.Wpf.CefSettings
        {
            RootCachePath = profileRoot,
            CachePath = Path.Combine(profileRoot, "Default"),
            PersistSessionCookies = true,
            WindowlessRenderingEnabled = true,
            LogSeverity = LogSeverity.Disable,
            UserAgentProduct = "Quartz/2.0"
        };

        // CEF 128+ removed the old dynamic extension API. Current CEF can load
        // unpacked extensions at process start; toolbar-dependent APIs remain limited
        // in WPF's off-screen/custom-window rendering mode.
        var enabledExtensionFolders = ExtensionService.CreateDefault().GetEnabledFolders();
        if (enabledExtensionFolders.Count > 0)
        {
            cefSettings.CefCommandLineArgs["load-extension"] =
                string.Join(',', enabledExtensionFolders);
        }

        if (int.TryParse(
                Environment.GetEnvironmentVariable("QUARTZ_REMOTE_DEBUGGING_PORT"),
                out var remoteDebuggingPort) &&
            remoteDebuggingPort is >= 1024 and <= 65535)
        {
            cefSettings.RemoteDebuggingPort = remoteDebuggingPort;
            cefSettings.CefCommandLineArgs["remote-allow-origins"] = "*";
        }

        if (!Cef.Initialize(cefSettings, performDependencyCheck: true, browserProcessHandler: null))
        {
            MessageBox.Show(
                "Quartz could not initialize its Chromium engine.",
                "Quartz startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cef.Shutdown();
        base.OnExit(e);
    }
}
