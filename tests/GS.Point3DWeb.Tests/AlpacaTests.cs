using System.Net;
using System.Text;
using GS.Alpaca.Shared;
using GS.Alpaca.Support;
using GS.Point3DWeb.FakeAlpaca;
using GS.Point3DWeb.Models;
using GS.Point3DWeb.Services;

namespace GS.Point3DWeb.Tests;

/// <summary>A fake Alpaca server per test.</summary>
public abstract class FakeServerTest : IAsyncLifetime
{
    protected FakeAlpacaServer Fake { get; private set; } = null!;
    protected FakeMount Mount => Fake[0];

    protected virtual string Prefix => "";

    public async Task InitializeAsync() => Fake = await FakeAlpacaServer.StartAsync(mounts: 2, prefix: Prefix);

    public virtual async Task DisposeAsync() => await Fake.DisposeAsync();

    protected AlpacaServer NewServer(int mount = 0) => new(Fake.MountUrl(mount), $"Fake {mount}");
}

public class AlpacaClientTests : FakeServerTest
{
    [Fact]
    public async Task Device_state_is_read_in_one_request()
    {
        Mount.Connected = true;
        var client = new AlpacaClient(Fake.MountUrl(), 0);
        var state = await client.GetDeviceStateAsync();
        Assert.NotNull(state);
        Assert.Equal(0, state.AlpacaResponse!.ErrorNumber);
        Assert.Equal((10.5, 45.25, 12.0, 0), (state.RightAscension, state.Declination, state.SiderealTime, state.SideOfPier!.Value));
        Assert.True(state.Tracking);
        Assert.NotNull(state.UTCDate);
        Assert.Equal(1, Mount.TotalRequests);
    }

    [Fact]
    public async Task Requests_carry_the_client_and_an_increasing_transaction_id()
    {
        Mount.Connected = true;
        var client = new AlpacaClient(Fake.MountUrl(), 41);
        await client.GetPropertyAsync("RightAscension");
        await client.GetPropertyAsync("declination");
        await client.ConnectAsync();
        var requests = Mount.Requests.ToArray();
        Assert.Equal(["42", "43", "44"], requests.Select(r => r.ClientTransactionId));
        Assert.Equal("rightascension", requests[0].Method); // only the method name is lower-cased
        Assert.Equal("233", requests[2].Form["ClientID"]);
        Assert.Equal("true", requests[2].Form["Connected"]);
        Assert.Equal(44, client.ClientTransactionId);
    }

    [Fact]
    public void Transaction_id_wraps_before_the_unsigned_32_bit_limit()
    {
        var client = new AlpacaClient("http://unused/", 4294960000);
        client.ClientTransactionId++;
        Assert.Equal(0, client.ClientTransactionId);
    }

    [Fact]
    public async Task Not_connected_is_reported_as_the_servers_error()
    {
        var state = await new AlpacaClient(Fake.MountUrl(), 0).GetDeviceStateAsync();
        Assert.Equal(0x407, state!.AlpacaResponse!.ErrorNumber);
        Assert.Equal("Not connected", state.AlpacaResponse.ErrorMessage);
    }

    [Theory]
    [InlineData(DeviceStateMode.NotImplemented)]
    [InlineData(DeviceStateMode.NoArray)]
    public async Task Device_state_without_a_list_is_reported_as_not_implemented(DeviceStateMode mode)
    {
        Mount.Connected = true;
        Mount.DeviceState = mode;
        var state = await new AlpacaClient(Fake.MountUrl(), 0).GetDeviceStateAsync();
        Assert.Equal(AlpacaClient.NotImplementedError, state!.AlpacaResponse!.ErrorNumber);
    }

    [Theory]
    [InlineData(DeviceStateMode.Http400, HttpStatusCode.BadRequest)]
    [InlineData(DeviceStateMode.Http404, HttpStatusCode.NotFound)]
    public async Task Http_errors_throw_with_their_status(DeviceStateMode mode, HttpStatusCode status)
    {
        Mount.Connected = true;
        Mount.DeviceState = mode;
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => new AlpacaClient(Fake.MountUrl(), 0).GetDeviceStateAsync());
        Assert.Equal(status, ex.StatusCode);
    }

    [Fact]
    public async Task One_property_at_a_time_reads_the_position()
    {
        Mount.Connected = true;
        var state = await new AlpacaClient(Fake.MountUrl(), 0).GetDeviceStateFromPropertiesAsync();
        Assert.Equal((10.5, 45.25, 12.0, 30.0, 120.0, 0), (state!.RightAscension, state.Declination, state.SiderealTime, state.Altitude, state.Azimuth, state.SideOfPier!.Value));
        Assert.Null(state.Tracking);
        Assert.Equal(6, Mount.TotalRequests);
    }

    [Fact]
    public async Task One_property_at_a_time_stops_at_a_failed_position()
    {
        Mount.Connected = true;
        Mount.PositionError = true;
        var state = await new AlpacaClient(Fake.MountUrl(), 0).GetDeviceStateFromPropertiesAsync();
        Assert.Equal(0x500, state!.AlpacaResponse!.ErrorNumber);
        Assert.Equal(1, Mount.TotalRequests);
    }

    [Fact]
    public async Task Wrong_credentials_report_the_http_status_and_the_servers_reason()
    {
        Mount.UserName = "observer";
        Mount.Password = "pässwörd";
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => new AlpacaClient(Fake.MountUrl(), 0, "observer", "wrong").GetPropertyAsync("connected"));
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Equal("HTTP 401 Unauthorized: Bad user name or password", ex.Message);

        var ok = await new AlpacaClient(Fake.MountUrl(), 0, "observer", "pässwörd").GetPropertyAsync("connected");
        Assert.Equal(0, ok!.ErrorNumber);
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("observer:pässwörd")), Mount.Requests.Last().Authorization);
    }

    [Fact]
    public async Task No_credentials_send_no_authorization_header()
    {
        await new AlpacaClient(Fake.MountUrl(), 0).GetPropertyAsync("connected");
        Assert.Null(Mount.Requests.Single().Authorization);
    }

    [Fact]
    public async Task A_caller_cancel_is_not_reported_as_a_timeout()
    {
        Mount.LatencyMs = 5000;
        using var cts = new CancellationTokenSource(200);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AlpacaClient(Fake.MountUrl(), 0).GetPropertyAsync("connected", cts.Token));
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task A_server_that_doesnt_answer_times_out_after_ten_seconds()
    {
        Mount.LatencyMs = 15_000;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<TimeoutException>(() => new AlpacaClient(Fake.MountUrl(), 0).GetPropertyAsync("connected"));
        Assert.InRange(sw.Elapsed.TotalSeconds, 9.5, 12);
        Assert.Contains("10 s", ex.Message);
    }

    [Fact]
    public async Task Disconnect_reports_whether_it_was_accepted()
    {
        Mount.Connected = true;
        Assert.True(await new AlpacaClient(Fake.MountUrl(), 0).DisconnectAsync());
        Assert.False(Mount.Connected);
        Mount.Connected = true;
        Mount.Disconnect = DisconnectMode.RefuseAndStayConnected;
        Assert.False(await new AlpacaClient(Fake.MountUrl(), 0).DisconnectAsync());
    }

    [Fact]
    public async Task A_refused_host_throws_quickly()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<HttpRequestException>(() => new AlpacaClient("http://127.0.0.1:1/api/v1/telescope/0/", 0).GetPropertyAsync("connected"));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Garbage_instead_of_json_throws()
    {
        // A web page on that address (say, a router's) instead of an Alpaca server.
        // (The fake server's /fake/stats answers a JSON list, not an Alpaca response.)
        await Assert.ThrowsAnyAsync<Newtonsoft.Json.JsonException>(() => new AlpacaClient(Fake.BaseUrl + "/fake/", 0).GetPropertyAsync("stats"));
    }
}

/// <summary>A proxy prefix in the address keeps its case (the fake server matches paths case-sensitively).</summary>
public class AlpacaPathCaseTests : FakeServerTest
{
    protected override string Prefix => "/Proxy/Observatory";

    [Fact]
    public async Task The_address_before_the_method_is_kept_as_typed()
    {
        var server = new AlpacaServer(Fake.MountUrl().Replace("/api/v1/telescope/0/", "/API/V1/Telescope/0"), "Proxied");
        Assert.True(await AlpacaConnector.ConnectAsync(server));
        Assert.True(Mount.Connected);
    }
}

public class AlpacaConnectorTests : FakeServerTest
{
    [Fact]
    public async Task Connect_connects_and_records_it()
    {
        var server = NewServer();
        Assert.True(await AlpacaConnector.ConnectAsync(server));
        Assert.True(server.IsConnected && server.ConnectRequested && !server.IsConnecting);
        Assert.True(Mount.Connected);
        Assert.Equal(1, Mount.Requests.Count(r => r.HttpMethod == "PUT"));
        Assert.True(server.ClientTransactionId >= 3);
    }

    [Fact]
    public async Task Connect_to_a_device_that_is_already_connected_sends_no_connect()
    {
        Mount.Connected = true;
        var server = NewServer();
        Assert.True(await AlpacaConnector.ConnectAsync(server));
        Assert.Equal(0, Mount.Requests.Count(r => r.HttpMethod == "PUT"));
    }

    [Fact]
    public async Task A_refusal_throws_with_the_servers_message()
    {
        Mount.Connect = ConnectMode.RefuseWithError;
        var server = NewServer();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => AlpacaConnector.ConnectAsync(server));
        Assert.Equal("The mount's serial port is in use", ex.Message);
        Assert.False(server.IsConnected || server.ConnectRequested || server.IsConnecting);
    }

    [Fact]
    public async Task A_silent_refusal_returns_false()
    {
        Mount.Connect = ConnectMode.IgnoreSilently;
        var server = NewServer();
        Assert.False(await AlpacaConnector.ConnectAsync(server));
        Assert.False(server.IsConnected);
    }

    [Fact]
    public async Task A_server_that_is_down_throws_and_is_not_connected()
    {
        Mount.Down = true;
        var server = NewServer();
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => AlpacaConnector.ConnectAsync(server));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        Assert.False(server.IsConnected);
    }

    [Fact]
    public async Task Is_connecting_shows_while_a_slow_connect_runs()
    {
        Mount.ConnectLatencyMs = 600;
        var server = NewServer();
        var connect = AlpacaConnector.ConnectAsync(server);
        await Wait.UntilAsync(() => server.IsConnecting, TimeSpan.FromSeconds(2), "IsConnecting");
        Assert.True(await connect);
        Assert.False(server.IsConnecting);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task A_slow_connect_within_30_seconds_succeeds()
    {
        Mount.ConnectLatencyMs = 12_000; // beyond the 10 s request limit: Connect has its own, longer one
        var server = NewServer();
        Assert.True(await AlpacaConnector.ConnectAsync(server));
    }

    [Fact]
    public async Task A_server_deleted_during_its_connect_is_not_marked_connected()
    {
        Mount.ConnectLatencyMs = 500;
        var server = NewServer();
        var connect = AlpacaConnector.ConnectAsync(server);
        await Task.Delay(100);
        server.Removed = true;
        Assert.False(await connect);
        Assert.False(server.IsConnected);
        Assert.False(server.ConnectRequested);
    }

    [Fact]
    public async Task A_server_deleted_during_its_connect_releases_the_device()
    {
        // Deleting a connected server disconnects it (HomeViewModel.DeleteAsync). One deleted while its connect is
        // still running isn't yet ConnectRequested, so nothing disconnects it, and the connect then leaves the
        // Alpaca device connected with no entry left to disconnect it. (A COM driver is released in this case.)
        Mount.ConnectLatencyMs = 500;
        var server = NewServer();
        var connect = AlpacaConnector.ConnectAsync(server);
        await Task.Delay(100);
        server.Removed = true;
        await connect;
        await Wait.UntilAsync(() => !Mount.Connected, TimeSpan.FromSeconds(2), "the deleted server's device released");
    }

    [Fact]
    public async Task A_device_another_program_connected_is_left_alone_after_a_delete()
    {
        Mount.Connected = true; // already connected: this connect sends no connect, so it releases nothing
        Mount.LatencyMs = 300;
        var server = NewServer();
        var connect = AlpacaConnector.ConnectAsync(server);
        await Task.Delay(100);
        server.Removed = true;
        await connect;
        Assert.True(Mount.Connected);
    }

    [Fact]
    public async Task Releasing_the_old_device_doesnt_hold_up_the_server_at_its_new_address()
    {
        Mount.ConnectLatencyMs = 500;
        Mount.DisconnectLatencyMs = 3000;
        var server = NewServer();
        var connect = AlpacaConnector.ConnectAsync(server);
        await Task.Delay(100);
        server.Url = Fake.MountUrl(1);
        // The connect returns without waiting for the old device's slow release, which still goes on.
        Assert.False(await connect.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.False(server.IsConnecting);
        Assert.True(Mount.Connected, "the release should still be under way");
        await Wait.UntilAsync(() => !Mount.Connected, TimeSpan.FromSeconds(5), "the old device released");
    }

    [Fact]
    public async Task A_connect_to_a_device_being_released_waits_for_the_release()
    {
        Mount.ConnectLatencyMs = 500;
        Mount.DisconnectLatencyMs = 1000;
        var server = NewServer();
        var connect = AlpacaConnector.ConnectAsync(server);
        await Task.Delay(100);
        server.Url = Fake.MountUrl(1);
        Assert.False(await connect); // the old device's release is now under way
        server.Url = Fake.MountUrl(0); // and back: connecting again mustn't be undone by that release
        Assert.True(await AlpacaConnector.ConnectAsync(server));
        await Task.Delay(500);
        Assert.True(Mount.Connected);
        Assert.True(server.IsConnected);
    }

    [Fact]
    public async Task A_device_another_entry_is_connecting_is_not_released()
    {
        Mount.ConnectLatencyMs = 500;
        var moved = NewServer();
        var other = NewServer(); // a second entry for the same device
        var connect = AlpacaConnector.ConnectAsync(moved);
        await Task.Delay(100);
        var otherConnect = AlpacaConnector.ConnectAsync(other);
        await Task.Delay(50);
        moved.Url = Fake.MountUrl(1);
        Assert.False(await connect);
        Assert.True(await otherConnect);
        await Task.Delay(500);
        Assert.True(Mount.Connected, "the device was released under the other entry");
        Assert.True(other.IsConnected);
    }

    [Fact]
    public async Task A_connect_for_an_old_address_releases_the_old_device()
    {
        Mount.ConnectLatencyMs = 500;
        var server = NewServer();
        var connect = AlpacaConnector.ConnectAsync(server);
        await Task.Delay(100);
        server.Url = Fake.MountUrl(1);
        Assert.False(await connect);
        await Wait.UntilAsync(() => !Mount.Connected, TimeSpan.FromSeconds(2), "the old device released");
        Assert.False(Fake[1].Connected);
    }

    [Fact]
    public async Task A_connect_for_an_old_address_is_discarded()
    {
        Mount.ConnectLatencyMs = 500;
        var server = NewServer();
        var connect = AlpacaConnector.ConnectAsync(server);
        await Task.Delay(100);
        server.Url = Fake.MountUrl(1);
        Assert.False(await connect);
        Assert.False(server.IsConnected);
    }

    [Fact]
    public async Task Cancel_leaves_the_shared_state_alone()
    {
        Mount.ConnectLatencyMs = 2000;
        var server = NewServer();
        server.IsConnected = true; // say another tab connected it meanwhile
        server.ConnectRequested = true;
        using var cts = new CancellationTokenSource(200);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AlpacaConnector.ConnectAsync(server, cts.Token));
        Assert.True(server.IsConnected);
        Assert.False(server.IsConnecting);
    }

    [Fact]
    public async Task Disconnect_accepted()
    {
        var server = NewServer();
        await AlpacaConnector.ConnectAsync(server);
        Assert.False(await AlpacaConnector.DisconnectAsync(server));
        Assert.False(server.IsConnected || server.ConnectRequested || Mount.Connected);
    }

    [Fact]
    public async Task Disconnect_refused_stays_connected()
    {
        var server = NewServer();
        await AlpacaConnector.ConnectAsync(server);
        Mount.Disconnect = DisconnectMode.RefuseAndStayConnected;
        Assert.True(await AlpacaConnector.DisconnectAsync(server));
        Assert.True(server.IsConnected && server.ConnectRequested);
    }

    [Fact]
    public async Task Disconnect_with_an_error_that_did_disconnect()
    {
        var server = NewServer();
        await AlpacaConnector.ConnectAsync(server);
        Mount.Disconnect = DisconnectMode.ErrorButDisconnects;
        Assert.False(await AlpacaConnector.DisconnectAsync(server));
        Assert.False(server.IsConnected || server.ConnectRequested);
    }

    [Fact]
    public async Task Disconnect_without_an_answer_still_lets_go()
    {
        var server = NewServer();
        await AlpacaConnector.ConnectAsync(server);
        Mount.Down = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => AlpacaConnector.DisconnectAsync(server));
        Assert.False(server.IsConnected || server.ConnectRequested);
    }

    [Fact]
    public async Task Refresh_does_not_claim_a_device_this_app_didnt_connect()
    {
        Mount.Connected = true; // another program connected it
        var server = NewServer();
        Assert.False(await AlpacaConnector.RefreshConnectedAsync(server));
        Assert.False(server.IsConnected);
    }

    [Fact]
    public async Task Refresh_notices_another_program_disconnecting()
    {
        var server = NewServer();
        await AlpacaConnector.ConnectAsync(server);
        Mount.Connected = false;
        Assert.False(await AlpacaConnector.RefreshConnectedAsync(server));
        Assert.False(server.IsConnected);
        Assert.True(server.ConnectRequested); // still wanted: the view reconnects when it comes back
        Mount.Connected = true;
        Assert.True(await AlpacaConnector.RefreshConnectedAsync(server));
    }

    [Fact]
    public async Task Refresh_that_a_disconnect_overtook_keeps_the_newer_result()
    {
        var server = NewServer();
        await AlpacaConnector.ConnectAsync(server);
        Mount.LatencyMs = 400;
        var refresh = AlpacaConnector.RefreshConnectedAsync(server);
        await Task.Delay(50);
        server.ConnectRequested = false; // the user disconnected meanwhile
        server.IsConnected = false;
        Assert.False(await refresh);
        Assert.False(server.IsConnected);
    }

    [Fact]
    public async Task Refresh_of_a_server_that_is_down_is_false_without_throwing()
    {
        var server = NewServer();
        await AlpacaConnector.ConnectAsync(server);
        Mount.Down = true;
        Assert.False(await AlpacaConnector.RefreshConnectedAsync(server));
        Assert.False(server.IsConnected);
    }

    [Fact]
    public async Task Mount_info_is_read()
    {
        Mount.AlignmentMode = 0;
        Mount.Latitude = -33.9;
        var server = NewServer();
        Assert.True(await AlpacaConnector.ReadMountInfoAsync(server));
        Assert.Equal(AlignmentModes.algAltAz, server.AlignmentMode);
        Assert.Equal(-33.9, server.Latitude);
    }

    [Fact]
    public async Task Mount_info_not_implemented_keeps_the_defaults_and_isnt_asked_again()
    {
        Mount.AlignmentModeNotImplemented = Mount.LatitudeNotImplemented = true;
        var server = NewServer();
        Assert.True(await AlpacaConnector.ReadMountInfoAsync(server));
        Assert.Equal(AlignmentModes.algGermanPolar, server.AlignmentMode);
        Assert.Null(server.Latitude);
    }

    [Fact]
    public async Task Mount_info_answered_with_http_404_counts_as_not_implemented()
    {
        Mount.MountInfoHttp404 = true;
        Assert.True(await AlpacaConnector.ReadMountInfoAsync(NewServer()));
    }

    [Fact]
    public async Task An_unknown_alignment_mode_is_taken_as_german_polar()
    {
        Mount.AlignmentMode = 7;
        var server = NewServer();
        server.AlignmentMode = AlignmentModes.algAltAz;
        await AlpacaConnector.ReadMountInfoAsync(server);
        Assert.Equal(AlignmentModes.algGermanPolar, server.AlignmentMode);
    }

    [Fact]
    public async Task Mount_info_from_a_server_that_is_briefly_down_is_read_again()
    {
        // A 503 is a server that is (for now) not working, not a mount without the property: the read should be
        // tried again, or an Alt-Az mount is drawn as a German equatorial one until the user reconnects.
        Mount.Down = true;
        var server = NewServer();
        Assert.False(await AlpacaConnector.ReadMountInfoAsync(server));
    }

    [Fact]
    public async Task Mount_info_from_a_server_that_doesnt_answer_is_read_again()
    {
        var server = new AlpacaServer("http://127.0.0.1:1/api/v1/telescope/0/", "Nobody");
        await Assert.ThrowsAsync<HttpRequestException>(() => AlpacaConnector.ReadMountInfoAsync(server));
    }

    [Fact]
    public void Disconnect_question_names_the_server()
    {
        Assert.Contains("Fake 0", AlpacaConnector.DisconnectQuestion(NewServer()));
        Assert.Contains("ASCOM drivers", AlpacaConnector.DisconnectQuestion(new AlpacaServer("ascom:A.B", "Com")));
    }

    [Fact]
    public async Task Com_servers_are_refused_off_windows()
    {
        if (ComTelescopes.IsSupported) return;
        var server = new AlpacaServer("ascom:ASCOM.Simulator.Telescope", "Com");
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => AlpacaConnector.ConnectAsync(server));
        Assert.False(await AlpacaConnector.RefreshConnectedAsync(server));
        Assert.False(await AlpacaConnector.DisconnectAsync(server));
    }

    [Fact]
    public void Com_drivers_are_listed_on_windows()
    {
        // Read-only: lists the registered drivers; nothing is created or connected.
        if (!OperatingSystem.IsWindows()) return;
        var drivers = ComTelescopes.GetDrivers();
        Assert.All(drivers, d => Assert.False(string.IsNullOrWhiteSpace(d.ProgId)));
        Assert.All(drivers, d => Assert.False(string.IsNullOrWhiteSpace(d.Name)));
        Assert.Equal(drivers.Count, drivers.Select(d => d.ProgId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task Many_connects_at_once_to_one_server_end_connected()
    {
        var server = NewServer();
        Mount.ConnectLatencyMs = 100;
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => AlpacaConnector.ConnectAsync(server)));
        Assert.All(results, Assert.True);
        Assert.True(server.IsConnected && !server.IsConnecting);
    }
}
