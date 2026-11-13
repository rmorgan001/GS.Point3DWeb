using System.Reflection;

namespace GS.Point3DWeb.Support;

/// <summary>
/// The app's version: from the release tag, or for other builds the newest version tag plus the commits
/// since it (Directory.Build.props and Directory.Build.targets).
/// </summary>
public static class AppVersion
{
    /// <summary>The version for display, e.g. "1.0.1", "1.0.1+3" (3 commits after v1.0.1) or "1.1.0-rc.1".</summary>
    public static string Display { get; } = Read();

    private static string Read()
    {
        var assembly = typeof(AppVersion).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        // The commit hash is kept out of it (IncludeSourceRevisionInInformationalVersion), so a "+" is the
        // commit count.
        if (!string.IsNullOrWhiteSpace(informational)) return informational;
        return assembly.GetName().Version?.ToString(2) ?? "?";
    }
}
