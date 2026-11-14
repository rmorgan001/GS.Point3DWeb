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

using GS.Point3DWeb.Localization;
using Microsoft.AspNetCore.Components;

namespace GS.Point3DWeb.Components;

/// <summary>
/// Base for components that show translated text and re-render from events raised on other threads
/// (shared servers, polling loops). Every re-render and <see cref="InvokeAsync(Action)"/> work item
/// runs in this browser's language rather than the language of whichever circuit raised the event.
/// </summary>
public abstract class LocalizedComponentBase : ComponentBase
{
    [Inject]
    private CircuitCulture Culture { get; set; } = default!;

    /// <summary>Called by StateHasChanged just before a re-render is queued; the render runs in the same flow.</summary>
    protected override bool ShouldRender()
    {
        Culture.Apply();
        return true;
    }

    /// <summary>Runs the work item on the renderer in this circuit's language.</summary>
    protected new Task InvokeAsync(Action workItem) => base.InvokeAsync(() =>
    {
        Culture.Apply();
        workItem();
    });

    /// <summary>Runs the work item on the renderer in this circuit's language.</summary>
    protected new Task InvokeAsync(Func<Task> workItem) => base.InvokeAsync(() =>
    {
        Culture.Apply();
        return workItem();
    });
}
