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

using System.Text.Json;
using System.Text.Json.Nodes;
using GS.Alpaca.Shared;
using GS.Point3DWeb.Dialogs;
using GS.Point3DWeb.FakeAlpaca;
using GS.Point3DWeb.Models;
using GS.Point3DWeb.Services;
using GS.Point3DWeb.Support;
using GS.Point3DWeb.ViewModels;
using Microsoft.AspNetCore.DataProtection;

namespace GS.Point3DWeb.Tests;

/// <summary>servers.json: loading, saving, upgrades and damaged files. Isolated settings folder only.</summary>
[Collection("Settings")]
public class SettingsServiceTests
{
    public SettingsServiceTests()
    {
        if (Sandbox.IsIsolated) Sandbox.Reset();
    }

    private static string FilePath => Path.Combine(Sandbox.AppFolder, "servers.json");

    private static void WriteFile(string json)
    {
        Directory.CreateDirectory(Sandbox.AppFolder);
        File.WriteAllText(FilePath, json);
    }

    private static JsonNode ReadFile() => JsonNode.Parse(File.ReadAllText(FilePath))!;

    private static string Server(string id, string url = "http://h/api/v1/telescope/0/", string name = "S", string extra = "")
        => $$"""{"IdGuid":"{{id}}","ServerName":"{{name}}","Url":"{{url}}","UserName":null,"Password":null{{extra}}}""";

    [IsolatedFact]
    public void First_run_starts_with_the_simulator_and_writes_nothing()
    {
        var settings = Sandbox.NewSettings();
        var server = Assert.Single(settings.Servers);
        Assert.Equal("http://127.0.0.1:32323/api/v1/telescope/0/", server.Url);
        Assert.Same(server, settings.SelectedAlpacaServer);
        Assert.False(File.Exists(FilePath));
        Assert.Null(settings.LoadProblem);
    }

    [IsolatedFact]
    public void Everything_saved_is_loaded_back()
    {
        var settings = Sandbox.NewSettings();
        var s = new AlpacaServer("http://h:1/api/v1/telescope/3/", "Everything")
        {
            UserName = "user", Password = "secret", ModelType = Model3DType.RitcheyChretienTruss, RaOffset = 12, DecOffset = -7,
            PolarMode = PolarMode.Right, AlignmentMode = AlignmentModes.algPolar, Latitude = -33.5,
            Camera = new CameraView(1, 2, 3, 4, 5, 6), PollSeconds = 2.5, ShowRa = false, ShowDec = false, ShowAz = false,
            ShowAlt = false, ShowLha = false, ShowLst = false, RaInDegrees = true, ModelColor = "#123456", Background = "M33",
            ShowGrid = false, ShowMountState = false
        };
        Assert.True(settings.TryAddServer(s, 10));
        settings.SetQuadrant(2, s.IdGuid);
        settings.ViewCount = 3;
        settings.SelectedAlpacaServer = s;
        settings.Save();
        Assert.False(File.Exists(FilePath + ".tmp"));

        var loaded = Sandbox.NewSettings();
        var l = loaded.Servers.Single(x => x.IdGuid == s.IdGuid);
        Assert.Equal((s.ServerName, s.Url, s.UserName, s.Password), (l.ServerName, l.Url, l.UserName, l.Password));
        Assert.Equal((s.ModelType, s.RaOffset, s.DecOffset, s.PolarMode, s.AlignmentMode, s.Latitude), (l.ModelType, l.RaOffset, l.DecOffset, l.PolarMode, l.AlignmentMode, l.Latitude));
        Assert.Equal(s.Camera, l.Camera);
        Assert.Equal((s.PollSeconds, s.ShowRa, s.ShowDec, s.ShowAz, s.ShowAlt, s.ShowLha, s.ShowLst, s.RaInDegrees), (l.PollSeconds, l.ShowRa, l.ShowDec, l.ShowAz, l.ShowAlt, l.ShowLha, l.ShowLst, l.RaInDegrees));
        Assert.Equal((s.ModelColor, s.Background, s.ShowGrid, s.ShowMountState), (l.ModelColor, l.Background, l.ShowGrid, l.ShowMountState));
        Assert.Equal(s.IdGuid, loaded.QuadrantServerIds[2]);
        Assert.Equal(3, loaded.ViewCount);
        Assert.Same(l, loaded.SelectedAlpacaServer);
        Assert.False(l.IsConnected); // never saved
        var file = ReadFile();
        Assert.Equal(2, (int)file["Version"]!);
        Assert.Equal(AppVersion.Display, (string)file["AppVersion"]!);
    }

    [IsolatedFact]
    public void Passwords_are_saved_encrypted()
    {
        var settings = Sandbox.NewSettings();
        settings.Servers[0].Password = "hunter2-very-secret";
        settings.Save();
        var text = File.ReadAllText(FilePath);
        Assert.DoesNotContain("hunter2", text);
        Assert.Equal("hunter2-very-secret", Sandbox.NewSettings().Servers[0].Password);
    }

    [IsolatedFact]
    public void An_unchanged_password_is_written_back_unchanged()
    {
        var settings = Sandbox.NewSettings();
        settings.Servers[0].Password = "pw";
        settings.Save();
        var first = (string)ReadFile()["Servers"]![0]!["Password"]!;
        settings.Save();
        Assert.Equal(first, (string)ReadFile()["Servers"]![0]!["Password"]!);
    }

    [IsolatedFact]
    public void A_password_from_another_machine_is_kept_until_it_is_replaced()
    {
        var settings = Sandbox.NewSettings();
        settings.Servers[0].Password = "pw";
        settings.Save();
        var encrypted = (string)ReadFile()["Servers"]![0]!["Password"]!;

        var elsewhere = Sandbox.NewSettings(new EphemeralDataProtectionProvider()); // other keys: can't decrypt
        var server = elsewhere.Servers[0];
        Assert.Null(server.Password);
        Assert.True(server.PasswordUnreadable);
        elsewhere.Save();
        Assert.Equal(encrypted, (string)ReadFile()["Servers"]![0]!["Password"]!);

        server.Password = "new";
        server.PasswordUnreadable = false;
        elsewhere.Save();
        Assert.NotEqual(encrypted, (string)ReadFile()["Servers"]![0]!["Password"]!);
    }

    [IsolatedFact]
    public void A_damaged_file_is_copied_aside_and_the_app_starts_empty()
    {
        WriteFile("{ this is not json");
        var settings = Sandbox.NewSettings();
        Assert.Empty(settings.Servers);
        Assert.Contains("could not be read", settings.LoadProblem);
        var copy = Assert.Single(Directory.GetFiles(Sandbox.AppFolder, "servers.json.unreadable-*"));
        Assert.Equal("{ this is not json", File.ReadAllText(copy));
        settings.Save(); // allowed: the original is safe
    }

    [IsolatedFact]
    public void A_damaged_file_that_cant_be_copied_is_never_saved_over()
    {
        WriteFile("{ broken");
        var mode = File.GetUnixFileMode(Sandbox.AppFolder);
        File.SetUnixFileMode(Sandbox.AppFolder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var settings = Sandbox.NewSettings();
            Assert.Contains("won't be saved over", settings.LoadProblem);
            Assert.Throws<InvalidOperationException>(settings.Save);
        }
        finally
        {
            File.SetUnixFileMode(Sandbox.AppFolder, mode);
        }
        Assert.Equal("{ broken", File.ReadAllText(FilePath));
    }

    [IsolatedTheory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{"SelectedId":null,"Servers":null}""")]
    [InlineData("""{"Servers":[{"IdGuid":"a","ServerName":"S","Url":"http://h/","PollSeconds":"fast"}]}""")]
    public void Unusable_files_are_reported(string json)
    {
        WriteFile(json);
        var settings = Sandbox.NewSettings();
        Assert.Empty(settings.Servers);
        Assert.NotNull(settings.LoadProblem);
    }

    [IsolatedFact]
    public void Bad_entries_are_skipped_and_the_rest_kept()
    {
        WriteFile($$"""
            {"Version":2,"AppVersion":"{{AppVersion.Display}}","Servers":[
              {{Server("a", name: "Good")}},
              {"IdGuid":"","ServerName":"No id","Url":"http://h/"},
              {"IdGuid":"b","ServerName":"No url","Url":"  "},
              null,
              {{Server("a", name: "Same id")}},
              {{Server("c", name: "Also good")}}
            ]}
            """);
        var settings = Sandbox.NewSettings();
        Assert.Equal(["Good", "Also good"], settings.Servers.Select(s => s.ServerName));
        Assert.Null(settings.LoadProblem);
    }

    [IsolatedFact]
    public void Out_of_range_values_are_brought_back_into_range()
    {
        WriteFile($$"""
            {"Version":2,"AppVersion":"{{AppVersion.Display}}","ViewCount":9,"Quadrants":["gone","a",null,"a","extra"],"Servers":[
              {{Server("a", name: "", extra: ",\"PollSeconds\":99,\"ModelColor\":\"blue\",\"Background\":\"xyz\",\"ModelType\":\"Bogus\",\"PolarMode\":\"Up\",\"RaOffset\":500,\"DecOffset\":-500")}}
            ]}
            """);
        var settings = Sandbox.NewSettings();
        var s = settings.Servers.Single();
        Assert.Equal(s.Url, s.ServerName); // a blank name shows the address
        Assert.Equal(5, s.PollSeconds);
        Assert.Equal(ModelAppearance.DefaultColor, s.ModelColor);
        Assert.Equal("NorthAmerica", s.Background);
        Assert.Equal(Model3DType.Default, s.ModelType);
        Assert.Equal(PolarMode.Left, s.PolarMode);
        Assert.Equal((90.0, -90.0), (s.RaOffset, s.DecOffset));
        Assert.Equal(4, settings.ViewCount);
        Assert.Equal([null, "a", null, "a"], settings.QuadrantServerIds);
    }

    [IsolatedFact]
    public void An_alignment_mode_number_that_isnt_defined_is_not_kept()
    {
        // Enum.TryParse accepts any number ("7"), so an undefined mode would load as (AlignmentModes)7. The mount is
        // asked again on connect, but until then the model has no file suffix and the GEM offsets.
        WriteFile($$"""{"Version":2,"AppVersion":"{{AppVersion.Display}}","Servers":[{{Server("a", extra: ",\"AlignmentMode\":\"7\"")}}]}""");
        Assert.True(Enum.IsDefined(Sandbox.NewSettings().Servers.Single().AlignmentMode));
    }

    [IsolatedFact]
    public void Version_1_offsets_become_adjustments()
    {
        WriteFile($$"""{"Servers":[{{Server("a", extra: ",\"RaOffset\":10,\"DecOffset\":-90")}}]}""");
        var s = Sandbox.NewSettings().Servers.Single();
        Assert.Equal((10.0, 0.0), (s.RaOffset, s.DecOffset));

        WriteFile($$"""{"Version":2,"AppVersion":"{{AppVersion.Display}}","Servers":[{{Server("a", extra: ",\"RaOffset\":10,\"DecOffset\":5")}}]}""");
        s = Sandbox.NewSettings().Servers.Single();
        Assert.Equal((10.0, 5.0), (s.RaOffset, s.DecOffset));
    }

    [IsolatedFact]
    public void An_upgrade_backs_up_the_file_once()
    {
        WriteFile($$"""{"Version":2,"AppVersion":"0.1.0","Servers":[{{Server("a")}}]}""");
        Sandbox.NewSettings();
        var settings = Sandbox.NewSettings();
        var backup = Assert.Single(Directory.GetFiles(settings.BackupFolder));
        Assert.StartsWith("servers-0.1.0-", Path.GetFileName(backup));
        Assert.Contains("\"0.1.0\"", File.ReadAllText(backup));
        Assert.Equal(AppVersion.Display, (string)ReadFile()["AppVersion"]!);
        Assert.Null(settings.LoadProblem);
    }

    [IsolatedFact]
    public void A_file_without_a_version_is_backed_up_as_older()
    {
        WriteFile($$"""{"Servers":[{{Server("a")}}]}""");
        var settings = Sandbox.NewSettings();
        Assert.StartsWith("servers-older-", Path.GetFileName(Assert.Single(Directory.GetFiles(settings.BackupFolder))));
    }

    [IsolatedFact]
    public void A_version_with_odd_characters_makes_a_safe_backup_name()
    {
        WriteFile($$"""{"AppVersion":"1.0/..\\x","Servers":[{{Server("a")}}]}""");
        var settings = Sandbox.NewSettings();
        var name = Path.GetFileName(Assert.Single(Directory.GetFiles(settings.BackupFolder)));
        Assert.DoesNotContain("/", name);
        Assert.Equal(settings.BackupFolder, Path.GetDirectoryName(Directory.GetFiles(settings.BackupFolder)[0]));
    }

    [IsolatedFact]
    public void A_file_from_a_newer_version_warns_and_isnt_rewritten()
    {
        var json = $$"""{"Version":2,"AppVersion":"99.0.0","Servers":[{{Server("a")}}]}""";
        WriteFile(json);
        var settings = Sandbox.NewSettings();
        Assert.Contains("99.0.0", settings.LoadProblem);
        Assert.Equal(json, File.ReadAllText(FilePath));
        Assert.False(Directory.Exists(settings.BackupFolder) && Directory.GetFiles(settings.BackupFolder).Length > 0);
    }

    [IsolatedFact]
    public void The_server_limit_holds_when_tabs_add_at_once()
    {
        var settings = Sandbox.NewSettings(); // has the simulator
        var added = 0;
        Parallel.For(0, 64, _ =>
        {
            if (settings.TryAddServer(new AlpacaServer("http://h/api/v1/telescope/0/", "x"), 5)) Interlocked.Increment(ref added);
        });
        Assert.Equal(4, added);
        Assert.Equal(5, settings.Servers.Length);
    }

    [IsolatedFact]
    public void Saves_from_many_tabs_at_once_always_leave_a_readable_file()
    {
        var settings = Sandbox.NewSettings();
        for (var i = 0; i < 20; i++) settings.TryAddServer(new AlpacaServer($"http://h{i}/api/v1/telescope/0/", $"S{i}") { Password = $"p{i}" }, 100);
        var errors = 0;
        Parallel.For(0, 200, new ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            try
            {
                settings.Servers[i % 20].ServerName = $"renamed {i}";
                settings.SetQuadrant(i % 4, settings.Servers[i % 21 % 20].IdGuid);
                settings.Save();
            }
            catch
            {
                Interlocked.Increment(ref errors);
            }
        });
        Assert.Equal(0, errors);
        var loaded = Sandbox.NewSettings();
        Assert.Equal(21, loaded.Servers.Length);
        Assert.All(loaded.Servers.Skip(1), s => Assert.StartsWith("p", s.Password));
    }

    [IsolatedFact]
    public void Remove_flags_the_server_and_tells_the_tabs()
    {
        var settings = Sandbox.NewSettings();
        var server = settings.Servers[0];
        var changes = 0;
        settings.ServersChanged += () => changes++;
        settings.RemoveServer("no such id");
        Assert.Equal(0, changes);
        settings.RemoveServer(server.IdGuid);
        Assert.Equal(1, changes);
        Assert.True(server.Removed);
        Assert.Empty(settings.Servers);
    }

    [IsolatedFact]
    public void Quadrant_and_view_count_changes_are_announced_once()
    {
        var settings = Sandbox.NewSettings();
        var changes = 0;
        settings.LayoutChanged += () => changes++;
        settings.SetQuadrant(0, "x");
        settings.SetQuadrant(0, "x");
        settings.SetQuadrant(-1, "x");
        settings.SetQuadrant(4, "x");
        settings.ViewCount = 2;
        settings.ViewCount = 2;
        settings.ViewCount = 0; // becomes 1
        Assert.Equal(3, changes);
        Assert.Equal(1, settings.ViewCount);
    }

    [IsolatedFact]
    public void Quadrants_of_deleted_servers_are_not_saved()
    {
        var settings = Sandbox.NewSettings();
        var server = settings.Servers[0];
        settings.SetQuadrant(1, server.IdGuid);
        settings.RemoveServer(server.IdGuid);
        settings.Save();
        Assert.Null(ReadFile()["Quadrants"]![1]);
    }
}

/// <summary>A theory that only runs with an isolated settings folder.</summary>
public sealed class IsolatedTheoryAttribute : TheoryAttribute
{
    public IsolatedTheoryAttribute()
    {
        if (!Sandbox.IsIsolated) Skip = Sandbox.SkipReason;
    }
}

[Collection("Settings")]
public class FeatureKeyServiceTests
{
    public FeatureKeyServiceTests()
    {
        if (Sandbox.IsIsolated) Sandbox.Reset();
    }

    [IsolatedFact]
    public void Without_a_key_the_free_limits_apply()
    {
        var keys = Sandbox.NewFeatureKeys();
        Assert.False(keys.IsLicensed);
        Assert.Equal(5, keys.MaxServers);
        Assert.Equal(4, keys.MaxViews);
    }

    [IsolatedFact]
    public void A_bad_key_is_refused_and_not_saved()
    {
        var keys = Sandbox.NewFeatureKeys();
        var changed = 0;
        keys.Changed += () => changed++;
        Assert.False(keys.TryApply("GSP3W1.YQ.YQ", out var error));
        Assert.Equal("This key isn't valid.", error);
        Assert.False(File.Exists(keys.FilePath));
        Assert.Equal(0, changed);
    }

    [IsolatedFact]
    public void A_damaged_key_file_is_ignored()
    {
        Directory.CreateDirectory(Sandbox.AppFolder);
        File.WriteAllText(Path.Combine(Sandbox.AppFolder, "feature.key"), "GSP3W1.garbage");
        Assert.False(Sandbox.NewFeatureKeys().IsLicensed);
    }

    [IsolatedFact]
    public void An_unreadable_key_file_doesnt_stop_the_app()
    {
        Directory.CreateDirectory(Sandbox.AppFolder);
        var path = Path.Combine(Sandbox.AppFolder, "feature.key");
        File.WriteAllText(path, "x");
        File.SetUnixFileMode(path, UnixFileMode.None);
        try
        {
            Assert.False(Sandbox.NewFeatureKeys().IsLicensed);
        }
        finally
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [IsolatedFact]
    public void Remove_deletes_the_file()
    {
        Directory.CreateDirectory(Sandbox.AppFolder);
        var keys = Sandbox.NewFeatureKeys();
        File.WriteAllText(keys.FilePath, "x");
        Assert.True(keys.TryRemove(out _));
        Assert.False(File.Exists(keys.FilePath));
    }
}

/// <summary>The home page's logic (server list, toolbar, views) with the fake server.</summary>
[Collection("Settings")]
public class HomeViewModelTests : IAsyncLifetime
{
    private FakeAlpacaServer _fake = null!;
    private SettingsService _settings = null!;
    private DialogService _dialogs = null!;
    private DialogResponder _responder = null!;
    private HomeViewModel _home = null!;

    public async Task InitializeAsync()
    {
        _fake = await FakeAlpacaServer.StartAsync(mounts: 2);
        if (!Sandbox.IsIsolated) return;
        Sandbox.Reset();
        _settings = Sandbox.NewSettings();
        _dialogs = new DialogService();
        _responder = new DialogResponder(_dialogs);
        _home = new HomeViewModel(_settings, _dialogs, Sandbox.NewFeatureKeys());
        await _home.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        _home?.Dispose();
        await _fake.DisposeAsync();
    }

    private HomeViewModel SecondTab()
    {
        var tab = new HomeViewModel(_settings, new DialogService(), Sandbox.NewFeatureKeys());
        tab.InitializeAsync();
        return tab;
    }

    private AlpacaServer AddFake(int mount = 0)
    {
        var server = new AlpacaServer(_fake.MountUrl(mount), $"Fake {mount}");
        Assert.True(_settings.TryAddServer(server, 5));
        return server;
    }

    [IsolatedFact]
    public async Task Add_stops_at_five_servers_and_says_why()
    {
        for (var i = 0; i < 4; i++) await _home.AddCommand.ExecuteAsync(null);
        Assert.Equal(5, _home.Count);
        _responder.Decide = _ => false;
        await _home.AddCommand.ExecuteAsync(null);
        Assert.Equal(5, _home.Count);
        var limit = _responder.Shown.OfType<ConfirmDialogViewModel>().Last();
        Assert.Equal("Feature key needed", limit.Title);
        Assert.Contains("up to 5 servers", limit.Message);
        Assert.Equal(5, Sandbox.NewSettings().Servers.Length); // saved
    }

    [IsolatedFact]
    public async Task A_second_tab_sees_adds_and_deletes()
    {
        var tab = SecondTab();
        await _home.AddCommand.ExecuteAsync(null);
        Assert.Equal(2, tab.Count);
        await _home.DeleteCommand.ExecuteAsync(_home.AlpacaServers[1]);
        Assert.Equal(1, tab.Count);
        tab.Dispose();
    }

    [IsolatedFact]
    public async Task Dropping_a_server_on_a_view_shows_and_connects_it()
    {
        var server = AddFake();
        _home.StartDrag(server);
        _home.DropOnQuadrant(1);
        Assert.Same(server, _home.Quadrants[1].Server);
        Assert.Same(server, _home.SelectedAlpacaServer);
        await Wait.UntilAsync(() => server.IsConnected, TimeSpan.FromSeconds(3), "connected by the drop");
        Assert.True(_fake[0].Connected);
        Assert.Equal(server.IdGuid, Sandbox.NewSettings().QuadrantServerIds[1]);
    }

    [IsolatedFact]
    public async Task A_failed_connect_after_a_drop_is_shown()
    {
        _fake[0].Connect = ConnectMode.RefuseWithError;
        var server = AddFake();
        _home.StartDrag(server);
        _home.DropOnQuadrant(0);
        await Wait.UntilAsync(() => _responder.Messages.Any(m => m.Contains("serial port is in use")), TimeSpan.FromSeconds(3), "error dialog");
    }

    [IsolatedFact]
    public void A_drop_outside_the_shown_views_is_ignored()
    {
        var server = AddFake();
        _settings.ViewCount = 2;
        _home.StartDrag(server);
        _home.DropOnQuadrant(3);
        Assert.Null(_home.Quadrants[3].Server);
        Assert.Null(_home.DragServer);
    }

    [IsolatedFact]
    public async Task Deleting_a_connected_server_disconnects_it_and_clears_its_views()
    {
        var server = AddFake();
        Assert.True(await AlpacaConnector.ConnectAsync(server));
        _home.StartDrag(server);
        _home.DropOnQuadrant(0);
        var tab = SecondTab();
        Assert.Same(server, tab.Quadrants[0].Server);

        await _home.DeleteCommand.ExecuteAsync(server);
        Assert.DoesNotContain(server, _home.AlpacaServers);
        Assert.Null(_home.Quadrants[0].Server);
        Assert.Null(tab.Quadrants[0].Server);
        Assert.False(_fake[0].Connected);
        Assert.DoesNotContain(Sandbox.NewSettings().Servers, s => s.IdGuid == server.IdGuid);
        tab.Dispose();
    }

    [IsolatedFact]
    public async Task Changing_a_connected_servers_address_disconnects_the_old_device()
    {
        var server = AddFake(0);
        Assert.True(await AlpacaConnector.ConnectAsync(server));
        _responder.Edit = edit => edit.Url = _fake.MountUrl(1);
        await _home.EditServerCommand.ExecuteAsync(server);
        Assert.Equal(_fake.MountUrl(1), server.Url);
        Assert.False(server.IsConnected);
        Assert.False(_fake[0].Connected);
        Assert.False(_fake[1].Connected);
    }

    [IsolatedFact]
    public async Task Connect_and_disconnect_from_the_toolbar()
    {
        var server = AddFake();
        _home.SelectServerCommand.Execute(server);
        await _home.ConnectCommand.ExecuteAsync(null);
        Assert.True(server.IsConnected);
        Assert.Contains("Connected to Fake 0.", _responder.Messages);
        await _home.ConnectCommand.ExecuteAsync(null);
        Assert.False(server.IsConnected);
        Assert.False(_fake[0].Connected);
    }

    [IsolatedFact]
    public async Task A_cancelled_connect_does_nothing()
    {
        var server = AddFake();
        _home.SelectServerCommand.Execute(server);
        _responder.Decide = _ => false;
        await _home.ConnectCommand.ExecuteAsync(null);
        Assert.False(server.IsConnected);
        Assert.Equal(0, _fake[0].TotalRequests);
    }

    [IsolatedFact]
    public async Task Save_failure_is_reported_not_thrown()
    {
        var folder = Sandbox.AppFolder;
        Directory.CreateDirectory(folder);
        _settings.Save();
        var mode = File.GetUnixFileMode(folder);
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            await _home.AddCommand.ExecuteAsync(null);
            Assert.Contains(_responder.Messages, m => m.StartsWith("Could not save"));
        }
        finally
        {
            File.SetUnixFileMode(folder, mode);
        }
    }

    [IsolatedFact]
    public void View_count_is_saved()
    {
        _home.ViewCount = 2;
        Assert.Equal(2, Sandbox.NewSettings().ViewCount);
        _home.ViewCount = 9; // more than allowed: refused (a dialog says why)
        Assert.Equal(2, _home.ViewCount);
    }

    [IsolatedFact]
    public async Task A_load_problem_is_shown_once()
    {
        Directory.CreateDirectory(Sandbox.AppFolder);
        File.WriteAllText(Path.Combine(Sandbox.AppFolder, "servers.json"), "{ broken");
        var settings = Sandbox.NewSettings();
        var dialogs = new DialogService();
        var responder = new DialogResponder(dialogs);
        var home = new HomeViewModel(settings, dialogs, Sandbox.NewFeatureKeys());
        await home.ShowStartupNoticeAsync();
        await home.ShowStartupNoticeAsync();
        Assert.Single(responder.Shown);
        home.Dispose();
    }

    [IsolatedFact]
    public void Dispose_unsubscribes_from_the_shared_services()
    {
        var tab = SecondTab();
        var server = AddFake();
        var before = tab.Count;
        tab.Dispose();
        _settings.RemoveServer(server.IdGuid);
        Assert.Equal(before, tab.Count); // no longer updated
    }
}
