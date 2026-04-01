using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace ZatcaFatooraDotNet8Sample.Models.Requests;

public sealed class GenerateCsrRequest : IValidatableObject
{
    [Required]
    public string CommonName { get; set; } = string.Empty;

    [Required]
    public string OrganizationName { get; set; } = string.Empty;

    [Required]
    public string OrganizationalUnitName { get; set; } = string.Empty;

    [Required]
    [StringLength(2, MinimumLength = 2)]
    public string CountryCode { get; set; } = "SA";

    [Required]
    public string SerialNumber { get; set; } = string.Empty;

    [Required]
    public string OrganizationIdentifier { get; set; } = string.Empty;

    [Required]
    public string InvoiceType { get; set; } = string.Empty;

    [Required]
    public string LocationAddress { get; set; } = string.Empty;

    [Required]
    public string IndustryBusinessCategory { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.Equals(CountryCode?.Trim(), "SA", StringComparison.OrdinalIgnoreCase))
        {
            yield return new ValidationResult(
                "CountryCode must be SA.",
                new[] { nameof(CountryCode) });
        }

        if (!Regex.IsMatch(OrganizationIdentifier ?? string.Empty, @"^3\d{13}3$"))
        {
            yield return new ValidationResult(
                "OrganizationIdentifier must be 15 digits, start with 3, and end with 3.",
                new[] { nameof(OrganizationIdentifier) });
        }

        if (!Regex.IsMatch(SerialNumber ?? string.Empty, @"^1-.+\|2-.+\|3-.+$"))
        {
            yield return new ValidationResult(
                "SerialNumber must match the format 1-...|2-...|3-....",
                new[] { nameof(SerialNumber) });
        }

        if (!Regex.IsMatch(InvoiceType ?? string.Empty, @"^[01]{4}$") || InvoiceType == "0000")
        {
            yield return new ValidationResult(
                "InvoiceType must be a 4-digit binary value like 1000, 0100, or 1100 and cannot be 0000.",
                new[] { nameof(InvoiceType) });
        }

        if (Regex.IsMatch(OrganizationIdentifier ?? string.Empty, @"^3\d{13}3$"))
        {
            var isVatGroup = OrganizationIdentifier[10] == '1';

            if (isVatGroup && !Regex.IsMatch(OrganizationalUnitName ?? string.Empty, @"^\d{10}$"))
            {
                yield return new ValidationResult(
                    "For VAT groups, OrganizationalUnitName must be the 10-digit TIN of the member being onboarded.",
                    new[] { nameof(OrganizationalUnitName) });
            }
        }
    }
}