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
        if (_detached) return; // the view is gone
        if (Dispatcher is { } dispatcher) _ = dispatcher(action); // it handles exceptions itself
        else action();
    }

    private volatile bool _detached;

    /// <summary>Called when the owning view is disposed: work for it from <see cref="OnRenderer"/> is dropped.</summary>
    internal void Detach() => _detached = true;

    /// <summary>
    /// Called once when the owning view is initialized, after <see cref="Dispatcher"/> is set.
    /// Override to load data or subscribe to shared events (through <see cref="OnRenderer"/>).
    /// </summary>
    public virtual Task InitializeAsync() => Task.CompletedTask;
}
