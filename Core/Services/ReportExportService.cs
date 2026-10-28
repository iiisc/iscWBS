using QuestPDF.Fluent;
using iscWBS.Core.Models;
using iscWBS.Helpers;

namespace iscWBS.Core.Services;

public sealed class ReportExportService
{
    private readonly ProjectStateService _projectStateService;
    private readonly WbsService _wbsService;
    private readonly MilestoneService _milestoneService;

    public ReportExportService(
        ProjectStateService projectStateService,
        WbsService wbsService,
        MilestoneService milestoneService)
    {
        _projectStateService = projectStateService;
        _wbsService = wbsService;
        _milestoneService = milestoneService;
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
        IReadOnlyList<Milestone> milestones =
            await _milestoneService.GetUpcomingAsync(projectId);

        var data = new ProjectStatusReportData(
            projectName,
            DateTime.Now,
            ProjectSummary.Compute(nodes, blockedIds),
            DeliverableRow.Build(nodes, blockedIds),
            milestones.Select(m => (m.Title, m.DueDate)).ToList(),
            BuildWbsTree(nodes, blockedIds));

        var document = new ProjectStatusReportDocument(data, options);
        await Task.Run(() => document.GeneratePdf(filePath));
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
