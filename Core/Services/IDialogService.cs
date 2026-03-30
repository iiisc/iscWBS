using Microsoft.UI.Xaml;

namespace iscWBS.Core.Services;

/// <summary>Provides dialog functionality without coupling ViewModels to WinUI <c>ContentDialog</c>.</summary>
public interface IDialogService
{
    /// <summary>
    /// Binds the service to the shell's <see cref="XamlRoot"/>.
    /// Called once by <c>ShellWindow.Loaded</c> — never by ViewModels.
    /// </summary>
    void Initialize(XamlRoot xamlRoot);

    /// <summary>Shows a modal error dialog.</summary>
    Task ShowErrorAsync(string title, string message);

    /// <summary>Shows a confirmation dialog. Returns <see langword="true"/> if the user confirms.</summary>
    Task<bool> ShowConfirmAsync(string title, string message);

    /// <summary>Shows a <c>ContentDialog</c> hosting a new instance of <typeparamref name="TControl"/>.</summary>
    Task ShowContentAsync<TControl>(string title) where TControl : new();
}
