using System.Globalization;
using System.Text;

namespace ZatcaFatooraDotNet8Sample.Services;

/// <summary>
/// ZATCA QR TLV per <c>QRCodeCreation.pdf</c> (ZATCA):
/// <list type="bullet">
/// <item><description>Tag: one byte</description></item>
/// <item><description>Length: one byte — length of the byte array produced by UTF-8 encoding the field value</description></item>
/// <item><description>Value: UTF-8 bytes of the field value. Tags 1–5 and 6–7 are UTF-8 text; tags 8–9 are raw key/signature bytes (decoded from Base64).</description></item>
/// </list>
/// Final QR payload is Base64(concatenated TLV bytes). Max ~500 characters in typical use.
/// Tag 3 time stamp example from the spec: <c>2022-04-25T15:30:00Z</c>.
/// </summary>
public sealed class InvoiceQrService
{
    public string BuildBase64(
    string sellerName,
    string sellerVatNumber,
    DateTimeOffset issueDateTime,
    decimal totalWithVat,
    decimal vatTotal,
    string? invoiceHashBase64 = null,
    string? signatureBase64 = null,
    string? ecdsaPublicKeyBase64 = null,
    string? certificateStampSignatureBase64 = null)
    {
        using var buffer = new MemoryStream();

        // Tags 1-5: Write as UTF-8 Strings
        WriteTag(buffer, 1, Encoding.UTF8.GetBytes(sellerName));
        WriteTag(buffer, 2, Encoding.UTF8.GetBytes(sellerVatNumber));

        // Ensure timestamp is exactly ISO 8601: yyyy-MM-ddTHH:mm:ssZ
        var stamp = issueDateTime.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        WriteTag(buffer, 3, Encoding.UTF8.GetBytes(stamp));

        WriteTag(buffer, 4, Encoding.UTF8.GetBytes(totalWithVat.ToString("0.00", CultureInfo.InvariantCulture)));
        WriteTag(buffer, 5, Encoding.UTF8.GetBytes(vatTotal.ToString("0.00", CultureInfo.InvariantCulture)));

        // Tags 6–7: UTF-8 of the Base64 *text* (same as zatca-xml-js TLV: Buffer.from(string)).
        // Raw SHA-256 / signature bytes in TLV cause invoiceHash_QRCODE_INVALID / INVOICE_SIGNATURE_VALUE_QRCODE_INVALID.
        if (!string.IsNullOrWhiteSpace(invoiceHashBase64))
            WriteTag(buffer, 6, Encoding.UTF8.GetBytes(invoiceHashBase64.Trim()));

        if (!string.IsNullOrWhiteSpace(signatureBase64))
            WriteTag(buffer, 7, Encoding.UTF8.GetBytes(signatureBase64.Trim()));

        // Tag 8: Public Key (Raw bytes, not text)
        if (!string.IsNullOrWhiteSpace(ecdsaPublicKeyBase64))
            WriteTag(buffer, 8, Convert.FromBase64String(ecdsaPublicKeyBase64.Trim()));

        // Tag 9: Certificate Signature (Raw bytes, not text)
        if (!string.IsNullOrWhiteSpace(certificateStampSignatureBase64))
            WriteTag(buffer, 9, Convert.FromBase64String(certificateStampSignatureBase64.Trim()));

        return Convert.ToBase64String(buffer.ToArray());
    }

    private static void WriteTag(Stream stream, byte tag, byte[] value)
    {
        if (value.Length > 255)
            throw new InvalidOperationException($"Tag {tag} length exceeds 255 bytes.");

        stream.WriteByte(tag);
        stream.WriteByte((byte)value.Length);
        stream.Write(value, 0, value.Length);
    }
}
