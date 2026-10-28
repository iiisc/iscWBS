namespace iscWBS.Core.Exceptions;

/// <summary>An expected failure whose message is fit to show the user.</summary>
public sealed class WbsException : Exception
{
    public WbsException(string message) : base(message) { }

    public static WbsException NodeNotFound(Guid id)
        => new WbsException($"WBS node '{id}' was not found.").WithMessageKey("Error_NodeNotFound", id);

    public static WbsException ProjectNotFound(string filePath)
        => new WbsException($"Project file '{filePath}' was not found or could not be opened.")
            .WithMessageKey("Error_ProjectNotFound", filePath);

    public static WbsException NoProjectOpen()
        => new WbsException("No project is currently open.").WithMessageKey("Error_NoProjectOpen");
}
