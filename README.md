# GS Point3D Web

[![Build](https://github.com/rmorgan001/GS.Point3DWeb/actions/workflows/build.yml/badge.svg)](https://github.com/rmorgan001/GS.Point3DWeb/actions/workflows/build.yml)

A browser-based 3D viewer for telescope mounts. GS Point3D Web connects to ASCOM Alpaca
servers on the network, or to ASCOM COM drivers on the computer it runs on. It then shows each
mount as a live 3D model that moves as the mount does, so you can see where the telescope is
pointing from any browser.

![GS Point3D Web showing a German equatorial mount connected to the ASCOM Alpaca Simulators](docs/screenshot.png)

## Features

- **Live 3D model:** follows the mount's axes, using the same model transform as GSServer.
  Supports German equatorial, fork (polar) and Alt-Az mounts in either hemisphere.
- **Telescope models:** reflector, refractor, Schmidt-Cassegrain, Ritchey-Chrétien (tube or
  truss), dual telescope and a default model, in any colour, with optional sky backgrounds and
  a ground grid.
- **Up to four views at once:** two views and five servers without a feature key; four views
  and any number of servers with one. Each view can show a different mount.
- **Coordinates over the model:** RA, Dec, Az, Alt, LHA and LST, each one optional. RA can be
  shown in hours or degrees.
- **Mount state:** for servers that support `devicestate` (ASCOM Platform 7 / ITelescopeV4),
  a strip shows tracking, slewing, parked, at home, guiding, side of pier and the mount's UTC
  time. Older servers are read one property at a time.
- **Two ways to connect:**
  - Alpaca servers, found automatically with Alpaca discovery (UDP port 32227) or added by
    address.
  - ASCOM COM drivers, chosen from the drivers installed on the computer running the app
    (Windows only).
- **Saved camera views:** per mount, plus each mount's own polling interval (0.5–5 s).
- **Languages:** English, Deutsch, Español, Français, Italiano and 中文.
- **Works offline:** fonts and the 3D engine are bundled, so no internet connection is needed.
- **Logging:** a daily log file and a built-in log viewer, opened with **View Log** in the top
  bar.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) to build, or the
  ASP.NET Core 10 runtime to run a published copy.
- Windows, for ASCOM COM drivers (with the
  [ASCOM Platform](https://ascom-standards.org/) installed). Alpaca servers work on any
  operating system .NET supports.
- A browser with WebGL.

## Getting started

```shell
git clone https://github.com/rmorgan001/GS.Point3DWeb.git
cd GS.Point3DWeb
dotnet run --project GS.Point3DWeb
```

Then open <http://localhost:5181>.

On its first run the app adds a connection to the
[ASCOM Alpaca Simulators](https://github.com/ASCOMInitiative/ASCOM.Alpaca.Simulators) at
`http://127.0.0.1:32323`. To view a mount:

1. Add it with **+** (by address), the discovery button (Alpaca servers on the network) or the
   ASCOM drivers button (COM drivers).
2. Drag it onto a view. Dragging connects it.
3. Use the cog on a view to choose the model, colours, background, coordinates and camera.

### Building a release

```shell
dotnet publish GS.Point3DWeb/GS.Point3DWeb.csproj --configuration Release --output publish
```

Every push to `main` is also built by [GitHub Actions](.github/workflows/build.yml). Each run
keeps the published app as a downloadable artifact for 14 days.

## Where things are stored

| What | Windows | Linux / macOS |
| --- | --- | --- |
| Server list and view settings (`servers.json`) | `%APPDATA%\GS.Point3DWeb` | `~/.config/GS.Point3DWeb` |
| Feature key (`feature.key`) | `%APPDATA%\GS.Point3DWeb` | `~/.config/GS.Point3DWeb` |
| Log files (`point3dweb-yyyyMMdd.log`, the last 14 files kept, up to 20 MB each) | `Documents\GS.Point3DWeb` | `~/.config/GS.Point3DWeb/logs` |

If the Documents folder can't be written (for example because of Windows Security's
Controlled folder access), the logs go to `%APPDATA%\GS.Point3DWeb\logs` instead. Warnings and
errors also go to the Windows Event Log.

## Repository layout

| Path | Contents |
| --- | --- |
| `GS.Point3DWeb/` | The web app (Blazor Server, MVVM with CommunityToolkit.Mvvm). |
| `GS.Point3DWeb/wwwroot/model3d/` | The Babylon.js viewer, the trimmed Babylon bundle and the `.obj` models. |
| `tools/babylon-bundle/` | Rebuilds the trimmed Babylon.js bundle. Only needed when changing it (see its README). |
| `tools/GS.Point3DWeb.KeyGen/` | Issues feature keys. Its private signing key is kept outside the repository and must never be shipped with the app. |

## Credits

- The telescope models and the mount transform are ported from Green Swamp's GSServer.
- Built with [ASCOM Alpaca and COM components](https://github.com/ASCOMInitiative),
  [Babylon.js](https://www.babylonjs.com/) and [Serilog](https://serilog.net/).
- The Barlow fonts are used under the [SIL Open Font License](GS.Point3DWeb/wwwroot/fonts/OFL.txt).
