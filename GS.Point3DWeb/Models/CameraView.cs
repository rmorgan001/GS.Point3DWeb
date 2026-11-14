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

namespace GS.Point3DWeb.Models;

/// <summary>
/// A saved 3D camera position for a server's model view (Babylon ArcRotateCamera).
/// Alpha is the rotation around the model and Beta the tilt from straight down, both in degrees;
/// Radius is the zoom distance; Target is the point the camera orbits (moved by panning).
/// </summary>
public sealed record CameraView(
    double Alpha,
    double Beta,
    double Radius,
    double TargetX,
    double TargetY,
    double TargetZ);
