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
using GS.Point3DWeb.Services;

namespace GS.Point3DWeb.Dialogs;

/// <summary>Shows the feature-key status and lets the user add or remove a key.</summary>
public partial class FeatureKeyDialogViewModel(FeatureKeyService featureKeys) : DialogViewModel
{
    public FeatureKeyService FeatureKeys { get; } = featureKeys;

    [ObservableProperty] public partial string Title { get; set; } = L.T("Feature key");

    /// <summary>Optional reason the dialog was opened (e.g. a limit was reached).</summary>
    [ObservableProperty] public partial string? Reason { get; set; }

    [ObservableProperty] public partial string KeyText { get; set; } = "";

    [ObservableProperty] public partial string? Error { get; set; }

    [ObservableProperty] public partial string? Message { get; set; }

    [RelayCommand]
    private void Apply()
    {
        Message = null;
        if (!FeatureKeys.TryApply(KeyText, out var error))
        {
            Error = error;
            return;
        }

        Error = null;
        KeyText = "";
        Reason = null;
        Message = L.T("Key added. All views and servers are unlocked.");
    }

    [RelayCommand]
    private void Remove()
    {
        if (!FeatureKeys.TryRemove(out var error))
        {
            Error = error;
            Message = null;
            return;
        }
        Error = null;
        Message = L.T("Key removed. Limited to {0} views and {1} servers.", FeatureKeyService.FreeMaxViews, FeatureKeyService.FreeMaxServers);
    }

    [RelayCommand]
    private void CloseDialog() => Close();
}
