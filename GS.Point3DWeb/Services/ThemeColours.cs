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
using System.Text.Json;
using GS.Point3DWeb.Localization;

namespace GS.Point3DWeb.Services;

/// <summary>
/// The user's own colour theme: seven colours (the rest of the palette is worked out from them in app.css), kept by the
/// browser. The same palette format as GS Test Harness, so a palette made in one can be pasted into the other. This
/// app uses five of the seven (error and info are carried along so a palette stays whole). Holds the built-in palettes
/// to start from, the contrast and red-light checks, and the text a palette is exported and imported as.
/// </summary>
public static class ThemeColours
{
    /// <summary>Every colour of a palette, in the order the text export has them.</summary>
    public static readonly IReadOnlyList<string> Keys = ["bg", "text", "accent", "warn", "error", "fail", "info"];

    /// <summary>The colours this app has pickers for (the others are kept but nothing here uses them).</summary>
    public static readonly IReadOnlyList<string> EditorKeys = ["bg", "text", "accent", "warn", "fail"];

    /// <summary>The name of a colour in the current language.</summary>
    public static string Label(string key) => key switch
    {
        "bg" => L.T("Background"),
        "text" => L.T("Text"),
        "accent" => L.T("Accent"),
        "warn" => L.T("Warning"),
        "error" => L.T("Error"),
        "fail" => L.T("Fail"),
        _ => L.T("Info")
    };

    /// <summary>The built-in themes to start from.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Presets = new Dictionary<string, IReadOnlyDictionary<string, string>>
    {
        ["dark"] = Palette("#070b0e", "#e8e6df", "#52b788", "#f2c84b", "#f08a3c", "#ef6461", "#6aa8d8"),
        ["day"] = Palette("#f4f7f8", "#14202a", "#1f7a52", "#8a6100", "#a84a00", "#b3261e", "#0b6aa8"),
        ["dusk"] = Palette("#0a0500", "#ffb066", "#e0a050", "#ffb066", "#ff8a3d", "#ff6a3d", "#d9a060"),
        ["night"] = Palette("#000000", "#ff2020", "#ff2020", "#ff2020", "#ff2020", "#ff2020", "#ff2020")
    };

    private static IReadOnlyDictionary<string, string> Palette(string bg, string text, string accent, string warn, string error, string fail, string info)
        => new Dictionary<string, string> { ["bg"] = bg, ["text"] = text, ["accent"] = accent, ["warn"] = warn, ["error"] = error, ["fail"] = fail, ["info"] = info };

    private static readonly System.Buffers.SearchValues<char> HexDigits = System.Buffers.SearchValues.Create("0123456789abcdefABCDEF");

    /// <summary>"#rrggbb" (any case) as its three channels; null if it isn't one.</summary>
    public static (int R, int G, int B)? Parse(string? hex)
    {
        if (hex is not { Length: 7 } || hex[0] != '#') return null;
        // Checked as hex digits first: HexNumber alone also lets spaces through ("# 1 2 3").
        if (hex.AsSpan(1).IndexOfAnyExcept(HexDigits) >= 0) return null;
        return int.TryParse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
               && int.TryParse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
               && int.TryParse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)
            ? (r, g, b) : null;
    }

    private static double Luminance((int R, int G, int B) c)
    {
        static double Linear(int v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
    }

    /// <summary>The WCAG contrast ratio of two colours (1 to 21); 0 if either isn't a colour. 4.5 or more reads well as body text.</summary>
    public static double Contrast(string? a, string? b)
    {
        if (Parse(a) is not { } x || Parse(b) is not { } y) return 0;
        var (hi, lo) = (Math.Max(Luminance(x), Luminance(y)), Math.Min(Luminance(x), Luminance(y)));
        return (hi + 0.05) / (lo + 0.05);
    }

    /// <summary>Whether the colours that make up text and marks on the screen have almost no green or blue (so they keep night vision).</summary>
    public static bool RedLightSafe(IReadOnlyDictionary<string, string> colours)
        => new[] { "text", "accent", "warn", "fail" }
            .All(k => colours.TryGetValue(k, out var hex) && Parse(hex) is { } c && c.G <= 0x40 && c.B <= 0x40);

    /// <summary>Whether the background is light (the browser's own controls then want a light colour scheme).</summary>
    public static bool IsLight(string? bg) => Parse(bg) is { } c && Luminance(c) > 0.4;

    /// <summary>The palette as text to keep or paste into another browser (or another project): one line of JSON.</summary>
    public static string Export(IReadOnlyDictionary<string, string> colours)
    {
        var ordered = new Dictionary<string, object> { ["gsTheme"] = 1 };
        foreach (var key in Keys) ordered[key] = colours.TryGetValue(key, out var v) ? v.ToLowerInvariant() : "";
        return JsonSerializer.Serialize(ordered);
    }

    /// <summary>Reads text made by <see cref="Export"/>; on failure says what is wrong (in the current language).</summary>
    public static bool TryImport(string? text, out Dictionary<string, string> colours, out string? error)
    {
        colours = [];
        error = null;
        if (string.IsNullOrWhiteSpace(text)) { error = L.T("Paste a palette first."); return false; }
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(text);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            error = L.T("That isn't a palette: it should be the text Export gave.");
            return false;
        }
        if (root.ValueKind != JsonValueKind.Object) { error = L.T("That isn't a palette: it should be the text Export gave."); return false; }
        foreach (var key in Keys)
        {
            if (!root.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String)
            {
                error = L.T("The palette has no {0} colour ({1}).", Label(key), key);
                return false;
            }
            var hex = v.GetString()!;
            if (Parse(hex) is null)
            {
                error = L.T("The {0} colour \"{1}\" isn't a colour like #1a2b3c.", Label(key), hex);
                return false;
            }
            colours[key] = hex.ToLowerInvariant();
        }
        return true;
    }
}

/// <summary>
/// The theme in use in this browser tab, shared by the header's switch and the colour editor, so changing it in one shows in
/// the other. The browser itself keeps the choice (wwwroot/theme.js).
/// </summary>
public sealed class ThemeService
{
    /// <summary>"dark" (the default look), "day", "dusk", "night" or "custom".</summary>
    public string Current { get; private set; } = "dark";

    public event Action? Changed;

    public void Set(string name)
    {
        if (Current == name) return;
        Current = name;
        Changed?.Invoke();
    }
}
