using CommunityToolkit.Mvvm.ComponentModel;

namespace GS.Point3DWeb.ViewModels;

/// <summary>
/// Base class for all view models. Raises PropertyChanged so bound views re-render.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>
    /// Runs work on the owning view's renderer; set by <see cref="Components.MvvmComponentBase{TViewModel}"/>
    /// before <see cref="InitializeAsync"/>. It reports any exception to that view, so none is lost.
    /// </summary>
    internal Func<Action, Task>? Dispatcher { get; set; }

    /// <summary>
    /// Runs work on this view's renderer. Shared (singleton) services raise their events on the thread of
    /// the browser tab that made the change; handlers for them use this, so another tab's change updates
    /// this view between its renders, not in the middle of one.
    /// </summary>
    protected void OnRenderer(Action action)
    {
        if (Dispatcher is { } dispatcher) _ = dispatcher(action); // it handles exceptions itself
        else action();
    }

    /// <summary>
    /// Called once when the owning view is initialized, after <see cref="Dispatcher"/> is set.
    /// Override to load data or subscribe to shared events (through <see cref="OnRenderer"/>).
    /// </summary>
    public virtual Task InitializeAsync() => Task.CompletedTask;
}
