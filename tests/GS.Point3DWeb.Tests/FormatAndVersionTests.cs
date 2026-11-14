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
using GS.Point3DWeb.Services;
using GS.Point3DWeb.Support;

namespace GS.Point3DWeb.Tests;

public class CoordinateFormatTests
{
    [Theory]
    [InlineData(10.5, "10:30:00")]
    [InlineData(0.0, "00:00:00")]
    [InlineData(23.999999, "24:00:00")] // rounds up whole: 23:59:59.9964 to the second
    [InlineData(-1.25, "-01:15:00")]
    [InlineData(100.0, "100:00:00")]
    public void Hours_default_format(double hours, string expected) => Assert.Equal(expected, CoordinateFormat.HoursToHms(hours));

    [Fact]
    public void Seconds_that_round_to_60_carry_into_the_minutes()
    {
        Assert.Equal("13h 00:00.00", CoordinateFormat.HoursToHms(12.999999, "h ", ":", "", 2));
        Assert.Equal("10° 00:00.00", CoordinateFormat.DegreesToDms(9.9999999, "° ", ":", "", 2));
        Assert.Equal("01:00:00", CoordinateFormat.HoursToHms(59.9999 / 60.0));
    }

    [Theory]
    [InlineData(-45.25, "-45° 15:00.00")]
    [InlineData(45.2525, "45° 15:09.00")]
    [InlineData(89.999999999, "90° 00:00.00")]
    public void Degrees_view_format(double degrees, string expected)
        => Assert.Equal(expected, CoordinateFormat.DegreesToDms(degrees, "° ", ":", "", 2));

    [Fact]
    public void Degrees_default_format() => Assert.Equal("45° 15' 00\"", CoordinateFormat.DegreesToDms(45.25));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Not_a_number_shows_dashes(double value)
    {
        Assert.Equal("--", CoordinateFormat.HoursToHms(value));
        Assert.Equal("--", CoordinateFormat.DegreesToDms(value));
    }

    [Fact]
    public void Format_ignores_the_culture()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in new[] { "de-DE", "fr-FR", "ar-SA", "fa-IR", "hi-IN" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                Assert.Equal("13h 05:07.50", CoordinateFormat.HoursToHms(13 + 5 / 60.0 + 7.5 / 3600, "h ", ":", "", 2));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void Every_tenth_of_a_second_formats_back_to_itself()
    {
        for (var tenths = 0; tenths < 24 * 3600 * 10; tenths += 7)
        {
            var hours = tenths / 36000.0;
            var text = CoordinateFormat.HoursToHms(hours, ":", ":", "", 1);
            var parts = text.Split(':');
            var back = int.Parse(parts[0]) + int.Parse(parts[1]) / 60.0 + double.Parse(parts[2], CultureInfo.InvariantCulture) / 3600.0;
            Assert.Equal(hours, back, 6);
            Assert.InRange(double.Parse(parts[2], CultureInfo.InvariantCulture), 0, 59.95);
            Assert.InRange(int.Parse(parts[1]), 0, 59);
        }
    }
}

public class AppVersionTests
{
    [Theory]
    [InlineData("1.0.9", "1.0.9", 0)]
    [InlineData("1.0.10", "1.0.9", 1)]
    [InlineData("1.0.9", "1.0.10", -1)]
    [InlineData("1.0", "1.0.0", 0)]
    [InlineData("1", "1.0.0.0", 0)]
    [InlineData("2.0", "1.99.99", 1)]
    [InlineData("1.1.0-rc.1", "1.1.0", -1)]
    [InlineData("1.1.0", "1.1.0-rc.1", 1)]
    [InlineData("1.0.6+3", "1.0.6", 0)]
    [InlineData("1.0.6+3", "1.0.6+9", 0)]
    [InlineData("1.0.7", "1.0.6+30", 1)]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1", -1)]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta", -1)]
    [InlineData("1.0.0-alpha.beta", "1.0.0-beta", -1)]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11", -1)]
    [InlineData("1.0.0-beta.11", "1.0.0-rc.1", -1)]
    [InlineData(" 1.0.9 ", "1.0.9", 0)]
    [InlineData("1.1.0-rc.1+4", "1.1.0-rc.1", 0)]
    public void SemVer_order(string a, string b, int expected) => Assert.Equal(expected, AppVersion.Compare(a, b));

    [Theory]
    [InlineData(null, "1.0")]
    [InlineData("", "1.0")]
    [InlineData("?", "1.0")]
    [InlineData("v1.0.0", "1.0.0")]
    [InlineData("1.0-", "1.0")]
    [InlineData("1.2.3.4.5", "1.0")]
    [InlineData("1.-2", "1.0")]
    [InlineData("1..2", "1.0")]
    [InlineData("abc", "1.0")]
    public void Unreadable_versions_compare_as_null(string? a, string b)
    {
        Assert.Null(AppVersion.Compare(a, b));
        Assert.Null(AppVersion.Compare(b, a));
    }

    [Fact]
    public void The_build_has_a_readable_version() => Assert.NotNull(AppVersion.Compare(AppVersion.Display, AppVersion.Display));

    [Fact]
    public void Compare_is_antisymmetric_and_transitive()
    {
        string[] versions = ["0.9", "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-beta", "1.0.0-rc.1", "1.0.0", "1.0.1", "1.1.0-rc.1", "1.1.0", "2.0.0"];
        for (var i = 0; i < versions.Length; i++)
        for (var j = 0; j < versions.Length; j++)
        {
            Assert.Equal(Math.Sign(i.CompareTo(j)), AppVersion.Compare(versions[i], versions[j]));
        }
    }
}

public class FeatureKeyFormatTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("GSP3W1")]
    [InlineData("GSP3W1.YQ")]
    [InlineData("GSP3W2.YQ.YQ")]
    [InlineData("GSP3W1.YQ.YQ.YQ")]
    [InlineData("GSP3W1.a.YQ")]          // one base64 character can't be decoded
    [InlineData("GSP3W1.YQ==.!!")]
    public void Not_a_key(string? key) => Assert.False(FeatureKeyFormat.TryParse(key, out _, out _));

    [Fact]
    public void Parses_and_formats_round_trip()
    {
        byte[] payload = [1, 2, 3, 250], signature = [9, 8, 7];
        var key = FeatureKeyFormat.Format(payload, signature);
        Assert.StartsWith("GSP3W1.", key);
        Assert.True(FeatureKeyFormat.TryParse("  " + key + "\n", out var p, out var s));
        Assert.Equal(payload, p);
        Assert.Equal(signature, s);
    }

    [Fact]
    public void A_key_signed_with_another_key_is_refused()
    {
        using var other = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var payload = System.Text.Encoding.UTF8.GetBytes("""{"name":"Mallory","issued":"2026-01-01","expires":null}""");
        var signature = other.SignData(payload, System.Security.Cryptography.HashAlgorithmName.SHA256);
        Assert.Null(FeatureKeyService.Verify(FeatureKeyFormat.Format(payload, signature), out var error));
        Assert.Equal("This key isn't valid.", error);
    }

    [Fact]
    public void Garbage_is_refused_with_a_reason()
    {
        Assert.Null(FeatureKeyService.Verify("hello", out var error));
        Assert.Equal("That isn't a GS Point3D Web feature key.", error);
        Assert.Null(FeatureKeyService.Verify(FeatureKeyFormat.Format([1, 2, 3], new byte[64]), out error));
        Assert.Equal("This key isn't valid.", error);
        Assert.Null(FeatureKeyService.Verify(FeatureKeyFormat.Format([1, 2, 3], new byte[3]), out error));
    }

    [Fact]
    public void A_huge_key_is_refused_quickly()
    {
        var huge = FeatureKeyFormat.Format(new byte[2_000_000], new byte[64]);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Null(FeatureKeyService.Verify(huge, out _));
        Assert.True(sw.ElapsedMilliseconds < 2000, $"{sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Expiry_is_the_end_of_the_day()
    {
        var key = new FeatureKeyInfo("x", new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30));
        Assert.False(key.IsExpired(new DateOnly(2026, 6, 30)));
        Assert.True(key.IsExpired(new DateOnly(2026, 7, 1)));
        Assert.False(new FeatureKeyInfo("x", new DateOnly(2026, 1, 1), null).IsExpired(DateOnly.MaxValue));
    }
}
