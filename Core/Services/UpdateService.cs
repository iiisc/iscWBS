using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Windows.ApplicationModel;
using Windows.Storage;
using Windows.System;
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
            PackageVersion v = Package.Current.Id.Version;
            return $"{v.Major}.{v.Minor}.{v.Build}";
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

        PackageVersion pv = Package.Current.Id.Version;
        Version currentVersion = new(pv.Major, pv.Minor, pv.Build);
        if (latestVersion <= currentVersion)
            return null;

        string? msixUrl = FindMsixAsset(release.Assets);

        return new UpdateInfo(release.TagName, release.Name, release.HtmlUrl, msixUrl);
    }

    /// <inheritdoc/>
    public async Task DownloadAndInstallAsync(UpdateInfo info, IProgress<int> progress, CancellationToken cancellationToken = default)
    {
        if (info.MsixDownloadUrl is null)
        {
            await OpenReleasePageAsync(info);
            return;
        }

        string tempPath = Path.Combine(Path.GetTempPath(), $"iscWBS-update-{info.TagName}.msix");

        using HttpResponseMessage response = await _httpClient.GetAsync(
            info.MsixDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using FileStream fileStream = File.Create(tempPath);

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
        StorageFile file = await StorageFile.GetFileFromPathAsync(tempPath);
        await Launcher.LaunchFileAsync(file);
    }

    /// <inheritdoc/>
    public async Task OpenReleasePageAsync(UpdateInfo info)
        => await Launcher.LaunchUriAsync(new Uri(info.ReleasePageUrl));

    private static string? FindMsixAsset(List<GitHubAsset> assets)
    {
        // Prefer the asset whose name contains the current process architecture
        // so x64 machines download the x64 MSIX and ARM64 machines download the ARM64 one.
        string archTag = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64   => "_x64",
            Architecture.Arm64 => "_ARM64",
            Architecture.X86   => "_x86",
            _                  => string.Empty
        };

        string? url = null;
        if (archTag.Length > 0)
        {
            url = assets
                .FirstOrDefault(a =>
                    a.Name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase) &&
                    a.Name.Contains(archTag, StringComparison.OrdinalIgnoreCase))
                ?.BrowserDownloadUrl;
        }

        // Fall back to any .msix asset when no architecture-specific one is found.
        return url ?? assets
            .FirstOrDefault(a => a.Name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase))
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
