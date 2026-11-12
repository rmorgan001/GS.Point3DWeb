using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GS.Alpaca.Shared;
using GS.Alpaca.Support;
using GS.Point3DWeb.Models;
using GS.Point3DWeb.Services;
using GS.Point3DWeb.Support;

namespace GS.Point3DWeb.ViewModels;

/// <summary>
/// 3D model panel. Ported from GS.Alpaca.Client.ViewModels.ModelPageViewModel: connects to
/// <see cref="Server"/>, polls its device state and turns it into mount rotations for the Babylon.js viewer.
/// The view owns the JavaScript side and listens to <see cref="RotationChanged"/>.
/// </summary>
public partial class Model3DViewModel(SettingsService settingsService) : ViewModelBase, IDisposable
{
    private readonly SettingsService _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    private CancellationTokenSource? _cts;
    private Stopwatch? _timer;
    private double _lastRaHours;
    private bool _mountInfoRead;

    /// <summary>The server this panel shows. Set before <see cref="StartTimerUpdates"/>.</summary>
    public AlpacaServer? Server { get; set; }

    /// <summary>Raised when the auto-connect changes the server's connection state.</summary>
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

    [ObservableProperty] public partial string DeviceName { get; set; } = "3D Model";

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

    [ObservableProperty] public partial string StatusText { get; set; } = "Ready";

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
        _timer = Stopwatch.StartNew();
        var busy = false;
        try
        {
            await AutoConnectAsync(token);

            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimerInMs, token);

                var server = Server;
                if (server is null)
                {
                    DeviceName = "3D Model";
                    StatusText = "No server";
                    continue;
                }

                DeviceName = server.ServerName;
                if (!server.IsConnected)
                {
                    StatusText = "Not connected";
                    _mountInfoRead = false; // read it again after reconnecting
                    continue;
                }

                if (busy)
                {
                    TimerOverruns++;
                    continue;
                }

                busy = true;
                try
                {
                    // Alignment mode and latitude pick the model and tilt; read them once per connection.
                    if (!_mountInfoRead)
                    {
                        _mountInfoRead = await AlpacaConnector.ReadMountInfoAsync(server);
                        if (token.IsCancellationRequested) break;
                    }

                    var client = new AlpacaClient(server.Url, server.ClientTransactionId);
                    var deviceState = await client.GetDeviceStateAsync();
                    server.SetClientTransactionId(client.ClientTransactionId);
                    if (token.IsCancellationRequested) break;

                    if (deviceState?.AlpacaResponse?.ErrorNumber > 0)
                    {
                        StatusText = $"Error {deviceState.AlpacaResponse.ErrorNumber}: {deviceState.AlpacaResponse.ErrorMessage}";
                        continue;
                    }
                    if (deviceState is null) continue;

                    UpdateLocalProperties(deviceState);
                    TimerCounter++;
                    ElapsedTime = (int)_timer.Elapsed.TotalSeconds;
                    StatusText = $"Int={TimerInMs}ms, overruns={TimerOverruns}, counter={TimerCounter}, timer={ElapsedTime}s";

                    RotationChanged?.Invoke();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    StatusText = $"Error updating: {ex.Message}";
                }
                finally
                {
                    busy = false;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // stopped
        }
        finally
        {
            _timer.Stop();
        }
    }

    private async Task AutoConnectAsync(CancellationToken token)
    {
        var server = Server;
        if (server is null) return;

        DeviceName = server.ServerName;
        if (server.IsConnected) return;

        StatusText = $"Connecting to {server.ServerName}...";
        try
        {
            var connected = await AlpacaConnector.ConnectAsync(server);
            if (token.IsCancellationRequested) return;
            StatusText = connected ? "Connected" : "Could not connect";
        }
        catch (Exception ex)
        {
            StatusText = $"Connect failed: {ex.Message}";
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

    public void Dispose()
    {
        StopTimerUpdates();
        GC.SuppressFinalize(this);
    }
}
