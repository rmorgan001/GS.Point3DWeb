using System.Diagnostics;
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
public partial class Model3DViewModel(SettingsService settingsService, DialogService dialogService)
    : ViewModelBase, IDisposable
{
    private readonly SettingsService _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    private readonly DialogService _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
    private CancellationTokenSource? _cts;
    private Stopwatch? _timer;
    private double _lastRaHours;
    private bool _mountInfoRead;

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

    /// <summary>North or south compass on the pier base, as GSServer chooses it.</summary>
    public bool UseNorthCompass => Server?.Latitude is { } latitude
        ? Model3D.UseNorthCompass(Mode, latitude)
        : _settingsService.NHemiSphere;
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
    public double Tilt => Model3D.GetZAxis(Mode, Server?.Latitude ?? 0);

    /// <summary>Mount data the viewer needs to build the pier.</summary>
    public object MountInfo => new { mode = (int)Mode, latitude = Server?.Latitude ?? 0 };

    /// <summary>
    /// GSServer's model transform (XAxisOffset, YAxisOffset, ZAxisOffset, YAxisCentre) for the
    /// current axis positions, with the server's own RA / Dec adjustments.
    /// </summary>
    public (double X, double Y, double Z, double YCentre) CurrentPose => Model3D.GetPose(
        Mode, Server?.Latitude ?? 0, PrimaryAxis, SecondaryAxis,
        Server?.RaOffset ?? Model3D.DefaultRaOffset,
        Server?.DecOffset ?? Model3D.DefaultDecOffset);

    [ObservableProperty] public partial string StatusText { get; set; } = L.T("Ready");

    // ===== Top coordinates bar =====

    [ObservableProperty] public partial string Ra { get; set; } = "00h 00m 00s";
    [ObservableProperty] public partial string Dec { get; set; } = "00° 00m 00s";
    [ObservableProperty] public partial string Alt { get; set; } = "00° 00m 00s";
    [ObservableProperty] public partial string Az { get; set; } = "00° 00m 00s";
    [ObservableProperty] public partial string Lha { get; set; } = "00h 00m 00s";

    /// <summary>Local sidereal time, in hours.</summary>
    [ObservableProperty] public partial string Lst { get; set; } = "00h 00:00.00";

    /// <summary>Re-formats Ra after the server's hours / degrees setting changes.</summary>
    public void RefreshRa() => Ra = FormatRa(_lastRaHours);

    // ===== Timer =====

    [ObservableProperty] public partial int TimerCounter { get; set; }
    [ObservableProperty] public partial int TimerOverruns { get; set; }
    [ObservableProperty] public partial int ElapsedTime { get; set; }

    /// <summary>The server's polling interval; read every cycle, so an edit applies straight away.</summary>
    public int TimerInMs => (int)(1000 * AlpacaServer.ClampPollSeconds(Server?.PollSeconds ?? AlpacaServer.DefaultPollSeconds));

    public void StartTimerUpdates()
    {
        if (_disposed) return; // the view was removed before its start-up finished
        StopTimerUpdates();
        _cts = new CancellationTokenSource();
        _ = RunTimerAsync(_cts.Token);
    }

    public void StopTimerUpdates()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _timer?.Stop();
    }

    private async Task RunTimerAsync(CancellationToken token)
    {
        // This loop's own stopwatch: a cancelled earlier loop finishing late must not stop the new one's.
        var timer = Stopwatch.StartNew();
        _timer = timer;
        try
        {
            await AutoConnectAsync(token);

            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimerInMs, token);

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
                    if (!IsChangingConnection) StatusText = _connectionMessage ?? L.T("Not connected");
                    if (IsChangingConnection || timer.Elapsed - _lastConnectedCheck < ConnectedCheckInterval)
                        continue;

                    _lastConnectedCheck = timer.Elapsed;
                    if (await AlpacaConnector.RefreshConnectedAsync(server) && !token.IsCancellationRequested)
                    {
                        _connectionMessage = null;
                        StatusText = L.T("Connected");
                        ConnectionChanged?.Invoke();
                    }
                    continue;
                }

                // Polls run one after another, so a slow one delays the next rather than overlapping it.
                var pollStarted = timer.Elapsed;
                try
                {
                    // Alignment mode and latitude pick the model and tilt; read them once per connection.
                    if (!_mountInfoRead)
                    {
                        _mountInfoRead = await AlpacaConnector.ReadMountInfoAsync(server);
                        if (token.IsCancellationRequested) break;
                    }

                    if (!IsChangingConnection && timer.Elapsed - _lastConnectedCheck >= ConnectedCheckInterval)
                    {
                        _lastConnectedCheck = timer.Elapsed;
                        if (!await AlpacaConnector.RefreshConnectedAsync(server))
                        {
                            if (token.IsCancellationRequested) break;
                            ReportConnectionLost(L.T("the server reports it is not connected"));
                            continue;
                        }
                    }

                    var client = AlpacaConnector.CreateClient(server);
                    var deviceState = await client.GetDeviceStateAsync();
                    server.SetClientTransactionId(client.ClientTransactionId);
                    if (token.IsCancellationRequested) break;

                    if (deviceState?.AlpacaResponse?.ErrorNumber > 0)
                    {
                        var error = L.T("Error {0}: {1}", deviceState.AlpacaResponse.ErrorNumber, deviceState.AlpacaResponse.ErrorMessage);
                        if (deviceState.AlpacaResponse.ErrorNumber == NotConnectedError)
                            await CheckStillConnectedAsync(server, error, token);
                        else
                            StatusText = error;
                        continue;
                    }
                    if (deviceState is null)
                    {
                        await CheckStillConnectedAsync(server, L.T("no answer from the server"), token);
                        continue;
                    }

                    _connectionMessage = null;
                    UpdateLocalProperties(deviceState);
                    TimerCounter++;
                    // A poll that took longer than the interval: count it as an overrun.
                    if (timer.Elapsed - pollStarted > TimeSpan.FromMilliseconds(TimerInMs)) TimerOverruns++;
                    ElapsedTime = (int)timer.Elapsed.TotalSeconds;
                    StatusText = $"Int={TimerInMs}ms, overruns={TimerOverruns}, counter={TimerCounter}, timer={ElapsedTime}s";

                    RotationChanged?.Invoke();
                }
                // An HTTP timeout is also an OperationCanceledException, so only a stop ends the loop.
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    await CheckStillConnectedAsync(server, ex.Message, token);
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

    /// <summary>
    /// After a failed poll, asks the server whether it is still connected. If it isn't (or doesn't answer),
    /// the view reports the lost connection instead of a stale "connected".
    /// </summary>
    private async Task CheckStillConnectedAsync(AlpacaServer server, string reason, CancellationToken token)
    {
        if (IsChangingConnection)
            return; // the Connect / Disconnect button is changing it right now

        if (await AlpacaConnector.RefreshConnectedAsync(server) || token.IsCancellationRequested)
        {
            StatusText = L.T("Error updating: {0}", reason);
            return;
        }

        ReportConnectionLost(reason);
    }

    private void ReportConnectionLost(string reason)
    {
        _connectionMessage = L.T("Connection lost: {0}", reason);
        StatusText = _connectionMessage;
        _lastConnectedCheck = _timer?.Elapsed ?? TimeSpan.Zero;
        ConnectionChanged?.Invoke();
    }

    /// <summary>True while the view's Connect / Disconnect button is working.</summary>
    [ObservableProperty] public partial bool IsChangingConnection { get; set; }

    /// <summary>
    /// Connects or disconnects this view's server (the button next to the coordinates).
    /// After a disconnect the view stops polling until it is connected again.
    /// </summary>
    public async Task ToggleConnectionAsync()
    {
        var server = Server;
        if (server is null || IsChangingConnection) return;

        if (server.IsConnected)
        {
            // Disconnecting acts on the mount's Alpaca device, so ask first.
            var confirm = new ConfirmDialogViewModel
            {
                Title = L.T("Disconnect"),
                Message = L.T("Disconnect from {0}? Its views stop updating, and other programs using this mount's Alpaca device may be disconnected too.", server.ServerName),
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

    private async Task AutoConnectAsync(CancellationToken token)
    {
        var server = Server;
        if (server is null) return;

        DeviceName = server.ServerName;
        if (server.IsConnected) return;

        StatusText = L.T("Connecting to {0}…", server.ServerName);
        try
        {
            var connected = await AlpacaConnector.ConnectAsync(server);
            if (token.IsCancellationRequested) return;
            _connectionMessage = connected ? null : L.T("Could not connect");
            StatusText = connected ? L.T("Connected") : L.T("Could not connect");
        }
        catch (Exception ex)
        {
            _connectionMessage = L.T("Connect failed: {0}", ex.Message);
            StatusText = _connectionMessage;
        }
        _lastConnectedCheck = _timer?.Elapsed ?? TimeSpan.Zero;
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

        // Convert to mount axis positions for the model
        var southern = Server?.Latitude is { } latitude ? latitude < 0 : !_settingsService.NHemiSphere;
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
                    deviceState.SideOfPier);
                break;
        }

        PrimaryAxis = axes[0];
        SecondaryAxis = axes[1];
    }

    private string FormatRa(double hours) => Server?.RaInDegrees == true
        ? CoordinateFormat.DegreesToDms(hours * 15.0, "° ", ":", "", 2)
        : CoordinateFormat.HoursToHms(hours, "h ", ":", "", 2);

    private bool _disposed;

    public void Dispose()
    {
        _disposed = true;
        StopTimerUpdates();
        GC.SuppressFinalize(this);
    }
}
