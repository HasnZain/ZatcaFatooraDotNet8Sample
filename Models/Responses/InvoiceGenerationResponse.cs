namespace ZatcaFatooraDotNet8Sample.Models.Responses;

public sealed class InvoiceGenerationResponse
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public long InvoiceCounterValue { get; set; }
    public string PreviousInvoiceHashBase64 { get; set; } = string.Empty;
    public string SignatureBase64 { get; set; } = string.Empty;
    public string QrCodeBase64 { get; set; } = string.Empty;
    public string SignedXml { get; set; } = string.Empty;
    public decimal TaxExclusiveAmount { get; set; }
    public decimal TaxInclusiveAmount { get; set; }
    public decimal TotalTaxAmount { get; set; }
    public string Uuid { get; set; } = string.Empty;
    public string InvoiceHash { get; set; } = string.Empty;
    public string InvoiceBase64 { get; set; } = string.Empty;

}
