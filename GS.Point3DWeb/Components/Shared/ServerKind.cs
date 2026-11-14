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

using GS.Point3DWeb.Localization;
using GS.Point3DWeb.Models;

namespace GS.Point3DWeb.Components.Shared;

/// <summary>
/// How a server's kind shows: the Alpaca logo for an Alpaca server, the ASCOM logo for a COM driver on this
/// computer. Used by the server list and by each view's header, so the two always match.
/// </summary>
public static class ServerKind
{
    /// <summary>The logo's address.</summary>
    public static string Image(AlpacaServer server) => server.IsCom ? "img/com.svg" : "img/alpaca.png";

    /// <summary>What the logo stands for, in the UI language (its alt text and tooltip).</summary>
    public static string Text(AlpacaServer server) => server.IsCom ? L.T("ASCOM driver") : L.T("Alpaca server");
}
