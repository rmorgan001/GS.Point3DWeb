using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GS.Point3DWeb.Localization;
using GS.Point3DWeb.Services;

namespace GS.Point3DWeb.Dialogs;

/// <summary>
/// Lists the ASCOM telescope drivers registered on the computer running this app, to add one as a server
/// used through COM. Takes the place of the ASCOM Chooser, which would open on that computer's desktop
/// rather than in the browser.
/// </summary>
public partial class ComDriversDialogViewModel : DialogViewModel
{
    public ComDriversDialogViewModel()
    {
        if (!ComTelescopes.IsSupported)
        {
            Message = L.T("ASCOM drivers can only be used when this app runs on Windows.");
            return;
        }

        try
        {
            Drivers = ComTelescopes.GetDrivers();
            Message = Drivers.Count > 0
                ? L.T("Select an ASCOM telescope driver on the computer running this app.")
                : L.T("No ASCOM telescope drivers are installed on the computer running this app.");
        }
        catch (Exception e)
        {
            Message = L.T("Could not read the ASCOM drivers: {0}", e.Message);
        }
    }

    public string Title => L.T("ASCOM Drivers");

    public string Message { get; }

    /// <summary>The registered telescope drivers, by name.</summary>
    public IReadOnlyList<(string ProgId, string Name)> Drivers { get; } = [];

    [ObservableProperty] public partial string? SelectedProgId { get; set; }

    [ObservableProperty] public partial bool Confirmed { get; set; }

    /// <summary>The selected driver's name.</summary>
    public string? SelectedName => Drivers.FirstOrDefault(d => d.ProgId == SelectedProgId).Name;

    [RelayCommand]
    private Task ConfirmAsync()
    {
        if (SelectedProgId is null) return Task.CompletedTask;
        Confirmed = true;
        Close();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task CancelAsync()
    {
        Confirmed = false;
        Close();
        return Task.CompletedTask;
    }
}
