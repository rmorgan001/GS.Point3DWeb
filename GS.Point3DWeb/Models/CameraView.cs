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
