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
using GS.Alpaca.Shared;

namespace GS.Point3DWeb.Support;

/// <summary>Which side the OTA sits on a polar (fork) mount; picks the PolarLeft / PolarRight model.</summary>
public enum PolarMode
{
    Left,
    Right
}

/// <summary>How the coordinates show on the front of a 3D model card.</summary>
public enum CoordinatesLayout
{
    /// <summary>One chip per coordinate along the top of the scene, the mount state along the bottom.</summary>
    Individual,

    /// <summary>One panel on an edge of the scene with every coordinate and, when connected, the mount state.</summary>
    Grid,

    /// <summary>No coordinates or mount state: only the Connect / Disconnect button, so the scene is left clear.</summary>
    None
}

/// <summary>How big the distant N E S W letters in the 3D view are (model3d.js CARDINAL_SIZES).</summary>
public enum CardinalSize
{
    Small,
    Medium,
    Large,
    XLarge
}

/// <summary>
/// EXPERIMENTAL: what an assembled model stands on (model3d.js ASSEMBLY_SUPPORTS, keyed by these names in lower case):
/// tripods at 82%, 100% and 120% of the full-size one, piers 6, 10 and 12 inches across.
/// </summary>
public enum AssemblySupport
{
    SmallTripod,
    MediumTripod,
    LargeTripod,
    SmallPier,
    MediumPier,
    LargePier
}

/// <summary>
/// EXPERIMENTAL: the mount of an assembled model (model3d.js ASSEMBLY_MOUNTS, keyed by these names in lower case).
/// All German equatorial: they share the viewer's joints and differ in measurements and shapes.
/// </summary>
public enum AssemblyMount
{
    Eq6rPro,
    Heq5Pro,
    Eq8rPro,
    Am5,
    Cem70,
    Cq350Pro,
    Eqm35Pro,
    Wave150iPro,
    Cem40,
    /// <summary>A generic fork mount: on a wedge (polar), or straight up (Alt-Az).</summary>
    Fork
}

/// <summary>
/// EXPERIMENTAL: the OTA an assembled model carries. The first five are the tube from that model's German polar .obj
/// (same names as <see cref="GS.Alpaca.Shared.Model3DType"/>); the rest the viewer builds itself
/// (model3d.js ASSEMBLY_BUILT_OTAS, keyed by these names).
/// </summary>
public enum AssemblyOta
{
    Refractor,
    Reflector,
    SchmidtCassegrain,
    RitcheyChretien,
    RitcheyChretienTruss,
    Newtonian,
    MaksutovCassegrain
}

/// <summary>Which edge of the scene the coordinates grid sits on; the model moves into the space left over.</summary>
public enum GridPosition
{
    Left,
    Right,
    Top,
    Bottom
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

    // ===== EXPERIMENTAL: models assembled from parts (model3d.js loadAssembly) =====

    // The assembled model's dropdowns start at these (the support's is DefaultAssemblySupport, below).
    public const AssemblyMount DefaultAssemblyMount = AssemblyMount.Eq6rPro;
    public const AssemblyOta DefaultAssemblyOta = AssemblyOta.Refractor;

    /// <summary>The OTAs an assembled model can carry, in the order offered.</summary>
    public static IReadOnlyList<AssemblyOta> AssemblyOtas { get; } = Enum.GetValues<AssemblyOta>();

    /// <summary>
    /// The model type whose .obj an OTA's tube comes from, or null for an OTA the viewer builds itself.
    /// </summary>
    public static Model3DType? GetObjModel(AssemblyOta ota)
        => Enum.TryParse<Model3DType>(ota.ToString(), out var type) ? type : null;

    /// <summary>
    /// Whether the assembled model shows: when the chosen mount works the way the real one does. German equatorial
    /// mounts in their own mode (as is an unknown mode); the AM5 and Wave 150i Pro in Alt-Az too, as the real ones
    /// can; the fork as a polar mount (on its wedge) or Alt-Az. Otherwise the usual model of the chosen OTA shows.
    /// </summary>
    public static bool ShowsAssembly(AlignmentModes mode, AssemblyMount mount) => mode switch
    {
        AlignmentModes.algAltAz => mount is AssemblyMount.Am5 or AssemblyMount.Wave150iPro or AssemblyMount.Fork,
        AlignmentModes.algPolar => mount is AssemblyMount.Fork,
        _ => mount is not AssemblyMount.Fork
    };

    /// <summary>
    /// The support and OTA that suit a mount: what it starts with when it's chosen, unless the support or OTA has
    /// been changed from the previous mount's.
    /// </summary>
    public static (AssemblySupport Support, AssemblyOta Ota) GetSuggestedParts(AssemblyMount mount) => mount switch
    {
        AssemblyMount.Heq5Pro => (AssemblySupport.SmallTripod, AssemblyOta.Newtonian),
        AssemblyMount.Eq8rPro => (AssemblySupport.LargePier, AssemblyOta.RitcheyChretienTruss),
        AssemblyMount.Cq350Pro => (AssemblySupport.LargeTripod, AssemblyOta.SchmidtCassegrain),
        AssemblyMount.Eqm35Pro => (AssemblySupport.SmallTripod, AssemblyOta.MaksutovCassegrain),
        AssemblyMount.Wave150iPro => (AssemblySupport.MediumTripod, AssemblyOta.Refractor),
        AssemblyMount.Am5 => (AssemblySupport.SmallTripod, AssemblyOta.RitcheyChretien),
        AssemblyMount.Cem40 => (AssemblySupport.MediumTripod, AssemblyOta.Newtonian),
        AssemblyMount.Cem70 => (AssemblySupport.MediumPier, AssemblyOta.SchmidtCassegrain),
        AssemblyMount.Fork => (AssemblySupport.LargeTripod, AssemblyOta.SchmidtCassegrain),
        _ => (DefaultAssemblySupport, DefaultAssemblyOta)
    };

    /// <summary>
    /// The OTA sizes offered, in percent of the mount's own OTA size (model3d.js ASSEMBLY_MOUNTS otaScale), so one
    /// setting suits whichever mount is chosen. 100 is the mount's own size.
    /// </summary>
    public static IReadOnlyList<int> AssemblyOtaSizes { get; } = [50, 75, 100, 125, 150, 200];

    public const int DefaultAssemblyOtaSize = 100;

    /// <summary>A saved OTA size if it is one offered, else the mount's own size.</summary>
    public static int GetAssemblyOtaSize(int percent)
        => AssemblyOtaSizes.Contains(percent) ? percent : DefaultAssemblyOtaSize;

    /// <summary>
    /// The usual (.obj) model to show for an OTA: when the assembled model is off, or on a mount it doesn't cover. A
    /// built OTA shows the nearest one: the Newtonian as the reflector, the Maksutov as the Schmidt-Cassegrain.
    /// </summary>
    public static Model3DType GetUsualModel(AssemblyOta ota) => GetObjModel(ota) ?? ota switch
    {
        AssemblyOta.MaksutovCassegrain => Model3DType.SchmidtCassegrain,
        _ => Model3DType.Reflector
    };

    /// <summary>
    /// The OTA for a server saved before the OTA was chosen on the card: its old Model's, or the refractor for the
    /// default and dual-telescope models, which have no OTA of their own.
    /// </summary>
    public static AssemblyOta GetOtaFor(Model3DType modelType)
        => Enum.TryParse<AssemblyOta>(modelType.ToString(), out var ota) ? ota : AssemblyOta.Refractor;

    public static string GetDisplayName(AssemblyOta ota) => ota switch
    {
        AssemblyOta.Newtonian => L.T("Newtonian"),
        AssemblyOta.MaksutovCassegrain => L.T("Maksutov-Cassegrain"),
        _ => GetDisplayName(GetObjModel(ota) ?? Model3DType.Refractor)
    };

    public static string GetDisplayName(AssemblySupport support) => support switch
    {
        AssemblySupport.SmallTripod => L.T("Small tripod"),
        AssemblySupport.LargeTripod => L.T("Large tripod"),
        AssemblySupport.SmallPier => L.T("Small pier"),
        AssemblySupport.MediumPier => L.T("Medium pier"),
        AssemblySupport.LargePier => L.T("Large pier"),
        _ => L.T("Medium tripod")
    };

    /// <summary>The support a new server starts with.</summary>
    public const AssemblySupport DefaultAssemblySupport = AssemblySupport.MediumTripod;

    /// <summary>
    /// A saved support. Earlier names become their size: the full-size tripod the medium one, the observatory pier
    /// (9 inches) and the 10-inch pier the medium pier, the 6-inch the small one and the 12-inch the large one.
    /// Anything else unknown is the default.
    /// </summary>
    public static AssemblySupport ParseAssemblySupport(string? saved) => saved switch
    {
        "Tripod" => AssemblySupport.MediumTripod,
        "Pier" or "Pier10" => AssemblySupport.MediumPier,
        "Pier6" => AssemblySupport.SmallPier,
        "Pier12" => AssemblySupport.LargePier,
        _ => Enum.TryParse<AssemblySupport>(saved, out var support) && Enum.IsDefined(support) ? support : DefaultAssemblySupport
    };

    /// <summary>The mount's maker and product name (not translated), or for the generic fork its kind.</summary>
    public static string GetDisplayName(AssemblyMount mount) => mount switch
    {
        AssemblyMount.Heq5Pro => "Sky-Watcher HEQ5 Pro",
        AssemblyMount.Eq8rPro => "Sky-Watcher EQ8-R Pro",
        AssemblyMount.Am5 => "ZWO AM5",
        AssemblyMount.Cem70 => "iOptron CEM70",
        AssemblyMount.Cem40 => "iOptron CEM40",
        AssemblyMount.Cq350Pro => "Sky-Watcher CQ350 Pro",
        AssemblyMount.Eqm35Pro => "Sky-Watcher EQM-35 Pro",
        AssemblyMount.Wave150iPro => "Sky-Watcher Wave 150i Pro",
        AssemblyMount.Fork => L.T("Fork mount"),
        _ => "Sky-Watcher EQ6-R Pro"
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

    public static string GetDisplayName(Model3DType modelType) => L.T(modelType switch
    {
        Model3DType.SchmidtCassegrain => "Schmidt-Cassegrain",
        Model3DType.RitcheyChretien => "Ritchey-Chrétien",
        Model3DType.RitcheyChretienTruss => "Ritchey-Chrétien Truss",
        Model3DType.DualTelescope => "Dual Telescope",
        _ => modelType.ToString()
    });

    public static string GetDisplayName(AlignmentModes mode) => L.T(mode switch
    {
        AlignmentModes.algAltAz => "Alt-Az",
        AlignmentModes.algPolar => "Polar (fork)",
        AlignmentModes.algGermanPolar => "German polar",
        _ => "Unknown"
    });

    /// <summary>
    /// Rounds an offset to whole degrees within -90 to 90. Adding 0.0 turns the -0 that rounding gives for values
    /// just below zero into 0, which is written "0" like the lists' option, not "-0".
    /// </summary>
    public static double ClampOffset(double value) => Math.Clamp(Math.Round(value), MinOffset, MaxOffset) + 0.0;
}
