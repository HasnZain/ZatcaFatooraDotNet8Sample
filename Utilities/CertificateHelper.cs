using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace ZatcaFatooraDotNet8Sample.Utilities;

/// <summary>
/// Loads X509Certificate2 from a ZATCA binarySecurityToken.
///
/// ZATCA returns binarySecurityToken as Base64( Base64( DER ) ) — double-encoded.
/// Passing a single-decoded value to X509Certificate2 feeds it a base64 text string
/// instead of raw DER bytes. On Windows the CNG parser silently accepts the malformed
/// input and produces a broken certificate object whose RawData, public key, and hash
/// are all wrong — causing every certificate-related ZATCA error:
///   invalid-certificate, certificate-hashing, certificate-signing-time-format,
///   publicKey_QRCODE_INVALID, CERTIFICATE_SIGNATURE_QRCODE_INVALID.
///
/// Detection: every DER certificate starts with 0x30 (ASN.1 SEQUENCE tag).
/// If the first decoded byte is not 0x30, the bytes are still base64 text — decode again.
/// </summary>
public static class CertificateHelper
{
    public static X509Certificate2 LoadFromBinarySecurityToken(string binarySecurityToken)
    {
        if (string.IsNullOrWhiteSpace(binarySecurityToken))
            throw new ArgumentException("binarySecurityToken must not be empty.", nameof(binarySecurityToken));

        // First decode
        var firstDecode = Convert.FromBase64String(binarySecurityToken);

        byte[] derBytes;
        if (firstDecode.Length > 0 && firstDecode[0] != 0x30)
        {
            // Still base64 text — decode again to reach raw DER
            var innerBase64 = Encoding.ASCII.GetString(firstDecode).Trim();
            derBytes = Convert.FromBase64String(innerBase64);
        }
        else
        {
            derBytes = firstDecode;
        }

        if (derBytes.Length == 0 || derBytes[0] != 0x30)
            throw new InvalidOperationException(
                "binarySecurityToken could not be decoded to a valid DER certificate. " +
                "Expected an ASN.1 SEQUENCE (0x30) as the first byte.");

        return new X509Certificate2(derBytes);
    }
}