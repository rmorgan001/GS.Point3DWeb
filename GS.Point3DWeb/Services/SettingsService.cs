using System.Collections.ObjectModel;
using System.Text.Json;
using GS.Point3DWeb.Models;

namespace GS.Point3DWeb.Services;

/// <summary>
/// Application-wide settings (registered as a singleton).
/// Mirrors the connection-related parts of GS.Alpaca.Client.Singletons.SettingsService.
/// The server list is loaded at startup and written by <see cref="Save"/> to
/// <see cref="FilePath"/> (the user's application-data folder, which differs by OS).
/// </summary>
public class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ILogger<SettingsService> _logger;

    public SettingsService(ILogger<SettingsService> logger)
    {
        _logger = logger;
        FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GS.Point3DWeb",
            "servers.json");
        Load();
    }

    public string FilePath { get; }

    public AlpacaServer? SelectedAlpacaServer { get; set; }

    public ObservableCollection<AlpacaServer> AlpacaServers { get; set; } = [];

    /// <summary>Automatically add the "ASCOM Alpaca Simulators" connection when it is missing.</summary>
    public bool AutoAddComm { get; set; } = true;

    /// <summary>Loads the saved server list, if there is one.</summary>
    public void Load()
    {
        if (!File.Exists(FilePath)) return;

        try
        {
            var stored = JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(FilePath), JsonOptions);
            if (stored is null) return;

            AlpacaServers = new ObservableCollection<AlpacaServer>(stored.Servers.Select(s =>
                new AlpacaServer(s.Url, s.ServerName)
                {
                    IdGuid = s.IdGuid,
                    UserName = s.UserName
                }));
            SelectedAlpacaServer = AlpacaServers.FirstOrDefault(s => s.IdGuid == stored.SelectedId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read server list from {FilePath}", FilePath);
        }
    }

    /// <summary>
    /// Writes the server list to disk. Connection state, transaction IDs and
    /// passwords are not saved.
    /// </summary>
    public void Save()
    {
        var stored = new StoredSettings(
            SelectedAlpacaServer?.IdGuid,
            [.. AlpacaServers.Select(s => new StoredServer(s.IdGuid, s.ServerName, s.Url, s.UserName))]);

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(stored, JsonOptions));
    }

    private sealed record StoredSettings(string? SelectedId, List<StoredServer> Servers);

    private sealed record StoredServer(string IdGuid, string ServerName, string Url, string? UserName);
}
