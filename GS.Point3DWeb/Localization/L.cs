using System.Globalization;

namespace GS.Point3DWeb.Localization;

/// <summary>
/// UI text lookup. The English text is the key: <c>L.T("Connect")</c>, or with placeholders
/// <c>L.T("Connect to {0}?", name)</c>. The language is the current UI culture, which the request
/// localization middleware sets for each page load and Blazor circuit. Text without a translation
/// shows in English.
/// </summary>
public static class L
{
    /// <summary>The text in the current UI language.</summary>
    public static string T(string english)
    {
        var table = Translations.For(Languages.Current.Code);
        return table is not null && table.TryGetValue(english, out var text) ? text : english;
    }

    /// <summary>The text in the current UI language with {0}, {1}... filled in.</summary>
    public static string T(string english, params object?[] args)
        => string.Format(CultureInfo.CurrentCulture, T(english), args);
}
