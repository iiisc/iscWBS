namespace iscWBS.Core.Exceptions;

/// <summary>Thrown when a requested WBS node does not exist in the database.</summary>
public sealed class WbsNotFoundException : WbsException
{
    public WbsNotFoundException(Guid id) : base($"WBS node '{id}' was not found.") { }
}
