using System.Text;

namespace GS.Point3DWeb.Support;

/// <summary>Finds and reads the app's log files (see <see cref="AppLog.Folder"/>) for the log viewer.</summary>
public static class LogFiles
{
    /// <summary>The file name pattern Serilog writes: point3dweb-yyyyMMdd.log, plus _001… when a day's file fills up.</summary>
    public const string Pattern = "point3dweb-*.log";

    /// <summary>
    /// The newest log file, or null if there is none yet. Chosen by name (the date, then the _001… part sorts
    /// after the plain name), not by the folder's size or time stamps: Windows doesn't keep those current while
    /// the app has the file open.
    /// </summary>
    public static FileInfo? Latest()
    {
        var folder = new DirectoryInfo(AppLog.Folder);
        return folder.Exists
            ? folder.GetFiles(Pattern).MaxBy(f => f.Name, StringComparer.Ordinal)
            : null;
    }

    /// <summary>Opens a log file for reading while Serilog keeps writing to it.</summary>
    public static FileStream OpenShared(FileInfo file)
        => new(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    /// <summary>A line of the log and where it starts in the file (in bytes).</summary>
    public readonly record struct Line(long Offset, string Text);

    /// <summary>What <see cref="Read"/> found.</summary>
    /// <param name="Lines">The whole lines read.</param>
    /// <param name="End">Where the next read starts: just after the last whole line (a line still being written waits).</param>
    /// <param name="Length">The file's current length, from the open file (always current).</param>
    /// <param name="FromStart">True if this read replaced what was shown (a first read, or the file started over),
    /// false if it continues from the previous one.</param>
    /// <param name="Truncated">True if the start of the file was left out (only on a read that starts over).</param>
    public sealed record Chunk(IReadOnlyList<Line> Lines, long End, long Length, bool FromStart, bool Truncated);

    /// <summary>
    /// Reads whole lines from <paramref name="from"/> to the end of the file. Starts over with the last
    /// <paramref name="maxBytes"/> instead when <paramref name="from"/> is negative, past the end (the file
    /// was replaced), or more than <paramref name="maxBytes"/> behind.
    /// </summary>
    public static Chunk Read(FileInfo file, long from, int maxBytes)
    {
        using var stream = OpenShared(file);
        var length = stream.Length;
        if (from == length) return new Chunk([], from, length, false, false);

        var fromStart = from < 0 || from > length || length - from > maxBytes;
        var start = fromStart ? Math.Max(0, length - maxBytes) : from;
        var truncated = fromStart && start > 0;

        var buffer = new byte[length - start];
        stream.Seek(start, SeekOrigin.Begin);
        stream.ReadExactly(buffer);

        var lines = new List<Line>();
        var lineStart = 0;
        if (truncated)
        {
            // Started in the middle of a line: skip to the next one.
            var firstBreak = Array.IndexOf(buffer, (byte)'\n');
            lineStart = firstBreak < 0 ? buffer.Length : firstBreak + 1;
        }
        for (var i = lineStart; i < buffer.Length; i++)
        {
            if (buffer[i] != (byte)'\n') continue;
            var text = Encoding.UTF8.GetString(buffer, lineStart, i - lineStart).TrimEnd('\r').TrimStart('﻿');
            if (text.Length > 0) lines.Add(new Line(start + lineStart, text));
            lineStart = i + 1;
        }
        return new Chunk(lines, start + lineStart, length, fromStart, truncated);
    }
}
