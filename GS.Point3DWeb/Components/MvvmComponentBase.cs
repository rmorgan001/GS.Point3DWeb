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

    [Inject]
    private ILoggerFactory LoggerFactory { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        ViewModel.Dispatcher = RunOnRendererAsync;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        await ViewModel.InitializeAsync();
    }

    /// <summary>
    /// Runs view-model work on this component's renderer. The work handles a change made elsewhere (another
    /// tab), so an exception is logged rather than lost, and doesn't end this tab's session for one missed
    /// update. Work arriving after the component is gone is dropped.
    /// </summary>
    private async Task RunOnRendererAsync(Action action)
    {
        try
        {
            await InvokeAsync(action);
        }
        catch (ObjectDisposedException)
        {
            // the page closed while the change was on its way
        }
        catch (Exception ex)
        {
            LoggerFactory.CreateLogger(GetType()).LogError(ex, "Applying a change from another tab failed");
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => _ = InvokeAsync(StateHasChanged);

    public virtual void Dispose()
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.Detach(); // an event already on its way is then dropped, not run on a disposed page
        (ViewModel as IDisposable)?.Dispose();
        GC.SuppressFinalize(this);
    }
}
