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
    [ObservableProperty] public partial string Title { get; set; } = L.T("Alpaca Discovery");
    [ObservableProperty] public partial string Message { get; set; } = L.T("Click Search…");
    [ObservableProperty] public partial string AcceptText { get; set; } = L.T("Accept");
    [ObservableProperty] public partial string CancelText { get; set; } = L.T("Cancel");
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
        Message = L.T("Discovering…");
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
            Message = telescopes.Count > 0 ? L.T("Select an Alpaca telescope server.") : L.T("Nothing found.");
        }
        catch (Exception e)
        {
            Message = L.T("Nothing found.");
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
