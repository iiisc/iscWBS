namespace iscWBS.Core.Models;

/// <summary>Describes a new application release available on GitHub.</summary>
public record UpdateInfo(
    string TagName,
    string ReleaseName,
    string ReleasePageUrl,
    string? DownloadUrl);
