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
