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
