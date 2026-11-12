using GS.Alpaca.Shared;
using GS.Alpaca.Support;
using GS.Point3DWeb.Models;

namespace GS.Point3DWeb.Services;

/// <summary>
/// Connect / disconnect an Alpaca telescope server without any UI.
/// Used by the toolbar Connect button and by the 3D model quadrants (auto-connect).
/// </summary>
public static class AlpacaConnector
{
    /// <summary>Connects the server if it is not already connected. Returns the final state.</summary>
    public static async Task<bool> ConnectAsync(AlpacaServer server)
    {
        try
        {
            var client = new AlpacaClient(server.Url, server.ClientTransactionId);

            var response = await client.GetPropertyAsync("connected");
            server.SetClientTransactionId(client.ClientTransactionId);
            var isConnected = response?.Value.ToObject<bool>() == true;

            if (!isConnected)
            {
                await client.ConnectAsync();
                response = await client.GetPropertyAsync("connected");
                server.SetClientTransactionId(client.ClientTransactionId);
                isConnected = response?.Value.ToObject<bool>() == true;
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
    /// Reads the mount's alignment mode and site latitude into the server, as GSServer uses
    /// them to pick the model and tilt the mount. Returns false if the mount didn't answer.
    /// </summary>
    public static async Task<bool> ReadMountInfoAsync(AlpacaServer server)
    {
        var client = new AlpacaClient(server.Url, server.ClientTransactionId);

        var mode = await client.GetPropertyAsync("alignmentmode");
        var latitude = await client.GetPropertyAsync("sitelatitude");
        server.SetClientTransactionId(client.ClientTransactionId);

        if (mode is not { ErrorNumber: 0 } || latitude is not { ErrorNumber: 0 }) return false;

        var modeValue = mode.Value.ToObject<int>();
        server.AlignmentMode = Enum.IsDefined(typeof(AlignmentModes), modeValue)
            ? (AlignmentModes)modeValue
            : AlignmentModes.algGermanPolar;
        server.Latitude = latitude.Value.ToObject<double>();
        return true;
    }

    public static async Task DisconnectAsync(AlpacaServer server)
    {
        var client = new AlpacaClient(server.Url, server.ClientTransactionId);
        await client.DisconnectAsync();
        server.SetClientTransactionId(client.ClientTransactionId);
        server.IsConnected = false;
    }
}
