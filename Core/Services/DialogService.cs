using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace iscWBS.Core.Services;

public sealed class DialogService : IDialogService
{
    private XamlRoot? _xamlRoot;

    public void Initialize(XamlRoot xamlRoot) => _xamlRoot = xamlRoot;

    public async Task ShowErrorAsync(string title, string message)
    {
        if (_xamlRoot is null) return;

        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = _xamlRoot
        };

        await dialog.ShowAsync();
    }

    public async Task<bool> ShowConfirmAsync(string title, string message)
    {
        if (_xamlRoot is null) return false;

        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = "Yes",
            CloseButtonText = "No",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = _xamlRoot
        };

        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    public async Task ShowContentAsync<TControl>(string title) where TControl : new()
    {
        if (_xamlRoot is null) return;

        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TControl(),
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = _xamlRoot
        };

        await dialog.ShowAsync();
    }
}
