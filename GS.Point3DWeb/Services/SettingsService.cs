using System.Security.Cryptography;
using System.Text.Json;
using GS.Alpaca.Shared;
using Microsoft.AspNetCore.DataProtection;
using GS.Point3DWeb.Localization;
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

    // Server passwords are saved encrypted with ASP.NET Core Data Protection (on Windows its keys are
    // protected with DPAPI for this user), so a server that needs a login still connects after a restart.
    private readonly IDataProtector _passwordProtector;

    public SettingsService(ILogger<SettingsService> logger, IDataProtectionProvider dataProtection)
    {
        _logger = logger;
        _passwordProtector = dataProtection.CreateProtector("GS.Point3DWeb.ServerPasswords.v1");
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
        lock (_serversLock)
        {
            foreach (var server in _servers.Where(s => s.IdGuid == idGuid)) server.Removed = true;
            removed = _servers.RemoveAll(s => s.IdGuid == idGuid) > 0;
        }
        if (removed) ServersChanged?.Invoke();
    }

    /// <summary>Number of 3D model quadrants on the Home page.</summary>
    public const int QuadrantCount = 4;

    // The quadrant layout is shared by every tab too: each tab shows it, and a change in one is
    // pushed to the others (LayoutChanged), so no tab saves a stale copy over another's change.
    private readonly Lock _layoutLock = new();
    private string?[] _quadrantServerIds = new string?[QuadrantCount];

    /// <summary>Server IdGuid shown in each 3D model quadrant (null = empty), as a copy.</summary>
    public string?[] QuadrantServerIds
    {
        get
        {
            lock (_layoutLock) return [.. _quadrantServerIds];
        }
    }

    /// <summary>Shows a server (or nothing) in one quadrant, for every tab.</summary>
    public void SetQuadrant(int index, string? serverId)
    {
        if (index < 0 || index >= QuadrantCount) return;
        lock (_layoutLock)
        {
            if (_quadrantServerIds[index] == serverId) return;
            _quadrantServerIds[index] = serverId;
        }
        LayoutChanged?.Invoke();
    }

    /// <summary>How many quadrants are shown (1 to <see cref="QuadrantCount"/>), saved with the server list.</summary>
    public int ViewCount
    {
        get;
        set
        {
            var count = Math.Clamp(value, 1, QuadrantCount);
            if (field == count) return;
            field = count;
            LayoutChanged?.Invoke();
        }
    } = QuadrantCount;

    /// <summary>Raised after the quadrant layout or the view count changes (on the thread that changed it).</summary>
    public event Action? LayoutChanged;

    /// <summary>
    /// Why servers.json couldn't be read at start-up (null if it was fine). The app then starts with
    /// an empty list; the page tells the user once.
    /// </summary>
    public string? LoadProblem => _loadProblem?.Invoke();

    // Built when read, so it shows in the language of whoever reads it, not of whoever started the app.
    private Func<string>? _loadProblem;

    /// <summary>Set once the page has shown <see cref="LoadProblem"/>.</summary>
    public bool LoadProblemShown { get; set; }

    // Set when servers.json couldn't be read or copied aside: then it is never saved over, so the
    // user's servers are kept until the file can be read again.
    private bool _saveBlocked;

    // A first run (no servers.json yet) starts with the ASCOM Alpaca Simulators connection.
    private const string SimulatorName = "ASCOM Alpaca Simulators";
    private const string SimulatorUrl = "http://127.0.0.1:32323/api/v1/telescope/0/";

    // ===== 3D model (mirrors the Model settings in GS.Alpaca.Client) =====


    /// <summary>Scene light color (Green Swamp moon white).</summary>
    public string ModelSecondaryColor { get; set; } = "#e8e6df";

    /// <summary>
    /// Loads the saved server list. A first run (no file) starts with the simulator connection. If the
    /// file can't be read, the app starts with an empty list and the file is kept: it is copied aside
    /// before anything can save over it, or, if even that fails, it is never saved over.
    /// </summary>
    public void Load()
    {
        if (!File.Exists(FilePath))
        {
            var simulator = new AlpacaServer(SimulatorUrl, SimulatorName);
            _servers.Add(simulator);
            SelectedAlpacaServer = simulator;
            return;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<StoredSettings>(ReadAllTextWithRetry(FilePath), JsonOptions)
                         ?? throw new InvalidDataException("The file is empty.");
            if (stored.Servers is null) throw new InvalidDataException("The file has no server list.");

            // Version 1 files stored the old viewer's full offsets (default RA 0, Dec -90); from
            // version 2 they are adjustments on top of GSServer's offsets, so keep only the change
            // the user made from those defaults.
            var (legacyRa, legacyDec) = stored.Version is null or < 2 ? (0.0, -90.0) : (0.0, 0.0);

            // Build the whole list before using any of it, so a bad entry can't leave half a list.
            var servers = stored.Servers.Select(s =>
                new AlpacaServer(s.Url, s.ServerName)
                {
                    IdGuid = s.IdGuid,
                    UserName = s.UserName,
                    Password = UnprotectPassword(s.IdGuid, s.Password, out var unreadable),
                    PasswordUnreadable = unreadable,
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
                    ShowGrid = s.ShowGrid ?? true,
                    ShowMountState = s.ShowMountState ?? true
                }).ToList();
            _servers.AddRange(servers);
            SelectedAlpacaServer = _servers.FirstOrDefault(s => s.IdGuid == stored.SelectedId);

            // Older files have no quadrants; ids for servers that no longer exist are dropped.
            var quadrants = new string?[QuadrantCount];
            for (var i = 0; i < QuadrantCount && i < (stored.Quadrants?.Count ?? 0); i++)
            {
                var id = stored.Quadrants![i];
                quadrants[i] = _servers.Any(s => s.IdGuid == id) ? id : null;
            }
            _quadrantServerIds = quadrants;
            if (stored.ViewCount is { } viewCount) ViewCount = viewCount;
        }
        catch (Exception ex)
        {
            _servers.Clear();
            _storedPasswords.Clear();
            SelectedAlpacaServer = null;
            _logger.LogError(ex, "Could not read server list from {FilePath}", FilePath);
            KeepUnreadableFile(ex);
        }
    }

    /// <summary>Reads the file, retrying briefly if another program (antivirus, backup) has it open.</summary>
    private static string ReadAllTextWithRetry(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(200 * attempt);
            }
        }
    }

    /// <summary>Copies an unreadable servers.json aside so saving can't lose it; if that fails, blocks saving.</summary>
    private void KeepUnreadableFile(Exception error)
    {
        var copy = $"{FilePath}.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}";
        try
        {
            File.Copy(FilePath, copy, overwrite: false);
            _logger.LogWarning("Kept a copy of the unreadable server list as {Copy}", copy);
            _loadProblem = () => L.T("The server list could not be read ({0}). A copy of the file was saved as {1}, and the app started with an empty list.",
                error.Message, copy);
        }
        catch (Exception copyError)
        {
            _saveBlocked = true;
            _logger.LogError(copyError, "Could not copy {FilePath} aside; it will not be saved over", FilePath);
            _loadProblem = () => L.T("The server list could not be read ({0}). It won't be saved over, so your servers are kept: close any program using {1}, or fix the file, then restart the app.",
                error.Message, FilePath);
        }
    }

    /// <summary>
    /// Writes the server list and the quadrant assignments to disk. Connection state,
    /// transaction IDs are not saved; passwords are saved encrypted.
    /// </summary>
    public void Save()
    {
        if (_saveBlocked)
            throw new InvalidOperationException(L.T("{0} couldn't be read at start-up, so it isn't saved over. Restart the app once the file can be read.", FilePath));

        // Snapshot, serialize and write in one step: two tabs saving at once can't then leave the
        // older snapshot on disk.
        lock (_saveLock)
        {
            var servers = Servers;
            var quadrants = QuadrantServerIds;
            var stored = new StoredSettings(
                SelectedAlpacaServer?.IdGuid,
                [.. servers.Select(s => new StoredServer(s.IdGuid, s.ServerName, s.Url, s.UserName, ProtectPassword(s),
                    s.ModelType.ToString(), s.RaOffset, s.DecOffset,
                    s.ShowRa, s.ShowDec, s.ShowAz, s.ShowAlt, s.ShowLha,
                    s.PolarMode.ToString(), s.AlignmentMode.ToString(), s.Latitude, s.Camera,
                    s.PollSeconds, s.ShowLst, s.RaInDegrees, s.ModelColor, s.Background, s.ShowGrid, s.ShowMountState))],
                [.. quadrants.Select(id => servers.Any(s => s.IdGuid == id) ? id : null)],
                ViewCount,
                CurrentVersion);

            var json = JsonSerializer.Serialize(stored, JsonOptions);

            // Write to a temp file then swap it in, so a crash mid-write can't corrupt the list.
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tempPath = FilePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, FilePath, overwrite: true);
        }
    }

    // Each server's password as last loaded or saved, with its encrypted text (by server IdGuid). Saving an
    // unchanged password writes the same text back: no encryption on every save, and a password that
    // couldn't be decrypted (Password is null) is kept in the file rather than lost. Used under _saveLock,
    // or at start-up before anything can save.
    private readonly Dictionary<string, (string? Plain, string Encrypted)> _storedPasswords = [];

    /// <summary>
    /// The encrypted text to save for a server's password. If it can't be encrypted (the key store can't be
    /// written), the password is left out of the file rather than failing the whole save.
    /// </summary>
    private string? ProtectPassword(AlpacaServer server)
    {
        var (idGuid, password) = (server.IdGuid, server.Password);
        if (_storedPasswords.TryGetValue(idGuid, out var stored)
            && (server.PasswordUnreadable || (stored.Plain is not null && stored.Plain == password)))
            return stored.Encrypted; // unchanged, or unreadable and not edited: write it back as it was
        if (string.IsNullOrEmpty(password))
        {
            _storedPasswords.Remove(idGuid);
            return null;
        }

        try
        {
            var encrypted = _passwordProtector.Protect(password);
            _storedPasswords[idGuid] = (password, encrypted);
            return encrypted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A server password could not be encrypted, so it isn't saved; it has to be entered again after a restart");
            return null;
        }
    }

    /// <summary>
    /// The saved password, or null if there is none or it can't be decrypted (another user or computer, a
    /// missing key, or text that isn't encrypted at all). The encrypted text is remembered either way.
    /// </summary>
    private string? UnprotectPassword(string idGuid, string? stored, out bool unreadable)
    {
        unreadable = false;
        if (string.IsNullOrEmpty(stored)) return null;
        string? password;
        try
        {
            password = _passwordProtector.Unprotect(stored);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            _logger.LogWarning(ex, "A saved server password could not be decrypted; it is kept in the file but has to be entered again");
            password = null;
            unreadable = true;
        }
        _storedPasswords[idGuid] = (password, stored);
        return password;
    }

    /// <summary>File format version; 2 = model offsets are adjustments on the alignment mode's offsets.</summary>
    private const int CurrentVersion = 2;

    private sealed record StoredSettings(
        string? SelectedId, List<StoredServer>? Servers, List<string?>? Quadrants = null, int? ViewCount = null,
        int? Version = null);

    private sealed record StoredServer(
        string IdGuid, string ServerName, string Url, string? UserName, string? Password,
        string? ModelType = null, double? RaOffset = null, double? DecOffset = null,
        bool? ShowRa = null, bool? ShowDec = null, bool? ShowAz = null, bool? ShowAlt = null, bool? ShowLha = null,
        string? PolarMode = null, string? AlignmentMode = null, double? Latitude = null,
        CameraView? Camera = null, double? PollSeconds = null, bool? ShowLst = null, bool? RaInDegrees = null,
        string? ModelColor = null, string? Background = null, bool? ShowGrid = null, bool? ShowMountState = null);
}
