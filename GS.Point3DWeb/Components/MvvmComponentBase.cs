using System.ComponentModel;
using GS.Point3DWeb.ViewModels;
using Microsoft.AspNetCore.Components;

namespace GS.Point3DWeb.Components;

/// <summary>
/// Base class for views bound to a view model. The view model is resolved from DI,
/// and the view re-renders whenever the view model raises PropertyChanged.
/// </summary>
public abstract class MvvmComponentBase<TViewModel> : LocalizedComponentBase, IDisposable
    where TViewModel : ViewModelBase
{
    [Inject]
    protected TViewModel ViewModel { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        ViewModel.Dispatcher = RunOnRendererAsync;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        await ViewModel.InitializeAsync();
    }

    /// <summary>
    /// Runs view-model work on this component's renderer. An exception is reported to this component
    /// (as if it had thrown it itself), rather than lost in a task nobody awaits.
    /// </summary>
    private async Task RunOnRendererAsync(Action action)
    {
        try
        {
            await InvokeAsync(action);
        }
        catch (Exception ex)
        {
            await DispatchExceptionAsync(ex);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => _ = InvokeAsync(StateHasChanged);

    public virtual void Dispose()
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        (ViewModel as IDisposable)?.Dispose();
        GC.SuppressFinalize(this);
    }
}
