namespace ZatcaFatooraDotNet8Sample.Models.Domain;

public sealed class SignedInvoiceArtifacts
{
    public string SignedXml { get; set; } = string.Empty;
    public string InvoiceHashBase64 { get; set; } = string.Empty;
    public string SignatureBase64 { get; set; } = string.Empty;
    public string? PublicKeyBase64 { get; set; }
    public string? CertificateSignatureBase64 { get; set; }
}
