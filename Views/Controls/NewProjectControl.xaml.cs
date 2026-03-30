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
    public string Currency => CbxCurrency.SelectedValue as string ?? "USD";
    public string FolderPath { get; private set; } = string.Empty;

    public NewProjectControl()
    {
        InitializeComponent();

        CbxCurrency.Items.Add("USD");
        CbxCurrency.Items.Add("EUR");
        CbxCurrency.Items.Add("GBP");
        CbxCurrency.Items.Add("AUD");
        CbxCurrency.Items.Add("CAD");
        CbxCurrency.SelectedIndex = 0;
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
