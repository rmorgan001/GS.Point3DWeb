using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;

namespace GS.Point3DWeb.Support;

/// <summary>
/// The feature key format, shared by the web app (which checks keys) and tools/GS.Point3DWeb.KeyGen
/// (which issues them; it compiles this same file), so the two can't drift apart.
///
/// A key is <c>GSP3W1.&lt;payload&gt;.&lt;signature&gt;</c>, both parts base64url: the payload is UTF-8 JSON
/// {"name", "issued": "yyyy-MM-dd", "expires": "yyyy-MM-dd" | null} and the signature is ECDSA P-256 /
/// SHA-256 over the payload bytes (IEEE P1363 format).
/// </summary>
public static class FeatureKeyFormat
{
    public const string Prefix = "GSP3W1";

    /// <summary>
    /// The public half of the signing key: the web app checks keys against it. The private half stays
    /// with the key tool, outside the repository ("public" prints it, to paste here after "init").
    /// </summary>
    public const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEnTK/KmWAVvDlUzLvMK0GUImFCFYY
        qxusTKyLIalDpbQKrfCPegSBJnbY8v3d02KzBsh4tq0nTw3onhEf/6sQKw==
        -----END PUBLIC KEY-----
        """;

    /// <summary>A key from its payload and signature.</summary>
    public static string Format(byte[] payload, byte[] signature)
        => $"{Prefix}.{Base64Url.EncodeToString(payload)}.{Base64Url.EncodeToString(signature)}";

    /// <summary>Splits a key into its payload and signature; false if it isn't in this format.</summary>
    public static bool TryParse(string? key, [NotNullWhen(true)] out byte[]? payload, [NotNullWhen(true)] out byte[]? signature)
    {
        payload = signature = null;
        var parts = (key ?? "").Trim().Split('.');
        if (parts.Length != 3 || parts[0] != Prefix) return false;
        try
        {
            payload = Base64Url.DecodeFromChars(parts[1]);
            signature = Base64Url.DecodeFromChars(parts[2]);
            return true;
        }
        catch (FormatException)
        {
            payload = signature = null;
            return false;
        }
    }
}
