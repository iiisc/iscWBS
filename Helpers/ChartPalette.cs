using SkiaSharp;

namespace iscWBS.Helpers;

/// <summary>Centralised SKiaSharp colour definitions for chart series, keyed by WBS status.</summary>
public static class ChartPalette
{
    /// <summary>Opaque colours — for pie series and line strokes.</summary>
    public static readonly SKColor NotStarted = new(0x80, 0x80, 0x80);
    public static readonly SKColor InProgress  = new(0x00, 0x78, 0xD4);
    public static readonly SKColor Complete    = new(0x10, 0x7C, 0x10);
    public static readonly SKColor Blocked     = new(0xC5, 0x0F, 0x1F);

    /// <summary>Semi-transparent variants (alpha 0xCC) — for bar and column series.</summary>
    public static readonly SKColor NotStartedAlpha = new(0x80, 0x80, 0x80, 0xCC);
    public static readonly SKColor InProgressAlpha  = new(0x00, 0x78, 0xD4, 0xCC);
    public static readonly SKColor CompleteAlpha    = new(0x10, 0x7C, 0x10, 0xCC);
    public static readonly SKColor BlockedAlpha     = new(0xC5, 0x0F, 0x1F, 0xCC);
}
