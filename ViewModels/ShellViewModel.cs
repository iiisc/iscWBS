using CommunityToolkit.Mvvm.ComponentModel;
using iscWBS.Core.Models;
using iscWBS.Core.Services;

namespace iscWBS.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    private readonly ProjectStateService _projectStateService;
    private readonly NavigationService _navigationService;

    [ObservableProperty]
    public partial bool HasActiveProject { get; set; }

    [ObservableProperty]
    public partial string WindowTitle { get; set; } = "iscWBS";

    public ShellViewModel(ProjectStateService projectStateService, NavigationService navigationService)
    {
        _projectStateService = projectStateService;
        _navigationService = navigationService;

        _projectStateService.ActiveProjectChanged += OnActiveProjectChanged;
        HasActiveProject = _projectStateService.HasActiveProject;
    }

    private void OnActiveProjectChanged(object? sender, Project? project)
    {
        HasActiveProject = project is not null;
        WindowTitle = project is not null ? $"iscWBS \u2014 {project.Name}" : "iscWBS";

        if (project is not null)
            _navigationService.NavigateTo("DashboardPage");
    }
}
