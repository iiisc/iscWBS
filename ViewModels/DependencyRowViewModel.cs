using iscWBS.Core.Models;

namespace iscWBS.ViewModels;

/// <summary>Display data for a single predecessor dependency row.</summary>
public sealed class DependencyRowViewModel
{
    public NodeDependency Dependency { get; init; } = null!;
    public string PredecessorCode { get; init; } = string.Empty;
    public string PredecessorTitle { get; init; } = string.Empty;
    public string TypeLabel { get; init; } = string.Empty;
    public string TypeDescription { get; init; } = string.Empty;
    /// <summary>True when this predecessor is actively preventing the successor from proceeding.</summary>
    public bool IsBlocking { get; init; }
    /// <summary>Human-readable explanation of why this predecessor is blocking, empty when not blocking.</summary>
    public string BlockingReason { get; init; } = string.Empty;
}

/// <summary>Display option for a <see cref="DependencyType"/> value in a picker.</summary>
public sealed record DependencyTypeOption(DependencyType Type, string Label, string Description);
