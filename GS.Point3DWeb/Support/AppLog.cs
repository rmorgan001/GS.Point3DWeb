using System.Collections.Concurrent;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.EventLog;

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

    /// <summary>
    /// Limits the Windows Event Log to warnings and errors. Entries use .NET's default source, ".NET Runtime":
    /// a source named after the app would have to be registered, which needs administrator rights.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static void EventLogWarningsAndErrorsOnly(EventLogSettings eventLog)
        => eventLog.Filter = (_, level) => level >= LogLevel.Warning;

    /// <summary>A logger for a category (a class name, shown in each log line).</summary>
    public static ILogger For(string category) => Loggers.GetOrAdd(category, c => _factory.CreateLogger(c));

    /// <summary>
    /// The log folder. On Windows, Documents\GS.Point3DWeb, where it is easy to find. Elsewhere, for an
    /// account without a Documents folder (a service account), or when Documents can't be written (Windows
    /// Security's Controlled folder access blocks apps it doesn't know), a "logs" folder next to the server
    /// list: %APPDATA%\GS.Point3DWeb\logs (~/.config/GS.Point3DWeb/logs on Linux).
    /// </summary>
    public static string Folder { get; } = ChooseFolder();

    /// <summary>Set when Documents was chosen but couldn't be written, so the logs went to the fallback.</summary>
    public static string? FolderProblem { get; private set; }

    private static string ChooseFolder()
    {
        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GS.Point3DWeb", "logs");
        if (!OperatingSystem.IsWindows()) return fallback;

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrEmpty(documents)) return fallback;

        var folder = Path.Combine(documents, "GS.Point3DWeb");
        try
        {
            // The file sink fails silently if it can't write, so check first with a file of our own.
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, $".write-test-{Environment.ProcessId}.tmp");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return folder;
        }
        catch (Exception ex)
        {
            FolderProblem = $"{folder} can't be written ({ex.Message})";
            return fallback;
        }
    }
}
