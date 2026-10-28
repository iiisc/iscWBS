using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;

namespace iscWBS.Helpers;

/// <summary>
/// Looks up strings in <c>Strings/*/Resources.resw</c>. Uses the MRT Core loader because the
/// <c>Windows.ApplicationModel.Resources</c> one throws without package identity.
/// </summary>
public static class Strings
{
    // Created on first use, which is after App applies the language override.
    private static readonly ResourceLoader _loader = new();

    /// <summary>Returns the string for <paramref name="key"/>, or the key itself when it is missing.</summary>
    public static string Get(string key)
    {
        try
        {
            string value = _loader.GetString(key);
            return string.IsNullOrEmpty(value) ? key : value;
        }
        catch (Exception)
        {
            return key;
        }
    }

    public static string Format(string key, params object?[] args)
        => string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
