using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GS.Point3DWeb.Dialogs;
using GS.Point3DWeb.Localization;
using GS.Point3DWeb.Models;
using GS.Point3DWeb.Services;

namespace GS.Point3DWeb.ViewModels;

/// <summary>
/// Home page view model. The server list, selection and toolbar commands follow the
/// Connection Manager (ConnectPageViewModel) in GS.Alpaca.Client.
/// </summary>
public partial class HomeViewModel : ViewModelBase, IDisposable
{
    private const string SimulatorName = "ASCOM Alpaca Simulators";
    private const string DefaultUrl = "http://127.0.0.1:32323/api/v1/telescope/0/";

    private readonly SettingsService _settingsService;
    private readonly DialogService _dialogService;

    public HomeViewModel(SettingsService settingsService, DialogService dialogService, FeatureKeyService featureKeys)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        FeatureKeys = featureKeys ?? throw new ArgumentNullException(nameof(featureKeys));
        FeatureKeys.Changed += OnFeatureKeyChanged;

        // Read the saved selection first: selecting a server (below) also stores it in the settings.
        var savedSelection = _settingsService.SelectedAlpacaServer;
        AutoAddComm();
        RestoreQuadrants();
        var servers = AlpacaServers;
        Count = servers.Length;
        SelectedAlpacaServer = savedSelection is not null && Array.IndexOf(servers, savedSelection) >= 0
            ? savedSelection
            : SelectedAlpacaServer ?? servers.FirstOrDefault();

        _settingsService.ServersChanged += OnServersChanged;
    }

    /// <summary>
    /// The server list (a copy: the list is shared by every browser tab, and changed through
    /// <see cref="SettingsService.AddServer"/> / <see cref="SettingsService.RemoveServer"/>).
    /// </summary>
    public AlpacaServer[] AlpacaServers => _settingsService.Servers;

    /// <summary>
    /// A server was added or removed, here or in another tab: refresh the count and list, and drop
    /// a removed server from the selection and the quadrants.
    /// </summary>
    private void OnServersChanged()
    {
        var servers = AlpacaServers;
        Count = servers.Length;
        if (SelectedAlpacaServer is { } selected && Array.IndexOf(servers, selected) < 0)
            SelectedAlpacaServer = servers.LastOrDefault();

        var cleared = false;
        foreach (var quadrant in Quadrants)
        {
            if (quadrant.Server is not { } server || Array.IndexOf(servers, server) >= 0) continue;
            quadrant.Server = null;
            quadrant.Flipped = false;
            cleared = true;
        }
        if (cleared)
        {
            StoreQuadrants();
            OnPropertyChanged(nameof(Quadrants));
        }
        OnPropertyChanged(nameof(AlpacaServers));
    }

    // ===== Feature key: 1 view and 5 servers without a key; 4 views and any number with one =====

    public FeatureKeyService FeatureKeys { get; }

    public bool IsLicensed => FeatureKeys.IsLicensed;

    public int MaxViews => FeatureKeys.MaxViews;

    public int MaxServers => FeatureKeys.MaxServers;

    /// <summary>
    /// Servers beyond the limit (e.g. added while a key was installed) stay in the list but
    /// can't be connected or shown until a key is added.
    /// </summary>
    public bool IsServerAllowed(AlpacaServer server)
    {
        var index = Array.IndexOf(AlpacaServers, server);
        return index >= 0 && index < MaxServers;
    }

    private void OnFeatureKeyChanged()
    {
        OnPropertyChanged(nameof(IsLicensed));
        OnPropertyChanged(nameof(ViewCount));
        OnPropertyChanged(nameof(Quadrants));
    }

    /// <summary>Opens the feature-key dialog (header key button, or after hitting a limit).</summary>
    [RelayCommand]
    private Task FeatureKeyAsync() => ShowFeatureKeyDialogAsync(null);

    private Task ShowFeatureKeyDialogAsync(string? reason)
        => _dialogService.ShowDialog(new FeatureKeyDialogViewModel(FeatureKeys) { Reason = reason });

    /// <summary>Explains a free-version limit and offers to enter a key.</summary>
    private async Task ShowLimitAsync(string message)
    {
        var limit = new ConfirmDialogViewModel
        {
            Title = L.T("Feature key needed"),
            Message = message,
            AcceptText = L.T("Enter a key"),
            CancelText = L.T("Close")
        };
        await _dialogService.ShowDialog(limit);
        if (limit.Confirmed) await ShowFeatureKeyDialogAsync(message);
    }

    private string ServerLimitMessage =>
        L.T("The free version allows up to {0} servers. A feature key unlocks more.", FeatureKeyService.FreeMaxServers);

    private static string BeyondLimitMessage(AlpacaServer server) =>
        L.T("{0} is beyond the free version's {1} servers. A feature key unlocks it.",
            server.ServerName, FeatureKeyService.FreeMaxServers);

    public void Dispose()
    {
        FeatureKeys.Changed -= OnFeatureKeyChanged;
        _settingsService.ServersChanged -= OnServersChanged;
        GC.SuppressFinalize(this);
    }

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

        if (!editViewModel.Confirmed) return;

        editViewModel.ApplyTo(server);
        await PersistAsync();
    }

    // ===== Toolbar commands =====

    /// <summary>Adds a blank connection pointing at the local simulator URL.</summary>
    [RelayCommand]
    private async Task AddAsync()
    {
        if (AlpacaServers.Length >= MaxServers)
        {
            await ShowLimitAsync(ServerLimitMessage);
            return;
        }

        var connection = new AlpacaServer(DefaultUrl, "Blank")
        {
            IsConnected = false,
            UserName = null,
            Password = null
        };
        _settingsService.AddServer(connection);
        SelectedAlpacaServer = connection;
        await PersistAsync();
    }

    /// <summary>Searches the network for Alpaca telescopes and adds the one picked.</summary>
    [RelayCommand]
    private async Task DiscoveryAsync()
    {
        if (AlpacaServers.Length >= MaxServers)
        {
            await ShowLimitAsync(ServerLimitMessage);
            return;
        }

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
            _settingsService.AddServer(connection);
            SelectedAlpacaServer = connection;
            await PersistAsync();
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
                Message = L.T("Nothing selected"),
                CancelVisible = false
            });
            return;
        }

        if (!server.IsConnected && !IsServerAllowed(server))
        {
            await ShowLimitAsync(BeyondLimitMessage(server));
            return;
        }

        var disconnecting = server.IsConnected;
        var confirmViewModel = new ConfirmDialogViewModel
        {
            Message = disconnecting
                ? L.T("Disconnect from {0}? Its views stop updating, and other programs using this mount's Alpaca device may be disconnected too.", server.ServerName)
                : L.T("Connect to {0}?", server.ServerName)
        };
        await _dialogService.ShowDialog(confirmViewModel);
        // Skip if the state changed while the dialog was open (a view connected or lost it).
        if (!confirmViewModel.Confirmed || server.IsConnected != disconnecting) return;

        IsBusy = true;
        try
        {
            if (disconnecting)
            {
                if (await AlpacaConnector.DisconnectAsync(server))
                {
                    await _dialogService.ShowDialog(new ConfirmDialogViewModel
                    {
                        Message = L.T("{0} is still connected: the server kept the connection open.", server.ServerName),
                        CancelVisible = false
                    });
                }
                return;
            }

            var isConnected = await AlpacaConnector.ConnectAsync(server);
            if (isConnected)
            {
                _settingsService.SelectedAlpacaServer = server;
            }

            await _dialogService.ShowDialog(new ConfirmDialogViewModel
            {
                Message = isConnected
                    ? L.T("Connected to {0}.", server.ServerName)
                    : L.T("Could not connect to {0}.", server.ServerName),
                CancelVisible = false
            });
        }
        catch (Exception e)
        {
            // A failed disconnect leaves the state unknown: ask the server what it is now.
            // (ConnectAsync already marks a failed connect as not connected.)
            if (disconnecting) await AlpacaConnector.RefreshConnectedAsync(server);
            await _dialogService.ShowDialog(new ConfirmDialogViewModel
            {
                Message = disconnecting
                    ? L.T("Disconnect from {0} failed: {1}", server.ServerName, e.Message)
                    : L.T("Connect to {0} failed: {1}", server.ServerName, e.Message),
                CancelVisible = false
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Saves the server list to disk now (changes are also saved automatically).</summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        var confirmViewModel = new ConfirmDialogViewModel { Message = L.T("Save the server list?") };
        await _dialogService.ShowDialog(confirmViewModel);
        if (!confirmViewModel.Confirmed) return;

        await PersistAsync();
    }

    /// <summary>
    /// Writes the server list, quadrants and view count to the JSON file. Called after every
    /// change (add, discover, edit, delete, quadrant drop/clear, view count).
    /// </summary>
    private async Task PersistAsync()
    {
        try
        {
            _settingsService.SelectedAlpacaServer = SelectedAlpacaServer;
            _settingsService.Save();
        }
        catch (Exception e)
        {
            await _dialogService.ShowDialog(new ConfirmDialogViewModel
            {
                Message = L.T("Could not save: {0}", e.Message),
                CancelVisible = false
            });
        }
    }

    /// <summary>Removes the given server from the list.</summary>
    [RelayCommand]
    private async Task DeleteAsync(AlpacaServer? connection)
    {
        if (connection is null) return;

        var confirmViewModel = new ConfirmDialogViewModel { Message = L.T("Delete {0}?", connection.ServerName) };
        await _dialogService.ShowDialog(confirmViewModel);
        if (!confirmViewModel.Confirmed) return;

        // Removing it raises ServersChanged, which clears it from the quadrants of every open tab.
        _settingsService.RemoveServer(connection.IdGuid);
        SelectedAlpacaServer = AlpacaServers.LastOrDefault();
        await PersistAsync();
    }

    // ===== 3D model quadrants (drag a server from the list onto one) =====

    /// <summary>The four model quadrants on the right side of the page.</summary>
    public ModelQuadrant[] Quadrants { get; } =
        [.. Enumerable.Range(0, SettingsService.QuadrantCount).Select(_ => new ModelQuadrant())];

    /// <summary>
    /// How many quadrants are shown (1-4). Hidden quadrants keep their server but stop
    /// polling, and come back when the count is raised again.
    /// </summary>
    public int ViewCount
    {
        // The stored count is kept, so adding a key brings a larger layout back.
        get => Math.Min(_settingsService.ViewCount, MaxViews);
        set
        {
            if (value > MaxViews)
            {
                _ = ShowLimitAsync(L.T("The free version shows {0} view. A feature key unlocks up to {1}.",
                    FeatureKeyService.FreeMaxViews, FeatureKeyService.LicensedMaxViews));
                return;
            }
            if (_settingsService.ViewCount == Math.Clamp(value, 1, Quadrants.Length)) return;
            _settingsService.ViewCount = value;
            OnPropertyChanged();
            _ = PersistAsync();
        }
    }

    /// <summary>The server currently being dragged from the list, if any.</summary>
    [ObservableProperty]
    public partial AlpacaServer? DragServer { get; set; }

    public void StartDrag(AlpacaServer server) => DragServer = server;

    public void EndDrag() => DragServer = null;

    /// <summary>Shows the dragged server's model in a quadrant. Dropping again restarts it.</summary>
    public void DropOnQuadrant(int index)
    {
        var server = DragServer;
        DragServer = null;
        if (server is null || index < 0 || index >= ViewCount) return;
        if (!IsServerAllowed(server))
        {
            _ = ShowLimitAsync(BeyondLimitMessage(server));
            return;
        }

        Quadrants[index].Server = server;
        Quadrants[index].Generation++;
        Quadrants[index].Flipped = false;
        SelectedAlpacaServer = server;
        StoreQuadrants();
        OnPropertyChanged(nameof(Quadrants));
        _ = PersistAsync();
    }

    public void ClearQuadrant(int index)
    {
        if (index < 0 || index >= Quadrants.Length) return;
        Quadrants[index].Server = null;
        Quadrants[index].Flipped = false;
        StoreQuadrants();
        OnPropertyChanged(nameof(Quadrants));
        _ = PersistAsync();
    }

    public void ToggleFlip(int index)
    {
        if (index < 0 || index >= Quadrants.Length) return;
        Quadrants[index].Flipped = !Quadrants[index].Flipped;
        OnPropertyChanged(nameof(Quadrants));
    }

    /// <summary>Saves a per-server view change (shown coordinates, camera) made in a quadrant.</summary>
    public Task ServerSettingsChangedAsync() => PersistAsync();

    /// <summary>Fills the quadrants from the stored assignments (they then auto-connect).</summary>
    private void RestoreQuadrants()
    {
        var ids = _settingsService.QuadrantServerIds;
        for (var i = 0; i < Quadrants.Length && i < ids.Length; i++)
        {
            Quadrants[i].Server = ids[i] is { } id ? AlpacaServers.FirstOrDefault(s => s.IdGuid == id) : null;
        }
    }

    /// <summary>
    /// Keeps the settings in step with the quadrants, so a page refresh keeps them;
    /// Save writes them to disk with the server list.
    /// </summary>
    private void StoreQuadrants()
        => _settingsService.QuadrantServerIds = [.. Quadrants.Select(q => q.Server?.IdGuid)];

    public sealed class ModelQuadrant
    {
        public AlpacaServer? Server { get; set; }

        /// <summary>True while the quadrant shows its settings (back of the flip card).</summary>
        public bool Flipped { get; set; }

        /// <summary>Bumped on every drop so the view is rebuilt (and reconnects).</summary>
        public int Generation { get; set; }

        public string Key => $"{Server?.IdGuid}-{Generation}";
    }

    // ===== Helpers =====

    /// <summary>Adds the default simulator connection when it is missing.</summary>
    private void AutoAddComm()
    {
        var servers = AlpacaServers;
        if (servers.Any(i => i.ServerName == SimulatorName)) return;
        if (!_settingsService.AutoAddComm) return;
        if (servers.Length >= MaxServers) return;

        var connection = new AlpacaServer(DefaultUrl, SimulatorName)
        {
            IsConnected = false,
            UserName = null,
            Password = null
        };
        _settingsService.AddServer(connection);
        SelectedAlpacaServer = connection;
    }
}
