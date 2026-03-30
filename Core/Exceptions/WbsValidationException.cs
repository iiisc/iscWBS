namespace iscWBS.Core.Exceptions;

/// <summary>Thrown when a WBS operation violates a business rule.</summary>
public sealed class WbsValidationException : WbsException
{
    public WbsValidationException(string message) : base(message) { }
}
