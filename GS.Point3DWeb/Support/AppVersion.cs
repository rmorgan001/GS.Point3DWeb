using System.Reflection;

namespace GS.Point3DWeb.Support;

/// <summary>The app's version, from &lt;Version&gt; in Directory.Build.props.</summary>
public static class AppVersion
{
    /// <summary>The version for display, e.g. "1.0" or "1.1.0-beta".</summary>
    public static string Display { get; } = Read();

    private static string Read()
    {
        var assembly = typeof(AppVersion).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            // The SDK appends "+<commit hash>" (source link); the header shows only the version.
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }
        return assembly.GetName().Version?.ToString(2) ?? "?";
    }
}
