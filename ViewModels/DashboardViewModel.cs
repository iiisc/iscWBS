using CommunityToolkit.Mvvm.ComponentModel;
using iscWBS.Core.Services;

namespace iscWBS.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly IProjectStateService _projectStateService;

    public DashboardViewModel(IProjectStateService projectStateService)
    {
        _projectStateService = projectStateService;
    }
}
