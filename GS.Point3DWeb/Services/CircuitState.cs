using Microsoft.AspNetCore.Components.Server.Circuits;

namespace GS.Point3DWeb.Services;

/// <summary>
/// Whether this browser connection (Blazor circuit) can be reached right now (registered as scoped, and as the
/// circuit's handler). When a browser goes without saying goodbye (killed, a phone asleep, the network dropped),
/// Blazor keeps its page for a few minutes in case it comes back; its 3D views stop asking their mounts meanwhile
/// (<see cref="ViewModels.Model3DViewModel"/>), and carry on if it does come back.
/// </summary>
public sealed class CircuitState : CircuitHandler
{
    private volatile bool _connected = true;

    public bool IsConnected => _connected;

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _connected = false;
        return Task.CompletedTask;
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _connected = true;
        return Task.CompletedTask;
    }
}
