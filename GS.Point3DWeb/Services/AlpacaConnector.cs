using GS.Alpaca.Shared;
using GS.Alpaca.Support;
using GS.Point3DWeb.Localization;
using GS.Point3DWeb.Models;
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
    /// <summary>A client for the server's address, with its credentials (if any).</summary>
    public static AlpacaClient CreateClient(AlpacaServer server)
        => new(server.Url, server.ClientTransactionId, server.UserName, server.Password);

    /// <summary>
    /// Connects the server if it is not already connected. Returns the server's connected state afterwards.
    /// Throws with the server's error message if it refused to connect, or OperationCanceledException
    /// if <paramref name="token"/> is cancelled (the view was stopped).
    /// </summary>
    public static async Task<bool> ConnectAsync(AlpacaServer server, CancellationToken token = default)
    {
        var url = server.Url; // the result is for this address (see SetConnected)
        server.IsConnecting = true;
        try
        {
            if (server.IsCom)
            {
                if (!ComTelescopes.IsSupported) throw ComNotSupported();
                var comConnected = await ComTelescopes.ConnectAsync(server.ProgId!).WaitAsync(token);
                SetConnected(server, url, comConnected);
                return comConnected;
            }

            var client = CreateClient(server);

            var isConnected = await ReadConnectedAsync(client, token);
            server.SetClientTransactionId(client.ClientTransactionId);

            if (!isConnected)
            {
                var response = await client.ConnectAsync(token);
                isConnected = await ReadConnectedAsync(client, token);
                server.SetClientTransactionId(client.ClientTransactionId);

                if (!isConnected && response is { ErrorNumber: not 0 })
                {
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(response.ErrorMessage)
                        ? $"the server returned error {response.ErrorNumber}"
                        : response.ErrorMessage);
                }
            }

            SetConnected(server, url, isConnected);
            return isConnected;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The caller stopped. The server is shared, so leave its state alone:
            // another view or tab may have connected it meanwhile.
            throw;
        }
        catch
        {
            SetConnected(server, url, false);
            throw;
        }
        finally
        {
            server.IsConnecting = false;
        }
    }

    /// <summary>
    /// Records a connect's outcome: whether this app is connected, and so whether the server's connected
    /// state counts as this app's (<see cref="AlpacaServer.ConnectRequested"/>). Skipped if the user changed
    /// the address meanwhile: the outcome is then for the old device, not the new one.
    /// </summary>
    private static void SetConnected(AlpacaServer server, string url, bool connected)
    {
        if (!string.Equals(server.Url, url, StringComparison.Ordinal)) return;
        server.ConnectRequested = connected;
        server.IsConnected = connected;
    }

    /// <summary>
    /// Asks the server to disconnect. If it accepts, this app is disconnected, even though the device may
    /// stay connected for other programs (see <see cref="AlpacaServer.ConnectRequested"/>). If it returns
    /// an error, reads back whether it really disconnected. Returns true if the server is still connected
    /// (it refused and kept the connection open).
    /// </summary>
    public static async Task<bool> DisconnectAsync(AlpacaServer server)
    {
        if (server.IsCom)
        {
            // Releasing the driver always ends this app's connection; other programs have their own instances.
            try
            {
                if (ComTelescopes.IsSupported) await ComTelescopes.DisconnectAsync(server.ProgId!);
            }
            finally
            {
                server.ConnectRequested = false;
                server.IsConnected = false;
            }
            return false;
        }

        var client = CreateClient(server);
        var accepted = await client.DisconnectAsync();
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
    /// (checked after the request, so a check already under way when they disconnect can't undo it).
    /// Never throws, except OperationCanceledException when <paramref name="token"/> is cancelled (the view
    /// stopped); the server's state is then left alone, as it is shared with other views and tabs.
    /// </summary>
    public static async Task<bool> RefreshConnectedAsync(AlpacaServer server, CancellationToken token = default)
    {
        bool connected;
        try
        {
            if (server.IsCom)
            {
                connected = ComTelescopes.IsSupported
                            && await ComTelescopes.IsConnectedAsync(server.ProgId!).WaitAsync(token);
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
            return true; // a property the driver doesn't implement won't appear on a retry
        }

        var client = CreateClient(server);

        var mode = await client.GetPropertyAsync("alignmentmode", token);
        var latitude = await client.GetPropertyAsync("sitelatitude", token);
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

        return mode is not null && latitude is not null;
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

    /// <summary>The error for a COM driver when this app isn't running on Windows.</summary>
    private static PlatformNotSupportedException ComNotSupported()
        => new(L.T("ASCOM drivers can only be used when this app runs on Windows."));

    /// <summary>The server's "connected" property; false if it returned an error or no value.</summary>
    private static async Task<bool> ReadConnectedAsync(AlpacaClient client, CancellationToken token = default)
    {
        var response = await client.GetPropertyAsync("connected", token);
        return response is { ErrorNumber: 0, Value: { Type: not JTokenType.Null } value }
               && value.ToObject<bool>();
    }
}
