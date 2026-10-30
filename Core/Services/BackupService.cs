using System.Security.Cryptography;
using System.Text;

namespace iscWBS.Core.Services;

/// <summary>
/// Copies of each project kept on this computer, so a project on a lost or damaged USB stick can be recovered.
/// A copy is taken whenever a project opens or closes, skipping ones identical to the last, and the newest
/// <see cref="Keep"/> of each project are kept.
/// </summary>
public sealed class BackupService(string folder, Action<Exception>? onError = null)
{
    public const int Keep = 20;

    public static string DefaultFolder => Path.Combine(SettingsService.AppDataFolder, "Backups");

    public string Folder { get; } = folder;

    /// <summary>Copies the project file, which must not be open. A failure is reported, not thrown: it must never stop work.</summary>
    public void BackUp(string projectPath)
    {
        try
        {
            string folder = FolderFor(projectPath);
            Directory.CreateDirectory(folder);
            List<string> existing = Backups(folder);
            // A project opened and closed without changes would otherwise fill the folder with identical copies.
            if (existing.Count > 0 && File.ReadAllBytes(existing[0]).AsSpan().SequenceEqual(File.ReadAllBytes(projectPath)))
                return;

            File.Copy(projectPath, Path.Combine(folder, $"{DateTime.Now:yyyy-MM-dd_HHmmss}.iscwbs"), overwrite: true);
            foreach (string old in Backups(folder).Skip(Keep))
                File.Delete(old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            onError?.Invoke(ex);
        }
    }

    /// <summary>One folder per project file, named after it; the hash of its full path keeps same-named files apart.</summary>
    public string FolderFor(string projectPath)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(projectPath).ToUpperInvariant()));
        return Path.Combine(Folder, $"{Path.GetFileNameWithoutExtension(projectPath)}-{Convert.ToHexString(hash, 0, 4)}");
    }

    /// <summary>Newest first; the names sort by time.</summary>
    private static List<string> Backups(string folder)
        => Directory.EnumerateFiles(folder, "*.iscwbs").OrderByDescending(Path.GetFileName, StringComparer.Ordinal).ToList();
}
