using System.ComponentModel.DataAnnotations;

namespace ZatcaFatooraDotNet8Sample.Models.Requests;

public sealed class ComplianceCsidRequest
{
    [Required]
    public string Otp { get; set; } = string.Empty;

    public string? CsrBase64 { get; set; }
}
