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

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        QuestPDF.Settings.License = LicenseType.Community;

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
        services.AddSingleton<IWbsService, WbsService>();

        services.AddTransient<ShellViewModel>();
        services.AddTransient<WelcomeViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<WbsTreeViewModel>();
        services.AddTransient<WbsOutlineViewModel>();
        services.AddTransient<GanttViewModel>();
        services.AddTransient<ReportsViewModel>();
        services.AddTransient<SettingsViewModel>();
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Logger.Write(e.Exception);
        e.Handled = true;
    }
}

