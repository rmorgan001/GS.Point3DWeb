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
