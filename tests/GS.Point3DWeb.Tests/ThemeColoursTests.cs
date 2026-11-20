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

using GS.Point3DWeb.Services;

namespace GS.Point3DWeb.Tests;

/// <summary>The user's own colour theme: contrast, the red-light check, and exporting and importing a palette.</summary>
public class ThemeColoursTests
{
    [Fact]
    public void Black_on_white_is_the_highest_contrast_and_a_colour_on_itself_the_lowest()
    {
        Assert.Equal(21.0, ThemeColours.Contrast("#000000", "#ffffff"), 1);
        Assert.Equal(1.0, ThemeColours.Contrast("#336699", "#336699"), 1);
    }

    [Fact]
    public void Deep_red_on_black_is_the_measured_5_5_to_1()
        => Assert.Equal(5.5, ThemeColours.Contrast("#ff2020", "#000000"), 1);

    [Fact]
    public void Something_that_isnt_a_colour_has_no_contrast()
    {
        Assert.Equal(0, ThemeColours.Contrast("red", "#000000"));
        Assert.Null(ThemeColours.Parse("#12345"));
        Assert.Null(ThemeColours.Parse("123456"));
        Assert.Equal((0x12, 0xab, 0xef), ThemeColours.Parse("#12ABef"));
        // int.TryParse with HexNumber alone would let these through.
        Assert.Null(ThemeColours.Parse("# 1 2 3"));
        Assert.Null(ThemeColours.Parse("#12 45 "));
        Assert.Null(ThemeColours.Parse("#+1+2+3"));
        Assert.Null(ThemeColours.Parse("#12345g"));
    }

    [Fact]
    public void Every_built_in_theme_has_all_seven_colours()
    {
        foreach (var (name, palette) in ThemeColours.Presets)
            foreach (var key in ThemeColours.Keys)
                Assert.True(ThemeColours.Parse(palette.GetValueOrDefault(key)) is not null, $"{name} has no valid {key}");
    }

    [Fact]
    public void The_editor_only_offers_colours_the_palette_has()
        => Assert.All(ThemeColours.EditorKeys, k => Assert.Contains(k, ThemeColours.Keys));

    [Fact]
    public void Only_the_night_theme_is_red_light_safe()
    {
        Assert.True(ThemeColours.RedLightSafe(ThemeColours.Presets["night"]));
        Assert.False(ThemeColours.RedLightSafe(ThemeColours.Presets["dark"]));
        Assert.False(ThemeColours.RedLightSafe(ThemeColours.Presets["dusk"]));
    }

    [Fact]
    public void A_light_background_is_light_and_a_dark_one_isnt()
    {
        Assert.True(ThemeColours.IsLight(ThemeColours.Presets["day"]["bg"]));
        Assert.False(ThemeColours.IsLight(ThemeColours.Presets["dark"]["bg"]));
    }

    [Fact]
    public void An_exported_palette_imports_back_unchanged()
    {
        var text = ThemeColours.Export(ThemeColours.Presets["dusk"]);
        Assert.True(ThemeColours.TryImport(text, out var colours, out var error), error);
        Assert.Equal(ThemeColours.Presets["dusk"].OrderBy(p => p.Key), colours.OrderBy(p => p.Key));
    }

    [Theory]
    [InlineData("2")]
    [InlineData("\"1\"")]
    [InlineData("null")]
    public void A_palette_from_another_version_is_refused(string version)
    {
        var text = ThemeColours.Export(ThemeColours.Presets["dusk"]).Replace("\"gsTheme\":1", $"\"gsTheme\":{version}");
        Assert.False(ThemeColours.TryImport(text, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void A_palette_without_a_version_is_still_read()
    {
        var text = ThemeColours.Export(ThemeColours.Presets["dusk"]).Replace("\"gsTheme\":1,", "");
        Assert.True(ThemeColours.TryImport(text, out var colours, out var error), error);
        Assert.Equal(ThemeColours.Presets["dusk"].OrderBy(p => p.Key), colours.OrderBy(p => p.Key));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"gsTheme\":1,\"bg\":\"#000000\"}")]
    [InlineData("{\"gsTheme\":1,\"bg\":\"black\",\"text\":\"#fff000\",\"accent\":\"#fff000\",\"warn\":\"#fff000\",\"error\":\"#fff000\",\"fail\":\"#fff000\",\"info\":\"#fff000\"}")]
    public void Text_that_isnt_a_palette_is_refused_with_a_reason(string text)
    {
        Assert.False(ThemeColours.TryImport(text, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }
}
