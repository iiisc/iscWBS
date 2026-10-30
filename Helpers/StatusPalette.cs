using iscWBS.Core.Models;

namespace iscWBS.Helpers;

/// <summary>
/// Status colours for the PDF report: the app's brand hues, darkened until status text reads on white paper.
/// The app itself uses the theme-aware <c>WbsStatus*Color</c> resources.
/// </summary>
public static class StatusPalette
{
    public static string Hex(WbsStatus status) => status switch
    {
        WbsStatus.InProgress => "#1873B5",
        WbsStatus.Complete   => "#3D7A12",
        WbsStatus.Blocked    => "#C8264F",
        _                    => "#6B60A8",
    };
}
