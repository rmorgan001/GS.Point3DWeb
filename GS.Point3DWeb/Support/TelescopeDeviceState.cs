
namespace GS.Alpaca.Support
{
    /// <summary>
    /// A telescope's state from one poll. The position (RightAscension, Declination, SiderealTime) is always
    /// read; the rest is null when the mount didn't report it (a server read one property at a time reads
    /// only the position and, if it has one, the side of pier).
    /// </summary>
    public class TelescopeDeviceState
    {
        public AlpacaResponse? AlpacaResponse { get; set; }
        public double Altitude { get; set; }
        public bool? AtHome { get; set; }
        public bool? AtPark { get; set; }
        public double Azimuth { get; set; }
        public double Declination { get; set; }
        public bool? IsPulseGuiding { get; set; }
        public double RightAscension { get; set; }
        /// <summary>ASCOM PierSide: 0 east, 1 west, -1 unknown; null if not reported.</summary>
        public int? SideOfPier { get; set; }
        public double SiderealTime { get; set; }
        public bool? Slewing { get; set; }
        public bool? Tracking { get; set; }
        // ReSharper disable once InconsistentNaming
        public DateTime? UTCDate { get; set; }
        public DateTime? TimeStamp { get; set; }
    }
}
