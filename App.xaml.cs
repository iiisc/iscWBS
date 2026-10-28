using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using QuestPDF.Infrastructure;
using iscWBS.Core.Services;
using iscWBS.Helpers;
using iscWBS.ViewModels;
using iscWBS.Views;

namespace iscWBS;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static Window? MainWindow { get; private set; }

    private readonly SettingsService _settingsService = new();

    public App()
    {
        // Must precede any XAML or resource load so x:Uid and ResourceLoader resolve in the saved language.
        // Without a saved choice, Windows' language preferences pick between the shipped en-US and sv-SE resources.
        if (_settingsService.Get<string>(SettingsKeys.Language) is { } language)
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language;

        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        ServiceCollection services = new();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        UnhandledException += OnUnhandledException;

        NavigationService navService = Services.GetRequiredService<NavigationService>();
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

    private void ConfigureServices(ServiceCollection services)
    {
        services.AddSingleton(_settingsService);
        services.AddSingleton<ProjectStateService>();
        services.AddSingleton<NavigationService>();
        services.AddSingleton<DialogService>();
        services.AddSingleton<WbsService>();
        services.AddSingleton<MilestoneService>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<ReportExportService>();
        services.AddSingleton<LocalizationService>();

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

