using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using QuestPDF.Infrastructure;
using iscWBS.Core.Repositories;
using iscWBS.Core.Services;
using iscWBS.Helpers;
using iscWBS.ViewModels;
using iscWBS.Views;

namespace iscWBS;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static Window? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    /// <summary>Returns <see langword="true"/> when the process has MSIX package identity.</summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int length, char[]? name);

    private static bool IsPackaged()
    {
        int length = 0;
        const int AppmodelErrorNoPackage = 15700;
        return GetCurrentPackageFullName(ref length, null) != AppmodelErrorNoPackage;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        // Apply saved language preference before any UI resources are loaded.
        // A temporary SettingsService is used here because DI has not been built yet.
        // PrimaryLanguageOverride requires package identity; skip it in unpackaged runs
        // (e.g. direct Debug launches). The preference is persisted and takes effect in
        // packaged builds.
        SettingsService earlySettings = new();
        if (IsPackaged())
        {
            Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride =
                earlySettings.Get<string>(SettingsKeys.Language) ?? "en-US";
        }

        ServiceCollection services = new();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        UnhandledException += OnUnhandledException;

        INavigationService navService = Services.GetRequiredService<INavigationService>();
        navService.RegisterPage("WelcomePage", typeof(WelcomePage));
        navService.RegisterPage("DashboardPage", typeof(DashboardPage));
        navService.RegisterPage("WbsTreePage", typeof(WbsTreePage));
        navService.RegisterPage("WbsOutlinePage", typeof(WbsOutlinePage));
        navService.RegisterPage("GanttPage", typeof(GanttPage));
        navService.RegisterPage("ReportsPage", typeof(ReportsPage));
        navService.RegisterPage("MilestonesPage", typeof(MilestonesPage));
        navService.RegisterPage("SettingsPage", typeof(SettingsPage));

        MainWindow = new ShellWindow();
        MainWindow.Activate();
    }

    private static void ConfigureServices(ServiceCollection services)
    {
        services.AddSingleton<IProjectStateService, ProjectStateService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<WbsNodeRepository>();
        services.AddSingleton<MilestoneRepository>();
        services.AddSingleton<MilestoneNodeLinkRepository>();
        services.AddSingleton<NodeDependencyRepository>();
        services.AddSingleton<IWbsService, WbsService>();
        services.AddSingleton<IMilestoneService, MilestoneService>();
        services.AddSingleton<IGanttLayoutService, GanttLayoutService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IReportExportService, ReportExportService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();

        services.AddTransient<ShellViewModel>();
        services.AddTransient<WelcomeViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<WbsTreeViewModel>();
        services.AddTransient<WbsOutlineViewModel>();
        services.AddTransient<GanttViewModel>();
        services.AddTransient<ReportsViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<MilestonesViewModel>();
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Logger.Write(e.Exception);
        if (System.Diagnostics.Debugger.IsAttached)
            System.Diagnostics.Debugger.Break();
        e.Handled = !System.Diagnostics.Debugger.IsAttached;
    }
}

