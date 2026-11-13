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
    /// <summary>Opens with "Loading…"; the drivers are read from the registry off the page's thread.</summary>
    public ComDriversDialogViewModel()
    {
        if (!ComTelescopes.IsSupported)
        {
            Message = AlpacaConnector.ComNotSupportedMessage;
            return;
        }

        Message = L.T("Loading…");
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            // (IsSupported was checked in the constructor; the analyzer needs to see it here too.)
            var drivers = await Task.Run(() => OperatingSystem.IsWindows() ? ComTelescopes.GetDrivers() : []);
            Drivers = drivers;
            Message = drivers.Count > 0
                ? L.T("Select an ASCOM telescope driver on the computer running this app.")
                : L.T("No ASCOM telescope drivers are installed on the computer running this app.");
        }
        catch (Exception e)
        {
            Message = L.T("Could not read the ASCOM drivers: {0}", e.Message);
        }
    }

    public string Title => L.T("ASCOM Drivers");

    [ObservableProperty] public partial string Message { get; set; }

    /// <summary>The registered telescope drivers, by name.</summary>
    [ObservableProperty] public partial IReadOnlyList<(string ProgId, string Name)> Drivers { get; set; } = [];

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
