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
