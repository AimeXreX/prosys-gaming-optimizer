using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProSyS.Windows;

/// <summary>Schema 2 update manifest. <see cref="ExpiresAt"/> is covered by the signature so an old manifest cannot be replayed forever.</summary>
public sealed record SignedUpdateManifest(int SchemaVersion, string Version, string PackageUrl, string Sha256, DateTimeOffset ExpiresAt, string Signature);

public static class SecureUpdateVerifier
{
    public const int SchemaVersion = 2;
    public static readonly TimeSpan MaximumValidity = TimeSpan.FromDays(90);

    /// <param name="installedVersion">When supplied, a manifest that is not strictly newer is rejected (downgrade/replay protection).</param>
    /// <param name="now">Current time; injectable for tests.</param>
    public static SignedUpdateManifest ParseAndVerify(string json, string publicKeyPem, Version? installedVersion = null, DateTimeOffset? now = null)
    {
        var manifest = JsonSerializer.Deserialize<SignedUpdateManifest>(json) ?? throw new InvalidDataException("Update manifest is empty.");
        if (manifest is { Version: null } or { PackageUrl: null } or { Sha256: null } or { Signature: null }) throw new InvalidDataException("Update manifest is incomplete.");
        if (manifest.SchemaVersion != SchemaVersion) throw new InvalidDataException($"Update manifest schema {manifest.SchemaVersion} is not supported (expected {SchemaVersion}).");
        if (!Version.TryParse(manifest.Version, out var offered)) throw new InvalidDataException("Update version is invalid.");
        if (!Uri.TryCreate(manifest.PackageUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("Update package must use HTTPS.");
        if (manifest.Sha256.Length != 64 || !manifest.Sha256.All(Uri.IsHexDigit)) throw new InvalidDataException("Package SHA-256 is invalid.");
        byte[] signature;
        try { signature = Convert.FromBase64String(manifest.Signature); } catch (FormatException ex) { throw new InvalidDataException("Manifest signature is invalid.", ex); }
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);
        var payload = Encoding.UTF8.GetBytes(CanonicalPayload(manifest));
        if (!rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)) throw new CryptographicException("Update manifest signature verification failed.");
        // Checked after the signature so the expiry being compared is the signed one.
        var current = now ?? DateTimeOffset.UtcNow;
        if (manifest.ExpiresAt <= current) throw new InvalidDataException($"Update manifest expired at {manifest.ExpiresAt:u}.");
        if (manifest.ExpiresAt > current + MaximumValidity) throw new InvalidDataException($"Update manifest validity exceeds {MaximumValidity.TotalDays:F0} days.");
        if (installedVersion is not null && offered <= installedVersion) throw new InvalidDataException($"Update version {offered} is not newer than the installed version {installedVersion}.");
        return manifest;
    }

    public static bool VerifyPackage(string path, SignedUpdateManifest manifest)
    {
        if (!File.Exists(path)) return false;
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The exact bytes that are signed: a schema tag followed by every field except the signature.</summary>
    public static string CanonicalPayload(SignedUpdateManifest manifest) =>
        $"prosys-update-v{manifest.SchemaVersion}\n{manifest.Version}\n{manifest.PackageUrl}\n{manifest.Sha256.ToUpperInvariant()}\n{manifest.ExpiresAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)}";
}
