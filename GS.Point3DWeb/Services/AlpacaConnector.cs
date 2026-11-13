using GS.Alpaca.Shared;
using GS.Alpaca.Support;
using GS.Point3DWeb.Localization;
using GS.Point3DWeb.Models;
using GS.Point3DWeb.Support;
using System.Net;
using Newtonsoft.Json.Linq;

namespace GS.Point3DWeb.Services;

/// <summary>
/// Connect / disconnect an Alpaca telescope server without any UI.
/// Used by the toolbar Connect button and by the 3D views (connecting a server dragged onto a view, and their Connect / Disconnect button).
/// Every method leaves <see cref="AlpacaServer.IsConnected"/> set to what the server itself reports,
/// so the connection dots and buttons never show an assumed state.
/// A server that is an ASCOM COM driver on this computer (<see cref="AlpacaServer.IsCom"/>) is used through
/// <see cref="ComTelescopes"/> instead of over Alpaca.
/// </summary>
public static class AlpacaConnector
{
    private static ILogger Log => AppLog.For(nameof(AlpacaConnector));

    /// <summary>How a server is named in the log: its name and address.</summary>
    internal static string Describe(AlpacaServer server) => $"{server.ServerName} ({server.Url})";

    /// <summary>A client for the server's address, with its credentials (if any).</summary>
    public static AlpacaClient CreateClient(AlpacaServer server)
        => new(server.Url, server.ClientTransactionId, server.UserName, server.Password);

    /// <summary>
    /// Connects the server if it is not already connected. Returns the server's connected state afterwards.
    /// Throws with the server's error message if it refused to connect, or OperationCanceledException
    /// if <paramref name="token"/> is cancelled (the view was stopped). A COM driver's connect isn't cancelled:
    /// it always runs to the end (at most about 40 s for a driver that doesn't answer), so its outcome is
    /// recorded and the driver is never left connected with nothing holding it.
    /// </summary>
    public static async Task<bool> ConnectAsync(AlpacaServer server, CancellationToken token = default)
    {
        var name = Describe(server);
        Log.LogInformation("Connecting to {Server}", name);
        try
        {
            var (connected, discarded) = await ConnectCoreAsync(server, token);
            if (discarded)
                Log.LogInformation("Connect to {Server} finished after the server was deleted or its address changed; " +
                                   "its result was not used", name);
            else if (connected) Log.LogInformation("Connected to {Server}", name);
            else Log.LogWarning("Could not connect to {Server}: it reports not connected", name);
            return connected;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Log.LogInformation("Connect to {Server} stopped", name);
            throw;
        }
        catch (Exception ex)
        {
            Log.LogWarning(ex, "Connect to {Server} failed: {Error}", name, ex.Message);
            throw;
        }
    }

    /// <returns>Whether the server is now connected (false when the result was not used), and whether the
    /// result was not used: the server was deleted or its address changed meanwhile.</returns>
    private static async Task<(bool Connected, bool Discarded)> ConnectCoreAsync(AlpacaServer server, CancellationToken token)
    {
        var url = server.Url; // the result is for this address (see SetConnected)
        server.IsConnecting = true;
        try
        {
            if (server.IsCom)
            {
                if (!ComTelescopes.IsSupported) throw ComNotSupported();
                // Not cancelled: a driver that is starting can't be stopped, so the connect is seen through
                // and its outcome recorded, and the driver released again if the address changed meanwhile.
                // Otherwise a connect abandoned part way would leave the driver connected with nothing holding it.
                var comConnected = await ComTelescopes.ConnectAsync(server.ProgId!, server.IdGuid);
                // Deleted meanwhile, or now pointing at a different driver: this hold isn't wanted any more.
                if (server.Removed || !SetConnected(server, url, comConnected))
                {
                    if (comConnected) await ComTelescopes.ReleaseAndLogAsync(AlpacaServer.GetProgId(url)!, server.IdGuid);
                    return (false, true);
                }
                return (comConnected, false);
            }

            var client = CreateClient(server);

            var isConnected = await ReadConnectedAsync(client, token);
            server.SetClientTransactionId(client.ClientTransactionId);
            // Whether this connect is what connects the device (it wasn't connected already, for another program).
            var connectedHere = false;

            if (!isConnected)
            {
                var response = await client.ConnectAsync(token);
                isConnected = await ReadConnectedAsync(client, token);
                server.SetClientTransactionId(client.ClientTransactionId);
                connectedHere = isConnected;

                if (!isConnected && response is { ErrorNumber: not 0 })
                {
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(response.ErrorMessage)
                        ? $"the server returned error {response.ErrorNumber}"
                        : response.ErrorMessage);
                }
            }

            // Deleted meanwhile, or now pointing at a different device: this result isn't for it.
            var used = !server.Removed && SetConnected(server, url, isConnected);
            if (!used && connectedHere)
            {
                // Nothing will disconnect it now (the delete or the address change found no connection to end,
                // as this one was still under way), so release the device this connect connected, as for a COM
                // driver. A device that was already connected (another program's) is left alone.
                try
                {
                    await client.DisconnectAsync();
                    Log.LogInformation("Released {Url}: it was connected for a server that was deleted or moved meanwhile", url);
                }
                catch (Exception ex)
                {
                    Log.LogWarning(ex, "Could not release {Url} after its server was deleted or moved: {Error}", url, ex.Message);
                }
            }
            return (used && isConnected, !used);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The caller stopped. The server is shared, so leave its state alone:
            // another view or tab may have connected it meanwhile.
            throw;
        }
        catch
        {
            // The server is shared by every view and tab. If one of them connected it while this attempt
            // was failing, leave that connection alone (the regular connection check corrects the state if
            // the server is really down); otherwise record that this app isn't connected.
            if (!server.ConnectRequested) SetConnected(server, url, false);
            throw;
        }
        finally
        {
            server.IsConnecting = false;
        }
    }

    /// <summary>
    /// Records a connect's outcome: whether this app is connected, and so whether the server's connected
    /// state counts as this app's (<see cref="AlpacaServer.ConnectRequested"/>). Skipped, returning false, if
    /// the user changed the address to a different device meanwhile: the outcome is then for the old device.
    /// The comparison matches the edit dialog's (<see cref="AlpacaServer.SameUrl"/>), so an edit it treats as
    /// the same device (say, a ProgID's case) doesn't discard a connect it kept.
    /// </summary>
    private static bool SetConnected(AlpacaServer server, string url, bool connected)
    {
        if (!AlpacaServer.SameUrl(server.Url, url)) return false;
        server.ConnectRequested = connected;
        server.IsConnected = connected;
        return true;
    }

    /// <summary>
    /// Asks the server to disconnect. If it accepts, this app is disconnected, even though the device may
    /// stay connected for other programs (see <see cref="AlpacaServer.ConnectRequested"/>). If it returns
    /// an error, reads back whether it really disconnected. Returns true if the server is still connected
    /// (it refused and kept the connection open).
    /// </summary>
    public static async Task<bool> DisconnectAsync(AlpacaServer server)
    {
        var name = Describe(server);
        Log.LogInformation("Disconnecting from {Server}", name);
        try
        {
            var stillConnected = await DisconnectCoreAsync(server);
            if (stillConnected) Log.LogWarning("{Server} refused to disconnect and is still connected", name);
            else Log.LogInformation("Disconnected from {Server}", name);
            return stillConnected;
        }
        catch (Exception ex)
        {
            Log.LogWarning(ex, "Disconnect from {Server} failed: {Error}", name, ex.Message);
            throw;
        }
    }

    private static async Task<bool> DisconnectCoreAsync(AlpacaServer server)
    {
        if (server.IsCom)
        {
            // Releasing the driver always ends this app's connection; other programs have their own instances.
            try
            {
                if (ComTelescopes.IsSupported) await ComTelescopes.ReleaseAsync(server.ProgId!, server.IdGuid);
            }
            finally
            {
                server.ConnectRequested = false;
                server.IsConnected = false;
            }
            return false;
        }

        var client = CreateClient(server);
        bool accepted;
        try
        {
            accepted = await client.DisconnectAsync();
        }
        catch
        {
            // No answer (timeout, network error): the user still asked to disconnect, so stop counting the
            // device as this app's. Otherwise the connection check would show it connected again, and start
            // updating, as soon as the server answers.
            server.ConnectRequested = false;
            server.IsConnected = false;
            throw;
        }
        server.SetClientTransactionId(client.ClientTransactionId);

        if (accepted)
        {
            server.ConnectRequested = false;
            server.IsConnected = false;
            return false;
        }

        bool stillConnected;
        try
        {
            stillConnected = await ReadConnectedAsync(client);
            server.SetClientTransactionId(client.ClientTransactionId);
        }
        catch
        {
            stillConnected = false; // it stopped answering, so it isn't connected for us
        }

        // Not connected any more (whatever the error said): stop counting the device's state as ours.
        server.ConnectRequested = stillConnected;
        server.IsConnected = stillConnected;
        return stillConnected;
    }

    /// <summary>
    /// Reads the server's connected state into <see cref="AlpacaServer.IsConnected"/>.
    /// A server that doesn't answer counts as not connected, and so does one the user disconnected
    /// (checked after the request, so a check already under way when they disconnect can't undo it), and
    /// a check that a connect or disconnect overtook leaves that newer result alone.
    /// Never throws, except OperationCanceledException when <paramref name="token"/> is cancelled (the view
    /// stopped); the server's state is then left alone, as it is shared with other views and tabs.
    /// </summary>
    public static async Task<bool> RefreshConnectedAsync(AlpacaServer server, CancellationToken token = default)
    {
        var changesBefore = server.ConnectionChanges;
        bool connected;
        try
        {
            if (server.IsCom)
            {
                connected = ComTelescopes.IsSupported
                            && await ComTelescopes.IsConnectedAsync(server.ProgId!, server.IdGuid).WaitAsync(token);
            }
            else
            {
                var client = CreateClient(server);
                connected = await ReadConnectedAsync(client, token);
                server.SetClientTransactionId(client.ClientTransactionId);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            connected = false;
        }
        // A connect or disconnect finished while this was asking: its result is newer than this answer.
        if (server.ConnectionChanges != changesBefore) return server.IsConnected;
        if (!server.ConnectRequested) connected = false;

        server.IsConnected = connected;
        return connected;
    }

    /// <summary>
    /// Reads the mount's alignment mode and site latitude into the server, as GSServer uses
    /// them to pick the model and tilt the mount. Each is applied on its own, so a mount that
    /// doesn't implement SiteLatitude still gets the right model. Returns true once the mount has
    /// answered both requests (even with an error, which won't change on a retry), so they are read
    /// once per connection; false if a request got no answer, so it is tried again.
    /// </summary>
    public static async Task<bool> ReadMountInfoAsync(AlpacaServer server, CancellationToken token = default)
    {
        if (server.IsCom)
        {
            if (!ComTelescopes.IsSupported) throw ComNotSupported();
            var (comMode, comLatitude) = await ComTelescopes.ReadMountInfoAsync(server.ProgId!).WaitAsync(token);
            if (comMode is { } m)
                server.AlignmentMode = Enum.IsDefined(typeof(AlignmentModes), m) ? (AlignmentModes)m : AlignmentModes.algGermanPolar;
            if (comLatitude is { } lat) server.Latitude = lat;
            LogMountInfo(server, comMode is not null, comLatitude is not null);
            return true; // a property the driver doesn't implement won't appear on a retry
        }

        var client = CreateClient(server);

        var mode = await GetOptionalPropertyAsync(client, "alignmentmode", token);
        var latitude = await GetOptionalPropertyAsync(client, "sitelatitude", token);
        server.SetClientTransactionId(client.ClientTransactionId);

        if (mode is { ErrorNumber: 0, Value.Type: JTokenType.Integer or JTokenType.Float })
        {
            var modeValue = mode.Value.ToObject<int>();
            server.AlignmentMode = Enum.IsDefined(typeof(AlignmentModes), modeValue)
                ? (AlignmentModes)modeValue
                : AlignmentModes.algGermanPolar;
        }
        if (latitude is { ErrorNumber: 0, Value.Type: JTokenType.Integer or JTokenType.Float })
            server.Latitude = latitude.Value.ToObject<double>();

        var answered = mode is not null && latitude is not null;
        if (answered)
            LogMountInfo(server, mode is { ErrorNumber: 0 }, latitude is { ErrorNumber: 0 });
        return answered;
    }

    private static void LogMountInfo(AlpacaServer server, bool modeRead, bool latitudeRead)
    {
        Log.LogInformation("{Server}: alignment mode {Mode}{ModeNote}, latitude {Latitude}",
            server.ServerName, server.AlignmentMode, modeRead ? "" : " (not reported; assumed)",
            latitudeRead ? $"{server.Latitude:0.###}°" : "not reported");
    }


    /// <summary>
    /// A property that a mount may not implement. A server that answers it with an HTTP error saying the method
    /// doesn't exist (some answer 400 or 404 instead of Alpaca's NotImplemented) counts as having answered
    /// "not implemented", so the model still loads with what the mount did report. Any other HTTP error (a 500 or
    /// 503: the server isn't working right now) is no answer (null), so it is asked again; that isn't a property
    /// the mount lacks. No answer at all (a network error) still throws.
    /// </summary>
    private static async Task<AlpacaResponse?> GetOptionalPropertyAsync(AlpacaClient client, string method, CancellationToken token)
    {
        try
        {
            return await client.GetPropertyAsync(method, token);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound
                                                  or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented)
        {
            return new AlpacaResponse { Value = JValue.CreateNull(), ErrorNumber = AlpacaClient.NotImplementedError, ErrorMessage = ex.Message };
        }
        catch (HttpRequestException ex) when (ex.StatusCode is not null)
        {
            return null;
        }
    }

    /// <summary>
    /// The mount's position from a COM driver (an Alpaca server's is polled by the view with
    /// <see cref="AlpacaClient"/>). Throws if the driver fails or doesn't answer.
    /// </summary>
    public static Task<TelescopeDeviceState> ReadComStateAsync(AlpacaServer server, CancellationToken token = default)
    {
        if (!ComTelescopes.IsSupported) return Task.FromException<TelescopeDeviceState>(ComNotSupported());
        return ComTelescopes.ReadStateAsync(server.ProgId!).WaitAsync(token);
    }

    /// <summary>Asks before disconnecting: what disconnecting may do to other programs using the mount.</summary>
    public static string DisconnectQuestion(AlpacaServer server)
        => server.IsCom
            ? L.T("Disconnect from {0}? Its views stop updating, and some ASCOM drivers disconnect the mount for every program using them.", server.ServerName)
            : L.T("Disconnect from {0}? Its views stop updating, and other programs using this mount's Alpaca device may be disconnected too.", server.ServerName);

    /// <summary>The message for a COM driver when this app isn't running on Windows.</summary>
    public static string ComNotSupportedMessage => L.T("ASCOM drivers can only be used when this app runs on Windows.");

    private static PlatformNotSupportedException ComNotSupported() => new(ComNotSupportedMessage);

    /// <summary>The server's "connected" property; false if it returned an error or no value.</summary>
    private static async Task<bool> ReadConnectedAsync(AlpacaClient client, CancellationToken token = default)
    {
        var response = await client.GetPropertyAsync("connected", token);
        return response is { ErrorNumber: 0, Value: { Type: not JTokenType.Null } value }
               && value.ToObject<bool>();
    }
}
