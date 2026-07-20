using System.IO;

namespace Quartz.Services;

internal static class QuartzLog
{
    public static void Error(string area, Exception exception)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quartz", "Logs");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "quartz.log"), $"[{DateTimeOffset.Now:O}] {area}: {exception.Message}{Environment.NewLine}{exception.StackTrace}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never destabilize a Chromium callback.
        }
    }

    public static string WriteStartup(Exception exception)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quartz", "Logs");
        var path = Path.Combine(directory, "startup.log");
        try
        {
            Directory.CreateDirectory(directory);
            File.AppendAllText(path, $"[{DateTimeOffset.Now:O}] {exception.GetType().FullName}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
        }

        return path;
    }
}
