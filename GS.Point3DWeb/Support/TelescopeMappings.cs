using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GS.Alpaca.Support
{
    public class TelescopeMappings
    {
        /// <summary>
        /// Maps a devicestate answer (a list of Name / Value pairs) to a <see cref="TelescopeDeviceState"/>.
        /// A position value that can't be read fails the whole state (the model would be drawn in the wrong
        /// place); any other value that can't be read is left out, so one odd value doesn't stop the updates.
        /// </summary>
        public static TelescopeDeviceState MapDeviceState(AlpacaResponse localResponseValue)
        {
            var deviceState = new TelescopeDeviceState();

            foreach (var token in localResponseValue.Value)
            {
                if (token is not JObject obj) continue;

                // Skip entries without a name or value rather than failing the whole state.
                var propertyName = obj["Name"]?.Value<string>();
                if (propertyName is null || obj["Value"] is not { } value || value.Type == JTokenType.Null) continue;

                switch (propertyName)
                {
                    case "Altitude":
                        deviceState.Altitude = value.ToObject<double>();
                        break;
                    case "Azimuth":
                        deviceState.Azimuth = value.ToObject<double>();
                        break;
                    case "Declination":
                        deviceState.Declination = value.ToObject<double>();
                        break;
                    case "RightAscension":
                        deviceState.RightAscension = value.ToObject<double>();
                        break;
                    case "SiderealTime":
                        deviceState.SiderealTime = value.ToObject<double>();
                        break;
                    case "AtHome":
                        deviceState.AtHome = Optional<bool>(value);
                        break;
                    case "AtPark":
                        deviceState.AtPark = Optional<bool>(value);
                        break;
                    case "IsPulseGuiding":
                        deviceState.IsPulseGuiding = Optional<bool>(value);
                        break;
                    case "SideOfPier":
                        deviceState.SideOfPier = Optional<int>(value);
                        break;
                    case "Slewing":
                        deviceState.Slewing = Optional<bool>(value);
                        break;
                    case "Tracking":
                        deviceState.Tracking = Optional<bool>(value);
                        break;
                    case "UTCDate":
                        deviceState.UTCDate = Optional<DateTime>(value);
                        break;
                    case "TimeStamp":
                        deviceState.TimeStamp = Optional<DateTime>(value);
                        break;
                }
            }
            return deviceState;
        }

        /// <summary>The value, or null if it isn't of the expected type (e.g. a date in a format JSON doesn't use).</summary>
        private static T? Optional<T>(JToken value) where T : struct
        {
            try
            {
                return value.ToObject<T>();
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or ArgumentException
                                           or OverflowException or JsonException)
            {
                return null;
            }
        }
    }
}
