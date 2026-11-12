namespace GS.Point3DWeb.Localization;

/// <summary>
/// Translation tables, one file per language (Translations.de.cs, ...). Each maps the English UI text
/// (as passed to <see cref="L.T(string)"/>) to that language. English needs no table.
/// When you add or change UI text, add the same key to every table; missing keys show in English.
/// </summary>
internal static partial class Translations
{
    /// <summary>The table for a language code from <see cref="Languages.All"/>, or null for English.</summary>
    public static IReadOnlyDictionary<string, string>? For(string code) => code switch
    {
        "de" => German,
        "fr" => French,
        "it" => Italian,
        "es" => Spanish,
        "zh-Hans" => ChineseSimplified,
        _ => null
    };
}
