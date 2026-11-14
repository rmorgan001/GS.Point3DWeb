using System.Diagnostics;

namespace GS.Point3DWeb.Support;

/// <summary>
/// Opens the app in the default browser when it is started by hand (the exe, a shortcut), once the server is
/// listening, so nobody has to type the address.
/// </summary>
public static class BrowserLauncher
{
    /// <summary>
    /// Whether to open the browser: on unless turned off ("OpenBrowser": false in appsettings.json, or
    /// --OpenBrowser=false), and only where someone can see it. Not in Development (Visual Studio and dotnet run
    /// open it from launchSettings.json), as a Windows service (no desktop session), in a container, on Linux
    /// without a desktop, or over SSH. (A macOS launchd daemon can't be told apart: turn it off there.)
    /// </summary>
    public static bool ShouldOpen(IConfiguration configuration, IHostEnvironment environment)
        => configuration.GetValue("OpenBrowser", true)
           && !environment.IsDevelopment()
           && Environment.UserInteractive
           && !string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase)
           && (!OperatingSystem.IsLinux()
               || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
               || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
           // Started over SSH (macOS or Linux): the browser would open on someone else's screen, if any.
           && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SSH_CONNECTION"));

    /// <summary>
    /// The address to open from the ones the server listens on: an http one if there is one (no certificate
    /// warning; the https redirect still applies if it is set up), else the first. An address on every network
    /// interface (0.0.0.0, [::], * or +) is opened as localhost, as a browser can't go to those. A Unix socket
    /// or named pipe (behind a reverse proxy) can't be opened at all. Null if there is nothing to open.
    /// </summary>
    public static string? PickUrl(IEnumerable<string> addresses)
    {
        var list = addresses.Where(a => !a.Contains("://unix:", StringComparison.OrdinalIgnoreCase)
                                        && !a.Contains("://pipe:", StringComparison.OrdinalIgnoreCase)).ToList();
        var address = list.FirstOrDefault(a => a.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) ?? list.FirstOrDefault();
        if (address is null) return null;
        foreach (var any in new[] { "0.0.0.0", "[::]", "*", "+" })
            address = address.Replace($"://{any}:", "://localhost:").Replace($"://{any}/", "://localhost/");
        return address;
    }

    /// <summary>Opens the address in the default browser. A failure (no browser set up) is logged, never thrown.</summary>
    public static void Open(string url, ILogger log)
    {
        try
        {
            log.LogInformation("Opening {Url} in the browser", url);
            var start = OperatingSystem.IsWindows() ? new ProcessStartInfo(url) { UseShellExecute = true }
                : OperatingSystem.IsMacOS() ? new ProcessStartInfo("open", [url])
                : new ProcessStartInfo("xdg-open", [url]);
            using var process = Process.Start(start);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Could not open the browser; go to {Url}: {Error}", url, ex.Message);
        }
    }
}
