namespace iscWBS.Core.Exceptions;

/// <summary>
/// Tags an exception with a resource key so the UI can show it in the user's language while
/// <see cref="Exception.Message"/> stays English for logs. Uses <see cref="Exception.Data"/> so any exception type can carry it.
/// </summary>
public static class LocalizedMessage
{
    private const string KeyEntry  = "iscWBS.MessageKey";
    private const string ArgsEntry = "iscWBS.MessageArgs";

    public static TException WithMessageKey<TException>(this TException exception, string key, params object[] args)
        where TException : Exception
    {
        exception.Data[KeyEntry]  = key;
        exception.Data[ArgsEntry] = args;
        return exception;
    }

    public static bool TryGetMessageKey(this Exception exception, out string key, out object[] args)
    {
        key  = exception.Data[KeyEntry] as string ?? string.Empty;
        args = exception.Data[ArgsEntry] as object[] ?? [];
        return key.Length > 0;
    }
}
