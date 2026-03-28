using System.Text;

namespace ZatcaFatooraDotNet8Sample.Services;

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
        string? publicKeyBase64 = null,
        string? certificateSignatureBase64 = null)
    {
        using var buffer = new MemoryStream();

        WriteTag(buffer, 1, sellerName);
        WriteTag(buffer, 2, sellerVatNumber);
        WriteTag(buffer, 3, issueDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        WriteTag(buffer, 4, totalWithVat.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        WriteTag(buffer, 5, vatTotal.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));

        if (!string.IsNullOrWhiteSpace(invoiceHashBase64))
        {
            WriteTag(buffer, 6, invoiceHashBase64);
        }

        if (!string.IsNullOrWhiteSpace(signatureBase64))
        {
            WriteTag(buffer, 7, signatureBase64);
        }

        if (!string.IsNullOrWhiteSpace(publicKeyBase64))
        {
            WriteTag(buffer, 8, publicKeyBase64);
        }

        if (!string.IsNullOrWhiteSpace(certificateSignatureBase64))
        {
            WriteTag(buffer, 9, certificateSignatureBase64);
        }

        return Convert.ToBase64String(buffer.ToArray());
    }

    private static void WriteTag(Stream stream, byte tag, string value)
    {
        var valueBytes = Encoding.UTF8.GetBytes(value);
        stream.WriteByte(tag);
        WriteLength(stream, valueBytes.Length);
        stream.Write(valueBytes, 0, valueBytes.Length);
    }

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
        {
            stream.WriteByte(b);
        }
    }
}
