using iscWBS.Core.Models;

namespace iscWBS.Core.Services;

/// <summary>Checks for and installs application updates from GitHub Releases.</summary>
public interface IUpdateService
{
    /// <summary>Gets the current application version string (e.g. "1.2.3").</summary>
    string CurrentVersion { get; }

    /// <summary>Gets the URL of the GitHub repository used as the update source.</summary>
    string RepositoryUrl { get; }

    /// <summary>
    /// Queries GitHub for the latest release. Returns <c>null</c> when the running version is
    /// already the latest.
    /// </summary>
    Task<UpdateInfo?> CheckForUpdateAsync();

    /// <summary>
    /// Downloads the ZIP asset from <paramref name="info"/>, extracts it to a temporary folder,
    /// launches a background PowerShell script that will copy the files into the install directory
    /// once the application exits, and then returns. The caller is responsible for calling
    /// <see cref="Environment.Exit"/> immediately after this method returns.
    /// Falls back to opening the release page in the browser when no ZIP asset is attached.
    /// </summary>
    Task DownloadAndInstallAsync(UpdateInfo info, IProgress<int> progress, CancellationToken cancellationToken = default);

    /// <summary>Opens the release page for <paramref name="info"/> in the default browser.</summary>
    Task OpenReleasePageAsync(UpdateInfo info);
}
