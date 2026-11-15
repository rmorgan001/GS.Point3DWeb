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

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using GS.Point3DWeb.Support;

namespace GS.Point3DWeb.Services;

/// <summary>
/// Keeps a browser window above all other windows ("Stay on top" in the header). A web page can't do this
/// itself, so the app does it with Win32: the page briefly gives its tab a unique title, and the browser
/// window showing that title is made topmost (or not). That only works when the browser runs on the same
/// Windows computer and desktop as this app; otherwise no window has the title and nothing changes.
/// </summary>
public static class WindowPin
{
    private static ILogger Log => AppLog.For(nameof(WindowPin));

    /// <summary>How long to wait for the browser to show the new title in its window caption.</summary>
    private static readonly TimeSpan FindTimeout = TimeSpan.FromSeconds(2);

    [SupportedOSPlatformGuard("windows")]
    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>
    /// Makes the window whose caption contains <paramref name="titleToken"/> topmost, or no longer topmost.
    /// </summary>
    /// <returns>True if the window was found and changed; false if no window on this desktop has the title.</returns>
    public static async Task<bool> SetTopMostAsync(string titleToken, bool topMost, CancellationToken token = default)
    {
        if (!IsSupported) return false;

        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            var window = Find(titleToken);
            if (window != IntPtr.Zero)
            {
                var ok = SetWindowPos(window, topMost ? HwndTopMost : HwndNoTopMost, 0, 0, 0, 0,
                    SwpNoMove | SwpNoSize | SwpNoActivate);
                if (!ok) Log.LogWarning("Couldn't change the browser window's stay-on-top (Win32 error {Error})", Marshal.GetLastWin32Error());
                else Log.LogInformation("Browser window {State}", topMost ? "kept on top" : "no longer kept on top");
                return ok;
            }
            if (Stopwatch.GetElapsedTime(started) >= FindTimeout)
            {
                Log.LogInformation("Stay on top: no browser window with the page's title on this computer's desktop");
                return false;
            }
            await Task.Delay(60, token).ConfigureAwait(false);
        }
    }

    /// <summary>The first visible top-level window whose caption contains the text, or zero.</summary>
    [SupportedOSPlatform("windows")]
    private static IntPtr Find(string text)
    {
        var found = IntPtr.Zero;
        var caption = new StringBuilder(512);
        EnumWindowsProc callback = (window, _) =>
        {
            if (!IsWindowVisible(window)) return true;
            caption.Clear();
            if (GetWindowText(window, caption, caption.Capacity) <= 0) return true;
            if (!caption.ToString().Contains(text, StringComparison.Ordinal)) return true;
            found = window;
            return false; // stop
        };
        EnumWindows(callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return found;
    }

    private static readonly IntPtr HwndTopMost = new(-1);
    private static readonly IntPtr HwndNoTopMost = new(-2);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;

    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
