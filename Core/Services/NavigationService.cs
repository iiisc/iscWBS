using Microsoft.UI.Xaml.Controls;

namespace iscWBS.Core.Services;

public sealed class NavigationService
{
    private Frame? _frame;
    private readonly Dictionary<string, Type> _pageRegistry = new();

    public void Initialize(Frame frame) => _frame = frame;

    public object? CurrentPage => _frame?.Content;

    public void RegisterPage(string pageKey, Type pageType) => _pageRegistry[pageKey] = pageType;

    public void NavigateTo(string pageKey, object? parameter = null)
    {
        if (_pageRegistry.TryGetValue(pageKey, out Type? pageType))
            _frame?.Navigate(pageType, parameter);
    }
}
