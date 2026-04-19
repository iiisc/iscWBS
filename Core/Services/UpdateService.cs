using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using iscWBS.Core.Models;

namespace iscWBS.Core.Services;

/// <inheritdoc cref="IUpdateService"/>
public sealed class UpdateService : IUpdateService
{
    private const string _gitHubApiUrl = "https://api.github.com/repos/iiisc/iscWBS/releases/latest";

    /// <inheritdoc/>
    public string RepositoryUrl { get; } = "https://github.com/iiisc/iscWBS";

    private readonly HttpClient _httpClient;

    public UpdateService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "iscWBS-UpdateChecker");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        GitHubRelease? release = await _httpClient.GetFromJsonAsync<GitHubRelease>(_gitHubApiUrl);

        if (release is null)
            return null;

        string tag = release.TagName.TrimStart('v');
        if (!Version.TryParse(tag, out Version? latestVersion))
            return null;

        if (!Version.TryParse(CurrentVersion, out Version? currentVersion))
            return null;

        if (latestVersion <= currentVersion)
            return null;

        string? exeUrl = FindZipAsset(release.Assets);

        return new UpdateInfo(release.TagName, release.Name, release.HtmlUrl, exeUrl);
    }

    /// <inheritdoc/>
    public async Task DownloadAndInstallAsync(UpdateInfo info, IProgress<int> progress, CancellationToken cancellationToken = default)
    {
        if (info.DownloadUrl is null)
        {
            await OpenReleasePageAsync(info);
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

        // Write a PowerShell script that waits for this process to exit, copies the
        // extracted files over the install directory, relaunches the exe, then cleans up.
        // The caller (SettingsViewModel) calls Environment.Exit(0) after this returns.
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

    /// <inheritdoc/>
    public Task OpenReleasePageAsync(UpdateInfo info)
    {
        Process.Start(new ProcessStartInfo(info.ReleasePageUrl) { UseShellExecute = true });
        return Task.CompletedTask;
    }

    private static string? FindZipAsset(List<GitHubAsset> assets)
    {
        // Prefer the x64 ZIP; fall back to any ZIP if no arch-specific one is found.
        return assets
            .FirstOrDefault(a =>
                a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                a.Name.Contains("x64", StringComparison.OrdinalIgnoreCase))
            ?.BrowserDownloadUrl
            ?? assets
                .FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                ?.BrowserDownloadUrl;
    }

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
