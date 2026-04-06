using System.Text;

namespace ZatcaFatooraDotNet8Sample.Services;

public sealed class InvoiceQrService
{
    /// <summary>
    /// Builds the ZATCA QR code TLV payload (base64-encoded) per the e-invoicing specification.
    ///
    /// Tag layout:
    ///   1 = Seller name (UTF-8)
    ///   2 = Seller VAT registration number
    ///   3 = Invoice timestamp  — MUST match IssueDate + IssueTime in the XML exactly
    ///   4 = Total with VAT (2 decimal places)
    ///   5 = VAT total (2 decimal places)
    ///   6 = Invoice hash (base64) — for simplified invoices
    ///   7 = ECDSA signature value (base64) — ds:SignatureValue from the signed XML
    ///   8 = ECDSA public key (uncompressed point, base64) — from the compliance certificate
    ///   9 = Certificate SHA-256 hash (base64) — SHA256(certificate.RawData)
    /// </summary>
    public string BuildBase64(
        string sellerName,
        string sellerVatNumber,
        DateTimeOffset issueDateTime,
        decimal totalWithVat,
        decimal vatTotal,
        string? invoiceHashBase64 = null,
        string? signatureBase64 = null,
        string? publicKeyBase64 = null,
        string? certificateSignatureBase64 = null)
    {
        using var buffer = new MemoryStream();

        WriteTag(buffer, 1, sellerName);
        WriteTag(buffer, 2, sellerVatNumber);

        // FIX invoiceTimeStamp_QRCODE_INVALID:
        // Tag 3 MUST be the exact UTC timestamp that was written into IssueDate/IssueTime
        // of the invoice XML. Both places use issueDateTime.UtcDateTime, so they always match.
        // Format: "yyyy-MM-ddTHH:mm:ssZ" — the trailing 'Z' is required by ZATCA.
        WriteTag(buffer, 3, issueDateTime.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"));

        WriteTag(buffer, 4, totalWithVat.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        WriteTag(buffer, 5, vatTotal.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));

        // Tags 6-9 are only written for simplified invoices (they are absent for standard).
        if (!string.IsNullOrWhiteSpace(invoiceHashBase64))
            WriteTag(buffer, 6, invoiceHashBase64);

        if (!string.IsNullOrWhiteSpace(signatureBase64))
            WriteTag(buffer, 7, signatureBase64);

        if (!string.IsNullOrWhiteSpace(publicKeyBase64))
            WriteTag(buffer, 8, publicKeyBase64);

        if (!string.IsNullOrWhiteSpace(certificateSignatureBase64))
            WriteTag(buffer, 9, certificateSignatureBase64);

        return Convert.ToBase64String(buffer.ToArray());
    }

    private static void WriteTag(Stream stream, byte tag, string value)
    {
        var valueBytes = Encoding.UTF8.GetBytes(value);
        stream.WriteByte(tag);
        WriteLength(stream, valueBytes.Length);
        stream.Write(valueBytes, 0, valueBytes.Length);
    }

    /// <summary>
    /// Writes a BER/DER length field.
    /// Lengths ≤ 127 use the short (1-byte) form.
    /// Longer lengths use the multi-byte long form: 0x80|n followed by n big-endian bytes.
    /// </summary>
    private static void WriteLength(Stream stream, int length)
    {
        if (length <= 127)
        {
            stream.WriteByte((byte)length);
            return;
        }

        var bytes = new List<byte>();
        var remaining = length;
        while (remaining > 0)
        {
            bytes.Insert(0, (byte)(remaining & 0xFF));
            remaining >>= 8;
        }

        stream.WriteByte((byte)(0x80 | bytes.Count));
        foreach (var b in bytes)
            stream.WriteByte(b);
    }
}