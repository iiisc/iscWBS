using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace iscWBS.Views.Controls;

public sealed partial class NewProjectControl : UserControl
{
    public string ProjectName => TxtName.Text.Trim();
    public string Owner => TxtOwner.Text.Trim();
    public string FolderPath { get; private set; } = string.Empty;

    /// <summary>
    /// Sets both the displayed path label and the <see cref="FolderPath"/> backing value.
    /// Call this before the dialog is shown to pre-populate a sensible default.
    /// </summary>
    public string DefaultFolderPath
    {
        set
        {
            FolderPath = value;
            TxtSavePath.Text = value;
        }
    }

    public NewProjectControl()
    {
        InitializeComponent();

        string defaultFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "iscWBS Projects");
        FolderPath = defaultFolder;
        TxtSavePath.Text = defaultFolder;
    }

    private async void BtnPickFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        picker.FileTypeFilter.Add("*");

        StorageFolder? folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            FolderPath = folder.Path;
            TxtSavePath.Text = folder.Path;
        }
    }
}
