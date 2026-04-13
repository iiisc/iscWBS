using QuestPDF.Fluent;
using iscWBS.Core.Models;
using iscWBS.Helpers;

namespace iscWBS.Core.Services;

public sealed class ReportExportService : IReportExportService
{
    private readonly IProjectStateService _projectStateService;
    private readonly IWbsService _wbsService;
    private readonly IMilestoneService _milestoneService;

    public ReportExportService(
        IProjectStateService projectStateService,
        IWbsService wbsService,
        IMilestoneService milestoneService)
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
            _wbsService.ResolveBlockedNodeIds(nodes, dependencies);
        IReadOnlyList<Milestone> milestones =
            await _milestoneService.GetUpcomingAsync(projectId, 60);

        ProjectSummary summary = _wbsService.ComputeProjectSummary(nodes, blockedIds);

        int atRisk = nodes.Count(n =>
            n.Status != WbsStatus.Complete &&
            n.DueDate.HasValue &&
            n.DueDate.Value.Date >= DateTime.Today &&
            n.DueDate.Value.Date <= DateTime.Today.AddDays(14));

        IReadOnlyList<DeliverableRow> deliverables = BuildDeliverableRows(nodes, blockedIds);
        IReadOnlyList<WbsTreeEntry> wbsTree = BuildWbsTree(nodes, blockedIds);

        var data = new ProjectStatusReportData(
            projectName,
            DateTime.Now,
            summary.PercentComplete,
            summary.TotalNodes,
            summary.CompleteCount,
            summary.InProgressCount,
            atRisk,
            summary.OverdueCount,
            summary.BlockedCount,
            deliverables,
            milestones.Select(m => (m.Title, m.DueDate)).ToList(),
            wbsTree);

        var document = new ProjectStatusReportDocument(data, options);
        await Task.Run(() => document.GeneratePdf(filePath));
    }

    private static IReadOnlyList<DeliverableRow> BuildDeliverableRows(
        IReadOnlyList<WbsNode> allNodes,
        IReadOnlySet<Guid> blockedIds)
    {
        Dictionary<Guid, List<WbsNode>> childMap = allNodes
            .Where(n => n.ParentId.HasValue)
            .GroupBy(n => n.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(n => n.SortOrder).ToList());

        IEnumerable<WbsNode> deliverableNodes = allNodes
            .Where(n => n.IsDeliverable)
            .OrderBy(n => n.SortOrder);

        var rows = new List<DeliverableRow>();
        foreach (WbsNode root in deliverableNodes)
        {
            List<WbsNode> subtree = CollectSubtree(root, childMap);
            int total    = subtree.Count;
            int complete = subtree.Count(n => n.Status == WbsStatus.Complete);
            double fraction = total > 0 ? (double)complete / total : 0;

            List<DateTime> dueDates = subtree
                .Where(n => n.DueDate.HasValue)
                .Select(n => n.DueDate!.Value)
                .ToList();
            DateTime? latestDue = dueDates.Count > 0 ? dueDates.Max() : null;

            bool isOverdue = latestDue.HasValue &&
                latestDue.Value.Date < DateTime.Today &&
                root.Status != WbsStatus.Complete;
            bool isAtRisk = latestDue.HasValue &&
                latestDue.Value.Date >= DateTime.Today &&
                latestDue.Value.Date <= DateTime.Today.AddDays(14) &&
                root.Status != WbsStatus.Complete;

            WbsStatus effectiveStatus = blockedIds.Contains(root.Id)
                ? WbsStatus.Blocked
                : root.Status;

            rows.Add(new DeliverableRow(
                root.Code, root.Title, total, complete, fraction,
                effectiveStatus, latestDue, isOverdue, isAtRisk));
        }
        return rows;
    }

    private static List<WbsNode> CollectSubtree(WbsNode root, Dictionary<Guid, List<WbsNode>> childMap)
    {
        var result = new List<WbsNode> { root };
        if (childMap.TryGetValue(root.Id, out List<WbsNode>? children))
            foreach (WbsNode child in children)
                result.AddRange(CollectSubtree(child, childMap));
        return result;
    }

    /// <summary>
    /// Produces a flat DFS-ordered list of every WBS node enriched with depth and progress data,
    /// suitable for rendering the hierarchical outline table in the PDF.
    /// </summary>
    private static IReadOnlyList<WbsTreeEntry> BuildWbsTree(
        IReadOnlyList<WbsNode> allNodes,
        IReadOnlySet<Guid> blockedIds)
    {
        Dictionary<Guid, List<WbsNode>> childMap = allNodes
            .Where(n => n.ParentId.HasValue)
            .GroupBy(n => n.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(n => n.SortOrder).ToList());

        IEnumerable<WbsNode> roots = allNodes
            .Where(n => !n.ParentId.HasValue)
            .OrderBy(n => n.SortOrder);

        var entries = new List<WbsTreeEntry>();

        void Walk(WbsNode node, int depth)
        {
            bool isParent = childMap.ContainsKey(node.Id);
            WbsStatus effectiveStatus = blockedIds.Contains(node.Id)
                ? WbsStatus.Blocked
                : node.Status;

            double progress;
            if (isParent)
            {
                List<WbsNode> subtree = CollectSubtree(node, childMap);
                int total    = subtree.Count;
                int complete = subtree.Count(n => n.Status == WbsStatus.Complete);
                progress = total > 0 ? (double)complete / total : 0;
            }
            else
            {
                progress = node.EstimatedHours > 0
                    ? Math.Clamp(node.ActualHours / node.EstimatedHours, 0, 1)
                    : node.Status == WbsStatus.Complete ? 1.0 : 0.0;
            }

            entries.Add(new WbsTreeEntry(
                node.Code,
                node.Title,
                depth,
                isParent,
                effectiveStatus,
                progress,
                node.DueDate,
                node.AssignedTo,
                node.EstimatedHours,
                node.ActualHours));

            if (childMap.TryGetValue(node.Id, out List<WbsNode>? children))
                foreach (WbsNode child in children)
                    Walk(child, depth + 1);
        }

        foreach (WbsNode root in roots)
            Walk(root, 0);

        return entries;
    }
}
