using Microsoft.UI.Xaml.Media.Imaging;

namespace iscWBS.Helpers;

/// <summary>Images and text the build embeds in the exe (the EmbeddedResource items in iscWBS.csproj), by file name.</summary>
public static class EmbeddedAssets
{
    private static Stream? Open(string name) => typeof(EmbeddedAssets).Assembly.GetManifestResourceStream(name);

    /// <summary>Null when there is no such image.</summary>
    public static BitmapImage? Png(string name)
    {
        if (Open(name) is not { } stream) return null;
        var image = new BitmapImage();
        _ = image.SetSourceAsync(stream.AsRandomAccessStream());
        return image;
    }

    public static SvgImageSource Svg(string name)
    {
        var image = new SvgImageSource();
        _ = image.SetSourceAsync(Open(name)!.AsRandomAccessStream());
        return image;
    }

    public static string Text(string name) => new StreamReader(Open(name)!).ReadToEnd();
}
