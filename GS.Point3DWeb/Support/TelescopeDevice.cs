namespace GS.Alpaca.Support
{
    /// <summary>
    /// 
    /// </summary>
    public class TelescopeDevice
    { public int AlignmentMode { get; set; }
        public double Altitude { get; set; }
        public double ApertureArea { get; set; }
        public double ApertureDiameter { get; set; }
        public bool AtHome { get; set; }
        public bool AtPark { get; set; }
        public double Azimuth { get; set; }
        public bool CanFindHome { get; set; }
        public bool CanPark { get; set; }
        public bool CanPulseGuide { get; set; }
        public bool CanSetDeclinationRate { get; set; }
        public bool CanSetGuideRates { get; set; }
        public bool CanSetPark { get; set; }
        public bool CanSetPierSide { get; set; }
        public bool CanSetRightAscensionRate { get; set; }
        public bool CanSetTracking { get; set; }
        public bool CanSlew { get; set; }
        public bool CanSlewAltAz { get; set; }
        public bool CanSlewAltAzAsync { get; set; }
        public bool CanSlewAsync { get; set; }
        public bool CanSync { get; set; }
        public bool CanSyncAltAz { get; set; }
        public bool CanUnpark { get; set; }
        public bool Connected { get; set; }
        public bool Connecting { get; set; }
        public double Declination { get; set; } = 0;
        public double DeclinationRate { get; set; } = 0;
        public string? Description { get; set; } = string.Empty;
        public TelescopeDeviceState? DeviceState { get; set; }
        public bool DoesRefraction { get; set; }

        public string? DriverInfo { get; set; } = string.Empty;

        public string? DriverVersion = string.Empty;
        public int EquatorialSystem { get; set; }
        public double FocalLength { get; set; } = 0;
        public double GuideRateDeclination { get; set; } = 0;
        public double GuideRateRightAscension { get; set; } = 0;
        public int InterfaceVersion { get; set; } = 0;
        public bool IsPulseGuiding { get; set; }

        public string? Name { get; set; } = string.Empty;
        public double RightAscensionRate { get; set; } = 0;
        public int SideOfPier { get; set; }
        public double SiderealTime { get; set; } = 0;
        public double SiteElevation { get; set; } = 0;
        public double SiteLatitude { get; set; } = 0;
        public double SiteLongitude { get; set; } = 0;
        public int SlewSettleTime { get; set; } = 0;
        public bool Slewing { get; set; }

        public string? SupportedActions { get; set; } = string.Empty;
        public double TargetDeclination { get; set; } = 0;
        public double TargetRightAscension { get; set; } = 0;

        // public int DriveRates { get; set; } = 0;
        public bool Tracking { get; set; }
        public int TrackingRate { get; set; } = 0;
        public string TrackingRates { get; set; } = string.Empty;
        public DateTime UTCDate { get; set; }

       // public ConnectionInfo? ConnectionInfo { get; set; }
    }
}
