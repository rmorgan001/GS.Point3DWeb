/* Copyright(C) 2026  Rob Morgan (robert.morgan.e@gmail.com)

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published
    by the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using GS.Alpaca.Shared;
using GS.Alpaca.Support;
using GS.Point3DWeb.FakeAlpaca;
using GS.Point3DWeb.Localization;
using GS.Point3DWeb.Models;
using GS.Point3DWeb.Services;
using GS.Point3DWeb.Support;
using GS.Point3DWeb.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;

switch (args.FirstOrDefault())
{
    case "bench":
        BenchmarkRunner.Run<PollPipelineBenchmarks>(DefaultConfig.Instance.AddJob(Job.ShortRun).WithOptions(ConfigOptions.DisableOptimizationsValidator), args[1..]);
        break;
    case "http":
        await HttpLoad.RunAsync(args[1..]);
        break;
    case "poll":
        await PollScale.RunAsync(args[1..]);
        break;
    default:
        Console.WriteLine("bench | http <baseUrl> [seconds] | poll [views...]");
        break;
}

/// <summary>What one poll costs the server: parsing the devicestate answer and turning it into the view's text and pose.</summary>
[MemoryDiagnoser]
public class PollPipelineBenchmarks
{
    private const string DeviceStateJson = """
        {"ClientTransactionID":12,"ServerTransactionID":99,"ErrorNumber":0,"ErrorMessage":"","Value":[
        {"Name":"Altitude","Value":30.123456},{"Name":"AtHome","Value":false},{"Name":"AtPark","Value":false},
        {"Name":"Azimuth","Value":120.98765},{"Name":"Declination","Value":45.25},{"Name":"IsPulseGuiding","Value":false},
        {"Name":"RightAscension","Value":10.5123},{"Name":"SideOfPier","Value":1},{"Name":"SiderealTime","Value":12.000123},
        {"Name":"Slewing","Value":true},{"Name":"Tracking","Value":true},{"Name":"UTCDate","Value":"2026-11-13T10:00:00.1234567Z"},
        {"Name":"TimeStamp","Value":"2026-11-13T10:00:00.1234567"}]}
        """;

    private readonly AlpacaResponse _parsed = JsonConvert.DeserializeObject<AlpacaResponse>(DeviceStateJson)!;
    private readonly TelescopeDeviceState _state;

    public PollPipelineBenchmarks() => _state = TelescopeMappings.MapDeviceState(_parsed);

    [Benchmark(Description = "parse devicestate JSON (Newtonsoft)")]
    public AlpacaResponse Parse() => JsonConvert.DeserializeObject<AlpacaResponse>(DeviceStateJson)!;

    [Benchmark(Description = "map to TelescopeDeviceState")]
    public TelescopeDeviceState Map() => TelescopeMappings.MapDeviceState(_parsed);

    [Benchmark(Description = "format the six coordinates")]
    public int Format()
    {
        var s = _state;
        return CoordinateFormat.HoursToHms(s.RightAscension, "h ", ":", "", 2).Length
               + CoordinateFormat.DegreesToDms(s.Declination, "° ", ":", "", 2).Length
               + CoordinateFormat.DegreesToDms(s.Azimuth, "° ", ":", "", 2).Length
               + CoordinateFormat.DegreesToDms(s.Altitude, "° ", ":", "", 2).Length
               + CoordinateFormat.HoursToHms(Coordinate.Ra2Ha12(s.RightAscension, s.SiderealTime)).Length
               + CoordinateFormat.HoursToHms(s.SiderealTime, "h ", ":", "", 2).Length;
    }

    [Benchmark(Description = "axes + GSServer pose")]
    public double Pose()
    {
        var axes = Axes.RaDecToAxesXy(AlignmentModes.algGermanPolar, [_state.RightAscension, _state.Declination], _state.SiderealTime, false, 1);
        var pose = Model3D.GetPose(AlignmentModes.algGermanPolar, 51.5, axes[0], axes[1], 0, 0);
        return pose.X + pose.Y;
    }

    [Benchmark(Description = "status line text (L.T, 2 strings)")]
    public int Status() => L.T("Every {0} s · Updates {1} · Slow {2}", "0.5", 1234, 0).Length
                           + L.T("Polling every {0} s. {1} updates received; {2} took longer than the interval.", "0.5", 1234, 0).Length;

    [Benchmark(Description = "whole poll: parse + map + format + pose")]
    public double WholePoll() => Format() + Pose() + TelescopeMappings.MapDeviceState(Parse()).RightAscension;
}

/// <summary>A simple closed-loop HTTP load generator: N workers request the paths as fast as they are answered.</summary>
public static class HttpLoad
{
    public static async Task RunAsync(string[] args)
    {
        var baseUrl = args.ElementAtOrDefault(0) ?? "http://localhost:5300";
        var seconds = int.Parse(args.ElementAtOrDefault(1) ?? "10");
        using var handler = new SocketsHttpHandler { MaxConnectionsPerServer = 1024, AutomaticDecompression = DecompressionMethods.None };
        using var http = new HttpClient(handler) { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };

        (string Name, string Path, string? Etag)[] targets =
        [
            ("home page (prerender)", "/", null),
            ("help page", "/help", null),
            ("log viewer page", "/log", null),
            ("app.css", "/app.css", null),
            ("model .obj 438 KB", "/model3d/models/RitcheyChretienTruss.obj", null),
            ("babylon bundle (br)", "/model3d/babylon.custom.min.js", null),
            ("babylon bundle, 304", "/model3d/babylon.custom.min.js", "etag"),
            ("language switch", "/culture/set?culture=de&redirectUri=%2F", null),
            ("not found", "/no/such/page", null)
        ];

        Console.WriteLine($"HTTP load on {baseUrl}, {seconds} s per row");
        Console.WriteLine($"{"target",-26} {"conc",4} {"req/s",8} {"p50 ms",7} {"p95 ms",7} {"p99 ms",7} {"max ms",7} {"err",5} {"KB/req",7}");
        foreach (var target in targets)
        {
            string? etag = null;
            if (target.Etag is not null)
            {
                using var probe = new HttpRequestMessage(HttpMethod.Get, target.Path);
                probe.Headers.TryAddWithoutValidation("Accept-Encoding", "br, gzip");
                using var first = await http.SendAsync(probe);
                etag = first.Headers.ETag?.ToString();
            }
            foreach (var concurrency in new[] { 1, 16, 64 })
            {
                var r = await RunOneAsync(http, target.Path, etag, concurrency, TimeSpan.FromSeconds(seconds));
                Console.WriteLine($"{target.Name,-26} {concurrency,4} {r.Rps,8:0} {r.P50,7:0.0} {r.P95,7:0.0} {r.P99,7:0.0} {r.Max,7:0.0} {r.Errors,5} {r.KbPerRequest,7:0.0}");
            }
        }
    }

    private static async Task<(double Rps, double P50, double P95, double P99, double Max, int Errors, double KbPerRequest)> RunOneAsync(
        HttpClient http, string path, string? etag, int concurrency, TimeSpan duration)
    {
        var latencies = new List<double>[concurrency];
        var errors = 0;
        long bytes = 0;
        var stop = DateTime.UtcNow + duration;
        await Task.WhenAll(Enumerable.Range(0, concurrency).Select(async w =>
        {
            var mine = latencies[w] = [];
            var buffer = new byte[81920];
            while (DateTime.UtcNow < stop)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, path);
                    request.Headers.TryAddWithoutValidation("Accept-Encoding", "br, gzip");
                    if (etag is not null) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
                    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                    await using var body = await response.Content.ReadAsStreamAsync();
                    int n;
                    while ((n = await body.ReadAsync(buffer)) > 0) Interlocked.Add(ref bytes, n);
                    if ((int)response.StatusCode >= 500) Interlocked.Increment(ref errors);
                }
                catch
                {
                    Interlocked.Increment(ref errors);
                }
                mine.Add(sw.Elapsed.TotalMilliseconds);
            }
        }));
        var all = latencies.SelectMany(l => l).Order().ToArray();
        double Pct(double p) => all.Length == 0 ? double.NaN : all[Math.Min(all.Length - 1, (int)(p * all.Length))];
        return (all.Length / duration.TotalSeconds, Pct(0.5), Pct(0.95), Pct(0.99), all.LastOrDefault(), errors,
            all.Length == 0 ? 0 : bytes / 1024.0 / all.Length);
    }
}

/// <summary>
/// Many 3D views (Model3DViewModel) polling fake mounts in this process, as on a server with many open tabs:
/// how many polls arrive, how close to the interval, and what it costs in CPU and memory.
/// </summary>
public static class PollScale
{
    public static async Task RunAsync(string[] args)
    {
        var counts = args.Length > 0 ? args.Select(int.Parse).ToArray() : [4, 40, 200, 1000];
        Console.WriteLine($"{"views",6} {"mounts",6} {"latency",7} {"polls/s",8} {"expected",8} {"core%",6} {"CPU ms/poll",11} {"alloc KB/poll",13} {"heap MB",8} {"threads",7} {"slow",5}");
        foreach (var latency in new[] { 0, 50 })
        foreach (var views in counts)
        {
            await using var fake = await FakeAlpacaServer.StartAsync(mounts: Math.Min(views, 16));
            foreach (var m in fake.Mounts) { m.Moving = true; m.LatencyMs = latency; }
            var servers = fake.Mounts.Select(m => new AlpacaServer(fake.MountUrl(m.Number), $"m{m.Number}") { PollSeconds = 0.5 }).ToArray();
            foreach (var s in servers) await AlpacaConnector.ConnectAsync(s);
            var settings = (SettingsService)RuntimeHelpers.GetUninitializedObject(typeof(SettingsService));
            var vms = Enumerable.Range(0, views).Select(i => new Model3DViewModel(settings, new DialogService(), NullLogger<Model3DViewModel>.Instance)
            {
                Server = servers[i % servers.Length]
            }).ToList();
            vms.ForEach(v => v.StartTimerUpdates());
            await Task.Delay(3000); // warm up: mount info, JIT
            foreach (var m in fake.Mounts) m.ResetStats();
            GC.Collect();
            var proc = Process.GetCurrentProcess();
            var cpu0 = proc.TotalProcessorTime;
            var alloc0 = GC.GetTotalAllocatedBytes(true);
            var counter0 = vms.Sum(v => v.TimerCounter);
            var sw = Stopwatch.StartNew();
            await Task.Delay(10_000);
            var elapsed = sw.Elapsed.TotalSeconds;
            proc.Refresh();
            var cpuMs = (proc.TotalProcessorTime - cpu0).TotalMilliseconds;
            var alloc = GC.GetTotalAllocatedBytes(true) - alloc0;
            var polls = vms.Sum(v => v.TimerCounter) - counter0;
            // The fake server runs in this process too: its share is included in the CPU and allocations.
            Console.WriteLine($"{views,6} {servers.Length,6} {latency,5}ms {polls / elapsed,8:0} {views / 0.5,8:0} {cpuMs / elapsed / 10,6:0.0} {cpuMs / Math.Max(1, polls),11:0.000} {alloc / 1024.0 / Math.Max(1, polls),13:0.0} {GC.GetTotalMemory(false) / 1048576.0,8:0.0} {proc.Threads.Count,7} {vms.Sum(v => v.TimerOverruns),5}");
            vms.ForEach(v => v.Dispose());
        }
    }
}
