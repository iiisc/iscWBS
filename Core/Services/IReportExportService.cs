using iscWBS.Core.Models;

namespace iscWBS.Core.Services;

/// <summary>Generates and saves a PDF project status report for the currently active project.</summary>
public interface IReportExportService
{
    /// <summary>
    /// Loads a snapshot of the active project's status data, composes a QuestPDF document
    /// using the provided <paramref name="options"/>, and writes it to <paramref name="filePath"/>.
    /// </summary>
    Task ExportAsync(string filePath, ReportOptions options);
}
