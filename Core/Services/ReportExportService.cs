using QuestPDF.Fluent;
using iscWBS.Core.Models;
using iscWBS.Helpers;

namespace iscWBS.Core.Services;

public sealed class ReportExportService
{
    /// <summary>How far back the report's notes go.</summary>
    public const int RecentNoteDays = 30;

    private readonly ProjectStateService _projectStateService;
    private readonly WbsService _wbsService;
    private readonly MilestoneService _milestoneService;
    private readonly RiskService _riskService;

    public ReportExportService(
        ProjectStateService projectStateService,
        WbsService wbsService,
        MilestoneService milestoneService,
        RiskService riskService)
    {
        _projectStateService = projectStateService;
        _wbsService = wbsService;
        _milestoneService = milestoneService;
        _riskService = riskService;
    }

    public async Task ExportAsync(string filePath, ReportOptions options)
    {
        if (_projectStateService.ActiveProject is not { Id: var projectId, Name: var projectName })
            return;

        IReadOnlyList<WbsNode> nodes =
            await _wbsService.GetAllByProjectAsync(projectId);
        IReadOnlyList<NodeDependency> dependencies =
            await _wbsService.GetAllDependenciesByProjectAsync(projectId);
        IReadOnlySet<Guid> blockedIds =
            DependencyConstraints.BlockedNodeIds(nodes, dependencies);
        List<Milestone> milestones =
            [.. (await _milestoneService.GetByProjectAsync(projectId)).Where(m => m.IsDueSoon(DateTime.Today))];
        var notes = await _wbsService.GetNotesSinceAsync(projectId, DateTime.Today.AddDays(-RecentNoteDays));
        IReadOnlyList<Risk> risks = await _riskService.GetByProjectAsync(projectId);

        var data = new ProjectStatusReportData(
            projectName,
            DateTime.Now,
            ProjectSummary.Compute(nodes, blockedIds),
            ProgressRow.Build(nodes, blockedIds, n => n.IsDeliverable),
            milestones.Select(m => (m.Title, m.DueDate)).ToList(),
            BuildWbsTree(nodes, blockedIds),
            notes.Select(n => (n.Note.CreatedAt, $"{n.Code} {n.Title}", n.Note.Text)).ToList(),
            risks.Where(r => !r.IsClosed).ToList());

        var document = new ProjectStatusReportDocument(data, options);
        await Task.Run(() => document.GeneratePdf(filePath));
    }

    /// <summary>The whole project as an Excel workbook, which New Project can also start from.</summary>
    public async Task ExportWorkbookAsync(string filePath)
    {
        if (_projectStateService.ActiveProject is not { Id: var projectId })
            return;
        ProjectContent content = await ProjectContent.ReadAsync(_projectStateService.Connection, projectId);
        await Task.Run(() => ProjectWorkbook.Write(content, filePath, Strings.Get));
    }

    private static IReadOnlyList<WbsTreeEntry> BuildWbsTree(
        IReadOnlyList<WbsNode> allNodes,
        IReadOnlySet<Guid> blockedIds)
    {
        WbsHierarchy hierarchy = new(allNodes);
        return hierarchy.DepthFirst()
            .Select(e => new WbsTreeEntry(
                e.Node.Code,
                e.Node.Title,
                e.Depth,
                hierarchy.HasChildren(e.Node.Id),
                DependencyConstraints.EffectiveStatus(e.Node, blockedIds),
                hierarchy.Progress(e.Node),
                e.Node.DueDate,
                e.Node.AssignedTo,
                e.Node.EstimatedHours,
                e.Node.ActualHours))
            .ToList();
    }
}
