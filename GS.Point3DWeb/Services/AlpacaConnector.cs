using GS.Alpaca.Shared;
using GS.Alpaca.Support;
using GS.Point3DWeb.Models;
using Newtonsoft.Json.Linq;

namespace GS.Point3DWeb.Services;

/// <summary>
/// Connect / disconnect an Alpaca telescope server without any UI.
/// Used by the toolbar Connect button and by the 3D views (auto-connect and their Connect / Disconnect button).
/// Every method leaves <see cref="AlpacaServer.IsConnected"/> set to what the server itself reports,
/// so the connection dots and buttons never show an assumed state.
/// </summary>
public static class AlpacaConnector
{
    /// <summary>
    /// Connects the server if it is not already connected. Returns the server's connected state afterwards.
    /// Throws with the server's error message if it refused to connect.
    /// </summary>
    public static async Task<bool> ConnectAsync(AlpacaServer server)
    {
        try
        {
            var client = new AlpacaClient(server.Url, server.ClientTransactionId);

            var isConnected = await ReadConnectedAsync(client);
            server.SetClientTransactionId(client.ClientTransactionId);

            if (!isConnected)
            {
                var response = await client.ConnectAsync();
                isConnected = await ReadConnectedAsync(client);
                server.SetClientTransactionId(client.ClientTransactionId);

                if (!isConnected && response is { ErrorNumber: not 0 })
                {
                    server.IsConnected = false;
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(response.ErrorMessage)
                        ? $"the server returned error {response.ErrorNumber}"
                        : response.ErrorMessage);
                }
            }

            server.IsConnected = isConnected;
            return isConnected;
        }
        catch
        {
            server.IsConnected = false;
            throw;
        }
    }

    /// <summary>
    /// Asks the server to disconnect, then reads back whether it really did.
    /// Returns true if the server is still connected (it kept the connection open).
    /// </summary>
    public static async Task<bool> DisconnectAsync(AlpacaServer server)
    {
        var client = new AlpacaClient(server.Url, server.ClientTransactionId);
        await client.DisconnectAsync();
        server.SetClientTransactionId(client.ClientTransactionId);

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

        server.IsConnected = stillConnected;
        return stillConnected;
    }

    /// <summary>
    /// Reads the server's connected state into <see cref="AlpacaServer.IsConnected"/>.
    /// A server that doesn't answer counts as not connected. Never throws.
    /// </summary>
    public static async Task<bool> RefreshConnectedAsync(AlpacaServer server)
    {
        bool connected;
        try
        {
            var client = new AlpacaClient(server.Url, server.ClientTransactionId);
            connected = await ReadConnectedAsync(client);
            server.SetClientTransactionId(client.ClientTransactionId);
        }
        catch
        {
            connected = false;
        }

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
    public static async Task<bool> ReadMountInfoAsync(AlpacaServer server)
    {
        var client = new AlpacaClient(server.Url, server.ClientTransactionId);

        var mode = await client.GetPropertyAsync("alignmentmode");
        var latitude = await client.GetPropertyAsync("sitelatitude");
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

    /// <summary>The server's "connected" property; false if it returned an error or no value.</summary>
    private static async Task<bool> ReadConnectedAsync(AlpacaClient client)
    {
        var response = await client.GetPropertyAsync("connected");
        return response is { ErrorNumber: 0, Value: { Type: not JTokenType.Null } value }
               && value.ToObject<bool>();
    }
}
