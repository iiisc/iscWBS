using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace iscWBS.Helpers;

/// <summary>File pickers shared by several pages; each must be bound to the main window handle.</summary>
public static class FilePickers
{
    public static async Task<StorageFile?> PickProjectFileAsync()
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        picker.FileTypeFilter.Add(".iscwbs");
        return await picker.PickSingleFileAsync();
    }

    public static async Task<StorageFile?> PickStatusReportSaveFileAsync()
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.SuggestedFileName = Strings.Format("Picker_StatusReportFileName", DateTime.Today.ToString("yyyy-MM-dd"));
        picker.FileTypeChoices.Add(Strings.Get("Picker_PdfDocument"), new List<string> { ".pdf" });
        return await picker.PickSaveFileAsync();
    }
}
