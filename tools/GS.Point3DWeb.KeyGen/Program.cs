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

// GS Point3D Web feature-key generator.
//
// The key format and the web app's public key are in GS.Point3DWeb/Support/FeatureKeyFormat.cs, which
// this tool compiles too, so the two always agree. The web app holds only the public key, so it can check
// keys but not make them. The private key stays with this tool, outside the repository.
//
// Commands:
//   init   [--key <file>] [--force]                  create the signing key pair; prints the public key
//   public [--key <file>]                            print the public key (paste into FeatureKeyFormat.cs)
//   issue  --name "<licensee>" [--expires yyyy-MM-dd] [--key <file>]   print a new feature key
//   verify <feature-key>                             check a key the way the web app does

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GS.Point3DWeb.Support;

var defaultKeyFile = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "GS.Point3DWeb.KeyGen", "private-key.pem");

if (args.Length == 0) return Usage();

// An option given without its value (e.g. "--name --expires 2027-01-01") is an error, not the next
// option's name taken as the value.
foreach (var option in new[] { "--key", "--name", "--expires" })
{
    if (HasFlag(option) && Option(option) is null)
    {
        Console.Error.WriteLine($"{option} needs a value.");
        return 1;
    }
}

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
            WritePrivateKey(keyFile, ecdsa.ExportPkcs8PrivateKeyPem());
            Console.WriteLine($"Private key written to {keyFile}");
            Console.WriteLine("Back this file up and keep it private.");
            Console.WriteLine();
            Console.WriteLine("Public key: paste it into PublicKeyPem in GS.Point3DWeb/Support/FeatureKeyFormat.cs,");
            Console.WriteLine("then rebuild the web app and this tool:");
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
                // The app refuses a key past its date (FeatureKeyInfo.IsExpired), so one would be dead on arrival.
                if (expiresDate < DateOnly.FromDateTime(DateTime.Now))
                {
                    Console.Error.WriteLine($"--expires {expiresText} is in the past: the app would refuse the key as expired.");
                    return 1;
                }
                expires = expiresDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            using var ecdsa = LoadPrivateKey(keyFile);
            if (!MatchesApp(ecdsa))
            {
                // Signing anyway would hand out a key the app rejects.
                Console.Error.WriteLine($"The key in {keyFile} isn't the one the web app checks against " +
                                        "(FeatureKeyFormat.PublicKeyPem). Use the right --key, or paste this key's " +
                                        "public half ('public') into FeatureKeyFormat.cs and rebuild the app and this tool.");
                return 1;
            }

            var payload = JsonSerializer.SerializeToUtf8Bytes(new
            {
                name = name.Trim(),
                issued = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                expires
            });
            Console.WriteLine(FeatureKeyFormat.Format(payload, ecdsa.SignData(payload, HashAlgorithmName.SHA256)));
            return 0;
        }

        case "verify":
        {
            if (args.Length < 2) return Usage();
            if (!FeatureKeyFormat.TryParse(args[1], out var payload, out var signature))
            {
                Console.WriteLine("Invalid: not a GS Point3D Web feature key.");
                return 1;
            }

            // The same checks as the web app (FeatureKeyService.Verify and TryApply): signed with the key built
            // into the app, a payload it can read, and not past its date.
            using var appKey = ECDsa.Create();
            appKey.ImportFromPem(FeatureKeyFormat.PublicKeyPem);
            if (!appKey.VerifyData(payload, signature, HashAlgorithmName.SHA256))
            {
                Console.WriteLine("Invalid: the web app would reject it (not signed with the app's key, or changed).");
                return 1;
            }

            var json = Encoding.UTF8.GetString(payload);
            if (ReadPayload(payload) is not { } info)
            {
                Console.WriteLine($"Invalid: signed, but the web app can't read its details: {json}");
                return 1;
            }
            if (info.Expires is { } until && DateOnly.FromDateTime(DateTime.Now) > until)
            {
                Console.WriteLine($"Expired on {until:yyyy-MM-dd}: the web app would refuse it. {json}");
                return 1;
            }
            Console.WriteLine($"Valid (the web app accepts it): {json}");
            return 0;
        }

        default:
            return Usage();
    }
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException
                               or FormatException or ArgumentException)
{
    // A missing or unreadable key file, a file that isn't a PEM key, a folder that can't be written...
    Console.Error.WriteLine(ex.Message);
    return 1;
}

// The value after an option, or null if the option is missing or has no value after it.
string? Option(string name)
{
    var index = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
        ? args[index + 1]
        : null;
}

bool HasFlag(string name) => args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

// Reads a key's details as the web app does (FeatureKeyService.Verify): null if it couldn't.
static (string Name, DateOnly Issued, DateOnly? Expires)? ReadPayload(byte[] payload)
{
    try
    {
        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;
        var name = root.GetProperty("name").GetString() ?? "";
        var issued = DateOnly.ParseExact(root.GetProperty("issued").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        DateOnly? expires = root.TryGetProperty("expires", out var e) && e.ValueKind == JsonValueKind.String
            ? DateOnly.ParseExact(e.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
        return (name, issued, expires);
    }
    catch (Exception ex) when (ex is FormatException or JsonException or KeyNotFoundException
                                   or InvalidOperationException or ArgumentNullException)
    {
        return null;
    }
}

static ECDsa LoadPrivateKey(string path)
{
    if (!File.Exists(path))
        throw new IOException($"No signing key at {path}. Run 'init' first (or pass --key).");
    var ecdsa = ECDsa.Create();
    try
    {
        ecdsa.ImportFromPem(File.ReadAllText(path));
    }
    catch
    {
        ecdsa.Dispose();
        throw;
    }
    return ecdsa;
}

// Whether a private key is the pair of the public key compiled into the web app.
static bool MatchesApp(ECDsa privateKey)
{
    using var appKey = ECDsa.Create();
    appKey.ImportFromPem(FeatureKeyFormat.PublicKeyPem);
    return privateKey.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(appKey.ExportSubjectPublicKeyInfo());
}

// Writes the private key readable by its owner only. On Windows the default folder (%APPDATA%) is
// already private to the account; on Linux and macOS the file would otherwise get the usual 0644.
static void WritePrivateKey(string path, string pem)
{
    var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
    if (OperatingSystem.IsWindows())
    {
        Directory.CreateDirectory(folder);
    }
    else
    {
        Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        // The mode only applies to a new file, so a key being replaced (--force) is removed first.
        File.Delete(path);
    }

    var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
    if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    using var writer = new StreamWriter(path, Encoding.ASCII, options);
    writer.Write(pem);
}

static int Usage()
{
    Console.WriteLine("""
        GS Point3D Web feature-key generator

          init   [--key <file>] [--force]                          create the signing key pair
          public [--key <file>]                                    print the public key
          issue  --name "<licensee>" [--expires yyyy-MM-dd] [--key <file>]
          verify <feature-key>                                     check a key as the web app does
        """);
    return 1;
}
