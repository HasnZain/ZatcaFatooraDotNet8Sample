using System.ComponentModel.DataAnnotations;
using ZatcaFatooraDotNet8Sample.Models.Domain;

namespace ZatcaFatooraDotNet8Sample.Models.Requests;

public sealed class GenerateInvoiceRequest
{
    [Required]
    public string InvoiceNumber { get; set; } = string.Empty;

    [Required]
    public string InvoiceCategory { get; set; } = "Standard"; // Standard or Simplified

    public string InvoiceTypeCode { get; set; } = "388";
    public string? InvoiceTypeName { get; set; }
    public DateTimeOffset? IssueDateTime { get; set; }
    public string CurrencyCode { get; set; } = "SAR";
    public string TaxCurrencyCode { get; set; } = "SAR";
    public string PaymentMeansCode { get; set; } = "10";
    public string? Notes { get; set; }
    public string? PreviousInvoiceHashBase64 { get; set; }
    public long? InvoiceCounterValue { get; set; }

    [Required]
    public PartyInfo Seller { get; set; } = new();

    public PartyInfo? Buyer { get; set; }

    [MinLength(1)]
    public List<InvoiceLineItem> Lines { get; set; } = new();
}
