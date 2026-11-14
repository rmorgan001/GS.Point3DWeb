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

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;

// ReSharper disable StringLiteralTypo

namespace GS.Alpaca.Support
{
    /// <summary>
    /// Alpaca telescope API client. A user name (and password) is sent as HTTP basic authentication,
    /// for servers that require it.
    /// </summary>
    public class AlpacaClient(string url, double clientTransactionId, string? userName = null, string? password = null)
    {
        private readonly string _baseUrl = $"{url}";
        private readonly AuthenticationHeaderValue? _authorization = string.IsNullOrEmpty(userName)
            ? null
            : new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userName}:{password}")));
        // One HttpClient for every request (a new one per poll leaks sockets). Its own timeout is off: each
        // request sets one (SendAsync), short so a server that stops answering is reported as disconnected in
        // seconds, but long for Connect, which a mount can take a while to finish (a serial port opening).
        private static readonly HttpClient SharedHttpClient = new() { Timeout = Timeout.InfiniteTimeSpan };
        private readonly HttpClient _httpClient = SharedHttpClient;

        /// <summary>How long a request may take before the server counts as not answering.</summary>
        public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

        /// <summary>How long a Connect may take (as for a COM driver, connecting can be slow).</summary>
        public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);
        private const int ClientId = 71 + 81 + 81; // = 233 or Decimal  G + S + S

        /// <summary>Alpaca's NotImplemented error (0x400).</summary>
        public const int NotImplementedError = 0x400;

        public double ClientTransactionId
        {
            get;
            set
            {
                field++;
                if (value > 4294960000)
                {
                    field = 0;
                }
            }
        } = clientTransactionId;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="method"></param>
        /// <returns></returns>
        private string BuildUrl(string method)
        {
            return $"{_baseUrl}{method}";
        }

        // ===== Gets =====

        /// <summary>
        /// 
        /// </summary>
        /// <param name="method"></param>
        /// <returns></returns>
        private async Task<AlpacaResponse?> GetAsync(string method, CancellationToken token = default)
        {
            // Only the method name is lower-cased: the server path may be case-sensitive.
            var url = BuildUrl(method.ToLowerInvariant());
            ClientTransactionId++;
            url += $"?ClientID={ClientId}&ClientTransactionID={ClientTransactionId}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            return await SendAsync(request, token);
        }

        /// <summary>
        /// Sends a request (with the credentials, if any) and reads the Alpaca response. An HTTP error
        /// (e.g. 401 when the credentials are wrong) throws with its status, rather than failing to parse
        /// an error page as JSON.
        /// </summary>
        private async Task<AlpacaResponse?> SendAsync(HttpRequestMessage request, CancellationToken token, TimeSpan? timeout = null)
        {
            if (_authorization is not null) request.Headers.Authorization = _authorization;
            var limit = timeout ?? RequestTimeout;
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutSource.CancelAfter(limit);
            try
            {
                using var response = await _httpClient.SendAsync(request, timeoutSource.Token);
                var body = await response.Content.ReadAsStringAsync(timeoutSource.Token);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException(DescribeHttpError(response, body), null, response.StatusCode);
                return JsonConvert.DeserializeObject<AlpacaResponse>(body);
            }
            catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
            {
                // Our time limit ran out, not the caller's token: report it as such.
                throw new TimeoutException($"the server did not answer within {limit.TotalSeconds:0} s", ex);
            }
        }

        /// <summary>"HTTP 401 Unauthorized", plus the server's short plain-text reason if it gave one.</summary>
        private static string DescribeHttpError(HttpResponseMessage response, string body)
        {
            var text = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
            var detail = body.Trim();
            return detail.Length is > 0 and <= 200 && !detail.StartsWith('<') ? $"{text}: {detail}" : text;
        }

       /// <summary>
        /// Asynchronously retrieves the current state of the device.
        /// </summary>
        /// <remarks>This method sends a request to obtain the device's state and maps the response to a
        /// <see cref="TelescopeDeviceState"/> instance. If the response is unavailable or invalid, the method returns <see
        /// langword="null"/>. Callers should check the result for <see langword="null"/> to handle cases where the
        /// device state could not be determined.</remarks>
        /// <returns>A task that represents the asynchronous operation. The task result contains a <see cref="TelescopeDeviceState"/>
        /// object representing the current state of the device, or <see langword="null"/> if the device state could not
        /// be retrieved.</returns>
        public async Task<TelescopeDeviceState?> GetDeviceStateAsync(CancellationToken token = default)
        {
            const string method = "devicestate";
            var alpacaResponse = await GetAsync(method, token);
            if (alpacaResponse == null)
            {
                //todo log null response
                return null;
            }
            alpacaResponse.MethodName = method;

            // An error (e.g. 0x407 NotConnected) usually comes without a Value: return just the response,
            // so the caller can show the server's own error rather than failing on the missing values.
            if (alpacaResponse.ErrorNumber != 0)
                return new TelescopeDeviceState { AlpacaResponse = alpacaResponse };
            if (alpacaResponse.Value is not JArray)
            {
                // Answered, but without the list of values: treat it as not implemented, so the caller reads
                // the properties one at a time instead (null would mean the server didn't answer at all).
                alpacaResponse.ErrorNumber = NotImplementedError;
                alpacaResponse.ErrorMessage = "devicestate returned no list of values";
                return new TelescopeDeviceState { AlpacaResponse = alpacaResponse };
            }

            var deviceState = TelescopeMappings.MapDeviceState(alpacaResponse);
            deviceState.AlpacaResponse = alpacaResponse;
            return deviceState;
        }

        /// <summary>
        /// Asynchronously retrieves a property from the Alpaca device using the specified method name.
        /// </summary>
        /// <remarks>If the <paramref name="method"/> parameter is null or empty, the method returns <see
        /// langword="null"/> without making a request. The returned <see cref="AlpacaResponse"/> will have its
        /// MethodName and MethodType properties set before being returned.</remarks>
        /// <param name="method">The name of the Alpaca API method to retrieve the property for. Cannot be null or empty.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains an <see cref="AlpacaResponse"/>
        /// with the property details if the method name is valid and a response is received; otherwise, <see
        /// langword="null"/>.</returns>
        public async Task<AlpacaResponse?> GetPropertyAsync(string method, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(method)){return null;}
            var alpacaResponse = await GetAsync(method, token);
            if (alpacaResponse == null)
            {
                //todo log null response
                return null;
            }
            alpacaResponse.MethodName = method;
            alpacaResponse.MethodType = "Get";
            //todo log any errors in response 
            return alpacaResponse;
        }


        /// <summary>
        /// The device state read one property at a time, for servers without devicestate (it came with
        /// ITelescopeV4 / ASCOM Platform 7). An error on a required property is returned as the state's
        /// AlpacaResponse, as <see cref="GetDeviceStateAsync"/> does; Azimuth, Altitude and SideOfPier are
        /// optional on an equatorial mount, so they are left at their defaults if not implemented.
        /// Returns null if the server gives no answer.
        /// </summary>
        public async Task<TelescopeDeviceState?> GetDeviceStateFromPropertiesAsync(CancellationToken token = default)
        {
            var state = new TelescopeDeviceState(); // SideOfPier stays null (unknown) unless the mount says
            AlpacaResponse? last = null;
            foreach (var (method, required) in new[]
                     {
                         ("rightascension", true), ("declination", true), ("siderealtime", true),
                         ("azimuth", false), ("altitude", false), ("sideofpier", false)
                     })
            {
                var response = await GetPropertyAsync(method, token);
                if (response is null) return null;
                if (response.ErrorNumber != 0 || response.Value is null || response.Value.Type == JTokenType.Null)
                {
                    if (required) return new TelescopeDeviceState { AlpacaResponse = response };
                    continue;
                }

                last = response;
                switch (method)
                {
                    case "rightascension": state.RightAscension = response.Value.ToObject<double>(); break;
                    case "declination": state.Declination = response.Value.ToObject<double>(); break;
                    case "siderealtime": state.SiderealTime = response.Value.ToObject<double>(); break;
                    case "azimuth": state.Azimuth = response.Value.ToObject<double>(); break;
                    case "altitude": state.Altitude = response.Value.ToObject<double>(); break;
                    case "sideofpier": state.SideOfPier = response.Value.ToObject<int>(); break;
                }
            }
            state.AlpacaResponse = last;
            return state;
        }

        // ===== Puts =====

        /// <summary>
        /// Sends an asynchronous HTTP PUT request to the specified API method with the provided parameters and returns
        /// the deserialized response.
        /// </summary>
        /// <remarks>This method automatically increments the client transaction ID for each request and
        /// includes it in the request body. The request body is sent using form URL encoding. Ensure that the specified
        /// method name and parameters are valid for the target API.</remarks>
        /// <param name="method">The name of the API method to which the PUT request is sent. This value cannot be null or empty.</param>
        /// <param name="parameters">An array of key-value pairs representing the parameters to include in the request body. Each pair specifies
        /// a parameter name and its corresponding value.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains an instance of <see
        /// cref="AlpacaResponse"/> with the response data, or <see langword="null"/> if the response cannot be
        /// deserialized.</returns>
        private async Task<AlpacaResponse?> PutAsync(string method, CancellationToken token, TimeSpan? timeout, params (string key, string value)[] parameters)
        {
            // Only the method name is lower-cased: the server path may be case-sensitive.
            var url = BuildUrl(method.ToLowerInvariant());
            ClientTransactionId++;

            // Build a mutable list of form entries (FormUrlEncodedContent doesn't support Add)
            var formEntries = new List<KeyValuePair<string, string>>
            {
                new("ClientID", ClientId.ToString(CultureInfo.InvariantCulture)),
                new("ClientTransactionID", ClientTransactionId.ToString(CultureInfo.InvariantCulture))
            };

            // Add custom parameters
            foreach (var (key, value) in parameters)
            {
                formEntries.Add(new KeyValuePair<string, string>(key, value));
            }

            using var request = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = new FormUrlEncodedContent(formEntries)
            };
            return await SendAsync(request, token, timeout);
        }

        /// <summary>
        /// Asynchronously establishes a connection to the telescope device.
        /// </summary>
        /// <remarks>This method sends a request to connect to the telescope using the Alpaca protocol. If
        /// the connection attempt is unsuccessful, the method returns <see langword="null"/>. Callers should check the
        /// returned value to determine whether the connection was established successfully.</remarks>
        /// <returns>An <see cref="AlpacaResponse"/> that contains the result of the connection attempt, or <see
        /// langword="null"/> if the connection fails.</returns>
        public async Task<AlpacaResponse?> ConnectAsync(CancellationToken token = default)
        {
            const string method = "connected";
            var alpacaResponse = await PutAsync(method, token, ConnectTimeout, ("Connected", "true"));
            if (alpacaResponse == null)
            {
                //todo log null response
                return null;
            }
            alpacaResponse.MethodName = method;
            alpacaResponse.MethodType = "Put, Connected, true";
            //todo log any errors in response 
            // Keep ErrorNumber / ErrorMessage: they say why the server would not connect.
            return alpacaResponse;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<bool> DisconnectAsync()
        {
            var response = await PutAsync("connected", CancellationToken.None, null, ("Connected", "false"));
            return response is { ErrorNumber: 0 };
        }
    }
}
