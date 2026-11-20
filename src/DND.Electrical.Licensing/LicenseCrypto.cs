using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DND.Electrical.Licensing;

/// <summary>
/// ECDSA P-256 / SHA-256 signing and verification of licence documents. The private key exists only on the
/// licensing server; clients hold public keys (by key id) and verify locally, offline.
/// </summary>
public static class LicenseCrypto
{
    public const string Algorithm = "ECDSA-P256-SHA256";

    public static (string keyId, string privatePem, string publicPem) GenerateKeyPair()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pub = ec.ExportSubjectPublicKeyInfoPem();
        return (KeyIdOf(pub), ec.ExportPkcs8PrivateKeyPem(), pub);
    }

    /// <summary>Key id = first 16 hex characters of SHA-256 over the DER public key.</summary>
    public static string KeyIdOf(string publicPem)
    {
        using var ec = ECDsa.Create();
        ec.ImportFromPem(publicPem);
        return Convert.ToHexString(SHA256.HashData(ec.ExportSubjectPublicKeyInfo()))[..16].ToLowerInvariant();
    }

    public static string Canonical<T>(T value) => JsonSerializer.Serialize(value, LicenseJson.Options);

    public static SignedLicense Sign(LicenseClaims claims, string privatePem, string keyId)
    {
        var payload = Encoding.UTF8.GetBytes(Canonical(claims));
        return new SignedLicense { KeyId = keyId, Payload = Convert.ToBase64String(payload), Signature = Convert.ToBase64String(SignBytes(payload, privatePem)) };
    }

    public static (string payload, string signature) SignObject<T>(T value, string privatePem)
    {
        var bytes = Encoding.UTF8.GetBytes(Canonical(value));
        return (Convert.ToBase64String(bytes), Convert.ToBase64String(SignBytes(bytes, privatePem)));
    }

    public static byte[] SignBytes(byte[] data, string privatePem)
    {
        using var ec = ECDsa.Create();
        ec.ImportFromPem(privatePem);
        return ec.SignData(data, HashAlgorithmName.SHA256);
    }

    public static bool VerifyBytes(byte[] data, byte[] signature, string publicPem)
    {
        try
        {
            using var ec = ECDsa.Create();
            ec.ImportFromPem(publicPem);
            return ec.VerifyData(data, signature, HashAlgorithmName.SHA256);
        }
        catch (CryptographicException) { return false; }
    }
}

/// <summary>Public keys trusted by the client, by key id.</summary>
public sealed class TrustedKeys
{
    private readonly Dictionary<string, string> _keys = new(StringComparer.OrdinalIgnoreCase);

    public TrustedKeys Add(string publicPem)
    {
        _keys[LicenseCrypto.KeyIdOf(publicPem)] = publicPem;
        return this;
    }

    public string? Find(string keyId) => _keys.GetValueOrDefault(keyId);
    public IReadOnlyCollection<string> KeyIds => _keys.Keys;

    /// <summary>
    /// Keys compiled into the product. The development key below verifies the bundled evaluation licence.
    /// Production deployments generate their own key pair (dnd-license keygen), keep the private key on the
    /// licensing server only, and add the public key here (or in trusted-keys.pem next to the executable).
    /// </summary>
    public static TrustedKeys Product()
    {
        var keys = new TrustedKeys().Add(ProductKeys.DevelopmentPublicKey);
        var extra = Path.Combine(AppContext.BaseDirectory, "Licensing", "trusted-keys.pem");
        if (File.Exists(extra))
            foreach (var block in File.ReadAllText(extra).Split("-----END PUBLIC KEY-----", StringSplitOptions.RemoveEmptyEntries))
                if (block.Contains("BEGIN PUBLIC KEY")) keys.Add(block.Trim() + "\n-----END PUBLIC KEY-----");
        return keys;
    }
}

public static class LicenseVerifier
{
    /// <summary>Verifies signature and product, returning the claims or an error.</summary>
    public static (LicenseClaims? claims, string? error) Verify(SignedLicense doc, TrustedKeys keys)
    {
        if (doc.Algorithm != LicenseCrypto.Algorithm) return (null, $"Unsupported signature algorithm '{doc.Algorithm}'.");
        var key = keys.Find(doc.KeyId);
        if (key is null) return (null, $"The licence was signed with an unknown key ({doc.KeyId}).");
        byte[] payload, sig;
        try { payload = Convert.FromBase64String(doc.Payload); sig = Convert.FromBase64String(doc.Signature); }
        catch (FormatException) { return (null, "The licence file is corrupted."); }
        if (!LicenseCrypto.VerifyBytes(payload, sig, key)) return (null, "The licence signature is invalid (file modified or not issued by D&D | Data&Development).");
        var claims = JsonSerializer.Deserialize<LicenseClaims>(payload, LicenseJson.Options);
        if (claims is null) return (null, "The licence content is invalid.");
        if (!string.Equals(claims.ProductId, Shared.ProductInfo.ProductId, StringComparison.Ordinal)) return (null, "The licence is for another product.");
        if (claims.SchemaVersion > 1) return (null, "The licence requires a newer version of the software.");
        return (claims, null);
    }

    public static bool VerifyObject(string payloadB64, string signatureB64, string keyId, TrustedKeys keys)
    {
        var key = keys.Find(keyId);
        if (key is null) return false;
        try { return LicenseCrypto.VerifyBytes(Convert.FromBase64String(payloadB64), Convert.FromBase64String(signatureB64), key); }
        catch (FormatException) { return false; }
    }
}
