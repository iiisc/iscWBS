using CommunityToolkit.Mvvm.ComponentModel;
using iscWBS.Core.Services;

namespace iscWBS.ViewModels;

public sealed partial class ReportsViewModel : ObservableObject
{
    private readonly IProjectStateService _projectStateService;

    public ReportsViewModel(IProjectStateService projectStateService)
    {
        _projectStateService = projectStateService;
    }
}
