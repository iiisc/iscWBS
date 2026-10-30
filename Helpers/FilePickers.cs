using Microsoft.UI;
using Microsoft.Windows.Storage.Pickers;

namespace iscWBS.Helpers;

/// <summary>
/// File pickers shared by several pages. These are the Windows App SDK's own pickers: with the older
/// Windows.Storage.Pickers, double-clicking a file in the dialog crashed the app.
/// </summary>
public static class FilePickers
{
    // Pickers only open from the UI, so the window exists by then.
    private static WindowId Owner => App.MainWindow!.AppWindow.Id;

    public static async Task<string?> PickProjectFileAsync()
    {
        var picker = new FileOpenPicker(Owner);
        picker.FileTypeFilter.Add(".iscwbs");
        return (await picker.PickSingleFileAsync())?.Path;
    }

    public static async Task<string?> PickStatusReportSaveFileAsync()
    {
        var picker = new FileSavePicker(Owner)
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = Strings.Format("Picker_StatusReportFileName", DateTime.Today.ToString("yyyy-MM-dd")),
        };
        picker.FileTypeChoices.Add(Strings.Get("Picker_PdfDocument"), new List<string> { ".pdf" });
        return (await picker.PickSaveFileAsync())?.Path;
    }

    public static async Task<string?> PickWorkbookSaveFileAsync()
    {
        var picker = new FileSavePicker(Owner)
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = App.Services.ProjectState.ActiveProject?.Name ?? "iscWBS",
        };
        picker.FileTypeChoices.Add(Strings.Get("Picker_ExcelWorkbook"), new List<string> { ".xlsx" });
        return (await picker.PickSaveFileAsync())?.Path;
    }

    /// <summary>A project to copy the plan of, or a workbook exported from iscWBS, for a new project to start from.</summary>
    public static async Task<string?> PickProjectSourceAsync()
    {
        var picker = new FileOpenPicker(Owner);
        picker.FileTypeFilter.Add(".iscwbs");
        picker.FileTypeFilter.Add(".xlsx");
        return (await picker.PickSingleFileAsync())?.Path;
    }

    public static async Task<string?> PickFolderAsync()
        => (await new FolderPicker(Owner).PickSingleFolderAsync())?.Path;
}
