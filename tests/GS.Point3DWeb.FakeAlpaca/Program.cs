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

using GS.Point3DWeb.FakeAlpaca;

// Standalone fake Alpaca server for the browser and load tests.
//   --port 5400  --mounts 4  --latency 0  --moving true  --host 127.0.0.1
var options = args.Select((a, i) => (a, i)).Where(x => x.a.StartsWith("--"))
    .ToDictionary(x => x.a[2..], x => x.i + 1 < args.Length ? args[x.i + 1] : "true", StringComparer.OrdinalIgnoreCase);
string Opt(string name, string fallback) => options.TryGetValue(name, out var v) ? v : fallback;

await using var server = await FakeAlpacaServer.StartAsync(
    mounts: int.Parse(Opt("mounts", "4")),
    port: int.Parse(Opt("port", "5400")),
    host: Opt("host", "127.0.0.1"));
foreach (var mount in server.Mounts)
{
    mount.LatencyMs = int.Parse(Opt("latency", "0"));
    mount.Moving = bool.Parse(Opt("moving", "true"));
}

Console.WriteLine($"Fake Alpaca server on {server.BaseUrl} with {server.Mounts.Count} mount(s): {server.MountUrl(0)} ...");
Console.WriteLine("READY");
var stop = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.TrySetResult(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.TrySetResult();
await stop.Task;
