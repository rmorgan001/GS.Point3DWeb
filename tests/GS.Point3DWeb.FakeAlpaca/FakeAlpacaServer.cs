using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GS.Point3DWeb.FakeAlpaca;

/// <summary>How a fake mount answers devicestate.</summary>
public enum DeviceStateMode
{
    /// <summary>A list of Name / Value pairs (ITelescopeV4).</summary>
    Normal,
    /// <summary>Alpaca error 0x400 NotImplemented (a server from before ASCOM Platform 7).</summary>
    NotImplemented,
    /// <summary>HTTP 400, as some older servers answer an unknown method.</summary>
    Http400,
    /// <summary>HTTP 404.</summary>
    Http404,
    /// <summary>Answers, but with a single value instead of a list.</summary>
    NoArray,
    /// <summary>Alpaca error 0x500 with a message.</summary>
    DriverError
}

public enum ConnectMode { Accept, RefuseWithError, IgnoreSilently }

public enum DisconnectMode { Accept, RefuseAndStayConnected, ErrorButDisconnects }

/// <summary>One telescope device on the fake server. Every field can be changed while it runs.</summary>
public sealed class FakeMount(int number)
{
    public int Number { get; } = number;
    public volatile bool Connected;

    public double RightAscension = 10.5;      // hours
    public double Declination = 45.25;        // degrees
    public double SiderealTime = 12.0;        // hours
    public double Altitude = 30.0;
    public double Azimuth = 120.0;
    public int SideOfPier = 0;                // 0 east, 1 west, -1 unknown
    public int AlignmentMode = 2;             // German polar
    public double Latitude = 51.5;
    public bool Tracking = true, Slewing, AtPark, AtHome, IsPulseGuiding;

    /// <summary>Moves the mount with time (RA and Dec swing, LST runs), as a mount that is slewing.</summary>
    public bool Moving;

    public DeviceStateMode DeviceState = DeviceStateMode.Normal;
    public ConnectMode Connect = ConnectMode.Accept;
    public DisconnectMode Disconnect = DisconnectMode.Accept;

    /// <summary>Alpaca error 0x400 for alignmentmode / sitelatitude.</summary>
    public bool AlignmentModeNotImplemented, LatitudeNotImplemented;

    /// <summary>HTTP 404 for alignmentmode / sitelatitude (servers that don't use Alpaca's error).</summary>
    public bool MountInfoHttp404;

    /// <summary>A position property (RA, Dec, LST) answered with an error, for the one-at-a-time read.</summary>
    public bool PositionError;

    /// <summary>Extra time before each answer.</summary>
    public int LatencyMs;

    /// <summary>Extra time before an answer to the connected PUT only.</summary>
    public int ConnectLatencyMs;

    /// <summary>Every request is answered HTTP 503 (the server is going down).</summary>
    public volatile bool Down;

    /// <summary>Requests that need a login (HTTP basic) when set.</summary>
    public string? UserName, Password;

    // Request statistics.
    public readonly ConcurrentDictionary<string, int> Calls = new(StringComparer.OrdinalIgnoreCase);
    public readonly ConcurrentQueue<RequestRecord> Requests = new();
    public int TotalRequests => Calls.Values.Sum();

    public int CallsTo(string method) => Calls.TryGetValue(method, out var n) ? n : 0;

    public void ResetStats()
    {
        Calls.Clear();
        Requests.Clear();
    }
}

/// <summary>A request the fake server answered.</summary>
public sealed record RequestRecord(DateTime Utc, string HttpMethod, string Path, string Method, string? ClientTransactionId,
    string? Authorization, IReadOnlyDictionary<string, string> Form);

/// <summary>
/// A fake ASCOM Alpaca telescope server (Kestrel), with any number of devices, for tests. Path matching is
/// ordinal (case-sensitive), so a test can check the client keeps the case of the address it was given.
/// </summary>
public sealed class FakeAlpacaServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly FakeMount[] _mounts;
    private readonly string _prefix;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private int _serverTransactionId;

    private FakeAlpacaServer(WebApplication app, FakeMount[] mounts, string prefix)
    {
        _app = app;
        _mounts = mounts;
        _prefix = prefix;
    }

    public string BaseUrl { get; private set; } = "";

    public FakeMount this[int number] => _mounts[number];

    public IReadOnlyList<FakeMount> Mounts => _mounts;

    /// <summary>The device address the app should be given for a mount.</summary>
    public string MountUrl(int number = 0) => $"{BaseUrl}{_prefix}/api/v1/telescope/{number}/";

    /// <param name="prefix">A path before /api/v1 (a proxy prefix), matched case-sensitively; "" for none.</param>
    public static async Task<FakeAlpacaServer> StartAsync(int mounts = 1, int port = 0, string prefix = "",
        string host = "127.0.0.1")
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://{host}:{port}");
        builder.WebHost.ConfigureKestrel(k => k.Limits.MaxConcurrentConnections = null);
        var app = builder.Build();
        var server = new FakeAlpacaServer(app, [.. Enumerable.Range(0, mounts).Select(n => new FakeMount(n))], prefix);
        app.Run(server.HandleAsync);
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        server.BaseUrl = address.Replace("0.0.0.0", "127.0.0.1").Replace("[::]", "127.0.0.1").TrimEnd('/');
        return server;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private async Task HandleAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        if (path == "/management/v1/configureddevices")
        {
            await WriteJsonAsync(context, new Dictionary<string, object?>
            {
                ["Value"] = _mounts.Select(m => new Dictionary<string, object?>
                {
                    ["DeviceName"] = $"Fake mount {m.Number}",
                    ["DeviceType"] = "Telescope",
                    ["DeviceNumber"] = m.Number,
                    ["UniqueID"] = $"fake-{m.Number}"
                }).ToArray(),
                ["ErrorNumber"] = 0,
                ["ErrorMessage"] = ""
            });
            return;
        }

        // Control endpoint for the browser tests: /fake/{n}?Down=true&LatencyMs=200 ...
        if (path.StartsWith("/fake/", StringComparison.Ordinal)
            && int.TryParse(path["/fake/".Length..], out var controlled) && controlled >= 0 && controlled < _mounts.Length)
        {
            var mount = _mounts[controlled];
            foreach (var (key, value) in context.Request.Query) SetField(mount, key, value.ToString());
            await WriteJsonAsync(context, new { mount.Number, mount.Connected, mount.Down, mount.LatencyMs, Requests = mount.TotalRequests });
            return;
        }
        if (path == "/fake/stats")
        {
            await WriteJsonAsync(context, _mounts.Select(m => new { m.Number, m.Connected, Calls = m.Calls.ToDictionary() }));
            return;
        }

        var apiStart = _prefix + "/api/v1/telescope/";
        if (!path.StartsWith(apiStart, StringComparison.Ordinal))
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsync("Not found");
            return;
        }

        var rest = path[apiStart.Length..].Split('/', 2);
        if (rest.Length != 2 || !int.TryParse(rest[0], out var number) || number < 0 || number >= _mounts.Length)
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsync("No such device");
            return;
        }
        var m = _mounts[number];
        var method = rest[1].TrimEnd('/');

        var form = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (HttpMethods.IsPut(context.Request.Method) && context.Request.HasFormContentType)
        {
            foreach (var (key, value) in await context.Request.ReadFormAsync()) form[key] = value.ToString();
        }
        var transaction = context.Request.Query["ClientTransactionID"].FirstOrDefault()
                          ?? (form.TryGetValue("ClientTransactionID", out var t) ? t : null);
        m.Calls.AddOrUpdate(method, 1, (_, n) => n + 1);
        m.Requests.Enqueue(new RequestRecord(DateTime.UtcNow, context.Request.Method, path, method, transaction,
            context.Request.Headers.Authorization.FirstOrDefault(), form));
        while (m.Requests.Count > 5000) m.Requests.TryDequeue(out _);

        var latency = m.LatencyMs + (method == "connected" && HttpMethods.IsPut(context.Request.Method) ? m.ConnectLatencyMs : 0);
        if (latency > 0)
        {
            try
            {
                await Task.Delay(latency, context.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        if (m.Down)
        {
            context.Response.StatusCode = 503;
            await context.Response.WriteAsync("Service unavailable");
            return;
        }

        if (m.UserName is not null)
        {
            var expected = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{m.UserName}:{m.Password}"));
            if (context.Request.Headers.Authorization.FirstOrDefault() != expected)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Bad user name or password");
                return;
            }
        }

        if (m.Moving) Move(m);

        if (HttpMethods.IsPut(context.Request.Method))
        {
            await HandlePutAsync(context, m, method, form, transaction);
            return;
        }

        switch (method)
        {
            case "connected":
                await AnswerAsync(context, transaction, m.Connected);
                return;
            case "devicestate":
                await DeviceStateAsync(context, m, transaction);
                return;
            case "alignmentmode":
                if (m.MountInfoHttp404) { context.Response.StatusCode = 404; return; }
                if (m.AlignmentModeNotImplemented) { await ErrorAsync(context, transaction, 0x400, "AlignmentMode is not implemented"); return; }
                await AnswerAsync(context, transaction, m.AlignmentMode);
                return;
            case "sitelatitude":
                if (m.MountInfoHttp404) { context.Response.StatusCode = 404; return; }
                if (m.LatitudeNotImplemented) { await ErrorAsync(context, transaction, 0x400, "SiteLatitude is not implemented"); return; }
                await AnswerAsync(context, transaction, m.Latitude);
                return;
        }

        if (!m.Connected)
        {
            await ErrorAsync(context, transaction, 0x407, "Not connected");
            return;
        }
        switch (method)
        {
            case "rightascension" when m.PositionError:
                await ErrorAsync(context, transaction, 0x500, "RA read failed");
                return;
            case "rightascension": await AnswerAsync(context, transaction, m.RightAscension); return;
            case "declination": await AnswerAsync(context, transaction, m.Declination); return;
            case "siderealtime": await AnswerAsync(context, transaction, m.SiderealTime); return;
            case "altitude": await AnswerAsync(context, transaction, m.Altitude); return;
            case "azimuth": await AnswerAsync(context, transaction, m.Azimuth); return;
            case "sideofpier": await AnswerAsync(context, transaction, m.SideOfPier); return;
            default:
                await ErrorAsync(context, transaction, 0x400, $"{method} is not implemented");
                return;
        }
    }

    private async Task HandlePutAsync(HttpContext context, FakeMount m, string method, Dictionary<string, string> form, string? transaction)
    {
        if (method != "connected" || !form.TryGetValue("Connected", out var value) || !bool.TryParse(value, out var connect))
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("Bad request");
            return;
        }

        if (connect)
        {
            switch (m.Connect)
            {
                case ConnectMode.Accept:
                    m.Connected = true;
                    await AnswerAsync(context, transaction, null);
                    return;
                case ConnectMode.RefuseWithError:
                    await ErrorAsync(context, transaction, 0x500, "The mount's serial port is in use");
                    return;
                default:
                    await AnswerAsync(context, transaction, null); // says OK, stays disconnected
                    return;
            }
        }

        switch (m.Disconnect)
        {
            case DisconnectMode.Accept:
                m.Connected = false;
                await AnswerAsync(context, transaction, null);
                return;
            case DisconnectMode.RefuseAndStayConnected:
                await ErrorAsync(context, transaction, 0x500, "Another client still needs the mount");
                return;
            default:
                m.Connected = false;
                await ErrorAsync(context, transaction, 0x500, "Disconnect reported an error");
                return;
        }
    }

    private void Move(FakeMount m)
    {
        var seconds = _clock.Elapsed.TotalSeconds;
        m.SiderealTime = (12.0 + seconds / 3600.0 * 1.0027379) % 24.0;
        m.RightAscension = (10.5 + 2.0 * Math.Sin(seconds / 7.0) + 24.0) % 24.0;
        m.Declination = 30.0 + 50.0 * Math.Sin(seconds / 11.0);
        m.Azimuth = (180.0 + 170.0 * Math.Sin(seconds / 9.0) + 360.0) % 360.0;
        m.Altitude = 45.0 + 40.0 * Math.Sin(seconds / 13.0);
        m.SideOfPier = Math.Sin(seconds / 17.0) >= 0 ? 0 : 1;
        m.Slewing = true;
    }

    private async Task DeviceStateAsync(HttpContext context, FakeMount m, string? transaction)
    {
        switch (m.DeviceState)
        {
            case DeviceStateMode.NotImplemented:
                await ErrorAsync(context, transaction, 0x400, "DeviceState is not implemented");
                return;
            case DeviceStateMode.Http400:
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("Unknown method devicestate");
                return;
            case DeviceStateMode.Http404:
                context.Response.StatusCode = 404;
                return;
            case DeviceStateMode.NoArray:
                await AnswerAsync(context, transaction, "not a list");
                return;
            case DeviceStateMode.DriverError:
                await ErrorAsync(context, transaction, 0x500, "Driver exploded");
                return;
        }
        if (!m.Connected)
        {
            await ErrorAsync(context, transaction, 0x407, "Not connected");
            return;
        }

        var now = DateTime.UtcNow;
        var values = new (string Name, object Value)[]
        {
            ("Altitude", m.Altitude), ("AtHome", m.AtHome), ("AtPark", m.AtPark), ("Azimuth", m.Azimuth),
            ("Declination", m.Declination), ("IsPulseGuiding", m.IsPulseGuiding), ("RightAscension", m.RightAscension),
            ("SideOfPier", m.SideOfPier), ("SiderealTime", m.SiderealTime), ("Slewing", m.Slewing),
            ("Tracking", m.Tracking), ("UTCDate", now.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture)),
            ("TimeStamp", now.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture))
        };
        await AnswerAsync(context, transaction, values.Select(v => new Dictionary<string, object> { ["Name"] = v.Name, ["Value"] = v.Value }).ToArray());
    }

    private Task AnswerAsync(HttpContext context, string? transaction, object? value)
        => WriteJsonAsync(context, Envelope(transaction, value, 0, ""));

    private Task ErrorAsync(HttpContext context, string? transaction, int error, string message)
        => WriteJsonAsync(context, Envelope(transaction, null, error, message));

    private Dictionary<string, object?> Envelope(string? transaction, object? value, int error, string message)
    {
        var envelope = new Dictionary<string, object?>
        {
            ["ClientTransactionID"] = long.TryParse(transaction, out var id) ? id : 0,
            ["ServerTransactionID"] = Interlocked.Increment(ref _serverTransactionId),
            ["ErrorNumber"] = error,
            ["ErrorMessage"] = message
        };
        if (value is not null) envelope["Value"] = value;
        return envelope;
    }

    private static Task WriteJsonAsync(HttpContext context, object body)
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(body));
    }

    /// <summary>Sets a public field of a mount from text (the control endpoint).</summary>
    public static void SetField(FakeMount mount, string name, string value)
    {
        var field = typeof(FakeMount).GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance
                                                     | System.Reflection.BindingFlags.IgnoreCase);
        if (field is null) return;
        var type = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;
        object? parsed = type.IsEnum ? Enum.Parse(type, value, ignoreCase: true)
            : type == typeof(string) ? value
            : Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        field.SetValue(mount, parsed);
    }
}
