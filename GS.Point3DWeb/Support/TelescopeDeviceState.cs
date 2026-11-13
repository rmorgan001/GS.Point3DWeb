
namespace GS.Alpaca.Support
{
    /// <summary>
    /// 
    /// </summary>
    public class TelescopeDeviceState
    {
        public AlpacaResponse? AlpacaResponse { get; set; }
        public double Altitude { get; set; }
        public bool AtHome { get; set; }
        public bool AtPark { get; set; }
        public double Azimuth { get; set; }
        public double Declination { get; set; }
        public bool IsPulseGuiding { get; set; }
        public double RightAscension { get; set; }
        public int SideOfPier { get; set; }
        public double SiderealTime { get; set; }
        public bool Slewing { get; set; }
        public bool Tracking { get; set; }
        // ReSharper disable once InconsistentNaming
        public DateTime UTCDate { get; set; }
        public DateTime TimeStamp { get; set; }

        /// <summary>
        /// The property names the mount reported in one devicestate / TelescopeState answer. Empty when the
        /// state was read one property at a time, which reads the position only.
        /// </summary>
        public HashSet<string> Reported { get; } = new(StringComparer.Ordinal);
    }
}
