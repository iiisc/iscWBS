using CommunityToolkit.Mvvm.ComponentModel;
using iscWBS.Core.Services;

namespace iscWBS.ViewModels;

public sealed partial class GanttViewModel : ObservableObject
{
    private readonly IProjectStateService _projectStateService;

    public GanttViewModel(IProjectStateService projectStateService)
    {
        _projectStateService = projectStateService;
    }
}
