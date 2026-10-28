using System.IO;
using iscWBS.Core.Services;

namespace iscWBS.Helpers;

public static class Logger
{
    private static readonly string LogDirectory = Path.Combine(SettingsService.AppDataFolder, "Logs");

    public static void Write(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            string logFile = Path.Combine(LogDirectory, $"log-{DateTime.Now:yyyy-MM-dd}.txt");
            string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}";
            File.AppendAllText(logFile, entry);
        }
        catch
        {
            // Logging must never throw.
        }
    }
}
