using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GS.Point3DWeb.Services;

/// <summary>Who a feature key was issued to, and until when.</summary>
public sealed record FeatureKeyInfo(string Name, DateOnly Issued, DateOnly? Expires)
{
    public bool IsExpired(DateOnly today) => Expires is { } expires && today > expires;
}

/// <summary>
/// Feature key (registered as a singleton). Without a valid key the app is limited to one 3D view
/// and five servers; a valid key unlocks up to four views and any number of servers.
///
/// A key is <c>GSP3W1.&lt;payload&gt;.&lt;signature&gt;</c> (base64url): the payload is JSON
/// {name, issued, expires} and the signature is ECDSA P-256 / SHA-256 over it. Only the public key
/// is here, so keys can be checked but not made; tools/GS.Point3DWeb.KeyGen issues them.
/// The key is stored in feature.key next to the server list.
/// </summary>
public sealed class FeatureKeyService
{
    public const int FreeMaxViews = 1;
    public const int FreeMaxServers = 5;
    public const int LicensedMaxViews = SettingsService.QuadrantCount;

    private const string Prefix = "GSP3W1";

    // Public half of the signing key (tools/GS.Point3DWeb.KeyGen: "public").
    private const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEnTK/KmWAVvDlUzLvMK0GUImFCFYY
        qxusTKyLIalDpbQKrfCPegSBJnbY8v3d02KzBsh4tq0nTw3onhEf/6sQKw==
        -----END PUBLIC KEY-----
        """;

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
            error = $"This key expired on {info.Expires:yyyy-MM-dd}.";
            return false;
        }

        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, keyText!.Trim());
            Key = info;
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>Removes the installed key (back to the free limits).</summary>
    public void Remove()
    {
        lock (_lock)
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
            Key = null;
        }
        Changed?.Invoke();
    }

    /// <summary>Returns the key's details if its signature is genuine, otherwise null and a reason.</summary>
    public static FeatureKeyInfo? Verify(string? keyText, out string error)
    {
        error = "";
        var parts = (keyText ?? "").Trim().Split('.');
        if (parts.Length != 3 || parts[0] != Prefix)
        {
            error = "That isn't a GS Point3D Web feature key.";
            return null;
        }

        try
        {
            var payload = FromBase64Url(parts[1]);
            var signature = FromBase64Url(parts[2]);

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(PublicKeyPem);
            if (!ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256))
            {
                error = "This key isn't valid.";
                return null;
            }

            using var json = JsonDocument.Parse(payload);
            var root = json.RootElement;
            var name = root.GetProperty("name").GetString() ?? "";
            var issued = DateOnly.ParseExact(root.GetProperty("issued").GetString()!, "yyyy-MM-dd");
            DateOnly? expires = root.TryGetProperty("expires", out var e) && e.ValueKind == JsonValueKind.String
                ? DateOnly.ParseExact(e.GetString()!, "yyyy-MM-dd")
                : null;
            return new FeatureKeyInfo(name, issued, expires);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or CryptographicException
                                       or KeyNotFoundException or InvalidOperationException)
        {
            error = "This key isn't valid.";
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
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not read feature key from {FilePath}", FilePath);
        }
    }

    private static byte[] FromBase64Url(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
