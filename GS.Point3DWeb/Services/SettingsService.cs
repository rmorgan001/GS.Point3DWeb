using System.Collections.ObjectModel;
using System.Text.Json;
using GS.Alpaca.Shared;
using GS.Point3DWeb.Models;
using GS.Point3DWeb.Support;

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

    // Every browser tab shares this singleton, so writes are serialized.
    private readonly Lock _saveLock = new();

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

    // The server list is shared by every browser tab (circuit), so it is only touched under this lock;
    // readers get a copy, which can't change while it is being enumerated.
    private readonly List<AlpacaServer> _servers = [];
    private readonly Lock _serversLock = new();

    /// <summary>A copy of the server list, in order.</summary>
    public AlpacaServer[] Servers
    {
        get
        {
            lock (_serversLock) return [.. _servers];
        }
    }

    /// <summary>
    /// Raised after a server is added or removed (on the thread that changed it), so every open
    /// page can refresh its list.
    /// </summary>
    public event Action? ServersChanged;

    public void AddServer(AlpacaServer server)
    {
        lock (_serversLock) _servers.Add(server);
        ServersChanged?.Invoke();
    }

    public void RemoveServer(string idGuid)
    {
        bool removed;
        lock (_serversLock) removed = _servers.RemoveAll(s => s.IdGuid == idGuid) > 0;
        if (removed) ServersChanged?.Invoke();
    }

    /// <summary>Number of 3D model quadrants on the Home page.</summary>
    public const int QuadrantCount = 4;

    /// <summary>
    /// Server IdGuid shown in each 3D model quadrant (null = empty), saved with the server list.
    /// </summary>
    public string?[] QuadrantServerIds { get; set; } = new string?[QuadrantCount];

    /// <summary>How many quadrants are shown (1 to <see cref="QuadrantCount"/>), saved with the server list.</summary>
    public int ViewCount
    {
        get;
        set => field = Math.Clamp(value, 1, QuadrantCount);
    } = QuadrantCount;

    /// <summary>Automatically add the "ASCOM Alpaca Simulators" connection when it is missing.</summary>
    public bool AutoAddComm { get; set; } = true;

    // ===== 3D model (mirrors the Model settings in GS.Alpaca.Client) =====


    public bool NHemiSphere { get; set; } = true;

    /// <summary>Scene light color (Green Swamp moon white).</summary>
    public string ModelSecondaryColor { get; set; } = "#e8e6df";

    /// <summary>Loads the saved server list, if there is one.</summary>
    public void Load()
    {
        if (!File.Exists(FilePath)) return;

        try
        {
            var stored = JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(FilePath), JsonOptions);
            if (stored is null) return;

            // Version 1 files stored the old viewer's full offsets (default RA 0, Dec -90); from
            // version 2 they are adjustments on top of GSServer's offsets, so keep only the change
            // the user made from those defaults.
            var (legacyRa, legacyDec) = stored.Version is null or < 2 ? (0.0, -90.0) : (0.0, 0.0);

            _servers.AddRange(stored.Servers.Select(s =>
                new AlpacaServer(s.Url, s.ServerName)
                {
                    IdGuid = s.IdGuid,
                    UserName = s.UserName,
                    // Older files have no model settings, so they get the defaults.
                    ModelType = Enum.TryParse<Model3DType>(s.ModelType, out var modelType) ? modelType : Model3DType.Default,
                    RaOffset = Model3D.ClampOffset(s.RaOffset is { } ra ? ra - legacyRa : Model3D.DefaultRaOffset),
                    DecOffset = Model3D.ClampOffset(s.DecOffset is { } dec ? dec - legacyDec : Model3D.DefaultDecOffset),
                    PolarMode = Enum.TryParse<PolarMode>(s.PolarMode, out var polarMode) ? polarMode : PolarMode.Left,
                    AlignmentMode = Enum.TryParse<AlignmentModes>(s.AlignmentMode, out var mode) ? mode : AlignmentModes.algGermanPolar,
                    Latitude = s.Latitude,
                    Camera = s.Camera,
                    PollSeconds = AlpacaServer.ClampPollSeconds(s.PollSeconds ?? AlpacaServer.DefaultPollSeconds),
                    ShowRa = s.ShowRa ?? true,
                    ShowDec = s.ShowDec ?? true,
                    ShowAz = s.ShowAz ?? true,
                    ShowAlt = s.ShowAlt ?? true,
                    ShowLha = s.ShowLha ?? true,
                    ShowLst = s.ShowLst ?? true,
                    RaInDegrees = s.RaInDegrees ?? false,
                    ModelColor = ModelAppearance.NormalizeColor(s.ModelColor),
                    Background = ModelAppearance.GetBackground(s.Background).Key,
                    ShowGrid = s.ShowGrid ?? true
                }));
            SelectedAlpacaServer = _servers.FirstOrDefault(s => s.IdGuid == stored.SelectedId);

            // Older files have no quadrants; ids for servers that no longer exist are dropped.
            var quadrants = new string?[QuadrantCount];
            for (var i = 0; i < QuadrantCount && i < (stored.Quadrants?.Count ?? 0); i++)
            {
                var id = stored.Quadrants![i];
                quadrants[i] = _servers.Any(s => s.IdGuid == id) ? id : null;
            }
            QuadrantServerIds = quadrants;
            if (stored.ViewCount is { } viewCount) ViewCount = viewCount;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read server list from {FilePath}", FilePath);
        }
    }

    /// <summary>
    /// Writes the server list and the quadrant assignments to disk. Connection state,
    /// transaction IDs and passwords are not saved.
    /// </summary>
    public void Save()
    {
        var servers = Servers;
        var stored = new StoredSettings(
            SelectedAlpacaServer?.IdGuid,
            [.. servers.Select(s => new StoredServer(s.IdGuid, s.ServerName, s.Url, s.UserName,
                s.ModelType.ToString(), s.RaOffset, s.DecOffset,
                s.ShowRa, s.ShowDec, s.ShowAz, s.ShowAlt, s.ShowLha,
                s.PolarMode.ToString(), s.AlignmentMode.ToString(), s.Latitude, s.Camera,
                s.PollSeconds, s.ShowLst, s.RaInDegrees, s.ModelColor, s.Background, s.ShowGrid))],
            [.. QuadrantServerIds.Select(id => servers.Any(s => s.IdGuid == id) ? id : null)],
            ViewCount,
            CurrentVersion);

        var json = JsonSerializer.Serialize(stored, JsonOptions);

        lock (_saveLock)
        {
            // Write to a temp file then swap it in, so a crash mid-write can't corrupt the list.
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tempPath = FilePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, FilePath, overwrite: true);
        }
    }

    /// <summary>File format version; 2 = model offsets are adjustments on the alignment mode's offsets.</summary>
    private const int CurrentVersion = 2;

    private sealed record StoredSettings(
        string? SelectedId, List<StoredServer> Servers, List<string?>? Quadrants = null, int? ViewCount = null,
        int? Version = null);

    private sealed record StoredServer(
        string IdGuid, string ServerName, string Url, string? UserName,
        string? ModelType = null, double? RaOffset = null, double? DecOffset = null,
        bool? ShowRa = null, bool? ShowDec = null, bool? ShowAz = null, bool? ShowAlt = null, bool? ShowLha = null,
        string? PolarMode = null, string? AlignmentMode = null, double? Latitude = null,
        CameraView? Camera = null, double? PollSeconds = null, bool? ShowLst = null, bool? RaInDegrees = null,
        string? ModelColor = null, string? Background = null, bool? ShowGrid = null);
}
