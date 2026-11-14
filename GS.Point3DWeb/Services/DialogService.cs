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

using GS.Point3DWeb.Dialogs;

namespace GS.Point3DWeb.Services;

/// <summary>
/// Shows dialogs for one browser connection (registered as scoped, so one per circuit).
/// View models call <see cref="ShowDialog"/> and await it; the DialogHost component renders
/// <see cref="Current"/>. Mirrors GS.Alpaca.Client.Singletons.DialogService.ShowDialog.
/// </summary>
public class DialogService
{
    public DialogViewModel? Current { get; private set; }

    public event Action? DialogChanged;

    // One dialog at a time: a dialog asked for while another is open (say, a failed connect while the user
    // is editing a server) waits its turn. Replacing the open one would leave whoever is waiting on it
    // waiting for ever.
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    /// <summary>Shows the dialog once any open one has closed, and completes when it closes.</summary>
    public async Task ShowDialog(DialogViewModel dialogViewModel)
    {
        await _oneAtATime.WaitAsync();
        try
        {
            Current = dialogViewModel;
            dialogViewModel.Show();
            DialogChanged?.Invoke();
            await dialogViewModel.WaitAsync();
        }
        finally
        {
            Current = null;
            DialogChanged?.Invoke();
            _oneAtATime.Release();
        }
    }
}
