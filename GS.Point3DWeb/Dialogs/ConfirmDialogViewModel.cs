using GS.Point3DWeb.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GS.Point3DWeb.Dialogs;

/// <summary>
/// Accept/Cancel dialog. Mirrors GS.Alpaca.Client.Dialog.ConfirmDialogViewModel.
/// </summary>
public partial class ConfirmDialogViewModel : DialogViewModel
{
    [ObservableProperty] public partial string Title { get; set; } = L.T("Confirm");
    [ObservableProperty] public partial string Message { get; set; } = L.T("Are you sure?");
    [ObservableProperty] public partial string AcceptText { get; set; } = L.T("Accept");
    [ObservableProperty] public partial string CancelText { get; set; } = L.T("Cancel");
    [ObservableProperty] public partial string StatusText { get; set; } = "";
    [ObservableProperty] public partial string ProgressText { get; set; } = "";
    [ObservableProperty] public partial bool CancelVisible { get; set; } = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool Busy { get; set; }

    [ObservableProperty] public partial bool Confirmed { get; set; }

    public Func<ConfirmDialogViewModel, Task<bool>> OnConfirm { get; set; } = _ => Task.FromResult(true);

    private bool NotBusy() => !Busy;

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (Busy) return;

        Busy = true;
        StatusText = "";
        ProgressText = L.T("Processing…");

        var result = await OnConfirm(this);

        Busy = false;
        if (!result) return;

        Confirmed = true;
        Close();
    }

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private Task CancelAsync()
    {
        Confirmed = false;
        Close();
        return Task.CompletedTask;
    }
}
