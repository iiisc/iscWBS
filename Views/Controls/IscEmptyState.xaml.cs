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

    /// <summary>File name, without extension, in Assets\Illustrations.</summary>
    public string Illustration
    {
        get => _illustration;
        set
        {
            _illustration = value;
            IllustrationImage.Source = new BitmapImage(new Uri($"ms-appx:///Assets/Illustrations/{value}.png"));
        }
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
