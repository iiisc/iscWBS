namespace iscWBS.Core.Models;

/// <summary>
/// One person's share of the work. Only tasks without children count, so a parent's hours aren't added on top of
/// its children's.
/// </summary>
/// <param name="Person">Empty for work nobody is assigned to.</param>
/// <param name="RemainingHours">Estimated hours of the work not yet complete, less what has already been spent on it.</param>
public sealed record WorkloadRow(string Person, int OpenTasks, double RemainingHours)
{
    /// <summary>
    /// A row per person with open work among <paramref name="nodes"/>, the most remaining first and unassigned
    /// work last. <paramref name="allNodes"/> tells which nodes have children.
    /// </summary>
    public static List<WorkloadRow> Build(IReadOnlyList<WbsNode> allNodes, IEnumerable<WbsNode> nodes)
    {
        var hierarchy = new WbsHierarchy(allNodes);
        return nodes
            .Where(n => !hierarchy.HasChildren(n.Id) && n.Status != WbsStatus.Complete)
            .GroupBy(n => n.AssignedTo.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new WorkloadRow(g.First().AssignedTo.Trim(), g.Count(), g.Sum(n => Math.Max(0, n.EstimatedHours - n.ActualHours))))
            .OrderBy(r => r.Person.Length == 0)
            .ThenByDescending(r => r.RemainingHours)
            .ThenBy(r => r.Person, StringComparer.CurrentCulture)
            .ToList();
    }
}
