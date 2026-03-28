using System.ComponentModel.DataAnnotations;

namespace ZatcaFatooraDotNet8Sample.Models.Requests;

public sealed class SubmitInvoiceRequest
{
    [Required]
    public string InvoiceHash { get; set; } = string.Empty;

    [Required]
    public string Uuid { get; set; } = string.Empty;

    [Required]
    public string InvoiceBase64 { get; set; } = string.Empty;
}
