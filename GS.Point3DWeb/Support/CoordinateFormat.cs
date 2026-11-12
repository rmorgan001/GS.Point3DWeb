using System.Globalization;

namespace GS.Point3DWeb.Support;

/// <summary>
/// Sexagesimal formatting, matching ASCOM.Tools.Utilities.HoursToHMS / DegreesToDMS
/// as used by the desktop client.
/// </summary>
public static class CoordinateFormat
{
    public static string HoursToHms(double hours, string hrsDelim = ":", string minDelim = ":", string secDelim = "", int secDecimalDigits = 0)
        => Format(hours, 2, hrsDelim, minDelim, secDelim, secDecimalDigits);

    public static string DegreesToDms(double degrees, string degDelim = "° ", string minDelim = "' ", string secDelim = "\"", int secDecimalDigits = 0)
        => Format(degrees, 2, degDelim, minDelim, secDelim, secDecimalDigits);

    private static string Format(double value, int unitDigits, string unitDelim, string minDelim, string secDelim, int secDecimalDigits)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return "--";

        var sign = value < 0 ? "-" : "";
        value = Math.Abs(value);

        // Round to the displayed precision first so 59.999 doesn't print as 60.
        var scale = Math.Pow(10, secDecimalDigits);
        var totalSeconds = Math.Round(value * 3600.0 * scale) / scale;

        var units = (long)(totalSeconds / 3600);
        totalSeconds -= units * 3600;
        var minutes = (long)(totalSeconds / 60);
        var seconds = totalSeconds - minutes * 60;

        var secFormat = secDecimalDigits > 0 ? "00." + new string('0', secDecimalDigits) : "00";
        var unitFormat = new string('0', unitDigits);

        return string.Concat(
            sign,
            units.ToString(unitFormat, CultureInfo.InvariantCulture), unitDelim,
            minutes.ToString("00", CultureInfo.InvariantCulture), minDelim,
            seconds.ToString(secFormat, CultureInfo.InvariantCulture), secDelim);
    }
}
