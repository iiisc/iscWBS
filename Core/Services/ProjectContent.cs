using Microsoft.Data.Sqlite;
using iscWBS.Core.Data;
using iscWBS.Core.Models;

namespace iscWBS.Core.Services;

/// <summary>Everything a project holds besides its own name and details: what export writes, and what a new project can start from.</summary>
public sealed class ProjectContent
{
    public List<WbsNode> Nodes { get; init; } = [];
    public List<NodeDependency> Dependencies { get; init; } = [];
    public List<Milestone> Milestones { get; init; } = [];
    public List<(Guid MilestoneId, Guid NodeId)> MilestoneLinks { get; init; } = [];
    public List<NodeNote> Notes { get; init; } = [];
    public List<Risk> Risks { get; init; } = [];

    public static async Task<ProjectContent> ReadAsync(SqliteConnection db, Guid projectId) => new()
    {
        Nodes = await WbsService.ReadNodesAsync(db, projectId),
        Dependencies = await WbsService.ReadDependenciesAsync(db, projectId),
        Milestones = await MilestoneService.ReadByProjectAsync(db, projectId),
        MilestoneLinks = await db.QueryAsync(
            "SELECT l.MilestoneId, l.NodeId FROM MilestoneNodeLinks l INNER JOIN Milestones m ON l.MilestoneId = m.Id " +
            "WHERE m.ProjectId = $1 ORDER BY l.Id",
            r => (r.GetGuidText(0), r.GetGuidText(1)), projectId),
        Notes = await db.QueryAsync(
            "SELECT nn.Id, nn.NodeId, nn.CreatedAt, nn.Text FROM NodeNotes nn INNER JOIN WbsNodes n ON nn.NodeId = n.Id " +
            "WHERE n.ProjectId = $1 ORDER BY nn.CreatedAt, nn.Id",
            WbsService.ReadNote, projectId),
        Risks = await RiskService.ReadByProjectAsync(db, projectId),
    };

    /// <summary>What a new project starts from: another project's plan as a template, or a workbook exported from iscWBS.</summary>
    /// <param name="label">The app's text for a resource key, for the workbook's localized headings and values.</param>
    public static async Task<ProjectContent> FromFileAsync(string path, Func<string, string> label)
        => Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? await Task.Run(() => ProjectWorkbook.Read(path, label))
            : (await ProjectStateService.ReadContentAsync(path)).AsTemplate(DateTime.Today);

    /// <summary>
    /// The plan as a fresh start: nothing started or complete, no baseline or notes, only the open risks, and every
    /// date moved by the same amount so the plan begins on <paramref name="startOn"/>.
    /// </summary>
    public ProjectContent AsTemplate(DateTime startOn)
    {
        List<DateTime> dates = [.. Nodes.SelectMany(n => new[] { n.StartDate, n.DueDate }).OfType<DateTime>(), .. Milestones.Select(m => m.DueDate)];
        TimeSpan shift = dates.Count == 0 ? TimeSpan.Zero : startOn.Date - dates.Min().Date;
        return new ProjectContent
        {
            Nodes = Nodes.Select(n =>
            {
                WbsNode copy = n.CopyAsNotStarted();
                copy.Id = n.Id;
                copy.StartDate += shift;
                copy.DueDate += shift;
                return copy;
            }).ToList(),
            Dependencies = Dependencies,
            Milestones = Milestones.Select(m => new Milestone { Id = m.Id, Title = m.Title, DueDate = m.DueDate + shift }).ToList(),
            MilestoneLinks = MilestoneLinks,
            Risks = Risks.Where(r => !r.IsClosed).ToList(),
        };
    }

    /// <summary>Writes the content into the empty project <paramref name="projectId"/>, numbering the nodes from their order.</summary>
    internal async Task WriteAsync(SqliteConnection db, Guid projectId)
    {
        WbsService.Renumber(Nodes);
        foreach (WbsNode node in Nodes)
        {
            node.ProjectId = projectId;
            await WbsService.InsertAsync(db, node);
        }
        foreach (NodeDependency dependency in Dependencies)
            await WbsService.InsertDependencyAsync(db, dependency);
        foreach (Milestone milestone in Milestones)
        {
            milestone.ProjectId = projectId;
            await MilestoneService.InsertAsync(db, milestone);
        }
        foreach ((Guid milestoneId, Guid nodeId) in MilestoneLinks)
            await MilestoneService.InsertLinkAsync(db, milestoneId, nodeId);
        foreach (NodeNote note in Notes)
            await WbsService.InsertNoteAsync(db, note);
        foreach (Risk risk in Risks)
        {
            risk.ProjectId = projectId;
            await RiskService.InsertAsync(db, risk);
        }
    }
}
