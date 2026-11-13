using Newtonsoft.Json.Linq;

namespace GS.Alpaca.Support
{
    public class TelescopeMappings
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="localResponseValue"></param>
        /// <returns></returns>
        public static TelescopeDeviceState MapDeviceState(AlpacaResponse localResponseValue)
        {
            var deviceState = new TelescopeDeviceState();

            foreach (var token in localResponseValue.Value)
            {
                if (token is not JObject obj) continue;

                    // Skip entries without a name or value rather than failing the whole state.
                    var propertyName = obj["Name"]?.Value<string>();
                    if (propertyName is null || obj["Value"] is not { } propertyValue || propertyValue.Type == JTokenType.Null) continue;
                    deviceState.Reported.Add(propertyName);

                    // Map the property to the DeviceState class
                    switch (propertyName)
                    {
                        case "Altitude":
                            deviceState.Altitude = propertyValue.ToObject<double>();
                            break;
                        case "AtHome":
                            deviceState.AtHome = propertyValue.ToObject<bool>();
                            break;
                        case "AtPark":
                            deviceState.AtPark = propertyValue.ToObject<bool>();
                            break;
                        case "Azimuth":
                            deviceState.Azimuth = propertyValue.ToObject<double>();
                            break;
                        case "Declination":
                            deviceState.Declination = propertyValue.ToObject<double>();
                            break;
                        case "IsPulseGuiding":
                            deviceState.IsPulseGuiding = propertyValue.ToObject<bool>();
                            break;
                        case "RightAscension":
                            deviceState.RightAscension = propertyValue.ToObject<double>();
                            break;
                        case "SideOfPier":
                            deviceState.SideOfPier = propertyValue.ToObject<int>();
                            break;
                        case "SiderealTime":
                            deviceState.SiderealTime = propertyValue.ToObject<double>();
                            break;
                        case "Slewing":
                            deviceState.Slewing = propertyValue.ToObject<bool>();
                            break;
                        case "Tracking":
                            deviceState.Tracking = propertyValue.ToObject<bool>();
                            break;
                        case "UTCDate":
                            deviceState.UTCDate = propertyValue.ToObject<DateTime>();
                            break;
                        case "TimeStamp":
                            deviceState.TimeStamp = propertyValue.ToObject<DateTime>();
                            break;
                    }
            }
            return deviceState;
        }
    }
}
