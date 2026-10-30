using iscWBS.Core.Exceptions;
using iscWBS.Core.Models;

namespace iscWBS.Helpers;

/// <summary>Localized text for Core enums and exceptions, which stay language-neutral.</summary>
public static class DisplayText
{
    public static string ToDisplayString(this WbsStatus status) => Strings.Get($"Status_{status}");

    public static string ToDisplayString(this RiskLevel level) => Strings.Get($"RiskLevel_{level}");

    /// <summary>Long label, e.g. "Finish → Start".</summary>
    public static string GetLabel(DependencyType type) => Strings.Get($"DependencyLabel_{type}");

    public static string GetDescription(DependencyType type) => Strings.Get($"DependencyDescription_{type}");

    /// <summary>Why the constraint is blocking, or <see cref="string.Empty"/> when it is satisfied.</summary>
    public static string GetBlockingReason(DependencyType type, WbsStatus predecessorStatus)
        => DependencyConstraints.IsViolated(type, predecessorStatus)
            ? Strings.Format($"DependencyBlocking_{type}", predecessorStatus.ToDisplayString())
            : string.Empty;

    /// <summary>The message to show the user for <paramref name="exception"/>.</summary>
    public static string ErrorMessage(Exception exception)
        => exception.TryGetMessageKey(out string key, out object[] args)
            ? Strings.Format(key, args)
            : exception.Message;
}
