using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace iscWBS.Views.Controls;

/// <summary>An illustration, a title, a line of text and an optional action, for a view with nothing to show.</summary>
public sealed partial class IscEmptyState : UserControl
{
    private string _illustration = string.Empty;

    public IscEmptyState() => InitializeComponent();

    /// <summary>File name, without extension, in Assets\Illustrations; the build embeds them in the exe. Optional.</summary>
    public string Illustration
    {
        get => _illustration;
        set
        {
            _illustration = value;
            IllustrationImage.Source = LoadIllustration(value);
            IllustrationImage.Visibility = IllustrationImage.Source is null ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public static BitmapImage? LoadIllustration(string name)
    {
        if (typeof(IscEmptyState).Assembly.GetManifestResourceStream($"{name}.png") is not { } stream)
            return null;
        var image = new BitmapImage();
        _ = image.SetSourceAsync(stream.AsRandomAccessStream());
        return image;
    }

    public string Title
    {
        get => TitleText.Text;
        set => TitleText.Text = value;
    }

    public string Body
    {
        get => BodyText.Text;
        set => BodyText.Text = value;
    }

    /// <summary>The action button's label; the button shows only when this is set.</summary>
    public string ActionText
    {
        get => ActionButton.Content as string ?? string.Empty;
        set
        {
            ActionButton.Content = value;
            ActionButton.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public ICommand? Command
    {
        get => ActionButton.Command;
        set => ActionButton.Command = value;
    }
}
