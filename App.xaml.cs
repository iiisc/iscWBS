using System.Globalization;
using Microsoft.UI.Xaml;
using QuestPDF.Infrastructure;
using iscWBS.Core.Services;
using iscWBS.Helpers;
using iscWBS.Views;

namespace iscWBS;

public partial class App : Application
{
    public static AppServices Services { get; private set; } = null!;
    public static Window? MainWindow { get; private set; }

    private readonly SettingsService _settingsService = new();

    public App()
    {
        // Must precede any XAML or resource load so x:Uid and ResourceLoader resolve in the saved language.
        // Without a saved choice, Windows' language preferences pick between the shipped en-US and sv-SE resources.
        if (_settingsService.Get<string>(SettingsKeys.Language) is { } language)
        {
            Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language;
            // Month names and number formats follow the chosen language too, not Windows' regional format.
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo(language);
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentCulture;
        }

        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        UpdateService.DeleteOldVersion();

        QuestPDF.Settings.License = LicenseType.Community;
        // QuestPDF 2026.9 stopped using system fonts by default, and the report is set in Segoe UI,
        // which every Windows install has. Without this, every export fails.
        QuestPDF.Settings.UseSystemFonts = true;
        // QuestPDF keeps its Lato fonts as a fallback after Segoe UI. They aren't shipped, and their absence shouldn't fail the export.
        QuestPDF.Settings.ThrowOnMissingFontFamilies = false;

        Services = new AppServices(_settingsService);

        UnhandledException += OnUnhandledException;

        NavigationService navService = Services.Navigation;
        navService.RegisterPage("WelcomePage", typeof(WelcomePage));
        navService.RegisterPage("DashboardPage", typeof(DashboardPage));
        navService.RegisterPage("WbsTreePage", typeof(WbsTreePage));
        navService.RegisterPage("WbsOutlinePage", typeof(WbsOutlinePage));
        navService.RegisterPage("GanttPage", typeof(GanttPage));
        navService.RegisterPage("ReportsPage", typeof(ReportsPage));
        navService.RegisterPage("MilestonesPage", typeof(MilestonesPage));
        navService.RegisterPage("RisksPage", typeof(RisksPage));
        navService.RegisterPage("SettingsPage", typeof(SettingsPage));

        MainWindow = new ShellWindow();
        MainWindow.Activate();
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        if (System.Diagnostics.Debugger.IsAttached)
        {
            Logger.Write(e.Exception);
            System.Diagnostics.Debugger.Break();
            return;
        }
        // Keep running, but tell the user instead of failing silently; the dialog call also logs it.
        e.Handled = true;
        _ = Services.Dialog.ShowErrorAsync("ErrorTitle_Generic", e.Exception);
    }
}

