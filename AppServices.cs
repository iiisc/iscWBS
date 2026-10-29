using iscWBS.Core.Services;
using iscWBS.ViewModels;

namespace iscWBS;

/// <summary>The app's single instance of each service, plus a fresh view model for each page visit.</summary>
public sealed class AppServices
{
    public SettingsService Settings { get; }
    public ProjectStateService ProjectState { get; }
    public NavigationService Navigation { get; } = new();
    public DialogService Dialog { get; } = new();
    public WbsService Wbs { get; }
    public MilestoneService Milestones { get; }
    public ReportExportService ReportExport { get; }
    public LocalizationService Localization { get; }
    public UpdateService Update { get; } = new();

    public AppServices(SettingsService settings)
    {
        Settings = settings;
        ProjectState = new ProjectStateService(settings);
        Wbs = new WbsService(ProjectState);
        Milestones = new MilestoneService(ProjectState);
        ReportExport = new ReportExportService(ProjectState, Wbs, Milestones);
        Localization = new LocalizationService(settings);
    }

    public ShellViewModel CreateShellViewModel() => new(ProjectState, Navigation);
    public WelcomeViewModel CreateWelcomeViewModel() => new(ProjectState, Settings, Dialog);
    public DashboardViewModel CreateDashboardViewModel() => new(ProjectState, Wbs, Milestones, Dialog, ReportExport);
    public WbsTreeViewModel CreateWbsTreeViewModel() => new(Wbs, ProjectState, Dialog, Milestones);
    public WbsOutlineViewModel CreateWbsOutlineViewModel() => new(Wbs, ProjectState, Dialog, Navigation);
    public GanttViewModel CreateGanttViewModel() => new(ProjectState, Wbs, Dialog, Milestones);
    public ReportsViewModel CreateReportsViewModel() => new(ReportExport, Dialog, Settings);
    public MilestonesViewModel CreateMilestonesViewModel() => new(Milestones, Wbs, ProjectState, Dialog);
    public SettingsViewModel CreateSettingsViewModel() => new(Settings, Update, Dialog, ProjectState, Navigation, Localization);
}
