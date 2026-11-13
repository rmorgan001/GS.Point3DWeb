using System.Text;

namespace GS.Point3DWeb.Support;

/// <summary>Finds and reads the app's log files (see <see cref="AppLog.Folder"/>) for the log viewer.</summary>
public static class LogFiles
{
    /// <summary>The file name pattern Serilog writes: point3dweb-yyyyMMdd.log, plus _001… when a day's file fills up.</summary>
    public const string Pattern = "point3dweb-*.log";

    /// <summary>The most recently written log file, or null if there is none yet.</summary>
    public static FileInfo? Latest()
    {
        var folder = new DirectoryInfo(AppLog.Folder);
        return folder.Exists
            ? folder.GetFiles(Pattern).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()
            : null;
    }

    /// <summary>Opens a log file for reading while Serilog keeps writing to it.</summary>
    public static FileStream OpenShared(FileInfo file)
        => new(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    /// <summary>
    /// The end of a log file, at most <paramref name="maxBytes"/>, starting at a whole line. Truncated is
    /// true when the start of the file was left out.
    /// </summary>
    public static (string Text, bool Truncated) ReadTail(FileInfo file, int maxBytes)
    {
        using var stream = OpenShared(file);
        var truncated = stream.Length > maxBytes;
        if (truncated) stream.Seek(-maxBytes, SeekOrigin.End);

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: !truncated);
        var text = reader.ReadToEnd();
        if (truncated)
        {
            // Drop the partial first line.
            var firstBreak = text.IndexOf('\n');
            text = firstBreak >= 0 ? text[(firstBreak + 1)..] : "";
        }
        return (text, truncated);
    }
}
