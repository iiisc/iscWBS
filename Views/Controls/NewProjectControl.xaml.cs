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

    private string _folderPath = App.Services.Settings.GetDefaultProjectFolder();

    public NewProjectControl()
    {
        InitializeComponent();
        TxtSavePath.Text = _folderPath;
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
            XamlRoot = xamlRoot
        };

        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary
            && !string.IsNullOrWhiteSpace(control.ProjectName)
            && !string.IsNullOrWhiteSpace(control._folderPath)
            ? control
            : null;
    }

    private async void BtnPickFolder_Click(object sender, RoutedEventArgs e)
    {
        if (await FilePickers.PickFolderAsync() is { } folder)
            TxtSavePath.Text = _folderPath = folder;
    }
}
