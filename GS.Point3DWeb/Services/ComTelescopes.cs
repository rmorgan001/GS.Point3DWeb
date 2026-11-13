using System.Collections.Concurrent;
using System.Runtime.InteropServices;
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
/// COM drivers expect to be created and called on one single-threaded-apartment (STA) thread that handles
/// window messages, so each driver gets its own such thread. Every view and tab showing a driver shares the
/// one driver instance, which is held for each server ("owner") that connected it and released once the last
/// one lets go. A driver being released is disconnected and released completely before a new instance of it is
/// created. The ASCOM Chooser isn't used (see <see cref="GetDrivers"/>).
/// </summary>
[SupportedOSPlatform("windows")]
public static class ComTelescopes
{
    /// <summary>Whether ASCOM COM drivers can be used here (Windows only).</summary>
    [SupportedOSPlatformGuard("windows")]
    public static bool IsSupported => OperatingSystem.IsWindows();

    private sealed class Driver(string progId)
    {
        public string ProgId { get; } = progId;
        public StaThread Thread { get; } = new($"ASCOM {progId}");

        /// <summary>The driver instance; only used on <see cref="Thread"/>.</summary>
        public Telescope? Telescope;

        // The rest is guarded by Gate.

        /// <summary>The servers (by ID) holding this driver; it is released when the last one lets go.</summary>
        public readonly HashSet<string> Owners = [];

        // Reads under way, shared by every caller: a slow or stuck driver gets one of each queued, not one per poll.
        public Task<bool>? ConnectedRead;
        public Task<TelescopeDeviceState>? StateRead;
        public Task<(int? Mode, double? Latitude)>? InfoRead;
    }

    private static readonly object Gate = new();

    // Under Gate. By ProgID (case-insensitive, as COM treats it). Only drivers some server holds are here.
    private static readonly Dictionary<string, Driver> Drivers = new(StringComparer.OrdinalIgnoreCase);

    // Under Gate. Drivers being released, by ProgID: a new instance waits until the old one is released.
    private static readonly Dictionary<string, Task> Releasing = new(StringComparer.OrdinalIgnoreCase);

    // Connecting can be slow (a driver that runs as its own program starts up, a mount answers);
    // a single read or a disconnect should not be.
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(10);

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
    /// Connects the driver for <paramref name="owner"/> (a server's ID), creating it if no server holds it yet.
    /// Returns whether it reports connected. If it can't be created, or fails or refuses to connect, the
    /// owner's hold is dropped again (unless it already held the driver), and the error is thrown. Other
    /// owners keep the driver.
    /// </summary>
    public static async Task<bool> ConnectAsync(string progId, string owner)
    {
        var (driver, added) = await AcquireAsync(progId, owner);
        try
        {
            var connected = await WithTimeout(driver.Thread.Run(() =>
            {
                driver.Telescope ??= new Telescope(driver.ProgId);
                if (!driver.Telescope.Connected) driver.Telescope.Connected = true;
                return driver.Telescope.Connected;
            }), ConnectTimeout);
            if (!connected && added) await ReleaseAsync(progId, owner);
            return connected;
        }
        catch
        {
            // Don't wait: the driver may be stuck. A connect still queued runs before the release does.
            if (added) _ = ReleaseAsync(progId, owner);
            throw;
        }
    }

    /// <summary>
    /// Adds the owner to the driver, creating the driver if needed, once any earlier instance of it has been
    /// released. Returns whether the owner was added (false if it already held the driver).
    /// </summary>
    private static async Task<(Driver Driver, bool Added)> AcquireAsync(string progId, string owner)
    {
        while (true)
        {
            Task? releasing;
            lock (Gate)
            {
                if (!Releasing.TryGetValue(progId, out releasing) || releasing.IsCompleted)
                {
                    Releasing.Remove(progId);
                    if (!Drivers.TryGetValue(progId, out var driver))
                        Drivers[progId] = driver = new Driver(progId);
                    return (driver, driver.Owners.Add(owner));
                }
            }

            try
            {
                await releasing.WaitAsync(ConnectTimeout);
            }
            catch (TimeoutException)
            {
                throw new TimeoutException("the ASCOM driver is still being disconnected; try again later");
            }
            catch
            {
                // a release that failed has still finished
            }
        }
    }

    /// <summary>
    /// Drops <paramref name="owner"/>'s hold on the driver. When no server holds it any more, it is disconnected
    /// and released (so a driver that runs as its own program can close); other owners keep it connected.
    /// Does nothing if the owner doesn't hold it. Throws if the driver fails or doesn't answer while being
    /// disconnected; it is released either way.
    /// </summary>
    public static Task ReleaseAsync(string progId, string owner)
    {
        Task release;
        lock (Gate)
        {
            if (!Drivers.TryGetValue(progId, out var driver) || !driver.Owners.Remove(owner) || driver.Owners.Count > 0)
                return Task.CompletedTask;
            Drivers.Remove(progId);
            Releasing[progId] = release = DisposeAsync(driver);
        }
        return WithTimeout(release, CallTimeout);
    }

    /// <summary>Disconnects and releases a driver no server holds any more, then stops its thread.</summary>
    private static async Task DisposeAsync(Driver driver)
    {
        try
        {
            await driver.Thread.Run(() =>
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
            });
        }
        finally
        {
            driver.Thread.Dispose();
        }
    }

    /// <summary>
    /// Whether <paramref name="owner"/> holds the driver and it reports connected. False if the owner doesn't
    /// hold it: the driver isn't started just to ask (that would launch a driver that runs as its own program).
    /// </summary>
    public static Task<bool> IsConnectedAsync(string progId, string owner)
    {
        lock (Gate)
        {
            if (!Drivers.TryGetValue(progId, out var driver) || !driver.Owners.Contains(owner))
                return Task.FromResult(false);
            return Shared(driver, ref driver.ConnectedRead, telescope => telescope.Connected);
        }
    }

    /// <summary>The mount's alignment mode (ASCOM numbering) and site latitude; null for one it doesn't implement.</summary>
    public static Task<(int? Mode, double? Latitude)> ReadMountInfoAsync(string progId)
        => Read(progId, driver => ref driver.InfoRead, telescope =>
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
        => Read(progId, driver => ref driver.StateRead, telescope =>
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
    /// Disconnects and releases every driver (when the app shuts down), so drivers that count their clients
    /// don't stay connected for it. Waits a few seconds at most.
    /// </summary>
    public static void DisconnectAll()
    {
        Task[] pending;
        lock (Gate)
        {
            pending = [.. Drivers.Values.Select(DisposeAsync), .. Releasing.Values];
            Drivers.Clear();
        }
        try
        {
            Task.WaitAll(pending, TimeSpan.FromSeconds(5));
        }
        catch
        {
            // shutting down: nothing more to do
        }
    }

    private delegate ref Task<T>? PendingRead<T>(Driver driver);

    /// <summary>Reads from a held driver (sharing a read already under way); throws if no server holds it.</summary>
    private static Task<T> Read<T>(string progId, PendingRead<T> pending, Func<Telescope, T> work)
    {
        lock (Gate)
        {
            return Drivers.TryGetValue(progId, out var driver)
                ? Shared(driver, ref pending(driver), work)
                : Task.FromException<T>(new InvalidOperationException($"{progId} is not connected"));
        }
    }

    /// <summary>
    /// Runs a read on the driver's thread, or joins the same read if one is already queued or running. Call
    /// under <see cref="Gate"/>. Each caller stops waiting after <see cref="CallTimeout"/>.
    /// </summary>
    private static Task<T> Shared<T>(Driver driver, ref Task<T>? pending, Func<Telescope, T> work)
    {
        if (pending is not { IsCompleted: false })
        {
            pending = driver.Thread.Run(() => driver.Telescope is { } telescope
                ? work(telescope)
                : throw new InvalidOperationException($"{driver.ProgId} is not connected"));
        }
        return WithTimeout(pending, CallTimeout);
    }

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

    private static async Task WithTimeout(Task call, TimeSpan timeout)
    {
        try
        {
            await call.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"the ASCOM driver did not answer within {timeout.TotalSeconds:0} s");
        }
    }

    /// <summary>
    /// A single-threaded-apartment thread that runs queued work one item at a time and, while waiting,
    /// dispatches window messages, as COM drivers expect of the thread that created them (WinForms timers,
    /// hidden windows, calls from an out-of-process driver).
    /// </summary>
    private sealed class StaThread : IDisposable
    {
        private readonly ConcurrentQueue<Action> _work = new();
        private readonly AutoResetEvent _signal = new(false);
        private readonly object _gate = new();
        private bool _stopping; // under _gate

        public StaThread(string name)
        {
            var thread = new Thread(Loop) { IsBackground = true, Name = name };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        private void Loop()
        {
            var handles = new[] { _signal.SafeWaitHandle.DangerousGetHandle() };
            try
            {
                while (true)
                {
                    while (_work.TryDequeue(out var item)) item();
                    lock (_gate)
                    {
                        if (_stopping && _work.IsEmpty) return;
                    }

                    // Wake for new work or for a window message.
                    MsgWaitForMultipleObjectsEx(1, handles, Infinite, QsAllInput, MwmoInputAvailable);
                    while (PeekMessage(out var msg, IntPtr.Zero, 0, 0, PmRemove))
                    {
                        TranslateMessage(in msg);
                        DispatchMessage(in msg);
                    }
                }
            }
            finally
            {
                _signal.Dispose();
            }
        }

        public Task<T> Run<T>(Func<T> work)
        {
            var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
            {
                if (_stopping)
                {
                    // the driver was released and its thread stopped
                    result.SetException(new ObjectDisposedException("The ASCOM driver was disconnected."));
                    return result.Task;
                }
                _work.Enqueue(() =>
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
                _signal.Set();
            }
            return result.Task;
        }

        /// <summary>Stops the thread once the work already queued has run.</summary>
        public void Dispose()
        {
            lock (_gate)
            {
                if (_stopping) return;
                _stopping = true;
                _signal.Set();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Msg
        {
            public IntPtr Hwnd;
            public uint Message;
            public IntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public int X;
            public int Y;
            public uint Private;
        }

        private const uint Infinite = 0xFFFFFFFF;
        private const uint QsAllInput = 0x04FF;
        private const uint MwmoInputAvailable = 0x0004;
        private const uint PmRemove = 0x0001;

        [DllImport("user32.dll")]
        private static extern uint MsgWaitForMultipleObjectsEx(uint count, IntPtr[] handles, uint milliseconds, uint wakeMask, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PeekMessage(out Msg msg, IntPtr hwnd, uint filterMin, uint filterMax, uint remove);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TranslateMessage(in Msg msg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(in Msg msg);
    }
}
