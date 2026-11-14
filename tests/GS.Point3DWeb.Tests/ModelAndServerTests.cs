using System.Reflection;
using GS.Alpaca.Shared;
using GS.Point3DWeb.Dialogs;
using GS.Point3DWeb.Models;
using GS.Point3DWeb.Services;
using GS.Point3DWeb.Support;

namespace GS.Point3DWeb.Tests;

public class Model3DTests
{
    [Theory]
    [InlineData(Model3DType.Reflector, AlignmentModes.algAltAz, PolarMode.Left, "ReflectorAltAz.obj")]
    [InlineData(Model3DType.Reflector, AlignmentModes.algGermanPolar, PolarMode.Left, "Reflector.obj")]
    [InlineData(Model3DType.DualTelescope, AlignmentModes.algGermanPolar, PolarMode.Left, "Default.obj")]
    [InlineData(Model3DType.Reflector, AlignmentModes.algPolar, PolarMode.Left, "DualTelescopePolarLeft.obj")]
    [InlineData(Model3DType.Refractor, AlignmentModes.algPolar, PolarMode.Right, "DualTelescopePolarRight.obj")]
    [InlineData(Model3DType.DualTelescope, AlignmentModes.algAltAz, PolarMode.Right, "DualTelescopeAltAz.obj")]
    [InlineData(Model3DType.SchmidtCassegrain, AlignmentModes.algUnknown, PolarMode.Left, "SchmidtCassegrain.obj")]
    public void Model_file(Model3DType type, AlignmentModes mode, PolarMode polar, string expected)
        => Assert.Equal(expected, Model3D.GetModelFile(type, mode, polar));

    [Fact]
    public void Every_combination_names_a_file_that_is_served()
    {
        foreach (var type in Enum.GetValues<Model3DType>())
        foreach (var mode in Enum.GetValues<AlignmentModes>())
        foreach (var polar in Enum.GetValues<PolarMode>())
        {
            var url = Model3D.GetModelUrl(type, mode, polar);
            Assert.True(File.Exists(Path.Combine(Repo.WebRoot, url)), $"{type}/{mode}/{polar}: {url} is missing");
        }
    }

    [Fact]
    public void Every_listed_model_file_exists_and_every_file_is_listed()
    {
        var listed = (HashSet<string>)typeof(Model3D).GetField("ModelFiles", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var onDisk = Directory.GetFiles(Path.Combine(Repo.WebRoot, "model3d", "models"), "*.obj").Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Empty(listed.Except(onDisk!, StringComparer.OrdinalIgnoreCase));
        Assert.Empty(onDisk.Except(listed, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void German_polar_pose_north()
    {
        var (x, y, z, centre) = Model3D.GetPose(AlignmentModes.algGermanPolar, 51.5, 30, 45, 0, 0);
        Assert.Equal((45.0, -60.0, -51.5, -102.0), (x, y, z, centre));
    }

    [Fact]
    public void German_polar_pose_south_turns_the_dec_axis()
    {
        var (x, _, z, _) = Model3D.GetPose(AlignmentModes.algGermanPolar, -30, 30, 45, 0, 0);
        Assert.Equal(-45.0, x);
        Assert.Equal(-30.0, z);
    }

    [Fact]
    public void Alt_az_pose_and_user_offsets()
    {
        var (x, y, z, centre) = Model3D.GetPose(AlignmentModes.algAltAz, 51.5, 120, 30, 5, -3);
        Assert.Equal((-33.0, 215.0, -90.0, 0.0), (x, y, z, centre));
    }

    [Theory]
    [InlineData(120, 90)]
    [InlineData(-90.4, -90)]
    [InlineData(12.4, 12)]
    [InlineData(12.6, 13)]
    [InlineData(-200, -90)]
    public void Offsets_are_whole_degrees_within_90(double value, double expected) => Assert.Equal(expected, Model3D.ClampOffset(value));

    [Fact]
    public void An_offset_just_below_zero_is_written_like_the_lists_zero()
        // Rounding -0.3 gives -0, written "-0", which matches no option of the adjust lists ("0").
        => Assert.Equal("0", Model3D.ClampOffset(-0.3).ToString(System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void Offset_list_has_every_whole_degree()
    {
        Assert.Equal(181, Model3D.OffsetValues.Count);
        Assert.Equal(-90, Model3D.OffsetValues[0]);
        Assert.Equal(90, Model3D.OffsetValues[^1]);
    }

    [Fact]
    public void Compass_and_tilt()
    {
        Assert.True(Model3D.UseNorthCompass(AlignmentModes.algGermanPolar, 0));
        Assert.False(Model3D.UseNorthCompass(AlignmentModes.algGermanPolar, -10));
        Assert.False(Model3D.UseNorthCompass(AlignmentModes.algPolar, -10));
        Assert.True(Model3D.UseNorthCompass(AlignmentModes.algAltAz, -10));
        Assert.Equal(33.46, Model3D.GetZAxis(AlignmentModes.algGermanPolar, -33.456));
        Assert.Equal(90, Model3D.GetZAxis(AlignmentModes.algAltAz, -33.456));
    }

    [Fact]
    public void Display_names_exist_for_every_type_and_mode()
    {
        foreach (var type in Enum.GetValues<Model3DType>()) Assert.False(string.IsNullOrWhiteSpace(Model3D.GetDisplayName(type)));
        foreach (var mode in Enum.GetValues<AlignmentModes>()) Assert.False(string.IsNullOrWhiteSpace(Model3D.GetDisplayName(mode)));
    }
}

public class ModelAppearanceTests
{
    [Theory]
    [InlineData("#ABCDEF", "#abcdef")]
    [InlineData("#52b788", "#52b788")]
    [InlineData("#abc", ModelAppearance.DefaultColor)]
    [InlineData("red", ModelAppearance.DefaultColor)]
    [InlineData("#12345g", ModelAppearance.DefaultColor)]
    [InlineData(" #123456", ModelAppearance.DefaultColor)]
    [InlineData("#123456\n", ModelAppearance.DefaultColor)]
    [InlineData("javascript:alert(1)", ModelAppearance.DefaultColor)]
    [InlineData(null, ModelAppearance.DefaultColor)]
    public void Colours_are_normalised(string? input, string expected) => Assert.Equal(expected, ModelAppearance.NormalizeColor(input));

    [Theory]
    [InlineData("m51", "M51")]
    [InlineData("None", "None")]
    [InlineData(null, "NorthAmerica")]
    [InlineData("../../etc/passwd", "NorthAmerica")]
    public void Backgrounds(string? key, string expected) => Assert.Equal(expected, ModelAppearance.GetBackground(key).Key);

    [Fact]
    public void Every_background_and_preset_is_usable()
    {
        Assert.Null(ModelAppearance.GetBackground("None").Url);
        foreach (var bg in ModelAppearance.Backgrounds.Where(b => b.File is not null))
            Assert.True(File.Exists(Path.Combine(Repo.WebRoot, bg.Url!)), bg.Url);
        foreach (var preset in ModelAppearance.ColorPresets)
            Assert.Equal(preset.Hex, ModelAppearance.NormalizeColor(preset.Hex));
        Assert.Equal(ModelAppearance.Backgrounds.Count, ModelAppearance.Backgrounds.Select(b => b.Key.ToLowerInvariant()).Distinct().Count());
    }
}

public class AlpacaServerTests
{
    [Theory]
    [InlineData("  http://h:1/API/V1/Telescope/0  ", "http://h:1/api/v1/telescope/0/")]
    [InlineData("http://h/Proxy/api/v1/telescope/2", "http://h/Proxy/api/v1/telescope/2/")]
    [InlineData("http://h/api/v1/telescope/12/", "http://h/api/v1/telescope/12/")]
    [InlineData("http://h/somewhere/else", "http://h/somewhere/else")]
    [InlineData("ASCOM: ASCOM.Simulator.Telescope ", "ascom:ASCOM.Simulator.Telescope")]
    [InlineData("ascom:ASCOM.GS.Sky.Telescope", "ascom:ASCOM.GS.Sky.Telescope")]
    public void Address_is_normalised(string input, string expected) => Assert.Equal(expected, AlpacaServer.NormalizeUrl(input));

    [Fact]
    public void Constructor_normalises_the_address() => Assert.Equal("http://h:1/api/v1/telescope/0/", new AlpacaServer("http://h:1/API/v1/telescope/0", "x").Url);

    [Theory]
    [InlineData("HTTP://LOCALHOST:32323/api/v1/telescope/0/", "http://localhost:32323/api/v1/telescope/0", true)]
    [InlineData("http://h:80/api/v1/telescope/0/", "http://h/api/v1/telescope/0/", true)]
    [InlineData("http://h/Proxy/api/v1/telescope/0/", "http://h/proxy/api/v1/telescope/0/", false)]
    [InlineData("http://h/api/v1/telescope/0/", "http://h/api/v1/telescope/1/", false)]
    [InlineData("http://h/api/v1/telescope/0/", "https://h/api/v1/telescope/0/", false)]
    [InlineData("ascom:A.B", "ASCOM:a.b", true)]
    [InlineData("ascom:A.B", "http://a.b/", false)]
    [InlineData("not a url", "not a url", true)]
    public void Same_device(string a, string b, bool expected)
    {
        Assert.Equal(expected, AlpacaServer.SameUrl(a, b));
        Assert.Equal(expected, AlpacaServer.SameUrl(b, a));
    }

    [Fact]
    public void Com_addresses()
    {
        var server = new AlpacaServer("ascom:ASCOM.Simulator.Telescope", "Sim");
        Assert.True(server.IsCom);
        Assert.Equal("ASCOM.Simulator.Telescope", server.ProgId);
        Assert.Null(new AlpacaServer("http://h/api/v1/telescope/0/", "x").ProgId);
    }

    [Theory]
    [InlineData(0.04, 0.5)]
    [InlineData(7, 5)]
    [InlineData(1.26, 1.3)]
    [InlineData(0.5, 0.5)]
    public void Poll_interval_is_clamped(double input, double expected) => Assert.Equal(expected, AlpacaServer.ClampPollSeconds(input));

    [Fact]
    public void A_poll_interval_that_isnt_a_number_is_not_clamped()
    {
        // ClampPollSeconds(NaN) is NaN, and the view's timer then waits (int)(1000 * NaN) = 0 ms between polls.
        // Only the edit dialog can set it, and it refuses NaN (see EditServerDialogTests), so this is a note.
        Assert.True(double.IsNaN(AlpacaServer.ClampPollSeconds(double.NaN)));
    }

    [Fact]
    public void Each_connect_or_disconnect_is_counted()
    {
        var server = new AlpacaServer("http://h/api/v1/telescope/0/", "x");
        var before = server.ConnectionChanges;
        server.ConnectRequested = true;
        server.ConnectRequested = false;
        Assert.Equal(before + 2, server.ConnectionChanges);
    }

    [Fact]
    public void Connecting_shows_until_the_last_of_overlapping_connects_ends()
    {
        var server = new AlpacaServer("http://h/api/v1/telescope/0/", "x");
        server.BeginConnect(); // two tabs connect at once
        server.BeginConnect();
        server.EndConnect();
        Assert.True(server.IsConnecting);
        server.EndConnect();
        Assert.False(server.IsConnecting);
    }

    [Fact]
    public void Defaults_for_a_new_server()
    {
        var server = new AlpacaServer("http://h/api/v1/telescope/0/", "x");
        Assert.True(Guid.TryParse(server.IdGuid, out _));
        Assert.Equal(AlignmentModes.algGermanPolar, server.AlignmentMode);
        Assert.Equal(0.5, server.PollSeconds);
        Assert.True(server.ShowRa && server.ShowDec && server.ShowAz && server.ShowAlt && server.ShowLha && server.ShowLst && server.ShowMountState && server.ShowGrid);
        Assert.False(server.IsConnected || server.ConnectRequested || server.IsConnecting);
    }
}

public class EditServerDialogTests
{
    private static EditServerDialogViewModel Dialog(string url = "http://127.0.0.1:32323/api/v1/telescope/0/", params AlpacaServer[] others)
    {
        var server = new AlpacaServer(url, "Mount") { UserName = "u", Password = "p", PollSeconds = 1 };
        return new EditServerDialogViewModel(server, [server, .. others]);
    }

    [Theory]
    [InlineData("http://h:1/api/v1/telescope/0", null)]
    [InlineData("https://h/Proxy/api/v1/telescope/3/", null)]
    [InlineData("http://h:1/API/V1/TELESCOPE/0/", null)]
    [InlineData("", "Required")]
    [InlineData("   ", "Required")]
    [InlineData("h:1/api/v1/telescope/0/", "Invalid")]
    [InlineData("ftp://h/api/v1/telescope/0/", "Invalid")]
    [InlineData("file:///api/v1/telescope/0/", "Invalid")]
    [InlineData("http://h/api/v1/camera/0/", "Invalid path (expected …/api/v1/telescope/{n}/)")]
    [InlineData("http://h/api/v1/telescope/x/", "Invalid path (expected …/api/v1/telescope/{n}/)")]
    [InlineData("http://h/api/v1/telescope/0/?a=1", "Invalid path (expected …/api/v1/telescope/{n}/)")]
    [InlineData("http://h/api/v1/telescope/0/#top", "Invalid path (expected …/api/v1/telescope/{n}/)")]
    [InlineData("http://h/api/v1/telescope/0/devicestate", "Invalid path (expected …/api/v1/telescope/{n}/)")]
    public void Alpaca_address_validation(string url, string? error)
    {
        var dialog = Dialog();
        dialog.Url = url;
        Assert.Equal(error, dialog.UrlError);
    }

    [Fact]
    public void Com_address_validation()
    {
        var dialog = Dialog("http://h/api/v1/telescope/0/", new AlpacaServer("ascom:ASCOM.Taken.Telescope", "Taken"));
        if (!ComTelescopes.IsSupported)
        {
            dialog.Url = "ascom:ASCOM.Simulator.Telescope";
            Assert.Equal(AlpacaConnector.ComNotSupportedMessage, dialog.UrlError);
            return;
        }
        dialog.Url = "ascom:ASCOM.Simulator.Telescope";
        Assert.Null(dialog.UrlError);
        dialog.Url = "ascom:1Bad";
        Assert.Equal("Invalid ProgID (expected ascom:<ProgID>)", dialog.UrlError);
        dialog.Url = "ascom:Bad-Id";
        Assert.NotNull(dialog.UrlError);
        dialog.Url = "ascom:";
        Assert.NotNull(dialog.UrlError);
        dialog.Url = "ASCOM:ascom.taken.telescope";
        Assert.Equal("Already in the list as Taken", dialog.UrlError);
    }

    [Theory]
    [InlineData(0.5, false)]
    [InlineData(5.0, false)]
    [InlineData(0.49, true)]
    [InlineData(5.01, true)]
    [InlineData(double.NaN, true)]
    [InlineData(double.PositiveInfinity, true)]
    public void Poll_interval_validation(double seconds, bool error)
    {
        var dialog = Dialog();
        dialog.PollSeconds = seconds;
        Assert.Equal(error, dialog.PollSecondsError is not null);
    }

    [Fact]
    public void Accept_is_refused_while_anything_is_invalid()
    {
        var dialog = Dialog();
        dialog.Show();
        dialog.ServerName = "  ";
        dialog.ConfirmCommand.Execute(null);
        Assert.False(dialog.Confirmed);
        Assert.True(dialog.IsDialogOpen);
        dialog.ServerName = "ok";
        dialog.PollSeconds = double.NaN;
        dialog.ConfirmCommand.Execute(null);
        Assert.False(dialog.Confirmed);
        dialog.PollSeconds = 2;
        dialog.ConfirmCommand.Execute(null);
        Assert.True(dialog.Confirmed);
        Assert.False(dialog.IsDialogOpen);
    }

    [Fact]
    public void ApplyTo_copies_and_reports_an_address_change()
    {
        var server = new AlpacaServer("http://h/api/v1/telescope/0/", "Old") { UserName = "u", Password = "p" };
        server.ConnectRequested = true;
        server.IsConnected = true;

        var same = new EditServerDialogViewModel(server) { ServerName = "  New name  ", Url = "HTTP://H/api/v1/telescope/0" };
        Assert.False(same.ApplyTo(server));
        Assert.Equal("New name", server.ServerName);
        Assert.True(server.IsConnected); // same device: still connected

        var moved = new EditServerDialogViewModel(server) { Url = "http://h/api/v1/telescope/1/", PollSeconds = 4.04 };
        Assert.True(moved.ApplyTo(server));
        Assert.False(server.IsConnected);
        Assert.False(server.ConnectRequested);
        Assert.Equal(4.0, server.PollSeconds);
    }

    [Fact]
    public void Blank_credentials_are_removed_and_a_com_driver_has_none()
    {
        var server = new AlpacaServer("http://h/api/v1/telescope/0/", "x") { UserName = "u", Password = "p" };
        new EditServerDialogViewModel(server) { UserName = "  ", Password = "" }.ApplyTo(server);
        Assert.Null(server.UserName);
        Assert.Null(server.Password);

        var com = new AlpacaServer("http://h/api/v1/telescope/0/", "x") { UserName = "u", Password = "p", PasswordUnreadable = true };
        new EditServerDialogViewModel(com) { Url = "ascom:ASCOM.Simulator.Telescope" }.ApplyTo(com);
        Assert.Null(com.UserName);
        Assert.Null(com.Password);
        Assert.False(com.PasswordUnreadable);
    }

    [Fact]
    public void An_unreadable_password_is_kept_unless_edited()
    {
        var server = new AlpacaServer("http://h/api/v1/telescope/0/", "x") { UserName = "u", Password = null, PasswordUnreadable = true };
        new EditServerDialogViewModel(server) { ServerName = "renamed" }.ApplyTo(server);
        Assert.True(server.PasswordUnreadable);

        var edit = new EditServerDialogViewModel(server) { Password = "new" };
        edit.ApplyTo(server);
        Assert.False(server.PasswordUnreadable);
        Assert.Equal("new", server.Password);
    }
}
