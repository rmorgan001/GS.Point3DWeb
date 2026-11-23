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

namespace GS.Point3DWeb.Services;

/// <summary>
/// A picture of the user's own for a server's 3D view background: one per server, kept in the settings folder
/// (backgrounds\{server id}.jpg, .png or .webp) and served at /background/{server id}. Only JPEG, PNG and WebP
/// images are kept, told apart by their first bytes rather than their name.
/// </summary>
public static class BackgroundImages
{
    /// <summary>The largest picture accepted.</summary>
    public const long MaxBytes = 15 * 1024 * 1024;

    /// <summary>The file picker's filter.</summary>
    public const string Accept = "image/jpeg,image/png,image/webp";

    private static readonly (string Extension, string ContentType)[] Kinds =
        [(".jpg", "image/jpeg"), (".png", "image/png"), (".webp", "image/webp")];

    /// <summary>Where the pictures are kept: beside servers.json.</summary>
    public static string Folder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GS.Point3DWeb", "backgrounds");

    /// <summary>A server id as it is saved: a GUID, so it can't name a file anywhere else.</summary>
    private static bool IsServerId(string? id) => Guid.TryParse(id, out _);

    /// <summary>The server's picture, if it has one.</summary>
    public static FileInfo? Find(string? serverId)
    {
        if (!IsServerId(serverId)) return null;
        return Kinds.Select(k => new FileInfo(Path.Combine(Folder, serverId + k.Extension))).FirstOrDefault(f => f.Exists);
    }

    /// <summary>The address the page loads the server's picture from (it changes with the picture), or null.</summary>
    public static string? Url(string? serverId)
        => Find(serverId) is { } file ? $"background/{serverId}?v={file.LastWriteTimeUtc.Ticks}" : null;

    /// <summary>The picture's content type, from its extension (it was checked when saved).</summary>
    public static string ContentType(FileInfo file)
        => Kinds.FirstOrDefault(k => k.Extension.Equals(file.Extension, StringComparison.OrdinalIgnoreCase)).ContentType
           ?? "application/octet-stream";

    /// <summary>The kind of image the bytes start with (JPEG, PNG or WebP), or null for anything else.</summary>
    public static string? KindOf(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return ".jpg";
        if (head.Length >= 8 && head[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return ".png";
        if (head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8)) return ".webp";
        return null;
    }

    /// <summary>
    /// Keeps a picture for the server, replacing any it had. Returns false (keeping nothing) for one too big or
    /// not a JPEG, PNG or WebP image.
    /// </summary>
    public static async Task<bool> SaveAsync(string serverId, Stream picture, CancellationToken token = default)
    {
        if (!IsServerId(serverId)) return false;
        using var copy = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await picture.ReadAsync(buffer, token)) > 0)
        {
            if (copy.Length + read > MaxBytes) return false;
            copy.Write(buffer, 0, read);
        }
        if (KindOf(copy.GetBuffer().AsSpan(0, (int)Math.Min(copy.Length, 16))) is not { } extension) return false;

        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, serverId + extension);
        // Written aside and moved in, so a failed write keeps the old picture.
        var temp = path + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temp, copy.ToArray(), token);
            File.Move(temp, path, overwrite: true);
            // A picture of another type has a different name: removed once the new one is in place.
            foreach (var kind in Kinds)
            {
                var other = Path.Combine(Folder, serverId + kind.Extension);
                if (other != path && File.Exists(other)) File.Delete(other);
            }
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
        return true;
    }

    /// <summary>Removes the server's picture, if it has one.</summary>
    public static void Delete(string serverId)
    {
        if (!IsServerId(serverId)) return;
        foreach (var kind in Kinds)
        {
            var path = Path.Combine(Folder, serverId + kind.Extension);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
