/* Copyright(C) 2021  Rob Morgan (robert.morgan.e@gmail.com)

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

namespace GS.Alpaca.Shared
{
    public static class Axes
    {
        /// <summary>
        /// convert a RaDec position to an axes positions. 
        /// </summary>
        /// <param name="mode"></param>
        /// <param name="raDec"></param>
        /// <param name="sideRealtime"></param>
        /// <param name="southernHemisphere"></param>
        /// <param name="pierSide"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <returns></returns>
        public static double[] RaDecToAxesXy(AlignmentModes mode, double[] raDec, double sideRealtime, bool southernHemisphere,int pierSide)
        {
            var axes = new[] {raDec[0], raDec[1]};
            switch (mode)
            {
                case AlignmentModes.algAltAz:
                    //axes = Range.RangeAzAlt(axes);
                    //axes = Coordinate.RaDec2AltAz(axes[0], axes[1], SkyServer.SiderealTime, SkySettings.Latitude);
                    return axes;
                case AlignmentModes.algGermanPolar:
                    axes[0] = (sideRealtime - axes[0]) * 15.0;
                    if (southernHemisphere){axes[1] = -axes[1];}

                    //Axis2 = axes[0];
                    //Axis3 = axes[1];

                    var axes3 = GetAltAxisPosition(axes);

                    //Axis4 = axes3[0];
                    //Axis5 = axes3[1];

                    switch (pierSide)
                    {
                        case -1: //SOP.pierUnknown:
                            break;
                        case 0: //SOP.pierEast:
                            if (southernHemisphere)
                            {
                                // southern
                                axes[0] = axes[0];
                                axes[1] = axes[1];
                            }
                            else
                            {
                                axes[0] = axes[0];
                                axes[1] = axes[1];
                            }

                            break;
                        case 1: //SOP.pierWest:
                            if (southernHemisphere)
                            {
                                // southern
                                axes[0] = axes3[0];
                                axes[1] = axes3[1];
                            }
                            else
                            {
                                axes[0] = axes3[0];
                                axes[1] = axes3[1];
                            }

                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    return axes;
                case AlignmentModes.algPolar:
                    //axes[0] = (SkyServer.SiderealTime - axes[0]) * 15.0;
                    //axes[1] = (SkyServer.SouthernHemisphere) ? -axes[1] : axes[1];
                    break;
                case AlignmentModes.algUnknown:
                    throw new ArgumentOutOfRangeException($"RaDecToAxesXy");
                default:
                    throw new ArgumentOutOfRangeException($"RaDecToAxesXy");
            }

            axes = Range.RangeAxesXy(axes);
            return axes;
        }

        /// <summary>
        /// GEMs have two possible axes positions, given an axis position this returns the other 
        /// </summary>
        /// <param name="alt">position</param>
        /// <returns>other axis position</returns>
        private static double[] GetAltAxisPosition(double[] alt)
        {
            var d = new[] {0.0, 0.0};
            if (alt[0] > 90)
            {
                d[0] = alt[0] - 180;
                d[1] = 180 - alt[1];
            }
            else
            {
                d[0] = alt[0] + 180;
                d[1] = 180 - alt[1];
            }

            return d;
        }
    }
}
