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
