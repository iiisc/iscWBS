using Microsoft.UI.Xaml.Controls;

namespace iscWBS.Core.Services;

/// <summary>Wraps the root <see cref="Frame"/> in <c>ShellWindow</c> for ViewModel-driven navigation.</summary>
public interface INavigationService
{
    /// <summary>Whether the frame has pages it can navigate back to.</summary>
    bool CanGoBack { get; }

    /// <summary>
    /// Binds the service to the shell's content <see cref="Frame"/>.
    /// Called once by <c>ShellWindow.Loaded</c> — never by ViewModels.
    /// </summary>
    void Initialize(Frame frame);

    /// <summary>
    /// Registers a string key mapping to a page <see cref="Type"/>.
    /// Called from <c>App.xaml.cs OnLaunched</c> after the DI container is built.
    /// </summary>
    void RegisterPage(string pageKey, Type pageType);

    /// <summary>
    /// Navigates to a page by compile-time type.
    /// Intended for the View layer where the type is known statically.
    /// </summary>
    void NavigateTo<TPage>(object? parameter = null);

    /// <summary>
    /// Navigates to a page by registered string key.
    /// Intended for ViewModels — avoids WinUI type references.
    /// </summary>
    void NavigateTo(string pageKey, object? parameter = null);

    /// <summary>Navigates back if possible.</summary>
    void GoBack();
}

/// <summary>Implemented by page ViewModels that need navigation lifecycle callbacks.</summary>
public interface INavigationAware
{
    void OnNavigatedTo(object? parameter);
    void OnNavigatedFrom();
}
