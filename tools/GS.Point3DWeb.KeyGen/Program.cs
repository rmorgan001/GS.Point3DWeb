// GS Point3D Web feature-key generator.
//
// A feature key is   GSP3W1.<payload>.<signature>   (both parts base64url):
//   payload   = UTF-8 JSON {"name":"...","issued":"yyyy-MM-dd","expires":"yyyy-MM-dd" | null}
//   signature = ECDSA P-256 / SHA-256 over the payload bytes (IEEE P1363 format)
// The web app holds only the public key (Services/FeatureKeyService.cs), so it can check keys
// but not make them. The private key stays with this tool, outside the repository.
//
// Commands:
//   init   [--key <file>] [--force]                  create the signing key pair; prints the public key
//   public [--key <file>]                            print the public key (paste into FeatureKeyService)
//   issue  --name "<licensee>" [--expires yyyy-MM-dd] [--key <file>]   print a new feature key
//   verify <feature-key> [--key <file>]              check a key against the key pair

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

const string Prefix = "GSP3W1";

var defaultKeyFile = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "GS.Point3DWeb.KeyGen", "private-key.pem");

if (args.Length == 0) return Usage();

var command = args[0].ToLowerInvariant();
var keyFile = Option("--key") ?? defaultKeyFile;

try
{
    switch (command)
    {
        case "init":
        {
            if (File.Exists(keyFile) && !HasFlag("--force"))
            {
                Console.Error.WriteLine($"A key already exists at {keyFile}. Use --force to replace it " +
                                        "(keys issued with the old key will stop working).");
                return 1;
            }

            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            Directory.CreateDirectory(Path.GetDirectoryName(keyFile)!);
            File.WriteAllText(keyFile, ecdsa.ExportPkcs8PrivateKeyPem());
            Console.WriteLine($"Private key written to {keyFile}");
            Console.WriteLine("Back this file up and keep it private.");
            Console.WriteLine();
            Console.WriteLine("Public key (goes in the web app):");
            Console.WriteLine(ecdsa.ExportSubjectPublicKeyInfoPem());
            return 0;
        }

        case "public":
        {
            using var ecdsa = LoadPrivateKey(keyFile);
            Console.WriteLine(ecdsa.ExportSubjectPublicKeyInfoPem());
            return 0;
        }

        case "issue":
        {
            var name = Option("--name");
            if (string.IsNullOrWhiteSpace(name))
            {
                Console.Error.WriteLine("--name is required.");
                return 1;
            }

            string? expires = null;
            if (Option("--expires") is { } expiresText)
            {
                // Key dates are always Gregorian (invariant), whatever this machine's culture.
                if (!DateOnly.TryParseExact(expiresText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiresDate))
                {
                    Console.Error.WriteLine("--expires must be yyyy-MM-dd.");
                    return 1;
                }
                expires = expiresDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            var payload = JsonSerializer.SerializeToUtf8Bytes(new
            {
                name = name.Trim(),
                issued = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                expires
            });

            using var ecdsa = LoadPrivateKey(keyFile);
            var signature = ecdsa.SignData(payload, HashAlgorithmName.SHA256);
            Console.WriteLine($"{Prefix}.{Base64Url(payload)}.{Base64Url(signature)}");
            return 0;
        }

        case "verify":
        {
            if (args.Length < 2) return Usage();
            var parts = args[1].Trim().Split('.');
            if (parts.Length != 3 || parts[0] != Prefix)
            {
                Console.WriteLine("Invalid: not a GS Point3D Web feature key.");
                return 1;
            }

            var payload = FromBase64Url(parts[1]);
            var signature = FromBase64Url(parts[2]);
            using var ecdsa = LoadPrivateKey(keyFile);
            var valid = ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256);
            Console.WriteLine(valid ? $"Valid: {Encoding.UTF8.GetString(payload)}" : "Invalid signature.");
            return valid ? 0 : 1;
        }

        default:
            return Usage();
    }
}
catch (Exception ex) when (ex is IOException or CryptographicException or FormatException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

string? Option(string name)
{
    var index = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

bool HasFlag(string name) => args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

static ECDsa LoadPrivateKey(string path)
{
    if (!File.Exists(path))
        throw new IOException($"No signing key at {path}. Run 'init' first (or pass --key).");
    var ecdsa = ECDsa.Create();
    ecdsa.ImportFromPem(File.ReadAllText(path));
    return ecdsa;
}

static string Base64Url(byte[] bytes)
    => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

static byte[] FromBase64Url(string text)
{
    var s = text.Replace('-', '+').Replace('_', '/');
    return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
}

static int Usage()
{
    Console.WriteLine("""
        GS Point3D Web feature-key generator

          init   [--key <file>] [--force]                          create the signing key pair
          public [--key <file>]                                    print the public key
          issue  --name "<licensee>" [--expires yyyy-MM-dd] [--key <file>]
          verify <feature-key> [--key <file>]
        """);
    return 1;
}
