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

                    var properties = obj.Properties().ToDictionary(p => p.Name, p => p.Value);
                    var propertyName = properties["Name"].Value<string>();
                    var propertyValue = properties["Value"];

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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="client"></param>
        /// <returns></returns>
        public static async Task<TelescopeDevice> MapTelescopeDevice(AlpacaClient? client)
        {
            var device = new TelescopeDevice();
            if (client == null) return device;

            var localResponse = await client.GetPropertyAsync("AlignmentMode");
            if (localResponse != null) device.AlignmentMode = localResponse.Value.ToObject<int>();
            localResponse = await client.GetPropertyAsync("Altitude");
            if (localResponse != null) device.Altitude = localResponse.Value.ToObject<double>();
            localResponse = await client.GetPropertyAsync("ApertureArea");
            if (localResponse != null) device.ApertureArea = localResponse.Value.ToObject<double>();
            localResponse = await client.GetPropertyAsync("ApertureDiameter");
            if (localResponse != null) device.ApertureDiameter = localResponse.Value.ToObject<double>();
            localResponse = await client.GetPropertyAsync("AtHome");
            if (localResponse != null) device.AtHome = localResponse.Value.ToObject<bool>();
            localResponse = await client.GetPropertyAsync("AtPark");
            if (localResponse != null) device.AtPark = localResponse.Value.ToObject<bool>();
            localResponse = await client.GetPropertyAsync("Azimuth");
            if (localResponse != null) device.Azimuth = localResponse.Value.ToObject<double>();
            localResponse = await client.GetPropertyAsync("CanFindHome");
            if (localResponse != null) device.CanFindHome = localResponse.Value.ToObject<bool>();
            localResponse = await client.GetPropertyAsync("CanPark");
            if (localResponse != null) device.CanPark = localResponse.Value.ToObject<bool>();
            localResponse = await client.GetPropertyAsync("CanPulseGuide");
            if (localResponse != null) device.CanPulseGuide = localResponse.Value.ToObject<bool>();
            localResponse = await client.GetPropertyAsync("CanSetDeclinationRate");
            if (localResponse != null) device.CanSetDeclinationRate = localResponse.Value.ToObject<bool>();
            localResponse = await client.GetPropertyAsync("CanSetGuideRates");
            if (localResponse != null) device.CanSetGuideRates = localResponse.Value.ToObject<bool>();
            localResponse = await client.GetPropertyAsync("CanSetPark");
            if (localResponse != null) device.CanSetPark = localResponse.Value.ToObject<bool>();
            localResponse = await client.GetPropertyAsync("CanSetPierSide");
            if (localResponse != null) device.CanSetPierSide = localResponse.Value.ToObject<bool>();
            localResponse = await client.GetPropertyAsync("CanSetRightAscensionRate");
            if (localResponse != null) device.CanSetRightAscensionRate = localResponse.Value.ToObject<bool>();
            localResponse = await client.GetPropertyAsync("CanSetTracking");
            if (localResponse != null) device.CanSetTracking = localResponse.Value.ToObject<bool>();
            localResponse = await client.GetPropertyAsync("CanSlew");
            if (localResponse != null) device.CanSlew = localResponse.Value.ToObject<bool>();
            localResponse = await client.GetPropertyAsync("CanSlewAltAz");
            if (localResponse != null) device.CanSlewAltAz = localResponse.Value.ToObject<bool>();
            localResponse = await client.GetPropertyAsync("CanSlewAltAzAsync");
            if (localResponse != null) device.CanSlewAltAzAsync = localResponse.Value.ToObject<bool>();
            localResponse = await client.GetPropertyAsync("CanSlewAsync");
            if (localResponse != null) device.CanSlewAsync = localResponse.Value.ToObject<bool>();
            localResponse = await client.GetPropertyAsync("CanSync");
            if (localResponse != null) device.CanSync = localResponse.Value.ToObject<bool>();
            localResponse = await client.GetPropertyAsync("CanSyncAltAz");
            if (localResponse != null) device.CanSyncAltAz = localResponse.Value.ToObject<bool>();
            localResponse = await client.GetPropertyAsync("CanUnpark");
            if (localResponse != null) device.CanUnpark = localResponse.Value.ToObject<bool>();
            localResponse = await client.GetPropertyAsync("Connected");
            if (localResponse != null) device.Connected = localResponse.Value.ToObject<bool>();
            localResponse = await client.GetPropertyAsync("Connecting");
            if (localResponse != null) device.Connecting = localResponse.Value.ToObject<bool>();
            localResponse = await client.GetPropertyAsync("Declination");
            if (localResponse != null) device.Declination = localResponse.Value.ToObject<double>();
            localResponse = await client.GetPropertyAsync("DeclinationRate");
            if (localResponse != null) device.DeclinationRate = localResponse.Value.ToObject<double>();
            localResponse = await client.GetPropertyAsync("Description");
            if (localResponse != null) device.Description = localResponse.Value.ToObject<string>();
            localResponse = await client.GetPropertyAsync("DoesRefraction");
            if (localResponse != null) device.DoesRefraction = localResponse.Value.ToObject<bool>();
            localResponse = await client.GetPropertyAsync("DriverInfo");
            if (localResponse != null) device.DriverInfo = localResponse.Value.ToObject<string>();
            localResponse = await client.GetPropertyAsync("DriverVersion");
            if (localResponse != null) device.DriverVersion = localResponse.Value.ToObject<string>();
            localResponse = await client.GetPropertyAsync("EquatorialSystem");
            if (localResponse != null) device.EquatorialSystem = localResponse.Value.ToObject<int>();
            localResponse = await client.GetPropertyAsync("FocalLength");
            if (localResponse != null) device.FocalLength = localResponse.Value.ToObject<double>();
            localResponse = await client.GetPropertyAsync("GuideRateDeclination");
            if (localResponse != null) device.GuideRateDeclination = localResponse.Value.ToObject<double>();
            localResponse = await client.GetPropertyAsync("GuideRateRightAscension");
            if (localResponse != null) device.GuideRateRightAscension = localResponse.Value.ToObject<double>();
            localResponse = await client.GetPropertyAsync("InterfaceVersion");
            if (localResponse != null) device.InterfaceVersion = localResponse.Value.ToObject<int>();
            localResponse = await client.GetPropertyAsync("IsPulseGuiding");
            if (localResponse != null) device.IsPulseGuiding = localResponse.Value.ToObject<bool>();
            localResponse = await client.GetPropertyAsync("Name");
            if (localResponse != null) device.Name = localResponse.Value.ToObject<string>();
            localResponse = await client.GetPropertyAsync("RightAscensionRate");
            if (localResponse != null) device.RightAscensionRate = localResponse.Value.ToObject<double>();
            localResponse = await client.GetPropertyAsync("SideOfPier");
            if (localResponse != null) device.SideOfPier = localResponse.Value.ToObject<int>();
            localResponse = await client.GetPropertyAsync("SiderealTime");
            if (localResponse != null) device.SiderealTime = localResponse.Value.ToObject<double>();
            localResponse = await client.GetPropertyAsync("SiteElevation");
            if (localResponse != null) device.SiteElevation = localResponse.Value.ToObject<double>();
            localResponse = await client.GetPropertyAsync("SiteLatitude");
            if (localResponse != null) device.SiteLatitude = localResponse.Value.ToObject<double>();
            localResponse = await client.GetPropertyAsync("SiteLongitude");
            if (localResponse != null) device.SiteLongitude = localResponse.Value.ToObject<double>();
            localResponse = await client.GetPropertyAsync("SlewSettleTime");
            if (localResponse != null) device.SlewSettleTime = localResponse.Value.ToObject<int>();
            localResponse = await client.GetPropertyAsync("Slewing");
            if (localResponse != null) device.Slewing = localResponse.Value.ToObject<bool>();
            localResponse = await client.GetPropertyAsync("SupportedActions");
            var a = localResponse?.Value.ToObject<string[]>();
            if (a != null)
            {
                var b = string.Join(", ", a);
                device.SupportedActions = b;
            }
            localResponse = await client.GetPropertyAsync("TargetDeclination");
            if (localResponse != null) device.TargetDeclination = localResponse.Value.ToObject<double>();
            localResponse = await client.GetPropertyAsync("TargetRightAscension");
            if (localResponse != null) device.TargetRightAscension = localResponse.Value.ToObject<double>();
            //localResponse = await client.GetPropertyAsync("DriveRates");
            //if (localResponse != null) device.DriveRates = localResponse.Value.ToObject<int>();
            localResponse = await client.GetPropertyAsync("Tracking");
            if (localResponse != null) device.Tracking = localResponse.Value.ToObject<bool>();
            localResponse = await client.GetPropertyAsync("TrackingRate");
            if (localResponse != null) device.TrackingRate = localResponse.Value.ToObject<int>();
            localResponse = await client.GetPropertyAsync("TrackingRates");
            a = localResponse?.Value.ToObject<string[]>();
            if (a == null) return device;
            {
                var b = string.Join(", ", a);
                device.TrackingRates = b;
            }
            localResponse = await client.GetPropertyAsync("UTCDate");
            if (localResponse != null) device.UTCDate = localResponse.Value.ToObject<DateTime>();
            return device;
        }
    }
}
