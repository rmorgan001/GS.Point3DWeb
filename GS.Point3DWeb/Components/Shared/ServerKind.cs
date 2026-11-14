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
