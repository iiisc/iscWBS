using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using iscWBS.Core.Models;

namespace iscWBS.Core.Services;

/// <summary>Checks GitHub Releases for a newer version and installs its ZIP over this one.</summary>
public sealed partial class UpdateService
{
    private const string _gitHubApiUrl = "https://api.github.com/repos/iiisc/iscWBS/releases/latest";

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

        return new UpdateInfo(release.TagName, release.Name, release.HtmlUrl, FindZipAsset(release.Assets));
    }

    /// <summary>
    /// Downloads and extracts the release, then starts a script that copies it over the install folder once
    /// this process exits. The caller must exit straight afterwards. Without a ZIP asset, opens the release page.
    /// </summary>
    public async Task DownloadAndInstallAsync(UpdateInfo info, IProgress<int> progress, CancellationToken cancellationToken = default)
    {
        if (info.DownloadUrl is null)
        {
            OpenReleasePage(info);
            return;
        }

        string zipPath = Path.Combine(Path.GetTempPath(), $"iscWBS-update-{info.TagName}.zip");
        string extractDir = Path.Combine(Path.GetTempPath(), $"iscWBS-update-{info.TagName}");

        using HttpResponseMessage response = await _httpClient.GetAsync(
            info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
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

        fileStream.Close();
        cancellationToken.ThrowIfCancellationRequested();

        if (Directory.Exists(extractDir))
            Directory.Delete(extractDir, recursive: true);

        ZipFile.ExtractToDirectory(zipPath, extractDir);

        // The files can't be replaced while this process runs, so a script waits for it to exit, copies them and relaunches.
        string processPath = Environment.ProcessPath ?? string.Empty;
        string installDir  = Path.GetDirectoryName(processPath) ?? string.Empty;
        string scriptPath  = Path.Combine(Path.GetTempPath(), $"iscWBS-updater-{info.TagName}.ps1");

        string script = $$"""
            $targetPid = {{Environment.ProcessId}}
            $src = '{{EscapePs(extractDir)}}'
            $dst = '{{EscapePs(installDir)}}'
            $exe = '{{EscapePs(processPath)}}'
            $zip = '{{EscapePs(zipPath)}}'

            while (Get-Process -Id $targetPid -ErrorAction SilentlyContinue) {
                Start-Sleep -Milliseconds 300
            }

            Copy-Item -Path "$src\*" -Destination $dst -Recurse -Force
            Start-Process -FilePath $exe
            Remove-Item -Path $src -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -Path $zip -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue
            """;

        await File.WriteAllTextAsync(scriptPath, script, cancellationToken);

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-ExecutionPolicy Bypass -NonInteractive -WindowStyle Hidden -File \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    /// <summary>Escapes a path for use inside a PowerShell single-quoted string.</summary>
    private static string EscapePs(string path) => path.Replace("'", "''");

    public void OpenReleasePage(UpdateInfo info)
        => Process.Start(new ProcessStartInfo(info.ReleasePageUrl) { UseShellExecute = true });

    /// <summary>The x64 ZIP if there is one, otherwise any ZIP.</summary>
    private static string? FindZipAsset(List<GitHubAsset> assets)
        => assets
            .Where(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.Name.Contains("x64", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault()
            ?.BrowserDownloadUrl;

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
    }
}
