namespace iscWBS.Core.Exceptions;

/// <summary>Base exception for all iscWBS domain errors.</summary>
public class WbsException : Exception
{
    public WbsException(string message) : base(message) { }
    public WbsException(string message, Exception innerException) : base(message, innerException) { }
}
