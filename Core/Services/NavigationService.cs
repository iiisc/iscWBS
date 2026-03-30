using Microsoft.UI.Xaml.Controls;

namespace iscWBS.Core.Services;

public sealed class NavigationService : INavigationService
{
    private Frame? _frame;
    private readonly Dictionary<string, Type> _pageRegistry = new();

    public bool CanGoBack => _frame?.CanGoBack ?? false;

    public void Initialize(Frame frame) => _frame = frame;

    public void RegisterPage(string pageKey, Type pageType) => _pageRegistry[pageKey] = pageType;

    public void NavigateTo<TPage>(object? parameter = null) => _frame?.Navigate(typeof(TPage), parameter);

    public void NavigateTo(string pageKey, object? parameter = null)
    {
        if (_pageRegistry.TryGetValue(pageKey, out Type? pageType))
            _frame?.Navigate(pageType, parameter);
    }

    public void GoBack()
    {
        if (CanGoBack)
            _frame?.GoBack();
    }
}
