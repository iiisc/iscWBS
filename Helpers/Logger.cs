using System.IO;
using iscWBS.Core.Services;

namespace iscWBS.Helpers;

public static class Logger
{
    private const int KeepDays = 30;
    private static readonly string LogDirectory = Path.Combine(SettingsService.AppDataFolder, "Logs");
    private static bool _pruned;

    public static void Write(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            string logFile = Path.Combine(LogDirectory, $"log-{DateTime.Now:yyyy-MM-dd}.txt");
            string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}";
            File.AppendAllText(logFile, entry);

            if (_pruned) return;
            _pruned = true;
            DateTime cutoff = DateTime.Now.AddDays(-KeepDays);
            foreach (string old in Directory.EnumerateFiles(LogDirectory, "log-*.txt"))
                if (File.GetLastWriteTime(old) < cutoff)
                    File.Delete(old);
        }
        catch
        {
            // Logging must never throw.
        }
    }
}
