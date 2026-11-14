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

using ASCOM.Alpaca.Discovery;
using System.Text.Json;

namespace GS.Alpaca.Support
{
    public class AlpacaDiscoveryService
    {
        private const int DiscoveryPort = 32227;

        // Shared client with a short timeout: one host that answers discovery but hangs on its device
        // list must not hold up the dialog (the 100 s HttpClient default would).
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

        /// <summary>
        /// Finds Alpaca servers on the local network (UDP discovery), then asks each one, in parallel,
        /// for its configured telescopes.
        /// </summary>
        public static async Task<List<AlpacaTelescopeDevice>> DiscoverTelescopes()
        {
            using var discovery = new AlpacaDiscovery();

            discovery.StartDiscovery(
                numberOfPolls: 2,
                pollInterval: 100,
                discoveryPort: DiscoveryPort,
                discoveryDuration: 2.0,
                resolveDnsName: false,
                useIpV4: true,
                useIpV6: false);

            while (!discovery.DiscoveryComplete)
                await Task.Delay(50);

            var perServer = await Task.WhenAll(discovery.GetAlpacaDevices().Select(QueryTelescopesAsync));
            return [.. perServer.SelectMany(telescopes => telescopes)];
        }

        /// <summary>The telescopes one server reports; none if it doesn't answer in time.</summary>
        private static async Task<List<AlpacaTelescopeDevice>> QueryTelescopesAsync(AlpacaDevice server)
        {
            var telescopes = new List<AlpacaTelescopeDevice>();
            try
            {
                var url = $"http://{server.HostName}:{server.Port}/management/v1/configureddevices";
                var json = await Http.GetStringAsync(url);

                using var doc = JsonDocument.Parse(json);
                foreach (var device in doc.RootElement.GetProperty("Value").EnumerateArray())
                {
                    var deviceType = device.GetProperty("DeviceType").GetString();
                    if (!string.Equals(deviceType, "Telescope", StringComparison.OrdinalIgnoreCase)) continue;

                    var deviceNumber = device.GetProperty("DeviceNumber").GetInt32();
                    telescopes.Add(new AlpacaTelescopeDevice
                    {
                        DeviceName = device.GetProperty("DeviceName").GetString(),
                        DeviceUrl = $"http://{server.HostName}:{server.Port}/api/v1/telescope/{deviceNumber}/".ToLower()
                    });
                }
            }
            catch
            {
                // ignore servers that fail to respond
            }
            return telescopes;
        }
    }
}
