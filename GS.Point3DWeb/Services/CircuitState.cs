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
