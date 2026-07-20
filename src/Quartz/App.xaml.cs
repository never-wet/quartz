using System.IO;
using System.Windows;
using System.Windows.Input;
using CefSharp;
using Quartz.Services;

namespace Quartz;

public partial class App : Application
{
    private bool _cefInitialized;

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            var preferences = SettingsService.CreateDefault().Current;
            ThemeManager.Apply(preferences.Theme, preferences.Accent);

            var quartzRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quartz");
            var profileRoot = Path.Combine(quartzRoot, "Chromium");
            var logsDirectory = Path.Combine(quartzRoot, "Logs");
            Directory.CreateDirectory(profileRoot);
            Directory.CreateDirectory(logsDirectory);
            ValidateCefRuntime(AppContext.BaseDirectory);

            CefSharpSettings.SubprocessExitIfParentProcessClosed = true;
            var safeMode = e.Args.Any(argument => string.Equals(argument, "--safe-mode", StringComparison.OrdinalIgnoreCase)) || Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
            if (!InitializeCef(profileRoot, logsDirectory, loadExtensions: !safeMode))
            {
                // A failed initialization can be retried only after releasing CEF's partial state.
                try { Cef.Shutdown(); } catch (Exception exception) { QuartzLog.Error("CEF shutdown after failed startup", exception); }
                if (!InitializeCef(profileRoot, logsDirectory, loadExtensions: false))
                {
                    throw new InvalidOperationException("Cef.Initialize returned false in normal mode and in the extensions-disabled fallback. Check the CEF log and required runtime files.");
                }

                MessageBox.Show("Quartz started with extensions disabled because Chromium could not initialize with the current extension configuration.", "Quartz safe mode", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            _cefInitialized = true;
            base.OnStartup(e);
        }
        catch (Exception exception)
        {
            var logPath = QuartzLog.WriteStartup(exception);
            MessageBox.Show(
                $"Quartz could not initialize its Chromium engine.\n\n{exception.GetType().Name}: {exception.Message}\n\nFull startup details were saved to:\n{logPath}\n\nTry starting Quartz with --safe-mode or hold Shift to disable extensions.",
                "Quartz startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static bool InitializeCef(string profileRoot, string logsDirectory, bool loadExtensions)
    {
        var cefSettings = new CefSharp.Wpf.CefSettings
        {
            RootCachePath = profileRoot,
            CachePath = Path.Combine(profileRoot, "Default"),
            PersistSessionCookies = true,
            WindowlessRenderingEnabled = true,
            LogSeverity = LogSeverity.Warning,
            LogFile = Path.Combine(logsDirectory, "cef.log"),
            UserAgentProduct = "Quartz/2.1"
        };

        if (loadExtensions)
        {
            var folders = ExtensionService.CreateDefault().GetEnabledFolders();
            if (folders.Count > 0) cefSettings.CefCommandLineArgs["load-extension"] = string.Join(',', folders);
        }

        if (int.TryParse(Environment.GetEnvironmentVariable("QUARTZ_REMOTE_DEBUGGING_PORT"), out var port) && port is >= 1024 and <= 65535)
        {
            cefSettings.RemoteDebuggingPort = port;
            cefSettings.CefCommandLineArgs["remote-allow-origins"] = "*";
        }

        return Cef.Initialize(cefSettings, performDependencyCheck: true, browserProcessHandler: null);
    }

    private static void ValidateCefRuntime(string applicationDirectory)
    {
        var required = new[] { "libcef.dll", "CefSharp.Core.Runtime.dll", "CefSharp.BrowserSubprocess.exe", "icudtl.dat" };
        var missing = required.Where(file => !File.Exists(Path.Combine(applicationDirectory, file))).ToList();
        if (!Directory.Exists(Path.Combine(applicationDirectory, "locales"))) missing.Add("locales folder");
        if (missing.Count > 0)
        {
            throw new FileNotFoundException($"The bundled Chromium runtime is incomplete. Missing: {string.Join(", ", missing)}. Reinstall Quartz using the x64 installer.");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_cefInitialized)
        {
            try { Cef.Shutdown(); } catch (Exception exception) { QuartzLog.Error("CEF shutdown", exception); }
        }
        base.OnExit(e);
    }
}
