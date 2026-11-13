using GS.Alpaca.Shared;
using AlpacaRange = GS.Alpaca.Shared.Range;

namespace GS.Point3DWeb.Tests;

public class RangeTests
{
    [Theory]
    [InlineData(90.169444444444451, -5.8305555555555486)]
    [InlineData(12.0, 12.0)]
    [InlineData(-12.0, 12.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(-36.5, -12.5 + 24)]
    public void Range12(double input, double expected) => Assert.Equal(expected, AlpacaRange.Range12(input), 9);

    [Theory]
    [InlineData(90.169444444444451, 18.169444444444451)]
    [InlineData(24.0, 0.0)]
    [InlineData(-1.0, 23.0)]
    public void Range24(double input, double expected) => Assert.Equal(expected, AlpacaRange.Range24(input), 9);

    [Theory]
    [InlineData(90.169444444444451, 89.830555555555549)]
    [InlineData(-95.0, 85.0)]
    [InlineData(90.0, 90.0)]
    [InlineData(-90.0, -90.0)]
    public void Range90(double input, double expected) => Assert.Equal(expected, AlpacaRange.Range90(input), 9);

    [Theory]
    [InlineData(180.0, 180.0)]
    [InlineData(-180.0, 180.0)]
    [InlineData(540.0, 180.0)]
    [InlineData(-190.0, 170.0)]
    public void Range180(double input, double expected) => Assert.Equal(expected, AlpacaRange.Range180(input), 9);

    [Theory]
    [InlineData(290.169444444444451, -69.830555555555577)]
    [InlineData(270.0, -90.0)]
    [InlineData(-90.0, -90.0)]
    public void Range270(double input, double expected) => Assert.Equal(expected, AlpacaRange.Range270(input), 9);

    [Theory]
    [InlineData(590.169444444444451, 230.16944444444448)]
    [InlineData(360.0, 0.0)]
    [InlineData(-0.0000001, 0.0)] // a hair under 360 snaps to 0
    [InlineData(-10.0, 350.0)]
    public void Range360(double input, double expected) => Assert.Equal(expected, AlpacaRange.Range360(input), 9);

    [Fact]
    public void RangeDouble_without_a_range_is_NaN()
    {
        Assert.True(double.IsNaN(AlpacaRange.RangeDouble(5, 0)));
        Assert.True(double.IsNaN(AlpacaRange.RangeDouble(5, -1)));
        Assert.True(double.IsNaN(AlpacaRange.RangeDouble(5, double.PositiveInfinity)));
        Assert.Equal(9, AlpacaRange.RangeDouble(-1, 10));
    }

    public static TheoryData<double> Extremes => new() { double.PositiveInfinity, double.NegativeInfinity, double.NaN };

    [Theory]
    [MemberData(nameof(Extremes))]
    public void Non_finite_values_become_NaN_instead_of_looping_for_ever(double value)
    {
        foreach (var f in new Func<double, double>[] { AlpacaRange.Range12, AlpacaRange.Range24, AlpacaRange.Range90,
                     AlpacaRange.Range180, AlpacaRange.Range270, AlpacaRange.Range360 })
        {
            Assert.True(double.IsNaN(f(value)));
        }
    }

    [Theory]
    [InlineData(1e300)]
    [InlineData(-1e300)]
    [InlineData(1e17)]
    [InlineData(123456789.123)]
    public void Huge_values_finish_quickly_and_land_in_range(double value)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r360 = AlpacaRange.Range360(value);
        var r24 = AlpacaRange.Range24(value);
        var r90 = AlpacaRange.Range90(value);
        var r12 = AlpacaRange.Range12(value);
        Assert.InRange(r360, 0, 360);
        Assert.InRange(r24, 0, 24);
        Assert.InRange(r90, -90, 90);
        Assert.InRange(r12, -12, 12);
        Assert.True(sw.ElapsedMilliseconds < 100, $"took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Every_range_function_holds_its_range_for_many_values()
    {
        var random = new Random(42);
        for (var i = 0; i < 100_000; i++)
        {
            var v = (random.NextDouble() - 0.5) * 20_000;
            Assert.InRange(AlpacaRange.Range12(v), -12, 12);
            Assert.True(AlpacaRange.Range12(v) > -12);
            Assert.InRange(AlpacaRange.Range24(v), 0, 24);
            Assert.True(AlpacaRange.Range24(v) < 24);
            Assert.InRange(AlpacaRange.Range90(v), -90, 90);
            Assert.InRange(AlpacaRange.Range180(v), -180, 180);
            Assert.True(AlpacaRange.Range180(v) > -180);
            Assert.InRange(AlpacaRange.Range270(v), -90, 270);
            Assert.True(AlpacaRange.Range270(v) < 270);
            Assert.InRange(AlpacaRange.Range360(v), 0, 360);
            Assert.True(AlpacaRange.Range360(v) < 360);
        }
    }

    [Fact]
    public void Pair_helpers_keep_their_order()
    {
        Assert.Equal([89.0, 10.0], AlpacaRange.RangeAltAz([91.0, 370.0]));
        Assert.Equal([10.0, 89.0], AlpacaRange.RangeAz360Alt90([370.0, 91.0]));
        Assert.Equal([1.0, 89.0], AlpacaRange.RangeRaDec([25.0, 91.0]));
        Assert.Equal([10.0, -80.0], AlpacaRange.RangeAxesXy([370.0, 280.0]));
        Assert.Equal([-80.0, 10.0], AlpacaRange.RangeAxesYx([370.0, 280.0]));
    }
}

public class UnitsTests
{
    [Fact]
    public void Sexagesimal_to_decimal()
    {
        Assert.Equal(90.169444444444451, Units.Deg2Dou(90, 10, 10), 12);
        Assert.Equal(-10.5, Units.Deg2Dou(-10, 30, 0), 12);
        Assert.Equal(1.5, Units.Ra2Dou(1, 30, 0), 12);
    }

    [Fact]
    public void Minus_zero_degrees_loses_its_sign()
    {
        // -0° 30' can't be written as (degrees, minutes): -0 isn't below zero. Kept as the desktop client has it.
        Assert.Equal(0.5, Units.Deg2Dou(-0.0, 30, 0), 12);
    }

    [Fact]
    public void Angle_conversions_agree()
    {
        foreach (var r in new[] { 0.0, 1.0, Math.PI, -2.5, 6.021385919380436 })
        {
            Assert.Equal(Units.Rad2Deg(r), Units.Rad2Deg1(r), 9);
            Assert.Equal(Units.Rad2Deg(r), Units.Rad2Deg2(r), 9);
            Assert.Equal(r, Units.Deg2Rad(Units.Rad2Deg(r)), 12);
            Assert.Equal(Units.Deg2Rad(r), Units.Deg2Rad1(r), 12);
            Assert.Equal(r, Units.Hrs2Rad(Units.Rad2Hrs(r)), 9);
        }
        Assert.Equal(180, Units.Rad2Deg(Math.PI), 9);
        Assert.Equal(12, Units.Rad2Hrs(Math.PI), 9);
        Assert.Equal(23, Units.Rad2Hrs(6.021385919380436), 9);
    }
}

public class CoordinateTests
{
    [Fact]
    public void Hour_angle()
    {
        Assert.Equal(2, Coordinate.Ra2Ha12(10, 12), 9);
        Assert.Equal(2, Coordinate.Ra2Ha12(23, 1), 9);
        Assert.Equal(-2, Coordinate.Ra2Ha12(1, 23), 9);
        Assert.Equal(22, Coordinate.Ra2Ha24(1, 23), 9);
    }

    [Fact]
    public void Star_on_the_meridian_at_the_celestial_equator()
    {
        // Returns { altitude, azimuth } (its doc comment says the other order; every caller uses it this way round).
        var altAz = Coordinate.HaDec2AltAz(0, 0, 51.5);
        Assert.Equal(38.5, altAz[0], 9);
        Assert.Equal(180, altAz[1], 9);
    }

    [Fact]
    public void Declination_equal_to_latitude_on_the_meridian_is_the_zenith()
        => Assert.Equal(90, Coordinate.HaDec2AltAz(0, 40, 40)[0], 6);

    [Fact]
    public void AltAz_and_HaDec_round_trip()
    {
        var random = new Random(7);
        for (var i = 0; i < 5_000; i++)
        {
            var lat = random.NextDouble() * 170 - 85;
            var ha = random.NextDouble() * 24 - 12;
            var dec = random.NextDouble() * 170 - 85;
            var altAz = Coordinate.HaDec2AltAz(ha, dec, lat);
            var back = Coordinate.AltAz2HaDec(altAz[0], altAz[1], lat);
            Assert.Equal(dec, back[1], 6);
            Assert.Equal(0, AlpacaRangeHelpers.HoursApart(ha, back[0]), 6);
        }
    }

    [Fact]
    public void AltAz_to_RaDec_uses_the_sidereal_time()
    {
        var lst = 7.25;
        var altAz = Coordinate.RaDec2AltAz(5.0, 20.0, lst, 35.0);
        var raDec = Coordinate.AltAz2RaDec(altAz[0], altAz[1], 35.0, lst);
        Assert.Equal(5.0, raDec[0], 6);
        Assert.Equal(20.0, raDec[1], 6);
        Assert.Equal(raDec[1], Coordinate.AltAz2Dec(altAz[0], altAz[1], 35.0), 9);
        Assert.Equal(raDec[0], Coordinate.AltAz2Ra(altAz[0], altAz[1], 35.0, lst), 9);
    }
}

internal static class AlpacaRangeHelpers
{
    public static double HoursApart(double a, double b) => AlpacaRange.Range12(a - b);
}

public class AxesTests
{
    [Fact]
    public void German_polar_east_side_is_the_hour_angle_and_declination()
        => Assert.Equal([30.0, 45.0], Axes.RaDecToAxesXy(AlignmentModes.algGermanPolar, [10.0, 45.0], 12.0, false, 0));

    [Fact]
    public void German_polar_unknown_side_is_treated_as_east()
        => Assert.Equal([30.0, 45.0], Axes.RaDecToAxesXy(AlignmentModes.algGermanPolar, [10.0, 45.0], 12.0, false, -1));

    [Fact]
    public void German_polar_west_side_is_the_flipped_position()
        => Assert.Equal([210.0, 135.0], Axes.RaDecToAxesXy(AlignmentModes.algGermanPolar, [10.0, 45.0], 12.0, false, 1));

    [Fact]
    public void German_polar_west_side_with_the_hour_angle_over_90()
        => Assert.Equal([0.0, 135.0], Axes.RaDecToAxesXy(AlignmentModes.algGermanPolar, [0.0, 45.0], 12.0, false, 1));

    [Fact]
    public void Southern_hemisphere_mirrors_the_declination()
        => Assert.Equal([30.0, -45.0], Axes.RaDecToAxesXy(AlignmentModes.algGermanPolar, [10.0, 45.0], 12.0, true, 0));

    [Fact]
    public void Results_are_finite_rotations()
    {
        // The German polar branch returns before RangeAxesXy, so the east side gives (LST - RA) * 15 as it is
        // (-360..360). The values are only used as rotation angles, where -17° and 343° are the same.
        var random = new Random(3);
        for (var i = 0; i < 10_000; i++)
        {
            var axes = Axes.RaDecToAxesXy(AlignmentModes.algGermanPolar,
                [random.NextDouble() * 24, random.NextDouble() * 180 - 90], random.NextDouble() * 24,
                random.Next(2) == 0, random.Next(-1, 2));
            Assert.InRange(axes[0], -360, 540);
            Assert.InRange(axes[1], -90, 270);
        }
    }

    [Fact]
    public void Alt_az_passes_through()
        => Assert.Equal([123.0, 45.0], Axes.RaDecToAxesXy(AlignmentModes.algAltAz, [123.0, 45.0], 0, false, 0));

    [Fact]
    public void A_pier_side_that_isnt_east_west_or_unknown_throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => Axes.RaDecToAxesXy(AlignmentModes.algGermanPolar, [1.0, 1.0], 1, false, 2));

    [Fact]
    public void Unknown_alignment_mode_throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => Axes.RaDecToAxesXy(AlignmentModes.algUnknown, [1.0, 1.0], 1, false, 0));
}
