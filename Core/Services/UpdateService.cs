using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using iscWBS.Core.Models;

namespace iscWBS.Core.Services;

/// <summary>
/// Checks GitHub Releases for a newer version and installs its ZIP over this one. The running files can't be
/// overwritten, so the downloaded version is started from a staging folder with <see cref="FinishUpdateArgument"/>,
/// waits for this process to exit, copies itself over the install folder and starts it.
/// </summary>
public sealed partial class UpdateService
{
    public const string FinishUpdateArgument = "--finish-update";

    private const string _gitHubApiUrl = "https://api.github.com/repos/iiisc/iscWBS/releases/latest";
    private const string _exeName = "iscWBS.exe";
    private const string _stagingPrefix = "iscWBS-update-";

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
    /// Downloads, verifies and stages the release, then starts it to finish the install. Returns
    /// <see langword="true"/> when the caller must exit straight away so the new version can replace it;
    /// <see langword="false"/> when the release has no verifiable ZIP and its page was opened instead.
    /// </summary>
    public async Task<bool> DownloadAndInstallAsync(UpdateInfo info, IProgress<int> progress, CancellationToken cancellationToken = default)
    {
        if (info.DownloadUrl is null || info.Sha256 is null)
        {
            OpenReleasePage(info);
            return false;
        }

        string stagingDir = Path.Combine(Path.GetTempPath(), _stagingPrefix + info.TagName);
        string zipPath = stagingDir + ".zip";

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

        if (Directory.Exists(stagingDir))
            Directory.Delete(stagingDir, recursive: true);
        ZipFile.ExtractToDirectory(zipPath, stagingDir);
        File.Delete(zipPath);

        string stagedExe = Path.Combine(stagingDir, _exeName);
        if (!File.Exists(stagedExe))
            throw new InvalidDataException($"The update package does not contain {_exeName}.");

        var start = new ProcessStartInfo(stagedExe) { UseShellExecute = false };
        start.ArgumentList.Add(FinishUpdateArgument);
        start.ArgumentList.Add(AppContext.BaseDirectory);
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        Process.Start(start);
        return true;
    }

    /// <summary>
    /// Runs in the staged copy: waits for the old version to exit, copies the staged files over the install
    /// folder and starts the installed app. The old version is started again even if copying fails.
    /// </summary>
    public static void FinishUpdate(string installDir, int oldProcessId)
    {
        try
        {
            try { Process.GetProcessById(oldProcessId).WaitForExit(TimeSpan.FromSeconds(30)); }
            catch (ArgumentException) { } // already gone

            // The old process can hold file handles for a moment after it exits.
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    CopyOver(AppContext.BaseDirectory, installDir);
                    break;
                }
                catch (IOException) when (attempt < 10)
                {
                    Thread.Sleep(500);
                }
            }
        }
        finally
        {
            Process.Start(new ProcessStartInfo(Path.Combine(installDir, _exeName)) { UseShellExecute = false });
        }
    }

    /// <summary>Removes staging folders left by earlier updates; best effort, run by the installed app on start.</summary>
    public static void DeleteStagedUpdates()
    {
        string current = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        foreach (string dir in Directory.EnumerateDirectories(Path.GetTempPath(), _stagingPrefix + "*"))
        {
            if (string.Equals(dir, current, StringComparison.OrdinalIgnoreCase)) continue;
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Copies every file under <paramref name="sourceDir"/> into <paramref name="targetDir"/>, leaving other files there alone.</summary>
    internal static void CopyOver(string sourceDir, string targetDir)
    {
        foreach (string source in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(targetDir, Path.GetRelativePath(sourceDir, source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
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
