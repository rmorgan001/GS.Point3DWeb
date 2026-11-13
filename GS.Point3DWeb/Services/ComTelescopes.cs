using System.Collections.Concurrent;
using System.Runtime.Versioning;
using ASCOM.Com;
using ASCOM.Com.DriverAccess;
using ASCOM.Common;
using GS.Alpaca.Support;

namespace GS.Point3DWeb.Services;

/// <summary>
/// ASCOM telescope drivers on this computer, used through COM (Windows only), alongside Alpaca servers.
/// A server whose address is <c>ascom:&lt;ProgID&gt;</c> (see <see cref="Models.AlpacaServer.IsCom"/>) is one of these.
///
/// COM drivers expect to be created and called on one single-threaded-apartment (STA) thread, so each driver
/// gets its own thread, and every view and tab showing it shares the one driver instance. The ASCOM Chooser
/// isn't used (see <see cref="GetDrivers"/>).
/// </summary>
[SupportedOSPlatform("windows")]
public static class ComTelescopes
{
    /// <summary>Whether ASCOM COM drivers can be used here (Windows only).</summary>
    [SupportedOSPlatformGuard("windows")]
    public static bool IsSupported => OperatingSystem.IsWindows();

    private sealed class Driver(string progId)
    {
        public StaThread Thread { get; } = new($"ASCOM {progId}");
        public Telescope? Telescope { get; set; }
    }

    // By ProgID (case-insensitive, as COM treats it). Only drivers this app has connected are here.
    private static readonly ConcurrentDictionary<string, Driver> Drivers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The telescope drivers registered with ASCOM on this computer, by name. Listed in the browser instead
    /// of the ASCOM Chooser: the Chooser is a Windows dialog, so it would open on this computer's desktop
    /// (invisible to a browser elsewhere), and it isn't Intel CET compatible under .NET 9 or later.
    /// </summary>
    public static IReadOnlyList<(string ProgId, string Name)> GetDrivers()
        => [.. Profile.GetDrivers(DeviceTypes.Telescope)
            .Where(d => !string.IsNullOrWhiteSpace(d.ProgID))
            .Select(d => (d.ProgID, string.IsNullOrWhiteSpace(d.Name) ? d.ProgID : d.Name))
            .OrderBy(d => d.Item2, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>
    /// Creates the driver if needed and connects it. Returns whether it reports connected. A driver that
    /// can't be created, or fails or refuses to connect, is released again (unless another view had it
    /// connected already), and the error is thrown.
    /// </summary>
    public static async Task<bool> ConnectAsync(string progId)
    {
        var driver = Drivers.GetOrAdd(progId, id => new Driver(id));
        var wasConnected = false;
        try
        {
            var connected = await WithTimeout(driver.Thread.Run(() =>
            {
                driver.Telescope ??= new Telescope(progId);
                wasConnected = driver.Telescope.Connected;
                if (!wasConnected) driver.Telescope.Connected = true;
                return driver.Telescope.Connected;
            }), ConnectTimeout);
            if (!connected) await DisconnectAsync(progId);
            return connected;
        }
        catch
        {
            if (!wasConnected) _ = DisconnectAsync(progId); // don't wait on a driver that may be stuck
            throw;
        }
    }

    // Connecting can be slow (a driver that runs as its own program starts up, a mount answers);
    // a single read should not be.
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Stops waiting for a driver call that hasn't finished in time. The call itself carries on (it can't
    /// be stopped), and later calls queue behind it on the driver's thread.
    /// </summary>
    private static async Task<T> WithTimeout<T>(Task<T> call, TimeSpan timeout)
    {
        try
        {
            return await call.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"the ASCOM driver did not answer within {timeout.TotalSeconds:0} s");
        }
    }

    /// <summary>
    /// Disconnects the driver and releases it (so a driver that runs as its own program can close).
    /// Does nothing if this app hasn't connected it.
    /// </summary>
    public static async Task DisconnectAsync(string progId)
    {
        if (!Drivers.TryRemove(progId, out var driver)) return;
        try
        {
            await WithTimeout(driver.Thread.Run(() =>
            {
                if (driver.Telescope is not { } telescope) return true;
                try
                {
                    telescope.Connected = false;
                }
                finally
                {
                    telescope.Dispose();
                    driver.Telescope = null;
                }
                return true;
            }), CallTimeout);
        }
        finally
        {
            driver.Thread.Dispose();
        }
    }

    /// <summary>
    /// Whether the driver reports connected. False if this app hasn't connected it: the driver isn't
    /// started just to ask (that would launch a driver that runs as its own program).
    /// </summary>
    public static Task<bool> IsConnectedAsync(string progId)
        => Drivers.TryGetValue(progId, out var driver)
            ? WithTimeout(driver.Thread.Run(() => driver.Telescope?.Connected ?? false), CallTimeout)
            : Task.FromResult(false);

    /// <summary>The mount's alignment mode (ASCOM numbering) and site latitude; null for one it doesn't implement.</summary>
    public static Task<(int? Mode, double? Latitude)> ReadMountInfoAsync(string progId)
        => Run(progId, telescope =>
        {
            int? mode = null;
            double? latitude = null;
            try { mode = (int)telescope.AlignmentMode; } catch { /* not implemented */ }
            try { latitude = telescope.SiteLatitude; } catch { /* not implemented */ }
            return (mode, latitude);
        });

    /// <summary>
    /// The mount's position: TelescopeState in one call where the driver supports it (ITelescopeV4), otherwise
    /// each property on its own. Azimuth, Altitude and SideOfPier are optional on an equatorial mount.
    /// </summary>
    public static Task<TelescopeDeviceState> ReadStateAsync(string progId)
        => Run(progId, telescope =>
        {
            var state = new TelescopeDeviceState { SideOfPier = -1 }; // unknown unless the mount says
            try
            {
                var s = telescope.TelescopeState;
                if (s.RightAscension is { } ra && s.Declination is { } dec && s.SiderealTime is { } lst)
                {
                    state.RightAscension = ra;
                    state.Declination = dec;
                    state.SiderealTime = lst;
                    if (s.Azimuth is { } az) state.Azimuth = az;
                    if (s.Altitude is { } alt) state.Altitude = alt;
                    if (s.SideOfPier is { } side) state.SideOfPier = (int)side;
                    return state;
                }
            }
            catch
            {
                // an older driver without TelescopeState: read the properties below
            }

            state.RightAscension = telescope.RightAscension;
            state.Declination = telescope.Declination;
            state.SiderealTime = telescope.SiderealTime;
            try { state.Azimuth = telescope.Azimuth; } catch { /* optional */ }
            try { state.Altitude = telescope.Altitude; } catch { /* optional */ }
            try { state.SideOfPier = (int)telescope.SideOfPier; } catch { /* optional */ }
            return state;
        });

    /// <summary>
    /// Disconnects and releases every driver this app connected (when the app shuts down), so drivers that
    /// count their clients don't stay connected for it. Waits a few seconds at most.
    /// </summary>
    public static void DisconnectAll()
    {
        var pending = Drivers.Keys.Select(progId => DisconnectAsync(progId)).ToArray();
        try
        {
            Task.WaitAll(pending, TimeSpan.FromSeconds(5));
        }
        catch
        {
            // shutting down: nothing more to do
        }
    }

    /// <summary>Runs work on a connected driver's thread; throws if this app hasn't connected it.</summary>
    private static Task<T> Run<T>(string progId, Func<Telescope, T> work)
    {
        if (!Drivers.TryGetValue(progId, out var driver))
            return Task.FromException<T>(new InvalidOperationException($"{progId} is not connected"));
        return WithTimeout(driver.Thread.Run(() => driver.Telescope is { } telescope
            ? work(telescope)
            : throw new InvalidOperationException($"{progId} is not connected")), CallTimeout);
    }

    /// <summary>A single-threaded-apartment thread that runs queued work one item at a time.</summary>
    private sealed class StaThread : IDisposable
    {
        private readonly BlockingCollection<Action> _work = [];

        public StaThread(string name)
        {
            var thread = new Thread(() =>
            {
                foreach (var item in _work.GetConsumingEnumerable()) item();
            })
            {
                IsBackground = true,
                Name = name
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        public Task<T> Run<T>(Func<T> work)
        {
            var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                _work.Add(() =>
                {
                    try
                    {
                        result.SetResult(work());
                    }
                    catch (Exception ex)
                    {
                        result.SetException(ex);
                    }
                });
            }
            catch (InvalidOperationException)
            {
                // the driver was disconnected and its thread stopped
                result.SetException(new ObjectDisposedException("The ASCOM driver was disconnected."));
            }
            return result.Task;
        }

        /// <summary>Stops the thread once the work already queued has run.</summary>
        public void Dispose() => _work.CompleteAdding();
    }
}
