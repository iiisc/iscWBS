namespace iscWBS.Core.Exceptions;

/// <summary>Thrown when a project file cannot be found or opened.</summary>
public sealed class ProjectNotFoundException : WbsException
{
    public ProjectNotFoundException(string filePath)
        : base($"Project file '{filePath}' was not found or could not be opened.") { }
}
