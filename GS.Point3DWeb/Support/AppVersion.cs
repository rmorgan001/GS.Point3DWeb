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

    /// <summary>
    /// Compares two versions as SemVer orders them: by number (1.0 = 1.0.0), then a pre-release
    /// (1.1.0-rc.1) comes before its release (1.1.0). Build metadata after "+" (here, the commits since the
    /// tag) is ignored, so 1.0.6+3 equals 1.0.6. Returns null if either version can't be read.
    /// </summary>
    public static int? Compare(string? a, string? b)
    {
        if (!TryParse(a, out var va, out var preA) || !TryParse(b, out var vb, out var preB)) return null;
        var byNumber = va.CompareTo(vb);
        if (byNumber != 0) return Math.Sign(byNumber);
        return ComparePreRelease(preA, preB);
    }

    private static bool TryParse(string? text, out Version number, out string? preRelease)
    {
        number = new Version();
        preRelease = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var core = text.Trim();
        var plus = core.IndexOf('+');
        if (plus >= 0) core = core[..plus]; // build metadata: not part of the order
        var dash = core.IndexOf('-');
        if (dash >= 0)
        {
            preRelease = core[(dash + 1)..];
            core = core[..dash];
            if (preRelease.Length == 0) return false;
        }

        // Fill in missing parts (1 -> 1.0.0, 1.0 -> 1.0.0) so 1.0 and 1.0.0 compare equal.
        var parts = core.Split('.');
        if (parts.Length is < 1 or > 4 || parts.Any(p => !int.TryParse(p, out var n) || n < 0)) return false;
        var numbers = parts.Select(int.Parse).Concat(Enumerable.Repeat(0, 4 - parts.Length)).ToArray();
        number = new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }

    /// <summary>SemVer pre-release order: none is higher than any; otherwise part by part, numbers below words.</summary>
    private static int ComparePreRelease(string? a, string? b)
    {
        if (a == b) return 0;
        if (a is null) return 1;
        if (b is null) return -1;

        var pa = a.Split('.');
        var pb = b.Split('.');
        for (var i = 0; i < Math.Min(pa.Length, pb.Length); i++)
        {
            var aNumeric = long.TryParse(pa[i], out var na);
            var bNumeric = long.TryParse(pb[i], out var nb);
            var result = (aNumeric, bNumeric) switch
            {
                (true, true) => na.CompareTo(nb),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(pa[i], pb[i])
            };
            if (result != 0) return Math.Sign(result);
        }
        return pa.Length.CompareTo(pb.Length);
    }
}
