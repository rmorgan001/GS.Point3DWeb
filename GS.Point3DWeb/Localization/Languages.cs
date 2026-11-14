/* Copyright(C) 2026  Rob Morgan (robert.morgan.e@gmail.com)

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published
    by the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using System.Globalization;

namespace GS.Point3DWeb.Localization;

/// <summary>A language the UI can be shown in.</summary>
/// <param name="Code">Culture name (also the cookie value and the html lang attribute).</param>
/// <param name="NativeName">The language's name in that language, for the language menu.</param>
public sealed record Language(string Code, string NativeName)
{
    /// <summary>Two-letter code shown on the language menu button: EN, DE, ... ZH.</summary>
    public string ShortCode => Code.Split('-')[0].ToUpperInvariant();
}

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
        new("zh-Hans", "中文")
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
