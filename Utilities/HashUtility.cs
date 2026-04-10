using System.Security.Cryptography;
using System.Text;

namespace ZatcaFatooraDotNet8Sample.Utilities;

public static class HashUtility
{
    public static string Sha256Base64(string text)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(text);
        return Convert.ToBase64String(sha.ComputeHash(bytes));
    }

    public static byte[] Sha256Bytes(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(bytes);
    }

    /// <summary>
    /// ZATCA / zatca-xml-js <c>getCertificateHash</c>: SHA-256 of the UTF-8 bytes of the certificate
    /// Base64 body (PEM content without headers), then lowercase hex of the digest, then Base64 of those ASCII hex bytes.
    /// </summary>
    public static string ZatcaCertificateBase64BodyDigestValueBase64(string certificateBase64Body)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(certificateBase64Body));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return Convert.ToBase64String(Encoding.ASCII.GetBytes(hex));
    }

    /// <summary>Standard XMLDSig: Base64 of raw SHA-256 digest of DER (not used for ZATCA CertDigest in portal checks).</summary>
    public static string CertificateDerSha256DigestValueBase64(ReadOnlySpan<byte> certificateDer)
        => Convert.ToBase64String(SHA256.HashData(certificateDer));
}
