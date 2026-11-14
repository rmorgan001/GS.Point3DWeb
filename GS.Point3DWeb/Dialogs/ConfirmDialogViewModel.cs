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
