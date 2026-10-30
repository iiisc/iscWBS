using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using iscWBS.Core.Services;
using iscWBS.Helpers;

namespace iscWBS.Views.Controls;

public sealed partial class NewProjectControl : UserControl
{
    public string ProjectName => TxtName.Text.Trim();
    public string Owner => TxtOwner.Text.Trim();
    public string FilePath => Path.Combine(_folderPath, $"{ProjectName}.iscwbs");

    /// <summary>The project or workbook to start from, or null for an empty project.</summary>
    public string? SourcePath { get; private set; }

    // The name becomes the file name, so a / or : would put the file somewhere else.
    private bool HasValidName => ProjectName.Length > 0 && ProjectName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private string _folderPath = App.Services.Settings.GetDefaultProjectFolder();

    public NewProjectControl()
    {
        InitializeComponent();
        TxtSavePath.Text = _folderPath;
        SetSource(null);
    }

    /// <summary>Shows the New Project dialog; returns the filled-in control, or null when cancelled or incomplete.</summary>
    public static async Task<NewProjectControl?> ShowAsync(XamlRoot xamlRoot)
    {
        var control = new NewProjectControl();
        var dialog = new ContentDialog
        {
            Title = Strings.Get("NewProject_DialogTitle"),
            Content = control,
            PrimaryButtonText = Strings.Get("NewProject_Create"),
            CloseButtonText = Strings.Get("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
            XamlRoot = xamlRoot
        };
        control.TxtName.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = control.HasValidName;

        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary
            && control.HasValidName
            && !string.IsNullOrWhiteSpace(control._folderPath)
            ? control
            : null;
    }

    private async void BtnPickSource_Click(object sender, RoutedEventArgs e)
    {
        if (await FilePickers.PickProjectSourceAsync() is not { } path)
            return;
        SetSource(path);
        // A workbook's name is usually the project's. A copied project's isn't: in the same folder it would clash.
        if (TxtName.Text.Length == 0 && Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            TxtName.Text = Path.GetFileNameWithoutExtension(path);
    }

    private void BtnClearSource_Click(object sender, RoutedEventArgs e) => SetSource(null);

    private void SetSource(string? path)
    {
        SourcePath = path;
        TxtSource.Text = path is null ? Strings.Get("NewProject_EmptyProject") : Path.GetFileName(path);
        BtnClearSource.Visibility = path is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void BtnPickFolder_Click(object sender, RoutedEventArgs e)
    {
        if (await FilePickers.PickFolderAsync() is { } folder)
            TxtSavePath.Text = _folderPath = folder;
    }
}
