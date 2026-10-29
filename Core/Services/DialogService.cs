using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using iscWBS.Helpers;

namespace iscWBS.Core.Services;

/// <summary>Shows dialogs on behalf of view models, which have no <see cref="XamlRoot"/> of their own.</summary>
public sealed class DialogService
{
    private XamlRoot? _xamlRoot;

    /// <summary>Called once by <c>ShellWindow</c> when it has loaded.</summary>
    public void Initialize(XamlRoot xamlRoot) => _xamlRoot = xamlRoot;

    /// <summary>Logs <paramref name="ex"/> and shows its localized message under the title resource <paramref name="titleKey"/>.</summary>
    public Task ShowErrorAsync(string titleKey, Exception ex)
    {
        Logger.Write(ex);
        return ShowErrorAsync(Strings.Get(titleKey), DisplayText.ErrorMessage(ex));
    }

    public async Task ShowErrorAsync(string title, string message)
    {
        if (_xamlRoot is null) return;

        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = Strings.Get("Dialog_Ok"),
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
            PrimaryButtonText = Strings.Get("Dialog_Yes"),
            CloseButtonText = Strings.Get("Dialog_No"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = _xamlRoot
        };

        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }
}
