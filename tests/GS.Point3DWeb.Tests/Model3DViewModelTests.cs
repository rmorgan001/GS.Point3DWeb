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

using GS.Alpaca.Shared;
using GS.Point3DWeb.Dialogs;
using GS.Point3DWeb.FakeAlpaca;
using GS.Point3DWeb.Models;
using GS.Point3DWeb.Services;
using GS.Point3DWeb.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace GS.Point3DWeb.Tests;

/// <summary>The 3D view's polling loop, against the fake server (no browser).</summary>
public class Model3DViewModelTests : FakeServerTest
{
    private readonly DialogService _dialogs = new();
    private readonly List<Model3DViewModel> _views = [];

    private Model3DViewModel View(AlpacaServer server)
    {
        var view = new Model3DViewModel(DiskFreeSettings.Create(), _dialogs, NullLogger<Model3DViewModel>.Instance) { Server = server };
        _views.Add(view);
        return view;
    }

    private async Task<(AlpacaServer Server, Model3DViewModel View)> ConnectedViewAsync(double poll = 0.5)
    {
        var server = NewServer();
        server.PollSeconds = poll;
        Assert.True(await AlpacaConnector.ConnectAsync(server));
        var view = View(server);
        view.StartTimerUpdates();
        return (server, view);
    }

    public override async Task DisposeAsync()
    {
        foreach (var view in _views) view.Dispose();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task Polling_updates_the_coordinates_and_the_model()
    {
        var (server, view) = await ConnectedViewAsync();
        var rotations = 0;
        view.RotationChanged += () => Interlocked.Increment(ref rotations);
        await Wait.UntilAsync(() => rotations >= 2, TimeSpan.FromSeconds(4), "two updates");

        Assert.Equal("10h 30:00.00", view.Ra);
        Assert.Equal("45° 15:00.00", view.Dec);
        Assert.Equal("120° 00:00.00", view.Az);
        Assert.Equal("30° 00:00.00", view.Alt);
        Assert.Equal("01:30:00", view.Lha);
        Assert.Equal("12h 00:00.00", view.Lst);
        Assert.Equal(22.5, view.PrimaryAxis, 9);
        Assert.Equal(45.25, view.SecondaryAxis, 9);
        Assert.Equal(51.5, view.Tilt);
        Assert.Equal("Fake 0", view.DeviceName);
        Assert.NotNull(view.Status);
        Assert.True(view.Status!.Tracking);
        Assert.StartsWith("Every 0.5 s · Updates", view.StatusText);
        Assert.Equal(51.5, server.Latitude);
        Assert.True(view.IsRunningClockOn);
        Assert.Equal(1, Mount.CallsTo("alignmentmode")); // read once per connection
    }

    [Fact]
    public async Task Ra_in_degrees()
    {
        var (server, view) = await ConnectedViewAsync();
        server.RaInDegrees = true;
        await Wait.UntilAsync(() => view.Ra == "157° 30:00.00", TimeSpan.FromSeconds(4), "Ra in degrees");
    }

    [Fact]
    public async Task Alt_az_mount_uses_azimuth_and_altitude()
    {
        Mount.AlignmentMode = 0;
        var (_, view) = await ConnectedViewAsync();
        await Wait.UntilAsync(() => view.PrimaryAxis == 120 && view.SecondaryAxis == 30, TimeSpan.FromSeconds(4), "alt-az axes");
        Assert.Equal(90, view.Tilt);
        Assert.Contains("AltAz", view.ModelUrl);
    }

    [Fact]
    public async Task Fork_mount_uses_hour_angle_without_a_flip()
    {
        Mount.AlignmentMode = 1;
        Mount.SideOfPier = 1;
        var (_, view) = await ConnectedViewAsync();
        await Wait.UntilAsync(() => Math.Abs(view.PrimaryAxis - 22.5) < 1e-9 && view.SecondaryAxis == 45.25, TimeSpan.FromSeconds(4), "polar axes");
    }

    [Fact]
    public async Task West_of_the_pier_flips_the_german_mount()
    {
        Mount.SideOfPier = 1;
        var (_, view) = await ConnectedViewAsync();
        await Wait.UntilAsync(() => Math.Abs(view.PrimaryAxis - 202.5) < 1e-9, TimeSpan.FromSeconds(4), "flipped axes");
        Assert.Equal(134.75, view.SecondaryAxis, 9);
    }

    [Theory]
    [InlineData(DeviceStateMode.NotImplemented)]
    [InlineData(DeviceStateMode.Http400)]
    [InlineData(DeviceStateMode.Http404)]
    [InlineData(DeviceStateMode.Http405)]
    [InlineData(DeviceStateMode.Http501)]
    [InlineData(DeviceStateMode.NoArray)]
    public async Task A_server_without_devicestate_is_read_one_property_at_a_time(DeviceStateMode mode)
    {
        Mount.DeviceState = mode;
        var (_, view) = await ConnectedViewAsync();
        var rotations = 0;
        view.RotationChanged += () => Interlocked.Increment(ref rotations);
        await Wait.UntilAsync(() => rotations >= 3, TimeSpan.FromSeconds(5), "three updates");
        Assert.Equal("10h 30:00.00", view.Ra);
        Assert.Null(view.Status); // no tracking / slewing from single properties
        Assert.Equal(1, Mount.CallsTo("devicestate")); // asked once, then not again
        Assert.True(view.TimerCounter >= 3);
    }

    [Fact]
    public async Task A_driver_error_is_shown_and_polling_carries_on()
    {
        Mount.DeviceState = DeviceStateMode.DriverError;
        var (server, view) = await ConnectedViewAsync();
        await Wait.UntilAsync(() => view.StatusText == "Error 1280: Driver exploded", TimeSpan.FromSeconds(4), "error status");
        Assert.True(server.IsConnected);
        Mount.DeviceState = DeviceStateMode.Normal;
        await Wait.UntilAsync(() => view.StatusText.StartsWith("Every"), TimeSpan.FromSeconds(4), "recovery");
    }

    [Fact]
    public async Task A_server_going_down_is_reported_as_a_lost_connection_and_then_comes_back()
    {
        var (server, view) = await ConnectedViewAsync();
        var changes = 0;
        view.ConnectionChanged += () => Interlocked.Increment(ref changes);
        await Wait.UntilAsync(() => view.TimerCounter >= 1, TimeSpan.FromSeconds(4), "first update");

        Mount.Down = true;
        await Wait.UntilAsync(() => !server.IsConnected, TimeSpan.FromSeconds(4), "lost connection");
        await Wait.UntilAsync(() => view.StatusText.StartsWith("Connection lost"), TimeSpan.FromSeconds(2), "lost status");
        Assert.True(changes >= 1);
        Assert.Null(view.Status);
        await Wait.UntilAsync(() => !view.IsRunningClockOn, TimeSpan.FromSeconds(2), "running clock paused");

        Mount.Down = false;
        await Wait.UntilAsync(() => server.IsConnected, TimeSpan.FromSeconds(8), "reconnected (checked every 5 s)");
        var counter = view.TimerCounter;
        await Wait.UntilAsync(() => view.TimerCounter > counter, TimeSpan.FromSeconds(3), "updates again");
    }

    [Fact]
    public async Task Another_program_disconnecting_the_device_is_noticed()
    {
        var (server, view) = await ConnectedViewAsync();
        await Wait.UntilAsync(() => view.TimerCounter >= 1, TimeSpan.FromSeconds(4), "first update");
        Mount.Connected = false; // devicestate now answers NotConnected
        await Wait.UntilAsync(() => !server.IsConnected, TimeSpan.FromSeconds(4), "lost connection");
        Assert.StartsWith("Connection lost", view.StatusText);
    }

    [Fact]
    public async Task A_view_of_a_server_nobody_connected_doesnt_ask_it_anything()
    {
        var server = NewServer();
        var view = View(server);
        view.StartTimerUpdates();
        await Task.Delay(1500);
        Assert.Equal("Not connected", view.StatusText);
        Assert.Equal(0, Mount.TotalRequests);
        Assert.False(view.IsRunningClockOn);
    }

    [Fact]
    public async Task Stop_stops_the_requests()
    {
        var (_, view) = await ConnectedViewAsync();
        await Wait.UntilAsync(() => view.TimerCounter >= 1, TimeSpan.FromSeconds(4), "first update");
        view.StopTimerUpdates();
        await Task.Delay(700);
        var after = Mount.TotalRequests;
        await Task.Delay(1500);
        Assert.Equal(after, Mount.TotalRequests);
        Assert.False(view.IsRunningClockOn);
    }

    [Fact]
    public async Task Start_after_dispose_does_nothing()
    {
        var server = NewServer();
        await AlpacaConnector.ConnectAsync(server);
        var view = View(server);
        view.Dispose();
        view.StartTimerUpdates();
        await Task.Delay(1200);
        Assert.Equal(0, Mount.CallsTo("devicestate"));
    }

    [Fact]
    public async Task Restarting_doesnt_leave_two_loops_running()
    {
        var (_, view) = await ConnectedViewAsync();
        for (var i = 0; i < 20; i++) view.StartTimerUpdates();
        await Task.Delay(500);
        Mount.ResetStats();
        await Task.Delay(3000);
        Assert.InRange(Mount.CallsTo("devicestate"), 3, 7); // one loop at 0.5 s: about 6
    }

    [Fact]
    public async Task The_poll_interval_is_kept()
    {
        var (_, view) = await ConnectedViewAsync(poll: 0.5);
        await Wait.UntilAsync(() => view.TimerCounter >= 1, TimeSpan.FromSeconds(4), "first update");
        Mount.ResetStats();
        await Task.Delay(5000);
        Assert.InRange(Mount.CallsTo("devicestate"), 8, 11);
    }

    [Fact]
    public async Task A_change_of_interval_applies_straight_away()
    {
        var (server, view) = await ConnectedViewAsync(poll: 0.5);
        await Wait.UntilAsync(() => view.TimerCounter >= 1, TimeSpan.FromSeconds(4), "first update");
        server.PollSeconds = 2;
        await Task.Delay(700);
        Mount.ResetStats();
        await Task.Delay(4100);
        Assert.InRange(Mount.CallsTo("devicestate"), 1, 3);
    }

    [Fact]
    public async Task A_slow_mount_is_counted_as_slow_and_polls_dont_overlap()
    {
        Mount.LatencyMs = 700;
        var (_, view) = await ConnectedViewAsync(poll: 0.5);
        await Wait.UntilAsync(() => view.TimerOverruns >= 2, TimeSpan.FromSeconds(8), "overruns counted");
        // Polls are sequential: the server never sees two devicestate requests at once.
        var times = Mount.Requests.Where(r => r.Method == "devicestate").Select(r => r.Utc).ToArray();
        for (var i = 1; i < times.Length; i++) Assert.True((times[i] - times[i - 1]).TotalMilliseconds >= 690, "overlapping polls");
    }

    [Fact]
    public async Task Disconnect_from_the_view_asks_first()
    {
        var (server, view) = await ConnectedViewAsync();
        var responder = new DialogResponder(_dialogs) { Decide = _ => false };
        await view.ToggleConnectionAsync();
        Assert.True(server.IsConnected); // cancelled
        Assert.Single(responder.Shown);

        responder.Decide = _ => true;
        await view.ToggleConnectionAsync();
        Assert.False(server.IsConnected);
        Assert.Equal("Disconnected", view.StatusText);
        Assert.False(Mount.Connected);
        await Task.Delay(1200);
        Assert.Equal("Disconnected", view.StatusText); // kept while not connected
    }

    [Fact]
    public async Task Connect_failure_from_the_view_is_shown()
    {
        Mount.Connect = ConnectMode.RefuseWithError;
        var server = NewServer();
        var view = View(server);
        view.StartTimerUpdates();
        await view.ToggleConnectionAsync();
        Assert.Equal("Connect failed: The mount's serial port is in use", view.StatusText);
        await Task.Delay(1200);
        Assert.Equal("Connect failed: The mount's serial port is in use", view.StatusText);
    }

    [Fact]
    public async Task Many_views_of_one_server_share_it()
    {
        var server = NewServer();
        await AlpacaConnector.ConnectAsync(server);
        var views = Enumerable.Range(0, 8).Select(_ => View(server)).ToList();
        views.ForEach(v => v.StartTimerUpdates());
        await Wait.UntilAsync(() => views.All(v => v.TimerCounter >= 2), TimeSpan.FromSeconds(5), "all views updating");
        Assert.All(views, v => Assert.Equal("10h 30:00.00", v.Ra));
    }

    [Fact]
    public async Task Views_of_one_server_share_its_reads()
    {
        // Eight views (as in two tabs of four) used to send eight requests per interval; now about one.
        var server = NewServer();
        await AlpacaConnector.ConnectAsync(server);
        var views = Enumerable.Range(0, 8).Select(_ => View(server)).ToList();
        foreach (var v in views)
        {
            v.StartTimerUpdates();
            await Task.Delay(30); // spread out, like views opened one after another
        }
        await Wait.UntilAsync(() => views.All(v => v.TimerCounter >= 2), TimeSpan.FromSeconds(5), "all views updating");
        Mount.ResetStats();
        await Task.Delay(5000);
        Assert.InRange(Mount.CallsTo("devicestate"), 5, 14); // ~2 a second, not ~16
        Assert.All(views, v => Assert.True(v.TimerCounter >= 8));
    }

    [Fact]
    public async Task A_shared_read_follows_an_address_change()
    {
        var server = NewServer();
        await AlpacaConnector.ConnectAsync(server);
        Fake[1].Connected = true;
        Fake[1].RightAscension = 3;
        var first = await AlpacaConnector.ReadStateAsync(server, TimeSpan.FromSeconds(10));
        server.Url = Fake.MountUrl(1);
        var second = await AlpacaConnector.ReadStateAsync(server, TimeSpan.FromSeconds(10));
        Assert.Equal((10.5, 3.0), (first.State!.RightAscension, second.State!.RightAscension));
    }

    [Fact]
    public async Task A_view_is_never_handed_a_read_it_already_had()
    {
        var server = NewServer();
        await AlpacaConnector.ConnectAsync(server);
        var first = await AlpacaConnector.ReadStateAsync(server, TimeSpan.FromSeconds(10));
        var shared = await AlpacaConnector.ReadStateAsync(server, TimeSpan.FromSeconds(10));
        Assert.Equal(first.Generation, shared.Generation); // another view: shares it
        var next = await AlpacaConnector.ReadStateAsync(server, TimeSpan.FromSeconds(10), first.Generation);
        Assert.NotEqual(first.Generation, next.Generation); // the same view: a new read
        Assert.Equal(2, Mount.CallsTo("devicestate"));
    }

    [Fact]
    public async Task A_single_view_gets_a_new_reading_every_update()
    {
        var (_, view) = await ConnectedViewAsync(poll: 0.5);
        await Wait.UntilAsync(() => view.TimerCounter >= 1, TimeSpan.FromSeconds(4), "first update");
        Mount.ResetStats();
        var before = view.TimerCounter;
        await Task.Delay(6000); // includes a connected check, which starts that cycle's read late
        Assert.InRange(view.TimerCounter - before - Mount.CallsTo("devicestate"), -1, 1);
    }

    [Fact]
    public async Task A_disconnect_drops_a_read_under_way()
    {
        var server = NewServer();
        await AlpacaConnector.ConnectAsync(server);
        Mount.LatencyMs = 5000;
        var read = AlpacaConnector.ReadStateAsync(server, TimeSpan.FromSeconds(10));
        await Task.Delay(200);
        Mount.LatencyMs = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await AlpacaConnector.DisconnectAsync(server);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        Assert.True(sw.ElapsedMilliseconds < 2000, $"{sw.ElapsedMilliseconds} ms");
        // Reconnected: a new read, not the dropped one.
        await AlpacaConnector.ConnectAsync(server);
        Assert.Equal(10.5, (await AlpacaConnector.ReadStateAsync(server, TimeSpan.FromSeconds(10))).State!.RightAscension);
    }

    [Fact]
    public async Task Mount_info_that_always_fails_is_tried_a_few_times_then_left()
    {
        Mount.MountInfoHttp500 = true;
        var (_, view) = await ConnectedViewAsync(poll: 0.5);
        await Task.Delay(13000);
        Assert.Equal(5, Mount.CallsTo("alignmentmode"));
        Assert.True(view.TimerCounter >= 15); // the position kept updating meanwhile
    }

    [Fact]
    public async Task Polling_pauses_while_the_browser_is_gone()
    {
        var server = NewServer();
        await AlpacaConnector.ConnectAsync(server);
        var circuit = new CircuitState();
        var view = new Model3DViewModel(DiskFreeSettings.Create(), _dialogs, NullLogger<Model3DViewModel>.Instance, circuit) { Server = server };
        _views.Add(view);
        view.StartTimerUpdates();
        await Wait.UntilAsync(() => view.TimerCounter >= 1, TimeSpan.FromSeconds(4), "first update");

        Assert.True(view.IsRunningClockOn);
        await circuit.OnConnectionDownAsync(null!, default);
        await Task.Delay(700);
        Mount.ResetStats();
        await Task.Delay(2000);
        Assert.Equal(0, Mount.TotalRequests);
        Assert.False(view.IsRunningClockOn); // the clock pauses with the updates

        await circuit.OnConnectionUpAsync(null!, default);
        var counter = view.TimerCounter;
        await Wait.UntilAsync(() => view.TimerCounter > counter, TimeSpan.FromSeconds(3), "updates again");
        Assert.True(view.IsRunningClockOn);
    }

    [Fact]
    public async Task The_time_a_poll_takes_doesnt_stretch_the_interval()
    {
        Mount.LatencyMs = 200;
        var (_, view) = await ConnectedViewAsync(poll: 0.5);
        await Wait.UntilAsync(() => view.TimerCounter >= 1, TimeSpan.FromSeconds(6), "first update");
        Mount.ResetStats();
        await Task.Delay(5000);
        Assert.InRange(Mount.CallsTo("devicestate"), 8, 11); // every 0.5 s, not every 0.7 s (7)
    }

    [Fact]
    public void Mount_info_object_and_model_name()
    {
        var view = View(new AlpacaServer("http://h/api/v1/telescope/0/", "x") { AlignmentMode = AlignmentModes.algAltAz, Latitude = -20, ModelType = Model3DType.Reflector });
        Assert.Equal("{ mode = 0, latitude = -20 }", view.MountInfo.ToString());
        Assert.Equal("Reflector (Alt-Az)", view.ModelName);
        Assert.Equal("model3d/models/ReflectorAltAz.obj", view.ModelUrl);
        Assert.True(view.UseNorthCompass);
    }
}

public class DialogServiceTests
{
    [Fact]
    public async Task One_dialog_at_a_time_in_order()
    {
        var service = new DialogService();
        var first = new ConfirmDialogViewModel { Message = "first" };
        var second = new ConfirmDialogViewModel { Message = "second" };
        var showFirst = service.ShowDialog(first);
        var showSecond = service.ShowDialog(second);
        await Task.Delay(50);
        Assert.Same(first, service.Current);
        Assert.False(second.IsDialogOpen);
        first.CancelCommand.Execute(null);
        await showFirst;
        await Wait.UntilAsync(() => service.Current == second, TimeSpan.FromSeconds(1), "second dialog");
        second.ConfirmCommand.Execute(null);
        await showSecond;
        Assert.Null(service.Current);
        Assert.True(second.Confirmed);
        Assert.False(first.Confirmed);
    }

    [Fact]
    public async Task A_dialog_can_be_shown_again()
    {
        var service = new DialogService();
        var dialog = new ConfirmDialogViewModel();
        _ = new DialogResponder(service);
        await service.ShowDialog(dialog);
        dialog.Confirmed = false;
        await service.ShowDialog(dialog).WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(dialog.Confirmed);
    }
}

/// <summary>Changes AlpacaConnector.DeviceStateRetry, so it runs on its own (see SerialCollection).</summary>
[Collection("Serial")]
public class DeviceStateRetryTests : FakeServerTest
{
    [Fact]
    public async Task Devicestate_is_tried_again_after_a_while()
    {
        var retry = AlpacaConnector.DeviceStateRetry;
        AlpacaConnector.DeviceStateRetry = TimeSpan.FromSeconds(2);
        try
        {
            var server = NewServer();
            await AlpacaConnector.ConnectAsync(server);
            var view = new Model3DViewModel(DiskFreeSettings.Create(), new DialogService(), NullLogger<Model3DViewModel>.Instance) { Server = server };
            Mount.DeviceState = DeviceStateMode.Http404; // one odd answer (a proxy, a server restarting)
            view.StartTimerUpdates();
            await Wait.UntilAsync(() => view.TimerCounter >= 1, TimeSpan.FromSeconds(4), "first update");
            Assert.Null(view.Status); // read one property at a time meanwhile
            Mount.DeviceState = DeviceStateMode.Normal;
            await Wait.UntilAsync(() => view.Status is not null, TimeSpan.FromSeconds(6), "back on devicestate");
            view.Dispose();
        }
        finally
        {
            AlpacaConnector.DeviceStateRetry = retry;
        }
    }

    [Fact]
    public async Task Connecting_again_tries_devicestate_straight_away()
    {
        // The server is still connected, so the connect changes nothing else, but the user connecting is how they
        // start afresh: devicestate is tried again then, not after the minute's wait.
        var server = NewServer();
        await AlpacaConnector.ConnectAsync(server);
        var view = new Model3DViewModel(DiskFreeSettings.Create(), new DialogService(), NullLogger<Model3DViewModel>.Instance) { Server = server };
        Mount.DeviceState = DeviceStateMode.Http404;
        view.StartTimerUpdates();
        await Wait.UntilAsync(() => view.TimerCounter >= 1, TimeSpan.FromSeconds(4), "first update");
        Assert.Null(view.Status);
        Mount.DeviceState = DeviceStateMode.Normal;
        Assert.True(await AlpacaConnector.ConnectAsync(server));
        await Wait.UntilAsync(() => view.Status is not null, TimeSpan.FromSeconds(3), "back on devicestate");
        view.Dispose();
    }
}
