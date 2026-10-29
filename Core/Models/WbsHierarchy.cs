namespace iscWBS.Core.Models;

/// <summary>Parent → children lookup over a flat node list, children ordered by <see cref="WbsNode.SortOrder"/>.</summary>
public sealed class WbsHierarchy
{
    private readonly Dictionary<Guid, List<WbsNode>> _children;
    private readonly List<WbsNode> _roots;

    public WbsHierarchy(IEnumerable<WbsNode> nodes)
    {
        _roots = nodes.Where(n => !n.ParentId.HasValue).OrderBy(n => n.SortOrder).ToList();
        _children = nodes
            .Where(n => n.ParentId.HasValue)
            .GroupBy(n => n.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(n => n.SortOrder).ToList());
    }

    public bool HasChildren(Guid id) => _children.ContainsKey(id);

    public IReadOnlyList<WbsNode> ChildrenOf(Guid id)
        => _children.TryGetValue(id, out List<WbsNode>? children) ? children : [];

    /// <summary>
    /// 0–1. A parent's progress is the share of complete nodes in its subtree; a leaf is 1 when complete,
    /// otherwise actual ÷ estimated hours (0 without an estimate).
    /// </summary>
    public double Progress(WbsNode node)
    {
        if (HasChildren(node.Id))
        {
            List<WbsNode> subtree = CollectSubtree(node);
            return (double)subtree.Count(n => n.Status == WbsStatus.Complete) / subtree.Count;
        }
        if (node.Status == WbsStatus.Complete) return 1.0;
        return node.EstimatedHours > 0 ? Math.Clamp(node.ActualHours / node.EstimatedHours, 0, 1) : 0.0;
    }

    /// <summary>Every node reachable from a root, in outline order, with its depth (roots are 0).</summary>
    public List<(WbsNode Node, int Depth)> DepthFirst()
    {
        var result = new List<(WbsNode Node, int Depth)>();
        void Walk(WbsNode node, int depth)
        {
            result.Add((node, depth));
            foreach (WbsNode child in ChildrenOf(node.Id))
                Walk(child, depth + 1);
        }
        foreach (WbsNode root in _roots)
            Walk(root, 0);
        return result;
    }

    /// <summary>
    /// The brand colour level, 1–6, of every node reachable from a root: roots are 1, each branch directly under a
    /// root takes the next of 2–6 in turn, and everything below a branch shares its level.
    /// </summary>
    public Dictionary<Guid, int> BranchLevels()
    {
        var levels = new Dictionary<Guid, int>();
        int branch = 0;
        foreach ((WbsNode node, int depth) in DepthFirst())
            levels[node.Id] = depth switch
            {
                0 => 1,
                1 => 2 + branch++ % 5,
                _ => levels[node.ParentId!.Value],
            };
        return levels;
    }

    /// <summary><paramref name="root"/> followed by all its descendants, depth-first.</summary>
    public List<WbsNode> CollectSubtree(WbsNode root)
    {
        var result = new List<WbsNode>();
        void Walk(WbsNode node)
        {
            result.Add(node);
            foreach (WbsNode child in ChildrenOf(node.Id))
                Walk(child);
        }
        Walk(root);
        return result;
    }
}
