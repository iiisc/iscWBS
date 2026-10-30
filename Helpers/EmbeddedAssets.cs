using Microsoft.UI.Xaml.Media.Imaging;

namespace iscWBS.Helpers;

/// <summary>Images and text the build embeds in the exe (the EmbeddedResource items in iscWBS.csproj), by file name.</summary>
public static class EmbeddedAssets
{
    private static Stream? Open(string name) => typeof(EmbeddedAssets).Assembly.GetManifestResourceStream(name);

    /// <summary>
    /// The image decoded at <paramref name="width"/> (in view pixels, so the display scale is applied), or null when
    /// there is no such image. Decoding at the shown size is what keeps the edges smooth: the decoder scales down
    /// properly, whereas XAML shrinking a large image, and its SVG renderer, both leave jagged edges.
    /// </summary>
    public static BitmapImage? Png(string name, double width)
    {
        if (Open(name) is not { } stream) return null;
        var image = new BitmapImage { DecodePixelType = DecodePixelType.Logical, DecodePixelWidth = (int)Math.Ceiling(width) };
        _ = image.SetSourceAsync(stream.AsRandomAccessStream());
        return image;
    }

    public static string Text(string name) => new StreamReader(Open(name)!).ReadToEnd();
}
