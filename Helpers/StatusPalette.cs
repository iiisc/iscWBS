using iscWBS.Core.Models;

namespace iscWBS.Helpers;

/// <summary>
/// Fixed status colours for the Dashboard chart and badges, the Gantt bars (which draw white text on them)
/// and the PDF report.
/// The WBS diagram and detail panel use the theme-aware <c>WbsStatus*Brush</c> resources instead.
/// </summary>
public static class StatusPalette
{
    private static (byte R, byte G, byte B) Rgb(WbsStatus status) => status switch
    {
        WbsStatus.InProgress => (0x00, 0x78, 0xD4),
        WbsStatus.Complete   => (0x10, 0x7C, 0x10),
        WbsStatus.Blocked    => (0xC5, 0x0F, 0x1F),
        _                    => (0x80, 0x80, 0x80),
    };

    public static Windows.UI.Color Color(WbsStatus status)
    {
        (byte r, byte g, byte b) = Rgb(status);
        return Windows.UI.Color.FromArgb(0xFF, r, g, b);
    }

    public static string Hex(WbsStatus status)
    {
        (byte r, byte g, byte b) = Rgb(status);
        return $"#{r:X2}{g:X2}{b:X2}";
    }
}
