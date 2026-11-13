using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using GS.Alpaca.Shared;
using GS.Point3DWeb.Support;

namespace GS.Point3DWeb.Models;

/// <summary>
/// An Alpaca server connection. Mirrors GS.Alpaca.Client.Models.AlpacaServer.
/// </summary>
public partial class AlpacaServer(string url, string name) : ObservableObject
{
    [ObservableProperty]
    public partial string IdGuid { get; set; } = Guid.NewGuid().ToString();

    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    /// <summary>True while a connect is under way (from any view, the toolbar, or a drop); not saved.</summary>
    [ObservableProperty]
    public partial bool IsConnecting { get; set; }

    /// <summary>
    /// True once the user has connected this server from this app (Connect, or dragging it to a view), until
    /// they disconnect it or change its address; not saved, so every run starts disconnected.
    /// Alpaca's "connected" is the device's state, shared by every program using it, so the server can report
    /// true when this app never connected it (or has disconnected it); that is only taken as this app being
    /// connected while this is set.
    /// </summary>
    public volatile bool ConnectRequested;

    [ObservableProperty]
    public partial string ServerName { get; set; } = name;

    [ObservableProperty]
    public partial string Url { get; set; } = NormalizeUrl(url);

    private static readonly Regex AlpacaPath =
        new(@"/api/v1/telescope/(\d+)/?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The address prefix of an ASCOM COM driver on this computer: <c>ascom:&lt;ProgID&gt;</c>.</summary>
    public const string ComPrefix = "ascom:";

    /// <summary>The address of an ASCOM COM driver.</summary>
    public static string ComUrl(string progId) => ComPrefix + progId.Trim();

    /// <summary>Whether an address is an ASCOM COM driver (<c>ascom:&lt;ProgID&gt;</c>) rather than an Alpaca URL.</summary>
    public static bool IsComUrl(string url) => url.TrimStart().StartsWith(ComPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True if this is an ASCOM COM driver on the computer running this app (see
    /// <see cref="Services.ComTelescopes"/>), used through COM instead of over Alpaca.
    /// </summary>
    public bool IsCom => IsComUrl(Url);

    /// <summary>The COM driver's ProgID (e.g. ASCOM.Simulator.Telescope); null for an Alpaca server.</summary>
    public string? ProgId => IsCom ? Url.Trim()[ComPrefix.Length..].Trim() : null;

    /// <summary>
    /// Trims the address and writes its Alpaca path in lower case (…/api/v1/telescope/{n}/), as Alpaca
    /// servers expect; anything before it (host, a proxy prefix) is left as typed. The edit dialog accepts
    /// the path in any case, and requests no longer lower-case the whole address.
    /// A COM driver's address is written as <c>ascom:&lt;ProgID&gt;</c>, with the ProgID as typed.
    /// </summary>
    public static string NormalizeUrl(string url)
        => IsComUrl(url)
            ? ComUrl(url.Trim()[ComPrefix.Length..])
            : AlpacaPath.Replace(url.Trim(), m => $"/api/v1/telescope/{m.Groups[1].Value}/");

    /// <summary>
    /// Whether two addresses are the same device: the scheme and host may differ in case, the path may not
    /// (a proxy prefix can be case-sensitive). COM ProgIDs aren't case-sensitive.
    /// </summary>
    public static bool SameUrl(string a, string b)
        => IsComUrl(a) || IsComUrl(b)
            ? string.Equals(NormalizeUrl(a), NormalizeUrl(b), StringComparison.OrdinalIgnoreCase)
            : Uri.TryCreate(NormalizeUrl(a), UriKind.Absolute, out var ua) && Uri.TryCreate(NormalizeUrl(b), UriKind.Absolute, out var ub)
                ? Uri.Compare(ua, ub, UriComponents.AbsoluteUri, UriFormat.UriEscaped, StringComparison.Ordinal) == 0
                : string.Equals(NormalizeUrl(a), NormalizeUrl(b), StringComparison.Ordinal);

    /// <summary>
    /// Set at start-up when the saved password couldn't be decrypted (Password is then null); not saved.
    /// The encrypted text is kept in the file until the user enters or clears the password in the edit dialog.
    /// </summary>
    public bool PasswordUnreadable { get; set; }

    [ObservableProperty]
    public partial string? UserName { get; set; }

    [ObservableProperty]
    public partial string? Password { get; set; }

    /// <summary>
    /// The last Alpaca client transaction ID used. Changes on every request, so it is a plain property:
    /// a change notification would redraw everything watching the server several times a second.
    /// </summary>
    public double ClientTransactionId { get; set; }

    /// <summary>Telescope model shown for this server.</summary>
    [ObservableProperty]
    public partial Model3DType ModelType { get; set; } = Model3DType.Default;

    /// <summary>
    /// RA axis adjustment in degrees (-90 to 90), added to the alignment mode's built-in offset.
    /// </summary>
    [ObservableProperty]
    public partial double RaOffset { get; set; } = Model3D.DefaultRaOffset;

    /// <summary>
    /// Dec axis adjustment in degrees (-90 to 90), added to the alignment mode's built-in offset.
    /// </summary>
    [ObservableProperty]
    public partial double DecOffset { get; set; } = Model3D.DefaultDecOffset;

    /// <summary>OTA side for polar (fork) mounts; picks the PolarLeft / PolarRight model.</summary>
    [ObservableProperty]
    public partial PolarMode PolarMode { get; set; } = PolarMode.Left;

    /// <summary>Alignment mode reported by the mount (read on connect; remembered between runs).</summary>
    [ObservableProperty]
    public partial AlignmentModes AlignmentMode { get; set; } = AlignmentModes.algGermanPolar;

    /// <summary>Site latitude reported by the mount, in degrees (null until read).</summary>
    [ObservableProperty]
    public partial double? Latitude { get; set; }

    // Which coordinates show on the front of the 3D model card.
    [ObservableProperty] public partial bool ShowRa { get; set; } = true;
    [ObservableProperty] public partial bool ShowDec { get; set; } = true;
    [ObservableProperty] public partial bool ShowAz { get; set; } = true;
    [ObservableProperty] public partial bool ShowAlt { get; set; } = true;
    [ObservableProperty] public partial bool ShowLha { get; set; } = true;
    [ObservableProperty] public partial bool ShowLst { get; set; } = true;

    /// <summary>Show Ra on the front in degrees instead of hours.</summary>
    [ObservableProperty] public partial bool RaInDegrees { get; set; }

    /// <summary>Telescope color in the 3D view (#rrggbb).</summary>
    [ObservableProperty] public partial string ModelColor { get; set; } = ModelAppearance.DefaultColor;

    /// <summary>Background image key for the 3D view (see <see cref="ModelAppearance.Backgrounds"/>).</summary>
    [ObservableProperty] public partial string Background { get; set; } = ModelAppearance.DefaultBackground;

    /// <summary>Show the ground grid under the mount in the 3D view (on by default).</summary>
    [ObservableProperty] public partial bool ShowGrid { get; set; } = true;

    /// <summary>Saved 3D camera position for this server's model (null = the default view).</summary>
    [ObservableProperty] public partial CameraView? Camera { get; set; }

    public const double MinPollSeconds = 0.5;
    public const double MaxPollSeconds = 5.0;
    public const double DefaultPollSeconds = 0.5;

    /// <summary>How often the server is asked for its state, in seconds (0.5 to 5.0).</summary>
    [ObservableProperty]
    public partial double PollSeconds { get; set; } = DefaultPollSeconds;

    /// <summary>Rounds to 0.1 s and keeps the value within 0.5 to 5.0 seconds.</summary>
    public static double ClampPollSeconds(double seconds)
        => Math.Clamp(Math.Round(seconds, 1), MinPollSeconds, MaxPollSeconds);

    public void SetClientTransactionId(double value) => ClientTransactionId = value;
}
