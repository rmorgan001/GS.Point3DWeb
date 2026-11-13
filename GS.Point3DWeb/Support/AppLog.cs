using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;

namespace GS.Point3DWeb.Support;

/// <summary>
/// Loggers for the static classes that have no dependency injection (<see cref="Services.AlpacaConnector"/>,
/// <see cref="Services.ComTelescopes"/>). <see cref="Factory"/> is set once at start-up; until then (and in
/// tools or tests that never set it) nothing is logged.
/// </summary>
public static class AppLog
{
    private static readonly ConcurrentDictionary<string, ILogger> Loggers = new();
    private static ILoggerFactory _factory = NullLoggerFactory.Instance;

    /// <summary>The app's logger factory. Setting it replaces any loggers handed out before.</summary>
    public static ILoggerFactory Factory
    {
        get => _factory;
        set
        {
            _factory = value;
            Loggers.Clear();
        }
    }

    /// <summary>A logger for a category (a class name, shown in each log line).</summary>
    public static ILogger For(string category) => Loggers.GetOrAdd(category, c => _factory.CreateLogger(c));

    /// <summary>The log folder: logs next to the server list, in %APPDATA%\GS.Point3DWeb\logs.</summary>
    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GS.Point3DWeb", "logs");
}
