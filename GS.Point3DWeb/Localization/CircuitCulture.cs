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
/// The UI language of one browser connection (Blazor circuit), captured when the circuit starts.
/// Servers are shared by every connection, so a server change made by one browser's polling loop
/// raises events, and renders, in another browser's circuit on the first one's thread and culture.
/// Components re-apply this culture before rendering so each browser keeps its own language
/// (see <see cref="Components.LocalizedComponentBase"/>).
/// </summary>
public sealed class CircuitCulture
{
    public CultureInfo UICulture { get; } = CultureInfo.CurrentUICulture;

    /// <summary>Makes this circuit's language current for the rest of the calling flow.</summary>
    public void Apply()
    {
        if (!Equals(CultureInfo.CurrentUICulture, UICulture)) CultureInfo.CurrentUICulture = UICulture;
    }
}
