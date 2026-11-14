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
