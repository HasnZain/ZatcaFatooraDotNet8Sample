namespace ZatcaFatooraDotNet8Sample.Models.Domain;

public sealed class InvoiceBuildResult
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public string Uuid { get; set; } = string.Empty;
    public long InvoiceCounterValue { get; set; }
    public string PreviousInvoiceHashBase64 { get; set; } = string.Empty;
    public string UnsignedXml { get; set; } = string.Empty;
    public string SignedXml { get; set; } = string.Empty;
    public string InvoiceHashBase64 { get; set; } = string.Empty;
    public string SignatureBase64 { get; set; } = string.Empty;
    public string QrCodeBase64 { get; set; } = string.Empty;
    public string InvoiceBase64 { get; set; } = string.Empty;
    public decimal TaxExclusiveAmount { get; set; }
    public decimal TaxInclusiveAmount { get; set; }
    public decimal TotalTaxAmount { get; set; }
}
