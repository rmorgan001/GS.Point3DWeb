using ASCOM.Alpaca.Discovery;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace GS.Alpaca.Support
{
    public class AlpacaDiscoveryService
    {
        private const int DiscoveryPort = 32227;
        private const string DiscoveryMessage = "alpacadiscovery1";
        public static async Task<List<AlpacaTelescopeDevice>> DiscoverTelescopes()
        {
            var telescopes = new List<AlpacaTelescopeDevice>();
            using var discovery = new AlpacaDiscovery();

            discovery.StartDiscovery(
                numberOfPolls: 2,
                pollInterval: 100,
                discoveryPort: 32227,
                discoveryDuration: 2.0,
                resolveDnsName: false,
                useIpV4: true,
                useIpV6: false);

            while (!discovery.DiscoveryComplete)
                await Task.Delay(50);

            var servers = discovery.GetAlpacaDevices();

            using var http = new HttpClient();

            foreach (var server in servers)
            {
                try
                {
                    var url =
                        $"http://{server.HostName}:{server.Port}/management/v1/configureddevices";

                    var json = await http.GetStringAsync(url);

                    using var doc = JsonDocument.Parse(json);

                    foreach (var device in doc.RootElement.GetProperty("Value").EnumerateArray())
                    {
                        var deviceType =
                            device.GetProperty("DeviceType").GetString();

                        if (deviceType != null && deviceType.Equals("Telescope",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            var deviceNumber = device.GetProperty("DeviceNumber").GetInt32();
                            //var uniqueId = device.GetProperty("UniqueID").GetString();
                            var hostName = server.HostName;
                            var port = server.Port;

                            telescopes.Add(new AlpacaTelescopeDevice
                            {
                                DeviceName = device.GetProperty("DeviceName").GetString(), 
                                DeviceUrl =  $"http://{hostName}:{port}/api/v1/telescope/{deviceNumber}/".ToLower()
                            });
                        }
                    }
                }
                catch
                {
                    // ignore servers that fail to respond
                }
            }

            return telescopes;
        }

        public static async Task<List<AlpacaDevice>> DiscoverAsync(CancellationToken token, int timeoutMs = 3000)
        {
            var results = new List<AlpacaDevice>();

            // Prevent duplicates (host + port)
            var seenHosts = new HashSet<string>();

            using var udp = new UdpClient();
            udp.EnableBroadcast = true;

            var endpoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
            var bytes = Encoding.ASCII.GetBytes(DiscoveryMessage);

            await udp.SendAsync(bytes, bytes.Length, endpoint);

            var start = DateTime.UtcNow;

            while ((DateTime.UtcNow - start).TotalMilliseconds < timeoutMs)
            {
                token.ThrowIfCancellationRequested();

                if (udp.Available > 0)
                {
                    var response = await udp.ReceiveAsync(token);

                    var json = Encoding.ASCII.GetString(response.Buffer);

                    try
                    {
                        var doc = JsonDocument.Parse(json);

                        int port = doc.RootElement.GetProperty("AlpacaPort").GetInt32();
                        string host = response.RemoteEndPoint.Address.ToString();

                        string hostKey = $"{host}:{port}";

                        // 🚫 Skip duplicates here
                        if (!seenHosts.Add(hostKey))
                            continue;

                        var devices = await QueryDevices(host, port, token);

                        results.AddRange(devices);
                    }
                    catch
                    {
                        // ignore bad responses
                    }
                }

                await Task.Delay(50, token);
            }

            return results;
        }

        private static async Task<List<AlpacaDevice>> QueryDevices( string host, int port, CancellationToken token)
        {
            var list = new List<AlpacaDevice>();

            using var client = new HttpClient();

            var url = $"http://{host}:{port}/management/v1/configureddevices";

            try
            {
                using var response = await client.GetAsync(url, token);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(token);
                var doc = JsonDocument.Parse(json);

                var devices = doc.RootElement.GetProperty("Value");

                foreach (var unused in devices.EnumerateArray())
                {
                    token.ThrowIfCancellationRequested();

                    //list.Add(new AlpacaDevice
                    //{
                    //    Host = host,
                    //    Port = port,
                    //    DeviceNumber = d.GetProperty("DeviceNumber").GetInt32(),
                    //    DeviceType = d.GetProperty("DeviceType").GetString() ?? "",
                    //    DeviceName = d.GetProperty("DeviceName").GetString() ?? ""
                    //});
                }
            }
            catch
            {
                // ignore per-device errors
            }

            return list;
        }
    }
}
