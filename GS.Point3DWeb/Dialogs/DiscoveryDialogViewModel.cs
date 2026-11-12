using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GS.Alpaca.Support;

namespace GS.Point3DWeb.Dialogs;

/// <summary>
/// Searches the network for Alpaca telescopes. Mirrors GS.Alpaca.Client.Dialog.DiscoveryDialogViewModel.
/// </summary>
public partial class DiscoveryDialogViewModel : DialogViewModel
{
    [ObservableProperty] public partial string Title { get; set; } = "Alpaca Discovery";
    [ObservableProperty] public partial string Message { get; set; } = "Click Search...";
    [ObservableProperty] public partial string AcceptText { get; set; } = "Accept";
    [ObservableProperty] public partial string CancelText { get; set; } = "Cancel";
    [ObservableProperty] public partial string StatusText { get; set; } = "";
    [ObservableProperty] public partial string ProgressText { get; set; } = "";
    [ObservableProperty] public partial bool CancelVisible { get; set; } = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool Busy { get; set; }

    [ObservableProperty] public partial bool Confirmed { get; set; }

    [ObservableProperty] public partial ObservableCollection<AlpacaTelescopeDevice> Devices { get; set; } = [];

    [ObservableProperty] public partial AlpacaTelescopeDevice? SelectedDevice { get; set; }

    public Func<DiscoveryDialogViewModel, Task<bool>> OnConfirm { get; set; } = _ => Task.FromResult(true);

    private bool NotBusy() => !Busy;

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (Busy) return;

        Busy = true;
        StatusText = "";
        Message = "Discovering...";
        Devices = [];
        SelectedDevice = null;

        try
        {
            var telescopes = await AlpacaDiscoveryService.DiscoverTelescopes();
            foreach (var scope in telescopes)
            {
                Devices.Add(new AlpacaTelescopeDevice
                {
                    DeviceName = scope.DeviceName,
                    DeviceUrl = scope.DeviceUrl,
                });
            }
            Message = telescopes.Count > 0 ? "Select an Alpaca Telescope Server.." : "Nothing found...";
        }
        catch (Exception e)
        {
            Message = "Nothing found...";
            StatusText = e.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        if (Busy) return;

        Busy = true;
        StatusText = "";
        ProgressText = "Processing...";

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
