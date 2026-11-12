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

using System.ComponentModel;

namespace GS.Alpaca.Shared
{
    #region Enums
    public enum Model3DType
    {
        [Description("Default")]
        Default = 0,
        [Description("Reflector")]
        Reflector = 1,
        [Description("Refractor")]
        Refractor = 2,
        [Description("SchmidtCassegrain")]
        SchmidtCassegrain = 3,
        [Description("RitcheyChretien")]
        RitcheyChretien = 4,
        [Description("RitcheyChretienTruss")]
        RitcheyChretienTruss = 5,
        [Description("DualTelescope")]
        DualTelescope = 6
    }

    public enum PierSide
    {
        [Description("pierUnknown")]
        pierUnknown = -1,
        [Description("pierEast")]
        pierEast = 0,
        [Description("pierWest")]
        pierWest = 1
    }

    public enum AlignmentModes
    {
        [Description("algUnknown")]
        algUnknown =-1,
        [Description("algAltAz")]
        algAltAz = 0,
        [Description("algPolar")]
        algPolar = 1,
        [Description("algGermanPolar")]
        algGermanPolar = 2
    }

    public enum DriveRates
    {
        [Description("driverUnknown")]
        driverUnknown =-1,
        [Description("driveSidereal")]
        driveSidereal = 0,
        [Description("driveLunar")]
        driveLunar = 1,
        [Description("driveSolar")]
        driveSolar = 2,
        [Description("driveKing")]
        driveKing = 3
    }

    public enum EquatorialCoordinateType
    {
        [Description("equUnknown")]
        equUnknown =-1,
        [Description("equOther")]
        equOther = 0,
        [Description("equTopocentric")]
        equTopocentric = 1,
        [Description("equJ2000")]
        equJ2000 = 2,
        [Description("equJ2050")]
        equJ2050 = 3,
        [Description("equB1950")]
        equB1950 = 4
    }

    public enum GuideDirections
    {
        [Description("guideUnknown")]
        guideUnknown =-1,
        [Description("guideNorth")]
        guideNorth = 0,
        [Description("guideSouth")]
        guideSouth = 1,
        [Description("guideEast")]
        guideEast = 2,
        [Description("guideWest")]
        guideWest = 3
    }

    public enum TelescopeAxes
    {
        [Description("axisUnknown")]
        axisUnknown =-1,
        [Description("axisPrimary")]
        axisPrimary = 0,
        [Description("axisSecondary")]
        axisSecondary = 1,
        [Description("axisTertiary")]
        axisTertiary = 2
    }
    #endregion
}
