using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GS.Alpaca.Support;
using GS.Point3DWeb.Dialogs;
using GS.Point3DWeb.Models;
using GS.Point3DWeb.Services;

namespace GS.Point3DWeb.ViewModels;

/// <summary>
/// Home page view model. The server list, selection and toolbar commands follow the
/// Connection Manager (ConnectPageViewModel) in GS.Alpaca.Client.
/// </summary>
public partial class HomeViewModel : ViewModelBase
{
    private const string SimulatorName = "ASCOM Alpaca Simulators";
    private const string DefaultUrl = "http://127.0.0.1:32323/api/v1/telescope/0/";

    private readonly SettingsService _settingsService;
    private readonly DialogService _dialogService;

    public HomeViewModel(SettingsService settingsService, DialogService dialogService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

        GetStoredConnections();
        Count = AlpacaServers.Count;
        SelectedAlpacaServer = _settingsService.SelectedAlpacaServer ?? AlpacaServers.FirstOrDefault();
    }

    [ObservableProperty]
    public partial ObservableCollection<AlpacaServer> AlpacaServers { get; set; } = [];

    [ObservableProperty]
    public partial AlpacaServer? SelectedAlpacaServer { get; set; }

    [ObservableProperty]
    public partial int Count { get; set; }

    partial void OnSelectedAlpacaServerChanged(AlpacaServer? value)
        => _settingsService.SelectedAlpacaServer = value;

    [RelayCommand]
    private void SelectServer(AlpacaServer? server) => SelectedAlpacaServer = server;

    /// <summary>Opens the edit dialog for a server; changes apply only on Accept.</summary>
    [RelayCommand]
    private async Task EditServerAsync(AlpacaServer? server)
    {
        if (server is null) return;

        SelectedAlpacaServer = server;
        var editViewModel = new EditServerDialogViewModel(server);
        await _dialogService.ShowDialog(editViewModel);

        if (editViewModel.Confirmed) editViewModel.ApplyTo(server);
    }

    // ===== Toolbar commands =====

    /// <summary>Adds a blank connection pointing at the local simulator URL.</summary>
    [RelayCommand]
    private void Add()
    {
        var connection = new AlpacaServer(DefaultUrl, "Blank")
        {
            IsConnected = false,
            UserName = null,
            Password = null
        };
        AlpacaServers.Add(connection);
        SelectedAlpacaServer = connection;
        Count = AlpacaServers.Count;
    }

    /// <summary>Searches the network for Alpaca telescopes and adds the one picked.</summary>
    [RelayCommand]
    private async Task DiscoveryAsync()
    {
        var discoveryViewModel = new DiscoveryDialogViewModel();
        await _dialogService.ShowDialog(discoveryViewModel);

        if (!discoveryViewModel.Confirmed) return;

        var selection = discoveryViewModel.SelectedDevice;
        if (selection is { DeviceUrl: not null, DeviceName: not null })
        {
            var connection = new AlpacaServer(selection.DeviceUrl, selection.DeviceName)
            {
                IsConnected = false,
                UserName = null,
                Password = null
            };
            AlpacaServers.Add(connection);
            SelectedAlpacaServer = connection;
            Count = AlpacaServers.Count;
        }
    }

    /// <summary>Connects to the selected server, or disconnects it if it is already connected.</summary>
    [RelayCommand]
    private async Task ConnectAsync()
    {
        var server = SelectedAlpacaServer;
        if (server is null)
        {
            await _dialogService.ShowDialog(new ConfirmDialogViewModel
            {
                Message = "Nothing Selected",
                CancelVisible = false
            });
            return;
        }

        var confirmViewModel = new ConfirmDialogViewModel
        {
            Message = server.IsConnected
                ? $"Disconnect from {server.ServerName}?"
                : $"Connect to {server.ServerName}?"
        };
        await _dialogService.ShowDialog(confirmViewModel);
        if (!confirmViewModel.Confirmed) return;

        IsBusy = true;
        try
        {
            var client = new AlpacaClient(server.Url, server.ClientTransactionId);

            if (server.IsConnected)
            {
                await client.DisconnectAsync();
                server.SetClientTransactionId(client.ClientTransactionId);
                server.IsConnected = false;
                return;
            }

            var response = await client.GetPropertyAsync("connected");
            server.SetClientTransactionId(client.ClientTransactionId);
            var isConnected = response?.Value.ToObject<bool>() == true;

            if (!isConnected)
            {
                await client.ConnectAsync();
                response = await client.GetPropertyAsync("connected");
                server.SetClientTransactionId(client.ClientTransactionId);
                isConnected = response?.Value.ToObject<bool>() == true;
            }

            server.IsConnected = isConnected;
            if (isConnected)
            {
                _settingsService.SelectedAlpacaServer = server;
            }

            await _dialogService.ShowDialog(new ConfirmDialogViewModel
            {
                Message = $"Connected: {isConnected}",
                CancelVisible = false
            });
        }
        catch (Exception e)
        {
            server.IsConnected = false;
            await _dialogService.ShowDialog(new ConfirmDialogViewModel
            {
                Message = e.Message,
                CancelVisible = false
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Saves the server list to disk.</summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        var confirmViewModel = new ConfirmDialogViewModel { Message = "Save the server list?" };
        await _dialogService.ShowDialog(confirmViewModel);
        if (!confirmViewModel.Confirmed) return;

        try
        {
            _settingsService.SelectedAlpacaServer = SelectedAlpacaServer;
            _settingsService.AlpacaServers = AlpacaServers;
            _settingsService.Save();
        }
        catch (Exception e)
        {
            await _dialogService.ShowDialog(new ConfirmDialogViewModel
            {
                Message = $"Could not save: {e.Message}",
                CancelVisible = false
            });
        }
    }

    /// <summary>Removes the given server from the list.</summary>
    [RelayCommand]
    private async Task DeleteAsync(AlpacaServer? connection)
    {
        if (connection is null) return;

        var confirmViewModel = new ConfirmDialogViewModel { Message = $"Delete {connection.ServerName}?" };
        await _dialogService.ShowDialog(confirmViewModel);
        if (!confirmViewModel.Confirmed) return;

        var item = AlpacaServers.FirstOrDefault(i => i.IdGuid == connection.IdGuid);
        if (item is not null) AlpacaServers.Remove(item);

        SelectedAlpacaServer = AlpacaServers.LastOrDefault();
        Count = AlpacaServers.Count;
    }

    // ===== Helpers =====

    /// <summary>Loads the stored connections and selects the first one, if any.</summary>
    private void GetStoredConnections()
    {
        AlpacaServers = _settingsService.AlpacaServers;
        if (AlpacaServers.Count > 0)
        {
            SelectedAlpacaServer = AlpacaServers.First();
        }
        AutoAddComm();
    }

    /// <summary>Adds the default simulator connection when it is missing.</summary>
    private void AutoAddComm()
    {
        if (AlpacaServers.Any(i => i.ServerName == SimulatorName)) return;
        if (!_settingsService.AutoAddComm) return;

        var connection = new AlpacaServer(DefaultUrl, SimulatorName)
        {
            IsConnected = false,
            UserName = null,
            Password = null
        };
        AlpacaServers.Add(connection);
        SelectedAlpacaServer = connection;
        Count = AlpacaServers.Count;
    }
}
