using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ProSyS.Windows;

public sealed record SignedUpdateManifest(string Version, string PackageUrl, string Sha256, string Signature);

public static class SecureUpdateVerifier
{
    /// <param name="installedVersion">When supplied, a manifest that is not strictly newer is rejected (downgrade/replay protection).</param>
    public static SignedUpdateManifest ParseAndVerify(string json, string publicKeyPem, Version? installedVersion = null)
    {
        var manifest = JsonSerializer.Deserialize<SignedUpdateManifest>(json) ?? throw new InvalidDataException("Update manifest is empty.");
        if (manifest is { Version: null } or { PackageUrl: null } or { Sha256: null } or { Signature: null }) throw new InvalidDataException("Update manifest is incomplete.");
        if (!Version.TryParse(manifest.Version, out var offered)) throw new InvalidDataException("Update version is invalid.");
        if (installedVersion is not null && offered <= installedVersion) throw new InvalidDataException($"Update version {offered} is not newer than the installed version {installedVersion}.");
        if (!Uri.TryCreate(manifest.PackageUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("Update package must use HTTPS.");
        if (manifest.Sha256.Length != 64 || !manifest.Sha256.All(Uri.IsHexDigit)) throw new InvalidDataException("Package SHA-256 is invalid.");
        byte[] signature;
        try { signature = Convert.FromBase64String(manifest.Signature); } catch (FormatException ex) { throw new InvalidDataException("Manifest signature is invalid.", ex); }
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);
        var payload = Encoding.UTF8.GetBytes(CanonicalPayload(manifest));
        if (!rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)) throw new CryptographicException("Update manifest signature verification failed.");
        return manifest;
    }

    public static bool VerifyPackage(string path, SignedUpdateManifest manifest)
    {
        if (!File.Exists(path)) return false;
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    public static string CanonicalPayload(SignedUpdateManifest manifest) => $"{manifest.Version}\n{manifest.PackageUrl}\n{manifest.Sha256.ToUpperInvariant()}";
}
