using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using iscWBS.Core.Models;

namespace iscWBS.Core.Services;

/// <summary>Checks GitHub Releases for a newer version and installs its ZIP in place of the running app.</summary>
public sealed partial class UpdateService
{
    /// <summary>Where the files an update replaced are moved, since they're in use until the app restarts.</summary>
    internal const string OldVersionDir = "iscWBS-old";

    private const string _gitHubApiUrl = "https://api.github.com/repos/iiisc/iscWBS/releases/latest";
    private const string _exeName = "iscWBS.exe";

    private readonly HttpClient _httpClient;

    public UpdateService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "iscWBS-UpdateChecker");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
    }

    public string CurrentVersion
    {
        get
        {
            string? path = Environment.ProcessPath;
            if (string.IsNullOrEmpty(path)) return "0.0.0";
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
            return $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}";
        }
    }

    /// <summary>The latest release, or <see langword="null"/> when this version is already current.</summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        GitHubRelease? release = await _httpClient.GetFromJsonAsync(_gitHubApiUrl, GitHubJsonContext.Default.GitHubRelease);
        if (release is null
            || !Version.TryParse(release.TagName.TrimStart('v'), out Version? latestVersion)
            || !Version.TryParse(CurrentVersion, out Version? currentVersion)
            || latestVersion <= currentVersion)
            return null;

        GitHubAsset? zip = FindZipAsset(release.Assets);
        string? sha256 = zip?.Digest is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : null;
        return new UpdateInfo(release.TagName, release.Name, release.HtmlUrl, zip?.BrowserDownloadUrl, sha256);
    }

    /// <summary>
    /// Downloads and verifies the release, then puts it in place of the running app. Returns <see langword="true"/>
    /// when the caller must restart into the new version; <see langword="false"/> when the release has no verifiable
    /// ZIP and its page was opened instead. If installing fails, the app is left as it was.
    /// </summary>
    public async Task<bool> DownloadAndInstallAsync(UpdateInfo info, IProgress<int> progress, CancellationToken cancellationToken = default)
    {
        if (info.DownloadUrl is null || info.Sha256 is null)
        {
            OpenReleasePage(info);
            return false;
        }

        string tempDir = Path.Combine(Path.GetTempPath(), "iscWBSUpdate");
        try
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
            Directory.CreateDirectory(tempDir);
            string zipPath = Path.Combine(tempDir, "release.zip");

            using (HttpResponseMessage response = await _httpClient.GetAsync(
                info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                long? totalBytes = response.Content.Headers.ContentLength;
                await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using FileStream fileStream = File.Create(zipPath);

                byte[] buffer = new byte[81920];
                long downloaded = 0;
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    downloaded += bytesRead;
                    if (totalBytes > 0)
                        progress.Report((int)(downloaded * 100 / totalBytes.Value));
                }
            }

            if (!Sha256Matches(zipPath, info.Sha256))
                throw new InvalidDataException("The downloaded update does not match the SHA-256 digest GitHub published for it.");

            string releaseDir = Path.Combine(tempDir, "release");
            ZipFile.ExtractToDirectory(zipPath, releaseDir);
            // The release zip holds the app in an iscWBS folder, so unzipping it by hand can't spill files elsewhere.
            SwapIn(Path.Combine(releaseDir, "iscWBS"), Environment.ProcessPath ?? throw new InvalidOperationException("The app could not find its own exe."));
            return true;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Deletes the files the last update replaced; that version isn't running any more. Best effort, on start.</summary>
    public static void DeleteOldVersion()
    {
        try { Directory.Delete(Path.Combine(AppContext.BaseDirectory, OldVersionDir), recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Moves the release's files into the app's folder. Windows won't let a running program's files be overwritten, but
    /// it does let them be moved, so each one being replaced goes into <see cref="OldVersionDir"/> first, for the next
    /// start to delete. Files the release doesn't contain are left alone. If anything fails, every file is put back.
    /// </summary>
    internal static void SwapIn(string releaseDir, string currentExe)
    {
        if (!File.Exists(Path.Combine(releaseDir, _exeName)))
            throw new InvalidDataException($"The update package does not contain {_exeName}.");
        string appDir = Path.GetDirectoryName(currentExe)!;
        string oldDir = Path.Combine(appDir, OldVersionDir);
        if (Directory.Exists(oldDir))
            Directory.Delete(oldDir, recursive: true);

        var replaced = new List<(string File, string MovedTo)>();
        var added = new List<string>();
        try
        {
            foreach (string file in Directory.EnumerateFiles(releaseDir, "*", SearchOption.AllDirectories))
            {
                string name = Path.GetRelativePath(releaseDir, file);
                // A copy of the app the user renamed keeps its name.
                string target = name == _exeName ? currentExe : Path.Combine(appDir, name);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (File.Exists(target))
                {
                    string movedTo = Path.Combine(oldDir, Path.GetRelativePath(appDir, target));
                    Directory.CreateDirectory(Path.GetDirectoryName(movedTo)!);
                    File.Move(target, movedTo);
                    replaced.Add((target, movedTo));
                }
                else
                {
                    added.Add(target);
                }
                File.Move(file, target);
            }
        }
        catch
        {
            foreach (string file in added)
                try { File.Delete(file); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            foreach ((string file, string movedTo) in replaced)
            {
                try
                {
                    File.Delete(file);
                    File.Move(movedTo, file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            throw;
        }
    }

    internal static bool Sha256Matches(string filePath, string expectedHex)
    {
        using FileStream stream = File.OpenRead(filePath);
        return string.Equals(Convert.ToHexString(SHA256.HashData(stream)), expectedHex, StringComparison.OrdinalIgnoreCase);
    }

    public void OpenReleasePage(UpdateInfo info)
        => Process.Start(new ProcessStartInfo(info.ReleasePageUrl) { UseShellExecute = true });

    /// <summary>The x64 ZIP if there is one, otherwise any ZIP.</summary>
    private static GitHubAsset? FindZipAsset(List<GitHubAsset> assets)
        => assets
            .Where(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.Name.Contains("x64", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

    [JsonSerializable(typeof(GitHubRelease))]
    private sealed partial class GitHubJsonContext : JsonSerializerContext;

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; init; } = string.Empty;

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; init; } = [];
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; init; } = string.Empty;

        /// <summary>"sha256:&lt;hex&gt;"; GitHub computes it on upload.</summary>
        [JsonPropertyName("digest")]
        public string? Digest { get; init; }
    }
}
