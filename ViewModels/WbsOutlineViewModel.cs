using CommunityToolkit.Mvvm.ComponentModel;
using iscWBS.Core.Services;

namespace iscWBS.ViewModels;

public sealed partial class WbsOutlineViewModel : ObservableObject
{
    private readonly IProjectStateService _projectStateService;
    private readonly IWbsService _wbsService;

    public WbsOutlineViewModel(IProjectStateService projectStateService, IWbsService wbsService)
    {
        _projectStateService = projectStateService;
        _wbsService = wbsService;
    }
}
