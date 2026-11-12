using CommunityToolkit.Mvvm.ComponentModel;
using GS.Point3DWeb.ViewModels;

namespace GS.Point3DWeb.Dialogs;

/// <summary>
/// Base class for dialog view models. Mirrors GS.Alpaca.Client.Dialog.DialogViewModel.
/// </summary>
public abstract partial class DialogViewModel : ViewModelBase
{
    private TaskCompletionSource _closeTask = new(TaskCreationOptions.RunContinuationsAsynchronously);

    [ObservableProperty]
    public partial bool IsDialogOpen { get; set; }

    public Task WaitAsync() => _closeTask.Task;

    public void Show()
    {
        if (_closeTask.Task.IsCompleted)
            _closeTask = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IsDialogOpen = true;
    }

    public void Close()
    {
        IsDialogOpen = false;
        _closeTask.TrySetResult();
    }
}
