using Newtonsoft.Json;
using System.Globalization;

// ReSharper disable StringLiteralTypo

namespace GS.Alpaca.Support
{
    public class AlpacaClient(string url, double clientTransactionId)
    {
        private readonly string _baseUrl = $"{url}";
        private readonly HttpClient _httpClient = new();
        private const int ClientId = 71 + 81 + 81; // = 233 or Decimal  G + S + S

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
        private async Task<AlpacaResponse?> GetAsync(string method)
        {
            var url = BuildUrl(method).ToLower();
            ClientTransactionId++;
            url += $"?ClientID={ClientId}&ClientTransactionID={ClientTransactionId}";
            var httpResponse = await _httpClient.GetAsync(url);
            var jsonResponse = await httpResponse.Content.ReadAsStringAsync();
            var alpacaResponse = JsonConvert.DeserializeObject<AlpacaResponse>(jsonResponse);
            //todo log any return errors
            return alpacaResponse;
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
        public async Task<TelescopeDeviceState?> GetDeviceStateAsync()
        {
            const string method = "devicestate";
            var alpacaResponse = await GetAsync(method);
            if (alpacaResponse == null)
            {
                //todo log null response
                return null;
            }
            var deviceState = TelescopeMappings.MapDeviceState(alpacaResponse);
            alpacaResponse.MethodName = method;
            deviceState.AlpacaResponse = alpacaResponse;
            //todo log any errors in response
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
        public async Task<AlpacaResponse?> GetPropertyAsync(string method)
        {
            if (string.IsNullOrEmpty(method)){return null;}
            var alpacaResponse = await GetAsync(method);
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
        private async Task<AlpacaResponse?> PutAsync(string method, params (string key, string value)[] parameters)
        {
            var url = BuildUrl(method).ToLower();
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

            using var content = new FormUrlEncodedContent(formEntries);

            var response = await _httpClient.PutAsync(url, content);
            var jsonResponse = await response.Content.ReadAsStringAsync();
            
            return JsonConvert.DeserializeObject<AlpacaResponse>(jsonResponse);
        }

        /// <summary>
        /// Asynchronously establishes a connection to the telescope device.
        /// </summary>
        /// <remarks>This method sends a request to connect to the telescope using the Alpaca protocol. If
        /// the connection attempt is unsuccessful, the method returns <see langword="null"/>. Callers should check the
        /// returned value to determine whether the connection was established successfully.</remarks>
        /// <returns>An <see cref="AlpacaResponse"/> that contains the result of the connection attempt, or <see
        /// langword="null"/> if the connection fails.</returns>
        public async Task<AlpacaResponse?> ConnectAsync()
        {
            const string method = "connected";
            var alpacaResponse = await PutAsync(method, ("Connected", "true"));
            if (alpacaResponse == null)
            {
                //todo log null response
                return null;
            }
            alpacaResponse.MethodName = method;
            alpacaResponse.MethodType = "Put, Connected, true";
            //todo log any errors in response 
            alpacaResponse.ErrorNumber = 0;
            return alpacaResponse;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<bool> DisconnectAsync()
        {
            var response = await PutAsync("connected", ("Connected", "false"));
            return response is { ErrorNumber: 0 };
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="rightAscension"></param>
        /// <param name="declination"></param>
        /// <returns></returns>
        public async Task<bool> SlewToAsync(double rightAscension, double declination)
        {
            var response = await PutAsync("slewtocoordinates", 
                ("RightAscension", rightAscension.ToString(CultureInfo.InvariantCulture)),
                ("Declination", declination.ToString(CultureInfo.InvariantCulture)));
            return response is { ErrorNumber: 0 };
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public async Task<bool> AbortSlewAsync()
        {
            var response = await PutAsync("abortslew");
            return response is { ErrorNumber: 0 };
        }

    }
}
