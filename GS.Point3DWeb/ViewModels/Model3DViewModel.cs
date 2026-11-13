using System.Diagnostics;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GS.Alpaca.Shared;
using GS.Alpaca.Support;
using GS.Point3DWeb.Dialogs;
using GS.Point3DWeb.Localization;
using GS.Point3DWeb.Models;
using GS.Point3DWeb.Services;
using GS.Point3DWeb.Support;

namespace GS.Point3DWeb.ViewModels;

/// <summary>
/// 3D model panel. Ported from GS.Alpaca.Client.ViewModels.ModelPageViewModel: connects to
/// <see cref="Server"/>, polls its device state and turns it into mount rotations for the Babylon.js viewer.
/// The view owns the JavaScript side and listens to <see cref="RotationChanged"/>.
/// </summary>
public partial class Model3DViewModel(SettingsService settingsService, DialogService dialogService,
    ILogger<Model3DViewModel> logger)
    : ViewModelBase, IDisposable
{
    private readonly ILogger<Model3DViewModel> _logger = logger;

    // Failed polls in a row. Only the first of a run is logged (a poll runs every half second or so),
    // then one line when updates resume, so a mount that stops answering doesn't flood the log.
    private int _pollFailures;
    private readonly SettingsService _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    private readonly DialogService _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
    private CancellationTokenSource? _cts;
    private Stopwatch? _timer;
    private double _lastRaHours;
    private bool _mountInfoRead;
    // Whether to try devicestate (one request) for the position; cleared for servers without it, which are
    // then read one property at a time. Tried again after each reconnect.
    private bool _useDeviceState = true;

    // How often to read the server's "connected" property: while connected, to notice another program
    // disconnecting it (devicestate keeps answering on some servers); while not, to notice it reconnecting.
    private static readonly TimeSpan ConnectedCheckInterval = TimeSpan.FromSeconds(5);
    // Stopwatch time of the last check. Zero (not MinValue: Elapsed - MinValue overflows) means the
    // first check comes one interval after the view starts.
    private TimeSpan _lastConnectedCheck = TimeSpan.Zero;

    // The last connect / disconnect outcome, kept on the status line while not connected.
    private string? _connectionMessage;

    /// <summary>The server this panel shows. Set before <see cref="StartTimerUpdates"/>.</summary>
    public AlpacaServer? Server { get; set; }

    /// <summary>Raised when this view finds or changes the server's connection state.</summary>
    public event Action? ConnectionChanged;

    /// <summary>Raised after each poll with the new model pose.</summary>
    public event Action? RotationChanged;

    // ===== Viewer options =====

    private AlignmentModes Mode => Server?.AlignmentMode ?? AlignmentModes.algGermanPolar;

    public string ModelUrl => Model3D.GetModelUrl(
        Server?.ModelType ?? Model3DType.Default, Mode, Server?.PolarMode ?? PolarMode.Left);

    public string ModelName =>
        $"{Model3D.GetDisplayName(Server?.ModelType ?? Model3DType.Default)} ({Model3D.GetDisplayName(Mode)})";

    /// <summary>
    /// The site latitude for the model: until the mount reports it, 0 (the equator), which every use below
    /// (compass, tilt, pose, axes) treats as the northern hemisphere.
    /// </summary>
    private double Latitude => Server?.Latitude ?? 0;

    /// <summary>North or south compass on the pier base, as GSServer chooses it.</summary>
    public bool UseNorthCompass => Model3D.UseNorthCompass(Mode, Latitude);
    /// <summary>The server's telescope color.</summary>
    public string ModelColor => ModelAppearance.NormalizeColor(Server?.ModelColor);

    /// <summary>The server's background image URL, or null for a plain dark sky.</summary>
    public string? BackgroundUrl => ModelAppearance.GetBackground(Server?.Background).Url;
    public string SecondaryColor => _settingsService.ModelSecondaryColor;

    [ObservableProperty] public partial string DeviceName { get; set; } = L.T("3D Model");

    // Mount axis positions (degrees), as GSServer's ActualAxisX / ActualAxisY:
    // RA / Dec axes for equatorial mounts, Azimuth / Altitude for Alt-Az.
    [ObservableProperty] public partial double PrimaryAxis { get; set; }
    [ObservableProperty] public partial double SecondaryAxis { get; set; }

    /// <summary>Tilt of the primary axis (GSServer's ZAxis): latitude, or 90° for Alt-Az.</summary>
    public double Tilt => Model3D.GetZAxis(Mode, Latitude);

    /// <summary>Mount data the viewer needs to build the pier.</summary>
    public object MountInfo => new { mode = (int)Mode, latitude = Latitude };

    /// <summary>
    /// GSServer's model transform (XAxisOffset, YAxisOffset, ZAxisOffset, YAxisCentre) for the
    /// current axis positions, with the server's own RA / Dec adjustments.
    /// </summary>
    public (double X, double Y, double Z, double YCentre) CurrentPose => Model3D.GetPose(
        Mode, Latitude, PrimaryAxis, SecondaryAxis,
        Server?.RaOffset ?? Model3D.DefaultRaOffset,
        Server?.DecOffset ?? Model3D.DefaultDecOffset);

    [ObservableProperty] public partial string StatusText { get; set; } = L.T("Ready");

    /// <summary>
    /// A fuller explanation of the status line, for its tooltip (null = the status text itself).
    /// Cleared whenever the status text changes, so it only describes the status it was set with.
    /// </summary>
    [ObservableProperty] public partial string? StatusDetail { get; set; }

    partial void OnStatusTextChanged(string value) => StatusDetail = null;

    // ===== Top coordinates bar =====

    [ObservableProperty] public partial string Ra { get; set; } = "00h 00m 00s";
    [ObservableProperty] public partial string Dec { get; set; } = "00° 00m 00s";
    [ObservableProperty] public partial string Alt { get; set; } = "00° 00m 00s";
    [ObservableProperty] public partial string Az { get; set; } = "00° 00m 00s";
    [ObservableProperty] public partial string Lha { get; set; } = "00h 00m 00s";

    /// <summary>Local sidereal time, in hours.</summary>
    [ObservableProperty] public partial string Lst { get; set; } = "00h 00:00.00";

    /// <summary>
    /// The rest of the mount's state (tracking, slewing, parked…), from the last devicestate / TelescopeState
    /// answer. Null while not connected, or when the server is read one property at a time (position only).
    /// </summary>
    [ObservableProperty] public partial MountStatus? Status { get; set; }

    /// <summary>Re-formats Ra after the server's hours / degrees setting changes.</summary>
    public void RefreshRa() => Ra = FormatRa(_lastRaHours);

    // ===== Timer =====

    [ObservableProperty] public partial int TimerCounter { get; set; }
    [ObservableProperty] public partial int TimerOverruns { get; set; }
    // Counts only while the view is updating and its server is connected; it pauses while disconnected and
    // carries on from there on reconnecting. The RunClock component at the bottom of the view reads it.
    // Started and stopped from the poll loop and from connection changes raised on other threads (another
    // tab, the toolbar), so every Start / Stop is under this lock; Stopwatch itself isn't thread-safe.
    private readonly Stopwatch _runningClock = new();
    private readonly Lock _runningClockLock = new();

    /// <summary>How long this view has been connected, in total.</summary>
    public TimeSpan RunningTime
    {
        get { lock (_runningClockLock) return _runningClock.Elapsed; }
    }

    /// <summary>Whether the running clock is counting right now (the server is connected).</summary>
    public bool IsRunningClockOn
    {
        get { lock (_runningClockLock) return _runningClock.IsRunning; }
    }

    /// <summary>Runs the clock while the view is updating and the server is connected; pauses it otherwise.</summary>
    public void SyncRunningClock()
    {
        lock (_runningClockLock)
        {
            if (_cts is not null && Server?.IsConnected == true)
            {
                if (!_runningClock.IsRunning) _runningClock.Start();
            }
            else
            {
                _runningClock.Stop();
            }
        }
    }

    /// <summary>The server's polling interval; read every cycle, so an edit applies straight away.</summary>
    public int TimerInMs => (int)(1000 * AlpacaServer.ClampPollSeconds(Server?.PollSeconds ?? AlpacaServer.DefaultPollSeconds));

    public void StartTimerUpdates()
    {
        if (_disposed) return; // the view was removed before its start-up finished
        StopTimerUpdates();
        _cts = new CancellationTokenSource();
        _ = RunTimerAsync(_cts.Token);
        SyncRunningClock();
    }

    public void StopTimerUpdates()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _timer?.Stop();
        lock (_runningClockLock) _runningClock.Stop();
    }

    private async Task RunTimerAsync(CancellationToken token)
    {
        // This loop's own stopwatch: a cancelled earlier loop finishing late must not stop the new one's.
        var timer = Stopwatch.StartNew();
        _timer = timer;
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimerInMs, token);

                // Backstop for connection changes the view didn't see (a different server shown, etc.).
                SyncRunningClock();
                var server = Server;
                if (server is null)
                {
                    DeviceName = L.T("3D Model");
                    StatusText = L.T("No server");
                    continue;
                }

                DeviceName = server.ServerName;
                if (!server.IsConnected)
                {
                    _mountInfoRead = false; // read it again after reconnecting
                    _pollFailures = 0; // a new connection starts a new run
                    _useDeviceState = true;
                    Status = null; // not current any more
                    if (server.IsConnecting) StatusText = L.T("Connecting to {0}…", server.ServerName);
                    else if (!IsChangingConnection) StatusText = _connectionMessage ?? L.T("Not connected");
                    // Not connected by the user (never, or they disconnected): don't ask until they connect.
                    if (IsConnectionBusy || !server.ConnectRequested
                        || timer.Elapsed - _lastConnectedCheck < ConnectedCheckInterval)
                        continue;

                    _lastConnectedCheck = timer.Elapsed;
                    if (await AlpacaConnector.RefreshConnectedAsync(server, token) && !token.IsCancellationRequested)
                    {
                        _logger.LogInformation("{Server}: connected again", server.ServerName);
                        _connectionMessage = null;
                        StatusText = L.T("Connected");
                        ConnectionChanged?.Invoke();
                    }
                    continue;
                }

                try
                {
                    // Alignment mode and latitude pick the model and tilt; read them once per connection.
                    if (!_mountInfoRead)
                    {
                        _mountInfoRead = await AlpacaConnector.ReadMountInfoAsync(server, token);
                        if (token.IsCancellationRequested) break;
                    }

                    if (!IsConnectionBusy && timer.Elapsed - _lastConnectedCheck >= ConnectedCheckInterval)
                    {
                        _lastConnectedCheck = timer.Elapsed;
                        if (!await AlpacaConnector.RefreshConnectedAsync(server, token))
                        {
                            if (token.IsCancellationRequested) break;
                            ReportConnectionLost(server, L.T("the server reports it is not connected"),
                                "the server reports it is not connected");
                            continue;
                        }
                    }

                    // Polls run one after another, so a slow one delays the next rather than overlapping it.
                    // Only the devicestate request is timed: the occasional connected check and mount-info
                    // read add requests that shouldn't count as a slow mount.
                    var pollStarted = timer.Elapsed;
                    TelescopeDeviceState? deviceState;
                    if (server.IsCom)
                    {
                        // A COM driver's failure throws, and is handled below like a failed request.
                        deviceState = await AlpacaConnector.ReadComStateAsync(server, token);
                    }
                    else
                    {
                        var client = AlpacaConnector.CreateClient(server);
                        deviceState = await ReadDeviceStateAsync(client, token);
                        server.SetClientTransactionId(client.ClientTransactionId);
                    }
                    if (token.IsCancellationRequested) break;

                    if (deviceState?.AlpacaResponse?.ErrorNumber > 0)
                    {
                        var error = L.T("Error {0}: {1}", deviceState.AlpacaResponse.ErrorNumber, deviceState.AlpacaResponse.ErrorMessage);
                        var errorForLog = $"error {deviceState.AlpacaResponse.ErrorNumber}: {deviceState.AlpacaResponse.ErrorMessage}";
                        if (deviceState.AlpacaResponse.ErrorNumber == NotConnectedError)
                        {
                            await CheckStillConnectedAsync(server, error, errorForLog, null, token);
                        }
                        else
                        {
                            StatusText = error;
                            PollFailed(server, errorForLog, null);
                        }
                        continue;
                    }
                    if (deviceState is null)
                    {
                        await CheckStillConnectedAsync(server, L.T("no answer from the server"), "no answer from the server", null, token);
                        continue;
                    }

                    _connectionMessage = null;
                    PollSucceeded(server);
                    UpdateLocalProperties(deviceState);
                    TimerCounter++;
                    // A poll that took longer than the interval: count it as an overrun.
                    if (timer.Elapsed - pollStarted > TimeSpan.FromMilliseconds(TimerInMs)) TimerOverruns++;
                    var interval = (TimerInMs / 1000.0).ToString("0.0#");
                    StatusText = L.T("Every {0} s · Updates {1} · Slow {2}", interval, TimerCounter, TimerOverruns);
                    StatusDetail = L.T("Polling every {0} s. {1} updates received; {2} took longer than the interval.",
                        interval, TimerCounter, TimerOverruns);

                    RotationChanged?.Invoke();
                }
                // An HTTP timeout is also an OperationCanceledException, so only a stop ends the loop.
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    await CheckStillConnectedAsync(server, ex.Message, ex.Message, ex, token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // stopped
        }
        finally
        {
            timer.Stop();
        }
    }

    /// <summary>Alpaca's NotConnected error (0x407): the device isn't connected.</summary>
    private const int NotConnectedError = 0x407;

    /// <summary>Alpaca's NotImplemented error (0x400).</summary>
    private const int NotImplementedError = 0x400;

    /// <summary>
    /// The mount's position: devicestate if the server has it, otherwise each property on its own (servers
    /// from before ASCOM Platform 7 answer devicestate with NotImplemented, or HTTP 400 / 404).
    /// </summary>
    private async Task<TelescopeDeviceState?> ReadDeviceStateAsync(AlpacaClient client, CancellationToken token)
    {
        if (_useDeviceState)
        {
            try
            {
                var state = await client.GetDeviceStateAsync(token);
                if (state?.AlpacaResponse?.ErrorNumber != NotImplementedError) return state;
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
            {
            }
            _useDeviceState = false;
        }
        return await client.GetDeviceStateFromPropertiesAsync(token);
    }

    /// <summary>
    /// After a failed poll, asks the server whether it is still connected. If it isn't (or doesn't answer),
    /// the view reports the lost connection instead of a stale "connected".
    /// </summary>
    /// <param name="reason">Why the poll failed, in the UI language (shown in the view).</param>
    /// <param name="reasonForLog">The same in English, for the log.</param>
    private async Task CheckStillConnectedAsync(AlpacaServer server, string reason, string reasonForLog,
        Exception? error, CancellationToken token)
    {
        if (IsConnectionBusy)
            return; // a Connect / Disconnect (here, the toolbar or a drop) is changing it right now

        if (await AlpacaConnector.RefreshConnectedAsync(server, token) || token.IsCancellationRequested)
        {
            StatusText = L.T("Error updating: {0}", reason);
            if (!token.IsCancellationRequested) PollFailed(server, reasonForLog, error);
            return;
        }

        ReportConnectionLost(server, reason, reasonForLog);
    }

    /// <summary>Logs the first failed poll of a run (see <see cref="_pollFailures"/>).</summary>
    private void PollFailed(AlpacaServer server, string reason, Exception? error)
    {
        if (_pollFailures++ == 0)
            _logger.LogWarning(error, "{Server}: update failed: {Reason} (further failures not logged until updates resume)",
                server.ServerName, reason);
    }

    private void PollSucceeded(AlpacaServer server)
    {
        if (_pollFailures == 0) return;
        _logger.LogInformation("{Server}: updates resumed after {Count} failed poll(s)", server.ServerName, _pollFailures);
        _pollFailures = 0;
    }

    private void ReportConnectionLost(AlpacaServer server, string reason, string reasonForLog)
    {
        _logger.LogWarning("{Server}: connection lost: {Reason}", AlpacaConnector.Describe(server), reasonForLog);
        _pollFailures = 0;
        _connectionMessage = L.T("Connection lost: {0}", reason);
        StatusText = _connectionMessage;
        _lastConnectedCheck = _timer?.Elapsed ?? TimeSpan.Zero;
        ConnectionChanged?.Invoke();
    }

    /// <summary>True while the view's Connect / Disconnect button is working.</summary>
    [ObservableProperty] public partial bool IsChangingConnection { get; set; }

    /// <summary>
    /// True while the connection is being changed, by this view's button or by a connect from anywhere else
    /// (the toolbar, a server dragged onto a view): the button then shows it and stays disabled.
    /// </summary>
    public bool IsConnectionBusy => IsChangingConnection || Server?.IsConnecting == true;

    /// <summary>
    /// Connects or disconnects this view's server (the button next to the coordinates).
    /// After a disconnect the view stops polling until it is connected again.
    /// </summary>
    public async Task ToggleConnectionAsync()
    {
        var server = Server;
        if (server is null || IsConnectionBusy) return;

        if (server.IsConnected)
        {
            // Disconnecting acts on the mount's driver or Alpaca device, so ask first.
            var confirm = new ConfirmDialogViewModel
            {
                Title = L.T("Disconnect"),
                Message = AlpacaConnector.DisconnectQuestion(server),
                AcceptText = L.T("Disconnect"),
                CancelText = L.T("Cancel")
            };
            await _dialogService.ShowDialog(confirm);
            if (!confirm.Confirmed || !server.IsConnected) return;
        }

        var disconnecting = server.IsConnected;
        IsChangingConnection = true;
        try
        {
            if (disconnecting)
            {
                StatusText = L.T("Disconnecting from {0}…", server.ServerName);
                var stillConnected = await AlpacaConnector.DisconnectAsync(server);
                _connectionMessage = stillConnected ? null : L.T("Disconnected");
                StatusText = stillConnected
                    ? L.T("Still connected: {0} kept the connection open", server.ServerName)
                    : L.T("Disconnected");
            }
            else
            {
                StatusText = L.T("Connecting to {0}…", server.ServerName);
                var connected = await AlpacaConnector.ConnectAsync(server);
                _mountInfoRead = false; // read the mount type and latitude again
                _connectionMessage = connected ? null : L.T("Could not connect");
                StatusText = connected ? L.T("Connected") : L.T("Could not connect");
            }
        }
        catch (Exception ex)
        {
            // A failed disconnect leaves the state unknown: ask the server what it is now.
            if (disconnecting) await AlpacaConnector.RefreshConnectedAsync(server);
            _connectionMessage = disconnecting ? L.T("Disconnect failed: {0}", ex.Message) : L.T("Connect failed: {0}", ex.Message);
            StatusText = _connectionMessage;
        }
        finally
        {
            _lastConnectedCheck = _timer?.Elapsed ?? TimeSpan.Zero;
            IsChangingConnection = false;
        }
        ConnectionChanged?.Invoke();
    }

    private void UpdateLocalProperties(TelescopeDeviceState deviceState)
    {
        // Coordinate bar
        _lastRaHours = deviceState.RightAscension;
        Ra = FormatRa(deviceState.RightAscension);
        Dec = CoordinateFormat.DegreesToDms(deviceState.Declination, "° ", ":", "", 2);
        Az = CoordinateFormat.DegreesToDms(deviceState.Azimuth, "° ", ":", "", 2);
        Alt = CoordinateFormat.DegreesToDms(deviceState.Altitude, "° ", ":", "", 2);
        Lha = CoordinateFormat.HoursToHms(Coordinate.Ra2Ha12(deviceState.RightAscension, deviceState.SiderealTime));
        Lst = CoordinateFormat.HoursToHms(deviceState.SiderealTime, "h ", ":", "", 2);
        Status = MountStatus.From(deviceState);

        // Convert to mount axis positions for the model
        var southern = Latitude < 0;
        double[] axes;
        switch (Mode)
        {
            case AlignmentModes.algAltAz:
                axes = [deviceState.Azimuth, deviceState.Altitude];
                break;
            case AlignmentModes.algPolar:
                // Fork mount: hour angle and declination, no meridian flip.
                axes = [(deviceState.SiderealTime - deviceState.RightAscension) * 15.0,
                        southern ? -deviceState.Declination : deviceState.Declination];
                break;
            default:
                axes = Axes.RaDecToAxesXy(
                    AlignmentModes.algGermanPolar,
                    [deviceState.RightAscension, deviceState.Declination],
                    deviceState.SiderealTime,
                    southern,
                    // East, west or unknown; anything else a mount reports is taken as unknown.
                    deviceState.SideOfPier is 0 or 1 ? deviceState.SideOfPier : -1);
                break;
        }

        PrimaryAxis = axes[0];
        SecondaryAxis = axes[1];
    }

    private string FormatRa(double hours) => Server?.RaInDegrees == true
        ? CoordinateFormat.DegreesToDms(hours * 15.0, "° ", ":", "", 2)
        : CoordinateFormat.HoursToHms(hours, "h ", ":", "", 2);

    private bool _disposed;

    /// <summary>
    /// The mount state shown on the view besides the coordinates. Each item is null when the mount didn't
    /// report it. <see cref="UtcDate"/> is to the second, so a poll only redraws it once a second.
    /// </summary>
    public sealed record MountStatus(bool? Tracking, bool? Slewing, bool? AtPark, bool? AtHome,
        bool? IsPulseGuiding, int? SideOfPier, DateTime? UtcDate)
    {
        /// <summary>The reported items of a device state, or null if it reported none of them.</summary>
        public static MountStatus? From(TelescopeDeviceState state)
        {
            var reported = state.Reported;
            if (reported.Count == 0) return null;

            DateTime? utc = null;
            if (reported.Contains("UTCDate") && state.UTCDate != default)
            {
                var date = state.UTCDate.Kind == DateTimeKind.Local ? state.UTCDate.ToUniversalTime() : state.UTCDate;
                utc = new DateTime(date.Ticks - date.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
            }

            var status = new MountStatus(
                reported.Contains("Tracking") ? state.Tracking : null,
                reported.Contains("Slewing") ? state.Slewing : null,
                reported.Contains("AtPark") ? state.AtPark : null,
                reported.Contains("AtHome") ? state.AtHome : null,
                reported.Contains("IsPulseGuiding") ? state.IsPulseGuiding : null,
                reported.Contains("SideOfPier") ? state.SideOfPier : null,
                utc);
            return status == Empty ? null : status;
        }

        private static readonly MountStatus Empty = new(null, null, null, null, null, null, null);
    }

    public void Dispose()
    {
        _disposed = true;
        StopTimerUpdates();
        GC.SuppressFinalize(this);
    }
}
