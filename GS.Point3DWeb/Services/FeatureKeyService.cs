using GS.Point3DWeb.Localization;
using GS.Point3DWeb.Support;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace GS.Point3DWeb.Services;

/// <summary>Who a feature key was issued to, and until when.</summary>
public sealed record FeatureKeyInfo(string Name, DateOnly Issued, DateOnly? Expires)
{
    public bool IsExpired(DateOnly today) => Expires is { } expires && today > expires;
}

/// <summary>
/// Feature key (registered as a singleton). Without a valid key the app is limited to two 3D views
/// (<see cref="FreeMaxViews"/>) and five servers (<see cref="FreeMaxServers"/>); a valid key unlocks
/// up to four views and any number of servers.
///
/// The key format and the public key are in <see cref="FeatureKeyFormat"/>. Only the public key is
/// here, so keys can be checked but not made; tools/GS.Point3DWeb.KeyGen issues them.
/// The key is stored in feature.key next to the server list.
/// </summary>
public sealed class FeatureKeyService
{
    public const int FreeMaxViews = 2;
    public const int FreeMaxServers = 5;
    public const int LicensedMaxViews = SettingsService.QuadrantCount;

    private readonly ILogger<FeatureKeyService> _logger;
    private readonly Lock _lock = new();

    public FeatureKeyService(ILogger<FeatureKeyService> logger)
    {
        _logger = logger;
        FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GS.Point3DWeb",
            "feature.key");
        Load();
    }

    public string FilePath { get; }

    /// <summary>The installed key, if it is genuine (it may still have expired; see <see cref="IsLicensed"/>).</summary>
    public FeatureKeyInfo? Key { get; private set; }

    /// <summary>Raised when a key is added or removed (all browser tabs share this service).</summary>
    public event Action? Changed;

    public bool IsLicensed => Key is { } key && !key.IsExpired(DateOnly.FromDateTime(DateTime.Now));

    public int MaxViews => IsLicensed ? LicensedMaxViews : FreeMaxViews;

    public int MaxServers => IsLicensed ? int.MaxValue : FreeMaxServers;

    /// <summary>Checks a key and, if it is valid and current, installs it.</summary>
    public bool TryApply(string? keyText, out string error)
    {
        var info = Verify(keyText, out error);
        if (info is null) return false;
        if (info.IsExpired(DateOnly.FromDateTime(DateTime.Now)))
        {
            error = L.T("This key expired on {0}.", info.Expires?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            return false;
        }

        try
        {
            lock (_lock)
            {
                // Write to a temp file then swap it in, so a failed write can't leave the installed key
                // truncated (and the app unlicensed after a restart) while it still shows as licensed.
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var tempPath = FilePath + ".tmp";
                File.WriteAllText(tempPath, keyText!.Trim());
                File.Move(tempPath, FilePath, overwrite: true);
                Key = info;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // e.g. a read-only folder, or the file locked by another program: report it in the dialog.
            error = L.T("Could not save the key: {0}", ex.Message);
            return false;
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>Removes the installed key (back to the free limits). False, with a reason, if the file can't be deleted.</summary>
    public bool TryRemove(out string error)
    {
        try
        {
            lock (_lock)
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
                Key = null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = L.T("Could not remove the key: {0}", ex.Message);
            return false;
        }
        error = "";
        Changed?.Invoke();
        return true;
    }

    /// <summary>Returns the key's details if its signature is genuine, otherwise null and a reason.</summary>
    public static FeatureKeyInfo? Verify(string? keyText, out string error)
    {
        error = "";
        if (!FeatureKeyFormat.TryParse(keyText, out var payload, out var signature))
        {
            error = L.T("That isn't a GS Point3D Web feature key.");
            return null;
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(FeatureKeyFormat.PublicKeyPem);
            if (!ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256))
            {
                error = L.T("This key isn't valid.");
                return null;
            }

            using var json = JsonDocument.Parse(payload);
            var root = json.RootElement;
            var name = root.GetProperty("name").GetString() ?? "";
            // Keys always use the Gregorian calendar, whatever the server's culture.
            var issued = DateOnly.ParseExact(root.GetProperty("issued").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            DateOnly? expires = root.TryGetProperty("expires", out var e) && e.ValueKind == JsonValueKind.String
                ? DateOnly.ParseExact(e.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                : null;
            return new FeatureKeyInfo(name, issued, expires);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or CryptographicException
                                       or KeyNotFoundException or InvalidOperationException)
        {
            error = L.T("This key isn't valid.");
            return null;
        }
    }

    private void Load()
    {
        if (!File.Exists(FilePath)) return;
        try
        {
            Key = Verify(File.ReadAllText(FilePath), out var error);
            if (Key is null) _logger.LogWarning("Ignoring feature key in {FilePath}: {Error}", FilePath, error);
        }
        // Runs while the service is being created: an error escaping here would break every page that
        // uses it, so an unreadable file (locked, or no access: written by another account) is logged
        // and the app runs with the free limits.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                       or System.Security.SecurityException)
        {
            _logger.LogWarning(ex, "Could not read feature key from {FilePath}; using the free limits", FilePath);
        }
    }
}
