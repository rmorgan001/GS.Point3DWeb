using GS.Alpaca.Shared;

namespace GS.Point3DWeb.Support;

/// <summary>Which side the OTA sits on a polar (fork) mount; picks the PolarLeft / PolarRight model.</summary>
public enum PolarMode
{
    Left,
    Right
}

/// <summary>
/// Telescope model files and model offsets. Ported from GSServer (GS.Shared/Model3D.cs and
/// GS.Server/Model3D/Model3DVM.cs): the model file is the model type plus a suffix for the
/// mount's alignment mode, and each alignment mode has its own axis offsets. The .obj files
/// (GS.Server/Models) are served from wwwroot/model3d/models.
/// </summary>
public static class Model3D
{
    public const string ModelFolder = "model3d/models/";

    /// <summary>User RA / Dec offsets are whole degrees in this range, as in GS.Point3d.</summary>
    public const int MinOffset = -90;
    public const int MaxOffset = 90;

    /// <summary>The whole-degree choices for the RA / Dec adjustment lists.</summary>
    public static IReadOnlyList<int> OffsetValues { get; } =
        [.. Enumerable.Range(MinOffset, MaxOffset - MinOffset + 1)];

    /// <summary>
    /// Default user offsets. These are adjustments on top of GSServer's offsets for the
    /// alignment mode (<see cref="GetAxisModelOffsets"/>), so 0 means "as GSServer draws it".
    /// </summary>
    public const double DefaultRaOffset = 0;
    public const double DefaultDecOffset = 0;

    public static IReadOnlyList<Model3DType> ModelTypes { get; } = Enum.GetValues<Model3DType>();

    /// <summary>The .obj files in wwwroot/model3d/models (same set as GSServer).</summary>
    private static readonly HashSet<string> ModelFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Default.obj", "DefaultAltAz.obj",
        "Reflector.obj", "ReflectorAltAz.obj",
        "Refractor.obj", "RefractorAltAz.obj",
        "SchmidtCassegrain.obj", "SchmidtCassegrainAltAz.obj",
        "RitcheyChretien.obj", "RitcheyChretienAltAz.obj",
        "RitcheyChretienTruss.obj", "RitcheyChretienTrussAltAz.obj",
        "DualTelescopeAltAz.obj", "DualTelescopePolarLeft.obj", "DualTelescopePolarRight.obj"
    };

    /// <summary>File-name suffix GSServer adds for each alignment mode.</summary>
    public static string GetSuffix(AlignmentModes mode, PolarMode polarMode) => mode switch
    {
        AlignmentModes.algAltAz => "AltAz",
        AlignmentModes.algPolar => polarMode == PolarMode.Left ? "PolarLeft" : "PolarRight",
        _ => ""
    };

    /// <summary>
    /// The model file for a model type on a mount. GSServer only has some combinations
    /// (e.g. Dual Telescope has no German polar file, polar mounts only have Dual Telescope),
    /// so a missing combination falls back to the Default model for that mount, then to Default.obj.
    /// </summary>
    public static string GetModelFile(Model3DType modelType, AlignmentModes mode, PolarMode polarMode)
    {
        var suffix = GetSuffix(mode, polarMode);
        foreach (var candidate in new[]
                 {
                     $"{modelType}{suffix}.obj",
                     $"{Model3DType.Default}{suffix}.obj",
                     // Polar mounts only have dual-telescope models in GSServer.
                     $"{Model3DType.DualTelescope}{suffix}.obj"
                 })
        {
            if (ModelFiles.Contains(candidate)) return candidate;
        }
        return "Default.obj";
    }

    public static string GetModelUrl(Model3DType modelType, AlignmentModes mode, PolarMode polarMode)
        => ModelFolder + GetModelFile(modelType, mode, polarMode);

    /// <summary>
    /// GSServer's AxisModelOffsets for each alignment mode profile (X = Dec axis, Y = RA axis,
    /// Z = tilt): German polar and Polar {X:90, Y:-90, Z:0}, Alt-Az {X:0, Y:90, Z:0}.
    /// </summary>
    public static (double X, double Y, double Z) GetAxisModelOffsets(AlignmentModes mode) => mode switch
    {
        AlignmentModes.algAltAz => (0.0, 90.0, 0.0),
        _ => (90.0, -90.0, 0.0)
    };

    /// <summary>GSServer's ZAxis: 90 for Alt-Az, otherwise the latitude (rounded to 2 places).</summary>
    public static double GetZAxis(AlignmentModes mode, double latitude)
        => mode == AlignmentModes.algAltAz ? 90.0 : Math.Round(Math.Abs(latitude), 2);

    /// <summary>GSServer's YAxisCentre: the German polar pivot (Server.settings default -102), else 0.</summary>
    public static double GetYAxisCentre(AlignmentModes mode)
        => mode == AlignmentModes.algGermanPolar ? -102.0 : 0.0;

    /// <summary>
    /// Port of GSServer's Model3D.RotateModel for a non-SkyWatcher ("Simulator") mount, which is
    /// what an Alpaca client sees. Returns (YAxis, XAxis) from the mount axis positions.
    /// </summary>
    public static (double YAxis, double XAxis) RotateModel(double ax, double ay, bool southernHemisphere, AlignmentModes mode)
    {
        var yAxis = Math.Round(ax, 3);
        var xAxis = mode == AlignmentModes.algGermanPolar && southernHemisphere
            ? Math.Round(ay - 180, 3)
            : Math.Round(ay * -1.0, 3);
        return (yAxis, xAxis);
    }

    /// <summary>
    /// The transform GSServer applies to the model (XAxisOffset, YAxisOffset, ZAxisOffset,
    /// YAxisCentre) for mount axis positions ax / ay, plus the server's own RA / Dec adjustments.
    /// </summary>
    public static (double X, double Y, double Z, double YCentre) GetPose(
        AlignmentModes mode, double latitude, double ax, double ay, double raAdjust, double decAdjust)
    {
        var (yAxis, xAxis) = RotateModel(ax, ay, latitude < 0, mode);
        var offsets = GetAxisModelOffsets(mode);
        return (
            xAxis + offsets.X + decAdjust,          // XAxisOffset = XAxis + offset.X
            yAxis + offsets.Y + raAdjust,           // YAxisOffset = YAxis + offset.Y
            offsets.Z - GetZAxis(mode, latitude),   // ZAxisOffset = offset.Z - ZAxis
            GetYAxisCentre(mode));
    }

    /// <summary>GSServer uses the southern compass only for equatorial mounts in the south.</summary>
    public static bool UseNorthCompass(AlignmentModes mode, double latitude)
        => latitude >= 0 || mode == AlignmentModes.algAltAz;

    public static string GetDisplayName(Model3DType modelType) => modelType switch
    {
        Model3DType.SchmidtCassegrain => "Schmidt-Cassegrain",
        Model3DType.RitcheyChretien => "Ritchey-Chrétien",
        Model3DType.RitcheyChretienTruss => "Ritchey-Chrétien Truss",
        Model3DType.DualTelescope => "Dual Telescope",
        _ => modelType.ToString()
    };

    public static string GetDisplayName(AlignmentModes mode) => mode switch
    {
        AlignmentModes.algAltAz => "Alt-Az",
        AlignmentModes.algPolar => "Polar (fork)",
        AlignmentModes.algGermanPolar => "German polar",
        _ => "Unknown"
    };

    public static double ClampOffset(double value) => Math.Clamp(Math.Round(value), MinOffset, MaxOffset);
}
