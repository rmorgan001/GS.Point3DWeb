using System.Text.RegularExpressions;

namespace GS.Point3DWeb.Support;

/// <summary>A background choice for the 3D scene; <see cref="File"/> is null for a plain dark sky.</summary>
public sealed record SceneBackground(string Key, string Name, string? File)
{
    /// <summary>URL of the image (also used for the thumbnail), or null for a plain dark sky.</summary>
    public string? Url => File is null ? null : ModelAppearance.BackgroundFolder + File;
}

/// <summary>A named model color (Green Swamp palette).</summary>
public sealed record ModelColorPreset(string Name, string Hex);

/// <summary>Model color and scene background choices for the 3D views.</summary>
public static partial class ModelAppearance
{
    public const string BackgroundFolder = "model3d/backgrounds/";

    /// <summary>Green Swamp green.</summary>
    public const string DefaultColor = "#52b788";

    /// <summary>The North America nebula, the scene's original background.</summary>
    public const string DefaultBackground = "NorthAmerica";

    public static IReadOnlyList<ModelColorPreset> ColorPresets { get; } =
    [
        new("Green", DefaultColor),
        new("Yellow", "#f2c84b"),
        new("Moon", "#e8e6df"),
        new("Red", "#ef6461"),
        new("Blue", "#5aa9e6"),
        new("Orange", "#f08a4b"),
        new("Slate", "#7d8c92")
    ];

    public static IReadOnlyList<SceneBackground> Backgrounds { get; } =
    [
        new(DefaultBackground, "North America nebula", "NorthAmerica.jpg"),
        new("Heart", "Heart nebula", "Heart.jpg"),
        new("Rosette", "Rosette nebula", "Rosette.jpg"),
        new("M51", "Whirlpool galaxy (M51)", "M51.jpg"),
        new("M33", "Triangulum galaxy (M33)", "M33.jpg"),
        new("None", "Plain dark sky", null)
    ];

    public static SceneBackground GetBackground(string? key)
        => Backgrounds.FirstOrDefault(b => string.Equals(b.Key, key, StringComparison.OrdinalIgnoreCase))
           ?? Backgrounds[0];

    /// <summary>A #rrggbb color, lower-cased; anything else falls back to the default green.</summary>
    public static string NormalizeColor(string? hex)
        => hex is not null && HexColor().IsMatch(hex) ? hex.ToLowerInvariant() : DefaultColor;

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();
}
