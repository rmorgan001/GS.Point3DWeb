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
    /// Called once when the owning view is initialized. Override to load data.
    /// </summary>
    public virtual Task InitializeAsync() => Task.CompletedTask;
}
