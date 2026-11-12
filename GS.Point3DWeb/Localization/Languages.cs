using System.Globalization;

namespace GS.Point3DWeb.Localization;

/// <summary>A language the UI can be shown in.</summary>
/// <param name="Code">Culture name (also the cookie value and the html lang attribute).</param>
/// <param name="NativeName">The language's name in that language, for the dropdown.</param>
public sealed record Language(string Code, string NativeName);

/// <summary>
/// The UI languages. The choice is kept per browser in ASP.NET Core's culture cookie
/// (see Program.cs); without one the browser's own language is used if it is in this list.
/// </summary>
public static class Languages
{
    public const string DefaultCode = "en";

    public static readonly IReadOnlyList<Language> All =
    [
        new("en", "English"),
        new("de", "Deutsch"),
        new("fr", "Français"),
        new("it", "Italiano"),
        new("es", "Español"),
        new("zh-Hans", "中文（简体）")
    ];

    /// <summary>The language for the current request / circuit.</summary>
    public static Language Current => Find(CultureInfo.CurrentUICulture) ?? All[0];

    /// <summary>The supported language for a culture name ("de-DE" → German, "zh-CN" → Chinese), or null.</summary>
    public static Language? Find(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName)) return null;
        try
        {
            return Find(CultureInfo.GetCultureInfo(cultureName));
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Walks the culture and its parents (de-DE → de, zh-CN → zh-Hans → zh) to a supported language.</summary>
    public static Language? Find(CultureInfo culture)
    {
        for (var c = culture; !string.IsNullOrEmpty(c.Name); c = c.Parent)
        {
            var match = All.FirstOrDefault(l => string.Equals(l.Code, c.Name, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }
        return null;
    }
}
