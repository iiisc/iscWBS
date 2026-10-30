namespace iscWBS.ViewModels;

/// <summary>Represents a selectable language option in the settings language picker.</summary>
public sealed record LanguageOption(string Code, string DisplayName)
{
    public override string ToString() => DisplayName;
}
