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
    /// Downloads the MSIX asset from <paramref name="info"/> to a temporary file and launches the
    /// Windows App Installer. Falls back to opening the release page in the browser when no MSIX
    /// asset is attached to the release.
    /// </summary>
    Task DownloadAndInstallAsync(UpdateInfo info, IProgress<int> progress, CancellationToken cancellationToken = default);

    /// <summary>Opens the release page for <paramref name="info"/> in the default browser.</summary>
    Task OpenReleasePageAsync(UpdateInfo info);
}
